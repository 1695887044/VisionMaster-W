using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;

namespace VM.Start.Models
{
    [Serializable]
    public class PointModel : NotifyPropertyBase
    {
        private float _X;
        /// <summary>
        /// X
        /// </summary>
        public float X
        {
            get { return _X; }
            set { _X = value; RaisePropertyChanged(); }
        }
        private float _Y;
        /// <summary>
        /// Y
        /// </summary>
        public float Y
        {
            get { return _Y; }
            set { _Y = value; RaisePropertyChanged(); }
        }
        private float _Z;
        /// <summary>
        /// Z
        /// </summary>
        public float Z
        {
            get { return _Z; }
            set { _Z = value; RaisePropertyChanged(); }
        }
        private float _U;
        /// <summary>
        /// U
        /// </summary>
        public float U
        {
            get { return _U; }
            set { _U = value; RaisePropertyChanged(); }
        }
        private float _W;
        /// <summary>
        /// W
        /// </summary>
        public float W
        {
            get { return _W; }
            set { _W = value; RaisePropertyChanged(); }
        }

    }
}
