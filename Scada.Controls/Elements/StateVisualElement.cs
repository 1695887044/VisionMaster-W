using System.Windows;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 「按设备状态变色」这一类图元的公共底座：泵（<see cref="PumpElement"/>）、
    /// 电机（<see cref="MotorElement"/>）、管道（<see cref="PipeElement"/>）三家共用。
    ///
    /// <b>它替派生类扛下三件事</b>
    /// ---------
    /// <code>
    ///   State（停 / 转 / 故障）──┬─▶ StateBrush（只读：从三个状态色里挑一个）
    ///   TrackColor（底色）       │
    ///   StoppedColor（停机色）   │
    ///   RunningColor（运行色）   │
    ///   FaultColor（故障色）─────┘
    ///
    ///   ① 6 个依赖属性的声明与 CLR 包装
    ///   ② 三处「随尺寸重算」的生命周期接线（构造首帧 / SizeChanged / 收尾钩子）
    ///   ③ 挑色的那段 switch
    /// </code>
    /// 派生类只剩一件事：实现 <see cref="RebuildStateVisual"/>，把随尺寸变化的几何算出来。
    ///
    /// <b>为什么这三处生命周期也要收上来</b>
    /// ---------
    /// 它们原本在每个图元的构造函数里各写一遍，而且写漏了不报错：漏订 <c>SizeChanged</c>，
    /// 设计期拖大尺寸时图形不跟手；漏掉构造首帧，断言环境（不挂可视树、不跑布局）里读到的是空几何。
    /// 这是「隐式契约」型的样板——收进基类以后，新建一个状态图元不可能再漏。
    ///
    /// <b>为什么不把这些直接挂在 <see cref="ScadaElementBase"/> 上</b>
    /// ---------
    /// 那样「文字」「矩形」「按钮」这些跟设备状态毫无关系的图元也会凭空长出 State 与四个颜色：
    /// 属性面板上多出五行永远用不上的项，绑定下拉框里多出五个永远不该绑的键。
    /// 状态色是一类图元的语言，不是所有图元的语言——所以单开一层中间基类。
    ///
    /// <b>为什么不改用 attached property 一处收口</b>
    /// ---------
    /// attached property 解决的是「把一个属性挂到别人家的类型上」，而这里要收的是
    /// 「六个属性加一段生命周期」。换成 attached 之后，模板里得写
    /// <c>{TemplateBinding local:StateVisualElement.State}</c>，描述符的 <c>TargetProperty</c> 也要跟着改，
    /// 换来的只是更绕的写法；而基类的属性是真长在这个类型上的，<c>{TemplateBinding State}</c> 原样可用。
    ///
    /// <b>与 <see cref="DeviceState"/> 的分工</b>
    /// ---------
    /// 枚举单独一处定义，因为它是<b>词汇</b>——泵、电机、管道必须说同一种话；
    /// 本类收的是<b>这套词汇的用法</b>：怎么挑色、什么时候重算。
    /// </summary>
    public abstract class StateVisualElement : ScadaElementBase
    {
        // 默认色必须冻结：依赖属性默认值被所有实例共享，未冻结的 Freezable 被某实例改到会串到别的实例。
        private static readonly Brush DefaultTrackColor = ScadaBrushes.Frozen("#FF3A3A3A");
        private static readonly Brush DefaultStoppedColor = ScadaBrushes.Frozen("#FF7A7A7A");
        private static readonly Brush DefaultRunningColor = ScadaBrushes.Frozen("#FF34C759");
        private static readonly Brush DefaultFaultColor = ScadaBrushes.Frozen("#FFE03A2B");

        #region 输入

        /// <summary>运行状态（停 / 转 / 故障）</summary>
        public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
            nameof(State), typeof(DeviceState), typeof(StateVisualElement),
            new FrameworkPropertyMetadata(
                DeviceState.Stopped, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

        #endregion

        #region 外观输入

        /// <summary>底色（随状态切换的那一层之外，露出来的那一层）</summary>
        public static readonly DependencyProperty TrackColorProperty = DependencyProperty.Register(
            nameof(TrackColor), typeof(Brush), typeof(StateVisualElement),
            new FrameworkPropertyMetadata(
                DefaultTrackColor, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

        /// <summary>停机色（随状态切换的那一层）</summary>
        public static readonly DependencyProperty StoppedColorProperty = DependencyProperty.Register(
            nameof(StoppedColor), typeof(Brush), typeof(StateVisualElement),
            new FrameworkPropertyMetadata(
                DefaultStoppedColor, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

        /// <summary>运行色（随状态切换的那一层）</summary>
        public static readonly DependencyProperty RunningColorProperty = DependencyProperty.Register(
            nameof(RunningColor), typeof(Brush), typeof(StateVisualElement),
            new FrameworkPropertyMetadata(
                DefaultRunningColor, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

        /// <summary>故障色（随状态切换的那一层）</summary>
        public static readonly DependencyProperty FaultColorProperty = DependencyProperty.Register(
            nameof(FaultColor), typeof(Brush), typeof(StateVisualElement),
            new FrameworkPropertyMetadata(
                DefaultFaultColor, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

        #endregion

        #region 推导结果（只读）

        private static readonly DependencyPropertyKey StateBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(StateBrush), typeof(Brush), typeof(StateVisualElement),
            new PropertyMetadata(Brushes.Transparent));

        /// <summary>随状态切换的那一层的颜色（由状态从三个颜色输入里挑一个，只读）</summary>
        public static readonly DependencyProperty StateBrushProperty = StateBrushPropertyKey.DependencyProperty;

        #endregion

        protected StateVisualElement()
        {
            // 几何量都依赖「此刻多大」，而尺寸只有在布局跑完之后才知道，所以尺寸一变就得重算。
            SizeChanged += (_, _) => RebuildStateVisual();

            // 构造即算第一帧：状态色与尺寸无关，断言环境（不挂可视树、不跑布局）
            // 里不显式算一遍就会读到空值。
            RefreshStateVisual();
        }

        // 收尾钩子：内缩量要扣掉半个线宽，而 StrokeThickness 是在 RefreshCore 里才落到控件上的，
        // 所以几何必须在所有输入落地之后重算一次（与阀门、棒图、表盘同一个理由）。
        protected override void OnElementRefreshed() => RebuildStateVisual();

        /// <summary>运行状态（见 <see cref="StateProperty"/>）</summary>
        public DeviceState State
        {
            get => (DeviceState)GetValue(StateProperty);
            set => SetValue(StateProperty, value);
        }

        /// <summary>底色（见 <see cref="TrackColorProperty"/>）</summary>
        public Brush? TrackColor
        {
            get => (Brush?)GetValue(TrackColorProperty);
            set => SetValue(TrackColorProperty, value);
        }

        /// <summary>停机色（见 <see cref="StoppedColorProperty"/>）</summary>
        public Brush? StoppedColor
        {
            get => (Brush?)GetValue(StoppedColorProperty);
            set => SetValue(StoppedColorProperty, value);
        }

        /// <summary>运行色（见 <see cref="RunningColorProperty"/>）</summary>
        public Brush? RunningColor
        {
            get => (Brush?)GetValue(RunningColorProperty);
            set => SetValue(RunningColorProperty, value);
        }

        /// <summary>故障色（见 <see cref="FaultColorProperty"/>）</summary>
        public Brush? FaultColor
        {
            get => (Brush?)GetValue(FaultColorProperty);
            set => SetValue(FaultColorProperty, value);
        }

        /// <summary>随状态切换的那一层的颜色（只读，见 <see cref="StateBrushProperty"/>）</summary>
        public Brush? StateBrush => (Brush?)GetValue(StateBrushProperty);

        /// <summary>
        /// 本图元任一外观输入变化时重算：先按状态挑色，再让派生类重算几何。
        ///
        /// 派生类自己的视觉输入（例如管道的 <c>Direction</c>）也可以直接挂这个回调——
        /// 它要做的正是「重算一遍」。
        /// </summary>
        protected static void OnVisualInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((StateVisualElement)d).RefreshStateVisual();

        /// <summary>
        /// 重算状态色，再让派生类重算几何。
        /// </summary>
        private void RefreshStateVisual()
        {
            SetValue(StateBrushPropertyKey, PickStateColor() ?? Brushes.Transparent);
            RebuildStateVisual();
        }

        /// <summary>
        /// 按当前状态挑颜色。
        ///
        /// 没配到颜色就退回透明——宁可看不见，也别拿别人的颜色顶上（与多态灯、阀门同一条纪律）。
        /// </summary>
        private Brush? PickStateColor() => State switch
        {
            DeviceState.Running => RunningColor,
            DeviceState.Fault => FaultColor,
            _ => StoppedColor,
        };

        /// <summary>
        /// 派生类在这里重算「随尺寸变化」的几何量。
        ///
        /// 契约：本方法会被基类构造函数调用（构造即算第一帧），因此实现里不得读写派生类的
        /// 实例字段——那时派生类的字段初始化器还没跑。只读依赖属性、const 常量、静态字段都是安全的。
        /// </summary>
        protected abstract void RebuildStateVisual();
    }
}
