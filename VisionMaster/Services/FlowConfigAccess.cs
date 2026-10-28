using System;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 流程配置类弹窗的"能不能编辑"探针。
    ///
    /// 为什么抽这一层而不是让弹窗直接拿 <see cref="ScadaPermissionGate"/>：
    /// 弹窗 VM 要能在无 WPF 宿主下被构造、被断言（FlowCanvasChecks 就是这么测的），
    /// 而权限判定依赖登录会话与 UI 线程上的 <c>ScadaAccessPolicy</c>。
    /// 抽一个只有一句判定的接口，测试传桩、运行时传真实实现，两边都不必迁就对方。
    /// </summary>
    public interface IFlowConfigAccess
    {
        /// <summary>当前账号能否编辑流程结构；不能时 <paramref name="reason"/> 是可直接上屏的中文原因。</summary>
        bool CanEdit(out string reason);
    }

    /// <summary>默认实现：走「编辑流程结构」权限（与流程栏右键菜单同一 Key）。</summary>
    public sealed class FlowConfigAccess : IFlowConfigAccess
    {
        private readonly ScadaPermissionGate _gate;

        public FlowConfigAccess(ScadaPermissionGate gate)
            => _gate = gate ?? throw new ArgumentNullException(nameof(gate));

        public bool CanEdit(out string reason)
        {
            if (_gate.Can(ScadaPermissionKeys.EditStructure, out var deny))
            {
                reason = string.Empty;
                return true;
            }

            reason = string.IsNullOrWhiteSpace(deny) ? "需要流程编辑权限" : deny!;
            return false;
        }
    }
}
