using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace VisionMaster.Views
{
    /// <summary>
    /// FlowListView.xaml 的交互逻辑（程序面板：流程列表）
    /// </summary>
    public partial class FlowListView : UserControl
    {
        public FlowListView()
        {
            InitializeComponent();

            // 入树/离树成对挂摘订阅：AvalonDock 切换标签页、隐藏面板都会触发 Unloaded，
            // 所以清理必须可逆（离树摘干净让旧 VM 可 GC，入树重新挂上），不能用一次性 Dispose。
            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;
            DataContextChanged += OnViewDataContextChanged;
        }

        // Prism 的 AutoWireViewModel 可能晚于 Loaded 才把 VM 装进 DataContext，
        // 此时入树事件已经过去，需要在这里补挂一次（Activate 幂等，不会重复订阅）
        private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsLoaded)
                (e.NewValue as ViewModels.FlowListViewModel)?.Activate();
        }

        private void OnViewLoaded(object sender, RoutedEventArgs e)
            => (DataContext as ViewModels.FlowListViewModel)?.Activate();

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
            => (DataContext as ViewModels.FlowListViewModel)?.Deactivate();

        /// <summary>
        /// 右键先选中：菜单命令作用于 SelectFlow，少了这一步会“右键 A 却操作 B”。
        /// Preview 事件早于 ContextMenu 打开；此处挂 Up（ProcessView 是 Down），两者同效。
        /// </summary>
        private void FlowList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item != null)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }

        private T? FindVisualParent<T>(DependencyObject? obj) where T : class
        {
            while (obj != null)
            {
                if (obj is T target)
                    return target;

                obj = VisualTreeHelper.GetParent(obj);
            }

            return null;
        }
    }
}
