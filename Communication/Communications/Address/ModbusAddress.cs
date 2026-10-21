using System;
using System.Globalization;

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
        private bool _useLegacyAddress;

        /// <summary>
        /// <para>按 Modbus 传统 5 位地址写法解析 <see cref="DeviceAddressBase.Offset"/>：</para>
        /// <para>线圈 00001~09999 / 离散输入 10001~19999 / 输入寄存器 30001~39999 / 保持寄存器 40001~49999。</para>
        /// <para>默认关闭 = 沿用新表单的 0 基偏移（40001 要填 0）。打开后 40001 才表示第 1 个保持寄存器；
        /// 旧方案里已存的字面 40001 不会因为升级被改义（开关随方案落盘，默认 false）。</para>
        /// </summary>
        [UI.Attributes.SuperDisplay(
            Name = "40001 写法",
            Group = new[] { "地址" },
            Order = 21,
            Description = "打开后按 Modbus 5 位地址写法解析（保持寄存器 40001~49999 → 0 基 0~9998）")]
        public bool UseLegacyAddress
        {
            get => _useLegacyAddress;
            set
            {
                if (SetProperty(ref _useLegacyAddress, value))
                {
                    _cachedAddress = null;
                    RaisePropertyChanged(nameof(Address));
                    RaisePropertyChanged(nameof(EffectiveStart));
                }
            }
        }

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

        /// <summary>存储区中文名（校验文案用；枚举名不上屏）</summary>
        protected string AreaDisplayName => Area switch
        {
            ModbusArea.Coils => "线圈",
            ModbusArea.DiscreteInputs => "离散输入",
            ModbusArea.InputRegisters => "输入寄存器",
            ModbusArea.HoldingRegisters => "保持寄存器",
            _ => Area.ToString()
        };

        /// <summary>区域对应的读功能码（1 线圈 / 2 离散 / 3 保持 / 4 输入）</summary>
        public int FunctionCode => Area switch
        {
            ModbusArea.Coils => 1,
            ModbusArea.DiscreteInputs => 2,
            ModbusArea.HoldingRegisters => 3,
            ModbusArea.InputRegisters => 4,
            _ => 3
        };

        /// <summary>
        /// 0 基原始地址（基地址解析结果 + 地址偏移，直接就是 HSL 认识的地址）。
        /// 解析失败返回 0；创建路径会先走 <see cref="Validate"/> 拦住非法地址。
        /// </summary>
        public int RawAddress => EffectiveStart ?? 0;

        protected override bool TryParseBaseStart(out int start)
        {
            start = 0;
            if (!int.TryParse(Offset, out int raw) || raw < 0) return false;
            if (!_useLegacyAddress)
            {
                start = raw;
                return true;
            }

            // 传统 5 位写法：按当前存储区的区间换算成 0 基地址。
            // 区间不匹配返回 false（Validate 给出"地址与存储区不匹配"的明确原因），
            // 不静默退回字面值——那会把 40001 读成第 40001 个寄存器。
            switch (Area)
            {
                case ModbusArea.Coils when raw is >= 1 and <= 9999:
                    start = raw - 1;
                    return true;
                case ModbusArea.DiscreteInputs when raw is >= 10001 and <= 19999:
                    start = raw - 10001;
                    return true;
                case ModbusArea.InputRegisters when raw is >= 30001 and <= 39999:
                    start = raw - 30001;
                    return true;
                case ModbusArea.HoldingRegisters when raw is >= 40001 and <= 49999:
                    start = raw - 40001;
                    return true;
                default:
                    return false;
            }
        }

        protected override string BuildAddress()
        {
            // 位偏移不参与：线圈/离散本身就是位粒度；寄存器区已禁止 Boolean 位访问（见 Validate）
            string baseAddress = $"x={FunctionCode};{RawAddress}";
            return _station > 0 ? $"s={_station};{baseAddress}" : baseAddress;
        }

        public override (bool IsValid, string ErrorMessage) Validate()
        {
            if (!TryParseBaseStart(out _))
            {
                return (false, _useLegacyAddress
                    ? $"地址 {Offset} 不符合当前存储区（{AreaDisplayName}）的 40001 写法（应填该存储区的 5 位地址）"
                    : "地址必须是 0 基的非负整数");
            }

            var baseResult = base.Validate();
            if (!baseResult.IsValid) return baseResult;

            if (RawAddress > 65535)
                return (false, "地址超出 Modbus 16 位范围（0~65535）");

            return (true, string.Empty);
        }

        public override bool TryCreatePollAddress(out PollAddress? poll)
        {
            poll = null;
            if (EffectiveStart is not int start || start < 0) return false;

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

        /// <inheritdoc />
        public override bool TryCreateElementAddress(
            int index, out DeviceAddressBase? elementAddress, out string? error)
        {
            elementAddress = null;
            error = null;

            if (index < 0)
            {
                error = "数组下标不能为负数";
                return false;
            }

            if (index >= Length)
            {
                error = $"下标 {index} 超出数组长度 {Length}";
                return false;
            }

            int fc = FunctionCode;
            bool bitArea = fc is 1 or 2;

            int elementStart;
            DataValueType elementType;
            int elementLength;

            if (DataType == DataValueType.ByteArray)
            {
                // Modbus 寄存器是 16 位粒度：按字节写会覆盖同一寄存器的另一半，
                // 只能整组写或改成 S7 这类字节寻址协议。
                error = "Modbus 寄存器区按 16 位寄存器寻址，byte[] 不支持按字节写入（会覆盖同寄存器另一半）";
                return false;
            }

            if (DataType == DataValueType.String)
            {
                if (bitArea)
                {
                    error = "线圈/离散输入区不支持字符串数组";
                    return false;
                }

                if (ElementByteLength <= 0)
                {
                    error = "字符串数组未配置每元素字节数，无法按元素写入";
                    return false;
                }

                if (ElementByteLength % 2 != 0)
                {
                    error = $"字符串元素宽度 {ElementByteLength} 字节不是 2 的倍数，无法按 Modbus 寄存器对齐写入";
                    return false;
                }

                elementStart = RawAddress + index * (ElementByteLength / 2);
                elementType = DataValueType.ByteArray;
                elementLength = ElementByteLength;
            }
            else
            {
                // 位区 1 元素 = 1 位；寄存器区按字折算（Int32 = 2 个寄存器，Int64 = 4 个）
                int unitsPerElement = bitArea ? 1 : Math.Max(1, TypeSize / 2);
                elementStart = RawAddress + index * unitsPerElement;
                elementType = DataType;
                elementLength = 1;
            }

            var element = new ModbusAddress
            {
                Area = Area,
                Offset = elementStart.ToString(CultureInfo.InvariantCulture),
                DataType = elementType,
                BitOffset = BitOffset,
                Station = Station,
                Length = elementLength,
                ElementByteLength = 0,
                AddressOffset = 0,
                UseLegacyAddress = false,
            };

            element.EnableConversion = false;

            var validation = element.Validate();
            if (!validation.IsValid)
            {
                error = validation.ErrorMessage;
                return false;
            }

            elementAddress = element;
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
