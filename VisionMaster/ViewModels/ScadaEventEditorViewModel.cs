using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Prism.Commands;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 「事件 + 动作表」编辑器（公共控件 <c>ScadaEventEditor</c> 的视图模型）：
    /// 一个勾选框 = "这个事件有没有配钩子"，勾上之后内联展开一张<b>动作表</b>
    /// （增 / 删 / 改类型 / 改日志文案 / 调顺序 / 选变量 / 选画面 / 选流程 / 选轴）。
    ///
    /// <b>为什么单独抽这一层</b>：同一套编辑逻辑现在有两个消费者——属性面板的事件行
    /// （图元事件、画面事件）和独立的「变量事件」弹窗（变量级 5 类事件）。
    /// 两个消费者都在 <c>VisionMaster</c> 程序集内，所以这份逻辑落在这里既能让两边共用，
    /// 又不必把它下沉到 <c>VM.Scada.Controls</c>（那里刻意不引 UI 工程，见其 csproj 注释），
    /// 从而不用为了一处复用去撬开主题层的颜色令牌。
    ///
    /// <b>只认接口不认具体宿主</b>：依赖的是 <see cref="IScadaEventHost"/> 那六个成员，
    /// 于是"图元 / 画面 / 变量事件"三种宿主在这里走的是同一份代码，一行分支都不需要。
    ///
    /// <b>勾上 = 建钩子并自动补一条默认「记录日志」动作</b>：空动作表在执行侧等同没配，
    /// 不补的话用户勾完立刻去运行，看到的就是"点了没反应"，那比勾不上更伤信任。
    /// 取消勾选 = 整条钩子（连同它下面的动作）一起摘掉，三种宿主同一口径。
    ///
    /// <b>设计期只有数据进出，绝不执行动作</b>：这里是改模型，动作执行是运行态会话的事。
    ///
    /// <b>动作表为什么直接双向绑到模型对象</b>：动作本来就是有变更通知的模型对象
    /// （<see cref="ScadaAction"/>），没有"文本形态"这回事，中间再套一层影子状态只会多一份要同步的东西。
    ///
    /// <b>顺序有语义</b>：执行侧按 <see cref="ScadaEventHook.Actions"/> 的集合顺序依次执行，
    /// 所以"上移/下移"不是排版功能，是在改运行行为。
    /// </summary>
    public sealed class ScadaEventEditorViewModel : BindableBase
    {
        /// <summary>
        /// 动作类型下拉的全部候选（枚举扩展时自动带上，三种宿主共用同一份静态列表）。
        /// 枚举值按“末尾追加”的落盘纪律排（PauseFlow/ResumeFlow 是 16/17），但下拉里
        /// 按类别分组显示——流程类排在一起，运动类排在一起；只改显示顺序，不改值。
        /// </summary>
        private static readonly IReadOnlyList<ScadaActionTypeOption> TypeChoices
            = Enum.GetValues<ScadaActionType>()
                .OrderBy(ActionDisplayOrder)
                .Select(t => new ScadaActionTypeOption(t))
                .ToArray();

        private static int ActionDisplayOrder(ScadaActionType type) => type switch
        {
            ScadaActionType.Log => 0,
            ScadaActionType.WriteVariable => 1,
            ScadaActionType.Increment => 1,
            ScadaActionType.Decrement => 1,
            ScadaActionType.Navigate => 2,
            ScadaActionType.NavigateBack => 3,
            ScadaActionType.OpenPopup => 4,
            ScadaActionType.ClosePopup => 5,
            ScadaActionType.StartFlow => 10,
            ScadaActionType.PauseFlow => 11,
            ScadaActionType.ResumeFlow => 12,
            ScadaActionType.StopFlow => 13,
            ScadaActionType.RunFlowOnce => 14,
            ScadaActionType.MotionEnable => 20,
            ScadaActionType.MotionDisable => 21,
            ScadaActionType.MotionMoveAbsolute => 22,
            ScadaActionType.MotionMoveRelative => 23,
            ScadaActionType.MotionHome => 24,
            ScadaActionType.MotionStop => 25,
            ScadaActionType.MotionClearAlarm => 26,
            ScadaActionType.MotionJogStart => 27,
            ScadaActionType.MotionJogStop => 28,
            // 方案级配方：排在流程/运动之后——它是"整单换参数"，不是单步动作，
            // 与运动命令族混排会让"这条动作到底动没动设备"变得难判断。
            ScadaActionType.ApplyRecipe => 30,
            _ => 100,
        };

        /// <param name="host">本次编辑的目标宿主：图元 / 画面 / 变量事件，见 <see cref="IScadaEventHost"/></param>
        /// <param name="eventType">本编辑器对应的组态事件</param>
        /// <param name="displayName">勾选框上显示的事件名（与运行日志里那句同一个出处）</param>
        /// <param name="description">悬停说明</param>
        /// <param name="axisPicker">
        /// 「选轴」入口（运动命令族用）。<b>可选</b>：它是在"运动命令族"之后才加的依赖，
        /// 而本类的既有消费者（属性面板的事件行、变量事件弹窗）与断言工程都在按老签名构造。
        /// 为 null 时「选轴」按钮不可用（而不是静默空跑）——见 <see cref="PickAxisCommand"/>。
        /// </param>
        /// <param name="recipePicker">
        /// 「选配方」入口（「应用配方」动作用）。同样是<b>可选</b>参数：老消费者按 8 参构造，
        /// 为 null 时「选择配方」按钮不可用——见 <see cref="PickRecipeCommand"/>。
        /// </param>
        public ScadaEventEditorViewModel(
            IScadaEventHost host,
            ScadaEventType eventType,
            string displayName,
            string? description,
            IScadaVariablePicker picker,
            IScadaPagePicker pagePicker,
            IScadaFlowPicker flowPicker,
            IScadaAxisPicker? axisPicker = null,
            IScadaRecipePicker? recipePicker = null)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            EventType = eventType;
            DisplayName = displayName ?? string.Empty;
            Description = description;
            _picker = picker ?? throw new ArgumentNullException(nameof(picker));
            _pagePicker = pagePicker ?? throw new ArgumentNullException(nameof(pagePicker));
            _flowPicker = flowPicker ?? throw new ArgumentNullException(nameof(flowPicker));
            _axisPicker = axisPicker;
            _recipePicker = recipePicker;

            // 配方切换只允许挂在按钮事件上（用户口径：SCADA 只能通过按钮切配方）。
            // 页面加载/变量事件里即使手改 .vms 配了，运行侧仍会执行；这里收的是配置入口。
            ActionTypeChoices = AllowsRecipeSwitch()
                ? TypeChoices
                : TypeChoices.Where(t => t.Type != ScadaActionType.ApplyRecipe).ToArray();

            AddActionCommand = new DelegateCommand(OnAddAction);
            RemoveActionCommand = new DelegateCommand<ScadaAction>(OnRemoveAction, a => IndexOf(a) >= 0);
            MoveActionUpCommand = new DelegateCommand<ScadaAction>(a => OnMoveAction(a, -1), a => CanMove(a, -1));
            MoveActionDownCommand = new DelegateCommand<ScadaAction>(a => OnMoveAction(a, +1), a => CanMove(a, +1));
            PickVariableCommand = new DelegateCommand<ScadaAction>(OnPickVariable, a => a != null);
            PickPageCommand = new DelegateCommand<ScadaAction>(OnPickPage, a => a != null);
            PickFlowCommand = new DelegateCommand<ScadaAction>(OnPickFlow, a => a != null);
            // 没接选轴入口时按钮必须是"灰的"：能点却什么都不发生，用户只会怀疑软件坏了。
            PickAxisCommand = new DelegateCommand<ScadaAction>(OnPickAxis, a => a != null && _axisPicker != null);
            // 「选配方」同一口径：没接入口就灰掉，不留"点了没反应"的按钮。
            PickRecipeCommand = new DelegateCommand<ScadaAction>(OnPickRecipe, a => a != null && _recipePicker != null);
        }

        /// <summary>「选变量」入口（弹窗怎么弹、弹哪个由这一层决定，见 IScadaVariablePicker）</summary>
        private readonly IScadaVariablePicker _picker;

        /// <summary>「选画面」入口（弹窗怎么弹、弹哪个由这一层决定，见 IScadaPagePicker）</summary>
        private readonly IScadaPagePicker _pagePicker;

        /// <summary>「选流程」入口（弹窗怎么弹、弹哪个由这一层决定，见 IScadaFlowPicker）</summary>
        private readonly IScadaFlowPicker _flowPicker;

        /// <summary>「选轴」入口（运动命令族用）；可为 null = 没接（见构造参数说明）</summary>
        private readonly IScadaAxisPicker? _axisPicker;

        /// <summary>「选配方」入口（「应用配方」动作用）；可为 null = 没接（见构造参数说明）</summary>
        private readonly IScadaRecipePicker? _recipePicker;

        /// <summary>当前盯住的钩子（只为刷新危险标记/确认开关，见 <see cref="WatchHook"/>）</summary>
        private ScadaEventHook? _watchedHook;

        /// <summary>本次编辑的目标宿主（接口而不是具体类型，理由见类注释）</summary>
        public IScadaEventHost Host { get; }

        /// <summary>本编辑器对应的组态事件</summary>
        public ScadaEventType EventType { get; }

        /// <summary>勾选框上显示的事件名</summary>
        public string DisplayName { get; }

        /// <summary>悬停说明</summary>
        public string? Description { get; }

        /// <summary>
        /// 这个事件配没配钩子（勾选框的真值）。
        /// 真值始终从宿主回读——不缓存字段，"钩子被别处摘了"也能如实反映。
        /// 赋同值会被短路掉（等价于原来"已配过的钩子原样保留"的语义）。
        /// </summary>
        public bool IsConfigured
        {
            get => Host.FindEventHook(EventType) != null;
            set
            {
                if (value == IsConfigured) return;

                if (value)
                {
                    // 外层包一次作用域，把"建钩子"和"塞首条动作"合成一条撤销记录——
                    // 单看 AddEventHook 自带的那层，首条动作的 Add 落在作用域外，就记不上。
                    using (Host.BeginEdit($"配置事件 [{EventType}]"))
                    {
                        var hook = Host.AddEventHook(EventType);
                        hook.Actions.Add(ScadaChangeScope.Detached(() => new ScadaAction { Type = ScadaActionType.Log }));
                    }
                }
                else
                {
                    // 返回值不用看：读出来是 true 才可能走到这，没得删也无妨
                    Host.RemoveEventHook(EventType);
                }

                RaiseConfiguredChanged();
            }
        }

        /// <summary>
        /// 本事件已配的动作表；<b>没配钩子时为 <c>null</c></b>——"没有动作表"和"有一张空表"
        /// 是两回事，前者是压根没配，后者是配了但把动作删光了。模板据此整块隐藏动作区。
        /// </summary>
        public ObservableCollection<ScadaAction>? Actions => Host.FindEventHook(EventType)?.Actions;

        /// <summary>有动作表可编辑（= 已勾上），驱动动作区的可见性</summary>
        public bool HasActions => Actions != null;

        /// <summary>
        /// 运行态命中该事件时是否先弹二次确认（P0）。
        /// 真值从钩子回读；没配钩子时为 false（确认行随动作区一起隐藏）。
        /// </summary>
        public bool RequireConfirmation
        {
            get => Host.FindEventHook(EventType)?.RequireConfirmation ?? false;
            set
            {
                var hook = Host.FindEventHook(EventType);
                if (hook == null || hook.RequireConfirmation == value)
                    return;

                using (Host.BeginEdit($"{(value ? "开启" : "关闭")} [{EventType}] 执行前确认"))
                    hook.RequireConfirmation = value;

                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// 确认框里的后果提示（P0）；空 = 执行侧用动作摘要。
        /// 绑定用 LostFocus 提交，避免每敲一个字就进一次撤销栈。
        /// </summary>
        public string? ConfirmationMessage
        {
            get => Host.FindEventHook(EventType)?.ConfirmationMessage;
            set
            {
                var hook = Host.FindEventHook(EventType);
                if (hook == null)
                    return;

                var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                if (string.Equals(hook.ConfirmationMessage, text, StringComparison.Ordinal))
                    return;

                using (Host.BeginEdit($"修改 [{EventType}] 确认提示"))
                    hook.ConfirmationMessage = text;

                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// 动作表里是否有危险动作（P0）。只做编辑器上的提示；运行态的红色语气
        /// 由分发器按同一份 <see cref="ScadaActionTypeExtensions.IsDangerous"/> 判定。
        /// </summary>
        public bool IsDangerous => Actions?.Any(a => a != null && a.Type.IsDangerous()) == true;

        /// <summary>动作类型下拉的候选（所有事件共用同一份静态列表，按宿主/事件裁剪「应用配方」）</summary>
        public IReadOnlyList<ScadaActionTypeOption> ActionTypeChoices { get; }

        public DelegateCommand AddActionCommand { get; }

        public DelegateCommand<ScadaAction> RemoveActionCommand { get; }

        public DelegateCommand<ScadaAction> MoveActionUpCommand { get; }

        public DelegateCommand<ScadaAction> MoveActionDownCommand { get; }

        /// <summary>「选变量」：参数是那条写变量动作（弹窗确认后回填它的 Id 与名字）</summary>
        public DelegateCommand<ScadaAction> PickVariableCommand { get; }

        /// <summary>「选画面」：参数是那条切换画面动作（弹窗确认后回填它的 Id 与名字）</summary>
        public DelegateCommand<ScadaAction> PickPageCommand { get; }

        /// <summary>「选流程」：参数是那条流程动作（弹窗确认后回填它的 Id 与名字）</summary>
        public DelegateCommand<ScadaAction> PickFlowCommand { get; }

        /// <summary>
        /// 「选轴」：参数是那条运动动作（弹窗确认后回填它的 Id 与名字）。
        /// 走<b>单选</b>：一条运动动作只操作一根轴（见 <see cref="IScadaAxisPicker.PickSingle"/>）。
        /// 没接选轴入口时 CanExecute 为假（按钮灰掉），不会出现"点了没反应"。
        /// </summary>
        public DelegateCommand<ScadaAction> PickAxisCommand { get; }

        /// <summary>
        /// 「选配方」：参数是那条「应用配方」动作（弹窗确认后回填它的 Id 与名字）。
        /// 没接选择器时 CanExecute 为假（按钮灰掉），不会出现"点了没反应"。
        /// </summary>
        public DelegateCommand<ScadaAction> PickRecipeCommand { get; }

        /// <summary>
        /// 勾选框真值的文本形态：供 <see cref="ScadaEventRow"/> 沿用基类
        /// <c>Value</c> / <c>Commit</c> 那条通道（断言钉的就是 <c>BoolValue</c>）。
        /// </summary>
        public string ReadConfigured() => IsConfigured ? "True" : "False";

        /// <summary>把一段文本（"True"/"False"）当作勾选框的提交。解析不了就原样拒绝。</summary>
        public bool CommitConfigured(string text)
        {
            if (!bool.TryParse(text, out var flag))
                return false;

            IsConfigured = flag;
            return true;
        }

        /// <summary>
        /// 从宿主重新读一遍对外状态并广播。
        ///
        /// 由面板/弹窗在外部可能改过模型之后显式调用（属性面板是"面板统一刷新"而非行自订阅：
        /// <c>foreach (var row in _rows) row.RefreshValue();</c>）。动作集合与其中任意一条的
        /// 属性变更，都会经钩子 → 宿主 → 面板汇到这条路上来。
        /// </summary>
        public void RefreshFromHost()
        {
            RaiseConfiguredChanged();
        }

        private void RaiseConfiguredChanged()
        {
            WatchHook();
            RaisePropertyChanged(nameof(IsConfigured));
            RaisePropertyChanged(nameof(Actions));
            RaisePropertyChanged(nameof(HasActions));
            RaisePropertyChanged(nameof(RequireConfirmation));
            RaisePropertyChanged(nameof(ConfirmationMessage));
            RaisePropertyChanged(nameof(IsDangerous));

            // 上/下/删的可用性取决于"这条动作在表里的位置"，位置一变就得重算——
            // 否则第一条动作的"↑"按钮还亮着，点下去没反应。
            RemoveActionCommand.RaiseCanExecuteChanged();
            MoveActionUpCommand.RaiseCanExecuteChanged();
            MoveActionDownCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 盯住当前钩子的 PropertyChanged：动作增删/改类型会经
        /// <see cref="ScadaEventHook.Actions"/> 冒上来，确认开关会经
        /// <see cref="ScadaEventHook.RequireConfirmation"/> 冒上来。
        /// 只为了把「危险动作」标记与确认行状态刷成最新，不参与任何业务判定。
        /// </summary>
        private void WatchHook()
        {
            var hook = Host.FindEventHook(EventType);
            if (ReferenceEquals(_watchedHook, hook))
                return;

            if (_watchedHook != null)
                _watchedHook.PropertyChanged -= OnWatchedHookPropertyChanged;

            _watchedHook = hook;

            if (_watchedHook != null)
                _watchedHook.PropertyChanged += OnWatchedHookPropertyChanged;
        }

        private void OnWatchedHookPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ScadaEventHook.Actions))
                RaisePropertyChanged(nameof(IsDangerous));
            else if (e.PropertyName == nameof(ScadaEventHook.RequireConfirmation))
                RaisePropertyChanged(nameof(RequireConfirmation));
        }

        /// <summary>
        /// 「应用配方」是否允许出现在本事件的动作下拉里。
        /// 只认按钮图元（TypeKey 以 <c>Button</c> 结尾：Hmi.Button / Hmi.BitButton / Hmi.AckButton）
        /// 的按下/释放事件；页面事件、变量事件、普通图元一律不出这一项。
        /// </summary>
        private bool AllowsRecipeSwitch()
        {
            if (EventType is not (ScadaEventType.Pressed or ScadaEventType.Released))
                return false;

            return Host is ScadaElement element
                && !string.IsNullOrEmpty(element.TypeKey)
                && element.TypeKey.EndsWith("Button", StringComparison.OrdinalIgnoreCase);
        }

        private void OnAddAction()
        {
            // GetOrAdd 而不是 Find：能点到这个按钮说明勾选框是勾上的，钩子必然已在；
            // 真遇到"表被别处摘了"的竞态，这里补一条空钩子也比抛异常强。
            using (Host.BeginEdit($"新增 [{EventType}] 动作"))
            {
                Host.GetOrAddEventHook(EventType).Actions.Add(
                    ScadaChangeScope.Detached(() => new ScadaAction { Type = ScadaActionType.Log }));
            }

            RaiseConfiguredChanged();
        }

        private void OnRemoveAction(ScadaAction? action)
        {
            if (action == null) return;

            // 删掉最后一条动作<b>不</b>顺手摘钩子：那是"配置"与"行为"两件事，
            // 用户可能只是先把旧动作清掉、紧接着要加新的。空动作表在执行侧本来就等同没配，
            // 界面上用一行提示把这件事说明白，比替他做决定更诚实。
            using (Host.BeginEdit($"删除 [{EventType}] 动作"))
            {
                Actions?.Remove(action);
            }

            RaiseConfiguredChanged();
        }

        /// <summary>
        /// 「选变量」：把这条写变量动作当前的目标交给选择器，选中后回填。
        ///
        /// 回填走 <see cref="ScadaAction.BindVariable"/>（先 Id 再名字）：Id 是权威身份，
        /// 名字只作显示与"找不到 Id 时的兜底寻址"。取消时回调<b>一次都不触发</b>——
        /// 弹窗的取消不该被翻译成"清空原绑定"这种破坏性动作。
        /// </summary>
        private void OnPickVariable(ScadaAction? action)
        {
            if (action == null) return;

            // 回填要走作用域：Id 与名字是两次属性写，不包的话"选变量"这个动作压根撤不回来。
            _picker.Pick(action.VariableId, action.VariableName, action.ArrayIndex,
                (id, name, arrayIndex) =>
            {
                string label = new ScadaVariableRef(id, name, arrayIndex).DisplayText;

                using (Host.BeginEdit($"选择变量 [{label}]"))
                {
                    action.BindVariable(id, name, arrayIndex);
                }
            });
        }

        /// <summary>
        /// 「选画面」：把这条切换画面动作当前的目标交给选择器，选中后回填。
        /// 与「选变量」同一口径：Id 是权威身份，名字只作显示与兜底寻址。取消时回调<b>一次都不触发</b>。
        /// </summary>
        private void OnPickPage(ScadaAction? action)
        {
            if (action == null) return;

            void Picked(Guid id, string? name)
            {
                using (Host.BeginEdit($"选择目标画面 [{name}]"))
                {
                    action.BindPage(id, name);
                }
            }

            // 「打开弹窗」只列弹窗页；「切换画面」只列普通页（弹窗页只能由 OpenPopup 打开）。
            if (action.Type == ScadaActionType.OpenPopup)
                _pagePicker.PickPopup(action.TargetPageId, action.TargetPageName, Picked);
            else
                _pagePicker.Pick(action.TargetPageId, action.TargetPageName, Picked);
        }

        /// <summary>
        /// 「选流程」：把这条流程动作当前的目标交给选择器，选中后回填。
        /// 与「选变量」/「选画面」同一口径：Id 是权威身份，名字只作显示与兜底寻址。
        /// 取消时回调<b>一次都不触发</b>。三种流程动作（启动/停止/执行一次）共用这一个入口——
        /// 它们的参数形状一模一样（就一个目标流程），差异只在出口那侧的语义。
        /// </summary>
        private void OnPickFlow(ScadaAction? action)
        {
            if (action == null) return;

            _flowPicker.Pick(action.FlowId, action.FlowName, (id, name) =>
            {
                using (Host.BeginEdit($"选择目标流程 [{name}]"))
                {
                    action.BindFlow(id, name);
                }
            });
        }

        /// <summary>
        /// 「选轴」：把这条运动动作当前的目标轴交给选择器，选中后回填。
        /// 与「选变量」/「选画面」/「选流程」同一口径：Id 是权威身份，名字只作显示与兜底寻址。
        /// 取消时回调<b>一次都不触发</b>（取消不该被翻译成"清空原绑定"这种破坏性动作）。
        ///
        /// 走 <see cref="IScadaAxisPicker.PickSingle"/>：一条运动动作只操作一根轴。
        /// 入参是"当前已选的那一根"（没选则空表），让弹窗能预选并让用户看出现在配的是哪根。
        /// </summary>
        private void OnPickAxis(ScadaAction? action)
        {
            if (action == null || _axisPicker == null) return;

            var current = action.HasAxis
                ? new[] { new ScadaAxisRef(action.AxisId, action.AxisName) }
                : Array.Empty<ScadaAxisRef>();

            _axisPicker.PickSingle(current, picked =>
            {
                if (picked == null) return;

                // 回填要走作用域：Id 与名字是两次属性写，不包的话"选轴"这个动作压根撤不回来
                using (Host.BeginEdit($"选择轴 [{picked.Name}]"))
                {
                    action.BindAxis(picked.AxisId, picked.Name);
                }
            });
        }

        /// <summary>
        /// 「选配方」：把这条「应用配方」动作当前的目标交给选择器，选中后回填。
        /// 与「选变量」/「选画面」/「选流程」/「选轴」同一口径：Id 是权威身份，名字只作显示与兜底寻址。
        /// 取消时回调<b>一次都不触发</b>（取消不该被翻译成"清空原配方"这种破坏性动作）。
        /// </summary>
        private void OnPickRecipe(ScadaAction? action)
        {
            if (action == null || _recipePicker == null) return;

            _recipePicker.Pick(action.TargetRecipeId, action.TargetRecipeName, (id, name) =>
            {
                using (Host.BeginEdit($"选择配方 [{name}]"))
                {
                    action.BindRecipe(id, name);
                }
            });
        }

        private void OnMoveAction(ScadaAction? action, int offset)
        {
            var list = Actions;
            int index = IndexOf(action);

            if (list == null || index < 0) return;

            int target = index + offset;
            if (target < 0 || target >= list.Count) return;

            // Move 而不是"删了再插"：Move 只发一次 CollectionChanged，订阅链上少一轮摘挂，
            // 也不会让被移的那条动作在中间态里短暂地"不存在"。
            using (Host.BeginEdit($"调整 [{EventType}] 动作次序"))
            {
                list.Move(index, target);
            }

            RaiseConfiguredChanged();
        }

        private bool CanMove(ScadaAction? action, int offset)
        {
            var list = Actions;
            int index = IndexOf(action);

            if (list == null || index < 0) return false;

            int target = index + offset;
            return target >= 0 && target < list.Count;
        }

        private int IndexOf(ScadaAction? action)
            => action == null || Actions is not { } list ? -1 : list.IndexOf(action);
    }
}
