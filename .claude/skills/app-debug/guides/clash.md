# Clash 设置业务指令

| method | arg | 作用 |
|---|---|---|
| `clash.list` | `keys` | 列出可测试的 Clash 设置键和预期生效方式。 |
| `clash.get` | `<key>` | 读取指定 Clash 设置值。 |
| `clash.set` | `<key> <value>` | 修改指定 Clash 设置并返回前后值、是否变化、预期生效方式和核心应用结果。 |
| `clash.state` | 空 | 读取设置变更区域、最近运行时配置应用模式和错误。 |

业务列表返回值也用 `|` 分隔行。列表输入值用 `|` 分隔，空值用 `__EMPTY__`。`clash.list keys` 的 `effect` 含义：`reload` 表示当前 runtime yaml 应走热重载，`restart` 表示应重启核心，`app-only` 表示只影响应用层设置，`next-restart` 表示保存后随下一次核心重启生效。`clash.list keys` 覆盖 Clash / 核心配置键；系统集成设置走 `settings.system-integration.*`；`tun.enable` 是供 `clash.get` / `clash.set` 读写的配置键，默认不纳入开关验证。

`tun.stack` 支持 `Mixed`、`System`、`gVisor`、`Mips`，忽略大小写并去除首尾空白；`Mips` 需要 mihomo 1.19.31 或更新版本。系统集成页通过 `Settings.TunStackComboBox` 选择协议栈，可用 `dropdown.list items` 和 `dropdown.select` 验证。
