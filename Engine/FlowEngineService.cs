using Core.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using VisionMaster.EventModel;
using VisionMaster.Models;
using VisionMaster.Core;

namespace VisionMaster.Services
{
    /// <summary>
    /// 流程执行引擎服务实现
    /// 负责管理多个流程会话的并发执行、状态管理和生命周期控制
    /// </summary>
    public class FlowEngineService : IFlowEngine
    {
        /// <summary>
        /// 运行时管理器，管理所有活动会话
        /// </summary>
        private readonly IRuntimeManager _runtimeManager;

        /// <summary>
        /// 日志服务
        /// </summary>
        private readonly ILogService _logService;

        /// <summary>
        /// 工作空间管理器，提供全局变量访问
        /// </summary>
        private readonly IWorkspaceManager _workspaceManager;

        /// <summary>
        /// 性能监控服务
        /// </summary>
        private readonly IPerformanceMonitor _performanceMonitor;

        /// <summary>
        /// 资源锁服务：会话启动靠它做原子互斥（A3）
        /// </summary>
        private readonly IResourceLockService _resourceLocks;

        /// <summary>
        /// 会话状态变更事件
        /// 当会话状态发生变化时触发
        /// </summary>
        public event EventHandler<SessionStateChangedEventArgs> SessionStateChanged;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="runtimeManager">运行时管理器</param>
        /// <param name="logService">日志服务</param>
        /// <param name="workspaceManager">工作空间管理器</param>
        /// <param name="performanceMonitor">性能监控服务</param>
        /// <param name="resourceLocks">资源锁服务</param>
        public FlowEngineService(
            IRuntimeManager runtimeManager, 
            ILogService logService, 
            IWorkspaceManager workspaceManager, 
            IPerformanceMonitor performanceMonitor,
            IResourceLockService resourceLocks)
        {
            _runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
            _performanceMonitor = performanceMonitor;
            _resourceLocks = resourceLocks ?? throw new ArgumentNullException(nameof(resourceLocks));
        }

        /// <summary>
        /// 会话启动锁的资源名
        /// 以会话为粒度互斥；将来若图纸上能声明"本流程占用某台相机/某组轴"，
        /// 就在同一个服务上按设备名再叠一层锁，这里保持不变。
        /// </summary>
        private static string StartLockKey(FlowSession session) => $"FlowSession:{session.SessionID}";

        /// <summary>
        /// 原子地占用会话的执行权
        ///
        /// 为什么不能沿用 if (session.IsRunning) return; 紧接 session.IsRunning = true;：
        /// IsRunning 的 getter 和 setter 各自单独 lock，"读—判—写"不是一步，
        /// 连点两次启动按钮时两个线程能双双穿过判断。后果不是简单的跑两遍——
        /// 第二次 new CancellationTokenSource() 会把第一次的覆盖掉，
        /// 那个循环线程的令牌从此没人拿得到，变成点"停止"也停不掉的僵尸循环；
        /// 同一批插件实例（Halcon 句柄、相机连接）还会被两个线程同时驱动。
        /// 信号量的 Wait(0) 由系统保证原子，抢不到就是抢不到，缝没有了。
        /// </summary>
        /// <returns>抢到则返回释放句柄，抢不到返回 null（调用方直接放弃启动）</returns>
        private IDisposable TryOccupySession(FlowSession session)
        {
            if (_resourceLocks.TryAcquireLock(StartLockKey(session), out var handle, session.SessionID))
            {
                session.LockedResources.Add(StartLockKey(session));
                return handle;
            }

            return null;
        }

        /// <summary>
        /// 归还会话执行权（句柄可能为 null：从未抢到的那条早退路径）
        /// </summary>
        private void ReleaseSession(FlowSession session, IDisposable handle)
        {
            if (handle == null) return;

            handle.Dispose();
            session.LockedResources.Remove(StartLockKey(session));
        }

        /// <summary>
        /// 获取当前活跃会话数量
        /// </summary>
        public int ActiveSessionCount => _runtimeManager.ActiveSessions.Count(s => s.State == SessionState.Running);

        /// <summary>
        /// 通知会话状态变更
        /// </summary>
        /// <param name="session">会话实例</param>
        /// <param name="state">新状态</param>
        /// <param name="message">附加消息（可选）</param>
        private void NotifyStateChanged(FlowSession session, SessionState newState, string message = null)
        {
            var oldState = session.State;
            session.State = newState;
            SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs(session.SessionID, session.FlowName, oldState, newState));
        }

        /// <summary>
        /// 启动会话连续执行
        /// </summary>
        /// <param name="session">要执行的会话</param>
        /// <returns>异步任务</returns>
        public async Task RunSessionAsync(FlowSession session)
        {
            if (session == null || session.ExecutionEngine == null)
                throw new ArgumentException("Session 或底层执行引擎不能为空，请先编译！");

            // A3：抢锁即"检查+置位"，抢到才继续；抢不到说明这个会话已经在跑
            var sessionLock = TryOccupySession(session);
            if (sessionLock == null)
            {
                _logService.Warn($"流程 {session.FlowName} 已在运行中，忽略重复的启动请求");
                return;
            }

            // 从抢到锁的那一刻起，后面所有语句都必须在 try 内：
            // 中途抛异常若落不到 finally，这把锁就永久泄漏，该会话除了重启再也启不来
            try
            {
                session.IsRunning = true;
                session.State = SessionState.Running;
                session.PauseLock.Set();
                session.CancellationTokenSource = new CancellationTokenSource();
                var token = session.CancellationTokenSource.Token;

                // 复位所有步序状态
                foreach (var step in session.Blueprints)
                {
                    step.ResetState();
                }

                NotifyStateChanged(session, SessionState.Running);
                _performanceMonitor?.RecordSessionStart(session.SessionID, session.FlowName);

                await Task.Run(() =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        session.PauseLock.Wait(token);

                        if (token.IsCancellationRequested) break;

                        // 每次循环前再次复位所有步序状态
                        foreach (var step in session.Blueprints)
                        {
                            step.ResetState();
                        }

                        var context = new ExecutionContext(_logService, session, _workspaceManager, token);
                        session.ExecutionEngine.Run(context);

                        // 用令牌等待替代 Thread.Sleep：空闲节流 10ms，但停止请求会立即唤醒退出
                        token.WaitHandle.WaitOne(10);
                    }
                }, token);
            }
            catch (OperationCanceledException)
            {
                // 正常取消，无需处理
            }
            catch (Exception ex)
            {
                session.State = SessionState.Faulted;
                _logService.Error($"流程执行异常 {session.FlowName}: {ex.Message}");
                NotifyStateChanged(session, SessionState.Faulted, ex.Message);
            }
            finally
            {
                session.IsRunning = false;

                // 循环退出后清除所有步骤的运行焦点：
                // 停止时机可能落在步骤"置 Running 之后、置 Success 之前"，不清除会导致高亮永久卡住
                foreach (var step in session.Blueprints)
                    step.IsRunningFocus = false;
                session.FocusedStep = null;

                if (session.State != SessionState.Faulted)
                {
                    session.State = SessionState.Stopped;
                    NotifyStateChanged(session, SessionState.Stopped);
                }

                _performanceMonitor?.RecordSessionEnd(session.SessionID);

                session.PauseLock.Set();
                session.CancellationTokenSource?.Dispose();
                session.CancellationTokenSource = null;

                // 最后一步才放锁：IsRunning 已置 false，后来者抢到锁时不会看见半死的旧会话
                ReleaseSession(session, sessionLock);
            }
        }

        /// <summary>
        /// 启动会话单次执行
        /// </summary>
        /// <param name="session">要执行的会话</param>
        /// <returns>异步任务</returns>
        public async Task RunSessionOnceAsync(FlowSession session)
        {
            if (session == null || session.ExecutionEngine == null) return;

            // A3：抢到锁才继续，抢不到说明这个会话已经在跑
            var sessionLock = TryOccupySession(session);
            if (sessionLock == null)
            {
                _logService.Warn($"流程 {session.FlowName} 已在运行中，忽略重复的单次执行请求");
                return;
            }

            try
            {
                session.IsRunning = true;
                session.State = SessionState.Running;

                // 单次执行同样需要取消令牌：否则 StopSession 因 CTS 为 null 而无法停止
                session.CancellationTokenSource = new CancellationTokenSource();
                var token = session.CancellationTokenSource.Token;

                // 复位所有步序状态
                foreach (var step in session.Blueprints)
                {
                    step.ResetState();
                }

                NotifyStateChanged(session, SessionState.Running);
                _performanceMonitor?.RecordSessionStart(session.SessionID, session.FlowName);

                await Task.Run(() =>
                {
                    var context = new ExecutionContext(_logService, session, _workspaceManager, token);
                    session.ExecutionEngine.Run(context);
                }, token);
            }
            catch (OperationCanceledException)
            {
                // 用户主动停止，正常路径
            }
            catch (Exception ex)
            {
                session.State = SessionState.Faulted;
                _logService.Error($"流程单次执行异常 {session.FlowName}: {ex.Message}");
                NotifyStateChanged(session, SessionState.Faulted, ex.Message);
            }
            finally
            {
                session.IsRunning = false;

                // 单次执行结束同样清除运行焦点，避免最终行高亮卡住
                foreach (var step in session.Blueprints)
                    step.IsRunningFocus = false;
                session.FocusedStep = null;

                _performanceMonitor?.RecordSessionEnd(session.SessionID);

                if (session.State != SessionState.Faulted)
                {
                    session.State = SessionState.Stopped;
                    NotifyStateChanged(session, SessionState.Stopped);
                }

                session.CancellationTokenSource?.Dispose();
                session.CancellationTokenSource = null;

                // 与连续执行同理：锁留到最后一步归还
                ReleaseSession(session, sessionLock);
            }
        }

        /// <summary>
        /// 暂停会话执行
        /// </summary>
        /// <param name="session">要暂停的会话</param>
        public void PauseSession(FlowSession session)
        {
            if (session != null && session.IsRunning && session.State == SessionState.Running)
            {
                session.PauseLock.Reset();
                session.State = SessionState.Paused;
                NotifyStateChanged(session, SessionState.Paused);
            }
        }

        /// <summary>
        /// 恢复会话执行
        /// </summary>
        /// <param name="session">要恢复的会话</param>
        public void ResumeSession(FlowSession session)
        {
            if (session != null && session.IsRunning && session.State == SessionState.Paused)
            {
                session.PauseLock.Set();
                session.State = SessionState.Running;
                NotifyStateChanged(session, SessionState.Running);
            }
        }

        /// <summary>
        /// 停止会话执行
        /// </summary>
        /// <param name="session">要停止的会话</param>
        public void StopSession(FlowSession session)
        {
            if (session != null && session.IsRunning && session.CancellationTokenSource != null)
            {
                session.CancellationTokenSource.Cancel();
            }
        }

        /// <summary>
        /// 停止所有会话
        /// </summary>
        public void StopAll()
        {
            foreach (var session in _runtimeManager.ActiveSessions.ToList())
            {
                StopSession(session);
            }
        }
    }
}