using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
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
