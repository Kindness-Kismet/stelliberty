# 连接页业务指令

| method | arg | 作用 |
|---|---|---|
| `connections.refresh` | 空 | 从核心客户端刷新连接列表。 |
| `connections.toggle` | `pause` | 切换连接监控暂停状态。 |
| `connections.close` | `all` | 关闭全部连接并清空页面列表。 |
| `connections.close` | `<connection_id>` | 关闭指定连接。 |
| `connections.filter` | `all` / `direct` / `proxy` | 切换连接过滤。 |
| `connections.get` | `detail <connection_id>` | 读取连接详情文本。 |
| `connections.state` | 空 | 读取连接页业务状态，含 `ids` 活跃连接 ID 列表，可配合 `connections.get detail` 使用。 |
