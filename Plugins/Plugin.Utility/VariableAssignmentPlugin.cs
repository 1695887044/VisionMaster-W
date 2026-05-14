using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 变量赋值插件
    /// 为已定义的本地变量赋值
    /// </summary>
    [Display(
        Name = "变量赋值",
        GroupName = "变量操作",
        Description = "为已定义的本地变量赋值",
        ShortName = "\uf044"
    )]
    public class VariableAssignmentPlugin : VisionPluginBase
    {
        /// <summary>
        /// 变量名称输入端口
        /// </summary>
        public InputPort<string> VariableName { get; } = new InputPort<string>("Name", "", "目标变量名称") { IsRequired = true };

        /// <summary>
        /// 赋值内容输入端口
        /// </summary>
        public InputPort<object> Value { get; } = new InputPort<object>("Value", null, "要赋的值");

        /// <summary>
        /// 是否创建不存在的变量输入端口
        /// </summary>
        public InputPort<bool> CreateIfNotExists { get; } = new InputPort<bool>("CreateIfNotExists", false, "变量不存在时是否自动创建");

        /// <summary>
        /// 赋值是否成功输出端口
        /// </summary>
        public OutputPort<bool> Success { get; } = new OutputPort<bool>("Success", "是否赋值成功");

        /// <summary>
        /// 错误信息输出端口
        /// </summary>
        public OutputPort<string> ErrorMessage { get; } = new OutputPort<string>("ErrorMessage", "错误信息");

        /// <summary>
        /// 执行变量赋值
        /// </summary>
        /// <param name="context">执行上下文</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            string varName = VariableName.GetTypedValue();
            object value = Value.GetTypedValue();
            bool createIfNotExists = CreateIfNotExists.GetTypedValue();

            try
            {
                if (string.IsNullOrWhiteSpace(varName))
                {
                    Success.Value = false;
                    ErrorMessage.Value = "变量名称不能为空";
                    return;
                }

                if (context.LocalVariables.ContainsKey(varName))
                {
                    context.LocalVariables[varName] = value;
                    context.Logger.Info($"{InstanceName} 本地变量 {varName} 已赋值");
                    Success.Value = true;
                }
                else
                {
                    if (createIfNotExists)
                    {
                        context.LocalVariables.Add(varName, value);
                        context.Logger.Info($"{InstanceName} 本地变量 {varName} 已创建并赋值");
                        Success.Value = true;
                    }
                    else
                    {
                        Success.Value = false;
                        ErrorMessage.Value = $"本地变量 {varName} 不存在";
                    }
                }

                ErrorMessage.Value = string.Empty;
            }
            catch (Exception ex)
            {
                Success.Value = false;
                ErrorMessage.Value = ex.Message;
                context.Logger.Error($"{InstanceName} 变量赋值失败: {ex.Message}");
            }
        }

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
