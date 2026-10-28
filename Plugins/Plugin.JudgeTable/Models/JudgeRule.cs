using Core.Interfaces;
using Newtonsoft.Json;
using System;
using System.Globalization;

namespace Plugin.JudgeTable.Models
{
    /// <summary>
    /// 一条判定规则（随方案持久化）。
    ///
    /// 【存盘字段】RuleId / Name / Enabled / GenerateResult / Mode / Expected / Tolerance /
    /// 文本选项（IgnoreCase / IgnoreWhitespace / MaxEditDistance / Confusables）/ SetSemantics。
    /// 【运行期字段】全部标 <see cref="JsonIgnoreAttribute"/>，不落盘：
    /// LastActual / LastOk / LastReason / LastFormula（上一次运行的回显）、
    /// IsExpanded（行展开态）、InputPortName / ResultPortName（按当前表名与规则名算出的端口名）、
    /// SourceText（数据源显示串）。
    ///
    /// 【为什么不用 [RuntimeState]】那个特性在 <c>VisionMaster.Models</c>（VM.Core）里，
    /// 插件只引用 Core.Interfaces，物理上拿不到。而它要解决的"派生属性别把 FlowModel.Version 推高"
    /// 在这里不成立：本类是插件自己的配置模型，不是 StepModel，其 PropertyChanged 不会进
    /// FlowModel.OnStepPropertyChanged。运行期字段因此只需"不落盘 + 不参与端口重建"两条纪律。
    /// </summary>
    public class JudgeRule : ObservableObject
    {
        private string _ruleId = Guid.NewGuid().ToString("N");

        /// <summary>稳定身份（改名不断链的锚点；界面不显示，只用于日志与断言）</summary>
        public string RuleId
        {
            get => _ruleId;
            set => SetProperty(ref _ruleId, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value);
        }

        private string _name = "规则";

        /// <summary>
        /// 显示名。**同时是端口名的一部分**（输入端口 <c>实际_{Name}</c>、
        /// 输出端口/运行时变量 <c>{表名}.{Name}.结果</c>），改名会重建端口并迁移/清理连线。
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                if (!SetProperty(ref _name, value ?? string.Empty)) return;
                // 派生显示全部跟着变（折叠行短名、结果输出卡片的完整端口名）
                OnPropertyChanged(nameof(ResultPortShort));
                OnPropertyChanged(nameof(ResultPortPlannedShort));
            }
        }

        private bool _enabled = true;

        /// <summary>是否参与总判定。停用行不判定、不生成端口、不写运行时变量（配置保留）。</summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (!SetProperty(ref _enabled, value)) return;
                OnPropertyChanged(nameof(ResultBadge));
                OnPropertyChanged(nameof(HasResultPort));
                OnPropertyChanged(nameof(ResultPortDisplay));
                OnPropertyChanged(nameof(WritePreview));
            }
        }

        private bool _generateResult = true;

        /// <summary>
        /// 是否为本行生成"结果输出"（bool 动态输出端口 + 同名运行时变量）。
        /// 关闭后本行仍参与总判定，只是不对外输出结果。
        /// </summary>
        public bool GenerateResult
        {
            get => _generateResult;
            set
            {
                if (!SetProperty(ref _generateResult, value)) return;
                OnPropertyChanged(nameof(ResultBadge));
                OnPropertyChanged(nameof(HasResultPort));
                OnPropertyChanged(nameof(ResultPortDisplay));
                OnPropertyChanged(nameof(WritePreview));
            }
        }

        private JudgeMode _mode = JudgeMode.Numeric;

        /// <summary>判定模式（数值 / 文本 / 集合 / 正则 / 布尔 / 计数 / 非空）</summary>
        public JudgeMode Mode
        {
            get => _mode;
            set
            {
                if (!SetProperty(ref _mode, value)) return;
                OnPropertyChanged(nameof(ModeText));
                OnPropertyChanged(nameof(IsNumericMode));
                OnPropertyChanged(nameof(IsTextMode));
                OnPropertyChanged(nameof(IsSetMode));
            }
        }

        private string _expected = string.Empty;

        /// <summary>
        /// 期望 / 规格。按模式解析：
        /// 数值 <c>12.00</c> / <c>12.00 ± 0.05</c> / <c>18 ~ 24</c> / <c>≥ 2.5</c>；
        /// 文本 <c>LOT-2026-1027</c>；集合 <c>CODE-A, CODE-B</c>；正则 <c>^LOT-\d{4}$</c>；布尔 <c>true</c>。
        /// </summary>
        public string Expected
        {
            get => _expected;
            set
            {
                if (!SetProperty(ref _expected, value ?? string.Empty)) return;
                OnPropertyChanged(nameof(SpecText));
            }
        }

        private double _tolerance;

        /// <summary>
        /// 数值容差（± 的补充）：Expected 只写中心值时生效；
        /// Expected 里已经写了 <c>± b</c> 时以写明的 b 为准（不叠加）。
        /// </summary>
        public double Tolerance
        {
            get => _tolerance;
            set
            {
                if (!SetProperty(ref _tolerance, value)) return;
                OnPropertyChanged(nameof(SpecText));
            }
        }

        private bool _ignoreCase;

        /// <summary>文本 / 集合：忽略大小写</summary>
        public bool IgnoreCase
        {
            get => _ignoreCase;
            set => SetProperty(ref _ignoreCase, value);
        }

        private bool _ignoreWhitespace;

        /// <summary>文本：忽略所有空白字符（前后与中间）</summary>
        public bool IgnoreWhitespace
        {
            get => _ignoreWhitespace;
            set => SetProperty(ref _ignoreWhitespace, value);
        }

        private int _maxEditDistance;

        /// <summary>文本：允许的编辑距离（0 = 必须完全一致；按 Unicode 码点计算，不会把中文切坏）</summary>
        public int MaxEditDistance
        {
            get => _maxEditDistance;
            set => SetProperty(ref _maxEditDistance, value < 0 ? 0 : value);
        }

        private string _confusables = string.Empty;

        /// <summary>
        /// 文本：易混字符组（如 <c>0O,1I,5S</c>）。比较前把每组的字符都归一成组内第一个字符，
        /// 于是 <c>1O27</c> 与 <c>1027</c> 视为相同。空 = 不启用。
        /// </summary>
        public string Confusables
        {
            get => _confusables;
            set => SetProperty(ref _confusables, value ?? string.Empty);
        }

        private SetSemantics _setSemantics = SetSemantics.AllExpected;

        /// <summary>集合：全部期望都在 / 完全一致 / 不许多余</summary>
        public SetSemantics SetSemantics
        {
            get => _setSemantics;
            set => SetProperty(ref _setSemantics, value);
        }

        // ==================================================================
        //  运行期状态（不落盘）
        // ==================================================================

        private bool _isExpanded;

        /// <summary>行展开态（纯界面状态，不落盘、不影响判定）</summary>
        [JsonIgnore]
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        private string _inputPortName = string.Empty;

        /// <summary>本规则对应的动态输入端口名（<c>实际_{Name}</c>，由插件每次重建时写回）</summary>
        [JsonIgnore]
        public string InputPortName
        {
            get => _inputPortName;
            set => SetProperty(ref _inputPortName, value ?? string.Empty);
        }

        private string _resultPortName = string.Empty;

        /// <summary>
        /// 本规则对应的结果端口全名（<c>{表名}.{规则名}.结果</c>，由插件每次重建时写回）。
        /// 停用或关闭结果输出时该名字仍保留（界面上作为"打开后会叫什么"的预告），
        /// 但端口**不会**真的建出来 —— 判据看 <see cref="HasResultPort"/>。
        /// </summary>
        [JsonIgnore]
        public string ResultPortName
        {
            get => _resultPortName;
            set
            {
                if (!SetProperty(ref _resultPortName, value ?? string.Empty)) return;
                OnPropertyChanged(nameof(ResultPortShort));
                OnPropertyChanged(nameof(ResultPortPlannedShort));
                OnPropertyChanged(nameof(ResultPortDisplay));
                OnPropertyChanged(nameof(WritePreview));
            }
        }

        private string _lastActual = string.Empty;

        /// <summary>上一次运行的实际值（配置视图回显；没跑过为空）</summary>
        [JsonIgnore]
        public string LastActual
        {
            get => _lastActual;
            set => SetProperty(ref _lastActual, value ?? string.Empty);
        }

        private bool? _lastOk;

        /// <summary>上一次运行的判定结果（null = 还没跑过）</summary>
        [JsonIgnore]
        public bool? LastOk
        {
            get => _lastOk;
            set
            {
                if (!SetProperty(ref _lastOk, value)) return;
                OnPropertyChanged(nameof(ResultBadge));
                OnPropertyChanged(nameof(WritePreview));
            }
        }

        private string _lastReason = string.Empty;

        /// <summary>上一次运行的原因（失败时写清为什么）</summary>
        [JsonIgnore]
        public string LastReason
        {
            get => _lastReason;
            set => SetProperty(ref _lastReason, value ?? string.Empty);
        }

        private string _lastFormula = string.Empty;

        /// <summary>上一次运行的判定算式（如 <c>|12.07 − 12.00| = 0.07 &gt; 0.05 → NG</c>）</summary>
        [JsonIgnore]
        public string LastFormula
        {
            get => _lastFormula;
            set => SetProperty(ref _lastFormula, value ?? string.Empty);
        }

        private string _sourceText = "未连线";

        /// <summary>数据源显示串（上游连线的显示地址；未连线时为"未连线"）</summary>
        [JsonIgnore]
        public string SourceText
        {
            get => _sourceText;
            set => SetProperty(ref _sourceText, string.IsNullOrWhiteSpace(value) ? "未连线" : value);
        }

        private string? _linkAddress;

        /// <summary>
        /// 上游连线地址；**null = 未连线**（不许用"未连线"这类非空文案顶替）。
        ///
        /// 绑定给 <c>LinkableValueEditor.LinkAddress</c>：控件按"非空即已链接"决定渲染形态
        /// （已链接 → 隐藏固定值框、显示只读地址；未链接 → 显示可编辑的固定值框 + ✕ 隐藏）。
        /// 若把显示串"未连线"直接绑上去，未连线会被当成已链接：地址框显示"未连线"、
        /// 输入框被折叠 —— 用户既填不了常量也看不到该填什么。
        ///
        /// <see cref="SourceText"/>（折叠行与只读展示用）由本属性派生，
        /// 两个属性只有一个写入点（本 setter），避免两处各写一份导致显示与连线脱节。
        /// </summary>
        [JsonIgnore]
        public string? LinkAddress
        {
            get => _linkAddress;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
                if (!SetProperty(ref _linkAddress, normalized)) return;
                SourceText = normalized ?? "未连线";
            }
        }

        // ==================================================================
        //  派生显示（纯展示，不落盘）
        // ==================================================================

        /// <summary>模式中文名（读 <see cref="JudgeMode"/> 成员上的 [Display(Name)]）</summary>
        [JsonIgnore]
        public string ModeText => StepConfigOption.ForEnum(typeof(JudgeMode), Mode.ToString()).Display;

        /// <summary>模式分类（展开行按模式显示不同的选项组）</summary>
        [JsonIgnore]
        public bool IsNumericMode => Mode is JudgeMode.Numeric or JudgeMode.Count;

        /// <summary>是否文本模式（文本选项组可见）</summary>
        [JsonIgnore]
        public bool IsTextMode => Mode == JudgeMode.Text;

        /// <summary>是否集合模式（集合语义下拉可见）</summary>
        [JsonIgnore]
        public bool IsSetMode => Mode == JudgeMode.Set;

        /// <summary>
        /// 规格显示：数值模式且 Expected 未写 ± 而容差 &gt; 0 时补上 <c>± 容差</c>，
        /// 其余模式原样显示 Expected。
        /// </summary>
        [JsonIgnore]
        public string SpecText
        {
            get
            {
                var expected = Expected ?? string.Empty;
                if (Mode == JudgeMode.Numeric
                    && Tolerance > 0
                    && expected.IndexOf('±') < 0
                    && expected.IndexOf('~') < 0
                    && !expected.Contains('≥') && !expected.Contains('≤')
                    && !expected.Contains('>') && !expected.Contains('<'))
                {
                    return $"{expected} ± {Tolerance.ToString("0.####", CultureInfo.InvariantCulture)}";
                }
                return expected;
            }
        }

        /// <summary>本行是否真的会生成结果端口（停用 / 关闭生成都不生成）</summary>
        [JsonIgnore]
        public bool HasResultPort => Enabled && GenerateResult;

        /// <summary>折叠行的结果输出短标签：<c>→ 规则名.结果</c> / 未生成</summary>
        [JsonIgnore]
        public string ResultPortShort
        {
            get
            {
                if (!HasResultPort) return "未生成";
                var name = string.IsNullOrWhiteSpace(Name) ? "规则" : Name;
                return $"→ {name}.结果";
            }
        }

        /// <summary>结果输出关掉时也能看到的"打开后会叫什么"（预告，不带箭头）</summary>
        [JsonIgnore]
        public string ResultPortPlannedShort
        {
            get
            {
                var name = string.IsNullOrWhiteSpace(Name) ? "规则" : Name;
                return $"{name}.结果";
            }
        }

        /// <summary>
        /// 结果输出卡片里的端口名显示：真的会生成时显示全名，否则显示"未生成"占位。
        /// 单独派生是为了"开关一改，文字立刻跟着变"（绑定在 HasResultPort / ResultPortName 上都会漏通知）。
        /// </summary>
        [JsonIgnore]
        public string ResultPortDisplay =>
            HasResultPort && !string.IsNullOrWhiteSpace(ResultPortName) ? ResultPortName : "—（未生成）";

        /// <summary>结果输出卡片底部的写入预览</summary>
        [JsonIgnore]
        public string WritePreview
        {
            get
            {
                if (!HasResultPort) return "未生成（不写入）";
                string value = LastOk == true ? "true" : LastOk == false ? "false" : "true / false";
                return $"{ResultPortName} = {value}";
            }
        }

        /// <summary>结果徽标文案：OK / NG / 跳过（停用）/ —（还没跑过）</summary>
        [JsonIgnore]
        public string ResultBadge
        {
            get
            {
                if (!Enabled) return "跳过";
                if (LastOk == null) return "—";
                return LastOk.Value ? "OK" : "NG";
            }
        }

        /// <summary>Actual 列显示：跑过显示实际值，没跑过显示占位文案</summary>
        [JsonIgnore]
        public string ActualDisplay => string.IsNullOrEmpty(LastActual) ? "—（运行时）" : LastActual;

        /// <summary>原因列显示：跑过显示原因，没跑过显示占位文案</summary>
        [JsonIgnore]
        public string ReasonDisplay => string.IsNullOrEmpty(LastReason) ? "—（运行时）" : LastReason;

        /// <summary>判定算式显示：跑过显示算式，没跑过给出按模式生成的预览提示</summary>
        [JsonIgnore]
        public string FormulaDisplay
        {
            get
            {
                if (!string.IsNullOrEmpty(LastFormula)) return LastFormula;
                return Enabled ? "运行后在这里显示判定算式" : "未启用，不参与总判定";
            }
        }

        /// <summary>清空上一次运行的回显（打开配置窗口时调用：配置态没有"上次结果"）</summary>
        public void ClearRuntimeState()
        {
            LastActual = string.Empty;
            LastOk = null;
            LastReason = string.Empty;
            LastFormula = string.Empty;
        }
    }
}
