using Stelliberty.Infrastructure.Tray;

namespace Stelliberty.Tray;

internal interface IUiSessionConnection
{
    Guid Id { get; }

    Task RequestActivationAsync(CancellationToken cancellationToken);

    Task RequestToggleAsync(CancellationToken cancellationToken);
}

internal sealed class UiSessionManager(
    IDesktopUiLauncher launcher,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan LaunchReservationDuration = TimeSpan.FromSeconds(30);
    private readonly IDesktopUiLauncher _launcher = launcher;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UiSession? _activeSession;
    private string? _pendingToken;
    private DateTimeOffset _pendingUntil;

    public async Task<UiActivateResult> ActivateAsync(CancellationToken cancellationToken)
    {
        IUiSessionConnection? activeConnection;
        string? launchToken = null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            activeConnection = _activeSession?.Connection;
            if (activeConnection is null)
            {
                if (_pendingToken is not null && _timeProvider.GetUtcNow() < _pendingUntil)
                {
                    return new UiActivateResult(false, false, true);
                }

                launchToken = Guid.NewGuid().ToString("N");
                _pendingToken = launchToken;
                _pendingUntil = _timeProvider.GetUtcNow() + LaunchReservationDuration;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (activeConnection is not null)
        {
            try
            {
                await activeConnection.RequestActivationAsync(cancellationToken).ConfigureAwait(false);
                return new UiActivateResult(false, true, false);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                await OnConnectionClosedAsync(activeConnection.Id).ConfigureAwait(false);
                return await ActivateAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        try
        {
            await _launcher.LaunchAsync(launchToken!, cancellationToken).ConfigureAwait(false);
            return new UiActivateResult(true, false, true);
        }
        catch
        {
            await ClearPendingLaunchAsync(launchToken!).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<UiActivateResult> ToggleAsync(CancellationToken cancellationToken)
    {
        IUiSessionConnection? activeConnection;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            activeConnection = _activeSession?.Connection;
        }
        finally
        {
            _gate.Release();
        }

        if (activeConnection is null)
        {
            return await ActivateAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await activeConnection.RequestToggleAsync(cancellationToken).ConfigureAwait(false);
            return new UiActivateResult(false, true, false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            await OnConnectionClosedAsync(activeConnection.Id).ConfigureAwait(false);
            return await ActivateAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<UiRegisterResult> RegisterAsync(
        string sessionToken,
        int uiPid,
        IUiSessionConnection connection,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_activeSession is { } active)
            {
                if (active.Connection.Id == connection.Id && active.SessionToken == sessionToken)
                {
                    return new UiRegisterResult(active.SessionId, 0);
                }

                throw new UiSessionException("ui.session_active", "A UI session is already registered.");
            }

            if (_pendingToken != sessionToken || _timeProvider.GetUtcNow() >= _pendingUntil)
            {
                throw new UiSessionException("ui.session_invalid", "The UI launch reservation is invalid or expired.");
            }

            var sessionId = Guid.NewGuid().ToString("N");
            _activeSession = new UiSession(sessionId, sessionToken, uiPid, connection);
            _pendingToken = null;
            return new UiRegisterResult(sessionId, 0);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UiUnregisterResult> UnregisterAsync(
        string sessionId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wasRegistered = _activeSession is { } active
                && active.SessionId == sessionId
                && active.Connection.Id == connectionId;
            if (wasRegistered)
            {
                _activeSession = null;
            }

            return new UiUnregisterResult(wasRegistered);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task OnConnectionClosedAsync(Guid connectionId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_activeSession?.Connection.Id == connectionId)
            {
                _activeSession = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UiSessionState> GetStateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var isPending = _pendingToken is not null && _timeProvider.GetUtcNow() < _pendingUntil;
            return new UiSessionState(_activeSession?.UiPid, isPending);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ClearPendingLaunchAsync(string launchToken)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_pendingToken == launchToken)
            {
                _pendingToken = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record UiSession(
        string SessionId,
        string SessionToken,
        int UiPid,
        IUiSessionConnection Connection);
}

internal sealed record UiSessionState(int? UiPid, bool IsLaunchPending);

internal sealed class UiSessionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
