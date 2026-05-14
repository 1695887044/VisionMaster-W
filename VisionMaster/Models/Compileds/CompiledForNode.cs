using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class CompiledForNode : CompiledNode
    {
        // 🌟 原生自带的输出端口，在内存里实际长这样
        public OutputPort<int> IndexPort { get; } = new OutputPort<int>("Index");

        // 如果上游连了线，从这里取循环次数
        public IOutputPort LoopCountLink { get; set; }
        public int DefaultLoopCount { get; set; }

        public List<CompiledNode> LoopBody { get; set; } = new();

        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            // 动态获取循环次数：如果连线了就用连线的，没连线就用固定填写的
            int targetCount = DefaultLoopCount;
            if (LoopCountLink != null && LoopCountLink.Value != null)
            {
                targetCount = Convert.ToInt32(LoopCountLink.Value);
            }

            for (int i = 0; i < targetCount; i++)
            {
                if (context.CancellationToken.IsCancellationRequested) break;

                // 🌟 核心：更新原生输出端口的值！下游算子通过连线读的就是这个引用！
                IndexPort.Value = i;

                foreach (var step in LoopBody)
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
            }
            return null;
        }
    }
}
