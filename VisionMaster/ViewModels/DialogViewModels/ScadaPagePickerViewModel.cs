using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 组态「选择画面」弹窗：从当前方案的画面清单里挑一页，确认后回传 (画面 Id, 画面名)。
    ///
    /// 三条口径（与运行态寻址保持一致，避免"选得到却切不过去"这类错位）
    /// ---------
    /// ① **清单就是可寻址的那一份**：只列当前方案文档的 <c>Pages</c>。
    ///    运行态 <c>ScadaRuntime.Navigate</c> 背后的寻址正是同一份文档（<c>ScadaDocument.FindPage</c> /
    ///    <c>FindPageByName</c>），所以这里列出来的每一页，运行时都切得过去；
    ///    反过来不在清单里的，选了也是白选。
    /// ② **Id 是权威**：确认时回传的是 <c>PageId</c>（外加当时的名字，仅作旧数据兜底与展示）。
    ///    画面改名不会让已配好的动作断链——名字只是"快照"，Id 才是锚点。
    /// ③ **预选走 Id 优先、名字兜底**：与 <c>ScadaRuntime.Navigate</c> 同一顺序
    ///    （Id 优先、名字兜底），让"面板上看到的是哪一页"和"运行时会切到哪一页"永远对得上。
    ///
    /// 取消语义：本弹窗不产生任何回写（<see cref="Services.ScadaPagePicker.Pick"/> 只在 OK 时回调），
    /// 所以"取消"不会把已配的画面清掉——那是破坏性动作，不能藏在取消键里。
    /// </summary>
    public class ScadaPagePickerViewModel : BindableBase, IDialogAware
    {
        private readonly IWorkspaceManager _workspace;

        /// <summary>全量清单（打开那一刻的快照）；<see cref="Pages"/> 是它在关键字过滤下的投影</summary>
        private readonly List<ScadaPageRow> _all = new();

        private string _keyword = string.Empty;
        private ScadaPageRow? _selectedPage;

        public ScadaPagePickerViewModel(IWorkspaceManager workspace)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

            ConfirmCommand = new DelegateCommand(OnConfirm, () => SelectedPage != null)
                .ObservesProperty(() => SelectedPage);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        public DialogCloseListener RequestClose { get; set; }

        public string Title => "选择画面";

        /// <summary>过滤后的可见清单（关键字为空即全量）</summary>
        public ObservableCollection<ScadaPageRow> Pages { get; } = new();

        /// <summary>搜索关键字：匹配 画面名 / 尺寸 / 说明，大小写不敏感</summary>
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
        public ScadaPageRow? SelectedPage
        {
            get => _selectedPage;
            set => SetProperty(ref _selectedPage, value);
        }

        /// <summary>列表为空时的提示文案（有内容时为空串）</summary>
        public string EmptyHint =>
            _all.Count == 0
                ? "当前方案里还没有画面。请先在组态编辑器里新建画面，再回来配置这条动作。"
                : Pages.Count == 0
                    ? $"没有匹配「{Keyword}」的画面。"
                    : string.Empty;

        /// <summary>列表是否有内容（空态提示靠它显隐）</summary>
        public bool HasPages => Pages.Count > 0;

        public DelegateCommand ConfirmCommand { get; }

        public DelegateCommand CancelCommand { get; }

        /// <summary>
        /// 重建可见清单。
        /// 选中项的处理与变量选择器同一条手感：原来选的那条若还在结果里就留着，否则落到第一条。
        /// </summary>
        private void ApplyFilter()
        {
            var keep = _selectedPage;
            var keyword = _keyword.Trim();

            Pages.Clear();
            foreach (var row in _all)
            {
                if (keyword.Length == 0 || row.Matches(keyword))
                    Pages.Add(row);
            }

            SelectedPage = keep != null && Pages.Contains(keep) ? keep : Pages.FirstOrDefault();

            RaisePropertyChanged(nameof(EmptyHint));
            RaisePropertyChanged(nameof(HasPages));
        }

        /// <summary>确认：回传 (Id, 名字)。Id 拿不到就什么也不做（不许退化成"按名字猜一个"）</summary>
        private void OnConfirm()
        {
            var row = SelectedPage;
            if (row == null || row.PageId == Guid.Empty)
                return;

            var parameters = new DialogParameters
            {
                { Services.ScadaPagePicker.PickedIdKey, row.PageId },
                { Services.ScadaPagePicker.PickedNameKey, row.Name },
            };
            RequestClose.Invoke(parameters, ButtonResult.OK);
        }

        #region IDialogAware

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        /// <summary>
        /// 打开时：现取一份画面清单快照，并按入参预选。
        /// 每次打开都重新取，是因为两次打开之间用户可能已经去编辑器里增删过画面；
        /// 缓存在字段里跨弹窗复用，就会给出"清单里没有它，却怎么也选不上"的幽灵条目。
        /// </summary>
        public void OnDialogOpened(IDialogParameters parameters)
        {
            var document = _workspace.CurrentSolution?.Scada;
            var startup = document?.ResolveStartupPage();

            _all.Clear();
            if (document != null)
            {
                foreach (var page in document.Pages)
                {
                    if (page != null)
                        _all.Add(new ScadaPageRow(page, ReferenceEquals(page, startup)));
                }
            }

            // 关键字归零 + 无条件重建一次清单。
            //
            // 这里**不能**只写 `Keyword = string.Empty` 指望 setter 顺带刷新：首次打开时
            // _keyword 本来就是空串，SetProperty 判定"值没变"会直接返回，ApplyFilter 压根不跑，
            // 于是 _all 装满了画面而 Pages 恒空、界面显示"当前方案里还没有画面"（变量选择器踩过同一个坑）。
            // 把"清单投影"从 setter 的副作用里解耦出来，是这一段的唯一要点。
            if (_keyword.Length != 0)
            {
                _keyword = string.Empty;
                RaisePropertyChanged(nameof(Keyword));
            }
            ApplyFilter();

            parameters.TryGetValue<Guid>(Services.ScadaPagePicker.CurrentIdKey, out var currentId);
            parameters.TryGetValue<string>(Services.ScadaPagePicker.CurrentNameKey, out var currentName);

            SelectedPage =
                currentId != Guid.Empty
                    ? _all.FirstOrDefault(r => r.PageId == currentId)
                    : null;
            SelectedPage ??= string.IsNullOrEmpty(currentName)
                ? null
                : _all.FirstOrDefault(r =>
                    string.Equals(r.Name, currentName, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }

    /// <summary>
    /// 清单里的一行：把 <see cref="ScadaPage"/> 摊成"给人看的四列"。
    ///
    /// 为什么不直接把 <c>ScadaPage</c> 塞进列表：画面模型是领域对象，它的 <c>Elements</c> 是图元集合、
    /// <c>EventHooks</c> 是钩子集合，这些直接上屏对操作员毫无意义；
    /// 摊平这一步刻意留在视图模型里，领域模型就不必为"怎么显示"负责（与 <c>ScadaVariableRow</c> 同一取舍）。
    /// </summary>
    public sealed class ScadaPageRow
    {
        public ScadaPageRow(ScadaPage model, bool isStartup)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            IsStartup = isStartup;
        }

        /// <summary>底层画面模型（确认时取 Id 与名字的唯一出处）</summary>
        public ScadaPage Model { get; }

        public Guid PageId => Model.PageId;

        public string Name => Model.Name ?? string.Empty;

        /// <summary>画布尺寸（"1280 × 720"这种给人看的形式）</summary>
        public string SizeText => $"{Model.Width:0} × {Model.Height:0}";

        /// <summary>图元数（一眼看出这页是不是空的——切过去一片空白的画面最容易被当成没生效）</summary>
        public string ElementCountText => Model.Elements.Count.ToString();

        public string Description => Model.Description ?? string.Empty;

        /// <summary>
        /// 是不是运行起点（<c>ScadaDocument.ResolveStartupPage</c> 选中的那一页）。
        /// 标出来是因为"点了运行先看到哪一页"是操作员最容易记混的一件事，
        /// 而它由"指定的启动画面 → 否则第一页"这条回落规则决定，光看列表次序看不出来。
        /// </summary>
        public bool IsStartup { get; }

        /// <summary>起点徽标文案（不是起点则为空串，模板据空串收掉徽标）</summary>
        public string StartupBadge => IsStartup ? "起点" : string.Empty;

        /// <summary>关键字匹配：名 / 尺寸 / 说明，任一命中即算（大小写不敏感）</summary>
        public bool Matches(string keyword) =>
            Contains(Name, keyword)
            || Contains(SizeText, keyword)
            || Contains(Description, keyword);

        private static bool Contains(string? text, string keyword) =>
            !string.IsNullOrEmpty(text)
            && text!.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
