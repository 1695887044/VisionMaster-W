using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VM.Start.Models
{
    [Serializable]
    public class ReportModel
    {
        /// <summary>
        /// 条码
        /// </summary>
        public string SN { get; set; }
        /// <summary>
        /// 查询时间
        /// </summary>
        public string Time { get; set; }
        /// <summary>
        /// 工位号
        /// </summary>
        public string StationNum { get; set; }
        /// <summary>
        /// 电脑名
        /// </summary>
        public string ComputerName { get; set; }
        /// <summary>
        /// 配方名
        /// </summary>
        public string RecipeName { get; set; }
        /// <summary>
        /// 焊接周期（s）
        /// </summary>
        public string WeldCT { get; set; }
        /// <summary>
        /// 加工结果 OK/NG
        /// </summary>
        public string WeldResult { get; set; }


    }
}
