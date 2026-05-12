using System;
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
        // 1. 暴露给 UI 绑定的会话集合
        ObservableCollection<FlowSession> ActiveSessions { get; }

        // 2. 基础管理操作
        void RegisterSession(FlowSession session);
        void UnregisterSession(string sessionId);

        // 3. 快速检索
        FlowSession GetSessionById(string sessionId);
        FlowSession GetSessionByName(string flowName);

        // 4. 清理
        void ClearAll();
    }
    public class RuntimeManager : IRuntimeManager
    {
        private readonly object _lock = new object();

        public ObservableCollection<FlowSession> ActiveSessions { get; } = new();

        public RuntimeManager()
        {
            BindingOperations.EnableCollectionSynchronization(ActiveSessions, _lock);
        }

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

        public FlowSession GetSessionById(string sessionId) =>
            ActiveSessions.FirstOrDefault(s => s.SessionID == sessionId);

        public FlowSession GetSessionByName(string flowName) =>
            ActiveSessions.FirstOrDefault(s => s.FlowName == flowName);

        public void ClearAll()
        {
            lock (_lock)
            {
                ActiveSessions.Clear();
            }
        }
    }
}
