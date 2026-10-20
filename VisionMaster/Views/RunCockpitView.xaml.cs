using System.Windows;

namespace VisionMaster.Views
{
    /// <summary>
    /// 运行驾驶舱：深色顶条 + 图像区 + KPI 条。
    /// 图像区复用现有 <see cref="ImageView"/>，不复制图像渲染链路。
    /// KPI 目前没有运行统计源，显示“待接入”，不伪造生产数字。
    /// </summary>
    public partial class RunCockpitView : System.Windows.Controls.UserControl
    {
        public RunCockpitView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // AvalonDock 的文档内容通常继承 Shell 的 DataContext；浮窗/独立宿主下可能为空。
            // 只在为空时兜底，不覆盖外部显式设置的 DataContext。
            if (DataContext == null && Application.Current?.MainWindow?.DataContext is object shellVm)
                DataContext = shellVm;
        }
    }
}
