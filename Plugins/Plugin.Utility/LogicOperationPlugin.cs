using Core.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    public enum LogicOperationType
    {
        And,
        Or,
        Not,
        Xor,
        Nand,
        Nor
    }

    [Display(
        Name = "逻辑运算",
        GroupName = "逻辑判断",
        Description = "支持布尔逻辑运算：AND、OR、NOT、XOR等",
        ShortName = "\uf1b2"
    )]
    public class LogicOperationPlugin : VisionPluginBase
    {
        public InputPort<LogicOperationType> Operation { get; } = new InputPort<LogicOperationType>("Operation", LogicOperationType.And, "逻辑运算类型");

        public InputPort<bool> InputA { get; } = new InputPort<bool>("A", false, "输入A");

        public InputPort<bool> InputB { get; } = new InputPort<bool>("B", false, "输入B");

        public OutputPort<bool> Result { get; } = new OutputPort<bool>("Result", "运算结果");

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

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
