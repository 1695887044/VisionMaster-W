using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 扫描组编辑器视图。代码后置只承担三件"XAML 做不到"的事：
    /// ① 打开"一键预设"的下拉菜单（Button + ContextMenu 的组合）；
    /// ② 拖动手柄的拖动启动；
    /// ③ 拖放落点判定（把落点行翻成 VM 能懂的"移到哪一行"）。
    /// 业务逻辑一律在 <see cref="ScanGroupEditorViewModel"/>，这里不碰组表数据。
    /// </summary>
    public partial class ScanGroupEditorView : UserControl
    {
        /// <summary>按下时的鼠标位置。拖动只有超过系统阈值才算数——否则点一下就误触发拖动</summary>
        private Point _dragOrigin;

        /// <summary>正在被拖动的行（在按下手柄时记下）</summary>
        private ScanGroupRow? _dragRow;

        public ScanGroupEditorView() => InitializeComponent();

        /// <summary>
        /// 打开"一键预设"菜单。
        /// 之所以要代码后置：Button 没有"下拉"概念，得手动把 ContextMenu 挂到按钮下方并展开。
        /// </summary>
        private void PresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.ContextMenu is null) return;

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }

        private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragOrigin = e.GetPosition(null);
            _dragRow = (sender as FrameworkElement)?.DataContext as ScanGroupRow;
        }

        private void DragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragRow is null) return;

            var current = e.GetPosition(null);
            if (Math.Abs(current.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return; // 还没挪够系统认定的"拖动距离"，继续等
            }

            DragDrop.DoDragDrop((DependencyObject)sender, _dragRow, DragDropEffects.Move);
        }

        private void GroupsGrid_Drop(object sender, DragEventArgs e)
        {
            var source = _dragRow;
            _dragRow = null; // 无论本次落点是否有效，拖动状态都要清掉，避免下一次误用旧值

            if (source is null) return;
            if (e.OriginalSource is not DependencyObject origin) return;

            // 落点元素往上找它所属的行；拖到空白处（表头/表格下方）会拿到 null，此时不动
            if (ItemsControl.ContainerFromElement(GroupsGrid, origin) is not DataGridRow row) return;
            if (row.DataContext is not ScanGroupRow target) return;

            (DataContext as ScanGroupEditorViewModel)?.MoveGroup(source, target);
        }
    }
}
