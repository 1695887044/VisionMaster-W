using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Core.Interfaces;

namespace Plugin.Motion.Steps
{
    /// <summary>
    /// 等待到位。
    ///
    /// 为什么它必须是一个**独立步骤**（而不是让「轴运动」自己等）：
    /// 运动是异步的。产线上大量场景要"两轴同时运动"，如果运动步骤内部阻塞等到位，
    /// 两条轴就只能串行——现场会发现"节拍莫名慢了一倍"，而且极难看出原因。
    /// 把"下发"与"等待"拆开，用户才能显式表达自己的意图。
    ///
    /// 等待的依据是**设备状态**（轴的 InPosition / Moving），不是"某条命令的完成信号"：
    /// 命令对象不跨步骤传递（那会让流程依赖对象身份，无法保存/重放），
    /// 而"这个轴现在到没到位"是设备上随时可查的事实。
    /// </summary>
    [Display(
        Name = "等待到位",
        GroupName = "运动控制",
        Description = "等待指定轴运动到目标位置（轮询设备状态，带超时与报警检查）",
        ShortName = "\uf017")]
    public class MotionWaitPlugin : VisionPluginBase
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

        /// <summary>等待超时（ms）。超时即判失败 —— "走不到"与"走得慢"必须能区分</summary>
        [StepConfig]
        public int TimeoutMs { get; set; } = 30_000;

        /// <summary>轮询间隔（ms）。太小会占满通信带宽（网口卡尤其明显）</summary>
        [StepConfig]
        public int PollIntervalMs { get; set; } = 20;

        /// <summary>等待结束时的实际位置（mm），供报表/下游步骤使用</summary>
        public OutputPort<double> Position { get; } = new OutputPort<double>("Position", "到位位置(mm)");

        /// <summary>等待耗时（ms）</summary>
        public OutputPort<int> ElapsedMs { get; } = new OutputPort<int>("Elapsed", "等待耗时(ms)");

        public override void RunAlgorithm(IExecutionContext context)
        {
            var cardKey = Card.ActualValue ?? string.Empty;
            var axisName = Axis.ActualValue ?? string.Empty;

            Position.Value = 0;
            ElapsedMs.Value = 0;

            if (!context.Motions.TryGetByKey(cardKey, out var device))
            {
                Fail($"找不到运动卡「{cardKey}」：请确认该卡已在「运动卡设置」里配置并连接");
                return;
            }

            var mapping = device.Descriptor.Axes.FirstOrDefault(a =>
                a.Enabled && string.Equals(a.LogicalName, axisName, StringComparison.OrdinalIgnoreCase));

            if (mapping == null)
            {
                Fail($"运动卡「{device.Descriptor.Caption}」上没有启用名为「{axisName}」的轴");
                return;
            }

            var timeout = Math.Max(1, TimeoutMs);
            var interval = Math.Clamp(PollIntervalMs, 1, 500);
            var watch = Stopwatch.StartNew();

            while (true)
            {
                // 取消优先：流程停止时立刻退出，不要等满超时
                if (context.CancellationToken.IsCancellationRequested)
                {
                    Fail($"等待轴「{axisName}」到位时被中断（流程已停止）");
                    return;
                }

                var status = device.GetAxisStatus(mapping.PhysicalIndex);
                if (status == null)
                {
                    Fail($"读不到轴「{axisName}」的状态：运动卡「{device.Descriptor.Caption}」可能已断开（{device.StateDetail}）");
                    return;
                }

                // 报警/限位优先于"到位"：轴在报警状态下可能恰好停在目标附近，
                // 若只看 InPosition 会把"撞停"当成"走到位"
                if (status.HasCriticalSignal)
                {
                    var fault = device.LastFault;
                    Fail($"等待轴「{axisName}」期间出现异常信号：{status.Describe()}"
                         + (string.IsNullOrWhiteSpace(fault?.Suggestion) ? string.Empty : $"（建议：{fault!.Suggestion}）"));
                    return;
                }

                if (status.InPosition && !status.Moving)
                {
                    Position.Value = status.PositionMm;
                    ElapsedMs.Value = (int)watch.ElapsedMilliseconds;
                    Success.Value = true;
                    context.Logger.Info($"{InstanceName} 轴「{axisName}」已到位：{status.PositionMm:F3} mm（耗时 {watch.ElapsedMilliseconds} ms）");
                    return;
                }

                if (watch.ElapsedMilliseconds > timeout)
                {
                    Position.Value = status.PositionMm;
                    ElapsedMs.Value = (int)watch.ElapsedMilliseconds;
                    Fail($"等待轴「{axisName}」到位超时（>{timeout} ms，当前位置 {status.PositionMm:F3} mm）："
                         + "请检查目标是否超出行程、速度是否过低，或该轴是否被限位/报警阻挡");
                    return;
                }

                try { context.CancellationToken.WaitHandle.WaitOne(interval); }
                catch (ObjectDisposedException) { /* 令牌已释放：下一轮循环会因取消而退出 */ }
            }
        }

        public override void Initialize() { }

        public override void Dispose() { }
    }
}
