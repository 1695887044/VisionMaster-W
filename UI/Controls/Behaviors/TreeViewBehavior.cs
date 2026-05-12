using System.Windows;
using System.Windows.Controls;

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
    }
}