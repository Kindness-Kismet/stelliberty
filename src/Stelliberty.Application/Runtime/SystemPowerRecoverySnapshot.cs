namespace Stelliberty.Application.Runtime;

public enum SystemPowerEventKind
{
    Suspend,
    Resume,
}

public enum SystemPowerRecoveryPhase
{
    Idle,
    Suspended,
    Waiting,
    Recovering,
    Completed,
    Skipped,
    Failed,
    Cancelled,
}

public sealed record SystemPowerRecoverySnapshot(
    long Cycle,
    SystemPowerRecoveryPhase Phase,
    bool WasCoreRunning,
    string Source,
    DateTimeOffset? LastEventAt,
    int Attempt,
    string? Message);
