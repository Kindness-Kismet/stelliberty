using Stelliberty.Application.Proxies;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;
using Stelliberty.Domain.CoreLogs;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Infrastructure.Tray;

public static class TrayProtocol
{
    public const int Version = 7;

    public const string HelloMethod = "tray.hello";
    public const string HealthMethod = "tray.get_health";
    public const string BackgroundStatusMethod = "background.get_status";
    public const string BackgroundChangedEvent = "background.changed";
    public const string ShutdownMethod = "tray.shutdown";
    public const string CoreEnsureStartedMethod = "core.ensure_started";
    public const string CoreStopMethod = "core.stop";
    public const string CoreSnapshotMethod = "core.get_snapshot";
    public const string CoreApplyConfigMethod = "core.apply_config";
    public const string CoreRestartMethod = "core.restart";
    public const string CoreLogsMethod = "core.get_logs";
    public const string RuntimeSnapshotMethod = "runtime.get_snapshot";
    public const string RuntimeResetTrafficMethod = "runtime.reset_traffic";
    public const string SystemProxyStatusMethod = "system_proxy.get_status";
    public const string SystemProxySetEnabledMethod = "system_proxy.set_enabled";
    public const string ServiceModeStatusMethod = "service_mode.get_status";
    public const string ServiceModeInstallMethod = "service_mode.install_or_update";
    public const string ServiceModeUninstallMethod = "service_mode.uninstall";
    public const string HotkeyApplyMethod = "hotkey.apply";
    public const string HotkeySetSuppressedMethod = "hotkey.set_suppressed";
#if DEBUG
    public const string CopyTerminalProxyMethod = "tray.copy_terminal";
    public const string HotkeySimulateMethod = "hotkey.simulate";
#endif
    public const string UiActivateMethod = "ui.activate";
    public const string UiRegisterMethod = "ui.register";
    public const string UiUnregisterMethod = "ui.unregister";
    public const string UiActivationEvent = "ui.activate";
    public const string UiToggleEvent = "ui.toggle";
    public const string CoreStateChangedEvent = "core.state_changed";
    public const string CoreLogEntryEvent = "core.log_entry";
    public const string RuntimeSampledEvent = "runtime.sampled";
    public const string SystemProxyChangedEvent = "system_proxy.changed";
}

public sealed record TrayHelloRequest(
    int ProtocolVersion,
    string AppVersion,
    int ProcessId);

public sealed record TrayHello(
    int ProtocolVersion,
    string AppVersion,
    int TrayPid,
    string TrayEpoch,
    string[] Capabilities,
    long CoreGeneration);

public sealed record TrayHealth(
    int TrayPid,
    string TrayEpoch,
    long UptimeMilliseconds,
    int? UiPid,
    bool IsUiLaunchPending,
    TrayCoreStatus Core,
    long LatestCoreLogSequence,
    DateTimeOffset? LastRuntimeSampledAt,
    SystemProxyStatus SystemProxy);

public sealed record TrayCoreStatus(CoreSnapshot Snapshot, long CoreGeneration);

public sealed record TrayCoreOperationResult(
    bool IsSuccess,
    string Message,
    TrayCoreStatus Status);

public sealed record TrayCoreLogEntry(
    long Sequence,
    long CoreGeneration,
    CoreLogMessage Message);

public sealed record TrayCoreLogBatch(
    TrayCoreLogEntry[] Entries,
    long OldestSequence,
    long LatestSequence,
    bool HasGap);

public sealed record TrayCoreLogsRequest(long AfterSequence);

public sealed record TraySystemProxySetRequest(
    bool IsEnabled,
    SystemProxyApplicationRequest? Request);

public sealed record TrayHotkeyApplyRequest(GlobalHotkeyAction Action, string Gesture);

public sealed record TrayHotkeySuppressionRequest(bool IsSuppressed);

#if DEBUG
public sealed record TrayHotkeySimulationRequest(GlobalHotkeyAction Action);
#endif

public sealed record UiActivateRequest(int LauncherPid);

public sealed record UiActivateResult(
    bool WasLaunched,
    bool WasSignaled,
    bool IsPending);

public sealed record UiRegisterRequest(
    int ProtocolVersion,
    string AppVersion,
    int UiPid,
    string SessionToken);

public sealed record UiRegisterResult(
    string SessionId,
    long WatermarkSequence);

public sealed record UiUnregisterRequest(string SessionId);

public sealed record UiUnregisterResult(bool WasRegistered);
