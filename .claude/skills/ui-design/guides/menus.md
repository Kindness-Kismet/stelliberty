# 下拉与上下文菜单

按容器用途选择公共菜单组件，不把上下文菜单项当成设置列表行或普通操作按钮。

| 控件 | 100% 圆角 | 内容布局 |
|---|---:|---|
| PopupMenu 上下文弹出菜单 | 12 | 内容四侧名义留白各 5，按当前 DPI 统一吸附 |
| InnerDropdown 内嵌下拉面板 | 8 | 容器四侧留白各 10 |
| PopupMenu 内条目的悬停或选中底 | 7 | 无额外背景内缩，圆角由外框与有效留白派生 |
| 无圆角祖先的独立菜单项 | 7 | 左右内缩各 6，上下内缩各 1 |

菜单容器使用 [公共投影](shadows.md)：模糊 5、偏移 `(0, 1)`、颜色 `rgba(0, 0, 0, 0.25)`，预留外扩 `5, 4, 5, 6`。背景、文本、禁用态、分隔线和危险操作沿用现有主题。PopupMenu 与 InnerDropdown 的容器圆角不同，内部条目的圆角按 [嵌套同心圆角](geometry.md) 派生；相邻2边不等距时先调整背景内缩，不能只改半径。

PopupMenu 第1项上角与最后一项下角对应外框角，按实际边距检查圆心；中间条目沿用组内圆角，不与外框共享圆心。容器留白、条目宽度和总高度按同一有效内缩计算，避免 DPI 取整造成左右或上下错位。

## 菜单外点击

菜单展开后，菜单及其子菜单之外的指针按下关闭整个菜单链，并吞掉该次输入序列。底层控件不接收该次按下、释放或 click，菜单触发器也不能因同一次点击再次展开。

复用组件库已有的 outside-dismiss 与输入屏蔽能力；未覆盖时使用覆盖宿主内容区的透明输入屏障，菜单本体位于屏障上方。屏障在按下时请求关闭，将指针捕获保持到释放或取消后再移除；不能先关闭再把事件重新派发给底层。无论事件来自鼠标、触控还是触笔，每次指针序列只关闭1次。

菜单阴影不算内容命中范围，点击阴影也按菜单外处理。菜单项内点击只执行所选业务动作；进入子菜单不关闭父菜单，禁用项不执行动作。关闭后清理悬停、按压与捕获状态，并恢复合理焦点。`Escape` 关闭菜单；窗口失活按原组件生命周期处理。

验收时让菜单覆盖业务按钮，点击按钮露出的区域：菜单关闭，按钮动作计数保持不变；下一次独立点击才触发按钮。另核对触发器点击不重开、子菜单内部点击、范围外释放与隐藏后重新展开的状态。

保持实际键盘导航、焦点、禁用项、子菜单展开和关闭行为；选择动作绑定真实业务入口。子菜单位置按内容区与阴影外扩计算，不能将阴影空间当成菜单项的命中区域。

窗口材质采用平台原生方案时，由原生窗口负责圆角和外框投影。Windows 的 DWM 圆角由系统策略控制，实际效果通过对应平台核对。

框架接入：[Qt 示例](frameworks/qt.md)、[Kotlin 示例](frameworks/kotlin.md)、[GPUI 示例](frameworks/gpui.md)、[Avalonia 示例](frameworks/avalonia.md)。设置与导航列表项另见 [列表项](list-items.md)。

来源：[菜单参数](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/widgets.style#L970)、[内嵌下拉参数](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/widgets.style#L1311)、[菜单项绘制](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/menu/menu_item_base.cpp#L25)。

InnerDropdown 的投影取 animation.shadow，见 [构造](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/inner_dropdown.cpp#L17) 与 [绘制](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/inner_dropdown.cpp#L127)。原生材质分支见 [菜单适配](https://github.com/Kindness-Kismet/AstraGram-Desktop/blob/2955a62bb2828c6ab82403c1ee5d40d3a9a75430/Telegram/SourceFiles/extras/features/window_material/window_material.cpp#L323)。
