using Core.Interfaces.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces
{
    /// <summary>
    /// 泛型输入端口
    /// 数据消费者，支持两种数据来源：手动设置值 + 链接到上游输出端口
    /// 自动处理上游值的类型转换和变更通知
    /// </summary>
    /// <typeparam name="T">端口承载的数据类型</typeparam>
    public class InputPort<T> : ObservableObject, IInputPort
    {
        /// <summary>
        /// 缓存的上游链接源的值
        /// 避免每次访问都重新转换
        /// </summary>
        private T _cachedLinkedValue;

        /// <summary>
        /// 是否为必填端口
        /// 编译/运行时会检查未绑定的必填端口
        /// </summary>
        public bool IsRequired { get; set; } = true;

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
        /// 手动设置的值
        /// 当没有链接上游端口时使用
        /// </summary>
        private T _manualValue;

        /// <summary>
        /// 手动设置的值（弱类型访问）
        /// </summary>
        public object Value
        {
            get => _manualValue;
            set
            {
                T newValue;

                // 统一使用与OutputPort相同的转换逻辑
                if (value == null)
                {
                    newValue = default(T);
                }
                else if (value is T directValue)
                {
                    newValue = directValue;
                }
                else
                {
                    newValue = DefaultConvert(value);
                }

                // 只有值真正变化时才更新并触发事件
                if (!EqualityComparer<T>.Default.Equals(_manualValue, newValue))
                {
                    SetProperty(ref _manualValue, newValue);
                    // 手动值变化时，需要通知ActualValue变化
                    OnPropertyChanged(nameof(ActualValue));
                    // 只有当没有链接上游时，才触发ValueChanged
                    if (LinkedSource == null)
                    {
                        ValueChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        /// <summary>
        /// 链接的上游输出端口
        /// </summary>
        private IOutputPort _linkedSource;

        /// <summary>
        /// 链接的上游输出端口
        /// 当设置为非null时，优先使用上游端口的值
        /// </summary>
        public IOutputPort LinkedSource
        {
            get => _linkedSource;
            set
            {
                if (_linkedSource != null)
                {
                    // 移除旧链接的事件订阅，防止内存泄漏
                    _linkedSource.ValueChanged -= UpstreamValueChanged;
                }

                bool oldHasLink = _linkedSource != null;
                SetProperty(ref _linkedSource, value);
                bool newHasLink = _linkedSource != null;

                if (_linkedSource != null)
                {
                    // 订阅新链接的事件
                    _linkedSource.ValueChanged += UpstreamValueChanged;
                    // 立即刷新缓存值
                    RefreshLinkedCache();
                }
                else
                {
                    // 清除缓存值
                    _cachedLinkedValue = default(T);
                }

                // 通知ActualValue变化
                OnPropertyChanged(nameof(ActualValue));

                // 如果链接状态发生变化（有→无 或 无→有），触发ValueChanged
                if (oldHasLink != newHasLink)
                {
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// 当端口实际值发生实质性变化时触发
        /// </summary>
        public event EventHandler ValueChanged;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="name">端口唯一名称</param>
        /// <param name="defaultValue">手动值的初始默认值</param>
        /// <param name="description">端口描述信息</param>
        public InputPort(string name, T defaultValue = default, string description = "")
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _manualValue = defaultValue;
            Description = description;
        }

        /// <summary>
        /// 获取端口的实际有效值
        /// 优先级：链接源值 > 手动设置值
        /// </summary>
        /// <returns>端口当前实际使用的值</returns>
        public object GetActualValue()
        {
            return LinkedSource != null ? _cachedLinkedValue : _manualValue;
        }

        /// <summary>
        /// 获取强类型的实际有效值
        /// </summary>
        /// <returns>强类型的实际有效值</returns>
        public T GetTypedValue() => (T)GetActualValue();

        /// <summary>
        /// 端口的实际有效值（强类型访问）
        /// 推荐在业务代码中使用此属性获取值
        /// </summary>
        public T ActualValue
        {
            get
            {
                // 注意：这里的逻辑已经完全修复
                // 不再错误地比较data和_cachedLinkedValue
                // 因为：
                // 1. 当使用链接源时，_cachedLinkedValue已经在RefreshLinkedCache中更新
                // 2. 当使用手动值时，_cachedLinkedValue与实际值无关
                // 3. ValueChanged事件已经在正确的地方触发
                return GetTypedValue();
            }
        }

        /// <summary>
        /// 上游值变化事件处理程序
        /// </summary>
        private void UpstreamValueChanged(object sender, EventArgs e)
        {
            // 保存旧值用于比较
            T oldValue = _cachedLinkedValue;
            // 刷新缓存值
            RefreshLinkedCache();

            // 只有值真正变化时才触发事件
            if (!EqualityComparer<T>.Default.Equals(oldValue, _cachedLinkedValue))
            {
                OnPropertyChanged(nameof(ActualValue));
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 刷新上游链接源的缓存值
        /// 从上游获取最新值并进行类型转换
        /// </summary>
        private void RefreshLinkedCache()
        {
            if (_linkedSource == null)
                return;

            object rawValue = _linkedSource.Value;

            if (rawValue == null)
            {
                _cachedLinkedValue = default(T);
                return;
            }

            // 类型完全匹配，直接赋值
            if (rawValue is T typedValue)
            {
                _cachedLinkedValue = typedValue;
                return;
            }

            // 类型不匹配，尝试智能转换
            try
            {
                _cachedLinkedValue = DefaultConvert(rawValue);
            }
            catch (Exception ex)
            {
                throw new InvalidCastException(
                    $"端口 {Name} 无法将上游输出值转换为类型 {typeof(T).FullName}。" +
                    $"上游值类型：{rawValue.GetType().FullName}，上游端口：{_linkedSource.Name}",
                    ex);
            }
        }

        /// <summary>
        /// 默认的类型转换方法
        /// 统一处理可空类型、枚举字符串转换和系统类型转换
        /// </summary>
        /// <param name="rawValue">原始值</param>
        /// <returns>转换后的强类型值</returns>
        protected virtual T DefaultConvert(object rawValue)
        {
            if (rawValue == null)
                return default(T);

            Type targetType = typeof(T);
            // 处理可空类型
            if (Nullable.GetUnderlyingType(targetType) != null)
            {
                targetType = Nullable.GetUnderlyingType(targetType);
            }

            // 处理枚举的字符串转换
            if (targetType.IsEnum && rawValue is string strValue)
            {
                return (T)Enum.Parse(targetType, strValue);
            }

            // 系统默认的兜底转换
            return (T)Convert.ChangeType(rawValue, targetType);
        }
    }
}
