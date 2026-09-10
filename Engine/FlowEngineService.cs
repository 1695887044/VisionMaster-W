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
        public FlowEngineService(
            IRuntimeManager runtimeManager, 
            ILogService logService, 
            IWorkspaceManager workspaceManager, 
            IPerformanceMonitor performanceMonitor)
        {
            _runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
            _performanceMonitor = performanceMonitor;
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

            if (session.IsRunning) return;

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

            try
            {
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

                        Thread.Sleep(10);
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

                if (session.State != SessionState.Faulted)
                {
                    session.State = SessionState.Stopped;
                    NotifyStateChanged(session, SessionState.Stopped);
                }

                _performanceMonitor?.RecordSessionEnd(session.SessionID);

                session.PauseLock.Set();
                session.CancellationTokenSource?.Dispose();
                session.CancellationTokenSource = null;
            }
        }

        /// <summary>
        /// 启动会话单次执行
        /// </summary>
        /// <param name="session">要执行的会话</param>
        /// <returns>异步任务</returns>
        public async Task RunSessionOnceAsync(FlowSession session)
        {
            if (session == null || session.ExecutionEngine == null || session.IsRunning) return;

            session.IsRunning = true;
            session.State = SessionState.Running;

            // 复位所有步序状态
            foreach (var step in session.Blueprints)
            {
                step.ResetState();
            }

            NotifyStateChanged(session, SessionState.Running);
            _performanceMonitor?.RecordSessionStart(session.SessionID, session.FlowName);

            try
            {
                await Task.Run(() =>
                {
                    var context = new ExecutionContext(_logService, session, _workspaceManager, 
                        session.CancellationTokenSource?.Token ?? CancellationToken.None);
                    session.ExecutionEngine.Run(context);
                });
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
                _performanceMonitor?.RecordSessionEnd(session.SessionID);

                if (session.State != SessionState.Faulted)
                {
                    session.State = SessionState.Stopped;
                    NotifyStateChanged(session, SessionState.Stopped);
                }
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