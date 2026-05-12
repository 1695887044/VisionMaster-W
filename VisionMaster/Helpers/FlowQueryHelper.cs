using System;
using System.Collections.Generic;
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
        /// <summary>
        /// 获取指定算子之前的所有可用变量（包含全局变量和上游算子输出）
        /// </summary>
        /// <param name="globals">全局变量集合</param>
        /// <param name="allSteps">当前流程的所有算子</param>
        /// <param name="targetStep">目标算子（作为截断点，只取它前面的）</param>
        /// <returns>可以直接绑定到 UI 树形控件的节点集合</returns>
        public static List<NodeLinkViewModel> GetAvailableVariablesTree(
            IEnumerable<GlobalVariableModel> globals,
            IEnumerable<StepModel> allSteps,
            StepModel targetStep)
        {
            var treeNodes = new List<NodeLinkViewModel>();

            // 1. 组装全局变量节点
            if (globals != null && globals.Any())
            {
                var globalNode = new NodeLinkViewModel
                {
                    StepID = "Global",
                    DisplayName = "全局变量 (Global)",
                    IconCode = "\uf0ac", // 全局地球图标
                    InputSchemas = new List<PortSchema>(),
                    OutputSchemas = globals.Select(gv => new PortSchema
                    {
                        Name = gv.Name,
                        DataType = gv.DataType,
                        Description = gv.Description,
                    }).ToList()
                };
                treeNodes.Add(globalNode);
            }

            var upstreamNodes = GetUpstreamNodes(allSteps, targetStep);

            foreach (var node in upstreamNodes)
            {
                var data = PluginRegistry.GetSchema(node.PluginTypeName);
                if (data == null || data.OutputSchemas == null || !data.OutputSchemas.Any())
                    continue; // 如果这个算子没有输出，就不显示在字典里

                var uiNode = new NodeLinkViewModel
                {
                    StepID = node.StepID,
                    DisplayName = node.StepName,
                    IconCode = node.Icon,
                    InputSchemas = data.InputSchemas,
                    OutputSchemas = data.OutputSchemas,
                };

                treeNodes.Add(uiNode);
            }

            return treeNodes;
        }

        public static List<StepModel> GetUpstreamNodes(IEnumerable<StepModel> steps, StepModel targetStep)
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
