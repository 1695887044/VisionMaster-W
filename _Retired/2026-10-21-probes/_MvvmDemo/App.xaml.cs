using System.Diagnostics;
using System.Windows;
using VM.Charts;

namespace _MvvmDemo
{
    /// <summary>
    /// 应用生命周期:只做进程级职责(诊断接入、异常兜底、退出清理)。
    /// 界面逻辑一律在 ViewModel / XAML 绑定里,不在代码后台。
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 图表库诊断接入(慢帧/渲染异常/菜单自检);真实工程改接 NLog / Logger
            ChartView.DiagnosticsLog = msg => Debug.WriteLine("[Chart] " + msg);

            // 兜底:界面异常不崩窗口(示例做法;真实工程可换成日志+上报)
            DispatcherUnhandledException += (s, args) =>
            {
                Debug.WriteLine("未处理异常: " + args.Exception);
                args.Handled = true;
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 进程级清理:通知 ViewModel 停止采集线程
            (MainWindow?.DataContext as ViewModels.MainViewModel)?.StopCollection();
            base.OnExit(e);
        }
    }
}
