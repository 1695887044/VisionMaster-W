using Prism.Dialogs;

namespace VisionMaster.Services.Hardware
{
    /// <summary>
    /// 嵌入页生命周期契约（硬件管理外壳专用）。
    ///
    /// 与 <see cref="Prism.Dialogs.IDialogAware"/> 的区别：对话框的 Opened/Closed 各一次、
    /// 且关闭后 VM 作废；嵌入页会在同一宿主里被反复切走/切回，所以把"一次性初始化"
    /// 与"可见性切换"拆成两组方法：
    /// <list type="bullet">
    /// <item><b>Opened</b>：只调一次（懒创建时）。对应原 OnDialogOpened。</item>
    /// <item><b>Activated / Deactivated</b>：可多次。只停/启本页的 UI 定时器与点动看门狗，
    ///      不退订全局设备事件、不 CancelAll。</item>
    /// <item><b>Closed</b>：恰好一次（宿主关闭时）。对应原 OnDialogClosed。</item>
    /// </list>
    ///
    /// 放在宿主工程而不是 Core.Interfaces：三个域 VM 都是宿主自有，
    /// 放进公共契约只会扩大插件引用面（与 IHealthPanel 同一条口径）。
    /// </summary>
    public interface IEmbeddedDialogPage
    {
        /// <summary>懒创建时调用一次；实现方转调原 OnDialogOpened。</summary>
        void OnEmbeddedOpened(IDialogParameters parameters);

        /// <summary>页面重新可见；可多次调用，必须幂等。</summary>
        void OnEmbeddedActivated();

        /// <summary>页面被切走；可多次调用。只停本页 UI 定时器/点动，不许取消流程命令。</summary>
        void OnEmbeddedDeactivated();

        /// <summary>宿主关闭；恰好一次，异常路径也要走到。实现方转调原 OnDialogClosed。</summary>
        void OnEmbeddedClosed();

        /// <summary>关闭前确认（例如点动中）；返回 false 时 reason 必须可直接显示。</summary>
        bool CanCloseEmbedded(out string? reason);
    }

    /// <summary>
    /// 由宿主把权限门禁结果推给嵌入页。页面自己不做角色判定——
    /// 判定只在 <see cref="IHardwareEditGate"/> 一处，避免"相机页允许、通讯页拒绝"的口径漂移。
    /// </summary>
    public interface IHardwareEditGateAware
    {
        void ApplyEditGate(bool canEditConfig, bool canOperateRuntime, string? configReason, string? runtimeReason);
    }
}
