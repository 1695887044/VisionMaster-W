using MaterialDesignThemes.Wpf;
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
using VM.Start.Communacation;
using VM.Start.Events;
using VM.Start.ViewModels;
using VM.Start.ViewModels.Dock;

namespace VM.Start.Views.Dock
{
    /// <summary>
    /// DeviceStateView.xaml 的交互逻辑
    /// </summary>
    public partial class DeviceStateView : UserControl
    {
        #region Singleton
        private static readonly DeviceStateView _instance = new DeviceStateView();

        public DeviceStateView()
        {
            InitializeComponent();
            this.DataContext = DeviceStateViewModel.Ins;
            EventMgrLib.EventMgr.Ins
                .GetEvent<HardwareChangedEvent>()
                .Subscribe(OnCommunicationChanged);
        }

        public static DeviceStateView Ins
        {
            get { return _instance; }
        }
        #endregion

        #region Method
        private void OnCommunicationChanged()
        {
            Common.CommonMethods.UIAsync(() =>
            {
                wrapPanel.Children.Clear();
                foreach (var item in CameraSetViewModel.Ins.CameraModels)
                {
                    Chip chip = new Chip();
                    chip.Icon = "\ue66e";
                    chip.Content = item.CameraNo;
                    chip.IconBackground = item.Connected == true ? Brushes.Lime : Brushes.Red;
                    chip.ToolTip =
                        $"名称:{item.CameraNo}\r\n设备连接ID:{item.SerialNo}\r\n相机类型:{item.CameraType}\r\n备注:{item.Remarks}";
                    chip.MouseDoubleClick += Camera_MouseDoubleClick;
                    wrapPanel.Children.Add(chip);
                }
                foreach (var item in HardwareConfigViewModel.Ins.MotionModels)
                {
                    Chip chip = new Chip();
                    chip.Icon = "\ue640";
                    chip.Content = item.Remarks;
                    chip.IconBackground = item.Connected == true ? Brushes.Lime : Brushes.Red;
                    chip.ToolTip = item.Remarks + item.MotionNo;
                    chip.MouseDoubleClick += Motion_MouseDoubleClick;
                    wrapPanel.Children.Add(chip);
                }

                var comList = EComManageer.GetEcomList();
                foreach (var item in comList)
                {
                    Chip chip = new Chip();
                    chip.Content = item.Key;
                    chip.IconBackground = item.IsConnected == true ? Brushes.Lime : Brushes.Red;
                    chip.ToolTip = item.GetInfoStr();
                    chip.MouseDoubleClick += Communication_MouseDoubleClick;
                    wrapPanel.Children.Add(chip);
                }
                foreach (var item in LaserSetViewModel.Ins.LaserModels)
                {
                    Chip chip = new Chip();
                    chip.Icon = "\ue640";
                    chip.Content = item.LaserName;
                    chip.IconBackground = item.IsInit == true ? Brushes.Lime : Brushes.Red;
                    chip.ToolTip = item.LaserName + item.LaserIndex;
                    chip.MouseDoubleClick += Laser_MouseDoubleClick;
                    wrapPanel.Children.Add(chip);
                }
            });
        }

        private void Laser_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            LaserSetView.Ins.ShowDialog();
        }

        private void Camera_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            CameraSetView.Ins.ShowDialog();
        }

        private void Motion_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            HardwareConfigView.Ins.ShowDialog();
        }

        private void Communication_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            CommunicationSetView.Ins.ShowDialog();
        }
        #endregion
    }
}
