using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Application.Subscriptions;

public sealed record SubscriptionProviderRuntimeState(
    string Name,
    string Type,
    int Count,
    DateTimeOffset? UpdatedAt,
    SubscriptionTrafficInfo? TrafficInfo = null);

public interface ISubscriptionProviderStateReader
{
    Task<IReadOnlyList<SubscriptionProviderRuntimeState>> ReadStatesAsync(CancellationToken cancellationToken = default);
}
