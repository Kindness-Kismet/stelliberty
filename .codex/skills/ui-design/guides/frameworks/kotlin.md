# Kotlin + Compose Multiplatform

[Android 运行示例](kotlin-android.md)提供完整依赖、Activity 和调用页面，复用下方公共组件。

适配 Compose Multiplatform 1.9.0；UI、foundation、animation、material3 使用 1.9.0 发布 API。FeedbackButton/FeedbackRow 复用 clickable、selectable、InteractionSource 输入，关闭 ripple 后绘制明确状态反馈。scale 只作用于外层命中区域内的 graphicsLayer；按压中断保留当前值，失焦、窗口失活或禁用 snap 回 1.0。主题重组每帧重算实际背景和文字。

复合行的标题、副标题保持各自主题颜色；悬停底色与图标等非文字装饰使用轻度 Luminance。单色图标可通过 LocalContentColor 接入，位图整图滤镜需要独立图像绘制 adapter。TabPage.content(active) 关闭退出页的控件输入与焦点。

默认阴影使用 androidx.compose.ui.graphics.shadow.Shadow 与 Modifier.dropShadow，浮点 alpha 0.25。Surface 的 shadowElevation、tonalElevation 都为 0，避免叠加 Material elevation。BasicAlertDialog 使用显式 ExperimentalMaterial3Api opt-in；菜单采用窗口 overlay 保持整次 pointer 拦截。阴影映射到原生 primitive，跨框架不要求逐像素相同。

优先复用项目已有组件库的样式和输入，只调整明确差异；缺少动画时使用所附包装。普通按钮按 ButtonRole 分为 Plain、Dismiss、Primary。Plain 使用主题前景、透明底色和无 hover 底色；Dismiss 使用中性填充；Primary 使用主题强调色。禁用时复用 Material3 ButtonDefaults 对应变体的容器色与文字色，保持禁用语义和可辨识外观。

surface 输入为当前已合成的不透明 sRGB 颜色。8% mix 在 sRGB 编码通道执行 base × (1 − a) + overlay × a，alpha 先合成，再按 8 bit 四舍五入。强调色先合成到 surface。文字对比度另外使用线性化 sRGB 相对亮度，分别计算 #000000 和 #FFFFFF 的对比度并选择较高者；Dismiss、Primary 的 hover 中间帧也按实际背景重算文字，普通字号目标至少 4.5。

按压目标 0.94、释放目标 1.0，180 ms；曲线用 x 二分 16 次求 cubic-bezier(0.34,1.56,0.64,1) 的 y，保留 overshoot。取消、失焦、隐藏、禁用复位，不改变外层命中区域。

无真实圆角祖先时普通行圆角 10、菜单行圆角 7。equalInsetAncestor(R,dx,dy) 仅接受非负等距内缩；Adjacent 只约束声明的相邻角，半径 max(R − d,0)，其余角采用组件默认半径。GroupCap 显式继承同组派生上限。宿主通过实际等距 padding d 提供对应行 bounds；有 context 时背景 inset 为 0，不再叠加 Navigation 的 8,2,8,2。非等距布局不宣称严格同心。

无祖先 Navigation 使用 navigationHover，背景内缩 8,2,8,2；SettingsCard 的 hover 覆盖整行，亮主题 #eff2f7、暗主题 #2d2d2d，可用主题值替换。Menu 使用整行 hover 和菜单默认圆角。FeedbackRowText 的详细行最小高度 58，副标题随宽度换行后增加高度。按压时悬停底色轻度加深，标题和副标题保持原值，hover 的 72 ms alpha 过渡继续独立工作。悬停底色与 leading/trailing 图标等非文字装饰接收反馈 level，普通与 hover 为 0，pressed 为 1；按压 30 ms、恢复 72 ms，easeOutCubic，局部颜色默认 brightness .92、contrast 1.12。没有装饰的行保留轻度底色反馈。触屏按压通过 pressed 显示背景，不依赖 hover；业务 selected 项屏蔽悬停叠加。FeedbackRowText 将 enabled、visible 与 selected 透传到公共行，标签页面使用 active 禁用退出页控件。

FeedbackRowText 接收可选 leading/trailing RowAdornment，RowAdornmentContent 将主题 palette 中每个颜色按同一 level 量化，再交给局部 content 绘制。彩色圆形加白色加号可登记 [圆底色,Color.White]，2部分分别使用对应颜色；单色 SVG 使用处理后的 tint。多色 SVG 与位图交给局部绘制 adapter，标题和副标题不加滤镜。TabPage.adornment 复用同一入口。

Navigation 行使用 navigationHover，文字 Tab 使用独立的 textTabHover 主题键；UiTokens 从 surface 叠 8% 最高对比度黑/白派生默认颜色，两者默认同色、之后各自可覆盖。已有组件库分别注入对应角色的 hover 键。按压对自己的文字 Tab 悬停色轻度加深，标题保持原色，混合 Tab 的图标继续应用局部反馈；背景内缩 4、圆角 7，状态变化立即更新。持久选中项按业务 selected 目标屏蔽 hover 背景，未选中项保留 hover；混合 Tab 图标的按压反馈继续生效，使用 30 ms 按压和 72 ms 恢复。icon 或 adornment 非空且 label 为空才是纯图标，背景透明且不增加按压反馈；带图标与非空标题时同时绘制两者，按文字型背景处理。选中权重与页面以 160 ms linear 交叉渐隐渐显，快速切换从当前权重接续。标题先按实际背景校正选中端点，再从 normal 向校正后的 selected 混色；最终不透明文字仍检查 4.5。底部指示线厚 2、端点半径 1，处于背景 inset 外；不透明基色对 surface 检查 3，显示 alpha 跟随权重，透明过渡帧不要求达到 3。页面保持组合实例，active=false 时控件同步关闭输入和焦点。

RoundedMenu 始终放在窗口全尺寸 Box 宿主，menuOffset 提供菜单位置。外部 Press 立即隐藏 body 并通知 onDismiss，overlay 留至全部鼠标按钮和触点释放；期间在 PointerEventPass.Initial 消费 down/move/up/wheel。onDismiss 只更新业务状态，不能移除 RoundedMenu 的调用。内部 pointer 正常交给菜单内容；程序关闭或 Escape 时仍有鼠标按钮/触点按住则进入 Draining，窗口失活直接清除状态。真实点击边界使用菜单 bounds 和相同圆角，不包含阴影。returnFocus 指向当前可聚焦的触发器，菜单关闭且 Draining 结束后请求恢复内部焦点；Android 返回键由宿主 BackHandler 接入，触摸模式遵循组件库的焦点规则。

Dialog、PopupMenu、InnerDropdown 默认圆角分别 16、12、8，可通过 CornerContext 派生。所有浮层共用 blur 5、offset (0,1)、rgba(0,0,0,0.25)、spread 0 的外阴影；普通按钮、行项、标签无阴影。示例固定 1x 动画速度，动态变速由项目的公共时钟适配。

```kotlin
package ui.design.examples

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.Easing
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.snap
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.hoverable
import androidx.compose.foundation.focusable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsHoveredAsState
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.shape.AbsoluteRoundedCornerShape
import androidx.compose.material3.BasicAlertDialog
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.key
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.dropShadow
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.TransformOrigin
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.shadow.Shadow
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.type
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.PointerEventType
import androidx.compose.ui.input.pointer.areAnyPressed
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.boundsInRoot
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalWindowInfo
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.DpOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.zIndex
import kotlin.math.max
import kotlin.math.pow
import kotlin.math.roundToInt

enum class ButtonRole { Plain, Dismiss, Primary }
enum class SurfaceKind { Dialog, PopupMenu, InnerDropdown }
enum class RowKind { Navigation, SettingsCard, Menu }
enum class CornerAnchor { TopLeft, TopRight, BottomRight, BottomLeft }
data class RoundedAncestor(val radius: Dp, val inset: Dp)
sealed interface CornerContext {
    data object None : CornerContext
    data class Adjacent(val anchor: CornerAnchor, val ancestor: RoundedAncestor) : CornerContext
    data class GroupCap(val radius: Dp) : CornerContext
}

fun equalInsetAncestor(radius: Dp, dx: Dp, dy: Dp): RoundedAncestor? =
    if (radius < 0.dp || dx < 0.dp || dy < 0.dp || dx != dy) null else RoundedAncestor(radius, dx)

fun derivedRadius(context: CornerContext, fallback: Dp): Dp = when (context) {
    CornerContext.None -> fallback
    is CornerContext.Adjacent -> (context.ancestor.radius - context.ancestor.inset).coerceAtLeast(0.dp)
    is CornerContext.GroupCap -> context.radius.coerceAtLeast(0.dp)
}

private fun cornerRadii(context: CornerContext, fallback: Dp): List<Dp> {
    val radii = MutableList(4) { fallback }
    when (context) {
        CornerContext.None -> Unit
        is CornerContext.GroupCap -> radii.indices.forEach { radii[it] = derivedRadius(context, fallback) }
        is CornerContext.Adjacent -> radii[context.anchor.ordinal] = derivedRadius(context, fallback)
    }
    return radii
}

private fun cornerShape(context: CornerContext, fallback: Dp): AbsoluteRoundedCornerShape {
    val radii = cornerRadii(context, fallback)
    return AbsoluteRoundedCornerShape(radii[0], radii[1], radii[2], radii[3])
}

data class UiTokens(
    val surface: Color,
    val foreground: Color,
    val accent: Color,
    val rowTitle: Color,
    val rowSubtitle: Color,
    val isDark: Boolean,
    val buttonRadius: Dp,
    val buttonPadding: PaddingValues,
    val rowPadding: PaddingValues,
    val rowRadius: Dp = 10.dp,
    val menuRowRadius: Dp = 7.dp,
    val dialogRadius: Dp = 16.dp,
    val popupRadius: Dp = 12.dp,
    val innerDropdownRadius: Dp = 8.dp,
    val pressScale: Float = 0.94f,
    val pressMillis: Int = 180,
    val rowPressMillis: Int = 30,
    val rowHoverMillis: Int = 72,
    val tabMillis: Int = 160,
    val settingsHover: Color = if (isDark) Color(0xff2d2d2d) else Color(0xffeff2f7),
    val navigationHover: Color = over(bestText(surface), surface, 0.08f),
    val textTabHover: Color = navigationHover,
    val surfaceShadow: Shadow = Shadow(radius = 5.dp, spread = 0.dp,
        offset = DpOffset(0.dp, 1.dp), color = Color.Black, alpha = 0.25f),
) {
    val hoverLevel: Float get() = 0f

    fun rawRowHover(kind: RowKind): Color = if (kind != RowKind.SettingsCard)
        navigationHover else settingsHover

    fun rowBackground(kind: RowKind, level: Float, hover: Float): Color {
        val color = luminance(rawRowHover(kind), level)
        return color.copy(alpha = (color.alpha * hover.coerceIn(0f, 1f) * 255).roundToInt() / 255f)
    }

    fun buttonColors(role: ButtonRole, hover: Float): Pair<Color, Color> {
        if (role == ButtonRole.Plain) return Color.Transparent to foreground
        val normal = if (role == ButtonRole.Dismiss) over(bestText(surface), surface, 0.08f)
            else over(accent, surface, 1f)
        val actual = over(bestText(normal), normal, 0.08f * hover)
        return actual to bestText(actual)
    }

    fun luminance(color: Color, level: Float): Color {
        if (level == 0f) return color
        val alpha = (color.alpha * 255).roundToInt()
        if (alpha == 0) return Color.Transparent
        val brightness = 1 - 0.08f * level
        val contrast = 1 + 0.12f * max(0f, level)
        fun channel(value: Float): Int {
            val premultiplied = (value * alpha).roundToInt()
            val filtered = ((premultiplied * brightness - alpha / 2f) * contrast + alpha / 2f)
                .roundToInt().coerceIn(0, alpha)
            return (filtered * 255f / alpha).roundToInt()
        }
        return Color(channel(color.red), channel(color.green), channel(color.blue), alpha)
    }
}

fun over(foreground: Color, background: Color, opacity: Float): Color {
    val alpha = foreground.alpha * opacity
    fun channel(front: Float, back: Float): Int = ((front * alpha + back * (1 - alpha)) * 255).roundToInt()
    return Color(
        channel(foreground.red, background.red), channel(foreground.green, background.green),
        channel(foreground.blue, background.blue), 255,
    )
}

private fun relativeLuminance(color: Color): Double {
    fun linear(value: Float): Double = if (value <= 0.04045f) value.toDouble() / 12.92
        else ((value + 0.055) / 1.055).pow(2.4)
    return 0.2126 * linear(color.red) + 0.7152 * linear(color.green) + 0.0722 * linear(color.blue)
}

fun contrastRatio(first: Color, second: Color): Double {
    val a = relativeLuminance(first)
    val b = relativeLuminance(second)
    return (max(a, b) + 0.05) / (minOf(a, b) + 0.05)
}

fun bestAccent(accent: Color, background: Color, minimum: Double): Color {
    val actual = over(accent, background, 1f)
    return if (contrastRatio(actual, background) >= minimum) actual else bestText(background)
}

fun tabBackground(tokens: UiTokens, hovered: Boolean, pressed: Boolean, icon: Boolean): Pair<Color, Color> {
    val raw = tokens.textTabHover
    val color = if (icon) Color.Transparent else if (pressed) tokens.luminance(raw, 1f)
        else if (hovered) raw else Color.Transparent
    return color to over(color, tokens.surface, 1f)
}

private fun tabStateBackground(tokens: UiTokens, hovered: Boolean, pressed: Boolean, icon: Boolean,
                               selected: Boolean): Pair<Color, Color> =
    tabBackground(tokens, hovered && !selected, pressed, icon)

fun tabForeground(tokens: UiTokens, background: Color, weight: Float): Color = bestAccent(
    over(bestAccent(tokens.accent, background, 4.5), over(tokens.foreground, background, 1f), weight.coerceIn(0f, 1f)),
    background, 4.5,
)

fun bestText(background: Color): Color {
    fun linear(value: Float): Double = if (value <= 0.04045f) value.toDouble() / 12.92
        else ((value + 0.055) / 1.055).pow(2.4)
    val luminance = 0.2126 * linear(background.red) + 0.7152 * linear(background.green) + 0.0722 * linear(background.blue)
    return if ((luminance + 0.05) / 0.05 >= 1.05 / (luminance + 0.05)) Color.Black else Color.White
}

val PressCurve = Easing { x ->
    fun cubic(t: Float, a: Float, b: Float): Float =
        3 * (1 - t).pow(2) * t * a + 3 * (1 - t) * t.pow(2) * b + t.pow(3)
    var lo = 0f
    var hi = 1f
    repeat(16) {
        val mid = (lo + hi) / 2
        if (cubic(mid, 0.34f, 0.64f) < x) lo = mid else hi = mid
    }
    cubic((lo + hi) / 2, 1.56f, 1f)
}

private val EaseOutCubic = Easing { 1 - (1 - it).pow(3) }

data class RowAdornment(val palette: (UiTokens) -> List<Color>,
                         val content: @Composable (List<Color>) -> Unit)

fun adornmentColors(tokens: UiTokens, colors: List<Color>, level: Float): List<Color> =
    colors.map { tokens.luminance(it, level) }

@Composable
fun RowAdornmentContent(tokens: UiTokens, adornment: RowAdornment, level: Float) {
    adornment.content(adornmentColors(tokens, adornment.palette(tokens), level))
}

@Composable
fun FeedbackButton(
    tokens: UiTokens,
    role: ButtonRole,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    visible: Boolean = true,
    content: @Composable () -> Unit,
) {
    if (!visible) return
    val interactions = remember { MutableInteractionSource() }
    val pressed by interactions.collectIsPressedAsState()
    val hovered by interactions.collectIsHoveredAsState()
    val windowFocused = LocalWindowInfo.current.isWindowFocused
    var hadFocus by remember { mutableStateOf(false) }
    var focusCancelled by remember { mutableStateOf(false) }
    val scale = remember { Animatable(1f) }
    LaunchedEffect(pressed, enabled, windowFocused, focusCancelled) {
        if (!enabled || !windowFocused || focusCancelled) scale.snapTo(1f)
        else scale.animateTo(if (pressed) tokens.pressScale else 1f,
            tween(tokens.pressMillis, easing = PressCurve))
        if (!pressed) focusCancelled = false
    }
    val hoverWeight by animateFloatAsState(
        if (hovered && enabled && windowFocused && role != ButtonRole.Plain) 1f else 0f,
        tween(tokens.rowHoverMillis, easing = EaseOutCubic), label = "filled-hover",
    )
    val (background, foreground) = if (enabled) tokens.buttonColors(role, hoverWeight) else {
        val defaults = when (role) {
            ButtonRole.Plain -> ButtonDefaults.textButtonColors()
            ButtonRole.Dismiss -> ButtonDefaults.filledTonalButtonColors()
            ButtonRole.Primary -> ButtonDefaults.buttonColors()
        }
        defaults.disabledContainerColor to defaults.disabledContentColor
    }
    val shape = RoundedCornerShape(tokens.buttonRadius)
    Box(modifier.onFocusChanged {
        if (hadFocus && !it.isFocused) focusCancelled = true
        hadFocus = it.isFocused
    }.hoverable(interactions, enabled).clickable(
        interactionSource = interactions, indication = null, enabled = enabled,
        role = Role.Button, onClick = onClick,
    )) {
        CompositionLocalProvider(LocalContentColor provides foreground) {
            Box(Modifier.graphicsLayer {
                scaleX = scale.value
                scaleY = scale.value
                transformOrigin = TransformOrigin.Center
            }.clip(shape).background(background).padding(tokens.buttonPadding)) { content() }
        }
    }
}

@Composable
fun FeedbackRow(
    tokens: UiTokens,
    kind: RowKind,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    visible: Boolean = true,
    cornerContext: CornerContext = CornerContext.None,
    selected: Boolean = false,
    content: @Composable RowScope.(Float) -> Unit,
) {
    if (!visible) return
    val interactions = remember { MutableInteractionSource() }
    val pressed by interactions.collectIsPressedAsState()
    val hovered by interactions.collectIsHoveredAsState()
    val windowFocused = LocalWindowInfo.current.isWindowFocused
    val level by animateFloatAsState(
        if (!enabled || !windowFocused) 0f else if (pressed) 1f else if (hovered) tokens.hoverLevel else 0f,
        if (!enabled || !windowFocused) snap() else
            tween(if (pressed) tokens.rowPressMillis else tokens.rowHoverMillis, easing = EaseOutCubic),
        label = "row-luminance",
    )
    val inset = if (kind == RowKind.Navigation && cornerContext == CornerContext.None) PaddingValues(horizontal = 8.dp, vertical = 2.dp)
        else PaddingValues(0.dp)
    val backgroundWeight by animateFloatAsState(
        if ((pressed || hovered && !selected) && enabled && windowFocused) 1f else 0f,
        if (!enabled || !windowFocused) snap() else
            tween(if (pressed) tokens.rowPressMillis else tokens.rowHoverMillis, easing = EaseOutCubic),
        label = "row-background-alpha",
    )
    val background = if (selected && !pressed) Color.Transparent
        else tokens.rowBackground(kind, level, backgroundWeight)
    val shape = cornerShape(cornerContext, if (kind == RowKind.Menu) tokens.menuRowRadius else tokens.rowRadius)
    CompositionLocalProvider(LocalContentColor provides tokens.rowTitle) {
        Box(modifier.hoverable(interactions, enabled).clickable(
                interactionSource = interactions, indication = null, enabled = enabled,
                role = Role.Button, onClick = onClick,
            ), propagateMinConstraints = true) {
            Row(Modifier.padding(inset).clip(shape)
                .background(background).padding(tokens.rowPadding),
                verticalAlignment = Alignment.CenterVertically) { content(level) }
        }
    }
}

@Composable
fun FeedbackRowText(tokens: UiTokens, kind: RowKind, title: String, subtitle: String?, onClick: () -> Unit,
                    modifier: Modifier = Modifier, cornerContext: CornerContext = CornerContext.None,
                    leading: RowAdornment? = null, trailing: RowAdornment? = null,
                    enabled: Boolean = true, visible: Boolean = true, selected: Boolean = false) {
    val geometry = if (subtitle != null) modifier.fillMaxWidth().heightIn(min = 58.dp) else modifier.fillMaxWidth()
    FeedbackRow(tokens, kind, onClick, geometry, enabled, visible, cornerContext, selected) { level ->
        if (leading != null) RowAdornmentContent(tokens, leading, level)
        Column(Modifier.weight(1f)) {
            Text(title)
            if (subtitle != null) Text(subtitle, color = tokens.rowSubtitle)
        }
        if (trailing != null) RowAdornmentContent(tokens, trailing, level)
    }
}

data class TabPage(val id: String, val label: String, val content: @Composable (Boolean) -> Unit,
                   val icon: (@Composable () -> Unit)? = null, val adornment: RowAdornment? = null)

@Composable
fun CrossfadeTabs(tokens: UiTokens, pages: List<TabPage>, modifier: Modifier = Modifier) {
    require(pages.isNotEmpty()) { "Tabs require at least one page" }
    var selected by remember(pages.map { it.id }) { mutableStateOf(pages.first().id) }
    val windowFocused = LocalWindowInfo.current.isWindowFocused
    val weights = pages.associate { page ->
        key(page.id) {
            page.id to animateFloatAsState(if (selected == page.id) 1f else 0f,
                tween(tokens.tabMillis, easing = LinearEasing), label = "tab-${page.id}")
        }
    }
    Column(modifier) {
        Row {
            pages.forEach { page ->
                key(page.id) {
                    val interactions = remember { MutableInteractionSource() }
                    val hovered by interactions.collectIsHoveredAsState()
                    val pressed by interactions.collectIsPressedAsState()
                    val weight by weights.getValue(page.id)
                    val iconOnly = (page.icon != null || page.adornment != null) && page.label.isEmpty()
                    val hasAdornment = (page.icon != null || page.adornment != null) && !iconOnly
                    val iconLevel by animateFloatAsState(
                        if (pressed && windowFocused && hasAdornment) 1f else 0f,
                        if (!windowFocused) snap() else tween(if (pressed) tokens.rowPressMillis else tokens.rowHoverMillis,
                            easing = EaseOutCubic), label = "tab-adornment-${page.id}",
                    )
                    val (paint, actual) = tabStateBackground(tokens, hovered && windowFocused,
                        pressed && windowFocused, iconOnly, page.id == selected)
                    val foreground = tabForeground(tokens, actual, weight)
                    Box(Modifier.heightIn(min = 40.dp).hoverable(interactions).selectable(
                        selected = selected == page.id, interactionSource = interactions,
                        indication = null, role = Role.Tab, onClick = { selected = page.id },
                    )) {
                        Box(Modifier.matchParentSize().padding(4.dp).clip(RoundedCornerShape(7.dp)).background(paint))
                        CompositionLocalProvider(LocalContentColor provides foreground) {
                            Row(Modifier.padding(tokens.buttonPadding)) {
                                val icon = page.icon
                                val adornment = page.adornment
                                if (adornment != null) RowAdornmentContent(tokens, adornment, iconLevel)
                                else if (icon != null) CompositionLocalProvider(
                                    LocalContentColor provides tokens.luminance(foreground, iconLevel)) { icon() }
                                if (page.label.isNotEmpty()) Text(page.label, color = foreground)
                            }
                        }
                        Box(Modifier.matchParentSize()) {
                            Box(Modifier.align(Alignment.BottomCenter).fillMaxWidth().padding(horizontal = 4.dp)
                                .height(2.dp).graphicsLayer { alpha = weight }.clip(RoundedCornerShape(1.dp))
                                .background(bestAccent(tokens.accent, tokens.surface, 3.0)))
                        }
                    }
                }
            }
        }
        Box(Modifier.fillMaxSize()) {
            pages.forEach { page ->
                key(page.id) {
                    val active = page.id == selected
                    val weight by weights.getValue(page.id)
                    Box(Modifier.fillMaxSize().zIndex(if (active) 1f else 0f).graphicsLayer { alpha = weight }
                        .pointerInput(active) {
                            awaitPointerEventScope {
                                while (true) {
                                    val event = awaitPointerEvent(PointerEventPass.Initial)
                                    if (!active) event.changes.forEach { it.consume() }
                                }
                            }
                        }) {
                        // 页面保持组合实例，控件使用 active 同步关闭输入与焦点。
                        page.content(active)
                    }
                }
            }
        }
    }
}

@Composable
fun RoundedSurface(tokens: UiTokens, kind: SurfaceKind, modifier: Modifier = Modifier,
                   cornerContext: CornerContext = CornerContext.None, content: @Composable () -> Unit) {
    val radius = when (kind) {
        SurfaceKind.Dialog -> tokens.dialogRadius
        SurfaceKind.PopupMenu -> tokens.popupRadius
        SurfaceKind.InnerDropdown -> tokens.innerDropdownRadius
    }
    val shape = cornerShape(cornerContext, radius)
    Surface(modifier = modifier.dropShadow(shape, tokens.surfaceShadow), shape = shape, color = tokens.surface,
        contentColor = tokens.foreground, tonalElevation = 0.dp, shadowElevation = 0.dp,
        content = content)
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun RoundedDialog(tokens: UiTokens, onDismiss: () -> Unit, content: @Composable () -> Unit) {
    BasicAlertDialog(onDismissRequest = onDismiss) {
        RoundedSurface(tokens, SurfaceKind.Dialog, content = content)
    }
}

@Composable
fun RoundedMenu(tokens: UiTokens, expanded: Boolean, onDismiss: () -> Unit,
                menuOffset: DpOffset, modifier: Modifier = Modifier,
                cornerContext: CornerContext = CornerContext.None,
                returnFocus: FocusRequester? = null,
                content: @Composable androidx.compose.foundation.layout.ColumnScope.() -> Unit) {
    val state = remember { MenuState() }
    val focus = remember { FocusRequester() }
    val currentDismiss by rememberUpdatedState(onDismiss)
    val currentExpanded by rememberUpdatedState(expanded)
    val currentTokens by rememberUpdatedState(tokens)
    val currentContext by rememberUpdatedState(cornerContext)
    val density = LocalDensity.current
    val currentDensity by rememberUpdatedState(density)
    val windowFocused = LocalWindowInfo.current.isWindowFocused
    var heldFocus by remember { mutableStateOf(false) }
    LaunchedEffect(expanded, windowFocused) {
        if (!windowFocused) {
            val notify = state.visible
            state.cancel()
            if (notify) currentDismiss()
        } else if (expanded && !state.draining) {
            state.visible = true
        } else if (!expanded) state.close()
    }
    LaunchedEffect(state.visible, state.draining, windowFocused) {
        if (state.visible) {
            focus.requestFocus()
            heldFocus = true
        } else if (!state.draining && heldFocus) {
            heldFocus = false
            if (windowFocused) returnFocus?.requestFocus()
        }
    }
    if (!state.visible && !state.draining) return
    Box(modifier.fillMaxSize().zIndex(Float.MAX_VALUE).focusRequester(focus)
        .onPreviewKeyEvent { event ->
            if (event.key != Key.Escape) false else {
                if (event.type == KeyEventType.KeyDown && state.visible) {
                    state.close()
                    currentDismiss()
                }
                true
            }
        }.focusable().onGloballyPositioned { state.overlayBounds = it.boundsInRoot() }
        .pointerInput(Unit) {
            awaitPointerEventScope {
                try {
                    while (true) {
                        val event = awaitPointerEvent(PointerEventPass.Initial)
                        val held = event.buttons.areAnyPressed || event.changes.any { it.pressed }
                        state.held = held
                        if (!currentExpanded && state.visible) state.close()
                        val radii = with(currentDensity) {
                            cornerRadii(currentContext, currentTokens.popupRadius).map { it.toPx() }
                        }
                        val outside = event.changes.any { !state.contains(it.position + state.overlayBounds.topLeft, radii) }
                        if (event.type == PointerEventType.Press && state.visible && outside) {
                            state.close()
                            currentDismiss()
                        }
                        if (state.draining || !state.visible || outside) event.changes.forEach { it.consume() }
                        if (state.draining && !held) state.draining = false
                    }
                } finally {
                    state.held = false
                    state.draining = false
                }
            }
        }) {
        if (state.visible) {
            RoundedSurface(tokens, SurfaceKind.PopupMenu,
                Modifier.offset(menuOffset.x, menuOffset.y).onGloballyPositioned { state.menuBounds = it.boundsInRoot() },
                cornerContext) { Column(content = content) }
        }
    }
}

private class MenuState {
    var visible by mutableStateOf(false)
    var draining by mutableStateOf(false)
    var held = false
    var menuBounds = Rect.Zero
    var overlayBounds = Rect.Zero

    fun close() { visible = false; draining = held }
    fun cancel() { held = false; visible = false; draining = false }

    fun contains(position: Offset, radii: List<Float>): Boolean {
        if (!menuBounds.contains(position)) return false
        val x = position.x - menuBounds.left
        val y = position.y - menuBounds.top
        val width = menuBounds.width
        val height = menuBounds.height
        return radii.withIndex().all { (index, raw) ->
            val radius = raw.coerceAtMost(minOf(width, height) / 2)
            val (dx, dy) = when (index) {
                0 -> x to y; 1 -> width - x to y; 2 -> width - x to height - y; else -> x to height - y
            }
            dx >= radius || dy >= radius || (dx - radius).pow(2) + (dy - radius).pow(2) <= radius.pow(2)
        }
    }
}
```
