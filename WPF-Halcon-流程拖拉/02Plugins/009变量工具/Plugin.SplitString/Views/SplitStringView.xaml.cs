using Plugin.SplitString.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VM.Halcon;
using VM.Start.Core;

namespace Plugin.SplitString.Views
{
    /// <summary>
    /// SplitStringView.xaml 的交互逻辑
    /// </summary>
    public partial class SplitStringView : ModuleViewBase
    {
        public SplitStringView()
        {
            InitializeComponent();
        }
        private void ModuleViewBase_Loaded(object sender, RoutedEventArgs e)
        {

        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

    }

}
