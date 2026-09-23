using System;
using System.Windows;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 管道流向（<see cref="PipeElement"/> 的 <c>Direction</c> 属性的取值）。
    ///
    /// 只有 <see cref="PipeElement"/> 一个使用者，所以按"判据是有几个使用者"的规矩
    /// 直接住在本文件里（对照：<see cref="DeviceState"/> 被泵 / 电机 / 管道三家共用，才单开一个文件）。
    ///
    /// <see cref="None"/> 是"纯管身"：一条不画箭头的管子。有它，用户就不必为了画一段不带流向的管
    /// 去凑一个别的图元——同屏既有带流向的管又有不带流向的管，是很常见的画法。
    /// </summary>
    public enum PipeFlowDirection
    {
        /// <summary>介质从左往右流（箭头朝右）</summary>
        LeftToRight,

        /// <summary>介质从右往左流（箭头朝左）</summary>
        RightToLeft,

        /// <summary>不画箭头（只要管身）</summary>
        None,
    }

    /// <summary>
    /// 管道图元（TypeKey = <c>Hmi.Pipe</c>）：工艺流程图上的一段管子，
    /// 画的是管身（一块圆角矩形）+ 沿管腔等距排开的流向箭头。
    ///
    /// <b>两个输入，各管一件事</b>
    /// ---------
    /// <code>
    ///   Direction ─▶ ArrowGeometry（箭头朝右 / 朝左 / 不画）
    ///   State ─────▶ StateBrush（箭头颜色：停=灰 / 流=绿 / 异常=红）
    /// </code>
    /// <see cref="Direction"/> 是配管定死的走向，属于设计期语言，所以不声明可绑；
    /// <see cref="State"/> 是运行期语言（泵停了、阀关了、管道堵了，箭头要当场变色），所以可绑。
    ///
    /// <b>为什么要有 Direction，而不是让用户把管子旋转 180°</b>
    /// ---------
    /// 基类的 <c>$Rotation</c> 确实能把管子转 180°，但那个旋转是"整个图元"的：
    /// 用户想表达的是"介质往左走"这一件事，却要绕个弯去算角度，而且管子一旦不止一段、
    /// 拼成一条折线时，逐段算角度会算到人崩溃。给一个"流向"下拉框，一次点击就到位。
    ///
    /// <b>为什么管道没有位号（Text）</b>
    /// ---------
    /// 泵、电机、阀门都要位号，是因为它们的图形中间空着一块（泵壳里、蝴蝶结的凹口里、机身下方）；
    /// 管道的管腔被箭头占满了，硬塞一行字只能把箭头挤掉或者压在箭头上。
    /// 管线号在工艺图上本来也是<b>独立标注</b>——摆一个「文字」图元在管子旁边即可，
    /// 位置还能随版面自由调。少一个属性，换来的是一块不会打架的版面。
    ///
    /// <b>为什么箭头数量随尺寸变，而不是写死三个</b>
    /// ---------
    /// 管道是会被拉长拉短的（同一张图上既有 80 像素的短接、也有 600 像素的长输）。
    /// 写死三个：拉长后三个箭头孤零零地漂在中间，像画漏了；写死"每隔 40 像素一个"：
    /// 缩短后箭头会叠在一起糊成一团。所以数量按"管身能排下几个"现算（步距随管高走），
    /// 再夹在 1~6 之间——短管至少有一个箭头，长管也不会排成一串蜈蚣。
    ///
    /// <b>为什么几何全部在代码里算</b>
    /// ---------
    /// 与泵（<see cref="PumpElement"/>）、电机（<see cref="MotorElement"/>）、阀门（<see cref="ValveElement"/>）
    /// 同一条纪律：管身要扣掉半个线宽才落在标注尺寸上，箭头的位置 / 大小 / 笔画粗细都随尺寸缩放。
    /// 这些账只在代码里算一遍，模板里只留两个 <c>Path</c> 绑只读几何，一个算术都不写。
    ///
    /// 运行时的接法：把 <see cref="State"/> 绑到工程变量上（枚举名，或 0/1/2），箭头就活了。
    ///
    /// <b>状态与配色不归本类管</b>
    /// ---------
    /// 状态词汇（<see cref="DeviceState"/>）与四个颜色输入被泵、电机、管道三家逐字共用，
    /// 因此上提到了 <see cref="StateVisualElement"/>；本类只留 <see cref="Direction"/>
    /// 与管身、箭头两段几何。
    /// </summary>
    public class PipeElement : StateVisualElement
    {
        /// <summary>管身圆角半径（上限另按管高折半夹一次：管子很细时圆角不能大过半个管高）</summary>
        private const double TubeRadius = 3d;

        /// <summary>相邻箭头中心的间距 / 可画区短边（1.8：排得开，又不至于稀疏到像漏画）</summary>
        private const double ArrowStepRatio = 1.8d;

        /// <summary>箭头半宽 / 可画区短边（0.22：夹角约 100°，比 90° 略钝，小尺寸下更清楚）</summary>
        private const double ArrowHalfWidthRatio = 0.22d;

        /// <summary>箭头半高 / 可画区短边（0.26：高度略大于宽度，看着像"指向"而不是"对勾"）</summary>
        private const double ArrowHalfHeightRatio = 0.26d;

        /// <summary>箭头笔画粗细 / 可画区短边</summary>
        private const double ArrowThicknessRatio = 0.10d;

        private const int MinArrowCount = 1;
        private const int MaxArrowCount = 6;

        #region 输入

        /// <summary>流向（正 / 反 / 不画箭头）</summary>
        public static readonly DependencyProperty DirectionProperty = DependencyProperty.Register(
            nameof(Direction), typeof(PipeFlowDirection), typeof(PipeElement),
            new FrameworkPropertyMetadata(
                PipeFlowDirection.LeftToRight, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

        #endregion

        #region 推导结果（只读）

        private static readonly DependencyPropertyKey TubeGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(TubeGeometry), typeof(Geometry), typeof(PipeElement), new PropertyMetadata(null));

        /// <summary>管身几何：可画区的圆角矩形（只读）</summary>
        public static readonly DependencyProperty TubeGeometryProperty = TubeGeometryPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ArrowGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ArrowGeometry), typeof(Geometry), typeof(PipeElement), new PropertyMetadata(null));

        /// <summary>流向箭头几何：沿管腔等距排开的一组折线（只读；流向选「不画箭头」时为空）</summary>
        public static readonly DependencyProperty ArrowGeometryProperty = ArrowGeometryPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ArrowThicknessPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ArrowThickness), typeof(double), typeof(PipeElement), new PropertyMetadata(0d));

        /// <summary>箭头笔画粗细（只读：按可画区短边算，跟着管子缩放）</summary>
        public static readonly DependencyProperty ArrowThicknessProperty = ArrowThicknessPropertyKey.DependencyProperty;

        #endregion

        static PipeElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(PipeElement),
                new FrameworkPropertyMetadata(typeof(PipeElement)));
        }

        /// <summary>流向（见 <see cref="DirectionProperty"/>）</summary>
        public PipeFlowDirection Direction
        {
            get => (PipeFlowDirection)GetValue(DirectionProperty);
            set => SetValue(DirectionProperty, value);
        }

        /// <summary>管身几何（只读，见 <see cref="TubeGeometryProperty"/>）</summary>
        public Geometry? TubeGeometry => (Geometry?)GetValue(TubeGeometryProperty);

        /// <summary>流向箭头几何（只读，见 <see cref="ArrowGeometryProperty"/>）</summary>
        public Geometry? ArrowGeometry => (Geometry?)GetValue(ArrowGeometryProperty);

        /// <summary>箭头笔画粗细（只读，见 <see cref="ArrowThicknessProperty"/>）</summary>
        public double ArrowThickness => (double)GetValue(ArrowThicknessProperty);

        protected override void RebuildStateVisual()
        {
            // ---- 与尺寸有关的三项 ----

            double inset = StrokeThickness / 2d + 1d; // 半线宽 + 1 像素呼吸空间，外沿才落在标注尺寸上

            double width = ActualWidth - 2d * inset;
            double height = ActualHeight - 2d * inset;

            if (!(width > 0d) || !(height > 0d))
            {
                // 还没布局：几何量留空。尺寸一到（SizeChanged）就会重算，
                // 而不是在这里拿一个猜测的尺寸画出一段待会儿会跳一下的管子。
                SetValue(TubeGeometryPropertyKey, null);
                SetValue(ArrowGeometryPropertyKey, null);
                SetValue(ArrowThicknessPropertyKey, 0d);
                return;
            }

            // 管身铺满整个可画区：管道与泵 / 电机不同，它没有"符号之外还要留一行位号"的需求
            // （理由见类注释），所以可画区就是管腔，不留白。
            double radius = Math.Min(TubeRadius, Math.Min(width, height) / 2d);
            SetValue(TubeGeometryPropertyKey,
                Freeze(new RectangleGeometry(new Rect(inset, inset, width, height), radius, radius)));

            // 箭头的一切尺寸都按"可画区短边"算：管子被拉成又长又扁时，短边就是管高，
            // 箭头跟着管高走，不会被拉成一只扁嘴；被拉成又短又高时，短边是管长，
            // 箭头又不会横向冲出管口。一个 min 就把两个方向都兜住了。
            double size = Math.Min(width, height);

            SetValue(ArrowThicknessPropertyKey, size * ArrowThicknessRatio);
            SetValue(ArrowGeometryPropertyKey, BuildArrows(inset, width, height, size));
        }

        /// <summary>
        /// 流向箭头：沿管腔等距排开的一组"人字"折线，顶点指着介质前进的方向。
        ///
        /// 数量由"管身排得下几个"现算（步距 <see cref="ArrowStepRatio"/> 倍短边），
        /// 再夹在 <see cref="MinArrowCount"/> ~ <see cref="MaxArrowCount"/> 之间——
        /// 短管至少一个箭头（否则一段管子上空空的，读不出流向），长管也不排成一串。
        ///
        /// 位置用 <c>(i + 1) / (count + 1)</c> 等分：两端各留一份空档，
        /// 箭头永远不会顶在管口上（管口在工艺图上往往还要接别的图形）。
        /// </summary>
        private Geometry? BuildArrows(double inset, double width, double height, double size)
        {
            if (Direction == PipeFlowDirection.None) return null;

            double step = size * ArrowStepRatio;
            int count = (int)Math.Round(width / step, MidpointRounding.AwayFromZero);
            count = Math.Clamp(count, MinArrowCount, MaxArrowCount);

            double halfWidth = size * ArrowHalfWidthRatio;
            double halfHeight = size * ArrowHalfHeightRatio;
            double centerY = inset + height / 2d;
            bool reverse = Direction == PipeFlowDirection.RightToLeft;

            var geometry = new PathGeometry();

            for (int i = 0; i < count; i++)
            {
                double centerX = inset + width * (i + 1) / (count + 1);

                // 人字只有三个点：两个"端头"（上、下）与一个"尖"（中间那个折点）。
                // 尖落在前进方向那一侧、端头留在另一侧，所以两个方向就是把这一对 x 对调。
                // 起笔点走的是"端头"，不是尖——第一版把端头命名成 tipX，两个方向就整体反了
                // （LeftToRight 画出来朝左，靠离屏证据图才看出来）。
                double tipX = reverse ? centerX - halfWidth : centerX + halfWidth;
                double tailX = reverse ? centerX + halfWidth : centerX - halfWidth;

                var figure = new PathFigure
                {
                    StartPoint = new Point(tailX, centerY - halfHeight),
                    IsClosed = false,
                    IsFilled = false,
                };
                figure.Segments.Add(new LineSegment(new Point(tipX, centerY), true));
                figure.Segments.Add(new LineSegment(new Point(tailX, centerY + halfHeight), true));

                geometry.Figures.Add(figure);
            }

            return Freeze(geometry);
        }

        /// <summary>
        /// 冻结几何：几何量每次重算都新建，本来不存在"多图元共享一份"的问题；
        /// 冻结是为了让算完的几何从类型上就不可改（与泵、电机、阀门同一条纪律）。
        /// </summary>
        private static Geometry Freeze(Geometry geometry)
        {
            geometry.Freeze();
            return geometry;
        }
    }
}
