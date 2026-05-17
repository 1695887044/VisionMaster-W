using System;
using System.ComponentModel;

namespace VisionMaster.Communications
{
    public abstract class ConnectionConfigBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public abstract CommunicationType Type { get; }
        public int TimeoutMs { get; set; } = 3000;
        public int RetryCount { get; set; } = 3;
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
        public override CommunicationType Type => CommunicationType.ModbusRtu;
        public string PortName { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public int DataBits { get; set; } = 8;
        public ParityMode Parity { get; set; } = ParityMode.None;
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
        public string IpAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 502;
        public bool EnableKeepAlive { get; set; } = true;
        public int KeepAliveIntervalMs { get; set; } = 30000;
    }

    public class ModbusTcpConfig : EthernetConfigBase
    {
        public override CommunicationType Type => CommunicationType.ModbusTcp;
        public override ICommunicationConnection CreateConnection() => new ModbusTcpConnection(this);
        public override ConnectionConfigBase Clone() => new ModbusTcpConfig
        {
            TimeoutMs = TimeoutMs, RetryCount = RetryCount, RetryIntervalMs = RetryIntervalMs,
            IpAddress = IpAddress, Port = Port, EnableKeepAlive = EnableKeepAlive, KeepAliveIntervalMs = KeepAliveIntervalMs
        };
    }

    public class SiemensS7Config : EthernetConfigBase
    {
        public override CommunicationType Type => CommunicationType.SiemensS7;
        public string S7CpuType { get; set; } = "S1200";
        public byte Rack { get; set; } = 0;
        public byte Slot { get; set; } = 1;

        public SiemensS7Config() { Port = 102; }

        public override ICommunicationConnection CreateConnection() => new SiemensS7Connection(this);
        public override ConnectionConfigBase Clone() => new SiemensS7Config
        {
            TimeoutMs = TimeoutMs, RetryCount = RetryCount, RetryIntervalMs = RetryIntervalMs,
            IpAddress = IpAddress, Port = Port, EnableKeepAlive = EnableKeepAlive, KeepAliveIntervalMs = KeepAliveIntervalMs,
            S7CpuType = S7CpuType, Rack = Rack, Slot = Slot
        };
    }

    public class CommunicationDevice : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public string DeviceName { get; set; } = "Device_1";
        public CommunicationType Protocol { get; set; } = CommunicationType.ModbusTcp;
        public ConnectionConfigBase TransportConfig { get; set; } = new ModbusTcpConfig();
        public bool IsEnabled { get; set; } = true;
        public bool IsVisible { get; set; } = true;
        public string Description { get; set; } = string.Empty;

        public static ConnectionConfigBase CreateDefaultConfig(CommunicationType protocol)
        {
            return protocol switch
            {
                CommunicationType.ModbusTcp => new ModbusTcpConfig(),
                CommunicationType.ModbusRtu => new SerialConfig(),
                CommunicationType.SiemensS7 => new SiemensS7Config(),
                _ => throw new NotSupportedException($"不支持: {protocol}")
            };
        }

        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
