using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VisionMaster.Scada.Controls
{
    /// <summary>多态灯的状态</summary>
    public enum LampState
    {
        /// <summary>熄灭（默认）：停机、离线、数据没来——"没有信息"本身也是一种状态，得画得出来</summary>
        Off,

        /// <summary>点亮：运行中、正常、已到位</summary>
        On,

        /// <summary>警告：越限但还能跑（温度偏高、余量偏低），黄灯</summary>
        Warning,

        /// <summary>报警：故障、急停、联锁断开，红灯</summary>
        Alarm,
    }

    /// <summary>
    /// 多态灯图元（TypeKey = <c>Hmi.Lamp</c>）：一根<b>多状态</b>变量的可视化身。
    ///
    /// 与指示灯（<see cref="IndicatorElement"/>）的分工
    /// ---------
    /// 指示灯只有两种颜色，吃一个布尔，适合"开/关""到位/未到位"。
    /// 多态灯有四种颜色，吃一个状态枚举，适合"停机/运行/警告/报警"这种一条产线上最常见的四级状态——
    /// 现场若用四个指示灯去表达，占地方且容易看错（操作员扫一眼画面，颜色比位置先被认出来）。
    ///
    /// 灯色同样是<b>推导</b>出来的：由 <see cref="State"/> 从四个颜色输入里挑一个，
    /// 结果放在只读的 <see cref="LampBrush"/> 上供模板绑定。理由与指示灯一致——
    /// 可写属性一旦有第二个写入源，就会出现"绑了变量后颜色偶尔不对"这种极难复现的问题。
    ///
    /// <b>为什么不做闪烁</b>
    /// ---------
    /// 报警闪烁是现场常见诉求，但它需要一条<b>全画面统一</b>的节拍：若每个灯各起一个 DispatcherTimer，
    /// 同一屏上几个报警灯会各闪各的（相位对不齐，看上去像画面卡了），而定时器数量还随图元数增长。
    /// 正确做法是运行态宿主养一个节拍源、统一推进（S11 报警系统一并做），
    /// 这里先不埋一个"每灯一个定时器"的坑。
    ///
    /// 运行时的接法：把 <see cref="State"/> 绑到工程变量上（整数 0~3 或按枚举名），灯就跟着状态变。
    /// </summary>
    [TemplatePart(Name = PartLampCircle, Type = typeof(Ellipse))]
    [TemplatePart(Name = PartLampSquare, Type = typeof(Rectangle))]
    public partial class LampElement : ScadaElementBase
    {
        /// <summary>模板部件名：圆形灯体</summary>
        public const string PartLampCircle = "LampCircle";

        /// <summary>模板部件名：方形灯体</summary>
        public const string PartLampSquare = "LampSquare";

        // 声明次序 = 属性面板里的行序（生成器按源码次序收集）：状态在前、四色在后，
        // 与"先绑状态、再按需调色"的配置习惯一致。

        /// <summary>当前状态</summary>
        [ScadaProperty(Key = "State", DisplayName = "状态", Kind = ElementPropertyKind.Choice,
                       Group = "状态", DefaultValue = "Off", IsBindable = true,
                       Choices = new[] { "Off", "On", "Warning", "Alarm" },
                       Description = "运行态把工程变量绑到这里：整数 0=熄灭 1=点亮 2=警告 3=报警，"
                                   + "写枚举名（On/Warning/Alarm）也认")]
        public partial LampState State { get; set; }

        /// <summary>熄灭色</summary>
        [ScadaProperty(Key = "OffColor", DisplayName = "熄灭色", Kind = ElementPropertyKind.Color,
                       Group = "状态", DefaultValue = "#FF3A3A3A", IsBindable = true)]
        public partial Brush? OffColor { get; set; }

        /// <summary>点亮色</summary>
        [ScadaProperty(Key = "OnColor", DisplayName = "点亮色", Kind = ElementPropertyKind.Color,
                       Group = "状态", DefaultValue = "#FF34C759", IsBindable = true)]
        public partial Brush? OnColor { get; set; }

        /// <summary>警告色</summary>
        [ScadaProperty(Key = "WarningColor", DisplayName = "警告色", Kind = ElementPropertyKind.Color,
                       Group = "状态", DefaultValue = "#FFFFB020", IsBindable = true)]
        public partial Brush? WarningColor { get; set; }

        /// <summary>报警色</summary>
        [ScadaProperty(Key = "AlarmColor", DisplayName = "报警色", Kind = ElementPropertyKind.Color,
                       Group = "状态", DefaultValue = "#FFE03A2B", IsBindable = true)]
        public partial Brush? AlarmColor { get; set; }

        /// <summary>灯体形状（与指示灯共用同一套形状词汇）</summary>
        [ScadaProperty(Key = "Shape", DisplayName = "形状", Kind = ElementPropertyKind.Choice,
                       Group = "外观", DefaultValue = "Circle", Choices = new[] { "Circle", "Square" },
                       Description = "与指示灯共用同一套形状词汇：多灯密集排列时方形更省地方")]
        public partial IndicatorShape Shape { get; set; }

        private static readonly DependencyPropertyKey LampBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(LampBrush), typeof(Brush), typeof(LampElement),
            new PropertyMetadata(Brushes.Transparent));

        /// <summary>当前该显示的灯色（由 State 从四个颜色里挑一个，只读）</summary>
        public static readonly DependencyProperty LampBrushProperty = LampBrushPropertyKey.DependencyProperty;

        static LampElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(LampElement),
                new FrameworkPropertyMetadata(typeof(LampElement)));
        }

        /// <summary>当前灯色（只读，见 <see cref="LampBrushProperty"/>）</summary>
        public Brush? LampBrush => (Brush?)GetValue(LampBrushProperty);

        protected override void OnElementRefreshed() => ApplyStrokeInset(1);

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdateShapeVisibility();
        }

        // 五个输入属性共用同一条副作用（重算灯色），形状单独一条。
        partial void OnStatePropertyChanged() => UpdateLampBrush();
        partial void OnOffColorPropertyChanged() => UpdateLampBrush();
        partial void OnOnColorPropertyChanged() => UpdateLampBrush();
        partial void OnWarningColorPropertyChanged() => UpdateLampBrush();
        partial void OnAlarmColorPropertyChanged() => UpdateLampBrush();
        partial void OnShapePropertyChanged() => UpdateShapeVisibility();

        /// <summary>
        /// 圆/方两块灯体同一时刻只显示一块。形状是个纯模板层的选择，但本环境下
        /// ControlTemplate.Triggers 里的 DataTrigger 不触发（见 ScadaElementBase.SetPartVisible），
        /// 所以只能由代码翻显隐；模板里那两块也就不写 Visibility 初值。
        /// </summary>
        private void UpdateShapeVisibility()
        {
            var square = Shape == IndicatorShape.Square;
            SetPartVisible(PartLampCircle, !square);
            SetPartVisible(PartLampSquare, square);
        }

        private void UpdateLampBrush()
            => SetValue(LampBrushPropertyKey, PickColor() ?? Brushes.Transparent);

        /// <summary>按当前状态挑颜色；没配到颜色就退回透明（宁可看不见，也别拿别人的颜色顶上）</summary>
        private Brush? PickColor() => State switch
        {
            LampState.On => OnColor,
            LampState.Warning => WarningColor,
            LampState.Alarm => AlarmColor,
            _ => OffColor,
        };
    }
}
