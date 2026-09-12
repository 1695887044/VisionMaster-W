using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using EventMgrLib;
using VM.Start.Common.Const;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.PersistentData;

namespace VM.Start.Dialogs.Views
{
    /// <summary>
    /// LoginView.xaml 的交互逻辑
    /// </summary>
    public partial class LoginView : Window
    {
        public LoginView()
        {
            InitializeComponent();
            UserList = GetUsers();
            cobUserName.ItemsSource = UserList;
            cobUserName.DisplayMemberPath = "UserName";//设置下拉框的显示文本
            cobUserName.SelectedValuePath = "UserName";//设置下拉框显示文本对应的value
            cobUserName.SelectedIndex = 0;
            LoadIcon();
        }
        #region Singleton
        private static readonly LoginView _instance = new LoginView();
        public static LoginView Ins
        {
            get { return _instance; }
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;  // cancels the window close
            this.Hide();      // Programmatically hides the window
        }
        #endregion
        #region Prop
        public static UserModel CurrentUser = null;
        public static List<UserModel> UserList;
        public static bool LoginFlag = false;
        #endregion
        /// <summary>
        /// 用户登陆
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                //【1】数据验证
                if (cobUserName.SelectedIndex == -1)
                {
                    MessageView.Ins.MessageBoxShow("请选则用户！", eMsgType.Warn);
                    return;
                }

                if (txtLoginPwd.Password.Length == 0)
                {
                    MessageView.Ins.MessageBoxShow("请输入登陆密码！", eMsgType.Warn);
                    return;
                }
                //【2】封装的是登陆账号和密码
                string userName = cobUserName.SelectedValue.ToString();
                UserModel loginUser = new UserModel()
                {
                    UserName = userName,
                    UserPwd = MD5Provider.GetMD5String(txtLoginPwd.Password),

                };
                //【3】判断登陆信息是否正确
                CurrentUser = CheckUser(loginUser);
                if (CurrentUser != null)
                {
                    LoginFlag = true;
                    EventMgr.Ins.GetEvent<CurrentUserChangedEvent>().Publish(CurrentUser);
                    this.DialogResult = true;
                    //关闭窗体
                    this.Close();
                }
                else
                {
                    MessageView.Ins.MessageBoxShow("用户名或密码错误！", eMsgType.Warn);
                }
            }
            catch (Exception ex)
            {
                Logger.GetExceptionMsg(ex);
            }

        }
        /// <summary>
        /// 用户注销
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnLogout_Click(object sender, RoutedEventArgs e)
        {
            CurrentUser = null;
            EventMgr.Ins.GetEvent<CurrentUserChangedEvent>().Publish(CurrentUser);
            this.Close();
        }
        /// <summary>
        /// 关闭窗体
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnExit_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
        /// <summary>
        /// 修改密码
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void labChangePwd_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (CurrentUser != null)
            {
                new ChangePwdView().ShowDialog();
            }
            else
            {
                MessageView.Ins.MessageBoxShow("请先登录账户！", eMsgType.Warn);
            }
        }

        #region Method

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

        private UserModel CheckUser(UserModel checkUser)
        {
            UserModel user = new UserModel();
            foreach (var item in UserList)
            {
                if (item.UserName == checkUser.UserName)
                {
                    if (item.UserPwd == checkUser.UserPwd)
                    {
                        user = item;
                        return user;
                    }
                    else
                    {
                        user = null;
                        return user;
                    }
                }
            }

            return user;

        }
        private List<UserModel> GetUsers()
        {
            List<UserModel> users = new List<UserModel>();
            XmlDocument xdoc = new XmlDocument();
            xdoc.Load(FilePaths.UserConfig);
            foreach (XmlNode nooderoot in xdoc.ChildNodes)
            {
                if (nooderoot.Name == "Root")
                {
                    foreach (XmlNode noodchild in nooderoot.ChildNodes)
                    {
                        if (noodchild.Name == "User")
                        {
                            UserModel objVar = new UserModel();
                            objVar.UserId = XMLAttributeGetValue(noodchild, "UserId");
                            string userName = XMLAttributeGetValue(noodchild, "UserName");
                            if (SystemConfig.Ins.CurrentCultureName == LanguageNames.Chinese)
                            {
                                objVar.UserName = userName;
                            }
                            else
                            {
                                switch (userName)
                                {
                                    case UserType.Developer://
                                        objVar.UserName = UserType.Developer_en;
                                        break;
                                    case UserType.Administrator://
                                        objVar.UserName = UserType.Administrator_en;
                                        break;
                                    case UserType.Operator://
                                        objVar.UserName = UserType.Operator_en;
                                        break;
                                    default:
                                        break;
                                }
                            }
                            objVar.UserPwd = XMLAttributeGetValue(noodchild, "UserPwd");
                            users.Add(objVar);
                        }
                    }
                }
            }
            return users;

        }
        public string XMLAttributeGetValue(XmlNode rootxml, string name)
        {
            string resvalue = string.Empty;
            if (rootxml != null && rootxml.Attributes != null && rootxml.Attributes[name] != null)
            {
                resvalue = rootxml.Attributes[name].Value;
            }
            return resvalue;
        }
        #endregion

        #region 改善用户体验
        private void cobUserName_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this.txtLoginPwd.Focus();//将当前焦点跳转到密码框
        }

        private void txtLoginPwd_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                btnLogin_Click(null, null);
            }
        }
        private void labChangePwd_MouseEnter(object sender, MouseEventArgs e)
        {
            this.labChangePwd.Foreground = Brushes.Red;
        }

        private void labChangePwd_MouseLeave(object sender, MouseEventArgs e)
        {
            this.labChangePwd.Foreground = Brushes.White;
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
