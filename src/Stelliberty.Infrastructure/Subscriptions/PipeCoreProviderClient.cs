using System.Net.Http;
using System.Text.Json;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Infrastructure.Proxies;

namespace Stelliberty.Infrastructure.Subscriptions;

// 通过核心管道读取和写入 providers，独立于 TCP 控制。
public sealed class PipeCoreProviderClient : ISubscriptionProviderSyncer, ISubscriptionProviderStateReader, IDisposable
{
    private readonly HttpClient _client;

    public PipeCoreProviderClient(string corePipe)
    {
        _client = PipeCoreProxyClient.CreatePipeHttpClient(corePipe);
        // 同步会让核心重新拉取远程 providers，慢源需要预留余量。
        _client.Timeout = TimeSpan.FromSeconds(30);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    public async Task SyncAsync(SubscriptionProvider provider, CancellationToken cancellationToken = default)
    {
        using var response = await _client.PutAsync(BuildProviderPath(provider.Type, provider.Name), content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Core rejected Provider update: HTTP {(int)response.StatusCode}");
        }

        AppLogger.Info($"Provider sync completed: {provider.Name}");
    }

    public async Task<IReadOnlyList<SubscriptionProviderRuntimeState>> ReadStatesAsync(CancellationToken cancellationToken = default)
    {
        var states = new List<SubscriptionProviderRuntimeState>();
        states.AddRange(await ReadSectionAsync("proxy", cancellationToken));
        states.AddRange(await ReadSectionAsync("rule", cancellationToken));
        return states;
    }

    // 局部合并成功会造成误导；调用方处理整体失败。
    private async Task<IReadOnlyList<SubscriptionProviderRuntimeState>> ReadSectionAsync(string providerType, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(providerType == "rule" ? "providers/rules" : "providers/proxies", cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var states = new List<SubscriptionProviderRuntimeState>();
        foreach (var item in providers.EnumerateObject())
        {
            states.Add(new SubscriptionProviderRuntimeState(
                item.Name, providerType, ReadCount(item.Value, providerType), ReadUpdatedAt(item.Value), ReadTrafficInfo(item.Value)));
        }

        return states;
    }

    private static SubscriptionTrafficInfo? ReadTrafficInfo(JsonElement provider)
    {
        if (!provider.TryGetProperty("subscriptionInfo", out var info) || info.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return SubscriptionTrafficInfo.FromValues(
            ReadTrafficValue(info, "Upload"),
            ReadTrafficValue(info, "Download"),
            ReadTrafficValue(info, "Total"),
            ReadTrafficValue(info, "Expire"));
    }

    private static long ReadTrafficValue(JsonElement info, string name)
    {
        return info.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
            && number >= 0 ? number : 0;
    }

    private static int ReadCount(JsonElement provider, string providerType)
    {
        if (providerType == "rule")
        {
            return provider.TryGetProperty("ruleCount", out var ruleCount) && ruleCount.ValueKind == JsonValueKind.Number ? ruleCount.GetInt32() : 0;
        }

        return provider.TryGetProperty("proxies", out var proxies) && proxies.ValueKind == JsonValueKind.Array ? proxies.GetArrayLength() : 0;
    }

    private static DateTimeOffset? ReadUpdatedAt(JsonElement provider)
    {
        if (!provider.TryGetProperty("updatedAt", out var updatedAt)
            || updatedAt.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(updatedAt.GetString(), out var parsed))
        {
            return null;
        }

        // 核心对从未拉取的 providers 返回零时间戳。
        return parsed.Year <= 1 ? null : parsed;
    }

    private static string BuildProviderPath(string providerType, string providerName)
    {
        var providerTypeSegment = string.Equals(providerType, "rule", StringComparison.OrdinalIgnoreCase) ? "rules" : "proxies";
        return $"providers/{providerTypeSegment}/{Uri.EscapeDataString(providerName)}";
    }
}
