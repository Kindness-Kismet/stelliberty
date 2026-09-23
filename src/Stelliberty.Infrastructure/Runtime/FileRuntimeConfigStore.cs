using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Infrastructure.Runtime;

public sealed class FileRuntimeConfigStore(string runtimeDirectory) : ISelectedSubscriptionRuntimeStore
{
    private readonly string _runtimeDirectory = runtimeDirectory;

    public void Save(Subscription subscription, string originalContent, string effectiveConfigContent, string runtimeConfigContent)
    {
        var subscriptionRuntimeDirectory = Path.Combine(_runtimeDirectory, subscription.Id);
        Directory.CreateDirectory(subscriptionRuntimeDirectory);

        File.WriteAllText(Path.Combine(subscriptionRuntimeDirectory, "original.yaml"), originalContent);
        File.WriteAllText(Path.Combine(subscriptionRuntimeDirectory, "effective.yaml"), effectiveConfigContent);
        File.WriteAllText(Path.Combine(subscriptionRuntimeDirectory, "runtime.yaml"), runtimeConfigContent);
        AppLogger.Info($"Runtime config generated: {subscription.Name}");
    }

    public void SaveEmpty(string runtimeConfigContent)
    {
        var emptyRuntimeDirectory = Path.Combine(_runtimeDirectory, "empty");
        Directory.CreateDirectory(emptyRuntimeDirectory);

        File.WriteAllText(Path.Combine(emptyRuntimeDirectory, "runtime.yaml"), runtimeConfigContent);
        AppLogger.Info("Empty runtime config generated");
    }

    public string ReadRuntimeConfig(string subscriptionId)
    {
        return File.ReadAllText(Path.Combine(_runtimeDirectory, subscriptionId, "runtime.yaml"));
    }

    public void Delete(string subscriptionId)
    {
        var subscriptionRuntimeDirectory = Path.Combine(_runtimeDirectory, subscriptionId);
        if (Directory.Exists(subscriptionRuntimeDirectory))
        {
            Directory.Delete(subscriptionRuntimeDirectory, true);
        }

        AppLogger.Info($"Runtime config deleted: {subscriptionId}");
    }
}
