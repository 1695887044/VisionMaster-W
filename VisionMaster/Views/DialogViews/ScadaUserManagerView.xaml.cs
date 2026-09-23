using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 组态「用户管理」弹窗：增账号、删账号、改角色、重置密码（管理员专属）。
    ///
    /// 视图本身无逻辑——清单、表单、权限把关、二次确认全在
    /// <see cref="ViewModels.DialogViewModels.ScadaUserManagerViewModel"/> 里。
    ///
    /// 与 <c>ScadaAlarmHistoryView</c>（手工装配、能注入 Confirm 委托弹 MessageBox）不同，
    /// 这里走 <c>prism:ViewModelLocator.AutoWireViewModel</c>：视图模型需要
    /// <c>ScadaUserStore</c> + <c>ScadaAccessPolicy</c> 两个注入，交给容器装配；
    /// 代价是构造期拿不到"由哪个窗口来弹"，所以删除的二次确认改在弹窗内就地做。
    /// </summary>
    public partial class ScadaUserManagerView : UserControl
    {
        public ScadaUserManagerView()
        {
            InitializeComponent();
        }
    }
}
