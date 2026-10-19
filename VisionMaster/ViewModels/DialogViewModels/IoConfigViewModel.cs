using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using Core.Interfaces.Io;
using HslCommunication.Profinet.Siemens;
using Newtonsoft.Json.Linq;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using UI.Attributes;
using VisionMaster.Binding;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.Services.Io;

namespace VisionMaster.ViewModels.DialogViewModels
{
    #region 链路参数（按传输类型切换）

    /// <summary>
    /// 链路参数基类。属性网格靠 <c>[PropertyItem]</c> 把它当作嵌套对象递归渲染，
    /// 遇到 <see cref="IoTransportKind"/> 变化时整表重建（<c>RequireRefresh</c>），
    /// 于是"选 TCP 只出 TCP 字段、选串口只出串口字段"——与通讯设置弹窗同一手法。
    /// </summary>
    public abstract class IoLinkEditModel : BindableBase
    {
        /// <summary>把当前编辑值写到描述符对应的链路配置上。</summary>
        public abstract void ApplyTo(IoBusDescriptor descriptor);
    }

    public sealed class TcpLinkEditModel : IoLinkEditModel
    {
        [SuperDisplay(Name = "IP 地址", Group = new[] { "TCP" }, Order = 1)]
        public string Host { get; set; } = "127.0.0.1";

        [SuperDisplay(Name = "端口", Group = new[] { "TCP" }, Order = 2)]
        public int Port { get; set; } = 502;

        [SuperDisplay(Name = "连接超时(ms)", Group = new[] { "TCP" }, Order = 3)]
        public int ConnectTimeoutMs { get; set; } = 3000;

        [SuperDisplay(Name = "读超时(ms)", Group = new[] { "TCP" }, Order = 4)]
        public int ReadTimeoutMs { get; set; } = 1000;

        public override void ApplyTo(IoBusDescriptor descriptor)
        {
            descriptor.Tcp = new TcpTransportConfig
            {
                Host = Host,
                Port = Port,
                ConnectTimeoutMs = ConnectTimeoutMs,
                ReadTimeoutMs = ReadTimeoutMs
            };
        }
    }

    public sealed class SerialLinkEditModel : IoLinkEditModel
    {
        [SuperDisplay(Name = "串口", Group = new[] { "串口" }, Order = 1)]
        public string PortName { get; set; } = "COM1";

        [SuperDisplay(Name = "波特率", Group = new[] { "串口" }, Order = 2)]
        public int BaudRate { get; set; } = 9600;

        [SuperDisplay(Name = "数据位", Group = new[] { "串口" }, Order = 3)]
        public int DataBits { get; set; } = 8;

        [SuperDisplay(Name = "校验", Group = new[] { "串口" }, Order = 4)]
        public IoParity Parity { get; set; } = IoParity.None;

        [SuperDisplay(Name = "停止位", Group = new[] { "串口" }, Order = 5)]
        public IoStopBits StopBits { get; set; } = IoStopBits.One;

        [SuperDisplay(Name = "读超时(ms)", Group = new[] { "串口" }, Order = 6)]
        public int ReadTimeoutMs { get; set; } = 1000;

        public override void ApplyTo(IoBusDescriptor descriptor)
        {
            descriptor.Serial = new SerialTransportConfig
            {
                PortName = PortName,
                BaudRate = BaudRate,
                DataBits = DataBits,
                Parity = Parity,
                StopBits = StopBits,
                ReadTimeoutMs = ReadTimeoutMs
            };
        }
    }

    #endregion

    #region 驱动私有参数（按 driverId 切换）

    /// <summary>驱动私有参数基类。同样靠 <c>[PropertyItem]</c> 嵌套渲染。</summary>
    public abstract class IoDriverEditModel : BindableBase
    {
        /// <summary>写成驱动配置 JSON（驱动自己解析，核心不碰）。</summary>
        public abstract string ToJson();
    }

    public sealed class SimulatedDriverEditModel : IoDriverEditModel
    {
        [SuperDisplay(Name = "说明", Group = new[] { "模拟" }, Order = 1, IsReadOnly = true)]
        public string Note { get; set; } = "内存模拟设备：无真实链路，用于无硬件联调与契约测试";

        public override string ToJson() => "{}";
    }

    public sealed class ModbusDriverEditModel : IoDriverEditModel
    {
        [SuperDisplay(Name = "站号", Group = new[] { "Modbus" }, Order = 1)]
        public byte UnitId { get; set; } = 1;

        [SuperDisplay(Name = "字序", Group = new[] { "Modbus" }, Order = 2)]
        public IoByteOrder ByteOrder { get; set; } = IoByteOrder.CDAB;

        /// <summary>
        /// 地址基数：true = 0 基（HSL 默认，"40001" 是第 2 个寄存器）；false = 1 基（PLC 习惯，"40001" 是第 1 个）。
        /// 默认 true 与既有行为一致；按 PLC 变量表配置时必须关掉，否则会静默错位一个寄存器。
        /// </summary>
        [SuperDisplay(
            Name = "地址从 0 起",
            Group = new[] { "Modbus" },
            Order = 3,
            Description = "勾选=0 基（HSL 默认，40001 是第 2 个寄存器）；取消=1 基（PLC 习惯，40001 是第 1 个）")]
        public bool AddressStartWithZero { get; set; } = true;

        public override string ToJson() => new JObject
        {
            ["UnitId"] = UnitId,
            ["ByteOrder"] = ByteOrder.ToString(),
            ["AddressStartWithZero"] = AddressStartWithZero
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    public sealed class S7DriverEditModel : IoDriverEditModel
    {
        [SuperDisplay(Name = "PLC 系列", Group = new[] { "S7" }, Order = 1)]
        public SiemensPLCS PlcSeries { get; set; } = SiemensPLCS.S1200;

        [SuperDisplay(Name = "机架", Group = new[] { "S7" }, Order = 2)]
        public byte Rack { get; set; }

        [SuperDisplay(Name = "槽位", Group = new[] { "S7" }, Order = 3)]
        public byte Slot { get; set; } = 1;

        public override string ToJson() => new JObject
        {
            ["PlcSeries"] = PlcSeries.ToString(),
            ["Rack"] = Rack,
            ["Slot"] = Slot
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    #endregion

    /// <summary>
    /// 总线编辑模型。
    ///
    /// 关键：链路参数（TCP / 串口）**不是**平铺在本类上，而是放在 <see cref="Transport"/>
    /// 这个嵌套对象里。属性网格遇到 <c>[PropertyItem]</c> 会递归渲染子对象，
    /// 切换 <see cref="TransportKind"/>（<c>RequireRefresh</c>）时整表重建 →
    /// 界面上只出现当前传输类型的字段。平铺写法的后果是"串口和 TCP 参数一起暴露"
    /// （2026-10-18 真机反馈）。
    /// </summary>
    public sealed class IoBusEditModel : BindableBase
    {
        private string _busId = "BUS_1";
        private string _displayName = "IO 总线";
        private IoTransportKind _transportKind = IoTransportKind.Tcp;
        private int _maxPollFrameTimeMs = 50;
        private IoLinkEditModel _transport = new TcpLinkEditModel();
        private bool _reconnectEnabled = true;
        private int _baseIntervalMs = 1000;
        private int _maxIntervalMs = 30000;
        private int _maxAttempts;

        /// <summary>两种链路的草稿：来回切换传输类型时不丢用户已填的参数。</summary>
        private TcpLinkEditModel _tcpDraft = new();
        private SerialLinkEditModel _serialDraft = new();

        [SuperDisplay(Name = "总线标识", Group = new[] { "总线" }, Order = 1)]
        public string BusId
        {
            get => _busId;
            set => SetProperty(ref _busId, value);
        }

        /// <summary>列表行主标题：标识 + 显示名（显示名可空）。</summary>
        public string NameText => string.IsNullOrWhiteSpace(DisplayName) ? BusId : $"{BusId} · {DisplayName}";

        /// <summary>列表行次要说明：链路端点摘要（TCP host:port / 串口 COMx @ baud）。</summary>
        public string DetailText => Transport switch
        {
            TcpLinkEditModel tcp => $"TCP {tcp.Host}:{tcp.Port}",
            SerialLinkEditModel serial => $"串口 {serial.PortName} @ {serial.BaudRate}",
            _ => string.Empty
        };

        [SuperDisplay(Name = "显示名", Group = new[] { "总线" }, Order = 2)]
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        /// <summary>
        /// 传输类型。<c>RequireRefresh = true</c> 让属性网格在切换后整表重建，
        /// 从而只渲染新传输类型对应的链路字段。
        /// </summary>
        [SuperDisplay(Name = "传输类型", Group = new[] { "总线" }, Order = 3, RequireRefresh = true)]
        public IoTransportKind TransportKind
        {
            get => _transportKind;
            set
            {
                if (!SetProperty(ref _transportKind, value)) return;
                Transport = value == IoTransportKind.Serial
                    ? SwapToSerial()
                    : SwapToTcp();
            }
        }

        [SuperDisplay(Name = "单帧最大时间(ms)", Group = new[] { "总线" }, Order = 4)]
        public int MaxPollFrameTimeMs
        {
            get => _maxPollFrameTimeMs;
            set => SetProperty(ref _maxPollFrameTimeMs, value);
        }

        /// <summary>
        /// 链路参数（嵌套对象，按 <see cref="TransportKind"/> 决定具体类型）。
        /// <c>[PropertyItem]</c> 是"向下递归"的声明，与 <c>CommunicationConfig.Config</c> 同一手法。
        /// </summary>
        [SuperDisplay(Name = "链路参数", Group = new[] { "链路配置" }, Order = 1)]
        [PropertyItem(Type = typeof(System.Windows.Controls.Control))]
        public IoLinkEditModel Transport
        {
            get => _transport;
            private set => SetProperty(ref _transport, value);
        }

        [SuperDisplay(Name = "自动重连", Group = new[] { "重连" }, Order = 1)]
        public bool ReconnectEnabled
        {
            get => _reconnectEnabled;
            set => SetProperty(ref _reconnectEnabled, value);
        }

        [SuperDisplay(Name = "基础间隔(ms)", Group = new[] { "重连" }, Order = 2)]
        public int BaseIntervalMs
        {
            get => _baseIntervalMs;
            set => SetProperty(ref _baseIntervalMs, value);
        }

        [SuperDisplay(Name = "最大间隔(ms)", Group = new[] { "重连" }, Order = 3)]
        public int MaxIntervalMs
        {
            get => _maxIntervalMs;
            set => SetProperty(ref _maxIntervalMs, value);
        }

        [SuperDisplay(Name = "最大次数(0=无限)", Group = new[] { "重连" }, Order = 4)]
        public int MaxAttempts
        {
            get => _maxAttempts;
            set => SetProperty(ref _maxAttempts, value);
        }

        private IoLinkEditModel SwapToTcp()
        {
            if (_transport is TcpLinkEditModel tcp) _tcpDraft = tcp;
            return _tcpDraft;
        }

        private IoLinkEditModel SwapToSerial()
        {
            if (_transport is SerialLinkEditModel serial) _serialDraft = serial;
            return _serialDraft;
        }

        public static IoBusEditModel From(IoBusDescriptor d)
        {
            var model = new IoBusEditModel
            {
                BusId = d.BusId,
                DisplayName = d.DisplayName,
                _transportKind = d.TransportKind,
                MaxPollFrameTimeMs = d.MaxPollFrameTimeMs,
                _reconnectEnabled = d.Reconnect.Enabled,
                _baseIntervalMs = d.Reconnect.BaseIntervalMs,
                _maxIntervalMs = d.Reconnect.MaxIntervalMs,
                _maxAttempts = d.Reconnect.MaxAttempts
            };

            model._tcpDraft = new TcpLinkEditModel
            {
                Host = d.Tcp.Host,
                Port = d.Tcp.Port,
                ConnectTimeoutMs = d.Tcp.ConnectTimeoutMs,
                ReadTimeoutMs = d.Tcp.ReadTimeoutMs
            };
            model._serialDraft = new SerialLinkEditModel
            {
                PortName = d.Serial.PortName,
                BaudRate = d.Serial.BaudRate,
                DataBits = d.Serial.DataBits,
                Parity = d.Serial.Parity,
                StopBits = d.Serial.StopBits,
                ReadTimeoutMs = d.Serial.ReadTimeoutMs
            };
            model._transport = d.TransportKind == IoTransportKind.Serial
                ? model._serialDraft
                : (IoLinkEditModel)model._tcpDraft;

            return model;
        }

        public IoBusDescriptor ToDescriptor(Guid id)
        {
            var descriptor = new IoBusDescriptor
            {
                Id = id,
                BusId = BusId,
                DisplayName = DisplayName,
                TransportKind = TransportKind,
                MaxPollFrameTimeMs = MaxPollFrameTimeMs,
                Reconnect = new IoReconnectPolicy
                {
                    Enabled = ReconnectEnabled,
                    BaseIntervalMs = BaseIntervalMs,
                    MaxIntervalMs = MaxIntervalMs,
                    MaxAttempts = MaxAttempts
                }
            };

            // 两种链路都写回：只写当前那种会让"切走再切回"丢参数，
            // 而描述符里两套配置本来就是并存字段（运行期只看 TransportKind）。
            _tcpDraft.ApplyTo(descriptor);
            _serialDraft.ApplyTo(descriptor);
            return descriptor;
        }
    }

    /// <summary>
    /// 设备编辑模型。驱动私有参数放在 <see cref="DriverConfig"/> 嵌套对象里，
    /// 按 <see cref="DriverId"/> 切换具体类型（同理：平铺会导致 Modbus 与 S7 字段同时出现）。
    /// </summary>
    public sealed class IoDeviceEditModel : BindableBase
    {
        private string _deviceId = "IO_1";
        private string _displayName = "IO 设备";
        private string _busId = "BUS_1";
        private string _driverId = "simulated";
        private bool _enabled = true;
        private bool _autoConnect = true;
        private IoDriverEditModel _driverConfig = new SimulatedDriverEditModel();

        private readonly Dictionary<string, IoDriverEditModel> _driverDrafts =
            new(StringComparer.OrdinalIgnoreCase);

        [SuperDisplay(Name = "设备标识", Group = new[] { "设备" }, Order = 1)]
        public string DeviceId
        {
            get => _deviceId;
            set => SetProperty(ref _deviceId, value);
        }

        [SuperDisplay(Name = "显示名", Group = new[] { "设备" }, Order = 2)]
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        [SuperDisplay(Name = "所属总线", Group = new[] { "设备" }, Order = 3)]
        public string BusId
        {
            get => _busId;
            set => SetProperty(ref _busId, value);
        }

        /// <summary>列表行主标题：标识 + 显示名（显示名可空）。</summary>
        public string NameText => string.IsNullOrWhiteSpace(DisplayName) ? DeviceId : $"{DeviceId} · {DisplayName}";

        /// <summary>列表行次要说明：驱动 + 所属总线。</summary>
        public string DetailText => string.IsNullOrWhiteSpace(DriverId)
            ? $"总线 {BusId}"
            : $"{DriverId} · 总线 {BusId}";

        /// <summary>
        /// 驱动标识。<c>RequireRefresh = true</c>：换驱动后整表重建，
        /// 只显示该驱动的私有参数（Modbus 只出站号/字序，S7 只出系列/机架/槽位）。
        /// </summary>
        [SuperDisplay(Name = "驱动", Group = new[] { "设备" }, Order = 4, RequireRefresh = true)]
        public string DriverId
        {
            get => _driverId;
            set
            {
                if (!SetProperty(ref _driverId, value)) return;
                DriverConfig = CreateDriverConfig(value);
            }
        }

        [SuperDisplay(Name = "启用", Group = new[] { "设备" }, Order = 5)]
        public bool Enabled
        {
            get => _enabled;
            set => SetProperty(ref _enabled, value);
        }

        [SuperDisplay(Name = "自动连接", Group = new[] { "设备" }, Order = 6)]
        public bool AutoConnect
        {
            get => _autoConnect;
            set => SetProperty(ref _autoConnect, value);
        }

        [SuperDisplay(Name = "驱动参数", Group = new[] { "驱动配置" }, Order = 1)]
        [PropertyItem(Type = typeof(System.Windows.Controls.Control))]
        public IoDriverEditModel DriverConfig
        {
            get => _driverConfig;
            private set => SetProperty(ref _driverConfig, value);
        }

        private IoDriverEditModel CreateDriverConfig(string driverId)
        {
            var key = (driverId ?? string.Empty).Trim();

            // 已切换过的驱动：复用草稿（来回切换不丢已填参数）
            if (_driverDrafts.TryGetValue(key, out var draft))
                return draft;

            IoDriverEditModel created;
            if (key.StartsWith("modbus", StringComparison.OrdinalIgnoreCase))
                created = new ModbusDriverEditModel();
            else if (string.Equals(key, "siemens.s7", StringComparison.OrdinalIgnoreCase))
                created = new S7DriverEditModel();
            else
                created = new SimulatedDriverEditModel();

            _driverDrafts[key] = created;
            return created;
        }

        public static IoDeviceEditModel From(IoDeviceDescriptor d)
        {
            var model = new IoDeviceEditModel
            {
                DeviceId = d.DeviceId,
                DisplayName = d.DisplayName,
                BusId = d.BusId,
                _driverId = d.DriverId,
                Enabled = d.Enabled,
                AutoConnect = d.AutoConnect
            };

            var config = model.CreateDriverConfig(d.DriverId);

            // 驱动的 JSON 里可能有当前类型还不认识的字段（驱动升级/降级）；解析失败就保持默认值，
            // 原文由 ToDescriptor 之外的地方负责保留（见 DriverConfigJson 的兼容说明）。
            try
            {
                var obj = JObject.Parse(string.IsNullOrWhiteSpace(d.DriverConfigJson) ? "{}" : d.DriverConfigJson);
                if (config is ModbusDriverEditModel modbus)
                {
                    if (obj.TryGetValue("UnitId", StringComparison.OrdinalIgnoreCase, out var unit))
                        modbus.UnitId = unit.Value<byte>();
                    if (obj.TryGetValue("ByteOrder", StringComparison.OrdinalIgnoreCase, out var order) &&
                        Enum.TryParse<IoByteOrder>(order.Value<string>(), true, out var parsedOrder))
                        modbus.ByteOrder = parsedOrder;
                    if (obj.TryGetValue("AddressStartWithZero", StringComparison.OrdinalIgnoreCase, out var zero))
                        modbus.AddressStartWithZero = zero.Value<bool>();
                }
                else if (config is S7DriverEditModel s7)
                {
                    if (obj.TryGetValue("PlcSeries", StringComparison.OrdinalIgnoreCase, out var series) &&
                        Enum.TryParse<SiemensPLCS>(series.Value<string>(), true, out var parsedSeries))
                        s7.PlcSeries = parsedSeries;
                    if (obj.TryGetValue("Rack", StringComparison.OrdinalIgnoreCase, out var rack))
                        s7.Rack = rack.Value<byte>();
                    if (obj.TryGetValue("Slot", StringComparison.OrdinalIgnoreCase, out var slot))
                        s7.Slot = slot.Value<byte>();
                }
            }
            catch
            {
                // 配置损坏：保留默认值，应用时由驱动 Validate 报错。
            }

            model._driverConfig = config;
            return model;
        }

        public IoDeviceDescriptor ToDescriptor(Guid id, IEnumerable<IoPointDescriptor> points) => new()
        {
            Id = id,
            DeviceId = DeviceId,
            DisplayName = DisplayName,
            BusId = BusId,
            DriverId = DriverId,
            Enabled = Enabled,
            AutoConnect = AutoConnect,
            DriverConfigJson = DriverConfig.ToJson(),
            Points = points.ToList()
        };
    }

public sealed class IoPointEditModel : BindableBase
    {
        [SuperDisplay(Name = "点位标识", Group = new[] { "点位" }, Order = 1)]
        public string PointId { get; set; } = "DI_1";

        [SuperDisplay(Name = "显示名", Group = new[] { "点位" }, Order = 2)]
        public string DisplayName { get; set; } = "IO 点";

        [SuperDisplay(Name = "类型", Group = new[] { "点位" }, Order = 3)]
        public IoPointKind Kind { get; set; } = IoPointKind.DigitalInput;

        [SuperDisplay(Name = "地址", Group = new[] { "点位" }, Order = 4)]
        public string Address { get; set; } = "00001";

        [SuperDisplay(Name = "数据类型", Group = new[] { "点位" }, Order = 5)]
        public IoDataType DataType { get; set; } = IoDataType.Bool;

        [SuperDisplay(Name = "访问权限", Group = new[] { "点位" }, Order = 6)]
        public IoAccess Access { get; set; } = IoAccess.ReadOnly;

        [SuperDisplay(Name = "缩放", Group = new[] { "模拟量" }, Order = 10)]
        public double Scale { get; set; } = 1.0;

        [SuperDisplay(Name = "偏移", Group = new[] { "模拟量" }, Order = 11)]
        public double Offset { get; set; }

        [SuperDisplay(Name = "单位", Group = new[] { "模拟量" }, Order = 12)]
        public string Unit { get; set; } = string.Empty;

        [SuperDisplay(Name = "死区", Group = new[] { "模拟量" }, Order = 13)]
        public double Deadband { get; set; }

        [SuperDisplay(Name = "防抖(ms)", Group = new[] { "数字量" }, Order = 20)]
        public int DebounceMs { get; set; }

        [SuperDisplay(Name = "故障安全", Group = new[] { "安全" }, Order = 30)]
        public IoFailSafe FailSafe { get; set; } = IoFailSafe.Hold;

        [SuperDisplay(Name = "安全值", Group = new[] { "安全" }, Order = 31)]
        public string SafeValue { get; set; } = string.Empty;

        [SuperDisplay(Name = "允许手动写", Group = new[] { "安全" }, Order = 32)]
        public bool AllowManualWrite { get; set; } = true;

        [SuperDisplay(Name = "启用", Group = new[] { "点位" }, Order = 40)]
        public bool Enabled { get; set; } = true;

        [SuperDisplay(Name = "说明", Group = new[] { "点位" }, Order = 41)]
        public string Description { get; set; } = string.Empty;

        public static IoPointEditModel From(IoPointDescriptor d) => new()
        {
            PointId = d.PointId,
            DisplayName = d.DisplayName,
            Kind = d.Kind,
            Address = d.Address,
            DataType = d.DataType,
            Access = d.Access,
            Scale = d.Scale,
            Offset = d.Offset,
            Unit = d.Unit,
            Deadband = d.Deadband,
            DebounceMs = d.DebounceMs,
            FailSafe = d.FailSafe,
            SafeValue = d.SafeValue,
            AllowManualWrite = d.AllowManualWrite,
            Enabled = d.Enabled,
            Description = d.Description
        };

        public IoPointDescriptor ToDescriptor() => new()
        {
            PointId = PointId,
            DisplayName = DisplayName,
            Kind = Kind,
            Address = Address,
            DataType = DataType,
            Access = Access,
            Scale = Scale,
            Offset = Offset,
            Unit = Unit,
            Deadband = Deadband,
            DebounceMs = DebounceMs,
            FailSafe = FailSafe,
            SafeValue = SafeValue,
            AllowManualWrite = AllowManualWrite,
            Enabled = Enabled,
            Description = Description
        };
    }

    public sealed class IoConfigViewModel : BindableBase, IDialogAware
    {
        private readonly IWorkspaceManager _workspace;
        private readonly IoProvider _provider;
        private readonly IoVariableCatalog _variables;
        private readonly IUserNotifier _notifier;
        private readonly IoDriverRegistry _registry;

        private IoBusEditModel? _selectedBus;
        private IoDeviceEditModel? _selectedDevice;
        private IoPointEditModel? _selectedPoint;

        public IoConfigViewModel(
            IWorkspaceManager workspace,
            IoProvider provider,
            IoVariableCatalog variables,
            IUserNotifier notifier,
            IoDriverRegistry registry)
        {
            _workspace = workspace;
            _provider = provider;
            _variables = variables;
            _notifier = notifier;
            _registry = registry;

            RefreshDriverOptions();

            AddBusCommand = new DelegateCommand(AddBus);
            AddDeviceCommand = new DelegateCommand(AddDevice);
            AddPointCommand = new DelegateCommand(AddPoint);
            RemoveCommand = new DelegateCommand(RemoveSelected);
            ApplyCommand = new DelegateCommand(Apply);
            TestCommand = new DelegateCommand(TestSelectedDevice);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke());
        }

        public string Title => "IO 设置";

        public ObservableCollection<IoBusEditModel> Buses { get; } = new();
        public ObservableCollection<IoDeviceEditModel> Devices { get; } = new();
        public ObservableCollection<IoPointEditModel> Points { get; } = new();

        /// <summary>
        /// 点位表的显示集合（= <see cref="Points"/> 经搜索过滤后的结果）。
        /// <para>为什么不让界面直接绑 <see cref="Points"/>：点位数上百时，用户只能靠滚动找；
        /// 搜索必须只影响"看得见哪些"，不能影响"保存哪些"——写回方案仍然走完整的 <see cref="Points"/>。</para>
        /// </summary>
        public ObservableCollection<IoPointEditModel> FilteredPoints { get; } = new();

        private string _pointSearch = string.Empty;
        /// <summary>点位搜索词（匹配点位标识 / 显示名 / 地址 / 说明，大小写不敏感）。</summary>
        public string PointSearch
        {
            get => _pointSearch;
            set
            {
                if (SetProperty(ref _pointSearch, value))
                    ApplyPointFilter();
            }
        }

        /// <summary>点位计数提示：有搜索时显示"命中 / 总数"，让用户知道还有多少被藏起来了。</summary>
        public string PointsSummary => Points.Count == 0
            ? "无点位"
            : FilteredPoints.Count == Points.Count
                ? $"{Points.Count} 个点位"
                : $"{FilteredPoints.Count} / {Points.Count} 个点位";
        public ObservableCollection<IoDriverInfo> DriverOptions { get; } = new();

        public IoBusEditModel? SelectedBus
        {
            get => _selectedBus;
            set
            {
                if (SetProperty(ref _selectedBus, value))
                {
                    // 总线换了，能挂的驱动也换了：TCP 总线只出 TCP 驱动，串口总线只出串口驱动。
                    // 不过滤的话，用户在 TCP 总线上能选到 Modbus RTU，配置能存但永远连不上。
                    RefreshDriverOptions();

                    SelectedDevice = null;
                    RaisePropertyChanged(nameof(SelectedObject));
                }
            }
        }

        /// <summary>
        /// 按当前总线的传输类型重建驱动候选。
        /// <para><c>BusConfigType == null</c> 的驱动（模拟器）与任何总线兼容。</para>
        /// </summary>
        private void RefreshDriverOptions()
        {
            DriverOptions.Clear();

            foreach (var driver in _registry.All)
            {
                if (!IsDriverCompatible(driver.BusConfigType, SelectedBus?.TransportKind)) continue;
                DriverOptions.Add(new IoDriverInfo(
                    driver.DriverId, driver.GetType(), driver.DisplayName, driver.Description, 1));
            }
        }

        private static bool IsDriverCompatible(Type? busConfigType, IoTransportKind? transportKind)
        {
            if (busConfigType == null) return true;

            return transportKind switch
            {
                IoTransportKind.Tcp => busConfigType == typeof(TcpTransportConfig),
                IoTransportKind.Serial => busConfigType == typeof(SerialTransportConfig),
                _ => false
            };
        }

        public IoDeviceEditModel? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (ReferenceEquals(_selectedDevice, value)) return;

                // 切设备前先把当前设备的点位编辑写回方案，避免“改完点位切走就丢”。
                CommitPointsToSolution();

                if (SetProperty(ref _selectedDevice, value))
                {
                    LoadPoints();
                    SelectedPoint = null;
                    RaisePropertyChanged(nameof(SelectedObject));
                }
            }
        }

        private void CommitPointsToSolution()
        {
            if (_selectedDevice == null) return;
            var solution = _workspace.CurrentSolution;
            var device = solution?.IoDevices?.FirstOrDefault(d =>
                string.Equals(d.DeviceId, _selectedDevice.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (device != null)
                device.Points = Points.Select(p => p.ToDescriptor()).ToList();
        }

        public IoPointEditModel? SelectedPoint
        {
            get => _selectedPoint;
            set
            {
                if (SetProperty(ref _selectedPoint, value))
                    RaisePropertyChanged(nameof(SelectedObject));
            }
        }

        public object? SelectedObject => (object?)SelectedPoint ?? (object?)SelectedDevice ?? SelectedBus;

        public DelegateCommand AddBusCommand { get; }
        public DelegateCommand AddDeviceCommand { get; }
        public DelegateCommand AddPointCommand { get; }
        public DelegateCommand RemoveCommand { get; }
        public DelegateCommand ApplyCommand { get; }
        public DelegateCommand TestCommand { get; }
        public DelegateCommand CloseCommand { get; }

        public DialogCloseListener RequestClose { get; set; }

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            Buses.Clear();
            Devices.Clear();
            Points.Clear();
            PointSearch = string.Empty;
            ApplyPointFilter();

            var solution = _workspace.CurrentSolution;
            if (solution == null) return;

            foreach (var bus in solution.IoBuses ?? new List<IoBusDescriptor>())
                Buses.Add(IoBusEditModel.From(bus));
            foreach (var device in solution.IoDevices ?? new List<IoDeviceDescriptor>())
                Devices.Add(IoDeviceEditModel.From(device));

            SelectedBus = Buses.FirstOrDefault();
        }

        private void LoadPoints()
        {
            Points.Clear();
            ApplyPointFilter();
            if (SelectedDevice == null) return;

            var solution = _workspace.CurrentSolution;
            var device = solution?.IoDevices?.FirstOrDefault(d =>
                string.Equals(d.DeviceId, SelectedDevice.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (device == null) return;

            foreach (var point in device.Points)
                Points.Add(IoPointEditModel.From(point));

            ApplyPointFilter();
        }

        /// <summary>
        /// 按搜索词重建显示集合。
        /// <para>选中项被过滤掉时必须清空选中：否则属性网格还在编辑一个"列表里看不见"的点，
        /// 用户改完不知道改的是哪一个。</para>
        /// </summary>
        private void ApplyPointFilter()
        {
            string query = PointSearch?.Trim() ?? string.Empty;
            var selected = SelectedPoint;

            FilteredPoints.Clear();
            foreach (var point in Points)
            {
                if (MatchesPoint(point, query))
                    FilteredPoints.Add(point);
            }

            if (selected != null && !FilteredPoints.Contains(selected))
                SelectedPoint = null;

            RaisePropertyChanged(nameof(PointsSummary));
        }

        private static bool MatchesPoint(IoPointEditModel point, string query)
            => query.Length == 0
               || Contains(point.PointId, query)
               || Contains(point.DisplayName, query)
               || Contains(point.Address, query)
               || Contains(point.Description, query);

        private static bool Contains(string? value, string query)
            => !string.IsNullOrEmpty(value)
               && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        private void AddBus()
        {
            var bus = new IoBusEditModel { BusId = NextId(Buses.Select(b => b.BusId), "BUS_") };
            Buses.Add(bus);
            SelectedBus = bus;
        }

        private void AddDevice()
        {
            if (Buses.Count == 0) AddBus();
            var device = new IoDeviceEditModel
            {
                DeviceId = NextId(Devices.Select(d => d.DeviceId), "IO_"),
                BusId = SelectedBus?.BusId ?? Buses[0].BusId,
                // 默认驱动取当前总线上第一个可用的：TCP 总线上不会默认成 Modbus RTU。
                DriverId = DriverOptions.FirstOrDefault()?.DriverId ?? string.Empty
            };
            Devices.Add(device);
            SelectedDevice = device;
        }

        private void AddPoint()
        {
            if (SelectedDevice == null)
            {
                _notifier.ShowWarn("请先选择一个设备");
                return;
            }

            var point = new IoPointEditModel
            {
                PointId = NextId(Points.Select(p => p.PointId), "P_")
            };

            // 新建点位通常不匹配当前搜索词（如搜索 "DO_" 时新建的是 "P_3"）：
            // 先清掉搜索，否则刚建的点位立刻被过滤掉，用户以为"没建成功"。
            if (!string.IsNullOrEmpty(PointSearch))
                PointSearch = string.Empty;

            Points.Add(point);
            ApplyPointFilter();
            SelectedPoint = point;
        }

        private void RemoveSelected()
        {
            if (SelectedPoint != null)
            {
                Points.Remove(SelectedPoint);
                SelectedPoint = null;
                ApplyPointFilter();
                return;
            }
            if (SelectedDevice != null)
            {
                Devices.Remove(SelectedDevice);
                SelectedDevice = null;
                return;
            }
            if (SelectedBus != null)
            {
                Buses.Remove(SelectedBus);
                SelectedBus = null;
            }
        }

        private void Apply()
        {
            var solution = _workspace.CurrentSolution;
            if (solution == null) return;

            var oldBusIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var bus in solution.IoBuses ?? new List<IoBusDescriptor>())
                oldBusIds[bus.BusId] = bus.Id;

            var oldDeviceIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in solution.IoDevices ?? new List<IoDeviceDescriptor>())
                oldDeviceIds[device.DeviceId] = device.Id;

            var buses = Buses.Select(b =>
                b.ToDescriptor(oldBusIds.TryGetValue(b.BusId, out var id) ? id : Guid.NewGuid())).ToList();

            var devices = new List<IoDeviceDescriptor>();
            foreach (var device in Devices)
            {
                var points = string.Equals(device.DeviceId, SelectedDevice?.DeviceId, StringComparison.OrdinalIgnoreCase)
                    ? Points.Select(p => p.ToDescriptor()).ToList()
                    : solution.IoDevices?.FirstOrDefault(d =>
                        string.Equals(d.DeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase))?.Points
                      ?? new List<IoPointDescriptor>();

                devices.Add(device.ToDescriptor(
                    oldDeviceIds.TryGetValue(device.DeviceId, out var id) ? id : Guid.NewGuid(),
                    points));
            }

            solution.IoBuses = buses;
            solution.IoDevices = devices;

            _provider.Apply(buses, devices, autoStart: true);
            _variables.Rebuild();

            _notifier.ShowInfo("IO 配置已应用（请保存方案以落盘）");
        }

        private void TestSelectedDevice()
        {
            if (SelectedDevice == null)
            {
                _notifier.ShowWarn("请先选择一个设备");
                return;
            }

            if (!_provider.TryGetDevice(SelectedDevice.DeviceId, out var device))
            {
                _notifier.ShowWarn("设备尚未应用，请先点“应用”");
                return;
            }

            if (!device.Connect())
            {
                _notifier.ShowWarn($"连接失败：{device.StateDetail}");
                return;
            }

            var point = device.Descriptor.Points.FirstOrDefault(p => p.Enabled);
            if (point == null)
            {
                _notifier.ShowInfo("已连接（未配置点位）");
                return;
            }

            var value = device.ReadNow(point.PointId, 1000, CancellationToken.None, out var error);
            if (error != null)
                _notifier.ShowWarn($"已连接，但读点失败：{error}");
            else
                _notifier.ShowInfo($"已连接，{point.PointId} = {value.Numeric}");
        }

        private static string NextId(IEnumerable<string> existing, string prefix)
        {
            int index = 1;
            var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            while (set.Contains(prefix + index)) index++;
            return prefix + index;
        }
    }
}
