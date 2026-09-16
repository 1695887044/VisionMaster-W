using HalconDotNet;
using Plugin.GrabImage.ViewModels;
using System.IO;
using System.Windows;
using VM.Halcon;
using VM.Start.Common.Enums;
using VM.Start.Common.Provide;
using VM.Start.Communacation;
using VM.Start.Core;
using VM.Start.Views.Dock;

namespace Plugin.GrabImage.Views
{
    /// <summary>
    /// GrabImageModuleView.xaml 的交互逻辑
    /// </summary>
    public partial class GrabImageView : ModuleViewBase
    {
        public GrabImageView()
        {
            InitializeComponent();
        }
        public VMHWindowControl mWindowH;
        GrabImageViewModel viewModel;

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #region Method
        private void InitData()
        {
            //var viewModel1 = DataContext as GrabImageViewModel;
            //if (viewModel1 == null) return;

            //viewModel1.AcquisitionModes = ;//EComManageer.GetPLCConnectKeys();
            ////viewModel1.upDataRemaks();
        }
        #endregion

        private void ModuleViewBase_Activated(object sender, System.EventArgs e)
        {

        }

        private void ModuleViewBase_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}
