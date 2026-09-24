using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Application.Subscriptions;

public sealed record SubscriptionProviderSnapshot(
    string SubscriptionId,
    string ContentFingerprint,
    IReadOnlyList<SubscriptionProvider> Providers,
    DateTimeOffset? ObservedAt = null,
    bool IsCurrent = false,
    bool IsCached = true)
{
    public SubscriptionProviderTrafficSummary GetTrafficSummary() => SubscriptionProviderTrafficSummary.Calculate(Providers);
}

public interface ISubscriptionProviderSnapshotStore
{
    SubscriptionProviderSnapshot? Load(string subscriptionId);

    void Save(SubscriptionProviderSnapshot snapshot);
}

public interface ISubscriptionProviderSource
{
    Task<SubscriptionProviderSnapshot> ReadAsync(string subscriptionId, CancellationToken cancellationToken = default);

    Task SyncAsync(string subscriptionId, string providerType, string providerName, CancellationToken cancellationToken = default);
}
