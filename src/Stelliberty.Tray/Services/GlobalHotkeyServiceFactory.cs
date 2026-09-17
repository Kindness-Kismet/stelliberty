using Stelliberty.Application.Platform;

namespace Stelliberty.Tray;

public static class GlobalHotkeyServiceFactory
{
    public static IGlobalHotkeyService Create(Action<GlobalHotkeyAction> activated)
    {
        return OperatingSystem.IsWindows()
            ? new WindowsGlobalHotkeyService(activated)
            : new UnsupportedGlobalHotkeyService(activated);
    }
}
