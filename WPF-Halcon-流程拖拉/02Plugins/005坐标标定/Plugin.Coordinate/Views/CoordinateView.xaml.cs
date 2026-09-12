using HalconDotNet;
using Plugin.Coordinate.ViewModels;
using System.Windows;
using VM.Halcon;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Models;
using VM.Start.Services;

namespace Plugin.Coordinate.Views
{
    /// <summary>
    /// GrabImageModuleView.xaml 的交互逻辑
    /// </summary>
    public partial class CoordinateView : ModuleViewBase
    {
        public CoordinateView()
        {
            InitializeComponent();
        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

    }

}
