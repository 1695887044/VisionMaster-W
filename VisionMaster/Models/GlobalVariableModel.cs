using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class GlobalVariableModel : BindableBase, IOutputPort
    {
        public string Name { get; set; }
        public Type DataType { get; set; }
        public string Description { get; set; }

        public object DefaultValue { get; set; }

        public object Value
        {
            get => field;
            set
            {
                SetProperty(ref field, value);
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler ValueChanged;

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
    public class ArrayIndexProxyPort : IOutputPort
    {
        private readonly IOutputPort _parentSource;
        private readonly int _index;

        public ArrayIndexProxyPort(IOutputPort parentSource, int index)
        {
            _parentSource = parentSource ?? throw new ArgumentNullException(nameof(parentSource));
            _index = index;
        }

        // 🌟 公共值读取：执行索引逻辑
        public object Value => GetIndexedValue();

        // 🌟 关键修复：显式实现接口时，也必须返回“索引后的值”
        object IPort.Value
        {
            get => GetIndexedValue();
            set => throw new NotSupportedException("代理端口不支持反向写入！");
        }

        private object GetIndexedValue()
        {
            var raw = _parentSource.Value;
            // 增加判空和类型检查，防止上游算子还没运行（Value为null）时崩溃
            if (raw is Array arr && _index >= 0 && _index < arr.Length)
            {
                return arr.GetValue(_index);
            }
            return null;
        }

        // 类型：自动转为元素类型（如 double[] -> double）
        public Type DataType => _parentSource.DataType?.GetElementType();

        // 🌟 调试友好：名字建议加上索引后缀，方便你在日志里看清是谁在报错
        public string Name => $"{_parentSource.Name}[{_index}]";

        public string Description
        {
            get => $"[索引代理] {_parentSource.Description}";
            set => _parentSource.Description = value;
        }

        // 🌟 事件转发：这个写得很棒，保留
        public event EventHandler ValueChanged
        {
            add => _parentSource.ValueChanged += value;
            remove => _parentSource.ValueChanged -= value;
        }
    }
}
