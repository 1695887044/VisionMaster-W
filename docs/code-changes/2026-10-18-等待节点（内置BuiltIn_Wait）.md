# 2026-10-18 等待节点（内置 BuiltIn_Wait）：轮询条件 + 墙钟超时 + 超时处置

- 日期：2026-10-18（审查修复批同日并入）
- 范围：`Core\Enums.cs`（+`WaitTimeoutAction`）、`Core\Models\ProcessStep\WaitStep.cs`（新建）、`Core\Models\Compileds\CompiledWaitNode.cs`（新建）、`Core\Models\Compileds\CompiledNode.cs`（`BuildConditionArgs` 上收基类）、`CompiledIfNode.cs` / `CompiledWhileNode.cs`（改调基类）、`Engine\FlowCompiler.cs`（编译块 + `LinkPorts` 四处 + **`LinkPorts` 容器递归合并** + `TimeoutAction` 校验）、`Engine\PluginService.cs`（工具箱注册 `BuiltIn_Wait`）、`VisionMaster\Services\StepFactory.cs`、`VisionMaster\Services\StepParameterDialog.cs`、`VisionMaster\Services\FlowClipboard.cs`、`VisionMaster\ViewModels\CanvasNodeViewModel.cs`、`VisionMaster\ViewModels\DialogViewModels\ConditionEditorViewModel .cs` + `Views\DialogViews\ConditionEditorView.xaml`（等待模式 / 报警变量候选）、`VisionMaster\ViewModels\DialogViewModels\VariableBindingViewModel.cs`（等待节点端口枚举 / Guid 键）、`VisionMaster\Views\ProcessView.xaml`（等待节点模板）、检查 4 文件（`FlowCanvasChecks\WaitNodeChecks.cs`、`Program.cs` 注册与 `StepFactory` 断言、`ParallelExecutionChecks.cs` 的 P11、`VariableBindingCheck.cs` 的 `[V]` 等待端口契约）
- 类型：新功能（内置节点 + 编译期条件 + 运行期轮询 + 编辑器模式）；**附一处既有高危缺陷修复（H1，非等待节点专属）**
- 起因：用户提出「流程需要暂停等待外部条件、要轮询、要有最大超时、超时要能报警/终止/走异常分支」，逐条核对既有节点后确认**替代不了**（见第一节），因此新增一个非容器的内置等待节点

---

## 一、起因：既有节点为什么替代不了

| 需求 | 既有做法 | 为什么不行 |
| --- | --- | --- |
| 暂停流程直到某条件成立 | **延时**（固定时长） | 只能睡固定毫秒。设备到位时间不确定时，要么睡不够（下游拿到旧值），要么白等（节拍被固定时长拖死） |
| 轮询条件 | **While** 容器 | ① 循环上限 `DefaultMaxIterations = 9999` 是**硬编码**（`CompiledWhileNode.cs:28`），跑满即按"疑似死循环"抛异常；② 没有**墙钟超时**（只有圈数上限），也没有"超时分支"与"超时报警"；③ While 是容器，为"等一次"摆一个循环容器在图纸上语义不清 |
| 有超时 | **调用流程（RunFlow）** 的 `TimeoutMs` | 它等的是**子流程跑完**（`RunFlowPlugin.cs:35-36` 的端口说明就是"等待超时(ms)"），不是"某个外部条件成立"；超时也不产生可判定的输出端口 |
| 超时后终止/报警/走异常分支 | **普通算子** | 插件业务失败只把步骤标 `Failed`、**不抛异常、流程照常往下走**（`CompiledPluginNode.cs:44-51`）；插件也没有条件表达式求值能力，读不到"全局变量当前值"以外的运行时状态，更没有"墙钟计时 + 轮询"这套骨架 |
| 等一个外部信号 | **变量 + If** | If 只判一次：判完就走，等不到就立刻走 Else——那是"判断"，不是"等待" |

结论：需要一个**非容器**、带条件表达式、按轮询间隔反复求值、有墙钟超时与超时处置、并给出 `TimedOut` / `ElapsedMs` 两个输出端口的节点。

## 二、设计口径（后续改动先读这里）

| # | 决策 | 理由 |
| --- | --- | --- |
| 1 | **`WaitStep` 不实现 `IContainerStep`**（`WaitStep.cs:21`） | 等待没有"内部步骤"，只有一条布尔表达式 + 一组条件变量。做成容器会让画布/流程栏长出分支卡片，编译器还要递归子分支——全是无意义结构 |
| 2 | **条件变量沿用 If/While 的两份表**：`LocalVariables`（上游端口连线）+ `RuntimeVariableRefs`（变量定义节点写入 `context.LocalVariables`）（`WaitStep.cs:27/:33`） | 条件编辑器、`FlowCompiler`、`FlowClipboard`、`VariableBindingViewModel` 共用同一条链路，不另造一套变量体系 |
| 3 | **六个语义属性全部走 `SetProperty`**（`WaitStep.cs:36-78`） | `FlowModel.OnStepPropertyChanged` 靠**属性名**排除 `[RuntimeState]` 后 `Version++`。用自动属性不发通知 → "改了等待条件/超时，跑的还是旧编译产物"（与 `CaseStep.JudgeExpression` 同一坑） |
| 4 | **`PollIntervalMs` 默认 50，编译期下限 10**（`WaitStep.cs:43-47`、`FlowCompiler.cs:698-705`） | 更小的间隔取不到新值，只会空转 CPU。这是**配置错误**，编译期拦下而不是运行期夹取 |
| 5 | **`TimeoutMs` 默认 60000，`0 = 不限`，负数编译期报错**（`WaitStep.cs:50-54`、`FlowCompiler.cs:707-715`） | 0 是"一直等"的显式表达；负数既不是"不限"也不是合法时长。`0 < TimeoutMs < PollIntervalMs` 是**合法**的（运行期按剩余时间夹取等待），不报错 |
| 6 | **`TimeoutAction` 两个成员：`TerminateFlow`（默认）/ `Continue`**（`Core\Enums.cs:513-525`） | 终止 = 抛 `TimeoutException`，由 `FlowEngineService` 把会话置 `Faulted`；继续 = 返回 `null`，后续用 `If` 判 `TimedOut` 走异常分支。候选一律走 `StepConfigOption.ForEnum`，界面显示 `[Display(Name)]` 中文、写进流程的仍是成员名（R40） |
| 7 | **`TimeoutAction` 必须过 `Enum.IsDefined`**（编译器 `FlowCompiler.cs:717-727`、编辑器 `ConditionEditorViewModel .cs:711-721`） | 枚举按整数落盘，手改 `.vms` 写成 `99` 时运行期"不是 Continue 就终止"会**静默当成终止流程**——看起来能跑，实际配置坏了没人知道。`Enum.TryParse` 会把数字串也认成成功，所以必须再判 `IsDefined` |
| 8 | **"异常分支"用组合实现，不新增分支类型** | `等待 → If(TimedOut) → 处理 → Return / 继续`。等待节点自己保持"一条表达式"的简单形状；分支语义交给现成的 If，不重复实现一套分支容器 |
| 9 | **输出端口 `TimedOut`（bool）/ `ElapsedMs`（int）**（`CompiledWaitNode.cs:57/:60`） | `TimedOut` 是"超时"的唯一判据（取消不写它）；`ElapsedMs` 两条出口都给值，便于把等待耗时接到日志/上报 |
| 10 | **条件求值链路与 If/While 同构，实参构建上收基类**（`CompiledNode.cs:141`） | 类型归一（`CoerceConditionArg`）与无值兜底（`DefaultForConditionArg`）只能有一处实现；各写一份的下场是"某个节点忘了归一 → 只在它身上炸" |
| 11 | **编译分派必须排在"反射实例化视觉算子"兜底之前**（`FlowCompiler.cs:635-640`） | 否则会去 `Type.GetType("BuiltIn_Wait")` 失败，报"找不到插件" |
| 12 | **`StepFactory` 必须先认 `ModuleTypeName`，再走 `!IsContainer` 兜底**（`StepFactory.cs:32-43`） | 等待是"非容器但不是算子"：落进 `ActionStep` 会静默变成反射加载的普通步骤，编译期才以"找不到插件 BuiltIn_Wait"的面目爆出来 |
| 13 | **容器递归一律认 `IContainerStep`**（`FlowCompiler.cs:1354-1368`） | 与 `FlowTopology` / `FlowModel` / `StepFactory` 同一口径。按具体类型逐个 `is` 判定的写法漏一个容器就是"整层连线静默消失"（H1，见第四节） |

## 三、运行时语义（`CompiledWaitNode.RunAndGetNext`，`CompiledWaitNode.cs:62-141`）

1. **进入即置 Running，并先把两个输出端口清零**（`:66-71`）：`TimedOutPort.Value = false; ElapsedMsPort.Value = 0`。同一个编译节点在连续运行里被反复复用，不清零的话本轮"还没跑完"就会被下游读到上一轮的 `TimedOut` / 耗时（假告警、假分支）。
2. **每圈先查取消，再求值**（`:79-88`）：`context.CancellationToken.IsCancellationRequested` 一旦为真 → **`Blueprint?.EndTiming()`** → 标 `Skipped` 并返回（**不写 `TimedOut`、不抛**——用户主动停的不是超时）。`UpdateStepRuntimeState(Skipped)` 只释放焦点、不结束计时，漏了 `EndTiming` 的表现是 `LastRunTimeMs` 停在 0、画布上看起来"这一步没花时间"。
3. **求值后再查一次取消**（`:107-114`）：求值期间停止请求到达时，这一次结果不许再被当成 `Success` / 超时（否则"点了停止却报成功"或"报了超时告警"）。同样是 `EndTiming` + `Skipped`。
4. **求值抛异常 → Error 日志 + 标 `Failed` + 上抛**（`:90-105`）：`"等不到"与"算不出来"是两件事`，静默当成 `false` 会一直等到超时，归因全错。
5. **先求值、后判超时**（`:116-128`）：条件成立 → `TimedOut=false`、写 `ElapsedMs`、标 `Success`、继续下一步。条件恰好在这一刻成立时不会被误判成超时（与 While"条件求值放在上限判断之前"同一条纪律；断言 E24-2 用 `poll=200ms` + "总耗时 < 150ms"钉住）。
6. **超时判定用墙钟**：`TimeoutMs > 0 && elapsedMs >= TimeoutMs`。
7. **每轮只睡"到下次该求值的时刻"，且不超过剩余超时**：`waitMs = min(PollIntervalMs, TimeoutMs - elapsedMs)`，再 `WaitOne(max(1, waitMs))`（`:130-139`）。两个作用：剩余不足一个轮询周期时也准时醒来判超时；`CancellationToken.WaitHandle` 被置位时立即返回，**停止响应不受轮询间隔影响**。
8. **超时收尾顺序是刻意的**（`HandleTimeout`，`:148-196`）：日志 → 报警变量 → 两个输出端口 → 步骤状态，最后才按配置终止或继续。即使下一步要抛异常终止流程，日志/端口也已经落定（抛异常后流程上下文不再可用）。
   - 日志：`[等待超时] 等待节点 '{Name}' 等待条件 [{ConditionExpression}] 超时（{TimeoutMs}ms）：{TimeoutMessage}`（`TimeoutMessage` 为空时冒号整段不出现，`:150-158`）。**分隔符是必须的**：直接粘会读成"超时（5000ms）等待料到位信号"。
   - 报警变量：`context.GlobalVariables.TryWrite(name, true, out error)`；失败只记 `Warn`（`写报警变量 '{name}' 失败：{error}`），**不改变超时处置**——一次配置疏漏不该把"终止流程"变成"静默继续"。
   - `TerminateFlow`：抛 `TimeoutException`，文案含条件表达式、超时值与 `TimeoutMessage`，末尾"已按配置终止流程。"
   - `Continue`：返回 `null`，流程继续。
9. **步骤状态一律 `Failed`**（`:186`）：超时是事实，不能因为"配置成继续"就把这一步染绿（否则画面上一片绿，现场看不出哪一步等超了）。走异常分支由 `TimedOut` 端口表达。
10. **`ElapsedMs` 超过 `int.MaxValue` 时夹取**（`:199-200`），避免负值；正常工况到不了。

## 四、审查修复（2026-10-18 复核后并入）

### 4.1 H1：`LinkPorts` 漏递归 `ParallelStep`（既有高危，**顺带修复**）

**不是等待节点专属，是既有缺陷被等待节点踩中。** 旧实现的"递归子分支连线"只认两支：`ConditionStep`（含 While/Case）与 `ForStep`；而 `ParallelStep : StepModel, IContainerStep` 两支都不命中——**并行分支里的所有连线、必填参数检查整层被静默丢弃**：

- 分支内算子的输入端口全部没有 `LinkedSource`（运行期取默认值/空值）；
- 分支内等待节点的 `UpstreamLinks` 为空 → 条件恒取默认 `false` → **一直等到超时**（等待节点踩中的正是这条）；
- 连"必填参数未配置"这类编译期检查也一并跳过（分支里配错参数编译照样过）。

**修法**：合并成一条 `if (model is IContainerStep container && container.Children != null)` 统一递归（`FlowCompiler.cs:1354-1368`），与 `FlowTopology` / `FlowModel` / `StepFactory` 同一口径。**不按具体类型逐个 `is` 判定**——漏一个容器就是同一类静默故障。

**守门**：
- `ParallelExecutionChecks` 的 **P11**（`:681-719`）判据强化：父域预置 `X=3.0`、分支内 If 条件 `X == 3`、期望 `Runs==1`。旧版用 `X > 5` 时"连线被丢（默认 0）"与"影子种子没生效"**都是 0，是假绿**；改成 `== 3` 后只有连线真的被编译进来才会成立。
- `WaitNodeChecks` 的 **E24-6c**（`:376-415`）：等待节点放进并行分支，全局变量连线必须生效（100ms 后成立 → `Success`，不是等到超时 `Failed`）。

### 4.2 L1 / M5：跨轮端口残留与取消计时

- **L1**：入口清零两个输出端口（`CompiledWaitNode.cs:68-71`）——上一版记录里"跨轮残留"那条限制**已作废**（当时 `TimedOut` / `ElapsedMs` 进节点时不清零）。
- **M5**：两条取消出口补 `Blueprint?.EndTiming()`（`:85`、`:111`），取消后 `LastRunTimeMs` 不再停在 0。
- 守门：E24-7 的"停止请求也会冻结耗时（`LastRunTimeMs > 0`）"、E24-7b 的"第二轮入口清零（`TimedOut=false / ElapsedMs=0`）"。

### 4.3 M1：画布绑定窗口能看到等待节点的条件变量

`VariableBindingViewModel` 在**非单绑模式**下，`_targetStep is WaitStep` 时用 `LocalVariables` 生成输入端口：`Name = 别名`、`DataTypeName = 声明类型`、`Description = 变量 Guid`（`VariableBindingViewModel.cs:524-548`）。`ResolveBindKey`（`:264-269`）与 `DoFinalBind`（`:452-470`）对 `ConditionStep or WaitStep` 一律用 `Description` 作键——与 `FlowCompiler` 消费方分支的 Guid 键同一口径。

**边界**：等待节点没有插件端口表，**条件变量表为空时绑定窗口左侧就是空的**——要先在「等待条件配置」里新增变量，画布拖线才有端口可绑。

守门：`VariableBindingCheck` 的 `RunWaitStepPortsContract`（`:122-178`，3 条）：列出 2 个端口 / 端口名=别名且 `Description`=变量 Guid / 双击绑定写到 Guid 键（不是别名）。

### 4.4 M2：报警变量改为"可编辑下拉 + 保存校验"

- 编辑器新增 `AlarmVariableCandidates`（`ConditionEditorViewModel .cs:130-136`），打开等待节点时现取：只列**工作区里 `VariableType != System` 且 `DataType == typeof(bool)`** 的用户变量（`:316-332`）——系统变量是运行态派生只读量，列出来只会让人选了保存失败。
- XAML 由裸文本框改为**可编辑 `ComboBox`**（`ConditionEditorView.xaml:515-527`）：保留"变量还没建、先把名字写上"的自由输入。
- `SaveWait` 非空时校验"名字在候选里"（`ConditionEditorViewModel .cs:723-732`），否则停在弹窗报 `报警变量「{name}」不存在或不是全局 bool 变量（请先在变量管理里创建）`。
- 运行期口径不变：写失败只记 `Warn`，不改变超时处置。

## 五、改动清单

| 文件 | 位置 | 改动 |
| --- | --- | --- |
| `Core\Enums.cs` | `:513-525` | 新增 `WaitTimeoutAction`（`TerminateFlow = 0` / `Continue = 1`），成员带 `[Display(Name)]` 中文；注释写明"新增成员只许追加在末尾"（枚举整数值可能已落进 `.vms`） |
| `Core\Models\ProcessStep\WaitStep.cs` | 全文（`:21`） | 新建非容器步骤模型：`LocalVariables` / `RuntimeVariableRefs` / `ConditionExpression` / `PollIntervalMs=50` / `TimeoutMs=60000` / `TimeoutAction=TerminateFlow` / `TimeoutMessage` / `AlarmVariableName`；类注释写明"为什么不是容器""为什么必须走 SetProperty" |
| `Core\Models\Compileds\CompiledWaitNode.cs` | 全文（`:27`） | 新建编译节点：`Condition`、`UpstreamLinks`、六个语义字段、`ConditionExpression`（日志用原文）、两个输出端口；**入口清零**（`:68-71`）、**两处取消 `EndTiming`**（`:85`/`:111`）、**求值后二次查取消**（`:107-114`）、超时日志分隔符（`:150-158`）、`RunAndGetNext` / `HandleTimeout` |
| `Core\Models\Compileds\CompiledNode.cs` | `:141-179` | `BuildConditionArgs` 上收基类（连同 `:75` 的 `CoerceConditionArg`、`:112` 的 `DefaultForConditionArg`）——If / While / 等待三个节点求值的是同一种 `CompiledBranch`，参数布局与类型归一只能有一处事实来源 |
| `Core\Models\Compileds\CompiledIfNode.cs` | `:56-58` | 改调 `BuildConditionArgs(branch, UpstreamLinks, context)`，删除本地那份实参构建 |
| `Core\Models\Compileds\CompiledWhileNode.cs` | `:93-95` | 同上；实参因此**每圈新建一次数组**（语义等价，去掉了一处"跨圈复用同一数组"的微优化） |
| `Engine\FlowCompiler.cs` | `:635-729` | 场景 A-2b 编译块：建 `CompiledWaitNode` → 编译条件 → 抄五个字段 → 四条硬错（空条件 `:669`、轮询过小 `:699`、超时为负 `:709`、**超时处置未定义 `:720`**） |
| `Engine\FlowCompiler.cs` | `:1008-1016` | `LinkPorts` 运行时变量分支：目标节点白名单加 `CompiledWaitNode`；等待节点的条件变量按**自己的** `LocalVariables` 声明类型解析 `expectedType`（漏这一支 → 代理端口按 `object` 兜底，运行期归一类型与声明不符） |
| `Engine\FlowCompiler.cs` | `:1128-1154` | `LinkPorts` 生产方分支：`TimedOut` → `TimedOutPort`、`ElapsedMs` → `ElapsedMsPort`，并 `AddDependency`（试运行先跑等待节点，否则下游拿到默认值 false/0）；其它端口名显式报错 |
| `Engine\FlowCompiler.cs` | `:1265-1290` | `LinkPorts` 消费方分支：条件变量连线按 Guid 键挂进 `waitNode.UpstreamLinks`，走 `TryValidateLink` 类型校验；键转不成 Guid → 显式报错 |
| `Engine\FlowCompiler.cs` | `:1354-1368` | **H1 修复**：`LinkPorts` 容器递归合并为 `if (model is IContainerStep container && container.Children != null)`（旧实现只认 `ConditionStep`/`ForStep`，漏 `ParallelStep`） |
| `Engine\FlowCompiler.cs` | `:1491-1523` | `ResolveConditionVarType` 增加 `WaitStep` 分支（它不是 `ConditionStep`，变量表在自己的 `LocalVariables` 上） |
| `Engine\PluginService.cs` | `:260-286` | 工具箱注册：`逻辑控制 / 等待`，`Icon="\uf252"`，`IsContainer=false`，`OutputDefinitions` = `TimedOut`（`System.Boolean`）+ `ElapsedMs`（`System.Int32`） |
| `VisionMaster\Services\StepFactory.cs` | `:32-37` | `BuiltIn_Wait → WaitStep`，**放在 `!IsContainer` 兜底之前** |
| `VisionMaster\Services\StepParameterDialog.cs` | `:88-98` | 条件编辑器出口白名单加 `WaitStep`（不是 `ConditionStep`，必须显式列出） |
| `VisionMaster\Services\FlowClipboard.cs` | `:177-184` | 粘贴时的变量键重映射增加 `WaitStep` 分支（与 `ConditionStep` 同一条 `RemapLocalVariableKeys`）；漏了会沿用源步骤的变量 Guid → 串键 / 条件恒取默认值 |
| `VisionMaster\ViewModels\CanvasNodeViewModel.cs` | `:166` | 画布类型徽标 `WaitStep => "等待"` |
| `VisionMaster\ViewModels\DialogViewModels\ConditionEditorViewModel .cs` | `:67-136` / `:228-232` / `:276-336` / `:505-509` / `:678-757` | 等待模式：`IsWaitMode`、五个参数草稿 + 表达式草稿、`TimeoutActionOptions`（`ForEnum`）、**`AlarmVariableCandidates`**、`WaitHintText`、标题"等待条件配置"；`SaveWait` 校验（轮询 ≥10、超时 ≥0、**枚举 `IsDefined`**、**报警变量存在且为全局 bool**）后一次性写回 + `Version++` 兜底 |
| `VisionMaster\Views\DialogViews\ConditionEditorView.xaml` | `:429-535` | 参数区（`IsWaitMode` 控制显隐，`MaxHeight=132` + 竖向滚动）：轮询间隔 / 最大超时(0=不限) / 超时处理（`DisplayMemberPath=Display` + `SelectedValuePath=Value`）/ 超时说明 / **报警变量名（可编辑 `ComboBox`，候选=`AlarmVariableCandidates`）** + 提示行 |
| `VisionMaster\ViewModels\DialogViewModels\VariableBindingViewModel.cs` | `:264-269` / `:452-470` / `:524-548` | **M1**：`WaitStep` 的条件变量作为绑定窗口的输入端口（`Name`=别名、`Description`=变量 Guid）；`ResolveBindKey` / `DoFinalBind` 对 `ConditionStep or WaitStep` 用 `Description` 作键 |
| `VisionMaster\Views\ProcessView.xaml` | `:563-716` | 等待节点模板：**普通 `DataTemplate`**（非容器，不许 `HierarchicalDataTemplate` / `ItemsSource`），状态点 + 主色图标块 + "等待"chip + 实例名 + 条件小字；`Failed` 时名字与条件一起转红（R23 守门） |
| `FlowCanvasChecks\WaitNodeChecks.cs` | 全文（`:32`） | `[E24]` 段 **51 条**断言（编译门禁 / 先求值后等待 / 轮询 / 求值异常 / 两种超时处置 / timeout<poll / 两个端口 / 并行分支 H1 回归 / 停止 / 跨轮清零 / 报警变量 / `.vms` 往返） |
| `FlowCanvasChecks\Program.cs` | `:174` / `:1987-2001` | 注册 `WaitNodeChecks.Run()`；`X4 StepFactory` 段补 `BuiltIn_Wait（非容器）→ WaitStep` 1 条 |
| `FlowCanvasChecks\ParallelExecutionChecks.cs` | `:681-719` | **P11 判据强化**（H1 守门）：父域预置 `X=3.0`、条件 `X == 3`、期望 `Runs==1`；旧版 `X > 5` 在"连线被丢"与"影子失效"两种情况下都是 0（假绿） |
| `FlowCanvasChecks\VariableBindingCheck.cs` | `:106` / `:122-178` | 新增 `RunWaitStepPortsContract`（`[V]` 3 条）：端口列表 / 别名与 Guid 键口径 / 双击绑定写到 Guid 键 |

## 六、验证证据（本机实跑）

- **构建**：`dotnet build FlowCanvasChecks\FlowCanvasChecks.csproj` → **0 错误**（含 `VM.Core` / `VM.FlowEngine` / `VisionMaster` 依赖链；1 条既有 `MSB3245`：`DLL\HslCommunication` 解析不到 `System.Resources.Extensions`，非本批引入）。
- **断言**：`FlowCanvasChecks` → **通过 2601 / 失败 0**（末尾 `>>> 全部断言通过 <<<`）。
  - 构成：上一版 2582 + 本轮 19（`[E24]` **+16**、`[V]` **+3**）= 2601；
  - `[E24]` 由 **35 → 51**，新增 16 条的分组：求值异常 **4** / `timeout < poll` **2** / `ElapsedMs` 生产方 **2** / 并行分支 H1 回归 **2** / `.vms` 往返 **2** / 取消计时 **1** / 跨轮端口清零 **3**；
  - `[V]` 新增 3 条（等待节点端口枚举与 Guid 键）；
  - P11 判据强化（父域 `X=3`、条件 `X == 3`、期望 `Runs==1`），同时保留"分支1 的 X=7 合并回父域"对照。
  - 本次实跑的采样值（随机器/调度波动，断言用的是量级区间）：轮询命中 `ElapsedMs=181`、超时 `ElapsedMs=153`、停止 110ms 返回、`timeout<poll` 61ms 判超时、跨轮第一轮 `ElapsedMs=202` / 第二轮 `0`。
- **变异反证**（H1 / L1+M5 / 首版两条，均"改坏 → 精确变红 → 还原复绿"）：
  1. **H1**：把容器递归改回"跳过 `ParallelStep`" → **恰好 2 条红**（P11 + E24-6c 等待节点并行分支回归），其余全绿；还原后 2601/0。
  2. **L1/M5**：去掉入口清零 + 取消分支 `EndTiming` → **2 条红**（"停止请求也会冻结耗时" / "第二轮入口清零"）；还原后 2601/0。
  3. 早前两条（首版）：把"先求值后等待"改成"先睡后求值" → 立即继续断言红；把 `TimedOutPort.Value = true` 改成 `false` → 三条端口/分支断言红。
- 说明：本次补文档时重跑了构建与全量断言（2601/0）；变异结论取自对应开发批次记录。

## 七、已知边界 / 未决

1. **等待期间只响应"停止"，不响应调试"暂停"**：调试门在 `CompiledNode.RunSequence` 的**节点执行前**（`CompiledNode.cs:236-239`），而等待循环内部只查 `CancellationToken`。断点/单步命中在进入等待之前；进入之后按"继续"再点暂停，要等条件成立或超时才会停到下一个节点。
2. **报警变量**：编辑器只列**用户可写的全局 bool 变量**（系统变量被排除），保存时校验存在性与类型；运行期 `GlobalVariableWriter.TryWrite`（`Core\Binding\GlobalVariableWriter.cs:42-99`）仍按声明类型守门，写失败只落一条 `Warn`、不影响超时处置。**报警变量没有任何复位点**——第一次超时之后恒为 `true`（见后续建议 1）。
3. **报警与 SCADA 报警引擎之间没有直连**：报警引擎挂在组态运行窗上下文（`VisionMaster\Services\ScadaRuntimeHost.cs:321` 调用 `:452` 的 `StartAlarms`），流程侧没有投报警的入口；写全局变量是流程与组态之间唯一的通道，报警规则要在组态侧对该变量配置。
4. **`TimeoutMs = 0` 只能靠"停止"或"条件成立"结束**：没有墙钟上限，现场误配会让流程无限等待（这是"不限"的语义，不是缺陷）。
5. **`Continue` 下步骤仍标 `Failed`**（有意）：画面/日志上看得见"这一步超时了"；"流程是否继续"由 `TimeoutAction` 决定，"这一步是否成功"由超时事实决定，两者不混。
6. **画布绑定的前提是"条件变量已存在"**：等待节点没有插件端口表，`LocalVariables` 为空时绑定窗口左侧就是空的——必须先在「等待条件配置」里新增变量，画布拖线才有端口可绑。
7. **GUI 实机冒烟未做**：工具箱拖入"等待"、双击打开「等待条件配置」、报警变量下拉的可编辑行为、流程栏红色状态与条件小字，需现场验收；断言只覆盖数据层与编译/执行层。
8. **`DebugChecks` 单步的两条断言时序敏感**（与本批无关，既存现状）：全量 gate 偶发受调度影响，重跑即可复现绿；本批未改调试链路。

## 八、后续建议（未做）

1. **报警变量复位策略**：当前只在超时时写 `true`，没有任何地方复位。建议现场用"边沿触发"（等待节点每次进入时先写 `false`，超时再写 `true`），或由组态侧确认后复位——否则第一次超时之后报警变量恒为 `true`。
2. **等待中可暂停**：若调试体验需要"停在等待里"，要把等待循环的 `WaitOne` 换成"可被 PauseLock 与停止令牌同时唤醒"的等待，并在循环内查调试门；这会打破"调试门只在 `RunSequence` 一处"的纪律，需要先设计再动。
3. **快捷插入面板在等待模式下的空间取舍**：等待参数区已占 `Grid.Row=1`（`MaxHeight=132`），表达式编辑器与快捷插入面板在矮屏上会被压缩；若要继续加参数（如"条件成立后再稳定 N ms"），应改成可折叠分组而不是继续加行。
