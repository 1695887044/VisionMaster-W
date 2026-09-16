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
    /// LoadingView.xaml 的交互逻辑
    /// </summary>
    public partial class LoadingView : MetroWindow
    {
        public LoadingView()
        {
            InitializeComponent();
            DataContext = new LoadingViewModel();
        }
        private static LoadingView _instance;
        public static LoadingView Ins
        {
            get
            {
                Application.Current.Dispatcher.Invoke(() => { _instance = new LoadingView(); });
                return _instance;
            }
        }
        public void LoadingShow(string msg)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    var vm = DataContext as LoadingViewModel;
                    vm.Message = msg;
                    this.Topmost = true;
                    vm.ToolBarMsg = Resource.Info;
                    vm.Icon = new BitmapImage(new Uri(@"/Assets/Images/Info.png", UriKind.Relative));
                    Show();
                }
                catch (Exception ex)
                {
                }
            });

        }

    }
}
