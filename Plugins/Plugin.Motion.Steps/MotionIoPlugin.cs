using System;
using System.ComponentModel.DataAnnotations;
using Core.Interfaces;

namespace Plugin.Motion.Steps
{
    /// <summary>
    /// 轴卡 IO（读输入 / 写输出）。
    ///
    /// 为什么归在"运动控制"而不是"通讯"：这些点位是**运动卡上的** IO
    /// （气缸、夹爪、传感器、限位），与轴的动作在同一个时序里，走同一条卡通信；
    /// 拆到通讯侧会出现"同一台设备的状态被两条链路读写"的麻烦。
    ///
    /// 读的是轮询快照（不发起通信），写走命令队列 —— 与轴命令同一条队伍，
    /// 因此"先动轴再夹紧"这类顺序关系天然成立。
    /// </summary>
    [Display(
        Name = "轴卡 IO",
        GroupName = "运动控制",
        Description = "读写运动卡上的数字输入/输出（气缸、夹爪、传感器等）；写输出按命令队列顺序执行",
        ShortName = "\uf0e7")]
    public class MotionIoPlugin : VisionPluginBase
    {


        /// <summary>（端口）运动卡地址：可被上游链接；不链接用手填值；都为空则回落到上面的常量</summary>
        public InputPort<string> Card { get; } = new InputPort<string>("Card", string.Empty, "运动卡地址（可链接；不链接则用卡地址常量）")
        {
            IsRequired = false,
            IsFunctionalEnum = true,
            OptionKind = StepConfigOptionKind.MotionCardAddress,
            PresetOptions = StepConfigOptionSource.GetOptions(StepConfigOptionKind.MotionCardAddress).ToList(),
        };

        /// <summary>操作方向：写输出 / 读输入</summary>
        [StepConfig]
        public MotionIoDirection Direction { get; set; } = MotionIoDirection.WriteOutput;

        /// <summary>
        /// 点号（0 基，与卡接线对应）。
        ///
        /// 必须是输入端口而不是 [StepConfig]：本插件没有自定义配置视图，
        /// 属性面板统一走「变量绑定」窗口而它只列输入端口 ——
        /// 做成 [StepConfig] 在界面上没有任何入口（见 MotionMovePlugin 同款说明）。
        /// </summary>
        public InputPort<int> IoPort { get; } = new InputPort<int>("IoPort", 0, "点号（0 基，与卡接线对应）")
        {
            IsRequired = false,   // 有默认值 0，不因"没连上游"被判编译失败
        };

        /// <summary>
        /// 写入的值（仅"写输出"用；用输入端口便于被变量驱动，如"夹紧=真"）。
        /// 属性名刻意不叫 Value：那个名字会与基类上的成员撞车，解析到的不是这个端口。
        /// </summary>
        public InputPort<bool> WriteValue { get; } = new InputPort<bool>("Value", true, "写入值（仅写输出用）");

        /// <summary>写入命令是否被接受（仅"写输出"用）</summary>
        public OutputPort<bool> Accepted { get; } = new OutputPort<bool>("Accepted", "写入命令是否被接受");

        /// <summary>读到的输入值（仅"读输入"用）</summary>
        public OutputPort<bool> InputValue { get; } = new OutputPort<bool>("InputValue", "读到的输入状态");

        public override void RunAlgorithm(IExecutionContext context)
        {
            var cardKey = Card.ActualValue ?? string.Empty;

            Accepted.Value = false;
            InputValue.Value = false;

            // IO 是卡级操作（读写的是卡上的端子，不属于任何一根轴），必须指定卡
            if (!MotionAxisResolution.TryResolveDevice(cardKey, out var device, out var resolveError))
            {
                Fail(resolveError);
                return;
            }

            var ioPort = IoPort.GetTypedValue();

            if (Direction == MotionIoDirection.ReadInput)
            {
                if (!device.Capabilities.IsInputValid(ioPort))
                {
                    var hint = device.Capabilities.DigitalInputCount <= 0
                        ? "（该卡尚未上报能力，请确认已连接）"
                        : $"（本卡输入点范围 0~{device.Capabilities.DigitalInputCount - 1}）";
                    Fail($"输入点 {ioPort} 无效{hint}");
                    return;
                }

                // 注意：OutputPort<T>.Value 是 object（弱类型入口），不能直接参与判断 ——
                // 取值先落局部变量，再用它做日志与后续判断，语义也更清楚
                var readValue = device.ReadInput(ioPort);
                InputValue.Value = readValue;
                Success.Value = true;
                context.Logger.Info($"{InstanceName} 读输入 {ioPort} = {(readValue ? "ON" : "OFF")}");
                return;
            }

            // 写输出：与轴命令同队列，顺序关系天然成立
            if (device.State != MotionCardState.Online && device.State != MotionCardState.Alarm)
            {
                Fail($"运动卡「{device.Descriptor.Caption}」当前不可下发输出：{device.StateDetail}");
                return;
            }

            using var command = new MotionCommand
            {
                Kind = MotionCommandKind.SetOutput,
                IoPort = ioPort,
                IoValue = WriteValue.GetTypedValue(),
                WaitsForCompletion = false,
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            };

            var result = device.Enqueue(command);
            if (result != MotionCommandResult.Accepted)
            {
                Fail($"写输出命令被拒绝（{result}）：{device.LastFault?.Suggestion ?? device.StateDetail}");
                return;
            }

            Accepted.Value = true;
            Success.Value = true;
            context.Logger.Info($"{InstanceName} 已下发：{command.Describe()}");
        }

        public override void Initialize() { }

        public override void Dispose() { }
    }
}
