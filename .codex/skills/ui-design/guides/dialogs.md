# 对话框

通用对话框使用公共容器、内容区和按钮区，圆角与投影随同一份主题参数变化；现有业务对话框复用该容器。

| 参数 | 100% 基准 |
|---|---|
| 容器圆角 | 16 |
| 阴影模糊 | 5 |
| 阴影偏移 | `(0, 1)` |
| 投影颜色 | `rgba(0, 0, 0, 0.25)` |
| 阴影外扩：左、上、右、下 | `5, 4, 5, 6` |

阴影按实际圆角生成，内容与子控件裁剪到容器轮廓；圆角变化时同步命中、裁剪和投影。对话框贴到宿主边缘时，只保留可见悬浮边的阴影，不沿贴边制造黑线。独立原生面板继续采用所属平台的窗口装饰与阴影。

底色和文字使用项目现有主题。正文、操作区与滚动内容共用容器边界；内部列表背景采用 [嵌套同心圆角](geometry.md)，由对话框提供实际圆角与背景内缩。保持默认操作、取消、键盘焦点和关闭的实际业务行为。投影细节见 [阴影](shadows.md)，对话框按钮见 [按钮](buttons.md)。

框架接入：[Qt 示例](frameworks/qt.md)、[Kotlin 示例](frameworks/kotlin.md)、[GPUI 示例](frameworks/gpui.md)、[Avalonia 示例](frameworks/avalonia.md)。分类参数转换为框架逻辑单位，像素换算与渲染接口采用项目版本的实际实现。

来源：[圆角与投影参数](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/layers/layers.style#L40)、[投影消费](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/layers/box_layer_shadow.cpp#L16)、[贴边处理](https://github.com/Kindness-Net/lib_ui/blob/c8391db3b5b177e6cc29c2a726b2f259953a8749/ui/layers/layer_widget.cpp#L86)。
