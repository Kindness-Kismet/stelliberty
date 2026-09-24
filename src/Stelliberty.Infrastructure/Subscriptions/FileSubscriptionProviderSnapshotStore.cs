using System.Text.Json;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Infrastructure.Storage;

namespace Stelliberty.Infrastructure.Subscriptions;

// 跟随订阅运行目录清理，后台采集不会改写订阅元数据。
public sealed class FileSubscriptionProviderSnapshotStore(string runtimeDirectory) : ISubscriptionProviderSnapshotStore
{
    public SubscriptionProviderSnapshot? Load(string subscriptionId) =>
        JsonFileRecovery.ReadOrRecover<SubscriptionProviderSnapshot>(PathFor(subscriptionId));

    public void Save(SubscriptionProviderSnapshot snapshot)
    {
        var path = PathFor(snapshot.SubscriptionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(snapshot));
    }

    private string PathFor(string subscriptionId) => Path.Combine(runtimeDirectory, subscriptionId, "providers.json");
}
