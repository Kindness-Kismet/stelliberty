using System.ComponentModel;
using System.Windows.Input;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed class SubscriptionProviderViewModel : ViewModelBase, IDisposable
{
    private readonly SelectedSubscriptionProviderCatalogLoader? _catalogLoader;
    private readonly ISubscriptionProviderUploader? _uploader;
    private readonly ILocalizationService? _localization;
    private readonly DialogCloseResetScheduler _closeReset = new();
    private readonly List<SubscriptionProviderItemViewModel> _providers = [];
    private readonly List<string> _syncedProviderNames = [];
    private readonly List<string> _uploadedProviderNames = [];
    private readonly List<string> _syncingProviderNames = [];
    private SubscriptionProviderCatalog? _catalog;
    private Task? _runtimeRefreshTask;
    private string? _selectorSubscriptionId;
    // 会话版本拒绝订阅切换或清空后的过期异步结果。
    private int _selectorSession;
    private CancellationTokenSource? _syncCts;
    private bool _isProviderSelectorDialogVisible;
    private string _searchKeyword = string.Empty;
    private bool _isSyncingAll;
    private bool _hasSyncedAllHttpProviders;
    private bool _hasRefreshedAfterSync;
    private bool _hasRefreshedAfterUpload;

    public SubscriptionProviderViewModel(
        SelectedSubscriptionProviderCatalogLoader? catalogLoader,
        ISubscriptionProviderUploader? uploader,
        ILocalizationService? localization = null)
    {
        _catalogLoader = catalogLoader;
        _uploader = uploader;
        _localization = localization;
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLanguageChanged;
        }
        ShowProviderSelectorCommand = new RelayCommand<string>(ShowSelector);
        CloseProviderSelectorCommand = new RelayCommand(CloseSelector);
        SyncProviderCommand = new RelayCommand<string>(providerName => _ = SyncProviderAsync(providerName));
        SyncAllProvidersCommand = new RelayCommand(() => _ = SyncAllProvidersAsync());
        UploadProviderCommand = new RelayCommand<SubscriptionProviderUploadRequest>(request => _ = UploadProviderAsync(request));
    }

    public event EventHandler<SubscriptionProviderSyncCompletedEventArgs>? ProvidersSynced;

    public event EventHandler? DialogStateChanged;

    public event EventHandler<(string Message, ToastType Type)>? ToastRequested;

    public string? ProviderSelectorSubscriptionId => _selectorSubscriptionId;

    public bool IsProviderSelectorDialogVisible => _isProviderSelectorDialogVisible;

    public IReadOnlyList<SubscriptionProviderItemViewModel> Providers => _providers.ToList();

    public IReadOnlyList<SubscriptionProviderItemViewModel> FilteredProviders
    {
        get
        {
            var keyword = _searchKeyword.Trim();
            return string.IsNullOrWhiteSpace(keyword)
                ? Providers
                : Providers.Where(item => Contains(item.Name, keyword) || Contains(item.DisplayName, keyword)).ToList();
        }
    }

    public IReadOnlyList<SubscriptionProviderItemViewModel> FilteredProxyProviders => FilteredProviders
        .Where(item => !string.Equals(item.Type, "rule", StringComparison.OrdinalIgnoreCase))
        .ToList();

    public IReadOnlyList<SubscriptionProviderItemViewModel> FilteredRuleProviders => FilteredProviders
        .Where(item => string.Equals(item.Type, "rule", StringComparison.OrdinalIgnoreCase))
        .ToList();

    public bool HasProxyProviders => FilteredProxyProviders.Count > 0;

    public bool HasRuleProviders => FilteredRuleProviders.Count > 0;

    public IReadOnlyList<string> SyncedProviderNames => _syncedProviderNames;

    public IReadOnlyList<string> UploadedProviderNames => _uploadedProviderNames;

    public bool HasSyncedAllHttpProviders => _hasSyncedAllHttpProviders;

    public bool IsSyncingAll => _isSyncingAll;

    // 全部同步会冻结对话框；单个同步只冻结该行和全部同步入口。
    public bool IsDialogInteractionEnabled => !_isSyncingAll;

    public bool CanSyncAll => !_isSyncingAll
        && _syncingProviderNames.Count == 0
        && _providers.Any(item => item.CanSync);

    public bool HasRefreshedProvidersAfterSync => _hasRefreshedAfterSync;

    public bool HasRefreshedProvidersAfterUpload => _hasRefreshedAfterUpload;

    public string ProviderSearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value))
            {
                OnPropertyChanged(nameof(FilteredProviders));
                OnPropertyChanged(nameof(FilteredProxyProviders));
                OnPropertyChanged(nameof(FilteredRuleProviders));
                OnPropertyChanged(nameof(HasProxyProviders));
                OnPropertyChanged(nameof(HasRuleProviders));
                OnPropertyChanged(nameof(CanSyncAll));
            }
        }
    }

    public ICommand ShowProviderSelectorCommand { get; }

    public ICommand CloseProviderSelectorCommand { get; }

    public ICommand SyncProviderCommand { get; }

    public ICommand SyncAllProvidersCommand { get; }

    public ICommand UploadProviderCommand { get; }

    public void Dispose()
    {
        _selectorSession++;
        _isProviderSelectorDialogVisible = false;
        _syncCts?.Cancel();
        _syncCts?.Dispose();
        _syncCts = null;
        if (_localization is not null)
        {
            _localization.LanguageChanged -= OnLanguageChanged;
        }
    }

    public void LoadProviders(IReadOnlyList<SubscriptionProviderItemViewModel> providers)
    {
        _providers.Clear();
        _providers.AddRange(providers);
        RaiseProviderStateChanged();
    }

    public void LoadSelectedProviderCatalog(SubscriptionProviderCatalog catalog)
    {
        _catalog = catalog;
        LoadProviders(catalog.VisibleProviders.Select(ToProviderItem).ToList());
    }

    public Task RefreshSelectedProviderCatalogAsync()
    {
        if (_catalogLoader is null || string.IsNullOrWhiteSpace(_selectorSubscriptionId))
        {
            return Task.CompletedTask;
        }

        return RefreshCatalogWithRuntimeAsync(_selectorSubscriptionId);
    }

    public Task UploadProviderAsync(string providerName, string sourcePath)
    {
        return UploadProviderAsync(new SubscriptionProviderUploadRequest(providerName, sourcePath));
    }

    public void Show(string? subscriptionId)
    {
        ShowSelector(subscriptionId);
    }

    // Debug 命令等待运行时状态合并后再读取断言。
    public async Task ShowAsync(string? subscriptionId)
    {
        ShowSelector(subscriptionId);
        if (_runtimeRefreshTask is { } task)
        {
            await task;
        }
    }

    public void Close()
    {
        BeginClose();
    }

    public void ClearForSubscription(string subscriptionId)
    {
        if (_selectorSubscriptionId == subscriptionId)
        {
            BeginClose();
        }
    }

    private void ShowSelector(string? subscriptionId)
    {
        _closeReset.Cancel();
        // 新订阅开启干净会话；重开同一个订阅保留草稿状态。
        if (!string.Equals(_selectorSubscriptionId, subscriptionId, StringComparison.Ordinal))
        {
            ResetSessionState();
        }

        _selectorSubscriptionId = subscriptionId;
        _isProviderSelectorDialogVisible = subscriptionId is not null;
        if (_catalogLoader is not null && !string.IsNullOrWhiteSpace(subscriptionId))
        {
            // 静态解析会立即显示行；运行时数量和时间戳稍后到达。
            LoadSelectedProviderCatalog(_catalogLoader.LoadCatalog(subscriptionId));
            _runtimeRefreshTask = RefreshCatalogWithRuntimeAsync(subscriptionId);
        }

        OnPropertyChanged(nameof(ProviderSelectorSubscriptionId));
        OnPropertyChanged(nameof(IsProviderSelectorDialogVisible));
        DialogStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RefreshCatalogWithRuntimeAsync(string subscriptionId)
    {
        if (_catalogLoader is null)
        {
            return;
        }

        var session = _selectorSession;
        try
        {
            var catalog = await _catalogLoader.LoadCatalogAsync(subscriptionId, EnsureSyncToken());
            // 对话框关闭或订阅切换会让待处理结果失效。
            if (session != _selectorSession || !_isProviderSelectorDialogVisible || _selectorSubscriptionId != subscriptionId)
            {
                return;
            }

            ApplyCatalog(catalog);
        }
        catch (OperationCanceledException)
        {
            // 关闭对话框会取消进行中的读取。
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Provider runtime state read failed: {exception.Message}");
        }
    }

    private void ApplyCatalog(SubscriptionProviderCatalog catalog)
    {
        _catalog = catalog;
        _providers.Clear();
        _providers.AddRange(catalog.VisibleProviders.Select(ToProviderItem));
        catalog.MarkSynced(_syncedProviderNames);
        foreach (var providerName in _syncedProviderNames.ToList())
        {
            MarkProviderSynced(providerName);
        }

        foreach (var providerName in _uploadedProviderNames.ToList())
        {
            MarkProviderUploaded(providerName);
        }

        // 目录刷新会重建行，所以要回放进行中同步的转圈状态。
        foreach (var providerName in _syncingProviderNames)
        {
            var index = _providers.FindIndex(item => item.Name == providerName);
            if (index >= 0)
            {
                _providers[index] = _providers[index] with { IsSyncing = true };
            }
        }

        if (_isSyncingAll)
        {
            for (var index = 0; index < _providers.Count; index++)
            {
                if (_providers[index].CanSync && !_providers[index].IsSynced)
                {
                    _providers[index] = _providers[index] with { IsSyncing = true };
                }
            }
        }

        RaiseProviderStateChanged();
    }

    private void CloseSelector()
    {
        BeginClose();
    }

    private void ClearSelectorState()
    {
        ResetSessionState();
        _isProviderSelectorDialogVisible = false;
        _selectorSubscriptionId = null;
        _catalog = null;
        _runtimeRefreshTask = null;
        _providers.Clear();
        RaiseProviderStateChanged();
        OnPropertyChanged(nameof(ProviderSelectorSubscriptionId));
        OnPropertyChanged(nameof(IsProviderSelectorDialogVisible));
        DialogStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ResetSessionState()
    {
        _selectorSession++;
        _syncCts?.Cancel();
        _syncedProviderNames.Clear();
        _uploadedProviderNames.Clear();
        _syncingProviderNames.Clear();
        _searchKeyword = string.Empty;
        _isSyncingAll = false;
        _hasSyncedAllHttpProviders = false;
        _hasRefreshedAfterSync = false;
        _hasRefreshedAfterUpload = false;
    }

    // 取消源属于会话，关闭或切换后重建。
    private CancellationToken EnsureSyncToken()
    {
        if (_syncCts is null || _syncCts.IsCancellationRequested)
        {
            _syncCts?.Dispose();
            _syncCts = new CancellationTokenSource();
        }

        return _syncCts.Token;
    }

    private void BeginClose()
    {
        if (!_isProviderSelectorDialogVisible)
        {
            return;
        }

        // 关闭会取消进行中的同步和运行时读取。
        _syncCts?.Cancel();
        _isProviderSelectorDialogVisible = false;
        OnPropertyChanged(nameof(IsProviderSelectorDialogVisible));
        DialogStateChanged?.Invoke(this, EventArgs.Empty);
        _closeReset.Run(() => !_isProviderSelectorDialogVisible, ClearSelectorState);
    }

    public async Task SyncProviderAsync(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName) || _isSyncingAll || _syncingProviderNames.Contains(providerName))
        {
            return;
        }

        if (_catalog is not null)
        {
            var session = _selectorSession;
            var token = EnsureSyncToken();
            SetProviderSyncing(providerName, true);
            var minDisplayTask = Task.Delay(600);
            try
            {
                await ApplyProviderSyncResultAsync(await _catalog.SyncProviderAsync(providerName, token), session);
            }
            catch (OperationCanceledException)
            {
                // 关闭对话框会取消进行中的同步。
            }
            catch (Exception exception)
            {
                AppLogger.Error(exception, $"Provider sync failed: {providerName}");
                if (session == _selectorSession)
                {
                    ShowErrorToast(Localize("Subscriptions.Toast.ProviderSyncFailed"));
                }
            }
            finally
            {
                await minDisplayTask;
                if (session == _selectorSession)
                {
                    SetProviderSyncing(providerName, false);
                }
            }
            return;
        }

        var index = _providers.FindIndex(item => item.Name == providerName);
        if (index < 0 || !_providers[index].CanSync)
        {
            return;
        }

        MarkProviderSynced(providerName);
        _hasRefreshedAfterSync = true;
        RaiseProviderStateChanged();
    }

    public async Task SyncAllProvidersAsync()
    {
        if (_catalog is not null)
        {
            if (!CanSyncAll)
            {
                return;
            }

            var session = _selectorSession;
            var token = EnsureSyncToken();
            SetSyncingAll(true);
            var minDisplayTask = Task.Delay(600);
            try
            {
                var result = await _catalog.SyncAllAsync(token);
                await ApplyProviderSyncResultAsync(result, session);
                if (session == _selectorSession)
                {
                    _hasSyncedAllHttpProviders = true;
                    if (result.FailedProviderNames.Count > 0)
                    {
                        ShowErrorToast(string.Format(
                            Localize("Subscriptions.Toast.ProviderSyncPartialFailed"),
                            result.FailedProviderNames.Count,
                            string.Join(", ", result.FailedProviderNames)));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 关闭对话框会取消进行中的同步。
            }
            catch (Exception exception)
            {
                AppLogger.Error(exception, "Syncing all providers failed");
                if (session == _selectorSession)
                {
                    ShowErrorToast(Localize("Subscriptions.Toast.ProviderSyncFailed"));
                }
            }
            finally
            {
                await minDisplayTask;
                if (session == _selectorSession)
                {
                    SetSyncingAll(false);
                }
            }
            return;
        }

        _hasSyncedAllHttpProviders = true;
        _hasRefreshedAfterSync = true;
        for (var index = 0; index < _providers.Count; index++)
        {
            if (!_providers[index].CanSync)
            {
                continue;
            }

            MarkProviderSynced(_providers[index].Name);
        }
        RaiseProviderStateChanged();
    }

    private void SetProviderSyncing(string providerName, bool isSyncing)
    {
        if (isSyncing)
        {
            if (!_syncingProviderNames.Contains(providerName))
            {
                _syncingProviderNames.Add(providerName);
            }
        }
        else
        {
            _syncingProviderNames.Remove(providerName);
        }

        var index = _providers.FindIndex(item => item.Name == providerName);
        if (index >= 0)
        {
            _providers[index] = _providers[index] with { IsSyncing = isSyncing };
        }

        RaiseProviderStateChanged();
    }

    private void SetSyncingAll(bool isSyncingAll)
    {
        _isSyncingAll = isSyncingAll;
        // 已同步行会被 SyncAllAsync 跳过，且不显示转圈。
        for (var index = 0; index < _providers.Count; index++)
        {
            if (_providers[index].CanSync)
            {
                _providers[index] = _providers[index] with { IsSyncing = isSyncingAll && !_providers[index].IsSynced };
            }
        }

        RaiseProviderStateChanged();
    }

    private async Task ApplyProviderSyncResultAsync(SubscriptionProviderSyncResult result, int session)
    {
        // 会话切换或清空后，丢弃完成标记。
        if (session != _selectorSession)
        {
            return;
        }

        var syncedProviderNames = result.SyncedProviderNames.ToList();
        _syncedProviderNames.AddRange(syncedProviderNames.Where(providerName => !_syncedProviderNames.Contains(providerName)));
        // 重新加载带回刷新后的数量和时间戳，再回放已同步标记。
        await RefreshSelectedProviderCatalogAsync();
        if (session != _selectorSession)
        {
            return;
        }

        _hasRefreshedAfterSync = true;
        RaiseProviderStateChanged();
        if (!string.IsNullOrWhiteSpace(_selectorSubscriptionId) && syncedProviderNames.Count > 0)
        {
            ProvidersSynced?.Invoke(this, new SubscriptionProviderSyncCompletedEventArgs(_selectorSubscriptionId, syncedProviderNames));
        }
    }

    private async Task UploadProviderAsync(SubscriptionProviderUploadRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SourcePath) || _uploader is null)
        {
            return;
        }

        var provider = _catalog?.VisibleProviders.FirstOrDefault(item => item.Name == request.ProviderName);
        if (provider is null)
        {
            var item = _providers.FirstOrDefault(item => item.Name == request.ProviderName);
            provider = item is null ? null : new SubscriptionProvider(item.Name, item.Type, item.VehicleType, item.DisplayName, item.Count, null);
        }

        if (provider is not { IsVisible: true })
        {
            return;
        }

        var session = _selectorSession;
        try
        {
            var result = await _uploader.UploadAsync(provider, request.SourcePath);
            if (session != _selectorSession)
            {
                return;
            }

            if (!result.IsUploaded)
            {
                ShowErrorToast(Localize("Subscriptions.Toast.ProviderUploadFailed"));
                return;
            }

            if (_catalog is not null)
            {
                await _catalog.ReloadProviderAsync(provider.Name);
            }

            await RefreshSelectedProviderCatalogAsync();
            if (session != _selectorSession)
            {
                return;
            }

            MarkProviderUploaded(provider.Name);
            _hasRefreshedAfterUpload = true;
            RaiseProviderStateChanged();
            if (!string.IsNullOrWhiteSpace(_selectorSubscriptionId))
            {
                ProvidersSynced?.Invoke(this, new SubscriptionProviderSyncCompletedEventArgs(_selectorSubscriptionId, [provider.Name]));
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, $"File provider upload failed: {request.ProviderName}");
            if (session == _selectorSession)
            {
                ShowErrorToast(Localize("Subscriptions.Toast.ProviderUploadFailed"));
            }
        }
    }

    private void MarkProviderSynced(string providerName)
    {
        var index = _providers.FindIndex(item => item.Name == providerName);
        if (index < 0)
        {
            return;
        }

        if (!_syncedProviderNames.Contains(providerName))
        {
            _syncedProviderNames.Add(providerName);
        }

        _providers[index] = _providers[index] with { IsSynced = true };
    }

    private void MarkProviderUploaded(string providerName)
    {
        var index = _providers.FindIndex(item => item.Name == providerName);
        if (index < 0)
        {
            return;
        }

        if (!_uploadedProviderNames.Contains(providerName))
        {
            _uploadedProviderNames.Add(providerName);
        }

        _providers[index] = _providers[index] with { IsUploaded = true };
    }

    private void RaiseProviderStateChanged()
    {
        OnPropertyChanged(nameof(Providers));
        OnPropertyChanged(nameof(FilteredProviders));
        OnPropertyChanged(nameof(FilteredProxyProviders));
        OnPropertyChanged(nameof(FilteredRuleProviders));
        OnPropertyChanged(nameof(HasProxyProviders));
        OnPropertyChanged(nameof(HasRuleProviders));
        OnPropertyChanged(nameof(SyncedProviderNames));
        OnPropertyChanged(nameof(UploadedProviderNames));
        OnPropertyChanged(nameof(IsSyncingAll));
        OnPropertyChanged(nameof(IsDialogInteractionEnabled));
        OnPropertyChanged(nameof(CanSyncAll));
        OnPropertyChanged(nameof(HasSyncedAllHttpProviders));
        OnPropertyChanged(nameof(HasRefreshedProvidersAfterSync));
        OnPropertyChanged(nameof(HasRefreshedProvidersAfterUpload));
    }

    private SubscriptionProviderItemViewModel ToProviderItem(SubscriptionProvider provider)
    {
        return new SubscriptionProviderItemViewModel(
            provider.Name,
            string.IsNullOrWhiteSpace(provider.Path) ? provider.Name : provider.Path,
            provider.Type,
            provider.VehicleType,
            provider.Count,
            provider.UpdatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? Localize("Common.NotUpdated"),
            HasRuntimeState: provider.UpdatedAt is not null,
            Localization: _localization,
            TrafficInfo: provider.TrafficInfo);
    }

    private void ShowErrorToast(string message)
    {
        ToastRequested?.Invoke(this, (message, ToastType.Error));
    }

    private static bool Contains(string value, string keyword)
    {
        return value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        RaiseProviderStateChanged();
    }

    private string Localize(string key) => _localization?.GetString(key) ?? key;
}
