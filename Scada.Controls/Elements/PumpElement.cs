using System;
using System.Windows;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 泵图元（TypeKey = <c>Hmi.Pump</c>）：管路上一台泵的可视化身，画的是离心泵的标准符号——
    /// 一个圆（泵壳）套一个尖角朝右的内接等边三角形（叶轮），尖角指的就是介质被推出去的方向。
    ///
    /// <b>为什么状态只有一个输入</b>
    /// ---------
    /// 现场对泵的关心只有三件事：停着、在转、坏了。这三件事在画面上只差一个颜色，
    /// 所以图元只收一个 <see cref="State"/>，其余全部推导：
    ///
    /// <code>
    ///   State ──┬─▶ StateBrush（停=灰 / 转=绿 / 故障=红）
    ///           ├─▶ BodyGeometry（泵壳圆，随尺寸缩放）
    ///           └─▶ ImpellerGeometry（叶轮三角，随尺寸缩放）
    /// </code>
    ///
    /// <b>为什么泵与电机是两个图元，而不是一个"设备"图元加一个形状开关</b>
    /// ---------
    /// 它们的差别不在形状参数上：泵的辨识特征是"圆里的三角"，电机的辨识特征是"圆顶上的接线盒 + 机身上的 M"。
    /// 合成一个图元加开关，就得在同一个模板里塞两套几何、两套推导，还要为"这个开关该不该可绑"再吵一次；
    /// 而现场看图的人靠轮廓先认出设备，两个轮廓本来就该是两张脸。共用的是状态词汇
    /// （<see cref="DeviceState"/>）与配色口径，不是模板——状态与四个颜色因此上提到了
    /// <see cref="StateVisualElement"/>，本类只留泵壳与叶轮两个几何。
    ///
    /// <b>为什么几何全部在代码里算</b>
    /// ---------
    /// 泵壳是"可画区的内接圆"、叶轮是"泵壳圆的内接等边三角形"，两个尺寸都随控件缩放；
    /// 而"外沿要落在标注尺寸上"要求内缩半个线宽——与阀门（<see cref="ValveElement"/>）、
    /// 表盘（<see cref="GaugeElement"/>）同一个账。模板里只留两个 <c>Path</c> 绑只读几何属性，
    /// 一个算术都不写。
    ///
    /// <b>为什么不做叶轮旋转动画</b>
    /// ---------
    /// 与多态灯不做闪烁、阀门不做开启动画同一条理由：动画需要一条全画面统一的节拍源（S11 报警系统一并做），
    /// 每个图元各起一个定时器，同屏几台泵会各转各的，看上去像画面卡了。
    ///
    /// 运行时的接法：把 <see cref="State"/> 绑到工程变量上（枚举名，或 0/1/2），泵就活了。
    /// </summary>
    public class PumpElement : StateVisualElement
    {
        /// <summary>叶轮半径 / 泵壳半径（0.62：三角够大看得清，又不碰壳壁，留出一圈壳体的厚度）</summary>
        private const double ImpellerRatio = 0.62d;

        /// <summary>
        /// 底部留给位号标签的高度比例（0.25）。
        ///
        /// 为什么泵要专门让出一条底边，而阀门不用：阀门的蝴蝶结上下各有一个 V 形凹口，
        /// 文字塞在凹口里既不压住图形也不用额外留白；泵壳是<b>整圆</b>，圆的下缘只剩一条越来越窄的缝，
        /// 文字压上去就会"骑"在圆弧上——半径越小越难看。所以泵把可画区先切掉底下四分之一当标签行，
        /// 圆只在上方四分之三里内接。
        ///
        /// 这个比例模板并不知道，也不需要知道：模板只把文字贴底摆放（见 PumpElement.xaml），
        /// 两边靠同一个默认尺寸对齐——默认字号（12）的一行字高约 16 像素，
        /// 而默认高 72 的四分之一是 18 像素，正好容得下。
        /// </summary>
        private const double LabelRatio = 0.25d;

        #region 推导结果（只读）

        private static readonly DependencyPropertyKey BodyGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(BodyGeometry), typeof(Geometry), typeof(PumpElement), new PropertyMetadata(null));

        /// <summary>泵壳几何：可画区的内接圆（只读）</summary>
        public static readonly DependencyProperty BodyGeometryProperty = BodyGeometryPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ImpellerGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ImpellerGeometry), typeof(Geometry), typeof(PumpElement), new PropertyMetadata(null));

        /// <summary>叶轮几何：泵壳圆的内接等边三角形，尖角朝右（只读）</summary>
        public static readonly DependencyProperty ImpellerGeometryProperty = ImpellerGeometryPropertyKey.DependencyProperty;

        #endregion

        static PumpElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(PumpElement),
                new FrameworkPropertyMetadata(typeof(PumpElement)));
        }

        /// <summary>泵壳几何（只读，见 <see cref="BodyGeometryProperty"/>）</summary>
        public Geometry? BodyGeometry => (Geometry?)GetValue(BodyGeometryProperty);

        /// <summary>叶轮几何（只读，见 <see cref="ImpellerGeometryProperty"/>）</summary>
        public Geometry? ImpellerGeometry => (Geometry?)GetValue(ImpellerGeometryProperty);

        protected override void RebuildStateVisual()
        {
            // ---- 与尺寸有关的两项 ----

            double inset = StrokeThickness / 2d + 1d; // 半线宽 + 1 像素呼吸空间，外沿才落在标注尺寸上

            // 底下四分之一让给位号标签，泵壳只在上方四分之三里内接（理由见 LabelRatio 的注释）。
            double symbolBottom = ActualHeight * (1d - LabelRatio);

            double width = ActualWidth - 2d * inset;
            double height = symbolBottom - 2d * inset;

            if (!(width > 0d) || !(height > 0d))
            {
                // 还没布局：几何量留空。尺寸一到（SizeChanged）就会重算，
                // 而不是在这里拿一个猜测的尺寸画出一个待会儿会跳一下的泵。
                SetValue(BodyGeometryPropertyKey, null);
                SetValue(ImpellerGeometryPropertyKey, null);
                return;
            }

            // 泵壳取"可画区的内接圆"：宽高不等时按短边，圆永远不会被拉成椭圆
            // （现场符号里的泵壳是正圆，拉扁了就不像泵了）。
            double diameter = Math.Min(width, height);
            double radius = diameter / 2d;
            double centerX = inset + width / 2d;
            double centerY = inset + height / 2d;

            SetValue(BodyGeometryPropertyKey,
                Freeze(new EllipseGeometry(new Point(centerX, centerY), radius, radius)));

            SetValue(ImpellerGeometryPropertyKey, BuildImpeller(centerX, centerY, radius));
        }

        /// <summary>
        /// 叶轮：泵壳圆的内接等边三角形，一个顶点落在"正右方"。
        ///
        /// 顶点朝右不是随手定的：那个方向就是泵的出口方向，现场一眼就能读出介质往哪走。
        /// 等边三角形用 <b>0° / 120° / 240°</b> 三个角均分圆周得到——均分保证三条边到圆心等距，
        /// 看起来才像一只对称的叶轮，而不是随手摆的三个点。
        /// </summary>
        private static Geometry BuildImpeller(double centerX, double centerY, double radius)
        {
            double r = radius * ImpellerRatio;

            Point At(double degrees) => new(
                centerX + r * Math.Cos(degrees * Math.PI / 180d),
                centerY + r * Math.Sin(degrees * Math.PI / 180d));

            return Triangle(At(0d), At(120d), At(240d));
        }

        private static Geometry Triangle(Point a, Point b, Point c)
        {
            var figure = new PathFigure { StartPoint = a, IsClosed = true, IsFilled = true };
            figure.Segments.Add(new LineSegment(b, true));
            figure.Segments.Add(new LineSegment(c, true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            return Freeze(geometry);
        }

        /// <summary>
        /// 冻结几何：几何量每次重算都新建，本来不存在"多图元共享一份"的问题；
        /// 冻结是为了让算完的几何从类型上就不可改（与阀门、表盘同一条纪律）。
        /// </summary>
        private static Geometry Freeze(Geometry geometry)
        {
            geometry.Freeze();
            return geometry;
        }
    }
}
