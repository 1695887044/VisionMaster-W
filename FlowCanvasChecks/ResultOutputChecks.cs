using System;
using System.Collections.Generic;
using Core.Interfaces;
using VisionMaster.Plugins.Util;

namespace FlowCanvasChecks
{
    /// <summary>
    /// [RO] 结果输出步骤：判定声明 / NG 不失败 / 未接线告警。
    ///
    /// 这条断言守的是"NG 与故障分离"的根：判定表 NG 会 Fail()，卡尺/斑点 NG 时 Success 仍为 true，
    /// 宿主不能靠步骤状态反推产品判定。结果输出步骤必须把 IsOk 原样交出去，自己始终 Success。
    /// </summary>
    internal static class ResultOutputChecks
    {
        public static void Run()
        {
            Program.Section("[RO] 结果输出步骤：判定声明 / NG 不失败 / 未接线告警");

            var log = new StubLog();
            var plugin = new ResultOutputPlugin();
            var ctx = new StubContext(log);
            var source = plugin as IFlowResultSource;

            Program.Check("RO-1 插件实现 IFlowResultSource", source != null, plugin.GetType().Name);

            Program.Check("RO-2 端口名与必填：IsOk 必填 / Reason 可选",
                plugin.Inputs.ContainsKey("IsOk")
                && plugin.Inputs.ContainsKey("Reason")
                && plugin.IsOkPort.IsRequired
                && !plugin.ReasonPort.IsRequired,
                string.Join(",", plugin.Inputs.Keys));

            // ---- OK：接口读到 true，步骤 Success ----
            plugin.IsOkPort.TypedValue = true;
            plugin.ReasonPort.TypedValue = "外观 OK";
            plugin.RunAlgorithm(ctx);
            Program.Check("RO-3 IsOk=true → 接口 true / 步骤 Success",
                source!.IsOk && plugin.Success.TypedValue,
                $"IsOk={source.IsOk} Success={plugin.Success.TypedValue}");

            // ---- NG：接口读到 false，但步骤仍 Success（NG 是正常结果） ----
            plugin.IsOkPort.TypedValue = false;
            plugin.ReasonPort.TypedValue = "焊点直径 0.42 < 0.50";
            plugin.RunAlgorithm(ctx);
            Program.Check("RO-4 IsOk=false → 接口 false / 步骤仍 Success（NG 不失败）",
                !source.IsOk && plugin.Success.TypedValue,
                $"IsOk={source.IsOk} Success={plugin.Success.TypedValue}");
            Program.Check("RO-5 Reason 原样带出",
                source.Reason == "焊点直径 0.42 < 0.50",
                source.Reason);

            // ---- Reason 为空：接口返回空串，不返回 null ----
            plugin.ReasonPort.TypedValue = null!;
            plugin.RunAlgorithm(ctx);
            Program.Check("RO-6 Reason 为空 → 接口空串（不返回 null）",
                source.Reason == string.Empty,
                source.Reason == null ? "null" : $"'{source.Reason}'");

            // ---- 未接线（手填常量）：允许跑，但必须留 Warn ----
            log.Warns.Clear();
            plugin.IsOkPort.TypedValue = true;
            plugin.RunAlgorithm(ctx);
            Program.Check("RO-7 未接线手填常量 → Warn 提示固定值",
                log.HasWarn("固定值"),
                string.Join(" | ", log.Warns));

            plugin.Dispose();
        }

        /// <summary>最小执行上下文：只递送 Logger，其余能力为空实现。</summary>
        private sealed class StubContext : IExecutionContext
        {
            private readonly ILogService _log;
            public StubContext(ILogService log) => _log = log;

            public ILogService Logger => _log;
            public System.Threading.CancellationToken CancellationToken => default;
            public FlowControlState CurrentFlowState { get; set; }
            public Guid? CurrentNodeId { get; set; }
            public string CurrentFlowName => "ResultOutputChecks";
            public DateTime ExecutionStartTime => DateTime.Now;
            public IDictionary<string, object> LocalVariables { get; } = new Dictionary<string, object>();
            public IGlobalVariableWriter GlobalVariables { get; } = new NullGlobalWriter();
            public ISharedVariableAccess SharedVariables { get; } = NullSharedVariableAccess.Instance;
            public ICameraProvider Cameras => NullCameraProvider.Instance;
            public IMotionProvider Motions => NullMotionProvider.Instance;
            public IFlowInvoker FlowInvoker => NullFlowInvoker.Instance;
        }

        private sealed class NullGlobalWriter : IGlobalVariableWriter
        {
            public bool TryWrite(string name, object? value, out string? error)
            {
                error = "断言桩：无全局变量管理";
                return false;
            }
        }
    }
}
