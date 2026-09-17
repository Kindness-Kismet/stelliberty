using System.Diagnostics;
using System.Text.Json;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;
using Stelliberty.Domain.CoreLogs;
using Stelliberty.Infrastructure.Proxies;

namespace Stelliberty.Desktop.Services;

internal sealed class ServiceModeCoreManager : ICoreManager, IDisposable
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StatePollInterval = TimeSpan.FromSeconds(2);
    // 覆盖一次观测往返（800ms）留足余量；超时则放弃等待，不阻塞进程退出。
    private static readonly TimeSpan MonitorExitTimeout = TimeSpan.FromSeconds(3);

    private readonly DesktopServiceModeManager _serviceModeManager;
    private readonly HttpClient _coreClient;
    private readonly CorePipeLogStreamer _logStreamer;
    private readonly Func<string, string> _writeActiveConfig;
    private readonly Action<bool> _setCoreHostActive;
    private readonly object _monitorGate = new();
    private readonly object _snapshotGate = new();
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private CoreSnapshot? _lastSnapshot;
    private volatile bool _isShutdownSuspended;
    private bool _isDisposed;

    public ServiceModeCoreManager(
        DesktopServiceModeManager serviceModeManager,
        string corePipe,
        Func<string, string> writeActiveConfig,
        Action<bool> setCoreHostActive)
    {
        _serviceModeManager = serviceModeManager;
        _coreClient = PipeCoreProxyClient.CreatePipeHttpClient(corePipe);
        _logStreamer = new CorePipeLogStreamer(corePipe);
        _logStreamer.MessageReceived += OnLogMessageReceived;
        _writeActiveConfig = writeActiveConfig;
        _setCoreHostActive = setCoreHostActive;
    }

    public event EventHandler<CoreSnapshot>? StateChanged;

    public event EventHandler<CoreLogMessage>? CoreLogReceived;

    // 关机窗口内核心被系统终止会被服务记成崩溃，停掉轮询避免观测到这个瞬态。
    public void SetShutdownSuspension(bool isSuspended)
    {
        _isShutdownSuspended = isSuspended;
        if (isSuspended)
        {
            StopStatusMonitor();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopStatusMonitor(waitForExit: true);
        _logStreamer.MessageReceived -= OnLogMessageReceived;
        _logStreamer.Dispose();
        _coreClient.Dispose();
    }

    public async Task<CoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var observation = await _serviceModeManager.ObserveCoreAsync(cancellationToken).ConfigureAwait(false);
        return ApplyObservation(observation);
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var pid = await WaitReadyAsync(cancellationToken).ConfigureAwait(false);
        _logStreamer.Restart();
        StartStatusMonitor();
        PublishState(CoreState.Running, pid, null);
    }

    public async Task<CoreApplyConfigResult> ApplyConfigAsync(CoreApplyConfigRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var activePath = _writeActiveConfig(request.RuntimeYamlContent);

        // 核心未运行时服务侧没有可回填的启动参数，直接走完整启动
        CoreSnapshot? snapshot;
        lock (_snapshotGate)
        {
            snapshot = _lastSnapshot;
        }

        if (snapshot is not { State: CoreState.Running })
        {
            return await StartCoreHostAsync(activePath, cancellationToken).ConfigureAwait(false);
        }

        var result = await _serviceModeManager.ApplyCoreConfigAsync(activePath, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Message);
        }

        if (TryParseReloadOutcome(result.Message, out var reloadPid))
        {
            // 重载进程未变，日志流与状态监视器保持运行
            AppLogger.Info($"Service-mode core config reloaded: pid={reloadPid}");
            return new CoreApplyConfigResult(CoreApplyMode.Reload, reloadPid);
        }

        // 回执格式漂移时按重启处理并记录原文，避免静默误判
        AppLogger.Warning($"Unexpected core apply receipt: {result.Message}");
        return await WaitCoreHostReadyAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<CoreApplyConfigResult> StartCoreHostAsync(string activePath, CancellationToken cancellationToken)
    {
        var serviceRequest = new ServiceModeCoreHostRequest(
            CorePath: DesktopApplicationLayout.CoreBinaryPath,
            DataCoreDir: DesktopApplicationLayout.CoreDirectory,
            ConfigPath: activePath);
        var result = await _serviceModeManager.StartCoreHostAsync(serviceRequest, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Message);
        }

        return await WaitCoreHostReadyAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<CoreApplyConfigResult> WaitCoreHostReadyAsync(CancellationToken cancellationToken)
    {
        var pid = await WaitReadyAsync(cancellationToken).ConfigureAwait(false);
        _logStreamer.Restart();
        StartStatusMonitor();
        PublishState(CoreState.Running, pid, null);
        return new CoreApplyConfigResult(CoreApplyMode.Restart, pid);
    }

    // 服务侧回执格式固定：mode=reload pid=<n>
    private static bool TryParseReloadOutcome(string message, out int pid)
    {
        const string prefix = "mode=reload pid=";
        if (!message.StartsWith(prefix, StringComparison.Ordinal))
        {
            pid = 0;
            return false;
        }

        return int.TryParse(message.AsSpan(prefix.Length), out pid);
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var stopwatch = Stopwatch.StartNew();
        var result = await _serviceModeManager.RestartCoreHostAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Message);
        }

        var pid = await WaitReadyAsync(cancellationToken).ConfigureAwait(false);
        _logStreamer.Restart();
        StartStatusMonitor();
        PublishState(CoreState.Running, pid, null);
        AppLogger.Info($"Service-mode core restart is ready: pid={pid} elapsed={stopwatch.Elapsed.TotalMilliseconds:0}ms");
    }

    private void StartStatusMonitor()
    {
        if (_isDisposed || _isShutdownSuspended)
        {
            return;
        }

        lock (_monitorGate)
        {
            if (_isDisposed || _isShutdownSuspended || _monitorCancellation is not null)
            {
                return;
            }

            _monitorCancellation = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorStatusAsync(_monitorCancellation.Token));
        }
    }

    // waitForExit 仅用于释放路径：轮询任务持有 _coreClient，必须等它退出后才能释放。
    private void StopStatusMonitor(bool waitForExit = false)
    {
        CancellationTokenSource? cancellation;
        Task? task;
        lock (_monitorGate)
        {
            cancellation = _monitorCancellation;
            task = _monitorTask;
            _monitorCancellation = null;
            _monitorTask = null;
        }

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        if (waitForExit && task is not null)
        {
            // 轮询全程 ConfigureAwait(false)，同步等待不会死锁；取消后至多等一个观测往返。
            try
            {
                task.Wait(MonitorExitTimeout);
            }
            catch (AggregateException)
            {
                // 轮询自身异常在循环内已记录，释放路径无需再处理。
            }
        }

        DisposeCancellationAfterTask(cancellation, task);
    }

    private static void DisposeCancellationAfterTask(CancellationTokenSource cancellation, Task? task)
    {
        if (task is null || task.IsCompleted)
        {
            cancellation.Dispose();
            return;
        }

        task.ContinueWith(
            _ => cancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnLogMessageReceived(object? sender, CoreLogMessage message)
    {
        if (_isDisposed)
        {
            return;
        }

        CoreLogReceived?.Invoke(this, message);
    }

    private async Task MonitorStatusAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(StatePollInterval, cancellationToken).ConfigureAwait(false);
                var observation = await _serviceModeManager.ObserveCoreAsync(cancellationToken).ConfigureAwait(false);
                ApplyObservation(observation);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    // 观测失败不等于核心停了；只有确认核心进程已消失才降级为不可用。
    private CoreSnapshot ApplyObservation(CoreObservation observation)
    {
        if (observation.IsObserved)
        {
            var snapshot = new CoreSnapshot(
                observation.State!.Value,
                observation.Pid,
                HubStartupCoordinator.CorePipe,
                observation.LastError);
            PublishSnapshot(snapshot);
            return snapshot;
        }

        CoreSnapshot? lastSnapshot;
        lock (_snapshotGate)
        {
            lastSnapshot = _lastSnapshot;
        }

        if (lastSnapshot is not null && IsCoreProcessAlive(lastSnapshot.Pid))
        {
            // 核心进程仍在则保留上次状态，不清日志页也不重下代理选择。
            AppLogger.Debug($"Service-mode core observation skipped: reason={observation.UnobservedReason} pid={lastSnapshot.Pid}");
            return lastSnapshot;
        }

        var unavailable = new CoreSnapshot(
            CoreState.Unavailable,
            null,
            HubStartupCoordinator.CorePipe,
            observation.UnobservedReason);
        PublishSnapshot(unavailable);
        return unavailable;
    }

    // 核心被服务的 Job 对象绑定，宿主消失核心必消失；进程名兜住 PID 复用。
    private static bool IsCoreProcessAlive(int? pid)
    {
        if (pid is not > 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid.Value);
            var processName = process.ProcessName;
            return string.Equals(
                processName,
                Path.GetFileNameWithoutExtension(DesktopApplicationLayout.CoreBinaryPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private async Task<int> WaitReadyAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < ReadyTimeout)
        {
            var observation = await _serviceModeManager.ObserveCoreAsync(cancellationToken).ConfigureAwait(false);
            // 服务上报的进程号用于确认管道响应来自服务核心。
            if (observation is { State: CoreState.Running, Pid: > 0 }
                && !string.IsNullOrWhiteSpace(await ProbeVersionAsync(cancellationToken).ConfigureAwait(false)))
            {
                return observation.Pid.Value;
            }

            await Task.Delay(ReadyPollInterval, cancellationToken).ConfigureAwait(false);
        }

        PublishState(CoreState.Crashed, null, "Service-mode core startup timed out");
        throw new TimeoutException($"Service-mode core was not ready within {ReadyTimeout.TotalSeconds:N0} seconds.");
    }

    private void PublishState(CoreState state, int? pid, string? lastError)
    {
        PublishSnapshot(new CoreSnapshot(state, pid, HubStartupCoordinator.CorePipe, lastError));
    }

    private void PublishSnapshot(CoreSnapshot snapshot)
    {
        if (_isDisposed || _isShutdownSuspended)
        {
            return;
        }

        // 日志流可能在快照未变期间断开，故每次发布都重建，不能并入去重分支。
        if (snapshot.State == CoreState.Running)
        {
            _logStreamer.Start();
        }
        else
        {
            _logStreamer.Stop();
        }

        _setCoreHostActive(snapshot.State == CoreState.Running);

        CoreSnapshot? previous;
        lock (_snapshotGate)
        {
            if (_lastSnapshot == snapshot)
            {
                return;
            }

            previous = _lastSnapshot;
            _lastSnapshot = snapshot;
        }

        LogStateTransition(previous, snapshot);
        StateChanged?.Invoke(this, snapshot);
    }

    private static void LogStateTransition(CoreSnapshot? previous, CoreSnapshot snapshot)
    {
        var previousState = previous?.State.ToString() ?? "none";
        var pid = snapshot.Pid?.ToString() ?? "none";
        var error = string.IsNullOrWhiteSpace(snapshot.LastError) ? "none" : snapshot.LastError;
        var message = $"Service-mode core state changed: previous={previousState} current={snapshot.State} pid={pid} error={error}";

        if (snapshot.State is CoreState.Unavailable or CoreState.Crashed)
        {
            AppLogger.Warning(message);
            return;
        }

        AppLogger.Info(message);
    }

    private async Task<string?> ProbeVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _coreClient.GetAsync("version", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
                ? version.GetString()
                : null;
        }
        // 等待轮询退出有超时上限，超时后客户端可能已在释放路径中关闭。
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or IOException or ObjectDisposedException)
        {
            return null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

}
