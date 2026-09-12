using MahApps.Metro.Controls;
using System.ComponentModel;
using System.Windows;
using VM.Start.Common.Provide;
using VM.Start.Core;

namespace Plugin.DiplayData.Views
{
    /// <summary>
    /// GrabImageModuleView.xaml 的交互逻辑
    /// </summary>
    public partial class TextView : MetroWindow
    {
        public TextView()
        {
            InitializeComponent();

        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private void btnConfirm_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
