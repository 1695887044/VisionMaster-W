using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Nodify;
using Prism.Dialogs;
using Prism.Ioc;
using VisionMaster.Models;
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

            // 入树/离树成对挂摘订阅：AvalonDock 切换标签页、隐藏面板都会触发 Unloaded，
            // 所以清理必须可逆（离树摘干净让旧 VM 可 GC，入树重新挂上），不能用一次性 Dispose。
            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;

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

        /// <summary>工具箱拖入：只放行 ToolItemModel 负载（Scada 图元/文本等一律显「禁止」）</summary>
        private void OnEditorDragOver(object sender, DragEventArgs e)
        {
            e.Effects = FindDraggedTool(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>
        /// 指针在编辑器内移动：把图坐标回报给画布 VM（粘贴落点用；高频写、无绑定消费）。
        /// 换算与 Editor.GetLocationInsideEditor(DragEventArgs)（工具箱拖入用的是库内重载）
        /// 同一条口径：图坐标 = 视口原点 + 屏幕坐标 / 缩放。
        /// </summary>
        private void OnEditorPointerMoved(object sender, MouseEventArgs e)
        {
            if (_viewModel == null) return;

            var screen = e.GetPosition(Editor);
            double zoom = Editor.ViewportZoom;
            if (zoom <= 0) zoom = 1.0;

            _viewModel.LastPointerGraphPosition = new Point(
                Editor.ViewportLocation.X + screen.X / zoom,
                Editor.ViewportLocation.Y + screen.Y / zoom);
        }

        private void OnEditorDrop(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;

            var tool = FindDraggedTool(e.Data);
            if (tool == null || _viewModel == null) return;

            // 图坐标（与节点 Location / 容器框矩形同一坐标系）：
            // Nodify 7.3 的 GetLocationInsideEditor(DragEventArgs) 直接做视口→图坐标换算
            if (_viewModel.DropToolAt(tool, Editor.GetLocationInsideEditor(e)))
                e.Effects = DragDropEffects.Copy;
        }

        /// <summary>
        /// 从拖放负载取工具箱模板。Gong 的 IsDragSource 把数据本体按「类型即格式键」投放，
        /// 但不同版本/自定义 DragSource 可能换格式——先按类型直取，再逐个格式判型兜底
        ///（延迟呈现的格式取值会抛，跳过继续找；全拿不到就是「不是我们的负载」，显禁止）。
        /// </summary>
        private static ToolItemModel? FindDraggedTool(IDataObject? data)
        {
            if (data == null) return null;

            if (data.GetDataPresent(typeof(ToolItemModel))
                && data.GetData(typeof(ToolItemModel)) is ToolItemModel direct)
                return direct;

            foreach (var format in data.GetFormats())
            {
                try
                {
                    if (data.GetData(format) is ToolItemModel tool)
                        return tool;
                }
                catch
                {
                    // 延迟呈现的提供方可能已经不在场：跳过该格式继续找
                }
            }

            return null;
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
