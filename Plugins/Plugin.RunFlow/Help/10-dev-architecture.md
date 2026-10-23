# 调用流程 · 开发：架构、宿主契约与不变量

> 分类：流程控制 ｜ 来源：插件 Plugin.RunFlow ｜ 读者：开发

S 档一页承担三个角色：架构与数据流、白拿的宿主行为、改这个文件时的红线。
用户册讲的"怎么填 / 怎么排查"这里不重复，只讲**这些行为由谁提供、为什么这么写、写错会怎样**。

## 一、类身份与工程分层

```
Plugins\Plugin.RunFlow\
  RunFlowPlugin.cs        80 行   ← 两个输入端口 + 三个输出端口 + RunAlgorithm
  Plugin.RunFlow.csproj         ← net9.0（不带 -windows）、Nullable enable、只引 Core.Interfaces
  Help\*.md                     ← 内嵌资源，宿主按 {程序集名}.Help. 前缀扫描
```

- `[Display]` 实值：`Name="调用流程"`、`GroupName="流程控制"`、
  `Description="把另一条流程当子程序调用（目标流程需勾选「子程序」调用方式），等待其跑完并把结果带回"`、
  `ShortName="\uf0e8"`（私有区字形，靠 FontAwesome 渲染，换字体就是方框）。
- 命名空间是 `VisionMaster.Plugins.RunFlow`、程序集是 `Plugin.RunFlow` —— 断言宿主按**全名**
  `VisionMaster.Plugins.RunFlow.RunFlowPlugin` 从 `Modules\Plugin.RunFlow.dll` 里取类型，
  两个名字都不能随手改。
- 基类：`VisionPluginBase`。**不实现**任何视图接口（csproj 注释写明：没有 WPF、没有 `Core.Controls`），
  宿主发现没有自定义视图接口时自动包一层通用参数面板；**没有** `[StepConfig]`、**没有** `[ParallelSafe]`。
- `TargetFlowPort` 声明了 `IsFunctionalEnum = true` + `OptionKind = StepConfigOptionKind.SubroutineFlowName`：
  通用面板据此把它渲染成**封闭候选下拉**（并隐藏 🔗/✕）。插件只说"要哪一类候选"，
  候选内容由宿主算（见第七节）——插件仍然不碰 WPF、不碰方案模型。
- `Initialize()` 与 `Dispose()` 都是**空重写**：本插件无自有状态、无自有句柄，也不接管任何资源的释放。

## 二、`RunAlgorithm` 逐步（执行时间线）

| # | 代码动作 | 为什么必须在这一刻做 |
| --- | --- | --- |
| 1 | `Invoked=false`、`ElapsedMs=0`、`InvokeMessage=""` | **轮首清零**：流程可重复执行，输出端口不是"曾经成功过"的存证。不清零，条件分支会读着上一轮的 `true` 走 |
| 2 | `Success.Value = false` | 框架契约：`VisionPluginBase.Execute` 以 `Success.Value is true` 判定业务成功。本步骤的成功**只能由调用结果给**，所以先把默认预置的 `true` 压回 `false`（源码里这一行有注释标明）。空名/异常等提前 return 的分支因此天然是失败 |
| 3 | `TargetFlowPort.GetTypedValue()?.Trim()`，空则写 `InvokeMessage` + `Logger.Error` 后 return | 名字空是**插件层**拦截，不会走到宿主调用器；注意这一支**只写 `Message`、不写 `ErrorMessage`**（见 B1） |
| 4 | `context.FlowInvoker.Invoke(target, TimeoutPort.GetTypedValue(), context.CancellationToken)` | 一次调用把"名字 + 超时 + 父流程取消令牌"三件交出去。能力由执行上下文递送（插件物理上够不到引擎）；没装配时拿到 `NullFlowInvoker`，"调用即失败并说明原因"，**插件不必判空** |
| 5 | `Invoked=result.Success`、`ElapsedMs=result.ElapsedMs`、`InvokeMessage=result.Message`、`Success=result.Success` | 结果**原样转发**，插件不做二次判定、不改写文案、不把失败说成成功。三件事实（成不成 / 为什么 / 多久）在任何失败分支上都必须齐全 |
| 6 | 成功 `Logger.Info`（带耗时）/ 失败 `Logger.Error`（带目标名与原因） | 日志与端口同源，现场可以从任一侧复现判定 |

## 三、门禁与执行在哪一侧（`Engine/FlowInvoker.cs`）

判定顺序固定，任一不过 → 子流程**一步都不跑**、当场返回失败（都走 `FlowInvokeResult.Fail`）：

```
名字判空 → 父流程取消令牌预检 → 查找流程（Ordinal）→ IsEnabled → InvokeType 含 Subroutine
        → StepsEncrypted → 目标会话 IsRunning → 会话准备（图纸改过则重新编译并注册）
        → 抢会话锁 → 单次执行（非调试）→ 20ms 一拍轮询等待 → 数失败步骤 → 返回
```

| 环节 | 事实 | 为什么这样设计 |
| --- | --- | --- |
| 调用方式位集 | 只看 `Subroutine` 这一位；`Manual` / `Timer` / `Variable` / `Http` 都不放行 | 用户决策："只有选择了这个的才能被调用"在同一条纪律下扩展到子程序 |
| 目标正在运行 | 直接失败，**不排队、不补跑** | 排队会把"A 等 B、B 又在等 A"这种成环误配变成挂死现场；自己调自己也被这一条天然拦住（父流程持有自己的会话锁） |
| 会话锁抢不到 | 如实返回失败 | 历史上这里只记一条警告就正常返回，父流程会把"我没跑上"读成"跑完了"，带着未落地的数据继续走 |
| 超时 | 只结束**等待**，**不打断目标** | 取消令牌归引擎自己管，调用侧去掐它会污染会话状态 |
| 跑完但有失败步骤 | 失败步骤数 > 0 → `Success=false` | 数的是**这一次**的失败：单次执行开跑前整树复位过步骤状态，不吃上一轮连续运行的历史 |
| 装配方式 | `engine.FlowInvoker = new FlowInvoker(...)` 由 `FlowEngineModule` 完成 | 属性注入：引擎与调用器互相引用，构造期解不开环 |

## 四、变量与图像怎么在两次执行之间传

**唯一通道是全局变量**（`IGlobalVariableWriter`，同样经执行上下文递送）：

| 想传的东西 | 走哪 | 谁负责 |
| --- | --- | --- |
| 父 → 子 的参数 | 父流程调用前用「变量赋值」写全局变量，子流程内读 | 宿主变量表 |
| 子 → 父 的结果 | 子流程内写全局变量，本步骤返回后父流程后续步骤读 | 同上 |
| **图像** | 全局**图像**变量：子流程里用「图像赋值」写入，父流程侧由组态图元或后续算子读 | 图像所有权仍在**生产者算子**，本插件与写入器都只搬引用 |
| 流程内局部量 | 执行上下文上的 `LocalVariables` / 运行时变量 | **不跨流程**，每轮执行新建一份，不能当父子通道 |

本插件**不接触任何 `HImage` 句柄**：它只传名字与超时，不 clone、不 Dispose、不清空变量表。

## 五、白拿的宿主 / 基类行为（作者不用写、也别重写）

| 行为 | 由谁提供 | 表现 |
| --- | --- | --- |
| 端口发现与注册 | `VisionPluginBase` 反射 public 端口属性 | 端口名可省略（自动取属性名）。本插件 `TargetFlowPort` 显式写成 `FlowName`、`InvokeMessage` 显式写成 `Message`——**存档键是端口名** |
| 灌值 / 写回 | 基类 `ApplyConfigValues` + `OnConfirm` | 面板显示已保存值而非默认值；已链接的口写回时移除常量键 |
| 通用参数面板 | 宿主自动端口面板 | 不实现自定义视图接口就自动有：`FlowName` 一行是封闭候选下拉（无链接/清除），`TimeoutMs` 一行是值 + 链接 + 清除 |
| 业务成功判定 | `VisionPluginBase.Execute` 读 `Success.Value` | 所以第 2 步必须显式压 `false`，否则提前 return 的分支会被当成成功 |
| 异常兜底 | 基类 `Execute` 包裹 `RunAlgorithm` | 调用器抛出的异常转成失败与错误文案，不把流程线程带崩；**门禁失败不走异常**（是正常返回值） |
| 流程调用能力 | `IFlowInvoker` / `FlowInvokeResult`（`Shard\Core.Interfaces\IFlowInvoker.cs`） | 结果用只读结构体而不是 `bool` + `out`，就是为了"三件事实齐全"；宿主没引擎时给 `NullFlowInvoker` |
| 取消传播 | `IExecutionContext.CancellationToken` | 父流程急停时等待一起结束（但目标流程不被打断，见第三节） |

`[StepConfig]` 源生成器（AGENTS.md R16/R18）在本插件上没有产出：一个配置属性都没有。
**若日后把超时改成配置属性**，它会脱离端口通道 → 参数面板只列输入端口，那一项在界面上就没有渲染形态。

## 六、`.vms` 落盘键（引用一律用名字，不用显示名）

| 存的东西 | 键 | 值形态 |
| --- | --- | --- |
| 目标流程名 | `InputValues["FlowName"]` | 字符串，逐字是被调流程的**名字** |
| 等待超时 | `InputValues["TimeoutMs"]` | 整数毫秒 |
| 被调流程的可调性 | 那条流程自己的 `InvokeType` 位集 | 位集值；老单选 1 / 2 / 3 在加载时迁移为 Timer / Variable / Subroutine 位 |

- 面板标签（"目标流程名" / "等待超时(ms)"）与成员描述**不进存档**，改文案不打断任何方案。
- **改名连锁**：被调流程改名 = 所有调用点的 `FlowName` 失配；`FlowName` 与 HTTP 路由、运行状态镜像
  共用同一个"按名 Ordinal 查找"口径，不会出现"面板里找到了、引擎找不到会话"的自相矛盾。

## 七、目标流程名的候选来源（宿主侧）

插件只声明"要哪一类候选"，真正的列表由宿主算 —— 链路四跳：

```
RunFlowPlugin.TargetFlowPort
  OptionKind = StepConfigOptionKind.SubroutineFlowName      ← 契约层只放类别
        ↓
Engine\SubroutineFlowOptions.Build(workspace)               ← 宿主算候选（本仓唯一一份）
  只留 (InvokeType & Subroutine) != 0；禁用/加密/当前流程照列，只加显示后缀
        ↓
FlowEngineModule.Initialize → SubroutineFlowOptions.Register(...)
  StepConfigOptionSource.RegisterAdditional(...)            ← 启动期注册，UI 每次打开现取
        ↓
LinkableValueEditor（Core.Controls）→ IsOptionPort → 下拉
```

四条不变量：

| # | 判据 | 为什么 |
| --- | --- | --- |
| 1 | **候选必须现取**（用 `OptionKind`，不是 `PresetOptions`） | 插件实例在方案打开前就被反射 new 出来，那一刻的 `PresetOptions` 必然是空的；`OptionKind` 让界面每次打开都问一次 |
| 2 | **只列 `InvokeType` 含 `Subroutine` 位的流程** | 与 `FlowInvoker` 的门禁同一条判据；列出没勾的等于递给用户一个"选了必然失败"的选项 |
| 3 | **值 = 流程名本体**，显示文本只加后缀 | `FlowInvoker.FindFlow` 按名 `Ordinal` 查找；把显示文本当值写进 `.vms`，改一次后缀就断链 |
| 4 | **禁用 / 加密 / 当前流程照列并标注** | 剔掉会让已配好的值在下拉里显示空白（值还在、只是选不到），用户会以为配置丢了；标注才看得出失败原因 |
| 5 | **绑定弹窗对封闭端口恒强制常量模式**（判据只看 `IsFunctionalEnum`，**不看候选数**） | 空候选时放开上游面板 = 允许运行期拿值当名字；空候选的正确退化是手填（`VariableBindingView` 里按 `PresetOptions.Count` 显示手填框） |
| 6 | **封闭端口上的老上游连线：下拉照常显示，选一项先 `RemoveLink` 再写值** | 老 `.vms` 里这条线是"当年它还是普通 string 端口"时绑的；不拆就继续动态供值，而 🔗/✕ 又被隐藏，用户既改不了也拆不掉 |

`RegisterAdditional` 而不是 `Register`：运动模块（轴名/卡名）与通讯模块（通讯变量名）也各注册一份，
谁也不该顶掉谁。解析工作区放在委托里现做，切方案后候选自动跟着变。

## 八、A 组不变量（违反就编译/闸门红）

本插件的守门断言**真实分布**（逐条回源码抄）：
`FlowCanvasChecks\FlowAutomationChecks.cs` 的分节 `[E16] 子程序调用：门禁`（门禁单元 + Modules 装载 + 端到端父子跑通）、
`FlowCanvasChecks\FlowInvokeChecks.cs`（调用方式位集 37 条）、
`FlowCanvasChecks\ParallelExecutionChecks.cs`（P23 两条）、
`FlowCanvasChecks\FlowContainerChecks.cs`（前提项）。

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 门禁被绕过：没勾「子程序」也能被拖起来 | 判定挪到了插件侧、与宿主两处各判一半 | `★未勾「子程序」→ 失败并指路流程管理`、`★子流程未勾「子程序」→ 调用被门禁拦下（这一步失败、子流程没跑）`（`FlowAutomationChecks.cs`） |
| 拦下了却"其实偷偷跑了一遍" | 只看返回值不核对是否真执行 | `被门禁拦下的调用没有真的执行（计数为 0）`（同上） |
| 禁用/加密流程被当子程序调用 | 判定顺序或判据被改 | `禁用流程 → 失败（禁用判定在调用方式之前）`（同上） |
| 找不到流程时给出莫名错误 | 按名查找口径漂移 | `不存在的流程 → 失败且说明原因`（同上） |
| 插件拿到空实现、子流程永不执行 | 上下文没注入真调用器 | `★子流程真的被执行了（插件拿到的是真调用器，不是 NullFlowInvoker）`（同上） |
| 等待语义丢失（变成发出后不管）→ 父子竞态 | 调用未真等或父流程被放行 | `父流程跑完（未挂死——子流程等待是真等的）`、`★勾选后真跑一次并成功（含耗时）`（同上） |
| 插件产物没进 `Modules`，宿主加载不到类型 | 红线①：插件工程必须放 `Plugins\` 下并由构建约定投递 | `Plugin.RunFlow.dll 已投递到 Modules 且类型可解析（红线①：插件产物必经 Modules 投递）`、`父流程（含「调用流程」步骤）编译通过`（同上）、`（前提）Plugin.RunFlow.dll 可装载`（`FlowContainerChecks.cs`） |
| 并行分支里一次门禁失败把整条并行链拖崩 | 门禁失败被写成抛异常（会被登记成故障源） | `P23：门禁失败不抛异常（并行分支里是普通业务失败，不触发故障源登记）`、`P23：Invoked=false 且 Message 如实带回门禁原因`（`ParallelExecutionChecks.cs`） |
| 老 `.vms` 的调用方式被读成另一种触发 | 新位值与老单选值撞号 | `老值 3（子程序）→ Subroutine 位（32）`、`★新位值全部避开老单选值 1/2/3（撞号 = 老文件被读成另一种调用方式）`、`已是位集的值原样保留（高版本 → 低版本不丢调用方式）`（`FlowInvokeChecks.cs`） |
| 目标流程名又能手打成不存在的名字 / 没勾子程序也配得下去 | 候选来源没接上或类别写错，界面退回自由文本 | `★FlowName 端口声明为封闭候选（IsFunctionalEnum + SubroutineFlowName）`、`★候选只列勾了「子程序」的流程`、`注册后从契约层来源表取到同一份候选`（`FlowAutomationChecks.cs`） |
| 「勾了却没生效」在界面上看不出来 | 显示文本回落到"手动"或被吞 | `认不出的位值显示成「未知(4)」而不是「手动」`、`★四个调用位全部接通 → PendingReason 全为 null（界面不再提示「待接通」）`（同上） |
| 手册被自动生成页顶掉 / 串页 / 不达标 | Help 页没进 DLL 或命名不合骨架 | `HM4 有 Help/*.md 的工程都带 EmbeddedResource（手册进得了 DLL）`、`HM2 多插件程序集不许放扁平手册（防串页）`、`HM8`（用户册合计 ≥70 行）、`HM9`（禁占位词）、`HM12` / `HM12b`（`NN-dev-` 命名、不许另立子目录）、`HM13`（本档 S：开发页 1 页、合计 ≥100 行）、`HM16`（档位表与带 `GroupName` 的插件类一一对应）（`FlowCanvasChecks\HelpManualCheck.cs`） |

## 九、B 组不变量（不报错，只出诡异现象）

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 什么都没填、界面上 `ErrorMessage` 是空的 | 第 3 步的空名分支只写 `Message` + 日志，没走 `Fail()`；报警只连 `ErrorMessage` 的现场收到空消息 | 无断言覆盖 `ErrorMessage` 文案（P23 断的是 `Success` / `Invoked` / `InvokeMessage` 三件），靠人工核对 |
| 本轮失败、下游条件分支照样走"成功"那支 | 轮首没清零输出端口 | 无断言，靠人工核对（第 1 步就是这条） |
| 提前 return 的分支被当成业务成功 | 没显式压 `Success.Value=false`，吃到基类预置的 `true` | 无断言，靠人工核对（源码注释钉住了这条契约） |
| 超时后子流程还在动机构、写变量，父流程已经往下走 | 超时不打断目标是**设计**；`TimeoutMs` 小于最坏耗时才会暴露 | 无断言，靠人工核对（用户册第四节口径） |
| "跑完但有 N 个步骤失败"被判成成功 | 失败步骤数判据（`FlowInvoker.cs` 末段）被改成只判"任务结束" | `★勾选后真跑一次并成功（含耗时）` 只覆盖成功路径；失败计数这条**无专属断言，靠人工核对** |
| 二次调用撞上"正在运行"，看起来像偶发故障 | 上一轮超时未打断 / 定时或 HTTP 也在唤起同一条 | 无断言，靠人工核对 |
| 图纸整树状态复位漏了嵌套层，失败计数吃到历史轮 | 递归收集不彻底 | `嵌套的循环体节点同样挂对图纸`、`分支里的第二层步骤也进 Blueprints（递归到底）`（`FlowCanvasChecks\ExecutionChecks.cs`）；**复位动作本身无专属断言** |
| 改端口名后老方案静默失效 | `InputValues` 的键随端口名变（`FlowName` / `TimeoutMs`），旧键没人读 | 无断言，靠人工核对（改键必须同步用户册《端口与参数》） |

## 十、改完这个文件必须做的动作

| 改了什么 | 必须同步 |
| --- | --- |
| 端口名 / 默认值 / 必填性 | 用户册《端口与参数》的端口表与第六节存档键；`FlowAutomationChecks` 里用 `SetInputValue("FlowName", …)` 的夹具 |
| 端口候选类别 / 宿主候选来源 | `StepConfigOptionKind` 的类别、`Engine\SubroutineFlowOptions.cs`、`FlowEngineModule.Initialize` 的注册、用户册《端口与参数》的候选说明、`FlowAutomationChecks` 的候选断言（四处必须同口径） |
| 输出端口集合 | `ParallelExecutionChecks` P23 的三件判据（它按属性名反射取 `TargetFlowPort` / `Invoked` / `InvokeMessage`） |
| 门禁判据或判定顺序 | **只能改宿主 `Engine\FlowInvoker.cs`**，不要在本插件里加二次判定；同步 `FlowAutomationChecks` [E16] 与用户册《字段与排错》的消息原文表 |
| 错误文案 / 等待与超时语义 | 《字段与排错》的原文表与超时口径表、《排错》的判定树（三处要求逐字一致） |
| 调用方式位集 | `FlowInvokeChecks` 全部 + 用户册里"位集/勾哪一位"的口径 |
| 手册页数 | 收尾一次 `--no-incremental` 重建 + 逐 DLL 资源名对账（R42：增量构建会静默漏打包）。**断言是从 `Modules\` 反射拿插件的**，不重建就等于在测旧 DLL（红线③） |

```powershell
# 只看构建退出码不算数——核 DLL 真的带了 Help 资源（R42）
tr '\0' '\n' < Modules\Plugin.RunFlow.dll | grep -a "\.Help\."
```
