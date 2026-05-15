using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using VisionMaster.Helpers;

namespace VisionMaster.Models
{
    /// <summary>
    /// 变量类型枚举
    /// </summary>
    public enum VariableType
    {
        /// <summary>
        /// 本地变量
        /// </summary>
        Local,
        /// <summary>
        /// 通讯变量
        /// </summary>
        Communication
    }

    /// <summary>
    /// 全局变量模型
    /// 实现 IOutputPort 接口，可作为数据端口被其他步骤引用
    /// </summary>
    public class GlobalVariableModel : BindableBase, IOutputPort
    {
        private string _dataTypeString;
        private Type _dataType;
        private object? _value;

        /// <summary>
        /// 变量名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 变量类型（本地变量或通讯变量）
        /// </summary>
        public VariableType VariableType { get; set; } = VariableType.Local;

        /// <summary>
        /// 关联的通讯连接名称（通讯变量使用）
        /// </summary>
        public string? ConnectionName { get; set; }

        /// <summary>
        /// 通讯地址（通讯变量使用）
        /// </summary>
        public string? Address { get; set; }

        /// <summary>
        /// 数据类型（用于序列化）
        /// </summary>
        public string DataTypeString
        {
            get => _dataTypeString ?? TypeCache.GetTypeKey(DataType);
            set
            {
                _dataTypeString = value;
                _dataType = TypeCache.GetType(value);
            }
        }

        /// <summary>
        /// 数据类型
        /// </summary>
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

        /// <summary>
        /// 变量描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 默认值
        /// </summary>
        public object? DefaultValue { get; set; }

        /// <summary>
        /// 当前值
        /// </summary>
        public object? Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                {
                    if (VariableType == VariableType.Local)
                    {
                        _valueChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        /// <summary>
        /// 值变更事件
        /// </summary>
        [JsonIgnore]
        private EventHandler? _valueChanged;
        
        public event EventHandler ValueChanged
        {
            add => _valueChanged += value;
            remove => _valueChanged -= value;
        }

        /// <summary>
        /// 通讯变量值变更（由通讯管理器调用）
        /// </summary>
        public void UpdateCommunicationValue(object? newValue)
        {
            if (SetProperty(ref _value, newValue))
            {
                _valueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

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
                return arr.GetValue(_index)!;
            }
            return null!;
        }

        /// <summary>
        /// 元素类型（自动从数组类型推断）
        /// </summary>
        public Type? DataType => _parentSource.DataType?.GetElementType();

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

    /// <summary>
    /// 常量输出端口
    /// 用于绑定常量值到输入端口
    /// </summary>
    public class ConstantOutputPort : IOutputPort
    {
        private readonly object _constantValue;
        private readonly Type _dataType;

        /// <summary>
        /// 创建常量输出端口
        /// </summary>
        /// <param name="constantValue">常量值</param>
        /// <param name="dataType">数据类型</param>
        public ConstantOutputPort(object constantValue, Type dataType)
        {
            _constantValue = constantValue;
            _dataType = dataType ?? typeof(string);
        }

        /// <summary>
        /// 获取常量值
        /// </summary>
        public object Value => _constantValue;

        object IPort.Value
        {
            get => _constantValue;
            set => throw new NotSupportedException("常量端口不支持写入！");
        }

        /// <summary>
        /// 数据类型
        /// </summary>
        public Type DataType => _dataType;

        /// <summary>
        /// 端口名称
        /// </summary>
        public string Name => "常量值";

        /// <summary>
        /// 端口描述
        /// </summary>
        public string Description
        {
            get => $"常量值: {_constantValue}";
            set { }
        }

        /// <summary>
        /// 值变更事件（常量值不会变更）
        /// </summary>
        public event EventHandler ValueChanged { add { } remove { } }
    }
}
