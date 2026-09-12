using System.Windows;
using VM.Start.Common.Provide;
using VM.Start.Core;

namespace Plugin.HKSetOutPut.Views
{
    /// <summary>
    /// DelayView.xaml 的交互逻辑
    /// </summary>
    public partial class DelayView : ModuleViewBase
    {
        public DelayView()
        {
            InitializeComponent();

        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
