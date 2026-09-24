using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public sealed class SelectedSubscriptionProviderCatalogLoader(
    SubscriptionProviderSnapshotService snapshots,
    ISubscriptionProviderSource source)
{
    public SubscriptionProviderCatalog LoadCatalog(string subscriptionId)
    {
        return CreateCatalog(snapshots.ReadCached(subscriptionId));
    }

    public async Task<SubscriptionProviderCatalog> LoadCatalogAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        return CreateCatalog(await source.ReadAsync(subscriptionId, cancellationToken));
    }

    private SubscriptionProviderCatalog CreateCatalog(SubscriptionProviderSnapshot snapshot)
    {
        return new(snapshot.Providers, snapshot.IsCurrent ? new ScopedSyncer(source, snapshot.SubscriptionId) : null, snapshot);
    }

    private sealed class ScopedSyncer(ISubscriptionProviderSource source, string subscriptionId) : ISubscriptionProviderSyncer
    {
        public Task SyncAsync(SubscriptionProvider provider, CancellationToken cancellationToken = default) =>
            source.SyncAsync(subscriptionId, provider.Type, provider.Name, cancellationToken);
    }
}
