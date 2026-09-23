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

## 五、后续修复（同日）：xshd 加载抛 HighlightingDefinitionInvalidException

### 现象
打开 C# 脚本编辑器即崩（`CSharpScriptView.OnLoaded` → `CSharpHighlighting.EnsureRegistered` 第 103 行）：

```
HighlightingDefinitionInvalidException: Error at position (line 1, column 621):
The element 'Span' has invalid child element 'Rule'. List of possible elements expected: 'Begin, End, RuleSet'.
```

### 根因（两处，均为 xshd 语法错误）

1. **`Span` 下不能直接放 `Rule`**。AvalonEdit 的 `ModeV2.xsd`（第 82-96 行）规定 `Span` 的子元素序列只能是 `Begin` → `End` → `RuleSet`（各 0~1 次）；`Rule` / `Keywords` / `Import` 只能出现在 `RuleSet` 下（第 128-139 行）。原写法把转义规则 `<Rule>\\.</Rule>` 直接塞进 `<Span>`，被 XSD 校验拒绝。
2. **块注释 `end="*/"` 是非法正则**。`*` 作量词却前面没有可限定的内容，AvalonEdit 编译正则时抛 `Invalid pattern '*/' at offset 1. Quantifier '*' following nothing.`。必须写成 `/\*` 与 `\*/`。

两处都是**只在运行时加载才暴露**的错误，编译期（dotnet build 0 错误 0 警告）毫无提示。

### 修法（对齐 AvalonEdit 内置 `CSharp-Mode.xshd` 的既有写法）

| 位置 | 修改前 | 修改后 |
|------|--------|--------|
| 普通字符串转义 | `<Span begin="&quot;" end="&quot;"><Rule>\\.</Rule></Span>` | `<Span …><RuleSet><Span color="String" begin="\\" end="." /></RuleSet></Span>` |
| 逐字字符串 `""` | `<Span begin="@&quot;" …><Rule>&quot;&quot;</Rule></Span>` | `<Span …><RuleSet><Span color="String" begin="&quot;&quot;" end="" /></RuleSet></Span>` |
| 字符转义 | 同普通字符串 | 同普通字符串 |
| 块注释 | `begin="/*" end="*/"` | `begin="/\*" end="\*/"` |

- 转义序列改用**嵌套 Span**（而非 `Rule`）：用子 Span 吃掉 `\"`，使外层 `end` 不提前触发——这是 AvalonEdit 内置 C# 定义的原始做法，`<Span begin="\\" end="."/>`。
- 逐字字符串的 `""` 用 `begin="&quot;&quot;" end=""`（end 为空正则 = 匹配后立即结束），同样照搬内置写法。

### 验证方式（本轮新增，值得保留）

这类错误静态审阅极易漏（第一版正是漏了），故搭了一次性校验工程**真实调用** `HighlightingLoader.Load`：

1. 反射调用 `CSharpHighlighting.BuildXshd`（覆盖"无接口变量 / 带接口变量"两条分支）拿到真实 xshd 文本；
2. 走与生产代码完全一致的 `HighlightingLoader.Load(reader, HighlightingManager.Instance)`；
3. 校验工程放在系统临时目录、用 `AssemblyResolve` 指向插件 bin 解析依赖，**不污染仓库**，用完即删。

结果：

```
[no-vars]   PASS  name=CSharpScript  rules=7  spans=5
[with-vars] PASS  name=CSharpScript  rules=8  spans=5
        span color=String  begin="  end=($|") inner(span=1,rule=0)
        span color=String  begin=@" end="       inner(span=1,rule=0)
        span color=String  begin='  end=($|') inner(span=1,rule=0)
        span color=Comment begin=// end=$      inner(span=0,rule=0)
        span color=Comment begin=/\* end=\*/   inner(span=0,rule=0)
ALL PASS
```

两条分支均通过，5 个 Span 全部解析成功，块注释 `/\*` … `\*/` 正确。

### 教训

- xshd 是"运行时才校验"的领域语言，改完**必须真实加载一次**，不能只靠肉眼对照 XML。
- 对照 AvalonEdit 自带 `Highlighting/Resources/*.xshd` 是最省事的正确性来源；本轮两处修法均直接取自内置 `CSharp-Mode.xshd`。
- `Shard/Core.Controls` 上收的 `ScriptEditorBehavior` 等其它组件不涉及 xshd，本次异常与它们无关。

## 六、后续修复（同日）：脚本编辑器"只有行号、内容空白、无法编辑"

### 现象
上一节修完 xshd 加载异常后，编辑器可打开，但正文区**只显示行号 1、内容空白、无法输入**。打开后应用日志被同一条告警刷屏：

```
2026-09-19 22:07:30.895 [WARN] [UI] A highlighting rule matched 0 characters, which would cause an endless loop.
Change the highlighting definition so that the rule matches at least one character.
Regex: (?m)^[ \t]*#[ \t]*[^\n]*
```

单次运行累计 **9414 条**（约占全日志 65%）；`[ERROR]|Exception|未处理|Unhandled` 零命中——异常被宿主 UI 兜底捕获成 WARN。

### 根因：xshd 的 `<Rule>文本</Rule>` 写法会被强制附加 `IgnorePatternWhitespace`

`CSharpHighlighting.cs` 第 154 行的预处理指令规则用"元素文本"写法：

```csharp
sb.Append("<Rule color=\"Preprocessor\">(?m)^[ \\t]*#[ \\t]*[^\\n]*</Rule>");
```

AvalonEdit 对这种写法会**强制**加 x 模式（`V2Loader.ParseRule`）：

```csharp
if (reader.NodeType == XmlNodeType.Text) {
    rule.Regex = reader.ReadContentAsString();
    rule.RegexType = XshdRegexType.IgnorePatternWhitespace;   // ← 元凶
}
```

x 模式下 `#` 起"行注释直到行尾"，于是 `#[ \t]*[^\n]*` 整段被当注释丢掉，规则退化为 `(?m)^[ \t]*` —— **每行行首都能零长度匹配**。`DocumentHighlighter` 随即抛异常：

```csharp
if (firstMatch.Length == 0) {
    throw new InvalidOperationException(
        "A highlighting rule matched 0 characters, which would cause an endless loop.\n" +
        "Change the highlighting definition so that the rule matches at least one character.\n" +
        "Regex: " + rules[ruleIndex].Regex);
}
```

正文绘制每次渲染都在此处中断，而行号由独立 Visual 绘制故仍可见；每次键入又触发重渲染再抛异常 → 表现为"空白 + 无法编辑"。

> 注：x 模式是上游有意设计，用于支持在规则文本里写 `#` 注释（内置 `CSharp-Mode.xshd` 就有 `[\d\w_]+  # an identifier`）。副作用是**字面量 `#` 必须转义为 `\#`**——上游自己的预处理 Span 正是 `<Begin>\#</Begin>`。

### 修法（一处）

`Plugins/Plugin.CSharpScript/CSharpHighlighting.cs` 第 154 行，`#` → `\#`：

```csharp
sb.Append("<Rule color=\"Preprocessor\">(?m)^[ \\t]*\\#[ \\t]*[^\\n]*</Rule>");
```

### 未改动项（经核对确认安全）
- 第 141 行逐字字符串转义 Span `<Span begin="&quot;&quot;" end="" />`：`end=""` 与 AvalonEdit 内置 `CSharp-Mode.xshd` 写法完全一致；该 Span 的 `RuleSet` 为空，入栈后其 end 立即零长度命中并出栈，此时回退校验用的是父级缓存匹配数组（索引不同）故不触发零长度守卫。**保持原样**。
- 第 157/158 行数字规则不含 `#`，`\b` 为转义序列不受 x 模式影响，安全。
- `HalconHighlighting.cs` 第 101 行的 `(?m)^[ \t]*\*[^\\n]*` 已转义 `\*`、无 `#`，即使在 x 模式下也至少匹配 1 个字符，安全。

### 验证结果

1. **正则层**（PowerShell 实测 `IgnorePatternWhitespace` 下）：

   ```
   pattern=(?m)^[ \t]*#[ \t]*[^\n]*   emptyMatch=True  len=0
   pattern=(?m)^[ \t]*\#[ \t]*[^\n]*  emptyMatch=False len=0
   ```

2. **真实加载 + 真实渲染**（临时校验工程，系统临时目录，用完即删）：反射调用 `BuildXshd` 取真实 xshd → `HighlightingLoader.Load` → 构造 `DocumentHighlighter` 逐行 `HighlightLine`（与 TextEditor 生产路径一致）：

   ```
   [no-vars]   LOAD OK  name=CSharpScript  xshdLen=4048
   [no-vars]     zero-length rules: none
   [no-vars]     DocumentHighlighter: OK (no throw)  PreprocessorColored=True
   [with-vars] LOAD OK  name=CSharpScript  xshdLen=4133
   [with-vars]   zero-length rules: none
   [with-vars]   DocumentHighlighter: OK (no throw)  PreprocessorColored=True

   === 反向自检：把 \# 还原成旧写法 # ，应当复现零长度匹配 ===
   [buggy]      zero-length rules: 1
           root regex=(?m)^[ \t]*#[ \t]*[^\n]*  probe="" idx=0
   [buggy]   已复现缺陷（校验有效）
   ALL PASS
   ```

   反向自检复现出的正则与 `idx=0` 与生产日志逐字一致，证明校验有效、修复到位，且预处理指令着色仍生效（`PreprocessorColored=True`）。

3. **编译**：`dotnet build VisionMaster.sln -c Debug` → **0 个错误**；`Modules\Plugin.CSharpScript.dll` 已由 PostBuild 刷新（79360 字节）。

### 教训
- 日志排查**必须忽略大小写**：`matched 0 characters` 是 `[WARN]` 级、无堆栈，首轮用大小写敏感模式搜索 `Exception` 直接漏掉了 9414 条关键证据。
- xshd 的坑不止"语法非法"（会抛 `HighlightingDefinitionInvalidException`），还有"语法合法但语义退化"（零长度匹配，运行时才炸且只报 WARN）。**验证必须跑真实 `DocumentHighlighter`，只 Load 是不够的**——上一节的校验工程只做了 `HighlightingLoader.Load`，所以漏过了这一层。
- 新增校验务必配**反向对照**（故意还原旧写法看能否复现），否则 `ALL PASS` 可能是空转。

### 已知边界
- `CSharpHighlighting._defWithVars` / `_varsKey` 为静态缓存，多编辑器实例共享；当前按变量集合键控，变量不变则复用同一 definition 对象（definition 本身只读，无并发写风险）。
- `CSharpIntelliSense` 的静态 `_current` / `_hoverTip` / `_paramTip` / `_hoverTimer` 属跨实例共享状态，多脚本编辑器同时打开时理论上存在串扰，本轮未处理。
- `ScriptEditorBehavior` / `CSharpIntelliSense` 通过 `Loaded +=` 挂接且未解绑，视图重挂父元素会重复 `Attach` 全套处理器与右键菜单，属独立隐患，本轮未处理。
