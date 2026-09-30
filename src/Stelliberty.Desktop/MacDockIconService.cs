using System.Runtime.InteropServices;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Desktop;

internal static class MacDockIconService
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    public static void SetPackagedIcon()
    {
        var iconPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Resources", "AppIcon.icns"));
        if (!File.Exists(iconPath))
        {
            AppLogger.Warning($"macOS Dock icon was not found: {iconPath}");
            return;
        }

        try
        {
            SetIcon(iconPath);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"macOS Dock icon could not be set: {exception.Message}");
        }
    }

    private static void SetIcon(string iconPath)
    {
        var utf8Path = Marshal.StringToCoTaskMemUTF8(iconPath);
        try
        {
            var nsString = Send(GetClass("NSString"), GetSelector("stringWithUTF8String:"), utf8Path);
            var nsImage = Send(Send(GetClass("NSImage"), GetSelector("alloc")),
                GetSelector("initWithContentsOfFile:"), nsString);
            if (nsImage == nint.Zero)
            {
                AppLogger.Warning($"macOS Dock icon could not be loaded: {iconPath}");
                return;
            }

            try
            {
                var application = Send(GetClass("NSApplication"), GetSelector("sharedApplication"));
                Send(application, GetSelector("setApplicationIconImage:"), nsImage);
            }
            finally
            {
                Send(nsImage, GetSelector("release"));
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8Path);
        }
    }

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
    private static extern nint GetClass(string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
    private static extern nint GetSelector(string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint argument);
}
