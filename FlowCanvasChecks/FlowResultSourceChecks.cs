using System;
using System.Collections.Generic;
using System.Diagnostics;
using Core.Interfaces;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Plugins.Util;
using VisionMaster.Services;

namespace FlowCanvasChecks
{
    /// <summary>
    /// [FRS] 结果源配置：Auto / Variable / StepPort / None 的解析优先级。
    /// </summary>
    internal static class FlowResultSourceChecks
    {
        public static void Run()
        {
            Program.Section("[FRS] 结果源配置：Auto / Variable / StepPort / None");

            var workspace = new WorkspaceContext();
            var solution = new SolutionModel();
            solution.Flows.Clear();

            var flow = new FlowModel { FlowName = "FRS_工位", IsEnabled = true };
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);
            workspace.GlobalVariables.Add(
                VariableFactory.CreateLocal("FRS_工位_Result", typeof(bool), "测试结果", false));

            var resolver = new FlowVerdictResolver(workspace, workspace.VariableRegistry);
            var session = NewSession(flow);

            // ---- Variable 模式 ----
            flow.ResultSource = FlowResultSourceKind.Variable;
            flow.ResultVariableName = "FRS_工位_Result";
            var variable = resolver.Resolve(session);
            Program.Check("FRS-1 Variable 模式 → Ng / 来源 Variable",
                variable.Outcome == FlowRoundOutcome.Ng && variable.Source == FlowVerdictSource.Variable,
                $"{variable.Outcome} / {variable.Source} / {variable.Reason}");

            // ---- None 模式：即使变量存在也不读 ----
            flow.ResultSource = FlowResultSourceKind.None;
            var none = resolver.Resolve(session);
            Program.Check("FRS-2 None 模式 → 未判定",
                none.Outcome == FlowRoundOutcome.Unknown,
                $"{none.Outcome} / {none.Reason}");

            // ---- Auto：没有结果步骤 → 约定变量兜底 ----
            flow.ResultSource = FlowResultSourceKind.Auto;
            flow.ResultVariableName = string.Empty;
            var auto = resolver.Resolve(session);
            Program.Check("FRS-3 Auto 无结果步骤 → 约定变量兜底 Ng",
                auto.Outcome == FlowRoundOutcome.Ng && auto.Source == FlowVerdictSource.Variable,
                $"{auto.Outcome} / {auto.Source} / {auto.Reason}");

            // ---- Auto：结果输出步骤优先于变量 ----
            var resultStep = new ActionStep("I", "结果输出", typeof(ResultOutputPlugin).AssemblyQualifiedName!, "结果输出");
            resultStep.State = StepState.Success;
            resultStep.LastRunStartTimestamp = Stopwatch.GetTimestamp();
            session.Blueprints.Add(resultStep);
            var resultPlugin = new ResultOutputPlugin();
            resultPlugin.IsOkPort.TypedValue = true;
            resultPlugin.ReasonPort.TypedValue = "ok";
            session.ExecutionEngine.PluginLookup[resultStep.StepID] = resultPlugin;

            flow.ResultVariableName = "FRS_工位_Result"; // 变量是 false；结果步骤是 true，应优先
            var autoStep = resolver.Resolve(session);
            Program.Check("FRS-4 Auto 结果步骤优先于变量 → Ok / ResultStep",
                autoStep.Outcome == FlowRoundOutcome.Ok && autoStep.Source == FlowVerdictSource.ResultStep,
                $"{autoStep.Outcome} / {autoStep.Source}");

            // ---- StepPort 模式 ----
            var portStep = new ActionStep("P", "判定表", "stub.port", "判定表");
            portStep.State = StepState.Success;
            session.Blueprints.Add(portStep);
            var portPlugin = new StepPortStub();
            portPlugin.IsOk.Value = false;
            session.ExecutionEngine.PluginLookup[portStep.StepID] = portPlugin;

            flow.ResultSource = FlowResultSourceKind.StepPort;
            flow.ResultStepId = portStep.StepID;
            flow.ResultPortName = "IsOk";
            var port = resolver.Resolve(session);
            Program.Check("FRS-5 StepPort 模式 → Ng / 来源 StepPort",
                port.Outcome == FlowRoundOutcome.Ng && port.Source == FlowVerdictSource.StepPort,
                $"{port.Outcome} / {port.Source} / {port.Reason}");

            // ---- StepPort：步骤没执行 → 未判定（不沿用上一轮） ----
            portStep.State = StepState.Skipped;
            var skipped = resolver.Resolve(session);
            Program.Check("FRS-6 StepPort 步骤未执行 → 未判定",
                skipped.Outcome == FlowRoundOutcome.Unknown,
                $"{skipped.Outcome} / {skipped.Reason}");

            resultPlugin.Dispose();
            portPlugin.Dispose();
        }

        private static FlowSession NewSession(FlowModel flow)
        {
            var session = new FlowSession
            {
                FlowName = flow.FlowName,
                CompiledFlowId = flow.FlowID,
            };
            session.ExecutionEngine = new CompiledFlow(
                new List<CompiledNode>(),
                new Dictionary<Guid, IVisionPlugin>(),
                new Dictionary<Guid, CompiledNode>(),
                new Dictionary<Guid, List<Guid>>());
            return session;
        }

        private sealed class StepPortStub : VisionPluginBase
        {
            public OutputPort<bool> IsOk { get; } = new("IsOk", "判定");

            public override void RunAlgorithm(IExecutionContext context) => Success.Value = true;
            public override void Initialize() { }
            public override void Dispose() { }
        }
    }
}
