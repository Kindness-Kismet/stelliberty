namespace Stelliberty.Application.Runtime;

public interface ICorePowerRecovery
{
    CorePowerRecoveryContext CaptureContext();

    Task<CorePowerRecoveryResult> RecoverAsync(CorePowerRecoveryContext context, CancellationToken cancellationToken);
}

public sealed record CorePowerRecoveryContext(bool WasRunning, long OperationRevision, long CoreRevision);

public enum CorePowerRecoveryOutcome
{
    Recovered,
    Skipped,
    Retry,
}

public sealed record CorePowerRecoveryResult(CorePowerRecoveryOutcome Outcome, string Message);
