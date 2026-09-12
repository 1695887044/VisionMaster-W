using System.Windows.Media;

namespace VM.Start.Models
{
    public class PenParModel
    {
        /// <summary>
        /// 笔号
        /// </summary>
        public int ID { get; set; }
        /// <summary>
        /// 颜色
        /// </summary>
        public int clr { get; set; }
        /// <summary>
        /// 颜色
        /// </summary>
        public Brush Color { get; set; }
        /// <summary>
        /// 是否使能笔号
        /// </summary>
        public bool DisableMark { get; set; }
        /// <summary>
        /// 是否使用默认值
        /// </summary>
        public bool UseDefParam { get; set; }

        /// <summary>
        /// 加工次数
        /// </summary>
        public int MarkLoop { get; set; }
        /// <summary>
        /// 标刻速度
        /// </summary>
        public double MarkSpeed { get; set; }
        /// <summary>
        /// 功率
        /// </summary>
        public double PowerRatio { get; set; }
        /// <summary>
        /// 电流
        /// </summary>
        public double Current { get; set; }
        /// <summary>
        /// 频率
        /// </summary>
        public double Frequency { get; set; }
        /// <summary>
        /// Q脉冲宽度
        /// </summary>
        public double QPulseWidth { get; set; }
        /// <summary>
        /// 开始延时
        /// </summary>
        public int StartTC { get; set; }
        /// <summary>
        /// 激光关闭延时
        /// </summary>
        public int LaserOffTC { get; set; }
        /// <summary>
        /// 结束延时
        /// </summary>
        public int EndTC { get; set; }
        /// <summary>
        /// 拐角延时
        /// </summary>
        public int PolyTC { get; set; }
        /// <summary>
        /// 跳转速度
        /// </summary>
        public double JumpSpeed { get; set; }
        /// <summary>
        /// 最小跳转延时
        /// </summary>
        public int MinJumpDelayTCUs { get; set; }
        /// <summary>
        /// 最大跳转延时
        /// </summary>
        public int MaxJumpDelayTCUs { get; set; }
        /// <summary>
        /// 跳转长度极限
        /// </summary>
        public double JumpLengthLimit { get; set; }
        /// <summary>
        /// 打点时间
        /// </summary>
        public double PointTimeMs { get; set; }
        /// <summary>
        /// SPI连续模式
        /// </summary>
        public bool SpiContinueMode { get; set; }
        /// <summary>
        /// SPI波形选择
        /// </summary>
        public int SpiWave { get; set; }
        /// <summary>
        /// YAG优化填充模式
        /// </summary>
        public int YagMarkMode { get; set; }
        /// <summary>
        ///脉冲点模式
        /// </summary>
        public bool PulsePointMode { get; set; }
        /// <summary>
        /// 脉冲点数
        /// </summary>
        public int PulseNum { get; set; }
        /// <summary>
        /// 使能加速模式
        /// </summary>
        public bool EnableACCMode { get; set; }
        /// <summary>
        /// 末点补偿
        /// </summary>
        public double EndComp { get; set; }
        /// <summary>
        /// 加速距离
        /// </summary>
        public double AccDist { get; set; }
        /// <summary>
        /// 抖动模式
        /// </summary>
        public bool WobbleMode { get; set; }
        /// <summary>
        /// 抖动类型
        /// </summary>
        public int WobbleType { get; set; }
        /// <summary>
        /// 抖动直径mm
        /// </summary>
        public double WobbleDiameter { get; set; }
        /// <summary>
        /// 抖动直径2mm
        /// </summary>
        public double WobbleDiameterB { get; set; }
        /// <summary>
        /// 抖动距离mm
        /// </summary>
        public double WobbleDist { get; set; }
        /// <summary>
        /// 抖动距离mm
        /// </summary>
        public double WobbleSpeed { get; set; }


    }
}
