using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Presentation.ViewModels;

public sealed partial class SubscriptionPageViewModel
{
    private SubscriptionItemViewModel ToSubscriptionItem(Subscription subscription, bool isCurrent = false)
    {
        var trafficUsed = subscription.TrafficInfo?.Upload + subscription.TrafficInfo?.Download ?? 0;
        return new SubscriptionItemViewModel(
            subscription.Id,
            subscription.Name,
            subscription.SourceLocation,
            subscription.IsLocalFile,
            subscription.UserAgent,
            subscription.AutoTestDelayIntervalMinutes,
            subscription.AutoUpdateMode,
            subscription.AutoUpdateIntervalMinutes,
            subscription.UpdateProxyMode,
            ageSecretKey: subscription.AgeSecretKey,
            isCurrent: isCurrent,
            createdAt: subscription.CreatedAt,
            lastUpdatedAt: subscription.LastUpdatedAt,
            overrideCount: subscription.OverrideIds.Count,
            chainProxyCount: subscription.BuiltinChainProxyNames.Count + subscription.CustomChainProxies.Count,
            trafficUsed: trafficUsed,
            trafficTotal: subscription.TrafficInfo?.Total ?? 0,
            trafficExpire: subscription.TrafficInfo?.Expire ?? 0,
            lastError: subscription.LastError,
            lastErrorAt: subscription.LastErrorAt,
            sourceFormat: subscription.SourceFormat,
            localization: _localization,
            hasTrafficInfo: subscription.TrafficInfo is not null);
    }

    private static SubscriptionOverrideOptionViewModel ToOverrideOption(OverrideProfile overrideProfile)
    {
        return new SubscriptionOverrideOptionViewModel(
            overrideProfile.Id,
            overrideProfile.Name,
            overrideProfile.Format == OverrideFormat.Yaml ? "YAML" : "JavaScript");
    }
}
