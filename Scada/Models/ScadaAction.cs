using System;

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
        /// 本动作的关键参数（属性面板上"这一行右边要显示的短文本"）。
        /// 单独开一个只读属性，是为了让界面不必各自去 switch 一遍动作类型——
        /// 那份 switch 与 <see cref="Describe"/> 是同一份知识，只该存在一处。
        /// </summary>
        public string Detail => Type switch
        {
            ScadaActionType.Log => _text ?? string.Empty,
            ScadaActionType.WriteVariable => $"{_variableName} := {_value}",
            ScadaActionType.Navigate => _targetPageName ?? string.Empty,
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
            _ => Type.DisplayName(),
        };
    }
}
