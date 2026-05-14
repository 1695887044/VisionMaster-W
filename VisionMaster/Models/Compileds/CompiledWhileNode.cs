using Core.Interfaces;
using DynamicExpresso;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class CompiledWhileNode : CompiledNode
    {
        public CompiledBranch LoopBranch { get; set; }
        public Dictionary<Guid, IOutputPort> UpstreamLinks { get; set; } = new();
        public int MaxIterations { get; set; } = 9999; // 防死循环

        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            if (LoopBranch?.ConditionLambda == null) return null;

            int iter = 0;
            while (iter < MaxIterations)
            {
                if (context.CancellationToken.IsCancellationRequested) break;

                // 1. 动态取值
                var args = new object[LoopBranch.LocalVarIds.Count];
                for (int i = 0; i < LoopBranch.LocalVarIds.Count; i++)
                {
                    Guid varId = LoopBranch.LocalVarIds[i];
                    if (UpstreamLinks.TryGetValue(varId, out var sourcePort) && sourcePort?.Value != null)
                        args[i] = sourcePort.Value;
                    else
                        args[i] = 0.0; // 兜底
                }

                // 2. 判断条件
                bool isTrue = false;
                try { isTrue = (bool)LoopBranch.ConditionLambda.Invoke(args); }
                catch { break; }

                // 3. 决定流向
                if (!isTrue) break; // 条件不成立，跳出 While 循环

                // 4. 递归执行循环体内部算子
                foreach (var step in LoopBranch.ExecutionSteps)
                {
                    if (context.CancellationToken.IsCancellationRequested) return null;
                    step.RunAndGetNext(context);
                    if (context.CurrentFlowState == FlowControlState.Continue)
                    {
                        // 1. 拦截 Continue：把信号灯重置为正常，然后跳出内层的 foreach，让外层 for 进入下一次迭代
                        context.CurrentFlowState = FlowControlState.Normal;
                        break;
                    }
                    if (context.CurrentFlowState == FlowControlState.Break)
                    {
                        // 2. 拦截 Break：把信号灯重置为正常，然后彻底结束这个 CompiledForNode 的执行
                        context.CurrentFlowState = FlowControlState.Normal;
                        return null; // 直接返回，外层的 for 循环也被强行终止了！
                    }
                    if (context.CurrentFlowState == FlowControlState.Return)
                    {
                        // 3. 遇到 Return：不要重置信号灯！直接返回！
                        // 让 Return 信号一直往上冒泡，直到被主流程引擎看到！
                        return null;
                    }
                }
                iter++;
            }
            return null; // 执行完毕，让主流程继续
        }
    }
}
