using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.EventModel;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    public class FlowEngineService : IFlowEngine
    {
        // 引擎不存机器，但它需要一把“仓库钥匙”，用来执行 StopAll 一键急停
        private readonly IRuntimeManager _runtimeManager;
        private readonly IExecutionContext executionContext;

        // 🌟 对外广播的事件：UI 可以订阅它来改变运行指示灯的颜色
        public event EventHandler<SessionStateChangedEventArgs> SessionStateChanged;

        public FlowEngineService(IRuntimeManager runtimeManager, IExecutionContext executionContext)
        {
            _runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
            this.executionContext = executionContext ?? throw new ArgumentNullException(nameof(executionContext));
        }

        // 统计当前真正在跑的流程数量
        public int ActiveSessionCount => _runtimeManager.ActiveSessions.Count(s => s.State == SessionState.Running);

        // 内部辅助方法：触发状态变更事件
        private void NotifyStateChanged(FlowSession session, SessionState state, string message = null)
        {
            SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs(session, state, message));
        }
        #region 1. 核心执行逻辑

        public async Task RunSessionAsync(FlowSession session)
        {
            if (session == null || session.ExecutionEngine == null)
                throw new ArgumentException("Session 或底层执行引擎不能为空，请先编译！");

            if (session.IsRunning) return;

            // 1. 初始化运行状态
            session.IsRunning = true;
            session.State = SessionState.Running;
            session.PauseLock.Set(); // 确保刚启动时是绿灯（通行）状态
            session.CancellationTokenSource = new CancellationTokenSource();
            var token = session.CancellationTokenSource.Token;

            NotifyStateChanged(session, SessionState.Running);

            try
            {
                // 2. 丢入线程池后台执行
                await Task.Run(() =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        // 🌟 核心暂停点：如果被 Pause() 设为红灯，线程会在这里低耗休眠。
                        // 传入 token 是为了防止死锁：即使在暂停状态下点击停止，也能瞬间打断并抛出取消异常退出。
                        session.PauseLock.Wait(token);

                        // 🌟 核心执行点：将 session 自身作为 Context 传给算子
                        session.ExecutionEngine.Run(executionContext);

                        // 工业节拍控制：释放 CPU 调度权
                        Thread.Sleep(10);
                    }
                }, token);
            }
            catch (OperationCanceledException)
            {
                // 正常停止 (用户点击了停止按钮)，不需要当作异常处理
            }
            catch (Exception ex)
            {
                // 崩溃停止
                session.State = SessionState.Faulted;
                NotifyStateChanged(session, SessionState.Faulted, ex.Message);
            }
            finally
            {
                // 3. 彻底重置机器状态 (不管是正常停还是报错停)
                session.IsRunning = false;

                // 如果不是异常崩溃，就标记为已停止
                if (session.State != SessionState.Faulted)
                {
                    session.State = SessionState.Stopped;
                    NotifyStateChanged(session, SessionState.Stopped);
                }

                // 释放非托管资源
                session.PauseLock.Set(); // 防止停止后锁死
                session.CancellationTokenSource?.Dispose();
                session.CancellationTokenSource = null;
            }
        }

        public async Task RunSessionOnceAsync(FlowSession session)
        {
            if (session == null || session.ExecutionEngine == null || session.IsRunning) return;

            session.IsRunning = true;
            session.State = SessionState.Running;
            NotifyStateChanged(session, SessionState.Running);

            try
            {
                await Task.Run(() =>
                {
                    // 单次执行不需要死循环和暂停锁，直接跑一圈
                    session.ExecutionEngine.Run(executionContext);
                });
            }
            catch (Exception ex)
            {
                session.State = SessionState.Faulted;
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
            }
        }

        #endregion

        #region 2. 状态干预逻辑 (暂停/恢复/停止)

        public void PauseSession(FlowSession session)
        {
            if (session != null && session.IsRunning && session.State == SessionState.Running)
            {
                session.PauseLock.Reset(); // 🚦 亮红灯：拦截下一次循环
                session.State = SessionState.Paused;
                NotifyStateChanged(session, SessionState.Paused);
            }
        }

        public void ResumeSession(FlowSession session)
        {
            if (session != null && session.IsRunning && session.State == SessionState.Paused)
            {
                session.PauseLock.Set(); // 🚦 亮绿灯：放行
                session.State = SessionState.Running;
                NotifyStateChanged(session, SessionState.Running);
            }
        }

        public void StopSession(FlowSession session)
        {
            if (session != null && session.IsRunning && session.CancellationTokenSource != null)
            {
                // 发出取消信号，while 循环和 PauseLock.Wait 都会立刻收到通知并退出
                session.CancellationTokenSource.Cancel();
            }
        }

        public void StopAll()
        {
            // 利用注入的 RuntimeManager，遍历仓库里所有机器直接拉闸
            foreach (var session in _runtimeManager.ActiveSessions.ToList())
            {
                StopSession(session);
            }
        }

        #endregion
    }
}
