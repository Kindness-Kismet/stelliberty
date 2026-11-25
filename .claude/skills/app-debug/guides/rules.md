# 规则页业务指令

| method | arg | 作用 |
|---|---|---|
| `rules.refresh` | 空 | 刷新规则列表。 |
| `rules.filter` | `all` / `domain` / `ip` / `rule-set` / `ruleset` / `other` | 切换规则类型过滤。 |
| `rules.search` | `<keyword>` | 设置规则搜索关键字。 |
| `rules.list` | 空 | 列出当前过滤后的规则。 |
| `rules.list` | `builtin` / `custom` | 列出内置或自定义规则，含稳定 ID 与启用状态。 |
| `rules.state` | 空 | 读取规则页业务状态。 |
| `rules.add` | `custom <type> <payload> <outbound> [--no-resolve]` | 新增自定义规则；出站目标必须是当前订阅已有的内置动作或代理组。 |
| `rules.edit` | `custom <id> <type> <payload> <outbound> [--no-resolve]` | 编辑指定自定义规则。 |
| `rules.remove` | `custom <id>` | 删除指定自定义规则。 |
| `rules.toggle` | `builtin <id>` / `custom <id>` | 翻转内置或自定义规则的启用状态。 |
| `rules.move` | `up <rule_id>` | 上移指定可见规则。 |
| `rules.move` | `down <rule_id>` | 下移指定可见规则。 |
| `rules.reset-order` | 空 | 清除自定义排序并恢复默认顺序。 |

`rules.add` / `rules.edit` 的 `<type>` 为规则类型标识，例如 `DOMAIN-SUFFIX`、`IP-CIDR`、`IP-CIDR6`。仅 `IP-CIDR`、`IP-CIDR6`、`SRC-IP-CIDR` 支持 `--no-resolve`；CIDR 匹配内容必须为对应地址族的有效网段。
