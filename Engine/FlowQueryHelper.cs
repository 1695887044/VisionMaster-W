﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
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
        /// 包括全局变量、运行时本地变量和上游步骤的输出端口
        /// </summary>
        public static List<ToolItemModel> GetAvailableVariablesTree(
            IEnumerable<IVariable> globals,
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
                            Description = gv.VariableType == VariableType.Communication
                                ? $"[网络变量] {gv.Description} (连接: {gv.ConnectionName})"
                                : $"[本地变量] {gv.Description}"
                        })
                        .ToList(),
                };
                treeNodes.Add(globalNode);
            }

            var upstreamNodes = GetUpstreamNodes(allSteps, targetStep);

            // 运行时本地变量：由上游 VariableDefinitionPlugin 节点动态写入 context.LocalVariables
            // 设计期静态推断变量名/类型，运行期由 RuntimeVariableProxyPort 从 LocalVariables 取值
            var runtimeVars = GetRuntimeVariableDefinitions(upstreamNodes);
            if (runtimeVars.Count > 0)
            {
                var runtimeNode = new ToolItemModel()
                {
                    Id = FlowCompiler.RuntimeVariableMarkerGuid,
                    ModuleGroup = "Runtime",
                    Name = "运行时变量 (Runtime)",
                    Icon = "\uf085",
                    Description = "流程执行中由变量定义节点动态创建的本地变量",
                    OutputDefinitions = runtimeVars,
                };
                treeNodes.Add(runtimeNode);
            }

            foreach (var node in upstreamNodes)
            {
                var data = pluginProvider.ModulePlugins[node.PluginTypeName];
                if (data == null || data.OutputDefinitions == null || !data.OutputDefinitions.Any())
                    continue;

                // 合并动态输出端口：StepModel.OutputPortDefinitions 快照（名字+类型）里的端口
                // 不在静态表里，补进去让绑定界面可选，类型兼容检查按真实类型执行
                var outputDefs = data.OutputDefinitions.ToList();
                if (node.OutputPortDefinitions != null && node.OutputPortDefinitions.Count > 0)
                {
                    var existing = new HashSet<string>(outputDefs.Select(p => p.Name));
                    foreach (var dyn in node.OutputPortDefinitions)
                    {
                        if (string.IsNullOrEmpty(dyn?.Name) || existing.Contains(dyn.Name)) continue;
                        outputDefs.Add(new PortDefinition
                        {
                            Name = dyn.Name,
                            DataTypeName = dyn.DataTypeName ?? typeof(object).AssemblyQualifiedName,
                            Description = dyn.Description ?? "[动态输出]"
                        });
                    }
                }

                var uiNode = new ToolItemModel
                {
                    Id = node.StepID,
                    ModuleGroup = node.StepName,
                    Name = node.StepName,
                    Icon = node.Icon,
                    Description = node.Description,
                    OutputDefinitions = outputDefs,
                };

                treeNodes.Add(uiNode);
            }

            return treeNodes;
        }

        /// <summary>
        /// 从上游步骤中静态扫描 VariableDefinitionPlugin 节点，
        /// 提取其声明的运行时变量名与类型，构造可供绑定的 PortDefinition 列表
        /// </summary>
        /// <remarks>
        /// VariableDefinitionPlugin 的 Name 端口值（变量名）和 Type 端口值（类型字符串）
        /// 在设计期由用户填写并持久化到 StepModel.InputValues，
        /// 据此可以在不执行流程的情况下推断出"将被创建"的运行时变量
        /// </remarks>
        private static List<PortDefinition> GetRuntimeVariableDefinitions(IEnumerable<StepModel> upstreamSteps)
        {
            var result = new List<PortDefinition>();
            if (upstreamSteps == null)
                return result;

            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var step in upstreamSteps)
            {
                if (step?.PluginTypeName == null)
                    continue;

                // 通过类型名识别变量定义插件，避免反射依赖外部插件 DLL
                if (!step.PluginTypeName.Contains("VariableDefinitionPlugin"))
                    continue;

                if (step.InputValues == null)
                    continue;

                // 取变量名
                if (!step.InputValues.TryGetValue("Name", out var nameObj))
                    continue;
                if (nameObj is not string varName || string.IsNullOrWhiteSpace(varName))
                    continue;

                // 同名变量以最后一次定义为准（运行期也是覆盖语义）
                if (!seenNames.Add(varName))
                {
                    // 已存在则移除旧的，准备覆盖
                    var existing = result.Find(p => p.Name == varName);
                    if (existing != null)
                        result.Remove(existing);
                }

                // 取声明的类型
                Type dataType = typeof(object);
                if (step.InputValues.TryGetValue("Type", out var typeObj) && typeObj is string typeStr)
                {
                    dataType = ParseVariableType(typeStr);
                }

                result.Add(new PortDefinition
                {
                    Name = varName,
                    DataTypeName = dataType.AssemblyQualifiedName,
                    Description = $"[运行时变量] 由 '{step.StepName}' 定义"
                });
            }

            return result;
        }

        /// <summary>
        /// 将 VariableDefinitionPlugin 的 Type 端口字符串解析为对应的 Type
        /// 与 VariableDefinitionPlugin.ParseType 保持一致的基础类型集合
        /// </summary>
        private static Type ParseVariableType(string typeName)
        {
            return typeName?.ToLower() switch
            {
                "int" or "int32" => typeof(int),
                "double" => typeof(double),
                "string" => typeof(string),
                "bool" or "boolean" => typeof(bool),
                "datetime" => typeof(DateTime),
                "float" or "single" => typeof(float),
                "long" or "int64" => typeof(long),
                _ => typeof(object)
            };
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
