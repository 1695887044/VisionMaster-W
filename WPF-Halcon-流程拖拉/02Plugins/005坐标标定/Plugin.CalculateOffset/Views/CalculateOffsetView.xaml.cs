using HalconDotNet;
using Plugin.CalculateOffset.ViewModels;
using System.Windows;
using VM.Halcon;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Models;
using VM.Start.Services;

namespace Plugin.CalculateOffset.Views
{
    /// <summary>
    /// GrabImageModuleView.xaml 的交互逻辑
    /// </summary>
    public partial class CalculateOffsetView : ModuleViewBase
    {
        public CalculateOffsetView()
        {
            InitializeComponent();
        }
        public VMHWindowControl mWindowH;
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

    }

}
