using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;

namespace VM.Start.ViewModels.Dock
{
    public class ProcessViewModel:NotifyPropertyBase
    {
        #region Singleton
        private static readonly ProcessViewModel _instance = new ProcessViewModel();

        private ProcessViewModel()
        {
        }
        public static ProcessViewModel Ins
        {
            get { return _instance; }
        }
        #endregion

        #region Prop
        private double _ProcessTime;
        public double ProcessTime
        {
            get { return _ProcessTime; }
            set { _ProcessTime = value; this.RaisePropertyChanged(); }
        }

        #endregion
    }
}
