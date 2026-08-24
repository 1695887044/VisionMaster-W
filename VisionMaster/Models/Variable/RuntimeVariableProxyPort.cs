using Core.Interfaces;
using System;
using System.Collections.Generic;

namespace VisionMaster.Models
{
    /// <summary>
    /// 上下文感知输出端口
    /// 标记需要在执行期被注入 IExecutionContext 才能取值的输出端口
    /// CompiledPluginNode 在调用插件 Execute 前，会对其所有 ContextAware 绑定调用 BindContext
    /// </summary>
    public interface IContextAwareOutputPort : IOutputPort
    {
        /// <summary>
        /// 绑定执行上下文，并触发 ValueChanged 通知下游 InputPort 刷新缓存
        /// </summary>
        void BindContext(IExecutionContext context);
    }

    /// <summary>
    /// 运行时变量代理端口
    /// 编译期占位：让 InputPort.LinkedSource 指向一个"未实例化"的运行时变量
    /// 运行期取值：从 context.LocalVariables 按变量名现取，由 BindContext 注入 context
    /// </summary>
    /// <remarks>
    /// 典型场景：节点 N 计算出某中间结果写入 context.LocalVariables["mid"]，
    /// 节点 M（不相邻）的 InputPort 通过本端口引用 "mid"，
    /// 节点 M 执行前 CompiledPluginNode 调 BindContext 注入 context，
    /// 触发 ValueChanged → 下游 InputPort.RefreshLinkedCache 重新读取，从而取到最新值。
    /// </remarks>
    public class RuntimeVariableProxyPort : IContextAwareOutputPort
    {
        /// <summary>
        /// 当前绑定的执行上下文（每次节点执行前由 CompiledPluginNode 注入）
        /// </summary>
        private IExecutionContext _context;

        /// <summary>
        /// 引用的运行时变量名（对应 context.LocalVariables 的 key）
        /// </summary>
        public string VariableName { get; }

        /// <summary>端口名称（用于调试显示）</summary>
        public string Name => $"Runtime.{VariableName}";

        /// <summary>
        /// 期望的数据类型（取自消费方 InputPort.DataType）
        /// 用于在变量不存在时返回类型默认值，避免装箱 null 引发转换异常
        /// </summary>
        public Type DataType { get; }

        /// <summary>端口描述</summary>
        public string Description { get; set; }

        /// <summary>
        /// 值变化事件
        /// BindContext 后主动触发一次，让下游 InputPort 重新 RefreshLinkedCache
        /// </summary>
        public event EventHandler ValueChanged;

        /// <summary>
        /// 创建运行时变量代理端口
        /// </summary>
        /// <param name="variableName">引用的运行时变量名</param>
        /// <param name="expectedType">消费方 InputPort 期望的数据类型</param>
        public RuntimeVariableProxyPort(string variableName, Type expectedType)
        {
            if (string.IsNullOrWhiteSpace(variableName))
                throw new ArgumentException("运行时变量名不能为空", nameof(variableName));

            VariableName = variableName;
            DataType = expectedType ?? typeof(object);
            Description = $"运行时变量: {variableName}";
        }

        /// <summary>
        /// 当前端口值
        /// 从 context.LocalVariables 按变量名取；
        /// 变量不存在时返回类型默认值（值类型返回 default(T)，引用类型返回 null）
        /// </summary>
        public object Value
        {
            get
            {
                if (_context == null)
                    return DataType.IsValueType ? Activator.CreateInstance(DataType) : null;

                if (_context.LocalVariables.TryGetValue(VariableName, out var v))
                    return v;

                // 变量未定义时返回类型默认值，由消费方 InputPort.DefaultConvert 兜底转换
                return DataType.IsValueType ? Activator.CreateInstance(DataType) : null;
            }
            set
            {
                // 运行时变量代理是只读的：值由 VariableDefinitionPlugin 等通过 context.LocalVariables 写入
                // 这里静默忽略写入，避免抛异常中断流程
            }
        }

        /// <summary>
        /// 绑定执行上下文并通知下游刷新
        /// </summary>
        public void BindContext(IExecutionContext context)
        {
            _context = context;
            // 触发 ValueChanged，让订阅了的下游 InputPort.UpstreamValueChanged 被调用，
            // 进而 RefreshLinkedCache 重新读本端口的 Value，确保取到最新变量值
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
