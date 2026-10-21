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
    /// ① **清单就是可寻址的那一份**：列 <c>IWorkspaceManager.GlobalVariables</c> 与
    ///    <c>IWorkspaceManager.SystemVariables</c> 两份。运行态 <c>IScadaValueSource</c> 背后的
    ///    变量注册表正是这两份的合并镜像（见 VariableRegistry.AttachSystem），
    ///    所以这里列出来的每一条，运行态都解析得到；反过来不在清单里的，选了也是白选。
    ///    **系统变量（Motion.*）也会列进来**：读方向的绑定（数值显示、动画、报警判据）本来就要读轴状态，
    ///    不列等于逼用户去"变量管理"里手打一个根本不存在的名字。它们标着"（只读）"，
    ///    选去写变量的动作会在运行期被写入器如实拒绝（不实现 IWritableVariable）。
    /// ② **Id 是权威**：确认时回传的是 <c>VariableId</c>（外加当时的名字，仅作旧数据兜底与展示）。
    ///    变量改名不会让已配好的动作断链——名字只是"快照"，Id 才是锚点。
    ///    系统变量的 Id 是确定性 Guid（轴 Id + 字段名推导），重启后仍是同一个。
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
        private string _arrayIndexText = "0";
        private int _currentArrayIndex = -1;

        public ScadaVariablePickerViewModel(IWorkspaceManager workspace)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

            ConfirmCommand = new DelegateCommand(OnConfirm, CanConfirm)
                .ObservesProperty(() => SelectedVariable)
                .ObservesProperty(() => ArrayIndexText);
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
            set
            {
                if (!SetProperty(ref _selectedVariable, value))
                    return;

                RaisePropertyChanged(nameof(IsArraySelected));
                RaisePropertyChanged(nameof(ArrayLength));
                RaisePropertyChanged(nameof(ArrayIndexHint));
                RaisePropertyChanged(nameof(ArrayIndexError));

                // 选中数组变量时，默认落到当前绑定下标；没有当前值就落 0。
                if (IsArraySelected)
                    ArrayIndexText = _currentArrayIndex >= 0
                        ? _currentArrayIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : "0";

                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>当前选中的变量是不是数组（决定"数组下标"那一行显不显示）</summary>
        public bool IsArraySelected => SelectedVariable?.IsArray == true;

        /// <summary>选中数组的长度；-1 = 未知（运行时由值源按实际值再校验）</summary>
        public int ArrayLength => SelectedVariable?.ArrayLength ?? -1;

        /// <summary>数组下标输入框的文本（保留用户打了一半的形态，解析推迟到确认）</summary>
        public string ArrayIndexText
        {
            get => _arrayIndexText;
            set
            {
                if (!SetProperty(ref _arrayIndexText, value ?? string.Empty))
                    return;

                RaisePropertyChanged(nameof(ArrayIndexError));
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>下标输入提示：长度已知时给出 0..N-1，未知时只要求非负整数</summary>
        public string ArrayIndexHint
        {
            get
            {
                if (!IsArraySelected)
                    return string.Empty;

                return ArrayLength >= 0
                    ? $"数组长度 {ArrayLength}，可填 0 ~ {ArrayLength - 1}"
                    : "数组长度未知；请填一个非负整数，运行态会按实际长度校验";
            }
        }

        /// <summary>下标输入错误（空串 = 没有错误）</summary>
        public string ArrayIndexError
        {
            get
            {
                if (!IsArraySelected)
                    return string.Empty;

                if (!TryParseArrayIndex(out int index, out string error))
                    return error;

                if (ArrayLength >= 0 && index >= ArrayLength)
                    return $"下标 {index} 超出数组长度 {ArrayLength}";

                return string.Empty;
            }
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

        /// <summary>确认键可用性：选了变量、Id 有效、数组下标（若适用）合法</summary>
        private bool CanConfirm()
        {
            var row = SelectedVariable;
            if (row == null || row.VariableId == Guid.Empty)
                return false;

            // 不能只判"是不是非负整数"：越界下标也会在运行态解析失败，
            // 这里必须用与界面上红字同一个判据（ArrayIndexError）。
            return !IsArraySelected || string.IsNullOrEmpty(ArrayIndexError);
        }

        /// <summary>确认：回传 (Id, 名字, 下标)。Id 拿不到就什么也不做（不许退化成"按名字猜一个"）</summary>
        private void OnConfirm()
        {
            var row = SelectedVariable;
            if (row == null || row.VariableId == Guid.Empty)
                return;

            int index = -1;

            if (IsArraySelected)
            {
                if (!TryParseArrayIndex(out index, out _) || !string.IsNullOrEmpty(ArrayIndexError))
                    return; // 按钮本该已置灰；这里再挡一次，避免回车绕过
            }

            var parameters = new DialogParameters
            {
                { ScadaVariablePicker.PickedIdKey, row.VariableId },
                { ScadaVariablePicker.PickedNameKey, row.Name },
                { ScadaVariablePicker.PickedArrayIndexKey, index },
            };
            RequestClose.Invoke(parameters, ButtonResult.OK);
        }

        /// <summary>解析下标文本（不变区域性；空串/负数/非数字都拒绝）</summary>
        private bool TryParseArrayIndex(out int index, out string error)
        {
            index = -1;
            error = string.Empty;

            string text = (_arrayIndexText ?? string.Empty).Trim();

            if (text.Length == 0)
            {
                error = "数组下标不能为空";
                return false;
            }

            if (!int.TryParse(text, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out index))
            {
                error = $"「{text}」不是有效的整数下标";
                return false;
            }

            if (index < 0)
            {
                error = "数组下标不能为负数";
                return false;
            }

            return true;
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

            // 系统变量（Motion.*）：派生只读量，每次打开都现取——它们随轴表变化，
            // 缓存下来会给出"清单里有一条已经不存在的轴"的幽灵条目。
            // 放在用户变量之后：同名冲突时用户变量在前，与 VariableRegistry 的"用户优先"口径一致。
            foreach (var variable in CopySystemVariables())
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
            // 当前下标必须在 ApplyFilter 之前就位：ApplyFilter 会自动选中第一行，
            // 若当前数组变量恰好是第一行，之后再赋同一个对象会被 SetProperty 短路，
            // SelectedVariable 的 setter 不会再跑，下标就停在 "0"（打开直接确定会改绑元素 0）。
            parameters.TryGetValue<Guid>(ScadaVariablePicker.CurrentIdKey, out var currentId);
            parameters.TryGetValue<string>(ScadaVariablePicker.CurrentNameKey, out var currentName);
            _currentArrayIndex =
                parameters.TryGetValue<int>(ScadaVariablePicker.CurrentArrayIndexKey, out var currentArrayIndex)
                && currentArrayIndex >= 0
                    ? currentArrayIndex
                    : -1;

            if (_keyword.Length != 0)
            {
                _keyword = string.Empty;
                RaisePropertyChanged(nameof(Keyword));
            }
            ApplyFilter();

            SelectedVariable =
                currentId != Guid.Empty
                    ? _all.FirstOrDefault(r => r.VariableId == currentId)
                    : null;
            SelectedVariable ??= string.IsNullOrEmpty(currentName)
                ? null
                : _all.FirstOrDefault(r =>
                    string.Equals(r.Name, currentName, StringComparison.OrdinalIgnoreCase));

            // 双保险：即使上面的赋值被短路，当前是数组元素时也把下标回填到输入框。
            if (IsArraySelected && _currentArrayIndex >= 0)
            {
                ArrayIndexText = _currentArrayIndex.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        #endregion

        /// <summary>
        /// 复制一份系统变量清单（打开弹窗那一刻的快照）。
        ///
        /// 为什么要复制而不是直接遍历 <c>_workspace.SystemVariables</c>：
        /// 那个集合由 <c>MotionStatePublisher</c> 在定时器线程上改写（轴表变化时 Clear+Add），
        /// 与这里在 UI 线程上的遍历撞上就是"集合已修改，无法枚举"——宿主没有全局兜底，
        /// UI 线程异常直接退进程。快照期间被改写是安全的：拿到的可能是"改之前"那一版，
        /// 最坏也只是少一个刚加上的轴，下次打开就有——远好于抛异常。
        ///
        /// 重试三次：轴表变化是稀疏事件（用户改运动卡设置时才发生），撞上的概率很低；
        /// 真撞上时重试一次通常就过去了。三次都失败说明发布器正在高频重建，
        /// 这时返回已复制到的部分（开窗不被打断），下次打开即完整。
        /// </summary>
        private List<IVariable> CopySystemVariables()
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var snapshot = new List<IVariable>();
                try
                {
                    foreach (var variable in _workspace.SystemVariables)
                        snapshot.Add(variable);

                    return snapshot;
                }
                catch (InvalidOperationException)
                {
                    // 集合正在被发布器改写（见方法注释）：再试一次
                }
            }

            return new List<IVariable>();
        }
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

        /// <summary>是不是数组变量（决定选变量弹窗里"数组下标"那一行显不显示）</summary>
        public bool IsArray => DataType.IsArray;

        /// <summary>
        /// 数组长度：当前值/默认值里的数组优先，其次地址配置的 Length；都拿不到返回 -1。
        /// 这里只用于弹窗提示与即时校验，运行态以值源的实际解析为准。
        /// </summary>
        public int ArrayLength
        {
            get
            {
                if (Model.Value is Array current)
                    return current.Length;

                if (Model.DefaultValue is Array fallback)
                    return fallback.Length;

                if (Model.AddressConfig is { Length: > 0 } address)
                    return address.Length;

                return -1;
            }
        }

        /// <summary>
        /// 是否只读（系统变量恒 true）。
        /// 供调用方/断言按行判定，不必再去认 VariableType；视图模板本批不改，
        /// 只读这件事靠下面的 <see cref="SourceLabel"/> 直接写在行上（见那里的注释）。
        /// </summary>
        public bool IsReadOnly => (Model as IVariableDisplaySource)?.IsReadOnly == true;

        /// <summary>
        /// 来源：本地变量直说"本地"，网络变量带上连接名（与变量管理弹窗同一口径），
        /// 系统变量写"系统只读"。
        ///
        /// 为什么系统变量这一档不取 <see cref="IVariableDisplaySource.SourceLabel"/> 再拼后缀：
        /// 那一格是"给操作员看的一列"（宽度固定 80），拼出来的东西是"系统·只读"还是
        /// "系统（只读）"得先算清楚放不放得下——这里直接定死四个字，不依赖另一处给多长的基础文案。
        /// 用户变量仍按类型判断，行为与改动前逐字一致。
        /// </summary>
        public string SourceLabel
        {
            get
            {
                // 为什么把"只读"写在这一格而不是加一列：模板本批不改（行模板一改就要连带
                // 复核列宽预算与列头对齐那批断言），而"只读"是选中系统变量时最该让人看见的信息。
                //
                // 为什么是"系统只读"四个字而不是"系统（只读）"六个字：来源列宽 80，
                // 行内两侧各让出 10px（行容器 4 + GridViewRowPresenter 内置 6），可用约 60px；
                // 全角字宽≈字号（12~13px），六字必然被 CharacterEllipsis 裁掉尾部的"只读"——
                // 恰好裁掉唯一要传达的那两个字。四个字在两种字号下都完整可见。
                if (Model.VariableType == VariableType.System)
                    return "系统只读";

                if (Model.VariableType == VariableType.Io)
                    return Model is IWritableVariable ? "IO可写" : "IO只读";

                // 用户变量：保持原有口径不动（本地="本地"，网络变量带连接名、缺名字时"网络"）
                return Model.VariableType == VariableType.Local
                    ? "本地"
                    : string.IsNullOrWhiteSpace(Model.ConnectionName) ? "网络" : Model.ConnectionName!;
            }
        }

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
