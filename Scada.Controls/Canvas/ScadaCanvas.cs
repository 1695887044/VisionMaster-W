using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// SCADA 画布宿主：把一袋 <see cref="ScadaElement"/> 画成可编辑的画面，
    /// 并负责<b>缩放 / 平移 / 选中 / 拖动 / 改尺寸</b>这五件编辑器必需的事。
    ///
    /// 坐标系统（后面所有换算都以此为准，改交互代码前先读这段）：
    ///
    /// <code>
    ///   设计坐标（model）  ──×Zoom +Offset──▶  视口坐标（viewport，= 本控件的客户端坐标）
    /// </code>
    ///
    /// 画面尺寸是<b>设计像素</b>（<see cref="ScadaPage.Width"/>），永远不变；
    /// Zoom/Offset 只是"取景器"，所以缩放平移<b>不</b>回写任何模型数据——
    /// 这条纪律让"看多大"与"东西在哪"彻底分离，运行态与编辑态才能共用一份画面。
    ///
    /// 为什么不用 ItemsControl 生成容器：
    /// WPF 的 ItemsControl 只允许覆写 <c>GetContainerForItemOverride()</c>（<b>拿不到 item</b>），
    /// 而"按 TypeKey 造不同控件"恰恰需要知道 item 是谁。硬套只有两条路：
    /// ① 用 ContentPresenter 当容器、把 Canvas.Left/Top/旋转绑到容器上——等于把
    ///   <see cref="ScadaElementBase.ApplyGeometry"/> 已经写好的那套几何落地再抄一遍，两处真相；
    /// ② 套一层自定义容器控件——多一层可视树、多一次内容替换，且容器复用时容易残留上一件的属性。
    /// 自己同步元素层（增/删/换/清空）只有几十行，却保住了"几何只由控件自己落地"这一条，
    /// 代价是没有容器虚拟化——画布类编辑器本来就要一次性铺整幅画面，虚拟化的收益在这里不成立。
    ///
    /// 订阅纪律与 S2 一致：只订阅"<see cref="SelectedElement"/> 非空 <b>且</b> 已挂载"这一个条件，
    /// 摘挂全走 <see cref="SyncSelectedSubscription"/>，不留第二个漏摘点。
    /// </summary>
    public partial class ScadaCanvas : Control
    {
        /// <summary>图元最小宽/高（防止拖到 0 之后再也点不中）</summary>
        private const double MinElementWidth = 8;
        private const double MinElementHeight = 8;

        /// <summary>缩放上下界（0.1 → 8 倍，够看完全厂总图与单个按钮细节）</summary>
        private const double MinZoom = 0.1;
        private const double MaxZoom = 8;

        /// <summary>每多少条细线一条粗线（主网格）</summary>
        private const int MajorGridEvery = 10;

        /// <summary>选中手柄在设计坐标下的边长基准（实际值除以 Zoom，保证屏幕上恒为 7px）</summary>
        private const double ThumbDesignSize = 7;

        // ===== 模板部件（OnApplyTemplate 里取，取不到就是 null，全部使用点都判空） =====

        private Border? _surface;
        private Rectangle? _gridLayer;
        private Canvas? _elementLayer;
        private Canvas? _selectionLayer;
        private Canvas? _diagnosticLayer;

        private readonly ScaleTransform _scale = new();
        private readonly TranslateTransform _translate = new();

        // 网格画笔刻意<b>不</b> Freeze：线宽要随缩放补偿成 1/Zoom，
        // 冻结了就只能整块画刷重建（缩放时每帧 new 一个 DrawingBrush，纯属白给）。
        private Pen? _minorPen;
        private Pen? _majorPen;

        private Canvas? _selectionBox;
        private Rectangle? _selectionRect;
        private Rectangle[]? _thumbs;
        private RotateTransform? _selectionRotation;

        /// <summary>当前挂着变更订阅的选中图元（与 SelectedElement 的当前值可能不同，见 SyncSelectedSubscription）</summary>
        private ScadaElement? _subscribedSelected;

        /// <summary>
        /// 当前挂着订阅的图层（来自 <see cref="Page"/> 的 Layers）。
        ///
        /// 为什么要有这张表而不是照事件参数挂/摘：与 <c>ScadaPage._subscribedLayers</c> 同一个理由——
        /// <c>Layers.Clear()</c> 走 Reset 分支且 OldItems 为 null，拿参数摘就会漏，
        /// 漏了就是"删掉的图层仍然钉着画布"。有登记表，挂摘都幂等。
        ///
        /// 生命周期口径跟着 <see cref="ItemsSource"/> 走（赋值时挂、换值/换画面时摘），
        /// <b>不</b>跟着 Loaded/Unloaded 走：画布一旦被赋上 <c>page.Elements</c>，就已经被这个画面钉住了，
        /// Layers 只是同一个持有者的第二条订阅。离树时摘掉它并不会让画布更早回收，
        /// 只会让它错过"隐藏图层 → 该重算"的通知（离树期间改了可见性，回来就是旧样子）。
        /// 只有 <c>SelectedElement</c> 那条走 Loaded/Unloaded，因为它挂在图元对象上、随点选随时来一条，
        /// 是"外部对象钉住画布"里唯一需要按挂载期收口的。
        /// </summary>
        private readonly HashSet<ScadaLayer> _subscribedLayers = new();

        static ScadaCanvas()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ScadaCanvas), new FrameworkPropertyMetadata(typeof(ScadaCanvas)));
        }

        public ScadaCanvas()
        {
            // 图元按钮（Hmi.Button）在编辑态不该真响应点击：编辑期点它=选中，运行期点它=触发。
            // IsReadOnly 是这里唯一的开关，不引入第二套"设计模式"状态机。
            Focusable = true;

            Diagnostics = new ScadaDiagnosticOverlay(this);
        }

        #region 依赖属性

        /// <summary>图元来源（一般直接绑 <see cref="ScadaPage.Elements"/>）。支持增删/替换/清空/移动</summary>
        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            nameof(ItemsSource), typeof(IEnumerable), typeof(ScadaCanvas),
            new FrameworkPropertyMetadata(null, OnItemsSourceChanged));

        /// <summary>画面设计宽（落盘数据的只读镜像，仅用于布局画面尺寸；画布不写回）</summary>
        public static readonly DependencyProperty PageWidthProperty = DependencyProperty.Register(
            nameof(PageWidth), typeof(double), typeof(ScadaCanvas),
            new FrameworkPropertyMetadata(1920d, OnPageSizeChanged));

        /// <summary>画面设计高</summary>
        public static readonly DependencyProperty PageHeightProperty = DependencyProperty.Register(
            nameof(PageHeight), typeof(double), typeof(ScadaCanvas),
            new FrameworkPropertyMetadata(1080d, OnPageSizeChanged));

        /// <summary>画面底色（来自 <see cref="ScadaPage.Background"/>，由宿主换算成画刷后传入）</summary>
        public static readonly DependencyProperty PageBackgroundProperty = DependencyProperty.Register(
            nameof(PageBackground), typeof(Brush), typeof(ScadaCanvas),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E))));

        /// <summary>是否显示设计网格</summary>
        public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
            nameof(ShowGrid), typeof(bool), typeof(ScadaCanvas),
            new FrameworkPropertyMetadata(true, OnGridAppearanceChanged));

        /// <summary>网格间距（设计像素）</summary>
        public static readonly DependencyProperty GridSizeProperty = DependencyProperty.Register(
            nameof(GridSize), typeof(double), typeof(ScadaCanvas),
            new FrameworkPropertyMetadata(10d, OnGridAppearanceChanged));

        /// <summary>细网格线色（主网格线用同色但更粗一档，省一个属性）</summary>
        public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
            nameof(GridBrush), typeof(Brush), typeof(ScadaCanvas),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), OnGridAppearanceChanged));

        /// <summary>拖动是否吸附网格</summary>
        public static readonly DependencyProperty SnapToGridProperty = DependencyProperty.Register(
            nameof(SnapToGrid), typeof(bool), typeof(ScadaCanvas),
            new FrameworkPropertyMetadata(true));

        /// <summary>缩放倍率（1 = 100%）。写入会被夹到 [<see cref="MinZoom"/>, <see cref="MaxZoom"/>]</summary>
        public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
            nameof(Zoom), typeof(double), typeof(ScadaCanvas),
            new FrameworkPropertyMetadata(1d, OnZoomChanged, CoerceZoom));

        /// <summary>视口平移量（屏幕像素）。只是取景器状态，<b>不</b>落盘</summary>
        public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
            nameof(Offset), typeof(Point), typeof(ScadaCanvas),
            new FrameworkPropertyMetadata(default(Point), OnOffsetChanged));

        /// <summary>当前选中图元（默认双向：宿主的视图模型用 <c>{Binding SelectedElement}</c> 就能双向同步）</summary>
        public static readonly DependencyProperty SelectedElementProperty = DependencyProperty.Register(
            nameof(SelectedElement), typeof(ScadaElement), typeof(ScadaCanvas),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedElementChanged));

        /// <summary>只读态（运行预览）：不吃选中、不能拖动改尺寸，图元自己的交互照常工作</summary>
        public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
            nameof(IsReadOnly), typeof(bool), typeof(ScadaCanvas),
            new FrameworkPropertyMetadata(false, OnIsReadOnlyChanged));

        /// <summary>
        /// 画面上下文（图层归属的判定源）。
        ///
        /// 为什么要把 <see cref="ScadaPage"/> 交给画布：图层可见/锁定的唯一口径写在
        /// <see cref="ScadaPage.IsElementVisible"/> / <see cref="ScadaPage.IsElementEditable"/> 上
        /// （图元只带一个 <see cref="ScadaElement.LayerId"/>，光看它判断不出什么），
        /// 而画布原先只有 <see cref="ItemsSource"/> —— 一堆图元、没有它们属于哪个画面，
        /// 于是图层规则在渲染侧根本无从落地。
        ///
        /// 边界：画布<b>只读</b>这个对象，永远不写它（不改图层集合、不改任何图元属性）。
        /// 不传（null）= 图层规则完全不参与，画布退化成"ItemsSource 的可视化"——
        /// 这样 S2 那些不带画面的控件用法与既有断言行为一字不变。
        /// </summary>
        public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
            nameof(Page), typeof(ScadaPage), typeof(ScadaCanvas),
            new FrameworkPropertyMetadata(null, OnPageChanged));

        /// <summary>图元来源集合</summary>
        public IEnumerable? ItemsSource
        {
            get => (IEnumerable?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public double PageWidth
        {
            get => (double)GetValue(PageWidthProperty);
            set => SetValue(PageWidthProperty, value);
        }

        public double PageHeight
        {
            get => (double)GetValue(PageHeightProperty);
            set => SetValue(PageHeightProperty, value);
        }

        public Brush? PageBackground
        {
            get => (Brush?)GetValue(PageBackgroundProperty);
            set => SetValue(PageBackgroundProperty, value);
        }

        public bool ShowGrid
        {
            get => (bool)GetValue(ShowGridProperty);
            set => SetValue(ShowGridProperty, value);
        }

        public double GridSize
        {
            get => (double)GetValue(GridSizeProperty);
            set => SetValue(GridSizeProperty, value);
        }

        public Brush? GridBrush
        {
            get => (Brush?)GetValue(GridBrushProperty);
            set => SetValue(GridBrushProperty, value);
        }

        public bool SnapToGrid
        {
            get => (bool)GetValue(SnapToGridProperty);
            set => SetValue(SnapToGridProperty, value);
        }

        public double Zoom
        {
            get => (double)GetValue(ZoomProperty);
            set => SetValue(ZoomProperty, value);
        }

        public Point Offset
        {
            get => (Point)GetValue(OffsetProperty);
            set => SetValue(OffsetProperty, value);
        }

        public ScadaElement? SelectedElement
        {
            get => (ScadaElement?)GetValue(SelectedElementProperty);
            set => SetValue(SelectedElementProperty, value);
        }

        public bool IsReadOnly
        {
            get => (bool)GetValue(IsReadOnlyProperty);
            set => SetValue(IsReadOnlyProperty, value);
        }

        /// <summary>画面上下文（只读；null = 图层规则不参与渲染与交互）</summary>
        public ScadaPage? Page
        {
            get => (ScadaPage?)GetValue(PageProperty);
            set => SetValue(PageProperty, value);
        }

        private static void OnPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ScadaCanvas)d).OnPageChangedCore((ScadaPage?)e.OldValue, (ScadaPage?)e.NewValue);

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ScadaCanvas)d).OnItemsSourceChangedCore((IEnumerable?)e.OldValue, (IEnumerable?)e.NewValue);

        private static void OnPageSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ScadaCanvas)d).ApplyPageSize();

        private static void OnGridAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ScadaCanvas)d).RebuildGrid();

        private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = (ScadaCanvas)d;
            canvas.ApplyTransform();
            canvas.RebuildGrid();       // 线宽按 1/Zoom 补偿，缩放必须重算一次
            canvas.UpdateSelectionVisual();
        }

        private static void OnOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ScadaCanvas)d).ApplyTransform();

        private static void OnSelectedElementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = (ScadaCanvas)d;
            canvas.SyncSelectedSubscription();
            canvas.UpdateSelectionVisual();
        }

        private static void OnIsReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = (ScadaCanvas)d;
            canvas.CancelPendingPress();
            canvas.UpdateSelectionVisual();
            canvas.UpdateCursorState();
        }

        private static object CoerceZoom(DependencyObject d, object baseValue)
        {
            double zoom = baseValue is double z ? z : MinZoom;

            // NaN 一律回落 1：绑定源给了个算坏的值（比如 0/0 出来的 zoom）时，
            // 让画面正常显示比把 NaN 传进变换矩阵强（矩阵一旦 NaN，整棵子树直接消失且无提示）。
            if (double.IsNaN(zoom) || double.IsInfinity(zoom))
                return 1d;

            return Math.Clamp(zoom, MinZoom, MaxZoom);
        }

        #endregion

        #region 对外 API（宿主/工具箱要用）

        /// <summary>视口坐标（本控件客户端坐标）→ 设计坐标。工具箱拖放落点、交互换算都走这里</summary>
        public Point ToModelPoint(Point viewportPoint)
        {
            double zoom = Zoom;
            var offset = Offset;

            return new Point((viewportPoint.X - offset.X) / zoom, (viewportPoint.Y - offset.Y) / zoom);
        }

        /// <summary>设计坐标 → 视口坐标（把选中图元滚进视野、放高亮标记用）</summary>
        public Point ToViewportPoint(Point modelPoint)
        {
            double zoom = Zoom;
            var offset = Offset;

            return new Point(modelPoint.X * zoom + offset.X, modelPoint.Y * zoom + offset.Y);
        }

        /// <summary>按倍率缩放，并保持 <paramref name="anchorViewport"/> 这一点在屏幕上不动（滚轮缩放到光标）</summary>
        public void ZoomAt(double newZoom, Point anchorViewport)
        {
            double old = Zoom;
            if (Math.Abs(old - newZoom) < 1e-6)
                return;

            // 先算出锚点下的设计坐标，换倍率后把它放回原处——顺序反了画面会"跑偏"
            var anchorModel = ToModelPoint(anchorViewport);

            SetCurrentValue(ZoomProperty, newZoom);

            double applied = Zoom; // 可能被 CoerceZoom 夹过，用实际生效值回算
            SetCurrentValue(OffsetProperty, new Point(
                anchorViewport.X - anchorModel.X * applied,
                anchorViewport.Y - anchorModel.Y * applied));
        }

        /// <summary>以画布中心为锚缩放一档（工具栏 +/− 用；正数放大）</summary>
        public void ZoomBy(double factor)
            => ZoomAt(Zoom * factor, new Point(ActualWidth / 2, ActualHeight / 2));

        /// <summary>适应窗口：整幅画面等比放进视口，留 24px 呼吸边</summary>
        public void FitToScreen()
        {
            double vw = ActualWidth, vh = ActualHeight;
            double pw = PageWidth, ph = PageHeight;

            if (vw <= 0 || vh <= 0 || pw <= 0 || ph <= 0)
                return; // 还没布局（面板刚切出来）：此时缩放没有意义，交给下次调用

            double fit = Math.Min((vw - 48) / pw, (vh - 48) / ph);

            SetCurrentValue(ZoomProperty, Math.Clamp(fit, MinZoom, MaxZoom));
            CenterPage();
        }

        /// <summary>回到 100% 并居中画面（"实际大小"按钮）</summary>
        public void ZoomToActualSize()
        {
            SetCurrentValue(ZoomProperty, 1d);
            CenterPage();
        }

        /// <summary>
        /// 指定设计坐标进视口（图层列表点一个图元、报警定位到画面上的控件时用）。
        /// 只保证该点可见，不改变缩放。
        /// </summary>
        public void BringModelPointIntoView(Point modelPoint)
        {
            double vw = ActualWidth, vh = ActualHeight;
            if (vw <= 0 || vh <= 0)
                return;

            var viewport = ToViewportPoint(modelPoint);
            double dx = 0, dy = 0;

            const int margin = 40;

            if (viewport.X < margin) dx = margin - viewport.X;
            else if (viewport.X > vw - margin) dx = vw - margin - viewport.X;

            if (viewport.Y < margin) dy = margin - viewport.Y;
            else if (viewport.Y > vh - margin) dy = vh - margin - viewport.Y;

            if (dx != 0 || dy != 0)
                SetCurrentValue(OffsetProperty, new Point(Offset.X + dx, Offset.Y + dy));
        }

        /// <summary>
        /// 工具箱落点（视口坐标）→ 新图元左上角的设计坐标。
        ///
        /// 三件事在这一处一次做完，宿主只管调：
        /// ① 视口→设计换算；② 以落点为图元<b>中心</b>——用户指着"这儿"，要的是图元出现在这儿，
        ///    不是它的左上角跑到这儿；③ 把左上角吸附到网格上。
        ///
        /// 吸附刻意复用 <see cref="GridStep"/> 与拖动同一条规则，而不是让宿主自己按 GridSize 算：
        /// 两处各写一遍，迟早出现"拖过去压线、放下去差半格"，网格就成了摆设。
        ///
        /// 中心还会被夹进画面内（<see cref="PageWidth"/> × <see cref="PageHeight"/>）：
        /// 适应窗口时视口比画面大，用户很自然会在画面外的灰边上松手。不夹的话图元生在画面外，
        /// 表现为"我明明拖了个矩形，怎么没出来"——它出来了，只是在看不见的地方。
        /// </summary>
        public Point ToDropOrigin(Point viewportPoint, double width, double height)
        {
            var center = ToModelPoint(viewportPoint);

            double pageW = PageWidth, pageH = PageHeight;
            if (pageW > 0) center.X = Math.Clamp(center.X, 0, pageW);
            if (pageH > 0) center.Y = Math.Clamp(center.Y, 0, pageH);

            double left = center.X - width / 2;
            double top = center.Y - height / 2;

            if (!SnapToGrid)
                return new Point(left, top);

            double step = GridStep;
            if (step <= 0)
                return new Point(left, top);

            return new Point(Math.Round(left / step) * step, Math.Round(top / step) * step);
        }

        /// <summary>当前元素层里的图元控件数（断言与"渲染是否跟上了数据"的自检用）</summary>
        public int RenderedElementCount => _elementLayer?.Children.Count ?? 0;

        /// <summary>
        /// 运行态诊断角标层（S6）：宿主/绑定器用它标记"哪个图元没接上、哪个值转不过去"。
        /// 实例在构造期就建好，模板部件缺席时 Set/Clear 自行降级为无操作，调用方不必判空。
        /// </summary>
        public ScadaDiagnosticOverlay Diagnostics { get; }

        /// <summary>诊断角标层部件（<see cref="ScadaDiagnosticOverlay"/> 内部用；自定义模板缺此部件时为 null）</summary>
        internal Canvas? DiagnosticLayer => _diagnosticLayer;

        /// <summary>
        /// 枚举当前已渲染的图元控件。
        ///
        /// 运行态建表（S6）靠它一次性建"图元模型 → 控件"的反查索引：
        /// 绑定表是按 ElementId 写的，而刷值必须落到控件上，中间就差这一次反查。
        /// 类型未注册的图元渲染成占位框（不是 <see cref="ScadaElementBase"/>），自然不会出现在这里——
        /// 这正是想要的：占位框不跟随模型刷新，往它身上写值没有意义。
        /// </summary>
        public IEnumerable<ScadaElementBase> EnumerateControls()
        {
            if (_elementLayer == null)
                yield break;

            foreach (var child in _elementLayer.Children)
            {
                if (child is ScadaElementBase control)
                    yield return control;
            }
        }

        #endregion

        #region 模板套用与几何应用

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _surface = GetTemplateChild("PART_Surface") as Border;
            _gridLayer = GetTemplateChild("PART_GridLayer") as Rectangle;
            _elementLayer = GetTemplateChild("PART_ElementLayer") as Canvas;
            _selectionLayer = GetTemplateChild("PART_SelectionLayer") as Canvas;
            _diagnosticLayer = GetTemplateChild("PART_DiagnosticLayer") as Canvas;

            // 变换实例只造一次，之后只改数值：每次缩放 new 一个 TransformGroup
            // 会让整棵子树的渲染缓存重来一遍（画面越大越明显）。
            if (_surface != null)
            {
                _surface.RenderTransformOrigin = new Point(0, 0);
                _surface.RenderTransform = new TransformGroup { Children = { _scale, _translate } };
            }

            ApplyTransform();
            ApplyPageSize();
            RebuildGrid();
            RebuildElements();
            EnsureSelectionVisual();
            SyncSelectedSubscription();
            UpdateSelectionVisual();
            UpdateCursorState();
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            // 与 ScadaElementBase 同一套纪律：只在挂载期订阅，离树摘干净（否则文档对象钉住画布=泄漏）
            Loaded += (_, _) =>
            {
                SyncSelectedSubscription();
                UpdateSelectionVisual();
            };
            Unloaded += (_, _) => SyncSelectedSubscription();
        }

        private void ApplyTransform()
        {
            _scale.ScaleX = Zoom;
            _scale.ScaleY = Zoom;
            _translate.X = Offset.X;
            _translate.Y = Offset.Y;
        }

        private void ApplyPageSize()
        {
            if (_surface == null)
                return;

            // 负数/0 会让画面变成"看不见的取景框"，这里夹成最小 1px：
            // 落盘数据被手工改坏时，宁可显示成一小块，也不要整幅画面空白且无从解释。
            _surface.Width = Math.Max(1, PageWidth);
            _surface.Height = Math.Max(1, PageHeight);

            if (_gridLayer != null)
            {
                _gridLayer.Width = _surface.Width;
                _gridLayer.Height = _surface.Height;
            }
        }

        /// <summary>把画面中心摆到视口中心（首次显示、适应窗口后调用）</summary>
        private void CenterPage()
        {
            double zoom = Zoom;

            SetCurrentValue(OffsetProperty, new Point(
                CenterAxis(ActualWidth, PageWidth * zoom),
                CenterAxis(ActualHeight, PageHeight * zoom)));
        }

        /// <summary>
        /// 单轴取景：画面放得下就居中，放不下就贴视口左上角（返回 0）。
        ///
        /// 为什么不能无条件套 (视口 - 画面) / 2：画面比视口大时它是<b>负数</b>，
        /// 等价于把画面左上角推到视口左上角之外。组态画面的图元习惯从左上区开始摆放，
        /// 于是整幅画面被推出可视区、只剩贴着视口左边缘的一条窄带——用户看到的就是
        /// "画布空白、图元不见了"（1920×1080 的画面配 ~1560×800 的编辑区必现）。
        /// 而 _centeredOnce 是单向闩锁，一旦写坏就再也不会被纠正。
        /// 放不下时贴左上，是唯一可预期的取景：1:1 就该从 (0,0) 开始看。
        /// </summary>
        private static double CenterAxis(double viewport, double page)
        {
            double slack = viewport - page;
            return slack > 0 ? slack / 2 : 0;
        }

        /// <summary>视口尺寸变了也要重新居中吗？——不。用户手动平移过之后重排窗口不该把画面弹回去，</summary>
        /// <remarks>这里只在首次拿到非零尺寸时居中一次（见 _centeredOnce）。</remarks>
        private bool _centeredOnce;

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            if (!_centeredOnce && sizeInfo.NewSize.Width > 0 && sizeInfo.NewSize.Height > 0)
            {
                _centeredOnce = true;
                CenterPage();
            }

            UpdateSelectionVisual(); // 手柄尺寸与视口无关，但适应窗口按钮的可用性/居中会随尺寸变化
        }

        #endregion

        #region 元素层同步（ItemsSource ↔ 可视子元素）

        private void OnItemsSourceChangedCore(IEnumerable? oldSource, IEnumerable? newSource)
        {
            if (oldSource is INotifyCollectionChanged oldINpc)
                oldINpc.CollectionChanged -= OnElementsCollectionChanged;

            RebuildElements();

            if (newSource is INotifyCollectionChanged newINpc)
                newINpc.CollectionChanged += OnElementsCollectionChanged;
        }

        /// <summary>
        /// 集合变更 → 元素层同步。
        ///
        /// 为什么 Add 用"顺序追加 + 末尾校正"而不按 NewStartingIndex 插入：
        /// Canvas 的视觉次序由 Panel.ZIndex 决定（控件自己从模型落地），
        /// 子元素次序只影响同 ZIndex 时的先后。按索引插入要处理"索引比 Children.Count 大"
        /// 的越界（数据集合与可视集合在 Reset 之后可能短暂不同步），收益极小、风险实在。
        /// </summary>
        private void OnElementsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_elementLayer == null)
                return;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    foreach (var item in Enumerate(e.NewItems))
                        _elementLayer.Children.Add(CreateContainer(item));
                    break;

                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Replace:
                    foreach (var item in Enumerate(e.OldItems))
                        RemoveContainerFor(item);
                    if (e.Action == NotifyCollectionChangedAction.Replace)
                    {
                        foreach (var item in Enumerate(e.NewItems))
                            _elementLayer.Children.Add(CreateContainer(item));
                    }
                    break;

                default:
                    // Reset（Clear() / 整体替换）与 Move：全量重建最省事也最不会错
                    RebuildElements();
                    break;
            }

            // 选中项可能随 Remove 一起没了：这里收口，避免选中框钉在一个已删除的图元上
            if (SelectedElement != null && !Contains(SelectedElement))
                SetCurrentValue(SelectedElementProperty, null);

            UpdateSelectionVisual();
        }

        private void RebuildElements()
        {
            if (_elementLayer == null)
                return;

            // 控件全被换掉，角标上绑的是旧控件的几何，留着就是指向空气的标记
            Diagnostics.Clear();

            _elementLayer.Children.Clear();

            foreach (var element in EnumerateItems())
                _elementLayer.Children.Add(CreateContainer(element));
        }

        private static IEnumerable<ScadaElement> Enumerate(IList? items)
        {
            if (items == null)
                yield break;

            foreach (var item in items)
            {
                if (item is ScadaElement element)
                    yield return element;
            }
        }

        private IEnumerable<ScadaElement> EnumerateItems()
        {
            if (ItemsSource is not { } source)
                yield break;

            foreach (var item in source)
            {
                if (item is ScadaElement element)
                    yield return element;
            }
        }

        private bool Contains(ScadaElement element)
        {
            foreach (var item in EnumerateItems())
            {
                if (ReferenceEquals(item, element))
                    return true;
            }

            return false;
        }

        private void RemoveContainerFor(ScadaElement element)
        {
            if (_elementLayer == null)
                return;

            var container = FindContainer(element);

            if (container != null)
                _elementLayer.Children.Remove(container); // 一个图元只应有一个容器；找到就收工
        }

        /// <summary>
        /// 找到承载某图元的控件（按<b>模型引用</b>比对，不是按 ElementId 查表）。
        ///
        /// 为什么用引用而不是 Id：Id 要遍历一遍模型再查索引，引用一次比较就够；
        /// 而"同一个模型被渲染两次"本来就是不成立的状态，用引用比对反而能把它暴露出来。
        /// </summary>
        private ScadaElementBase? FindContainer(ScadaElement element)
        {
            if (_elementLayer == null)
                return null;

            foreach (var child in _elementLayer.Children)
            {
                if (child is ScadaElementBase control && ReferenceEquals(control.Element, element))
                    return control;
            }

            return null;
        }

        /// <summary>
        /// 该图元当前是否被渲染成了可编辑控件。
        /// 类型未注册的图元渲染成占位框（不是 <see cref="ScadaElementBase"/>，也不跟随模型刷新），
        /// 所以它<b>不</b>算已渲染——也就是"未知图元不画选中框、不可选中"。
        /// 让它可编辑会得到一个更糟的结果：拖动时模型改了而占位框不动，选中框与实际位置分家。
        /// </summary>
        private bool IsRendered(ScadaElement element) => FindContainer(element) != null;

        /// <summary>
        /// 造承载控件。类型键未注册时画"未知图元"占位框而不是抛异常——
        /// 低版本软件打开高版本 .vms 必须还能看见画面结构（S2 定的降级口径落在这里）。
        ///
        /// 为什么从 static 改成实例方法：容器光"按类型键造对应控件"时确实不需要实例状态；
        /// 有了图层之后，造完还得把<b>本画布</b>的可见性判定注入控件（判定源是实例的 <see cref="Page"/>），
        /// 静态方法拿不到。三个调用点（Add / Replace / Rebuild）本来就是实例方法，签名改动不外溢。
        /// </summary>
        private UIElement CreateContainer(ScadaElement element)
        {
            var container = ElementRegistry.IsRegistered(element.TypeKey)
                ? (UIElement)ElementRegistry.CreateControl(element)
                : CreateUnknownPlaceholder(element);

            if (container is ScadaElementBase control)
                control.LayerVisibilityResolver = IsElementShown;

            // 新建/复用的容器必须当场定一次可见性：它可能落在一个正被隐藏的图层上
            container.Visibility = IsElementShown(element) ? Visibility.Visible : Visibility.Collapsed;

            return container;
        }

        private static readonly Brush UnknownFill = ScadaBrushes.Frozen(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        private static readonly Brush UnknownStroke = ScadaBrushes.Frozen(Color.FromRgb(0xC0, 0x60, 0x60));
        private static readonly Brush UnknownText = ScadaBrushes.Frozen(Color.FromRgb(0xD0, 0xD0, 0xD0));

        private static FrameworkElement CreateUnknownPlaceholder(ScadaElement element)
        {
            var box = new Border
            {
                Width = Math.Max(1, element.Width),
                Height = Math.Max(1, element.Height),
                Background = UnknownFill,
                BorderBrush = UnknownStroke,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = $"未知图元：{element.TypeKey}",
                    Foreground = UnknownText,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            // 占位框不是 ScadaElementBase，没人替它落地几何，这三行就是它的 ApplyGeometry
            Canvas.SetLeft(box, element.X);
            Canvas.SetTop(box, element.Y);
            Panel.SetZIndex(box, element.ZIndex);

            // Tag 是"这个占位框代表哪个图元"的反查凭据：占位框没有 Element 依赖属性，
            // 而图层开关翻上来时（ApplyLayerVisibility）必须知道该按哪个图元的归属判可见性。
            // 选中手柄也用 Tag 装东西（那里装的是 Vector），两者在同一层集合里不冲突。
            box.Tag = element;

            return box;
        }

        #endregion

        #region 图层上下文（可见性 / 可编辑性）

        /// <summary>
        /// 画面换人：把图层订阅整体搬到新画面上，再全量重算一次可见性。
        /// </summary>
        private void OnPageChangedCore(ScadaPage? oldPage, ScadaPage? newPage)
        {
            CancelPendingPress(); // 换画面 = 原来那次按下彻底作废

            if (oldPage != null)
            {
                oldPage.PropertyChanged -= OnPagePropertyChanged;
                DetachLayers(oldPage);
            }

            if (newPage != null)
            {
                newPage.PropertyChanged += OnPagePropertyChanged;
                AttachLayers(newPage);
            }

            ApplyLayerVisibility();
        }

        private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ScadaPage.Layers))
                return; // 画面的其它属性（宽高等）与图层判定无关

            if (sender is not ScadaPage page)
                return;

            // 整个图层集合被换成一个新实例（落盘的反序列化走 setter 就是这个形状）。
            // 注意这里摘不到"旧的那个集合"——事件回调拿到手时 page.Layers 已经是新的了。
            // 不纠结：旧集合此刻只被它自己那条挂到本画布的委托引用，两者一起变成不可达、一起回收，
            // 不存在"画布被一份没人用的旧数据钉住"的泄漏。真要接"原地换集合"的场景，
            // 正确做法是让 ScadaPage 把旧集合一并告知（自定义事件参数），而不是在这里猜。
            DetachLayers(page);
            AttachLayers(page);

            ApplyLayerVisibility();
        }

        private void AttachLayers(ScadaPage page)
        {
            page.Layers.CollectionChanged += OnLayersChanged;

            foreach (var layer in page.Layers)
                HookLayer(layer);
        }

        private void DetachLayers(ScadaPage page)
        {
            page.Layers.CollectionChanged -= OnLayersChanged;

            foreach (var layer in _subscribedLayers)
                layer.PropertyChanged -= OnLayerPropertyChanged;

            _subscribedLayers.Clear();
        }

        private void HookLayer(ScadaLayer layer)
        {
            // Add 返回 false 说明表里已有 = 已经订阅过，一行就挡住了重复挂
            if (_subscribedLayers.Add(layer))
                layer.PropertyChanged += OnLayerPropertyChanged;
        }

        private void OnLayersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var layer in _subscribedLayers)
                    layer.PropertyChanged -= OnLayerPropertyChanged;

                _subscribedLayers.Clear();

                if (Page is { } page)
                {
                    foreach (var layer in page.Layers)
                        HookLayer(layer);
                }
            }
            else
            {
                if (e.OldItems != null)
                {
                    foreach (ScadaLayer layer in e.OldItems)
                    {
                        if (_subscribedLayers.Remove(layer))
                            layer.PropertyChanged -= OnLayerPropertyChanged;
                    }
                }

                if (e.NewItems != null)
                {
                    foreach (ScadaLayer layer in e.NewItems)
                        HookLayer(layer);
                }
            }

            // 新增图层（默认可见）、删掉图层（归属它的图元变成"未分层"= 不受图层约束）
            // 都会改变该显示什么，所以一律重算一次
            ApplyLayerVisibility();
        }

        private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ScadaLayer.IsVisible) or null)
                ApplyLayerVisibility();

            // IsLocked / Name 不进这里：锁定判定是在鼠标事件发生时现算的（CanEditElement），
            // 改名不影响渲染。没什么要预备的，就不挂没什么要重算的订阅——少一条通路少一处漏。
        }

        /// <summary>图元此刻该不该显示（没有画面上下文就一律显示）</summary>
        private bool IsElementShown(ScadaElement element)
            => Page is not { } page || page.IsElementVisible(element);

        /// <summary>
        /// 图元此刻能不能被编辑（图层锁定与图元锁定是<b>或</b>关系）。
        ///
        /// 判定权整个交给 <see cref="ScadaPage.IsElementEditable"/>，画布自己不再写一遍
        /// <c>!element.IsLocked</c>：唯一口径只有一份（S3-e2 决策 ⑧）。
        /// 传不进画面（纯控件库用法）才退回图元自身的锁。
        /// </summary>
        private bool CanEditElement(ScadaElement element)
            => Page is { } page ? page.IsElementEditable(element) : !element.IsLocked;

        /// <summary>
        /// 按当前图层状态重算所有容器的可见性。
        ///
        /// 一个必须记住的语义决定：隐藏图层<b>不</b>把控件从 <c>Children</c> 里摘掉，只置 Collapsed。
        /// 于是元素层永远是 <see cref="ItemsSource"/> 的全量同步视图
        /// （<see cref="RenderedElementCount"/> == 数据条数），上面那套 Add/Remove/Reset 的同步逻辑
        /// 不必再长出一个"过滤"例外分支。代价是隐藏层的控件仍占可视树；
        /// 换来的是"图层一开，图元立刻回来"——万级图元下"隐藏再显示"若走重建，是一次几百毫秒的白等。
        /// </summary>
        public void ApplyLayerVisibility()
        {
            // 选中项正好在"刚被隐藏"的图层上 → 顺手取消选中。
            // 宁可让用户回到画布上重新点一下，也不能留一个"看不见、却随时会被 Delete 掉"的选中项：
            // 编辑器现在没有撤销，误删一个隐形图元是找不回来的。
            if (SelectedElement is { } selected && !IsElementShown(selected))
                SetCurrentValue(SelectedElementProperty, null);

            if (_elementLayer == null)
                return;

            foreach (var child in _elementLayer.Children)
            {
                if (child is ScadaElementBase control)
                    control.RefreshLayerVisibility(); // 走控件自己那条路，判定仍是下面注入的这一份
                else if (child is FrameworkElement placeholder && placeholder.Tag is ScadaElement element)
                    placeholder.Visibility = IsElementShown(element) ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdateSelectionVisual(); // 隐藏层上的图元不该还顶着个选中框
        }

        #endregion

        #region 网格

        /// <summary>吸附步长（网格间距小于 1 视为"没有可用的网格"，返回 0 让吸附自己失效）</summary>
        private double GridStep
        {
            get
            {
                double size = GridSize;

                return double.IsNaN(size) || size < 1 ? 0 : size;
            }
        }

        /// <summary>
        /// 重画网格：一个平铺的 DrawingBrush，tile 边长 = 一个主网格周期。
        ///
        /// 为什么用 DrawingBrush 平铺而不是一堆 Line 元素：1920×1080、10px 网格 = 300 条线，
        /// 每条都是一个可视元素，缩放时每帧重排 300 个 Visual 的代价远大于刷一次画刷。
        /// </summary>
        private void RebuildGrid()
        {
            if (_gridLayer == null)
                return;

            double size = GridSize;

            if (!ShowGrid || size < 1 || double.IsNaN(size))
            {
                _gridLayer.Fill = null;
                _minorPen = null;
                _majorPen = null;
                return;
            }

            double tile = size * MajorGridEvery;

            _minorPen ??= new Pen();
            _majorPen ??= new Pen();

            var brush = GridBrush ?? Brushes.Gray;
            _minorPen.Brush = brush;
            _majorPen.Brush = brush;

            // 线宽补偿：设计坐标画的线会被 RenderTransform 一起放大，除以 Zoom 才能在屏幕上恒定
            // 细线 1px、主网格 2px（"更粗一档"就靠这个，省一条独立的颜色属性）。
            _minorPen.Thickness = 1 / Math.Max(0.01, Zoom);
            _majorPen.Thickness = 2 / Math.Max(0.01, Zoom);

            var group = new DrawingGroup();

            for (int i = 0; i < MajorGridEvery; i++)
            {
                double p = i * size;
                var pen = i == 0 ? _majorPen : _minorPen;

                group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(p, 0), new Point(p, tile))));
                group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, p), new Point(tile, p))));
            }

            _gridLayer.Fill = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, tile, tile),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
        }

        #endregion

        #region 选中框

        /// <summary>造选中框（一个虚线框 + 四个角手柄），放在画面坐标系里跟着缩放走</summary>
        private void EnsureSelectionVisual()
        {
            if (_selectionLayer == null || _selectionBox != null)
                return;

            _selectionRect = new Rectangle
            {
                Stroke = ScadaBrushes.Frozen(Color.FromRgb(0x00, 0x7A, 0xCC)),
                StrokeDashArray = new DoubleCollection { 3, 2 },
                Fill = null,
                IsHitTestVisible = false, // 点框内空白要穿透到图元（选中框自己不吃点击）
            };

            _selectionRotation = new RotateTransform();

            _selectionBox = new Canvas
            {
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _selectionRotation,
                IsHitTestVisible = true,
            };
            _selectionBox.Children.Add(_selectionRect);

            // 四个角：(-1,-1)=左上、(1,-1)=右上、(1,1)=右下、(-1,1)=左下，符号直接参与改尺寸算式
            _thumbs = new[]
            {
                CreateThumb(new Vector(-1, -1), Cursors.SizeNWSE),
                CreateThumb(new Vector(1, -1), Cursors.SizeNESW),
                CreateThumb(new Vector(1, 1), Cursors.SizeNWSE),
                CreateThumb(new Vector(-1, 1), Cursors.SizeNESW),
            };

            foreach (var thumb in _thumbs)
                _selectionBox.Children.Add(thumb);

            _selectionLayer.Children.Add(_selectionBox);
        }

        private static Rectangle CreateThumb(Vector sign, Cursor cursor)
            => new()
            {
                Fill = ScadaBrushes.Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                Stroke = ScadaBrushes.Frozen(Color.FromRgb(0x00, 0x7A, 0xCC)),
                StrokeThickness = 1,
                Cursor = cursor,
                Tag = sign, // 手柄标识装在 Tag 里：省一个枚举，且命中测试天然带回来
            };

        private void SyncSelectedSubscription()
        {
            var target = IsLoaded ? SelectedElement : null;

            if (ReferenceEquals(_subscribedSelected, target))
                return;

            if (_subscribedSelected != null)
                _subscribedSelected.PropertyChanged -= OnSelectedElementPropertyChanged;

            _subscribedSelected = target;

            if (target != null)
                target.PropertyChanged += OnSelectedElementPropertyChanged;
        }

        private void OnSelectedElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
            => UpdateSelectionVisual();

        /// <summary>
        /// 按当前选中图元与缩放重画选中框。
        ///
        /// 手柄尺寸/线宽都除以 Zoom：选中框画在画面坐标系里（这样平移缩放时天然跟着走），
        /// 但操作手柄必须在屏幕上保持可点大小，所以这两者要反向补偿。
        /// </summary>
        private void UpdateSelectionVisual()
        {
            if (_selectionBox == null || _elementLayer == null)
                return;

            var element = SelectedElement;

            if (element == null || !IsLoaded || !IsRendered(element) || !IsElementShown(element))
            {
                // 没有选中、选中的图元当前没被渲染（切画面途中）、或它所在的图层被隐藏 → 收起
                if (_selectionBox.Visibility != Visibility.Collapsed)
                    _selectionBox.Visibility = Visibility.Collapsed;

                return;
            }

            double zoom = Math.Max(0.01, Zoom);
            double gap = 1 / zoom;              // 离开图元边框 1 屏幕像素
            double thumb = ThumbDesignSize / zoom;
            double half = thumb / 2;

            double boxWidth = Math.Max(1, element.Width) + gap * 2;
            double boxHeight = Math.Max(1, element.Height) + gap * 2;

            _selectionBox.Visibility = Visibility.Visible;
            _selectionBox.Width = boxWidth;
            _selectionBox.Height = boxHeight;
            Canvas.SetLeft(_selectionBox, element.X - gap);
            Canvas.SetTop(_selectionBox, element.Y - gap);

            if (_selectionRotation != null)
                _selectionRotation.Angle = element.Rotation;

            if (_selectionRect != null)
            {
                _selectionRect.Width = boxWidth;
                _selectionRect.Height = boxHeight;
                _selectionRect.StrokeThickness = 1 / zoom;
            }

            if (_thumbs != null)
            {
                bool showThumbs = !IsReadOnly && CanEditElement(element);

                for (int i = 0; i < _thumbs.Length; i++)
                {
                    var thumbRect = _thumbs[i];
                    var sign = (Vector)thumbRect.Tag!;

                    thumbRect.Width = thumb;
                    thumbRect.Height = thumb;
                    thumbRect.StrokeThickness = 1 / zoom;
                    thumbRect.Visibility = showThumbs ? Visibility.Visible : Visibility.Collapsed;

                    Canvas.SetLeft(thumbRect, sign.X > 0 ? boxWidth - half : -half);
                    Canvas.SetTop(thumbRect, sign.Y > 0 ? boxHeight - half : -half);
                }
            }
        }

        #endregion
    }
}
