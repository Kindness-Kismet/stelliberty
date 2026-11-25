# C# 代码风格

## 命名

| 标识符 | 写法 | 示例 |
|---|---|---|
| 类型、方法、属性、枚举成员 | PascalCase | `ArchiveEntry`、`OpenArchive`、`EntryCount` |
| 接口、泛型参数 | 接口前缀 I，泛型参数用 T 或带 T 的说明性名称 | `IEntryReader`、`TEntry` |
| 参数、局部变量、私有实例字段 | camelCase，私有实例字段用下划线前缀 | `entryCount`、`_entryCount` |
| 常量、作为常量使用的静态只读成员 | PascalCase | `MaxEntries`、`DefaultTimeout` |

布尔属性使用 `IsEmpty`、`HasEntries`、`CanRead`；返回任务的异步方法采用 `Async` 后缀。重写和接口实现保持契约名称。

- 尝试型 API 可用 `TryReadEntry(out Entry entry)`；读取已存值的属性用名词，执行操作使用方法。
- 事件以 `EntryClosing`、`EntryClosed` 区分阶段，触发方法可用 `OnEntryClosed`；组合标志枚举通常用复数。
- 缩写按单词组合，如 `HttpClient`、`userId`；字段前缀以有效 `.editorconfig` 为准。

## using 与文件组织

- `using` 放在项目约定的文件或命名空间顶部，分组排序遵循 `.editorconfig`；同名类型使用明确别名。
- `global using` 只用于项目普遍使用的稳定依赖，不把某个功能的依赖扩散到全部文件；静态导入保持成员来源清晰。
- 命名空间、文件范围声明和类型文件布局沿用实际语言版本及项目约定；平台依赖集中在对应边界。

## 类型与控制流

- 右侧能明显推断类型时使用 `var`，否则显式声明；复杂结果用类型或枚举表达，不用裸 `bool` 隐藏多种状态。
- nullable 表达真实缺项；通过控制流或类型建立不变量，不用 null-forgiving 运算符掩盖缺失值。
- 模式匹配与 `switch` 处理明确状态；公开接口和序列化模型按真实契约保持稳定。

## 错误、资源与异步

- `catch` 只位于能处理失败的边界；上下文和取消继续传播，重新抛出当前异常使用 `throw;` 保留堆栈。
- `IDisposable` 和 `IAsyncDisposable` 按所有权使用 `using` 或 `await using`；资源不早于消费者结束时释放。
- 可取消异步操作沿调用链传递 `CancellationToken`，不使用 `.Result`、`.Wait()` 阻塞异步任务。
- 除框架事件处理器外避免 `async void`；启动任务要有等待、取消和异常观察路径。
- UI 调度、平台 API、文件和 FFI 位于实际边界，领域模型不依赖它们。`ConfigureAwait` 根据宿主上下文决定，不机械统一。

## 格式与验证

采用项目的 `.editorconfig`、nullable、分析器与实际构建入口；按修改范围运行格式化或检查。

## Stelliberty 命名约定

- 私有实例字段使用 `_camelCase`；常量性质的私有 `static readonly` 使用 PascalCase。
- 接口仅在表达抽象能力时使用 `I` 前缀。
