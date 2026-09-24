using System.Collections.ObjectModel;
using System.Windows.Input;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Updates;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed record SubscriptionProviderSyncCompletedEventArgs(string SubscriptionId, IReadOnlyList<string> SyncedProviderNames);

public sealed partial class SubscriptionPageViewModel : ViewModelBase, IDisposable
{
    private readonly LocalSubscriptionFileImporter? _localFileImporter;
    private readonly RemoteSubscriptionImporter? _remoteSubscriptionImporter;
    private readonly SubscriptionUpdater? _subscriptionUpdater;
    private readonly ISubscriptionStore? _subscriptionStore;
    private readonly IOverrideStore? _overrideStore;
    private readonly SubscriptionOverrideSelectionUpdater? _overrideSelectionUpdater;
    private readonly SubscriptionFailureRecorder? _failureRecorder;
    private readonly SubscriptionChainProxyUpdater? _chainProxyUpdater;
    private readonly SubscriptionMetadataUpdater? _metadataUpdater;
    private readonly SubscriptionReorderer? _reorderer;
    private readonly IClipboardWriter? _clipboardWriter;
    private readonly ISubscriptionFileOpener? _subscriptionFileOpener;
    private readonly ILocalizationService? _localization;
    private readonly QrCodeGenerator _qrCodeGenerator = new();
    private readonly DialogCloseResetScheduler _qrCodeCloseReset = new();
    private readonly SubscriptionDeleter _subscriptionDeleter;
    private readonly ISubscriptionSelectionStore? _subscriptionSelectionStore;
    private readonly ISelectedSubscriptionRuntimeStore? _runtimeStore;
    private readonly SelectedSubscriptionProviderCatalogLoader? _providerCatalogLoader;
    private readonly ObservableCollection<SubscriptionItemViewModel> _subscriptions = [];
    private readonly ReadOnlyObservableCollection<SubscriptionItemViewModel> _subscriptionView;
    private readonly UpdateOperationState _updateState = new();
    private string? _currentSubscriptionId;
    private SubscriptionRowMenuSelection? _selectedRowMenuAction;
    private string? _copiedLink;
    private string? _qrCodeSubscriptionId;
    private bool _isQrCodeDialogVisible;
    private string? _deleteDialogSubscriptionId;

    public SubscriptionPageViewModel(
        SubscriptionDeleter subscriptionDeleter,
        LocalSubscriptionFileImporter? localFileImporter = null,
        RemoteSubscriptionImporter? remoteSubscriptionImporter = null,
        SubscriptionUpdater? subscriptionUpdater = null,
        ISubscriptionStore? subscriptionStore = null,
        IOverrideStore? overrideStore = null,
        SubscriptionOverrideSelectionUpdater? overrideSelectionUpdater = null,
        IClipboardWriter? clipboardWriter = null,
        ISubscriptionFileOpener? subscriptionFileOpener = null,
        ISubscriptionProviderUploader? providerUploader = null,
        SelectedSubscriptionProviderCatalogLoader? providerCatalogLoader = null,
        ISubscriptionSelectionStore? subscriptionSelectionStore = null,
        ISelectedSubscriptionRuntimeStore? runtimeStore = null,
        ILocalizationService? localization = null,
        Func<string, SubscriptionChainProxyContext>? chainProxyContextLoader = null)
    {
        _localFileImporter = localFileImporter;
        _remoteSubscriptionImporter = remoteSubscriptionImporter;
        _subscriptionUpdater = subscriptionUpdater;
        _subscriptionStore = subscriptionStore;
        _failureRecorder = subscriptionStore is null ? null : new SubscriptionFailureRecorder(subscriptionStore);
        _chainProxyUpdater = subscriptionStore is null ? null : new SubscriptionChainProxyUpdater(subscriptionStore);
        _metadataUpdater = subscriptionStore is null ? null : new SubscriptionMetadataUpdater(subscriptionStore);
        _reorderer = subscriptionStore is null ? null : new SubscriptionReorderer(subscriptionStore);
        _overrideStore = overrideStore;
        _overrideSelectionUpdater = overrideSelectionUpdater;
        _clipboardWriter = clipboardWriter;
        _subscriptionFileOpener = subscriptionFileOpener;
        _localization = localization;
        _subscriptionDeleter = subscriptionDeleter;
        _subscriptionSelectionStore = subscriptionSelectionStore;
        _runtimeStore = runtimeStore;
        _providerCatalogLoader = providerCatalogLoader;
        Provider = new SubscriptionProviderViewModel(providerCatalogLoader, providerUploader, localization);
        Provider.ProvidersSynced += (sender, args) => ProvidersSynced?.Invoke(sender, args);
        Provider.SnapshotChanged += (_, snapshot) => ApplyProviderSnapshot(snapshot);
        Provider.DialogStateChanged += (_, _) => NotifyDialogOverlayChanged();
        Provider.ToastRequested += (_, toast) => ShowToast(toast.Message, toast.Type);
        ChainProxy = new SubscriptionChainProxyDialogViewModel(localization, chainProxyContextLoader);
        ChainProxy.Saved += OnChainProxySaved;
        ChainProxy.DialogStateChanged += (_, _) => NotifyDialogOverlayChanged();
        OverrideSelector = new SubscriptionOverrideSelectorViewModel();
        OverrideSelector.SaveRequested += OnOverrideSelectionSaveRequested;
        OverrideSelector.DialogStateChanged += (_, _) => NotifyDialogOverlayChanged();
        RuntimeConfigDialog = new SubscriptionRuntimeConfigDialogViewModel();
        RuntimeConfigDialog.DialogStateChanged += (_, _) => NotifyDialogOverlayChanged();
        FileEditor = new SubscriptionFileEditorViewModel();
        FileEditor.Confirmed += OnFileEditorConfirmed;
        FileEditor.DialogStateChanged += (_, _) => NotifyDialogOverlayChanged();
        EditDialog = new SubscriptionEditDialogViewModel(localization);
        EditDialog.Confirmed += OnEditDialogConfirmed;
        EditDialog.DialogStateChanged += (_, _) => NotifyDialogOverlayChanged();
        AddDialog = new SubscriptionAddDialogViewModel(localization);
        AddDialog.RemoteRequested += OnAddRemoteRequested;
        AddDialog.LocalRequested += OnAddLocalRequested;
        AddDialog.DialogStateChanged += OnAddDialogStateChanged;

        SubscriptionStore = subscriptionStore;
        SelectionStore = subscriptionSelectionStore;
        _subscriptionView = new ReadOnlyObservableCollection<SubscriptionItemViewModel>(_subscriptions);
        SelectSubscriptionCommand = new RelayCommand<string>(SelectSubscription);
        UpdateSubscriptionCommand = new RelayCommand<string>(subscriptionId => _ = UpdateSubscriptionAsync(subscriptionId));
        UpdateAllSubscriptionsCommand = new RelayCommand(() => _ = UpdateAllSubscriptionsAsync());
        CopyLinkCommand = new RelayCommand<string>(CopyLink);
        OpenExternalEditorCommand = new RelayCommand<string>(OpenExternalEditor);
        ShowQrCodeCommand = new RelayCommand<string>(ShowQrCode);
        CloseQrCodeDialogCommand = new RelayCommand(CloseQrCodeDialog);
        ShowOverrideSelectorCommand = new RelayCommand<string>(ShowOverrideSelector);
        ShowRuntimeConfigDialogCommand = new RelayCommand<string>(ShowRuntimeConfigDialog);
        EditFileCommand = new RelayCommand<string>(EditFile);
        ShowChainProxyDialogCommand = new RelayCommand<string>(ShowChainProxyDialog);
        ShowEditDialogCommand = new RelayCommand<string>(ShowEditDialog);
        ShowDeleteDialogCommand = new RelayCommand<string>(ShowDeleteDialog);
        ConfirmDeleteCommand = new RelayCommand(ConfirmDelete);
        CancelDeleteDialogCommand = new RelayCommand(CancelDeleteDialog);
        MoveSubscriptionCommand = new RelayCommand<SubscriptionMoveRequest>(MoveSubscription);
        MoveSubscriptionUpCommand = new RelayCommand<string>(MoveSubscriptionUp);
        MoveSubscriptionDownCommand = new RelayCommand<string>(MoveSubscriptionDown);
        RowMenuActionCommand = new RelayCommand<SubscriptionRowMenuSelection>(RunRowMenuAction);
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLanguageChanged;
        }
    }

    public event EventHandler<string?>? SubscriptionSelected;

    public event EventHandler<(string Message, ToastType Type)>? ToastRequested;

    public event EventHandler<SubscriptionUpdateResult>? SubscriptionsUpdated;

    public event EventHandler<IReadOnlyList<string>>? SubscriptionUpdateStarting;

    public event EventHandler<string>? OverrideSelectionSaved;

    public event EventHandler<SubscriptionProviderSyncCompletedEventArgs>? ProvidersSynced;

    public event EventHandler<string>? SubscriptionFileEdited;

    public event EventHandler<string>? SubscriptionMetadataEdited;

    public event EventHandler<string>? SubscriptionChainProxySaved;

    public IReadOnlyList<SubscriptionItemViewModel> Subscriptions => _subscriptionView;

    internal ISubscriptionStore? SubscriptionStore { get; }

    internal ISubscriptionSelectionStore? SelectionStore { get; }

    public string? CurrentSubscriptionId => _currentSubscriptionId;

    public int CurrentSubscriptionAutoTestDelayIntervalMinutes => _subscriptions.FirstOrDefault(item => item.Id == _currentSubscriptionId)?.AutoTestDelayIntervalMinutes ?? 0;

    public int TotalSubscriptionCount => _subscriptions.Count;

    public int RemoteSubscriptionCount => _subscriptions.Count(item => !item.IsLocalFile);

    public int LocalSubscriptionCount => _subscriptions.Count(item => item.IsLocalFile);

    public int AutoUpdateSubscriptionCount => _subscriptions.Count(item => !item.IsLocalFile && item.AutoUpdateMode != SubscriptionAutoUpdateMode.Disabled);

    public int AutoDelaySubscriptionCount => _subscriptions.Count(item => item.IsAutoTestDelayEnabled);

    public string CurrentSubscriptionName => _subscriptions.FirstOrDefault(item => item.Id == _currentSubscriptionId)?.Name ?? Localize("Home.Subscription.Empty");

    public SubscriptionItemViewModel? CurrentSubscription => _subscriptions.FirstOrDefault(item => item.Id == _currentSubscriptionId);

    public bool HasCurrentSubscription => CurrentSubscription is not null;

    public PageLoadingState Loading { get; } = new();
    public bool IsEmptyVisible => _subscriptions.Count == 0;

    public bool IsEmptyTextVisible => IsEmptyVisible;

    public bool IsListVisible => !IsEmptyVisible;

    public string EmptyText => Localize("Subscriptions.Empty.NoSubscriptions");

    public IReadOnlyList<string> UpdatedSubscriptionIds => _updateState.UpdatedItemIds;

    public bool HasUpdatedAllSubscriptions => _updateState.HasUpdatedAllItems;

    public bool IsBatchUpdatingSubscriptions => _updateState.IsBatchUpdating;

    public IReadOnlyList<string> UpdatingSubscriptionIds => _updateState.UpdatingItemIds;

    public bool CanUpdateAllSubscriptions => !IsBatchUpdatingSubscriptions && GetPendingSubscriptionUpdateIds().Count > 0;

    public bool IsUpdateAllIconVisible => !IsBatchUpdatingSubscriptions;

    public IReadOnlyList<string> SkippedSubscriptionUpdateIds => _updateState.SkippedItemIds;

    public string? CopiedLink => _copiedLink;

    public string? QrCodeSubscriptionId => _qrCodeSubscriptionId;

    public string QrCodeContent => _subscriptions.FirstOrDefault(item => item.Id == _qrCodeSubscriptionId)?.SourceLocation ?? string.Empty;

    public QrCodeMatrix? QrCodeMatrix => string.IsNullOrWhiteSpace(QrCodeContent) ? null : _qrCodeGenerator.Generate(QrCodeContent);

    public SubscriptionOverrideSelectorViewModel OverrideSelector { get; }

    public SubscriptionRuntimeConfigDialogViewModel RuntimeConfigDialog { get; }

    public SubscriptionFileEditorViewModel FileEditor { get; }

    public SubscriptionEditDialogViewModel EditDialog { get; }

    public SubscriptionChainProxyDialogViewModel ChainProxy { get; }

    public string? DeleteDialogSubscriptionId => _deleteDialogSubscriptionId;

    public bool IsDeleteDialogVisible => _deleteDialogSubscriptionId is not null;

    public bool IsQrCodeDialogVisible => _isQrCodeDialogVisible;

    public bool IsDialogOverlayVisible => IsPageDialogOverlayVisible || Provider.IsProviderSelectorDialogVisible;

    public bool IsPageDialogOverlayVisible => AddDialog.IsDialogVisible
        || EditDialog.IsDialogVisible
        || IsDeleteDialogVisible
        || OverrideSelector.IsDialogVisible
        || RuntimeConfigDialog.IsDialogVisible
        || FileEditor.IsDialogVisible
        || ChainProxy.IsDialogVisible
        || IsQrCodeDialogVisible;

    public SubscriptionProviderViewModel Provider { get; }

    public SubscriptionAddDialogViewModel AddDialog { get; }

    public ICommand SelectSubscriptionCommand { get; }
    public ICommand UpdateSubscriptionCommand { get; }
    public ICommand UpdateAllSubscriptionsCommand { get; }
    public ICommand CopyLinkCommand { get; }
    public ICommand OpenExternalEditorCommand { get; }
    public ICommand ShowQrCodeCommand { get; }
    public ICommand CloseQrCodeDialogCommand { get; }
    public ICommand ShowOverrideSelectorCommand { get; }
    public ICommand ShowRuntimeConfigDialogCommand { get; }
    public ICommand EditFileCommand { get; }
    public ICommand ShowChainProxyDialogCommand { get; }
    public ICommand ShowEditDialogCommand { get; }
    public ICommand ShowDeleteDialogCommand { get; }
    public ICommand ConfirmDeleteCommand { get; }
    public ICommand CancelDeleteDialogCommand { get; }
    public ICommand MoveSubscriptionCommand { get; }
    public ICommand MoveSubscriptionUpCommand { get; }
    public ICommand MoveSubscriptionDownCommand { get; }
    public ICommand RowMenuActionCommand { get; }

    public void AddSubscription(SubscriptionItemViewModel subscription)
    {
        var hadNoSelection = string.IsNullOrWhiteSpace(_currentSubscriptionId);
        if (hadNoSelection)
        {
            _currentSubscriptionId = subscription.Id;
            _subscriptionSelectionStore?.SetCurrentSubscriptionId(subscription.Id);
        }

        subscription.SetCurrent(subscription.Id == _currentSubscriptionId);
        _subscriptions.Add(subscription);
        RaiseSubscriptionStateChanged();

        if (hadNoSelection)
        {
            SubscriptionSelected?.Invoke(this, subscription.Id);
        }
    }

    public void LoadSubscriptions(IReadOnlyList<Subscription> subscriptions)
    {
        _providerRefreshVersion++;
        foreach (var id in _providerSnapshots.Keys.Except(subscriptions.Select(item => item.Id)).ToList())
        {
            _providerSnapshots.Remove(id);
        }
        var currentSubscriptionId = _currentSubscriptionId ?? _subscriptionSelectionStore?.GetCurrentSubscriptionId();
        _subscriptions.Clear();
        foreach (var subscription in subscriptions)
        {
            _subscriptions.Add(ToSubscriptionItem(subscription, subscription.Id == currentSubscriptionId));
        }

        var hasCurrentSubscription = _subscriptions.Any(item => item.Id == currentSubscriptionId);
        _currentSubscriptionId = hasCurrentSubscription ? currentSubscriptionId : null;
        if (!hasCurrentSubscription && !string.IsNullOrWhiteSpace(currentSubscriptionId))
        {
            _subscriptionSelectionStore?.SetCurrentSubscriptionId(null);
        }

        SyncCurrentSubscriptionRows();
        RaiseSubscriptionStateChanged();
        Loading.CompleteInitialLoad();
    }

    public bool CurrentSubscriptionUsesAnyOverride(IReadOnlyCollection<string> overrideIds)
    {
        if (string.IsNullOrWhiteSpace(_currentSubscriptionId) || overrideIds.Count == 0)
        {
            return false;
        }

        var subscription = _subscriptionStore?.LoadSubscriptions().FirstOrDefault(item => item.Id == _currentSubscriptionId);
        return subscription?.OverrideIds.Any(overrideId => overrideIds.Contains(overrideId, StringComparer.Ordinal)) == true;
    }

    public bool DisableOverridesForSubscription(string subscriptionId)
    {
        if (_overrideSelectionUpdater is null || _subscriptionStore is null)
        {
            return false;
        }

        if (_overrideSelectionUpdater.DisableOverridesForSubscription(subscriptionId) is null)
        {
            return false;
        }

        RefreshOverrideSelectionFromStore(subscriptionId);
        return true;
    }

    // 覆写启用状态由用例持久化；这里只同步选择器 UI 状态。
    public void RefreshOverrideSelectionFromStore(string subscriptionId)
    {
        if (_subscriptionStore is null)
        {
            return;
        }

        var subscriptions = _subscriptionStore.LoadSubscriptions();
        var subscription = subscriptions.FirstOrDefault(item => item.Id == subscriptionId);
        if (subscription is not null && string.Equals(_currentSubscriptionId, subscriptionId, StringComparison.Ordinal))
        {
            OverrideSelector.ApplySaved(subscription);
        }

        LoadSubscriptions(subscriptions);
    }

    // 启动列表在后台加载并回到 UI 线程提交；失败只记日志。
    public async Task InitializeAsync()
    {
        using var loading = Loading.BeginLoading();
        if (_subscriptionStore is null)
        {
            return;
        }

        try
        {
            var subscriptions = await Task.Run(() => _subscriptionStore.LoadSubscriptions());
            LoadSubscriptions(subscriptions);
            await RefreshProviderTrafficAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Startup list load failed: {exception.Message}");
        }
    }

    public IReadOnlyList<string> SetOverridesForSubscription(string subscriptionId, IReadOnlyList<string> overrideIds)
    {
        if (_overrideSelectionUpdater is null || _subscriptionStore is null)
        {
            throw new InvalidOperationException("Subscription override selection is unavailable");
        }

        var updated = _overrideSelectionUpdater.SaveValidatedSelection(subscriptionId, overrideIds);
        if (string.Equals(_currentSubscriptionId, subscriptionId, StringComparison.Ordinal))
        {
            OverrideSelector.ApplySaved(updated);
        }

        LoadSubscriptions(_subscriptionStore.LoadSubscriptions());
        OverrideSelectionSaved?.Invoke(this, subscriptionId);
        return updated.OverrideIds;
    }

    public bool MarkSubscriptionRuntimeFailed(string subscriptionId, string message)
    {
        return RecordSubscriptionFailure(subscriptionId, recorder => recorder.MarkFailed(subscriptionId, message));
    }

    public bool ClearSubscriptionRuntimeFailure(string subscriptionId)
    {
        return RecordSubscriptionFailure(subscriptionId, recorder => recorder.ClearFailure(subscriptionId));
    }

    // 失败状态由用例持久化；这里仅在需要时刷新行。
    private bool RecordSubscriptionFailure(string subscriptionId, Func<SubscriptionFailureRecorder, bool> record)
    {
        if (_failureRecorder is null || !record(_failureRecorder))
        {
            return false;
        }

        RefreshPersistedSubscriptionRows([subscriptionId]);
        RaiseSubscriptionStateChanged();
        return true;
    }

    private void ShowToast(string message, ToastType type = ToastType.Error)
    {
        ToastRequested?.Invoke(this, (message, type));
    }

    private void ShowSuccessToast(string localizationKey, string value)
    {
        ShowToast(string.Format(Localize(localizationKey), value), ToastType.Success);
    }

    private void ShowErrorToast(string localizationKey)
    {
        ShowToast(Localize(localizationKey));
    }

    private void NotifyDialogOverlayChanged()
    {
        OnPropertyChanged(nameof(IsDialogOverlayVisible));
        OnPropertyChanged(nameof(IsPageDialogOverlayVisible));
    }

    private void OnAddDialogStateChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(IsEmptyTextVisible));
        OnPropertyChanged(nameof(IsListVisible));
        NotifyDialogOverlayChanged();
    }

    public void ClearCurrentSubscription()
    {
        if (string.IsNullOrWhiteSpace(_currentSubscriptionId))
        {
            _subscriptionSelectionStore?.SetCurrentSubscriptionId(null);
            return;
        }

        var previousId = _currentSubscriptionId;
        _currentSubscriptionId = null;
        _subscriptionSelectionStore?.SetCurrentSubscriptionId(null);
        _subscriptions.FirstOrDefault(item => item.Id == previousId)?.SetCurrent(false);
        RaiseSubscriptionStateChanged();
    }

    private void SelectSubscription(string? subscriptionId)
    {
        var selectedId = subscriptionId ?? string.Empty;
        if (_subscriptions.All(item => item.Id != selectedId))
        {
            return;
        }

        // 重新选择当前订阅只关闭面板，避免重复应用运行时配置。
        if (string.Equals(_currentSubscriptionId, selectedId, StringComparison.Ordinal))
        {
            Provider.Close();
            return;
        }

        SetCurrentSubscriptionId(selectedId);
        _subscriptionSelectionStore?.SetCurrentSubscriptionId(selectedId);
        Provider.Close();
        SubscriptionSelected?.Invoke(this, selectedId);
    }

    private void SetCurrentSubscriptionId(string selectedId)
    {
        var previousId = _currentSubscriptionId;
        if (previousId == selectedId)
        {
            return;
        }

        _currentSubscriptionId = selectedId;
        _subscriptions.FirstOrDefault(item => item.Id == previousId)?.SetCurrent(false);
        _subscriptions.FirstOrDefault(item => item.Id == selectedId)?.SetCurrent(true);
        OnPropertyChanged(nameof(CurrentSubscriptionId));
        OnPropertyChanged(nameof(CurrentSubscriptionAutoTestDelayIntervalMinutes));
        OnPropertyChanged(nameof(CurrentSubscriptionName));
        OnPropertyChanged(nameof(CurrentSubscription));
        OnPropertyChanged(nameof(HasCurrentSubscription));
        NotifyHomeCardPresentationChanged();
    }

    private int FindSubscriptionIndex(string? subscriptionId)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return -1;
        }

        for (var index = 0; index < _subscriptions.Count; index++)
        {
            if (string.Equals(_subscriptions[index].Id, subscriptionId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private void SyncCurrentSubscriptionRows()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.SetCurrent(subscription.Id == _currentSubscriptionId);
        }
    }

    private void SyncUpdatingSubscriptionRows()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.SetUpdating(_updateState.UpdatingItemIds.Contains(subscription.Id, StringComparer.Ordinal));
        }
    }

    private void RaiseSubscriptionStateChanged()
    {
        SyncUpdatingSubscriptionRows();
        OnPropertyChanged(nameof(Subscriptions));
        OnPropertyChanged(nameof(CurrentSubscriptionId));
        OnPropertyChanged(nameof(CurrentSubscriptionAutoTestDelayIntervalMinutes));
        OnPropertyChanged(nameof(TotalSubscriptionCount));
        OnPropertyChanged(nameof(RemoteSubscriptionCount));
        OnPropertyChanged(nameof(LocalSubscriptionCount));
        OnPropertyChanged(nameof(AutoUpdateSubscriptionCount));
        OnPropertyChanged(nameof(AutoDelaySubscriptionCount));
        OnPropertyChanged(nameof(CurrentSubscriptionName));
        OnPropertyChanged(nameof(CurrentSubscription));
        OnPropertyChanged(nameof(HasCurrentSubscription));
        NotifyHomeCardPresentationChanged();
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(IsEmptyTextVisible));
        OnPropertyChanged(nameof(IsListVisible));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(UpdatedSubscriptionIds));
        OnPropertyChanged(nameof(UpdatingSubscriptionIds));
        OnPropertyChanged(nameof(CanUpdateAllSubscriptions));
        OnPropertyChanged(nameof(IsUpdateAllIconVisible));
        OnPropertyChanged(nameof(SkippedSubscriptionUpdateIds));
        OnPropertyChanged(nameof(HasUpdatedAllSubscriptions));
        OnPropertyChanged(nameof(IsBatchUpdatingSubscriptions));
    }

    private void RaiseMenuStateChanged()
    {
        OnPropertyChanged(nameof(CopiedLink));
        OnPropertyChanged(nameof(QrCodeSubscriptionId));
        OnPropertyChanged(nameof(QrCodeContent));
        OnPropertyChanged(nameof(QrCodeMatrix));
        OnPropertyChanged(nameof(DeleteDialogSubscriptionId));
        OnPropertyChanged(nameof(IsDeleteDialogVisible));
        OnPropertyChanged(nameof(IsQrCodeDialogVisible));
        NotifyDialogOverlayChanged();
    }

    public void Dispose()
    {
        _providerLifetime.Cancel();
        _providerLifetime.Dispose();
        _qrCodeCloseReset.Cancel();
        if (_localization is not null)
        {
            _localization.LanguageChanged -= OnLanguageChanged;
        }

        // 只释放持有外部订阅或取消源的子 VM。
        Provider.Dispose();
        ChainProxy.Dispose();
        AddDialog.Dispose();
        EditDialog.Dispose();
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.RefreshLanguage();
        }

        RaiseSubscriptionStateChanged();
        RaiseMenuStateChanged();
    }

    private string Localize(string key) => _localization?.GetString(key) ?? key;
}
