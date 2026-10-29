namespace VisionMaster.Scada
{
    /// <summary>
    /// 报警静音出口：让画面上的按钮能把报警声音静音一段时间。
    ///
    /// 为什么单独一个最小接口而不是把宿主的 <c>ScadaSoundAlarmService</c> 交给领域层：
    /// 与 <see cref="IScadaAlarmCommands"/> 同一条分工——领域层只认"能不能静音"这一件事，
    /// 具体是声卡、蜂鸣器还是语音播报，由宿主实现。断言工程也能塞一个假出口进来。
    ///
    /// 线程：只允许在 UI 线程调用（与权限/报警命令同一纪律）。
    /// </summary>
    public interface IScadaAlarmMute
    {
        /// <summary>此刻是否处于静音期</summary>
        bool IsMuted { get; }

        /// <summary>当前配置的静音时长（分钟）</summary>
        int MuteMinutes { get; }

        /// <summary>
        /// 静音 <paramref name="minutes"/> 分钟。返回 false 时 <paramref name="error"/> 给可上屏原因
        /// （未接声音设备 / 时长非法 / 宿主拒绝）。<b>紧急级报警不受静音约束</b>，这一点由执行侧说明。
        /// </summary>
        bool TryMute(int minutes, out string? error);

        /// <summary>取消静音（幂等：本来没静音也返回 true）</summary>
        bool TryUnmute(out string? error);
    }
}
