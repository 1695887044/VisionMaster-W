using System;
using Core.Interfaces.Io;
using HslCommunication;
using HslCommunication.Core;
using HslCommunication.Profinet.Siemens;
using Newtonsoft.Json.Linq;

namespace VisionMaster.Services.Io
{
    public sealed class SiemensS7IoDevice : HslIoDeviceBase
    {
        private SiemensS7Net? _client;

        public SiemensS7IoDevice(IoDeviceDescriptor descriptor, IoBusDescriptor bus, IIoRuntimeContext context)
            : base(descriptor, bus, context)
        {
            Capabilities = IoCapabilities.DigitalInput | IoCapabilities.DigitalOutput |
                           IoCapabilities.AnalogInput | IoCapabilities.AnalogOutput;
        }

        protected override IReadWriteNet? Open(out string? error)
        {
            error = null;
            var (series, rack, slot) = ParseConfig(Descriptor.DriverConfigJson);
            var client = new SiemensS7Net(series, Bus.Tcp.Host)
            {
                Rack = rack,
                Slot = slot,
                ConnectTimeOut = Math.Max(100, Descriptor.Params.ConnectTimeoutMs)
            };

            // HSL 的 SiemensS7Net 构造函数把 Port 写死成 102（S7 标准端口）。
            // 总线配置里既然给了端口，就必须覆盖——否则：
            //   · 界面上改端口"改了没有任何反应"（配置与行为不符，R33 同类缺陷）；
            //   · 联调工具/模拟器（如 tools\DeviceSimulator 的 10000-10009）永远连不上；
            //   · 现场走端口映射/防火墙转发时无法接入。
            // 端口非法（<=0）时保留 HSL 默认 102，非法值由描述符校验负责拦。
            if (Bus.Tcp.Port > 0) client.Port = Bus.Tcp.Port;

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

        private static (SiemensPLCS Series, byte Rack, byte Slot) ParseConfig(string? json)
        {
            var series = SiemensPLCS.S1200;
            byte rack = 0;
            byte slot = 1;
            if (string.IsNullOrWhiteSpace(json)) return (series, rack, slot);

            try
            {
                var obj = JObject.Parse(json);
                if (obj.TryGetValue("PlcSeries", StringComparison.OrdinalIgnoreCase, out var s) &&
                    Enum.TryParse<SiemensPLCS>(s.Value<string>(), true, out var parsedSeries))
                    series = parsedSeries;
                if (obj.TryGetValue("Rack", StringComparison.OrdinalIgnoreCase, out var r))
                    rack = r.Value<byte>();
                if (obj.TryGetValue("Slot", StringComparison.OrdinalIgnoreCase, out var sl))
                    slot = sl.Value<byte>();
            }
            catch
            {
                // 配置损坏时用默认值；UI 会提示具体错误。
            }
            return (series, rack, slot);
        }
    }

    [IoDriver("siemens.s7")]
    public sealed class SiemensS7IoDriverFactory : IIoDriverFactory
    {
        public string DriverId => "siemens.s7";
        public string DisplayName => "Siemens S7";
        public string Description => "西门子 S7 PLC（DB/M/I/Q 区）";
        public IoCapabilities Capabilities =>
            IoCapabilities.DigitalInput | IoCapabilities.DigitalOutput |
            IoCapabilities.AnalogInput | IoCapabilities.AnalogOutput;
        public Type? BusConfigType => typeof(TcpTransportConfig);
        public Type? DeviceConfigType => typeof(S7DeviceConfig);

        public IIoDevice Create(IoDeviceDescriptor descriptor, IoBusDescriptor bus, IIoRuntimeContext context)
            => new SiemensS7IoDevice(descriptor, bus, context);
    }

    public sealed class S7DeviceConfig
    {
        [UI.Attributes.SuperDisplay(Name = "PLC 系列", Group = new[] { "S7" }, Order = 1)]
        public SiemensPLCS PlcSeries { get; set; } = SiemensPLCS.S1200;

        [UI.Attributes.SuperDisplay(Name = "机架", Group = new[] { "S7" }, Order = 2)]
        public byte Rack { get; set; }

        [UI.Attributes.SuperDisplay(Name = "槽位", Group = new[] { "S7" }, Order = 3)]
        public byte Slot { get; set; } = 1;
    }
}
