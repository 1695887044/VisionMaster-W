using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using UI.CustomControl;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;
using VisionMaster.Views.Controls;

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

        /// <summary>
        /// 方案级弹窗出口。可选：断言工程只验画面管理，不构造 Prism 容器；
        /// 为 null 时「报警配置」按钮保持可用性判断（不静默空跑）。
        /// </summary>
        private readonly IDialogService? _dialogService;

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

        /// <summary>等间距动作的"相邻留多少像素"（见 <see cref="SpacingGap"/>）；一次操作的参数，不落盘</summary>
        private double _spacingGap = 20;

        public ScadaEditorViewModel(IWorkspaceManager workspace, IScadaRuntimeHost runtime, IDialogService? dialogService = null)
        {
            _workspace = workspace;
            _runtime = runtime;
            _dialogService = dialogService;
            _workspaceChanged = workspace as INotifyPropertyChanged;

            AddPageCommand = new DelegateCommand(OnAddPage, () => Document != _fallback);
            AddBasePageCommand = new DelegateCommand(OnAddBasePage, () => Document != _fallback);
            AddPopupPageCommand = new DelegateCommand(OnAddPopupPage, () => Document != _fallback);
            ClearBasePagesCommand = new DelegateCommand(OnClearBasePages, () => CanSelectBasePage);
            RemovePageCommand = new DelegateCommand(OnRemovePage, CanRemovePage);
            RunPageCommand = new DelegateCommand(OnRunPage, CanRunPage);
            OpenAlarmConfigCommand = new DelegateCommand(OnOpenAlarmConfig, CanOpenAlarmConfig);
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

            // 装入底板 / 移出底板：入口同样只有右键菜单（判灰条件是视图模型的知识）。
            // 与组合那一项同构，但两向拆成两条命令而不是一条二态——
            // 差别在于"装入"要选一只底板当靶，而"移出"不要；
            // 一条命令兼两向就得在动作里判"这次该拿谁当底板"，而菜单标题只写得下一件事。
            // 靶的选取见 <see cref="ResolveDockTarget"/>（主选中若不是底板，就找本页第一只空底板）。
            DockIntoPanelCommand = new DelegateCommand(() => DockSelectedIntoPanel(), CanDockSelected);
            UndockFromPanelCommand = new DelegateCommand(() => UndockSelected(), CanUndockSelected);

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
            BatchInsertTemplateCommand = new DelegateCommand(OnBatchInsertTemplate, () => _selectedPage != null);

            // 格式工具栏（r5）。上面那一批命令的入口都只有"右键菜单 + 快捷键"，
            // 而右键菜单里那些项是<b>每次弹出就地 new 一条命令</b>（所以它们的灰亮天然是最新的）；
            // 工具栏上的按钮是长期挂在可视树上的，必须有自己的属性型命令，
            // 并在选中变化时显式 RaiseCanExecuteChanged（见 RefreshSelectionCommands）。
            //
            // 动作与判据都直接转调既有方法（AlignSelected / CanAlignSelected 等），不另写一份：
            // 工具栏与右键菜单的灰亮必须同源，各写一遍迟早出现"菜单能点、工具栏灰着"。
            //
            // 用参数型命令（DelegateCommand&lt;T&gt;）而不是每种动作一个属性：八个对齐各占一个属性，
            // 就是把同一件事抄八遍，而工具栏那 8 个按钮本来就只差一个枚举值
            //（参数由 XAML 的 CommandParameter 常量给，不会被外部构造，与 NudgeCommand 同一取舍）。
            // 泛型参数一律用<b>可空枚举</b>（ScadaAlign? 而不是 ScadaAlign）：
            // Prism 的 DelegateCommand&lt;T&gt; 要求 T 是引用类型或可空类型，裸枚举在构造时
            // 会抛 InvalidCastException（"T ... is not an object nor Nullable"）。
            // 可空之后 XAML 传进来的常量（值类型）装箱成可空没有问题，动作里再判 is { } 取值。
            AlignCommand = new DelegateCommand<ScadaAlign?>(a => AlignSelected(a), a => CanAlignSelected(a));
            MoveZCommand = new DelegateCommand<ScadaZMove?>(m => MoveSelectedZ(m), m => CanMoveSelectedZ(m));
            MatchSizeCommand = new DelegateCommand<ScadaSizeMatch?>(m => MatchSelectedSize(m), m => CanMatchSelectedSize(m));
            SpaceCommand = new DelegateCommand<ScadaSpacing?>(s => SpaceSelected(s), s => CanSpaceSelected(s));
            SnapElementsToGridCommand = new DelegateCommand(() => SnapSelectedToGrid(), CanSnapSelectedToGrid);

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
            ScadaTemplateStore.Shared.TemplateUpdated += OnTemplateUpdated;

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
            ScadaTemplateStore.Shared.TemplateUpdated -= OnTemplateUpdated;

            // 候选项订阅的是文档的集合：离树时摘掉，避免"切几次标签攒几个影子编辑器"（与上面同一条理由）
            if (_optionsDocument != null)
            {
                _optionsDocument.BasePages.CollectionChanged -= OnBasePagesCollectionChanged;
                _optionsDocument = null;

                foreach (var option in BasePageOptions)
                    option.Detach();

                BasePageOptions.Clear();

                foreach (var selection in BasePageSelections)
                    selection.Detach();

                BasePageSelections.Clear();
            }
        }

        /// <summary>当前方案的组态文档（无方案时为空的兜底文档）</summary>
        public ScadaDocument Document => _workspace.CurrentSolution?.Scada ?? _fallback;

        /// <summary>是否已经打开了方案（用于属性面板区分“无方案”与“有方案但没有画面”）</summary>
        public bool HasSolution => _workspace.CurrentSolution != null;

        /// <summary>当前是否选中了画面</summary>
        public bool HasPage => SelectedPage != null;

        /// <summary>画面集合（模型本体，供画面下拉/标签绑定）</summary>
        public ObservableCollection<ScadaPage> Pages => Document.Pages;

        /// <summary>
        /// 可作为基座的画面集合（模型本体，供基座下拉框绑定）。
        ///
        /// 基座与画面是两个集合：基座不许被导航（它不是终点）、删除时不连带删引用它的画面
        /// （引用方按"无基座"降级）。所以下拉框的候选只能来自 <see cref="ScadaDocument.BasePages"/>，
        /// 不能拿全部画面充数——那种"顺手把 Pages 也放进去"的做法会让"建页时随便挑一张当基座"
        /// 成为可能，而那种页面一删就全链坏掉。
        /// </summary>
        public ObservableCollection<ScadaPage> BasePages => Document.BasePages;

        /// <summary>
        /// 顶栏基座下拉的候选项：<b>第一项恒为「（无基座）」</b>（<see cref="BasePageOption.None"/>），
        /// 其余是 <see cref="BasePages"/> 里的基座。
        ///
        /// <b>为什么不用 CompositeCollection + ComboBoxItem 占位</b>：那条路上选「（无基座）」时，
        /// WPF 会把 <c>ComboBoxItem</c> 往 <c>ScadaPage?</c> 类型的属性里塞——绑定引擎转换失败，
        /// 源属性根本收不到通知，于是"选了清除、模型没变、界面还显示（无基座）"这种静默失败。
        /// 让候选项与选中项是<b>同一个类型</b>（<see cref="BasePageOption"/>），类型不匹配的问题从根上消失。
        /// </summary>
        public ObservableCollection<BasePageOption> BasePageOptions { get; } = new();

        /// <summary>
        /// 顶栏「基座」多选菜单的候选项：每只已注册基座一行，<see cref="BasePageSelection.IsSelected"/>
        /// 表示本页是否引用它。勾选/取消直接调文档的引用管理方法（含环检测与一层限制）。
        /// </summary>
        public ObservableCollection<BasePageSelection> BasePageSelections { get; } = new();

        /// <summary>顶栏按钮上的摘要：无基座 / 一只基座名 / "名字 等 N 个"。</summary>
        public string BasePageSummary
        {
            get
            {
                var bases = SelectedBasePages;

                return bases.Count switch
                {
                    0 => "（无基座）",
                    1 => bases[0].Name,
                    _ => $"{bases[0].Name} 等 {bases.Count} 个",
                };
            }
        }

        /// <summary>当前页真正生效的基座列表（画布按它逐层叠；顺序 = 文档里的引用顺序）。</summary>
        public IReadOnlyList<ScadaPage> SelectedBasePages
            => Document.ResolveBasePages(_selectedPage);

        public DelegateCommand ClearBasePagesCommand { get; }

        private ScadaDocument? _optionsDocument;

        /// <summary>上一次重建候选项时当前页的基座引用；撤销/重做只在这份快照真的变了时才重建。</summary>
        private List<Guid> _lastBasePageIds = new();

        /// <summary>重建基座候选项（文档换了 / 基座集合变了都要重建）</summary>
        private void RebuildBasePageOptions()
        {
            var document = Document;

            if (!ReferenceEquals(_optionsDocument, document))
            {
                if (_optionsDocument != null)
                    _optionsDocument.BasePages.CollectionChanged -= OnBasePagesCollectionChanged;

                _optionsDocument = document;
                _optionsDocument.BasePages.CollectionChanged += OnBasePagesCollectionChanged;
            }

            foreach (var option in BasePageOptions)
                option.Detach();

            BasePageOptions.Clear();
            BasePageOptions.Add(BasePageOption.None);

            foreach (var page in document.BasePages)
                BasePageOptions.Add(new BasePageOption(page));

            // 多选菜单与单选视图同源：都从文档的 BasePages 建，勾选状态取当前页的引用列表。
            foreach (var selection in BasePageSelections)
                selection.Detach();

            BasePageSelections.Clear();

            var selectedIds = _selectedPage?.BasePageIds ?? new List<Guid>();
            _lastBasePageIds = selectedIds.ToList();

            foreach (var page in document.BasePages)
                BasePageSelections.Add(new BasePageSelection(this, page, selectedIds.Contains(page.PageId)));

            RaisePropertyChanged(nameof(SelectedBasePage));
            RaisePropertyChanged(nameof(SelectedBasePages));
            RaisePropertyChanged(nameof(BasePageSummary));
            RaisePropertyChanged(nameof(CanSelectBasePage));
            ClearBasePagesCommand.RaiseCanExecuteChanged();
        }

        private void OnBasePagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => RebuildBasePageOptions();

        /// <summary>
        /// 顶栏基座下拉能不能选（= 当前页本身不是基座）。
        ///
        /// 一层限制（基座不能再引用基座）在模型里由 <c>TrySetBasePage</c> 拦，
        /// 但界面必须**先置灰**：让用户选一个注定被拒的值、再把下拉弹回原值，
        /// 是最让人以为"软件坏了"的交互。置灰 + 一句说明，规则就自解释了。
        /// </summary>
        public bool CanSelectBasePage => !Document.IsBasePage(_selectedPage);

        /// <summary>当前编辑的画面</summary>
        public ScadaPage? SelectedPage
        {
            get => _selectedPage;
            set
            {
                var previous = _selectedPage;
                if (!SetProperty(ref _selectedPage, value)) return;

                if (previous != null)
                    previous.PropertyChanged -= OnSelectedPagePropertyChanged;
                if (value != null)
                    value.PropertyChanged += OnSelectedPagePropertyChanged;

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
                BatchInsertTemplateCommand.RaiseCanExecuteChanged();

                // 基座下拉框跟着当前页走：换页时它要显示"这一页引用的是哪只基座"
                RaisePropertyChanged(nameof(HasPage));
                RaisePropertyChanged(nameof(SelectedBasePage));
                RaisePropertyChanged(nameof(CanSelectBasePage));
                RaiseBackgroundProperties();
                RebuildBasePageOptions();
            }
        }

        /// <summary>
        /// 当前方案目录（背景图等方案资产的解析根）；方案还没保存过时为 null。
        /// 属性面板的图片选择器与画布的背景图解析共用这一条。
        /// </summary>
        public string? SolutionRoot
            => string.IsNullOrWhiteSpace(_workspace.CurrentSolution?.SolutionFilePath)
                ? null
                : System.IO.Path.GetDirectoryName(_workspace.CurrentSolution.SolutionFilePath);

        /// <summary>
        /// 方案图片解析器（相对路径 → 位图），交给画布注入给 Hmi.Picture。
        /// 失败返回 null（图元显示占位），不抛、不弹窗。
        /// </summary>
        public Func<string, ImageSource?> ResolveImageAsset
            => relativePath => SolutionAssetService.TryLoad(
                    _workspace.CurrentSolution?.SolutionFilePath,
                    relativePath,
                    out var image,
                    out _)
                ? image
                : null;

        /// <summary>
        /// 当前页背景图（解析成位图给画布；null = 只用背景色）。
        /// 解析失败（方案未保存 / 文件缺失 / 解码失败）时返回 null，画布回退背景色——
        /// 编辑器不因此弹窗，属性面板那一行会显示“缺失”。
        /// </summary>
        public ImageSource? SelectedPageBackgroundImage
        {
            get
            {
                var page = SelectedPage;
                if (page == null || string.IsNullOrWhiteSpace(page.BackgroundImage))
                    return null;

                return SolutionAssetService.TryLoad(
                    _workspace.CurrentSolution?.SolutionFilePath,
                    page.BackgroundImage,
                    out var image,
                    out _)
                    ? image
                    : null;
            }
        }

        /// <summary>背景图填充方式（Tile 由 <see cref="SelectedPageBackgroundTile"/> 单独表达）</summary>
        public Stretch SelectedPageBackgroundStretch => ScadaBackgroundStretch.Parse(SelectedPage?.BackgroundStretch);

        /// <summary>背景图不透明度（0~1；模型是 10~100）</summary>
        public double SelectedPageBackgroundOpacity => ScadaBackgroundStretch.ToOpacity(SelectedPage?.BackgroundOpacity ?? 100d);

        /// <summary>背景图平铺（Stretch 表达不了平铺）</summary>
        public bool SelectedPageBackgroundTile => ScadaBackgroundStretch.IsTile(SelectedPage?.BackgroundStretch);

        private void OnSelectedPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ScadaPage.BackgroundImage)
                or nameof(ScadaPage.BackgroundStretch)
                or nameof(ScadaPage.BackgroundOpacity))
            {
                RaiseBackgroundProperties();
            }
        }

        private void RaiseBackgroundProperties()
        {
            RaisePropertyChanged(nameof(SelectedPageBackgroundImage));
            RaisePropertyChanged(nameof(SelectedPageBackgroundStretch));
            RaisePropertyChanged(nameof(SelectedPageBackgroundOpacity));
            RaisePropertyChanged(nameof(SelectedPageBackgroundTile));
        }

        /// <summary>
        /// 当前页引用的基座画面（属性面板与顶栏下拉框都绑它）。
        ///
        /// 读写都走 <see cref="ScadaDocument.TrySetBasePage"/>（含环检测与"目标是不是已注册基座"的校验）：
        /// 视图模型不自己写 <c>BasePageId</c>——"谁能设基座"的判据只有一份，
        /// 写岔了的后果（循环引用、引用未注册对象）不该由界面层各写一份来兜。
        ///
        /// 失败时静默返回：环检测与"未注册基座"两条拒由在正常界面路径上不可达
        /// （候选列表就只有已注册基座），而撤销/重做/脚本路径由领域层负责。
        /// 切换成功的观感是即时的（画布按 BasePage 依赖属性回调全量重建）。
        /// </summary>
        public BasePageOption? SelectedBasePage
        {
            get
            {
                if (_selectedPage is not { } page)
                    return null;

                var basePage = Document.FindBasePage(page.BasePageId);

                return basePage == null
                    ? BasePageOption.None
                    : BasePageOptions.FirstOrDefault(o => ReferenceEquals(o.Page, basePage))
                      // 候选项还没重建好时的兜底：<b>不订阅</b>改名。
                      // getter 每次绑定求值都会跑，在这里 new 一个会订阅的项 = 每次求值多挂一个订阅（泄漏）。
                      ?? BasePageOption.Detached(basePage);
            }
            set
            {
                if (_selectedPage is not { } page)
                    return;

                var target = value?.Page;

                if (ReferenceEquals(Document.FindBasePage(page.BasePageId), target))
                    return;

                Document.TrySetBasePage(page, target, out _);
                RebuildBasePageOptions();
            }
        }

        /// <summary>
        /// 多选菜单里勾/取消一只基座：写文档的引用列表，然后刷新摘要与画布绑定。
        /// 文档拒绝（比如当前页本身是基座）时重建候选项，把复选框拨回真实状态——
        /// 让界面自己"弹回去"比静默留在一个假勾选上清楚。
        /// </summary>
        internal void OnBasePageSelectionChanged(BasePageSelection selection)
        {            if (_selectedPage is not { } page || Document.IsBasePage(page))
            {
                RebuildBasePageOptions();
                return;
            }

            if (selection.IsSelected)
            {
                if (!Document.TryAddBasePageReference(page, selection.Page, out _))
                {
                    RebuildBasePageOptions();
                    return;
                }
            }
            else if (!Document.TryRemoveBasePageReference(page, selection.Page, out _))
            {
                RebuildBasePageOptions();
                return;
            }

            _lastBasePageIds = page.BasePageIds.ToList();
            RaisePropertyChanged(nameof(SelectedBasePage));
            RaisePropertyChanged(nameof(SelectedBasePages));
            RaisePropertyChanged(nameof(BasePageSummary));
        }

        private void OnClearBasePages()
        {
            if (_selectedPage is not { } page)
                return;

            Document.TrySetBasePage(page, null, out _);
            RebuildBasePageOptions();
        }

        /// <summary>基座页改名时刷新顶栏摘要（候选项自己订阅了 Name，摘要读的是同一批对象）。</summary>
        internal void RaiseBasePageSummaryChanged()
        {
            RaisePropertyChanged(nameof(SelectedBasePage));
            RaisePropertyChanged(nameof(SelectedBasePages));
            RaisePropertyChanged(nameof(BasePageSummary));
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
        /// 把"指向了某一个图元"展开成"真正该选上的那一批"——它所在的整组 + 它装着的子图元。
        ///
        /// <b>规则本身在领域层</b>（<see cref="ScadaPage.ExpandSelectionCluster"/>），这里只留一句调用。
        ///
        /// 为什么不再各写一份：这段逻辑原先在画布（<c>ScadaCanvas.ExpandPointSelection</c>）
        /// 与这里各有一份实现，靠注释约定"两边必须逐字对齐"。容器图元把展开规则从一条
        /// （分组）变成两条（分组 + 父子），再各写一份就是四处要同步；
        /// 而两处不同步的表现是"同一次点击，画布选中的与菜单认的不是同一批"，极难查。
        /// 收到领域层一处，两侧都只消费同一份结果。
        ///
        /// <b>被点中的那个排首位</b>：<see cref="ApplySelection"/> 拿首项当主选中，
        /// 属性面板与单目标命令都跟着主选中走，排到后面会变成"我点的是 B，面板显示的是 A"。
        /// </summary>
        private IReadOnlyList<ScadaElement> ExpandPointSelection(ScadaElement element)
            => _selectedPage is { } page ? page.ExpandSelectionCluster(element) : new[] { element };

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
            DockIntoPanelCommand.RaiseCanExecuteChanged();
            UndockFromPanelCommand.RaiseCanExecuteChanged();

            // 复制 / 再制 / 存模板三条共用 CanCopySelection 这一个判据，所以一起刷。
            CopyCommand.RaiseCanExecuteChanged();
            DuplicateCommand.RaiseCanExecuteChanged();
            SaveTemplateCommand.RaiseCanExecuteChanged();

            // 格式工具栏（r5）五条。它们判的是"选中的图元够不够、能不能编辑"，
            // 与上面那批同源——漏掉任何一条，工具栏上那个按钮的灰亮就会留在上一个选中上。
            AlignCommand.RaiseCanExecuteChanged();
            MoveZCommand.RaiseCanExecuteChanged();
            MatchSizeCommand.RaiseCanExecuteChanged();
            SpaceCommand.RaiseCanExecuteChanged();
            SnapElementsToGridCommand.RaiseCanExecuteChanged();
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

        /// <summary>
        /// 新建一个<b>基座</b>画面（跨页公共层）：建页 + 立刻注册进 <see cref="ScadaDocument.BasePages"/> + 选中它。
        ///
        /// 为什么是"新建基座"而不是"把当前页注册为基座"：基座是一份**独立的公共层**，
        /// 它的内容（边框、报警条、公共指示灯）不该和某一页的业务图元混在一起。
        /// 先建一份空白的公共层、在里面画公共内容，比"把已经画好的一页提升为基座"更不容易误伤
        /// （后者会让那一页的业务内容变成所有页面共享的东西）。
        ///
        /// 新建完立刻选中它：基座图元<b>只有</b>在基座自己的编辑界面里能改（在引用页上只渲染、不可选中），
        /// 不切过去的话用户会看到"新建了但画布没变、也放不进东西"。
        /// </summary>
        public DelegateCommand AddBasePageCommand { get; }

        /// <summary>新建弹窗页（特殊画面）：建页后立即勾上 <see cref="ScadaPage.IsPopup"/> 并切过去。</summary>
        public DelegateCommand AddPopupPageCommand { get; }

        public DelegateCommand RemovePageCommand { get; }

        /// <summary>运行当前方案（弹出全屏运行窗口）</summary>
        public DelegateCommand RunPageCommand { get; }

        /// <summary>
        /// 打开方案级报警配置。报警是多张画面共用的方案内容，不是当前页属性，
        /// 所以入口放在组态工具栏、数据仍落 <see cref="ScadaDocument.Alarms"/>。
        /// </summary>
        public DelegateCommand OpenAlarmConfigCommand { get; }

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

        /// <summary>
        /// 把选中图元装进一只底板（右键菜单「放入底板」）。可用性见 <see cref="CanDockSelected"/>。
        ///
        /// 靶底板的选取见 <see cref="ResolveDockTarget"/>：主选中<b>是</b>底板就装它自己，
        /// 否则找本页第一只"还装得下"的底板。这条"就近猜一个"的口径是有意的——
        /// 让用户在菜单里再挑一次底板，等于每次放东西都多一步；
        /// 而放错了的代价很小（移出底板 / 撤销，都是一步）。
        /// </summary>
        public DelegateCommand DockIntoPanelCommand { get; }

        /// <summary>把选中图元从它们各自的底板里移出（右键菜单「移出底板」）。可用性见 <see cref="CanUndockSelected"/></summary>
        public DelegateCommand UndockFromPanelCommand { get; }

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
        /// 从「我的模板」批量生成实例（参数化模板的入口）。
        /// 与「存为模板」同族，但它是"一次生成一批"，所以放在编辑器工具栏而不是图元右键菜单——
        /// 右键菜单只在有选中项时出现，而批量插入不需要先选中任何东西。
        /// </summary>
        public DelegateCommand BatchInsertTemplateCommand { get; }

        /// <summary>
        /// 方向键微调选中图元；参数是 <c>"Left"</c>/<c>"Right"</c>/<c>"Up"</c>/<c>"Down"</c>。
        /// 用字符串而不是四个命令：四条 <c>KeyBinding</c> 只差一个方向，拆成四个属性
        /// 等于把同一件事抄四遍，而这里的参数压根不会被外部构造（只由 XAML 常量给）。
        /// </summary>
        public DelegateCommand<string> NudgeCommand { get; }

        /// <summary>
        /// 格式工具栏的"对齐与分布"（参数是 <see cref="ScadaAlign"/> 的八个取值）。
        /// 动作见 <see cref="AlignSelected"/>，判据见 <see cref="CanAlignSelected"/>——
        /// 与右键菜单「对齐与分布」子菜单读的是同一对方法，两处灰亮同源。
        /// </summary>
        public DelegateCommand<ScadaAlign?> AlignCommand { get; }

        /// <summary>
        /// 格式工具栏的"叠放次序"（参数是 <see cref="ScadaZMove"/> 的四个取值）。
        /// 动作见 <see cref="MoveSelectedZ"/>，判据见 <see cref="CanMoveSelectedZ"/>。
        /// </summary>
        public DelegateCommand<ScadaZMove?> MoveZCommand { get; }

        /// <summary>
        /// 格式工具栏的"等尺寸"（等宽 / 等高 / 等大小）。动作见 <see cref="MatchSelectedSize"/>，
        /// 判据见 <see cref="CanMatchSelectedSize"/>。
        /// </summary>
        public DelegateCommand<ScadaSizeMatch?> MatchSizeCommand { get; }

        /// <summary>
        /// 格式工具栏的"等间距"（水平 / 垂直）。间距值取 <see cref="SpacingGap"/> 这个属性
        /// 而不是命令参数：<c>DelegateCommand&lt;T&gt;</c> 只带得动一个参数，
        /// 而"方向"和"间距"是两件事，硬塞进一个元组只会让 XAML 那边没法用 <c>x:Static</c> 传常量。
        /// </summary>
        public DelegateCommand<ScadaSpacing?> SpaceCommand { get; }

        /// <summary>
        /// 格式工具栏的"对齐网格"：把选中图元的左上角吸到网格点上。
        /// 与"吸附"开关无关（那管的是拖动时吸不吸），它是一次显式命令。
        /// </summary>
        public DelegateCommand SnapElementsToGridCommand { get; }

        /// <summary>
        /// 等间距动作里"相邻图元之间留多少像素"（可正可负，0 = 边贴边）。
        ///
        /// 放在视图模型而不是 <see cref="ScadaPage"/> 上：它是一次操作的<b>参数</b>，
        /// 不是一个画面状态——存进 .vms 只会让"上次在这儿填了几"变成跨机器复现的隐式配置。
        /// 与 Zoom / Offset 同一条口径（取景与操作参数不落盘）。
        ///
        /// 默认给 20 而不是 0：0 的结果是图元紧贴，看起来像"没生效"；
        /// 给一个肉眼可见的缝，"等间距"这件事才一眼看得出来。
        /// </summary>
        public double SpacingGap
        {
            get => _spacingGap;
            set => SetProperty(ref _spacingGap, value);
        }

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

            // 撤销/重做可能把 BasePageIds 整个换掉（它是一次标量变更）：候选项、摘要与画布绑定
            // 都是按当前页算的，不重建就会"模型回去了、画面和勾选还停在旧值"。
            // 但普通图元编辑的撤销也会走到这里，先比快照，避免每次 Ctrl+Z 都全量重建画布。
            if (_selectedPage is { } page && !page.BasePageIds.SequenceEqual(_lastBasePageIds))
                RebuildBasePageOptions();
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

        private bool CanOpenAlarmConfig()
            => _dialogService != null && _workspace.CurrentSolution != null;

        private void OnOpenAlarmConfig()
        {
            // 报警是方案级配置（ScadaDocument.Alarms），不是当前页属性；
            // 弹窗自己从当前方案现读，这里不传文档副本。
            _dialogService?.ShowDialog("ScadaAlarmConfigDialogView");
        }

        /// <summary>
        /// 末页保护在模型里（<see cref="ScadaDocument.TryRemovePage"/>），这里只是提前把按钮置灰，
        /// 省掉一次"点了才知道不行"的点击。
        /// </summary>
        private bool CanRemovePage() => _selectedPage != null && Document.Pages.Count > 1;

        private void OnAddPage()
        {
            // 新画面默认沿用<b>当前页的基座</b>：现场建一批"长得一样"的画面是常态
            // （同一产线的好几个工位都该带同一份报警条与公共图元），
            // 没有基座则不带——新画面从空白开始。
            var baseIds = _selectedPage?.BasePageIds.ToList() ?? new List<Guid>();
            var page = Document.AddPage(basePageId: baseIds.FirstOrDefault());

            // 多基座：第一只由 AddPage 带进去，其余在建页之后补上（低频动作，多一条撤销记录可接受）。
            if (baseIds.Count > 1)
            {
                using (page.BeginEdit("沿用多只基座"))
                {
                    page.SetBasePageIds(baseIds);
                }
            }

            // 新画面立刻成为编辑目标：否则用户点完"新建"看到的还是旧画面，
            // 会以为没生效而连点好几次
            SelectedPage = page;
        }

        /// <summary>
        /// 新建基座：建页 → 注册 → 选中它（三步一次做完，见 <see cref="AddBasePageCommand"/> 的注释）。
        ///
        /// 注册失败要把画面留着、并把原因说出来：先建页后注册的两步顺序里，
        /// 注册失败（理论上只剩"这页自己引用了基座"，新建页不可能）也不该静默吞掉。
        /// </summary>
        private void OnAddBasePage()
        {
            var page = Document.AddPage(NextBasePageName());

            if (!Document.TryAddBasePage(page, out string error))
            {
                EasyDialog.ShowSync("无法新建基座", error);
                SelectedPage = page; // 页面已经建出来了，仍然切过去，免得用户找不到它
                return;
            }

            SelectedPage = page;
        }

        private void OnAddPopupPage()
        {
            ScadaPage page;

            // 建页 + 标记 + 改尺寸合成一次撤销（AddPage 内部的作用域会并入外层）。
            using (Document.BeginEdit("新建弹窗页"))
            {
                page = Document.AddPage(NextPopupPageName());
                page.IsPopup = true;
                page.Width = 480;
                page.Height = 320;
            }

            SelectedPage = page;
        }

        private string NextPopupPageName()
        {
            var used = new HashSet<string>(
                Document.Pages.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

            int index = 1;
            while (used.Contains($"弹窗_{index}"))
                index++;

            return $"弹窗_{index}";
        }

        /// <summary>取当前未被占用的最小「基座_N」（与 <c>ScadaDocument.NextPageName</c> 同一口径）</summary>
        private string NextBasePageName()
        {
            var used = new HashSet<string>(
                Document.Pages.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

            for (int i = 1; ; i++)
            {
                string name = $"基座_{i}";

                if (!used.Contains(name))
                    return name;
            }
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
            ScadaClipboardPayload? payload, double offsetX, double offsetY, string label,
            bool groupAll = false,
            Guid templateId = default,
            int templateIndex = -1,
            int templateRevision = 0)
        {
            if (_selectedPage is not { } page)
                return Array.Empty<ScadaElement>();

            if (payload is not { IsEmpty: false })
                return Array.Empty<ScadaElement>();

            var created = ScadaClipboard.Materialize(
                page, payload, offsetX, offsetY, label, groupAll,
                templateId, templateIndex, templateRevision);

            if (created.Count > 0)
                ApplySelection(created);

            return created;
        }

        /// <summary>
        /// 从工具箱拖入一个模板到画布指定位置（单实例）。
        ///
        /// 参数化模板（含 <c>{i}</c>）必须走与批量插入同一套展开/校验；否则拖进去的是
        /// 字面量 <c>Control[{i}]</c>，同项目内按 Id 静默绑回原变量、跨项目直接失效。
        /// 非参数化模板保持旧行为（原样物化），避免老模板因为"变量暂时找不到"而拖不进来。
        /// </summary>
        public bool InsertTemplateAt(Guid templateId, double originX, double originY)
        {
            if (_selectedPage is not { } page)
                return false;

            var payload = ScadaTemplateStore.Shared.GetPayload(templateId);

            if (payload is not { IsEmpty: false })
                return false;

            string name = ScadaTemplateStore.Shared.Templates
                .FirstOrDefault(t => t.TemplateId == templateId)?.Name ?? "模板";

            int templateRevision = ScadaTemplateStore.Shared.GetRevision(templateId);

            if (!ScadaTemplateTokens.ContainsIndexToken(payload))
            {
                ScadaClipboard.GetBounds(payload, out double left, out double top, out _, out _);

                return PlacePayload(
                    payload, originX - left, originY - top,
                    $"插入模板 [{name}]", groupAll: true,
                    templateId: templateId,
                    templateIndex: ScadaTemplateStore.Shared.GetParameterIndex(templateId),
                    templateRevision: templateRevision).Count > 0;
            }

            // 参数化模板拖入时问一次实例序号：用户想"把 0 换成 1"最自然的入口就是这里，
            // 不必为了一个实例去开批量插入对话框。
            int defaultIndex = ScadaTemplateStore.Shared.GetParameterIndex(templateId);
            var (indexConfirmed, indexText) = EasyDialog.ShowTextInputSync(
                "插入模板序号（{i} 的值）",
                defaultIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (!indexConfirmed)
                return false;

            if (!int.TryParse(indexText.Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int instanceIndex)
                || instanceIndex < 0)
            {
                EasyDialog.ShowSync("插入模板失败", "实例序号必须是非负整数");
                return false;
            }

            var resolver = new ScadaTemplateVariableResolver(_workspace.VariableRegistry);
            var options = new ScadaTemplateExpansionOptions
            {
                Count = 1,
                StartIndex = instanceIndex,
                Columns = 1,
                OriginX = originX,
                OriginY = originY,
            };

            if (!ScadaTemplateExpansion.TryPlan(payload, options, resolver,
                    out var plan, out var errors, out var warnings))
            {
                EasyDialog.ShowSync("插入模板失败", FormatBatchErrors(errors));
                return false;
            }

            if (warnings.Count > 0
                && !EasyDialog.ShowSync("模板提示",
                    string.Join(Environment.NewLine, warnings.Select(w => "• " + w))
                    + Environment.NewLine + Environment.NewLine + "仍要插入吗？"))
                return false;

            if (!ScadaTemplateBatchInserter.TryInsert(
                    page, plan, options, name, out var created, out string? error,
                    templateId, templateRevision))
            {
                EasyDialog.ShowSync("插入模板失败", error ?? "插入模板失败");
                return false;
            }

            if (created.Count > 0)
                ApplySelection(created);

            return created.Count > 0;
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

            if (targets.Count < 2)
            {
                EasyDialog.ShowSync("存为模板", "至少选中 2 个图元才能存为模板。");
                return false;
            }

            var payload = ScadaClipboard.Capture(page, targets);

            if (payload.IsEmpty)
                return false;

            var (confirmed, value) = EasyDialog.ShowTextInputSync("存为模板", _selectedElement?.Name ?? "模板");

            if (!confirmed)
                return false;

            // 真实绑定存的是具体下标/具体变量名，不会自己变成 {i}。
            // 这里让用户选：数组下标参数化、名称/界面文字查找替换，或都留空按原样存。
            if (!TryParameterizePayload(payload, out int parameterIndex))
                return false;

            if (!ScadaTemplateStore.Shared.TrySave(value, payload, parameterIndex, out _, out string? error))
            {
                EasyDialog.ShowSync("存为模板失败", error ?? "模板保存失败");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 存/更新模板前的参数化确认：数组下标 + 多行名称/文字替换。
        /// 返回 false 表示用户取消或参数化失败；成功时 <paramref name="parameterIndex"/> 是落库的默认起始序号。
        /// </summary>
        private bool TryParameterizePayload(ScadaClipboardPayload payload, out int parameterIndex)
        {
            parameterIndex = 0;

            int? detected = DetectTemplateIndex(payload);
            var parameterizeEditor = new ScadaTemplateParameterizeEditor(detected);

            if (!EasyDialog.ShowSync("参数化模板", parameterizeEditor))
                return false;

            if (!parameterizeEditor.TryApply(
                    payload, out int changed, out var changes,
                    out parameterIndex, out string? parameterError))
            {
                EasyDialog.ShowSync("参数化失败", parameterError ?? "参数化失败");
                return false;
            }

            // 参数化会改写模板快照：让用户看见"改了哪些"再落库，
            // 避免把 Temp_0 这类恰好以数字结尾的标量误当家族成员。
            if (changed > 0)
            {
                string preview = string.Join(Environment.NewLine, changes.Take(8).Select(c => "• " + c));
                string more = changed > 8 ? $"{Environment.NewLine}……另有 {changed - 8} 条" : string.Empty;

                if (!EasyDialog.ShowSync("参数化确认",
                        $"将改写 {changed} 处：{Environment.NewLine}{preview}{more}{Environment.NewLine}{Environment.NewLine}仍要继续吗？"))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 用当前选中覆盖已有模板（关联模板的"改模板"入口）。
        /// 成功后 <see cref="ScadaTemplateStore.TemplateUpdated"/> 会同步触发关联实例刷新。
        /// </summary>
        public bool TryUpdateTemplateFromSelection(Guid templateId, out string? error)
        {
            error = null;

            if (_selectedPage is not { } page)
            {
                error = "当前没有可编辑的画面";
                return false;
            }

            var targets = CollectPresent(page);

            if (targets.Count < 2)
            {
                error = "至少选中 2 个图元才能更新模板";
                return false;
            }

            var payload = ScadaClipboard.Capture(page, targets);

            if (payload.IsEmpty)
            {
                error = "没有可保存的图元";
                return false;
            }

            // 与"存为模板"共用同一套参数化编辑器：直接 capture 当前选中会把实例上的
            // 具体名字（Cylinder1_Manual）写回模板定义，下一次展开就全绑同一个变量。
            if (!TryParameterizePayload(payload, out int parameterIndex))
            {
                error = "已取消";
                return false;
            }

            // 当前模板是参数化的、这次捕获却没有令牌：很可能是直接选中了展开后的实例，
            // 又没在编辑器里重填规则。写回去会让以后所有实例都绑同一组变量，必须让用户明确确认。
            var currentPayload = ScadaTemplateStore.Shared.GetPayload(templateId);

            if (ScadaTemplateTokens.ContainsIndexToken(currentPayload)
                && !ScadaTemplateTokens.ContainsIndexToken(payload)
                && !EasyDialog.ShowSync("更新模板警告",
                    "当前选中的内容里没有 {i} 令牌，而这条模板原来是参数化的。\n"
                    + "继续更新会让以后插入的实例全部绑同一组变量。\n\n仍要更新吗？"))
            {
                error = "已取消";
                return false;
            }

            return ScadaTemplateStore.Shared.TryUpdate(
                templateId, payload, parameterIndex, out _, out error);
        }

        /// <summary>
        /// 把画面上的关联实例刷新到模板的当前定义。返回刷新/失败统计；
        /// 旧选中与新图元有交集时，自动恢复选中。
        /// </summary>
        public ScadaTemplateRefreshResult RefreshTemplateInstances(Guid templateId, int? revision = null)
        {
            var payload = ScadaTemplateStore.Shared.GetPayload(templateId);

            if (payload == null)
            {
                return new ScadaTemplateRefreshResult(
                    0, 0, new[] { "模板不存在，关联实例保持原样" },
                    Array.Empty<ScadaTemplateRefreshInstance>());
            }

            var selectedIds = SelectedElements
                .Where(e => e != null)
                .Select(e => e.ElementId)
                .ToHashSet();

            var resolver = new ScadaTemplateVariableResolver(_workspace.VariableRegistry);
            var result = ScadaTemplateInstanceRefresher.Refresh(
                Document, templateId, payload,
                revision ?? ScadaTemplateStore.Shared.GetRevision(templateId),
                resolver);

            if (result.Instances.Count > 0 && selectedIds.Count > 0)
            {
                var reselect = result.Instances
                    .Where(i => i.OldElementIds.Any(selectedIds.Contains))
                    .SelectMany(i => i.NewElements)
                    .ToList();

                if (reselect.Count > 0)
                    ApplySelection(reselect);
            }

            return result;
        }

        private void OnTemplateUpdated(object? sender, ScadaTemplateUpdatedEventArgs e)
        {
            var result = RefreshTemplateInstances(e.TemplateId, e.Revision);

            if (result.Failed > 0)
            {
                EasyDialog.ShowSync("关联模板刷新",
                    string.Join(Environment.NewLine, result.Warnings));
            }
        }

        /// <summary>
        /// 猜一个"要参数化的下标"给输入框做默认值：优先取显式 ArrayIndex，
        /// 其次取变量名末尾的 <c>[n]</c> 或 <c>_n</c>。猜不到返回 null（用户可手填）。
        /// </summary>
        private static int? DetectTemplateIndex(ScadaClipboardPayload payload)
        {
            foreach (var element in payload.Items)
            {
                if (element == null)
                    continue;

                foreach (var binding in element.Bindings)
                {
                    if (binding == null)
                        continue;

                    if (binding.ArrayIndex >= 0)
                        return binding.ArrayIndex;

                    if (TryParseTrailingIndex(binding.VariableName, out int parsed))
                        return parsed;
                }

                foreach (var animation in element.Animations)
                {
                    if (animation == null)
                        continue;

                    if (animation.ArrayIndex >= 0)
                        return animation.ArrayIndex;

                    if (TryParseTrailingIndex(animation.VariableName, out int parsed))
                        return parsed;
                }

                foreach (var hook in element.EventHooks)
                {
                    if (hook == null)
                        continue;

                    foreach (var action in hook.Actions)
                    {
                        if (action == null || action.Type != ScadaActionType.WriteVariable)
                            continue;

                        if (action.ArrayIndex >= 0)
                            return action.ArrayIndex;

                        if (TryParseTrailingIndex(action.VariableName, out int parsed))
                            return parsed;
                    }
                }
            }

            return null;
        }

        /// <summary>解析变量名末尾的 <c>[12]</c> 或 <c>_12</c>；没有则返回 false。</summary>
        private static bool TryParseTrailingIndex(string? variableName, out int index)
        {
            index = -1;

            if (string.IsNullOrWhiteSpace(variableName))
                return false;

            string name = variableName!;

            if (name.EndsWith("]", StringComparison.Ordinal))
            {
                int open = name.LastIndexOf('[');

                if (open >= 0 && name.Length - open >= 3
                    && int.TryParse(name.AsSpan(open + 1, name.Length - open - 2),
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out index))
                    return true;
            }

            // 下划线后缀（Temp_0）不再猜测：它分不清"家族成员"和"恰好以数字结尾的标量"。
            return false;
        }

        private void OnCopy() => CopySelection();

        private void OnPaste() => PasteClipboard();

        private void OnDuplicate() => DuplicateSelection();

        private void OnSaveTemplate() => SaveSelectionAsTemplate();

        /// <summary>
        /// 批量插入模板：弹参数框 → 展开计划（全量校验）→ 原子插入。
        ///
        /// 校验与插入分两段：校验阶段把"哪条变量解析不到"一次列全，插入阶段只做
        /// "按计划物化"；这样失败时画面与撤销栈都是零变化，不会出现半批实例。
        /// </summary>
        private void OnBatchInsertTemplate()
        {
            if (_selectedPage is not { } page)
                return;

            var resolver = new ScadaTemplateVariableResolver(_workspace.VariableRegistry);
            var editor = new ScadaBatchInsertTemplateEditor(resolver);

            // 校验失败时留在同一份编辑器上（用户改完参数直接重试），不重新构造丢输入。
            while (true)
            {
                if (!EasyDialog.ShowSync("批量插入模板", editor))
                    return;

                if (!editor.TryBuildPlan(out var plan, out var options, out var errors, out var warnings))
                {
                    EasyDialog.ShowSync("模板校验失败", FormatBatchErrors(errors));
                    continue;
                }

                if (plan == null || options == null)
                    return;

                // 警告必须让用户看见：半参数化模板（文字有 {i}、变量没有）会生成
                // 一批"看起来各不相同、实际绑同一个变量"的实例，这是最难查的静默错绑。
                if (warnings.Count > 0
                    && !EasyDialog.ShowSync("模板提示",
                        string.Join(Environment.NewLine, warnings.Select(w => "• " + w))
                        + Environment.NewLine + Environment.NewLine + "仍要插入吗？"))
                    return;

                if (!ScadaTemplateBatchInserter.TryInsert(
                        page, plan, options, editor.SelectedTemplateName,
                        out var created, out string? insertError))
                {
                    EasyDialog.ShowSync("批量插入失败", insertError ?? "批量插入失败");
                    return;
                }

                // 100 个实例 × 多个图元全选会让属性面板一次展开上千行；超过阈值只选第一个实例。
                if (created.Count <= 200)
                {
                    ApplySelection(created);
                }
                else
                {
                    int firstInstanceCount = plan.Instances[0].Items.Count(item => item != null);
                    ApplySelection(created.Take(firstInstanceCount).ToArray());
                }

                return;
            }
        }

        /// <summary>把展开错误压成一段可读文本（最多列 8 条，其余只报数量）。</summary>
        private static string FormatBatchErrors(IReadOnlyList<ScadaTemplateExpansionError> errors)
        {
            if (errors.Count == 0)
                return "模板展开失败";

            var lines = errors.Take(8).Select(e =>
                $"实例 {e.InstanceIndex} / 图元「{e.ElementName}」 / {e.Reference}：{e.Reason}");

            string text = string.Join(Environment.NewLine, lines);

            if (errors.Count > 8)
                text += Environment.NewLine + $"……另有 {errors.Count - 8} 条错误";

            return text;
        }

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
        /// 放入 / 移出底板（Font Awesome 6 Pro Solid 的 <c>object-group</c> 变体语义）。
        ///
        /// 图标取"方框套方块"与"方块从方框里出来"两个形态，
        /// 与组合/取消组合那一对刻意画得不一样：那对是"几块并排 / 散开"，
        /// 这对是"被框住 / 脱离框"。两种关系在语义上本就不同（底板会裁剪、分组不会），
        /// 图标混用会让用户以为它们是同一件事的两个说法。
        /// </summary>
        private const string DockIcon = "\uE4C6";

        private const string UndockIcon = "\uE4C7";

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

        /// <summary>
        /// "等尺寸"分组图标（<c>expand</c>：四个方向的箭头往外撑）。
        ///
        /// 挑它的理由与 <see cref="AlignIcon"/> 一致：分组图标不能与某个子项同形，
        /// 否则用户会以为"这一栏就是那个动作"。等宽用左右向箭头、等高用上下向箭头，
        /// 分组图标就用"四个方向一起撑"——表达"尺寸被拉齐"，不指某一根轴。
        /// </summary>
        private const string SizeMatchIcon = "\uF065";

        /// <summary>
        /// "等间距"分组图标（<c>arrows-left-right</c>：左右两个箭头之间空着一段）。
        ///
        /// 两个子项分别是"竖排握线条"（F7A5，表达沿水平轴排开）与"横排握线条"
        /// （F7A4，表达沿垂直轴排开），分组图标若取其中之一，另一项就显得像外人。
        /// 左右箭头中间那段空隙正是"间距"这个概念的图形本身。
        /// </summary>
        private const string SpacingIcon = "\uF550";

        /// <summary>
        /// "对齐网格"（<c>border-all</c>：一整块被纵横线切开的方框，即网格本身）。
        ///
        /// 用网格的图形而不是某个箭头：这一项吸的是<b>绝对位置</b>，
        /// 画成箭头会被读成"朝某个方向挪一下"，而它其实是"贴到最近的格点上"。
        /// </summary>
        private const string SnapToGridIcon = "\uF84C";

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
        /// 等尺寸在菜单/工具栏里的排列：等宽 → 等高 → 等大小。
        ///
        /// 顺序照"用户改尺寸的粒度"排：先单根轴（宽、高），再两根轴一起；
        /// 把"等大小"放最后是因为它的结果最强（宽高都被改），摆中间容易误点。
        /// 与 <see cref="AlignOrder"/> 一样是常量表而不是散在各处的 new DelegateCommand。
        /// </summary>
        private static readonly ScadaSizeMatch[] SizeMatchOrder =
        {
            ScadaSizeMatch.Width,
            ScadaSizeMatch.Height,
            ScadaSizeMatch.Both,
        };

        /// <summary>
        /// 等间距在菜单/工具栏里的排列：水平 → 垂直。
        ///
        /// 与 <see cref="AlignOrder"/> 里"水平三兄弟在前、垂直三兄弟在后"同一口径，
        /// 用户在这两个地方扫到的都是"先横后竖"。
        /// </summary>
        private static readonly ScadaSpacing[] SpacingOrder =
        {
            ScadaSpacing.Horizontal,
            ScadaSpacing.Vertical,
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

            // 等尺寸（等宽 / 等高 / 等大小）：三个口径共用一条判灰规则（可编辑选中数够不够），
            // 差异只在 ScadaSizeMatchExtensions.MinimumCount 那一个数字上——与对齐那一栏同一写法，
            // 这里同样不写 switch，判据整个交给 CanMatchSelectedSize，与领域层前置校验同源。
            var sizes = new List<ScadaMenuItem>();
            foreach (var sizeMatch in SizeMatchOrder)
            {
                var target = sizeMatch;
                sizes.Add(ScadaMenuItem.Action(
                    target.DisplayName(),
                    IconOf(target),
                    new DelegateCommand(() => MatchSelectedSize(target), () => CanMatchSelectedSize(target))));
            }

            // 等间距（水平 / 垂直）：与"对齐与分布"里的分布互为反面——
            // 分布是"两端钉住、间距算出来"，这里是"第一个钉住、间距由工具栏那个数值框给定"。
            // 正因为是两件事，才各占一栏而不是折进"对齐与分布"。
            var spacings = new List<ScadaMenuItem>();
            foreach (var spacing in SpacingOrder)
            {
                var target = spacing;
                spacings.Add(ScadaMenuItem.Action(
                    target.DisplayName(),
                    IconOf(target),
                    new DelegateCommand(() => SpaceSelected(target), () => CanSpaceSelected(target))));
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
                ScadaMenuItem.Submenu("等尺寸", SizeMatchIcon, sizes),
                ScadaMenuItem.Submenu("等间距", SpacingIcon, spacings),

                // 对齐网格单独一项、不折进"等间距"：它改的是位置（吸到最近的格点），
                // 与等间距"按给定间距重排"是两件事——并进去用户会以为它也要读那个间距值。
                // 它复用工具栏那条属性型命令（没有参数，不需要闭包），
                // 与下面"复制/粘贴"复用 CopyCommand/PasteCommand 同一写法。
                ScadaMenuItem.Action("对齐网格", SnapToGridIcon, SnapElementsToGridCommand),

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

                // 放入/移出底板。与分组相邻（都属"改变图元之间的关系"），但语义不同：
                // 分组是编辑期捆绑（不裁剪、不嵌套），底板是视觉嵌套（裁剪、跟着走）。
                // 两项都常驻（各按自己的可用性判灰），不做二态合并——
                // 它们不是同一个动作的两个方向：一个图元可以既在组里、又装在底板里。
                ScadaMenuItem.Action("放入底板", DockIcon, DockIntoPanelCommand),
                ScadaMenuItem.Action("移出底板", UndockIcon, UndockFromPanelCommand),

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
        /// 选出"这一次要把图元装进哪只底板"。
        ///
        /// 两条候选口径，按优先级：
        /// ① <b>主选中本身就是底板</b>——用户右键点在一只底板上选"放入底板"，
        ///    意思显然是"把这一批装进我点中的这只"；
        /// ② 否则找本页<b>第一只不含父的底板</b>（<see cref="IsDockContainer"/>）——
        ///    用户选了一片散图元、想让它们成个块，此时点"放入底板"期望的是"找个框装起来"。
        ///
        /// 为什么不弹一个底板选择框：装错了的代价极小（移出底板 / 撤销各一步），
        /// 而弹框会让每一次归置都多一步确认——那是组态时最常做的动作之一。
        /// 真要在多只底板之间选，把图元直接拖到目标底板上更直观（拖动本身就表达了目标）。
        /// </summary>
        private ScadaElement? ResolveDockTarget(ScadaPage page)
        {
            if (IsDockContainer(_selectedElement))
                return _selectedElement;

            foreach (var element in page.Elements)
            {
                if (IsDockContainer(element))
                    return element;
            }

            return null;
        }

        /// <summary>
        /// 本图元能不能当"放入底板"的靶：它是<b>已注册的容器类型</b>且自己没有父（一层限制）。
        ///
        /// 判据走注册表（<c>ElementRegistry</c>）而不是写死 <c>TypeKey == "Hmi.Panel"</c>：
        /// 将来再加一类容器（比如"选项卡"）时，这里一行都不用改。
        /// 用 <c>ControlType</c> 判接口而不是按 TypeKey 比字符串——
        /// 前者是编译期事实，后者是"改个 TypeKey 就静默失效"的字符串约定。
        /// </summary>
        private static bool IsDockContainer(ScadaElement? element)
        {
            if (element is null || element.ParentId != Guid.Empty)
                return false;

            var descriptor = ElementRegistry.Find(element.TypeKey);

            return descriptor is not null
                   && typeof(IElementContainer).IsAssignableFrom(descriptor.ControlType);
        }

        /// <summary>「放入底板」能不能点：有选中、本页面有可用的靶底板、且选中项里至少有一个真会动</summary>
        public bool CanDockSelected()
        {
            if (_selectedPage is not { } page || _selectedElement is null)
                return false;

            if (ResolveDockTarget(page) is not { } target)
                return false;

            // 选中项全是"已在该底板里"的成员时是空操作，项该判灰（与"取消组合"那一项同一条口径）
            foreach (var element in CollectPresent(page))
            {
                if (!ReferenceEquals(element, target) && element.ParentId != target.ElementId)
                    return true;
            }

            return false;
        }

        /// <summary>把选中图元装进 <see cref="ResolveDockTarget"/> 选出的底板（右键菜单「放入底板」）</summary>
        public bool DockSelectedIntoPanel()
        {
            if (_selectedPage is not { } page || ResolveDockTarget(page) is not { } target)
                return false;

            // 底板自己也传进去：领域层会把它剔掉（不能装进自己），
            // 而调用方保留它能让"只选中底板 + 想看它装了多少东西"这类场景走同一条路。
            return page.TrySetParent(target, CollectPresent(page), out _);
        }

        /// <summary>
        /// 「移出底板」能不能点：确实在本画面上、且<b>真的装着父</b>的选中图元至少 1 个。
        ///
        /// 数的是"装着父的"而不是"选中的"：全都已经在顶层时点下去是空操作
        /// （<see cref="ScadaPage.TryClearParent"/> 返回 true 但不产记录），
        /// 菜单亮着却什么都没发生，用户只会记成"这一项坏了"——与"取消组合"同一条理由。
        /// </summary>
        public bool CanUndockSelected()
        {
            if (_selectedPage is not { } page)
                return false;

            foreach (var element in CollectPresent(page))
            {
                if (element.ParentId != Guid.Empty)
                    return true;
            }

            return false;
        }

        /// <summary>把选中图元从各自的底板里移出（右键菜单「移出底板」）</summary>
        public bool UndockSelected()
            => _selectedPage is { } page && page.TryClearParent(CollectPresent(page), out _);

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
        public bool MoveSelectedZ(ScadaZMove? move)
            => move is { } value
               && _selectedPage is { } page
               && _selectedElement is { } element
               && page.TryMoveElementZ(element, value, out _);

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
        public bool AlignSelected(ScadaAlign? align)
            => align is { } value
               && _selectedPage is { } page
               && page.TryAlignElements(_selectedElements, value, out _);

        /// <summary>
        /// 这一项排列动作现在能不能点：可编辑的选中图元数够不够
        /// （对齐 2 个、分布 3 个，下界取自 <see cref="ScadaAlignExtensions.MinimumCount"/>）。
        ///
        /// 数的是<b>可编辑</b>的而不是"选中的"：多选里混进一个锁住的底图时，
        /// 真正会动的只有其余几个，拿选中总数判就会出现"菜单亮着、点了只挪了一半"。
        /// </summary>
        public bool CanAlignSelected(ScadaAlign? align)
            => align is { } value
               && _selectedPage is { } page
               && CollectEditable(page).Count >= value.MinimumCount();

        /// <summary>
        /// 把选中的整批图元统一成同一个尺寸（等宽 / 等高 / 等大小）。
        ///
        /// 写入口只有 <see cref="ScadaPage.TryMatchElementSize"/> 一条，理由与
        /// <see cref="AlignSelected"/> 逐字相同：基准怎么取、锁定怎么处理、一条撤销记录怎么罩，
        /// 全在领域层那一处；视图模型自己再算一遍"最大宽是多少"，
        /// 就会出现"画布上看着一样大、撤销一下又不一样"这类算式劈叉。
        ///
        /// 传 <c>_selectedElements</c> 而不是 <see cref="CollectEditable"/> 的结果，同样与对齐同源：
        /// 领域层内部本来就要再筛一遍，更要紧的是<b>基准必须由同一批图元算出来</b>。
        /// </summary>
        public bool MatchSelectedSize(ScadaSizeMatch? match)
            => match is { } value
               && _selectedPage is { } page
               && page.TryMatchElementSize(_selectedElements, value, out _);

        /// <summary>
        /// 把选中的整批图元按"相邻留 <see cref="SpacingGap"/> 像素"重排。
        ///
        /// 写入口只有 <see cref="ScadaPage.TrySpaceElements"/> 一条：锚点取谁、
        /// 按哪根轴排序、间距怎么累加全在领域层那一处——与 <see cref="AlignSelected"/> 同一条理由。
        /// </summary>
        public bool SpaceSelected(ScadaSpacing? spacing)
            => spacing is { } value
               && _selectedPage is { } page
               && page.TrySpaceElements(_selectedElements, value, SpacingGap, out _);

        /// <summary>
        /// 把选中图元的左上角吸到网格点上。
        ///
        /// 写入口只有 <see cref="ScadaPage.TrySnapElementsToGrid"/> 一条：吸附的算式
        /// （吸"绝对位置"而不是"相对位移"）与画布拖动、工具箱落点共用同一份口径，
        /// 这里再写一遍 <c>Math.Round</c> 就会多出第三份"吸到哪儿"的答案。
        /// </summary>
        public bool SnapSelectedToGrid()
            => _selectedPage is { } page
               && page.TrySnapElementsToGrid(_selectedElements, out _);

        /// <summary>
        /// "等尺寸"现在能不能点：可编辑的选中图元数够不够
        /// （下界取自 <see cref="ScadaSizeMatchExtensions.MinimumCount"/>，与领域层前置校验同源）。
        ///
        /// 数的是<b>可编辑</b>的而不是"选中的"，理由与 <see cref="CanAlignSelected"/> 逐字相同：
        /// 混进一个锁住的底图时，真正会变的只有其余几个。
        /// 三种口径的下界一样，所以参数不参与计算——它只是 <c>DelegateCommand&lt;T&gt;</c> 要求的签名。
        /// </summary>
        public bool CanMatchSelectedSize(ScadaSizeMatch? match)
            => _selectedPage is { } page
               && CollectEditable(page).Count >= ScadaSizeMatchExtensions.MinimumCount;

        /// <summary>
        /// "等间距"现在能不能点：可编辑的选中图元数够不够
        /// （下界取自 <see cref="ScadaSpacingExtensions.MinimumCount"/>，与领域层前置校验同源）。
        /// 两个方向的下界一样，理由同上。
        /// </summary>
        public bool CanSpaceSelected(ScadaSpacing? spacing)
            => _selectedPage is { } page
               && CollectEditable(page).Count >= ScadaSpacingExtensions.MinimumCount;

        /// <summary>
        /// "对齐网格"现在能不能点：得有可编辑的选中图元。
        ///
        /// 刻意<b>不</b>在这里判"网格间距是否小于 1"：那一条只在脏数据下才成立
        /// （属性面板给的 Min 是 1），多判一次等于给一条走不到的分支写代码——
        /// 真正的门槛在领域层 <see cref="ScadaPage.TrySnapElementsToGrid"/> 里。
        /// </summary>
        public bool CanSnapSelectedToGrid()
            => _selectedPage is { } page
               && CollectEditable(page).Count > 0;

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
        public bool CanMoveSelectedZ(ScadaZMove? move)
            => move is { } value
               && _selectedPage is { } page
               && _selectedElement is { } element
               && IndexInZOrder(page, element) is { } index
               && (value is ScadaZMove.ToFront or ScadaZMove.Forward
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

        /// <summary>
        /// 三个等尺寸口径各配一个图标（Font Awesome 6 Pro Solid）。
        ///
        /// 选型口径与对齐那一栏同源：三兄弟必须是同一套画法，用户才认得出它们是一组。
        /// 等宽用"左右箭头指到两条竖线"（arrows-left-right-to-line，E4BA）——
        /// 那两条竖线就是"宽"的两条边；等高用同一套画法转 90°（arrows-up-down，F07D）；
        /// 等大小用"对角箭头往外撑"（up-right-and-down-left-from-center，F424），
        /// 表达两根轴一起变，与分组图标 F065 同族但不重形。
        ///
        /// 三个码点同样落在 <c>ScadaChecks</c> 那条逐字符 <c>CharacterToGlyphMap.ContainsKey</c>
        /// 的断言里，改动这里必须同时过那条断言（字形缺一个就是空心方框，深色菜单上极难发现）。
        /// </summary>
        private static string IconOf(ScadaSizeMatch match) => match switch
        {
            ScadaSizeMatch.Width => "\uE4BA",
            ScadaSizeMatch.Height => "\uF07D",
            ScadaSizeMatch.Both => "\uF424",
            _ => SizeMatchIcon,
        };

        /// <summary>
        /// 两个等间距方向各配一个图标（Font Awesome 6 Pro Solid）。
        ///
        /// 水平等间距沿 X 轴排开，用"竖排的握线条"（grip-lines-vertical，F7A5）——
        /// 竖线并排正是"沿水平方向被隔开"的样子；垂直等间距用同一套画法转 90°
        /// （grip-lines，F7A4）。两个码点同样在那条字体断言里。
        /// </summary>
        private static string IconOf(ScadaSpacing spacing) => spacing switch
        {
            ScadaSpacing.Horizontal => "\uF7A5",
            ScadaSpacing.Vertical => "\uF7A4",
            _ => SpacingIcon,
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
            RaisePropertyChanged(nameof(HasSolution));
            RaisePropertyChanged(nameof(HasPage));
            RaisePropertyChanged(nameof(Pages));
            RebuildBasePageOptions();
            AddPageCommand.RaiseCanExecuteChanged();
            AddBasePageCommand.RaiseCanExecuteChanged();
            AddPopupPageCommand.RaiseCanExecuteChanged();
            RunPageCommand.RaiseCanExecuteChanged();

            var pages = Pages;
            SelectedPage = pages.Count > 0 ? pages[0] : null;
        }
    }

    /// <summary>
    /// 顶栏基座下拉的一项：<see cref="Page"/> 为 null 表示「（无基座）」。
    ///
    /// 为什么要这个包装（而不是直接把 <see cref="ScadaPage"/> 当候选项、另塞一个 ComboBoxItem 占位）：
    /// 占位项的类型与选中项的类型不一致时，WPF 会把 ComboBoxItem 往 <c>ScadaPage?</c> 里塞，
    /// 绑定引擎转换失败 → 源属性收不到通知 → "选了清除、模型没变"，而且不报错。
    /// 包装之后候选项与选中项是同一个类型，这条静默失败路径不存在。
    ///
    /// 名字走<b>实时读取</b>（订阅基座画面的 <c>Name</c> 变更并转发通知）：
    /// 基座改名之后下拉框要立刻跟着变，不能等到切页面才刷新。
    /// </summary>
    public sealed class BasePageOption : BindableBase
    {
        /// <summary>「（无基座）」那一项（全局唯一，引用相等即可判等）</summary>
        public static readonly BasePageOption None = new(null);

        public BasePageOption(ScadaPage? page)
            : this(page, subscribe: true)
        {
        }

        private BasePageOption(ScadaPage? page, bool subscribe)
        {
            Page = page;

            if (subscribe && page != null)
                page.PropertyChanged += OnPagePropertyChanged;
        }

        /// <summary>
        /// 不订阅改名的候选（只用于"候选项还没重建好"的兜底）。
        /// 订阅型候选由 <see cref="ScadaEditorViewModel.RebuildBasePageOptions"/> 统一创建与摘除；
        /// 在属性 getter 里随手 new 一个订阅型，会让每次绑定求值都多挂一个订阅。
        /// </summary>
        public static BasePageOption Detached(ScadaPage page)
            => new(page, subscribe: false);

        /// <summary>底层基座画面；null = 不引用</summary>
        public ScadaPage? Page { get; }

        public string Name => Page?.Name ?? "（无基座）";

        /// <summary>摘掉对底层画面的订阅（候选项被重建时调用，避免旧项被画面钉住）</summary>
        public void Detach()
        {
            if (Page != null)
                Page.PropertyChanged -= OnPagePropertyChanged;
        }

        private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ScadaPage.Name))
                RaisePropertyChanged(nameof(Name));
        }
    }

    /// <summary>
    /// 顶栏「基座」多选菜单的一行：一只已注册基座 + 本页是否引用它。
    ///
    /// 勾选动作直接回写文档（<see cref="ScadaEditorViewModel.OnBasePageSelectionChanged"/>），
    /// 行本身不持有"本页引用了哪些基座"的第二份状态——真值只有 <see cref="ScadaPage.BasePageIds"/> 一处。
    /// </summary>
    public sealed class BasePageSelection : BindableBase
    {
        private readonly ScadaEditorViewModel _owner;
        private bool _isSelected;

        internal BasePageSelection(ScadaEditorViewModel owner, ScadaPage page, bool isSelected)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Page = page ?? throw new ArgumentNullException(nameof(page));
            _isSelected = isSelected;
            Page.PropertyChanged += OnPagePropertyChanged;
        }

        public ScadaPage Page { get; }

        public string Name => Page.Name;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                    _owner.OnBasePageSelectionChanged(this);
            }
        }

        /// <summary>摘掉对基座页的改名订阅（菜单重建时调用）。</summary>
        public void Detach() => Page.PropertyChanged -= OnPagePropertyChanged;

        private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ScadaPage.Name))
            {
                RaisePropertyChanged(nameof(Name));
                _owner.RaiseBasePageSummaryChanged();
            }
        }
    }
}
