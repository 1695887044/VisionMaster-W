using System.Collections.Generic;

namespace Core.Interfaces
{
    /// <summary>
    /// 运动控制枚举的**界面文案**（中文）+ 提示。
    ///
    /// 为什么放在契约层、而不是各界面各写一份：
    ///   回零方式、卡状态这些是**驱动与界面共用的词汇** —— 驱动写日志、界面显示下拉、
    ///   故障信息里提到它们，必须是同一个说法。否则同一个概念会在不同地方变成不同中文，
    ///   现场对不上话。
    ///
    /// 为什么不用 [Description]/[Display] 特性标在枚举成员上：
    ///   ① 特性要靠反射读，启动时逐项取不如字典直接，且**漏标是静默的**；
    ///      这里用 switch 表达式，**漏一个枚举成员编译器会直接报错**（穷尽性检查）。
    ///   ② 枚举成员名（<c>NegativeLimitIndex</c>）要保持与厂商手册一致，方便对照文档；
    ///      中文属于"展示层"，不该塞进契约枚举的定义里。
    /// </summary>
    public static class MotionEnumDisplay
    {
        /// <summary>
        /// 契约里定义的全部回零方式（顺序即界面下拉的推荐顺序）。
        /// 用在"未连接、还不知道卡支持哪些"的时候 —— 此时列全部比列空白好：
        /// 空白下拉会让用户以为这个功能不可用。
        /// </summary>
        public static readonly IReadOnlyList<HomeMode> AllHomeModes = new[]
        {
            HomeMode.NegativeLimitIndex,
            HomeMode.PositiveLimitIndex,
            HomeMode.Origin,
            HomeMode.NegativeLimitOrigin,
            HomeMode.PositiveLimitOrigin,
            HomeMode.NegativeLimit,
            HomeMode.PositiveLimit,
            HomeMode.PresetZero,
        };

        /// <summary>回零方式的中文名（下拉里显示的就是它）</summary>
        public static string Text(HomeMode mode) => mode switch
        {
            HomeMode.NegativeLimitIndex => "负限位 + Index",
            HomeMode.PositiveLimitIndex => "正限位 + Index",
            HomeMode.Origin => "原点开关",
            HomeMode.NegativeLimitOrigin => "负限位 + 原点",
            HomeMode.PositiveLimitOrigin => "正限位 + 原点",
            HomeMode.NegativeLimit => "负限位（不回原点）",
            HomeMode.PositiveLimit => "正限位（不回原点）",
            HomeMode.PresetZero => "零位置预设（不找开关）",
            _ => mode.ToString(),
        };

        /// <summary>回零方式的一句话说明（下拉项的第二行 / 悬停提示）</summary>
        public static string Hint(HomeMode mode) => mode switch
        {
            HomeMode.NegativeLimitIndex => "撞到负限位后找第一个 Index 脉冲，精度最高，机台最常见",
            HomeMode.PositiveLimitIndex => "撞到正限位后找第一个 Index 脉冲",
            HomeMode.Origin => "只找原点开关，不看限位",
            HomeMode.NegativeLimitOrigin => "先找负限位再回原点开关",
            HomeMode.PositiveLimitOrigin => "先找正限位再回原点开关",
            HomeMode.NegativeLimit => "撞到负限位即停（不回原点，位置随开关重复性变化）",
            HomeMode.PositiveLimit => "撞到正限位即停（不回原点）",
            HomeMode.PresetZero => "直接把当前位置置为 0 —— 用于绝对值编码器或已经在对位位置的场合",
            _ => string.Empty,
        };

        /// <summary>运动卡状态的中文（状态栏、故障提示共用）</summary>
        public static string Text(MotionCardState state) => state switch
        {
            MotionCardState.Closed => "未连接",
            MotionCardState.Connecting => "连接中",
            MotionCardState.Online => "在线",
            MotionCardState.Alarm => "报警",
            MotionCardState.SafeStopped => "已安全停机",
            _ => state.ToString(),
        };

        /// <summary>
        /// 命令入队结果的中文。
        /// 每种"被拒绝"都要说清**为什么**，而不是笼统一句"被拒绝"——
        /// 现场看到"队列已满"和"未连接"该做的事完全不同（一个是发太快，一个是查线）。
        /// </summary>
        public static string Text(MotionCommandResult result) => result switch
        {
            MotionCommandResult.Accepted => "已接受",
            MotionCommandResult.Rejected_NotConnected => "被拒绝：控制器未连接",
            MotionCommandResult.Rejected_NotSupported => "被拒绝：该卡或该轴不支持此命令",
            MotionCommandResult.Rejected_InvalidArgument => "被拒绝：参数非法（轴号越界或超出软限位）",
            MotionCommandResult.Rejected_QueueFull => "被拒绝：命令队列已满（下发太快或卡侧卡住）",
            _ => result.ToString(),
        };
    }
}
