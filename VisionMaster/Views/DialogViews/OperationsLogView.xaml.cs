using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>操作记录弹窗（只读）。视图本身没有逻辑，查询/分页/权限都在 VM 里。</summary>
    public partial class OperationsLogView : UserControl
    {
        public OperationsLogView() => InitializeComponent();
    }
}
