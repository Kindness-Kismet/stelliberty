using Stelliberty.Application.Proxies;
using System.Text.Json;
using Stelliberty.Infrastructure.Tray;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;
using Stelliberty.Infrastructure.Core;

namespace Stelliberty.Infrastructure.Tray;

public sealed class TrayIpcClient : IDisposable, IAsyncDisposable
{
    private readonly JsonRpcPipeClient _client;

    public TrayIpcClient(string? endpoint = null)
    {
        _client = new JsonRpcPipeClient(endpoint ?? TrayEndpoint.Current);
        _client.EventReceived += OnEventReceived;
        _client.Disconnected += OnDisconnected;
    }

    public event EventHandler? ActivationRequested;

    public event EventHandler? ToggleRequested;

    public event EventHandler<TrayCoreStatus>? CoreStateChanged;

    public event EventHandler<TrayCoreLogEntry>? CoreLogReceived;

    public event EventHandler<CoreRuntimeSample>? RuntimeSampled;

    public event EventHandler<SystemProxyStatus>? SystemProxyChanged;

    public event EventHandler? Disconnected;
    public event EventHandler<BackgroundTaskStatus>? BackgroundChanged;

    public Task ConnectAsync(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);

    public async Task<TrayHello> HelloAsync(int processId, CancellationToken cancellationToken)
    {
        var request = new TrayHelloRequest(TrayProtocol.Version, AppMetadata.Version, processId);
        var hello = await RequestAsync<TrayHello>(TrayProtocol.HelloMethod, request, cancellationToken).ConfigureAwait(false);
        if (hello.ProtocolVersion != TrayProtocol.Version)
        {
            throw new TrayProtocolMismatchException(TrayProtocol.Version, hello.ProtocolVersion);
        }

        return hello;
    }

    public Task<TrayHealth> GetHealthAsync(CancellationToken cancellationToken) =>
        RequestAsync<TrayHealth>(TrayProtocol.HealthMethod, new { }, cancellationToken);

    public Task<BackgroundTaskStatus> GetBackgroundStatusAsync(CancellationToken cancellationToken) =>
        RequestAsync<BackgroundTaskStatus>(TrayProtocol.BackgroundStatusMethod, new { }, cancellationToken);

    public Task<TrayCoreOperationResult> EnsureCoreStartedAsync(CancellationToken cancellationToken) =>
        RequestAsync<TrayCoreOperationResult>(TrayProtocol.CoreEnsureStartedMethod, new { }, cancellationToken);

    public Task<TrayCoreOperationResult> StopCoreAsync(CancellationToken cancellationToken) =>
        RequestAsync<TrayCoreOperationResult>(TrayProtocol.CoreStopMethod, new { }, cancellationToken);

    public Task<TrayCoreStatus> GetCoreStatusAsync(CancellationToken cancellationToken) =>
        RequestAsync<TrayCoreStatus>(TrayProtocol.CoreSnapshotMethod, new { }, cancellationToken);

    public Task<CoreApplyConfigResult> ApplyCoreConfigAsync(
        CoreApplyConfigRequest request,
        CancellationToken cancellationToken) =>
        RequestAsync<CoreApplyConfigResult>(TrayProtocol.CoreApplyConfigMethod, request, cancellationToken);

    public Task<TrayCoreStatus> RestartCoreAsync(CancellationToken cancellationToken) =>
        RequestAsync<TrayCoreStatus>(TrayProtocol.CoreRestartMethod, new { }, cancellationToken);

    public Task<TrayCoreLogBatch> GetCoreLogsAsync(long afterSequence, CancellationToken cancellationToken) =>
        RequestAsync<TrayCoreLogBatch>(
            TrayProtocol.CoreLogsMethod,
            new TrayCoreLogsRequest(afterSequence),
            cancellationToken);

    public Task<CoreRuntimeSnapshot> GetRuntimeSnapshotAsync(CancellationToken cancellationToken) =>
        RequestAsync<CoreRuntimeSnapshot>(TrayProtocol.RuntimeSnapshotMethod, new { }, cancellationToken);

    public Task<CoreRuntimeSnapshot> ResetRuntimeTrafficAsync(CancellationToken cancellationToken) =>
        RequestAsync<CoreRuntimeSnapshot>(TrayProtocol.RuntimeResetTrafficMethod, new { }, cancellationToken);

    public Task<SystemProxyStatus> GetSystemProxyStatusAsync(CancellationToken cancellationToken) =>
        RequestAsync<SystemProxyStatus>(TrayProtocol.SystemProxyStatusMethod, new { }, cancellationToken);

    public Task<SystemProxyApplyResult> SetSystemProxyEnabledAsync(
        bool isEnabled,
        SystemProxyApplicationRequest? request,
        CancellationToken cancellationToken) =>
        RequestAsync<SystemProxyApplyResult>(
            TrayProtocol.SystemProxySetEnabledMethod,
            new TraySystemProxySetRequest(isEnabled, request),
            cancellationToken);

    public Task<ServiceModeStatus> GetServiceModeStatusAsync(CancellationToken cancellationToken) =>
        RequestAsync<ServiceModeStatus>(TrayProtocol.ServiceModeStatusMethod, new { }, cancellationToken);

    public Task<ServiceModeOperationResult> InstallOrUpdateServiceModeAsync(CancellationToken cancellationToken) =>
        RequestAsync<ServiceModeOperationResult>(TrayProtocol.ServiceModeInstallMethod, new { }, cancellationToken);

    public Task<ServiceModeOperationResult> UninstallServiceModeAsync(CancellationToken cancellationToken) =>
        RequestAsync<ServiceModeOperationResult>(TrayProtocol.ServiceModeUninstallMethod, new { }, cancellationToken);

    public Task<GlobalHotkeyApplyResult> ApplyGlobalHotkeyAsync(
        GlobalHotkeyAction action,
        string gesture,
        CancellationToken cancellationToken) =>
        RequestAsync<GlobalHotkeyApplyResult>(
            TrayProtocol.HotkeyApplyMethod,
            new TrayHotkeyApplyRequest(action, gesture),
            cancellationToken);

    public Task SetGlobalHotkeysSuppressedAsync(
        bool isSuppressed,
        CancellationToken cancellationToken) =>
        RequestAsync<JsonElement>(
            TrayProtocol.HotkeySetSuppressedMethod,
            new TrayHotkeySuppressionRequest(isSuppressed),
            cancellationToken);

#if DEBUG
    public Task CopyTerminalProxyAsync(CancellationToken cancellationToken) =>
        RequestAsync<JsonElement>(TrayProtocol.CopyTerminalProxyMethod, new { }, cancellationToken);

    public Task<bool> SimulateGlobalHotkeyAsync(
        GlobalHotkeyAction action,
        CancellationToken cancellationToken) =>
        RequestAsync<bool>(
            TrayProtocol.HotkeySimulateMethod,
            new TrayHotkeySimulationRequest(action),
            cancellationToken);
#endif

    public Task<UiActivateResult> ActivateUiAsync(int launcherPid, CancellationToken cancellationToken) =>
        RequestAsync<UiActivateResult>(
            TrayProtocol.UiActivateMethod,
            new UiActivateRequest(launcherPid),
            cancellationToken);

    public Task<UiRegisterResult> RegisterUiAsync(string sessionToken, int uiPid, CancellationToken cancellationToken) =>
        RequestAsync<UiRegisterResult>(
            TrayProtocol.UiRegisterMethod,
            new UiRegisterRequest(TrayProtocol.Version, AppMetadata.Version, uiPid, sessionToken),
            cancellationToken);

    public Task<UiUnregisterResult> UnregisterUiAsync(string sessionId, CancellationToken cancellationToken) =>
        RequestAsync<UiUnregisterResult>(
            TrayProtocol.UiUnregisterMethod,
            new UiUnregisterRequest(sessionId),
            cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken) =>
        RequestAsync<JsonElement>(TrayProtocol.ShutdownMethod, new { }, cancellationToken);

    private async Task<T> RequestAsync<T>(string method, object parameters, CancellationToken cancellationToken)
    {
        var parameterElement = JsonSerializer.SerializeToElement(parameters, TrayJson.Options);
        var result = await _client.RequestAsync(method, parameterElement, cancellationToken).ConfigureAwait(false);
        return result.Deserialize<T>(TrayJson.Options)
            ?? throw new JsonException($"Tray response for {method} is empty.");
    }

    private void OnEventReceived(object? sender, EventNotification notification)
    {
        switch (notification.Event)
        {
            case TrayProtocol.BackgroundChangedEvent:
                BackgroundChanged?.Invoke(this, DeserializeEvent<BackgroundTaskStatus>(notification));
                break;
            case TrayProtocol.UiActivationEvent:
                ActivationRequested?.Invoke(this, EventArgs.Empty);
                break;
            case TrayProtocol.UiToggleEvent:
                ToggleRequested?.Invoke(this, EventArgs.Empty);
                break;
            case TrayProtocol.CoreStateChangedEvent:
                CoreStateChanged?.Invoke(this, DeserializeEvent<TrayCoreStatus>(notification));
                break;
            case TrayProtocol.CoreLogEntryEvent:
                CoreLogReceived?.Invoke(this, DeserializeEvent<TrayCoreLogEntry>(notification));
                break;
            case TrayProtocol.RuntimeSampledEvent:
                RuntimeSampled?.Invoke(this, DeserializeEvent<CoreRuntimeSample>(notification));
                break;
            case TrayProtocol.SystemProxyChangedEvent:
                SystemProxyChanged?.Invoke(this, DeserializeEvent<SystemProxyStatus>(notification));
                break;
        }
    }

    private static T DeserializeEvent<T>(EventNotification notification)
    {
        return notification.Data.Deserialize<T>(TrayJson.Options)
            ?? throw new JsonException($"Tray event {notification.Event} is empty.");
    }

    private void OnDisconnected(object? sender, EventArgs args) => Disconnected?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _client.EventReceived -= OnEventReceived;
        _client.Disconnected -= OnDisconnected;
        _client.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _client.EventReceived -= OnEventReceived;
        _client.Disconnected -= OnDisconnected;
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}

public sealed class TrayGlobalHotkeyService : IGlobalHotkeyService
{
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly TrayIpcClient _client = new();
    private volatile bool _isConnected;
    private bool _isDisposed;

    public TrayGlobalHotkeyService()
    {
        _client.Disconnected += OnDisconnected;
    }

    public async Task<GlobalHotkeyApplyResult> ApplyAsync(
        GlobalHotkeyAction action,
        string gesture,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        return await _client.ApplyGlobalHotkeyAsync(action, gesture, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetActivationSuppressedAsync(
        bool isSuppressed,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        await _client.SetGlobalHotkeysSuppressedAsync(isSuppressed, cancellationToken).ConfigureAwait(false);
    }

#if DEBUG
    public async Task<bool> SimulateActivationAsync(
        GlobalHotkeyAction action,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        return await _client.SimulateGlobalHotkeyAsync(action, cancellationToken).ConfigureAwait(false);
    }
#endif

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isConnected)
        {
            return;
        }

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isConnected)
            {
                return;
            }

            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await _client.HelloAsync(Environment.ProcessId, cancellationToken).ConfigureAwait(false);
            _isConnected = true;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private void OnDisconnected(object? sender, EventArgs args) => _isConnected = false;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _client.Disconnected -= OnDisconnected;
        _client.Dispose();
        _connectGate.Dispose();
    }
}

public sealed class TrayProtocolMismatchException(int expected, int actual)
    : Exception($"Tray protocol mismatch: expected {expected}, actual {actual}.")
{
    public int Expected { get; } = expected;

    public int Actual { get; } = actual;
}
