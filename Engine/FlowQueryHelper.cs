﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
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
        /// <summary>
        /// 插件提供者（懒解析）。
        ///
        /// 【为什么不裸写 if (x == null) x = Resolve()】那是非原子的"检查-赋值"：
        /// 多线程下两个调用方可能各自 Resolve 一次，且一方可能读到另一方尚未发布完成的引用。
        ///
        /// 【为什么不用 Lazy&lt;T&gt;】Lazy 会**缓存首次 Resolve 抛出的异常** ——
        /// 万一第一次调用发生在容器注册完成之前，之后每次调用都会重抛同一个异常，
        /// 而本类没有任何"容器已就绪"的信号可以等。
        /// CompareExchange 只让其中一个赢，输的一方下次调用会重新尝试，
        /// 行为与原来的懒解析一致，但发布是原子的。
        /// </summary>
        private static IPluginProvider _pluginProvider;

        private static IPluginProvider PluginProvider
        {
            get
            {
                var current = _pluginProvider;
                if (current != null) return current;

                var resolved = ContainerLocator.Container.Resolve<IPluginProvider>();
                Interlocked.CompareExchange(ref _pluginProvider, resolved, null);
                return _pluginProvider;
            }
        }

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
            var treeNodes = new List<ToolItemModel>();

            if (globals != null && globals.Any())
            {
                var globalNode = new ToolItemModel()
                {
                    ModuleGroup = "Global",
                    DefaultLinkKind = LinkKind.GlobalVariable,
                    Name = "全局变量 (Global)",
                    Icon = "\uf0ac",
                    Description = "全局共享变量",
                    OutputDefinitions = globals
                        .Select(gv => new PortDefinition
                        {
                            Name = gv.Name,
                            // 带上稳定身份：绑定弹窗据此把 Id 写进连线，改名后连线仍能命中
                            VariableId = gv.VariableId,
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
                    Id = LinkProtocol.RuntimeVariableMarkerGuid,
                    ModuleGroup = "Runtime",
                    DefaultLinkKind = LinkKind.RuntimeVariable,
                    Name = "运行时变量 (Runtime)",
                    Icon = "\uf085",
                    Description = "流程执行中由变量定义节点动态创建的本地变量",
                    OutputDefinitions = runtimeVars,
                };
                treeNodes.Add(runtimeNode);
            }

            foreach (var node in upstreamNodes)
            {
                var data = PluginProvider.ModulePlugins[node.PluginTypeName];
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
                if (!TryGetDefinedVariable(step, out var varName, out var dataType))
                    continue;

                // 同名变量以最后一次定义为准（运行期也是覆盖语义）
                if (!seenNames.Add(varName))
                {
                    // 已存在则移除旧的，准备覆盖
                    var existing = result.Find(p => p.Name == varName);
                    if (existing != null)
                        result.Remove(existing);
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
        /// 判断一个步骤是否为「变量定义」插件节点，并取回它声明的变量名与类型。
        ///
        /// 为什么公开：绑定弹窗的候选树与画布的值输出脚必须认同一批节点、用同一套取名取类型规则，
        /// 两处各写一遍迟早漂移（漂移的表现是"画布能连、弹窗选不到"或反过来）。
        /// </summary>
        /// <remarks>
        /// VariableDefinitionPlugin 的 Name 端口值（变量名）和 Type 端口值（类型字符串）
        /// 在设计期由用户填写并持久化到 StepModel.InputValues，
        /// 据此可以在不执行流程的情况下推断出"将被创建"的运行时变量。
        /// 变量名未填时返回 false —— 名字是这条线的寻址键，空名连上了也取不到值。
        /// </remarks>
        public static bool TryGetDefinedVariable(
            StepModel step,
            out string variableName,
            out Type variableType
        )
        {
            variableName = null;
            variableType = typeof(object);

            if (step?.PluginTypeName == null || step.InputValues == null)
                return false;

            // 通过类型名识别变量定义插件，避免反射依赖外部插件 DLL
            if (!step.PluginTypeName.Contains("VariableDefinitionPlugin"))
                return false;

            if (!step.InputValues.TryGetValue("Name", out var nameObj))
                return false;
            if (nameObj is not string name || string.IsNullOrWhiteSpace(name))
                return false;

            variableName = name;

            if (step.InputValues.TryGetValue("Type", out var typeObj) && typeObj is string typeStr)
                variableType = ParseVariableType(typeStr);

            return true;
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

                // 递归所有容器（If / While / For），而不是只认 ConditionStep。
                // 旧实现只判 ConditionStep：While 靠继承侥幸覆盖，For 的子层整个漏掉，
                // 表现是"循环体里定义的变量在下游绑定弹窗里选不到"，而运行期 LocalVariables 明明有值。
                // 判接口不判具体类型，以后新增容器步骤不必再来这里补一刀。
                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var childCollection in container.Children)
                    {
                        if (childCollection?.Steps == null)
                            continue;

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
