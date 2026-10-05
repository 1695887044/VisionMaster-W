using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 连接工厂接口
    /// </summary>
    public interface IConnectionFactory
    {
        /// <summary>
        /// 支持的通讯类型
        /// </summary>
        CommunicationType SupportedType { get; }

        /// <summary>
        /// 创建连接对象
        /// </summary>
        ICommunicationConnection Create(CommunicationConfig config);

        /// <summary>
        /// 检查是否支持指定的配置类型
        /// </summary>
        bool Supports(ConnectionConfigBase config);

        /// <summary>
        /// 工厂描述
        /// </summary>
        string Description { get; }
    }

    /// <summary>
    /// Modbus TCP 连接工厂
    /// </summary>
    public class ModbusTcpConnectionFactory : IConnectionFactory
    {
        public CommunicationType SupportedType => CommunicationType.ModbusTcp;
        public string Description => "Modbus TCP/IP 连接工厂";

        public ICommunicationConnection Create(CommunicationConfig config)
        {
            return new ModbusTcpConnection((ModbusTcpConfig)config.Config);
        }

        public bool Supports(ConnectionConfigBase config)
        {
            return config is ModbusTcpConfig;
        }
    }

    /// <summary>
    /// 西门子 S7 连接工厂
    /// </summary>
    public class SiemensS7ConnectionFactory : IConnectionFactory
    {
        public CommunicationType SupportedType => CommunicationType.SiemensS7;
        public string Description => "西门子 S7 系列 PLC 连接工厂";

        public ICommunicationConnection Create(CommunicationConfig config)
        {
            return new SiemensS7Connection((SiemensS7Config)config.Config);
        }

        public bool Supports(ConnectionConfigBase config)
        {
            return config is SiemensS7Config;
        }
    }

    /// <summary>
    /// Modbus RTU (串口) 连接工厂
    /// </summary>
    public class ModbusRtuConnectionFactory : IConnectionFactory
    {
        public CommunicationType SupportedType => CommunicationType.ModbusRtu;
        public string Description => "Modbus RTU (串口) 连接工厂";

        public ICommunicationConnection Create(CommunicationConfig config)
        {
            return new SerialConnection((SerialConfig)config.Config);
        }

        public bool Supports(ConnectionConfigBase config)
        {
            return config is SerialConfig;
        }
    }

    /// <summary>
    /// 连接工厂管理器（单例）
    /// </summary>
    public class ConnectionFactoryManager
    {
        private static readonly Lazy<ConnectionFactoryManager> _instance = new(() => new ConnectionFactoryManager());
        private readonly Dictionary<CommunicationType, IConnectionFactory> _factories = new();
        private readonly object _lock = new();

        public static ConnectionFactoryManager Instance => _instance.Value;

        private ConnectionFactoryManager()
        {
            RegisterDefaults();
        }

        private void RegisterDefaults()
        {
            Register(new ModbusTcpConnectionFactory());
            Register(new SiemensS7ConnectionFactory());
            Register(new ModbusRtuConnectionFactory());
        }

        public void Register(IConnectionFactory factory)
        {
            lock (_lock)
            {
                _factories[factory.SupportedType] = factory;
            }
        }

        public bool Unregister(CommunicationType type)
        {
            lock (_lock)
            {
                return _factories.Remove(type);
            }
        }

        public IConnectionFactory? GetFactory(CommunicationType type)
        {
            lock (_lock)
            {
                return _factories.TryGetValue(type, out var factory) ? factory : null;
            }
        }

        public ICommunicationConnection CreateConnection(CommunicationConfig config)
        {
            var factory = GetFactory(config.Protocol);
            if (factory == null)
                throw new NotSupportedException($"不支持的通讯协议: {config.Protocol}");

            return factory.Create(config);
        }

        public bool Supports(ConnectionConfigBase config)
        {
            var factory = GetFactory(config.Type);
            return factory?.Supports(config) ?? false;
        }

        public IEnumerable<CommunicationType> SupportedTypes
        {
            get
            {
                lock (_lock)
                {
                    return _factories.Keys.ToList().AsReadOnly();
                }
            }
        }
    }
}