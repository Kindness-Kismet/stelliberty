# 阴影

悬浮容器共用默认投影，圆角、模糊、偏移与强度集中到主题参数。按钮、普通列表项和标签默认无阴影；字形投影、细分隔线和平台原生窗口阴影分别维护。

## 公共容器

| 用途 | 模糊 | 偏移 | 颜色与 alpha | 外扩：左、上、右、下 |
|---|---:|---|---:|---|
| 对话框、弹出菜单、内嵌下拉和其他悬浮容器 | 5 | `(0, 1)` | `rgba(0, 0, 0, 0.25)` | `5, 4, 5, 6` |

投影 RGB 为 `#000000`，alpha 为 `0.25`；8 位 alpha 量化为 `64`，RGBA 十六进制记为 `#00000040`。控件圆角按各自指南设置，贴边一侧保留实际可见投影，同一组参数由公共样式维护。

尺寸为 100% 逻辑单位。投影由实际圆角轮廓生成，容器不透明底覆盖内部阴影；内容裁剪在内层，投影绘制在外层。布局预留按 `max(blur ± offset, 0)` 计算，容器与截图缓存保留这段空间，贴着宿主的边不画投影。

不同框架的 blur 参数含义不同，公共预设按下表映射到 API，实际像素轮廓用对应渲染器核对。DPI 换算应用于几何、模糊和缓存尺寸，避免重复缩放。原生窗口已提供投影时，复用其装饰方案。

| 框架 | 参数映射 |
|---|---|
| Compose | `Modifier.dropShadow`：`radius=5.dp`、`offset=DpOffset(0.dp,1.dp)`、`spread=0.dp`、`Color(0f,0f,0f,0.25f)` |
| Qt | `QGraphicsDropShadowEffect`：`blurRadius=5`、`offset=(0,1)`、`QColor(0,0,0,64)` |
| GPUI | `BoxShadow`：`blur_radius=px(5)`、偏移 `px(0),px(1)`、alpha `0.25`、spread `0` |
| Avalonia | `BoxShadow`：`Blur=5`、`OffsetX=0`、`OffsetY=1`、`Spread=0`、`Color.FromArgb(64,0,0,0)` |

## 搜索框参考投影

参考搜索框使用紧层与宽层共同划分边界：紧层模糊 2、宽层模糊 5，2层偏移均为 `(0, 1)`，合并外扩 `5, 4, 5, 6`。圆角为控件高度的一半，默认高度 36、圆角 18。该组合按实际搜索框样式选用，不作为普通控件的默认投影。

下表每格依次为普通、悬停、聚焦状态；聚焦过渡按实际进度插值。

| 主题 | 紧层不透明度 | 宽层不透明度 | 细边不透明度 | 底色向白色混合 |
|---|---|---|---|---|
| 浅色 | `.06 / .07 / .08` | `.08 / .11 / .16` | `.08 / .12 / .08` | `0 / 0 / 0` |
| 深色 | `.40 / .45 / .45` | `.28 / .32 / .40` | `.07 / .10 / .09` | `.05 / .075 / .085` |

参考实现用底色 `lightnessF() < .5` 区分浅暗外观；接入已有主题系统时复用其主题状态，不另建1套设置。2个样式预设中的 opacity=1 用于生成投影轮廓，实际绘制强度由上表覆盖。

## 参考项目的预设分类

固定源码中共有 14 个命名 BoxShadow 预设、12 种不同参数，以下动态投影的 RGB 均为 `#000000`。它们按用途区分，通用控件不需要复制全部业务预设。

| 其他预设用途 | 模糊 / 偏移 / 不透明度 | 参数来源 |
|---|---|---|
| 表情选择器 | `20 / (0,6) / .22` | [emoji_picker_overlay.style:41](https://github.com/Kindness-Kismet/AstraGram-Desktop/blob/2955a62bb2828c6ab82403c1ee5d40d3a9a75430/Telegram/SourceFiles/chat_helpers/emoji_picker_overlay.style#L41) |
| 列表提示、编辑器胶囊、钱包信息岛 | `3 / (0,1) / .09` | [dialogs.style:833](https://github.com/Kindness-Kismet/AstraGram-Desktop/blob/2955a62bb2828c6ab82403c1ee5d40d3a9a75430/Telegram/SourceFiles/dialogs/dialogs.style#L833)、[iv.style:131](https://github.com/Kindness-Kismet/AstraGram-Desktop/blob/2955a62bb2828c6ab82403c1ee5d40d3a9a75430/Telegram/SourceFiles/iv/iv.style#L131)、[wallet.style:157](https://github.com/Kindness-Kismet/AstraGram-Desktop/blob/2955a62bb2828c6ab82403c1ee5d40d3a9a75430/Telegram/SourceFiles/wallet/wallet.style#L157) |
| 钱包浮卡、卡片前景 | `18 / (0,2) / .22`、`3 / (0,1) / .45` | [wallet.style:75](https://github.com/Kindness-Kismet/AstraGram-Desktop/blob/2955a62bb2828c6ab82403c1ee5d40d3a9a75430/Telegram/SourceFiles/wallet/wallet.style#L75) |
| 地图定位点 | `6 / (0,2) / .29` | [chat.style:528](https://github.com/Kindness-Kismet/AstraGram-Desktop/blob/2955a62bb2828c6ab82403c1ee5d40d3a9a75430/Telegram/SourceFiles/ui/chat/chat.style#L528) |
| 资料顶部操作按钮 | `1 / (0,0) / .25` | [info_profile_top_bar.style:70](https://github.com/Kindness-Kismet/AstraGram-Desktop/blob/2955a62bb2828c6ab82403c1ee5d40d3a9a75430/Telegram/SourceFiles/info/profile/info_profile_top_bar.style#L70) |
| infoProfileTabsShadow | `8 / (0,2) / .18` | [info.style:867](https://github.com/Kindness-Kismet/AstraGram-Desktop/blob/2955a62bb2828c6ab82403c1ee5d40d3a9a75430/Telegram/SourceFiles/info/info.style#L867)，维护源码中没有消费者 |

另外有3套仍被绘制的九宫格纹理阴影：defaultRoundShadow 用于 [颜色编辑器](https://github.com/Kindness-Kismet/AstraGram-Desktop/blob/2955a62bb2828c6ab82403c1ee5d40d3a9a75430/Telegram/SourceFiles/ui/widgets/color_editor.cpp#L387)，外扩 `3, 2, 3, 4`；roundShadowRadius8px 用于 [播放器浮层](https://github.com/Kindness-Kismet/AstraGram-Desktop/blob/2955a62bb2828c6ab82403c1ee5d40d3a9a75430/Telegram/SourceFiles/media/player/media_player_panel.cpp#L188)，四侧外扩 10；callShadow 用于 [独立面板](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/separate_panel.cpp#L1127) 与 PiP，外扩 `9, 8, 9, 10`。纹理轮廓不对应1组可调 blur/opacity；沿用现有组件时保留其真实资源边界，不把旧样式字段误认成当前动态投影。

框架接入：[Qt 示例](frameworks/qt.md)、[Kotlin 示例](frameworks/kotlin.md)、[GPUI 示例](frameworks/gpui.md)、[Avalonia 示例](frameworks/avalonia.md)。

来源：[公共投影](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/widgets.style#L926)、[对话框与侧栏](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/layers/layers.style#L92)、[生成与外扩](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/shadow.cpp#L199)、[搜索框3态](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/fields/input_field.cpp#L1665)、[逻辑尺寸换算](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/style/style_core_scale.h#L36)。
