---
name: translation
description: 新增、翻译或审查应用界面文案、翻译键和多语言资源时使用。覆盖操作、进度、结果、状态和面向用户的失败提示。
---

# 界面翻译

先从项目 `AGENTS.md` 的配置来源找到多语言资源，查看关联控件、状态和已有译文。按项目使用的资源库查键名，按文案用途查译法。

## 按任务阅读指南

| 任务 | 指南 |
|---|---|
| 选择语态、精简提示、处理动态内容 | [字符串写法](guides/wording.md) |
| 查简体中文和英文的常用操作与状态 | [中英文词典](guides/glossary.md) |
| GPUI / Rust | [GPUI 键名与资源](guides/frameworks/gpui.md) |
| Kotlin / Android 或 Compose Multiplatform | [Kotlin 键名与资源](guides/frameworks/kotlin.md) |
| Qt Widgets / QML | [Qt 文案标识与资源](guides/frameworks/qt.md) |
| Avalonia / C# | [Avalonia 键名与资源](guides/frameworks/avalonia.md) |
| TypeScript | [TypeScript 键名与资源](guides/frameworks/typescript.md) |

## 执行流程

1. 查看文案出现的位置和触发条件，区分按钮动作、进行中、已结束、当前状态与失败原因。在同一功能的资源和调用处查找已有表达。
2. 同一含义、用途和参数优先复用已有资源。显式键用稳定的英文语义命名，沿用项目的命名空间和大小写。英文拼写相同但含义不同的词分开建项，如打开文件的 `Open` 与工单状态的 `Open`。
3. 用词典的对应条目写译文。项目有已确定的产品术语时，把需要跨页面复用的词义记在项目技能的词典中。其他语言沿用该语言的产品术语与句法。
4. 通过项目现有资源入口核对本次改动的语言、参数和数量变化。检查相同状态在不同位置的用词，以及异步操作的提示与实际结果是否对应。

## 完成与维护

术语有变动时一并核对同义调用处。
