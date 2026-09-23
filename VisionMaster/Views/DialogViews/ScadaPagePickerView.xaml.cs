using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 组态「选择画面」弹窗：从当前方案的画面清单里挑一页，回传 (画面 Id, 画面名)。
    /// 视图本身无逻辑——清单、过滤、预选、确认口径全在
    /// <see cref="ViewModels.DialogViewModels.ScadaPagePickerViewModel"/> 里。
    /// </summary>
    public partial class ScadaPagePickerView : UserControl
    {
        public ScadaPagePickerView()
        {
            InitializeComponent();
        }
    }
}
