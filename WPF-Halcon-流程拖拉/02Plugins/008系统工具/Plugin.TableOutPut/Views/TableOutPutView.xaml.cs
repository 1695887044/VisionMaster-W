using HalconDotNet;
using Plugin.TableOutPut.ViewModels;
using System.Windows;
using VM.Halcon;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Models;
using VM.Start.Services;

namespace Plugin.TableOutPut.Views
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
        public VMHWindowControl mWindowH;
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void dg_PreviewMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            TextView textView = new TextView();
            if (dg.SelectedItem == null) return;
            textView.DataContext = this.DataContext;
            textView.ShowDialog();
        }
    }

}
