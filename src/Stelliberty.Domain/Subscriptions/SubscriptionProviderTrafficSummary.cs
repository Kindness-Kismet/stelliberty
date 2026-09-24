namespace Stelliberty.Domain.Subscriptions;

public sealed record SubscriptionProviderTrafficSummary(
    int ProviderCount,
    int AvailableProviderCount,
    SubscriptionTrafficInfo? TrafficInfo,
    bool HasCompleteExpiry)
{
    public static SubscriptionProviderTrafficSummary Calculate(IEnumerable<SubscriptionProvider> providers)
    {
        var remoteProviders = providers.Where(provider => provider.IsRemoteProxy).ToList();
        var traffic = remoteProviders.Where(provider => provider.TrafficInfo is not null)
            .Select(provider => provider.TrafficInfo!).ToList();
        if (traffic.Count == 0)
        {
            return new(remoteProviders.Count, 0, null, false);
        }

        var isComplete = traffic.Count == remoteProviders.Count;
        var total = isComplete && traffic.All(info => info.Total > 0)
            ? Sum(traffic.Select(info => info.Total)) : 0;
        var expire = traffic.Where(info => info.Expire > 0).Select(info => info.Expire).DefaultIfEmpty().Min();
        return new(remoteProviders.Count, traffic.Count,
            new SubscriptionTrafficInfo(Sum(traffic.Select(info => info.Upload)), Sum(traffic.Select(info => info.Download)), total, expire),
            isComplete && traffic.All(info => info.Expire > 0));
    }

    // 外部额度可能超过有符号整数范围，饱和相加避免显示负数。
    private static long Sum(IEnumerable<long> values) => (long)Math.Min(values.Sum(value => (decimal)value), long.MaxValue);
}
