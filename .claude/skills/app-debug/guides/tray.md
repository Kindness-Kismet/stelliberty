# 托盘与退出

窗口和托盘共用桌面宿主。隐藏窗口保留核心、后台任务和调试端口；隐藏满 5 秒后释放旧合成关系并保留页面，重新显示取消尚未执行的释放。

```text
python .codex/skills/app-debug/scripts/run.py app.status
python .codex/skills/app-debug/scripts/run.py app.ensure --no-focus
python .codex/skills/app-debug/scripts/run.py app.stop
```

- `window.close` 经过窗口原有策略，是否隐藏由实际应用行为设置决定；通过 `window.state` 核对结果。
- `app.ensure` 复用宿主并显示界面，按当前实例焦点策略操作。
- `app.stop` 发出完整退出请求，等待已匹配进程结束；异常时只清理精确路径和已确认 PID。
- 普通核心由宿主协调退出；已安装系统服务具有独立生命周期，不将关闭窗口等同于卸载服务。
- 用户手动托盘交互与正常重复启动保留原应用行为，专项验证按用户指定范围执行。
