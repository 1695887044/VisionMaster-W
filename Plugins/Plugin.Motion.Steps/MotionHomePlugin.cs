using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Core.Interfaces;

namespace Plugin.Motion.Steps
{
    /// <summary>
    /// 轴回零。
    ///
    /// 三处与其它步骤不同的地方，都是回零这件事本身带来的：
    ///   1. <b>它会等待</b>（与「轴运动」相反）。回零是一个有明确终点的过程动作，
    ///      后面必须站在零点上才能继续，所以这一步天然是阻塞的。
    ///   2. <b>超时给得很大</b>（默认 120s）。回零要先找开关再找 Index，慢是正常的；
    ///      用点位运动的 30s 去等回零会频繁误判。
    ///   3. <b>不支持的回零方式要当场说清</b>。各家卡的差异就集中在这里，
    ///      能力协商不通过时给出"该卡支持哪些"，比让用户在界面上试一遍强。
    ///
    /// 何时必须回零：增量式编码器的卡在**每次上电/断线重连后**位置都不可信（能力位
    /// SupportsAbsoluteEncoder 为 false）。建议把本步骤放在流程首步。
    /// </summary>
    [Display(
        Name = "轴回零",
        GroupName = "运动控制",
        Description = "让轴回原点（阻塞到回零完成；增量式编码器在每次上电后都应先执行本步骤）",
        ShortName = "\uf015")]
    public class MotionHomePlugin : VisionPluginBase
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

        /// <summary>回零方式（各卡支持不同，能力协商不通过时会明确报错）</summary>
        [StepConfig]
        public HomeMode Mode { get; set; } = HomeMode.NegativeLimitIndex;

        /// <summary>回零超时（ms）。默认 120s —— 回零慢是正常的</summary>
        [StepConfig]
        public int TimeoutMs { get; set; } = 120_000;

        /// <summary>回零是否完成</summary>
        public OutputPort<bool> Homed { get; } = new OutputPort<bool>("Homed", "回零是否完成");

        public override void RunAlgorithm(IExecutionContext context)
        {
            var cardKey = Card.ActualValue ?? string.Empty;
            var axisName = Axis.ActualValue ?? string.Empty;

            Homed.Value = false;

            if (!MotionAxisResolution.TryResolveAxis(cardKey, axisName, out var device, out var mapping, out var resolveError))
            {
                Fail(resolveError);
                return;
            }

            if (device.State != MotionCardState.Online)
            {
                Fail($"运动卡「{device.Descriptor.Caption}」当前不可运动：{device.StateDetail}");
                return;
            }

            // 提前拦住"这张卡不支持这种回零方式"，并告诉用户它支持哪些
            if (!device.Capabilities.SupportsHomeMode(Mode))
            {
                var supported = device.Capabilities.SupportedHomeModes.Count == 0
                    ? "（该卡尚未上报能力，请确认已连接）"
                    : string.Join("、", device.Capabilities.SupportedHomeModes);
                Fail($"运动卡「{device.Descriptor.Caption}」不支持回零方式「{Mode}」，它支持的是：{supported}");
                return;
            }

            var timeout = Math.Max(1000, TimeoutMs);
            using var command = new MotionCommand
            {
                Kind = MotionCommandKind.Home,
                PhysicalAxis = mapping.PhysicalIndex,
                LogicalAxis = mapping.LogicalName,
                HomeMode = Mode,
                WaitsForCompletion = true,
                // 回零绝不允许盲目重试：中途失败可能正卡在原点开关上，再来一次可能撞机械原点
                Retryable = false,
                Timeout = TimeSpan.FromMilliseconds(timeout),
            };

            var result = device.Enqueue(command);
            if (result != MotionCommandResult.Accepted)
            {
                var suggestion = device.LastFault?.Suggestion;
                Fail($"回零命令被拒绝（{result}）"
                     + (string.IsNullOrWhiteSpace(suggestion) ? string.Empty : $"：{suggestion}"));
                return;
            }

            // 等到命令结束（驱动在 Home 上是 WaitsForCompletion 语义）。
            // 多给 20% 余量：命令内部的超时判据与这里的不必严丝合缝，先到者胜即可。
            var waitMs = timeout + 1000;
            if (!command.Completion.Wait(waitMs))
            {
                Fail($"轴「{axisName}」回零超时（>{timeout} ms）：请检查原点/限位开关接线、回零速度与回零方式是否正确");
                return;
            }

            switch (command.State)
            {
                case MotionCommandState.Done:
                    Homed.Value = true;
                    Success.Value = true;
                    context.Logger.Info($"{InstanceName} 轴「{axisName}」回零完成（方式 {Mode}）");
                    return;

                case MotionCommandState.Canceled:
                    Fail($"轴「{axisName}」回零被取消：{command.Error}");
                    return;

                default:
                    Fail($"轴「{axisName}」回零失败（{command.State}）：{command.Error}"
                         + (string.IsNullOrWhiteSpace(device.LastFault?.Suggestion)
                             ? string.Empty
                             : $"（建议：{device.LastFault!.Suggestion}）"));
                    return;
            }
        }

        public override void Initialize() { }

        public override void Dispose() { }
    }
}
