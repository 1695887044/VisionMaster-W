using MahApps.Metro.Controls;
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
using System.Windows.Shapes;
using VM.Start.Common.Enums;
using VM.Start.Dialogs.ViewModels;
using VM.Start.Localization;


namespace VM.Start.Dialogs.Views
{
    /// <summary>
    /// MessageView.xaml 的交互逻辑
    /// </summary>
    public partial class MessageView : MetroWindow
    {
        public MessageView()
        {
            InitializeComponent();
            DataContext = new MessageViewModel();
        }
        private static MessageView _instance;
        public static MessageView Ins
        {
            get
            {
                Application.Current.Dispatcher.Invoke(() => { _instance = new MessageView(); });
                return _instance;
            }
        }

        private MessageBoxButton _MessageBoxButton;
        public void MessageBoxShow(string msg, eMsgType msgType = eMsgType.Info, MessageBoxButton messageBoxButton = MessageBoxButton.OK, bool allowClose = true)
        {
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                try
                {
                    var vm = DataContext as MessageViewModel;
                    if (allowClose == false)
                    {
                        vm.ConfirmVisibility = Visibility.Collapsed;
                        vm.IsCloseButtonEnabled = false;
                        vm.IsMinButtonEnabled = false;
                    }
                    _MessageBoxButton = messageBoxButton;
                    vm.Message = msg;
                    this.Topmost = true;
                    switch (messageBoxButton)
                    {
                        case MessageBoxButton.OK:
                            vm.ConfirmVisibility = Visibility.Visible;
                            vm.CancelVisibility = Visibility.Hidden;
                            break;
                        case MessageBoxButton.OKCancel:
                            vm.ConfirmVisibility = Visibility.Visible;
                            vm.CancelVisibility = Visibility.Visible;
                            break;
                        case MessageBoxButton.YesNoCancel:
                            break;
                        case MessageBoxButton.YesNo:
                            break;
                        default:
                            break;
                    }
                    switch (msgType)
                    {
                        case eMsgType.Info:
                            vm.ToolBarMsg = Resource.Info;
                            vm.Icon = new BitmapImage(new Uri(@"/Assets/Images/Info.png", UriKind.Relative));
                            break;
                        case eMsgType.Warn:
                            vm.ToolBarMsg = Resource.Warn;
                            vm.Icon = new BitmapImage(new Uri(@"/Assets/Images/Warn.png", UriKind.Relative));
                            break;
                        case eMsgType.Error:
                            vm.ToolBarMsg = Resource.Error;
                            vm.Icon = new BitmapImage(new Uri(@"/Assets/Images/Error.png", UriKind.Relative));
                            break;
                        default:
                            break;
                    }
                    ShowDialog();
                }
                catch (Exception ex)
                {
                }
            }));

        }

        private void btnConfirm_Click(object sender, RoutedEventArgs e)
        {
            switch (_MessageBoxButton)
            {
                case MessageBoxButton.OK:
                    this.DialogResult = true;
                    break;
                case MessageBoxButton.OKCancel:
                    this.DialogResult = true;
                    break;
                case MessageBoxButton.YesNoCancel:
                    break;
                case MessageBoxButton.YesNo:
                    break;
                default:
                    break;
            }

        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            switch (_MessageBoxButton)
            {
                case MessageBoxButton.OK:
                    this.DialogResult = false;
                    break;
                case MessageBoxButton.OKCancel:
                    this.DialogResult = false;
                    break;
                case MessageBoxButton.YesNoCancel:
                    break;
                case MessageBoxButton.YesNo:
                    break;
                default:
                    break;
            }
        }
        private void MetroWindow_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                btnConfirm_Click(null, null);
            }
            else if (e.Key == Key.Escape)
            {
                btnCancel_Click(null, null);
            }
        }
    }
}
