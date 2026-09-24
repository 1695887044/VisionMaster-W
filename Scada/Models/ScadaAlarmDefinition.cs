using System;
using System.Globalization;
using Newtonsoft.Json;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 一条报警定义（<b>配置</b>，落盘）。它只说"什么条件算报警"，
    /// 不说"现在报没报"——后者是 <see cref="ScadaAlarmRecord"/> 的事。
    ///
    /// 为什么配置与记录要分成两个类型
    /// ---------
    /// 两者的生命周期完全不同：定义跟着方案文件走，用户改一次用一年；
    /// 记录是运行态产物，一次报警一条，按天滚动写历史。合成一个类型的话，
    /// 要么把运行态字段（时间戳、当前态）写进 .vms，要么让历史文件里躺着配置字段——
    /// 两种都是"两个真相"，早晚对不上。
    ///
    /// 寻址口径与 <see cref="ScadaBinding"/> <b>逐字一致</b>（Id 优先、名字兜底）：
    /// 报警要挂在变量上，与画面绑定挂在变量上是同一件事，没有理由发明第二套寻址规则。
    /// 于是 <see cref="Matches"/> 与 <see cref="IsLegacyByName"/> 也从那里照抄语义——
    /// 变量改名级联（<see cref="ScadaDocument.RefreshVariableReferences"/>）会一并修到报警上。
    /// </summary>
    public class ScadaAlarmDefinition : ScadaModelBase
    {
        private Guid _alarmId = Guid.NewGuid();
        private string _name = string.Empty;
        private Guid _variableId;
        private string? _variableName;
        private ScadaAlarmKind _kind = ScadaAlarmKind.High;
        private double _threshold;
        private ScadaAlarmSeverity _severity = ScadaAlarmSeverity.Warning;
        private double _deadband;
        private double _delaySeconds;
        private double _staleSeconds = DefaultStaleSeconds;
        private bool _isEnabled = true;
        private string? _message;
        private string? _alarmGroup;
        private string? _cause;
        private string? _remedy;
        private string? _extraInfo;

        /// <summary>通信断线判定的默认超时（秒）。现场常见的轮询周期是 100ms~1s，10 秒足够宽松</summary>
        public const double DefaultStaleSeconds = 10;

        /// <summary>
        /// 报警定义的稳定身份（落盘）。与 <see cref="ScadaPage.PageId"/> / <see cref="ScadaElement.ElementId"/>
        /// 同一口径：<b>构造即非空</b>，<c>Guid.Empty</c> 只可能出现在旧文件里，由 <see cref="ScadaDocument.EnsureIdentity"/> 补齐。
        ///
        /// 为什么初始值不能省：引擎的 <c>Acknowledge(alarmId)</c> 按身份找记录，且明确拒绝 <c>Guid.Empty</c>。
        /// 若新建的报警没有身份，运行中刚加的那条报警就<b>点确认没反应</b>，而存一次盘再打开（EnsureIdentity 补过 Id）
        /// 就好了——一个只在"新加、还没存过盘"这段时间里存在的 bug，最难被复现。
        /// </summary>
        public Guid AlarmId
        {
            get => _alarmId;
            set => SetProperty(ref _alarmId, value);
        }

        /// <summary>报警名（必填，方案内唯一）。同时是报警列表与历史里的主标题</summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value ?? string.Empty);
        }

        /// <summary>被监视变量的稳定身份（权威寻址键）。<c>Guid.Empty</c> = 旧数据，只能按名字找</summary>
        public Guid VariableId
        {
            get => _variableId;
            set => SetProperty(ref _variableId, value);
        }

        /// <summary>变量名（展示串 + 旧数据兜底键）。机器寻址一律用 <see cref="VariableId"/></summary>
        public string? VariableName
        {
            get => _variableName;
            set => SetProperty(ref _variableName, value);
        }

        /// <summary>
        /// 条件种类（高/高高/低/低低/布尔/断线）。
        /// 改种类时<b>不</b>自动改严重度：用户可能特意把"高高限"降成警告
        /// （比如某台设备的"高高"只是工艺上限而非安全极限），自动改写会静默推翻他的决定。
        /// 默认值只在新建时给一次（见 <see cref="ScadaDocument.AddAlarm"/>）。
        /// </summary>
        public ScadaAlarmKind Kind
        {
            get => _kind;
            set => SetProperty(ref _kind, value);
        }

        /// <summary>阈值。仅"四限"使用；布尔与断线种类下这个值无意义（留着是为了改回四限时不用重填）</summary>
        public double Threshold
        {
            get => _threshold;
            set => SetProperty(ref _threshold, value);
        }

        /// <summary>严重度（决定列表排序与配色）</summary>
        public ScadaAlarmSeverity Severity
        {
            get => _severity;
            set => SetProperty(ref _severity, value);
        }

        /// <summary>
        /// 回差（死区）。激活后，值必须回到"阈值再往回让开这么多"之外才算恢复。
        ///
        /// 为什么这个字段是必需的而不是"高级选项"：测量值在阈值附近抖动是常态
        /// （传感器噪声、PID 的稳态振荡），没有回差就会在阈值上下每跳一次产一条报警记录，
        /// 一晚上能刷出几千条——现场管这叫"报警洪水"，而报警系统一旦开始刷屏，
        /// 操作员就会把整个报警条当背景噪声看，真正的报警也就没人看了。
        /// </summary>
        public double Deadband
        {
            get => _deadband;
            set => SetProperty(ref _deadband, Math.Max(0, value));
        }

        /// <summary>
        /// 激活延时（秒）。条件必须<b>连续</b>成立这么久才真正报警；0 = 立即报。
        /// 与 <see cref="Deadband"/> 是两道不同的消抖闸门：回差治"来回跳"，延时治"短脉冲"。
        /// </summary>
        public double DelaySeconds
        {
            get => _delaySeconds;
            set => SetProperty(ref _delaySeconds, Math.Max(0, value));
        }

        /// <summary>通信断线判定的超时秒数（仅 <see cref="ScadaAlarmKind.Stale"/> 使用，下限 1 秒）</summary>
        public double StaleSeconds
        {
            get => _staleSeconds;
            set => SetProperty(ref _staleSeconds, value < 1 ? 1 : value);
        }

        /// <summary>是否启用。停用时运行态完全跳过它（连变量都不订阅），配置保留</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>报警文本。留空时回落成 <see cref="Name"/>（见 <see cref="DisplayText"/>）</summary>
        public string? Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        /// <summary>
        /// 报警组（自由文本，如"一号线"/"冷却水"）。用于把几百条报警按设备或工艺段归类，
        /// 让操作员在报警列表与历史查询里能只看自己负责的那一组。
        ///
        /// 为什么是自由文本而不是枚举：组名随现场工艺划分走，各厂各线都不一样，
        /// 枚举一发布就不能改（见 <see cref="ScadaAlarmKind"/> 的数值稳定性约定），
        /// 用户想加一个组就得改代码发版本。配置界面提供"历史值下拉"兼顾录入效率与自由度。
        /// </summary>
        public string? AlarmGroup
        {
            get => _alarmGroup;
            set => SetProperty(ref _alarmGroup, value);
        }

        /// <summary>
        /// 故障原因（长文本）。写"为什么会出现这条报警"，如"冷却水泵停转或阀门被误关"。
        ///
        /// 为什么与 <see cref="Remedy"/> 分开两个字段而不是合成一句"处理说明"：
        /// 现场查故障时是两条不同的动作路径——先照着原因去核实，再照着措施去处理。
        /// 合成一段话，操作员就得自己从里面挑哪句是"查什么"、哪句是"做什么"。
        /// </summary>
        public string? Cause
        {
            get => _cause;
            set => SetProperty(ref _cause, value);
        }

        /// <summary>解决措施（长文本）。写"该怎么做"，如"检查水泵电源与接触器，确认后复位热继电器"</summary>
        public string? Remedy
        {
            get => _remedy;
            set => SetProperty(ref _remedy, value);
        }

        /// <summary>附加信息（长文本）。留给无法归入原因或措施的补充，如"停机后需重新标定"</summary>
        public string? ExtraInfo
        {
            get => _extraInfo;
            set => SetProperty(ref _extraInfo, value);
        }

        /// <summary>是否是"只能按名字找"的旧数据报警（只有这种才需要改名级联修名字）</summary>
        [JsonIgnore]
        public bool IsLegacyByName => VariableId == Guid.Empty && !string.IsNullOrWhiteSpace(VariableName);

        /// <summary>报警列表与历史里显示的文本：优先用 <see cref="Message"/>，没配就用名字</summary>
        [JsonIgnore]
        public string DisplayText => string.IsNullOrWhiteSpace(_message) ? _name : _message!;

        /// <summary>阈值的人话描述（面板与导出用），如 <c>"高限 &gt; 80"</c> / <c>"通信断线 &gt; 10s"</c></summary>
        [JsonIgnore]
        public string ConditionText
        {
            get
            {
                if (Kind == ScadaAlarmKind.Stale)
                    return $"{Kind.DisplayName()} > {_staleSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s";
                if (Kind.IsBoolean())
                    return Kind.DisplayName();

                string op = Kind == ScadaAlarmKind.High || Kind == ScadaAlarmKind.HighHigh ? ">" : "<";
                string text = $"{Kind.DisplayName()} {op} {_threshold.ToString("0.###", CultureInfo.InvariantCulture)}";
                return _deadband > 0
                    ? $"{text}（回差 {_deadband.ToString("0.###", CultureInfo.InvariantCulture)}）"
                    : text;
            }
        }

        /// <summary>
        /// 绑定/自愈回填：写入变量稳定身份与最新名字。语义与 <see cref="ScadaBinding.Bind"/> 相同，
        /// 两种调用场景也相同（用户选了变量 / 加载旧方案后按名解析成功把 Id 补回来）。
        /// </summary>
        public void Bind(Guid variableId, string? variableName)
        {
            VariableId = variableId;
            VariableName = variableName;
        }

        /// <summary>
        /// 命中判定：判断"被改名的变量"是不是本报警监视的那个。口径与 <see cref="ScadaBinding.Matches"/> 逐条同构——
        /// 有 Id 只认 Id（名字可能已过期而 Id 永不变），没 Id 才按名字比且忽略大小写。
        /// </summary>
        public bool Matches(Guid variableId, string? oldName)
        {
            if (VariableId != Guid.Empty)
                return variableId != Guid.Empty && VariableId == variableId;

            return !string.IsNullOrEmpty(oldName)
                && string.Equals(VariableName, oldName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
