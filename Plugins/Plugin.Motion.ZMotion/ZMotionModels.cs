using System;
using System.Collections.Generic;
using Core.Interfaces;

namespace Plugin.Motion.ZMotion
{
    /// <summary>
    /// 轴状态位域（<c>ZAux_Direct_GetAxisStatus</c> 的返回值）。
    ///
    /// 依据：正运动手册的 AXISSTATUS 定义，与仓库参考工程
    /// （<c>WPF-Halcon-流程拖拉\05Motion\Plugin.ZMotion\ZMotionAxis.cs</c>）里已在现场跑过的解析一致。
    /// 写成常量而不是散落的十六进制字面量：位定义是驱动与手册之间的契约，
    /// 散在代码里以后没人敢动（怕改错一位就误判限位）。
    /// </summary>
    public static class ZMotionAxisBits
    {
        /// <summary>bit3：伺服报警</summary>
        public const int Alarm = 0x08;

        /// <summary>bit4：正限位触发</summary>
        public const int PositiveLimit = 0x10;

        /// <summary>bit5：负限位触发</summary>
        public const int NegativeLimit = 0x20;

        /// <summary>bit11：到位（InPosition）</summary>
        public const int InPosition = 0x800;

        /// <summary>bit12：急停输入触发</summary>
        public const int EmergencyStop = 0x1000;
    }

    /// <summary>
    /// 状态字 → 轴状态快照（纯函数，便于断言）。
    ///
    /// 为什么单独抽出来：位含义写错的后果不是"少显示一个灯"，而是
    /// **把报警读成到位**（安全停机不触发）或把限位读成正常（继续往限位方向走）。
    /// 这类错误只有真机才会暴露，所以用纯函数钉死它。
    /// </summary>
    public static class ZMotionStatusParser
    {
        /// <summary>解析 <c>ZAux_Direct_GetAxisStatus</c> 返回的状态字</summary>
        public static AxisStatus Parse(int state, int physicalIndex = 0)
        {
            var status = new AxisStatus
            {
                PhysicalIndex = physicalIndex,
                Alarm = (state & ZMotionAxisBits.Alarm) != 0,
                PositiveLimit = (state & ZMotionAxisBits.PositiveLimit) != 0,
                NegativeLimit = (state & ZMotionAxisBits.NegativeLimit) != 0,
                InPosition = (state & ZMotionAxisBits.InPosition) != 0,
                EmergencyStop = (state & ZMotionAxisBits.EmergencyStop) != 0,
            };

            if (status.Alarm) status.AlarmMessage = "伺服报警";
            return status;
        }
    }

    /// <summary>
    /// 回零方式码（契约枚举 → 正运动 <c>ZAux_BusCmd_Datum</c> 的 homemode 参数）。
    ///
    /// 这些数字**只能来自手册**，不能靠猜 —— 填错一个数字，回零会按完全不同的寻零路径走
    /// （例如把"负限位+Index"填成"正限位+Index"，轴会朝反方向撞过去）。
    /// 本表取自参考工程里已投产的映射。
    /// </summary>
    public static class ZMotionHomeModes
    {
        /// <summary>映射失败返回 null（表示该卡不支持这种回零方式，由调用方拒绝命令）</summary>
        public static uint? ToSdk(HomeMode mode) => mode switch
        {
            HomeMode.NegativeLimitIndex => 1,
            HomeMode.PositiveLimitIndex => 2,
            HomeMode.NegativeLimit => 17,
            HomeMode.PositiveLimit => 18,
            HomeMode.Origin => 19,
            HomeMode.PositiveLimitOrigin => 23,
            HomeMode.NegativeLimitOrigin => 27,
            HomeMode.PresetZero => 37,
            _ => null,
        };

        /// <summary>该方式是不是"预设零点"（不需要找开关，直接置零；用于界面提示）</summary>
        public static bool IsPreset(HomeMode mode) => mode == HomeMode.PresetZero;
    }

    /// <summary>机型能力（轴数 / IO 数 / 是否总线型）</summary>
    public sealed class ZMotionModelInfo
    {
        /// <summary>机型名（用于日志与界面）</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>轴数</summary>
        public int AxisCount { get; init; } = 4;

        /// <summary>数字输入点数</summary>
        public int InputCount { get; init; } = 24;

        /// <summary>数字输出点数</summary>
        public int OutputCount { get; init; } = 16;

        /// <summary>
        /// 是否总线型（EtherCAT）。
        /// 决定回零走哪条 API：总线型用 <c>ZAux_BusCmd_Datum</c> + <c>GetHomeStatus</c>，
        /// 脉冲型用 <c>ZAux_Direct_Single_Datum</c> + <c>GetIfIdle</c>。
        /// </summary>
        public bool IsBusType { get; init; }

        /// <summary>机型是否被识别（未识别时界面/日志要提示核对轴数）</summary>
        public bool Recognized { get; init; }

        /// <summary>
        /// IO 点数是"按常见值给的上限"而不是核实过的数（虚拟 PLC / 未识别机型为 true）。
        ///
        /// 为什么单独标出来：IO 点数列多了只是"点一下会失败"，但列少了会让用户以为某个输出坏了。
        /// 与其在两个都可能是错的数字里选一个，不如**标出来让界面说实话**
        /// （"IO 点数未核实，按 24 显示上限"），用户至少知道该去哪儿确认。
        /// </summary>
        public bool IoCountApproximate { get; init; }
    }

    /// <summary>
    /// 机型 → 能力。支持哪些轴/回零方式全靠它，所以宁可"不认识就说不知道"也不猜：
    /// 猜错轴数会让界面允许操作一个不存在的轴，猜错 IO 数会让用户以为某个输出坏了。
    /// </summary>
    public static class ZMotionModels
    {
        /// <summary>明确认识的机型（取自参考工程里已投产的清单）</summary>
        private static readonly Dictionary<string, ZMotionModelInfo> Known =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["ECI3428"] = new() { Name = "ECI3428", AxisCount = 4, InputCount = 24, OutputCount = 16, IsBusType = true, Recognized = true },
                ["ECI3828"] = new() { Name = "ECI3828", AxisCount = 8, InputCount = 24, OutputCount = 20, IsBusType = true, Recognized = true },
                ["ZMC408SCAN"] = new() { Name = "ZMC408SCAN", AxisCount = 4, InputCount = 24, OutputCount = 20, IsBusType = true, Recognized = true },

                // 虚拟 PLC（RTSys 自带的仿真器也是这一类，实测机型名就是 VPLC532R）。
                // 轴数 32 取自控制器状态窗口的「RealAxes: 32」——
                // 同一窗口还写着 VirtualAxes: 64，那是控制器内部的**虚轴**（用于电子齿轮/凸轮的计算轴），
                // 不作为运动轴对外开放，所以这里取 32 而不是 64。
                // IO 点数**未经核实**（虚拟 PLC 的点位取决于组态），故标记 IoCountApproximate。
                ["VPLC532R"] = new()
                {
                    Name = "VPLC532R",
                    AxisCount = 32,
                    InputCount = 24,
                    OutputCount = 16,
                    IsBusType = true,
                    Recognized = true,
                    IoCountApproximate = true,
                },
            };

        /// <summary>未识别机型的兜底：4 轴 / 24 入 / 16 出，并标记 Recognized=false 让上层提示核对</summary>
        private static readonly ZMotionModelInfo Fallback =
            new()
            {
                Name = "未识别机型",
                AxisCount = 4,
                InputCount = 24,
                OutputCount = 16,
                IsBusType = true,
                Recognized = false,
                IoCountApproximate = true,   // 兜底值本来就是猜的，如实标记
            };

        /// <summary>
        /// 解析机型能力。
        /// 优先用配置里填的机型（用户可能知道得比卡更准），
        /// 配置为空时用控制器上报的 SoftType（<c>ZAux_GetControllerInfo</c>）。
        /// </summary>
        public static ZMotionModelInfo Resolve(string? configuredModel, string? reportedSoftType)
        {
            if (TryMatch(configuredModel, out var byConfig)) return byConfig;
            if (TryMatch(reportedSoftType, out var byReport)) return byReport;

            // 都没匹配上：不猜，用兜底并让上层提示。
            // 兜底值刻意取"常见的最小配置"（4 轴）：能力报小的后果是"某些轴不能操作"（可见、可改），
            // 报大的后果是"对着不存在的轴下发命令"（不可见，且真机上表现为莫名其妙的失败）。
            var name = !string.IsNullOrWhiteSpace(configuredModel) ? configuredModel!
                : !string.IsNullOrWhiteSpace(reportedSoftType) ? reportedSoftType!
                : "未知";
            return new ZMotionModelInfo
            {
                Name = name,
                AxisCount = Fallback.AxisCount,
                InputCount = Fallback.InputCount,
                OutputCount = Fallback.OutputCount,
                IsBusType = Fallback.IsBusType,
                Recognized = false,
                IoCountApproximate = true,
            };
        }

        private static bool TryMatch(string? model, out ZMotionModelInfo info)
        {
            info = Fallback;
            if (string.IsNullOrWhiteSpace(model)) return false;

            // 控制器上报的 SoftType 可能带后缀（如 "ECI3428_V2"），按前缀匹配
            foreach (var kv in Known)
            {
                if (model.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    info = kv.Value;
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 正运动错误码 → 可读故障（分级 + 说明 + 建议）。
    ///
    /// 【为什么这里不做"逐码翻译"】
    /// SDK 只给一个 int（<c>ZAux_*</c> 全系列都返回错误码），且**没有提供码→文案的接口**
    /// （本文件核实过：zauxdll 没有 GetErrMsg 这类导出）。
    /// 凭记忆编一张"12 = 参数错误、34 = 通信超时"的对照表，看起来专业，
    /// 但现场一旦按错误的方向排查（比如把"轴未使能"当成"网线坏了"），损失比"没翻译"大得多。
    ///
    /// 所以给出的东西是**有把握的那三层**：
    ///   ① 原始码（现场可对厂商手册，也可报给厂商）；
    ///   ② 出错的操作上下文（"回零时"还是"读状态时"——这本身就能定位大半）；
    ///   ③ 按操作类别的处置建议与分级（这决定"能不能自动重连/需不需要人"）。
    /// </summary>
    public static class ZMotionFaults
    {
        /// <summary>按"什么操作失败"构造一条故障</summary>
        public static MotionFault Interpret(
            string operation, int code, string axis = "", string suggestion = "")
            => new()
            {
                Severity = SeverityOf(operation),
                Code = code,
                Message = string.IsNullOrEmpty(axis)
                    ? $"{operation}失败（正运动错误码 {code}）"
                    : $"{operation}失败（轴 {axis}，正运动错误码 {code}）",
                Suggestion = string.IsNullOrWhiteSpace(suggestion) ? SuggestOf(operation) : suggestion,
                Axis = axis,
            };

        /// <summary>操作 → 分级（决定自动恢复还是必须人工介入）</summary>
        private static MotionFaultSeverity SeverityOf(string operation) => operation switch
        {
            // 连接类：多半是网线/IP/供电/被占用，不是"重试一下就好"，要人去看
            "连接控制器" => MotionFaultSeverity.RequiresService,

            // 回零类：失败可能正卡在原点开关上，必须人工确认后重来
            "回零" => MotionFaultSeverity.RequiresReset,

            // 运动类：不接受命令说明卡侧状态不对（未使能/报警/限位），要人处理
            "轴使能" or "轴失能" or "绝对运动" or "相对运动" or "轴停止" or "点动" => MotionFaultSeverity.RequiresReset,

            // 读状态/IO：连续失败才判失联（基类看门狗会兜），单次按可恢复
            _ => MotionFaultSeverity.Recoverable,
        };

        /// <summary>操作 → 处置建议</summary>
        private static string SuggestOf(string operation) => operation switch
        {
            "连接控制器" => "检查控制器 IP、网线与供电；确认没有被厂商调试软件或另一个本程序实例占用",
            "回零" => "检查原点/限位开关是否被触发、回零方式与回零速度是否与机构匹配；排除后重新回零",
            "轴使能" or "轴失能" => "检查伺服供电与急停回路是否闭合；确认轴号与驱动器映射正确",
            "绝对运动" or "相对运动" or "点动" => "检查该轴是否已使能、是否报警或撞到限位；确认目标位置在行程范围内",
            "轴停止" => "确认控制器通信正常；必要时用控制器面板或断电处理",
            "清除报警" => "确认报警原因已排除（过流/过载/断线）；若报警反复出现请检查机械负载",
            _ => "查看控制器状态与该轴是否有报警/限位；如持续出现请记录错误码联系厂商",
        };
    }
}
