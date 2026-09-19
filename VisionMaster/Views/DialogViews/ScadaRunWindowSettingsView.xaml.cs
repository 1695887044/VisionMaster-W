using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 「运行窗口设置」弹窗：选组态运行窗口的显示形态（依附主窗口 / 独立窗口）。
    /// 视图本身无逻辑——读写配置、取消语义、选项互斥全在
    /// <see cref="ViewModels.DialogViewModels.ScadaRunWindowSettingsViewModel"/> 里。
    /// </summary>
    public partial class ScadaRunWindowSettingsView : UserControl
    {
        public ScadaRunWindowSettingsView()
        {
            InitializeComponent();
        }
    }
}
