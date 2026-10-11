# 流程引擎真并行执行 · 第二期方案说明书

> **状态：设计定稿 v2（双线评审已吸收）** ｜ 日期：2026-10-09 ｜ 作者：并发引擎架构师
>
> v1 → v2：两线评审（第一线"找会崩点" + 第二线"语义边界/实现陷阱/可测性"）共 15 条 finding，各条均经第三方独立复核证实，全部吸收；另有用户裁决 3 条（已定，不可改）与主会话新增口径 2 条。逐条对照见 §0「v2 修订记录」。
>
> 一期记录：`docs\code-changes\2026-10-07-流程画布三大核心交互断链修复（拖拽改序-建线-右键菜单）与海康对标补齐.md` §十
> 行号引用说明：所有 `file:line` 均为 2026-10-09 当日仓库现状取证；实现期行号可能漂移，结构性结论（某成员在哪个类、做什么）不随行号失效。
> 本文档为纯设计交付（本任务只读仓库，未运行构建与断言程序）；§8 断言清单为实现期验收契约。

---

## 0. v2 修订记录

| # | 来源 | 缺陷/裁决 → 修订 | 落点 |
|---|---|---|---|
| 1 | 用户裁决 1 | 失败聚合默认值走全局配置项：`AppConfigModel` 新增 `ParallelExecutionSettings` 节（`FailFastByDefault` 默认 false），容器级三态 `FailFastMode` 可覆盖 | §3.2、§3.3、§7 |
| 2 | 用户裁决 2 | 硬件算子进并行分支：二期编译期拒绝（不标 [ParallelSafe] → 门禁拦截）+ 三期做资源锁（§10 给出设计） | §5.2、§6、§10 |
| 3 | 用户裁决 3 | 调试运行遇并行组：就地退化顺序执行 | §4 |
| 4 | 主会话口径 4 | 试运行（PluginTestRunner）与界面调试同权重退化：试运行目标为并行容器时也会真并发（session 从不置 DebugEnabled，PluginTestRunner.cs:86-95 证实临时会话无调试位），必须堵——PluginTestRunner 置会话退化标志，退化判定同时认"调试标志 + 试运行标志"两个入口 | §4 |
| 5 | 主会话口径 5 | 标注清单修订：ExcelExport、ImageAssign 不标 [ParallelSafe]（实证不安全）；DataRecord、ResultUpload 可标（进程级单消费串行实证安全）；27 个 Plugins\ 工程逐个打勾表写进本文档 §6 | §6 |
| 6 | 评审高危 1（groupCts 泄漏） | v1 伪代码 CreateLinkedTokenSource 后无 Dispose 落点；父令牌是会话级（FlowEngineService.cs:331 建、:439 收尾才 Dispose），连续运行每轮挂一份注册 → 长跑内存增长。修法：`using var groupCts` + join 后释放；断言"连续运行 N 轮后 Created==Disposed"（P5） | §3.1、§8 |
| 7 | 评审高危 2（故障源登记缺失） | v1 裁决表"按分支声明序取第一"上抛 Faulted 异常——被取消分支的 OCE 会顶掉真故障源异常，FlowEngineService 的 catch(OperationCanceledException)（:383-386）判为正常取消 → 会话 Stopped 非 Faulted，错误静默吞掉。修法：并行节点持共享槽 `int failureSource=-1`；分支捕获异常先判 `groupCts.IsCancellationRequested`，已取消且 CAS 登记失败（非源）→ 记 Cancelled（异常降级日志）；只有真正触发 Cancel 的分支记 Failed(ex) 并上抛 | §3.1、§3.4、§8 |
| 8 | 评审高危 3（FailFast 状态机自相矛盾） | v1 写"业务失败→FailFast→记 Faulted(业务)"，但 BranchOutcome 无此成员、业务失败无异常对象（CompiledPluginNode.cs:44-51 不抛、VisionPluginBase.cs:105-118 默认 RethrowOnException=false），而裁决 1 要求 ExceptionDispatchInfo 原样上抛——实现者只能编造异常，后果是把业务失败升级成会话 Faulted：连续运行 while 循环整体终止（FlowEngineService.cs:347-381 一抛跳出循环，"跑一次就停"）+ HTTP 200 变 500。修法（定死）：**FailFast 只做提前取消、不做异常升级**——取消兄弟分支 + 容器标 Failed + 本轮正常收尾（连续运行下一轮照常启动、会话不进 Faulted、HTTP 按容器状态呈现） | §3.2、§3.4、§8 |
| 9 | 评审高危 4（FailFastMode 三态 + 全局配置） | 只留 bool 时全局置"开"后任何容器无法单独关掉；存量 .vms 缺字段=0 需兼容。修法：`FailFastMode { Inherit=0, On=1, Off=2 }`（Inherit=0 兼容存量）；全局节 `ParallelExecutionSettings` **必须带 `= new()` 初始化器**（AppConfigModel.cs:356-384 三次踩过缺节变 null 的坑，HttpImageServer/NetworkCameraServer/ImageGallery 同款防护）+ `FailFastByDefault` 默认 false（= 一期行为，P20 才守得住）；读取时机 = 运行期（编译期读会传染 21 处 new FlowCompiler 调用点，且断言宿主会在 bin 写 AppConfig.json 导致非密闭）；装配在 AppSettingsService 注册点灌进静态策略（与 RunWindowMode 同口径，App.xaml.cs:139 RegisterSingleton\<AppSettingsService\>） | §3.2、§3.3、§8 |
| 10 | 评审高危 5（编译门禁 ambient 化） | v1 门禁只查"直接父容器 ExecutionMode==Parallel"，但 CompileSteps 递归编译无"处于并行分支内"的环境状态——两个绕过口：分支内 If/For/While 藏未标注算子；外层 Parallel 分支内的 Sequential 内层组（内层节点仍与其它外层分支真并发）。修法：CompileSteps 增加 `bool inParallelBranch` 参数，进 Parallel 模式分支置 true 且被嵌套容器/内层并行组全部分支继承；凡 true 时编译出的 CompiledPluginNode 一律查 [ParallelSafe]。特性匹配按特性类型 FullName 字符串而非 typeof 等值（AGENTS.md 红线 2"两份 Core.Interfaces.dll"会打掉类型身份，表现为"明明标了还报不安全"）。报错定位到分支内具体步骤 + 分支序号（复用 DescribePosition/DescribeScope 口径，FlowCompiler.cs:1262-1291） | §5.1、§5.2、§8 |
| 11 | 评审高危 6（线程模型修正） | v1"同步 join + 不限流"在嵌套并行下线程池饥饿（分支体同步占池线程到 join；8×8≈73 线程；CLR 注入每秒 1-2 条 → 秒级卡顿）。且分支 Task 体无 try/catch，WaitAll 首个异常直接抛 AggregateException 走不到合并。修法：每并行组只 Task.Run `min(核数-1, n-1)` 条，最后一条分支在 join 线程就地执行（每层至少一个前进者，饥饿链断开）；分支 Task 体整体 try/catch——任务永不 faulted，结果（含异常）全部记入 ParallelBranchResult，join 处统一裁决；"同步 join 非池线程"从 Debug 断言升级运行期检查（SynchronizationContext.Current != null 时 Warn） | §3.1、§8 |
| 12 | 评审高危 7（合并规则重写） | v1 三缺陷：(a) "与快照比对（引用/值相等⇒未写）"漏报同值写入——承诺"≥2 分支写同名⇒Warn"失效；(b) 精确记账必须改 ExecutionContext.LocalVariables（:88 只读属性派生类换不掉字典实例），与 v1"ExecutionContext 零改动"承诺矛盾；(c) Equals 可能抛（字典可放任意 object），在父线程打断合并。修法：ExecutionContext.LocalVariables 改 `{ get; protected init; }`（IExecutionContext 契约零改动，实现一处改动）；分支上下文安装**记账字典**（写先登记 key）；合并与告警按真实写入集合；值比较包 try/catch（异常按已写处理 + Warn）；已知假阴性"写回与快照相等的值视为未写"不再存在（记账制无此缺陷） | §3.5、§7 |
| 13 | 评审高危 8（取消分支的影子处置） | v1 合并规则无"分支终态→是否合并"维度——被取消分支的半成品写入照样覆盖父域且参与冲突告警，下游取值随取消时机漂移。修法：合并表加"分支终态→是否合并"列：**Cancelled 分支影子整体丢弃**（不合并不参与告警，丢弃时 Warn 变量名+原因）；只合 Success/BusinessFailed 分支；Return 分支"已完成部分"是否合并 = 二选一写死（本方案选**合并**，理由见 §3.5）；外部停止 → 不合并。断言：FailFast 场景被取消分支写入不可见；停止场景父域保持快照 | §3.4、§3.5、§8 |
| 14 | 评审中危 9（全局变量口径统一） | v1 §3 不变量 5 说"编译期对同名全局写报错"但 §5 只查 Scope=Runtime，两节矛盾。写路径已核对无锁（LocalVariableModel.cs:85-98 写线程直接触发 ValueChanged，通信回写/SCADA/监视订阅者被多线程调用；GlobalVariableWriter.cs:38-82 直写）。修法：编译期同名写冲突检查扩到 Scope=Global（仅当可静态解析同名常量时）；运行期并行分支写全局 → Warn（变量名+分支）；"并行分支内避免写全局"写成图纸纪律；风险表"实现期待核对"改"已核对：值路径无锁" | §5.3、§9 |
| 15 | 评审中危 10（join 窗口停止口径） | v1 裁决 3"join 后父令牌已取消→Skipped"会让"全部分支跑完、步骤全绿、恰在 join 期间停止"也标 Skipped——"这一步没跑"的假象，与顺序路径同窗口报 Success 不一致。修法：**扇出前已取消 → Skipped**（与既有 CompiledParallelNode.cs:45-48 逐位一致）；**扇出后取消 → 容器按规则正常裁决、分支步骤态如实呈现** | §3.4、§8 |
| 16 | 评审中危 11（僵尸会话放大） | StopSessionGracefully 3 秒放弃且不 Dispose（IRuntimeManager.cs:94-104/:141-143），新旧会话共享同一批 StepModel，_pendingRuntimeNotify 无锁 List（StepModel.cs:180-184/:220-228）——并行把一个僵尸放大成 N 线程僵尸。修法：join 改可中断语义（WaitAll 循环等待 + deadline，超时记 Error 按明确口径放弃：容器 Failed + 日志 + 不阻塞会话替换）；StepModel pending 记账加锁或不可变快照；"并行组不可无限期拖住会话替换"写进硬约束 | §3.1、§7、§9 |
| 17 | 评审中危 12（RunFlow 绕过通道） | 子流程独立编译不受门禁约束（FlowInvoker.cs:81-84 会话准备单点独立编译），且 v1 把 RunFlow 列入可标清单。修法：RunFlow 从可标清单移除；文档 + 风险表明示"三期资源锁落地前，经 RunFlow 进入的子流程不受并行门禁保护，禁止在并行分支调用含采集/运动的子流程" | §6、§9 |
| 18 | 评审低危 13（配置级资源） | 类型级 [ParallelSafe] 看不见目标路径：ExcelExport 整文件重写无锁 + 固定 tmp 抢占（XlsxHyperlinkInjector.cs:203-232 tmp 路径 = 目标路径+".tmp" 无锁抢占）；ImageAssign 写全局变量（ImageAssignPlugin.cs:61-68）。DataRecord/ResultUpload 实证安全（进程级单消费线程串行：RecordingHub BlockingCollection + 单消费线程 :85-96；ResultUpload UploadQueue SpoolLock :86）。修法 = 主会话口径 5，见 #5 | §6 |
| 19 | 评审低危 14（UpdateStepRuntimeState 改动面） | v1"ExecutionContext 零改动 / 分支线程完全不触碰 FocusedStep 交接段 / 约+8 行"三处承诺虚低：Skipped 分支（CompiledNode.cs:254-258）同样读写 session.FocusedStep，分支被取消时必然进来。修法：抑制写成**整方法级分支快捷路径**（分支上下文只做 State+timing，绝不触碰 FocusedStep，含 Skipped）；§7 改动量改口"重构"非"+8 行"；"分支步骤只写自己 StepModel 的 State/耗时、容器只由父线程写"写成并行节点不变量（§3.6） | §3.6、§7 |
| 20 | 评审低危 15（杂项修正） | v1 P21 焦点断言跨线程采样有竞态，改"探针桩在真并发窗口内采样记录"；v1 P19 ForceSequential 断言补 try/finally 复位 + 复位后并行行为恢复断言 + 读写 Volatile；ExecutionMode 加"改属性→Version++→重编译"正例断言；P6 汇合裁决抽纯函数表驱动断言；P1 前置会合点 GatePlugin.Reached.Wait；P3 会合点 InGroup 事件；性能基线数字（1620/4）实现期先实测再按 P 清单增量；DebugChecks 组数按实际（11 组 Section，DebugChecks.cs:108-448） | §8 |
| 21 | 评审低危 15（RecordingHub 裸读） | RecordingHub.cs:406 有一处裸读 `_started`（声明 :95 `static int`，启动用 CAS :195），评审建议改 Volatile.Read。本方案确认：该读取在 Shutdown() 内（进程退出钩子），与并行无交互，不阻塞 DataRecord 标注；作为实现期顺手项记入改动清单 | §6、§7 |

**v1 存活内容**（未被评审否决，v2 原样保留并按需强化）：同步外形内部扇出的总体架构（§2）、影子副本 + 汇合合并的变量域选型（§3.5）、Break/Continue 只终结本分支 + Return 全局终止的控制流语义（§3.7）、调试门/高亮的容器持焦方案（§3.6）、ExecutionMode 兼容性设计（§4）、桩插件断言范式（§8）。

---

## 1. 背景与目标

一期已落地 ParallelStep 容器（顺序逐分支执行）：`Core\Models\ProcessStep\ParallelStep.cs`（默认双 Default 分支 :48-50、`RecommendedMaxBranches=8` :29）、`Core\Models\Compileds\CompiledParallelNode.cs`（Branches 平铺交出 :55、CompileNotes 旁注 :33）、FlowCompiler 场景 A-3（`Engine\FlowCompiler.cs:499-545`）、画布并列泳道、拓扑分支互取 = CrossBranch 非法（一期断言 V2，`FlowCanvasChecks\ParallelContainerChecks.cs:102-104`）。

二期目标：ParallelStep 的分支**真正并发执行**，任一分支失败 → 其余分支尽快取消 → 汇合按确定性规则裁决（海康口径）。

### 1.1 引擎现状取证（设计的全部地基）

| # | 事实 | 出处 |
|---|---|---|
| F1 | `RunSequence` 是全引擎唯一节点序列执行器（栈式深度优先）；每个节点边界写 `context.CurrentNodeId`（:178）、过调试门（:188-189）、检查 `CurrentFlowState`（:199-211） | `Core\Models\Compileds\CompiledNode.cs:154-213` |
| F2 | `ExecutionContext.LocalVariables` 是 `IDictionary<string,object>` 只读自动属性（内部普通 Dictionary 非线程安全），`CurrentNodeId`/`CurrentFlowState` 是单值实例字段，`CancellationToken` 是 init-only | `Core\ExecutionContext.cs:88 / :78 / :29 / :24` |
| F3 | `ExecutionContext` 完整构造函数签名 `(ILogService, FlowSession, IWorkspaceManager, CancellationToken)`，Cameras/Motions/FlowInvoker 可在对象初始化器注入 | `Core\ExecutionContext.cs:118-124 / :55 / :63 / :73` |
| F4 | `CompiledParallelNode.RunAndGetNext` 当前把全部分支 `SelectMany` 平铺交出（顺序语义），节点结构注释已预告二期仅替换实现 | `CompiledParallelNode.cs:35-56 / :16-17` |
| F5 | 节点业务失败（`Success=false`）**不抛异常、流程继续**；异常才上抛中断 | `CompiledPluginNode.cs:44-51 / :55-59`、`Shard\Core.Interfaces\VisionPluginBase.cs:94-119`（Execute 预置成功 :98、异常转失败 :109-117、`RethrowOnException` 默认 false :126） |
| F6 | 调试门 `DebugGateBeforeNode`：`DebugEnabled=false` 一行 bool 早退（:249）；停点三类；等待 `PauseLock.Wait(token)`（:286）；`DebugStepPending` 一次性消费（:267） | `Core\Models\FlowSession.cs:247-299`、`DebugEnabled` 定义 :203 |
| F7 | `session.FocusedStep` 执行指针单行高亮：新步骤 Running 时从上一焦点平滑移交（四步非原子：读旧→清旧→置新→赋值）；Skipped 且是焦点时同样读写 | `CompiledNode.cs:243-247 / :254-258`、`FlowSession.cs:346`（裸属性，非线程安全） |
| F8 | `StepModel` 运行态写入无锁，但每个步骤对象只属于一个执行位置（编译期一个 StepModel 对应一个 CompiledNode/一个插件实例，FlowCompiler.cs:576 Activator.CreateInstance 独立实例）；`_pendingRuntimeNotify` 是无锁 List | `StepModel.cs:168-185 / :153-156 / :180-184 / :220-228` |
| F9 | 会话启动互斥走 `IResourceLockService.TryAcquireLock("FlowSession:{id}")`（信号量 1,1 原子抢锁） | `Engine\FlowEngineService.cs:145 / :167-173` |
| F10 | 引擎两条执行入口均在 `Task.Run`（线程池线程）里跑 `session.ExecutionEngine.Run(context)`，异常 → 会话 Faulted，`catch(OperationCanceledException)` 判正常取消；收尾 finally 归还会话锁 | `FlowEngineService.cs:347-381 / :528-547 / :383-398 / :399-455` |
| F11 | 容器失败上浮 `EscalateContainerFailures` 在**轮末**由引擎线程单线程执行（子步骤 Failed → 容器 Failed，只改状态报告不动控制流；递归整棵蓝图树） | `FlowSession.cs:49-76`、`FlowEngineService.cs:373 / :543` |
| F12 | 运行时变量写方：`VariableAssignmentPlugin`（Scope 端口白名单 Runtime/Global :88-95；Runtime 路径 `ContainsKey→类型守门→写/Add` :124-164；Global 路径交 GlobalVariableWriter :99-121）与 `CSharpScript` | `Plugins\Plugin.Utility\VariableAssignmentPlugin.cs:59-164`、`Plugins\Plugin.CSharpScript\ScriptContext.cs` |
| F13 | 全局变量写路径**已核对无锁**：`LocalVariableModel.Value` setter 直接触发 `ValueChanged`（通信回写/SCADA/监视订阅者被多线程调用）；`GlobalVariableWriter.TryWrite` 查注册表→类型守门→`writable.TryWrite` 直写 | `LocalVariableModel.cs:85-98 / :118-123`、`Core\Binding\GlobalVariableWriter.cs:38-82` |
| F14 | HTTP 收图入口：`IsRunning` 预检 409 → `TryRunSessionOnceAsync`（非调试）→ 超时观察不取消 | `VisionMaster\Services\HttpImageServer.cs` |
| F15 | 一期断言 V3 钉住"分支1A→分支1B→分支2A"顺序语义与容器 Success | `ParallelContainerChecks.cs:152-155` |
| F16 | 条件/循环求值从 `context.LocalVariables` 现取值，求值发生在节点所属执行线程上 | `CompiledIfNode.cs`、`CompiledWhileNode.cs`、`RuntimeVariableProxyPort.cs` |
| F17 | DynamicExpresso：`Interpreter` 非线程安全故每次解析新建；编译产物 `Lambda` 只被所属节点的执行线程 Invoke | `FlowCompiler.cs:30-33` |
| F18 | 插件静态可变状态普查：`YoloSession.Cache` 带 CacheLock；`CSharpScriptEngine.CallCache` 带 CacheLock；`RecordingHub` 全 Interlocked/BlockingCollection（:85-96，裸读 _started 一处 :406）；`MatchingPlugin.EnsureModelLoaded` static 但只改本实例条目 | 各插件源文件 |
| F19 | `IExecutionContext` 契约面**没有**资源锁递送成员 | `Shard\Core.Interfaces\IExecutionContext.cs:15-72` |
| F20 | 日志：`LogService` 文件通道走 Channel 队列（线程安全），UI 通道经 `SafeDispatch.BeginInvoke` 异步投递 | `Core\LogService.cs:28-52` |
| F21 | `AppConfigModel` 引用类型配置节缺字段时 Newtonsoft 反序列化为 null（HttpImageServer/NetworkCameraServer/ImageGallery 三节均带 `= new()` 初始化器并注释踩坑史） | `AppConfigModel.cs:356-384` |
| F22 | `RuntimeManager.StopSessionGracefully` 3 秒轮询超时即放弃，超时会话保留实例不 Dispose（僵尸会话） | `Engine\IRuntimeManager.cs:94-104 / :141-143` |
| F23 | `PluginTestRunner` 试运行建临时会话（`new FlowSession`，不置 DebugEnabled），容器目标整体走 `ExecuteChain→node.RunAndGetNext(context)` | `Engine\PluginTestRunner.cs:86-95 / :129-133 / :178-207` |
| F24 | `FlowInvoker` 子流程调用：会话准备单点 `FlowSessionFactory.TryEnsureSession` **独立编译**目标流程，与父流程的编译期门禁无交集 | `Engine\FlowInvoker.cs:81-84` |
| F25 | `DebugChecks` 共 11 组 Section（E13 系列）；`ParallelContainerChecks.Run()` 注册于 `FlowCanvasChecks\Program.cs:90` | `DebugChecks.cs:108-448`、`Program.cs:90` |

---

## 2. 总体架构：同步外形、内部扇出

**核心形状**：`CompiledNode.RunAndGetNext(IExecutionContext)` 的签名是同步的（`CompiledNode.cs:131`），全引擎（含 PluginTestRunner.ExecuteChain:200 直接调节点、If/For/While 的交接协议）都依赖这一外形。二期**不改签名**：`CompiledParallelNode.RunAndGetNext` 内部完成"扇出 → 并发执行 → 同步汇合 → 合并 → 裁决"，对上层完全透明。

```
父线程（引擎 Task.Run 线程，FlowEngineService.cs:347/528）
  └─ RunSequence(...) ──► CompiledParallelNode.RunAndGetNext(parentCtx)
        │ ⓪ 退化判定（§4）：Sequential 模式 / 调试标志 / 试运行标志 / 单分支 / ForceSequential
        │     → 命中任一 → 一期平铺路径原样执行，直接 return
        │ ① 扇出前令牌检查：parentCtx.CancellationToken 已取消 → 容器 Skipped，return
        │    （与既有 :45-48 逐位一致——评审中危 10 修复：扇出前才判 Skipped）
        │ ② using groupCts = CancellationTokenSource.CreateLinkedTokenSource(parentCtx.CancellationToken)
        │    （using 保证 Dispose——评审高危 1 修复；父令牌是会话级 :331 建 :439 才收，
        │     不释放则连续运行每轮挂一份注册）
        │ ③ int failureSource = -1; Exception firstFault = null;   ← 失败源登记槽（CAS）
        │ ④ 快照种子 + 每分支 ParallelBranchExecutionContext（记账字典，§3.5）
        │ ⑤ 调度（评审高危 6 修复）：
        │     D = min(n-1, Environment.ProcessorCount-1)
        │     分支[0..D-1] → Task.Run(Body)；分支[n-1] → 父线程就地执行
        │     （每层至少一个前进者不占池 → 嵌套并行饥饿链断开）
        │ ⑥ 分支体 Body（整体 try/catch——任务永不 faulted，§3.1）
        │ ⑦ 可中断 join（评审中危 11 修复）：WaitAll(tasks, 轮询步进) 循环等待 + deadline
        │ ⑧ 裁决（§3.4）→ 变量合并（§3.5）→ groupCts 释放（using）
        │ ⑨ 若 failureSource ≥ 0：ExceptionDispatchInfo.Capture(firstFault).Throw() 原样上抛
        │ ⑩ 若某分支 Return：parentCtx.CurrentFlowState = Return 上抛给顶层执行器
        └─ return null（并行组自带汇合，不向父序列交出子清单——与 For/While 同款骨架）
```

**join 为什么同步**：调用链全同步（CompiledFlow.Run → RunSequence → RunAndGetNext），引入 async 会传染整个引擎；引擎线程本就是线程池线程（F10），同步等待无死锁风险。**运行期防护**（评审高危 6）：join 入口检查 `SynchronizationContext.Current != null` 时记 Warn（未来新入口在 UI 线程直接调引擎时立刻暴露）。

---

## 3. 详细设计

### 3.1 调度器与 CTS 生命周期

**groupCts 生命周期（评审高危 1）**：

```
using (var groupCts = CancellationTokenSource.CreateLinkedTokenSource(parentCtx.CancellationToken))
{
    // 扇出、执行、join、裁决、合并——全部路径都在 using 块内
}   // 任何路径（含 join 上抛失败源异常）都保证 Dispose
```

创建计数/释放计数（`Interlocked.Increment` 的静态调试计数器，`[Conditional("DEBUG")]` 语义对外只读属性）供断言 P5 验证"连续运行 N 轮后 Created==Disposed"。

**调度并发度（评审高危 6）**：

- `D = min(n-1, Environment.ProcessorCount-1)`：每并行组提交给线程池的瞬时 Task 数 ≤ D
- **最后一条分支由 join 线程就地执行**：父线程不空等；嵌套并行时每层至少有一个"前进者"不占池线程，饥饿链从结构上断开（8×8 嵌套从 ~73 池线程降到 ≤ 8×7=56 提交上限、实际每层 join 线程消化一条 → 池内同时等待数远小于分支总数）
- 分支 Task 体**整体 try/catch**：任务永不 faulted（WaitAll 不会提前抛 AggregateException 绕过合并），一切结果（含异常）记入 `ParallelBranchResult`，join 处统一裁决

**失败源登记（评审高危 2）——本方案确定性的根基**：

```
分支体 Body(i)：
  try {
      RunSequence(branch[i], branchCtx[i], yieldToControlFlow: true);
      outcome = groupCts.IsCancellationRequested ? BranchOutcome.Cancelled : 按步骤态判定;
  }
  catch (OperationCanceledException) when (groupCts.IsCancellationRequested) {
      // 协作取消的常态噪音：兄弟失败/外部停止引发的取消
      outcome = BranchOutcome.Cancelled;      // 异常不上抛、不参与裁决
  }
  catch (Exception ex) {
      if (Interlocked.CompareExchange(ref failureSource, i, -1) == -1) {
          // 我抢到了"失败源"身份：我是真正触发取消的那个
          firstFault = ex;
          groupCts.Cancel();
          outcome = BranchOutcome.Failed;
      } else {
          // 兄弟已先失败，我的异常是被取消连带引发的（OCE 已被上一条 catch 接走，
          // 走到这里的是"取消后仍抛非 OCE"的罕见路径）——降级为取消噪音
          context.Logger?.Warn($"并行分支 {i} 在取消后仍抛出异常（已降级，失败源为分支 {failureSource}）: {ex.Message}");
          outcome = BranchOutcome.Cancelled;
      }
  }
```

效果：**join 上抛的永远是"真正触发 Cancel 的那个分支"的异常**——被取消分支的 OCE 不可能顶掉真故障源（FlowEngineService 的 catch(OperationCanceledException) :383-386 判正常取消、真错误静默吞掉的窗口被堵死）。

**可中断 join（评审中危 11）**：

```
deadline = Now + JoinTimeout（默认 15s，可配）
while (!Task.WaitAll(tasks, 500ms)) {
    if (parentCtx.CancellationToken.IsCancellationRequested && Now > 短宽限) break;  // 外部停止
    if (Now >= deadline) { 记 Error 日志（容器名+各分支终态快照）；容器标 Failed；放弃等待 return; }
}
```

- 超时放弃口径（写死）：**容器 Failed + Error 日志 + 不再等分支**——已启动的分支可能还在写自己影子（影子与父域隔离，父域安全），会话收尾按既有路径走；**不阻塞会话替换**（RuntimeManager.StopSessionGracefully 的 3 秒等待不会被并行组拖住，僵尸会话不被并行放大）
- "并行组不可无限期拖住会话替换"列为硬约束（§9）
- 放弃后未收尾分支的 StepModel 写入风险：放弃路径只发生在"算子不协作取消"的病态场景（既有风险，R1），并行节点不再叠加无限等待

**StepModel pending 记账加固（评审中危 11）**：`StepModel.SetRuntimeState` 的 `_pendingRuntimeNotify` List（:180-184 无锁读写）加锁（`lock (_notifyLock)`）或改为不可变快照替换——分支线程与 UI 线程并发触碰时不再有撕裂/丢通知。改动面：StepModel 私有记账段约 15 行重写（§7）。

### 3.2 FailFast 三态与全局配置（用户裁决 1 + 评审高危 3/4）

**语义定义**：FailFast = "并行分支业务失败（步骤 Success=false）时，是否取消其余兄弟分支"。

**FailFast 不做的事（评审高危 3，定死）**：**不产生异常、不上抛、不置会话 Faulted。** 业务失败只做两件事——（若生效）取消兄弟分支 + 容器终态标 Failed（由轮末 EscalateContainerFailures 上浮，F11）。任何配置下：连续运行下一轮照常启动（while 循环不跳出，FlowEngineService.cs:349-380）、会话不进 Faulted、HTTP 按容器状态呈现（200 + 容器 Failed，而非 500）。理由：CompiledPluginNode.cs:44-51 业务失败不抛异常，引擎拿不到异常对象；编造异常上抛 = 把业务失败升级成系统故障，"跑一次就停"。

**三态模型（评审高危 4）**：

```csharp
public enum FailFastMode { Inherit = 0, On = 1, Off = 2 }   // Inherit=0：存量 .vms 缺字段反序列化 → 0 → 兼容

// ParallelStep 新增落盘属性
public FailFastMode FailFastMode { get; set; } = FailFastMode.Inherit;
```

**全局配置节（AppConfigModel 新增）**：

```csharp
public class ParallelExecutionSettings
{
    /// <summary>并行分支业务失败时默认是否取消其余分支（FailFastMode=Inherit 的容器取此值）</summary>
    public bool FailFastByDefault { get; set; } = false;   // false = 一期行为（P20 兼容断言守得住）
}

// AppConfigModel 内：
/// <summary>并行执行全局设置。必须带 = new() 初始化器（AppConfigModel.cs:356-384 三次踩坑：缺节 null）</summary>
public ParallelExecutionSettings ParallelExecution { get; set; } = new();
```

**生效值解析（运行期，不是编译期）**：

```csharp
// Core\Threading\GlobalParallelConfig.cs（新增，静态策略）
public static class GlobalParallelConfig
{
    private static volatile bool _failFastByDefault;
    public static bool FailFastByDefault  // volatile 读写（评审低危 15）
    {
        get => _failFastByDefault;
        set => _failFastByDefault = value;
    }
}

// CompiledParallelNode 运行时解析：
bool effective = FailFastMode switch {
    FailFastMode.On  => true,
    FailFastMode.Off => false,
    _                => GlobalParallelConfig.FailFastByDefault
};
```

- **为什么运行期解析**：编译期读配置会传染 21 处 `new FlowCompiler(` 调用点（FlowCanvasChecks 15 处 + HttpImageSmoke + tools/SolutionProbe 等，本次 grep 实测 18 处、v1 口径 21 处含 csproj 引用面——实现期以实际为准）；且断言宿主（FlowCanvasChecks）在 bin 目录跑，编译期读 AppConfig.json 会在断言环境写文件、破坏密闭性。运行期解析 = 改配置下一轮生效、无需重编译。
- **装配点**：`AppSettingsService.Load()/Save()` 末尾同步 `GlobalParallelConfig.FailFastByDefault = Current.ParallelExecution?.FailFastByDefault ?? false;`——与 RunWindowMode 同口径（App.xaml.cs:139 RegisterSingleton\<AppSettingsService\>，:105/:242 等处 `Container.Resolve<AppSettingsService>().Current` 惰性取值范式）。

### 3.3 分支终态模型

```csharp
public enum BranchOutcome
{
    Success,        // 正常完成，全步骤成功
    BusinessFailed, // 正常完成，但分支内有步骤标 Failed（业务失败）
    Failed,         // 异常失败且本分支是失败源（§3.1 CAS 登记）
    Cancelled,      // 被取消打断（外部停止 / 失败源取消 / FailFast 取消 / 超时放弃时未收尾）
    Return,         // 分支内 Return 节点触发（终止整个流程的全局指令）
    FlowSignal      // 分支顶层孤儿 Break/Continue 被就地消化后的正常收尾（视同 Success 合并）
}
```

判定次序（分支体收尾处，同线程）：
1. §3.1 的 catch 链先判定 Failed/Cancelled；
2. RunSequence 正常返回：branchCtx.CurrentFlowState == Return → **Return**（同时 groupCts.Cancel() 取消兄弟）；非 Normal（Break/Continue 孤儿）→ **FlowSignal**；否则 groupCts.IsCancellationRequested → **Cancelled**；否则步骤态有 Failed → **BusinessFailed**（若 FailFast 生效则此前已 Cancel，见下）；否则 **Success**。
3. **BusinessFailed 的 FailFast 联动**：分支收尾判定出 BusinessFailed 且 effectiveFailFast == true → `groupCts.Cancel()`（只取消兄弟，**不登记 failureSource、不记 Failed、不上抛**——评审高危 3 定死的口径）。

**BusinessFailed 检测依据**：分支步骤清单在同一线程 ExecuteBranch 调 RunSequence 前后访问，`ParallelStep.Children[i].Steps` 的 State 检查无并发问题（步骤只被本分支线程写，F8）。

### 3.4 join 裁决规则

join 完成（全部分支有终态或按 §3.1 超时放弃）后，父线程**纯函数式**裁决（评审低危 15：抽成纯函数 `Adjudicate(IReadOnlyList<ParallelBranchResult>, parentCancelled) → ContainerVerdict`，断言 P6 表驱动直接喂桩数据，不依赖真实并发）：

| 优先级 | 条件 | 容器终态 | 上抛 | 变量合并 |
|---|---|---|---|---|
| 1 | failureSource ≥ 0（存在 Failed 分支） | Failed | `ExceptionDispatchInfo.Capture(firstFault).Throw()` 原样上抛（→ 父层 For/While 标 Failed 再抛 → 会话 Faulted，F5/F10 同路） | **跳过**（父域保持快照——半合并比不合并危险） |
| 2 | 任一分支 Return | Success（Return 是指令性正常出口，与 CompiledReturnNode 标 Success 同口径） | 不抛异常；`parentCtx.CurrentFlowState = Return` 交顶层终结流程 | 合并（含 Return 分支已完成写入，见 §3.5） |
| 3 | 扇出前 parentCtx 令牌已取消（§2 步骤①） | Skipped | — | 不合并（父域快照） |
| 4 | join 超时放弃（§3.1） | Failed | Error 日志；不抛（不阻塞会话替换） | 不合并 |
| 5 | 其余（全 Success / 含 BusinessFailed / FlowSignal / Cancelled 混合） | 由轮末 EscalateContainerFailures 按步骤态上浮（有失败步骤 → 容器 Failed；否则 Success） | — | 合并（仅 Success/BusinessFailed/FlowSignal/Return 分支，§3.5） |

**join 窗口停止口径（评审中危 10）**：**扇出后**（分支已启动）才收到外部停止 → 走规则 5（容器按真实结果裁决、分支步骤态如实呈现），**不标 Skipped**——"全部分支跑完、步骤全绿、恰在 join 期间停止"标 Skipped 是"这一步没跑"的假象，与顺序路径同窗口报 Success 不一致。外部停止的变量合并语义见 §3.5（全 Cancelled → 无可合并）。

### 3.5 变量影子与记账合并（评审高危 7/8）

**v1 缺陷**：与父域快照 diff 判定"分支写了什么"——(a) 漏报同值写入（写回与快照相等的值视为未写，"≥2 分支写同名⇒Warn"承诺失效）；(b) 若要精确记账必须换掉 LocalVariables 实例，与"ExecutionContext 零改动"矛盾；(c) Equals 可能抛。

**v2 修复：记账字典**。

```csharp
// Core\Models\Compileds\BranchVariableRegistry.cs（新增）
internal sealed class BranchVariableRegistry : IDictionary<string, object>
{
    private readonly Dictionary<string, object> _store;    // 影子存储（种子=父域浅拷）
    internal readonly HashSet<string> _writtenKeys = new(); // 写入记账（分支线程独占写，join 后父线程读）

    // 所有写路径（set 索引器 / Add / Remove）先 _writtenKeys.Add(key) 再落 _store
    // 读路径直通 _store
    public IReadOnlyCollection<string> WrittenKeys => _writtenKeys;
}
```

- 插件零改动：既有代码写 `context.LocalVariables[name] = value`（VariableAssignmentPlugin.cs:146/154 等）被装饰器透明拦截记账
- **ExecutionContext.LocalVariables 修饰符调整（唯一改动，1 行）**：`{ get; }` → `{ get; protected init; }`（ExecutionContext.cs:88）——`IExecutionContext` 契约零改动（Shard\Core.Interfaces 不动），ParallelBranchExecutionContext 子类构造时替换为记账字典
- 冲突告警按**真实写入集合**统计（同值写入也计数——P9b 断言"两分支写同名同值必须告警"，按值比较的实现会红）
- 合并不做值比较（无 Equals 抛出风险——评审高危 7(c) 从根上消除）

**合并表（评审高危 8 加"分支终态→是否合并"维度）**：

| 分支终态 | 是否合并影子写入 | 是否参与冲突告警 |
|---|---|---|
| Success | ✅ 合并 | ✅ |
| BusinessFailed | ✅ 合并（失败分支的已完成写入是真实结果，容器反正会标 Failed） | ✅ |
| FlowSignal | ✅ 合并（视同 Success） | ✅ |
| Return | ✅ 合并（**二选一写死：合并**——Return 分支的写入是"流程正常出口前的最后产出"，顶层终结后下游虽不跑，但 HTTP 输出收集/结果查看会读父域，丢弃会造成"明明算出来了却读不到"） | ✅ |
| **Cancelled** | ❌ **整体丢弃**（半成品写入覆盖父域会让下游取值随取消时机漂移——偶发跑错分支、现场不可复现；丢弃时 Warn 变量名+原因） | ❌ |
| Failed（失败源） | ❌（规则 1 整体跳过合并，自然丢弃） | ❌ |

- 合并顺序：分支声明序 0→n 串行，后分支覆盖先分支（确定性）
- 新建键（种子无此 key）直接并入父域
- 外部停止（全 Cancelled）→ 无分支可合并 → 父域保持快照（正确语义）

### 3.6 状态上报与焦点（评审低危 14）

**并行节点不变量（写进类注释）**：
1. **分支步骤只写自己 StepModel 的 State/耗时，绝不触碰 session.FocusedStep**——含 Skipped 路径（CompiledNode.cs:254-258 的 Skipped 分支同样读写 FocusedStep，分支被取消时必然进来）；
2. **容器只由父线程写**（Running/终态在扇出前/join 后的父线程上报）；
3. 分支内 CurrentNodeId 写的是 branchCtx 实例字段（F2 实例隔离），父上下文停在容器 Id。

**实现方式（评审低危 14 修正）**：`UpdateStepRuntimeState` 改成**整方法级分支快捷路径**——方法开头判定 `context is ParallelBranchExecutionContext`：是 → 只做 `step.State = 映射` + Running 时 BeginTiming / 完成时 EndTiming，**整个方法提前 return，绝不进入焦点交接段**（含 :254-258 Skipped 分支）；否 → 既有逻辑原样。这不再是"+8 行"而是对既有方法的**重构**（§7 改口）。

**焦点呈现**：并行期只显示容器焦点——容器置 Running 时拿走 IsRunningFocus（交接代码 :243-247 只在父线程执行），分支内步骤照常上报 State（两条泳道的步骤都在变色），汇合后焦点随下一个父序列节点移交。副作用：并行期间分支步骤的实时耗时数字不刷新（UI 定时器只 tick 焦点步骤），完成时冻结的最终耗时准确——可接受（v1 结论存活）。

### 3.7 Break / Continue / Return 在并行分支内的语义（v1 存活，v2 复述）

- **Break/Continue 只终结本分支**：分支跑自己的 RunSequence(branch, yield:true)——分支内的 For/While 照常消化自己循环体里的指令；Break 出现在分支顶层（无循环包裹）→ 分支提前收束记 FlowSignal + Warn"并行分支顶层出现无循环归属的 Break/Continue"（与顶层孤儿指令同款哲学，CompiledNode.cs:207-210）。
- **Return 是全局的**：终止整个流程 → 该分支记 Return → groupCts.Cancel() 取消兄弟 → join 后 parentCtx.CurrentFlowState = Return 交顶层终结（CompiledNode.cs:199 处），容器标 Success。
- **与一期的差异如实声明**：一期平铺语义下分支内 Break 会截断整个后续序列；二期它只终结本分支。这是语义修正而非破坏（一期行为是平铺实现的副作用）；二期默认 Sequential 模式下一期行为原样保留。

---

## 4. 退化路径与向后兼容（用户裁决 3 + 主会话口径 4）

**退化判定（RunAndGetNext 开头，任一命中 → 一期平铺路径）**：

| # | 条件 | 来源 |
|---|---|---|
| 1 | `ExecutionMode == Sequential`（默认/存量） | v1 存活 |
| 2 | 分支数 == 1 | v1 存活 |
| 3 | `session.DebugDegradedParallel == true`（新增会话标志，见下） | 用户裁决 3 |
| 4 | `GlobalParallelConfig.ForceSequential == true`（进程级总闸，默认 false，现场排障用） | v1 存活 |

**退化标志的置位（主会话口径 4：四入口退化矩阵）**：

| 入口 | 退化？ | 机制 |
|---|---|---|
| 界面调试运行（RunSessionAsync/OnceAsync 且 debugSession=true） | ✅ 退化 | 抢到会话锁后 `session.DebugDegradedParallel = true`（与 session.DebugEnabled 同点位同时机，FlowEngineService.cs:328/:509），finally 收尾复位 false |
| 试运行（PluginTestRunner，目标为容器节点） | ✅ 退化 | PluginTestRunner.Run 在 ExecuteChain 前对 trialSession 置 true，运行完复位（F23：临时会话从不置 DebugEnabled——**调试豁免路径对试运行无效，必须单独置位**） |
| 非调试运行（HTTP / 定时 / 变量触发 / 界面普通运行） | ❌ 真并发 | 不置标志 |
| 断言宿主（ExecHarness 直调 Engine.Run） | ❌ 真并发 | 不置标志（P1 等并发断言依赖） |

CompiledParallelNode 退化判定只认 `session.DebugDegradedParallel` 一个标志（置位职责在两个入口，判定单点——避免"认两个标志"的判定逻辑分叉）。

**为什么调试不并发**：单步/断点的语义在真并发下没有良定义（DebugStepPending 单次消费、PauseReason 单值、Focus 单指针）；退化后调试门保持单线程，F6 全部时序钉子原样成立，`DebugChecks` 既有 11 组断言（F25）零改动。退化时记一条 Info 日志"并行组 'X' 在调试会话中按顺序执行"。

**ExecutionMode 兼容**：枚举 `Sequential=0, Parallel=1`，默认 Sequential——存量 .vms 缺字段反序列化取 0 → 一期行为逐位不变。新建容器（画布拖入）显式置 Parallel。`ExecutionMode`/`FailFastMode` 都是语义属性（非 [RuntimeState]）：修改走 FlowModel 版本链触发重编译（P22 断言"改属性→Version++→重编译"）。

---

## 5. 编译期门禁（评审高危 5 + 用户裁决 2）

### 5.1 inParallelBranch 环境传播

```csharp
private List<CompiledNode> CompileSteps(
    IEnumerable<StepModel> models, string? flowName,
    Dictionary<Guid, IVisionPlugin> pluginLookup,
    Dictionary<Guid, CompiledNode> nodeLookup,
    List<CompilationError> errors,
    bool inParallelBranch = false)   // 新增参数
```

传播规则（FlowCompiler.cs:255 起的全部递归调用点 :317/:389/:483/:526 同步透传）：

| 编译场景 | 传入值 |
|---|---|
| 顶层步骤序列（Compile 入口 :229） | false |
| ParallelStep（ExecutionMode==Sequential）的分支 :526 | false（顺序容器不门禁） |
| **ParallelStep（ExecutionMode==Parallel）的分支 :526** | **true**（唯一置 true 入口） |
| If / While / For / 内层容器 / 内层 Parallel 的子步骤（:317/:389/:483 及内层 Parallel 的分支） | **继承外层值**（inParallelBranch 原样透传） |

**覆盖两个 v1 绕过口**：① 分支内 If/For/While 里藏未标注算子（继承 true → 查）；② 外层 Parallel 分支内的内层组（含 Sequential 内层组）——内层节点仍与其它外层分支真并发（继承 true → 查）。

### 5.2 门禁检查与报错定位

CompileSteps 创建 CompiledPluginNode 处（:628 附近）：

```csharp
if (inParallelBranch && !IsParallelSafe(plugin.GetType()))
{
    errors.Add(Err(model,   // Err 带 StepModel 定位（CompilationError.StepId，CompilationResult.cs:17）
        $"[并行不安全] 分支内步骤 '{model.StepName}'（算子 {plugin.GetType().Name}）未声明 [ParallelSafe]，" +
        $"不能进入并行执行模式的并行分组（请将其移出并行组，或将并行组切回顺序模式）。" +
        $"位置：{DescribePosition(topology, position)}，分支序号 {branchIndex}"));
}
```

- **特性匹配按 FullName 字符串**（不是 typeof 等值）：

```csharp
private static bool IsParallelSafe(Type t) =>
    t.GetCustomAttributes(false).Any(a => string.Equals(
        a.GetType().FullName, "Core.Interfaces.ParallelSafeAttribute",
        StringComparison.Ordinal));
```

理由：AGENTS.md 红线 2"两份 Core.Interfaces.dll"场景下程序集身份不一致，typeof 等值判 false——"明明标了还报不安全"。FullName 字符串匹配不受程序集身份影响。

- **报错定位**：Err(pluginNode.Blueprint ?? model, ...) 定位到分支内**具体步骤**（不是容器），文案复用 DescribePosition/DescribeScope 的「容器名/分支名→本层第 N 步」口径（FlowCompiler.cs:1262-1291 既有实现，报并行容器内分支时自然带出「并行组名/分支 i」路径）。
- **容器/控制流节点（If/For/While/Break/Continue/Return/内层 Parallel）不受限**——引擎自产代码，线程安全由本设计保证。

**硬件类算子**（用户裁决 2）：ImageAcquisition（相机模式）、Motion.Steps 全家、海康/网络相机设备工程、CSharpScript/ImageScript（用户脚本不可静态审计）通过"不标 [ParallelSafe]"自然落入本门禁，无需单独名单。

### 5.3 变量写冲突静态检查（评审中危 9：Runtime + Global 双口径）

同一 ParallelStep（ExecutionMode==Parallel）下，多个 `VariableAssignmentPlugin` 步骤的 `VariableName` 端口可静态解析出同名常量（值非链接）：
- **Scope=Runtime** → `[并行变量写冲突]` 编译错误（v1 口径存活）；
- **Scope=Global** → `[并行全局变量写冲突]` 编译错误（v2 扩展——F13 已核对写路径无锁，LocalVariableModel.Value setter 直接触发 ValueChanged 多播，通信回写/SCADA/监视订阅者被多线程调用）；
- 变量名来自链接/脚本等运行期来源 → 无法静态判定，运行期 Warn 兜底（§3.5 冲突告警只覆盖 Runtime 影子；**Global 写无运行期拦截**——写全局的插件已被标注清单挡在并行分支外，见 §6）。

图纸纪律（写进文档与工具提示）：**并行分支内避免写全局变量**；确需写时每轮只允许一个分支写同一变量。

---

## 6. [ParallelSafe] 插件标注清单（主会话口径 5：Plugins\ 27 工程逐个打勾）

标注审查口径：插件类内是否存在 static 可变字段且无锁保护 + 是否触碰进程级/配置级资源 + 是否写全局共享状态。

| # | 工程 | 标注 | 依据 |
|---|---|---|---|
| 1 | Plugin.BeadInspect | ✅ | 纯视觉计算，实例隔离 + HALCON 局部对象 |
| 2 | Plugin.BlobDetect | ✅ | 同上 |
| 3 | Plugin.Calibration | ✅ | 同上 |
| 4 | Plugin.CaliperMeasure | ✅ | 同上 |
| 5 | Plugin.Camera.Hikvision | ❌ | 硬件相机设备宿主（用户裁决 2） |
| 6 | Plugin.Camera.Network | ❌ | 网络相机帧队列，资源独占 |
| 7 | Plugin.CodeReader | ✅ | 纯视觉计算 |
| 8 | Plugin.ColorCheck | ✅ | 纯视觉计算 |
| 9 | Plugin.ColorRegion | ✅ | 纯视觉计算 |
| 10 | Plugin.CreateRoi | ✅ | 纯视觉计算 |
| 11 | Plugin.CSharpScript | ❌ | 用户脚本任意代码不可静态审计（F18：引擎 Cache 有锁，但脚本体无约束） |
| 12 | Plugin.DataRecord | ✅ | **实证安全**：RecordingHub 全 BlockingCollection + 进程级单消费线程串行（RecordingHub.cs:85-96），并行多分支入队天然串行化（主会话口径 5） |
| 13 | Plugin.ExcelExport | ❌ | **实证不安全**：XlsxHyperlinkInjector 整文件重写无锁 + 固定 tmp 抢占（XlsxHyperlinkInjector.cs:203-232，tmp = 目标路径+".tmp"，两分支写同路径互踩）（主会话口径 5） |
| 14 | Plugin.ImageAcquisition | ❌ | 硬件采集（用户裁决 2；文件夹/单文件模式其实安全，按插件粒度整体拒绝，三期细分） |
| 15 | Plugin.ImageAssign | ❌ | **实证不安全**：写全局变量 = 共享状态写（ImageAssignPlugin.cs:61-68 GlobalVariables.TryWrite），并行写同名全局变量冲突（主会话口径 5） |
| 16 | Plugin.ImageScript | ❌ | 用户脚本，同 CSharpScript |
| 17 | Plugin.Matching | ✅ | EnsureModelLoaded static 但只改本实例条目（F18） |
| 18 | Plugin.Motion.Steps | ❌ | 运动控制硬件步骤（用户裁决 2） |
| 19 | Plugin.Motion.Virtual | ❌ | 虚拟运动设备宿主，与 Motion 语义同族，保守不标 |
| 20 | Plugin.Motion.ZMotion | ❌ | 正运动卡设备宿主，硬件 |
| 21 | Plugin.Ocr | ✅ | 纯视觉计算 |
| 22 | Plugin.PoseTransform | ✅ | 纯视觉计算 |
| 23 | Plugin.PreProcessing | ✅ | 纯视觉计算 |
| 24 | Plugin.ResultUpload | ✅ | **实证安全**：UploadQueue 进程级单消费线程串行 + SpoolLock（UploadQueue.cs:86/:336/:376/:437）（主会话口径 5） |
| 25 | Plugin.RunFlow | ❌ | **评审中危 12**：子流程独立编译不受门禁保护（FlowInvoker.cs:81-84），三期资源锁落地前经 RunFlow 进入的子流程无并行保障 |
| 26 | Plugin.Utility | ✅ | 纯计算类（Math/Logic/Comparison/DataConversion/Delay）；VariableAssignment 写 Runtime 走记账字典透明兼容，写 Global 有 §5.3 编译检查兜底 |
| 27 | Plugin.Yolo | ✅ | YoloSession.Cache 带 CacheLock（F18） |

**合计**：✅ 16 / ❌ 11。

**风险明示（评审中危 12）**：三期资源锁落地前，**经 RunFlow 进入的子流程不受并行门禁保护**——禁止在并行分支调用含采集/运动的子流程（P23 断言：两分支同时调用同一子流程 → 一支确定性失败，文案含"未执行"，FlowInvoker.cs:77-78 既有门禁兜底）。

---

## 7. 改动清单（评审低危 14：如实改口"重构"非"+8 行"）

### 新增

| 文件 | 内容 |
|---|---|
| `Shard\Core.Interfaces\ParallelSafeAttribute.cs` | `[ParallelSafe]` 特性（与 StepConfigAttribute 同目录同范式，Core.Interfaces 命名空间；空标记，XML 注释写清判定口径与协作取消要求） |
| `Core\Models\Compileds\ParallelBranchExecutionContext.cs` | 分支上下文：继承 ExecutionContext（复用完整构造 :118-124），挂 BranchIndex / 记账字典；LocalVariables 在构造时替换（protected init） |
| `Core\Models\Compileds\BranchVariableRegistry.cs` | 记账字典（IDictionary 样板 + 写登记，~120 行） |
| `Core\Models\Compileds\ParallelBranchResult.cs` | 分支终态记录（Outcome / Exception / WrittenKeys / 分支序号） |
| `Core\Threading\GlobalParallelConfig.cs` | 静态策略（FailFastByDefault / ForceSequential，volatile 字段） |
| `FlowCanvasChecks\ParallelExecutionChecks.cs` | §8 断言 + 桩插件（SafeGatePlugin 闸门桩 / ThrowPlugin / BizFailPlugin / VarWritePlugin / SlowVarReadPlugin / FocusProbePlugin 焦点探针桩） |

### 修改

| 文件 | 改动 | 量级 |
|---|---|---|
| `Core\Models\ProcessStep\ParallelStep.cs` | 增 `ExecutionMode`（enum，Sequential=0 默认）与 `FailFastMode`（enum，Inherit=0 默认）两个落盘属性 | 小 |
| `Core\Models\ExecutionContext.cs` | `LocalVariables` 修饰符 `{ get; }` → `{ get; protected init; }`（**全文件唯一改动，1 行**；IExecutionContext 契约零改动） | 微 |
| `Core\Models\Compileds\CompiledParallelNode.cs` | **RunAndGetNext 重写**（重构）：退化判定 → groupCts using → 失败源槽 → 调度（D=min(n-1,核-1)，末分支就地）→ 分支体 try/catch → 可中断 join → 纯函数裁决 → 记账合并 → 上抛/Return；平铺旧路径保留为私有方法 | 大（~200 行） |
| `Core\Models\Compileds\CompiledNode.cs` | `UpdateStepRuntimeState` **重构**：方法头判定分支上下文 → 整方法级快捷路径（State+timing only，绝不触碰 FocusedStep 含 Skipped），既有逻辑原样保留给父线程 | 中（重构，非 +8 行） |
| `Core\Models\FlowSession.cs` | 增 `DebugDegradedParallel` 标志（~5 行） | 微 |
| `Core\Models\ProcessStep\StepModel.cs` | `_pendingRuntimeNotify` 记账段加锁/不可变快照（:180-184/:220-228 重写，~15 行） | 小 |
| `Engine\FlowCompiler.cs` | CompileSteps 增 `inParallelBranch` 参数 + 全部递归调用点透传（:317/:389/:483/:526）+ Parallel 分支置 true + 门禁检查（FullName 匹配 + DescribePosition 定位）+ 双口径变量冲突检查 | 中（~60 行） |
| `Engine\FlowEngineService.cs` | debugSession 入口置位/复位 DebugDegradedParallel（:328/:509 附近 + finally，两处入口 ~8 行） | 小 |
| `Engine\PluginTestRunner.cs` | 试运行入口置位/复位 DebugDegradedParallel（~4 行） | 微 |
| `Core\AppSettingsService.cs` | Load/Save 同步 GlobalParallelConfig（~2 行） | 微 |
| `Core\Models\AppConfigModel.cs` | 增 `ParallelExecutionSettings` 类 + `ParallelExecution` 属性（**必须 `= new()`**，:385 前插入，同 HttpImageServer 范式） | 小 |
| 16 个可标插件工程 | 类头加 `[ParallelSafe]`（每工程主插件类 1 行） | 微 |
| `Plugins\Plugin.DataRecord\RecordingHub.cs` | `:406` 裸读 `_started`（声明 :95）改 `Volatile.Read`（评审低危 15 顺手项，1 行） | 微 |
| 画布/属性面板（UI） | 容器属性编辑暴露 ExecutionMode/FailFastMode；TypeBadge 按 mode 显示"并行/顺序"；工具箱 [ParallelSafe] 提示 | UI 期工作，随二期交付但不属引擎断言范围。**2026-10-09 已落地：入口=流程栏双击组头，能力=分组名/分支改名增删/执行模式/失败聚合/汇合超时；见 `docs\code-changes\2026-10-09-并行分组参数面板（草稿事务-白名单分派-分支卡片交互闭环）.md`** |

### 不改动

CompiledPluginNode（业务失败/异常路径零改动——FailFast 联动在并行节点收尾判定，不进插件节点）、CompiledFlow、If/While/For/Break/Continue/Return 节点、HttpImageServer、ResourceLockService、PerformanceMonitor（F13：已线程安全且未接线节点计时）、IExecutionContext 契约（Shard 零改动除新增特性文件）、VariableRegistry/WorkspaceManager（全局写风险靠标注清单+编译检查挡，不加锁）。

---

## 8. 断言清单（P1-P30：延续 v1 P1-P22 编号并按评审逐条扩充语义，新增 P23-P30；旧编号保留映射关系便于对照 v1 评审稿）

新增 `FlowCanvasChecks\ParallelExecutionChecks.cs`，注册进 `Program.cs`（:90 `ParallelContainerChecks.Run()` 之后一行）。桩插件纪律照 ExecutionHarness 顶部注释（真类型 + AssemblyQualifiedName、端口全 IsRequired=false、无输入口）；GatePlugin 式闸门桩构造并发窗口（ExecutionHarness.cs:180-204：Reached/Proceed 双事件）。**一期 V3 顺序断言（ParallelContainerChecks.cs:152-155）在默认 Sequential 下原样转绿 = 兼容性证明（P20 端到端复证）。**

| # | 机制 | 断言 |
|---|---|---|
| P1 | 真并发 | 分支1 SafeGatePlugin（堵住等放行）、分支2 计数桩；**前置：GatePlugin.Reached.Wait 确保分支1已到闸门**；断言：分支2 计数>0 且分支1 未放行（顺序语义下不可能） |
| P2 | 同步 join | Engine.Run(ctx) 返回后两分支计数均已落定（Run 内部完成 join） |
| P3 | 令牌链/停止 | 并行执行中 StopSession → 会话 Stopped、**分支步骤态如实**（已完成的保持 Success、未到的 Idle）、PauseLock 回放行态、收尾干净；**会合点用 InGroup 事件**（并行节点进入扇出后 Set 的 ManualResetEventSlim 桩内事件），确保停的是"真并发窗口"不是 join 后 |
| P4 | 失败取消-异常 + **故障源** | 分支1 ThrowPlugin 抛异常、分支2 SafeGatePlugin 等待；断言：分支2 闸门被令牌唤醒提前退出、容器 Failed、**上抛的必须是源分支异常**（Message 含分支1 标记）；**两分支同时异常时**（双闸门构造窗口）上抛的仍是 failureSource 登记的那个、被取消分支的 OCE 不得成为上抛物——多跑 20 次全一致 |
| P5 | **groupCts 无泄漏** | 连续运行 100 轮并行后 Created==Disposed（调试计数器直读；连续运行经 Engine.Run 循环触发） |
| P6 | 汇合裁决纯函数 | `Adjudicate` 抽纯函数后**表驱动断言**：构造各 BranchOutcome 组合（全 Success / 含 BusinessFailed / 含 Failed / 含 Return / 全 Cancelled / 混合）直接喂桩数据断言裁决结果——不依赖真实并发时序 |
| P7 | FailFast=On | 分支1 BizFailPlugin（Success=false）、分支2 闸门桩；断言：分支2 被取消（闸门令牌唤醒）、容器 Failed、**会话不 Faulted、无异常上抛、连续运行下一轮照常启动**（单一期望值，去掉 v1"或 BusinessFailed 终态"的或然判据） |
| P8 | 影子隔离 | 父域 X=1；分支1 VarWritePlugin 写 X=2；分支2 SlowVarReadPlugin 读 X；断言：分支2 读到 1 |
| P9 | 汇合合并 + 记账 | 分支1 写 X=2，分支2 写 X=3；并行组后读取桩；断言：读到 3（声明序后胜）+ Warn 含"X"与分支数。**P9b：两分支写同名同值（都写 X=2）必须告警**（记账制可见；按值比较 diff 的实现此用例必红——评审高危 7(a) 钉死） |
| P10 | 新建变量汇入 | 分支1 CreateIfNotExists 建变量 Y；断言：并行组后父域可读 Y |
| P11 | 分支内 If/For 取影子 | 分支内 If（条件引用运行时变量 X）+ For（LoopCount 引用运行时变量）；断言：求值取影子值、循环圈数正确 |
| P12 | 编译门禁-直接 | 未标注桩算子进 Parallel 分支 → 编译错误含"[并行不安全]"且 **StepId 指向具体步骤**（非容器）；同图纸 ExecutionMode=Sequential → 编译成功 |
| P13 | 门禁-嵌套绕过 | **两条绕过用例**：① 分支内 If/循环体里藏未标注算子 → 编译错误；② 外层 Parallel 分支内的内层组（Sequential/内层 Parallel）里藏未标注算子 → 编译错误（评审高危 5） |
| P14 | 变量冲突静态检查 | 两个常量名同名 VariableAssignment 进 Parallel 分支：**Runtime → 编译错误"[并行变量写冲突]"；Global → 编译错误"[并行全局变量写冲突]"**（评审中危 9 双口径）；变量名来自链接 → 无编译错误（P9 运行期兜底） |
| P15 | 调试退化 | DebugDegradedParallel 会话跑 Parallel 图纸 + OrderPlugin；断言：顺序执行（分支1→分支2）、日志含"调试会话中按顺序执行"、断点照常命中 |
| P16 | **试运行退化** | PluginTestRunner 试运行并行容器目标（F23 路径）→ DebugDegradedParallel 被置位 → 顺序执行、运行完复位（主会话口径 4） |
| P17 | Break 屏障 | 分支1 顶层 Break（并行组置于 For 循环体内）、分支2 计数桩；断言：外层 For 不跳出、分支2 当圈完整执行、Warn 出现"无循环归属" |
| P18 | 分支内循环消化 Break | 分支1 内嵌 For + 循环体 Break；断言：只跳出分支内循环，分支后续节点照跑、兄弟分支不受扰 |
| P19 | Return 全局 | 分支1 Return、分支2 阻塞桩；断言：分支2 被取消、容器 Success、父层 CurrentFlowState=Return、顶层终止；Return 分支写入已合并（§3.5 写死） |
| P20 | 兼容性总证 | 一期 V3 图纸（无 ExecutionMode/FailFastMode 字段序列化往返）→ 编译成功 + 顺序执行 + 容器 Success；**新 AppConfig 默认（无 ParallelExecution 节）→ FailFastByDefault=false = 一期行为**（评审高危 4） |
| P21 | 焦点口径 | **FocusProbePlugin 探针桩在真并发窗口内采样记录**（桩内采样并写入线程局部集合，join 后统一断言——消除跨线程采样竞态，评审低危 15）；断言：并行期间容器 IsRunningFocus=true、分支内 Running 步骤 IsRunningFocus=false 且 State=Running；join 后焦点随下一节点移交 |
| P22 | 属性版本链 | 改 ParallelStep.ExecutionMode → FlowModel Version++ → 重编译生效（正例断言，评审低危 15） |
| P23 | RunFlow 子流程并行 | 两分支同时调用同一子流程 → 一支确定性失败、文案含"未执行"（FlowInvoker.cs:77-78 既有门禁；评审中危 12）——已落地（2026-10-10，`ParallelExecutionChecks.cs`） |
| P24 | FailFastMode 三态矩阵 | **三态 × 全局 on/off 全矩阵**（Inherit+off→不取消 / Inherit+on→取消 / On+off→取消 / Off+on→不取消）+ **改 GlobalParallelConfig 后下一轮生效无需重编译**（评审高危 4） |
| P25 | **受限池压力** | `ThreadPool.SetMinThreads(2,2)` 临时压低后跑嵌套并行图纸（外层 Parallel 分支内嵌 Parallel）：在合理时间内完成且无饥饿卡顿（秒级），结束 try/finally 复位 MinThreads + **复位后并行行为恢复断言**（P1 手段复跑）+ GlobalParallelConfig 读写 Volatile（评审高危 6 + 低危 15） |
| P26 | ForceSequential 总闸 | 置 true 后 Parallel 图纸顺序执行；try/finally 复位 + 复位后并发恢复（P1 手段复跑，评审低危 15） |
| P27 | join 超时放弃 | 构造不协作取消的阻塞桩（桩内死等不放令牌）+ 缩短 JoinTimeout → 超时路径：容器 Failed、Error 日志、**不阻塞**（Run 返回） |
| P28 | 取消分支影子丢弃 | FailFast 场景：分支1 BizFail → 分支2 写 X 后被取消 → 断言父域 X 保持快照（被取消分支写入不可见）+ 丢弃 Warn；停止场景（P3 图纸复用）：父域全部保持快照（评审高危 8）——已落地（2026-10-10，`ParallelExecutionChecks.cs`） |
| P29 | 存量调试回归 | `DebugChecks.Run()` 全部 11 组原样绿（F25，调试门零改动的回归证明） |
| P30 | 单分支退化 | Parallel 模式 + 1 条分支 → 平铺执行，结果与 Sequential 一致 |

桩插件纪律照 `ExecutionHarness` 顶部注释。预计新增断言 60~80 条（P4/P6/P24 的表驱动与矩阵展开）；全套以 `dotnet run --project FlowCanvasChecks` 跑。**性能基线口径**：一期记录的 1620 通过 / 4 既有环境失败是 v1 时点文档引用数字；二期实现期先实测当前基线，再按 P 清单增量核对——文档引用与实测数字分开表述，不混用。

---

## 9. 风险登记表

| 风险 | 等级 | 缓解 |
|---|---|---|
| 算子不响应令牌 → 分支挂死 | 中（既有风险并行放大） | [ParallelSafe] 标注审核把"协作取消"列为检查项（DelayPlugin 50ms 步进是范式）；P27 超时放弃路径兜底（容器 Failed + 不拖会话替换）；ForceSequential 总闸现场一键回退 |
| **并行组无限期拖住会话替换（硬约束：不允许）** | 中 | 可中断 join + deadline（§3.1）；StopSessionGracefully 3 秒语义不被并行组改变（P27） |
| 全局变量并行写（值路径无锁，已核对） | 中 | 编译期双口径冲突检查（§5.3）+ 标注清单把写全局的插件挡在并行分支外（ImageAssign ❌）+ VariableAssignment 的 Global 写有编译检查 + 图纸纪律；CSharpScript 本身进不了并行分支 |
| 经 RunFlow 进入的子流程不受门禁保护 | 中 | RunFlow 不标 [ParallelSafe]（§6）+ 文档明示禁令 + P23 断言既有会话锁兜底；三期资源锁后解除 |
| [ParallelSafe] 标注漏审 | 中 | F18 检索范式逐插件过 static 可变字段；摘特性即回退（门禁自动挡） |
| 嵌套并行线程放大 | 低 | D=min(n-1,核-1) + join 线程就地执行末分支（§3.1）+ P25 受限池压力断言 |
| 记账字典样板实现遗漏写路径 | 低 | IDictionary 全接口覆盖 + P9/P9b/P28 断言主要写路径 |
| 退化标志残留 true | 低 | 置位/复位包 try-finally；P16 断言复位 |
| 同步 join 在非池线程被调（未来新入口） | 低 | join 入口运行期检查 SynchronizationContext.Current != null → Warn（§2） |
| HALCON 算子内部线程安全 | 低 | HALCON 对独立 HObject 实例并发调用官方支持；一期拓扑已禁跨分支共享图像对象 |

---

## 10. 三期演进路径（设计先行，本期不实现）：硬件资源锁（用户裁决 2）

1. `IExecutionContext` 增加 `IResourceLockService ResourceLocks { get; }` 递送成员（与 Cameras/Motions 同范式：默认 NullResourceLockService，IExecutionContext.cs 既有注释即该范式说明书）——Shard/Core 两处契约改动。
2. 硬件算子 `RunAlgorithm` 首行按设备粒度 `TryAcquireLock(resourceKey, out var handle, sessionId)`（非阻塞）：抢到 → `try { ... } finally { handle.Dispose(); }`；抢不到 → `Fail($"硬件 'X' 正被并行分支占用")` 明确失败（不静默等待——等待会伪装成串行成功，节拍预期全错）。
3. resourceKey 约定：`Camera:{序列号}`（采集/相机插件）、`Motion:{卡标识}:{轴名}`（Motion.Steps）。不同键并行放行（两台相机真并发）、同键第二分支立即失败。
4. 届时把对应插件改标 `[ParallelSafe]`，门禁自动放行；RunFlow 子流程门禁同法收口（子流程编译时透传父流程的 inParallelBranch 语义或运行期资源锁兜底）。

---

## 11. 已裁决事项归档（v1 开放问题，全部关闭）

| v1 开放问题 | 裁决 | 来源 |
|---|---|---|
| 失败聚合口径（业务失败默认是否触发取消） | 全局配置项 `ParallelExecutionSettings.FailFastByDefault`（默认 false）+ 容器三态覆盖；FailFast 只做提前取消不做异常升级 | 用户裁决 1 + 评审高危 3/4 |
| 硬件算子策略 | 二期编译期拒绝（不标 [ParallelSafe]）+ 三期资源锁（§10） | 用户裁决 2 |
| 调试期并行退化口径 | 界面调试 + 试运行双入口退化顺序执行（§4 四入口矩阵） | 用户裁决 3 + 主会话口径 4 |
