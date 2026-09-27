using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 步骤参数候选来源的类别。
    ///
    /// 为什么用枚举而不是直接给一串候选字符串：候选项是**运行期才知道**的
    /// （轴名来自当前方案的运动卡轴映射表，用户改映射表它就该跟着变），
    /// 所以特性只能说"去哪个来源取"，不能说"就是这几个"。
    /// </summary>
    public enum StepConfigOptionKind
    {
        /// <summary>运动卡地址（来自当前方案的运动卡列表）</summary>
        MotionCardAddress,

        /// <summary>运动卡上的逻辑轴名（来自当前方案里"已启用"的轴映射）</summary>
        MotionAxisName,
    }

    /// <summary>
    /// 标在步骤参数上，表示它应当渲染成**下拉选择**，候选来自 <see cref="StepConfigOptionKind"/> 指定的来源。
    ///
    /// 【为什么需要它】`[StepConfig]` 的 string 参数默认渲染成文本框，
    /// 于是"轴"这种东西要用户手打逻辑名 —— 他得先去别处翻配置、记住名字、再回来敲，
    /// 敲错一个字符要到运行时才报"没有名为 X 的轴"（用户反馈原话："不够直观，难以选择指定的轴"）。
    ///
    /// 与枚举参数的区别：枚举的候选**编译期**就定死了（如运动方式"绝对/相对"），
    /// 那类用 <c>EnumGenerator</c> 处理；轴名/卡地址的候选随方案变化，必须运行期取，
    /// 由 <c>OptionSourceGenerator</c> + 宿主注册的来源处理。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class StepConfigOptionsAttribute : Attribute
    {
        public StepConfigOptionsAttribute(StepConfigOptionKind kind) => Kind = kind;

        /// <summary>候选来源类别</summary>
        public StepConfigOptionKind Kind { get; }
    }
}
