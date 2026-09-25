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
        /// 【为什么需要这个标记】PauseSession 能生效的前提，是执行体在循环里等 PauseLock ——
        /// 只有连续执行有这个循环。单次执行是一把跑完整图（ExecutionEngine.Run 一次到底），
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