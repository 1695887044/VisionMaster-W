using System.Windows.Controls;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>轴点位表（每轴 16 行，行内编辑即生效）—— 见 AxisPointsViewModel 的说明</summary>
    public partial class AxisPointsView : UserControl
    {
        public AxisPointsView()
        {
            InitializeComponent();

            // 每次页签激活都刷新卡列表：方案可能刚加/删了卡，
            // 而这个视图的 DataContext（ViewModelLocator 接的）在首次构造后不会重建。
            Loaded += (_, _) => (DataContext as AxisPointsViewModel)?.Refresh();
        }
    }
}
