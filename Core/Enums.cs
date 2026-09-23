using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.Attributes;

namespace VisionMaster
{

    public enum eViewMode
    {
        One,
        Two,
        Three,
        Four,
        Five,
        Six,
        Seven,
        Eight,
        Night
    }
    /// <summary>
    /// 变量类型枚举
    /// </summary>
    public enum VariableType
    {
        /// <summary>
        /// 本地变量
        /// </summary>
        Local,
        /// <summary>
        /// 通讯变量
        /// </summary>
        Communication
    }
    /// <summary>
    /// 会话状态枚举
    /// </summary>
    public enum SessionState
    {
        Idle,
        Running,
        Paused,
        Stopped,
        Faulted
    }
    public enum DataQuality
    {
        /// <summary>数据有效且可靠</summary>
        Good,

        /// <summary>数据无效，通常由通信错误导致</summary>
        Bad,

        /// <summary>数据可能不准确或已过时</summary>
        Uncertain,

        /// <summary>未建立连接或数据未初始化</summary>
        NotConnected
    }
    /// <summary>
    /// 分支类型枚举
    /// ⚠ 新增值只允许追加在末尾：JSON 按 int 序列化，插入中间会错位老方案数据
    /// </summary>
    public enum BranchType
    {
        Default,
        If,
        Else,
        ElseIf,
        Case,

        /// <summary>
        /// While 条件循环体：必须书写循环条件。
        /// 历史上它与 For 循环体共用 Default，导致 UI 层无法区分"可不写条件"（Else/For）
        /// 与"必须写条件"（While），出现空条件零警告的说谎显示；老数据在方案加载时归一化升级
        /// </summary>
        WhileLoop,
    }

    /// <summary>
    /// 方案操作枚举
    /// </summary>
    public enum SolutionAction
    {
        Create,
        BrowseList,
        Open,
        Save
    }

    /// <summary>
    /// 执行操作枚举
    /// </summary>
    public enum ExecutionAction
    {
        Compile,
        RunOnce,
        RunContinuous,
        Stop
    }

    /// <summary>
    /// 主界面运行控制状态（UI 唯一事实来源：同时驱动按钮互锁与状态栏显示）
    /// </summary>
    public enum MainRunState
    {
        /// <summary>未启动</summary>
        NotStarted,
        /// <summary>单次运行中</summary>
        RunningOnce,
        /// <summary>循环运行中</summary>
        RunningContinuous
    }

    /// <summary>
    /// 系统操作枚举
    /// </summary>
    public enum SystemAction
    {
        UserLogin,
        GlobalVariables,
        CameraSettings,
        CommSettings,
        HardwareSettings,
        ReportQuery,
        ReturnToZero,
        UIDesign,
        /// <summary>
        /// 运行窗口设置：组态运行窗口是依附主窗口（全屏）还是独立窗口（并排）。
        /// 刻意加在末尾而不是插在中间——枚举的整数值可能已经落进过 Layout.xml / 旧配置文件，
        /// 中间插一个会把后面所有成员的值整体挪一位，读回来就是另一个动作了。
        /// </summary>
        RuntimeWindowSettings,
        /// <summary>
        /// 系统参数设置：空闲自动登出时长、审计 / 报警历史的保留天数。
        /// 与 <see cref="RuntimeWindowSettings"/> 同口径（软件级配置，存 AppConfig.json），
        /// 同样加在末尾——理由见上一条注释。
        /// </summary>
        SystemParameters,
        /// <summary>
        /// 变量事件：按变量集中配置值驱动的规则（更改数值 / 值为真 / 值为假 / 上限 / 下限）。
        /// 与上面两个不同，它改的是<b>方案内容</b>（落 .vms、进撤销栈），而不是软件级偏好；
        /// 之所以也走「系统」菜单，是因为它没有"当前选中的图元"这个落脚点——
        /// 可组态对象直接是变量，属性面板描述不了它。同样加在末尾（理由见 RuntimeWindowSettings）。
        /// </summary>
        VariableEvents
    }

    /// <summary>
    /// 流程操作枚举
    /// </summary>
    public enum FlowAction
    {
        Create,
        Delete,
        BrowseList,
        Edit,
        Rename,
        EditComment,
        Encrypt,
        Decrypt,
        Manager
    }

    /// <summary>
    /// 步骤状态枚举
    /// </summary>
    public enum StepState
    {
        Idle,
        Running,
        Success,
        Warning,
        Failed,
        Cancel,
        Skipped
    }

    /// <summary>
    /// 流程触发模式枚举
    /// </summary>
    public enum FlowTriggerMode
    {
        [Display(Name = "单次执行", Description = "手动触发，流程只运行一次")]
        Single = 0,
        [Display(Name = "连续运行", Description = "无间断循环执行当前流程")]
        Continuous = 1,
        [Display(Name = "外部触发", Description = "等待外部 IO 或 PLC 信号触发执行")]
        External = 2,
        [Display(Name = "定时触发", Description = "按照设定的时间周期自动循环执行")]
        Timer = 3
    }

    /// <summary>
    /// 模块命令操作枚举
    /// </summary>
    public enum ModuleCommandAction
    {
        Rename,
        EditComment,
        ExecuteSelected,
        ExecuteFromHere,
        ShowAll,
        EnableSuperTool,
        SetBreakpoint,
        ModuleParameters,
        Cut,
        Copy,
        Paste,
        Disable,
        Delete,
        AddElseIf,
        AddElse,
        AddCase
    }
    // 2. Modbus 特有区域枚举
    public enum ModbusArea
    {
        Coils,             // 0x: 线圈 (读写布尔)
        DiscreteInputs,    // 1x: 离散输入 (只读布尔)
        InputRegisters,    // 3x: 输入寄存器 (只读字)
        HoldingRegisters   // 4x: 保持寄存器 (读写字)
    }

    // 3. 西门子 S7 特有区域枚举
    public enum S7Area
    {
        DB,  // 数据块
        I,   // 输入映像区
        Q,   // 输出映像区
        M,   // 内部标志位
        V    // V区 (200Smart特有)
    }

    // 4. 欧姆龙特有区域枚举
    public enum OmronArea
    {
        D,   // 数据区
        CIO, // 核心I/O区
        W,   // 工作区
        H,   // 保持区
        A    // 辅助区
    }

    // 5. 三菱特有区域枚举
    public enum MitsubishiArea
    {
        D,   // 数据寄存器
        M,   // 内部继电器
        X,   // 输入继电器 (十六进制寻址)
        Y,   // 输出继电器 (十六进制寻址)
        W,   // 链接寄存器 (十六进制寻址)
        B    // 链接继电器 (十六进制寻址)
    }

    // 6. OPC UA 标识符类型枚举
    public enum OpcUaIdType
    {
        String,
        Numeric,
        Guid
    }

    // 7. 裸报文（Raw Socket）数据解析模式枚举
    public enum ParseMode
    {
        Regex,
        SplitByComma,
        ReadAll
    }

    public enum ByteOrderFormat
    {
        ABCD, // 大端
        DCBA, // 小端
        BADC, // 字交换大端
        CDAB  // 字交换小端
    }

    // 9. 变量访问权限模式枚举
    public enum VariableAccessMode
    {
        ReadOnly,
        WriteOnly,
        ReadWrite
    }

    // 10. 变量质量戳（简化三档，语义对齐 OPC UA StatusCode）
    public enum VariableQuality
    {
        Good,       // 最近一次读成功，值为新鲜值
        Uncertain,  // 最近一次读失败（段读/单读/解码失败），当前显示的是旧值
        Bad         // 连接断开或从未读到过任何值
    }
    // 校验位
    public enum ParityMode
    {
        None = 0,
       Odd = 1,
       Even = 2
    }

    // 停止位
    public enum StopBitsMode
    {
      One = 1,
        OnePointFive = 3, // 底层库通常用3代表1.5
        Two = 2
    }

    // 通讯类型枚举
    public enum CommunicationType
    {
        ModbusTcp,
        ModbusRtu,
        SiemensS7,
        OmronFins,
        MitsubishiMc,
        FreeProtocol,
        OpcUa
    }
    public enum DataValueType
    {
        Boolean,
        SByte,
        Byte,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Int64,
        UInt64,
        Float,
        Double,
        String,
        ByteArray
    }
    public enum McProtocolType
    {
        Tcp,
        Udp
    }

    public enum McFrameFormat
    {
        Binary,
        Ascii
    }

    public enum FreeProtocolMode
    {
        Serial,
        Ethernet
    }

    public enum ChecksumType
    {
        None,
        Xor,
        Sum8,
        Sum16,
        Crc16,
        Crc32
    }
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Error,
        Reconnecting,
    }

    public enum OpcUaSecurityPolicy
    {
        None,
        Basic128Rsa15,
        Basic256,
        Basic256Sha256,
        Aes128_Sha256_RsaOaep,
        Aes256_Sha256_RsaPss
    }

    public enum OpcUaMessageSecurityMode
    {
        None,
        Sign,
        SignAndEncrypt
    }
}
