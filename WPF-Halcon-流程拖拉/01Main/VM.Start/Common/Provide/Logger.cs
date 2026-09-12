using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Windows.Media;
using HandyControl.Controls;
using NLog;
using VM.Start.Common.Enums;
using VM.Start.Dialogs.Views;
using VM.Start.Models;
using VM.Start.Services;
namespace VM.Start.Common.Provide
{
    public class Logger
    {
        #region Prop
        public static ConcurrentQueue<LogModel> LogInfos = new ConcurrentQueue<LogModel>();
        private static NLog.Logger InfoLogger = LogManager.GetLogger("infofile");
        private static NLog.Logger ErrorLogger = LogManager.GetLogger("errorfile");
        #endregion

        #region Method
        /// <summary>
        /// 添加日志
        /// </summary>
        /// <param name="message"></param>
        /// <param name="msgType"></param>
        public static void AddLog(
            string message,
            eMsgType msgType = eMsgType.Info,
            string AlmNote = "",
            bool isDispGrowl = false
        )
        {
            if (string.IsNullOrEmpty(message))
                return;
            switch (msgType)
            {
                case eMsgType.Success:
                    InfoLogger.Info(message);
                    if (isDispGrowl)
                    {
                        Growl.Success(message);
                    }
                    if (Solution.Ins.QuickMode == true)
                        return;
                    AddDisplayLog(message, msgType);
                    break;
                case eMsgType.Info:
                    InfoLogger.Info(message);
                    if (isDispGrowl)
                    {
                        Growl.Info(message);
                    }
                    if (Solution.Ins.QuickMode == true)
                        return;
                    AddDisplayLog(message, msgType);
                    break;
                case eMsgType.Warn:
                    InfoLogger.Warn(message);
                    if (isDispGrowl)
                    {
                        Growl.Warning(message);
                    }
                    if (Solution.Ins.QuickMode == true)
                        return;
                    AddDisplayLog(message, msgType);
                    break;
                case eMsgType.Error:
                    InfoLogger.Error(message);
                    if (isDispGrowl)
                    {
                        Growl.Error(message);
                    }
                    if (Solution.Ins.QuickMode == true)
                        return;
                    AddDisplayLog(message, msgType);
                    break;
                case eMsgType.Alarm:
                    InfoLogger.Error(message);
                    if (isDispGrowl)
                    {
                        Growl.Error(message);
                    }
                    if (Solution.Ins.QuickMode == true)
                        return;
                    AddDisplayLog(message, msgType);
                    break;
            }
        }

        private static void AddDisplayLog(string message, eMsgType msgType = eMsgType.Info)
        {
            LogModel logModel = new LogModel()
            {
                Content = message,
                LogColor = Brushes.Lime,
                CreateTime = DateTime.Now,
                LogType = msgType
            };
            LogInfos.Enqueue(logModel);
        }

        /// <summary>
        /// 生成自定义异常消息并打印日志和弹窗警告
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <param name="backStr">备用异常消息：当ex为null时有效</param>
        public static void GetExceptionMsg(
            Exception ex,
            string backStr = "",
            bool ShowDialog = true
        )
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("*************程序异常！请截图保存并反馈至软件工程师*************");
            sb.AppendLine("【出现时间】：" + DateTime.Now.ToString());
            if (ex != null)
            {
                sb.AppendLine("【异常类型】：" + ex.GetType().Name);
                sb.AppendLine("【异常信息】：" + ex.Message);
                sb.AppendLine("【堆栈调用】：" + ex.StackTrace);
                sb.AppendLine("【异常源  】：" + ex.Source);
                if (ex.GetType().Name == "Win32Exception")
                {
                    sb.AppendLine(
                        "***************************************************************"
                    );
                    ErrorLogger.Fatal(sb.ToString());
                    return; //排除掉Win32Exception的异常报错（传递给系统调用的数据区域太小）
                }
            }
            else
            {
                sb.AppendLine("【未处理异常】：" + backStr);
            }
            sb.AppendLine("****************************************************************");
            ErrorLogger.Fatal(sb.ToString());
            if (ShowDialog == true)
            {
                CommonMethods.UISync(() =>
                {
                    MessageView.Ins.MessageBoxShow(sb.ToString(), eMsgType.Error);
                });
            }
        }
        #endregion
    }
}
