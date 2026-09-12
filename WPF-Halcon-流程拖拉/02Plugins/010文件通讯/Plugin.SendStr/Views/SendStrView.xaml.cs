using Plugin.SendStr.ViewModels;
using System.Windows;
using VM.Start.Common.Provide;
using VM.Start.Communacation;
using VM.Start.Core;

namespace Plugin.SendStr.Views
{
    /// <summary>
    /// ReceiveStrView.xaml 的交互逻辑
    /// </summary>
    public partial class SendStrView : ModuleViewBase
    {
        public SendStrView()
        {
            InitializeComponent();
        }


        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ModuleViewBase_Activated(object sender, System.EventArgs e)
        {
            InitData();
        }

        private void ModuleViewBase_Loaded(object sender, RoutedEventArgs e)
        {
            InitData();
        }
        #region Method
        private void InitData()
        {
            var viewModel = DataContext as SendStrViewModel;
            if (viewModel == null) return;
            viewModel.ComKeys = EComManageer.GetKeys();
        }
        #endregion
    }
}
