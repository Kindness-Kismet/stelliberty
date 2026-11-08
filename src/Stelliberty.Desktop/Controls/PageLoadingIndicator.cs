using System;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;

namespace Stelliberty.Desktop.Controls;

public sealed class PageLoadingIndicator : Panel
{
    private static readonly TimeSpan CycleDuration = TimeSpan.FromMilliseconds(1200);
    private const double BarHeight = 6d;
    private const double ThumbWidthRatio = 0.34d;

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<PageLoadingIndicator, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<PageLoadingIndicator, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> SurfaceBrushProperty =
        AvaloniaProperty.Register<PageLoadingIndicator, IBrush?>(nameof(SurfaceBrush));

    private readonly Border _surface;
    private readonly Border _track;
    private readonly Border _thumb;
    private readonly RectangleGeometry _barClip = new() { RadiusX = 3, RadiusY = 3 };
    private double _animationDistance;
    private bool _isAttached;
    private bool _isRunning;

    public PageLoadingIndicator()
    {
        _surface = CreateBar(70d / byte.MaxValue);
        _track = CreateBar(55d / byte.MaxValue);
        _thumb = new Border
        {
            CornerRadius = new CornerRadius(3),
            IsHitTestVisible = false,
        };
        Children.AddRange([_surface, _track, _thumb]);
        Clip = _barClip;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        MinWidth = 240;
        MinHeight = 64;
        IsHitTestVisible = false;
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? SurfaceBrush
    {
        get => GetValue(SurfaceBrushProperty);
        set => SetValue(SurfaceBrushProperty, value);
    }

    public void Start()
    {
        _isRunning = true;
        StartCompositionAnimation();
    }

    public void Stop()
    {
        _isRunning = false;
        StopCompositionAnimation();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        UpdateBrushes();
        if (_isRunning)
        {
            StartCompositionAnimation();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopCompositionAnimation();
        _isAttached = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AccentBrushProperty
            || change.Property == TrackBrushProperty
            || change.Property == SurfaceBrushProperty)
        {
            UpdateBrushes();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _surface.Measure(availableSize);
        _track.Measure(availableSize);
        _thumb.Measure(availableSize);
        return new Size(MinWidth, MinHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var barRect = ResolveBarRect(finalSize);
        var thumbWidth = barRect.Width * ThumbWidthRatio;
        _barClip.Rect = barRect;
        _surface.Arrange(barRect);
        _track.Arrange(barRect);
        _thumb.Arrange(new Rect(barRect.X - thumbWidth, barRect.Y, thumbWidth, barRect.Height));

        var animationDistance = barRect.Width + thumbWidth;
        if (Math.Abs(_animationDistance - animationDistance) > 0.1d)
        {
            _animationDistance = animationDistance;
            StartCompositionAnimation();
        }

        return finalSize;
    }

    private IBrush ResolveAccent()
        => AccentBrush
           ?? TryGetBrush("AppAccentBrush")
           ?? new SolidColorBrush(Color.Parse("#60A5FA"));

    private IBrush ResolveTrack()
        => TrackBrush
           ?? TryGetBrush("AppOverlayBrush")
           ?? new SolidColorBrush(Color.FromArgb(56, 255, 255, 255));

    private IBrush ResolveSurface()
        => SurfaceBrush
           ?? TryGetBrush("AppOverlaySubtleBrush")
           ?? new SolidColorBrush(Color.FromArgb(36, 255, 255, 255));

    private IBrush? TryGetBrush(string key)
        => TryGetResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;

    private void OnActualThemeVariantChanged(object? sender, EventArgs args)
    {
        UpdateBrushes();
    }

    private void UpdateBrushes()
    {
        _surface.Background = ResolveSurface();
        _track.Background = ResolveTrack();
        _thumb.Background = ResolveAccent();
    }

    private static Border CreateBar(double opacity)
        => new()
        {
            CornerRadius = new CornerRadius(3),
            IsHitTestVisible = false,
            Opacity = opacity,
        };

    private void StartCompositionAnimation()
    {
        if (!_isAttached || !_isRunning || _animationDistance <= 0
            || ElementComposition.GetElementVisual(_thumb) is not { } visual)
        {
            return;
        }

        visual.StopAnimation(nameof(CompositionVisual.Translation));
        visual.Translation = default;

        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        animation.Duration = CycleDuration;
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        // 循环重置发生在裁剪区外，首尾衔接不显示跳变。
        animation.InsertKeyFrame(0f, default);
        animation.InsertKeyFrame(1f, new Vector3D(_animationDistance, 0, 0), new LinearEasing());
        visual.StartAnimation(nameof(CompositionVisual.Translation), animation);
    }

    private void StopCompositionAnimation()
    {
        if (ElementComposition.GetElementVisual(_thumb) is not { } visual)
        {
            return;
        }

        visual.StopAnimation(nameof(CompositionVisual.Translation));
        visual.Translation = default;
    }

    private static Rect ResolveBarRect(Size size)
    {
        var barWidth = Math.Min(280, Math.Max(180, size.Width * 0.42));
        var x = (size.Width - barWidth) * 0.5;
        var y = size.Height * 0.5 - BarHeight * 0.5;
        return new Rect(x, y, barWidth, BarHeight);
    }

}
