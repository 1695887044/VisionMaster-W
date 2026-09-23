﻿﻿﻿using System;
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
        /// <summary>
        /// 集合锁：交给 WPF 做 ActiveSessions 的跨线程绑定同步用，
        /// 因此**只允许包住"增/删/查"这几微秒**，绝不能抱着它干等任何东西（见 B3）
        /// </summary>
        private readonly object _lock = new object();

        /// <summary>
        /// 注册表锁：串行化"注册/注销/清空"这类多步操作，
        /// 保证"等旧循环退出 → 摘旧 → 挂新"这整段不被并发调用插进来。
        /// 它和集合锁分家，正是为了让我们能抱着前者长时间等待，而把后者随时放开。
        /// </summary>
        private readonly object _registryLock = new object();

        /// <summary>
        /// 优雅停止会话的执行循环：发出取消令牌并等待循环线程退出（带超时保护）
        /// 替换/注销/清空运行中的会话前必须调用：
        /// 否则旧循环线程脱离 ActiveSessions 管理成为僵尸循环，"停止"按钮永远停不掉它，
        /// 且新旧循环会并发写同一批 StepModel 状态造成 UI 状态错乱
        ///
        /// 【调用约束】只能在 _lock 之外调用。这里最长要等 3 秒，
        /// 而 _lock 是执行线程退出时要用来给集合变更发通知的绑定锁 ——
        /// 抱着它等 = UI 等循环退，循环等 UI 手里的锁，谁也让不了，只能等超时。
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
        /// 在集合锁内取快照（只做线性查找，不做任何等待）
        /// </summary>
        private List<FlowSession> Snapshot()
        {
            lock (_lock)
            {
                return ActiveSessions.ToList();
            }
        }

        /// <summary>
        /// 从集合摘除并（在循环确认退出后）释放会话
        /// </summary>
        private void RemoveAndDispose(FlowSession session)
        {
            lock (_lock)
            {
                ActiveSessions.Remove(session);
            }

            // 超时仍没退出的会话保留实例不释放：执行线程还在用它的插件，Dispose 会踩已释放资源
            if (!session.IsRunning)
                session.Dispose();
        }

        /// <summary>
        /// 注册会话
        /// 如果同名会话已存在，则先注销再注册
        /// </summary>
        public void RegisterSession(FlowSession session)
        {
            lock (_registryLock)
            {
                var existing = Snapshot().FirstOrDefault(s => s.FlowName == session.FlowName);

                // 先让旧会话的执行循环退出再替换，杜绝僵尸循环；等待必须在集合锁外
                StopSessionGracefully(existing);
                if (existing != null)
                    RemoveAndDispose(existing);

                lock (_lock)
                {
                    ActiveSessions.Add(session);
                }
            }
        }

        /// <summary>
        /// 注销会话
        /// 注销前先等执行循环退出（超时未退出则保留实例不释放，避免执行线程使用已释放资源）
        /// 再释放旧会话编译创建的插件实例（HImage 等非托管资源），防止重编译泄漏
        /// </summary>
        public void UnregisterSession(string sessionId)
        {
            lock (_registryLock)
            {
                var session = Snapshot().FirstOrDefault(s => s.SessionID == sessionId);
                if (session == null) return;

                StopSessionGracefully(session);
                RemoveAndDispose(session);
            }
        }

        /// <summary>
        /// 根据会话ID获取会话
        /// </summary>
        public FlowSession GetSessionById(string sessionId)
        {
            // 查必须和增删用同一把集合锁：ObservableCollection 的枚举带版本校验，
            // EnableCollectionSynchronization 只替 WPF 的 CollectionView 兜底，不替我们代码里的 LINQ 兜底。
            // 少了这把锁，后台线程一 Add，UI 线程这次枚举就可能抛 "Collection was modified"。
            lock (_lock)
            {
                return ActiveSessions.FirstOrDefault(s => s.SessionID == sessionId);
            }
        }

        /// <summary>
        /// 根据流程名称获取会话
        /// </summary>
        public FlowSession GetSessionByName(string flowName)
        {
            // 同上：只做一次线性查找，几微秒，绝不在这把锁里等任何东西
            lock (_lock)
            {
                return ActiveSessions.FirstOrDefault(s => s.FlowName == flowName);
            }
        }

        /// <summary>
        /// 清空所有会话
        /// 清空前先停止每个会话的执行循环（防止切换方案后残留僵尸循环），再释放插件实例
        /// </summary>
        public void ClearAll()
        {
            lock (_registryLock)
            {
                var sessions = Snapshot();

                // 逐个等退出，全程不碰集合锁；先集中停再统一摘，
                // 避免"停一个摘一个"时，后面那些会话被反复枚举打断
                foreach (var session in sessions)
                    StopSessionGracefully(session);

                lock (_lock)
                {
                    ActiveSessions.Clear();
                }

                foreach (var session in sessions)
                {
                    if (!session.IsRunning)
                        session.Dispose();
                }
            }
        }
    }
}
