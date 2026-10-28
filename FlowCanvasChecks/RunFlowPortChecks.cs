using System;
using System.Collections.Generic;
using System.Threading;
using Core.Interfaces;
using VisionMaster.Plugins.RunFlow;

namespace FlowCanvasChecks
{
    /// <summary>
    /// [RF] 「调用流程」插件的 IsOk / Outcome 端口：产品 NG 不再被当成调用故障。
    /// </summary>
    internal static class RunFlowPortChecks
    {
        public static void Run()
        {
            Program.Section("[RF] 调用流程：IsOk/Outcome 端口与 NG 语义");

            var log = new StubLog();

            var ngInvoker = new StubInvoker
            {
                Result = FlowInvokeResult.Completed("跑完", 12, FlowRoundOutcome.Ng),
            };
            var ngPlugin = new RunFlowPlugin();
            ngPlugin.TargetFlowPort.TypedValue = "子流程";
            ngPlugin.TimeoutPort.TypedValue = 1000;
            ngPlugin.RunAlgorithm(new StubContext(log, ngInvoker));

            Program.Check("RF-1 子流程 Ng → 步骤 Success / Invoked=true",
                ngPlugin.Success.TypedValue && ngPlugin.Invoked.TypedValue,
                $"Success={ngPlugin.Success.TypedValue} Invoked={ngPlugin.Invoked.TypedValue}");
            Program.Check("RF-2 Ng → IsOk=false / Outcome=Ng",
                !ngPlugin.IsOk.TypedValue && ngPlugin.Outcome.TypedValue == "Ng",
                $"IsOk={ngPlugin.IsOk.TypedValue} Outcome={ngPlugin.Outcome.TypedValue}");
            Program.Check("RF-3 耗时/消息透传",
                ngPlugin.ElapsedMs.TypedValue == 12 && ngPlugin.InvokeMessage.TypedValue == "跑完",
                $"{ngPlugin.ElapsedMs.TypedValue}ms / {ngPlugin.InvokeMessage.TypedValue}");

            var failInvoker = new StubInvoker
            {
                Result = FlowInvokeResult.Fail("没跑", 3),
            };
            var failPlugin = new RunFlowPlugin();
            failPlugin.TargetFlowPort.TypedValue = "子流程";
            failPlugin.TimeoutPort.TypedValue = 1000;
            failPlugin.RunAlgorithm(new StubContext(log, failInvoker));

            Program.Check("RF-4 门禁失败 → Success=false / Invoked=false / IsOk=false / Outcome 空",
                !failPlugin.Success.TypedValue
                && !failPlugin.Invoked.TypedValue
                && !failPlugin.IsOk.TypedValue
                && failPlugin.Outcome.TypedValue == string.Empty,
                $"Success={failPlugin.Success.TypedValue} Invoked={failPlugin.Invoked.TypedValue} Outcome='{failPlugin.Outcome.TypedValue}'");

            ngPlugin.Dispose();
            failPlugin.Dispose();
        }

        private sealed class StubInvoker : IFlowInvoker
        {
            public FlowInvokeResult Result { get; set; } = FlowInvokeResult.Fail("stub");

            public FlowInvokeResult Invoke(string flowName, int timeoutMs, CancellationToken cancellationToken)
                => Result;
        }

        private sealed class StubContext : IExecutionContext
        {
            private readonly ILogService _log;
            private readonly IFlowInvoker _invoker;

            public StubContext(ILogService log, IFlowInvoker invoker)
            {
                _log = log;
                _invoker = invoker;
            }

            public ILogService Logger => _log;
            public CancellationToken CancellationToken => default;
            public FlowControlState CurrentFlowState { get; set; }
            public Guid? CurrentNodeId { get; set; }
            public string CurrentFlowName => "RunFlowPortChecks";
            public DateTime ExecutionStartTime => DateTime.Now;
            public IDictionary<string, object> LocalVariables { get; } = new Dictionary<string, object>();
            public IGlobalVariableWriter GlobalVariables { get; } = new NullGlobalWriter();
            public ISharedVariableAccess SharedVariables { get; } = NullSharedVariableAccess.Instance;
            public ICameraProvider Cameras => NullCameraProvider.Instance;
            public IMotionProvider Motions => NullMotionProvider.Instance;
            public IFlowInvoker FlowInvoker => _invoker;
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
