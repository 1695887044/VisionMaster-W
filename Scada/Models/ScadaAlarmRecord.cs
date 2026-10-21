using System;
using System.Globalization;
using Newtonsoft.Json;
using Prism.Mvvm;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 一次报警的<b>生命周期记录</b>（运行态产物，不落 .vms）。
    ///
    /// 一条记录 = 一次"激活 → 清除"的完整过程：条件成立时创建，态迁移（确认/恢复/清除）
    /// 时<b>原地更新同一个对象</b>，时间戳逐格填上。这样历史面板里看到的一行，
    /// 就是这次报警从生到死的全部事实，不需要用户自己去几条事件里拼。
    ///
    /// 为什么不继承 <see cref="ScadaModelBase"/>
    /// ---------
    /// 那个基类给的是"写守卫 + 撤销记账"，两者都是<b>编辑期</b>概念。
    /// 报警记录由运行态产生：让它进撤销栈，用户按 Ctrl+Z 就能"撤销掉一条已经发生过的报警"——
    /// 那是篡改历史，不是编辑。所以本类只继承 Prism 的 <see cref="BindableBase"/> 拿变更通知
    /// （实时报警条要跟着态迁移刷新），绝不接撤销栈。
    /// </summary>
    public class ScadaAlarmRecord : BindableBase
    {
        private ScadaAlarmState _state;
        private DateTime? _acknowledgedAtUtc;
        private DateTime? _recoveredAtUtc;
        private DateTime? _clearedAtUtc;

        /// <param name="definition">触发它的那条定义（名称/严重度/条件从配置里取一次快照）</param>
        /// <param name="activatedAtUtc">激活时刻（UTC）</param>
        /// <param name="triggerValue">触发时的值文本（给现场看"报的时候是多少"）</param>
        public ScadaAlarmRecord(ScadaAlarmDefinition definition, DateTime activatedAtUtc, string? triggerValue)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            RecordId = Guid.NewGuid();
            AlarmId = definition.AlarmId;
            Name = definition.Name;
            Message = definition.DisplayText;
            Severity = definition.Severity;
            Kind = definition.Kind;
            VariableName = definition.VariableName;
            ConditionText = definition.ConditionText;
            AlarmGroup = definition.AlarmGroup;
            Cause = definition.Cause;
            Remedy = definition.Remedy;
            ExtraInfo = definition.ExtraInfo;
            ActivatedAtUtc = activatedAtUtc;
            TriggerValue = triggerValue;
            _state = ScadaAlarmState.Active;
            IsHistorical = false;
        }

        /// <summary>
        /// 从方案目录里的历史快照恢复一条记录（P0：报警历史跟着方案走）。
        /// 与运行态记录的差别只有 <see cref="IsHistorical"/>：历史行不许点“确认”——
        /// 那一轮运行的引擎早就没了，按 AlarmId 找回去只会得到“找不到”。
        /// </summary>
        internal ScadaAlarmRecord(
            Guid recordId,
            Guid alarmId,
            string name,
            string message,
            ScadaAlarmSeverity severity,
            ScadaAlarmKind kind,
            string? variableName,
            string conditionText,
            string? alarmGroup,
            string? cause,
            string? remedy,
            string? extraInfo,
            DateTime activatedAtUtc,
            string? triggerValue,
            ScadaAlarmState state,
            DateTime? acknowledgedAtUtc,
            DateTime? recoveredAtUtc,
            DateTime? clearedAtUtc)
        {
            RecordId = recordId == Guid.Empty ? Guid.NewGuid() : recordId;
            AlarmId = alarmId;
            Name = name;
            Message = message;
            Severity = severity;
            Kind = kind;
            VariableName = variableName;
            ConditionText = conditionText;
            AlarmGroup = alarmGroup;
            Cause = cause;
            Remedy = remedy;
            ExtraInfo = extraInfo;
            ActivatedAtUtc = activatedAtUtc;
            TriggerValue = triggerValue;
            _state = state;
            _acknowledgedAtUtc = acknowledgedAtUtc;
            _recoveredAtUtc = recoveredAtUtc;
            _clearedAtUtc = clearedAtUtc;
            IsHistorical = true;
        }

        /// <summary>本条记录的身份（同一条报警定义可以产生很多条记录，实时列表按它认行）</summary>
        public Guid RecordId { get; }

        /// <summary>是否从方案目录的历史快照恢复（历史行只读，不许点确认）</summary>
        [JsonIgnore]
        public bool IsHistorical { get; }

        /// <summary>来源定义的身份（点"确认"时按它找回去）</summary>
        public Guid AlarmId { get; }

        /// <summary>报警名（配置快照）</summary>
        public string Name { get; }

        /// <summary>报警文本（配置快照，已做过"空则回落名字"处理）</summary>
        public string Message { get; }

        /// <summary>严重度（配置快照）</summary>
        public ScadaAlarmSeverity Severity { get; }

        /// <summary>条件种类（配置快照）</summary>
        public ScadaAlarmKind Kind { get; }

        /// <summary>被监视的变量名（配置快照，用于"是哪个变量报的"）</summary>
        public string? VariableName { get; }

        /// <summary>条件的人话描述（配置快照，如 <c>"高限 &gt; 80"</c>）</summary>
        public string ConditionText { get; }

        /// <summary>报警组（配置快照，如"一号线"）；没配为 null</summary>
        public string? AlarmGroup { get; }

        /// <summary>故障原因（配置快照，长文本）；没配为 null</summary>
        public string? Cause { get; }

        /// <summary>解决措施（配置快照，长文本）；没配为 null</summary>
        public string? Remedy { get; }

        /// <summary>附加信息（配置快照，长文本）；没配为 null</summary>
        public string? ExtraInfo { get; }

        /// <summary>激活时刻（UTC）</summary>
        public DateTime ActivatedAtUtc { get; }

        /// <summary>触发时的值文本；拿不到值时为 null</summary>
        public string? TriggerValue { get; }

        /// <summary>当前态机状态。用 <c>SetProperty</c> 通知——实时报警条靠它变色</summary>
        public ScadaAlarmState State
        {
            get => _state;
            set
            {
                if (SetProperty(ref _state, value))
                    RaisePropertyChanged(nameof(IsActive));
            }
        }

        /// <summary>操作员确认时刻（UTC）；没确认过为 null</summary>
        public DateTime? AcknowledgedAtUtc
        {
            get => _acknowledgedAtUtc;
            set => SetProperty(ref _acknowledgedAtUtc, value);
        }

        /// <summary>条件恢复时刻（UTC）；还没恢复为 null</summary>
        public DateTime? RecoveredAtUtc
        {
            get => _recoveredAtUtc;
            set
            {
                if (SetProperty(ref _recoveredAtUtc, value))
                    RaisePropertyChanged(nameof(Duration));
            }
        }

        /// <summary>回到正常态（彻底从实时列表消失）的时刻（UTC）</summary>
        public DateTime? ClearedAtUtc
        {
            get => _clearedAtUtc;
            set => SetProperty(ref _clearedAtUtc, value);
        }

        /// <summary>条件是否仍成立（<see cref="ScadaAlarmState.Recovered"/> 与 <see cref="ScadaAlarmState.Normal"/> 都是"不成立"）</summary>
        [JsonIgnore]
        public bool IsActive => _state == ScadaAlarmState.Active || _state == ScadaAlarmState.Acknowledged;

        /// <summary>是否还挂在实时报警列表上</summary>
        [JsonIgnore]
        public bool IsAbnormal => _state.IsAbnormal();

        /// <summary>是否在等操作员确认</summary>
        [JsonIgnore]
        public bool NeedsAcknowledge => _state.NeedsAcknowledge();

        /// <summary>
        /// 报警持续时长：从激活到恢复（未恢复则算到此刻）。
        /// 之所以算到<b>恢复</b>而不是<b>清除</b>：清除时刻里含操作员多久之后才来点确认，
        /// 那是"人慢"，不是"报警持续"。混在一起会让统计出来的设备故障时长偏大。
        /// </summary>
        [JsonIgnore]
        public TimeSpan Duration => (_recoveredAtUtc ?? DateTime.UtcNow) - ActivatedAtUtc;

        // ── 本地时间投影（面板显示与 CSV 导出用；落盘一律用上面的 UTC 字段）──

        [JsonIgnore] public DateTime ActivatedAtLocal => ActivatedAtUtc.ToLocalTime();
        [JsonIgnore] public DateTime? AcknowledgedAtLocal => _acknowledgedAtUtc?.ToLocalTime();
        [JsonIgnore] public DateTime? RecoveredAtLocal => _recoveredAtUtc?.ToLocalTime();
        [JsonIgnore] public DateTime? ClearedAtLocal => _clearedAtUtc?.ToLocalTime();

        /// <summary>持续时长的人话（如 <c>"1分23秒"</c>），面板与导出共用</summary>
        [JsonIgnore]
        public string DurationText
        {
            get
            {
                var d = Duration;
                if (d.TotalHours >= 1)
                    return $"{(int)d.TotalHours}小时{d.Minutes}分";
                if (d.TotalMinutes >= 1)
                    return $"{(int)d.TotalMinutes}分{d.Seconds}秒";
                return $"{d.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)}秒";
            }
        }

        /// <summary>
        /// 条件 + 触发值拼成一句话（如 <c>"高限 &gt; 80｜触发值 92.5"</c>），面板与报警条共用。
        ///
        /// 为什么放在记录上而不是各面板各拼一次：现场看到"1#电机超温"的第一句追问永远是
        /// "报的时候是多少"。这句话的措辞一旦在报警条与历史面板里长得不一样，
        /// 操作员就会以为两处说的是两件事。放这儿，措辞只有一处。
        /// </summary>
        [JsonIgnore]
        public string DetailText
        {
            get
            {
                var condition = ConditionText;

                if (string.IsNullOrWhiteSpace(TriggerValue))
                    return condition ?? string.Empty;

                var value = $"触发值 {TriggerValue}";

                return string.IsNullOrWhiteSpace(condition) ? value : $"{condition}｜{value}";
            }
        }

        /// <summary>
        /// 故障原因 / 解决措施 / 附加信息拼成的多行文本，供报警条与历史面板的悬停提示用。
        /// 三段都没配时返回空串（调用方据此决定不显示提示）。
        ///
        /// 为什么拼在记录上而不是各面板各拼一次：理由与 <see cref="DetailText"/> 逐字相同——
        /// "怎么修"这句话在报警条与历史里长得不一样，操作员就会以为说的是两件事。
        /// </summary>
        [JsonIgnore]
        public string HelpText
        {
            get
            {
                var lines = new List<string>(3);

                if (!string.IsNullOrWhiteSpace(Cause))
                    lines.Add($"故障原因：{Cause!.Trim()}");

                if (!string.IsNullOrWhiteSpace(Remedy))
                    lines.Add($"解决措施：{Remedy!.Trim()}");

                if (!string.IsNullOrWhiteSpace(ExtraInfo))
                    lines.Add($"附加信息：{ExtraInfo!.Trim()}");

                return string.Join(Environment.NewLine, lines);
            }
        }
    }

    /// <summary>
    /// 报警记录的可落盘快照（方案目录 <c>Alarms\AlarmHistory.json</c>）。
    ///
    /// 为什么不直接序列化 <see cref="ScadaAlarmRecord"/>：它的构造函数要一条
    /// <see cref="ScadaAlarmDefinition"/>，而历史记录必须独立于“现在这条报警定义还在不在”
    /// （定义改名/删除后，历史里那一条仍要按当时的快照显示）。快照把全部展示字段摊平，
    /// 反序列化不需要回头查方案。
    /// </summary>
    public sealed class ScadaAlarmRecordSnapshot
    {
        public Guid RecordId { get; set; }

        public Guid AlarmId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public ScadaAlarmSeverity Severity { get; set; }

        public ScadaAlarmKind Kind { get; set; }

        public string? VariableName { get; set; }

        public string ConditionText { get; set; } = string.Empty;

        public string? AlarmGroup { get; set; }

        public string? Cause { get; set; }

        public string? Remedy { get; set; }

        public string? ExtraInfo { get; set; }

        public DateTime ActivatedAtUtc { get; set; }

        public string? TriggerValue { get; set; }

        public ScadaAlarmState State { get; set; }

        public DateTime? AcknowledgedAtUtc { get; set; }

        public DateTime? RecoveredAtUtc { get; set; }

        public DateTime? ClearedAtUtc { get; set; }

        public static ScadaAlarmRecordSnapshot FromRecord(ScadaAlarmRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            return new ScadaAlarmRecordSnapshot
            {
                RecordId = record.RecordId,
                AlarmId = record.AlarmId,
                Name = record.Name,
                Message = record.Message,
                Severity = record.Severity,
                Kind = record.Kind,
                VariableName = record.VariableName,
                ConditionText = record.ConditionText,
                AlarmGroup = record.AlarmGroup,
                Cause = record.Cause,
                Remedy = record.Remedy,
                ExtraInfo = record.ExtraInfo,
                ActivatedAtUtc = record.ActivatedAtUtc,
                TriggerValue = record.TriggerValue,
                State = record.State,
                AcknowledgedAtUtc = record.AcknowledgedAtUtc,
                RecoveredAtUtc = record.RecoveredAtUtc,
                ClearedAtUtc = record.ClearedAtUtc,
            };
        }

        public ScadaAlarmRecord ToRecord()
            => new(
                RecordId,
                AlarmId,
                Name ?? string.Empty,
                Message ?? string.Empty,
                Severity,
                Kind,
                VariableName,
                ConditionText ?? string.Empty,
                AlarmGroup,
                Cause,
                Remedy,
                ExtraInfo,
                ActivatedAtUtc,
                TriggerValue,
                State,
                AcknowledgedAtUtc,
                RecoveredAtUtc,
                ClearedAtUtc);
    }
}
