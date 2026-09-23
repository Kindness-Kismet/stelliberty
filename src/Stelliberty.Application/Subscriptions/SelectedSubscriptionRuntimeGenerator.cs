using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Overrides;
using Stelliberty.Application.Runtime;
using Stelliberty.Application.Rules;

namespace Stelliberty.Application.Subscriptions;

public sealed class SelectedSubscriptionRuntimeGenerator(
    ISubscriptionStore subscriptionStore,
    ISubscriptionSelectionStore selectionStore,
    RuntimeConfigGenerator runtimeConfigGenerator,
    IOverrideStore? overrideStore = null,
    ISelectedSubscriptionRuntimeStore? runtimeStore = null,
    SubscriptionChainProxyRuntimeApplier? chainProxyApplier = null,
    RuleOverrideService? ruleOverrideService = null)
{
    private readonly SubscriptionChainProxyRuntimeApplier _chainProxyApplier = chainProxyApplier ?? new SubscriptionChainProxyRuntimeApplier();
    private readonly SubscriptionOverrideResolver _overrideResolver = new(overrideStore);
    private readonly RuleOverrideService? _ruleOverrideService = ruleOverrideService;

    public SelectedSubscriptionRuntimeResult Generate(SelectedSubscriptionRuntimeRequest request)
    {
        var subscriptionId = selectionStore.GetCurrentSubscriptionId()
            ?? throw new InvalidOperationException("No subscription is selected");
        return Generate(subscriptionId, request);
    }

    public SelectedSubscriptionRuntimeResult Generate(string subscriptionId, SelectedSubscriptionRuntimeRequest request)
    {
        var subscription = subscriptionStore.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId)
            ?? throw new InvalidOperationException($"Selected subscription not found: {subscriptionId}");
        var originalContent = ReadOriginalContent(subscription);

        // 基线由 PostOverrideTransform 回填；初值仅为满足确定赋值，transform 必定执行一次。
        var effectiveContent = originalContent;
        var runtimeConfig = runtimeConfigGenerator.Generate(new RuntimeConfigGenerationRequest(
            BaseConfigContent: originalContent,
            Overrides: _overrideResolver.Resolve(subscription).Concat(request.Overrides).ToList(),
            RuntimeParams: request.RuntimeParams,
            // 自定义规则最后定稿，避免订阅覆写改写用户编辑结果。
            PostOverrideTransform: content =>
            {
                effectiveContent = DisableBrokenChainProxiesAndApply(subscription.Id, content);
                return ApplyRuntimeRuleOverrides(subscription.Id, effectiveContent);
            }));
        runtimeStore?.Save(subscription, originalContent, effectiveContent, runtimeConfig.RuntimeConfigContent);

        return new SelectedSubscriptionRuntimeResult(
            subscription,
            runtimeConfig.RuntimeConfigContent);
    }

    private string ReadOriginalContent(Subscription subscription)
    {
        try
        {
            return subscriptionStore.ReadContent(subscription.Id);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Selected subscription content is missing or unreadable: {subscription.Name}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException($"Selected subscription content is missing or unreadable: {subscription.Name}", exception);
        }
    }

    // 会把失效链式回写为禁用，再产出规则页基线。
    private string DisableBrokenChainProxiesAndApply(string subscriptionId, string content)
    {
        var subscription = DisableBrokenChainProxies(subscriptionId, content);
        return _chainProxyApplier.Apply(content, subscription);
    }

    private string ApplyRuntimeRuleOverrides(string subscriptionId, string effectiveContent)
    {
        _ruleOverrideService?.DisableCustomRulesWithMissingOutbound(subscriptionId, effectiveContent);
        return _ruleOverrideService?.Apply(subscriptionId, effectiveContent) ?? effectiveContent;
    }

    // 失效链式先保存为禁用，再按禁用后的订阅生成配置。
    private Subscription DisableBrokenChainProxies(string subscriptionId, string content)
    {
        var subscription = subscriptionStore.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId)
            ?? throw new InvalidOperationException($"Selected subscription not found: {subscriptionId}");
        var inspection = _chainProxyApplier.Inspect(content, subscription);
        if (!inspection.HasBrokenChains)
        {
            return subscription;
        }

        var invalidIds = inspection.InvalidCustomChainIds.ToHashSet(StringComparer.Ordinal);
        var updated = subscription with
        {
            DisabledBuiltinChainProxyNames = subscription.DisabledBuiltinChainProxyNames
                .Concat(inspection.BrokenBuiltinChainProxyNames)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            CustomChainProxies = subscription.CustomChainProxies
                .Select(item => invalidIds.Contains(item.Id) ? item with { IsEnabled = false } : item)
                .ToList()
        };
        subscriptionStore.UpdateSubscription(updated);
        AppLogger.Warning(
            $"Chain proxies disabled for {subscription.Name}: "
            + $"builtin=[{string.Join(", ", inspection.BrokenBuiltinChainProxyNames)}], "
            + $"custom=[{string.Join(", ", inspection.InvalidCustomChainIds)}]");
        return updated;
    }
}
