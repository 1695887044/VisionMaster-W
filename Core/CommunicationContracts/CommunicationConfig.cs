using System;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using UI.Attributes;

namespace VisionMaster.Communications
{
    [Serializable]
    public class CommunicationConfig : BindableBase
    {
        [SuperDisplay(Name = "连接名称", GroupPath = "1. 基本设置", Order = 1, ColSpan = 12)]
        [Required(ErrorMessage = "名称不能为空")]
        public string ConnectionName
        {
            get => field;
            set => SetProperty(ref field, value);
        } = $"Conn_{DateTime.Now:HHmmss}";

        [SuperDisplay(
            Name = "协议类型",
            GroupPath = "1. 基本设置",
            Order = 2,
            ColSpan = 12,
            RequireRefresh = true
        )]
        public CommunicationType Protocol
        {
            get { return field; }
            set
            {
                OnChanged(value);
                field = value;
                RaisePropertyChanged();
            }
        }

        private void OnChanged(CommunicationType communication)
        {
            Config = communication switch
            {
                CommunicationType.ModbusTcp => new ModbusTcpConfig { Type = communication },
                CommunicationType.ModbusRtu => new SerialConfig { Type = communication },
                CommunicationType.SiemensS7 => new SiemensS7Config { Type = communication },
                CommunicationType.OmronFins => new OmronFinsConfig { Type = communication },
                CommunicationType.MitsubishiMc => new MitsubishiMcConfig { Type = communication },
                CommunicationType.OpcUa => new OpcUaConfig { Type = communication },
                _ => throw new ArgumentOutOfRangeException(nameof(communication), communication, "不支持的通信类型")
            };
        }
        [SuperDisplay(Name = "底层链路参数", GroupPath = "2. 链路配置", Order = 1, ColSpan = 12)]
        [PropertyItem(Type = typeof(System.Windows.Controls.Control))] // 告诉框架这是一个嵌套对象，向下解析
        public ConnectionConfigBase Config
        {
            get => field;
            set { SetProperty(ref field, value); }
        } = new ModbusTcpConfig();

        [SuperDisplay(Name = "读取周期(ms)", GroupPath = "3. 运行调度", Order = 1, ColSpan = 6)]
        public int ReadCycleMs { get; set; } = 1000;

        [SuperDisplay(Name = "开机自启", GroupPath = "3. 运行调度", Order = 2, ColSpan = 6)]
        public bool AutoStart { get; set; } = true;

        [SuperDisplay(Name = "自动重连", GroupPath = "3. 运行调度", Order = 3, ColSpan = 6)]
        public bool AutoReconnect { get; set; } = true;

        [SuperDisplay(Name = "启用该连接", GroupPath = "3. 运行调度", Order = 4, ColSpan = 6)]
        public bool IsEnabled { get; set; } = true;

        [SuperDisplay(Name = "备注说明", GroupPath = "1. 基本设置", Order = 3, ColSpan = 12)]
        public string Description { get; set; } = string.Empty;

        [JsonIgnore]
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        [JsonIgnore]
        public DateTime LastModifiedTime { get; set; } = DateTime.Now;

        [JsonIgnore]
        public DateTime LastConnectedTime { get; set; }

        private ConnectionState _state = ConnectionState.Disconnected;

        /// <summary>连接状态（带通知：连接/断开后 UI 状态徽标自动联动）</summary>
        [JsonIgnore]
        public ConnectionState State
        {
            get => _state;
            set => SetProperty(ref _state, value);
        }

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

        private static ConnectionConfigBase CreateConfig(CommunicationType protocol)
        {
            return protocol switch
            {
                CommunicationType.ModbusTcp => new ModbusTcpConfig(),
                CommunicationType.ModbusRtu => new SerialConfig(),
                CommunicationType.SiemensS7 => new SiemensS7Config(),
                _ => throw new NotSupportedException($"不支持: {protocol}"),
            };
        }

        public bool Validate(out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(ConnectionName))
            {
                errorMessage = "名称不能为空";
                return false;
            }
            if (Config == null)
            {
                errorMessage = "配置不能为空";
                return false;
            }
            if (!Config.Validate(out string configError))
            {
                errorMessage = configError;
                return false;
            }
            if (Protocol != Config.Type)
            {
                errorMessage = "协议类型不匹配";
                return false;
            }
            return true;
        }

        public CommunicationConfig Clone() =>
            new CommunicationConfig
            {
                ConnectionName = ConnectionName + "_Copy",
                Protocol = Protocol,
                Config = Config?.Clone(),
                ReadCycleMs = ReadCycleMs,
                IsEnabled = IsEnabled,
                AutoReconnect = AutoReconnect,
                AutoStart = AutoStart,
                Description = Description,
            };

        /// <summary>
        /// 把 <paramref name="other"/> 的**可编辑字段**复制到本实例（"副本编辑、确定才回写" 的落地手段）。
        /// 只覆盖用户在属性面板能改的项，<see cref="State"/>/<c>CreatedTime</c>/<c>LastConnectedTime</c>
        /// 等运行时状态一律保留本实例的值——它们反映真实连接实况，不能被编辑弹窗清掉。
        /// </summary>
        public void CopyFrom(CommunicationConfig other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            // 顺序不可颠倒：Protocol 的 setter 内部会 OnChanged() 重建一份默认 Config，
            // 先赋 Protocol 再赋 Config，最终落到 Config 上的才是副本里的那份配置
            Protocol = other.Protocol;
            Config = other.Config?.Clone(); // 再深拷一份：避免活对象与副本共享同一条链路配置子对象
            ConnectionName = other.ConnectionName;
            ReadCycleMs = other.ReadCycleMs;
            IsEnabled = other.IsEnabled;
            AutoReconnect = other.AutoReconnect;
            AutoStart = other.AutoStart;
            Description = other.Description;
            UpdateModifiedTime();
        }

        public void UpdateLastConnectedTime() => LastConnectedTime = DateTime.Now;

        public void UpdateModifiedTime() => LastModifiedTime = DateTime.Now;

        public override string ToString() =>
            $"{ConnectionName} [{Protocol}] ({Config}) - {(IsEnabled ? "启用" : "禁用")}";
    }
}
