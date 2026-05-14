using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin.Delay.SystemTime
{
    [Display(
    Name = "SystemTime",
    GroupName = "时间日期",
    Description = "获取当前系统时间的各个分量及完整时间对象",
    ShortName = "\uf017"
)]
    public class SystemTimePlugin : VisionPluginBase
    {
        public InputPort<bool> UseUtcTime { get; } = new InputPort<bool>("Use UTC", false, "是否使用UTC时间（否则使用本地时间）");

        public OutputPort<DateTime> CurrentTime { get; } = new OutputPort<DateTime>("Current Time", "完整的当前时间对象");
        public OutputPort<int> Year { get; } = new OutputPort<int>("Year", "年");
        public OutputPort<int> Month { get; } = new OutputPort<int>("Month", "月");
        public OutputPort<int> Day { get; } = new OutputPort<int>("Day", "日");
        public OutputPort<int> Hour { get; } = new OutputPort<int>("Hour", "时（24小时制）");
        public OutputPort<int> Minute { get; } = new OutputPort<int>("Minute", "分");
        public OutputPort<int> Second { get; } = new OutputPort<int>("Second", "秒");
        public OutputPort<int> Millisecond { get; } = new OutputPort<int>("Millisecond", "毫秒");
        public OutputPort<long> UnixTimestampSeconds { get; } = new OutputPort<long>("Unix Timestamp (s)", "Unix时间戳（秒）");
        public OutputPort<long> UnixTimestampMilliseconds { get; } = new OutputPort<long>("Unix Timestamp (ms)", "Unix时间戳（毫秒）");
        public OutputPort<string> Iso8601String { get; } = new OutputPort<string>("ISO 8601", "ISO 8601标准时间字符串");

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

            // 计算Unix时间戳
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan timeSpan = now.ToUniversalTime() - epoch;
            UnixTimestampSeconds.Value = (long)timeSpan.TotalSeconds;
            UnixTimestampMilliseconds.Value = (long)timeSpan.TotalMilliseconds;

            // ISO 8601格式
            Iso8601String.Value = now.ToString("o");

            context.Logger.Info($"{InstanceName} 获取时间: {now:yyyy-MM-dd HH:mm:ss.fff}");
        }

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
