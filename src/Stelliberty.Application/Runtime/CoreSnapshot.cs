namespace Stelliberty.Application.Runtime;

public sealed record CoreSnapshot(
    CoreState State,
    int? Pid,
    string ExternalController,
    string? LastError,
    DateTimeOffset? RunningSince = null);
