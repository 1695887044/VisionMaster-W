using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 连接工厂接口
    /// 定义创建连接对象的标准方法
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
        /// <param name="config">通讯配置</param>
        /// <returns>连接对象</returns>
        ICommunicationConnection Create(CommunicationConfig config);

        /// <summary>
        /// 创建连接对象
        /// </summary>
        /// <param name="config">连接配置</param>
        /// <returns>连接对象</returns>
        ICommunicationConnection Create(ConnectionConfigBase config);

        /// <summary>
        /// 检查是否支持指定的配置类型
        /// </summary>
        /// <param name="config">连接配置</param>
        /// <returns>是否支持</returns>
        bool Supports(ConnectionConfigBase config);

        /// <summary>
        /// 获取工厂描述
        /// </summary>
        string Description { get; }
    }

    /// <summary>
    /// Modbus TCP 连接工厂
    /// </summary>
    public class ModbusTcpConnectionFactory : IConnectionFactory
    {
        /// <inheritdoc />
        public CommunicationType SupportedType => CommunicationType.ModbusTcp;

        /// <inheritdoc />
        public string Description => "Modbus TCP/IP 连接工厂";

        /// <inheritdoc />
        public ICommunicationConnection Create(CommunicationConfig config)
        {
            if (config.Config is not ModbusTcpConfig)
            {
                throw new InvalidOperationException("需要 Modbus TCP 配置");
            }

            return new ModbusTcpConnection(config);
        }

        /// <inheritdoc />
        public ICommunicationConnection Create(ConnectionConfigBase config)
        {
            if (config is not ModbusTcpConfig modbusConfig)
            {
                throw new InvalidOperationException("需要 Modbus TCP 配置");
            }

            return new ModbusTcpConnection(modbusConfig);
        }

        /// <inheritdoc />
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
        /// <inheritdoc />
        public CommunicationType SupportedType => CommunicationType.SiemensS7;

        /// <inheritdoc />
        public string Description => "西门子 S7 系列 PLC 连接工厂";

        /// <inheritdoc />
        public ICommunicationConnection Create(CommunicationConfig config)
        {
            if (config.Config is not SiemensS7Config)
            {
                throw new InvalidOperationException("需要西门子S7配置 (SiemensS7Config)");
            }

            return new SiemensS7Connection(config);
        }

        /// <inheritdoc />
        public ICommunicationConnection Create(ConnectionConfigBase config)
        {
            if (config is not SiemensS7Config s7Config)
            {
                throw new InvalidOperationException("需要西门子S7配置 (SiemensS7Config)");
            }

            return new SiemensS7Connection(s7Config);
        }

        /// <inheritdoc />
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
        /// <inheritdoc />
        public CommunicationType SupportedType => CommunicationType.ModbusRtu;

        /// <inheritdoc />
        public string Description => "Modbus RTU (串口) 连接工厂";

        /// <inheritdoc />
        public ICommunicationConnection Create(CommunicationConfig config)
        {
            if (config.Config is not SerialConfig)
            {
                throw new InvalidOperationException("需要串口配置 (SerialConfig)");
            }

            return new SerialConnection(config);
        }

        /// <inheritdoc />
        public ICommunicationConnection Create(ConnectionConfigBase config)
        {
            if (config is not SerialConfig serialConfig)
            {
                throw new InvalidOperationException("需要串口配置 (SerialConfig)");
            }

            return new SerialConnection(serialConfig);
        }

        /// <inheritdoc />
        public bool Supports(ConnectionConfigBase config)
        {
            return config is SerialConfig;
        }
    }

    /// <summary>
    /// 连接工厂管理器
    /// 负责管理和注册各种连接工厂
    /// </summary>
    public class ConnectionFactoryManager
    {
        private static readonly Lazy<ConnectionFactoryManager> _instance =
            new(() => new ConnectionFactoryManager());

        private readonly Dictionary<CommunicationType, IConnectionFactory> _factories = new();
        private readonly object _lock = new();

        /// <summary>
        /// 单例实例
        /// </summary>
        public static ConnectionFactoryManager Instance => _instance.Value;

        /// <summary>
        /// 私有构造函数
        /// </summary>
        private ConnectionFactoryManager()
        {
            RegisterDefaults();
        }

        /// <summary>
        /// 注册默认工厂
        /// </summary>
        private void RegisterDefaults()
        {
            Register(new ModbusTcpConnectionFactory());
            Register(new SiemensS7ConnectionFactory());
            Register(new ModbusRtuConnectionFactory());
        }

        /// <summary>
        /// 注册连接工厂
        /// </summary>
        /// <param name="factory">连接工厂</param>
        public void Register(IConnectionFactory factory)
        {
            lock (_lock)
            {
                _factories[factory.SupportedType] = factory;
            }
        }

        /// <summary>
        /// 注销连接工厂
        /// </summary>
        /// <param name="type">通讯类型</param>
        /// <returns>是否成功注销</returns>
        public bool Unregister(CommunicationType type)
        {
            lock (_lock)
            {
                return _factories.Remove(type);
            }
        }

        /// <summary>
        /// 获取连接工厂
        /// </summary>
        /// <param name="type">通讯类型</param>
        /// <returns>连接工厂，如果不存在返回null</returns>
        public IConnectionFactory? GetFactory(CommunicationType type)
        {
            lock (_lock)
            {
                return _factories.TryGetValue(type, out var factory) ? factory : null;
            }
        }

        /// <summary>
        /// 创建连接对象
        /// </summary>
        /// <param name="config">通讯配置</param>
        /// <returns>连接对象</returns>
        public ICommunicationConnection CreateConnection(CommunicationConfig config)
        {
            var factory = GetFactory(config.Protocol);
            if (factory == null)
            {
                throw new NotSupportedException($"不支持的通讯协议: {config.Protocol}");
            }

            return factory.Create(config);
        }

        /// <summary>
        /// 创建连接对象
        /// </summary>
        /// <param name="config">连接配置</param>
        /// <returns>连接对象</returns>
        public ICommunicationConnection CreateConnection(ConnectionConfigBase config)
        {
            var factory = GetFactory(config.Type);
            if (factory == null)
            {
                throw new NotSupportedException($"不支持的通讯协议: {config.Type}");
            }

            return factory.Create(config);
        }

        /// <summary>
        /// 检查是否支持指定的配置类型
        /// </summary>
        /// <param name="config">连接配置</param>
        /// <returns>是否支持</returns>
        public bool Supports(ConnectionConfigBase config)
        {
            var factory = GetFactory(config.Type);
            return factory?.Supports(config) ?? false;
        }

        /// <summary>
        /// 获取所有支持的通讯类型
        /// </summary>
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

        /// <summary>
        /// 获取所有已注册的工厂
        /// </summary>
        public IEnumerable<IConnectionFactory> Factories
        {
            get
            {
                lock (_lock)
                {
                    return _factories.Values.ToList().AsReadOnly();
                }
            }
        }
    }
}
