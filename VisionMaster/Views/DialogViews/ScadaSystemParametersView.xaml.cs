using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 「系统参数设置」弹窗：空闲自动登出时长、审计 / 报警历史的保留天数。
    /// 视图本身无逻辑——读写配置、校验、取消语义全在
    /// <see cref="ViewModels.DialogViewModels.ScadaSystemParametersViewModel"/> 里。
    /// </summary>
    public partial class ScadaSystemParametersView : UserControl
    {
        public ScadaSystemParametersView()
        {
            InitializeComponent();
        }
    }
}
