using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using YamlDotNet.RepresentationModel;
using Xunit;

namespace Stelliberty.ChainProxy.Tests;

public sealed class ChainProxyRuntimeTests
{
    [Fact(DisplayName = "Analyzer finds distinct builtin chain proxy names")]
    public void AnalyzerFindsDistinctBuiltinChainProxyNames()
    {
        var names = new SubscriptionChainProxyAnalyzer().AnalyzeBuiltinChainProxyNames(
            """
            proxies:
              - name: JP
                type: ss
                server: jp.example
                dialer-proxy: HK
              - name: JP
                type: ss
                server: jp-alt.example
                dialer-proxy: HK
              - name: HK
                type: ss
                server: hk.example
              - name: Empty
                type: ss
                dialer-proxy: ''
            """);

        Assert.Equal(["JP"], names);
    }

    [Fact(DisplayName = "Runtime applier removes disabled builtin chain proxy only")]
    public void RuntimeApplierRemovesDisabledBuiltinChainProxyOnly()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(
            """
            proxies:
              - name: HK
                type: ss
                server: hk.example
              - name: JP
                type: ss
                server: jp.example
              - name: JP via HK
                type: ss
                server: jp.example
                dialer-proxy: HK
            proxy-groups:
              - name: GLOBAL
                type: select
                proxies: [HK, JP via HK, JP]
            rules: []
            """,
            Subscription("sub-1") with
            {
                DisabledBuiltinChainProxyNames = ["JP via HK"]
            });
        var proxies = Proxies(output);

        Assert.DoesNotContain(proxies, proxy => Scalar(proxy, "name") == "JP via HK");
        Assert.Contains(proxies, proxy => Scalar(proxy, "name") == "HK");
        Assert.Contains(proxies, proxy => Scalar(proxy, "name") == "JP");
        Assert.Equal(["HK", "JP"], ProxyGroupEntries(output, "GLOBAL"));
    }

    [Fact(DisplayName = "Runtime applier fills emptied proxy group with fallback outbound")]
    public void RuntimeApplierFillsEmptiedProxyGroupWithFallbackOutbound()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(
            """
            proxies:
              - name: HK
                type: ss
                server: hk.example
              - name: JP via HK
                type: ss
                server: jp.example
                dialer-proxy: HK
            proxy-groups:
              - name: LANDING
                type: select
                proxies: [JP via HK]
              - name: CUSTOM FALLBACK
                type: select
                empty-fallback: DIRECT
                proxies: [JP via HK]
            rules: []
            """,
            Subscription("sub-1") with
            {
                DisabledBuiltinChainProxyNames = ["JP via HK"]
            });

        Assert.Equal(["COMPATIBLE"], ProxyGroupEntries(output, "LANDING"));
        Assert.Equal(["DIRECT"], ProxyGroupEntries(output, "CUSTOM FALLBACK"));
    }

    [Fact(DisplayName = "Runtime applier keeps core provided groups without fallback")]
    public void RuntimeApplierKeepsCoreProvidedGroupsWithoutFallback()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(
            """
            proxies:
              - name: HK
                type: ss
                server: hk.example
              - name: JP via HK
                type: ss
                server: jp.example
                dialer-proxy: HK
            proxy-groups:
              - name: USE ONLY
                type: select
                use: [airport]
                proxies: [JP via HK]
              - name: INCLUDE ALL
                type: select
                include-all: true
                proxies: [JP via HK]
            rules: []
            """,
            Subscription("sub-1") with
            {
                DisabledBuiltinChainProxyNames = ["JP via HK"]
            });

        Assert.Empty(ProxyGroupEntries(output, "USE ONLY"));
        Assert.Empty(ProxyGroupEntries(output, "INCLUDE ALL"));
    }

    [Fact(DisplayName = "Runtime applier adds custom chain with internal hop")]
    public void RuntimeApplierAddsCustomChainWithInternalHop()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(BaseConfig(), Subscription("sub-1") with
        {
            CustomChainProxies =
            [
                Chain("chain-a", "JP via TW via HK", "GLOBAL", "HK", "TW", "JP")
            ]
        });
        var proxies = Proxies(output);
        var internalHop = proxies.Single(proxy => Scalar(proxy, "name") == "__stelliberty_chain_chain-a_1");
        var display = proxies.Single(proxy => Scalar(proxy, "name") == "JP via TW via HK");

        Assert.Equal("HK", Scalar(internalHop, "dialer-proxy"));
        Assert.Equal("__stelliberty_chain_chain-a_1", Scalar(display, "dialer-proxy"));
        Assert.Equal("jp.example", Scalar(display, "server"));
    }

    [Fact(DisplayName = "Runtime applier overrides leaf dialer proxy")]
    public void RuntimeApplierOverridesLeafDialerProxy()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(
            """
            proxies:
              - name: HK
                type: ss
                server: hk.example
              - name: JP
                type: ss
                server: jp.example
                dialer-proxy: OLD
            proxy-groups:
              - name: GLOBAL
                type: select
                proxies: [HK, JP]
            rules: []
            """,
            Subscription("sub-1") with
            {
                CustomChainProxies =
                [
                    Chain("chain-a", "JP via HK", "GLOBAL", "HK", "JP")
                ]
            });

        var display = Proxies(output).Single(proxy => Scalar(proxy, "name") == "JP via HK");

        Assert.Equal("HK", Scalar(display, "dialer-proxy"));
        Assert.Equal(["HK", "JP", "JP via HK"], ProxyGroupEntries(output, "GLOBAL"));
    }

    [Fact(DisplayName = "Runtime applier skips custom chain when display name is occupied")]
    public void RuntimeApplierSkipsCustomChainWhenDisplayNameIsOccupied()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(BaseConfig(), Subscription("sub-1") with
        {
            CustomChainProxies =
            [
                Chain("conflict-node", "JP", "GLOBAL", "HK", "TW"),
                Chain("conflict-group", "GLOBAL", "GLOBAL", "HK", "TW")
            ]
        });

        Assert.DoesNotContain(Proxies(output), proxy => Scalar(proxy, "name").StartsWith("__stelliberty_chain_", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Runtime applier skips invalid custom chains")]
    public void RuntimeApplierSkipsInvalidCustomChains()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(BaseConfig(), Subscription("sub-1") with
        {
            CustomChainProxies =
            [
                Chain("missing", "Missing chain", "GLOBAL", "HK", "Missing"),
                Chain("single", "Single chain", "GLOBAL", "HK")
            ]
        });

        Assert.DoesNotContain(Proxies(output), proxy => Scalar(proxy, "name") is "Missing chain" or "Single chain");
    }

    [Fact(DisplayName = "Runtime applier avoids internal name conflicts")]
    public void RuntimeApplierAvoidsInternalNameConflicts()
    {
        var output = new SubscriptionChainProxyRuntimeApplier().Apply(
            """
            proxies:
              - name: HK
                type: ss
                server: hk.example
              - name: TW
                type: ss
                server: tw.example
              - name: JP
                type: ss
                server: jp.example
              - name: __stelliberty_chain_chain-a_1
                type: ss
                server: occupied.example
            proxy-groups:
              - name: GLOBAL
                type: select
                proxies: [HK, TW, JP]
            rules: []
            """,
            Subscription("sub-1") with
            {
                CustomChainProxies =
                [
                    Chain("chain-a", "JP via TW via HK", "GLOBAL", "HK", "TW", "JP")
                ]
            });

        Assert.Contains(Proxies(output), proxy => Scalar(proxy, "name") == "__stelliberty_chain_chain-a_1_2");
    }

    [Fact(DisplayName = "Runtime applier returns original content when no work can be done")]
    public void RuntimeApplierReturnsOriginalContentWhenNoWorkCanBeDone()
    {
        var content = BaseConfig();
        var invalid = "proxies: [";
        var applier = new SubscriptionChainProxyRuntimeApplier();

        Assert.Equal(content, applier.Apply(content, Subscription("sub-1")));
        Assert.Equal(invalid, applier.Apply(invalid, Subscription("sub-1") with
        {
            DisabledBuiltinChainProxyNames = ["JP via HK"]
        }));
    }

    private static string BaseConfig()
    {
        return """
        proxies:
          - name: HK
            type: ss
            server: hk.example
          - name: TW
            type: ss
            server: tw.example
          - name: JP
            type: ss
            server: jp.example
          - name: JP via HK
            type: ss
            server: jp.example
            dialer-proxy: HK
        proxy-groups:
          - name: GLOBAL
            type: select
            proxies: [HK, TW, JP]
        rules: []
        """;
    }

    private static SubscriptionCustomChainProxy Chain(
        string id,
        string displayName,
        string proxyGroupName,
        params string[] proxyNames)
    {
        return new SubscriptionCustomChainProxy(
            id,
            displayName,
            proxyGroupName,
            proxyNames.Select(ProxyHop).ToList());
    }

    private static SubscriptionChainProxyHop ProxyHop(string name)
        => new(SubscriptionChainProxyHopKind.Proxy, name);

    private static Subscription Subscription(string id)
    {
        return new Subscription(id, "Sub", "source", false, DateTimeOffset.UnixEpoch);
    }

    private static IReadOnlyList<YamlMappingNode> Proxies(string content)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(content));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        return ((YamlSequenceNode)root.Children[new YamlScalarNode("proxies")]).Children.OfType<YamlMappingNode>().ToList();
    }

    private static IReadOnlyList<string> ProxyGroupEntries(string content, string groupName)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(content));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        var groups = ((YamlSequenceNode)root.Children[new YamlScalarNode("proxy-groups")]).Children.OfType<YamlMappingNode>();
        var group = groups.Single(group => Scalar(group, "name") == groupName);
        // 成员由核心补齐的组不写 proxies 键，视为空条目。
        return group.Children.TryGetValue(new YamlScalarNode("proxies"), out var value) && value is YamlSequenceNode sequence
            ? sequence.Children.Select(node => node.ToString()).ToList()
            : [];
    }

    private static string Scalar(YamlMappingNode mapping, string key)
    {
        return mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value.ToString() : string.Empty;
    }

}
