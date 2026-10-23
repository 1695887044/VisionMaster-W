using System;
using System.Collections.Generic;
using System.Windows;
using VisionMaster.Scada;

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

        /// <summary>
        /// 图像（值为 <see cref="System.Windows.Media.ImageSource"/>）。
        ///
        /// 这类属性的值<b>没法手打</b>——位图不是一段文本，所以属性面板给的是一条只读的绑定行：
        /// 未绑变量时提示"去右侧点 ƒx 选一个图像变量"，绑上后显示变量名。
        /// 它出现在这里而不是复用 Text，是因为面板必须知道"这一行不该长输入框"，
        /// 否则操作员会对着一个永远输不对的文本框发愣。
        ///
        /// 数值追加在末尾：枚举值一旦发布就不许改，只许往后加（.vms 里存的是数值）。
        /// </summary>
        Image,

        /// <summary>
        /// 轴候选清单（轴面板的设计期配置，值是 <see cref="VisionMaster.Scada.ScadaAxisOptionsCodec"/>
        /// 的规范 JSON 串）。
        ///
        /// 它与 <see cref="Image"/> 同一类：值不是手打的，面板给"摘要 + 选择轴…"按钮。
        /// <b>它不是数据绑定</b>——轴候选是设计期配置，运行期由面板自己的下拉框切换；
        /// 所以这类行一律 <c>IsBindable=false</c>，不要给它挂 ƒx。
        /// </summary>
        AxisList,

        /// <summary>
        /// 只读事实（图元类型键）。
        ///
        /// 与 <see cref="Text"/> 的区别是<b>改不了</b>：面板给的是一块只读底色，而不是输入框。
        /// 为什么不做成"只读的 Text 框"：那仍然能聚焦、能选中、能打字，而打进去的字会被静默丢掉
        /// ——"看起来能改"比"看起来不能改"更伤信任（与 <see cref="Image"/> 不给输入框同一条理由）。
        ///
        /// 目前只有「类型」一行用它（<c>ScadaTypeRow</c>），值来自 <c>ScadaElement.TypeKey</c>。
        /// </summary>
        ReadOnly,

        /// <summary>
        /// 列定义（数据表格图元）：值是规范 JSON，面板给"摘要 + 选择列…"。
        ///
        /// 与 <see cref="AxisList"/> 同一形态——都是"一条 JSON 摊成多个视觉元素"：
        /// 让人手写 JSON 不现实，必须配弹窗（设计基线 docs/UI设计基线/scada-datagrid/ §03）。
        /// </summary>
        ColumnList,

        /// <summary>
        /// 方案内的图片文件（值是相对路径，如 <c>Image\pid-layout.png</c>）。
        ///
        /// 与 <see cref="Image"/> 的区别：<see cref="Image"/> 是"由变量提供的运行态图像"，
        /// 这一类是"设计期选进方案的文件"。面板给缩略图 + 选择/清除，不让用户手打路径。
        /// </summary>
        ImageFile,
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
        public string Group { get; init; } = ScadaPropertyGroups.Appearance;

        /// <summary>补充说明（属性面板的悬停提示；可为空）</summary>
        public string? Description { get; init; }

        /// <summary>
        /// 能否绑定工程变量（属性面板据此决定这一行显不显示「ƒx」）。
        ///
        /// <b>这不是随手填的默认值，是一条产品口径</b>：<b>设计期语言不可绑，运行期语言可绑。</b>
        /// 具体地——<b>「布局」与「外观」两组一律不可绑</b>：前者是版面（画在哪儿、多大、
        /// 什么角度），后者是皮肤（填充、边框、配色），都属组态时一次定好的事实，
        /// 交给变量驱动只会让画面自己乱变。可绑的是"运行期语言"：数值、状态、文字这类
        /// 随时间涨落的量。
        ///
        /// 口径由 <see cref="ScadaPropertyGroups.NonBindable"/> 在注册期兜底校验，
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

        /// <summary>
        /// 条件可见：只有属性袋里的 <see cref="VisibleWhenKey"/> 等于 <see cref="VisibleWhenValue"/> 时，
        /// 属性面板才显示这一行。空 = 永远显示。
        ///
        /// 为什么放在描述符而不是面板里按图元类型写 if：同一个属性在不同呈现下是否有效，
        /// 是"这类图元的属性契约"，不是面板的显示偏好；放在这里，断言也能直接核对。
        /// </summary>
        public string? VisibleWhenKey { get; init; }

        /// <summary>条件可见的期望值（与 <see cref="VisibleWhenKey"/> 配套）</summary>
        public string? VisibleWhenValue { get; init; }

        /// <summary>按当前属性袋判断这一行是否该出现（无条件是永远出现）</summary>
        public bool IsVisibleFor(ScadaElement? element)
        {
            if (string.IsNullOrEmpty(VisibleWhenKey) || element is null)
                return true;

            // 必须读“有效值”（属性袋没写就取描述符默认值），不能直接读属性袋：
            // 新建图元的 Presentation 不在袋里，但它的默认值就是 Field；读袋会得到空串，
            // 把 Field 该显示的 Mode/Fill 全藏掉。
            var condition = ElementRegistry.FindProperty(element.TypeKey, VisibleWhenKey);
            string? value = condition is null
                ? element.GetProperty(VisibleWhenKey)
                : ElementValueAccess.Read(element, condition);

            return string.Equals(value, VisibleWhenValue, StringComparison.Ordinal);
        }

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
