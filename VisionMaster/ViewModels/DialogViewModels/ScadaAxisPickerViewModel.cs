using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Core.Interfaces;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 组态「选择轴」弹窗：从工程里**已启用**的轴中多选（或单选），确认后回传有序的
    /// <see cref="ScadaAxisRef"/> 列表（Id + 当时的名字）。
    ///
    /// 三条口径
    /// ---------
    /// ① 候选只列已启用的轴：未启用的轴在流程/面板里用不了，列出来等于诱导用户选一个必然失败的名字
    ///    （与 MotionModule.RegisterStepConfigOptions 的轴名候选同一口径）。
    /// ② 身份是 AxisId：确认后按 Id 记录，轴改名不会让已配好的面板断链。
    /// ③ 顺序即下拉顺序：已选的轴排在前面（保持用户原来的顺序），其余按名字排序补在后面。
    ///
    /// 单选模式（<see cref="ScadaAxisPicker.SingleSelectKey"/>）
    /// ---------
    /// 组态动作表里的运动命令只操作一根轴，所以那里走单选。单选与多选<b>共用一个弹窗</b>，
    /// 差别只有"勾一行要不要取消其它行"——候选来源、预选口径、搜索、空表提示全部相同。
    /// 互斥做在<b>勾选这一侧</b>（而不是"确认时只取第一项"）：界面上必须看得出只能选一个，
    /// 否则用户会勾三根、看到只动了一根，而且没有任何报错。
    ///
    /// 取消不产生任何回写（与变量/画面选择器同一语义）。
    /// </summary>
    public class ScadaAxisPickerViewModel : BindableBase, IDialogAware
    {
        private readonly MotionAxisRegistry _registry;

        /// <summary>全量清单（打开那一刻的快照）；<see cref="Axes"/> 是它在关键字过滤下的投影</summary>
        private readonly List<ScadaAxisRow> _all = new();

        private string _keyword = string.Empty;

        /// <summary>本次是否为单选（由打开参数决定；每次打开重设，不跨次残留）</summary>
        private bool _singleSelect;

        public ScadaAxisPickerViewModel(MotionAxisRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));

            ConfirmCommand = new DelegateCommand(OnConfirm, () => _all.Any(a => a.IsSelected))
                .ObservesProperty(() => Axes.Count);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        public DialogCloseListener RequestClose { get; set; }

        /// <summary>
        /// 弹窗标题。单选与多选都叫「选择轴」：两者选的是同一种东西，
        /// 在标题上区分反而会让用户以为打开了另一个功能（真正的区别在"勾一行会取消其它行"）。
        /// </summary>
        public string Title => "选择轴";

        /// <summary>本次是否单选（模板可据此换提示文案；行为上的互斥已由 <see cref="OnRowChanged"/> 保证）</summary>
        public bool IsSingleSelect => _singleSelect;

        /// <summary>过滤后的可见清单（关键字为空即全量）</summary>
        public ObservableCollection<ScadaAxisRow> Axes { get; } = new();

        /// <summary>搜索关键字：匹配轴名 / 卡名，大小写不敏感</summary>
        public string Keyword
        {
            get => _keyword;
            set
            {
                if (SetProperty(ref _keyword, value ?? string.Empty))
                    ApplyFilter();
            }
        }

        public string EmptyHint =>
            _all.Count == 0
                ? "当前方案里还没有已启用的轴。请先到「运动卡设置」配置轴并勾选启用，再回来选择。"
                : Axes.Count == 0
                    ? $"没有匹配「{Keyword}」的轴。"
                    : string.Empty;

        public bool HasAxes => Axes.Count > 0;

        public DelegateCommand ConfirmCommand { get; }

        public DelegateCommand CancelCommand { get; }

        private void ApplyFilter()
        {
            string keyword = _keyword.Trim();

            Axes.Clear();
            foreach (var row in _all)
            {
                if (keyword.Length == 0 || row.Matches(keyword))
                    Axes.Add(row);
            }

            RaisePropertyChanged(nameof(EmptyHint));
            RaisePropertyChanged(nameof(HasAxes));
            ConfirmCommand.RaiseCanExecuteChanged();
        }

        private void OnConfirm()
        {
            // 从 _all 而不是 Axes 取：搜索框把某一行过滤掉时，它仍然是"已选"的，
            // 按可见清单取会把用户看不见的选择悄悄丢掉。
            var picked = _all.Where(a => a.IsSelected)
                             .Select(a => new ScadaAxisRef(a.AxisId, a.Name))
                             .ToList();

            if (picked.Count == 0)
                return;

            var parameters = new DialogParameters
            {
                { ScadaAxisPicker.PickedAxesKey, picked },
            };
            RequestClose.Invoke(parameters, ButtonResult.OK);
        }

        #region IDialogAware

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        /// <summary>
        /// 打开时现取一份轴表快照并按入参预选。
        /// 每次打开都重新取：两次打开之间用户可能去运动卡设置里增删/改名/停用过轴。
        /// </summary>
        public void OnDialogOpened(IDialogParameters parameters)
        {
            _all.Clear();

            // 模式位每次打开都重设：两份调用（多选的面板、单选的动作用同一个弹窗实例注册）
            // 共用同一个 VM 类型，靠字段残留会让"上次单选、这次多选"变成单选。
            parameters.TryGetValue(ScadaAxisPicker.SingleSelectKey, out bool singleSelect);
            _singleSelect = singleSelect;
            RaisePropertyChanged(nameof(IsSingleSelect));

            parameters.TryGetValue<List<ScadaAxisRef>>(ScadaAxisPicker.CurrentAxesKey, out var current);
            current ??= new List<ScadaAxisRef>();

            var bindings = new Dictionary<Guid, MotionAxisBinding>();
            foreach (var binding in _registry.Snapshot().Where(b => b.Enabled))
            {
                // 历史方案的重复物理轴号会得到同一个确定性 AxisId（畸形配置，注册表只报问题不修）。
                // 这里用 TryAdd 先到者胜，绝不让 ToDictionary 抛异常把弹窗打死。
                bindings.TryAdd(binding.AxisId, binding);
            }

            var selectedIds = new HashSet<Guid>(current.Select(c => c.AxisId).Where(id => id != Guid.Empty));

            // ① 已选的轴排前面，保持用户原来的顺序；找不到的（轴已被删/停用）也留一行，
            //    让用户看得见"这条已经失效"，并能主动取消勾选。
            foreach (var item in current)
            {
                if (item == null || item.AxisId == Guid.Empty) continue;

                if (bindings.TryGetValue(item.AxisId, out var binding))
                {
                    _all.Add(new ScadaAxisRow(
                        item.AxisId, binding.Name, binding.CardCaption, true, isSelected: true));
                }
                else
                {
                    _all.Add(new ScadaAxisRow(
                        item.AxisId, item.Name, "（轴已移除或已停用）", false, isSelected: true));
                }
            }

            // ② 其余已启用的轴按名字补在后面（顺序稳定，不随字典遍历顺序漂移）。
            foreach (var binding in bindings.Values
                         .Where(b => !selectedIds.Contains(b.AxisId))
                         .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
            {
                _all.Add(new ScadaAxisRow(
                    binding.AxisId, binding.Name, binding.CardCaption, true, isSelected: false));
            }

            if (_keyword.Length != 0)
            {
                _keyword = string.Empty;
                RaisePropertyChanged(nameof(Keyword));
            }

            // 单选：入参若带了多根（历史数据 / 调用方传错），只留第一根。
            // 不做这一步的话，界面上会看到两行都勾着、"确定"也亮着，而回传只会取第一根——
            // 用户以为选了两根、实际只动了一根，且没有任何提示。
            if (_singleSelect)
            {
                var first = _all.FirstOrDefault(a => a.IsSelected);
                if (first != null) UnselectOthers(first);
            }

            // 勾选状态变化要让「确定」重新算可用性：命令的谓词读的是 Axes 里的 IsSelected，
            // 而 ObservedProperties 只盯集合数量，盯不到行内属性（与属性面板 ClearBinding 同一条坑）。
            foreach (var row in _all)
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(ScadaAxisRow.IsSelected)) return;

                    // 单选：勾上一行就把其它行取消掉。放在"勾选这一侧"而不是确认时取第一项，
                    // 是为了让界面上当场看得见"只能选一个"（见类注释）。
                    if (_singleSelect && row.IsSelected)
                        UnselectOthers(row);

                    ConfirmCommand.RaiseCanExecuteChanged();
                };

            ApplyFilter();
        }

        /// <summary>
        /// 单选互斥：把除 <paramref name="keep"/> 以外的行全部取消勾选。
        ///
        /// 这里刻意用"逐个写 IsSelected=false"而不是"清空后重建清单"：
        /// 重建会让搜索框的过滤状态与滚动位置一起丢失（用户点第二根轴时画面跳回顶部），
        /// 而逐行改只触发一次属性变更，界面只动那一格。
        /// </summary>
        private void UnselectOthers(ScadaAxisRow keep)
        {
            foreach (var row in _all)
            {
                if (ReferenceEquals(row, keep) || !row.IsSelected) continue;

                row.IsSelected = false;
            }
        }

        #endregion
    }

    /// <summary>
    /// 清单里的一行。勾选状态是弹窗内的草稿，确认之前不回写任何模型。
    /// </summary>
    public sealed class ScadaAxisRow : BindableBase
    {
        private bool _isSelected;

        public ScadaAxisRow(Guid axisId, string name, string cardCaption, bool isAvailable, bool isSelected)
        {
            AxisId = axisId;
            Name = name ?? string.Empty;
            CardCaption = cardCaption ?? string.Empty;
            IsAvailable = isAvailable;
            _isSelected = isSelected;
        }

        public Guid AxisId { get; }

        public string Name { get; }

        public string CardCaption { get; }

        public bool IsAvailable { get; }

        /// <summary>显示名：不可用的行在名字后点明原因，避免用户以为选上了就能用</summary>
        public string DisplayName => IsAvailable ? Name : $"{Name} {CardCaption}".Trim();

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool Matches(string keyword) =>
            Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || CardCaption.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
