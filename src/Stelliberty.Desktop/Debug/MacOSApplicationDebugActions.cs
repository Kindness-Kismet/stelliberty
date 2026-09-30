#if DEBUG
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;

namespace Stelliberty.Desktop.Debug;

[SupportedOSPlatform("macos")]
internal static class MacOSApplicationDebugActions
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    public static void Reopen(bool hasVisibleWindows)
    {
        // 主线程调用原生委托，让事件经过 Avalonia 平台分发和应用订阅。
        Dispatcher.UIThread.VerifyAccess();
        var application = Send(GetClass("NSApplication"), GetSelector("sharedApplication"));
        var applicationDelegate = Send(application, GetSelector("delegate"));
        if (applicationDelegate == nint.Zero)
        {
            throw new InvalidOperationException("The macOS application delegate is not available");
        }

        // 应用及其委托均为借用引用，不能在此释放；原生 BOOL 按单字节封送。
        SendReopen(applicationDelegate, GetSelector("applicationShouldHandleReopen:hasVisibleWindows:"),
            application, hasVisibleWindows);
    }

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
    private static extern nint GetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
    private static extern nint GetSelector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendReopen(nint receiver, nint selector, nint application,
        [MarshalAs(UnmanagedType.I1)] bool hasVisibleWindows);
}
#endif
