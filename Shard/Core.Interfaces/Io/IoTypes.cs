using System;
using System.ComponentModel.DataAnnotations;

namespace Core.Interfaces.Io
{
    // ============================================================================
    // IO 接入层已下线（2026-10-20）：设备/点位不再有运行时，IO 点位就是通讯层的网络变量。
    // 本文件只保留**配置描述符需要的枚举**——它们是方案里旧 IoBuses/IoDevices 字段的类型，
    // 也是 IoMigrationPlanner 折算成"连接 + 变量"的输入。
    // 运行时的 IoQuality / IoDeviceState / IoCapabilities / IoPointValue / IoWriteResult 等
    // 已随 IO 层一起删除（全仓零引用）。
    // ============================================================================

    /// <summary>IO 点位类型。P1 只支持数字量/模拟量输入输出，不包含高速计数/脉冲。</summary>
    public enum IoPointKind
    {
        [Display(Name = "数字输入")] DigitalInput,
        [Display(Name = "数字输出")] DigitalOutput,
        [Display(Name = "模拟输入")] AnalogInput,
        [Display(Name = "模拟输出")] AnalogOutput
    }

    public enum IoAccess
    {
        [Display(Name = "只读")] ReadOnly,
        [Display(Name = "只写")] WriteOnly,
        [Display(Name = "读写")] ReadWrite
    }

    public enum IoDataType
    {
        [Display(Name = "布尔")] Bool,
        [Display(Name = "16 位整数")] Int16,
        [Display(Name = "16 位无符号")] UInt16,
        [Display(Name = "32 位整数")] Int32,
        [Display(Name = "32 位无符号")] UInt32,
        [Display(Name = "浮点")] Float,
        [Display(Name = "双精度")] Double
    }

    public enum IoFailSafe
    {
        [Display(Name = "保持")] Hold,
        [Display(Name = "断开")] Off,
        [Display(Name = "闭合")] On,
        [Display(Name = "安全值")] Value
    }

    public enum IoTransportKind
    {
        [Display(Name = "TCP")] Tcp,
        [Display(Name = "串口")] Serial
    }

    public enum IoParity
    {
        [Display(Name = "无")] None,
        [Display(Name = "奇校验")] Odd,
        [Display(Name = "偶校验")] Even
    }

    public enum IoStopBits
    {
        [Display(Name = "1")] One,
        [Display(Name = "1.5")] OnePointFive,
        [Display(Name = "2")] Two
    }

    /// <summary>多寄存器数值的字序。16 位及以下时 ABCD 与 CDAB 等价。</summary>
    public enum IoByteOrder
    {
        [Display(Name = "ABCD")] ABCD,
        [Display(Name = "BADC")] BADC,
        [Display(Name = "CDAB")] CDAB,
        [Display(Name = "DCBA")] DCBA
    }
}
