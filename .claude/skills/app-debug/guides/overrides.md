# 覆写业务指令

| method | arg | 作用 |
|---|---|---|
| `overrides.add` | `remote <name> <url> [--format yaml\|javascript\|js] [--proxy direct\|system\|core]` | 导入远程覆写。 |
| `overrides.add` | `local <name> <path> [--format yaml\|javascript\|js]` | 导入本地覆写。 |
| `overrides.paste` | `url` | 添加覆写对话框已打开时，从剪贴板粘贴覆写地址。 |
| `overrides.create` | `blank <name> [--format yaml\|javascript\|js]` | 创建空白覆写。 |
| `overrides.create` | `inline <name> <content> [--format yaml\|javascript\|js]` | 创建覆写并写入内容，换行用 `\n` 输入。 |
| `overrides.select` | `<override_id>` | 选择覆写。 |
| `overrides.update` | `<override_id>` / `all` | 更新指定远程覆写或全部远程覆写。 |
| `overrides.delete` | `<override_id>` | 删除指定覆写。 |
| `overrides.move` | `up <override_id>` / `down <override_id>` | 上移或下移指定覆写。 |
| `overrides.edit` | `metadata <override_id>` | 打开覆写元数据编辑器。 |
| `overrides.edit` | `file <override_id>` | 打开覆写文件编辑器。 |
| `overrides.save` | `file <override_id> <content>` | 保存指定覆写文件内容，换行用 `\n` 输入。 |
| `overrides.open` | `external-editor <override_id>` | 用外部编辑器打开指定覆写文件。 |
| `overrides.list` | 空 | 列出覆写。 |
| `overrides.state` | 空 | 读取覆写页业务状态。 |
