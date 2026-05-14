using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 数组元素提取插件
    /// 从数组中提取指定索引的元素
    /// </summary>
    [Display(
        Name = "数组元素",
        GroupName = "数组处理",
        Description = "从数组中提取指定索引的元素",
        ShortName = "\uf0cb"
    )]
    public class ArrayElementPlugin : VisionPluginBase
    {
        /// <summary>
        /// 输入数组端口
        /// </summary>
        public InputPort<double[]> DataArray { get; } = new InputPort<double[]>("DataArray", new double[0], "输入数组");

        /// <summary>
        /// 元素索引输入端口（从0开始）
        /// </summary>
        public InputPort<int> Index { get; } = new InputPort<int>("Index", 0, "元素索引（从0开始）");

        /// <summary>
        /// 提取的元素值输出端口
        /// </summary>
        public OutputPort<double> Value { get; } = new OutputPort<double>("Value", "提取的元素值");

        /// <summary>
        /// 索引有效性输出端口
        /// </summary>
        public OutputPort<bool> IsValid { get; } = new OutputPort<bool>("IsValid", "索引是否有效");

        /// <summary>
        /// 执行数组元素提取
        /// </summary>
        /// <param name="context">执行上下文</param>
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
