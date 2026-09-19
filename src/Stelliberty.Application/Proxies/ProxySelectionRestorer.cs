using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Application.Proxies;

public sealed class ProxySelectionRestorer(
    IProxyCoreClient coreClient,
    IProxyConfigProvider coreConfigProvider,
    IProxyConfigProvider selectedRuntimeConfigProvider,
    StoredProxySelectionConfigProvider selectionProvider,
    ProxySelectionSyncState syncState,
    ISubscriptionSelectionStore subscriptionSelectionStore)
{
    private static readonly TimeSpan LoadRetryDelay = TimeSpan.FromMilliseconds(250);
    private const int LoadMaxAttempts = 20;
    private const int StableSnapshotCount = 3;

    public async Task RestoreCurrentSubscriptionAsync(CancellationToken cancellationToken = default, bool preserveFixedSelections = false)
    {
        var subscriptionId = SuspendCoreSelectionImport("startup");
        await RestoreSubscriptionAsync(subscriptionId, "startup", cancellationToken, preserveFixedSelections);
    }

    public string? SuspendCoreSelectionImport(string origin)
    {
        syncState.DisableCoreSelectionImport();
        var subscriptionId = subscriptionSelectionStore.GetCurrentSubscriptionId();
        AppLogger.Info($"Proxy selection import suspended: origin={origin} subscription={subscriptionId ?? "none"}");
        return subscriptionId;
    }

    public async Task RestoreSubscriptionAsync(
        string? subscriptionId,
        string origin,
        CancellationToken cancellationToken = default,
        bool preserveFixedSelections = false)
    {
        syncState.DisableCoreSelectionImport();
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            AppLogger.Info($"Proxy selection restore skipped: origin={origin} reason=no-subscription");
            return;
        }

        if (!IsCurrentSubscription(subscriptionId))
        {
            AppLogger.Info($"Proxy selection restore skipped: origin={origin} subscription={subscriptionId} reason=selection-changed");
            return;
        }

        AppLogger.Info($"Proxy selection restore started: origin={origin} subscription={subscriptionId}");
        var canImportCoreSelections = false;
        try
        {
            var expectedConfig = await selectedRuntimeConfigProvider.LoadAsync(cancellationToken);
            var config = await LoadRuntimeConfigAsync(expectedConfig, cancellationToken);
            if (!IsCurrentSubscription(subscriptionId))
            {
                AppLogger.Info($"Proxy selection restore skipped: origin={origin} subscription={subscriptionId} reason=selection-changed");
                return;
            }

            config = selectionProvider.ApplyStoredSelections(config, subscriptionId);
            var restoredCount = 0;
            var clearedCount = 0;
            var failedCount = 0;
            foreach (var group in config.Groups)
            {
                if (!IsCurrentSubscription(subscriptionId))
                {
                    AppLogger.Info($"Proxy selection restore interrupted: origin={origin} subscription={subscriptionId} reason=selection-changed");
                    return;
                }

                var selection = group.UserSelectionName;
                if (!group.IsManualSelectable)
                {
                    continue;
                }

                if (group.UsesFixedSelection)
                {
                    // 重建界面不能清除用户刚从托盘固定的节点。
                    if (preserveFixedSelections) continue;
                    // 核心重建时清空固定选择，避免 store-selected 恢复旧固定项。
                    var cleared = await coreClient.ClearProxySelectionAsync(group.Name, cancellationToken);
                    if (cleared)
                    {
                        clearedCount++;
                        AppLogger.Info($"Pinned proxy selection cleared: group={group.Name}");
                    }
                    else
                    {
                        failedCount++;
                        AppLogger.Warning($"Pinned proxy selection clear failed: group={group.Name}");
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(selection))
                {
                    continue;
                }

                var restored = await coreClient.ChangeProxyAsync(new ProxyChangeRequest(group.Name, selection), cancellationToken);
                if (restored)
                {
                    restoredCount++;
                    AppLogger.Info($"Proxy selection restored: group={group.Name} proxy={selection}");
                }
                else
                {
                    failedCount++;
                    AppLogger.Warning($"Proxy selection restore failed: group={group.Name} proxy={selection}");
                }
            }

            AppLogger.Info($"Proxy selection restore completed: origin={origin} subscription={subscriptionId} restored={restoredCount} cleared={clearedCount} failed={failedCount}");
            canImportCoreSelections = failedCount == 0;
            if (!canImportCoreSelections)
            {
                throw new InvalidOperationException($"Proxy selection restore incomplete: failed={failedCount}");
            }

            if (!IsCurrentSubscription(subscriptionId))
            {
                canImportCoreSelections = false;
                AppLogger.Info($"Proxy selection restore invalidated: origin={origin} subscription={subscriptionId} reason=selection-changed");
                return;
            }

            selectionProvider.PruneInvalidStoredSelections(config, subscriptionId);
        }
        finally
        {
            if (canImportCoreSelections && IsCurrentSubscription(subscriptionId))
            {
                syncState.EnableCoreSelectionImport();
            }
            else
            {
                // 还原成功前，不允许核心状态覆盖本地状态。
                syncState.DisableCoreSelectionImport();
            }
        }
    }

    private bool IsCurrentSubscription(string subscriptionId)
    {
        return string.Equals(
            subscriptionSelectionStore.GetCurrentSubscriptionId(),
            subscriptionId,
            StringComparison.Ordinal);
    }

    private async Task<ProxyConfig> LoadRuntimeConfigAsync(
        ProxyConfig expectedConfig,
        CancellationToken cancellationToken)
    {
        ProxyConfig? previous = null;
        ProxyConfig? latest = null;
        var stableCount = 0;
        // 目标配置连续稳定后再恢复，避免核心切换期间命中过渡快照。
        for (var attempt = 1; attempt <= LoadMaxAttempts; attempt++)
        {
            var config = await coreConfigProvider.LoadAsync(cancellationToken);
            latest = config;

            if (config.IsFullyResolved
                && MatchesExpectedConfig(expectedConfig, config)
                && IsSameGroupSnapshot(previous, config))
            {
                stableCount++;
                if (stableCount >= StableSnapshotCount)
                {
                    return config;
                }
            }
            else
            {
                stableCount = 0;
            }

            previous = config;

            if (attempt == LoadMaxAttempts)
            {
                break;
            }

            await Task.Delay(LoadRetryDelay, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Core proxy groups did not match the selected runtime config: expected={FormatGroupNames(expectedConfig)} actual={FormatGroupNames(latest)}");
    }

    private static bool IsSameGroupSnapshot(ProxyConfig? previous, ProxyConfig current)
    {
        if (previous is null || previous.Groups.Count != current.Groups.Count)
        {
            return false;
        }

        return previous.Groups.Zip(current.Groups).All(pair =>
            string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal)
            && string.Equals(pair.First.Type, pair.Second.Type, StringComparison.Ordinal)
            && pair.First.All.SequenceEqual(pair.Second.All, StringComparer.Ordinal));
    }

    private static bool MatchesExpectedConfig(ProxyConfig expected, ProxyConfig actual)
    {
        var expectedGroups = expected.Groups
            .Where(group => !IsGlobalGroup(group))
            .ToDictionary(group => group.Name, StringComparer.Ordinal);
        var actualGroups = actual.Groups
            .Where(group => !IsGlobalGroup(group))
            .ToDictionary(group => group.Name, StringComparer.Ordinal);
        if (expectedGroups.Count != actualGroups.Count
            || !expected.Nodes.Keys.All(actual.Nodes.ContainsKey))
        {
            return false;
        }

        foreach (var (name, expectedGroup) in expectedGroups)
        {
            if (!actualGroups.TryGetValue(name, out var actualGroup)
                || !string.Equals(
                    NormalizeGroupType(expectedGroup.Type),
                    NormalizeGroupType(actualGroup.Type),
                    StringComparison.Ordinal)
                || !expectedGroup.All.All(actualGroup.All.Contains))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsGlobalGroup(ProxyGroup group)
    {
        return string.Equals(group.Name, "GLOBAL", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeGroupType(string type)
    {
        var normalized = type.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized == "selector" ? "select" : normalized;
    }

    private static string FormatGroupNames(ProxyConfig? config)
    {
        return config is null
            ? "unavailable"
            : string.Join(',', config.Groups.Where(group => !IsGlobalGroup(group)).Select(group => group.Name));
    }
}
