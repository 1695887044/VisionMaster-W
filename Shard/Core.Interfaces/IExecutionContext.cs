using System;
using System.Collections.Generic;
using System.Threading;

namespace Core.Interfaces
{
    public enum FlowControlState
    {
        Normal,
        Continue,
        Break,
        Return
    }

    public interface IExecutionContext
    {
        ILogService Logger { get; }

        IPortBindingService PortBindingService { get; }
        
        CancellationToken CancellationToken { get; }

        FlowControlState CurrentFlowState { get; set; }
        
        Guid? CurrentNodeId { get; set; }

        /// <summary>
        /// 当前执行流程的名称（无会话/无名称时为 null）。
        /// 供"按流程名分槽"的取数口使用——目前是网络收图（<see cref="ImageHub"/>）：
        /// 采集步骤必须知道"我这张图该从哪个流程槽里取"，而这个信息只有执行上下文知道。
        /// </summary>
        string CurrentFlowName { get; }
        
        DateTime ExecutionStartTime { get; }
        
        IDictionary<string, object> LocalVariables { get; }
    }
}