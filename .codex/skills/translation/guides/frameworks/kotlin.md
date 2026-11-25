# Kotlin 键名与资源

| 资源系统 | “保存更改”的调用 | 资源来源 |
|---|---|---|
| Android 原生资源 | `R.string.settings_save_changes` | 应用模块 `res/values/strings.xml` 的默认英文与 `res/values-zh-rCN/strings.xml` 的简体中文 |
| Compose Multiplatform Resources | `Res.string.settings_save_changes` | 实际源集的 `composeResources/values/strings.xml` 与 `composeResources/values-zh-rCN/strings.xml` |

- 采用英文小写下划线，先写功能再写用途。各语言使用同一资源名。Android 的 Compose 与 View 可引用同一份 Android 资源。
- 参数按位置和类型对应，如 `%1$s` 和 `%2$d`。数量决定词形时使用资源系统的 `plurals`，若文案显示数量，同时向格式化调用传入数量值。
- Kotlin 桌面项目若使用 JVM 资源包，延续已有键与文件布局，按项目使用的格式化 API 填写参数。
