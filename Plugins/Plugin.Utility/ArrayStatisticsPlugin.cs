using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 数组统计插件
    /// 计算数值数组的最大值、最小值、平均值和元素个数
    /// </summary>
    [Display(
        Name = "数组统计",
        GroupName = "数据处理",
        Description = "计算数值数组的最大值、最小值、平均值和元素个数",
        ShortName = "\uf200"
    )]
    public class ArrayStatisticsPlugin : VisionPluginBase
    {
        /// <summary>
        /// 输入数组端口
        /// </summary>
        public InputPort<double[]> DataArray { get; } = new InputPort<double[]>("DataArray", new double[0], "输入的一维数据集合");

        /// <summary>
        /// 最大值输出端口
        /// </summary>
        public OutputPort<double> MaxValue { get; } = new OutputPort<double>("Max", "最大值");

        /// <summary>
        /// 最小值输出端口
        /// </summary>
        public OutputPort<double> MinValue { get; } = new OutputPort<double>("Min", "最小值");

        /// <summary>
        /// 平均值输出端口
        /// </summary>
        public OutputPort<double> Average { get; } = new OutputPort<double>("Average", "平均值");

        /// <summary>
        /// 元素个数输出端口
        /// </summary>
        public OutputPort<int> Count { get; } = new OutputPort<int>("Count", "元素个数");

        /// <summary>
        /// 总和输出端口
        /// </summary>
        public OutputPort<double> Sum { get; } = new OutputPort<double>("Sum", "总和");

        /// <summary>
        /// 标准差输出端口
        /// </summary>
        public OutputPort<double> StdDev { get; } = new OutputPort<double>("StdDev", "标准差");

        /// <summary>
        /// 执行数组统计计算
        /// </summary>
        /// <param name="context">执行上下文</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            double[] data = DataArray.GetTypedValue();

            if (data == null || data.Length == 0)
            {
                context.Logger.Warn($"{InstanceName} 输入数组为空，无法进行统计！");
                MaxValue.Value = 0.0;
                MinValue.Value = 0.0;
                Average.Value = 0.0;
                Count.Value = 0;
                Sum.Value = 0.0;
                StdDev.Value = 0.0;
                return;
            }

            var max = data.Max();
            var min = data.Min();
            var avg = data.Average();
            var count = data.Length;
            var sum = data.Sum();

            MaxValue.Value = max;
            MinValue.Value = min;
            Average.Value = avg;
            Count.Value = count;
            Sum.Value = sum;

            if (count > 1)
            {
                double variance = data.Average(v => Math.Pow(v - avg, 2));
                StdDev.Value = Math.Sqrt(variance);
            }
            else
            {
                StdDev.Value = 0.0;
            }

            context.Logger.Info($"{InstanceName} 统计完成，元素个数: {Count.Value}, 平均值: {Average.Value:F2}, 最大值: {MaxValue.Value}, 最小值: {MinValue.Value}");
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
