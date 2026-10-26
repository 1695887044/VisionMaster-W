using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using GongDragDrop = GongSolutions.Wpf.DragDrop.DragDrop;
using GongDropInfo = GongSolutions.Wpf.DragDrop.IDropInfo;
using GongDropTarget = GongSolutions.Wpf.DragDrop.IDropTarget;
using Nodify;
using Prism.Dialogs;
using Prism.Ioc;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;

namespace VisionMaster.Views
{
    /// <summary>
    /// 流程节点画布视图（海康式一图全展开）。
    ///
    /// 只承担三件"视图模型够不着"的事：
    ///   1. 视口动作（FitToScreen / ZoomIn / BringIntoView 是 NodifyEditor 的成员，视图模型拿不到）；
    ///   2. 建线时打开变量绑定弹窗（端口对的选择是 UI 流程，画布 VM 只发请求事件）；
    ///   3. 把上面两件事接到 FlowCanvasViewModel 的命令与事件上。
    /// 节点内容、连线合法性、折叠持久化一律留在视图模型，这里不做任何业务判断。
    /// </summary>
    public partial class FlowCanvasView : UserControl
    {
        private FlowCanvasViewModel? _viewModel;

        /// <summary>拖动对齐参考线浮层（入树挂、离树摘，与 Activate/Deactivate 同一对）</summary>
        private DragGuidesAdorner? _guidesAdorner;

        public FlowCanvasView()
        {
            InitializeComponent();

            // 工具箱拖入：编辑器是 gong 的拖放目标，落点分派交给下面的 CanvasDropTarget。
            // **不能改回裸 DragOver/Drop**——gong 的拖影 Popup 只在自己 DropTargetOnDrop 里关闭，
            // 裸 Drop 返回 Copy 时拖影会永久停在落点（2026-10-21 真机：胶囊行"Continue"不消失，
            // 且后续拖拽的胶囊内容也不刷新）。
            GongDragDrop.SetDropHandler(Editor, new CanvasDropTarget(this));

            // 自绘层也是工具箱的放置端（同一份 CanvasDropTarget）。
            // R171：拖影 Popup 只由 gong 自己的放置端关闭，裸 Drop 会把它永久留在落点。
            GongDragDrop.SetDropHandler(SurfacePreview, new CanvasDropTarget(this));
            GongDragDrop.SetIsDropTarget(SurfacePreview, true);

            // 入树/离树成对挂摘订阅：AvalonDock 切换标签页、隐藏面板都会触发 Unloaded，
            // 所以清理必须可逆（离树摘干净让旧 VM 可 GC，入树重新挂上），不能用一次性 Dispose。
            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;

            // 自绘画布**默认开启**（2026-10-25 上线）：日常走自绘渲染，Nodify 编辑器收起。
            // 取消勾选即回到可编辑画布（拖动改序 / 框选 / 工具箱拖入 / 容器折叠）。
            // 放在构造函数里而不是 XAML 的 IsChecked="True"：勾选要触发 OnSurfacePreviewChanged，
            // 那时 Editor 已经构造好，可以立刻收起它并摘掉数据源。
            ChkSurfacePreview.IsChecked = true;

            // 粘贴落点来源：指针在编辑器内移动即回报图坐标（Ctrl+V 直按也取"最后一次悬停位置"）
            Editor.PreviewMouseMove += OnEditorPointerMoved;
        }

        private void OnViewLoaded(object sender, RoutedEventArgs e)
        {
            // 参考线浮层只挂一次（幂等）；Editor 的 AdornerLayer 要等入树才拿得到
            if (_guidesAdorner == null
                && AdornerLayer.GetAdornerLayer(Editor) is { } layer)
            {
                _guidesAdorner = new DragGuidesAdorner(this);
                layer.Add(_guidesAdorner);
            }

            _viewModel?.Activate();
        }

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
        {
            if (_guidesAdorner != null)
            {
                (AdornerLayer.GetAdornerLayer(Editor) as AdornerLayer)?.Remove(_guidesAdorner);
                _guidesAdorner = null;
            }

            _viewModel?.Deactivate();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null)
            {
                // 自绘预览层订的是旧 VM 的集合，换 VM 前必须先摘，否则新 VM 的变更会打到旧快照上
                UnsubscribeSurface();
                _viewModel.ViewportActionRequested -= OnViewportActionRequested;
                _viewModel.ZoomCommandRequested -= OnZoomCommandRequested;
                _viewModel.ModuleLinkRequested -= OnModuleLinkRequested;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                // 旧 VM 即将失去视图：摘掉订阅，避免它继续响应流程变化（旧 VM 随之可被 GC）
                _viewModel.Deactivate();
            }

            _viewModel = e.NewValue as FlowCanvasViewModel;

            if (_viewModel == null) return;

            _viewModel.ViewportActionRequested += OnViewportActionRequested;
            _viewModel.ZoomCommandRequested += OnZoomCommandRequested;
            _viewModel.ModuleLinkRequested += OnModuleLinkRequested;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Prism 的 AutoWireViewModel 可能晚于 Loaded 才把 VM 装进 DataContext，
            // 此时入树事件已过去，需补挂一次（Activate 幂等，不会重复订阅）
            if (IsLoaded)
                _viewModel.Activate();

            // 视图模型在构造里就渲染过一次，那时还没有订阅者，首次的"适应画布"请求等于丢了。
            // 补一次，否则首次打开面板停在默认视口，节点可能整片在视野外。
            OnViewportActionRequested(null);

            // 自绘画布默认开启：VM 到位后补订阅 + 首帧（构造期 _viewModel 还是 null，
            // 那一次 SyncSurface 直接返回了，不补就是"画布空白"）。
            if (ChkSurfacePreview?.IsChecked == true)
            {
                SubscribeSurface();
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(() => SyncSurface(zoomToFit: true)));
            }
        }

        // ==================================================================
        //  视口动作
        // ==================================================================

        /// <summary>
        /// 视图模型的视口请求：null = 适应整图（切流程后），非 null = 把该节点滚入视野（外部选中联动）。
        /// 推迟到 Background 优先级执行——ItemsSource 刚 Clear/Add 完时 ItemContainer 还没生成，
        /// 此刻 FitToScreen 会按空内容计算，表现为"切完流程画布缩在左上角"。
        /// </summary>
        private void OnViewportActionRequested(CanvasNodeViewModel? node)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    // 启动期视图模型先发视口请求、Nodify 编辑器尚未装配：没有画布可操作，直接忽略。
                    if (Editor == null) return;

                    if (node == null)
                    {
                        Editor.FitToScreen(null);

                        // 适应后若缩得过远（旧方案散乱布局会把外接框撑得很大），
                        // 钳到可读缩放并以内容为中心，避免"打开就是一堆蚂蚁"
                        if (Editor.ViewportZoom < 0.6 && Editor.ItemsExtent is { IsEmpty: false } extent)
                        {
                            Editor.ViewportZoom = 0.6;
                            Editor.BringIntoView(extent);
                        }

                        return;
                    }

                    var container = FindContainerFor(node);
                    if (container != null)
                        Editor.BringIntoView(container.Bounds);
                    else
                        Editor.BringIntoView(node.Location, true, null);
                }),
                DispatcherPriority.Background);
        }

        /// <summary>缩放工具条：放大 / 缩小 / 100% / 适应画布</summary>
        private void OnZoomCommandRequested(FlowCanvasZoomCommand command)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    switch (command)
                    {
                        case FlowCanvasZoomCommand.ZoomIn:
                            Editor.ZoomIn();
                            break;

                        case FlowCanvasZoomCommand.ZoomOut:
                            Editor.ZoomOut();
                            break;

                        case FlowCanvasZoomCommand.ActualSize:
                            Editor.ViewportZoom = 1.0;
                            break;

                        case FlowCanvasZoomCommand.Fit:
                            Editor.FitToScreen(null);
                            break;
                    }
                }),
                DispatcherPriority.Background);
        }

        // ==================================================================
        //  建线 → 变量绑定弹窗
        // ==================================================================

        /// <summary>
        /// 模块级建线请求：画布只确定"生产方 → 消费方"结构合法，绑哪对端口由绑定弹窗决定。
        /// 弹窗以 TargetStep 为锚构建上游候选树（含刚拖线的生产方），写回 LinkedSources 后
        /// 回调 RefreshLinks 重画聚合连线。
        /// </summary>
        private void OnModuleLinkRequested(StepModel consumer)
        {
            var dialogService = ContainerLocator.Container.Resolve<IDialogService>();

            var parameters = new DialogParameters
            {
                { "TargetStep", consumer },
            };

            dialogService.ShowDialog("DataBindView", parameters, result =>
            {
                // 取消也要重画一次：弹窗的 UnbindCommand 可能已经删过绑定
                _viewModel?.RefreshLinks();
            });
        }

        // ==================================================================
        //  连线右键菜单
        // ==================================================================

        /// <summary>
        /// 顺序链（执行顺序虚线）是纯视图投影，没有绑定语义——右键菜单整体拦掉
        ///（菜单在连线模板里是每实例一份，DataContext 即连线 VM）。
        /// </summary>
        private void OnConnectionContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: CanvasLinkViewModel link }
                && link.IsOrderLink)
            {
                e.Handled = true;
            }
        }

        // ==================================================================
        //  双击节点 → 模块参数 / 工具箱拖入画布（主编辑面补齐）
        // ==================================================================

        /// <summary>
        /// 双击节点打开模块参数（与流程栏右键、画布节点右键「模块参数…」同一入口，
        /// 分派一律在 StepParameterDialog）。豁免两类交互元素：容器头带里的折叠钮（单击是折叠/展开）
        /// 与连接点（拖线抓手）/断点圆点——双击落在它们身上什么都不做。
        /// 双击连线（DataContext 是连线 VM）或空白：命不到节点，同样什么都不做。
        /// </summary>
        private void OnEditorMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            var node = FindNodeFromSource(e.OriginalSource);
            if (node == null) return;

            _viewModel?.OpenModuleParameters(node);
        }

        /// <summary>
        /// 指针在编辑器内移动：把图坐标回报给画布 VM（粘贴落点用；高频写、无绑定消费）。
        /// 换算与 <see cref="ToGraphPosition"/>（工具箱拖入的落点）同一条口径。
        /// </summary>
        private void OnEditorPointerMoved(object sender, MouseEventArgs e)
        {
            if (_viewModel == null) return;

            var position = e.GetPosition(Editor);
            _viewModel.LastPointerGraphPosition = ToGraphPosition(position);

            // 画布内拖动节点：浮层跟着指针走（偏移与工具箱的 DragAdornerTranslation 一致）
            if (_viewModel.IsDragGhostVisible)
                MoveNodeDragGhost(position);
        }

        /// <summary>拖动浮层相对指针的偏移（与工具箱 dd:DragDrop.DragAdornerTranslation="16,12" 一致）</summary>
        private const double DragGhostOffsetX = 16;
        private const double DragGhostOffsetY = 12;

        /// <summary>把画布内拖动浮层挪到指针右下方（纯视觉，不参与命中测试）</summary>
        private void MoveNodeDragGhost(Point positionInEditor)
        {
            NodeDragGhost.HorizontalOffset = positionInEditor.X + DragGhostOffsetX;
            NodeDragGhost.VerticalOffset = positionInEditor.Y + DragGhostOffsetY;
        }

        /// <summary>
        /// 编辑器内屏幕坐标 → 图坐标（与节点 Location / 容器框矩形同一坐标系）：
        /// 图坐标 = 视口原点 + 屏幕坐标 / 缩放。Nodify 7.3 的
        /// GetLocationInsideEditor(DragEventArgs) 也是这条式子，这里自己算一份是因为
        /// gong 的 IDropInfo 只给 <see cref="GongDropInfo.DropPosition"/>（相对编辑器）。
        /// </summary>
        private Point ToGraphPosition(Point positionInEditor)
        {
            // 自绘模式：编辑器收起后 ViewportZoom/Location 不再更新，必须改读自绘层的 Zoom/Pan
            if (SurfacePreview.Visibility == Visibility.Visible)
            {
                double surfaceZoom = SurfacePreview.Zoom;
                if (surfaceZoom <= 0) surfaceZoom = 1.0;

                return new Point(
                    (positionInEditor.X - SurfacePreview.Pan.X) / surfaceZoom,
                    (positionInEditor.Y - SurfacePreview.Pan.Y) / surfaceZoom);
            }

            double zoom = Editor.ViewportZoom;
            if (zoom <= 0) zoom = 1.0;

            return new Point(
                Editor.ViewportLocation.X + positionInEditor.X / zoom,
                Editor.ViewportLocation.Y + positionInEditor.Y / zoom);
        }

        /// <summary>
        /// 工具箱拖入的落点分派（gong IDropTarget）。
        ///
        /// 为什么是 gong 的拖放目标而不是裸 Drop 事件：拖影（胶囊行）是 gong 的
        /// DragDropPreview Popup，只有它自己的 DropTargetOnDrop 会把它关掉；
        /// 裸 Drop 返回 Copy 时预览被跳过、Popup 永久停在落点（2026-10-21 真机）。
        /// 顺带修好第二个症状：DragOver 也走 gong 之后，拖影内容才会跟着当前拖的条目刷新
        ///（此前永远停在第一次拖的那条）。
        ///
        /// 只放行 ToolItemModel 负载（Scada 图元/文本/文件等一律显「禁止」）。
        /// </summary>
        private sealed class CanvasDropTarget : GongDropTarget
        {
            private readonly FlowCanvasView _owner;

            public CanvasDropTarget(FlowCanvasView owner) => _owner = owner;

            public void DragOver(GongDropInfo dropInfo)
            {
                var tool = FindDraggedTool(dropInfo);
                if (tool == null || _owner._viewModel == null)
                {
                    dropInfo.Effects = DragDropEffects.None;
                    DragHintPresenter.Hide(DragHintPresenter.CanvasOwner);
                    return;
                }

                // 落点提示（设计基线 drag-drop-hint v2）：与 DropToolAt 同一份判据，
                // 提示"会落到哪个容器的哪条分支"，非法落点说清原因
                var (text, kind) = _owner._viewModel.DescribeDropTarget(tool, _owner.ToGraphPosition(dropInfo.DropPosition));
                DragHintPresenter.Show(DragHintPresenter.CanvasOwner, text, kind);

                // 光标语义必须与提示一致：非法/运行锁时不许显示"可复制"，
                // 否则会出现"光标说能放、提示说不能放、松手被拒"的三方矛盾
                dropInfo.Effects = kind is DragHintKind.Illegal or DragHintKind.Locked
                    ? DragDropEffects.None
                    : DragDropEffects.Copy;
            }

            /// <summary>
            /// 拖出画布：只撤自己写的那条提示。
            /// 不能无条件 Hide——gong 的 DragLeave 是 Dispatcher 延迟派发的，从画布移到流程栏时
            /// 它可能排在流程栏的 DragOver 之后，无条件 Hide 会把流程栏刚写的提示抹掉。
            /// </summary>
            public void DragLeave(GongDropInfo dropInfo) => DragHintPresenter.Hide(DragHintPresenter.CanvasOwner);

            public void Drop(GongDropInfo dropInfo)
            {
                DragHintPresenter.Hide();

                var tool = FindDraggedTool(dropInfo);
                if (tool == null || _owner._viewModel == null)
                {
                    dropInfo.Effects = DragDropEffects.None;
                    return;
                }

                // 落点：DropPosition 是相对编辑器（IsDropTarget 所在元素）的屏幕坐标
                dropInfo.Effects = _owner._viewModel.DropToolAt(tool, _owner.ToGraphPosition(dropInfo.DropPosition))
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
            }

            /// <summary>
            /// 从 gong 的拖放负载取工具箱模板。gong 已按拖拽源的 DataFormat 解出数据本体，
            /// 这里仍保留"多选时是列表"的兜底——取不到就当作不是我们的负载（显禁止）。
            /// </summary>
            private static ToolItemModel? FindDraggedTool(GongDropInfo dropInfo)
            {
                if (dropInfo.Data is ToolItemModel direct) return direct;

                if (dropInfo.Data is IEnumerable items and not string)
                    return items.OfType<ToolItemModel>().FirstOrDefault();

                return null;
            }
        }

        /// <summary>
        /// 双击命中的节点：沿视觉树找第一个 DataContext 是节点 VM 的元素；
        /// 路径上出现交互部件（折叠钮 / 断点圆点 / Nodify 连接点）则整体豁免。
        /// 判据是元素身份（模板内 x:Name 与类型），不靠 ToolTip 文案——文案会随主题改，
        /// 名字改坏了最差是回到「双击开窗」，不会静默吃掉箭头的点击（R25 家族的教训）。
        /// </summary>
        private static CanvasNodeViewModel? FindNodeFromSource(object? source)
        {
            CanvasNodeViewModel? node = null;

            for (var current = source as DependencyObject; current != null; current = GetParent(current))
            {
                if (IsInteractivePart(current)) return null;

                if (node == null && current is FrameworkElement { DataContext: CanvasNodeViewModel hit })
                    node = hit;
            }

            return node;
        }

        /// <summary>折叠钮 / 断点圆点（模板内 x:Name）与 Nodify 连接点（NodeInput/NodeOutput）：双击一律豁免</summary>
        private static bool IsInteractivePart(DependencyObject element)
        {
            if (element is FrameworkElement { Name: "ContainerCollapseToggle" or "NodeBreakpointDot" })
                return true;

            return element is Connector;
        }

        private static DependencyObject? GetParent(DependencyObject child)
            => child is Visual or Visual3D
                ? VisualTreeHelper.GetParent(child)
                : LogicalTreeHelper.GetParent(child);

        // ==================================================================
        //  拖动对齐参考线浮层
        // ==================================================================

        /// <summary>
        /// VM 的参考线值变了 → 重画浮层。只认三个参考线属性，
        /// 其余高频属性通知（Location / IsSelected 等）不进这里。
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(FlowCanvasViewModel.GuideX)
                or nameof(FlowCanvasViewModel.GuideY)
                or nameof(FlowCanvasViewModel.GuideVisibility))
            {
                _guidesAdorner?.InvalidateVisual();
            }

            // 小地图开关：自绘模式下由自绘层自己画，跟着同一个开关走
            if (e.PropertyName == nameof(FlowCanvasViewModel.ShowMinimap)
                && SurfacePreview.Visibility == Visibility.Visible)
            {
                SurfacePreview.ShowMiniMap = _viewModel?.ShowMinimap ?? false;
            }

            // 浮层刚打开时鼠标未必移动过：用当前指针位置补一次，避免从 (0,0) 弹出来
            if (e.PropertyName == nameof(FlowCanvasViewModel.IsDragGhostVisible)
                && _viewModel?.IsDragGhostVisible == true)
            {
                MoveNodeDragGhost(Mouse.GetPosition(Editor));
            }
        }

        /// <summary>
        /// 拖动对齐参考线的渲染浮层（评审清单 B 包，纯视觉）。
        /// VM 给的是图坐标（GuideX/GuideY，null = 该线不画），这里按视口换算成屏幕坐标画
        /// 1px 虚线——换算公式与 <see cref="OnEditorPointerMoved"/> 同一条（已对过
        /// GetLocationInsideEditor 的口径）：屏幕 = (图坐标 - 视口原点) × 缩放。
        /// 视口平移/缩放（ViewportUpdated）与自身尺寸变化都会触发重画；
        /// 颜色用既有运行令牌整刷 40% 透明（不引新色值）。
        /// </summary>
        private sealed class DragGuidesAdorner : Adorner
        {
            private readonly FlowCanvasView _owner;

            public DragGuidesAdorner(FlowCanvasView owner)
                : base(owner.Editor)
            {
                _owner = owner;
                IsHitTestVisible = false;

                // 拖动中视口可能被平移/缩放：视口一变就重画（事件随 adorner 生命周期，摘除即断）
                ((NodifyEditor)AdornedElement).ViewportUpdated += (_, _) => InvalidateVisual();
                SizeChanged += (_, _) => InvalidateVisual();
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                var vm = _owner._viewModel;
                if (vm == null || vm.GuideVisibility != Visibility.Visible) return;

                var editor = (NodifyEditor)AdornedElement;
                double zoom = editor.ViewportZoom;
                if (zoom <= 0) zoom = 1.0;

                // 既有令牌整刷透明度：Clone 后改 Opacity（同一色值，不算引新色）
                var baseBrush = _owner.FindResource("FlowCanvasRunningBrush") as Brush;
                var brush = baseBrush?.Clone();
                if (brush == null) return;
                brush.Opacity = 0.4;
                brush.Freeze();

                var pen = new Pen(brush, 1);
                pen.DashStyle = new DashStyle(new double[] { 5, 4 }, 0);
                pen.Freeze();

                if (vm.GuideX is { } guideX)
                {
                    double x = (guideX - editor.ViewportLocation.X) * zoom;
                    drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
                }

                if (vm.GuideY is { } guideY)
                {
                    double y = (guideY - editor.ViewportLocation.Y) * zoom;
                    drawingContext.DrawLine(pen, new Point(0, y), new Point(ActualWidth, y));
                }
            }
        }

        // ==================================================================
        //  自绘预览层（C2 测量入口）
        //
        //  目的只有一个：**在真机上量出"同一流程用自绘渲染"的打开耗时**，
        //  与 Nodify 画布对比后决定是否替换。所以本层是只读的：
        //  平移 / 缩放 / 点选（点选仍走 VM 既有路径），不参与拖动改序与拖线。
        //  默认关闭；勾选工具条「自绘预览」才生效，编辑器整体隐藏。
        // ==================================================================

        private bool _surfaceSubscribed;
        private bool _surfaceSyncPending;
        private bool _surfaceUserAdjusted;
        private bool _minimapDetached;

        /// <summary>
        /// 探针/自动化入口：等价于勾选工具条「自绘预览」。
        /// 只在测量脚本里用（`--switch-perf … surface`），产品界面走那个 CheckBox。
        /// </summary>
        public bool UseSelfDrawnCanvas
        {
            get => ChkSurfacePreview.IsChecked == true;
            set => ChkSurfacePreview.IsChecked = value;
        }

        /// <summary>诊断用：确认自绘层真的接管了（编辑器已收起、自绘层已显示）</summary>
        public bool IsSelfDrawnActive
            => Editor.Visibility == Visibility.Collapsed && SurfacePreview.Visibility == Visibility.Visible;

        private void OnSurfacePreviewChanged(object sender, RoutedEventArgs e)
        {
            bool on = ChkSurfacePreview.IsChecked == true;

            Editor.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            SurfacePreview.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

            // 背景跟着一起显隐：它承载底色 + 点阵，拖动时不能跟着位移
            if (on) SurfaceBackdrop.Background ??= FlowCanvasSurface.CanvasBackdropBrush;
            SurfaceBackdrop.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

            if (on)
            {
                _surfaceUserAdjusted = false;   // 每次打开都重新适配一次

                // 字体族取编辑器的（Nodify 模板继承的就是它）：中文回退字形与字重必须一致，
                // 否则同一张卡片在两边看着"粗细不同"。
                SurfacePreview.SetUiFont(Editor.FontFamily);

                // **继承编辑器当前视口**：开关前后画面完全不动，才能并排对照"只换渲染、不换布局"。
                // Nodify 的 ViewportLocation 是视口左上角的图坐标，ViewportZoom 是缩放；
                // 拿不到（布局未完成时为 0）就退回下面的自动适配。
                double inheritedZoom = Editor.ViewportZoom;
                bool inherited = inheritedZoom > 0.0001;
                if (inherited)
                {
                    var inheritedLocation = Editor.ViewportLocation;
                    SurfacePreview.Zoom = inheritedZoom;
                    SurfacePreview.Pan = new Vector(-inheritedLocation.X * inheritedZoom, -inheritedLocation.Y * inheritedZoom);
                    _surfaceUserAdjusted = true;
                    FlowCanvasViewModel.PerfLog?.Invoke(
                        $"[自绘] 继承视口 zoom={inheritedZoom:F4} loc=({inheritedLocation.X:F0},{inheritedLocation.Y:F0})");
                }
                // **只 Collapsed 不够**：容器在首次布局时已生成，折叠只是跳过 measure，
                // 它们对 VM 的属性绑定仍在响应——实测 451 节点时这部分占 ~1000ms，
                // 会把"自绘到底快多少"完全淹没。所以这里把编辑器的数据源真正摘掉，
                // 等价于"产品里根本没有这个编辑器"。
                Editor.ItemsSource = null;
                Editor.Connections = null;

                SubscribeSurface();
                // 首次显示要等布局拿到实际尺寸，否则 ActualWidth 还是 0。
                // 继承成功时**不许再适配**（zoomToFit=true 会把继承来的视口覆盖掉）。
                DetachMinimapBindings();

                // 自绘模式下小地图由**自绘层自己画**（Nodify 的 Minimap 在折叠的编辑器上不生成 item），
                // 所以这里只把原来那个宿主 Border 收起来，开关本身照常可用。
                MiniMapHost.Visibility = Visibility.Collapsed;
                SurfacePreview.ShowMiniMap = _viewModel?.ShowMinimap ?? false;
                ChkMinimap.ToolTip = "显示/隐藏右下角小地图（自绘模式下由画布自己绘制）";

                // 自绘层不做自由摆放：撤销/重做改了顺序之后必须按顺序重排坐标，
                // 否则"顺序回去了、位置还停在原处"（图上看着还是旧顺序）。
                if (_viewModel != null) _viewModel.AutoReflowOnStructureChange = true;

                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => SyncSurface(zoomToFit: !inherited)));
            }
            else
            {
                UnsubscribeSurface();
                AttachMinimapBindings();
                ChkMinimap.IsEnabled = true;
                ChkMinimap.ToolTip = "显示/隐藏右下角小地图（默认关闭，避免遮挡输出节点）";
                // 恢复 Nodify 的宿主 Border（清掉本地值，让 Style 里的 ShowMinimap 触发器重新接管）
                MiniMapHost.ClearValue(VisibilityProperty);
                SurfacePreview.ShowMiniMap = false;
                // Nodify 是自由摆放，坐标由用户拖出来——绝不能跟着顺序重排
                if (_viewModel != null) _viewModel.AutoReflowOnStructureChange = false;
                // 恢复：直接给集合引用（原先的 XAML 绑定被上面覆盖了；诊断开关不追求可反复切换的完美）
                Editor.ItemsSource = _viewModel?.Nodes;
                Editor.Connections = _viewModel?.Links;
            }
        }

        private void SubscribeSurface()
        {
            if (_surfaceSubscribed || _viewModel == null) return;
            _viewModel.Nodes.CollectionChanged += OnSurfaceSourceChanged;
            _viewModel.Links.CollectionChanged += OnSurfaceSourceChanged;
            _viewModel.DataLinkScopeChanged += OnSurfaceScopeChanged;
            _surfaceSubscribed = true;
        }

        private void UnsubscribeSurface()
        {
            if (!_surfaceSubscribed || _viewModel == null) return;
            _viewModel.Nodes.CollectionChanged -= OnSurfaceSourceChanged;
            _viewModel.Links.CollectionChanged -= OnSurfaceSourceChanged;
            _viewModel.DataLinkScopeChanged -= OnSurfaceScopeChanged;
            _surfaceSubscribed = false;
        }

        /// <summary>选中变了 → 数据线作用域变了 → 自绘层要重拍快照（合并到一次，和集合变化同一防抖）。</summary>
        private void OnSurfaceScopeChanged() => OnSurfaceSourceChanged(null, null!);

        /// <summary>
        /// 集合变化 → 合并成一次重拍快照。**必须防抖**：一次重建会先 Clear 再逐个 Add，
        /// 直接同步重拍等于每次切换拍 450 次快照（每次 25ms），比不优化还慢。
        /// </summary>
        private void OnSurfaceSourceChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (SurfacePreview.Visibility != Visibility.Visible) return;
            if (_surfaceSyncPending) return;

            _surfaceSyncPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _surfaceSyncPending = false;
                if (SurfacePreview.Visibility == Visibility.Visible) SyncSurface(zoomToFit: false);
            }));
        }

        /// <summary>重拍几何快照；还没成功适配过视口时顺带适配一次（等价于 FitToScreen）</summary>
        private void SyncSurface(bool zoomToFit)
        {
            if (_viewModel == null) return;

            // 自绘层不做自由摆放：撤销/重做改了顺序之后必须按顺序重排坐标。
            // 放在这里而不是只在开关分支里——启动时开关先于 DataContext 置位，
            // 那条路径上的 _viewModel 还是 null，只在开关分支里设会漏掉默认开启的场景。
            _viewModel.AutoReflowOnStructureChange = true;

            SurfacePreview.SetModel(_viewModel);

            // 适配只在"用户没自己动过视口"时做：动过之后重建不能把视口抢回去；
            // 没动过则每次切流程重新适配（不同流程的内容范围差很远）。
            // 尺寸没量出来（ActualWidth=0）时**不能算**——0 宽会让 zoom 掉到下限，
            // 整张图缩成一个点，真机表现为"勾了自绘预览后画布一片空白"。
            if (_surfaceUserAdjusted && !zoomToFit) return;

            double w = SurfacePreview.ActualWidth, h = SurfacePreview.ActualHeight;
            if (w < 2 || h < 2)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (SurfacePreview.Visibility == Visibility.Visible) SyncSurface(zoomToFit: false);
                }));
                return;
            }

            var size = new Size(w, h);
            // 下限 0.25：长链流程（实测内容高 29406、视口 349）按比例适配会算出 zoom≈0.01，
            // 451 个节点被压成 0.6px 的像素点，肉眼就是"一片空白"。低于下限时改为左上角对齐。
            const double FitZoomFloor = 0.25;
            var fit = SurfacePreview.ComputeFitViewport(size, FitZoomFloor, 1.0);
            if (fit.IsEmpty)
            {
                FlowCanvasViewModel.PerfLog?.Invoke($"[自绘] Sync 适配失败 视口={w:F0}x{h:F0} 内容={SurfacePreview.ContentBounds} 节点={SurfacePreview.Nodes.Count}");
                return;
            }

            double zoom = size.Width / fit.Width;
            SurfacePreview.Zoom = zoom;
            if (zoom <= FitZoomFloor + 1e-6)
            {
                var content = SurfacePreview.ContentBounds;
                const double margin = 24;
                SurfacePreview.Pan = new Vector((margin - content.X) * zoom, (margin - content.Y) * zoom);
            }
            else
            {
                SurfacePreview.Pan = new Vector(-fit.X * zoom, -fit.Y * zoom);
            }

            FlowCanvasViewModel.PerfLog?.Invoke($"[自绘] Sync 视口={w:F0}x{h:F0} 内容={SurfacePreview.ContentBounds} 节点={SurfacePreview.Nodes.Count} zoom={zoom:F4} pan=({SurfacePreview.Pan.X:F0},{SurfacePreview.Pan.Y:F0})");
            UpdateMinimapViewport();
        }

        /// <summary>
        /// 自绘模式下把视口喂给小地图。
        /// 小地图的 `ItemsSource` 本来就绑 `Nodes`（不依赖编辑器），坏的是
        /// `ViewportLocation`/`ViewportSize`——它们是 **TwoWay 绑到 NodifyEditor** 的，
        /// 编辑器一收起就恒为 0，`SetCurrentValue` 写进去立刻被绑定的 0 覆盖回来，
        /// 小地图据此算出的缩放是无穷，结果一片空白（2026-10-25 实拍）。
        /// 所以必须**先解绑再赋值**，切回编辑器时把绑定原样装回去。
        /// </summary>
        private void UpdateMinimapViewport()
        {
            if (SurfacePreview.Visibility != Visibility.Visible) return;

            double zoom = SurfacePreview.Zoom;
            if (!(zoom > 0.0001) || SurfacePreview.ActualWidth < 1) return;

            double vx = -SurfacePreview.Pan.X / zoom;
            double vy = -SurfacePreview.Pan.Y / zoom;
            double vw = SurfacePreview.ActualWidth / zoom;
            double vh = SurfacePreview.ActualHeight / zoom;

            // 视口可能比内容还宽（单列流程横向放大后就是这样），原样喂进去小地图会把矩形画得比整张图还大、
            // 把小地图铺成一块纯色。夹到内容范围内：矩形才代表"我在这张图的哪个位置"。
            var content = SurfacePreview.ContentBounds;
            if (!content.IsEmpty && content.Width > 1 && content.Height > 1)
            {
                vw = Math.Min(vw, content.Width);
                vh = Math.Min(vh, content.Height);
                vx = Math.Clamp(vx, content.X, Math.Max(content.X, content.Right - vw));
                vy = Math.Clamp(vy, content.Y, Math.Max(content.Y, content.Bottom - vh));
            }

            MiniMap.SetValue(Nodify.Minimap.ViewportLocationProperty, new Point(vx, vy));
            MiniMap.SetValue(Nodify.Minimap.ViewportSizeProperty, new Size(vw, vh));
        }

        /// <summary>断开小地图与编辑器的视口绑定（自绘接管时调用，幂等）。</summary>
        private void DetachMinimapBindings()
        {
            if (_minimapDetached) return;
            System.Windows.Data.BindingOperations.ClearBinding(MiniMap, Nodify.Minimap.ViewportLocationProperty);
            System.Windows.Data.BindingOperations.ClearBinding(MiniMap, Nodify.Minimap.ViewportSizeProperty);
            _minimapDetached = true;
        }

        /// <summary>把视口绑定装回编辑器（切回 Nodify 时调用）。</summary>
        private void AttachMinimapBindings()
        {
            if (!_minimapDetached) return;

            MiniMap.SetBinding(Nodify.Minimap.ViewportLocationProperty,
                new System.Windows.Data.Binding(nameof(NodifyEditor.ViewportLocation))
                {
                    ElementName = "Editor",
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                });
            MiniMap.SetBinding(Nodify.Minimap.ViewportSizeProperty,
                new System.Windows.Data.Binding(nameof(NodifyEditor.ViewportSize))
                {
                    ElementName = "Editor",
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                });

            _minimapDetached = false;
        }

        private void OnSurfaceMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var p = e.GetPosition(SurfacePreview);
            double wx = (p.X - SurfacePreview.Pan.X) / SurfacePreview.Zoom;
            double wy = (p.Y - SurfacePreview.Pan.Y) / SurfacePreview.Zoom;

            double zoom = SurfacePreview.Zoom * Math.Exp(e.Delta * 0.0015);
            SurfacePreview.Zoom = zoom;
            SurfacePreview.Pan = new Vector(p.X - wx * zoom, p.Y - wy * zoom);
            _surfaceUserAdjusted = true;   // 用户自己定过视口，之后切流程不再抢回
            UpdateMinimapViewport();
            e.Handled = true;
        }

        // ==================================================================
        //  自绘层交互：拖动改序 / 框选多选 / 中键平移 / 容器折叠 / 双击 / 右键
        // ==================================================================

        private enum SurfaceGesture { None, Pan, Marquee, NodeDrag, MiniMap }

        private SurfaceGesture _gesture;
        private Point _gestureAnchor;                    // UserControl 坐标（不受 RenderTransform 影响）
        private Vector _panOrigin;
        private Point _marqueeStart;                     // SurfacePreview 坐标
        private bool _marqueeAdditive;
        private Vector _dragOffset;                      // 拖动中的渲染偏移（图坐标）
        private readonly List<(CanvasNodeViewModel Node, Point Origin)> _dragOrigins = new();

        private Point ToSurfaceGraph(Point p) => new(
            (p.X - SurfacePreview.Pan.X) / SurfacePreview.Zoom,
            (p.Y - SurfacePreview.Pan.Y) / SurfacePreview.Zoom);

        private void OnSurfaceMouseDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(SurfacePreview);

            // 小地图优先：点它就跳转视口，按住拖动可以**连续**扫视
            if (SurfacePreview.TryMiniMapJump(p))
            {
                _gesture = SurfaceGesture.MiniMap;
                _surfaceUserAdjusted = true;
                SurfacePreview.CaptureMouse();
                UpdateMinimapViewport();
                e.Handled = true;
                return;
            }

            var graph = ToSurfaceGraph(p);
            var hit = SurfacePreview.HitTest(graph);

            if (hit is { Model: CanvasNodeViewModel node })
            {
                // 折叠钮优先：容器组头右上角 18×18（与绘制同源，别改这里不改绘制）
                if (node.Kind == CanvasNodeKind.Container
                    && FlowCanvasSurface.CollapseButtonRect(hit.Value.Bounds).Contains(graph))
                {
                    _viewModel?.ToggleCollapseCommand.Execute(node);
                    e.Handled = true;
                    return;
                }

                // 走 VM 既有的选中链路（属性栏跟随），不自建第二套选中。
                // **必须先清掉其它节点的选中**：只置 true 不清旧的，点几下就变成多选了。
                // Shift 按住才追加。
                bool additive = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                if (!additive && _viewModel != null)
                {
                    foreach (var other in _viewModel.Nodes)
                    {
                        if (!ReferenceEquals(other, node) && other.IsSelected) other.IsSelected = false;
                    }
                }

                node.IsSelected = true;

                // 双击 = 模块参数（与 Nodify 画布、流程栏右键同一入口，分派在 StepParameterDialog）
                if (e.ClickCount == 2)
                {
                    _viewModel?.OpenModuleParameters(node);
                    return;
                }

                // 拖动改序：**只做渲染偏移**，不碰模型坐标（见 BeginNodeDrag 注释）
                BeginNodeDrag(e.GetPosition(this));
                return;
            }

            // 空白处左键拖 = 框选（与 Nodify 一致）；平移改中键
            _gesture = SurfaceGesture.Marquee;
            _marqueeAdditive = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            _marqueeStart = p;
            _gestureAnchor = e.GetPosition(this);
            SurfacePreview.CaptureMouse();
        }

        /// <summary>中键平移（与 Nodify 一致：空白处左键让给框选）。</summary>
        private void OnSurfaceMouseDownAny(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle) return;

            _gesture = SurfaceGesture.Pan;
            _gestureAnchor = e.GetPosition(this);
            _panOrigin = SurfacePreview.Pan;
            _surfaceUserAdjusted = true;
            SurfacePreview.CaptureMouse();
            e.Handled = true;
        }

        /// <summary>
        /// 抬起选中集开始拖动。**拖动期不改模型坐标**（画布位置是布局器算的，写回会让图散架）；
        /// 只记录起点，移动时给自绘层喂"渲染偏移"，落定时再交给 VM 提交顺序并重排。
        /// 不走 `ItemsDragStartedCommand`：那条链路会拉起拖动浮层与对齐参考线，
        /// 它们挂在已收起的 Nodify 编辑器上，位置全是错的。
        /// </summary>
        private void BeginNodeDrag(Point anchorInHost)
        {
            _dragOrigins.Clear();

            if (_viewModel != null)
            {
                foreach (var n in _viewModel.Nodes)
                {
                    if (n.IsSelected && n.IsDraggable) _dragOrigins.Add((n, n.Location));
                }
            }

            if (_dragOrigins.Count == 0) return;

            _gesture = SurfaceGesture.NodeDrag;
            _gestureAnchor = anchorInHost;
            _surfaceUserAdjusted = true;
            SurfacePreview.CaptureMouse();
        }

        /// <summary>
        /// 自绘层右键 = 与 Nodify 画布同一份节点菜单（A1：StepNodeContextMenu / ContainerNodeContextMenu
        /// 是 UserControl.Resources 里的**单实例**，这里只换 DataContext 与放置点，不另建菜单）。
        /// 命中不到节点时不开菜单（与 Nodify 一致：空白处右键无动作）。
        /// </summary>
        private void OnSurfaceMouseRightDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(SurfacePreview);
            var graph = ToSurfaceGraph(p);

            if (SurfacePreview.HitTest(graph)?.Model is not CanvasNodeViewModel node) return;

            node.IsSelected = true;

            string key = node.Kind == CanvasNodeKind.Container ? "ContainerNodeContextMenu" : "StepNodeContextMenu";
            if (TryFindResource(key) is not ContextMenu menu) return;

            menu.DataContext = node;
            menu.PlacementTarget = SurfacePreview;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void OnSurfaceMouseMove(object sender, MouseEventArgs e)
        {
            if (_gesture == SurfaceGesture.None) return;

            var host = e.GetPosition(this);
            double zoom = SurfacePreview.Zoom <= 0 ? 1 : SurfacePreview.Zoom;

            switch (_gesture)
            {
                case SurfaceGesture.Pan:
                    // 拖动必须**真的重绘**：只做位图位移的话，新拖进视口的那部分没有节点
                    SurfacePreview.Pan = new Vector(
                        _panOrigin.X + (host.X - _gestureAnchor.X),
                        _panOrigin.Y + (host.Y - _gestureAnchor.Y));
                    break;

                case SurfaceGesture.NodeDrag:
                    {
                        double dx = (host.X - _gestureAnchor.X) / zoom;
                        double dy = (host.Y - _gestureAnchor.Y) / zoom;

                        // 只喂渲染偏移：模型坐标不动，落定后由布局器重排
                        _dragOffset = new Vector(dx, dy);
                        SurfacePreview.SetDragOffsets(
                            _dragOrigins.Select(o => (o.Node, _dragOffset)));
                        break;
                    }

                case SurfaceGesture.MiniMap:
                    // 按住小地图连续拖动：每一帧都把视口中心对到指针所指的图坐标
                    SurfacePreview.TryMiniMapJump(e.GetPosition(SurfacePreview));
                    UpdateMinimapViewport();
                    break;

                case SurfaceGesture.Marquee:
                    {
                        var p = e.GetPosition(SurfacePreview);
                        SurfacePreview.MarqueeScreenRect = new Rect(
                            Math.Min(_marqueeStart.X, p.X),
                            Math.Min(_marqueeStart.Y, p.Y),
                            Math.Abs(p.X - _marqueeStart.X),
                            Math.Abs(p.Y - _marqueeStart.Y));
                        break;
                    }
            }
        }

        private void OnSurfaceMouseUp(object sender, MouseButtonEventArgs e)
        {
            var gesture = _gesture;
            if (gesture == SurfaceGesture.None) return;

            _gesture = SurfaceGesture.None;
            SurfacePreview.ReleaseMouseCapture();

            switch (gesture)
            {
                case SurfaceGesture.NodeDrag:
                    {
                        SurfacePreview.ClearDragOffsets();

                        var dragged = _dragOrigins.Select(o => o.Node).ToList();
                        if (dragged.Count > 0 && _viewModel != null)
                        {
                            // 落点按**视觉**中心算（模型坐标 + 拖动偏移）
                            var origin = _dragOrigins[0].Origin;
                            var center = new Point(
                                origin.X + _dragOffset.X + FlowCanvasViewModel.NodeWidth / 2,
                                origin.Y + _dragOffset.Y + FlowCanvasViewModel.NodeHeight / 2);

                            _viewModel.CompleteSurfaceNodeDrag(dragged, center);
                        }

                        _dragOrigins.Clear();
                        _dragOffset = default;
                        break;
                    }

                case SurfaceGesture.Marquee:
                    ApplyMarqueeSelection();
                    SurfacePreview.MarqueeScreenRect = null;
                    break;

                case SurfaceGesture.Pan:
                    UpdateMinimapViewport();
                    break;
            }
        }

        /// <summary>框选落定：按屏幕矩形反变换到图坐标，选中与之相交的节点（泳道不参与）。</summary>
        private void ApplyMarqueeSelection()
        {
            if (_viewModel == null) return;

            var box = SurfacePreview.MarqueeScreenRect;
            if (box is not { } screen || screen.Width < 3 || screen.Height < 3)
            {
                // 空白处单击（没拖出面积）= 清空选中
                if (!_marqueeAdditive)
                {
                    foreach (var n in _viewModel.Nodes) n.IsSelected = false;
                }
                return;
            }

            double zoom = SurfacePreview.Zoom <= 0 ? 1 : SurfacePreview.Zoom;
            var graph = new Rect(
                (screen.X - SurfacePreview.Pan.X) / zoom,
                (screen.Y - SurfacePreview.Pan.Y) / zoom,
                screen.Width / zoom,
                screen.Height / zoom);

            var hits = SurfacePreview.HitTestRect(graph);

            if (!_marqueeAdditive)
            {
                foreach (var n in _viewModel.Nodes) n.IsSelected = false;
            }

            foreach (var n in hits) n.IsSelected = true;
        }

        // ==================================================================
        //  辅助
        // ==================================================================

        /// <summary>
        /// 按数据项找它的 ItemContainer。先问 ItemsControl 的生成器，问不到再遍历可视树——
        /// NodifyEditor.ItemsHost 是 internal，视图这边拿不到容器宿主面板。
        /// </summary>
        private ItemContainer? FindContainerFor(CanvasNodeViewModel node)
        {
            if (Editor.ItemContainerGenerator.ContainerFromItem(node) is ItemContainer generated
                && ReferenceEquals(generated.DataContext, node))
            {
                return generated;
            }

            return SearchContainer(Editor, node);
        }

        private static ItemContainer? SearchContainer(DependencyObject root, CanvasNodeViewModel node)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                // 命中容器但不是目标：不必再往下钻，容器里不会套着别的容器
                if (child is ItemContainer { DataContext: CanvasNodeViewModel data })
                {
                    if (ReferenceEquals(data, node))
                        return (ItemContainer)child;

                    continue;
                }

                var found = SearchContainer(child, node);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
