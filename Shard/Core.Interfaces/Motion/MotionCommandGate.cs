using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 一道闸门的结果。
    /// </summary>
    public readonly struct MotionGateResult
    {
        private MotionGateResult(bool passed, string reason, bool silent)
        {
            Passed = passed;
            Reason = reason;
            Silent = silent;
        }

        /// <summary>是否放行</summary>
        public bool Passed { get; }

        /// <summary>拒绝原因（已是可直接上 toast 的中文文案；放行时为空串）</summary>
        public string Reason { get; }

        /// <summary>
        /// 是否"静默拒绝"。
        ///
        /// 规格里"非空闲时再次按下点动 → 静默忽略"这一类拒绝不弹提示：
        /// 按住期间界面会重复脉冲，弹 toast 会刷屏，而用户什么都没做错。
        /// </summary>
        public bool Silent { get; }

        /// <summary>放行</summary>
        public static MotionGateResult Ok { get; } = new(true, string.Empty, false);

        /// <summary>拒绝</summary>
        public static MotionGateResult Reject(string reason, bool silent = false)
            => new(false, reason ?? string.Empty, silent);
    }

    /// <summary>
    /// 运动命令的**唯一**闸门实现。
    ///
    /// 为什么必须有它：同一个动作（走此点 / 定位 / 点动）在三个 VM 里各写了一遍判定，
    /// 文案已经分叉（一处说"运动卡未连接"、另一处说"找不到运动卡…"），
    /// 而且三处都没校验软限位 —— 软限位配了却不拦，等于这道防线不存在。
    /// 门禁散落在各处，必然是"加一条闸只加了一个界面"的局面。
    ///
    /// 抽成静态纯函数还有第二个理由：<b>必须能被无 WPF、无设备的测试直接断言</b>
    /// （与 <c>MotionDebugViewModel.IsJogPulseTimedOut</c> 当初被抽出来的理由相同）。
    ///
    /// 闸门顺序逐字按规格 4.2：未连接 → 目标非法 → 超软限位 → 未使能 → 未回零 → 忙。
    /// </summary>
    public static class MotionCommandGate
    {
        /// <summary>卡未连接（设备实例不存在，或状态不是 Online）</summary>
        public const string NotConnected = "运动卡未连接";

        /// <summary>目标坐标不是有限数字</summary>
        public const string InvalidTarget = "目标坐标不是有效数字";

        /// <summary>选中的轴为空（界面未选轴）</summary>
        public const string NoAxis = "请先在左侧选择一根轴";

        /// <summary>设备上有没有"能动"的前提：实例存在且状态为 Online</summary>
        public static MotionGateResult CheckDevice(IMotionDevice? device)
        {
            if (device == null) return MotionGateResult.Reject(NotConnected);

            // 报警 / 安全停机 / 连接中都不接受命令 —— 状态机的语义就是"能不能动"，
            // 这里不另发明判断（每个调用点各写一遍状态枚举必然漏项）
            return device.State == MotionCardState.Online
                ? MotionGateResult.Ok
                : MotionGateResult.Reject(NotConnected);
        }

        /// <summary>
        /// 停止 / 失能这类"让设备停下"的安全动作闸门。
        ///
        /// 与 <see cref="CheckDevice"/> 的唯一差别：<b>报警态与安全停机态也放行</b>。
        /// 报警态下轴可能还在走，这正是最需要把停止发下去的时刻；挡在门禁后面
        /// 等于"越危险越不发"。安全停机态已经由基类做过一次安全停轴，但操作员
        /// 仍可能想再停一次（比如卡刚恢复通信），允许它比拒绝更符合直觉。
        ///
        /// 未连接 / 正在连接仍然拒绝：这两种状态下命令线程不在跑，收下的命令
        /// 只会躺在队列里，反而会制造"已经发了停止"的假象。
        /// </summary>
        public static MotionGateResult CheckSafetyAction(IMotionDevice? device)
        {
            if (device == null) return MotionGateResult.Reject(NotConnected);

            return device.State switch
            {
                MotionCardState.Online => MotionGateResult.Ok,
                MotionCardState.Alarm => MotionGateResult.Ok,
                MotionCardState.SafeStopped => MotionGateResult.Ok,
                MotionCardState.Connecting => MotionGateResult.Reject("运动卡正在连接/断开，请稍候"),
                _ => MotionGateResult.Reject(NotConnected),
            };
        }

        /// <summary>
        /// 使能 / 失能的闸门（两条口径必须分家，见 <see cref="CheckSafetyAction"/>）：
        ///   · 使能 = 给伺服上电，有机械后果 → 只认 Online；报警态明确提示"先清报警"；
        ///   · 失能 = 撤掉力矩，是安全动作 → 与停止同一条闸门。
        /// </summary>
        public static MotionGateResult CheckEnable(IMotionDevice? device, bool enable)
        {
            if (!enable) return CheckSafetyAction(device);

            if (device == null) return MotionGateResult.Reject(NotConnected);

            return device.State switch
            {
                MotionCardState.Online => MotionGateResult.Ok,
                MotionCardState.Alarm => MotionGateResult.Reject("运动卡处于报警状态，请先清除报警，再使能"),
                MotionCardState.SafeStopped => MotionGateResult.Reject("运动卡已安全停机，请重新连接后再使能"),
                MotionCardState.Connecting => MotionGateResult.Reject("运动卡正在连接/断开，请稍候"),
                _ => MotionGateResult.Reject(NotConnected),
            };
        }

        /// <summary>
        /// 定位类命令（绝对定位 / 走此点）的闸门。
        /// </summary>
        /// <param name="device">目标设备（null = 未连接）</param>
        /// <param name="axis">目标轴的映射（null = 界面未选轴）</param>
        /// <param name="status">该轴的状态快照（null = 读不到，即未连接）</param>
        /// <param name="targetMm">目标位置（mm）</param>
        /// <param name="movingMode">该轴"正在做什么"（点动/回零/定位）；为空则显示"运动"</param>
        public static MotionGateResult CheckMove(
            IMotionDevice? device,
            AxisMapping? axis,
            AxisStatus? status,
            double targetMm,
            string? movingMode = null)
        {
            var deviceGate = CheckDevice(device);
            if (!deviceGate.Passed) return deviceGate;

            if (axis == null) return MotionGateResult.Reject(NoAxis);

            var name = DisplayName(axis);

            if (double.IsNaN(targetMm) || double.IsInfinity(targetMm))
                return MotionGateResult.Reject(InvalidTarget);

            // 软限位是软件层的最后一道防线：配置界面能填，运动路径就必须拦。
            // 放在"未使能"之前：目标本身不合法时，报"没使能"会把真正的问题盖掉。
            if (!axis.IsWithinSoftLimit(targetMm))
                return MotionGateResult.Reject(
                    $"{name} 目标 {targetMm:F3} mm 超出软限位"
                    + $"（{axis.SoftLimitMinMm:F3} ~ {axis.SoftLimitMaxMm:F3} mm）");

            if (status == null) return MotionGateResult.Reject(NotConnected);

            if (!status.Enabled)
                return MotionGateResult.Reject($"{name} 未使能，请先伺服使能");

            // 位置是否可信 = 已回零，**或**这台卡 / 这根轴配了绝对编码器（掉电不丢位置）。
            //
            // 为什么必须与设备层 MotionDeviceBase.CheckGateLocked 逐字同口径（用户裁决 2026-10-22 B2）：
            // 这一层是界面与 SCADA 动作表的**预检**，设备层是**硬门禁**。两处判据不一致时，
            // 配了绝对编码器的轴会出现「界面/SCADA 拒绝、设备层放行」——操作员看到"需要回零"，
            // 而其实根本不用回零，现场就会去反复回零、甚至改流程绕开，真正的问题永远查不出来。
            //
            // 逃生口两级，与设备层一一对应：
            //   · 卡级 SupportsAbsoluteEncoder —— 整卡都是绝对式（混合编码器机台不用这个）；
            //   · 每轴 HasAbsoluteEncoder      —— 只这一根轴是绝对式（默认 false，需显式开启）。
            var positionTrusted = device!.IsHomed
                                  || device.Capabilities.SupportsAbsoluteEncoder
                                  || axis.HasAbsoluteEncoder;

            if (!positionTrusted)
                return MotionGateResult.Reject($"{name} 需要回零后才能运动");

            if (status.Moving)
                return MotionGateResult.Reject($"{name} 正在{ModeOf(movingMode)}，请等待完成");

            return MotionGateResult.Ok;
        }

        /// <summary>
        /// 点动的闸门。与定位同一套，差别只有两处：
        ///   ① 点动没有目标位置，跳过"目标非法 / 软限位"两道；
        ///   ② 忙时是**静默**忽略（规格闸门表：非空闲 → 静默），因为按住期间界面会重复脉冲。
        /// </summary>
        public static MotionGateResult CheckJog(
            IMotionDevice? device,
            AxisMapping? axis,
            AxisStatus? status,
            bool supportsJog)
        {
            var deviceGate = CheckDevice(device);
            if (!deviceGate.Passed) return deviceGate;

            if (axis == null) return MotionGateResult.Reject(NoAxis);

            var name = DisplayName(axis);

            if (!supportsJog)
                return MotionGateResult.Reject("该卡未上报点动能力，请改用「定位」逐点对位");

            if (status == null) return MotionGateResult.Reject(NotConnected);

            if (!status.Enabled)
                return MotionGateResult.Reject($"{name} 未使能，请先伺服使能");

            if (!device!.IsHomed)
                return MotionGateResult.Reject($"{name} 需要回零后才能运动");

            if (status.Moving)
                return MotionGateResult.Reject($"{name} 正在运动，请等待完成", silent: true);

            return MotionGateResult.Ok;
        }

        /// <summary>回零的闸门：只挡"未连接 / 未选轴 / 忙"（回零本身就是为了解决未回零）</summary>
        public static MotionGateResult CheckHome(
            IMotionDevice? device,
            AxisMapping? axis,
            AxisStatus? status,
            string? movingMode = null)
        {
            var deviceGate = CheckDevice(device);
            if (!deviceGate.Passed) return deviceGate;

            if (axis == null) return MotionGateResult.Reject(NoAxis);

            if (status is { Moving: true })
                return MotionGateResult.Reject(
                    $"{DisplayName(axis)} 正在{ModeOf(movingMode)}，请等待完成");

            return MotionGateResult.Ok;
        }

        /// <summary>"正在{什么}"的缺省词</summary>
        public static string ModeOf(string? mode)
            => string.IsNullOrWhiteSpace(mode) ? "运动" : mode.Trim();

        /// <summary>轴显示名（逻辑名为空时退到物理轴号，与界面其它地方同一口径）</summary>
        public static string DisplayName(AxisMapping axis)
            => string.IsNullOrWhiteSpace(axis.LogicalName)
                ? $"轴{axis.PhysicalIndex}"
                : axis.LogicalName;

        /// <summary>目标是否有限数（NaN / ±∞ 都算非法）</summary>
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
