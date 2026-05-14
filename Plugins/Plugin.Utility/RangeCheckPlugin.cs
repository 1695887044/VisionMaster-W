using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 范围检查插件
    /// 检查数值是否在指定范围内，支持多种边界条件
    /// </summary>
    [Display(
        Name = "范围检查",
        GroupName = "逻辑判断",
        Description = "检查数值是否在指定范围内，支持多种边界条件",
        ShortName = "\uf058"
    )]
    public class RangeCheckPlugin : VisionPluginBase
    {
        /// <summary>
        /// 范围类型枚举
        /// </summary>
        public enum RangeType
        {
            Inclusive,      // [Min, Max]
            Exclusive,      // (Min, Max)
            MinInclusive,   // [Min, Max)
            MaxInclusive,   // (Min, Max]
            GreaterThan,    // > Min
            GreaterThanOrEqual,  // >= Min
            LessThan,       // < Max
            LessThanOrEqual // <= Max
        }

        /// <summary>
        /// 要检查的值输入端口
        /// </summary>
        public InputPort<double> Value { get; } = new InputPort<double>("Value", 0.0, "要检查的值");

        /// <summary>
        /// 最小值输入端口
        /// </summary>
        public InputPort<double> Min { get; } = new InputPort<double>("Min", 0.0, "最小值");

        /// <summary>
        /// 最大值输入端口
        /// </summary>
        public InputPort<double> Max { get; } = new InputPort<double>("Max", 100.0, "最大值");

        /// <summary>
        /// 范围类型输入端口
        /// </summary>
        public InputPort<RangeType> Type { get; } = new InputPort<RangeType>("Type", RangeType.Inclusive, "范围类型");

        /// <summary>
        /// 检查结果输出端口
        /// </summary>
        public OutputPort<bool> Result { get; } = new OutputPort<bool>("Result", "是否在范围内");

        /// <summary>
        /// 距离边界的偏差输出端口
        /// </summary>
        public OutputPort<double> Deviation { get; } = new OutputPort<double>("Deviation", "距离边界的偏差（超出范围时为正）");

        /// <summary>
        /// 执行范围检查
        /// </summary>
        /// <param name="context">执行上下文</param>
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
                case RangeType.Inclusive:
                    inRange = value >= min && value <= max;
                    deviation = value < min ? min - value : value > max ? value - max : 0;
                    break;
                case RangeType.Exclusive:
                    inRange = value > min && value < max;
                    deviation = value <= min ? min - value + double.Epsilon : value >= max ? value - max + double.Epsilon : 0;
                    break;
                case RangeType.MinInclusive:
                    inRange = value >= min && value < max;
                    deviation = value < min ? min - value : value >= max ? value - max : 0;
                    break;
                case RangeType.MaxInclusive:
                    inRange = value > min && value <= max;
                    deviation = value <= min ? min - value : value > max ? value - max : 0;
                    break;
                case RangeType.GreaterThan:
                    inRange = value > min;
                    deviation = value <= min ? min - value + double.Epsilon : 0;
                    break;
                case RangeType.GreaterThanOrEqual:
                    inRange = value >= min;
                    deviation = value < min ? min - value : 0;
                    break;
                case RangeType.LessThan:
                    inRange = value < max;
                    deviation = value >= max ? value - max + double.Epsilon : 0;
                    break;
                case RangeType.LessThanOrEqual:
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
