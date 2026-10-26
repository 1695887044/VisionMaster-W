using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Prism.Mvvm;
using VisionMaster.Scada;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 计划表管理器：左列计划清单 + 右列编辑表单。
    ///
    /// 编辑的是<b>活对象</b>（直接写 <see cref="ScadaDocument.Schedules"/>），所以：
    /// · 打开前由调用方开一个变更作用域、并留一份 JSON 备份；
    /// · 「取消」时由调用方把备份写回并作废那次作用域（见 ScadaEditorViewModel.OpenSchedules）。
    ///
    /// 动作编辑器是<b>按名字引用</b>的简化版（跳页 / 写变量 / 启停流程 / 应用配方 / 播放声音 / 日志）：
    /// 完整的 27 种动作编辑器在属性面板里，那一套要变量选择器、画面选择器、轴选择器；
    /// 计划表 v1 先用"填名字"覆盖现场最常用的六种，避免把整套选择器搬进这个弹窗。
    /// </summary>
    public partial class ScadaScheduleManagerView : UserControl, System.ComponentModel.INotifyPropertyChanged
    {
        private ScadaSchedule? _selected;
        private bool _syncing;

        public ScadaScheduleManagerView() : this(new ScadaDocument()) { }

        public ScadaScheduleManagerView(ScadaDocument document)
        {
            InitializeComponent();

            Document = document ?? throw new ArgumentNullException(nameof(document));
            DataContext = this;

            SelectedSchedule = Schedules.FirstOrDefault();
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        /// <summary>方案文档（列表直接绑它的 <see cref="ScadaDocument.Schedules"/>）</summary>
        public ScadaDocument Document { get; }

        public ObservableCollection<ScadaSchedule> Schedules => Document.Schedules;

        /// <summary>当前选中的计划</summary>
        public ScadaSchedule? SelectedSchedule
        {
            get => _selected;
            set
            {
                if (ReferenceEquals(_selected, value))
                    return;

                _selected = value;
                Raise(nameof(SelectedSchedule));

                RebuildActions();
                RefreshPanels();

                if (ScheduleList.SelectedItem != value)
                    ScheduleList.SelectedItem = value;
            }
        }

        /// <summary>当前计划的动作行（每行包一条 <see cref="ScadaAction"/>）</summary>
        public ObservableCollection<ScheduleActionRow> Actions { get; } = new();

        public Visibility TimePanelVisibility
            => _selected != null && !IsEventKind(_selected) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility EventPanelVisibility
            => _selected != null && IsEventKind(_selected) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility LimitPanelVisibility
            => _selected is { } schedule
               && (schedule.Condition == ScadaScheduleConditions.AboveLimit
                   || schedule.Condition == ScadaScheduleConditions.BelowLimit)
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>校验：返回空串表示通过；否则是给用户看的一句话</summary>
        public string Validate()
        {
            foreach (var schedule in Schedules)
            {
                var name = string.IsNullOrWhiteSpace(schedule.Name) ? "（未命名）" : schedule.Name;

                if (string.IsNullOrWhiteSpace(schedule.Name))
                    return "计划名称不能为空。";

                if (IsEventKind(schedule))
                {
                    if (string.IsNullOrWhiteSpace(schedule.VariableName))
                        return $"「{name}」还没填变量名。";

                    if (schedule.DurationSeconds < 0)
                        return $"「{name}」的持续秒数不能为负。";
                }
                else
                {
                    if (!ScadaScheduleClock.TryParseTimeOfDay(schedule.TimeOfDay, out _))
                        return $"「{name}」的时刻「{schedule.TimeOfDay}」看不懂，用 HH:mm（如 08:00）。";

                    if (schedule.Period == ScadaSchedulePeriods.Weekly
                        && ScadaScheduleClock.ParseDays(schedule.Days).Count == 0)
                    {
                        return $"「{name}」选了「每周」但没选星期（1=周一 … 7=周日）。";
                    }

                    if (schedule.Period == ScadaSchedulePeriods.Date
                        && string.IsNullOrWhiteSpace(schedule.DateText))
                    {
                        return $"「{name}」选了「指定日期」但没填日期。";
                    }
                }

                if (schedule.Actions.Count == 0)
                    return $"「{name}」至少要有一个动作。";
            }

            return string.Empty;
        }

        /// <summary>校验并把错误显示在表单底部</summary>
        public bool ValidateAndShow()
        {
            var error = Validate();
            ErrorText.Text = error;
            return error.Length == 0;
        }

        private static bool IsEventKind(ScadaSchedule schedule)
            => string.Equals(schedule.Kind, ScadaScheduleKinds.Event, StringComparison.OrdinalIgnoreCase);

        private void RefreshPanels()
        {
            Raise(nameof(TimePanelVisibility));
            Raise(nameof(EventPanelVisibility));
            Raise(nameof(LimitPanelVisibility));
        }

        private void RebuildActions()
        {
            Actions.Clear();

            if (_selected?.FindEventHook(ScadaEventType.ScheduleFired) is not { } hook)
                return;

            foreach (var action in hook.Actions)
                Actions.Add(ScheduleActionRow.FromAction(action));
        }

        private void Raise(string name)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        // ---- 事件 ----

        private void OnScheduleSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing)
                return;

            SelectedSchedule = ScheduleList.SelectedItem as ScadaSchedule;
        }

        private void OnKindChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing)
                return;

            RefreshPanels();
        }

        private void OnConditionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing)
                return;

            Raise(nameof(LimitPanelVisibility));
        }

        private void OnAddSchedule(object sender, RoutedEventArgs e)
        {
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

            _syncing = true;
            SelectedSchedule = schedule;
            _syncing = false;
        }

        private void OnDeleteSchedule(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
                return;

            var index = Schedules.IndexOf(_selected);
            Schedules.Remove(_selected);
            SelectedSchedule = Schedules.Count == 0 ? null : Schedules[Math.Clamp(index, 0, Schedules.Count - 1)];
        }

        private void OnAddAction(object sender, RoutedEventArgs e)
        {
            if (_selected?.FindEventHook(ScadaEventType.ScheduleFired) is not { } hook)
                return;

            var action = new ScadaAction { Type = ScadaActionType.Log, Text = "计划触发" };
            hook.Actions.Add(action);
            Actions.Add(ScheduleActionRow.FromAction(action));
        }

        private void OnRemoveAction(object sender, RoutedEventArgs e)
        {
            if (ActionRowOf(sender) is not { } row)
                return;

            var index = Actions.IndexOf(row);
            Actions.Remove(row);
            _selected?.FindEventHook(ScadaEventType.ScheduleFired)?.Actions.Remove(row.Source);
        }

        private void OnMoveActionUp(object sender, RoutedEventArgs e) => MoveAction(sender, -1);

        private void OnMoveActionDown(object sender, RoutedEventArgs e) => MoveAction(sender, +1);

        private void MoveAction(object sender, int delta)
        {
            if (ActionRowOf(sender) is not { } row)
                return;

            var actions = _selected?.FindEventHook(ScadaEventType.ScheduleFired)?.Actions;
            if (actions == null)
                return;

            var index = Actions.IndexOf(row);
            var target = index + delta;

            if (index < 0 || target < 0 || target >= Actions.Count)
                return;

            Actions.Move(index, target);
            actions.Move(index, target);
        }

        private void OnActionTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing)
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
    /// <see cref="Secondary"/> 只有写变量用得上（值）。写成两个文本框而不是每类一个编辑器：
    /// 计划表里一条动作就一行，字段一多整块就没法扫读。
    /// </summary>
    public sealed class ScheduleActionRow : BindableBase
    {
        private string _type = nameof(ScadaActionType.Log);
        private string _primary = string.Empty;
        private string _secondary = string.Empty;

        public ScheduleActionRow() { }

        /// <summary>对应的模型对象（删/排序时用）</summary>
        public ScadaAction Source { get; private set; } = new();

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
