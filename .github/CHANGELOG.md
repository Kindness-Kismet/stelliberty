- Fixed an issue with core uptime that previously reset the displayed duration when the interface restarted while the core kept running
- Fixed an issue with rule conflict validation that previously blocked unrelated rule additions and edits after a subscription update introduced a conflict
- Fixed an issue with custom rule enabled states that previously re-enabled disabled rules during editing and left toggles inconsistent after rejected saves
- Fixed an issue with macOS TUN service privileges and interface naming that previously prevented TUN from starting; existing installations need to install or update service mode

---

- 修复了关于核心运行时长的错误，该问题曾导致核心仍在运行时，重启界面却让显示时长归零
- 修复了关于规则冲突校验的错误，该问题曾导致订阅更新产生旧规则冲突后，无关规则也无法添加或编辑
- 修复了关于自定义规则启用状态的错误，该问题曾导致编辑时重新启用已禁用规则，保存被拒绝后开关状态也与实际结果不一致
- 修复了关于 macOS TUN 服务权限和网卡命名的错误，该问题曾导致 TUN 无法启动；已有安装需要重新安装或更新服务模式
