using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Core.Interfaces;

namespace Plugin.Motion.Steps
{
    /// <summary>
    /// 轴停止。
    ///
    /// 两种模式必须分清楚（这是安全相关的选择，不是口味问题）：
    ///   · <b>普通停止</b>：按减速曲线停下，对机械冲击小 —— 适用于"正常中断一次运动"。
    ///   · <b>急停</b>：清空队列 + 立即停全部轴 —— 适用于"出事了"。注意它**不是**安全回路，
    ///     真正的安全急停必须由硬件回路（急停按钮直接切伺服使能）保证，软件这条路只是补充。
    ///
    /// 本步骤**不等待**停止完成：停止本身就应该尽快返回，等它停稳是下一步的事
    /// （需要的话后面跟一个「读轴状态」或「等待到位」）。
    /// </summary>
    [Display(
        Name = "轴停止",
        GroupName = "运动控制",
        Description = "停止指定轴（或全部轴）。急停模式会清空待执行命令并立即停止，请勿替代硬件安全回路",
        ShortName = "\uf04d")]
    public class MotionStopPlugin : VisionPluginBase
    {



        /// <summary>（端口）逻辑轴名：可被上游链接。**留空 = 停所有卡的全部轴**</summary>
        public InputPort<string> Axis { get; } = new InputPort<string>("Axis", string.Empty, "逻辑轴名（留空 = 停全部轴）")
        {
            IsRequired = false,
            IsFunctionalEnum = true,
            OptionKind = StepConfigOptionKind.MotionAxisName,
            PresetOptions = StepConfigOptionSource.GetOptions(StepConfigOptionKind.MotionAxisName).ToList(),
        };

        /// <summary>停止方式：减速停（默认）/ 立即停</summary>
        [StepConfig]
        public MotionStopMode Mode { get; set; } = MotionStopMode.Decelerate;

        /// <summary>
        /// 是否走"急停"通道（清空待执行命令 + 停全部轴 + 立即停）。
        /// 勾上之后 AxisName 与 Mode 都被忽略 —— 急停的语义就是"全停下来，别排队"。
        /// </summary>
        [StepConfig]
        public bool UseEmergencyStop { get; set; }

        /// <summary>停止命令是否被接受</summary>
        public OutputPort<bool> Accepted { get; } = new OutputPort<bool>("Accepted", "停止命令是否被接受");

        public override void RunAlgorithm(IExecutionContext context)
        {
            var axisName = (Axis.ActualValue ?? string.Empty).Trim();

            Accepted.Value = false;

            // 两种形态，判据就是"轴名留不留空"：
            //   填了轴名 → 停这一根轴（按名解析出卡 + 轴号）；
            //   轴名留空 → 停**所有卡的全部轴**（PhysicalAxis = -1），
            //              这是产线急停/换型时真正要的那个动作，不需要指定卡。
            var stopAll = axisName.Length == 0;

            IReadOnlyList<IMotionDevice> targets;
            AxisMapping? mapping = null;

            if (stopAll)
            {
                if (!MotionAxisResolution.TryGetAllDevices(out targets, out var allError))
                {
                    Fail(allError);
                    return;
                }
            }
            else
            {
                if (!MotionAxisResolution.TryResolveAxis(axisName, out var device, out var resolved, out var axisError))
                {
                    Fail(axisError + "（若要停全部轴，请把轴名留空）");
                    return;
                }

                mapping = resolved;
                targets = new[] { device };
            }

            if (UseEmergencyStop)
            {
                foreach (var device in targets)
                {
                    var emergencyResult = device.EmergencyStop();
                    if (emergencyResult != MotionCommandResult.Accepted)
                    {
                        Fail($"对运动卡「{device.Descriptor.Caption}」的急停命令被拒绝（{emergencyResult}）：{device.StateDetail}");
                        return;
                    }

                    context.Logger.Warn($"{InstanceName} 已对运动卡「{device.Descriptor.Caption}」下发急停（清空待执行命令 + 立即停全部轴）");
                }

                Accepted.Value = true;
                Success.Value = true;
                return;
            }

            var physicalAxis = mapping?.PhysicalIndex ?? -1;   // -1 = 全部轴（契约约定）

            foreach (var device in targets)
            {
                using var command = new MotionCommand
                {
                    Kind = MotionCommandKind.Stop,
                    PhysicalAxis = physicalAxis,
                    LogicalAxis = stopAll ? "全部轴" : axisName,
                    StopMode = (int)Mode,
                    // 停止不等到位：等它停稳是后续步骤的事（这里等会把"停止"变成阻塞操作）
                    WaitsForCompletion = false,
                    Retryable = false,
                    Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
                };

                var result = device.Enqueue(command);
                if (result != MotionCommandResult.Accepted)
                {
                    Fail($"停止命令被拒绝（{result}）：{device.LastFault?.Suggestion ?? device.StateDetail}");
                    return;
                }

                context.Logger.Info($"{InstanceName} 已下发：{command.Describe()}");
            }

            Accepted.Value = true;
            Success.Value = true;
        }

        public override void Initialize() { }

        public override void Dispose() { }
    }
}
