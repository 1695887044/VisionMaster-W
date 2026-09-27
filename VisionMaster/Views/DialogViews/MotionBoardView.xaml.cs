using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 运动板卡（合并窗口）视图：左栏选卡 + 四页签。
    ///
    /// 【零逻辑】上一版骨架在这里做"子 VM 桥接"（Loaded 后把页签的 DataContext 交回外壳），
    /// 实测层次混乱且桥接脆弱；这一版四个页签 VM 由外壳 VM 构造时直接创建、
    /// 选中卡由外壳单向下发 —— 视图只剩构造初始化，任何联动都在 VM 层。
    /// </summary>
    public partial class MotionBoardView : UserControl
    {
        public MotionBoardView()
        {
            InitializeComponent();
        }
    }
}
