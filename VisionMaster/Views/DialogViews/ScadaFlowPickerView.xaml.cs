using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 组态「选择流程」弹窗：从当前方案的流程清单里挑一条，回传 (流程 Id, 流程名)。
    /// 视图本身无逻辑——清单、预选、确认口径全在
    /// <see cref="ViewModels.DialogViewModels.ScadaFlowPickerViewModel"/> 里。
    /// </summary>
    public partial class ScadaFlowPickerView : UserControl
    {
        public ScadaFlowPickerView()
        {
            InitializeComponent();
        }
    }
}
