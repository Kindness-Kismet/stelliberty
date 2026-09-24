using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public sealed class SelectedSubscriptionProviderCatalogLoader(
    ISubscriptionStore subscriptionStore,
    ISubscriptionSelectionStore selectionStore,
    SubscriptionProviderParser parser,
    ISubscriptionProviderSyncer? syncer = null,
    ISubscriptionProviderStateReader? stateReader = null)
{
    public SubscriptionProviderCatalog LoadCatalog()
    {
        return LoadCatalog(selectionStore.GetCurrentSubscriptionId()
            ?? throw new InvalidOperationException("No subscription is selected"));
    }

    public SubscriptionProviderCatalog LoadCatalog(string subscriptionId)
    {
        return new SubscriptionProviderCatalog(LoadProviders(subscriptionId), syncer);
    }

    public async Task<SubscriptionProviderCatalog> LoadCatalogAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        var providers = LoadProviders(subscriptionId);
        if (stateReader is null || !string.Equals(selectionStore.GetCurrentSubscriptionId(), subscriptionId, StringComparison.Ordinal))
        {
            return new SubscriptionProviderCatalog(providers, syncer);
        }

        var states = await stateReader.ReadStatesAsync(cancellationToken);
        if (states.Count == 0)
        {
            return new SubscriptionProviderCatalog(providers, syncer);
        }

        var statesByKey = states.ToDictionary(state => (state.Type, state.Name));
        var merged = providers
            .Select(provider => statesByKey.TryGetValue((provider.Type, provider.Name), out var state)
                ? provider with { Count = state.Count, UpdatedAt = state.UpdatedAt, TrafficInfo = state.TrafficInfo }
                : provider)
            .ToList();
        return new SubscriptionProviderCatalog(merged, syncer);
    }

    private IReadOnlyList<SubscriptionProvider> LoadProviders(string subscriptionId)
    {
        var subscription = subscriptionStore.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId)
            ?? throw new InvalidOperationException($"Selected subscription not found: {subscriptionId}");
        return parser.Parse(subscriptionStore.ReadContent(subscription.Id));
    }
}
