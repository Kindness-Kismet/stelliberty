# C# + Avalonia

适配 Avalonia 12.1.4、net8.0。FeedbackButton 与 TabHeader 通过 FeedbackInputButton 复用 Avalonia Button 的点击、键盘和 pointer capture，按真实指针位置补齐拖出与重新进入反馈；独立 ControlTheme 不叠加 Fluent 默认 hover 背景或按钮阴影。SetTokens 更新当前主题，SetRowText 提供标题与副标题，CrossfadeTabs 缓存每页的 Control 并负责选中权重和内容交叉淡化。

TransformOperationsTransition 作用于模板内 Border，按钮命中区域保持不变。有效隐藏通过公开的祖先 IsVisible 属性订阅处理；窗口 Deactivated、LostFocus、禁用及脱离视觉树均取消输入并复位反馈，取消后的 Space/Enter 释放不会再触发动作。所有组件在 UI 线程创建，由宿主视觉树管理生命周期。

SurfaceShadow 的 alpha 从 0.25 量化为 8 bit A=64。RoundedSurfaces.Create 返回 RoundedSurface：外层 Border 绘制 BoxShadows，Body 的 RectangleGeometry 将背景与子控件一起裁成圆角。将返回值接入实际 Dialog/Menu/Popup 宿主，投影留白由宿主布局提供；BoxShadow 的 blur 按 Avalonia 的真实坐标语义处理。

优先复用项目已有组件库的样式和输入，只调整明确差异；缺少动画时使用所附包装。普通按钮按 ButtonRole 分为 Plain、Dismiss、Primary。Plain 使用主题前景、透明底色和无 hover 底色；Dismiss 对应取消、返回等中性填充操作，Primary 使用主题强调色。按钮圆角沿用项目几何，可通过 UiTokens.ButtonRadius 配置；默认强调色由宿主提供 #238fe8。

surface 输入为当前已合成的不透明 sRGB 颜色。8% mix 在 sRGB 编码通道执行 base × (1 − a) + overlay × a，alpha 先合成，再按 8 bit 四舍五入。强调色先合成到 surface。文字对比度另外使用线性化 sRGB 相对亮度，分别计算 #000000 和 #FFFFFF 的对比度并选择较高者；Dismiss、Primary 的 hover 中间帧也按实际背景重算文字，普通字号目标至少 4.5。

按压目标 0.94、释放目标 1.0，180 ms；曲线用 x 二分 16 次求 cubic-bezier(0.34,1.56,0.64,1) 的 y，保留 overshoot。取消、失焦、隐藏、禁用复位，不改变外层命中区域。

NavigationRow 与 SettingsCardRow 的独立圆角为 10，MenuRow 为 7。NavigationRow 的 raw hover 使用共享主题 NavigationHover，背景内缩 8,2,8,2；MenuRow 内缩 6,1,6,1。SettingsCardRow 正常填充 surface，hover 覆盖整行，亮主题 #eff2f7、暗主题 #2d2d2d，可用主题值替换。标题和 hover 背景分别布局，文字内缩不改变背景几何。单行 Navigation 最低 40，带副标题时最低 58 并随换行增高；SettingsCardRow 最低 58，MenuRow 最低 36，副标题间距 4。

按压 level 从 0 到 1，brightness=.92、contrast=1.12，按压 30 ms、恢复 72 ms，easeOutCubic。背景通过 FeedbackColor 做轻微加深，leading/trailing 装饰图标通过 DecorativeIcon 局部反馈，标题和副标题保持原主题颜色。raw hover 保持原始 RGB，Navigation/Menu 用 72 ms alpha 淡入淡出，Settings 用 72 ms 混色过渡。4.5:1 动态选色规则用于 Filled 操作与选中的 Tab 文字。

DecorativeIcon 接收 20 个逻辑单位内的 Geometry 和主题颜色，IconColor 对这个图标的颜色执行预乘 alpha 通道公式。SetLeadingContent、SetTrailingContent 只驱动明确标记为 DecorativeIcon 的子元素，Checkbox、Toggle 等真实 Control 直接保留自己的命中、状态和反馈。已有组件库可在图标绘制层复用 IconColor 或 DecorativeIcon，整行背景与文字各自绘制；SetTokens 更新参数，SetColor 更新图标语义色。

RoundedAncestor 提供最近圆角祖先和真实内缩 d，d 包括 border、padding、paint inset，文字内缩和 shadow 不参与。SetCornerContext 同时接收 CornerContext 和实际 paintedInset。Adjacent 只对指定的物理邻接角使用 max(R-d,0)；不等距时调用方先调整宿主布局和绘制内缩再提供上下文。没有圆角祖先时使用独立圆角，中间行使用 GroupCap 显式共享组圆角。ExampleRows.CreateGroup 演示宿主统一 padding 4、子行全幅绘制，首行顶角、末行底角使用 d=4，中间行共享相同组圆角。

文字型 Tab 包含纯文字和图标加文字，hover 使用 UiTokens.TextTabHover，Navigation/Menu 使用独立的 NavigationHover。构造共享主题 Tokens 时，2个字段采用同一个默认配色：在主题 surface 上叠 8% 的高对比纯黑或纯白。已有组件库优先保留导航和文字 Tab 各自的语义键，TextTabHover 与 NavigationHover 可以分别覆盖。通过 with 改变局部 Background 时保留主题 hover 字段。CrossfadeTabs.Select 直接更新 TabHeader.IsSelected，当前业务选中项不画 hover 底色，未选中项正常显示；pressed 对自己的 raw hover 做 brightness=.92、contrast=1.12 的轻微加深，混合型的 DecorativeIcon 局部反馈。背景内缩 4、圆角 7，hover 立即更新，按压 30 ms、恢复 72 ms；仅提供 Icon 的纯图标型保持原视觉，不接这些新增反馈。文字先按 weight 混色，再对最终不透明文字与实际背景核对 4.5:1，不足时选纯黑或纯白；未选中文字保留主题颜色和 alpha。指示线厚度 2、圆头半径 1，不透明基色相对实际下方 surface 核对 3:1，显示 alpha 随 160 ms linear 权重改变。点击、Space/Enter 走 Button，方向键、Home/End 由 CrossfadeTabs 切换并移动焦点。

CrossfadeTabs 在首次选择时调用对应 factory 1次，之后复用原 Control；退出包装层禁用输入，页面本身的 IsEnabled 设置保留。快速切换从当前已绘制权重接续，CrossfadeBlend 将各层 alpha 按累计权重归一化，使实际 alpha-over 贡献等于每页 weight，内容不透明且保持线性交叉淡化。所附实现固定 1x：Spring 180 ms、Luminance 30/72 ms、Tab 160 ms。

MenuDismissLayer 添加到覆盖内容的 host Grid。ShowAt 接收菜单 body 的位置和尺寸，命中按 Body 圆角轮廓判断；tunnel handler 在子控件处理前拦截外部按压，立即隐藏菜单并触发 Dismissed。屏蔽层接管当前鼠标和触摸指针，吞掉移动、鼠标所有已按下按钮的释放和多点触摸整个序列，全部结束才隐藏并触发 Closed。Escape 按下关闭、释放后结束屏蔽；窗口失活、祖先隐藏、捕获取消和脱离视觉树清理序列。业务完成时调用 CloseMenu，宿主 Grid 布局随窗口尺寸调整屏蔽层；Dismissed 对应菜单内容关闭，Closed 对应屏蔽层移除。

Dialog、PopupMenu、InnerDropdown 圆角分别 16、12、8。所有浮层共用 blur 5、offset (0,1)、rgba(0,0,0,0.25)、spread 0 的外阴影；普通按钮、行项、标签无阴影。下面是可独立复制的完整组件源码，按职责拆文件时保留同一套参数与输入辅助类。

```csharp
using System.Globalization;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace UiDesignExamples;

public sealed record UiTokens(
    Color Background, Color Foreground, Color Accent, Color RowTitle, Color RowSubtitle, bool IsDark)
{
    public double PressScale => 0.94;
    public TimeSpan PressDuration => TimeSpan.FromMilliseconds(180);
    public TimeSpan RowPressDuration => TimeSpan.FromMilliseconds(30);
    public TimeSpan RowHoverDuration => TimeSpan.FromMilliseconds(72);
    public double HoverLevel => 0;
    public double ButtonRadius { get; init; } = 7;
    public double RowRadius { get; init; } = 10;
    public Color NavigationHover { get; init; } = Over(ContrastText(Background), Background, 0.08);
    public Color TextTabHover { get; init; } = Over(ContrastText(Background), Background, 0.08);
    public Color? SettingsHoverOverride { get; init; }
    public Color SettingsHover => SettingsHoverOverride ?? (IsDark ? Color.Parse("#2d2d2d") : Color.Parse("#eff2f7"));
    public double DialogRadius { get; init; } = 16;
    public double PopupRadius { get; init; } = 12;
    public double InnerDropdownRadius { get; init; } = 8;
    public TimeSpan CrossfadeDuration => TimeSpan.FromMilliseconds(160);
    public BoxShadows SurfaceShadow { get; init; } = new BoxShadows(new BoxShadow
    {
        OffsetX = 0, OffsetY = 1, Blur = 5, Spread = 0,
        Color = Color.FromArgb((byte)Math.Round(0.25 * 255, MidpointRounding.AwayFromZero), 0, 0, 0)
    });
    public Easing PressEasing { get; } = new PressCurve();

    public static Color Over(Color foreground, Color background, double opacity)
    {
        var alpha = foreground.A / 255d * opacity;
        byte Channel(byte fg, byte bg) => (byte)Math.Round(fg * alpha + bg * (1 - alpha), MidpointRounding.AwayFromZero);
        return Color.FromArgb(255, Channel(foreground.R, background.R),
            Channel(foreground.G, background.G), Channel(foreground.B, background.B));
    }

    public static Color Mix(Color background, Color overlay, double weight) =>
        Over(Color.FromArgb(255, overlay.R, overlay.G, overlay.B), background, weight);

    public static double ContrastRatio(Color foreground, Color background)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte value)
            {
                var c = value / 255d;
                return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }
        var a = Luminance(foreground);
        var b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    public static Color BestContrastAccent(Color accent, Color surface, double minimum)
    {
        var actual = Over(accent, surface, 1);
        return ContrastRatio(actual, surface) >= minimum ? actual : ContrastText(surface);
    }

    public Color TabBackground(bool hover, bool pressed, bool iconOnly, bool selected = false, double pressLevel = 0)
    {
        return iconOnly || !pressed && (!hover || selected) ? Background : FeedbackColor(TextTabHover, pressLevel);
    }

    public Color TabText(Color background, double weight)
    {
        if (weight == 0) return Foreground;
        var selected = BestContrastAccent(Accent, background, 4.5);
        var mixed = Mix(Over(Foreground, background, 1), selected, weight);
        return BestContrastAccent(mixed, background, 4.5);
    }

    public static Color ContrastText(Color background)
    {
        static double Linear(byte value)
        {
            var c = value / 255d;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        var luminance = 0.2126 * Linear(background.R) + 0.7152 * Linear(background.G) + 0.0722 * Linear(background.B);
        return (luminance + 0.05) / 0.05 >= 1.05 / (luminance + 0.05) ? Colors.Black : Colors.White;
    }

    public (Color background, Color foreground) ButtonColors(ButtonRole role, double hover)
    {
        if (role == ButtonRole.Plain) return (Colors.Transparent, Foreground);
        var normal = role == ButtonRole.Dismiss ? Over(ContrastText(Background), Background, 0.08) : Over(Accent, Background, 1);
        var overlay = ContrastText(normal);
        var actual = Over(overlay, normal, 0.08 * hover);
        return (actual, ContrastText(actual));
    }

    public Color RawRowHover(FeedbackKind kind) => kind is FeedbackKind.NavigationRow or FeedbackKind.MenuRow
        ? NavigationHover : SettingsHover;

    public Color RowBackground(FeedbackKind kind, double pressLevel, double hover)
    {
        if (kind == FeedbackKind.SettingsCardRow)
            return FeedbackColor(Mix(Background, RawRowHover(kind), hover), pressLevel);
        var color = FeedbackColor(RawRowHover(kind), pressLevel);
        return Color.FromArgb((byte)Math.Round(color.A * Math.Clamp(hover, 0, 1), MidpointRounding.AwayFromZero),
            color.R, color.G, color.B);
    }

    public Color IconColor(Color color, double level) => FeedbackColor(color, level);

    public Color FeedbackColor(Color color, double level)
    {
        if (level == 0) return color;
        var alpha = (double)color.A;
        if (alpha == 0) return Colors.Transparent;
        var brightness = 1 - 0.08 * level;
        var contrast = 1 + 0.12 * Math.Max(0, level);
        byte Channel(byte value)
        {
            var premultiplied = Math.Round(value * alpha / 255, MidpointRounding.AwayFromZero);
            var filtered = Math.Clamp(Math.Round(
                (premultiplied * brightness - alpha / 2) * contrast + alpha / 2,
                MidpointRounding.AwayFromZero), 0, alpha);
            return (byte)Math.Round(filtered * 255 / alpha, MidpointRounding.AwayFromZero);
        }
        return Color.FromArgb(color.A, Channel(color.R), Channel(color.G), Channel(color.B));
    }
}

public sealed class PressCurve : Easing
{
    public override double Ease(double x)
    {
        static double Cubic(double t, double a, double b) =>
            3 * Math.Pow(1 - t, 2) * t * a + 3 * (1 - t) * t * t * b + t * t * t;
        var lo = 0d;
        var hi = 1d;
        for (var index = 0; index < 16; index++)
        {
            var mid = (lo + hi) / 2;
            if (Cubic(mid, 0.34, 0.64) < x) lo = mid; else hi = mid;
        }
        return Cubic((lo + hi) / 2, 1.56, 1);
    }
}

public readonly record struct RoundedAncestor(double Radius, double Inset);
[Flags]
public enum Corners { TopLeft = 1, TopRight = 2, BottomRight = 4, BottomLeft = 8, Top = 3, Bottom = 12, All = 15 }
public enum CornerKind { None, Adjacent, GroupCap }
public readonly record struct CornerContext(CornerKind Kind, Corners Corners = Corners.All,
    RoundedAncestor? Ancestor = null, double GroupRadius = 0);

public static class RoundedGeometry
{
    public static RoundedAncestor? EqualInsetAncestor(double radius, double dx, double dy) =>
        dx >= 0 && dy >= 0 && Math.Abs(dx - dy) < 0.000001 ? new RoundedAncestor(radius, dx) : null;

    public static CornerRadius Resolve(double standalone, CornerContext context)
    {
        if (context.Kind == CornerKind.GroupCap) return new CornerRadius(context.GroupRadius);
        if (context.Kind == CornerKind.None) return new CornerRadius(standalone);
        if (context.Ancestor is not { } ancestor)
            throw new ArgumentException("Adjacent corners require equal painted insets", nameof(context));
        var radius = Math.Max(ancestor.Radius - ancestor.Inset, 0);
        double Corner(Corners corner) => context.Corners.HasFlag(corner) ? radius : standalone;
        return new CornerRadius(Corner(Corners.TopLeft), Corner(Corners.TopRight),
            Corner(Corners.BottomRight), Corner(Corners.BottomLeft));
    }

    public static bool Contains(Size size, double radius, Point point)
    {
        if (!new Rect(size).Contains(point)) return false;
        var r = Math.Min(radius, Math.Min(size.Width, size.Height) / 2);
        var x = point.X < r ? r : point.X > size.Width - r ? size.Width - r : point.X;
        var y = point.Y < r ? r : point.Y > size.Height - r ? size.Height - r : point.Y;
        return Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2) <= r * r;
    }
}

public enum FeedbackKind { Scale, NavigationRow, SettingsCardRow, MenuRow }
public enum ButtonRole { Plain, Dismiss, Primary }

public sealed class DecorativeIcon : Control
{
    public static readonly StyledProperty<double> LevelProperty =
        AvaloniaProperty.Register<DecorativeIcon, double>(nameof(Level));
    private readonly Geometry _glyph;
    private UiTokens _tokens;
    private readonly DoubleTransition _transition;
    public Color BaseColor { get; private set; }
    public double Level { get => GetValue(LevelProperty); private set => SetValue(LevelProperty, value); }
    public Color RenderColor => _tokens.IconColor(BaseColor, Level);

    static DecorativeIcon() => AffectsRender<DecorativeIcon>(LevelProperty);

    public DecorativeIcon(Geometry glyph, Color color, UiTokens tokens)
    {
        _glyph = glyph;
        _tokens = tokens;
        BaseColor = color;
        Width = Height = 20;
        Focusable = false;
        IsHitTestVisible = false;
        _transition = new DoubleTransition
        {
            Property = LevelProperty, Duration = tokens.RowHoverDuration, Easing = new CubicEaseOut()
        };
        Transitions = new Transitions { _transition };
    }

    public void SetPressed(bool pressed)
    {
        _transition.Duration = pressed ? _tokens.RowPressDuration : _tokens.RowHoverDuration;
        Level = pressed && IsEffectivelyEnabled ? 1 : 0;
    }
    public void SetTokens(UiTokens tokens) { _tokens = tokens; InvalidateVisual(); }
    public void SetColor(Color color) { BaseColor = color; InvalidateVisual(); }
    public void Cancel()
    {
        var transitions = Transitions;
        Transitions = null;
        Level = 0;
        Transitions = transitions;
    }
    public override void Render(DrawingContext context) =>
        context.DrawGeometry(new SolidColorBrush(RenderColor), null, _glyph);

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Cancel();
        base.OnDetachedFromVisualTree(e);
    }
}

public abstract class FeedbackInputButton : Button
{
    private IPointer? _pressPointer;
    private bool _pointerInside;
    private bool _inputCancelled;
    private readonly HashSet<Key> _heldKeys = new();
    private readonly HashSet<Key> _cancelledKeys = new();
    private bool _suppressKeyboardClick;
    private WindowBase? _window;
    private readonly List<Visual> _visibilityParents = new();
    protected bool HoverWithin => !_inputCancelled && IsPointerOver;
    protected bool PressedWithin => !_inputCancelled && IsPressed && (_pressPointer is null
        ? _cancelledKeys.Count == 0 : _pointerInside);
    protected abstract void RefreshInputFeedback();
    protected virtual void FeedbackCancelled() => RefreshInputFeedback();
    protected FeedbackInputButton() { LostFocus += (_, _) => CancelInput(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _pressPointer = e.Pointer;
        _inputCancelled = false;
        _pointerInside = new Rect(Bounds.Size).Contains(e.GetPosition(this));
        base.OnPointerPressed(e);
        RefreshInputFeedback();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        _pointerInside = new Rect(Bounds.Size).Contains(e.GetPosition(this));
        base.OnPointerMoved(e);
        RefreshInputFeedback();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _pressPointer = null;
        RefreshInputFeedback();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _pressPointer = null;
        base.OnPointerCaptureLost(e);
        RefreshInputFeedback();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter)
        {
            if (!_heldKeys.Add(e.Key)) { e.Handled = true; return; }
            _cancelledKeys.Remove(e.Key);
            _pressPointer = null;
            _inputCancelled = false;
        }
        base.OnKeyDown(e);
        RefreshInputFeedback();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        var cancelled = _cancelledKeys.Remove(e.Key);
        _heldKeys.Remove(e.Key);
        _suppressKeyboardClick = cancelled;
        base.OnKeyUp(e);
        _suppressKeyboardClick = false;
        if (cancelled) e.Handled = true;
        RefreshInputFeedback();
    }

    protected override void OnClick() { if (!_suppressKeyboardClick) base.OnClick(); }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        _inputCancelled = false;
        _pointerInside = true;
        base.OnPointerEntered(e);
        RefreshInputFeedback();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        _pointerInside = false;
        base.OnPointerExited(e);
        RefreshInputFeedback();
    }

    private void CancelInput()
    {
        _inputCancelled = true;
        _cancelledKeys.UnionWith(_heldKeys);
        _heldKeys.Clear();
        if (_pressPointer?.Captured == this) _pressPointer.Capture(null);
        _pressPointer = null;
        FeedbackCancelled();
    }

    private void OnDeactivated(object? sender, EventArgs e) => CancelInput();
    private void OnAncestorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && e.NewValue is false) CancelInput();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.NewValue is false
            || change.Property == IsEffectivelyEnabledProperty && !IsEffectivelyEnabled) CancelInput();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var ancestor in this.GetVisualAncestors())
        {
            _visibilityParents.Add(ancestor);
            ancestor.PropertyChanged += OnAncestorChanged;
        }
        _window = TopLevel.GetTopLevel(this) as WindowBase;
        if (_window is not null) _window.Deactivated += OnDeactivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelInput();
        foreach (var ancestor in _visibilityParents) ancestor.PropertyChanged -= OnAncestorChanged;
        _visibilityParents.Clear();
        if (_window is not null) _window.Deactivated -= OnDeactivated;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }
}

public sealed class FeedbackButton : FeedbackInputButton
{
    public static readonly StyledProperty<double> PressLevelProperty =
        AvaloniaProperty.Register<FeedbackButton, double>(nameof(PressLevel));
    public static readonly StyledProperty<double> HoverWeightProperty =
        AvaloniaProperty.Register<FeedbackButton, double>(nameof(HoverWeight));
    private UiTokens _tokens;
    private readonly DoubleTransition _backgroundPressTransition;
    private readonly FeedbackKind _kind;
    private readonly ButtonRole _role;
    private Border? _surface;
    private TextBlock? _subtitle;
    private TextBlock? _title;
    private Grid? _rowContent;
    private ContentPresenter? _leadingHost;
    private ContentPresenter? _trailingHost;
    private DecorativeIcon? _leadingIcon;
    private DecorativeIcon? _trailingIcon;
    private CornerContext _corners;
    private Thickness? _paintInset;
    public double PressLevel { get => GetValue(PressLevelProperty); private set => SetValue(PressLevelProperty, value); }
    public double HoverWeight { get => GetValue(HoverWeightProperty); private set => SetValue(HoverWeightProperty, value); }

    public FeedbackButton(UiTokens tokens, FeedbackKind kind, ButtonRole role = ButtonRole.Plain)
    {
        _tokens = tokens;
        _kind = kind;
        _role = role;
        Background = new SolidColorBrush(tokens.Background);
        Foreground = new SolidColorBrush(tokens.Foreground);
        CornerRadius = new CornerRadius(StandaloneRadius);
        HorizontalContentAlignment = IsRow ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        if (IsRow) Padding = new Thickness(22, 10);
        MinHeight = kind == FeedbackKind.SettingsCardRow ? 58 : kind == FeedbackKind.MenuRow ? 36 : 40;
        _backgroundPressTransition = new DoubleTransition
        {
            Property = PressLevelProperty, Duration = tokens.RowHoverDuration, Easing = new CubicEaseOut()
        };
        Transitions = new Transitions
        {
            _backgroundPressTransition,
            new DoubleTransition { Property = HoverWeightProperty, Duration = tokens.RowHoverDuration, Easing = new CubicEaseOut() }
        };
        if (kind == FeedbackKind.Scale)
        {
            var (background, foreground) = tokens.ButtonColors(role, 0);
            Background = new SolidColorBrush(background);
            Foreground = new SolidColorBrush(foreground);
        }
        else UpdateRowColors();
        Theme = new ControlTheme(typeof(Button))
        {
            Setters =
            {
                new Setter(TemplateProperty, new FuncControlTemplate<Button>((_, _) => CreateSurface()))
            }
        };
    }

    private Control CreateSurface()
    {
        var presenter = new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            HorizontalContentAlignment = IsRow ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        presenter.Bind(ContentPresenter.ContentProperty, new TemplateBinding(ContentProperty));
        presenter.Bind(ContentPresenter.ForegroundProperty, new TemplateBinding(ForegroundProperty));
        presenter.Bind(ContentPresenter.ContentTemplateProperty, new TemplateBinding(ContentTemplateProperty));
        _surface = new Border
        {
            RenderTransformOrigin = RelativePoint.Center,
            Margin = PaintInset,
            RenderTransform = TransformOperations.Identity,
            Transitions = new Transitions
            {
                new TransformOperationsTransition
                {
                    Property = RenderTransformProperty, Duration = _tokens.PressDuration,
                    Easing = _tokens.PressEasing
                }
            }
        };
        _surface.Bind(Border.BackgroundProperty, new TemplateBinding(BackgroundProperty));
        _surface.Bind(Border.CornerRadiusProperty, new TemplateBinding(CornerRadiusProperty));
        presenter.Bind(ContentPresenter.PaddingProperty, new TemplateBinding(PaddingProperty));
        if (!IsRow) _surface.Child = presenter;
        if (!IsRow) return _surface;
        var row = new Grid();
        row.Children.Add(_surface);
        row.Children.Add(presenter);
        return row;
    }

    public void SetTokens(UiTokens tokens)
    {
        _tokens = tokens;
        ResetFeedback(false);
        var (background, foreground) = _kind == FeedbackKind.Scale
            ? tokens.ButtonColors(_role, HoverWeight) : (tokens.Background, tokens.Foreground);
        Background = new SolidColorBrush(background);
        Foreground = new SolidColorBrush(foreground);
        CornerRadius = RoundedGeometry.Resolve(StandaloneRadius, _corners);
        _leadingIcon?.SetTokens(tokens);
        _trailingIcon?.SetTokens(tokens);
        if (IsRow) UpdateRowColors();
    }

    private bool IsRow => _kind is FeedbackKind.NavigationRow or FeedbackKind.SettingsCardRow or FeedbackKind.MenuRow;
    private double StandaloneRadius => _kind == FeedbackKind.MenuRow ? 7 : IsRow ? _tokens.RowRadius : _tokens.ButtonRadius;
    private Thickness PaintInset => _paintInset ?? (_kind == FeedbackKind.NavigationRow
        ? new Thickness(8, 2) : _kind == FeedbackKind.MenuRow ? new Thickness(6, 1) : new Thickness(0));

    public void SetCornerContext(CornerContext context, Thickness paintedInset)
    {
        if (context.Kind == CornerKind.Adjacent &&
            (paintedInset.Left != paintedInset.Top || paintedInset.Right != paintedInset.Bottom))
            throw new ArgumentException("Adjacent corners require equal painted insets", nameof(paintedInset));
        _corners = context;
        _paintInset = paintedInset;
        CornerRadius = RoundedGeometry.Resolve(StandaloneRadius, context);
        if (_surface is not null) _surface.Margin = paintedInset;
    }

    public void SetRowText(string title, string? subtitle = null)
    {
        EnsureRowContent();
        _title!.Text = title;
        _subtitle!.Text = subtitle ?? string.Empty;
        _subtitle.IsVisible = subtitle is not null;
        if (_kind == FeedbackKind.NavigationRow) MinHeight = subtitle is null ? 40 : 58;
        UpdateRowColors();
    }

    private void EnsureRowContent()
    {
        if (_rowContent is not null) return;
        _rowContent = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        _leadingHost = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center };
        _trailingHost = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center };
        _title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        _subtitle = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var caption = new StackPanel { Spacing = 4 };
        caption.Children.Add(_title);
        caption.Children.Add(_subtitle);
        Grid.SetColumn(caption, 1);
        Grid.SetColumn(_trailingHost, 2);
        _rowContent.Children.Add(_leadingHost);
        _rowContent.Children.Add(caption);
        _rowContent.Children.Add(_trailingHost);
        Content = _rowContent;
    }

    public void SetLeadingContent(Control? content)
    {
        EnsureRowContent();
        _leadingHost!.Content = content;
        _leadingHost.Margin = content is null ? new Thickness(0) : new Thickness(0, 0, 12, 0);
        _leadingIcon = content as DecorativeIcon;
        RefreshInputFeedback();
    }
    public void SetTrailingContent(Control? content)
    {
        EnsureRowContent();
        _trailingHost!.Content = content;
        _trailingHost.Margin = content is null ? new Thickness(0) : new Thickness(12, 0, 0, 0);
        _trailingIcon = content as DecorativeIcon;
        RefreshInputFeedback();
    }

    private void UpdateRowColors()
    {
        UpdateRowBackground();
        Foreground = new SolidColorBrush(_tokens.RowTitle);
        if (_subtitle is not null) _subtitle.Foreground = new SolidColorBrush(_tokens.RowSubtitle);
    }

    private void UpdateRowBackground() =>
        Background = new SolidColorBrush(_tokens.RowBackground(_kind, PressLevel, HoverWeight));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_tokens is null) return;
        if (change.Property == IsVisibleProperty || change.Property == IsEffectivelyEnabledProperty)
        {
            if (!IsVisible || !IsEffectivelyEnabled) ResetFeedback();
        }
        if (change.Property == IsPressedProperty || change.Property == IsPointerOverProperty)
        {
            RefreshInputFeedback();
        }
        if (change.Property == HoverWeightProperty && _kind == FeedbackKind.Scale)
        {
            var (background, foreground) = _tokens.ButtonColors(_role, HoverWeight);
            Background = new SolidColorBrush(background);
            Foreground = new SolidColorBrush(foreground);
        }
        if (IsRow && (change.Property == HoverWeightProperty || change.Property == PressLevelProperty)) UpdateRowBackground();
    }

    protected override void RefreshInputFeedback()
    {
        if (_tokens is null) return;
        HoverWeight = HoverWithin && IsEffectivelyEnabled ? 1 : 0;
        var pressed = PressedWithin && IsEffectivelyEnabled;
        if (_kind == FeedbackKind.Scale && _surface is not null)
        {
            var scale = pressed ? _tokens.PressScale : 1;
            _surface.RenderTransform = TransformOperations.Parse(
                "scale(" + scale.ToString(CultureInfo.InvariantCulture) + ")");
        }
        if (IsRow)
        {
            _backgroundPressTransition.Duration = pressed ? _tokens.RowPressDuration : _tokens.RowHoverDuration;
            PressLevel = pressed ? 1 : 0;
            _leadingIcon?.SetPressed(pressed);
            _trailingIcon?.SetPressed(pressed);
        }
    }

    protected override void FeedbackCancelled() { if (_tokens is not null) ResetFeedback(); }

    private void ResetFeedback(bool clearHover = true)
    {
        if (_surface is not null)
        {
            var transitions = _surface.Transitions;
            _surface.Transitions = null;
            _surface.RenderTransform = TransformOperations.Identity;
            _surface.Transitions = transitions;
        }
        var levelTransitions = Transitions;
        Transitions = null;
        PressLevel = 0;
        _leadingIcon?.Cancel();
        _trailingIcon?.Cancel();
        if (clearHover) HoverWeight = 0;
        Transitions = levelTransitions;
    }
}

public sealed record TabHeaderSpec(string? Text, Control? Icon = null);

public static class CrossfadeBlend
{
    public static double[] Interpolate(IReadOnlyList<double> from, int selected, double progress) =>
        from.Select((weight, index) => weight + ((index == selected ? 1 : 0) - weight) * progress).ToArray();

    public static double[] LayerOpacities(IReadOnlyList<double> weights)
    {
        var result = new double[weights.Count];
        var cumulative = 0d;
        for (var index = 0; index < weights.Count; index++)
        {
            cumulative += weights[index];
            result[index] = cumulative > 0 ? weights[index] / cumulative : 0;
        }
        return result;
    }
}

public sealed class TabHeader : FeedbackInputButton
{
    public static readonly StyledProperty<double> PressLevelProperty =
        AvaloniaProperty.Register<TabHeader, double>(nameof(PressLevel));
    private UiTokens _tokens;
    private readonly DoubleTransition _backgroundPressTransition;
    private readonly bool _iconOnly;
    private readonly DecorativeIcon? _decorativeIcon;
    private Border? _backdrop;
    private Border? _indicator;
    private double _weight;
    public bool IsSelected { get; private set; }
    public double PressLevel { get => GetValue(PressLevelProperty); private set => SetValue(PressLevelProperty, value); }

    public TabHeader(UiTokens tokens, TabHeaderSpec specification)
    {
        _tokens = tokens;
        _backgroundPressTransition = new DoubleTransition
        {
            Property = PressLevelProperty, Duration = tokens.RowHoverDuration, Easing = new CubicEaseOut()
        };
        Transitions = new Transitions { _backgroundPressTransition };
        _iconOnly = specification.Icon is not null && string.IsNullOrEmpty(specification.Text);
        _decorativeIcon = _iconOnly ? null : specification.Icon as DecorativeIcon;
        if (_iconOnly || specification.Icon is null) Content = _iconOnly ? specification.Icon : specification.Text;
        else
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            content.Children.Add(specification.Icon);
            content.Children.Add(new TextBlock { Text = specification.Text, VerticalAlignment = VerticalAlignment.Center });
            Content = content;
        }
        MinHeight = 40;
        Theme = new ControlTheme(typeof(Button))
        {
            Setters = { new Setter(TemplateProperty, new FuncControlTemplate<Button>((_, _) => CreateSurface())) }
        };
    }

    private Control CreateSurface()
    {
        var layout = new Grid();
        _backdrop = new Border { Margin = new Thickness(4), CornerRadius = new CornerRadius(7) };
        var presenter = new ContentPresenter
        {
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 4)
        };
        presenter.Bind(ContentPresenter.ContentProperty, new TemplateBinding(ContentProperty));
        presenter.Bind(ContentPresenter.ForegroundProperty, new TemplateBinding(ForegroundProperty));
        _indicator = new Border { Height = 2, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Bottom };
        layout.Children.Add(_backdrop);
        layout.Children.Add(presenter);
        layout.Children.Add(_indicator);
        UpdateAppearance();
        return layout;
    }

    public void SetTokens(UiTokens tokens)
    {
        _tokens = tokens;
        _decorativeIcon?.SetTokens(tokens);
        UpdateAppearance();
    }
    public void SetWeight(double weight) { _weight = weight; UpdateAppearance(); }
    public void SetSelected(bool selected) { IsSelected = selected; UpdateAppearance(); }

    private void UpdateAppearance()
    {
        var hover = HoverWithin && IsEffectivelyEnabled;
        var pressed = PressedWithin && IsEffectivelyEnabled;
        var background = _tokens.TabBackground(hover, pressed, _iconOnly, IsSelected, PressLevel);
        Background = new SolidColorBrush(_tokens.Background);
        Foreground = new SolidColorBrush(_tokens.TabText(background, _weight));
        if (_backdrop is not null)
            _backdrop.Background = !_iconOnly && (hover && !IsSelected || pressed)
                ? new SolidColorBrush(background) : Brushes.Transparent;
        if (_indicator is not null)
        {
            // 指示线位于内缩背景之外，不透明基色对实际 surface 核对。
            _indicator.Background = new SolidColorBrush(UiTokens.BestContrastAccent(_tokens.Accent, _tokens.Background, 3));
            _indicator.Opacity = _weight;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_tokens is null) return;
        if (change.Property == PressLevelProperty) UpdateAppearance();
        if (change.Property == IsPressedProperty || change.Property == IsPointerOverProperty
            || change.Property == IsEffectivelyEnabledProperty) RefreshInputFeedback();
    }

    protected override void RefreshInputFeedback()
    {
        if (_tokens is null) return;
        var pressed = PressedWithin && IsEffectivelyEnabled;
        _backgroundPressTransition.Duration = pressed ? _tokens.RowPressDuration : _tokens.RowHoverDuration;
        PressLevel = pressed && !_iconOnly ? 1 : 0;
        _decorativeIcon?.SetPressed(pressed);
        UpdateAppearance();
    }
    protected override void FeedbackCancelled()
    {
        var transitions = Transitions;
        Transitions = null;
        PressLevel = 0;
        Transitions = transitions;
        _decorativeIcon?.Cancel();
        if (_tokens is not null) UpdateAppearance();
    }
}

public sealed class CrossfadeTabs : StackPanel
{
    private readonly IReadOnlyList<Func<Control>> _factories;
    private readonly Control?[] _pages;
    private readonly Border[] _layers;
    private readonly TabHeader[] _headers;
    private readonly Grid _content;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly TimeSpan _duration;
    private double[] _from;
    private double[] _weights;
    private int _selected = -1;

    public CrossfadeTabs(UiTokens tokens, IReadOnlyList<string> titles, IReadOnlyList<Func<Control>> pages)
        : this(tokens, titles.Select(title => new TabHeaderSpec(title)).ToArray(), pages) { }

    public CrossfadeTabs(UiTokens tokens, IReadOnlyList<TabHeaderSpec> headers, IReadOnlyList<Func<Control>> pages)
    {
        if (headers.Count != pages.Count) throw new ArgumentException("Tab header and page counts must match");
        _factories = pages;
        _pages = new Control?[pages.Count];
        _layers = new Border[pages.Count];
        _headers = new TabHeader[pages.Count];
        _weights = new double[pages.Count];
        _from = new double[pages.Count];
        _duration = tokens.CrossfadeDuration;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Advance();
        var headerPanel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", pages.Count)))
        };
        _content = new Grid { Background = new SolidColorBrush(tokens.Background) };
        for (var index = 0; index < headers.Count; index++)
        {
            var target = index;
            var header = new TabHeader(tokens, headers[index]);
            _headers[index] = header;
            header.Click += (_, _) => Select(target);
            header.KeyDown += OnHeaderKeyDown;
            Grid.SetColumn(header, index);
            headerPanel.Children.Add(header);
            var layer = new Border
            {
                Background = new SolidColorBrush(tokens.Background), Opacity = 0,
                IsVisible = false, IsEnabled = false, IsHitTestVisible = false
            };
            _layers[index] = layer;
            _content.Children.Add(layer);
        }
        Children.Add(headerPanel);
        Children.Add(_content);
        if (pages.Count > 0) Select(0);
    }

    public void SetTokens(UiTokens tokens)
    {
        _content.Background = new SolidColorBrush(tokens.Background);
        foreach (var layer in _layers) layer.Background = new SolidColorBrush(tokens.Background);
        foreach (var header in _headers) header.SetTokens(tokens);
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _factories.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (index == _selected) return;
        _timer.Stop();
        _from = (double[])_weights.Clone();
        var initial = _selected < 0;
        _selected = index;
        _pages[index] ??= _factories[index]();
        _layers[index].Child = _pages[index];
        for (var page = 0; page < _layers.Length; page++)
        {
            var active = page == index;
            _headers[page].SetSelected(active);
            _layers[page].IsEnabled = active;
            _layers[page].IsHitTestVisible = active;
            _layers[page].IsVisible = active || _weights[page] > 0;
        }
        if (initial) { Apply(1); return; }
        _clock.Restart();
        _timer.Start();
    }

    private void Advance()
    {
        var progress = Math.Clamp(_clock.Elapsed.TotalMilliseconds / _duration.TotalMilliseconds, 0, 1);
        Apply(progress);
        if (progress >= 1) _timer.Stop();
    }

    private void Apply(double progress)
    {
        _weights = CrossfadeBlend.Interpolate(_from, _selected, progress);
        var opacities = CrossfadeBlend.LayerOpacities(_weights);
        for (var index = 0; index < _weights.Length; index++)
        {
            // 归一化 alpha-over 后每页贡献正好等于 weight，避免底色露出。
            _layers[index].Opacity = opacities[index];
            _layers[index].IsVisible = _weights[index] > 0 || index == _selected;
            _headers[index].SetWeight(_weights[index]);
        }
    }

    private void OnHeaderKeyDown(object? sender, KeyEventArgs e)
    {
        var target = e.Key switch
        {
            Key.Left => Math.Max(0, _selected - 1), Key.Right => Math.Min(_headers.Length - 1, _selected + 1),
            Key.Home => 0, Key.End => _headers.Length - 1, _ => -1
        };
        if (target < 0) return;
        Select(target);
        _headers[target].Focus();
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        Apply(1);
        base.OnDetachedFromVisualTree(e);
    }
}

public enum SurfaceKind { Dialog, PopupMenu, InnerDropdown }

public sealed class RoundedSurface : Border
{
    public Border Body { get; }
    public double Radius { get; }

    public RoundedSurface(UiTokens tokens, double radius, Control content)
    {
        Radius = radius;
        CornerRadius = new CornerRadius(radius);
        BoxShadow = tokens.SurfaceShadow;
        Body = new Border
        {
            Background = new SolidColorBrush(tokens.Background), CornerRadius = new CornerRadius(radius), Child = content
        };
        Body.SizeChanged += (_, _) => Body.Clip = new RectangleGeometry
        {
            Rect = new Rect(Body.Bounds.Size), RadiusX = radius, RadiusY = radius
        };
        Child = Body;
    }
}

public static class RoundedSurfaces
{
    public static RoundedSurface Create(UiTokens tokens, SurfaceKind kind, Control content)
    {
        var radius = kind switch
        {
            SurfaceKind.Dialog => tokens.DialogRadius,
            SurfaceKind.PopupMenu => tokens.PopupRadius,
            _ => tokens.InnerDropdownRadius
        };
        return new RoundedSurface(tokens, radius, content);
    }
}

public static class ExampleRows
{
    public static Border CreateGroup(UiTokens tokens)
    {
        var padding = new Thickness(4);
        var paintInset = new Thickness(0);
        var group = new Border
        {
            Background = new SolidColorBrush(tokens.Background),
            CornerRadius = new CornerRadius(tokens.DialogRadius), Padding = padding
        };
        var rows = new StackPanel { Spacing = 2 };
        var ancestor = RoundedGeometry.EqualInsetAncestor(tokens.DialogRadius,
            padding.Left + paintInset.Left, padding.Top + paintInset.Top);
        var first = new FeedbackButton(tokens, FeedbackKind.SettingsCardRow);
        first.SetRowText("设备设置", "查看并管理连接选项");
        first.SetCornerContext(new CornerContext(CornerKind.Adjacent, Corners.Top, ancestor), paintInset);
        var middle = new FeedbackButton(tokens, FeedbackKind.SettingsCardRow);
        middle.SetRowText("投屏参数");
        middle.SetCornerContext(new CornerContext(CornerKind.GroupCap,
            GroupRadius: Math.Max(tokens.DialogRadius - padding.Left, 0)), paintInset);
        var last = new FeedbackButton(tokens, FeedbackKind.SettingsCardRow);
        last.SetRowText("会话设置");
        last.SetCornerContext(new CornerContext(CornerKind.Adjacent, Corners.Bottom, ancestor), paintInset);
        rows.Children.Add(first);
        rows.Children.Add(middle);
        rows.Children.Add(last);
        group.Child = rows;
        group.SizeChanged += (_, _) => group.Clip = new RectangleGeometry
        {
            Rect = new Rect(group.Bounds.Size), RadiusX = tokens.DialogRadius, RadiusY = tokens.DialogRadius
        };
        return group;
    }
}

public sealed class MenuSequence
{
    private readonly HashSet<long> _pointers = new();
    private bool _escapeHeld;
    public bool IsDismissed { get; private set; }
    public bool HasActive => _pointers.Count > 0 || _escapeHeld;
    public bool MayHide => IsDismissed && !HasActive;
    public void Press(long pointerId) => _pointers.Add(pointerId);
    public void Release(long pointerId, bool stillPressed) { if (!stillPressed) _pointers.Remove(pointerId); }
    public void HoldEscape() => _escapeHeld = true;
    public void ReleaseEscape() => _escapeHeld = false;
    public void Dismiss() => IsDismissed = true;
    public void Cancel() { _pointers.Clear(); _escapeHeld = false; IsDismissed = true; }
    public void Reset() { _pointers.Clear(); _escapeHeld = false; IsDismissed = false; }
}

public sealed class MenuDismissLayer : Grid
{
    private readonly RoundedSurface _menu;
    private readonly MenuSequence _sequence = new();
    private readonly Dictionary<long, IPointer> _pointers = new();
    private readonly List<Visual> _visibilityParents = new();
    private WindowBase? _window;
    private bool _changingCapture;
    private bool _cancelling;
    public event Action? Dismissed;
    public event Action? Closed;

    public MenuDismissLayer(Grid host, RoundedSurface menu)
    {
        _menu = menu;
        Background = Brushes.Transparent;
        Focusable = true;
        IsVisible = false;
        Children.Add(menu);
        host.Children.Add(this);
        ZIndex = int.MaxValue;
        Grid.SetRowSpan(this, Math.Max(1, host.RowDefinitions.Count));
        Grid.SetColumnSpan(this, Math.Max(1, host.ColumnDefinitions.Count));
        AddHandler(PointerPressedEvent, OnPress, RoutingStrategies.Tunnel, true);
        AddHandler(PointerReleasedEvent, OnRelease, RoutingStrategies.Tunnel, true);
        AddHandler(PointerMovedEvent, OnMove, RoutingStrategies.Tunnel, true);
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel, true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, true);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, true);
    }

    public bool ShowAt(Rect geometry)
    {
        if (_sequence.IsDismissed && _sequence.HasActive) return false;
        _sequence.Reset();
        _pointers.Clear();
        _menu.HorizontalAlignment = HorizontalAlignment.Left;
        _menu.VerticalAlignment = VerticalAlignment.Top;
        _menu.Margin = new Thickness(geometry.X, geometry.Y, 0, 0);
        _menu.Width = geometry.Width;
        _menu.Height = geometry.Height;
        _menu.IsVisible = true;
        IsVisible = true;
        Focus();
        return true;
    }

    public void CloseMenu() { DismissMenu(); CaptureActivePointers(); Finish(); }

    private bool Inside(PointerEventArgs e) => _menu.IsVisible
        && RoundedGeometry.Contains(_menu.Body.Bounds.Size, _menu.Radius, e.GetPosition(_menu.Body));

    private static bool AnyButtonPressed(PointerPointProperties properties) =>
        properties.IsLeftButtonPressed || properties.IsMiddleButtonPressed || properties.IsRightButtonPressed
        || properties.IsXButton1Pressed || properties.IsXButton2Pressed;

    private void OnPress(object? sender, PointerPressedEventArgs e)
    {
        _sequence.Press(e.Pointer.Id);
        _pointers[e.Pointer.Id] = e.Pointer;
        if (!_sequence.IsDismissed && Inside(e)) return;
        DismissMenu();
        CaptureActivePointers();
        e.Handled = true;
    }

    private void OnRelease(object? sender, PointerReleasedEventArgs e)
    {
        var stillPressed = e.Pointer.Type == PointerType.Mouse && AnyButtonPressed(e.GetCurrentPoint(this).Properties);
        _sequence.Release(e.Pointer.Id, stillPressed);
        if (!stillPressed) _pointers.Remove(e.Pointer.Id);
        if (!_sequence.IsDismissed) return;
        e.Handled = true;
        if (!stillPressed && e.Pointer.Captured == this)
        {
            _changingCapture = true;
            e.Pointer.Capture(null);
            _changingCapture = false;
        }
        Finish();
    }

    private void OnMove(object? sender, PointerEventArgs e) { if (_sequence.IsDismissed) e.Handled = true; }
    private void OnWheel(object? sender, PointerWheelEventArgs e) { if (_sequence.IsDismissed || !Inside(e)) e.Handled = true; }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _sequence.HoldEscape();
            DismissMenu();
            CaptureActivePointers();
            Focus();
            e.Handled = true;
        }
        else if (_sequence.IsDismissed) e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        _sequence.ReleaseEscape();
        if (!_sequence.IsDismissed) return;
        e.Handled = true;
        Finish();
    }

    private void DismissMenu()
    {
        if (_sequence.IsDismissed) return;
        _sequence.Dismiss();
        _menu.IsVisible = false;
        Dismissed?.Invoke();
    }

    private void CaptureActivePointers()
    {
        _changingCapture = true;
        foreach (var pointer in _pointers.Values) pointer.Capture(this);
        _changingCapture = false;
    }

    private void Finish()
    {
        if (!_sequence.MayHide || !IsVisible) return;
        IsVisible = false;
        Closed?.Invoke();
    }

    private void Cancel()
    {
        if (_cancelling) return;
        _cancelling = true;
        DismissMenu();
        _sequence.Cancel();
        _changingCapture = true;
        foreach (var pointer in _pointers.Values)
            if (pointer.Captured == this) pointer.Capture(null);
        _pointers.Clear();
        _changingCapture = false;
        Finish();
        _cancelling = false;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (!_changingCapture) Cancel();
        base.OnPointerCaptureLost(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_menu is null) return;
        if (change.Property == IsVisibleProperty && change.NewValue is false) Cancel();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var ancestor in this.GetVisualAncestors())
        {
            _visibilityParents.Add(ancestor);
            ancestor.PropertyChanged += OnAncestorChanged;
        }
        _window = TopLevel.GetTopLevel(this) as WindowBase;
        if (_window is not null) _window.Deactivated += OnDeactivated;
    }

    private void OnDeactivated(object? sender, EventArgs e) => Cancel();
    private void OnAncestorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && e.NewValue is false) Cancel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Cancel();
        foreach (var ancestor in _visibilityParents) ancestor.PropertyChanged -= OnAncestorChanged;
        _visibilityParents.Clear();
        if (_window is not null) _window.Deactivated -= OnDeactivated;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }
}
```

API 依据：[Button](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Controls/Button.cs)、[TransitioningContentControl](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Controls/TransitioningContentControl.cs)、[TransformOperationsTransition](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Base/Animation/Transitions/TransformOperationsTransition.cs)、[Visual](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Base/Visual.cs)。

## 项目接入

按 AGENTS 的实际框架版本、组件依赖、主题来源与控件定位约束接入示例，优先复用现有样式和输入路径；系统鼠标、焦点和全局输入按 app-debug 的授权边界执行。
