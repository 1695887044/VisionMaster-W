﻿﻿﻿using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// Break 编译节点
    /// 用于跳出循环
    /// </summary>
    public class CompiledBreakNode : CompiledNode
    {
        /// <summary>
        /// 执行 Break 指令
        /// 设置流程控制状态为 Break，跳出当前循环
        /// </summary>
        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentFlowState = FlowControlState.Break;
            context.Logger.Info("执行 Break，准备跳出循环...");
            return null;
        }
    }

    /// <summary>
    /// Continue 编译节点
    /// 用于跳过当前循环迭代
    /// </summary>
    public class CompiledContinueNode : CompiledNode
    {
        /// <summary>
        /// 执行 Continue 指令
        /// 设置流程控制状态为 Continue，跳过当前迭代
        /// </summary>
        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentFlowState = FlowControlState.Continue;
            return null;
        }
    }

    /// <summary>
    /// Return 编译节点
    /// 用于终止整个流程执行
    /// </summary>
    public class CompiledReturnNode : CompiledNode
    {
        /// <summary>
        /// 执行 Return 指令
        /// 设置流程控制状态为 Return，终止主流程
        /// </summary>
        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentFlowState = FlowControlState.Return;
            context.Logger.Warn("执行 Return，主流程即将终止！");
            return null;
        }
    }
}
