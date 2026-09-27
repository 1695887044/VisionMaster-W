using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
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
        }

        /// <summary>泳道装饰节点专用</summary>
        private CanvasNodeViewModel(StepCollection branch)
        {
            Kind = CanvasNodeKind.Lane;
            Branch = branch;
        }

        /// <summary>造一条分支泳道。branch 引用用于认人（同名分支可能有多个，实例才是唯一身份），
        /// 泳道标题绑 branch.DisplayName，分支改名即时生效</summary>
        public static CanvasNodeViewModel CreateLane(StepCollection branch) => new(branch);

        /// <summary>背后的步骤模型；泳道为 null</summary>
        public StepModel? Model { get; }

        /// <summary>泳道对应的分支集合；非泳道为 null</summary>
        public StepCollection? Branch { get; }

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

        /// <summary>副标题：步骤的插件名（真实步骤）；泳道为空</summary>
        public string SubTitle => Kind == CanvasNodeKind.Step ? Model?.PluginName ?? string.Empty : string.Empty;

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

        /// <summary>容器类型徽标（If / While / For）；非容器为空串</summary>
        public string TypeBadge => Model switch
        {
            // WhileStep 继承 ConditionStep，必须先判 While
            WhileStep => "While",
            ConditionStep => "If",
            ForStep => "For",
            _ => string.Empty,
        };

        /// <summary>类型徽标可见性（模板触发器用）</summary>
        public System.Windows.Visibility TypeBadgeVisibility
            => string.IsNullOrEmpty(TypeBadge)
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
        public bool IsLaneEmpty { get; set; }

        /// <summary>能否拖动。泳道与容器框一律钉死——框体位置由内容算出，拖了没地方存</summary>
        public bool IsDraggable => Kind == CanvasNodeKind.Step;

        /// <summary>能否被选中。泳道是背景；容器框可选中（联动属性栏的容器编辑）</summary>
        public bool IsSelectable => Kind != CanvasNodeKind.Lane;

        /// <summary>是否为纯装饰节点（不参与连线、不计入执行序）</summary>
        public bool IsDecorator => Kind == CanvasNodeKind.Lane;

        /// <summary>步骤/分支改名后刷新标题</summary>
        public void RefreshHeader() => RaisePropertyChanged(nameof(Header));

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
