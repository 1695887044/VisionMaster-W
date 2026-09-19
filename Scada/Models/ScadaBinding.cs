using System;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 画面绑定：把图元上的一个属性挂到一个工程变量上。
    ///
    /// 寻址口径与流程连线（Core.Interfaces.LinkReference）完全一致——<b>Id 优先、名字兜底</b>：
    /// - <see cref="VariableId"/> 是权威键，变量改名不影响命中；
    /// - <see cref="VariableName"/> 有两个职责：给人看的展示串，以及旧数据（没有 Id 字段）的唯一可读键。
    ///
    /// 为什么不删掉名字只留 Id：Id 落地之前存下的画面数据里只有名字。删了名字，旧工程的绑定就只能靠
    /// 用户重新配一遍——迁移成本必须降到"打开即自愈"（见 <see cref="Matches"/> 与
    /// <see cref="ScadaElement.RefreshVariableReferences"/> 的回填逻辑）。
    ///
    /// 本类刻意不存"显示地址串"（流程连线里的 DisplayAddress）：画面绑定只有一个数据源，
    /// 显示出来就是变量名本身，多一个字段就多一处改名后忘记同步的地方。
    /// </summary>
    public class ScadaBinding : BindableBase
    {
        private string _targetProperty = string.Empty;
        private Guid _variableId;
        private string? _variableName;
        private bool _isEnabled = true;
        private string? _displayFormat;

        /// <summary>
        /// 被绑定的图元属性名（如 "Value" / "FillColor" / "Angle"）。
        ///
        /// 领域层不认识这些名字，它由 S2 的图元描述符（IElementDescriptor）声明：
        /// 描述符既是属性面板的行来源，也是运行态往哪里写值的路标。
        /// </summary>
        public string TargetProperty
        {
            get => _targetProperty;
            set => SetProperty(ref _targetProperty, value ?? string.Empty);
        }

        /// <summary>
        /// 变量稳定身份（权威寻址键）。<c>Guid.Empty</c> 表示"旧数据，只能按名字找"。
        /// </summary>
        public Guid VariableId
        {
            get => _variableId;
            set => SetProperty(ref _variableId, value);
        }

        /// <summary>
        /// 变量名（展示串 + 旧数据兜底键）。
        /// 机器寻址一律用 <see cref="VariableId"/>，不要拿本属性当键。
        /// </summary>
        public string? VariableName
        {
            get => _variableName;
            set => SetProperty(ref _variableName, value);
        }

        /// <summary>
        /// 是否启用。停用（false）时运行态跳过本绑定，但配置保留——
        /// 联调时"临时摘掉一条绑定看现象"比"删掉再重新配一遍"常用得多。
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// 显示格式化串（如数值 "F2"、时间 "yyyy-MM-dd HH:mm:ss"）。
        /// 只对"文本展示型"图元有意义，由 S2/S4 决定怎么用；领域层不解释它。
        /// </summary>
        public string? DisplayFormat
        {
            get => _displayFormat;
            set => SetProperty(ref _displayFormat, value);
        }

        /// <summary>
        /// 是否是"只能按名字找"的旧数据绑定。
        /// 这种情况才需要改名级联去修名字（见 IVariableRegistry.VariableRenamed 的消费方）。
        /// </summary>
        public bool IsLegacyByName => VariableId == Guid.Empty && !string.IsNullOrWhiteSpace(VariableName);

        /// <summary>
        /// 绑定/自愈回填：写入稳定身份与最新名字。
        ///
        /// 两种调用场景共用本方法：
        /// ① 用户在设计器里选了一个变量；
        /// ② 加载旧方案后按名解析成功，把 Id 补回来（"用一次才落盘"的一次性迁移）。
        /// </summary>
        public void Bind(Guid variableId, string? variableName)
        {
            VariableId = variableId;
            VariableName = variableName;
        }

        /// <summary>
        /// 命中判定：判断"被改名的变量"是不是本绑定引用的那个。
        ///
        /// - 已有 Id → 只认 Id（此时名字对不上也算命中：名字可能已过期，而 Id 永不变）；
        /// - 没有 Id（旧数据）→ 按名字比，大小写不敏感（与注册表索引的 OrdinalIgnoreCase 同一口径）。
        /// </summary>
        /// <param name="variableId">改名变量的稳定身份</param>
        /// <param name="oldName">改名前的旧名</param>
        public bool Matches(Guid variableId, string? oldName)
        {
            if (VariableId != Guid.Empty)
                return variableId != Guid.Empty && VariableId == variableId;

            return !string.IsNullOrEmpty(oldName)
                && string.Equals(VariableName, oldName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
