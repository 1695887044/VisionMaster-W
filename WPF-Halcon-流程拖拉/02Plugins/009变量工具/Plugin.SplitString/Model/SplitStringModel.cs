using EventMgrLib;
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


namespace Plugin.SplitString.Model
{
    [Serializable]
    public class SplitStringModel : NotifyPropertyBase
    {
        private string _DataType;
        /// <summary>
        /// 数据类型
        /// </summary>
        public string DataType
        {
            get { return _DataType; }
            set { Set(ref _DataType, value); }
        }
        private string _DataName;
        /// <summary>
        /// 数据名称
        /// </summary>
        public string DataName
        {
            get { return _DataName; }
            set { Set(ref _DataName, value); }
        }
        private string _DataValue;
        /// <summary>
        /// 数据值
        /// </summary>
        public string DataValue
        {
            get { return _DataValue; }
            set { Set(ref _DataValue, value); }
        }

        private string _Prefix;
        /// <summary>
        /// 前缀
        /// </summary>
        public string Prefix
        {
            get { return _Prefix; }
            set { Set(ref _Prefix, value); }
        }
        private string _Suffix;
        /// <summary>
        /// 后缀
        /// </summary>
        public string Suffix
        {
            get { return _Suffix; }
            set { Set(ref _Suffix, value); }
        }
    }
}
