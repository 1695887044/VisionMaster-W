using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 组态「选择轴」弹窗的视图。逻辑全在 <c>ScadaAxisPickerViewModel</c>，
    /// 这里只保留 InitializeComponent（与其它选择器弹窗同一形态）。
    /// </summary>
    public partial class ScadaAxisPickerView : UserControl
    {
        public ScadaAxisPickerView()
        {
            InitializeComponent();
        }
    }
}
