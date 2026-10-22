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
        /// <summary>
        /// 运动卡**名称**（来自当前方案的运动卡列表；值 = 卡名，不是地址）。
        ///
        /// 【为什么是卡名而不是地址】地址（IP/槽位）是"机器怎么连上这块板"的物理事实，
        /// 它会随现场布线、换卡、改网段而变；流程里存着它，等于把物理接线写进了工艺。
        /// 卡名是用户给的身份，与接线无关 —— 换卡改地址，流程一个字都不用动。
        /// </summary>
        MotionCardName,

        /// <summary>运动卡上的逻辑轴名（来自当前方案里"已启用"的轴映射；轴名全局唯一）</summary>
        MotionAxisName,

        /// <summary>
        /// 通讯变量名（可读；来自当前方案的网络变量）。
        /// 值 = 变量名本体（流程与 .vms 存的就是名字），显示文本可带类型与连接名。
        /// </summary>
        CommunicationVariableName,

        /// <summary>
        /// 通讯变量名（可写；在 <see cref="CommunicationVariableName"/> 基础上排除
        /// Modbus 离散输入 / S7 输入映像 I 等只读区）。
        /// 单独一个类别是为了让「立刻写」的下拉里不出现"选了必然失败"的选项。
        /// </summary>
        CommunicationWritableVariableName,

        /// <summary>
        /// 布尔二选一（True / False）。用于"是否等待""是否启用"这类封闭开关：
        /// 渲染成只能选的下拉，不提供链接/清除——布尔值不该由上游运行时决定，
        /// 手输 true/false 的错误率也远高于选一次。
        /// </summary>
        BooleanChoice,

        /// <summary>
        /// 「调用流程」的目标流程名：只列当前方案里**勾了「子程序」**的流程（值 = 流程名本体）。
        ///
        /// 【为什么必须是宿主给】插件只引用 Core.Interfaces，物理上看不到方案里的流程表；
        /// 而"哪条流程允许被当子程序调用"由 <c>FlowModel.InvokeType</c> 的 Subroutine 位决定，
        /// 那是宿主的领域。让用户手打名字的代价是：拼错/漏勾都只在运行期以一句
        /// "当前方案中没有名为「X」的流程"暴露，配错与拼错在日志里长得一模一样。
        ///
        /// 【为什么值仍是流程名】FlowInvoker 按名 Ordinal 查找（与 HTTP 路由、运行状态镜像同口径），
        /// 改成 Id 会与既有 .vms、日志、HTTP 路径全部脱节；显示文本只加状态后缀，不参与寻址。
        /// </summary>
        SubroutineFlowName,
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
