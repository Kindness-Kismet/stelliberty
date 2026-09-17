<div align="center">

# Stelliberty 重制版

[![English](https://img.shields.io/badge/English-red?style=flat-square)](../../README.md)
&nbsp;
[![简体中文](https://img.shields.io/badge/简体中文-blue?style=flat-square)](README.zh-CN.md)

<br>

[![Avalonia](https://img.shields.io/badge/Avalonia-UI-9b4fdb?logo=avaloniaui&logoColor=white&style=flat-square)](https://avaloniaui.net)
[![.NET 11](https://img.shields.io/badge/.NET-11-512BD4?logo=dotnet&logoColor=white&style=flat-square)](https://dotnet.microsoft.com)
[![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white&style=flat-square)](#)
[![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black&style=flat-square)](#)
[![macOS](https://img.shields.io/badge/macOS-000000?logo=apple&logoColor=white&style=flat-square)](#)

</div>

<br>

| 主页 · 浅色 | 主页 · 深色 |
|---|---|
| ![主页浅色](../../.github/images/home-light.png) | ![主页深色](../../.github/images/home-dark.png) |

| 设置 · 浅色 | 设置 · 深色 |
|---|---|
| ![设置浅色](../../.github/images/settings-light.png) | ![设置深色](../../.github/images/settings-dark.png) |

Stelliberty 是跨平台桌面代理客户端，覆盖 Windows、Linux 与 macOS。

支持 Clash 标准订阅和 Base64 订阅导入，启动快、占用低。Windows 和 macOS 上还做了平台级的模糊窗口效果，界面偏简约原生风格。

<br>

---

## 导航

- [安装](#-安装)
- [快速上手](#-快速上手)
- [常见问题](#-常见问题)
- [开发指南](#-开发指南)
  - [架构](#架构)
  - [C# 规范](#c-规范)
  - [Rust 规范](#rust-规范)
  - [Avalonia / MVVM](#avalonia--mvvm)
  - [控件测试定位](#控件测试定位)
  - [编译与测试](#编译与测试)
- [PR 规范](#-pr-规范)
- [许可证](#-许可证)
- [友情链接](#-友情链接)

<br>

---

## 📦 安装

<sub>[↑ 回到导航](#导航)</sub>

### 下载

从 **[Releases 页面](https://github.com/Kindness-Kismet/stelliberty/releases/latest)** 下载对应平台的安装包：

| 平台 | 推荐 | 其他可选 |
|---|---|---|
| Windows · x64 / arm64 | `*-setup.exe`（安装版） | `*.zip`（便携版） |
| Linux · x64 / arm64 | `*.AppImage` | `*.zip` · `*.deb` · `*.rpm` · `*.pkg.tar.zst` |
| macOS · x64 / arm64 | `*.dmg` | `*.pkg` |

### 系统要求

| 平台 | 最低要求 |
|---|---|
| Windows | 10（1809+）或 11 · x64 / arm64 |
| Linux | 基于 glibc 的桌面环境，包含 fontconfig + X11 |
| macOS | 22+ · Intel / Apple Silicon |

<br>

---

## 🚀 快速上手

<sub>[↑ 回到导航](#导航)</sub>

1. 启动应用并导入订阅或配置文件。
2. 在节点页选择节点，在主页设置出站模式（规则 / 全局 / 直连）。
3. 启用系统代理或虚拟网卡模式以覆盖整机流量。

配置格式与 Clash Meta 完全兼容，详细参考 [mihomo 官方文档](https://wiki.metacubex.one/en/config/)。

<br>

---

## ❓ 常见问题

<sub>[↑ 回到导航](#导航)</sub>

### 安装后无法启动或启动无响应？

请先安装 .NET 11 运行时，然后重新启动 Stelliberty：

- 通用：[微软官方下载](https://dotnet.microsoft.com/download/dotnet/11.0)
- Arch Linux 及其衍生发行版：AUR 包 [`dotnet-core-preview-bin`](https://aur.archlinux.org/packages/dotnet-core-preview-bin)

### UWP 回环与管理员权限

Windows 上的 UWP 应用（如微软商店应用）默认禁止访问本地代理回环地址。Stelliberty 提供了 **UWP 回环豁免** 功能来解除此限制。

注意事项：

- 一般情况下不需要管理员权限，只有权限不够的时候系统才会提示你提权。
- 如果用的是虚拟网卡模式，这个限制其实不影响你——它直接接管了网卡流量，压根不走回环地址。
- 但如果你用的是系统代理模式，又想让 UWP 应用（比如微软商店里的软件）也走代理，那这个选项就得开。

### 系统代理 / 虚拟网卡需要管理员权限吗？

- **系统代理**：不需要管理员权限。
- **虚拟网卡**：创建虚拟网卡需要管理员权限。首次使用时安装服务模式可避免每次启动都弹出 UAC。
- **UWP 回环豁免**（Windows）：通常不需要管理员权限；权限不足时才需要提权。

<br>

---

## 🛠 开发指南

<sub>[↑ 回到导航](#导航)</sub>

### 前置依赖

| 工具 | 版本 | 获取方式 |
|---|---|---|
| .NET SDK | `11.0.x` | https://dotnet.microsoft.com/download/dotnet/11.0 |
| Rust | stable（rustup） | https://rustup.rs |
| Python | `3.x` | https://www.python.org/downloads/ |

### 架构

采用模块化分层 + Clean Architecture + MVVM，托盘和界面分别运行在独立宿主进程中。

```
src/Stelliberty.Tray            常驻托盘宿主、核心生命周期、后台任务
src/Stelliberty.Desktop         按需启动的 Avalonia 界面宿主、窗口、界面平台服务
src/Stelliberty.Presentation    ViewModel、UI 状态、命令绑定
src/Stelliberty.Application     用例、服务与平台能力接口
src/Stelliberty.Domain          实体、值对象、领域规则
src/Stelliberty.Infrastructure  文件系统、持久化、外部服务、进程通信传输与客户端
src/Stelliberty.Native          C# 到原生 FFI 层的包装
native/hub                      原生库：配置覆写、解析、能力模块
native/service                  服务模式
scripts/                        build.py · prebuild.py · test.py
```

依赖方向：`Desktop → Presentation → Application → Domain`，以及 `Tray → Application → Domain`。

`Infrastructure` 和 `Native` 实现 `Application` 定义的接口；`Application` 不依赖桌面、Avalonia 或 FFI 细节。

公共入口 `stelliberty.exe` 负责托盘、核心运行、系统代理、服务模式、全局快捷键与后台调度，需要窗口时通过授权会话启动 `data/deps/stelliberty_ui.exe`。其他桌面平台使用对应的无扩展名程序入口。

重复打开窗口时复用当前界面会话；关闭到托盘时退出界面进程，界面退出或崩溃后后台继续运行，完整退出由托盘统一清理。自启动、安装和提权重启均指向托盘入口，`scripts/build.py` 统一构建并打包两个宿主。

禁止：

- View 直接访问数据库、文件系统或 Rust FFI
- ViewModel 持有平台 API、文件路径、窗口生命周期细节
- Domain 依赖 Avalonia、数据库、网络、日志或配置文件
- Rust crate 感知 C#、Avalonia 或窗口生命周期

### C# 规范

**命名**

- 类型、枚举、接口、属性、方法：`PascalCase`
- 局部变量、参数、私有实例字段：`camelCase`；私有只读实例字段：`_camelCase`
- 接口只在表达抽象能力时使用 `I` 前缀
- 异步方法以 `Async` 结尾；可取消操作传递 `CancellationToken`
- Boolean 使用肯定语义：`IsEnabled`、`CanSave`、`HasSelection`

**实践**

- 能从右侧明显推断时使用 `var`，否则写显式类型
- 用结果对象表达复杂状态（`SaveResult`、`ParseResult`），不用裸 `bool`
- 系统边界使用卫语句验证；内部不可能状态不过度防御
- 不使用 `.Result`、`.Wait()` 阻塞异步任务
- 注释写意图、约束、坑点；简体中文，极致精炼，同段不超过 2 行

### Rust 规范

**命名**

- crate、module、function、variable：`snake_case`
- type、trait、enum、struct：`PascalCase`
- const、static：`SCREAMING_SNAKE_CASE`

**实践**

- 默认不可变，优先借用
- 使用 `Result<T, E>` 和 `?` 传播错误；不用 `unwrap()` 处理可恢复错误
- 默认禁止 `unsafe`；必须使用时限制最小范围并注释安全前提
- 不使用 `mod.rs`，采用 Rust 2018+ 同名文件风格
- FFI 函数命名：`hub_<能力名>_<动作>`

**能力模块结构**

```
native/hub/src/
├── lib.rs              // 仅模块声明
├── ffi.rs              // 根 FFI 聚合
├── capabilities/       // 每个能力一个文件
├── infra/              // HTTP 客户端、runtime 等基础设施
└── util/               // 纯工具函数
```

### Avalonia / MVVM

- View 只负责展示与绑定，不含业务逻辑
- ViewModel 暴露不可变或可观察状态，不直接操作控件实例
- UI 线程只处理 UI 更新；耗时任务放后台
- 平台能力（窗口、托盘、权限）放宿主层实现，经 `Application` 接口暴露

### 控件测试定位

所有可交互控件必须设置 `AutomationProperties.AutomationId`。

- 格式：`页面或区域.语义名称`，例如 `Main.SaveButton`、`Library.SearchBox`
- ID 稳定，不随显示文本、语言、布局变化
- 同一 View 内不得重复
- 禁止随机数、索引、视觉位置命名

### 编译与测试

开发流程：预构建 → 测试 → 构建。

#### 1. 预构建

下载核心二进制、GeoIP 数据与字体，构建服务模式二进制。

```bash
python scripts/prebuild.py
```

| 参数 | 作用 |
|---|---|
| *（默认）* | Release 服务二进制 |
| `--dev` | Debug 服务二进制 |
| `--all` | 同时构建 Debug 与 Release |
| `--platform <rid>` | 目标平台：`current` · `win-x64` · `win-arm64` · `linux-x64` · `linux-arm64` · `macos-x64` · `macos-arm64` |
| `--clean` | 准备前清理 `build/` 与项目 `bin/obj/` 目录 |

#### 2. 编译前测试

```bash
python scripts/test.py --all
python scripts/test.py <测试名>
```

| 参数 | 作用 |
|---|---|
| `--all` | 运行所有编译前测试 |
| `<测试名>` | 运行指定测试（见下表） |

<details>
<summary>可用测试列表</summary>

| 名称 | 说明 |
|---|---|
| `monitoring-rules` | 监控规则：连接与日志解析和归约、规则解析与分类 |
| `settings-rules` | 设置规则：TUN 权限修正、系统代理请求、更新版本选择 |
| `subscription-rules` | 订阅规则：更新计划、提供器解析、内容规范化 |
| `proxy-selection-rules` | 节点选择规则：组语义、规范化、选择、可见性 |
| `runtime-config-rules` | 运行时配置规则：设置规范化、确定性 YAML 生成 |
| `chain-proxy-rules` | 链式代理规则：分析、确定性运行时配置转换 |

</details>

#### 3. 构建

```bash
python scripts/build.py
```

| 参数 | 作用 |
|---|---|
| *（默认）* | Release 构建 |
| `--dev` | Debug 构建 |
| `--all` | 同时构建 Debug 与 Release |
| `--platform <rid>` | 目标平台（同预构建，额外支持 `desktop`） |
| `--pack <格式>` | 打包格式：`zip`（压缩包）· `installer`（安装包）· `all`（全部） |
| `--clean` | 构建前清理输出目录 |

**完整发布构建：**

```bash
python scripts/prebuild.py
python scripts/build.py --pack all
```

#### 4. 格式化

```bash
dotnet format
cargo fmt
```

<br>

---

## 📋 PR 规范

<sub>[↑ 回到导航](#导航)</sub>

提交 Pull Request 前，请确认以下清单：

### 必须项

Pull Request 必须以 `beta` 为目标分支，禁止直接向 `stable` 发起；仅仓库所有者可以将本仓库的 `beta` 分支合并到 `stable`。

| 检查项 | 说明 |
|---|---|
| 调试指令 | 界面指令维护在 `src/Stelliberty.Desktop/Debug`，托盘生命周期指令维护在 `src/Stelliberty.Tray/Debug` |
| 控件 ID | 引入新的可交互控件时，必须设置 `AutomationProperties.AutomationId` |
| 测试覆盖 | 纯业务逻辑使用编译前测试，安装包应用行为使用编译后测试 |
| 格式化 | C# 运行 `dotnet format`，Rust 运行 `cargo fmt` |

### 调试指令要求

界面指令封装在 `src/Stelliberty.Desktop/Debug`，通过仅在界面进程运行时存在的调试端口调用；托盘生命周期指令封装在 `src/Stelliberty.Tray/Debug`，通过进程通信调用。修改时同步维护本地 app-debug 技能，使用 `tray.*` 指令观察后台状态、打开界面或验证界面崩溃恢复。

本地技能还提供 `tray.copy-terminal`，用于验证界面关闭后的剪贴板操作；此指令要求核心已运行，失败时返回错误。两套本地技能均不纳入 Git 追踪。

### 控件 ID 要求

新增可点击、可输入、可选择、可断言状态的控件时：

```xml
<Button AutomationProperties.AutomationId="Settings.SaveButton" />
<TextBox AutomationProperties.AutomationId="Subscription.UrlInput" />
```

命名规则：`页面或区域.语义名称`，保持稳定、不重复、不随视觉布局变化。

<br>

---

## 📄 许可证

<sub>[↑ 回到导航](#导航)</sub>

### 开源承诺

本项目基于 WTF 协议完全开源。任何基于本项目的衍生作品，无论是直接分发还是以网络服务形式提供，均须公开完整对应源码，并继续采用 WTF 协议。

### 商业使用限制

本项目及其衍生作品均禁止用于商业用途。

### 品牌与标识

衍生作品不得保留与原版 Stelliberty 相关的任何标识，包括但不限于名称、Logo、图标、产品名、包名、应用标识符及其他品牌元素。

### 第三方组件

第三方组件仍受各自原始协议约束。完整授权条款及第三方项目清单，请参见 [WTF 协议](../../LICENSE)。

<br>

---

## 🤝 友情链接

<sub>[↑ 回到导航](#导航)</sub>

- [Telegram 通知频道](https://t.me/MaterialDesign3) —— 接收项目更新与发布通知。
