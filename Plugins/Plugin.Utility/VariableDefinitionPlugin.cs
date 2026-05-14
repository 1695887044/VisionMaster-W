using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 变量定义插件
    /// 在当前流程中定义一个新的本地变量，可指定初始值和数据类型
    /// </summary>
    [Display(
        Name = "变量定义",
        GroupName = "变量操作",
        Description = "在当前流程中定义一个新的本地变量",
        ShortName = "\uf03a"
    )]
    public class VariableDefinitionPlugin : VisionPluginBase
    {
        /// <summary>
        /// 变量名称输入端口
        /// </summary>
        public InputPort<string> VariableName { get; } = new InputPort<string>("Name", "", "变量名称") { IsRequired = true };

        /// <summary>
        /// 变量类型输入端口（字符串形式：int, double, string, bool）
        /// </summary>
        public InputPort<string> VariableType { get; } = new InputPort<string>("Type", "double", "变量类型(int/double/string/bool)") { IsRequired = true };

        /// <summary>
        /// 初始值输入端口
        /// </summary>
        public InputPort<object> InitialValue { get; } = new InputPort<object>("InitialValue", null, "初始值");

        /// <summary>
        /// 是否覆盖已存在变量输入端口
        /// </summary>
        public InputPort<bool> Overwrite { get; } = new InputPort<bool>("Overwrite", false, "是否覆盖已存在的同名变量");

        /// <summary>
        /// 定义是否成功输出端口
        /// </summary>
        public OutputPort<bool> Success { get; } = new OutputPort<bool>("Success", "是否定义成功");

        /// <summary>
        /// 错误信息输出端口
        /// </summary>
        public OutputPort<string> ErrorMessage { get; } = new OutputPort<string>("ErrorMessage", "错误信息");

        /// <summary>
        /// 定义变量
        /// </summary>
        /// <param name="context">执行上下文</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            string varName = VariableName.GetTypedValue();
            string varType = VariableType.GetTypedValue();
            object initialValue = InitialValue.GetTypedValue();
            bool overwrite = Overwrite.GetTypedValue();

            try
            {
                if (string.IsNullOrWhiteSpace(varName))
                {
                    Success.Value = false;
                    ErrorMessage.Value = "变量名称不能为空";
                    return;
                }

                Type targetType = ParseType(varType);
                if (targetType == null)
                {
                    Success.Value = false;
                    ErrorMessage.Value = $"不支持的变量类型: {varType}";
                    return;
                }

                object typedValue = ConvertValue(initialValue, targetType);

                if (context.LocalVariables.ContainsKey(varName))
                {
                    if (overwrite)
                    {
                        context.LocalVariables[varName] = typedValue;
                        context.Logger.Info($"{InstanceName} 变量 {varName} 已更新");
                    }
                    else
                    {
                        Success.Value = false;
                        ErrorMessage.Value = $"变量 {varName} 已存在，设置覆盖标志以更新";
                        return;
                    }
                }
                else
                {
                    context.LocalVariables.Add(varName, typedValue);
                    context.Logger.Info($"{InstanceName} 变量 {varName} 已定义，类型: {varType}");
                }

                Success.Value = true;
                ErrorMessage.Value = string.Empty;
            }
            catch (Exception ex)
            {
                Success.Value = false;
                ErrorMessage.Value = ex.Message;
                context.Logger.Error($"{InstanceName} 变量定义失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 解析类型字符串
        /// </summary>
        private Type ParseType(string typeName)
        {
            return typeName?.ToLower() switch
            {
                "int" or "int32" => typeof(int),
                "double" => typeof(double),
                "string" => typeof(string),
                "bool" or "boolean" => typeof(bool),
                "datetime" => typeof(DateTime),
                "float" => typeof(float),
                "long" => typeof(long),
                _ => null
            };
        }

        /// <summary>
        /// 转换值到目标类型
        /// </summary>
        private object ConvertValue(object value, Type targetType)
        {
            if (value == null)
                return Activator.CreateInstance(targetType);

            if (value.GetType() == targetType)
                return value;

            try
            {
                return Convert.ChangeType(value, targetType);
            }
            catch
            {
                return Activator.CreateInstance(targetType);
            }
        }

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
