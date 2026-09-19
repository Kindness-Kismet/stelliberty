using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Runtime;

namespace Stelliberty.Tray;

[SupportedOSPlatform("windows")]
internal sealed class WindowsPowerEventSource : ISystemPowerEventSource
{
    // PowrProf 回调无需主窗口，同时支持自动唤醒和用户唤醒通知。
    private const uint DeviceNotifyCallback = 2;
    private const uint PbtApmSuspend = 0x0004;
    private const uint PbtApmResumeAutomatic = 0x0012;
    private const uint PbtApmResumeSuspend = 0x0007;
    private readonly Action<SystemPowerEventKind, string> _report;
    private readonly PowerCallback _callback;
    private nint _registration;

    public WindowsPowerEventSource(Action<SystemPowerEventKind, string> report)
    {
        _report = report;
        _callback = OnPowerNotification;
    }

    public string Status => _registration != 0 ? "windows-powrprof-active" : "stopped";

    public Task StartAsync()
    {
        var parameters = new SubscriptionParameters { Callback = Marshal.GetFunctionPointerForDelegate(_callback) };
        var error = PowerRegisterSuspendResumeNotification(DeviceNotifyCallback, ref parameters, out _registration);
        if (error != 0) throw new Win32Exception((int)error, "Power notification registration failed.");
        AppLogger.Info("Power monitor ready: platform=windows source=PowrProf");
        return Task.CompletedTask;
    }

    private uint OnPowerNotification(nint context, uint eventType, nint setting)
    {
        switch (eventType)
        {
            case PbtApmSuspend:
                _report(SystemPowerEventKind.Suspend, "windows.suspend");
                break;
            case PbtApmResumeAutomatic:
                _report(SystemPowerEventKind.Resume, "windows.resume-automatic");
                break;
            case PbtApmResumeSuspend:
                _report(SystemPowerEventKind.Resume, "windows.resume-user");
                break;
        }
        return 0;
    }

    public ValueTask DisposeAsync()
    {
        var registration = Interlocked.Exchange(ref _registration, 0);
        if (registration != 0)
        {
            var error = PowerUnregisterSuspendResumeNotification(registration);
            if (error != 0) AppLogger.Warning($"Power monitor unregister failed: platform=windows error={error}");
        }
        // 系统解除订阅前必须保持回调委托存活。
        GC.KeepAlive(_callback);
        return ValueTask.CompletedTask;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint PowerCallback(nint context, uint eventType, nint setting);

    [StructLayout(LayoutKind.Sequential)]
    private struct SubscriptionParameters
    {
        public nint Callback;
        public nint Context;
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerRegisterSuspendResumeNotification(uint flags, ref SubscriptionParameters recipient, out nint registration);

    [DllImport("powrprof.dll")]
    private static extern uint PowerUnregisterSuspendResumeNotification(nint registration);
}
