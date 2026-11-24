# 窗口

先查询 `run.py window.state --help`，完整参数来自注册表。主窗口与托盘处于同一宿主，状态操作作用于已核对的 Debug 实例。

| 命令 | 参数 | 结果与含义 |
|---|---|---|
| `window.state` | 无 | 实际位置、尺寸、显示、最小化与焦点状态 |
| `window.show` / `window.close` | 可选 `--focus-mode` | 显示或按现有关闭策略关闭；关闭结果可能为隐藏到托盘 |
| `window.move` | `x y` | 通过现有平台与窗口入口移动 |
| `window.resize` | `width height` | 调整尺寸，保留窗口最小尺寸约束 |
| `window.minimize` / `window.maximize` / `window.restore` | 可选 `--focus-mode` | 执行对应状态变化，返回实际状态 |
| `window.appearance.state` | 无 | 实际透明、背景和原生合成状态 |
| `window.appearance.probe` / `window.appearance.stop` | 按帮助 | 观察合成更新，结束返回日志摘要 |

```text
python .codex/skills/app-debug/scripts/run.py app.start --no-focus
python .codex/skills/app-debug/scripts/run.py window.state
```

`no-focus` 保持系统焦点与后台层级；`focus` 模式的显示等动作按实际激活副作用取得明确授权。窗口专项操作按用户指定范围执行。

原生鼠标关闭按钮验证使用 `window.verify-hover`，会移动鼠标和发送点击，须有覆盖该动作的明确同意；它与普通控件动作、截图分别记录证据。
