using System;
using System.Linq;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IScadaFlowRunner"/> 的默认实现：把"启动/停止/执行一次这条流程"这句话
    /// 交给流程引擎去办。
    ///
    /// 为什么需要这一层
    /// ---------
    /// 动作分发器是长命单例，它只认领域层的 <see cref="IScadaFlowRunner"/>（不许碰引擎类型）；
    /// 而"取会话 → 判新鲜度 → 编译 → 注册 → 启动"这串事必须握着
    /// <see cref="IRuntimeManager"/> / <see cref="IFlowEngine"/> / <see cref="FlowCompiler"/>
    /// 才能办。本类就是中间那根线头——与 <see cref="ScadaNavigator"/>（切页出口）同一个角色：
    /// 薄适配 + 找不到流程安静返回。
    ///
    /// 启动配方与 ShellViewModel.RunAllEnabledContinuous 同源，改动两处必须同步
    /// ---------
    /// 会话准备已收口到 <see cref="FlowSessionFactory.TryEnsureSession"/>（第二批收口，引擎审查结论）：
    /// 查会话 → 判新鲜度[流程身份 FlowID + 版本] → 必要时重编译 → 注册。ShellViewModel 那条链
    /// 之外的新触发源（HTTP 收图、定时调度、本类）都走它——口径只写一份，新门禁才只需动一处。
    /// 本类额外守的两道门与既有触发源同口径，<b>只守启动类方法</b>（StartContinuous / RunOnce）：
    /// - <see cref="FlowModel.StepsEncrypted"/>：加密流程不给跑（FlowAutoRunner / 定时判定 /
    ///   流程列表单跑，三处先例）；
    /// - <see cref="FlowModel.IsEnabled"/>：停用的流程不给启动（"配了却不该跑"的配置）。
    /// - <b>Stop 豁免这两道门</b>（2026-10-12 架构裁决）：工业现场的停止/急停入口永远要能按——
    ///   启动被停用/加密挡是对的，停止不能挡；找不到流程仍是 false（存在性门与门禁是两回事）。
    /// - <see cref="FlowModel.HasBreakpoint"/>：流程里有断点时自动按调试会话启动（方案 A，2026-10-10）。
    ///
    /// 线程与任务
    /// ---------
    /// 入口只被动作分发器调用（UI 线程）。连续启动的循环任务火忘（<see cref="IFlowEngine.RunSessionAsync"/>
    /// 内部全量捕获异常、收尾放锁，与主界面"运行"同一写法——循环会话的任务只有被停止后才结束，
    /// 绝不能 await 它）。引擎唯一落在捕获网之外的是入口<b>前置</b>的 <c>ArgumentException</c>
    /// （session 或 ExecutionEngine 为空时直接抛，不进 try）：那一档经本类<b>不可达</b>——
    /// 会话只会出自 <see cref="FlowSessionFactory.TryEnsureSession"/>，编译成功产出的会话必带执行引擎，
    /// 空引用到不了引擎入口。单次执行挂一个后续回执（见 <see cref="AttachOnceReceipt"/>），
    /// 回执<b>只记日志、不落审计</b>：<see cref="ScadaAuditWriter"/> 明文约定不锁、只许 UI 线程，
    /// 续结跑在线程池上，落审计会撞出交错的行。审计在"指令被受理"的那一次（分发器同步落）已经记了。
    /// </summary>
    public sealed class ScadaFlowRunner : IScadaFlowRunner
    {
        private readonly IRuntimeManager _runtime;
        private readonly IFlowEngine _engine;
        private readonly FlowCompiler _compiler;
        private readonly IWorkspaceManager _workspace;
        private readonly ILogService _log;

        public ScadaFlowRunner(
            IRuntimeManager runtime,
            IFlowEngine engine,
            FlowCompiler compiler,
            IWorkspaceManager workspace,
            ILogService log)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <inheritdoc/>
        public bool StartContinuous(string flowId, string? fallbackName, out string? reason)
        {
            var flow = ResolveFlow(flowId, fallbackName, out reason);
            if (flow == null)
                return false;

            var gate = CheckStartable(flow);
            if (gate != null)
            {
                reason = gate;
                return false;
            }

            // 幂等在会话准备之前先判一次：已在跑的流程不该被"判新鲜度 → 重编译"这套流程惊动
            //（运行中重建会话本来就会被 FlowSessionFactory 拒掉，但那时报的是"不能重建"，
            // 而不是这里该说的"已在运行"）
            var existing = _runtime.GetSessionByName(flow.FlowName);
            if (existing?.IsRunning == true)
            {
                reason = "已在运行，未重复启动";
                return true;
            }

            if (!FlowSessionFactory.TryEnsureSession(_runtime, _compiler, flow, out var session, out var sessionError, _log))
            {
                reason = sessionError;
                return false;
            }

            if (session.IsRunning)
            {
                reason = "已在运行，未重复启动";
                return true;
            }

            // 方案 A（2026-10-10）：有断点才按调试会话启动（装调试门），否则普通运行（并行组真并发）。
            // 连续运行的"抢不到锁"没有返回值可判：引擎的抢锁与 IsRunning 置位都在首个 await
            // 之前同步完成，所以调用返回后立刻取快照就是准的（与主界面同一判法，不猜时序）。
            // 引擎对循环任务自捕异常、收尾放锁，这里火忘（不能 await：循环只有被停止才结束）。
            _ = _engine.RunSessionAsync(session, flow.HasBreakpoint());

            if (!session.IsRunning)
            {
                reason = "未启动：会话正被其它触发占用（HTTP / 定时 / 手动）";
                return false;
            }

            reason = "已启动（连续运行）";
            return true;
        }

        /// <inheritdoc/>
        public bool RunOnce(string flowId, string? fallbackName, out string? reason)
        {
            var flow = ResolveFlow(flowId, fallbackName, out reason);
            if (flow == null)
                return false;

            var gate = CheckStartable(flow);
            if (gate != null)
            {
                reason = gate;
                return false;
            }

            // 单次语义不幂等：正在跑的时候这一遍无从发生，必须说清楚而不是静默排第二遍
            //（连续启动的幂等理由在这里恰好相反——操作员必须知道"这一遍没跑成"）
            var existing = _runtime.GetSessionByName(flow.FlowName);
            if (existing?.IsRunning == true)
            {
                reason = "流程正在运行，本次未执行";
                return false;
            }

            if (!FlowSessionFactory.TryEnsureSession(_runtime, _compiler, flow, out var session, out var sessionError, _log))
            {
                reason = sessionError;
                return false;
            }

            if (session.IsRunning)
            {
                reason = "流程正在运行，本次未执行";
                return false;
            }

            // 有断点 → 调试单次（断点/单步生效），与主界面单次运行同一判法。
            // Try 版本如实回答"这一单跑没跑上"：返回 false（IsRunning 不置位）有两档——
            // 会话被其它执行占用、流程被 ResourceLockService 锁定，两档下面这个快照一并拦住，
            // reason 也就必须把两档都盖上（只写"被占用"会把"被锁定"报成没影的事）。
            var onceTask = _engine.TryRunSessionOnceAsync(session, flow.HasBreakpoint());

            if (!session.IsRunning)
            {
                reason = "未启动：会话被其它执行占用或流程被锁定";
                return false;
            }

            AttachOnceReceipt(onceTask, flow.FlowName);

            reason = "已下发单次执行";
            return true;
        }

        /// <inheritdoc/>
        public bool Stop(string flowId, string? fallbackName, out string? reason)
        {
            var flow = ResolveFlow(flowId, fallbackName, out reason);
            if (flow == null)
                return false;

            // 刻意不调 CheckStartable：停止/急停入口永远要能按（2026-10-12 架构裁决）——
            // 停用/加密只挡"让它跑"，不挡"让它停"；找不到流程在上面已经 false（存在性门仍在）。
            var session = _runtime.GetSessionByName(flow.FlowName);
            if (session == null || !session.IsRunning)
            {
                // 幂等：操作员要的"它停下来"这个状态已经成立。停止键永远不该报错。
                reason = "本就未在运行";
                return true;
            }

            // 引擎对"未运行/CTS 已释放"的会话静默收手（契约③），这里只管把话带到
            _engine.StopSession(session);

            reason = "已发出停止";
            return true;
        }

        /// <inheritdoc/>
        public bool Pause(string flowId, string? fallbackName, out string? reason)
        {
            var flow = ResolveFlow(flowId, fallbackName, out reason);
            if (flow == null)
                return false;

            var session = _runtime.GetSessionByName(flow.FlowName);
            if (session == null || !session.IsRunning)
            {
                reason = "未在运行，无法暂停";
                return false;
            }

            // 停止请求已发出、执行循环还没收尾：此刻 State 仍是 Running，PauseSession 会把
            // 它置成 Paused 并报成功，但流程马上就会落 Stopped——审计写“已暂停”、实际已停止。
            if (session.CancellationTokenSource?.IsCancellationRequested == true)
            {
                reason = "流程正在停止";
                return false;
            }

            // 单次执行没有“下一轮再继续”这回事：引擎的 PauseSession 对它只能置一个
            // 没人消费的 Paused 状态，操作员看到“已暂停”而流程其实已经跑完/正在收尾。
            // 所以这里明确拒绝，让分发器记一条 Warn，而不是报假成功。
            if (!session.IsContinuousRun)
            {
                reason = "单次执行不支持暂停";
                return false;
            }

            // 幂等：已经暂停的再点一次不算失败，但也不能说成“刚刚暂停成功”
            if (session.State == SessionState.Paused)
            {
                reason = "本就已暂停";
                return true;
            }

            if (session.State != SessionState.Running)
            {
                reason = $"当前状态（{session.State}）不可暂停";
                return false;
            }

            _engine.PauseSession(session);

            // 引擎对“状态已变 / 正在收尾”的请求会静默收手（契约③）；不校验状态就会
            // 把“引擎什么也没做”报成“已暂停”，操作员据此以为设备停了，是最危险的假成功。
            if (session.State == SessionState.Paused)
            {
                reason = "已暂停";
                return true;
            }

            reason = "暂停未生效（引擎未进入暂停状态）";
            return false;
        }

        /// <inheritdoc/>
        public bool Resume(string flowId, string? fallbackName, out string? reason)
        {
            var flow = ResolveFlow(flowId, fallbackName, out reason);
            if (flow == null)
                return false;

            var session = _runtime.GetSessionByName(flow.FlowName);
            if (session == null || !session.IsRunning)
            {
                reason = "未在运行，无法恢复";
                return false;
            }

            // 停止请求已发出：恢复会报“本就未暂停/已恢复”，但流程正在收尾，马上就是 Stopped。
            if (session.CancellationTokenSource?.IsCancellationRequested == true)
            {
                reason = "流程正在停止";
                return false;
            }

            // 幂等：本来就在跑（没暂停）的再点一次恢复不算失败，但说清“本就未暂停”
            if (session.State == SessionState.Running)
            {
                reason = "本就未暂停";
                return true;
            }

            if (session.State != SessionState.Paused)
            {
                reason = $"当前状态（{session.State}）不可恢复";
                return false;
            }

            // 未暂停的运行中流程是幂等空操作：引擎的 ResumeSession 自己会守“没有等待门”。
            _engine.ResumeSession(session);

            if (session.State == SessionState.Running)
            {
                reason = "已恢复";
                return true;
            }

            reason = "恢复未生效（引擎未回到运行状态）";
            return false;
        }

        /// <summary>
        /// 在当前方案里解析目标流程：<b>Id 优先、名字兜底</b>（OrdinalIgnoreCase），与
        /// 值源/切页出口同一套寻址口径。找不到时 reason 给出按哪个键找的、没找到谁。
        /// </summary>
        private FlowModel? ResolveFlow(string? flowId, string? fallbackName, out string? reason)
        {
            reason = null;

            var flows = _workspace.CurrentSolution?.Flows;
            if (flows == null || flows.Count == 0)
            {
                reason = "当前方案里没有流程";
                return null;
            }

            if (!string.IsNullOrWhiteSpace(flowId))
            {
                var byId = flows.FirstOrDefault(f =>
                    f != null && string.Equals(f.FlowID, flowId, StringComparison.Ordinal));
                if (byId != null)
                    return byId;
            }

            if (!string.IsNullOrWhiteSpace(fallbackName))
            {
                var byName = flows.FirstOrDefault(f =>
                    f != null && string.Equals(f.FlowName, fallbackName, StringComparison.OrdinalIgnoreCase));
                if (byName != null)
                    return byName;
            }

            // 展示口径：有名字报名字（人看得懂），只有 Id 报 Id（手写/旧数据）
            reason = $"找不到流程「{(string.IsNullOrWhiteSpace(fallbackName) ? flowId : fallbackName)}」";
            return null;
        }

        /// <summary>
        /// <b>启动类方法</b>（StartContinuous / RunOnce）的前两道门：停用 / 加密。
        /// 返回 null = 放行，否则为可直接展示的原因。与 FlowAutoRunner（定时/变量触发）、
        /// 流程列表单跑同一口径——加密的意义就是"内容不给看、也不许被跑"，只在界面侧拦
        /// 会让这条链上出现例外。<b>Stop 不走这里</b>（豁免裁决见类注释与 Stop 方法注释）。
        /// </summary>
        private static string? CheckStartable(FlowModel flow)
        {
            if (!flow.IsEnabled)
                return $"流程「{flow.FlowName}」已停用";

            if (flow.StepsEncrypted)
                return $"流程「{flow.FlowName}」步序已加密，无法运行";

            return null;
        }

        /// <summary>
        /// 单次执行的后续回执：跑完 Info 一行、没跑上/异常 Warn 一行。
        ///
        /// 为什么只记日志、不落审计：<see cref="ScadaAuditWriter"/> 明文约定"不锁、
        /// 只许 UI 线程"（两条线程同时 Append 同一文件，出来的是谁也不是的行），
        /// 而本回执跑在线程池上。审计该记的（谁在什么时候点了哪个按钮、指令受没受理）
        /// 分发器在"指令被受理"那一刻已经同步记了；跑完与否是过程信息，运行日志是它的家。
        /// </summary>
        private void AttachOnceReceipt(System.Threading.Tasks.Task<bool> onceTask, string flowName)
        {
            onceTask.ContinueWith(t =>
            {
                if (t.IsCanceled)
                {
                    _log.Warn($"[组态事件] 流程「{flowName}」单次执行被取消");
                    return;
                }

                if (t.IsFaulted)
                {
                    // 结局由引擎侧反映（日志/状态事件）；这里只消化任务异常，留一条带来源的线索
                    _log.Warn($"[组态事件] 流程「{flowName}」单次执行异常结束：{t.Exception?.GetBaseException().Message}");
                    return;
                }

                if (!t.Result)
                    _log.Warn($"[组态事件] 流程「{flowName}」单次执行未启动：会话正被其它执行占用");
                else
                    _log.Info($"[组态事件] 流程「{flowName}」单次执行完成");
            }, System.Threading.Tasks.TaskScheduler.Default);
        }
    }
}
