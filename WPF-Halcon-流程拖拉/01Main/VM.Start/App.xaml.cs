using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using VM.Start.Common.Helper;
using VM.Start.Common;
using VM.Start.Dialogs.Views;
using VM.Start.PersistentData;
using VM.Start.Views;
using WPFLocalizeExtension.Engine;
using VM.Start.Common.Provide;
using AvalonDock.Themes.VS2013.Themes;
using VM.Start.Services;

namespace VM.Start
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        public static SplashScreen splashScreen;

        protected override void OnStartup(StartupEventArgs e)
        {
            //base.OnStartup(e);
            //SplashScreenWindow splashScreenWindow = new RoutedCommand SplashScreenWindow();
            //splashScreenWindow.Show();
            //return;
            try
            {
                Process currentProcess = Process.GetCurrentProcess();
                Process[] myProcess = Process.GetProcessesByName(currentProcess.ProcessName);
               
                if (myProcess.Length <= 1 || true)
                {
                    //【1】全局异常捕获
                    this.DispatcherUnhandledException += App_DispatcherUnhandledException;
                    Application.Current.DispatcherUnhandledException +=
                        Current_DispatcherUnhandledException;
                    AppDomain.CurrentDomain.UnhandledException +=
                        new UnhandledExceptionEventHandler(CurrentDoShell_UnhandledException);
                    this.Dispatcher.UnhandledException += Dispatcher_UnhandledException;
                    TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
                    //【2】SplashScreen
                    splashScreen = new SplashScreen(@"/Assets/Images/SplashScreen.png");
                    splashScreen.Show(false);
                    //【3】加载系统配置文件
                    SystemConfig.Ins.LoadSystemConfig();
                    //【3】初始化语言
                    InitializeCulture();
                    //【5】加载插件
                    PluginService.InitPlugin();
                    //【7】软件授权
                    string str = MD5Provider.GetMD5String("000A0653E6BCA4B1");
                    str = DesEncrypt.Encrypt("000A0653E6BCA4B10040");
#if !DEBUG
                    if (!EncryptionView.Ins.ConfirmLicense())
                    {
                        bool? dialogResult = EncryptionView.Ins.ShowDialog();
                        if (EncryptionView.RegisterSuccess != true)
                        {
                            CommonMethods.Exit();
                        }
                    }
                    else
#endif
                    {
                        EncryptionView.Ins.ProbationTime();
                    }
                    //【8】定期清理内存
                    //ClearMemoryHelper.ClearMemory();
                    //【19】用户登陆
                    base.OnStartup(e);
                    _ = MainView.Ins;
                    if (CommonMethods.UserLogin())
                    {
                        //【10】MainView启动
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MainView.Ins.Show();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.GetExceptionMsg(ex);
            }
        }

        /// <summary>
        /// 初始化语言
        /// </summary>
        private void InitializeCulture()
        {
            // 切换语言
            CultureInfo cultureInfo = new CultureInfo(SystemConfig.Ins.CurrentCultureName);
            LocalizeDictionary.Instance.Culture = cultureInfo;
        }

        /// <summary>
        /// 显示未捕获的App异常
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void App_DispatcherUnhandledException(
            object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e
        )
        {
            try
            {
                if (e != null && e.Exception != null)
                {
                    Logger.GetExceptionMsg(e.Exception);
                }
                e.Handled = true;
            }
            catch { }
        }

        /// <summary>
        /// 显示未捕获的Current异常
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Current_DispatcherUnhandledException(
            object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e
        )
        {
            try
            {
                if (e != null && e.Exception != null)
                {
                    Logger.GetExceptionMsg(e.Exception);
                }
                e.Handled = true;
            }
            catch { }
        }

        /// <summary>
        /// 显示未捕获的CurrentDoShell异常
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void CurrentDoShell_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                //记录dump文件
                MiniDump.TryDump($"dumps\\VM_{DateTime.Now.ToString("HH-mm-ss-ms")}.dmp");
                Exception ex = null;
                if (e != null)
                    ex = e.ExceptionObject as Exception;
                if (ex != null)
                {
                    Logger.GetExceptionMsg(ex);
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 显示未捕获的异常
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Dispatcher_UnhandledException(
            object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e
        )
        {
            try
            {
                Exception ex = null;
                if (e != null)
                    ex = e.Exception;

                if (ex != null)
                {
                    Logger.GetExceptionMsg(ex);
                }
                e.Handled = true;
            }
            catch { }
        }

        /// <summary>
        /// Task线程内未捕获异常处理事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void TaskScheduler_UnobservedTaskException(
            object sender,
            UnobservedTaskExceptionEventArgs e
        )
        {
            try
            {
                if (e != null && e.Exception != null)
                {
                    Logger.GetExceptionMsg(e.Exception);
                }
            }
            catch { }
        }
    }
}
