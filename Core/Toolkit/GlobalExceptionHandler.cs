using Microsoft.VisualBasic.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace VisionMaster.Core
{
    /// <summary>
    /// 全局异常捕获器（捕获所有线程的未处理异常，生成崩溃报告，自动重启）
    /// </summary>
    public static class GlobalExceptionHandler
    {
        /// <summary>
        /// 异常发生事件
        /// </summary>
        public static event EventHandler<UnhandledExceptionEventArgs> ExceptionOccurred;

        /// <summary>
        /// 注册全局异常处理
        /// </summary>
        /// <param name="appName">应用程序名称</param>
        /// <param name="crashReportPath">崩溃报告保存路径</param>
        /// <param name="autoRestart">是否自动重启程序</param>
        public static void Register(string appName, string crashReportPath = "crash_reports", bool autoRestart = true)
        {
            // WPF UI线程异常
            Application.Current.DispatcherUnhandledException += (s, e) =>
            {
                e.Handled = true;
                HandleException(e.Exception, "UI线程", appName, crashReportPath, autoRestart);
            };

            // 非UI线程异常
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                HandleException((Exception)e.ExceptionObject, "非UI线程", appName, crashReportPath, autoRestart);
            };

            // Task线程异常
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                e.SetObserved();
                HandleException(e.Exception, "Task线程", appName, crashReportPath, autoRestart);
            };

           // Log.Info($"全局异常处理器已注册: {appName}");
        }

        private static void HandleException(Exception ex, string threadType, string appName, string crashReportPath, bool autoRestart)
        {
            try
            {
                //Log.Fatal($"未处理异常 [{threadType}]: {ex.Message}", ex);
                ExceptionOccurred?.Invoke(null, new UnhandledExceptionEventArgs(ex, false));

                // 生成崩溃报告
                var reportPath = GenerateCrashReport(ex, threadType, appName, crashReportPath);

                // 显示错误提示
                MessageBox.Show(
                    $"程序发生严重错误，即将退出。\n崩溃报告已保存到:\n{reportPath}\n\n请联系技术支持。",
                    "严重错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                // 自动重启
                if (autoRestart)
                {
                    Process.Start(Process.GetCurrentProcess().MainModule.FileName);
                }

                // 退出程序
                Environment.Exit(1);
            }
            catch
            {
                // 异常处理本身不能再抛出异常
                Environment.Exit(1);
            }
        }

        private static string GenerateCrashReport(Exception ex, string threadType, string appName, string crashReportPath)
        {
            if (!Directory.Exists(crashReportPath))
            {
                Directory.CreateDirectory(crashReportPath);
            }

            var fileName = $"{appName}_crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var filePath = Path.Combine(crashReportPath, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("=====================================");
            sb.AppendLine($"应用程序: {appName}");
            sb.AppendLine($"崩溃时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"线程类型: {threadType}");
            sb.AppendLine($"操作系统: {Environment.OSVersion}");
            sb.AppendLine($"运行时版本: {Environment.Version}");
            sb.AppendLine($"进程ID: {Process.GetCurrentProcess().Id}");
            sb.AppendLine($"内存使用: {MemoryManager.Instance.GetPrivateMemoryMB()} MB");
            sb.AppendLine("=====================================");
            sb.AppendLine();
            sb.AppendLine("异常信息:");
            sb.AppendLine(ex.ToString());
            sb.AppendLine();
            sb.AppendLine("=====================================");
            sb.AppendLine("最近100条日志:");
           // sb.AppendLine(Log.GetRecentLogs(100));

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            return filePath;
        }
    }
}
