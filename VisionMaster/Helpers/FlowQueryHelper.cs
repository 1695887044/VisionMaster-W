using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.Helpers
{
    public static class FlowQueryHelper
    {
        static IPluginProvider pluginProvider;

        public static List<ToolItemModel> GetAvailableVariablesTree(
            IEnumerable<GlobalVariableModel> globals,
            IEnumerable<StepModel> allSteps,
            StepModel targetStep
        )
        {
            if(pluginProvider == null)
            {
                pluginProvider = ContainerLocator.Container.Resolve<IPluginProvider>();
            }
            var treeNodes = new List<ToolItemModel>();
            if (globals != null && globals.Any())
            {
                var globalNode = new ToolItemModel()
                {
                    ModuleGroup = "Global",
                    Name = "全局变量 (Global)",
                    Icon = "\uf0ac",
                    Description ="全局共享变量",
                    OutputDefinitions = globals
                        .Select(gv => new PortDefinition
                        {
                            Name = gv.Name,
                            DataTypeName = gv.DataType.AssemblyQualifiedName,
                            Description = gv.Description
                        })
                        .ToList(),
                };
                treeNodes.Add(globalNode);
            }
            //获取当前节点以上的所有输出 只是展示 不需要Clone
            var upstreamNodes = GetUpstreamNodes(allSteps, targetStep);
            foreach (var node in upstreamNodes)
            {
                var data = pluginProvider.ModulePlugins[node.PluginTypeName];
                if (data == null || data.OutputDefinitions == null || !data.OutputDefinitions.Any())
                    continue;

                var uiNode = new ToolItemModel
                {
                    Id = node.StepID,
                    ModuleGroup = node.StepName,
                    Name = node.StepName,
                    Icon = node.Icon,
                    Description= node.Description,
                    OutputDefinitions = data.OutputDefinitions.ToList(),
                };

                treeNodes.Add(uiNode);
            }

            return treeNodes;
        }

        public static List<StepModel> GetUpstreamNodes(
            IEnumerable<StepModel> steps,
            StepModel targetStep
        )
        {
            var result = new List<StepModel>();
            foreach (var step in steps)
            {
                if (step == targetStep)
                    return result;
                result.Add(step);

                if (step is ConditionStep branchStep)
                {
                    foreach (var childCollection in branchStep.Children)
                    {
                        var innerResult = GetUpstreamNodes(childCollection.Steps, targetStep);
                        result.AddRange(innerResult);

                        if (innerResult.Contains(targetStep))
                            return result;
                    }
                }
            }
            return result;
        }
    }
}
