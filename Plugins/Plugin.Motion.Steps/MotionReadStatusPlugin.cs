using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Core.Interfaces;

namespace Plugin.Motion.Steps
{
    /// <summary>
    /// 读轴状态（运动控制的"反馈"入口）。
    ///
    /// 读的是**轮询快照**（不发起通信）：一次执行里读到的所有字段来自同一采样时刻，
    /// 互相自洽；而且不会因为流程跑得比通信快而把网口打满。
    ///
    /// 输出里刻意同时给"结构化字段"和"一句话描述"：
    ///   · 结构化字段（Position/Moving/Alarm…）供流程做判断与绑定变量；
    ///   · <see cref="StatusText"/> 供直接上屏、写日志、进报表 —— 现场排查时最需要的是一句人话。
    /// </summary>
    [Display(
        Name = "读轴状态",
        GroupName = "运动控制",
        Description = "读取指定轴的位置/速度/使能/限位/报警等状态（读轮询快照，不额外占用通信）",
        ShortName = "\uf201")]
    public class MotionReadStatusPlugin : VisionPluginBase
    {


        /// <summary>（端口）运动卡地址：可被上游链接；不链接用手填值；都为空则回落到上面的常量</summary>
        public InputPort<string> Card { get; } = new InputPort<string>("Card", string.Empty, "运动卡地址（可链接；不链接则用卡地址常量）")
        {
            IsRequired = false,
            IsFunctionalEnum = true,
            OptionKind = StepConfigOptionKind.MotionCardAddress,
            PresetOptions = StepConfigOptionSource.GetOptions(StepConfigOptionKind.MotionCardAddress).ToList(),
        };


        /// <summary>（端口）逻辑轴名：可被上游链接；不链接用手填值；都为空则回落到上面的常量</summary>
        public InputPort<string> Axis { get; } = new InputPort<string>("Axis", "X", "逻辑轴名（可链接；不链接则用轴名常量）")
        {
            IsRequired = false,
            IsFunctionalEnum = true,
            OptionKind = StepConfigOptionKind.MotionAxisName,
            PresetOptions = StepConfigOptionSource.GetOptions(StepConfigOptionKind.MotionAxisName).ToList(),
        };

        /// <summary>
        /// 报警时是否判本步骤失败。
        /// 默认 false：它是"读状态"，读到一个报警也是成功的读取 —— 由流程显式判断
        /// （把"读取失败"与"读到坏消息"混在一起，会让流程的异常处理变得没法写）。
        /// </summary>
        [StepConfig]
        public bool FailOnAlarm { get; set; }

        public OutputPort<double> Position { get; } = new OutputPort<double>("Position", "当前位置(mm)");
        public OutputPort<double> Velocity { get; } = new OutputPort<double>("Velocity", "当前速度(mm/s)");
        public OutputPort<bool> Moving { get; } = new OutputPort<bool>("Moving", "是否运动中");
        public OutputPort<bool> InPosition { get; } = new OutputPort<bool>("InPosition", "是否已到位");
        public OutputPort<bool> Enabled { get; } = new OutputPort<bool>("Enabled", "是否已使能");
        public OutputPort<bool> Alarm { get; } = new OutputPort<bool>("Alarm", "是否报警");
        public OutputPort<bool> PositiveLimit { get; } = new OutputPort<bool>("PositiveLimit", "正限位是否触发");
        public OutputPort<bool> NegativeLimit { get; } = new OutputPort<bool>("NegativeLimit", "负限位是否触发");
        public OutputPort<string> StatusText { get; } = new OutputPort<string>("Status", "状态一句话描述");

        public override void RunAlgorithm(IExecutionContext context)
        {
            var cardKey = Card.ActualValue ?? string.Empty;
            var axisName = Axis.ActualValue ?? string.Empty;

            Position.Value = 0;
            Velocity.Value = 0;
            Moving.Value = false;
            InPosition.Value = false;
            Enabled.Value = false;
            Alarm.Value = false;
            PositiveLimit.Value = false;
            NegativeLimit.Value = false;
            StatusText.Value = string.Empty;

            if (!MotionAxisResolution.TryResolveAxis(cardKey, axisName, out var device, out var mapping, out var resolveError))
            {
                Fail(resolveError);
                return;
            }

            var status = device.GetAxisStatus(mapping.PhysicalIndex);
            if (status == null)
            {
                // 读不到快照 = 还没轮询到（或刚断开）。这时给"未知"而不是编一个 0：
                // 把未知当成 0 位置，下游可能据此做出错误判断（且看不出是猜的）
                Fail($"读不到轴「{axisName}」的状态快照：运动卡「{device.Descriptor.Caption}」当前为 {device.State}（{device.StateDetail}）");
                return;
            }

            Position.Value = status.PositionMm;
            Velocity.Value = status.VelocityMmPerS;
            Moving.Value = status.Moving;
            InPosition.Value = status.InPosition;
            Enabled.Value = status.Enabled;
            Alarm.Value = status.Alarm;
            PositiveLimit.Value = status.PositiveLimit;
            NegativeLimit.Value = status.NegativeLimit;
            StatusText.Value = status.Describe();

            if (FailOnAlarm && status.HasCriticalSignal)
            {
                Fail($"轴「{axisName}」状态异常：{status.Describe()}"
                     + (string.IsNullOrWhiteSpace(device.LastFault?.Suggestion)
                         ? string.Empty
                         : $"（建议：{device.LastFault!.Suggestion}）"));
                return;
            }

            Success.Value = true;
            context.Logger.Info($"{InstanceName} 轴「{axisName}」：{status.Describe()}");
        }

        public override void Initialize() { }

        public override void Dispose() { }
    }
}
