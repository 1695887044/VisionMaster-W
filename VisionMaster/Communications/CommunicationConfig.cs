using System;
using System.Text.Json.Serialization;

namespace VisionMaster.Communications
{
    [Serializable]
    public class CommunicationConfig
    {
        public string ConnectionName { get; set; } = string.Empty;
        public CommunicationType Protocol { get; set; } = CommunicationType.ModbusTcp;
        public ConnectionConfigBase Config { get; set; } = new ModbusTcpConfig();
        public int ReadCycleMs { get; set; } = 1000;
        public bool IsEnabled { get; set; } = true;
        public bool IsVisible { get; set; } = true;
        public bool AutoReconnect { get; set; } = true;
        public bool AutoStart { get; set; } = true;
        public string Description { get; set; } = string.Empty;

        [JsonIgnore]
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        [JsonIgnore]
        public DateTime LastModifiedTime { get; set; } = DateTime.Now;

        [JsonIgnore]
        public DateTime LastConnectedTime { get; set; }

        [JsonIgnore]
        public ConnectionState State { get; set; } = ConnectionState.Disconnected;

        public CommunicationConfig() { }

        public CommunicationConfig(CommunicationType protocol)
        {
            Protocol = protocol;
            Config = CreateConfig(protocol);
            ConnectionName = $"Conn_{protocol}_{DateTime.Now:HHmmss}";
        }

        public CommunicationConfig(string connectionName, ConnectionConfigBase config)
        {
            ConnectionName = connectionName;
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Protocol = config.Type;
        }

        public static CommunicationConfig CreateModbusTcp(string name, string ip, int port = 502)
        {
            return new CommunicationConfig
            {
                ConnectionName = name,
                Protocol = CommunicationType.ModbusTcp,
                Config = new ModbusTcpConfig { IpAddress = ip, Port = port },
                AutoStart = true, AutoReconnect = true, ReadCycleMs = 1000
            };
        }

        public static CommunicationConfig CreateSiemensS7(string name, string ip,int port = 102, string cpuType = "S1200")
        {
            return new CommunicationConfig
            {
                ConnectionName = name,
                Protocol = CommunicationType.SiemensS7,
                Config = new SiemensS7Config { IpAddress = ip, S7CpuType = cpuType ,Port =port},

                AutoStart = true, AutoReconnect = true, ReadCycleMs = 1000
            };
        }

        public static CommunicationConfig CreateModbusRtu(string name, string port, int baudRate = 9600)
        {
            return new CommunicationConfig
            {
                ConnectionName = name,
                Protocol = CommunicationType.ModbusRtu,
                Config = new SerialConfig { PortName = port, BaudRate = baudRate },
                AutoStart = true, AutoReconnect = true, ReadCycleMs = 1000
            };
        }

        private static ConnectionConfigBase CreateConfig(CommunicationType protocol)
        {
            return protocol switch
            {
                CommunicationType.ModbusTcp => new ModbusTcpConfig(),
                CommunicationType.ModbusRtu => new SerialConfig(),
                CommunicationType.SiemensS7 => new SiemensS7Config(),
                _ => throw new NotSupportedException($"不支持: {protocol}")
            };
        }

        public bool Validate(out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(ConnectionName)) { errorMessage = "名称不能为空"; return false; }
            if (Config == null) { errorMessage = "配置不能为空"; return false; }
            if (!Config.Validate(out string configError)) { errorMessage = configError; return false; }
            if (Protocol != Config.Type) { errorMessage = "协议类型不匹配"; return false; }
            return true;
        }

        public CommunicationConfig Clone() => new CommunicationConfig
        {
            ConnectionName = ConnectionName + "_Copy",
            Protocol = Protocol,
            Config = Config?.Clone(),
            ReadCycleMs = ReadCycleMs,
            IsEnabled = IsEnabled,
            IsVisible = IsVisible,
            AutoReconnect = AutoReconnect,
            AutoStart = AutoStart,
            Description = Description
        };

        public void UpdateLastConnectedTime() => LastConnectedTime = DateTime.Now;
        public void UpdateModifiedTime() => LastModifiedTime = DateTime.Now;

        public override string ToString() => $"{ConnectionName} [{Protocol}] ({Config}) - {(IsEnabled ? "启用" : "禁用")}";
    }

    public enum ConnectionState { Disconnected, Connecting, Connected, Error, Reconnecting }
}
