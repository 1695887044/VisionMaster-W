using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace UI.CustomControl
{
    /// <summary>
    /// OverlayHost.xaml 的交互逻辑
    /// </summary>
    public partial class OverlayHost : UserControl
    {
        internal static OverlayHost Instance { get; private set; }
        public OverlayHost()
        {
            InitializeComponent();
            Instance=this;
        }
        private void Notification_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is NotificationMessage msg)
            {
                msg.IsHovered = true; // 鼠标进来，标记为悬停
            }
        }

        private void Notification_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is NotificationMessage msg)
            {
                msg.IsHovered = false; // 鼠标离开，恢复计时
            }
        }
        private void BtnConfirm_Click(object sender, RoutedEventArgs e) => EasyDialog.SetResult(true);
        private void BtnCancel_Click(object sender, RoutedEventArgs e) => EasyDialog.SetResult(false);
        //private void Backdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Dialog.SetResult(false);
        private void Backdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>Console.WriteLine("");
    }
}
