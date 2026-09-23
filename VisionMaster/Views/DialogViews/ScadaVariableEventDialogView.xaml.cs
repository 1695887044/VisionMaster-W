using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 「变量事件」弹窗：按变量集中配置值驱动的规则（更改数值 / 值为真 / 值为假 / 上限 / 下限）。
    /// 视图本身无逻辑——清单认领、阈值解析、写回与审计全在
    /// <see cref="ViewModels.DialogViewModels.ScadaVariableEventDialogViewModel"/> 里。
    /// 右栏那五类事件用的是公共控件 <see cref="Controls.ScadaEventEditor"/>，
    /// 与属性面板里的事件行是同一份实现。
    /// </summary>
    public partial class ScadaVariableEventDialogView : UserControl
    {
        public ScadaVariableEventDialogView()
        {
            InitializeComponent();
        }
    }
}
