using Avalonia.Controls;
using Avalonia.Platform;

namespace Stelliberty.Tray;

internal enum TrayIconState
{
    Disabled,
    ProxyEnabled,
    TunEnabled,
    ProxyTunEnabled,
}

internal static class TrayIconFactory
{
    public static WindowIcon Create(TrayIconState state)
    {
        var authority = typeof(TrayIconFactory).Assembly.GetName().Name;
        var uri = new Uri($"avares://{authority}/Assets/{PlatformDirectory()}/tray/{FileName(state)}.{PlatformExtension()}");
        using var stream = AssetLoader.Open(uri);
        return new WindowIcon(stream);
    }

    private static string FileName(TrayIconState state) => state switch
    {
        TrayIconState.ProxyEnabled => "proxy_enabled",
        TrayIconState.TunEnabled => "tun_enabled",
        TrayIconState.ProxyTunEnabled => "proxy_tun_enabled",
        _ => "disabled",
    };

    private static string PlatformDirectory()
    {
        if (OperatingSystem.IsWindows()) return "win";
        if (OperatingSystem.IsMacOS()) return "macos";
        return "linux";
    }

    private static string PlatformExtension() => OperatingSystem.IsWindows() ? "ico" : "png";
}
