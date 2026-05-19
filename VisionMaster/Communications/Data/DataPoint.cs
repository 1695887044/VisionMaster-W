using System;
using Prism.Mvvm;

namespace VisionMaster.Communications
{

    /// <summary>
    /// 泛型数据点类
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    public class DataPoint<T> : BindableBase, IDataPoint
    {
        #region 私有字段

        /// <summary>数据值</summary>
        private T? _value;

        /// <summary>数据质量</summary>
        private DataQuality _quality = DataQuality.NotConnected;

        /// <summary>时间戳（UTC）</summary>
        private DateTime _timestamp = DateTime.UtcNow;

        /// <summary>错误消息</summary>
        private string? _errorMessage;

        /// <summary>值变化标志</summary>
        private bool _hasChanged;

        #endregion

        #region 只读属性

        /// <summary>
        /// <para>获取数据点的名称标识。</para>
        /// <para>在同一个连接中，数据点名称应该是唯一的。</para>
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// <para>获取数据点关联的地址配置。</para>
        /// <para>包含如何读取数据的配置信息，如通讯地址、协议类型等。</para>
        /// </summary>
        public DeviceAddressBase Address { get; }

        /// <summary>
        /// <para>获取数据的原始类型。</para>
        /// </summary>
        public Type DataType => typeof(T);

        #endregion

        #region 可绑定属性

        /// <summary>
        /// <para>获取或设置数据点的当前值。</para>
        /// <para>这是最常用的属性，UI绑定通常绑定到此属性。</para>
        /// </summary>
        public T? Value
        {
            get => _value;
            set
            {
                if (!EqualityComparer<T?>.Default.Equals(_value, value))
                {
                    SetProperty(ref _value, value);
                    _timestamp = DateTime.UtcNow;
                    _quality = DataQuality.Good;
                    _errorMessage = null;
                    _hasChanged = true;
                }
            }
        }

        /// <summary>
        /// <para>获取或设置数据的质量状态。</para>
        /// <para>质量状态指示数据的可靠性，用于判断数据是否可用。</para>
        /// </summary>
        public DataQuality Quality
        {
            get => _quality;
            set
            {
                if (SetProperty(ref _quality, value))
                {
                    _hasChanged = true;
                }
            }
        }

        /// <summary>
        /// <para>获取或设置数据的时间戳（UTC）。</para>
        /// <para>记录数据最后一次更新的时间。</para>
        /// </summary>
        public DateTime Timestamp
        {
            get => _timestamp;
            set => SetProperty(ref _timestamp, value);
        }

        /// <summary>
        /// <para>获取或设置错误消息。</para>
        /// <para>当数据读取失败时，此属性包含错误描述。</para>
        /// </summary>
        public string? ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    _hasChanged = true;
                }
            }
        }

        #endregion

        #region 显式接口实现

        object? IDataPoint.Value => _value;
        bool IDataPoint.HasChanged => _hasChanged;

        public Type ValueType =>typeof(T);

        void IDataPoint.AcceptChanges()
        {
            _hasChanged = false;
        }

        void IDataPoint.UpdateValue(object? value)
        {
            if (value is T typedValue)
            {
                Value = typedValue;
            }
            else if (value == null)
            {
                Value = default;
            }
            else
            {
                MarkAsBad($"类型不匹配：期望 {typeof(T).Name}，实际 {value.GetType().Name}");
            }
        }

        #endregion

        #region 构造方法

        /// <summary>
        /// <para>初始化数据点的新实例。</para>
        /// </summary>
        /// <param name="name">数据点的名称</param>
        /// <param name="address">关联的地址配置</param>
        /// <exception cref="ArgumentNullException">当name或address为null时抛出</exception>
        /// <exception cref="ArgumentException">当数据类型不匹配时抛出</exception>
        public DataPoint(string name, DeviceAddressBase address)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Address = address ?? throw new ArgumentNullException(nameof(address));

            // ✅ 新增：构造函数中验证数据类型一致性
            ValidateDataTypeMatch();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// <para>标记数据为无效状态。</para>
        /// <para>通常在读取失败时调用此方法。</para>
        /// </summary>
        /// <param name="errorMessage">错误消息描述</param>
        public void MarkAsBad(string errorMessage)
        {
            _quality = DataQuality.Bad;
            _errorMessage = errorMessage;
            _timestamp = DateTime.UtcNow;
            _hasChanged = true;

            RaisePropertyChanged(nameof(Quality));
            RaisePropertyChanged(nameof(ErrorMessage));
            RaisePropertyChanged(nameof(Timestamp));
        }

        /// <summary>
        /// <para>标记数据为不确定状态。</para>
        /// <para>当数据可能不准确但仍可使用时调用。</para>
        /// </summary>
        /// <param name="reason">不确定的原因描述</param>
        public void MarkAsUncertain(string reason)
        {
            _quality = DataQuality.Uncertain;
            _errorMessage = reason;
            _timestamp = DateTime.UtcNow; // ✅ 修复：添加时间戳更新
            _hasChanged = true;

            RaisePropertyChanged(nameof(Quality));
            RaisePropertyChanged(nameof(ErrorMessage));
            RaisePropertyChanged(nameof(Timestamp)); // ✅ 修复：添加时间戳通知
        }

        /// <summary>
        /// <para>获取数据的工程值。</para>
        /// <para>如果地址配置了数据转换，会返回转换后的值。</para>
        /// </summary>
        /// <returns>工程值（如果启用转换），否则返回原始值</returns>
        public object? GetEngineeringValue()
        {
            if (EqualityComparer<T?>.Default.Equals(_value, default) || _value == null)
                return null;

            return Address.ConvertToEngineering(_value);
        }

        /// <summary>
        /// <para>获取格式化的数据显示字符串。</para>
        /// </summary>
        /// <returns>格式化的字符串，包含工程值和单位</returns>
        public string GetDisplayString()
        {
            if (_quality != DataQuality.Good)
                return $"[{_quality}]";

            return Address.FormatEngineeringValue(_value);
        }

        /// <summary>
        /// <para>检查数据是否已超时。</para>
        /// </summary>
        /// <param name="timeoutSeconds">超时阈值（秒），默认60秒</param>
        /// <returns>如果数据更新时间超过阈值返回true，否则返回false</returns>
        public bool IsStale(int timeoutSeconds = 60)
        {
            return (DateTime.UtcNow - _timestamp).TotalSeconds > timeoutSeconds; // ✅ 修复：使用UTC时间
        }

        /// <summary>
        /// <para>重置数据点到初始状态。</para>
        /// </summary>
        public void Reset()
        {
            _value = default;
            _quality = DataQuality.NotConnected;
            _errorMessage = null;
            _timestamp = DateTime.UtcNow;
            _hasChanged = false;

            RaisePropertyChanged(nameof(Value));
            RaisePropertyChanged(nameof(Quality));
            RaisePropertyChanged(nameof(ErrorMessage));
            RaisePropertyChanged(nameof(Timestamp));
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 验证泛型类型T与地址配置的DataType是否匹配
        /// </summary>
        private void ValidateDataTypeMatch()
        {
            Type expectedType = GetSystemTypeFromDataType(Address.DataType);
            Type actualType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            if (expectedType != actualType)
            {
                throw new ArgumentException(
                    $"数据类型不匹配：数据点 {Name} 期望类型 {expectedType.Name}，" +
                    $"但地址配置为 {Address.DataType}（对应 {actualType.Name}）",
                    nameof(Address));
            }
        }

        /// <summary>
        /// 将DataType枚举转换为对应的.NET系统类型
        /// </summary>
        private static Type GetSystemTypeFromDataType(DataValueType dataType)
        {
            return dataType switch
            {
                DataValueType.Boolean => typeof(bool),
                DataValueType.SByte => typeof(sbyte),
                DataValueType.Byte => typeof(byte),
                DataValueType.Int16 => typeof(short),
                DataValueType.UInt16 => typeof(ushort),
                DataValueType.Int32 => typeof(int),
                DataValueType.UInt32 => typeof(uint),
                DataValueType.Int64 => typeof(long),
                DataValueType.UInt64 => typeof(ulong),
                DataValueType.Float => typeof(float),
                DataValueType.Double => typeof(double),
                DataValueType.String => typeof(string),
                DataValueType.ByteArray => typeof(byte[]),
                _ => throw new NotSupportedException($"不支持的数据类型: {dataType}")
            };
        }

        #endregion

        #region 操作符重载

        /// <summary>
        /// <para>隐式转换到基础类型。</para>
        /// <para>允许直接将DataPoint赋值给对应类型的变量。</para>
        /// </summary>
        public static implicit operator T?(DataPoint<T> point) => point._value;

        #endregion

        #region ToString

        /// <summary>
        /// <para>获取数据点的字符串表示。</para>
        /// </summary>
        /// <returns>包含名称、值、质量和时间的字符串</returns>
        public override string ToString()
        {
            var valueStr = _quality == DataQuality.Good
                ? GetDisplayString()
                : $"[{_quality}]";

            return $"{Name}: {valueStr} ({_timestamp.ToLocalTime():HH:mm:ss})";
        }

        #endregion
    }
}
