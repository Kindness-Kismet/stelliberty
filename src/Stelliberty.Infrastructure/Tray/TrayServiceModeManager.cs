using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Tray;

public sealed class TrayServiceModeManager : IServiceModeManager, IDisposable, IAsyncDisposable
{
    private readonly string? _endpoint;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private TrayIpcClient _client;
    private volatile bool _isConnected;
    private bool _hasConnected;
    private bool _isDisposed;

    public TrayServiceModeManager(string? endpoint = null)
    {
        _endpoint = endpoint;
        _client = CreateClient();
    }

    public async Task<ServiceModeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        return await _client.GetServiceModeStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ServiceModeOperationResult> InstallOrUpdateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        return await _client.InstallOrUpdateServiceModeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ServiceModeOperationResult> UninstallAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        return await _client.UninstallServiceModeAsync(cancellationToken).ConfigureAwait(false);
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
                ReplaceClient();
            }

            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await _client.HelloAsync(Environment.ProcessId, cancellationToken).ConfigureAwait(false);
            _isConnected = true;
            _hasConnected = true;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private TrayIpcClient CreateClient()
    {
        var client = new TrayIpcClient(_endpoint);
        client.Disconnected += OnDisconnected;
        return client;
    }

    private void ReplaceClient()
    {
        _client.Disconnected -= OnDisconnected;
        _client.Dispose();
        _client = CreateClient();
    }

    private void OnDisconnected(object? sender, EventArgs args) => _isConnected = false;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _client.Disconnected -= OnDisconnected;
        _client.Dispose();
        _connectGate.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _client.Disconnected -= OnDisconnected;
        await _client.DisposeAsync().ConfigureAwait(false);
        _connectGate.Dispose();
    }
}
