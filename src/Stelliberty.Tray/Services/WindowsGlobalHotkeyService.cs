using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Tray;

[SupportedOSPlatform("windows")]
internal sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    private const uint WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly GlobalHotkeyActivationController _activationController;
    private readonly WndProc _wndProc; // 保持委托引用，防 GC 回收后回调悬空。
    private readonly string _className;
    private readonly IntPtr _instance;
    private readonly Dictionary<GlobalHotkeyAction, ParsedHotkey> _registeredHotkeys = [];
    private IntPtr _windowHandle;
    private ushort _classAtom;

    public WindowsGlobalHotkeyService(Action<GlobalHotkeyAction> activated)
    {
        _activationController = new GlobalHotkeyActivationController(activated);
        _wndProc = HandleMessage;
        _className = $"StellibertyGlobalHotkey_{Environment.ProcessId}";
        _instance = GetModuleHandle(null);
        Initialize();
    }

    public Task<GlobalHotkeyApplyResult> ApplyAsync(
        GlobalHotkeyAction action,
        string gesture,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Apply(action, gesture));
    }

    private GlobalHotkeyApplyResult Apply(GlobalHotkeyAction action, string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            UnregisterHotkey(action);
            return GlobalHotkeyApplyResult.Success();
        }

        if (_windowHandle == IntPtr.Zero || !TryParse(gesture, out var hotkey))
        {
            return GlobalHotkeyApplyResult.Failure(
                _windowHandle == IntPtr.Zero ? GlobalHotkeyApplyError.Failed : GlobalHotkeyApplyError.Invalid);
        }

        if (_registeredHotkeys.Any(pair => pair.Key != action && pair.Value == hotkey))
        {
            return GlobalHotkeyApplyResult.Failure(GlobalHotkeyApplyError.Duplicate);
        }

        if (_registeredHotkeys.TryGetValue(action, out var current) && current == hotkey)
        {
            return GlobalHotkeyApplyResult.Success();
        }

        ParsedHotkey? previous = _registeredHotkeys.TryGetValue(action, out current) ? current : null;
        UnregisterHotkey(action);
        if (RegisterHotKey(_windowHandle, (int)action, hotkey.Modifiers | ModNoRepeat, hotkey.VirtualKey))
        {
            _registeredHotkeys[action] = hotkey;
            AppLogger.Info($"Global hotkey registered: action={action} gesture={gesture}");
            return GlobalHotkeyApplyResult.Success();
        }

        var error = Marshal.GetLastWin32Error();
        RestorePreviousHotkey(action, previous);
        AppLogger.Warning($"Global hotkey registration failed: action={action} error={error}");
        return GlobalHotkeyApplyResult.Failure(
            error == ErrorHotkeyAlreadyRegistered ? GlobalHotkeyApplyError.Conflict : GlobalHotkeyApplyError.Failed);
    }

    public void Dispose()
    {
        foreach (var action in _registeredHotkeys.Keys.ToArray())
        {
            UnregisterHotkey(action);
        }

        if (_windowHandle != IntPtr.Zero)
        {
            DestroyWindow(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }

        if (_classAtom != 0)
        {
            UnregisterClass(_className, _instance);
            _classAtom = 0;
        }
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
        return Task.FromResult(
            _registeredHotkeys.ContainsKey(action) && _activationController.TryActivate(action));
    }
#endif

    private void Initialize()
    {
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _instance,
            lpszClassName = _className,
        };

        _classAtom = RegisterClassEx(ref windowClass);
        if (_classAtom == 0)
        {
            AppLogger.Warning($"Global hotkey class registration failed: {Marshal.GetLastWin32Error()}");
            return;
        }

        _windowHandle = CreateWindowEx(
            0,
            _className,
            "Stelliberty Global Hotkey",
            0,
            0,
            0,
            0,
            0,
            HwndMessage,
            IntPtr.Zero,
            _instance,
            IntPtr.Zero);
        if (_windowHandle == IntPtr.Zero)
        {
            AppLogger.Warning($"Global hotkey window creation failed: {Marshal.GetLastWin32Error()}");
            UnregisterClass(_className, _instance);
            _classAtom = 0;
        }
    }

    private IntPtr HandleMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmHotkey)
        {
            var action = (GlobalHotkeyAction)wParam.ToInt32();
            if (!_registeredHotkeys.ContainsKey(action))
            {
                return IntPtr.Zero;
            }

            _activationController.TryActivate(action);
            return IntPtr.Zero;
        }

        return DefWindowProc(windowHandle, message, wParam, lParam);
    }

    private void UnregisterHotkey(GlobalHotkeyAction action)
    {
        if (_windowHandle != IntPtr.Zero && _registeredHotkeys.Remove(action))
        {
            UnregisterHotKey(_windowHandle, (int)action);
        }
    }

    private void RestorePreviousHotkey(GlobalHotkeyAction action, ParsedHotkey? previous)
    {
        if (previous is null || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (RegisterHotKey(_windowHandle, (int)action, previous.Value.Modifiers | ModNoRepeat, previous.Value.VirtualKey))
        {
            _registeredHotkeys[action] = previous.Value;
        }
    }

    private static bool TryParse(string gesture, out ParsedHotkey hotkey)
    {
        var tokens = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length is < 2 or > 3)
        {
            hotkey = default;
            return false;
        }

        var modifiers = 0u;
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            var modifier = tokens[index].ToLowerInvariant() switch
            {
                "ctrl" => ModControl,
                "alt" => ModAlt,
                "shift" => ModShift,
                _ => 0u,
            };
            if (modifier == 0 || (modifiers & modifier) != 0)
            {
                hotkey = default;
                return false;
            }

            modifiers |= modifier;
        }

        if (!TryVirtualKey(tokens[^1], out var virtualKey))
        {
            hotkey = default;
            return false;
        }

        hotkey = new ParsedHotkey(modifiers, virtualKey);
        return true;
    }

    private static bool TryVirtualKey(string token, out uint virtualKey)
    {
        if (token.Length == 1 && char.IsAsciiLetterOrDigit(token[0]))
        {
            virtualKey = char.ToUpperInvariant(token[0]);
            return true;
        }

        if (token.Length is 2 or 3
            && token[0] is 'F' or 'f'
            && int.TryParse(token[1..], out var functionKey)
            && functionKey is >= 1 and <= 12)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            return true;
        }

        virtualKey = 0;
        return false;
    }

    private readonly record struct ParsedHotkey(uint Modifiers, uint VirtualKey);

    private delegate IntPtr WndProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "UnregisterClassW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
