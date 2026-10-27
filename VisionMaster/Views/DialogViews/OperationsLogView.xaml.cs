using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>操作记录弹窗（只读）。查询/分页/权限/复制/导出都在视图模型里，这里只做双击行的落点守卫。</summary>
    public partial class OperationsLogView : UserControl
    {
        public OperationsLogView() => InitializeComponent();

        /// <summary>
        /// 双击行 = 复制该行明细（设计稿 §6）。写剪贴板与提示都在视图模型里；
        /// 这里只判"点有没有落在行上"——双击空白/表头不触发。
        /// </summary>
        private void RowsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not OperationsLogViewModel vm) return;
            if (e.OriginalSource is not DependencyObject source) return;
            if (ItemsControl.ContainerFromElement((ItemsControl)sender, source) == null) return;

            vm.CopySelectedRowDetail();
        }
    }
}
