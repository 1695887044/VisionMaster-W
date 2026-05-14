using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using Core.Interfaces;
using DynamicExpresso;
using VisionMaster.Helpers;
using VisionMaster.Models;
using static System.Windows.Forms.LinkLabel;
using Parameter = DynamicExpresso.Parameter;

namespace VisionMaster.Services
{
    public class FlowCompiler
    {
        /// <summary>
        /// 缓存
        /// </summary>
        static Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(
            50,
            StringComparer.Ordinal
        );
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
                if (model.IsDisEnable)
                    continue;

                // ==========================================
                // 🎯 场景 A-1：While 循环节点 (🚨 必须放在 ConditionStep 之前！)
                // ==========================================
                if (model is WhileStep whileModel)
                {
                    var whileNode = new CompiledWhileNode();
                    nodeLookup.Add(model.StepID, whileNode);

                    var delegateParams = new List<Parameter>();
                    var compiledVarTypes = new Dictionary<Guid, Type>();
                    var compiledVarIds = new List<Guid>();

                    // 1. 编译局部变量 (复用你的安全验证与缓存逻辑)
                    foreach (var localVar in whileModel.LocalVariables)
                    {
                        Type varType = typeof(double);
                        try
                        {
                            varType = Type.GetType(localVar.DataTypeName) ?? typeof(double);
                            if (TypeCache.TryGetValue(localVar.DataTypeName, out var type))
                            {
                                varType = type;
                            }
                            else
                            {
                                varType = TypeHelper.GetActualTypeFromLink(localVar.DataTypeName);
                                TypeCache[localVar.DataTypeName] = varType;
                            }

                            if (!TypeHelper.IsSafeExpressionType(varType))
                            {
                                errors.Add($"[安全拦截] 变量 '{localVar.Name}' 数据类型不合法！");
                                continue;
                            }
                        }
                        catch { }

                        delegateParams.Add(new Parameter(localVar.Name, varType));
                        compiledVarTypes[localVar.Id] = varType;
                        compiledVarIds.Add(localVar.Id);
                    }

                    // 2. 提取唯一的循环分支
                    var loopCollection = whileModel.Children.FirstOrDefault();
                    var compiledBranch = new CompiledBranch
                    {
                        LocalVarIds = compiledVarIds,
                        VarTypes = compiledVarTypes,
                        ExecutionSteps = new List<CompiledNode>(),
                    };

                    if (loopCollection != null)
                    {
                        // 递归编译循环体内部算子
                        if (loopCollection.Steps != null)
                        {
                            compiledBranch.ExecutionSteps = CompileSteps(
                                loopCollection.Steps,
                                pluginLookup,
                                nodeLookup,
                                errors
                            );
                        }

                        // 编译 While 的触发条件
                        if (string.IsNullOrWhiteSpace(loopCollection.Expression))
                        {
                            errors.Add(
                                $"[编译警告] '{whileModel.StepName}' 的循环条件表达式为空。"
                            );
                        }
                        else
                        {
                            try
                            {
                                compiledBranch.ConditionLambda = _interpreter.Parse(
                                    loopCollection.Expression,
                                    typeof(bool),
                                    delegateParams.ToArray()
                                );
                            }
                            catch (Exception ex)
                            {
                                errors.Add(
                                    $"[语法错误] While节点 '{whileModel.StepName}' 编译失败: {ex.Message}"
                                );
                            }
                        }
                    }

                    whileNode.LoopBranch = compiledBranch;
                    compiledNodes.Add(whileNode);
                }
                // ==========================================
                // 🎯 场景 A-2：If 条件节点 (保留你原本完美的代码)
                // ==========================================
                else if (model is ConditionStep conditionModel)
                {
                    var ifNode = new CompiledIfNode();
                    nodeLookup.Add(model.StepID, ifNode);

                    var delegateParams = new List<Parameter>();
                    var compiledVarTypes = new Dictionary<Guid, Type>();
                    var compiledVarIds = new List<Guid>();

                    foreach (var localVar in conditionModel.LocalVariables)
                    {
                        Type varType = typeof(double);
                        try
                        {
                            varType = Type.GetType(localVar.DataTypeName) ?? typeof(double);
                            if (TypeCache.TryGetValue(localVar.DataTypeName, out var type))
                            {
                                varType = type;
                            }
                            else
                            {
                                varType = TypeHelper.GetActualTypeFromLink(localVar.DataTypeName);
                                TypeCache[localVar.DataTypeName] = varType;
                            }

                            if (!TypeHelper.IsSafeExpressionType(varType))
                            {
                                errors.Add($"[安全拦截] 变量 '{localVar.Name}' 数据类型不合法！");
                                continue;
                            }
                        }
                        catch { }

                        delegateParams.Add(new Parameter(localVar.Name, varType));
                        compiledVarTypes[localVar.Id] = varType;
                        compiledVarIds.Add(localVar.Id);
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
                                compiledCondition = _interpreter.Parse(
                                    "true",
                                    typeof(bool),
                                    delegateParams.ToArray()
                                );
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
                                compiledCondition = _interpreter.Parse(
                                    childCollection.Expression,
                                    typeof(bool),
                                    delegateParams.ToArray()
                                );
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
                                LocalVarIds = compiledVarIds,
                                VarTypes = compiledVarTypes,
                                ExecutionSteps = childNodes,
                            }
                        );
                    }
                    compiledNodes.Add(ifNode);
                }
                // ==========================================
                // 🎯 场景 A-3：For 计次循环节点
                // ==========================================
                else if (model is ForStep forModel)
                {
                    var forNode = new CompiledForNode();
                    forNode.DefaultLoopCount = forModel.DefaultLoopCount;
                    nodeLookup.Add(model.StepID, forNode);

                    // For 节点没有局部变量和动态表达式，只需要递归编译它里面的算子即可
                    var loopCollection = forModel.Children.FirstOrDefault();
                    if (loopCollection != null && loopCollection.Steps != null)
                    {
                        forNode.LoopBody = CompileSteps(
                            loopCollection.Steps,
                            pluginLookup,
                            nodeLookup,
                            errors
                        );
                    }

                    compiledNodes.Add(forNode);
                }
                else if (model.PluginTypeName == "BuiltIn_Break")
                {
                    var breakNode = new CompiledBreakNode();
                    nodeLookup.Add(model.StepID, breakNode);
                    compiledNodes.Add(breakNode);
                }
                else if (model.PluginTypeName == "BuiltIn_Continue")
                {
                    var continueNode = new CompiledContinueNode();
                    nodeLookup.Add(model.StepID, continueNode);
                    compiledNodes.Add(continueNode);
                }
                else if (model.PluginTypeName == "BuiltIn_Return")
                {
                    var returnNode = new CompiledReturnNode();
                    nodeLookup.Add(model.StepID, returnNode);
                    compiledNodes.Add(returnNode);
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
                if (model == null || model.IsDisEnable)
                    continue;

                if (!nodeLookup.TryGetValue(model.StepID, out var targetNode))
                    continue;

                // --- 连线绑定 ---
                foreach (var linkKvp in model.LinkedSources)
                {
                    var myInputName = linkKvp.Key;
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
                        else if (nodeLookup.TryGetValue(linkRef.TargetStepId, out var upNode))
                        {
                            if (
                                upNode is CompiledForNode forNode
                                && linkRef.TargetPortName == "Index"
                            )
                            {
                                sourcePort = forNode.IndexPort;
                                actualUpstreamName = "ForLoop"; // 或者从图纸查名字
                            }
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
                        if (targetNode is CompiledPluginNode pluginNode)
                        {
                            // 普通算子：myInputName 依然是 "InImage" 这样的真实名字
                            if (
                                pluginNode.ExternalPlugin.Inputs.TryGetValue(
                                    myInputName,
                                    out var myInPort
                                )
                            )
                                myInPort.LinkedSource = sourcePort;
                        }
                        else if (targetNode is CompiledIfNode ifNode)
                        {
                            // 🌟 If 算子：myInputName 其实是 Guid 的 ToString()！直接转回 Guid 存进去！
                            if (Guid.TryParse(myInputName, out Guid varId))
                            {
                                ifNode.UpstreamLinks[varId] = sourcePort;
                            }
                        }
                        // 🌟🌟 补全：While 算子的连线逻辑 (和 If 一模一样，都是接收 Guid 作为键)
                        else if (targetNode is CompiledWhileNode whileNode)
                        {
                            if (Guid.TryParse(myInputName, out Guid varId))
                            {
                                whileNode.UpstreamLinks[varId] = sourcePort;
                            }
                        }
                        // 🌟🌟 补全：For 算子接收外部传来的循环次数 (连线名我们在注册时叫 "LoopCount")
                        else if (targetNode is CompiledForNode forNode)
                        {
                            if (myInputName == "LoopCount")
                            {
                                forNode.LoopCountLink = sourcePort;
                            }
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
                // 💡 注意：因为 WhileStep 继承自 ConditionStep，所以这里会自动包含了 If 和 While 两种容器！
                if (model is ConditionStep conditionModel && conditionModel.Children != null)
                {
                    foreach (var branch in conditionModel.Children)
                        if (branch.Steps != null)
                            LinkPorts(branch.Steps, nodeLookup, pluginLookup, errors);
                }
                // 🌟🌟 补全：For 容器的递归遍历
                else if (model is ForStep forModel && forModel.Children != null)
                {
                    foreach (var branch in forModel.Children)
                        if (branch.Steps != null)
                            LinkPorts(branch.Steps, nodeLookup, pluginLookup, errors);
                }
            }
        }
    }
}
