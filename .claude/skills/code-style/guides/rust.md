# Rust 代码风格

## 命名

| 标识符 | 写法 | 示例 |
|---|---|---|
| crate 的代码标识、模块、函数、变量、字段 | snake_case | `archive_io`、`read_entry`、`entry_count` |
| 类型、trait、枚举变体、类型参数 | UpperCamelCase | `ArchiveEntry`、`EntryReader`、`ReadOnly`、`T` |
| const、static | SCREAMING_SNAKE_CASE | `MAX_ENTRIES` |
| 布尔查询 | 用谓词描述含义 | `is_empty`、`has_entries` |

普通访问器常直接使用字段名，如 `len()`。转换方法按实际行为选前缀，廉价借用视图用 `as_bytes`，消耗自身并移交所有权用 `into_bytes`，构造新表示可用 `to_vec`。类型名中的缩写按单词处理，如 `HttpClient`。

- 常规构造函数可用 `new`，按输入来源命名的构造可用 `from_bytes`。允许失败的标准转换优先采用 `TryFrom::try_from`，按契约返回 `Result`。
- 借用迭代器通常叫 `iter`，可变借用为 `iter_mut`，移交容器所有权的迭代按 `IntoIterator` 契约实现。
- 短生命周期参数沿用 `'a`，需要区分来源时可用 `'input`。Cargo 包名可含连字符，代码引用 crate 时通常用下划线，如 `archive-io` 对应 `archive_io`。

## 导入与可见性

- 所需项在所属模块顶部通过 `use` 导入，实现中使用导入后的名称；同名项通过有意义的 `as` 别名区分。trait 消歧或关联类型表达确需限定路径时使用相应语法。
- 新代码按标准库、外部 crate、当前 crate 分组，组间空行；组内排序与换行交给项目实际 rustfmt。已有明确导入配置时沿用，不为分组启用 nightly。
- 跨模块依赖优先使用 `crate::`，紧邻父模块内部接口可用 `super::`；避免多层 `super::super::` 隐藏依赖位置。
- 默认显式列出导入项；框架约定的 prelude 可用通配导入，核对实际需要的 trait 与命名冲突。普通业务模块不使用 `use ...::*`。
- 条件导入的 `#[cfg(...)]` 与使用方条件一致；仅为 trait 方法解析引入而不需要名称时可用 `use Trait as _`。
- 接口采用最小必要可见性，如私有、`pub(super)` 或 `pub(crate)`。模块入口的 `pub use` 只重导出需要稳定暴露的接口，不批量透出内部实现。

## 模块组织

- 职责较小的模块使用 `<module>.rs`；有子模块时采用同名 `<module>.rs` 加 `<module>/`，由入口声明子模块。已有 `mod.rs` 结构沿用原项目约定。
- `main.rs` 保持入口声明与调用，组装和实现进入对应职责模块；库的 `lib.rs` 维护模块声明与实际公共接口。
- 按模型、用例、外部适配、展示等实际职责拆分，不为每层强建 crate。模块可见性和真实导入共同落实项目的依赖边界。

## 类型、所有权与错误

- 默认不可变，优先借用；只有真实所有权或生命周期需要时才 `clone`，不以克隆掩盖不清晰的数据归属。
- 借用参数按契约选择 `&str`、切片或其他借用类型；需要修改容量、保留所有权或特定容器能力时再接受具体拥有型容器。
- 用枚举和明确的数据结构表达互斥状态，采用穷尽 `match`；缺项使用 `Option`，可恢复失败使用 `Result` 与 `?` 并保留上下文。
- 不用 `unwrap`、`expect` 或 `panic!` 代替可恢复错误处理；集合优先通过迭代器访问，外部索引通过 `get` 等实际取值接口处理。

## unsafe 与并发

- `unsafe` 只用于必要的 ABI、指针或平台边界，范围最小，注释说明真实安全前提；unsafe 函数内的操作仍按实际 edition/lint 使用显式 unsafe 块。
- FFI 明确分配与释放、布局、编码和生命周期，防止 unwind 越过不支持展开的 ABI 边界。
- 锁与借用持有范围最小，不在锁内执行无关阻塞 IO 或长任务；跨 `await` 持锁需要明确契约，采用运行时对应的同步原语。
- 异步任务有明确的持有者、取消和结束路径；只有实际异步需求时才引入运行时或服务。

## 格式与验证

使用项目已有的 rustfmt、Clippy 和构建入口。Cargo workspace、lint 级别、profile、工具链、build.rs 与生成目录按项目工程配置维护，代码风格不另存一份构建来源。

## Stelliberty 模块与命名例外

- 新模块采用同名 `.rs` 加目录，不使用 `mod.rs`。
- FFI 命名为 `hub_<能力>_<动作>`，生命周期 `hub_bootstrap`、`hub_shutdown` 豁免动作后缀。
- 测试允许使用 expect 说明失败；可恢复错误仍使用 Result。
