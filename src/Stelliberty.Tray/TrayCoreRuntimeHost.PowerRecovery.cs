using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Runtime;
using Stelliberty.Infrastructure.Settings;

namespace Stelliberty.Tray;

internal sealed partial class TrayCoreRuntimeHost
{
    private long _powerOperationRevision;
    private bool _isCoreRequested;
    private bool _isPowerRecovering;

    public CorePowerRecoveryContext CapturePowerContext()
    {
        lock (_stateGate)
        {
            var wasRunning = _isCoreRequested && !_isShutdownSuspended
                && (_status.Snapshot.State == CoreState.Running || _isPowerRecovering);
            AppLogger.Info($"Power core snapshot: pid={_status.Snapshot.Pid} state={_status.Snapshot.State} service_mode={_isServiceModeActive} requested={_isCoreRequested}");
            return new(wasRunning, _powerOperationRevision, _status.CoreGeneration);
        }
    }

    public bool IsPowerRecoveryCurrent(CorePowerRecoveryContext context)
    {
        lock (_stateGate)
        {
            return !_isDisposed && !_isShutdownSuspended && _isCoreRequested
                && context.OperationRevision == _powerOperationRevision;
        }
    }

    public async Task<CorePowerRecoveryResult> RecoverCoreAfterResumeAsync(
        CorePowerRecoveryContext context, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsPowerRecoveryCurrent(context))
            {
                return new(CorePowerRecoveryOutcome.Skipped, "Core operation changed after suspend.");
            }

            if (_isServiceModeActive)
            {
                // 先续接服务租期；服务可能已因休眠期间的心跳超时停止核心。
                var heartbeat = await _serviceModeManager.SendHeartbeatAsync(cancellationToken).ConfigureAwait(false);
                if (!heartbeat.IsSuccess)
                {
                    return new(CorePowerRecoveryOutcome.Retry, $"Service heartbeat recovery failed: {heartbeat.Message}");
                }
            }

            var manager = RequireManager();
            var before = UpdateStatus(await manager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
            if (!IsPowerRecoveryCurrent(context))
            {
                return new(CorePowerRecoveryOutcome.Skipped, "Core operation changed while checking resume state.");
            }

            if (before.Snapshot.State == CoreState.Running && before.CoreGeneration != context.CoreRevision)
            {
                if (string.IsNullOrWhiteSpace(await _selectionClient.GetVersionAsync(cancellationToken).ConfigureAwait(false)))
                {
                    return new(CorePowerRecoveryOutcome.Retry, "The replacement core API is not ready.");
                }
                AppLogger.Info($"Power core already recovered: pid={before.Snapshot.Pid} generation={before.CoreGeneration} service_mode={_isServiceModeActive}");
                return new(CorePowerRecoveryOutcome.Recovered, "A new core instance is already ready.");
            }

            var settings = new JsonAppSettingsStore(new TrayPlatformDirectories()).Load();
            AppLogger.Info($"Power core recovery started: pid={before.Snapshot.Pid} state={before.Snapshot.State} service_mode={_isServiceModeActive} tun={settings.IsTunEnabled} generation={before.CoreGeneration}");
            lock (_stateGate) _isPowerRecovering = true;

            if (before.Snapshot.State == CoreState.Running)
            {
                // 重建核心连接和虚拟网卡，清除休眠前残留的网络状态。
                await _restoringManager!.RestartAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var started = await EnsureStartedCoreAsync(cancellationToken).ConfigureAwait(false);
                if (!started.IsSuccess)
                {
                    return new(CorePowerRecoveryOutcome.Retry, started.Message);
                }
            }

            var after = UpdateStatus(await manager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
            AppLogger.Info($"Power core recovery result: previous_pid={before.Snapshot.Pid} pid={after.Snapshot.Pid} state={after.Snapshot.State} service_mode={_isServiceModeActive} generation={after.CoreGeneration}");
            return after.Snapshot.State == CoreState.Running
                ? new(CorePowerRecoveryOutcome.Recovered, _isServiceModeActive ? "Service core is ready." : "Normal core is ready.")
                : new(CorePowerRecoveryOutcome.Retry, $"Core is not ready: {after.Snapshot.State} {after.Snapshot.LastError}");
        }
        finally
        {
            lock (_stateGate) _isPowerRecovering = false;
            _operationGate.Release();
        }
    }

    private void RecordCoreRunIntent(bool isRequested, bool invalidateRecovery = false)
    {
        lock (_stateGate)
        {
            if (_isCoreRequested != isRequested || invalidateRecovery) _powerOperationRevision++;
            _isCoreRequested = isRequested;
        }
    }
}
