using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 插件试运行基建（通用，所有插件免费获得）
    /// 与正式运行走同一套链路：FlowCompiler 编译 → CompiledNode.RunAndGetNext → 正式 ExecutionContext
    /// 执行范围：目标节点 + 其全部上游依赖链（按编译期记录的 DependencyMap 深度优先后序执行，先上游后目标），
    /// 与目标无关的分支不执行。因此上游输出端口能取到本次试运行产生的真实数据；
    /// 运行时变量链接由节点执行前的 BindContext 注入上下文，行为与正式运行一致。
    /// </summary>
    public static class PluginTestRunner
    {
        public static PluginExecuteResult Run(
            IStepConfigData stepData,
            IWorkspaceManager workspace,
            ILogService logger,
            FlowCompiler compiler)
        {
            var sw = Stopwatch.StartNew();

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
            var session = new FlowSession { FlowName = flow.FlowName };
            foreach (var step in flow.Steps)
                session.Blueprints.Add(step);

            // 4. 正式执行上下文（与正式运行一致）
            var context = new ExecutionContext(logger, session, workspace, CancellationToken.None);

            PluginExecuteResult finalResult;
            try
            {
                // 5. 按依赖顺序执行：先递归执行上游链，最后执行目标节点
                var executed = new HashSet<Guid>();
                ExecuteChain(stepData.StepId, compiledFlow, context, executed);

                // 6. 收集结果：按约定读取编译实例的 Success / ErrorMessage 输出端口
                // （必须在 Dispose 之前读取，释放后端口值可能已失效）
                var success = true;
                var info = string.Empty;

                if (compiledFlow.PluginLookup.TryGetValue(stepData.StepId, out var plugin))
                {
                    if (plugin.Outputs.TryGetValue("Success", out var successPort))
                        success = successPort.Value is bool b && b;

                    if (plugin.Outputs.TryGetValue("ErrorMessage", out var errorPort))
                        info = errorPort.Value?.ToString() ?? string.Empty;
                }

                finalResult = success
                    ? PluginExecuteResult.Ok(sw.ElapsedMilliseconds, string.IsNullOrEmpty(info) ? "执行完成" : info)
                    : PluginExecuteResult.Fail(sw.ElapsedMilliseconds, string.IsNullOrEmpty(info) ? "执行失败" : info);
            }
            catch (Exception ex)
            {
                return PluginExecuteResult.Fail(sw.ElapsedMilliseconds, $"执行异常: {ex.Message}");
            }
            finally
            {
                // 7. 释放本次编译创建的全部插件实例（含未执行的），防止 HImage 等非托管资源泄漏
                session.Dispose();
            }

            return finalResult;
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
    }
}
