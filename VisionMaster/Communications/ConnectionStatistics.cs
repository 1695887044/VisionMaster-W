using System;
using System.Collections.Generic;
using System.ComponentModel;
using Prism.Mvvm;
using UI.Attributes;

namespace VisionMaster.Communications
{
    public class ConnectionStatistics : BindableBase
    {
        private string _connectionName = "";
        private int _totalReads;
        private int _successReads;
        private int _failedReads;
        private int _totalWrites;
        private int _successWrites;
        private int _failedWrites;
        private double _averageReadTimeMs;
        private double _averageWriteTimeMs;
        private double _maxReadTimeMs;
        private double _maxWriteTimeMs;
        private DateTime _lastReadTime;
        private DateTime _lastWriteTime;
        private DateTime _lastErrorTime;
        private string _lastErrorMessage = "";
        private DateTime _connectedTime;
        private DateTime _lastSuccessfulReadTime;
        private DateTime _lastSuccessfulWriteTime;
        private int _consecutiveFailures;

        [Browsable(false)]
        public string ConnectionName
        {
            get => _connectionName;
            set => SetProperty(ref _connectionName, value);
        }

        [Category("读取统计"), SuperDisplay(Name = "总读取次数")]
        public int TotalReads
        {
            get => _totalReads;
            set => SetProperty(ref _totalReads, value);
        }

        [Category("读取统计"), SuperDisplay(Name = "成功读取次数")]
        public int SuccessReads
        {
            get => _successReads;
            set => SetProperty(ref _successReads, value);
        }

        [Category("读取统计"), SuperDisplay(Name = "失败读取次数")]
        public int FailedReads
        {
            get => _failedReads;
            set => SetProperty(ref _failedReads, value);
        }

        [Browsable(false)]
        public double ReadSuccessRate => TotalReads > 0 ? (double)SuccessReads / TotalReads * 100 : 0;

        [Category("写入统计"), SuperDisplay(Name = "总写入次数")]
        public int TotalWrites
        {
            get => _totalWrites;
            set => SetProperty(ref _totalWrites, value);
        }

        [Category("写入统计"), SuperDisplay(Name = "成功写入次数")]
        public int SuccessWrites
        {
            get => _successWrites;
            set => SetProperty(ref _successWrites, value);
        }

        [Category("写入统计"), SuperDisplay(Name = "失败写入次数")]
        public int FailedWrites
        {
            get => _failedWrites;
            set => SetProperty(ref _failedWrites, value);
        }

        [Browsable(false)]
        public double WriteSuccessRate => TotalWrites > 0 ? (double)SuccessWrites / TotalWrites * 100 : 0;

        [Category("性能统计"), SuperDisplay(Name = "平均读取时间(ms)")]
        public double AverageReadTimeMs
        {
            get => _averageReadTimeMs;
            set => SetProperty(ref _averageReadTimeMs, value);
        }

        [Category("性能统计"), SuperDisplay(Name = "平均写入时间(ms)")]
        public double AverageWriteTimeMs
        {
            get => _averageWriteTimeMs;
            set => SetProperty(ref _averageWriteTimeMs, value);
        }

        [Category("性能统计"), SuperDisplay(Name = "最大读取时间(ms)")]
        public double MaxReadTimeMs
        {
            get => _maxReadTimeMs;
            set => SetProperty(ref _maxReadTimeMs, value);
        }

        [Category("性能统计"), SuperDisplay(Name = "最大写入时间(ms)")]
        public double MaxWriteTimeMs
        {
            get => _maxWriteTimeMs;
            set => SetProperty(ref _maxWriteTimeMs, value);
        }

        [Category("时间记录"), SuperDisplay(Name = "最后读取时间")]
        public DateTime LastReadTime
        {
            get => _lastReadTime;
            set => SetProperty(ref _lastReadTime, value);
        }

        [Category("时间记录"), SuperDisplay(Name = "最后写入时间")]
        public DateTime LastWriteTime
        {
            get => _lastWriteTime;
            set => SetProperty(ref _lastWriteTime, value);
        }

        [Category("错误记录"), SuperDisplay(Name = "最后错误时间")]
        public DateTime LastErrorTime
        {
            get => _lastErrorTime;
            set => SetProperty(ref _lastErrorTime, value);
        }

        [Category("错误记录"), SuperDisplay(Name = "最后错误信息")]
        public string LastErrorMessage
        {
            get => _lastErrorMessage;
            set => SetProperty(ref _lastErrorMessage, value);
        }

        [Category("连接信息"), SuperDisplay(Name = "连接建立时间")]
        public DateTime ConnectedTime
        {
            get => _connectedTime;
            set => SetProperty(ref _connectedTime, value);
        }

        [Browsable(false)]
        public TimeSpan ConnectionDuration => DateTime.Now - ConnectedTime;

        [Category("状态监控"), SuperDisplay(Name = "最后成功读取")]
        public DateTime LastSuccessfulReadTime
        {
            get => _lastSuccessfulReadTime;
            set => SetProperty(ref _lastSuccessfulReadTime, value);
        }

        [Category("状态监控"), SuperDisplay(Name = "最后成功写入")]
        public DateTime LastSuccessfulWriteTime
        {
            get => _lastSuccessfulWriteTime;
            set => SetProperty(ref _lastSuccessfulWriteTime, value);
        }

        [Category("状态监控"), SuperDisplay(Name = "连续失败次数")]
        public int ConsecutiveFailures
        {
            get => _consecutiveFailures;
            set => SetProperty(ref _consecutiveFailures, value);
        }

        [Browsable(false)]
        public bool IsHealthy => ConsecutiveFailures < 3;

        public void RecordReadSuccess(double responseTimeMs)
        {
            TotalReads++;
            SuccessReads++;
            LastReadTime = DateTime.Now;
            LastSuccessfulReadTime = DateTime.Now;
            ConsecutiveFailures = 0;

            AverageReadTimeMs = (AverageReadTimeMs * (TotalReads - 1) + responseTimeMs) / TotalReads;
            if (responseTimeMs > MaxReadTimeMs) MaxReadTimeMs = responseTimeMs;
        }

        public void RecordReadFailure(string errorMessage)
        {
            TotalReads++;
            FailedReads++;
            LastReadTime = DateTime.Now;
            LastErrorTime = DateTime.Now;
            LastErrorMessage = errorMessage;
            ConsecutiveFailures++;
        }

        public void RecordWriteSuccess(double responseTimeMs)
        {
            TotalWrites++;
            SuccessWrites++;
            LastWriteTime = DateTime.Now;
            LastSuccessfulWriteTime = DateTime.Now;

            AverageWriteTimeMs = (AverageWriteTimeMs * (TotalWrites - 1) + responseTimeMs) / TotalWrites;
            if (responseTimeMs > MaxWriteTimeMs) MaxWriteTimeMs = responseTimeMs;
        }

        public void RecordWriteFailure(string errorMessage)
        {
            TotalWrites++;
            FailedWrites++;
            LastWriteTime = DateTime.Now;
            LastErrorTime = DateTime.Now;
            LastErrorMessage = errorMessage;
        }

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
    }
}
