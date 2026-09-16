using EventMgrLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using VM.Start.Common;
using VM.Start.Common.Helper;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.ViewModels;
using VM.Start.Views;

namespace Plugin.GrabImage.Model
{
    [Serializable]
    public class MathTemplateModel : NotifyPropertyBase
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
        private double _Score;
        /// <summary>
        /// 匹配分数
        /// </summary>
        public double Score
        {
            get { return _Score; }
            set { Set(ref _Score, value); }
        }
        private double _X;
        /// <summary>
        /// X坐标
        /// </summary>
        public double X
        {
            get { return _X; }
            set { Set(ref _X, value); }
        }
        private double _Y;
        /// <summary>
        /// Y坐标
        /// </summary>
        public double Y
        {
            get { return _Y; }
            set { Set(ref _Y, value); }
        }
        private double _Deg;
        /// <summary>
        /// 角度
        /// </summary>
        public double Deg
        {
            get { return _Deg; }
            set { Set(ref _Deg, value); }
        }

    }
}
