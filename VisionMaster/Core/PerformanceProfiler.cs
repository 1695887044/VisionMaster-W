using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Core
{
    /// <summary>
    /// 高性能代码执行时间监控器（支持嵌套、多线程、统计报告）
    /// </summary>
    public sealed class PerformanceProfiler : IDisposable
    {
        private static readonly Lazy<PerformanceProfiler> _instance = new(() => new PerformanceProfiler());
        public static PerformanceProfiler Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, PerformanceStats> _stats = new();
        private readonly AsyncLocal<Stopwatch> _currentStopwatch = new();
        private bool _disposed;

        /// <summary>
        /// 开始计时
        /// </summary>
        /// <param name="operationName">操作名称（建议用"模块.方法"格式）</param>
        public void Start(string operationName)
        {
            if (_disposed) return;
            _currentStopwatch.Value = Stopwatch.StartNew();
        }

        /// <summary>
        /// 结束计时并记录
        /// </summary>
        /// <param name="operationName">操作名称（必须与Start一致）</param>
        public void Stop(string operationName)
        {
            if (_disposed || _currentStopwatch.Value == null) return;

            var sw = _currentStopwatch.Value;
            sw.Stop();
            var elapsedMs = sw.Elapsed.TotalMilliseconds;

            _stats.AddOrUpdate(operationName,
                _ => new PerformanceStats { TotalMs = elapsedMs, Count = 1, MaxMs = elapsedMs, MinMs = elapsedMs },
                (_, existing) =>
                {
                    existing.TotalMs += elapsedMs;
                    existing.Count++;
                    if (elapsedMs > existing.MaxMs) existing.MaxMs = elapsedMs;
                    if (elapsedMs < existing.MinMs) existing.MinMs = elapsedMs;
                    return existing;
                });

            _currentStopwatch.Value = null;
        }

        /// <summary>
        /// 便捷计时方法（using语法）
        /// </summary>
        public IDisposable Measure(string operationName)
        {
            Start(operationName);
            return new DisposableAction(() => Stop(operationName));
        }

        /// <summary>
        /// 生成性能报告
        /// </summary>
        public string GenerateReport()
        {
            if (_stats.IsEmpty) return "暂无性能数据";

            var report = _stats.OrderByDescending(kv => kv.Value.TotalMs)
                .Select(kv => $"{kv.Key,-40} | 总耗时: {kv.Value.TotalMs,8:F2}ms | 次数: {kv.Value.Count,5} | 平均: {kv.Value.AvgMs,6:F2}ms | 最大: {kv.Value.MaxMs,6:F2}ms | 最小: {kv.Value.MinMs,6:F2}ms");

            return "性能监控报告\n" + string.Join("\n", report);
        }

        /// <summary>
        /// 清空所有统计数据
        /// </summary>
        public void Clear() => _stats.Clear();

        public void Dispose()
        {
            _disposed = true;
            _stats.Clear();
        }

        private class PerformanceStats
        {
            public double TotalMs { get; set; }
            public int Count { get; set; }
            public double MaxMs { get; set; }
            public double MinMs { get; set; }
            public double AvgMs => TotalMs / Count;
        }

        private class DisposableAction : IDisposable
        {
            private readonly Action _action;
            public DisposableAction(Action action) => _action = action;
            public void Dispose() => _action();
        }
    }
}