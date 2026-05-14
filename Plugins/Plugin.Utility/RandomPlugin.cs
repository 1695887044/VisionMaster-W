using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 随机数生成插件
    /// 生成指定范围内的随机数，支持整数和浮点数
    /// </summary>
    [Display(
        Name = "随机数",
        GroupName = "常用工具",
        Description = "生成指定范围内的随机数，支持整数和浮点数",
        ShortName = "\uf074"
    )]
    public class RandomPlugin : VisionPluginBase
    {
        /// <summary>
        /// 最小值输入端口
        /// </summary>
        public InputPort<double> MinValue { get; } = new InputPort<double>("Min", 0.0, "最小值（包含）");

        /// <summary>
        /// 最大值输入端口
        /// </summary>
        public InputPort<double> MaxValue { get; } = new InputPort<double>("Max", 1.0, "最大值（不包含）");

        /// <summary>
        /// 是否生成整数输入端口
        /// </summary>
        public InputPort<bool> GenerateInteger { get; } = new InputPort<bool>("Integer", false, "是否生成整数");

        /// <summary>
        /// 随机种子输入端口（可选）
        /// </summary>
        public InputPort<int?> Seed { get; } = new InputPort<int?>("Seed", null, "随机种子（可选，不设置则使用系统时间）");

        /// <summary>
        /// 生成的随机数输出端口
        /// </summary>
        public OutputPort<double> Result { get; } = new OutputPort<double>("Result", "生成的随机数");

        /// <summary>
        /// 随机数生成器实例
        /// </summary>
        private Random _random;

        /// <summary>
        /// 初始化插件，创建随机数生成器
        /// </summary>
        public override void Initialize()
        {
            _random = new Random();
        }

        /// <summary>
        /// 执行随机数生成
        /// </summary>
        /// <param name="context">执行上下文</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            double min = MinValue.GetTypedValue();
            double max = MaxValue.GetTypedValue();

            if (min >= max)
            {
                context.Logger.Warn($"{InstanceName} 最小值必须小于最大值");
                Result.Value = min;
                return;
            }

            int? seed = Seed.GetTypedValue();
            if (seed.HasValue)
            {
                _random = new Random(seed.Value);
            }

            double result;
            if (GenerateInteger.GetTypedValue())
            {
                result = _random.Next((int)min, (int)max);
            }
            else
            {
                result = _random.NextDouble() * (max - min) + min;
            }

            Result.Value = result;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose() { }
    }
}
