using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Util
{
    public enum ComparisonType
    {
        [Display(Name = "等于")] Equal,
        [Display(Name = "不等于")] NotEqual,
        [Display(Name = "大于")] GreaterThan,
        [Display(Name = "小于")] LessThan,
        [Display(Name = "大于等于")] GreaterThanOrEqual,
        [Display(Name = "小于等于")] LessThanOrEqual
    }

    [Display(
        Name = "数值比较",
        GroupName = "逻辑判断",
        Description = "比较两个数值的大小关系，支持浮点容差比较",
        ShortName = "\uf047"
    )]
    [ParallelSafe] // 二期真并行：纯计算类
    public class ComparisonPlugin : VisionPluginBase
    {
        public InputPort<double> ValueA { get; } = new InputPort<double>("A", 0.0, "第一个数值");

        public InputPort<double> ValueB { get; } = new InputPort<double>("B", 0.0, "第二个数值");

        public InputPort<ComparisonType> Type { get; } = new InputPort<ComparisonType>("Type", ComparisonType.Equal, "比较类型");

        public InputPort<double> Tolerance { get; } = new InputPort<double>("Tolerance", 1e-6, "浮点比较容差");

        public OutputPort<bool> Result { get; } = new OutputPort<bool>("Result", "比较结果");

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

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
