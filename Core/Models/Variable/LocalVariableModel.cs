using Core.Interfaces;
using VisionMaster.Communications;
using System;
using System.Globalization;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Helpers;
namespace VisionMaster.Models
{
    /// <summary>
    /// 本地变量模型
    /// 实现 IOutputPort 接口，可作为数据端口被其他步骤引用
    /// </summary>
    public class LocalVariableModel : BindableBase, IVariable, IVariableDisplaySource, IRenameableVariable, IWritableVariable, IArrayElementWritable, IValueChangeNotifier
    {
        private string _dataTypeString;
        private Type _dataType;
        private object? _value;

        /// <summary>
        /// 变量名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 变量稳定身份（GUID）：不随改名变化，供连线/监视项/SCADA 绑定寻址。
        /// 默认为新 GUID；可写仅用于反序列化与旧方案迁移（历史数据无此字段时由持久化层补发）
        /// </summary>
        public Guid VariableId { get; set; } = Guid.NewGuid();

        public VariableType VariableType { get; set; } = VariableType.Local;
        public string? ConnectionName { get; set; }

        public DeviceAddressBase? AddressConfig { get; set; }

        /// <summary>
        /// 数据类型（用于序列化）
        /// </summary>
        public string DataTypeString
        {
            get => _dataTypeString ?? TypeCache.GetTypeKey(DataType);
            set
            {
                _dataTypeString = value;
                _dataType = TypeCache.GetType(value);
            }
        }

        /// <summary>
        /// 数据类型
        /// </summary>
        [JsonIgnore]
        public Type DataType
        {
            get
            {
                if (_dataType == null && !string.IsNullOrEmpty(_dataTypeString))
                {
                    _dataType = TypeCache.GetType(_dataTypeString);
                }
                return _dataType ?? typeof(string);
            }
            set
            {
                _dataType = value;
                _dataTypeString = TypeCache.GetTypeKey(value);
            }
        }

        /// <summary>
        /// 变量描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 默认值
        /// </summary>
        public object? DefaultValue { get; set; }

        /// <summary>
        /// 当前值
        /// </summary>
        public object? Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                {
                    if (VariableType == VariableType.Local)
                    {
                        _valueChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        /// <summary>
        /// 值变更事件
        /// </summary>
        [JsonIgnore]
        private EventHandler? _valueChanged;
        
        public event EventHandler ValueChanged
        {
            add => _valueChanged += value;
            remove => _valueChanged -= value;
        }

        /// <summary>
        /// <see cref="IWritableVariable"/> 认领：本地变量没有设备侧，写值就是内存赋值，
        /// 因此恒成功。走 <see cref="Value"/> setter 而不是直接改字段，
        /// 是为了保住"值变化 → ValueChanged"这条通知链（运行态订阅靠它刷新控件）。
        /// 同值写入会被 <c>SetProperty</c> 短路——对本地变量这是对的：值没变就没有刷新可言。
        /// </summary>
        public bool TryWrite(object? value, out string? error)
        {
            error = null;
            Value = value;
            return true;
        }

        /// <summary>
        /// 就地写数组元素：不替换数组引用，只改这一项，然后显式广播一次
        /// <see cref="IValueChangeNotifier.NotifyValueChanged"/>。
        ///
        /// 为什么不能走 <see cref="TryWrite"/>：数组引用没变，<see cref="Value"/> setter 的
        /// <c>SetProperty</c> 会短路同引用，订阅方一个事件都收不到——画面上的数值不会刷新。
        /// </summary>
        public bool TryWriteElement(int index, object? value, out string? error)
        {
            error = null;

            if (Value is not Array array)
            {
                error = $"变量 [{Name}] 当前值不是数组，无法按下标写入";
                return false;
            }

            if (index < 0 || index >= array.Length)
            {
                error = $"下标 {index} 超出数组 [{Name}] 的长度 {array.Length}";
                return false;
            }

            Type? elementType = DataType.GetElementType() ?? array.GetType().GetElementType();

            if (elementType == null)
            {
                error = $"变量 [{Name}] 的数组元素类型未知，无法写入";
                return false;
            }

            if (!TryCoerceElement(value, elementType, out var converted, out error))
                return false;

            try
            {
                array.SetValue(converted, index);
            }
            catch (Exception ex)
            {
                error = $"变量 [{Name}] 的第 {index} 项写入失败：{ex.Message}";
                return false;
            }

            // 引用没变，必须手动广播（见 IValueChangeNotifier 的注释）
            NotifyValueChanged();
            return true;
        }

        /// <summary>
        /// 元素值归一：文本按元素类型解析（复用 <see cref="VariableValueConverter"/> 的宽进严出规则），
        /// 已是目标类型的值直通，其余按不变区域性转一次。
        /// </summary>
        private static bool TryCoerceElement(object? value, Type elementType, out object? converted, out string? error)
        {
            converted = null;
            error = null;

            if (value == null)
            {
                if (elementType.IsValueType && Nullable.GetUnderlyingType(elementType) == null)
                {
                    error = $"数组元素类型 {elementType.Name} 不接受空值";
                    return false;
                }

                return true;
            }

            if (elementType.IsInstanceOfType(value))
            {
                converted = value;
                return true;
            }

            if (value is string text)
                return VariableValueConverter.TryConvert(text, elementType, out converted, out error);

            try
            {
                converted = Convert.ChangeType(value, elementType, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                error = $"「{value}」无法转换为数组元素类型 {elementType.Name}：{ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 重置为默认值
        /// </summary>
        public void ResetToDefault()
        {
            if (DefaultValue is Array arr)
            {
                Value = arr.Clone();
            }
            else
            {
                Value = DefaultValue;
            }
        }

        /// <summary>
        /// 就地改数组元素后补一次值变更广播（见 <see cref="IValueChangeNotifier"/>）。
        /// 引用没变，<see cref="Value"/> setter 的 SetProperty 不会触发，只能显式广播。
        /// </summary>
        public void NotifyValueChanged() => _valueChanged?.Invoke(this, EventArgs.Empty);

        #region IVariableDisplaySource（HMI 画布组态预留，显式实现避免与模型接口冲突）

        string IVariableDisplaySource.BindingKey => Name;

        string IVariableDisplaySource.DisplayLabel => string.IsNullOrEmpty(Description) ? Name : Description;

        Type IVariableDisplaySource.DataType => DataType;

        object? IVariableDisplaySource.Value => Value;

        string IVariableDisplaySource.SourceLabel => "本地";

        bool IVariableDisplaySource.IsReadOnly => false; // 本地变量内存读写，无只读场景

        void IVariableDisplaySource.WriteValue(object? newValue) => Value = newValue;

        #endregion
    }




}
