using System.Windows;
using VM.Start.Common.Provide;
using VM.Start.Core;

namespace Plugin.SystemTime.Views
{
    /// <summary>
    /// VarDefineView.xaml 的交互逻辑
    /// </summary>
    public partial class SystemTimeView : ModuleViewBase
    {
        public SystemTimeView()
        {
            InitializeComponent();

        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
