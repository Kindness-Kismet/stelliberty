using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Application.Subscriptions;

public sealed record SelectedSubscriptionRuntimeResult(
    Subscription Subscription,
    string RuntimeConfigContent,
    string ContentFingerprint);
