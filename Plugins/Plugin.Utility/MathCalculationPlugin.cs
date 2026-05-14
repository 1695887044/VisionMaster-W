using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 数学计算插件
    /// 支持多种数学运算：加减乘除、幂次、开方、三角函数等
    /// </summary>
    [Display(
        Name = "数学计算",
        GroupName = "数据处理",
        Description = "支持多种数学运算：加减乘除、幂次、开方、三角函数等",
        ShortName = "\uf1ec"
    )]
    public class MathCalculationPlugin : VisionPluginBase
    {
        /// <summary>
        /// 运算类型枚举
        /// </summary>
        public enum OperationType
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

        /// <summary>
        /// 运算类型输入端口
        /// </summary>
        public InputPort<OperationType> Operation { get; } = new InputPort<OperationType>("Operation", OperationType.Add, "运算类型");

        /// <summary>
        /// 操作数A输入端口
        /// </summary>
        public InputPort<double> ValueA { get; } = new InputPort<double>("A", 0.0, "操作数A");

        /// <summary>
        /// 操作数B输入端口（部分运算需要）
        /// </summary>
        public InputPort<double> ValueB { get; } = new InputPort<double>("B", 0.0, "操作数B（加减法、乘除法、幂运算需要）");

        /// <summary>
        /// 运算结果输出端口
        /// </summary>
        public OutputPort<double> Result { get; } = new OutputPort<double>("Result", "运算结果");

        /// <summary>
        /// 执行数学运算
        /// </summary>
        /// <param name="context">执行上下文</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            double a = ValueA.GetTypedValue();
            double b = ValueB.GetTypedValue();
            var operation = Operation.GetTypedValue();

            try
            {
                Result.Value = operation switch
                {
                    OperationType.Add => a + b,
                    OperationType.Subtract => a - b,
                    OperationType.Multiply => a * b,
                    OperationType.Divide => b == 0 ? double.NaN : a / b,
                    OperationType.Power => Math.Pow(a, b),
                    OperationType.Sqrt => Math.Sqrt(a),
                    OperationType.Abs => Math.Abs(a),
                    OperationType.Sin => Math.Sin(a),
                    OperationType.Cos => Math.Cos(a),
                    OperationType.Tan => Math.Tan(a),
                    OperationType.Atan => Math.Atan(a),
                    OperationType.Log => Math.Log(a),
                    OperationType.Log10 => Math.Log10(a),
                    OperationType.Exp => Math.Exp(a),
                    OperationType.Round => Math.Round(a),
                    OperationType.Floor => Math.Floor(a),
                    OperationType.Ceiling => Math.Ceiling(a),
                    _ => 0.0
                };
            }
            catch (Exception ex)
            {
                context.Logger.Error($"{InstanceName} 运算失败: {ex.Message}");
                Result.Value = double.NaN;
            }
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
