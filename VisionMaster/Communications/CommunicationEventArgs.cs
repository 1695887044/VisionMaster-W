using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 通讯错误事件参数
    /// </summary>
    public class CommunicationErrorEventArgs : EventArgs
    {
        /// <summary>
        /// 连接名称
        /// </summary>
        public string ConnectionName { get; set; } = string.Empty;

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 异常对象
        /// </summary>
        public Exception? Exception { get; set; }
    }

    /// <summary>
    /// 变量值变化事件参数
    /// </summary>
    public class VariableChangedEventArgs : EventArgs
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
        /// 旧值
        /// </summary>
        public object? OldValue { get; set; }

        /// <summary>
        /// 新值
        /// </summary>
        public object? NewValue { get; set; }
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
