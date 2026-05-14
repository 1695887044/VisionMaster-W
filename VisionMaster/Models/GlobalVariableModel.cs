using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 全局变量模型
    /// 实现 IOutputPort 接口，可作为数据端口被其他步骤引用
    /// </summary>
    public class GlobalVariableModel : BindableBase, IOutputPort
    {
        /// <summary>
        /// 变量名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 数据类型
        /// </summary>
        public Type DataType { get; set; }

        /// <summary>
        /// 变量描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 默认值
        /// </summary>
        public object DefaultValue { get; set; }

        /// <summary>
        /// 当前值
        /// </summary>
        public object Value
        {
            get => field;
            set
            {
                SetProperty(ref field, value);
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 值变更事件
        /// </summary>
        public event EventHandler ValueChanged;

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
    }

    /// <summary>
    /// 数组索引代理端口
    /// 允许通过索引访问数组类型变量的元素
    /// </summary>
    public class ArrayIndexProxyPort : IOutputPort
    {
        private readonly IOutputPort _parentSource;
        private readonly int _index;

        /// <summary>
        /// 创建数组索引代理端口
        /// </summary>
        public ArrayIndexProxyPort(IOutputPort parentSource, int index)
        {
            _parentSource = parentSource ?? throw new ArgumentNullException(nameof(parentSource));
            _index = index;
        }

        /// <summary>
        /// 获取索引后的值
        /// </summary>
        public object Value => GetIndexedValue();

        /// <summary>
        /// 显式实现接口，返回索引后的值
        /// </summary>
        object IPort.Value
        {
            get => GetIndexedValue();
            set => throw new NotSupportedException("代理端口不支持反向写入！");
        }

        /// <summary>
        /// 获取索引后的值
        /// </summary>
        private object GetIndexedValue()
        {
            var raw = _parentSource.Value;
            if (raw is Array arr && _index >= 0 && _index < arr.Length)
            {
                return arr.GetValue(_index);
            }
            return null;
        }

        /// <summary>
        /// 元素类型（自动从数组类型推断）
        /// </summary>
        public Type DataType => _parentSource.DataType?.GetElementType();

        /// <summary>
        /// 端口名称（包含索引后缀）
        /// </summary>
        public string Name => $"{_parentSource.Name}[{_index}]";

        /// <summary>
        /// 端口描述
        /// </summary>
        public string Description
        {
            get => $"[索引代理] {_parentSource.Description}";
            set => _parentSource.Description = value;
        }

        /// <summary>
        /// 值变更事件（转发自父端口）
        /// </summary>
        public event EventHandler ValueChanged
        {
            add => _parentSource.ValueChanged += value;
            remove => _parentSource.ValueChanged -= value;
        }
    }
}
