using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Desktop;

[SupportedOSPlatform("macos")]
internal static class MacDockIconService
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    public static void SetPackagedIcon()
    {
        // AppKit 由 Avalonia 初始化，图标设置必须在主线程执行。
        Dispatcher.UIThread.VerifyAccess();
        // 发布包 UI 位于 Contents/MacOS/data/deps，图标位于 Contents/Resources。
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
            // 初始化阶段不依赖自动释放池，路径字符串与图像均显式持有并释放。
            var nsString = Send(Send(GetClass("NSString"), GetSelector("alloc")),
                GetSelector("initWithUTF8String:"), utf8Path);
            nint nsImage;
            try
            {
                nsImage = Send(Send(GetClass("NSImage"), GetSelector("alloc")),
                    GetSelector("initWithContentsOfFile:"), nsString);
            }
            finally
            {
                Release(nsString, GetSelector("release"));
            }
            if (nsImage == nint.Zero)
            {
                AppLogger.Warning($"macOS Dock icon could not be loaded: {iconPath}");
                return;
            }

            try
            {
                var application = Send(GetClass("NSApplication"), GetSelector("sharedApplication"));
                SetApplicationIcon(application, GetSelector("setApplicationIconImage:"), nsImage);
            }
            finally
            {
                // 应用持有设置后的图像，此处仅释放 alloc/init 创建的本地所有权。
                Release(nsImage, GetSelector("release"));
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8Path);
        }
    }

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
    private static extern nint GetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
    private static extern nint GetSelector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint argument);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SetApplicationIcon(nint receiver, nint selector, nint image);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void Release(nint receiver, nint selector);
}
