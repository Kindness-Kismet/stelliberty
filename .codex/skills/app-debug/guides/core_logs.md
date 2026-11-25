# 核心日志与核心状态

## 核心日志业务指令

| method | arg | 作用 |
|---|---|---|
| `core-logs.toggle` | `pause` | 切换核心日志暂停状态。 |
| `core-logs.clear` | 空 | 清空核心日志。 |
| `core-logs.filter` | `all` / `debug` / `info` / `warning` / `error` | 切换核心日志等级过滤。 |
| `core-logs.state` | 空 | 读取核心日志页业务状态。 |

## 核心状态指令

| method | arg | 作用 |
|---|---|---|
| `core.state` | 空 | 读取 hub 核心状态、PID、控制端和最近错误。 |
