using System;
using System.Collections.ObjectModel;
using System.Windows;
using VisionMaster.Models;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 画布节点的三种身份。
    ///
    /// 为什么要区分：M2 的子画布里同时存在「真正的步骤」「祖先容器的只读投影」和「分支泳道背景」，
    /// 三者长得像（都是画布上的一项）但行为规则完全不同——能不能拖、坐标存不存、要不要显示序号。
    /// 用枚举表达身份，比在 XAML 里堆一堆转换器或给每种东西建一个 VM 类要省得多：
    /// NodifyEditor 的 ItemContainerStyle 只能有一种目标类型，靠 Kind 驱动视觉差异是唯一的正解。
    /// </summary>
    public enum CanvasNodeKind
    {
        /// <summary>图纸里的真实步骤，可拖、可选、坐标入 FlowLayoutStore、显示执行序号</summary>
        Step,

        /// <summary>
        /// 层根锚点：祖先容器投影到本层的只读影子。
        /// 只暴露输出端口（本层步骤取 For.Index 之类），不可拖不可删，坐标不入库——
        /// 因为它的位置是每次进层时按本层内容算出来的，存下来反而会与本层坐标打架。
        /// </summary>
        LayerRoot,

        /// <summary>分支泳道：纯背景装饰，铺在同分支节点底下，不可拖不可选</summary>
        Lane,
    }

    /// <summary>
    /// 画布上的一个节点，对应图纸里的一个 <see cref="StepModel"/>（Step 类节点），
    /// 或一条分支泳道 / 一个层根锚点（装饰类节点）。
    ///
    /// 节点身份一律用 StepID 表达：连线写回、运行态高亮、错误定位都靠它，
    /// 不用步骤名（步骤名可重复、可被改）。
    /// </summary>
    public class CanvasNodeViewModel : BindableBase
    {
        /// <summary>真实步骤节点</summary>
        public CanvasNodeViewModel(StepModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Kind = CanvasNodeKind.Step;
        }

        /// <summary>装饰类节点（层根锚点 / 泳道）专用</summary>
        private CanvasNodeViewModel(CanvasNodeKind kind, string header, StepModel? container, StepCollection? branch)
        {
            Kind = kind;
            _header = header;
            Model = container;
            Branch = branch;
        }

        /// <summary>
        /// 造一个祖先容器的层根锚点。标题带上「层根」前缀，让操作人员一眼看出这不是本层可拖的步骤。
        /// </summary>
        public static CanvasNodeViewModel CreateLayerRoot(StepModel container, string header)
            => new(CanvasNodeKind.LayerRoot, header, container, null);

        /// <summary>
        /// 造一条分支泳道。branch 引用用于增量重建时认人（同名分支可能有多个，只有实例是唯一身份），
        /// 同时泳道标题直接绑 branch.DisplayName，分支改名即时生效，不需要手工补通知。
        /// </summary>
        public static CanvasNodeViewModel CreateLane(StepCollection branch, string title)
            => new(CanvasNodeKind.Lane, title, null, branch);

        /// <summary>背后的步骤模型；泳道节点为 null</summary>
        public StepModel? Model { get; }

        /// <summary>泳道对应的分支集合；非泳道为 null</summary>
        public StepCollection? Branch { get; }

        /// <summary>节点身份，决定模板选择与拖拽/选中规则</summary>
        public CanvasNodeKind Kind { get; }

        /// <summary>步骤唯一标识，连线的寻址依据；泳道为 Guid.Empty</summary>
        public Guid StepId => Model?.StepID ?? Guid.Empty;

        /// <summary>
        /// 节点标题。
        /// 步骤节点的名称以 <see cref="Model"/> 为唯一真相源，本类不再缓存一份——
        /// 缓存只会多出一个陈旧点（改名后忘了补通知就显示旧名），而显示刷新本来就要靠
        /// <see cref="RefreshHeader"/> 发 PropertyChanged，回读与缓存的通知成本完全一样。
        /// </summary>
        public string Header
        {
            get
            {
                // 装饰节点标题回读真相源，避免工厂传入的快照在改名后陈旧
                if (Kind == CanvasNodeKind.Lane)
                    return Branch?.DisplayName ?? _header;
                if (Kind == CanvasNodeKind.LayerRoot)
                    return Model != null ? $"层根 · {Model.StepName}" : _header;

                // 禁用步骤加后缀，与流程栏的观感保持一致
                var name = Model.StepName;
                return Model.IsDisEnable ? $"{name}（已禁用）" : name;
            }
        }

        private string _header = string.Empty;

        /// <summary>
        /// 节点在图坐标系中的位置。
        /// 由 Nodify 的 ItemContainer 双向绑定，拖动时回写；
        /// 主 VM 订阅本属性变化后写入 FlowLayoutStore——注意它不会递增 FlowModel.Version
        /// </summary>
        public Point Location
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>是否被选中（支持框选多选）</summary>
        public bool IsSelected
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public ObservableCollection<CanvasConnectorViewModel> Inputs { get; } = new();

        public ObservableCollection<CanvasConnectorViewModel> Outputs { get; } = new();

        /// <summary>是否为容器步骤（If/While/For），画布上提供进入子层的入口</summary>
        public bool IsContainer { get; set; }

        /// <summary>是否位于子画布层（非顶层），影响面包屑与返回逻辑</summary>
        public bool IsNested { get; set; }

        /// <summary>
        /// 本层本分支内的执行序号（1 基）。0 表示不显示——装饰节点没有序号可言。
        /// 「第几步执行」在这个项目里既不是 SortId 也不是坐标，而是所属步骤集合里的下标 +1，
        /// 所以序号必须由 VM 按拓扑算好后塞进来，绝不能在 XAML 里用集合下标凑。
        /// </summary>
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

        // ---- 泳道几何：由本分支内所有节点的坐标算出的外接框，纯展示用，不参与任何持久化 ----

        /// <summary>泳道宽度（含内边距）</summary>
        public double LaneWidth
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>泳道高度（含标题条与内边距）</summary>
        public double LaneHeight
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>分支里一个步骤都没有时，泳道显示占位提示</summary>
        public bool IsLaneEmpty { get; set; }

        /// <summary>能否拖动。层根与泳道一律钉死——它们的位置是算出来的，拖了也没地方存</summary>
        public bool IsDraggable => Kind == CanvasNodeKind.Step;

        /// <summary>能否被选中。泳道是背景，选中它没有任何意义</summary>
        public bool IsSelectable => Kind != CanvasNodeKind.Lane;

        /// <summary>是否为装饰节点（不参与连线写回、不计入序号）</summary>
        public bool IsDecorator => Kind != CanvasNodeKind.Step;

        /// <summary>步骤/分支改名后刷新标题（改名级联由 WorkspaceContext 负责，这里只做显示同步）</summary>
        public void RefreshHeader()
        {
            RaisePropertyChanged(nameof(Header));
        }
    }
}
