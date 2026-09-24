using System;
using System.Globalization;
using System.Windows.Media;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 报警历史面板上的一行。
    ///
    /// 为什么要有这个类，而不是让表格直接绑 <see cref="ScadaAlarmRecord"/>
    /// ---------
    /// 记录里全是<b>事实</b>（严重度是枚举、时间是 UTC、态是枚举），而表格要的是<b>画法</b>
    /// （这一行用哪个色、时间显成什么样、这一行能不能点"确认"）。跟
    /// <c>AlarmBannerRow</c> 是同一个理由：换算留在类里，一次构造就能断言，
    /// 不必靠转换器 + 跨层 <c>RelativeSource</c> 去凑——那两样在无窗口的断言环境里都不可靠，
    /// 出错还是"静默显示成默认色"。
    ///
    /// 与报警条那行的差别
    /// ---------
    /// ① 时间要带日期（<c>MM-dd</c>）：历史横跨一整天甚至跨天，只给 <c>HH:mm:ss</c> 分不清是哪天报的；
    /// ② 状态用<b>完整</b>词汇（"已恢复未确认"），不用报警条那套缩短写法——
    ///    这里一行够宽，而"还没确认"正是历史面板最需要一眼看出来的事；
    /// ③ 本类是<b>可变的</b>：只有持续时长会随钟走（未恢复的那条从"1分3秒"一路涨上去），
    ///    靠 <see cref="RefreshDuration"/> 单点刷新，避免为了一行数字整表重建。
    /// </summary>
    public sealed class ScadaAlarmHistoryRow : BindableBase
    {
        // 色板与报警条默认色一致（见 AlarmBannerElement 的四个 Default*Color）：
        // 同一个报警在报警条上是这个色、在历史里是另一个色，现场会以为严重度变了。
        private static readonly Brush InfoBrush = ScadaBrushes.Frozen("#FF3B82F6");
        private static readonly Brush WarningBrush = ScadaBrushes.Frozen("#FFFFB020");
        private static readonly Brush CriticalBrush = ScadaBrushes.Frozen("#FFE03A2B");

        private string _durationText;

        /// <param name="record">来源记录（保留引用：确认要按它找引擎，导出要按它取原值）</param>
        public ScadaAlarmHistoryRow(ScadaAlarmRecord record)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));

            Severity = record.Severity;
            SeverityText = record.Severity.DisplayName();
            SeverityBrush = BrushFor(record.Severity);

            ActivatedAt = record.ActivatedAtLocal;
            TimeText = ActivatedAt.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            FullTimeText = ActivatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            Name = record.Name;
            Detail = record.DetailText;
            VariableName = record.VariableName ?? string.Empty;
            AlarmGroup = record.AlarmGroup ?? string.Empty;

            // 名称列的悬停提示：报警文本 + 「怎么修」。表格宽度有限，三段长文本挤不进列，
            // 而现场查历史时"当时到底该干什么"正是翻这条记录的目的。
            // 拼装口径统一在记录上（见 ScadaAlarmRecord.HelpText），本行只做拼接。
            NameToolTip = string.IsNullOrWhiteSpace(record.HelpText)
                ? record.Message
                : record.Message + Environment.NewLine + Environment.NewLine + record.HelpText;

            State = record.State;
            StateText = record.State.DisplayName();
            IsAcknowledged = record.State == ScadaAlarmState.Acknowledged;
            CanAcknowledge = record.NeedsAcknowledge;

            _durationText = record.DurationText;
        }

        /// <summary>来源记录（只读；表格用不到，留给"确认/导出"与断言）</summary>
        public ScadaAlarmRecord Record { get; }

        /// <summary>本条记录的身份。表格按它认行——整表重建后仍能找回原来选中的那一行</summary>
        public Guid RecordId => Record.RecordId;

        /// <summary>严重度枚举（筛选按它判，不按显示文本判：文本是可改的词汇表）</summary>
        public ScadaAlarmSeverity Severity { get; }

        /// <summary>严重度徽标上的字：严重 / 警告 / 提示</summary>
        public string SeverityText { get; }

        /// <summary>严重度徽标的底色</summary>
        public Brush SeverityBrush { get; }

        /// <summary>激活时刻（本地）</summary>
        public DateTime ActivatedAt { get; }

        /// <summary>激活时刻（本地，<c>MM-dd HH:mm:ss</c>）：列里显示的短写法</summary>
        public string TimeText { get; }

        /// <summary>激活时刻（本地，带年份）：悬停提示用</summary>
        public string FullTimeText { get; }

        /// <summary>报警名</summary>
        public string Name { get; }

        /// <summary>条件与触发值的人话（与报警条同一句，见 <see cref="ScadaAlarmRecord.DetailText"/>）</summary>
        public string Detail { get; }

        /// <summary>被监视的变量名；没有时为空串</summary>
        public string VariableName { get; }

        /// <summary>报警组；没配时为空串（列里显示空白，不显示"（无）"之类的占位）</summary>
        public string AlarmGroup { get; }

        /// <summary>名称列的悬停提示：报警文本 + 故障原因/解决措施/附加信息</summary>
        public string NameToolTip { get; }

        /// <summary>态机状态（筛选按它判）</summary>
        public ScadaAlarmState State { get; }

        /// <summary>状态的人话（完整词汇：如"已恢复未确认"）</summary>
        public string StateText { get; }

        /// <summary>操作员已确认过——模板据此把整行淡化（"这条我看过了"）</summary>
        public bool IsAcknowledged { get; }

        /// <summary>还能不能点"确认"（已确认过的、或已清除的不给按钮，免得点了没反应）</summary>
        public bool CanAcknowledge { get; }

        /// <summary>持续时长的人话；未恢复的会随钟走，靠 <see cref="RefreshDuration"/> 刷新</summary>
        public string DurationText
        {
            get => _durationText;
            private set => SetProperty(ref _durationText, value);
        }

        /// <summary>严重度色。未知取值给"提示"色：宁可低估也不谎报严重</summary>
        public static Brush BrushFor(ScadaAlarmSeverity severity) => severity switch
        {
            ScadaAlarmSeverity.Critical => CriticalBrush,
            ScadaAlarmSeverity.Warning => WarningBrush,
            _ => InfoBrush,
        };

        /// <summary>
        /// 重算持续时长（只动这一个属性）。由面板的秒级节拍调用，见 <c>ScadaAlarmHistoryViewModel.OnBeat</c>。
        /// 已恢复的记录时长是定值，这里算出来一样，<c>SetProperty</c> 会自己判等不发通知。
        /// </summary>
        public void RefreshDuration() => DurationText = Record.DurationText;

        /// <summary>
        /// 关键字是否命中本行。搜的是<b>报警名 / 报警文本 / 变量名 / 条件描述 / 报警组</b>五样——
        /// 现场找一条报警时手里可能只有其中任何一样（"1#电机""Temp01""高限""一号线"）。
        /// 空关键字一律算命中（等价于不过滤）。
        ///
        /// 故障原因/解决措施/附加信息<b>刻意不搜</b>：那三段是长句子，
        /// 搜一个常用字（如"检查"）会把整张表点亮，反而筛不出想找的那条。
        /// </summary>
        public bool Matches(string? keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return true;

            return Contains(Name, keyword)
                || Contains(Record.Message, keyword)
                || Contains(VariableName, keyword)
                || Contains(Detail, keyword)
                || Contains(AlarmGroup, keyword);
        }

        /// <summary>忽略大小写与首尾空格：现场没人会为了搜一个变量名去对大小写</summary>
        private static bool Contains(string? source, string keyword)
            => !string.IsNullOrEmpty(source)
               && source.IndexOf(keyword.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
