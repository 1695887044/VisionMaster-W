using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// MotionDebugView.xaml 的交互逻辑。
    ///
    /// 刻意保持零逻辑：所有手动操作（使能/点动/定位/回零/IO）都是 VM 上的命令，
    /// "按住才动"由 UI 库的 <c>ui:HoldCommandBehavior</c> 提供 —— 两者都不需要 code-behind。
    /// 这样这些行为在无 WPF 宿主的环境里也能被断言（见 MotionChecks）。
    /// </summary>
    public partial class MotionDebugView : UserControl
    {
        public MotionDebugView()
        {
            InitializeComponent();
        }
    }
}
