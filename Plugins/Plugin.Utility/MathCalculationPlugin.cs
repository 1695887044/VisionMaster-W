using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    public enum MathOperationType
    {
        Add,
        Subtract,
        Multiply,
        Divide,
        Power,
        Sqrt,
        Abs,
        Sin,
        Cos,
        Tan,
        Atan,
        Log,
        Log10,
        Exp,
        Round,
        Floor,
        Ceiling
    }

    [Display(
        Name = "数学计算",
        GroupName = "数据处理",
        Description = "支持多种数学运算：加减乘除、幂次、开方、三角函数等",
        ShortName = "\uf1ec"
    )]
    public class MathCalculationPlugin : VisionPluginBase
    {
        public InputPort<MathOperationType> Operation { get; } = new InputPort<MathOperationType>("Operation", MathOperationType.Add, "运算类型");

        public InputPort<double> ValueA { get; } = new InputPort<double>("A", 0.0, "操作数A");

        public InputPort<double> ValueB { get; } = new InputPort<double>("B", 0.0, "操作数B");

        public OutputPort<double> Result { get; } = new OutputPort<double>("Result", "运算结果");

        public override void RunAlgorithm(IExecutionContext context)
        {
            var operation = Operation.GetTypedValue();
            double a = ValueA.GetTypedValue();
            double b = ValueB.GetTypedValue();

            try
            {
                Result.Value = operation switch
                {
                    MathOperationType.Add => a + b,
                    MathOperationType.Subtract => a - b,
                    MathOperationType.Multiply => a * b,
                    MathOperationType.Divide => b == 0 ? double.NaN : a / b,
                    MathOperationType.Power => Math.Pow(a, b),
                    MathOperationType.Sqrt => Math.Sqrt(a),
                    MathOperationType.Abs => Math.Abs(a),
                    MathOperationType.Sin => Math.Sin(a),
                    MathOperationType.Cos => Math.Cos(a),
                    MathOperationType.Tan => Math.Tan(a),
                    MathOperationType.Atan => Math.Atan(a),
                    MathOperationType.Log => Math.Log(a),
                    MathOperationType.Log10 => Math.Log10(a),
                    MathOperationType.Exp => Math.Exp(a),
                    MathOperationType.Round => Math.Round(a),
                    MathOperationType.Floor => Math.Floor(a),
                    MathOperationType.Ceiling => Math.Ceiling(a),
                    _ => double.NaN
                };
            }
            catch (Exception ex)
            {
                context.Logger.Error($"{InstanceName} 运算失败: {ex.Message}");
                Result.Value = double.NaN;
            }
        }

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
