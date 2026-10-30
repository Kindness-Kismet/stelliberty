using System.Runtime.InteropServices;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Tray;

internal static class ForegroundActivation
{
    public static void AllowForProcess(int processId)
    {
        // Windows 的前台激活权限不会随 IPC 传递，必须在通知目标进程前授权。
        if (OperatingSystem.IsWindows() && !AllowSetForegroundWindow((uint)processId))
        {
            AppLogger.Warning($"Foreground activation permission was not granted: pid={processId} error={Marshal.GetLastWin32Error()}");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
