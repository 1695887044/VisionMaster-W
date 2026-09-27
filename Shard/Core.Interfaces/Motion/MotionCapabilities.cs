using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Interfaces
{
    /// <summary>
    /// 运动卡能力描述（**能力协商**的载体）。
    ///
    /// 为什么要有它，而不是把接口做成"大而全 + 空实现"：
    /// 各家板卡差异巨大 —— 轴数（正运动 ECI3428 是 4 轴、ECI3828 是 8 轴）、IO 数量、
    /// 有没有硬限位、编码器是绝对还是增量（决定掉电后要不要回零）、能不能插补、回零支持哪些模式。
    /// 接口若按"最强卡"设计，弱卡就得写一堆 `NotSupported` 空实现，
    /// 而**使用方无从判断"某项功能这张卡到底有没有"** —— 只能运行到那一步才知道，现场代价太大。
    ///
    /// 有了能力描述，流程步骤与配置界面就能在**配置期**告警（"该卡不支持两轴直线插补"），
    /// 而不是等运行到那一句才失败。
    /// </summary>
    public sealed class MotionCapabilities
    {
        /// <summary>轴数（0 表示尚未连接、能力未知）</summary>
        public int AxisCount { get; init; }

        /// <summary>数字输入点数</summary>
        public int DigitalInputCount { get; init; }

        /// <summary>数字输出点数</summary>
        public int DigitalOutputCount { get; init; }

        /// <summary>
        /// 是否具备硬限位（限位开关接到卡上、由卡侧直接封锁运动）。
        /// 与"软件软限位"是两层防线，两者都要有；缺硬限位时软件层的责任更重，
        /// 配置界面应据此给出更醒目的提示。
        /// </summary>
        public bool SupportsHardLimit { get; init; }

        /// <summary>
        /// 是否使用绝对式编码器。
        /// true  = 掉电后位置不丢，重连后不需要回零；
        /// false = 增量式，掉电即丢位置，**重连后必须先回零**（否则坐标系是错的，
        ///         此时若允许直接 MoveAbs 会按错误的当前位置去算行程，可能直接撞限位）。
        /// 这个位决定断线恢复策略，是生命周期里最要紧的能力位。
        /// </summary>
        public bool SupportsAbsoluteEncoder { get; init; }

        /// <summary>是否支持多轴直线插补</summary>
        public bool SupportsLineInterpolation { get; init; }

        /// <summary>是否支持圆弧插补</summary>
        public bool SupportsArcInterpolation { get; init; }

        /// <summary>是否支持点动（Jog；调试面板用，按住才动）</summary>
        public bool SupportsJog { get; init; }

        /// <summary>支持的回零方式（空集合表示"尚未连接、能力未知"）</summary>
        public IReadOnlyList<HomeMode> SupportedHomeModes { get; init; } = Array.Empty<HomeMode>();

        /// <summary>能力未知（未连接时的默认值）</summary>
        public static MotionCapabilities Unknown { get; } = new();

        /// <summary>该回零方式是否被支持</summary>
        public bool SupportsHomeMode(HomeMode mode) => SupportedHomeModes.Contains(mode);

        /// <summary>
        /// IO 点数是"按常见值给的上限"而不是核实过的数（虚拟 PLC、未识别机型为 true）。
        ///
        /// 为什么让驱动把它报上来、而不是只写在说明文字里：
        /// 界面要**如实标注**（"点数未核实，按显示上限列出"），
        /// 而靠解析 <see cref="IMotionDevice.StateDetail"/> 那段自由文本无法可靠做到 ——
        /// 改一次措辞就会连带改坏界面。
        /// </summary>
        public bool IsIoCountApproximate { get; init; }

        /// <summary>轴号是否在范围内（防"配了 3 号轴、卡只有 2 个轴"这类配置错误）</summary>
        public bool IsAxisValid(int physicalIndex)
            => AxisCount > 0 && physicalIndex >= 0 && physicalIndex < AxisCount;

        /// <summary>数字输出点号是否在范围内</summary>
        public bool IsOutputValid(int port)
            => DigitalOutputCount > 0 && port >= 0 && port < DigitalOutputCount;

        /// <summary>数字输入点号是否在范围内</summary>
        public bool IsInputValid(int port)
            => DigitalInputCount > 0 && port >= 0 && port < DigitalInputCount;
    }
}
