using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 画布节点的三种身份（海康式一图全展开）。
    ///
    /// 与旧子画布模型（Step/LayerRoot/Lane）的差异：扁平化后没有"层根锚点"——
    /// 所有深度的步骤都在同一张图上，祖先容器自身渲染成可折叠的容器框，
    /// 祖先的产出（For.Index 等）直接从容器框连出，不再需要影子节点。
    /// </summary>
    public enum CanvasNodeKind
    {
        /// <summary>图纸里的真实步骤，可拖、可选、坐标入 FlowLayoutStore、显示执行序号</summary>
        Step,

        /// <summary>分支泳道：容器框内的列背景装饰，铺在同分支节点底下，不可拖不可选</summary>
        Lane,

        /// <summary>
        /// 容器框（If/While/For）：本身也是真实步骤（参与执行序、可被连线消费/生产），
        /// 框体几何由其子孙的外接框算出、不入库；可折叠（折叠态持久化在 FlowLayoutStore.Collapsed）。
        /// </summary>
        Container,
    }

    /// <summary>
    /// 画布上的一个节点：对应图纸里的一个 <see cref="StepModel"/>（Step/Container），或一条分支泳道（装饰）。
    ///
    /// 节点身份一律用 StepID 表达：连线写回、运行态高亮、错误定位都靠它，不用步骤名（可重复、可改）。
    /// </summary>
    public class CanvasNodeViewModel : BindableBase
    {
        /// <summary>真实步骤/容器节点</summary>
        public CanvasNodeViewModel(StepModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Kind = model is IContainerStep ? CanvasNodeKind.Container : CanvasNodeKind.Step;

            // 画布只订集合增删不订属性变更，改名/禁用发生在流程栏时画布不重建——
            // 节点自己跟随模型刷标题相关投影。PrunePool 摘池时 UnhookModel 释放
            model.PropertyChanged += OnModelPropertyChanged;

            // 容器摘要的实时刷新由 StepModel 统一订阅（自身属性 + 分支 Expression），
            // 画布节点只转发模型通知，见 OnModelPropertyChanged。
        }

        private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(StepModel.StepName) or nameof(StepModel.IsDisEnable))
            {
                RaisePropertyChanged(nameof(Header));
                RaisePropertyChanged(nameof(DisableMenuHeader));
            }

            // 断点标记：右键菜单的勾选态绑 IsBreakpoint（与流程栏 IsBreakpointSet 同口径）。
            // 菜单是共享实例，不转发这条通知的话勾选态会停在上一次打开的节点上。
            if (e.PropertyName == nameof(StepModel.IsBreakpoint))
                RaisePropertyChanged(nameof(IsBreakpoint));

            // 注释在配置界面/流程栏编辑，画布节点下方的注释行要即时跟随
            if (e.PropertyName == nameof(StepModel.Description))
            {
                RaisePropertyChanged(nameof(Comment));
                RaisePropertyChanged(nameof(CommentVisibility));
                RaisePropertyChanged(nameof(CardTooltip));
            }

            // 必填参数的判定原料变了：手填常量（InputValues）或上游连线（LinkedSources）。
            // StepModel 的统一写路径（SetInputValue/RemoveInputValue/SetLink/RemoveLink）都会发这两条通知
            if (e.PropertyName is nameof(StepModel.InputValues) or nameof(StepModel.LinkedSources))
            {
                RefreshMissingRequired();
                // 比较/逻辑节点的操作徽标与副标题都吃这两个字典：改完参数要即时重投影
                RaisePropertyChanged(nameof(OpBadge));
                RaisePropertyChanged(nameof(OpBadgeVisibility));
                RaisePropertyChanged(nameof(SubTitle));
                RaisePropertyChanged(nameof(CardTooltip));
            }

            // 容器摘要的唯一通知源在 StepModel（自身属性 + 分支 Expression 都在那里订阅）；
            // 这里只把模型的通知转发给画布节点，避免画布再维护第二套分支订阅。
            if (e.PropertyName is nameof(StepModel.ContainerSummary)
                or nameof(StepModel.ContainerSummaryVisibility))
            {
                RaisePropertyChanged(e.PropertyName);
                RaisePropertyChanged(nameof(ContainerMetaText));
                RaisePropertyChanged(nameof(HasMissingCondition));
            }
        }

        /// <summary>
        /// 节点退出渲染池时摘掉模型订阅。模型可能比节点活得久（留在图纸里），
        /// 不摘的话 PropertyChanged 委托链会钉住节点 VM 阻止 GC。
        /// </summary>
        public void UnhookModel()
        {
            if (Model != null) Model.PropertyChanged -= OnModelPropertyChanged;
        }

        /// <summary>泳道装饰节点专用</summary>
        private CanvasNodeViewModel(StepCollection branch, StepModel parent, int branchIndex)
        {
            Kind = CanvasNodeKind.Lane;
            Branch = branch;
            LaneParent = parent;
            LaneBranchIndex = branchIndex;

            // 泳道每帧新建（见主 VM 渲染阶段注释），订阅必须可摘：
            // UnhookBranch 由主 VM 在丢弃旧泳道前统一调用，否则引擎跨线程打标的
            // PropertyChanged 会钉住整帧旧泳道 VM 阻止 GC（与 UnhookModel 同一条纪律）
            branch.PropertyChanged += OnBranchPropertyChanged;
            // 父容器失败/条件缺失时泳道整列红染：订阅 State 与 ContainerSummary（后者由容器
            // 在分支 Expression 变化时补发），与分支订阅一起在 UnhookBranch 摘除
            parent.PropertyChanged += OnLaneParentPropertyChanged;
        }

        private void OnLaneParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(StepModel.State)
                or nameof(StepModel.ContainerSummary)
                or nameof(StepModel.ContainerSummaryVisibility))
            {
                RaisePropertyChanged(nameof(IsLaneParentFailed));
                RaisePropertyChanged(nameof(IsLaneParentMissing));
                RaisePropertyChanged(nameof(IsLaneParentFaulted));
            }
        }

        private void OnBranchPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 分支执行中（并行扇出打标）→ 泳道执行染色。IsRunning 是标量属性，
            // 引擎分支线程发通知、WPF 自动编组，与 StepModel.State 同款
            if (e.PropertyName == nameof(StepCollection.IsRunning))
                RaisePropertyChanged(nameof(IsBranchRunning));

            if (e.PropertyName == nameof(StepCollection.BranchType))
                RaisePropertyChanged(nameof(IsNeutralLane));

            if (e.PropertyName is nameof(StepCollection.StepName) or nameof(StepCollection.DisplayName))
                RaisePropertyChanged(nameof(LaneName));
        }

        /// <summary>泳道退出渲染时摘掉分支/父容器订阅（主 VM 在清旧泳道列表前逐个调用）</summary>
        public void UnhookBranch()
        {
            if (Branch != null) Branch.PropertyChanged -= OnBranchPropertyChanged;
            if (LaneParent != null) LaneParent.PropertyChanged -= OnLaneParentPropertyChanged;
        }

        /// <summary>造一条分支泳道。branch 引用用于认人（同名分支可能有多个，实例才是唯一身份），
        /// 泳道标题绑 branch.DisplayName，分支改名即时生效；parent/index 供类型配色与 A/B/C 徽标</summary>
        public static CanvasNodeViewModel CreateLane(StepCollection branch, StepModel parent, int branchIndex)
            => new(branch, parent, branchIndex);

        /// <summary>
        /// 分支执行中（并行扇出打标）：泳道模板据此把底色/边框染成运行色。
        /// 引擎的 CompiledParallelNode.ExecuteBranch 置 StepCollection.IsRunning，这里只做转发投影。
        /// </summary>
        public bool IsBranchRunning => Kind == CanvasNodeKind.Lane && Branch?.IsRunning == true;

        /// <summary>
        /// 中性泳道：If 容器的 Else/ElseIf 分支、Case 容器的默认分支。
        /// flow-logic 里这些分支不套类型色（灰底灰边灰徽标），避免“非条件命中路径”抢走类型识别。
        /// </summary>
        public bool IsNeutralLane
        {
            get
            {
                if (Kind != CanvasNodeKind.Lane) return false;

                if (LaneParent is CaseStep)
                    return Branch?.BranchType is BranchType.Default or BranchType.Else;

                if (LaneParent is ConditionStep && LaneParent is not WhileStep)
                    return Branch?.BranchType is BranchType.Else or BranchType.ElseIf;

                return false;
            }
        }

        /// <summary>父容器运行态失败：泳道整列红染（设计状态矩阵“失败组头 + 子分支保留自己的状态”）</summary>
        public bool IsLaneParentFailed
            => Kind == CanvasNodeKind.Lane && LaneParent?.State == StepState.Failed;

        /// <summary>父容器条件缺失（编译前硬错误）：与失败同色红染，让缺条件在画布上一眼可见</summary>
        public bool IsLaneParentMissing
            => Kind == CanvasNodeKind.Lane
               && LaneParent is IContainerStep container
               && container.Children != null
               && container.Children.Any(branch => branch?.IsConditionMissing == true);

        /// <summary>父容器失败或条件缺失（模板单触发器用；两个独立属性保留给断言/检查器）</summary>
        public bool IsLaneParentFaulted => IsLaneParentFailed || IsLaneParentMissing;

        /// <summary>背后的步骤模型；泳道为 null</summary>
        public StepModel? Model { get; }

        /// <summary>泳道对应的分支集合；非泳道为 null</summary>
        public StepCollection? Branch { get; }

        /// <summary>泳道所属容器（决定类型配色与说明条）；非泳道为 null</summary>
        public StepModel? LaneParent { get; }

        /// <summary>泳道在父容器 Children 里的下标（并行分支 A/B/C 徽标用）</summary>
        private int LaneBranchIndex { get; }

        /// <summary>节点身份，决定模板选择与拖拽/选中规则</summary>
        public CanvasNodeKind Kind { get; }

        /// <summary>步骤唯一标识；泳道为 Guid.Empty</summary>
        public Guid StepId => Model?.StepID ?? Guid.Empty;

        /// <summary>节点标题（泳道回读分支显示名；步骤禁用时加后缀，与流程栏观感一致）</summary>
        public string Header
        {
            get
            {
                if (Kind == CanvasNodeKind.Lane)
                    return Branch?.DisplayName ?? string.Empty;

                var name = Model!.StepName;
                return Model.IsDisEnable ? $"{name}（已禁用）" : name;
            }
        }

        // ------------------------------------------------------------------
        //  节点副标题 / 操作徽标（flow-logic）：比较与逻辑插件把端口值拼成表达式，
        //  其它节点回退插件名。值一律来自 StepModel.InputValues / LinkedSources，
        //  不造演示数据；徽标显示大写成员名（EQ/NEQ/AND/OR…），不显示中文。
        // ------------------------------------------------------------------

        private static readonly string[] ComparisonMembers =
        {
            "Equal", "NotEqual", "GreaterThan", "LessThan", "GreaterThanOrEqual", "LessThanOrEqual",
        };

        private static readonly string[] LogicMembers =
        {
            "And", "Or", "Not", "Xor", "Nand", "Nor",
        };

        private bool IsComparisonPlugin
            => Kind == CanvasNodeKind.Step
               && Model is ActionStep
               && ((Model.PluginTypeName?.Contains("ComparisonPlugin", StringComparison.OrdinalIgnoreCase) ?? false)
                   || string.Equals(Model.PluginName, "数值比较", StringComparison.Ordinal));

        private bool IsLogicPlugin
            => Kind == CanvasNodeKind.Step
               && Model is ActionStep
               && ((Model.PluginTypeName?.Contains("LogicOperationPlugin", StringComparison.OrdinalIgnoreCase) ?? false)
                   || string.Equals(Model.PluginName, "逻辑运算", StringComparison.Ordinal));

        /// <summary>操作徽标：EQ/NEQ/GT/LT/GTE/LTE、AND/OR/NOT/XOR/NAND/NOR；其它节点为空串（不占位）</summary>
        public string OpBadge
        {
            get
            {
                if (IsComparisonPlugin)
                {
                    return ResolveEnumMember("Type", ComparisonMembers, 0) switch
                    {
                        "Equal" => "EQ",
                        "NotEqual" => "NEQ",
                        "GreaterThan" => "GT",
                        "LessThan" => "LT",
                        "GreaterThanOrEqual" => "GTE",
                        "LessThanOrEqual" => "LTE",
                        _ => "EQ",
                    };
                }

                if (IsLogicPlugin)
                {
                    return ResolveEnumMember("Operation", LogicMembers, 0) switch
                    {
                        "And" => "AND",
                        "Or" => "OR",
                        "Not" => "NOT",
                        "Xor" => "XOR",
                        "Nand" => "NAND",
                        "Nor" => "NOR",
                        _ => "AND",
                    };
                }

                return string.Empty;
            }
        }

        public Visibility OpBadgeVisibility
            => string.IsNullOrEmpty(OpBadge) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>副标题：比较/逻辑插件拼 A/B/Tolerance 或上游端口；无可用摘要时回退插件名</summary>
        public string SubTitle
        {
            get
            {
                if (Kind != CanvasNodeKind.Step) return string.Empty;

                var summary = BuildOperationSummary();
                return string.IsNullOrWhiteSpace(summary)
                    ? Model?.PluginName ?? string.Empty
                    : summary;
            }
        }

        private string BuildOperationSummary()
        {
            if (IsComparisonPlugin)
            {
                var a = ResolveOperand("A");
                var b = ResolveOperand("B");
                if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                    return string.Empty;

                var text = $"{a} {ComparisonSymbol} {b}";
                var tolerance = ResolveOperand("Tolerance");
                if (!string.IsNullOrWhiteSpace(tolerance) && !IsDefaultTolerance(tolerance))
                    text += $" ± {tolerance}";

                return text;
            }

            if (IsLogicPlugin)
            {
                var a = ResolveOperand("A");
                var b = ResolveOperand("B");
                var operation = ResolveEnumMember("Operation", LogicMembers, 0);

                if (operation == "Not")
                    return string.IsNullOrWhiteSpace(a) ? string.Empty : $"NOT {a}";

                if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                    return string.Empty;

                return $"{a} {operation.ToUpperInvariant()} {b}";
            }

            // 等待节点不是容器，但条件表达式是它最有信息量的摘要（真实模型字段，不伪造）
            if (Model is WaitStep wait && !string.IsNullOrWhiteSpace(wait.ConditionExpression))
                return wait.ConditionExpression;

            return string.Empty;
        }

        private string ComparisonSymbol => ResolveEnumMember("Type", ComparisonMembers, 0) switch
        {
            "NotEqual" => "≠",
            "GreaterThan" => ">",
            "LessThan" => "<",
            "GreaterThanOrEqual" => "≥",
            "LessThanOrEqual" => "≤",
            _ => "=",
        };

        /// <summary>
        /// 解析枚举型输入值：InputValues 可能存成员名、整数（Newtonsoft 默认）或枚举对象。
        /// 解析不到时回退到插件默认成员（比较=Equal、逻辑=And），与实际执行语义一致。
        /// </summary>
        private string ResolveEnumMember(string key, string[] members, int fallbackIndex)
        {
            if (Model?.InputValues != null
                && Model.InputValues.TryGetValue(key, out var raw)
                && raw != null)
            {
                var text = raw switch
                {
                    Enum e => e.ToString(),
                    string s => s,
                    IConvertible c => c.ToInt32(CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                    _ => raw.ToString() ?? string.Empty,
                };

                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                    && index >= 0 && index < members.Length)
                    return members[index];

                var matched = members.FirstOrDefault(m => string.Equals(m, text, StringComparison.OrdinalIgnoreCase));
                if (matched != null) return matched;
            }

            return members[Math.Clamp(fallbackIndex, 0, members.Length - 1)];
        }

        /// <summary>端口摘要：上游连线优先（运行期以连线为准），否则手填常量；常量前缀只用于显示</summary>
        private string ResolveOperand(string portName)
        {
            if (Model?.LinkedSources != null
                && Model.LinkedSources.TryGetValue(portName, out var link)
                && link != null)
            {
                var address = link.DisplayAddress;
                if (!string.IsNullOrWhiteSpace(address))
                {
                    return address.StartsWith(LinkProtocol.ConstantDisplayPrefix, StringComparison.Ordinal)
                        ? address.Substring(LinkProtocol.ConstantDisplayPrefix.Length).Trim()
                        : address;
                }

                if (!string.IsNullOrWhiteSpace(link.TargetPortName))
                    return link.TargetPortName;
            }

            if (Model?.InputValues != null
                && Model.InputValues.TryGetValue(portName, out var value)
                && value != null)
            {
                return value switch
                {
                    double d => d.ToString("0.######", CultureInfo.InvariantCulture),
                    float f => f.ToString("0.######", CultureInfo.InvariantCulture),
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString() ?? string.Empty,
                };
            }

            return string.Empty;
        }

        private static bool IsDefaultTolerance(string text)
            => text is "1E-06" or "1E-6" or "0.000001" or "0" or "0.0";

        /// <summary>图标字形（与流程栏同一份 Icon 资源字体）；泳道为空串</summary>
        public string IconGlyph
        {
            get
            {
                if (Kind == CanvasNodeKind.Lane) return string.Empty;
                var icon = Model?.Icon;
                return string.IsNullOrWhiteSpace(icon) ? "\uea3c" : icon;
            }
        }

        /// <summary>容器类型徽标（IF / WHILE / FOR / PAR / CASE / WAIT）；非容器且非等待为空串</summary>
        public string TypeBadge => Model switch
        {
            // WhileStep / CaseStep 都继承 ConditionStep，必须先判子类
            WhileStep => "WHILE",
            CaseStep => "CASE",
            ParallelStep => "PAR",
            ConditionStep => "IF",
            ForStep => "FOR",
            // 等待节点不是容器，但带一条条件表达式 + 两个输出端口，值得在画布上给个身份徽标
            WaitStep => "WAIT",
            _ => string.Empty,
        };

        /// <summary>容器头部的条件/模式摘要（flow-logic 条件胶囊）；无摘要时折叠</summary>
        public string ContainerSummary => Model?.ContainerSummary ?? string.Empty;

        public Visibility ContainerSummaryVisibility
            => string.IsNullOrWhiteSpace(ContainerSummary) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>容器分支数（flow-logic 组头右侧 "N 分支"）</summary>
        public int BranchCount
            => Kind == CanvasNodeKind.Container && Model is IContainerStep container && container.Children != null
                ? container.Children.Count
                : 0;

        /// <summary>容器内算子总数（含嵌套容器；flow-logic 组头右侧 "M 算子"）</summary>
        public int DescendantCount => CountDescendants(Model);

        private static int CountDescendants(StepModel? step)
        {
            if (step is not IContainerStep container || container.Children == null) return 0;

            var count = 0;
            foreach (var branch in container.Children)
            {
                if (branch?.Steps == null) continue;
                foreach (var child in branch.Steps)
                {
                    if (child == null) continue;
                    count += 1 + CountDescendants(child);
                }
            }

            return count;
        }

        /// <summary>条件缺失（If/ElseIf/Case/WhileLoop 的 Expression 为空）= 编译前硬错误，容器红边红点</summary>
        public bool HasMissingCondition
            => Kind == CanvasNodeKind.Container
               && Model is IContainerStep container
               && container.Children != null
               && container.Children.Any(branch => branch?.IsConditionMissing == true);

        /// <summary>组头右侧统计：正常显示 "N 分支 · M 算子"，条件缺失时改为 "缺条件"</summary>
        public string ContainerMetaText
            => Kind == CanvasNodeKind.Container
                ? (HasMissingCondition ? "缺条件" : $"{BranchCount} 分支 · {DescendantCount} 算子")
                : string.Empty;

        /// <summary>并行分组底部汇合条文案（只用分支数，不伪造完成数/耗时）</summary>
        public string JoinBarText
            => Model is ParallelStep parallel && parallel.Children != null
                ? $"汇合 · {parallel.Children.Count} 分支"
                : string.Empty;

        /// <summary>汇合条只在展开的并行分组里显示（折叠后只剩头带）</summary>
        public Visibility JoinBarVisibility
            => Kind == CanvasNodeKind.Container && Model is ParallelStep && !IsCollapsed
                ? Visibility.Visible
                : Visibility.Collapsed;

        // ------------------------------------------------------------------
        //  右键菜单「分支结构」分组的可见性（画布与流程栏同一套判据）
        //
        //  此前加分支只能回流程栏点：画布节点菜单里根本没有这一类项。三个判据与
        //  ProcessViewModel.IsIfNodeSelected / IsCaseNodeSelected / IsParallelNodeSelected
        //  逐条同口径——分支能不能加，取决于容器类型，与"当前选中项"无关（画布菜单的
        //  DataContext 就是被点的那个节点，不存在命令靶错位）。
        // ------------------------------------------------------------------

        /// <summary>If 类容器（含 While 子类）：可加 ElseIf / Else 分支</summary>
        public bool IsIfLikeNode => Model is ConditionStep { } condition && condition.IsIfLike;

        /// <summary>分支匹配容器：可加 Case 分支</summary>
        public bool IsCaseNode => Model is CaseStep;

        /// <summary>并行分组：可加并行分支</summary>
        public bool IsParallelNode => Model is ParallelStep;

        /// <summary>任意容器节点（右键菜单里"分支结构"那一段的分隔线据此显隐）</summary>
        public bool IsContainerNode => Kind == CanvasNodeKind.Container;

        /// <summary>
        /// 右键菜单里有没有"分支结构"项（分隔线据此显隐）。
        /// 不是所有容器都能加分支：While/For 只有一个循环体，加分支没有语义。
        /// </summary>
        public bool HasBranchMenuItems => IsIfLikeNode || IsCaseNode || IsParallelNode;

        // ---- 泳道头：分支徽标 / 名称 / 计数 / 类型配色 / 底部说明条 ----

        /// <summary>泳道父容器类型（模板触发器按它选 soft/border/badge/text 四色）</summary>
        public string LaneParentKind => LaneParent switch
        {
            ParallelStep => "Parallel",
            CaseStep => "Case",
            WhileStep => "While",
            ForStep => "For",
            ConditionStep => "If",
            _ => string.Empty,
        };

        /// <summary>泳道分支徽标：If/Else/ElseIf、Case 序号/默认、While/For 的 BODY、并行 A/B/C</summary>
        public string BranchBadge
        {
            get
            {
                if (Kind != CanvasNodeKind.Lane) return string.Empty;

                return LaneParent switch
                {
                    WhileStep or ForStep => "BODY",
                    ParallelStep => LaneBranchIndex < 26
                        ? ((char)('A' + LaneBranchIndex)).ToString()
                        : (LaneBranchIndex + 1).ToString(CultureInfo.InvariantCulture),
                    CaseStep => Branch?.BranchType == BranchType.Else ? "默认" : $"CASE {CaseOrdinal()}",
                    ConditionStep => Branch?.BranchType switch
                    {
                        BranchType.Else => "ELSE",
                        BranchType.ElseIf => "ELSEIF",
                        BranchType.If => "IF",
                        _ => "分支",
                    },
                    _ => "分支",
                };
            }
        }

        private int CaseOrdinal()
        {
            if (LaneParent is not IContainerStep container || container.Children == null) return 1;

            var ordinal = 0;
            for (var i = 0; i < LaneBranchIndex && i < container.Children.Count; i++)
            {
                if (container.Children[i]?.BranchType == BranchType.Case)
                    ordinal++;
            }

            return ordinal + 1;
        }

        /// <summary>泳道名称：分支 DisplayName；Case 分支用匹配值（徽标已表达 CASE 序号，避免重复）</summary>
        public string LaneName
        {
            get
            {
                if (Kind != CanvasNodeKind.Lane) return string.Empty;

                if (LaneParent is CaseStep && Branch?.BranchType == BranchType.Case)
                {
                    return string.IsNullOrWhiteSpace(Branch.Expression)
                        ? Branch.StepName
                        : $"[ {Branch.Expression} ]";
                }

                return Branch?.DisplayName ?? string.Empty;
            }
        }

        /// <summary>泳道右侧计数（flow-logic：N 算子）</summary>
        public string LaneCountText => $"{Branch?.Steps.Count ?? 0} 算子";

        /// <summary>泳道底部说明条（While / For）；只用现有模型字段，不伪造运行数据</summary>
        public string LaneNoteText => LaneParent switch
        {
            WhileStep => string.IsNullOrWhiteSpace(Branch?.Expression)
                ? "条件未绑定 · 编译前必须补齐"
                : $"条件：{Branch.Expression} · 每轮执行前重新判断",
            // LoopCount 已被上游连线接管时，DefaultLoopCount 只是“未连线时的兜底值”，
            // 不能当成真实循环次数显示（否则界面在撒谎）
            ForStep forStep when forStep.LinkedSources?.ContainsKey("LoopCount") == true
                => "运行时/上游连线",
            ForStep forStep => $"循环 {forStep.DefaultLoopCount} 次 · Index 0 → {Math.Max(0, forStep.DefaultLoopCount - 1)}",
            _ => string.Empty,
        };

        public string LaneNoteGlyph => LaneParent is ForStep ? "\uf01e" : "\uf071";

        public Visibility LaneNoteVisibility
            => Kind == CanvasNodeKind.Lane && LaneParent is WhileStep or ForStep
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>类型徽标可见性（模板触发器用）</summary>
        public System.Windows.Visibility TypeBadgeVisibility
            => Kind == CanvasNodeKind.Step && string.IsNullOrEmpty(TypeBadge)
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;

        /// <summary>折叠按钮字形：展开中显示"收起"箭头，折叠中显示"展开"箭头（Icon 字体）</summary>
        public string CollapseGlyph => IsCollapsed ? "\uf077" : "\uf078";

        /// <summary>折叠后角标文案：框里藏了多少个步骤（含嵌套），由主 VM 按拓扑算好塞入</summary>
        public int CollapsedChildCount
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                    RaisePropertyChanged(nameof(CollapsedBadgeText));
            }
        }

        /// <summary>折叠角标文本；未折叠或没有子孙时为空串（不画）</summary>
        public string CollapsedBadgeText
            => Kind == CanvasNodeKind.Container && IsCollapsed && CollapsedChildCount > 0
                ? $"{CollapsedChildCount} 步"
                : string.Empty;

        /// <summary>
        /// 容器是否折叠。真值来源是 FlowLayoutStore.Collapsed（随图纸存盘），
        /// 主 VM 在渲染时同步进来；切换走主 VM 的命令（写存储后重建），此处只读展示
        /// </summary>
        public bool IsCollapsed
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                {
                    RaisePropertyChanged(nameof(CollapsedBadgeText));
                    RaisePropertyChanged(nameof(CollapseGlyph));
                    RaisePropertyChanged(nameof(Header));
                    RaisePropertyChanged(nameof(CardTooltip));
                    RaisePropertyChanged(nameof(JoinBarVisibility));
                    RaisePropertyChanged(nameof(SideConnectorMarginLeft));
                    RaisePropertyChanged(nameof(SideConnectorMarginRight));
                }
            }
        }

        /// <summary>
        /// 节点在图坐标系中的位置。
        /// 由 Nodify 的 ItemContainer 双向绑定，拖动时回写；
        /// 主 VM 订阅本属性变化后写入 FlowLayoutStore——不会递增 FlowModel.Version。
        /// 容器框的坐标是算出来的（子孙外接框），IsDraggable=false 保证了它不会被拖动污染。
        /// </summary>
        public Point Location
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>是否被选中（支持框选多选）。选中会联动工作区当前步骤（属性栏跟随）</summary>
        public bool IsSelected
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 模块级流连接器：每节点一根输入脚 + 一根输出脚（不再是端口逐条排列）。
        /// 具体绑哪对端口由绑定弹窗决定，画布连接器只承载"拖线"手势与几何锚点。
        /// </summary>
        public ObservableCollection<CanvasConnectorViewModel> Inputs { get; } = new();

        public ObservableCollection<CanvasConnectorViewModel> Outputs { get; } = new();

        /// <summary>本节点在所属步骤集合里的执行序号（1 基）。0 表示不显示（装饰节点）</summary>
        public int OrderIndex
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                    RaisePropertyChanged(nameof(OrderLabel));
            }
        }

        /// <summary>序号角标文本。空串表示模板里不画角标</summary>
        public string OrderLabel => OrderIndex > 0 ? OrderIndex.ToString() : string.Empty;

        /// <summary>
        /// 节点在流程树里的嵌套深度（顶层 = 0）。渲染时由主 VM 按拓扑回填，只用于画布叠放层次；
        /// 与 OrderIndex 一样是渲染期投影，不落盘、不进坐标库。
        /// </summary>
        public int Depth
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                    RaisePropertyChanged(nameof(ZOrder));
            }
        }

        /// <summary>
        /// 画布叠放层次（绑 Panel.ZIndex）：数值 = 深度 × 3 + 身份偏移（框 3 / 泳道 4 / 内容 5）。
        /// 同层内"容器框 &lt; 泳道 &lt; 内容节点"，跨层整体抬高——顶层框 3 &lt; 顶层泳道 4 &lt;
        /// 子层框 6 &lt; 子层泳道 7 &lt; 子层内容 8。
        ///
        /// 为什么不能只靠 Nodes 集合的加入顺序：集合序是"分支 1 全部子树 → 分支 2 泳道 → 分支 2 内容"，
        /// 后入分支的泳道会盖住先入分支里的嵌套容器框（2026-10-09 真机截图里那条半透明蓝块）。
        /// 层次只由深度与身份决定，与分支序无关。
        /// </summary>
        public int ZOrder => Math.Max(Depth, 0) * 3 + (Kind switch
        {
            CanvasNodeKind.Container => 3,
            CanvasNodeKind.Lane => 4,
            _ => 5,
        });

        // ---- 泳道 / 容器框几何：由内容外接框算出，纯展示，不参与持久化 ----

        /// <summary>泳道或容器框宽度（含内边距）</summary>
        public double LaneWidth
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>泳道或容器框高度（含头带与内边距）</summary>
        public double LaneHeight
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>分支里一个步骤都没有时，泳道显示占位提示</summary>
        public bool IsLaneEmpty
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        // ---- 必填参数角标：与编译期 FlowCompiler 的 [参数缺失] 同一判据的画布投影 ----

        /// <summary>
        /// 插件的必填输入端口名快照（主 VM 在节点创建时注入，按 PluginTypeName 解析并缓存；
        /// null = 解析不到插件类型——断言桩的假类型名 / 反射失败，此时一律不亮角标）。
        /// </summary>
        public IReadOnlyList<string>? RequiredPortNames { get; set; }

        /// <summary>
        /// 是否存在未配置的必填参数（模板据此显示黄叹号）。
        /// 判据与 FlowCompiler 必填项检查逐字对齐：IsRequired &amp;&amp; 无上游连线 &amp;&amp; 手填值为空——
        /// 界面与编译器各判一套必然出现"角标说缺、编译器放行"的分裂。
        /// </summary>
        public bool HasMissingRequired
        {
            get => field;
            private set => SetProperty(ref field, value);
        }

        /// <summary>黄叹号悬停提示：逐个列出缺失的必填端口名（空 = 不显示提示）</summary>
        public string MissingRequiredTooltip => _missingRequiredNames.Count == 0
            ? string.Empty
            : "必填参数未配置（编译会报参数缺失）：\n" + string.Join("\n", _missingRequiredNames.Select(n => $"· {n}"));

        private List<string> _missingRequiredNames = new();

        /// <summary>
        /// 重算必填参数缺失态（幂等）。调用时机：注入 RequiredPortNames 后、每次渲染
        /// （RefreshNodeState）、以及 Model 的 InputValues/LinkedSources 属性通知。
        /// </summary>
        public void RefreshMissingRequired()
        {
            _missingRequiredNames.Clear();

            if (Kind == CanvasNodeKind.Step && Model is ActionStep
                && RequiredPortNames is { Count: > 0 } required)
            {
                foreach (var name in required)
                {
                    bool hasConstant = Model.InputValues != null
                        && Model.InputValues.TryGetValue(name, out var constantValue)
                        && constantValue != null;
                    if (!Model.IsLinked(name) && !hasConstant)
                        _missingRequiredNames.Add(name);
                }
            }

            HasMissingRequired = _missingRequiredNames.Count > 0;
        }

        /// <summary>能否拖动。泳道与容器框一律钉死——框体位置由内容算出，拖了没地方存</summary>
        public bool IsDraggable => Kind == CanvasNodeKind.Step;

        /// <summary>能否被选中。泳道是背景；容器框可选中（联动属性栏的容器编辑）</summary>
        public bool IsSelectable => Kind != CanvasNodeKind.Lane;

        /// <summary>是否为纯装饰节点（不参与连线、不计入执行序）</summary>
        public bool IsDecorator => Kind == CanvasNodeKind.Lane;

        /// <summary>步骤/分支改名后刷新标题</summary>
        public void RefreshHeader()
        {
            RaisePropertyChanged(nameof(Header));
            RaisePropertyChanged(nameof(CardTooltip));
            // 渲染期重算的派生投影：分支增删/条件编辑后，即使节点从池里复用也要刷新
            RaisePropertyChanged(nameof(SubTitle));
            RaisePropertyChanged(nameof(OpBadge));
            RaisePropertyChanged(nameof(OpBadgeVisibility));
            RaisePropertyChanged(nameof(ContainerMetaText));
            RaisePropertyChanged(nameof(HasMissingCondition));
            RaisePropertyChanged(nameof(BranchCount));
            RaisePropertyChanged(nameof(DescendantCount));
            RaisePropertyChanged(nameof(JoinBarText));
            RaisePropertyChanged(nameof(JoinBarVisibility));
        }

        // ---- 算子注释：StepModel.Description 的画布投影，节点卡片下方的灰色小字 ----

        /// <summary>
        /// 算子注释原文。注意 StepModel 构造把 Description 默认置为插件名——
        /// 「用户真的写了注释」的判据是 HasUserComment（非空且 ≠ 插件名），
        /// 否则每个节点下方都会平白多出一行与副标题重复的字。
        /// </summary>
        public string Comment => Kind == CanvasNodeKind.Lane ? string.Empty : Model?.Description ?? string.Empty;

        /// <summary>用户写过的自定义注释（区别于构造期塞进来的默认值=插件名）</summary>
        public bool HasUserComment
            => Kind != CanvasNodeKind.Lane
               && !string.IsNullOrEmpty(Comment)
               && !string.Equals(Comment, Model!.PluginName, StringComparison.Ordinal);

        /// <summary>注释行可见性（仅用户自定义注释显示，节点不因默认值变高）</summary>
        public Visibility CommentVisibility
            => HasUserComment ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>卡片悬停提示：名称 / 插件名 / 操作摘要 / 自定义注释 逐行拼合，空行跳过</summary>
        public string CardTooltip
        {
            get
            {
                if (Kind == CanvasNodeKind.Lane) return string.Empty;

                var lines = new List<string> { Header };
                var pluginName = Model?.PluginName ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(pluginName))
                    lines.Add(pluginName);

                var subTitle = SubTitle;
                if (!string.IsNullOrWhiteSpace(subTitle)
                    && !string.Equals(subTitle, pluginName, StringComparison.Ordinal))
                    lines.Add(subTitle);

                if (HasUserComment) lines.Add(Comment);
                return string.Join("\n", lines);
            }
        }

        /// <summary>
        /// 节点右键菜单的命令入口：菜单是节点模板里的每实例对象，DataContext 是本 VM，
        /// 弹层够不到主 VM 的命令面——主 VM 在 GetOrCreateNode 时注入闭包命令（泳道不注入、无菜单）。
        /// </summary>
        public DelegateCommand? OpenBindingCommand { get; set; }

        /// <summary>
        /// 打开模块参数（双击节点 / 右键菜单共用同一入口，与流程栏「模块参数」同一分派）。
        /// 命令体由主 VM 注入（见 FlowCanvasViewModel.GetOrCreateNode）——泳道节点无参数面板，命中即空跑。
        /// </summary>
        public DelegateCommand? OpenModuleParametersCommand { get; set; }

        public DelegateCommand? ToggleBreakpointCommand { get; set; }

        public DelegateCommand? ToggleDisableCommand { get; set; }

        public DelegateCommand? DeleteCommand { get; set; }

        public DelegateCommand? CopyCommand { get; set; }

        /// <summary>
        /// 重命名（右键菜单，与流程栏「重命名」同一弹窗与同一门禁）。
        /// 2026-10-24 补：画布此前没有这两个入口，同一个步骤在流程栏能改名字/注释、在画布不能。
        /// </summary>
        public DelegateCommand? RenameCommand { get; set; }

        /// <summary>编辑注释（右键菜单，与流程栏「编辑注释」同一弹窗）</summary>
        public DelegateCommand? EditCommentCommand { get; set; }

        /// <summary>
        /// 分支结构动作（右键菜单「添加分支 / 添加 ElseIf / 添加 Else / 添加 Case」）：
        /// 与流程栏 AddParallelBranchCommand / ModuleActionCommand(AddElseIf|AddElse|AddCase)
        /// 走**同一批处理器**，主 VM 在 GetOrCreateNode 时注入（参数就是本节点的 Model）。
        /// 刻意不在画布侧另写一份判型/上限/确认逻辑——两处各写一份必然漂移。
        /// </summary>
        public DelegateCommand? AddBranchCommand { get; set; }

        /// <summary>If 容器追加 ElseIf 分支（与流程栏 ModuleCommandAction.AddElseIf 同一处理器）</summary>
        public DelegateCommand? AddElseIfBranchCommand { get; set; }

        /// <summary>If 容器补兜底 Else 分支（与流程栏 ModuleCommandAction.AddElse 同一处理器）</summary>
        public DelegateCommand? AddElseBranchCommand { get; set; }

        /// <summary>分支匹配容器追加 Case 分支（与流程栏 ModuleCommandAction.AddCase 同一处理器）</summary>
        public DelegateCommand? AddCaseBranchCommand { get; set; }

        /// <summary>断点菜单项可见性：只有普通步骤可挂断点（容器/泳道不参与，与 ToggleBreakpoint 的闸门同口径）</summary>
        public Visibility BreakpointMenuVisibility
            => Kind == CanvasNodeKind.Step ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>断点勾选态（右键菜单 IsChecked；随 Model.IsBreakpoint 变化转发通知）</summary>
        public bool IsBreakpoint => Model?.IsBreakpoint == true;

        /// <summary>禁用菜单项文案（随 Model.IsDisEnable 动态翻转）</summary>
        public string DisableMenuHeader => Model?.IsDisEnable == true ? "启用" : "禁用";

        /// <summary>
        /// 容器左右脚连接点的边距：纵向锚在头带中心（展开）或折叠框中心（折叠），
        /// 与主 VM 的连线锚点（EdgeMidpoint 对容器用头带高度）保持同一点——
        /// 否则框体增高后端口留在框中部、连线却从头带出，两边对不上。
        /// 横向 -6 让连接点骑在框缘上。步骤节点不使用（连接点垂直居中骑边）。
        /// </summary>
        public System.Windows.Thickness SideConnectorMarginLeft
            => Kind == CanvasNodeKind.Container
                ? new System.Windows.Thickness(-6, (IsCollapsed ? FlowCanvasViewModel.CollapsedFrameHeight : FlowCanvasViewModel.FrameHeaderHeight) / 2 - 6, 0, 0)
                : new System.Windows.Thickness(-6, 0, 0, 0);

        public System.Windows.Thickness SideConnectorMarginRight
            => Kind == CanvasNodeKind.Container
                ? new System.Windows.Thickness(0, (IsCollapsed ? FlowCanvasViewModel.CollapsedFrameHeight : FlowCanvasViewModel.FrameHeaderHeight) / 2 - 6, -6, 0)
                : new System.Windows.Thickness(0, 0, -6, 0);

        // ---- 外链角标：全局变量 / 常量绑定没有"上游模块"可画线，折成节点上的计数徽标 ----

        private readonly List<string> _externalBindings = new();

        /// <summary>全局变量/常量绑定条数（0 = 不画徽标）</summary>
        public int ExternalLinkCount => _externalBindings.Count;

        /// <summary>徽标悬停提示：逐条列出全局变量/常量绑定</summary>
        public string ExternalLinkTooltip
            => _externalBindings.Count == 0
                ? string.Empty
                : "非连线绑定：\n" + string.Join("\n", _externalBindings.Select(b => $"· {b}"));

        /// <summary>渲染前清零（BuildLinks 每轮重建计数）</summary>
        public void ResetExternalBindings()
        {
            if (_externalBindings.Count == 0) return;
            _externalBindings.Clear();
            NotifyExternalBindingsChanged();
        }

        /// <summary>登记一条全局变量/常量绑定（BuildLinks 期间调用）</summary>
        public void AddExternalBinding(string label)
        {
            _externalBindings.Add(label);
            NotifyExternalBindingsChanged();
        }

        private void NotifyExternalBindingsChanged()
        {
            RaisePropertyChanged(nameof(ExternalLinkCount));
            RaisePropertyChanged(nameof(ExternalLinkTooltip));
        }
    }
}
