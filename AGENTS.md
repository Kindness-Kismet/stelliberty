# Stelliberty 项目约定

## 项目结构与架构

Stelliberty 是使用 Avalonia 与 .NET 11 构建的跨平台代理客户端，采用模块化分层、Clean Architecture 与 MVVM。

| 目录 | 职责 |
|---|---|
| `src/Stelliberty.Desktop` | 桌面宿主、窗口、托盘、生命周期、系统代理、服务模式、全局快捷键和后台任务 |
| `src/Stelliberty.Presentation` | ViewModel、可观察 UI 状态与命令绑定 |
| `src/Stelliberty.Application` | 用例、服务与平台能力接口、应用状态编排 |
| `src/Stelliberty.Domain` | 实体、值对象、领域规则与纯业务逻辑 |
| `src/Stelliberty.Infrastructure` | 文件、外部服务、持久化与核心通信适配 |
| `src/Stelliberty.Native` | C# 到 Rust 的互操作包装与 Application 能力实现 |
| `native/hub` | 聚合计算密集、解析、加密、压缩、搜索和跨平台底层能力 |
| `native/service` | 独立系统服务与核心进程托管 |
| `native/xtask` | 构建期 FFI 绑定生成 |
| `tests` | 已有业务规则测试；执行入口为 `scripts/test.py` |

依赖方向为 `Desktop -> Presentation -> Application -> Domain`。Desktop 同时组装 Infrastructure 与 Native；后两者依赖 Application 的能力接口，Application 不依赖 Avalonia、桌面生命周期、文件系统或 FFI 细节。

- View 只负责展示与绑定，不直接访问文件、数据库或 FFI。ViewModel 不持有平台 API、文件路径或控件实例；平台能力由桌面宿主经 Application 接口提供。
- Domain 不依赖 UI、网络、日志、数据库或配置文件。耗时任务在后台执行，UI 调度器只提交展示状态。
- C# 负责 UI、状态与普通业务编排；Rust 负责纯逻辑和必要底层能力，不因技术栈齐全而迁移普通业务。Rust 不感知 C#、Avalonia 或窗口生命周期，C# 不复制 Rust 算法。
- `native/hub` 的 `lib.rs` 只声明模块，根 `ffi.rs` 聚合入口；`capabilities` 依赖 `infra` 与 `util`，能力之间不互相依赖，复用逻辑先进入 `util`。能力按职责自包含，超过 500 行时拆为同名模块目录。
- FFI 统一 UTF-8 和固定布局，C# 侧声明对应 StructLayout，共用类型优先使用 interoptopus。全部入口具有 C# 包装，负责错误转换、内存释放、日志上下文与取消协作，业务不直接调用裸入口。

### UI 与测试定位

- 所有可点击、输入、选择或断言状态的 Avalonia 控件必须设置稳定的 `AutomationProperties.AutomationId`，同一 View 内唯一。
- ID 使用 `页面或区域.语义名称`，不随语言、索引或布局变化；动态行使用稳定业务 ID。UI 自动化仅通过 AutomationId 定位。

### 桌面宿主与运行边界

- 发布根目录的 `stelliberty.exe` 是 Windows 唯一桌面入口，其他桌面平台使用对应无扩展名入口。窗口与托盘处于同一进程，界面通过 Application 直接访问同进程服务。
- 普通重复启动激活现有窗口；静默启动保留托盘与后台任务。隐藏后保留页面实例，满 5 秒脱树，使用 Avalonia 公开 API 解除旧合成关系并执行 GC；再次显示接回原页面并取消尚未执行的释放。
- 隐藏窗口继续运行核心、调度与 Debug 端口；完整退出统一清理窗口、托盘、核心和系统代理。普通核心与系统服务保持独立生命周期和通信边界。
- 构建、安装、自启动与提权重启统一指向桌面入口。资源和依赖布局由 `scripts/build.py` 生成，不为托盘与界面添加会话协议或客户端适配层。

## 配置来源

| 配置 | 唯一来源与消费者 |
|---|---|
| 应用身份、版本、目标框架与 C# 编译约束 | [Directory.Build.props](Directory.Build.props)，构建脚本与 MSBuild 共同消费 |
| 桌面依赖、Avalonia 版本、资源与 Debug 热重载 | [桌面项目](src/Stelliberty.Desktop/Stelliberty.Desktop.csproj) |
| .NET 项目集合 | [Stelliberty.slnx](Stelliberty.slnx) |
| Rust workspace、lint 与构建 profile | [Cargo.toml](Cargo.toml)，依赖解析由 [Cargo.lock](Cargo.lock) 固定 |
| 平台、构建输出与安装资源布局 | [scripts/build_support](scripts/build_support)，预编译资源位于 `build/pre_assets` |
| 设置模型与持久化 | [AppSettings.cs](src/Stelliberty.Application/Settings/AppSettings.cs)、[JsonAppSettingsStore.cs](src/Stelliberty.Infrastructure/Settings/JsonAppSettingsStore.cs) |
| 运行数据目录与路径约定 | [DesktopApplicationLayout.cs](src/Stelliberty.Desktop/Services/DesktopApplicationLayout.cs)、[PathConventions.cs](src/Stelliberty.Application/Platform/PathConventions.cs) |
| 界面文案与语言资源 | [Localization/Assets](src/Stelliberty.Infrastructure/Localization/Assets)：`en.json`、`zh-Hans.json`、`zh-Hant.json`；使用现有本地化服务与设置持久化 |
| 主题、控件与样式 | [Desktop/Styles](src/Stelliberty.Desktop/Styles)，窄侧边栏配置位于 [SidebarStyles.axaml](src/Stelliberty.Desktop/Styles/SidebarStyles.axaml)；实际依赖以桌面项目的 Semi.Avalonia 与 Fluent 引用为准 |
| 发布渠道与更新日志 | [.github/workflows](.github/workflows)、[CHANGELOG.md](.github/CHANGELOG.md)；默认分支 main，普通贡献目标 beta，beta 到 main 的提升由仓库所有者执行 |
| PR 正文模板 | [.github/pull_request_template.md](.github/pull_request_template.md) |
| Git 收纳边界 | [.gitignore](.gitignore)：规范、两侧完整技能与 `.pi/settings.json` 共享；`.pi` 其他文件、产物、缓存、备份和私人配置忽略 |
| 技能换行 | [.gitattributes](.gitattributes) 固定技能源与镜像为 LF，保持跨平台字节一致 |
| 技能主源与镜像 | [.codex/skills](.codex/skills)、[.claude/skills](.claude/skills)；主源单向复制到镜像并校验文件集合与字节，不生成同步状态文件 |
| Pi 技能加载 | [.pi/settings.json](.pi/settings.json)，`skills` 仅引用 `../.codex/skills`；CLAUDE.md 仅引用本文件 |

## 任务连续性与验证

- 默认不新增或修改测试代码，只有用户明确要求编写测试时才执行；必要验证采用构建、已有测试或应用调试。
- 已完成且明确通过的验证作为后续工作的有效依据。新任务、切换 Bug 或新一轮修改本身不触发重查、重测或重复分析。
- 执行验证前先查已有记录，只分析本次改动及实际依赖关系。优先验证最小受影响功能；独立功能分别保留结果，公共接口、共享模块或集成流程受到影响时才扩大对应范围。
- 仅在改动影响原验证覆盖的行为，相关接口、依赖或共享状态变化，环境或构建配置使原结果失效，出现质疑原结果的新证据，或用户明确要求时，重新执行对应验证。同一文件、模块或项目不构成重跑依据。
- 未受影响的通过项直接继承；完成后只汇报本次修改和实际执行的验证。已完成的工作作为后续基础，验证范围由实际影响决定。

## 技能使用

技能完整操作由对应主文件与指南维护；Pi 使用 Codex 主源，Claude 使用镜像。主源与镜像均由 Git 追踪，不创建技能修改前备份；每批主源修改后立即运行工程入口中的技能同步与只读检查，关联变更一起提交。镜像上的独立修改先归入主源，再执行单向同步。

- 编写、修改、重构或审查代码：阅读 [code-style](.codex/skills/code-style/SKILL.md)。
- 新增或修改界面文案、翻译键、多语言资源：阅读 [translation](.codex/skills/translation/SKILL.md)。
- 设计或调整 UI、绘制控件：阅读 [ui-design](.codex/skills/ui-design/SKILL.md)。
- 独立修改完成且必要验证通过后及时创建本地提交，或修改、整理提交：阅读 [commit](.codex/skills/commit/SKILL.md)。
- 推送提交、分支或标签：阅读 [git-push](.codex/skills/git-push/SKILL.md)。
- 更新版本、整理发布日志、准备发布：阅读 [version-bump](.codex/skills/version-bump/SKILL.md)。
- 回应或回复 Git issue：阅读 [issue-reply](.codex/skills/issue-reply/SKILL.md)。
- 准备、创建、更新、描述或审查 PR：阅读 [pull-request](.codex/skills/pull-request/SKILL.md)。
- 审查隐私性或本机独特路径：阅读 [privacy-review](.codex/skills/privacy-review/SKILL.md)。
- 业务逻辑新增或变更、补充业务调试指令，以及应用调试、验证、切页、控件操作、启停和截图：阅读 [app-debug](.codex/skills/app-debug/SKILL.md)。

## 方案与记录

按复现、验收和交接需要创建方案与记录；普通修改用提交与回复说明，完整输出保存在附件中。

### 命名与阶段

- 方案保存为 `build/docs/YYYYMMDD-简体中文标题.md`，同一任务维护一份。
- 记录保存为 `build/record/YYYYMMDD-简体中文标题-序号.md`，附件放在同名目录；文件首行与文件名一致。
- 日期取任务开始日，标题用简体中文概括目标，跨天和跨会话保持相同日期与标题。
- 同一日期和标题下，序号从 1 连续递增。每个序号对应一个独立探索或实施目标，至少包含一项可复核的验收条件。
- 同一目标的重试、修正、补编和恢复继续更新原文件；切换到独立目标时才增加序号，保留前一阶段的实际完成或受阻状态。
- 每个阶段记录最多 300 行，含空行；超过时合并重复内容，将完整工具输出保存为附件，不按篇幅另开阶段。

### 内容与证据

- 顶部写任务目标、本阶段目标、状态、更新时间、分支与提交、未提交改动、下一步；没有的项写“无”。
- 状态为进行中、受阻、已完成、已终止；对应验收条件全部通过后标记已完成，终止时说明原因。
- 验收项逐项标记未执行、通过或失败，附证据路径或失败原因；截图和日志有固定输出位置时引用原位置。
- 用户约束和决定只保留当前有效内容；存在跨阶段依赖时，维护唯一覆盖清单或索引、已通过门槛和剩余项。
- 本地要求、私人环境与对话过程保存在本地记录，公开提交和 PR 聚焦实现与相关验证。
- 记录正在运行的作业及句柄、当前执行者、临时设置、测试数据、备份和恢复要求；结论变化、暂停或交接前更新。

### 恢复

1. 按开始日与中文标题定位任务，先阅读该任务最大数值序号的记录。
2. 核对记录状态、证据、当前分支、提交和工作区差异，判断后续改动是否影响已有验收。
3. 核对原作业句柄、应用或设备的真实状态，按依赖继续剩余工作；需要时回溯方案和旧阶段。

## 工程入口

| 操作 | 命令 |
|---|---|
| 准备 Debug 资源 | `python scripts/prebuild.py --dev` |
| 构建 Debug | `python scripts/build.py --dev` |
| 构建 Release | `python scripts/build.py` |
| 运行已有测试 | `python scripts/test.py --all`；按实际影响选择命名测试 |
| 格式化 C# | `dotnet format Stelliberty.slnx --no-restore --include <实际修改的文件>` |
| 格式化 Rust | `cargo fmt --all` |
| 技能同步 | `python scripts/build_support/sync_skills.py --sync` |
| 只读技能一致性检查 | `python scripts/build_support/sync_skills.py --check` |
| 自有代码文件上限检查 | `python scripts/build_support/check_code.py --check` |
| 调试命令帮助 | `python .codex/skills/app-debug/scripts/run.py --help` |

构建统一经 `scripts/build.py`，日常开发明确传入 `--dev`，缺少预编译资源时按失败诊断运行 prebuild；参数一次带全。保留现有构建入口的 Release 默认行为和工具链并发配置。

共享路径、构建、输出与技能同步位于 `scripts/build_support`；调试专用代码位于 `app-debug/scripts` 并调用共享能力。构建、prebuild 与同步不反向依赖技能执行代码。Python 入口从自身位置定位项目，在项目模块导入前关闭字节码缓存并让子进程继承 `PYTHONDONTWRITEBYTECODE=1`；本项目未配置 Python formatter。
