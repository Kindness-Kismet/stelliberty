- Fixed an issue with connection classification that previously counted connections routed to DIRECT through proxy groups as proxied, and showed the wrong proxy group and node in connection details
- Changed the connection chain order, which now starts from the matched policy and ends at the actual outbound, consistent with the core logs
- Fixed an issue with chain proxies that previously left a proxy group empty and broke the configuration when all of its members were filtered out; empty groups now fall back to COMPATIBLE by default
- Moved macOS and Linux user data to the current user's application data directory, so the app installation no longer needs to be writable by all users and service mode can start the core normally in installed packages

## Upgrading on macOS and Linux [v2.0.36]

This release changes where user data is stored on macOS and Linux:

- macOS: `~/Library/Application Support/stelliberty/data`
- Linux: `~/.local/share/stelliberty/data`

Automatically migrating the old location would mean carrying compatibility code through every future release, which would make the app increasingly hard to maintain and more prone to issues. After weighing this, we decided not to migrate automatically this time. We apologize for the inconvenience and ask you to reinstall once as follows:

1. Uninstall service mode in the old version.
2. Back up your data in Settings > Data Management, and save the backup file outside the app's installation location, such as your desktop.
3. Exit the app completely and uninstall the old application package.
4. Install the current version, then restore the backup file in Settings > Data Management.

Once everything works as expected, you can delete the old data directory: `Stelliberty.data` next to the app on macOS, `/opt/stelliberty.data` on Linux (along with `/opt/stelliberty` if it remains after uninstalling), or `stelliberty.data` next to the AppImage.

Windows is not affected and requires no action.

---

- 修复了关于连接分类的错误，该问题曾导致经代理组走 DIRECT 的连接被算作代理连接，连接详情中的代理组和节点也显示不正确
- 调整了连接链路的显示顺序，现在从命中的策略开始、到实际出站结束，与核心日志保持一致
- 修复了关于链式代理的错误，该问题曾导致代理组成员被全部过滤后变成空组，使配置失效；现在空组默认会回退到 COMPATIBLE
- 调整了 macOS 和 Linux 的用户数据位置，现在保存在当前用户的应用数据目录中，应用安装目录不再需要对所有用户开放写入权限，安装包版本的服务模式也能正常启动核心

## macOS 与 Linux 升级说明 [v2.0.36]

本次更新调整了 macOS 和 Linux 上用户数据的保存位置：

- macOS：`~/Library/Application Support/stelliberty/data`
- Linux：`~/.local/share/stelliberty/data`

如果为旧位置做自动迁移，后续每个版本都得一直带着这套兼容逻辑，维护会越来越吃力，也更容易出问题。权衡之后，这次没有做自动迁移，给大家添麻烦了，还请按以下步骤重新安装一次：

1. 在旧版本中卸载服务模式。
2. 在“设置 > 数据管理”中备份数据，并把备份文件保存到应用安装位置以外的地方，例如桌面。
3. 完全退出应用，卸载旧的应用包。
4. 安装当前版本，再到“设置 > 数据管理”中还原刚才的备份文件。

确认一切正常后，可以删除旧的数据目录：macOS 上是应用旁边的 `Stelliberty.data`，Linux 上是 `/opt/stelliberty.data`（卸载后如果还残留 `/opt/stelliberty`，也可以一并删除），AppImage 则是它旁边的 `stelliberty.data`。

Windows 不受影响，无需任何操作。
