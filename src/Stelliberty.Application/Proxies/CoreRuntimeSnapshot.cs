using Stelliberty.Domain.Proxies;

namespace Stelliberty.Application.Proxies;

public sealed record CoreRuntimeSample(
    DateTimeOffset SampledAt,
    long CoreGeneration,
    long UploadSpeed,
    long DownloadSpeed,
    long UploadTotal,
    long DownloadTotal);

public sealed record CoreRuntimeSnapshot(
    CoreRuntimeStats? Stats,
    OutboundMode? Mode,
    string? Version,
    int ConnectionCount,
    CoreRuntimeSample[] History,
    DateTimeOffset? SampledAt,
    long CoreGeneration);
