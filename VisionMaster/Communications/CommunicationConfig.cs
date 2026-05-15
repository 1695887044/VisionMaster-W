using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 通讯连接配置模型
    /// </summary>
    [Serializable]
    public class CommunicationConfig
    {
        /// <summary>
        /// 连接名称
        /// </summary>
        public string ConnectionName { get; set; } = string.Empty;

        /// <summary>
        /// 通讯类型
        /// </summary>
        public CommunicationType Type { get; set; } = CommunicationType.ModbusTcp;

        /// <summary>
        /// IP地址(TCP通讯使用)
        /// </summary>
        public string IpAddress { get; set; } = "127.0.0.1";

        /// <summary>
        /// 端口号(TCP通讯使用)
        /// </summary>
        public int Port { get; set; } = 502;

        /// <summary>
        /// 连接超时时间(毫秒)
        /// </summary>
        public int ConnectionTimeout { get; set; } = 3000;

        /// <summary>
        /// 站号/从站地址(Modbus使用)
        /// </summary>
        public byte Station { get; set; } = 1;

        /// <summary>
        /// 串口名称(RTU通讯使用)
        /// </summary>
        public string SerialPort { get; set; } = "COM1";

        /// <summary>
        /// 波特率(RTU通讯使用)
        /// </summary>
        public int BaudRate { get; set; } = 9600;

        /// <summary>
        /// S7 CPU类型
        /// </summary>
        public string S7CpuType { get; set; } = "S1200";

        /// <summary>
        /// 读取周期(毫秒)
        /// </summary>
        public int ReadCycleMs { get; set; } = 1000;

        /// <summary>
        /// 是否启用该连接
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 是否在界面显示
        /// </summary>
        public bool IsVisible { get; set; } = true;
    }
}
