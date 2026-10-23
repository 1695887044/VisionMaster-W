using System.Collections.Generic;

namespace VisionMaster.Scada.Snapshots
{
    // ============================================================================
    //  M1a：页面快照契约（供 Web 渲染器消费的只读 DTO）
    //
    //  【为什么单独一个命名空间 VisionMaster.Scada.Snapshots】
    //  本组 DTO 里的 ScadaElementSnapshot / ScadaBindingSnapshot / ScadaAnimationSnapshot /
    //  ScadaAnimationStateSnapshot 与剪贴板/模板用的同名类型（Scada\Models\ScadaElementSnapshot.cs）
    //  **重名但不同形**：那一组服务"跨画面复制粘贴"，用槽位号（GroupSlot / ParentSlot）代替 Guid、
    //  VariableId 用 Guid；本组服务"渲染器只读消费"，一律用 Guid "D" 字符串并带图层可见性。
    //  两者不能合并（合并就得让剪贴板承担渲染语义，或让渲染器解析槽位号），也不许把既有那组改名
    //  （会波及 ScadaClipboard / ScadaTemplateStore / 既有断言）。所以按"一族一个命名空间"分开。
    //
    //  【不可变口径】
    //  · 所有 DTO 是 sealed + init-only，构造后不再变化；
    //  · 集合一律包 ReadOnlyCollection / ReadOnlyDictionary（强转去改会抛 NotSupportedException，
    //    而不是悄悄改到快照上——快照一旦可变，"同一帧里两次读到的内容不一致"就无从排查）；
    //  · 属性袋是**复制**（不是抓活字典），Build 之后模型再改不影响已发出的快照。
    // ============================================================================

    /// <summary>
    /// 一张画面的不可变快照：页面元信息 + 基座列表（每只只递归一层）+ 本页全部图元。
    ///
    /// <b>每只基座只递归一层</b>：<see cref="BasePages"/> 里每只快照的 <see cref="BasePages"/> 恒为空
    /// ——模型侧本就禁止"基座再套基座"，渲染器按"基座列表顺序逐层叠 + 本页"合成即可。
    /// <see cref="BasePage"/> 保留为第一只基座的单值兼容视图。
    ///
    /// <b>图元一律摊平</b>：容器（<c>Hmi.Panel</c>）的子图元也在同一张 <see cref="Elements"/> 里，
    /// 靠 <see cref="ScadaElementSnapshot.ParentId"/> 表达"装在谁里面"。刻意不在这里再挂一棵树：
    /// 模型里父子关系只有 <c>ParentId</c> 一个来源（坐标仍是画面绝对坐标），
    /// 快照再建一份嵌套结构就等于多一份真相，且渲染器的绘制次序本来就是按 ZIndex 摊平排的。
    /// </summary>
    public sealed class ScadaPageSnapshot
    {
        /// <summary>画面稳定身份（<see cref="System.Guid"/> 的 "D" 格式，如 <c>3f2504e0-4f89-11d3-9a0c-0305e82c3301</c>）</summary>
        public string PageId { get; init; } = string.Empty;

        /// <summary>画面名（标签页标题、画面跳转下拉框用；不参与寻址）</summary>
        public string PageName { get; init; } = string.Empty;

        /// <summary>
        /// 快照取自 <see cref="ScadaPage.Version"/> 的那一刻的值。
        ///
        /// 渲染器按它判断"内容有没有变过"（同一值 = 内容一致，可复用上一次的渲染结果）；
        /// 它是<b>只增不减</b>的变更计数，不是"第几次快照"。
        /// </summary>
        public int PageVersion { get; init; }

        /// <summary>
        /// 基座层此刻的合并版本号：所有基座 <see cref="ScadaPage.Version"/> 之和；<b>无基座为 <c>0</c></b>。
        ///
        /// 任一基座内容变化都会让这个值变化，服务端把它拼进页键即可让 web 缓存失效。
        /// 注意它对"基座顺序变化"不敏感（求和不含位置）——当前没有换序 UI；将来加排序时
        /// 必须改成顺序敏感的哈希/拼接。
        /// </summary>
        public int BasePageVersion { get; init; }

        /// <summary>画布宽（设计像素，不是窗口像素）</summary>
        public double Width { get; init; }

        /// <summary>画布高（设计像素）</summary>
        public double Height { get; init; }

        /// <summary>背景色（<c>#AARRGGBB</c> 文本；领域层不解析它，转换交给渲染器）</summary>
        public string Background { get; init; } = string.Empty;

        /// <summary>
        /// 页面背景图（方案内相对路径，如 <c>Image\pid.png</c>；空 = 只用背景色）。
        /// Web 渲染器由宿主按方案路由取图，快照里只带相对路径，不带绝对路径。
        /// </summary>
        public string BackgroundImage { get; init; } = string.Empty;

        /// <summary>背景图填充方式（成员名：Uniform / UniformToFill / Fill / Tile）</summary>
        public string BackgroundStretch { get; init; } = "Uniform";

        /// <summary>背景图不透明度（10~100）</summary>
        public double BackgroundOpacity { get; init; } = 100;

        /// <summary>
        /// 本页引用的<b>全部基座</b>快照（按渲染顺序：靠前的在下面）。
        ///
        /// <see cref="BasePage"/> 是它的第一项（单基座兼容视图）；新渲染器按本列表逐层叠。
        /// 空列表 = 本页不引用基座，或引用的基座都已被删除/摘除。
        /// </summary>
        public IReadOnlyList<ScadaPageSnapshot> BasePages { get; init; } = System.Array.Empty<ScadaPageSnapshot>();

        /// <summary>本页是不是弹窗页（特殊画面）。弹窗页不参与页面菜单/普通导航。</summary>
        public bool IsPopup { get; init; }

        /// <summary>弹窗九宫格锚点（成员名，如 <c>Center</c>）；非弹窗页为 null。</summary>
        public string? PopupAnchor { get; init; }

        /// <summary>弹窗是否模态；非弹窗页为 false。</summary>
        public bool PopupModal { get; init; }

        /// <summary>
        /// 当前打开的弹窗层（web 镜像用）；<c>null</c> = 没有弹窗。
        /// 桌面端的弹窗由运行窗口自己的浮层渲染，这个字段只给 web 跟随。
        /// </summary>
        public ScadaPageSnapshot? Popup { get; init; }

        /// <summary>
        /// 基座画面快照（跨页公共层）；<c>null</c> = 本页不引用基座，或引用的基座已被删除/摘除。
        ///
        /// <b>基座元素在 <c>BasePage.Elements</c> 里</b>，不与本页元素混在一起：混在一起之后
        /// "这个图元能不能在本页上选中/编辑"就再也分不清了（基座是只读的公共层）。
        /// </summary>
        public ScadaPageSnapshot? BasePage { get; init; }

        /// <summary>本页全部图元（含容器子图元），按 <see cref="ScadaElementSnapshot.ZIndex"/> 升序</summary>
        public IReadOnlyList<ScadaElementSnapshot> Elements { get; init; } = System.Array.Empty<ScadaElementSnapshot>();
    }

    /// <summary>
    /// 一个图元的不可变快照。字段与 <see cref="ScadaElement"/> 的强类型成员一一对应，
    /// 属性袋原样搬运（<b>不解释</b>：颜色/枚举/单位的含义由渲染器的图元描述符决定）。
    /// </summary>
    public sealed class ScadaElementSnapshot
    {
        /// <summary>图元稳定身份（Guid "D"；画面内部按它寻址，容器子图元靠它被 <see cref="ParentId"/> 指向）</summary>
        public string ElementId { get; init; } = string.Empty;

        /// <summary>图元类型键（如 <c>Hmi.Rectangle</c> / <c>Hmi.Tank</c>，取值由图元注册表定义）</summary>
        public string TypeKey { get; init; } = string.Empty;

        /// <summary>图元名（图层列表、属性面板标题用；不参与寻址）</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>左上角 X（画面绝对坐标）</summary>
        public double X { get; init; }

        /// <summary>左上角 Y（画面绝对坐标）</summary>
        public double Y { get; init; }

        public double Width { get; init; }

        public double Height { get; init; }

        /// <summary>旋转角（度，顺时针，绕图元中心）</summary>
        public double Rotation { get; init; }

        /// <summary>Z 序（越大越靠前）</summary>
        public int ZIndex { get; init; }

        /// <summary>
        /// 所属底板（容器图元）的身份；<c>null</c> = 顶层图元。
        ///
        /// 子图元的 <see cref="X"/>/<see cref="Y"/> 仍是<b>画面绝对坐标</b>（模型就是这么存的），
        /// 渲染器若把子图元挂进容器的局部坐标系，需要自己减去容器的 X/Y——快照不替它做这一步，
        /// 因为"怎么挂"是渲染实现的选择（直接绝对定位同样成立）。
        /// </summary>
        public string? ParentId { get; init; }

        /// <summary>所属图层身份；<c>null</c> = 未分层（或归属的图层已被删除）</summary>
        public string? LayerId { get; init; }

        /// <summary>
        /// 所属图层此刻是否可见（图层隐藏 → 本图元不该被渲染）。
        ///
        /// <b>找不到图层时按 <c>true</c></b>：未分层与"指向已删除图层的悬空 Id"都按"看得见"处理
        /// ——脏数据不该让图元凭空消失（与 <see cref="ScadaPage.IsElementVisible"/> 同一口径）。
        /// </summary>
        public bool LayerVisible { get; init; } = true;

        /// <summary>
        /// 操作本图元所需的最低角色（<see cref="ScadaRole"/> 的<b>成员名</b>：<c>Operator</c> /
        /// <c>Engineer</c> / <c>Administrator</c>）；<c>null</c> = 不限制。
        ///
        /// 用成员名而不是数值：数值是落盘标识（不许变），但给渲染器的是一句"能读懂的档位"，
        /// 且渲染器不该认识"2 等于工程师"这种领域知识。
        /// </summary>
        public string? RequiredRole { get; init; }

        /// <summary>
        /// 此刻这个图元<b>能不能被下发命令</b>（M2 caps）。
        ///
        /// <b>默认 <c>false</c></b>：快照构建器（<see cref="ScadaPageSnapshotBuilder"/>）刻意<b>不</b>读权限策略——
        /// 它只搬模型事实，而"谁能点"取决于当前在线会话集合与 <c>ScadaWeb.ClickPolicy</c>，
        /// 那些都不在模型里。默认 false 的含义是"未评估"，宁可让界面先置灰，也不许
        /// 因为"没人填"而默认放行（一次静默的权限放大，比多一次点击失败严重得多）。
        ///
        /// 由宿主集成时按 <c>ScadaWebAccessPolicy.CanOperate</c> 逐图元填充；
        /// 判定口径（角色、最高角色策略、只读总闸）只在策略里有一份实现。
        /// </summary>
        public bool CanOperate { get; init; }

        /// <summary>
        /// 不可操作时的<b>一句话原因</b>（"需要「工程师」权限，当前是「操作员」"）；
        /// 可操作时为 <c>null</c>。
        ///
        /// 与 <see cref="CanOperate"/> 成对：只给布尔值，界面上只能显示一句笼统的"不可用"，
        /// 而现场最需要知道的正是"为什么不可用"（权限不够？只读模式？没有已登录的操作者？）。
        /// </summary>
        public string? OperateReason { get; init; }

        /// <summary>图元特有属性袋（模型里的键值原样复制，不解释、不补默认值）</summary>
        public IReadOnlyDictionary<string, string> Properties { get; init; }
            = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>());

        /// <summary>变量绑定（数据流：变量 → 图元属性）</summary>
        public IReadOnlyList<ScadaBindingSnapshot> Bindings { get; init; } = System.Array.Empty<ScadaBindingSnapshot>();

        /// <summary>动画（映射：变量值 → 换色 / 移位 / 显隐）</summary>
        public IReadOnlyList<ScadaAnimationSnapshot> Animations { get; init; } = System.Array.Empty<ScadaAnimationSnapshot>();
    }

    /// <summary>
    /// 一条绑定的不可变快照（对应 <see cref="ScadaBinding"/> 的全部可写字段）。
    /// </summary>
    public sealed class ScadaBindingSnapshot
    {
        /// <summary>被绑定的图元属性键（如 <c>Value</c> / <c>FillColor</c>；含义由图元描述符决定）</summary>
        public string TargetProperty { get; init; } = string.Empty;

        /// <summary>变量稳定身份（Guid "D"）；<c>""</c> = 旧数据，只能按 <see cref="VariableName"/> 找</summary>
        public string VariableId { get; init; } = string.Empty;

        /// <summary>变量名（展示串 + 旧数据兜底键；权威寻址仍是 <see cref="VariableId"/>）</summary>
        public string? VariableName { get; init; }

        /// <summary>显示格式化串（如 <c>F2</c> / <c>yyyy-MM-dd HH:mm:ss</c>）；<c>null</c> = 不格式化</summary>
        public string? DisplayFormat { get; init; }

        /// <summary>是否启用；<c>false</c> = 运行态跳过本绑定但配置保留</summary>
        public bool Enabled { get; init; }
    }

    /// <summary>
    /// 一条动画的不可变快照（对应 <see cref="ScadaAnimation"/> 的全部可写字段 + 档位表）。
    ///
    /// <b>不按 <see cref="Type"/> 挑字段搬</b>：模型里四种动画的字段是并排摆着的
    /// （外观变化用 <see cref="States"/>、水平移动用 <see cref="EndX"/>……），
    /// 挑着搬等于在"哪些字段对哪种动画有意义"上留下第二份口径。
    /// </summary>
    public sealed class ScadaAnimationSnapshot
    {
        /// <summary>动画类型（<see cref="ScadaAnimationType"/> 的<b>成员名</b>：<c>Appearance</c> / <c>HorizontalMove</c> / <c>VerticalMove</c> / <c>Visibility</c>）</summary>
        public string Type { get; init; } = string.Empty;

        /// <summary>驱动变量的稳定身份（Guid "D"）；<c>""</c> = 还没选变量</summary>
        public string VariableId { get; init; } = string.Empty;

        /// <summary>驱动变量名（展示 + 旧数据兜底键）</summary>
        public string? VariableName { get; init; }

        /// <summary>范围下端点（<b>原文</b>：模型里是字符串，允许"1."这种打了一半的输入，解析交给消费方）</summary>
        public string RangeLow { get; init; } = string.Empty;

        /// <summary>范围上端点（原文，理由同 <see cref="RangeLow"/>）</summary>
        public string RangeHigh { get; init; } = string.Empty;

        /// <summary>水平/垂直移动的结束位置 X（画面绝对坐标；起始位置 = 图元当前的 X，不存副本）</summary>
        public double EndX { get; init; }

        /// <summary>垂直移动的结束位置 Y（画面绝对坐标）</summary>
        public double EndY { get; init; }

        /// <summary>可见性动画里"值命中范围时"的状态：<c>true</c> = 显示、<c>false</c> = 隐藏（不命中则交回图层判定）</summary>
        public bool VisibleInRange { get; init; } = true;

        /// <summary>这条动画启不启用（<c>false</c> = 配置保留、运行态不接管外观）</summary>
        public bool Enabled { get; init; } = true;

        /// <summary>「外观变化」的档位表（自上而下先命中先用；其余动画类型为空表）</summary>
        public IReadOnlyList<ScadaAnimationStateSnapshot> States { get; init; } = System.Array.Empty<ScadaAnimationStateSnapshot>();
    }

    /// <summary>
    /// 「外观变化」动画里的一档：<b>值域 → 一套外观</b>。
    ///
    /// <b>与契约草案的差异（实测模型决定，不是漏写）</b>
    /// ---------
    /// · <see cref="From"/> / <see cref="To"/> 是 <b>string</b> 而不是 double：模型
    ///   <see cref="ScadaAnimationState.ValueLow"/>/<see cref="ScadaAnimationState.ValueHigh"/>
    ///   本就是字符串（用户在输入框里打了一半的 "1." 不该被当场纠正，空串表示"这档还没配"）。
    ///   在这里 parse 成 double 只能靠"解析失败就填 0"的兜底，那是把用户的半成品静默改写成 0。
    /// · <see cref="Visible"/> <b>恒为 <c>null</c></b>：当前模型没有"档位级显隐"这个字段
    ///   （显隐是 <see cref="ScadaAnimationType.Visibility"/> 那条动画的
    ///   <see cref="ScadaAnimationSnapshot.VisibleInRange"/>）。保留这个字段是为了让 Web 端
    ///   按草案写的取值代码不用改；将来模型真加了档位显隐，这里填值即可，契约不变。
    /// </summary>
    public sealed class ScadaAnimationStateSnapshot
    {
        /// <summary>值域下端点（模型原文；与 <see cref="To"/> 相同 = 单点值）</summary>
        public string From { get; init; } = string.Empty;

        /// <summary>值域上端点（模型原文；闭区间 <c>From ≤ 值 ≤ To</c>）</summary>
        public string To { get; init; } = string.Empty;

        /// <summary>本档背景色（<c>#AARRGGBB</c> 文本；<c>null</c> = 本档不改背景色）</summary>
        public string? Fill { get; init; }

        /// <summary>本档前景色（<c>#AARRGGBB</c> 文本；<c>null</c> = 本档不改前景色）</summary>
        public string? Foreground { get; init; }

        /// <summary>本档是否闪烁</summary>
        public bool Flash { get; init; }

        /// <summary>本档的显隐（<c>null</c> = 本档不接管显隐；当前模型没有该字段，恒为 null，见类注释）</summary>
        public bool? Visible { get; init; }
    }
}
