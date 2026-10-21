using System;
using System.ComponentModel;
using Prism.Mvvm;
using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>设备地址配置基类，定义所有协议地址的通用结构。</para>
    /// <para>该类是一个纯配置对象，只存储"怎么读"的信息，不包含运行时数据。</para>
    /// </summary>
    public abstract class DeviceAddressBase : BindableBase
    {
        protected string? _cachedAddress = null;

        #region 核心通信属性

        private DataValueType _dataType = DataValueType.Int16;
        private int _bitOffset = -1;
        private int _length = 1;
        private int _addressOffset;
        private int _elementByteLength;

        [SuperDisplay(Name = "数据类型")]
        public DataValueType DataType
        {
            get => _dataType;
            set
            {
                if (SetProperty(ref _dataType, value))
                {
                    _length = GetDefaultLength(value);
                    _cachedAddress = null;
                    RaisePropertyChanged(nameof(Address));
                    RaisePropertyChanged(nameof(Length));
                    RaisePropertyChanged(nameof(IsBitType));
                }
            }
        }

        [SuperDisplay(Name = "位偏移(0-7)")]
        public int BitOffset
        {
            get => _bitOffset;
            set
            {
                if (value < -1) value = -1;
                // 上限随协议：Modbus/S7 是字节内位（0-7），欧姆龙 FINS 的字是 16 位（0-15）。
                // 写死 7 会让 D100.12 这类点位在建变量时被静默钳成 D100.7。
                if (value > MaxBitOffset) value = MaxBitOffset;
                if (SetProperty(ref _bitOffset, value))
                {
                    _cachedAddress = null;
                    RaisePropertyChanged(nameof(Address));
                }
            }
        }

        /// <summary>
        /// 位偏移上限（含）：默认 7（字节内位）。FINS 覆盖为 15（字内位，word*16+bit）。
        /// </summary>
        public virtual int MaxBitOffset => 7;

        /// <summary>
        /// <para>数据长度：数值/布尔类型 = 元素个数（标量填 1，数组填元素个数）；
        /// String / ByteArray = 总字节数（沿用历史语义）。</para>
        /// <para>轮询规划器用它算读取跨度（<see cref="TotalBytes"/>），数组解码也用它当元素个数。</para>
        /// </summary>
        [ SuperDisplay(Name = "数据长度")]
        public int Length
        {
            get => _length;
            set
            {
                if (value < 1) value = 1;
                if (SetProperty(ref _length, value))
                {
                    _cachedAddress = null;
                    RaisePropertyChanged(nameof(Address));
                }
            }
        }

        /// <summary>
        /// <para>地址偏移：在基地址（<see cref="Offset"/>）之上再偏移 N 个<b>地址单位</b>。</para>
        /// <para>单位随协议：Modbus 寄存器区 = 寄存器数、位区 = 位数；S7 = 字节数。
        /// 默认 0 = 不偏移，旧数据行为不变。</para>
        /// <para>与 <see cref="Offset"/> 分开存：现场常按"基地址 40001 + 偏移 2"描述点位，
        /// 拆成两个字段才能既保留基地址、又单独调整偏移（数组也从这里调起始元素）。</para>
        /// </summary>
        [SuperDisplay(
            Name = "地址偏移",
            Group = new[] { "地址" },
            Order = 20,
            Description = "在基地址上再加 N 个地址单位（Modbus 寄存器/位、S7 字节）。0 = 不偏移")]
        public int AddressOffset
        {
            get => _addressOffset;
            set
            {
                if (SetProperty(ref _addressOffset, value))
                {
                    _cachedAddress = null;
                    RaisePropertyChanged(nameof(Address));
                    RaisePropertyChanged(nameof(EffectiveStart));
                }
            }
        }

        /// <summary>
        /// <para>字符串数组专用：每个元素的固定字节数（0 = 不适用，按 <see cref="Length"/> 当总字节数）。</para>
        /// <para>为什么必须单独一个字段：<see cref="Length"/> 对 String 的历史语义是"总字节数"，
        /// 对数值数组是"元素个数"。string[] 两者都需要（元素个数 + 每元素宽度），不能复用同一个数。</para>
        /// </summary>
        [SuperDisplay(
            Name = "每元素字节数",
            Group = new[] { "地址" },
            Order = 22,
            Description = "仅字符串数组：每个元素的固定字节数（0 = 不适用）")]
        public int ElementByteLength
        {
            get => _elementByteLength;
            set
            {
                if (value < 0) value = 0;
                if (SetProperty(ref _elementByteLength, value))
                {
                    RaisePropertyChanged(nameof(TotalBytes));
                }
            }
        }

        public bool IsBitType => DataType == DataValueType.Boolean && BitOffset >= 0;

        public int TypeSize => GetTypeSize(DataType);

        /// <summary>
        /// 读取总字节数：数值/布尔 = 类型大小 × 元素个数；
        /// String = 总字节数（标量）或 每元素字节数 × 元素个数（字符串数组）。
        /// </summary>
        public int TotalBytes => DataType == DataValueType.String && _elementByteLength > 0
            ? _elementByteLength * Length
            : TypeSize * Length;

        /// <summary>
        /// 0 基有效起始地址 = 基地址解析结果 + <see cref="AddressOffset"/>。
        /// <para>返回 null 表示基地址字符串无法解析（由 <see cref="Validate"/> 报明确原因）。</para>
        /// </summary>
        public int? EffectiveStart
            => TryParseBaseStart(out int start) ? start + _addressOffset : null;

        /// <summary>
        /// 解析基地址字符串为 0 基起始地址。默认按十进制整数解析；
        /// Modbus 子类在"40001 写法"开关打开时按存储区前缀换算。
        /// </summary>
        protected virtual bool TryParseBaseStart(out int start)
        {
            start = 0;
            return int.TryParse(Offset, out start) && start >= 0;
        }

        #endregion

        #region 数据转换管道

        private double? _scale;
        private double _offset;
        private string? _unit;
        private int _decimalPlaces = 2;
        private bool _enableConversion;

        [Category("数据转换"), SuperDisplay(Name = "启用转换")]
        public bool EnableConversion
        {
            get => _enableConversion;
            set => SetProperty(ref _enableConversion, value);
        }

        [Category("数据转换"), SuperDisplay(Name = "缩放系数")]
        public double? Scale
        {
            get => _scale;
            set => SetProperty(ref _scale, value);
        }

        [Category("数据转换"), SuperDisplay(Name = "工程偏移")]
        public double EngineeringOffset
        {
            get => _offset;
            set => SetProperty(ref _offset, value);
        }

        [Category("数据转换"), SuperDisplay(Name = "工程单位")]
        public string? Unit
        {
            get => _unit;
            set => SetProperty(ref _unit, value);
        }

        [Category("数据转换"), SuperDisplay(Name = "小数位数")]
        public int DecimalPlaces
        {
            get => _decimalPlaces;
            set
            {
                if (value < 0) value = 0;
                if (value > 10) value = 10;
                SetProperty(ref _decimalPlaces, value);
            }
        }

        /// <summary>
        /// <para>将原始值转换为工程值。</para>
        /// <para>注意：此方法不再存储转换后的值，避免配置对象被运行时数据污染。</para>
        /// </summary>
        public object? ConvertToEngineering(object? rawValue)
        {
            if (!EnableConversion || rawValue == null) return rawValue;

            try
            {
            // 只对数值类型进行转换。数组（int[]/double[]…）不做逐元素换算：
            // 第一期数组只读，换算语义（每元素 Scale/Offset）没有定论，静默跳过比猜一个更安全。
            if (rawValue is bool || rawValue is string || rawValue is Array)
                return rawValue;

            double raw = Convert.ToDouble(rawValue);
                double scaled = Scale.HasValue ? raw * Scale.Value : raw;
                double result = scaled + EngineeringOffset;
                return Math.Round(result, DecimalPlaces);
            }
            catch
            {
                return rawValue;
            }
        }

        /// <summary>
        /// <para>将工程值转换为原始值（用于写入）。</para>
        /// </summary>
        public object? ConvertToRaw(object? engineeringValue)
        {
            if (!EnableConversion || engineeringValue == null) return engineeringValue;

            try
            {
            // 只对数值类型进行转换；数组不做逐元素换算（与读路径同一口径）
            if (engineeringValue is bool || engineeringValue is string || engineeringValue is Array)
                return engineeringValue;

            double eng = Convert.ToDouble(engineeringValue);
                double offset = eng - EngineeringOffset;
                double result = Scale.HasValue ? offset / Scale.Value : offset;

                // 根据数据类型进行舍入并转回目标 CLR 类型。
                // 修复点：旧实现整数分支返回 Math.Round 后的 double——写链路按 double 编 8 字节，
                // 导致 Int16/Int32 等下发时长度与字节序全部错位。
                return DataType switch
                {
                    DataValueType.Boolean => Convert.ToBoolean(result),
                    DataValueType.SByte => Convert.ToSByte(Math.Round(result, 0)),
                    DataValueType.Byte => Convert.ToByte(Math.Round(result, 0)),
                    DataValueType.Int16 => Convert.ToInt16(Math.Round(result, 0)),
                    DataValueType.UInt16 => Convert.ToUInt16(Math.Round(result, 0)),
                    DataValueType.Int32 => Convert.ToInt32(Math.Round(result, 0)),
                    DataValueType.UInt32 => Convert.ToUInt32(Math.Round(result, 0)),
                    DataValueType.Int64 => Convert.ToInt64(Math.Round(result, 0)),
                    DataValueType.UInt64 => Convert.ToUInt64(Math.Round(result, 0)),
                    _ => result
                };
            }
            catch
            {
                return engineeringValue;
            }
        }

        /// <summary>
        /// <para>格式化工程值为显示字符串。</para>
        /// </summary>
        public string FormatEngineeringValue(object? rawValue)
        {
            var engValue = ConvertToEngineering(rawValue);
            if (engValue == null) return "N/A";

            if (engValue is double d)
            {
                var format = $"F{DecimalPlaces}";
                return Unit != null ? $"{d.ToString(format)} {Unit}" : d.ToString(format);
            }

            if (engValue is bool b)
            {
                return b ? "ON" : "OFF";
            }

            return engValue.ToString() ?? "N/A";
        }

        #endregion

        #region 地址属性

        [SuperDisplay(Name = "地址/偏移量")]
        public virtual string Offset
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                {
                    _cachedAddress = null;
                    RaisePropertyChanged(nameof(Address));
                    RaisePropertyChanged(nameof(EffectiveStart));
                }
            }
        } = "0";

        public string Address => _cachedAddress ??= BuildAddress();

        #endregion

        #region 抽象方法与辅助方法

        /// <summary>
        /// <para>构建完整的通讯地址字符串。</para>
        /// <para>子类必须实现此方法。</para>
        /// </summary>
        protected abstract string BuildAddress();

        /// <summary>
        /// <para>尝试生成结构化的轮询地址（供批量轮询规划器使用，避免解析字符串）。</para>
        /// <para>不支持批量读的协议返回 false，调用方应回退到单变量字符串地址读。</para>
        /// </summary>
        public virtual bool TryCreatePollAddress(out PollAddress? poll)
        {
            poll = null;
            return false;
        }

        /// <summary>
        /// 生成"只指向数组第 <paramref name="index"/> 个元素"的单元素地址。
        ///
        /// 为什么需要它：网络数组如果只支持整组写，单元素写就只能"读整组→改一项→写回"，
        /// 镜像过期或并发写会覆盖别的元素。能按元素地址下发时，设备侧只改那一个点，
        /// 不需要读改写。默认不支持（未知协议），由具体地址类覆盖。
        /// </summary>
        public virtual bool TryCreateElementAddress(
            int index, out DeviceAddressBase? elementAddress, out string? error)
        {
            elementAddress = null;
            error = "当前协议地址不支持单元素写入";
            return false;
        }

        /// <summary>
        /// <para>验证地址配置是否有效。</para>
        /// </summary>
        /// <returns>验证结果，包含错误信息</returns>
        public virtual (bool IsValid, string ErrorMessage) Validate()
        {
            if (string.IsNullOrWhiteSpace(Offset))
                return (false, "地址偏移量不能为空");

            if (IsBitType && (BitOffset < 0 || BitOffset > MaxBitOffset))
                return (false, $"位偏移量必须在0-{MaxBitOffset}之间");

            if (Length < 1)
                return (false, "数据长度必须大于0");

            if (!TryParseBaseStart(out _))
                return (false, "地址必须是 0 基的非负整数");

            if (EffectiveStart is int start && start < 0)
                return (false, "地址偏移后起始地址不能为负");

            return (true, string.Empty);
        }

        /// <summary>
        /// <para>获取指定数据类型的默认字节大小。</para>
        /// </summary>
        protected virtual int GetTypeSize(DataValueType dataType)
        {
            return dataType switch
            {
                DataValueType.Boolean => 1, // 布尔值至少读取1个字节
                DataValueType.SByte or DataValueType.Byte => 1,
                DataValueType.Int16 or DataValueType.UInt16 => 2,
                DataValueType.Int32 or DataValueType.UInt32 or DataValueType.Float => 4,
                DataValueType.Int64 or DataValueType.UInt64 or DataValueType.Double => 8,
                DataValueType.String or DataValueType.ByteArray => 1, // 每个元素1字节
                _ => throw new NotSupportedException($"不支持的数据类型: {dataType}")
            };
        }

        /// <summary>
        /// <para>获取指定数据类型的默认长度。</para>
        /// </summary>
        protected virtual int GetDefaultLength(DataValueType dataType)
        {
            return dataType switch
            {
                DataValueType.String => 20, // 字符串默认长度20
                DataValueType.ByteArray => 10, // 字节数组默认长度10
                _ => 1 // 其他类型默认长度1
            };
        }

        public override string ToString() => Address;

        #endregion
    }

    public abstract class DeviceAddressBase<TAreaEnum> : DeviceAddressBase
        where TAreaEnum : Enum
    {
        private TAreaEnum _area;

        [SuperDisplay(Name = "存储区分类")]
        public TAreaEnum Area
        {
            get => _area;
            set
            {
                if (SetProperty(ref _area, value))
                {
                    _cachedAddress = null;
                    RaisePropertyChanged(nameof(Address));
                    RaisePropertyChanged(nameof(EffectiveStart));
                }
            }
        }

        public override (bool IsValid, string ErrorMessage) Validate()
        {
            var baseResult = base.Validate();
            if (!baseResult.IsValid)
                return baseResult;

            if (!IsAreaCompatibleWithDataType(Area, DataType))
                return (false, $"存储区 {Area} 不支持数据类型 {DataType}");

            return (true, string.Empty);
        }

        protected virtual bool IsAreaCompatibleWithDataType(TAreaEnum area, DataValueType dataType)
        {
            return true;
        }
    }

}