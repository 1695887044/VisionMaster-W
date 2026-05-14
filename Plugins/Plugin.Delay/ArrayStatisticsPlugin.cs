using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin.Delay
{
    [Display(
         Name = "Statistics",
         GroupName = "数据处理",
         Description = "计算数值数组的最大值、最小值、平均值和元素个数",
         ShortName = "\uf200" 
     )]
    public class ArrayStatisticsPlugin : VisionPluginBase
    {
        // 接收一个一维数组
        public InputPort<double[]> DataArray { get; } = new InputPort<double[]>("Data Array", new double[0], "输入的一维数据集合");

        public OutputPort<double> MaxValue { get; } = new OutputPort<double>("Max", "最大值");
        public OutputPort<double> MinValue { get; } = new OutputPort<double>("Min", "最小值");
        public OutputPort<double> Average { get; } = new OutputPort<double>("Average", "平均值");
        public OutputPort<int> Count { get; } = new OutputPort<int>("Count", "元素个数");

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
                return;
            }

            // 使用 LINQ 极速计算
            MaxValue.Value = data.Max();
            MinValue.Value = data.Min();
            Average.Value = data.Average();
            Count.Value = data.Length;

            context.Logger.Info($"{InstanceName} 统计完成，元素个数: {Count.Value}, 平均值: {Average.Value:F2},最大值:{MaxValue.Value},最小值:{MinValue.Value}");
        }

        public override void Initialize() { }
        public override void Dispose() { }
    }
}

