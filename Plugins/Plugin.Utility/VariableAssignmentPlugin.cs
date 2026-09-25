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
    public class VariableAssignmentPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        /// <summary>
        /// 变量名称输入端口
        /// </summary>
        /// 这三个端口都是"界面上手填"的参数，不是"从上游接线"的参数。
        /// 必须显式写 IsRequired = false —— InputPort.IsRequired 默认就是 true，
        /// 而编译期的必填检查是「IsRequired 且没有连线来源」：只看有没有连线，不看有没有填值。
        /// 于是默认值下，只要用户没用连线喂这三个参数（也就是正常用法），编译必然报
        /// 「必填参数 Name/Value/CreateIfNotExists 未配置」，这个节点等于用不了。
        public InputPort<string> VariableName { get; } = new InputPort<string>("Name", "", "目标变量名称") { IsRequired = false };

        /// <summary>
        /// 赋值内容输入端口
        /// </summary>
        public InputPort<object> Value { get; } = new InputPort<object>("Value", null, "要赋的值") { IsRequired = false };

        /// <summary>
        /// 是否创建不存在的变量输入端口
        /// </summary>
        public InputPort<bool> CreateIfNotExists { get; } = new InputPort<bool>("CreateIfNotExists", false, "变量不存在时是否自动创建（仅作用域=运行时有效）") { IsRequired = false };

        /// <summary>作用域取值：运行时变量（默认）</summary>
        public const string ScopeRuntime = "Runtime";

        /// <summary>作用域取值：全局变量</summary>
        public const string ScopeGlobal = "Global";

        /// <summary>
        /// 作用域：写运行时变量还是全局变量。
        ///
        /// 为什么是 string 而不是枚举 —— 踩过：
        /// InputPort&lt;T&gt;.Value 的 setter 对枚举只做硬转换，不做"从数字/文本解析"。
        /// 而本参数要经 InputValues 存盘（枚举会序列化成 int），读回来赋给端口时
        /// 直接抛 InvalidCastException: Invalid cast from 'System.Int32' to '枚举类型'，
        /// 方案一打开就炸。string 两端都能无损往返，另外取值做白名单校验（见 RunAlgorithm）。
        ///
        /// 为什么要有这个选择，而不是"先找运行时、再找全局"自动路由：
        /// 同名时"运行时变量"与"全局变量"是两个不同的东西，自动路由要么产生歧义、
        /// 要么用户看不出到底写去了哪。显式选一次，语义就定死了。
        /// IsRequired 必须显式写 false —— 它默认是 true，而编译期必填检查只看有没有连线。
        /// </summary>
        public InputPort<string> Scope { get; } =
            new InputPort<string>("Scope", ScopeRuntime, "写到哪里：Runtime=运行时变量 / Global=全局变量")
            { IsRequired = false };

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

                // 作用域取值白名单校验：认不出来就明确报错。
                // 不能"不是 Global 就当 Runtime" —— 那样拼错一个字母会把结果静默写到另一个变量池里，
                // 用户看到的是"赋值成功了但值不对"，比直接报错难查得多。
                string scope = Scope.GetTypedValue();
                bool isGlobal = string.Equals(scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase);
                if (!isGlobal && !string.Equals(scope, ScopeRuntime, StringComparison.OrdinalIgnoreCase))
                {
                    Success.Value = false;
                    ErrorMessage.Value = $"作用域取值无法识别：「{scope}」（应为 {ScopeRuntime} 或 {ScopeGlobal}）";
                    return;
                }

                // 作用域=全局：交给运行期能力契约写，查注册表 / 类型守门 / 真正落值都在主程序侧
                // （插件物理上够不到"变量管理"所在的程序集，见 IGlobalVariableWriter 的说明）
                if (isGlobal)
                {
                    var writer = context.GlobalVariables;
                    if (writer == null)
                    {
                        Success.Value = false;
                        ErrorMessage.Value = "当前执行环境不支持写全局变量";
                        return;
                    }

                    if (!writer.TryWrite(varName, value, out var writeError))
                    {
                        Success.Value = false;
                        ErrorMessage.Value = string.IsNullOrWhiteSpace(writeError)
                            ? $"写全局变量 {varName} 失败"
                            : writeError;
                        context.Logger.Error($"{InstanceName} {ErrorMessage.Value}");
                        return; // 必须短路：不 short-circuit 会在下一行把 ErrorMessage 洗成空
                    }

                    Success.Value = true;
                    context.Logger.Info($"{InstanceName} 全局变量 {varName} 已赋值");
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

        /// <summary>
        /// 返回本插件的自定义配置视图（变量名 / 值 / 自动创建 三行表单）。
        ///
        /// 不实现这个接口的话，主程序会回退到通用的「变量绑定」窗口 ——
        /// 那个窗口是给"任意端口绑任意数据源"用的，表达"给一个变量赋一个值"要绕一大圈，
        /// 见 VariableAssignmentView.xaml 顶部的说明。
        /// 视图只读端口、不写业务逻辑；点确定时由主程序调本类的 OnConfirm 把端口值回写进 InputValues。
        /// </summary>
        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new VariableAssignmentView { DataContext = this };
        }

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
