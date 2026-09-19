using Core.Interfaces;
using VisionMaster.Communications;
using System;
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
    public class LocalVariableModel : BindableBase, IVariable, IVariableDisplaySource, IRenameableVariable, IWritableVariable
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
