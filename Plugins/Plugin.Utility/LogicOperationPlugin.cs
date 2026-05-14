using Core.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 逻辑运算插件
    /// 支持布尔逻辑运算：AND、OR、NOT、XOR等
    /// </summary>
    [Display(
        Name = "逻辑运算",
        GroupName = "逻辑判断",
        Description = "支持布尔逻辑运算：AND、OR、NOT、XOR等",
        ShortName = "\uf1b2"
    )]
    public class LogicOperationPlugin : VisionPluginBase
    {
        /// <summary>
        /// 逻辑运算类型枚举
        /// </summary>
        public enum LogicOperationType
        {
            And,
            Or,
            Not,
            Xor,
            Nand,
            Nor
        }

        /// <summary>
        /// 运算类型输入端口
        /// </summary>
        public InputPort<LogicOperationType> Operation { get; } = new InputPort<LogicOperationType>("Operation", LogicOperationType.And, "逻辑运算类型");

        /// <summary>
        /// 输入A端口
        /// </summary>
        public InputPort<bool> InputA { get; } = new InputPort<bool>("A", false, "输入A");

        /// <summary>
        /// 输入B端口（NOT运算不需要）
        /// </summary>
        public InputPort<bool> InputB { get; } = new InputPort<bool>("B", false, "输入B");

        /// <summary>
        /// 运算结果输出端口
        /// </summary>
        public OutputPort<bool> Result { get; } = new OutputPort<bool>("Result", "运算结果");

        /// <summary>
        /// 执行逻辑运算
        /// </summary>
        /// <param name="context">执行上下文</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            bool a = InputA.GetTypedValue();
            bool b = InputB.GetTypedValue();
            var operation = Operation.GetTypedValue();

            Result.Value = operation switch
            {
                LogicOperationType.And => a && b,
                LogicOperationType.Or => a || b,
                LogicOperationType.Not => !a,
                LogicOperationType.Xor => a ^ b,
                LogicOperationType.Nand => !(a && b),
                LogicOperationType.Nor => !(a || b),
                _ => false
            };
        }

        /// <summary>
        /// 初始化插件
        /// </summary>
        public override void Initialize() { }

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose() { }
    }
}
