# 导航与设置复合列表项

使用公共列表项组件，明确 `navigation` 与 `settings` 变体；设置项按实际容器选择卡片或非卡片绘制范围。背景、文字、副标题与反馈引用 [主题参数](tokens.md)，业务动作经原控件绑定执行。

## 2类参考条目

| 参考目标 | 实际控件 | 标识与样式 |
|---|---|---|
| 账号主菜单“新建频道” | `Ui::SettingsButton` | `menu.newChannel`，`st::mainMenuButton` |
| 设置页“文本处理” | `Ui::SettingsButton` | `extras/cat/text`，`st::settingsCardDetailedButton` |

`Settings::Button`、`RowButton` 是 `Ui::SettingsButton` 别名。2类条目的独立默认圆角为 `10`；位于圆角表面容器内时采用 [嵌套同心圆角](geometry.md)。`Ui::Menu::Action` 的独立默认值 `7` 属于弹出上下文菜单，按 [菜单](menus.md) 处理。

主菜单单行条目名义高度 `40`。带副标题的设置项按实际文字布局计算高度：名义内容高度 `36`、上下 padding `10/12`，图标和标题左边距 `50`，右箭头保留 `37`。副标题使用 `settingsCardSubTextFg`，标题使用 `settingsCardFg`；宽度不足时换行并增高。

## 背景形状与颜色

```text
flat.paintRect = item.bounds.inset(left=8, top=2, right=8, bottom=2)
card.paintRect = item.bounds
radius = ancestor == null ? 10 : max(ancestor.radius - paintInset, 0)
```

没有圆角表面容器时，主菜单条目使用 flat 范围，普通背景透明，由菜单 surface 提供底色。设置卡片内条目使用整行范围。存在圆角表面容器时，靠近容器角的背景先统一相邻2边内缩，再按容器半径派生圆角；普通文字布局保持自己的 padding。圆角裁切同时覆盖底色与按压反馈，不能只裁文字或给每一层重复加圆角。

| 变体 | 普通态 | 悬停与按下的基础绘制 |
|---|---|---|
| 主菜单条目 | 透明，正文引用 `windowBoldFg` | 在 flat 范围绘制 `windowBgOver` |
| 设置卡片条目 | 当前设置 surface，标题与副标题引用各自语义色 | 在整行范围绘制 `settingsCardBgOver` |

2类基础背景不同，按压时分别对背景和装饰图标应用轻度 Luminance 反馈。默认浅色 `settingsCardBgOver=#eff2f7`，默认 Night 为 `#2d2d2d`；主菜单 `windowBgOver` 在参考实现中按底色与 `8%` 黑/白 overlay 派生。导航列表与文字标签的悬停主题键独立，默认颜色相同；组件的局部背景不重新派生它们。材质、强调色与用户主题从实际主题模型取值，不按截图保存固定最终颜色。

列表选中态是持续业务状态，悬停和按下是临时交互状态，分别保存。已有持久选中样式的条目，选中时不叠加悬停底色，保留选中底色、文字和标记。设置分类导航的选中实底引用当前 accent，文字按实际背景选择高对比色；普通动作条目没有选中状态时不额外引入它。

## 按压与取消

复合型列表项（Composite list item）由标题、副标题、前导图标和尾部元素组成。按压时背景与装饰图标轻微加深，标题和副标题保持原色：普通与悬停均为 `level=0`，按下为 `level=1`，稳定亮度 `0.92`、对比度 `1.12`。公式集中在 [主题参数](tokens.md#动画与列表按压)。

图标自身的底形、色块与符号属于同一个非文字视觉元素，局部处理这些颜色；不要给整行截图套滤镜。Checkbox、Toggle 等独立交互子控件使用自己的状态与反馈，不能因位于列表项中被当作装饰图标。

- 左键按下且位于命中范围内，进入按下态；动画从当前值到目标值，`30 ms`、`easeOutCubic`。
- 释放仍在范围内，触发1次原业务动作并恢复悬停态；释放在范围外，取消动作并恢复普通态。
- 按住后移出，恢复普通反馈；仍按住移回时重新进入按下反馈。悬停、离开和释放使用 `72 ms`、`easeOutCubic`。
- Space、Enter、Return 使用相同按压反馈，忽略自动重复；键盘操作不要求鼠标位于控件内。
- 隐藏、失焦、窗口失活立即取消反馈；禁用项停止动画并保留禁用视觉，不执行动作。

原事件绑定负责命中与动作，动画只维护展示状态。已有 ripple 被该反馈替代时停止原 ripple，避免同时叠加2套按压效果。恢复从当前显示值接续，快速移入移出或按下释放不能跳回旧目标。

主题变化时重新取得语义色；副标题和图标不是单独写死的灰色。辅助文字、键盘焦点和禁用状态按真实控件与背景核对。

## 框架接入与验证

采用 [Kotlin / Compose](frameworks/kotlin.md)、[Qt Widgets](frameworks/qt.md)、[GPUI](frameworks/gpui.md) 或 [Avalonia](frameworks/avalonia.md) 的公共状态、图标槽位和局部绘制方式。优先复用项目已有组件；背景、图标反馈与命中范围保持一致，绘制缩放不改变布局尺寸。

使用项目 app-debug 验证主菜单单行项与带副标题设置项：普通、悬停、按下、范围内释放、移出释放取消、按住移回、键盘触发、失焦取消、禁用。浅色与深色分别核对背景与图标轻度变化、标题及副标题不变；窄宽度核对副标题换行后的命中范围。

## 来源

稳定版本见 [主题参数的参考版本](tokens.md#参考版本与维护)。下列来源属于参考实现；实际项目按以上语义组件接入。

| 内容 | 仓库相对位置 |
|---|---|
| “新建频道”的标识与业务绑定 | `Telegram/SourceFiles/window/window_main_menu.cpp:716`、`:761` |
| 主菜单样式 | `Telegram/SourceFiles/window/window.style:145` |
| “文本处理”的标题、副标题与目标 | `Telegram/SourceFiles/extras/ui/settings/settings_main.cpp:65` |
| 有副标题时的样式选择 | `Telegram/SourceFiles/settings/settings_builder.cpp:410` |
| 详细行样式 | `Telegram/SourceFiles/settings/settings.style:123` |
| 创建卡片行与主题色 | `Telegram/SourceFiles/settings/settings_common.cpp:530` |
| flat / card 绘制范围 | `Telegram/lib_ui/ui/widgets/buttons.cpp:1046`、`Telegram/lib_ui/ui/widgets/widgets.style:1564` |
| Luminance 与取消事件 | `Telegram/SourceFiles/extras/ui/controls/click_feedback.cpp:84`、`:179`、`:188` |
| 释放命中后的原点击行为 | `Telegram/lib_ui/ui/abstract_button.cpp:180` |
