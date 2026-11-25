---
name: pull-request
description: 为 Stelliberty 准备、创建、更新、描述或审查 Pull Request。用户要求提 PR、创建 PR、修改 PR 文案或审查 PR 时使用。
---

# 拉取请求

先阅读根目录 `AGENTS.md`，按实际仓库、来源分支与目标分支确定 PR 范围。创建或修改 Git 提交时阅读同级 `../commit/SKILL.md`；推送来源分支时阅读同级 `../git-push/SKILL.md`。

PR 标题和正文使用英文。按实际 `head → base` 说明合并方向与目的，项目名称、版本和模块名称从真实配置与差异中取得。

分支、版本、正文模板和验证入口按 AGENTS 的配置来源与工程入口确认。

## 执行流程

1. 确定本次操作是准备文案、审查、创建还是更新 PR；从用户指示、已有 PR 和实际远端配置核对仓库、来源仓库、`head`、`base` 及请求状态。存在多个合理目标时确认本次指向。
2. 查询选定分支的最新引用，记录比较使用的提交 SHA。已有 PR 同时阅读标题、正文、讨论、差异和检查结果；本地改动与来源分支的已提交内容分别核对。
3. 用 `git log <base>..<head>` 阅读提交范围，用 `git diff <base>...<head>` 或平台提供的实际 PR diff 核对最终差异；按完整功能归纳新增、改善和修复，回退或替代的中间方案以当前最终结果为准。
4. 按 AGENTS 核对实际架构与构建来源，代码约束和文件上限按同级 `../code-style/SKILL.md` 审查；生命周期、设置与调试排除依据相关实现和配置。已有子模块变动时，核对目标提交可从对应远端取得；验证采用与改动相关的构建、已有检查或应用调试。
5. 核对本次公开范围中的提交内容、标题、正文及附件，按下述隐私规则整理材料。按主要目的选择标题模板，并形成与实际分支差异一致的正文或审查结果。
6. 完成当前请求已授权的操作。创建 PR 时明确传入实际仓库、`--head` 与 `--base`；更新时定位已核对的 PR 编号和待修改字段。涉及实际推送时，按 git-push skill 核对本次许可、剩余次数与执行范围。

准备和审查请求交付文案或审查结果；创建、更新、发表评论和合并 PR 各按用户已明确授权的操作执行。这些 PR 操作已有的授权覆盖当前目标与内容时直接沿用；推送许可与次数由 git-push skill 核对。需要新增授权时，先准备完整可审查的结果，再说明所需操作与依据。

## 标题规范

标题采用与 commit skill 一致的 `type(scope): summary`，建议控制在 72 个字符内。`scope` 使用实际模块或子系统的小写名称；跨模块且以1个共同目标组织的 PR 可采用 `type: summary`。英文摘要使用祈使句，省略句末句号。

按整份 PR 的主要目的选1个类型。同一需求包含实现、配置、文档和验证时，以该需求的最终结果概括标题；多个独立目标按各自完整范围组织 PR。

| 主要目的 | 标题模板 | 示例 |
|---|---|---|
| 版本更新或发布准备 | `chore(release): prepare v<version>` | `chore(release): prepare v1.2.3` |
| 新增功能 | `feat(<scope>): add <capability>` | `feat(settings): add profile export` |
| 修复问题 | `fix(<scope>): <corrective action>` | `fix(startup): restore the saved window position` |
| 性能改善 | `perf(<scope>): <performance improvement>` | `perf(search): reduce repeated index scans` |
| 界面与交互调整 | `ui(<scope>): <visual or interaction improvement>` | `ui(settings): align section spacing` |
| 保持外部行为的结构调整 | `refactor(<scope>): <structural change>` | `refactor(storage): centralize settings persistence` |
| 文档更新 | `docs(<scope>): <documentation change>` | `docs(setup): clarify Android build steps` |
| 代码格式调整 | `style(<scope>): format <source area>` | `style(core): format configuration modules` |
| 测试与验证能力 | `test(<scope>): <coverage change>` | `test(settings): cover profile migration` |
| 依赖、构建或打包 | `build(<scope>): <build change>` | `build(deps): update the Compose dependencies` |
| CI 工作流 | `ci(<scope>): <automation change>` | `ci(release): build Linux ARM64 packages` |
| 其他维护事项 | `chore(<scope>): <maintenance action>` | `chore(repo): update editor settings` |
| 撤销已引入的改动 | `revert(<scope>): revert <change>` | `revert(cache): revert persistent response caching` |

- 版本类 PR 以准备指定版本为主要目标，版本值读取本次实际来源分支的应用版本；版本发布范围在正文开头写明。版本提升操作与对应提交按 version-bump skill 执行，PR 标题使用本节格式。
- 功能、修复或维护 PR 即使包含配套文档与验证，也按主要行为选型；仅版本文件中的依赖字段变化，采用实际依赖或构建类型。
- 新增能力采用 `feat`；既有能力的故障修复采用 `fix`；主要目标为界面样式或交互呈现时采用 `ui`；代码排版采用 `style`。
- 性能、结构和工具链变动按已核对的最终作用选择 `perf`、`refactor`、`build` 或 `ci`。
- 分支整合也按最终差异选择类型；以仓库维护为目的的整合可采用 `chore: integrate <head> into <base>`，分支名称填写本次实际指向。
- 更新已有 PR 时，标题与正文一起反映当前完整范围；合并采用 squash 时，最终提交标题同时核对项目 commit 规范。

## 正文与审查

- 开头说明具体问题与最终行为；需要时用简短的前后变化帮助评审判断。发布准备 PR 在版本来源明确后写出 `<base version> → <target version>`，其余 PR 按实际功能差异描述。
- 根据实际来源分支与目标分支说明此次合并的用途，涉及跨仓库来源时写明对应仓库。
- 按最终功能归并相关改动，简单 PR 使用短段落，复杂 PR 按主题列出必要条目；结构遵循项目现有 PR 模板。
- 与评审直接相关的验证方法、结果和实际限制简短呈现；完整终端输出、内部审查过程与后续工作安排保存在本地记录中。
- 版本、行为和完成程度使用已核对的依据。审查发现附仓库相对路径、行号、影响及复现或推理依据，并明确源码检查与实际执行的证据来源。
- 关联 issue 前阅读原文并对照完整交付；全部要求已满足时使用关闭关键词，部分交付使用普通引用并说明实际完成范围。

## 隐私与公开材料

- 用户的本地要求、私人环境、会话过程和内部安排保存在本地记录；公开正文聚焦与本次评审有关的项目事实。
- 文件位置采用仓库相对路径；环境说明使用通用变量或明确标注的示例值。个人目录、账号、设备标识、私人服务地址与凭据在正文、日志、截图及附件中清理或脱敏后再使用。
- 隐私核对覆盖本次来源分支将公开的提交历史与附件；逐提交检查包括后续已删除的敏感内容，二进制和压缩包按实际格式检查。
- 结合上下文区分个人路径、通用安装路径、仓库相对路径与虚构示例，核对后再判断是否需要处理。
- 发现敏感材料时，暂停受影响的对外操作，先整理脱敏结果与修正方案；涉及已公开提交的历史改写时按 commit skill 的授权与可恢复引用要求执行。检查结果使用脱敏位置和实际覆盖范围说明。

## 完成与维护

使用平台可用的接口或 CLI 执行操作。多行正文保存为 UTF-8 文件，通过 `--body-file` 等正文文件参数提交，保留真实换行与 Markdown。

创建或更新前查询同一来源与目标的已有 PR，按实际请求复用对应编号；接口结果暂时无法确认时先查询远端状态，再决定后续操作。完成后核对 PR URL、标题、正文、`head`、`base` 与实际状态，向用户报告本次结果。
