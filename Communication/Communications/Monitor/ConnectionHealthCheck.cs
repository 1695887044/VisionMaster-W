using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>连接健康检查实现类，提供连接的定时监控功能。</para>
    /// <para>该类实现IConnectionHealthCheck接口，支持单次检查和持续监控。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 创建健康检查器
    /// var healthCheck = new ConnectionHealthCheck(commManager);
    /// 
    /// // 单次检查
    /// var result = await healthCheck.CheckHealthAsync("PLC_1");
    /// Console.WriteLine($"健康状态: {result.IsHealthy}");
    /// 
    /// // 启动持续监控
    /// healthCheck.StartHealthMonitor("PLC_1", 60000);
    /// 
    /// // 订阅事件
    /// healthCheck.HealthCheckCompleted += (s, e) =>
    /// {
    ///     if (!e.IsHealthy)
    ///     {
    ///         Console.WriteLine($"警告: {e.StatusMessage}");
    ///     }
    /// };
    /// </code>
    /// </example>
    /// <seealso cref="IConnectionHealthCheck"/>
    /// <seealso cref="HealthCheckResult"/>
    public class ConnectionHealthCheck : IConnectionHealthCheck, IDisposable
    {
        #region 私有字段

        /// <summary>
        /// 底层通讯管理器引用
        /// </summary>
        private readonly AdvancedCommunicationManager _commManager;

        /// <summary>
        /// 健康检查定时器字典
        /// Key: 连接名称, Value: 定时器对象
        /// </summary>
        private readonly Dictionary<string, System.Timers.Timer> _healthTimers = new();

        /// <summary>
        /// 最后检查结果字典
        /// </summary>
        private readonly Dictionary<string, HealthCheckResult> _lastResults = new();

        /// <summary>
        /// 连续失败计数字典
        /// </summary>
        private readonly Dictionary<string, int> _consecutiveFailures = new();

        /// <summary>
        /// 线程同步锁
        /// </summary>
        private readonly object _lock = new();

        /// <summary>
        /// 释放标志
        /// </summary>
        private bool _disposed = false;

        #endregion

        #region 事件

        /// <summary>
        /// <para>健康检查完成事件。</para>
        /// <para>当定时健康检查完成时触发。</para>
        /// </summary>
        public event EventHandler<HealthCheckResult>? HealthCheckCompleted;

        #endregion

        #region 构造方法

        /// <summary>
        /// <para>初始化健康检查器的新实例。</para>
        /// </summary>
        /// <param name="commManager">
        /// <para>底层通讯管理器。</para>
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// 当commManager为null时抛出。
        /// </exception>
        public ConnectionHealthCheck(AdvancedCommunicationManager commManager)
        {
            _commManager = commManager ?? throw new ArgumentNullException(nameof(commManager));
        }

        #endregion

        #region IConnectionHealthCheck 实现

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public async Task<HealthCheckResult> CheckHealthAsync(string connectionName)
        {
            var result = new HealthCheckResult
            {
                CheckedTime = DateTime.Now
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // 获取连接
                var connection = _commManager.GetConnection(connectionName);

                // 检查连接状态
                if (connection == null || !connection.IsConnected)
                {
                    result.IsHealthy = false;
                    result.StatusMessage = "Connection not found or not connected";
                    IncrementFailure(connectionName);
                }
                else
                {
                    try
                    {
                        // 执行连接测试
                        var testResult = connection.TestConnection();
                        stopwatch.Stop();

                        result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                        result.IsHealthy = testResult;

                        if (testResult)
                        {
                            result.StatusMessage = "Healthy";
                            ResetFailure(connectionName);
                        }
                        else
                        {
                            result.StatusMessage = "Connection test failed";
                            IncrementFailure(connectionName);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.IsHealthy = false;
                        result.StatusMessage = $"Exception: {ex.Message}";
                        result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                        IncrementFailure(connectionName);
                    }
                }
            }
            catch (Exception ex)
            {
                result.IsHealthy = false;
                result.StatusMessage = $"Error: {ex.Message}";
                result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                IncrementFailure(connectionName);
            }

            // 设置连续失败次数
            result.ConsecutiveFailures = GetFailureCount(connectionName);

            // 保存结果
            lock (_lock)
            {
                _lastResults[connectionName] = result;
            }

            return result;
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public void StartHealthMonitor(string connectionName, int intervalMs = 60000)
        {
            lock (_lock)
            {
                // 防止重复启动
                if (_healthTimers.ContainsKey(connectionName))
                    return;

                // 创建定时器
                var timer = new System.Timers.Timer(intervalMs);
                timer.Elapsed += async (s, e) =>
                {
                    var result = await CheckHealthAsync(connectionName);
                    HealthCheckCompleted?.Invoke(this, result);
                };
                timer.AutoReset = true;
                timer.Start();

                // 保存定时器
                _healthTimers[connectionName] = timer;
            }
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public void StopHealthMonitor(string connectionName)
        {
            lock (_lock)
            {
                if (_healthTimers.Remove(connectionName, out var timer))
                {
                    timer.Stop();
                    timer.Dispose();
                }
            }
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public HealthCheckResult? GetLastResult(string connectionName)
        {
            lock (_lock)
            {
                return _lastResults.TryGetValue(connectionName, out var result) ? result : null;
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// <para>增加连续失败计数。</para>
        /// </summary>
        private void IncrementFailure(string connectionName)
        {
            lock (_lock)
            {
                if (!_consecutiveFailures.ContainsKey(connectionName))
                    _consecutiveFailures[connectionName] = 0;

                _consecutiveFailures[connectionName]++;
            }
        }

        /// <summary>
        /// <para>重置连续失败计数。</para>
        /// </summary>
        private void ResetFailure(string connectionName)
        {
            lock (_lock)
            {
                _consecutiveFailures[connectionName] = 0;
            }
        }

        /// <summary>
        /// <para>获取连续失败计数。</para>
        /// </summary>
        private int GetFailureCount(string connectionName)
        {
            lock (_lock)
            {
                return _consecutiveFailures.TryGetValue(connectionName, out var count) ? count : 0;
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// <para>释放健康检查器使用的所有资源。</para>
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_lock)
            {
                // 停止并释放所有定时器
                foreach (var timer in _healthTimers.Values)
                {
                    timer.Stop();
                    timer.Dispose();
                }
                _healthTimers.Clear();
            }
        }

        #endregion
    }
}
