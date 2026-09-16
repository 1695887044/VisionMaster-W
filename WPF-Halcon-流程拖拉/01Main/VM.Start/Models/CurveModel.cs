using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Localization;

namespace VM.Start.Models
{
    [Serializable]
    public class CurveModel
    {
        [JsonIgnore]
        public EventHandler DeployCustomMenu;
        /// <summary>
        /// 曲线标题
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// X轴标签
        /// </summary>
        public string XAxisLabel { get; set; } = Resource.Time;

        /// <summary>
        /// Y轴标签
        /// </summary>
        public string YAxisLabel { get; set; }

        /// <summary>
        /// Y2轴标签
        /// </summary>
        public string YAxis2Label { get; set; }

        /// <summary>
        /// X轴颜色
        /// </summary>
        public Color XAxisColor { get; set; } = Color.Blue;

        /// <summary>
        /// Y轴颜色
        /// </summary>
        public Color YAxisColor { get; set; }= Color.Black;

        /// <summary>
        /// Y2轴颜色
        /// </summary>
        public Color YAxis2Color { get; set; } = Color.Black;

        public double AxisLimit_XMin { get; set; } = 0;
        public double AxisLimit_XMax { get; set; } = 6000;
        public double AxisLimit_YMin { get; set; } = 0;
        public double AxisLimit_YMax { get; set; } = 300;
        public double AxisLimit_Y2Min { get; set; } = 0;
        public double AxisLimit_Y2Max { get; set; } = 100;

        /// <summary>
        /// 线属性集合
        /// </summary>
        public List<ScatterLineModel> ScatterLineList { get; set; }
    }
}
