# 运行控制与观察

## 应用指令

| method | arg | 作用 |
|---|---|---|
| `app.start` | `--no-focus` / `--focus-mode focus\|no-focus` | 启动 Debug 应用，已有实例时只复用连接；返回窗口状态和焦点模式。 |
| `app.status` | 空 | 查询已运行 Debug 实例的窗口状态与焦点模式，未运行时返回错误。 |
| `app.ensure` | `--no-focus` / `--focus-mode focus\|no-focus` | 确保调试界面运行并显示，已隐藏的界面会重新显示；XAML 变更等待自动热重载，不自动打包。 |
| `app.restart` | `[--no-focus] [--silent-start] [--startup-delay-ms 0..30000]` | 重启已有 packaged Debug 应用；默认显示窗口，静默启动时仅等待调试端口，窗口保持隐藏。可组合延迟本次界面数据初始化，默认不延迟，不自动打包。 |
| `app.stop` | `[--output-dir <目录>]` | 先请求应用清理并退出；失败时按精确可执行文件路径停止进程。默认当前调试产物，显式目录仅允许本仓库当前平台的构建输出，并兼容旧拆分产物。 |
| `app.memory` | 空 | 读取进程编号、私有内存、工作集、托管堆、GC 已提交内存和控件数量。 |

## 观察指令

| method | arg | 作用 |
|---|---|---|
| `screenshot.take` | 空 | 截图当前窗口，JSON `result` 返回本次 PNG 的 `path` 与 `format`；文件位于 `build/screenshots/`。 |
| `clipboard.read` | 空 | 读取当前窗口剪贴板文本。 |
| `toast.state` | 空 | 读取当前 toast 是否显示和文本，用于验证错误提示链路。 |
| `window.state` | 空 | 读取当前窗口位置、大小、可见状态、激活状态（`active`）、`focus_mode` 和所在屏幕工作区。 |
| `window.appearance.state` | 空 | 读取当前页面、实际透明级别、窗口及表面不透明度、Windows 桌面合成状态。 |
| `window.appearance.probe` | `[duration_ms]` | 亚克力模式下启动合成更新采样，默认 2000 毫秒，允许 200～300000；立即返回，可继续串行切页。 |
| `window.appearance.stop` | 空 | 提前停止采样，返回并记录总样本数、异常数、属性变化数、最大采样间隔与逐页统计。 |
| `window.minimize` | 空 | 普通最小化，不隐藏到托盘；返回与 `window.state` 相同的状态字段。 |
| `window.maximize` | 空 | 设置为最大化，重复调用不会还原；返回窗口状态。 |
| `window.restore` | 空 | 从最小化或最大化恢复为普通窗口大小，重复调用保持普通状态；返回窗口状态。 |
| `window.move` | `x y` | 移动当前窗口到指定屏幕坐标，用于验证位置保存/恢复。 |
| `window.resize` | `width height` | 调整当前窗口尺寸并回写窗口状态；低于窗口最小尺寸时停在下限，用于验证最小宽度下的布局。 |
| `window.close` | 空 | 执行主窗口关闭动作；启用最小化到托盘时隐藏窗口，持续隐藏 5 秒后回收旧合成资源并保留页面实例，进程和核心继续运行；未启用最小化到托盘时完整退出。 |
| `window.show` | `[--focus-mode focus\|no-focus]` | 按当前焦点模式显示主窗口；`no-focus` 保持系统焦点和后台层级。 |
| `control.catalog` | 空 | 扫描 `Views` 下的 XAML，输出稳定 `AutomationId` 对应的 app-debug 触发指令。 |
| `control.list` | 空 | 列出当前窗口已加载控件的 `AutomationId`、控件类型、可见状态。 |
| `control.list --visible` | 空 | 只列出当前窗口可见控件，适合找模拟点击目标。 |

最小化、最大化和还原只设置窗口状态，不主动显示已隐藏到托盘的界面；需要重新显示时用 `window.show`，页面已被回收时也会自动重建。窗口尺寸可能等待系统布局后才更新，验证尺寸时再查询 `window.state`。

亚克力验收先设置 `settings.window-effect.set acrylic`，再启动 `window.appearance.probe 120000`，串行切页后执行 `window.appearance.stop`。每次属性变化都会立即记录，补充同帧内失效后又恢复的情况；到期或窗口关闭自动结束。日志位于当前调试包的 `data/applogs/running.logs`。

合成采样核对实际透明级别、窗口与表面不透明度，以及 Windows 11 的 `DwmIsCompositionEnabled` 查询结果；宿主背景标志不支持查询，不把查询错误当作材质关闭。通过要求为总样本数大于零、各目标页都有样本、异常样本与异常属性变化均为零；最大采样间隔同时反映界面线程阻塞造成的观察空档。截图保存的是应用视觉树，不包含系统桌面合成效果，不能单凭截图断言材质连续。

## PC 焦点模式

普通手动启动不带焦点参数时正常聚焦。调试启动显式指定 `--no-focus`，在窗口首次显示前生效；运行中通过 `--focus-mode focus|no-focus` 更新实例策略，省略参数和重连继承当前模式。

`--no-focus` 支持 `app.start`、`app.ensure`、`app.restart`；`--focus-mode` 支持 `app.start`、`app.ensure` 和 `window.show/close/move/resize/minimize/maximize/restore`。参数可放在指令前或后，同一命令段两者互斥。切换策略要求实例已经运行，目标不存在时返回错误。

`app.start` 已有实例时只复用连接；`app.start --no-focus` 保留该实例模式。`app.start --focus-mode <模式>` 只切换策略并返回状态，不显示或激活窗口。`app.ensure` 和窗口动作在同一 UI 操作中应用指定策略，再执行动作。`app.status` 和 `window.state` 返回实际 `focus_mode=focus|no-focus`。

```bash
python .codex/skills/app-debug/scripts/run.py app.start --no-focus
python .codex/skills/app-debug/scripts/run.py app.start --focus-mode no-focus
python .codex/skills/app-debug/scripts/run.py window.show --focus-mode no-focus + window.maximize + window.restore
python .codex/skills/app-debug/scripts/run.py --focus-mode no-focus window.resize 1040 680
```

调试窗口与控件操作遵守 [SKILL.md 的输入授权要求](../SKILL.md#输入与焦点)。窗口行为专项验证按用户明确要求执行。

## 单进程生命周期与电源恢复

静默启动使用 `app.restart --no-focus --silent-start`，随后通过 `window.state` 确认 `visible=false`，再用 `window.show` 或 `app.ensure --no-focus` 显示。可与 `--startup-delay-ms` 同时使用。

窗口和托盘使用同一个应用进程；隐藏窗口后端口继续存在，显示窗口应复用原进程。可结合 `app.memory` 的 `pid`、`window.state` 和 `core.state` 核对进程、窗口及核心状态。隐藏后等待 5 秒会回收旧合成资源并保留页面实例；重新显示后页面恢复，核心运行时长继续累加。

| 指令 | 作用 |
|---|---|
| `app.power-state` | 返回电源恢复快照和平台电源监听状态 |
| `app.power-event suspend` / `app.power-event resume` | 向实际恢复调度器发送模拟电源通知，不改变电脑电源状态；唤醒异步执行，通过 `app.power-state` 查询结果 |

`PowerRecovery` 和 `PowerMonitorStatus` 可用于反馈休眠恢复阶段、尝试次数、事件来源和平台监听状态。电源恢复的 `Phase`：0 空闲、1 休眠、2 等待、3 恢复中、4 完成、5 跳过、6 失败、7 取消。模拟通知使用与系统通知相同的业务入口，不能替代真实休眠、电源驱动或网络设备恢复验证。

启动加载观察可用 `app.restart --no-focus --startup-delay-ms 15000`，随后点击导航控件并查询 `page.state`。延迟只作用于调试界面数据初始化，不改配置，也不影响正式版。

`app.verify` 运行仓库既有构建后业务测试，涉及服务模式和页面业务；`app.verify --shortcuts` 验证窗口快捷键与隐藏恢复流程。按用户指定范围选择验证，测试结束后关闭本次调试进程。

`window.verify-hover [--assert-cleared]` 仅限 Windows：在用户允许暂时接管鼠标时，按 AutomationId 定位并真实点击关闭按钮，检查快速显示与隐藏回收后的悬停；保存到 build/screenshots，最后恢复鼠标位置和最小化到托盘设置。默认记录状态，--assert-cleared 要求隐藏与恢复后均无旧悬停。

## 记录与恢复

调试记录、验收证据与恢复流程按 [AGENTS 的方案与记录约定](../../../../AGENTS.md#方案与记录) 执行，截图与日志引用工具原始输出位置。
