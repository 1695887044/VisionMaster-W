using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;

namespace VM.Start.Models
{
    [Serializable]
    public class Rectangle1Model:NotifyPropertyBase
    {
        private double _Row1;

        public double Row1
        {
            get { return _Row1; }
            set 
            {
                if (value<0)
                {
                    value = 0;
                }
                _Row1 = value;
                this.RaisePropertyChanged();
            }
        }
        private double _Col1;

        public double Col1
        {
            get { return _Col1; }
            set
            {
                if (value < 0)
                {
                    value = 0;
                }
                _Col1 = value;
                this.RaisePropertyChanged();
            }
        }
        private double _Row2;

        public double Row2
        {
            get { return _Row2; }
            set { Set(ref _Row2, value); }
        }
        private double _Col2;

        public double Col2
        {
            get { return _Col2; }
            set { Set(ref _Col2, value); }
        }

    }
}
