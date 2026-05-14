using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 数值比较插件
    /// 比较两个数值的大小关系，支持浮点容差比较
    /// </summary>
    [Display(
        Name = "数值比较",
        GroupName = "逻辑判断",
        Description = "比较两个数值的大小关系，支持浮点容差比较",
        ShortName = "\uf047"
    )]
    public class ComparisonPlugin : VisionPluginBase
    {
        /// <summary>
        /// 比较类型枚举
        /// </summary>
        public enum ComparisonType
        {
            Equal,
            NotEqual,
            GreaterThan,
            LessThan,
            GreaterThanOrEqual,
            LessThanOrEqual
        }

        /// <summary>
        /// 第一个数值输入端口
        /// </summary>
        public InputPort<double> ValueA { get; } = new InputPort<double>("A", 0.0, "第一个数值");

        /// <summary>
        /// 第二个数值输入端口
        /// </summary>
        public InputPort<double> ValueB { get; } = new InputPort<double>("B", 0.0, "第二个数值");

        /// <summary>
        /// 比较类型输入端口
        /// </summary>
        public InputPort<ComparisonType> Type { get; } = new InputPort<ComparisonType>("Type", ComparisonType.Equal, "比较类型");

        /// <summary>
        /// 浮点比较容差输入端口
        /// </summary>
        public InputPort<double> Tolerance { get; } = new InputPort<double>("Tolerance", 1e-6, "浮点比较容差");

        /// <summary>
        /// 比较结果输出端口
        /// </summary>
        public OutputPort<bool> Result { get; } = new OutputPort<bool>("Result", "比较结果");

        /// <summary>
        /// 执行数值比较
        /// </summary>
        /// <param name="context">执行上下文</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            double a = ValueA.GetTypedValue();
            double b = ValueB.GetTypedValue();
            var type = Type.GetTypedValue();
            double tolerance = Tolerance.GetTypedValue();

            Result.Value = type switch
            {
                ComparisonType.Equal => Math.Abs(a - b) <= tolerance,
                ComparisonType.NotEqual => Math.Abs(a - b) > tolerance,
                ComparisonType.GreaterThan => a > b + tolerance,
                ComparisonType.LessThan => a < b - tolerance,
                ComparisonType.GreaterThanOrEqual => a >= b - tolerance,
                ComparisonType.LessThanOrEqual => a <= b + tolerance,
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
