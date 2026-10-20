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
    public abstract partial class StateVisualElement : ScadaElementBase
    {
        // 默认色由 ScadaPropertyDefaults.Resolve 在类型静态构造期解析并冻结（见 [ScadaProperty] 的 DefaultValue），
        // 所以不再需要手写静态 Brush 字段。

        #region 输入

        /// <summary>运行状态（停 / 转 / 故障）</summary>
        [ScadaProperty(Key = "State", DisplayName = "状态", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "Stopped",
                       Choices = new[] { "Stopped", "Running", "Fault" }, IsBindable = true,
                       Description = "运行态把工程变量绑到这里：整数 0=停止 1=运行 2=故障，写枚举名（Running/Fault）也认；" +
                                     "认不出的值按停止收敛，坏数据不会把整页渲染打断")]
        public partial DeviceState State { get; set; }

        #endregion

        #region 外观输入

        /// <summary>底色（随状态切换的那一层之外，露出来的那一层）</summary>
        [ScadaProperty(Key = "TrackColor", DisplayName = "底色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF3A3A3A",
                       Description = "随状态切换的那一层之外露出来的底色")]
        public partial Brush? TrackColor { get; set; }

        /// <summary>停机色（随状态切换的那一层）</summary>
        [ScadaProperty(Key = "StoppedColor", DisplayName = "停机色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF7A7A7A",
                       Description = "停机 / 停流时的颜色；灰色 = 没在动，与多态灯同一个语汇")]
        public partial Brush? StoppedColor { get; set; }

        /// <summary>运行色（随状态切换的那一层）</summary>
        [ScadaProperty(Key = "RunningColor", DisplayName = "运行色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF34C759",
                       Description = "运行 / 流动时的颜色；绿色 = 在动")]
        public partial Brush? RunningColor { get; set; }

        /// <summary>故障色（随状态切换的那一层）</summary>
        [ScadaProperty(Key = "FaultColor", DisplayName = "故障色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FFE03A2B",
                       Description = "故障 / 异常时的颜色；红色 = 跳闸 / 过载 / 联锁断开")]
        public partial Brush? FaultColor { get; set; }

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

        /// <summary>随状态切换的那一层的颜色（只读，见 <see cref="StateBrushProperty"/>）</summary>
        public Brush? StateBrush => (Brush?)GetValue(StateBrushProperty);

        // 五个输入共用同一条副作用：任一项变了都要重挑状态色、并让派生类重算几何。
        partial void OnStatePropertyChanged() => RefreshStateVisual();
        partial void OnTrackColorPropertyChanged() => RefreshStateVisual();
        partial void OnStoppedColorPropertyChanged() => RefreshStateVisual();
        partial void OnRunningColorPropertyChanged() => RefreshStateVisual();
        partial void OnFaultColorPropertyChanged() => RefreshStateVisual();

        /// <summary>
        /// 本图元任一外观输入变化时重算：先按状态挑色，再让派生类重算几何。
        ///
        /// 派生类自己的视觉输入（例如管道的 <c>Direction</c>）在它的
        /// <c>On&lt;属性名&gt;PropertyChanged</c> 钩子里直接调本方法即可——它要做的正是"重算一遍"。
        /// （迁移到 [ScadaProperty] 之前这里是一条静态依赖属性回调；现在每个属性各有一条钩子，
        /// 不再需要那个静态签名。）
        /// </summary>
        protected void RefreshStateVisual()
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
