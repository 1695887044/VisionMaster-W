using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 组态「选择配方」弹窗：从当前方案的配方清单里挑一条，回传 (配方 Id, 配方名)。
    /// 视图本身无逻辑——清单、预选、确认口径全在
    /// <see cref="ViewModels.DialogViewModels.ScadaRecipePickerViewModel"/> 里。
    /// </summary>
    public partial class ScadaRecipePickerView : UserControl
    {
        public ScadaRecipePickerView()
        {
            InitializeComponent();
        }
    }
}
