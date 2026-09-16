using HalconDotNet;
using Plugin.Matching1.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using VM.Start.Services;

namespace Plugin.Matching1.Views
{
    /// <summary>
    /// SaveImageView.xaml 的交互逻辑
    /// </summary>
    public partial class Matching1View : ModuleViewBase
    {
        public Matching1View()
        {
            InitializeComponent();
        }
        [NonSerialized]
        public VMHWindowControl mWindowH_Template;
        Matching1ViewModel viewModel;

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
