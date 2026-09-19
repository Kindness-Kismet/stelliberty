- Improved memory usage in normal mode, which now releases cached pages after 60 seconds in the tray and restores their scroll positions when reopened
- Changed how Lightweight Mode releases the interface; it now waits 60 seconds after hiding, allowing quick reopening to reuse the current interface
- Added proxy group menus to the tray, so you can now switch nodes and view delay results shared with the interface
- Improved recovery after system sleep, which now restores normal and service cores and reapplies system proxy settings managed by the app
- Fixed an issue that briefly showed empty states on some pages during startup or configuration loading
- Improved application log export, which now combines interface and tray logs into one file for troubleshooting

---

- 对普通模式的内存占用进行了改善，现在隐藏到托盘 60 秒后会释放页面缓存，重新打开时恢复页面滚动位置
- 调整了轻量模式释放界面的时机，现在隐藏满 60 秒后才退出界面，期间重新打开可复用当前界面
- 新增了托盘代理组菜单，现在可以直接切换节点并查看与界面同步的测速结果
- 对休眠唤醒后的恢复进行了改善，现在会恢复普通模式和服务模式的核心，并重新应用本应用管理的系统代理设置
- 修复了启动或加载配置时部分页面短暂显示空状态的问题
- 对应用日志导出进行了改善，现在会将界面和托盘日志合并到一个文件，便于排查问题
