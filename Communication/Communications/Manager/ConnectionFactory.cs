using System;
using System.Collections.Generic;
using System.Linq;
using VisionMaster.Communications;

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
    /// 三菱 MC（QnA 3E 帧）连接工厂
    /// </summary>
    public class MitsubishiMcConnectionFactory : IConnectionFactory
    {
        public CommunicationType SupportedType => CommunicationType.MitsubishiMc;
        public string Description => "三菱 MC（QnA 3E 帧，含基恩士/松下变体）连接工厂";

        public ICommunicationConnection Create(CommunicationConfig config)
        {
            return new MitsubishiMcConnection((MitsubishiMcConfig)config.Config);
        }

        public bool Supports(ConnectionConfigBase config)
        {
            return config is MitsubishiMcConfig;
        }
    }

    /// <summary>
    /// 欧姆龙 FINS/TCP 连接工厂
    /// </summary>
    public class OmronFinsConnectionFactory : IConnectionFactory
    {
        public CommunicationType SupportedType => CommunicationType.OmronFins;
        public string Description => "欧姆龙 FINS/TCP 连接工厂";

        public ICommunicationConnection Create(CommunicationConfig config)
        {
            return new OmronFinsConnection((OmronFinsConfig)config.Config);
        }

        public bool Supports(ConnectionConfigBase config)
        {
            return config is OmronFinsConfig;
        }
    }

    /// <summary>
    /// 自由协议（通用 TCP 文本命令）连接工厂
    /// </summary>
    public class FreeProtocolConnectionFactory : IConnectionFactory
    {
        public CommunicationType SupportedType => CommunicationType.FreeProtocol;
        public string Description => "自由协议（通用 TCP 文本命令）连接工厂";

        public ICommunicationConnection Create(CommunicationConfig config)
        {
            return new FreeProtocolConnection((FreeProtocolConfig)config.Config);
        }

        public bool Supports(ConnectionConfigBase config)
        {
            return config is FreeProtocolConfig;
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

    #region 内建驱动（字符串 Id 形态，供外部驱动统一注册）

    /// <summary>Modbus TCP 驱动：一个 host:port 一条 TCP，站号可逐帧指定（富地址 <c>s=N;</c>）。</summary>
    public sealed class ModbusTcpDriverFactory : ICommunicationDriverFactory
    {
        public string DriverId => "modbus.tcp";
        public string DisplayName => "Modbus TCP";
        public string Description => "Modbus TCP/IP（一个端点一条 TCP；站号可逐帧指定）";
        public Type? ConfigType => typeof(ModbusTcpConfig);

        public ICommunicationConnection Create(ConnectionConfigBase config)
            => new ModbusTcpConnection((ModbusTcpConfig)config);
    }

    /// <summary>Modbus RTU 驱动：一条 COM 一条串口，多从站靠逐帧站号共享（富地址 <c>s=N;</c>）。</summary>
    public sealed class ModbusRtuDriverFactory : ICommunicationDriverFactory
    {
        public string DriverId => "modbus.rtu";
        public string DisplayName => "Modbus RTU";
        public string Description => "Modbus RTU 串口（一条 COM 可挂多个从站，站号逐帧生效）";
        public Type? ConfigType => typeof(SerialConfig);

        public ICommunicationConnection Create(ConnectionConfigBase config)
            => new SerialConnection((SerialConfig)config);
    }

    /// <summary>西门子 S7 驱动：一个 PLC 一条 TCP。</summary>
    public sealed class SiemensS7DriverFactory : ICommunicationDriverFactory
    {
        public string DriverId => "siemens.s7";
        public string DisplayName => "西门子 S7";
        public string Description => "西门子 S7 系列 PLC（S7-200/300/400/1200/1500）";
        public Type? ConfigType => typeof(SiemensS7Config);

        public ICommunicationConnection Create(ConnectionConfigBase config)
            => new SiemensS7Connection((SiemensS7Config)config);
    }

    /// <summary>三菱 MC 驱动：一个 PLC 一条 TCP（二进制/ASCII 3E 帧）。</summary>
    public sealed class MitsubishiMcDriverFactory : ICommunicationDriverFactory
    {
        public string DriverId => "mitsubishi.mc";
        public string DisplayName => "三菱 MC";
        public string Description => "三菱 Q/L/FX 系列 MC 3E 帧（二进制/ASCII；含基恩士/松下变体）";
        public Type? ConfigType => typeof(MitsubishiMcConfig);

        public ICommunicationConnection Create(ConnectionConfigBase config)
            => new MitsubishiMcConnection((MitsubishiMcConfig)config);
    }

    /// <summary>欧姆龙 FINS 驱动：一个 PLC 一条 TCP。</summary>
    public sealed class OmronFinsDriverFactory : ICommunicationDriverFactory
    {
        public string DriverId => "omron.fins";
        public string DisplayName => "欧姆龙 FINS";
        public string Description => "欧姆龙 CP/CJ/NJ/NX 系列 FINS/TCP（默认端口 9600）";
        public Type? ConfigType => typeof(OmronFinsConfig);

        public ICommunicationConnection Create(ConnectionConfigBase config)
            => new OmronFinsConnection((OmronFinsConfig)config);
    }

    /// <summary>自由协议驱动：一条 TCP，按帧结束符收发文本命令。</summary>
    public sealed class FreeProtocolDriverFactory : ICommunicationDriverFactory
    {
        public string DriverId => "free.protocol";
        public string DisplayName => "自由协议";
        public string Description => "通用 TCP 文本命令（自定义帧结束符；适合条码枪/称重仪/扭矩枪等）";
        public Type? ConfigType => typeof(FreeProtocolConfig);

        public ICommunicationConnection Create(ConnectionConfigBase config)
            => new FreeProtocolConnection((FreeProtocolConfig)config);
    }

    #endregion

    /// <summary>
    /// 连接工厂管理器（单例）
    /// <para><b>改造后它只是驱动注册表的枚举视图</b>：<see cref="SupportedTypes"/> 与
    /// <see cref="CreateConnection(CommunicationConfig)"/> 都从 <see cref="Drivers"/> 取，
    /// 不再自己维护一份"内建三种协议"的清单。新增协议只需注册一个
    /// <see cref="ICommunicationDriverFactory"/>，无需再改本类。</para>
    /// </summary>
    public class ConnectionFactoryManager
    {
        private static readonly Lazy<ConnectionFactoryManager> _instance = new(() => new ConnectionFactoryManager());
        private readonly Dictionary<CommunicationType, IConnectionFactory> _factories = new();
        private readonly object _lock = new();

        public static ConnectionFactoryManager Instance => _instance.Value;

        /// <summary>驱动注册表（字符串 Id，外部驱动与内建驱动统一入口）。</summary>
        public CommunicationDriverRegistry Drivers { get; } = new();

        private ConnectionFactoryManager()
        {
            RegisterDefaults();
        }

        private void RegisterDefaults()
        {
            Register(new ModbusTcpConnectionFactory());
            Register(new SiemensS7ConnectionFactory());
            Register(new ModbusRtuConnectionFactory());
            Register(new MitsubishiMcConnectionFactory());
            Register(new OmronFinsConnectionFactory());
            Register(new FreeProtocolConnectionFactory());

            // 同一批协议再以字符串 Id 注册一遍：外部驱动走这张表，内建驱动也必须在里面，
            // 否则"可用协议清单"会出现两套（枚举一份、驱动一份），迟早不一致。
            Drivers.Register(new ModbusTcpDriverFactory());
            Drivers.Register(new ModbusRtuDriverFactory());
            Drivers.Register(new SiemensS7DriverFactory());
            Drivers.Register(new MitsubishiMcDriverFactory());
            Drivers.Register(new OmronFinsDriverFactory());
            Drivers.Register(new FreeProtocolDriverFactory());
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