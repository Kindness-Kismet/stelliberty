# 主题参数

参数集中到项目的主题与动画模型，同类控件引用同一语义键。尺寸以 100% 逻辑单位保存；Qt 的 `style::Scale() == 100`、Windows 的 96 DPI 对应该基准，实际物理像素由框架换算。

## 颜色与对比度

`surface` 表示控件实际所在的背景。计算控件颜色时先合成背景、材质和半透明填充，再选择文字色；使用当前绘制结果。深色文字候选为 `#000000`，浅色文字候选为 `#FFFFFF`，两者 alpha 均为 `1`。

sRGB 通道先归一化到 `0～1`，转换为线性亮度：

```text
linear(c) = c / 12.92                         c <= 0.04045
linear(c) = ((c + 0.055) / 1.055) ^ 2.4       c > 0.04045
Y = 0.2126 * linear(R) + 0.7152 * linear(G) + 0.0722 * linear(B)
contrast(a, b) = (max(Y(a), Y(b)) + 0.05) / (min(Y(a), Y(b)) + 0.05)
foreground(background) = #000000、#FFFFFF 中 contrast 较高的1项
```

填充按钮的普通、悬停、按下和动画中间帧都按实际背景重新选择黑白文字，对比度至少 `4.5:1`。半透明颜色先合成，不能直接拿原始 RGB 或主题名称判断。

操作按钮按语义选择变体：

| 变体 | 普通背景 | 悬停背景 | 文字 |
|---|---|---|---|
| `Plain` | 透明，显示所在 surface | 保持透明 | 当前主题前景色 |
| `Dismiss` | surface 叠加 `8%` 高对比黑/白前景，形成中性底 | 在普通背景上再叠加 `8%` 高对比黑/白前景 | 按每态、每帧背景选黑/白 |
| `Primary` | 当前主题 accent 合成后的实际背景 | 在普通背景上叠加 `8%` 高对比黑/白前景 | 按每态、每帧背景选黑/白 |

`Dismiss` 用于取消、关闭当前 UI；`Primary` 用于业务操作。`Plain` 是透明变体，无悬停底色；`Dismiss` 与 `Primary` 使用表中的 `8% Neutral overlay`。

```text
neutral(base, alpha) = mix(base, foreground(base), alpha)
dismiss.normal = neutral(surface, 0.08)
dismiss.hover = neutral(dismiss.normal, 0.08)
primary.normal = composite(currentAccent, surface)
primary.hover = neutral(primary.normal, 0.08)
```

`mix` 对 sRGB 编码通道计算 `base × (1-alpha) + overlay × alpha`；`composite` 使用相同通道与真实 alpha。转换到 8 位通道时四舍五入。对比度使用上面的线性 sRGB 相对亮度，颜色、文字和背景使用同一帧状态。按钮的按压缩放和完整状态处理见 [按钮](buttons.md)。

## 默认主题的参考语义色

下表记录 AstraGram 默认浅色与默认 Night 的源码值，作为语义主题的起点。消费者使用主题键，用户主题和强调色可以替换对应值。

| 语义键 | 默认浅色 | 默认 Night | 用途 |
|---|---|---|---|
| `windowBg` | `#ffffff` | `#212121` | 基础 surface |
| `windowFg` | `#18263a` | `#f5f5f5` | 普通正文与图标 |
| `windowBoldFg` | `#142136` | `#e9e8e8` | 主菜单正文 |
| `windowBoldFgOver` | `#142136` | `#e9e9e9` | 主菜单悬停正文 |
| `windowSubTextFg` | `#7c8da1` | `#8999aa` | 普通辅助文字 |
| `windowBgActive` | `#238fe8` | `#5288c1` | 强调色实底 |
| `windowActiveTextFg` | `#238fe8` | `#6ab3f3` | 强调色文字、标签指示线 |
| `settingsCardBgOver` | `#eff2f7` | `#2d2d2d` | 设置列表项悬停底 |
| `settingsCardFg` | `#282d37` | `#edf0f5` | 设置列表标题 |
| `settingsCardSubTextFg` | `#858c98` | `#989da9` | 设置列表副标题 |

默认浅色来自 `Telegram/lib_ui/ui/colors.palette`；默认 Night 来自 `Telegram/Resources/night.tdesktop-theme` 内的 `colors.tdesktop-theme`。

主菜单的 `mainMenuBg` 引用 `windowBg`，主菜单行普通背景透明。参考代码的 `windowBgOver`、`menuBgOver` 在加载主题时按实际底色派生：浅色叠加 `8%` 黑色，深色叠加 `8%` 白色，先混合成不透明色再保存到 palette。

```text
rowOverlay = 默认浅色用黑色，默认深色用白色，alpha = 0.08
windowBgOver = mix(windowBg, rowOverlay.rgb, rowOverlay.alpha)
menuBgOver = mix(menuBg, rowOverlay.rgb, rowOverlay.alpha)
```

列表颜色派生使用 `hoverOverlay` 与 `ApplyHoverPalette` 的参数；按钮 Neutral 按上面的实际背景规则计算。设置列表项引用自己的 `settingsCardBgOver`。

参考项目开启窗口材质时，Hover 角色改为当前 `windowFg`、alpha `26/255`。采用材质的项目按实际 surface 合成并核对，不直接使用无材质主题的最终色值。

强调色来自当前主题 palette。AstraGram 内置主题先检查系统强调色设置，再检查用户保存的主题强调色；系统强调色开关默认关闭。项目的 Primary 使用当前实际 accent，文字仍按实际按钮背景选择黑/白。

## 形状与布局

| 参数 | 值 | 消费者 |
|---|---|---|
| `row.defaultRadius` | `10` | 无圆角表面容器的主菜单、设置列表项 |
| `row.nestedRadius` | `max(outerRadius - inset, 0)` | 有圆角表面容器的条目；见 [圆角关系](geometry.md) |
| `row.flatInset` | 左/上/右/下 `8/2/8/2` | 非卡片列表项绘制范围 |
| `row.cardInset` | `0/0/0/0` | 卡片内列表项绘制范围 |
| `navigationRow.nominalHeight` | `40` | 主菜单单行条目 |
| `settingsDetailed.contentHeight` | `36` | 设置条目内容高度，另加上下 padding |
| `settingsDetailed.padding` | 左/上/右/下 `50/10/37/12` | 带图标、副标题和右箭头的设置条目 |
| `tab.indicatorThickness` | `2` | 标签选中指示线 |
| `tab.indicatorRadius` | `1` | 指示线端点 |
| `navigation.hoverBackground` | 主题 surface 叠加 `8%` 高对比黑/白 | 导航列表独立主题键 |
| `tab.textHoverBackground` | 默认与 `navigation.hoverBackground` 同色 | 文字标签独立主题键，可单独覆盖 |
| `tab.textPressedBackground` | `luminance(tab.textHoverBackground, 1)` | 在标签悬停底色上轻微加深，亮度 `0.92`、对比度 `1.12` |
| `tab.textBackgroundInset` | `4` | 文字标签底色四侧内缩 |
| `tab.textBackgroundRadius` | `7` | 文字标签底色圆角 |

详细行的参考名义高度为 `58`；副标题换行后按真实文本尺寸增高，最终高度结合当前语言、宽度和 padding 计算。菜单、对话框和其他控件的圆角分别由对应指南维护。

导航列表和文字标签使用独立主题键，构造主题时以相同默认颜色初始化，单独覆盖互不影响；组件局部背景变化不重新派生它们。已有组件库时优先采用其对应语义键。文字标签按压在自己的悬停色上应用列表 `level=1` 轻度滤色，装饰图标局部处理，文字和指示线继续使用标签自己的前景规则。底色随指针状态立即更新，`160 ms linear` 用于选中权重与内容切换。图标标签2种背景均透明。

具有持久选中样式的控件，业务选中项不叠加悬停底色；文字标签的选中文字与指示线保留。未选中项仍使用自己的悬停色，按压按对应控件规范处理。选中目标由业务状态确定，动画过渡权重不作为选中判断。

选中文字先核对 accent 与实际背景的对比度，低于 `4.5:1` 时将选中端点定为高对比黑/白，再按权重从主题前景混合到该端点；每帧仍核对最终文字。不能先向不可用的 accent 渐变，再在途中突然切回黑/白。指示线的不透明基色优先 accent，低于 `3:1` 时取高对比黑/白，再乘选中权重作为显示 alpha；其渐入、渐出阶段允许对比度随透明度降低。

## 动画与列表按压

| 参数 | 值 | 用途 |
|---|---|---|
| `button.pressScale` | `0.94` | 普通操作按钮按下的稳定缩放目标 |
| `button.releaseScale` | `1` | 释放、移出恢复目标 |
| `button.springDuration` | `180 ms` | 按下和恢复 |
| `button.springBezier` | `(0.34, 1.56, 0.64, 1)` | 有超调的3次 Bézier，不能把目标值当瞬时下限 |
| `row.pressDuration` | `30 ms` | 列表按下反馈 |
| `row.restoreDuration` | `72 ms` | 列表悬停、离开和释放 |
| `row.easing` | `easeOutCubic` | `1 - (1 - progress)^3` |
| `row.pressBrightness` | `0.92` | 背景与装饰图标按下的稳定亮度 |
| `row.pressContrast` | `1.12` | 背景与装饰图标按下的稳定对比度 |
| `tab.crossFadeDuration` | `160 ms` | 本技能默认纯交叉渐变 |
| `tab.crossFadeEasing` | `linear` | 旧、新权重分别渐出、渐入 |

复合列表项由背景、标题、副标题、前导图标和尾部元素组成。按压只对背景与装饰性非文字视觉元素局部处理亮度和对比度，保留透明度；标题、副标题保持主题原色。令 `level` 为反馈进度：普通与悬停均为 `0`，按下为 `1`。

```text
brightness = 1 - 0.08 * level
contrast = 1 + 0.12 * max(0, level)
outputChannel = clamp(round((inputChannel * brightness - alpha/2)
                            * contrast + alpha/2), 0, alpha)
outputAlpha = inputAlpha
```

上述通道公式使用预乘 alpha 的 `0～255` 图像通道；普通与悬停亮度为 `1`，按下亮度为 `0.92`、对比度为 `1.12`。背景与图标分别处理，不给整行绘制结果套滤镜；完整列表行为见 [列表项](list-items.md)。

| 示例 | 悬停或图标原色 | 稳定按压色 |
|---|---|---|
| 白色主题的导航底色 | `#EBEBEB` | `#E3E3E3` |
| 默认浅色设置条目底色 | `#EFF2F7` | `#E7EAEF` |
| 蓝色图标底形 | `#238FE8` | `#1584E0` |
| 图标中的白色符号 | `#FFFFFF` | `#F7F7F7` |

动画倍率独立于业务和 IO 时钟。默认 `1x`，有效时长为 `baseDuration / speed`；变化从当前显示状态接续。使用项目调试能力验证慢速中间帧，再恢复原倍率。

## 参考版本与维护

参数参考版本为 AstraGram Desktop：主仓 commit `2955a62bb2828c6ab82403c1ee5d40d3a9a75430`，`Telegram/lib_ui` commit `c8391db3b5b177e6cc29c2a726b2f259953a8749`。来源保存仓库相对路径，更新参数时核对实现和实际消费者。

| 来源 | 位置 |
|---|---|
| 浅色 palette 与设置行几何 | `Telegram/lib_ui/ui/colors.palette:9`、`Telegram/lib_ui/ui/widgets/widgets.style:1564` |
| 默认 Night 色值 | `Telegram/Resources/night.tdesktop-theme` 内 `colors.tdesktop-theme:1`、`:472` |
| 列表 hover 派生 | `Telegram/SourceFiles/extras/ui/components/floating_surface.cpp:53`、`window/themes/window_themes_embedded.cpp:362` |
| Spring 与 Luminance | `Telegram/SourceFiles/extras/ui/controls/click_feedback.cpp:24`、`:41`、`:145`、`:160`、`:188` |
| 系统与用户强调色 | `Telegram/SourceFiles/window/themes/window_themes_embedded.cpp:482` |
| 材质 Hover | `Telegram/SourceFiles/extras/features/window_material/window_material.cpp:533` |
| 动画倍率 | `Telegram/lib_ui/ui/effects/animations.h:497`、`Telegram/SourceFiles/extras/debug/commands/anim_commands.cpp:252` |

项目实现按所用框架阅读 [Kotlin / Compose](frameworks/kotlin.md)、[Qt Widgets](frameworks/qt.md)、[GPUI](frameworks/gpui.md) 或 [Avalonia](frameworks/avalonia.md)，核对项目版本中的真实 API。
