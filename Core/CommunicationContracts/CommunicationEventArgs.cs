using System;

namespace VisionMaster.Communications
{
    public class CommunicationErrorEventArgs : EventArgs
    {
        public string ConnectionName { get; }
        public string ErrorMessage { get; }

        public CommunicationErrorEventArgs(string connectionName, string errorMessage)
        {
            ConnectionName = connectionName;
            ErrorMessage = errorMessage;
        }
    }
    /// <summary>
    /// 变量值变化事件参数
    /// </summary>
    public class VariableChangedEventArgs : EventArgs
    {
        public string ConnectionName { get; }
        public string VariableName { get; }
        public object? OldValue { get; }
        public object? NewValue { get; }

        public VariableChangedEventArgs(string connectionName, string variableName, object? oldValue, object? newValue)
        {
            ConnectionName = connectionName;
            VariableName = variableName;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }
    public class ConnectionErrorEventArgs : EventArgs
    {
        public string ConnectionName { get; }
        public Exception Exception { get; }

        public ConnectionErrorEventArgs(string name, Exception ex)
        {
            ConnectionName = name;
            Exception = ex;
        }
    }

    public class CommunicationDataEventArgs : EventArgs
    {
        public string ConnectionName { get; }
        public string Address { get; }
        public object? Value { get; }

        public CommunicationDataEventArgs(string name, string address, object? value)
        {
            ConnectionName = name;
            Address = address;
            Value = value;
        }
    }

    /// <summary>
    /// 变量写入请求
    /// </summary>
    public class VariableWriteRequest
    {
        /// <summary>
        /// 连接名称
        /// </summary>
        public string ConnectionName { get; set; } = string.Empty;

        /// <summary>
        /// 变量地址
        /// </summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>
        /// 写入值
        /// </summary>
        public object Value { get; set; } = null!;

        /// <summary>
        /// 值类型
        /// </summary>
        public Type ValueType { get; set; } = typeof(object);
    }
}
