<div align="center">

# Stelliberty Remake

[![English](https://img.shields.io/badge/English-red?style=flat-square)](README.md)
&nbsp;
[![简体中文](https://img.shields.io/badge/简体中文-blue?style=flat-square)](.github/docs/README.zh-CN.md)

<br>

[![Avalonia](https://img.shields.io/badge/Avalonia-UI-9b4fdb?logo=avaloniaui&logoColor=white&style=flat-square)](https://avaloniaui.net)
[![.NET 11](https://img.shields.io/badge/.NET-11-512BD4?logo=dotnet&logoColor=white&style=flat-square)](https://dotnet.microsoft.com)
[![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white&style=flat-square)](#)
[![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black&style=flat-square)](#)
[![macOS](https://img.shields.io/badge/macOS-000000?logo=apple&logoColor=white&style=flat-square)](#)

</div>

<br>

| Home · Light | Home · Dark |
|---|---|
| ![Home light](.github/images/home-light.png) | ![Home dark](.github/images/home-dark.png) |

| Settings · Light | Settings · Dark |
|---|---|
| ![Settings light](.github/images/settings-light.png) | ![Settings dark](.github/images/settings-dark.png) |

Stelliberty is a cross-platform desktop proxy client for Windows, Linux, and macOS.

Import Clash-standard and Base64 subscriptions. It starts quickly and uses little resources. On Windows and macOS, platform-level frosted window effects are available, with a simple native look.

<br>

---

## Navigation

- [Installation](#-installation)
- [Quick Start](#-quick-start)
- [FAQ](#-faq)
- [Development Guide](#-development-guide)
  - [Architecture](#architecture)
  - [C# Conventions](#c-conventions)
  - [Rust Conventions](#rust-conventions)
  - [Avalonia / MVVM](#avalonia--mvvm)
  - [Control Test IDs](#control-test-ids)
  - [Build & Test](#build--test)
- [PR Guidelines](#-pr-guidelines)
- [License](#-license)
- [Friends](#-friends)

<br>

---

## 📦 Installation

<sub>[↑ Back to Navigation](#navigation)</sub>

### Download

Grab the package for your platform from the **[Releases page](https://github.com/Kindness-Kismet/stelliberty/releases/latest)**:

| Platform | Recommended | Alternative |
|---|---|---|
| Windows · x64 / arm64 | `*-setup.exe` (installer) | `*.zip` (portable) |
| Linux · x64 / arm64 | `*.AppImage` | `*.zip` · `*.deb` · `*.rpm` · `*.pkg.tar.zst` |
| macOS · x64 / arm64 | `*.dmg` | `*.pkg` |

### System Requirements

| Platform | Minimum |
|---|---|
| Windows | 10 (1809+) or 11 · x64 / arm64 |
| Linux | glibc desktop with fontconfig + X11 |
| macOS | 22+ · Intel / Apple Silicon |

<br>

---

## 🚀 Quick Start

<sub>[↑ Back to Navigation](#navigation)</sub>

1. Launch the app and import your subscription or config file.
2. Select nodes on the Nodes page; set an outbound mode on the Home page (Rule / Global / Direct).
3. Enable system proxy or virtual network mode for full-device traffic coverage.

Config format is fully compatible with Clash Meta — see the [mihomo documentation](https://wiki.metacubex.one/en/config/) for details.

<br>

---

## ❓ FAQ

<sub>[↑ Back to Navigation](#navigation)</sub>

### App will not start or appears unresponsive after installation?

Install the .NET 11 Runtime, then start Stelliberty again:

- General: [Microsoft download](https://dotnet.microsoft.com/download/dotnet/11.0)
- Arch Linux and derivatives: AUR package [`dotnet-core-preview-bin`](https://aur.archlinux.org/packages/dotnet-core-preview-bin)

### UWP Loopback & Administrator Privileges

UWP apps on Windows (e.g. Microsoft Store apps) are blocked from accessing the local proxy loopback address by default. Stelliberty provides a **UWP loopback exemption** toggle to remove this restriction.

Notes:

- Administrator privileges are usually not required; the system only prompts for elevation when permission is insufficient.
- Virtual network mode is unaffected — it takes over traffic at the adapter level and never goes through loopback.
- If you use system proxy mode and want UWP apps (such as Microsoft Store apps) to go through the proxy, turn this option on.

### Do system proxy / virtual network require admin?

- **System proxy**: No administrator privileges required.
- **Virtual network**: Creating a virtual network adapter requires administrator privileges. Install service mode on first use to avoid repeated UAC prompts.
- **UWP loopback exemption** (Windows): usually no administrator privileges required; elevate only if permission is insufficient.

<br>

---

## 🛠 Development Guide

<sub>[↑ Back to Navigation](#navigation)</sub>

### Prerequisites

| Tool | Version | Get it |
|---|---|---|
| .NET SDK | `11.0.x` | https://dotnet.microsoft.com/download/dotnet/11.0 |
| Rust | stable (rustup) | https://rustup.rs |
| Python | `3.x` | https://www.python.org/downloads/ |

### Architecture

Modular layers + Clean Architecture + MVVM, with separate tray and UI host processes.

```
src/Stelliberty.Tray            Persistent tray host, core lifecycle, background tasks
src/Stelliberty.Desktop         On-demand Avalonia UI host, windows, UI platform services
src/Stelliberty.Presentation    ViewModels, UI state, command bindings
src/Stelliberty.Application     Use cases, service & capability interfaces
src/Stelliberty.Domain          Entities, value objects, domain rules
src/Stelliberty.Infrastructure  File system, persistence, external services, IPC transport and clients
src/Stelliberty.Native          C# wrappers over the native FFI layer
native/hub                      Native library: config override, parsing, capabilities
native/service                  Service mode
scripts/                        build.py · prebuild.py · test.py
```

Dependency directions: `Desktop → Presentation → Application → Domain` and `Tray → Application → Domain`.

`Infrastructure` and `Native` implement interfaces defined by `Application`; `Application` has no dependency on desktop, Avalonia, or FFI details.

The public entry point, `stelliberty.exe`, owns the tray, core runtime, system proxy, service mode, global shortcuts, and background scheduling. It launches `data/deps/stelliberty_ui.exe` through an authenticated session when a window is needed. Other desktop platforms use the corresponding executable names without `.exe`.

Opening the window again reuses the active UI session. By default, hiding to the tray keeps the UI process alive. Lightweight Mode releases the UI process when hidden and creates a new session when reopened; the background runtime continues in both modes. Full application exit is coordinated by the tray. Startup integration, installers, and elevated restarts all use the tray entry point, and `scripts/build.py` packages both hosts together.

Prohibited:

- Views accessing databases, file system, or Rust FFI directly
- ViewModels holding platform APIs, file paths, or window lifecycle details
- Domain depending on Avalonia, databases, networking, logging, or config files
- Rust crates being aware of C#, Avalonia, or window lifecycle

### C# Conventions

**Naming**

- Types, enums, interfaces, properties, methods: `PascalCase`
- Local variables, parameters, private instance fields: `camelCase`; private readonly instance fields: `_camelCase`
- Interfaces use `I` prefix only when expressing an abstract capability
- Async methods end with `Async`; cancellable operations take `CancellationToken`
- Booleans use affirmative semantics: `IsEnabled`, `CanSave`, `HasSelection`

**Practices**

- Use `var` when the type is obvious from the right side; explicit types otherwise
- Use result objects for complex state (`SaveResult`, `ParseResult`), not bare `bool`
- Guard clauses at system boundaries; no excessive defensiveness for impossible internal states
- Never use `.Result` or `.Wait()` to block async tasks
- Comments state intent, constraints, and pitfalls; concise, max 2 lines per block

### Rust Conventions

**Naming**

- crate, module, function, variable: `snake_case`
- type, trait, enum, struct: `PascalCase`
- const, static: `SCREAMING_SNAKE_CASE`

**Practices**

- Immutable by default, prefer borrowing
- Use `Result<T, E>` and `?` for error propagation; no `unwrap()` for recoverable errors
- `unsafe` is prohibited by default; when necessary, minimize scope and comment the safety precondition
- No `mod.rs` — use Rust 2018+ same-name file style
- FFI function naming: `hub_<capability>_<action>`

**Capability module structure**

```
native/hub/src/
├── lib.rs              // module declarations only
├── ffi.rs              // root FFI aggregation
├── capabilities/       // one file per capability
├── infra/              // HTTP client, runtime, etc.
└── util/               // pure utility functions
```

### Avalonia / MVVM

- Views handle display and binding only — no business logic
- ViewModels expose immutable or observable state; never manipulate control instances directly
- UI thread handles UI updates only; heavy work goes to background threads
- Platform capabilities (windows, tray, permissions) are implemented in the host layer and exposed via `Application` interfaces

### Control Test IDs

All interactive controls must have `AutomationProperties.AutomationId` set.

- Format: `PageOrArea.SemanticName`, e.g. `Main.SaveButton`, `Library.SearchBox`
- IDs are stable — they don't change with display text, language, or layout
- No duplicates within the same View
- No random numbers, indices, or visual-position naming

### Build & Test

Development workflow: **prebuild → test → build**.

#### 1. Prebuild

Downloads the core binary, GeoIP data, and fonts; builds the service-mode binary.

```bash
python scripts/prebuild.py
```

| Flag | Effect |
|---|---|
| *(default)* | Release service binary |
| `--dev` | Debug service binary |
| `--all` | Both Debug and Release |
| `--platform <rid>` | `current` · `win-x64` · `win-arm64` · `linux-x64` · `linux-arm64` · `macos-x64` · `macos-arm64` |
| `--clean` | Clean `build/` and `bin/obj/` before fetching |

#### 2. Pre-build Tests

```bash
python scripts/test.py --all
```

| Flag | Effect |
|---|---|
| `<name>` | Run a specific test by name |
| `--all` | Run every pre-build test |

| Name | Description |
|---|---|
| `monitoring-rules` | Connection and log parsing and reduction; rule parsing and classification |
| `settings-rules` | TUN permission correction, system proxy requests, update release selection |
| `subscription-rules` | Update planning, provider parsing, content normalization |
| `proxy-selection-rules` | Group semantics, normalization, selection, visibility |
| `runtime-config-rules` | Settings normalization and deterministic YAML generation |
| `chain-proxy-rules` | Analysis and deterministic runtime config transformation |

#### 3. Build

```bash
python scripts/build.py
```

| Flag | Effect |
|---|---|
| *(default)* | Release build |
| `--dev` | Debug build |
| `--all` | Both Debug and Release |
| `--platform <rid>` | Same as prebuild, plus `desktop` (win-x64 + linux-x64 + macos-arm64) |
| `--pack <format>` | `zip` · `installer` · `all` |
| `--clean` | Clean target output directory before building |

**Full release build:**

```bash
python scripts/prebuild.py
python scripts/build.py --pack all
```

#### 4. Formatting

```bash
dotnet format
cargo fmt
```

<br>

---

## 📋 PR Guidelines

<sub>[↑ Back to Navigation](#navigation)</sub>

Before submitting a Pull Request, confirm the following:

### Requirements

Pull Requests must target `beta`. Direct Pull Requests to `stable` are prohibited; only the repository owner may promote the repository's `beta` branch to `stable`.

| Check | Description |
|---|---|
| Debug commands | Maintain UI commands under `src/Stelliberty.Desktop/Debug` and tray lifecycle commands under `src/Stelliberty.Tray/Debug` |
| Control IDs | New interactive controls must have `AutomationProperties.AutomationId` set |
| Test coverage | Pure business logic uses `Pre-build Tests`; packaged app behavior uses `Post-build Tests` |
| Formatting | C#: `dotnet format`, Rust: `cargo fmt` |

### Debug Command Requirements

UI commands are wrapped under `src/Stelliberty.Desktop/Debug` and invoked through the debug control port, which exists only while the UI process is running. Tray lifecycle commands live under `src/Stelliberty.Tray/Debug` and use IPC. Keep the local app-debug skill in sync with the affected host; use its `tray.*` commands to inspect background state, open the UI, or verify UI crash recovery.

The local skill also provides `tray.copy-terminal` to verify clipboard access while the UI is closed. It requires a running core and reports failures to the caller. Both local skill copies remain outside Git tracking.

### Control ID Requirements

When adding clickable, input-capable, selectable, or state-assertable controls:

```xml
<Button AutomationProperties.AutomationId="Settings.SaveButton" />
<TextBox AutomationProperties.AutomationId="Subscription.UrlInput" />
```

Naming rule: `PageOrArea.SemanticName` — stable, unique, independent of visual layout.

<br>

---

## 📄 License

<sub>[↑ Back to Navigation](#navigation)</sub>

### Open Source Commitment

This project is fully open source under the WTF License. Any derivative work based on this project must publish its complete corresponding source code and remain under the WTF License, whether it is distributed directly or provided as a network service.

### Commercial Use Restrictions

This project and its derivative works must not be used for commercial purposes.

### Branding and Identifiers

Derivative works must not retain any identifier associated with the original Stelliberty software, including but not limited to its name, logos, icons, product names, package names, application identifiers, and other branding.

### Third-Party Components

Third-party components remain subject to their original licenses. See the [WTF License](LICENSE) for the complete terms and third-party project list.

<br>

---

## 🤝 Friends

<sub>[↑ Back to Navigation](#navigation)</sub>

- [Telegram Updates](https://t.me/MaterialDesign3) — Project updates and release notices.
