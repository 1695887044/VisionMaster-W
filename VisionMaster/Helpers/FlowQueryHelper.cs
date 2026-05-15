﻿﻿﻿﻿﻿﻿﻿using System;
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
    /// <summary>
    /// 流程查询帮助类
    /// 提供流程相关的查询和分析功能
    /// </summary>
    public static class FlowQueryHelper
    {
        static IPluginProvider pluginProvider;

        /// <summary>
        /// 获取可用于绑定的变量树
        /// 包括全局变量和上游步骤的输出端口
        /// </summary>
        public static List<ToolItemModel> GetAvailableVariablesTree(
            IEnumerable<GlobalVariableModel> globals,
            IEnumerable<StepModel> allSteps,
            StepModel targetStep
        )
        {
            if (pluginProvider == null)
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
                    Description = "全局共享变量",
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
                    Description = node.Description,
                    OutputDefinitions = data.OutputDefinitions.ToList(),
                };

                treeNodes.Add(uiNode);
            }

            return treeNodes;
        }

        /// <summary>
        /// 获取目标步骤之前的所有上游步骤
        /// 支持嵌套容器步骤的递归查找
        /// </summary>
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
