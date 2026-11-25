# Avalonia 键名与资源

| 资源系统 | “保存更改”的键 | 资源来源 |
|---|---|---|
| 强类型 .NET 资源 | `SettingsSaveChanges` | 中性 `.resx` 与对应文化的文件，例如 `Strings.resx` 和 `Strings.zh-CN.resx` |
| AXAML 资源字典 | 如 `x:Key="SettingsSaveChanges"` | 各语言字典采用相同的资源键，按项目已有的字典装载与绑定方式访问 |

- 新的强类型资源名用 `PascalCase`，按功能和用途组合，保证生成的 C# 属性能直接引用。已有资源字典延续项目的大小写和分组。
- 使用 .NET 复合格式化时保留 `{0}`、`{1}` 等位置和格式说明，各语言可调整顺序。数量变化由项目采用的复数机制处理，普通 `.resx` 字符串按资源名查找。

## Stelliberty 键名约定

- 本项目沿用 JSON 字典与现有点分键名；语言和目录以 AGENTS 的资源来源为准，不套用 `.resx` 属性命名。
- 改动后核对三语言键集合、格式占位符和 JSON 解析；实际语言切换按 app-debug 的 settings 指南执行，结束恢复原语言。
