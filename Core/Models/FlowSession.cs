using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Core.Interfaces;
using Prism.Mvvm;

namespace VisionMaster.Models
{
    /// <summary>
    /// 流程会话类
    /// 代表一个流程实例的运行时状态，支持线程安全的状态管理
    /// 会话是编译实例的属主：Dispose 负责释放编译引擎创建的全部插件实例
    /// </summary>
    public class FlowSession : BindableBase, IDisposable
    {
        /// <summary>
        /// 状态锁对象，保护线程安全
        /// </summary>
        private readonly object _stateLock = new object();

        /// <summary>
        /// 是否正在运行
        /// </summary>
        private bool _isRunning;

        /// <summary>
        /// 当前状态
        /// </summary>
        private SessionState _state;

        /// <summary>
        /// 编译版本号
        /// </summary>
        private int _compiledVersion;

        /// <summary>
        /// 回合收尾的**容器状态上浮**：递归整棵蓝图树，只要有子孙步骤 State==Failed，
        /// 就把对应容器也标 Failed（只改状态报告，不动控制流）。
        ///
        /// 为什么放在回合收尾、而不是容器节点内部：If 容器在"选出分支"那一刻就返回了，
        /// 分支体由上层序列执行器（RunSequence）接着跑——容器自己根本没有"子步骤跑完"的时刻。
        /// 收尾统一上浮既覆盖 If/For/While 三种容器，又完全不碰执行顺序（零控制流风险）。
        ///
        /// 为什么必须上浮：子步骤业务失败而容器报绿，上位机/HTTP 若按容器状态判 OK 就会放行不良品——
        /// "绿着错了"比"红着停下"危险得多（本仓库既定判词）。
        /// State 是 [RuntimeState]，标 Failed 不递增 Version、不落盘。
        /// </summary>
        public void EscalateContainerFailures()
        {
            foreach (var step in Blueprints)
                EscalateOne(step);
        }

        /// <summary>自底向上：先处理孙辈，再决定本级容器是否标 Failed；返回本级（含子树）是否有失败</summary>
        private static bool EscalateOne(StepModel step)
        {
            if (step is not IContainerStep container || container.Children == null)
                return step?.State == StepState.Failed;

            bool anyFailed = false;
            foreach (var branch in container.Children)
            {
                if (branch?.Steps == null) continue;
                foreach (var child in branch.Steps)
                {
                    if (child == null) continue;
                    if (EscalateOne(child)) anyFailed = true;
                }
            }

            if (anyFailed && step.State != StepState.Failed)
                step.State = StepState.Failed;

            return step.State == StepState.Failed;
        }

        /// <summary>
        /// 这份编译产物属于哪个流程身份（<see cref="FlowModel.FlowID"/>）。
        ///
        /// 为什么除了 Version 还要它：Version 是**流程自己**的版本号，两份不同方案里的
        /// 同名流程各自完全可能都是 Version=3——只比 Version 会让"切换方案后，
        /// 同名流程复用上一份方案的编译产物"（静默跑别的方案的图纸）。
        /// 空串 = 老调用方未填：判据退化为只比 Version（兼容既有行为）。
        /// </summary>
        public string CompiledFlowId { get; set; } = string.Empty;

        /// <summary>
        /// 会话唯一标识
        /// </summary>
        public string SessionID { get; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 流程名称
        /// </summary>
        private string _flowName;
        public string FlowName
        {
            get => _flowName;
            set => SetProperty(ref _flowName, value);
        }

        /// <summary>
        /// 是否正在运行（线程安全）
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (_stateLock)
                {
                    return _isRunning;
                }
            }
            set
            {
                lock (_stateLock)
                {
                    SetProperty(ref _isRunning, value);
                }
            }
        }

        /// <summary>
        /// 当前会话状态（线程安全）
        /// </summary>
        public SessionState State
        {
            get
            {
                lock (_stateLock)
                {
                    return _state;
                }
            }
            set
            {
                lock (_stateLock)
                {
                    SetProperty(ref _state, value);
                }
            }
        }

        /// <summary>
        /// 编译版本号（线程安全）
        /// </summary>
        public int CompiledVersion
        {
            get
            {
                lock (_stateLock)
                {
                    return _compiledVersion;
                }
            }
            set
            {
                lock (_stateLock)
                {
                    _compiledVersion = value;
                }
            }
        }

        /// <summary>
        /// 当前这一轮执行是否为"连续执行"（RunSessionAsync），单次执行（RunSessionOnceAsync）为 false。
        ///
        /// 【为什么需要这个标记】PauseSession 能生效的前提，是执行体有等待 PauseLock 的位置 ——
        /// 连续执行在轮顶等；调试会话（DebugEnabled）的运行另经调试门逐节点等（DWV 第 1 期）。
        /// 非调试单次执行是一把跑完整图（ExecutionEngine.Run 一次到底），
        /// 中间没有任何可插入等待的位置；对它置 Paused 只会造出
        /// "UI 显示已暂停、流程却照跑到底"的假象（暂停期间结束还会直接落 Stopped）。
        ///
        /// 不能用 State == Running 代替：单次执行期间 State 同样是 Running，猜不出来。
        /// 所以把"这一轮到底能不能暂停"这个事实摆到台面上，由 PauseSession 显式判断。
        ///
        /// 运行期标记（会话对象本身不落盘）。
        /// 赋值顺序上的保证：本属性一律在 IsRunning = true 之前写入，
        /// 而 PauseSession 的守卫先读 IsRunning —— 能通过守卫的调用，一定看得到正确的值。
        /// </summary>
        public bool IsContinuousRun { get; set; }

        // ================= DWV 第 1 期：调试门（断点 / 单步 / 暂停继续） =================
        // 设计口径（评审结论 4–6 + 用户决策）：
        //  · 装门条件 = DebugEnabled（不是 IsContinuousRun）：单次执行也支持调试；
        //  · 插桩点只有一个——CompiledNode.RunSequence 循环体内、RunAndGetNext 之前（本类只提供门）；
        //  · 停的位置在"节点执行前"；等待一律 PauseLock.Wait(token)（停止能立即打断）；
        //  · 断点不落盘（IsBreakpoint / IsDebugStopped 见 StepModel）。

        /// <summary>
        /// 本会话是否按"调试会话"运行（DWV 第 1 期：断点 / 单步 / 暂停继续的装门条件）。
        ///
        /// 【谁置位 / 谁清位】默认 false；只有从界面发起的运行由 Shell 在两处运行入口置 true。
        /// 引擎在每轮运行收尾统一清回 false（见 FlowEngineService）——保证它精确表达
        /// "这一轮是不是界面发起的调试运行"，不泄漏给同一会话的下一次非界面触发
        /// （HTTP 收图会复用界面已编译的会话，残留 true 就会被断点卡住产线链路，硬约束）。
        /// 试运行（PluginTestRunner）自建新会话、从不置位，天然豁免。
        ///
        /// 【为什么不用 IsContinuousRun 分流（推翻评审稿 §3）】单次执行同样要支持断点/单步/暂停继续；
        /// 装门条件改为本标志后，两个运行入口共用同一套调试门，逻辑只有一处。
        /// </summary>
        public bool DebugEnabled { get; set; }

        /// <summary>
        /// 单步请求位（DWV 第 1 期）：StepSession 置位并放行，调试门在"下一个经过的节点边界"
        /// 消费它——从停点起恰好放行一个节点后再次停住。
        /// 跨线程（UI 线程置位 / 执行线程消费）与 IsContinuousRun 同口径：置位后紧跟 PauseLock.Set()，
        /// 等待侧 ManualResetEventSlim 的等待 / 唤醒提供必要的栅栏。
        /// </summary>
        public bool DebugStepPending { get; set; }

        /// <summary>
        /// 最近一次暂停的原因（UI / 日志据此区分"为什么停住"；恢复 / 单步放行时清回 None）。
        /// PauseSession 置 User；调试门置 Breakpoint / Step（见 <see cref="DebugGateBeforeNode"/>）。
        /// 纯运行时显示状态，会话对象本身不落盘。
        /// </summary>
        public SessionPauseReason PauseReason { get; set; }

        /// <summary>
        /// 调试门命中（断点 / 单步）通知：即将停在节点执行前时触发（此时锁已合上）。
        /// 订阅方（FlowEngineService）负责把会话归到 Paused —— State 的唯一赋值点在引擎侧
        /// （NotifyStateChanged），Core 层只发通知、不直接抢状态赋值。
        /// 用户暂停不触发本事件（PauseSession 已自行通知 Paused）。
        /// </summary>
        public event Action<FlowSession> DebugStopped;

        /// <summary>
        /// 节点执行前的调试门（DWV 第 1 期）：RunSequence 在每个节点边界调用一次。
        ///
        /// 三条硬规矩（评审结论 5–6）：
        ///  ① 停的位置在"节点执行前"——命中即等，等不到放行就绝不执行该节点；
        ///  ② 等待一律 <see cref="PauseLock"/>.Wait(token)：停止 / 会话替换取消令牌后立即打断
        ///     （无令牌等待打不断——参考工程 AutoResetEvent 的坑不能踩第二次）；
        ///  ③ 收尾 PauseLock.Set() 兜底：无论正常放行还是被取消，离开门时锁都回到放行态。
        ///
        /// 触发停住的三种情况：
        ///  · 节点是断点（IsBreakpoint；标记常驻，循环每圈回来都断）；
        ///  · 单步放行（DebugStepPending，一次性，命中即消费）；
        ///  · 用户暂停（PauseSession 已合锁）：只按住等待，不再补发通知（避免二次 Paused 事件）。
        ///
        /// 豁免：非调试会话（DebugEnabled=false）一步不停——HTTP 触发 / 试运行 / 普通运行全走
        /// 这条早退路径，热路径只多一次 bool 读。
        /// </summary>
        /// <param name="node">即将执行的编译节点（取其 Blueprint 查断点）</param>
        /// <param name="token">执行取消令牌（停止 / 会话替换）</param>
        public void DebugGateBeforeNode(CompiledNode node, CancellationToken token)
        {
            if (!DebugEnabled) return; // 豁免路径（HTTP / 试运行 / 非调试运行）

            var step = node?.Blueprint;
            bool hitBreakpoint = step?.IsBreakpoint == true;
            bool stepRequested = DebugStepPending;
            bool pauseRequested = !PauseLock.IsSet; // PauseSession 已把锁合上

            if (!hitBreakpoint && !stepRequested && !pauseRequested)
                return; // 调试会话未停：直通（只读几个标志，不等锁）

            if (token.IsCancellationRequested)
                return; // 停止请求已到：不再摆停点，交外层令牌检查退栈

            if (hitBreakpoint || stepRequested)
            {
                // 先落停点信息、再合锁、最后通知：
                // 合锁必须先于通知——否则"通知送达后、等待开始前"的快速"继续"会被随后到来的
                // Reset 吞掉，变成一场永远等不到放行的等待（时序钉子，别调换）。
                DebugStepPending = false; // 单步一次性；断点标记常驻（循环每圈回来都断）
                PauseReason = stepRequested ? SessionPauseReason.Step : SessionPauseReason.Breakpoint;

                try { PauseLock.Reset(); }
                catch (ObjectDisposedException) { return; } // 会话收尾窗口：同取消处理

                if (step != null) step.IsDebugStopped = true;

                try { DebugStopped?.Invoke(this); }
                catch
                {
                    // 隔离订阅者异常：通知是附加语义，不该把整轮流程打成 Faulted。
                    // （引擎侧处理器内部已隔离，这是给未来 UI 订阅者的防线；本类无日志依赖，只静默兜底）
                }
            }
            // else：纯用户暂停——锁已合、Paused 已由 PauseSession 通知，这里只负责按住执行

            try
            {
                PauseLock.Wait(token);
            }
            catch (ObjectDisposedException)
            {
                // 收尾窗口里锁被释放（[E9]② 的既有边界）：按取消退栈
            }
            finally
            {
                // 收尾兜底：离开门时锁必须回到放行态，不留"等人来放行"的合锁给后续任何等待者；
                // 会话可能已被 RemoveAndDispose 释放，Set 会抛 ObjectDisposedException
                try { PauseLock.Set(); } catch (ObjectDisposedException) { }
                if (step != null) step.IsDebugStopped = false;
            }
        }

        /// <summary>
        /// 递归把图纸填进 <see cref="Blueprints"/>（含 If/While/For 容器内的嵌套步骤）。
        ///
        /// 【为什么必须递归】Blueprints 是调度层复位运行状态、以及各处反查步骤名的唯一依据。
        /// 只填顶层，嵌套步骤就永远不上报运行状态 —— 试运行时画布上循环体/分支里的节点不变色，
        /// 现场看不出跑到哪了。
        ///
        /// 【为什么收敛到会话身上，只留这一份实现】原先有三份：ShellViewModel 与 HttpImageServer
        /// 各写了一份递归版、PluginTestRunner 写了一份浅版（只填顶层）。三份口径并存必然漂移，
        /// 而漂移的代价是"某一条执行路径下嵌套步骤静默不上报"。放在这里，
        /// 既覆盖所有调用方，也让"必须递归"这个不变量跟着数据一起走。
        /// </summary>
        public void AddBlueprintsDeep(IEnumerable<StepModel> steps)
        {
            if (steps == null) return;

            foreach (var step in steps)
            {
                if (step == null) continue;

                Blueprints.Add(step);

                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                        AddBlueprintsDeep(branch?.Steps);
                }
            }
        }

        /// <summary>
        /// 流程步骤蓝图集合
        /// </summary>
        public ObservableCollection<StepModel> Blueprints { get; } = new ObservableCollection<StepModel>();

        /// <summary>
        /// 暂停锁，用于控制流程暂停/恢复
        /// </summary>
        public ManualResetEventSlim PauseLock { get; } = new ManualResetEventSlim(true);

        /// <summary>
        /// 当前持有运行焦点的步骤（执行指针）
        /// 新步骤获得焦点时释放上一个，保证流程设计器上恒定单行高亮、平滑移动；
        /// 毫秒级步骤若"完成即清焦点"，高亮会在渲染帧间隙闪变导致肉眼不可见
        /// </summary>
        public StepModel FocusedStep { get; set; }

        /// <summary>
        /// 编译后的执行引擎
        /// </summary>
        public CompiledFlow ExecutionEngine { get; set; }

        /// <summary>
        /// 取消令牌源，用于优雅终止流程
        /// </summary>
        public CancellationTokenSource CancellationTokenSource { get; set; }

        private bool _disposed;

        /// <summary>
        /// 释放会话持有的资源：
        /// 1. 编译引擎创建的全部插件实例（每次编译都会 Activator.CreateInstance 一整套新实例，
        ///    其中持有 HImage 等非托管资源，必须显式释放，GC 无法回收）
        /// 2. 取消令牌源与暂停锁等同步对象
        /// 可安全重复调用
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (ExecutionEngine?.PluginLookup != null)
            {
                foreach (var plugin in ExecutionEngine.PluginLookup.Values)
                {
                    try
                    {
                        plugin?.Dispose();
                    }
                    catch
                    {
                        // 单个插件释放失败不阻断其余实例的释放
                    }
                }
            }

            CancellationTokenSource?.Dispose();
            PauseLock.Dispose();
        }
    }
}