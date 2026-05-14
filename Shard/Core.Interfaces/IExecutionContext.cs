using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces
{
    public enum FlowControlState
    {
        Normal,   // 正常执行
        Continue, // 跳过本次循环剩余节点，直接进入下一次循环
        Break,    // 立即终止当前循环
        Return    // 立即终止整个流程的执行
    }
    public interface IExecutionContext
    {
        ILogService Logger { get; }

        IPortBindingService PortBindingService { get; }
        CancellationToken CancellationToken { get; }

        FlowControlState CurrentFlowState { get; set; }

    }
}
