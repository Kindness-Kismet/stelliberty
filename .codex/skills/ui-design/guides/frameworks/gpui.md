# Rust + GPUI

适配 gpui-pre 0.3.9，Cargo 将依赖别名设为 gpui。UiTokens::new 接收 ThemePalette 和 ButtonGeometry，主题变化通过组件 set_tokens 更新。FeedbackButton 使用真实鼠标、键盘、焦点与窗口事件；CrossfadeTabs::select(index,window,cx) 将焦点移至对应标签。

GPUI 的 Div 没有通用 transform/scale。这里固定外层命中区域，让 ContentBuilder 按 scale 同步重建宽高、圆角、字号和图标几何；text_content 与 row_content 是完整实现。自定义内容遵守同一 builder 契约。PageBuilder 的 active 参数用于关闭退出页控件及焦点。复合行的 Luminance 轻度处理悬停底色与图标等非文字装饰；标题、副标题保持各自主题颜色。GPUI 没有通用的位图整图滤镜，位图需独立图像绘制 adapter，不能把单色 tint 当作整图处理。

BoxShadow 使用浮点 alpha 0.25。阴影放在外层，圆角裁剪放在内层。rounded_surface 与 rounded_surface_in 返回浮层视觉容器；阴影参数映射到 GPUI 原生 primitive，跨框架不要求逐像素相同。

优先复用项目已有组件库的样式和输入，只调整明确差异；缺少动画时使用所附包装。普通按钮按 ButtonRole 分为 Plain、Dismiss、Primary。Plain 使用主题前景、透明底色和无 hover 底色；Dismiss 使用中性填充；Primary 使用主题强调色。

surface 输入为当前已合成的不透明 sRGB 颜色。8% mix 在 sRGB 编码通道执行 base × (1 − a) + overlay × a，alpha 先合成，再按 8 bit 四舍五入。强调色先合成到 surface。文字对比度另外使用线性化 sRGB 相对亮度，分别计算 #000000 和 #FFFFFF 的对比度并选择较高者；Dismiss、Primary 的 hover 中间帧也按实际背景重算文字，普通字号目标至少 4.5。

按压目标 0.94、释放目标 1.0，180 ms；曲线用 x 二分 16 次求 cubic-bezier(0.34,1.56,0.64,1) 的 y，保留 overshoot。取消、失焦、隐藏、禁用复位，不改变外层命中区域。

无真实圆角祖先时普通行圆角 10、菜单行圆角 7。equal_inset_ancestor(R,dx,dy) 仅接受非负等距内缩；Adjacent 只约束声明的相邻角，半径 max(R − d,0)，其余角采用组件默认半径。GroupCap 显式继承同组派生上限。宿主须通过实际等距 padding d 提供对应行 bounds；有 context 时背景 inset 为 0，不再叠加 Navigation 的 8,2,8,2。非等距布局不宣称严格同心。

无祖先 NavigationRow 使用 navigation_hover，背景内缩 8,2,8,2；SettingsCardRow 的 hover 覆盖整行，亮主题 #eff2f7、暗主题 #2d2d2d，可用主题值替换。MenuRow 使用整行 hover 和菜单默认圆角。详细行最小高度 58，副标题随宽度换行后增加高度。按压时悬停底色轻度加深，标题和副标题保持原值，hover 的 72 ms alpha 过渡继续独立工作。悬停底色与 leading/trailing 图标等非文字装饰接收反馈 level，普通与 hover 为 0，pressed 为 1；按压 30 ms、恢复 72 ms，easeOutCubic，局部颜色默认 brightness .92、contrast 1.12。没有装饰的行保留轻度底色反馈。

row_content_with_adornments 接收可选 leading/trailing RowAdornment；row_adornment 将主题 palette 中每个颜色按同一 level 量化，content 只用处理后的 palette 绘制局部元素。彩色圆形加白色加号可登记 [圆底色,0xffffffff]，2部分分别使用对应颜色；单色 SVG 将处理后的颜色传给其 tint。多色 SVG 或位图的整图处理交给局部绘制 adapter，标题和副标题不加滤镜。旧 row_content 保留无装饰入口。

NavigationRow 使用 navigation_hover，文字 Tab 使用独立的 text_tab_hover 主题键；UiTokens::new 从 ThemePalette.surface 叠 8% 最高对比度黑/白派生默认颜色，两者默认同色、之后各自可覆盖，宿主覆写 background 保留这2个字段。已有组件库分别注入对应角色的 hover 键。按压对自己的文字 Tab 悬停色轻度加深，标题保持原色，混合 Tab 的图标继续应用局部反馈；背景内缩 4、圆角 7，状态变化立即更新。持久选中项按业务 selected 目标屏蔽 hover 背景，未选中项保留 hover；混合 Tab 图标的按压反馈继续生效，使用 30 ms 按压和 72 ms 恢复。纯图标 Tab 背景透明；TextWithIcon 与 TextWithAdornment 的非空标题按文字型绘制，空标题按纯图标处理且不增加按压反馈。选中权重与页面以 160 ms linear 交叉渐隐渐显，快速切换从当前权重接续。标题先按实际背景校正选中端点，再从 normal 向校正后的 selected 混色；最终不透明文字仍检查 4.5。底部指示线厚 2、端点半径 1，处于背景 inset 外；不透明基色对 surface 检查 3，显示 alpha 跟随权重，透明过渡帧不要求达到 3。PageBuilder 应返回保持实例的页面 Entity，active=false 时关闭退出页输入和焦点。

CrossfadeTabs 的 select 在同一 Instant 从全部当前权重接续；render 用同一次 Instant 采样 headers、指示线和内容。transition_snapshot 返回最近一次 render 保存的 selected、weights、sampled_at、duration 与 running；读取快照不会推进动画，可将 sampled_at 与实际截图关联。

DismissibleMenu Entity 始终挂在窗口原点的全尺寸 overlay 宿主。open 接收窗口坐标中的真实圆角表面 bounds；阴影区域不算内部。外部 down 立即隐藏 body 并通知 on_dismiss，shield 保留至全部按钮 up，期间消费 down/move/up/wheel；on_dismiss 只通知，不能移除 Entity。菜单内部 down 正常交给菜单内容，程序关闭或 Escape 时仍有鼠标按钮按住则进入 Draining；窗口失活或隐藏直接清除记录并关闭。Canvas paint 阶段注册 Window::on_mouse_event Capture，.occlude() 阻断底层 hover；不能用 div capture_any_mouse_down 冒充全局监听。

Dialog、PopupMenu、InnerDropdown 默认圆角分别 16、12、8，可通过 CornerContext 派生。所有浮层共用 blur 5、offset (0,1)、rgba(0,0,0,0.25)、spread 0 的外阴影；普通按钮、行项、标签无阴影。示例固定 1x 动画速度，接入动态变速时统一替换 Motion 的公共时钟。

```rust
use gpui::{
    AnyElement, App, Bounds, BoxShadow, Context, DispatchPhase, FocusHandle, InteractiveElement,
    IntoElement, KeyDownEvent, KeyUpEvent, MouseButton, MouseDownEvent, MouseMoveEvent,
    MouseUpEvent, ParentElement, Pixels, Point, Render, ScrollWheelEvent, SharedString,
    StatefulInteractiveElement, Styled, Subscription, Window, canvas, div, hsla, px, rgba,
};
use std::{
    rc::Rc,
    time::{Duration, Instant},
};

#[derive(Clone)]
pub struct UiTokens {
    pub press_scale: f32,
    pub press_duration: Duration,
    pub row_press_duration: Duration,
    pub row_hover_duration: Duration,
    pub hover_level: f32,
    pub crossfade_duration: Duration,
    pub button_width: f32,
    pub button_height: f32,
    pub font_size: f32,
    pub button_radius: f32,
    pub row_radius: f32,
    pub row_corner_context: CornerContext,
    pub menu_row_radius: f32,
    pub dialog_radius: f32,
    pub popup_radius: f32,
    pub inner_dropdown_radius: f32,
    pub foreground: u32,
    pub background: u32,
    pub accent: u32,
    pub row_title: u32,
    pub row_subtitle: u32,
    pub settings_hover: u32,
    pub navigation_hover: u32,
    pub text_tab_hover: u32,
    pub surface_shadow: Vec<BoxShadow>,
}

pub struct ThemePalette {
    pub surface: u32,
    pub foreground: u32,
    pub accent: u32,
    pub is_dark: bool,
    pub row_title: u32,
    pub row_subtitle: u32,
}
pub struct ButtonGeometry {
    pub width: f32,
    pub height: f32,
    pub font_size: f32,
    pub radius: f32,
}
impl UiTokens {
    pub fn new(palette: ThemePalette, geometry: ButtonGeometry) -> Self {
        let navigation_hover = over(contrast_text(palette.surface), palette.surface, 0.08);
        Self {
            press_scale: 0.94,
            press_duration: Duration::from_millis(180),
            row_press_duration: Duration::from_millis(30),
            row_hover_duration: Duration::from_millis(72),
            hover_level: 0.0,
            crossfade_duration: Duration::from_millis(160),
            button_width: geometry.width,
            button_height: geometry.height,
            font_size: geometry.font_size,
            button_radius: geometry.radius,
            row_radius: 10.0,
            row_corner_context: CornerContext::None,
            menu_row_radius: 7.0,
            dialog_radius: 16.0,
            popup_radius: 12.0,
            inner_dropdown_radius: 8.0,
            foreground: palette.foreground,
            background: palette.surface,
            accent: palette.accent,
            row_title: palette.row_title,
            row_subtitle: palette.row_subtitle,
            settings_hover: if palette.is_dark {
                0x2d2d2dff
            } else {
                0xeff2f7ff
            },
            navigation_hover,
            text_tab_hover: navigation_hover,
            surface_shadow: vec![
                BoxShadow::new(px(0.0), px(1.0), hsla(0.0, 0.0, 0.0, 0.25)).blur_radius(px(5.0)),
            ],
        }
    }
}

pub fn press_curve(x: f32) -> f32 {
    let cubic = |t: f32, a: f32, b: f32| {
        3.0 * (1.0 - t).powi(2) * t * a + 3.0 * (1.0 - t) * t.powi(2) * b + t.powi(3)
    };
    let (mut lo, mut hi) = (0.0, 1.0);
    for _ in 0..16 {
        let mid = (lo + hi) * 0.5;
        if cubic(mid, 0.34, 0.64) < x {
            lo = mid;
        } else {
            hi = mid;
        }
    }
    cubic((lo + hi) * 0.5, 1.56, 1.0)
}

pub fn ease_out_cubic(x: f32) -> f32 {
    1.0 - (1.0 - x).powi(3)
}

#[derive(Clone, Copy, PartialEq)]
pub enum ButtonRole {
    Plain,
    Dismiss,
    Primary,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct RoundedAncestor {
    pub radius: f32,
    pub inset: f32,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub enum CornerAnchor {
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

#[derive(Clone, Copy, Debug, PartialEq, Default)]
pub enum CornerContext {
    #[default]
    None,
    Adjacent(CornerAnchor, RoundedAncestor),
    GroupCap(f32),
}

pub fn equal_inset_ancestor(radius: f32, dx: f32, dy: f32) -> Option<RoundedAncestor> {
    if radius < 0.0 || dx < 0.0 || dy < 0.0 || dx != dy {
        return None;
    }
    Some(RoundedAncestor { radius, inset: dx })
}

pub fn derived_radius(context: CornerContext, fallback: f32) -> f32 {
    match context {
        CornerContext::None => fallback,
        CornerContext::Adjacent(_, ancestor) => (ancestor.radius - ancestor.inset).max(0.0),
        CornerContext::GroupCap(radius) => radius.max(0.0),
    }
}

fn corner_radii(context: CornerContext, fallback: f32) -> [f32; 4] {
    let mut radii = [fallback; 4];
    match context {
        CornerContext::None => {}
        CornerContext::GroupCap(radius) => radii = [radius.max(0.0); 4],
        CornerContext::Adjacent(anchor, ancestor) => {
            let index = match anchor {
                CornerAnchor::TopLeft => 0,
                CornerAnchor::TopRight => 1,
                CornerAnchor::BottomRight => 2,
                CornerAnchor::BottomLeft => 3,
            };
            radii[index] = (ancestor.radius - ancestor.inset).max(0.0);
        }
    }
    radii
}

pub fn over(foreground: u32, background: u32, opacity: f32) -> u32 {
    let alpha = (foreground & 255) as f32 / 255.0 * opacity;
    let channel = |shift: u32| {
        (((foreground >> shift) & 255) as f32 * alpha
            + ((background >> shift) & 255) as f32 * (1.0 - alpha))
            .round() as u32
    };
    (channel(24) << 24) | (channel(16) << 16) | (channel(8) << 8) | 255
}

pub fn contrast_text(background: u32) -> u32 {
    let linear = |shift: u32| {
        let value = ((background >> shift) & 255) as f32 / 255.0;
        if value <= 0.04045 {
            value / 12.92
        } else {
            ((value + 0.055) / 1.055).powf(2.4)
        }
    };
    let luminance = 0.2126 * linear(24) + 0.7152 * linear(16) + 0.0722 * linear(8);
    if (luminance + 0.05) / 0.05 >= 1.05 / (luminance + 0.05) {
        0x000000ff
    } else {
        0xffffffff
    }
}

fn relative_luminance(color: u32) -> f32 {
    let channel = |shift: u32| {
        let c = ((color >> shift) & 255) as f32 / 255.0;
        if c <= 0.04045 {
            c / 12.92
        } else {
            ((c + 0.055) / 1.055).powf(2.4)
        }
    };
    0.2126 * channel(24) + 0.7152 * channel(16) + 0.0722 * channel(8)
}

fn contrast_ratio(a: u32, b: u32) -> f32 {
    let a = relative_luminance(a);
    let b = relative_luminance(b);
    (a.max(b) + 0.05) / (a.min(b) + 0.05)
}

pub fn best_contrast_accent(accent: u32, surface: u32, minimum: f32) -> u32 {
    let actual = over(accent, surface, 1.0);
    if contrast_ratio(actual, surface) >= minimum {
        actual
    } else {
        contrast_text(surface)
    }
}

pub fn tab_background(tokens: &UiTokens, hovered: bool, pressed: bool, icon: bool) -> (u32, u32) {
    let raw = tokens.text_tab_hover;
    let paint = if icon {
        0
    } else if pressed {
        luminance_color(raw, 1.0)
    } else if hovered {
        raw
    } else {
        0
    };
    (paint, over(paint, tokens.background, 1.0))
}

fn tab_state_background(
    tokens: &UiTokens,
    hovered: bool,
    pressed: bool,
    icon: bool,
    selected: bool,
) -> (u32, u32) {
    tab_background(tokens, hovered && !selected, pressed, icon)
}

pub fn tab_foreground(tokens: &UiTokens, actual_background: u32, weight: f32) -> u32 {
    let normal = over(tokens.foreground, actual_background, 1.0);
    let selected = best_contrast_accent(tokens.accent, actual_background, 4.5);
    let candidate = over(selected, normal, weight.clamp(0.0, 1.0));
    best_contrast_accent(candidate, actual_background, 4.5)
}

// surface 由调用方提供当前已合成的不透明颜色；每帧以实际 hover 背景选文字。
pub fn button_colors(tokens: &UiTokens, role: ButtonRole, hover: f32) -> (u32, u32) {
    let base = match role {
        ButtonRole::Plain => return (0, tokens.foreground),
        ButtonRole::Dismiss => over(contrast_text(tokens.background), tokens.background, 0.08),
        ButtonRole::Primary => over(tokens.accent, tokens.background, 1.0),
    };
    let overlay = contrast_text(base);
    let actual = over(overlay, base, 0.08 * hover);
    (actual, contrast_text(actual))
}

// 输入输出采用 0xRRGGBBAA，滤色在预乘 alpha 通道上执行。
pub fn luminance_color(color: u32, level: f32) -> u32 {
    if level == 0.0 {
        return color;
    }
    let alpha = (color & 255) as f32;
    if alpha == 0.0 {
        return 0;
    }
    let brightness = 1.0 - 0.08 * level;
    let contrast = 1.0 + 0.12 * level.max(0.0);
    let channel = |shift: u32| {
        let value = (((color >> shift) & 255) as f32 * alpha / 255.0).round();
        let filtered = ((value * brightness - alpha / 2.0) * contrast + alpha / 2.0)
            .round()
            .clamp(0.0, alpha);
        (filtered * 255.0 / alpha).round() as u32
    };
    (channel(24) << 24) | (channel(16) << 16) | (channel(8) << 8) | alpha as u32
}

#[derive(Clone, Copy)]
struct Motion {
    from: f32,
    to: f32,
    started: Instant,
}

impl Motion {
    fn fixed(value: f32) -> Self {
        Self {
            from: value,
            to: value,
            started: Instant::now(),
        }
    }
    fn sample(&self, duration: Duration, easing: fn(f32) -> f32) -> (f32, bool) {
        self.sample_at(Instant::now(), duration, easing)
    }
    fn sample_at(
        &self,
        sampled_at: Instant,
        duration: Duration,
        easing: fn(f32) -> f32,
    ) -> (f32, bool) {
        if self.from == self.to {
            return (self.to, false);
        }
        if duration.is_zero() {
            return (self.to, false);
        }
        let t = (sampled_at.duration_since(self.started).as_secs_f32() / duration.as_secs_f32())
            .min(1.0);
        if t == 1.0 {
            return (self.to, false);
        }
        (self.from + (self.to - self.from) * easing(t), true)
    }
    fn retarget(&mut self, target: f32, duration: Duration, easing: fn(f32) -> f32) {
        self.retarget_at(Instant::now(), target, duration, easing);
    }
    fn retarget_at(
        &mut self,
        sampled_at: Instant,
        target: f32,
        duration: Duration,
        easing: fn(f32) -> f32,
    ) {
        let current = self.sample_at(sampled_at, duration, easing).0;
        *self = Self {
            from: current,
            to: target,
            started: sampled_at,
        };
    }
}

#[derive(Clone, Copy, PartialEq)]
pub enum FeedbackKind {
    Scale,
    NavigationRow,
    SettingsCardRow,
    MenuRow,
    Tab,
}

impl FeedbackKind {
    fn is_row(self) -> bool {
        matches!(
            self,
            Self::NavigationRow | Self::SettingsCardRow | Self::MenuRow
        )
    }
}

pub type ContentBuilder = Rc<dyn Fn(f32, f32, f32, ButtonRole, &UiTokens) -> AnyElement>;
pub type ClickHandler = Rc<dyn Fn(&mut Window, &mut App)>;
pub type RowAdornment = Rc<dyn Fn(f32, &UiTokens) -> AnyElement>;

pub fn adornment_colors(colors: &[u32], level: f32) -> Vec<u32> {
    colors
        .iter()
        .map(|color| luminance_color(*color, level))
        .collect()
}

pub fn row_adornment(
    palette: Rc<dyn Fn(&UiTokens) -> Vec<u32>>,
    content: Rc<dyn Fn(&[u32]) -> AnyElement>,
) -> RowAdornment {
    Rc::new(move |level, tokens| content(&adornment_colors(&palette(tokens), level)))
}

pub fn row_colors(
    tokens: &UiTokens,
    kind: FeedbackKind,
    hover: f32,
    level: f32,
) -> (u32, u32, u32) {
    let raw = match kind {
        FeedbackKind::NavigationRow => tokens.navigation_hover,
        FeedbackKind::MenuRow => over(contrast_text(tokens.background), tokens.background, 0.08),
        _ => tokens.settings_hover,
    };
    let filtered = luminance_color(raw, level);
    let alpha = ((filtered & 255) as f32 * hover.clamp(0.0, 1.0)).round() as u32;
    (
        (filtered & 0xffffff00) | alpha,
        tokens.row_title,
        tokens.row_subtitle,
    )
}

pub struct FeedbackButton {
    tokens: UiTokens,
    kind: FeedbackKind,
    role: ButtonRole,
    content: ContentBuilder,
    click: ClickHandler,
    focus: FocusHandle,
    is_enabled: bool,
    is_visible: bool,
    is_pressed: bool,
    pointer_held: bool,
    key_held: bool,
    is_hovered: bool,
    corner_context: CornerContext,
    scale: Motion,
    level: Motion,
    hover: Motion,
    level_duration: Duration,
    _subscriptions: Vec<Subscription>,
}

impl FeedbackButton {
    pub fn new(
        tokens: UiTokens,
        kind: FeedbackKind,
        role: ButtonRole,
        content: ContentBuilder,
        click: ClickHandler,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> Self {
        let focus = cx.focus_handle();
        let blur = cx.on_blur(&focus, window, |view, _, cx| view.cancel_press(cx));
        let activation = cx.observe_window_activation(window, |view, window, cx| {
            if !window.is_window_active() {
                view.reset(cx);
            }
        });
        let visibility = cx.observe_window_visibility(window, |view, _, _, cx| view.reset(cx));
        let level_duration = tokens.row_hover_duration;
        Self {
            tokens,
            kind,
            role,
            content,
            click,
            focus,
            is_enabled: true,
            is_visible: true,
            is_pressed: false,
            pointer_held: false,
            key_held: false,
            is_hovered: false,
            corner_context: CornerContext::None,
            scale: Motion::fixed(1.0),
            level: Motion::fixed(0.0),
            hover: Motion::fixed(0.0),
            level_duration,
            _subscriptions: vec![blur, activation, visibility],
        }
    }

    pub fn set_state(&mut self, is_enabled: bool, is_visible: bool, cx: &mut Context<Self>) {
        self.is_enabled = is_enabled;
        self.is_visible = is_visible;
        if !is_enabled || !is_visible {
            self.reset(cx);
        }
        cx.notify();
    }

    pub fn set_tokens(&mut self, tokens: UiTokens, cx: &mut Context<Self>) {
        self.level_duration = tokens.row_hover_duration;
        self.tokens = tokens;
        self.cancel_press(cx);
    }

    pub fn set_corner_context(&mut self, context: CornerContext, cx: &mut Context<Self>) {
        self.corner_context = context;
        cx.notify();
    }

    fn reset(&mut self, cx: &mut Context<Self>) {
        self.cancel_press(cx);
        self.is_hovered = false;
        self.hover = Motion::fixed(0.0);
        cx.notify();
    }

    fn cancel_press(&mut self, cx: &mut Context<Self>) {
        self.is_pressed = false;
        self.pointer_held = false;
        self.key_held = false;
        self.scale = Motion::fixed(1.0);
        self.level = Motion::fixed(0.0);
        cx.notify();
    }

    fn press(&mut self, value: bool, cx: &mut Context<Self>) {
        if self.is_pressed == value {
            cx.notify();
            return;
        }
        self.is_pressed = value;
        let target = if value { self.tokens.press_scale } else { 1.0 };
        if self.kind == FeedbackKind::Scale {
            self.scale
                .retarget(target, self.tokens.press_duration, press_curve);
        }
        self.update_level(cx);
    }

    fn update_level(&mut self, cx: &mut Context<Self>) {
        if !self.kind.is_row() {
            cx.notify();
            return;
        }
        let target = if self.is_pressed {
            1.0
        } else if self.is_hovered {
            self.tokens.hover_level
        } else {
            0.0
        };
        let current = self.level.sample(self.level_duration, ease_out_cubic).0;
        self.level_duration = if self.is_pressed {
            self.tokens.row_press_duration
        } else {
            self.tokens.row_hover_duration
        };
        self.level = Motion {
            from: current,
            to: target,
            started: Instant::now(),
        };
        cx.notify();
    }
}

impl Render for FeedbackButton {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        if !self.is_visible {
            return div().into_any_element();
        }
        let (scale, scale_running) = self.scale.sample(self.tokens.press_duration, press_curve);
        let (level, level_running) = self.level.sample(self.level_duration, ease_out_cubic);
        let (hover, hover_running) = self
            .hover
            .sample(self.tokens.row_hover_duration, ease_out_cubic);
        if scale_running || level_running || hover_running {
            window.request_animation_frame();
        }
        let scale = if self.kind == FeedbackKind::Scale {
            scale
        } else {
            1.0
        };
        let level = if self.kind.is_row() { level } else { 0.0 };
        let hover = if self.kind.is_row() || self.kind == FeedbackKind::Scale {
            hover
        } else {
            0.0
        };
        let mut tokens = self.tokens.clone();
        let fallback = if self.kind == FeedbackKind::MenuRow {
            tokens.menu_row_radius
        } else {
            tokens.row_radius
        };
        tokens.row_radius = fallback;
        tokens.row_corner_context = self.corner_context;
        let content = (self.content)(scale, level, hover, self.role, &tokens);
        let element = div()
            .id("feedback-hitbox")
            .track_focus(&self.focus)
            .w(px(self.tokens.button_width))
            .flex()
            .items_center()
            .justify_center()
            .child(content)
            .on_hover(cx.listener(|view, hovered, _, cx| {
                view.is_hovered = *hovered && view.is_enabled;
                if view.kind.is_row()
                    || view.kind == FeedbackKind::Scale && view.role != ButtonRole::Plain
                {
                    view.hover.retarget(
                        if view.is_hovered { 1.0 } else { 0.0 },
                        view.tokens.row_hover_duration,
                        ease_out_cubic,
                    );
                }
                let pressed = view.key_held || view.pointer_held && view.is_hovered;
                view.press(pressed, cx);
            }))
            .on_mouse_down(
                MouseButton::Left,
                cx.listener(|view, _, window, cx| {
                    if !view.is_enabled {
                        return;
                    }
                    window.focus(&view.focus, cx);
                    view.pointer_held = true;
                    view.is_hovered = true;
                    view.press(true, cx);
                }),
            )
            .on_mouse_up(
                MouseButton::Left,
                cx.listener(|view, _, window, cx| {
                    let activate = view.pointer_held && view.is_pressed && view.is_enabled;
                    view.pointer_held = false;
                    view.press(view.key_held, cx);
                    if activate {
                        (view.click)(window, cx);
                    }
                }),
            )
            .on_mouse_up_out(
                MouseButton::Left,
                cx.listener(|view, _, _, cx| {
                    view.pointer_held = false;
                    view.press(view.key_held, cx);
                }),
            )
            .on_key_down(cx.listener(|view, event: &KeyDownEvent, _, cx| {
                if !view.is_enabled || event.is_held {
                    return;
                }
                match event.keystroke.key.as_str() {
                    "enter" | "space" => {
                        view.key_held = true;
                        view.press(true, cx);
                    }
                    "escape" => view.cancel_press(cx),
                    _ => return,
                }
                cx.stop_propagation();
            }))
            .on_key_up(cx.listener(|view, event: &KeyUpEvent, window, cx| {
                if !matches!(event.keystroke.key.as_str(), "enter" | "space") {
                    return;
                }
                let activate = view.key_held && view.is_enabled;
                view.key_held = false;
                view.press(view.pointer_held && view.is_hovered, cx);
                if activate {
                    (view.click)(window, cx);
                }
                cx.stop_propagation();
            }));
        if self.kind.is_row() {
            element
                .min_h(px(self.tokens.button_height))
                .into_any_element()
        } else {
            element.h(px(self.tokens.button_height)).into_any_element()
        }
    }
}

// 自定义内容须同步缩放几何尺寸；图标也使用同一 level 处理颜色。
pub fn text_content(text: SharedString, kind: FeedbackKind) -> ContentBuilder {
    Rc::new(move |scale, level, hover, role, tokens| match kind {
        FeedbackKind::NavigationRow | FeedbackKind::SettingsCardRow | FeedbackKind::MenuRow => {
            row_content(text.clone(), None, kind)(scale, level, hover, role, tokens)
        }
        FeedbackKind::Scale | FeedbackKind::Tab => {
            let (background, foreground) = if kind == FeedbackKind::Tab {
                (0, tokens.foreground)
            } else {
                button_colors(tokens, role, hover)
            };
            div()
                .w(px(tokens.button_width * scale))
                .h(px(tokens.button_height * scale))
                .rounded(px(tokens.button_radius * scale))
                .bg(rgba(background))
                .text_color(rgba(foreground))
                .text_size(px(tokens.font_size * scale))
                .flex()
                .items_center()
                .justify_center()
                .child(text.clone())
                .into_any_element()
        }
    })
}

pub fn row_content(
    title: SharedString,
    subtitle: Option<SharedString>,
    kind: FeedbackKind,
) -> ContentBuilder {
    row_content_with_adornments(title, subtitle, kind, None, None)
}

pub fn row_content_with_adornments(
    title: SharedString,
    subtitle: Option<SharedString>,
    kind: FeedbackKind,
    leading: Option<RowAdornment>,
    trailing: Option<RowAdornment>,
) -> ContentBuilder {
    Rc::new(move |_, level, hover, _, tokens| {
        let (mut x, mut y) = if kind == FeedbackKind::NavigationRow {
            (8.0, 2.0)
        } else {
            (0.0, 0.0)
        };
        if tokens.row_corner_context != CornerContext::None {
            x = 0.0;
            y = 0.0;
        }
        let radii = corner_radii(tokens.row_corner_context, tokens.row_radius);
        let (background, title_color, subtitle_color) = row_colors(tokens, kind, hover, level);
        let mut text = div()
            .relative()
            .flex()
            .flex_col()
            .flex_1()
            .min_w_0()
            .gap(px(4.0))
            .text_size(px(tokens.font_size))
            .text_color(rgba(title_color))
            .child(title.clone());
        if let Some(subtitle) = &subtitle {
            text = text.child(
                div()
                    .text_color(rgba(subtitle_color))
                    .child(subtitle.clone()),
            );
        }
        let mut content = div()
            .relative()
            .w_full()
            .min_w_0()
            .flex()
            .items_center()
            .px(px(18.0))
            .py(px(10.0))
            .gap(px(12.0));
        if let Some(leading) = &leading {
            content = content.child(div().flex_shrink_0().child(leading(level, tokens)));
        }
        content = content.child(text);
        if let Some(trailing) = &trailing {
            content = content.child(div().flex_shrink_0().child(trailing(level, tokens)));
        }
        div()
            .relative()
            .w(px(tokens.button_width))
            .min_h(px(if subtitle.is_some() {
                58.0
            } else {
                tokens.button_height
            }))
            .flex()
            .items_center()
            .justify_center()
            .child(
                div()
                    .absolute()
                    .left(px(x))
                    .top(px(y))
                    .right(px(x))
                    .bottom(px(y))
                    .rounded_tl(px(radii[0]))
                    .rounded_tr(px(radii[1]))
                    .rounded_br(px(radii[2]))
                    .rounded_bl(px(radii[3]))
                    .bg(rgba(background)),
            )
            .child(content)
            .into_any_element()
    })
}

pub type PageBuilder = Rc<dyn Fn(usize, bool) -> AnyElement>;

#[derive(Clone)]
pub enum TabLabel {
    Text(SharedString),
    Icon(Rc<dyn Fn(u32) -> AnyElement>),
    TextWithIcon(SharedString, Rc<dyn Fn(u32) -> AnyElement>),
    TextWithAdornment(SharedString, RowAdornment),
}

#[derive(Clone, Copy)]
struct TabFeedback {
    level: Motion,
    duration: Duration,
}

#[derive(Clone, Copy, Default)]
struct TabInput {
    hovered: bool,
    pointer_held: bool,
    key_held: bool,
}

#[derive(Clone, Debug)]
pub struct TabTransitionSnapshot {
    pub selected: usize,
    pub weights: Vec<f32>,
    pub sampled_at: Instant,
    pub duration: Duration,
    pub running: bool,
}

pub struct CrossfadeTabs {
    tokens: UiTokens,
    labels: Vec<TabLabel>,
    focus: Vec<FocusHandle>,
    input: Vec<TabInput>,
    feedback: Vec<TabFeedback>,
    selected: usize,
    weights: Vec<Motion>,
    rendered_transition: TabTransitionSnapshot,
    page: PageBuilder,
    _subscriptions: Vec<Subscription>,
}

impl CrossfadeTabs {
    pub fn new(
        tokens: UiTokens,
        titles: Vec<SharedString>,
        page: PageBuilder,
        cx: &mut Context<Self>,
    ) -> Self {
        Self::new_with_tabs(
            tokens,
            titles.into_iter().map(TabLabel::Text).collect(),
            page,
            cx,
        )
    }

    pub fn new_with_tabs(
        tokens: UiTokens,
        labels: Vec<TabLabel>,
        page: PageBuilder,
        cx: &mut Context<Self>,
    ) -> Self {
        let count = labels.len();
        let initial_weights: Vec<f32> = (0..count)
            .map(|index| if index == 0 { 1.0 } else { 0.0 })
            .collect();
        let rendered_transition = TabTransitionSnapshot {
            selected: 0,
            weights: initial_weights.clone(),
            sampled_at: Instant::now(),
            duration: tokens.crossfade_duration,
            running: false,
        };
        let feedback = vec![
            TabFeedback {
                level: Motion::fixed(0.0),
                duration: tokens.row_hover_duration
            };
            count
        ];
        Self {
            tokens,
            labels,
            focus: (0..count).map(|_| cx.focus_handle()).collect(),
            input: vec![TabInput::default(); count],
            feedback,
            selected: 0,
            page,
            weights: initial_weights.into_iter().map(Motion::fixed).collect(),
            rendered_transition,
            _subscriptions: Vec::new(),
        }
    }

    pub fn select(&mut self, index: usize, window: &mut Window, cx: &mut Context<Self>) {
        if index >= self.weights.len() {
            return;
        }
        if let Some(focus) = self.focus.get(index) {
            window.focus(focus, cx);
        }
        if index == self.selected {
            return;
        }
        self.selected = index;
        let sampled_at = Instant::now();
        for (position, weight) in self.weights.iter_mut().enumerate() {
            weight.retarget_at(
                sampled_at,
                if position == index { 1.0 } else { 0.0 },
                self.tokens.crossfade_duration,
                |x| x,
            );
        }
        cx.notify();
    }

    pub fn set_tokens(&mut self, tokens: UiTokens, cx: &mut Context<Self>) {
        self.tokens = tokens;
        cx.notify();
    }

    pub fn selected(&self) -> usize {
        self.selected
    }

    // 返回最近一次 render 使用的权重，避免截图采样重新推进动画。
    pub fn transition_snapshot(&self) -> TabTransitionSnapshot {
        self.rendered_transition.clone()
    }

    fn sample_transition(&self, sampled_at: Instant) -> TabTransitionSnapshot {
        let samples: Vec<(f32, bool)> = self
            .weights
            .iter()
            .map(|weight| weight.sample_at(sampled_at, self.tokens.crossfade_duration, |x| x))
            .collect();
        TabTransitionSnapshot {
            selected: self.selected,
            weights: samples.iter().map(|(weight, _)| *weight).collect(),
            sampled_at,
            duration: self.tokens.crossfade_duration,
            running: samples.iter().any(|(_, running)| *running),
        }
    }

    fn update_input(
        &mut self,
        index: usize,
        update: impl FnOnce(&mut TabInput),
        cx: &mut Context<Self>,
    ) {
        let Some(input) = self.input.get_mut(index) else {
            return;
        };
        let before = input.key_held || input.pointer_held && input.hovered;
        update(input);
        let pressed = input.key_held || input.pointer_held && input.hovered;
        let has_adornment = matches!(&self.labels[index], TabLabel::TextWithIcon(text, _) | TabLabel::TextWithAdornment(text, _) if !text.is_empty());
        if before != pressed && has_adornment {
            let feedback = &mut self.feedback[index];
            let current = feedback.level.sample(feedback.duration, ease_out_cubic).0;
            feedback.duration = if pressed {
                self.tokens.row_press_duration
            } else {
                self.tokens.row_hover_duration
            };
            feedback.level = Motion {
                from: current,
                to: if pressed { 1.0 } else { 0.0 },
                started: Instant::now(),
            };
        }
        cx.notify();
    }

    fn cancel_input(&mut self, index: usize, cx: &mut Context<Self>) {
        self.input[index].pointer_held = false;
        self.input[index].key_held = false;
        self.feedback[index].level = Motion::fixed(0.0);
        cx.notify();
    }
}

impl Render for CrossfadeTabs {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        if self._subscriptions.is_empty() {
            for (index, focus) in self.focus.clone().iter().enumerate() {
                self._subscriptions
                    .push(cx.on_blur(focus, window, move |view, _, cx| {
                        view.cancel_input(index, cx);
                    }));
            }
            self._subscriptions
                .push(cx.observe_window_activation(window, |view, window, cx| {
                    if !window.is_window_active() {
                        view.input.fill(TabInput::default());
                        for feedback in &mut view.feedback {
                            feedback.level = Motion::fixed(0.0);
                        }
                        cx.notify();
                    }
                }));
            self._subscriptions
                .push(cx.observe_window_visibility(window, |view, _, _, cx| {
                    view.input.fill(TabInput::default());
                    for feedback in &mut view.feedback {
                        feedback.level = Motion::fixed(0.0);
                    }
                    cx.notify();
                }));
        }
        let snapshot = self.sample_transition(Instant::now());
        self.rendered_transition = snapshot.clone();
        if snapshot.running {
            window.request_animation_frame();
        }
        let feedback: Vec<f32> = self
            .feedback
            .iter()
            .map(|feedback| {
                let (level, running) = feedback.level.sample_at(
                    snapshot.sampled_at,
                    feedback.duration,
                    ease_out_cubic,
                );
                if running {
                    window.request_animation_frame();
                }
                level
            })
            .collect();
        let mut headers = div().flex();
        for (index, (((label, focus), input), weight)) in self
            .labels
            .iter()
            .zip(self.focus.iter())
            .zip(self.input.iter())
            .zip(snapshot.weights.iter())
            .enumerate()
        {
            let weight = *weight;
            let icon = match label {
                TabLabel::Icon(_) => true,
                TabLabel::TextWithIcon(text, _) | TabLabel::TextWithAdornment(text, _) => {
                    text.is_empty()
                }
                TabLabel::Text(_) => false,
            };
            let pressed = input.key_held || input.pointer_held && input.hovered;
            let (paint, actual) = tab_state_background(
                &self.tokens,
                input.hovered,
                pressed,
                icon,
                index == snapshot.selected,
            );
            let foreground = tab_foreground(&self.tokens, actual, weight);
            let content = match label {
                TabLabel::Text(text) => div()
                    .text_color(rgba(foreground))
                    .child(text.clone())
                    .into_any_element(),
                TabLabel::Icon(builder) => builder(foreground),
                TabLabel::TextWithIcon(text, builder) => div()
                    .flex()
                    .items_center()
                    .gap(px(8.0))
                    .text_color(rgba(foreground))
                    .child(builder(luminance_color(foreground, feedback[index])))
                    .child(text.clone())
                    .into_any_element(),
                TabLabel::TextWithAdornment(text, builder) => div()
                    .flex()
                    .items_center()
                    .gap(px(8.0))
                    .text_color(rgba(foreground))
                    .child(builder(feedback[index], &self.tokens))
                    .child(text.clone())
                    .into_any_element(),
            };
            let indicator = best_contrast_accent(self.tokens.accent, self.tokens.background, 3.0);
            headers = headers.child(
                div()
                    .id(("tab", index))
                    .track_focus(focus)
                    .relative()
                    .w(px(self.tokens.button_width))
                    .h(px(self.tokens.button_height))
                    .child(
                        div()
                            .absolute()
                            .left(px(4.0))
                            .right(px(4.0))
                            .top(px(4.0))
                            .bottom(px(4.0))
                            .rounded(px(7.0))
                            .bg(rgba(paint)),
                    )
                    .child(
                        div()
                            .relative()
                            .size_full()
                            .flex()
                            .items_center()
                            .justify_center()
                            .child(content),
                    )
                    .child(
                        div()
                            .absolute()
                            .left(px(4.0))
                            .right(px(4.0))
                            .bottom(px(0.0))
                            .h(px(2.0))
                            .rounded(px(1.0))
                            .bg(rgba(indicator))
                            .opacity(weight),
                    )
                    .on_hover(cx.listener(move |view, hovered, _, cx| {
                        view.update_input(index, |input| input.hovered = *hovered, cx)
                    }))
                    .on_mouse_down(
                        MouseButton::Left,
                        cx.listener(move |view, _, window, cx| {
                            if let Some(focus) = view.focus.get(index) {
                                window.focus(focus, cx);
                            }
                            view.update_input(
                                index,
                                |input| {
                                    input.pointer_held = true;
                                    input.hovered = true;
                                },
                                cx,
                            );
                        }),
                    )
                    .on_mouse_up(
                        MouseButton::Left,
                        cx.listener(move |view, _, _, cx| {
                            view.update_input(index, |input| input.pointer_held = false, cx)
                        }),
                    )
                    .on_mouse_up_out(
                        MouseButton::Left,
                        cx.listener(move |view, _, _, cx| {
                            view.update_input(index, |input| input.pointer_held = false, cx)
                        }),
                    )
                    .on_click(
                        cx.listener(move |view, _, window, cx| view.select(index, window, cx)),
                    )
                    .on_key_down(cx.listener(move |view, event: &KeyDownEvent, _, cx| {
                        if matches!(event.keystroke.key.as_str(), "enter" | "space")
                            && !event.is_held
                        {
                            view.update_input(index, |input| input.key_held = true, cx);
                            cx.stop_propagation();
                        }
                    }))
                    .on_key_up(cx.listener(move |view, event: &KeyUpEvent, window, cx| {
                        if matches!(event.keystroke.key.as_str(), "enter" | "space") {
                            let activate = view.input[index].key_held;
                            view.update_input(index, |input| input.key_held = false, cx);
                            if activate {
                                view.select(index, window, cx);
                            }
                            cx.stop_propagation();
                        }
                    })),
            );
        }
        let mut content = div().relative().w_full().flex_1().min_h_0();
        for (index, opacity) in snapshot.weights.iter().copied().enumerate() {
            if opacity <= 0.0 {
                continue;
            }
            content = content.child(
                div()
                    .absolute()
                    .size_full()
                    .opacity(opacity)
                    .child((self.page)(index, index == self.selected)),
            );
        }
        div()
            .size_full()
            .flex()
            .flex_col()
            .child(headers)
            .child(content)
    }
}

#[derive(Clone, Copy)]
pub enum SurfaceKind {
    Dialog,
    PopupMenu,
    InnerDropdown,
}

pub fn rounded_surface(tokens: &UiTokens, kind: SurfaceKind, child: AnyElement) -> AnyElement {
    rounded_surface_in(tokens, kind, CornerContext::None, child)
}

pub fn rounded_surface_in(
    tokens: &UiTokens,
    kind: SurfaceKind,
    context: CornerContext,
    child: AnyElement,
) -> AnyElement {
    surface_element(tokens, kind, context, child, false)
}

fn surface_element(
    tokens: &UiTokens,
    kind: SurfaceKind,
    context: CornerContext,
    child: AnyElement,
    fill: bool,
) -> AnyElement {
    let radius = match kind {
        SurfaceKind::Dialog => tokens.dialog_radius,
        SurfaceKind::PopupMenu => tokens.popup_radius,
        SurfaceKind::InnerDropdown => tokens.inner_dropdown_radius,
    };
    let radii = corner_radii(context, radius);
    let mut inner = div()
        .rounded_tl(px(radii[0]))
        .rounded_tr(px(radii[1]))
        .rounded_br(px(radii[2]))
        .rounded_bl(px(radii[3]))
        .overflow_hidden()
        .bg(rgba(tokens.background))
        .child(child);
    if fill {
        inner = inner.size_full();
    }
    let mut outer = div()
        .rounded_tl(px(radii[0]))
        .rounded_tr(px(radii[1]))
        .rounded_br(px(radii[2]))
        .rounded_bl(px(radii[3]))
        .shadow(tokens.surface_shadow.clone())
        .child(inner);
    if fill {
        outer = outer.size_full();
    }
    outer.into_any_element()
}

pub type MenuBuilder = Rc<dyn Fn() -> AnyElement>;

#[derive(Clone, Copy, Debug, PartialEq)]
enum MenuMode {
    Closed,
    Open,
    Draining,
}

pub struct DismissibleMenu {
    tokens: UiTokens,
    content: MenuBuilder,
    on_dismiss: ClickHandler,
    bounds: Bounds<Pixels>,
    corners: CornerContext,
    mode: MenuMode,
    held: Vec<MouseButton>,
    focus: FocusHandle,
    previous_focus: Option<FocusHandle>,
    _subscriptions: Vec<Subscription>,
}

impl DismissibleMenu {
    pub fn new(
        tokens: UiTokens,
        content: MenuBuilder,
        on_dismiss: ClickHandler,
        cx: &mut Context<Self>,
    ) -> Self {
        Self {
            tokens,
            content,
            on_dismiss,
            bounds: Bounds::default(),
            corners: CornerContext::None,
            mode: MenuMode::Closed,
            held: Vec::new(),
            focus: cx.focus_handle(),
            previous_focus: None,
            _subscriptions: Vec::new(),
        }
    }

    pub fn open(&mut self, bounds: Bounds<Pixels>, window: &mut Window, cx: &mut Context<Self>) {
        if self.mode == MenuMode::Draining {
            return;
        }
        self.bounds = bounds;
        self.previous_focus = window.focused(cx);
        self.mode = MenuMode::Open;
        window.focus(&self.focus, cx);
        cx.notify();
    }

    pub fn close(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        if self.mode != MenuMode::Open {
            return;
        }
        self.mode = if self.held.is_empty() {
            MenuMode::Closed
        } else {
            MenuMode::Draining
        };
        if let Some(focus) = self.previous_focus.take() {
            window.focus(&focus, cx);
        }
        cx.notify();
    }

    pub fn set_tokens(&mut self, tokens: UiTokens, cx: &mut Context<Self>) {
        self.tokens = tokens;
        cx.notify();
    }
    pub fn set_corner_context(&mut self, context: CornerContext, cx: &mut Context<Self>) {
        self.corners = context;
        cx.notify();
    }
    pub fn is_open(&self) -> bool {
        self.mode == MenuMode::Open
    }
    pub fn is_active(&self) -> bool {
        self.mode != MenuMode::Closed
    }

    fn contains(&self, position: Point<Pixels>) -> bool {
        if !self.bounds.contains(&position) {
            return false;
        }
        let x = f32::from(position.x - self.bounds.origin.x);
        let y = f32::from(position.y - self.bounds.origin.y);
        let w = f32::from(self.bounds.size.width);
        let h = f32::from(self.bounds.size.height);
        let radii = corner_radii(self.corners, self.tokens.popup_radius);
        for (index, radius) in radii.into_iter().enumerate() {
            let radius = radius.min(w * 0.5).min(h * 0.5);
            let (dx, dy) = match index {
                0 => (x, y),
                1 => (w - x, y),
                2 => (w - x, h - y),
                _ => (x, h - y),
            };
            if dx < radius
                && dy < radius
                && (dx - radius).powi(2) + (dy - radius).powi(2) > radius.powi(2)
            {
                return false;
            }
        }
        true
    }

    fn remember(&mut self, button: MouseButton) {
        if !self.held.contains(&button) {
            self.held.push(button);
        }
    }

    fn down(&mut self, event: &MouseDownEvent, window: &mut Window, cx: &mut Context<Self>) {
        if self.mode == MenuMode::Closed {
            return;
        }
        self.remember(event.button);
        if self.mode == MenuMode::Draining {
            cx.stop_propagation();
            return;
        }
        if !self.contains(event.position) {
            cx.stop_propagation();
            self.close(window, cx);
            (self.on_dismiss)(window, cx);
        }
    }

    fn up(&mut self, event: &MouseUpEvent, cx: &mut Context<Self>) {
        let draining = self.mode == MenuMode::Draining;
        self.held.retain(|button| *button != event.button);
        if draining {
            cx.stop_propagation();
            if self.held.is_empty() {
                self.mode = MenuMode::Closed;
                cx.notify();
            }
        }
    }

    fn dismiss(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        if self.mode == MenuMode::Open {
            self.close(window, cx);
            (self.on_dismiss)(window, cx);
        }
    }

    fn cancel(&mut self, window: &mut Window, cx: &mut Context<Self>) {
        self.held.clear();
        self.dismiss(window, cx);
        self.mode = MenuMode::Closed;
        cx.notify();
    }
}

impl Render for DismissibleMenu {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        if self._subscriptions.is_empty() {
            self._subscriptions
                .push(cx.observe_window_activation(window, |view, window, cx| {
                    if !window.is_window_active() {
                        view.cancel(window, cx);
                    }
                }));
            self._subscriptions.push(
                cx.observe_window_visibility(window, |view, _, window, cx| view.cancel(window, cx)),
            );
        }
        if !self.is_active() {
            return div().into_any_element();
        }
        let down = cx.listener(|view, event, window, cx| view.down(event, window, cx));
        let up = cx.listener(|view, event, _, cx| view.up(event, cx));
        let movement = cx.listener(|view, _: &MouseMoveEvent, _, cx| {
            if view.mode == MenuMode::Draining {
                cx.stop_propagation();
            }
        });
        let wheel = cx.listener(|view, event: &ScrollWheelEvent, _, cx| {
            if view.mode == MenuMode::Draining || view.is_open() && !view.contains(event.position) {
                cx.stop_propagation();
            }
        });
        let key = cx.listener(|view, event: &KeyDownEvent, window, cx| {
            if event.keystroke.key == "escape" && view.is_active() {
                cx.stop_propagation();
                view.dismiss(window, cx);
            }
        });
        let mut overlay = div()
            .id("menu-shield")
            .absolute()
            .size_full()
            .occlude()
            .track_focus(&self.focus);
        if self.is_open() {
            overlay = overlay.child(
                div()
                    .absolute()
                    .left(self.bounds.origin.x)
                    .top(self.bounds.origin.y)
                    .w(self.bounds.size.width)
                    .h(self.bounds.size.height)
                    .child(surface_element(
                        &self.tokens,
                        SurfaceKind::PopupMenu,
                        self.corners,
                        div().size_full().child((self.content)()).into_any_element(),
                        true,
                    )),
            );
        }
        overlay
            .child(
                canvas(
                    |_, _, _| (),
                    move |_, (), window, _| {
                        window.on_mouse_event(move |event, phase, window, cx| {
                            if phase == DispatchPhase::Capture {
                                down(event, window, cx);
                            }
                        });
                        window.on_mouse_event(move |event, phase, window, cx| {
                            if phase == DispatchPhase::Capture {
                                up(event, window, cx);
                            }
                        });
                        window.on_mouse_event(move |event, phase, window, cx| {
                            if phase == DispatchPhase::Capture {
                                movement(event, window, cx);
                            }
                        });
                        window.on_mouse_event(move |event, phase, window, cx| {
                            if phase == DispatchPhase::Capture {
                                wheel(event, window, cx);
                            }
                        });
                        window.on_key_event(move |event, phase, window, cx| {
                            if phase == DispatchPhase::Capture {
                                key(event, window, cx);
                            }
                        });
                    },
                )
                .absolute()
                .size_full(),
            )
            .into_any_element()
    }
}
```
