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
    Name = "ArrayElement",
    GroupName = "数组处理",
    Description = "从数组中提取指定索引的元素",
    ShortName = "\uf0cb"
)]
    public class ArrayElementPlugin : VisionPluginBase
    {
        public InputPort<double[]> DataArray { get; } = new InputPort<double[]>("Data Array", new double[0], "输入数组");
        public InputPort<int> Index { get; } = new InputPort<int>("Index", 0, "元素索引（从0开始）");

        public OutputPort<double> Value { get; } = new OutputPort<double>("Value", "提取的元素值");
        public OutputPort<bool> IsValid { get; } = new OutputPort<bool>("IsValid", "索引是否有效");

        public override void RunAlgorithm(IExecutionContext context)
        {
            double[] data = DataArray.GetTypedValue();
            int index = Index.GetTypedValue();

            if (data == null || index < 0 || index >= data.Length)
            {
                Value.Value = 0;
                IsValid.Value = false;
                context.Logger.Warn($"{InstanceName} 索引 {index} 超出数组范围 [0, {data?.Length - 1 ?? -1}]");
                return;
            }

            Value.Value = data[index];
            IsValid.Value = true;
        }

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
