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
        
        DateTime ExecutionStartTime { get; }
        
        IDictionary<string, object> LocalVariables { get; }
    }
}