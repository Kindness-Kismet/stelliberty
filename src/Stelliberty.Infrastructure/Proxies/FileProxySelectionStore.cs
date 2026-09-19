using System.Text.Json;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Proxies;
using Stelliberty.Infrastructure.Storage;

namespace Stelliberty.Infrastructure.Proxies;

public sealed class FileProxySelectionStore(string rootDirectory) : IProxySelectionStore
{
    private readonly string _statePath = Path.Combine(rootDirectory, "proxies", "selection_state.json");

    public IReadOnlyDictionary<string, string> GetSelections(string subscriptionId)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using (new NamedFileLock(_statePath))
        {
            var state = ReadState();
            return state.Subscriptions.TryGetValue(subscriptionId, out var selections)
                ? new Dictionary<string, string>(selections, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    public void SetSelection(string subscriptionId, string groupName, string proxyName)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId)
            || string.IsNullOrWhiteSpace(groupName)
            || string.IsNullOrWhiteSpace(proxyName))
        {
            return;
        }

        using (new NamedFileLock(_statePath))
        {
            var state = ReadState();
            if (!state.Subscriptions.TryGetValue(subscriptionId, out var selections))
            {
                selections = new Dictionary<string, string>(StringComparer.Ordinal);
                state.Subscriptions[subscriptionId] = selections;
            }

            selections[groupName] = proxyName;
            WriteState(state);
        }

        AppLogger.Info($"Proxy selection saved: subscription={subscriptionId} group={groupName} proxy={proxyName}");
    }

    public void RemoveSelection(string subscriptionId, string groupName)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId) || string.IsNullOrWhiteSpace(groupName))
        {
            return;
        }

        using (new NamedFileLock(_statePath))
        {
            var state = ReadState();
            if (!state.Subscriptions.TryGetValue(subscriptionId, out var selections)
                || !selections.Remove(groupName))
            {
                return;
            }

            if (selections.Count == 0)
            {
                state.Subscriptions.Remove(subscriptionId);
            }

            WriteState(state);
        }

        AppLogger.Info($"Proxy selection deleted: subscription={subscriptionId} group={groupName}");
    }

    public void RemoveSubscription(string subscriptionId)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return;
        }

        using (new NamedFileLock(_statePath))
        {
            var state = ReadState();
            if (!state.Subscriptions.Remove(subscriptionId))
            {
                return;
            }

            WriteState(state);
        }

        AppLogger.Info($"Proxy selections deleted: subscription={subscriptionId}");
    }

    private SelectionState ReadState()
    {
        var state = JsonFileRecovery.ReadOrRecover<SelectionState>(_statePath);
        return state?.Subscriptions is not null
            ? state
            : new SelectionState(new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal));
    }

    private void WriteState(SelectionState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        AtomicFile.WriteAllText(_statePath, json);
    }

    private sealed record SelectionState(Dictionary<string, Dictionary<string, string>> Subscriptions);
}
