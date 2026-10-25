// -----------------------------------------------------------------------------
//  FlowCanvasSurface —— 流程画布「C2 整块自绘」第 1 步：只读渲染层
//
//  背景（主会话实测）：画布现在是「每个节点一个 Nodify ItemContainer + 约 33 个模板元素」，
//  353 步方案一次重建 7.4 秒，其中自有 C# 逻辑不到 5%，其余全是 WPF 建控件。
//  本文件把节点 / 泳道 / 容器框 / 连线全部画进一个 FrameworkElement.OnRender，
//  不产生任何子元素（VisualTree 子级恒为 0）。
//
//  第 1 步刻意不做（后续步骤再接）：
//    · 只读：不接鼠标 / 拖拽 / 键盘 / 右键菜单；命中判定只经 IFlowCanvasGeometry.HitTest；
//    · 不订阅 VM 的 PropertyChanged：数据变了由宿主调 SetModel / InvalidateSurface；
//    · 不引用任何资源键——无 Application 的断言宿主里 StaticResource 解析不到会抛
//      XamlParseException，颜色一律 Color.FromArgb 字面量，注释里标出 Colors.xaml 的键名。
//
//  几何快照：SetModel / InvalidateSurface 时一次性把 VM 的节点与连线压成数组，并按
//  ZOrder 升序（同序保持 Nodes 集合顺序）排好绘制顺序；OnRender 只遍历缓存数组，
//  不碰 ObservableCollection、不做 LINQ、不建几何对象（连线 StreamGeometry 也在快照期
//  建好并 Freeze）。FormattedText 允许在 OnRender 里建，本文件仍按
//  (文本, 字号, 粗细, 最大宽, 画刷) 做了小缓存，平移 / 缩放时不再重排版文字。
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Nodify;
using VisionMaster.ViewModels;

namespace VisionMaster.Views
{
    /// <summary>
    /// 自绘层里的节点身份。与 <see cref="CanvasNodeKind"/> 一一对应，故意不复用 VM 的枚举：
    /// 断言侧不依赖 VisionMaster.ViewModels 的细节也能判型。
    /// </summary>
    public enum FlowSurfaceKind
    {
        Step,
        Container,
        Lane,
    }

    /// <summary>
    /// 一个节点的几何快照：图坐标下的外接框 + 叠放层次 + 原始节点 VM（契约里的 object）。
    /// </summary>
    public readonly record struct FlowNodeRect(FlowSurfaceKind Kind, Rect Bounds, int ZOrder, object Model);

    /// <summary>
    /// 一根连线的几何快照：两端锚点、进出边、以及三种语义标记（顺序链 / 非法 / 已执行）。
    /// </summary>
    public readonly record struct FlowLinkLine(
        Point Source,
        Point Target,
        ConnectorPosition SourceSide,
        ConnectorPosition TargetSide,
        bool IsOrder,
        bool IsIllegal,
        bool IsExecuted);

    /// <summary>
    /// 只读几何查询面：自绘层对外的唯一入口（不依赖 WPF 元素，断言宿主可直接用）。
    /// </summary>
    public interface IFlowCanvasGeometry
    {
        /// <summary>节点快照（与 VM 的 Nodes 集合同序，便于按下标对账；绘制顺序另按 ZOrder 排）</summary>
        IReadOnlyList<FlowNodeRect> Nodes { get; }

        /// <summary>连线快照（与 VM 的 Links 集合同序）</summary>
        IReadOnlyList<FlowLinkLine> Links { get; }

        /// <summary>命中判据：Bounds.Contains，取 ZOrder 最大者（同 ZOrder 取后画的）</summary>
        FlowNodeRect? HitTest(Point graphPoint);

        /// <summary>所有节点外接框的并集；空时返回 <see cref="Rect.Empty"/></summary>
        Rect ContentBounds { get; }

        /// <summary>
        /// 把 <see cref="ContentBounds"/> 按 viewport 等比缩放到留 24px 边距，
        /// zoom 夹在 [minZoom, maxZoom]。返回的是**图坐标下**的可见区域
        /// （即 viewport / zoom 居中到内容中心）；内容为空 / viewport 非正时返回
        /// <see cref="Rect.Empty"/>。本方法不改 <see cref="FlowCanvasSurface.Zoom"/> /
        /// <see cref="FlowCanvasSurface.Pan"/>：调用方取 zoom = viewport.Width / rect.Width，
        /// pan = viewport 中心 − 内容中心 × zoom。
        /// </summary>
        Rect ComputeFitViewport(Size viewport, double minZoom = 0.05, double maxZoom = 2.0);
    }

    /// <summary>
    /// 流程画布的自绘表面：一个 FrameworkElement 画完所有节点与连线，零子元素。
    /// 只读（第 1 步），交互在后续步骤接。
    /// </summary>
    public sealed class FlowCanvasSurface : FrameworkElement, IFlowCanvasGeometry
    {
        // ---- 视觉常量（与 FlowCanvasView.xaml 的模板对齐；数值只在这里出现一次）----

        private const double StepCornerRadius = 10;
        private const double ContainerCornerRadius = 12;
        private const double LaneCornerRadius = 8;

        /// <summary>贝塞尔控制点沿进出边外推的距离（图坐标）</summary>
        private const double LinkControlPush = 60;

        /// <summary>ComputeFitViewport 的四周留白（屏幕像素）</summary>
        private const double FitMargin = 24;

        private const double ZoomFloor = 0.01;
        private const double ZoomCeiling = 64;

        /// <summary>禁用节点整体透明度（与 StepNodeTemplate 的 0.45 一致）</summary>
        private const double DisabledOpacity = 0.45;

        private const int TextCacheLimit = 4096;

        // ---- 数据源与快照 ----

        private FlowCanvasViewModel? _viewModel;

        /// <summary>节点快照（集合顺序，供 IFlowCanvasGeometry.Nodes）</summary>
        private FlowNodeRect[] _nodes = Array.Empty<FlowNodeRect>();

        /// <summary>连线快照（集合顺序，供 IFlowCanvasGeometry.Links）</summary>
        private FlowLinkLine[] _links = Array.Empty<FlowLinkLine>();

        /// <summary>绘制顺序快照：ZOrder 升序、同序按集合顺序</summary>
        private NodeVisual[] _drawNodes = Array.Empty<NodeVisual>();

        /// <summary>连线绘制快照：几何已 Freeze，笔已选好</summary>
        private LinkVisual[] _drawLinks = Array.Empty<LinkVisual>();

        private Rect _contentBounds = Rect.Empty;

        // ---- 视口状态 ----

        private double _zoom = 1.0;
        private Vector _pan;
        private Transform _transform = Transform.Identity;

        // ---- 文字排版缓存 ----

        private double _pixelsPerDip = 1.0;
        private readonly Dictionary<TextKey, FormattedText> _textCache = new();

        public FlowCanvasSurface()
        {
            ClipToBounds = true;
            Focusable = false;
            UpdateTransform();
        }

        // ------------------------------------------------------------------
        //  公开契约
        // ------------------------------------------------------------------

        /// <inheritdoc />
        public IReadOnlyList<FlowNodeRect> Nodes => _nodes;

        /// <inheritdoc />
        public IReadOnlyList<FlowLinkLine> Links => _links;

        /// <inheritdoc />
        public Rect ContentBounds => _contentBounds;

        /// <summary>
        /// 图坐标 → 屏幕的缩放倍率，默认 1.0。非有限值回落 1.0，过小 / 过大的值夹到
        /// [<see cref="ZoomFloor"/>, <see cref="ZoomCeiling"/>]（0 缩放会让屏幕坐标换算出 NaN）。
        /// </summary>
        public double Zoom
        {
            get => _zoom;
            set
            {
                double zoom = double.IsFinite(value) ? Math.Clamp(value, ZoomFloor, ZoomCeiling) : 1.0;
                if (Math.Abs(zoom - _zoom) < 1e-9) return;
                _zoom = zoom;
                UpdateTransform();
                InvalidateVisual();
            }
        }

        /// <summary>图坐标 → 屏幕的平移（屏幕像素），默认 (0,0)。非有限值回落 (0,0)。</summary>
        public Vector Pan
        {
            get => _pan;
            set
            {
                var pan = double.IsFinite(value.X) && double.IsFinite(value.Y) ? value : default;
                if (pan == _pan) return;
                _pan = pan;
                UpdateTransform();
                InvalidateVisual();
            }
        }

        /// <summary>
        /// 重设数据源：把 VM 的节点 / 连线压成几何快照并重绘。
        /// 传 null 清空（Nodes / Links 变空数组，OnRender 只画透明底，不抛）。
        /// </summary>
        public void SetModel(FlowCanvasViewModel? vm)
        {
            _viewModel = vm;
            RebuildSnapshot();
            _diagDone = false;
            LogDiag($"SetModel nodes={_drawNodes.Length} links={_drawLinks.Length} bounds={_contentBounds}");
            InvalidateMeasure();
            InvalidateVisual();
        }

        // ---- 诊断（临时）：一次 SetModel 只落一行，用于真机定位"勾了自绘却是空白" ----
        private bool _diagDone;

        private void LogDiag(string message)
        {
            if (_diagDone) return;
            _diagDone = true;
            FlowCanvasViewModel.PerfLog?.Invoke("[自绘] " + message);
        }

        /// <summary>
        /// 数据变了（节点增删、坐标 / 尺寸 / 选中 / 运行态变化）由宿主调用：重拍快照并重绘。
        /// 只改缩放 / 平移不需要它（<see cref="Zoom"/> / <see cref="Pan"/> 自己会重绘）。
        /// </summary>
        public void InvalidateSurface()
        {
            RebuildSnapshot();
            _diagDone = false;
            LogDiag($"InvalidateSurface nodes={_drawNodes.Length} links={_drawLinks.Length} bounds={_contentBounds}");
            InvalidateMeasure();
            InvalidateVisual();
        }

        /// <inheritdoc />
        public FlowNodeRect? HitTest(Point graphPoint)
        {
            FlowNodeRect? hit = null;
            var items = _drawNodes;   // 已按 ZOrder 升序：后画的盖住先画的，同序取集合里靠后的

            for (int i = 0; i < items.Length; i++)
            {
                var candidate = items[i].Rect;
                if (!candidate.Bounds.Contains(graphPoint)) continue;
                if (hit is null || candidate.ZOrder >= hit.Value.ZOrder) hit = candidate;
            }

            return hit;
        }

        /// <inheritdoc />
        public Rect ComputeFitViewport(Size viewport, double minZoom = 0.05, double maxZoom = 2.0)
        {
            var content = _contentBounds;
            if (content.IsEmpty || content.Width <= 0 || content.Height <= 0) return Rect.Empty;
            if (!double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height)) return Rect.Empty;
            if (viewport.Width <= 0 || viewport.Height <= 0) return Rect.Empty;

            double floor = double.IsFinite(minZoom) && minZoom > 0 ? minZoom : 0.05;
            double ceiling = double.IsFinite(maxZoom) && maxZoom > floor ? maxZoom : floor;

            // 留白后的可用区：两个方向取更紧的那个缩放，保证内容整块进得来
            double availableWidth = Math.Max(1.0, viewport.Width - FitMargin * 2);
            double availableHeight = Math.Max(1.0, viewport.Height - FitMargin * 2);
            double zoom = Math.Clamp(
                Math.Min(availableWidth / content.Width, availableHeight / content.Height),
                floor,
                ceiling);

            double visibleWidth = viewport.Width / zoom;
            double visibleHeight = viewport.Height / zoom;
            double centerX = content.X + content.Width / 2;
            double centerY = content.Y + content.Height / 2;

            return new Rect(
                centerX - visibleWidth / 2,
                centerY - visibleHeight / 2,
                visibleWidth,
                visibleHeight);
        }

        // ------------------------------------------------------------------
        //  布局 / 渲染
        // ------------------------------------------------------------------

        /// <summary>
        /// 有限空间就填满；无限（Canvas / StackPanel 里）时退回内容在屏幕上的右下边界，
        /// 免得 DesiredSize 恒为 0 让整块表面被排成 0×0。
        /// </summary>
        protected override Size MeasureOverride(Size availableSize)
        {
            double width = availableSize.Width;
            double height = availableSize.Height;

            if (double.IsInfinity(width) || double.IsInfinity(height))
            {
                var content = _contentBounds;
                double contentWidth = content.IsEmpty ? 0 : (content.X + content.Width) * _zoom + _pan.X;
                double contentHeight = content.IsEmpty ? 0 : (content.Y + content.Height) * _zoom + _pan.Y;
                if (double.IsInfinity(width)) width = contentWidth;
                if (double.IsInfinity(height)) height = contentHeight;
            }

            return new Size(
                double.IsFinite(width) ? Math.Max(0, width) : 0,
                double.IsFinite(height) ? Math.Max(0, height) : 0);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = RenderSize;
            if (size.Width <= 0 || size.Height <= 0)
            {
                LogDiag($"OnRender 尺寸为0 size={size.Width}x{size.Height}");
                return;
            }

            // 画布底：与 NodifyEditor.Background 同一个 DrawingBrush——#F7F8FA 底 + 24×24 一个圆点（R:12,12）。
            // 点阵是**屏幕空间**的（Nodify 的 Background 画在控件上，不跟视口平移），所以画在 PushTransform 之前。
            dc.DrawRectangle(GridBackground, null, new Rect(size));

            if (_drawNodes.Length == 0 && _drawLinks.Length == 0)
            {
                LogDiag($"OnRender 空快照 size={size.Width:F0}x{size.Height:F0} vm={( _viewModel is null ? "null" : "ok")} vmNodes={_viewModel?.Nodes.Count ?? -1}");
                return;
            }

            double pixelsPerDip = GetPixelsPerDip();
            if (Math.Abs(pixelsPerDip - _pixelsPerDip) > 0.001)
            {
                // FormattedText 的字形度量绑 DPI：换屏 / 换缩放比例必须整表重排
                _pixelsPerDip = pixelsPerDip;
                _textCache.Clear();
            }

            dc.PushTransform(_transform);
            try
            {
                // 视口裁剪：把屏幕矩形反变换回图坐标，只画与它相交的节点。
                // 这是自绘相对"每节点一个元素"的核心优势——元素方案里屏外节点照样要 Measure/Arrange，
                // 这里直接跳过。外扩 CullMargin 是为了让"一半在屏内"的卡片连边框一起画全。
                var visible = new Rect(
                    (0 - _pan.X) / _zoom,
                    (0 - _pan.Y) / _zoom,
                    size.Width / _zoom,
                    size.Height / _zoom);
                visible.Inflate(CullMargin, CullMargin);

                // 低缩放时不画文字：缩到能看全图时字本来就不可读，而文字排版是自绘里最贵的一段。
                // 阈值 0.4 与 Nodify 自己的 OptimizeRenderingZoomOutPercent(0.3) 同一量级。
                _textVisible = _zoom >= LodTextZoom;

                // 再低一档：只画纯色块（无边框、无圆角、无图标）。看全图时这些细节在屏幕上
                // 不足一个像素，画了也看不见，却要付每条描边/每个圆角的代价。
                _simpleMode = _zoom < LodSimpleZoom;

                var nodes = _drawNodes;

                // 第 1 层：容器框 + 泳道。按 ZOrder 升序画（同层"框 < 泳道 < 内容"，跨层按拓扑深度），
                // 嵌套容器的框必须压在父泳道之上，所以这一层不能按 Kind 再拆开重排。
                int drawnNodes = 0;
                for (int i = 0; i < nodes.Length; i++)
                {
                    var item = nodes[i];
                    if (!visible.IntersectsWith(item.Rect.Bounds)) continue;

                    switch (item.Rect.Kind)
                    {
                        case FlowSurfaceKind.Container:
                            drawnNodes++;
                            DrawContainer(dc, item.Node, item.Rect.Bounds);
                            break;
                        case FlowSurfaceKind.Lane:
                            drawnNodes++;
                            DrawLane(dc, item.Node, item.Rect.Bounds);
                            break;
                    }
                }

                // 第 2 层：连线。**必须在节点之下**——否则线会压住卡片边缘与徽标（Nodify 也是这个层级）。
                var links = _drawLinks;
                for (int i = 0; i < links.Length; i++)
                {
                    var link = links[i];
                    if (!visible.IntersectsWith(link.Bounds)) continue;
                    DrawLink(dc, link);
                }

                // 第 2.5 层：容器组头内容重画一遍。
                // 连线是"框/泳道之上、卡片之下"，横穿容器时会把标题/条件胶囊/徽标压住
                //（2026-10-25 实拍：蓝线正好压在「该不存在图(应失败)」上）。重画文字即可，
                // 底带不重画——否则经过组头的顺序链会被切断。
                for (int i = 0; i < nodes.Length; i++)
                {
                    var item = nodes[i];
                    if (item.Rect.Kind != FlowSurfaceKind.Container) continue;
                    if (!visible.IntersectsWith(item.Rect.Bounds)) continue;
                    DrawContainerHeaderOverlay(dc, item.Node, item.Rect.Bounds);
                }

                // 第 3 层：步骤卡片（叶子，永远压在框/泳道/连线之上）
                for (int i = 0; i < nodes.Length; i++)
                {
                    var item = nodes[i];
                    if (item.Rect.Kind != FlowSurfaceKind.Step) continue;
                    if (!visible.IntersectsWith(item.Rect.Bounds)) continue;

                    drawnNodes++;
                    DrawStep(dc, item.Node, item.Rect.Bounds);
                }

                // 第 4 层：连线标签。必须压在卡片之上，否则会被卡片切掉半截。
                if (_textVisible) DrawLinkLabels(dc, visible);

                _lastDrawnNodes = drawnNodes;
                LogDiag($"OnRender size={size.Width:F0}x{size.Height:F0} zoom={_zoom:F4} pan=({_pan.X:F0},{_pan.Y:F0}) 快照={_drawNodes.Length} 实画={drawnNodes} 可见=({visible.X:F0},{visible.Y:F0},{visible.Width:F0},{visible.Height:F0})");
            }
            finally
            {
                dc.Pop();
            }
        }

        /// <summary>最近一帧实际绘制的节点数（探针/诊断用：证明裁剪真的生效）</summary>
        public int LastDrawnNodeCount => _lastDrawnNodes;

        /// <summary>诊断：图标字体解析出多少个字形面。0 = 字体没加载，FA 字形会退化成豆腐块。</summary>
        public static int IconFontTypefaceCount => IconFontFamily.GetTypefaces().Count();

        private int _lastDrawnNodes;

        /// <summary>裁剪外扩（图坐标）：保证跨在视口边缘的卡片/容器框完整画出</summary>
        private const double CullMargin = 64;

        /// <summary>低于这个缩放不画文字（只画色块与边框）</summary>
        private const double LodTextZoom = 0.4;

        /// <summary>低于这个缩放只画纯色块（连边框/圆角都省）——看全图时的档位</summary>
        private const double LodSimpleZoom = 0.15;

        private bool _simpleMode;

        /// <summary>禁用态的透明度（与 XAML 模板的 0.45 一致），通过预乘进画刷实现，不用图层</summary>
        // DisabledOpacity 已在类型顶部定义（:109），这里不重复声明

        /// <summary>Fade 结果缓存：Palette 的画刷/画笔都是冻结单例，身份稳定，可以直接当键</summary>
        private static readonly Dictionary<object, object> _fadeCache = new();

        /// <summary>把画刷的 alpha 预乘 <see cref="DisabledOpacity"/>（结果冻结并缓存）。</summary>
        private static Brush Fade(Brush source)
        {
            if (source is not SolidColorBrush solid) return source;
            if (_fadeCache.TryGetValue(source, out var cached)) return (Brush)cached;

            var c = solid.Color;
            var faded = new SolidColorBrush(
                Color.FromArgb((byte)Math.Round(c.A * DisabledOpacity), c.R, c.G, c.B));
            faded.Freeze();
            _fadeCache[source] = faded;
            return faded;
        }

        /// <summary>把画笔的 alpha 预乘（保留粗细与虚线样式）。</summary>
        private static Pen Fade(Pen source)
        {
            if (_fadeCache.TryGetValue(source, out var cached)) return (Pen)cached;

            var faded = new Pen(Fade(source.Brush), source.Thickness)
            {
                DashStyle = source.DashStyle,
                StartLineCap = source.StartLineCap,
                EndLineCap = source.EndLineCap,
            };
            faded.Freeze();
            _fadeCache[source] = faded;
            return faded;
        }

        private bool _textVisible = true;

        private double GetPixelsPerDip()
        {
            try
            {
                double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                return double.IsFinite(dpi) && dpi > 0 ? dpi : 1.0;
            }
            catch
            {
                // 未接入可视树的断言宿主里个别环境取 DPI 会抛（与 HistogramPlot 同一条兜底）
                return 1.0;
            }
        }

        // ------------------------------------------------------------------
        //  节点绘制
        // ------------------------------------------------------------------

        /// <summary>
        /// 泳道：逐项对齐 LaneNodeTemplate——圆角 8、类型色淡底、头带 28（分支徽标 + 名称 + 计数）、
        /// 空分支提示、While/For 的底部虚线说明条。运行/中性/故障三种覆盖色按 XAML 触发器顺序叠加。
        /// </summary>
        private void DrawLane(DrawingContext dc, CanvasNodeViewModel node, Rect bounds)
        {
            var theme = ResolveLaneTheme(node);

            if (_simpleMode)
            {
                dc.DrawRectangle(theme.Fill, null, bounds);
                return;
            }

            double radius = ClampRadius(LaneCornerRadius, bounds);
            dc.DrawRoundedRectangle(theme.Fill, null, bounds, radius, radius);

            double headerHeight = Math.Min(FlowCanvasViewModel.LaneHeaderHeight, bounds.Height);
            if (headerHeight > 0.5)
            {
                var band = new Rect(bounds.X, bounds.Y, bounds.Width, headerHeight);
                DrawTopBand(dc, band, radius, theme.HeaderFill);
                dc.DrawLine(
                    FrozenPenCached(theme.Border, 1),
                    new Point(band.X + 1, band.Bottom),
                    new Point(band.Right - 1, band.Bottom));
            }

            if (_textVisible && headerHeight >= 14)
            {
                double mid = bounds.Y + headerHeight / 2;
                double x = bounds.X + 8;
                double right = bounds.Right - 8;

                // 分支徽标：16 高、padding 5、圆角 4、Consolas 8 Bold 白字
                string? badgeText = node.BranchBadge;
                if (!string.IsNullOrEmpty(badgeText))
                {
                    x = DrawChip(dc, badgeText, x, mid - 8, 16, 5, 4,
                        theme.Badge, null, Palette.OnOrderBadgeText, SurfaceFont.Mono, bold: true, 8) + 6;
                }

                // 右侧计数（Consolas 8.5 灰），先量出来好给名字留宽度
                string? countText = node.LaneCountText;
                double countWidth = 0;
                FormattedText? countFmt = null;
                if (!string.IsNullOrEmpty(countText))
                {
                    countFmt = GetText(countText, 8.5, bold: false, Palette.SubText, 80, SurfaceFont.Mono);
                    countWidth = countFmt.Width + 6;
                    dc.DrawText(countFmt, new Point(right - countFmt.Width, mid - countFmt.Height / 2));
                }

                // 泳道名：9.5 SemiBold，类型色（运行/中性/故障已并入 theme.Text）
                double nameWidth = right - countWidth - x;
                if (nameWidth > 6)
                {
                    var nameFmt = GetText(node.LaneName, 9.5, bold: true, theme.Text, nameWidth);
                    dc.DrawText(nameFmt, new Point(x, mid - nameFmt.Height / 2));
                }
            }

            // 空分支提示：头带下方 10,6，9px 灰字
            if (_textVisible && node.IsLaneEmpty && headerHeight + 30 < bounds.Height)
            {
                var emptyFmt = GetText("（空分支，可从流程栏添加步骤）", 9, bold: false, Palette.SubText, bounds.Width - 20);
                dc.DrawText(emptyFmt, new Point(bounds.X + 10, bounds.Y + headerHeight + 6));
            }

            // 底部说明条（While / For）：22 高、底距 9、虚线框
            if (_textVisible && node.LaneNoteVisibility == Visibility.Visible && bounds.Height > headerHeight + 40)
            {
                var noteRect = new Rect(bounds.X + 10, bounds.Bottom - 9 - 22, bounds.Width - 20, 22);
                dc.DrawRoundedRectangle(theme.HeaderFill, FrozenDashPenCached(theme.Border, 1), noteRect, 5, 5);

                double nx = noteRect.X + 7;
                string? glyph = node.LaneNoteGlyph;
                if (!string.IsNullOrEmpty(glyph))
                {
                    var glyphFmt = GetText(glyph, 10, bold: false, theme.Text, 20, SurfaceFont.Icon);
                    dc.DrawText(glyphFmt, new Point(nx, noteRect.Y + (22 - glyphFmt.Height) / 2));
                    nx += glyphFmt.Width + 6;
                }

                var noteFmt = GetText(node.LaneNoteText, 8.5, bold: false, Palette.SubText,
                    Math.Max(1, noteRect.Right - 7 - nx));
                dc.DrawText(noteFmt, new Point(nx, noteRect.Y + (22 - noteFmt.Height) / 2));
            }

            // 边框最后画：头带会盖住上缘描边，描边压在最上层才不会被吃掉
            dc.DrawRoundedRectangle(null, FrozenPenCached(theme.Border, theme.Thickness), bounds, radius, radius);
        }

        /// <summary>
        /// 容器框：逐项对齐 ContainerNodeTemplate——圆角 12、1.5px 描边、头带 46（类型色条 + 徽标 +
        /// 标题 12.5 + 条件胶囊 + meta + 状态点）、并行汇合条 22、折叠钮 18×18、序号 24×18(−9,−9)。
        /// </summary>
        private void DrawContainer(DrawingContext dc, CanvasNodeViewModel node, Rect bounds)
        {
            var model = node.Model;
            bool faulted = model?.State == StepState.Failed || node.HasMissingCondition;
            bool disabled = model?.IsDisEnable == true;
            var theme = ResolveContainerTheme(model?.PluginTypeName, faulted);
            double radius = ClampRadius(ContainerCornerRadius, bounds);

            if (_simpleMode)
            {
                dc.DrawRectangle(faulted ? Palette.ContainerFailedFill : Palette.ContainerFill, null, bounds);
                return;
            }

            Brush frameFill = faulted ? Palette.ContainerFailedFill : Palette.ContainerFill;
            Brush headerFill = theme.Header;
            Brush borderBrush = theme.Border;
            Brush titleBrush = faulted ? Palette.FailedText : Palette.NodeText;

            if (disabled)
            {
                frameFill = Fade(frameFill);
                // 渐变头带没法按 alpha 预乘（Fade 只处理 SolidColorBrush），禁用态退成同色系纯色
                headerFill = Fade(Palette.FrameHeaderFill);
                borderBrush = Fade(borderBrush);
                titleBrush = Fade(titleBrush);
            }

            dc.DrawRoundedRectangle(frameFill, null, bounds, radius, radius);

            double headerHeight = Math.Min(FlowCanvasViewModel.FrameHeaderHeight, bounds.Height);
            if (headerHeight > 0.5)
            {
                // 头带：只圆上面两角 + 类型色→白(0.76) 横向渐变。渐变按几何盒映射，
                // 不能像纯色那样"圆角矩形 + 补角矩形"分两次画（两段各自映射会出现接缝）。
                var band = new Rect(bounds.X, bounds.Y, bounds.Width, headerHeight);
                dc.PushClip(TopRoundedGeometry(band, Math.Min(radius, 10)));
                dc.DrawRectangle(headerFill, null, band);
                dc.Pop();
                dc.DrawLine(
                    FrozenPenCached(borderBrush, 1),
                    new Point(band.X + 1, band.Bottom),
                    new Point(band.Right - 1, band.Bottom));
            }

            Pen framePen = node.IsSelected
                ? Palette.ContainerSelectedPen
                : FrozenPenCached(borderBrush, 1.5);
            dc.DrawRoundedRectangle(null, framePen, bounds, radius, radius);

            if (_textVisible && headerHeight >= 20)
                DrawContainerHeader(dc, node, bounds, headerHeight, theme, titleBrush, disabled);

            // 并行分组底部汇合条：22 高、底距 6、左右各 12
            if (_textVisible && node.JoinBarVisibility == Visibility.Visible && bounds.Height > headerHeight + 40)
            {
                var joinRect = new Rect(bounds.X + 12, bounds.Bottom - 6 - 22, bounds.Width - 24, 22);
                dc.DrawRoundedRectangle(Palette.ParallelSoft, FrozenPenCached(Palette.ParallelBorder, 1), joinRect, 6, 6);

                var glyphFmt = GetText("\uf126", 10, bold: false, Palette.ParallelText, 20, SurfaceFont.Icon);
                var textFmt = GetText(node.JoinBarText, 9, bold: false, Palette.ParallelText,
                    Math.Max(1, joinRect.Width - 20), SurfaceFont.Mono);
                double total = glyphFmt.Width + 6 + textFmt.Width;
                double startX = joinRect.X + (joinRect.Width - total) / 2;
                double midY = joinRect.Y + joinRect.Height / 2;

                dc.DrawText(glyphFmt, new Point(startX, midY - glyphFmt.Height / 2));
                dc.DrawText(textFmt, new Point(startX + glyphFmt.Width + 6, midY - textFmt.Height / 2));
            }

            // 折叠钮 18×18：浮在组头右上角（Margin 0,14,6,0）
            if (_textVisible && headerHeight >= 34 && bounds.Width >= 40)
            {
                var toggleRect = new Rect(bounds.Right - 6 - 18, bounds.Y + 14, 18, 18);
                dc.DrawRoundedRectangle(disabled ? Fade(Palette.CollapseFill) : Palette.CollapseFill,
                    Palette.CollapsePen, toggleRect, 5, 5);
                string? glyph = node.CollapseGlyph;
                if (!string.IsNullOrEmpty(glyph))
                {
                    DrawGlyphCentered(dc, glyph, toggleRect, 10,
                        disabled ? Fade(Palette.CollapseText) : Palette.CollapseText);
                }
            }

            // 执行序号角标：24×18，左上角 −9,−9
            if (!string.IsNullOrEmpty(node.OrderLabel))
            {
                Brush orderText = disabled ? Fade(Palette.OnOrderBadgeText) : Palette.OnOrderBadgeText;
                var orderFmt = GetText(node.OrderLabel, 9, bold: true, orderText, 60, SurfaceFont.Mono);
                var orderRect = new Rect(bounds.X - 9, bounds.Y - 9, 24, 18);
                dc.DrawRoundedRectangle(disabled ? Fade(Palette.OrderBadgeFill) : Palette.OrderBadgeFill,
                    null, orderRect, 5, 5);
                dc.DrawText(orderFmt, new Point(
                    orderRect.X + (24 - orderFmt.Width) / 2,
                    orderRect.Y + (18 - orderFmt.Height) / 2));
            }

            // 备注行：容器框下缘外 2px
            if (_textVisible && !string.IsNullOrEmpty(node.Comment))
            {
                var commentFmt = GetText(node.Comment, 10, bold: false,
                    disabled ? Fade(Palette.SubText) : Palette.SubText, bounds.Width);
                dc.DrawText(commentFmt, new Point(bounds.X, bounds.Bottom + 2));
            }
        }

        /// <summary>
        /// 组头一行：类型色条 4×26 → 类型徽标 20 → 标题 12.5 → 条件胶囊 20(max190) → meta 8.5 → 状态点 7。
        /// 标题与条件胶囊在 XAML 里是两个 `*` 列，各占剩余宽度一半。
        /// </summary>
        private void DrawContainerHeader(
            DrawingContext dc,
            CanvasNodeViewModel node,
            Rect bounds,
            double headerHeight,
            ContainerTheme theme,
            Brush titleBrush,
            bool disabled)
        {
            double mid = bounds.Y + headerHeight / 2;
            double x = bounds.X + 9;
            double rightEdge = bounds.Right - 6;

            // 状态点（右，Margin 0,0,28,0）+ meta（再往左 4）
            var dotCenter = new Point(rightEdge - 28 - 3.5, mid);
            dc.DrawEllipse(disabled ? Fade(StateDotBrush(node, node.HasMissingCondition || node.Model?.State == StepState.Failed)) : StateDotBrush(node, node.HasMissingCondition || node.Model?.State == StepState.Failed),
                null, dotCenter, 3.5, 3.5);

            double metaRight = rightEdge - 28 - 7 - 4;
            double metaWidth = 0;
            if (!string.IsNullOrEmpty(node.ContainerMetaText))
            {
                var metaFmt = GetText(node.ContainerMetaText, 8.5, bold: false,
                    disabled ? Fade(Palette.SubText) : Palette.SubText, 160, SurfaceFont.Mono);
                metaWidth = metaFmt.Width;
                dc.DrawText(metaFmt, new Point(metaRight - metaWidth, mid - metaFmt.Height / 2));
            }

            double starRight = metaRight - metaWidth - 6;

            // 类型色条 4×26
            if (headerHeight >= 30)
            {
                dc.DrawRoundedRectangle(theme.TypeBar, null, new Rect(x, mid - 13, 4, 26), 2, 2);
                x += 10;
            }

            // 类型徽标 20 高、padding 5、圆角 5、Consolas 9 Bold
            if (node.TypeBadgeVisibility == Visibility.Visible && !string.IsNullOrEmpty(node.TypeBadge))
            {
                x = DrawChip(dc, node.TypeBadge, x, mid - 10, 20, 5, 5,
                    theme.TypeBar, null, Palette.OnTypeBadgeText, SurfaceFont.Mono, bold: true, 9) + 6;
            }

            // 标题 + 条件胶囊：剩余宽度对半分
            double starWidth = Math.Max(0, starRight - x);
            double half = starWidth / 2;

            if (starWidth > 20)
            {
                var titleFmt = GetText(node.Header, 12.5, bold: true, titleBrush, Math.Max(1, half - 2));
                dc.DrawText(titleFmt, new Point(x, mid - titleFmt.Height / 2));
            }

            if (node.ContainerSummaryVisibility == Visibility.Visible && !string.IsNullOrEmpty(node.ContainerSummary))
            {
                double condLeft = x + half + 4;
                double condMax = Math.Max(20, Math.Min(190, starRight - condLeft));
                var condFmt = GetText(node.ContainerSummary, 9, bold: false, theme.CondText, condMax, SurfaceFont.Mono);
                double condWidth = Math.Min(condMax, condFmt.Width) + 10;
                var condRect = new Rect(condLeft, mid - 10, condWidth, 20);

                dc.DrawRoundedRectangle(Palette.NodeFill, FrozenPenCached(theme.CondBorder, 1), condRect, 5, 5);
                dc.DrawText(condFmt, new Point(condLeft + 5, mid - condFmt.Height / 2));
            }
        }

        /// <summary>
        /// 组头内容重画（连线之后调用）：标题 / 类型徽标 / 条件胶囊 / meta / 状态点 / 色条。
        /// 只画内容不画底带——底带重画会把穿过组头的顺序链切断。
        /// </summary>
        private void DrawContainerHeaderOverlay(DrawingContext dc, CanvasNodeViewModel node, Rect bounds)
        {
            if (!_textVisible) return;

            double headerHeight = Math.Min(FlowCanvasViewModel.FrameHeaderHeight, bounds.Height);
            if (headerHeight < 20) return;

            var model = node.Model;
            bool faulted = model?.State == StepState.Failed || node.HasMissingCondition;
            bool disabled = model?.IsDisEnable == true;
            var theme = ResolveContainerTheme(model?.PluginTypeName, faulted);
            var titleBrush = disabled
                ? Fade(faulted ? Palette.FailedText : Palette.NodeText)
                : faulted ? Palette.FailedText : Palette.NodeText;

            DrawContainerHeader(dc, node, bounds, headerHeight, theme, titleBrush, disabled);
        }

        /// <summary>容器主题：类型色五档 + 故障覆盖（失败/条件缺失用红系，压过类型色）。</summary>
        private readonly record struct ContainerTheme(
            Brush Border,
            Brush Header,
            Brush TypeBar,
            Brush CondBorder,
            Brush CondText);

        private static ContainerTheme ResolveContainerTheme(string? pluginTypeName, bool faulted)
        {
            if (faulted)
            {
                return new ContainerTheme(
                    Palette.StateFailed,
                    Palette.FailedSoftFill,
                    Palette.StateFailed,
                    Palette.FailedBorder,
                    Palette.FailedText);
            }

            return pluginTypeName switch
            {
                "BuiltIn_If" => new ContainerTheme(Palette.IfBorder, Palette.IfHeader, Palette.IfBrush, Palette.IfBorder, Palette.IfText),
                "BuiltIn_Case" => new ContainerTheme(Palette.CaseBorder, Palette.CaseHeader, Palette.CaseBrush, Palette.CaseBorder, Palette.CaseText),
                "BuiltIn_While" => new ContainerTheme(Palette.WhileBorder, Palette.WhileHeader, Palette.WhileBrush, Palette.WhileBorder, Palette.WhileText),
                "BuiltIn_For" => new ContainerTheme(Palette.ForBorder, Palette.ForHeader, Palette.ForBrush, Palette.ForBorder, Palette.ForText),
                "BuiltIn_Parallel" => new ContainerTheme(Palette.ParallelBorder, Palette.ParallelHeader, Palette.ParallelBrush, Palette.ParallelBorder, Palette.ParallelText),
                _ => new ContainerTheme(Palette.FrameBorder, Palette.FrameHeaderFill, Palette.TypeBadge, Palette.FrameBorder, Palette.FrameHeaderText),
            };
        }

        /// <summary>泳道主题：类型色先铺底，再按"运行 → 中性(非运行) → 故障"的顺序覆盖（与 XAML 触发器顺序一致）。</summary>
        private readonly record struct LaneTheme(
            Brush Fill,
            Brush Border,
            Brush HeaderFill,
            Brush Badge,
            Brush Text,
            double Thickness);

        private static LaneTheme ResolveLaneTheme(CanvasNodeViewModel node)
        {
            (Brush Fill, Brush Border, Brush Badge, Brush Text) baseTheme = node.LaneParentKind?.ToString() switch
            {
                "If" => (Palette.IfSoft, Palette.IfBorder, Palette.IfBrush, Palette.IfText),
                "Case" => (Palette.CaseSoft, Palette.CaseBorder, Palette.CaseBrush, Palette.CaseText),
                "While" => (Palette.WhileSoft, Palette.WhileBorder, Palette.WhileBrush, Palette.WhileText),
                "For" => (Palette.ForSoft, Palette.ForBorder, Palette.ForBrush, Palette.ForText),
                "Parallel" => (Palette.ParallelSoft, Palette.ParallelBorder, Palette.ParallelBrush, Palette.ParallelText),
                _ => ((Brush)Palette.LaneFill, Palette.LaneBorder, Palette.SubText, Palette.LaneText),
            };

            var fill = baseTheme.Fill;
            var border = baseTheme.Border;
            var badge = baseTheme.Badge;
            var text = baseTheme.Text;
            var headerFill = (Brush)Palette.LaneHeaderFill;
            double thickness = 1;

            if (node.IsBranchRunning)
            {
                fill = Palette.LaneRunningHeaderFill;
                border = Palette.StateRunning;
                headerFill = Palette.LaneRunningHeaderFill;
                text = Palette.RunningText;
                thickness = 1.5;
            }

            if (node.IsNeutralLane && !node.IsBranchRunning)
            {
                fill = Palette.LaneNeutralFill;
                border = Palette.LaneNeutralBorder;
                badge = Palette.LaneNeutralBadge;
                text = Palette.LaneNeutralText;
            }

            if (node.IsLaneParentFaulted)
            {
                fill = Palette.ContainerFailedFill;
                border = Palette.FailedBorder;
                headerFill = Palette.FailedSoftFill;
                badge = Palette.StateFailed;
                text = Palette.FailedText;
                thickness = 1.5;
            }

            return new LaneTheme(fill, border, headerFill, badge, text, thickness);
        }

        /// <summary>只圆上面两角的矩形几何（头带裁剪用）。</summary>
        private static Geometry TopRoundedGeometry(Rect rect, double radius)
        {
            double r = Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
            var geometry = new StreamGeometry();

            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(rect.X, rect.Bottom), true, true);
                ctx.LineTo(new Point(rect.X, rect.Y + r), true, false);
                ctx.ArcTo(new Point(rect.X + r, rect.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(rect.Right - r, rect.Y), true, false);
                ctx.ArcTo(new Point(rect.Right, rect.Y + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(rect.Right, rect.Bottom), true, false);
            }

            geometry.Freeze();
            return geometry;
        }

        private static readonly Dictionary<(Brush, double), Pen> DashPenCache = new();

        private static Pen FrozenDashPenCached(Brush brush, double thickness)
        {
            var key = (brush, thickness);
            if (DashPenCache.TryGetValue(key, out var cached)) return cached;

            var pen = new Pen(brush, thickness) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) };
            pen.Freeze();
            DashPenCache[key] = pen;
            return pen;
        }

        /// <summary>
        /// 步骤卡片：逐项对齐 XAML 的 StepNodeTemplate（156×58 / 圆角 10 / 1px 描边 / padding 10,8）。
        /// 图标块 28（分类色浅底 + 分类色字形 14）、标题 11.5 SemiBold（max 72）+ 类型徽标 + 操作徽标、
        /// 副标题 9（右边距 30）、状态点 7、缺失角标 14、断点 8、序号胶囊 min16×14(−7,−7)、
        /// 外链角标 12（左下）、耗时 Consolas 8（右下）、备注行 10（卡片下缘外）。
        /// </summary>
        private void DrawStep(DrawingContext dc, CanvasNodeViewModel node, Rect bounds)
        {
            var model = node.Model;
            var state = model?.State ?? StepState.Idle;
            bool disabled = model?.IsDisEnable == true;
            bool failed = state == StepState.Failed;
            bool running = state == StepState.Running;
            bool selected = node.IsSelected;
            bool debugStopped = model?.IsDebugStopped == true;
            double radius = ClampRadius(StepCornerRadius, bounds);

            // 最简档（看全图）：只画一个色块。此时一个节点在屏幕上往往不到 2 像素，
            // 描边/圆角/图标都不可辨，省掉它们是纯赚。
            if (_simpleMode)
            {
                var blockFill = failed ? Palette.NodeFailedFill : Palette.NodeFill;
                dc.DrawRectangle(disabled ? Fade(blockFill) : blockFill, null, bounds);
                return;
            }

            // 禁用态**不用** PushOpacity：`PushOpacity` 是"开一个合成图层"，每节点一个图层，
            // 软件光栅化下每节点多付几毫秒——实测 460 个禁用节点：开图层 3.2 s/帧、不开 33 ms/帧（差 100 倍）。
            // 正确做法是把透明度**预乘进画刷**（Fade），一次算好冻结复用，绘制时零额外代价。
            try
            {
                // ---- 卡片底 + 描边：StepCardStyle 四条触发器，优先级 失败 < 调试命中 < 选中 ----
                Brush cardFill = failed ? Palette.NodeFailedFill : Palette.NodeFill;
                Pen cardPen;
                if (selected) cardPen = Palette.StepSelectedPen;
                else if (debugStopped) cardPen = Palette.StepDebugPen;
                else if (running) cardPen = Palette.StepRunningPen;
                else if (failed) cardPen = Palette.StepFailedPen;
                else cardPen = Palette.StepBorderPen;

                Brush textBrush = Palette.NodeText;
                Brush subBrush = Palette.SubText;

                if (disabled)
                {
                    cardFill = Fade(cardFill);
                    cardPen = Fade(cardPen);
                    textBrush = Fade(textBrush);
                    subBrush = Fade(subBrush);
                }

                // 选中辉光：ItemContainer 的 SelectedBrush #333B82F6 + 1.5px，垫在卡片底下
                if (selected)
                {
                    var glow = new Rect(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4);
                    dc.DrawRoundedRectangle(null, Palette.SelectedGlowPen, glow, radius + 2, radius + 2);
                }

                dc.DrawRoundedRectangle(cardFill, cardPen, bounds, radius, radius);

                // ---- 图标块 28×28：分类色浅底 + 分类色 1px 描边 + 分类色字形 14 ----
                var category = model?.Category;
                Brush iconFill = CategorySoftBrush(category);
                Brush iconBorder = CategoryBorderBrush(category);
                Brush iconGlyphBrush = CategoryAccentBrush(category);
                if (disabled)
                {
                    iconFill = Fade(iconFill);
                    iconBorder = Fade(iconBorder);
                    iconGlyphBrush = Fade(iconGlyphBrush);
                }

                const double iconSize = 28;
                var iconRect = new Rect(
                    bounds.X + 10,
                    bounds.Y + (bounds.Height - iconSize) / 2,
                    iconSize,
                    iconSize);

                dc.DrawRoundedRectangle(iconFill, FrozenPenCached(iconBorder, 1), iconRect, 8, 8);

                string glyph = node.IconGlyph;
                if (string.IsNullOrEmpty(glyph)) glyph = VisionMaster.Helpers.ToolboxCategoryVisuals.IconFor(category);
                if (_textVisible && glyph.Length > 0)
                    DrawGlyphCentered(dc, glyph, iconRect, 14, iconGlyphBrush);

                // ---- 标题行（标题 max72 + 类型徽标 + 操作徽标）与副标题（右边距 30）----
                double textLeft = iconRect.Right + 8;
                double textWidth = bounds.Right - 10 - textLeft;

                var titleText = GetText(node.Header, 11.5, bold: true, textBrush, Math.Min(72, Math.Max(1, textWidth)), SurfaceFont.Ui, strike: disabled);
                double rowHeight = Math.Max(titleText.Height, 14);
                var subText = GetText(node.SubTitle, 9, bold: false, subBrush, Math.Max(1, textWidth - 30));
                double stackHeight = rowHeight + 1 + subText.Height;
                double stackTop = bounds.Y + 8 + Math.Max(0, (bounds.Height - 16 - stackHeight) / 2);

                if (_textVisible)
                {
                    dc.DrawText(titleText, new Point(textLeft, stackTop + (rowHeight - titleText.Height) / 2));

                    double badgeX = textLeft + titleText.Width + 4;
                    if (node.TypeBadgeVisibility == Visibility.Visible && !string.IsNullOrEmpty(node.TypeBadge))
                    {
                        badgeX = DrawChip(dc, node.TypeBadge, badgeX, stackTop + (rowHeight - 14) / 2,
                            14, 4, 3, Palette.TypeBadge, null, Palette.OnTypeBadgeText,
                            SurfaceFont.Ui, bold: true, 8) + 4;
                    }

                    if (node.OpBadgeVisibility == Visibility.Visible && !string.IsNullOrEmpty(node.OpBadge))
                    {
                        DrawChip(dc, node.OpBadge, badgeX, stackTop + (rowHeight - 14) / 2,
                            14, 4, 4, Palette.CaseSoft, Palette.CaseBorder, Palette.CaseText,
                            SurfaceFont.Mono, bold: true, 8);
                    }

                    dc.DrawText(subText, new Point(textLeft, stackTop + rowHeight + 1));
                }

                // ---- 右上角状态点 7×7（Margin 0,7,8,0）----
                var dot = StateDotBrush(node, failed);
                dc.DrawEllipse(disabled ? Fade(dot) : dot, null,
                    new Point(bounds.Right - 11.5, bounds.Y + 10.5), 3.5, 3.5);

                // ---- 必填缺失角标 14×14（Margin 0,0,4,16）+ 断点 14 命中/8 圆点（Margin 0,14,4,0）----
                if (node.HasMissingRequired && _textVisible)
                {
                    var missingRect = new Rect(bounds.Right - 18, bounds.Bottom - 30, 14, 14);
                    DrawGlyphCentered(dc, "\uf06a", missingRect, 10,
                        disabled ? Fade(Palette.Warning) : Palette.Warning);
                }

                bool breakpointOn = model?.IsBreakpoint == true;
                var bpCenter = new Point(bounds.Right - 11, bounds.Y + 21);
                Brush bpBrush = breakpointOn ? Palette.Breakpoint : Palette.Canceled;
                if (disabled) bpBrush = Fade(bpBrush);
                dc.DrawEllipse(breakpointOn ? bpBrush : null, FrozenPenCached(bpBrush, 1.2), bpCenter, 4, 4);

                // ---- 序号胶囊：左上角 −7,−7、min 16×14、圆角 4、Consolas 8 Bold ----
                if (!string.IsNullOrEmpty(node.OrderLabel))
                {
                    Brush orderText = disabled ? Fade(Palette.OnOrderBadgeText) : Palette.OnOrderBadgeText;
                    var orderTextFmt = GetText(node.OrderLabel, 8, bold: true, orderText, 60, SurfaceFont.Mono);
                    double badgeWidth = Math.Max(16, orderTextFmt.Width + 6);
                    var orderRect = new Rect(bounds.X - 7, bounds.Y - 7, badgeWidth, 14);
                    dc.DrawRoundedRectangle(disabled ? Fade(Palette.OrderBadgeFill) : Palette.OrderBadgeFill,
                        null, orderRect, 4, 4);
                    dc.DrawText(orderTextFmt, new Point(
                        orderRect.X + (badgeWidth - orderTextFmt.Width) / 2,
                        orderRect.Y + (14 - orderTextFmt.Height) / 2));
                }

                // ---- 外链角标 12 高（左下，Margin 10,0,0,2）：全局变量/常量绑定数 ----
                if (node.ExternalLinkCount > 0)
                {
                    Brush extText = disabled ? Fade(Palette.ExtBadgeText) : Palette.ExtBadgeText;
                    var extFmt = GetText(node.ExternalLinkCount.ToString(), 8, bold: true, extText, 40, SurfaceFont.Mono);
                    double extWidth = extFmt.Width + 8;
                    var extRect = new Rect(bounds.X + 10, bounds.Bottom - 14, extWidth, 12);
                    dc.DrawRoundedRectangle(disabled ? Fade(Palette.ExtBadgeFill) : Palette.ExtBadgeFill,
                        Palette.ExtBadgePen, extRect, 6, 6);
                    dc.DrawText(extFmt, new Point(extRect.X + 4, extRect.Y + (12 - extFmt.Height) / 2));
                }

                // ---- 耗时（右下，Margin 0,0,8,5，Consolas 8）：运行中实时、结束后冻结 ----
                string? timeText = null;
                Brush timeBrush = subBrush;
                bool timeBold = false;
                if (model?.IsRunningFocus == true)
                {
                    timeText = $"{model.CurrentRunTimeMs:0.#} ms";
                    timeBrush = disabled ? Fade(Palette.RunningText) : Palette.RunningText;
                    timeBold = true;
                }
                else if (model?.LastRunTimeMs > 0)
                {
                    timeText = $"{model.LastRunTimeMs:0.#} ms";
                }

                if (_textVisible && !string.IsNullOrEmpty(timeText))
                {
                    var timeFmt = GetText(timeText, 8, timeBold, timeBrush, 60, SurfaceFont.Mono);
                    dc.DrawText(timeFmt, new Point(bounds.Right - 8 - timeFmt.Width, bounds.Bottom - 5 - timeFmt.Height));
                }

                // ---- 备注行：卡片下缘外 2px，宽 = 卡片宽，10px 灰字（有注释才画）----
                if (_textVisible && !string.IsNullOrEmpty(node.Comment))
                {
                    var commentFmt = GetText(node.Comment, 10, bold: false, subBrush, bounds.Width);
                    dc.DrawText(commentFmt, new Point(bounds.X, bounds.Bottom + 2));
                }
            }
            catch
            {
                // 自绘层不许因为单个节点把整帧打断（缺 Model / 字体缺字形都可能抛）
            }
        }

        /// <summary>
        /// 圆角胶囊 + 居中文字，返回右边缘。用于类型徽标 / 操作徽标 / 泳道徽标这类 14~20 高的小件。
        /// </summary>
        private double DrawChip(
            DrawingContext dc,
            string? text,
            double left,
            double top,
            double height,
            double padX,
            double radius,
            Brush fill,
            Brush? border,
            Brush textBrush,
            SurfaceFont font,
            bool bold,
            double fontSize,
            double maxWidth = 200)
        {
            if (string.IsNullOrEmpty(text)) return left;

            var formatted = GetText(text, fontSize, bold, textBrush, maxWidth, font);
            double width = Math.Min(maxWidth, formatted.Width) + padX * 2;
            var rect = new Rect(left, top, width, height);

            dc.DrawRoundedRectangle(fill, border is null ? null : FrozenPenCached(border, 1), rect, radius, radius);
            dc.DrawText(formatted, new Point(left + padX, top + (height - formatted.Height) / 2));
            return rect.Right;
        }

        /// <summary>图标字形居中画进一个矩形（FA 字形自带留白，靠实测宽度居中）。</summary>
        private void DrawGlyphCentered(DrawingContext dc, string glyph, Rect box, double size, Brush brush)
        {
            var formatted = GetText(glyph, size, bold: false, brush, Math.Max(1, box.Width), SurfaceFont.Icon);
            dc.DrawText(formatted, new Point(
                box.X + (box.Width - formatted.Width) / 2,
                box.Y + (box.Height - formatted.Height) / 2));
        }

        private static readonly Dictionary<(Brush, double), Pen> PenCache = new();

        /// <summary>按（画刷, 线宽）缓存画笔：OnRender 里逐节点 new Pen 会让每帧产生上千个对象。</summary>
        private static Pen FrozenPenCached(Brush brush, double thickness)
        {
            var key = (brush, thickness);
            if (PenCache.TryGetValue(key, out var cached)) return cached;

            var pen = new Pen(brush, thickness);
            pen.Freeze();
            PenCache[key] = pen;
            return pen;
        }

        private static readonly VisionMaster.Helpers.CategorySoftBrushConverter CategorySoftConverter = new();
        private static readonly VisionMaster.Helpers.CategoryBorderBrushConverter CategoryBorderConverter = new();
        private static readonly VisionMaster.Helpers.CategoryAccentBrushConverter CategoryAccentConverter = new();

        // 分类色三件套**复用工具箱那三个转换器**，不另写一份混色公式——
        // 两份公式迟早会漂（soft = +88% 白、border = +65% 白）。
        private static Brush CategorySoftBrush(string? category)
            => (Brush)CategorySoftConverter.Convert(category, typeof(Brush), null, CultureInfo.CurrentUICulture);

        private static Brush CategoryBorderBrush(string? category)
            => (Brush)CategoryBorderConverter.Convert(category, typeof(Brush), null, CultureInfo.CurrentUICulture);

        private static Brush CategoryAccentBrush(string? category)
            => (Brush)CategoryAccentConverter.Convert(category, typeof(Brush), null, CultureInfo.CurrentUICulture);

        // ------------------------------------------------------------------
        //  连线绘制
        // ------------------------------------------------------------------

        private void DrawLink(DrawingContext dc, in LinkVisual link)
        {
            // 交叉断口：先用画布底色的粗笔垫一层（OutlineBrush 1.6），线条互相穿过处出现断口，
            // 谁在上谁下一眼可辨——XAML 里是 StepConnection 的 OutlineBrush/OutlineThickness。
            dc.DrawGeometry(null, link.OutlinePen, link.Geometry);
            dc.DrawGeometry(null, link.Pen, link.Geometry);

            if (link.Arrow is not null)
                dc.DrawGeometry(link.Pen.Brush, null, link.Arrow);
            // 标签不在这里画：它必须画在所有卡片之上，否则会被卡片切掉半截（见 DrawLinkLabels）。
        }

        /// <summary>
        /// 连线标签单独一趟，画在**所有卡片之上**。
        /// 之前跟连线一起画（连线在卡片下面），标签落在卡片边缘时被切掉一半——
        /// 用户截图里"文字被遮挡"就是这条。位置还会避开卡片：中点压到卡上就上下挪。
        /// </summary>
        private void DrawLinkLabels(DrawingContext dc, in Rect visible)
        {
            var placed = new List<Rect>(16);
            var links = _drawLinks;
            for (int i = 0; i < links.Length; i++)
            {
                var link = links[i];
                if (link.Label.Length == 0) continue;
                if (!visible.IntersectsWith(link.Bounds)) continue;

                var labelText = GetText(link.Label, 10.5, bold: link.LabelBold, link.LabelBrush, 180);
                double width = labelText.Width + 10;
                double height = labelText.Height + 2;
                var box = PlaceLabel(link.Mid, width, height, placed);
                placed.Add(box);

                dc.DrawRoundedRectangle(Palette.LinkLabelFill, null, box, 4, 4);
                dc.DrawText(labelText, new Point(box.X + 5, box.Y + 1));
            }
        }

        /// <summary>
        /// 标签位置：以折线中点为基准，**先避卡片、再避已放下的标签**。
        /// 只避卡片不够——绕行走线会把多个标签挤到同一条横带上，互相压成一团（2026-10-25 实拍）。
        /// </summary>
        private Rect PlaceLabel(Point mid, double width, double height, List<Rect> placed)
        {
            var center = new Rect(mid.X - width / 2, mid.Y - height / 2, width, height);
            if (IsLabelFree(center, placed)) return center;

            double step = height + 4;
            for (int k = 1; k <= 6; k++)
            {
                var down = new Rect(center.X, center.Y + step * k, width, height);
                if (IsLabelFree(down, placed)) return down;

                var up = new Rect(center.X, center.Y - step * k, width, height);
                if (IsLabelFree(up, placed)) return up;
            }

            // 纵向找不到位置就左右挪（半宽、整宽各试一次）
            double[] dx = { width * 0.5, -width * 0.5, width, -width };
            for (int i = 0; i < dx.Length; i++)
            {
                var side = new Rect(center.X + dx[i], center.Y, width, height);
                if (IsLabelFree(side, placed)) return side;
            }

            return center;
        }

        private bool IsLabelFree(Rect box, List<Rect> placed)
        {
            if (HitsBlockedArea(box)) return false;

            for (int i = 0; i < placed.Count; i++)
            {
                if (placed[i].IntersectsWith(box)) return false;
            }

            return true;
        }

        /// <summary>
        /// 标签不许压的地方：步骤卡片整块，以及容器的**组头**（标题/条件胶囊都在那 46 像素里，
        /// 压上去等于把容器名盖住）。泳道底色与容器体允许压。
        /// </summary>
        private bool HitsBlockedArea(Rect box)
        {
            var nodes = _drawNodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                var rect = nodes[i].Rect;
                if (rect.Bounds.IsEmpty) continue;

                if (rect.Kind == FlowSurfaceKind.Step)
                {
                    if (rect.Bounds.IntersectsWith(box)) return true;
                }
                else if (rect.Kind == FlowSurfaceKind.Container)
                {
                    var header = new Rect(
                        rect.Bounds.X,
                        rect.Bounds.Y,
                        rect.Bounds.Width,
                        Math.Min(FlowCanvasViewModel.FrameHeaderHeight, rect.Bounds.Height));
                    if (header.IntersectsWith(box)) return true;
                }
            }

            return false;
        }

        private bool HitsStep(Rect box)
        {
            var nodes = _drawNodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].Rect.Kind != FlowSurfaceKind.Step) continue;
                if (nodes[i].Rect.Bounds.IntersectsWith(box)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        //  绘制小工具
        // ------------------------------------------------------------------

        /// <summary>
        /// 画"只有上方圆角、下方直角"的头带：圆角矩形 + 底部一条方角矩形补平。
        /// 这样不需要 PushClip（每次渲染新建 Geometry），也就没有裁剪几何要缓存。
        /// </summary>
        private static void DrawTopBand(DrawingContext dc, Rect band, double radius, Brush fill)
        {
            double r = Math.Min(radius, Math.Min(band.Width / 2, band.Height));
            dc.DrawRoundedRectangle(fill, null, band, r, r);
            if (r > 0.5)
                dc.DrawRectangle(fill, null, new Rect(band.X, band.Bottom - r, band.Width, r));
        }

        private static double ClampRadius(double radius, Rect bounds)
            => Math.Max(0, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));

        /// <summary>左对齐、垂直居中画一行文字；maxWidth 不够就整行省略。</summary>
        private void DrawCenteredText(
            DrawingContext dc,
            string? text,
            double left,
            double centerY,
            double size,
            bool bold,
            Brush brush,
            double maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 2) return;

            var formatted = GetText(text, size, bold, brush, maxWidth);
            dc.DrawText(formatted, new Point(left, centerY - formatted.Height / 2));
        }

        /// <summary>执行序号角标：圆角底 + 居中小字（label 为空不画）。</summary>
        private void DrawOrderBadge(
            DrawingContext dc,
            double left,
            double top,
            string? label,
            double width,
            double height)
        {
            if (string.IsNullOrEmpty(label)) return;

            dc.DrawRoundedRectangle(Palette.OrderBadgeFill, null, new Rect(left, top, width, height), 5, 5);

            var text = GetText(label, 9, bold: true, Palette.OnOrderBadgeText, width - 2);
            dc.DrawText(text, new Point(
                left + (width - text.Width) / 2,
                top + (height - text.Height) / 2));
        }

        private static string ResolveIconText(CanvasNodeViewModel node)
        {
            var badge = node.OpBadge;
            if (!string.IsNullOrEmpty(badge)) return badge;

            var subtitle = node.SubTitle;
            if (!string.IsNullOrEmpty(subtitle))
            {
                // 首字；代理对（罕见）取两个 char，免得画出半个字
                return char.IsHighSurrogate(subtitle[0]) && subtitle.Length > 1
                    ? subtitle.Substring(0, 2)
                    : subtitle.Substring(0, 1);
            }

            return node.TypeBadge;
        }

        private static Brush StateDotBrush(CanvasNodeViewModel node, bool faulted)
        {
            if (faulted) return Palette.StateFailed;

            return node.Model?.State switch
            {
                StepState.Running => Palette.StateRunning,
                StepState.Success => Palette.StateSuccess,
                StepState.Failed => Palette.StateFailed,
                StepState.Skipped => Palette.StateSkipped,
                _ => Palette.StateIdle,
            };
        }

        // ------------------------------------------------------------------
        //  文字排版缓存
        // ------------------------------------------------------------------

        /// <summary>
        /// 取一行 FormattedText（按 文本 / 字号 / 粗细 / 最大宽 / 画刷 缓存）。
        /// 只允许在 UI 线程用；DPI 变了由 OnRender 整表清空。
        /// </summary>
        private FormattedText GetText(
            string? text,
            double size,
            bool bold,
            Brush brush,
            double maxWidth,
            SurfaceFont font = SurfaceFont.Ui,
            bool strike = false)
        {
            string singleLine = NormalizeSingleLine(text);
            double width = double.IsFinite(maxWidth) ? Math.Max(1, Math.Round(maxWidth)) : 1;
            var key = new TextKey(singleLine, size, bold, width, brush, font, strike);

            if (_textCache.TryGetValue(key, out var cached)) return cached;

            var formatted = new FormattedText(
                singleLine,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                ResolveTypeface(font, bold),
                size,
                brush,
                _pixelsPerDip)
            {
                MaxTextWidth = width,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };

            // 禁用态标题的删除线：与 XAML 的 TextDecorations="Strikethrough" 同一语义
            if (strike) formatted.SetTextDecorations(TextDecorations.Strikethrough);

            if (_textCache.Count >= TextCacheLimit) _textCache.Clear();
            _textCache[key] = formatted;
            return formatted;
        }

        private Typeface ResolveTypeface(SurfaceFont font, bool bold) => font switch
        {
            SurfaceFont.Mono => bold ? MonoBoldTypeface : MonoTypeface,
            SurfaceFont.Icon => IconTypeface,
            _ => bold ? _semiBoldTypeface : _regularTypeface,
        };

        private static string NormalizeSingleLine(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0) return text;

            return text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        }

        // ------------------------------------------------------------------
        //  快照
        // ------------------------------------------------------------------

        private void RebuildSnapshot()
        {
            var vm = _viewModel;
            if (vm is null)
            {
                _nodes = Array.Empty<FlowNodeRect>();
                _links = Array.Empty<FlowLinkLine>();
                _drawNodes = Array.Empty<NodeVisual>();
                _drawLinks = Array.Empty<LinkVisual>();
                _contentBounds = Rect.Empty;
                return;
            }

            var nodes = new List<FlowNodeRect>(vm.Nodes.Count);
            var drawNodes = new List<NodeVisual>(vm.Nodes.Count);
            var content = Rect.Empty;

            for (int i = 0; i < vm.Nodes.Count; i++)
            {
                var node = vm.Nodes[i];
                if (node is null) continue;   // 防御：集合类型非空，但宿主可能塞 null

                var bounds = NodeBounds(node);
                var rect = new FlowNodeRect(ToSurfaceKind(node.Kind), bounds, node.ZOrder, node);

                nodes.Add(rect);
                drawNodes.Add(new NodeVisual(rect, node, i));

                if (bounds.IsEmpty) continue;
                if (content.IsEmpty) content = bounds;
                else content.Union(bounds);
            }

            drawNodes.Sort(CompareDrawOrder);

            // 走线避让用的障碍：步骤卡片 + 容器框。
            // 容器必须进来——目标锚点在容器左缘时，横段会从组头横穿过去，
            // 剩下那 60px 入口段就变成"左边凸出来的一截线"（2026-10-25 用户截图）。
            var obstacles = new List<(Rect Bounds, bool IsContainer)>(drawNodes.Count);
            for (int i = 0; i < drawNodes.Count; i++)
            {
                var rect = drawNodes[i].Rect;
                if (rect.Bounds.IsEmpty) continue;

                if (rect.Kind == FlowSurfaceKind.Step) obstacles.Add((rect.Bounds, false));
                else if (rect.Kind == FlowSurfaceKind.Container) obstacles.Add((rect.Bounds, true));
            }

            var links = new List<FlowLinkLine>(vm.Links.Count);
            var drawLinks = new List<LinkVisual>(vm.Links.Count);

            for (int i = 0; i < vm.Links.Count; i++)
            {
                var link = vm.Links[i];
                if (link is null) continue;

                var line = new FlowLinkLine(
                    link.SourceAnchor,
                    link.TargetAnchor,
                    link.SourcePosition,
                    link.TargetPosition,
                    link.IsOrderLink,
                    link.IsIllegal,
                    link.IsExecuted);

                links.Add(line);
                drawLinks.Add(BuildDrawLink(link, line, obstacles));
            }

            _nodes = nodes.ToArray();
            _links = links.ToArray();
            _drawNodes = drawNodes.ToArray();
            _drawLinks = drawLinks.ToArray();
            _contentBounds = content;
        }

        private static int CompareDrawOrder(NodeVisual a, NodeVisual b)
        {
            int byZ = a.Rect.ZOrder.CompareTo(b.Rect.ZOrder);
            return byZ != 0 ? byZ : a.SourceIndex.CompareTo(b.SourceIndex);
        }

        private static Rect NodeBounds(CanvasNodeViewModel node)
        {
            var location = node.Location;
            double x = Finite(location.X);
            double y = Finite(location.Y);

            // 步骤盒固定 156×58；容器 / 泳道用布局器算好的框尺寸
            if (node.Kind == CanvasNodeKind.Step)
                return new Rect(x, y, FlowCanvasViewModel.NodeWidth, FlowCanvasViewModel.NodeHeight);

            return new Rect(x, y, Math.Max(0, Finite(node.LaneWidth)), Math.Max(0, Finite(node.LaneHeight)));
        }

        private static double Finite(double value) => double.IsFinite(value) ? value : 0;

        private static FlowSurfaceKind ToSurfaceKind(CanvasNodeKind kind) => kind switch
        {
            CanvasNodeKind.Container => FlowSurfaceKind.Container,
            CanvasNodeKind.Lane => FlowSurfaceKind.Lane,
            _ => FlowSurfaceKind.Step,
        };

        private static LinkVisual BuildDrawLink(
            CanvasLinkViewModel link,
            in FlowLinkLine line,
            IReadOnlyList<(Rect Bounds, bool IsContainer)> obstacles)
        {
            // 走线必须与 XAML 的 nodify:StepConnection 一致：**纯直角台阶**，不是曲线。
            // 曲线会把"两端都在屏外、只是连线跨过视口"的线拉成一道横穿全屏的弧。
            var points = BuildStepRoute(line, obstacles);

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(points[0], isFilled: false, isClosed: false);
                for (int i = 1; i < points.Count; i++)
                    context.LineTo(points[i], isStroked: true, isSmoothJoin: false);
            }
            geometry.Freeze();

            // 标签落在折线"按弧长一半"的位置：直角走线的几何中点经常落在拐角外的空白处
            var mid = MidpointAlong(points);

            var bounds = new Rect(points[0], points[0]);
            foreach (var p in points) bounds.Union(p);
            bounds.Inflate(24, 24);

            var pen = ResolveLinkPen(line);

            return new LinkVisual(
                geometry,
                pen,
                FrozenPenCached(Palette.LinkOutline, pen.Thickness + 1.6 * 2),
                BuildArrow(line.Target, line.TargetSide),
                mid,
                link.LinkLabel ?? string.Empty,
                line.IsIllegal ? Palette.FailedText : line.IsOrder ? Palette.OrderLinkLabelText : Palette.LinkLabelText,
                !line.IsOrder,
                bounds);
        }

        /// <summary>
        /// 台阶式正交走线。**关键约束：横段不许从卡片身上横穿过去**。
        /// 源在目标右侧时（单列流程里这是常态），"先竖后横"的走法会让横段落在目标卡片中心高度上，
        /// 卡片看上去像穿在线上（2026-10-25 用户截图指出的问题）。
        /// 这里检测到横段会压到目标卡时，改从卡片上方绕：出一段 → 抬到卡顶上方 → 横过去 → 落回 → 进卡。
        /// </summary>
        private static List<Point> BuildStepRoute(in FlowLinkLine line, IReadOnlyList<(Rect Bounds, bool IsContainer)> obstacles)
        {
            var source = line.Source;
            var target = line.Target;
            var outVector = SidePush(line.SourceSide);
            var inVector = SidePush(line.TargetSide);

            var a = source + outVector;   // 源侧出线段末端
            var b = target + inVector;    // 目标侧入线段起点

            var points = new List<Point>(6) { source, a };

            bool sourceHorizontal = Math.Abs(outVector.X) > Math.Abs(outVector.Y);

            if (sourceHorizontal)
            {
                // 横段会从卡片/容器身上穿过去吗？（源与目标分列两侧时必然如此）
                var crossing = new Rect(
                    Math.Min(a.X, b.X),
                    target.Y - 1,
                    Math.Abs(a.X - b.X),
                    2);

                var hit = FindBlockingRect(crossing, obstacles, source, target);
                if (hit is { } blocker)
                {
                    // 从障碍上方绕：抬到顶边再横过去，最后在目标左侧落回
                    double laneY = blocker.Y - 14;
                    points.Add(new Point(a.X, laneY));
                    points.Add(new Point(b.X, laneY));
                    points.Add(new Point(b.X, target.Y));
                    points.Add(target);
                    return points;
                }

                if (Math.Abs(a.Y - b.Y) > 0.01) points.Add(new Point(a.X, b.Y));
                if (Math.Abs(a.X - b.X) > 0.01) points.Add(new Point(b.X, b.Y));
            }
            else
            {
                if (Math.Abs(a.X - b.X) > 0.01) points.Add(new Point(b.X, a.Y));
                if (Math.Abs(a.Y - b.Y) > 0.01) points.Add(new Point(b.X, b.Y));
            }

            points.Add(target);
            return points;
        }

        /// <summary>
        /// 找第一个与给定细带相交的障碍。
        /// 跳过规则：① 源所在的卡片（线从它的右缘正常出发）；
        /// ② **包含目标的容器**——目标是容器内部的某个步骤时，线本来就要进到框里，
        ///    绕整框是荒谬的；但目标**就是**那个容器（锚点在它自己的左右缘）时必须绕。
        /// </summary>
        private static Rect? FindBlockingRect(
            Rect band,
            IReadOnlyList<(Rect Bounds, bool IsContainer)> obstacles,
            Point source,
            Point target)
        {
            Rect? best = null;

            for (int i = 0; i < obstacles.Count; i++)
            {
                var rect = obstacles[i].Bounds;
                if (rect.IsEmpty || !rect.IntersectsWith(band)) continue;
                if (ContainsEdge(rect, source)) continue;

                if (obstacles[i].IsContainer && ContainsStrictly(rect, target)) continue;

                if (best is null || rect.Y < best.Value.Y) best = rect;
            }

            return best;
        }

        /// <summary>点在矩形内部（不含边界容差）：用来判断"目标是这个容器的子孙"。</summary>
        private static bool ContainsStrictly(Rect rect, Point point)
        {
            const double inset = 2;
            return point.X > rect.X + inset && point.X < rect.Right - inset
                && point.Y > rect.Y + inset && point.Y < rect.Bottom - inset;
        }

        private static bool ContainsEdge(Rect rect, Point point)
        {
            const double tolerance = 2;
            return point.X >= rect.X - tolerance && point.X <= rect.Right + tolerance
                && point.Y >= rect.Y - tolerance && point.Y <= rect.Bottom + tolerance;
        }

        /// <summary>按弧长取折线中点（标签锚点）。</summary>
        private static Point MidpointAlong(List<Point> points)
        {
            double total = 0;
            for (int i = 1; i < points.Count; i++) total += (points[i] - points[i - 1]).Length;
            if (total < 0.01) return points[0];

            double half = total / 2;
            double walked = 0;
            for (int i = 1; i < points.Count; i++)
            {
                var segment = points[i] - points[i - 1];
                double length = segment.Length;
                if (walked + length >= half)
                {
                    double t = (half - walked) / Math.Max(0.0001, length);
                    return points[i - 1] + segment * t;
                }
                walked += length;
            }

            return points[^1];
        }

        /// <summary>
        /// 目标端箭头：Nodify 的 StepConnection 由 Fill 画一个实心三角，尖端落在锚点上。
        /// 方向按目标侧的"朝内法向"——Left 侧箭头朝 +X，Top 侧朝 +Y。
        /// </summary>
        private static Geometry BuildArrow(Point tip, ConnectorPosition side)
        {
            Vector direction = side switch
            {
                ConnectorPosition.Left => new Vector(1, 0),
                ConnectorPosition.Right => new Vector(-1, 0),
                ConnectorPosition.Top => new Vector(0, 1),
                _ => new Vector(0, -1),
            };

            var normal = new Vector(-direction.Y, direction.X);
            const double length = 9;
            const double halfWidth = 4;
            var baseCenter = tip - direction * length;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(tip, isFilled: true, isClosed: true);
                ctx.LineTo(baseCenter + normal * halfWidth, true, false);
                ctx.LineTo(baseCenter - normal * halfWidth, true, false);
            }
            geometry.Freeze();
            return geometry;
        }

        private static Pen ResolveLinkPen(in FlowLinkLine line)
        {
            // 优先级：非法 > 已执行 > 顺序链 > 普通数据线（顺序链走完就从灰虚线换成运行色实线）
            if (line.IsIllegal) return Palette.IllegalLinkPen;
            if (line.IsExecuted) return Palette.ExecutedLinkPen;
            if (line.IsOrder) return Palette.OrderLinkPen;
            return Palette.DataLinkPen;
        }

        private static Vector SidePush(ConnectorPosition side)
        {
            switch (side)
            {
                case ConnectorPosition.Left: return new Vector(-LinkControlPush, 0);
                case ConnectorPosition.Top: return new Vector(0, -LinkControlPush);
                case ConnectorPosition.Bottom: return new Vector(0, LinkControlPush);
                case ConnectorPosition.Right:
                default: return new Vector(LinkControlPush, 0);
            }
        }

        private void UpdateTransform()
        {
            var transform = new MatrixTransform(new Matrix(_zoom, 0, 0, _zoom, _pan.X, _pan.Y));
            transform.Freeze();
            _transform = transform;
        }

        // ------------------------------------------------------------------
        //  绘制快照的数据结构
        // ------------------------------------------------------------------

        private readonly struct NodeVisual
        {
            public NodeVisual(FlowNodeRect rect, CanvasNodeViewModel node, int sourceIndex)
            {
                Rect = rect;
                Node = node;
                SourceIndex = sourceIndex;
            }

            public readonly FlowNodeRect Rect;
            public readonly CanvasNodeViewModel Node;

            /// <summary>在 VM 的 Nodes 集合里的下标（同 ZOrder 时的稳定次序）</summary>
            public readonly int SourceIndex;
        }

        private readonly struct LinkVisual
        {
            public LinkVisual(
                StreamGeometry geometry,
                Pen pen,
                Pen outlinePen,
                Geometry? arrow,
                Point mid,
                string label,
                Brush labelBrush,
                bool labelBold,
                Rect bounds)
            {
                Geometry = geometry;
                Pen = pen;
                OutlinePen = outlinePen;
                Arrow = arrow;
                Mid = mid;
                Label = label;
                LabelBrush = labelBrush;
                LabelBold = labelBold;
                Bounds = bounds;
            }

            public readonly StreamGeometry Geometry;
            public readonly Pen Pen;

            /// <summary>交叉断口：画布底色的粗笔垫底，线条穿过处出现断口（XAML 的 OutlineBrush + OutlineThickness 1.6）</summary>
            public readonly Pen OutlinePen;

            /// <summary>目标端箭头（Nodify 的 Fill 填充三角）；无箭头语义时为 null</summary>
            public readonly Geometry? Arrow;

            public readonly Point Mid;
            public readonly string Label;
            public readonly Brush LabelBrush;

            /// <summary>数据线标签 SemiBold、顺序链标签 Normal（与 ModuleLinkTemplate 一致）</summary>
            public readonly bool LabelBold;

            /// <summary>裁剪用的外接框（两端点并集 + 控制点外推余量）</summary>
            public readonly Rect Bounds;
        }

        private readonly record struct TextKey(string Text, double Size, bool Bold, double MaxWidth, Brush Foreground, SurfaceFont Font, bool Strike);

        // ------------------------------------------------------------------
        //  颜色 / 画笔表
        // ------------------------------------------------------------------

        /// <summary>
        /// 本文件**不引用任何资源键**（无 Application 的断言宿主解析不到 StaticResource），
        /// 每个字段注释里标出 UI\Controls\Themes\Colors.xaml 的键名与十六进制；改色以那份文件为准。
        /// 全部 Freeze：跨帧复用只读对象，不进 Freezable 的变更追踪。
        /// </summary>
        private static class Palette
        {
            // ---- 契约点名的 8 个键 ----
            public static readonly SolidColorBrush NodeFill = Frozen(0xFF, 0xFF, 0xFF, 0xFF);      // FlowCanvasNodeBackgroundBrush #FFFFFF
            public static readonly SolidColorBrush NodeBorder = Frozen(0xFF, 0xC9, 0xD2, 0xDE);    // FlowCanvasNodeBorderBrush #C9D2DE
            public static readonly SolidColorBrush StateRunning = Frozen(0xFF, 0x25, 0x63, 0xEB);  // FlowCanvasStateRunningBrush #2563EB
            public static readonly SolidColorBrush StateFailed = Frozen(0xFF, 0xEF, 0x44, 0x44);   // FlowCanvasStateFailedBrush #EF4444
            public static readonly SolidColorBrush Link = Frozen(0xFF, 0x3B, 0x82, 0xF6);          // FlowCanvasLinkBrush #3B82F6
            public static readonly SolidColorBrush OrderLink = Frozen(0xFF, 0x94, 0xA3, 0xB8);     // FlowCanvasOrderLinkBrush #94A3B8
            public static readonly SolidColorBrush IllegalLink = Frozen(0xFF, 0xEF, 0x44, 0x44);   // FlowCanvasLinkIllegalBrush #EF4444
            public static readonly SolidColorBrush Running = Frozen(0xFF, 0x3B, 0x82, 0xF6);       // FlowCanvasRunningBrush #3B82F6

            // ---- 步骤卡片 ----
            public static readonly SolidColorBrush NodeFailedFill = Frozen(0xFF, 0xFF, 0xF7, 0xF7);  // FlowCanvasNodeFailedBackgroundBrush #FFF7F7
            public static readonly SolidColorBrush NodeText = Frozen(0xFF, 0x0F, 0x17, 0x2A);        // FlowCanvasNodeTextBrush #0F172A
            public static readonly SolidColorBrush SubText = Frozen(0xFF, 0x94, 0xA3, 0xB8);         // FlowCanvasNodeSubTextBrush #94A3B8
            public static readonly SolidColorBrush IconText = Frozen(0xFF, 0x3B, 0x82, 0xF6);        // FlowCanvasNodeIconBrush #3B82F6
            public static readonly SolidColorBrush IconFill = Frozen(0x1F, 0x3B, 0x82, 0xF6);        // FlowCanvasNodeIconBrush #3B82F6（12% 浅底）
            public static readonly SolidColorBrush IconBorder = Frozen(0x33, 0x3B, 0x82, 0xF6);      // FlowCanvasNodeIconBrush #3B82F6（20% 描边）

            // ---- 状态点 ----
            public static readonly SolidColorBrush StateIdle = Frozen(0xFF, 0xA8, 0xAB, 0xB2);     // FlowCanvasStateIdleBrush #A8ABB2
            public static readonly SolidColorBrush StateSuccess = Frozen(0xFF, 0x16, 0xA3, 0x4A);  // FlowCanvasStateSuccessBrush #16A34A
            public static readonly SolidColorBrush StateSkipped = Frozen(0xFF, 0xCB, 0xD5, 0xE1);  // FlowCanvasStateSkippedBrush #CBD5E1

            // ---- 容器框 ----
            public static readonly SolidColorBrush ContainerFill = Frozen(0xFF, 0xFF, 0xFF, 0xFF);        // FlowCanvasNodeBackgroundBrush #FFFFFF
            public static readonly SolidColorBrush ContainerFailedFill = Frozen(0xF2, 0xFE, 0xF2, 0xF2);  // FlowCanvasContainerFailedBackgroundBrush #FEF2F2
            public static readonly SolidColorBrush FrameBorder = Frozen(0xFF, 0xA5, 0xC0, 0xE8);          // FlowCanvasFrameBorderBrush #A5C0E8
            public static readonly SolidColorBrush FrameHeaderFill = Frozen(0xFF, 0xEE, 0xF4, 0xFF);      // FlowCanvasFrameHeaderBrush #EEF4FF
            public static readonly SolidColorBrush FailedSoftFill = Frozen(0xFF, 0xFE, 0xF2, 0xF2);       // FlowCanvasFailedSoftBrush #FEF2F2
            public static readonly SolidColorBrush FailedBorder = Frozen(0xFF, 0xFE, 0xCA, 0xCA);         // FlowCanvasFailedBorderBrush #FECACA
            public static readonly SolidColorBrush FailedText = Frozen(0xFF, 0xB9, 0x1C, 0x1C);           // FlowCanvasFailedTextBrush #B91C1C

            // ---- 徽标 ----
            public static readonly SolidColorBrush TypeBadge = Frozen(0xFF, 0x3B, 0x5B, 0x8C);        // FlowCanvasTypeBadgeBrush #3B5B8C
            public static readonly SolidColorBrush OnTypeBadgeText = Frozen(0xFF, 0xFF, 0xFF, 0xFF);  // FlowCanvasOnTypeBadgeTextBrush #FFFFFF
            public static readonly SolidColorBrush OrderBadgeFill = Frozen(0xFF, 0x0F, 0x17, 0x2A);   // FlowCanvasOrderBadgeBrush #0F172A
            public static readonly SolidColorBrush OnOrderBadgeText = Frozen(0xFF, 0xFF, 0xFF, 0xFF); // FlowCanvasOnOrderBadgeTextBrush #FFFFFF

            // ---- 泳道 ----
            public static readonly SolidColorBrush LaneFill = Frozen(0x0F, 0x3B, 0x82, 0xF6);         // FlowCanvasLaneTintBrush #0F3B82F6
            public static readonly SolidColorBrush LaneBorder = Frozen(0x26, 0x4B, 0x82, 0xF6);       // FlowCanvasLaneBorderBrush #264B82F6
            public static readonly SolidColorBrush LaneHeaderFill = Frozen(0xB3, 0xFF, 0xFF, 0xFF);   // FlowCanvasLaneHeaderBrush #B3FFFFFF
            public static readonly SolidColorBrush LaneText = Frozen(0xFF, 0x5A, 0x6C, 0x99);         // FlowCanvasLaneTextBrush #5A6C99

            // ---- 连线标签 ----
            public static readonly SolidColorBrush LinkLabelFill = Frozen(0xF2, 0xFF, 0xFF, 0xFF);     // FlowCanvasLinkLabelBackgroundBrush #F2FFFFFF
            public static readonly SolidColorBrush LinkLabelText = Frozen(0xFF, 0x1D, 0x4E, 0xD8);     // FlowCanvasLinkLabelTextBrush #1D4ED8
            public static readonly SolidColorBrush OrderLinkLabelText = Frozen(0xFF, 0x64, 0x74, 0x8B);// FlowCanvasOrderLinkLabelTextBrush #64748B

            // ---- 画笔（笔必须先于引用它的字段声明：静态字段按文本顺序初始化）----

            public static readonly Pen StepBorderPen = FrozenPen(NodeBorder, 1);
            public static readonly Pen StepRunningPen = FrozenPen(StateRunning, 1);
            public static readonly Pen StepFailedPen = FrozenPen(StateFailed, 1);
            public static readonly Pen StepSelectedPen = FrozenPen(StateRunning, 2);

            public static readonly Pen IconBorderPen = FrozenPen(IconBorder, 1);

            public static readonly Pen ContainerBorderPen = FrozenPen(FrameBorder, 1.5);
            public static readonly Pen ContainerFailedPen = FrozenPen(StateFailed, 1.5);
            public static readonly Pen ContainerSelectedPen = FrozenPen(StateRunning, 2);
            public static readonly Pen FrameHeaderLinePen = FrozenPen(FrameBorder, 1);
            public static readonly Pen FailedLinePen = FrozenPen(FailedBorder, 1);

            public static readonly Pen LaneBorderPen = FrozenPen(LaneBorder, 1);

            public static readonly Pen DataLinkPen = FrozenPen(Link, 1.5);
            public static readonly Pen OrderLinkPen = FrozenPen(OrderLink, 1.2, dashed: true);
            public static readonly Pen IllegalLinkPen = FrozenPen(IllegalLink, 1.5, dashed: true);
            public static readonly Pen ExecutedLinkPen = FrozenPen(Running, 1.4);
            public static readonly Pen LinkLabelBorderPen = FrozenPen(LaneBorder, 1);

            // ---- 以下为"还原当前样式"补齐的令牌，逐条对应 UI\Controls\Themes\Colors.xaml ----
            public static readonly SolidColorBrush CanvasBackground = Frozen(0xFF, 0xF7, 0xF8, 0xFA);   // FlowCanvasBackgroundBrush
            public static readonly SolidColorBrush GridDot = Frozen(0xFF, 0xC9, 0xD2, 0xDE);            // FlowCanvasGridDotBrush
            public static readonly SolidColorBrush FrameHeaderText = Frozen(0xFF, 0x3B, 0x5B, 0x8C);    // FlowCanvasFrameHeaderTextBrush
            public static readonly SolidColorBrush LaneRunningHeaderFill = Frozen(0xFF, 0xEF, 0xF6, 0xFF);
            public static readonly SolidColorBrush RunningText = Frozen(0xFF, 0x1D, 0x4E, 0xD8);
            public static readonly SolidColorBrush LaneNeutralFill = Frozen(0xFF, 0xF8, 0xFA, 0xFC);
            public static readonly SolidColorBrush LaneNeutralBorder = Frozen(0xFF, 0xE2, 0xE8, 0xF0);
            public static readonly SolidColorBrush LaneNeutralBadge = Frozen(0xFF, 0x94, 0xA3, 0xB8);
            public static readonly SolidColorBrush LaneNeutralText = Frozen(0xFF, 0x47, 0x55, 0x69);
            public static readonly SolidColorBrush ExtBadgeFill = Frozen(0xFF, 0xFF, 0xF7, 0xE6);
            public static readonly SolidColorBrush ExtBadgeBorder = Frozen(0xFF, 0xE6, 0xC9, 0x8A);
            public static readonly SolidColorBrush ExtBadgeText = Frozen(0xFF, 0x9A, 0x6B, 0x0F);
            public static readonly SolidColorBrush CollapseFill = Frozen(0xFF, 0xFF, 0xFF, 0xFF);
            public static readonly SolidColorBrush CollapseBorder = Frozen(0xFF, 0xC9, 0xD2, 0xDE);
            public static readonly SolidColorBrush CollapseText = Frozen(0xFF, 0x47, 0x55, 0x69);
            public static readonly SolidColorBrush Warning = Frozen(0xFF, 0xE6, 0xA2, 0x3C);          // FlowCanvasWarningBrush
            public static readonly SolidColorBrush Breakpoint = Frozen(0xFF, 0xF5, 0x6C, 0x6C);
            public static readonly SolidColorBrush Canceled = Frozen(0xFF, 0xC0, 0xC4, 0xCC);
            public static readonly SolidColorBrush LinkOutline = Frozen(0xFF, 0xF7, 0xF8, 0xFA);       // FlowCanvasLinkOutlineBrush
            public static readonly SolidColorBrush SelectedGlow = Frozen(0x33, 0x3B, 0x82, 0xF6);      // FlowCanvasNodeSelectedGlowBrush

            // 五类容器主题（Logic* 令牌）：Soft=浅底/头带渐变起点，Border，Brush=类型条/徽标底，Text
            public static readonly SolidColorBrush IfBrush = Frozen(0xFF, 0x63, 0x66, 0xF1);
            public static readonly SolidColorBrush IfBorder = Frozen(0xFF, 0xC7, 0xD2, 0xFE);
            public static readonly SolidColorBrush IfText = Frozen(0xFF, 0x43, 0x38, 0xCA);
            public static readonly SolidColorBrush IfSoft = Frozen(0xFF, 0xEE, 0xF2, 0xFF);
            public static readonly SolidColorBrush CaseBrush = Frozen(0xFF, 0x8B, 0x5C, 0xF6);
            public static readonly SolidColorBrush CaseBorder = Frozen(0xFF, 0xDD, 0xD6, 0xFE);
            public static readonly SolidColorBrush CaseText = Frozen(0xFF, 0x6D, 0x28, 0xD9);
            public static readonly SolidColorBrush CaseSoft = Frozen(0xFF, 0xF5, 0xF3, 0xFF);
            public static readonly SolidColorBrush WhileBrush = Frozen(0xFF, 0xF5, 0x9E, 0x0B);
            public static readonly SolidColorBrush WhileBorder = Frozen(0xFF, 0xFD, 0xE6, 0x8A);
            public static readonly SolidColorBrush WhileText = Frozen(0xFF, 0xB4, 0x53, 0x09);
            public static readonly SolidColorBrush WhileSoft = Frozen(0xFF, 0xFF, 0xFB, 0xEB);
            public static readonly SolidColorBrush ForBrush = Frozen(0xFF, 0x10, 0xB9, 0x81);
            public static readonly SolidColorBrush ForBorder = Frozen(0xFF, 0xA7, 0xF3, 0xD0);
            public static readonly SolidColorBrush ForText = Frozen(0xFF, 0x04, 0x78, 0x57);
            public static readonly SolidColorBrush ForSoft = Frozen(0xFF, 0xEC, 0xFD, 0xF5);
            public static readonly SolidColorBrush ParallelBrush = Frozen(0xFF, 0x0E, 0xA5, 0xE9);
            public static readonly SolidColorBrush ParallelBorder = Frozen(0xFF, 0xBA, 0xE6, 0xFD);
            public static readonly SolidColorBrush ParallelText = Frozen(0xFF, 0x03, 0x69, 0xA1);
            public static readonly SolidColorBrush ParallelSoft = Frozen(0xFF, 0xF0, 0xF9, 0xFF);

            // 头带渐变：类型浅底 → 白（0.76），与 Logic*HeaderBrush 逐字一致
            public static readonly Brush IfHeader = HeaderGradient(0xEE, 0xF2, 0xFF);
            public static readonly Brush CaseHeader = HeaderGradient(0xF5, 0xF3, 0xFF);
            public static readonly Brush WhileHeader = HeaderGradient(0xFF, 0xFB, 0xEB);
            public static readonly Brush ForHeader = HeaderGradient(0xEC, 0xFD, 0xF5);
            public static readonly Brush ParallelHeader = HeaderGradient(0xF0, 0xF9, 0xFF);

            public static readonly Pen LinkOutlinePen = FrozenPen(LinkOutline, 1.5 + 1.6 * 2);
            public static readonly Pen BreakpointPen = FrozenPen(Canceled, 1.2);
            public static readonly Pen BreakpointOnPen = FrozenPen(Breakpoint, 1.2);
            public static readonly Pen ExtBadgePen = FrozenPen(ExtBadgeBorder, 1);
            public static readonly Pen CollapsePen = FrozenPen(CollapseBorder, 1);
            public static readonly Pen LaneNotePen = FrozenPen(LaneBorder, 1, dashed: true);
            /// <summary>调试命中（停在断点/单步位置）：橙色 2px，与 StepCardStyle 的 IsDebugStopped 一致</summary>
            public static readonly Pen StepDebugPen = FrozenPen(Warning, 2);
            /// <summary>选中辉光：ItemContainer.SelectedBrush #333B82F6，垫在卡片外圈</summary>
            public static readonly Pen SelectedGlowPen = FrozenPen(SelectedGlow, 3);

            private static Brush HeaderGradient(byte r, byte g, byte b)
            {
                var brush = new LinearGradientBrush(
                    Color.FromRgb(r, g, b),
                    Colors.White,
                    new Point(0, 0),
                    new Point(1, 0))
                {
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                };
                // 渐变终点不是 1.0 而是 0.76：右半段整片纯白，与 XAML 的 GradientStop 对齐
                brush.GradientStops.Clear();
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(r, g, b), 0));
                brush.GradientStops.Add(new GradientStop(Colors.White, 0.76));
                brush.Freeze();
                return brush;
            }
        }

        private static SolidColorBrush Frozen(byte alpha, byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static Pen FrozenPen(Brush brush, double thickness, bool dashed = false)
        {
            var pen = new Pen(brush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };

            if (dashed)
            {
                pen.DashStyle = DashStyles.Dash;
                pen.DashCap = PenLineCap.Round;
            }

            pen.Freeze();
            return pen;
        }

        // ---- 字体（不依赖 Application；取系统 UI 字体，取不到退回 Segoe UI）----

        private static readonly FontFamily UiFontFamily = ResolveUiFontFamily();

        /// <summary>
        /// 画布底：与 NodifyEditor.Background 逐字一致——#F7F8FA 底 + 24×24 平铺一个 1.4 粗的圆点（位于 12,12）。
        /// 用 DrawingBrush 让 WPF 自己平铺，比每帧画几百条零长度线段省得多。
        /// </summary>
        private static readonly Brush GridBackground = BuildGridBackground();

        private static Brush BuildGridBackground()
        {
            var tile = new DrawingGroup();

            tile.Children.Add(new GeometryDrawing(
                Palette.CanvasBackground,
                null,
                new RectangleGeometry(new Rect(0, 0, 24, 24))));

            var dotPen = new Pen(Palette.GridDot, 1.4)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            dotPen.Freeze();

            var dotGeometry = Geometry.Parse("M 12 12 L 12.01 12");
            dotGeometry.Freeze();
            tile.Children.Add(new GeometryDrawing(null, dotPen, dotGeometry));
            tile.Freeze();

            var brush = new DrawingBrush(tile)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 24, 24),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None,
            };
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// 正文/标题用的字体族。**默认值只是兜底**：宿主会用 <see cref="SetUiFont"/> 把编辑器的
        /// FontFamily 灌进来——Nodify 模板继承的是 App 的字体，自绘层必须用同一族，
        /// 否则中文回退字形与字重会和真画布对不上（2026-10-25 真机对照发现）。
        /// </summary>
        private FontFamily _uiFontFamily = UiFontFamily;

        private Typeface _regularTypeface =
            new(UiFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private Typeface _semiBoldTypeface =
            new(UiFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        /// <summary>换成与宿主画布同一个字体族，并清空文字缓存（字形度量随字体变）。</summary>
        public void SetUiFont(FontFamily? family)
        {
            if (family is null || Equals(_uiFontFamily, family)) return;

            _uiFontFamily = family;
            _regularTypeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            _semiBoldTypeface = new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            _textCache.Clear();
            InvalidateVisual();
        }

        /// <summary>徽标/耗时用的等宽字体（XAML 里是 FontFamily="Consolas"）。</summary>
        private static readonly Typeface MonoTypeface =
            new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private static readonly Typeface MonoBoldTypeface =
            new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        /// <summary>
        /// 图标字体（App.xaml 的 Icon 令牌）：pack URI 必须拼成 "文件 uri#家族名"（R64）。
        /// 取不到就退回 Segoe UI——图标会变成方块，但不会把整帧画崩。
        /// </summary>
        private static readonly FontFamily IconFontFamily = ResolveIconFontFamily();

        private static readonly Typeface IconTypeface =
            new(IconFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private static FontFamily ResolveIconFontFamily()
        {
            // 优先用 App.xaml 的 Icon 令牌：与 XAML 模板用的是同一个 FontFamily 对象，绝不会漂。
            try
            {
                if (Application.Current?.TryFindResource("Icon") is FontFamily fromApp) return fromApp;
            }
            catch
            {
                // Application 还没建好 / 资源未加载：继续走 pack URI
            }

            try
            {
                // 没有 Application 的断言宿主：pack://application 这条 authority 解析不到，
                // 字形会退化成豆腐块——只影响出图观感，几何与排版断言不受影响。
                return new FontFamily(
                    "pack://application:,,,/UI;component/Asserts/FontFamilys/Font Awesome 6 Pro-Solid-900.otf#Font Awesome 6 Pro Solid");
            }
            catch
            {
                return UiFontFamily;
            }
        }

        /// <summary>文字用哪套字体：界面正文 / 等宽 / 图标。</summary>
        private enum SurfaceFont
        {
            Ui,
            Mono,
            Icon,
        }

        private static FontFamily ResolveUiFontFamily()
        {
            try
            {
                return SystemFonts.MessageFontFamily;
            }
            catch
            {
                return new FontFamily("Segoe UI");
            }
        }
    }
}
