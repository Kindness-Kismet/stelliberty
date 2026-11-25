# TypeScript 键名与资源

新项目采用路径式语义键时，用点号分组，各段使用 `lowerCamelCase`，例如 `settings.saveChanges`。区分同一操作的不同用途，如 `files.delete.action`、`files.delete.inProgress`、`files.delete.succeeded`。只为实际出现的文案建键。

| 资源库 | “保存更改”的示例 | 参数与数量 |
|---|---|---|
| i18next | 已用 `settings` 命名空间时，使用 `t('saveChanges', { ns: 'settings' })` | 默认插值写 `{{name}}`。数量用 `count`，资源形式按项目配置及目标语言选取。 |
| Vue I18n | 嵌套对象使用 `t('settings.saveChanges')` | 命名插值写 `{name}`。数量使用项目配置的复数消息和调用方式。 |
| FormatJS | `formatMessage({ id: 'settings.saveChanges', defaultMessage: 'Save changes' })` | ID 按字面值匹配。消息使用 ICU 参数和复数分支，例如 `{count, plural, one {# file} other {# files}}`。 |

保持同一功能域只出现一次。分组结构、命名空间、字面 ID 和占位符分别服从项目实际资源库；已有类型化键和提取流程继续使用可识别的消息标识。
