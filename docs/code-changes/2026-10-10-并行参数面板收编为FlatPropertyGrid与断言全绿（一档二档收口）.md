# 2026-10-10 并行参数面板收编为 FlatPropertyGrid 与断言全绿（一档二档收口）

- 日期：2026-10-10
- 项目：VisionMaster（Models\ParallelGroupEditModel.cs 新增、Services\StepParameterDialog.cs、ViewModels\ProcessViewModel.cs、Views\ProcessView.xaml、ShellViewModel.cs、App.xaml.cs、Core\Models\ProcessStep\ParallelStep.cs；删 Views\DialogViews\ParallelGroupConfigView.xaml(.cs)、ViewModels\DialogViewModels\ParallelGroupConfigViewModel.cs）+ FlowCanvasChecks（ParallelGroupConfigChecks.cs 重写、ProcessTreeInteractionChecks.cs V1/V6 改判、ParallelExecutionChecks.cs P23/P28、ParallelContainerChecks.cs V5 扩充、ExecutionChecks.cs / MotionZMotionChecks.cs / YoloChecks.cs 判据改口径、RenderProbe.cs 场景 G 改渲染对象）
- 类型：交互重构（专属视图面板 → 标准属性面板，用户裁决）+ 断言收口（4 条既有环境失败改判/加前置 → gate 首次全绿）+ 二档挂账补齐（P23/P28/ZIndex/畸形容器）
- 起因：用户看到并行分组参数面板（前一天落地的专属视图）后裁决——「**既然右键可以重命名，模块参数就不需要重命名了，这个属性没必要新建一个视图，考虑下 `UI\Controls\CustomControl\PropertyGrid\FlatPropertyGrid.cs` 控件**」，并要求「**一档二档全部开始做**」（一档 = 真机验收项收口、二档 = 挂账收尾，主会话前一轮列的计划）
- 上承：`docs\code-changes\2026-10-09-并行分组参数面板（草稿事务-白名单分派-分支卡片交互闭环）.md`（专属视图首版，本轮被取代）、`docs\code-changes\2026-10-09-并行分组分支结构动作下沉右键菜单（面板收窄为参数表单）.md`（右键命令族，本轮不变）、`docs\方案设计\parallel-execution-phase2-design.md` §8（P23/P28 断言契约）
- 验证口径：**构建** = `VisionMaster.csproj` / `FlowCanvasChecks.csproj` 均 0 错误；**断言** = `cd FlowCanvasChecks && dotnet run -c Debug` → **通过 1819 / 失败 0**（首轮全绿为 1810/0，此前基线 1820/4——4 条既有环境失败本轮收口，此后随新增断言涨到 1819/0）；**出图** = `--render` 全 8 场景 + 自检行；**真机** = VisionMaster 已用新产物重启（pid 31268）
- 事实来源：本轮实现与验证交接记录；落盘前对关键代码逐处回读核对（`ParallelGroupConfigChecks.cs` 的 `Check(` 调用点静态清点为 23 处，交接口径 22 条，本记录按实测 23 记）

---

## 一、A 段：参数面板收编为标准属性面板（替代专属视图）

### 1.1 裁决与总纲

用户裁决链：「右键已有重命名 → 参数面板里不需要再放分组名 → 三个参数（执行模式/失败聚合/汇合超时）+ 说明文案用现成的 FlatPropertyGrid 控件就能装 → **没必要新建一个视图**」。执行总纲 = **删专属视图、建编辑草稿、FlatPropertyGrid 反射渲染、分组名归右键**。

### 1.2 删除（用户裁决"没必要新建一个视图"）

| 文件 | 说明 |
| --- | --- |
| `VisionMaster\Views\DialogViews\ParallelGroupConfigView.xaml(.cs)` | 专属弹窗视图两文件删除 |
| `VisionMaster\ViewModels\DialogViewModels\ParallelGroupConfigViewModel.cs` | 专属 VM 删除（其草稿事务/校验手法由 EditModel 继承） |
| `App.xaml.cs` 的 `RegisterDialog<…>("ParallelGroupConfig")` 注册行 | 删除（现仅存注释说明退役原因，`App.xaml.cs:280`） |

### 1.3 新增 `VisionMaster\Models\ParallelGroupEditModel.cs`（编辑草稿类）

照 `MotionCardIdentityEdit` 的手法（构造时从活模型拷值、确认才写回）：

- **三个可编辑属性全贴 `[SuperDisplay]`**（FlatPropertyGrid 只渲染带该特性的属性，`PropertyGridDefaults.GetVisibleProperties` 是契约入口）：
  - `ExecutionMode`（枚举下拉）、`FailFastMode`（枚举下拉）、`JoinTimeoutMs`（贴 `[RangeValidation(0, 600000)]` 行内校验）；
- **四条只读说明属性**（同样贴 `[SuperDisplay]`，渲染进说明区）：右键入口指引（`GroupScopeText`——分组名/分支增删改都在流程栏右键）、调试退化提示（`DebugHintText`）、ForceSequential 总闸警示（`ForceSequentialHintText`，开 = 有内容、关 = 空串）、FailFast「继承全局（当前＝开/关）」动态值（`FailFastInheritHintText`）；
- **动态值放说明区而不是枚举 Description**：枚举描述是静态文案（编译期定死），「当前＝开/关」要实时读 `GlobalParallelConfig.FailFastByDefault`——放说明属性才能跟随全局配置变化（W5 断言钉住这个跟随关系）。`ParallelStep.cs:35` 的注释同口径说明；
- `ApplyToModel()` **只写不同值**（三值逐一与活模型比对，值没变不碰 setter → 版本号不白推，W2 断言），全走语义属性 setter。

### 1.4 `Core\Models\ProcessStep\ParallelStep.cs`：枚举补中文 Description

两个枚举 5 个成员补 `[Description]`（`ParallelStep.cs:19/23/38/42/46`）：执行模式「顺序执行（逐分支跑完再汇合）/ 真并发（分支各自执行，按失败规则汇合）」、失败聚合「继承全局（跟随软件级设置实时变化）/ 强制开：任一分支业务失败即取消其余分支 / 强制关：各跑各的，不取消兄弟」。EnumGenerator 读它做下拉显示，没贴就上屏英文枚举名。

### 1.5 打开链路（StepParameterDialog 第三条出口改道）

- `StepParameterDialog.Open` 加可选参数 `IWorkspaceManager workspace`（`StepParameterDialog.cs:53`）；
- `ParallelStep` 分支改为 `EasyDialog.ShowPropertyGridSync("并行分组参数", new ParallelGroupEditModel(par, …))`（`StepParameterDialog.cs:110`）——**确认（返回 true）才 ApplyToModel + `Version++` 兜底，取消 = 草稿丢弃**（什么都不写、版本号不动）。`Version++` 是兜底保险：三个语义属性走 setter 本来就各自触发一次版本链，这里多推一次只是让"打开又直接确认"必然重编译；
- **`Application.Current` 前置判空**（`StepParameterDialog.cs:105-109`）：headless 断言宿主按取消处理。坑（hmi 实现时首跑抓到的）：EasyDialog 内部的 `InternalExecuteAsync` 有"Application.Current == null → 返回 false"保护分支，**但它的前置步骤（Dispatcher.InvokeAsync 建 FlatPropertyGrid）没有**——不前置判空直接 NRE。分派本身仍返回 true（该靶有参数面板这一事实不因宿主形态而变）；
- 弹窗壳走 EasyDialog 静态弹窗、不经 IDialogService——`RecordingDialogService` 那类形状桩不会记录到它（断言口径改判见 §1.8 V1）；
- 命中窗入口同步传 Workspace（`ShellViewModel.cs:1350-1356`，实际路径 `VisionMaster\ShellViewModel.cs`：「打开模块参数」复用流程栏同一帮助类，workspace 传下去用于并行分组保存后推进 Version，口径只有一份）。

### 1.6 分组名改右键重命名

- `ProcessViewModel` 新增 `RenameParallelGroupCommand`（`DelegateCommand<ParallelStep?>`，`ProcessViewModel.cs:65`）+ 可注入 `ConfirmRenameParallelGroup`（`:453`，默认 `EasyDialog.ShowTextInputSync`）——三态：取消不动 / 空白拒（提示「分组名不能为空」）/ 确认写回（两端空白裁掉），名字没变不白推版本；
- `ProcessView.xaml` 右键菜单（父级 TreeView 菜单）加「重命名分组」（`:710-712`，`Visibility` 绑 `IsParallelNodeSelected`，与既有「添加分支」同可见性判据）；
- **分组名走 `StepModel.StepName` 的 setter，版本号自动推**——与分支重命名不同：分支名是 `StepCollection.StepName`，不在 FlowModel 监听面里、命令手动推 `Version++`。**两条口径在 W7 断言里对照着守**（分组 setter 自动推 / 分支直接赋值不推、命令路径才推），防后人搞混。

### 1.7 断言迁移（ParallelGroupConfigChecks.cs 重写）

W1-W7 全走 EditModel/命令级（headless 不开真窗），共 **23 条** `Check`（按落盘时静态清点）：

| 段 | 断言要点 |
| --- | --- |
| W1（3 条） | 草稿按活模型铺开（默认值/非默认值原样拷贝）/ 编辑只落草稿、活模型零污染 |
| W2（4 条） | ApplyToModel 三值写回 + 版本链自动推进 / 二次写回 / 三档逐一可写 / 值没变零写回 |
| W3（1 条） | 取消（不 ApplyToModel）→ 活模型零改动、版本号不变 |
| W4（4 条） | 汇合超时 0 原样写回（0=未配置不许夹取）/ 合法值 / 越界值原样写回不夹取（引擎回落归 P32）/ `[RangeValidation(0,600000)]` 特性贴对（含"0 = 使用默认"文案） |
| W5（5 条） | 草稿 7 个公开属性全贴 `[SuperDisplay]`（FlatPropertyGrid 一行不缺）/ 枚举 [Description] 齐备 / 「继承全局」当前值跟随全局配置 / ForceSequential 开关提示 / 说明区右键入口指引文案 |
| W6（5 条） | 分组改名命令三态（取消/空白拒/确认裁空白）+ 名字没变不白推 + 靶 null 空跑不抛 |
| W7（1 条） | 版本号口径对照：分组名 setter 自动推 vs 分支名靠命令手动推（两条口径并存互证） |

旧 W 段（面板草稿/分支只读总览，30 条）随视图删除退役；分支结构断言留在 `ProcessTreeInteractionChecks` V8 不动（那边管分支增删改，这边管组头重命名，互不重叠）。

### 1.8 相邻断言改判

- **V1 改判**（`ProcessTreeInteractionChecks.cs:47-60`）：并行分组打开模块参数 → **返回 true 且不再经 Prism 弹窗**——headless 下 EasyDialog 走取消保护分支不炸，`RecordingDialogService` 记录的弹窗数 = 0（EasyDialog 静态弹窗与 Prism 弹窗服务是两条通道）；
- **V6 改判**（`:107-125`）：旧视图退役契约——「ParallelGroupConfig」分派字面量清零（全仓 `ParallelGroupConfig` 命中仅剩 2 处注释性提及：`App.xaml.cs:280` 退役说明、`StepParameterDialog.cs:33-34` 口径注释，均非代码）、App 注册行已删（注释里提及不算注册）、磁盘上三份旧文件（View.xaml / View.xaml.cs / ViewModel.cs）必须已删除；
- **渲染探针场景 G 改渲染对象**（`RenderProbe.cs:621-651`）：改为渲染 `FlatPropertyGrid` 本体，输出 `parallel-group-params-render-G.png`——弹窗壳是 Popup（独立窗口），headless 出不了图，只能直出控件本体。

### 1.9 出图复核

`parallel-group-params-render-G.png` 为 FlatPropertyGrid 本体出图：四分组 Tab（执行 / 失败聚合 / 汇合 / 说明）、枚举下拉显示中文 Description、说明区带右键指引与 ForceSequential 警示。主会话读图复核通过。

## 二、B 段：4 条既有环境失败收口 → gate 首次全绿

原则先行：**红 = 真问题**。此前 4 条"既有环境失败"长期挂在基线里（相机注册表 / ONNX 模型文件 / SCADA 候选生成器×2），每天跑 gate 都要人肉认一遍"这 4 条不用管"——信号被稀释。本轮逐条核对代码现状后收口。

| # | 断言 | 改前判据 | 改后判据 / 处置 | 依据 |
| --- | --- | --- | --- | --- |
| 1 | 相机断言（`ExecutionChecks.cs:980`） | RegisterCamera 转交模块表 | **写进相机表且不进模块表**（`CameraPlugins.Count` 与 `ModulePlugins.Count` 对照） | `PluginProvider.RegisterCamera` 的现行口径就是"驱动不是流程步骤，必须有自己的表"——写模块表反而会让相机设置里查不到驱动。旧断言是过时口径 |
| 2 | Z 断言①（`MotionZMotionChecks.cs:126-128`） | 两个属性面板都注册动态候选生成器（Card/Flat 各自挂） | **扫 `PropertyGridDefaults`**（生成器清单已抽到公共类 `UI\Controls\CustomControl\PropertyGrid\Core\PropertyGridDefaults.cs`，Card/Flat 继承基类管线——结构上不可能漏一边） | 清单公共化后"逐面板检查"断言已失效（要漏一起漏，逐面板查无意义） |
| 3 | Z 断言②（`MotionZMotionChecks.cs:189-194`） | 卡候选值仍是地址（c.Address） | **按卡名寻址**：`MotionCardName` 分支 + 同名卡只列一项 + `g.Key.Trim()` | 寻址口径已从 IP/地址迁移到卡名，卡名才是稳定键（设备名/IP 会变，卡名是配置锚点） |
| 4 | Yolo 类别过滤断言（`YoloChecks.cs:185-191`） | 找不到模型直接红 | **加环境前置**：`ResolveTestModel()` 找不到测试模型（`YOLO_TEST_MODEL` 环境变量 / `%TEMP%\dlprobe\out\*.onnx` 都缺席，仓库不含模型文件）时**跳过并注明**（"跳过：未找到测试模型"），而不是红 | 本机无模型 ≠ 代码有病；红必须是真问题 |

- 结果：gate 从 **1820/4 → 1810/0（首轮全绿）**，此后随新增断言涨到最终 **1819/0**。
- 说明：4 条改判全部是"判据对齐代码现状"，不是放宽——相机表断言比旧的更严（同时验"进了相机表"与"不进模块表"两个方向）；Z 两条是结构升级后的正确锚点；Yolo 是环境前置不是跳过检查（有模型时断言照常跑）。

## 三、C 段：二档挂账补齐

### 3.1 P23（RunFlow 并行门禁，`ParallelExecutionChecks.cs:988-1036`）

RunFlowPlugin 在并行分支内的行为断言，2 条：

- **门禁失败不抛异常**（`:1031`）：并行分支里 RunFlow 是"一支 Success=false"的普通业务失败——抛了会被 CAS 故障源登记顶掉真正的语义；
- **`Invoked=false` + `Message` 如实带回门禁原因**（`:1033-1036`）：实测走到「未开放子程序调用」真门禁分支（门禁四种拦法——未开放/禁用/正在运行/不存在——殊途同归，都走 `FlowInvokeResult.Fail`；RunFlowPlugin 不做二次判定，原样转成业务失败）。

- 插件按 Modules 反射加载纪律：`Assembly.LoadFrom(Plugin.RunFlow.dll)`（`Modules\` 只放 Plugin*.dll，断言宿主不引用插件工程）。
- 门禁四判据本身已由 FlowAutomationChecks [E16] 覆盖，不重复。
- 实现期两个坑（如实记录）：① `WorkspaceContext.CurrentSolution` 初始为 null，要先 `SwitchSolution`；② 端口手动值要通过 `IInputPort.Value`（`InputPort<T>.Value`）设置，不走别的成员。

### 3.2 P28（取消分支影子丢弃独立用例，`ParallelExecutionChecks.cs:528-565`）

3 条断言：分支1 业务失败（FailFast=On）→ 分支2 先写 `P28X=888` 再死等（闸门桩）→ 取消后：

- **父域 P28X 保持快照 1**（`:561-562`，被取消分支的写入不可见——§3.5 合并表"Cancelled 分支影子整体丢弃"的执行证明）；
- **丢弃 Warn**（`:563-564`）：实测文案「分支 2 被取消，其运行时变量写入已整体丢弃（P28X）」（生产代码 `CompiledParallelNode.cs:560-562`）；
- 图纸编译成功前置（`:551`）。

坑（如实记录）：`VarWritePlugin` 的 VarName/Value 是**共享静态**——P28 用独立变量名 P28X 隔离；P8 用例开头补设自己的期望值（首跑抓到 P8 被 888 污染）。

### 3.3 ZIndex 遮挡方向守闸（`ParallelContainerChecks.cs:235-285`，[V5] 扩充）

2 条断言，**位图级**证明 items 宿主真的吃 Panel.ZIndex 且方向正确：两个全重叠 Border 先蓝后红——不给 ZIndex 采样 = 红（后加者在上，`ParallelContainerChecks.cs:261`）→ 给蓝 `ZIndex=10` 采样 = 蓝（`:278`）。此前只有"Panel.ZIndex 与 VM.ZOrder 绑定一致"自检（`:754-767`）——若宿主换成不吃 ZIndex 的容器，绑定照样生效、自检照样绿，遮挡却退回集合序，静默退化。

坑（如实记录）：① 断言宿主里**不能 new 第二个 Application**（会卡死线程——首跑挂起、进程杀掉后改掉；`RenderTargetBitmap` 渲染纯视觉树不需要 Application，`:246`）；② `RenderTargetBitmap.CopyPixels` 的 stride/buffer 必须按整幅（60×60×4 = 14400 字节，`:271`），传 4 会抛。

### 3.4 畸形容器断言（`ParallelContainerChecks.cs:295-325`，[V5] 扩充）

2 条：`Children` 为空列表 / 为 null 的并行分组参与画布「整理」**不抛异常**（`:319`）；空分支容器渲染出 230×64 折叠框（`:323`，兜底路径有产物，不是崩或零尺寸）。

### 3.5 最终 gate

**1820 通过 / 0 失败**——断言基线**首次全绿**：1810（B 段收口后的首轮全绿基线）+ P23×2 + P28×3 + ZIndex×2 + 畸形×2 + 类型容错×1（见 3.6）。

### 3.6 真机崩溃修复：结构命令改 `DelegateCommand<object?>` + `is` 判型（2026-10-10 真机反馈）

**现象**：真机 `System.InvalidCastException: Unable to cast object of type 'VisionMaster.Models.ActionStep' to type 'VisionMaster.Models.ParallelStep'`，栈顶是 `Prism.Commands.DelegateCommand<T>.CanExecute(Object)` ← `MenuItem.UpdateCanExecute()` ← **`ProcessViewModel.set_SelectStep`（:85）的属性通知链**。

**根因**：`AddParallelBranchCommand`/`RenameParallelGroupCommand` 原为 `DelegateCommand<ParallelStep?>`（强类型泛型），而菜单项 `CommandParameter="{Binding SelectStep}"` 绑的是 **object 型** 的当前选中项。选中项从并行组切到普通算子（ActionStep）时，`SelectStep` 的属性通知触发 `MenuItem.UpdateCanExecute()`，WPF 把**当前选中对象**塞进 `CanExecute(object)` —— Prism 的强类型泛型命令直接强转 `ParallelStep` → 崩。这不是"参数偶尔为 null"（null 可转 `T?`），是**类型不匹配必炸**。

**修法**：四条结构命令（`Add/Remove/RenameParallelBranch` + `RenameParallelGroup`）全部改 `DelegateCommand<object?>`，处理器首行 `if (target is not X x) return;`（拿错类型 = 静默空跑，不抛）。

**守门（V8 新增，`ProcessTreeInteractionChecks.cs`）**：用 `new ActionStep(...)` 作参数调四条命令的 `CanExecute`/`Execute` —— 断言**不抛异常 + 静默空跑（分支数不动）**。这条正是本次崩溃的最小复现形状（把命令改回强类型泛型它必红）。

**可复用的坑（已写进 `AGENTS.md` R27）**：`CommandParameter` 绑"object 型当前选中项/非编译期类型"时，命令**必须**用 `DelegateCommand<object?>` + `is` 判型；`DelegateCommand<T>` 只在"参数类型编译期可证"（如常量枚举、绑定到强类型属性）时用。

## 四、验证

- **构建**：`VisionMaster.csproj` / `FlowCanvasChecks.csproj` 均 0 错误。
- **gate**：`cd FlowCanvasChecks && dotnet run -c Debug` → **通过 1820 / 失败 0**。
- **出图**：`--render` 全 8 场景（A/B/C×2/D/E/F/G/H）+ 自检行——分支点击三判据（`RenderProbe.cs:477`）、胶囊菜单（`:508`）、组头菜单（`:521`）、ZOrder 一致性（`:767`）全部符合期望；`parallel-group-params-render-G.png` 为 FlatPropertyGrid 本体四 Tab 出图。
- **真机**：VisionMaster 已用新产物重启（修复后 pid 33852）。
- **真机验收项（一档，留用户）**：右键组头「添加分支 / 重命名分组」、右键分支胶囊「重命名 / 删除」、双击组头开 FlatPropertyGrid 参数面板（枚举下拉中文、行内校验、确认/取消）、画布「整理」看新布局、Popup 类交互（headless 抓不到）。

## 五、未决 / 说明

- W 段 `Check` 调用点按落盘静态清点为 23（交接口径 22）——差异或为交接笔误，本记录以实测为准；若后续 gate 计数与账目对不上，以 `dotnet run` 实际输出为权威。
- 旧记录的处理：`docs\code-changes\2026-10-09-并行分组参数面板（草稿事务-白名单分派-分支卡片交互闭环）.md` 与 `2026-10-09-并行分组分支结构动作下沉右键菜单（面板收窄为参数表单）.md` 已在前置概要追加指针（面板本体被取代、右键命令族不变），正文保持历史记录身份不动。
- 设计文档同步：`docs\方案设计\parallel-execution-phase2-design.md` §8 的 P23/P28 两行标注"已落地（2026-10-10，`ParallelExecutionChecks.cs`）"，设计正文未改。
- 与 2026-10-09 记录的分工边界延续：分支增删改名 = 流程栏右键命令（V8 守）、分组名 = 右键「重命名分组」（W6/W7 守）、三个语义参数 = FlatPropertyGrid 草稿（W1-W5 守）、真并发执行语义 = P1-P31（不在 W 段射程）。
