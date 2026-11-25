# 代理页业务指令

代理组或节点名称包含空格、引号时，作为一个完整参数传入；界面端统一解析转义，保持名称原样。

| method | arg | 作用 |
|---|---|---|
| `proxies.list` | `groups` | 列出代理组、类型、当前节点和是否允许手动切换。 |
| `proxies.list` | `nodes [group]` | 列出当前组或指定组的代理条目、延迟、测试状态和是否可点击。 |
| `proxies.select` | `group <group>` | 选择代理组。 |
| `proxies.select` | `node <node>` | 切换当前代理组节点；自动组会由业务层拒绝。 |
| `proxies.get` | `delay <node>` | 读取当前可见代理条目的延迟状态，`state` 为 `untested` / `testing` / `tested` / `failed`。 |
| `proxies.test` | `node <node>` | 触发单个代理节点延迟测试并立即返回。 |
| `proxies.test` | `group <group>` | 触发指定代理组内所有代理条目延迟测试并立即返回。 |
| `proxies.test` | `current-group` | 触发当前代理组延迟测试并立即返回。 |
| `proxies.test` | `all` | 触发全部代理组的代理条目延迟测试并立即返回。 |
| `proxies.refresh` | 空 | 刷新代理页数据；刷新前会取消正在进行的延迟测试。 |
| `proxies.cancel` | `delay` | 取消正在进行的代理延迟测试。 |
| `proxies.state` | 空 | 读取代理页业务状态，含 `layout`（horizontal/vertical）与 `expanded`（当前展开组）。 |
| `proxies.set` | `layout <horizontal\|vertical>` | 设置代理页布局：横向网格 / 竖向列表。 |
| `proxies.toggle` | `layout` | 切换代理页横向 / 竖向布局。 |
| `proxies.expand` | `group <group>` | 竖向布局下展开指定代理组（幂等，不折叠已展开项）。 |
| `proxies.collapse` | `group` | 折叠当前展开的代理组。 |
