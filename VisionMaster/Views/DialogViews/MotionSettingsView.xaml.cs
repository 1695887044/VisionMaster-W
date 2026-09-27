using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// MotionSettingsView.xaml 的交互逻辑。
    ///
    /// 与相机设置视图一样刻意保持"零逻辑"：连接/断开、清报警、应用参数全部是 VM 上的命令，
    /// 视图只负责摆控件。这样这些行为在无 WPF 宿主的环境里也能被断言（见 MotionChecks）。
    /// </summary>
    public partial class MotionSettingsView : UserControl
    {
        public MotionSettingsView()
        {
            InitializeComponent();
        }
    }
}
