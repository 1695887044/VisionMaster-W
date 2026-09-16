using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VM.Start.Models
{
    public class IoModel
    {
        #region 输入信号
        /// <summary>
        /// 急停
        /// </summary>
        public bool iEmergency;
        /// <summary>
        /// 复位按钮
        /// </summary>
        public bool iReset;
        /// <summary>
        /// 启动信号0
        /// </summary>
        public bool iStartSignal_0 { get; set; }
        /// <summary>
        /// 启动信号1
        /// </summary>
        public bool iStartSignal_1 { get; set; }
        /// <summary>
        /// 启动信号2
        /// </summary>
        public bool iStartSignal_2 { get; set; }
        /// <summary>
        /// 启动信号3
        /// </summary>
        public bool iStartSignal_3 { get; set; }
        /// <summary>
        /// 双手启动1
        /// </summary>
        public bool iTwoHandsStart1 { get; set; }
        /// <summary>
        /// 双手启动2
        /// </summary>
        public bool iTwoHandsStart2 { get; set; }
        /// <summary>
        /// 激光器就绪信号
        /// </summary>
        public bool iLaserReady;
        /// <summary>
        /// 激光器报警信号
        /// </summary>
        public bool iLaserAlm;
        /// <summary>
        /// 激光器警告信号
        /// </summary>
        public bool iLaserWarn;
        /// <summary>
        /// 水箱报警
        /// </summary>
        public bool iWaterTankAlm;
        #endregion

        #region 输出信号
        /// <summary>
        /// 红灯
        /// </summary>
        public bool qLampRed;
        /// <summary>
        /// 黄灯
        /// </summary>
        public bool qLampYellow;
        /// <summary>
        /// 绿灯
        /// </summary>
        public bool qLampGreen;
        /// <summary>
        /// 蜂鸣器
        /// </summary>
        public bool qBuzzer;
        /// <summary>
        /// PC_Ready
        /// </summary>
        public bool qPC_Ready;
        /// <summary>
        /// 功率报警
        /// </summary>
        public bool qAlmPower;
        /// <summary>
        /// 设备报警
        /// </summary>
        public bool qDeviceAlarm;
        /// <summary>
        /// 条码报警
        /// </summary>
        public bool qAlmBarcode;
        /// <summary>
        /// 设备运行中信号
        /// </summary>
        public bool qRunning;
        /// <summary>
        /// 结果OK
        /// </summary>
        public bool qResult_OK;
        /// <summary>
        /// 结果NG
        /// </summary>
        public bool qResult_NG;
        /// <summary>
        /// 系统回零完成
        /// </summary>
        public bool qHomeDone;
        /// <summary>
        /// 完成信号
        /// </summary>
        public bool qFinishSignal;
        /// <summary>
        /// 是否是旋转模式
        /// </summary>
        public bool qIsRotateMode;
        public int StartSignal;
        //public int FinishSignal;
        #endregion



    }
}
