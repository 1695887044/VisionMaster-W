using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 组态「选择变量」弹窗：从工程变量清单里挑一个，确认后回传 (变量 Id, 变量名)。
    ///
    /// 三条口径（与运行态寻址保持一致，避免"选得到却写不进"这类错位）
    /// ---------
    /// ① **清单就是可寻址的那一份**：只列 <c>IWorkspaceManager.GlobalVariables</c>。
    ///    运行态 <c>IScadaValueSource</c> 背后的变量注册表正是这份集合的镜像（见 VariableRegistry），
    ///    所以这里列出来的每一条，运行态都解析得到；反过来不在清单里的，选了也是白选。
    /// ② **Id 是权威**：确认时回传的是 <c>VariableId</c>（外加当时的名字，仅作旧数据兜底与展示）。
    ///    变量改名不会让已配好的动作断链——名字只是"快照"，Id 才是锚点。
    /// ③ **预选走 Id 优先、名字兜底**：与 <c>IVariableRegistry.Resolve</c> 同一顺序，
    ///    让"面板上看到的是哪个变量"和"运行时会写到哪个变量"永远对得上。
    ///
    /// 取消语义：本弹窗不产生任何回写（<see cref="ScadaVariablePicker.Pick"/> 只在 OK 时回调），
    /// 所以"取消"不会把已配的变量清掉——那是破坏性动作，不能藏在取消键里。
    /// </summary>
    public class ScadaVariablePickerViewModel : BindableBase, IDialogAware
    {
        private readonly IWorkspaceManager _workspace;

        /// <summary>全量清单（打开那一刻的快照）；<see cref="Variables"/> 是它在关键字过滤下的投影</summary>
        private readonly List<ScadaVariableRow> _all = new();

        private string _keyword = string.Empty;
        private ScadaVariableRow? _selectedVariable;

        public ScadaVariablePickerViewModel(IWorkspaceManager workspace)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

            ConfirmCommand = new DelegateCommand(OnConfirm, () => SelectedVariable != null)
                .ObservesProperty(() => SelectedVariable);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        public DialogCloseListener RequestClose { get; set; }

        public string Title => "选择变量";

        /// <summary>过滤后的可见清单（关键字为空即全量）</summary>
        public ObservableCollection<ScadaVariableRow> Variables { get; } = new();

        /// <summary>搜索关键字：匹配 变量名 / 类型 / 来源 / 说明，大小写不敏感</summary>
        public string Keyword
        {
            get => _keyword;
            set
            {
                if (SetProperty(ref _keyword, value ?? string.Empty))
                    ApplyFilter();
            }
        }

        /// <summary>当前选中项（未选时确认键不可用）</summary>
        public ScadaVariableRow? SelectedVariable
        {
            get => _selectedVariable;
            set => SetProperty(ref _selectedVariable, value);
        }

        /// <summary>列表为空时的提示文案（有内容时为空串）</summary>
        public string EmptyHint =>
            _all.Count == 0
                ? "当前方案里还没有变量。请先在「全局变量」里创建，再回来配置这条动作。"
                : Variables.Count == 0
                    ? $"没有匹配「{Keyword}」的变量。"
                    : string.Empty;

        /// <summary>列表是否有内容（空态提示靠它显隐）</summary>
        public bool HasVariables => Variables.Count > 0;

        public DelegateCommand ConfirmCommand { get; }

        public DelegateCommand CancelCommand { get; }

        /// <summary>
        /// 重建可见清单。
        /// 选中项的处理：原来选的那条若还在结果里就留着，否则落到第一条——
        /// 商业软件的搜索框都是这个手感（一边打字一边给一个"当前候选"），
        /// 而不是打完字还要再点一下才能确认。
        /// </summary>
        private void ApplyFilter()
        {
            var keep = _selectedVariable;
            var keyword = _keyword.Trim();

            Variables.Clear();
            foreach (var row in _all)
            {
                if (keyword.Length == 0 || row.Matches(keyword))
                    Variables.Add(row);
            }

            SelectedVariable =
                keep != null && Variables.Contains(keep) ? keep : Variables.FirstOrDefault();

            RaisePropertyChanged(nameof(EmptyHint));
            RaisePropertyChanged(nameof(HasVariables));
        }

        /// <summary>确认：回传 (Id, 名字)。Id 拿不到就什么也不做（不许退化成"按名字猜一个"）</summary>
        private void OnConfirm()
        {
            var row = SelectedVariable;
            if (row == null || row.VariableId == Guid.Empty)
                return;

            var parameters = new DialogParameters
            {
                { ScadaVariablePicker.PickedIdKey, row.VariableId },
                { ScadaVariablePicker.PickedNameKey, row.Name },
            };
            RequestClose.Invoke(parameters, ButtonResult.OK);
        }

        #region IDialogAware

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        /// <summary>
        /// 打开时：现取一份变量清单快照，并按入参预选。
        /// 每次打开都重新取，是因为两次打开之间用户可能已经去变量管理里增删过变量；
        /// 缓存在字段里跨弹窗复用，就会给出"清单里没有它，却怎么也选不上"的幽灵条目。
        /// </summary>
        public void OnDialogOpened(IDialogParameters parameters)
        {
            _all.Clear();
            foreach (var variable in _workspace.GlobalVariables)
            {
                if (variable != null)
                    _all.Add(new ScadaVariableRow(variable));
            }

            // 关键字归零 + 无条件重建一次清单。
            //
            // 这里**不能**只写 `Keyword = string.Empty` 指望 setter 顺带刷新：首次打开时
            // _keyword 本来就是空串，SetProperty 判定"值没变"会直接返回，ApplyFilter 压根不跑，
            // 于是 _all 装满了变量而 Variables 恒空、界面显示"当前方案里还没有变量"（真机踩过）。
            // 把"清单投影"从 setter 的副作用里解耦出来，是这一段的唯一要点。
            if (_keyword.Length != 0)
            {
                _keyword = string.Empty;
                RaisePropertyChanged(nameof(Keyword));
            }
            ApplyFilter();

            parameters.TryGetValue<Guid>(ScadaVariablePicker.CurrentIdKey, out var currentId);
            parameters.TryGetValue<string>(ScadaVariablePicker.CurrentNameKey, out var currentName);

            SelectedVariable =
                currentId != Guid.Empty
                    ? _all.FirstOrDefault(r => r.VariableId == currentId)
                    : null;
            SelectedVariable ??= string.IsNullOrEmpty(currentName)
                ? null
                : _all.FirstOrDefault(r =>
                    string.Equals(r.Name, currentName, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }

    /// <summary>
    /// 清单里的一行：把 <see cref="IVariable"/> 摊成"给人看的四列"。
    ///
    /// 为什么不直接把 <c>IVariable</c> 塞进列表：变量模型是领域对象，
    /// 它的 <c>DataType</c> 是 <see cref="Type"/>、来源是枚举，这些直接上屏对操作员毫无意义；
    /// 摊平这一步刻意留在视图模型里，领域模型就不必为"怎么显示"负责。
    /// </summary>
    public sealed class ScadaVariableRow
    {
        public ScadaVariableRow(IVariable model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
        }

        /// <summary>底层变量模型（确认时取 Id 与名字的唯一出处）</summary>
        public IVariable Model { get; }

        public Guid VariableId => Model.VariableId;

        public string Name => Model.Name ?? string.Empty;

        /// <summary>数据类型（XAML 侧经 TypeNameToFriendlyNameConverter 转成"整数 (Int)"这类友好名）</summary>
        public Type DataType => Model.DataType;

        /// <summary>来源：本地变量直说"本地"，网络变量带上连接名（与变量管理弹窗同一口径）</summary>
        public string SourceLabel =>
            Model.VariableType == VariableType.Local
                ? "本地"
                : string.IsNullOrWhiteSpace(Model.ConnectionName) ? "网络" : Model.ConnectionName!;

        /// <summary>当前值（本地变量是界面维护值，网络变量是最近一次轮询镜像）</summary>
        public string ValueText => Model.Value?.ToString() ?? string.Empty;

        public string Description => Model.Description ?? string.Empty;

        /// <summary>关键字匹配：名 / 类型 / 来源 / 说明，任一命中即算（大小写不敏感）</summary>
        public bool Matches(string keyword) =>
            Contains(Name, keyword)
            || Contains(DataType.Name, keyword)
            || Contains(SourceLabel, keyword)
            || Contains(Description, keyword);

        private static bool Contains(string? text, string keyword) =>
            !string.IsNullOrEmpty(text)
            && text!.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
