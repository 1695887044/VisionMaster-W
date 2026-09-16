using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;
using VM.Start.Models;

namespace VM.Start.ViewModels.Dock
{
    public class LogViewModel : NotifyPropertyBase
    {
        #region Singleton
        private static readonly LogViewModel _instance = new LogViewModel();

        private LogViewModel()
        {
        }
        public static LogViewModel Ins
        {
            get { return _instance; }
        }
        #endregion
        #region Prop
        private ObservableCollection<LogModel> _DisplayLogCollection = new ObservableCollection<LogModel>();
        /// <summary>
        /// 显示日志集合
        /// </summary>
        public ObservableCollection<LogModel> DisplayLogCollection
        {
            get { return _DisplayLogCollection; }
            set { Set(ref _DisplayLogCollection, value); }
        }
        private ObservableCollection<LogModel> _AllLogCollection = new ObservableCollection<LogModel>();
        /// <summary>
        /// 所有日志集合
        /// </summary>
        public ObservableCollection<LogModel> AllLogCollection
        {
            get { return _AllLogCollection; }
            set { Set(ref _AllLogCollection, value); }
        }
        private ObservableCollection<LogModel> _InfoCollection = new ObservableCollection<LogModel>();
        /// <summary>
        /// 消息日志集合
        /// </summary>
        public ObservableCollection<LogModel> InfoCollection
        {
            get { return _InfoCollection; }
            set { Set(ref _InfoCollection, value); }
        }

        private ObservableCollection<LogModel> _WarnCollection = new ObservableCollection<LogModel>();
        /// <summary>
        /// 警告日志集合
        /// </summary>
        public ObservableCollection<LogModel> WarnCollection
        {
            get { return _WarnCollection; }
            set { Set(ref _WarnCollection, value); }
        }
        private ObservableCollection<LogModel> _ErrorCollection = new ObservableCollection<LogModel>();
        /// <summary>
        /// 错误日志集合
        /// </summary>
        public ObservableCollection<LogModel> ErrorCollection
        {
            get { return _ErrorCollection; }
            set { Set(ref _ErrorCollection, value); }
        }

        private ObservableCollection<LogModel> _AlarmCollection = new ObservableCollection<LogModel>();
        /// <summary>
        /// 报警日志集合
        /// </summary>
        public ObservableCollection<LogModel> AlarmCollection
        {
            get { return _AlarmCollection; }
            set { Set(ref _AlarmCollection, value); }
        }
        private int _InfoCount;

        public int InfoCount
        {
            get { return _InfoCount; }
            set { Set(ref _InfoCount, value); }
        }

        private int _WarnCount;

        public int WarnCount
        {
            get { return _WarnCount; }
            set { Set(ref _WarnCount, value); }
        }
        private int _ErrorCount;

        public int ErrorCount
        {
            get { return _ErrorCount; }
            set { Set(ref _ErrorCount, value); }
        }

        private int _AlarmCount;

        public int AlarmCount
        {
            get { return _AlarmCount; }
            set { Set(ref _AlarmCount, value); }
        }
        private bool _InfoFilter;

        public bool InfoFilter
        {
            get { return _InfoFilter; }
            set
            {
                Set(ref _InfoFilter, value);
                if (_InfoFilter == true)
                {
                    WarnFilter = false;
                    ErrorFilter = false;
                    ErrorFilter = false;
                    DisplayLogCollection.Clear();
                    foreach (var item in InfoCollection)
                    {
                        DisplayLogCollection.Add(item);
                    }
                }
                else
                {
                    DisplayLogCollection.Clear();
                    foreach (var item in AllLogCollection)
                    {
                        DisplayLogCollection.Add(item);
                    }
                }
            }
        }
        private bool _WarnFilter;

        public bool WarnFilter
        {
            get { return _WarnFilter; }
            set
            {
                Set(ref _WarnFilter, value);
                if (_WarnFilter == true)
                {
                    InfoFilter = false;
                    ErrorFilter = false;
                    ErrorFilter = false;
                    DisplayLogCollection.Clear();
                    foreach (var item in WarnCollection)
                    {
                        DisplayLogCollection.Add(item);
                    }
                }
                else
                {
                    DisplayLogCollection.Clear();
                    foreach (var item in AllLogCollection)
                    {
                        DisplayLogCollection.Add(item);
                    }
                }
            }
        }
        private bool _ErrorFilter;

        public bool ErrorFilter
        {
            get { return _ErrorFilter; }
            set
            {
                Set(ref _ErrorFilter, value);
                if (_ErrorFilter == true)
                {
                    InfoFilter = false;
                    WarnFilter = false;
                    AlarmFilter = false;
                    DisplayLogCollection.Clear();
                    foreach (var item in ErrorCollection)
                    {
                        DisplayLogCollection.Add(item);
                    }
                }
                else
                {
                    DisplayLogCollection.Clear();
                    foreach (var item in AllLogCollection)
                    {
                        DisplayLogCollection.Add(item);
                    }
                }
            }
        }
        private bool _AlarmFilter;

        public bool AlarmFilter
        {
            get { return _AlarmFilter; }
            set
            {
                Set(ref _AlarmFilter, value);
                if (_AlarmFilter == true)
                {
                    InfoFilter = false;
                    WarnFilter = false;
                    ErrorFilter = false;
                    DisplayLogCollection.Clear();
                    foreach (var item in AlarmCollection)
                    {
                        DisplayLogCollection.Add(item);
                    }
                }
                else
                {
                    DisplayLogCollection.Clear();
                    foreach (var item in AllLogCollection)
                    {
                        DisplayLogCollection.Add(item);
                    }
                }
            }
        }

        #endregion
    }
}
