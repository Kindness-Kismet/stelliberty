using Stelliberty.Application.Updates;

namespace Stelliberty.Application.Runtime;

public sealed record BackgroundTaskStatus(
    long Revision,
    long SubscriptionRevision,
    AppUpdateAutoCheckResult? AppUpdate,
    string? DelaySubscriptionId,
    IReadOnlyDictionary<string, int> Delays);
