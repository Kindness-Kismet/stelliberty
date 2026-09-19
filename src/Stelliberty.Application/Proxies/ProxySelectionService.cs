using Stelliberty.Application.Connections;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Connections;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Application.Proxies;

// 代理选择先应用到核心，再由分组类型决定是否清理连接。
public sealed class ProxySelectionService(
    IProxyCoreClient? coreClient = null,
    IProxySelectionStore? selectionStore = null,
    ISubscriptionSelectionStore? subscriptionSelectionStore = null)
{
    // 核心拒绝时返回 null；applyToCore=false 只计算本地状态。
    public async Task<ProxySelectionResult?> SelectNodeAsync(
        ProxyConfig config,
        string groupName,
        string nodeName,
        bool applyToCore,
        CancellationToken cancellationToken = default)
    {
        AppLogger.Info($"Proxy selection requested: group={groupName} proxy={nodeName} applyCore={applyToCore.ToString().ToLowerInvariant()}");
        var subscriptionId = subscriptionSelectionStore?.GetCurrentSubscriptionId();
        var result = new ProxyGroupSelector(config).Select(groupName, nodeName);
        if (applyToCore && coreClient is not null)
        {
            if (!await coreClient.ChangeProxyAsync(result.ChangeRequest, cancellationToken))
            {
                AppLogger.Warning($"Proxy selection rejected by core: group={groupName} proxy={nodeName}");
                return null;
            }

            if (result.ShouldCloseConnections)
            {
                await coreClient.CloseConnectionsAsync(new ConnectionCloseRequest(ConnectionCloseMode.All), cancellationToken);
            }
        }

        // 固定选择不写入存储：重启后由还原流程清空，回到自动择优。
        if (!result.Config.Groups.Any(group => group.Name == groupName && group.UsesFixedSelection))
        {
            PersistSelection(subscriptionId, groupName, nodeName);
        }

        AppLogger.Info($"Proxy selection completed: group={groupName} proxy={nodeName} closeConnections={result.ShouldCloseConnections.ToString().ToLowerInvariant()}");
        return result;
    }

    // groupNames 为 null 表示全部分组。
    public async Task<ProxyFixedSelectionReleaseResult> ReleaseFixedSelectionsAsync(
        ProxyConfig config,
        IReadOnlyCollection<string>? groupNames,
        bool applyToCore,
        CancellationToken cancellationToken = default)
    {
        var scope = groupNames?.ToHashSet(StringComparer.Ordinal);
        var targets = config.Groups
            .Where(group => group.UsesFixedSelection
                && !string.IsNullOrWhiteSpace(group.Fixed)
                && (scope is null || scope.Contains(group.Name)))
            .ToList();
        if (targets.Count == 0)
        {
            return new ProxyFixedSelectionReleaseResult(config, []);
        }

        var released = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in targets)
        {
            if (applyToCore && coreClient is not null
                && !await coreClient.ClearProxySelectionAsync(group.Name, cancellationToken))
            {
                AppLogger.Warning($"Fixed proxy selection release failed: group={group.Name} proxy={group.Fixed}");
                continue;
            }

            released.Add(group.Name);
            // 固定选择已不写入存储，此处只清理早期版本留下的记录。
            RemovePersistedSelection(group.Name);
            AppLogger.Info($"Fixed proxy selection released: group={group.Name} proxy={group.Fixed}");
        }

        if (released.Count == 0)
        {
            return new ProxyFixedSelectionReleaseResult(config, []);
        }

        var groups = config.Groups
            .Select(group => released.Contains(group.Name) ? group with { Fixed = null } : group)
            .ToList();
        return new ProxyFixedSelectionReleaseResult(config with { Groups = groups }, [.. released]);
    }

    private void PersistSelection(string? subscriptionId, string groupName, string nodeName)
    {
        if (selectionStore is null || string.IsNullOrWhiteSpace(subscriptionId))
        {
            return;
        }

        selectionStore.SetSelection(subscriptionId, groupName, nodeName);
    }

    private void RemovePersistedSelection(string groupName)
    {
        var subscriptionId = subscriptionSelectionStore?.GetCurrentSubscriptionId();
        if (selectionStore is null || string.IsNullOrWhiteSpace(subscriptionId))
        {
            return;
        }

        selectionStore.RemoveSelection(subscriptionId, groupName);
    }
}
