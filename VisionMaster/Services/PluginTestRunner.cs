using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 插件试运行基建（通用，所有插件免费获得）
    /// 与正式运行走同一套链路：FlowCompiler 编译 → CompiledNode.RunAndGetNext → 正式 ExecutionContext
    ///
    /// 执行载体分工（所见即所得的关键）：
    /// - 上游依赖链：在编译实例上执行，产出真实数据（按 DependencyMap 深度优先后序，与目标无关的分支不执行）
    /// - 目标插件：在配置实例（配置窗口绑定的那个实例）上执行——
    ///   执行前把编译实例目标节点的输入端口实际值（上游输出/常量/全局变量/运行时变量）快照灌入
    ///   配置实例的同名输入端口，再调 configPlugin.Execute。
    ///   这样 RunAlgorithm 里赋值的属性（如 PreviewImage）和输出端口都落在界面可见的实例上。
    ///
    /// 会话生命周期：编译出的临时会话所有权转移给调用方（配置窗口），
    /// 上游实例的输出数据保留其中供调试查看；由调用方在窗口关闭（或下次试运行替换）时 Dispose，
    /// 以此兼顾"数据留存调试"与"非托管资源防泄漏"。
    /// </summary>
    public static class PluginTestRunner
    {
        /// <summary>
        /// 试运行目标节点及其上游依赖链
        /// </summary>
        /// <param name="configPlugin">配置实例（配置窗口绑定的插件实例）：目标插件在它上面执行，数据落点所见即所得</param>
        /// <param name="stepData">目标步骤配置数据</param>
        /// <param name="workspace">工作空间</param>
        /// <param name="logger">日志服务</param>
        /// <param name="compiler">流程编译器</param>
        /// <param name="trialSession">输出的试运行会话（含上游链编译实例及输出数据，所有权归调用方）；
        /// 编译失败/步骤不存在时为 null，执行失败时仍有已执行部分的留存数据</param>
        /// <returns>执行结果</returns>
        public static PluginExecuteResult Run(
            IVisionPlugin configPlugin,
            IStepConfigData stepData,
            IWorkspaceManager workspace,
            ILogService logger,
            FlowCompiler compiler,
            out FlowSession trialSession)
        {
            var sw = Stopwatch.StartNew();
            trialSession = null;

            if (configPlugin == null)
                return PluginExecuteResult.Fail(0, "插件配置实例为空");
            if (stepData == null)
                return PluginExecuteResult.Fail(0, "步骤配置数据为空");
            if (workspace?.CurrentFlow == null)
                return PluginExecuteResult.Fail(0, "当前没有打开的流程");

            var flow = workspace.CurrentFlow;

            // 1. 与正式运行相同：编译整个流程（所有链接类型按正式规则解析接线）
            var result = compiler.Compile(flow.Steps, flow.FlowName);
            if (!result.Success)
                return PluginExecuteResult.Fail(
                    sw.ElapsedMilliseconds,
                    "编译失败:\n" + string.Join("\n", result.Errors));

            var compiledFlow = result.Data;

            // 2. 定位目标节点
            if (!compiledFlow.NodeLookup.TryGetValue(stepData.StepId, out var targetNode))
                return PluginExecuteResult.Fail(sw.ElapsedMilliseconds, "目标步骤不存在或已被禁用");

            // 3. 临时会话：仅用于节点状态回报（流程图上显示 运行中/成功/失败），不注册到 RuntimeManager
            //    会话从这里创建，所有权即转移给调用方（本方法不再负责 Dispose）
            var session = new FlowSession { FlowName = flow.FlowName };
            foreach (var step in flow.Steps)
                session.Blueprints.Add(step);

            trialSession = session;

            // 4. 正式执行上下文（与正式运行一致）
            var context = new ExecutionContext(logger, session, workspace, CancellationToken.None);

            try
            {
                if (targetNode is CompiledPluginNode targetPluginNode)
                {
                    // 5a. 只执行上游链（编译实例），目标留给配置实例执行
                    ExecuteUpstreams(stepData.StepId, compiledFlow, context, new HashSet<Guid>());

                    // 5b. 数据桥接：把编译实例目标节点的输入实际值快照灌进配置实例同名端口
                    BridgeInputs(targetPluginNode, configPlugin, context);

                    // 5c. 在配置实例上执行目标插件（界面绑定的实例，所见即所得）
                    MarkStepState(session, stepData.StepId, StepState.Running, 0);
                    try
                    {
                        configPlugin.Execute(context);
                        MarkStepState(session, stepData.StepId, StepState.Success, sw.ElapsedMilliseconds);
                    }
                    catch (Exception)
                    {
                        MarkStepState(session, stepData.StepId, StepState.Failed, sw.ElapsedMilliseconds);
                        throw;
                    }
                }
                else
                {
                    // 容器节点（If/While/For）没有"配置实例"概念：整体（含上游链）在编译实例上执行
                    ExecuteChain(stepData.StepId, compiledFlow, context, new HashSet<Guid>());
                }
            }
            catch (Exception ex)
            {
                // 执行失败：会话不释放，已执行部分的数据留存供调试查看
                return PluginExecuteResult.Fail(sw.ElapsedMilliseconds, $"执行异常: {ex.Message}");
            }

            // 6. 收集结果：按约定从配置实例读取 Success / ErrorMessage 输出端口
            var success = true;
            var info = string.Empty;

            if (configPlugin.Outputs.TryGetValue("Success", out var successPort))
                success = successPort.Value is bool b && b;

            if (configPlugin.Outputs.TryGetValue("ErrorMessage", out var errorPort))
                info = errorPort.Value?.ToString() ?? string.Empty;

            return success
                ? PluginExecuteResult.Ok(sw.ElapsedMilliseconds, string.IsNullOrEmpty(info) ? "执行完成" : info)
                : PluginExecuteResult.Fail(sw.ElapsedMilliseconds, string.IsNullOrEmpty(info) ? "执行失败" : info);
        }

        /// <summary>
        /// 只执行当前节点的全部上游依赖（不含当前节点本身），供数据桥接前供数
        /// </summary>
        private static void ExecuteUpstreams(
            Guid nodeId,
            CompiledFlow flow,
            IExecutionContext context,
            HashSet<Guid> executed)
        {
            if (flow.DependencyMap.TryGetValue(nodeId, out var upstreams))
            {
                foreach (var upstreamId in upstreams)
                    ExecuteChain(upstreamId, flow, context, executed);
            }
        }

        /// <summary>
        /// 深度优先后序执行：先递归执行当前节点的所有上游依赖，再执行当前节点本身
        /// executed 集合防止多输入汇聚同一上游时重复执行，同时防御循环依赖
        /// </summary>
        private static void ExecuteChain(
            Guid nodeId,
            CompiledFlow flow,
            IExecutionContext context,
            HashSet<Guid> executed)
        {
            if (!executed.Add(nodeId))
                return;

            // 先执行上游（递归，天然形成拓扑顺序）
            if (flow.DependencyMap.TryGetValue(nodeId, out var upstreams))
            {
                foreach (var upstreamId in upstreams)
                    ExecuteChain(upstreamId, flow, context, executed);
            }

            // 再执行当前节点
            if (!flow.NodeLookup.TryGetValue(nodeId, out var node))
                return; // 上游节点不存在（如被禁用）：编译期已容忍，跳过

            try
            {
                node.RunAndGetNext(context);
            }
            catch (Exception ex)
            {
                // 带上步骤名抛出，让用户知道是链上哪一步失败
                throw new Exception($"执行步骤 [{node.Name}] 失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 数据桥接：把编译实例目标节点的各输入端口实际值（上游输出/常量/全局变量/运行时变量的统一取值结果）
        /// 快照灌进配置实例的同名输入端口，使配置实例执行 RunAlgorithm 时能取到上游真实数据
        /// </summary>
        private static void BridgeInputs(
            CompiledPluginNode targetNode,
            IVisionPlugin configPlugin,
            IExecutionContext context)
        {
            // 运行时变量代理端口先绑定上下文，让 GetActualValue 能取到 LocalVariables 的最新值
            foreach (var binding in targetNode.ContextAwareBindings)
                binding.BindContext(context);

            foreach (var kvp in targetNode.ExternalPlugin.Inputs)
            {
                var actual = kvp.Value.GetActualValue();

                if (configPlugin.Inputs.TryGetValue(kvp.Key, out var configPort))
                {
                    configPort.LinkedSource = null;
                    configPort.Value = actual; // 值快照（HImage 为引用复制，上游编译实例在窗口关闭前一直存活）
                }
            }
        }

        /// <summary>
        /// 手动更新目标步骤的运行状态（配置实例执行绕过了 CompiledNode 的状态回报，这里补上，
        /// 使流程图上目标步骤同样显示 运行中/成功/失败）
        /// </summary>
        private static void MarkStepState(FlowSession session, Guid stepId, StepState state, long elapsedMs)
        {
            var step = session.Blueprints.FirstOrDefault(s => s.StepID == stepId);
            if (step == null)
                return;

            step.State = state;

            if (state == StepState.Running)
            {
                step.IsRunningFocus = true;
                step.LastRunStartTime = DateTime.Now;
            }
            else
            {
                step.IsRunningFocus = false;
                step.LastRunTimeMs = elapsedMs;
            }
        }
    }
}
