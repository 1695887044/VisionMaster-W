using System;
using System.ComponentModel;
using Prism.Mvvm; // 引入 BindableBase
using UI.Attributes;

namespace VisionMaster.Communications
{
    // ==========================================
    // 🌟 1. 顶层基类 (引入缓存机制与属性通知)
    // ==========================================
    public abstract class DeviceAddressBase : BindableBase
    {
        // 核心缓存：运行期底层通讯频繁读取时，直接返回它
        protected string? _cachedAddress = null;

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
            set
            {
                if (SetProperty(ref _scale, value))
                {
                    RaisePropertyChanged(nameof(EngineeringValue));
                }
            }
        }

        [Category("数据转换"), SuperDisplay(Name = "工程偏移")]
        public double EngineeringOffset
        {
            get => _offset;
            set
            {
                if (SetProperty(ref _offset, value))
                {
                    RaisePropertyChanged(nameof(EngineeringValue));
                }
            }
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
                if (SetProperty(ref _decimalPlaces, value))
                {
                    RaisePropertyChanged(nameof(EngineeringValue));
                }
            }
        }

        [Browsable(false)]
        public double EngineeringValue { get; private set; }

        public object? ConvertToEngineering(object? rawValue)
        {
            if (!EnableConversion || rawValue == null) return rawValue;
            try
            {
                double raw = Convert.ToDouble(rawValue);
                double scaled = Scale.HasValue ? raw * Scale.Value : raw;
                double result = scaled + EngineeringOffset;
                EngineeringValue = Math.Round(result, DecimalPlaces);
                return EngineeringValue;
            }
            catch
            {
                return rawValue;
            }
        }

        public object? ConvertToRaw(object? engineeringValue)
        {
            if (!EnableConversion || engineeringValue == null) return engineeringValue;
            try
            {
                double eng = Convert.ToDouble(engineeringValue);
                double offset = eng - EngineeringOffset;
                double result = Scale.HasValue ? offset / Scale.Value : offset;
                return Math.Round(result, 0);
            }
            catch
            {
                return engineeringValue;
            }
        }

        public string FormatEngineeringValue(object? rawValue)
        {
            var engValue = ConvertToEngineering(rawValue);
            if (engValue == null) return "N/A";
            if (engValue is double d)
            {
                var format = $"F{DecimalPlaces}";
                return Unit != null ? $"{d.ToString(format)} {Unit}" : d.ToString(format);
            }
            return engValue.ToString() ?? "N/A";
        }

        #endregion

        /// <summary>
        /// 核心偏移量/地址编号 (设为 virtual 允许子类重写特性)
        /// </summary>
        [SuperDisplay(Name = "地址/偏移量")]
        public virtual string Offset
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                {
                    _cachedAddress = null; // 💡 值改变，炸毁缓存
                    RaisePropertyChanged(nameof(Address)); // 通知 UI 地址已更新
                }
            }
        } = "0";

        /// <summary>
        /// 最终对外输出的完整通讯地址字符串 (惰性求值)
        /// </summary>
        [Browsable(false)]
        public string Address => _cachedAddress ??= BuildAddress();

        // 🌟 子类必须实现这个方法，只在配置改变时才会执行一次！
        protected abstract string BuildAddress();

        public override string ToString() => Address;
    }

    // ==========================================
    // 🌟 2. 泛型基类 (处理分区枚举)
    // ==========================================
    public abstract class DeviceAddressBase<TAreaEnum> : DeviceAddressBase where TAreaEnum : Enum
    {
        [Category("基础设置"), SuperDisplay(Name = "存储区分类")]
        public TAreaEnum Area
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                {
                    _cachedAddress = null; // 💡 区改变，炸毁缓存
                    RaisePropertyChanged(nameof(Address));
                }
            }
        }
    }

    // ==========================================
    // 🌟 3. 各大协议具体实现
    // ==========================================

    public class ModbusAddress : DeviceAddressBase<ModbusArea>
    {
        [SuperDisplay(Name = "字节序(大小端)")]
        public ByteOrderFormat Endian
        {
            get => field;
            set => SetProperty(ref field, value); // 大小端不影响地址字符串本身，不用清空缓存
        } = ByteOrderFormat.ABCD;

        protected override string BuildAddress()
        {
            if (!int.TryParse(Offset, out int offsetVal)) offsetVal = 0;

            int prefix = Area == ModbusArea.HoldingRegisters ? 40000 :
                         Area == ModbusArea.InputRegisters ? 30000 :
                         Area == ModbusArea.DiscreteInputs ? 10000 : 0;

            return (prefix + offsetVal + 1).ToString();
        }
    }

    public class S7Address : DeviceAddressBase<S7Area>
    {
        [SuperDisplay(Name = "DB块编号(仅DB区有效)")]
        public int DbNumber
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                {
                    _cachedAddress = null; // 💡 DB块号改变，炸毁缓存
                    RaisePropertyChanged(nameof(Address));
                }
            }
        } = 1;

        protected override string BuildAddress()
        {
            if (Area == S7Area.DB)
            {
                return $"DB{DbNumber}.DBX{Offset}";
            }
            return $"{Area}{Offset}";
        }
    }

    public class MitsubishiAddress : DeviceAddressBase<MitsubishiArea>
    {
        protected override string BuildAddress()
        {
            return $"{Area}{Offset}";
        }
    }

    public class OmronAddress : DeviceAddressBase<OmronArea>
    {
        protected override string BuildAddress()
        {
            return $"{Area}{Offset}";
        }
    }

    public class AllenBradleyAddress : DeviceAddressBase
    {
        // 🌟 使用 override 覆盖显示特性，防止 new 关键字破坏多态缓存机制
        [SuperDisplay(Name = "标签名 (TagName)")]
        public override string Offset
        {
            get => base.Offset;
            set => base.Offset = value;
        }

        public AllenBradleyAddress()
        {
            Offset = "Program:MainProgram.MyTag"; // 设置初始默认值
        }

        protected override string BuildAddress()
        {
            return Offset;
        }
    }

    public class OpcUaAddress : DeviceAddressBase
    {
        [SuperDisplay(Name = "命名空间序号 (NamespaceIndex)")]
        public ushort NamespaceIndex
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
        } = 2;

        [SuperDisplay(Name = "标识符类型 (IdType)")]
        public OpcUaIdType IdType
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
        } = OpcUaIdType.String;

        // 🌟 使用 override 覆盖显示特性
        [SuperDisplay(Name = "节点标识符 (Identifier)")]
        public override string Offset
        {
            get => base.Offset;
            set => base.Offset = value;
        }

        public OpcUaAddress()
        {
            Offset = "PLC.Data.Trigger";
        }

        protected override string BuildAddress()
        {
            string typePrefix = IdType == OpcUaIdType.String ? "s=" :
                                IdType == OpcUaIdType.Numeric ? "i=" : "g=";
            return $"ns={NamespaceIndex};{typePrefix}{Offset}";
        }
    }
}