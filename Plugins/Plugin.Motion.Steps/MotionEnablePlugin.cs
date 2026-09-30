using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Core.Interfaces;

namespace Plugin.Motion.Steps
{
    /// <summary>
    /// 轴使能 / 失能。
    ///
    /// 为什么单独一步而不是让「轴运动」自动使能：
    /// 伺服上电是有机械后果的动作（力矩一下加上去、制动器打开），工业现场的上电顺序通常有讲究
    /// （先回零/先确认安全，再使能）。把它做成显式步骤，用户才能表达自己的上电时序；
    /// 而"运动前忘了使能"会在运动步骤里得到一句明确的报错（"轴未使能"），不会静默不动。
    ///
    /// 本步骤**会等待**：使能失败必须当场知道（否则后面每一步都会以"轴未使能"失败，原因却被淹没了）。
    /// </summary>
    [Display(
        Name = "轴使能",
        GroupName = "运动控制",
        Description = "给轴上电（使能）或断电（失能）。伺服上电有机械后果，建议按现场的上电顺序显式放置本步骤",
        ShortName = "\uf011")]
    public class MotionEnablePlugin : VisionPluginBase
    {


        /// <summary>（端口）逻辑轴名：可被上游链接；不链接用手填值；都为空则回落到上面的常量</summary>
        public InputPort<string> Axis { get; } = new InputPort<string>("Axis", "X", "逻辑轴名（可链接；不链接则用轴名常量）")
        {
            IsRequired = false,        // 有默认值即可运行，不因为"没连上游"被判编译失败
            IsFunctionalEnum = true,
            OptionKind = StepConfigOptionKind.MotionAxisName,
            PresetOptions = StepConfigOptionSource.GetOptions(StepConfigOptionKind.MotionAxisName).ToList(),
        };

        /// <summary>
        /// true=使能（上电），false=失能（断电）。
        ///
        /// 【为什么是输入端口】本插件没有自定义配置视图，宿主对这类插件统一打开
        /// 「变量绑定」窗口，而那个窗口只列输入端口 —— 写成 [StepConfig] 就没有编辑入口。
        /// 布尔型端口在窗口里用常量框填 true / false（窗口下方有提示），
        /// 也可以由上游变量决定（例如按配方切换"是否带使能"）。
        /// </summary>
        public InputPort<bool> Enable { get; } = new InputPort<bool>("Enable", true, "true=使能（上电）/ false=失能（断电）");

        /// <summary>等待超时（ms）。同样必须是端口，理由见 <see cref="Enable"/></summary>
        public InputPort<int> TimeoutMs { get; } = new InputPort<int>("Timeout", 10_000, "等待超时(ms)");

        /// <summary>操作后的使能状态</summary>
        public OutputPort<bool> Enabled { get; } = new OutputPort<bool>("Enabled", "操作后的使能状态");

        public override void RunAlgorithm(IExecutionContext context)
        {
            var axisName = Axis.ActualValue ?? string.Empty;

            // 端口的实际生效值在"读的这一刻"确定（上游链接值 > 界面手填值）。
            // 取出后存进局部变量：整个步骤内用同一个值，避免中途有人改了常量框
            // 导致"前半句按使能、后半句按失能"这种自相矛盾的日志。
            var enable = Enable.ActualValue;
            var timeout = Math.Max(500, TimeoutMs.ActualValue);

            Enabled.Value = false;

            if (!MotionAxisResolution.TryResolveAxis(axisName, out var device, out var mapping, out var resolveError))
            {
                Fail(resolveError);
                return;
            }

            // 使能/失能在"报警态"下也允许（清报警前往往需要先失能），但仍要求已连接
            if (device.State == MotionCardState.Closed || device.State == MotionCardState.SafeStopped)
            {
                Fail($"运动卡「{device.Descriptor.Caption}」当前不可操作：{device.StateDetail}");
                return;
            }

            using var command = new MotionCommand
            {
                Kind = enable ? MotionCommandKind.Enable : MotionCommandKind.Disable,
                PhysicalAxis = mapping.PhysicalIndex,
                LogicalAxis = mapping.LogicalName,
                WaitsForCompletion = true,
                Timeout = TimeSpan.FromMilliseconds(timeout),
            };

            var result = device.Enqueue(command);
            if (result != MotionCommandResult.Accepted)
            {
                var suggestion = device.LastFault?.Suggestion;
                Fail($"{(enable ? "使能" : "失能")}命令被拒绝（{result}）"
                     + (string.IsNullOrWhiteSpace(suggestion) ? string.Empty : $"：{suggestion}"));
                return;
            }

            if (!command.Completion.Wait(timeout + 500))
            {
                Fail($"轴「{axisName}」{(enable ? "使能" : "失能")}超时（>{timeout} ms）：请检查伺服供电与急停回路");
                return;
            }

            if (command.State != MotionCommandState.Done)
            {
                Fail($"轴「{axisName}」{(enable ? "使能" : "失能")}失败（{command.State}）：{command.Error}");
                return;
            }

            // 以设备回报的实际状态为准（卡可能拒绝或延迟生效），而不是"我发了使能所以它就该是使能"
            var status = device.GetAxisStatus(mapping.PhysicalIndex);
            Enabled.Value = status?.Enabled ?? enable;
            Success.Value = true;

            context.Logger.Info($"{InstanceName} 轴「{axisName}」{(enable ? "已使能" : "已失能")}（设备回报：{Enabled.Value}）");
        }

        public override void Initialize() { }

        public override void Dispose() { }
    }
}
