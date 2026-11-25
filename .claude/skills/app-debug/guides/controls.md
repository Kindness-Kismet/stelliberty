# 控件指令

| method | arg | 作用 |
|---|---|---|
| `control.exists` | `<automation_id>` | 断言控件存在。 |
| `control.click` | `<automation_id>` | 直接执行 Button、选择类控件、ComboBox 或 TextBox 的控件动作。 |
| `control.measure` | `<automation_id>` | 读取窗口内坐标、实际尺寸、控件及祖先合成的不透明度、勾选状态、前景色、样式类与首个图标形状。 |
| `control.transition` | `<observe_id> <click_id> [duration_ms]` | 点击目标控件，逐动画帧返回被观察控件的几何与视觉状态；默认 600 毫秒。 |
| `control.input` | `<automation_id> <value>` | 设置 TextBox、CodeEditor 或可输入 ComboBox 文本；`value` 传空串或纯空白即清空。 |
| `control.get` | `text <automation_id>` | 读取 TextBlock、TextBox、CodeEditor 或 ContentControl 文本。 |
| `dropdown.open` | `<automation_id>` | 直接设置指定 ComboBox 为打开状态。 |
| `dropdown.select` | `<automation_id> <index>` | 直接设置选中索引，验证绑定和业务状态；索引从 0 开始。 |
| `dropdown.list` | `items <automation_id>` | 读取指定 ComboBox 下拉项文本。 |
| `control.list` | `nodes <automation_id>` | 读取当前可见代理节点名。 |
| `control.list` | `rules <automation_id>` | 读取当前可见规则内容。 |
| `control.list` | `connections <automation_id>` | 读取当前可见连接 ID。 |
| `control.list` | `core-logs <automation_id>` | 读取当前可见核心日志内容。 |
| `control.scroll` | `y <automation_id> [y]` | 读取或设置控件内滚动容器 Y 偏移。 |
| `keyboard.press` | `<shortcut>` | 向当前焦点发送应用内按键或快捷键，如 `escape`、`ctrl+1`。 |
| `hotkey.trigger` | `window` / `system-proxy` / `tun` | 模拟已注册全局快捷键的动作触发，不发送系统级按键。 |

未找到控件时返回 `ok: false`、实际 `error` 与非零 `exit_code`，应用端原有错误文本保留在原因中。

`control.click` 和 `dropdown.*` 不经过操作系统输入、真实命中测试或 Popup 指针路由，不能用于证明鼠标可点击性。

`no-focus` 模式下，控件与 `keyboard.press` 只使用应用内部焦点，不激活窗口。`control.input` 直接设置文本，用于绑定与状态验证；真实文本输入、指针命中与输入法验收仍按实际输入能力选择路径。`hotkey.trigger window` 走用户快捷键的正常显示和聚焦流程，执行前须取得系统焦点操作授权。

`control.transition` 的 JSON `result` 为实际逐帧数组，适合检查宽度、位置、透明度与图标切换时序。

```bash
python .codex/skills/app-debug/scripts/run.py control.transition Sidebar.Root Settings.NarrowSidebarSwitch
python .codex/skills/app-debug/scripts/run.py control.transition Navigation.HomeLabel Settings.NarrowSidebarSwitch
```

`control.get pointer <automation_id>` 返回控件指针悬停、按下状态、背景和屏幕中心，用于验证隐藏后的输入状态。
