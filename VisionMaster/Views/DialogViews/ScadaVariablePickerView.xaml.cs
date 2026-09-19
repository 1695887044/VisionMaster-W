using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 组态「选择变量」弹窗：从工程变量清单里挑一个，回传 (变量 Id, 变量名)。
    /// 视图本身无逻辑——清单、过滤、预选、确认口径全在
    /// <see cref="ViewModels.DialogViewModels.ScadaVariablePickerViewModel"/> 里。
    /// </summary>
    public partial class ScadaVariablePickerView : UserControl
    {
        public ScadaVariablePickerView()
        {
            InitializeComponent();
        }
    }
}
