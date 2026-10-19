using System;
using Core.Interfaces.Io;
using HslCommunication;
using HslCommunication.Core;
using HslCommunication.ModBus;
using Newtonsoft.Json.Linq;

namespace VisionMaster.Services.Io
{
    internal static class ModbusConfig
    {
        public static (byte UnitId, IoByteOrder ByteOrder, bool AddressStartWithZero) Parse(string? json)
        {
            byte unitId = 1;
            var order = IoByteOrder.CDAB;
            bool startWithZero = true;   // HSL 默认：地址从 0 起
            if (string.IsNullOrWhiteSpace(json)) return (unitId, order, startWithZero);

            try
            {
                var obj = JObject.Parse(json);
                if (obj.TryGetValue("UnitId", StringComparison.OrdinalIgnoreCase, out var unit))
                    unitId = unit.Value<byte>();
                if (obj.TryGetValue("ByteOrder", StringComparison.OrdinalIgnoreCase, out var bo) &&
                    Enum.TryParse<IoByteOrder>(bo.Value<string>(), true, out var parsed))
                    order = parsed;
                if (obj.TryGetValue("AddressStartWithZero", StringComparison.OrdinalIgnoreCase, out var zero))
                    startWithZero = zero.Value<bool>();
            }
            catch
            {
                // 配置损坏时用默认值；具体错误由设备 Validate/UI 提示。
            }
            return (unitId, order, startWithZero);
        }

        public static DataFormat ToDataFormat(IoByteOrder order) => order switch
        {
            IoByteOrder.ABCD => DataFormat.ABCD,
            IoByteOrder.BADC => DataFormat.BADC,
            IoByteOrder.CDAB => DataFormat.CDAB,
            IoByteOrder.DCBA => DataFormat.DCBA,
            _ => DataFormat.CDAB
        };
    }

    public sealed class ModbusTcpIoDevice : HslIoDeviceBase
    {
        private ModbusTcpNet? _client;

        public ModbusTcpIoDevice(IoDeviceDescriptor descriptor, IoBusDescriptor bus, IIoRuntimeContext context)
            : base(descriptor, bus, context)
        {
            Capabilities = IoCapabilities.DigitalInput | IoCapabilities.DigitalOutput |
                           IoCapabilities.AnalogInput | IoCapabilities.AnalogOutput;
        }

        protected override IReadWriteNet? Open(out string? error)
        {
            error = null;
            var (unitId, order, startWithZero) = ModbusConfig.Parse(Descriptor.DriverConfigJson);
            var client = new ModbusTcpNet(Bus.Tcp.Host, Bus.Tcp.Port, unitId)
            {
                ConnectTimeOut = Math.Max(100, Descriptor.Params.ConnectTimeoutMs),
                ReceiveTimeOut = Math.Max(100, Bus.Tcp.ReadTimeoutMs),
                ByteTransform = new RegularByteTransform(ModbusConfig.ToDataFormat(order)),
                AddressStartWithZero = startWithZero
            };

            var result = client.ConnectServer();
            if (!result.IsSuccess)
            {
                error = Describe(result);
                try { client.ConnectClose(); } catch { }
                return null;
            }

            _client = client;
            return client;
        }

        protected override void Close()
        {
            _client?.ConnectClose();
            _client = null;
        }
    }

    [IoDriver("modbus.tcp")]
    public sealed class ModbusTcpIoDriverFactory : IIoDriverFactory
    {
        public string DriverId => "modbus.tcp";
        public string DisplayName => "Modbus TCP";
        public string Description => "Modbus TCP 远程 IO / PLC";
        public IoCapabilities Capabilities =>
            IoCapabilities.DigitalInput | IoCapabilities.DigitalOutput |
            IoCapabilities.AnalogInput | IoCapabilities.AnalogOutput;
        public Type? BusConfigType => typeof(TcpTransportConfig);
        public Type? DeviceConfigType => typeof(ModbusDeviceConfig);

        public IIoDevice Create(IoDeviceDescriptor descriptor, IoBusDescriptor bus, IIoRuntimeContext context)
            => new ModbusTcpIoDevice(descriptor, bus, context);
    }

    public sealed class ModbusRtuIoDevice : HslIoDeviceBase
    {
        private ModbusRtu? _client;

        public ModbusRtuIoDevice(IoDeviceDescriptor descriptor, IoBusDescriptor bus, IIoRuntimeContext context)
            : base(descriptor, bus, context)
        {
            Capabilities = IoCapabilities.DigitalInput | IoCapabilities.DigitalOutput |
                           IoCapabilities.AnalogInput | IoCapabilities.AnalogOutput;
        }

        protected override IReadWriteNet? Open(out string? error)
        {
            error = null;
            var (unitId, order, startWithZero) = ModbusConfig.Parse(Descriptor.DriverConfigJson);
            var client = new ModbusRtu(unitId);
            client.SerialPortInni(
                Bus.Serial.PortName,
                Bus.Serial.BaudRate,
                Bus.Serial.DataBits,
                ToStopBits(Bus.Serial.StopBits),
                ToParity(Bus.Serial.Parity));
            client.ReceiveTimeOut = Math.Max(100, Bus.Serial.ReadTimeoutMs);
            client.ByteTransform = new RegularByteTransform(ModbusConfig.ToDataFormat(order));
            client.AddressStartWithZero = startWithZero;

            var result = client.Open();
            if (!result.IsSuccess)
            {
                error = Describe(result);
                try { client.Close(); } catch { }
                return null;
            }

            _client = client;
            return client;
        }

        protected override void Close()
        {
            _client?.Close();
            _client = null;
        }

        private static System.IO.Ports.StopBits ToStopBits(IoStopBits bits) => bits switch
        {
            IoStopBits.One => System.IO.Ports.StopBits.One,
            IoStopBits.OnePointFive => System.IO.Ports.StopBits.OnePointFive,
            IoStopBits.Two => System.IO.Ports.StopBits.Two,
            _ => System.IO.Ports.StopBits.One
        };

        private static System.IO.Ports.Parity ToParity(IoParity parity) => parity switch
        {
            IoParity.Odd => System.IO.Ports.Parity.Odd,
            IoParity.Even => System.IO.Ports.Parity.Even,
            _ => System.IO.Ports.Parity.None
        };
    }

    [IoDriver("modbus.rtu")]
    public sealed class ModbusRtuIoDriverFactory : IIoDriverFactory
    {
        public string DriverId => "modbus.rtu";
        public string DisplayName => "Modbus RTU";
        public string Description => "Modbus RTU 串口总线（多从站）";
        public IoCapabilities Capabilities =>
            IoCapabilities.DigitalInput | IoCapabilities.DigitalOutput |
            IoCapabilities.AnalogInput | IoCapabilities.AnalogOutput;
        public Type? BusConfigType => typeof(SerialTransportConfig);
        public Type? DeviceConfigType => typeof(ModbusDeviceConfig);

        public IIoDevice Create(IoDeviceDescriptor descriptor, IoBusDescriptor bus, IIoRuntimeContext context)
            => new ModbusRtuIoDevice(descriptor, bus, context);
    }

    /// <summary>Modbus 驱动私有配置（由 FlatPropertyGrid 反射生成）。</summary>
    public sealed class ModbusDeviceConfig
    {
        [UI.Attributes.SuperDisplay(Name = "站号", Group = new[] { "Modbus" }, Order = 1)]
        public byte UnitId { get; set; } = 1;

        [UI.Attributes.SuperDisplay(Name = "字序", Group = new[] { "Modbus" }, Order = 2)]
        public IoByteOrder ByteOrder { get; set; } = IoByteOrder.CDAB;

        /// <summary>
        /// 地址基数：true = 从 0 起（HSL 默认，"40001" 指第 2 个保持寄存器）；
        /// false = 从 1 起（PLC/组态软件习惯，"40001" 指第 1 个）。
        ///
        /// <para>为什么要暴露它：现场图纸与 PLC 变量表几乎都按 4xxxx 的 1 基编号书写，
        /// 而 HSL 默认按 0 基解析。默认值保持 true 是为了不让已有方案整体错位一格；
        /// 按 PLC 习惯配置的方案必须显式关掉它，否则会**静默读写到相邻寄存器**。</para>
        /// </summary>
        [UI.Attributes.SuperDisplay(
            Name = "地址从 0 起",
            Group = new[] { "Modbus" },
            Order = 3,
            Description = "勾选=0 基（HSL 默认，40001 是第 2 个寄存器）；取消=1 基（PLC 习惯，40001 是第 1 个）")]
        public bool AddressStartWithZero { get; set; } = true;
    }
}
