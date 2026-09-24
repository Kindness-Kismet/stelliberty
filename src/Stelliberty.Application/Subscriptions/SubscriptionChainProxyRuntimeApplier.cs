using Stelliberty.Domain.Subscriptions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Application.Subscriptions;

public sealed record ChainProxyInspection(
    IReadOnlyList<string> InvalidCustomChainIds,
    IReadOnlyList<string> BrokenBuiltinChainProxyNames)
{
    public static ChainProxyInspection Empty { get; } = new([], []);

    public bool HasBrokenChains => InvalidCustomChainIds.Count > 0 || BrokenBuiltinChainProxyNames.Count > 0;
}

public sealed class SubscriptionChainProxyRuntimeApplier
{
    // dialer-proxy 允许指向内置出站，这些名字不算悬空。
    private static readonly HashSet<string> BuiltinOutbounds = new(StringComparer.Ordinal)
    {
        "DIRECT",
        "REJECT",
        "REJECT-DROP",
        "PASS",
        "COMPATIBLE"
    };

    // 与 mihomo 的 empty-fallback 默认值一致，作为空代理组占位。
    private const string DefaultEmptyFallback = "COMPATIBLE";

    public ChainProxyInspection Inspect(string content, Subscription subscription)
    {
        if (!TryLoad(content, out _, out _, out var proxies, out var proxyGroups))
        {
            return ChainProxyInspection.Empty;
        }

        var disabledNames = subscription.DisabledBuiltinChainProxyNames.ToHashSet(StringComparer.Ordinal);
        var brokenBuiltinNames = FindBrokenBuiltinChainProxies(proxies, proxyGroups, disabledNames);
        disabledNames.UnionWith(brokenBuiltinNames);
        return new ChainProxyInspection(
            FindInvalidCustomChainIds(proxies, proxyGroups, subscription, disabledNames),
            brokenBuiltinNames);
    }

    public string Apply(string content, Subscription subscription)
    {
        if (!TryLoad(content, out var stream, out var root, out var proxies, out var proxyGroups))
        {
            return content;
        }

        var disabledNames = subscription.DisabledBuiltinChainProxyNames.ToHashSet(StringComparer.Ordinal);
        if (disabledNames.Count == 0 && subscription.CustomChainProxies.All(item => !item.IsEnabled))
        {
            return content;
        }

        var result = BuildRuntimeConfig(proxies, proxyGroups, subscription, disabledNames);
        Set(root, "proxies", result.Proxies);
        if (HasMappingSequence(root, "proxy-groups"))
        {
            Set(root, "proxy-groups", result.ProxyGroups);
        }

        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    // 上游随订阅更新消失后核心会拒绝整份配置，这里找出所有连锁失效的内置链式节点。
    private static IReadOnlyList<string> FindBrokenBuiltinChainProxies(
        IReadOnlyList<YamlMappingNode> proxies,
        IReadOnlyList<YamlMappingNode> proxyGroups,
        IReadOnlySet<string> disabledNames)
    {
        var chainProxies = proxies
            .Select(proxy => (Name: Scalar(proxy, "name"), DialerProxy: Scalar(proxy, "dialer-proxy")))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.DialerProxy))
            .ToList();
        if (chainProxies.Count == 0)
        {
            return [];
        }

        // 已禁用节点不在可达集合内，指向它们的下游会被一并判定失效。
        var reachableNames = proxies
            .Select(proxy => Scalar(proxy, "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name) && !disabledNames.Contains(name))
            .Concat(proxyGroups.Select(group => Scalar(group, "name")).Where(name => !string.IsNullOrWhiteSpace(name)))
            .Concat(BuiltinOutbounds)
            .ToHashSet(StringComparer.Ordinal);
        var brokenNames = new List<string>();
        bool hasNewBroken;
        do
        {
            hasNewBroken = false;
            foreach (var chainProxy in chainProxies)
            {
                if (!reachableNames.Contains(chainProxy.Name) || reachableNames.Contains(chainProxy.DialerProxy))
                {
                    continue;
                }

                reachableNames.Remove(chainProxy.Name);
                brokenNames.Add(chainProxy.Name);
                hasNewBroken = true;
            }
        }
        while (hasNewBroken);

        return brokenNames;
    }

    private static IReadOnlyList<string> FindInvalidCustomChainIds(
        IReadOnlyList<YamlMappingNode> proxies,
        IReadOnlyList<YamlMappingNode> proxyGroups,
        Subscription subscription,
        IReadOnlySet<string> disabledNames)
    {
        var index = BuildActiveProxyIndex(proxies, proxyGroups, disabledNames);
        var invalidIds = new List<string>();
        foreach (var customProxy in subscription.CustomChainProxies.Where(item => item.IsEnabled))
        {
            if (BuildRuntimeDialerProxies(index.ProxyByName, proxyGroups, index.OccupiedNames, customProxy).Count == 0)
            {
                invalidIds.Add(customProxy.Id);
            }
        }

        return invalidIds;
    }

    private sealed record RuntimeConfigBuildResult(
        YamlSequenceNode Proxies,
        YamlSequenceNode ProxyGroups);

    private sealed record CustomProxyGroupEntry(string ProxyGroupName, string DisplayName);

    private sealed record ActiveProxyIndex(
        IReadOnlyList<YamlMappingNode> ActiveProxies,
        IReadOnlyDictionary<string, YamlMappingNode> ProxyByName,
        HashSet<string> OccupiedNames);

    private static ActiveProxyIndex BuildActiveProxyIndex(
        IReadOnlyList<YamlMappingNode> proxies,
        IReadOnlyList<YamlMappingNode> proxyGroups,
        IReadOnlySet<string> disabledNames)
    {
        // 内置链式代理是带 dialer-proxy 的覆写后节点。
        var activeProxies = proxies
            .Where(proxy => !IsDisabledBuiltinProxy(proxy, disabledNames))
            .ToList();
        var proxyByName = activeProxies
            .GroupBy(proxy => Scalar(proxy, "name"), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var occupiedNames = proxyByName.Keys
            .Concat(proxyGroups.Select(group => Scalar(group, "name")))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
        return new ActiveProxyIndex(activeProxies, proxyByName, occupiedNames);
    }

    private static RuntimeConfigBuildResult BuildRuntimeConfig(
        IReadOnlyList<YamlMappingNode> proxies,
        IReadOnlyList<YamlMappingNode> proxyGroups,
        Subscription subscription,
        IReadOnlySet<string> disabledNames)
    {
        var index = BuildActiveProxyIndex(proxies, proxyGroups, disabledNames);
        var runtimeProxies = index.ActiveProxies.Select(Clone).ToList();
        var customGroupEntries = new List<CustomProxyGroupEntry>();
        foreach (var customProxy in subscription.CustomChainProxies.Where(item => item.IsEnabled))
        {
            var runtimeDialerProxies = BuildRuntimeDialerProxies(
                index.ProxyByName,
                proxyGroups,
                index.OccupiedNames,
                customProxy);
            if (runtimeDialerProxies.Count == 0)
            {
                continue;
            }

            customGroupEntries.Add(new CustomProxyGroupEntry(
                customProxy.ProxyGroupName.Trim(),
                customProxy.DisplayName.Trim()));
            runtimeProxies.AddRange(runtimeDialerProxies);
        }

        var disabledBuiltinNames = proxies
            .Where(proxy => IsDisabledBuiltinProxy(proxy, disabledNames))
            .Select(proxy => Scalar(proxy, "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        return new RuntimeConfigBuildResult(
            new YamlSequenceNode(runtimeProxies),
            BuildProxyGroups(proxyGroups, disabledBuiltinNames, customGroupEntries));
    }

    private static IReadOnlyList<YamlMappingNode> BuildRuntimeDialerProxies(
        IReadOnlyDictionary<string, YamlMappingNode> proxyByName,
        IReadOnlyList<YamlMappingNode> proxyGroups,
        HashSet<string> occupiedNames,
        SubscriptionCustomChainProxy customProxy)
    {
        var hops = customProxy.Hops
            .Where(hop => !string.IsNullOrWhiteSpace(hop.Name))
            .Select(hop => hop with { Name = hop.Name.Trim() })
            .ToList();
        var displayName = customProxy.DisplayName.Trim();
        var proxyGroupName = customProxy.ProxyGroupName.Trim();
        if (hops.Count < 2
            || string.IsNullOrWhiteSpace(displayName)
            || string.IsNullOrWhiteSpace(proxyGroupName)
            || occupiedNames.Contains(displayName)
            || !proxyGroups.Any(group => Scalar(group, "name") == proxyGroupName)
            || hops.Skip(1).Any(hop => hop.Kind != SubscriptionChainProxyHopKind.Proxy)
            || hops.Count(hop => hop.Kind == SubscriptionChainProxyHopKind.ProxyGroup) > 1)
        {
            return [];
        }

        var firstHop = hops[0];
        if ((firstHop.Kind == SubscriptionChainProxyHopKind.ProxyGroup
                && !proxyGroups.Any(group => Scalar(group, "name") == firstHop.Name))
            || (firstHop.Kind == SubscriptionChainProxyHopKind.Proxy
                && !proxyByName.ContainsKey(firstHop.Name)))
        {
            return [];
        }

        if (hops.Skip(1).Any(hop => !proxyByName.ContainsKey(hop.Name)))
        {
            return [];
        }

        var plannedOccupiedNames = occupiedNames.ToHashSet(StringComparer.Ordinal);
        plannedOccupiedNames.Add(displayName);
        var runtimeNames = new List<string>();
        for (var index = 1; index < hops.Count; index++)
        {
            runtimeNames.Add(index == hops.Count - 1
                ? displayName
                : ReserveInternalProxyName(customProxy, index, plannedOccupiedNames));
        }

        occupiedNames.UnionWith(runtimeNames);
        var result = new List<YamlMappingNode>();
        var previousName = firstHop.Name;
        for (var index = 1; index < hops.Count; index++)
        {
            var runtimeName = runtimeNames[index - 1];
            var runtimeProxy = Clone(proxyByName[hops[index].Name]);
            SetScalar(runtimeProxy, "name", runtimeName);
            SetScalar(runtimeProxy, "dialer-proxy", previousName);

            result.Add(runtimeProxy);
            previousName = runtimeName;
        }

        return result;
    }

    private static YamlSequenceNode BuildProxyGroups(
        IReadOnlyList<YamlMappingNode> proxyGroups,
        HashSet<string> disabledBuiltinNames,
        IReadOnlyList<CustomProxyGroupEntry> customGroupEntries)
    {
        var groups = new List<YamlMappingNode>();
        foreach (var group in proxyGroups)
        {
            var clone = Clone(group);
            var sourceEntries = clone.Children.TryGetValue(new YamlScalarNode("proxies"), out var proxiesNode)
                && proxiesNode is YamlSequenceNode proxies
                ? proxies
                : new YamlSequenceNode();
            var entries = BuildProxyGroupEntries(
                Scalar(group, "name"),
                sourceEntries,
                disabledBuiltinNames,
                customGroupEntries);
            // 移除禁用链式节点可能清空代理组，mihomo 会以 `use` or `proxies` missing 拒绝整份配置。
            if (entries.Children.Count == 0 && !HasCoreProvidedMembers(group))
            {
                entries.Add(new YamlScalarNode(EmptyFallbackOutbound(group)));
            }

            if (entries.Children.Count > 0)
            {
                clone.Children[new YamlScalarNode("proxies")] = entries;
            }
            else
            {
                // 成员全部被过滤时必须删除原键，否则克隆会保留过期条目。
                clone.Children.Remove(new YamlScalarNode("proxies"));
            }

            groups.Add(clone);
        }

        return new YamlSequenceNode(groups);
    }

    private static YamlSequenceNode BuildProxyGroupEntries(
        string proxyGroupName,
        YamlSequenceNode proxies,
        HashSet<string> disabledBuiltinNames,
        IReadOnlyList<CustomProxyGroupEntry> customGroupEntries)
    {
        var entries = new List<YamlNode>();
        foreach (var entry in proxies.Children)
        {
            var name = entry.ToString();
            if (disabledBuiltinNames.Contains(name))
            {
                continue;
            }

            entries.Add(entry);
        }

        foreach (var customEntry in customGroupEntries.Where(item => string.Equals(item.ProxyGroupName, proxyGroupName, StringComparison.Ordinal)))
        {
            // 自定义链只挂到用户明确选择的代理组。
            if (!ContainsScalar(entries, customEntry.DisplayName))
            {
                entries.Add(new YamlScalarNode(customEntry.DisplayName));
            }
        }

        return new YamlSequenceNode(entries);
    }

    // use 与 include-all 系列由核心补齐成员，空 proxies 不触发拒绝。
    private static bool HasCoreProvidedMembers(YamlMappingNode group)
    {
        return HasNonEmptySequence(group, "use")
            || IsTrueScalar(group, "include-all")
            || IsTrueScalar(group, "include-all-proxies")
            || IsTrueScalar(group, "include-all-providers");
    }

    private static string EmptyFallbackOutbound(YamlMappingNode group)
    {
        var fallback = Scalar(group, "empty-fallback");
        return string.IsNullOrWhiteSpace(fallback) ? DefaultEmptyFallback : fallback;
    }

    private static bool HasNonEmptySequence(YamlMappingNode mapping, string key)
    {
        return mapping.Children.TryGetValue(new YamlScalarNode(key), out var value)
            && value is YamlSequenceNode sequence
            && sequence.Children.Count > 0;
    }

    private static bool IsTrueScalar(YamlMappingNode mapping, string key)
    {
        return string.Equals(Scalar(mapping, key), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDisabledBuiltinProxy(YamlMappingNode proxy, IReadOnlySet<string> disabledNames)
    {
        return disabledNames.Contains(Scalar(proxy, "name"))
            && !string.IsNullOrWhiteSpace(Scalar(proxy, "dialer-proxy"));
    }

    private static string ReserveInternalProxyName(
        SubscriptionCustomChainProxy customProxy,
        int index,
        HashSet<string> occupiedNames)
    {
        var stem = $"__stelliberty_chain_{NameSegment(customProxy.Id, customProxy.DisplayName)}_{index}";
        var name = stem;
        var suffix = 2;
        while (occupiedNames.Contains(name))
        {
            name = $"{stem}_{suffix}";
            suffix++;
        }

        occupiedNames.Add(name);
        return name;
    }

    private static string NameSegment(string id, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(id) ? fallback : id;
        var chars = source
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_')
            .ToArray();
        return chars.Length == 0 ? "custom" : new string(chars);
    }

    private static bool TryLoad(
        string content,
        out YamlStream stream,
        out YamlMappingNode root,
        out IReadOnlyList<YamlMappingNode> proxies,
        out IReadOnlyList<YamlMappingNode> proxyGroups)
    {
        stream = new YamlStream();
        root = null!;
        proxies = [];
        proxyGroups = [];
        try
        {
            stream.Load(new StringReader(content));
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
            {
                return false;
            }

            root = mapping;
            proxies = ReadMappingSequence(mapping, "proxies");
            proxyGroups = ReadMappingSequence(mapping, "proxy-groups");
            return true;
        }
        catch (YamlException)
        {
            return false;
        }
    }

    private static IReadOnlyList<YamlMappingNode> ReadMappingSequence(YamlMappingNode root, string key)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode(key), out var value) || value is not YamlSequenceNode sequence)
        {
            return [];
        }

        return sequence.Children.OfType<YamlMappingNode>().ToList();
    }

    private static bool HasMappingSequence(YamlMappingNode root, string key)
    {
        return root.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlSequenceNode;
    }

    private static void Set(YamlMappingNode root, string key, YamlSequenceNode value)
    {
        root.Children[new YamlScalarNode(key)] = value;
    }

    private static string Scalar(YamlMappingNode mapping, string key)
    {
        return mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value.ToString() : string.Empty;
    }

    private static void SetScalar(YamlMappingNode mapping, string key, string value)
    {
        mapping.Children[new YamlScalarNode(key)] = new YamlScalarNode(value);
    }

    private static bool ContainsScalar(IEnumerable<YamlNode> nodes, string value)
    {
        return nodes.Any(node => string.Equals(node.ToString(), value, StringComparison.Ordinal));
    }

    private static YamlMappingNode Clone(YamlMappingNode mapping)
    {
        var clone = new YamlMappingNode();
        foreach (var child in mapping.Children)
        {
            clone.Children.Add(child.Key, child.Value);
        }

        return clone;
    }
}
