# Kotlin 代码风格

## 命名

| 标识符 | 写法 | 示例 |
|---|---|---|
| 类、接口、对象、类型别名 | PascalCase | `ArchiveEntry`、`EntryReader` |
| 函数、属性、参数、局部变量 | camelCase | `readEntry`、`entryCount` |
| 编译期常量 | 大写下划线 | `const val MAX_ENTRIES` |
| 包名 | 小写，按层级分段 | `org.example.archive` |

布尔属性使用 `isEmpty`、`hasEntries`；普通只读属性仍用 camelCase。挂起函数按操作命名，回调参数按事件命名，如 `onEntryRead`。框架回调和 Java 互操作成员沿用真实 API。

- 公开只读属性与私有可变存储可配对为 `entries`、`_entries`；普通私有属性沿用项目命名。
- 公共参数支持命名调用，重命名要检查调用方；属性和 `isReady` 等布尔值还要核对 Java getter 契约。
- 可失败转换按行为命名，如 `toEntryOrNull`；运算符使用语言规定的函数名。

## 导入与模块

- 使用显式导入，不以通配符掩盖依赖；同名声明用 `as` 别名。排序、多行声明及尾随逗号服从项目 formatter。
- 文件与包按真实职责组织，顶层声明避免形成无边界的工具集合；`private`、`internal` 和公开接口按实际模块消费者选择。
- 扩展函数保持接收者语义清楚，不通过扩展隐藏跨层 IO 或全局可变状态。

## 类型与控制流

- 优先 `val`，可变状态由实际持有方管理；短小函数可使用表达式体。
- 可空类型按实际语义声明，通过安全调用、`let` 或提前返回处理缺项；不滥用 `!!` 或默认值掩盖失败。
- 使用 sealed 类型或枚举表达互斥状态，`when` 覆盖明确分支，不用无意义 `else` 隐藏遗漏。
- 资源按真实所有权使用 `use` 等作用域管理；失败在可处理的边界转换或传播。

## 协程与界面

- 协程归属实际生命周期，不使用 `GlobalScope`；沿调用链传播取消，捕获一般异常时保留 `CancellationException`。
- IO 和长任务离开 UI 线程，采用项目实际 Dispatcher；结构化并发中等待子任务并观察失败。
- UI 状态与事件单向流动，模型不混入平台 IO；Compose 中返回 Unit 的组件函数按框架约定使用 PascalCase。
- Compose、Flow、依赖注入与数据存储规则仅在实际采用时生效，不为风格统一引入整套框架。

## 格式与验证

沿用实际 Kotlin 版本、formatter、lint 与 Gradle 工程入口，核对 Kotlin/Java 边界及受影响模块。
