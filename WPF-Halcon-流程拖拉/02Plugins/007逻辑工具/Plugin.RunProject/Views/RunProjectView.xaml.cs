using Plugin.RunProject.ViewModels;
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
using VM.Start.Common.Provide;
using VM.Start.Core;

namespace Plugin.RunProject.Views
{
    /// <summary>
    /// SaveImageView.xaml 的交互逻辑
    /// </summary>
    public partial class RunProjectView : ModuleViewBase
    {
        public RunProjectView()
        {
            InitializeComponent();
        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ModuleViewBase_Loaded(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as RunProjectViewModel;
            if (viewModel == null) return;
            viewModel.InitDataSource();
            Logger.AddLog("Load");
        }        

        //public VMHWindowControl mWindowH;
        //AssignmentStrView viewModel;
        //private void ModuleViewBase_Loaded(object sender, RoutedEventArgs e)
        //{
        //    if (mWindowH == null)
        //    {
        //        mWindowH = new VMHWindowControl();
        //        winFormHost.Child = mWindowH;
        //    }
        //}
    }
}
