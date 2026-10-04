# C#脚本插件（Plugin.CSharpScript）

> **这是本插件的唯一文档。** 原先分散的 C#脚本开发记录、扩展库白名单 MVP、编辑器智能提示与高亮三份文档已全部并入本文件。
>
> 本文件分两层读法：
> - **零基础 / 操作者** → 读第 0 ~ 3 章 + 3.5~3.7 节（原理直觉、界面说明、逐步操作、参数速查、FAQ、排障）
> - **开发者 / 维护者** → 重点读第 4 章（异常与修复记录）与第 5 章（附录：**Context API 全表**、代码片段、实战案例、源码索引、回归断言）

> ⚠️ **别和「图像脚本」插件搞混**：本插件跑 **C#（Roslyn）**；`Plugin.ImageScript` 跑 **HALCON HDevelop 脚本**。两套引擎，两个插件。

---

## 目录

- [第 0 章 这是什么](#第-0-章-这是什么)
- [第 1 章 设计思想](#第-1-章-设计思想)
- [第 2 章 技术特点](#第-2-章-技术特点)
- [第 3 章 使用方式](#第-3-章-使用方式)
- [第 4 章 异常与修复记录](#第-4-章-异常与修复记录)
- [第 5 章 附录](#第-5-章-附录)

---

# 第 0 章 这是什么

**C#脚本 = 让你写真正的 C# 代码，当成一个流程节点来跑——做逻辑判定、算坐标、读写变量、控制流程。**

> 生活比喻：其它插件是"成品工具"。C#脚本是"**万能胶**"——把前面各家的结果拿来算一算、判一判，再决定下一步怎么走。

它在流程里的位置（归在 **「逻辑控制」** 分组——**全仓仅此插件用这个分组**）：

```
[图像采集] → [检测类插件] → [C#脚本] → [条件分支] / [CSV记录] / [结果上报]
                            ↑ 动态端口（按声明的变量生成）
```

**一句话**：图像类插件负责"看图"，**C#脚本负责"动脑"**——判定 OK/NG、算补偿量、格式化文本、读写变量、控制流程。

## 0.1 它的三个超能力

1. **完整 C# 语法**：class、方法、泛型、LINQ、`using`、try/catch 全都能写。
2. **动态端口**：声明什么变量，画布上就长出什么端口，能正常连线。
3. **`Context` 门面**：一套"自定义函数"，取输入、写输出、读写变量、日志、显示图像、主动失败。

## 0.2 与图像脚本的区别

| | C#脚本（本插件） | 图像脚本 |
|---|---|---|
| 语言 | **C#**（Roslyn） | **HDevelop 脚本**（类 Pascal） |
| 引擎 | Roslyn Scripting | HALCON `HDevEngine` |
| 强项 | 逻辑、LINQ、变量、流程控制 | 图像处理、HALCON 算子 |
| 分组 | 逻辑控制 | 图像处理 |

---

# 第 1 章 设计思想

## 1.1 核心原理：动态端口 + `Context` 门面

```
你在「输入变量」表加一行：名称=Count，类型=Int
        ↓ 自动重建
画布上长出输入端口 Count（int）
        ↓
脚本里写：int c = Context.GetInput<int>("Count");
        ↓
你在「输出变量」表加一行：名称=Result，类型=String
        ↓ 自动重建
画布上长出输出端口 Result（string）
        ↓
脚本里写：Context.SetOutput("Result", "NG");
```

> 变量表里的**名字 = 端口名 = 脚本里的键**，三者是同一个字符串。

## 1.2 五条设计要点（类注释原文）

```
- 动态输入端口必须在 ApplyConfigValues（编译器的 LinkPorts 之前）重建，否则按名连线失败；
- 动态输出端口走 IDynamicOutputProvider.RebuildDynamicOutputs 范式，并回写 StepData 输出快照；
- 脚本正文按内容 SHA256 指纹缓存已编译委托，内容不变零成本复用；
- 失败契约：脚本抛异常 = 失败；Context.Fail() = 显式失败；返回值不作为状态；
- "改全局变量"的正门：写已绑定到全局变量的动态输出端口
  （由引擎在连线时落回 Workspace.GlobalVariables），
  运行期临时变量读写走 context.LocalVariables。
```

## 1.3 脚本必须是"顶层语句"

> Roslyn 的 `CSharpScript` **会忽略手写的 Main / 自定义入口，只执行顶层**。
> 所以你可以在正文里定义 class / 方法 / LINQ / using，但**最终"干活"的语句要放在顶层**。

## 1.4 引用集：为什么要整目录扫描

Roslyn 编译要一份"引用集"（能 `using` 哪些程序集）。本插件用**三级来源**：

1. 运行期可信平台程序集清单（TPA）
2. 当前已加载程序集的物理路径（兜底）
3. **宿主根目录整目录扫描**（关键的一级）

> **为什么单靠 1) 和 2) 不够**：`deps.json` 里登记为 `"type": "reference"` 的库（`halcondotnet` 就是这一类）**不会**被塞进运行期 TPA，于是它能不能进引用集，完全取决于"编译脚本那一刻它有没有恰好被加载过"。
>
> 表现就是同一份脚本、同一份插件：无界面宿主里先跑了采集（碰过 `HImage`）→ 编译通过；主程序里若脚本先编译 → 报 **`The type or namespace name 'HImage' could not be found`**。
>
> 整目录扫描把这个不确定性从根上掐掉。原生 dll（`halcon.dll` 等）不是托管程序集，`CreateFromFile` 会抛异常，被就地丢弃。

## 1.5 扩展库白名单：默认拒绝（fail-closed）

想让脚本用第三方 dll？必须走白名单：

| 条件 | 结果 |
|---|---|
| `ExternalLibs\` 目录不存在 | 静默跳过（视为未启用） |
| 目录存在但**无** `whitelist.json` | **拒绝加载任何扩展**（Error） |
| 清单 JSON 解析失败 | **拒绝加载任何扩展**（Error） |
| `enabled` 不是显式 `true` | 全部不加载 |
| dll 未登记 | 忽略，但**打印真实 SHA256** 方便一键登记（Warning） |
| 与宿主程序集同名 | 以宿主版本为准，跳过扩展副本（**防影子替换**） |
| **哈希不符** | 拒载（疑似篡改，Error） |
| 全部通过 | 加编译引用 + 登记运行期路径 |

> ⚠️ **白名单只管 dll，管不住脚本正文**——脚本仍可 `Process.Start` / 读写文件。正文纪律靠"方案文件进版本管理"兜底。

---

# 第 2 章 技术特点

## 2.1 元数据

| 特性 | 值 |
|---|---|
| `Name` | C#脚本 |
| `GroupName` | **逻辑控制**（全仓唯一） |
| `Description` | 用完整 C# 语法编写脚本，可取模块参数、读写变量、输出日志、显示图像、控制流程 |
| `ShortName` | 图标（FontAwesome `f0ac`） |

实现 `IPluginCustomViewProvider` + **`IDynamicOutputProvider`**。

## 2.2 端口完整清单

### 固定端口

只有基类的 `Success` / `ErrorMessage` 两个输出。

### 动态端口（核心）

| 类型 | 生成规则 |
|---|---|
| **动态输入** | `InputVars` 每一项 → 一个 `InputPort<T>`，端口名 = 变量名 |
| **动态输出** | `OutputVars` 每一项 → 一个 `OutputPort<T>`，端口名 = 变量名 |

**声明类型 → CLR 类型映射**：

| `ScriptVarType` | 端口类型 |
|---|---|
| `Int` | `int` |
| `Double` | `double` |
| `Bool` | `bool` |
| `String` | `string` |
| `HTuple` | `HTuple` |
| `HObject` | `HObject` |
| `HImage` | `HImage` |
| `HRegion` | `HRegion` |
| `HXld` | ⚠️ **`HXLDCont`**（枚举名与 CLR 类型大小写不同） |
| `Object` / 其它 | `object` |

> ⚠️ `Type` 是**按枚举数值持久化**的（`.vms` 里 `"Type": 6`）。所以 `ScriptVarType` **新增成员只能追加到末尾，不能插队/改序**——否则存量方案会错位。

**UI 怎么跟上**：`PortsVersion` 计数器（同图像脚本），端口重建后发通知，行内 `MultiBinding` 自动重取新端口。

## 2.3 配置项（只有 3 个）

| 配置 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `ScriptText` | string | 默认模板 | C# 脚本正文（顶层语句） |
| `InputVars` | `ObservableCollection<ScriptVarDef>` | 空 | 输入变量 → 动态输入端口 |
| `OutputVars` | `ObservableCollection<ScriptVarDef>` | 空 | 输出变量 → 动态输出端口 |

**明确没有的配置**（别在文档里编造）：
- **没有超时配置**（只有 `CancellationToken` 透传）
- 没有"启用/禁用"
- 没有"引用程序集列表"配置（引用集自动构建）
- 没有"允许调用的 .NET 类型白名单"配置（白名单走磁盘文件 `ExternalLibs\whitelist.json`）
- 没有"显示窗口"全局配置（显示窗口是**每个图形输出变量自己**的 `DisplayWindow` 字段）

### `ScriptVarDef` 字段

| 字段 | 说明 |
|---|---|
| `Name` | 变量名 = 端口名 = 脚本里的键（字母开头，勿含空格） |
| `Type` | 声明类型（改了端口按新 CLR 类型重建） |
| `Remark` | 备注 → 端口 Description |
| `DisplayWindow` | `0=不显示，1~9=窗口1~9`（**仅图形类型有效**） |
| `IsIconic` | `HObject/HImage/HRegion/HXld` 为 true（输出可叠加显示、输入须链接上游） |

## 2.4 脚本引擎：Roslyn Scripting

| 项 | 说明 |
|---|---|
| 引擎 | `Microsoft.CodeAnalysis.CSharp.Scripting` 4.12.0 |
| 语言 | C# **顶层语句** |
| `Context` 注入 | `globalsType = typeof(ScriptContext)`，脚本里以顶层名 `Context` 访问 |
| 编译缓存 | `Dictionary<string, Script<object>>`，键 = 正文 SHA256 Base64；**>64 条整表清空** |
| 默认 `using` | `System` / `System.Collections.Generic` / `System.Linq` / `System.Text` / `System.Math` / **`HalconDotNet`** |
| 引用集 | 宿主运行目录里所有托管 dll（含 HalconDotNet、Newtonsoft、HslCommunication 等） |
| 执行 | `RunAsync(globals, token).GetAwaiter().GetResult()`（引擎线程本就是后台线程，无 UI 死锁风险） |

## 2.5 性能与独特实现

| 手段 / 设计 | 做法 | 收益 |
|---|---|---|
| **SHA256 指纹缓存** | 正文不变 → 复用已编译 `Script<object>` | ⭐⭐⭐ 内容不变零成本复用 |
| **引用集整目录扫描** | 掐掉"依赖加载顺序"的不确定性 | ⭐⭐⭐ 根治 `HImage could not be found` |
| **预编译提诊断** | `script.Compile()` 提前拦编译错误，给行列定位 | ⭐⭐⭐ 不用等到运行才炸 |
| **中文行列诊断** | `第 N 行 第 M 列: 消息` | ⭐⭐ 现场能自查 |
| **异常剥壳** | 剥掉 `Aggregate`/`TargetInvocationException` 外壳 | ⭐⭐ 给可读信息 |
| **防影子替换** | 与宿主同名的扩展 dll 一律不加载 | ⭐⭐ 避免 halcondotnet 版本冲突 |
| **8 个内置代码片段** | 3 分类，右键插入 | ⭐⭐ 不用从零写 |
| **编辑器全能力** | 高亮 + 补全 + 参数提示 + 悬浮文档 + Tab 步进 | ⭐⭐ 好用 |

---

# 第 3 章 使用方式

## 3.1 安装与启用

1. **无需单独安装**：正常编译解决方案即产出 `Modules/Plugin.CSharpScript.dll`（Roslyn / AvalonEdit / Prism 由 PostBuild 投递）。
2. 重开软件，工具箱里出现「C#脚本」（**逻辑控制** 分组）。

## 3.2 配置界面总览（两列）

```
┌──────────────────────────────────┬────────────────────────────┐
│ 左：460px 可滚动                   │ 右：AvalonEdit 编辑器        │
│  ① 输入变量                       │  标题「C# 脚本（顶层语句…）」  │
│     勾选框/列名/类型/连线/✕        │  代码区（行号 + 语法高亮）     │
│     「+ 新增」                     │  「校验脚本」按钮             │
│  ② 输出变量                       │  校验结果（绿 ✔ / 红 ✘）      │
│     列名/类型/显示窗口/备注/✕      │                            │
│     「+ 新增」                     │                            │
│  ③ Context 自定义函数速查（11 行） │                            │
└──────────────────────────────────┴────────────────────────────┘
```

> **左列横向滚动被刻意禁用**：一旦允许横向滚动，`ScrollViewer` 会用"无限宽"测量内容，提示文字 `TextWrapping` 失效撑成一条长线，把"显示窗口/备注/删除"整列推出视口——表现就是"输入输出变量显示不全"。

**编辑器能力**：语法高亮、智能补全（含 `Context.` 成员独占补全）、参数提示、悬浮文档、Tab 参数步进、撤销/缩进/括号配对/注释切换/缩放/当前行高亮/查找替换。

**右键菜单**：校验脚本 / 注释（Ctrl+/）/ 取消注释 / **插入代码片段（8 个按分类分组）**。

## 3.3 第一次写脚本（5 步）

**第 1 步｜拖节点**：新建就有默认脚本模板（含完整用法注释）。

**第 2 步｜声明输入**：「输入变量」点「+ 新增」，填名称、选类型，在行内点 🔗 连上游。

**第 3 步｜声明输出**：「输出变量」点「+ 新增」，填名称、选类型。图形类可选"显示到窗口"。

**第 4 步｜写脚本**：右侧编辑器写代码：

```csharp
// 取上游输入
var count = Context.GetInput<int>("DefectCount");

// 运行期变量（本次运行内共享）
var total = Context.GetVar<int>("TotalCount") + 1;
Context.SetVar("TotalCount", total);

// 判定
if (count > 0)
{
    Context.ShowImage(Context.GetInput<HImage>("Image"), 1,
                      "缺陷检测结果", ("缺陷数", count), ("判定", "NG"));
    Context.Fail($"检出 {count} 个缺陷");   // 主动判失败
    return;
}

Context.SetOutput("Result", "OK");
Context.Info("C# 脚本执行完成");
```

**第 5 步｜校验**：点「校验脚本」。绿 ✔ 通过；红 ✘ 显示 `第 N 行 第 M 列` 并跳转（Roslyn 诊断行号**即编辑器真实行号，无需换算**）。

## 3.4 类型选择速查

| 你想接/出什么 | 选什么类型 |
|---|---|
| 整数 | `Int` |
| 小数 | `Double` |
| 真假 | `Bool` |
| 文本 | `String` |
| 图像 | `HImage` |
| 区域 | `HRegion` |
| 轮廓/XLD | `HXld` |
| HALCON 元组 | `HTuple` |
| 不确定 | `Object` |

## 3.5 参数调节速查表

| 现象 | 处理 |
|---|---|
| 端口没出现 | 在变量表里加行；改了类型/名字会自动重建端口 |
| `The type or namespace name 'HImage' could not be found` | 引用集问题（**已由整目录扫描修掉**）。若仍出现，确认 `HalconDotNet.dll` 在宿主目录 |
| 写输出端口失败 | 类型不符。`catch` 会包成 `写输出端口[Name]失败：…` |
| 脚本死循环卡住流程 | **没有超时配置**。请自行用 `CancellationToken` / try-catch 保护 |
| 想用第三方 dll | 走 `ExternalLibs\whitelist.json`（见 3.6），**需重启宿主** |
| 改了白名单不生效 | 引用集只构建一次，必须重启宿主 |
| 补全里找不到 `MarkLine`/`MarkText` | 补全表尚未覆盖标注三件套（在 XAML 速查卡与模板里有） |

## 3.6 启用扩展库（三步）

1. 把 dll 扔进宿主目录下的 `ExternalLibs\`
2. 跑一次任意脚本，日志 **WARN 会带出该 dll 的真实 SHA256**
3. 粘进 `whitelist.json`，**重启宿主**生效：

```json
{
  "enabled": true,
  "remark": "总开关；false 或缺省 = 全部扩展库不加载",
  "libraries": [
    { "file": "SomeLib.dll",
      "sha256": "64位十六进制小写，必须与文件实际内容一致",
      "approvedBy": "批准人/部门",
      "purpose": "用途说明，出问题时回溯用" }
  ]
}
```

> ⚠️ 仓库里目前**没有** `ExternalLibs` 目录也没有 `whitelist.json`——功能默认处于"未启用"状态。

## 3.7 FAQ

**Q1** 脚本里能用什么？
完整 C#（class/方法/泛型/LINQ/`using`/try-catch）+ 默认 `using`（含 `HalconDotNet`）+ `Context` 门面 API + 宿主目录里所有托管 dll。

**Q2** 能引 NuGet 新包吗？
**不能**。只能走 `ExternalLibs` 白名单。

**Q3** 脚本抛异常会怎样？
**判失败**（`Success=false`，`ErrorMessage = "脚本运行异常: …"`）。已剥掉 `Aggregate`/`TargetInvocation` 外壳给可读信息。

**Q4** 返回值有用吗？
**没有**。返回值不作为状态。要判失败请用 `Context.Fail()`。

**Q5** 能改全局变量吗？
能——**写已绑定到全局变量的动态输出端口**（引擎在连线时落回 `Workspace.GlobalVariables`）。运行期临时变量走 `context.LocalVariables`（即 `Context.GetVar/SetVar`）。

**Q6** 有超时吗？
**没有超时配置**，只透传 `CancellationToken`。脚本死循环会卡住流程，请自行保护。

**Q7** 有沙箱吗？
**没有**。白名单只管 dll，管不住脚本正文（仍可 `Process.Start` / 读写文件）。

**Q8** 脚本能显示图像吗？
能。`Context.ShowImage(img, 1)` 推到主界面 1 号窗口；还有带标注、带标题键值的重载。

## 3.8 现场排障

| 报错 / 现象 | 排查 |
|---|---|
| `第 N 行 第 M 列: …` | Roslyn 编译错误，**行号即编辑器真实行号** |
| `脚本编译错误: …` | 运行期才冒出的延迟诊断 |
| `脚本运行异常: …` | 脚本内抛的异常（已剥壳） |
| `写输出端口[X]失败：…` | 输出类型不符 |
| `The type or namespace name 'HImage' could not be found` | 引用集残缺（已由整目录扫描修掉；确认 dll 在宿主目录） |
| 白名单 WARN「未登记」 | 日志里带真实 SHA256，粘进 `whitelist.json` 后重启 |
| 白名单 ERROR「哈希不符」 | dll 被改动过，重新登记 |
| 改了配置没生效 | 引用集只构建一次 → **重启宿主** |
| 构建报 `MSB3073` | 主程序占用 `Microsoft.CodeAnalysis*.dll`。**关程序 → 构建 → 核对 `Modules\Plugin.CSharpScript.dll` 时间戳 → 再启动** |

---

# 第 4 章 异常与修复记录

> 本章记录**真实发生过的问题**（来自代码注释与开发文档）。每条给：现象 → 原因 → 修法。

## 4.1 缺陷修复总览

| 级别 | 问题 | 一句话 |
|---|---|---|
| P0 | 命名空间遮蔽 Roslyn 同名类型 | 必须用 `global::` 全限定 |
| P0 | `HImage could not be found`（引用集依赖加载顺序） | 加"宿主根目录整目录扫描" |
| P1 | `CompilationErrorException` 真身位置 + `Diagnostics` 不能用 `?.` | `ImmutableArray` 是结构体 |
| P1 | xshd `Span` 下放 `Rule` 抛异常 | `HighlightingDefinitionInvalidException` |
| P1 | xshd `#` 未转义 → 零长度匹配 | 编辑器空白不可编辑 |
| P1 | `CopyLocalLockFileAssemblies` 未开 | Roslyn dll 没拷到输出 |
| P2 | 块注释 `*/` 是非法正则 | xshd 需转义 |
| P2 | 横向滚动导致变量显示不全 | 刻意禁用横向滚动 |
| P2 | 行内输入框高度不一致 | 用 `MinHeight` 兜底，不写死 Height |
| P2 | 编辑器双向绑 `Text` 导致换行/光标跳动 | 改 Loaded 拉取 + TextChanged 回写 |

## 4.2 P0/P1 级详解

### ① 命名空间遮蔽（最容易踩）

**现象**：`CSharpScript.Create<object>(...)` 编译不过或行为异常。

**原因**：本工程命名空间末段就叫 `CSharpScript`，**会遮蔽 Roslyn 的同名类型**。

**修法**：用 `global::` 全限定：

```csharp
global::Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript.Create<object>(...)
```

### ② `HImage could not be found`（最隐蔽的"测试能过、现场不能过"）

**现象**：同一份脚本、同一份插件——无界面宿主里能编译（先跑了采集，碰过 `HImage`）；主程序里若脚本先编译就报错。

**原因**：`deps.json` 里登记为 `"type": "reference"` 的库（`halcondotnet`）**不会**被塞进运行期 TPA，能否进引用集取决于"编译那一刻它有没有恰好被加载过"。

**修法**：加第三级来源——**宿主根目录整目录扫描**。原生 dll 不是托管程序集，`CreateFromFile` 会抛异常，被就地丢弃，不影响其余引用。

### ③ `CompilationErrorException` 的两个坑

| 坑 | 说明 |
|---|---|
| 真身位置 | 在 **`Microsoft.CodeAnalysis.Scripting`**（不是 CSharp.Scripting） |
| `Diagnostics` 不能用 `?.` | 它是 **`ImmutableArray`（结构体）**，要用 `IsDefault`/`Length` 判空 |

```csharp
var d = (!cex.Diagnostics.IsDefault && cex.Diagnostics.Length > 0)
        ? cex.Diagnostics[0] : null;
```

### ④ xshd 两个坑（编辑器）

| 坑 | 现象 | 修法 |
|---|---|---|
| `Span` 下不能直接放 `Rule` | 抛 `HighlightingDefinitionInvalidException` | Rule 只能放 `RuleSet` 下 |
| `#` 未转义 | `<Rule>文本</Rule>` 会被强制加 `IgnorePatternWhitespace`，`#` 起行注释把后面全注释掉 → 规则退化成零长度匹配 → **只有行号、内容空白、无法编辑** | 必须写成 `\#` |
| 块注释 `*/` | 是非法正则 | 需转义 |

### ⑤ `CopyLocalLockFileAssemblies`

**现象**：Roslyn dll 没拷到输出目录。

**修法**：工程里开 `CopyLocalLockFileAssemblies=true`。

## 4.3 代码级踩坑汇编

| # | 现象 | 原因 | 正确做法 |
|---|---|---|---|
| 1 | `CSharpScript` 类型解析错 | 命名空间遮蔽 | `global::` 全限定 |
| 2 | `HImage could not be found` | TPA 不含 `type:reference` 库 | 整目录扫描 |
| 3 | `Diagnostics` 判空失效 | `ImmutableArray` 是结构体 | 用 `IsDefault`/`Length` |
| 4 | 编译错误到运行才炸 | `Create` 不抛，首次运行才抛 | 提前 `script.Compile()` 提诊断 |
| 5 | 编辑器空白不可编辑 | xshd `#` 未转义 | 写 `\#` |
| 6 | 高亮定义抛异常 | `Span` 下放 `Rule` | 放 `RuleSet` 下 |
| 7 | Roslyn dll 缺失 | 未开 `CopyLocalLockFileAssemblies` | 开启 |
| 8 | 扩展库改了不生效 | 引用集只构建一次 | 重启宿主 |
| 9 | halcondotnet 版本冲突 | 扩展副本影子替换 | 与宿主同名一律用宿主版 |
| 10 | 行内输入框高度不齐 | 写死 Height 24 与 ComboBox 32 冲突 | `MinHeight` 兜底 |
| 11 | 变量显示不全 | 横向滚动撑宽内容 | 禁用横向滚动 |
| 12 | 换行/光标跳动 | 双向绑 `Text` | Loaded 拉取 + TextChanged 回写 |
| 13 | `ScriptVarType` 错位 | 按数值持久化 | 新增只能追加 |

## 4.4 已知边界与待办

- ⚠️ **`FlowCanvasChecks/` 没有 C#脚本专属断言**——只有 `WireSequenceCheck.cs` 间接覆盖（线序检测端到端，含"脚本步骤自报执行成功"）。
- **无超时配置**（只有 `CancellationToken`）——脚本死循环会卡住流程。
- **无沙箱**——白名单只管 dll，管不住脚本正文。
- **`ExternalLibs\whitelist.json` 仓库内不存在**——功能默认关闭。
- **引用集与编译缓存都是进程级静态终身缓存**，无 `AssemblyLoadContext.Unload()`；靠 64 条上限与进程级复用控制增长。
- **`ScriptVarType` 按数值持久化**（`.vms` 里 `"Type": 6`）——新增枚举只能追加。
- **`HXld` ↔ `HXLDCont`** 命名不一致（写类型对照表时要点明）。
- **`CSharpApiDoc` 与 `ScriptContext` 手工同步**——且补全表**缺** `MarkLine`/`MarkText`/`NewMarks`/非泛型 `GetVar`。
- 白名单改动 / 新增 dll 都要**重启宿主**。
- 审计日志**全局只投一次**（引擎是静态类拿不到 `IExecutionContext`，暂存后首次执行取走并清空）。
- 构建时主程序占用 `Microsoft.CodeAnalysis*.dll` 会让 PostBuild 报 `MSB3073` 并让整次构建判失败、投递中断。

---

# 第 5 章 附录

## 5.1 附录 A：搭配使用的算子清单

★ = 与 C#脚本直接相关。

| 工程目录 | 中文名 | 分组 | 怎么接 | 一句话用途 |
|---|---|---|---|---|
| Plugin.ImageAcquisition ★ | 图像采集 | 常用工具 | 输出 → 脚本输入 | 取图 |
| Plugin.BlobDetect ★ | Blob 缺陷检测 | 缺陷检测 | `DefectCount`/`IsOk` → 脚本输入 | 检测结果 |
| Plugin.CaliperMeasure ★ | 卡尺测量 | 测量 | `MeasureValue` → 脚本输入 | 测量值（算补偿） |
| Plugin.Ocr ★ / Plugin.CodeReader ★ | OCR / 码读取 | 图像处理 | `Text` → 脚本输入 | 文本比对 |
| Plugin.Yolo ★ | YOLO 检测 | 图像处理 | `Count`/`ClassNames` → 脚本输入 | 检测结果 |
| **Plugin.CSharpScript** | **C#脚本** | **逻辑控制** | — | 逻辑判定、算坐标、控制流程 |
| 条件分支 ★ | 流程控制 | — | 脚本输出 → 分支条件 | 走不同分支 |
| Plugin.DataRecord ★ | CSV记录 | 数据处理 | 脚本输出 → 记录 | 存档 |
| Plugin.ResultUpload ★ | 结果上报 | 数据处理 | 脚本输出 → 上报 | MES |

### 典型接线

```
① 判定 + 记录
Blob.IsOk / DefectCount ──▶ C#脚本 ──▶ Result ──▶ CSV记录

② 分支（线序检测端到端就是这么用的）
采集.Image ──▶ C#脚本_0（找线）──▶ 条件分支 ──▶ C#脚本_1（比对）──▶ 结果

③ 显示带标注的图
C#脚本（ShowImage 带标注 + 键值）──▶ 主界面窗口
```

## 5.2 附录 B：`Context` 自定义函数速查（★ 重点）

### B.1 模块参数 / 端口

| API | 签名 | 说明 |
|---|---|---|
| `Context` | `ScriptContext Context` | 门面本身（脚本里直接用 `Context`） |
| `Context.GetInput` | `object GetInput(string portName)` | 取输入端口值（弱类型），不存在返回 null |
| `Context.GetInput<T>` | `T GetInput<T>(string portName)` | 取并转 T；转换失败抛 `InvalidCastException` |
| `Context.SetOutput` | `void SetOutput(string portName, object value)` | 写输出端口；端口不存在则忽略 |

### B.2 运行期变量池（本次运行内共享，非跨运行持久）

| API | 签名 | 说明 |
|---|---|---|
| `Context.GetVar` | `object GetVar(string name)` | 不存在返回 null |
| `Context.GetVar<T>` | `T GetVar<T>(string name)` | 不存在/不可转返回 `default` |
| `Context.SetVar` | `void SetVar(string name, object value)` | 写运行期变量 |
| `Context.Vars` | `IDictionary<string, object> Vars` | 只读遍历 |

### B.3 日志（自动带 `[实例名]` 前缀）

| API | 说明 |
|---|---|
| `Context.Info(msg)` | 普通日志 |
| `Context.Warn(msg)` | 警告（不影响判定） |
| `Context.Error(msg)` | 错误日志 |
| `Context.Success(msg)` | 成功日志（绿色） |

### B.4 图像显示 / 标注 ★

| API | 签名 | 说明 |
|---|---|---|
| `ShowImage` | `(HImage image, int viewIndex = 1)` | 推到主界面第 N 号窗口（1~9） |
| `ShowImage` | `(HImage, int, IEnumerable<MeasureAnnotation>)` | 带**测量标注**同帧覆盖渲染 |
| `ShowImage` | `(HImage, int, string title, params (string Label, object Value)[] info)` | 带**标题 + 键值信息**推图（画布图片列表按此显示） |
| `ShowImage` | `(HImage, int, string, IEnumerable<MeasureAnnotation>, params (…)[])` | 标注 + 标题 + 键值全都要 |
| `NewMarks()` | `List<MeasureAnnotation>` | 建空标注层 |
| `MarkLine(row1,col1,row2,col2,color="green")` | `MeasureAnnotation` | 线标注（图像坐标 **行,列**） |
| `MarkText(row,col,text,color="green")` | `MeasureAnnotation` | 文本标注 |

```csharp
// 用法范例（源码注释原文）
Context.ShowImage(img, 1, "缺陷检测结果", ("缺陷数", 51), ("判定", "NG"));
```

> 键值行里**空键会被跳过**（列表里不出现无名信息）。

### B.5 控制流

| API | 说明 |
|---|---|
| `Context.Fail(message)` | **主动判失败**（置 `Failed=true` → 宿主转 `Success=false`）；**建议 Fail 后立即 `return`** |

### B.6 脚本里还能用什么

- 完整 C#：class、方法、泛型、LINQ、`using`、try/catch
- 默认 imports：`System` / `System.Collections.Generic` / `System.Linq` / `System.Text` / `System.Math` / **`HalconDotNet`**
- 宿主运行目录里所有托管 dll（HalconDotNet、Newtonsoft、HslCommunication 等）
- **不能**在脚本里引 NuGet 新包

## 5.3 附录 C：8 个内置代码片段

| 分类 | 标题 |
|---|---|
| 输入输出 | 取输入并写输出（基本骨架） |
| 输入输出 | 弱类型取输入（手动转换） |
| 输入输出 | 运行期变量计数（GetVar/SetVar） |
| 流程控制 | 判定 NG（Fail + return） |
| 流程控制 | try-catch 保护 |
| Halcon 图像 | 阈值分割 + 面积统计 |
| Halcon 图像 | 灰度统计（均值/标准差） |
| Halcon 图像 | 显示图像到视图窗口 |

## 5.4 附录 D：实战案例（6 个）

### 案例 1｜判定 NG（Fail + return）

```csharp
var count = Context.GetInput<int>("DefectCount");
if (count > 0)
{
    Context.Fail($"检出 {count} 个缺陷");   // 主动判失败
    return;                                  // ★ 必须短路
}
Context.SetOutput("Result", "OK");
```

### 案例 2｜运行期变量计数

```csharp
int total = Context.GetVar<int>("TotalCount") + 1;
Context.SetVar("TotalCount", total);
Context.SetOutput("累计", total);
Context.Info($"累计 {total} 件");
```

### 案例 3｜阈值分割 + 面积统计（Halcon 直用）

```csharp
var img = Context.GetInput<HImage>("Image");
HOperatorSet.Threshold(img, out var region, 0, 128);
HOperatorSet.Connection(region, out var conn);
HOperatorSet.AreaCenter(conn, out var area, out var row, out var col);
Context.SetOutput("面积", area.DArr.Length > 0 ? area.DArr.Max() : 0);
```

### 案例 4｜显示带标题 + 键值的图

```csharp
var img = Context.GetInput<HImage>("Image");
Context.ShowImage(img, 1, "缺陷检测结果", ("缺陷数", 51), ("判定", "NG"));
// 画布的图片列表会显示：标题 / 键值行 / 来源
```

### 案例 5｜显示带测量标注的图

```csharp
var img = Context.GetInput<HImage>("Image");
var marks = Context.NewMarks();
marks.Add(Context.MarkLine(100, 200, 300, 400, "green"));
marks.Add(Context.MarkText(100, 200, "起点", "green"));
Context.ShowImage(img, 1, marks);
```

### 案例 6｜try-catch 保护

```csharp
try
{
    var v = Context.GetInput<double>("MeasureValue");
    Context.SetOutput("偏差", v - 100.0);
}
catch (Exception ex)
{
    Context.Error($"计算失败: {ex.Message}");
    Context.SetOutput("偏差", 0.0);   // 给个安全值，不让下游拿到脏数据
}
```

## 5.5 附录 E：源码索引

### E.1 文件清单（`Plugins/Plugin.CSharpScript/`）

| 文件 | 职责 |
|---|---|
| `CSharpScriptPlugin.cs` | **主插件类**（413 行）：元数据、配置、动态端口、`RunAlgorithm` |
| `CSharpScriptEngine.cs` | **Roslyn 脚本宿主**（331 行）：编译/缓存/执行/引用集/**扩展库白名单** |
| `ScriptContext.cs` | 脚本门面（200 行）：`Context` 的全部 API |
| `ScriptVarDef.cs` | `ScriptVarType` 枚举 + `ScriptVarDef`（81 行） |
| `CSharpScriptView.xaml` | 配置界面（294 行） |
| `CSharpScriptView.xaml.cs` | 视图桥接 + `InputPortConverter` + 校验 + 右键菜单（267 行） |
| `CSharpApiDoc.cs` | 编辑器 API 文档数据层（158 行，**手工同步**） |
| `CSharpIntelliSense.cs` | 智能提示引擎（494 行） |
| `CSharpHighlighting.cs` | 运行时动态 xshd 高亮（198 行） |
| `CSharpCompletionData.cs` | 三类补全项（101 行） |
| `CSharpTemplates.cs` | 8 个代码片段（105 行） |

> 本插件**没有** `Models/` / `Services/` 目录，模型与视图代码都在根目录。

### E.2 关键方法与位置

| 方法 | 位置 | 说明 |
|---|---|---|
| `ApplyConfigValues` | `CSharpScriptPlugin.cs:112` | 基类灌值后**立刻**重建输入+输出动态端口（LinkPorts 之前） |
| `RebuildDynamicInputs` | `:146` | 按 `InputVars` 建动态输入 |
| `RebuildDynamicOutputs` | `:166` | `IDynamicOutputProvider` 实现（含 `StepData.OutputPortDefinitions` 快照回写） |
| `CreateInputPort`/`CreateOutputPort` | `:194`/`:208` | 类型映射（`HXld` → `HXLDCont`） |
| `ValidateScript` | `:281` | `Compile(force:true)` |
| `RunAlgorithm` | `:291` | 主执行链路（编译 → 审计投递 → 执行 → 失败/成功 → 推预览） |
| `PublishIconicOutputs` | `:393` | 按 `DisplayWindow >= 1 && IsIconic` 逐路推预览 |
| `CSharpScriptEngine.Compile` | `Engine:39` | SHA256 指纹缓存 + 预编译提诊断 |
| `CSharpScriptEngine.GetOptions` | `Engine:102` | **三级引用集 + 整目录扫描 + 默认 using** |
| `LoadWhitelistedExtensions` | `Engine:215` | 白名单判定（7 级顺序） |
| `HookResolving` | `Engine:294` | 编译引用 ≠ 运行加载的兜底 |

### E.3 数据模型（`ScriptVarDef.cs`）

`ScriptVarType`：`Object, Int, Double, Bool, String, HTuple, HImage, HRegion, HXld, HObject`

| 字段 | 默认 | 说明 |
|---|---|---|
| `Name` | `""` | 变量名 = 动态端口名（字母开头，勿含空格） |
| `Type` | `Object` | 声明类型（变更后端口按新 CLR 类型重建） |
| `Remark` | `""` | 备注 → 端口 Description |
| `DisplayWindow` | `0` | `0=不显示，1~9=窗口1~9`（仅图形有效） |
| `IsIconic` | 计算 | `HObject/HImage/HRegion/HXld` → true |

## 5.6 附录 F：回归断言清单

⚠️ **`FlowCanvasChecks/` 没有 C#脚本专属断言文件**（27 个 `*Checks.cs` 中无 `CSharpScriptChecks.cs`）。

**唯一覆盖到本插件的是线序检测端到端用例**：`FlowCanvasChecks/WireSequenceCheck.cs`（`Program.cs:98`）：

| 分组 | 测试点 |
|---|---|
| **W1 插件装载** | `Plugin.CSharpScript.dll` 等可 `Assembly.LoadFrom` |
| **W2 方案加载** | 生产 `SolutionService().LoadAsync()` 能打开 `解决方案/线序检测.vms` |
| **W3 流程骨架** | 主干 5 步，类型依次是 采集 / **脚本** / 条件分支 / **脚本** / 条件分支（断言 `PluginName == "C#脚本"`）；If/ElseIf/Else 三路；`WireCount` 运行时变量已声明；两个脚本的 `Image` 输入都连到采集的 `Image` |
| **W4/W5 三路执行** | 5 芯 → If → 配方 A；10 芯 → ElseIf → 配方 B；未知芯数 → Else → `Result=false`、结论含「未知产品」、`Verdict=="NG"` |
| **W6 投射** | 脚本发过预览帧；**带标注帧恰好 2 帧**；窗口号 1~9 合法；标注 = 1 线 + 11 文本；逐根标签 = 实测线序且行号递增；结论行绿字 |

> 引用集回归的设计说明（原文摘录）：
> ```
> 于是脚本引擎的 Roslyn 引用集一度取决于"编译脚本那一刻 HalconDotNet 有没有被加载过"——
> 主程序里报过「The type or namespace name 'HImage' could not be found」，而无界面宿主里同一份脚本却能编译。
> 本条检查不直接断言引用集内部（GetOptions 是私有的），而是用「脚本步骤自报执行成功」覆盖它：
> 引用集一旦残缺，脚本就编译不过，那一条会立刻变红。
> ```

> 历史：早期引擎冒烟（8 项）与白名单冒烟（9 项）都是**仓库外一次性控制台工程，跑完即删未提交**。
>
> **维护建议**：后续补 `FlowCanvasChecks/CSharpScriptChecks.cs`，覆盖：默认脚本可编译 / 动态端口正确生成 / `Context.Fail` 生效 / 写输出类型错误有中文提示 / 白名单 7 级判定 / 引用集含 HalconDotNet。

## 5.7 附录 G：术语表

| 术语 | 含义 |
|---|---|
| Roslyn | .NET 的编译器平台（本插件用它做脚本编译执行） |
| 顶层语句 | C# 的顶层语句写法——`CSharpScript` **只执行顶层**，忽略手写 Main |
| globals | Roslyn 注入的全局对象（本插件就是 `Context`） |
| `ScriptContext` | 脚本门面——不持有任何 UI 对象，跨线程安全 |
| 动态端口 | 运行期按变量表生成的端口 |
| `IDynamicOutputProvider` | 框架接口，编译前调用 `RebuildDynamicOutputs()` |
| `PortsVersion` | 端口重建计数器，UI 用它自动重取新端口 |
| TPA | Trusted Platform Assemblies，运行期可信平台程序集清单 |
| `type: reference` | deps.json 的一种登记方式——这类库**不会**进 TPA（根因） |
| 白名单 fail-closed | 默认拒绝：缺失/解析失败/未登记/哈希不符/开关关闭一律不加载 |
| `ExternalLibs` | 扩展库目录 + `whitelist.json` |
| 影子替换 | 与宿主同名的 dll 副本被误加载导致版本冲突 |
| xshd | AvalonEdit 的语法高亮定义格式 |
| `#` 转义 | xshd 里 `#` 必须写成 `\#`，否则零长度匹配导致编辑器空白 |

## 5.8 附录 H：文档合并说明

本文件由以下三份文档合并而成（内容已全部并入，原文件已归档在 `docs/C#脚本/`）：

| 原文件 | 并入位置 |
|---|---|
| `2026-09-16-CSharp脚本插件.md`（8 KB） | 第 1 章（Roslyn 选型/顶层语句/门面/失败契约/指纹缓存）+ 第 2 章 + 第 4.2 节（命名空间遮蔽、CompilationErrorException、CopyLocal）+ 第 4.4 节（已知边界 8 条） |
| `2026-09-16-CSharp脚本扩展库白名单MVP.md`（7.92 KB） | 第 1.5 节（fail-closed 决策 + 7 级判定）+ 第 3.6 节（启用三步 + whitelist 格式）+ 第 4.4 节 |
| `2026-09-19-CSharpScript编辑器智能提示与高亮.md`（17.29 KB） | 第 2.5 节（编辑器能力）+ 第 3.2 节（界面）+ 第 4.2 节④（xshd 三坑）+ 已知边界 |

**未并入、但保留为参考的跨插件文档**：
`docs/颜色检查/2026-09-25-线序检测投射与脚本引用集修复.md`（已归档，但"引用集 `type:reference` 根因""HostDir 整目录扫描""NewMarks/MarkLine/MarkText 来源""Modules 投递四步走"都是本插件硬知识，已在本文档重新表述）、`docs/code-changes/2026-09-20-插件构建投递收口Directory.Build.targets.md`（Roslyn dll 投递一行）、各插件文档的"搭配使用"表。
