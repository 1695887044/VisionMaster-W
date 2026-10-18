using System;
using System.Windows;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 电机图元（TypeKey = <c>Hmi.Motor</c>）：圆机身里一个 M，机身顶上一个小方盒（接线盒）。
    /// ---------
    /// 状态词汇（<see cref="DeviceState"/>）与四个颜色输入被泵、电机、管道三家逐字共用，
    /// 因此上提到了 <see cref="StateVisualElement"/>；本类只留机身、接线盒、M 三段几何。
    /// </summary>
    /// <remarks>partial 的理由与 <see cref="PumpElement"/> 相同（链上形态一致、留好加私有属性的口）。</remarks>
    public partial class MotorElement : StateVisualElement
    {
        /// <summary>顶部留给接线盒的高度比例（0.18）</summary>
        private const double BoxRatio = 0.18d;

        /// <summary>底部留给位号标签的高度比例（0.25，与泵同一个口径，两行字在画面上一般高）</summary>
        private const double LabelRatio = 0.25d;

        /// <summary>接线盒宽度 / 机身直径（0.5：够醒目，又不至于宽过机身看起来像块板子）</summary>
        private const double BoxWidthRatio = 0.5d;

        /// <summary>接线盒下沿探进机身多少（机身直径的比例）；探一点点才像"装在机身上"，悬空就断了</summary>
        private const double BoxOverlapRatio = 0.12d;

        /// <summary>M 的半宽 / 机身半径（0.46：左右各留出一圈机身的厚度）</summary>
        private const double LetterHalfWidthRatio = 0.46d;

        /// <summary>M 的半高 / 机身半径（0.55：上下同样留白）</summary>
        private const double LetterHalfHeightRatio = 0.55d;

        /// <summary>M 中间那个尖往下扎到哪儿（占整个字高的比例；0.6 是常规无衬线 M 的比例）</summary>
        private const double LetterMiddleRatio = 0.6d;

        /// <summary>M 的笔画粗细 / 机身半径（0.18：笔画与字腔大致等宽，小尺寸下不糊成一团）</summary>
        private const double LetterThicknessRatio = 0.18d;

        #region 推导结果（只读）

        private static readonly DependencyPropertyKey BodyGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(BodyGeometry), typeof(Geometry), typeof(MotorElement), new PropertyMetadata(null));

        /// <summary>机身几何：可画区的内接圆（只读）</summary>
        public static readonly DependencyProperty BodyGeometryProperty = BodyGeometryPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey BoxGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(BoxGeometry), typeof(Geometry), typeof(MotorElement), new PropertyMetadata(null));

        /// <summary>接线盒几何：压在机身顶上的圆角小方块（只读）</summary>
        public static readonly DependencyProperty BoxGeometryProperty = BoxGeometryPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey LetterGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(LetterGeometry), typeof(Geometry), typeof(MotorElement), new PropertyMetadata(null));

        /// <summary>字母 M 的几何：一段不闭合的折线（只描边，不填充），只读</summary>
        public static readonly DependencyProperty LetterGeometryProperty = LetterGeometryPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey LetterThicknessPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(LetterThickness), typeof(double), typeof(MotorElement), new PropertyMetadata(0d));

        /// <summary>字母 M 的笔画粗细（随机身缩放，只读）</summary>
        public static readonly DependencyProperty LetterThicknessProperty = LetterThicknessPropertyKey.DependencyProperty;

        #endregion

        static MotorElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(MotorElement),
                new FrameworkPropertyMetadata(typeof(MotorElement)));
        }

        /// <summary>机身几何（只读，见 <see cref="BodyGeometryProperty"/>）</summary>
        public Geometry? BodyGeometry => (Geometry?)GetValue(BodyGeometryProperty);

        /// <summary>接线盒几何（只读，见 <see cref="BoxGeometryProperty"/>）</summary>
        public Geometry? BoxGeometry => (Geometry?)GetValue(BoxGeometryProperty);

        /// <summary>字母 M 的几何（只读，见 <see cref="LetterGeometryProperty"/>）</summary>
        public Geometry? LetterGeometry => (Geometry?)GetValue(LetterGeometryProperty);

        /// <summary>字母 M 的笔画粗细（只读，见 <see cref="LetterThicknessProperty"/>）</summary>
        public double LetterThickness => (double)GetValue(LetterThicknessProperty);

        protected override void RebuildStateVisual()
        {
            // ---- 与尺寸有关的四项 ----

            double inset = StrokeThickness / 2d + 1d; // 半线宽 + 1 像素呼吸空间，外沿才落在标注尺寸上

            // 上下各切一条：顶上留给接线盒，底下留给位号（理由见 BoxRatio / LabelRatio）。
            double regionTop = ActualHeight * BoxRatio;
            double regionBottom = ActualHeight * (1d - LabelRatio);

            double width = ActualWidth - 2d * inset;
            double height = (regionBottom - regionTop) - 2d * inset;

            if (!(width > 0d) || !(height > 0d))
            {
                // 还没布局：几何量留空。尺寸一到（SizeChanged）就会重算，
                // 而不是在这里拿一个猜测的尺寸画出一个待会儿会跳一下的电机。
                SetValue(BodyGeometryPropertyKey, null);
                SetValue(BoxGeometryPropertyKey, null);
                SetValue(LetterGeometryPropertyKey, null);
                SetValue(LetterThicknessPropertyKey, 0d);
                return;
            }

            // 机身取"可画区的内接圆"：宽高不等时按短边，圆永远不会被拉成椭圆
            //（现场符号里的电机机身是正圆，拉扁了就不像电机了）。
            double diameter = Math.Min(width, height);
            double radius = diameter / 2d;
            double centerX = inset + width / 2d;
            double centerY = (regionTop + regionBottom) / 2d;

            SetValue(BodyGeometryPropertyKey,
                Freeze(new EllipseGeometry(new Point(centerX, centerY), radius, radius)));

            SetValue(BoxGeometryPropertyKey, BuildBox(inset, centerX, centerY, radius, diameter));

            SetValue(LetterGeometryPropertyKey, BuildLetter(centerX, centerY, radius));

            SetValue(LetterThicknessPropertyKey, radius * LetterThicknessRatio);
        }

        /// <summary>
        /// 接线盒：机身顶上一个小圆角方块，下沿探进机身一点点。
        ///
        /// 为什么下沿要探进去而不是贴着切点放：圆与方只有一个切点，
        /// 贴着放会看到方块的底边与圆弧之间裂开一条缝，像是没装上去；
        /// 探进去一截，方块自己的底色就把那段圆弧盖住，看上去才是一体的。
        /// 这个"盖住"是画法而不是遮挡关系——方块与机身在同一张画布上，谁后画谁在上（见模板的图层次序）。
        ///
        /// 顶边贴着内缩线（<paramref name="inset"/>）而不是 0：
        /// 与机身圆同一个账——描边是骑在路径上的，外沿要落在标注尺寸上，路径就得内缩半个线宽。
        /// </summary>
        private static Geometry? BuildBox(double inset, double centerX, double centerY, double radius, double diameter)
        {
            double boxWidth = diameter * BoxWidthRatio;
            double boxTop = inset;
            double boxBottom = (centerY - radius) + diameter * BoxOverlapRatio;
            double boxHeight = boxBottom - boxTop;

            // 控件被拉得又扁又矮时，顶上那条缝可能还装不下接线盒——
            // 那就干脆不画（留空），而不是画一个高度为负、渲染出来是反的方块。
            if (!(boxWidth > 0d) || !(boxHeight > 0d))
                return null;

            // 圆角半径不超过半高：超过半高会被 WPF 自己收敛，但显式取 min 意图更清楚
            double cornerRadius = Math.Min(3d, boxHeight / 2d);

            return Freeze(new RectangleGeometry(
                new Rect(centerX - boxWidth / 2d, boxTop, boxWidth, boxHeight),
                cornerRadius,
                cornerRadius));
        }

        /// <summary>
        /// 字母 M：一段不闭合的折线（左下 → 左上 → 中间尖 → 右上 → 右下）。
        ///
        /// 中间那个尖往下扎到整个字高的 <see cref="LetterMiddleRatio"/> 处，
        /// 这是无衬线 M 的常规比例；扎到底（1.0）会变成 W 的倒影，扎得太浅又像两道竖线。
        ///
        /// 折线本身没有面积，所以模板上只描边不填充；粗细由 <see cref="LetterThickness"/> 给出。
        /// </summary>
        private static Geometry BuildLetter(double centerX, double centerY, double radius)
        {
            double halfWidth = radius * LetterHalfWidthRatio;
            double halfHeight = radius * LetterHalfHeightRatio;

            double left = centerX - halfWidth;
            double right = centerX + halfWidth;
            double top = centerY - halfHeight;
            double bottom = centerY + halfHeight;
            double middle = top + (bottom - top) * LetterMiddleRatio;

            var figure = new PathFigure { StartPoint = new Point(left, bottom), IsClosed = false, IsFilled = false };
            figure.Segments.Add(new LineSegment(new Point(left, top), true));
            figure.Segments.Add(new LineSegment(new Point(centerX, middle), true));
            figure.Segments.Add(new LineSegment(new Point(right, top), true));
            figure.Segments.Add(new LineSegment(new Point(right, bottom), true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            return Freeze(geometry);
        }

        /// <summary>
        /// 冻结几何：几何量每次重算都新建，本来不存在"多图元共享一份"的问题；
        /// 冻结是为了让算完的几何从类型上就不可改（与泵、阀门、表盘同一条纪律）。
        /// </summary>
        private static Geometry Freeze(Geometry geometry)
        {
            geometry.Freeze();
            return geometry;
        }
    }
}
