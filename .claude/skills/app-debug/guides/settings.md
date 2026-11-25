# 应用设置业务指令

| method | arg | 作用 |
|---|---|---|
| `settings.state` | 空 | 读取语言、主题、应用行为、更新和系统集成设置状态。 |
| `settings.language.set` | `sys` / `zh` / `zh-hant` / `en` | 切显示语言。`sys` 跟随系统 UI 文化。 |
| `settings.language.state` | 空 | 读取当前语言设置。 |
| `settings.theme.set` | `sys` / `system` / `light` / `dark` | 设置应用主题。 |
| `settings.theme.state` | 空 | 读取主题、强调色和窗口效果状态。 |
| `settings.accent.set` | `mode <sys\|system\|custom>` | 设置强调色模式。 |
| `settings.accent.set` | `color <hex_color>` | 设置自定义强调色并切到自定义模式。 |
| `settings.window-effect.set` | `none` / `mica` / `acrylic` / `blur` | 设置窗口背景效果。 |
| `settings.app-behavior.list` | `keys` | 列出可设置的应用行为键。 |
| `settings.app-behavior.set` | `<key> <value>` | 设置应用行为项；可用键通过 `settings.app-behavior.list keys` 查询。 |
| `settings.app-behavior.state` | 空 | 读取应用行为设置状态。 |
| `settings.update.set` | `auto-check <boolean>` | 设置应用自动检查更新。 |
| `settings.update.set` | `interval <startup\|1day\|7days\|14days>` | 设置应用更新检查间隔。 |
| `settings.update.set` | `channel <stable\|beta>` | 设置应用更新通道。 |
| `settings.update.check` | 空 | 手动检查应用更新。 |
| `settings.update.ignore` | `latest` | 忽略最近发现的应用版本。 |
| `settings.update.state` | 空 | 读取应用更新设置状态。 |
| `settings.data-management.backup` | `<path>` | 创建数据备份，必须显式传入备份文件路径。 |
| `settings.data-management.restore` | `<path> <overwrite\|merge>` | 按指定模式还原指定备份。 |
| `settings.data-management.state` | 空 | 读取数据管理状态（含还原对话框可见性与所选模式）。 |
| `settings.data-management.webdav.list` | `keys` | 列出可设置的 WebDAV 备份键。 |
| `settings.data-management.webdav.set` | `<key> <value>` | 设置 WebDAV 备份项，如启用、地址、账号、密码、远端目录、间隔和保留份数。 |
| `settings.data-management.webdav.test` | 空 | 测试当前 WebDAV 备份连接。 |
| `settings.data-management.webdav.backup` | 空 | 创建当前数据备份并上传到 WebDAV。 |
| `settings.data-management.webdav.open` | `dialog` | 打开 WebDAV 备份管理对话框并刷新备份列表。 |
| `settings.data-management.webdav.list` | 空 | 列出当前 WebDAV 备份文件。 |
| `settings.data-management.webdav.restore` | `backup <fileName>` | 还原指定 WebDAV 备份文件。 |
| `settings.data-management.webdav.delete` | `backup <fileName>` | 删除指定 WebDAV 备份文件。 |
| `settings.data-management.webdav.restore` | `<overwrite\|merge>` | 从 WebDAV 最新备份按指定模式还原。 |
| `settings.data-management.webdav.state` | 空 | 读取 WebDAV 备份状态。 |
| `settings.app-log.refresh` | 空 | 从运行日志文件重新读取应用日志。 |
| `settings.app-log.export` | `<path>` | 导出当前运行日志文件到指定路径。 |
| `settings.app-log.state` | 空 | 读取应用日志页状态（总量、过滤数量、警告、错误、加载中和状态）。 |
| `settings.system-integration.list` | `keys` | 列出可设置的系统集成键。 |
| `settings.system-integration.set` | `<key> <value>` | 设置系统集成项，如代理主机、绕过列表、PAC 模式和 PAC 脚本。 |
| `settings.system-integration.refresh` | `proxy-host` | 刷新系统代理主机候选列表。 |
| `settings.system-integration.uwp.list` | 空 | 打开 UWP 对话框并列出全量条目（含勾选态、AC 名、SID）。 |
| `settings.system-integration.uwp.search` | `<keyword>` | 按关键字过滤 UWP 条目并返回过滤结果。 |
| `settings.system-integration.uwp.select` | `all` | 全选当前可见 UWP 条目。 |
| `settings.system-integration.uwp.invert` | 空 | 反选当前可见 UWP 条目。 |
| `settings.system-integration.uwp.set` | `<package_family_name> <boolean>` | 按包名设置 UWP 勾选态，不写系统。 |
| `settings.system-integration.uwp.save` | 空 | 批量保存 UWP 回环勾选到系统。 |
| `settings.system-integration.uwp.cancel` | 空 | 关闭 UWP 对话框，不保存改动。 |
| `settings.system-integration.state` | 空 | 读取系统集成设置状态。 |
