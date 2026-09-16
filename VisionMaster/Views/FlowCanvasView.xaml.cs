using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Nodify;
using VisionMaster.ViewModels;

namespace VisionMaster.Views
{
    /// <summary>
    /// 流程节点画布视图。
    ///
    /// 只承担三件"视图模型够不着"的事：
    ///   1. 视口动作（FitToScreen / BringIntoView 是 NodifyEditor 的成员，视图模型拿不到）；
    ///   2. 双击下钻的手势识别（Nodify 的 ItemContainer 没有 Activated 事件，只能自己从鼠标事件里找容器）；
    ///   3. 把上面两件事接到 FlowCanvasViewModel 的命令与事件上。
    /// 节点内容、连线合法性、层级导航一律留在视图模型，这里不做任何业务判断。
    /// </summary>
    public partial class FlowCanvasView : UserControl
    {
        private FlowCanvasViewModel? _viewModel;

        public FlowCanvasView()
        {
            InitializeComponent();

            // 用 Preview（tunnel）而不是 MouseDoubleClick：ItemContainer 在 MouseDown 里就会把事件
            // 标成已处理（要接管拖拽），冒泡版的双击事件有收不到的风险，隧道版一定先经过编辑器。
            Editor.PreviewMouseDoubleClick += OnEditorPreviewMouseDoubleClick;

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
                // 旧 VM 即将失去视图：摘掉订阅，避免它继续响应流程变化（旧 VM 随之可被 GC）
                _viewModel.Deactivate();
            }

            _viewModel = e.NewValue as FlowCanvasViewModel;

            if (_viewModel == null) return;

            _viewModel.ViewportActionRequested += OnViewportActionRequested;

            // Prism 的 AutoWireViewModel 可能晚于 Loaded 才把 VM 装进 DataContext，
            // 此时入树事件已过去，需补挂一次（Activate 幂等，不会重复订阅）
            if (IsLoaded)
                _viewModel.Activate();

            // 视图模型在构造里就渲染过一次，那时还没有订阅者，切层的视口请求等于丢了。
            // 补一次"适应整层"，否则首次打开面板停在默认视口，节点可能整片在视野外。
            OnViewportActionRequested(null);
        }

        // ==================================================================
        //  视口动作
        // ==================================================================

        /// <summary>
        /// 视图模型的视口请求：null = 适应整层（切层/切流程），非 null = 把该节点滚入视野（外部选中联动）。
        /// 推迟到 Background 优先级执行——ItemsSource 刚 Clear/Add 完时 ItemContainer 还没生成，
        /// 此刻 FitToScreen 会按空内容计算，表现为"切完层画布缩在左上角"。
        /// </summary>
        private void OnViewportActionRequested(CanvasNodeViewModel? node)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (node == null)
                    {
                        Editor.FitToScreen(null);
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

        // ==================================================================
        //  双击下钻
        // ==================================================================

        private void OnEditorPreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || _viewModel == null) return;
            if (e.OriginalSource is not DependencyObject source) return;

            // 端口上的双击留给拉线手势：Connector 的连线手势就是左键按下，不能在这里抢
            if (FindAncestor<Connector>(source) != null) return;

            // 空白区双击（没有容器）不下钻，保持编辑器原有的框选/平移语义
            if (FindAncestor<ItemContainer>(source) is not { DataContext: CanvasNodeViewModel node }) return;

            // 不置 e.Handled：选中、拖拽等原生行为照常，下钻只是附加动作
            _viewModel.DrillDownCommand.Execute(node);
        }

        /// <summary>沿视觉树向上找指定类型的祖先（命中的可能是模板深处的文本或边框）</summary>
        private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null && source is not T)
            {
                source = (source is Visual || source is Visual3D)
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }

            return source as T;
        }
    }
}
