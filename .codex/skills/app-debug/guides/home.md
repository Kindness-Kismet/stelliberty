# 首页业务指令

| method | arg | 作用 |
|---|---|---|
| `home.state` | 空 | 读取首页核心、系统代理、TUN、出站模式、流量与网络状态。 |
| `home.refresh` | `runtime` / `network` | 刷新首页运行态指标或网络状态。 |
| `home.set` | `outbound <rule\|global\|direct>` | 设置出站模式。 |
| `home.select` | `takeover <proxy\|tun>` | 切换首页接管页签。 |
| `home.copy` | `terminal-proxy <powershell\|cmd\|bash>` | 复制指定终端的代理命令。 |
| `home.toggle` | `system-proxy` | 切换系统代理。 |
| `home.set` | `tun <boolean>` | 设置 TUN 开关。 |
| `home.reset` | `traffic` | 清零首页本地流量基线。 |
| `home.restart` | `core` | 触发核心重启。 |
| `home.update` | `core` | 触发核心更新检查。 |

# 服务模式指令

| method | arg | 作用 |
|---|---|---|
| `service.state` | 空 | 刷新并读取服务模式安装、运行与修复状态。 |
| `service.install` | 空 | 调用安装或更新入口，按当前状态安装或更新服务模式。 |
| `service.update` | 空 | `service.install` 的别名，调用同一安装或更新入口。 |
| `service.repair` | 空 | 当前系统服务绑定其他应用目录时，卸载旧服务注册，不自动安装当前目录服务模式。 |
| `service.uninstall` | 空 | 卸载服务模式。 |

## 服务文件日志隔离验证

修改服务日志后先完成调试构建，再运行技能脚本：

```bash
python .codex/skills/app-debug/scripts/verify_service_logging.py --record-dir build/record/20261105-服务日志-1
```

`--record-dir` 指向当前任务在本仓库 `build/record/` 下的附件目录；脚本在其 `isolated-service/<时间戳>/` 内复制当前调试包的服务二进制，每次使用新目录保留已有证据。

脚本先检查调试服务端点为空，再以隐藏子进程运行 `foreground`。每次发送命令前核对端点属于自己启动的进程，使用同一二进制的 `logs` 和 `shutdown` 验证运行时日志、正常退出后回看、离线 `logs 0` 返回 `no logs`、重启追加，以及两轮 4 MiB 轮转和单备份替换。填充日志只发生在隔离服务退出后。

默认验证不安装或卸载系统服务，也不启动代理核心；端点已被占用则停止。异常时仅通过持有的子进程句柄完成清理。输出 `verification.json` 路径和退出码，命令输出、当前日志及轮转备份保留在同一隔离目录。

Windows 可加 `--with-core`，仅在首轮启动调试包中的真实核心，检查停止开始、退出确认及十六进制退出码：

```bash
python .codex/skills/app-debug/scripts/verify_service_logging.py --record-dir build/record/20261105-服务日志-1 --with-core
```

此模式使用本次隔离目录内的 `data/service/` 和 `data/core/`，分别存放服务、锁文件、核心及配置。服务的孤儿核心清理只扫描最近 `data` 祖先下的 `service/`，启动前该锁目录必须为空。配置关闭全部代理监听端口、DNS、TUN 和 Geo 自动更新，使用直连模式及唯一命名管道，不修改系统代理。

脚本核对服务返回的核心进程路径和控制器进程编号，持有该核心的进程句柄，确认服务退出后核心已经结束；异常时仅通过已核对路径的核心句柄清理。真实核心模式只在 Windows 验证，基础文件日志模式仍支持其他桌面平台。
