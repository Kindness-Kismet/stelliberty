using Stelliberty.Application.Proxies;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Infrastructure.Tray;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;
using Stelliberty.Infrastructure.Core;

namespace Stelliberty.Tray;

internal sealed class TrayRequestRouter : IDisposable
{
    private readonly TrayLifetime _lifetime;
    private readonly UiSessionManager _uiSessions;
    private readonly ITrayCoreRuntime _coreRuntime;
    private readonly CoreLogJournal _coreLogs;
    private readonly ITrayRuntimeMonitor _runtimeMonitor;
    private readonly ISystemProxyController _systemProxy;
    private readonly ITrayHotkeyRuntime _hotkeys;
    private readonly TrayBackgroundTasks _backgroundTasks;
    private readonly IProxyDelayResultSink _delayResults;
    private readonly SystemPowerRecoveryService _powerRecovery;
    private readonly SystemPowerMonitor _powerMonitor;
    private readonly ConcurrentDictionary<Guid, byte> _handshakes = new();
    private readonly ConcurrentDictionary<Guid, TrayIpcConnection> _connections = new();
    private readonly string _trayEpoch = Guid.NewGuid().ToString("N");
    private readonly long _startedAt = Stopwatch.GetTimestamp();

    public TrayRequestRouter(
        TrayLifetime lifetime,
        UiSessionManager uiSessions,
        ITrayCoreRuntime coreRuntime,
        CoreLogJournal coreLogs,
        ITrayRuntimeMonitor runtimeMonitor,
        ISystemProxyController systemProxy,
        ITrayHotkeyRuntime hotkeys,
        TrayBackgroundTasks backgroundTasks,
        IProxyDelayResultSink delayResults,
        SystemPowerRecoveryService powerRecovery,
        SystemPowerMonitor powerMonitor)
    {
        _lifetime = lifetime;
        _uiSessions = uiSessions;
        _coreRuntime = coreRuntime;
        _coreLogs = coreLogs;
        _runtimeMonitor = runtimeMonitor;
        _systemProxy = systemProxy;
        _hotkeys = hotkeys;
        _backgroundTasks = backgroundTasks;
        _delayResults = delayResults;
        _powerRecovery = powerRecovery;
        _powerMonitor = powerMonitor;
        _backgroundTasks.StateChanged += OnBackgroundChanged;
        _coreRuntime.StateChanged += OnCoreStateChanged;
        _coreRuntime.LogReceived += OnCoreLogReceived;

        _runtimeMonitor.Sampled += OnRuntimeSampled;

        _systemProxy.StatusChanged += OnSystemProxyChanged;
    }

    public async Task<TrayIpcResult> HandleAsync(
        TrayIpcConnection connection,
        TrayIpcRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.Method != TrayProtocol.HelloMethod && !_handshakes.ContainsKey(connection.Id))
            {
                return TrayIpcResult.Error("tray.handshake_required", "Call tray.hello before other methods.");
            }

            return request.Method switch
            {
                TrayProtocol.ProxyDelayScopeMethod => TrayIpcResult.Success(
                    await _delayResults.CaptureScopeAsync(cancellationToken).ConfigureAwait(false)),
                TrayProtocol.ProxyDelayPublishMethod => await HandleProxyDelayAsync(request, cancellationToken).ConfigureAwait(false),
#if DEBUG
                TrayProtocol.PowerDebugMethod => HandlePowerDebug(request),
#endif
                TrayProtocol.HelloMethod => HandleHello(connection, request),
                TrayProtocol.HealthMethod => await HandleHealthAsync(cancellationToken).ConfigureAwait(false),
                TrayProtocol.BackgroundStatusMethod => TrayIpcResult.Success(_backgroundTasks.Status),
                TrayProtocol.CoreEnsureStartedMethod => TrayIpcResult.Success(
                    await _coreRuntime.EnsureStartedAsync(cancellationToken).ConfigureAwait(false)),
                TrayProtocol.CoreStopMethod => TrayIpcResult.Success(
                    await _coreRuntime.StopAsync(cancellationToken).ConfigureAwait(false)),
                TrayProtocol.CoreSnapshotMethod => TrayIpcResult.Success(_coreRuntime.CurrentStatus),
                TrayProtocol.CoreApplyConfigMethod => TrayIpcResult.Success(
                    await _coreRuntime.ApplyConfigAsync(
                        request.DeserializeParameters<CoreApplyConfigRequest>(),
                        cancellationToken).ConfigureAwait(false)),
                TrayProtocol.CoreRestartMethod => await HandleCoreRestartAsync(cancellationToken).ConfigureAwait(false),
                TrayProtocol.CoreLogsMethod => TrayIpcResult.Success(
                    _coreLogs.ReadAfter(
                        request.DeserializeParameters<TrayCoreLogsRequest>().AfterSequence)),
                TrayProtocol.RuntimeSnapshotMethod => TrayIpcResult.Success(_runtimeMonitor.GetSnapshot()),
                TrayProtocol.RuntimeResetTrafficMethod => await HandleRuntimeResetAsync(cancellationToken).ConfigureAwait(false),
                TrayProtocol.SystemProxyStatusMethod => TrayIpcResult.Success(
                    await _systemProxy.GetStatusAsync(cancellationToken).ConfigureAwait(false)),
                TrayProtocol.SystemProxySetEnabledMethod => await HandleSystemProxySetAsync(
                    request,
                    cancellationToken).ConfigureAwait(false),
                TrayProtocol.ServiceModeStatusMethod => TrayIpcResult.Success(
                    await _coreRuntime.GetServiceModeStatusAsync(cancellationToken).ConfigureAwait(false)),
                TrayProtocol.ServiceModeInstallMethod => TrayIpcResult.Success(
                    await _coreRuntime.InstallOrUpdateServiceModeAsync(cancellationToken).ConfigureAwait(false)),
                TrayProtocol.ServiceModeUninstallMethod => TrayIpcResult.Success(
                    await _coreRuntime.UninstallServiceModeAsync(cancellationToken).ConfigureAwait(false)),
                TrayProtocol.HotkeyApplyMethod => await HandleHotkeyApplyAsync(request, cancellationToken).ConfigureAwait(false),
                TrayProtocol.HotkeySetSuppressedMethod => await HandleHotkeySuppressionAsync(
                    connection,
                    request,
                    cancellationToken).ConfigureAwait(false),
#if DEBUG
                TrayProtocol.CopyTerminalProxyMethod => await HandleCopyTerminalProxyAsync(cancellationToken).ConfigureAwait(false),
                TrayProtocol.HotkeySimulateMethod => await HandleHotkeySimulationAsync(
                    request,
                    cancellationToken).ConfigureAwait(false),
#endif
                TrayProtocol.UiActivateMethod => TrayIpcResult.Success(
                    await _uiSessions.ActivateAsync(cancellationToken).ConfigureAwait(false)),
                TrayProtocol.UiRegisterMethod => TrayIpcResult.Success(
                    await RegisterUiAsync(connection, request, cancellationToken).ConfigureAwait(false)),
                TrayProtocol.UiUnregisterMethod => TrayIpcResult.Success(
                    await UnregisterUiAsync(connection, request, cancellationToken).ConfigureAwait(false)),
                TrayProtocol.ShutdownMethod => HandleShutdown(),
                _ => TrayIpcResult.Error("tray.method_not_found", $"Unknown Tray method: {request.Method}"),
            };
        }
        catch (UiSessionException exception)
        {
            return TrayIpcResult.Error(exception.Code, exception.Message);
        }
        catch (JsonException exception)
        {
            return TrayIpcResult.Error("tray.invalid_params", exception.Message);
        }
        catch (IpcRemoteException exception)
        {
            return TrayIpcResult.Error(exception.Code, exception.Message);
        }
        catch (InvalidOperationException exception) when (
            request.Method.StartsWith("system_proxy.", StringComparison.Ordinal))
        {
            return TrayIpcResult.Error("system_proxy.operation_failed", exception.Message);
        }
        catch (InvalidOperationException exception) when (
            request.Method.StartsWith("service_mode.", StringComparison.Ordinal))
        {
            return TrayIpcResult.Error("service_mode.operation_failed", exception.Message);
        }
        catch (InvalidOperationException exception) when (
            request.Method.StartsWith("hotkey.", StringComparison.Ordinal))
        {
            return TrayIpcResult.Error("hotkey.operation_failed", exception.Message);
        }
        catch (InvalidOperationException exception) when (
            request.Method.StartsWith("core.", StringComparison.Ordinal)
            || request.Method.StartsWith("runtime.", StringComparison.Ordinal))
        {
            return TrayIpcResult.Error("core.operation_failed", exception.Message);
        }
        catch (Exception exception) when (
            request.Method == TrayProtocol.UiActivateMethod
            && exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            AppLogger.Error(exception, "Desktop UI launch failed");
            return TrayIpcResult.Error("ui.launch_failed", "Desktop UI could not be started.");
        }
    }

    public async Task OnConnectionClosedAsync(Guid connectionId)
    {
        _handshakes.TryRemove(connectionId, out _);
        _connections.TryRemove(connectionId, out _);
        await _hotkeys.ReleaseSuppressionAsync(connectionId).ConfigureAwait(false);
        await _uiSessions.OnConnectionClosedAsync(connectionId).ConfigureAwait(false);
    }

    private TrayIpcResult HandleHello(TrayIpcConnection connection, TrayIpcRequest request)
    {
        var hello = request.DeserializeParameters<TrayHelloRequest>();
        var error = ValidateClient(hello.ProtocolVersion, hello.AppVersion);
        if (error is not null)
        {
            return error;
        }

        _handshakes[connection.Id] = 0;
        _connections[connection.Id] = connection;
        return TrayIpcResult.Success(CreateHello());
    }

    private async Task<TrayIpcResult> HandleHealthAsync(CancellationToken cancellationToken)
    {
        var ui = await _uiSessions.GetStateAsync(cancellationToken).ConfigureAwait(false);
        return TrayIpcResult.Success(new TrayHealth(
            Environment.ProcessId,
            _trayEpoch,
            (long)Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds,
            ui.UiPid,
            ui.IsLaunchPending,
            _coreRuntime.CurrentStatus,
            _coreLogs.LatestSequence,
            _runtimeMonitor.GetSnapshot().SampledAt,
            await _systemProxy.GetStatusAsync(cancellationToken).ConfigureAwait(false),
            _powerRecovery.Snapshot,
            _powerMonitor.Status));
    }

    private async Task<UiRegisterResult> RegisterUiAsync(
        TrayIpcConnection connection,
        TrayIpcRequest request,
        CancellationToken cancellationToken)
    {
        var register = request.DeserializeParameters<UiRegisterRequest>();
        var error = ValidateClient(register.ProtocolVersion, register.AppVersion);
        if (error is not null)
        {
            throw new UiSessionException(error.ErrorCode!, error.ErrorMessage!);
        }

        return await _uiSessions.RegisterAsync(
            register.SessionToken,
            register.UiPid,
            new IpcUiSessionConnection(connection),
            cancellationToken).ConfigureAwait(false);
    }

    private Task<UiUnregisterResult> UnregisterUiAsync(
        TrayIpcConnection connection,
        TrayIpcRequest request,
        CancellationToken cancellationToken)
    {
        var unregister = request.DeserializeParameters<UiUnregisterRequest>();
        return _uiSessions.UnregisterAsync(unregister.SessionId, connection.Id, cancellationToken);
    }

    private TrayIpcResult HandleShutdown()
    {
        // 先让响应写回客户端，再结束宿主监听。
        _ = Task.Run(async () =>
        {
            await Task.Delay(100).ConfigureAwait(false);
            _lifetime.RequestStop();
        });
        return TrayIpcResult.Success(new { accepted = true });
    }

    private TrayIpcResult? ValidateClient(int protocolVersion, string appVersion)
    {
        if (protocolVersion != TrayProtocol.Version)
        {
            return TrayIpcResult.Error(
                "tray.protocol_mismatch",
                $"Expected protocol {TrayProtocol.Version}, received {protocolVersion}.");
        }

        return appVersion == AppMetadata.Version
            ? null
            : TrayIpcResult.Error(
                "tray.version_mismatch",
                $"Expected app version {AppMetadata.Version}, received {appVersion}.");
    }

    private TrayHello CreateHello() => new(
        TrayProtocol.Version,
        AppMetadata.Version,
        Environment.ProcessId,
        _trayEpoch,
        ["ui_session", "background_tray", "core_runtime", "core_log_journal", "runtime_traffic", "service_mode", "system_proxy", "global_hotkeys"],
        _coreRuntime.CurrentStatus.CoreGeneration);

    private async Task<TrayIpcResult> HandleProxyDelayAsync(TrayIpcRequest request, CancellationToken cancellationToken)
    {
        var publication = request.DeserializeParameters<ProxyDelayPublication>();
        if (string.IsNullOrWhiteSpace(publication.Scope) || string.IsNullOrWhiteSpace(publication.ProxyName)
            || publication.Delay < -1)
        {
            return TrayIpcResult.Error("tray.invalid_params", "Invalid proxy delay publication.");
        }
        await _delayResults.PublishAsync(publication, cancellationToken).ConfigureAwait(false);
        return TrayIpcResult.Success(new { });
    }

#if DEBUG
    private TrayIpcResult HandlePowerDebug(TrayIpcRequest request)
    {
        var kind = request.DeserializeParameters<TrayPowerDebugRequest>().Event;
        if (!Enum.IsDefined(kind)) return TrayIpcResult.Error("tray.invalid_params", "Unknown power event.");
        // 返回接收状态，恢复任务的最终结果由健康查询观察。
        _ = _powerRecovery.HandleAsync(kind, "debug.simulated");
        return TrayIpcResult.Success(_powerRecovery.Snapshot);
    }
#endif

    private async Task<TrayIpcResult> HandleCoreRestartAsync(CancellationToken cancellationToken)
    {
        var runtime = _coreRuntime;
        await runtime.RestartAsync(cancellationToken).ConfigureAwait(false);
        return TrayIpcResult.Success(runtime.CurrentStatus);
    }

    private async Task<TrayIpcResult> HandleRuntimeResetAsync(CancellationToken cancellationToken)
    {
        var monitor = _runtimeMonitor;
        await monitor.ResetTrafficAsync(cancellationToken).ConfigureAwait(false);
        return TrayIpcResult.Success(monitor.GetSnapshot());
    }

#if DEBUG
    private async Task<TrayIpcResult> HandleCopyTerminalProxyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _hotkeys.CopyTerminalProxyAsync(cancellationToken).ConfigureAwait(false);
            return TrayIpcResult.Success(new { });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return TrayIpcResult.Error("tray.copy_failed", exception.Message);
        }
    }
#endif

    private async Task<TrayIpcResult> HandleHotkeyApplyAsync(
        TrayIpcRequest request,
        CancellationToken cancellationToken)
    {
        var parameters = request.DeserializeParameters<TrayHotkeyApplyRequest>();
        var result = await _hotkeys
            .ApplyAsync(parameters.Action, parameters.Gesture, cancellationToken)
            .ConfigureAwait(false);
        return TrayIpcResult.Success(result);
    }

    private async Task<TrayIpcResult> HandleHotkeySuppressionAsync(
        TrayIpcConnection connection,
        TrayIpcRequest request,
        CancellationToken cancellationToken)
    {
        var parameters = request.DeserializeParameters<TrayHotkeySuppressionRequest>();
        await _hotkeys
            .SetSuppressedAsync(connection.Id, parameters.IsSuppressed, cancellationToken)
            .ConfigureAwait(false);
        return TrayIpcResult.Success(new { applied = true });
    }

#if DEBUG
    private async Task<TrayIpcResult> HandleHotkeySimulationAsync(
        TrayIpcRequest request,
        CancellationToken cancellationToken)
    {
        var parameters = request.DeserializeParameters<TrayHotkeySimulationRequest>();
        var activated = await _hotkeys
            .SimulateActivationAsync(parameters.Action, cancellationToken)
            .ConfigureAwait(false);
        return TrayIpcResult.Success(activated);
    }
#endif

    private async Task<TrayIpcResult> HandleSystemProxySetAsync(
        TrayIpcRequest request,
        CancellationToken cancellationToken)
    {
        var parameters = request.DeserializeParameters<TraySystemProxySetRequest>();
        if (parameters.IsEnabled && parameters.Request is null)
        {
            return TrayIpcResult.Error("tray.invalid_params", "Enabling the system proxy requires settings.");
        }

        var result = await _systemProxy.SetEnabledAsync(
            parameters.IsEnabled,
            parameters.Request,
            cancellationToken).ConfigureAwait(false);
        return TrayIpcResult.Success(result);
    }

    private void OnCoreStateChanged(object? sender, TrayCoreStatus status) =>
        Broadcast(TrayProtocol.CoreStateChangedEvent, status);

    private void OnCoreLogReceived(object? sender, TrayCoreLogEntry entry) =>
        Broadcast(TrayProtocol.CoreLogEntryEvent, entry);

    private void OnRuntimeSampled(object? sender, CoreRuntimeSample sample) =>
        Broadcast(TrayProtocol.RuntimeSampledEvent, sample);

    private void OnSystemProxyChanged(object? sender, SystemProxyStatus status) =>
        Broadcast(TrayProtocol.SystemProxyChangedEvent, status);

    private void OnBackgroundChanged(object? sender, BackgroundTaskStatus status) =>
        Broadcast(TrayProtocol.BackgroundChangedEvent, status);

    private void Broadcast(string eventName, object data)
    {
        foreach (var connection in _connections.Values)
        {
            _ = SendEventAsync(connection, eventName, data);
        }
    }

    private async Task SendEventAsync(TrayIpcConnection connection, string eventName, object data)
    {
        try
        {
            await connection.SendEventAsync(eventName, data, _lifetime.StoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            _connections.TryRemove(connection.Id, out _);
        }
    }

    public void Dispose()
    {
        _backgroundTasks.StateChanged -= OnBackgroundChanged;
        _coreRuntime.StateChanged -= OnCoreStateChanged;
        _coreRuntime.LogReceived -= OnCoreLogReceived;

        _runtimeMonitor.Sampled -= OnRuntimeSampled;

        _systemProxy.StatusChanged -= OnSystemProxyChanged;

        _connections.Clear();
        _handshakes.Clear();
    }

    private sealed class IpcUiSessionConnection(TrayIpcConnection connection) : IUiSessionConnection
    {
        public Guid Id => connection.Id;

        public Task RequestActivationAsync(CancellationToken cancellationToken) =>
            connection.SendEventAsync(TrayProtocol.UiActivationEvent, new { }, cancellationToken);

        public Task RequestToggleAsync(CancellationToken cancellationToken) =>
            connection.SendEventAsync(TrayProtocol.UiToggleEvent, new { }, cancellationToken);
    }
}
