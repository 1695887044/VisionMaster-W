using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 底板图元（TypeKey = <c>Hmi.Panel</c>）：一块能把别的图元装进去的面板。
    ///
    /// <b>它解决什么问题</b>
    /// ---------
    /// 一页画面上常常有好几块功能区域（"上料站 / 灌装站 / 贴标站"），
    /// 每块都该有自己的底色、边框、标题，而且整块能一起搬动。
    /// 用矩形当底再各自摆图元做不到后两件事：矩形和图元之间没有归属关系，
    /// 拖动时得先框选一整片（还容易漏选），也没有裁剪——
    /// 区域里放不下的内容会溢出到隔壁区域去。
    ///
    /// <b>三种关系各自管什么（别混）</b>
    /// ---------
    /// · <c>Hmi.Panel</c>（本图元）＝ <b>视觉嵌套</b>：子图元画在这块画布里，被它裁剪、跟着它走；
    /// · 图元「锁定」＝ 防止误挪已摆好的东西；
    /// · 图元「分组」（组合/取消组合）＝ 编辑期关系，让点一下选中一整组、拖一下搬走一整组。
    ///
    /// 三者的分工是刻意的：底板管"画面结构"，分组管"手边的临时捆绑"。
    /// 一个底板里的图元可以跨图层（图层管渲染显隐与锁定），
    /// 一组图元也可以分散在多个底板里（虽然少见）。
    ///
    /// <b>只支持一层</b>：底板不能再装进底板（写入时被
    /// <c>ScadaPage.TrySetParent</c> 拒绝）。多层嵌套要递归遍历与环检测，
    /// 而"画面上再套一层框"用两个并排的底板 + 图层就能表达。
    ///
    /// <b>选中底板会连子图元一起选中</b>（照"点中组员即整组选中"的既有范式）。
    /// 这条决定了拖动/对齐/微调这些路径<b>一行都不用改</b>——它们本来就按选中集合批量处理，
    /// 而子图元与底板被一起选中、各写一次自己的绝对坐标，整块就跟着走了，
    /// 不需要任何"父带动子"的级联（级联与批量写入叠加会双重位移，那是本设计刻意避开的一类坑）。
    /// </summary>
    [TemplatePart(Name = PartCanvas, Type = typeof(Canvas))]
    public partial class PanelElement : ScadaElementBase, IElementContainer
    {
        /// <summary>
        /// 把默认样式指向自己。
        ///
        /// <b>这一条不能省</b>：基类 <see cref="ScadaElementBase"/> 在 Generic.xaml 里有一条隐式样式
        /// （一个居中的 ContentPresenter，见 ScadaElementBase.xaml 的注释），派生类的
        /// <c>DefaultStyleKey</c> 默认<b>继承</b>基类那一份——不覆盖的话，本图元会套上基类模板，
        /// 于是 <c>GetTemplateChild("ChildrenCanvas")</c> 永远返回 null。
        ///
        /// 后果是静默的：控件照样显示（基类模板能画）、属性照样落地、编译期全绿，
        /// 只有"子图元装不进去"与"底板底色不画"这两件事不对——
        /// 实测就是这么栽的（模板明明套上了，子图元却一直被当顶层铺着）。
        /// </summary>
        static PanelElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(PanelElement),
                new FrameworkPropertyMetadata(typeof(PanelElement)));
        }

        /// <summary>模板部件名：承载子图元的画布</summary>
        public const string PartCanvas = "ChildrenCanvas";

        /// <summary>模板部件名：标题栏所在的行</summary>
        public const string PartTitleRow = "TitleRow";

        /// <summary>承载子图元的画布（由模板提供；未套模板时为 null）</summary>
        public Canvas? ChildrenHost { get; private set; }

        /// <summary>
        /// 模板就绪前先认下来的子图元。
        ///
        /// 画布重建时子控件刚造出来、还没进可视树，模板尚未套用 → <see cref="ChildrenHost"/> 是 null。
        /// 那时不能把子图元丢掉（丢 = 它再也不会被挂进来），也不能拒绝（拒绝 = 画布把它当顶层铺开，
        /// 模型里 ParentId 全对而屏幕上没嵌套）。所以记在这里，等 OnApplyTemplate 一到补挂。
        /// </summary>
        private readonly List<ScadaElementBase> _pendingChildren = new();

        /// <summary>
        /// 子图元区相对底板左上角的偏移。
        ///
        /// 纵向恒等于标题栏高度：承载画布在 Grid 的第 1 行，而画布侧的坐标换算是
        /// "绝对坐标 − 底板坐标"，不补这一下，每个子图元都会静默下移一个标题栏高度。
        /// 横向恒为 0（标题栏通栏铺满，不占横向空间）。
        /// </summary>
        public double ChildrenOriginX => 0;

        /// <inheritdoc />
        public double ChildrenOriginY => Math.Max(0, TitleHeight);

        /// <inheritdoc />
        public void TakeChild(ScadaElementBase child)
        {
            if (child is null)
                return;

            if (ChildrenHost is { } host)
            {
                // 先摘再挂：控件此刻可能还挂在元素层（画布重建时它就是先落在那里等认领的），
                // 而 Panel.Children.Add 对"已经是别人逻辑子元素"的控件会直接抛异常。
                // WPF 只对可视父自动解绑，逻辑子元素必须自己摘。
                ScadaElementBase.DetachFromParent(child);

                if (!host.Children.Contains(child))
                    host.Children.Add(child);

                _pendingChildren.Remove(child);
                return;
            }

            if (!_pendingChildren.Contains(child))
                _pendingChildren.Add(child);
        }

        /// <inheritdoc />
        public void ReleaseChild(ScadaElementBase child)
        {
            if (child is null)
                return;

            _pendingChildren.Remove(child);
            ChildrenHost?.Children.Remove(child);
        }

        /// <summary>标题字色与内容字色分开两组属性的理由见各自的 Description</summary>
        [ScadaProperty(Key = "TitleText", DisplayName = "标题", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "", IsBindable = true,
                       Description = "显示在底板左上角的一行字（如「灌装站」）；留空则不占标题行，"
                                   + "整个底板都是子图元区")]
        public partial string? TitleText { get; set; }

        /// <summary>
        /// 标题栏高度。为 0 时不画标题栏，子图元区从顶端开始。
        ///
        /// 与「标题」是两个独立属性：标题文字可以让变量驱动（显示当前工位/批次），
        /// 而标题栏高度是版面，属设计期语言。合在一起会让"绑了变量之后标题栏高度也跟着变"。
        /// </summary>
        [ScadaProperty(Key = "TitleHeight", DisplayName = "标题栏高度", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.General, DefaultValue = "24", Min = 0, Max = 200, AffectsRender = false,
                       Description = "0 = 不画标题栏（整块都是子图元区）。有标题栏时子图元区自动从它下面开始")]
        public partial double TitleHeight { get; set; }

        /// <summary>标题栏底色</summary>
        [ScadaProperty(Key = "TitleFill", DisplayName = "标题栏底色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF2D3748", AffectsRender = false,
                       Description = "默认比底板底色深一档：标题栏与内容区靠明度分界，不必额外画一条分隔线")]
        public partial Brush? TitleFill { get; set; }

        /// <summary>标题文字色</summary>
        [ScadaProperty(Key = "TitleForeground", DisplayName = "标题文字色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FFFFFFFF", AffectsRender = false)]
        public partial Brush? TitleForeground { get; set; }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            ChildrenHost = GetTemplateChild(PartCanvas) as Canvas;
            ApplyStrokeInset(1);
            ApplyTitleInset();

            // 模板一就绪就把之前认下来的子图元补挂进来（见 TakeChild 的注释）
            if (ChildrenHost is { } host)
            {
                // 快照：DetachFromParent / Add 会动可视树，而可视树变化可能反过来触发
                // 又一次认领（嵌套容器、或画布重挂），直接遍历 _pendingChildren 会抛
                // "集合已更改，枚举器无效"。
                var pending = _pendingChildren.ToArray();
                _pendingChildren.Clear();

                foreach (var child in pending)
                {
                    ScadaElementBase.DetachFromParent(child);

                    if (!host.Children.Contains(child))
                        host.Children.Add(child);
                }
            }
        }

        /// <summary>
        /// 设计值刷新时不碰 <see cref="ChildrenHost"/> 里的东西：那是别的图元的控件，
        /// 由画布负责挂载/卸载（这里是"我的画布在哪"，不是"我管我的子图元"）。
        /// </summary>
        protected override void OnElementRefreshed()
        {
            ApplyStrokeInset(1);
            ApplyTitleInset();
        }

        partial void OnTitleHeightPropertyChanged() => ApplyTitleInset();

        /// <summary>
        /// 子图元区从标题栏下面开始（由 Grid 的两行自动完成），这里只负责把行高设对。
        ///
        /// 行高给 <c>Auto</c> 时标题栏的高度由内容撑开（空标题就塌成 0），
        /// 于是"有标题栏"这件事就由 TitleText 与 TitleHeight 共同决定；
        /// 显式给像素则是"无论有没有字都留出这条" —— 后者才是可预期的版面，
        /// 否则给底板起个标题、子图元整片往上蹿一格。
        ///
        /// <see cref="ChildrenOriginY"/> 同步跟着 TitleHeight：
        /// 它是画布侧换算子图元坐标时要减掉的偏移，与这里设的行高是同一条事实的两面，
        /// 所以只在这一处改。
        /// </summary>
        private void ApplyTitleInset()
        {
            if (GetTemplateChild(PartTitleRow) is RowDefinition row)
                row.Height = new GridLength(Math.Max(0, TitleHeight));

            // 标题栏与标题一起隐显：TitleHeight = 0 时连边框都不该露出来
            var visible = TitleHeight > 0;
            SetPartVisible("TitleBar", visible);
            SetPartVisible("TitleLabel", visible);

            // 子画布的 ClipToBounds 之外还要跟一次：行高变了对已挂进去的子控件没有影响
            // （它们的位置由 RelativeOffset 决定），但画布本身要重排一次才能把新行高坐实。
            ChildrenHost?.InvalidateArrange();
        }
    }
}
