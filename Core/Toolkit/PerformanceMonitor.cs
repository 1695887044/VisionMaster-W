using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VisionMaster.Core
{
    /// <summary>
    /// 性能监控服务接口
    /// 提供节点执行时间统计、会话执行统计和性能快照功能
    /// </summary>
    public interface IPerformanceMonitor
    {
        /// <summary>
        /// 开始节点计时
        /// </summary>
        /// <param name="nodeId">节点ID</param>
        /// <param name="nodeName">节点名称</param>
        /// <param name="sessionId">会话ID</param>
        void StartNodeTiming(Guid nodeId, string nodeName, string sessionId);

        /// <summary>
        /// 结束节点计时
        /// </summary>
        /// <param name="nodeId">节点ID</param>
        void EndNodeTiming(Guid nodeId);

        /// <summary>
        /// 记录会话开始
        /// </summary>
        /// <param name="sessionId">会话ID</param>
        /// <param name="flowName">流程名称</param>
        void RecordSessionStart(string sessionId, string flowName);

        /// <summary>
        /// 记录会话结束
        /// </summary>
        /// <param name="sessionId">会话ID</param>
        void RecordSessionEnd(string sessionId);

        /// <summary>
        /// 获取性能快照
        /// </summary>
        /// <returns>性能快照对象</returns>
        PerformanceSnapshot GetSnapshot();

        /// <summary>
        /// 获取节点统计信息
        /// </summary>
        /// <param name="nodeId">节点ID</param>
        /// <returns>节点性能统计</returns>
        NodePerformanceStats GetNodeStats(Guid nodeId);

        /// <summary>
        /// 获取会话统计信息
        /// </summary>
        /// <param name="sessionId">会话ID</param>
        /// <returns>会话性能统计</returns>
        SessionPerformanceStats GetSessionStats(string sessionId);

        /// <summary>
        /// 清除所有统计数据
        /// </summary>
        void ClearAllStats();
    }

    /// <summary>
    /// 性能监控服务实现
    /// 提供高效的线程安全性能统计功能
    /// </summary>
    public class PerformanceMonitor : IPerformanceMonitor
    {
        /// <summary>
        /// 节点计时器字典
        /// </summary>
        private readonly ConcurrentDictionary<Guid, Stopwatch> _nodeTimers = new ConcurrentDictionary<Guid, Stopwatch>();

        /// <summary>
        /// 节点统计数据字典
        /// </summary>
        private readonly ConcurrentDictionary<Guid, NodePerformanceStats> _nodeStats = new ConcurrentDictionary<Guid, NodePerformanceStats>();

        /// <summary>
        /// 会话统计数据字典
        /// </summary>
        private readonly ConcurrentDictionary<string, SessionPerformanceStats> _sessionStats = new ConcurrentDictionary<string, SessionPerformanceStats>();

        /// <summary>
        /// 快照锁
        /// </summary>
        private readonly object _snapshotLock = new object();

        /// <summary>
        /// 开始节点计时
        /// </summary>
        public void StartNodeTiming(Guid nodeId, string nodeName, string sessionId)
        {
            var stopwatch = Stopwatch.StartNew();
            _nodeTimers.AddOrUpdate(nodeId, stopwatch, (_, _) => stopwatch);

            _nodeStats.AddOrUpdate(nodeId,
                _ => new NodePerformanceStats { NodeId = nodeId, NodeName = nodeName },
                (_, stats) => { stats.NodeName = nodeName; return stats; });
        }

        /// <summary>
        /// 结束节点计时
        /// </summary>
        public void EndNodeTiming(Guid nodeId)
        {
            if (_nodeTimers.TryRemove(nodeId, out var stopwatch))
            {
                long elapsedMs = stopwatch.ElapsedMilliseconds;

                _nodeStats.AddOrUpdate(nodeId,
                    _ => new NodePerformanceStats { NodeId = nodeId, TotalExecutionTimeMs = elapsedMs, ExecutionCount = 1 },
                    (_, stats) =>
                    {
                        stats.TotalExecutionTimeMs += elapsedMs;
                        stats.ExecutionCount++;
                        stats.MaxExecutionTimeMs = Math.Max(stats.MaxExecutionTimeMs, elapsedMs);
                        stats.MinExecutionTimeMs = Math.Min(stats.MinExecutionTimeMs, elapsedMs);
                        return stats;
                    });
            }
        }

        /// <summary>
        /// 记录会话开始
        /// </summary>
        public void RecordSessionStart(string sessionId, string flowName)
        {
            _sessionStats.AddOrUpdate(sessionId,
                _ => new SessionPerformanceStats
                {
                    SessionId = sessionId,
                    FlowName = flowName,
                    StartTime = DateTime.Now,
                    ExecutionCount = 1
                },
                (_, stats) =>
                {
                    stats.StartTime = DateTime.Now;
                    stats.ExecutionCount++;
                    return stats;
                });
        }

        /// <summary>
        /// 记录会话结束
        /// </summary>
        public void RecordSessionEnd(string sessionId)
        {
            if (_sessionStats.TryGetValue(sessionId, out var stats))
            {
                stats.EndTime = DateTime.Now;
                stats.TotalDurationMs = (stats.EndTime - stats.StartTime).TotalMilliseconds;
            }
        }

        /// <summary>
        /// 获取性能快照
        /// </summary>
        public PerformanceSnapshot GetSnapshot()
        {
            lock (_snapshotLock)
            {
                return new PerformanceSnapshot
                {
                    Timestamp = DateTime.Now,
                    NodeStats = _nodeStats.Values.ToList(),
                    SessionStats = _sessionStats.Values.ToList(),
                    ActiveTimerCount = _nodeTimers.Count
                };
            }
        }

        /// <summary>
        /// 获取节点统计信息
        /// </summary>
        public NodePerformanceStats GetNodeStats(Guid nodeId)
        {
            _nodeStats.TryGetValue(nodeId, out var stats);
            return stats;
        }

        /// <summary>
        /// 获取会话统计信息
        /// </summary>
        public SessionPerformanceStats GetSessionStats(string sessionId)
        {
            _sessionStats.TryGetValue(sessionId, out var stats);
            return stats;
        }

        /// <summary>
        /// 清除所有统计数据
        /// </summary>
        public void ClearAllStats()
        {
            _nodeTimers.Clear();
            _nodeStats.Clear();
            _sessionStats.Clear();
        }
    }

    /// <summary>
    /// 节点性能统计
    /// </summary>
    public class NodePerformanceStats
    {
        /// <summary>
        /// 节点ID
        /// </summary>
        public Guid NodeId { get; set; }

        /// <summary>
        /// 节点名称
        /// </summary>
        public string NodeName { get; set; }

        /// <summary>
        /// 执行次数
        /// </summary>
        public long ExecutionCount { get; set; }

        /// <summary>
        /// 总执行时间（毫秒）
        /// </summary>
        public long TotalExecutionTimeMs { get; set; }

        /// <summary>
        /// 最大执行时间（毫秒）
        /// </summary>
        public long MaxExecutionTimeMs { get; set; }

        /// <summary>
        /// 最小执行时间（毫秒）
        /// </summary>
        public long MinExecutionTimeMs { get; set; } = long.MaxValue;

        /// <summary>
        /// 平均执行时间（毫秒）
        /// </summary>
        public double AverageExecutionTimeMs => ExecutionCount > 0 ? (double)TotalExecutionTimeMs / ExecutionCount : 0;
    }

    /// <summary>
    /// 会话性能统计
    /// </summary>
    public class SessionPerformanceStats
    {
        /// <summary>
        /// 会话ID
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// 流程名称
        /// </summary>
        public string FlowName { get; set; }

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 执行次数
        /// </summary>
        public long ExecutionCount { get; set; }

        /// <summary>
        /// 总持续时间（毫秒）
        /// </summary>
        public double TotalDurationMs { get; set; }
    }

    /// <summary>
    /// 性能快照
    /// </summary>
    public class PerformanceSnapshot
    {
        /// <summary>
        /// 快照时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 节点统计列表
        /// </summary>
        public List<NodePerformanceStats> NodeStats { get; set; }

        /// <summary>
        /// 会话统计列表
        /// </summary>
        public List<SessionPerformanceStats> SessionStats { get; set; }

        /// <summary>
        /// 活动计时器数量
        /// </summary>
        public int ActiveTimerCount { get; set; }
    }
}