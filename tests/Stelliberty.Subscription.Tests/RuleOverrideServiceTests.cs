using Stelliberty.Application.Overrides;
using Stelliberty.Application.Rules;
using Stelliberty.Application.Runtime;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Overrides;
using Stelliberty.Domain.Rules;
using Stelliberty.Domain.Subscriptions;
using YamlDotNet.RepresentationModel;
using Xunit;
using DomainSubscription = Stelliberty.Domain.Subscriptions.Subscription;

namespace Stelliberty.Subscription.Tests;

public sealed class InMemorySubscriptionStore : ISubscriptionStore
{
    private readonly Dictionary<string, (DomainSubscription Subscription, string Content)> _items = new();

    public void Save(DomainSubscription subscription, string originalContent) => _items[subscription.Id] = (subscription, originalContent);

    public void UpdateSubscription(DomainSubscription subscription) => _items[subscription.Id] = (subscription, _items[subscription.Id].Content);

    public void SaveSubscriptions(IReadOnlyList<DomainSubscription> subscriptions)
    {
        foreach (var subscription in subscriptions)
        {
            UpdateSubscription(subscription);
        }
    }

    public void SaveContent(string subscriptionId, string originalContent) => _items[subscriptionId] = (_items[subscriptionId].Subscription, originalContent);

    public IReadOnlyList<DomainSubscription> LoadSubscriptions() => _items.Values.Select(item => item.Subscription).ToList();

    public string ReadContent(string subscriptionId) => _items[subscriptionId].Content;

    public string GetContentPath(string subscriptionId) => string.Empty;

    public void Delete(string subscriptionId) => _items.Remove(subscriptionId);
}

public sealed class InMemorySelectionStore : ISubscriptionSelectionStore
{
    public string? CurrentSubscriptionId { get; private set; }

    public string? GetCurrentSubscriptionId() => CurrentSubscriptionId;

    public void SetCurrentSubscriptionId(string? subscriptionId) => CurrentSubscriptionId = subscriptionId;
}

public sealed class InMemoryOverrideProfileStore : IOverrideStore
{
    private readonly Dictionary<string, (OverrideProfile Profile, string Content)> _items = new();

    public void Save(OverrideProfile overrideProfile, string content) => _items[overrideProfile.Id] = (overrideProfile, content);

    public IReadOnlyList<OverrideProfile> LoadOverrides() => _items.Values.Select(item => item.Profile).ToList();

    public string ReadContent(string overrideId) => _items[overrideId].Content;

    public string GetContentPath(string overrideId) => string.Empty;

    public void SaveOverrides(IReadOnlyList<OverrideProfile> overrides) { }

    public void Delete(string overrideId) => _items.Remove(overrideId);
}

public sealed class InMemoryRuleOverrideStore : IRuleOverrideStore
{
    private readonly Dictionary<string, RuleOverrideSet> _items = new();

    public RuleOverrideSet Load(string subscriptionId) => _items.TryGetValue(subscriptionId, out var set) ? set : new RuleOverrideSet(subscriptionId);

    public void Save(RuleOverrideSet set) => _items[set.SubscriptionId] = set;

    public void UpsertTemplate(RuleTemplate template) { }

    public void DeleteTemplate(string templateId) { }

    public void Delete(string subscriptionId) => _items.Remove(subscriptionId);
}

/// <summary>
/// 纯 YAML 键级合并，语义对齐 Rust hub_overrides_apply_yaml：数组键整体替换；
/// 测试不依赖 native 库。
/// </summary>
public sealed class TestOverrideEngine : IConfigOverrideEngine
{
    public string Apply(string baseConfigContent, RuntimeOverride runtimeOverride)
    {
        if (runtimeOverride.Format == OverrideFormat.JavaScript)
        {
            throw new NotSupportedException("JS overrides are not used in these tests");
        }

        return YamlReplaceMerge(baseConfigContent, runtimeOverride.Content);
    }

    private static string YamlReplaceMerge(string baseContent, string overrideContent)
    {
        var baseRoot = LoadRoot(baseContent);
        var overrideRoot = LoadRoot(overrideContent);
        foreach (var (key, value) in overrideRoot.Children)
        {
            baseRoot.Children[key] = value;
        }

        var merged = new YamlMappingNode();
        foreach (var (key, value) in baseRoot.Children)
        {
            merged.Children[key] = value;
        }

        var stream = new YamlStream(new YamlDocument(merged));
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    private static YamlMappingNode LoadRoot(string content)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(content));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }
}

public sealed class RuleOverrideServiceTests
{
    private const string SubscriptionContent = """
        mixed-port: 7890
        proxies:
          - name: node-a
            type: ss
        proxy-groups:
          - name: auto
            type: select
        rules:
          - MATCH,auto
        """;

    private const string OverrideContent = """
        proxy-groups:
          - name: auto
            type: select
          - name: company
            type: select
        rules:
          - IP-CIDR,192.168.0.0/24,company,no-resolve
          - DOMAIN-SUFFIX,example.org,auto
          - MATCH,company
        """;

    private static (RuleOverrideService Service, ISubscriptionSelectionStore Selection) CreateService(
        string overrideProfileContent = OverrideContent)
    {
        var subscriptionStore = new InMemorySubscriptionStore();
        var selectionStore = new InMemorySelectionStore();
        var profileStore = new InMemoryOverrideProfileStore();
        var ruleOverrideStore = new InMemoryRuleOverrideStore();

        subscriptionStore.Save(
            new DomainSubscription("sub-1", "Test subscription", "https://example.com/sub", false, DateTimeOffset.UtcNow, OverrideIds: ["ov-1"]),
            SubscriptionContent);
        profileStore.Save(
            new OverrideProfile("ov-1", "Test override", OverrideSourceType.Local, OverrideFormat.Yaml, "", DateTimeOffset.UtcNow),
            overrideProfileContent);
        selectionStore.SetCurrentSubscriptionId("sub-1");

        var service = new RuleOverrideService(
            subscriptionStore,
            selectionStore,
            ruleOverrideStore,
            new RuleParser(),
            overrideEngine: new TestOverrideEngine(),
            profileOverrideStore: profileStore);
        return (service, selectionStore);
    }

    [Fact(DisplayName = "Rule page snapshot applies selected override rules to builtin list")]
    public void LoadCurrentAppliesSelectedOverrideRulesToBuiltinList()
    {
        var (service, _) = CreateService();

        var snapshot = service.LoadCurrent();

        var cidr = Assert.Single(snapshot.Items, item => item.Type == "IP-CIDR");
        Assert.Equal("192.168.0.0/24", cidr.Payload);
        Assert.Equal("company", cidr.Proxy);
        // 覆写是替换式 rules，订阅自己的 MATCH 不再生效，显示覆写的 MATCH。
        Assert.Equal("company", Assert.Single(snapshot.Items, item => item.Type == "MATCH").Proxy);
    }

    [Fact(DisplayName = "Rule page snapshot offers override proxy groups in outbound targets")]
    public void LoadCurrentOffersOverrideProxyGroupsInProxyOptions()
    {
        var (service, _) = CreateService();

        var snapshot = service.LoadCurrent();

        Assert.Contains("company", snapshot.ProxyOptions);
        Assert.Contains("auto", snapshot.ProxyOptions);
    }

    [Fact(DisplayName = "Rule page snapshot falls back to subscription content when override fails")]
    public void LoadCurrentFallsBackToSubscriptionContentWhenOverrideFails()
    {
        var (service, _) = CreateService(overrideProfileContent: "rules: [ unclosed");

        var snapshot = service.LoadCurrent();

        Assert.NotEmpty(snapshot.Items);
        Assert.Equal("auto", Assert.Single(snapshot.Items, item => item.Type == "MATCH").Proxy);
        Assert.DoesNotContain(snapshot.ProxyOptions, option => option == "company");
    }

    [Fact(DisplayName = "Rule page snapshot keeps subscription rules when no override selected")]
    public void LoadCurrentKeepsSubscriptionRulesWhenNoOverrideSelected()
    {
        var subscriptionStore = new InMemorySubscriptionStore();
        var selectionStore = new InMemorySelectionStore();
        subscriptionStore.Save(
            new DomainSubscription("sub-1", "Test subscription", "https://example.com/sub", false, DateTimeOffset.UtcNow),
            SubscriptionContent);
        selectionStore.SetCurrentSubscriptionId("sub-1");
        var service = new RuleOverrideService(
            subscriptionStore,
            selectionStore,
            new InMemoryRuleOverrideStore(),
            new RuleParser(),
            overrideEngine: new TestOverrideEngine(),
            profileOverrideStore: new InMemoryOverrideProfileStore());

        var snapshot = service.LoadCurrent();

        Assert.Equal("auto", Assert.Single(snapshot.Items, item => item.Type == "MATCH").Proxy);
        Assert.DoesNotContain(snapshot.ProxyOptions, option => option == "company");
    }

    [Fact(DisplayName = "Duplicate validation counts override rules when saving custom rules")]
    public void SaveRejectsCustomRuleMatchingOverrideRule()
    {
        var (service, _) = CreateService();

        // 覆写规则 IP-CIDR,192.168.0.0/24 与自定义规则撞匹配条件，应拒绝保存。
        var exception = Assert.Throws<RuleOverrideException>(() => service.Save(
            "sub-1",
            [new EditableRule("custom-1", "IP-CIDR", "192.168.0.0/24", "other", "no-resolve")],
            new HashSet<string>(StringComparer.Ordinal)));

        Assert.Equal(RuleOverrideError.DuplicateBuiltinRule, exception.Error);
    }

    [Fact(DisplayName = "Duplicate validation allows custom rules on subscription without override")]
    public void SaveAllowsCustomRuleWhenOverrideNoLongerSelected()
    {
        var subscriptionStore = new InMemorySubscriptionStore();
        var selectionStore = new InMemorySelectionStore();
        subscriptionStore.Save(
            new DomainSubscription("sub-2", "Test subscription 2", "https://example.com/sub2", false, DateTimeOffset.UtcNow),
            SubscriptionContent);
        selectionStore.SetCurrentSubscriptionId("sub-2");
        var service = new RuleOverrideService(
            subscriptionStore,
            selectionStore,
            new InMemoryRuleOverrideStore(),
            new RuleParser(),
            overrideEngine: new TestOverrideEngine(),
            profileOverrideStore: new InMemoryOverrideProfileStore());

        service.Save(
            "sub-2",
            [new EditableRule("custom-1", "IP-CIDR", "192.168.0.0/24", "other", "no-resolve")],
            new HashSet<string>(StringComparer.Ordinal));

        // 保存成功即通过（不抛 RuleOverrideException）。
    }
}
