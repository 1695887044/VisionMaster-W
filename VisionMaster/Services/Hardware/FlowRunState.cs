using System.Linq;
using VisionMaster.Models;

namespace VisionMaster.Services.Hardware
{
    /// <summary>任意流程是否在运行/暂停（含单流程运行、HTTP 触发）。</summary>
    public interface IFlowRunState
    {
        bool AnyFlowRunning { get; }
    }

    /// <summary>
    /// 从当前方案现算运行态。刻意不缓存：调用点在 1 秒刷新与命令执行前，
    /// 缓存会让"刚点停止、门禁还锁着"或反过来"流程已启动、门禁还开着"。
    /// </summary>
    public sealed class WorkspaceFlowRunState : IFlowRunState
    {
        private readonly IWorkspaceManager _workspace;

        public WorkspaceFlowRunState(IWorkspaceManager workspace)
        {
            _workspace = workspace;
        }

        public bool AnyFlowRunning
        {
            get
            {
                var flows = _workspace.CurrentSolution?.Flows;
                if (flows == null) return false;

                foreach (var flow in flows)
                {
                    if (flow != null && flow.RunState != FlowRunState.Stopped)
                        return true;
                }

                return false;
            }
        }
    }
}
