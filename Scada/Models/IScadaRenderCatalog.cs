using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 图元目录的只读快照：把"有哪些图元类型、默认尺寸、可配属性、事件名"
    /// 从 WPF 侧（<c>VM.Scada.Controls</c> 的 <c>ElementRegistry</c>）抽成
    /// 无 WPF 的 DTO，供 Web 运行态模块使用。
    ///
    /// <b>为什么需要它</b>
    /// ---------
    /// <c>ElementDescriptor</c> 的 <c>TargetProperty</c> 是 WPF <c>DependencyProperty</c>，
    /// 直接引用会把 WPF 拖进 Web 渲染器；而 Web 侧只需要"类型键、默认尺寸、
    /// 可绑定属性键、事件名、是否进工具箱"。本接口是宿主实现的单向投影，
    /// Web 不反向依赖 <c>VM.Scada.Controls</c>。
    ///
    /// <b>构建时机</b>：宿主在 UI 线程构建一次；必须晚于
    /// <c>ScadaChartElements.Register()</c>，否则会漏掉趋势图条目。
    /// Web 侧只读，不缓存跨运行。
    ///
    /// <b>M1a 起补上属性默认值</b>：渲染器要能把"没写进 .vms 的属性"按描述符默认值画出来
    /// （模型层本来就是这个语义：属性袋缺键时现取描述符默认值，见
    /// <c>ElementValueAccess.Read</c>），所以每个图元除了"有哪些属性键"还要给出
    /// <see cref="ScadaElementDescriptorInfo.Properties"/>（键 / 显示名 / 默认值 / 编辑器种类 / 候选 / 能否绑变量）。
    /// <see cref="ScadaElementDescriptorInfo.PropertyKeys"/> 保留：它是"键清单"的轻量视图，
    /// 现有调用方（覆盖度断言、诊断）只需要键、不需要整份元数据。
    /// </summary>
    public interface IScadaRenderCatalog
    {
        /// <summary>全部已注册图元（含 <c>ShowInToolbox=false</c> 的兼容别名）</summary>
        IReadOnlyList<ScadaElementDescriptorInfo> All { get; }
    }

    /// <summary>
    /// 单个图元类型的只读描述。字段是 Web 渲染器与覆盖度断言的最小集；
    /// 不要在这里塞 WPF 类型（Brush/Geometry/DependencyProperty）。
    /// </summary>
    public sealed class ScadaElementDescriptorInfo
    {
        /// <summary>落盘长期标识（如 <c>Hmi.Tank</c>）；发布后不许改</summary>
        public string TypeKey { get; init; } = string.Empty;

        /// <summary>工具箱/诊断显示名</summary>
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>工具箱分组</summary>
        public string Category { get; init; } = string.Empty;

        /// <summary>默认宽（新建图元时用）</summary>
        public double DefaultWidth { get; init; }

        /// <summary>默认高（新建图元时用）</summary>
        public double DefaultHeight { get; init; }

        /// <summary>是否出现在工具箱（兼容别名可为 false）</summary>
        public bool ShowInToolbox { get; init; }

        /// <summary>可在属性面板里配的属性键（含几何键 <c>$X</c> 等）</summary>
        public IReadOnlyList<string> PropertyKeys { get; init; } = new List<string>();

        /// <summary>
        /// 属性元数据（与 <see cref="PropertyKeys"/> 同序、同一批键，只多了默认值与编辑器种类）。
        ///
        /// 为什么两份都留着：键清单是"这图元有哪些属性"的最小回答，Web 渲染器与覆盖度断言
        /// 大多数时候只要它；而"没写进 .vms 的属性该画成什么"必须靠 <see cref="ScadaElementPropertyInfo.DefaultValue"/>，
        /// 那是本属性独有的信息。两份由同一个投影生成，不会各说各话。
        /// </summary>
        public IReadOnlyList<ScadaElementPropertyInfo> Properties { get; init; } = new List<ScadaElementPropertyInfo>();

        /// <summary>可配的事件名（<c>ScadaEventType</c> 的成员名）</summary>
        public IReadOnlyList<string> EventNames { get; init; } = new List<string>();
    }

    /// <summary>
    /// 单个图元属性的只读描述（M1a：Web 渲染器补默认值用）。
    ///
    /// 与 <c>ElementPropertyDescriptor</c> 的分工：那边带着 WPF 的 <c>DependencyProperty</c>
    /// 与分组/边界等"属性面板才要"的信息；本 DTO 只留"画出来需要什么"——
    /// 键、显示名、默认值、值形态、候选、能否绑变量。
    /// </summary>
    public sealed class ScadaElementPropertyInfo
    {
        /// <summary>属性袋键；以 <c>$</c> 开头的是保留几何键（<c>$X</c>/<c>$Y</c>/<c>$Width</c>/<c>$Height</c>/<c>$Rotation</c>/<c>$Name</c>）</summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>给人看的名字（Web 属性面板的标签）</summary>
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>
        /// 描述符声明的默认值的<b>字符串形式</b>（<c>"#FFFFFFFF"</c>、<c>"50"</c>、<c>"True"</c>、<c>"Horizontal"</c>…）。
        ///
        /// 渲染器拿它当"模型里没写这个键时"的取值，与 WPF 侧 <c>ElementValueAccess.Read</c> 的口径一致。
        /// 空串表示描述符没给默认值（不是"默认值就是空串"这个断言——几何键的默认值由模型字段给，
        /// 不在属性袋里）。
        /// </summary>
        public string DefaultValue { get; init; } = string.Empty;

        /// <summary>
        /// 值形态：<c>ElementPropertyKind</c> 的成员名（<c>Text</c> / <c>MultilineText</c> / <c>Number</c> /
        /// <c>Bool</c> / <c>Color</c> / <c>Choice</c> / <c>Image</c> / <c>AxisList</c> / <c>ReadOnly</c> / <c>ColumnList</c>）。
        ///
        /// 存名字而不是枚举：<c>ElementPropertyKind</c> 住在 <c>VM.Scada.Controls</c>（带 WPF），
        /// Web 侧只引用 <c>VM.Scada</c>，引用那个枚举等于把 WPF 拖进渲染器。
        /// 名字是长期标识（与 <c>TypeKey</c>、<c>ScadaEventType</c> 同一口径），发布后不许改。
        /// 几何键的识别看 <see cref="Key"/> 的 <c>$</c> 前缀，不靠 Kind（几何键是 Number/Text）。
        /// </summary>
        public string Kind { get; init; } = string.Empty;

        /// <summary>下拉候选（仅 <c>Choice</c> 型有值；值就是落盘用的字符串）</summary>
        public IReadOnlyList<string> Choices { get; init; } = new List<string>();

        /// <summary>能否绑定工程变量（Web 侧据此决定显不显示「ƒx」入口）</summary>
        public bool IsBindable { get; init; }
    }
}
