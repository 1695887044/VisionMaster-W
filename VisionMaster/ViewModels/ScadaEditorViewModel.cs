using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Prism.Commands;
using Prism.Mvvm;
using UI.CustomControl;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 组态画面编辑器视图模型：把「当前方案的 SCADA 文档」接到画布宿主上。
    ///
    /// 职责边界（本阶段刻意做小）：
    ///   读：当前文档 → 当前画面 → 当前选中图元，三个指针而已
    ///   写：新建 / 删除画面；从工具箱放置新图元（<see cref="AddElement"/>）。
    ///       图元的移动/改尺寸由画布直接落在 <see cref="ScadaPage.Elements"/> 的元素上，
    ///       不经过这里转发——那是连续的高频写，转一道只会把鼠标每帧都派发到视图模型
    ///
    /// 四条约束与理由：
    /// 1. 不复制画面数据。本类不持有自己的画面列表，<see cref="Pages"/> 就是模型那份集合。
    ///    复制一份就得处理双向同步，而画布已经是「模型 → 可视树」的单向渲染，
    ///    多出一条平行通道必然出现「界面改了、模型没改」这类劈叉。
    /// 2. 画面属性不中转。宽/高/底色/网格/吸附一律由 XAML 直接绑 <c>SelectedPage.*</c>。
    ///    中转意味着这里要订阅 <see cref="ScadaPage"/> 的属性变更——多一处订阅就多一处漏摘的机会，
    ///    而「改画面尺寸立刻反映到画布」用绑定天然成立。
    /// 3. 缩放/平移不进视图模型。Zoom/Offset 是取景器状态、不落盘（见 <c>ScadaCanvas</c> 的坐标约定），
    ///    放进视图模型等于把「这台机器这一次的视角」写进组态文档。
    /// 4. 挂摘可逆，不做一次性 Dispose。AvalonDock 切标签、隐藏面板都会让视图离树且不回树，
    ///    一次性清理会让面板恢复后不再响应方案切换（与 <c>FlowCanvasViewModel</c> 同一口径）。
    /// </summary>
    public class ScadaEditorViewModel : BindableBase
    {
        private readonly IWorkspaceManager _workspace;

        /// <summary>
        /// 方案/文档的通知来自 WorkspaceContext（BindableBase），不是 SolutionModel——
        /// 换方案时 SolutionModel 整个被替换，挂在旧实例上的通知永远不会响。
        /// </summary>
        private readonly INotifyPropertyChanged? _workspaceChanged;

        /// <summary>无方案时的兜底文档：只为让绑定有个空集合可指，任何写入都不该落到它上面</summary>
        private readonly ScadaDocument _fallback = new();

        /// <summary>
        /// 运行态宿主。<b>必需</b>，不给默认值：给了默认值，容器一旦注不进来就表现为
        /// "按钮永远灰着"这种静默残废，排查成本远高于启动当场抛异常。
        /// 断言工程只验画面管理与属性联动，运行入口不参与，故显式传 <c>null!</c>，
        /// 意图写在调用点上。
        /// </summary>
        private readonly IScadaRuntimeHost? _runtime;

        private ScadaPage? _selectedPage;
        private ScadaElement? _selectedElement;

        /// <summary>
        /// 当前选中的<b>全部</b>图元（画布双向同步）。
        ///
        /// 与 <see cref="SelectedElement"/> 的关系见 <c>ScadaCanvas.SelectedElements</c> 的注释：
        /// 这一条是"全集"，那一条是"主选中"（= 集合首项）。默认给空数组而不是 null，
        /// 省掉消费侧每一处判空。
        /// </summary>
        private IReadOnlyList<ScadaElement> _selectedElements = Array.Empty<ScadaElement>();

        /// <summary>
        /// 上一次为哪份文档保留撤销栈。<b>只用来判断"文档是不是真的换了"</b>——
        /// 撤销栈是静态单栈，换文档必须清，但重新入树（切标签回来）不该清。
        /// </summary>
        private ScadaDocument? _historyDocument;

        public ScadaEditorViewModel(IWorkspaceManager workspace, IScadaRuntimeHost runtime)
        {
            _workspace = workspace;
            _runtime = runtime;
            _workspaceChanged = workspace as INotifyPropertyChanged;

            AddPageCommand = new DelegateCommand(OnAddPage, () => Document != _fallback);
            RemovePageCommand = new DelegateCommand(OnRemovePage, CanRemovePage);
            RunPageCommand = new DelegateCommand(OnRunPage, CanRunPage);
            UndoCommand = new DelegateCommand(OnUndo, () => ScadaEditHistory.CanUndo);
            RedoCommand = new DelegateCommand(OnRedo, () => ScadaEditHistory.CanRedo);

            // 删除与方向键微调都走命令而不是 code-behind：置灰条件（有没有选中、图元是否锁定）
            // 只有视图模型知道，写在 XAML 的 KeyBinding 上就只剩"绑一条命令"这一件事。
            RemoveElementCommand = new DelegateCommand(OnRemoveElement, CanRemoveSelectedElement);
            NudgeCommand = new DelegateCommand<string>(OnNudge, CanNudge);

            // 锁定/解锁没有键盘快捷键，入口只有右键菜单那一项（属性面板顶栏另有一个单图元的复选框）。
            // 它仍走命令而不是让视图直接调方法：判灰条件（有没有选中、选中项还在不在本画面上）
            // 是视图模型的知识，摆在视图里就又出现第二份"谁能编辑"的判定。
            LockElementCommand = new DelegateCommand(() => ToggleSelectedLock(), CanSetSelectedLocked);

            // 组合/取消组合同样只有右键菜单一个入口，理由与锁定那一项相同（判灰条件是视图模型的知识）。
            // 一条命令管两向：菜单标题已经写明了这次会往哪边走（见 BuildElementContextMenu），
            // 再拆成两条命令等于把"现在该走哪边"这个判据复制到菜单组装与命令可用性两处。
            GroupElementCommand = new DelegateCommand(() => ToggleSelectedGroup(), CanToggleSelectedGroup);

            // 复制 / 粘贴 / 再制（S13）。这三条与删除同域——读的都是"选中集合"、写的都是当前画面，
            // 所以可用性判据也走同一个漏斗，不另开一套。
            // 复制与再制的判据相同（都要"有选中"），共用一个方法而不是抄两遍：
            // 两处各写一遍，将来"锁定图元能不能复制"这条口径改了，必然只改一处。
            CopyCommand = new DelegateCommand(OnCopy, CanCopySelection);
            DuplicateCommand = new DelegateCommand(OnDuplicate, CanCopySelection);
            PasteCommand = new DelegateCommand(OnPaste, CanPasteClipboard);

            // 存为模板要弹输入框问名字，但它仍然走命令：判灰条件（有没有选中）是视图模型的知识，
            // 摆在视图里就又出现第二份"谁能被复制"的判定——与上面那三条同一条理由。
            SaveTemplateCommand = new DelegateCommand(OnSaveTemplate, CanCopySelection);

            // 构造即视为已入树（AutoWireViewModel 通常晚于 Loaded 才装好 DataContext，
            // 那时视图的 Loaded 已经过去，故视图侧还要在 DataContextChanged 里补挂一次）
            Activate();
        }

        private bool _subscribed;

        /// <summary>挂接工作区通知并对齐当前文档（幂等）</summary>
        public void Activate()
        {
            if (_subscribed) return;
            _subscribed = true;

            if (_workspaceChanged != null)
                _workspaceChanged.PropertyChanged += OnWorkspacePropertyChanged;

            // 撤销栈是静态的（全局一份），订阅必须在入树时挂、离树时摘：
            // 漏摘就会让这个 VM 被静态事件钉住，切几次标签就攒几个"影子编辑器"。
            ScadaEditHistory.Changed += OnHistoryChanged;

            OnDocumentChanged();
        }

        /// <summary>摘除工作区通知（幂等）；摘干净后本 VM 不再被工作区单例钉住</summary>
        public void Deactivate()
        {
            if (!_subscribed) return;
            _subscribed = false;

            if (_workspaceChanged != null)
                _workspaceChanged.PropertyChanged -= OnWorkspacePropertyChanged;

            ScadaEditHistory.Changed -= OnHistoryChanged;
        }

        /// <summary>当前方案的组态文档（无方案时为空的兜底文档）</summary>
        public ScadaDocument Document => _workspace.CurrentSolution?.Scada ?? _fallback;

        /// <summary>画面集合（模型本体，供画面下拉/标签绑定）</summary>
        public ObservableCollection<ScadaPage> Pages => Document.Pages;

        /// <summary>当前编辑的画面</summary>
        public ScadaPage? SelectedPage
        {
            get => _selectedPage;
            set
            {
                if (!SetProperty(ref _selectedPage, value)) return;

                // 上一画面的选中图元不属于这一页，留着它会让属性面板改到"看不见的图元"。
                // 反过来不做的事：不把选中态存进模型——选中是编辑器态，不该落盘。
                SelectedElement = null;

                // 删除按钮的可用性看的是"有没有当前画面 + 文档里还剩几页"，跟选中图元无关，
                // 所以刷新点挂在这里（挂在 SelectedElement 上是借道：SelectedElement 已是 null 时
                // SetProperty 返回 false，通知根本不发，按钮状态就会留在上一画面的值）。
                //
                // 运行按钮一并在这里刷：本工程的 DelegateCommand 不跟随 CommandManager 自动重查，
                // 每个命令的可用性都必须在它依赖的状态变化处显式 raise 一次。
                // 新建/删除画面都会走到这个 setter，所以这里是画面数量变化的必经漏斗。
                RemovePageCommand.RaiseCanExecuteChanged();
                RunPageCommand.RaiseCanExecuteChanged();

                // 粘贴的可用性同时看"剪贴板有没有货"和"有没有当前画面"：前一半由复制那一处刷，
                // 后一半只能在这里刷。少了这一行，"在 A 画面复制、切到 B 画面"之后粘贴项会是灰的，
                // 而用户手上明明有东西可粘——这种"看着像坏了"的灰是最难被报上来的。
                PasteCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// 主选中图元（画布双向同步；属性面板与工具箱都以它为写入目标）。
        ///
        /// 语义与 <c>ScadaCanvas.OnSelectedElementChanged</c> 逐字对齐——两边必须是同一套解释，
        /// 否则同一次点击流经绑定会在两侧得到不同结果：
        ///   · 落在集合<b>之内</b> = 多选里换了个"队长"，集合一个字节都不动；
        ///   · 落在集合<b>之外</b>（含 null）= 换了一批人，集合跟着收拢成"就他一个"。
        ///
        /// 第二条是必需的，不是顺手加的：<see cref="AddElement"/> 放完新图元直接写主选中，
        /// 集合若不跟着收拢，就会出现"集合里还是旧的三个、主选中是刚放的那个"这种自相矛盾态。
        ///
        /// 收拢进来的同样是<b>它所在的一整组</b>（见 <see cref="ExpandPointSelection"/>）：
        /// 画布那边点一下就展开整组，这里若只收拢成"就他一个"，同一句话从两条路进来会得到两批人，
        /// 而绑定是双向的、两条路都会走。展开只在这个"落在集合之外"的分支上做，
        /// 落在集合之内的分支（多选里换队长）一个字节都不动。
        /// </summary>
        public ScadaElement? SelectedElement
        {
            get => _selectedElement;
            set
            {
                if (ReferenceEquals(_selectedElement, value))
                    return;

                // 落在集合之外：集合先收拢，再由 ApplySelection 落主选中。
                // 反过来（先写主选中）会让绑定抢先一步，把"主选中变了、集合还是旧的"推给画布。
                if (value == null || !ContainsRef(_selectedElements, value))
                {
                    ApplySelection(value == null ? Array.Empty<ScadaElement>() : ExpandPointSelection(value));
                    return;
                }

                // 落在集合之内：只写主选中，集合不动（右键点组内某个成员走的就是这条路）。
                SetMainSelection(value);
                RefreshSelectionCommands();
            }
        }

        /// <summary>
        /// 当前选中的全部图元（画布双向同步）。
        ///
        /// 只整个换掉、绝不原地增删：与画布同一口径，于是"谁改了选中"永远是一次赋值，
        /// 不存在"集合被两边同时改"的中间态。
        /// </summary>
        public IReadOnlyList<ScadaElement> SelectedElements
        {
            get => _selectedElements;
            set => ApplySelection(value);
        }

        /// <summary>
        /// 换一批选中（视图模型侧唯一写入口）。
        ///
        /// 两件事的<b>顺序</b>是硬要求，与 <c>ScadaCanvas.SetSelection</c> 一致：
        /// 集合在前、主选中在后。依赖属性一改就同步通知绑定源，顺序反了会出现一瞬间
        /// "集合还是旧的、主选中已经是新的"，宿主正好在这个瞬间读就会拿到自相矛盾的状态。
        /// </summary>
        private void ApplySelection(IReadOnlyList<ScadaElement>? items)
        {
            var next = items ?? Array.Empty<ScadaElement>();

            if (!ReferenceEquals(_selectedElements, next))
            {
                _selectedElements = next;
                RaisePropertyChanged(nameof(SelectedElements));
            }

            // 集合没变也照样写一次主选中：删掉/撤销之后集合可能<b>恰好</b>已经是空表，
            // 而主选中还指着那个已经不存在的图元——那正是属性面板显示幽灵数据的来源。
            SetMainSelection(next.Count > 0 ? next[0] : null);

            RefreshSelectionCommands();
        }

        /// <summary>
        /// 直写主选中字段并发通知。
        ///
        /// 刻意<b>不</b>走 <see cref="SelectedElement"/> 的 setter：那个 setter 在"值落在集合之外"
        /// 时会回调 <see cref="ApplySelection"/>，而本方法正是从它里面调出来的，绕一圈就是死循环。
        /// </summary>
        private void SetMainSelection(ScadaElement? value)
        {
            if (ReferenceEquals(_selectedElement, value))
                return;

            _selectedElement = value;
            RaisePropertyChanged(nameof(SelectedElement));
        }

        /// <summary>
        /// 把"指向了某一个图元"展开成"真正该选上的那一批"——它在一组里就是整组，否则就是它自己。
        ///
        /// 与 <c>ScadaCanvas.ExpandPointSelection</c> 是同一套语义的两份实现（各自私有：画布是控件库、
        /// 视图模型在上层，谁也不该为了这一件事反向依赖对方）。两边必须逐字对齐，
        /// 否则同一次点击流经双向绑定会在两侧得到不同结果——那正是本类
        /// <see cref="SelectedElement"/> 注释里反复强调要避免的事。
        ///
        /// 判据只有 <see cref="ScadaPage.GetGroupMembers"/> 一处：视图侧自己按 <c>GroupId</c> 再筛一遍，
        /// 就会出现"画布认一组、菜单认另一组"的劈叉。
        ///
        /// <b>被点中的那个排首位</b>：<see cref="ApplySelection"/> 拿首项当主选中，
        /// 属性面板与单目标命令都跟着主选中走，排到后面会变成"我点的是 B，面板显示的是 A"。
        ///
        /// 图层隐藏的成员不并进来（与画布同判据 <see cref="ScadaPage.IsElementVisible"/>），
        /// 锁住的成员照并（单击本来就选得中锁住的图元，只是拖不动）。
        /// </summary>
        private IReadOnlyList<ScadaElement> ExpandPointSelection(ScadaElement element)
        {
            if (_selectedPage is not { } page)
                return new[] { element };

            var members = page.GetGroupMembers(element);

            if (members.Count <= 1)
                return new[] { element }; // 未分组，或组里只剩它自己（单成员组与未分组行为等价）

            var expanded = new List<ScadaElement>(members.Count) { element };

            foreach (var member in members)
            {
                if (ReferenceEquals(member, element) || !page.IsElementVisible(member))
                    continue;

                expanded.Add(member);
            }

            return expanded;
        }

        /// <summary>
        /// 删除/微调/锁定/组合/复制/再制/存模板这些命令的可用性都取决于"选中了谁、能不能编辑"，
        /// 选中一变就得重算。
        ///
        /// 快捷键本身在按键那一刻会重查 CanExecute，但绑在同一批命令上的按钮不会——
        /// 不在这里 raise，画布上换了图元之后按钮的灰亮状态就会留在上一个图元上。
        /// </summary>
        private void RefreshSelectionCommands()
        {
            RemoveElementCommand.RaiseCanExecuteChanged();
            NudgeCommand.RaiseCanExecuteChanged();
            LockElementCommand.RaiseCanExecuteChanged();
            GroupElementCommand.RaiseCanExecuteChanged();

            // 复制 / 再制 / 存模板三条共用 CanCopySelection 这一个判据，所以一起刷。
            CopyCommand.RaiseCanExecuteChanged();
            DuplicateCommand.RaiseCanExecuteChanged();
            SaveTemplateCommand.RaiseCanExecuteChanged();
        }

        /// <summary>这个图元在不在选中集合里（引用相等——图元没有重写 Equals，也不该重写）</summary>
        private static bool ContainsRef(IReadOnlyList<ScadaElement> items, ScadaElement element)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], element))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 选中集合里"此刻真的能改"的那些（顺序照选中顺序）。
        ///
        /// 四个条件缺一不可：属于本画面（撤销之后可能已经不在）、未被锁定
        /// （图元锁与图层锁，判定权整个交给 <see cref="ScadaPage.IsElementEditable"/>）、不重复。
        /// 批量操作全都走这一个漏斗——各写一遍过滤，迟早出现"删除认锁定、对齐不认"这种劈叉。
        /// </summary>
        private List<ScadaElement> CollectEditable(ScadaPage page) => CollectTargets(page, editableOnly: true);

        /// <summary>
        /// 选中集合里"确实在本画面上"的那些（顺序照选中顺序），<b>不含锁定过滤</b>。
        ///
        /// 两类入口用它：
        /// ① 锁定/解锁——那一对动作的输入恰恰包含已经锁上的图元
        ///   （见 <see cref="ScadaPage.TrySetElementLocked"/> 类注释那段），走
        ///   <see cref="CollectEditable"/> 会把"解锁"筛成空操作；
        /// ② 复制 / 再制 / 存为模板——它们是<b>读</b>操作，不写画面。锁定的图元照样读得出来，
        ///   "照这张锁住的底图再做一个"是现场真实诉求，被锁挡住只会让人先去解锁、复制、再锁回去。
        /// 与 <see cref="CollectEditable"/> 共用同一个循环体，是为了让"属于本画面 / 去重"这两条口径只有一份。
        /// </summary>
        private List<ScadaElement> CollectPresent(ScadaPage page) => CollectTargets(page, editableOnly: false);

        private List<ScadaElement> CollectTargets(ScadaPage page, bool editableOnly)
        {
            var targets = new List<ScadaElement>(_selectedElements.Count);

            foreach (var element in _selectedElements)
            {
                if (!page.Elements.Contains(element))
                    continue;

                if (editableOnly && !page.IsElementEditable(element))
                    continue;

                if (targets.Contains(element))
                    continue;

                targets.Add(element);
            }

            return targets;
        }

        public DelegateCommand AddPageCommand { get; }

        public DelegateCommand RemovePageCommand { get; }

        /// <summary>运行当前方案（弹出全屏运行窗口）</summary>
        public DelegateCommand RunPageCommand { get; }

        /// <summary>撤销一步编辑（Ctrl+Z）。可用性直接问 <see cref="ScadaEditHistory"/>，本类不自己记栈深</summary>
        public DelegateCommand UndoCommand { get; }

        /// <summary>重做一步编辑（Ctrl+Y）</summary>
        public DelegateCommand RedoCommand { get; }

        /// <summary>删除选中图元（Delete 键 / 右键菜单），可用性见 <see cref="CanRemoveSelectedElement"/></summary>
        public DelegateCommand RemoveElementCommand { get; }

        /// <summary>
        /// 锁定 / 解锁选中图元（右键菜单「锁定图元 / 解锁图元」那一项，<b>整批生效</b>）。
        /// 可用性见 <see cref="CanSetSelectedLocked"/>，动作见 <see cref="ToggleSelectedLock"/>。
        /// </summary>
        public DelegateCommand LockElementCommand { get; }

        /// <summary>
        /// 组合 / 取消组合选中图元（右键菜单「组合 / 取消组合」那一项）。
        /// 可用性见 <see cref="CanToggleSelectedGroup"/>，动作见 <see cref="ToggleSelectedGroup"/>。
        /// </summary>
        public DelegateCommand GroupElementCommand { get; }

        /// <summary>复制选中图元到剪贴板（Ctrl+C / 右键菜单）。可用性见 <see cref="CanCopySelection"/></summary>
        public DelegateCommand CopyCommand { get; }

        /// <summary>把剪贴板内容粘到当前画面（Ctrl+V / 右键菜单）。可用性见 <see cref="CanPasteClipboard"/></summary>
        public DelegateCommand PasteCommand { get; }

        /// <summary>原地再制一份选中图元（Ctrl+D / 右键菜单），<b>不动剪贴板</b>。可用性见 <see cref="CanCopySelection"/></summary>
        public DelegateCommand DuplicateCommand { get; }

        /// <summary>
        /// 把选中图元存进「我的模板」（右键菜单「存为模板…」）。可用性见 <see cref="CanCopySelection"/>。
        /// 会弹一个输入框问名字，故不是"一键完成"的动作——菜单标题带省略号就是这件事的预告。
        /// </summary>
        public DelegateCommand SaveTemplateCommand { get; }

        /// <summary>
        /// 方向键微调选中图元；参数是 <c>"Left"</c>/<c>"Right"</c>/<c>"Up"</c>/<c>"Down"</c>。
        /// 用字符串而不是四个命令：四条 <c>KeyBinding</c> 只差一个方向，拆成四个属性
        /// 等于把同一件事抄四遍，而这里的参数压根不会被外部构造（只由 XAML 常量给）。
        /// </summary>
        public DelegateCommand<string> NudgeCommand { get; }

        /// <summary>
        /// 撤销按钮的提示文案：直接写出"下一步会撤掉什么"。
        /// 只写"撤销"两个字的按钮，用户在栈里攒了七八步之后只能靠试；写上操作名才是商业软件的做法。
        /// 栈空时也返回一句完整的话（按钮此时本就灰着，但 ToolTip 不该是空白）。
        /// </summary>
        public string UndoLabel
            => ScadaEditHistory.NextUndoLabel is { } label ? $"撤销：{label}（Ctrl+Z）" : "没有可撤销的操作（Ctrl+Z）";

        /// <summary>重做按钮的提示文案，口径同 <see cref="UndoLabel"/></summary>
        public string RedoLabel
            => ScadaEditHistory.NextRedoLabel is { } label ? $"重做：{label}（Ctrl+Y）" : "没有可重做的操作（Ctrl+Y）";

        private void OnUndo()
        {
            if (!ScadaEditHistory.Undo()) return;
            PruneSelection();
        }

        private void OnRedo()
        {
            if (!ScadaEditHistory.Redo()) return;
            PruneSelection();
        }

        /// <summary>
        /// 撤销/重做之后把选中态拉回合法范围。
        ///
        /// 必须做：撤销"放置图元"会把那个图元从画面里摘掉，而选中集合还指着它——
        /// 属性面板随即显示一个已经不在画面上的对象，用户改了看不见、存盘还多一份幽灵数据。
        ///
        /// 只剔"确实失效"的成员：撤销一次移动不该顺手取消用户的整批选中。
        /// 全员幸存时连一个列表都不分配（这是常态路径）。
        ///
        /// 不在这里判图层可见性：图层隐藏由画布在可见性变化时自己剔
        /// （见 <c>ScadaCanvas.ApplyLayerVisibility</c>），它剔完会经绑定回流到这里。
        /// 同一批判定写两处，迟早对不上。
        /// </summary>
        private void PruneSelection()
        {
            var current = _selectedElements;

            if (current.Count == 0)
                return;

            if (_selectedPage is not { } page)
            {
                ApplySelection(Array.Empty<ScadaElement>());
                return;
            }

            List<ScadaElement>? survivors = null;

            for (int i = 0; i < current.Count; i++)
            {
                bool keep = page.Elements.Contains(current[i]);

                if (survivors == null)
                {
                    if (keep)
                        continue; // 还没遇到要被剔掉的，先不建表

                    survivors = new List<ScadaElement>(current.Count);

                    for (int j = 0; j < i; j++)
                        survivors.Add(current[j]); // 回头补上前面那些已确认幸存的

                    continue;
                }

                if (keep)
                    survivors.Add(current[i]);
            }

            if (survivors != null)
                ApplySelection(survivors);
        }

        /// <summary>栈变了就刷按钮：可用态与提示文案都跟着变（DelegateCommand 不自动重查，必须显式 raise）</summary>
        private void OnHistoryChanged(object? sender, EventArgs e)
        {
            UndoCommand.RaiseCanExecuteChanged();
            RedoCommand.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(UndoLabel));
            RaisePropertyChanged(nameof(RedoLabel));
        }

        /// <summary>
        /// 提前把"没什么可跑"的按钮置灰：一页都没有，或者压根没有运行环境。
        /// 至于启动画面存不存在，<b>不在这里判断</b>——那是 <see cref="ScadaDocument.ResolveStartupPage"/>
        /// 与运行宿主的口径（没启动画面会回落到首页），在按钮上重复一遍就等于养第二套判定，
        /// 两边迟早对不上，表现为"按钮能点但什么都没发生"。
        /// </summary>
        private bool CanRunPage() => _runtime != null && Document.Pages.Count > 0;

        private void OnRunPage()
        {
            // 交出去的是内存里的当前文档，不是磁盘上那份方案：用户刚拖完图元点运行，
            // 看到的就该是他刚改完的样子。要不要先存盘不由这里决定。
            _runtime?.Start(Document);
        }

        /// <summary>
        /// 末页保护在模型里（<see cref="ScadaDocument.TryRemovePage"/>），这里只是提前把按钮置灰，
        /// 省掉一次"点了才知道不行"的点击。
        /// </summary>
        private bool CanRemovePage() => _selectedPage != null && Document.Pages.Count > 1;

        private void OnAddPage()
        {
            var page = Document.AddPage();

            // 新画面立刻成为编辑目标：否则用户点完"新建"看到的还是旧画面，
            // 会以为没生效而连点好几次
            SelectedPage = page;
        }

        private void OnRemovePage()
        {
            var page = _selectedPage;
            if (page == null) return;

            // 确认框保留：撤销是补救，不是让人放心乱删的理由（口径与 ScadaDocument.TryRemovePage 一致）。
            // 文案写"可撤销"而不是"无法恢复"——S9 起删画面确实能撤，提示必须与事实一致，
            // 否则用户会为了保住画面而放弃一次本该随手可撤的整理。
            string message = page.Elements.Count > 0
                ? $"画面 [{page.Name}] 上还有 {page.Elements.Count} 个图元，删除后可用 Ctrl+Z 撤销。\n确定要删除该画面吗？"
                : $"画面 [{page.Name}] 为空，确定要删除吗？";

            if (!EasyDialog.ShowSync("删除确认", message)) return;

            int index = Document.Pages.IndexOf(page); // 删之前记下位置，才知道"就近"该往哪边挑

            if (!Document.TryRemovePage(page, out string error))
            {
                EasyDialog.ShowSync("无法删除", error);
                return;
            }

            // 删除后就近选中（删尾页就选它前一个），让画面上下文不断档。
            // 不用再判空：TryRemovePage 保证了删完至少还剩一页。
            SelectedPage = Document.Pages[Math.Min(index, Document.Pages.Count - 1)];
        }

        /// <summary>
        /// 放置一个图元（工具箱拖放的落点已经由画布换算成设计坐标并吸附过）。
        ///
        /// 只往模型集合里 Add：画布订阅了 <c>CollectionChanged</c>，控件由它自己生成。
        /// 这里绝不能再手工往可视树塞一份，否则同一个图元出现两个控件，
        /// 而其中一个是后面任何一次集合变更都清不掉的孤儿。
        /// </summary>
        /// <param name="typeKey">图元类型键（来自拖放负载）</param>
        /// <param name="origin">新图元左上角的设计坐标</param>
        /// <returns>放进去的图元；没画面或类型没注册则返回 null（放下是失败，不该崩）</returns>
        public ScadaElement? AddElement(string typeKey, Point origin)
        {
            var page = _selectedPage;
            if (page == null) return null;

            var descriptor = ElementRegistry.Find(typeKey);
            if (descriptor == null) return null;

            var element = descriptor.CreateElement(origin.X, origin.Y);

            // 一次放置 = 一条撤销记录（S9-d）。作用域要罩住"起名 + 归层 + 排序 + 进集合"四步，
            // 少罩一步就会出现"Ctrl+Z 之后图元还在，只是名字回去了"这种半截撤销。
            // 集合 Add 本身也走记录（ScadaCollectionRecorder），所以整段必须同在一个作用域里。
            using (page.BeginEdit($"放置 {descriptor.DisplayName}"))
            {
                element.Name = page.MakeUniqueElementName(descriptor.DisplayName);

                // 新图元默认归到画面的首个图层。不这么做的话它就是个"未分层"图元：
                // 图层面板里谁都装不下它，用户关掉唯一的图层却发现画面上的东西一个没少，
                // 只会判定成"开关坏了"。未分层（LayerId=Empty）这个取值留给脏数据与手工取消分层，
                // 不该是新内容的默认状态（旧文件的平铺图元由 EnsureIdentity 归到同一层，口径一致）。
                element.LayerId = page.DefaultLayer?.LayerId ?? Guid.Empty;

                // 新图元排到最前：画面里往往铺着一张 ZIndex=0 的设备底图，不抬序的话
                // 刚拖出来的矩形会直接生在它下面，用户看到的是"拖了没反应"。
                element.ZIndex = page.Elements.Count > 0 ? page.Elements.Max(e => e.ZIndex) + 1 : 1;

                page.Elements.Add(element);
            }

            // 放完就选中：属性面板（S3-d）紧接着要编辑的就是它，
            // 而且选中框是"东西确实放下了"最直接的反馈。
            // 放在作用域外：选中是编辑器态、不落盘，不该混进这条撤销记录里。
            SelectedElement = element;

            return element;
        }

        /// <summary>
        /// 删除选中的图元（Delete 键 / 右键菜单）。写入口只有
        /// <see cref="ScadaPage.TryRemoveElement"/> 一条，这里不自己动 <c>Elements</c>。
        ///
        /// <b>整批一起删</b>：多选之后按一次 Delete 是一次动作，用户按一次 Ctrl+Z 就该全部回来——
        /// 所以外面再罩一层作用域（内层 <see cref="ScadaPage.TryRemoveElement"/> 的作用域会合并进来，
        /// 记录标签以最外层为准，见 <c>ScadaChangeScope</c>）。
        ///
        /// 锁定的图元<b>跳过</b>而不是整批失败：多选里混进一个锁住的底图是常态
        /// （设备底图往往整层锁住），为此让整批删除失败，Delete 键看起来就是"时灵时不灵"。
        /// 一个都删不掉（全锁死）时才返回 false。
        ///
        /// 删完顺手清选中：主选中若还指着刚删掉的对象，属性面板就会继续编辑一个不在画面上的图元
        /// （与撤销后的 <see cref="PruneSelection"/> 同一个坑）。
        /// </summary>
        public bool RemoveSelectedElement()
        {
            if (_selectedPage is not { } page)
                return false;

            var targets = CollectEditable(page);

            if (targets.Count == 0)
                return false;

            string label = targets.Count == 1
                ? $"删除图元 [{targets[0].Name}]"
                : $"删除 {targets.Count} 个图元";

            using (page.BeginEdit(label))
            {
                foreach (var element in targets)
                    page.TryRemoveElement(element, out _);
            }

            ApplySelection(Array.Empty<ScadaElement>());
            return true;
        }

        /// <summary>
        /// 方向键微调：把选中图元挪 <paramref name="dx"/>/<paramref name="dy"/> 个设计像素。
        ///
        /// 为什么值得单独做一个入口：鼠标拖动最小只能到 1 像素、还受吸附网格影响，
        /// 而"把标签往左挪两像素对齐"是组态时最高频的收尾动作，商业软件都留了方向键。
        /// 每次按键产出一条撤销记录（不按时间合并）——预测性优先：用户按了 5 次就知道要撤 5 次。
        /// 多选时<b>整组一起挪</b>：选中三个标签按一下方向键，用户要的是"这一组整体微调"，
        /// 只挪主选中那一个会让人以为键卡了。锁定的图元不动（口径与拖动一致）。
        /// </summary>
        private void NudgeSelectedElement(double dx, double dy)
        {
            if (_selectedPage is not { } page)
                return;

            var targets = CollectEditable(page);

            if (targets.Count == 0)
                return;

            using (page.BeginEdit(targets.Count == 1 ? "微调图元位置" : $"微调 {targets.Count} 个图元位置"))
            {
                foreach (var element in targets)
                {
                    element.X += dx;
                    element.Y += dy;
                }
            }
        }

        /// <summary>方向键步长（设计像素）。按住 Shift 走粗调，对应"先摆大位置、再逐像素对齐"两段操作。</summary>
        private const double NudgeStep = 1;

        private const double NudgeCoarseStep = 10;

        private void OnNudge(string? direction)
        {
            // 位移量在这里查表，方向键的 KeyBinding 只负责"按了哪个方向"。
            // 表里没有的方向（将来 XAML 写错字）直接忽略，而不是当成 (0,0) 走一遍空作用域。
            (double dx, double dy) offset = direction switch
            {
                "Left" => (-1d, 0d),
                "Right" => (1d, 0d),
                "Up" => (0d, -1d),
                "Down" => (0d, 1d),
                _ => (0d, 0d),
            };

            if (offset == (0d, 0d))
                return;

            double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? NudgeCoarseStep : NudgeStep;
            NudgeSelectedElement(offset.dx * step, offset.dy * step);
        }

        private bool CanNudge(string? direction)
            => direction is "Left" or "Right" or "Up" or "Down"
               && _selectedPage is { } page
               && CollectEditable(page).Count > 0;

        private void OnRemoveElement() => RemoveSelectedElement();

        #region 复制 / 粘贴 / 再制 / 存为模板（S13）

        /// <summary>
        /// 每粘贴一次往后错开的距离（设计像素）。
        ///
        /// 不给偏移的话，粘出来的副本与原件<b>像素级重合</b>——用户看到的是"按了 Ctrl+V 没反应"，
        /// 而实际上画面里已经多了一个东西，直到他拖开上面那个才会发现下面还压着一个。
        /// 12 是个折中：一眼看得出错开了，又不至于连粘几次就挪出画面。
        /// </summary>
        private const double PasteStep = 12;

        /// <summary>
        /// 本次"复制"之后已经粘贴了几次。偏移量 = 步长 × 次数，于是连续按 Ctrl+V 会阶梯式排开
        /// （商业软件都这么做），而不是叠在同一格上。重新复制一次就归零、重新起算。
        ///
        /// 只活在内存里、不落盘：它是"这一次操作序列"的节奏，不是文档内容。
        /// </summary>
        private int _pasteCount;

        /// <summary>
        /// 把选中的图元抓成一份快照放进剪贴板。
        ///
        /// 过滤走 <see cref="CollectPresent"/>（只看"还在不在本画面"、不看锁定）：
        /// 复制是读操作，"照这张锁住的底图再做一个"是现场真实诉求，
        /// 被锁挡住只会逼用户先解锁、复制、再锁回去。
        ///
        /// 快照怎么造、组槽位怎么编号全在 <see cref="ScadaClipboard.Capture"/> 那一处，
        /// 这里只负责"哪些该被抓走"。空手而归时不覆盖剪贴板里原有的内容——
        /// 用户上一次复制的东西不该因为一次误按 Ctrl+C 而消失。
        /// </summary>
        public bool CopySelection()
        {
            if (_selectedPage is not { } page)
                return false;

            var targets = CollectPresent(page);

            if (targets.Count == 0)
                return false;

            ScadaClipboard.Payload = ScadaClipboard.Capture(page, targets);

            // 新复制的一批从第一格偏移重新起算：不归零的话，复制完再粘会莫名其妙跳到 (36,36)。
            _pasteCount = 0;

            // 粘贴的可用性跟着剪贴板走，而剪贴板只在复制这里变，所以刷在这一处。
            PasteCommand.RaiseCanExecuteChanged();
            return true;
        }

        /// <summary>
        /// 把剪贴板内容物化到当前画面。
        ///
        /// <b>不在调用方再包一层作用域</b>：<see cref="ScadaClipboard.Materialize"/> 内部
        /// 已经开了 <c>page.BeginEdit</c>，"一次粘贴 = 一条撤销位"这条不变量由它一家负责
        /// （见那个方法的注释）。这里再包一层虽然结果一样，但会让"撤销位到底谁开的"变成两处说法。
        /// </summary>
        public bool PasteClipboard()
        {
            if (ScadaClipboard.Payload is not { IsEmpty: false } payload)
                return false;

            _pasteCount++;
            double offset = PasteStep * _pasteCount;

            return PlacePayload(payload, offset, offset, $"粘贴 {payload.Items.Count} 个图元").Count > 0;
        }

        /// <summary>
        /// 原地再制：把选中的图元复制一份、立刻贴到本画面上，<b>不经过剪贴板</b>。
        ///
        /// 为什么不写成"先 <see cref="CopySelection"/> 再 <see cref="PasteClipboard"/>"：
        /// 那会把用户手里的剪贴板冲掉——他在别的画面复制了一批东西，回到这里按一次 Ctrl+D，
        /// 剪贴板就变成这几个图元了，待会儿切回去想粘贴时粘出来的是错的。
        /// 两条路各自造自己的负载，互不干涉。
        /// </summary>
        public bool DuplicateSelection()
        {
            if (_selectedPage is not { } page)
                return false;

            var targets = CollectPresent(page);

            if (targets.Count == 0)
                return false;

            var payload = ScadaClipboard.Capture(page, targets);

            if (payload.IsEmpty)
                return false;

            _pasteCount++;
            double offset = PasteStep * _pasteCount;

            string label = targets.Count == 1
                ? $"再制 [{targets[0].Name}]"
                : $"再制 {targets.Count} 个图元";

            return PlacePayload(payload, offset, offset, label).Count > 0;
        }

        /// <summary>
        /// 把一份负载放到当前画面上（粘贴 / 再制 / 插模板 / 拖模板四条路的<b>共同落点</b>）。
        ///
        /// 放进来的东西立刻<b>整批选中</b>：用户粘完/插完紧接着的动作十有八九是拖到位置上，
        /// 让他再框选一次是白费一步。选中写在 <c>Materialize</c> 之外是对的——
        /// 选中是编辑器态、不落盘，不该混进那条撤销记录里（与 <see cref="AddElement"/> 同口径）。
        ///
        /// 偏移量由调用方给：快捷键粘贴要的是"阶梯式错开"，而从工具箱拖模板要的是
        /// "整块内容左上角落在鼠标处"（见 <see cref="ScadaClipboard.GetBounds"/>），
        /// 两种算法差得远，塞进这里就得先判"这次是哪种调用"。
        /// </summary>
        /// <param name="payload">要放的快照（空负载直接返回空表）</param>
        /// <param name="offsetX">相对快照原坐标的横向偏移（设计像素）</param>
        /// <param name="offsetY">相对快照原坐标的纵向偏移（设计像素）</param>
        /// <param name="label">撤销按钮上显示的操作名</param>
        /// <returns>真正进了画面的图元（顺序即物化顺序）；没画面或负载为空时是空表</returns>
        public IReadOnlyList<ScadaElement> PlacePayload(
            ScadaClipboardPayload? payload, double offsetX, double offsetY, string label)
        {
            if (_selectedPage is not { } page)
                return Array.Empty<ScadaElement>();

            if (payload is not { IsEmpty: false })
                return Array.Empty<ScadaElement>();

            var created = ScadaClipboard.Materialize(page, payload, offsetX, offsetY, label);

            if (created.Count > 0)
                ApplySelection(created);

            return created;
        }

        /// <summary>
        /// 把选中图元存进「我的模板」。
        ///
        /// 名字<b>问用户要</b>而不是自动取图元名：模板是拿来复用的，名字要能表达用途
        /// （"三通阀_开位"而不是"按钮_7"）。默认值给主选中的图元名，多数情况下回车即可。
        ///
        /// 失败一律弹提示而不是静默：模板库在另一个面板上（工具箱的「我的模板」分组），
        /// 用户看不见它，存不进去却没有任何反馈，他只会以为"这个功能没做"。
        /// 成功则不弹——输入框上那个"确定"就是这次提交的回执，再弹一个"已保存"是二次确认。
        /// </summary>
        public bool SaveSelectionAsTemplate()
        {
            if (_selectedPage is not { } page)
                return false;

            var targets = CollectPresent(page);

            if (targets.Count == 0)
                return false;

            var payload = ScadaClipboard.Capture(page, targets);

            if (payload.IsEmpty)
                return false;

            var (confirmed, value) = EasyDialog.ShowTextInputSync("存为模板", _selectedElement?.Name ?? "模板");

            if (!confirmed)
                return false;

            if (!ScadaTemplateStore.Shared.TrySave(value, payload, out _, out string? error))
            {
                EasyDialog.ShowSync("存为模板失败", error ?? "模板保存失败");
                return false;
            }

            return true;
        }

        private void OnCopy() => CopySelection();

        private void OnPaste() => PasteClipboard();

        private void OnDuplicate() => DuplicateSelection();

        private void OnSaveTemplate() => SaveSelectionAsTemplate();

        /// <summary>
        /// 复制 / 再制 / 存为模板三条命令共用的判据：选中集合里得有"确实在本画面上"的图元。
        ///
        /// 用 <see cref="CollectPresent"/> 而不是 <see cref="CollectEditable"/>：
        /// 这三件事都是<b>读</b>选中项、往画面里<b>新增</b>东西，锁定的原件照样该被读出来
        /// （理由见 <see cref="CollectPresent"/> 的注释）。
        /// </summary>
        public bool CanCopySelection()
            => _selectedPage is { } page
               && CollectPresent(page).Count > 0;

        /// <summary>
        /// 粘贴这一项能不能点：手上有货、且当前有画面。
        ///
        /// 两半各有一处刷新点——"有没有货"跟着复制走（见 <see cref="CopySelection"/>），
        /// "有没有画面"跟着 <see cref="SelectedPage"/> 走。缺一处就会留下一个灰错的按钮。
        /// </summary>
        public bool CanPasteClipboard()
            => _selectedPage != null && ScadaClipboard.HasPayload;

        #endregion

        #region 图元右键菜单（图层归属 + 叠放次序）

        /// <summary>右键菜单里"未分层"那一项的显示名（图元 <see cref="ScadaElement.LayerId"/> 为 <see cref="Guid.Empty"/> 时）</summary>
        public const string UnassignedLayerName = "（未分层）";

        /// <summary>
        /// 菜单图标（Font Awesome 6 Pro Solid 的码点，与宿主 <c>ContextMenu</c> 上的
        /// <c>{StaticResource Icon}</c> 字体配套）。图标放在视图模型里的理由与
        /// <see cref="ScadaZMoveExtensions.DisplayName"/> 一致：这是"菜单长什么样"的唯一一份知识，
        /// 宿主只负责按字体把字形渲染出来。
        /// </summary>
        private const string LayerIcon = "\uF5FD";

        private const string ZOrderIcon = "\uF0C9";

        /// <summary>垃圾桶（Font Awesome 6 Pro Solid）；与图层/叠放两个图标同属一套字形，避免混搭风格</summary>
        private const string DeleteIcon = "\uF2ED";

        /// <summary>
        /// "对齐与分布"分组图标（<c>align-justify</c>）。
        ///
        /// 挑它当分组图标而不是拿六种对齐里的某一个：分组图标若与某个子项同形，
        /// 用户扫一眼会以为"这一栏就是那个动作"。<c>align-justify</c> 是唯一一个
        /// 表达"把一堆东西摆整齐"而不指某个具体方向、且不占子项名额的字形。
        /// </summary>
        private const string AlignIcon = "\uF039";

        /// <summary>
        /// 锁定 / 解锁（Font Awesome 6 Pro Solid 的 <c>lock</c> / <c>lock-open</c>）。
        ///
        /// 图标跟着<b>动作</b>走而不是跟着状态走：菜单标题写的是"点下去会发生什么"
        /// （"锁定图元"），图标若是当前状态（未锁时画一把开着的锁）就会与标题互相打架。
        /// 与图层面板那两颗行内按钮同一套字形，用户在两个地方看到的是同一个符号。
        /// </summary>
        private const string LockIcon = "\uF023";

        private const string UnlockIcon = "\uF3C1";

        /// <summary>
        /// 组合 / 取消组合（Font Awesome 6 Pro Solid 的 <c>object-group</c> / <c>object-ungroup</c>）。
        ///
        /// 与锁定那一对同样的口径：图标跟着<b>动作</b>走而不是跟着状态走，标题写的是
        /// "点下去会发生什么"，图标就得是同一个方向；两者都画成"一个外框圈住几块"的同一族字形，
        /// 差别只在框里那几块是并在一起还是散开——这正是组合与取消组合的区别本身。
        /// </summary>
        private const string GroupIcon = "\uF247";

        private const string UngroupIcon = "\uF248";

        /// <summary>
        /// 复制 / 粘贴 / 再制（Font Awesome 6 Pro Solid 的 <c>copy</c> / <c>paste</c> / <c>clone</c>）。
        ///
        /// 三个图标刻意同族："复制"是两页纸，"粘贴"是夹板上一页纸，"再制"是两个摞在一起的方块——
        /// 都表达"内容被搬了一份"，用户扫一眼就知道这四项是一组。
        /// "再制"不用 <c>copy</c> 的重复是因为它要跟"复制"区分开：两者在菜单里紧挨着摆。
        /// </summary>
        private const string CopyIcon = "\uF0C5";

        private const string PasteIcon = "\uF0EA";

        private const string DuplicateIcon = "\uF24D";

        /// <summary>
        /// 存为模板（<c>bookmark</c>）。
        ///
        /// 用书签而不是软盘（<c>floppy-disk</c>）：软盘在这个软件里已经等同于"保存方案"，
        /// 摆进图元菜单会被读成"把方案存了"。书签表达的是"把这一份收起来，以后还能取用"，
        /// 正是模板的语义。
        /// </summary>
        private const string SaveTemplateIcon = "\uF02E";

        /// <summary>叠放次序在菜单里的排列：从上到下就是"从最前到最底"，与用户的心智模型一致</summary>
        private static readonly ScadaZMove[] ZMoveOrder =
        {
            ScadaZMove.ToFront,
            ScadaZMove.Forward,
            ScadaZMove.Backward,
            ScadaZMove.ToBack,
        };

        /// <summary>
        /// 排列动作在菜单里的顺序：先六种对齐（水平三个、垂直三个），再两种分布。
        ///
        /// 分组方式照搬用户脑中的那张表：对齐是"往哪条边贴"，分布是"把间隙摊平"，
        /// 两件事的前置条件也不同（分布至少要三个），所以不能交错排——
        /// 交错之后"为什么这一项是灰的"就得逐项去数，而按组排一眼就知道是"选得不够多"。
        ///
        /// 与 <see cref="ZMoveOrder"/> 一样是常量表而不是散在各处的 new DelegateCommand：
        /// 菜单顺序只写这一份，将来插入新动作不会漏改某一处。
        /// </summary>
        private static readonly ScadaAlign[] AlignOrder =
        {
            ScadaAlign.Left,
            ScadaAlign.HorizontalCenter,
            ScadaAlign.Right,
            ScadaAlign.Top,
            ScadaAlign.VerticalCenter,
            ScadaAlign.Bottom,
            ScadaAlign.DistributeHorizontal,
            ScadaAlign.DistributeVertical,
        };

        /// <summary>
        /// 组装"选中图元"的右键菜单结构。
        ///
        /// 为什么返回一份描述而不是在这里造 <c>MenuItem</c>：视图模型不该认识 WPF 控件；
        /// 而菜单的<b>内容</b>（这张画面有哪些图层、哪一项该打勾、哪一项该灰）全是模型知识，
        /// 留在这一层才能被断言直接验，不必去离屏渲染里数控件。
        /// 视图那一侧只做"描述 → MenuItem"的机械翻译（见 ScadaEditorView.xaml.cs）。
        ///
        /// 没有选中图元时返回空表：菜单里每一项都要落在"选中的那个图元"上，
        /// 没有目标时弹出来只会是一排灰按钮，不如不弹。
        /// </summary>
        public IReadOnlyList<ScadaMenuItem> BuildElementContextMenu()
        {
            if (_selectedPage is not { } page || _selectedElement is not { } element)
                return Array.Empty<ScadaMenuItem>();

            // 图层：一图元只归一层，所以是"单选"式的一列，当前归属打勾；末尾补"未分层"
            //（脏数据与手工取消分层的落点，也是 TryAssignLayer(element, null) 唯一的入口）。
            // 每项各自闭包捕获自己的目标，因此不需要 CommandParameter——泛型 DelegateCommand<T>
            // 在参数为 null 时 CanExecute 一律判假，"未分层"用参数传 null 会被整项灰掉，这个坑不踩。
            var layers = new List<ScadaMenuItem>();
            foreach (var layer in page.Layers)
            {
                var target = layer;
                layers.Add(ScadaMenuItem.Choice(
                    target.Name,
                    new DelegateCommand(() => AssignSelectedLayer(target)),
                    isChecked: element.LayerId == target.LayerId));
            }

            layers.Add(ScadaMenuItem.Choice(
                UnassignedLayerName,
                new DelegateCommand(() => AssignSelectedLayer(null)),
                isChecked: element.LayerId == Guid.Empty));

            // 叠放：四个方向；已经到端点的那一项由命令的 CanExecute 判灰，
            // 判据与 ScadaPage.TryMoveElementZ 同源（见 CanMoveSelectedZ）。
            var moves = new List<ScadaMenuItem>();
            foreach (var move in ZMoveOrder)
            {
                var target = move;
                moves.Add(ScadaMenuItem.Action(
                    target.DisplayName(),
                    IconOf(target),
                    new DelegateCommand(() => MoveSelectedZ(target), () => CanMoveSelectedZ(target))));
            }

            // 对齐与分布：八个动作共用一条判灰规则（选中数够不够），差异只在
            // <see cref="ScadaAlignExtensions.MinimumCount"/> 那一个数字上——
            // 所以这里不写 switch，判据整个交给 CanAlignSelected，与领域层前置校验同源。
            //
            // 单选中时整栏判灰而不是整栏不显示：一栏时隐时现的菜单，用户永远学不会
            // "还有对齐这回事"；灰着摆在那里，"再选一个就能用"是能自己看出来的。
            var aligns = new List<ScadaMenuItem>();
            foreach (var align in AlignOrder)
            {
                var target = align;
                aligns.Add(ScadaMenuItem.Action(
                    target.DisplayName(),
                    IconOf(target),
                    new DelegateCommand(() => AlignSelected(target), () => CanAlignSelected(target))));
            }

            // 位次写在分组标题里：属性面板撤掉"叠放次序"行之后，这里是用户唯一能读到
            // "我在第几层"的地方（数越小越靠后，与 TryMoveElementZ 的编号口径一致）。
            bool locked = IsMainSelectionLocked;
            bool grouped = IsMainSelectionGrouped;

            return new[]
            {
                ScadaMenuItem.Submenu("图层", LayerIcon, layers),
                ScadaMenuItem.Submenu($"叠放次序（{ReadPosition(page, element)}）", ZOrderIcon, moves),
                ScadaMenuItem.Submenu("对齐与分布", AlignIcon, aligns),

                // 剪贴板三件（复制 / 粘贴 / 再制）紧挨着摆、不折进子菜单：它们是用得最勤的一组，
                // 折一层就多一次移动和一次判断。三项判灰各自跟着自己的命令走——
                // "复制"要选中集合非空，"粘贴"要有货且有画面，判据全在命令那一侧，这里不重算。
                ScadaMenuItem.Action("复制", CopyIcon, CopyCommand),
                ScadaMenuItem.Action("粘贴", PasteIcon, PasteCommand),
                ScadaMenuItem.Action("再制", DuplicateIcon, DuplicateCommand),

                // 存为模板排在再制之后、锁定之前：它跟上面三项同属"把选中的东西变成一份可再用的内容"，
                // 而与下面两项"改变图元之间的关系/保护状态"分开。标题带省略号——点下去还会问一个名字，
                // 不带省略号就是骗用户"点一下就完了"（这个输入框正是它没做成 Ctrl 快捷键的原因）。
                ScadaMenuItem.Action("存为模板…", SaveTemplateIcon, SaveTemplateCommand),

                // 锁定/解锁一项二态（标题与图标都跟着主选中的当前状态走），排在删除之前：
                // 它与删除同属"保护/破坏画面内容"的一组，而与上面三栏"只换个摆法"分开。
                // 它作用于<b>整批</b>选中（多选一次锁住一片底图是常态），标题里不写数量——
                // "几个真的会变"要按 TrySetElementLocked 的过滤算，写在这里就是第二份口径；
                // 数量已经落在撤销记录的操作名里（"锁定 3 个图元"），Ctrl+Z 的提示上看得见。
                ScadaMenuItem.Action(locked ? "解锁图元" : "锁定图元", locked ? UnlockIcon : LockIcon,
                    LockElementCommand),

                // 组合/取消组合一项二态（标题与图标都跟着主选中的分组状态走），紧随锁定之后：
                // 两者同属"改变图元之间的关系/保护状态"，都与删除的"改变画面内容"分开。
                //
                // 排在删除<b>之前</b>而不是末尾：菜单末尾那一格留给唯一不可逆的破坏性动作，
                // 这是整份菜单的排布约定，多一项就往前挪一格，不能插到删除后面去。
                //
                // 标题不写数量（"组合 3 个图元"）：真正会变的个数要按领域层的过滤算
                //（已在同一组的成员是空操作、图元可能已不在本画面上），写在这里就是第二份口径；
                // 数量落在撤销记录的操作名里，Ctrl+Z 的提示上看得见——与锁定那一项同一条理由。
                ScadaMenuItem.Action(grouped ? "取消组合" : "组合",
                    grouped ? UngroupIcon : GroupIcon,
                    GroupElementCommand),

                // 删除放最后：它是唯一"不可见地改变画面内容"的一项（另外三栏都只是换个摆法），
                // 排在末尾才不会被误点。锁定的图元这一项判灰——拖动都拖不动的东西，
                // 却能从菜单里删掉，那种不一致在现场会被当成"锁没生效"。
                ScadaMenuItem.Action("删除图元", DeleteIcon,
                    new DelegateCommand(() => RemoveSelectedElement(), CanRemoveSelectedElement)),
            };
        }

        /// <summary>
        /// 把选中图元改归到 <paramref name="layer"/>（null = 取消分层）。
        /// 写入口只有 <see cref="ScadaPage.TryAssignLayer"/> 一条，这里不自己动 <see cref="ScadaElement.LayerId"/>。
        /// 失败不弹提示：菜单里的候选与目标都取自同一张画面，正常路径下不可能失败。
        ///
        /// 只作用于<b>主选中</b>那一个（与叠放次序一致），不做整批赋值：菜单上的打勾状态
        /// 只能表达"一个归属"，整批赋值时多选若跨了层，就会渲染出"一项都没打勾"的空档，
        /// 用户看不出这批图元现在算哪一层——那比少一个批量功能更难解释。
        /// </summary>
        public bool AssignSelectedLayer(ScadaLayer? layer)
            => _selectedPage is { } page
               && _selectedElement is { } element
               && page.TryAssignLayer(element, layer, out _);

        /// <summary>
        /// 把选中的整批图元设成"锁定 / 解锁"。
        ///
        /// 写入口只有 <see cref="ScadaPage.TrySetElementLocked"/> 一条，理由与对齐同源：
        /// "哪些图元该被改"的过滤（尤其是<b>不能筛掉已锁定的</b>那一条）只该有一个答案，
        /// 视图模型再筛一遍就会出现"菜单能点、点了没反应"的解锁死锁。
        ///
        /// 传 <c>_selectedElements</c> 而不是 <see cref="CollectEditable"/> 的结果：
        /// 解锁的输入必须包含已经锁上的那些（见上）。这里改用 <see cref="CollectPresent"/>
        /// 只筛"还在不在本画面上"，与领域层的过滤口径对齐但不多筛一层。
        ///
        /// 失败不弹提示：菜单里的目标与选中项同源，正常路径下只有"没选中"一种失败，
        /// 而那一种已经由 <see cref="CanSetSelectedLocked"/> 判灰挡住了。
        /// </summary>
        public bool SetSelectedLocked(bool locked)
            => _selectedPage is { } page
               && page.TrySetElementLocked(CollectPresent(page), locked, out _);

        /// <summary>
        /// 菜单那一项点下去做什么：把整批设成<b>主选中当前状态的相反值</b>。
        ///
        /// 为什么以"主选中"为准而不是"只要有一个没锁就锁上"：
        /// 菜单标题只能写一件事（"锁定图元"或"解锁图元"），标题与实际动作必须是同一个判据，
        /// 否则会出现"菜单写着解锁、点下去把旁边两个也锁上了"。多选混合状态时以主选中为准，
        /// 与"图层"栏打勾那种单选式口径一致——点一次是<b>把这一批拉齐到同一个状态</b>，
        /// 这也是用户点它时心里想的事。
        /// </summary>
        public bool ToggleSelectedLock() => SetSelectedLocked(!IsMainSelectionLocked);

        /// <summary>
        /// 主选中图元现在锁没锁（菜单标题与图标都读它）。
        /// 没选中时返回 false：此时菜单项本就判灰，返回值只用于算标题，不该让标题写"解锁"。
        /// </summary>
        public bool IsMainSelectionLocked => _selectedElement?.IsLocked == true;

        /// <summary>
        /// 锁定/解锁这一项能不能点：得有"确实在本画面上"的选中图元。
        ///
        /// 刻意<b>不</b>复用 <see cref="CanRemoveSelectedElement"/> 那条"可编辑"判据：
        /// 全批都已锁定时，删除确实该灰，但解锁恰恰是唯一该亮着的动作。
        /// </summary>
        public bool CanSetSelectedLocked()
            => _selectedPage is { } page
               && CollectPresent(page).Count > 0;

        /// <summary>
        /// 把选中的整批图元合成一组。
        ///
        /// 写入口只有 <see cref="ScadaPage.TryGroupElements"/> 一条："哪些图元能进组"
        /// （属于本画面 / 去重 / 已在同一组算空操作 / 不足 2 个拒绝）只该有一个答案，
        /// 视图模型再筛一遍就会出现"菜单能点、点了没反应"。
        ///
        /// 传 <see cref="CollectPresent"/> 而不是 <see cref="CollectEditable"/>：组合<b>不筛锁定</b>——
        /// 把锁住的底图和旁边的标注捆在一起是合理诉求，锁定已经保证它不会被拖走。
        /// 这与 <see cref="AlignSelected"/> 那条"只动可编辑的"是反着的两份口径，都是刻意的。
        ///
        /// 判据（<see cref="CanGroupSelected"/>）与动作传的是<b>同一份</b>集合：
        /// 各筛一遍迟早筛出两个集合，那正是"菜单亮着、点了没反应"的来源。
        /// </summary>
        public bool GroupSelected()
            => _selectedPage is { } page
               && page.TryGroupElements(CollectPresent(page), out _);

        /// <summary>
        /// 把选中的图元从各自所在的组里摘出来。
        ///
        /// 同样只走 <see cref="ScadaPage.TryUngroupElements"/>，同样<b>不筛锁定</b>：
        /// 与组合共用口径，否则"锁住之后就再也解不开"。
        /// 刻意不要求"必须选中整组"——组只是模型里的一个 Guid，加这条校验只会给
        /// 撤销中间态与脏数据各留一条走不通的路。
        /// </summary>
        public bool UngroupSelected()
            => _selectedPage is { } page
               && page.TryUngroupElements(CollectPresent(page), out _);

        /// <summary>
        /// 菜单那一项点下去做什么：主选中在组里就<b>取消组合</b>，否则<b>组合</b>。
        ///
        /// 与 <see cref="ToggleSelectedLock"/> 同一条理由：菜单标题只能写一件事，
        /// 标题与实际动作必须是同一个判据，否则会出现"菜单写着取消组合、点下去把旁边两个也组上了"。
        /// 多选混合（一半在组里、一半不在）时以主选中为准——点一次是把这一批<b>拉齐</b>到同一个状态，
        /// 与"图层"栏那种单选式打勾口径一致，也是用户点它时心里想的事。
        /// </summary>
        public bool ToggleSelectedGroup() => IsMainSelectionGrouped ? UngroupSelected() : GroupSelected();

        /// <summary>
        /// 主选中图元在不在一个组里（菜单标题与图标都读它）。
        ///
        /// 判据只看主选中自己那个 Guid，不去数"选中里有没有两个同组的"：标题必须与
        /// <see cref="ToggleSelectedGroup"/> 走同一分支，而那个动作问的正是主选中。
        /// 没选中时返回 false——此时菜单项本就判灰，返回值只用于算标题，不该让标题写"取消组合"。
        /// </summary>
        public bool IsMainSelectionGrouped => _selectedElement is { } element && element.GroupId != Guid.Empty;

        /// <summary>组合这一项能不能点：确实在本画面上的选中图元至少 2 个（与领域层的前置校验同源）</summary>
        public bool CanGroupSelected()
            => _selectedPage is { } page
               && CollectPresent(page).Count >= 2;

        /// <summary>
        /// 取消组合这一项能不能点：确实在本画面上、且<b>真的在某个组里</b>的选中图元至少 1 个。
        ///
        /// 数的是"有组的"而不是"选中的"：全都没组时点下去是空操作
        /// （<see cref="ScadaPage.TryUngroupElements"/> 返回 true 但不产记录），
        /// 菜单亮着却什么都没发生，用户只会记成"这一项坏了"。
        /// </summary>
        public bool CanUngroupSelected()
            => _selectedPage is { } page
               && CollectPresent(page).Exists(e => e.GroupId != Guid.Empty);

        /// <summary>
        /// 组合/取消组合这一项能不能点：走哪一边由主选中决定，可用性就按那一边算
        /// （与 <see cref="ToggleSelectedGroup"/> 同一分支，否则又会出现"亮着但点了没反应"）。
        /// </summary>
        public bool CanToggleSelectedGroup()
            => IsMainSelectionGrouped ? CanUngroupSelected() : CanGroupSelected();

        /// <summary>把选中图元按 <paramref name="move"/> 挪一次叠放次序（同样只走模型写入口）</summary>
        public bool MoveSelectedZ(ScadaZMove move)
            => _selectedPage is { } page
               && _selectedElement is { } element
               && page.TryMoveElementZ(element, move, out _);

        /// <summary>
        /// 把选中的<b>整组</b>图元按 <paramref name="align"/> 摆整齐。
        ///
        /// 写入口只有 <see cref="ScadaPage.TryAlignElements"/> 一条：包围盒怎么算、
        /// 哪些图元该被跳过、锁定怎么处理、一条撤销记录怎么罩，全在领域层那一处，
        /// 这里只是把"选中的这一批"递进去。视图模型自己再算一遍包围盒，
        /// 就会出现"画布上看着对齐了、撤销一下位置又不对"这类算式劈叉。
        ///
        /// 传 <c>_selectedElements</c> 而不是 <c>CollectEditable</c> 的结果：
        /// 领域层内部本来就要按"属于本画面 / 未锁定"再筛一遍，这里先筛一次是白算；
        /// 更要紧的是<b>包围盒必须由同一批图元算出来</b>，两边各筛一遍迟早筛出两个集合。
        /// </summary>
        public bool AlignSelected(ScadaAlign align)
            => _selectedPage is { } page
               && page.TryAlignElements(_selectedElements, align, out _);

        /// <summary>
        /// 这一项排列动作现在能不能点：可编辑的选中图元数够不够
        /// （对齐 2 个、分布 3 个，下界取自 <see cref="ScadaAlignExtensions.MinimumCount"/>）。
        ///
        /// 数的是<b>可编辑</b>的而不是"选中的"：多选里混进一个锁住的底图时，
        /// 真正会动的只有其余几个，拿选中总数判就会出现"菜单亮着、点了只挪了一半"。
        /// </summary>
        public bool CanAlignSelected(ScadaAlign align)
            => _selectedPage is { } page
               && CollectEditable(page).Count >= align.MinimumCount();

        /// <summary>
        /// 删除这一项能不能点：得有"能编辑的"选中图元（口径与 <see cref="RemoveSelectedElement"/> 同源）。
        ///
        /// 不写成"主选中能编辑就行"：删除键走的是整批，多选里主选中恰好是锁定的那个时，
        /// 菜单判灰、Delete 键却能删掉旁边几个——同一件事两个答案，现场只会记成"菜单坏了"。
        /// </summary>
        public bool CanRemoveSelectedElement()
            => _selectedPage is { } page
               && CollectEditable(page).Count > 0;

        /// <summary>这个方向还有没有意义：已在最上就没有"上移一层"，已在最下就没有"下移一层"</summary>
        public bool CanMoveSelectedZ(ScadaZMove move)
            => _selectedPage is { } page
               && _selectedElement is { } element
               && IndexInZOrder(page, element) is { } index
               && (move is ScadaZMove.ToFront or ScadaZMove.Forward
                       ? index < page.Elements.Count - 1
                       : index > 0);

        /// <summary>
        /// 按叠放次序排序后该图元的位次（0 = 最靠后）；不在本画面返回 null。
        ///
        /// 必须用 <c>OrderBy</c> 的稳定排序，与 <see cref="ScadaPage.TryMoveElementZ"/> 的判序口径一致——
        /// 两处各排一次，就会出现"按钮说还能上移、点了却不动"这种对不上的毛病。
        /// </summary>
        private static int? IndexInZOrder(ScadaPage page, ScadaElement element)
        {
            int index = 0;

            foreach (var item in page.Elements.OrderBy(e => e.ZIndex))
            {
                if (ReferenceEquals(item, element))
                    return index;

                index++;
            }

            return null;
        }

        /// <summary>菜单标题里的位次文案："2 / 5"；图元不在本画面时退化成破折号</summary>
        private static string ReadPosition(ScadaPage page, ScadaElement element)
            => IndexInZOrder(page, element) is { } index
                ? $"{index + 1} / {page.Elements.Count}"
                : "—";

        /// <summary>四个叠放方向各配一个箭头：指向"往哪儿挪"，一眼就懂</summary>
        private static string IconOf(ScadaZMove move) => move switch
        {
            ScadaZMove.ToFront => "\uF102",
            ScadaZMove.Forward => "\uF062",
            ScadaZMove.Backward => "\uF063",
            ScadaZMove.ToBack => "\uF103",
            _ => ZOrderIcon,
        };

        /// <summary>
        /// 八个排列动作各配一个图标（Font Awesome 6 Pro Solid）。
        ///
        /// 选型口径：水平三兄弟用 <c>align-left / align-center / align-right</c>（F036/F037/F038），
        /// 垂直三兄弟用与它们同族的竖直版本（F341/F034/F33D）——水平组和垂直组必须是
        /// 同一套画法转 90°，否则"顶对齐"和"左对齐"看起来会像两个软件里的东西。
        /// 分布另用 <c>F337/F338</c>（等距排布的方块），与前六个区分开：
        /// 分布不是"贴到某条边"，图标跟着不一样，用户才不会把它当成第七种对齐。
        ///
        /// 全部落在 Font Awesome 6 Pro Solid 里实测存在的码点上。字形缺一个就会渲染成
        /// 空心方框，而那种"豆腐块"在深色菜单上极难发现——所以 <c>ScadaChecks</c> 里
        /// 有一条断言逐个 <c>CharacterToGlyphMap.ContainsKey</c> 验这八个码点，
        /// 改动这里必须同时过那条断言。
        ///
        /// 表里没写的取值退化成分组图标：将来枚举加了新动作而忘了配图标，
        /// 是"图标不好看"，不是"菜单空白"。
        /// </summary>
        private static string IconOf(ScadaAlign align) => align switch
        {
            ScadaAlign.Left => "\uF036",
            ScadaAlign.HorizontalCenter => "\uF037",
            ScadaAlign.Right => "\uF038",
            ScadaAlign.Top => "\uF341",
            ScadaAlign.VerticalCenter => "\uF034",
            ScadaAlign.Bottom => "\uF33D",
            ScadaAlign.DistributeHorizontal => "\uF337",
            ScadaAlign.DistributeVertical => "\uF338",
            _ => AlignIcon,
        };

        #endregion

        private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 只认 CurrentSolution：加载方案是"整个 SolutionModel 换掉"，
            // 文档（Scada）跟着新实例一起来，不需要再单独盯 SolutionModel.Scada
            if (e.PropertyName != nameof(IReadOnlyWorkspaceContext.CurrentSolution)) return;

            OnDocumentChanged();
        }

        /// <summary>
        /// 换方案后重对齐：先让界面按新文档重画画面列表，再挑一个当前画面。
        ///
        /// 顺序很要紧——先 <c>RaisePropertyChanged(Pages)</c> 让下拉框换掉 ItemsSource
        /// （它会顺手把选中项清成 null），之后赋的 SelectedPage 才是最终值。反过来写
        /// 就是"选了新方案第一页，界面却停在空白"。
        /// </summary>
        private void OnDocumentChanged()
        {
            // 撤销栈是静态单栈（全局一份）。换方案后若还留着上一条方案的记录，
            // 一句 Ctrl+Z 就会去改已经不在屏幕上的旧文档对象——那种崩法最难反查。
            //
            // 判据用"文档实例是否真的换了"，而不是"Activate 被调过"：
            // AvalonDock 切标签会让视图离树再回树，若每次入树都清栈，
            // 用户"看一眼流程面板再回来"就丢光了撤销历史。
            if (!ReferenceEquals(_historyDocument, Document))
            {
                _historyDocument = Document;
                ScadaEditHistory.Clear();
            }

            RaisePropertyChanged(nameof(Document));
            RaisePropertyChanged(nameof(Pages));
            AddPageCommand.RaiseCanExecuteChanged();
            RunPageCommand.RaiseCanExecuteChanged();

            var pages = Pages;
            SelectedPage = pages.Count > 0 ? pages[0] : null;
        }
    }
}
