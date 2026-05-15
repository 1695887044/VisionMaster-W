namespace VisionMaster.Communications
{
    /// <summary>
    /// 通讯类型枚举
    /// </summary>
    public enum CommunicationType
    {
        /// <summary>
        /// Modbus TCP协议
        /// </summary>
        ModbusTcp = 0,

        /// <summary>
        /// Modbus RTU协议(待实现)
        /// </summary>
        ModbusRtu = 1,

        /// <summary>
        /// 西门子S7协议
        /// </summary>
        SiemensS7 = 2,

        /// <summary>
        /// 自由协议(待实现)
        /// </summary>
        FreeProtocol = 3
    }

    /// <summary>
    /// 变量访问权限枚举
    /// </summary>
    public enum VariableAccessMode
    {
        /// <summary>
        /// 只读权限
        /// </summary>
        ReadOnly = 0,

        /// <summary>
        /// 读写权限
        /// </summary>
        ReadWrite = 1
    }
}
