using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VM.Start.Models
{
    public class IoInfoModel
    {
        #region Prop
        private string[] _InputsInfo = new string[16];

        public string[] InputsInfo
        {
            get { return _InputsInfo; }
            set { _InputsInfo = value;  }
        }
        private string[] _OutputsInfo = new string[16];

        public string[] OutputsInfo
        {
            get { return _OutputsInfo; }
            set { _OutputsInfo = value;}
        }

        #endregion

    }
}
