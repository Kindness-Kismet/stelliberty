# 截图

使用实际已实现的应用合成 PNG 入口。编码在应用内执行，客户端返回本次新文件的实际路径。

```text
python .codex/skills/app-debug/scripts/run.py screenshot.take --help
python .codex/skills/app-debug/scripts/run.py app.ensure --no-focus + screenshot.take
```

- `screenshot.take` 保存到 `build/screenshots`，JSON `result` 包含 `path` 和 `format: png`。
- 页面导航与截图串行执行，先读取页面状态确认目标内容已就绪；动作受理不等同于所有异步业务已完成。
- 系统材质和桌面整体的观察按实际捕获覆盖判断，应用合成图与系统屏幕截图分别记录。
- 当前命令沿用 PNG，不公开 WebP、质量切换或连续截图参数；如需新增能力，应用端、注册表、客户端与指南成组接入。
- 本地附件保持在 build 内，公开前清理订阅地址、账号、节点名及其他任务无关私人信息。
