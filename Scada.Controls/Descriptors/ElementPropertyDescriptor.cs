using System;
using System.Collections.Generic;
using System.Windows;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 属性编辑器类型（决定 S3 属性面板给你什么控件来改这个值）。
    ///
    /// 注意它<b>不</b>参与"字符串 → 外观"的换算：那件事由
    /// <see cref="ElementPropertyDescriptor.TargetProperty"/> 的依赖属性类型反查
    /// <see cref="System.ComponentModel.TypeConverter"/> 完成（Brush / double / FontWeight
    /// 各有各的转换器，WPF 早就写好了，不必我们重造）。
    /// Kind 只回答"怎么编辑最顺手"这一个问题。
    /// </summary>
    public enum ElementPropertyKind
    {
        /// <summary>单行文本</summary>
        Text,

        /// <summary>多行文本（文本框、说明文字）</summary>
        MultilineText,

        /// <summary>数值（配合 Min/Max/Decimals 使用）</summary>
        Number,

        /// <summary>开关（值为 "True"/"False"）</summary>
        Bool,

        /// <summary>颜色（值为 "#AARRGGBB"，编辑器给取色器）</summary>
        Color,

        /// <summary>枚举下拉（取值必须是 <see cref="Choices"/> 里的一项）</summary>
        Choice,
    }

    /// <summary>
    /// 一个图元属性的元数据：属性袋里这个键叫什么、给人看叫什么、默认值多少、改它等于改控件上哪个依赖属性。
    ///
    /// 这是 S2 的"单一事实来源"：S3 的属性面板、S4 的绑定引擎、图元的渲染，
    /// 全都读同一份声明。新增一个可配属性 = 在描述符里加一条，没有第二处需要同步。
    ///
    /// <see cref="TargetProperty"/> 为 null 时表示"这个键的换算规则没法用现成的类型转换器表达"，
    /// 由控件自己覆写 <see cref="ScadaElementBase.ApplyCustomProperty"/> 处理。
    /// </summary>
    public sealed class ElementPropertyDescriptor : IPropertySpec
    {
        /// <summary>
        /// 属性袋里的键。
        /// 以 "$" 开头的是<b>保留几何键</b>（$X/$Y/$Width/$Height/$Rotation/$Name），
        /// 它们不落在属性袋里，而是直接读写 <see cref="ScadaElement"/> 上的强类型字段——
        /// 这样"拖动矩形"仍是改 double，不是解析字符串；
        /// 而属性面板/绑定引擎又只需遍历一份清单，不必把几何单独写一套分支。
        /// </summary>
        public required string Key { get; init; }

        /// <summary>给人看的名字（属性面板的标签）</summary>
        public required string DisplayName { get; init; }

        /// <summary>编辑器类型（只影响 S3 给你什么编辑控件）</summary>
        public ElementPropertyKind Kind { get; init; } = ElementPropertyKind.Text;

        /// <summary>
        /// 默认值（字符串形态，写法必须能被目标依赖属性的类型转换器解析）。
        /// 默认值<b>不</b>写进属性袋——模型里没写就现取这里的值，
        /// 于是以后改默认值时，旧 .vms 会跟着一起变（详见 ScadaElement.SetProperty 注释）。
        /// </summary>
        public string DefaultValue { get; init; } = string.Empty;

        /// <summary>属性面板里的分组标题</summary>
        public string Group { get; init; } = "外观";

        /// <summary>补充说明（属性面板的悬停提示；可为空）</summary>
        public string? Description { get; init; }

        /// <summary>
        /// 能否绑定工程变量（属性面板据此决定这一行显不显示「ƒx」）。
        ///
        /// <b>这不是随手填的默认值，是一条产品口径</b>：<b>设计期语言不可绑，运行期语言可绑。</b>
        /// 具体地——<b>「位置与尺寸」与「外观」两组一律不可绑</b>：前者是版面（画在哪儿、多大、
        /// 什么角度），后者是皮肤（填充、边框、配色），都属组态时一次定好的事实，
        /// 交给变量驱动只会让画面自己乱变。可绑的是"运行期语言"：数值、状态、文字这类
        /// 随时间涨落的量。
        ///
        /// 口径由 <see cref="ElementDescriptor.NonBindableGroups"/> 在注册期兜底校验，
        /// 新增图元时写错会直接注册失败。逐条理由见 <see cref="GeometryProperties"/> 的类注释。
        /// </summary>
        public bool IsBindable { get; init; }

        /// <summary>数值下界（仅 <see cref="ElementPropertyKind.Number"/> 有意义）</summary>
        public double Min { get; init; } = double.NegativeInfinity;

        /// <summary>数值上界（仅 <see cref="ElementPropertyKind.Number"/> 有意义）</summary>
        public double Max { get; init; } = double.PositiveInfinity;

        /// <summary>下拉候选项（仅 <see cref="ElementPropertyKind.Choice"/> 有意义）</summary>
        public IReadOnlyList<string> Choices { get; init; } = Array.Empty<string>();

        /// <summary>
        /// 换算落点：控件上的哪个依赖属性。
        ///
        /// 存 <see cref="DependencyProperty"/> 对象而不是属性名字符串，是为了让笔误在编译期就暴露，
        /// 同时运行时零反射（用名字查找要过 TypeDescriptor，每次刷新都要查一遍）。
        /// </summary>
        public DependencyProperty? TargetProperty { get; init; }

        /// <summary>是不是保留几何键（即不落属性袋、直接改模型字段的那种）</summary>
        public bool IsGeometry => ElementValueAccess.IsReservedKey(Key);

        /// <summary>
        /// 注册期的合法性自检。返回 null 表示通过，否则是给人看的失败原因
        /// （注册表把这些原因原样返回，断言与日志据此定位，避免"注册了但没生效"的哑失败）。
        /// </summary>
        internal string? Validate()
        {
            if (string.IsNullOrWhiteSpace(Key))
                return "属性键为空";

            if (ElementValueAccess.IsReservedKey(Key))
            {
                if (!ElementValueAccess.IsKnownGeometryKey(Key))
                    return $"保留键 {Key} 不是已知几何键（已知：{string.Join("/", ElementValueAccess.KnownGeometryKeys)}）";
            }
            else if (string.Equals(Key, "Properties", StringComparison.Ordinal))
            {
                return "属性键不能叫 Properties（与模型上的属性袋同名，会让人误读）";
            }

            if (string.IsNullOrWhiteSpace(DisplayName))
                return $"{Key} 缺少显示名";

            if (Kind == ElementPropertyKind.Choice && Choices.Count == 0)
                return $"{Key} 是 Choice 型但没给 Choices";

            if (Kind == ElementPropertyKind.Number)
            {
                if (double.IsNaN(Min) || double.IsNaN(Max) || Min > Max)
                    return $"{Key} 的 Min/Max 非法（Min={Min}, Max={Max}）";
            }
            else if (!double.IsNegativeInfinity(Min) || !double.IsPositiveInfinity(Max))
            {
                return $"{Key} 不是 Number 型，不应设 Min/Max";
            }

            return null;
        }
    }
}
