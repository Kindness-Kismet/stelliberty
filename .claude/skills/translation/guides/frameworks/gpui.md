# GPUI 键名与资源

GPUI 项目按实际采用的 Rust 本地化库查找文本。使用支持点号分组的 JSON 字典时，新键按功能分组，组内用 `snake_case`，例如 `settings.save_changes`。查看加载器区分嵌套对象与含点号的字面键的方式，按同一结构维护各语言。

- 显式键区分意义不同的消息，例如删除文件的动作、进度和结果。新增条目时先查看同组已有键。
- 使用 Fluent 时沿用 FTL 的消息 ID，例如 `settings-save-changes`。变量用 `{ $name }`，数量变化用选择表达式。JSON 项目的变量与复数按实际加载器处理。
- 采用普通 JSON 字典的新项目可使用 `assets/locales/en.json` 和 `assets/locales/zh-CN.json`。使用其他资源库时，以该库实际加载的源文件为准。
