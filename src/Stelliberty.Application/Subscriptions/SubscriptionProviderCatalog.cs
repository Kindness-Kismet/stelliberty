using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionProviderCatalog(
    IReadOnlyList<SubscriptionProvider> providers,
    ISubscriptionProviderSyncer? syncer = null,
    SubscriptionProviderSnapshot? snapshot = null)
{
    public SubscriptionProviderSnapshot? Snapshot { get; } = snapshot;

    private readonly List<string> _syncedProviderNames = [];

    public IReadOnlyList<SubscriptionProvider> VisibleProviders => providers.Where(item => item.IsVisible).ToList();

    public IReadOnlyList<SubscriptionProvider> Search(string keyword)
    {
        return VisibleProviders
            .Where(item => Contains(item.Name, keyword) || Contains(item.Path, keyword))
            .ToList();
    }

    public void MarkSynced(IReadOnlyList<string> providerNames)
    {
        foreach (var providerName in providerNames)
        {
            if (!_syncedProviderNames.Contains(providerName))
            {
                _syncedProviderNames.Add(providerName);
            }
        }
    }

    public async Task<SubscriptionProviderSyncResult> SyncProviderAsync(string providerName, CancellationToken cancellationToken = default)
    {
        var provider = providers.FirstOrDefault(item => item.Name == providerName);
        if (provider is null)
        {
            return new SubscriptionProviderSyncResult([], []);
        }

        if (!provider.CanSync || syncer is null || _syncedProviderNames.Contains(provider.Name))
        {
            return new SubscriptionProviderSyncResult([], [provider.Name]);
        }

        await syncer.SyncAsync(provider, cancellationToken);
        _syncedProviderNames.Add(provider.Name);
        return new SubscriptionProviderSyncResult([provider.Name], []);
    }

    public async Task ReloadProviderAsync(string providerName, CancellationToken cancellationToken = default)
    {
        var provider = providers.FirstOrDefault(item => item.Name == providerName);
        if (provider is null || syncer is null)
        {
            return;
        }

        await syncer.SyncAsync(provider, cancellationToken);
    }

    public async Task<SubscriptionProviderSyncResult> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var synced = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();

        foreach (var provider in providers)
        {
            if (!provider.CanSync || syncer is null || _syncedProviderNames.Contains(provider.Name))
            {
                skipped.Add(provider.Name);
                continue;
            }

            try
            {
                await syncer.SyncAsync(provider, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 用户取消会停止批处理；单项超时继续处理。
                throw;
            }
            catch (Exception)
            {
                failed.Add(provider.Name);
                continue;
            }

            _syncedProviderNames.Add(provider.Name);
            synced.Add(provider.Name);
        }

        return new SubscriptionProviderSyncResult(synced, skipped) { FailedProviderNames = failed };
    }

    private static bool Contains(string value, string keyword)
    {
        return value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
