using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;

namespace VisionMaster.Services
{
    /// <summary>
    /// 试运行专用轻量上下文（无流程引擎参与）
    /// 由 PluginTestRunner 在配置弹窗中构建，供插件的 Execute/RunAlgorithm 使用
    /// </summary>
    internal class TestExecutionContext : IExecutionContext
    {
        public ILogService Logger { get; }

        public IPortBindingService PortBindingService =>
            throw new NotSupportedException("试运行不支持呼出端口绑定服务");

        public CancellationToken CancellationToken => CancellationToken.None;

        public FlowControlState CurrentFlowState { get; set; }

        public Guid? CurrentNodeId { get; set; }

        public DateTime ExecutionStartTime { get; } = DateTime.Now;

        public IDictionary<string, object> LocalVariables { get; } = new Dictionary<string, object>();

        public TestExecutionContext(ILogService logger)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
    }
}
