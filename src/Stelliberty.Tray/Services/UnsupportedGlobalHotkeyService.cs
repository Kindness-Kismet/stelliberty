using Stelliberty.Application.Platform;

namespace Stelliberty.Tray;

internal sealed class UnsupportedGlobalHotkeyService(Action<GlobalHotkeyAction> activated) : IGlobalHotkeyService
{
    private readonly GlobalHotkeyActivationController _activationController = new(activated);

    public Task<GlobalHotkeyApplyResult> ApplyAsync(
        GlobalHotkeyAction action,
        string gesture,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = string.IsNullOrWhiteSpace(gesture)
            ? GlobalHotkeyApplyResult.Success()
            : GlobalHotkeyApplyResult.Failure(GlobalHotkeyApplyError.Unsupported);
        return Task.FromResult(result);
    }

    public Task SetActivationSuppressedAsync(
        bool isSuppressed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _activationController.SetSuppressed(isSuppressed);
        return Task.CompletedTask;
    }

#if DEBUG
    public Task<bool> SimulateActivationAsync(
        GlobalHotkeyAction action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_activationController.TryActivate(action));
    }
#endif

    public void Dispose()
    {
    }
}
