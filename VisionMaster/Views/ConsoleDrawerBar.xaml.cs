using System.Windows.Controls;

namespace VisionMaster.Views
{
    /// <summary>
    /// 底部控制台抽屉条（收起态 32px）。
    ///
    /// 刻意**不用** <c>prism:ViewModelLocator.AutoWireViewModel</c>：
    /// 它的 DataContext 由 Shell.xaml 显式绑到 <c>ShellViewModel.ConsoleDrawer</c>，
    /// 这样断言宿主（UIThemeSmokeTest 加载 ShellCopy.xaml，没有容器）解析这个视图时
    /// 不会去容器里找一个不存在的 VM 而抛异常。
    /// </summary>
    public partial class ConsoleDrawerBar : UserControl
    {
        public ConsoleDrawerBar()
        {
            InitializeComponent();
        }
    }
}
