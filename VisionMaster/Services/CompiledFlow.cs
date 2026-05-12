using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Interfaces;

namespace VisionMaster.Services
{
    public class CompiledFlow
    {
        public List<IVisionPlugin> RootPlugins { get; private set; } // 主干执行流
        public Dictionary<string, IVisionPlugin> PluginLookup { get; private set; } // 全局找人字典

        public CompiledFlow(List<IVisionPlugin> rootPlugins, Dictionary<string, IVisionPlugin> lookup)
        {
            RootPlugins = rootPlugins;
            PluginLookup = lookup;
        }

        public void Run(IExecutionContext context)
        {
            // 依次执行主线机器
            foreach (var plugin in RootPlugins)
            {
                plugin.Execute(context);
            }
        }
    }
}
