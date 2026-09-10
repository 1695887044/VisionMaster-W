using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 通讯变量类，用于定义和管理通讯变量
    /// </summary>
    public class CommunicationVariable
    {
        /// <summary>
        /// 所属连接名称
        /// </summary>
        public string ConnectionName { get; set; } = string.Empty;

        /// <summary>
        /// 变量名称
        /// </summary>
        public string VariableName { get; set; } = string.Empty;

        /// <summary>
        /// 通讯地址
        /// </summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>
        /// 值类型
        /// </summary>
        public string ValueType { get; set; } = typeof(object).AssemblyQualifiedName;

        /// <summary>
        /// 访问权限模式
        /// </summary>
        public VariableAccessMode AccessMode { get; set; } = VariableAccessMode.ReadOnly;

        /// <summary>
        /// 当前值
        /// </summary>
        public object? CurrentValue { get; private set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdateTime { get; private set; }

        /// <summary>
        /// 值变化事件
        /// </summary>
        public event EventHandler<object?>? ValueChanged;

        /// <summary>
        /// 更新变量值
        /// </summary>
        /// <param name="newValue">新值</param>
        public void UpdateValue(object? newValue)
        {
            // 如果值发生变化，触发事件通知
            if (CurrentValue != newValue)
            {
                CurrentValue = newValue;
                LastUpdateTime = DateTime.Now;
                ValueChanged?.Invoke(this, newValue);
            }
        }
    }
}
