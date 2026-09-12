using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VM.Start.ViewModels.Dock
{
    public class VisionViewModel
    {
        #region Singleton
        private static readonly VisionViewModel _instance = new VisionViewModel();
        private VisionViewModel()
        {
        }
        public static VisionViewModel Ins
        {
            get { return _instance; }
        }
        #endregion

    }
}
