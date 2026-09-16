using System.Windows;
using VM.Start.Common.Provide;
using VM.Start.Core;

namespace Plugin.If.Views
{
    /// <summary>
    /// GrabImageModuleView.xaml 的交互逻辑
    /// </summary>
    public partial class IfView : ModuleViewBase
    {
        public IfView()
        {
            InitializeComponent();

        }


        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
