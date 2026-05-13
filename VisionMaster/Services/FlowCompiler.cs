using Core.Interfaces;
using DynamicExpresso;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using VisionMaster.Helpers;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    public class FlowCompiler
    {
        /// <summary>
        /// 缓存
        /// </summary>
        static Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();
        private readonly IWorkspaceManager workspaceManager;
        private readonly Interpreter _interpreter = new Interpreter().Reference(typeof(Math));

        public FlowCompiler(IWorkspaceManager workspaceManager)
        {
            this.workspaceManager = workspaceManager;
        }

        public CompilationResult Compile(IEnumerable<StepModel> blueprints)
        {
            var result = new CompilationResult();

            var pluginLookup = new Dictionary<Guid, IVisionPlugin>();
            // 给编译器内部连线用的：所有节点（包含插件和 IF 控制节点）
            var nodeLookup = new Dictionary<Guid, CompiledNode>();

            try
            {
                // 1. 将图纸编译为虚拟机指令树
                var rootNodes = CompileSteps(blueprints, pluginLookup, nodeLookup, result.Errors);

                // 2. 接管子 + 安全检查
                LinkPorts(blueprints, nodeLookup, pluginLookup, result.Errors);

                result.Success = result.Errors.Count == 0;
                if (result.Success)
                {
                    result.Data = new CompiledFlow(rootNodes, pluginLookup);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add("系统崩溃级错误: " + ex.Message);
            }
            return result;
        }

        private List<CompiledNode> CompileSteps(
            IEnumerable<StepModel> models,
            Dictionary<Guid, IVisionPlugin> pluginLookup,
            Dictionary<Guid, CompiledNode> nodeLookup,
            List<string> errors
        )
        {
            var compiledNodes = new List<CompiledNode>();

            foreach (var model in models)
            {
                if (!model.IsEnabled)
                    continue;

                // ==========================================
                // 🎯 场景 A：内置逻辑节点 (绕过反射，直接硬编译)
                // ==========================================
                if (model is ConditionStep conditionModel)
                {
                    var ifNode = new CompiledIfNode();
                    nodeLookup.Add(model.StepID, ifNode); // 登记节点，为了后续连线

                    // 准备形参签名       变量名和它推断出的类型存下来，运行期需要用到！
                    var delegateParams = new List<Parameter>();
                    var compiledVarTypes = new Dictionary<string, Type>();
                    if (conditionModel.LocalVariableNames != null)
                    {
                        foreach (var localVar in conditionModel.LocalVariableNames)
                        {
                            Type varType = typeof(double);
                            if (conditionModel.LinkedSources.TryGetValue(localVar, out var link) && link != null)
                            {

                                var actualType = TypeHelper.GetActualTypeFromLink(link);
                                if (actualType != null)
                                {
                                    // ==========================================
                                    // 🛑 核心拦截：类型安检！
                                    // ==========================================
                                    if (!TypeHelper.IsSafeExpressionType(actualType))
                                    {
                                        errors.Add($"[安全拦截] 变量 '{localVar}' 绑定的数据类型 '{actualType.Name}' 不合法！表达式仅支持基础数值和字符串。");
                                        continue; 
                                    }

                                    varType = actualType;
                                }
                            }
                            delegateParams.Add(new Parameter(localVar, varType));
                            compiledVarTypes[localVar] = varType;
                        }
                    }

                    // 编译分支
                    foreach (var childCollection in conditionModel.Children)
                    {
                        var childNodes = CompileSteps(
                            childCollection.Steps,
                            pluginLookup,
                            nodeLookup,
                            errors
                        );
                        Lambda compiledCondition = null;

                        if (
                            childCollection.BranchType == BranchType.Else
                            || childCollection.BranchType == BranchType.Default
                        )
                        {
                            try
                            {
                                //// 强制第二个参数返回 bool，增加安全性
                                compiledCondition = _interpreter.Parse("true", typeof(bool), delegateParams.ToArray());
                            }
                            catch { }
                        }
                        else if (string.IsNullOrWhiteSpace(childCollection.Expression))
                        {
                            errors.Add(
                                $"[编译警告] '{model.StepName}' 的分支 '{childCollection.StepName}' 表达式为空。"
                            );
                        }
                        else
                        {
                            try
                            {
                                compiledCondition = _interpreter.Parse(childCollection.Expression, typeof(bool), delegateParams.ToArray());
                            }
                            catch (Exception ex)
                            {
                                errors.Add(
                                    $"[语法错误] 节点 '{model.StepName}' 编译失败: {ex.Message}"
                                );
                            }
                        }

                        ifNode.Branches.Add(
                            new CompiledBranch
                            {
                                ConditionLambda = compiledCondition,
                                LocalVarNames = conditionModel.LocalVariableNames?.ToList() ?? new List<string>(),
                                VarTypes = compiledVarTypes,
                                ExecutionSteps = childNodes
                            }
                        );
                    }
                    compiledNodes.Add(ifNode);
                }
                // ==========================================
                // 🎯 场景 B：真正的视觉算子 (反射实例化)
                // ==========================================
                else
                {
                    IVisionPlugin plugin = null;
                    try
                    {
                        Type type = Type.GetType(model.PluginTypeName);
                        if (type == null)
                            throw new Exception($"找不到插件: {model.PluginTypeName}");

                        plugin = (IVisionPlugin)Activator.CreateInstance(type);
                        plugin.InstanceName = model.StepName;

                        pluginLookup.Add(model.StepID, plugin); // 登记到外部字典 (供查结果用)
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"[加载失败] 算子 '{model.StepName}': {ex.Message}");
                        continue;
                    }

                    // 灌入静态固定参数
                    foreach (var kvp in model.InputValues)
                    {
                        if (plugin.Inputs.TryGetValue(kvp.Key, out var inputPort))
                            inputPort.Value = kvp.Value;
                    }

                    var pluginNode = new CompiledPluginNode { ExternalPlugin = plugin };
                    nodeLookup.Add(model.StepID, pluginNode); // 登记到内部字典 (供连线用)
                    compiledNodes.Add(pluginNode);
                }
            }
            return compiledNodes;
        }

        private void LinkPorts(
            IEnumerable<StepModel> models,
            Dictionary<Guid, CompiledNode> nodeLookup,
            Dictionary<Guid, IVisionPlugin> pluginLookup, // 只有真正的算子才能作为【数据源】提供输出
            List<string> errors
        )
        {
            foreach (var model in models)
            {
                if (model == null || !model.IsEnabled)
                    continue;

                if (!nodeLookup.TryGetValue(model.StepID, out var targetNode))
                    continue;

                // --- 连线绑定 ---
                foreach (var linkKvp in model.LinkedSources)
                {
                    string myInputName = linkKvp.Key;
                    LinkReference linkRef = linkKvp.Value;
                    if (linkRef == null)
                        continue;

                    IOutputPort sourcePort = null;
                    string actualUpstreamName = "未知";

                    if (linkRef.TargetStepId == Guid.Empty)
                    {
                        sourcePort = workspaceManager.GlobalVariables.FirstOrDefault(s =>
                            s.Name == linkRef.TargetPortName
                        );
                        if (sourcePort == null)
                            errors.Add(
                                $"[连线断开] '{model.StepName}' 找不到全局变量: '{linkRef.TargetPortName}'"
                            );
                        actualUpstreamName = "Global";
                    }
                    else
                    {
                        // 🌟 上游一定是一个真正的 Plugin，所以去 pluginLookup 找输出端口
                        if (pluginLookup.TryGetValue(linkRef.TargetStepId, out var upPlugin))
                        {
                            actualUpstreamName = upPlugin.InstanceName;
                            var match = Regex.Match(
                                linkRef.TargetPortName,
                                @"^(?<port>[^\[]+)(\[(?<idx>\d+)\])?$"
                            );
                            if (match.Success)
                            {
                                string cleanPortName = match.Groups["port"].Value;
                                int arrayIdx = match.Groups["idx"].Success
                                    ? int.Parse(match.Groups["idx"].Value)
                                    : -1;

                                if (upPlugin.Outputs.TryGetValue(cleanPortName, out sourcePort))
                                {
                                    if (arrayIdx >= 0)
                                        sourcePort = new ArrayIndexProxyPort(sourcePort, arrayIdx);
                                }
                                else
                                {
                                    errors.Add(
                                        $"[连线断开] 上游 '{actualUpstreamName}' 不存在输出 '{cleanPortName}'"
                                    );
                                }
                            }

                            // 自愈功能
                            string correctAddress =
                                $"{actualUpstreamName}.{linkRef.TargetPortName}";
                            if (linkRef.DisplayAddress != correctAddress)
                                linkRef.DisplayAddress = correctAddress;
                        }
                        else
                        {
                            errors.Add(
                                $"[致命断连] '{model.StepName}' 引用的上游节点 (ID:{linkRef.TargetStepId}) 不存在！"
                            );
                        }
                    }

                    if (sourcePort != null)
                    {
                        // 🎯 目标是普通算子
                        if (targetNode is CompiledPluginNode pluginNode)
                        {
                            if (
                                pluginNode.ExternalPlugin.Inputs.TryGetValue(
                                    myInputName,
                                    out var myInPort
                                )
                            )
                                myInPort.LinkedSource = sourcePort;
                            else
                                errors.Add(
                                    $"[结构错误] '{model.StepName}' 不存在输入端口 '{myInputName}'"
                                );
                        }
                        // 🎯 目标是 IF 控制节点
                        else if (targetNode is CompiledIfNode ifNode)
                        {
                            // 直接把上游的输出端口挂到字典里，运行时直接读取它的 Value！
                            ifNode.UpstreamLinks[myInputName] = sourcePort;
                        }
                    }
                }

                // --- 必填项检查 (只针对普通算子) ---
                if (targetNode is CompiledPluginNode pNode)
                {
                    foreach (var input in pNode.ExternalPlugin.Inputs.Values)
                    {
                        if (input.IsRequired && input.LinkedSource == null)
                            errors.Add(
                                $"[参数缺失] '{model.StepName}' 的必填参数 '{input.Name}' 未配置！"
                            );
                    }
                }

                // --- 递归子分支连线 ---
                if (model is ConditionStep conditionModel && conditionModel.Children != null)
                {
                    foreach (var branch in conditionModel.Children)
                        if (branch.Steps != null)
                            LinkPorts(branch.Steps, nodeLookup, pluginLookup, errors);
                }
            }
        }
    }
}
