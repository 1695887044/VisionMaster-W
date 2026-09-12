using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VM.Start.PersistentData;
using System.Xml;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Models;

namespace VM.Start.Dialogs.Views
{
    /// <summary>
    /// ChangePwdView.xaml 的交互逻辑
    /// </summary>
    public partial class ChangePwdView : Window
    {
        public ChangePwdView()
        {
            InitializeComponent();
            LoadIcon();
        }

        private void window_Loaded(object sender, RoutedEventArgs e)
        {
            txtOldPwd.Focus();
        }
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

        private void btnConfirm_Click(object sender, RoutedEventArgs e)
        {
            //【1】数据验证
            if (this.txtOldPwd.Password.Length == 0)
            {
                MessageView.Ins.MessageBoxShow("请输入旧密码！", eMsgType.Info);
                this.txtOldPwd.Focus();
                return;
            }
            if (MD5Provider.GetMD5String(txtOldPwd.Password) != LoginView.CurrentUser.UserPwd)
            {
                MessageView.Ins.MessageBoxShow("旧密码输入错误！", eMsgType.Info);
                this.txtOldPwd.Focus();
                return;
            }
            if (this.txtNewCommandPwd.Password.Length == 0)
            {
                MessageView.Ins.MessageBoxShow("请输入新密码！", eMsgType.Info);
                this.txtNewCommandPwd.Focus();
                return;
            }
            if (this.txtNewCommandPwdConfirm.Password.Length == 0)
            {
                MessageView.Ins.MessageBoxShow("请输入新密码确认！", eMsgType.Info);
                this.txtNewCommandPwdConfirm.Focus();
                return;
            }
            if (this.txtNewCommandPwdConfirm.Password != this.txtNewCommandPwd.Password)
            {
                MessageView.Ins.MessageBoxShow("两次输入的新密码不一致，请检查！", eMsgType.Info);
                this.txtNewCommandPwdConfirm.Focus();
                return;
            }
            MessageView dlgMessageBox = MessageView.Ins;
            dlgMessageBox.MessageBoxShow("确实要修改密码吗？", eMsgType.Warn, MessageBoxButton.OKCancel);
            if (dlgMessageBox.DialogResult == false)
            {
                return;
            }
            //【2】和后台交互，将新密码写入xml

            try
            {
                if (LoginView.CurrentUser != null)
                {
                    LoginView.CurrentUser.UserPwd = MD5Provider.GetMD5String(this.txtNewCommandPwdConfirm.Password);
                    if (ChangePwdView.ChangePwd(LoginView.CurrentUser))
                    {
                        //设置登陆窗体的返回值
                        this.DialogResult = Convert.ToBoolean(1);
                        MessageView.Ins.MessageBoxShow("密码修改成功！", eMsgType.Info);
                        //关闭窗体
                        this.Close();

                    }
                    else
                    {
                        MessageView.Ins.MessageBoxShow("密码修改失败！", eMsgType.Warn);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageView.Ins.MessageBoxShow("密码修改失败！具体原因：" + ex.Message, eMsgType.Error);
            }




        }
        //退出
        private void btnExit_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #region Method

        public static bool ChangePwd(UserModel objAdmin)
        {
            if (!File.Exists(FilePaths.UserConfig))
            {
                File.Create(FilePaths.UserConfig).Close();
            }
            else
            {
                LoginView.UserList = new List<UserModel>();

                XmlDocument xdoc = new XmlDocument();
                xdoc.Load(FilePaths.UserConfig);
                foreach (XmlNode nooderoot in xdoc.ChildNodes)
                {
                    if (nooderoot.Name == "Root")
                    {
                        foreach (XmlNode noodchild in nooderoot.ChildNodes)
                        {
                            if (noodchild.Name == "User" && noodchild.Attributes["UserName"].Value == objAdmin.UserName)
                            {

                                noodchild.Attributes["UserPwd"].Value = objAdmin.UserPwd;
                                if (File.Exists(FilePaths.UserConfig))
                                {
                                    File.Delete(FilePaths.UserConfig);
                                }
                                xdoc.Save(FilePaths.UserConfig);
                            }
                        }

                    }

                }

            }
            return true;
        }


        #endregion
        #region 改善用户体验
        private void txtOldPwd_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) txtNewCommandPwd.Focus();
        }

        private void txtNewCommandPwd_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) txtNewCommandPwdConfirm.Focus();
        }
        private void txtNewCommandPwdConfirm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) btnConfirm_Click(null, null);
        }

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


    }
}
