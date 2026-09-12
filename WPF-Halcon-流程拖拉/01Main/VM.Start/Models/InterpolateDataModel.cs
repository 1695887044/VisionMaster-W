using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;

namespace VM.Start.Models
{
    public class InterpolateDataModel : NotifyPropertyBase
    {
        public InterpolateDataModel()
        {

        }
        private PointModel _Point = new PointModel();
        /// <summary>
        /// 点
        /// </summary>
        public PointModel Point
        {
            get { return _Point; }
            set { Set(ref _Point, value); }
        }

        private PointModel _CircleCenter = new PointModel();
        /// <summary>
        /// 圆心
        /// </summary>
        public PointModel CircleCenter
        {
            get { return _CircleCenter; }
            set { Set(ref _CircleCenter, value); }
        }
        private PointModel _CircleStartPoint = new PointModel();
        /// <summary>
        /// 圆起始点
        /// </summary>
        public PointModel CircleStartPoint
        {
            get { return _CircleStartPoint; }
            set { Set(ref _CircleStartPoint, value); }
        }
        private PointModel _CircleEndPoint = new PointModel();
        /// <summary>
        /// 圆终止点
        /// </summary>
        public PointModel CircleEndPoint
        {
            get { return _CircleEndPoint; }
            set { Set(ref _CircleEndPoint, value); }
        }
        private float _RadiusX;
        public float RadiusX
        {
            get { return _RadiusX; }
            set { _RadiusX = value; RaisePropertyChanged(); }
        }

        private PointModel _LineStartPoint = new PointModel();
        /// <summary>
        /// 起始点
        /// </summary>
        public PointModel LineStartPoint
        {
            get { return _LineStartPoint; }
            set { Set(ref _LineStartPoint, value); }
        }
        private PointModel _LineEndPoint = new PointModel();
        /// <summary>
        /// 终止点
        /// </summary>
        public PointModel LineEndPoint
        {
            get { return _LineEndPoint; }
            set { Set(ref _LineEndPoint, value); }
        }
        private float _Velocity1 = 80;
        /// <summary>
        /// XYZ插补速度
        /// </summary>
        public float Velocity1
        {
            get { return _Velocity1; }
            set { Set(ref _Velocity1, value); }
        }
        private float _Velocity2 = 80;
        /// <summary>
        /// XYZ插补速度
        /// </summary>
        public float Velocity2
        {
            get { return _Velocity2; }
            set { Set(ref _Velocity2, value); }
        }

        private int _DelayTime;
        /// <summary>
        /// 延时时间ms
        /// </summary>
        public int DelayTime
        {
            get { return _DelayTime; }
            set { _DelayTime = value; RaisePropertyChanged(); }
        }
        private ushort _IoIndex = 1;
        /// <summary>
        /// IO序号
        /// </summary>
        public ushort IoIndex
        {
            get { return _IoIndex; }
            set { _IoIndex = value; RaisePropertyChanged(); }
        }
        private bool _IoState;

        public bool IoState
        {
            get { return _IoState; }
            set { _IoState = value; RaisePropertyChanged(); }
        }

    }
}
