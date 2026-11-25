# 订阅业务指令

| method | arg | 作用 |
|---|---|---|
| `subscriptions.add` | `remote <name> <url> [--ua <ua>] [--age-key <key>] [--auto disabled\|startup\|interval] [--interval <min>] [--proxy direct\|system\|core]` | 通过订阅页导入远程订阅，下载并校验配置后选择它。 |
| `subscriptions.add` | `local <name> <path>` | 通过订阅页导入本地订阅并选择它。 |
| `subscriptions.paste` | `url` | 添加订阅对话框已打开时，从剪贴板粘贴订阅地址。 |
| `subscriptions.select` | `<subscription_id>` | 选择订阅并等待运行时配置刷新。 |
| `subscriptions.set` | `overrides <subscription_id> [override_id...]` | 设置订阅启用的覆写列表并等待运行时配置刷新；不传覆写 ID 表示清空。 |
| `subscriptions.update` | `<subscription_id>` / `all` | 更新指定远程订阅或全部远程订阅，并等待当前订阅运行时配置刷新。 |
| `subscriptions.list` | 空 | 列出订阅。 |
| `subscriptions.state` | 空 | 读取订阅页业务状态。 |
| `subscriptions.state` | `store` | 读取订阅持久化列表数量、本地/远程数量和 ID。 |
| `subscriptions.state` | `selection` | 读取持久化当前订阅 ID，并校验它是否仍存在于订阅列表。 |
| `subscriptions.delete` | `<subscription_id>` | 删除指定订阅并刷新当前选择。 |
| `subscriptions.move` | `up <subscription_id>` / `down <subscription_id>` | 上移或下移指定订阅。 |
| `subscriptions.copy` | `link <subscription_id>` | 复制远程订阅链接并返回复制状态。 |
| `subscriptions.show` | `qr <subscription_id>` | 打开远程订阅的二维码弹窗。 |
| `subscriptions.chain-proxy.open` | `<subscription_id>` | 打开订阅的链式代理管理弹窗；读取内容状态用 `subscriptions.chain-proxy.state`。 |
| `subscriptions.chain-proxy.start` | `add` | 进入新增自定义链草稿，默认选择首个所属代理组。 |
| `subscriptions.chain-proxy.set` | `name <name>` | 设置当前草稿的链名称。 |
| `subscriptions.chain-proxy.set` | `group <group_name>` | 设置当前草稿的所属代理组。 |
| `subscriptions.chain-proxy.toggle` | `hop <proxy\|group> <name>` | 切换当前草稿跳点；代理组固定在第一跳且最多一个。 |
| `subscriptions.chain-proxy.save` | `draft` | 校验并保存当前草稿；循环引用由最终运行时配置加载后统一检测。 |
| `subscriptions.chain-proxy.save` | 空 | 保存弹窗内全部链式代理改动并触发运行时刷新。 |
| `subscriptions.chain-proxy.remove` | `custom <custom_id>` | 从弹窗内删除指定自定义链，需再执行 `subscriptions.chain-proxy.save` 持久化。 |
| `subscriptions.chain-proxy.edit` | `custom <custom_id>` | 进入指定自定义链草稿，可修改名称、所属代理组和跳点。 |
| `subscriptions.chain-proxy.state` | `draft` | 读取草稿名称、所属组、可选组、候选跳点、跳点顺序、保存可用状态和校验状态。 |
| `subscriptions.override-selector.move` | `up <override_id>` / `down <override_id>` | 上移或下移覆写选择器中的指定覆写。 |
| `subscriptions.chain-proxy.move` | `up <hop_name>` / `down <hop_name>` | 上移或下移草稿中的指定代理跳点；代理组首跳不会移动。 |
| `subscriptions.chain-proxy.state` | 空 | 读取内置链和自定义链启用状态，以及自定义链编号、名称、所属组和带类型链路。内容异步加载，打开弹窗后经本命令读取。 |
| `subscriptions.chain-proxy.toggle` | `builtin <name>` | 切换指定内置链的启用状态并返回弹窗状态。 |
| `subscriptions.chain-proxy.toggle` | `custom <custom_id>` | 切换指定自定义链的启用状态；最终运行时检测到循环时仅警告。 |
| `subscriptions.open` | `external-editor <subscription_id>` | 用外部编辑器打开指定订阅文件。 |
| `subscriptions.get` | `runtime-config <subscription_id>` | 读取指定订阅运行时配置，多行内容用 `\n` 转义。 |
| `subscriptions.save` | `file <subscription_id> <content>` | 保存指定订阅文件内容，换行用 `\n` 输入。 |
| `subscriptions.edit` | `metadata <subscription_id> [--name <name>] [--url <url>] [--ua <ua>] [--age-key <key>] [--delay <min>] [--auto disabled\|startup\|interval] [--interval <min>] [--proxy direct\|system\|core]` | 编辑订阅元数据并走确认链路。 |
| `subscriptions.provider.list` | `<subscription_id>` | 列出指定订阅的 Provider；当前订阅读取托盘核对后的运行状态，其他订阅显示缓存且禁用同步、上传。流量与到期信息通过 control.get text 读取，标识为 Subscriptions.ProviderSelector.<proxy 或 rule>.<名称>.TrafficText 和 ExpireText。 |
| `subscriptions.provider.rows` | 空 | 读取当前 Provider 对话框条目状态（含 syncing 转圈标记），不触发重载，可抓同步中间态。 |
| `subscriptions.provider.sync` | `<subscription_id> <provider_name>` / `all <subscription_id>` | 经托盘核对当前运行订阅后同步一个或全部 Provider，并刷新弹窗与卡片合计；失败走错误 toast。 |
| `subscriptions.provider.upload` | `<subscription_id> <provider_name> <path>` | 上传 File Provider 内容。 |
| `subscriptions.trigger` | `auto-delay` | 立刻对当前订阅全节点跑批量测延迟，绕过定时器与到点判断；订阅未配自动测延迟间隔时直接返回。回传测过且当前可见的节点名。 |
| `subscriptions.set` | `update-delay <milliseconds>` | 设置 Debug 远程订阅下载延迟，用于验证更新过程中的暂停与刷新状态。 |
| `subscriptions.edit` | `file <subscription_id>` | 打开指定订阅文件编辑器。 |
