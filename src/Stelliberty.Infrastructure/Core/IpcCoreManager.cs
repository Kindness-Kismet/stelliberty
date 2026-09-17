using System.Diagnostics;
using System.Text.Json;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.CoreLogs;
using Stelliberty.Domain.CoreLogs;
using Stelliberty.Application.Runtime;

namespace Stelliberty.Infrastructure.Core;

public sealed class IpcCoreManager : IReadyCoreManager, IDisposable, IAsyncDisposable
{
    private readonly JsonRpcPipeClient _client;
    private readonly CoreLogParser _coreLogParser = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private CoreSnapshot _last = new(CoreState.Unavailable, null, string.Empty, null);
    private bool _isConnected;
    private bool _isDisposed;
    // 核心重启后会短暂繁忙；重试覆盖连续应用配置。
    private const int ApplyConfigMaxAttempts = 20;
    private static readonly TimeSpan ApplyConfigBusyDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CoreReadyTimeout = TimeSpan.FromSeconds(12);

    public event EventHandler<CoreSnapshot>? StateChanged;

    public event EventHandler<CoreLogMessage>? CoreLogReceived;

    public IpcCoreManager(string pipeName)
    {
        _client = new JsonRpcPipeClient(pipeName);
        _client.EventReceived += OnEvent;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        _last = snapshot;
        StateChanged?.Invoke(this, snapshot);
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);
        await WaitForReadyAsync(expectedPid: null, "startup", cancellationToken).ConfigureAwait(false);
    }

    public async Task<CoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        using var empty = JsonDocument.Parse("{}");
        var result = await _client.RequestAsync("core.status", empty.RootElement, cancellationToken).ConfigureAwait(false);
        return ParseSnapshot(result);
    }

    public async Task<CoreApplyConfigResult> ApplyConfigAsync(CoreApplyConfigRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await ApplyConfigOnceAsync(request, cancellationToken).ConfigureAwait(false);
                if (result.Mode == CoreApplyMode.Restart)
                {
                    await WaitForReadyAsync(result.Pid, "config apply", cancellationToken).ConfigureAwait(false);
                }

                return result;
            }
            catch (IpcRemoteException exception) when (exception.Code == "core.busy" && attempt < ApplyConfigMaxAttempts)
            {
                await Task.Delay(ApplyConfigBusyDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<CoreApplyConfigResult> ApplyConfigOnceAsync(CoreApplyConfigRequest request, CancellationToken cancellationToken)
    {
        var paramJson = JsonSerializer.SerializeToElement(new
        {
            runtime_yaml_content = request.RuntimeYamlContent,
            subscription_id = request.SubscriptionId,
        });
        var result = await _client.RequestAsync("core.apply_config", paramJson, cancellationToken).ConfigureAwait(false);
        var mode = result.TryGetProperty("mode", out var modeProp) && modeProp.ValueKind == JsonValueKind.String
            ? modeProp.GetString() switch
            {
                "reload" => CoreApplyMode.Reload,
                "restart" => CoreApplyMode.Restart,
                var value => throw new InvalidOperationException($"Core apply config returned an unknown mode: {value}")
            }
            : throw new InvalidOperationException("Core apply config did not return a mode.");
        var pid = ParsePid(result)
            ?? throw new InvalidOperationException("Core apply config did not return a valid process pid.");
        return new CoreApplyConfigResult(mode, pid);
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        using var empty = JsonDocument.Parse("{}");
        var result = await _client.RequestAsync("core.restart", empty.RootElement, cancellationToken).ConfigureAwait(false);
        var pid = ParsePid(result)
            ?? throw new InvalidOperationException("Core restart did not return a new process pid.");
        try
        {
            await WaitForReadyAsync(pid, "restart", cancellationToken).ConfigureAwait(false);
            AppLogger.Info($"Core restart ready: pid={pid} elapsed={stopwatch.Elapsed.TotalMilliseconds:0}ms");
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Core restart confirmation failed: pid={pid} elapsed={stopwatch.Elapsed.TotalMilliseconds:0}ms {exception.Message}");
            throw;
        }
    }

    private async Task WaitForReadyAsync(
        int? expectedPid,
        string operation,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (IsReady(snapshot, expectedPid))
        {
            return;
        }

        ThrowIfReadyFailed(snapshot, operation);

        var completion = new TaskCompletionSource<CoreSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStateChanged(object? sender, CoreSnapshot state)
        {
            if (IsReady(state, expectedPid) || state.State == CoreState.Crashed)
            {
                completion.TrySetResult(state);
            }
        }

        StateChanged += OnStateChanged;
        try
        {
            // 订阅后立即检查一次，覆盖事件早于响应的竞态。
            snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (IsReady(snapshot, expectedPid))
            {
                return;
            }

            ThrowIfReadyFailed(snapshot, operation);

            var timeoutTask = Task.Delay(CoreReadyTimeout, cancellationToken);
            var completed = await Task.WhenAny(completion.Task, timeoutTask).ConfigureAwait(false);
            if (completed == timeoutTask)
            {
                await timeoutTask.ConfigureAwait(false);
                var target = expectedPid is null ? "core" : $"process {expectedPid}";
                throw new TimeoutException($"Core {operation} timed out: {target} did not become running within {CoreReadyTimeout.TotalSeconds:N0} seconds.");
            }

            snapshot = await completion.Task.ConfigureAwait(false);
            if (IsReady(snapshot, expectedPid))
            {
                return;
            }

            ThrowIfReadyFailed(snapshot, operation);
        }
        finally
        {
            StateChanged -= OnStateChanged;
        }
    }

    private static bool IsReady(CoreSnapshot snapshot, int? expectedPid)
    {
        return snapshot.State == CoreState.Running
            && snapshot.Pid is not null
            && (expectedPid is null || snapshot.Pid == expectedPid);
    }

    private static void ThrowIfReadyFailed(CoreSnapshot snapshot, string operation)
    {
        if (snapshot.State != CoreState.Crashed)
        {
            return;
        }

        var message = string.IsNullOrWhiteSpace(snapshot.LastError)
            ? $"Core entered crashed state during {operation}."
            : $"Core entered crashed state during {operation}: {snapshot.LastError}";
        throw new InvalidOperationException(message);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_isConnected)
        {
            return;
        }

        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isConnected)
            {
                return;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            await _client.ConnectAsync(timeout.Token).ConfigureAwait(false);
            _isConnected = true;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private void OnEvent(object? sender, EventNotification ev)
    {
        if (_isDisposed)
        {
            return;
        }

        if (ev.Event == "core_logs.entry")
        {
            PublishCoreLog(ev.Data);
            return;
        }

        if (ev.Event != "core.state_changed")
        {
            return;
        }
        // 事件帧不带 external_controller，所以保留上一次完整值。
        var snapshot = ParseSnapshotPartial(ev.Data, _last.ExternalController);
        _last = snapshot;
        StateChanged?.Invoke(this, snapshot);
    }

    private void PublishCoreLog(JsonElement data)
    {
        if (!data.TryGetProperty("line", out var lineProp)
            || lineProp.GetString() is not { Length: > 0 } line)
        {
            return;
        }

        foreach (var message in _coreLogParser.Parse(line, DateTimeOffset.Now))
        {
            CoreLogReceived?.Invoke(this, message);
        }
    }

    private static CoreSnapshot ParseSnapshot(JsonElement data)
    {
        var state = ParseState(data);
        var pid = ParsePid(data);
        var ext = data.TryGetProperty("external_controller", out var extProp) ? extProp.GetString() ?? string.Empty : string.Empty;
        var lastError = data.TryGetProperty("last_error", out var errProp) && errProp.ValueKind == JsonValueKind.String ? errProp.GetString() : null;
        return new CoreSnapshot(state, pid, ext, lastError);
    }

    private static CoreSnapshot ParseSnapshotPartial(JsonElement data, string fallbackExternalController)
    {
        var state = ParseState(data);
        var pid = ParsePid(data);
        var reason = data.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String ? reasonProp.GetString() : null;
        return new CoreSnapshot(state, pid, fallbackExternalController, reason);
    }

    private static int? ParsePid(JsonElement data)
    {
        return data.TryGetProperty("pid", out var pidProp)
            && pidProp.ValueKind == JsonValueKind.Number
            && pidProp.TryGetInt32(out var pid)
            && pid > 0
            ? pid
            : null;
    }

    private static CoreState ParseState(JsonElement data)
    {
        if (!data.TryGetProperty("state", out var stateProp))
        {
            return CoreState.Unavailable;
        }
        return stateProp.GetString() switch
        {
            "starting" => CoreState.Starting,
            "running" => CoreState.Running,
            "stopping" => CoreState.Stopping,
            "stopped" => CoreState.Stopped,
            "crashed" => CoreState.Crashed,
            _ => CoreState.Unavailable,
        };
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _client.EventReceived -= OnEvent;
        _client.Dispose();
        _connectLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _client.EventReceived -= OnEvent;
        await _client.DisposeAsync().ConfigureAwait(false);
        _connectLock.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
