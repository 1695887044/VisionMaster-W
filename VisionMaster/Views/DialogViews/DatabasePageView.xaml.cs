using System.Windows;
using System.Windows.Controls;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 硬件管理 → 数据库页（嵌入页）。
    ///
    /// DataContext 由 Prism AutoWire 按命名约定注入。
    /// 代码后置只做两件 View 必须参与的事：
    ///   ① PasswordBox 的值不进绑定引擎（明文密码不落 DataContext 之外的任何地方）；
    ///   ② 切换 profile 时清空密码框，避免把 A 的密码带进 B 的编辑态。
    /// </summary>
    public partial class DatabasePageView : UserControl
    {
        public DatabasePageView()
        {
            InitializeComponent();
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is DatabasePageViewModel vm && sender is PasswordBox box)
                vm.SetPassword(box.Password);
        }

        private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 选中另一条 profile：清空密码框（新 profile 的 PasswordTouched 本来就是 false，
            // 不清会把上一条的明文留在界面上，用户以为"这条也是这个密码"）。
            ProfilePasswordBox?.Clear();
        }
    }
}
