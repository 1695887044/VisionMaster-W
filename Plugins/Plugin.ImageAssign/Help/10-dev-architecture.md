# 图像赋值 · 开发：架构、宿主契约与不变量

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageAssign ｜ 读者：开发

S 档一页承担三个角色：架构与数据流、白拿的宿主行为、改这个文件时的红线。
用户册讲的"怎么填"这里不重复，只讲**这些行为由谁提供、为什么这么写、写错会怎样**。

## 一、类身份：全仓最薄的一类插件

```
Plugins\Plugin.ImageAssign\
  ImageAssignPlugin.cs      83 行   ← 两个输入端口 + RunAlgorithm + 空的 Initialize
  Plugin.ImageAssign.csproj         ← 只引 Core.Interfaces，另挂 halcondotnet 编译期引用
  Help\*.md                         ← 内嵌资源，宿主按 {程序集名}.Help. 前缀扫描
```

单插件程序集，手册**必须扁平放 `Help\` 下**（不建 `{类名}\` 子目录——那是多插件程序集才需要的）。

- `[Display]` 实值：`Name="图像赋值"`、`GroupName="图像处理"`、
  `Description="把输入图像写入指定的全局图像变量（配合组态图像显示图元使用）"`、
  `ShortName="\uf03e"`（私有区字形，靠 FontAwesome 字体渲染，换字体就是方框）。
- 基类：`VisionPluginBase`。**不实现** `IPluginCustomViewProvider` / `IPluginConfigView` / `IStepConfigData` 的自定义分支，
  也**没有** `[StepConfig]` 属性、**没有** `[ParallelSafe]` 标注。
- csproj 刻意**不引** UI / `Core.Controls`、**不开** `UseWPF`：本插件只搬引用、不画界面。
- `halcondotnet.dll` 只做**编译期**引用（`HintPath` 指 `..\..\DLL\Halcon\`），
  **切勿拷进 `Modules\`**（会造成运行期两份同名程序集、类型标识冲突——AGENTS.md 红线②；
  插件自身 DLL 的投递由 `Plugins\Directory.Build.targets` 统一处理，本工程没有 PostBuild）。

## 二、`RunAlgorithm` 逐步（执行时间线）

| # | 代码动作 | 为什么必须在这一刻做 |
| --- | --- | --- |
| 1 | `InputImage.GetTypedValue()` | 端口生效值在"读的这一刻"确定（链接值优先于手填值）；开头取一次，整步用它 |
| 2 | `image == null or !image.IsInitialized()` → `Fail("输入图像为空或未初始化")` | 上游失败时图像输出通常是空句柄；这里必须**当场**判失败，否则 `TryWrite` 会把一个空句柄塞进变量、显示端才炸 |
| 3 | `TargetVariableName.GetTypedValue()` → 空白判 `Fail("目标变量名不能为空")` | 名字空 = "写到哪"未定；不做默认名兜底（静默写去别处比失败更糟） |
| 4 | `var writer = context?.GlobalVariables;` 为 null → `Fail("当前执行环境不支持写入全局变量")` | 防御支。正式的 `ExecutionContext` 是惰性 `new GlobalVariableWriter(Workspace)`、**永不返回 null**（`Core/ExecutionContext.cs:104`），所以这一支只在**简化构造的上下文 / 断言夹具**里命中 |
| 5 | `writer.TryWrite(targetName, image, out var error)` | 三件事全在宿主写入器里做：**查名 → 类型守门 → 落值**。插件不参与任何一条 |
| 6 | 返回 false → `Fail(error 非空 ? error : "写入全局变量「X」失败")` + `Logger.Error` | **按真实返回值如实反馈**，不做"无条件报成功"。原因优先用写入器给的原文（含变量名、声明类型、被拒的值），插件的兜底文案只在写入器没给原因时出现 |
| 7 | 返回 true → 只 `Logger.Info`，**不碰 `Success`** | 基类在每次执行开头预置 `Success=true`，所以成功路径什么都不用写；写了反而是重复劳动 |

三个 `Fail` 出口都是**提前 return**，不会走到第 5 步；`Fail` 之后流程照常往下执行（要不要停是流程/容器的事）。

## 三、图像句柄：怎么交给下游、谁负责 Dispose

| 对象 | 谁创建 | 谁释放 | 本插件的动作 |
| --- | --- | --- | --- |
| 上游那张 `HImage` | 上游算子（采集 / 预处理 / 结果显示） | 归生产者一侧的回收路径 | **只转交引用**：不 `new`、不 `Clone`、不 `Dispose` |
| 全局变量里存的那份 | 写入器把同一个引用存进变量 | 由变量侧与生产者侧共同遵守"引用不是快照"的口径 | 本插件不介入，也不清空旧值 |
| 本插件的输出端口 | 无（本插件零输出端口） | 基类轮首回收只遍历**输出端口**里 `IDisposable` 的值 | 因此那条自动回收对本算子**一个句柄都碰不到** |

关键结论（都是设计，不是巧合）：

- **写进去的是引用，不是快照**：零拷贝、零额外显存；上游若原地修改同一实例，变量里看到的会跟着变。
  要"冻结某一帧"应由**上游产出新实例**，不在本算子里想办法（在这里 `Clone` 等于每次跑流程复制一整张图）。
- **本插件不覆写 `Dispose()`**：它没有任何自有资源。若哪天加了自有句柄，必须同时补 `Dispose` 并调 `base.Dispose()`。
- **图像变量的当前值不落盘**：`HImage` 无法 JSON 序列化，落盘只写变量的**定义**（名字 / 类型 / 稳定身份），
  值与初始值落 `null`——所以"重启后画面无图"是口径而非缺陷（守门见第六节 A4）。

## 四、白拿的宿主 / 基类行为（作者不用写、也别重写）

| 行为 | 由谁提供 | 表现 |
| --- | --- | --- |
| 端口发现与注册 | `VisionPluginBase` 反射 public 端口属性 | 端口名可省略（自动取属性名）。本插件 `TargetVariableName` 属性**显式写了端口名 `VariableName`**，存档键随之是 `VariableName` |
| 灌值 / 写回 | 基类 `ApplyConfigValues` + `OnConfirm` | 面板显示**已保存值**而非默认值；已链接的口在写回时移除常量键 |
| 参数面板 | 宿主 `AutoPortConfigView`（框架包一层） | 不实现自定义视图接口就自动有面板；面板**只列输入端口**，行标签取端口的**描述文字** |
| 成功/失败契约 | `VisionPluginBase.Fail(...)` | 置 `Success=false` + `ErrorMessage`，后续步骤照常执行 |
| 异常兜底 | 基类 `Execute` 包裹 `RunAlgorithm` | 未捕获异常转成失败与错误文案，不把流程线程带崩 |
| 写全局变量的能力 | `IGlobalVariableWriter`（宿主实现）经 `IExecutionContext` 递送 | 插件只引用 `Core.Interfaces`，物理上够不到「变量管理」所在程序集——**这是它必须存在的原因** |

`[StepConfig]` 源生成器（AGENTS.md R16/R18）**在本插件上没有产出**：一个配置属性都没有，
所以生成物的属性实现/钩子声明这一半边不参与。但纪律仍然生效：
**日后若加 `[StepConfig]`**，改动生成器或本插件写法都要重建插件工程（否则跑的是旧 DLL）。

## 五、`.vms` 落盘键（引用一律用名字，不用显示名）

| 存的东西 | 键 | 值形态 |
| --- | --- | --- |
| 目标变量名 | `InputValues["VariableName"]` | 字符串，逐字是全局变量的**名字** |
| 图像 | `InputValues` 里**没有**这一项 | 只能由连线提供；连线本身记的是上游步骤/端口标识 |
| 变量本身 | 方案变量表 | 名字 / 声明类型 / 稳定身份；**当前值与初始值不落盘** |

- 面板上那行的中文标签（"目标全局图像变量名"）**不进存档**，改描述文字不会打断任何方案。
- 变量名是**按名字寻址**的：改名 = 所有写它的步骤失配（与 `MotionDescriptor` 的"引用用 Id 不用显示名"同一条纪律的另一种表现）。

## 六、A 组不变量（违反就编译/闸门红）

**前提事实**：`ImageAssignPlugin` **没有专属断言文件**（`FlowCanvasChecks\` 里只在
`HelpManualCheck.cs:121` 以 `["ImageAssignPlugin"] = ManualTier.S` 登记过档位）。
下面能引的断言名都来自**共享契约的守门人**（连线类型判据、全局变量写入器、手册闸门），
逐条回源码抄；引不到的写明"无断言，靠人工核对"。

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 区域/数值输出被拖到图像口上，运行期才炸 | 编译期连线类型判据放行错了 | `【判据】HRegion → HImage 不可绑`、`【判据】HImage → 数值端口不可绑`（`FlowCanvasChecks\PortBindChecks.cs`） |
| 文本口连不进上游字符串 | `string` 端口的"字符串化是有意放开的"这条判据被改坏 | `【判据】任意类型 → string 端口可绑（字符串化是有意放开的）`（同上） |
| 图像塞不进全局变量 / 类型守门失效 | 宿主写入器的"查名 → 类型守门 → 落值"三步是共用件 | `【全局】目标存在 + 值是 HImage → 写进全局变量`、`【全局】名字行挂「选中的全局变量」连线 → 写不到 OKCount，且运行期明确失败（连线给的是值，当不了名字）`（`FlowCanvasChecks\VariableAssignmentCheck.cs`；断言对象是「变量赋值」，但与本算子共用同一个写入器） |
| 方案存不下带图像变量的表（旧实现抛 HALCON `#4056`） | 序列化时误把像素/句柄当值写 | `含图像变量的方案能序列化（旧实现抛 HALCON #4056 object-ID is NULL）`、`图像变量的定义照样落盘（名字 / 类型 / 稳定身份都在）`、`图像变量的当前值与初始值都不落盘（落 null，不把像素写进 .vms）`（同上） |
| 手册被自动生成页顶掉 / 串页 / 不达标 | Help 页没进 DLL 或命名不合骨架 | `HM4 有 Help/*.md 的工程都带 EmbeddedResource（手册进得了 DLL）`、`HM2 多插件程序集不许放扁平手册（防串页）`、`HM3 Help 子目录名逐字等于插件类名（分册能被认出）`、`HM8`（用户册合计 ≥70 行）、`HM9`（禁占位词）、`HM12`/`HM12b`（`NN-dev-` 命名、不许另立子目录）、`HM13`（本档 S：开发页 1 页、合计 ≥100 行）（`FlowCanvasChecks\HelpManualCheck.cs`） |

## 七、B 组不变量（不报错，只出诡异现象）

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 永远报失败且 `ErrorMessage` 为空 | 派生类重复声明 `Success` / `ErrorMessage`，影子隐藏基类成员（插件写自己那份、引擎读基类那份，源码注释里的 CS0108 教训） | 无断言，靠人工核对（禁止再声明同名端口） |
| "写成功了但写去了别的变量" | `VariableName` 被挂上"由上游提供值"的通用变量绑定 → 拿值当名字；若撞上同名 `object` 变量则真的写进去 | 无本算子专属断言；同族判据见上表 `VariableAssignmentCheck` 的「名字行挂连线」那条 |
| 画面显示的图跟着上游一起变 | 存的是引用不是快照，上游原地修改会串 | 无断言，靠人工核对（第三节口径） |
| 最后一帧图像句柄没人收 | 本插件零输出端口 → 基类轮首回收路径碰不到它；所有权始终在生产者一侧 | 无断言，靠人工核对 |
| 变量不存在时"看起来跑通了" | 若哪天把 `TryWrite` 的返回值改成不判（"无条件报成功"），操作员永远看不到原因 | 无断言，靠人工核对（源码第 6 步就是这个守门） |
| 重启后画面空白被当成 bug 提单 | 图像变量当前值设计上不落盘 | 同 A 组第三行的三条落盘断言可反证 |

## 八、改完这个文件必须做的动作

| 改了什么 | 必须同步 |
| --- | --- |
| 端口名 / 默认值 | 用户册《端口与参数》的端口表与存档键表；`InputValues` 的键会变，老方案里的旧键静默失效 |
| 错误文案（三个 `Fail` + 兜底句） | 《排错》第一节的原文对照表（那张表要求逐字一致，改了不改手册就是假绿） |
| 新增 `[StepConfig]` 属性 | 用户册《端口与参数》的"关键参数"节；并按 AGENTS.md R16/R18 重建插件工程 |
| 加自有句柄（例如真的开始 `Clone`） | 必须补 `Dispose` 并调 `base.Dispose()`；第三节的所有权表要重写 |
| 手册页数 | 收尾一次 `--no-incremental` 重建 + 逐 DLL 资源名对账（R42：增量构建会静默漏打包） |

```powershell
# 只看构建退出码不算数——核 DLL 真的带了 Help 资源（R42）
tr '\0' '\n' < Modules\Plugin.ImageAssign.dll | grep -a "\.Help\."
```
