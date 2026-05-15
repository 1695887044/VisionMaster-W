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
                    UnregisterSession(existing.SessionID);
                }
                ActiveSessions.Add(session);
            }
        }

        /// <summary>
        /// 注销会话
        /// </summary>
        public void UnregisterSession(string sessionId)
        {
            lock (_lock)
            {
                var session = ActiveSessions.FirstOrDefault(s => s.SessionID == sessionId);
                if (session != null)
                {
                    ActiveSessions.Remove(session);
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
        /// </summary>
        public void ClearAll()
        {
            lock (_lock)
            {
                ActiveSessions.Clear();
            }
        }
    }
}
