using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Updates;

namespace Stelliberty.Presentation.ViewModels;

public sealed partial class SubscriptionPageViewModel
{
    private async void OnAddRemoteRequested(object? sender, SubscriptionAddRemoteRequestedEventArgs args)
    {
        var minDisplayTask = Task.Delay(600);
        try
        {
            var item = await AddRemoteSubscriptionCoreAsync(args);
            await minDisplayTask;
            AddDialog.Close();
            ShowSuccessToast("Subscriptions.Toast.ImportRemoteSucceeded", item.Name);
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Remote subscription import failed");
            await minDisplayTask;
            AddDialog.EndSubmit();
            ShowErrorToast("Subscriptions.Toast.ImportRemoteFailed");
        }
    }

    private async void OnAddLocalRequested(object? sender, SubscriptionAddLocalRequestedEventArgs args)
    {
        var minDisplayTask = Task.Delay(600);
        try
        {
            var item = AddLocalSubscriptionCore(args);
            await minDisplayTask;
            AddDialog.Close();
            ShowSuccessToast("Subscriptions.Toast.ImportLocalSucceeded", item.Name);
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Local subscription import failed");
            await minDisplayTask;
            AddDialog.EndSubmit();
            ShowErrorToast("Subscriptions.Toast.ImportLocalFailed");
        }
    }

    public async Task<SubscriptionItemViewModel> AddRemoteSubscriptionAsync(SubscriptionAddRemoteRequestedEventArgs args, CancellationToken cancellationToken = default)
    {
        try
        {
            var item = await AddRemoteSubscriptionCoreAsync(args, cancellationToken);
            ShowSuccessToast("Subscriptions.Toast.ImportRemoteSucceeded", item.Name);
            return item;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            ShowErrorToast("Subscriptions.Toast.ImportRemoteFailed");
            throw;
        }
    }

    public SubscriptionItemViewModel AddLocalSubscription(SubscriptionAddLocalRequestedEventArgs args)
    {
        try
        {
            var item = AddLocalSubscriptionCore(args);
            ShowSuccessToast("Subscriptions.Toast.ImportLocalSucceeded", item.Name);
            return item;
        }
        catch
        {
            ShowErrorToast("Subscriptions.Toast.ImportLocalFailed");
            throw;
        }
    }

    private async Task<SubscriptionItemViewModel> AddRemoteSubscriptionCoreAsync(SubscriptionAddRemoteRequestedEventArgs args, CancellationToken cancellationToken = default)
    {
        var importer = _remoteSubscriptionImporter
            ?? throw new InvalidOperationException("Remote subscription importer is not initialized");
        var subscription = await importer.ImportAsync(new RemoteSubscriptionImportRequest(
            args.Name,
            args.Url,
            args.UserAgent,
            args.AutoTestDelayIntervalMinutes,
            args.AutoUpdateMode,
            args.AutoUpdateIntervalMinutes,
            args.UpdateProxyMode,
            args.AgeSecretKey),
            cancellationToken);
        var item = ToSubscriptionItem(subscription);
        AddSubscription(item);
        AppLogger.Info($"Subscription page received remote import: {subscription.Name}");
        return item;
    }

    private SubscriptionItemViewModel AddLocalSubscriptionCore(SubscriptionAddLocalRequestedEventArgs args)
    {
        var importer = _localFileImporter
            ?? throw new InvalidOperationException("Local subscription importer is not initialized");
        var subscription = importer.Import(new LocalSubscriptionFileImportRequest(
            args.Name,
            args.LocalFilePath,
            args.AutoTestDelayIntervalMinutes));
        var item = ToSubscriptionItem(subscription);
        AddSubscription(item);
        AppLogger.Info($"Subscription page received local import: {subscription.Name}");
        return item;
    }

    public async Task UpdateSubscriptionAsync(string? subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = _subscriptions.FirstOrDefault(item => item.Id == subscriptionId);
        if (subscription is null)
        {
            return;
        }
        var updater = _subscriptionUpdater
            ?? throw new InvalidOperationException("Subscription updater is not initialized");

        if (_updateState.TryStartItemUpdate(new UpdateOperationItem(subscription.Id, CanUpdate: !subscription.IsLocalFile)) == UpdateStartResult.Skipped)
        {
            RaiseSubscriptionStateChanged();
            return;
        }

        RaiseSubscriptionStateChanged();
        SubscriptionUpdateStarting?.Invoke(this, [subscription.Id]);
        var minDisplayTask = Task.Delay(600);
        try
        {
            var result = await updater.UpdateAsync(subscription.Id, cancellationToken);
            await minDisplayTask;
            ApplySubscriptionUpdateResult(result);
            ShowSubscriptionUpdateToast(result.UpdatedSubscriptionIds.Contains(subscription.Id));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _updateState.CompleteItemUpdate(subscription.Id);
            RaiseSubscriptionStateChanged();
            throw;
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, $"Subscription update failed: {subscription.Name}");
            await minDisplayTask;
            _updateState.CompleteItemUpdate(subscription.Id);
            RaiseSubscriptionStateChanged();
            ShowSubscriptionUpdateToast(false);
        }
    }

    public async Task UpdateAllSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var subscriptionIds = GetPendingSubscriptionUpdateIds();
        if (subscriptionIds.Count == 0)
        {
            RaiseSubscriptionStateChanged();
            return;
        }
        var updater = _subscriptionUpdater
            ?? throw new InvalidOperationException("Subscription updater is not initialized");
        if (_updateState.TryStartBatchUpdate(subscriptionIds.Select(item => new UpdateOperationItem(item, CanUpdate: true)).ToList()) == UpdateStartResult.Skipped)
        {
            RaiseSubscriptionStateChanged();
            return;
        }

        RaiseSubscriptionStateChanged();
        SubscriptionUpdateStarting?.Invoke(this, subscriptionIds);
        var minDisplayTask = Task.Delay(600);
        try
        {
            var result = await updater.UpdateManyAsync(subscriptionIds, cancellationToken);
            await minDisplayTask;
            ApplySubscriptionUpdateResult(result);
            ShowSubscriptionBatchUpdateToast(result.UpdatedSubscriptionIds.Count, result.SkippedSubscriptionIds.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _updateState.CompleteBatchUpdate();
            RaiseSubscriptionStateChanged();
            throw;
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Updating all subscriptions failed");
            await minDisplayTask;
            _updateState.CompleteBatchUpdate();
            RaiseSubscriptionStateChanged();
            ShowSubscriptionBatchUpdateToast(0, subscriptionIds.Count);
        }
    }

    private void ShowSubscriptionUpdateToast(bool isSuccessful)
    {
        ShowToast(
            Localize(isSuccessful ? "Subscriptions.Toast.UpdateSucceeded" : "Subscriptions.Toast.UpdateFailed"),
            isSuccessful ? ToastType.Success : ToastType.Error);
    }

    private void ShowSubscriptionBatchUpdateToast(int succeededCount, int failedCount)
    {
        var type = failedCount == 0
            ? ToastType.Success
            : succeededCount == 0 ? ToastType.Error : ToastType.Warning;
        ShowToast(string.Format(Localize("Subscriptions.Toast.UpdateAllCompleted"), succeededCount, failedCount), type);
    }

    public void ApplySubscriptionUpdateResult(SubscriptionUpdateResult result)
    {
        var resultIds = result.UpdatedSubscriptionIds.Concat(result.SkippedSubscriptionIds).ToHashSet(StringComparer.Ordinal);
        var completesBatch = resultIds.Any(_updateState.IsBatchUpdatingItem);
        if (!completesBatch && !resultIds.Any(_updateState.IsUpdating))
        {
            _updateState.StartBatchUpdate(resultIds.Select(id => new UpdateOperationItem(id, CanUpdate: result.UpdatedSubscriptionIds.Contains(id))).ToList());
            completesBatch = true;
        }

        foreach (var subscriptionId in result.SkippedSubscriptionIds)
        {
            _updateState.MarkItemSkipped(subscriptionId);
            _updateState.CompleteItemUpdate(subscriptionId);
        }

        foreach (var subscriptionId in result.UpdatedSubscriptionIds)
        {
            _updateState.CompleteItemUpdate(subscriptionId, isUpdated: true);
        }

        foreach (var subscriptionId in result.UpdatedSubscriptionIds)
        {
            _providerSnapshots.Remove(subscriptionId);
        }
        RefreshPersistedSubscriptionRows(result.UpdatedSubscriptionIds.Concat(result.SkippedSubscriptionIds));
        if (completesBatch)
        {
            _updateState.CompleteBatchUpdate();
        }

        RaiseSubscriptionStateChanged();
        SubscriptionsUpdated?.Invoke(this, result);
        _ = RefreshProviderTrafficAsync();
    }

    private IReadOnlyList<string> GetPendingSubscriptionUpdateIds()
    {
        return _subscriptions
            .Where(item => !item.IsLocalFile && !_updateState.IsUpdating(item.Id))
            .Select(item => item.Id)
            .ToList();
    }

    private void RefreshPersistedSubscriptionRows(IEnumerable<string> subscriptionIds)
    {
        if (_subscriptionStore is null)
        {
            return;
        }

        var refreshedSubscriptions = _subscriptionStore.LoadSubscriptions()
            .Where(subscription => subscriptionIds.Contains(subscription.Id, StringComparer.Ordinal))
            .ToDictionary(subscription => subscription.Id, StringComparer.Ordinal);
        for (var index = 0; index < _subscriptions.Count; index++)
        {
            if (refreshedSubscriptions.TryGetValue(_subscriptions[index].Id, out var subscription))
            {
                _subscriptions[index] = ToSubscriptionItem(subscription, subscription.Id == _currentSubscriptionId);
            }
        }
    }
}
