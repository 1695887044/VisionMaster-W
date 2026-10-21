using System;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IScadaFlowTriggerQuery"/> 的默认实现：按
    /// <c>FlowVariableTriggerService</c> 的同一套判据（启用 + InvokeType.Variable +
    /// TriggerVariable 与变量当前名相等）判断。
    ///
    /// <b>为什么必须复用同一套判据</b>
    /// ---------
    /// 如果这里和 <c>FlowVariableTriggerService</c> 各写一套（例如一边 Trim、一边不 Trim），
    /// 会出现"Web 认为不是触发器、流程引擎认为是"的错位——Web 放行了写入，流程照样被启动。
    /// 判据逐字照抄 `Engine\FlowVariableTriggerService.cs:156-160`。
    ///
    /// <b>线程</b>：命令泵在 UI 线程上调用；<c>CurrentSolution.Flows</c> 是 UI 单线程语义。
    /// </summary>
    public sealed class ScadaFlowTriggerQuery : IScadaFlowTriggerQuery
    {
        private readonly IWorkspaceManager _workspace;

        public ScadaFlowTriggerQuery(IWorkspaceManager workspace)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        }

        /// <inheritdoc />
        public bool IsFlowTrigger(Guid variableId, string? variableName, out string? flowName)
        {
            flowName = null;

            // Id 优先、名字兜底：变量改名后绑定里那份名字可能已经过期，Id 才是权威。
            var variable = _workspace.VariableRegistry.Resolve(variableId, variableName);
            var name = (variable?.Name ?? variableName)?.Trim();
            if (string.IsNullOrEmpty(name))
                return false;

            var flows = _workspace.CurrentSolution?.Flows;
            if (flows == null)
                return false;

            foreach (var flow in flows)
            {
                if (flow == null || !flow.IsEnabled)
                    continue;

                if ((flow.InvokeType & FlowInvokeType.Variable) == 0)
                    continue;

                if (!string.Equals(flow.TriggerVariable?.Trim(), name, StringComparison.Ordinal))
                    continue;

                flowName = string.IsNullOrWhiteSpace(flow.FlowName) ? "<未命名流程>" : flow.FlowName;
                return true;
            }

            return false;
        }
    }
}
