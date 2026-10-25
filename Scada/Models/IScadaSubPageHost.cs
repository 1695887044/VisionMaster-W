namespace VisionMaster.Scada
{
    /// <summary>
    /// 活跃子页宿主：SubPage 在运行态显示/卸载目标页时向会话登记。
    /// 会话据此把子页里的图元事件也算作“当前可见对象”，
    /// 否则 <c>ScadaRuntime.RaiseElementEvent</c> 只认当前页/弹窗，子页按钮永远不响。
    /// </summary>
    public interface IScadaSubPageHost
    {
        /// <summary>子页开始显示（可重复调用，实现方按引用去重）</summary>
        void Enter(ScadaPage page);

        /// <summary>子页不再显示（切页/卸载/换目标页）</summary>
        void Exit(ScadaPage page);
    }
}
