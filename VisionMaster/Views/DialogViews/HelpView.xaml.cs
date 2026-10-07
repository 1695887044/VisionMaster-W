using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 「帮助手册」弹窗（左目录 / 右正文 / 隐藏目录·上一步·打印·字号）。
    /// 逻辑全在 <see cref="ViewModels.DialogViewModels.HelpViewModel"/> 里，这里只留 XAML 入口。
    /// </summary>
    public partial class HelpView : UserControl
    {
        public HelpView()
        {
            InitializeComponent();
        }
    }
}
