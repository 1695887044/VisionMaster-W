using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.Services.Hardware
{
    /// <summary>基于 ScadaAccessPolicy + IFlowRunState 的门禁实现；口径见接口注释。</summary>
    public sealed class HardwareEditGate : IHardwareEditGate
    {
        private readonly ScadaAccessPolicy _access;
        private readonly IFlowRunState _runState;

        public HardwareEditGate(ScadaAccessPolicy access, IFlowRunState runState)
        {
            _access = access;
            _runState = runState;
        }

        public bool CanEditConfig
            => _access.CurrentRole.Satisfies(ScadaRole.Administrator) && !_runState.AnyFlowRunning;

        public bool CanOperateRuntime
            => _access.CurrentRole.Satisfies(ScadaRole.Engineer);

        public string? ConfigReason
        {
            get
            {
                if (!_access.CurrentRole.Satisfies(ScadaRole.Administrator))
                    return "需要管理员权限";
                if (_runState.AnyFlowRunning)
                    return "流程运行中，禁止修改硬件配置；如需修改请先停止";
                return null;
            }
        }

        public string? RuntimeReason
            => _access.CurrentRole.Satisfies(ScadaRole.Engineer) ? null : "需要工程师权限";
    }
}
