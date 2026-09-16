using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VM.Start.Models
{
    public class MachineModel
    {
        /// <summary>
        /// 启动
        /// </summary>
        public bool Start{ get; set; }
        /// <summary>
        /// 暂停
        /// </summary>
        public bool Pause{ get; set; }
        /// <summary>
        /// 停止
        /// </summary>
        public bool Stop{ get; set; }
        /// <summary>
        /// 急停
        /// </summary>
        public bool Emergency{ get; set; }
        /// <summary>
        /// 自动运行中标志
        /// </summary>
        public bool AutoRunning{ get; set; }
        /// <summary>
        /// 报警标志
        /// </summary>
        public bool AlmFlag{ get; set; }
        /// <summary>
        /// 系统总复位
        /// </summary>
        public bool SysHomeStart{ get; set; }
        /// <summary>
        /// 系统复位完成
        /// </summary>
        public bool SysHomeDone{ get; set; }
        /// <summary>
        /// 安全光栅信号
        /// </summary>
        public bool Raster{ get; set; }
        /// <summary>
        /// 自动运行步号
        /// </summary>
        public int StepAuto{ get; set; }
        private AlarmSummaryModel _Alm = new AlarmSummaryModel();
        public AlarmSummaryModel Alm
        {
            get { return _Alm; }
            set { _Alm = value; }
        }


    }
}
