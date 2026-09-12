using Plugin.PerProcessing.ViewModels;
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
using VM.Start.Common.Provide;
using VM.Start.Core;

namespace Plugin.PerProcessing.Views
{
    /// <summary>
    /// SaveImageView.xaml 的交互逻辑
    /// </summary>
    public partial class PerProcessingView : ModuleViewBase
    {
        public PerProcessingView()
        {
            InitializeComponent();
        }

        private void ModuleViewBase_Loaded(object sender, RoutedEventArgs e)
        {
            if (mWindowH == null)
            {
                mWindowH = new VMHWindowControl();
                winFormHost.Child = mWindowH;
            }
        }

        private void btnComp_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var viewModel1 = DataContext as PerProcessingViewModel;
            if (viewModel1 == null) return;
            if (viewModel1.DispImage != null && viewModel1.DispImage.IsInitialized())
                mWindowH.Image = viewModel1.DispImage;
            mWindowH.DispObj(mWindowH.Image);
        }

        private void btnComp_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var viewModel1 = DataContext as PerProcessingViewModel;
            if (viewModel1 == null) return;
            viewModel1.ExeModule();
            if (viewModel1.m_outImage != null && viewModel1.m_outImage.IsInitialized())
                mWindowH.Image = viewModel1.m_outImage;
            mWindowH.DispObj(mWindowH.Image);
        }
    }
}
