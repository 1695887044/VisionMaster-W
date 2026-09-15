using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Util
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

        // Success / ErrorMessage 复用基类端口：此前自声明同名属性会"影子隐藏"基类成员，
        // 插件写派生端口、引擎读基类端口，导致永远报失败且错误日志为空（CS0108 教训）

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
                    // A2 类型守门：变量池的类型是条件表达式的"契约"——
                    // double 变量被塞 string 后，If/While 每次求值都抛异常、所有分支集体不走且流程照常往下跑，
                    // 是最难排查的静默故障形态。这里对齐既有类型：可转换则转，不可转换显式失败。
                    var existing = context.LocalVariables[varName];
                    if (value != null && existing != null && value.GetType() != existing.GetType())
                    {
                        try
                        {
                            value = Convert.ChangeType(value, existing.GetType());
                            context.Logger.Warn($"{InstanceName} 赋值 {varName}：值类型已按变量既有类型 {existing.GetType().Name} 自动转换");
                        }
                        catch (Exception ex)
                        {
                            Success.Value = false;
                            ErrorMessage.Value = $"变量 {varName} 类型为 {existing.GetType().Name}，值 [{value}] 无法转换：{ex.Message}";
                            context.Logger.Error($"{InstanceName} {ErrorMessage.Value}");
                            return;
                        }
                    }

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
                        return; // 必须短路：原先此处直落会在下一行把 ErrorMessage 洗成空，引擎日志丢失失败原因
                    }
                }
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
