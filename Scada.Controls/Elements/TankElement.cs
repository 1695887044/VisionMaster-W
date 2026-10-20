using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 罐体 / 料仓图元（TypeKey = <c>Hmi.Tank</c>）：一只带液位填充的立式容器。
    ///
    /// <b>一个输入推三样东西</b>
    /// ---------
    /// <code>
    ///   Level ──┬─▶ FillRatio（0~1，越界/NaN 收敛）
    ///           ├─▶ FillGeometry（从底部往上填到该比例，裁在罐体轮廓里）
    ///           └─▶ ValueText（= Capacity × FillRatio + Unit）
    /// </code>
    ///
    /// <b>为什么填充用「罐体几何 ∩ 矩形」而不是画一根伸缩的条</b>
    /// ---------
    /// 罐体是圆角矩形，底部两个圆角只有沿着轮廓裁才不会被方角盖住。
    /// 做法与阀门（<see cref="ValveElement"/>）同族：填充与罐体共用同一份
    /// <see cref="TankBodyGeometry"/>，再拿一个矩形裁到「液面高度」——
    /// 无论圆角多大、罐体多宽，液体都严丝合缝地落在轮廓里；比例 0 时矩形高度为 0，
    /// 天然就是「空罐」，不需要为全空单开一条分支。
    ///
    /// <b>为什么几何全部在代码里算</b>
    /// ---------
    /// 罐体要扣掉半个线宽才落在标注尺寸上，圆角半径还要按短边夹一次；
    /// 这些账只在代码里算一遍，模板里只留两个 <c>Path</c> 绑只读几何，一个算术都不写。
    ///
    /// <b>为什么不做液面动画</b>
    /// ---------
    /// 与泵/阀/多态灯同一条纪律：动画需要全画面统一节拍源（S11 报警系统一并做），
    /// 每个图元各起一个定时器会各走各的，看上去像画面卡了。
    ///
    /// 运行时的接法：把 <see cref="Level"/> 绑到工程变量上（0~100 的百分比），罐体就活了。
    /// </summary>
    [TemplatePart(Name = PartTankBody, Type = typeof(Path))]
    [TemplatePart(Name = PartTankFill, Type = typeof(Path))]
    [TemplatePart(Name = PartValue, Type = typeof(TextBlock))]
    public partial class TankElement : ScadaElementBase
    {
        /// <summary>模板部件名：罐体轮廓（Path）</summary>
        public const string PartTankBody = "PART_TankBody";

        /// <summary>模板部件名：液位填充（Path，几何已裁在罐体轮廓内）</summary>
        public const string PartTankFill = "PART_TankFill";

        /// <summary>模板部件名：液位/容量读数（TextBlock）</summary>
        public const string PartValue = "PART_Value";

        // 声明次序 = 属性面板里的行序（生成器按源码次序收集）：数据 → 外观 → 文字。

        /// <summary>液位百分比（0~100）</summary>
        [ScadaProperty(Key = "Level", DisplayName = "液位", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "50", Min = 0, Max = 100, IsBindable = true,
                       Description = "液位百分比（0~100）。运行态把工程变量绑到这里；" +
                                     "NaN 与越界值按 0~100 收敛，坏值不会把整页渲染打断")]
        public partial double Level { get; set; }

        /// <summary>满罐容量（与 <see cref="Unit"/> 配套，决定读数）</summary>
        [ScadaProperty(Key = "Capacity", DisplayName = "容量", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "1000", Min = 0,
                       Description = "满罐对应的工程量（如 1000 L）。读数 = 容量 × 液位比例；" +
                                     "填 0 时读数退回百分比")]
        public partial double Capacity { get; set; }

        /// <summary>容量单位（跟在读数后面的一小段字）</summary>
        [ScadaProperty(Key = "Unit", DisplayName = "单位", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "L",
                       Description = "跟在容量读数后面的一小段字（L / m³ / kg）；留空则只显数值")]
        public partial string? Unit { get; set; }

        /// <summary>是否显示读数</summary>
        [ScadaProperty(Key = "ShowValue", DisplayName = "显示读数", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "True",
                       Description = "关掉只留罐体与液位（读数交给旁边的数值域），默认开")]
        public partial bool ShowValue { get; set; }

        /// <summary>罐体空腔色（未填充部分）</summary>
        [ScadaProperty(Key = "TrackColor", DisplayName = "空腔色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF26313D",
                       Description = "罐体内部未填充部分的底色；深色画面用深蓝灰，浅色画面要调亮")]
        public partial Brush? TrackColor { get; set; }

        #region 推导结果（只读）

        private static readonly DependencyPropertyKey TankBodyGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(TankBodyGeometry), typeof(Geometry), typeof(TankElement), new PropertyMetadata(null));

        /// <summary>罐体轮廓几何（圆角矩形，只读）</summary>
        public static readonly DependencyProperty TankBodyGeometryProperty =
            TankBodyGeometryPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey FillGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(FillGeometry), typeof(Geometry), typeof(TankElement), new PropertyMetadata(null));

        /// <summary>液位填充几何（罐体轮廓 ∩ 底部矩形，只读）</summary>
        public static readonly DependencyProperty FillGeometryProperty =
            FillGeometryPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ValueTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ValueText), typeof(string), typeof(TankElement), new PropertyMetadata(string.Empty));

        /// <summary>容量读数的显示串（如 "500 L"，只读）</summary>
        public static readonly DependencyProperty ValueTextProperty = ValueTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey FillRatioPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(FillRatio), typeof(double), typeof(TankElement), new PropertyMetadata(0d));

        /// <summary>液位比例（0~1，只读；由 Level 收敛得到）</summary>
        public static readonly DependencyProperty FillRatioProperty = FillRatioPropertyKey.DependencyProperty;

        #endregion

        static TankElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(TankElement),
                new FrameworkPropertyMetadata(typeof(TankElement)));
        }

        public TankElement()
        {
            // 几何量都依赖"此刻多大"，而尺寸只有在布局跑完之后才知道——
            // 与阀门、表盘、棒图监听 SizeChanged 是同一个理由。
            SizeChanged += (_, _) => UpdateTank();

            // 构造即算第一帧：断言环境（不挂可视树、不跑布局）里不显式算一遍就会读到空值。
            UpdateTank();
        }

        // 收尾钩子：内缩量要扣掉半个线宽、圆角要按短边夹，而 StrokeThickness / CornerRadius
        // 是在 RefreshCore 里才落到控件上的，所以几何必须在所有输入落地之后重算一次。
        protected override void OnElementRefreshed() => UpdateTank();

        // 模板刚套上时补摆一次：ShowValue 若在套模板之前就落地，那次 SetPartVisible 是空操作
        //（那时还取不到模板部件），这里必须再摆一次。
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdateTank();
        }

        /// <summary>
        /// 直接改依赖属性（不经模型）时也要重算几何：StrokeThickness / CornerRadius 都参与内缩与圆角。
        /// 模型路径最终由 <see cref="OnElementRefreshed"/> 兜底，这里补的是"控件被直接设值"的那条路。
        /// </summary>
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.Property == StrokeThicknessProperty || e.Property == CornerRadiusProperty)
                UpdateTank();
        }

        /// <summary>罐体轮廓几何（只读，见 <see cref="TankBodyGeometryProperty"/>）</summary>
        public Geometry? TankBodyGeometry => (Geometry?)GetValue(TankBodyGeometryProperty);

        /// <summary>液位填充几何（只读，见 <see cref="FillGeometryProperty"/>）</summary>
        public Geometry? FillGeometry => (Geometry?)GetValue(FillGeometryProperty);

        /// <summary>容量读数（只读，见 <see cref="ValueTextProperty"/>）</summary>
        public string? ValueText => (string?)GetValue(ValueTextProperty);

        /// <summary>液位比例（只读，见 <see cref="FillRatioProperty"/>）</summary>
        public double FillRatio => (double)GetValue(FillRatioProperty);

        // 五个输入共用同一条副作用：比例、读数、几何都要重算。
        partial void OnLevelPropertyChanged() => UpdateTank();
        partial void OnCapacityPropertyChanged() => UpdateTank();
        partial void OnUnitPropertyChanged() => UpdateTank();

        // 「显不显读数」是个纯模板层的开关，但本环境下 ControlTemplate.Triggers 里的 DataTrigger
        // 不触发（见 ScadaElementBase.SetPartVisible），所以由代码翻，模板里也不写 Visibility 初值。
        partial void OnShowValuePropertyChanged() => SetPartVisible(PartValue, ShowValue);

        private void UpdateTank()
        {
            // 读数的显隐与尺寸无关，先办掉——下面尺寸为 0 时会提前 return，别把它漏在外面。
            SetPartVisible(PartValue, ShowValue);

            double ratio = ComputeFillRatio();

            // ---- 与尺寸无关的两项：先算，保证断言环境（不跑布局）里也是对的 ----
            SetValue(FillRatioPropertyKey, ratio);
            SetValue(ValueTextPropertyKey, FormatValue(ratio));

            // ---- 与尺寸有关的几何 ----

            double inset = StrokeThickness / 2d + 1d; // 半线宽 + 1 像素呼吸空间，外沿才落在标注尺寸上
            double width = ActualWidth - 2d * inset;
            double height = ActualHeight - 2d * inset;

            if (!(width > 0d) || !(height > 0d))
            {
                // 还没布局：几何量留空。尺寸一到（SizeChanged）就会重算，
                // 而不是在这里拿一个猜测的尺寸画出一个待会儿会跳一下的罐。
                SetValue(TankBodyGeometryPropertyKey, null);
                SetValue(FillGeometryPropertyKey, null);
                return;
            }

            // 坏圆角（NaN/Infinity）按直角画：RectangleGeometry 的半径必须是有限非负数，
            // 一个手工改坏的 .vms 不该在布局阶段抛异常。
            double radius = double.IsFinite(CornerRadius)
                ? Math.Clamp(CornerRadius, 0d, Math.Min(width, height) / 2d)
                : 0d;

            var body = new RectangleGeometry(new Rect(inset, inset, width, height), radius, radius);
            SetValue(TankBodyGeometryPropertyKey, Freeze(body));

            double fillHeight = height * ratio;

            if (fillHeight <= 0d)
            {
                // 空罐：给一个零高度矩形而不是 null。null 在 Path 上同样"不画"，
                // 但断言/诊断需要区分"还没布局"（null）与"液位为 0"（空几何）。
                SetValue(FillGeometryPropertyKey,
                    Freeze(new RectangleGeometry(new Rect(inset, inset + height, width, 0d))));
                return;
            }

            // 液面矩形：从罐底往上 fillHeight；与罐体轮廓求交后，底部圆角自然被保留。
            var liquid = new RectangleGeometry(
                new Rect(inset, inset + height - fillHeight, width, fillHeight));

            var fill = new CombinedGeometry(GeometryCombineMode.Intersect, body, liquid);
            SetValue(FillGeometryPropertyKey, Freeze(fill));
        }

        /// <summary>
        /// 液位比例（0~1）。
        ///
        /// 非有限值与越界值一律收敛：一个坏值不该把整页渲染打断（与棒图、表盘、阀门同一条纪律）。
        /// NaN 收敛到 0（"不知道"按空罐画，宁可少画也不要把罐画满）；
        /// ±Infinity 分别收敛到 1 / 0。
        /// </summary>
        private double ComputeFillRatio()
        {
            double level = Level;

            if (double.IsNaN(level))
                return 0d;

            return Math.Clamp(level / 100d, 0d, 1d);
        }

        /// <summary>
        /// 读数：容量 × 液位比例 + 单位。
        ///
        /// 容量没配（0 或坏值）时退回百分比——否则会显示一个永远为 0 的"0 L"，
        /// 让操作员误以为罐是空的。用不变文化：.vms 跨机器交换，小数点不该跟着系统区域走。
        /// </summary>
        private string FormatValue(double ratio)
        {
            double capacity = Capacity;

            if (double.IsFinite(capacity) && capacity > 0d)
            {
                string volume = (capacity * ratio).ToString("0.#", CultureInfo.InvariantCulture);
                return string.IsNullOrEmpty(Unit) ? volume : volume + " " + Unit;
            }

            return (ratio * 100d).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>
        /// 冻结几何：几何量每次重算都新建，本来不存在"多图元共享一份"的问题；
        /// 冻结是为了让算完的几何从类型上就不可改（与表盘、阀门同一条纪律）。
        /// </summary>
        private static Geometry Freeze(Geometry geometry)
        {
            geometry.Freeze();
            return geometry;
        }
    }
}
