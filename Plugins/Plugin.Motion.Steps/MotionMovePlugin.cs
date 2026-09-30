using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Core.Interfaces;

namespace Plugin.Motion.Steps
{
    /// <summary>
    /// 轴运动（点位运动）。
    ///
    /// 【本步骤最关键的一条语义：它只**下发**，不等到位】
    /// 运动是异步的：`Enqueue` 返回 Accepted 只代表"命令被收进队列了"。
    /// 要等轴真的走到位，必须再放一个「等待到位」步骤 —— 这不是设计缺陷，而是必然：
    /// 产线上大量场景需要"两轴同时运动"，如果 Move 内部阻塞等到位，
    /// 两条轴就只能串行跑（而且现场很难看出为什么慢了一倍）。
    ///
    /// 目标位置与速度是**输入端口**（不是配置项），因为它常常来自上游：
    /// 视觉定位给出坐标 → 这一步接过去 → 轴走过去。用配置项就绑不了变量，视觉引导就无从落地。
    /// </summary>
    [Display(
        Name = "轴运动",
        GroupName = "运动控制",
        Description = "让某个轴走到目标位置（只下发命令，不等待到位；需要等待请加「等待到位」步骤）",
        ShortName = "\uf0d1")]
    public class MotionMovePlugin : VisionPluginBase
    {
        /// <summary>
        /// 目标运动卡（按地址寻址，如 192.168.0.11）。
        ///
        /// 【为什么是输入端口，而不是 [StepConfig] 常量】
        /// 本插件**没有自定义配置视图**。宿主对这类插件统一打开「变量绑定」窗口
        ///（ProcessViewModel 里"回退到通用 DataBindView"那条路径），
        /// 而那个窗口**只列输入端口** —— 做成 [StepConfig] 的参数在界面上**没有任何入口**。
        /// 端口一个入口办两件事：左列填常量、右列连上游变量；常量的灵活性一分不少，还多了可链接。
        /// 这也是其它无自定义视图插件的既有做法（延时 / 比较 / 换算 / 运算 …）。
        /// </summary>
        public InputPort<string> Card { get; } = new InputPort<string>("Card", string.Empty, "运动卡地址（可链接；也可直接填）")
        {
            IsRequired = false,        // 有默认值即可运行，不因为"没连上游"被判编译失败
            IsFunctionalEnum = true,   // 绑定界面显示为下拉（候选来自当前方案）
            OptionKind = StepConfigOptionKind.MotionCardAddress,
            PresetOptions = StepConfigOptionSource.GetOptions(StepConfigOptionKind.MotionCardAddress).ToList(),
        };

        /// <summary>
        /// 逻辑轴名（在运动卡设置里配置，如 "X"）。换卡/改接线只需改映射表，流程不动。
        /// 同样必须是端口，理由见 <see cref="Card"/>。
        /// </summary>
        public InputPort<string> Axis { get; } = new InputPort<string>("Axis", "X", "逻辑轴名（可链接；也可直接填）")
        {
            IsRequired = false,        // 有默认值即可运行，不因为"没连上游"被判编译失败
            IsFunctionalEnum = true,
            OptionKind = StepConfigOptionKind.MotionAxisName,
            PresetOptions = StepConfigOptionSource.GetOptions(StepConfigOptionKind.MotionAxisName).ToList(),
        };

        /// <summary>
        /// 运动方式：绝对（走到坐标点）/ 相对（再走一段）。
        ///
        /// 枚举型端口在绑定界面里自动呈现为下拉（IsFunctionalEnum 对枚举默认为 true，
        /// 候选由枚举成员生成），所以它既能在界面上改，也能被上游变量动态指定。
        /// 若仍写成 [StepConfig]，这个"绝对/相对"开关在界面上将无处可改 ——
        /// 而这恰恰是现场最常切的一个参数。
        /// </summary>
        public InputPort<MotionMoveMode> Mode { get; } = new InputPort<MotionMoveMode>("Mode", MotionMoveMode.Absolute, "运动方式（绝对 / 相对）");

        /// <summary>目标位置（mm）。绝对=目标点；相对=增量（可为负）</summary>
        public InputPort<double> Target { get; } = new InputPort<double>("Target", 0.0, "目标位置(mm)");

        /// <summary>速度（mm/s）。填 0 或用卡级默认值</summary>
        public InputPort<double> Velocity { get; } = new InputPort<double>("Velocity", 0.0, "速度(mm/s，0=用卡级默认)");

        /// <summary>命令是否被接受（收进队列）——注意它**不代表已到位**</summary>
        public OutputPort<bool> Accepted { get; } = new OutputPort<bool>("Accepted", "命令是否被接受（不代表已到位）");

        /// <summary>本步骤用到的逻辑轴（供下游「等待到位」步骤直接引用，避免手抄轴名抄错）</summary>
        public OutputPort<string> AxisUsed { get; } = new OutputPort<string>("Axis", "本次运动的逻辑轴名");

        public override void RunAlgorithm(IExecutionContext context)
        {
            Accepted.Value = false;

            // ★ 端口优先、常量兜底。
            //   [StepConfig] 常量**不可变量链接**，端口**可以** —— 两者并存才既不破坏老流程，
            //   又让"轴/卡由上游决定"（配方切换、上位机指定）成为可能。
            //   ActualValue 的取值优先级已经是"链接值 > 手填值"，这里只需在它为空白时回落到常量。
            // 取值一律走端口的实际生效值（优先级：上游链接值 > 界面手填值）。
            // 空合并只为挡住 null：手填框被清空时 ActualValue 可能是 null，
            // 与其在这里编一个默认值，不如让它空着走到下面的校验，由那里给出
            // "没有启用名为「」的轴" —— 现场据此就能反推是卡地址/轴名没填。
            var cardKey = Card.ActualValue ?? string.Empty;
            var axisName = Axis.ActualValue ?? string.Empty;

            AxisUsed.Value = axisName;

            // 一次调用拿到"设备 + 轴配置"：Card 端口留空即按轴名全局解析（新流程推荐）；
            // 填了地址则按老规则走，保证已保存流程的行为一个字节都不变。
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

            var target = Target.GetTypedValue();
            var command = new MotionCommand
            {
                Kind = Mode.ActualValue == MotionMoveMode.Relative
                    ? MotionCommandKind.MoveRelative
                    : MotionCommandKind.MoveAbsolute,
                PhysicalAxis = mapping.PhysicalIndex,
                LogicalAxis = mapping.LogicalName,
                TargetMm = target,
                VelocityMmPerS = Velocity.GetTypedValue(),
                // 点位运动下发即完成，超时只用于"写命令"这一步（真正的到位超时由等待步骤负责）
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            };

            var result = device.Enqueue(command);
            if (result != MotionCommandResult.Accepted)
            {
                var suggestion = device.LastFault?.Suggestion;
                Fail($"运动命令被拒绝（{result}）"
                     + (string.IsNullOrWhiteSpace(suggestion) ? string.Empty : $"：{suggestion}"));
                return;
            }

            Accepted.Value = true;
            Success.Value = true;

            // 日志里明确写清"未等到位" —— 这是最容易误解的一步，宁可啰嗦
            context.Logger.Info(
                $"{InstanceName} 已下发：{command.Describe()}（未等到位；如需等待请在其后加「等待到位」步骤）");
        }

        public override void Initialize() { }

        public override void Dispose() { }
    }
}
