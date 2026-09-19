using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Application.Runtime;

public sealed class SystemPowerRecoveryService(ICorePowerRecovery runtime, TimeProvider? timeProvider = null) : IAsyncDisposable
{
    // 唤醒后给网络设备 2 秒恢复时间；后续两次退避覆盖驱动和服务的延迟就绪。
    private static readonly TimeSpan[] AttemptDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly List<Task> _tasks = [];
    private CancellationTokenSource? _recoveryCancellation;
    private Task _recoveryTask = Task.CompletedTask;
    private CorePowerRecoveryContext? _context;
    private SystemPowerRecoverySnapshot _snapshot = new(0, SystemPowerRecoveryPhase.Idle, false, string.Empty, null, 0, null);
    private bool _isSuspended;
    private bool _isDisposed;

    public SystemPowerRecoverySnapshot Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    public Task HandleAsync(SystemPowerEventKind kind, string source)
    {
        lock (_gate)
        {
            if (_isDisposed) return Task.CompletedTask;
            if (kind == SystemPowerEventKind.Suspend)
            {
                if (_isSuspended)
                {
                    AppLogger.Debug($"Power suspend duplicate: cycle={_snapshot.Cycle} source={source}");
                    return Task.CompletedTask;
                }

                var suspendContext = runtime.CaptureContext();
                // 上一轮恢复尚未结束时再次休眠，保留运行意图；用户操作变更仍使其失效。
                if (_recoveryCancellation is not null && _context is { WasRunning: true }
                    && _context.OperationRevision == suspendContext.OperationRevision)
                {
                    suspendContext = suspendContext with { WasRunning = true };
                }
                _recoveryCancellation?.Cancel();
                _context = suspendContext;
                _isSuspended = true;
                _snapshot = new(_snapshot.Cycle + 1, SystemPowerRecoveryPhase.Suspended,
                    _context.WasRunning, source, _time.GetUtcNow(), 0, null);
                AppLogger.Info($"Power suspend: cycle={_snapshot.Cycle} source={source} core_was_running={_context.WasRunning} operation_revision={_context.OperationRevision} core_revision={_context.CoreRevision}");
                return Task.CompletedTask;
            }

            if (!_isSuspended)
            {
                AppLogger.Debug($"Power resume ignored: cycle={_snapshot.Cycle} source={source} reason=no-pending-suspend phase={_snapshot.Phase}");
                return _recoveryTask;
            }

            _isSuspended = false;
            var context = _context!;
            _snapshot = _snapshot with { Source = source, LastEventAt = _time.GetUtcNow() };
            if (!context.WasRunning)
            {
                _snapshot = _snapshot with { Phase = SystemPowerRecoveryPhase.Skipped, Message = "Core was not running before suspend." };
                AppLogger.Info($"Power recovery skipped: cycle={_snapshot.Cycle} source={source} reason=core-was-stopped");
                return Task.CompletedTask;
            }

            var cycle = _snapshot.Cycle;
            var cancellation = new CancellationTokenSource();
            _recoveryCancellation = cancellation;
            _snapshot = _snapshot with { Phase = SystemPowerRecoveryPhase.Waiting };
            // 系统回调只记录事件，恢复操作不占用电源通知线程。
            _recoveryTask = Task.Run(() => RecoverAsync(cycle, context, cancellation));
            _tasks.RemoveAll(task => task.IsCompleted);
            _tasks.Add(_recoveryTask);
            AppLogger.Info($"Power resume: cycle={cycle} source={source} recovery_scheduled=true");
            return _recoveryTask;
        }
    }

    private async Task RecoverAsync(long cycle, CorePowerRecoveryContext context, CancellationTokenSource cancellation)
    {
        var started = _time.GetTimestamp();
        var token = cancellation.Token;
        string? lastError = null;
        try
        {
            for (var index = 0; index < AttemptDelays.Length; index++)
            {
                var attempt = index + 1;
                Update(cycle, SystemPowerRecoveryPhase.Waiting, attempt, lastError);
                await Task.Delay(AttemptDelays[index], _time, token).ConfigureAwait(false);
                Update(cycle, SystemPowerRecoveryPhase.Recovering, attempt, lastError);
                AppLogger.Info($"Power recovery attempt: cycle={cycle} attempt={attempt}/{AttemptDelays.Length}");
                using var timeout = new CancellationTokenSource(AttemptTimeout, _time);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
                try
                {
                    var result = await runtime.RecoverAsync(context, linked.Token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (result.Outcome != CorePowerRecoveryOutcome.Retry)
                    {
                        var phase = result.Outcome == CorePowerRecoveryOutcome.Recovered
                            ? SystemPowerRecoveryPhase.Completed : SystemPowerRecoveryPhase.Skipped;
                        Update(cycle, phase, attempt, result.Message);
                        AppLogger.Info($"Power recovery finished: cycle={cycle} outcome={result.Outcome} attempt={attempt} elapsed_ms={_time.GetElapsedTime(started).TotalMilliseconds:0} detail={result.Message}");
                        return;
                    }
                    lastError = result.Message;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    lastError = $"Recovery attempt exceeded {AttemptTimeout.TotalSeconds:0} seconds.";
                }
                catch (Exception exception)
                {
                    lastError = exception.Message;
                }
                AppLogger.Warning($"Power recovery attempt failed: cycle={cycle} attempt={attempt} detail={lastError}");
            }

            Update(cycle, SystemPowerRecoveryPhase.Failed, AttemptDelays.Length, lastError);
            AppLogger.Error($"Power recovery exhausted: cycle={cycle} attempts={AttemptDelays.Length} elapsed_ms={_time.GetElapsedTime(started).TotalMilliseconds:0} detail={lastError}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Update(cycle, SystemPowerRecoveryPhase.Cancelled, Snapshot.Attempt, "Recovery cancelled by suspend or shutdown.");
            AppLogger.Info($"Power recovery cancelled: cycle={cycle} reason=suspend-or-shutdown");
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_recoveryCancellation, cancellation)) _recoveryCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private void Update(long cycle, SystemPowerRecoveryPhase phase, int attempt, string? message)
    {
        lock (_gate)
        {
            if (_snapshot.Cycle == cycle)
            {
                _snapshot = _snapshot with { Phase = phase, Attempt = attempt, Message = message };
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (_gate)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _recoveryCancellation?.Cancel();
            pending = [.. _tasks];
        }
        await Task.WhenAll(pending).ConfigureAwait(false);
    }
}
