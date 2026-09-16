using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VM.Halcon;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.ViewModels;

namespace VM.Start.Views
{
    /// <summary>
    /// SystemParamView.xaml 的交互逻辑
    /// </summary>
    public partial class HardwareConfigView : MetroWindow
    {
        #region Singleton
        private static HardwareConfigView _instance = new HardwareConfigView();

        public HardwareConfigView()
        {
            InitializeComponent();
            this.DataContext = HardwareConfigViewModel.Ins;

        }
        public static HardwareConfigView Ins
        {
            get { return _instance; }
            set { _instance = value; }
        }

        #endregion

        #region Prop
        #endregion

        #region Method

        public bool IsClosed { get; set; } = true;

        protected override void OnClosing(CancelEventArgs e)
        {
            EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
            e.Cancel = true;  // cancels the window close
            IsClosed = true;
            this.Hide();      // Programmatically hides the window
        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }


        #endregion

        private void btnJogBak_MouseUp(object sender, MouseButtonEventArgs e)
        {
            HardwareConfigViewModel.Ins.SelectedAxis.Stop();
        }

        private void btnJogBak_MouseDown(object sender, MouseButtonEventArgs e)
        {
            HardwareConfigViewModel.Ins.SelectedAxis.MoveJog(eDirection.Negative, HardwareConfigViewModel.Ins.SelectedAxis.JogVel);
        }

        private void btnEnable_Click(object sender, RoutedEventArgs e)
        {
            if (HardwareConfigViewModel.Ins.SelectedAxis.SvOn)
            {
                HardwareConfigViewModel.Ins.SelectedAxis.Disable();
            }
            else
            {
                HardwareConfigViewModel.Ins.SelectedAxis.Enable();
            }
        }

        private void btnJogFwd_MouseDown(object sender, MouseButtonEventArgs e)
        {
            HardwareConfigViewModel.Ins.SelectedAxis.MoveJog(eDirection.Positive, HardwareConfigViewModel.Ins.SelectedAxis.JogVel);
        }

        private void btnJogFwd_MouseUp(object sender, MouseButtonEventArgs e)
        {
            HardwareConfigViewModel.Ins.SelectedAxis.Stop();
        }

        private void btnMove_Click(object sender, RoutedEventArgs e)
        {
            if (HardwareConfigViewModel.Ins.SelectedAxis.IsRelMove)
            {
                HardwareConfigViewModel.Ins.SelectedAxis.MoveRel(HardwareConfigViewModel.Ins.SelectedAxis.RunPos, HardwareConfigViewModel.Ins.SelectedAxis.RunVel);
            }
            else
            {
                HardwareConfigViewModel.Ins.SelectedAxis.MoveAbs(HardwareConfigViewModel.Ins.SelectedAxis.RunPos, HardwareConfigViewModel.Ins.SelectedAxis.RunVel);
            }
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            HardwareConfigViewModel.Ins.SelectedAxis.Stop();
        }

        private void btnHome_Click(object sender, RoutedEventArgs e)
        {
            HardwareConfigViewModel.Ins.SelectedAxis.Home();
        }

        private void ClearAlm(object sender, MouseButtonEventArgs e)
        {
            HardwareConfigViewModel.Ins.SelectedAxis.ClearAlm();
        }

        private void cmbMotionBrand_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbMotionBrand.SelectedIndex < 0) return;
            if (cmbMotionBrand.SelectedValue.ToString() == "正运动")
            {
                HardwareConfigViewModel.Ins.MotionTypes = new List<string>
                {
                    "ECI3428",
                    "ECI3828",
                    "ZMC408SCAN"
                };
            }
            cmbMotionType.ItemsSource = HardwareConfigViewModel.Ins.MotionTypes;
            cmbMotionType.SelectedIndex = 0;
        }

    }
}
