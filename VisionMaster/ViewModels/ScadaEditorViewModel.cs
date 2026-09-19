using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
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

        public ScadaEditorViewModel(IWorkspaceManager workspace, IScadaRuntimeHost runtime)
        {
            _workspace = workspace;
            _runtime = runtime;
            _workspaceChanged = workspace as INotifyPropertyChanged;

            AddPageCommand = new DelegateCommand(OnAddPage, () => Document != _fallback);
            RemovePageCommand = new DelegateCommand(OnRemovePage, CanRemovePage);
            RunPageCommand = new DelegateCommand(OnRunPage, CanRunPage);

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

            OnDocumentChanged();
        }

        /// <summary>摘除工作区通知（幂等）；摘干净后本 VM 不再被工作区单例钉住</summary>
        public void Deactivate()
        {
            if (!_subscribed) return;
            _subscribed = false;

            if (_workspaceChanged != null)
                _workspaceChanged.PropertyChanged -= OnWorkspacePropertyChanged;
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
            }
        }

        /// <summary>当前选中图元（画布双向同步；属性面板与工具箱都以它为写入目标）</summary>
        public ScadaElement? SelectedElement
        {
            get => _selectedElement;
            set => SetProperty(ref _selectedElement, value);
        }

        public DelegateCommand AddPageCommand { get; }

        public DelegateCommand RemovePageCommand { get; }

        /// <summary>运行当前方案（弹出全屏运行窗口）</summary>
        public DelegateCommand RunPageCommand { get; }

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

            // 确认框是这道操作唯一的闸门：工程里没有撤销，画面连着它上面所有图元一起没了。
            // 报个数而不是只问"确定吗"——空画面一删就过，铺了几十个图元的画面得让人先看一眼损失。
            string message = page.Elements.Count > 0
                ? $"画面 [{page.Name}] 上还有 {page.Elements.Count} 个图元，删除后无法恢复。\n确定要删除该画面吗？"
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

            element.Name = UniqueName(page, descriptor.DisplayName);

            // 新图元默认归到画面的首个图层。不这么做的话它就是个"未分层"图元：
            // 图层面板里谁都装不下它，用户关掉唯一的图层却发现画面上的东西一个没少，
            // 只会判定成"开关坏了"。未分层（LayerId=Empty）这个取值留给脏数据与手工取消分层，
            // 不该是新内容的默认状态（旧文件的平铺图元由 EnsureIdentity 归到同一层，口径一致）。
            element.LayerId = page.DefaultLayer?.LayerId ?? Guid.Empty;

            // 新图元排到最前：画面里往往铺着一张 ZIndex=0 的设备底图，不抬序的话
            // 刚拖出来的矩形会直接生在它下面，用户看到的是"拖了没反应"。
            element.ZIndex = page.Elements.Count > 0 ? page.Elements.Max(e => e.ZIndex) + 1 : 1;

            page.Elements.Add(element);

            // 放完就选中：属性面板（S3-d）紧接着要编辑的就是它，
            // 而且选中框是"东西确实放下了"最直接的反馈。
            SelectedElement = element;

            return element;
        }

        /// <summary>
        /// 图元落名：重名就续 _2、_3。
        ///
        /// 唯一性只在<b>同一画面内</b>要求：跨画面重名太常见且无害（不同页是不同上下文），
        /// 要全局唯一的话"复制一屏设备到另一页"会变成一场改名工程。
        /// </summary>
        private static string UniqueName(ScadaPage page, string baseName)
        {
            if (!page.Elements.Any(e => string.Equals(e.Name, baseName, StringComparison.Ordinal)))
                return baseName;

            // 候选数上界取"已有图元数 + 2"：已有名字至多占掉 Elements.Count 个坑，
            // 所以这个区间里必定还有一个空位，循环不会跑飞。
            for (int n = 2; n <= page.Elements.Count + 2; n++)
            {
                string candidate = $"{baseName}_{n}";
                if (!page.Elements.Any(e => string.Equals(e.Name, candidate, StringComparison.Ordinal)))
                    return candidate;
            }

            return $"{baseName}_{page.Elements.Count + 1}";
        }

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
            RaisePropertyChanged(nameof(Document));
            RaisePropertyChanged(nameof(Pages));
            AddPageCommand.RaiseCanExecuteChanged();
            RunPageCommand.RaiseCanExecuteChanged();

            var pages = Pages;
            SelectedPage = pages.Count > 0 ? pages[0] : null;
        }
    }
}
