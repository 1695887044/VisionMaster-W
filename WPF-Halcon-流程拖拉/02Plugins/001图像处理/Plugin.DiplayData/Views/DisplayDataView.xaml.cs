using HalconDotNet;
using Plugin.DiplayData.ViewModels;
using System.Windows;
using VM.Halcon;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Models;
using VM.Start.Services;

namespace Plugin.DiplayData.Views
{
    /// <summary>
    /// GrabImageModuleView.xaml 的交互逻辑
    /// </summary>
    public partial class DisplayDataView : ModuleViewBase
    {
        public DisplayDataView()
        {
            InitializeComponent();
        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private void dg_PreviewMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (dg.SelectedItem == null) return;
            TextView textView = new TextView();
            textView.DataContext = this.DataContext;
            textView.ShowDialog();
        }
    }

}
