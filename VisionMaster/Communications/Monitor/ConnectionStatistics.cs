using System;
using System.Collections.Generic;
using System.ComponentModel;
using Prism.Mvvm;
using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>通讯质量统计类，用于监控和记录通讯连接的性能指标。</para>
    /// <para>统计内容包括读写次数、成功率、响应时间等。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// var stats = new ConnectionStatistics { ConnectionName = "PLC_1" };
    /// 
    /// // 记录读取成功
    /// stats.RecordReadSuccess(15.5);  // 15.5ms响应时间
    /// 
    /// // 记录读取失败
    /// stats.RecordReadFailure("Connection timeout");
    /// 
    /// // 查看统计
    /// Console.WriteLine($"成功率: {stats.ReadSuccessRate:F2}%");
    /// Console.WriteLine($"平均响应时间: {stats.AverageReadTimeMs:F2}ms");
    /// Console.WriteLine($"是否健康: {stats.IsHealthy}");
    /// </code>
    /// </example>
    public class ConnectionStatistics : BindableBase
    {
        #region 私有字段

        /// <summary>连接名称</summary>
        private string _connectionName = "";

        // 读取统计
        private int _totalReads;
        private int _successReads;
        private int _failedReads;

        // 写入统计
        private int _totalWrites;
        private int _successWrites;
        private int _failedWrites;

        // 性能统计
        private double _averageReadTimeMs;
        private double _averageWriteTimeMs;
        private double _maxReadTimeMs;
        private double _maxWriteTimeMs;

        // 时间记录
        private DateTime _lastReadTime;
        private DateTime _lastWriteTime;
        private DateTime _lastErrorTime;
        private DateTime _connectedTime;
        private DateTime _lastSuccessfulReadTime;
        private DateTime _lastSuccessfulWriteTime;

        // 错误信息
        private string _lastErrorMessage = "";

        // 状态
        private int _consecutiveFailures;

        #endregion

        #region 连接信息

        /// <summary>
        /// <para>获取或设置连接名称。</para>
        /// </summary>
        [Browsable(false)]
        public string ConnectionName
        {
            get => _connectionName;
            set => SetProperty(ref _connectionName, value);
        }

        /// <summary>
        /// <para>获取或设置连接建立时间。</para>
        /// </summary>
        [Category("连接信息"), SuperDisplay(Name = "连接建立时间")]
        public DateTime ConnectedTime
        {
            get => _connectedTime;
            set => SetProperty(ref _connectedTime, value);
        }

        /// <summary>
        /// <para>获取连接持续时间。</para>
        /// </summary>
        [Browsable(false)]
        public TimeSpan ConnectionDuration => DateTime.Now - ConnectedTime;

        #endregion

        #region 读取统计

        /// <summary>
        /// <para>获取或设置总读取次数。</para>
        /// </summary>
        [Category("读取统计"), SuperDisplay(Name = "总读取次数")]
        public int TotalReads
        {
            get => _totalReads;
            set => SetProperty(ref _totalReads, value);
        }

        /// <summary>
        /// <para>获取或设置成功读取次数。</para>
        /// </summary>
        [Category("读取统计"), SuperDisplay(Name = "成功读取次数")]
        public int SuccessReads
        {
            get => _successReads;
            set => SetProperty(ref _successReads, value);
        }

        /// <summary>
        /// <para>获取或设置失败读取次数。</para>
        /// </summary>
        [Category("读取统计"), SuperDisplay(Name = "失败读取次数")]
        public int FailedReads
        {
            get => _failedReads;
            set => SetProperty(ref _failedReads, value);
        }

        /// <summary>
        /// <para>获取读取成功率。</para>
        /// <para>计算公式：成功次数 / 总次数 × 100%</para>
        /// </summary>
        /// <value>成功率百分比（0-100）</value>
        [Browsable(false)]
        public double ReadSuccessRate => TotalReads > 0 ? (double)SuccessReads / TotalReads * 100 : 0;

        #endregion

        #region 写入统计

        /// <summary>
        /// <para>获取或设置总写入次数。</para>
        /// </summary>
        [Category("写入统计"), SuperDisplay(Name = "总写入次数")]
        public int TotalWrites
        {
            get => _totalWrites;
            set => SetProperty(ref _totalWrites, value);
        }

        /// <summary>
        /// <para>获取或设置成功写入次数。</para>
        /// </summary>
        [Category("写入统计"), SuperDisplay(Name = "成功写入次数")]
        public int SuccessWrites
        {
            get => _successWrites;
            set => SetProperty(ref _successWrites, value);
        }

        /// <summary>
        /// <para>获取或设置失败写入次数。</para>
        /// </summary>
        [Category("写入统计"), SuperDisplay(Name = "失败写入次数")]
        public int FailedWrites
        {
            get => _failedWrites;
            set => SetProperty(ref _failedWrites, value);
        }

        /// <summary>
        /// <para>获取写入成功率。</para>
        /// </summary>
        [Browsable(false)]
        public double WriteSuccessRate => TotalWrites > 0 ? (double)SuccessWrites / TotalWrites * 100 : 0;

        #endregion

        #region 性能统计

        /// <summary>
        /// <para>获取或设置平均读取时间（毫秒）。</para>
        /// </summary>
        [Category("性能统计"), SuperDisplay(Name = "平均读取时间(ms)")]
        public double AverageReadTimeMs
        {
            get => _averageReadTimeMs;
            set => SetProperty(ref _averageReadTimeMs, value);
        }

        /// <summary>
        /// <para>获取或设置平均写入时间（毫秒）。</para>
        /// </summary>
        [Category("性能统计"), SuperDisplay(Name = "平均写入时间(ms)")]
        public double AverageWriteTimeMs
        {
            get => _averageWriteTimeMs;
            set => SetProperty(ref _averageWriteTimeMs, value);
        }

        /// <summary>
        /// <para>获取或设置最大读取时间（毫秒）。</para>
        /// </summary>
        [Category("性能统计"), SuperDisplay(Name = "最大读取时间(ms)")]
        public double MaxReadTimeMs
        {
            get => _maxReadTimeMs;
            set => SetProperty(ref _maxReadTimeMs, value);
        }

        /// <summary>
        /// <para>获取或设置最大写入时间（毫秒）。</para>
        /// </summary>
        [Category("性能统计"), SuperDisplay(Name = "最大写入时间(ms)")]
        public double MaxWriteTimeMs
        {
            get => _maxWriteTimeMs;
            set => SetProperty(ref _maxWriteTimeMs, value);
        }

        #endregion

        #region 时间记录

        /// <summary>
        /// <para>获取或设置最后读取时间。</para>
        /// </summary>
        [Category("时间记录"), SuperDisplay(Name = "最后读取时间")]
        public DateTime LastReadTime
        {
            get => _lastReadTime;
            set => SetProperty(ref _lastReadTime, value);
        }

        /// <summary>
        /// <para>获取或设置最后写入时间。</para>
        /// </summary>
        [Category("时间记录"), SuperDisplay(Name = "最后写入时间")]
        public DateTime LastWriteTime
        {
            get => _lastWriteTime;
            set => SetProperty(ref _lastWriteTime, value);
        }

        /// <summary>
        /// <para>获取或设置最后错误发生时间。</para>
        /// </summary>
        [Category("错误记录"), SuperDisplay(Name = "最后错误时间")]
        public DateTime LastErrorTime
        {
            get => _lastErrorTime;
            set => SetProperty(ref _lastErrorTime, value);
        }

        /// <summary>
        /// <para>获取或设置最后错误消息。</para>
        /// </summary>
        [Category("错误记录"), SuperDisplay(Name = "最后错误信息")]
        public string LastErrorMessage
        {
            get => _lastErrorMessage;
            set => SetProperty(ref _lastErrorMessage, value);
        }

        /// <summary>
        /// <para>获取或设置最后成功读取时间。</para>
        /// </summary>
        [Category("状态监控"), SuperDisplay(Name = "最后成功读取")]
        public DateTime LastSuccessfulReadTime
        {
            get => _lastSuccessfulReadTime;
            set => SetProperty(ref _lastSuccessfulReadTime, value);
        }

        /// <summary>
        /// <para>获取或设置最后成功写入时间。</para>
        /// </summary>
        [Category("状态监控"), SuperDisplay(Name = "最后成功写入")]
        public DateTime LastSuccessfulWriteTime
        {
            get => _lastSuccessfulWriteTime;
            set => SetProperty(ref _lastSuccessfulWriteTime, value);
        }

        #endregion

        #region 状态

        /// <summary>
        /// <para>获取或设置连续失败次数。</para>
        /// <para>用于判断连接是否需要重建。</para>
        /// </summary>
        [Category("状态监控"), SuperDisplay(Name = "连续失败次数")]
        public int ConsecutiveFailures
        {
            get => _consecutiveFailures;
            set => SetProperty(ref _consecutiveFailures, value);
        }

        /// <summary>
        /// <para>获取连接是否健康。</para>
        /// <para>判断标准：连续失败次数小于3次。</para>
        /// </summary>
        [Browsable(false)]
        public bool IsHealthy => ConsecutiveFailures < 3;

        #endregion

        #region 公共方法

        /// <summary>
        /// <para>记录一次成功的读取操作。</para>
        /// </summary>
        /// <param name="responseTimeMs">响应时间（毫秒）</param>
        public void RecordReadSuccess(double responseTimeMs)
        {
            TotalReads++;
            SuccessReads++;
            LastReadTime = DateTime.Now;
            LastSuccessfulReadTime = DateTime.Now;
            ConsecutiveFailures = 0;

            // 更新平均响应时间
            AverageReadTimeMs = (AverageReadTimeMs * (TotalReads - 1) + responseTimeMs) / TotalReads;

            // 更新最大响应时间
            if (responseTimeMs > MaxReadTimeMs) MaxReadTimeMs = responseTimeMs;
        }

        /// <summary>
        /// <para>记录一次失败的读取操作。</para>
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        public void RecordReadFailure(string errorMessage)
        {
            TotalReads++;
            FailedReads++;
            LastReadTime = DateTime.Now;
            LastErrorTime = DateTime.Now;
            LastErrorMessage = errorMessage;
            ConsecutiveFailures++;
        }

        /// <summary>
        /// <para>记录一次成功的写入操作。</para>
        /// </summary>
        /// <param name="responseTimeMs">响应时间（毫秒）</param>
        public void RecordWriteSuccess(double responseTimeMs)
        {
            TotalWrites++;
            SuccessWrites++;
            LastWriteTime = DateTime.Now;
            LastSuccessfulWriteTime = DateTime.Now;

            // 更新平均响应时间
            AverageWriteTimeMs = (AverageWriteTimeMs * (TotalWrites - 1) + responseTimeMs) / TotalWrites;

            // 更新最大响应时间
            if (responseTimeMs > MaxWriteTimeMs) MaxWriteTimeMs = responseTimeMs;
        }

        /// <summary>
        /// <para>记录一次失败的写入操作。</para>
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        public void RecordWriteFailure(string errorMessage)
        {
            TotalWrites++;
            FailedWrites++;
            LastWriteTime = DateTime.Now;
            LastErrorTime = DateTime.Now;
            LastErrorMessage = errorMessage;
        }

        /// <summary>
        /// <para>重置所有统计数据。</para>
        /// </summary>
        public void Reset()
        {
            TotalReads = 0;
            SuccessReads = 0;
            FailedReads = 0;
            TotalWrites = 0;
            SuccessWrites = 0;
            FailedWrites = 0;
            AverageReadTimeMs = 0;
            AverageWriteTimeMs = 0;
            MaxReadTimeMs = 0;
            MaxWriteTimeMs = 0;
            ConsecutiveFailures = 0;
            LastErrorMessage = "";
        }

        /// <summary>
        /// <para>获取统计摘要字符串。</para>
        /// </summary>
        public string GetSummary()
        {
            return $"{ConnectionName}: 读取成功率 {ReadSuccessRate:F1}%, " +
                   $"平均响应 {AverageReadTimeMs:F1}ms, " +
                   $"连续失败 {ConsecutiveFailures}";
        }

        #endregion
    }
}
