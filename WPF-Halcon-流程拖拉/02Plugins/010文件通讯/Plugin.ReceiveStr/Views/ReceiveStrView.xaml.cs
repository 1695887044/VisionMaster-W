using Plugin.ReceiveStr.ViewModels;
using System.Windows;
using VM.Start.Common.Provide;
using VM.Start.Communacation;
using VM.Start.Core;

namespace Plugin.ReceiveStr.Views
{
    /// <summary>
    /// ReceiveStrView.xaml 的交互逻辑
    /// </summary>
    public partial class ReceiveStrView : ModuleViewBase
    {
        public ReceiveStrView()
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
            var viewModel = DataContext as ReceiveStrViewModel;
            if (viewModel == null) return;
            viewModel.ComKeys = EComManageer.GetKeys();
        }
        #endregion
    }
}
