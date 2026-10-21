using System;
using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 报警只读快照出口（M2b，AckButton 的列表/未确认计数）。
    ///
    /// <b>为什么单独一个接口</b>：<see cref="IScadaAlarmCommands"/> 只有"确认"这一个写动作；
    /// Web 的 AckButton 还要显示"当前有哪些报警、哪条最早、还有几条没确认"。
    /// 直接把 <c>ScadaAlarmEngine</c> 暴露给 Web 模块会让它拿到可变引擎（可以 Tick/Detach），
    /// 这里只给不可变 DTO。
    /// </summary>
    public interface IScadaAlarmFeed
    {
        /// <summary>当前报警快照（已按严重度/激活时间排序，与桌面报警条同源）</summary>
        ScadaAlarmFeedSnapshot Snapshot();
    }

    /// <summary>一条报警的只读投影（字段与 <c>ScadaAlarmRecord</c> 的读面一致）</summary>
    public sealed record ScadaAlarmInfo(
        Guid AlarmId,
        string Name,
        string Message,
        ScadaAlarmSeverity Severity,
        DateTime ActivatedAtUtc,
        bool Acknowledged);

    /// <summary>报警列表 + 未确认计数（同一次读取，避免"列表 3 条、计数 2"的撕裂）</summary>
    public sealed record ScadaAlarmFeedSnapshot(
        IReadOnlyList<ScadaAlarmInfo> Alarms,
        int UnacknowledgedCount)
    {
        public static readonly ScadaAlarmFeedSnapshot Empty =
            new(Array.Empty<ScadaAlarmInfo>(), 0);
    }
}
