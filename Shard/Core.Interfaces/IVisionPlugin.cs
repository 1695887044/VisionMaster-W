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

        /// <summary>
        /// 最近一次执行失败的错误信息（Execute 返回 false 时有效，供引擎写日志/状态栏）
        /// </summary>
        string LastError { get; }


        void Initialize();

        void Dispose();
    }
}
