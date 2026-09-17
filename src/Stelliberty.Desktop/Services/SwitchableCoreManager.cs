using Stelliberty.Application.CoreLogs;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Runtime;
using Stelliberty.Domain.CoreLogs;
using Stelliberty.Infrastructure.Core;

namespace Stelliberty.Desktop.Services;

internal sealed class SwitchableCoreManager : ICoreManager, IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ICoreManager _current;
    private volatile bool _isDisposalRequested;
    private volatile bool _isShutdownSuspended;
    private bool _isDisposed;

    public SwitchableCoreManager(ICoreManager initial)
    {
        _current = initial;
        Attach(initial);
    }

    public event EventHandler<CoreSnapshot>? StateChanged;

    public event EventHandler<CoreLogMessage>? CoreLogReceived;

    public Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        return UseAsync(EnsureCoreReadyAsync, cancellationToken);
    }

    public async Task<CoreTransition> BeginTransitionAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return new CoreTransition(this);
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    public Task<CoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return UseAsync((core, token) => core.GetSnapshotAsync(token), cancellationToken);
    }

    public Task<CoreApplyConfigResult> ApplyConfigAsync(CoreApplyConfigRequest request, CancellationToken cancellationToken = default)
    {
        return UseAsync((core, token) => core.ApplyConfigAsync(request, token), cancellationToken);
    }

    public Task RestartAsync(CancellationToken cancellationToken = default)
    {
        return UseAsync((core, token) => core.RestartAsync(token), cancellationToken);
    }

    public void Dispose()
    {
        // 进程退出路径只有同步 Dispose，此处有意阻塞桥接异步释放。
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        _isDisposalRequested = true;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            Detach(_current);
            DisposeCore(_current);
        }
        finally
        {
            _gate.Release();
        }
    }

    // 系统关机窗口内服务核心会先被终止，其状态变化不再转发；关机取消时由调用方复位。
    // 不取 _gate：关机来自窗口消息线程，不得阻塞在在途核心操作上。
    public void SetShutdownSuspension(bool isSuspended)
    {
        _isShutdownSuspended = isSuspended;
        // 与 ReplaceCore 并发时可能读到旧核心，切换后由 Attach 再次同步抑制状态。
        if (Volatile.Read(ref _current) is ServiceModeCoreManager serviceModeCoreManager)
        {
            serviceModeCoreManager.SetShutdownSuspension(isSuspended);
        }
    }

    public bool TryDisposeForShutdown()
    {
        _isDisposalRequested = true;
        // 退出事件不能等待仍在执行的核心操作，Windows 会随 Job 关闭核心。
        if (!_gate.Wait(0))
        {
            return false;
        }

        try
        {
            if (_isDisposed)
            {
                return true;
            }

            _isDisposed = true;
            Detach(_current);
            DisposeCore(_current);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UseAsync(Func<ICoreManager, CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await operation(_current, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> UseAsync<T>(Func<ICoreManager, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await operation(_current, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SwitchCoreAsync(ICoreManager next, CancellationToken cancellationToken)
    {
        CoreSnapshot snapshot;
        try
        {
            snapshot = await EnsureCoreReadyAsync(next, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            DisposeCore(next);
            throw;
        }

        ReplaceCore(next, snapshot);
    }

    private async Task<Exception?> SwitchEvenIfUnavailableAsync(ICoreManager next, CancellationToken cancellationToken)
    {
        CoreSnapshot snapshot;
        Exception? readinessFailure = null;
        try
        {
            snapshot = await EnsureCoreReadyAsync(next, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            readinessFailure = exception;
            snapshot = new CoreSnapshot(CoreState.Unavailable, null, string.Empty, exception.Message);
        }

        ReplaceCore(next, snapshot);
        return readinessFailure;
    }

    private void ReplaceCore(ICoreManager next, CoreSnapshot snapshot)
    {
        var previous = _current;
        Detach(previous);
        Volatile.Write(ref _current, next);
        Attach(next);
        DisposeCore(previous);
        try
        {
            OnStateChanged(this, snapshot);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Core manager state observer failed during switch: {exception.Message}");
        }
    }

    private static async Task<CoreSnapshot> EnsureCoreReadyAsync(ICoreManager core, CancellationToken cancellationToken)
    {
        switch (core)
        {
            case IpcCoreManager ipcCoreManager:
                await ipcCoreManager.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ServiceModeCoreManager serviceModeCoreManager:
                await serviceModeCoreManager.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
                break;
        }

        return await core.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Attach(ICoreManager core)
    {
        core.StateChanged += OnStateChanged;
        core.CoreLogReceived += OnCoreLogReceived;
        // 关机置位与切换并发时抑制可能落在旧核心上，接管新核心时补齐。
        if (_isShutdownSuspended && core is ServiceModeCoreManager serviceModeCoreManager)
        {
            serviceModeCoreManager.SetShutdownSuspension(true);
        }
    }

    private void Detach(ICoreManager core)
    {
        core.StateChanged -= OnStateChanged;
        core.CoreLogReceived -= OnCoreLogReceived;
    }

    private void OnStateChanged(object? sender, CoreSnapshot snapshot)
    {
        if (_isShutdownSuspended)
        {
            return;
        }

        StateChanged?.Invoke(this, snapshot);
    }

    private void OnCoreLogReceived(object? sender, CoreLogMessage message)
    {
        CoreLogReceived?.Invoke(this, message);
    }

    private static void DisposeCore(ICoreManager core)
    {
        try
        {
            if (core is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Core manager dispose failed: {exception.Message}");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed || _isDisposalRequested)
        {
            throw new ObjectDisposedException(nameof(SwitchableCoreManager));
        }
    }

    internal sealed class CoreTransition : IDisposable
    {
        private SwitchableCoreManager? _owner;

        internal CoreTransition(SwitchableCoreManager owner)
        {
            _owner = owner;
        }

        public Task SwitchAsync(ICoreManager next, CancellationToken cancellationToken = default)
        {
            var owner = _owner ?? throw new ObjectDisposedException(nameof(CoreTransition));
            return owner.SwitchCoreAsync(next, cancellationToken);
        }

        public Task<Exception?> SwitchEvenIfUnavailableAsync(
            ICoreManager next,
            CancellationToken cancellationToken = default)
        {
            var owner = _owner ?? throw new ObjectDisposedException(nameof(CoreTransition));
            return owner.SwitchEvenIfUnavailableAsync(next, cancellationToken);
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?._gate.Release();
        }
    }
}
