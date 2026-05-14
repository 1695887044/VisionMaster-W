using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 字符串格式化插件
    /// 将多个值格式化为一个字符串，支持标准的.NET格式字符串
    /// </summary>
    [Display(
        Name = "字符串格式化",
        GroupName = "字符串处理",
        Description = "将多个值格式化为一个字符串，支持标准的.NET格式字符串",
        ShortName = "\uf035"
    )]
    public class StringFormatPlugin : VisionPluginBase
    {
        /// <summary>
        /// 格式字符串输入端口
        /// 使用标准的.NET格式语法，如 "Value: {0:F2}"
        /// </summary>
        public InputPort<string> FormatString { get; } = new InputPort<string>("Format", "{0}", "格式字符串，如 \"Value: {0:F2}\"") { IsRequired = true };

        /// <summary>
        /// 参数1输入端口
        /// </summary>
        public InputPort<object> Arg1 { get; } = new InputPort<object>("Arg1", null, "格式化参数1");

        /// <summary>
        /// 参数2输入端口
        /// </summary>
        public InputPort<object> Arg2 { get; } = new InputPort<object>("Arg2", null, "格式化参数2");

        /// <summary>
        /// 参数3输入端口
        /// </summary>
        public InputPort<object> Arg3 { get; } = new InputPort<object>("Arg3", null, "格式化参数3");

        /// <summary>
        /// 参数4输入端口
        /// </summary>
        public InputPort<object> Arg4 { get; } = new InputPort<object>("Arg4", null, "格式化参数4");

        /// <summary>
        /// 格式化结果输出端口
        /// </summary>
        public OutputPort<string> Result { get; } = new OutputPort<string>("Result", "格式化后的字符串");

        /// <summary>
        /// 执行字符串格式化
        /// </summary>
        /// <param name="context">执行上下文</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            try
            {
                string format = FormatString.GetTypedValue();
                object[] args = { Arg1.GetTypedValue(), Arg2.GetTypedValue(), Arg3.GetTypedValue(), Arg4.GetTypedValue() };
                
                int argCount = args.Length;
                for (int i = args.Length - 1; i >= 0; i--)
                {
                    if (args[i] != null)
                    {
                        argCount = i + 1;
                        break;
                    }
                }

                object[] usedArgs = new object[argCount];
                Array.Copy(args, usedArgs, argCount);

                Result.Value = string.Format(format, usedArgs);
            }
            catch (Exception ex)
            {
                context.Logger.Error($"{InstanceName} 格式化失败: {ex.Message}");
                Result.Value = string.Empty;
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
