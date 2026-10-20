using System;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 图元属性声明（配合 <c>Core.ScadaGenerators</c> 源生成器）：一处声明产出三样东西。
    ///
    /// <code>
    /// [ScadaProperty(Key = "OffColor", DisplayName = "熄灭色", Kind = ElementPropertyKind.Color,
    ///                Group = ScadaPropertyGroups.State, DefaultValue = "#FF3A3A3A", IsBindable = true)]
    /// public partial Brush? OffColor { get; set; }
    ///
    /// partial void OnOffColorPropertyChanged() =&gt; UpdateLampBrush();   // 可选副作用
    /// </code>
    ///
    /// 生成物：① 依赖属性 <c>OffColorProperty</c>；② <c>OffColor</c> 的 CLR 包装；③ 属性描述符条目。
    /// 描述符条目是"属性面板 / 绑定引擎 / 渲染落地"三处共用的一份元数据——
    /// 原先它要在 <see cref="BuiltInElements"/> 里手写一遍，与控件类的依赖属性声明两处对齐；
    /// 两处写岔了<b>不报任何错</b>（Key 不一致 = 属性面板改了不生效；TargetProperty 漏写 = 值只进属性袋不落控件），
    /// 所以交给生成器，让 Key / 默认值 / 目标属性只有一处事实来源。
    ///
    /// <b>什么该用本特性、什么不该</b>
    /// ---------
    /// · 本特性只用于<b>图元私有</b>属性（控件类自己声明的新依赖属性）。
    /// · 想让某个图元"暴露出基类已有的依赖属性"（填色、字号、文字这类通用外观），
    ///   请用 <c>AppearanceProperties</c> / <c>TextProperties</c> 那组打包助手——
    ///   在这里再生成一个同名属性会<b>遮蔽</b>基类依赖属性，模板里那句 <c>TemplateBinding Fill</c>
    ///   会绑到新属性上，而基类的 Fill 永远不动，表现为"属性面板改了没反应"。
    /// · 推导出来的只读属性（如指示灯的当前灯色）保持手写：它是显示结果，不是可配属性。
    ///
    /// 不满足约定（类没加 <c>partial</c>、缺 Key/DisplayName/Kind、Choice 没给 Choices 等）
    /// 一律报编译 Error（SPG0001~SPG0005），不静默跳过。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class ScadaPropertyAttribute : Attribute
    {
        /// <summary>
        /// 属性袋里的键。以 "$" 开头的是保留几何键（由 <c>GeometryProperties</c> 统一声明），不能用在这里。
        ///
        /// <b>必填，且是写进 .vms 的长期标识</b>：可以"属性改名、Key 不改"（旧文件照样读得出），
        /// 但不许省——省了就等于让落盘键默默跟着属性名走，改属性名会静默丢掉用户已配的值。
        /// </summary>
        public string? Key { get; set; }

        /// <summary>给人看的名字（属性面板标签、描述符 DisplayName）</summary>
        public string? DisplayName { get; set; }

        /// <summary>编辑器类型（属性面板据此挑模板）</summary>
        public ElementPropertyKind Kind { get; set; } = ElementPropertyKind.Text;

        /// <summary>属性面板里的分组标题</summary>
        public string Group { get; set; } = ScadaPropertyGroups.Appearance;

        /// <summary>
        /// 默认值（字符串形态）。同时喂给两处：依赖属性的元数据默认值、以及描述符的 DefaultValue。
        ///
        /// 两处必须一致才有"改默认值时旧 .vms 跟着变"这条既有语义（旧文件里没写的属性会现取默认值），
        /// 而生成器保证它们同源，正好把这层一致性从"靠人记着"变成"不可能不一致"。
        /// 写法必须能被目标类型的 WPF 类型转换器解析（<c>#FF7A7A7A</c> / <c>1</c> / <c>Circle</c>），
        /// 解析不了的默认值会在注册期被 <c>ElementDescriptor.Validate</c> 报出来。
        /// </summary>
        public string DefaultValue { get; set; } = string.Empty;

        /// <summary>悬停说明（可为空）</summary>
        public string? Description { get; set; }

        /// <summary>
        /// 能否绑定工程变量（决定属性面板这一行显不显示「ƒx」）。
        ///
        /// <b>这不是随手填的默认值，是一条产品口径</b>：设计期语言不可绑、运行期语言可绑——
        /// 「布局」与「外观」两组一律不声明可绑（见 <see cref="GeometryProperties"/> 的类注释）。
        /// 口径由 <see cref="ScadaPropertyGroups.NonBindable"/> 在注册期兜底校验。
        /// </summary>
        public bool IsBindable { get; set; }

        /// <summary>属性值变化时是否需要重绘（映射到 <c>FrameworkPropertyMetadataOptions.AffectsRender</c>）。默认开。</summary>
        public bool AffectsRender { get; set; } = true;

        /// <summary>数值下界（仅 <see cref="ElementPropertyKind.Number"/> 有意义；不给＝不设界）</summary>
        public double Min { get; set; }

        /// <summary>数值上界（仅 <see cref="ElementPropertyKind.Number"/> 有意义；不给＝不设界）</summary>
        public double Max { get; set; }

        /// <summary>下拉候选项（仅 <see cref="ElementPropertyKind.Choice"/> 有意义）</summary>
        public string[]? Choices { get; set; }
    }
}
