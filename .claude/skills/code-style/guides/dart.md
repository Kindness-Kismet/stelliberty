# Dart 代码风格

## 命名

- 库、目录与文件使用 lower_case_with_underscores；类、枚举、mixin、扩展和类型别名使用 UpperCamelCase。
- 函数、变量、参数、字段、常量及枚举值用 lowerCamelCase；布尔值可用 `isEmpty`、`hasEntries`。
- 前导下划线表示库级私有，按实际库边界使用；外部 JSON 字段、路由和生成接口保持协议拼写。

## 导入与库

- 导入位于文件顶部，通常按 `dart:`、`package:` 和项目相对导入分组，组内排序遵循 analyzer 与 formatter。
- 同名符号使用 `as` 前缀，需要缩小暴露面时使用 `show` 或 `hide`；项目包导入与相对导入沿用已生效规则。
- `export` 只暴露稳定接口，`part`/`part of` 用于实际共享库或生成器要求，不把普通模块拆分都做成 part。
- 条件导入与导出采用相同接口契约，平台 API 留在实际适配边界。

## 类型与状态

- 优先 `final`，确实能在编译期确定时使用 `const`；需要重新赋值才用可变绑定。
- 使用 sound null safety 表达真实缺项；`!` 和 `late` 必须有已成立的生命周期或初始化前提，不用于压掉诊断。
- 公共接口和边界使用明确类型，内部在清楚时采用推断；`dynamic` 留给真实动态协议，未知数据在边界收窄。
- 枚举、sealed 类型和模式匹配按实际语言版本表达状态；避免多个独立布尔值组成无效状态。

## 错误、资源与异步

- 失败使用项目定义的异常或结果契约，`catch` 只处理能恢复或转换的错误；重新传播当前异常使用 `rethrow` 保留堆栈。
- Future 被等待或有明确的错误处理路径；`unawaited` 只表明有意不等待，不代替异常处理与生命周期管理。
- StreamSubscription、控制器和句柄由所有者取消或关闭；耗时计算按实际需要放入 isolate，避免阻塞事件循环。
- Flutter 项目中的状态、订阅和异步回调跟随组件生命周期，跨异步间隙使用上下文前核对有效性；纯 Dart 模型不依赖 Flutter。

## 格式与验证

使用 dart format、项目实际 analysis_options 与构建入口；框架规则只在对应 Dart/Flutter 工程中应用。
