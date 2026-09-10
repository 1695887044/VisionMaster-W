using Core.Interfaces;
using VisionMaster.Communications;
using Prism.Mvvm;
using System.Windows;
using VisionMaster.Helpers;

using Newtonsoft.Json;

namespace VisionMaster.Models
{
    public class NetworkVariableModel : BindableBase, IVariable, IVariableDisplaySource
    {
        private string _dataTypeString;
        private Type _dataType;
        private object? _value;

        [JsonIgnore]
        private ICommunicationManager? CommunicationManager;

        /// <summary>
        /// 绑定通信管理器（建网络变量后必须调用，否则 Value 读写不会触达设备）。
        /// 创建方负责在方案加载/变量新建时注入，保证镜像值与设备值联动
        /// </summary>
        public void Bind(ICommunicationManager manager) => CommunicationManager = manager;

        /// <summary>
        /// 轮询镜像更新：由通信管理器的轮询链路调用，仅更新本地镜像值并触发通知，
        /// 不走 Value setter（避免回写设备造成读写自激）
        /// </summary>
        public void UpdateMirrorValue(object? newValue)
        {
            if (SetProperty(ref _value, newValue))
                _valueChanged?.Invoke(this, EventArgs.Empty);
        }

        public string Name { get; set; } = string.Empty;
        public VariableType VariableType => VariableType.Communication;
        public string? ConnectionName { get; set; }
        public DeviceAddressBase? AddressConfig { get; set; }
        public int PollIntervalMs { get; set; } = 500;

        public string DataTypeString
        {
            get => _dataTypeString ?? TypeCache.GetTypeKey(DataType);
            set
            {
                _dataTypeString = value;
                _dataType = TypeCache.GetType(value);
            }
        }

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

        public string Description { get; set; } = string.Empty;
        public object? DefaultValue { get; set; }

        public object? Value
        {
            get => _value; // 轮询镜像值：设备同步由通信管理器轮询链路负责，getter 不再直读设备（避免绑定高频同步 IO）
            set
            {
                if (SetProperty(ref _value, value))
                {
                    WriteToDevice(value);
                }
            }
        }

        [JsonIgnore]
        private EventHandler? _valueChanged;
        
        public event EventHandler ValueChanged
        {
            add => _valueChanged += value;
            remove => _valueChanged -= value;
        }

        private object? ReadFromDevice(ICommunicationConnection connection)
        {
            if (AddressConfig == null) return _value;
            
            Type valueType = Nullable.GetUnderlyingType(DataType) ?? DataType;
            var readMethod = typeof(ICommunicationConnection)
                .GetMethod(nameof(ICommunicationConnection.Read))!
                .MakeGenericMethod(valueType);
            
            var rawValue = readMethod.Invoke(connection, new object[] { AddressConfig.Address });
            return AddressConfig.ConvertToEngineering(rawValue);
        }

        private void WriteToDevice(object? value)
        {
            if (AddressConfig == null || CommunicationManager == null) return;
            
            var connection = CommunicationManager.GetConnection(ConnectionName);
            if (connection != null && connection.IsConnected)
            {
                try
                {
                    var rawValue = AddressConfig.ConvertToRaw(value);
                    connection.Write(AddressConfig.Address, rawValue);
                }
                catch { }
            }
        }

        public void ResetToDefault()
        {
            Value = DefaultValue;
        }

        #region IVariableDisplaySource（HMI 画布组态预留，显式实现避免与模型接口冲突）

        string IVariableDisplaySource.BindingKey => Name;

        string IVariableDisplaySource.DisplayLabel => string.IsNullOrEmpty(Description) ? Name : Description;

        Type IVariableDisplaySource.DataType => DataType;

        object? IVariableDisplaySource.Value => Value;

        string IVariableDisplaySource.SourceLabel => ConnectionName ?? "网络";

        /// <summary>只读存储区来源不可写（Modbus 离散输入 / S7 输入映像 I）</summary>
        bool IVariableDisplaySource.IsReadOnly
        {
            get
            {
                if (AddressConfig is DeviceAddressBase<ModbusArea> ma)
                    return ma.Area == ModbusArea.DiscreteInputs;
                if (AddressConfig is DeviceAddressBase<S7Area> sa)
                    return sa.Area == S7Area.I;
                return false;
            }
        }

        void IVariableDisplaySource.WriteValue(object? newValue) => Value = newValue;

        #endregion
    }
}
