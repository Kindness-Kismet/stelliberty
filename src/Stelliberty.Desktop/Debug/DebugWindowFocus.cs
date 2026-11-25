#if DEBUG
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Stelliberty.Desktop.Debug;

internal sealed class DebugWindowFocus : IDisposable
{
    // Win32 窗口样式、位置消息与显示命令采用 user32 的固定值。
    private const int GwlStyle = -16, GwlExStyle = -20;
    private const long WsChild = 0x40000000, WsPopup = 0x80000000, WsExTopmost = 0x00000008;
    private const uint WmWindowPosChanging = 0x0046;
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010, SwpFrameChanged = 0x0020;
    private const int SwMaximize = 3, SwShowNoActivate = 4, SwShowMinNoActive = 7;

    private readonly Window _window;
    private readonly bool _originalShowActivated;
    private readonly Win32Properties.CustomWndProcHookCallback _hook;
    private bool _backgroundRequest;

    public DebugWindowFocus(Window window)
    {
        Dispatcher.UIThread.VerifyAccess();
        _window = window;
        _originalShowActivated = window.ShowActivated;
        _hook = OnWndProc;
        if (OperatingSystem.IsWindows()) Win32Properties.AddWndProcHookCallback(window, _hook);
    }

    public bool IsNoFocus { get; private set; }
    public string Mode => IsNoFocus ? "no-focus" : "focus";

    public void SetMode(string mode)
    {
        Dispatcher.UIThread.VerifyAccess();
        IsNoFocus = mode switch
        {
            "focus" => false,
            "no-focus" => true,
            _ => throw new InvalidOperationException("Focus mode must be focus or no-focus"),
        };
        _window.ShowActivated = IsNoFocus ? false : _originalShowActivated;
    }

    public void ShowInitial(Action show)
    {
        Run(() =>
        {
            var restoreMaximized = IsNoFocus && OperatingSystem.IsWindows()
                && _window.WindowState == WindowState.Maximized;
            // 后台首次显示先走普通窗口生命周期，再恢复原生最大化状态。
            if (restoreMaximized) _window.WindowState = WindowState.Normal;
            show();
            if (restoreMaximized) SetState(WindowState.Maximized);
        });
    }

    public void Show()
    {
        Run(() =>
        {
            var wasMinimized = _window.WindowState == WindowState.Minimized;
            _window.Show();
            if (wasMinimized) SetState(WindowState.Normal);
            if (!IsNoFocus) _window.Activate();
        });
    }

    public void SetState(WindowState state)
    {
        Run(() =>
        {
            if (!UseNativeActions || !_window.IsVisible)
            {
                _window.WindowState = state;
                // Windows 的最小化还原会先恢复此前的最大化状态。
                if (OperatingSystem.IsWindows() && state == WindowState.Normal && _window.WindowState == WindowState.Maximized)
                {
                    _window.WindowState = WindowState.Maximized;
                    _window.WindowState = WindowState.Normal;
                }
                return;
            }

            var hwnd = Handle;
            switch (state)
            {
                case WindowState.Minimized:
                    ShowWindow(hwnd, SwShowMinNoActive);
                    break;
                case WindowState.Normal:
                    ShowWindow(hwnd, SwShowNoActivate);
                    // 最小化前为最大化时，第一次还原会先恢复最大化。
                    if (IsZoomed(hwnd)) ShowWindow(hwnd, SwShowNoActivate);
                    break;
                case WindowState.Maximized:
                    Maximize(hwnd);
                    break;
            }
        });
    }

    public void Move(int x, int y)
    {
        Run(() =>
        {
            if (UseNativeActions) PositionWindow(x, y, 0, 0, SwpNoSize | SwpNoZOrder);
            else _window.Position = new PixelPoint(x, y);
        });
    }

    public void Resize(double width, double height)
    {
        Run(() =>
        {
            SetState(WindowState.Normal);
            width = Math.Clamp(width, _window.MinWidth, _window.MaxWidth);
            height = Math.Clamp(height, _window.MinHeight, _window.MaxHeight);
            if (!UseNativeActions)
            {
                _window.Width = width;
                _window.Height = height;
            }
            else
            {
                var hwnd = Handle;
                if (!GetWindowRect(hwnd, out var outer))
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                // 扩展客户区包含标题栏，外框差值按 Avalonia 实际内容尺寸换算。
                var pixelWidth = outer.Width + checked((int)Math.Round((width - _window.ClientSize.Width) * _window.RenderScaling));
                var pixelHeight = outer.Height + checked((int)Math.Round((height - _window.ClientSize.Height) * _window.RenderScaling));
                PositionWindow(0, 0, pixelWidth, pixelHeight, SwpNoMove | SwpNoZOrder);
            }
            _window.UpdateLayout();
        });
    }

    private bool UseNativeActions => IsNoFocus && OperatingSystem.IsWindows();

    private IntPtr Handle => _window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle
        ? handle.Handle : throw new InvalidOperationException("The window has no Windows HWND");

    private void Run(Action action)
    {
        Dispatcher.UIThread.VerifyAccess();
        var previous = _backgroundRequest;
        _backgroundRequest = IsNoFocus;
        try { action(); }
        finally { _backgroundRequest = previous; }
    }

    private void Maximize(IntPtr hwnd)
    {
        if (IsZoomed(hwnd)) return;
        if (IsIconic(hwnd)) ShowWindow(hwnd, SwShowNoActivate);
        var original = ReadWindowLong(hwnd, GwlStyle).ToInt64();
        // 临时 WS_CHILD 避开顶层最大化的激活路径；同步完成后只恢复窗口类型位。
        WriteWindowStyle(hwnd, (original | WsChild) & ~WsPopup);
        try { ShowWindow(hwnd, SwMaximize); }
        finally
        {
            var current = ReadWindowLong(hwnd, GwlStyle).ToInt64();
            WriteWindowStyle(hwnd, (current & ~(WsChild | WsPopup)) | (original & (WsChild | WsPopup)));
        }
        PositionWindow(0, 0, 0, 0, SwpFrameChanged | SwpNoZOrder | SwpNoMove | SwpNoSize);
    }

    private void PositionWindow(int x, int y, int width, int height, uint flags)
    {
        // WM_WINDOWPOSCHANGING 中追加 NOACTIVATE 无效，必须在原生请求入口传入。
        if (!SetWindowPos(Handle, IntPtr.Zero, x, y, width, height, flags | SwpNoActivate))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    private IntPtr OnWndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_backgroundRequest && message == WmWindowPosChanging)
        {
            var foreground = GetForegroundWindow();
            if (foreground != hwnd)
            {
                var position = Marshal.PtrToStructure<WindowPos>(lParam);
                var isTopmost = foreground != IntPtr.Zero
                    && (ReadWindowLong(foreground, GwlExStyle).ToInt64() & WsExTopmost) != 0;
                position.InsertAfter = foreground == IntPtr.Zero || isTopmost ? new IntPtr(1) : foreground;
                position.Flags &= ~SwpNoZOrder;
                Marshal.StructureToPtr(position, lParam, false);
            }
        }
        // 保留 Avalonia 的状态、几何与渲染通知。
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (OperatingSystem.IsWindows()) Win32Properties.RemoveWndProcHookCallback(_window, _hook);
        _window.ShowActivated = _originalShowActivated;
    }

    private static IntPtr ReadWindowLong(IntPtr hwnd, int index)
    {
        SetLastError(0);
        var value = IntPtr.Size == 8 ? GetWindowLongPtrW(hwnd, index) : new IntPtr(GetWindowLongW(hwnd, index));
        var error = Marshal.GetLastPInvokeError();
        if (value == IntPtr.Zero && error != 0) throw new Win32Exception(error);
        return value;
    }

    private static void WriteWindowStyle(IntPtr hwnd, long style)
    {
        SetLastError(0);
        var previous = IntPtr.Size == 8 ? SetWindowLongPtrW(hwnd, GwlStyle, new IntPtr(style))
            : new IntPtr(SetWindowLongW(hwnd, GwlStyle, unchecked((int)style)));
        var error = Marshal.GetLastPInvokeError();
        if (previous == IntPtr.Zero && error != 0) throw new Win32Exception(error);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd, InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLongW(IntPtr hwnd, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLongW(IntPtr hwnd, int index, int value);
    [DllImport("kernel32.dll")] private static extern void SetLastError(uint error);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
}
#endif
