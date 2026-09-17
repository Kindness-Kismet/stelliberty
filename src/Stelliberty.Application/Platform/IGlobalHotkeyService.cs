namespace Stelliberty.Application.Platform;

public interface IGlobalHotkeyService : IDisposable
{
    Task<GlobalHotkeyApplyResult> ApplyAsync(
        GlobalHotkeyAction action,
        string gesture,
        CancellationToken cancellationToken = default);

    Task SetActivationSuppressedAsync(bool isSuppressed, CancellationToken cancellationToken = default);

#if DEBUG
    Task<bool> SimulateActivationAsync(
        GlobalHotkeyAction action,
        CancellationToken cancellationToken = default);
#endif
}
