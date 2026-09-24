using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 报警配置弹窗视图。代码后置只承担一件"XAML 做不到"的事：
    /// 打开「报警组」的历史值下拉菜单（Button + ContextMenu 的组合）。
    /// 业务逻辑一律在 <c>ScadaAlarmConfigDialogViewModel</c>，这里不碰报警数据。
    /// </summary>
    public partial class ScadaAlarmConfigDialogView : UserControl
    {
        public ScadaAlarmConfigDialogView() => InitializeComponent();

        /// <summary>
        /// 打开某一行「报警组」格子里那个小箭头的历史值菜单。
        /// 之所以要代码后置：Button 没有"下拉"概念，得手动把 ContextMenu 挂到按钮下方并展开
        /// （与扫描组编辑器的「一键预设」同一手法）。
        /// </summary>
        private void GroupHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.ContextMenu is null) return;

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }
    }
}
