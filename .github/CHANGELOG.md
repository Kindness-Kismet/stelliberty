- Fixed an issue with the rule page that previously hid rules and proxy groups introduced by subscription overrides, which also prevented custom rules from targeting those proxy groups
- Fixed an issue with rule editing before the rule baseline was ready, which previously allowed duplicate rules and a broken rule order to reach the core
- Removed proxy group menus from the tray; this feature is temporarily unavailable because the Avalonia memory usage issue cannot be resolved for now
- Changed the app icons in preparation for the upcoming Stelliberty mobile app

---

- 修复了关于规则页的错误，该问题曾导致订阅覆写引入的规则和代理组不显示，自定义规则也无法指向这些代理组
- 修复了关于规则基线就绪前编辑规则的错误，该问题曾导致重复规则和错乱的规则顺序进入核心
- 移除了托盘代理组菜单，由于目前无法解决 Avalonia 的内存占用问题，此功能暂时移除
- 调整了应用图标，为即将到来的 Stelliberty 移动端做好准备
