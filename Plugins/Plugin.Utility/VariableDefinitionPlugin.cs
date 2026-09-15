using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Util
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

        // Success / ErrorMessage 复用基类端口：此前自声明同名属性会"影子隐藏"基类成员，
        // 插件写派生端口、引擎读基类端口，导致永远报失败且错误日志为空（CS0108 教训）

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

                object? typedValue;
                if (!TryConvertValue(initialValue, targetType, out typedValue, out var convError))
                {
                    // A3：契约"默认成功、显式失败"——旧实现转换失败静默回落 0/false 还报成功，
                    // 数据被悄悄篡改是最难排查的故障形态，必须当场失败并给出原因
                    Success.Value = false;
                    ErrorMessage.Value = convError;
                    context.Logger.Error($"{InstanceName} {convError}");
                    return;
                }

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
        /// 转换值到目标类型（A3：显式失败版——不再"catch 后偷渡默认值"）
        /// </summary>
        private bool TryConvertValue(object value, Type targetType, out object? result, out string? error)
        {
            // 初始值为空：给类型默认值是明确语义（用户没填），保留
            if (value == null)
            {
                result = Activator.CreateInstance(targetType);
                error = null;
                return true;
            }

            if (value.GetType() == targetType)
            {
                result = value;
                error = null;
                return true;
            }

            try
            {
                result = Convert.ChangeType(value, targetType);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                result = null;
                error = $"初始值 [{value}] 无法转换为 {targetType.Name}：{ex.Message}";
                return false;
            }
        }

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
