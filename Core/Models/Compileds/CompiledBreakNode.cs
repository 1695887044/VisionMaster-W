﻿﻿﻿﻿﻿﻿﻿using Core.Interfaces;
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
            // A2 口径：控制流节点也要上报状态与耗时——否则画布上这几步永远不亮、没有耗时，
            // 现场"循环为什么提前退出"时看不出是这一步干的（第二批审查修复）
            context.CurrentNodeId = Id;
            UpdateStepRuntimeState(context, StepRuntimeState.Running);

            context.CurrentFlowState = FlowControlState.Break;
            context.Logger.Info("执行 Break，准备跳出循环...");

            UpdateStepRuntimeState(context, StepRuntimeState.Success);
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
            context.CurrentNodeId = Id;
            UpdateStepRuntimeState(context, StepRuntimeState.Running);

            context.CurrentFlowState = FlowControlState.Continue;

            UpdateStepRuntimeState(context, StepRuntimeState.Success);
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
            context.CurrentNodeId = Id;
            UpdateStepRuntimeState(context, StepRuntimeState.Running);

            context.CurrentFlowState = FlowControlState.Return;
            context.Logger.Warn("执行 Return，主流程即将终止！");

            // Return 是"按指令终止"的正常出口（不是失败）：标成功，把它与"流程异常"区分开——
            // 现场要看得出这是设计里的收尾，而不是崩了
            UpdateStepRuntimeState(context, StepRuntimeState.Success);
            return null;
        }
    }
}
