using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Interfaces
{

    public interface IVisionPlugin
    {
        
        static int InstanceCount { get; }
        string PluginID { get; set; }

        string InstanceName { get; set; }
        IReadOnlyDictionary<string, IInputPort> Inputs { get; }
        IReadOnlyDictionary<string, IOutputPort> Outputs { get; }

        bool Execute(IExecutionContext context);


        void Initialize();

        void Dispose();
    }
}
