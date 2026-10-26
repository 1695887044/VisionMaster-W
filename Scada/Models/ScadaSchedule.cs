using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace VisionMaster.Scada
{
    /// <summary>计划表的触发类型（落盘字符串，显示名见 <see cref="ScadaChoiceNames"/>）。</summary>
    public static class ScadaScheduleKinds
    {
        public const string Time = "Time";
        public const string Event = "Event";
    }

    /// <summary>时间计划的周期。</summary>
    public static class ScadaSchedulePeriods
    {
        public const string Daily = "Daily";
        public const string Weekly = "Weekly";
        public const string Date = "Date";
    }

    /// <summary>事件计划的条件（与变量事件同名词，语义也一致）。</summary>
    public static class ScadaScheduleConditions
    {
        public const string IsTrue = "IsTrue";
        public const string IsFalse = "IsFalse";
        public const string Changed = "Changed";
        public const string AboveLimit = "AboveLimit";
        public const string BelowLimit = "BelowLimit";
    }

    /// <summary>
    /// 一条计划（<b>方案级配置</b>，落在 <c>.vms</c> 的 <c>Schedules</c> 里）。
    ///
    /// 两种触发
    /// ---------
    /// · <see cref="ScadaScheduleKinds.Time"/>：每天 / 每周 / 指定日期 + 时刻（<see cref="TimeOfDay"/>）；
    /// · <see cref="ScadaScheduleKinds.Event"/>：一个变量满足条件（<see cref="Condition"/>）<b>持续</b>
    ///   <see cref="DurationSeconds"/> 秒。
    ///
    /// 动作复用事件钩子
    /// ---------
    /// 实现 <see cref="IScadaEventHost"/>，里面只有一条 <see cref="ScadaEventType.ScheduleFired"/> 钩子。
    /// 这样"动作表"与它的编辑器（属性面板同一套 UI）、分发器、日志与审计全部原样复用——
    /// 计划表不需要自己的动作模型。
    ///
    /// 明确不做
    /// ---------
    /// 不替代 PLC 联锁：急停、超压泄放这类必须留在安全回路里；本表只做提示、记录、画面切换与非安全启停。
    /// 也不做毫秒级时序（引擎按秒级节拍评估）。
    /// </summary>
    public class ScadaSchedule : ScadaModelBase, IScadaEventHost
    {
        private Guid _scheduleId = Guid.NewGuid();
        private string _name = string.Empty;
        private bool _isEnabled = true;
        private string _kind = ScadaScheduleKinds.Time;
        private string _period = ScadaSchedulePeriods.Daily;
        private string _timeOfDay = "08:00";
        private string _days = "1,2,3,4,5";
        private string _dateText = string.Empty;
        private Guid _variableId;
        private string _variableName = string.Empty;
        private string _condition = ScadaScheduleConditions.IsTrue;
        private int _durationSeconds = 60;
        private double _upperLimit = 100;
        private double _lowerLimit;
        private ObservableCollection<ScadaEventHook> _eventHooks = new();

        /// <summary>稳定身份（动作里的引用、运行态的"上次触发"记账都按它走）</summary>
        public Guid ScheduleId
        {
            get => _scheduleId;
            set => SetProperty(ref _scheduleId, value);
        }

        /// <summary>计划名（列表上显示、日志与审计里的 subject）</summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value ?? string.Empty);
        }

        /// <summary>启用开关（停用 = 引擎跳过它，配置保留）</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>触发类型：<see cref="ScadaScheduleKinds"/></summary>
        public string Kind
        {
            get => _kind;
            set => SetProperty(ref _kind, string.IsNullOrWhiteSpace(value) ? ScadaScheduleKinds.Time : value);
        }

        /// <summary>时间计划的周期：<see cref="ScadaSchedulePeriods"/></summary>
        public string Period
        {
            get => _period;
            set => SetProperty(ref _period, string.IsNullOrWhiteSpace(value) ? ScadaSchedulePeriods.Daily : value);
        }

        /// <summary>时刻（HH:mm）。只取时与分，秒按 0 算</summary>
        public string TimeOfDay
        {
            get => _timeOfDay;
            set => SetProperty(ref _timeOfDay, value ?? string.Empty);
        }

        /// <summary>
        /// 每周的哪几天：<c>1..7</c>（1=周一 … 7=周日）逗号分隔。
        /// 与 .NET 的 <see cref="DayOfWeek"/>（0=周日）差一位——落盘用 ISO 口径，读的时候转换，
        /// 免得"文件里写 0 是周日还是周一"这种每年都要吵一次的问题。
        /// </summary>
        public string Days
        {
            get => _days;
            set => SetProperty(ref _days, value ?? string.Empty);
        }

        /// <summary>指定日期（yyyy-MM-dd）；只在 <see cref="ScadaSchedulePeriods.Date"/> 下有效</summary>
        public string DateText
        {
            get => _dateText;
            set => SetProperty(ref _dateText, value ?? string.Empty);
        }

        /// <summary>事件计划的变量 Id（稳定身份）</summary>
        public Guid VariableId
        {
            get => _variableId;
            set => SetProperty(ref _variableId, value);
        }

        /// <summary>事件计划的变量名（落盘兜底 + 显示；改名后由引用刷新修正）</summary>
        public string VariableName
        {
            get => _variableName;
            set => SetProperty(ref _variableName, value ?? string.Empty);
        }

        /// <summary>事件计划的条件：<see cref="ScadaScheduleConditions"/></summary>
        public string Condition
        {
            get => _condition;
            set => SetProperty(ref _condition, string.IsNullOrWhiteSpace(value) ? ScadaScheduleConditions.IsTrue : value);
        }

        /// <summary>条件需持续多少秒才触发（连续判定：条件一断就重新计时）</summary>
        public int DurationSeconds
        {
            get => _durationSeconds;
            set => SetProperty(ref _durationSeconds, value < 0 ? 0 : value);
        }

        /// <summary>「高于上限」用的阈值（只在 <see cref="ScadaScheduleConditions.AboveLimit"/> 下有效）</summary>
        public double UpperLimit
        {
            get => _upperLimit;
            set => SetProperty(ref _upperLimit, value);
        }

        /// <summary>「低于下限」用的阈值（只在 <see cref="ScadaScheduleConditions.BelowLimit"/> 下有效）</summary>
        public double LowerLimit
        {
            get => _lowerLimit;
            set => SetProperty(ref _lowerLimit, value);
        }

        /// <summary>动作钩子（只有 <see cref="ScadaEventType.ScheduleFired"/> 一条）</summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<ScadaEventHook> EventHooks
        {
            get => _eventHooks;
            set => _eventHooks = value ?? new ObservableCollection<ScadaEventHook>();
        }

        /// <summary>
        /// 本计划要执行的动作（没有就返回空表，不建空钩子）。
        ///
        /// <b>必须 JsonIgnore</b>：它是只读便利属性，返回的是钩子里那份集合；
        /// 不标的话 Json.NET 会把它当成"可填充的只读集合"，反序列化时把动作<b>再加一遍</b>
        /// （实测：序列化前 1 条、读回来 2 条，且不报任何错）。
        /// </summary>
        [JsonIgnore]
        public IReadOnlyList<ScadaAction> Actions
            => FindEventHook(ScadaEventType.ScheduleFired)?.Actions ?? (IReadOnlyList<ScadaAction>)Array.Empty<ScadaAction>();

        public ScadaEventHook? FindEventHook(ScadaEventType eventType)
            => _eventHooks.FirstOrDefault(hook => hook != null && hook.Event == eventType);

        public ScadaEventHook GetOrAddEventHook(ScadaEventType eventType)
            => FindEventHook(eventType) ?? AddEventHook(eventType);

        public ScadaEventHook AddEventHook(ScadaEventType eventType)
        {
            var hook = new ScadaEventHook { Event = eventType };
            _eventHooks.Add(hook);
            return hook;
        }

        public bool RemoveEventHook(ScadaEventType eventType)
        {
            var hook = FindEventHook(eventType);
            return hook != null && _eventHooks.Remove(hook);
        }

        public IScadaChangeScope BeginEdit(string label) => ScadaChangeScope.Begin(label);

        /// <summary>
        /// 列表上的一行摘要（"每天 08:00" / "每周一、三、五 08:00" / "T101.液位 高于上限 持续 600 秒"）。
        /// 放在模型上而不是界面里：日志、列表、审计要用同一句话。
        /// </summary>
        public string Describe
        {
            get
            {
                if (string.Equals(Kind, ScadaScheduleKinds.Event, StringComparison.OrdinalIgnoreCase))
                {
                    var condition = Condition switch
                    {
                        ScadaScheduleConditions.IsTrue => "为真",
                        ScadaScheduleConditions.IsFalse => "为假",
                        ScadaScheduleConditions.Changed => "值改变",
                        ScadaScheduleConditions.AboveLimit => "高于上限",
                        ScadaScheduleConditions.BelowLimit => "低于下限",
                        _ => Condition,
                    };

                    var variable = string.IsNullOrWhiteSpace(VariableName) ? "（未选变量）" : VariableName;
                    return $"{variable} {condition} 持续 {DurationSeconds} 秒";
                }

                var time = string.IsNullOrWhiteSpace(TimeOfDay) ? "--:--" : TimeOfDay;

                if (string.Equals(Period, ScadaSchedulePeriods.Weekly, StringComparison.OrdinalIgnoreCase))
                {
                    var days = ScadaScheduleClock.ParseDays(Days);
                    if (days.Count == 0)
                        return $"每周（未选星期） {time}";

                    var names = new[] { "一", "二", "三", "四", "五", "六", "日" };
                    var text = string.Join("、", days.OrderBy(d => d)
                        .Select(d => names[ScadaScheduleClock.ToIsoDay(d) - 1]));
                    return $"每周{text} {time}";
                }

                if (string.Equals(Period, ScadaSchedulePeriods.Date, StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrWhiteSpace(DateText) ? $"指定日期（未选） {time}" : $"{DateText} {time}";

                return $"每天 {time}";
            }
        }

        /// <summary>
        /// 摘要依赖的字段一变就补发一次 <see cref="Describe"/> 的通知——
        /// 列表那一行要跟着改（否则改了时刻，左列还写着旧时间，看着像没生效）。
        /// </summary>
        protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs args)
        {
            base.OnPropertyChanged(args);

            if (args.PropertyName is nameof(Name) or nameof(Kind) or nameof(Period) or nameof(TimeOfDay)
                or nameof(Days) or nameof(DateText) or nameof(VariableName) or nameof(Condition)
                or nameof(DurationSeconds))
            {
                RaisePropertyChanged(nameof(Describe));
            }
        }
    }

    /// <summary>
    /// 计划表的时间算术（<b>纯函数</b>，不读时钟、不碰 WPF）。
    ///
    /// 为什么单独一层：引擎按"到点没到点"判触发、界面按"下次什么时候"给预览、断言按固定时刻核对——
    /// 三处必须同一套算法，否则会出现"界面说 08:00、实际 08:01 才动"。
    /// </summary>
    public static class ScadaScheduleClock
    {
        /// <summary>解析 <c>HH:mm</c>（也接受 <c>H:mm</c> 与 <c>HH:mm:ss</c>，秒被丢弃）</summary>
        public static bool TryParseTimeOfDay(string? text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (!TimeSpan.TryParse(text.Trim(), CultureInfo.InvariantCulture, out var parsed))
                return false;

            if (parsed < TimeSpan.Zero || parsed >= TimeSpan.FromDays(1))
                return false;

            time = new TimeSpan(parsed.Hours, parsed.Minutes, 0);
            return true;
        }

        /// <summary>解析星期集合（<c>1..7</c>，1=周一；非法项丢弃）</summary>
        public static IReadOnlyList<DayOfWeek> ParseDays(string? text)
        {
            var result = new List<DayOfWeek>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            foreach (var part in text.Split(new[] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var iso))
                    continue;

                if (iso < 1 || iso > 7)
                    continue;

                var day = iso == 7 ? DayOfWeek.Sunday : (DayOfWeek)iso;
                if (!result.Contains(day))
                    result.Add(day);
            }

            result.Sort();
            return result;
        }

        /// <summary>星期 → ISO 数字（1=周一 … 7=周日）</summary>
        public static int ToIsoDay(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;

        /// <summary>
        /// 下一次触发时刻（严格晚于 <paramref name="now"/>）。配置不完整时返回 false
        /// （界面据此显示"未配置"，引擎据此跳过——不猜）。
        /// </summary>
        public static bool TryNextOccurrence(ScadaSchedule schedule, DateTime now, out DateTime next)
        {
            next = default;
            if (schedule == null)
                return false;

            if (!TryParseTimeOfDay(schedule.TimeOfDay, out var time))
                return false;

            if (string.Equals(schedule.Period, ScadaSchedulePeriods.Weekly, StringComparison.OrdinalIgnoreCase))
            {
                var days = ParseDays(schedule.Days);
                if (days.Count == 0)
                    return false;

                for (var offset = 0; offset <= 7; offset++)
                {
                    var candidate = now.Date.AddDays(offset) + time;
                    if (candidate <= now)
                        continue;

                    if (days.Contains(candidate.DayOfWeek))
                    {
                        next = candidate;
                        return true;
                    }
                }

                return false;
            }

            if (string.Equals(schedule.Period, ScadaSchedulePeriods.Date, StringComparison.OrdinalIgnoreCase))
            {
                if (!DateTime.TryParse(schedule.DateText, CultureInfo.CurrentCulture,
                        DateTimeStyles.AssumeLocal, out var date))
                {
                    return false;
                }

                var candidate = date.Date + time;
                if (candidate <= now)
                    return false;

                next = candidate;
                return true;
            }

            // 每天
            var today = now.Date + time;
            next = today > now ? today : today.AddDays(1);
            return true;
        }

        /// <summary>
        /// 某一"到点"是否在 <paramref name="window"/> 内（引擎按秒评估，窗口给 1 分钟足够）。
        /// 返回命中的那个时刻，供"同一次到点只执行一次"记账用。
        /// </summary>
        public static bool TryDueOccurrence(
            ScadaSchedule schedule, DateTime now, TimeSpan window, out DateTime due)
        {
            due = default;
            if (window <= TimeSpan.Zero)
                window = TimeSpan.FromMinutes(1);

            if (!TryNextOccurrence(schedule, now - window, out var candidate))
                return false;

            if (candidate > now)
                return false;

            due = candidate;
            return true;
        }

        /// <summary>"同一次到点"的记账键（精确到分；每天/每周同一时刻的两次到点不会撞）</summary>
        public static string OccurrenceKey(DateTime occurrence)
            => occurrence.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
    }
}
