namespace Stelliberty.Domain.Subscriptions;

public sealed record SubscriptionTrafficInfo(long Upload, long Download, long Total, long Expire)
{
    public long Used => (long)Math.Min((decimal)Upload + Download, long.MaxValue);

    public static SubscriptionTrafficInfo FromValues(long upload, long download, long total, long expire) => new(
        Math.Max(0, upload), Math.Max(0, download), Math.Max(0, total),
        expire > 0 && expire <= DateTimeOffset.MaxValue.ToUnixTimeSeconds() ? expire : 0);

    public static SubscriptionTrafficInfo ParseHeader(string header)
    {
        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var keyValue = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (keyValue.Length == 2 && long.TryParse(keyValue[1], out var value))
            {
                values[keyValue[0]] = value;
            }
        }

        return FromValues(
            values.GetValueOrDefault("upload"),
            values.GetValueOrDefault("download"),
            values.GetValueOrDefault("total"),
            values.GetValueOrDefault("expire"));
    }
}
