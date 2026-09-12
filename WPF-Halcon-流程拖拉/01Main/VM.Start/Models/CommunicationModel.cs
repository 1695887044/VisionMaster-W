using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;

namespace VM.Start.Models
{
    [Serializable]
    public class CommunicationModel : NotifyPropertyBase
    {
        private int _ID;
        /// <summary>
        /// ID
        /// </summary>
        public int ID
        {
            get { return _ID; }
            set { Set(ref _ID, value); }
        }
        private bool _IsEnable;
        /// <summary>
        /// 使能
        /// </summary>
        public bool IsEnable
        {
            get { return _IsEnable; }
            set { Set(ref _IsEnable, value); }
        }
        private string _Name;
        /// <summary>
        /// 名称
        /// </summary>
        public string Name
        {
            get { return _Name; }
            set { Set(ref _Name, value); }
        }
        private string _Remarks;
        /// <summary>
        /// 备注
        /// </summary>
        public string Remarks
        {
            get { return _Remarks; }
            set { Set(ref _Remarks, value); }
        }

    }
}
