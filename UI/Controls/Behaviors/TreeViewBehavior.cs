using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace UI.Behaviors
{
    public class TreeViewBehavior
    {
        static TreeViewBehavior()
        {
            EventManager.RegisterClassHandler(
                typeof(TreeView),
                TreeView.SelectedItemChangedEvent,
                new RoutedPropertyChangedEventHandler<object>(GlobalTreeView_SelectedItemChanged));
        }

        /// <summary>
        /// 双向：TreeView 选中 → 写回 VM；VM 改选中项 → 推送给 TreeView（选中并逐级展开到该节点）。
        /// 反向推送必须挂 PropertyChangedCallback，否则「画布双击下钻 → 流程树跟着跳过去」这类
        /// 程序端改选中项的场景，界面上完全没有反应（原来只有 TreeView→VM 单向）。
        /// </summary>
        public static readonly DependencyProperty BindableSelectedItemProperty =
            DependencyProperty.RegisterAttached(
                "BindableSelectedItem",
                typeof(object),
                typeof(TreeViewBehavior),
                new FrameworkPropertyMetadata(
                    default,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnBindableSelectedItemChanged));

        public static object GetBindableSelectedItem(DependencyObject obj) => obj.GetValue(BindableSelectedItemProperty);
        public static void SetBindableSelectedItem(DependencyObject obj, object value) => obj.SetValue(BindableSelectedItemProperty, value);

        /// <summary>true = 正在由 VM 往 TreeView 推送选中项，此时树自身冒出的选中变更不再写回 VM</summary>
        private static bool _syncingFromViewModel;

        /// <summary>true = 类处理器正把 TreeView 的选中项写进 DP，此时 DP 回调不再反向推送（回环防护）</summary>
        private static bool _updatingFromTreeView;

        // 3. 全局统一处理逻辑
        private static void GlobalTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_syncingFromViewModel) return;
            if (sender is not TreeView treeView) return;

            _updatingFromTreeView = true;
            try
            {
                SetBindableSelectedItem(treeView, e.NewValue);
            }
            finally
            {
                _updatingFromTreeView = false;
            }
        }

        #region 反向推送：VM 改选中项 → 展开路径 + 选中该节点

        private static void OnBindableSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TreeView tree) return;
            if (_updatingFromTreeView) return;   // 树自己写进来的，不需要推回去
            if (e.NewValue == null) return;
            if (ReferenceEquals(e.NewValue, tree.SelectedItem)) return;

            // 容器生成要等布局完成，直接在 DP 回调里走一遍 UpdateLayout 属"布局过程中触发布局"，
            // 可能抛异常或找不到容器；因此推迟到 Background 优先级（布局与渲染都已完成）。
            tree.Dispatcher.BeginInvoke(
                new Action(() => SyncSelectionFromViewModel(tree, e.NewValue)),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private static void SyncSelectionFromViewModel(TreeView tree, object target)
        {
            // 排队期间用户可能又改了选中项 / 切了流程：以当前 DP 值为准，过期请求直接丢弃
            var current = GetBindableSelectedItem(tree);
            if (current == null || !ReferenceEquals(current, target)) return;
            if (ReferenceEquals(target, tree.SelectedItem)) return;

            _syncingFromViewModel = true;
            try
            {
                // 找不到（目标不属于当前 ItemsSource、或该层被虚拟化未生成）就静默放弃：
                // 选中项不同步只是观感问题，抛异常会让整条绑定链断掉
                if (TrySelectDescendant(tree, target))
                    return;

                // 目标就在根层但上面没命中（例如 ItemsSource 尚未就绪）——不再重试，避免与用户点选打架
            }
            finally
            {
                _syncingFromViewModel = false;
            }
        }

        /// <summary>
        /// 深度优先在容器树里找目标数据项：途经的分支逐级展开，命中后置选中并滚动到可见。
        /// 每展开一层都 UpdateLayout，强制 ItemContainerGenerator 生成子容器（TreeView 默认不虚拟化）。
        /// </summary>
        private static bool TrySelectDescendant(ItemsControl host, object target)
        {
            host.UpdateLayout();

            for (int i = 0; i < host.Items.Count; i++)
            {
                if (host.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem container)
                    continue;   // 容器尚未生成：交给下一次显式操作，这里不猜索引

                if (ReferenceEquals(container.DataContext, target))
                {
                    container.IsSelected = true;
                    container.BringIntoView();
                    return true;
                }

                if (!container.HasItems) continue;

                bool wasExpanded = container.IsExpanded;
                container.IsExpanded = true;

                if (TrySelectDescendant(container, target))
                    return true;

                container.IsExpanded = wasExpanded;   // 这条子树里没有，恢复原展开态，不把整棵树摊开
            }

            return false;
        }

        #endregion

        #region 双击命令（双击节点 = 选中该节点 + 执行命令，View 零 code-behind）

        /// <summary>挂到 TreeView 上：任意节点被双击时先选中它，再执行该命令</summary>
        public static readonly DependencyProperty DoubleClickCommandProperty =
            DependencyProperty.RegisterAttached(
                "DoubleClickCommand",
                typeof(ICommand),
                typeof(TreeViewBehavior),
                new PropertyMetadata(null, OnDoubleClickCommandChanged));

        /// <summary>随 DoubleClickCommand 一起传给命令的参数</summary>
        public static readonly DependencyProperty DoubleClickCommandParameterProperty =
            DependencyProperty.RegisterAttached(
                "DoubleClickCommandParameter",
                typeof(object),
                typeof(TreeViewBehavior),
                new PropertyMetadata(null));

        public static ICommand GetDoubleClickCommand(DependencyObject obj) => (ICommand)obj.GetValue(DoubleClickCommandProperty);
        public static void SetDoubleClickCommand(DependencyObject obj, ICommand value) => obj.SetValue(DoubleClickCommandProperty, value);
        public static object GetDoubleClickCommandParameter(DependencyObject obj) => obj.GetValue(DoubleClickCommandParameterProperty);
        public static void SetDoubleClickCommandParameter(DependencyObject obj, object value) => obj.SetValue(DoubleClickCommandParameterProperty, value);

        private static void OnDoubleClickCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TreeView treeView) return;
            if (e.NewValue != null)
                treeView.MouseDoubleClick += TreeView_MouseDoubleClick;
            else
                treeView.MouseDoubleClick -= TreeView_MouseDoubleClick;
        }

        private static void TreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TreeView treeView) return;

            // 只响应"双击在节点上"：空白区双击不触发
            var source = e.OriginalSource as DependencyObject;
            if (source == null) return;

            // 展开/折叠小箭头上的双击放行给 TreeView 自己，不劫持为"打开配置"
            if (FindAncestor<ToggleButton>(source) != null) return;

            if (FindAncestor<TreeViewItem>(source) is not TreeViewItem item) return;

            ICommand command = GetDoubleClickCommand(treeView);
            if (command == null) return;
            object parameter = GetDoubleClickCommandParameter(treeView);

            // 先选中被双击的节点：BindableSelectedItem 双向绑定会同步 VM 的当前选中项，
            // 命令内部（按选中项取参）拿到的就是这张卡片，而不是上一次选中的节点
            item.IsSelected = true;

            if (command.CanExecute(parameter))
                command.Execute(parameter);
            // 不置 e.Handled：保留双击展开/选中等原生行为
        }

        /// <summary>沿视觉树向上找指定类型的祖先（源可能是文本、图标等深层元素）</summary>
        private static T FindAncestor<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null && source is not T)
            {
                source = (source is Visual || source is Visual3D)
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }
            return source as T;
        }

        #endregion
    }
}