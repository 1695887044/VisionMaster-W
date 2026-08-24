using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    public enum RangeCheckType
    {
        Inclusive,
        Exclusive,
        MinInclusive,
        MaxInclusive,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual
    }

    [Display(
        Name = "范围检查",
        GroupName = "逻辑判断",
        Description = "检查数值是否在指定范围内，支持多种边界条件",
        ShortName = "\uf058"
    )]
    public class RangeCheckPlugin : VisionPluginBase
    {
        public InputPort<double> Value { get; } = new InputPort<double>("Value", 0.0, "要检查的值");

        public InputPort<double> Min { get; } = new InputPort<double>("Min", 0.0, "最小值");

        public InputPort<double> Max { get; } = new InputPort<double>("Max", 100.0, "最大值");

        public InputPort<RangeCheckType> Type { get; } = new InputPort<RangeCheckType>("Type", RangeCheckType.Inclusive, "范围类型");

        public OutputPort<bool> Result { get; } = new OutputPort<bool>("Result", "是否在范围内");

        public OutputPort<double> Deviation { get; } = new OutputPort<double>("Deviation", "距离边界的偏差");

        public override void RunAlgorithm(IExecutionContext context)
        {
            double value = Value.GetTypedValue();
            double min = Min.GetTypedValue();
            double max = Max.GetTypedValue();
            var type = Type.GetTypedValue();

            bool inRange;
            double deviation = 0;

            switch (type)
            {
                case RangeCheckType.Inclusive:
                    inRange = value >= min && value <= max;
                    deviation = value < min ? min - value : value > max ? value - max : 0;
                    break;
                case RangeCheckType.Exclusive:
                    inRange = value > min && value < max;
                    deviation = value <= min ? min - value + double.Epsilon : value >= max ? value - max + double.Epsilon : 0;
                    break;
                case RangeCheckType.MinInclusive:
                    inRange = value >= min && value < max;
                    deviation = value < min ? min - value : value >= max ? value - max : 0;
                    break;
                case RangeCheckType.MaxInclusive:
                    inRange = value > min && value <= max;
                    deviation = value <= min ? min - value : value > max ? value - max : 0;
                    break;
                case RangeCheckType.GreaterThan:
                    inRange = value > min;
                    deviation = value <= min ? min - value + double.Epsilon : 0;
                    break;
                case RangeCheckType.GreaterThanOrEqual:
                    inRange = value >= min;
                    deviation = value < min ? min - value : 0;
                    break;
                case RangeCheckType.LessThan:
                    inRange = value < max;
                    deviation = value >= max ? value - max + double.Epsilon : 0;
                    break;
                case RangeCheckType.LessThanOrEqual:
                    inRange = value <= max;
                    deviation = value > max ? value - max : 0;
                    break;
                default:
                    inRange = false;
                    break;
            }

            Result.Value = inRange;
            Deviation.Value = deviation;
        }

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
