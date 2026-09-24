namespace Stelliberty.Domain.Subscriptions;

public sealed record SubscriptionProvider(
    string Name,
    string Type,
    string VehicleType,
    string Path,
    int Count,
    DateTimeOffset? UpdatedAt,
    bool IsUpdating = false,
    SubscriptionTrafficInfo? TrafficInfo = null,
    string SourceFingerprint = "")
{
    public bool IsRemoteProxy => IsHttp && string.Equals(Type, "proxy", StringComparison.OrdinalIgnoreCase);

    public bool IsVisible => IsHttp || string.Equals(VehicleType, "File", StringComparison.OrdinalIgnoreCase);

    public bool CanSync => IsHttp && !IsUpdating;

    private bool IsHttp => string.Equals(VehicleType, "HTTP", StringComparison.OrdinalIgnoreCase);
}
