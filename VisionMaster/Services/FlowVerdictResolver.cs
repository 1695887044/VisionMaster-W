using System;
using System.Linq;
using Core.Interfaces;
using VisionMaster.Binding;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 宿主侧判定解析器：按流程配置（<see cref="FlowModel.ResultSource"/>）把本轮 OK/NG 读出来。
    ///
    /// 优先级：
    /// - 显式 ResultStep / Variable / StepPort：只认该来源，读不到记未判定；
    /// - None：不参与；
    /// - Auto：结果输出步骤 &gt; 配置的结果变量 &gt; 配置的步骤端口 &gt; 约定变量 &lt;流程名&gt;_Result &gt; 未判定。
    ///
    /// 引擎只依赖 <see cref="IFlowVerdictResolver"/>；解析器在宿主层实现，注册进 DI 后由引擎构造注入。
    /// </summary>
    public sealed class FlowVerdictResolver : IFlowVerdictResolver
    {
        private readonly IWorkspaceManager _workspace;
        private readonly IVariableRegistry _registry;

        public FlowVerdictResolver(IWorkspaceManager workspace, IVariableRegistry registry)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public FlowVerdict Resolve(FlowSession session)
        {
            if (session == null)
                return Unknown(string.Empty);

            var flow = FindFlow(session);
            var kind = flow?.ResultSource ?? FlowResultSourceKind.Auto;

            switch (kind)
            {
                case FlowResultSourceKind.None:
                    return Unknown("流程配置为不参与统计");

                case FlowResultSourceKind.ResultStep:
                    return ResolveResultStep(session);

                case FlowResultSourceKind.Variable:
                    return ResolveVariable(flow, explicitMode: true);

                case FlowResultSourceKind.StepPort:
                    return ResolveStepPort(session, flow, explicitMode: true);

                default:
                    // Auto：逐个回退；每个 Resolve 返回 Unknown 才继续。
                    var step = ResolveResultStep(session);
                    if (step.Outcome != FlowRoundOutcome.Unknown) return step;

                    var variable = ResolveVariable(flow, explicitMode: false);
                    if (variable.Outcome != FlowRoundOutcome.Unknown) return variable;

                    var port = ResolveStepPort(session, flow, explicitMode: false);
                    if (port.Outcome != FlowRoundOutcome.Unknown) return port;

                    return ResolveConventionVariable(session);
            }
        }

        private FlowModel FindFlow(FlowSession session)
        {
            var flows = _workspace.CurrentSolution?.Flows;
            if (flows == null) return null;

            if (!string.IsNullOrEmpty(session.CompiledFlowId)
                && Guid.TryParse(session.CompiledFlowId, out var flowId)
                && flowId != Guid.Empty)
            {
                var byId = flows.FirstOrDefault(f => f != null
                    && Guid.TryParse(f.FlowID, out var id)
                    && id == flowId);
                if (byId != null) return byId;
            }

            return flows.FirstOrDefault(f => f != null
                && string.Equals(f.FlowName, session.FlowName, StringComparison.Ordinal));
        }

        /// <summary>
        /// ① 结果输出步骤：**一条流程只允许一个**（2026-10-29 规则）。
        /// 本轮执行成功的结果输出步骤多于一个 → 记未判定并说明歧义，不再"取最后执行"：
        /// 最后执行在循环里会变成"最后一个对象说了算"，在顺序两步里会变成"复检覆盖初检"，
        /// 两种都不是用户配得出的语义。
        /// </summary>
        private static FlowVerdict ResolveResultStep(FlowSession session)
        {
            var executed = new System.Collections.Generic.List<StepModel>();
            var lookup = session.ExecutionEngine?.PluginLookup;
            if (lookup != null)
            {
                foreach (var step in session.Blueprints)
                {
                    if (step == null || step.State != StepState.Success) continue;
                    if (!lookup.TryGetValue(step.StepID, out var plugin)) continue;
                    if (plugin is not IFlowResultSource) continue;
                    executed.Add(step);
                }
            }

            if (executed.Count > 1)
            {
                return Unknown(
                    $"流程里有 {executed.Count} 个结果输出步骤（只能保留一个）：" +
                    string.Join("、", executed.Select(s => s.StepName)));
            }

            if (executed.Count == 1
                && lookup != null
                && lookup.TryGetValue(executed[0].StepID, out var bestPlugin)
                && bestPlugin is IFlowResultSource result)
            {
                return new FlowVerdict(
                    result.IsOk ? FlowRoundOutcome.Ok : FlowRoundOutcome.Ng,
                    FlowVerdictSource.ResultStep,
                    result.Reason ?? string.Empty);
            }

            return Unknown("没有结果输出步骤（或本轮未执行）");
        }

        /// <summary>② 配置的结果变量：Id 优先、名字兜底。</summary>
        private FlowVerdict ResolveVariable(FlowModel flow, bool explicitMode)
        {
            if (flow == null)
                return Unknown(string.Empty);

            bool configured = flow.ResultVariableId != Guid.Empty
                || !string.IsNullOrWhiteSpace(flow.ResultVariableName);
            if (!configured)
                return Unknown(explicitMode ? "没有配置结果变量" : string.Empty);

            var variable = _registry.Resolve(flow.ResultVariableId, flow.ResultVariableName);
            bool? normalized = Normalize(variable?.Value);
            if (!normalized.HasValue)
                return Unknown($"结果变量「{flow.ResultVariableName}」未找到或类型不符");

            string name = string.IsNullOrWhiteSpace(flow.ResultVariableName)
                ? flow.ResultVariableId.ToString("N")
                : flow.ResultVariableName;
            return new FlowVerdict(
                normalized.Value ? FlowRoundOutcome.Ok : FlowRoundOutcome.Ng,
                FlowVerdictSource.Variable,
                $"结果变量 {name}");
        }

        /// <summary>③ 配置的步骤端口：步骤本轮必须 Success，端口必须是 bool/string/int。</summary>
        private static FlowVerdict ResolveStepPort(FlowSession session, FlowModel flow, bool explicitMode)
        {
            if (flow == null || flow.ResultStepId == Guid.Empty || string.IsNullOrWhiteSpace(flow.ResultPortName))
                return Unknown(explicitMode ? "没有配置判定步骤/端口" : string.Empty);

            var step = session.Blueprints.FirstOrDefault(s => s != null && s.StepID == flow.ResultStepId);
            if (step == null)
                return Unknown($"判定步骤 {flow.ResultStepId:N} 不在本流程里");
            if (step.State != StepState.Success)
                return Unknown($"判定步骤「{step.StepName}」本轮未执行（{step.State}）");

            var lookup = session.ExecutionEngine?.PluginLookup;
            if (lookup == null || !lookup.TryGetValue(step.StepID, out var plugin))
                return Unknown($"判定步骤「{step.StepName}」没有插件实例");

            if (!plugin.Outputs.TryGetValue(flow.ResultPortName, out var port))
                return Unknown($"步骤「{step.StepName}」没有输出端口「{flow.ResultPortName}」");

            bool? normalized = Normalize(port.Value);
            if (!normalized.HasValue)
                return Unknown($"端口「{flow.ResultPortName}」的值不是可判定的 bool/string/int");

            return new FlowVerdict(
                normalized.Value ? FlowRoundOutcome.Ok : FlowRoundOutcome.Ng,
                FlowVerdictSource.StepPort,
                $"步骤端口 {step.StepName}.{flow.ResultPortName}");
        }

        /// <summary>④ 旧方案兼容：约定变量 &lt;流程名&gt;_Result。</summary>
        private FlowVerdict ResolveConventionVariable(FlowSession session)
        {
            string flowName = session.FlowName ?? string.Empty;
            string variableName = flowName + "_Result";
            var variable = _registry.Resolve(Guid.Empty, variableName);
            bool? normalized = Normalize(variable?.Value);
            if (!normalized.HasValue)
                return Unknown(string.Empty);

            return new FlowVerdict(
                normalized.Value ? FlowRoundOutcome.Ok : FlowRoundOutcome.Ng,
                FlowVerdictSource.Variable,
                $"约定变量 {variableName}");
        }

        private static FlowVerdict Unknown(string reason)
            => new(FlowRoundOutcome.Unknown, FlowVerdictSource.None, reason ?? string.Empty);

        private static bool? Normalize(object? value)
        {
            switch (value)
            {
                case null:
                    return null;
                case bool b:
                    return b;
                case string s:
                    s = s.Trim();
                    if (s.Length == 0) return null;
                    if (s.Equals("OK", StringComparison.OrdinalIgnoreCase)
                        || s.Equals("PASS", StringComparison.OrdinalIgnoreCase)
                        || s.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                        || s == "合格" || s == "良品")
                        return true;
                    if (s.Equals("NG", StringComparison.OrdinalIgnoreCase)
                        || s.Equals("FAIL", StringComparison.OrdinalIgnoreCase)
                        || s.Equals("FALSE", StringComparison.OrdinalIgnoreCase)
                        || s == "不合格" || s == "不良")
                        return false;
                    return null;
                case int i:
                    return i != 0;
                case long l:
                    return l != 0;
                case double d:
                    return Math.Abs(d) > double.Epsilon;
                default:
                    return null;
            }
        }
    }
}
