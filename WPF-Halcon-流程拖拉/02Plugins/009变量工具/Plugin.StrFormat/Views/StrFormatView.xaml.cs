
using Plugin.StrFormat.ViewModels;
using System.Windows;
using VM.Halcon;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Models;
using VM.Start.Services;

namespace Plugin.StrFormat.Views
{
    /// <summary>
    /// GrabImageModuleView.xaml 的交互逻辑
    /// </summary>
    public partial class StrFormatView : ModuleViewBase
    {
        public StrFormatView()
        {
            InitializeComponent();
        }
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

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            TextView textView = new TextView();
            if (dg.SelectedItem == null) return;
            textView.DataContext = this.DataContext;
            textView.ShowDialog();
        }

        private void dg_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

        }
    }

}
