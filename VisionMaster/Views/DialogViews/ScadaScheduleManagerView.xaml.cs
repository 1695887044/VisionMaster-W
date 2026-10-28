using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Prism.Mvvm;
using UI.CustomControl;
using VisionMaster.Scada;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 计划表管理器：左列计划清单 + 右列编辑表单（v2 版式，见 docs/UI设计基线/scada-schedule/）。
    ///
    /// 编辑的是<b>活对象</b>（直接写 <see cref="ScadaDocument.Schedules"/>），所以：
    /// · 打开前由调用方开一个变更作用域、并留一份 JSON 备份；
    /// · 「取消」时由调用方把备份写回并作废那次作用域（见 ScadaEditorViewModel.OpenSchedules）。
    ///
    /// 校验走 <see cref="IDialogValidator"/>：宿主 EasyDialog 在把结果设成 Primary 之前调用
    /// <see cref="ValidateAndShow"/>，不通过就留在弹窗里显示错误（旧版这里没有任何调用方）。
    ///
    /// 动作编辑器是<b>按名字引用</b>的简化版（跳页 / 写变量 / 启停流程 / 应用配方 / 播放声音 / 日志）：
    /// 完整的 27 种动作编辑器在属性面板里，那一套要变量选择器、画面选择器、轴选择器；
    /// 计划表 v2 先用"填名字"覆盖现场最常用的八种，避免把整套选择器搬进这个弹窗。
    /// </summary>
    public partial class ScadaScheduleManagerView : UserControl, INotifyPropertyChanged, IDialogValidator
    {
        private ScadaSchedule? _selected;
        private bool _syncing;
        private bool _ready;
        private bool _isReadOnly;
        private string _errorSummary = string.Empty;
        private bool _nameError;
        private bool _timeError;
        private bool _daysError;
        private bool _dateError;
        private bool _variableError;
        private bool _actionsError;

        public ScadaScheduleManagerView() : this(new ScadaDocument()) { }

        public ScadaScheduleManagerView(ScadaDocument document)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));

            InitializeComponent();

            DataContext = this;
            Schedules.CollectionChanged += OnSchedulesChanged;
            foreach (var schedule in Schedules)
                schedule.PropertyChanged += OnAnySchedulePropertyChanged;

            _ready = true;
            SelectedSchedule = Schedules.FirstOrDefault();
            RaiseAll();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>方案文档（列表直接绑它的 <see cref="ScadaDocument.Schedules"/>）</summary>
        public ScadaDocument Document { get; }

        public ObservableCollection<ScadaSchedule> Schedules => Document.Schedules;

        /// <summary>当前计划的动作行（每行包一条 <see cref="ScadaAction"/>）</summary>
        public ObservableCollection<ScheduleActionRow> Actions { get; } = new();

        /// <summary>当前选中的计划</summary>
        public ScadaSchedule? SelectedSchedule
        {
            get => _selected;
            set
            {
                if (ReferenceEquals(_selected, value))
                    return;

                if (_selected != null)
                    _selected.PropertyChanged -= OnSelectedPropertyChanged;

                _selected = value;

                if (_selected != null)
                    _selected.PropertyChanged += OnSelectedPropertyChanged;

                Raise(nameof(SelectedSchedule));
                RebuildActions();
                ClearErrors();
                RefreshAll();
                SyncSelection();
            }
        }

        /// <summary>只读态（角色不足时由宿主设置；v2 先留出口，接线见 README「已知偏差」）</summary>
        public bool IsReadOnly
        {
            get => _isReadOnly;
            set
            {
                if (_isReadOnly == value)
                    return;

                _isReadOnly = value;
                RaiseAll();
            }
        }

        // ---------------------------------------------------------------- 可见性

        public Visibility HasSelectionVisibility => Vis(_selected != null);

        public Visibility NoSelectionVisibility => Vis(_selected == null);

        public Visibility NoSchedulesVisibility => Vis(Schedules.Count == 0);

        public Visibility DeleteVisibility => Vis(_selected != null && !_isReadOnly);

        public Visibility ReadOnlyBannerVisibility => Vis(_isReadOnly);

        public Visibility TimePanelVisibility => Vis(_selected != null && !IsEventKind(_selected));

        public Visibility EventPanelVisibility => Vis(_selected != null && IsEventKind(_selected));

        public Visibility WeekPanelVisibility
            => Vis(_selected != null && !IsEventKind(_selected) && IsPeriod(_selected, ScadaSchedulePeriods.Weekly));

        public Visibility DatePanelVisibility
            => Vis(_selected != null && !IsEventKind(_selected) && IsPeriod(_selected, ScadaSchedulePeriods.Date));

        public Visibility LimitPanelVisibility
            => Vis(_selected != null && IsEventKind(_selected) && IsLimitCondition(_selected.Condition));

        public Visibility ErrorVisibility => Vis(_errorSummary.Length > 0);

        public Visibility NameErrorVisibility => Vis(_nameError);

        public Visibility TimeErrorVisibility => Vis(_timeError);

        public Visibility DaysErrorVisibility => Vis(_daysError);

        public Visibility DateErrorVisibility => Vis(_dateError);

        public Visibility VariableErrorVisibility => Vis(_variableError);

        public Visibility ActionsErrorVisibility => Vis(_actionsError);

        /// <summary>校验失败时动作表整体让位给「至少需要一个动作」的虚线块（设计稿口径）</summary>
        public Visibility ActionsTableVisibility => Vis(!_actionsError);

        public bool IsEditable => !_isReadOnly;

        public double FormOpacity => _isReadOnly ? 0.55 : 1.0;

        // ---------------------------------------------------------------- 文本

        public string CountText
            => Schedules.Count == 0 ? "0 条" : $"{Schedules.Count} 条 · {Schedules.Count(schedule => schedule.IsEnabled)} 启用";

        public string NextTriggerLabel => _selected != null && IsEventKind(_selected) ? "执行时机" : "下次触发";

        public string NextTriggerText
        {
            get
            {
                if (_selected == null)
                    return "—";

                if (IsEventKind(_selected))
                {
                    return string.Equals(_selected.Condition, ScadaScheduleConditions.Changed, StringComparison.OrdinalIgnoreCase)
                        ? "值改变后立即执行"
                        : $"满足条件并持续 {_selected.DurationSeconds} 秒后";
                }

                if (!ScadaScheduleClock.TryNextOccurrence(_selected, DateTime.Now, out var next))
                    return "配置不完整，暂不计算";

                var day = next.Date == DateTime.Today ? "今天"
                    : next.Date == DateTime.Today.AddDays(1) ? "明天"
                    : next.ToString("MM-dd");

                var week = "日一二三四五六"[(int)next.DayOfWeek];
                return $"{day} {next:HH:mm}（周{week}）";
            }
        }

        public string ErrorSummary => _errorSummary;

        public bool NameError => _nameError;

        public bool TimeError => _timeError;

        public bool DaysError => _daysError;

        public bool DateError => _dateError;

        public bool VariableError => _variableError;

        public bool ActionsError => _actionsError;

        // ---------------------------------------------------------------- 校验

        /// <summary>第一条问题的文字（旧签名，保留给外部探测）；空串 = 通过</summary>
        public string Validate()
        {
            foreach (var schedule in Schedules)
            {
                var problems = CollectProblems(schedule);
                if (problems.Count > 0)
                    return problems[0].Text;
            }

            return string.Empty;
        }

        /// <summary>
        /// 校验全部计划并就地显示错误。返回 false 时宿主不关窗。
        ///
        /// 为什么校验"全部"而不只是当前选中：计划表是整份方案配置，
        /// 一条没名字/没动作的计划落盘后会在运行态静默跳过（引擎只记一条 Warn），
        /// 现场很难把它和"这条计划没生效"联系起来。发现别的计划有问题时先切到那一条，
        /// 让字段级红框落在用户看得见的地方。
        /// </summary>
        public bool ValidateAndShow()
        {
            ClearErrors();

            if (Schedules.Count == 0)
                return true;

            foreach (var schedule in Schedules)
            {
                var problems = CollectProblems(schedule);
                if (problems.Count == 0)
                    continue;

                if (!ReferenceEquals(schedule, _selected))
                    SelectedSchedule = schedule;

                ApplyErrors(problems);
                return false;
            }

            return true;
        }

        private static List<(string Field, string Text)> CollectProblems(ScadaSchedule schedule)
        {
            var problems = new List<(string Field, string Text)>();

            if (string.IsNullOrWhiteSpace(schedule.Name))
                problems.Add(("name", "名称不能为空"));

            if (IsEventKind(schedule))
            {
                if (string.IsNullOrWhiteSpace(schedule.VariableName))
                    problems.Add(("variable", "变量不能为空"));

                if (schedule.DurationSeconds < 0)
                    problems.Add(("duration", "持续秒数不能为负"));
            }
            else
            {
                if (!ScadaScheduleClock.TryParseTimeOfDay(schedule.TimeOfDay, out _))
                    problems.Add(("time", "时刻用 HH:mm，如 08:00"));

                if (IsPeriod(schedule, ScadaSchedulePeriods.Weekly)
                    && ScadaScheduleClock.ParseDays(schedule.Days).Count == 0)
                {
                    problems.Add(("days", "每周至少选一天"));
                }

                if (IsPeriod(schedule, ScadaSchedulePeriods.Date))
                {
                    if (string.IsNullOrWhiteSpace(schedule.DateText))
                        problems.Add(("date", "指定日期不能为空"));
                    else if (!DateTime.TryParse(schedule.DateText, out _))
                        problems.Add(("date", "日期格式用 yyyy-MM-dd"));
                }
            }

            if (schedule.Actions.Count == 0)
                problems.Add(("actions", "至少需要一个动作"));

            return problems;
        }

        private void ApplyErrors(List<(string Field, string Text)> problems)
        {
            foreach (var problem in problems)
            {
                switch (problem.Field)
                {
                    case "name": _nameError = true; break;
                    case "time": _timeError = true; break;
                    case "days": _daysError = true; break;
                    case "date": _dateError = true; break;
                    case "variable": _variableError = true; break;
                    case "actions": _actionsError = true; break;
                }
            }

            _errorSummary = problems.Count == 1
                ? problems[0].Text
                : $"有 {problems.Count} 处需要修正：{string.Join("、", problems.Select(problem => FieldLabel(problem.Field)))}";

            RaiseAll();
        }

        private static string FieldLabel(string field) => field switch
        {
            "name" => "名称",
            "time" => "时刻",
            "days" => "星期",
            "date" => "日期",
            "variable" => "变量",
            "duration" => "持续",
            "actions" => "动作",
            _ => field,
        };

        private void ClearErrors()
        {
            if (!_nameError && !_timeError && !_daysError && !_dateError && !_variableError
                && !_actionsError && _errorSummary.Length == 0)
            {
                return;
            }

            _nameError = false;
            _timeError = false;
            _daysError = false;
            _dateError = false;
            _variableError = false;
            _actionsError = false;
            _errorSummary = string.Empty;
            RaiseAll();
        }

        // ---------------------------------------------------------------- 刷新

        private void RefreshAll()
        {
            if (!_ready)
                return;

            SyncRadios();
            SyncWeekdays();
            RaiseAll();
        }

        private void RaiseAll()
        {
            Raise(nameof(HasSelectionVisibility));
            Raise(nameof(NoSelectionVisibility));
            Raise(nameof(NoSchedulesVisibility));
            Raise(nameof(DeleteVisibility));
            Raise(nameof(ReadOnlyBannerVisibility));
            Raise(nameof(TimePanelVisibility));
            Raise(nameof(EventPanelVisibility));
            Raise(nameof(WeekPanelVisibility));
            Raise(nameof(DatePanelVisibility));
            Raise(nameof(LimitPanelVisibility));
            Raise(nameof(ErrorVisibility));
            Raise(nameof(NameErrorVisibility));
            Raise(nameof(TimeErrorVisibility));
            Raise(nameof(DaysErrorVisibility));
            Raise(nameof(DateErrorVisibility));
            Raise(nameof(VariableErrorVisibility));
            Raise(nameof(ActionsErrorVisibility));
            Raise(nameof(ActionsTableVisibility));
            Raise(nameof(NameError));
            Raise(nameof(TimeError));
            Raise(nameof(DaysError));
            Raise(nameof(DateError));
            Raise(nameof(VariableError));
            Raise(nameof(ActionsError));
            Raise(nameof(IsEditable));
            Raise(nameof(FormOpacity));
            Raise(nameof(IsReadOnly));
            Raise(nameof(CountText));
            Raise(nameof(NextTriggerLabel));
            Raise(nameof(NextTriggerText));
            Raise(nameof(ErrorSummary));
        }

        private void Raise(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void SyncRadios()
        {
            if (KindTime == null)
                return;

            _syncing = true;
            try
            {
                var isEvent = IsEventKind(_selected);
                KindTime.IsChecked = !isEvent;
                KindEvent.IsChecked = isEvent;

                var period = _selected?.Period ?? ScadaSchedulePeriods.Daily;
                PeriodDaily.IsChecked = string.Equals(period, ScadaSchedulePeriods.Daily, StringComparison.OrdinalIgnoreCase);
                PeriodWeekly.IsChecked = string.Equals(period, ScadaSchedulePeriods.Weekly, StringComparison.OrdinalIgnoreCase);
                PeriodDate.IsChecked = string.Equals(period, ScadaSchedulePeriods.Date, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                _syncing = false;
            }
        }

        private void SyncWeekdays()
        {
            if (WeekChip1 == null)
                return;

            var days = _selected == null
                ? new List<int>()
                : ScadaScheduleClock.ParseDays(_selected.Days).Select(ScadaScheduleClock.ToIsoDay).ToList();

            _syncing = true;
            try
            {
                foreach (var chip in WeekdayChips())
                {
                    var iso = int.TryParse(chip.Tag as string, out var value) ? value : 0;
                    chip.IsChecked = days.Contains(iso);
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private void SyncSelection()
        {
            if (!_ready)
                return;

            _syncing = true;
            try
            {
                if (!ReferenceEquals(ScheduleList.SelectedItem, _selected))
                    ScheduleList.SelectedItem = _selected;
            }
            finally
            {
                _syncing = false;
            }
        }

        private IEnumerable<ToggleButton> WeekdayChips()
        {
            yield return WeekChip1;
            yield return WeekChip2;
            yield return WeekChip3;
            yield return WeekChip4;
            yield return WeekChip5;
            yield return WeekChip6;
            yield return WeekChip7;
        }

        private void RebuildActions()
        {
            Actions.Clear();

            if (_selected?.FindEventHook(ScadaEventType.ScheduleFired) is not { } hook)
                return;

            foreach (var action in hook.Actions)
                Actions.Add(ScheduleActionRow.FromAction(action));

            RefreshIndexes();
        }

        private void RefreshIndexes()
        {
            for (var index = 0; index < Actions.Count; index++)
                Actions[index].Index = index + 1;
        }

        private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

        private static bool IsEventKind(ScadaSchedule? schedule)
            => schedule != null && string.Equals(schedule.Kind, ScadaScheduleKinds.Event, StringComparison.OrdinalIgnoreCase);

        private static bool IsPeriod(ScadaSchedule schedule, string period)
            => string.Equals(schedule.Period, period, StringComparison.OrdinalIgnoreCase);

        private static bool IsLimitCondition(string? condition)
            => string.Equals(condition, ScadaScheduleConditions.AboveLimit, StringComparison.OrdinalIgnoreCase)
               || string.Equals(condition, ScadaScheduleConditions.BelowLimit, StringComparison.OrdinalIgnoreCase);

        // ---------------------------------------------------------------- 模型事件

        private void OnSelectedPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_ready)
                return;

            var name = e.PropertyName;

            if (name == nameof(ScadaSchedule.Days))
                SyncWeekdays();

            if (name == nameof(ScadaSchedule.Kind) || name == nameof(ScadaSchedule.Period))
                SyncRadios();

            if (name is nameof(ScadaSchedule.Name) or nameof(ScadaSchedule.TimeOfDay) or nameof(ScadaSchedule.Days)
                or nameof(ScadaSchedule.DateText) or nameof(ScadaSchedule.VariableName) or nameof(ScadaSchedule.Condition)
                or nameof(ScadaSchedule.DurationSeconds) or nameof(ScadaSchedule.Kind) or nameof(ScadaSchedule.Period))
            {
                ClearErrors();
            }

            RaiseAll();
        }

        private void OnAnySchedulePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_ready)
                return;

            if (e.PropertyName is nameof(ScadaSchedule.IsEnabled) or nameof(ScadaSchedule.Name))
                Raise(nameof(CountText));
        }

        private void OnSchedulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (ScadaSchedule schedule in e.OldItems)
                    schedule.PropertyChanged -= OnAnySchedulePropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (ScadaSchedule schedule in e.NewItems)
                    schedule.PropertyChanged += OnAnySchedulePropertyChanged;
            }

            Raise(nameof(CountText));
            Raise(nameof(NoSchedulesVisibility));

            if (_selected != null && !Schedules.Contains(_selected))
                SelectedSchedule = Schedules.FirstOrDefault();
            else if (_selected == null && Schedules.Count > 0)
                SelectedSchedule = Schedules[0];
        }

        // ---------------------------------------------------------------- 交互

        private void OnScheduleSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing)
                return;

            SelectedSchedule = ScheduleList.SelectedItem as ScadaSchedule;
        }

        private void OnKindChecked(object sender, RoutedEventArgs e)
        {
            if (_syncing || !_ready || _selected == null)
                return;

            if (sender is not FrameworkElement element || element.Tag is not string kind)
                return;

            if (string.Equals(_selected.Kind, kind, StringComparison.OrdinalIgnoreCase))
                return;

            _selected.Kind = kind;
        }

        private void OnPeriodChecked(object sender, RoutedEventArgs e)
        {
            if (_syncing || !_ready || _selected == null)
                return;

            if (sender is not FrameworkElement element || element.Tag is not string period)
                return;

            if (string.Equals(_selected.Period, period, StringComparison.OrdinalIgnoreCase))
                return;

            _selected.Period = period;
        }

        private void OnWeekdayClick(object sender, RoutedEventArgs e)
        {
            if (_syncing || !_ready || _selected == null)
                return;

            var days = new List<int>();
            foreach (var chip in WeekdayChips())
            {
                if (chip.IsChecked == true
                    && int.TryParse(chip.Tag as string, out var iso)
                    && iso >= 1 && iso <= 7)
                {
                    days.Add(iso);
                }
            }

            days.Sort();
            _selected.Days = string.Join(",", days);
        }

        private void OnConditionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || !_ready)
                return;

            // 只刷新「上限/下限」的显隐，**不清错误**：
            // 事件块即使 Visibility=Collapsed，里面的 ComboBox 仍会在布局期完成一次绑定，
            // 那一刻 SelectionChanged 就会触发——从这里 ClearErrors 会把刚校验出来的红框
            // 在弹窗显示前全抹掉（R35：不能从"值变了"倒推用户改过）。
            Raise(nameof(LimitPanelVisibility));
        }

        private void OnAddSchedule(object sender, RoutedEventArgs e)
        {
            if (_isReadOnly)
                return;

            var schedule = new ScadaSchedule
            {
                Name = "新计划 " + (Schedules.Count + 1),
                Kind = ScadaScheduleKinds.Time,
                Period = ScadaSchedulePeriods.Daily,
                TimeOfDay = "08:00",
            };

            schedule.GetOrAddEventHook(ScadaEventType.ScheduleFired)
                .Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "计划触发" });

            Schedules.Add(schedule);
            SelectedSchedule = schedule;
        }

        private void OnDeleteSchedule(object sender, RoutedEventArgs e)
        {
            if (_isReadOnly || _selected == null)
                return;

            var index = Schedules.IndexOf(_selected);
            Schedules.Remove(_selected);

            SelectedSchedule = Schedules.Count == 0
                ? null
                : Schedules[Math.Clamp(index, 0, Schedules.Count - 1)];
        }

        private void OnAddAction(object sender, RoutedEventArgs e)
        {
            if (_isReadOnly)
                return;

            if (_selected?.FindEventHook(ScadaEventType.ScheduleFired) is not { } hook)
                return;

            var action = new ScadaAction { Type = ScadaActionType.Log, Text = "计划触发" };
            hook.Actions.Add(action);
            Actions.Add(ScheduleActionRow.FromAction(action));
            RefreshIndexes();
        }

        private void OnRemoveAction(object sender, RoutedEventArgs e)
        {
            if (_isReadOnly)
                return;

            if (ActionRowOf(sender) is not { } row)
                return;

            var index = Actions.IndexOf(row);
            Actions.Remove(row);
            _selected?.FindEventHook(ScadaEventType.ScheduleFired)?.Actions.Remove(row.Source);
            RefreshIndexes();

            if (index >= 0)
                Raise(nameof(ActionsErrorVisibility));
        }

        private void OnMoveActionUp(object sender, RoutedEventArgs e) => MoveAction(sender, -1);

        private void OnMoveActionDown(object sender, RoutedEventArgs e) => MoveAction(sender, +1);

        private void MoveAction(object sender, int delta)
        {
            if (_isReadOnly)
                return;

            if (ActionRowOf(sender) is not { } row)
                return;

            MoveActionAt(Actions.IndexOf(row), delta);
        }

        /// <summary>把第 index 行动作上移 / 下移一格（delta = -1 / +1），并重排行号</summary>
        public bool MoveActionAt(int index, int delta)
        {
            if (_isReadOnly || _selected == null)
                return false;

            var actions = _selected.FindEventHook(ScadaEventType.ScheduleFired)?.Actions;
            if (actions == null)
                return false;

            var target = index + delta;
            if (index < 0 || index >= Actions.Count || target < 0 || target >= Actions.Count)
                return false;

            Actions.Move(index, target);
            actions.Move(index, target);
            RefreshIndexes();
            return true;
        }

        private void OnActionTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _isReadOnly)
                return;

            if (ActionRowOf(sender) is { } row)
                row.ApplyTypeDefaults();
        }

        private static ScheduleActionRow? ActionRowOf(object sender)
            => (sender as FrameworkElement)?.DataContext as ScheduleActionRow;
    }

    /// <summary>
    /// 动作表里的一行（包装一条 <see cref="ScadaAction"/>）。
    ///
    /// 两个输入框按动作类型换语义：<see cref="Primary"/> 是"引用谁"（画面名/变量名/流程名/配方名/档位/文本），
    /// <see cref="Secondary"/> 只有写变量 / 语音播报用得上（值 / 档位）。
    /// 用不上的类型在"参数"列显示「—」而不是一个空输入框——空框会被读成"这里必须填"。
    /// </summary>
    public sealed class ScheduleActionRow : BindableBase
    {
        private string _type = nameof(ScadaActionType.Log);
        private string _primary = string.Empty;
        private string _secondary = string.Empty;
        private int _index;

        public ScheduleActionRow() { }

        /// <summary>对应的模型对象（删/排序时用）</summary>
        public ScadaAction Source { get; private set; } = new();

        /// <summary>行号（1 起；增删/调序后由视图重排）</summary>
        public int Index
        {
            get => _index;
            set => SetProperty(ref _index, value);
        }

        public string Type
        {
            get => _type;
            set
            {
                if (!SetProperty(ref _type, value ?? nameof(ScadaActionType.Log)))
                    return;

                RaisePropertyChanged(nameof(PrimaryHint));
                RaisePropertyChanged(nameof(SecondaryHint));
                RaisePropertyChanged(nameof(SecondaryVisibility));
                RaisePropertyChanged(nameof(SecondaryPlaceholderVisibility));
            }
        }

        public string Primary
        {
            get => _primary;
            set
            {
                if (!SetProperty(ref _primary, value ?? string.Empty))
                    return;

                Write();
            }
        }

        public string Secondary
        {
            get => _secondary;
            set
            {
                if (!SetProperty(ref _secondary, value ?? string.Empty))
                    return;

                Write();
            }
        }

        public string PrimaryHint => Type switch
        {
            nameof(ScadaActionType.Navigate) => "目标画面名（与本页画面名一致）",
            nameof(ScadaActionType.WriteVariable) => "变量名（与工程变量同名）",
            nameof(ScadaActionType.StartFlow) or nameof(ScadaActionType.StopFlow) => "流程名",
            nameof(ScadaActionType.ApplyRecipe) => "配方名",
            nameof(ScadaActionType.PlaySound) => "Info / Warning / Critical",
            nameof(ScadaActionType.SpeakText) => "播报文本（可写 {变量名} 按当前值替换）",
            _ => "日志文本",
        };

        public string SecondaryHint => Type switch
        {
            nameof(ScadaActionType.WriteVariable) => "写入的值（数字/文本/True/False）",
            nameof(ScadaActionType.SpeakText) => "档位 Info / Warning / Critical（严重会打断正在念的低级别）",
            _ => string.Empty,
        };

        public Visibility SecondaryVisibility
            => Type == nameof(ScadaActionType.WriteVariable) || Type == nameof(ScadaActionType.SpeakText)
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>「参数」列对不适用的类型显示「—」（保留列宽，避免行高跳动）</summary>
        public Visibility SecondaryPlaceholderVisibility
            => SecondaryVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        public static ScheduleActionRow FromAction(ScadaAction action)
        {
            var row = new ScheduleActionRow { Source = action, _type = action.Type.ToString() };

            switch (action.Type)
            {
                case ScadaActionType.Navigate:
                    row._primary = action.TargetPageName ?? string.Empty;
                    break;
                case ScadaActionType.WriteVariable:
                    row._primary = action.VariableName ?? string.Empty;
                    row._secondary = action.Value ?? string.Empty;
                    break;
                case ScadaActionType.StartFlow:
                case ScadaActionType.StopFlow:
                    row._primary = action.FlowName ?? string.Empty;
                    break;
                case ScadaActionType.ApplyRecipe:
                    row._primary = action.TargetRecipeName ?? string.Empty;
                    break;
                case ScadaActionType.PlaySound:
                    row._primary = action.SoundKind;
                    break;
                case ScadaActionType.SpeakText:
                    row._primary = action.Text ?? string.Empty;
                    row._secondary = action.SoundKind;
                    break;
                default:
                    row._primary = action.Text ?? string.Empty;
                    break;
            }

            return row;
        }

        /// <summary>换类型时把输入框清成该类型的默认值（不把上一类的文字留在新语义里）</summary>
        public void ApplyTypeDefaults()
        {
            if (Type == nameof(ScadaActionType.PlaySound))
            {
                _primary = string.IsNullOrWhiteSpace(_primary) ? "Info" : _primary;
                _secondary = string.Empty;
            }

            if (Type == nameof(ScadaActionType.SpeakText))
            {
                _secondary = string.IsNullOrWhiteSpace(_secondary) ? "Info" : _secondary;
            }

            RaisePropertyChanged(nameof(Primary));
            RaisePropertyChanged(nameof(Secondary));
            RaisePropertyChanged(nameof(SecondaryVisibility));
            RaisePropertyChanged(nameof(SecondaryPlaceholderVisibility));
            Write();
        }

        private void Write()
        {
            if (!Enum.TryParse<ScadaActionType>(Type, ignoreCase: true, out var type))
                type = ScadaActionType.Log;

            Source.Type = type;

            switch (type)
            {
                case ScadaActionType.Navigate:
                    Source.TargetPageName = Primary;
                    break;
                case ScadaActionType.WriteVariable:
                    Source.VariableName = Primary;
                    Source.Value = Secondary;
                    break;
                case ScadaActionType.StartFlow:
                case ScadaActionType.StopFlow:
                    Source.FlowName = Primary;
                    break;
                case ScadaActionType.ApplyRecipe:
                    Source.TargetRecipeName = Primary;
                    break;
                case ScadaActionType.PlaySound:
                    Source.SoundKind = string.IsNullOrWhiteSpace(Primary) ? "Info" : Primary;
                    break;
                case ScadaActionType.SpeakText:
                    Source.Text = Primary;
                    Source.SoundKind = string.IsNullOrWhiteSpace(Secondary) ? "Info" : Secondary;
                    break;
                default:
                    Source.Text = Primary;
                    break;
            }
        }
    }
}
