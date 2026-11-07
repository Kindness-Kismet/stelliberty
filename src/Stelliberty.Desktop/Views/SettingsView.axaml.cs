using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Stelliberty.Desktop.Controls;
using Stelliberty.Desktop.Views.Settings;
using Stelliberty.Presentation.ViewModels;
using NavigationPage = Stelliberty.Presentation.ViewModels.NavigationPage;

namespace Stelliberty.Desktop.Views;

public sealed partial class SettingsView : UserControl
{
    private MainWindowViewModel? _viewModel;
    private SettingsPageViewModel? _settings;
    private readonly PagePointeroverSuppressor _pointeroverSuppressor;
    private readonly Dictionary<SettingsSubPage, Vector> _scrollOffsets = new();
    private readonly Dictionary<SettingsSubPage, Control> _subPageViews = new();
    private SettingsSubPage _currentSubPage;
    private long _subPageAnimationVersion;
    private bool _isAttached;

    public SettingsView()
    {
        InitializeComponent();
        _pointeroverSuppressor = new PagePointeroverSuppressor(SettingsContentPanel, "settings-row");
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_isAttached)
        {
            AttachSettings();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
    {
        base.OnAttachedToVisualTree(args);
        _isAttached = true;
        AttachSettings();
    }

    private void AttachSettings()
    {
        if (_settings is not null)
        {
            _settings.PropertyChanged -= OnSettingsPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        _settings = _viewModel?.Settings;
        if (_settings is not null)
        {
            _currentSubPage = _settings.SubPage;
            _settings.PropertyChanged += OnSettingsPropertyChanged;
            ShowSubPage(_currentSubPage);
        }
    }

    // 子页路由切换时内容区淡入上浮；仅在已处于设置页时播放，
    // 避免与进入设置页的主导航过渡叠加成双重动画。
    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SettingsPageViewModel.SubPage))
        {
            return;
        }

        SaveScrollOffset(_currentSubPage);
        _currentSubPage = _settings?.SubPage ?? SettingsSubPage.Root;
        ShowSubPage(_currentSubPage);
        ScheduleScrollRestore(_currentSubPage);
        if (_viewModel?.CurrentPage == NavigationPage.Settings)
        {
            AnimateSubPageEnter();
        }
    }

    private void SaveScrollOffset(SettingsSubPage page)
        => _scrollOffsets[page] = SettingsScroll.Offset;

    private void ScheduleScrollRestore(SettingsSubPage page)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_settings?.SubPage == page)
                {
                    RestoreScrollOffset(page);
                }
            },
            DispatcherPriority.Render);
    }

    private void RestoreScrollOffset(SettingsSubPage page)
    {
        SettingsScroll.UpdateLayout();
        SettingsScroll.Offset = _scrollOffsets.GetValueOrDefault(page);
        SettingsScroll.UpdateLayout();
    }

    private void ShowSubPage(SettingsSubPage page)
    {
        // 子页按需创建并缓存，避免快速往返时重建整棵视觉树。
        if (!_subPageViews.TryGetValue(page, out var view))
        {
            view = CreateSubPage(page);
            _subPageViews[page] = view;
        }

        SettingsContentPanel.Content = view;
        SettingsContentPanel.UpdateLayout();
    }

    private static Control CreateSubPage(SettingsSubPage page) => page switch
    {
        SettingsSubPage.Root => new SettingsRootView(),
        SettingsSubPage.Theme => new SettingsThemeView(),
        SettingsSubPage.Language => new SettingsLanguageView(),
        SettingsSubPage.ClashFeatures => new SettingsClashFeaturesView(),
        SettingsSubPage.AppBehavior => new SettingsAppBehaviorView(),
        SettingsSubPage.DataManagement => new SettingsDataManagementView(),
        SettingsSubPage.Update => new SettingsUpdateView(),
        SettingsSubPage.About => new SettingsAboutView(),
        SettingsSubPage.AppLog => new SettingsAppLogView(),
        SettingsSubPage.Network => new SettingsNetworkView(),
        SettingsSubPage.PortControl => new SettingsPortControlView(),
        SettingsSubPage.SystemIntegration => new SettingsSystemIntegrationView(),
        SettingsSubPage.Dns => new SettingsDnsView(),
        SettingsSubPage.Performance => new SettingsPerformanceView(),
        SettingsSubPage.CoreLog => new SettingsCoreLogView(),
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, null)
    };

    private void AnimateSubPageEnter()
    {
        var version = ++_subPageAnimationVersion;

        // 切页会按旧鼠标坐标重算命中，首帧禁止继承 hover。
        _pointeroverSuppressor.Begin();

        // 起始态须先无动画落位，再注入过渡才能触发淡入上浮。
        SettingsHeaderText.Transitions = null;
        SettingsHeaderText.Opacity = 0;
        SettingsHeaderText.RenderTransform = PageTransition.HeaderEnterFromTransform;
        SettingsContentPanel.Transitions = null;
        SettingsContentPanel.Opacity = 0;
        SettingsContentPanel.RenderTransform = PageTransition.EnterFromTransform;

        RequestSubPageEnterFrame(version);

        Dispatcher.UIThread.Post(
            () =>
            {
                if (version == _subPageAnimationVersion && TopLevel.GetTopLevel(this) is not null)
                {
                    _pointeroverSuppressor.Apply();
                }
            },
            DispatcherPriority.Background);
    }

    private void RequestSubPageEnterFrame(long version)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            if (version == _subPageAnimationVersion)
            {
                RestoreSubPageVisualState();
            }

            return;
        }

        topLevel.RequestAnimationFrame(
            _ =>
            {
                if (version != _subPageAnimationVersion)
                {
                    return;
                }

                SettingsHeaderText.Transitions = PageTransition.CreateHeaderEnterTransitions();
                SettingsHeaderText.Opacity = 1;
                SettingsHeaderText.RenderTransform = PageTransition.HeaderRestTransform;
                SettingsContentPanel.Transitions = PageTransition.CreateEnterTransitions();
                SettingsContentPanel.Opacity = 1;
                SettingsContentPanel.RenderTransform = PageTransition.RestTransform;
                _pointeroverSuppressor.Apply();
            });
    }

    private void RestoreSubPageVisualState()
    {
        SettingsHeaderText.Transitions = null;
        SettingsHeaderText.Opacity = 1;
        SettingsHeaderText.RenderTransform = PageTransition.HeaderRestTransform;
        SettingsContentPanel.Transitions = null;
        SettingsContentPanel.Opacity = 1;
        SettingsContentPanel.RenderTransform = PageTransition.RestTransform;
        _pointeroverSuppressor.Reset();
    }

    // 页面离开视觉树时解除设置订阅，避免保留已关闭的视图。
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
    {
        SaveScrollOffset(_currentSubPage);
        base.OnDetachedFromVisualTree(args);
        _isAttached = false;
        _subPageAnimationVersion++;
        RestoreSubPageVisualState();
        SettingsContentPanel.Content = null;
        if (_settings is not null)
        {
            _settings.PropertyChanged -= OnSettingsPropertyChanged;
            _settings = null;
            _viewModel = null;
        }
    }
}
