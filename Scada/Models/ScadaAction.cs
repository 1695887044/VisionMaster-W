using System;
using System.Globalization;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 一条<b>动作</b>：事件钩子命中后要执行的一件事。
    ///
    /// 为什么是"一个扁平类 + 类型字段"，不是"抽象基类 + LogAction/WriteVariableAction 派生"：
    /// 方案文件用 <c>TypeNameHandling.Auto</c> + <c>SafeSerializationBinder</c> 白名单，多态落盘会往
    /// .vms 里写 <c>$type</c>，每加一种动作都要过一遍白名单，白名单漏一项就是"存进去打不开"。
    /// <see cref="ScadaElement.TypeKey"/> 当年就是为同一个理由选了字符串。这里照抄那个结论：
    /// 动作的差异全在"用哪几个字段"上，不在行为上——行为在执行侧的 dispatcher 里。
    ///
    /// 代价是一堆"对本类型无意义"的字段（切画面用不着 <see cref="Value"/>）。
    /// 用 <see cref="Describe"/> 与执行侧的 switch 把"哪些字段属于哪种动作"说清楚，
    /// 比让每个消费方都做一次类型判断 + 强转要可靠得多。
    ///
    /// 变量寻址口径与 <see cref="ScadaBinding"/> 完全一致：<b>Id 优先、名字兜底</b>，
    /// 所以 <see cref="Matches"/> 可以照搬，变量改名级联也能用同一套遍历。
    /// </summary>
    public class ScadaAction : ScadaModelBase
    {
        private ScadaActionType _type = ScadaActionType.Log;
        private string? _text;
        private Guid _variableId;
        private string? _variableName;
        private string? _value;
        private Guid _targetPageId;
        private string? _targetPageName;
        private string? _flowId;
        private string? _flowName;
        private Guid _axisId;
        private string? _axisName;
        private double? _targetMm;
        private double? _velocityMmPerS;

        /// <summary>动作类型（决定下面哪几组字段有意义）</summary>
        public ScadaActionType Type
        {
            get => _type;
            set
            {
                if (SetProperty(ref _type, value))
                {
                    RaisePropertyChanged(nameof(Detail));
                    RaisePropertyChanged(nameof(PendingReason));
                    // IsMotionAction 由 Type 算出来：不广播的话，动作行上那块"轴 + 目标 + 速度"
                    // 的编辑区要等整块重建才出现/消失（用户改完类型看着像没生效）。
                    RaisePropertyChanged(nameof(IsMotionAction));
                }
            }
        }

        /// <summary>日志内容（<see cref="ScadaActionType.Log"/>）；空则由执行侧回落成"图元名 + 事件名"</summary>
        public string? Text
        {
            get => _text;
            set
            {
                if (SetProperty(ref _text, value))
                    RaisePropertyChanged(nameof(Detail));
            }
        }

        /// <summary>目标变量稳定身份（<see cref="ScadaActionType.WriteVariable"/>）。<c>Guid.Empty</c> = 旧数据只能按名找</summary>
        public Guid VariableId
        {
            get => _variableId;
            set
            {
                if (SetProperty(ref _variableId, value))
                    RaisePropertyChanged(nameof(HasVariable));
            }
        }

        /// <summary>目标变量名（展示串 + 旧数据兜底键；机器寻址一律用 <see cref="VariableId"/>）</summary>
        public string? VariableName
        {
            get => _variableName;
            set
            {
                if (SetProperty(ref _variableName, value))
                {
                    RaisePropertyChanged(nameof(Detail));
                    RaisePropertyChanged(nameof(HasVariable));
                }
            }
        }

        /// <summary>
        /// 是否已经选好目标变量（Id 或名字任一非空即算）。
        /// 给属性面板一个布尔位去切"未选择"提示与按钮文案——判定口径与执行侧
        /// <c>ExecuteWriteVariable</c> 的第一步逐字一致（那里判的也是这两个字段），
        /// 面板自己再拼一遍就等于留下第二份口径。
        /// </summary>
        public bool HasVariable => _variableId != Guid.Empty || !string.IsNullOrWhiteSpace(_variableName);

        /// <summary>
        /// 要写入的值（<see cref="ScadaActionType.WriteVariable"/>），以字符串承载。
        ///
        /// 一律 string 而不是 <c>double</c>：变量类型有 bool/整/浮/字符串，"点一下写 1"与
        /// "点一下写『运行』"是同一种配置动作，领域层不该替执行侧决定它能不能解析。
        /// 本阶段也<b>不支持表达式</b>（<c>@温度+5</c> 那种）——那是 S6 数据泵之后的事，
        /// 现在存下来只会得到一个"配了却没反应"的哑配置。
        /// </summary>
        public string? Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        /// <summary>目标画面稳定身份（<see cref="ScadaActionType.Navigate"/>）</summary>
        public Guid TargetPageId
        {
            get => _targetPageId;
            set
            {
                if (SetProperty(ref _targetPageId, value))
                    RaisePropertyChanged(nameof(HasTargetPage));
            }
        }

        /// <summary>目标画面名（展示串 + 旧数据兜底键）</summary>
        public string? TargetPageName
        {
            get => _targetPageName;
            set
            {
                if (SetProperty(ref _targetPageName, value))
                {
                    RaisePropertyChanged(nameof(Detail));
                    RaisePropertyChanged(nameof(HasTargetPage));
                }
            }
        }

        /// <summary>
        /// 是否已经选好目标画面（Id 或名字任一非空即算）。与 <see cref="HasVariable"/> 同一口径、
        /// 同一用途：给属性面板一个布尔位去切"未选择"提示与占位文案，
        /// 判定口径与执行侧 <c>ExecuteNavigate</c> 的第一步逐字一致（那里判的也是这两个字段）。
        /// </summary>
        public bool HasTargetPage => _targetPageId != Guid.Empty || !string.IsNullOrWhiteSpace(_targetPageName);

        /// <summary>
        /// 目标流程稳定身份（<see cref="ScadaActionType.StartFlow"/> / StopFlow / RunFlowOnce）。
        ///
        /// 为什么是 <c>string</c> 而不像变量/画面那样是 <see cref="Guid"/>：目标身份要跟着
        /// 流程模型走，而流程模型（Core 侧，本领域层刻意不引用它）的身份键 FlowID
        /// 本来就是字符串（Guid 的 "N" 格式文本）。这里原样承载那份文本，
        /// 寻址时由实现方与 FlowModel.FlowID 做字符串比对——转成 Guid 再比只会
        /// 多出一条"空串/坏文本怎么办"的口径，没有换来任何东西。
        /// </summary>
        public string? FlowId
        {
            get => _flowId;
            set
            {
                if (SetProperty(ref _flowId, value))
                    RaisePropertyChanged(nameof(HasFlow));
            }
        }

        /// <summary>目标流程名（展示串 + 旧数据兜底键；与 <see cref="TargetPageName"/> 同一分工）</summary>
        public string? FlowName
        {
            get => _flowName;
            set
            {
                if (SetProperty(ref _flowName, value))
                {
                    RaisePropertyChanged(nameof(Detail));
                    RaisePropertyChanged(nameof(HasFlow));
                }
            }
        }

        /// <summary>
        /// 是否已经选好目标流程（Id 或名字任一非空白即算）。与 <see cref="HasVariable"/> /
        /// <see cref="HasTargetPage"/> 同一口径、同一用途：给属性面板一个布尔位去切
        /// "未选择"提示与占位文案，判定口径与执行侧流程动作的第一步逐字一致。
        /// </summary>
        public bool HasFlow => !string.IsNullOrWhiteSpace(_flowId) || !string.IsNullOrWhiteSpace(_flowName);

        /// <summary>
        /// 目标轴稳定身份（运动九条）。<c>Guid.Empty</c> = 旧数据只能按名找。
        ///
        /// 与变量/画面同一条口径：<b>Id 优先、名字兜底</b>。轴的 <c>AxisId</c> 由运动模块的
        /// <c>AxisMapping.AxisId</c> 给出（新建轴给新 Guid、历史轴按 (卡Id, 轴号) 确定性补齐），
        /// 所以"轴改名"不会把已经配好的按钮打断——这正是 SCADA 绑定当初坚持用 Id 的理由。
        /// </summary>
        public Guid AxisId
        {
            get => _axisId;
            set
            {
                if (SetProperty(ref _axisId, value))
                {
                    RaisePropertyChanged(nameof(HasAxis));
                    RaisePropertyChanged(nameof(AxisDisplayName));
                    RaisePropertyChanged(nameof(Detail));
                }
            }
        }

        /// <summary>目标轴名（展示串 + 旧数据兜底键；机器寻址一律用 <see cref="AxisId"/>）</summary>
        public string? AxisName
        {
            get => _axisName;
            set
            {
                if (SetProperty(ref _axisName, value))
                {
                    RaisePropertyChanged(nameof(HasAxis));
                    RaisePropertyChanged(nameof(AxisDisplayName));
                    RaisePropertyChanged(nameof(Detail));
                }
            }
        }

        /// <summary>
        /// 是否已经选好目标轴（Id 或名字任一非空即算）。与 <see cref="HasVariable"/> /
        /// <see cref="HasTargetPage"/> / <see cref="HasFlow"/> 同一口径、同一用途：
        /// 给属性面板一个布尔位去切"未选择"提示与按钮文案，判定口径与执行侧
        /// <c>MotionActionExecutor.TryExecute</c> 的第一步逐字一致。
        /// </summary>
        public bool HasAxis => _axisId != Guid.Empty || !string.IsNullOrWhiteSpace(_axisName);

        /// <summary>
        /// 动作行上"轴：……"那一格显示的文本。
        ///
        /// 三种状态都要看得见（名字 / 只剩 Id / 还没选）：只绑 <see cref="AxisName"/> 的话，
        /// "选了轴但名字为空"（旧数据或轴被删）会显示成"还没选轴"，用户以为没配、
        /// 其实执行侧会按 Id 找到那根轴——界面与行为各说各话。
        /// </summary>
        public string AxisDisplayName => HasAxis ? AxisLabel : "（还没选轴）";

        /// <summary>
        /// 这条动作是不是运动命令族（九个）。给动作行模板一个布尔位去切"轴 / 目标 / 速度"那块编辑区。
        ///
        /// 为什么不直接在 XAML 里写九个 <c>DataTrigger Binding="{Binding Type}"</c>：WPF 的
        /// <c>DataTrigger</c> 只能判等、不支持"属于集合"，九个触发器写在一处既容易漏（加一种运动动作
        /// 就要记得补第九行）又看不出意图。判断"哪些类型算运动动作"这份知识留在领域层一处，
        /// 模板只问"是不是"。执行侧的 switch 与这里是同一份清单——那边少一条会走进 default 被如实跳过。
        /// </summary>
        public bool IsMotionAction => Type is
            ScadaActionType.MotionEnable or ScadaActionType.MotionDisable
            or ScadaActionType.MotionMoveAbsolute or ScadaActionType.MotionMoveRelative
            or ScadaActionType.MotionHome or ScadaActionType.MotionStop
            or ScadaActionType.MotionClearAlarm
            or ScadaActionType.MotionJogStart or ScadaActionType.MotionJogStop;

        /// <summary>
        /// 定位目标（mm）：绝对定位=目标点；相对定位=增量（可为负）；点动=方向（正/负号，见 <see cref="Describe"/>）。
        ///
        /// 为什么是 <c>double?</c> 而不是 <c>double</c>：属性面板要把"没填"与"填了 0"分开——
        /// 定位目标 0 mm 是一个完全合法的目标（回原点位置），拿 0 当"没填"会让用户配不出"走到零点"。
        /// 执行侧对 null 的处置是拒绝（"没填目标位置"），而不是悄悄按 0 下发。
        /// </summary>
        public double? TargetMm
        {
            get => _targetMm;
            set
            {
                if (SetProperty(ref _targetMm, value))
                    RaisePropertyChanged(nameof(Detail));
            }
        }

        /// <summary>
        /// 速度（mm/s）。null / &lt;=0 都表示"用卡级默认值"（与 <c>MotionCommand.VelocityMmPerS</c> 的约定一致）。
        ///
        /// 为什么允许留空而不是必填：卡级默认速度在「运动卡设置」里已经配过一遍，
        /// 每个按钮再抄一次既啰嗦又容易与卡级配置不一致（改了卡级默认，按钮上的旧值还在）。
        /// </summary>
        public double? VelocityMmPerS
        {
            get => _velocityMmPerS;
            set
            {
                if (SetProperty(ref _velocityMmPerS, value))
                    RaisePropertyChanged(nameof(Detail));
            }
        }

        /// <summary>
        /// 本动作的关键参数（属性面板上"这一行右边要显示的短文本"）。
        /// 单独开一个只读属性，是为了让界面不必各自去 switch 一遍动作类型——
        /// 那份 switch 与 <see cref="Describe"/> 是同一份知识，只该存在一处。
        /// </summary>
        public string Detail => Type switch
        {
            ScadaActionType.Log => _text ?? string.Empty,
            ScadaActionType.WriteVariable => $"{_variableName} := {_value}",
            ScadaActionType.Navigate => _targetPageName ?? string.Empty,
            // 流程三条与切画面同款：行上显示目标流程名（未选则为空，面板另有占位与警告）
            ScadaActionType.StartFlow => _flowName ?? string.Empty,
            ScadaActionType.StopFlow => _flowName ?? string.Empty,
            ScadaActionType.RunFlowOnce => _flowName ?? string.Empty,
            // 运动九条的行内摘要（属性面板 260px 宽，只放"哪根轴 + 关键数字"，
            // 完整的措辞在 Describe 里给日志与审计用）。
            // 轴一律走 AxisLabel：没选轴时它给"（未选轴）"，只剩 Id 时给"轴#前8位"，
            // 不留一段空白（空白看不出是"没配"还是"配了但名字丢了"）。
            ScadaActionType.MotionEnable => AxisLabel,
            ScadaActionType.MotionDisable => AxisLabel,
            ScadaActionType.MotionHome => AxisLabel,
            ScadaActionType.MotionStop => AxisLabel,
            ScadaActionType.MotionClearAlarm => AxisLabel,
            ScadaActionType.MotionMoveAbsolute => $"{AxisLabel} → {Mm(_targetMm)}",
            ScadaActionType.MotionMoveRelative => $"{AxisLabel} 增量 {SignedMm(_targetMm)}",
            ScadaActionType.MotionJogStart => HasAxis
                ? $"{AxisLabel}（{(IsForward(_targetMm) ? "正向" : "反向")}）"
                : AxisLabel,
            ScadaActionType.MotionJogStop => AxisLabel,
            _ => string.Empty,
        };

        /// <summary>
        /// 这条动作在运行侧"还没接通"的一句话原因；<c>null</c> = 已经能真跑。
        /// 转发到 <see cref="ScadaActionTypeExtensions.PendingReason"/>，
        /// 让属性面板可以逐行绑定、不必各自去 switch 一遍类型。
        /// </summary>
        public string? PendingReason => Type.PendingReason();

        /// <summary>绑定/自愈回填变量（与 <see cref="ScadaBinding.Bind"/> 同一职责）</summary>
        public void BindVariable(Guid variableId, string? variableName)
        {
            VariableId = variableId;
            VariableName = variableName;
        }

        /// <summary>绑定/自愈回填目标画面（口径同 <see cref="BindVariable"/>：Id 是权威键，名字给人看）</summary>
        public void BindPage(Guid pageId, string? pageName)
        {
            TargetPageId = pageId;
            TargetPageName = pageName;
        }

        /// <summary>
        /// 绑定/回填目标流程（口径同 <see cref="BindVariable"/> / <see cref="BindPage"/>：
        /// Id 是权威键、名字只作展示与兜底寻址——流程的 Id 本身就是字符串，见 <see cref="FlowId"/>）。
        /// </summary>
        public void BindFlow(string flowId, string? flowName)
        {
            FlowId = flowId;
            FlowName = flowName;
        }

        /// <summary>
        /// 绑定/回填目标轴（口径同 <see cref="BindVariable"/> / <see cref="BindPage"/>：
        /// Id 是权威键，名字给人看、只在"Id 对不上"时兜底寻址）。
        /// </summary>
        public void BindAxis(Guid axisId, string? axisName)
        {
            AxisId = axisId;
            AxisName = axisName;
        }

        /// <summary>
        /// 命中的是不是"被改名的这根轴"。口径与 <see cref="Matches"/> 逐字一致
        /// （Id 优先、名字兜底），这样轴改名级联可以对绑定和动作复用同一次遍历。
        ///
        /// 只对运动类动作有意义，但判定本身不看动作类型：类型判错最多是白扫一条，
        /// 而两处各写一套判定迟早会不一致。
        /// </summary>
        public bool MatchesAxis(Guid axisId, string? oldName)
        {
            if (_axisId != Guid.Empty)
                return axisId != Guid.Empty && _axisId == axisId;

            return !string.IsNullOrEmpty(oldName)
                && string.Equals(_axisName, oldName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 点动方向是否为正。判据只有一条：<see cref="TargetMm"/> 的正负号（缺省 / 0 都算正向）。
        ///
        /// 为什么不新增一个"方向"枚举字段：点动是"往哪边走"，而"走到哪"与"往哪边走"
        /// 在动作表里不需要同时出现——一个字段两种解释，比多一个只在 1/9 种动作上有意义的
        /// 字段更容易在界面上配错（而且落盘时多一个字段就多一处旧数据缺省值要解释）。
        /// 执行侧 <c>MotionActionExecutor</c> 用同一个判据，两处不会分叉。
        /// </summary>
        public bool IsJogForward => IsForward(_targetMm);

        /// <summary>毫米的展示口径（三位小数、不变区域性：日志与审计在中文/英文系统上必须同形）</summary>
        private static string Mm(double? value)
            => value.HasValue
                ? value.Value.ToString("F3", CultureInfo.InvariantCulture) + " mm"
                : "（未填目标）";

        /// <summary>带符号的毫米（相对定位/点动要看出方向，正号必须显式给）</summary>
        private static string SignedMm(double? value)
            => value.HasValue
                ? value.Value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + " mm"
                : "（未填增量）";

        /// <summary>正负号判据（null 与 0 都按正向：界面上"没填"时不该显示成反向）</summary>
        private static bool IsForward(double? value) => !value.HasValue || value.Value >= 0;

        /// <summary>
        /// 命中的是不是"被改名的这个变量"（口径与 <see cref="ScadaBinding.Matches"/> 逐字一致，
        /// 这样改名级联可以对绑定和动作复用同一次遍历）。
        /// 只对写变量类动作有意义，但判定本身不看动作类型：类型判错最多是白扫一条，
        /// 而两处各写一套判定迟早会不一致。
        /// </summary>
        public bool Matches(Guid variableId, string? oldName)
        {
            if (_variableId != Guid.Empty)
                return variableId != Guid.Empty && _variableId == variableId;

            return !string.IsNullOrEmpty(oldName)
                && string.Equals(_variableName, oldName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 一行人类可读的动作描述（执行时写进日志、属性面板兜底展示）。
        /// 刻意由模型自己长出来而不是让执行侧拼字符串：日志里出现的措辞和面板上出现的措辞
        /// 必须是同一句话，否则用户对不上"我配的那条"和"日志里那条"。
        /// </summary>
        public string Describe() => Type switch
        {
            ScadaActionType.Log => string.IsNullOrEmpty(_text)
                ? "记录日志（内容未填）"
                : $"记录日志「{_text}」",
            ScadaActionType.WriteVariable => _variableId == Guid.Empty && string.IsNullOrEmpty(_variableName)
                ? "写变量（未选变量）"
                : $"写变量 {_variableName} := {_value}",
            ScadaActionType.Navigate => _targetPageId == Guid.Empty && string.IsNullOrEmpty(_targetPageName)
                ? "切换画面（未选画面）"
                : $"切换画面 → {_targetPageName}",
            // 流程三条的判定口径与 HasFlow 一致（Id 或名字任一非空白即算已选）
            ScadaActionType.StartFlow => !HasFlow
                ? "启动流程（未选流程）"
                : $"启动流程 {_flowName}",
            ScadaActionType.StopFlow => !HasFlow
                ? "停止流程（未选流程）"
                : $"停止流程 {_flowName}",
            ScadaActionType.RunFlowOnce => !HasFlow
                ? "执行一次流程（未选流程）"
                : $"执行一次流程 {_flowName}",
            // 运动九条：未选轴的措辞与另外三种同款（"××（未选轴）"），面板据此亮橙色提示。
            // 速度只在"配了才提"：没配速度走卡级默认是常态，每行都缀一句"默认速度"会把日志读花。
            ScadaActionType.MotionEnable => !HasAxis
                ? "轴使能（未选轴）"
                : $"轴使能 {AxisLabel}",
            ScadaActionType.MotionDisable => !HasAxis
                ? "轴失能（未选轴）"
                : $"轴失能 {AxisLabel}",
            ScadaActionType.MotionHome => !HasAxis
                ? "轴回零（未选轴）"
                : $"轴回零 {AxisLabel}",
            ScadaActionType.MotionStop => !HasAxis
                ? "轴停止（未选轴）"
                : $"轴停止 {AxisLabel}",
            ScadaActionType.MotionClearAlarm => !HasAxis
                ? "清轴报警（未选轴）"
                : $"清轴报警 {AxisLabel}",
            ScadaActionType.MotionMoveAbsolute => !HasAxis
                ? "轴绝对定位（未选轴）"
                : $"轴绝对定位 {AxisLabel} → {Mm(_targetMm)}{SpeedSuffix}",
            ScadaActionType.MotionMoveRelative => !HasAxis
                ? "轴相对定位（未选轴）"
                : $"轴相对定位 {AxisLabel} {SignedMm(_targetMm)}{SpeedSuffix}",
            ScadaActionType.MotionJogStart => !HasAxis
                ? "点动开始（未选轴）"
                : $"点动开始 {AxisLabel}（{(IsForward(_targetMm) ? "正向" : "反向")}）",
            ScadaActionType.MotionJogStop => !HasAxis
                ? "点动结束（未选轴）"
                : $"点动结束 {AxisLabel}",
            _ => Type.DisplayName(),
        };

        /// <summary>
        /// 行内摘要里那根轴的写法：
        /// 有名字 → 名字；只剩 Id → "轴#前 8 位"；两者都没有 → "（未选轴）"。
        /// 三种都给出一个可见的词，不留空白——空白看不出是"没配"还是"配了但名字丢了"。
        /// </summary>
        private string AxisLabel
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_axisName))
                    return _axisName!;

                return _axisId != Guid.Empty
                    ? $"轴#{_axisId.ToString("N")[..8]}"
                    : "（未选轴）";
            }
        }

        /// <summary>速度后缀（只在配了正速度时才缀上；0/负数在命令里等同"用卡级默认"）</summary>
        private string SpeedSuffix => _velocityMmPerS is > 0
            ? $" @ {_velocityMmPerS.Value.ToString("0.###", CultureInfo.InvariantCulture)} mm/s"
            : string.Empty;
    }
}
