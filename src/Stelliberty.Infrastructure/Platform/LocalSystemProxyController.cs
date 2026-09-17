using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public sealed class LocalSystemProxyController : ISystemProxyController, IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);
    private readonly ISystemProxyService _service;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateGate = new();
    private SystemProxyStatus _status = new(false, false);
    private int _operationVersion;
    private bool _isDisposed;

    public LocalSystemProxyController(ISystemProxyService service)
    {
        _service = service;
    }

    public event EventHandler<SystemProxyStatus>? StatusChanged;

    public Task<SystemProxyStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CurrentStatus);
    }

    public async Task<SystemProxyApplyResult> SetEnabledAsync(
        bool isEnabled,
        SystemProxyApplicationRequest? request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (isEnabled && request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var version = Interlocked.Increment(ref _operationVersion);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (version != Volatile.Read(ref _operationVersion))
            {
                return new SystemProxyApplyResult(true, "System proxy request was superseded.", CurrentStatus);
            }

            var result = await Task.Run(
                () => isEnabled ? _service.Enable(request!) : _service.Disable(),
                cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return new SystemProxyApplyResult(false, result.Message, CurrentStatus);
            }

            var status = new SystemProxyStatus(isEnabled, isEnabled);
            UpdateStatus(status);
            return new SystemProxyApplyResult(true, result.Message, status);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public SystemProxyApplyResult Shutdown(TimeSpan? timeout = null)
    {
        Interlocked.Increment(ref _operationVersion);
        if (!_operationGate.Wait(timeout ?? ShutdownTimeout))
        {
            return new SystemProxyApplyResult(false, "System proxy cleanup timed out.", CurrentStatus);
        }

        try
        {
            var current = CurrentStatus;
            if (!current.IsOwned)
            {
                return new SystemProxyApplyResult(true, "System proxy is not owned by this process.", current);
            }

            var result = _service.Disable();
            if (!result.IsSuccess)
            {
                return new SystemProxyApplyResult(false, result.Message, current);
            }

            var status = new SystemProxyStatus(false, false);
            UpdateStatus(status);
            return new SystemProxyApplyResult(true, result.Message, status);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"System proxy cleanup failed: {exception.Message}");
            return new SystemProxyApplyResult(false, exception.Message, CurrentStatus);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private SystemProxyStatus CurrentStatus
    {
        get
        {
            lock (_stateGate)
            {
                return _status;
            }
        }
    }

    private void UpdateStatus(SystemProxyStatus status)
    {
        lock (_stateGate)
        {
            _status = status;
        }

        StatusChanged?.Invoke(this, status);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Shutdown();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
