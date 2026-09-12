using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VM.Start.Common;
using VM.Start.PersistentData;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Dialogs.ViewModels;

namespace VM.Start.Dialogs.Views
{
    /// <summary>
    /// EncryptionView.xaml 的交互逻辑
    /// </summary>
    public partial class EncryptionView : Window
    {
        #region Singleton
        private static readonly EncryptionView _instance = new EncryptionView();

        private EncryptionView()
        {
            InitializeComponent();
            LoadIcon();
            MachineCode = getCpu() + GetDiskVolumeSerialNumber();//获得24位Cpu和硬盘序列号
            tbMachineCode.Text = MachineCode + SystemConfig.Ins.ProbationNum.ToString("X2");
            License = MD5Provider.GetMD5String(MachineCode);
            ProbationLicense = DesEncrypt.Encrypt(MachineCode + SystemConfig.Ins.ProbationNum.ToString("X2"));
        }
        public static EncryptionView Ins
        {
            get { return _instance; }
        }

        #endregion

        #region Prop

        public string MachineCode;
        public static string License;
        public string ProbationLicense;
        private static int ProbationTimeLimit = 720;//一个月30*24=720
        public static bool RegisterSuccess = false;

        #endregion
        private void LoadIcon()
        {
            ImageBrush imageBrush = new ImageBrush();
            if (!File.Exists(SystemConfig.Ins.SoftwareIcoPath))
            {
                SystemConfig.Ins.SoftwareIcoPath = FilePaths.DefultSoftwareIcon;
            }
            imageBrush.ImageSource = new BitmapImage(new Uri(SystemConfig.Ins.SoftwareIcoPath, UriKind.Relative));
            bdImage.Background = imageBrush;
        }

        /// <summary>
        /// 试用期计时
        /// </summary>
        public void ProbationTime()
        {
            if (!(License == SystemConfig.Ins.License))
            {
                Task.Run(async () =>
                {
                    while (true)
                    {
                        try
                        {
                            //判断是否达到720h 即一个月30*24=720
                            if (SystemConfig.Ins.ProbationTime > ProbationTimeLimit)
                            {
                                SystemConfig.Ins.ProbationNum++;
                                SystemConfig.Ins.SaveSystemConfig();
                                MessageView.Ins.MessageBoxShow("软件已过试用期，请联系开发者！", eMsgType.Warn, MessageBoxButton.OKCancel);
                                CommonMethods.Exit();
                            }
                            else
                            {
                                SystemConfig.Ins.ProbationTime++;
                                SystemConfig.Ins.SaveSystemConfig();
                            }
                            //延时1h
                            await Task.Delay(3600000);
                        }
                        catch (Exception ex)
                        {
                            Logger.GetExceptionMsg(ex);
                        }

                    }
                });
            }
        }
        public bool ConfirmLicense()
        {
            try
            {
                if (License == SystemConfig.Ins.License)
                {
                    AboutViewModel.Ins.ActiveState = eActiveState.Actived;
                    return true;
                }
                else if (ProbationLicense == AnalysisProbationLicense(SystemConfig.Ins.ProbationLicense))
                {
                    AboutViewModel.Ins.ActiveState = eActiveState.Probation;
                    return true;
                }
                else
                {
                    AboutViewModel.Ins.ActiveState = eActiveState.NotActived;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.GetExceptionMsg(ex);
                return false;
            }
        }
        private string AnalysisProbationLicense(string probationLicense)
        {
            string str = "";
            try
            {
                if (probationLicense == null)
                {
                    str = probationLicense;
                    return str;
                }
                str = DesEncrypt.Decrypt(probationLicense);
                ProbationTimeLimit = int.Parse(str.Substring(str.Length - 2, 2), NumberStyles.HexNumber) * 24;
                str = str.Substring(0, str.Length - 2);
                str = DesEncrypt.Encrypt(str);
                return str;
            }
            catch (Exception)
            {
                return str;
            }
        }
        private void btnExit_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
        /// <summary>
        /// 获取CPU的参数
        /// </summary>
        /// <returns></returns>
        public string getCpu()
        {
            string strCpu = null;
            ManagementClass myCpu = new ManagementClass("win32_Processor");
            ManagementObjectCollection myCpuConnection = myCpu.GetInstances();
            foreach (ManagementObject myObject in myCpuConnection)
            {
                strCpu = myObject.Properties["Processorid"].Value.ToString();
                break;
            }
            return strCpu.Substring(strCpu.Length-8, 8);
        }
        /// <summary>
        /// 获取硬盘的参数
        /// </summary>
        /// <returns></returns>
        public string GetDiskVolumeSerialNumber()
        {
            ManagementClass mc = new ManagementClass("Win32_NetworkAdapterConfiguration");
            ManagementObject disk = new ManagementObject("win32_logicaldisk.deviceid=\"c:\"");
            disk.Get();
            return disk.GetPropertyValue("VolumeSerialNumber").ToString();
        }
        #region 改善用户体验


        #endregion

        #region 无边框窗体拖动
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            // 获取鼠标相对标题栏位置 
            Point position = e.GetPosition(this);
            // 如果鼠标位置在标题栏内，允许拖动 
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (position.X >= 0 && position.X < this.ActualWidth && position.Y >= 0 && position.Y < this.ActualHeight)
                {
                    this.DragMove();
                }
            }
        }

        #endregion

        private void btnRegister_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (tbRegistrationCode.Text != "")
                {
                    if (tbRegistrationCode.Text == License)
                    {
                        MessageView.Ins.MessageBoxShow("注册成功！");
                        SystemConfig.Ins.License = tbRegistrationCode.Text;
                        SystemConfig.Ins.SaveSystemConfig();
                        AboutViewModel.Ins.ActiveState = eActiveState.Actived;
                        RegisterSuccess = true;
                        this.DialogResult = true;
                    }
                    else if (AnalysisProbationLicense(tbRegistrationCode.Text) == ProbationLicense)
                    {
                        MessageView.Ins.MessageBoxShow("试用码注册成功！");
                        SystemConfig.Ins.ProbationLicense = tbRegistrationCode.Text;
                        SystemConfig.Ins.ProbationTime = 0;
                        SystemConfig.Ins.SaveSystemConfig();
                        AboutViewModel.Ins.ActiveState = eActiveState.Probation;
                        RegisterSuccess = true;
                        this.DialogResult = true;
                    }
                    else
                    {
                        MessageView.Ins.MessageBoxShow("注册失败！", eMsgType.Warn);
                    }
                }
                else
                {
                    MessageView.Ins.MessageBoxShow("注册码为空！", eMsgType.Warn);
                }
            }
            catch (Exception ex)
            {
                MessageView.Ins.MessageBoxShow("注册码错误，注册失败！", eMsgType.Warn);
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            this.Hide();
            e.Cancel = true;
            base.OnClosing(e);
        }

        private void window_Activated(object sender, EventArgs e)
        {
            tbRegistrationCode.Focus();
        }
    }
}
