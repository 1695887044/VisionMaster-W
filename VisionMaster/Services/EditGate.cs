using System;
using Core.Events;

namespace VisionMaster.Services
{
    /// <summary>
    /// 画布 / 流程栏共用的编辑动作类别。豁免表按类别放行，不认具体命令实例——
    /// 两处编辑面（流程栏 ModuleActionCommand、画布节点闭包命令）共用同一份判据，
    /// 避免"流程栏放行、画布拦死"（或反过来）的口径漂移。
    /// </summary>
    public enum ModuleEditAction
    {
        /// <summary>结构编辑：增删步骤 / 改名 / 注释 / 禁用 / 拖入 / 拖拽改序 / 解绑连线 / 分支结构动作</summary>
        StructuralEdit,

        /// <summary>打开模块参数（含画布双击节点）</summary>
        ModuleParameters,

        /// <summary>切换断点（运行期标记，不落盘、不递增 Version）</summary>
        ToggleBreakpoint,
    }

    /// <summary>
    /// 画布编辑门禁（三视角评审第一优先，DWV 运行锁的单一出口）。
    ///
    /// 【为什么是实例 + Activate/Deactivate 而不是静态单例】消费方（ProcessViewModel /
    /// FlowCanvasViewModel）的生命周期跟随 AvalonDock 面板的挂/摘——挂摘必须可逆，
    /// 订阅也得可逆，否则离树的旧 VM 会被 GlobalEventBus 静态表钉住无法 GC
    /// （与两个 VM 自己的 Activate/Deactivate 同一条纪律）。
    /// 实例还让断言宿主可以单独 new 一个门禁来验豁免表，不必托底整个视图模型。
    ///
    /// 【豁免表口径（自 ProcessViewModel.IsRunLockExempt 逐字搬迁，2026-10-11 收口为单一出口）】
    ///  · 切换断点：任何运行态都放行——断点标记是 [RuntimeState] 运行期设施，不落盘、
    ///    不递增 Version、不改流程语义；"停在断点上给后续步骤加减断点"恰是调试的日常动作。
    ///  · 模块参数：仅"已暂停"放行——暂停中查看/调整参数是调试的核心诉求（引擎停在节点
    ///    执行前，此刻看参数不与被执行中的插件状态打架）；运行中（非暂停）仍锁死，
    ///    防"边跑边换轮胎"。豁免只影响门禁放行；参数写回仍走既有通道（Version 递增照旧），
    ///    是否对下一轮生效由引擎的编译版本检查决定。
    ///  · 其余编辑动作：运行中（RunningOnce/RunningContinuous/Paused）一律拦截。
    /// </summary>
    public sealed class EditGate
    {
        /// <summary>运行锁拦截的统一文案（流程栏 / 画布共用一份字面；改文案只改这里）</summary>
        public const string RunLockedMessage = "流程运行中，禁止编辑；如需修改请先点击“停止”";

        /// <summary>
        /// 进程内最近一次广播的运行态（由 <c>ShellViewModel.RunState</c> 的 setter 同步写入）。
        ///
        /// 【为什么需要它】<c>GlobalEventBus</c> 是纯转发，**不缓存最后一条消息**；而本门禁的订阅
        /// 随面板挂/摘反复 Activate/Deactivate（AvalonDock 隐藏面板、切标签、组态设计态收敛都会
        /// 让 View 走 Unloaded）。离树期间发生的那次状态变化就永远收不到，重新入树后
        /// <see cref="_runState"/> 停在旧值——现场表现正是"流程在循环运行，双击算子却还能弹出参数窗"
        /// （2026-10-27 真机：弹窗把主窗口置灰，用户感知为界面卡死）。
        /// 因此 <see cref="Activate"/> 先用这个镜像补种，再订阅后续变化。
        ///
        /// 与 <c>ImagePreviewCaptureHub.Current</c> 同一形态：状态由唯一持有方写入，消费方只读。
        /// </summary>
        public static MainRunState LastPublishedRunState { get; private set; } = MainRunState.NotStarted;

        /// <summary>运行态持有方（<c>ShellViewModel.RunState</c> setter）调用：更新镜像供后入树的门禁补种</summary>
        public static void PublishRunState(MainRunState state) => LastPublishedRunState = state;

        private MainRunState _runState = MainRunState.NotStarted;
        private bool _subscribed;

        /// <summary>运行态变化通知（订阅期间由总线驱动）。持有方据此刷新 IsRunLocked 相关绑定面（如画布锁定横幅）</summary>
        public event Action? RunStateChanged;

        /// <summary>运行锁镜像：非 NotStarted 即视为"整体运行中"（单流程运行 / HTTP 触发不广播 MainRunState，不在本门禁管辖内）</summary>
        public bool IsRunLocked => _runState != MainRunState.NotStarted;

        /// <summary>当前运行态快照（IsEditable 的判据；订阅期间由总线同步）</summary>
        public MainRunState RunState => _runState;

        /// <summary>挂接全局运行态订阅（幂等）。由持有方 VM 的 Activate/Deactivate 成对调用</summary>
        public void Activate()
        {
            if (_subscribed) return;
            _subscribed = true;

            // 先补种：离树期间发生的状态变化收不到（总线不缓存），不补种就会带着旧值入树，
            // 运行中照样放行编辑。补种后仍订阅，后续变化照常覆盖。
            if (_runState != LastPublishedRunState)
            {
                _runState = LastPublishedRunState;
                RunStateChanged?.Invoke();
            }

            // 用命名方法而非 lambda：GlobalEventBus.Unsubscribe 依赖委托的 Target+Method 匹配
            GlobalEventBus.Subscribe<MainRunState>(OnMainRunStateChanged);
        }

        /// <summary>摘除订阅（幂等）。摘干净后本实例不再被静态总线引用，可被 GC</summary>
        public void Deactivate()
        {
            if (!_subscribed) return;
            _subscribed = false;
            GlobalEventBus.Unsubscribe<MainRunState>(OnMainRunStateChanged);
        }

        private void OnMainRunStateChanged(MainRunState state)
        {
            _runState = state;
            RunStateChanged?.Invoke();
        }

        /// <summary>
        /// 该编辑动作当前是否允许。豁免表只有两条特例（断点全放行 / 暂停中放参数），
        /// 其余按 IsRunLocked 一刀切——新增编辑入口时先归入 ModuleEditAction，别再私写判据。
        /// </summary>
        public bool IsEditable(ModuleEditAction action)
        {
            if (action == ModuleEditAction.ToggleBreakpoint)
                return true;

            if (action == ModuleEditAction.ModuleParameters && _runState == MainRunState.Paused)
                return true;

            return !IsRunLocked;
        }
    }
}
