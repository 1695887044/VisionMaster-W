using Core.Interfaces;
using Prism.Mvvm;
using System.Text.Json.Serialization;
using System.Windows;
using VisionMaster.Communications;
using VisionMaster.Helpers;

namespace VisionMaster.Models
{
    public class NetworkVariableModel : BindableBase, IVariable
    {
        private string _dataTypeString;
        private Type _dataType;
        private object? _value;

        [JsonIgnore]
        private ICommunicationManager? CommunicationManager;

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
            get
            {
                if (CommunicationManager != null 
                    && !string.IsNullOrEmpty(ConnectionName)
                    && AddressConfig != null)
                {
                    var connection = CommunicationManager.GetConnection(ConnectionName);
                    if (connection != null && connection.IsConnected)
                    {
                        try
                        {
                            _value = ReadFromDevice(connection);
                        }
                        catch { }
                    }
                }
                return _value;
            }
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
    }
}