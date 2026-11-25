# 页面指令

| method | arg | 作用 |
|---|---|---|
| `page.state` | 空 | 返回当前页名称、数据是否加载中、遮罩和内容是否显示，以及各页加载状态。 |
| `page.open` | `home` | 打开主页 |
| `page.open` | `proxies` | 打开代理页 |
| `page.open` | `connections` | 打开连接页 |
| `page.open` | `core-logs` | 打开核心日志页 |
| `page.open` | `rules` | 打开规则页 |
| `page.open` | `subscriptions` | 打开订阅页 |
| `page.open` | `overrides` | 打开覆写页 |
| `page.open` | `settings` | 打开设置首页 |
| `page.open` | `settings/root` | 打开设置首页 |
| `page.open` | `settings/theme` | 打开设置 / 主题 |
| `page.open` | `settings/language` | 打开设置 / 语言 |
| `page.open` | `settings/clash-features` | 打开设置 / Clash 特性 |
| `page.open` | `settings/network` | 打开设置 / 网络设置 |
| `page.open` | `settings/port-control` | 打开设置 / 端口控制 |
| `page.open` | `settings/system-integration` | 打开设置 / 系统集成 |
| `page.open` | `settings/dns` | 打开设置 / DNS 配置 |
| `page.open` | `settings/performance` | 打开设置 / 性能优化 |
| `page.open` | `settings/core-log` | 打开设置 / 核心日志 |
| `page.open` | `settings/app-behavior` | 打开设置 / 应用行为 |
| `page.open` | `settings/data-management` | 打开设置 / 数据管理 |
| `page.open` | `settings/update` | 打开设置 / 应用更新 |
| `page.open` | `settings/about` | 打开设置 / 关于 |
| `page.open` | `settings/app-log` | 打开设置 / 应用日志 |
| `page.scroll` | `y [y]` | 读取或设置当前页面主滚动容器 Y 偏移。 |

`page.open` 等待目标页数据就绪并进入视觉树后返回，不截图。需要观察加载期间，使用 `control.click Navigation.ProxyButton` 等导航控件指令，再查询 `page.state` 或截图。
