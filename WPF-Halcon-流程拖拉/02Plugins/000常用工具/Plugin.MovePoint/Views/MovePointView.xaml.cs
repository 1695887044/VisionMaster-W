using Plugin.MovePoint.ViewModels;
using System;
using System.Windows;
using System.Windows.Input;
using VM.Start.Common.Enums;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.ViewModels;

namespace Plugin.MovePoint.Views
{
    /// <summary>
    /// MovePointView.xaml 的交互逻辑
    /// </summary>
    public partial class MovePointView : ModuleViewBase
    {
        public MovePointView()
        {
            InitializeComponent();
        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private void btnJogBak_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as MovePointViewModel;
            if (viewModel == null) return;
            viewModel.SelectedAxis.Stop();
        }

        private void btnJogBak_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as MovePointViewModel;
            if (viewModel == null) return;
            viewModel.SelectedAxis.MoveJog(eDirection.Negative, viewModel.SelectedAxis.JogVel);
        }

        private void btnEnable_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MovePointViewModel;
            if (viewModel == null) return;
            if (viewModel.SelectedAxis.SvOn)
            {
                viewModel.SelectedAxis.Disable();
            }
            else
            {
                viewModel.SelectedAxis.Enable();
            }
        }

        private void btnJogFwd_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as MovePointViewModel;
            if (viewModel == null) return;
            viewModel.SelectedAxis.MoveJog(eDirection.Positive, Convert.ToDouble(viewModel.GetLinkValue(viewModel.JogVel)));
        }

        private void btnJogFwd_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as MovePointViewModel;
            if (viewModel == null) return;
            viewModel.SelectedAxis.Stop();
        }

        private void btnMove_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MovePointViewModel;
            if (viewModel == null) return;
            if (viewModel.IsRelMove)
            {
                viewModel.SelectedAxis.MoveRel(Convert.ToDouble(viewModel.GetLinkValue(viewModel.RunPos)), Convert.ToDouble(viewModel.GetLinkValue(viewModel.RunVel)));
            }
            else
            {
                viewModel.SelectedAxis.MoveAbs(Convert.ToDouble(viewModel.GetLinkValue(viewModel.RunPos)), Convert.ToDouble(viewModel.GetLinkValue(viewModel.RunVel)));
            }
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MovePointViewModel;
            if (viewModel == null) return;
            viewModel.SelectedAxis.Stop();
        }

        private void btnHome_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MovePointViewModel;
            if (viewModel == null) return;
            viewModel.SelectedAxis.Home();
        }

        private void ClearAlm(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as MovePointViewModel;
            if (viewModel == null) return;
            viewModel.SelectedAxis.ClearAlm();
        }

        private void window_Closed(object sender, EventArgs e)
        {
            //var viewModel = DataContext as MovePointViewModel;
            //for (int i = 0; i < viewModel.Axis.Count; i++)
            //{
            //    viewModel.PtMoveToList[i] = viewModel.Axis[i].GoalPt;
            //    viewModel.OffSetLinkTextList[i] = viewModel.Axis[i].OffsetLinkText;
            //    viewModel.AxisChecked[i] = viewModel.Axis[i].AxisChecked;

            //}
        }
    }
}
