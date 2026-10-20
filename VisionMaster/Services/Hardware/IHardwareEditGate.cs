namespace VisionMaster.Services.Hardware
{
    /// <summary>
    /// 硬件编辑门禁（唯一判定处）。
    ///
    /// 矩阵（2026-10-20 批次 C 拍板）：
    /// <list type="bullet">
    /// <item>查看：所有角色（含未登录操作员）。</item>
    /// <item>运行操作（连接/断开/测试/点动/使能/回零）：工程师及以上。</item>
    /// <item>配置修改（增删设备/改地址/参数/轴映射）：管理员，且任意流程未运行。</item>
    /// </list>
    ///
    /// 运行锁为什么不用 <c>EditGate.IsRunLocked</c>：它只镜像 MainRunState，
    /// 单流程运行 / HTTP 触发不写它（见 ShellViewModel.AnyFlowRunning 的注释）。
    /// 这里走 <see cref="IFlowRunState"/>，覆盖全部运行入口。
    /// 急停/停止不在本门禁的拦截面内——它们是安全动作，调用方不得用本门禁去灰掉。
    /// </summary>
    public interface IHardwareEditGate
    {
        bool CanEditConfig { get; }

        bool CanOperateRuntime { get; }

        /// <summary>配置不可编辑的原因（可编辑时为 null），可直接上屏。</summary>
        string? ConfigReason { get; }

        /// <summary>运行操作不可执行的原因（可执行时为 null），可直接上屏。</summary>
        string? RuntimeReason { get; }
    }
}
