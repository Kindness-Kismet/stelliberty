using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Runtime;

namespace Stelliberty.Tray;

[SupportedOSPlatform("macos")]
internal sealed class MacOSPowerEventSource(Action<SystemPowerEventKind, string> report) : ISystemPowerEventSource
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private static readonly ConcurrentDictionary<nint, MacOSPowerEventSource> Observers = new();
    private static readonly NotificationCallback SuspendCallback = OnSuspend;
    private static readonly NotificationCallback ResumeCallback = OnResume;
    private static readonly Lazy<nint> ObserverClass = new(CreateObserverClass);
    private nint _appKit;
    private nint _center;
    private nint _observer;
    private volatile bool _isDisposed;

    public string Status => _observer != 0 && !_isDisposed ? "macos-workspace-active" : "stopped";

    public async Task StartAsync()
    {
        // AppKit 对象的创建和移除都放在 Avalonia 已建立的主线程上。
        await Dispatcher.UIThread.InvokeAsync(StartOnUiThread);
    }

    private void StartOnUiThread()
    {
        try
        {
            _appKit = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
            var workspace = SendMessage(GetClass("NSWorkspace"), GetSelector("sharedWorkspace"));
            _center = SendMessage(workspace, GetSelector("notificationCenter"));
            _observer = SendMessage(SendMessage(ObserverClass.Value, GetSelector("alloc")), GetSelector("init"));
            if (_center == 0 || _observer == 0) throw new InvalidOperationException("NSWorkspace power observer could not be created.");
            Observers[_observer] = this;
            Subscribe("NSWorkspaceWillSleepNotification", "stellibertyWillSleep:");
            Subscribe("NSWorkspaceDidWakeNotification", "stellibertyDidWake:");
            AppLogger.Info("Power monitor ready: platform=macos source=NSWorkspace");
        }
        catch
        {
            DisposeOnUiThread();
            throw;
        }
    }

    private void Subscribe(string notification, string selector)
    {
        // AppKit 导出的是 NSString 指针变量，须先解引用再传入通知中心。
        var name = Marshal.ReadIntPtr(NativeLibrary.GetExport(_appKit, notification));
        AddObserver(_center, GetSelector("addObserver:selector:name:object:"), _observer, GetSelector(selector), name, 0);
    }

    private static nint CreateObserverClass()
    {
        var observerClass = AllocateClassPair(GetClass("NSObject"), "StellibertySystemPowerObserver", 0);
        if (observerClass == 0) throw new InvalidOperationException("Power observer class allocation failed.");
        // v@:@ 对应 void(self, selector, notification)，回调委托随类保持存活。
        if (!AddMethod(observerClass, GetSelector("stellibertyWillSleep:"), Marshal.GetFunctionPointerForDelegate(SuspendCallback), "v@:@")
            || !AddMethod(observerClass, GetSelector("stellibertyDidWake:"), Marshal.GetFunctionPointerForDelegate(ResumeCallback), "v@:@"))
        {
            DisposeClassPair(observerClass);
            throw new InvalidOperationException("Power observer callback registration failed.");
        }
        RegisterClassPair(observerClass);
        return observerClass;
    }

    private static void OnSuspend(nint receiver, nint selector, nint notification) => Report(receiver, SystemPowerEventKind.Suspend);

    private static void OnResume(nint receiver, nint selector, nint notification) => Report(receiver, SystemPowerEventKind.Resume);

    private static void Report(nint receiver, SystemPowerEventKind kind)
    {
        if (Observers.TryGetValue(receiver, out var source) && !source._isDisposed)
        {
            source.Report(kind);
        }
    }

    private void Report(SystemPowerEventKind kind) => report(kind, "macos.workspace");

    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;
        await Dispatcher.UIThread.InvokeAsync(DisposeOnUiThread);
    }

    private void DisposeOnUiThread()
    {
        if (_observer != 0)
        {
            Observers.TryRemove(_observer, out _);
            if (_center != 0) RemoveObserver(_center, GetSelector("removeObserver:"), _observer);
            SendMessage(_observer, GetSelector("release"));
            _observer = 0;
        }
        _center = 0;
        if (_appKit != 0)
        {
            NativeLibrary.Free(_appKit);
            _appKit = 0;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NotificationCallback(nint receiver, nint selector, nint notification);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
    private static extern nint GetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
    private static extern nint GetSelector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_allocateClassPair")]
    private static extern nint AllocateClassPair(nint superClass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nuint extraBytes);

    [DllImport(ObjectiveCLibrary, EntryPoint = "class_addMethod")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AddMethod(nint cls, nint selector, nint implementation, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_registerClassPair")]
    private static extern void RegisterClassPair(nint cls);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_disposeClassPair")]
    private static extern void DisposeClassPair(nint cls);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendMessage(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void AddObserver(nint receiver, nint selector, nint observer, nint callback, nint name, nint obj);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void RemoveObserver(nint receiver, nint selector, nint observer);
}
