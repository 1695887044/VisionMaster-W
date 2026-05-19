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
        /// <summary>
        /// 缓存的完整地址字符串
        /// </summary>
        protected string? _cachedAddress = null;

        #region 核心通信属性

        private DataValueType _dataType = DataValueType.Int16;
        private int _bitOffset = -1;
        private int _length = 1;

        /// <summary>
        /// <para>获取或设置数据类型。</para>
        /// <para>决定了通信层读取多少字节以及如何解析数据。</para>
        /// </summary>
        [Category("核心设置"), SuperDisplay(Name = "数据类型")]
        public DataValueType DataType
        {
            get => _dataType;
            set
            {
                if (SetProperty(ref _dataType, value))
                {
                    // 根据数据类型自动设置默认长度
                    _length = GetDefaultLength(value);
                    _cachedAddress = null;
                    RaisePropertyChanged(nameof(Address));
                    RaisePropertyChanged(nameof(Length));
                    RaisePropertyChanged(nameof(IsBitType));
                }
            }
        }

        /// <summary>
        /// <para>获取或设置位偏移量（0-7）。</para>
        /// <para>仅当DataType为Boolean时有效，用于访问字节中的特定位。</para>
        /// <para>值为-1表示不使用位偏移。</para>
        /// </summary>
        [Category("核心设置"), SuperDisplay(Name = "位偏移(0-7)")]
        public int BitOffset
        {
            get => _bitOffset;
            set
            {
                if (value < -1) value = -1;
                if (value > 7) value = 7;
                if (SetProperty(ref _bitOffset, value))
                {
                    _cachedAddress = null;
                    RaisePropertyChanged(nameof(Address));
                }
            }
        }

        /// <summary>
        /// <para>获取或设置数据长度。</para>
        /// <para>对于基本类型：表示元素个数（数组长度）</para>
        /// <para>对于字符串：表示最大字符数</para>
        /// <para>对于ByteArray：表示字节数</para>
        /// </summary>
        [Category("核心设置"), SuperDisplay(Name = "数据长度")]
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
        /// <para>获取是否为位类型数据。</para>
        /// <para>位类型数据需要特殊的读取和解析逻辑。</para>
        /// </summary>
        [Browsable(false)]
        public bool IsBitType => DataType == DataValueType.Boolean && BitOffset >= 0;

        /// <summary>
        /// <para>获取该数据类型的默认字节大小。</para>
        /// </summary>
        [Browsable(false)]
        public int TypeSize => GetTypeSize(DataType);

        /// <summary>
        /// <para>获取读取该地址需要的总字节数。</para>
        /// </summary>
        [Browsable(false)]
        public int TotalBytes => TypeSize * Length;

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
                // 只对数值类型进行转换
                if (rawValue is bool || rawValue is string || rawValue is byte[])
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
                // 只对数值类型进行转换
                if (engineeringValue is bool || engineeringValue is string || engineeringValue is byte[])
                    return engineeringValue;

                double eng = Convert.ToDouble(engineeringValue);
                double offset = eng - EngineeringOffset;
                double result = Scale.HasValue ? offset / Scale.Value : offset;

                // 根据数据类型进行舍入
                return DataType switch
                {
                    DataValueType.Boolean => Convert.ToBoolean(result),
                    DataValueType.SByte or DataValueType.Byte or
                    DataValueType.Int16 or DataValueType.UInt16 or
                    DataValueType.Int32 or DataValueType.UInt32 or
                    DataValueType.Int64 or DataValueType.UInt64 => Math.Round(result, 0),
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

        /// <summary>
        /// <para>核心偏移量/地址编号。</para>
        /// </summary>
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
                }
            }
        } = "0";

        /// <summary>
        /// <para>最终对外输出的完整通讯地址字符串。</para>
        /// </summary>
        [Browsable(false)]
        public string Address => _cachedAddress ??= BuildAddress();

        #endregion

        #region 抽象方法与辅助方法

        /// <summary>
        /// <para>构建完整的通讯地址字符串。</para>
        /// <para>子类必须实现此方法。</para>
        /// </summary>
        protected abstract string BuildAddress();

        /// <summary>
        /// <para>验证地址配置是否有效。</para>
        /// </summary>
        /// <returns>验证结果，包含错误信息</returns>
        public virtual (bool IsValid, string ErrorMessage) Validate()
        {
            if (string.IsNullOrWhiteSpace(Offset))
                return (false, "地址偏移量不能为空");

            if (IsBitType && (BitOffset < 0 || BitOffset > 7))
                return (false, "位偏移量必须在0-7之间");

            if (Length < 1)
                return (false, "数据长度必须大于0");

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

        [Category("基础设置"), SuperDisplay(Name = "存储区分类")]
        public TAreaEnum Area
        {
            get => _area;
            set
            {
                if (SetProperty(ref _area, value))
                {
                    _cachedAddress = null;
                    RaisePropertyChanged(nameof(Address));
                }
            }
        }

        public override (bool IsValid, string ErrorMessage) Validate()
        {
            var baseResult = base.Validate();
            if (!baseResult.IsValid)
                return baseResult;

            // 验证存储区与数据类型的兼容性
            if (!IsAreaCompatibleWithDataType(Area, DataType))
                return (false, $"存储区 {Area} 不支持数据类型 {DataType}");

            return (true, string.Empty);
        }

        /// <summary>
        /// <para>验证存储区是否支持指定的数据类型。</para>
        /// <para>子类可以重写此方法实现协议特定的验证逻辑。</para>
        /// </summary>
        protected virtual bool IsAreaCompatibleWithDataType(TAreaEnum area, DataValueType dataType)
        {
            return true;
        }
    }

}