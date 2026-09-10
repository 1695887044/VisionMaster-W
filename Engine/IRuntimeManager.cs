﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 运行时管理器接口：负责管理所有已编译的 FlowSession 仓库
    /// </summary>
    public interface IRuntimeManager
    {
        /// <summary>
        /// 暴露给 UI 绑定的活动会话集合
        /// </summary>
        ObservableCollection<FlowSession> ActiveSessions { get; }

        /// <summary>
        /// 注册会话
        /// </summary>
        void RegisterSession(FlowSession session);

        /// <summary>
        /// 注销会话
        /// </summary>
        void UnregisterSession(string sessionId);

        /// <summary>
        /// 根据会话ID获取会话
        /// </summary>
        FlowSession GetSessionById(string sessionId);

        /// <summary>
        /// 根据流程名称获取会话
        /// </summary>
        FlowSession GetSessionByName(string flowName);

        /// <summary>
        /// 清空所有会话
        /// </summary>
        void ClearAll();
    }

    /// <summary>
    /// 运行时管理器实现类
    /// 提供线程安全的会话管理能力
    /// </summary>
    public class RuntimeManager : IRuntimeManager
    {
        private readonly object _lock = new object();

        /// <summary>
        /// 优雅停止会话的执行循环：发出取消令牌并等待循环线程退出（带超时保护）
        /// 替换/注销/清空运行中的会话前必须调用：
        /// 否则旧循环线程脱离 ActiveSessions 管理成为僵尸循环，"停止"按钮永远停不掉它，
        /// 且新旧循环会并发写同一批 StepModel 状态造成 UI 状态错乱
        /// </summary>
        /// <param name="session">待停止的会话</param>
        /// <param name="timeoutMs">等待循环退出的超时（毫秒）</param>
        private static void StopSessionGracefully(FlowSession session, int timeoutMs = 3000)
        {
            if (session == null || !session.IsRunning) return;

            try { session.CancellationTokenSource?.Cancel(); }
            catch (ObjectDisposedException) { /* 令牌已被循环线程释放，说明循环正在退出 */ }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (session.IsRunning && sw.ElapsedMilliseconds < timeoutMs)
                System.Threading.Thread.Sleep(20);
        }

        /// <summary>
        /// 活动会话集合
        /// </summary>
        public ObservableCollection<FlowSession> ActiveSessions { get; } = new();

        /// <summary>
        /// 初始化运行时管理器
        /// 启用集合同步以支持多线程访问
        /// </summary>
        public RuntimeManager()
        {
            BindingOperations.EnableCollectionSynchronization(ActiveSessions, _lock);
        }

        /// <summary>
        /// 注册会话
        /// 如果同名会话已存在，则先注销再注册
        /// </summary>
        public void RegisterSession(FlowSession session)
        {
            lock (_lock)
            {
                var existing = ActiveSessions.FirstOrDefault(s => s.FlowName == session.FlowName);
                if (existing != null)
                {
                    // 先让旧会话的执行循环退出再替换，杜绝僵尸循环
                    StopSessionGracefully(existing);
                    UnregisterSession(existing.SessionID);
                }
                ActiveSessions.Add(session);
            }
        }

        /// <summary>
        /// 注销会话
        /// 注销前先等执行循环退出（超时未退出则保留实例不释放，避免执行线程使用已释放资源）
        /// 再释放旧会话编译创建的插件实例（HImage 等非托管资源），防止重编译泄漏
        /// </summary>
        public void UnregisterSession(string sessionId)
        {
            lock (_lock)
            {
                var session = ActiveSessions.FirstOrDefault(s => s.SessionID == sessionId);
                if (session != null)
                {
                    StopSessionGracefully(session);
                    ActiveSessions.Remove(session);

                    if (!session.IsRunning)
                        session.Dispose();
                }
            }
        }

        /// <summary>
        /// 根据会话ID获取会话
        /// </summary>
        public FlowSession GetSessionById(string sessionId) =>
            ActiveSessions.FirstOrDefault(s => s.SessionID == sessionId);

        /// <summary>
        /// 根据流程名称获取会话
        /// </summary>
        public FlowSession GetSessionByName(string flowName) =>
            ActiveSessions.FirstOrDefault(s => s.FlowName == flowName);

        /// <summary>
        /// 清空所有会话
        /// 清空前先停止每个会话的执行循环（防止切换方案后残留僵尸循环），再释放插件实例
        /// </summary>
        public void ClearAll()
        {
            lock (_lock)
            {
                foreach (var session in ActiveSessions)
                {
                    StopSessionGracefully(session);
                    if (!session.IsRunning)
                        session.Dispose();
                }
                ActiveSessions.Clear();
            }
        }
    }
}
