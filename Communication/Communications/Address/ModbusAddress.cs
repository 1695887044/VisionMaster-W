using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>Modbus 地址配置。</para>
    /// <para>生成的地址串为 HSL 富地址格式 "x=功能码;0基址"（如 "x=3;100"），原因：</para>
    /// <para>1) 旧 "40001" 约定会被 HSL 当作寄存器号 40001 字面解析——静默读错位置；</para>
    /// <para>2) 旧 "40001.1" 位格式 HSL 无法解析——读失败。</para>
    /// <para>读写两条链路都吃富地址（已核对 HSL：写时 x=3 自动转 16、x=1 自动转 5）。</para>
    /// </summary>
    /// <summary>
    /// <para>HSL Modbus 富地址的轻量解析（只取"功能码"这一项，够 ReadBits 分 FC1/FC2 用）。</para>
    /// <para>为什么不能用 <c>StartsWith("x=2;")</c>：带站号的地址是 <c>s=2;x=2;100</c>，
    /// 前缀不在开头 → 离散输入被当成线圈发 FC1，读回来的是另一片区域（静默读错值）。</para>
    /// </summary>
    public static class ModbusRichAddress
    {
        /// <summary>取富地址里的功能码；地址里没有 <c>x=</c> 段时返回 false。</summary>
        public static bool TryGetFunctionCode(string? address, out int functionCode)
        {
            functionCode = 0;
            if (string.IsNullOrWhiteSpace(address)) return false;

            foreach (var part in address.Split(';'))
            {
                var p = part.Trim();
                if (p.StartsWith("x=", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(p.AsSpan(2), out int fc))
                {
                    functionCode = fc;
                    return true;
                }
            }
            return false;
        }

        /// <summary>该富地址是否指向离散输入区（FC2）——不看前缀位置，只看 <c>x=</c> 段。</summary>
        public static bool IsDiscreteInput(string? address)
            => TryGetFunctionCode(address, out int fc) && fc == 2;
    }

    public class ModbusAddress : DeviceAddressBase<ModbusArea>
    {
        private int _station;

        /// <summary>
        /// <para>从站号（0 = 跟随连接配置）。</para>
        /// <para>为什么地址上要有站号：一条串口（或一个 Modbus 网关）上挂多个从站时，
        /// "读哪个从站"是<b>每一帧</b>的事，不是连接的事。HSL 的富地址 <c>s=2;x=3;100</c>
        /// 就是逐帧站号——组帧时写进帧头、应答按本次发出的站号校验
        /// （<c>ModbusInfo</c> / <c>ModbusHelper.ExtraRtuResponseContent</c>）。</para>
        /// <para>取 0 = 不写 <c>s=</c> 前缀，用连接实例自己的 Station（历史行为不变）。
        /// 取 1~255 = 显式指定，同一连接下不同变量可以指向不同从站。</para>
        /// </summary>
        [UI.Attributes.SuperDisplay(Name = "站号(0=跟随连接)", Group = new[] { "从站" }, Order = 10)]
        public int Station
        {
            get => _station;
            set
            {
                if (value < 0) value = 0;
                if (value > 255) value = 255;
                if (SetProperty(ref _station, value))
                {
                    _cachedAddress = null;
                    RaisePropertyChanged(nameof(Address));
                }
            }
        }

        /// <summary>区域对应的读功能码（1 线圈 / 2 离散 / 3 保持 / 4 输入）</summary>
        public int FunctionCode => Area switch
        {
            ModbusArea.Coils => 1,
            ModbusArea.DiscreteInputs => 2,
            ModbusArea.HoldingRegisters => 3,
            ModbusArea.InputRegisters => 4,
            _ => 3
        };

        /// <summary>0 基原始地址（用户填写的偏移量，直接就是 HSL 认识的地址）</summary>
        public int RawAddress => int.TryParse(Offset, out int v) && v >= 0 ? v : 0;

        protected override string BuildAddress()
        {
            // 位偏移不参与：线圈/离散本身就是位粒度；寄存器区已禁止 Boolean 位访问（见 Validate）
            string baseAddress = $"x={FunctionCode};{RawAddress}";
            return _station > 0 ? $"s={_station};{baseAddress}" : baseAddress;
        }

        public override bool TryCreatePollAddress(out PollAddress? poll)
        {
            poll = null;
            if (!int.TryParse(Offset, out int start) || start < 0) return false;

            int fc = FunctionCode;
            bool bitArea = fc is 1 or 2;
            // 位区 1 点=1 单元；寄存器区按字折算（Boolean 已被限制在位区，这里都是数值类型）
            int units = bitArea ? Math.Max(1, Length) : Math.Max(1, (TotalBytes + 1) / 2);

            // 站号必须同时进 GroupKey 与 SegmentPrefix：
            // ① GroupKey 不含站号 → 两个从站同区同地址的变量会被合并成一个段，
            //    而一次 FC3 只发给一个站，合并后另一站的变量静默读到错的值；
            // ② SegmentPrefix 不含站号 → 段地址丢了 s= 前缀，读的还是连接默认站。
            string stationPrefix = _station > 0 ? $"s={_station};" : string.Empty;

            poll = new PollAddress
            {
                Protocol = PollProtocol.Modbus,
                GroupKey = $"{stationPrefix}FC{fc}",
                SegmentPrefix = $"{stationPrefix}x={fc};",
                Start = start,
                SpanUnits = units,
                IsBitArea = bitArea,
                IsBitAccess = bitArea,
                BitOffset = -1
            };
            return true;
        }

        protected override bool IsAreaCompatibleWithDataType(ModbusArea area, DataValueType dataType)
        {
            // 线圈和离散输入只支持布尔类型
            if (area is ModbusArea.Coils or ModbusArea.DiscreteInputs)
            {
                return dataType == DataValueType.Boolean;
            }

            // 寄存器区不支持布尔：旧方案允许 Boolean 读 1 个寄存器（2 字节错位），
            // 位访问 "40001.1" 又无法解析——布尔一律走线圈/离散区
            if (dataType == DataValueType.Boolean)
            {
                return false;
            }

            return true;
        }
    }
}
