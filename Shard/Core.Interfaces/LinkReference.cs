using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 连线数据源类型：决定一条 LinkReference 如何被解析，以及 TargetPortName 承载什么含义。
    ///
    /// 为什么需要它：此前该语义靠"哨兵 Guid + DisplayAddress 字符串前缀"隐式推断——
    /// 常量与全局变量的 TargetStepId 都是 Guid.Empty，仅凭显示串有没有 "常量值: " 前缀区分。
    /// 于是改动一句 UI 文案、或用户手输地址串，就会让常量被当成全局变量解析，
    /// 且编译器不报错、静默连错。现改为显式字段，显示串退回纯展示职责。
    /// </summary>
    public enum LinkKind
    {
        /// <summary>未设置。旧工程 JSON 缺该字段时的默认值，由 <see cref="LinkReference.NormalizeKind"/> 按旧规则回填</summary>
        Unset = 0,

        /// <summary>上游步骤的输出端口。TargetStepId = 真实 StepID，TargetPortName = 端口名（可带 [索引]）</summary>
        StepPort = 1,

        /// <summary>全局变量。TargetStepId 不参与判定，TargetPortName = 变量名</summary>
        GlobalVariable = 2,

        /// <summary>运行时本地变量。TargetStepId = LinkProtocol.RuntimeVariableMarkerGuid，TargetPortName = 变量名</summary>
        RuntimeVariable = 3,

        /// <summary>常量。TargetStepId 不参与判定，TargetPortName = 常量值字符串</summary>
        Constant = 4,
    }

    /// <summary>
    /// 连线协议常量：Kind 与存盘字段之间的约定。
    /// 放在协议层而非编译器，因为生产端（变量绑定弹窗）和消费端（FlowCompiler）都要用同一份定义。
    /// </summary>
    public static class LinkProtocol
    {
        /// <summary>
        /// 运行时变量引用的标记 Guid（零概率随机值，避免与真实 StepID 冲突）。
        /// 现在只作为"给旧工程回填 Kind"的判据，新数据一律显式写 Kind。
        /// </summary>
        public static readonly Guid RuntimeVariableMarkerGuid =
            new Guid("D5A2E1B0-1111-4F8C-9B3F-2A6E0B6A9F01");

        /// <summary>
        /// 常量地址的显示前缀。
        /// 仅用于生成显示串和回填旧工程 Kind，绝不可作为编译判据。
        /// </summary>
        public const string ConstantDisplayPrefix = "常量值: ";
    }

    /// <summary>
    /// 连线引用：描述一个输入端口的数据来源。
    /// 寻址以 Kind + Guid 为准，DisplayAddress 只是给人看的显示串。
    /// </summary>
    public class LinkReference
    {
        /// <summary>
        /// 数据源类型（编译语义，参与存盘）
        /// </summary>
        public LinkKind Kind { get; set; }

        /// <summary>
        /// 来源步骤的 ID。仅 Kind == StepPort 时用于寻址；
        /// 其余 Kind 下该字段不参与语义判定（历史遗留的哨兵用法见 InferKind）。
        /// </summary>
        public Guid TargetStepId { get; set; }

        /// <summary>
        /// 来源端口名 / 变量名 / 常量值字符串，具体含义由 Kind 决定
        /// </summary>
        public string TargetPortName { get; set; }

        /// <summary>
        /// 来源变量的稳定身份（仅 Kind == GlobalVariable 时有效；RuntimeVariable 走执行期上下文，暂不适用）。
        ///
        /// 为什么与 TargetPortName 并存而不是替换它：
        /// TargetPortName 存的是变量名，是唯一能读懂旧工程数据的兜底键，不能删；
        /// TargetVariableId 是新写入数据的权威键——变量改名后依然命中。
        /// 解析时 Id 优先、Name 兜底（见 IVariableRegistry.ResolveGlobalLink），
        /// 旧工程数据（无此字段 → Guid.Empty）按名命中后会被自愈回填，下次保存即完成迁移。
        /// </summary>
        public Guid TargetVariableId { get; set; }

        /// <summary>
        /// 显示地址（如 "Blob定位.中心X"、"常量值: 3.14"）。
        /// 纯展示用途：改名级联时重算它，编译器不得读取它做判定。
        /// </summary>
        public string DisplayAddress { get; set; }

        public LinkReference() { }

        /// <summary>
        /// 兼容旧调用：不指定 Kind 时按旧规则推断，保证不产生 Unset 连线
        /// </summary>
        public LinkReference(Guid targetId, string targetPort, string displayAddress)
        {
            TargetStepId = targetId;
            TargetPortName = targetPort;
            DisplayAddress = displayAddress;
            Kind = InferKind(targetId, displayAddress);
        }

        /// <summary>
        /// 显式指定 Kind 的推荐构造
        /// </summary>
        public LinkReference(LinkKind kind, Guid targetId, string targetPort, string displayAddress)
        {
            Kind = kind;
            TargetStepId = targetId;
            TargetPortName = targetPort;
            DisplayAddress = displayAddress;
        }

        /// <summary>
        /// 按旧规则（哨兵 Guid + 显示串前缀）推断 Kind。
        /// 唯一用途是给没有 Kind 字段的旧工程数据回填，新写入的数据不应依赖它。
        /// </summary>
        public static LinkKind InferKind(Guid targetStepId, string displayAddress)
        {
            if (targetStepId == LinkProtocol.RuntimeVariableMarkerGuid)
                return LinkKind.RuntimeVariable;

            if (targetStepId == Guid.Empty)
            {
                return !string.IsNullOrEmpty(displayAddress)
                    && displayAddress.StartsWith(LinkProtocol.ConstantDisplayPrefix, StringComparison.Ordinal)
                        ? LinkKind.Constant
                        : LinkKind.GlobalVariable;
            }

            return LinkKind.StepPort;
        }

        /// <summary>
        /// Kind 缺失（旧工程反序列化）时按旧规则回填，已有值保持不动。
        /// 由编译器在解析连线前调用，使 UI 层读到的 Kind 总是有效。
        /// </summary>
        public LinkKind NormalizeKind()
        {
            if (Kind == LinkKind.Unset)
                Kind = InferKind(TargetStepId, DisplayAddress);

            return Kind;
        }
    }
}
