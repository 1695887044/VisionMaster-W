using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 数据类型转换插件
    /// 在不同数据类型之间进行转换，支持多种常见类型转换
    /// </summary>
    [Display(
        Name = "类型转换",
        GroupName = "数据处理",
        Description = "在不同数据类型之间进行转换，支持多种常见类型转换",
        ShortName = "\uf044"
    )]
    public class DataConversionPlugin : VisionPluginBase
    {
        /// <summary>
        /// 转换目标类型枚举
        /// </summary>
        public enum TargetType
        {
            Int32,
            Double,
            Boolean,
            String,
            DateTime
        }

        /// <summary>
        /// 输入值端口
        /// </summary>
        public InputPort<object> InputValue { get; } = new InputPort<object>("Input", null, "输入值");

        /// <summary>
        /// 目标类型输入端口
        /// </summary>
        public InputPort<TargetType> Target { get; } = new InputPort<TargetType>("Target", TargetType.Double, "目标类型");

        /// <summary>
        /// 字符串格式输入端口（用于DateTime转换）
        /// </summary>
        public InputPort<string> Format { get; } = new InputPort<string>("Format", "yyyy-MM-dd HH:mm:ss", "格式字符串（用于日期转换）");

        /// <summary>
        /// 转换结果输出端口
        /// </summary>
        public OutputPort<object> Result { get; } = new OutputPort<object>("Result", "转换结果");

        /// <summary>
        /// 转换是否成功输出端口
        /// </summary>
        public OutputPort<bool> Success { get; } = new OutputPort<bool>("Success", "转换是否成功");

        /// <summary>
        /// 执行数据类型转换
        /// </summary>
        /// <param name="context">执行上下文</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            object input = InputValue.GetTypedValue();
            if (input == null)
            {
                Result.Value = null;
                Success.Value = true;
                return;
            }

            try
            {
                var target = Target.GetTypedValue();
                Result.Value = target switch
                {
                    TargetType.Int32 => Convert.ToInt32(input),
                    TargetType.Double => Convert.ToDouble(input),
                    TargetType.Boolean => Convert.ToBoolean(input),
                    TargetType.String => input.ToString(),
                    TargetType.DateTime => DateTime.ParseExact(input.ToString(), Format.GetTypedValue(), null),
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
