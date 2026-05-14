using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class CompiledBreakNode : CompiledNode
    {
        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentFlowState = FlowControlState.Break;
            context.Logger.Info("执行 Break，准备跳出循环...");
            return null;
        }
    }

    public class CompiledContinueNode : CompiledNode
    {
        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentFlowState = FlowControlState.Continue;
            return null;
        }
    }

    public class CompiledReturnNode : CompiledNode
    {
        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentFlowState = FlowControlState.Return;
            context.Logger.Warn("执行 Return，主流程即将终止！");
            return null;
        }
    }
}
