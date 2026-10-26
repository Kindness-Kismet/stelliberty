#if DEBUG
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Settings;
using Stelliberty.Presentation.ViewModels;
using AppNavigationPage = Stelliberty.Presentation.ViewModels.NavigationPage;

namespace Stelliberty.Desktop.Debug;

internal sealed class WindowAppearanceProbe
{
    private readonly MainWindow _window;
    private readonly Compositor _compositor;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly Dictionary<string, PageSamples> _pages = new();
    private Snapshot? _lastSnapshot;
    private string? _summary;
    private int _samples;
    private int _invalidSamples;
    private int _propertyChanges;
    private int _invalidPropertyChanges;
    private double _lastSampleMs;
    private double _maxSampleGapMs;

    public WindowAppearanceProbe(MainWindow window, int durationMs)
    {
        if (((MainWindowViewModel)window.DataContext!).Theme.SelectedWindowEffect != WindowEffect.Acrylic)
        {
            throw new InvalidOperationException("Select Acrylic before starting the appearance probe");
        }

        _window = window;
        _compositor = ElementComposition.GetElementVisual(window)!.Compositor;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        _timer.Tick += (_, _) => Stop("elapsed");
        _window.PropertyChanged += OnWindowPropertyChanged;
        _window.Closed += OnWindowClosed;
        AppLogger.Info($"Window appearance probe started: duration_ms={durationMs} {JsonSerializer.Serialize(Capture(window))}");
        _timer.Start();
        _compositor.RequestCompositionUpdate(OnCompositionUpdate);
    }

    public static Snapshot Capture(MainWindow window)
    {
        var viewModel = (MainWindowViewModel)window.DataContext!;
        var page = viewModel.CurrentPage.ToString();
        if (viewModel.CurrentPage == AppNavigationPage.Settings)
        {
            page += "/" + viewModel.Settings.SubPage;
        }

        window.TryGetResource("AppSurfaceBrush", window.ActualThemeVariant, out var surface);
        bool? nativeCompositionEnabled = null;
        int? nativeResult = null;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            // 宿主背景标志仅支持设置；原生状态使用可查询的 DWM 合成开关。
            nativeResult = DwmIsCompositionEnabled(out var enabled);
            nativeCompositionEnabled = enabled != 0;
        }

        return new Snapshot(
            page,
            window.ActualThemeVariant.ToString(),
            viewModel.Theme.SelectedWindowEffect.ToString(),
            window.ActualTransparencyLevel.ToString(),
            (window.Background as ISolidColorBrush)?.Color.ToString(),
            GetOpacity(window.Background),
            GetOpacity(surface as IBrush),
            window.Opacity,
            nativeCompositionEnabled,
            nativeResult);
    }

    public string Stop(string reason = "requested")
    {
        if (_summary is not null)
        {
            return _summary;
        }

        _timer.Stop();
        _window.PropertyChanged -= OnWindowPropertyChanged;
        _window.Closed -= OnWindowClosed;
        _summary = JsonSerializer.Serialize(new
        {
            Reason = reason,
            ElapsedMs = Math.Round(_stopwatch.Elapsed.TotalMilliseconds, 1),
            Samples = _samples,
            InvalidSamples = _invalidSamples,
            PropertyChanges = _propertyChanges,
            InvalidPropertyChanges = _invalidPropertyChanges,
            MaxSampleGapMs = Math.Round(_maxSampleGapMs, 1),
            Pages = _pages.Values.ToArray()
        });
        AppLogger.Info($"Window appearance probe completed: {_summary}");
        return _summary;
    }

    private void OnCompositionUpdate()
    {
        if (_summary is not null)
        {
            return;
        }

        var elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;
        _maxSampleGapMs = Math.Max(_maxSampleGapMs, elapsedMs - _lastSampleMs);
        _lastSampleMs = elapsedMs;
        var snapshot = Capture(_window);
        var key = snapshot.Theme + "/" + snapshot.Page;
        if (!_pages.TryGetValue(key, out var page))
        {
            page = new PageSamples { Page = snapshot.Page, Theme = snapshot.Theme };
            _pages.Add(key, page);
        }

        _samples++;
        page.Samples++;
        if (!snapshot.IsAcrylicVisible)
        {
            _invalidSamples++;
            page.InvalidSamples++;
        }

        if (snapshot != _lastSnapshot)
        {
            LogSnapshot("frame", snapshot);
            _lastSnapshot = snapshot;
        }

        // 只在探测期间按合成器更新采样，正常运行不持续唤醒渲染。
        _compositor.RequestCompositionUpdate(OnCompositionUpdate);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property != TopLevel.ActualTransparencyLevelProperty
            && args.Property != TopLevel.TransparencyLevelHintProperty
            && args.Property != TemplatedControl.BackgroundProperty
            && args.Property != Visual.OpacityProperty)
        {
            return;
        }

        // 属性事件补足同一帧内先失效再恢复的情况。
        _propertyChanges++;
        var snapshot = Capture(_window);
        if (!snapshot.IsAcrylicVisible)
        {
            _invalidPropertyChanges++;
        }
        LogSnapshot(args.Property.Name, snapshot);
    }

    private void OnWindowClosed(object? sender, EventArgs args) => Stop("closed");

    private static void LogSnapshot(string source, Snapshot snapshot)
    {
        var message = $"Window appearance sample: trigger={source} {JsonSerializer.Serialize(snapshot)}";
        if (snapshot.IsAcrylicVisible)
        {
            AppLogger.Info(message);
        }
        else
        {
            AppLogger.Warning(message);
        }
    }

    private static double? GetOpacity(IBrush? brush) => brush is ISolidColorBrush solid
        ? solid.Color.A / 255.0 * solid.Opacity
        : null;

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmIsCompositionEnabled(out int enabled);

    internal sealed record Snapshot(
        string Page,
        string Theme,
        string Requested,
        string Actual,
        string? RootColor,
        double? RootOpacity,
        double? SurfaceOpacity,
        double WindowOpacity,
        bool? NativeCompositionEnabled,
        int? NativeResult)
    {
        [JsonIgnore]
        public bool IsAcrylicVisible => Requested == nameof(WindowEffect.Acrylic)
            && Actual == nameof(WindowTransparencyLevel.AcrylicBlur)
            && RootOpacity is >= 0 and < 1
            && SurfaceOpacity is >= 0 and < 1
            && WindowOpacity == 1
            && (NativeResult is null || NativeResult == 0 && NativeCompositionEnabled == true);
    }

    private sealed class PageSamples
    {
        public required string Page { get; init; }
        public required string Theme { get; init; }
        public int Samples { get; set; }
        public int InvalidSamples { get; set; }
    }
}
#endif
