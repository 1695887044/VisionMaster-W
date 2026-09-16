using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Halcon.Helper;
using VM.Start.Common.Enums;

namespace VM.Start.Models
{
    [Serializable]
    public class OutputVarModel : NotifyPropertyBase
    {
        private string _Name;

        public string Name
        {
            get { return _Name; }
            set { _Name = value; RaisePropertyChanged(); }
        }
        private eTypes _Type;

        public eTypes Type
        {
            get { return _Type; }
            set { _Type = value; RaisePropertyChanged(); }
        }
        private object _Value ;

        public object Value
        {
            get { return _Value; }
            set { _Value = value; RaisePropertyChanged(); }
        }

    }
}
