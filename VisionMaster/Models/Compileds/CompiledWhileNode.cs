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
        /// 最大迭代次数（防止无限循环）
        /// </summary>
        public int MaxIterations { get; set; } = 9999;

        /// <summary>
        /// 执行 While 循环
        /// 条件为真时执行循环体，支持 Break/Continue/Return 控制流
        /// </summary>
        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentNodeId = Id;

            if (LoopBranch?.ConditionLambda == null) return null;

            int iter = 0;
            while (iter < MaxIterations)
            {
                if (context.CancellationToken.IsCancellationRequested) break;

                // args 数组：先局部变量（LocalVarIds），再运行时变量（RuntimeVarNames）
                int totalArgs = LoopBranch.LocalVarIds.Count + LoopBranch.RuntimeVarNames.Count;
                var args = new object[totalArgs];

                // 局部变量：从 UpstreamLinks 取值
                for (int i = 0; i < LoopBranch.LocalVarIds.Count; i++)
                {
                    Guid varId = LoopBranch.LocalVarIds[i];
                    if (UpstreamLinks.TryGetValue(varId, out var sourcePort) && sourcePort?.Value != null)
                        args[i] = sourcePort.Value;
                    else
                        args[i] = 0.0;
                }

                // 运行时变量：从 context.LocalVariables 取值
                for (int i = 0; i < LoopBranch.RuntimeVarNames.Count; i++)
                {
                    string varName = LoopBranch.RuntimeVarNames[i];
                    int argIndex = LoopBranch.LocalVarIds.Count + i;
                    Type expectedType = LoopBranch.RuntimeVarTypes.ContainsKey(varName) ? LoopBranch.RuntimeVarTypes[varName] : typeof(object);

                    if (context.LocalVariables.TryGetValue(varName, out var v))
                        args[argIndex] = v;
                    else
                        args[argIndex] = expectedType.IsValueType ? Activator.CreateInstance(expectedType) : null;
                }

                bool isTrue = false;
                try { isTrue = (bool)LoopBranch.ConditionLambda.Invoke(args); }
                catch { break; }

                if (!isTrue) break;

                foreach (var step in LoopBranch.ExecutionSteps)
                {
                    if (context.CancellationToken.IsCancellationRequested) return null;
                    step.RunAndGetNext(context);
                    if (context.CurrentFlowState == FlowControlState.Continue)
                    {
                        context.CurrentFlowState = FlowControlState.Normal;
                        break;
                    }
                    if (context.CurrentFlowState == FlowControlState.Break)
                    {
                        context.CurrentFlowState = FlowControlState.Normal;
                        return null;
                    }
                    if (context.CurrentFlowState == FlowControlState.Return)
                    {
                        return null;
                    }
                }
                iter++;
            }
            return null;
        }
    }
}
