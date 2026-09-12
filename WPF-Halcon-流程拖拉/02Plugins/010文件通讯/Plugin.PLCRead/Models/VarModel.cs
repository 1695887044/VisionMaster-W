using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;

namespace Plugin.PLCRead.Models
{
    [Serializable]
    public class ReadVarModel:NotifyPropertyBase
    {
		private string _Addr;

		public string Addr
		{
			get { return _Addr; }
			set { _Addr = value; RaisePropertyChanged(); }
		}
		private eDataType _DataType;

		public eDataType DataType
        {
			get { return _DataType; }
			set { _DataType = value; RaisePropertyChanged(); }
		}
        private string _Name;

        public string Name
        {
            get { return _Name; }
            set { _Name = value; RaisePropertyChanged(); }
        }
        private string _Value;

        public string Value
        {
            get { return _Value; }
            set { _Value = value; RaisePropertyChanged(); }
        }
        private string _Remarks;

        public string Remarks
        {
            get { return _Remarks; }
            set { _Remarks = value; RaisePropertyChanged(); }
        }
    }
}
