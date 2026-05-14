using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Prism.Mvvm;

namespace VisionMaster.Models
{
    /// <summary>
    /// 流程执行优先级枚举
    /// 用于多流程并发执行时的调度优先级控制
    /// </summary>
    public enum FlowPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    /// <summary>
    /// 流程会话类
    /// 代表一个流程实例的运行时状态，支持线程安全的状态管理
    /// </summary>
    public class FlowSession : BindableBase
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
        /// 流程执行优先级
        /// </summary>
        public FlowPriority Priority { get; set; } = FlowPriority.Normal;

        /// <summary>
        /// 当前会话锁定的资源集合
        /// 用于多流程并发时的资源互斥控制
        /// </summary>
        public HashSet<string> LockedResources { get; } = new HashSet<string>();

        /// <summary>
        /// 流程步骤蓝图集合
        /// </summary>
        public ObservableCollection<StepModel> Blueprints { get; } = new ObservableCollection<StepModel>();

        /// <summary>
        /// 暂停锁，用于控制流程暂停/恢复
        /// </summary>
        public ManualResetEventSlim PauseLock { get; } = new ManualResetEventSlim(true);

        /// <summary>
        /// 编译后的执行引擎
        /// </summary>
        public CompiledFlow ExecutionEngine { get; set; }

        /// <summary>
        /// 取消令牌源，用于优雅终止流程
        /// </summary>
        public CancellationTokenSource CancellationTokenSource { get; set; }
    }
}