using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Infrastructure.Tray;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;
using Stelliberty.Application.Rules;
using Stelliberty.Domain.Rules;
using Stelliberty.Application.Runtime;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Updates;
using Stelliberty.Desktop.Services;
using Stelliberty.Infrastructure.Core;
using Stelliberty.Infrastructure.DataManagement;
using Stelliberty.Infrastructure.Diagnostics;
using Stelliberty.Infrastructure.Localization;
using Stelliberty.Infrastructure.Overrides;
using Stelliberty.Infrastructure.Platform;
using Stelliberty.Infrastructure.Proxies;
using Stelliberty.Infrastructure.Rules;
using Stelliberty.Infrastructure.Runtime;
using Stelliberty.Infrastructure.Settings;
using Stelliberty.Infrastructure.Subscriptions;
using Stelliberty.Infrastructure.Updates;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Desktop.Controls;
using Stelliberty.Desktop.Debug;
using Stelliberty.Desktop.Localization;
using Stelliberty.Native;
using Stelliberty.Native.Hub;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop;

public sealed partial class App : Avalonia.Application
{
    private DesktopTraySession? _traySession;
    private MainWindow? _mainWindow;
    private long _backgroundRevision = -1;
    private long _subscriptionRevision;
    private long _providerRevision;
    private AppUpdateAutoCheckResult? _lastAppUpdate;
    private DispatcherTimer? _homeRuntimeTimer;
    // 主动退出最多等待服务核心 5 秒，普通核心在 Rust 侧使用相同总预算。

    public override void Initialize()
    {
        AppLogger.Debug("Loading Avalonia XAML");
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
#if DEBUG
            var startupStartedAt = Stopwatch.GetTimestamp();
            LogStartupTrace("Framework initialization started", startupStartedAt);
#endif
            _traySession = DesktopLaunchContext.TraySession
                ?? throw new InvalidOperationException("Desktop UI requires a tray session.");
            _traySession.ActivationRequested += OnTrayActivationRequested;
            _traySession.ToggleRequested += OnTrayToggleRequested;
            _traySession.Disconnected += OnTrayDisconnected;
            _traySession.BackgroundChanged += OnBackgroundChanged;
            AppLogger.Info("Creating main window");
            var platformDirectories = new DesktopPlatformDirectories();
            // 代理组图标磁盘缓存，进程重启后免重下。
            RemoteImageCache.Configure(Path.Combine(platformDirectories.AppDataDirectory, "icon-cache"));
            var settingsStore = new JsonAppSettingsStore(platformDirectories);
            var settings = settingsStore.Load();
#if DEBUG
            LogStartupTrace($"Settings loaded silent={settings.IsSilentStartEnabled} tun={settings.IsTunEnabled}", startupStartedAt);
#endif
            var updateChecker = new GitHubAppUpdateChecker(
                () => settingsStore.Load().AppUpdateChannel,
                () =>
                {
                    var currentSettings = settingsStore.Load();
                    return (currentSettings.ProxyHost, currentSettings.MixedPort);
                });
            var systemProxyPlatform = CurrentSystemProxyPlatform();
            IUwpLoopbackService uwpLoopbackService = OperatingSystem.IsWindows()
                ? new WindowsUwpLoopbackService()
                : new UnsupportedUwpLoopbackService();
            var systemProxyHostDetector = new NetworkInterfaceSystemProxyHostDetector();
            ISystemProxyController systemProxyService = new TraySystemProxyController();
            var serviceModeManager = new TrayServiceModeManager();
            var networkConnectionProbe = new SystemNetworkConnectionProbe();
            var processPrivilegeProbe = new SystemProcessPrivilegeProbe();
            IAppBehaviorService appBehaviorService = CreateAppBehaviorService();
            IGlobalHotkeyService globalHotkeyService = new TrayGlobalHotkeyService();
            var initialLanguage = AppLanguageParser.Parse(settings.Language);
            var localization = new JsonLocalizationService(initialLanguage);
            LocalizationManager.Initialize(localization);
            var subscriptionStore = new FileSubscriptionStore(platformDirectories.AppDataDirectory);
            var subscriptionSelectionStore = new FileSubscriptionSelectionStore(platformDirectories.AppDataDirectory);
            var ruleOverrideStore = new FileRuleOverrideStore(platformDirectories.AppDataDirectory);
            var ruleOverrideService = new RuleOverrideService(
                subscriptionStore,
                subscriptionSelectionStore,
                ruleOverrideStore,
                new RuleParser(),
                new FileRuleBaselineConfigSource(platformDirectories.RuntimeDirectory));
            var proxySelectionStore = new FileProxySelectionStore(platformDirectories.AppDataDirectory);
            var overrideStore = new FileOverrideStore(platformDirectories.AppDataDirectory);
#if DEBUG
            IRemoteOverrideDownloader remoteOverrideDownloader = new RemoteOverrideDownloader();
#else
            IRemoteOverrideDownloader remoteOverrideDownloader = new HttpRemoteOverrideDownloader();
#endif
            var overrideImporter = new OverrideImporter(overrideStore, remoteOverrideDownloader);
            var overrideUpdater = new OverrideUpdater(overrideStore, remoteOverrideDownloader);
            var overrideDeleter = new OverrideDeleter(overrideStore, subscriptionStore);
            var overrideSelectionUpdater = new SubscriptionOverrideSelectionUpdater(subscriptionStore, overrideStore);
            var localSubscriptionImporter = new LocalSubscriptionFileImporter(
                new LocalSubscriptionImporter(subscriptionStore),
                new FileLocalSubscriptionFileReader());
#if DEBUG
            IRemoteSubscriptionDownloader remoteSubscriptionDownloader = new RemoteSubscriptionDownloader(() => (settings.ProxyHost, settings.MixedPort));
#else
            IRemoteSubscriptionDownloader remoteSubscriptionDownloader = new HttpRemoteSubscriptionDownloader(() => (settings.ProxyHost, settings.MixedPort));
#endif
            var subscriptionContentDecryptor = new HubSubscriptionContentDecryptor();
            var remoteSubscriptionImporter = new RemoteSubscriptionImporter(
                subscriptionStore,
                remoteSubscriptionDownloader,
                contentDecryptor: subscriptionContentDecryptor);
            var subscriptionUpdater = new SubscriptionUpdater(
                subscriptionStore,
                remoteSubscriptionDownloader,
                contentDecryptor: subscriptionContentDecryptor);
            var runtimeStore = new FileRuntimeConfigStore(platformDirectories.RuntimeDirectory);
            var selectedSubscriptionRuntimeGenerator = new SelectedSubscriptionRuntimeGenerator(
                subscriptionStore,
                subscriptionSelectionStore,
                new RuntimeConfigGenerator(new HubOverrideEngine()),
                overrideStore,
                runtimeStore,
                ruleOverrideService: ruleOverrideService);
            var subscriptionDeleter = new SubscriptionDeleter(subscriptionStore, subscriptionSelectionStore, runtimeStore, ruleOverrideStore, proxySelectionStore);
            var providerCatalogLoader = new SelectedSubscriptionProviderCatalogLoader(
                new SubscriptionProviderSnapshotService(subscriptionStore,
                    new FileSubscriptionProviderSnapshotStore(platformDirectories.RuntimeDirectory),
                    new SubscriptionProviderParser()),
                _traySession);
            ISubscriptionProviderUploader subscriptionProviderUploader = new FileSubscriptionProviderUploader(platformDirectories.CoreDirectory);
            ISubscriptionFileOpener subscriptionFileOpener = new DesktopSubscriptionFileOpener(subscriptionStore.GetContentPath);
            IOverrideFileOpener overrideFileOpener = new DesktopOverrideFileOpener(overrideStore.GetContentPath);
            var clipboardWriter = new DesktopClipboardWriter();
            var chainProxyContextLoader = new SubscriptionChainProxyContextLoader(subscriptionStore, new HubOverrideEngine(), overrideStore);
            var subscriptionPage = new SubscriptionPageViewModel(
                subscriptionDeleter,
                localSubscriptionImporter,
                remoteSubscriptionImporter,
                subscriptionUpdater,
                subscriptionStore,
                overrideStore,
                overrideSelectionUpdater,
                clipboardWriter,
                subscriptionFileOpener,
                subscriptionProviderUploader,
                providerCatalogLoader,
                subscriptionSelectionStore,
                runtimeStore,
                localization,
                chainProxyContextLoader.Load);
            var overridePage = new OverridePageViewModel(
                overrideDeleter,
                overrideStore,
                overrideImporter,
                overrideUpdater,
                new FileLocalOverrideFileReader(),
                overrideFileOpener,
                localization);
#if DEBUG
            var pipeProxyCoreClient = new PipeCoreProxyClient(TrayCoreEndpoints.Core);
            IProxyCoreClient proxyCoreClient = new TrayRuntimeProxyCoreClient(new ProxyCoreClient(pipeProxyCoreClient));
            IProxyDelayTester proxyDelayTester = new PipeCoreProxyDelayTester(
                TrayCoreEndpoints.Core,
                () => settings.DelayTestUrl,
                5000);
#else

            IProxyCoreClient proxyCoreClient = new TrayRuntimeProxyCoreClient(new PipeCoreProxyClient(TrayCoreEndpoints.Core));
            IProxyDelayTester proxyDelayTester = new PipeCoreProxyDelayTester(
                TrayCoreEndpoints.Core,
                () => settings.DelayTestUrl,
                5000);
#endif

            var coreManager = new TrayCoreManager();
            var connectionPage = new ConnectionPageViewModel(proxyCoreClient, localization: localization);
            var proxyConfigSource = new FileRuntimeProxyConfigSource(platformDirectories.RuntimeDirectory, subscriptionSelectionStore);
            var proxyConfigParser = new ProxyConfigParser();
            var proxyConfigLoader = new ProxyConfigLoader(
                proxyConfigSource,
                proxyConfigParser);
            var fileRuntimeProxyConfigProvider = new FileRuntimeProxyConfigProvider(proxyConfigLoader);
            var proxySelectionSyncState = new ProxySelectionSyncState();
            var mihomoApiProxyConfigProvider = new MihomoApiProxyConfigProvider(
                proxyCoreClient,
                new FileRuntimeProxyGroupIconProvider(proxyConfigSource, proxyConfigParser));
            var primaryProxyConfigProvider = new StoredProxySelectionConfigProvider(
                mihomoApiProxyConfigProvider,
                proxySelectionStore,
                subscriptionSelectionStore,
                proxySelectionSyncState,
                importCoreSelections: true);
            var fallbackProxyConfigProvider = new StoredProxySelectionConfigProvider(
                fileRuntimeProxyConfigProvider,
                proxySelectionStore,
                subscriptionSelectionStore);
            var proxySelectionRestorer = new ProxySelectionRestorer(
                coreClient: proxyCoreClient,
                coreConfigProvider: mihomoApiProxyConfigProvider,
                selectedRuntimeConfigProvider: fileRuntimeProxyConfigProvider,
                selectionProvider: primaryProxyConfigProvider,
                syncState: proxySelectionSyncState,
                subscriptionSelectionStore: subscriptionSelectionStore);
            var selectionRestoringCoreManager = new ProxySelectionRestoringCoreManager(
                coreManager,
                proxySelectionRestorer);
            var coreUpdater = new MihomoCoreUpdater(
                DesktopApplicationLayout.CoreBinaryPath,
                selectionRestoringCoreManager);
            var proxyPageLayout = Enum.TryParse<ProxyPageLayout>(settings.ProxyPageLayout, ignoreCase: true, out var parsedProxyLayout)
                ? parsedProxyLayout
                : ProxyPageLayout.Horizontal;
            var proxyNodeSortMode = Enum.TryParse<ProxyNodeSortMode>(settings.ProxyNodeSortMode, ignoreCase: true, out var parsedProxyNodeSortMode)
                ? parsedProxyNodeSortMode
                : ProxyNodeSortMode.Default;
            var proxyPage = new ProxyPageViewModel(
                new ProxyDelayService(proxyDelayTester, _traySession),
                proxyCoreClient,
                primaryProxyConfigProvider,
                fallbackProxyConfigProvider,
                localization,
                new ProxySelectionService(proxyCoreClient, proxySelectionStore, subscriptionSelectionStore),
                initialLayout: proxyPageLayout,
                persistLayout: layout =>
                {
                    // 复用共享设置实例，避免后续完整保存丢失这个偏好。
                    settings.ProxyPageLayout = layout.ToString();
                    settingsStore.Save(settings);
                },
                initialSortMode: proxyNodeSortMode,
                persistSortMode: sortMode =>
                {
                    settings.ProxyNodeSortMode = sortMode.ToString();
                    settingsStore.Save(settings);
                },
                isPresentationActive: false);
            var rulePage = new RulePageViewModel(ruleOverrideService, localization);
            var coreLogPage = new CoreLogPageViewModel(localization: localization);
            var dataBackupService = new FileDataBackupService(platformDirectories.AppDataDirectory);
            var webDavBackupStore = new WebDavBackupStore();
            var webDavDataBackupService = new WebDavDataBackupService(dataBackupService, webDavBackupStore);
            var viewModel = new MainWindowViewModel(
                settingsStore,
                localization,
                systemProxyService,
                appBehaviorService,
                globalHotkeyService,
                subscriptionPage,
                overridePage,
                proxyPage,
                connectionPage,
                coreLogPage,
                rulePage,
                dataManagementService: dataBackupService,
                webDavDataBackupService: webDavDataBackupService,
                updateChecker: updateChecker,
                uwpLoopbackService: uwpLoopbackService,
                systemProxyHostDetector: systemProxyHostDetector,
                serviceModeManager: serviceModeManager,
                systemProxyRequestFactory: () => SystemProxyApplicationRequest.Build(settingsStore.Load(), systemProxyPlatform),
                runtimeFallbackGenerator: new SelectedRuntimeFallbackGenerator(
                    subscriptionStore,
                    overrideSelectionUpdater,
                    selectedSubscriptionRuntimeGenerator),
                runtimeStore: runtimeStore,
                coreManager: selectionRestoringCoreManager,
                initialSettings: settings,
                windowEffectCapability: new WindowEffectCapability(),
                networkConnectionProbe: networkConnectionProbe,
                homeProxyClient: proxyCoreClient,
                coreUpdater: coreUpdater,
                processPrivilegeProbe: processPrivilegeProbe,
                systemPlatform: systemProxyPlatform,
                clipboardWriter: clipboardWriter,
                appLogReader: new FileAppLogReader(DesktopApplicationLayout.RunningLogFilePath),
                appLogExporter: new FileAppLogExporter(
                    DesktopApplicationLayout.RunningLogFilePath,
                    DesktopApplicationLayout.TrayRunningLogFilePath));
#if DEBUG
            LogStartupTrace("Main view model created", startupStartedAt);
#endif
            StartHomeRuntimeTimer(viewModel);
            var mainWindow = new MainWindow(settingsStore, settings)
            {
                DataContext = viewModel,
                CanExitToBackground = true,
            };
            _mainWindow = mainWindow;
            clipboardWriter.Attach(mainWindow);
            mainWindow.PrepareShutdownAsync = async () =>
            {
                StopBackgroundServices();
                if (mainWindow.ShouldShutdownTray)
                {
                    await ShutdownTrayAsync();
                }
                await UnregisterTraySessionAsync();
            };
            desktop.MainWindow = mainWindow;
            desktop.Exit += (_, _) =>
            {
                StopBackgroundServices();
                if (_traySession is not null)
                {
                    _traySession.ActivationRequested -= OnTrayActivationRequested;
                    _traySession.ToggleRequested -= OnTrayToggleRequested;
                    _traySession.Disconnected -= OnTrayDisconnected;
                    _traySession.BackgroundChanged -= OnBackgroundChanged;
                }
                _traySession = null;
                _mainWindow = null;
                viewModel.Dispose();
                globalHotkeyService.Dispose();
                coreManager.Dispose();
                DisposeOwnedServices(selectionRestoringCoreManager, proxyCoreClient, proxyDelayTester,
                    webDavBackupStore, systemProxyService, serviceModeManager);
                AppLogger.Info("Desktop UI session closed");
            };
            if (_traySession.IsDisconnected)
            {
                mainWindow.RequestUiShutdown();
            }
#if DEBUG
            DebugCommands.Start(mainWindow);
#endif
            // 首屏出现后再启动重活，保持窗口启动响应。
            Dispatcher.UIThread.Post(
                () =>
                {
#if DEBUG
                    LogStartupTrace("Background startup dispatch entered", startupStartedAt);
#endif
                    _ = InitializePageDataAsync(coreManager, viewModel, proxySelectionRestorer);
                },
                DispatcherPriority.Background);
#if DEBUG
            LogStartupTrace("Framework initialization completed", startupStartedAt);
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task UnregisterTraySessionAsync()
    {
        if (_traySession is null)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await _traySession.UnregisterAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            AppLogger.Warning($"Desktop UI session unregister failed: {exception.Message}");
        }
    }

    private void OnTrayActivationRequested(object? sender, EventArgs args) =>
        Dispatcher.UIThread.Post(ShowMainWindow);

    private void OnTrayToggleRequested(object? sender, EventArgs args) =>
        Dispatcher.UIThread.Post(ToggleMainWindow);

    private void OnTrayDisconnected(object? sender, EventArgs args) =>
        Dispatcher.UIThread.Post(() => _mainWindow?.RequestUiShutdown());

    private void ShowMainWindow()
    {
        if (_mainWindow is not { } mainWindow)
        {
            return;
        }

        mainWindow.Show();
        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }
        mainWindow.Activate();
    }

    private void ToggleMainWindow()
    {
        if (_mainWindow is not { } mainWindow)
        {
            return;
        }

        if (mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized)
        {
            if (mainWindow.CanExitToBackground)
            {
                mainWindow.HideToBackground();
            }
            else
            {
                mainWindow.RequestShutdown();
            }
            return;
        }

        ShowMainWindow();
    }

    private async Task ShutdownTrayAsync()
    {
        if (_traySession is null)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await _traySession.ShutdownTrayAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            AppLogger.Warning($"Tray shutdown request failed: {exception.Message}");
        }
    }

    private void OnBackgroundChanged(object? sender, BackgroundTaskStatus status) =>
        Dispatcher.UIThread.Post(() => _ = ApplyBackgroundStatusAsync(status));

    private async Task ApplyBackgroundStatusAsync(BackgroundTaskStatus status)
    {
        if (_mainWindow?.DataContext is not MainWindowViewModel viewModel || status.Revision <= _backgroundRevision)
        {
            return;
        }

        _backgroundRevision = status.Revision;
        if (status.AppUpdate is { } update && update != _lastAppUpdate)
        {
            _lastAppUpdate = update;
            viewModel.Update.ApplyAutoCheckResult(update);
        }
        if (status.SubscriptionRevision != _subscriptionRevision)
        {
            _subscriptionRevision = status.SubscriptionRevision;
            await viewModel.SubscriptionPage.InitializeAsync();
            await viewModel.ProxyPage.RefreshProxiesAsync();
        }
        if (status.ProviderRevision != _providerRevision)
        {
            _providerRevision = status.ProviderRevision;
            await viewModel.SubscriptionPage.RefreshProviderTrafficAsync();
        }
        viewModel.ProxyPage.ApplyBackgroundDelays(status.DelaySubscriptionId, status.Delays);
    }

    private void StopBackgroundServices()
    {
        StopTimer(ref _homeRuntimeTimer);
    }

    private static void StopTimer(ref DispatcherTimer? timer)
    {
        if (timer is null)
        {
            return;
        }

        timer.Stop();
        timer = null;
    }

    private static void DisposeOwnedServices(params object?[] services)
    {
        foreach (var service in services)
        {
            if (service is not IDisposable disposable)
            {
                continue;
            }

            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                AppLogger.Warning($"Service dispose failed: {exception.Message}");
            }
        }
    }

    private async Task InitializePageDataAsync(
        TrayCoreManager coreManager, MainWindowViewModel viewModel, ProxySelectionRestorer proxySelectionRestorer)
    {
#if DEBUG
        await DebugCommands.DelayPageInitializationAsync();
#endif
        try
        {
            await Task.WhenAll(
                viewModel.SubscriptionPage.InitializeAsync(),
                viewModel.OverridePage.InitializeAsync(),
                StartCoreServicesAsync(coreManager, viewModel, viewModel.ProxyPage, viewModel.RulePage, proxySelectionRestorer));
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Page initialization failed");
        }
    }

    private async Task StartCoreServicesAsync(
        TrayCoreManager coreManager,
        MainWindowViewModel viewModel,
        ProxyPageViewModel proxyPage,
        RulePageViewModel rulePage,
        ProxySelectionRestorer proxySelectionRestorer)
    {
        using var proxyLoading = proxyPage.Loading.BeginLoading();
        using var ruleLoading = rulePage.Loading.BeginLoading();
        using var connectionLoading = viewModel.ConnectionPage.Loading.BeginLoading();
        using var coreLogLoading = viewModel.CoreLogPage.Loading.BeginLoading();
        try
        {
            await coreManager.EnsureReadyAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Core manager startup failed: {exception.Message}");
            viewModel.ShowErrorToast(LocalizationManager.Translate("Common.Error.CoreStartupFailed"));
        }

        viewModel.CompleteInitialCoreLogLoad();
        try
        {
            await viewModel.ConnectionPage.RefreshConnectionsAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Startup connection list load failed: {exception.Message}");
        }

        try
        {
            await proxySelectionRestorer.RestoreCurrentSubscriptionAsync(preserveFixedSelections: true);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Startup proxy selection restore failed: {exception.Message}");
        }

        try
        {
            await proxyPage.RefreshProxiesAsync();
            if (viewModel.SubscriptionPage.CurrentSubscriptionId is { } subscriptionId)
            {
                proxyPage.BindLoadedConfigToSubscription(subscriptionId);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Startup proxy list refresh failed: {exception.Message}");
        }

        RefreshRulesForStartup(rulePage);
        if (_traySession is not null)
        {
            await ApplyBackgroundStatusAsync(await _traySession.GetBackgroundStatusAsync(CancellationToken.None));
        }
    }

    internal static void RefreshRulesForStartup(RulePageViewModel rulePage)
    {
        try
        {
            rulePage.RefreshRulesCommand.Execute(null);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Startup rule list refresh failed: {exception.Message}");
        }
    }

#if DEBUG
    private static void LogStartupTrace(string stage, long startedAt)
    {
        AppLogger.Info($"[StartupTrace] {stage} elapsed={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0.0}ms");
    }
#endif

    private void StartHomeRuntimeTimer(MainWindowViewModel viewModel)
    {
        _homeRuntimeTimer = new DispatcherTimer
        {
            // 主页运行状态每秒刷新；后台页面跳过实时轮询。
            Interval = TimeSpan.FromSeconds(1)
        };
        _homeRuntimeTimer.Tick += (_, _) => viewModel.OnHomeRuntimeTick();
        _homeRuntimeTimer.Start();
        AppLogger.Info("Home runtime refresh started");
    }

    private static SystemProxyPlatform CurrentSystemProxyPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return SystemProxyPlatform.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return SystemProxyPlatform.Linux;
        }

        if (OperatingSystem.IsMacOS())
        {
            return SystemProxyPlatform.MacOS;
        }

        return SystemProxyPlatform.Other;
    }

    private static IAppBehaviorService CreateAppBehaviorService()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsAppBehaviorService(DesktopApplicationLayout.TrayBinaryPath);
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxAppBehaviorService(DesktopApplicationLayout.TrayBinaryPath);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSAppBehaviorService(DesktopApplicationLayout.TrayBinaryPath);
        }

        return new UnsupportedAppBehaviorService();
    }

}
