using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Interfaces.Io;
using Newtonsoft.Json.Linq;

namespace Core.Interfaces.Io
{
    /// <summary>
    /// <para>IO 接入层 → 通讯层的配置折算（一次性、幂等、**纯数据**）。</para>
    ///
    /// <para><b>为什么合并</b>：IO 层与通讯层各自持有一条物理链路（各开一条 TCP / 一个 COM），
    /// 同一端点配了两边就会互抢端口。合并成一层之后物理链路只有一条，轮询调度也只有一套
    /// （连接 Worker 的读写分队列 = 唯一的仲裁点，跨层"插队优先"问题自然消失）。</para>
    ///
    /// <para><b>为什么产出"草稿"而不是直接造对象</b>：本类在 <c>Core.Interfaces</c> 里——
    /// 它是被 Core 与 Communication 共同引用的最底层程序集，<b>不能</b>引用
    /// <c>VisionMaster.Communications</c> 的 <c>CommunicationConfig</c> / <c>ModbusAddress</c>
    /// （依赖方向反过来会成环）。所以这里只产出"字段级草稿"，
    /// 由宿主侧（<c>IoMigrationApplier</c>，在 Communication 程序集里）翻译成真实对象。</para>
    ///
    /// <para><b>映射口径</b>：</para>
    /// <para>· 一条 <see cref="IoBusDescriptor"/> → 一条通讯连接（TCP → ModbusTcp；串口 → ModbusRtu）；</para>
    /// <para>· 一个 <see cref="IoPointDescriptor"/> → 一个网络变量，命名 <c>{设备Id}.{点位Id}</c>；</para>
    /// <para>· 站号（<c>DriverConfigJson.UnitId</c>）→ 逐帧站号（一条 COM 上的多从站靠它区分）；</para>
    /// <para>· 工程量换算（Scale/Offset/Unit）→ 换算参数；</para>
    /// <para>· 写共享（AllowSharedWrite / LeaseTimeoutMs）→ 写租约开关。</para>
    ///
    /// <para><b>幂等</b>：调用方传入"已有连接名"集合，同名连接跳过该总线。</para>
    /// <para><b>失败不阻断</b>：任何一条总线/点位折算失败只记消息、跳过它——
    /// 配置是现场资产，宁可留着让人对照修改，也不要让整个方案打不开。</para>
    /// </summary>
    public static class IoMigrationPlanner
    {
        /// <summary>迁移产生的连接名前缀（便于现场一眼看出这条连接是从 IO 折过来的）。</summary>
        public const string ConnectionNamePrefix = "IO:";

        /// <summary>一条待创建的通讯连接（字段级草稿）。</summary>
        public sealed class ConnectionDraft
        {
            public string ConnectionName = string.Empty;
            public string Description = string.Empty;

            /// <summary>true = TCP（ModbusTcp）；false = 串口（ModbusRtu）。</summary>
            public bool IsTcp;

            // ---- TCP ----
            public string Host = string.Empty;
            public int Port = 502;
            public int ConnectTimeoutMs = 3000;

            // ---- 串口 ----
            public string PortName = string.Empty;
            public int BaudRate = 9600;
            public int DataBits = 8;
            public IoParity Parity = IoParity.None;
            public IoStopBits StopBits = IoStopBits.One;

            // ---- 共用 ----
            public int ReadTimeoutMs = 1000;

            /// <summary>连接级轮询周期（取该总线上所有设备 PollGroups 的最短周期）。</summary>
            public int ReadCycleMs = 1000;

            /// <summary>本连接下要迁的变量。</summary>
            public List<VariableDraft> Variables { get; } = new();
        }

        /// <summary>一个待创建的网络变量（字段级草稿）。</summary>
        public sealed class VariableDraft
        {
            public string Name = string.Empty;
            public string Description = string.Empty;

            /// <summary>true = Bool；false = double（与 IO 点位的数字量/模拟量对应）。</summary>
            public bool IsBool;

            /// <summary>原始地址串（S7 走这个；Modbus 纯数字时也用这个兜底）。</summary>
            public string Address = string.Empty;

            // ---- Modbus 结构化地址（AddressIsStructured = false 时以下字段无效）----
            public bool AddressIsStructured;

            /// <summary>0=线圈 1=离散输入 2=保持寄存器 3=输入寄存器（与 ModbusArea 枚举同序）。</summary>
            public int ModbusAreaIndex;

            /// <summary>数据类型（0..12，与 Core 的 DataValueType 枚举同序；由应用器翻译）。</summary>
            public int DataTypeIndex = 2;   // 2 = Int16
            public int RawOffset;
            public int Station;

            // ---- 工程量换算 ----
            public bool EnableConversion;
            public double Scale = 1.0;
            public double EngineeringOffset;
            public string Unit = string.Empty;

            // ---- 写租约 ----
            public bool AllowSharedWrite;
            public int LeaseTimeoutMs;
        }

        /// <summary>
        /// 折算方案里的 IO 配置。
        /// </summary>
        /// <param name="buses">IO 总线</param>
        /// <param name="devices">IO 设备</param>
        /// <param name="existingConnectionNames">已有连接名（幂等判据，大小写不敏感）</param>
        /// <param name="messages">过程消息（跳过/失败原因）</param>
        public static List<ConnectionDraft> Plan(
            IEnumerable<IoBusDescriptor>? buses,
            IEnumerable<IoDeviceDescriptor>? devices,
            IEnumerable<string>? existingConnectionNames = null,
            ICollection<string>? messages = null)
        {
            var result = new List<ConnectionDraft>();
            if (buses == null || devices == null) return result;

            var deviceList = devices.Where(d => d != null).ToList();
            if (deviceList.Count == 0) return result;

            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in existingConnectionNames ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(name)) taken.Add(name.Trim());
            }

            foreach (var bus in buses)
            {
                if (bus == null || string.IsNullOrWhiteSpace(bus.BusId)) continue;

                var busDevices = deviceList
                    .Where(d => string.Equals(d.BusId, bus.BusId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (busDevices.Count == 0)
                {
                    messages?.Add($"总线 {bus.BusId} 上没有设备，跳过");
                    continue;
                }

                string connectionName = $"{ConnectionNamePrefix}{bus.BusId}";
                if (taken.Contains(connectionName))
                {
                    messages?.Add($"连接 {connectionName} 已存在，跳过该总线（迁移幂等）");
                    continue;
                }

                var draft = BuildConnection(bus, connectionName, messages);
                if (draft == null) continue;

                draft.ReadCycleMs = ResolveReadCycleMs(busDevices);

                foreach (var device in busDevices)
                {
                    if (!device.Enabled)
                    {
                        messages?.Add($"设备 {device.DeviceId} 已禁用，跳过");
                        continue;
                    }

                    AddDeviceVariables(draft, device, messages);
                }

                if (draft.Variables.Count == 0)
                {
                    messages?.Add($"总线 {bus.BusId} 折算后没有可用点位，不创建连接");
                    continue;
                }

                result.Add(draft);
                taken.Add(connectionName);
            }

            return result;
        }

        private static ConnectionDraft? BuildConnection(
            IoBusDescriptor bus, string connectionName, ICollection<string>? messages)
        {
            if (bus.TransportKind == IoTransportKind.Tcp)
            {
                var tcp = bus.Tcp;
                if (tcp == null || string.IsNullOrWhiteSpace(tcp.Host))
                {
                    messages?.Add($"总线 {bus.BusId} 无法迁移：TCP 主机为空");
                    return null;
                }

                return new ConnectionDraft
                {
                    ConnectionName = connectionName,
                    Description = $"由 IO 总线「{bus.Caption}」迁移",
                    IsTcp = true,
                    Host = tcp.Host.Trim(),
                    Port = tcp.Port > 0 ? tcp.Port : 502,
                    ConnectTimeoutMs = Math.Max(100, tcp.ConnectTimeoutMs),
                    ReadTimeoutMs = Math.Max(100, tcp.ReadTimeoutMs)
                };
            }

            var serial = bus.Serial;
            if (serial == null || string.IsNullOrWhiteSpace(serial.PortName))
            {
                messages?.Add($"总线 {bus.BusId} 无法迁移：串口名为空");
                return null;
            }

            return new ConnectionDraft
            {
                ConnectionName = connectionName,
                Description = $"由 IO 总线「{bus.Caption}」迁移",
                IsTcp = false,
                PortName = serial.PortName.Trim(),
                BaudRate = serial.BaudRate > 0 ? serial.BaudRate : 9600,
                DataBits = serial.DataBits > 0 ? serial.DataBits : 8,
                Parity = serial.Parity,
                StopBits = serial.StopBits,
                ReadTimeoutMs = Math.Max(100, serial.ReadTimeoutMs)
            };
        }

        private static void AddDeviceVariables(
            ConnectionDraft draft, IoDeviceDescriptor device, ICollection<string>? messages)
        {
            if (device.Points == null || device.Points.Count == 0) return;

            // 驱动 Id 决定地址语义：S7 走西门子语法（字符串路径），其余按 Modbus 结构化处理
            bool isS7 = device.DriverId != null &&
                        device.DriverId.Contains("s7", StringComparison.OrdinalIgnoreCase);

            var (unitId, _) = ParseModbusDriverConfig(device.DriverConfigJson);

            foreach (var point in device.Points)
            {
                if (point == null || !point.Enabled) continue;
                if (string.IsNullOrWhiteSpace(point.PointId)) continue;
                if (string.IsNullOrWhiteSpace(point.Address)) continue;

                var variable = new VariableDraft
                {
                    Name = $"{device.DeviceId}.{point.PointId}",
                    Description = BuildDescription(device, point),
                    IsBool = point.DataType == IoDataType.Bool,
                    Address = point.Address.Trim(),
                    AllowSharedWrite = point.AllowSharedWrite,
                    LeaseTimeoutMs = point.LeaseTimeoutMs
                };

                if (!isS7)
                {
                    if (int.TryParse(point.Address, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw))
                    {
                        bool digital = point.Kind is IoPointKind.DigitalInput or IoPointKind.DigitalOutput;
                        variable.AddressIsStructured = true;
                        // 索引与 Core 的 ModbusArea 枚举同序：Coils=0 / DiscreteInputs=1 /
                        // InputRegisters=2 / HoldingRegisters=3。
                        // ⚠ 保持寄存器是 **3** 不是 2（2 是输入寄存器）——按"功能码-1"想会错位一格，
                        // 那样模拟量会被当成输入寄存器读（只读区），写回必然失败。
                        variable.ModbusAreaIndex = digital
                            ? (point.Kind == IoPointKind.DigitalInput ? 1 : 0)   // 1=离散输入 / 0=线圈
                            : 3;                                                   // 3=保持寄存器
                        variable.DataTypeIndex = ToDataTypeIndex(point.DataType);
                        variable.RawOffset = raw;
                        // 站号逐帧生效：同一条 COM 上的多从站靠它区分（不写就全读默认站）
                        variable.Station = unitId;
                    }
                    else if (!point.Address.Contains(';'))
                    {
                        messages?.Add($"点位 {device.DeviceId}.{point.PointId} 的地址「{point.Address}」不是纯数字，跳过");
                        continue;
                    }
                }

                if (point.Scale != 1.0 || point.Offset != 0)
                {
                    variable.EnableConversion = true;
                    variable.Scale = point.Scale;
                    variable.EngineeringOffset = point.Offset;
                    variable.Unit = point.Unit ?? string.Empty;
                }

                draft.Variables.Add(variable);
            }
        }

        private static string BuildDescription(IoDeviceDescriptor device, IoPointDescriptor point)
        {
            string kind = point.Kind switch
            {
                IoPointKind.DigitalInput => "数字输入",
                IoPointKind.DigitalOutput => "数字输出",
                IoPointKind.AnalogInput => "模拟输入",
                IoPointKind.AnalogOutput => "模拟输出",
                _ => point.Kind.ToString()
            };
            string unit = string.IsNullOrWhiteSpace(point.Unit) ? string.Empty : $" {point.Unit}";
            string rw = point.IsWritable ? "可写" : "只读";
            return $"由 IO 迁移：{device.Caption} / {point.Caption}（{kind}，{rw}）{unit}".Trim();
        }

        private static int ToDataTypeIndex(IoDataType type) => type switch
        {
            IoDataType.Bool => 0,   // Boolean
            IoDataType.Int16 => 2,
            IoDataType.UInt16 => 3,
            IoDataType.Int32 => 4,
            IoDataType.UInt32 => 5,
            IoDataType.Float => 8,
            IoDataType.Double => 9,
            _ => 2
        };

        private static (byte UnitId, string ByteOrder) ParseModbusDriverConfig(string? json)
        {
            byte unitId = 1;
            string order = "CDAB";
            if (string.IsNullOrWhiteSpace(json)) return (unitId, order);

            try
            {
                var obj = JObject.Parse(json);
                if (obj.TryGetValue("UnitId", StringComparison.OrdinalIgnoreCase, out var unit))
                    unitId = unit.Value<byte>();
                if (obj.TryGetValue("ByteOrder", StringComparison.OrdinalIgnoreCase, out var bo))
                    order = bo.Value<string>() ?? order;
            }
            catch
            {
                // 配置损坏用默认值：迁移不该因为一个坏 JSON 就整体失败
            }
            return (unitId, order);
        }

        private static int ResolveReadCycleMs(IReadOnlyList<IoDeviceDescriptor> devices)
        {
            int min = int.MaxValue;
            foreach (var device in devices)
            {
                var groups = device.Params?.PollGroups;
                if (groups == null) continue;
                foreach (var group in groups)
                {
                    if (group == null || group.PeriodMs <= 0) continue;
                    if (group.PeriodMs < min) min = group.PeriodMs;
                }
            }
            // 下限 50ms：低于它的周期在通讯层会被过滤掉（PollScheduler.MinIntervalMs）
            return min == int.MaxValue ? 1000 : Math.Max(50, min);
        }
    }
}
