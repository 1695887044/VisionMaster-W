using EventMgrLib;
using Plugin.CreateString.ViewModels;
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


namespace Plugin.CreateString.Model
{
    [Serializable]
    public class CreateStringModel : NotifyPropertyBase
    {
        private int _ID;
        /// <summary>
        /// 索引
        /// </summary>
        public int ID
        {
            get { return _ID; }
            set { Set(ref _ID, value);
                IDString = "Value" + ID.ToString();
            }
        }
        private string _IDString;
        /// <summary>
        /// 名称
        /// </summary>
        public string IDString
        {
            get { return _IDString; }
            set { Set(ref _IDString, value); }
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
