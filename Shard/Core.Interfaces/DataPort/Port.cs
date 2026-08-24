using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Interfaces.Core;

namespace Core.Interfaces
{
    /// <summary>
    /// 基础泛型端口类
    /// 提供强类型数据存储和基本的属性变更通知
    /// </summary>
    /// <typeparam name="T">端口承载的数据类型</typeparam>
    public class Port<T> : ObservableObject, IPort
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
        /// 强类型的端口值
        /// </summary>
        private T _typedValue;

        /// <summary>
        /// 强类型访问端口值
        /// 推荐在内部代码中使用，避免装箱拆箱
        /// </summary>
        public T TypedValue
        {
            get => _typedValue;
            set
            {
                // 使用泛型相等比较器确保所有类型比较正确
                if (!EqualityComparer<T>.Default.Equals(_typedValue, value))
                {
                    _typedValue = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Value));
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// 弱类型访问端口值（实现IPort接口）
        /// 用于反射和动态场景
        /// </summary>
        public object Value
        {
            get => _typedValue;
            set => TypedValue = (T)value;
        }

        /// <summary>
        /// 端口描述信息
        /// </summary>
        private string _description;

        /// <summary>
        /// 端口描述信息，用于UI显示和文档生成
        /// </summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        /// <summary>
        /// 当端口值发生实质性变化时触发
        /// </summary>
        public event EventHandler ValueChanged;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="name">端口唯一名称</param>
        /// <param name="defaultValue">端口初始默认值</param>
        public Port(string name, T defaultValue = default)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _typedValue = defaultValue;
        }
    }
}
