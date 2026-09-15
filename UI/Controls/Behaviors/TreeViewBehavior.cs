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

        public static readonly DependencyProperty BindableSelectedItemProperty =
            DependencyProperty.RegisterAttached(
                "BindableSelectedItem",
                typeof(object),
                typeof(TreeViewBehavior),
                new FrameworkPropertyMetadata(default, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static object GetBindableSelectedItem(DependencyObject obj) => obj.GetValue(BindableSelectedItemProperty);
        public static void SetBindableSelectedItem(DependencyObject obj, object value) => obj.SetValue(BindableSelectedItemProperty, value);

        // 3. 全局统一处理逻辑
        private static void GlobalTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (sender is TreeView treeView)
            {
                SetBindableSelectedItem(treeView, e.NewValue);
            }
        }

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