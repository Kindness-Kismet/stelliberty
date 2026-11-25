# 动画与过渡观察

现有逐帧入口在点击实际控件后，用 Avalonia 动画帧采样另一个控件的尺寸、位置、透明度和状态。

```text
python .codex/skills/app-debug/scripts/run.py control.transition --help
python .codex/skills/app-debug/scripts/run.py page.open settings/theme
python .codex/skills/app-debug/scripts/run.py control.transition Sidebar.Root Settings.NarrowSidebarSwitch 750
```

- 参数为 `observe_id click_id [duration_ms]`，默认观察 600 ms；观察控件和点击控件均使用 AutomationId。
- `result` 为实际帧数组，字段包括 `ElapsedMs`、`X`、`Y`、`Width`、`Height`、`Opacity`、`EffectiveOpacity`、`IsChecked`、`Foreground`、`Classes`、`IconKind`。
- 目标控件必须附着于可见窗口。隐藏窗口或动画帧超过等待预算时明确失败。
- 核对过渡起止值、中间帧、单调性与实际采样间隔；截图用于核对最终视觉，几何采样不能代替真实鼠标命中或屏幕帧率验证。
- 点击会改变实际设置；先记录原值，观察结束通过真实开关恢复。
- 当前倍率和 frame/settled 等待参数尚未接入，不在帮助或示例中公开；新增能力先接入真实时钟与渲染信号，再更新完整指引。
