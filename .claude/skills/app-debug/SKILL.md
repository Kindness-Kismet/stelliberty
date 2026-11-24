---
name: app-debug
description: 调试 Stelliberty 的 Debug 应用，验证页面、控件与业务改动，维护业务调试指令和指引。用户要求调试、验证功能、检查 UI、切页、截图或启停应用，以及业务逻辑新增或变更、调试任务需要补充指令时使用。
---

# 应用调试

先阅读根目录 AGENTS.md，按任务选择下列指南。统一入口为 `python .codex/skills/app-debug/scripts/run.py`；Claude 使用 `.claude/skills` 镜像，两个入口从自身位置定位同一项目。

## 应用身份与结果

- 唯一桌面宿主承载窗口、托盘和后台编排，Windows 入口为 `stelliberty.exe`；Debug 产物按 Directory.Build.props 与当前平台定位。
- 验证其他工作树时可用 `STELLIBERTY_DEBUG_ROOT` 显式指定项目根目录，产物定位与进程路径校验随之切换。
- Debug 服务监听 `127.0.0.1:20000`。客户端先校验端口 PID 的可执行路径，连接已有实例时不重复启动或激活窗口。
- 操作向 stdout 返回一个 JSON：成功包含 `ok`、`command`、`result`，失败包含 `ok: false`、`error`、`exit_code`；诊断与子工具输出送 stderr。
- 单次调用可用独立 `+` 串联命令；整组参数先解析再执行，按顺序返回 `steps`，运行失败保留完成步骤与 `failed_index`，并停止后续动作。
- 应用端的 JSON 返回为结构化 `result`，现有文本协议保留文本结果；业务状态和受理结果按指南中的实际返回内容判断。

## 按任务阅读指南

| 任务 | 指南 |
|---|---|
| 启停、连接、实例、记录与恢复 | [运行](guides/runtime.md) |
| 窗口、焦点与隐藏恢复 | [窗口](guides/windows.md) |
| 托盘与完整退出 | [托盘](guides/tray.md) |
| 截图与文件 | [截图](guides/screenshots.md) |
| 逐帧动画观察 | [动画](guides/animations.md) |
| 控件动作、输入、下拉框、读取与滚动 | [控件](guides/controls.md) |
| 页面导航、设置子页与滚动 | [页面](guides/pages.md) |
| 核心、系统代理、TUN、服务模式 | [首页](guides/home.md) |
| 代理组、节点、延迟 | [代理](guides/proxies.md) |
| 连接过滤、关闭与详情 | [连接](guides/connections.md) |
| 核心日志与核心状态 | [核心日志](guides/core_logs.md) |
| 规则过滤、搜索与编辑 | [规则](guides/rules.md) |
| 订阅、Provider 与链式代理 | [订阅](guides/subscriptions.md) |
| 覆写导入、编辑与排序 | [覆写](guides/overrides.md) |
| 语言、主题、行为、更新与数据管理 | [设置](guides/settings.md) |
| Clash 配置键 | [Clash](guides/clash.md) |

## 执行流程

1. 继续任务时按 [AGENTS 的方案与记录约定](../../../AGENTS.md#方案与记录) 恢复现场，并按其增量验证范围选择所需操作。
2. 执行前查询入口 `--help` 和 `<命令> --help`，确认真实指令、参数和取值。帮助只加载本地命令声明，不连接应用、写文件或检查工具链。
3. 应用源码或影响产物的构建输入变化后调用 AGENTS 的 Debug 构建入口。XAML 单独修改复用 HotAvalonia，布局不更新或异常时再重启；缺少产物按诊断构建，缺资源按 prebuild 入口处理。
4. 先查询 `app.status` 核对实例，再按指南连接或启动。启动、显示、页面、交互、截图与退出均串行执行，同一端口不并发调用。
5. 业务操作经实际 ViewModel、用例与应用入口执行，设置项传入显式值；界面与绑定验证使用稳定 AutomationId。当前控件动作与真实输入路由的覆盖含义按控件指南区分。
6. 核对实际业务状态、页面内容和动画端点；逐帧几何变化不替代系统鼠标命中验证。窗口行为专项验证按用户明确要求执行，窗口修改默认以源码与构建核对收尾。
7. 结束后按约定恢复临时设置与数据，经 `app.stop` 正常退出并核对实际进程终态。

## 输入与焦点

- 技能启动必须显式传入 `app.start --no-focus`、`app.ensure --no-focus` 或 `app.restart --no-focus`；普通手动启动 Debug 产物不带该参数时正常聚焦。
- `--no-focus` 控制新实例启动；运行中 `--focus-mode focus|no-focus` 切换策略，省略时继承，切换本身不显示或激活窗口。已有实例复用连接，目标不存在时模式切换失败且不启动。
- 控件与键盘指令仅作用于应用内部。移动系统光标、发送全局输入、占用系统输入捕获或激活窗口前，说明具体目标、动作和影响并取得明确同意；普通调试请求不包含这项同意。
- Windows `window.verify-hover` 使用真实系统鼠标，只有获得对应授权后才执行。系统代理、TUN、服务安装、提权、数据恢复或删除采用对应任务的明确范围。

## 进程与证据

- `app.stop` 先发送内部 `app.quit`，由宿主清理核心、系统代理与托盘；异常清理只针对已核对的当前 Debug 可执行路径和 PID。
- 构建输出占用清理由共享 `scripts/build_support/processes.py` 提供，不加载技能。调试停止通过本入口调用共享能力，不手写进程名通配符或额外杀进程命令。
- 默认截图沿用现有应用合成 PNG，保存到 `build/screenshots`；逐帧观察使用 `control.transition`，参数和实际返回字段见相关指南。

## 维护

- 业务或调试能力新增、移除、改名或行为变化时，同步更新实际应用端、`scripts/commands.py`、客户端、指南和本索引；只公开已实现能力。
- `run.py` 负责解析和 JSON 编排，commands.py 负责声明，client.py 负责分发，runtime.py 负责连接与进程。
