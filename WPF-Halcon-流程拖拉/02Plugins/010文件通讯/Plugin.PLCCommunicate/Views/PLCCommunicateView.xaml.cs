using Plugin.PLCCommunicate.ViewModels;
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
using VM.Halcon;
using VM.Start.Common.Provide;
using VM.Start.Communacation;
using VM.Start.Core;

namespace Plugin.PLCCommunicate.Views
{
    /// <summary>
    /// SaveImageView.xaml 的交互逻辑
    /// </summary>
    public partial class PLCCommunicateView : ModuleViewBase
    {
        public PLCCommunicateView()
        {
            InitializeComponent();
        }
        PLCCommunicateViewModel viewModel2;
        private void ModuleViewBase_Loaded(object sender, RoutedEventArgs e)
        {
            InitData();
        }
        private void ModuleViewBase_Activated(object sender, EventArgs e)
        {
            InitData();
        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        #region Method
        private void InitData()
        {
            var viewModel1 = DataContext as PLCCommunicateViewModel;
            if (viewModel1 == null) return;
            viewModel1.ComKeys = EComManageer.GetKeys();            
        }
        #endregion

    }

}
