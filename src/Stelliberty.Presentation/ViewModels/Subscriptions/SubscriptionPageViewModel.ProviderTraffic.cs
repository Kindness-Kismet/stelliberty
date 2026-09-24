using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Subscriptions;

namespace Stelliberty.Presentation.ViewModels;

public sealed partial class SubscriptionPageViewModel
{
    private readonly Dictionary<string, SubscriptionProviderSnapshot> _providerSnapshots = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _providerLifetime = new();
    private int _providerRefreshVersion;

    public async Task RefreshProviderTrafficAsync()
    {
        if (_providerCatalogLoader is null)
        {
            return;
        }

        var version = ++_providerRefreshVersion;
        foreach (var subscriptionId in _subscriptions.Select(item => item.Id).ToList())
        {
            try
            {
                var catalog = await _providerCatalogLoader.LoadCatalogAsync(subscriptionId, _providerLifetime.Token);
                if (version != _providerRefreshVersion || _providerLifetime.IsCancellationRequested)
                {
                    return;
                }
                if (catalog.Snapshot is { } snapshot)
                {
                    ApplyProviderSnapshot(snapshot);
                }
            }
            catch (OperationCanceledException) when (_providerLifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                AppLogger.Warning($"Subscription provider traffic refresh failed: {exception.Message}");
            }
        }
    }

    private void ApplyProviderSnapshot(SubscriptionProviderSnapshot snapshot)
    {
        var item = _subscriptions.FirstOrDefault(item => item.Id == snapshot.SubscriptionId);
        if (item is null)
        {
            return;
        }
        _providerSnapshots[snapshot.SubscriptionId] = snapshot;
        item.ApplyProviderSnapshot(snapshot);
        if (item.IsCurrent)
        {
            NotifyHomeCardPresentationChanged();
        }
    }
}
