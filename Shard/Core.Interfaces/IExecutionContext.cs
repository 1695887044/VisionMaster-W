using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces
{
    public interface IExecutionContext
    {
        ILogService Logger { get; }

        IPortBindingService PortBindingService { get; }
        CancellationToken CancellationToken { get; }

    }
}
