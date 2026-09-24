using Core.Interfaces;
using DynamicExpresso;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Models
{
    /// <summary>
    /// 编译后的 While 循环节点
    /// 支持条件循环执行
    /// </summary>
    public class CompiledWhileNode : CompiledNode
    {
        /// <summary>
        /// 循环分支（包含条件和循环体）
        /// </summary>
        public CompiledBranch LoopBranch { get; set; }

        /// <summary>
        /// 上游连线映射（变量ID -> 输出端口）
        /// </summary>
        public Dictionary<Guid, IOutputPort> UpstreamLinks { get; set; } = new();

        /// <summary>
        /// 默认最大迭代次数（防止无限循环）
        /// </summary>
        private const int DefaultMaxIterations = 9999;

        /// <summary>
        /// 最大迭代次数（防止无限循环）
        /// </summary>
        public int MaxIterations { get; set; } = DefaultMaxIterations;

        /// <summary>
        /// 执行 While 循环
        /// 条件为真时执行循环体，支持 Break/Continue/Return 控制流
        ///
        /// A2：容器节点也要上报运行状态，否则画布上循环永不高亮、永无耗时，
        /// 现场排查时看不出"循环进没进、跑了几圈、卡在哪一圈"
        /// </summary>
        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentNodeId = Id;

            UpdateStepRuntimeState(context, StepRuntimeState.Running);
            try
            {
                RunLoop(context);
                UpdateStepRuntimeState(
                    context,
                    context.CancellationToken.IsCancellationRequested
                        ? StepRuntimeState.Skipped
                        : StepRuntimeState.Success);
            }
            catch
            {
                UpdateStepRuntimeState(context, StepRuntimeState.Failed);
                throw;
            }

            return null;
        }

        /// <summary>
        /// 循环主体：条件为假自然结束，或达到迭代上限 / 遇到 Break / Return / 取消时提前结束
        /// </summary>
        private void RunLoop(IExecutionContext context)
        {
            // 断桥修复：代理端口注入 context（每次执行本节点一次即可，context 在循环期间不变）
            BindContextAwarePorts(context);

            if (LoopBranch?.ConditionLambda == null) return;

            int iter = 0;

            // args 数组布局：先局部变量（LocalVarIds），再运行时变量（RuntimeVarNames）
            // 数组大小每次迭代固定，分配一次放循环外复用，循环体内只重填值
            int totalArgs = LoopBranch.LocalVarIds.Count + LoopBranch.RuntimeVarNames.Count;
            var args = new object[totalArgs];

            while (iter < MaxIterations)
            {
                if (context.CancellationToken.IsCancellationRequested) break;

                // 局部变量：从 UpstreamLinks 取值
                // P0-2/P0-3：取值一律经 CoerceConditionArg 按声明类型归一，无值走 DefaultForConditionArg。
                // 与 CompiledIfNode 保持同一口径（详见基类两个助手的注释）：
                // 编译期 IsLinkable 按 ValueConverter.Convert 放行"数值族互转"，
                // 而 DynamicInvoke 不做收窄（double → int 抛 ArgumentException），
                // 运行期不补这一步就会"编译通过、一跑就炸"。
                for (int i = 0; i < LoopBranch.LocalVarIds.Count; i++)
                {
                    Guid varId = LoopBranch.LocalVarIds[i];
                    Type expectedType = LoopBranch.VarTypes.ContainsKey(varId) ? LoopBranch.VarTypes[varId] : typeof(double);

                    if (UpstreamLinks.TryGetValue(varId, out var sourcePort) && sourcePort?.Value != null)
                        args[i] = CoerceConditionArg(sourcePort.Value, expectedType);
                    else
                        args[i] = DefaultForConditionArg(expectedType);
                }

                // 运行时变量：从 context.LocalVariables 取值
                for (int i = 0; i < LoopBranch.RuntimeVarNames.Count; i++)
                {
                    string varName = LoopBranch.RuntimeVarNames[i];
                    int argIndex = LoopBranch.LocalVarIds.Count + i;
                    Type expectedType = LoopBranch.RuntimeVarTypes.ContainsKey(varName) ? LoopBranch.RuntimeVarTypes[varName] : typeof(object);

                    if (context.LocalVariables.TryGetValue(varName, out var v) && v != null)
                        args[argIndex] = CoerceConditionArg(v, expectedType);
                    else
                        args[argIndex] = DefaultForConditionArg(expectedType);
                }

                bool isTrue = false;
                try { isTrue = (bool)LoopBranch.ConditionLambda.Invoke(args); }
                catch (Exception ex)
                {
                    // P1-4：条件求值抛异常 = 循环该不该继续未知，静默 break 等于"悄悄少跑几圈"，
                    // 步骤状态还是绿的，比停机报警危险得多（与 CompiledIfNode 的失败语义对齐）。
                    // 这里保留 Error 日志以交代发生在哪一圈，随后上抛，由 RunAndGetNext 标 Failed。
                    context.Logger.Error(
                        $"While节点 '{Name}' 第 {iter + 1} 次迭代条件求值失败，已中断流程: {ex.Message}"
                    );
                    throw;
                }

                if (!isTrue) break;

                // A1：循环体交给全引擎统一的序列执行器。
                // 旧实现是 foreach 挨个 step.RunAndGetNext(context) 并丢弃返回值，
                // 而 CompiledIfNode 靠返回值交出选中分支 —— 于是循环体里的 If 一步都不执行（静默失效）。
                // 循环体用 yieldToControlFlow: true，Break/Continue 会被立刻交回来由本循环裁决。
                CompiledNode.RunSequence(LoopBranch.ExecutionSteps, context, yieldToControlFlow: true);

                if (context.CancellationToken.IsCancellationRequested) return;

                if (context.CurrentFlowState == FlowControlState.Continue)
                {
                    context.CurrentFlowState = FlowControlState.Normal;
                }
                else if (context.CurrentFlowState == FlowControlState.Break)
                {
                    context.CurrentFlowState = FlowControlState.Normal;
                    return; // 跳出整个循环
                }
                else if (context.CurrentFlowState == FlowControlState.Return)
                {
                    return; // 终止流程，状态保留给上层执行器识别
                }

                // Continue 与正常跑完一圈都要计数，否则 MaxIterations 拦不住"每圈都 Continue"的死循环
                iter++;
            }

            // P2-⑧（语义升级）：迭代上限被触发 = 循环没跑完就被强行掐断，结果不可信。
            // 旧实现只 Warn 一声就把步骤标成 Success，现场会误以为"循环正常跑完"。
            // 现在与 CompiledPluginNode 的失败语义对齐：程序级异常 → 标 Failed（由 RunAndGetNext 的 catch 落状态）+ 上抛中断流程。
            // 这里能成立的前提：只有 while 的循环头守卫能带着 iter == MaxIterations 退出，
            // 其余出口（条件为假 break / 取消 break / Continue / Break / Return）退出时 iter 必然 < MaxIterations。
            // MaxIterations > 0 是为了排除"上限配成 0 圈"这种正常的不执行，别误报成死循环。
            if (
                iter >= MaxIterations
                && MaxIterations > 0
                && !context.CancellationToken.IsCancellationRequested
            )
            {
                throw new InvalidOperationException(
                    $"While节点 '{Name}' 达到最大迭代次数 {MaxIterations}，循环被强制中断（疑似死循环，请检查循环条件与变量刷新）"
                );
            }
        }
    }
}
