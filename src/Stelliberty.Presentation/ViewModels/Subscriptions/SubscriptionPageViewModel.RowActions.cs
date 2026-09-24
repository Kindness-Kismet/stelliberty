using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Presentation.ViewModels;

public sealed partial class SubscriptionPageViewModel
{
    public SubscriptionRowMenuSelection? SelectedRowMenuAction
    {
        get => _selectedRowMenuAction;
        set
        {
            if (!SetProperty(ref _selectedRowMenuAction, value) || value is null)
            {
                return;
            }

            RunRowMenuAction(value);
            _selectedRowMenuAction = null;
            OnPropertyChanged();
        }
    }

    private void RunRowMenuAction(SubscriptionRowMenuSelection? selection)
    {
        if (selection is null)
        {
            return;
        }

        switch (selection.Action)
        {
            case SubscriptionRowMenuAction.Update:
                _ = UpdateSubscriptionAsync(selection.SubscriptionId);
                break;
            case SubscriptionRowMenuAction.Edit:
                ShowEditDialog(selection.SubscriptionId);
                break;
            case SubscriptionRowMenuAction.EditFile:
                EditFile(selection.SubscriptionId);
                break;
            case SubscriptionRowMenuAction.OpenExternalEditor:
                OpenExternalEditor(selection.SubscriptionId);
                break;
            case SubscriptionRowMenuAction.ChainProxy:
                ShowChainProxyDialog(selection.SubscriptionId);
                break;
            case SubscriptionRowMenuAction.Delete:
                ShowDeleteDialog(selection.SubscriptionId);
                break;
            case SubscriptionRowMenuAction.ViewRuntimeConfig:
                ShowRuntimeConfigDialog(selection.SubscriptionId);
                break;
            case SubscriptionRowMenuAction.OverrideSelector:
                ShowOverrideSelector(selection.SubscriptionId);
                break;
            case SubscriptionRowMenuAction.ProviderSelector:
                ShowProviderSelector(selection.SubscriptionId);
                break;
            case SubscriptionRowMenuAction.CopyLink:
                CopyLink(selection.SubscriptionId);
                break;
            case SubscriptionRowMenuAction.QrCode:
                ShowQrCode(selection.SubscriptionId);
                break;
        }
    }

    private void CopyLink(string? subscriptionId)
    {
        var subscription = _subscriptions.FirstOrDefault(item => item.Id == subscriptionId && !item.IsLocalFile);
        _copiedLink = subscription?.SourceLocation;
        if (!string.IsNullOrWhiteSpace(_copiedLink) && _clipboardWriter is not null)
        {
            _clipboardWriter.WriteText(_copiedLink);
            ShowToast(Localize("Subscriptions.Toast.LinkCopied"), ToastType.Success);
        }

        RaiseMenuStateChanged();
    }

    private void OpenExternalEditor(string? subscriptionId)
    {
        var subscription = _subscriptions.FirstOrDefault(item => item.Id == subscriptionId);
        if (subscription is not null)
        {
            _subscriptionFileOpener?.OpenSubscriptionFile(subscription.Id);
        }
    }

    private void ShowQrCode(string? subscriptionId)
    {
        var subscription = _subscriptions.FirstOrDefault(item => item.Id == subscriptionId && !item.IsLocalFile);
        _qrCodeCloseReset.Cancel();
        _qrCodeSubscriptionId = subscription is null ? null : subscriptionId;
        _isQrCodeDialogVisible = _qrCodeSubscriptionId is not null;
        RaiseMenuStateChanged();
    }

    private void CloseQrCodeDialog()
    {
        if (!_isQrCodeDialogVisible)
        {
            return;
        }

        _isQrCodeDialogVisible = false;
        RaiseMenuStateChanged();
        _qrCodeCloseReset.Run(() => !_isQrCodeDialogVisible, ResetQrCodeDialog);
    }

    private void ResetQrCodeDialog()
    {
        _qrCodeSubscriptionId = null;
        RaiseMenuStateChanged();
    }

    private void ShowOverrideSelector(string? subscriptionId)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return;
        }

        var subscription = _subscriptionStore?.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId);
        if (subscription is not null)
        {
            OverrideSelector.ApplySaved(subscription);
            OverrideSelector.LoadAvailable(_overrideStore?.LoadOverrides().Select(ToOverrideOption).ToList() ?? []);
        }

        OverrideSelector.Open(subscriptionId);
        RaiseMenuStateChanged();
    }

    private void OnOverrideSelectionSaveRequested(object? sender, SubscriptionOverrideSelectionSaveRequestedEventArgs args)
    {
        if (_overrideSelectionUpdater is null)
        {
            return;
        }

        OverrideSelector.ApplySaved(_overrideSelectionUpdater.SaveSelection(
            args.SubscriptionId,
            args.SelectedOverrideIds,
            args.OverrideSortPreference));
        OverrideSelector.Close();
        OverrideSelectionSaved?.Invoke(this, args.SubscriptionId);
    }

    private void ShowProviderSelector(string? subscriptionId)
    {
        Provider.Show(subscriptionId);
        RaiseMenuStateChanged();
    }

    private void ShowRuntimeConfigDialog(string? subscriptionId)
    {
        var subscription = _subscriptions.FirstOrDefault(item => item.Id == subscriptionId);
        if (subscription is null || _runtimeStore is null)
        {
            return;
        }

        RuntimeConfigDialog.Open(subscription.Id, _runtimeStore.ReadRuntimeConfig(subscription.Id));
        RaiseMenuStateChanged();
    }

    private void EditFile(string? subscriptionId)
    {
        var subscription = _subscriptions.FirstOrDefault(item => item.Id == subscriptionId);
        if (subscription is null || _subscriptionStore is null)
        {
            return;
        }

        FileEditor.Open(subscription.Id, _subscriptionStore.ReadContent(subscription.Id));
        RaiseMenuStateChanged();
    }

    private void OnFileEditorConfirmed(object? sender, SubscriptionFileEditCompletedEventArgs args)
    {
        if (_subscriptionStore is null)
        {
            return;
        }

        var subscription = _subscriptionStore.LoadSubscriptions().FirstOrDefault(item => item.Id == args.SubscriptionId);
        if (subscription is null)
        {
            return;
        }

        _subscriptionStore.SaveContent(subscription.Id, args.Content);
        _providerSnapshots.Remove(subscription.Id);
        RefreshPersistedSubscriptionRows([subscription.Id]);
        RaiseSubscriptionStateChanged();
        SubscriptionFileEdited?.Invoke(this, subscription.Id);
        _ = RefreshProviderTrafficAsync();
    }

    private void ShowChainProxyDialog(string? subscriptionId)
    {
        var subscription = _subscriptionStore?.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId);
        if (subscription is null)
        {
            return;
        }

        ChainProxy.Open(
            subscription.Id,
            subscription.DisabledBuiltinChainProxyNames,
            subscription.CustomChainProxies);
        RaiseMenuStateChanged();
    }

    private void OnChainProxySaved(object? sender, SubscriptionChainProxySaveEventArgs args)
    {
        var updated = _chainProxyUpdater?.Save(args.SubscriptionId, args.DisabledBuiltinNames, args.CustomChainProxies);
        if (updated is null)
        {
            return;
        }

        ChainProxy.CompleteSave();
        SubscriptionChainProxySaved?.Invoke(this, updated.Id);
        RaiseMenuStateChanged();
    }

    private void ShowEditDialog(string? subscriptionId)
    {
        var subscription = _subscriptions.FirstOrDefault(item => item.Id == subscriptionId);
        if (subscription is null)
        {
            return;
        }

        EditDialog.Open(subscription);
    }

    private void OnEditDialogConfirmed(object? sender, SubscriptionEditCompletedEventArgs args)
    {
        var index = FindSubscriptionIndex(args.SubscriptionId);
        if (index < 0)
        {
            return;
        }

        var updatedSubscription = _subscriptions[index].WithConfiguration(
            args.Name,
            args.Url,
            args.UserAgent,
            args.AgeSecretKey,
            args.AutoTestDelayIntervalMinutes,
            args.AutoUpdateMode,
            args.AutoUpdateIntervalMinutes,
            args.UpdateProxyMode);
        _subscriptions[index] = updatedSubscription;
        PersistEditedSubscription(updatedSubscription);
        RaiseSubscriptionStateChanged();
    }

    private void PersistEditedSubscription(SubscriptionItemViewModel subscription)
    {
        var updated = _metadataUpdater?.Save(subscription.Id, new SubscriptionMetadataEdit(
            subscription.Name,
            subscription.SourceLocation,
            subscription.UserAgent,
            subscription.AgeSecretKey,
            subscription.AutoTestDelayIntervalMinutes,
            subscription.AutoUpdateMode,
            subscription.AutoUpdateIntervalMinutes,
            subscription.UpdateProxyMode));
        if (updated is null)
        {
            return;
        }

        SubscriptionMetadataEdited?.Invoke(this, subscription.Id);
    }
}
