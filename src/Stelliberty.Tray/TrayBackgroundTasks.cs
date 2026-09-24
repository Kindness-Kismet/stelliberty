using Stelliberty.Application.Runtime;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Proxies;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Infrastructure.Tray;
using Stelliberty.Application.Updates;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Infrastructure.Proxies;
using Stelliberty.Infrastructure.Settings;
using Stelliberty.Infrastructure.Subscriptions;
using Stelliberty.Infrastructure.Updates;
using Stelliberty.Infrastructure.DataManagement;
using Stelliberty.Native.Hub;

namespace Stelliberty.Tray;

internal sealed class TrayBackgroundTasks : IAsyncDisposable
{
    private readonly ITrayCoreRuntime _coreRuntime;
    private readonly IProxyDelayResultSink _delayResults;
    private readonly JsonAppSettingsStore _settingsStore = new(new TrayPlatformDirectories());
    private readonly FileSubscriptionStore _subscriptions = new(TrayApplicationLayout.AppDataDirectory);
    private readonly FileSubscriptionSelectionStore _selection = new(TrayApplicationLayout.AppDataDirectory);
    private readonly PipeCoreProxyClient _proxyClient = new(TrayCoreEndpoints.Core);
    private readonly WebDavBackupStore _backupStore = new();
    private readonly SubscriptionAutoDelayPlanner _delayPlanner = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _stateGate = new();
    private BackgroundTaskStatus _status = new(0, 0, null, null, new Dictionary<string, int>());
    private Task? _runTask;

    public TrayBackgroundTasks(ITrayCoreRuntime coreRuntime, IProxyDelayResultSink delayResults)
    {
        _coreRuntime = coreRuntime;
        _delayResults = delayResults;
    }

    public event EventHandler<BackgroundTaskStatus>? StateChanged;

    public BackgroundTaskStatus Status
    {
        get
        {
            lock (_stateGate)
            {
                return _status;
            }
        }
    }

    public void Start(Task coreStartup)
    {
        _runTask = RunAsync(coreStartup, _stopping.Token);
    }

    private async Task RunAsync(Task coreStartup, CancellationToken cancellationToken)
    {
        await coreStartup.ConfigureAwait(false);
        var downloader = new HttpRemoteSubscriptionDownloader(() =>
        {
            var settings = _settingsStore.Load();
            return (settings.ProxyHost, settings.MixedPort);
        });
        var subscriptionRunner = new SubscriptionAutoUpdateRunner(_subscriptions,
            new SubscriptionAutoUpdatePlanner(), new SubscriptionUpdater(_subscriptions, downloader,
                contentDecryptor: new HubSubscriptionContentDecryptor()));
        var updateChecker = new GitHubAppUpdateChecker(() => _settingsStore.Load().AppUpdateChannel, () =>
        {
            var settings = _settingsStore.Load();
            return (settings.ProxyHost, settings.MixedPort);
        });
        var updateScheduler = new AppUpdateAutoCheckScheduler(updateChecker,
            _settingsStore.Load, _settingsStore.Save, () => DateTimeOffset.Now);
        var backupScheduler = new WebDavBackupScheduler(_settingsStore,
            new WebDavDataBackupService(new FileDataBackupService(TrayApplicationLayout.AppDataDirectory), _backupStore));

        await Task.WhenAll(
            // 低频采集提供者额度，界面关闭后仍保留最新记录。
            RunLoopAsync("provider snapshots", TimeSpan.FromSeconds(30), async (_, token) =>
            {
                var revision = await _coreRuntime.RefreshProvidersAsync(token).ConfigureAwait(false);
                if (revision != Status.ProviderRevision)
                {
                    Publish(status => status with { ProviderRevision = revision });
                }
            }, cancellationToken),
            RunLoopAsync("service heartbeat", TimeSpan.FromSeconds(10),
                (_, token) => _coreRuntime.SendHeartbeatAsync(token), cancellationToken),
            RunLoopAsync("subscription updates", TimeSpan.FromMinutes(1), async (startup, token) =>
            {
                var result = startup
                    ? await subscriptionRunner.RunStartupUpdatesAsync(token).ConfigureAwait(false)
                    : await subscriptionRunner.RunDueIntervalUpdatesAsync(DateTimeOffset.Now, token).ConfigureAwait(false);
                if (result.UpdatedSubscriptionIds.Count + result.SkippedSubscriptionIds.Count == 0)
                {
                    return;
                }
                if (_selection.GetCurrentSubscriptionId() is { } selected && result.UpdatedSubscriptionIds.Contains(selected))
                {
                    await _coreRuntime.ApplyCurrentSettingsAsync(token).ConfigureAwait(false);
                }
                Publish(status => status with { SubscriptionRevision = status.SubscriptionRevision + 1 });
            }, cancellationToken),
            RunLoopAsync("subscription delay tests", TimeSpan.FromMinutes(1),
                (_, token) => RunDelayAsync(token), cancellationToken),
            RunLoopAsync("app update checks", TimeSpan.FromMinutes(30), async (startup, token) =>
            {
                var result = startup
                    ? await updateScheduler.CheckOnStartupAsync(token).ConfigureAwait(false)
                    : await updateScheduler.CheckWhenDueAsync(token).ConfigureAwait(false);
                if (result.WasChecked)
                {
                    Publish(status => status with { AppUpdate = result });
                }
            }, cancellationToken),
            RunLoopAsync("scheduled backup", TimeSpan.FromMinutes(10),
                (_, token) => backupScheduler.RunDueAsync(token), cancellationToken)).ConfigureAwait(false);
    }

    private async Task RunDelayAsync(CancellationToken cancellationToken)
    {
        var subscriptionId = _selection.GetCurrentSubscriptionId();
        var subscription = _subscriptions.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId);
        var interval = subscription?.AutoTestDelayIntervalMinutes ?? 0;
        if (_delayPlanner.Evaluate(subscriptionId, interval, DateTimeOffset.Now) != SubscriptionAutoDelayDecision.Due)
        {
            return;
        }

        try
        {
            using var tester = new PipeCoreProxyDelayTester(TrayCoreEndpoints.Core, () => _settingsStore.Load().DelayTestUrl, 5000);
            var config = await new MihomoApiProxyConfigProvider(_proxyClient).LoadAsync(cancellationToken).ConfigureAwait(false);
            var result = await new ProxyDelayService(tester, _delayResults).TestAllAsync(config, cancellationToken).ConfigureAwait(false);
            if (_selection.GetCurrentSubscriptionId() != subscriptionId)
            {
                return;
            }
            await new ProxySelectionService(_proxyClient,
                new FileProxySelectionStore(TrayApplicationLayout.AppDataDirectory), _selection)
                .ReleaseFixedSelectionsAsync(result.Config, null, true, cancellationToken).ConfigureAwait(false);
            var delays = result.TestedNodeNames.ToDictionary(name => name, name =>
            {
                result.Config.TryGetEntryDelay(name, out var delay);
                return delay ?? -1;
            });
            Publish(status => status with { DelaySubscriptionId = subscriptionId, Delays = delays });
        }
        finally
        {
            _delayPlanner.CompleteRun(interval, DateTimeOffset.Now);
        }
    }

    private static async Task RunLoopAsync(string name, TimeSpan interval,
        Func<bool, CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        var startup = true;
        try
        {
            do
            {
                try
                {
                    await action(startup, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    AppLogger.Warning($"Background {name} failed: {exception.Message}");
                }
                startup = false;
            } while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Publish(Func<BackgroundTaskStatus, BackgroundTaskStatus> update)
    {
        BackgroundTaskStatus status;
        lock (_stateGate)
        {
            _status = update(_status) with { Revision = _status.Revision + 1 };
            status = _status;
        }
        StateChanged?.Invoke(this, status);
    }

    public async ValueTask DisposeAsync()
    {
        _stopping.Cancel();
        if (_runTask is not null)
        {
            await _runTask.ConfigureAwait(false);
        }
        _proxyClient.Dispose();
        _backupStore.Dispose();
        _stopping.Dispose();
    }
}
