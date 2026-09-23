using System;
using System.Globalization;
using System.Windows.Media;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 报警条上的一行。
    ///
    /// <b>为什么要有这个类，而不是让模板直接绑 <see cref="ScadaAlarmRecord"/></b>
    /// ---------
    /// 记录里全是"事实"（严重度是枚举、时间是 UTC、态是枚举），而模板要的是"画法"
    /// （这一行该用哪个画刷、时间该显成什么样）。把这段换算放进模板，就得用
    /// 转换器 + 跨层 <c>RelativeSource</c> 去取本图元配的颜色——那两样在无窗口的断言环境里
    /// 都不可靠，而且一旦出错是"静默显示成默认色"，最难查的一类问题。
    /// 放在这里，换算变成一次构造、一句断言就能钉住。
    ///
    /// 另一条：<b>本类是快照</b>。记录是长命对象（会从激活一路走到清除），
    /// 而这一行只表示"某一刻该怎么画"。引擎每次事件后整表重读，行对象全部重建——
    /// 于是不存在"行忘了跟着记录刷新"这种陈旧显示。
    /// </summary>
    public sealed class AlarmBannerRow
    {
        /// <param name="record">来源记录（保留引用，供"点这一行去确认"之类将来用）</param>
        /// <param name="chipBrush">这一行的严重度色（由图元按 <c>Severity</c> 挑好传进来）</param>
        public AlarmBannerRow(ScadaAlarmRecord record, Brush chipBrush)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));
            ChipBrush = chipBrush ?? Brushes.Transparent;

            SeverityText = record.Severity.DisplayName();
            TimeText = record.ActivatedAtLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            Name = record.Name;

            // 与历史面板共用同一句"条件｜触发值"（见 ScadaAlarmRecord.DetailText）：
            // 同一个报警在两处长得不一样，操作员就会以为说的是两件事。
            Detail = record.DetailText;
            StateText = CompactStateText(record.State);
            IsAcknowledged = record.State == ScadaAlarmState.Acknowledged;
        }

        /// <summary>来源记录（只读，模板用不到，留给交互与断言）</summary>
        public ScadaAlarmRecord Record { get; }

        /// <summary>严重度徽标的底色</summary>
        public Brush ChipBrush { get; }

        /// <summary>严重度徽标上的字：严重 / 警告 / 提示</summary>
        public string SeverityText { get; }

        /// <summary>激活时刻（本地时间，只到秒）</summary>
        public string TimeText { get; }

        /// <summary>报警名</summary>
        public string Name { get; }

        /// <summary>条件与触发值的人话（如 <c>"高限 &gt; 80｜触发值 92.5"</c>）；都没有时为空串</summary>
        public string Detail { get; }

        /// <summary>态机状态的短写法（见 <see cref="CompactStateText"/>）</summary>
        public string StateText { get; }

        /// <summary>操作员已确认过——模板据此把整行淡化（"这条我看过了"）</summary>
        public bool IsAcknowledged { get; }

        /// <summary>
        /// 状态的<b>短</b>写法。
        ///
        /// 为什么不直接用 <see cref="ScadaAlarmExtensions.DisplayName(ScadaAlarmState)"/>：
        /// 那个词汇表里"已恢复未确认"是给历史面板、导出、日志用的完整叫法；
        /// 报警条一行要挤下徽标、时间、名字、条件、状态五样东西，六个字的状态会把名字挤没。
        /// 缩短丢掉的"未确认"这一层意思，由表头的未确认计数与整行淡化补回来。
        /// </summary>
        private static string CompactStateText(ScadaAlarmState state) => state switch
        {
            ScadaAlarmState.Active => "激活",
            ScadaAlarmState.Acknowledged => "已确认",
            ScadaAlarmState.Recovered => "已恢复",
            _ => "正常",
        };
    }
}
