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

            if (!device!.IsHomed)
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
