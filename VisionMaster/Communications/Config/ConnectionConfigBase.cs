using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Sockets;
using System.Reflection.Metadata;
using UI.Attributes;

namespace VisionMaster.Communications
{
    public abstract class ConnectionConfigBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;



        public CommunicationType Type { get; set; }

        [SuperDisplay(Name = "超时时间(ms)", GroupPath = "高级参数", Order = 10, ColSpan = 4)]
        [RangeValidation(100, 60000, "超时必须在 100 - 60000 之间")]
        public int TimeoutMs { get; set; } = 3000;
        [SuperDisplay(Name = "重试次数", GroupPath = "高级参数", Order = 11, ColSpan = 4)]
        [RangeValidation(0, 10, "重试次数不能小于0")]
        public int RetryCount { get; set; } = 3;

        [SuperDisplay(Name = "重试间隔(ms)", GroupPath = "高级参数", Order = 12, ColSpan = 4)]
        public int RetryIntervalMs { get; set; } = 1000;

        public abstract ICommunicationConnection CreateConnection();
        public abstract ConnectionConfigBase Clone();

        public virtual bool Validate(out string errorMessage)
        {
            errorMessage = string.Empty;
            if (TimeoutMs <= 0) { errorMessage = "超时必须>0"; return false; }
            if (RetryCount < 0) { errorMessage = "重试次数不能<0"; return false; }
            return true;
        }

        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class SerialConfig : ConnectionConfigBase
    {
        [SuperDisplay(Name = "串口号", GroupPath = "串口参数", Order = 1, ColSpan = 6)]
        [Icon(IconCode = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z")]
        [Required(ErrorMessage = "串口号不能为空")]
        [RegexValidation(@"^COM\d+$", "格式必须如 COM1, COM2")]
        public string PortName { get; set; } = "COM1";
        [SuperDisplay(Name = "波特率", GroupPath = "串口参数", Order = 2, ColSpan = 6)]
        public int BaudRate { get; set; } = 9600;
        [SuperDisplay(Name = "数据位", GroupPath = "串口参数", Order = 3, ColSpan = 4)]
        public int DataBits { get; set; } = 8;
        [SuperDisplay(Name = "校验位", GroupPath = "串口参数", Order = 4, ColSpan = 4)]
        public ParityMode Parity { get; set; } = ParityMode.None;
        [SuperDisplay(Name = "停止位", GroupPath = "串口参数", Order = 5, ColSpan = 4)]
        public StopBitsMode StopBits { get; set; } = StopBitsMode.One;

        public override ICommunicationConnection CreateConnection() => new SerialConnection(this);
        public override ConnectionConfigBase Clone() => new SerialConfig
        {
            TimeoutMs = TimeoutMs, RetryCount = RetryCount, RetryIntervalMs = RetryIntervalMs,
            PortName = PortName, BaudRate = BaudRate, DataBits = DataBits, Parity = Parity, StopBits = StopBits
        };
    }

    public abstract class EthernetConfigBase : ConnectionConfigBase
    {
        // Icon 采用了经典的网球/网络节点 SVG
        [SuperDisplay(Name = "IP 地址", GroupPath = "网络参数", Order = 1, ColSpan = 8 )]
        [Required(ErrorMessage = "IP 地址不能为空")]
        [Icon(IconCode = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z")]
        [RegexValidation(@"^((25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(25[0-5]|2[0-4]\d|[01]?\d\d?)$", "IP 地址格式不合法")]
        public string IpAddress { get; set; } = "127.0.0.1";

        [SuperDisplay(Name = "端口号", GroupPath = "网络参数", Order = 2, ColSpan = 4)]
        [RangeValidation(1, 65535, "端口范围：1-65535")]
        public int Port { get; set; } = 502;

        [SuperDisplay(Name = "开启 KeepAlive", GroupPath = "网络参数", Order = 3, ColSpan = 6)]
        public bool EnableKeepAlive { get; set; } = true;

        [SuperDisplay(Name = "心跳间隔(ms)", GroupPath = "网络参数", Order = 4, ColSpan = 6)]
        public int KeepAliveIntervalMs { get; set; } = 30000;
    }

    public class ModbusTcpConfig : EthernetConfigBase
    {
        public override ICommunicationConnection CreateConnection() => new ModbusTcpConnection(this);
        public override ConnectionConfigBase Clone() => new ModbusTcpConfig
        {
            TimeoutMs = TimeoutMs, RetryCount = RetryCount, RetryIntervalMs = RetryIntervalMs,
            IpAddress = IpAddress, Port = Port, EnableKeepAlive = EnableKeepAlive, KeepAliveIntervalMs = KeepAliveIntervalMs
        };
    }

    public class SiemensS7Config : EthernetConfigBase
    {
        [SuperDisplay(Name = "CPU 类型", GroupPath = "S7专有参数", Order = 5, ColSpan = 4)]
        public string S7CpuType { get; set; } = "S1200";

        [SuperDisplay(Name = "机架号(Rack)", GroupPath = "S7专有参数", Order = 6, ColSpan = 4)]
        public byte Rack { get; set; } = 0;

        [SuperDisplay(Name = "插槽号(Slot)", GroupPath = "S7专有参数", Order = 7, ColSpan = 4)]
        public byte Slot { get; set; } = 0;

        public SiemensS7Config() {  }

        public override ICommunicationConnection CreateConnection() => new SiemensS7Connection(this);
        public override ConnectionConfigBase Clone() => new SiemensS7Config
        {
            TimeoutMs = TimeoutMs, RetryCount = RetryCount, RetryIntervalMs = RetryIntervalMs,
            IpAddress = IpAddress, Port = Port, EnableKeepAlive = EnableKeepAlive, KeepAliveIntervalMs = KeepAliveIntervalMs,
            S7CpuType = S7CpuType, Rack = Rack, Slot = Slot
        };
    }


    /// <summary>
    /// 三菱 MC 协议配置
    /// </summary>
    public class MitsubishiMcConfig : EthernetConfigBase
    {
        [SuperDisplay(Name = "PLC 型号", GroupPath = "MC专有参数", Order = 5, ColSpan = 6)]
        public string PlcModel { get; set; } = "QnA";

        [SuperDisplay(Name = "网络号", GroupPath = "MC专有参数", Order = 6, ColSpan = 4)]
        [RangeValidation(0, 255, "网络号范围：0-255")]
        public byte NetworkNumber { get; set; } = 0;

        [SuperDisplay(Name = "站号", GroupPath = "MC专有参数", Order = 7, ColSpan = 4)]
        [RangeValidation(0, 255, "站号范围：0-255")]
        public byte StationNumber { get; set; } = 0;

        [SuperDisplay(Name = "协议类型", GroupPath = "MC专有参数", Order = 8, ColSpan = 6)]
        public McProtocolType ProtocolType { get; set; } = McProtocolType.Tcp;

        [SuperDisplay(Name = "帧格式", GroupPath = "MC专有参数", Order = 9, ColSpan = 6)]
        public McFrameFormat FrameFormat { get; set; } = McFrameFormat.Binary;

        public MitsubishiMcConfig()
        {
            Port = 8000; // MC协议默认端口
        }

        public override ICommunicationConnection CreateConnection() => throw new NotImplementedException();

        public override ConnectionConfigBase Clone() => new MitsubishiMcConfig
        {
            TimeoutMs = TimeoutMs,
            RetryCount = RetryCount,
            RetryIntervalMs = RetryIntervalMs,
            IpAddress = IpAddress,
            Port = Port,
            EnableKeepAlive = EnableKeepAlive,
            KeepAliveIntervalMs = KeepAliveIntervalMs,
            PlcModel = PlcModel,
            NetworkNumber = NetworkNumber,
            StationNumber = StationNumber,
            ProtocolType = ProtocolType,
            FrameFormat = FrameFormat
        };
    }
    /// <summary>
    /// 欧姆龙 FINS 协议配置
    /// </summary>
    public class OmronFinsConfig : EthernetConfigBase
    {
        [SuperDisplay(Name = "PLC 网络号", GroupPath = "FINS专有参数", Order = 5, ColSpan = 4)]
        [RangeValidation(0, 127, "网络号范围：0-127")]
        public byte NetworkNumber { get; set; } = 0;

        [SuperDisplay(Name = "PLC 节点号", GroupPath = "FINS专有参数", Order = 6, ColSpan = 4)]
        [RangeValidation(0, 254, "节点号范围：0-254")]
        public byte NodeNumber { get; set; } = 0;

        [SuperDisplay(Name = "PLC 单元号", GroupPath = "FINS专有参数", Order = 7, ColSpan = 4)]
        [RangeValidation(0, 255, "单元号范围：0-255")]
        public byte UnitNumber { get; set; } = 0;

        [SuperDisplay(Name = "响应超时(ms)", GroupPath = "FINS专有参数", Order = 8, ColSpan = 6)]
        public int ResponseTimeoutMs { get; set; } = 5000;

        public OmronFinsConfig()
        {
            Port = 9600; // FINS协议默认端口
        }

        public override ICommunicationConnection CreateConnection() => throw new NotImplementedException();

        public override ConnectionConfigBase Clone() => new OmronFinsConfig
        {
            TimeoutMs = TimeoutMs,
            RetryCount = RetryCount,
            RetryIntervalMs = RetryIntervalMs,
            IpAddress = IpAddress,
            Port = Port,
            EnableKeepAlive = EnableKeepAlive,
            KeepAliveIntervalMs = KeepAliveIntervalMs,
            NetworkNumber = NetworkNumber,
            NodeNumber = NodeNumber,
            UnitNumber = UnitNumber,
            ResponseTimeoutMs = ResponseTimeoutMs
        };
    }
    /// <summary>
    /// OPC UA 协议配置
    /// </summary>
    public class OpcUaConfig : EthernetConfigBase
    {
        [SuperDisplay(Name = "端点URL", GroupPath = "OPC UA参数", Order = 5, ColSpan = 12)]
        [Required(ErrorMessage = "端点URL不能为空")]
        public string EndpointUrl { get; set; } = "opc.tcp://localhost:4840";

        [SuperDisplay(Name = "安全策略", GroupPath = "OPC UA参数", Order = 6, ColSpan = 6)]
        public OpcUaSecurityPolicy SecurityPolicy { get; set; } = OpcUaSecurityPolicy.None;

        [SuperDisplay(Name = "消息安全模式", GroupPath = "OPC UA参数", Order = 7, ColSpan = 6)]
        public OpcUaMessageSecurityMode MessageSecurityMode { get; set; } = OpcUaMessageSecurityMode.None;

        [SuperDisplay(Name = "用户名", GroupPath = "OPC UA参数", Order = 8, ColSpan = 6)]
        public string UserName { get; set; } = string.Empty;

        [SuperDisplay(Name = "密码", GroupPath = "OPC UA参数", Order = 9, ColSpan = 6)]
        public string Password { get; set; } = string.Empty;

        [SuperDisplay(Name = "会话超时(ms)", GroupPath = "OPC UA参数", Order = 10, ColSpan = 6)]
        public int SessionTimeoutMs { get; set; } = 30000;

        [SuperDisplay(Name = "订阅发布间隔(ms)", GroupPath = "OPC UA参数", Order = 11, ColSpan = 6)]
        public int PublishingIntervalMs { get; set; } = 1000;

        public OpcUaConfig()
        {
            Port = 4840; // OPC UA默认端口
        }

      //  public override ICommunicationConnection CreateConnection() => new OpcUaConnection(this);
        public override ICommunicationConnection CreateConnection() =>throw new NotImplementedException();

        public override ConnectionConfigBase Clone() => new OpcUaConfig
        {
            TimeoutMs = TimeoutMs,
            RetryCount = RetryCount,
            RetryIntervalMs = RetryIntervalMs,
            IpAddress = IpAddress,
            Port = Port,
            EnableKeepAlive = EnableKeepAlive,
            KeepAliveIntervalMs = KeepAliveIntervalMs,
            EndpointUrl = EndpointUrl,
            SecurityPolicy = SecurityPolicy,
            MessageSecurityMode = MessageSecurityMode,
            UserName = UserName,
            Password = Password,
            SessionTimeoutMs = SessionTimeoutMs,
            PublishingIntervalMs = PublishingIntervalMs
        };
    }
}
