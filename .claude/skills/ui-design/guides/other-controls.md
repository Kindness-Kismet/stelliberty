# 输入、选择、滚动与提示

这些控件有各自的输入与状态语义，优先采用项目已有组件、主题和框架原生能力。没有现成视觉约定时，按具体控件补足必要状态，不直接套按钮、标签或菜单项的效果。

| 家族 | 需要保留的区别与参考 |
|---|---|
| 普通输入框 | 文字编辑、选择、焦点、占位与错误反馈；参考组件使用普通底边 1、聚焦底边 2、圆角 0，圆角输入变体沿用实际设计 |
| 搜索框 | 胶囊轮廓、搜索和清除动作；默认高度 36、圆角 18，参考组合见 [搜索框投影](shadows.md#搜索框参考投影) |
| 密码、数字、时间输入 | 复用输入基类，保留遮罩、允许的字符、范围与提交规则 |
| Checkbox / Radio / Toggle | 分别表达多选、组内单选、即时开关；使用实际选中、禁用与焦点状态，开关样式不借用菜单选择高亮 |
| 多选标签输入 | 标签添加/删除、输入与清除、内部滚动共同组成1个控件，不拆成互不关联的按钮 |
| 滚动区 | 内容滚动、滑块拖动与可见性；自动隐藏与常驻滚动区分别复用，滚动阴影跟随内容位置更新 |
| Tooltip | 普通悬停说明，参考圆角 3、细边框；保持不截断必要内容 |
| ImportantTooltip | 带箭头的定位提示，参考圆角 4、箭头 4，按触发目标计算位置 |
| Toast | 操作成功、错误等动态结果的统一形式，参考圆角 8；自动消失与关闭按组件规范处理，浮层投影使用统一参数 |

颜色使用现有主题的文本、背景、边界、选中和错误角色。组件新增的状态使用同一来源；输入、滚动与提示的行为通过真实控件验证。

动态操作结果使用 Toast，静态常驻提示放在对应界面；两者保持简短。具体文案要求见 [界面文案](content.md)。

框架接入：[Qt 示例](frameworks/qt.md)、[Kotlin 示例](frameworks/kotlin.md)、[GPUI 示例](frameworks/gpui.md)、[Avalonia 示例](frameworks/avalonia.md)。已有 Slider 或分段选择按其实际用途和组件边界处理，相关切换约定见 [标签](tabs.md)。

来源：[普通输入](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/widgets.style#L1043)、[选择与开关](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/widgets.style#L845)、[滚动](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/widgets.style#L814)、[多选输入](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/widgets.style#L1183)、[定位提示](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/widgets.style#L1346)、[普通 Tooltip 绘制](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/tooltip.cpp#L178)、[Toast](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/widgets/widgets.style#L1610)。
