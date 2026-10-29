using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 圆形进度条图元（TypeKey = <c>Hmi.CircularProgress</c>）：一个数值在圆环上的可视化身。
    ///
    /// 与棒图（<see cref="ProgressBarElement"/>）、表盘（<see cref="GaugeElement"/>）是同一族——
    /// "值 → 一个比例 → 一个几何量"：棒图把比例落在长度上，表盘落在指针角度上，
    /// 本图元落在<b>圆弧的扫过角</b>上。差别在于它没有指针、也没有刻度：整块圆环自己就是读数。
    ///
    /// <b>为什么绘制落在一个模板子元素上，而不是本控件的 OnRender</b>
    /// ---------
    /// 卡片底（<c>Fill</c> / <c>Stroke</c> / 圆角）由模板里的 <c>Border</c> 画，而 WPF 的
    /// <b>模板子元素画在控件自身 OnRender 之上</b>（实测：控件 OnRender 里铺满的矩形会被
    /// 模板 Border 的不透明底色整个盖住）。所以"纯 OnRender 画弧"必须落在 Border 里面的
    /// <see cref="CircularProgressArc"/> 上——与 <see cref="XYChartElement"/> 之于
    /// <see cref="XYChartSurface"/>、<see cref="MultiTrendElement"/> 之于 <see cref="TrendSurface"/>
    /// 是同一条做法。
    ///
    /// <b>角度口径</b>（WPF 约定，与直觉相反）
    /// ---------
    /// 0° 指向右（3 点钟），角度<b>顺时针</b>增加。默认 <see cref="StartAngle"/> = -90°（12 点钟）、
    /// <see cref="SweepAngle"/> = 360°，于是进度从正上方起、顺时针绕一整圈——圆形进度条的标准长相。
    ///
    /// <b>设计期不画 0</b>
    /// ---------
    /// 比例只由 <see cref="Value"/> / <see cref="Min"/> / <see cref="Max"/> 决定，没有
    /// "运行期才有值"的分支：工程师在属性面板里把「当前值」调成 65，设计器里立刻就是 65% 的弧，
    /// 不必先起运行窗口才知道这个图元长什么样。
    ///
    /// 运行时的接法（S6）：把 <see cref="Value"/> 绑到工程变量上，弧就跟着变量长。
    /// </summary>
    [TemplatePart(Name = PartArc, Type = typeof(CircularProgressArc))]
    [TemplatePart(Name = PartValueLabel, Type = typeof(TextBlock))]
    [TemplatePart(Name = PartCaptionLabel, Type = typeof(TextBlock))]
    public partial class CircularProgressElement : ScadaElementBase
    {
        /// <summary>模板部件名：画环与进度弧的绘制面</summary>
        public const string PartArc = "ArcLayer";

        /// <summary>模板部件名：圆心的数值文字</summary>
        public const string PartValueLabel = "ValueLabel";

        /// <summary>模板部件名：数值下方的单位/说明</summary>
        public const string PartCaptionLabel = "CaptionLabel";

        /// <summary>数值格式串写坏时的回落格式（见 <see cref="ValueFormat"/>）</summary>
        public const string DefaultFormat = "F0";

        // 声明次序 = 属性面板里的行序（生成器按源码次序收集，面板组内也按这条次序排）：
        // 数据一条 → 刻度三条 → 外观五条，与规格表一致。

        /// <summary>当前值（绑到工程变量上）</summary>
        [ScadaProperty(Key = "Value", DisplayName = "当前值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", IsBindable = true,
                       Description = "运行态把工程变量绑到这里；进度 = (当前值 − 最小值) ÷ (最大值 − 最小值)，" +
                                     "超出量程按两端收敛，非法值按 0% 画")]
        public partial double Value { get; set; }

        /// <summary>量程下限</summary>
        [ScadaProperty(Key = "Min", DisplayName = "最小值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Scale, DefaultValue = "0",
                       Description = "最小值不小于最大值时画空环，而不是让非法量程把整页渲染打断")]
        public partial double Min { get; set; }

        /// <summary>量程上限</summary>
        [ScadaProperty(Key = "Max", DisplayName = "最大值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Scale, DefaultValue = "100")]
        public partial double Max { get; set; }

        /// <summary>环宽（像素；就是画弧那支 Pen 的粗细）</summary>
        [ScadaProperty(Key = "Thickness", DisplayName = "环宽", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "12", Min = 2, Max = 60,
                       Description = "圆环的粗细（像素）。控件被缩小时会自动收到半径以内，不会画到框外")]
        public partial double Thickness { get; set; }

        /// <summary>起始角（度，0 = 3 点钟方向，顺时针为正）</summary>
        [ScadaProperty(Key = "StartAngle", DisplayName = "起始角度", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Scale, DefaultValue = "-90",
                       Description = "度。0° 指向右（3 点钟）、角度顺时针增加；默认 −90° 就是正上方")]
        public partial double StartAngle { get; set; }

        /// <summary>扫过角度（度；进度弧最多能走多远）</summary>
        [ScadaProperty(Key = "SweepAngle", DisplayName = "扫过角度", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Scale, DefaultValue = "360", Min = 30, Max = 360,
                       Description = "度。360 = 整圈（默认）；270 会留一个缺口（放单位字用）。" +
                                     "当前值走满时弧正好扫过这么多度")]
        public partial double SweepAngle { get; set; }

        /// <summary>是否在圆心显示数值</summary>
        [ScadaProperty(Key = "ShowValue", DisplayName = "显示数值", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "True",
                       Description = "关掉只留圆环（读数由旁边的数值域承担）")]
        public partial bool ShowValue { get; set; }

        /// <summary>数值的显示格式（标准 .NET 数字格式串）</summary>
        [ScadaProperty(Key = "ValueFormat", DisplayName = "数值格式", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "F0",
                       Description = "标准 .NET 数字格式串（F0 取整、F1 一位小数、0.## 去掉多余的零）；" +
                                     "写成一个不含数字占位符的串（如「乱写」）会回落到 F0，不会把文字画到圆心")]
        public partial string? ValueFormat { get; set; }

        /// <summary>进度弧的颜色</summary>
        [ScadaProperty(Key = "ProgressColor", DisplayName = "进度色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF2D7DD2",
                       Description = "已走完那一段弧的颜色；与轨道色要拉开，否则看不出走到哪儿了")]
        public partial Brush? ProgressColor { get; set; }

        /// <summary>轨道（空环）的颜色</summary>
        [ScadaProperty(Key = "TrackColor", DisplayName = "轨道色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF2A3745",
                       Description = "未走完那一段的颜色（整圈都画，进度弧压在上面）；浅色画面上要调亮")]
        public partial Brush? TrackColor { get; set; }

        private static readonly DependencyPropertyKey ValueTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ValueText), typeof(string), typeof(CircularProgressElement),
            new PropertyMetadata(string.Empty));

        /// <summary>圆心显示的数值文字（只读；= Value 按 ValueFormat 格式化，格式坏就回落 F0）</summary>
        public static readonly DependencyProperty ValueTextProperty = ValueTextPropertyKey.DependencyProperty;

        static CircularProgressElement()
        {
            // 少了这一句，WPF 会按基类的键去找样式 → 本图元的模板永远查不到 →
            // 画布上拖上去只剩基类的空模板，而且不抛任何异常（ElementStyleKeyChecks 就是钉这条）。
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(CircularProgressElement),
                new FrameworkPropertyMetadata(typeof(CircularProgressElement)));
        }

        public CircularProgressElement()
        {
            // 构造即算第一帧：断言环境不挂可视树、不跑布局，不显式算一遍圆心就没有数值文字。
            UpdateVisual();
        }

        private CircularProgressArc? _arcLayer;

        /// <summary>圆心显示的数值文字（只读，见 <see cref="ValueTextProperty"/>）</summary>
        public string? ValueText => (string?)GetValue(ValueTextProperty);

        /// <summary>
        /// 当前值在量程里的比例（恒在 0~1）。
        ///
        /// 量程非法（最大值 ≤ 最小值）或值不是有限数（NaN / ±∞）时返回 0，而不是让除法算出
        /// NaN/Infinity——一个坏值不该把整页渲染打断（与棒图、表盘同一条纪律）。
        /// 只读派生量，不是可配属性，所以不走 <c>[ScadaProperty]</c>。
        /// </summary>
        public double ProgressRatio => Ratio();

        // 十个输入共用同一条副作用（数值文字与绘制面都要重算）。绘制面是模板子元素，
        // 依赖属性上的 AffectsRender 只会失效控件自己那份 visual，不会连带子元素——
        // 所以这里必须显式把子元素也失效掉，否则"改了值、弧却停在上一格"。
        partial void OnValuePropertyChanged() => UpdateVisual();
        partial void OnMinPropertyChanged() => UpdateVisual();
        partial void OnMaxPropertyChanged() => UpdateVisual();
        partial void OnThicknessPropertyChanged() => UpdateVisual();
        partial void OnStartAnglePropertyChanged() => UpdateVisual();
        partial void OnSweepAnglePropertyChanged() => UpdateVisual();
        partial void OnShowValuePropertyChanged() => UpdateVisual();
        partial void OnValueFormatPropertyChanged() => UpdateVisual();
        partial void OnProgressColorPropertyChanged() => UpdateVisual();
        partial void OnTrackColorPropertyChanged() => UpdateVisual();

        // 收尾钩子：StrokeThickness / Padding 这类"由基类 RefreshCore 才落地"的量变了也要重摆一遍
        //（显隐由代码翻，模板里不写 Visibility 初值——本环境下 ControlTemplate.Triggers 里的
        // DataTrigger 不触发，写了也是死代码，见 ScadaElementBase.SetPartVisible）。
        protected override void OnElementRefreshed() => UpdateVisual();

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            // 模板刚套上时绘制面还是"裸"的：注入 Owner，让它知道该画谁的数据。
            _arcLayer = GetTemplateChild(PartArc) as CircularProgressArc;
            if (_arcLayer != null)
                _arcLayer.Owner = this;

            UpdateVisual();
        }

        /// <summary>尺寸变了要重画：弧的半径与线宽都取决于"此刻有多少像素"（子元素自己的尺寸也随之变）</summary>
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            _arcLayer?.InvalidateVisual();
        }

        /// <summary>
        /// 把输入算成"该显示什么"：数值文字 + 数值行显隐 + 让绘制面重画。
        ///
        /// 与尺寸无关的部分在这里就落地，所以断言环境（不挂模板、不跑布局）里
        /// <see cref="ValueText"/> 与 <see cref="ProgressRatio"/> 也是对的。
        /// </summary>
        private void UpdateVisual()
        {
            SetValue(ValueTextPropertyKey, FormatValue());
            SetPartVisible(PartValueLabel, ShowValue);
            _arcLayer?.InvalidateVisual();
        }

        /// <summary>当前值在量程里的比例（0~1；量程非法或值非有限数一律 0，绝不返回 NaN）</summary>
        private double Ratio()
        {
            double range = Max - Min;

            if (!double.IsFinite(range) || range <= 0d)
                return 0d; // 量程配错（最大值 ≤ 最小值）时画空环，而不是让 NaN 把渲染打断

            double ratio = (Value - Min) / range;

            return double.IsFinite(ratio) ? Math.Clamp(ratio, 0d, 1d) : 0d;
        }

        /// <summary>
        /// 圆心数值的显示串。
        ///
        /// 格式串写坏时回落 <see cref="DefaultFormat"/>（F0）：与棒图、表盘、数值域同一条纪律——
        /// 一个坏格式串不该让整页渲染中断。用不变文化：.vms 是跨机器交换的，显示串不该跟着
        /// 系统区域设置变（德语系统上小数点会变成逗号）。
        ///
        /// <b>为什么不能只 catch FormatException</b>
        /// ---------
        /// .NET 的数字格式串有两种：单个标准说明符（<c>F0</c> / <c>N2</c> / <c>P0</c>）与自定义模板
        /// （<c>0.0</c> / <c>#,##0</c>）。自定义模板里<b>不认识的字符是当字面量原样输出的</b>——
        /// 于是 <c>Value.ToString("乱写")</c> 既不抛、也不格式化，直接把「乱写」两个字画到圆心
        /// （实测，本图元的 [CP7] 就是这么抓到的）。只有单字符的非法说明符（如 <c>"Q"</c>）才抛。
        ///
        /// 所以判据改成"<b>格式化结果里必须至少有一个数字</b>"：一个真在格式化数字的格式串
        /// （含带单位的 <c>0.0 ℃</c>）必然含数字；一个纯字面量的坏格式必然不含。NaN / ∞ 这类
        /// 本来就无数位的结果由回落那一支兜住，不会来回打转。
        /// </summary>
        private string FormatValue()
        {
            var format = string.IsNullOrWhiteSpace(ValueFormat) ? DefaultFormat : ValueFormat!;

            if (TryFormat(Value, format, out var text) && HasDigit(text))
                return text;

            return TryFormat(Value, DefaultFormat, out var fallback)
                ? fallback
                : Value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>按格式串格式化；格式串非法（FormatException）时返回 false</summary>
        private static bool TryFormat(double value, string format, out string text)
        {
            try
            {
                text = value.ToString(format, CultureInfo.InvariantCulture);
                return true;
            }
            catch (FormatException)
            {
                text = string.Empty;
                return false;
            }
        }

        /// <summary>串里有没有十进制数字（判断"这个格式串到底有没有在格式化数字"）</summary>
        private static bool HasDigit(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (var c in text)
            {
                if (c >= '0' && c <= '9')
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 本图元的绘制入口（由模板里的 <see cref="CircularProgressArc"/> 在自己的 OnRender 里调）。
        ///
        /// 先画整圈轨道，再把进度弧压上去——于是"走到哪儿"由两段的颜色差表达，不必再画刻度。
        /// 半径扣掉半个环宽：描边以中心线为轴向两侧各铺半个线宽，不扣就会顶到控件边界外。
        /// </summary>
        internal void PaintArc(DrawingContext dc, FrameworkElement surface)
        {
            double width = surface.ActualWidth;
            double height = surface.ActualHeight;

            if (!(width > 2d) || !(height > 2d))
                return; // 还没布局：什么都不画，等 SizeChanged 再来

            double cx = width / 2d;
            double cy = height / 2d;

            // 环宽夹在"至少 1 像素"与"半径以内"之间：属性面板给了 2~60，运行态写进来的
            // 任何值也不该把环画到框外（那看起来像图元漏了半张脸）。
            double maxRing = Math.Max(1d, Math.Min(width, height) / 2d - 2d);
            double ring = double.IsFinite(Thickness) ? Math.Clamp(Thickness, 1d, maxRing) : Math.Min(12d, maxRing);
            double radius = Math.Min(width, height) / 2d - ring / 2d - 1d;

            if (!(radius > 0d))
                return;

            var center = new Point(cx, cy);

            var trackPen = CreatePen(TrackColor, ring, round: false);
            if (trackPen != null)
                dc.DrawEllipse(null, trackPen, center, radius, radius);

            double ratio = ProgressRatio;
            if (ratio <= 0d)
                return; // 0% 只留轨道（不画一个零长度的圆头，否则看起来像有个点）

            double start = double.IsFinite(StartAngle) ? StartAngle : -90d;
            double sweep = double.IsFinite(SweepAngle) ? Math.Clamp(SweepAngle, 0d, 360d) : 0d;
            double drawn = sweep * ratio;

            if (drawn < 0.01d)
                return;

            var progressPen = CreatePen(ProgressColor, ring, round: true);
            if (progressPen == null)
                return;

            // 整圈：ArcSegment 一段画不出 360°（起点终点重合，它无从判断走哪边），
            // 直接给一个圆，比拆成两段弧少一次边界判断（与表盘的量程弧同一处理）。
            if (drawn >= 359.99d)
            {
                dc.DrawEllipse(null, progressPen, center, radius, radius);
                return;
            }

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(PointOnCircle(cx, cy, radius, start), isFilled: false, isClosed: false);
                context.ArcTo(
                    point: PointOnCircle(cx, cy, radius, start + drawn),
                    size: new Size(radius, radius),
                    rotationAngle: 0d,
                    isLargeArc: drawn > 180d, // 超过半圈必须置 true，否则 WPF 会走短的那一边
                    sweepDirection: SweepDirection.Clockwise,
                    isStroked: true,
                    isSmoothJoin: false);
            }

            geometry.Freeze();
            dc.DrawGeometry(null, progressPen, geometry);
        }

        /// <summary>
        /// 画环用的笔。空画刷返回 null（画了也看不见，省一次绘制）；
        /// 圆头只给进度弧——轨道是整圈，圆头与否看不出差别，而进度弧的起点用圆头才不像被刀切过。
        /// </summary>
        private static Pen? CreatePen(Brush? brush, double thickness, bool round)
        {
            if (brush is null)
                return null;

            var pen = new Pen(brush, thickness);

            if (round)
            {
                pen.StartLineCap = PenLineCap.Round;
                pen.EndLineCap = PenLineCap.Round;
            }

            // 能冻结就冻结：画刷是共享的（依赖属性默认值），冻结后没人能从外面改到它。
            // 用了动态资源/动画的画刷不能冻结，那就照常画——不为一个视觉优化把画面打断。
            if (pen.CanFreeze)
                pen.Freeze();

            return pen;
        }

        /// <summary>圆周上某角度处的点（角度按 WPF 约定：0° 向右、顺时针为正）</summary>
        private static Point PointOnCircle(double cx, double cy, double radius, double angle)
        {
            double radians = angle * Math.PI / 180d;

            return new Point(cx + radius * Math.Cos(radians), cy + radius * Math.Sin(radians));
        }
    }

    /// <summary>
    /// 圆形进度条的绘制面（<see cref="FrameworkElement"/> + <c>OnRender</c>）。
    ///
    /// 为什么要单独一个元素：卡片底由模板里的 <c>Border</c> 画，而模板子元素画在控件自身
    /// <c>OnRender</c> 之上（实测：控件里铺满的矩形会被 Border 的不透明底色整个盖住）。
    /// 绘制面挂在 Border 里面，既保住了"卡片底走模板"的既有做法，又让环画在底之上。
    ///
    /// 与 <see cref="XYChartSurface"/> 同一分工：本类只管"把 Owner 的弧画出来"，
    /// 比例、角度、颜色全在 <see cref="CircularProgressElement"/> 里。
    /// </summary>
    public sealed class CircularProgressArc : FrameworkElement
    {
        /// <summary>绘制谁的数据（模板应用时由 <see cref="CircularProgressElement.OnApplyTemplate"/> 注入）</summary>
        public CircularProgressElement? Owner { get; set; }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            Owner?.PaintArc(dc, this);
        }
    }
}
