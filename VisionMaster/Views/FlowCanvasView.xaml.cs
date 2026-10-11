using System;
using System.Windows;
using System.Windows.Controls;
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

        public FlowCanvasView()
        {
            InitializeComponent();

            // 入树/离树成对挂摘订阅：AvalonDock 切换标签页、隐藏面板都会触发 Unloaded，
            // 所以清理必须可逆（离树摘干净让旧 VM 可 GC，入树重新挂上），不能用一次性 Dispose。
            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;
        }

        private void OnViewLoaded(object sender, RoutedEventArgs e)
            => _viewModel?.Activate();

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
            => _viewModel?.Deactivate();

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.ViewportActionRequested -= OnViewportActionRequested;
                _viewModel.ZoomCommandRequested -= OnZoomCommandRequested;
                _viewModel.ModuleLinkRequested -= OnModuleLinkRequested;
                // 旧 VM 即将失去视图：摘掉订阅，避免它继续响应流程变化（旧 VM 随之可被 GC）
                _viewModel.Deactivate();
            }

            _viewModel = e.NewValue as FlowCanvasViewModel;

            if (_viewModel == null) return;

            _viewModel.ViewportActionRequested += OnViewportActionRequested;
            _viewModel.ZoomCommandRequested += OnZoomCommandRequested;
            _viewModel.ModuleLinkRequested += OnModuleLinkRequested;

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
