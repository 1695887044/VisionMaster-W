# C# 脚本编辑器 L2 完整体验档：语法高亮 + 智能补全 + 编辑器行为上收

- 日期：2026-09-19
- 范围：`Shard/Core.Controls`、`Plugins/Plugin.ImageScript`、`Plugins/Plugin.CSharpScript`
- 目标：C# 脚本编辑器达到与 Halcon 脚本编辑器（ImageScript）对齐的 L2 完整体验——语法高亮、成员补全、参数提示、悬浮说明、错误行红底、全套编辑器行为（撤销/缩进/括号配对/注释切换/缩放/查找替换）、右键代码片段菜单。

## 一、需求与决策

### 需求背景
ImageScript 插件的脚本编辑器此前已具备完整的编辑体验（高亮/补全/参数提示/Tab 步进/错误行标红等），但这些能力全部内嵌在插件目录，C# 脚本插件（Plugin.CSharpScript）无法复用。本轮将公共部分上收到程序集，并为 C# 脚本补齐语言侧的差异化实现。

### 关键决策

| # | 决策 | 理由 |
|---|------|------|
| D1 | `ScriptEditorBehavior` 上收到 `Core.Controls`，命名空间 `Core.Editing`，注释前缀参数化 `Attach(TextEditor, string commentPrefix = "//")` | C# 用 `"//"`、Halcon 用 `"*"`，参数化后消除语言耦合；与 RelayCommand 上收到 `Core.Commands` 同范式 |
| D2 | C# 高亮不用 AvalonEdit 内置 "C#" 定义，自建动态 xshd（`CSharpHighlighting`） | 与 ImageScript 视觉语言统一：API 棕黄 `#795E26` = 算子棕黄；并可把 `Context.*` 12 个 API 与已知类型（HImage/HTuple…）纳入词法着色 |
| D3 | 补全模板用 `\u0001…\u0001` 包裹 Tab 步进参数区间 | 复用 ImageScript 思路但数据内嵌（不依赖外部模板文件）；`ApiCompletionData.Complete` 拆段重建后注册 `SetTabStops` |
| D4 | `Context.` 触发独占成员补全（contextOnly 模式） | 输入 `Context.` 时只弹 API 成员，不混入关键字/变量，降低噪音 |
| D5 | 错误行直接用 Roslyn 诊断行号，不做"非空行计数"换算 | Halcon 编译器报错行号会跳过空注释行故需换算；Roslyn 行号即编辑器真实行号 |
| D6 | `CSharpApiDoc` 三处描述以 `ScriptContext.cs` 源码为准修正 | 文档初稿凭印象编写，读源码后发现：`Fail()` 不抛异常（置 Failed 标记，宿主转业务失败）；`GetVar/SetVar` 是运行期变量池（本次运行内共享，非跨运行持久） |
| D7 | 变量表增删/改名后动态重建高亮与补全源 | `RefreshLists()` 尾部统一触发 `UpdateEditorIntel()`；带变量版本 xshd 只作对象不注册全局，`_varsKey` 缓存避免重复构建 |

### 语言侧差异（C# vs Halcon）
- 注释前缀 `"//"` vs `"*"`（D1 已参数化）。
- C# 大小写敏感：xshd `<RuleSet>` 不带 `ignoreCase`。
- Roslyn 默认 imports（System / System.Collections.Generic / System.Linq / System.Text / System.Math / HalconDotNet），片段无需 `using` 即可编译。
- 片段签名逐一验证：`region.AreaCenter(out double row, out double col)` 返回面积、`img.Intensity(domain, out HTuple dev)` 返回均值，均为仓库内真实用法。

## 二、修改文件清单

### 新建
| 文件 | 说明 |
|------|------|
| `Shard/Core.Controls/Editing/ScriptEditorBehavior.cs` | 上收版编辑器行为集（~420 行）：撤销/自动缩进/括号配对/注释切换/缩放/当前行+错误行高亮/查找替换。`Attach` 闭包捕获局部 `renderer` 变量，修复原静态 `_renderer` 字段多实例串扰隐患；Nullable=enable 适配（`Window?`/`LineBackgroundRenderer?`/`IEnumerable<int>?`） |
| `Plugins/Plugin.CSharpScript/CSharpHighlighting.cs` | 动态 xshd C# 高亮（~180 行）：运行时拼 XML → `HighlightingLoader.Load`；首次注册全局（"CSharpScript"/".csx"），带变量版本只作对象；色板 9 类 |
| `Plugins/Plugin.CSharpScript/CSharpApiDoc.cs` | 13 个 Context API 数据层（Name/Signature/Summary/Insert）+ CommonKeywords + `Find`/`Match` 查询 |
| `Plugins/Plugin.CSharpScript/CSharpCompletionData.cs` | 三类补全项：`ApiCompletionData`（Priority 5，`\u0001` 模板拆段 + Tab 步进注册）、`KeywordCompletionData`（1）、`VarCompletionData`（10） |
| `Plugins/Plugin.CSharpScript/CSharpIntelliSense.cs` | 补全引擎（~440 行）：TextEntered（`(` 参数提示 / `.` Context 成员 / 词首补全）、PreviewKeyDown（Ctrl+Space / Tab 步进 / Esc）、`Document.Changed`+`OffsetChangeMap` 偏移跟踪（多段改动保守作废）、400ms DispatcherTimer 自建悬浮卡 |
| `Plugins/Plugin.CSharpScript/CSharpTemplates.cs` | 8 个代码片段 × 3 分类（输入输出/流程控制/Halcon 图像），签名全部经源码验证 |

### 重写
| 文件 | 说明 |
|------|------|
| `Plugins/Plugin.CSharpScript/CSharpScriptView.xaml.cs` | OnLoaded 四件套接线（高亮 / CSharpIntelliSense.Attach / ScriptEditorBehavior.Attach(Editor, "//") / 右键菜单）；`CollectVarNames`/`CollectVarInfos` 动态数据源；`Validate_Click` 全错误行标红 + 跳首行；右键菜单（校验/注释/取消注释/插入片段按分类分组）；`InsertTemplate` 光标行下方插入、保留撤销栈 |

### 修改
| 文件 | 变更 |
|------|------|
| `Plugins/Plugin.ImageScript/ImageScriptView.xaml.cs` | 3 处：`using Core.Editing;`、`ScriptEditorBehavior.Attach(Editor, "*")`、两处 `SetComment(…, "*")`——行为保持不变 |
| `Shard/Core.Controls/Core.Controls.csproj` | 新增 `AvalonEdit 6.3.1.120` 包引用（首次写入遇 EBUSY，重试成功） |
| `Plugins/Plugin.CSharpScript/CSharpCompletionData.cs` | 编译期补 `using ICSharpCode.AvalonEdit.CodeCompletion;`（修复 CS0246） |

### 删除
| 文件 | 说明 |
|------|------|
| `Plugins/Plugin.ImageScript/ScriptEditorBehavior.cs` | 本地版已被 `Core.Editing` 上收版替代 |

## 三、验证结果

按依赖顺序构建，全部通过（dotnet build，.NET 9）：

| 项目 | 结果 | 备注 |
|------|------|------|
| `Shard/Core.Controls` | 0 错误 / 6 警告 | 警告均为存量（ImageDisplayEvent/LinkableValueEditor 等 Nullable 警告） |
| `Plugins/Plugin.ImageScript` | 0 错误 / 188 警告 | 警告均为 UI.csproj 存量；验证上收后引用与 `"*"` 前缀调用点；首次构建因 `$(SolutionDir)` 未定义报 MSB3073（PostBuild copy），带 `-p:SolutionDir` 后通过 |
| `Plugins/Plugin.CSharpScript` | 0 错误 / 0 警告 | 验证 6 个新文件 + View 重写；首构建报 3×CS0246（ICompletionData 缺 using），补引用后通过 |

构建命令（单独构建插件需手动传 SolutionDir）：

```
dotnet build Shard/Core.Controls/Core.Controls.csproj
dotnet build Plugins/Plugin.ImageScript/Plugin.ImageScript.csproj -p:SolutionDir="e:\VM\VisionMaster-W-master\"
dotnet build Plugins/Plugin.CSharpScript/Plugin.CSharpScript.csproj -p:SolutionDir="e:\VM\VisionMaster-W-master\"
```

## 四、已知边界

1. **`$(SolutionDir)` 无回退**（遗留）：`Plugin.ImageScript.csproj` / `Plugin.CSharpScript.csproj` / `Plugin.CreateRoi.csproj` 的 PostBuild 用 `$(SolutionDir)`，直接构建 csproj 不带 `-p:SolutionDir`（尾部反斜杠必须）会报 MSB3073。长期方案待定。
2. **高亮为词法级**：xshd 着色不做 Roslyn 语义分析，字符串内的标识符样文本不参与 Context API 着色；语义级需接 Roslyn Workspace，当前档位不做。
3. **Tab 步进多段保守作废**：单段改动按 delta 移位跟踪占位符；一次 undo/redo 或多段改动会作废当前步进状态（与 ImageScript 行为一致）。
4. **悬浮提示自建**：AvalonEdit 6.3 移除了 ToolTipManager，采用 400ms DispatcherTimer + `ToolTip(StaysOpen=true)` 手动管理；鼠标快速划过时不弹出。
5. **错误行标红为静态快照**：仅在校验按钮触发时刷新，编辑对应行不会即时清除红底（下次校验覆盖）。
6. **变量高亮同步时机**：变量增删/改名经 `RefreshLists()` 生效；直接在属性面板外改动变量名（如方案加载）依赖 View 重建时的 `OnLoaded` 初始化。
