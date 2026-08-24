using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 系统时间插件
    /// 获取当前系统时间的各个分量及完整时间对象
    /// </summary>
    [Display(
        Name = "系统时间",
        GroupName = "时间日期",
        Description = "获取当前系统时间的各个分量及完整时间对象",
        ShortName = "\uf017"
    )]
    public class SystemTimePlugin : VisionPluginBase
    {
        /// <summary>
        /// 是否使用UTC时间输入端口
        /// True时使用UTC时间，False时使用本地时间
        /// </summary>
        public InputPort<bool> UseUtcTime { get; } = new InputPort<bool>("UseUTC", false, "是否使用UTC时间（否则使用本地时间）");

        /// <summary>
        /// 完整时间对象输出端口
        /// </summary>
        public OutputPort<DateTime> CurrentTime { get; } = new OutputPort<DateTime>("CurrentTime", "完整的当前时间对象");

        /// <summary>
        /// 年份输出端口
        /// </summary>
        public OutputPort<int> Year { get; } = new OutputPort<int>("Year", "年");

        /// <summary>
        /// 月份输出端口
        /// </summary>
        public OutputPort<int> Month { get; } = new OutputPort<int>("Month", "月");

        /// <summary>
        /// 日期输出端口
        /// </summary>
        public OutputPort<int> Day { get; } = new OutputPort<int>("Day", "日");

        /// <summary>
        /// 小时输出端口（24小时制）
        /// </summary>
        public OutputPort<int> Hour { get; } = new OutputPort<int>("Hour", "时（24小时制）");

        /// <summary>
        /// 分钟输出端口
        /// </summary>
        public OutputPort<int> Minute { get; } = new OutputPort<int>("Minute", "分");

        /// <summary>
        /// 秒输出端口
        /// </summary>
        public OutputPort<int> Second { get; } = new OutputPort<int>("Second", "秒");

        /// <summary>
        /// 毫秒输出端口
        /// </summary>
        public OutputPort<int> Millisecond { get; } = new OutputPort<int>("Millisecond", "毫秒");

        /// <summary>
        /// Unix时间戳输出端口（秒）
        /// </summary>
        public OutputPort<long> UnixTimestampSeconds { get; } = new OutputPort<long>("UnixTimestampSec", "Unix时间戳（秒）");

        /// <summary>
        /// Unix时间戳输出端口（毫秒）
        /// </summary>
        public OutputPort<long> UnixTimestampMilliseconds { get; } = new OutputPort<long>("UnixTimestampMs", "Unix时间戳（毫秒）");

        /// <summary>
        /// ISO 8601标准时间字符串输出端口
        /// </summary>
        public OutputPort<string> Iso8601String { get; } = new OutputPort<string>("ISO8601", "ISO 8601标准时间字符串");

        /// <summary>
        /// 执行时间获取算法
        /// </summary>
        /// <param name="context">执行上下文</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            DateTime now = UseUtcTime.GetTypedValue() ? DateTime.UtcNow : DateTime.Now;

            CurrentTime.Value = now;
            Year.Value = now.Year;
            Month.Value = now.Month;
            Day.Value = now.Day;
            Hour.Value = now.Hour;
            Minute.Value = now.Minute;
            Second.Value = now.Second;
            Millisecond.Value = now.Millisecond;

            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan timeSpan = now.ToUniversalTime() - epoch;
            UnixTimestampSeconds.Value = (long)timeSpan.TotalSeconds;
            UnixTimestampMilliseconds.Value = (long)timeSpan.TotalMilliseconds;

            Iso8601String.Value = now.ToString("o");

            context.Logger.Info($"{InstanceName} 获取时间: {now:yyyy-MM-dd HH:mm:ss.fff}");
        }

        /// <summary>
        /// 初始化插件
        /// </summary>
        public override void Initialize() { }

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose() { }
    }
}
