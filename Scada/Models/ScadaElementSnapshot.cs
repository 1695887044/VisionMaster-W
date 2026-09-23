using System;
using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 图元快照：把 <see cref="ScadaElement"/> 摊平成一份<b>不可变纯数据</b>。
    ///
    /// <b>为什么是快照，不是"抓着活对象"</b>
    /// ---------
    /// 剪贴板与模板共用这一种载体，两条硬理由把"活对象"这条路堵死了：
    /// ① <b>连粘两次必撞身份</b>——活对象只有一个 <c>ElementId</c>，粘第二遍时画面上就有
    ///    两个同 Id 的图元，而 <see cref="ScadaPage.FindElement"/> 按"保留靠前者"解析，
    ///    第二个从此永远选不中、绑定也刷不到它；快照每次物化都<b>重编 Id</b>，天然不会撞。
    /// ② <b>模板要落盘</b>——模板得存成 JSON 活到下次开软件，活对象拖着一整棵 WPF 无关但
    ///    仍带订阅的模型树，序列化它等于把运行时状态也写进模板文件。
    ///
    /// <b>为什么不直接用 JSON 深拷贝图元</b>
    /// ---------
    /// <c>SolutionService</c> 的序列化设置带 <c>TypeNameHandling.Auto</c> + <c>$type</c> 白名单
    /// （只放行 Models / CommunicationContracts 命名空间）。走 JSON 做深拷贝就会踩白名单，
    /// 而绕开白名单去放宽序列化安全更是捡了芝麻丢了西瓜。本类型手写字段搬运，
    /// 一个字节的 <c>$type</c> 都不产生。
    ///
    /// <b>哪些字段刻意不搬</b>
    /// ---------
    /// <see cref="ScadaElement.ElementId"/> 不搬（物化时重生成，见上）；
    /// <see cref="ScadaElement.LayerId"/> / <see cref="ScadaElement.GroupId"/> 搬的是
    /// <b>语义</b>而不是 Guid——见 <see cref="LayerName"/> 与 <see cref="GroupSlot"/>。
    /// </summary>
    public sealed class ScadaElementSnapshot
    {
        /// <summary>未分组 / 无组槽位</summary>
        public const int NoGroupSlot = -1;

        /// <summary>图元类型键（决定物化出来的是个什么东西）</summary>
        public string TypeKey { get; set; } = string.Empty;

        /// <summary>图元名（物化时会在目标画面内重新去重）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>左上角 X（物化时加上落点偏移）</summary>
        public double X { get; set; }

        /// <summary>左上角 Y（物化时加上落点偏移）</summary>
        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public double Rotation { get; set; }

        /// <summary>源画面的 Z 序（物化时只用来排相对先后，绝对值会被重算）</summary>
        public int ZIndex { get; set; }

        public bool IsLocked { get; set; }

        public ScadaRole? RequiredRole { get; set; }

        /// <summary>
        /// 源图元所在图层的<b>名字</b>（未分层 / 悬空归属时为 <c>null</c>）。
        ///
        /// 为什么不搬 <c>LayerId</c>：跨画面粘贴时那个 Guid 在新画面里必然不存在，
        /// 搬过去只会得到一批"指向已删除图层"的悬空图元（它们在渲染上等同未分层，
        /// 但图层列表里数不出来，用户会以为东西丢了）。按名字回查是唯一能跨画面成立的解释，
        /// 查不到就落默认图层——与 <c>EnsureIdentity</c> 把平铺旧数据归进默认图层同一条口径。
        /// </summary>
        public string? LayerName { get; set; }

        /// <summary>
        /// 组槽位：<see cref="NoGroupSlot"/> = 未分组；同一次拷贝里<b>相同非负值 = 同一组</b>。
        ///
        /// 为什么不搬 <c>GroupId</c>：与 <see cref="LayerName"/> 同一个理由（Guid 出了源画面就失效），
        /// 但组还多一层要求——粘出来的副本<b>自己之间</b>仍要成组。于是快照只记"哪几个是一伙的"
        /// 这个槽位，物化时给每个槽位现造一个新 Guid，既不与画面里任何现存组串味，
        /// 又保住了"拷一整组、粘出来还是一整组"。
        /// </summary>
        public int GroupSlot { get; set; } = NoGroupSlot;

        /// <summary>图元特有属性袋（逐键搬运）</summary>
        public Dictionary<string, string> Properties { get; set; } = new();

        /// <summary>变量绑定（物化时逐条重建）</summary>
        public List<ScadaBindingSnapshot> Bindings { get; set; } = new();

        /// <summary>事件钩子（物化时逐条重建）</summary>
        public List<ScadaEventHookSnapshot> EventHooks { get; set; } = new();

        /// <summary>动画（物化时逐条重建，含「外观变化」的档位表）</summary>
        public List<ScadaAnimationSnapshot> Animations { get; set; } = new();
    }

    /// <summary>
    /// 绑定快照。<see cref="ScadaBinding"/> 上可写的 5 个字段逐一对应——
    /// <c>IsLegacyByName</c> 那类只读派生属性由字段自己算出来，不搬。
    /// </summary>
    public sealed class ScadaBindingSnapshot
    {
        public string TargetProperty { get; set; } = string.Empty;

        public Guid VariableId { get; set; }

        public string? VariableName { get; set; }

        public bool IsEnabled { get; set; } = true;

        public string? DisplayFormat { get; set; }
    }

    /// <summary>
    /// 事件钩子快照。搬的是"哪个事件 + 一串动作"，
    /// 与 <see cref="ScadaEventHook"/> 的存储形状一一对应。
    /// </summary>
    public sealed class ScadaEventHookSnapshot
    {
        public ScadaEventType Event { get; set; }

        public List<ScadaActionSnapshot> Actions { get; set; } = new();
    }

    /// <summary>
    /// 动作快照。<see cref="ScadaAction"/> 是扁平类（类型字段 + 各类型参数并排摆着），
    /// 所以这里也照原样搬全部 7 个字段，不按 <see cref="Type"/> 挑字段搬——
    /// 挑着搬就等于在"哪些字段对哪种类型有意义"这件事上留下第二份口径，
    /// 而领域层本来就刻意不认识这套对应关系（见 <see cref="ScadaAction"/> 类注释）。
    /// </summary>
    public sealed class ScadaActionSnapshot
    {
        public ScadaActionType Type { get; set; } = ScadaActionType.Log;

        public string? Text { get; set; }

        public Guid VariableId { get; set; }

        public string? VariableName { get; set; }

        public string? Value { get; set; }

        public Guid TargetPageId { get; set; }

        public string? TargetPageName { get; set; }
    }

    /// <summary>
    /// 动画快照。搬的是 <see cref="ScadaAnimation"/> 上全部可写字段 + 内层档位表。
    ///
    /// 与 <see cref="ScadaActionSnapshot"/> 同一个口径：<b>不按 <see cref="Type"/> 挑字段搬</b>。
    /// 四种动画的字段是并排摆着的（外观变化用 <see cref="States"/>，水平移动用 <see cref="EndX"/>……），
    /// 挑着搬就等于在"哪些字段对哪种动画有意义"上留下第二份口径，而领域层本来就刻意
    /// 不认识这套对应关系（换类型时旧字段原地留着，用户改回原类型能拿回原来的配置）。
    /// </summary>
    public sealed class ScadaAnimationSnapshot
    {
        public ScadaAnimationType Type { get; set; } = ScadaAnimationType.Appearance;

        public bool IsEnabled { get; set; } = true;

        public Guid VariableId { get; set; }

        public string? VariableName { get; set; }

        public string RangeLow { get; set; } = "0";

        public string RangeHigh { get; set; } = "100";

        public double EndX { get; set; }

        public double EndY { get; set; }

        public bool VisibleInRange { get; set; } = true;

        /// <summary>「外观变化」的档位表（其余动画类型为空表）</summary>
        public List<ScadaAnimationStateSnapshot> States { get; set; } = new();
    }

    /// <summary>
    /// 外观档位快照：<see cref="ScadaAnimationState"/> 的 5 个可写字段逐一对应。
    /// 派生展示属性（<c>RangeText</c> / <c>Detail</c>）由字段自己算出来，不搬。
    /// </summary>
    public sealed class ScadaAnimationStateSnapshot
    {
        public string ValueLow { get; set; } = "0";

        public string ValueHigh { get; set; } = "0";

        public string? Foreground { get; set; }

        public string? Fill { get; set; }

        public bool IsFlashing { get; set; }
    }
}
