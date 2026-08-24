using Core.Interfaces.Core;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces
{
    /// <summary>
    /// 泛型输出端口
    /// 数据生产者，向外暴露强类型数据
    /// 支持跨类型智能转换，值变化时自动通知所有订阅者
    /// </summary>
    /// <typeparam name="T">端口承载的数据类型</typeparam>
    public class OutputPort<T> : ObservableObject, IOutputPort
    {
        /// <summary>
        /// 端口唯一名称
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 端口承载的数据类型
        /// </summary>
        public Type DataType => typeof(T);

        /// <summary>
        /// 端口描述信息
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 当端口值发生实质性变化时触发
        /// </summary>
        public event EventHandler ValueChanged;

        /// <summary>
        /// 集合变更事件（保留兼容性，本类不主动触发）
        /// 如需集合变更通知，请使用专门的CollectionOutputPort<T>
        /// </summary>
        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        /// <summary>
        /// 存储端口的强类型值
        /// </summary>
        private T _typedValue;

        /// <summary>
        /// 端口当前值（支持跨类型智能转换）
        /// </summary>
        public object Value
        {
            get => _typedValue;
            set
            {
                T newValue;

                // 情况1：传入null值，使用类型默认值
                if (value == null)
                {
                    newValue = default(T);
                }
                // 情况2：类型完全匹配，直接赋值（最高性能路径）
                else if (value is T directValue)
                {
                    newValue = directValue;
                }
                // 情况3：类型不匹配，尝试智能转换
                else
                {
                    try
                    {
                        // 处理可空类型和枚举字符串转换
                        Type targetType = typeof(T);
                        if (Nullable.GetUnderlyingType(targetType) != null)
                            targetType = Nullable.GetUnderlyingType(targetType);

                        if (targetType.IsEnum && value is string strValue)
                            newValue = (T)Enum.Parse(targetType, strValue);
                        else
                            newValue = (T)Convert.ChangeType(value, targetType);
                    }
                    catch (Exception ex)
                    {
                        // 转换失败时抛出详细异常信息
                        throw new InvalidCastException(
                            $"无法将类型 {value.GetType().FullName} 的值转换为端口 {Name} 要求的类型 {typeof(T).FullName}",
                            ex);
                    }
                }

                // 使用 SetProperty 方法进行值比较和更新
                // SetProperty 内部会使用 ValuesAreEqual 进行正确的值比较
                if (SetProperty(ref _typedValue, newValue))
                {
                    // 值确实发生了变化，触发 ValueChanged 事件
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// 强类型的值属性
        /// 直接设置强类型值，避免装箱拆箱
        /// </summary>
        public T TypedValue
        {
            get => _typedValue;
            set
            {
                if (SetProperty(ref _typedValue, value))
                {
                    OnPropertyChanged(nameof(Value));
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="name">端口唯一名称</param>
        /// <param name="description">端口描述信息</param>
        public OutputPort(string name, string description = "")
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description;
        }

        /// <summary>
        /// 构造函数（带初始值）
        /// </summary>
        /// <param name="name">端口唯一名称</param>
        /// <param name="description">端口描述信息</param>
        /// <param name="initialValue">初始值</param>
        public OutputPort(string name, string description, T initialValue)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description;
            _typedValue = initialValue;
        }
    }
}
