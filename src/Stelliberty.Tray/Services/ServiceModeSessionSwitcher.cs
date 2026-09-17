using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;

namespace Stelliberty.Tray;

internal sealed class ServiceModeSessionSwitcher(
    ServiceModeManager serviceModeManager,
    SwitchableCoreManager coreManager,
    Func<ServiceModeStatus, ICoreManager> createServiceCoreManager,
    Func<ICoreManager> createNormalCoreManager,
    Func<CancellationToken, Task<CoreHostOperationResult>> stopNormalCore,
    Func<CancellationToken, Task<CoreHostOperationResult>> resumeNormalCore,
    Func<ServiceModeStatus, CancellationToken, Task<CoreHostOperationResult>> startServiceCore,
    Action<bool> setServiceModeCoreHostActive,
    bool isServiceModeActive = false) : IDisposable
{
    private readonly object _lifetimeGate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _isServiceModeActive = isServiceModeActive;
    private bool _isDisposed;

    public Task<ServiceModeOperationResult> ActivateAsync(CancellationToken cancellationToken)
    {
        return RunOperationAsync(ActivateCoreAsync, cancellationToken);
    }

    public Task<ServiceModeOperationResult> DeactivateAsync(CancellationToken cancellationToken)
    {
        return RunOperationAsync(DeactivateCoreAsync, cancellationToken);
    }

    public async Task<ServiceModeOperationResult> PrepareForShutdownAsync(CancellationToken cancellationToken)
    {
        lock (_lifetimeGate)
        {
            if (_isDisposed)
            {
                return ServiceModeOperationResult.Canceled("Service mode session is already shut down.");
            }

            // 退出后不再接受模式切换，避免停止核心后又被并发操作拉起。
            _lifetimeCancellation.Cancel();
        }

        try
        {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ServiceModeOperationResult.Canceled("Service mode shutdown timed out.");
        }

        try
        {
            if (!_isServiceModeActive)
            {
                return ServiceModeOperationResult.Success("Normal mode does not require service-core shutdown.");
            }

            ServiceModeOperationResult result;
            try
            {
                result = await serviceModeManager.StopCoreHostAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return ServiceModeOperationResult.Canceled("Service-mode core shutdown timed out.");
            }
            catch (Exception exception)
            {
                return ServiceModeOperationResult.Failed(exception.Message);
            }

            if (result.IsSuccess)
            {
                _isServiceModeActive = false;
                setServiceModeCoreHostActive(false);
            }

            return result;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _lifetimeCancellation.Cancel();
        }

        // 同步等待在途操作释放信号量，确保取消令牌不被使用中释放。
        _operationGate.Wait();
        _lifetimeCancellation.Dispose();
    }

    public bool TryDisposeForShutdown()
    {
        lock (_lifetimeGate)
        {
            if (_isDisposed)
            {
                return true;
            }

            _isDisposed = true;
            _lifetimeCancellation.Cancel();
        }

        // 退出事件不得再次等待已经超时的模式切换，未完成资源随进程回收。
        if (!_operationGate.Wait(0))
        {
            return false;
        }

        _lifetimeCancellation.Dispose();
        return true;
    }

    private async Task<ServiceModeOperationResult> RunOperationAsync(
        Func<CancellationToken, Task<ServiceModeOperationResult>> operation,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource linkedCancellation;
        lock (_lifetimeGate)
        {
            if (_isDisposed)
            {
                return ServiceModeOperationResult.Canceled("Service mode session is shutting down.");
            }

            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation.Token);
        }

        using var ownedLinkedCancellation = linkedCancellation;
        try
        {
            await _operationGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ServiceModeOperationResult.Canceled("Service mode operation was canceled.");
        }

        try
        {
            lock (_lifetimeGate)
            {
                if (_isDisposed)
                {
                    return ServiceModeOperationResult.Canceled("Service mode session is shutting down.");
                }
            }

            return await operation(linkedCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<ServiceModeOperationResult> ActivateCoreAsync(CancellationToken cancellationToken)
    {
        ServiceModeStatus status;
        try
        {
            status = await serviceModeManager.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ServiceModeOperationResult.Canceled("Service mode activation was canceled.");
        }
        catch (Exception exception)
        {
            return ServiceModeOperationResult.Failed(exception.Message);
        }

        if (!status.IsRunning)
        {
            return ServiceModeOperationResult.Failed("Service mode is not running.");
        }

        SwitchableCoreManager.CoreTransition transition;
        try
        {
            transition = await coreManager.BeginTransitionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ServiceModeOperationResult.Canceled("Service mode activation was canceled.");
        }
        catch (Exception exception)
        {
            return ServiceModeOperationResult.Failed(exception.Message);
        }

        using var ownedTransition = transition;
        // 两种核心共用控制管道，整段切换期间不允许其它核心操作进入。
        CoreHostOperationResult stopped;
        try
        {
            stopped = await stopNormalCore(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await RollBackAsync(
                transition,
                ServiceModeOperationResult.Canceled("Service mode activation was canceled.")).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return await RollBackAsync(
                transition,
                ServiceModeOperationResult.Failed(exception.Message)).ConfigureAwait(false);
        }

        if (!stopped.IsSuccess)
        {
            return await RollBackAsync(
                transition,
                ServiceModeOperationResult.Failed(stopped.Message)).ConfigureAwait(false);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var started = await startServiceCore(status, cancellationToken).ConfigureAwait(false);
            if (!started.IsSuccess)
            {
                return await RollBackAsync(
                    transition,
                    ServiceModeOperationResult.Failed(started.Message)).ConfigureAwait(false);
            }

            _isServiceModeActive = true;
            setServiceModeCoreHostActive(true);
            await transition.SwitchAsync(createServiceCoreManager(status), cancellationToken).ConfigureAwait(false);
            return ServiceModeOperationResult.Success("Service mode is active.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await RollBackAsync(
                transition,
                ServiceModeOperationResult.Canceled("Service mode activation was canceled.")).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return await RollBackAsync(
                transition,
                ServiceModeOperationResult.Failed(exception.Message)).ConfigureAwait(false);
        }
    }

    private async Task<ServiceModeOperationResult> DeactivateCoreAsync(CancellationToken cancellationToken)
    {
        SwitchableCoreManager.CoreTransition transition;
        try
        {
            transition = await coreManager.BeginTransitionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ServiceModeOperationResult.Canceled("Normal mode activation was canceled.");
        }
        catch (Exception exception)
        {
            return ServiceModeOperationResult.Failed(exception.Message);
        }

        using var ownedTransition = transition;
        try
        {
            var resumed = await resumeNormalCore(cancellationToken).ConfigureAwait(false);
            if (!resumed.IsSuccess)
            {
                return ServiceModeOperationResult.Failed($"Normal-mode activation failed: {resumed.Message}");
            }

            // 服务已经卸载，普通核心启动后必须完成管理器归属切换。
            var readinessFailure = await transition.SwitchEvenIfUnavailableAsync(
                createNormalCoreManager(),
                CancellationToken.None).ConfigureAwait(false);
            _isServiceModeActive = false;
            setServiceModeCoreHostActive(false);
            return readinessFailure is null
                ? ServiceModeOperationResult.Success("Normal mode is active.")
                : ServiceModeOperationResult.Failed($"Normal mode started but is not ready: {readinessFailure.Message}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ServiceModeOperationResult.Canceled("Normal mode activation was canceled.");
        }
        catch (Exception exception)
        {
            return ServiceModeOperationResult.Failed($"Normal-mode activation failed: {exception.Message}");
        }
    }

    private async Task<ServiceModeOperationResult> RollBackAsync(
        SwitchableCoreManager.CoreTransition transition,
        ServiceModeOperationResult result)
    {
        try
        {
            var stopped = await serviceModeManager.StopCoreHostAsync(CancellationToken.None).ConfigureAwait(false);
            if (!stopped.IsSuccess)
            {
                AppLogger.Warning($"Service-mode core rollback stop failed: {stopped.Message}");
                return ServiceModeOperationResult.Failed($"{result.Message} Normal-mode recovery was skipped: {stopped.Message}");
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Service-mode core rollback stop failed: {exception.Message}");
            return ServiceModeOperationResult.Failed($"{result.Message} Normal-mode recovery was skipped: {exception.Message}");
        }

        _isServiceModeActive = false;
        setServiceModeCoreHostActive(false);
        try
        {
            var resumed = await resumeNormalCore(CancellationToken.None).ConfigureAwait(false);
            if (!resumed.IsSuccess)
            {
                return ServiceModeOperationResult.Failed($"{result.Message} Normal-mode recovery failed: {resumed.Message}");
            }

            await transition.SwitchAsync(createNormalCoreManager(), CancellationToken.None).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            return ServiceModeOperationResult.Failed($"{result.Message} Normal-mode recovery failed: {exception.Message}");
        }
    }
}
