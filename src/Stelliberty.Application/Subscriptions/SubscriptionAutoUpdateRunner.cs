using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionAutoUpdateRunner(
    ISubscriptionStore subscriptionStore,
    SubscriptionAutoUpdatePlanner planner,
    SubscriptionUpdater updater)
{
    public async Task<SubscriptionUpdateResult> RunStartupUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var plan = planner.PlanStartupUpdates(subscriptionStore.LoadSubscriptions());
        return await RunPlanAsync(plan, cancellationToken);
    }

    public async Task<SubscriptionUpdateResult> RunDueIntervalUpdatesAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var plan = planner.PlanDueIntervalUpdates(subscriptionStore.LoadSubscriptions(), now);
        return await RunPlanAsync(plan, cancellationToken);
    }

    private async Task<SubscriptionUpdateResult> RunPlanAsync(SubscriptionAutoUpdatePlan plan, CancellationToken cancellationToken)
    {
        var updatedIds = new List<string>();
        var skippedIds = new List<string>();
        foreach (var subscriptionId in plan.UpdateSubscriptionIds)
        {
            try
            {
                var result = await updater.UpdateAsync(subscriptionId, cancellationToken);
                updatedIds.AddRange(result.UpdatedSubscriptionIds);
                skippedIds.AddRange(result.SkippedSubscriptionIds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                AppLogger.Error(exception, $"Subscription auto update failed: {subscriptionId}");
                skippedIds.Add(subscriptionId);
            }
        }

        return new SubscriptionUpdateResult(updatedIds, skippedIds);
    }
}
