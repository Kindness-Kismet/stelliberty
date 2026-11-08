using Stelliberty.Application.Connections;
using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Connections;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Desktop.Runtime;

internal sealed class RuntimeProxyCoreClient : IProxyCoreClient, IRuntimeSnapshotClient, IDisposable
{
    private readonly IProxyCoreClient _inner;
    private readonly RuntimeTrafficMonitor _monitor;

    public RuntimeProxyCoreClient(IProxyCoreClient inner, RuntimeTrafficMonitor monitor)
    {
        _inner = inner;
        _monitor = monitor;
    }

    public Task<IReadOnlyList<ConnectionInfo>?> GetConnectionsAsync(CancellationToken cancellationToken = default) =>
        _inner.GetConnectionsAsync(cancellationToken);

    public Task<bool> ChangeProxyAsync(ProxyChangeRequest request, CancellationToken cancellationToken = default) =>
        _inner.ChangeProxyAsync(request, cancellationToken);

    public Task<bool> ClearProxySelectionAsync(string groupName, CancellationToken cancellationToken = default) =>
        _inner.ClearProxySelectionAsync(groupName, cancellationToken);

    public Task<bool> CloseConnectionsAsync(ConnectionCloseRequest request, CancellationToken cancellationToken = default) =>
        _inner.CloseConnectionsAsync(request, cancellationToken);

    public Task<ProxyRuntimeSnapshot> GetProxiesAsync(CancellationToken cancellationToken = default) =>
        _inner.GetProxiesAsync(cancellationToken);

    public Task<OutboundMode?> GetOutboundModeAsync(CancellationToken cancellationToken = default) =>
        _inner.GetOutboundModeAsync(cancellationToken);

    public Task<bool> SetOutboundModeAsync(OutboundMode mode, CancellationToken cancellationToken = default) =>
        _inner.SetOutboundModeAsync(mode, cancellationToken);

    public Task<string?> GetVersionAsync(CancellationToken cancellationToken = default) =>
        _inner.GetVersionAsync(cancellationToken);

    public async Task<CoreRuntimeStats?> GetRuntimeStatsAsync(CancellationToken cancellationToken = default)
    {
        return (await GetRuntimeSnapshotAsync(cancellationToken).ConfigureAwait(false)).Stats;
    }

    public async Task<CoreTrafficRate?> GetTrafficAsync(CancellationToken cancellationToken = default)
    {
        var stats = (await GetRuntimeSnapshotAsync(cancellationToken).ConfigureAwait(false)).Stats;
        return stats is null ? null : new CoreTrafficRate(stats.UploadSpeed, stats.DownloadSpeed);
    }

    public async Task<CoreRuntimeSnapshot> GetRuntimeSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _monitor.GetRuntimeSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Stats is not null)
        {
            return snapshot;
        }

        var stats = await _inner.GetRuntimeStatsAsync(cancellationToken).ConfigureAwait(false);
        var mode = await _inner.GetOutboundModeAsync(cancellationToken).ConfigureAwait(false);
        var version = await _inner.GetVersionAsync(cancellationToken).ConfigureAwait(false);
        return new CoreRuntimeSnapshot(
            stats,
            mode,
            version,
            stats?.ConnectionCount ?? 0,
            [],
            null,
            snapshot.CoreGeneration);
    }

    public Task ResetRuntimeTrafficAsync(CancellationToken cancellationToken = default) =>
        _monitor.ResetRuntimeTrafficAsync(cancellationToken);

    public void Dispose()
    {
        if (_inner is IDisposable disposable) disposable.Dispose();
    }
}
