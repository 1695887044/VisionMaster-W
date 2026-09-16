using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Services;

namespace VM.Start.Models
{
    [Serializable]
    public class MeasInfoModel:NotifyPropertyBase
    {
        public MeasInfoModel()
        {
            ParamName = new HTuple();
            ParamName.Append("measure_transition");
            ParamName.Append("measure_select");
            ParamName.Append("measure_distance");
            ParamValue = new HTuple();
            ParamValue.Append(REnum.EnumToStr(_MeasMode));
            ParamValue.Append(REnum.EnumToStr(_MeasSelect));
            ParamValue.Append(_MeasDis);
        }
        public Array MeasModes { get; set; } = Enum.GetValues(typeof(eMeasMode));
        private eMeasMode _MeasMode=eMeasMode.由白到黑;
        /// <summary>
        /// 测量模式
        /// </summary>
        public eMeasMode MeasMode
        {
            get { return _MeasMode; }
            set
            {
                Set(ref _MeasMode, value,
                    new Action(() =>
                    {
                        ParamValue = new HTuple();
                        ParamValue.Append(REnum.EnumToStr(_MeasMode));
                        ParamValue.Append(REnum.EnumToStr(_MeasSelect));
                        ParamValue.Append(_MeasDis);
                    }));
            }
        }
        public Array MeasSelects { get; set; } = Enum.GetValues(typeof(eMeasSelect));
        private eMeasSelect _MeasSelect=eMeasSelect.第一点;
        /// <summary>
        /// 测量点筛选
        /// </summary>
        public eMeasSelect MeasSelect
        {
            get { return _MeasSelect; }
            set
            {
                Set(ref _MeasSelect, value,
                    new Action(() =>
                    {
                        ParamValue = new HTuple();
                        ParamValue.Append(REnum.EnumToStr(_MeasMode));
                        ParamValue.Append(REnum.EnumToStr(_MeasSelect));
                        ParamValue.Append(_MeasDis);
                    }));
            }
        }
        private double _Length1=20;
        /// <summary>
        /// 长/2
        /// </summary>
        public double Length1
        {
            get { return _Length1; }
            set { Set(ref _Length1, value); }
        }
        private double _Length2=5;
        /// <summary>
        /// 宽/2
        /// </summary>
        public double Length2
        {
            get { return _Length2; }
            set { Set(ref _Length2, value); }
        }
        private double _Threshold=30;
        /// <summary>
        /// 阈值
        /// </summary>
        public double Threshold
        {
            get { return _Threshold; }
            set { Set(ref _Threshold, value); }
        }
        private double _MeasDis=10;
        /// <summary>
        /// 间隔
        /// </summary>
        public double MeasDis
        {
            get { return _MeasDis; }
            set { Set(ref _MeasDis, value); }
        }
        private HTuple _ParamName;
        /// <summary>
        /// 参数名
        /// </summary>
        public HTuple ParamName
        {
            get { return _ParamName; }
            set { Set(ref _ParamName, value); }
        }
        private HTuple _ParamValue;
        /// <summary>
        /// 参数值
        /// </summary>
        public HTuple ParamValue
        {
            get { return _ParamValue; }
            set { Set(ref _ParamValue, value); }
        }
        private int _PointsOrder;
        /// <summary>
        /// 点顺序 0位默认,1顺时针,2逆时针
        /// </summary>
        public int PointsOrder
        {
            get { return _PointsOrder; }
            set { Set(ref _PointsOrder, value); }
        }

    }
}
