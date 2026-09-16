using DMSkin.Socket;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
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
using System.Windows.Threading;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Provide;
using VM.Start.Models;
using VM.Start.ViewModels.Dock;

namespace VM.Start.Views.Dock
{
    /// <summary>
    /// LogView.xaml 的交互逻辑
    /// </summary>
    public partial class LogView : UserControl
    {
        #region Singleton
        private static readonly LogView _instance = new LogView();
        private LogView()
        {
            InitializeComponent();
            this.DataContext = LogViewModel.Ins;
            DispatcherTimer dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Interval = TimeSpan.FromMilliseconds(100);
            dispatcherTimer.Tick += UpdateUI;
            dispatcherTimer.Start();
        }
        public static LogView Ins
        {
            get { return _instance; }
        }
        #endregion
        #region Prop
        private const int MaxCountDisplayLogCollection = 1999;
        private const int MaxCountAllLogCollection = 1999;
        private const int MaxCountInfoCollection = 1999;
        private const int MaxCountWarnCollection = 1999;
        private const int MaxCountErrorCollection = 1999;
        private const int MaxCountAlarmCollection = 1999;
        #endregion
        #region Method
        private void UpdateUI(object sender, EventArgs e)
        {
            while (Logger.LogInfos.Count > 0)
            {
                LogModel logModel = new LogModel();
                if (Logger.LogInfos.TryDequeue(out logModel))
                {
                    if (LogViewModel.Ins.DisplayLogCollection.Count >= MaxCountDisplayLogCollection)
                    {
                        LogViewModel.Ins.DisplayLogCollection.RemoveAt(0);
                    }
                    if (LogViewModel.Ins.AllLogCollection.Count >= MaxCountAllLogCollection)
                    {
                        LogViewModel.Ins.AllLogCollection.RemoveAt(0);
                    }
                    if (LogViewModel.Ins.InfoCollection.Count >= MaxCountInfoCollection)
                    {
                        LogViewModel.Ins.InfoCollection.RemoveAt(0);
                    }
                    if (LogViewModel.Ins.WarnCollection.Count >= MaxCountWarnCollection)
                    {
                        LogViewModel.Ins.WarnCollection.RemoveAt(0);
                    }
                    if (LogViewModel.Ins.ErrorCollection.Count >= MaxCountErrorCollection)
                    {
                        LogViewModel.Ins.ErrorCollection.RemoveAt(0);
                    }
                    if (LogViewModel.Ins.AlarmCollection.Count >= MaxCountAlarmCollection)
                    {
                        LogViewModel.Ins.AlarmCollection.RemoveAt(0);
                    }
                    switch (logModel.LogType)
                    {
                        case eMsgType.Success:
                        case eMsgType.Info:
                            logModel.LogColor = Brushes.Lime;
                            LogViewModel.Ins.InfoCollection.Add(logModel);
                            if (LogViewModel.Ins.InfoFilter)
                            {
                                LogViewModel.Ins.DisplayLogCollection.Add(logModel);
                            }
                            break;
                        case eMsgType.Warn:
                            logModel.LogColor = Brushes.Yellow;
                            LogViewModel.Ins.WarnCollection.Add(logModel);
                            if (LogViewModel.Ins.WarnFilter)
                            {
                                LogViewModel.Ins.DisplayLogCollection.Add(logModel);
                            }
                            break;
                        case eMsgType.Error:
                            logModel.LogColor = Brushes.Red;
                            LogViewModel.Ins.ErrorCollection.Add(logModel);
                            if (LogViewModel.Ins.ErrorFilter)
                            {
                                LogViewModel.Ins.DisplayLogCollection.Add(logModel);
                            }
                            break;
                        case eMsgType.Alarm:
                            logModel.LogColor = Brushes.Red;
                            LogViewModel.Ins.AlarmCollection.Add(logModel);
                            if (LogViewModel.Ins.AlarmFilter)
                            {
                                LogViewModel.Ins.DisplayLogCollection.Add(logModel);
                            }
                            CommonMethods.Mach.AlmFlag = true;
                            break;
                    }
                    if (!LogViewModel.Ins.InfoFilter && !LogViewModel.Ins.WarnFilter && !LogViewModel.Ins.ErrorFilter && !LogViewModel.Ins.AlarmFilter)
                    {
                        LogViewModel.Ins.DisplayLogCollection.Add(logModel);
                    }
                    LogViewModel.Ins.AllLogCollection.Add(logModel);
                    LogViewModel.Ins.InfoCount = LogViewModel.Ins.InfoCollection.Count;
                    LogViewModel.Ins.WarnCount = LogViewModel.Ins.WarnCollection.Count;
                    LogViewModel.Ins.ErrorCount = LogViewModel.Ins.ErrorCollection.Count;
                    LogViewModel.Ins.AlarmCount = LogViewModel.Ins.AlarmCollection.Count;
                }
                if (Logger.LogInfos.Count == 0)
                {
                    ScrollIntoEnd();
                }
            }
        }
        private void btnScrollIntoTop_Click(object sender, RoutedEventArgs e)
        {
            if (dg.Items.Count > 0)
            {
                dg.ScrollIntoView(dg.Items[0]);
            }
        }

        private void btnScrollIntoEnd_Click(object sender, RoutedEventArgs e)
        {
            if (dg.Items.Count > 0)
            {
                dg.ScrollIntoView(dg.Items[dg.Items.Count - 1]);
            }
        }
        private void ScrollIntoEnd()
        {
            if (dg.Items.Count > 0)
            {
                dg.ScrollIntoView(dg.Items[dg.Items.Count - 1]);
            }
        }

        private void ClearAlarm(object sender, RoutedEventArgs e)
        {
            LogViewModel.Ins.AlarmCollection.Clear();
            LogViewModel.Ins.AlarmCount = LogViewModel.Ins.AlarmCollection.Count;
            CommonMethods.Mach.AlmFlag = false;
        }

        #endregion

    }
}
