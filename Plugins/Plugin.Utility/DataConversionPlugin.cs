using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    public enum TargetDataType
    {
        Int32,
        Double,
        Boolean,
        String,
        DateTime
    }

    [Display(
        Name = "类型转换",
        GroupName = "数据处理",
        Description = "在不同数据类型之间进行转换，支持多种常见类型转换",
        ShortName = "\uf044"
    )]
    public class DataConversionPlugin : VisionPluginBase
    {
        public InputPort<object> InputValue { get; } = new InputPort<object>("Input", null, "输入值");

        public InputPort<TargetDataType> Target { get; } = new InputPort<TargetDataType>("Target", TargetDataType.Double, "目标类型");

        public InputPort<string> Format { get; } = new InputPort<string>("Format", "yyyy-MM-dd HH:mm:ss", "格式字符串");

        public OutputPort<object> Result { get; } = new OutputPort<object>("Result", "转换结果");

        public OutputPort<bool> Success { get; } = new OutputPort<bool>("Success", "转换是否成功");

        public override void RunAlgorithm(IExecutionContext context)
        {
            object input = InputValue.GetTypedValue();
            var target = Target.GetTypedValue();
            string format = Format.GetTypedValue() ?? "yyyy-MM-dd HH:mm:ss";

            if (input == null)
            {
                Result.Value = null;
                Success.Value = true;
                return;
            }

            try
            {
                Result.Value = target switch
                {
                    TargetDataType.Int32 => Convert.ToInt32(input),
                    TargetDataType.Double => Convert.ToDouble(input),
                    TargetDataType.Boolean => Convert.ToBoolean(input),
                    TargetDataType.String => input.ToString(),
                    TargetDataType.DateTime => DateTime.ParseExact(input.ToString(), format, null),
                    _ => input
                };
                Success.Value = true;
            }
            catch (Exception ex)
            {
                context.Logger.Error($"{InstanceName} 类型转换失败: {ex.Message}");
                Result.Value = null;
                Success.Value = false;
            }
        }

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
