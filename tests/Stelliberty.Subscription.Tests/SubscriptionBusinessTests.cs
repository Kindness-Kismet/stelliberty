using System.Text;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Xunit;
using YamlDotNet.RepresentationModel;
using DomainSubscription = Stelliberty.Domain.Subscriptions.Subscription;

namespace Stelliberty.Subscription.Tests;

public sealed class SubscriptionBusinessTests
{
    [Fact(DisplayName = "Auto update planner filters startup and due interval subscriptions")]
    public void AutoUpdatePlannerFiltersStartupAndDueIntervalSubscriptions()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(2);
        var subscriptions = new[]
        {
            Subscription("startup") with { AutoUpdateMode = SubscriptionAutoUpdateMode.Startup },
            Subscription("local", isLocal: true) with { AutoUpdateMode = SubscriptionAutoUpdateMode.Startup },
            Subscription("due") with { AutoUpdateMode = SubscriptionAutoUpdateMode.Interval, AutoUpdateIntervalMinutes = 30, LastUpdatedAt = now.AddHours(-1) },
            Subscription("fresh") with { AutoUpdateMode = SubscriptionAutoUpdateMode.Interval, AutoUpdateIntervalMinutes = 30, LastUpdatedAt = now.AddMinutes(-5) }
        };
        var planner = new SubscriptionAutoUpdatePlanner();

        var startup = planner.PlanStartupUpdates(subscriptions);
        var interval = planner.PlanDueIntervalUpdates(subscriptions, now);

        Assert.Equal(["startup"], startup.UpdateSubscriptionIds);
        Assert.Equal(["due"], interval.UpdateSubscriptionIds);
        Assert.DoesNotContain("fresh", interval.UpdateSubscriptionIds);
    }

    [Fact(DisplayName = "Auto update planner waits one interval after failed attempt")]
    public void AutoUpdatePlannerWaitsOneIntervalAfterFailedAttempt()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(2);
        var subscription = Subscription("failed") with
        {
            AutoUpdateMode = SubscriptionAutoUpdateMode.Interval,
            AutoUpdateIntervalMinutes = 30,
            LastUpdatedAt = now.AddHours(-1),
            LastErrorAt = now.AddMinutes(-5)
        };
        var planner = new SubscriptionAutoUpdatePlanner();

        Assert.Empty(planner.PlanDueIntervalUpdates([subscription], now).UpdateSubscriptionIds);
        Assert.Equal(["failed"], planner.PlanDueIntervalUpdates([subscription], now.AddMinutes(25)).UpdateSubscriptionIds);
    }

    [Fact(DisplayName = "Provider parser handles YAML merge and counts")]
    public void ProviderParserHandlesYamlMergeAndCounts()
    {
        var providers = new SubscriptionProviderParser().Parse(
            """
            defaults: &defaults
              type: http
              path: ./provider.yaml
            proxy-providers:
              hk:
                <<: *defaults
                proxies:
                  - name: HK
                  - name: TW
            rule-providers:
              reject:
                <<: *defaults
                ruleCount: 3
            """);

        Assert.Contains(providers, provider => provider.Name == "hk" && provider.Type == "proxy" && provider.VehicleType == "HTTP" && provider.Count == 2);
        Assert.Contains(providers, provider => provider.Name == "reject" && provider.Type == "rule" && provider.Count == 3);
    }

    [Fact(DisplayName = "Content normalizer keeps clash YAML and converts proxy links")]
    public void ContentNormalizerKeepsClashYamlAndConvertsProxyLinks()
    {
        var normalizer = new SubscriptionContentNormalizer();
        var clash = "proxy-groups: []\nproxies: []\nrules: []";
        var converted = normalizer.Normalize("ss://aes-128-gcm:pwd@server.example:8388#HK");

        Assert.Equal(clash, normalizer.Normalize(clash));
        Assert.Contains("proxy-groups", converted, StringComparison.Ordinal);
        Assert.Contains("HK", converted, StringComparison.Ordinal);
        Assert.Contains("MATCH,PROXY", converted, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Content normalizer converts base64 v2ray link subscription variants")]
    public void ContentNormalizerConvertsBase64V2RayLinkSubscriptionVariants()
    {
        var normalizer = new SubscriptionContentNormalizer();
        var vmessJson =
            """
            {"ps":"VMess WS","add":"vmess.example","port":"443","id":"11111111-1111-1111-1111-111111111111","aid":"0","scy":"auto","net":"ws","host":"ws.example","path":"/ws","tls":"tls","sni":"tls.example","fp":"chrome","alpn":"h2,http/1.1","allowInsecure":"1"}
            """;
        var vmess = $"vmess://{Base64UrlNoPadding(vmessJson)}";
        var vmessAead = "vmess://44444444-4444-4444-4444-444444444444@aead.example:443?encryption=auto&security=tls&type=http&host=h2.example&path=/h2&sni=aead.example&fp=chrome#VMess%20AEAD";
        var vless = "vless://22222222-2222-2222-2222-222222222222@vless.example:443?encryption=none&security=reality&sni=reality.example&fp=chrome&pbk=public-key&sid=ab12&type=grpc&serviceName=svc&flow=xtls-rprx-vision&allowInsecure=1&alpn=h2,http/1.1#VLESS%20Reality";
        var trojan = "trojan://secret@trojan.example:443?type=ws&host=trojan-host.example&path=/trojan&sni=trojan.example&fp=firefox&alpn=h2#Trojan%20WS";
        var shadowsocks = $"ss://{Base64UrlNoPadding("aes-128-gcm:pwd@ss.example:8388")}#SS%20Full";
        var hysteria2 = "hy2://hy-pass@hy2.example:443?sni=hy2.example&alpn=h3&pinSHA256=sha256-pin&up=30Mbps&down=100Mbps#HY2";
        var tuic = "tuic://33333333-3333-3333-3333-333333333333:tuic-pass@tuic.example:443?congestion_control=bbr&udp_relay_mode=native&alpn=h3&sni=tuic.example#TUIC";
        var encodedSubscription = Base64UrlNoPadding(string.Join('\n', [vmess, vmessAead, vless, trojan, shadowsocks, hysteria2, tuic]));

        var converted = normalizer.Normalize(encodedSubscription);

        new SubscriptionConfigValidator().Validate(converted);
        Assert.Contains("name: VMess WS", converted, StringComparison.Ordinal);
        Assert.Contains("type: vmess", converted, StringComparison.Ordinal);
        Assert.Contains("client-fingerprint: chrome", converted, StringComparison.Ordinal);
        Assert.Contains("name: VMess AEAD", converted, StringComparison.Ordinal);
        Assert.Contains("h2-opts", converted, StringComparison.Ordinal);
        Assert.Contains("name: VLESS Reality", converted, StringComparison.Ordinal);
        Assert.Contains("type: vless", converted, StringComparison.Ordinal);
        Assert.Contains("encryption: none", converted, StringComparison.Ordinal);
        Assert.Contains("reality-opts", converted, StringComparison.Ordinal);
        Assert.Contains("public-key: public-key", converted, StringComparison.Ordinal);
        Assert.Contains("short-id: ab12", converted, StringComparison.Ordinal);
        Assert.Contains("grpc-service-name: svc", converted, StringComparison.Ordinal);
        Assert.Contains("name: Trojan WS", converted, StringComparison.Ordinal);
        Assert.Contains("type: trojan", converted, StringComparison.Ordinal);
        Assert.Contains("name: SS Full", converted, StringComparison.Ordinal);
        Assert.Contains("server: ss.example", converted, StringComparison.Ordinal);
        Assert.Contains("name: HY2", converted, StringComparison.Ordinal);
        Assert.Contains("fingerprint: sha256-pin", converted, StringComparison.Ordinal);
        Assert.Contains("up: 30Mbps", converted, StringComparison.Ordinal);
        Assert.Contains("down: 100Mbps", converted, StringComparison.Ordinal);
        Assert.Contains("name: TUIC", converted, StringComparison.Ordinal);
        Assert.Contains("congestion-controller: bbr", converted, StringComparison.Ordinal);
        Assert.Contains("udp-relay-mode: native", converted, StringComparison.Ordinal);
        Assert.Contains("MATCH,PROXY", converted, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "Content normalizer removes IPv6 URI brackets from proxy server addresses")]
    [InlineData("vless://11111111-1111-1111-1111-111111111111@[2001:db8::1]:8443#IPv6%20Node", "vless", 8443)]
    [InlineData("vmess://11111111-1111-1111-1111-111111111111@[2001:db8::1]:8443#IPv6%20Node", "vmess", 8443)]
    [InlineData("trojan://secret@[2001:db8::1]:8443#IPv6%20Node", "trojan", 8443)]
    [InlineData("hysteria2://secret@[2001:db8::1]:8443#IPv6%20Node", "hysteria2", 8443)]
    [InlineData("hy2://secret@[2001:db8::1]:8443#IPv6%20Node", "hysteria2", 8443)]
    [InlineData("hysteria://secret@[2001:db8::1]:8443#IPv6%20Node", "hysteria", 8443)]
    [InlineData("tuic://11111111-1111-1111-1111-111111111111:secret@[2001:db8::1]:8443#IPv6%20Node", "tuic", 8443)]
    [InlineData("http://user:secret@[2001:db8::1]:8080#IPv6%20Node", "http", 8080)]
    [InlineData("https://user:secret@[2001:db8::1]:8443#IPv6%20Node", "http", 8443)]
    [InlineData("socks://user:secret@[2001:db8::1]:1080#IPv6%20Node", "socks5", 1080)]
    [InlineData("socks5://user:secret@[2001:db8::1]:1080#IPv6%20Node", "socks5", 1080)]
    [InlineData("ss://aes-128-gcm:secret@[2001:db8::1]:8388#IPv6%20Node", "ss", 8388)]
    public void ContentNormalizerRemovesIpv6UriBracketsFromProxyServerAddresses(string link, string expectedType, int expectedPort)
    {
        var normalizer = new SubscriptionContentNormalizer();

        foreach (var content in new[] { link, Base64UrlNoPadding(link) })
        {
            var proxy = ParseSingleProxy(normalizer.Normalize(content));

            Assert.Equal("2001:db8::1", proxy.Children[new YamlScalarNode("server")].ToString());
            Assert.Equal(expectedType, proxy.Children[new YamlScalarNode("type")].ToString());
            Assert.Equal(expectedPort.ToString(), proxy.Children[new YamlScalarNode("port")].ToString());
            Assert.Equal("IPv6 Node", proxy.Children[new YamlScalarNode("name")].ToString());
        }
    }

    [Theory(DisplayName = "Content normalizer converts IPv6 Shadowsocks links with either base64 format")]
    [InlineData(false)]
    [InlineData(true)]
    public void ContentNormalizerConvertsIpv6ShadowsocksLinksWithEitherBase64Format(bool encodeFullAuthority)
    {
        var authority = encodeFullAuthority
            ? Base64UrlNoPadding("aes-128-gcm:secret@[2001:db8::1]:8388")
            : $"{Base64UrlNoPadding("aes-128-gcm:secret")}@[2001:db8::1]:8388";
        var proxy = ParseSingleProxy(new SubscriptionContentNormalizer().Normalize($"ss://{authority}#IPv6%20Node"));

        Assert.Equal("2001:db8::1", proxy.Children[new YamlScalarNode("server")].ToString());
        Assert.Equal("8388", proxy.Children[new YamlScalarNode("port")].ToString());
        Assert.Equal("aes-128-gcm", proxy.Children[new YamlScalarNode("cipher")].ToString());
        Assert.Equal("secret", proxy.Children[new YamlScalarNode("password")].ToString());
    }

    [Theory(DisplayName = "Content normalizer preserves domain and IPv4 proxy server addresses")]
    [InlineData("proxy.example")]
    [InlineData("127.0.0.1")]
    [InlineData("例子.测试")]
    public void ContentNormalizerPreservesDomainAndIpv4ProxyServerAddresses(string host)
    {
        var proxy = ParseSingleProxy(new SubscriptionContentNormalizer().Normalize($"http://{host}:8080#Control"));

        Assert.Equal(host, proxy.Children[new YamlScalarNode("server")].ToString());
        Assert.Equal("8080", proxy.Children[new YamlScalarNode("port")].ToString());
    }

    [Fact(DisplayName = "Content normalizer preserves bare IPv6 addresses in VMess JSON links")]
    public void ContentNormalizerPreservesBareIpv6AddressesInVmessJsonLinks()
    {
        const string vmessJson = """
            {"ps":"IPv6","add":"2001:db8::1","port":"443","id":"11111111-1111-1111-1111-111111111111","net":"tcp"}
            """;
        var proxy = ParseSingleProxy(new SubscriptionContentNormalizer().Normalize($"vmess://{Base64UrlNoPadding(vmessJson)}"));

        Assert.Equal("2001:db8::1", proxy.Children[new YamlScalarNode("server")].ToString());
        Assert.Equal("vmess", proxy.Children[new YamlScalarNode("type")].ToString());
    }

    private static YamlMappingNode ParseSingleProxy(string content)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(content));
        var root = Assert.IsType<YamlMappingNode>(Assert.Single(stream.Documents).RootNode);
        var proxies = Assert.IsType<YamlSequenceNode>(root.Children[new YamlScalarNode("proxies")]);
        return Assert.IsType<YamlMappingNode>(Assert.Single(proxies.Children));
    }

    private static DomainSubscription Subscription(string id, bool isLocal = false)
    {
        return new DomainSubscription(id, id, isLocal ? "local.yaml" : "https://sub.example/config.yaml", isLocal, DateTimeOffset.UnixEpoch);
    }

    private static string Base64UrlNoPadding(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
