using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Util
{
    public enum MathOperationType
    {
        [Display(Name = "加")] Add,
        [Display(Name = "减")] Subtract,
        [Display(Name = "乘")] Multiply,
        [Display(Name = "除")] Divide,
        [Display(Name = "幂")] Power,
        [Display(Name = "开方")] Sqrt,
        [Display(Name = "绝对值")] Abs,
        [Display(Name = "正弦")] Sin,
        [Display(Name = "余弦")] Cos,
        [Display(Name = "正切")] Tan,
        [Display(Name = "反正切")] Atan,
        [Display(Name = "自然对数")] Log,
        [Display(Name = "常用对数")] Log10,
        [Display(Name = "指数")] Exp,
        [Display(Name = "四舍五入")] Round,
        [Display(Name = "向下取整")] Floor,
        [Display(Name = "向上取整")] Ceiling
    }

    [Display(
        Name = "数学计算",
        GroupName = "数据处理",
        Description = "支持多种数学运算：加减乘除、幂次、开方、三角函数等",
        ShortName = "\uf1ec"
    )]
    [ParallelSafe] // 二期真并行：纯计算类
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
                // 契约"默认成功、显式失败"：吞异常路径必须显式报失败，否则会被预置的 true 误放行
                Success.Value = false;
                ErrorMessage.Value = ex.Message;
            }
        }

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
