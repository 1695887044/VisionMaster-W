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

            // 上限配成 0 或负数 = "这个循环不执行"，属正常配置而非死循环。
            // 提到循环外判断，循环体里就不必再夹一个 MaxIterations > 0 的条件。
            if (MaxIterations <= 0) return;

            int iter = 0;

            // args 数组布局：先局部变量（LocalVarIds），再运行时变量（RuntimeVarNames）
            // 数组大小每次迭代固定，分配一次放循环外复用，循环体内只重填值
            int totalArgs = LoopBranch.LocalVarIds.Count + LoopBranch.RuntimeVarNames.Count;
            var args = new object[totalArgs];

            // 【为什么是 while (true) 而不是 while (iter < MaxIterations)】
            // 旧写法把"上限"当循环头守卫，退出后无法区分两种退出原因：
            //   (a) 条件自然转假 —— 循环正常跑完
            //   (b) 跑满上限被掐断 —— 疑似死循环
            // 于是 `i < 9999` 这种"正好跑满 9999 圈后条件转假"的完全合法写法会被误报成死循环，
            // 整条流程 Faulted。默认上限就是 9999，工业连续场景真会撞上。
            // 现在把条件求值放在上限判断之前："自然跑完"从 return 走、"被掐断"从 throw 走，两者不再混淆。
            // 代价仅是"真死循环"场景多求值一次条件（自然结束路径的求值次数与旧实现完全一致）。
            while (true)
            {
                if (context.CancellationToken.IsCancellationRequested) return;

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

                // 条件转假 = 循环自然跑完，走这条出口正常返回（绝不会被误判成死循环）
                if (!isTrue) return;

                // 条件仍为真却已跑满上限 = 循环没跑完就被强行掐断，结果不可信。
                // P2-⑧（语义升级）：与 CompiledPluginNode 的失败语义对齐 ——
                // 标 Failed（由 RunAndGetNext 的 catch 落状态）+ 上抛中断流程。
                // 旧实现只 Warn 一声就把步骤标成 Success，现场会误以为"循环正常跑完"。
                // 【顺序是这条修复的全部要害】上限判断必须在"条件为真"之后做。
                // 提到前面（旧实现的等价物）就会把"条件恰好在上限那一圈转假"误判成死循环 ——
                // [E8] ④b 已用灵敏度实测钉住：探针一挪位置，两条断言立刻变红。
                if (iter >= MaxIterations)
                {
                    throw new InvalidOperationException(
                        $"While节点 '{Name}' 达到最大迭代次数 {MaxIterations}，循环被强制中断（疑似死循环，请检查循环条件与变量刷新）"
                    );
                }

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
        }
    }
}
