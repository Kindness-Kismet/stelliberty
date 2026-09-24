using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionProviderSnapshotService(
    ISubscriptionStore subscriptions,
    ISubscriptionProviderSnapshotStore snapshots,
    SubscriptionProviderParser parser)
{
    public SubscriptionProviderSnapshot ReadCached(string subscriptionId)
    {
        var content = ReadSubscriptionContent(subscriptionId);
        var fingerprint = SubscriptionProviderParser.Fingerprint(content);
        var cached = snapshots.Load(subscriptionId);
        return cached?.ContentFingerprint == fingerprint
            ? cached with { IsCurrent = false, IsCached = true }
            : new(subscriptionId, fingerprint, parser.Parse(content));
    }

    public SubscriptionProviderSnapshot CreateContext(string subscriptionId, string runtimeConfig, string? contentFingerprint = null)
    {
        var content = ReadSubscriptionContent(subscriptionId);
        return new(subscriptionId, contentFingerprint ?? SubscriptionProviderParser.Fingerprint(content), parser.Parse(runtimeConfig));
    }

    public SubscriptionProviderSnapshot Capture(
        SubscriptionProviderSnapshot context,
        IReadOnlyList<SubscriptionProviderRuntimeState> states,
        DateTimeOffset observedAt)
    {
        var current = ReadCached(context.SubscriptionId);
        if (current.ContentFingerprint != context.ContentFingerprint)
        {
            return current;
        }

        var byKey = states.ToDictionary(state => (state.Type, state.Name));
        var providers = context.Providers.Select(provider => byKey.TryGetValue((provider.Type, provider.Name), out var state)
            ? provider with { Count = state.Count, UpdatedAt = state.UpdatedAt, TrafficInfo = state.TrafficInfo }
            : provider).ToList();
        var snapshot = context with { Providers = providers, ObservedAt = observedAt, IsCurrent = true, IsCached = false };
        if (current.ContentFingerprint != snapshot.ContentFingerprint || !current.Providers.SequenceEqual(providers))
        {
            snapshots.Save(snapshot with { IsCurrent = false, IsCached = true });
        }
        return snapshot;
    }

    private string ReadSubscriptionContent(string subscriptionId)
    {
        if (!subscriptions.LoadSubscriptions().Any(item => item.Id == subscriptionId))
        {
            throw new InvalidOperationException("Subscription no longer exists.");
        }
        return subscriptions.ReadContent(subscriptionId);
    }
}
