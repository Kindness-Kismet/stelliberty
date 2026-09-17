using Stelliberty.Infrastructure.Tray;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Proxies;
using Stelliberty.Application.Runtime;
using Stelliberty.Infrastructure.Proxies;

namespace Stelliberty.Tray;

internal interface ITrayRuntimeMonitor
{
    event EventHandler<CoreRuntimeSample>? Sampled;

    CoreRuntimeSnapshot GetSnapshot();

    Task ResetTrafficAsync(CancellationToken cancellationToken);
}

internal sealed class RuntimeTrafficMonitor : ITrayRuntimeMonitor, IAsyncDisposable
{
    internal const int HistoryCapacity = 60;

    private readonly ITrayCoreRuntime _coreRuntime;
    private readonly IProxyCoreClient _coreClient;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _stateGate = new();
    private readonly TrafficRateTracker _trafficTracker = new();
    private readonly Queue<CoreRuntimeSample> _history = new(HistoryCapacity);
    private readonly CancellationTokenSource _stopping = new();
    private CoreRuntimeSnapshot _snapshot = new(null, null, null, 0, [], null, 0);
    private Task? _runTask;

    public RuntimeTrafficMonitor(
        ITrayCoreRuntime coreRuntime,
        IProxyCoreClient? coreClient = null,
        Func<DateTimeOffset>? now = null)
    {
        _coreRuntime = coreRuntime;
        _coreClient = coreClient ?? new PipeCoreProxyClient(TrayCoreEndpoints.Core);
        _now = now ?? (() => DateTimeOffset.Now);
        _coreRuntime.StateChanged += OnCoreStateChanged;
    }

    public event EventHandler<CoreRuntimeSample>? Sampled;

    public void Start(CancellationToken cancellationToken)
    {
        if (_runTask is not null)
        {
            throw new InvalidOperationException("Runtime traffic monitor is already running.");
        }

        _runTask = RunAsync(cancellationToken);
    }

    public CoreRuntimeSnapshot GetSnapshot()
    {
        lock (_stateGate)
        {
            return _snapshot with { History = [.. _history] };
        }
    }

    public Task ResetTrafficAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateGate)
        {
            _trafficTracker.ResetBaseline();
            _history.Clear();
            var stats = _snapshot.Stats;
            _snapshot = _snapshot with
            {
                Stats = stats is null
                    ? null
                    : stats with
                    {
                        UploadSpeed = 0,
                        DownloadSpeed = 0,
                        UploadTotal = 0,
                        DownloadTotal = 0,
                    },
                History = [],
            };
        }

        return Task.CompletedTask;
    }

    internal async Task SampleOnceAsync(CancellationToken cancellationToken)
    {
        var coreStatus = _coreRuntime.CurrentStatus;
        if (coreStatus.Snapshot.State != CoreState.Running)
        {
            return;
        }

        var statsTask = _coreClient.GetRuntimeStatsAsync(cancellationToken);
        var modeTask = _coreClient.GetOutboundModeAsync(cancellationToken);
        var shouldReadVersion = string.IsNullOrWhiteSpace(GetSnapshot().Version);
        var versionTask = shouldReadVersion
            ? _coreClient.GetVersionAsync(cancellationToken)
            : Task.FromResult<string?>(null);
        await Task.WhenAll(statsTask, modeTask, versionTask).ConfigureAwait(false);
        var stats = await statsTask.ConfigureAwait(false);
        if (stats is null)
        {
            return;
        }
        var mode = await modeTask.ConfigureAwait(false);
        var version = await versionTask.ConfigureAwait(false);

        var sampledAt = _now();
        CoreRuntimeSample sample;
        lock (_stateGate)
        {
            var currentCore = _coreRuntime.CurrentStatus;
            if (coreStatus.CoreGeneration != currentCore.CoreGeneration
                || currentCore.Snapshot.State != CoreState.Running)
            {
                return;
            }

            var tracked = _trafficTracker.Update(stats.UploadTotal, stats.DownloadTotal, sampledAt);
            sample = new CoreRuntimeSample(
                sampledAt,
                coreStatus.CoreGeneration,
                stats.HasTrafficRate ? stats.UploadSpeed : tracked.UploadSpeed,
                stats.HasTrafficRate ? stats.DownloadSpeed : tracked.DownloadSpeed,
                tracked.UploadTotal,
                tracked.DownloadTotal);
            if (_history.Count >= HistoryCapacity)
            {
                _history.Dequeue();
            }

            _history.Enqueue(sample);
            var effectiveStats = stats with
            {
                UploadSpeed = sample.UploadSpeed,
                DownloadSpeed = sample.DownloadSpeed,
                UploadTotal = sample.UploadTotal,
                DownloadTotal = sample.DownloadTotal,
                HasTrafficRate = true,
            };
            _snapshot = new CoreRuntimeSnapshot(
                effectiveStats,
                mode,
                version ?? _snapshot.Version,
                effectiveStats.ConnectionCount,
                [.. _history],
                sampledAt,
                coreStatus.CoreGeneration);
        }

        Sampled?.Invoke(this, sample);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(linked.Token).ConfigureAwait(false))
            {
                try
                {
                    await SampleOnceAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    AppLogger.Warning($"Tray runtime sampling failed: {exception.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
    }

    private void OnCoreStateChanged(object? sender, TrayCoreStatus status)
    {
        lock (_stateGate)
        {
            if (status.Snapshot.State == CoreState.Running
                && status.CoreGeneration == _snapshot.CoreGeneration)
            {
                return;
            }

            _trafficTracker.Reset();
            _history.Clear();
            _snapshot = new CoreRuntimeSnapshot(null, null, null, 0, [], null, status.CoreGeneration);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _coreRuntime.StateChanged -= OnCoreStateChanged;
        _stopping.Cancel();
        if (_runTask is not null)
        {
            await _runTask.ConfigureAwait(false);
        }

        if (_coreClient is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _stopping.Dispose();
    }
}
