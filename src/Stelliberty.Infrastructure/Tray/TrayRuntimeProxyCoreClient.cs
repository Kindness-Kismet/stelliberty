using Stelliberty.Infrastructure.Tray;
using Stelliberty.Application.Connections;
using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Connections;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Infrastructure.Tray;

public sealed class TrayRuntimeProxyCoreClient : IProxyCoreClient, IRuntimeSnapshotClient, IDisposable
{
    private readonly IProxyCoreClient _inner;
    private readonly string? _endpoint;
    private TrayIpcClient _tray;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private volatile bool _isConnected;
    private bool _hasConnected;
    private bool _isDisposed;

    public TrayRuntimeProxyCoreClient(IProxyCoreClient inner, string? endpoint = null)
    {
        _inner = inner;
        _endpoint = endpoint;
        _tray = CreateTrayClient();
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
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await _tray.GetRuntimeSnapshotAsync(cancellationToken).ConfigureAwait(false);
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

    public async Task ResetRuntimeTrafficAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        await _tray.ResetRuntimeTrafficAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isConnected)
        {
            return;
        }

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isConnected)
            {
                return;
            }

            if (_hasConnected)
            {
                _tray.Disconnected -= OnTrayDisconnected;
                _tray.Dispose();
                _tray = CreateTrayClient();
            }

            await _tray.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await _tray.HelloAsync(Environment.ProcessId, cancellationToken).ConfigureAwait(false);
            _isConnected = true;
            _hasConnected = true;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private TrayIpcClient CreateTrayClient()
    {
        var client = new TrayIpcClient(_endpoint);
        client.Disconnected += OnTrayDisconnected;
        return client;
    }

    private void OnTrayDisconnected(object? sender, EventArgs args) => _isConnected = false;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _tray.Disconnected -= OnTrayDisconnected;
        _tray.Dispose();
        if (_inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
        _connectGate.Dispose();
    }
}
