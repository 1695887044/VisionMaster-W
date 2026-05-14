using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Models
{
    /// <summary>
    /// 编译后的 For 循环节点
    /// 支持指定次数的循环执行，并向外暴露循环索引
    /// </summary>
    public class CompiledForNode : CompiledNode
    {
        /// <summary>
        /// 索引输出端口（供下游算子绑定）
        /// </summary>
        public OutputPort<int> IndexPort { get; } = new OutputPort<int>("Index");

        /// <summary>
        /// 循环次数输入连线
        /// </summary>
        public IOutputPort LoopCountLink { get; set; }

        /// <summary>
        /// 默认循环次数（未连接时使用）
        /// </summary>
        public int DefaultLoopCount { get; set; }

        /// <summary>
        /// 循环体步骤列表
        /// </summary>
        public List<CompiledNode> LoopBody { get; set; } = new();

        /// <summary>
        /// 执行 For 循环
        /// 根据指定次数执行循环体，支持 Break/Continue/Return 控制流
        /// </summary>
        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentNodeId = Id;

            int targetCount = DefaultLoopCount;
            if (LoopCountLink != null && LoopCountLink.Value != null)
            {
                targetCount = Convert.ToInt32(LoopCountLink.Value);
            }

            for (int i = 0; i < targetCount; i++)
            {
                if (context.CancellationToken.IsCancellationRequested) break;

                IndexPort.Value = i;

                foreach (var step in LoopBody)
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
            }
            return null;
        }
    }
}
