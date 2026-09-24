using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
#if DEBUG
using HotAvalonia;
#endif
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;
using Stelliberty.Desktop.Controls;
using Stelliberty.Desktop.Services;
using Stelliberty.Desktop.Views;
using Stelliberty.Desktop.Views.Dialogs;
using Stelliberty.Presentation.ViewModels;
using AppNavigationPage = Stelliberty.Presentation.ViewModels.NavigationPage;

namespace Stelliberty.Desktop;

public sealed partial class MainWindow : Window
{
    // 隐藏后统一等待该时长：轻量模式结束 UI 进程，常规模式回收页面视觉树。
    private static readonly TimeSpan HiddenReleaseDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PageLoadingMinVisible = TimeSpan.FromMilliseconds(300);
    private readonly WindowAppearanceService _windowAppearanceService = new();
    private readonly WindowStateService _windowStateService;
    private readonly SystemAccentColorService _systemAccentColorService = new();
    private readonly BitmapCache _proxyPageBitmapCache = new() { SnapsToDevicePixels = true };
    private readonly Dictionary<AppNavigationPage, ContentControl> _pageHosts = new();
    private readonly Dictionary<AppNavigationPage, Dictionary<string, Vector>> _pageScrollOffsets = new();
    private readonly DispatcherTimer _hiddenReleaseTimer;
    private IReadOnlyDictionary<SettingsSubPage, Vector>? _settingsScrollOffsets;
    private bool _pageHostsReady;
    private ContentControl? _visiblePageHost;
    private ContentControl? _pendingPageHost;
    private MainWindowViewModel? _attachedViewModel;
    private AccentColorPickerView? _activeAccentPicker;
    private bool _isShutdownRequested;
    private bool _isShutdownPreparing;
    private bool _shouldShutdownTray = true;
    private bool _hasOpened;
    private long _pageTransitionVersion;
    private long _pageLoadingShownAt;
#if DEBUG
    private long _navigationDebugVersion;
    private long _hotReloadRecoveryVersion;
#endif

    public MainWindow()
        : this(null, null)
    {
    }

    public MainWindow(IAppSettingsStore? settingsStore, AppSettings? settings)
    {
        _windowStateService = new WindowStateService(settingsStore, settings);
        _hiddenReleaseTimer = new DispatcherTimer { Interval = HiddenReleaseDelay };
        _hiddenReleaseTimer.Tick += OnHiddenReleaseTimerTick;
        InitializeComponent();
        ApplyPlatformWindowDecorations();
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += OnWindowPropertyChanged;
        Opened += OnOpened;
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        _windowStateService.Attach(this);
        UpdateWindowStateVisuals();
#if DEBUG
        AttachDevBadge();
#endif
    }

    private void ApplyPlatformWindowDecorations()
    {
        if (OperatingSystem.IsMacOS())
        {
            // macOS 保留系统标题栏按钮，避免与自绘窗口控制重复。
            CaptionButtons.IsVisible = false;
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            WindowDecorations = Avalonia.Controls.WindowDecorations.BorderOnly;
        }
    }
#if DEBUG
    private void AttachDevBadge()
    {
        var devBadge = new Border
        {
            Classes = { "debug-badge" },
            Child = new TextBlock
            {
                Classes = { "debug-badge-text" },
                Text = "Dev",
            }
        };
        AutomationProperties.SetAutomationId(devBadge, "TitleBar.DevBadge");
        TitleBarLayout.Children.Insert(0, devBadge);
    }
#endif

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        DetachViewModel();

        if (DataContext is MainWindowViewModel viewModel)
        {
            _attachedViewModel = viewModel;
            _attachedViewModel.Theme.CustomAccentRequested += OnCustomAccentRequested;
            _attachedViewModel.PropertyChanged += OnAttachedViewModelPropertyChanged;
            _windowAppearanceService.Attach(this, viewModel.Theme);
            if (_hasOpened)
            {
                _systemAccentColorService.Attach(this, viewModel.Theme);
            }
            InitializePageHosts();
        }
    }

    private void DetachViewModel()
    {
        if (_attachedViewModel is null)
        {
            return;
        }

        _attachedViewModel.Theme.CustomAccentRequested -= OnCustomAccentRequested;
        _attachedViewModel.PropertyChanged -= OnAttachedViewModelPropertyChanged;
        _windowAppearanceService.Dispose();
        _systemAccentColorService.Dispose();
        _attachedViewModel = null;
    }

    private void OnOpened(object? sender, EventArgs args)
    {
#if DEBUG
        var openedAt = Stopwatch.GetTimestamp();
        AppLogger.Info("[StartupTrace] Main window opened");
        Dispatcher.UIThread.Post(
            () => AppLogger.Info($"[StartupTrace] Main window first background turn elapsed={Stopwatch.GetElapsedTime(openedAt).TotalMilliseconds:0.0}ms"),
            DispatcherPriority.Background);
#endif
        _hasOpened = true;
        if (DataContext is MainWindowViewModel viewModel)
        {
            _systemAccentColorService.Attach(this, viewModel.Theme);
            if (_visiblePageHost is null)
            {
                ShowInitialPage(viewModel.CurrentPage);
            }
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled
            || args.Key != Key.Escape
            || args.KeyModifiers != KeyModifiers.None
            || DialogHost.IsOpen
            || UpdateDialogHost.IsOpen
            || DataContext is not MainWindowViewModel viewModel
            || viewModel.SubscriptionPage.Provider.IsProviderSelectorDialogVisible
            || viewModel.CurrentPage != AppNavigationPage.Settings
            || !viewModel.Settings.IsBackVisible)
        {
            return;
        }

        if (viewModel.Settings.BackCommand.CanExecute(null))
        {
            viewModel.Settings.BackCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnAttachedViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (args.PropertyName == nameof(MainWindowViewModel.IsCurrentPageLoading))
        {
            if (!IsVisible) return;
            if (_pendingPageHost is { } pending)
            {
                if (pending.Content is not null)
                {
                    CompletePageLoadingThenEnter(_visiblePageHost, pending, _pageTransitionVersion);
                }
            }
            else if (viewModel.IsCurrentPageLoading && _visiblePageHost is not null)
            {
                AnimatePageTransition(viewModel.CurrentPage);
            }
            return;
        }
        if (args.PropertyName != nameof(MainWindowViewModel.CurrentPage)) return;

        AnimatePageTransition(viewModel.CurrentPage);
#if DEBUG
        ScheduleNavigationDebugLog(viewModel.CurrentPage);
#endif
    }

    // 将页面映射到持久宿主；XAML 按导航顺序堆叠宿主。
    private void InitializePageHosts()
    {
        if (_attachedViewModel is null)
        {
            return;
        }

        if (!_pageHostsReady)
        {
            _pageHosts[AppNavigationPage.Home] = HomePageHost;
            _pageHosts[AppNavigationPage.Proxy] = ProxyPageHost;
            _pageHosts[AppNavigationPage.Connections] = ConnectionsPageHost;
            _pageHosts[AppNavigationPage.CoreLogs] = CoreLogsPageHost;
            _pageHosts[AppNavigationPage.Rules] = RulesPageHost;
            _pageHosts[AppNavigationPage.Subscriptions] = SubscriptionsPageHost;
            _pageHosts[AppNavigationPage.Overrides] = OverridesPageHost;
            _pageHosts[AppNavigationPage.Settings] = SettingsPageHost;
            // 代理页作为单层纹理参与整页变换，避免动画期间重复绘制大视觉树。
            ProxyPageHost.CacheMode = _proxyPageBitmapCache;
            _pageHostsReady = true;
        }

        if (IsVisible)
        {
            ShowInitialPage(_attachedViewModel.CurrentPage);
        }
    }

    private void EnsurePageLoaded(AppNavigationPage page)
    {
        if (!_pageHostsReady
            || !_pageHosts.TryGetValue(page, out var host)
            || host.Content is not null
            || !TryGetPageConverter(out var converter))
        {
            return;
        }

        host.Content = converter.GetOrCreateView(page);
    }

    private void SetPageLoadingVisible(bool isVisible)
    {
        if (isVisible)
        {
            if (_pageLoadingShownAt != 0) return;
            _pageLoadingShownAt = Stopwatch.GetTimestamp();
            PageLoadingIndicator.Start();
            PageLoadingOverlay.Opacity = 1;
            PageLoadingOverlay.IsHitTestVisible = true;
            return;
        }

        PageLoadingOverlay.Opacity = 0;
        PageLoadingOverlay.IsHitTestVisible = false;
        PageLoadingIndicator.Stop();
        _pageLoadingShownAt = 0;
    }

    // 首页直接显示无动画；其余宿主复位到隐藏的下浮起始态。
    private void ShowInitialPage(AppNavigationPage page)
    {
        if (!_pageHosts.TryGetValue(page, out var host))
        {
            return;
        }

        CancelPendingPageTransition();
        foreach (var other in _pageHosts.Values)
        {
            DeactivatePageHost(other);
            other.Transitions = null;
            other.ZIndex = 0;
            other.IsHitTestVisible = false;
            other.Opacity = 0;
            other.RenderTransform = PageTransition.EnterFromTransform;
            // 非当前页退出布局，避免多棵视觉树持续参与 Measure。
            other.IsVisible = false;
        }

        _visiblePageHost = null;
        if (_attachedViewModel?.IsCurrentPageLoading == true)
        {
            AnimatePageTransition(page);
            return;
        }

        EnsurePageLoaded(page);
        host.IsVisible = true;
        ShowPageHost(host);
        _visiblePageHost = host;
        ActivatePageHost(host);
        PreparePageLayout(page, host);
    }

    private void AnimatePageTransition(AppNavigationPage page)
    {
        if (!IsVisible || !_pageHostsReady || !_pageHosts.TryGetValue(page, out var nextHost))
        {
            return;
        }

        var isDataLoading = _attachedViewModel?.IsCurrentPageLoading == true;
        if (ReferenceEquals(nextHost, _visiblePageHost) && nextHost.Content is not null && !isDataLoading)
        {
            CancelPendingPageTransition();
            ShowPageHost(nextHost);
            ActivatePageHost(nextHost);
            return;
        }

        CancelPendingPageTransition();
        var previousHost = _visiblePageHost;
        var version = ++_pageTransitionVersion;
        _pendingPageHost = nextHost;
        if (previousHost is not null)
        {
            previousHost.IsHitTestVisible = false;
        }

        if (nextHost.Content is not null && !isDataLoading)
        {
            // 缓存页已有视觉树，直接启动过渡，避免等待空闲合成器唤醒。
            PrepareNextHostEnterState(nextHost);
            ActivatePageHost(nextHost);
            StartPageTransition(previousHost, nextHost, version);
            return;
        }

        // 先让加载状态进入渲染队列，再在后台优先级创建页面。
        if (previousHost is not null)
        {
            DeactivatePageHost(previousHost);
            previousHost.Transitions = null;
            previousHost.Opacity = 0;
            previousHost.IsVisible = false;
            previousHost.IsHitTestVisible = false;
            previousHost.ZIndex = 0;
        }

        SetPageLoadingVisible(true);
        if (nextHost.Content is not null)
        {
            // 缓存页立即准备显示状态，避免数据就绪通知先于后台布局回调。
            PrepareNextHostEnterState(nextHost);
            ActivatePageHost(nextHost);
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (version != _pageTransitionVersion || !ReferenceEquals(_pendingPageHost, nextHost))
                {
                    return;
                }

                EnsurePageLoaded(page);
                PrepareNextHostEnterState(nextHost);
                ActivatePageHost(nextHost);
                CompletePageLoadingThenEnter(previousHost, nextHost, version);
            },
            DispatcherPriority.Background);
    }

    private static void PrepareNextHostEnterState(ContentControl nextHost)
    {
        nextHost.IsVisible = true;
        nextHost.Transitions = null;
        nextHost.ZIndex = 1;
        nextHost.IsHitTestVisible = false;
        nextHost.Opacity = 0;
        nextHost.RenderTransform = PageTransition.EnterFromTransform;
    }

    private void CompletePageLoadingThenEnter(
        ContentControl? previousHost,
        ContentControl nextHost,
        long version)
    {
        if (version != _pageTransitionVersion || !ReferenceEquals(_pendingPageHost, nextHost))
        {
            return;
        }

        // 视觉树创建与业务加载分别就绪，数据未返回时保持遮罩。
        if (_attachedViewModel?.IsCurrentPageLoading == true) return;

        var remaining = PageLoadingMinVisible - Stopwatch.GetElapsedTime(_pageLoadingShownAt);
        if (remaining <= TimeSpan.Zero)
        {
            StartPageTransition(previousHost, nextHost, version);
            return;
        }

        var timer = new DispatcherTimer { Interval = remaining };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CompletePageLoadingThenEnter(previousHost, nextHost, version);
        };
        timer.Start();
    }

    private void StartPageTransition(ContentControl? previousHost, ContentControl nextHost, long version)
    {
        if (version != _pageTransitionVersion || !ReferenceEquals(_pendingPageHost, nextHost))
        {
            return;
        }

        PreparePageLayout(_attachedViewModel!.CurrentPage, nextHost);
        SetPageLoadingVisible(false);
        _pendingPageHost = null;
        _visiblePageHost = nextHost;
        if (previousHost is not null
            && !ReferenceEquals(previousHost, nextHost)
            && previousHost.IsVisible)
        {
            DeactivatePageHost(previousHost);
            previousHost.Transitions = PageTransition.CreateLeaveTransitions();
            previousHost.ZIndex = 0;
            previousHost.IsHitTestVisible = false;
            previousHost.Opacity = 0;
            previousHost.RenderTransform = PageTransition.LeaveToTransform;
            ScheduleHidePageHost(previousHost);
        }
        else if (previousHost is not null && !ReferenceEquals(previousHost, nextHost))
        {
            HidePageHost(previousHost);
        }

        nextHost.Transitions = PageTransition.CreateEnterTransitions();
        nextHost.IsHitTestVisible = true;
        nextHost.Opacity = 1;
        nextHost.RenderTransform = PageTransition.RestTransform;
    }

    private void CancelPendingPageTransition()
    {
        _pageTransitionVersion++;
        SetPageLoadingVisible(false);
        if (_pendingPageHost is not { } pendingHost)
        {
            return;
        }

        DeactivatePageHost(pendingHost);
        HidePageHost(pendingHost);
        _pendingPageHost = null;
    }

    // 离场动画结束后再踢出布局，避免快切时误藏当前页。
    private void ScheduleHidePageHost(ContentControl host)
    {
        var timer = new DispatcherTimer { Interval = PageTransition.LeaveDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ReferenceEquals(host, _visiblePageHost)
                || ReferenceEquals(host, _pendingPageHost))
            {
                return;
            }

            HidePageHost(host);
        };
        timer.Start();
    }

    private static void HidePageHost(ContentControl host)
    {
        host.Transitions = null;
        host.ZIndex = 0;
        host.IsHitTestVisible = false;
        host.Opacity = 0;
        host.RenderTransform = PageTransition.EnterFromTransform;
        host.IsVisible = false;
    }

    private static void ShowPageHost(ContentControl host)
    {
        host.Transitions = null;
        host.ZIndex = 1;
        host.IsHitTestVisible = true;
        host.Opacity = 1;
        host.RenderTransform = PageTransition.RestTransform;
        host.IsVisible = true;
    }

    private static void ActivatePageHost(ContentControl host)
    {
        if (host.Content is IPageContentLifecycle lifecycle)
        {
            lifecycle.ActivatePageContent();
        }
    }

    private static void DeactivatePageHost(ContentControl? host)
    {
        if (host?.Content is IPageContentLifecycle lifecycle)
        {
            lifecycle.DeactivatePageContent();
        }
    }

    private bool TryGetPageConverter(out PageToViewConverter converter)
    {
        if (TryGetResource("PageToView", ActualThemeVariant, out var resource)
            && resource is PageToViewConverter pageToView)
        {
            converter = pageToView;
            return true;
        }

        converter = null!;
        return false;
    }

#if DEBUG
    internal object CapturePageLoadingState()
    {
        var viewModel = _attachedViewModel!;
        var host = _pageHosts[viewModel.CurrentPage];
        return new
        {
            Page = viewModel.CurrentPage.ToString(),
            IsDataLoading = viewModel.IsCurrentPageLoading,
            IsOverlayVisible = IsVisible && PageLoadingOverlay.Opacity > 0,
            IsContentVisible = IsVisible && host.IsVisible && host.Opacity > 0,
            Pages = Enum.GetValues<AppNavigationPage>().ToDictionary(
                page => page.ToString(), page => viewModel.GetPageLoadingState(page)?.IsLoading == true),
        };
    }

    internal Task WaitForPageReadyAsync(AppNavigationPage page)
    {
        if (!_pageHosts.TryGetValue(page, out var host))
        {
            return Task.FromException(new InvalidOperationException($"Page host is not available: {page}"));
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WaitForPageReadyOnNextFrame(page, host, completion);
        return completion.Task;
    }

    // 数据就绪且页面开始显示后才响应，避免查询抢占首次加载或同页刷新。
    private void WaitForPageReadyOnNextFrame(
        AppNavigationPage page,
        ContentControl host,
        TaskCompletionSource completion)
    {
        RequestAnimationFrame(
            _ =>
            {
                if (_attachedViewModel?.CurrentPage != page)
                {
                    completion.TrySetException(new InvalidOperationException($"Navigation was superseded: {page}"));
                    return;
                }

                if (ReferenceEquals(_visiblePageHost, host)
                    && _pendingPageHost is null
                    && !_attachedViewModel.IsCurrentPageLoading
                    && host.Content is Control
                    && host.IsVisible
                    && host.Opacity > 0)
                {
                    completion.TrySetResult();
                    return;
                }

                WaitForPageReadyOnNextFrame(page, host, completion);
            });
    }

    [AvaloniaHotReload]
    private void OnHotReloaded()
    {
        ScheduleHotReloadRecovery();
    }

    private void ScheduleHotReloadRecovery()
    {
        var version = System.Threading.Interlocked.Increment(ref _hotReloadRecoveryVersion);
        Dispatcher.UIThread.Post(
            () => Dispatcher.UIThread.Post(
                () => ApplyHotReloadRecovery(version),
                DispatcherPriority.Background),
            DispatcherPriority.Render);
    }

    private void ApplyHotReloadRecovery(long version)
    {
        if (version != System.Threading.Volatile.Read(ref _hotReloadRecoveryVersion))
        {
            return;
        }

        try
        {
            _windowAppearanceService.Reapply();
            _systemAccentColorService.Reapply();
            RebuildNavigationViewsAfterHotReload();
            AppLogger.Info("Hot reload recovered theme, accent color, and page view cache");
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Hot reload recovery failed");
        }
    }

    private void RebuildNavigationViewsAfterHotReload()
    {
        if (_attachedViewModel is null || !TryGetPageConverter(out var converter))
        {
            return;
        }

        // 热重载会替换视觉树和资源。
        // 宿主与缓存视图必须重新绑定，避免继续引用旧资源。
        ClearPageHostContents();
        converter.ClearCache();
        SetPageLoadingVisible(false);
        CancelPendingPageTransition();
        _pageHostsReady = false;
        _pageHosts.Clear();
        _visiblePageHost = null;
        InitializePageHosts();
    }

    private void ScheduleNavigationDebugLog(AppNavigationPage page)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var version = ++_navigationDebugVersion;
        Dispatcher.UIThread.Post(
            () => Dispatcher.UIThread.Post(
                () => LogNavigationDebug(page, startedAt, version),
                DispatcherPriority.Background),
            DispatcherPriority.Render);
    }

    private void LogNavigationDebug(AppNavigationPage page, long startedAt, long version)
    {
        if (version != _navigationDebugVersion
            || _attachedViewModel is null
            || _attachedViewModel.CurrentPage != page)
        {
            return;
        }

        // 首次创建走 Background，可能比本日志队列更晚完成。
        if (GetNavigationPageView(page) is null && PageLoadingOverlay.Opacity > 0)
        {
            Dispatcher.UIThread.Post(
                () => LogNavigationDebug(page, startedAt, version),
                DispatcherPriority.Background);
            return;
        }

        var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var controls = CountPageControls(GetNavigationPageView(page));
        AppLogger.Info($"Navigation complete: page={FormatPageDebugName(page)} elapsed={elapsedMs:0.0}ms controls={controls.Total} visible={controls.Visible} automation={controls.Automation} {BuildPageDebugState(page, _attachedViewModel)}");
    }

    private static (int Total, int Visible, int Automation) CountPageControls(Control? page)
    {
        if (page is not null)
        {
            var controls = page.GetVisualDescendants().OfType<Control>().Append(page).ToList();
            return (
                controls.Count,
                controls.Count(IsControlEffectivelyVisible),
                controls.Count(control => !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(control))));
        }

        return (0, 0, 0);
    }

    private Control? GetNavigationPageView(AppNavigationPage page)
        => _pageHosts.TryGetValue(page, out var host) ? host.Content as Control : null;

    private static string BuildPageDebugState(AppNavigationPage page, MainWindowViewModel viewModel)
    {
        return page switch
        {
            AppNavigationPage.Proxy => $"groups={viewModel.ProxyPage.VisibleGroups.Count} nodes={viewModel.ProxyPage.VisibleNodeRows.Count} realized={viewModel.ProxyPage.RealizedNodeRowCount} parsed_nodes={viewModel.ProxyPage.ParsedNodeCount ?? 0}",
            AppNavigationPage.Connections => $"connections={viewModel.ConnectionPage.TotalConnectionCount} filtered={viewModel.ConnectionPage.FilteredConnectionCount}",
            AppNavigationPage.CoreLogs => $"core_logs={viewModel.CoreLogPage.TotalLogCount} filtered={viewModel.CoreLogPage.FilteredLogCount}",
            AppNavigationPage.Rules => $"rules={viewModel.RulePage.Rules.Count} filtered={viewModel.RulePage.FilteredRules.Count}",
            AppNavigationPage.Subscriptions => $"subscriptions={viewModel.SubscriptionPage.TotalSubscriptionCount} current={viewModel.SubscriptionPage.CurrentSubscriptionId ?? string.Empty}",
            AppNavigationPage.Overrides => $"overrides={viewModel.OverridePage.Overrides.Count}",
            _ => string.Empty
        };
    }

    private static string FormatPageDebugName(AppNavigationPage page) => page.ToString();

    private static bool IsControlEffectivelyVisible(Control control)
    {
        for (var current = control; current is not null; current = current.GetVisualParent<Control>())
        {
            if (!current.IsVisible)
            {
                return false;
            }
        }

        return true;
    }
#endif

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (args.ClickCount == 2)
        {
            ToggleWindowMaximized();
            return;
        }

        BeginMoveDrag(args);
    }

    private void OnMinimizeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        ToggleWindowMaximized();
    }

    private void OnCloseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        Close();
    }

    private void ToggleWindowMaximized()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateWindowStateVisuals();
    }

    private void UpdateWindowStateVisuals()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeIcon.IsVisible = !isMaximized;
        RestoreIcon.IsVisible = isMaximized;
    }

    public void RequestShutdown()
    {
        BeginShutdown(shouldShutdownTray: true);
    }

    internal void RequestUiShutdown()
    {
        BeginShutdown(shouldShutdownTray: false);
    }

    internal void HideToBackground()
    {
        if (_isShutdownRequested || _isShutdownPreparing)
        {
            return;
        }

        _windowStateService.SaveNow();
        Hide();
    }

    private void BeginShutdown(bool shouldShutdownTray)
    {
        if (_isShutdownRequested || _isShutdownPreparing)
        {
            return;
        }

        _shouldShutdownTray = shouldShutdownTray;
        AppLogger.Info(shouldShutdownTray ? "Application exit requested" : "Desktop UI exit requested");
        _isShutdownPreparing = true;
        _ = PrepareAndShutdownAsync();
    }

    internal Func<Task>? PrepareShutdownAsync { private get; set; }

    internal Action? OsShutdownDetected { private get; set; }

    internal bool IsShutdownPreparing => _isShutdownPreparing;

    internal bool ShouldShutdownTray => _shouldShutdownTray;

    internal bool CanExitToBackground { get; set; }

    protected override void OnClosing(WindowClosingEventArgs args)
    {
        _windowStateService.SaveNow();
        if (args.CloseReason == WindowCloseReason.OSShutdown)
        {
            OsShutdownDetected?.Invoke();
            AppLogger.Info("OS window close accepted without cancellation");
            base.OnClosing(args);
            return;
        }

        if (!_isShutdownRequested)
        {
            args.Cancel = true;
            if (DataContext is MainWindowViewModel { AppBehavior.IsMinimizeToTrayEnabled: true }
                && CanExitToBackground)
            {
                HideToBackground();
            }
            else
            {
                RequestShutdown();
            }
            return;
        }

        base.OnClosing(args);
    }

    private async Task PrepareAndShutdownAsync()
    {
        var scope = _shouldShutdownTray ? "Application" : "Desktop UI";
        AppLogger.Info($"{scope} exit preparation started");
        try
        {
            if (PrepareShutdownAsync is not null)
            {
                await PrepareShutdownAsync();
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Application exit preparation failed: {exception.Message}");
        }

        AppLogger.Info($"{scope} exit preparation completed");
        var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var requiresExplicitShutdown = desktop?.ShutdownMode == ShutdownMode.OnExplicitShutdown;
        _isShutdownRequested = true;
        Close();
        if (requiresExplicitShutdown)
        {
            desktop?.TryShutdown(0);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        SetPageLoadingVisible(false);
        CancelPendingPageTransition();
        _hiddenReleaseTimer.Stop();
        _hiddenReleaseTimer.Tick -= OnHiddenReleaseTimerTick;
        CloseAccentPicker();
        _visiblePageHost = null;
        ClearPageHostContents();
        if (TryGetPageConverter(out var converter))
        {
            converter.ClearCache();
        }

        DetachViewModel();
        _windowStateService.Dispose();
        DataContextChanged -= OnDataContextChanged;
        PropertyChanged -= OnWindowPropertyChanged;
        Opened -= OnOpened;
        base.OnClosed(e);
    }

    private void ClearPageHostContents()
    {
        foreach (var host in _pageHosts.Values)
        {
            if (host.Content is Control pageContent)
            {
                if (pageContent is IPageContentLifecycle lifecycle)
                {
                    lifecycle.ReleasePageContent();
                }

                pageContent.DataContext = null;
            }

            host.Content = null;
        }
    }

    // 隐藏回收前记录滚动位置，重建后按 AutomationId 还原。
    private void CapturePageScrollOffsets()
    {
        foreach (var (page, host) in _pageHosts)
        {
            if (page == AppNavigationPage.Settings || host.Content is not Control content)
            {
                continue;
            }

            // 尚未重建的页面保留上次快照，已重建的页面以当前状态覆盖。
            _pageScrollOffsets.Remove(page);
            var offsets = content.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .Select(scrollViewer => (Id: AutomationProperties.GetAutomationId(scrollViewer), scrollViewer.Offset))
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && (item.Offset.X > 0 || item.Offset.Y > 0))
                .ToDictionary(item => item.Id!, item => item.Offset, StringComparer.Ordinal);
            if (offsets.Count > 0)
            {
                _pageScrollOffsets[page] = offsets;
            }
        }

        if (_pageHosts.TryGetValue(AppNavigationPage.Settings, out var settingsHost)
            && settingsHost.Content is SettingsView settingsView)
        {
            _settingsScrollOffsets = settingsView.CaptureScrollOffsets();
        }
    }

    private void PreparePageLayout(AppNavigationPage page, ContentControl host)
    {
        if (page == AppNavigationPage.Settings && host.Content is SettingsView settingsView)
        {
            if (_settingsScrollOffsets is null)
            {
                return;
            }

            host.UpdateLayout();
            settingsView.RestoreScrollOffsets(_settingsScrollOffsets);
            _settingsScrollOffsets = null;
            return;
        }

        if (!_pageScrollOffsets.Remove(page, out var offsets)
            || host.Content is not Control content)
        {
            return;
        }

        host.UpdateLayout();
        var restoredCount = 0;
        foreach (var scrollViewer in content.GetVisualDescendants().OfType<ScrollViewer>())
        {
            var automationId = AutomationProperties.GetAutomationId(scrollViewer);
            if (automationId is null || !offsets.TryGetValue(automationId, out var offset))
            {
                continue;
            }

            scrollViewer.Offset = offset;
            restoredCount++;
        }

        if (restoredCount > 0)
        {
            host.UpdateLayout();
        }
    }

    private void OnHiddenReleaseTimerTick(object? sender, EventArgs args)
    {
        _hiddenReleaseTimer.Stop();
        if (IsVisible || _isShutdownRequested || _isShutdownPreparing)
        {
            return;
        }

        if (DataContext is MainWindowViewModel { AppBehavior.IsLightweightModeEnabled: true })
        {
            RequestUiShutdown();
            return;
        }

        ReleaseHiddenPageViews();
    }

    private void ReleaseHiddenPageViews()
    {
        CapturePageScrollOffsets();
        _visiblePageHost = null;
        ClearPageHostContents();
        var releasedViewCount = 0;
        if (TryGetPageConverter(out var converter))
        {
            releasedViewCount = converter.ClearCache();
        }

        if (releasedViewCount > 0)
        {
            AppLogger.Debug($"Released {releasedViewCount} hidden page views");
        }

        Dispatcher.UIThread.Post(CollectHiddenMemory, DispatcherPriority.Background);
    }

    private void CollectHiddenMemory()
    {
        if (IsVisible || _isShutdownRequested || _isShutdownPreparing)
        {
            return;
        }

        // 等视觉树解除引用后再压缩托管堆，窗口重新显示时跳过。
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == WindowStateProperty)
        {
            UpdateWindowStateVisuals();
        }

        if (args.Property != Visual.IsVisibleProperty)
        {
            return;
        }

        if (IsVisible)
        {
            _hiddenReleaseTimer.Stop();
            if (_attachedViewModel is not null)
            {
                // 隐藏可能中断首次加载，恢复时按当前导航重建完整显示状态。
                ShowInitialPage(_attachedViewModel.CurrentPage);
            }

            return;
        }

        if (_isShutdownRequested || _isShutdownPreparing)
        {
            return;
        }

        CancelPendingPageTransition();
        DeactivatePageHost(_visiblePageHost);
        _hiddenReleaseTimer.Stop();
        _hiddenReleaseTimer.Start();
    }

    private void OnCustomAccentRequested(object? sender, EventArgs args)
    {
        if (_attachedViewModel is null)
        {
            return;
        }

        _activeAccentPicker = new AccentColorPickerView
        {
            DataContext = _attachedViewModel,
            InitialColor = Color.Parse(_attachedViewModel.Theme.CustomAccentColor)
        };
        _activeAccentPicker.Confirmed += OnAccentPickerConfirmed;
        _activeAccentPicker.Cancelled += OnAccentPickerCancelled;

        DialogHost.Show(new DialogPanel { DialogContent = _activeAccentPicker });
    }

    private void OnOpenUpdateReleaseClicked(object? sender, RoutedEventArgs args)
    {
        try
        {
            if (sender is not Button { Tag: string url } || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"External link open failed: {exception.Message}");
        }
    }

    private void OnAccentPickerConfirmed(object? sender, EventArgs args)
    {
        if (DataContext is MainWindowViewModel viewModel && _activeAccentPicker is not null)
        {
            var color = _activeAccentPicker.SelectedColor;
            viewModel.Theme.ConfirmCustomAccentColor($"#{color.R:X2}{color.G:X2}{color.B:X2}");
        }

        CloseAccentPicker();
    }

    private void OnAccentPickerCancelled(object? sender, EventArgs args)
    {

        CloseAccentPicker();
    }

    private void CloseAccentPicker()
    {
        if (_activeAccentPicker is not null)
        {
            _activeAccentPicker.Confirmed -= OnAccentPickerConfirmed;
            _activeAccentPicker.Cancelled -= OnAccentPickerCancelled;
            _activeAccentPicker = null;
        }

        DialogHost.Close();
    }
}
