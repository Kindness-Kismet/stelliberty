#if DEBUG
using Stelliberty.Application.Platform;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static async Task<string?> ExecuteHotkeyCommandAsync(MainWindow window, string command)
    {
        var actionName = command["hotkey.trigger ".Length..].Trim();
        var action = actionName.ToLowerInvariant() switch
        {
            "window" => GlobalHotkeyAction.ToggleWindow,
            "system-proxy" => GlobalHotkeyAction.ToggleSystemProxy,
            "tun" => GlobalHotkeyAction.ToggleTun,
            _ => throw new InvalidOperationException($"Unknown hotkey action: {actionName}"),
        };

        var activated = await RequireViewModel(window).AppBehavior.SimulateHotkeyActivationAsync(action);
        return $"action={action};activated={activated.ToString().ToLowerInvariant()}";
    }
}
#endif
