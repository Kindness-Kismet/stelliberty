using Stelliberty.Domain.Proxies;

namespace Stelliberty.Application.Proxies;

public sealed record CoreRuntimeSnapshotData(
    CoreRuntimeStats? Stats,
    OutboundMode? Mode,
    string? Version,
    int ConnectionCount,
    CoreRuntimeSample[] History);

public sealed class CoreRuntimeSnapshotLoader(IProxyCoreClient client)
{
    // 外部取消返回 null，让调用方跳过本次快照。
    public async Task<CoreRuntimeSnapshotData?> LoadAsync(bool includeVersion, CancellationToken cancellationToken = default)
    {
        if (client is IRuntimeSnapshotClient runtimeClient)
        {
            var snapshot = await runtimeClient.GetRuntimeSnapshotAsync(cancellationToken);
            return new CoreRuntimeSnapshotData(
                snapshot.Stats,
                snapshot.Mode,
                includeVersion ? snapshot.Version : null,
                snapshot.ConnectionCount,
                snapshot.History);
        }

        var stats = await client.GetRuntimeStatsAsync(cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var mode = await client.GetOutboundModeAsync(cancellationToken);
        var version = includeVersion ? await client.GetVersionAsync(cancellationToken) : null;
        return new CoreRuntimeSnapshotData(stats, mode, version, stats?.ConnectionCount ?? 0, []);
    }
}
