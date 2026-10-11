using Core.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;

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
    [ParallelSafe] // 二期真并行：Runtime xie zou jizhang zidian; Global xie you bianjianqi jiancha
    public class VariableAssignmentPlugin : VisionPluginBase, IPluginCustomViewProvider, IPluginConfigContextProvider
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

        #region 配置界面：「变量名」这一行的候选与提示（宿主快照）

        private PluginConfigContext _configContext;
        private bool _configEventsHooked;
        private string _legacyNameLink;

        /// <summary>
        /// 「变量名」这一行的候选 = 按作用域过滤后的变量名（宿主快照，见
        /// <see cref="PluginConfigContext.Variables"/>）。
        ///
        /// 为什么是名字的**候选**而不是一条连线：这一行要的是"名字"，而通用变量绑定弹窗给的是"值" ——
        /// 在弹窗里选中全局变量 OKCount，落下来的是一条 GlobalVariable 连线，语义是
        /// "运行期把 OKCount 的**当前值**喂给这个端口当名字"。于是"给 OKCount 赋值"变成了
        /// "给一个叫 0 的变量赋值"（真机 2026-10-10 反馈的坑）。名字从列表里选出来，落成**常量**。
        ///
        /// 用 ObservableCollection 而不是 List：换作用域时候选整批重算，
        /// 普通 List 清空重填不通知界面，下拉会一直是旧的。
        /// </summary>
        public ObservableCollection<string> NameCandidates { get; } = new();

        private string _nameHint = "「变量名」填要写入的变量名，也可点右侧箭头从列表里选。";

        /// <summary>
        /// 名字行的提示（随作用域与当前名字变化）。
        ///
        /// 为什么值得专门做一行提示：这条链上失败的两种方式在界面上都看不出原因 ——
        ///   ① 名字打错 → 运行期才报「找不到全局变量」，用户以为节点坏了；
        ///   ② 值填成文本而变量是数值型 → 运行期报类型不匹配。
        /// 提示里点明目标变量的**声明类型**，用户当场就知道该填 21 还是 abc。
        /// </summary>
        public string NameHint
        {
            get => _nameHint;
            private set => SetProperty(ref _nameHint, value);
        }

        /// <summary>
        /// 接收宿主透传的变量名快照（<see cref="IPluginConfigContextProvider"/>）。
        /// 调用时窗口刚打开、端口已灌过值，这里只负责把候选铺上。
        /// </summary>
        public void SetConfigContext(PluginConfigContext context)
        {
            _configContext = context;
            RefreshNameCandidates();
            RefreshNameHint();
        }

        /// <summary>
        /// 打开配置窗口时的初始化：除基类的"灌 InputValues"，还要
        /// ① 记下这一行有没有**旧连线**（老方案 / 早先在通用弹窗里绑过全局变量留下的）；
        /// ② 订阅作用域变化，让候选与提示跟着走。
        /// </summary>
        public override void Initialize(IStepConfigData stepData)
        {
            base.Initialize(stepData); // StepData + ApplyConfigValues（灌端口值）

            // 旧连线只记下来给提示用：不在这里直接解除 —— 用户可能只是打开看一眼。
            // 真正解除的时机是"他动了这一行"（见 NotifyNameEditedByUser）
            _legacyNameLink = stepData?.GetLinkedAddress("Name");

            if (!_configEventsHooked)
            {
                _configEventsHooked = true;

                // 只订作用域（界面下拉驱动 → 重算候选与提示）。
                //
                // **不订 VariableName.ValueChanged**：端口值的来源不止"用户键入"这一种 ——
                // 试运行时 PluginTestRunner.BridgeInputs 会把上游实际值灌进配置实例的同名端口，
                // 若把"值变了"当成"用户改了名字"，就会在那条路径上**误解除用户什么都没动的旧连线**
                // （解除的是活模型，点"取消"也退不回来）。用户编辑改由视图显式上报（见下）。
                Scope.ValueChanged += (_, __) =>
                {
                    RefreshNameCandidates();
                    RefreshNameHint();
                };
            }

            RefreshNameCandidates();
            RefreshNameHint();
        }

        /// <summary>
        /// 视图上报"用户动了名字这一行"（键盘键入 / 删字符 / 从下拉里选）——
        /// 接的是真实输入事件，程序灌值不会走到这里。
        /// </summary>
        public void NotifyNameEditedByUser()
        {
            if (StepData != null && StepData.IsLinked(VariableName.Name))
            {
                StepData.RemoveLink(VariableName.Name);
                _legacyNameLink = null;
            }

            RefreshNameHint();
        }

        /// <summary>按当前作用域重算候选（拿不到宿主快照时留空 —— 界面仍可直接手填名字）</summary>
        private void RefreshNameCandidates()
        {
            var scope = CurrentScope;

            // ★ 重算前先把"现在这一行里的名字"记下来，重算后顶回去。
            //
            // 为什么必须这么做（2026-10-10 复核实测的 WPF 行为）：可编辑 ComboBox 在当前**有选中项**时，
            // ItemsSource 被 Clear() 会把 Text 一并置空，并沿 TwoWay 绑定把空串**推回端口** ——
            // 于是"选中名字 → 切作用域"这个最常见的连招会静默把名字清掉（再点确定就落盘成空名，
            // 运行期报"变量名称不能为空"）。端口是这一行的真值来源，重算候选不该改值。
            var keep = VariableName.GetTypedValue();

            NameCandidates.Clear();
            if (_configContext?.Variables != null)
            {
                foreach (var option in _configContext.Variables)
                {
                    if (option == null || string.IsNullOrWhiteSpace(option.Name))
                        continue;
                    if (!string.Equals(option.Scope, scope, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (NameCandidates.Contains(option.Name))
                        continue;

                    NameCandidates.Add(option.Name);
                }
            }

            if (!string.IsNullOrEmpty(keep)
                && !string.Equals(VariableName.GetTypedValue(), keep, StringComparison.Ordinal))
            {
                VariableName.Value = keep;
            }
        }

        /// <summary>
        /// 重算提示：这一行现在指向谁、那个变量是什么类型、找不到时会怎么报错。
        /// 文案按"人在现场会怎么问"写（"为什么写不进去"），不翻译内部术语。
        /// </summary>
        private void RefreshNameHint()
        {
            var scope = CurrentScope;
            bool isGlobal = string.Equals(scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase);
            var name = VariableName.GetTypedValue();

            var match = FindOption(name, scope);
            // 同名但另一个作用域也有：这是最容易让人以为"变量明明建好了"的情形，单独说一句
            var otherScopeMatch = match == null ? FindOption(name, isGlobal ? ScopeRuntime : ScopeGlobal) : null;

            var text = !string.IsNullOrEmpty(_legacyNameLink)
                ? $"该行原由上游提供（{_legacyNameLink}）—— 在本行选/输一个变量名，即可改为手填。"
                : string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                NameHint = text + (isGlobal
                    ? "还没填名字。作用域=全局变量时，名字要来自变量管理（点右侧箭头选）。"
                    : "还没填名字。作用域=运行时变量时，名字通常来自上游「变量定义」节点。");
                return;
            }

            if (match != null)
            {
                NameHint = text + (isGlobal
                    ? $"写入全局变量「{match.Name}」（{match.TypeName}）——「值」按它的类型解析（如 Int32 填 21）。"
                    : $"写入运行时变量「{match.Name}」（{match.TypeName}）· {match.Description}。");
                return;
            }

            if (otherScopeMatch != null)
            {
                NameHint = text + (isGlobal
                    ? $"「{name}」是运行时变量（{otherScopeMatch.TypeName}），不在变量管理里 —— 要写它请把作用域改成「运行时变量」。"
                    : $"「{name}」是全局变量（{otherScopeMatch.TypeName}），不是上游声明的运行时变量 —— 要写它请把作用域改成「全局变量」。");
                return;
            }

            NameHint = text + (isGlobal
                ? $"变量管理里没有「{name}」：运行期会明确报「找不到全局变量」，不会静默新建。"
                : $"「{name}」不在上游的变量定义里：需要它运行时已存在，或勾上「不存在时自动创建」。");
        }

        /// <summary>在宿主快照里按"名字 + 作用域"找一条候选（找不到返回 null）</summary>
        private VariableOption FindOption(string name, string scope)
            => _configContext?.Variables?.FirstOrDefault(o =>
                o != null
                && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(o.Scope, scope, StringComparison.OrdinalIgnoreCase));

        /// <summary>当前作用域（认不出来按运行时 —— 与 RunAlgorithm 的白名单校验不同，这里只是界面取候选）</summary>
        private string CurrentScope
        {
            get
            {
                var scope = Scope.GetTypedValue();
                return string.Equals(scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase)
                    ? ScopeGlobal
                    : ScopeRuntime;
            }
        }

        #endregion

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
