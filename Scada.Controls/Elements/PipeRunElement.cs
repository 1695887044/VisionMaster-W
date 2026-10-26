using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 管道连线（TypeKey = <c>Hmi.PipeRun</c>）：两端<b>连到图元的连接口</b>，
    /// 走线由 <see cref="ScadaPipeRouter"/> 现算——拖设备时管线自动重排。
    ///
    /// 与 <see cref="PipeElement"/>（Hmi.Pipe）的分工
    /// ---------
    /// Hmi.Pipe 是"一段固定管身 + 流向箭头"，位置尺寸由用户拉；本图元是"两点之间的走线"，
    /// 端点来自别的图元，几何不归用户调（属性面板里 X/Y/宽高只读展示）。
    /// 两者不合并：合并会让"管身要拉多长"和"端点在哪"两套语义挤在同一组属性上。
    ///
    /// 几何怎么来
    /// ---------
    /// ① 用 <see cref="ScadaElementBase.PageElementsResolver"/> 找到两端图元；
    /// ② 用路由器算绝对折线；
    /// ③ 减去自己的 X/Y 得到本地折点，写成只读几何；
    /// ④ 标签按 <see cref="LabelAt"/> 沿折线取点。
    /// 外框（X/Y/宽高）由画布在重排时写回模型——所以"选中框"永远贴着线。
    ///
    /// 标签
    /// ---------
    /// <see cref="ScadaElementBase.Text"/> 就是标签（可写死，也可绑变量——描述符里声明为可绑）。
    /// <see cref="LabelAt"/> 决定它落在管线的百分之几处，<see cref="LabelOffset"/> 让它离开管身。
    /// </summary>
    public partial class PipeRunElement : ScadaElementBase
    {
        /// <summary>
        /// 默认样式键钉到本类型（R73 同族，2026-10-25 修）。
        ///
        /// 不覆盖就继承基类的样式键：主题里 <c>Themes/PipeRunElement.xaml</c> 是按本类型键的，
        /// 画布上永远查不到 → 只剩基类那个空模板，**拖上去什么都看不见**。
        /// 断言里因为显式赋了 Style（<c>theme[typeof(PipeRunElement)]</c>）反而是绿的——
        /// 显式赋值绕过这条查找路径，这正是"只有真机画布才看得见"的一类静默故障。
        /// </summary>
        static PipeRunElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(PipeRunElement),
                new FrameworkPropertyMetadata(typeof(PipeRunElement)));
        }

        /// <summary>标签居中用的固定宽度（Canvas 定位需要知道宽，量文字要 FormattedText，不值当）</summary>
        public const double LabelWidth = 160d;

        /// <summary>拐角圆角半径</summary>
        public const double CornerRadius = 10d;

        // ---- 连接（由画布的连线手势写入；属性面板只读展示） ----

        /// <summary>起点图元的稳定 Id</summary>
        [ScadaProperty(Key = "FromId", DisplayName = "起点图元", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = false,
                       Description = "起点图元的 ElementId（在画布上从连接口拖线自动写入；要改就删了重连）")]
        public partial string? FromId { get; set; }

        /// <summary>起点连接口</summary>
        [ScadaProperty(Key = "FromPort", DisplayName = "起点接口", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "Right",
                       Choices = new[] { "Top", "Right", "Bottom", "Left" }, IsBindable = false,
                       Description = "从图元的哪一边出线；出线方向跟着它走（不会从设备身体里穿进去）")]
        public partial string? FromPort { get; set; }

        /// <summary>终点图元的稳定 Id</summary>
        [ScadaProperty(Key = "ToId", DisplayName = "终点图元", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = false,
                       Description = "终点图元的 ElementId")]
        public partial string? ToId { get; set; }

        /// <summary>终点连接口</summary>
        [ScadaProperty(Key = "ToPort", DisplayName = "终点接口", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "Left",
                       Choices = new[] { "Top", "Right", "Bottom", "Left" }, IsBindable = false,
                       Description = "进图元的哪一边")]
        public partial string? ToPort { get; set; }

        /// <summary>走线方式</summary>
        [ScadaProperty(Key = "RouteMode", DisplayName = "走线", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "Orthogonal",
                       Choices = new[] { "Orthogonal", "Straight" }, IsBindable = false,
                       Description = "正交 = 横平竖直 + 圆角；直线 = 两点直连（斜线）")]
        public partial string? RouteMode { get; set; }

        /// <summary>避让设备</summary>
        [ScadaProperty(Key = "Detour", DisplayName = "避让设备", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "True",
                       Description = "基础走线穿过别的图元时，自动改绕上方/下方（或左/右）")]
        public partial bool Detour { get; set; }

        /// <summary>显示标签</summary>
        [ScadaProperty(Key = "ShowLabel", DisplayName = "显示标签", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "True",
                       Description = "关掉只画线（管线号由旁边的文字图元标注时用）")]
        public partial bool ShowLabel { get; set; }

        /// <summary>标签落点（沿折线的比例）</summary>
        [ScadaProperty(Key = "LabelAt", DisplayName = "标签位置", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "0.5", Min = 0, Max = 1, IsBindable = false,
                       Description = "0 = 起点、1 = 终点、0.5 = 中间；沿折线长度算")]
        public partial double LabelAt { get; set; }

        /// <summary>标签离管身的距离（负 = 往上/左）</summary>
        [ScadaProperty(Key = "LabelOffset", DisplayName = "标签偏移", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "-14", Min = -60, Max = 60, IsBindable = false,
                       Description = "垂直管身方向的偏移（像素）；负值让字浮在线上方")]
        public partial double LabelOffset { get; set; }

        // ---- 只读投影 ----

        private static readonly DependencyPropertyKey PipeGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(PipeGeometry), typeof(Geometry), typeof(PipeRunElement), new PropertyMetadata(null));

        public static readonly DependencyProperty PipeGeometryProperty = PipeGeometryPropertyKey.DependencyProperty;

        /// <summary>本地折线几何（模板里的 Path 直接绑）</summary>
        public Geometry? PipeGeometry => (Geometry?)GetValue(PipeGeometryProperty);

        private static readonly DependencyPropertyKey LabelLeftPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(LabelLeft), typeof(double), typeof(PipeRunElement), new PropertyMetadata(0d));

        public static readonly DependencyProperty LabelLeftProperty = LabelLeftPropertyKey.DependencyProperty;

        /// <summary>标签左上角 X（已减去固定宽度的一半，让字居中在落点上）</summary>
        public double LabelLeft => (double)GetValue(LabelLeftProperty);

        private static readonly DependencyPropertyKey LabelTopPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(LabelTop), typeof(double), typeof(PipeRunElement), new PropertyMetadata(0d));

        public static readonly DependencyProperty LabelTopProperty = LabelTopPropertyKey.DependencyProperty;

        /// <summary>标签左上角 Y</summary>
        public double LabelTop => (double)GetValue(LabelTopProperty);

        private static readonly DependencyPropertyKey LabelVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(LabelVisibility), typeof(Visibility), typeof(PipeRunElement), new PropertyMetadata(Visibility.Collapsed));

        public static readonly DependencyProperty LabelVisibilityProperty = LabelVisibilityPropertyKey.DependencyProperty;

        /// <summary>标签显隐（没接线 / 关掉标签 / 没文字 时收起）</summary>
        public Visibility LabelVisibility => (Visibility)GetValue(LabelVisibilityProperty);

        private static readonly DependencyPropertyKey IsLinkedPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsLinked), typeof(bool), typeof(PipeRunElement), new PropertyMetadata(false));

        public static readonly DependencyProperty IsLinkedProperty = IsLinkedPropertyKey.DependencyProperty;

        /// <summary>两端都解析到了（否则画虚线提示"端点丢失"）</summary>
        public bool IsLinked => (bool)GetValue(IsLinkedProperty);

        private static readonly DependencyPropertyKey PipeLengthPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(PipeLength), typeof(double), typeof(PipeRunElement), new PropertyMetadata(0d));

        public static readonly DependencyProperty PipeLengthProperty = PipeLengthPropertyKey.DependencyProperty;

        /// <summary>走线总长（设计像素；属性面板/断言用）</summary>
        public double PipeLength => (double)GetValue(PipeLengthProperty);

        /// <summary>标签盒宽度（模板绑它；常量，不需要通知）</summary>
        public double LabelBoxWidth => LabelWidth;

        private IReadOnlyList<ScadaPipePoint> _route = Array.Empty<ScadaPipePoint>();
        private readonly List<ScadaElement> _watched = new();
        private bool _rebuilding;

        partial void OnFromIdPropertyChanged() => Rebuild();
        partial void OnFromPortPropertyChanged() => Rebuild();
        partial void OnToIdPropertyChanged() => Rebuild();
        partial void OnToPortPropertyChanged() => Rebuild();
        partial void OnRouteModePropertyChanged() => Rebuild();
        partial void OnDetourPropertyChanged() => Rebuild();
        partial void OnLabelAtPropertyChanged() => UpdateLabel();
        partial void OnLabelOffsetPropertyChanged() => UpdateLabel();
        partial void OnShowLabelPropertyChanged() => UpdateLabel();

        protected override void OnElementRefreshed()
        {
            base.OnElementRefreshed();
            Rebuild();
        }

        protected override void OnPageElementsResolverChanged() => Rebuild();

        /// <summary>当前走线（绝对设计坐标；画布重排与断言共用）</summary>
        public IReadOnlyList<ScadaPipePoint> Route => _route;

        /// <summary>
        /// 重算走线。返回 false 表示端点没解析到（此时画虚线占位）。
        /// 画布在"图元动了"之后调 <see cref="Rebuild"/>，并用 <see cref="Route"/> 写回外框。
        /// </summary>
        public bool Rebuild()
        {
            if (_rebuilding)
                return _route.Count >= 2;

            _rebuilding = true;
            try
            {
                var from = ResolveElement(ParseGuid(FromId));
                var to = ResolveElement(ParseGuid(ToId));

                Watch(from, to);

                if (from == null || to == null)
                {
                    _route = Array.Empty<ScadaPipePoint>();
                    SetValue(PipeGeometryPropertyKey, null);
                    SetValue(IsLinkedPropertyKey, false);
                    SetValue(PipeLengthPropertyKey, 0d);
                    SetValue(LabelVisibilityPropertyKey, Visibility.Collapsed);
                    return false;
                }

                var fromRect = new ScadaPipeRect(from.X, from.Y, Math.Max(1, from.Width), Math.Max(1, from.Height));
                var toRect = new ScadaPipeRect(to.X, to.Y, Math.Max(1, to.Width), Math.Max(1, to.Height));

                var obstacles = Detour ? CollectObstacles(from.ElementId, to.ElementId) : null;
                var route = ScadaPipeRouter.Route(
                    fromRect, ScadaPipeRouter.ParsePort(FromPort, ScadaPipePort.Right),
                    toRect, ScadaPipeRouter.ParsePort(ToPort, ScadaPipePort.Left),
                    ScadaPipeRouter.ParseMode(RouteMode, ScadaPipeRouteMode.Orthogonal),
                    obstacles);

                _route = route;
                SetValue(IsLinkedPropertyKey, true);
                SetValue(PipeLengthPropertyKey, ScadaPipeRouter.Length(route));

                // 外框写回模型：选中框/命中区才贴着线。这是"布局"不是"编辑"，
                // 走 Detached（不产生撤销记录，也不触发写守卫）。
                var bounds = ScadaPipeRouter.Bounds(route);
                var model = Element;
                if (model != null)
                {
                    ScadaChangeScope.Detached(() =>
                    {
                        model.X = bounds.X;
                        model.Y = bounds.Y;
                        model.Width = bounds.Width;
                        model.Height = bounds.Height;
                        return 0;
                    });
                }

                var offsetX = model?.X ?? 0d;
                var offsetY = model?.Y ?? 0d;
                SetValue(PipeGeometryPropertyKey, BuildLocalGeometry(route, offsetX, offsetY));

                UpdateLabel();
                return true;
            }
            finally
            {
                _rebuilding = false;
            }
        }

        /// <summary>
        /// 盯住两端图元的几何变化：设备一动，管线就重排。
        ///
        /// 为什么在这里订阅而不是等画布通知：画布只知道"某个控件刷新了"，
        /// 不知道"这根管子的端点是不是它"。端点模型自己会发 PropertyChanged
        /// （<see cref="ScadaModelBase"/> 的 SetProperty），订阅它最直接、也最省一次全页遍历。
        /// </summary>
        private void Watch(ScadaElement? from, ScadaElement? to)
        {
            foreach (var element in _watched)
                element.PropertyChanged -= OnEndpointChanged;

            _watched.Clear();

            if (from != null)
            {
                _watched.Add(from);
                from.PropertyChanged += OnEndpointChanged;
            }

            if (to != null && !ReferenceEquals(to, from))
            {
                _watched.Add(to);
                to.PropertyChanged += OnEndpointChanged;
            }
        }

        private void OnEndpointChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not ("X" or "Y" or "Width" or "Height"))
                return;

            Rebuild();
        }

        /// <summary>解绑（控件从画布摘掉后不该继续攥着模型）</summary>
        public void Detach()
        {
            foreach (var element in _watched)
                element.PropertyChanged -= OnEndpointChanged;

            _watched.Clear();
        }

        /// <summary>避让障碍：本页其余图元（去掉两端自己）</summary>
        private IReadOnlyList<ScadaPipeRect>? CollectObstacles(Guid fromId, Guid toId)
        {
            var elements = ResolvePageElements();
            if (elements.Count == 0)
                return null;

            var result = new List<ScadaPipeRect>(elements.Count);

            foreach (var element in elements)
            {
                if (element == null || element.ElementId == fromId || element.ElementId == toId)
                    continue;

                if (element.Width <= 0 || element.Height <= 0)
                    continue;

                // 稍微内缩：管线贴着设备边缘走不算穿模（现场就是这么画的）
                result.Add(new ScadaPipeRect(
                    element.X + 4, element.Y + 4,
                    Math.Max(1, element.Width - 8), Math.Max(1, element.Height - 8)));
            }

            return result.Count == 0 ? null : result;
        }

        private static Geometry? BuildLocalGeometry(IReadOnlyList<ScadaPipePoint> route, double offsetX, double offsetY)
        {
            if (route.Count < 2)
                return null;

            var figure = new PathFigure
            {
                StartPoint = new Point(route[0].X - offsetX, route[0].Y - offsetY),
                IsClosed = false,
                IsFilled = false,
            };

            for (var i = 1; i < route.Count - 1; i++)
            {
                var prev = route[i - 1];
                var cur = route[i];
                var next = route[i + 1];

                double inLen = Math.Sqrt(Math.Pow(cur.X - prev.X, 2) + Math.Pow(cur.Y - prev.Y, 2));
                double outLen = Math.Sqrt(Math.Pow(next.X - cur.X, 2) + Math.Pow(next.Y - cur.Y, 2));
                double r = Math.Min(CornerRadius, Math.Min(inLen, outLen) / 2d);

                if (r < 0.5)
                {
                    figure.Segments.Add(new LineSegment(new Point(cur.X - offsetX, cur.Y - offsetY), true));
                    continue;
                }

                var inPoint = new Point(
                    cur.X - (cur.X - prev.X) / inLen * r - offsetX,
                    cur.Y - (cur.Y - prev.Y) / inLen * r - offsetY);
                var outPoint = new Point(
                    cur.X + (next.X - cur.X) / outLen * r - offsetX,
                    cur.Y + (next.Y - cur.Y) / outLen * r - offsetY);

                figure.Segments.Add(new LineSegment(inPoint, true));
                figure.Segments.Add(new QuadraticBezierSegment(
                    new Point(cur.X - offsetX, cur.Y - offsetY), outPoint, true));
            }

            var last = route[route.Count - 1];
            figure.Segments.Add(new LineSegment(new Point(last.X - offsetX, last.Y - offsetY), true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        private void UpdateLabel()
        {
            var visible = ShowLabel && _route.Count >= 2;

            SetValue(LabelVisibilityPropertyKey, visible ? Visibility.Visible : Visibility.Collapsed);

            if (!visible)
                return;

            var offsetX = Element?.X ?? 0d;
            var offsetY = Element?.Y ?? 0d;
            var anchor = ScadaPipeRouter.PointAt(_route, LabelAt);

            SetValue(LabelLeftPropertyKey, anchor.X - offsetX - LabelWidth / 2d);
            SetValue(LabelTopPropertyKey, anchor.Y - offsetY + LabelOffset);
        }

        private static Guid ParseGuid(string? text)
            => Guid.TryParse((text ?? string.Empty).Trim(), out var id) ? id : Guid.Empty;
    }
}
