using System;
using System.Threading.Tasks;
using Prism.Mvvm;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>健康检查结果类，存储连接健康检查的详细结果。</para>
    /// <para>该类继承BindableBase，支持属性变更通知，便于UI绑定。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// var result = new HealthCheckResult
    /// {
    ///     IsHealthy = true,
    ///     ResponseTimeMs = 15.5,
    ///     StatusMessage = "Connection OK"
    /// };
    /// </code>
    /// </example>
    /// <seealso cref="IConnectionHealthCheck"/>
    public class HealthCheckResult : BindableBase
    {
        #region 私有字段

        /// <summary>是否健康</summary>
        private bool _isHealthy;

        /// <summary>响应时间（毫秒）</summary>
        private double _responseTimeMs;

        /// <summary>连续失败次数</summary>
        private int _consecutiveFailures;

        /// <summary>状态消息</summary>
        private string _statusMessage = "";

        /// <summary>检查时间</summary>
        private DateTime _checkedTime;

        #endregion

        #region 公共属性

        /// <summary>
        /// <para>获取或设置连接是否健康。</para>
        /// <para>判断标准：连续失败次数小于3次，且能够成功通信。</para>
        /// </summary>
        /// <value>健康返回true，否则返回false</value>
        public bool IsHealthy
        {
            get => _isHealthy;
            set => SetProperty(ref _isHealthy, value);
        }

        /// <summary>
        /// <para>获取或设置响应时间。</para>
        /// <para>单位为毫秒（ms），表示从发送请求到收到响应的时间。</para>
        /// </summary>
        /// <value>响应时间（毫秒）</value>
        /// <remarks>
        /// <para>响应时间的参考标准：</para>
        /// <list type="bullet">
        ///   <item>&lt; 50ms：优秀</item>
        ///   <item>50-200ms：良好</item>
        ///   <item>200-500ms：一般</item>
        ///   <item>&gt; 500ms：较差，可能需要优化</item>
        /// </list>
        /// </remarks>
        public double ResponseTimeMs
        {
            get => _responseTimeMs;
            set => SetProperty(ref _responseTimeMs, value);
        }

        /// <summary>
        /// <para>获取或设置连续失败次数。</para>
        /// <para>从最后一次成功通信后，连续失败的次数。</para>
        /// </summary>
        /// <value>连续失败次数</value>
        /// <remarks>
        /// <para>该值用于判断连接是否需要重建：</para>
        /// <list type="bullet">
        ///   <item>0：正常通信中</item>
        ///   <item>1-2：偶发通信问题</item>
        ///   <item>≥3：连接可能已断开，建议重建</item>
        /// </list>
        /// </remarks>
        public int ConsecutiveFailures
        {
            get => _consecutiveFailures;
            set => SetProperty(ref _consecutiveFailures, value);
        }

        /// <summary>
        /// <para>获取或设置状态消息。</para>
        /// <para>描述当前健康状态的详细信息或错误原因。</para>
        /// </summary>
        /// <value>状态消息文本</value>
        /// <example>
        /// <code>
        /// // 正常状态
        /// StatusMessage = "Connection healthy";
        /// 
        /// // 异常状态
        /// StatusMessage = "Connection timeout after 3000ms";
        /// </code>
        /// </example>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// <para>获取或设置检查时间。</para>
        /// <para>记录此次健康检查的时间戳。</para>
        /// </summary>
        /// <value>检查时间</value>
        public DateTime CheckedTime
        {
            get => _checkedTime;
            set => SetProperty(ref _checkedTime, value);
        }

        #endregion

        #region 方法

        /// <summary>
        /// <para>获取状态描述。</para>
        /// </summary>
        /// <returns>人类可读的状态描述</returns>
        public override string ToString()
        {
            return $"[{CheckedTime:yyyy-MM-dd HH:mm:ss}] " +
                   $"{(IsHealthy ? "Healthy" : "Unhealthy")} - " +
                   $"{StatusMessage} ({ResponseTimeMs:F2}ms)";
        }

        #endregion
    }

    /// <summary>
    /// <para>连接健康检查接口，定义连接健康监控的标准行为。</para>
    /// <para>该接口提供连接状态的检测和持续监控功能。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 创建健康检查器
    /// var healthCheck = new ConnectionHealthCheck(commManager);
    /// 
    /// // 单次检查
    /// var result = await healthCheck.CheckHealthAsync("PLC_1");
    /// 
    /// // 启动持续监控
    /// healthCheck.StartHealthMonitor("PLC_1", 30000); // 每30秒检查一次
    /// healthCheck.HealthCheckCompleted += OnHealthCheck;
    /// </code>
    /// </example>
    /// <seealso cref="HealthCheckResult"/>
    /// <seealso cref="ConnectionHealthCheck"/>
    public interface IConnectionHealthCheck
    {
        #region 方法

        /// <summary>
        /// <para>对指定连接执行一次健康检查。</para>
        /// <para>检查过程包括：</para>
        /// <list type="number">
        ///   <item>检查连接是否存在</item>
        ///   <item>尝试进行通信测试</item>
        ///   <item>记录响应时间</item>
        ///   <item>更新连续失败计数</item>
        /// </list>
        /// </summary>
        /// <param name="connectionName">
        /// <para>要检查的连接名称。</para>
        /// </param>
        /// <returns>
        /// <para>包含检查结果的HealthCheckResult对象。</para>
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// 当connectionName为null或空字符串时抛出。
        /// </exception>
        /// <example>
        /// <code>
        /// var result = await healthCheck.CheckHealthAsync("PLC_1");
        /// if (result.IsHealthy)
        /// {
        ///     Console.WriteLine($"响应时间: {result.ResponseTimeMs}ms");
        /// }
        /// else
        /// {
        ///     Console.WriteLine($"连接异常: {result.StatusMessage}");
        /// }
        /// </code>
        /// </example>
        Task<HealthCheckResult> CheckHealthAsync(string connectionName);

        /// <summary>
        /// <para>启动对指定连接的持续健康监控。</para>
        /// <para>监控将以固定间隔自动执行健康检查，并触发事件通知。</para>
        /// </summary>
        /// <param name="connectionName">
        /// <para>要监控的连接名称。</para>
        /// </param>
        /// <param name="intervalMs">
        /// <para>检查间隔（毫秒）。</para>
        /// <para>默认值：60000（60秒）</para>
        /// <para>建议值：30000-120000（30秒-2分钟）</para>
        /// </param>
        /// <remarks>
        /// <para>注意事项：</para>
        /// <list type="bullet">
        ///   <item>如果已存在该连接的监控，将忽略此次调用</item>
        ///   <item>监控会持续执行，直到调用StopHealthMonitor或Dispose</item>
        ///   <item>建议合理设置间隔，避免过频繁检查影响性能</item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 启动每分钟检查一次
        /// healthCheck.StartHealthMonitor("PLC_1", 60000);
        /// 
        /// // 订阅检查完成事件
        /// healthCheck.HealthCheckCompleted += (s, e) =>
        /// {
        ///     if (!e.IsHealthy)
        ///     {
        ///         // 发送告警通知
        ///         SendAlert($"连接 {e.ConnectionName} 不健康");
        ///     }
        /// };
        /// </code>
        /// </example>
        /// <seealso cref="StopHealthMonitor(string)"/>
        void StartHealthMonitor(string connectionName, int intervalMs = 60000);

        /// <summary>
        /// <para>停止对指定连接的持续健康监控。</para>
        /// </summary>
        /// <param name="connectionName">
        /// <para>要停止监控的连接名称。</para>
        /// </param>
        /// <remarks>
        /// <para>注意事项：</para>
        /// <list type="bullet">
        ///   <item>如果该连接没有正在进行的监控，调用将被忽略</item>
        ///   <item>停止后，仍可通过GetLastResult获取最后的检查结果</item>
        ///   <item>不会影响连接本身的通信状态</item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 停止监控
        /// healthCheck.StopHealthMonitor("PLC_1");
        /// </code>
        /// </example>
        /// <seealso cref="StartHealthMonitor(string, int)"/>
        void StopHealthMonitor(string connectionName);

        /// <summary>
        /// <para>获取指定连接的最后一次健康检查结果。</para>
        /// </summary>
        /// <param name="connectionName">
        /// <para>连接名称。</para>
        /// </param>
        /// <returns>
        /// <para>如果存在返回最后的检查结果。</para>
        /// <para>如果从未检查过或连接不存在，返回null。</para>
        /// </returns>
        /// <remarks>
        /// <para>此方法用于快速获取状态，无需等待新的检查完成。</para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var lastResult = healthCheck.GetLastResult("PLC_1");
        /// if (lastResult != null)
        /// {
        ///     Console.WriteLine($"最后检查: {lastResult.CheckedTime}");
        ///     Console.WriteLine($"状态: {(lastResult.IsHealthy ? "健康" : "异常")}");
        /// }
        /// </code>
        /// </example>
        HealthCheckResult? GetLastResult(string connectionName);

        #endregion

        #region 事件

        /// <summary>
        /// <para>健康检查完成事件。</para>
        /// <para>当定时健康检查完成时触发（仅通过StartHealthMonitor启动的检查）。</para>
        /// </summary>
        /// <remarks>
        /// <para>通过CheckHealthAsync直接调用不会触发此事件。</para>
        /// </remarks>
        event EventHandler<HealthCheckResult>? HealthCheckCompleted;

        #endregion
    }
}
