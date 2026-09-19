using Stelliberty.Application.Runtime;
using Stelliberty.Application.CoreLogs;
using Stelliberty.Domain.CoreLogs;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Updates;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly ILocalizationService _localization;
    private readonly Func<DateTimeOffset> _now;
    private readonly SelectedRuntimeFallbackGenerator? _runtimeFallbackGenerator;
    private readonly RuntimeConfigGenerator _runtimeConfigGenerator;
    private readonly ISelectedSubscriptionRuntimeStore? _runtimeStore;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly AppSettings _settings;
    private int _runtimeRefreshVersion;
    private string? _pendingRuntimeSubscriptionId;
    private string? _startupOverrideRetrySubscriptionId;
    private bool _hasCoreReachedRunning;
    private bool _hasHandledInitialCoreCrash;
    private readonly SemaphoreSlim _runtimeApplyLock = new(1, 1);
    private long _systemProxyEndpointChangeVersion;
    private long _systemProxyEndpointAppliedVersion;
    private NavigationPage _currentPage = NavigationPage.Home;
    private string _toastMessage = string.Empty;
    private ToastType _toastType;
    private bool _isToastVisible;
    private readonly Queue<(string Message, ToastType Type)> _toastQueue = new();
    private readonly object _toastLock = new();
    private Task? _toastProcessingTask;
    private readonly object _coreLogLock = new();
    private readonly Queue<CoreLogMessage> _pendingCoreLogs = new();
    private bool _isCoreLogFlushScheduled;
    private CancellationTokenSource? _proxySelectionSyncCancellation;
    private bool _isDisposed;

    private long _lastNavTickMs;
    // 导航节流只覆盖双击窗口，不影响正常页面切换。
    private const long NavThrottleMs = 150;
    private const int CoreLogFlushBatchSize = 4;
    private const int ToastMessageMaxLength = 72;
    private static readonly SubscriptionChainProxyCycleDetector ChainProxyCycleDetector = new();
    private static readonly TimeSpan ToastDisplayDuration = TimeSpan.FromMilliseconds(1500);
    // 与 ToastNotification.CloseDuration 联动，退场完成后再进下一条
    private static readonly TimeSpan ToastCloseDuration = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan CoreLogFlushDelay = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan ProxySelectionSyncInterval = TimeSpan.FromSeconds(2);
    // 核心离线时每 2 秒重试只会打满日志与 IO，退到低频探活。
    private static readonly TimeSpan ProxySelectionSyncDegradedInterval = TimeSpan.FromSeconds(15);

    public MainWindowViewModel(
        IAppSettingsStore settingsStore,
        ILocalizationService localization,
        ISystemProxyController systemProxyService,
        IAppBehaviorService appBehaviorService,
        IGlobalHotkeyService globalHotkeyService,
        SubscriptionPageViewModel subscriptionPage,
        OverridePageViewModel overridePage,
        ProxyPageViewModel? proxyPage = null,
        ConnectionPageViewModel? connectionPage = null,
        CoreLogPageViewModel? coreLogPage = null,
        RulePageViewModel? rulePage = null,
        IDataManagementService? dataManagementService = null,
        IWebDavDataBackupService? webDavDataBackupService = null,
        IAppUpdateChecker? updateChecker = null,
        Func<DateTimeOffset>? now = null,
        IUwpLoopbackService? uwpLoopbackService = null,
        ISystemProxyHostDetector? systemProxyHostDetector = null,
        IServiceModeManager? serviceModeManager = null,
        Func<SystemProxyApplicationRequest>? systemProxyRequestFactory = null,
        SelectedRuntimeFallbackGenerator? runtimeFallbackGenerator = null,
        RuntimeConfigGenerator? runtimeConfigGenerator = null,
        ISelectedSubscriptionRuntimeStore? runtimeStore = null,
        ICoreManager? coreManager = null,
        AppSettings? initialSettings = null,
        IWindowEffectCapability? windowEffectCapability = null,
        INetworkConnectionProbe? networkConnectionProbe = null,
        IProxyCoreClient? homeProxyClient = null,
        ICoreUpdater? coreUpdater = null,
        IProcessPrivilegeProbe? processPrivilegeProbe = null,
        ServiceModeStatus? initialServiceModeStatus = null,
        SystemProxyPlatform systemPlatform = SystemProxyPlatform.Other,
        IClipboardWriter? clipboardWriter = null,
        IAppLogReader? appLogReader = null,
        IAppLogExporter? appLogExporter = null)
    {
        _settingsStore = settingsStore;
        _localization = localization;
        _synchronizationContext = SynchronizationContext.Current;
        _now = now ?? (() => DateTimeOffset.Now);
        _settings = initialSettings ?? settingsStore.Load();
        DataManagement = new SettingsDataManagementViewModel(
            dataManagementService,
            localization,
            _settings,
            settingsStore,
            webDavDataBackupService,
            _now);
        DataManagement.RestoreCompleted += OnDataRestoreCompleted;
        DataManagement.ToastRequested += OnToastRequested;
        AppLog = new SettingsAppLogViewModel(localization, appLogReader, appLogExporter);
        Settings = new SettingsPageViewModel(localization);
        Settings.SubPageChanged += OnSettingsSubPageChanged;
        _runtimeFallbackGenerator = runtimeFallbackGenerator;
        _runtimeConfigGenerator = runtimeConfigGenerator ?? new RuntimeConfigGenerator();
        _runtimeStore = runtimeStore;
        CoreManager = coreManager;
        Update = new SettingsUpdateViewModel(_settings, settingsStore, localization, _now, updateChecker);
        AppBehavior = new SettingsAppBehaviorViewModel(_settings, settingsStore, localization, appBehaviorService, globalHotkeyService);
        AppBehavior.ToastRequested += OnToastRequested;
        Language = new SettingsLanguageViewModel(_settings, settingsStore, localization);
        Theme = new SettingsThemeViewModel(_settings, settingsStore, localization, windowEffectCapability);
        CoreConfig = new SettingsCoreConfigViewModel(
            _settings,
            settingsStore,
            localization,
            (success, failure) => RefreshSelectedSubscriptionRuntime(SubscriptionPage!.CurrentSubscriptionId, success, failure),
            isEnabled => HomePage!.ApplyTunState(isEnabled && HomePage.CanToggleTun),
            MarkSystemProxyEndpointChanged);
        CoreConfig.ToastRequested += OnToastRequested;
        _localization.LanguageChanged += OnLocalizationLanguageChanged;

        var resolvedSystemProxyRequestFactory = systemProxyRequestFactory ?? (() => SystemProxyApplicationRequest.Build(_settings, systemPlatform));
        HomePage = new HomePageViewModel(
            systemProxyService,
            resolvedSystemProxyRequestFactory,
            serviceModeManager,
            CoreConfig.ApplyTunFromHome,
            networkConnectionProbe,
            homeProxyClient,
            () => $"{_settings.ProxyHost}:{_settings.MixedPort}",
            _settings.LastCoreVersion,
            PersistCoreVersion,
            _now,
            coreManager is null ? null : () => coreManager.RestartAsync(),
            coreUpdater,
            processPrivilegeProbe,
            initialServiceModeStatus,
            localization,
            systemPlatform,
            clipboardWriter);
        SystemIntegration = new SettingsSystemIntegrationViewModel(
            _settings,
            settingsStore,
            localization,
            uwpLoopbackService,
            systemProxyHostDetector,
            systemPlatform,
            () => HomePage.ReapplySystemProxySettings());
        SystemIntegration.ToastRequested += OnToastRequested;
        HomePage.ToastRequested += OnToastRequested;
        Update.ToastRequested += OnToastRequested;
        // TUN 权限由托盘判定，界面只同步结果。
        HomePage.ApplyTunState(_settings.IsTunEnabled);
        // 模式偏好是应用级状态；先注入主页和代理基线，再加载订阅。
        HomePage.ApplyOutboundMode(OutboundModeParser.TryParse(_settings.OutboundMode) ?? Domain.Proxies.OutboundMode.Rule);
        HomePage.RefreshNetworkConnection();
        ConnectionPage = connectionPage ?? new ConnectionPageViewModel(homeProxyClient, _now, localization);
        ProxyPage = proxyPage ?? new ProxyPageViewModel(localization: localization);
        HomePage.PropertyChanged += OnHomePagePropertyChanged;
        // 初始化代理页出站模式和核心运行状态。
        ProxyPage.SetOutboundMode(HomePage.OutboundMode);
        ProxyPage.SetCoreRunning(HomePage.IsCoreRunning);
        // 节点切换会关闭核心连接；无需耦合连接页即可清空本地连接行。
        ProxyPage.NodeSelectionClosedConnections += OnProxyNodeSelectionClosedConnections;
        CoreLogPage = coreLogPage ?? new CoreLogPageViewModel(localization: localization);
        CoreLogPage.LogsCleared += OnCoreLogsCleared;
        RulePage = rulePage ?? new RulePageViewModel(localization: localization);
        RulePage.RuntimeRefreshRequested += OnRuleRuntimeRefreshRequested;
        RulePage.ToastRequested += OnToastRequested;
        SubscriptionPage = subscriptionPage;
        ProxyPage.PropertyChanged += OnProxyPagePropertyChanged;
        SyncHomeSubscriptionRuntimeStats();
        OverridePage = overridePage;
        foreach (var loading in PageLoadingStates) loading.PropertyChanged += OnPageLoadingChanged;
        if (CoreManager is not null)
        {
            CoreManager.StateChanged += OnCoreStateChanged;
            CoreManager.CoreLogReceived += OnCoreLogReceived;
        }

        SubscriptionAutoDelay = new SubscriptionAutoDelayCoordinator(SubscriptionPage, ProxyPage, _now);
        SubscriptionPage.SubscriptionSelected += OnSubscriptionSelected;
        SubscriptionPage.SubscriptionUpdateStarting += OnSubscriptionUpdateStarting;
        SubscriptionPage.SubscriptionsUpdated += OnSubscriptionsUpdated;
        SubscriptionPage.OverrideSelectionSaved += OnOverrideSelectionSaved;
        SubscriptionPage.ProvidersSynced += OnProvidersSynced;
        SubscriptionPage.SubscriptionFileEdited += OnSubscriptionFileEdited;
        SubscriptionPage.SubscriptionMetadataEdited += OnSubscriptionMetadataEdited;
        SubscriptionPage.SubscriptionChainProxySaved += OnSubscriptionChainProxySaved;
        SubscriptionPage.ToastRequested += OnToastRequested;
        OverridePage.OverridesUpdated += OnOverridesUpdated;
        OverridePage.OverridesEdited += OnOverridesEdited;
        OverridePage.OverrideDeleted += OnOverrideDeleted;
        OverridePage.ToastRequested += OnToastRequested;

        ShowHomeCommand = new RelayCommand(() => CurrentPage = NavigationPage.Home);
        ShowProxyCommand = new RelayCommand(() => CurrentPage = NavigationPage.Proxy);
        ShowConnectionsCommand = new RelayCommand(() =>
        {
            CurrentPage = NavigationPage.Connections;
            // 首批快照由启动流程加载，后续进入页面立即刷新。
            if (!ConnectionPage.Loading.IsLoading) _ = ConnectionPage.RefreshConnectionsAsync();
        });
        ShowCoreLogsCommand = new RelayCommand(() => CurrentPage = NavigationPage.CoreLogs);
        ShowRulesCommand = new RelayCommand(() => CurrentPage = NavigationPage.Rules);
        ShowSubscriptionsCommand = new RelayCommand(() => CurrentPage = NavigationPage.Subscriptions);
        ShowOverridesCommand = new RelayCommand(() => CurrentPage = NavigationPage.Overrides);
        ShowSettingsCommand = new RelayCommand(GoToSettingsRoot);
    }

    // 应用退出时分离本地订阅并释放持久页面 VM。
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        foreach (var loading in PageLoadingStates) loading.PropertyChanged -= OnPageLoadingChanged;
        _localization.LanguageChanged -= OnLocalizationLanguageChanged;
        Settings.SubPageChanged -= OnSettingsSubPageChanged;
        DataManagement.RestoreCompleted -= OnDataRestoreCompleted;
        DataManagement.ToastRequested -= OnToastRequested;
        CoreConfig.ToastRequested -= OnToastRequested;
        SystemIntegration.ToastRequested -= OnToastRequested;
        HomePage.ToastRequested -= OnToastRequested;
        Update.ToastRequested -= OnToastRequested;
        AppBehavior.ToastRequested -= OnToastRequested;
        HomePage.PropertyChanged -= OnHomePagePropertyChanged;
        ProxyPage.NodeSelectionClosedConnections -= OnProxyNodeSelectionClosedConnections;
        ProxyPage.PropertyChanged -= OnProxyPagePropertyChanged;
        CoreLogPage.LogsCleared -= OnCoreLogsCleared;
        SubscriptionPage.SubscriptionSelected -= OnSubscriptionSelected;
        SubscriptionPage.SubscriptionUpdateStarting -= OnSubscriptionUpdateStarting;
        SubscriptionPage.SubscriptionsUpdated -= OnSubscriptionsUpdated;
        SubscriptionPage.OverrideSelectionSaved -= OnOverrideSelectionSaved;
        SubscriptionPage.ProvidersSynced -= OnProvidersSynced;
        SubscriptionPage.SubscriptionFileEdited -= OnSubscriptionFileEdited;
        SubscriptionPage.SubscriptionMetadataEdited -= OnSubscriptionMetadataEdited;
        SubscriptionPage.SubscriptionChainProxySaved -= OnSubscriptionChainProxySaved;
        SubscriptionPage.ToastRequested -= OnToastRequested;
        OverridePage.OverridesUpdated -= OnOverridesUpdated;
        OverridePage.OverridesEdited -= OnOverridesEdited;
        OverridePage.OverrideDeleted -= OnOverrideDeleted;
        OverridePage.ToastRequested -= OnToastRequested;
        RulePage.ToastRequested -= OnToastRequested;
        if (CoreManager is not null)
        {
            CoreManager.StateChanged -= OnCoreStateChanged;
            CoreManager.CoreLogReceived -= OnCoreLogReceived;
        }

        StopProxySelectionSync();
        DataManagement.Dispose();
        HomePage.Dispose();
        AppLog.Dispose();
        Update.Dispose();
        AppBehavior.Dispose();
        Language.Dispose();
        Theme.Dispose();
        CoreConfig.Dispose();
        SystemIntegration.Dispose();
        ProxyPage.Dispose();
        ConnectionPage.Dispose();
        CoreLogPage.Dispose();
        RulePage.Dispose();
        SubscriptionPage.Dispose();
        OverridePage.Dispose();
        ClearPendingCoreLogs();
    }

    private void OnRuleRuntimeRefreshRequested(object? sender, EventArgs args)
    {
        RefreshSelectedSubscriptionRuntime(
            SubscriptionPage.CurrentSubscriptionId,
            "Runtime config refreshed after rule update",
            "Runtime config refresh failed after rule update");
    }

    public string Title => AppMetadata.DisplayName;

    public HomePageViewModel HomePage { get; }
    public ProxyPageViewModel ProxyPage { get; }
    public ConnectionPageViewModel ConnectionPage { get; }
    public CoreLogPageViewModel CoreLogPage { get; }
    public RulePageViewModel RulePage { get; }
    public SubscriptionPageViewModel SubscriptionPage { get; }
    public OverridePageViewModel OverridePage { get; }

    public SettingsDataManagementViewModel DataManagement { get; }
    public SettingsPageViewModel Settings { get; }
    public SettingsAppLogViewModel AppLog { get; }
    public SettingsUpdateViewModel Update { get; }
    public SettingsAppBehaviorViewModel AppBehavior { get; }
    public SettingsLanguageViewModel Language { get; }
    public SettingsThemeViewModel Theme { get; }
    public SettingsSystemIntegrationViewModel SystemIntegration { get; }
    public SettingsCoreConfigViewModel CoreConfig { get; }

    public SubscriptionAutoDelayCoordinator SubscriptionAutoDelay { get; }

    public string ToastMessage => _toastMessage;

    public ToastType ToastType => _toastType;

    public bool IsToastVisible => _isToastVisible;

    internal ICoreManager? CoreManager { get; }

    internal IAppSettingsStore SettingsStore => _settingsStore;

    internal Task? LastRuntimeRefreshTask { get; private set; }

    internal string? LastRuntimeRefreshSubscriptionId { get; private set; }

    internal string LastRuntimeApplyMode { get; private set; } = "none";

    internal int? LastRuntimeApplyPid { get; private set; }

    internal string? LastRuntimeApplyError { get; private set; }

    private string Localize(string key) => _localization.GetString(key);

    private static T ParseEnum<T>(string value, T fallback) where T : struct, Enum
    {
        return Enum.TryParse<T>(value, out var result) ? result : fallback;
    }

    private void OnCoreStateChanged(object? sender, CoreSnapshot snapshot)
    {
        if (_isDisposed)
        {
            return;
        }

        ApplyCoreSnapshot(snapshot);
    }

    private void OnCoreLogReceived(object? sender, CoreLogMessage message)
    {
        if (_isDisposed)
        {
            return;
        }

        lock (_coreLogLock)
        {
            if (_pendingCoreLogs.Count >= CoreLogFlushBatchSize)
            {
                _pendingCoreLogs.Dequeue();
            }

            _pendingCoreLogs.Enqueue(message);
            if (_isCoreLogFlushScheduled)
            {
                return;
            }

            _isCoreLogFlushScheduled = true;
        }

        _ = FlushCoreLogsSoonAsync();
    }

    private async Task FlushCoreLogsSoonAsync()
    {
        await Task.Delay(CoreLogFlushDelay);
        if (_isDisposed)
        {
            return;
        }

        List<CoreLogMessage> logs;
        lock (_coreLogLock)
        {
            logs = [.. _pendingCoreLogs];
            _pendingCoreLogs.Clear();
            _isCoreLogFlushScheduled = false;
        }

        if (logs.Count == 0)
        {
            return;
        }

        PostToUi(() => CoreLogPage.AppendLogs(logs));
    }

    private void OnCoreLogsCleared(object? sender, EventArgs args)
    {
        ClearPendingCoreLogs();
    }

    private void OnProxyNodeSelectionClosedConnections(object? sender, EventArgs args)
    {
        ConnectionPage.ApplyAllConnectionsClosed();
    }

    private void OnProxyPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ProxyPage.ParsedGroupCount)
            or nameof(ProxyPage.ParsedNodeCount)
            or nameof(ProxyPage.TestedAverageDelay)
            or nameof(ProxyPage.LoadedSubscriptionId))
        {
            SyncHomeSubscriptionRuntimeStats();
        }
    }

    private void SyncHomeSubscriptionRuntimeStats()
    {
        if (!string.Equals(
                SubscriptionPage.CurrentSubscriptionId,
                ProxyPage.LoadedSubscriptionId,
                StringComparison.Ordinal))
        {
            SubscriptionPage.ClearHomeCardRuntimeStats();
            return;
        }

        SubscriptionPage.SetHomeCardRuntimeStats(
            ProxyPage.ParsedGroupCount,
            ProxyPage.ParsedNodeCount,
            ProxyPage.TestedAverageDelay);
    }

    private void ClearPendingCoreLogs()
    {
        lock (_coreLogLock)
        {
            _pendingCoreLogs.Clear();
            _isCoreLogFlushScheduled = false;
        }
    }

    private void ApplyCoreSnapshot(CoreSnapshot snapshot)
    {
        PostToUi(() =>
        {
            var isRunning = snapshot.State == CoreState.Running;
            if (isRunning)
            {
                var completedSubscriptionId = _pendingRuntimeSubscriptionId;
                var isInitialRunning = !_hasCoreReachedRunning;
                _hasCoreReachedRunning = true;
                _pendingRuntimeSubscriptionId = null;
                _startupOverrideRetrySubscriptionId = null;
                if (!string.IsNullOrWhiteSpace(completedSubscriptionId))
                {
                    SubscriptionPage.ClearSubscriptionRuntimeFailure(completedSubscriptionId);
                }
                else if (isInitialRunning && CurrentRuntimeSubscriptionId() is { Length: > 0 } startupSubscriptionId)
                {
                    SubscriptionPage.ClearSubscriptionRuntimeFailure(startupSubscriptionId);
                }
            }
            else if (snapshot.State == CoreState.Crashed && _pendingRuntimeSubscriptionId is { } pendingSubscriptionId)
            {
                // 优先回退到该订阅的基础配置，再使用空配置。
                var refreshVersion = ++_runtimeRefreshVersion;
                _pendingRuntimeSubscriptionId = null;
                var failureMessage = snapshot.LastError ?? pendingSubscriptionId;
                if (SubscriptionPage.DisableOverridesForSubscription(pendingSubscriptionId))
                {
                    AppLogger.Warning($"Subscription runtime config crashed the core; disabled selected overrides and reloaded: {pendingSubscriptionId}, {failureMessage}");
                    RefreshSelectedSubscriptionRuntime(pendingSubscriptionId, "Runtime config refreshed after disabling subscription overrides", "Runtime config refresh failed after disabling subscription overrides");
                }
                else
                {
                    if (!_hasCoreReachedRunning)
                    {
                        _hasHandledInitialCoreCrash = true;
                    }

                    AppLogger.Warning($"Subscription runtime config crashed the core; restarting with empty config: {failureMessage}");
                    _ = RevertToEmptyRuntimeAsync(pendingSubscriptionId, refreshVersion, failureMessage);
                }
            }
            else if (snapshot.State == CoreState.Crashed
                && !_hasCoreReachedRunning
                && CurrentRuntimeSubscriptionId() is { Length: > 0 } startupSubscriptionId)
            {
                _pendingRuntimeSubscriptionId = null;
                var failureMessage = snapshot.LastError ?? startupSubscriptionId;
                if (!_hasHandledInitialCoreCrash
                    && !string.Equals(_startupOverrideRetrySubscriptionId, startupSubscriptionId, StringComparison.Ordinal)
                    && SubscriptionPage.DisableOverridesForSubscription(startupSubscriptionId))
                {
                    _startupOverrideRetrySubscriptionId = startupSubscriptionId;
                    AppLogger.Warning($"Selected subscription failed core startup; disabled selected overrides and reloaded: {startupSubscriptionId}, {failureMessage}");
                    RefreshSelectedSubscriptionRuntime(startupSubscriptionId, "Runtime config refreshed after disabling subscription overrides", "Runtime config refresh failed after disabling subscription overrides");
                }
                else if (!_hasHandledInitialCoreCrash)
                {
                    // 首次启动没有覆写降级时，清空选择以避免崩溃循环。
                    _hasHandledInitialCoreCrash = true;
                    var refreshVersion = ++_runtimeRefreshVersion;
                    AppLogger.Warning($"Selected subscription failed core startup; restarting with empty config: {failureMessage}");
                    _ = RevertToEmptyRuntimeAsync(startupSubscriptionId, refreshVersion, failureMessage);
                }
            }

            if (!isRunning)
            {
                ClearPendingCoreLogs();
            }

            HomePage.ApplyCoreRunning(isRunning);
            CoreLogPage.ApplyCoreRunning(isRunning);
            RulePage.ApplyCoreRunning(isRunning);
        });
    }

    private void PostToUi(Action action)
    {
        if (_isDisposed)
        {
            return;
        }

        if (_synchronizationContext is not null && SynchronizationContext.Current != _synchronizationContext)
        {
            _synchronizationContext.Post(_ =>
            {
                if (!_isDisposed)
                {
                    action();
                }
            }, null);
            return;
        }

        action();
    }

    private string? CurrentRuntimeSubscriptionId()
    {
        return SubscriptionPage.CurrentSubscriptionId ?? SubscriptionPage.SelectionStore?.GetCurrentSubscriptionId();
    }

    private void OnSubscriptionSelected(object? sender, string? subscriptionId)
    {
        SubscriptionAutoDelay.Reset();
        ProxyPage.CancelDelayTests();
        SubscriptionPage.ClearHomeCardRuntimeStats();
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            // 删除最后一个订阅时，让核心收敛到空配置，而不是保留旧配置。
            LastRuntimeRefreshTask = ConvergeCoreToEmptyRuntimeAsync();
            return;
        }

        RefreshSelectedSubscriptionRuntime(subscriptionId, "Subscription runtime config refreshed", "Subscription runtime config refresh failed");
    }

    private void OnSubscriptionUpdateStarting(object? sender, IReadOnlyList<string> subscriptionIds)
    {
        if (!string.IsNullOrWhiteSpace(SubscriptionPage.CurrentSubscriptionId)
            && subscriptionIds.Contains(SubscriptionPage.CurrentSubscriptionId, StringComparer.Ordinal))
        {
            SubscriptionAutoDelay.Reset();
            ProxyPage.CancelDelayTests();
        }
    }

    private void OnSubscriptionsUpdated(object? sender, SubscriptionUpdateResult result)
    {
        if (!string.IsNullOrWhiteSpace(SubscriptionPage.CurrentSubscriptionId)
            && result.UpdatedSubscriptionIds.Contains(SubscriptionPage.CurrentSubscriptionId, StringComparer.Ordinal))
        {
            RefreshSelectedSubscriptionRuntime(SubscriptionPage.CurrentSubscriptionId, "Runtime config refreshed after subscription update", "Runtime config refresh failed after subscription update");
        }
    }

    private void OnOverrideSelectionSaved(object? sender, string subscriptionId)
    {
        if (string.Equals(subscriptionId, SubscriptionPage.CurrentSubscriptionId, StringComparison.Ordinal))
        {
            RefreshSelectedSubscriptionRuntime(subscriptionId, "Runtime config refreshed after subscription override selection", "Runtime config refresh failed after subscription override selection");
        }
    }

    private void OnSubscriptionFileEdited(object? sender, string subscriptionId)
    {
        if (string.Equals(subscriptionId, SubscriptionPage.CurrentSubscriptionId, StringComparison.Ordinal))
        {
            RefreshSelectedSubscriptionRuntime(subscriptionId, "Runtime config refreshed after subscription file edit", "Runtime config refresh failed after subscription file edit");
        }
    }

    private void OnSubscriptionMetadataEdited(object? sender, string subscriptionId)
    {
        if (string.Equals(subscriptionId, SubscriptionPage.CurrentSubscriptionId, StringComparison.Ordinal))
        {
            RefreshSelectedSubscriptionRuntime(subscriptionId, "Runtime config refreshed after subscription metadata edit", "Runtime config refresh failed after subscription metadata edit");
        }
    }

    private void OnSubscriptionChainProxySaved(object? sender, string subscriptionId)
    {
        if (string.Equals(subscriptionId, SubscriptionPage.CurrentSubscriptionId, StringComparison.Ordinal))
        {
            RefreshSelectedSubscriptionRuntime(subscriptionId, "Runtime config refreshed after chain proxy update", "Runtime config refresh failed after chain proxy update");
        }
    }

    private void OnOverridesUpdated(object? sender, OverrideUpdateResult result)
    {
        if (SubscriptionPage.CurrentSubscriptionUsesAnyOverride(result.UpdatedOverrideIds))
        {
            RefreshSelectedSubscriptionRuntime(SubscriptionPage.CurrentSubscriptionId, "Runtime config refreshed after override update", "Runtime config refresh failed after override update");
        }
    }

    private void OnProvidersSynced(object? sender, SubscriptionProviderSyncCompletedEventArgs args)
    {
        if (string.Equals(args.SubscriptionId, SubscriptionPage.CurrentSubscriptionId, StringComparison.Ordinal) && args.SyncedProviderNames.Count > 0)
        {
            ProxyPage.RefreshProxies();
            RulePage.RefreshRulesCommand.Execute(null);
        }
    }

    private void OnOverridesEdited(object? sender, IReadOnlyList<string> overrideIds)
    {
        if (SubscriptionPage.CurrentSubscriptionUsesAnyOverride(overrideIds))
        {
            RefreshSelectedSubscriptionRuntime(SubscriptionPage.CurrentSubscriptionId, "Runtime config refreshed after override edit", "Runtime config refresh failed after override edit");
        }
    }

    private void OnDataRestoreCompleted(object? sender, DataRestoreMode mode)
    {
        LastRuntimeRefreshTask = ReconcileAfterDataRestoreAsync(mode);
    }

    private async Task ReconcileAfterDataRestoreAsync(DataRestoreMode mode)
    {
        try
        {
            ReloadSettingsFromStore();
            SyncRestoredSettingsPages();
            HomePage.ApplyTunState(_settings.IsTunEnabled && HomePage.CanToggleTun);
            HomePage.ApplyOutboundMode(OutboundModeParser.TryParse(_settings.OutboundMode) ?? Domain.Proxies.OutboundMode.Rule);
            MarkSystemProxyEndpointChanged();
            await SubscriptionPage.InitializeAsync();
            await OverridePage.InitializeAsync();

            if (mode == DataRestoreMode.Overwrite)
            {
                SubscriptionPage.ClearCurrentSubscription();
                SubscriptionAutoDelay.Reset();
                ProxyPage.CancelDelayTests();
                await ApplyEmptyRuntimeToCoreAsync();
                AppLogger.Info("Runtime config refreshed after data overwrite restore: empty");
                return;
            }

            await RefreshSelectedSubscriptionRuntimeAsync(
                SubscriptionPage.CurrentSubscriptionId,
                "Runtime config refreshed after data merge restore",
                "Runtime config refresh failed after data merge restore");
        }
        catch (Exception exception)
        {
            LastRuntimeApplyMode = "error";
            LastRuntimeApplyError = exception.Message;
            AppLogger.Error(exception, "Runtime config refresh failed after data restore");
            ShowErrorToast(Localize("RuntimeConfig.Toast.RefreshFailed"));
        }
    }

    private void SyncRestoredSettingsPages()
    {
        Language.RefreshFromSettings();
        Theme.RefreshFromSettings();
        AppBehavior.RefreshFromSettings();
        Update.RefreshFromSettings();
        DataManagement.RefreshFromSettings();
        CoreConfig.RefreshFromSettings();
        SystemIntegration.RefreshFromSettings();
    }

    private void ReloadSettingsFromStore()
    {
        var restored = _settingsStore.Load();
        foreach (var property in typeof(AppSettings).GetProperties().Where(property => property.CanRead && property.CanWrite))
        {
            property.SetValue(_settings, property.GetValue(restored));
        }
    }

    private void OnOverrideDeleted(object? sender, OverrideDeleteResult result)
    {
        if (result.AffectedSubscriptionIds.Contains(SubscriptionPage.CurrentSubscriptionId ?? string.Empty, StringComparer.Ordinal))
        {
            RefreshSelectedSubscriptionRuntime(SubscriptionPage.CurrentSubscriptionId, "Runtime config refreshed after override delete", "Runtime config refresh failed after override delete");
        }
    }

    // 宿主心跳只提供节奏；当前页面状态决定是否刷新。
    public void OnHomeRuntimeTick()
    {
        SyncTunState();
        HomePage.RefreshServiceMode();
        if (CurrentPage == NavigationPage.Home)
        {
            HomePage.RefreshRuntime();
        }
        else if (CurrentPage == NavigationPage.Connections
            && !ConnectionPage.IsMonitoringPaused
            && !ConnectionPage.Loading.IsLoading)
        {
            // 连接是动态数据；可见且未暂停页面每秒拉取，避免入口为空。
            _ = ConnectionPage.RefreshConnectionsAsync();
        }
    }

    private void SyncTunState()
    {
        var isTunEnabled = _settingsStore.Load().IsTunEnabled;
        if (_settings.IsTunEnabled == isTunEnabled && HomePage.IsTunEnabled == isTunEnabled)
        {
            return;
        }

        _settings.IsTunEnabled = isTunEnabled;
        CoreConfig.RefreshFromSettings();
        HomePage.ApplyTunState(isTunEnabled);
    }

    private void RefreshSelectedSubscriptionRuntime(string? subscriptionId, string successMessage, string failureMessage)
    {
        LastRuntimeRefreshTask = RefreshSelectedSubscriptionRuntimeAsync(subscriptionId, successMessage, failureMessage);
    }

    private async Task RefreshSelectedSubscriptionRuntimeAsync(string? subscriptionId, string successMessage, string failureMessage)
    {
        using var proxyLoading = ProxyPage.Loading.BeginLoading();
        using var ruleLoading = RulePage.Loading.BeginLoading();
        var refreshVersion = Interlocked.Increment(ref _runtimeRefreshVersion);
        var endpointChangeVersion = Volatile.Read(ref _systemProxyEndpointChangeVersion);
        LastRuntimeRefreshSubscriptionId = subscriptionId;
        LastRuntimeApplyMode = "generating";
        LastRuntimeApplyPid = null;
        LastRuntimeApplyError = null;
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            await ApplyEmptyRuntimeToCoreAsync(refreshVersion, endpointChangeVersion);
            AppLogger.Info($"{successMessage}: empty");
            return;
        }

        var runtimeFallbackGenerator = _runtimeFallbackGenerator
            ?? throw new InvalidOperationException("Runtime config generator is not initialized");

        try
        {
            var shouldClearFailureNow = true;
            var wasAppliedToCore = false;
            var result = GenerateSelectedSubscriptionRuntimeWithOverrideFallback(subscriptionId, runtimeFallbackGenerator);
            if (CoreManager is not null)
            {
                // pending 只覆盖重启路径，重载成功后立即清除。
                _pendingRuntimeSubscriptionId = subscriptionId;
                var applyResult = await ApplyRuntimeConfigToCoreAsync(
                    new CoreApplyConfigRequest(result.RuntimeConfigContent, subscriptionId),
                    refreshVersion,
                    endpointChangeVersion);
                if (applyResult is null)
                {
                    return;
                }

                LastRuntimeApplyMode = applyResult.Mode.ToString();
                LastRuntimeApplyPid = applyResult.Pid;
                wasAppliedToCore = true;
                if (applyResult.Mode == CoreApplyMode.Reload)
                {
                    _pendingRuntimeSubscriptionId = null;
                }
                else
                {
                    shouldClearFailureNow = false;
                }
            }
            else
            {
                LastRuntimeApplyMode = "generated";
            }

            await ProxyPage.RefreshProxiesAsync();
            ProxyPage.BindLoadedConfigToSubscription(subscriptionId);
            if (wasAppliedToCore
                && ProxyPage.LastRuntimeSnapshot is { } snapshot
                && ChainProxyCycleDetector.HasCycle(snapshot))
            {
                ShowToast(Localize("RuntimeConfig.Toast.ChainProxyCycleWarning"), ToastType.Warning);
            }

            RulePage.RefreshRulesCommand.Execute(null);
            if (shouldClearFailureNow)
            {
                SubscriptionPage.ClearSubscriptionRuntimeFailure(subscriptionId);
            }

            AppLogger.Info($"{successMessage}: {subscriptionId}");
        }
        catch (Exception exception)
        {
            LastRuntimeApplyMode = "error";
            LastRuntimeApplyError = exception.Message;
            AppLogger.Error(exception, $"{failureMessage}: {subscriptionId}");
            var isCycleError = IsCoreCycleError(exception);
            if (isCycleError)
            {
                ShowToast(Localize("RuntimeConfig.Toast.ChainProxyCycleWarning"), ToastType.Warning);
            }

            // 生成器只处理覆写失败回退；此路径收敛到空配置。
            try
            {
                _pendingRuntimeSubscriptionId = null;
                SubscriptionPage.RefreshOverrideSelectionFromStore(subscriptionId);
                await RevertToEmptyRuntimeAsync(subscriptionId, refreshVersion, exception.Message, suppressFailureToast: isCycleError);
            }
            catch (Exception revertException)
            {
                AppLogger.Error(revertException, "Failed to revert to empty config");
                ShowErrorToast(Localize("RuntimeConfig.Toast.LoadFailedRevertFailed"));
            }
        }
    }

    private SelectedSubscriptionRuntimeResult GenerateSelectedSubscriptionRuntimeWithOverrideFallback(
        string subscriptionId,
        SelectedRuntimeFallbackGenerator runtimeFallbackGenerator)
    {
        var request = new SelectedSubscriptionRuntimeRequest([], CurrentRuntimeConfigParams());
        var result = runtimeFallbackGenerator.Generate(subscriptionId, request);
        if (result.OverridesDisabled)
        {
            SubscriptionPage.RefreshOverrideSelectionFromStore(subscriptionId);
            ShowToast(Localize("RuntimeConfig.Toast.OverridesDisabled"), ToastType.Warning);
        }

        return result.Runtime;
    }

    private static bool IsCoreCycleError(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("loop is detected in ProxyGroup", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("circular dialer-proxy dependency", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private RuntimeConfigParams CurrentRuntimeConfigParams()
    {
        var parameters = RuntimeConfigParams.FromSettings(_settings);
        return parameters with { IsTunEnabled = parameters.IsTunEnabled && HomePage.IsTunEnabled && HomePage.CanToggleTun };
    }

    private async Task RevertToEmptyRuntimeAsync(
        string subscriptionId,
        int refreshVersion,
        string? failureMessage = null,
        bool suppressFailureToast = false)
    {
        // 较早的刷新不能覆盖用户后来选择的订阅。
        if (refreshVersion != Volatile.Read(ref _runtimeRefreshVersion) || !string.Equals(CurrentRuntimeSubscriptionId(), subscriptionId, StringComparison.Ordinal))
        {
            return;
        }

        SubscriptionPage.MarkSubscriptionRuntimeFailed(subscriptionId, failureMessage ?? "Subscription runtime config is unavailable");
        SubscriptionPage.ClearCurrentSubscription();
        SubscriptionAutoDelay.Reset();
        ProxyPage.CancelDelayTests();
        await ApplyEmptyRuntimeToCoreAsync();
        if (!suppressFailureToast)
        {
            ShowErrorToast(Localize("RuntimeConfig.Toast.LoadFailedReverted"));
        }
    }

    // 删除最后一个订阅后，带本地失败处理收敛到空配置。
    private async Task ConvergeCoreToEmptyRuntimeAsync()
    {
        LastRuntimeApplyError = null;
        try
        {
            await ApplyEmptyRuntimeToCoreAsync();
        }
        catch (Exception exception)
        {
            LastRuntimeApplyMode = "error";
            LastRuntimeApplyError = exception.Message;
            AppLogger.Error(exception, "Failed to switch to empty runtime config after subscription delete");
        }
    }

    // 空运行时配置收敛后也刷新代理页和规则页。
    private async Task ApplyEmptyRuntimeToCoreAsync(int? refreshVersion = null, long endpointChangeVersion = 0)
    {
        using var proxyLoading = ProxyPage.Loading.BeginLoading();
        using var ruleLoading = RulePage.Loading.BeginLoading();
        var emptyConfig = _runtimeConfigGenerator.GenerateEmpty(CurrentRuntimeConfigParams());
        _runtimeStore?.SaveEmpty(emptyConfig.RuntimeConfigContent);
        if (CoreManager is not null)
        {
            var applyResult = await ApplyRuntimeConfigToCoreAsync(
                new CoreApplyConfigRequest(emptyConfig.RuntimeConfigContent, string.Empty),
                refreshVersion,
                endpointChangeVersion);
            if (applyResult is null)
            {
                return;
            }

            LastRuntimeApplyMode = applyResult.Mode.ToString();
            LastRuntimeApplyPid = applyResult.Pid;
        }
        else
        {
            LastRuntimeApplyMode = "empty";
        }

        await ProxyPage.RefreshProxiesAsync();
        RulePage.RefreshRulesCommand.Execute(null);
    }

    private void OnToastRequested(object? sender, (string Message, ToastType Type) toast)
    {
        ShowToast(toast.Message, toast.Type);
    }

    private void MarkSystemProxyEndpointChanged()
    {
        Interlocked.Increment(ref _systemProxyEndpointChangeVersion);
    }

    private async Task<CoreApplyConfigResult?> ApplyRuntimeConfigToCoreAsync(
        CoreApplyConfigRequest request,
        int? refreshVersion,
        long endpointChangeVersion)
    {
        if (CoreManager is null)
        {
            return null;
        }

        await _runtimeApplyLock.WaitAsync();
        try
        {
            if (refreshVersion is { } version && version != Volatile.Read(ref _runtimeRefreshVersion))
            {
                return null;
            }

            var applyResult = await CoreManager.ApplyConfigAsync(request);
            ReapplySystemProxyAfterRuntimeApplyIfCurrent(endpointChangeVersion);
            return applyResult;
        }
        finally
        {
            _runtimeApplyLock.Release();
        }
    }

    private void ReapplySystemProxyAfterRuntimeApplyIfCurrent(long endpointChangeVersion)
    {
        if (endpointChangeVersion == 0
            || endpointChangeVersion != Volatile.Read(ref _systemProxyEndpointChangeVersion)
            || endpointChangeVersion <= Volatile.Read(ref _systemProxyEndpointAppliedVersion))
        {
            return;
        }

        Volatile.Write(ref _systemProxyEndpointAppliedVersion, endpointChangeVersion);
        HomePage.ReapplySystemProxySettings();
    }

    private void OnHomePagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomePage.OutboundMode))
        {
            ProxyPage.SetOutboundMode(HomePage.OutboundMode);
            PersistOutboundMode(HomePage.OutboundMode);
        }
        else if (e.PropertyName == nameof(HomePage.IsCoreRunning))
        {
            ProxyPage.SetCoreRunning(HomePage.IsCoreRunning);
        }
    }

    // 主页广播频繁，只有值实际变化时才保存设置。
    private void PersistOutboundMode(Domain.Proxies.OutboundMode mode)
    {
        var modeName = mode.ToString();
        if (string.Equals(_settings.OutboundMode, modeName, StringComparison.Ordinal))
        {
            return;
        }

        _settings.OutboundMode = modeName;
        _settingsStore.Save(_settings);
    }

    private void PersistCoreVersion(string version)
    {
        if (string.Equals(_settings.LastCoreVersion, version, StringComparison.Ordinal))
        {
            return;
        }

        _settings.LastCoreVersion = version;
        _settingsStore.Save(_settings);
    }

    public void ShowToast(string message) => ShowToast(message, ToastType.Info);

    public void ShowErrorToast(string message) => ShowToast(message, ToastType.Error);

    public void ShowToast(string message, ToastType type)
    {
        if (_isDisposed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var displayMessage = CompactToastMessage(message, type);
        lock (_toastLock)
        {
            _toastQueue.Enqueue((displayMessage, type));
            if (_toastProcessingTask == null || _toastProcessingTask.IsCompleted)
            {
                // 保持一个队列工作器，避免多个 toast 竞争显示状态。
                _toastProcessingTask = ProcessToastQueueAsync();
            }
        }
    }

    private string CompactToastMessage(string message, ToastType type)
    {
        var normalized = message.ReplaceLineEndings(" ").Trim();
        if (normalized.Length <= ToastMessageMaxLength)
        {
            return normalized;
        }

        if (type == ToastType.Error)
        {
            return string.Format(Localize("Common.Error.ViewAppLogsHint"), ErrorToastSummary(normalized));
        }

        return $"{normalized[..(ToastMessageMaxLength - 1)].TrimEnd()}…";
    }

    private string ErrorToastSummary(string message)
    {
        if (message.Contains("hub startup failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("bootstrap failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Core startup failed", StringComparison.OrdinalIgnoreCase))
        {
            return Localize("Common.Error.CoreStartupFailed");
        }

        var separatorIndex = FirstErrorSeparatorIndex(message);
        if (separatorIndex is > 0 and <= 36)
        {
            return message[..separatorIndex].Trim();
        }

        return Localize("Common.Error.OperationFailed");
    }

    private static int FirstErrorSeparatorIndex(string message)
    {
        var colonIndex = message.IndexOf(':', StringComparison.Ordinal);
        var chineseColonIndex = message.IndexOf('：', StringComparison.Ordinal);
        if (colonIndex < 0)
        {
            return chineseColonIndex;
        }

        return chineseColonIndex < 0 ? colonIndex : Math.Min(colonIndex, chineseColonIndex);
    }

    private async Task ProcessToastQueueAsync()
    {
        while (true)
        {
            if (_isDisposed)
            {
                return;
            }

            (string Message, ToastType Type) toast;
            lock (_toastLock)
            {
                if (_toastQueue.Count == 0)
                {
                    return;
                }
                toast = _toastQueue.Dequeue();
            }

            try
            {
                PostToUi(() =>
                {
                    _toastMessage = toast.Message;
                    _toastType = toast.Type;
                    _isToastVisible = true;
                    OnPropertyChanged(nameof(ToastMessage));
                    OnPropertyChanged(nameof(ToastType));
                    OnPropertyChanged(nameof(IsToastVisible));
                });

                await Task.Delay(ToastDisplayDuration);
                if (_isDisposed)
                {
                    return;
                }

                PostToUi(() =>
                {
                    _isToastVisible = false;
                    OnPropertyChanged(nameof(IsToastVisible));
                });

                bool hasMore;
                lock (_toastLock)
                {
                    hasMore = _toastQueue.Count > 0;
                }
                if (hasMore)
                {
                    await Task.Delay(ToastCloseDuration);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Toast display failed");

                PostToUi(() =>
                {
                    _isToastVisible = false;
                    OnPropertyChanged(nameof(IsToastVisible));
                });
            }
        }
    }
}
