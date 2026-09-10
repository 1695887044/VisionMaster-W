using System;
using System.ComponentModel;

namespace VisionMaster.Models
{
    /// <summary>
    /// HMI 画布组态预留接口：
    /// 未来画布控件（指示灯/数值框/按钮/曲线等）按变量名绑定数据源时依赖此契约，
    /// 变量管理器提供"复制变量名"入口，控件侧通过名称解析到实现对象。
    /// </summary>
    public interface IVariableDisplaySource : INotifyPropertyChanged
    {
        /// <summary>绑定键（变量名，全局唯一——画布控件序列化时只存此名）</summary>
        string BindingKey { get; }

        /// <summary>显示标签（默认取变量名，可被控件覆盖）</summary>
        string DisplayLabel { get; }

        /// <summary>数据类型（控件据此选择编辑器/格式化器）</summary>
        Type DataType { get; }

        /// <summary>当前值（INPC 通知自动刷新控件）</summary>
        object? Value { get; }

        /// <summary>数据来源描述："本地" 或 连接名</summary>
        string SourceLabel { get; }

        /// <summary>是否只读（网络变量输入类控件禁用，如离散输入区）</summary>
        bool IsReadOnly { get; }

        /// <summary>
        /// 控件写回值入口（数值框/按钮切换等；只读来源抛 InvalidOperationException）
        /// </summary>
        void WriteValue(object? newValue);
    }
}
