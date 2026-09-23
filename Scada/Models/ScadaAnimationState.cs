namespace VisionMaster.Scada
{
    /// <summary>
    /// 「外观变化」动画里的一档：<b>值/值域 → 一套外观</b>。
    ///
    /// 为什么是"一条动画内嵌多行档位"而不是"一档一条动画"（手册 7.5.1.1 的原义）：
    /// 现场最常见的需求是"0 灰 / 1 绿 / 2 黄 / 3 红"这种四档色标，四档共用同一个驱动变量、
    /// 同一套"值不命中就显示默认外观"的兜底。拆成四条动画，用户就得把同一个变量选四遍，
    /// 而且"值 1.5 到底算哪一档"这种边界会因为四条动画各算一次而出现四份口径。
    /// 合成一条，档位的匹配次序（自上而下、先命中先用）就只有一个解释。
    ///
    /// <b>为什么是两个端点而不是一个值</b>：手册把"整型数"又分成「值」与「范围」两种写法
    /// （7.5.1.1 的"类型"一栏），二进制按位与位模式则天然是"一个点值"。用 <see cref="ValueLow"/> /
    /// <see cref="ValueHigh"/> 两个端点统一表达：两个端点填一样就是"值"，填不一样就是"范围"，
    /// 于是三种模式共用同一份匹配代码，不必在模型上再加一个"模式"字段去跟匹配逻辑对表。
    ///
    /// 端点存<b>字符串</b>而不是 double：与属性袋同一口径——用户在输入框里打了一半（"1."）
    /// 不该被当场纠正成 1，空串也该能表示"这档还没配"。解析发生在求值那一刻，失败只影响这一档。
    /// </summary>
    public class ScadaAnimationState : ScadaModelBase
    {
        private string _valueLow = "0";
        private string _valueHigh = "0";
        private string? _foreground;
        private string? _fill;
        private bool _isFlashing;

        /// <summary>
        /// 打开一次可撤销的编辑（D3 统一写入口），用法与 <see cref="ScadaElement.BeginEdit"/> 一致。
        /// 属性面板上"加一档/删一档/改档位内容"都走这个作用域。
        /// </summary>
        public IScadaChangeScope BeginEdit(string label) => ScadaChangeScope.Begin(label);

        /// <summary>值域下端点（与 <see cref="ValueHigh"/> 相同 = 单点值；两者相同也是"位"模式）</summary>
        public string ValueLow
        {
            get => _valueLow;
            set => SetProperty(ref _valueLow, value ?? string.Empty);
        }

        /// <summary>值域上端点（闭区间；命中判定为 <c>低 ≤ 值 ≤ 高</c>）</summary>
        public string ValueHigh
        {
            get => _valueHigh;
            set => SetProperty(ref _valueHigh, value ?? string.Empty);
        }

        /// <summary>本档的前景色（落到控件的 <c>Foreground</c>；空 = 本档不改前景色）</summary>
        public string? Foreground
        {
            get => _foreground;
            set
            {
                if (SetProperty(ref _foreground, value))
                    RaisePropertyChanged(nameof(Detail));
            }
        }

        /// <summary>本档的背景色（落到控件的 <c>Fill</c>；空 = 本档不改背景色）</summary>
        public string? Fill
        {
            get => _fill;
            set
            {
                if (SetProperty(ref _fill, value))
                    RaisePropertyChanged(nameof(Detail));
            }
        }

        /// <summary>本档是否闪烁（运行态按统一节拍在"正常/变淡"之间切换）</summary>
        public bool IsFlashing
        {
            get => _isFlashing;
            set
            {
                if (SetProperty(ref _isFlashing, value))
                    RaisePropertyChanged(nameof(Detail));
            }
        }

        /// <summary>值域的展示文本（面板标题与诊断文案共用一处措辞）</summary>
        public string RangeText => string.Equals(ValueLow, ValueHigh, System.StringComparison.Ordinal)
            ? ValueLow
            : $"{ValueLow} ~ {ValueHigh}";

        /// <summary>这一档"配了点什么"的一句话摘要（面板上不展开也能看懂）</summary>
        public string Detail
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();

                if (!string.IsNullOrWhiteSpace(_foreground))
                    parts.Add($"前景 {_foreground}");

                if (!string.IsNullOrWhiteSpace(_fill))
                    parts.Add($"背景 {_fill}");

                if (_isFlashing)
                    parts.Add("闪烁");

                return parts.Count == 0 ? "(未配外观)" : string.Join(" / ", parts);
            }
        }
    }
}
