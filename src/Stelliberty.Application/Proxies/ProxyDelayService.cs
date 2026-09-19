using System.Collections.Concurrent;
using System.Diagnostics;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Application.Proxies;

public sealed class ProxyDelayService(IProxyDelayTester tester, IProxyDelayResultSink resultSink)
{
    // 所有测速共享并发预算，避免单测与批测叠加超出核心承载。
    private const int DelayTestConcurrency = 15;
    private readonly SemaphoreSlim _delayTestSemaphore = new(DelayTestConcurrency);

    public async Task<ProxyDelayResult> TestNodeAsync(ProxyConfig config, string proxyName, CancellationToken cancellationToken = default)
    {
        if (!ProxyConfigSelectionNormalizer.HasEntry(config, proxyName))
        {
            return new ProxyDelayResult(config, [], [proxyName], []);
        }

        var scope = await CaptureScopeAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        var delay = await TestDelayAsync(config, proxyName, cancellationToken);
        await PublishAsync(scope, proxyName, delay, cancellationToken);
        if (delay >= 0)
        {
            AppLogger.Info($"Proxy delay test completed: proxy={proxyName} delay={delay}ms elapsed={stopwatch.Elapsed.TotalMilliseconds:0}ms");
        }
        else
        {
            AppLogger.Warning($"Proxy delay test failed: proxy={proxyName} elapsed={stopwatch.Elapsed.TotalMilliseconds:0}ms");
        }

        return new ProxyDelayResult(
            config.WithEntryDelay(proxyName, delay),
            [proxyName],
            [],
            delay < 0 ? [proxyName] : []);
    }

    public Task<ProxyDelayResult> TestGroupAsync(ProxyConfig config, string groupName, CancellationToken cancellationToken = default)
        => TestGroupAsync(config, groupName, [], null, cancellationToken);

    public Task<ProxyDelayResult> TestGroupAsync(
        ProxyConfig config,
        string groupName,
        IProgress<ProxyDelayProgress>? progress,
        CancellationToken cancellationToken = default)
        => TestGroupAsync(config, groupName, [], progress, cancellationToken);

    public Task<ProxyDelayResult> TestGroupAsync(
        ProxyConfig config,
        string groupName,
        IReadOnlyCollection<string> excludedProxyNames,
        IProgress<ProxyDelayProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var group = config.Groups.FirstOrDefault(item => item.Name == groupName)
            ?? throw new InvalidOperationException($"Proxy group not found: {groupName}");
        return TestNodesAsync(config, group.All, excludedProxyNames, $"group={group.Name}", progress, cancellationToken);
    }

    public Task<ProxyDelayResult> TestAllAsync(ProxyConfig config, CancellationToken cancellationToken = default)
        => TestAllAsync(config, [], null, cancellationToken);

    public Task<ProxyDelayResult> TestAllAsync(
        ProxyConfig config,
        IProgress<ProxyDelayProgress>? progress,
        CancellationToken cancellationToken = default)
        => TestAllAsync(config, [], progress, cancellationToken);

    public Task<ProxyDelayResult> TestAllAsync(
        ProxyConfig config,
        IReadOnlyCollection<string> excludedProxyNames,
        IProgress<ProxyDelayProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var proxyNames = config.Groups
            .SelectMany(group => group.All)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return TestNodesAsync(config, proxyNames, excludedProxyNames, "scope=all", progress, cancellationToken);
    }

    // 并发测试结束后再合并，避免工作任务修改快照。
    private async Task<ProxyDelayResult> TestNodesAsync(
        ProxyConfig config,
        IReadOnlyList<string> proxyNames,
        IReadOnlyCollection<string> excludedProxyNames,
        string scope,
        IProgress<ProxyDelayProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var targets = new List<string>();
        var skipped = new List<string>();
        var excluded = excludedProxyNames.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var proxyName in proxyNames)
        {
            if (!seen.Add(proxyName))
            {
                continue;
            }

            if (excluded.Contains(proxyName))
            {
                skipped.Add(proxyName);
            }
            else if (ProxyConfigSelectionNormalizer.HasEntry(config, proxyName))
            {
                targets.Add(proxyName);
            }
            else
            {
                skipped.Add(proxyName);
            }
        }

        var scopeId = await CaptureScopeAsync(cancellationToken);
        var delays = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var tasks = targets.Select(async proxyName =>
        {
            var delay = await TestDelayAsync(
                config,
                proxyName,
                cancellationToken,
                () => progress?.Report(new ProxyDelayProgress(proxyName, 0, IsCompleted: false)));
            delays[proxyName] = delay;
            progress?.Report(new ProxyDelayProgress(proxyName, delay));
            await PublishAsync(scopeId, proxyName, delay, cancellationToken);
        });
        await Task.WhenAll(tasks);
        cancellationToken.ThrowIfCancellationRequested();

        // 批量填充延迟，避免为每个项目重建完整配置。
        var testedDelays = new Dictionary<string, int>(StringComparer.Ordinal);
        var tested = new List<string>();
        var failed = new List<string>();
        foreach (var proxyName in targets)
        {
            var delay = delays[proxyName];
            testedDelays[proxyName] = delay;
            tested.Add(proxyName);
            if (delay < 0)
            {
                failed.Add(proxyName);
            }
        }

        LogBatchResult(scope, targets.Count, tested.Count - failed.Count, failed.Count, skipped.Count, stopwatch.Elapsed);
        return new ProxyDelayResult(config.WithEntryDelays(testedDelays), tested, skipped, failed);
    }

    private async Task<int> TestDelayAsync(
        ProxyConfig config,
        string proxyName,
        CancellationToken cancellationToken,
        Action? onStarted = null)
    {
        await _delayTestSemaphore.WaitAsync(cancellationToken);
        try
        {
            onStarted?.Invoke();
            if (tester is IProviderProxyDelayTester providerTester
                && config.Nodes.TryGetValue(proxyName, out var node)
                && !string.IsNullOrWhiteSpace(node.ProviderName))
            {
                return await providerTester.TestProviderDelayAsync(node.ProviderName, proxyName, cancellationToken);
            }

            return await tester.TestDelayAsync(proxyName, cancellationToken);
        }
        finally
        {
            _delayTestSemaphore.Release();
        }
    }

    private static void LogBatchResult(string scope, int total, int succeeded, int failed, int skipped, TimeSpan elapsed)
    {
        var message = $"Proxy delay batch completed: {scope} total={total} succeeded={succeeded} failed={failed} skipped={skipped} elapsed={elapsed.TotalMilliseconds:0}ms";
        if (failed > 0)
        {
            AppLogger.Warning(message);
            return;
        }

        AppLogger.Info(message);
    }

    private async Task<string?> CaptureScopeAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await resultSink.CaptureScopeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            AppLogger.Warning($"Proxy delay sharing unavailable: {exception.Message}");
            return null;
        }
    }

    private async Task PublishAsync(string? scope, string proxyName, int delay, CancellationToken cancellationToken)
    {
        if (scope is null) return;
        var publication = new ProxyDelayPublication(scope, proxyName, delay, DateTimeOffset.UtcNow);
        try
        {
            await resultSink.PublishAsync(publication, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // 展示通道失效不能抹掉已完成的测速结果。
            AppLogger.Warning($"Proxy delay sharing failed: {exception.Message}");
        }
    }
}
