using System;
using System.Collections.Concurrent;
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
        // 运行时变量标记 Guid 已上收到协议层 Core.Interfaces.LinkProtocol，
        // 因为生产端（变量绑定弹窗）与消费端（此处）必须共用同一份定义。

        /// <summary>
        /// 类型缓存（静态共享，跨编译复用）。使用线程安全的 ConcurrentDictionary：
        /// 流程编译可能在多线程下进行，避免普通 Dictionary 在并发读写时损坏
        /// </summary>
        static ConcurrentDictionary<string, Type> TypeCache = new ConcurrentDictionary<string, Type>(
            1,
            50,
            StringComparer.Ordinal
        );

        /// <summary>
        /// 创建表达式解释器。DynamicExpresso.Interpreter 不是线程安全的，因此不能跨线程共享单个实例；
        /// 每次解析表达式时新建一个（构造开销极低，且无状态），从根本上消除并发隐患
        /// </summary>
        private Interpreter CreateInterpreter() => new Interpreter().Reference(typeof(Math));

        private readonly IWorkspaceManager workspaceManager;

        public FlowCompiler(IWorkspaceManager workspaceManager)
        {
            this.workspaceManager = workspaceManager;
        }

        /// <summary>
        /// 构造带步骤定位的编译错误。
        /// 统一走这里，避免各处漏填 StepId 导致画布无法把错误红框定位到节点。
        /// </summary>
        private static CompilationError Err(StepModel owner, string message)
            => new CompilationError
            {
                StepId = owner?.StepID,
                StepName = owner?.StepName,
                Message = message,
            };

        /// <summary>
        /// 将条件步骤中引用的运行时变量添加到 delegateParams
        /// 返回运行时变量名称列表和类型映射，供运行时从 context.LocalVariables 取值
        /// </summary>
        private (List<string> runtimeVarNames, Dictionary<string, Type> runtimeVarTypes) 
            CompileRuntimeVarRefs(
                StepModel owner,
                IEnumerable<LocalVariableItem> runtimeRefs,
                List<Parameter> delegateParams,
                List<CompilationError> errors)
        {
            var names = new List<string>();
            var types = new Dictionary<string, Type>();
            if (runtimeRefs == null)
                return (names, types);

            foreach (var runtimeVar in runtimeRefs)
            {
                if (string.IsNullOrWhiteSpace(runtimeVar?.Name))
                    continue;

                Type varType = typeof(double);
                try
                {
                    varType = Type.GetType(runtimeVar.DataTypeName) ?? typeof(double);
                    if (TypeCache.TryGetValue(runtimeVar.DataTypeName, out var type))
                        varType = type;
                    else
                    {
                        varType = TypeHelper.GetActualTypeFromLink(runtimeVar.DataTypeName);
                        TypeCache[runtimeVar.DataTypeName] = varType;
                    }

                    if (!TypeHelper.IsSafeExpressionType(varType))
                    {
                        errors.Add(Err(owner, $"[安全拦截] 运行时变量 '{runtimeVar.Name}' 数据类型不合法！"));
                        continue;
                    }
                }
                catch { }

                delegateParams.Add(new Parameter(runtimeVar.Name, varType));
                names.Add(runtimeVar.Name);
                types[runtimeVar.Name] = varType;
            }

            return (names, types);
        }

        /// <summary>
        /// 编译条件/循环节点的局部变量为 DynamicExpresso 参数与类型映射（While / If 共用）
        /// 负责安全类型校验与类型缓存，返回供表达式解析使用的 delegateParams 及类型映射
        /// </summary>
        private (List<Parameter> delegateParams, Dictionary<Guid, Type> compiledVarTypes, List<Guid> compiledVarIds)
            CompileLocalVarParams(StepModel owner, IEnumerable<LocalVariableItem> localVariables, List<CompilationError> errors)
        {
            var delegateParams = new List<Parameter>();
            var compiledVarTypes = new Dictionary<Guid, Type>();
            var compiledVarIds = new List<Guid>();

            foreach (var localVar in localVariables)
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
                        errors.Add(Err(owner, $"[安全拦截] 变量 '{localVar.Name}' 数据类型不合法！"));
                        continue;
                    }
                }
                catch { }

                delegateParams.Add(new Parameter(localVar.Name, varType));
                compiledVarTypes[localVar.Id] = varType;
                compiledVarIds.Add(localVar.Id);
            }

            return (delegateParams, compiledVarTypes, compiledVarIds);
        }

        public CompilationResult Compile(IEnumerable<StepModel> blueprints, string? flowName = null)
        {
            var result = new CompilationResult();

            var pluginLookup = new Dictionary<Guid, IVisionPlugin>();
            var nodeLookup = new Dictionary<Guid, CompiledNode>();
            var dependencyMap = new Dictionary<Guid, List<Guid>>();

            try
            {
                var rootNodes = CompileSteps(blueprints, flowName, pluginLookup, nodeLookup, result.Errors);

                LinkPorts(blueprints, nodeLookup, pluginLookup, dependencyMap, result.Errors);

                // 结构层取数检查（M2-1）：数据依赖倒序 / 跨分支取数。
                // 必须在 LinkPorts 之后：那条链路里的 [致命断连] 等错误要保持原有文案与顺序不变，
                // 本检查只做追加；放在 result.Success 赋值之前才能保证非法图纸编不出 CompiledFlow。
                CheckLinkOrder(blueprints, result.Errors);

                result.Success = result.Errors.Count == 0;
                if (result.Success)
                {
                    result.Data = new CompiledFlow(rootNodes, pluginLookup, nodeLookup, dependencyMap);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(new CompilationError
                {
                    // 异常发生在步骤遍历之外，无法归属到具体步骤，StepId 留空表示流程级错误
                    Message = "系统崩溃级错误: " + ex.Message,
                });
            }
            return result;
        }

        private List<CompiledNode> CompileSteps(
            IEnumerable<StepModel> models,
            string? flowName,
            Dictionary<Guid, IVisionPlugin> pluginLookup,
            Dictionary<Guid, CompiledNode> nodeLookup,
            List<CompilationError> errors
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
                        var whileNode = new CompiledWhileNode { Id = model.StepID, Name = model.StepName, StepName = model.StepName };
                        nodeLookup.Add(model.StepID, whileNode);

                    // 1. 编译局部变量（While / If 共用逻辑，提取至 CompileLocalVarParams）
                    var (delegateParams, compiledVarTypes, compiledVarIds) =
                        CompileLocalVarParams(model, whileModel.LocalVariables, errors);

                    // 2. 编译运行时变量引用（从 context.LocalVariables 取值）
                    var (runtimeVarNames, runtimeVarTypes) = CompileRuntimeVarRefs(
                        model, whileModel.RuntimeVariableRefs, delegateParams, errors);

                    // 3. 提取唯一的循环分支
                    var loopCollection = whileModel.Children.FirstOrDefault();
                    var compiledBranch = new CompiledBranch
                    {
                        LocalVarIds = compiledVarIds,
                        VarTypes = compiledVarTypes,
                        RuntimeVarNames = runtimeVarNames,
                        RuntimeVarTypes = runtimeVarTypes,
                        ExecutionSteps = new List<CompiledNode>(),
                    };

                    if (loopCollection != null)
                    {
                        if (loopCollection.Steps != null)
                        {
                            compiledBranch.ExecutionSteps = CompileSteps(
                                loopCollection.Steps,
                                flowName,
                                pluginLookup,
                                nodeLookup,
                                errors
                            );
                        }

                        // 编译 While 的触发条件
                        if (string.IsNullOrWhiteSpace(loopCollection.Expression))
                        {
                            // 措辞纠偏：errors.Add 会阻断编译（Success=Errors.Count==0），这是硬错误不是警告
                            errors.Add(
                                Err(model, $"[编译错误] '{whileModel.StepName}' 的循环条件表达式为空。")
                            );
                        }
                        else
                        {
                            try
                            {
                                compiledBranch.ConditionLambda = CreateInterpreter().Parse(
                                    loopCollection.Expression,
                                    typeof(bool),
                                    delegateParams.ToArray()
                                );
                            }
                            catch (Exception ex)
                            {
                                errors.Add(
                                    Err(model, $"[语法错误] While节点 '{whileModel.StepName}' 编译失败: {ex.Message}")
                                );
                            }
                        }
                    }

                    whileNode.LoopBranch = compiledBranch;
                    compiledNodes.Add(whileNode);
                }
                // ==========================================
                // 🎯 场景 A-2：If 条件节点
                // ==========================================
                else if (model is ConditionStep conditionModel)
                {
                    var ifNode = new CompiledIfNode { Id = model.StepID, Name = model.StepName, StepName = model.StepName };
                    nodeLookup.Add(model.StepID, ifNode);

                    // 编译局部变量（While / If 共用逻辑，提取至 CompileLocalVarParams）
                    var (delegateParams, compiledVarTypes, compiledVarIds) =
                        CompileLocalVarParams(model, conditionModel.LocalVariables, errors);

                    // 编译运行时变量引用（从 context.LocalVariables 取值）
                    var (runtimeVarNames, runtimeVarTypes) = CompileRuntimeVarRefs(
                        model, conditionModel.RuntimeVariableRefs, delegateParams, errors);

                    // 编译分支
                    foreach (var childCollection in conditionModel.Children)
                    {
                        var childNodes = CompileSteps(
                            childCollection.Steps,
                            flowName,
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
                                compiledCondition = CreateInterpreter().Parse(
                                    "true",
                                    typeof(bool),
                                    delegateParams.ToArray()
                                );
                            }
                            catch { }
                        }
                        else if (string.IsNullOrWhiteSpace(childCollection.Expression))
                        {
                            // 措辞纠偏：errors.Add 会阻断编译（Success=Errors.Count==0），这是硬错误不是警告
                            errors.Add(
                                Err(model, $"[编译错误] '{model.StepName}' 的分支 '{childCollection.StepName}' 表达式为空。")
                            );
                        }
                        else
                        {
                            try
                            {
                                compiledCondition = CreateInterpreter().Parse(
                                    childCollection.Expression,
                                    typeof(bool),
                                    delegateParams.ToArray()
                                );
                            }
                            catch (Exception ex)
                            {
                                errors.Add(
                                    Err(model, $"[语法错误] 节点 '{model.StepName}' 编译失败: {ex.Message}")
                                );
                            }
                        }

                        ifNode.Branches.Add(
                            new CompiledBranch
                            {
                                ConditionLambda = compiledCondition,
                                LocalVarIds = compiledVarIds,
                                VarTypes = compiledVarTypes,
                                RuntimeVarNames = runtimeVarNames,
                                RuntimeVarTypes = runtimeVarTypes,
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
                    var forNode = new CompiledForNode { Id = model.StepID, Name = model.StepName, StepName = model.StepName };
                    forNode.DefaultLoopCount = forModel.DefaultLoopCount;
                    nodeLookup.Add(model.StepID, forNode);

                    var loopCollection = forModel.Children.FirstOrDefault();
                    if (loopCollection != null && loopCollection.Steps != null)
                    {
                        forNode.LoopBody = CompileSteps(
                            loopCollection.Steps,
                            flowName,
                            pluginLookup,
                            nodeLookup,
                            errors
                        );
                    }

                    compiledNodes.Add(forNode);
                }
                else if (model.PluginTypeName == "BuiltIn_Break")
                {
                    var breakNode = new CompiledBreakNode { Id = model.StepID, Name = model.StepName, StepName = model.StepName };
                    nodeLookup.Add(model.StepID, breakNode);
                    compiledNodes.Add(breakNode);
                }
                else if (model.PluginTypeName == "BuiltIn_Continue")
                {
                    var continueNode = new CompiledContinueNode { Id = model.StepID, Name = model.StepName, StepName = model.StepName };
                    nodeLookup.Add(model.StepID, continueNode);
                    compiledNodes.Add(continueNode);
                }
                else if (model.PluginTypeName == "BuiltIn_Return")
                {
                    var returnNode = new CompiledReturnNode { Id = model.StepID, Name = model.StepName, StepName = model.StepName };
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
                        plugin.InstanceName = string.IsNullOrEmpty(flowName) 
                            ? model.StepName 
                            : $"{flowName}.{model.StepName}";

                        pluginLookup.Add(model.StepID, plugin);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(Err(model, $"[加载失败] 算子 '{model.StepName}': {ex.Message}"));
                        continue;
                    }

                    // 灌入静态固定参数（输入端口 + [StepConfig] 配置属性；链接端口跳过，以链接为准）
                    if (plugin is VisionPluginBase pluginBase)
                    {
                        pluginBase.ApplyConfigValues(model);

                        // 动态输出端口：编译前按 StepModel.OutputPortNames 快照重建
                        // （配置窗口里画的 ROI 存在快照里，编译实例据此生成对应的动态端口供接线）
                        if (plugin is IDynamicOutputProvider dynProvider)
                            dynProvider.RebuildDynamicOutputs();
                    }
                    else
                    {
                        foreach (var kvp in model.InputValues)
                        {
                            if (plugin.Inputs.TryGetValue(kvp.Key, out var inputPort))
                                inputPort.Value = kvp.Value;
                        }
                    }

                    var pluginNode = new CompiledPluginNode { Id = model.StepID, Name = model.StepName, StepName = model.StepName, ExternalPlugin = plugin };
                    nodeLookup.Add(model.StepID, pluginNode);
                    compiledNodes.Add(pluginNode);
                }
            }
            return compiledNodes;
        }

        private void LinkPorts(
            IEnumerable<StepModel> models,
            Dictionary<Guid, CompiledNode> nodeLookup,
            Dictionary<Guid, IVisionPlugin> pluginLookup, // 只有真正的算子才能作为【数据源】提供输出
            Dictionary<Guid, List<Guid>> dependencyMap,   // 节点依赖表：接线成功时记录 下游 -> 上游
            List<CompilationError> errors
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

                    // 按显式 Kind 分派数据源。旧工程 Kind 缺失时先按旧规则回填，
                    // 使"常量 vs 全局变量"不再依赖 DisplayAddress 的文案前缀
                var linkKind = linkRef.NormalizeKind();
                if (linkKind == LinkKind.RuntimeVariable)
                {
                    var varName = linkRef.TargetPortName;
                    if (string.IsNullOrWhiteSpace(varName))
                    {
                        errors.Add(
                            Err(model, $"[连线错误] '{model.StepName}' 引用的运行时变量名为空")
                        );
                    }
                    else if (targetNode is CompiledPluginNode pluginNode)
                    {
                        // 期望类型取自消费方 InputPort.DataType，让代理端口在变量未定义时返回类型默认值
                        Type expectedType = typeof(object);
                        if (
                            pluginNode.ExternalPlugin?.Inputs?.TryGetValue(
                                myInputName,
                                out var targetInPort
                            ) == true
                        )
                            expectedType = targetInPort.DataType;

                        var proxy = new RuntimeVariableProxyPort(varName, expectedType);
                        sourcePort = proxy;
                        // 加入节点的 ContextAwareBindings，节点执行前会被 BindContext
                        pluginNode.ContextAwareBindings.Add(proxy);
                        actualUpstreamName = "RuntimeVar";
                    }
                    else if (targetNode is CompiledIfNode || targetNode is CompiledWhileNode
                             || targetNode is CompiledForNode)
                    {
                        // 🌉 断桥修复：条件节点引脚与 For 的 LoopCount 端口支持引用运行时变量。
                        // 旧实现"仅普通算子支持"把唯一入口堵死，而条件模型的 RuntimeVariableRefs 又无任何
                        // UI 写入方——两条路全断，用户根本无法在 If/While/For 里用"变量定义"插件的变量。
                        // 期望类型：条件节点取 LocalVariables 里该 Guid 的声明类型；For 的 LoopCount 固定 int。
                        Type expectedType = typeof(object);
                        if (targetNode is CompiledForNode)
                        {
                            expectedType = typeof(int);
                        }
                        else if (model is ConditionStep condModel
                                 && Guid.TryParse(myInputName, out Guid condVarId))
                        {
                            var decl = condModel.LocalVariables.FirstOrDefault(x => x.Id == condVarId);
                            if (decl != null)
                                expectedType = TypeHelper.GetActualTypeFromLink(decl.DataTypeName);
                        }

                        var proxy = new RuntimeVariableProxyPort(varName, expectedType);
                        sourcePort = proxy;
                        targetNode.ContextAwareBindings.Add(proxy);
                        // 公共赋值段（L585+ 起）会把 sourcePort 写进 UpstreamLinks[varId] / forNode.LoopCountLink
                        actualUpstreamName = "RuntimeVar";
                    }
                    else
                    {
                        errors.Add(
                            Err(model, $"[连线错误] '{model.StepName}' 节点类型不支持引用运行时变量")
                        );
                    }
                }
                else if (linkKind == LinkKind.Constant)
                {
                    // 常量：TargetPortName 直接就是常量值字符串，无需再看显示串前缀
                    var constantValue = linkRef.TargetPortName;
                    actualUpstreamName = "常量";
                    Type targetType = typeof(string);
                    if (targetNode is CompiledPluginNode pluginNode && pluginNode.ExternalPlugin?.Inputs?.TryGetValue(myInputName, out var targetPort) == true)
                    {
                        targetType = targetPort?.DataType ?? typeof(string);
                    }
                    sourcePort = new ConstantOutputPort(constantValue, targetType);
                }
                else if (linkKind == LinkKind.GlobalVariable)
                {
                    sourcePort = workspaceManager.GlobalVariables.FirstOrDefault(s =>
                        s.Name == linkRef.TargetPortName
                    );
                    if (sourcePort == null)
                        errors.Add(
                                Err(model, $"[连线断开] '{model.StepName}' 找不到全局变量: '{linkRef.TargetPortName}'")
                            );
                    actualUpstreamName = "Global";
                }
                    // 其余即 StepPort：上游一定是个真正的 Plugin，去 pluginLookup 找输出端口
                    else
                    {
                        // 🌟 上游一定是一个真正的 Plugin，所以去 pluginLookup 找输出端口
                        if (pluginLookup.TryGetValue(linkRef.TargetStepId, out var upPlugin))
                        {
                            // 记录执行依赖：下游节点依赖上游节点（供试运行先执行上游链）
                            AddDependency(dependencyMap, model.StepID, linkRef.TargetStepId);

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
                                        Err(model, $"[连线断开] 上游 '{actualUpstreamName}' 不存在输出 '{cleanPortName}'")
                                    );
                                }
                            }
                        }
                        else if (nodeLookup.TryGetValue(linkRef.TargetStepId, out var upNode))
                        {
                            if (
                                upNode is CompiledForNode forNode
                                && linkRef.TargetPortName == "Index"
                            )
                            {
                                sourcePort = forNode.IndexPort;
                                // 记录执行依赖：下游依赖 For 节点的 Index 输出（供试运行先执行 For）
                                AddDependency(dependencyMap, model.StepID, linkRef.TargetStepId);
                                actualUpstreamName = "ForLoop"; // 或者从图纸查名字
                            }
                        }
                        else
                        {
                            errors.Add(
                                Err(model, $"[致命断连] '{model.StepName}' 引用的上游节点 (ID:{linkRef.TargetStepId}) 不存在！")
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
                                Err(model, $"[参数缺失] '{model.StepName}' 的必填参数 '{input.Name}' 未配置！")
                            );
                    }
                }

                // --- 递归子分支连线 ---
                // 💡 注意：因为 WhileStep 继承自 ConditionStep，所以这里会自动包含了 If 和 While 两种容器！
                if (model is ConditionStep conditionModel && conditionModel.Children != null)
                {
                    foreach (var branch in conditionModel.Children)
                        if (branch.Steps != null)
                            LinkPorts(branch.Steps, nodeLookup, pluginLookup, dependencyMap, errors);
                }
                // 🌟🌟 补全：For 容器的递归遍历
                else if (model is ForStep forModel && forModel.Children != null)
                {
                    foreach (var branch in forModel.Children)
                        if (branch.Steps != null)
                            LinkPorts(branch.Steps, nodeLookup, pluginLookup, dependencyMap, errors);
                }
            }
        }

        /// <summary>
        /// 记录节点执行依赖（下游 -> 上游），供试运行按依赖顺序先执行上游链
        /// </summary>
        private static void AddDependency(Dictionary<Guid, List<Guid>> dependencyMap, Guid downstream, Guid upstream)
        {
            if (!dependencyMap.TryGetValue(downstream, out var deps))
                dependencyMap[downstream] = deps = new List<Guid>();

            if (!deps.Contains(upstream))
                deps.Add(upstream);
        }

        /// <summary>
        /// 结构层取数合法性检查：把「数据依赖倒序」与「跨分支取数」挡在编译期。
        ///
        /// 为什么必须报错（而不是"能跑就行"）：
        /// dependencyMap 只服务单步试运行——试运行会先把上游链跑一遍，于是倒序/跨分支的连线"看起来是对的"；
        /// 而全速运行严格按步骤集合的下标顺序执行，同一份图纸会取到 null 或上一轮的陈旧值。
        /// 两种跑法结果不同的问题在现场根本排查不出来，只能编译期挡死。
        ///
        /// 为什么写成 internal static：不依赖 workspaceManager、不依赖已编译节点，
        /// 纯函数便于断言程序直接调用（无需为了测试去凑一个 IWorkspaceManager）。
        /// </summary>
        /// <param name="blueprints">流程图纸（含各层容器步骤）</param>
        /// <param name="errors">错误收集器；本方法只追加，绝不改动已有错误的文案与顺序</param>
        internal static void CheckLinkOrder(IEnumerable<StepModel> blueprints, List<CompilationError> errors)
        {
            if (blueprints == null || errors == null)
                return;

            // 拓扑快照一次遍历建索引；Positions 已覆盖全部嵌套层级（含 For 循环体），
            // 所以此处不再自己写递归——少一套递归，就少一次"漏了某种容器"的机会。
            var topology = FlowTopology.Build(blueprints);

            foreach (var consumer in topology.Positions)
            {
                // FlowTopology.Walk 建行前已剔除空步骤，故 consumer / consumer.Step 恒非空；
                // 这里仍保留一层判空，将来若改了 Walk 也不会在这里变成静默 NRE
                StepModel model = consumer.Step;
                if (model == null || model.IsDisEnable)
                    continue;

                // LinkedSources 以"输入端口名"为键，因此天然满足"同一对 (消费步骤, 输入端口) 只报一条"
                if (model.LinkedSources == null)
                    continue;

                foreach (var linkKvp in model.LinkedSources)
                {
                    LinkReference linkRef = linkKvp.Value;
                    if (linkRef == null)
                        continue;

                    // 只检查"取别的步骤的输出"：全局变量 / 运行时变量 / 常量与步骤执行顺序无关，一律跳过
                    if (linkRef.NormalizeKind() != LinkKind.StepPort)
                        continue;

                    // 找不到 producer：连线指向野 Id，LinkPorts 已报 [致命断连]，此处不重复报
                    if (!topology.TryGet(linkRef.TargetStepId, out var producer))
                        continue;

                    // 产出步骤被禁用时同样不报：它压根不参与执行，
                    // 且强行报倒序会与已有的"致命断连"类错误重复，同一根因刷两条只会干扰排查
                    if (producer.Step == null || producer.Step.IsDisEnable)
                        continue;

                    LinkLegality legality = topology.Classify(producer.StepId, consumer.StepId);
                    string? message = legality switch
                    {
                        // 自连：结构上"自己取自己"永远不可能先产出再取用，归入依赖倒序，
                        // 但提示语必须换成自环的说法，否则会生成"把 A 拖到 A 之前"这种无意义指引
                        LinkLegality.SameListReversed when producer.StepId == consumer.StepId
                            => $"[依赖倒序] '{StepName(model)}' 的输入引用了自身的输出（本层第 {consumer.IndexInOwner + 1} 步 → 第 {producer.IndexInOwner + 1} 步）。" +
                               "自己不可能先于自己产出，连续运行时取到的必是上一轮的陈旧值。" +
                               "请改由真正的上游步骤提供该输入，若确实要跨轮次取值请改用运行时变量。",

                        // 同层排在后面却取前面的输出，以及"跨层但产出方排在包住消费方的容器之后"，
                        // 本质都是同一件事：执行到这一步时上游还没跑，统一按依赖倒序报
                        LinkLegality.SameListReversed
                            or LinkLegality.ProducerAfterEnclosingContainer
                            => $"[依赖倒序] '{StepName(model)}' 排在 '{StepName(producer.Step)}' 之后" +
                               $"（{DescribeOrder(topology, consumer, producer)}），却引用了 '{StepName(producer.Step)}' 的输出。" +
                               "全速运行时将取到空值或上一轮的陈旧值；单步调试会掩盖该问题。" +
                               $"请把 '{StepName(producer.Step)}' 拖到 '{StepName(model)}' 之前，若确实要跨轮次取值请改用运行时变量。",

                        // 兄弟分支互取、或取了别人子树里的输出：这一轮根本轮不到对方执行
                        LinkLegality.CrossBranch
                            => $"[跨分支取数] '{StepName(model)}'（{DescribePosition(topology, consumer)}）" +
                               $"引用了另一分支中 '{StepName(producer.Step)}'（{DescribePosition(topology, producer)}）的输出，" +
                               "该分支本次可能未执行，取到的是空值或陈旧值。请改用全局变量或运行时变量传递。",

                        // 其余（SameListBefore / ProducerIsAncestor 合法，Unknown 说明 Id 已失效）不报
                        _ => null,
                    };

                    if (message != null)
                        errors.Add(Err(model, message));
                }
            }
        }

        /// <summary>
        /// 描述倒序连线双方的执行序号（1 基，面向操作人员；内部下标是 0 基，直接展示会让人差一位）。
        /// 双方同层时用"本层第 N 步 → 第 M 步"（与既有报错样例一致），跨层时各自带作用域路径。
        /// </summary>
        private static string DescribeOrder(FlowTopology topology, StepPosition consumer, StepPosition producer)
        {
            if (ReferenceEquals(consumer.Owner, producer.Owner))
                return $"本层第 {consumer.IndexInOwner + 1} 步 → 第 {producer.IndexInOwner + 1} 步";

            return $"{DescribePosition(topology, consumer)} → {DescribePosition(topology, producer)}";
        }

        /// <summary>单个步骤的位置描述：作用域 + 本层序号（1 基）</summary>
        private static string DescribePosition(FlowTopology topology, StepPosition position)
        {
            string seq = $"第 {position.IndexInOwner + 1} 步";
            return position.Depth == 0 ? $"流程{seq}" : $"{DescribeScope(topology, position)} {seq}";
        }

        /// <summary>
        /// 步骤所在的可读作用域：顶层写"流程"，嵌套写「容器名/分支名」由外及里的路径。
        /// 一个容器步骤可能有 If/Else 多个分支，只报容器名不足以定位，所以必须带上分支名。
        /// 祖先链走 FlowTopology.AncestorChain 这个公开入口（StepPosition 内部的数组是 internal，
        /// 跨程序集不可见，也不该为了让编译器省事而把快照的索引细节升格成公开契约）。
        /// </summary>
        private static string DescribeScope(FlowTopology topology, StepPosition position)
        {
            if (position.Depth == 0)
                return "流程";

            var ancestors = topology.AncestorChain(position);
            var segments = new string[ancestors.Count];
            for (int i = 0; i < ancestors.Count; i++)
            {
                // AncestorChain 是由近及远（[父, 祖父, ...]），报错要按人读路径的习惯由外及里，故倒序填
                StepPosition ancestor = ancestors[i];
                StepPosition holder = i == 0 ? position : ancestors[i - 1];
                segments[ancestors.Count - 1 - i] =
                    $"'{StepName(ancestor.Step)}/{BranchName(holder.Branch)}'";
            }

            return string.Join(" → ", segments);
        }

        private static string StepName(StepModel? step)
            => string.IsNullOrWhiteSpace(step?.StepName) ? "(未命名步骤)" : step.StepName;

        /// <summary>
        /// 分支名：优先用分支自身的 StepName（如"循环体"、"Else 分支"），
        /// 老数据可能没写名字，退化成枚举名，保证文案里永远不会出现空串
        /// </summary>
        private static string BranchName(StepCollection? branch)
        {
            if (branch == null)
                return "流程";

            return string.IsNullOrWhiteSpace(branch.StepName) ? branch.BranchType.ToString() : branch.StepName;
        }
    }
}
