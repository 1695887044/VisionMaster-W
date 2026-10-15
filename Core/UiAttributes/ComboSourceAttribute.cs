using System;

namespace UI.Attributes
{
    /// <summary>
    /// 标在 string 属性上，表示它应当渲染成**下拉框**，候选来自**同一个绑定对象**上的
    /// <see cref="SourceProperty"/> 属性（`IEnumerable&lt;string&gt;`，或元素能 ToString 的集合）。
    ///
    /// 【为什么需要它（与邻居的分工）】
    ///   · <c>EnumGenerator</c>：候选**编译期**就定死（枚举成员），显示名取 [Description]；
    ///   · <c>OptionSourceGenerator</c>：候选来自**宿主全局注册的来源**（轴名/卡名），
    ///     由 <c>StepConfigOptionKind</c> 表达"去哪个来源取"——它是**静态 kind**，
    ///     表达不了"候选挂在当前这个对象上、随这个对象的其它字段而变"；
    ///   · 本特性：候选就是绑定对象自己的一个属性。典型场景是**弹窗草稿**（DTO）——
    ///     草稿在构造时由调用方把候选列表（连接名 / 扫描组 / 存储区…）注入进来，
    ///     这些候选既不来自宿主全局注册表，也不是编译期常量，只能挂在草稿自己身上。
    ///
    /// 【为什么不用 SelectedValuePath】候选是纯字符串，值就是显示名本身，
    /// 不需要"显示名 ↔ 值"两套（那是枚举上屏的诉求，见 StepConfigOption）。
    ///
    /// 【与 OptionSourceGenerator 的互斥】两者的 CanProcess 各自要求自己的特性，
    /// 一个属性同时贴两个是写法错误（候选来源不唯一），但不会静默串位：
    /// Priority 高者胜出，实际以本生成器（105）为准。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ComboSourceAttribute : Attribute
    {
        public ComboSourceAttribute(string sourceProperty) => SourceProperty = sourceProperty;

        /// <summary>候选来源属性名（同一个绑定对象上的实例属性）</summary>
        public string SourceProperty { get; }

        /// <summary>
        /// 是否允许手填。默认 false（候选即全集）。
        /// 置 true 时按 OptionSourceGenerator 的口径只绑 <c>Text</c>——
        /// 只读下拉在"候选为空"时用户连字都打不进去，那是更不直观。
        /// </summary>
        public bool IsEditable { get; set; }
    }
}
