using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using Core.Interfaces;

namespace Plugin.Motion.Virtual
{
    /// <summary>
    /// 虚拟运动卡（仿真驱动）。
    ///
    /// 存在的理由与"网络相机（虚拟）"完全一样，但更迫切：
    ///   1. <b>没有硬件也能开发与回归</b>。运动控制的多数逻辑（命令排队、异步语义、
    ///      急停插队、看门狗停机、报警门禁）都发生在"基类 + 宿主"这一层，
    ///      与具体板卡无关 —— 没有虚拟卡，这些逻辑就只能等真卡到位才能验证。
    ///   2. <b>故障注入</b>。真卡上没法随便制造"掉线""伺服报警""轮询超时"，
    ///      而这些恰恰是最需要验证的路径（安全停机、错误分级、重连后要求回零）。
    ///      虚拟卡把它们做成开关（见下方 Simulate* 属性），冒烟测试直接开。
    ///
    /// 它刻意模拟"真实异步运动"而不是"瞬间到位"：
    ///   Move 命令**立即返回**，位置由轮询线程按速度逐拍推进，走完才置 InPosition。
    /// 这样才测得出"下发 ≠ 到位"这条最容易写错的语义 ——
    /// 若虚拟卡是瞬间到位，一切流程看起来都对，真机上却会出现"两条轴同时下发、实际串行"。
    /// </summary>
    [Display(
        Name = "虚拟运动卡（仿真）",
        GroupName = "运动卡",
        Description = "无硬件仿真：3 轴 + 8 输入 + 8 输出，支持模拟报警 / 通信失败 / 增量编码器",
        ShortName = "\uf085")]
    public sealed class VirtualMotionCard : MotionDeviceBase
    {
        /// <summary>一个仿真轴的全部可变状态</summary>
        private sealed class SimAxis
        {
            public int Index;
            public double PositionMm;
            public double TargetMm;
            public double VelocityMmPerS;
            public bool Enabled;
            public bool Alarm;
            public bool Homing;
            public long HomeDeadlineTick;
        }

        private readonly List<SimAxis> _axes = new();
        private bool[] _outputs = Array.Empty<bool>();
        private bool[] _inputs = Array.Empty<bool>();

        public VirtualMotionCard(MotionDescriptor descriptor) : base(descriptor) { }

        #region 仿真参数与故障注入开关（冒烟测试用；真机驱动没有这些）

        /// <summary>仿真轴数（连接时生效）</summary>
        public int SimulatedAxisCount { get; set; } = 3;

        /// <summary>仿真数字输入点数</summary>
        public int SimulatedInputCount { get; set; } = 8;

        /// <summary>仿真数字输出点数</summary>
        public int SimulatedOutputCount { get; set; } = 8;

        /// <summary>
        /// 仿真为绝对式编码器。
        /// true  = 掉电/失联后位置仍可信，重连不需回零；
        /// false = 增量式，失联后 <see cref="IMotionDevice.IsHomed"/> 会被置 false（用来验证那条恢复规则）。
        /// </summary>
        public bool SimulatedAbsoluteEncoder { get; set; } = true;

        /// <summary>回零耗时（ms，模拟机械找零）</summary>
        public int SimulatedHomeDurationMs { get; set; } = 200;

        /// <summary>故障注入：为 true 时所有轴都报"伺服报警"（验证报警门禁与清报警）</summary>
        public bool SimulateAlarm { get; set; }

        /// <summary>故障注入：为 true 时轮询抛异常（验证看门狗 → 安全停机）</summary>
        public bool SimulatePollException { get; set; }

        /// <summary>故障注入：为 true 时运动类命令强制失败（验证失败分级与队列清空）</summary>
        public bool SimulateCommandFailure { get; set; }

        /// <summary>把某个输入点置位（模拟传感器/限位信号）</summary>
        public void SetInput(int port, bool value)
        {
            if (port >= 0 && port < _inputs.Length) _inputs[port] = value;
        }

        /// <summary>读某轴当前仿真位置（冒烟断言用）</summary>
        public double GetSimulatedPosition(int index)
            => index >= 0 && index < _axes.Count ? _axes[index].PositionMm : double.NaN;

        /// <summary>把某轴直接摆到指定位置（造初始条件用，不走命令）</summary>
        public void SetSimulatedPosition(int index, double positionMm)
        {
            if (index < 0 || index >= _axes.Count) return;
            _axes[index].PositionMm = positionMm;
            _axes[index].TargetMm = positionMm;
        }

        #endregion

        #region 驱动实现

        protected override bool ConnectCore()
        {
            _axes.Clear();
            for (int i = 0; i < Math.Max(1, SimulatedAxisCount); i++)
            {
                _axes.Add(new SimAxis { Index = i, PositionMm = 0, TargetMm = 0, Enabled = false });
            }

            _outputs = new bool[Math.Max(1, SimulatedOutputCount)];
            _inputs = new bool[Math.Max(1, SimulatedInputCount)];

            SetCapabilities(new MotionCapabilities
            {
                AxisCount = _axes.Count,
                DigitalInputCount = _inputs.Length,
                DigitalOutputCount = _outputs.Length,
                SupportsHardLimit = true,
                SupportsAbsoluteEncoder = SimulatedAbsoluteEncoder,
                SupportsLineInterpolation = false,
                SupportsArcInterpolation = false,
                SupportsJog = true,
                SupportedHomeModes = new[]
                {
                    HomeMode.NegativeLimitIndex,
                    HomeMode.Origin,
                    HomeMode.PresetZero,
                },
            });

            // 绝对式：位置开箱即可信；增量式：必须先回零才有可信坐标系
            MarkHomed(SimulatedAbsoluteEncoder);

            SetDetail(SimulatedAbsoluteEncoder
                ? $"已连接虚拟卡（{_axes.Count} 轴，绝对编码器，位置可信）"
                : $"已连接虚拟卡（{_axes.Count} 轴，增量编码器，需先回零）");
            return true;
        }

        protected override void DisconnectCore()
        {
            _axes.Clear();
            _outputs = Array.Empty<bool>();
            _inputs = Array.Empty<bool>();
        }

        protected override void PollStatusCore(CancellationToken ct)
        {
            // 故障注入：模拟通信中断（基类的看门狗会据此判失联并安全停机）
            if (SimulatePollException)
                throw new InvalidOperationException("模拟：与运动卡的通信中断");

            var intervalMs = Math.Clamp(Descriptor.Params.PollIntervalMs, 1, 1000);
            var dtSec = intervalMs / 1000.0;
            var nowTick = Environment.TickCount64;

            foreach (var axis in _axes)
            {
                ct.ThrowIfCancellationRequested();

                var status = new AxisStatus
                {
                    PhysicalIndex = axis.Index,
                    Enabled = axis.Enabled,
                    Alarm = axis.Alarm || SimulateAlarm,
                    AlarmMessage = (axis.Alarm || SimulateAlarm) ? "模拟：伺服报警" : string.Empty,
                };

                if (axis.Homing)
                {
                    if (nowTick >= axis.HomeDeadlineTick)
                    {
                        axis.Homing = false;
                        axis.PositionMm = 0;
                        axis.TargetMm = 0;
                        status.InPosition = true;
                    }
                    else
                    {
                        status.Moving = true;
                    }
                }
                else
                {
                    var remain = axis.TargetMm - axis.PositionMm;
                    if (Math.Abs(remain) > 1e-9)
                    {
                        var step = axis.VelocityMmPerS * dtSec;
                        if (step <= 1e-9) step = Descriptor.Params.DefaultVelocityMmPerS * dtSec;

                        if (Math.Abs(remain) <= step) axis.PositionMm = axis.TargetMm;
                        else axis.PositionMm += Math.Sign(remain) * step;

                        var stillMoving = Math.Abs(axis.TargetMm - axis.PositionMm) > 1e-9;
                        status.Moving = stillMoving;
                        status.InPosition = !stillMoving;
                        status.VelocityMmPerS = stillMoving ? axis.VelocityMmPerS : 0;
                    }
                    else
                    {
                        status.InPosition = true;
                    }
                }

                status.PositionMm = axis.PositionMm;

                // 限位/急停信号也按位置模拟：软限位之外就算撞到硬限位（用来验证限位停机路径）
                var mapping = Descriptor.FindAxis(axis.Index);
                if (mapping != null && mapping.Enabled && !mapping.IsWithinSoftLimit(axis.PositionMm))
                {
                    status.PositiveLimit = axis.PositionMm > mapping.SoftLimitMaxMm;
                    status.NegativeLimit = axis.PositionMm < mapping.SoftLimitMinMm;
                }

                UpdateAxisStatus(status);
            }

            for (int i = 0; i < _inputs.Length; i++) UpdateInput(i, _inputs[i]);
        }

        protected override MotionCommandOutcome ExecuteCommandCore(
            MotionCommand command, CancellationToken ct, out string error)
        {
            error = string.Empty;

            if (SimulateCommandFailure &&
                command.Kind is MotionCommandKind.MoveAbsolute or MotionCommandKind.MoveRelative
                    or MotionCommandKind.Home)
            {
                error = "模拟：运动命令执行失败";
                return MotionCommandOutcome.Failed;
            }

            switch (command.Kind)
            {
                case MotionCommandKind.Enable:
                {
                    if (!TryGetSimAxis(command.PhysicalAxis, out var axis, out error)) return MotionCommandOutcome.Failed;
                    axis.Enabled = true;
                    return MotionCommandOutcome.Done;
                }

                case MotionCommandKind.Disable:
                {
                    if (!TryGetSimAxis(command.PhysicalAxis, out var axis, out error)) return MotionCommandOutcome.Failed;
                    axis.Enabled = false;
                    axis.TargetMm = axis.PositionMm;   // 失能即停（真机也如此）
                    return MotionCommandOutcome.Done;
                }

                case MotionCommandKind.MoveAbsolute:
                {
                    if (!TryGetSimAxis(command.PhysicalAxis, out var axis, out error)) return MotionCommandOutcome.Failed;
                    if (!axis.Enabled) { error = "轴未使能（请先执行「轴使能」步骤）"; return MotionCommandOutcome.Failed; }
                    if (axis.Alarm) { error = "轴处于报警状态，无法运动"; return MotionCommandOutcome.Failed; }

                    axis.TargetMm = command.TargetMm;
                    axis.VelocityMmPerS = command.VelocityMmPerS > 0
                        ? command.VelocityMmPerS
                        : Descriptor.Params.DefaultVelocityMmPerS;

                    // 关键：立即返回 —— 运动由轮询线程推进。这正是"下发 ≠ 到位"的真实语义。
                    return MotionCommandOutcome.Done;
                }

                case MotionCommandKind.MoveRelative:
                {
                    if (!TryGetSimAxis(command.PhysicalAxis, out var axis, out error)) return MotionCommandOutcome.Failed;
                    if (!axis.Enabled) { error = "轴未使能（请先执行「轴使能」步骤）"; return MotionCommandOutcome.Failed; }
                    if (axis.Alarm) { error = "轴处于报警状态，无法运动"; return MotionCommandOutcome.Failed; }

                    axis.TargetMm = axis.PositionMm + command.TargetMm;
                    axis.VelocityMmPerS = command.VelocityMmPerS > 0
                        ? command.VelocityMmPerS
                        : Descriptor.Params.DefaultVelocityMmPerS;
                    return MotionCommandOutcome.Done;
                }

                case MotionCommandKind.Jog:
                {
                    if (!TryGetSimAxis(command.PhysicalAxis, out var axis, out error)) return MotionCommandOutcome.Failed;
                    if (!axis.Enabled) { error = "轴未使能（请先执行「轴使能」步骤）"; return MotionCommandOutcome.Failed; }
                    if (axis.Alarm) { error = "轴处于报警状态，无法运动"; return MotionCommandOutcome.Failed; }

                    // 点动没有终点：把目标设成"很远处"，让轮询线程按速度一直推，直到 Stop 把目标撤回当前位置。
                    // 这同时把"点动撞限位"也模拟出来了 —— 位置越过软限位后，
                    // PollStatusCore 会置 PositiveLimit/NegativeLimit，基类据此安全停机（真机行为一致）。
                    axis.VelocityMmPerS = command.VelocityMmPerS > 0
                        ? command.VelocityMmPerS
                        : Descriptor.Params.DefaultVelocityMmPerS;
                    axis.TargetMm = command.JogDirection >= 0
                        ? axis.PositionMm + 1_000_000
                        : axis.PositionMm - 1_000_000;
                    return MotionCommandOutcome.Done;
                }

                case MotionCommandKind.Home:
                {
                    if (!TryGetSimAxis(command.PhysicalAxis, out var axis, out error)) return MotionCommandOutcome.Failed;
                    if (!axis.Enabled) { error = "轴未使能（回零前需先使能）"; return MotionCommandOutcome.Failed; }

                    axis.Homing = true;
                    axis.HomeDeadlineTick = Environment.TickCount64 + Math.Max(1, SimulatedHomeDurationMs);

                    // Home 是 WaitsForCompletion 命令：在这里等它做完（可被取消 / 超时）。
                    // 注意位置推进在轮询线程上，本循环只负责"等"，两条线程各司其职、无死锁。
                    var timeoutMs = command.Timeout > TimeSpan.Zero
                        ? (long)command.Timeout.TotalMilliseconds
                        : Descriptor.Params.CommandTimeoutMs;
                    var deadline = Environment.TickCount64 + Math.Max(1, timeoutMs);

                    while (axis.Homing)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            // 按契约：取消抛异常（而不是返回 Failed）。
                            // 基类据此把"被急停打断"与"真的回零失败"分开处理 —— 前者不报故障、不停机。
                            axis.Homing = false;
                            throw new OperationCanceledException(ct);
                        }
                        if (Environment.TickCount64 > deadline)
                        {
                            axis.Homing = false;
                            error = $"回零超时（>{timeoutMs}ms）";
                            return MotionCommandOutcome.TimedOut;
                        }
                        ct.WaitHandle.WaitOne(5);
                    }

                    axis.PositionMm = 0;
                    axis.TargetMm = 0;
                    return MotionCommandOutcome.Done;
                }

                case MotionCommandKind.Stop:
                {
                    // PhysicalAxis = -1 表示全部轴（基类急停走这条）
                    if (command.PhysicalAxis < 0)
                    {
                        foreach (var axis in _axes)
                        {
                            axis.TargetMm = axis.PositionMm;
                            axis.Homing = false;
                        }
                        return MotionCommandOutcome.Done;
                    }

                    if (!TryGetSimAxis(command.PhysicalAxis, out var single, out error)) return MotionCommandOutcome.Failed;
                    single.TargetMm = single.PositionMm;
                    single.Homing = false;
                    return MotionCommandOutcome.Done;
                }

                case MotionCommandKind.SetOutput:
                {
                    if (command.IoPort < 0 || command.IoPort >= _outputs.Length)
                    {
                        error = $"输出点 {command.IoPort} 超出范围（0~{_outputs.Length - 1}）";
                        return MotionCommandOutcome.Failed;
                    }
                    _outputs[command.IoPort] = command.IoValue;
                    return MotionCommandOutcome.Done;
                }

                case MotionCommandKind.ClearAlarm:
                {
                    // 清报警：把注入开关也一起复位，否则"清完立刻又报"
                    SimulateAlarm = false;
                    foreach (var axis in _axes) axis.Alarm = false;
                    return MotionCommandOutcome.Done;
                }

                default:
                    error = $"虚拟卡不支持命令：{command.Kind}";
                    return MotionCommandOutcome.NotSupported;
            }
        }

        protected override void SafeStopCore()
        {
            // 真机在这里要下发"停止全部轴"；虚拟卡只需把目标撤回当前位置
            foreach (var axis in _axes)
            {
                axis.TargetMm = axis.PositionMm;
                axis.Homing = false;
            }
        }

        protected override MotionFault ClassifyOutcome(
            MotionCommandOutcome outcome, MotionCommand command, string error)
        {
            // 虚拟卡的"轴未使能"是配置/流程问题，属于可恢复；其余沿用基类默认分级。
            // 真机驱动应在这里把厂商错误码映射成更准的级别与建议（见 MotionFault.Suggestion 的注释）。
            if (error.Contains("未使能"))
            {
                return new MotionFault
                {
                    Severity = MotionFaultSeverity.Recoverable,
                    Message = error,
                    Suggestion = "在流程里把「轴使能」步骤放在运动步骤之前",
                    Axis = command.LogicalAxis,
                    CommandId = command.CommandId,
                };
            }

            return base.ClassifyOutcome(outcome, command, error);
        }

        private bool TryGetSimAxis(int index, out SimAxis axis, out string error)
        {
            error = string.Empty;
            axis = null!;

            if (index < 0 || index >= _axes.Count)
            {
                error = $"轴号 {index} 超出仿真范围（0~{Math.Max(0, _axes.Count - 1)}）";
                return false;
            }

            axis = _axes[index];
            return true;
        }

        #endregion
    }
}
