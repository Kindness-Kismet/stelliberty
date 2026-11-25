# TypeScript 代码风格

## 命名

| 标识符 | 写法 | 示例 |
|---|---|---|
| 类型、接口、类、枚举 | PascalCase | `ArchiveEntry`、`EntryReader` |
| 函数、方法、属性、参数、局部变量 | camelCase | `readEntry`、`entryCount` |
| 模块级约定常量 | 大写下划线 | `MAX_ENTRIES` |
| 布尔值、事件回调 | 谓词或事件名 | `isEmpty`、`canRead`、`onEntryRead` |

普通 `const` 绑定仍按用途命名，如 `entryCount`。回调属性描述事件，实现函数表达动作；协议字段可在边界映射到内部拼写，文件名沿用模块惯例。

- 类型谓词用 `isArchiveEntry(value): value is ArchiveEntry`；断言函数可用 `assertArchiveEntry(value): asserts value is ArchiveEntry`。
- 泛型可用 `T`，需区分时用 `TKey`、`TValue`；接口优先用含义明确的名词，沿用项目风格。
- 外部字段可用解构别名映射，如 `const { entry_count: entryCount } = payload`；序列化字符串保持协议取值。

## 导入与模块

- 顶部显式导入实际依赖，支持时用 `import type` 区分纯类型依赖；同名项使用有意义的别名。
- ESM/CommonJS、路径别名和文件后缀服从实际构建与运行配置；类型检查和运行时采用相同解析约定。
- 默认使用明确命名的导出；已有默认导出和框架约定保持一致。聚合入口只暴露稳定接口，避免循环依赖与宽泛 `export *`。
- 副作用导入仅用于实际初始化需求，保持入口与执行时机明确。

## 类型与状态

- 遵循项目生效的严格类型配置；旧工程启用 strict 按已确认范围执行，不用 `any` 批量压掉诊断。
- 外部未知输入使用 `unknown`，在真实边界收窄；内部类型已保证的条件不重复检查。
- 不用 `as any`、连续断言或非空断言绕过尚未建立的不变量；泛型与接口表达真实关系。
- 用可辨识联合表达多状态，采用穷尽分发；`type` 与 `interface` 按实际用途和既有约定选择，不重复定义同义 DTO。
- `Readonly` 和不可变更新服务于实际共享状态，避免直接修改展示状态；领域类型不依赖 DOM、Node 文件系统或 UI 框架。

## 错误与异步

- Promise 和 `async`/`await` 沿调用链传播结果与失败；启动的任务必须被等待或有明确异常观察路径。
- `catch` 只处理本层能恢复或转换的错误，捕获值按实际类型收窄；保留原因与操作上下文。
- 取消和清理由项目采用的信号或生命周期机制传递，`finally` 负责已持有资源；不默默吞掉拒绝或返回伪成功。

## 格式与验证

沿用实际 tsconfig、formatter、lint 和构建入口。React、Vue、Node 等规则仅在实际使用时加入，验证受影响的类型消费者和运行路径。
