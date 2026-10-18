using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace VisionMaster.Models
{
    /// <summary>
    /// 编译后的「等待」节点（BuiltIn_Wait）。
    ///
    /// 语义：进入本节点后按 <see cref="PollIntervalMs"/> 反复求值
    /// <see cref="Condition"/>（一个只含条件、没有执行步骤的 <see cref="CompiledBranch"/>），
    /// 条件成立即继续执行下一步；超过 <see cref="TimeoutMs"/>（0 = 不限）仍未成立时按
    /// <see cref="TimeoutAction"/> 终止流程或继续。
    ///
    /// 与 If / While 的关系：三者求值的是同一种 CompiledBranch，实参构建统一走基类
    /// <see cref="CompiledNode.BuildConditionArgs"/>——类型归一（CoerceConditionArg）与
    /// 无值兜底（DefaultForConditionArg）只有一处实现，本节点**不另写一份**。
    ///
    /// 三个失败口径（与仓库既定的"红着停机 &gt; 绿着走错"一致）：
    ///  · 条件求值抛异常 → Error 日志 + 标 Failed + <b>上抛</b>（绝不当成 false 继续等：
    ///    "等不到"与"算不出来"是两件事，静默当成 false 会一直等到超时，归因全错）；
    ///  · 超时 → 先落日志/报警变量/输出端口，再按配置终止（throw TimeoutException，
    ///    由 FlowEngineService 置会话 Faulted）或继续（返回 null）；
    ///  · 停止请求 → 标 Skipped 并返回（不写 TimedOut，也不抛——那是用户主动停的，不是超时）。
    /// </summary>
    public class CompiledWaitNode : CompiledNode
    {
        /// <summary>条件（只有条件 Lambda 与变量元数据，没有执行步骤）</summary>
        public CompiledBranch Condition { get; set; }

        /// <summary>上游连线映射（条件变量 Id -> 输出端口）</summary>
        public Dictionary<Guid, IOutputPort> UpstreamLinks { get; set; } = new();

        /// <summary>轮询间隔（毫秒），编译期已校验下限</summary>
        public int PollIntervalMs { get; set; } = 50;

        /// <summary>最大等待时长（毫秒）；<b>0 = 不限</b></summary>
        public int TimeoutMs { get; set; } = 60000;

        /// <summary>超时后的处置</summary>
        public WaitTimeoutAction TimeoutAction { get; set; }

        /// <summary>超时说明（追加到超时日志与异常文案）</summary>
        public string TimeoutMessage { get; set; }

        /// <summary>报警全局变量名（可空）：超时时写 true，写失败只记 Warn</summary>
        public string AlarmVariableName { get; set; }

        /// <summary>
        /// 条件表达式原文（仅用于日志/异常文案——Lambda 编译后拿不回源码文本）。
        /// 超时日志里必须能看出"在等什么"，否则现场只看到一句"等待超时"。
        /// </summary>
        public string ConditionExpression { get; set; }

        /// <summary>是否因超时结束等待（供后续 If 判断走异常分支）</summary>
        public OutputPort<bool> TimedOutPort { get; } = new("TimedOut");

        /// <summary>本次等待耗时（毫秒，超时与正常成立都给值）</summary>
        public OutputPort<int> ElapsedMsPort { get; } = new("ElapsedMs");

        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentNodeId = Id;

            UpdateStepRuntimeState(context, StepRuntimeState.Running);

            // 跨轮清零：同一个编译节点在连续运行里被反复复用，上一轮的两个输出若不清，
            // 本轮"还没跑完"就会被下游读到上一轮的 TimedOut / 耗时（假告警、假分支）。
            TimedOutPort.Value = false;
            ElapsedMsPort.Value = 0;

            BindContextAwarePorts(context);

            var stopwatch = Stopwatch.StartNew();

            while (true)
            {
                // 取消检查放在求值之前：停止请求一旦到达就立即退出，
                // 不会因为"再多算一次条件"把已取消的流程判成 Success。
                if (context.CancellationToken.IsCancellationRequested)
                {
                    // 取消路径同样要结束计时：UpdateStepRuntimeState(Skipped) 只释放焦点，
                    // 不调 EndTiming → LastRunTimeMs 停在 0、画布上看起来"这一步没花时间"。
                    Blueprint?.EndTiming();
                    UpdateStepRuntimeState(context, StepRuntimeState.Skipped);
                    return null;
                }

                bool isTrue;
                try
                {
                    isTrue = (bool)Condition.ConditionLambda.Invoke(
                        BuildConditionArgs(Condition, UpstreamLinks, context));
                }
                catch (Exception ex)
                {
                    // 与 CompiledWhileNode 同款：求值抛异常 = "等没等到"未知，
                    // 静默当成 false 会一直等到超时，归因指向"条件不成立"（错的）。
                    context.Logger?.Error(
                        $"等待节点 '{Name}' 条件求值失败，已中断流程: {ex.Message}"
                    );
                    UpdateStepRuntimeState(context, StepRuntimeState.Failed);
                    throw;
                }

                // 停止优先：求值期间停止请求到达时，这一次求值结果不许再被当成
                // Success / 超时（否则"点了停止却报成功"或"报了超时告警"）。
                if (context.CancellationToken.IsCancellationRequested)
                {
                    Blueprint?.EndTiming();
                    UpdateStepRuntimeState(context, StepRuntimeState.Skipped);
                    return null;
                }

                if (isTrue)
                {
                    TimedOutPort.Value = false;
                    ElapsedMsPort.Value = ClampElapsed(stopwatch.ElapsedMilliseconds);
                    UpdateStepRuntimeState(context, StepRuntimeState.Success);
                    return null;
                }

                // 先求值后判超时：条件恰好在这一刻成立时不会被误判成超时
                // （与 While"条件求值放在上限判断之前"同一条纪律）。
                long elapsedMs = stopwatch.ElapsedMilliseconds;
                if (TimeoutMs > 0 && elapsedMs >= TimeoutMs)
                    return HandleTimeout(context, elapsedMs);

                // 每轮只睡"到下次该求值的时刻"，且不超过剩余超时：
                //  · min(PollIntervalMs, 剩余) —— 剩余不足一个轮询周期时也要准时醒来判超时；
                //  · WaitOne(ms) 在停止请求到达时立即返回（CancellationToken.WaitHandle 被置位），
                //    不必等满一个轮询间隔，停止响应不受轮询间隔影响。
                long waitMs = PollIntervalMs;
                if (TimeoutMs > 0)
                    waitMs = Math.Min(waitMs, TimeoutMs - elapsedMs);

                // 剩余为 0（夹取后）也至少睡 1ms，避免极端配置下忙等自旋
                context.CancellationToken.WaitHandle.WaitOne((int)Math.Max(1, waitMs));
            }
        }

        /// <summary>
        /// 超时收尾：日志 → 报警变量 → 两个输出端口 → 状态，再按配置终止或继续。
        /// 顺序是刻意的：即使下一步要抛异常终止流程，日志/端口也已经落定，
        /// 现场能拿到"等了多久、在等什么"（抛异常后流程上下文不再可用）。
        /// </summary>
        private List<CompiledNode> HandleTimeout(IExecutionContext context, long elapsedMs)
        {
            // 超时说明与时长之间必须有分隔符：直接粘会读成
            // "超时（5000ms）等待料到位信号"（说明的第一句恰好以动词开头时尤其像同一句话）。
            string messageSuffix = string.IsNullOrWhiteSpace(TimeoutMessage)
                ? string.Empty
                : $"：{TimeoutMessage}";

            context.Logger?.Error(
                $"[等待超时] 等待节点 '{Name}' 等待条件 [{ConditionExpression}] 超时（{TimeoutMs}ms）{messageSuffix}"
            );

            if (!string.IsNullOrWhiteSpace(AlarmVariableName))
            {
                // 报警变量只是"顺带记一笔"：写失败（变量不存在 / 类型不是 bool）不该改变超时处置，
                // 否则一次配置疏漏会把"终止流程"变成"静默继续"（或反过来），比不写更危险。
                try
                {
                    if (!context.GlobalVariables.TryWrite(AlarmVariableName, true, out var error))
                    {
                        context.Logger?.Warn(
                            $"等待节点 '{Name}' 写报警变量 '{AlarmVariableName}' 失败：{error}"
                        );
                    }
                }
                catch (Exception ex)
                {
                    context.Logger?.Warn(
                        $"等待节点 '{Name}' 写报警变量 '{AlarmVariableName}' 抛出异常：{ex.Message}"
                    );
                }
            }

            TimedOutPort.Value = true;
            ElapsedMsPort.Value = ClampElapsed(elapsedMs);

            // 步骤状态一律 Failed：超时是事实，不能因为"配置成继续"就把这一步染绿
            //（否则画面上一片绿，现场看不出哪一步等超了；走异常分支由 TimedOut 端口表达）。
            UpdateStepRuntimeState(context, StepRuntimeState.Failed);

            if (TimeoutAction == WaitTimeoutAction.Continue)
                return null; // 流程继续：后续步骤可用 If 判断 TimedOut 走异常分支

            throw new TimeoutException(
                $"等待节点 '{Name}' 等待条件 [{ConditionExpression}] 超时（{TimeoutMs}ms）"
                    + (string.IsNullOrWhiteSpace(TimeoutMessage) ? string.Empty : $"：{TimeoutMessage}")
                    + "，已按配置终止流程。"
            );
        }

        /// <summary>耗时转 int 输出端口：超过 int 上限时夹取（避免负值；正常工况到不了这里）</summary>
        private static int ClampElapsed(long elapsedMs)
            => elapsedMs > int.MaxValue ? int.MaxValue : (int)elapsedMs;
    }
}
