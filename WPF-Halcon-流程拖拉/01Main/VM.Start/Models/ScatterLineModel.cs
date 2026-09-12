using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VM.Start.Common.Helper;

namespace VM.Start.Models
{
    [Serializable]
    public class ScatterLineModel:NotifyPropertyBase
    {
        public const int DataLength = 1_000_000;
        private bool _IsVisible = true;
        /// <summary>
        /// 是否显示
        /// </summary>
        public bool IsVisible
        {
            get { return _IsVisible; }
            set { _IsVisible = value; this.RaisePropertyChanged(); }
        }
        /// <summary>
        /// 是上下限固定曲线
        /// </summary>
        public bool IsLimitLine { get; set; } = false;

        /// <summary>
        /// 是水平线
        /// </summary>
        public bool IsHorizontalLine { get; set; } = false;
        /// <summary>
        /// 水平线Y值
        /// </summary>
        public double HorizontalLine_Y { get; set; }

        /// <summary>
        /// X轴数据
        /// </summary>
        [JsonIgnore]
        public double[] Xs { get; set; } = new double[DataLength];
        /// <summary>
        /// Y轴数据
        /// </summary>
        [JsonIgnore]
        public double[] Ys { get; set; } = new double[DataLength];
        private Color _Color = Color.Green;
        /// <summary>
        /// 颜色
        /// </summary>
        public Color Color
        {
            get { return _Color; }
            set { _Color = value; this.RaisePropertyChanged(); }
        } 
        /// <summary>
        /// 标签
        /// </summary>
        public string Lable { get; set; }

        /// <summary>
        /// Y轴序号 0左边轴 1右边轴
        /// </summary>
        public int YAxisIndex { get; set; } = 0;

        /// <summary>
        /// 线宽
        /// </summary>
        public double LineWidth { get; set; } = 1;
        /// <summary>
        /// 是否平滑处理曲线
        /// </summary>
        public bool Smooth { get; set; }= true;
        /// <summary>
        /// Tension to use for smoothing when <see cref="Smooth"/> is enabled
        /// </summary>
        public double SmoothTension { get; set; } = 0.5;
        /// <summary>
        /// 标记点尺寸-0不显示标记点
        /// </summary>
        public float MarkerSize { get; set; } = 0;


    }
}
