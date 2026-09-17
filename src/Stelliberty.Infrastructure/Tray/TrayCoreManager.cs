using System.Text.Json;
using Stelliberty.Application.CoreLogs;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Runtime;
using Stelliberty.Domain.CoreLogs;

namespace Stelliberty.Infrastructure.Tray;

public sealed class TrayCoreManager : IReadyCoreManager, IDisposable, IAsyncDisposable
{
    private readonly string? _endpoint;
    private TrayIpcClient _client;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly object _logGate = new();
    private readonly List<TrayCoreLogEntry> _pendingInitialLogs = [];
    private CoreSnapshot _lastSnapshot = new(CoreState.Unavailable, null, TrayCoreEndpoints.Core, null);
    private long _lastLogSequence;
    private bool _isBackfillingLogs = true;
    private volatile bool _isConnected;
    private bool _hasConnected;
    private bool _isDisposed;
    private string? _trayEpoch;

    public TrayCoreManager(string? endpoint = null)
    {
        _endpoint = endpoint;
        _client = CreateClient();
    }

    public event EventHandler<CoreSnapshot>? StateChanged;

    public event EventHandler<CoreLogMessage>? CoreLogReceived;

    public async Task<TrayCoreOperationResult> EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var result = await _client.EnsureCoreStartedAsync(cancellationToken).ConfigureAwait(false);
        PublishSnapshot(result.Status.Snapshot);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Message);
        }

        return result;
    }

    async Task IReadyCoreManager.EnsureReadyAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrayCoreOperationResult> StopCoreAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var result = await _client.StopCoreAsync(cancellationToken).ConfigureAwait(false);
        PublishSnapshot(result.Status.Snapshot);
        return result;
    }

    public async Task<CoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var status = await _client.GetCoreStatusAsync(cancellationToken).ConfigureAwait(false);
        PublishSnapshot(status.Snapshot);
        return status.Snapshot;
    }

    public async Task<CoreApplyConfigResult> ApplyConfigAsync(
        CoreApplyConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        return await _client.ApplyCoreConfigAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var status = await _client.RestartCoreAsync(cancellationToken).ConfigureAwait(false);
        PublishSnapshot(status.Snapshot);
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

            lock (_logGate)
            {
                _isBackfillingLogs = true;
            }

            if (_hasConnected)
            {
                ReplaceClient();
            }

            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var hello = await _client.HelloAsync(Environment.ProcessId, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(_trayEpoch, hello.TrayEpoch, StringComparison.Ordinal))
            {
                lock (_logGate)
                {
                    _lastLogSequence = 0;
                    _pendingInitialLogs.Clear();
                }
                _trayEpoch = hello.TrayEpoch;
            }
            _isConnected = true;
            _hasConnected = true;
            try
            {
                var batch = await _client.GetCoreLogsAsync(_lastLogSequence, cancellationToken).ConfigureAwait(false);
                CompleteLogBackfill(batch);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                AppLogger.Warning($"Tray core log backfill failed: {exception.Message}");
                CompleteLogBackfill(new TrayCoreLogBatch([], 0, 0, false));
            }
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private void CompleteLogBackfill(TrayCoreLogBatch batch)
    {
        TrayCoreLogEntry[] entries;
        lock (_logGate)
        {
            entries = batch.Entries
                .Concat(_pendingInitialLogs)
                .Where(entry => entry.Sequence > _lastLogSequence)
                .DistinctBy(entry => entry.Sequence)
                .OrderBy(entry => entry.Sequence)
                .ToArray();
            _pendingInitialLogs.Clear();
            _isBackfillingLogs = false;
            if (entries.Length > 0)
            {
                _lastLogSequence = entries[^1].Sequence;
            }
        }

        foreach (var entry in entries)
        {
            CoreLogReceived?.Invoke(this, entry.Message);
        }
    }

    private void OnCoreStateChanged(object? sender, TrayCoreStatus status) =>
        PublishSnapshot(status.Snapshot);

    private void OnDisconnected(object? sender, EventArgs args) => _isConnected = false;

    private TrayIpcClient CreateClient()
    {
        var client = new TrayIpcClient(_endpoint);
        client.CoreStateChanged += OnCoreStateChanged;
        client.CoreLogReceived += OnCoreLogReceived;
        client.Disconnected += OnDisconnected;
        return client;
    }

    private void ReplaceClient()
    {
        DetachClient(_client);
        _client.Dispose();
        _client = CreateClient();
    }

    private void DetachClient(TrayIpcClient client)
    {
        client.CoreStateChanged -= OnCoreStateChanged;
        client.CoreLogReceived -= OnCoreLogReceived;
        client.Disconnected -= OnDisconnected;
    }

    private void PublishSnapshot(CoreSnapshot snapshot)
    {
        if (snapshot == _lastSnapshot)
        {
            return;
        }

        _lastSnapshot = snapshot;
        StateChanged?.Invoke(this, snapshot);
    }

    private void OnCoreLogReceived(object? sender, TrayCoreLogEntry entry)
    {
        lock (_logGate)
        {
            if (_isBackfillingLogs)
            {
                _pendingInitialLogs.Add(entry);
                return;
            }

            if (entry.Sequence <= _lastLogSequence)
            {
                return;
            }

            _lastLogSequence = entry.Sequence;
        }

        CoreLogReceived?.Invoke(this, entry.Message);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        DetachClient(_client);
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
        DetachClient(_client);
        await _client.DisposeAsync().ConfigureAwait(false);
        _connectGate.Dispose();
    }
}
