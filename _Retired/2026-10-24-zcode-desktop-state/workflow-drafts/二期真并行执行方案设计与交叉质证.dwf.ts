// 二期真并行执行：方案设计与交叉质证（多模型多阶段，模型切换由主会话用 AmendWorkflow 调度）
// 阶段 1（glm-5.3 方案设计）+ 阶段 2（deepseek-flash 评审+逐条确认）已完成；
// 本段 = 阶段 3：hy4-preview-f 第二线独立评审（不给它看 deepseek 结论，保持无锚定）。

interface ReviewVerdict2 {
  /** 评审结论：pass / pass-with-fixes / fail */
  verdict: "pass" | "pass-with-fixes" | "fail";
  /** 你核对过的方案决策（读了代码验证） */
  perDecision: { topic: string; ok: boolean; evidence: string; fixNeeded: string }[];
  /** 方案会出问题的地方：并发语义边界、实现工程师会被带偏的坑、断言测不住的机制，每条带代码证据 */
  findings: { what: string; why: string; evidence: string; severity: "high" | "medium" | "low"; fix: string }[];
  /** 三项用户裁决融入方案后的设计缺口 */
  userDecisionGaps: { decision: string; gap: string; fix: string }[];
  /** 方案断言清单可测性缺口 */
  assertionGaps: string[];
}

phase("第二线独立评审：换角度找方案缺口");
log("hy4-preview-f 独立评审（不参考第一线结论）：并发语义边界与实现陷阱");
const reviewer2 = agent("并发评审员-第二线", {
  system: "你是第二线并发方案评审员，与方案作者及任何其他评审员相互独立——没人给你预设结论。你的价值在于差异化视角：找方案作者盲区里的并发语义边界、实现工程师照着做会踩的坑、以及'设计看起来对但断言测不住'的机制。每个论断都要有文件行号证据；读不到的明说读不到。不要修改任何文件。诚实优先：不确定就标不确定。",
});

const review2 = await reviewer2.ask<ReviewVerdict2>(`
评审 D:\\C#\\VM\\docs\\方案设计\\parallel-execution-phase2-design.md（"流程引擎真并行执行二期"设计方案）。

**产出纪律（最重要）**：你的返回必须是非空的结构化对象——perDecision 至少 5 条、findings 至少 3 条（如果实在找不到 high，就报 medium/low，一条没有是不可能的：这是一份未经实现验证的并发设计，边界场景一定存在）。空返回等于没做评审。findings 为空数组会被上游直接判为无效评审丢弃重跑。

## 用户已裁决（按此口径评审）
1. 失败聚合默认值走全局配置项（AppConfig 决定并行分支业务失败是否默认取消兄弟分支）。
2. 硬件算子（相机/运动）进并行分支：编译期拒绝 + 三期做资源锁。
3. 调试运行遇并行组：就地退化顺序执行。

## 你的评审视角（与"逐行核对"互补的三块）
1. **并发语义边界**：方案的变量影子合并/失败状态机/取消传播/Break-Continue-Return 语义里，有哪些边界场景是方案没定义的？例如：分支内再嵌套并行组的令牌与合并如何嵌套作用；分支里定义运行时变量且容器之后有人消费（含 For.LoopCount 接变量）；合并冲突 Warn 与取消分支丢弃影子的交互；StopSession 到达瞬间各分支的不同进度下汇合状态的确定性。
2. **实现工程师陷阱**：照这份方案直接开写，哪里会被带偏？例如伪代码里没写但实现必须处理的细节（分支上下文构造参数、FocusedStep 抑制的具体改法、蓝图状态在分支线程写的时序）；方案承诺"零改动"的地方是否真的零改动。
3. **断言测不住的机制**：方案 §8 断言清单（P1-P22）里，哪些机制设计得再好断言也测不到（时序依赖/竞态窗口/只在故障路径出现的行为）？给出能测的替代判据。

## 必读文件（读全再评）
- 方案文档全文
- Core\\Models\\Compileds\\CompiledNode.cs / CompiledParallelNode.cs / CompiledIfNode.cs / CompiledForNode.cs / CompiledWhileNode.cs / CompiledBreakNode.cs
- Core\\ExecutionContext.cs、Core\\FlowSession.cs
- Engine\\FlowCompiler.cs（场景 A-3:499-545 与插件实例化:572-628）、Engine\\FlowEngineService.cs（:331-460 连续运行/:479-615 单次）、Engine\\FlowInvoker.cs、Engine\\PluginTestRunner.cs
- Plugins\\Plugin.Utility\\VariableAssignmentPlugin.cs、Plugins\\Plugin.RunFlow\\RunFlowPlugin.cs
- Core\\Models\\ProcessStep\\ParallelStep.cs、VisionMaster\\ViewModels\\ProcessViewModel.cs:495-514、Engine\\PluginService.cs:68-165

## 产出（结构化，中文）
verdict / perDecision（你核对过的决策，核几条报几条，至少 5 条）/ findings（按 severity 排序至少 3 条，每条带 what-why-evidence-fix）/ userDecisionGaps（至少 1 条）/ assertionGaps（至少 2 条）。`);

log("阶段 4：方案修订定稿——吸收两线评审全部已证实发现与用户裁决");
// 同名智能体：amend 缓存按名字匹配——"方案设计师-并发引擎"复用阶段 1 的完整上下文（它记得自己的一手设计）
const architect = agent("方案设计师-并发引擎", {
  system: "你是工业视觉软件的并发引擎架构师。你的方案必须落到这个仓库的真实代码上——每个决策都要引用你读到的具体文件与行号，禁止凭空设计。诚实纪律：读不到的就明说读不到，不确定的标注不确定；任何'检查过了'都必须真实发生过。本阶段允许修改的唯一文件是 docs/方案设计/parallel-execution-phase2-design.md（你的方案文档本身），其余文件一律只读。",
});
const reviseBrief = `<以下是对你方案的两线评审结论（第一线"找会崩点"+第二线"语义边界/实现陷阱/可测性"，各 finding 均经第三方独立复核证实），以及用户裁决与主会话新增口径。逐条修订进 docs/方案设计/parallel-execution-phase2-design.md，直接改文件，改完在文首把状态改为"设计定稿 v2（双线评审已吸收）"并列一节"v2 修订记录"逐条记录改动。

== 用户裁决（已定，不可改）==
1. 失败聚合默认值走全局配置项（AppConfig 决定并行分支业务失败是否默认取消兄弟分支）。
2. 硬件算子进并行分支：编译期拒绝 + 三期做资源锁。
3. 调试运行遇并行组：就地退化顺序执行。
主会话新增口径（按用户"编译期拒绝"的保守精神裁决）：
4. 试运行（PluginTestRunner）与界面调试同权重退化顺序执行——试运行目标为并行容器时会真并发（session 从不置 DebugEnabled），必须堵：PluginTestRunner 编译传门禁豁免 + 会话置退化标志，退化判定同时认两个标志。
5. 标注修订：ExcelExport、ImageAssign 不标 [ParallelSafe]（实证不安全）；DataRecord、ResultUpload 可标（进程级单消费串行实证安全）。不标清单以 Plugins\\ 目录逐工程打勾的表写进 docs。

== 必须修正的设计缺陷（全部经独立复核证实，含代码证据）==

【高危 1：groupCts 泄漏】你的伪代码 CreateLinkedTokenSource 后无 Dispose 落点；父令牌是会话级（FlowEngineService.cs:331 建 :439 收尾才 Dispose），连续运行每轮挂一份注册，长跑内存增长。复核员用 .NET 9 实测探针确认累积与回收边界。修法：using var groupCts + join 后释放；加 Created/Disposed 计数，断言"连续运行 N 轮后 Created==Disposed"。

【高危 2：故障源登记缺失】分支被取消时算子抛 OperationCanceledException 是协作取消常态，但你的裁决表按"分支声明序取第一"上抛 Faulted 异常——被取消分支的 OCE 会顶掉真故障源异常，FlowEngineService 的 catch(OperationCanceledException) 判为正常取消 → 会话 Stopped 非 Faulted，错误静默吞掉。修法：并行节点持共享槽 int failureSource=-1；分支捕获异常先判 groupCts.IsCancellationRequested，已取消且 Interlocked.CompareExchange 登记失败(非源) → 记 Cancelled（异常降级日志）；只有真正触发 Cancel 的分支记 Faulted(ex) 并上抛。P4 断言升级为"两分支同时异常时上抛的必须是源分支异常，被取消分支的 OCE 不得成为上抛物"。

【高危 3：FailFast 状态机自相矛盾】你写"业务失败→FailFast 是→记 Faulted(业务)"，但 BranchOutcome 枚举没有这个成员、业务失败无异常对象（CompiledPluginNode.cs:44-51 不抛、VisionPluginBase 默认 RethrowOnException=false），而裁决 1 要求 ExceptionDispatchInfo 原样上抛——实现者只能编造异常，后果是把业务失败升级成会话 Faulted：连续运行 while 循环整体终止（FlowEngineService.cs:347-381 一抛跳出循环，产线"跑一次就停"）+HTTP 200 变 500。两线评审独立发现同一矛盾。修法（定死）：FailFast 只做提前取消、不做异常升级——取消兄弟分支 + 容器标 Failed + 本轮正常收尾（连续运行下一轮照常启动、会话不进 Faulted、HTTP 按容器状态呈现）；P7 去掉"（或 BusinessFailed 终态）"的或然判据，写成单一期望值。

【高危 4：FailFastMode 三态 + 全局配置】只留 bool 时全局置"开"后任何容器都无法单独关掉；存量 .vms 缺字段=0 需兼容。修法：ParallelStep.FailFastMode { Inherit=0, On=1, Off=2 }（Inherit=0 兼容存量）；全局 AppConfigModel 新增 ParallelExecutionSettings 节（引用类型字段必须带 = new() 初始化器——AppConfigModel.cs:356-384 三次踩过缺节变 null 的坑）+ FailFastByDefault 默认 false（=一期行为，P20 才守得住）；读取时机=运行期（编译期读会传染 21 处 new FlowCompiler 调用点，且 FlowCanvasChecks 会在 bin 写 AppConfig.json 导致断言非密闭）；生效值=Inherit 取全局、On/Off 覆盖全局；装配在 App.xaml.cs AppSettingsService 注册点灌进静态策略（与 RunWindowMode 同口径）；断言：三态×全局 on/off 矩阵 + "改配置后下一轮生效无需重编译"。

【高危 5：编译门禁 ambient 化】门禁只查"直接父容器 ExecutionMode==Parallel"，但 CompileSteps 递归编译无"处于并行分支内"的环境状态——两个绕过口：分支内 If/For/While 藏未标注算子；外层 Parallel 分支内的 Sequential 内层组（内层节点仍与其它外层分支真并发）。修法：CompileSteps 增加 bool inParallelBranch 参数，进 Parallel 模式分支置 true 且被嵌套容器/内层并行组全部分支继承；凡 true 时编译出的 CompiledPluginNode 一律查 [ParallelSafe]。特性匹配按特性类型名（FullName 字符串）不是 typeof 等值——AGENTS.md 红线 2"两份 Core.Interfaces.dll"会打掉类型身份，表现为"明明标了还报不安全"。报错改 Err(pluginNode.Blueprint ?? model, ...) 定位到分支内具体步骤 + 分支序号（复用 DescribePosition/DescribeScope 口径）。断言补：藏在分支内 If/循环体、藏在 Sequential 内层组两条绕过用例 + 报错 StepId 指向具体步骤。

【高危 6：线程模型修正】"同步 join + 不限流"在嵌套并行下线程池饥饿（分支体同步占池线程到 join；8×8≈73 线程；CLR 注入每秒 1-2 条 → 秒级卡顿）。且分支 Task 体无 try/catch，WaitAll 首个异常直接抛 AggregateException 走不到合并。修法（二期直接落地你预留的内联降级）：每并行组只 Task.Run min(核数-1, n-1) 条，最后一条分支在 join 线程就地执行（每层至少一个前进者，饥饿链断开）；分支 Task 体整体 try/catch——任务永不 faulted，结果（含异常）全部记入 ParallelBranchResult，join 处统一裁决；"同步 join 非池线程"从 Debug 断言升级运行期检查（SynchronizationContext.Current != null 时 Warn）。断言：ThreadPool.SetMinThreads 临时压到 2 跑嵌套图纸的受限池压力断言（结束复位）。

【高危 7：合并规则重写】三个证实缺陷：(a) "与快照比对（引用/值相等⇒未写）"漏报同值写入——承诺"≥2 分支写同名⇒Warn"失效；(b) 要精确记账必须改 ExecutionContext.LocalVariables（:88 只读属性派生类换不掉字典实例），与你"ExecutionContext 零改动"承诺矛盾；(c) Equals 可能抛（字典可放任意 object），在父线程打断合并。修法：ExecutionContext.LocalVariables 改 { get; protected init; }（IExecutionContext 契约零改动，实现一处改动），分支上下文安装记账字典（写先登记 key）；合并与告警按真实写入集合；值比较包 try/catch（异常按已写处理+Warn）；已知假阴性"写回与快照相等的值视为未写"写进文档与断言注释。断言：P9b 两分支写同名同值必须告警（按值比较的实现会红）。

【高危 8：取消分支的影子处置】合并规则无"分支终态→是否合并"维度——被取消分支的半成品写入照样覆盖父域且参与冲突告警，下游取值随取消时机漂移（偶发跑错分支，现场不可复现）。修法：合并表加"分支终态→是否合并"列：Cancelled 分支影子整体丢弃（不合并不参与告警，丢弃时 Warn 变量名+原因）；只合 Success/BusinessFailed/FlowSignal 分支；Return 分支"已完成部分"是否合并二选一写死；外部停止→不合并。断言：FailFast 场景被取消分支写入不可见；停止场景父域保持快照。

【中危 9：全局变量口径统一】你 §3 不变量 5 说"编译期对同名全局写报错"但 §5 只查 Scope=Runtime，两节矛盾。已核对写路径无锁（LocalVariableModel.cs:85-123/GlobalVariableWriter.cs:55-82，写线程直接触发 ValueChanged，通信回写/SCADA/监视订阅者被多线程调用）。修法：编译期同名写冲突检查扩到 Scope=Global；运行期并行分支写全局 → Warn（变量名+分支）；"并行分支内避免写全局"写成图纸纪律；风险表"实现期待核对"改"已核对：值路径无锁"。

【中危 10：join 窗口停止口径】裁决 3"join 后父令牌已取消→Skipped"会让"全部分支跑完、步骤全绿、恰在 join 期间停止"也标 Skipped——"这一步没跑"的假象，与顺序路径同窗口报 Success 不一致。修法：扇出前已取消→Skipped（与现有 :45-48 逐位一致）；扇出后取消→容器 Success、分支步骤态如实呈现。P3 判据改"容器非 Failed、步骤态如实、PauseLock 回放行态、收尾干净"。

【中危 11：僵尸会话放大】StopSessionGracefully 3 秒放弃且不 Dispose（IRuntimeManager.cs:94-104/:141-143），新旧会话共享同一批 StepModel，_pendingRuntimeNotify 无锁 List（StepModel.cs:180-184/:220-228）——并行把一个僵尸放大成 N 线程僵尸。修法：join 改可中断语义（WaitAll 循环等待+deadline，超时记 Error 按明确口径放弃：容器 Failed+日志+不阻塞会话替换）；StepModel pending 记账加锁或不可变快照；"并行组不可无限期拖住会话替换"写进硬约束。

【中危 12：RunFlow 绕过通道】子流程独立编译不受门禁约束（FlowInvoker.cs:81-84），且方案把 RunFlow 列入可标清单。修法：文档+风险表明示"三期资源锁落地前，经 RunFlow 进入的子流程不受并行门禁保护，禁止在并行分支调用含采集/运动的子流程"；断言"两分支调用同一子流程→一支确定性失败（文案含未执行）"。

【中危 13：配置级资源】类型级 [ParallelSafe] 看不见目标路径：ExcelExport 整文件重写无锁+固定 tmp 抢占（XlsxHyperlinkInjector.cs:203-232）、ImageAssign 写全局变量（另有 9 管控）。DataRecord/ResultUpload 实证安全（进程级单消费线程串行、SpoolLock）。修法：按主会话新增口径 5 修订标注清单，并把 27 个 Plugins\\ 工程逐个打勾的表写进 docs。

【低危 14：UpdateStepRuntimeState 改动面】"ExecutionContext 零改动/分支线程完全不触碰 FocusedStep 交接段/约+8 行"三处承诺虚低：Skipped 分支（:254-258）同样读写 session.FocusedStep，分支被取消时必然进来。修法：抑制写成整方法级分支快捷路径（分支上下文只做 State+timing，绝不触碰 FocusedStep，含 Skipped）；§7 改动量改口"重构"非"+8 行"；"分支步骤只写自己 StepModel 的 State/耗时、容器只由父线程写"写成并行节点不变量。

【低危 15：杂项修正】DebugChecks 是 11 组不是 12；RecordingHub :406 有一处裸读 _started 改 Volatile.Read；性能基线数字（1620/4）实现期先实测再按 P 清单增量，文档引用与实测分开表述；P19 ForceSequential 断言 try/finally 复位+复位后并行行为恢复断言+读写 Volatile；P21 焦点断言改"探针桩在真并发窗口内采样记录"（跨线程采样有竞态）；ExecutionMode 加"改属性→Version++→重编译"正例断言；P6 汇合裁决抽纯函数表驱动断言；P1 前置会合点 GatePlugin.Reached.Wait；P3 会合点 InGroup 事件；DebugChecks/试运行的四入口退化矩阵（界面调试=退化/试运行=退化/非调试运行=真并发/HTTP=真并发）写进决策 10 + 退化优先级表。

== 交付要求 ==
- 修订后的方案必须达到"实现工程师照着做不需要再做设计决策"的密度；每条修订在 v2 修订记录里注明来源（两线评审/用户裁决/主会话口径）。
- 断言清单按上述逐条扩充并编号（P1-P22 可延续编号加 P23+，或重排，标注清楚）。
- 不改任何产品代码，只改方案文档。
- 返回：修订摘要（每条缺陷→修法一句话）、你修订后仍存疑的点（如有）。`;

const revision = await architect.ask<{ revisionSummary: string[]; remainingConcerns: string[] }>(
  `你现在收到两线评审的汇合结论与用户/主会话裁决，请据此把你的方案修订为 v2 定稿。\n${reviseBrief}`);

log("方案 v2 定稿完成");
return {
  revision,
  nextStage: "deepseek 实现（按 v2 定稿）→ 确定性 gate（构建+全量断言+新增断言）→ reviewer 独立审查",
};