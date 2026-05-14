using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Services
{
    public class ExecutionContext : IExecutionContext
    {
        public ILogService Logger { get; init; }

        public IPortBindingService PortBindingService { get; init; }

        public CancellationToken CancellationToken { get; init; }
        public FlowControlState CurrentFlowState { get; set; }

        public ExecutionContext(ILogService logService)
        {
            Logger = logService;
        }
    }
}
