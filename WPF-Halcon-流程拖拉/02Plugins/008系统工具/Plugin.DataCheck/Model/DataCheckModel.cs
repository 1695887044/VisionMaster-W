using EventMgrLib;
using Plugin.DataCheck.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using VM.Start.Common;
using VM.Start.Common.Helper;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.ViewModels;
using VM.Start.Views;


namespace Plugin.DataCheck.Model
{
    [Serializable]
    public class DataCheckModel: NotifyPropertyBase
    {
        private int _ID;
        /// <summary>
        /// 索引
        /// </summary>
        public int ID
        {
            get { return _ID; }
            set { Set(ref _ID, value); }
        }
        private bool _IsCheck=true;
        /// <summary>
        /// 是否检查
        /// </summary>
        public bool IsCheck
        {
            get { return _IsCheck; }
            set { Set(ref _IsCheck, value); }
        }
        private string _DataLinkText;
        /// <summary>
        /// 数据链接
        /// </summary>
        public string DataLinkText
        {
            get { return _DataLinkText; }
            set { Set(ref _DataLinkText, value); }
        }
        private string _DataLinkValue;
        /// <summary>
        /// 数据链接值
        /// </summary>
        public string DataLinkValue
        {
            get { return _DataLinkValue; }
            set { Set(ref _DataLinkValue, value); }
        }
        private bool _State;
        /// <summary>
        /// 状态
        /// </summary>
        public bool State
        {
            get { return _State; }
            set { Set(ref _State, value); }
        }
        private string _lowerLimit="-999999";
        /// <summary>
        /// 下限位
        /// </summary>
        public string lowerLimit
        {
            get { return _lowerLimit; }
            set { Set(ref _lowerLimit, value); }
        }
        private string _upperLimit="99999";
        /// <summary>
        /// 上限位
        /// </summary>
        public string upperLimit
        {
            get { return _upperLimit; }
            set { Set(ref _upperLimit, value); }
        }

        private string _lowerDeviation="0";
        /// <summary>
        /// 下公差
        /// </summary>
        public string lowerDeviation
        {
            get { return _lowerDeviation; }
            set { Set(ref _lowerDeviation, value); }
        }
        private string _upperDeviation="0";
        /// <summary>
        /// 上公差
        /// </summary>
        public string upperDeviation
        {
            get { return _upperDeviation; }
            set { Set(ref _upperDeviation, value); }
        }
        private string _DataType;
        /// <summary>
        /// 数据类型
        /// </summary>
        public string DataType
        {
            get { return _DataType; }
            set { Set(ref _DataType, value); }
        }

    }
}
