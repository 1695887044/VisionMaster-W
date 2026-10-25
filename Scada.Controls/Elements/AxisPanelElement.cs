using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Prism.Commands;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 轴面板（TypeKey = <c>Hmi.AxisPanel</c>）：设计期选一组轴，运行期用下拉框切到其中一根，
    /// 面板只显示"当前轴"的位置/速度/状态灯。
    ///
    /// 为什么是"轴选择器 + 单轴视图"，而不是多轴同屏
    /// ---------
    /// 同屏多轴要么把面板撑成一张表（调试时看不清单轴的细节），要么逼每个字段各绑一根轴
    /// （用户要在属性面板里配 N 次）。现场调试的真实动作是"先看 X、再看 Y"，
    /// 所以候选集放在下拉里、视图只留一根轴——与参考模板的轴调试台一致。
    ///
    /// 它跟别的图元有什么不同
    /// ---------
    /// 别的图元是"一根变量 → 一个外观"，接 <see cref="ScadaBinding"/>；
    /// 本图元接的是宿主注入的 <see cref="ScadaRuntimeContext.AxisSource"/>：
    /// 轴不是工程变量（见 MotionStatePublisher 的注释），而"当前显示哪根轴"是
    /// 控件自己的运行期选择状态，不该写进 .vms。
    ///
    /// 数据从哪来、什么时候刷新
    /// ---------
    /// 搭全画面统一节拍（<see cref="ScadaBeatSource"/>，默认 200ms）**拉**当前轴的快照：
    /// 运动卡自己 10ms 轮询一次，若按值变化推事件，一屏几十个面板会把 UI 线程刷满。
    /// 节拍里同时比对 <see cref="IScadaAxisSource.Version"/>，轴表增删/改名时重建下拉项。
    ///
    /// 设计态（<see cref="ScadaElementBase.RuntimeContext"/> 为 null）
    /// ---------
    /// 只按 <see cref="ScadaAxisOptionsCodec.Key"/> 里的候选轴名摆出下拉框与版面，
    /// 量值显示"—"并提示"设计预览"。编辑器里不运行、也没有运动数据，这是常态。
    ///
    /// 本轮只读：面板不提供使能/点动/回零/停止——那些是运动命令，必须走动作表、
    /// 门禁与点动看门狗，不能由图元直接下发（见方案的分批约定）。
    /// </summary>
    public partial class AxisPanelElement : ScadaElementBase
    {
        // ---- 可配置颜色（文字/填充/边框/圆角/字号走基类打包助手，不在这里重复声明） ----

        /// <summary>激活态强调色（使能/到位/运动/原点灯，以及位置大字）</summary>
        [ScadaProperty(Key = "AccentColor", DisplayName = "强调色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.State, DefaultValue = "#FFFF6161",
                       Description = "激活态的颜色：使能/到位/运动中/原点灯与位置大字；默认取参考模板的珊瑚红")]
        public partial Brush? AccentColor { get; set; }

        /// <summary>告警态颜色（报警/正负限位/急停）</summary>
        [ScadaProperty(Key = "AlarmColor", DisplayName = "告警色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.State, DefaultValue = "#FFE03A2B",
                       Description = "报警/限位/急停灯的颜色；与多态灯、报警条的「严重红」同一个色")]
        public partial Brush? AlarmColor { get; set; }

        /// <summary>熄灭态颜色（灯不亮时的底色）</summary>
        [ScadaProperty(Key = "OffColor", DisplayName = "熄灭色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.State, DefaultValue = "#FF2A2D31",
                       Description = "灯不亮时的底色；比卡片底稍亮一档，才看得出「这里有一只灯」")]
        public partial Brush? OffColor { get; set; }

        /// <summary>次要文字色（单位、说明、轴卡片副标题）</summary>
        [ScadaProperty(Key = "MutedColor", DisplayName = "次要文字色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "#FF9A9A9D",
                       Description = "单位、状态说明与轴卡片副标题的颜色")]
        public partial Brush? MutedColor { get; set; }

        /// <summary>是否显示操作按钮（使能/点动/回零/停止/清报警）</summary>
        [ScadaProperty(Key = "ShowCommands", DisplayName = "显示操作按钮", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Operation, DefaultValue = "True",
                       Description = "关掉就退化成纯监视面板：只显示位置/速度/状态灯，不出现任何会动机构的按钮")]
        public partial bool ShowCommands { get; set; }

        /// <summary>点动速度（mm/s）。按住点动按钮时按这个速度走。</summary>
        [ScadaProperty(Key = "JogVelocity", DisplayName = "点动速度", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Operation, DefaultValue = "10", Min = 0.1, Max = 10000,
                       Description = "按住点动按钮时的速度（mm/s）；方向由按钮决定")]
        public partial double JogVelocityMmPerS { get; set; }

        /// <summary>
        /// 点动模式：连续（按住才走）或单步（按一次走一格）。
        ///
        /// 为什么做成"一个模式开关"而不是另加两颗按钮：面板高度是固定的，
        /// 两种点动是对**同一对**点动键的两种解释；多长一行按钮就要牺牲位置大字与状态灯。
        /// </summary>
        [ScadaProperty(Key = "JogMode", DisplayName = "点动模式", Kind = ElementPropertyKind.Choice,
                       Choices = new[] { nameof(ScadaJogMode.Continuous), nameof(ScadaJogMode.Step) },
                       Group = ScadaPropertyGroups.Operation, DefaultValue = nameof(ScadaJogMode.Continuous),
                       Description = "连续 = 按住才走（松开即停）；单步 = 按一次走一个「点动步长」，目标 = 当前位置 ± 步长")]
        public partial ScadaJogMode JogMode { get; set; }

        /// <summary>
        /// 单步点动的步长（mm）。只在 <see cref="ScadaJogMode.Step"/> 下有意义。
        ///
        /// 口径：0.1 mm 精度、0~50 mm。取 0 表示"没给步长"——此时单步点动键灰掉，
        /// 而不是发一个 0 mm 的空定位（那会被门禁当成"目标=当前位置"，看起来像按钮坏了）。
        /// </summary>
        [ScadaProperty(Key = "JogStepMm", DisplayName = "点动步长", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Operation, DefaultValue = "1", Min = 0, Max = 50,
                       Description = "单步点动的步长（mm，0.1 精度，0~50）；目标 = 当前位置 ± 步长。0 = 单步点动不可用")]
        public partial double JogStepMm { get; set; }

        /// <summary>
        /// 示教/定位前是否弹二次确认（默认开）。
        ///
        /// 为什么默认开：示教写的是**方案配置**（随方案落盘），定位会让机构真的走；
        /// 两者都不该因为手滑一下就发生。关掉之后是一键直接执行——权限仍照常检查，
        /// 这个开关只控制"多问一句"，不是权限的替代品。
        /// </summary>
        [ScadaProperty(Key = "ConfirmCommands", DisplayName = "操作二次确认", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Operation, DefaultValue = "True",
                       Description = "示教/定位前弹出确认框；关掉后一键直接执行（权限仍照常检查）")]
        public partial bool ConfirmCommands { get; set; }

        /// <summary>
        /// 是否显示点位区（示教 / 定位 / 改速度）。
        ///
        /// 默认 false：`Hmi.AxisPanel` 是"只要状态与手动操作"的紧凑预设；
        /// `Hmi.AxisConsole`（轴控台）把默认值覆盖成 true，就是"状态 + 操作 + 点位"的完整调试台。
        /// 同一个控件类、同一份数据绑定，只是预设不同——这样运动与监控永远是同一根轴。
        /// </summary>
        [ScadaProperty(Key = "ShowPoints", DisplayName = "显示点位", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Points, DefaultValue = "False",
                       Description = "显示点位表（示教/定位/改速度）。关掉就是紧凑面板；打开后与运动共用同一个轴选择器")]
        public partial bool ShowPoints { get; set; }

        /// <summary>点位区可见行数（其余滚动）</summary>
        [ScadaProperty(Key = "PointsRows", DisplayName = "点位可见行数", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Points, DefaultValue = "6", Min = 1, Max = 16,
                       Description = "点位区最多显示几行，超出的滚动；这个值决定点位区高度")]
        public partial int PointsRows { get; set; }

        private static readonly Brush FallbackAccent = ScadaBrushes.Frozen("#FFFF6161");
        private static readonly Brush FallbackAlarm = ScadaBrushes.Frozen("#FFE03A2B");
        private static readonly Brush FallbackOff = ScadaBrushes.Frozen("#FF2A2D31");
        private static readonly Brush FallbackMuted = ScadaBrushes.Frozen("#FF9A9A9D");

        // ---- 只读运行态属性（模板与断言都读它们，不在代码里直接改模板元素） ----

        private static readonly DependencyPropertyKey AxisItemsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(AxisItems), typeof(IReadOnlyList<AxisPanelItem>), typeof(AxisPanelElement),
            new PropertyMetadata(Array.Empty<AxisPanelItem>()));

        /// <summary>下拉框的候选轴（按设计期选择顺序）</summary>
        public static readonly DependencyProperty AxisItemsProperty = AxisItemsPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey HasAxesPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HasAxes), typeof(bool), typeof(AxisPanelElement), new PropertyMetadata(false));

        /// <summary>是否配了至少一根候选轴</summary>
        public static readonly DependencyProperty HasAxesProperty = HasAxesPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey HasMultipleAxesPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HasMultipleAxes), typeof(bool), typeof(AxisPanelElement), new PropertyMetadata(false));

        /// <summary>候选是否多于一根（只有一根时下拉框没有意义，模板改显静态轴名）</summary>
        public static readonly DependencyProperty HasMultipleAxesProperty = HasMultipleAxesPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey SelectorVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(SelectorVisibility), typeof(Visibility), typeof(AxisPanelElement),
            new PropertyMetadata(Visibility.Collapsed));

        /// <summary>下拉框的显隐（多轴才显示）。模板直接绑它，不写 ControlTemplate DataTrigger——
        /// 本环境下模板里的 DataTrigger 不触发（与 AlarmBannerElement 同一条纪律）。</summary>
        public static readonly DependencyProperty SelectorVisibilityProperty =
            SelectorVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey SingleAxisVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(SingleAxisVisibility), typeof(Visibility), typeof(AxisPanelElement),
            new PropertyMetadata(Visibility.Collapsed));

        /// <summary>单轴时那行静态轴名的显隐</summary>
        public static readonly DependencyProperty SingleAxisVisibilityProperty =
            SingleAxisVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey EmptyVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(EmptyVisibility), typeof(Visibility), typeof(AxisPanelElement),
            new PropertyMetadata(Visibility.Visible));

        /// <summary>一根轴都没配时那句提示的显隐</summary>
        public static readonly DependencyProperty EmptyVisibilityProperty =
            EmptyVisibilityPropertyKey.DependencyProperty;

        /// <summary>运行期当前选中的轴（下拉框 TwoWay 绑定；设计期不落盘）</summary>
        public static readonly DependencyProperty SelectedAxisIdProperty = DependencyProperty.Register(
            nameof(SelectedAxisId), typeof(Guid), typeof(AxisPanelElement),
            new PropertyMetadata(Guid.Empty, OnSelectedAxisIdChanged));

        private static readonly DependencyProperty SelectedAxisItemProperty = DependencyProperty.Register(
            nameof(SelectedAxisItem), typeof(AxisPanelItem), typeof(AxisPanelElement),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedAxisItemChanged));

        /// <summary>
        /// 下拉框的选中项（与 <see cref="SelectedAxisId"/> 双向同步）。
        ///
        /// 为什么模板绑 `SelectedItem` 而不是 `SelectedValue`：`SelectedValue` 在
        /// "ItemsSource 后到"时会丢掉选择——模板套上那一刻 `SelectedAxisId` 已经有值，
        /// 但 `AxisItems` 还没绑定过去，ComboBox 找不到对应项就把选中置空，
        /// 屏幕上就是一个**空的选择框**（2026-10-19 离屏图抓到）。
        /// `SelectedItem` 绑定的是对象本身，Selector 会在 ItemsSource 到位后重新解析它。
        /// </summary>
        public AxisPanelItem? SelectedAxisItem
        {
            get => (AxisPanelItem?)GetValue(SelectedAxisItemProperty);
            set => SetValue(SelectedAxisItemProperty, value);
        }

        private static readonly DependencyPropertyKey SelectedAxisNamePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(SelectedAxisName), typeof(string), typeof(AxisPanelElement), new PropertyMetadata(string.Empty));

        /// <summary>当前轴名（给标题/无障碍读；轴改名后由数据源回填最新名字）</summary>
        public static readonly DependencyProperty SelectedAxisNameProperty = SelectedAxisNamePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey PositionTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(PositionText), typeof(string), typeof(AxisPanelElement), new PropertyMetadata("—"));

        /// <summary>位置文本（mm，三位小数；未知时是"—"，不编 0）</summary>
        public static readonly DependencyProperty PositionTextProperty = PositionTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey VelocityTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(VelocityText), typeof(string), typeof(AxisPanelElement), new PropertyMetadata("—"));

        /// <summary>速度文本（mm/s，一位小数；未知时是"—"）</summary>
        public static readonly DependencyProperty VelocityTextProperty = VelocityTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(StatusText), typeof(string), typeof(AxisPanelElement), new PropertyMetadata("设计预览"));

        /// <summary>状态一句话（驱动给的 Describe 或离线原因）</summary>
        public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey IsValidPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsValid), typeof(bool), typeof(AxisPanelElement), new PropertyMetadata(false));

        /// <summary>当前轴的快照是否有效（离线/未轮询到时为 false）</summary>
        public static readonly DependencyProperty IsValidProperty = IsValidPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey IsMovingPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsMoving), typeof(bool), typeof(AxisPanelElement), new PropertyMetadata(false));

        /// <summary>当前轴是否在运动</summary>
        public static readonly DependencyProperty IsMovingProperty = IsMovingPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey AxisEnabledPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(AxisEnabled), typeof(bool), typeof(AxisPanelElement), new PropertyMetadata(false));

        /// <summary>当前轴是否已使能</summary>
        public static readonly DependencyProperty AxisEnabledProperty = AxisEnabledPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey IsInPositionPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsInPosition), typeof(bool), typeof(AxisPanelElement), new PropertyMetadata(false));

        /// <summary>当前轴是否已到位</summary>
        public static readonly DependencyProperty IsInPositionProperty = IsInPositionPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey IsAlarmPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsAlarm), typeof(bool), typeof(AxisPanelElement), new PropertyMetadata(false));

        /// <summary>当前轴是否报警</summary>
        public static readonly DependencyProperty IsAlarmProperty = IsAlarmPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey MovingLampBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(MovingLampBrush), typeof(Brush), typeof(AxisPanelElement), new PropertyMetadata(FallbackOff));

        public static readonly DependencyProperty MovingLampBrushProperty = MovingLampBrushPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey EnableLampBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(EnableLampBrush), typeof(Brush), typeof(AxisPanelElement), new PropertyMetadata(FallbackOff));

        public static readonly DependencyProperty EnableLampBrushProperty = EnableLampBrushPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey InPositionLampBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(InPositionLampBrush), typeof(Brush), typeof(AxisPanelElement), new PropertyMetadata(FallbackOff));

        public static readonly DependencyProperty InPositionLampBrushProperty = InPositionLampBrushPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey AlarmLampBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(AlarmLampBrush), typeof(Brush), typeof(AxisPanelElement), new PropertyMetadata(FallbackOff));

        public static readonly DependencyProperty AlarmLampBrushProperty = AlarmLampBrushPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey PosLimitLampBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(PosLimitLampBrush), typeof(Brush), typeof(AxisPanelElement), new PropertyMetadata(FallbackOff));

        public static readonly DependencyProperty PosLimitLampBrushProperty = PosLimitLampBrushPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey NegLimitLampBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(NegLimitLampBrush), typeof(Brush), typeof(AxisPanelElement), new PropertyMetadata(FallbackOff));

        public static readonly DependencyProperty NegLimitLampBrushProperty = NegLimitLampBrushPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey OriginLampBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(OriginLampBrush), typeof(Brush), typeof(AxisPanelElement), new PropertyMetadata(FallbackOff));

        public static readonly DependencyProperty OriginLampBrushProperty = OriginLampBrushPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey EmergencyStopLampBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(EmergencyStopLampBrush), typeof(Brush), typeof(AxisPanelElement), new PropertyMetadata(FallbackOff));

        public static readonly DependencyProperty EmergencyStopLampBrushProperty =
            EmergencyStopLampBrushPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CanOperatePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CanOperate), typeof(bool), typeof(AxisPanelElement), new PropertyMetadata(false));

        /// <summary>
        /// 操作按钮是否可用（只读）：有命令出口、有选中轴、且当前角色够格。
        /// 模板直接绑它到按钮容器的 IsEnabled —— 灰掉比"按下去没反应"诚实。
        /// </summary>
        public static readonly DependencyProperty CanOperateProperty = CanOperatePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CommandMessagePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CommandMessage), typeof(string), typeof(AxisPanelElement), new PropertyMetadata(string.Empty));

        /// <summary>
        /// 最近一次操作按钮的结果文案（只读）。成功为空串，失败是可直接上屏的原因。
        /// 不做自动消失：下一次操作会覆盖它——多养一个计时器只为淡出一行字，不值得。
        /// </summary>
        public static readonly DependencyProperty CommandMessageProperty = CommandMessagePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CommandsVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CommandsVisibility), typeof(Visibility), typeof(AxisPanelElement),
            new PropertyMetadata(Visibility.Visible));

        /// <summary>操作按钮区的显隐（由 <see cref="ShowCommands"/> 算好，模板直绑）</summary>
        public static readonly DependencyProperty CommandsVisibilityProperty =
            CommandsVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey EnableButtonTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(EnableButtonText), typeof(string), typeof(AxisPanelElement), new PropertyMetadata("使能"));

        /// <summary>使能按钮的文案（已使能时显示「失能」）。模板 DataTrigger 不触发，所以由代码算好。</summary>
        public static readonly DependencyProperty EnableButtonTextProperty =
            EnableButtonTextPropertyKey.DependencyProperty;

        // ---- 点位区（与运动共用同一个 SelectedAxisId） ----

        private static readonly DependencyPropertyKey PointsVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(PointsVisibility), typeof(Visibility), typeof(AxisPanelElement),
            new PropertyMetadata(Visibility.Collapsed));

        /// <summary>点位区显隐（由 <see cref="ShowPoints"/> 算好，模板直绑）</summary>
        public static readonly DependencyProperty PointsVisibilityProperty =
            PointsVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey PointsHeightPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(PointsHeight), typeof(double), typeof(AxisPanelElement), new PropertyMetadata(132d));

        /// <summary>
        /// 点位区高度（由 <see cref="PointsRows"/> 算好；行高 22px）。
        ///
        /// <b>表头不计入</b>：模板里表头那行在 ScrollViewer 外面，把 18px 加进来的话
        /// 6 行视口会变成 150px，实际能塞下 8 行——`PointsRows` 这个"可见行数"就不准了。
        /// </summary>
        public static readonly DependencyProperty PointsHeightProperty =
            PointsHeightPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey RowsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(Rows), typeof(IReadOnlyList<AxisPointRow>), typeof(AxisPanelElement),
            new PropertyMetadata(Array.Empty<AxisPointRow>()));

        /// <summary>点位行（只读；示教/改速度/外部改动后按指纹重建）</summary>
        public static readonly DependencyProperty RowsProperty = RowsPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CanEditPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CanEdit), typeof(bool), typeof(AxisPanelElement), new PropertyMetadata(false));

        /// <summary>行内"可改"判据 = 有权限且显示了操作入口（关掉后速度框只读）</summary>
        public static readonly DependencyProperty CanEditProperty = CanEditPropertyKey.DependencyProperty;

        /// <summary>设计期候选轴（从属性袋里的规范 JSON 解析；运行期下拉项由它 + 数据源名字合成）</summary>
        private IReadOnlyList<ScadaAxisRef> _options = Array.Empty<ScadaAxisRef>();

        /// <summary>上一次重建下拉项时的轴表版本（节拍里比对，变了才重建）</summary>
        private int _lastSourceVersion = -1;

        static AxisPanelElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(AxisPanelElement),
                new FrameworkPropertyMetadata(typeof(AxisPanelElement)));
        }

        public AxisPanelElement()
        {
            // 面板被移出可视树（切页/关窗口）时，若还按着点动、或回零长按计时器还在跑，
            // 必须收手：点动没有终点，"松手即停"的前提是有人发停止。
            // 这里只是补一道兜底，执行器自己的看门狗仍会在 1s 后兜住。
            Unloaded += (_, _) =>
            {
                _homeHoldTimer?.Stop();
                EndJog();
            };

            TeachCommand = new DelegateCommand<AxisPointRow>(OnTeach, r => r != null && CanOperate);
            LocateCommand = new DelegateCommand<AxisPointRow>(OnLocate, r => r != null && CanOperate);
        }

        /// <summary>下拉框的候选轴（只读，见 <see cref="AxisItemsProperty"/>）</summary>
        public IReadOnlyList<AxisPanelItem> AxisItems => (IReadOnlyList<AxisPanelItem>)GetValue(AxisItemsProperty);

        /// <summary>是否配了候选轴（只读）</summary>
        public bool HasAxes => (bool)GetValue(HasAxesProperty);

        /// <summary>候选是否多于一根（只读）</summary>
        public bool HasMultipleAxes => (bool)GetValue(HasMultipleAxesProperty);

        /// <summary>下拉框显隐（只读）</summary>
        public Visibility SelectorVisibility => (Visibility)GetValue(SelectorVisibilityProperty);

        /// <summary>单轴静态轴名显隐（只读）</summary>
        public Visibility SingleAxisVisibility => (Visibility)GetValue(SingleAxisVisibilityProperty);

        /// <summary>空态提示显隐（只读）</summary>
        public Visibility EmptyVisibility => (Visibility)GetValue(EmptyVisibilityProperty);

        /// <summary>运行期当前选中的轴（下拉框 TwoWay 绑定）</summary>
        public Guid SelectedAxisId
        {
            get => (Guid)GetValue(SelectedAxisIdProperty);
            set => SetValue(SelectedAxisIdProperty, value);
        }

        /// <summary>当前轴名（只读）</summary>
        public string SelectedAxisName => (string)GetValue(SelectedAxisNameProperty);

        /// <summary>位置文本（只读）</summary>
        public string PositionText => (string)GetValue(PositionTextProperty);

        /// <summary>速度文本（只读）</summary>
        public string VelocityText => (string)GetValue(VelocityTextProperty);

        /// <summary>状态文本（只读）</summary>
        public string StatusText => (string)GetValue(StatusTextProperty);

        /// <summary>快照是否有效（只读）</summary>
        public bool IsValid => (bool)GetValue(IsValidProperty);

        public bool IsMoving => (bool)GetValue(IsMovingProperty);
        public bool AxisEnabled => (bool)GetValue(AxisEnabledProperty);
        public bool IsInPosition => (bool)GetValue(IsInPositionProperty);
        public bool IsAlarm => (bool)GetValue(IsAlarmProperty);

        public Brush MovingLampBrush => (Brush)GetValue(MovingLampBrushProperty);
        public Brush EnableLampBrush => (Brush)GetValue(EnableLampBrushProperty);
        public Brush InPositionLampBrush => (Brush)GetValue(InPositionLampBrushProperty);
        public Brush AlarmLampBrush => (Brush)GetValue(AlarmLampBrushProperty);
        public Brush PosLimitLampBrush => (Brush)GetValue(PosLimitLampBrushProperty);
        public Brush NegLimitLampBrush => (Brush)GetValue(NegLimitLampBrushProperty);
        public Brush OriginLampBrush => (Brush)GetValue(OriginLampBrushProperty);
        public Brush EmergencyStopLampBrush => (Brush)GetValue(EmergencyStopLampBrushProperty);

        /// <summary>操作按钮是否可用（只读）</summary>
        public bool CanOperate => (bool)GetValue(CanOperateProperty);

        private static readonly DependencyPropertyKey CanJogPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CanJog), typeof(bool), typeof(AxisPanelElement), new PropertyMetadata(false));

        /// <summary>
        /// 两个点动键是否可点（只读）= <see cref="CanOperate"/> 且当前轴**不在运动**。
        ///
        /// 为什么不直接连 CanOperate：运动中再发 JogStart / 单步定位会和正在执行的指令抢同一根轴，
        /// 但「停止 / 清报警 / 失能 / 回零」在运动中恰恰是**该点**的那几个键——
        /// 一起灰掉等于在最需要处置的时候把按钮收走了。所以只锁点动这两颗。
        /// </summary>
        public static readonly DependencyProperty CanJogProperty = CanJogPropertyKey.DependencyProperty;

        /// <summary>两个点动键是否可点（只读；运动中锁这两颗，其余按钮照常）</summary>
        public bool CanJog => (bool)GetValue(CanJogProperty);

        // ---- 步进点动行的算好属性 ----
        // 与 CommandsVisibility / PointsVisibility 同一套路：基类是纯 DP 控件、没有 CLR 属性通知，
        // 所以凡是要在界面上跟着变的，一律做成只读 DP（模板直绑），不做 DataTrigger（本环境不触发）。

        private static readonly DependencyPropertyKey IsStepJogPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsStepJog), typeof(bool), typeof(AxisPanelElement), new PropertyMetadata(false));

        /// <summary>步进点动行里「单步」是否选中（只读）</summary>
        public static readonly DependencyProperty IsStepJogProperty = IsStepJogPropertyKey.DependencyProperty;

        /// <summary>「单步」是否选中（只读；连续是默认档）</summary>
        public bool IsStepJog => (bool)GetValue(IsStepJogProperty);

        private static readonly DependencyPropertyKey IsContinuousJogPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsContinuousJog), typeof(bool), typeof(AxisPanelElement), new PropertyMetadata(true));

        /// <summary>「连续」是否选中（只读）</summary>
        public static readonly DependencyProperty IsContinuousJogProperty =
            IsContinuousJogPropertyKey.DependencyProperty;

        /// <summary>「连续」是否选中（只读）</summary>
        public bool IsContinuousJog => (bool)GetValue(IsContinuousJogProperty);

        private static readonly DependencyPropertyKey JogStepTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(JogStepText), typeof(string), typeof(AxisPanelElement), new PropertyMetadata("—"));

        /// <summary>步长文本（只读）：单步档两位小数，连续档给「—」——省得用户以为它生效了</summary>
        public static readonly DependencyProperty JogStepTextProperty = JogStepTextPropertyKey.DependencyProperty;

        /// <summary>步长文本（只读）</summary>
        public string JogStepText => (string)GetValue(JogStepTextProperty);

        private static readonly DependencyPropertyKey IsStepFieldEditablePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsStepFieldEditable), typeof(bool), typeof(AxisPanelElement), new PropertyMetadata(false));

        /// <summary>步长输入框是否可编辑（只读；连续档下只做显示）</summary>
        public static readonly DependencyProperty IsStepFieldEditableProperty =
            IsStepFieldEditablePropertyKey.DependencyProperty;

        /// <summary>步长输入框是否可编辑（只读）</summary>
        public bool IsStepFieldEditable => (bool)GetValue(IsStepFieldEditableProperty);

        private static readonly DependencyPropertyKey JogStepValidPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(JogStepValid), typeof(bool), typeof(AxisPanelElement), new PropertyMetadata(false));

        /// <summary>单步步长是否可用（只读）= 单步档 且 步长落在 0~50 且不为 0</summary>
        public static readonly DependencyProperty JogStepValidProperty =
            JogStepValidPropertyKey.DependencyProperty;

        /// <summary>单步步长是否可用（只读）</summary>
        public bool JogStepValid => (bool)GetValue(JogStepValidProperty);

        private static readonly DependencyPropertyKey MovingBannerVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(MovingBannerVisibility), typeof(Visibility), typeof(AxisPanelElement),
            new PropertyMetadata(Visibility.Collapsed));

        /// <summary>运动中提示条的显隐（只读；模板直绑，不做 DataTrigger）</summary>
        public static readonly DependencyProperty MovingBannerVisibilityProperty =
            MovingBannerVisibilityPropertyKey.DependencyProperty;

        /// <summary>运动中提示条的显隐（只读）</summary>
        public Visibility MovingBannerVisibility => (Visibility)GetValue(MovingBannerVisibilityProperty);

        /// <summary>单步点动的步长上限（mm）。用户口径：0~50。</summary>
        public const double JogStepMaxMm = 50d;

        /// <summary>步长的最小有效增量（mm）。用户口径：精度 0.1。</summary>
        public const double JogStepQuantumMm = 0.1d;

        /// <summary>把步长夹到"合法且对齐 0.1"的口径里（写入口与提交口共用）</summary>
        public static double NormalizeJogStep(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0d;
            double clamped = Math.Clamp(value, 0d, JogStepMaxMm);
            return Math.Round(clamped / JogStepQuantumMm, MidpointRounding.AwayFromZero) * JogStepQuantumMm;
        }

        /// <summary>最近一次操作的结果文案（只读；空串 = 上一次是成功的）</summary>
        public string CommandMessage => (string)GetValue(CommandMessageProperty);

        /// <summary>操作按钮区的显隐（只读）</summary>
        public Visibility CommandsVisibility => (Visibility)GetValue(CommandsVisibilityProperty);

        /// <summary>使能按钮文案（只读；已使能时是「失能」）</summary>
        public string EnableButtonText => (string)GetValue(EnableButtonTextProperty);

        /// <summary>点位区显隐（只读）</summary>
        public Visibility PointsVisibility => (Visibility)GetValue(PointsVisibilityProperty);

        /// <summary>点位区高度（只读）</summary>
        public double PointsHeight => (double)GetValue(PointsHeightProperty);

        /// <summary>点位行（只读）</summary>
        public IReadOnlyList<AxisPointRow> Rows => (IReadOnlyList<AxisPointRow>)GetValue(RowsProperty);

        /// <summary>行内可改（只读）= 有权限且显示了操作入口</summary>
        public bool CanEdit => (bool)GetValue(CanEditProperty);

        /// <summary>「示教」：把当前位置写进该行点位（与运动共用当前选中轴）</summary>
        public DelegateCommand<AxisPointRow> TeachCommand { get; }

        /// <summary>「定位」：走到该行点位（与运动共用当前选中轴）</summary>
        public DelegateCommand<AxisPointRow> LocateCommand { get; }

        /// <summary>
        /// 执行一条轴命令（面板按钮与断言共用这一个入口）。
        ///
        /// 权限、命令出口、选中轴三件事缺一不可；任何一条不满足都返回 false 并给原因，
        /// <b>绝不静默空跑</b>——操作员按了按钮什么都没发生，是这类界面最伤信任的失效。
        /// </summary>
        public bool TryExecuteCommand(ScadaAxisCommandKind command, double? value = null)
        {
            if (!CheckCanRun(out var error))
            {
                SetValue(CommandMessagePropertyKey, error ?? "命令不可用");
                return false;
            }

            bool ok;
            try
            {
                ok = RuntimeContext!.AxisCommands!.TryExecute(SelectedAxisId, command, value, out error);
            }
            catch (Exception ex)
            {
                // 契约要求实现方不抛；真抛了也绝不让它冒到鼠标事件链上（那会退进程）。
                ok = false;
                error = $"命令执行异常：{ex.Message}";
            }

            SetValue(CommandMessagePropertyKey, ok ? string.Empty : error ?? "命令被拒绝（没有给出原因）");
            return ok;
        }

        /// <summary>
        /// "权限 + 出口 + 选中轴"的统一闸门：操作按钮与点位写入都先过这里。
        /// 返回 false 时 <paramref name="error"/> 已是可上屏的原因。
        /// </summary>
        private bool CheckCanRun(out string? error)
        {
            error = null;

            if (RuntimeContext == null)
            {
                error = "设计期不执行命令（运行后可用）";
                return false;
            }

            if (RuntimeContext.AxisCommands == null)
            {
                error = "未接入运动模块，命令不可用";
                return false;
            }

            if (SelectedAxisId == Guid.Empty)
            {
                error = "请先选择一根轴";
                return false;
            }

            // 权限：与事件钩子同一口径（高角色含低角色，未登录=操作员）。
            // 这里先判一次是为了让操作员"按之前"就知道自己没权限，而不是命令发出去才被拦。
            if (!RuntimeContext.AccessPolicy.CanOperate(Element?.RequiredRole, out var denyReason))
            {
                error = denyReason ?? "没有权限操作该轴";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 按钮可用性重算：有出口 + 有选中轴 + 权限够。
        /// 每个"会让它变"的入口都调一次（装/摘上下文、换轴、角色变化）。
        /// </summary>
        private void RefreshCanOperate()
        {
            bool can = RuntimeContext?.AxisCommands != null
                       && SelectedAxisId != Guid.Empty
                       && RuntimeContext.AccessPolicy.CanOperate(Element?.RequiredRole, out _);

            SetValue(CanOperatePropertyKey, can);
            SetValue(CanEditPropertyKey, can && ShowCommands);

            // 点动键额外挡一道"轴没在动"：运动中按下去会和正在走的指令抢轴。
            // 停止/清报警/失能/回零**不**受这一条约束（那正是运动中该点的键）。
            SetValue(CanJogPropertyKey, can && !IsMoving);

            TeachCommand.RaiseCanExecuteChanged();
            LocateCommand.RaiseCanExecuteChanged();
        }

        // ==================================================================
        //  点位区（与运动共用同一个 SelectedAxisId —— 这是合并控件的核心）
        // ==================================================================

        /// <summary>上一次刷新时点位表的指纹（轴 Id + 每行 序号/名字/位置/速度）</summary>
        private string _pointsFingerprint = string.Empty;

        /// <summary>
        /// 重读点位并（必要时）重建行。
        ///
        /// 点位是**配置**、`List&lt;MotionPoint&gt;` 不是可观察集合：外部（运动卡设置）改了它
        /// 没有任何通知。所以节拍里每拍重读一次，靠指纹短路——没变时连行对象都不重建，
        /// 否则会把用户正在输入的速度框连行一起换掉。
        /// </summary>
        /// <param name="force">true = 跳过指纹比对（本控件自己写完配置后用）</param>
        private void RefreshRows(bool force = false)
        {
            // 点位区收起时不读：16 行 × N 轴的点位是配置，没必要为一个看不见的区域每拍现读。
            if (!ShowPoints)
            {
                _pointsFingerprint = string.Empty;
                SetValue(RowsPropertyKey, Array.Empty<AxisPointRow>());
                return;
            }

            var source = RuntimeContext?.AxisSource;
            if (source == null || SelectedAxisId == Guid.Empty
                || !source.TryGetPoints(SelectedAxisId, out var points))
            {
                _pointsFingerprint = string.Empty;
                SetValue(RowsPropertyKey, Array.Empty<AxisPointRow>());
                return;
            }

            string fingerprint = BuildPointsFingerprint(SelectedAxisId, points);
            if (!force && string.Equals(fingerprint, _pointsFingerprint, StringComparison.Ordinal))
                return;

            _pointsFingerprint = fingerprint;

            var rows = new List<AxisPointRow>(points.Count);
            foreach (var point in points)
                rows.Add(new AxisPointRow(point, OnSpeedCommitted));

            SetValue(RowsPropertyKey, (IReadOnlyList<AxisPointRow>)rows);
        }

        private static string BuildPointsFingerprint(Guid axisId, IReadOnlyList<ScadaAxisPointInfo> points)
        {
            var builder = new System.Text.StringBuilder(axisId.ToString("N"));

            foreach (var point in points)
            {
                builder.Append(';')
                       .Append(point.Index).Append('|')
                       .Append(point.Name).Append('|')
                       .Append(point.PositionMm.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                       .Append(point.SpeedMmPerS.ToString("R", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private void OnTeach(AxisPointRow row)
        {
            if (!CheckCanRun(out var error))
            {
                SetValue(CommandMessagePropertyKey, error ?? "命令不可用");
                return;
            }

            if (RuntimeContext!.AxisSource is not { } source
                || !source.TryGetSample(SelectedAxisId, out var sample) || !sample.IsValid)
            {
                SetValue(CommandMessagePropertyKey, "读不到当前位置，无法示教（轴可能离线）");
                return;
            }

            // 二次确认放在"读到当前位置"之后：确认框里要写清**这一下会写入什么值**。
            // 权限已在 CheckCanRun 里判过——没权限的人根本走不到弹窗（不会先弹再拒）。
            if (!ConfirmOperation(
                    "示教确认",
                    $"确认把轴「{SelectedAxisName}」的当前位置 {sample.PositionMm:F3} mm "
                    + $"写入 {row.Caption}？"))
                return;

            bool ok;
            try
            {
                ok = RuntimeContext.AxisCommands!.TryTeach(
                    SelectedAxisId, row.Index, sample.PositionMm, row.SpeedMmPerS, out error);
            }
            catch (Exception ex)
            {
                ok = false;
                error = $"命令执行异常：{ex.Message}";
            }

            SetValue(CommandMessagePropertyKey, ok ? string.Empty : error ?? "示教失败");
            if (ok) RefreshRows(force: true);
        }

        private void OnLocate(AxisPointRow row)
        {
            if (!CheckCanRun(out var error))
            {
                SetValue(CommandMessagePropertyKey, error ?? "命令不可用");
                return;
            }

            if (!ConfirmOperation(
                    "定位确认",
                    $"确认让轴「{SelectedAxisName}」走到 {row.Caption}"
                    + $"（{row.PositionText} mm @ {row.SpeedText} mm/s）？"))
                return;

            bool ok;
            try
            {
                ok = RuntimeContext!.AxisCommands!.TryMove(
                    SelectedAxisId, row.PositionMm, row.SpeedMmPerS, out error);
            }
            catch (Exception ex)
            {
                ok = false;
                error = $"命令执行异常：{ex.Message}";
            }

            SetValue(CommandMessagePropertyKey, ok ? string.Empty : error ?? "定位失败");
        }

        /// <summary>
        /// 示教/定位前的二次确认。
        ///
        /// <b>权限不是在这里判的</b>：调用方（OnTeach/OnLocate）已经先过了
        /// <see cref="CheckCanRun"/>（命令出口 + 选中轴 + <c>RequiredRole</c>）。
        /// 所以没权限的用户根本走不到这里，不会出现"先弹窗、再告诉他没权限"的羞辱式交互。
        ///
        /// <c>ConfirmCommands=false</c> 时直接放行（这个开关只管"多问一句"）。
        /// 开关为 true 但宿主没接确认出口时**拒绝执行**：静默跳过确认等于把防误触悄悄关掉。
        /// </summary>
        private bool ConfirmOperation(string title, string message)
        {
            if (!ConfirmCommands)
                return true;

            var confirmations = RuntimeContext?.Confirmations;
            if (confirmations == null)
            {
                SetValue(CommandMessagePropertyKey, "二次确认服务未接入，已取消操作");
                return false;
            }

            try
            {
                if (confirmations.Confirm(title, message))
                    return true;

                // 用户主动取消不是错误：不写红字，弹窗本身已经是最直接的反馈。
                SetValue(CommandMessagePropertyKey, string.Empty);
                return false;
            }
            catch (Exception ex)
            {
                SetValue(CommandMessagePropertyKey, $"二次确认失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>行内速度编辑提交（LostFocus 触发）</summary>
        private void OnSpeedCommitted(AxisPointRow row, string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed)
                || !double.IsFinite(speed) || speed < 0)
            {
                SetValue(CommandMessagePropertyKey, $"速度「{text}」不是有效数字（必须 ≥ 0）");
                RefreshRows(force: true);
                return;
            }

            if (!CheckCanRun(out var error))
            {
                SetValue(CommandMessagePropertyKey, error ?? "命令不可用");
                RefreshRows(force: true);
                return;
            }

            bool ok;
            try
            {
                ok = RuntimeContext!.AxisCommands!.TrySetPointSpeed(
                    SelectedAxisId, row.Index, speed, out error);
            }
            catch (Exception ex)
            {
                ok = false;
                error = $"命令执行异常：{ex.Message}";
            }

            SetValue(CommandMessagePropertyKey, ok ? string.Empty : error ?? "改速度失败");
            RefreshRows(force: true);
        }

        private void UpdatePointsLayout()
        {
            SetValue(PointsVisibilityPropertyKey, ShowPoints ? Visibility.Visible : Visibility.Collapsed);

            // 行高 22（与模板里的 MinHeight 一致）；夹在 1~16 行。
            // 表头在 ScrollViewer 外面，不计入 MaxHeight（见 PointsHeight 的注释）。
            int rows = Math.Clamp(PointsRows, 1, 16);
            SetValue(PointsHeightPropertyKey, rows * 22d);
        }

        partial void OnShowPointsPropertyChanged()
        {
            UpdatePointsLayout();
            RefreshRows();
        }

        partial void OnPointsRowsPropertyChanged() => UpdatePointsLayout();

        protected override void OnElementRefreshed()
        {
            ApplyStrokeInset(1);

            // 权限（RequiredRole）改了要立刻反映到按钮上：属性面板改完、或加载期恢复布局，
            // 都走 RefreshCore → 这里。不补的话按钮会停在旧权限上，直到下一次切轴。
            RefreshCanOperate();

            // 数据快照也要一起补：运动锁（CanJog）是由「权限 + 是否在动」两个条件算出来的，
            // 只刷权限会让 IsMoving 停在上一次的旧值——轴其实在动、点动键却还是亮的。
            // 这是会让机构抢轴的那一类错误，不是观感问题。
            RefreshFromSource();
            RefreshJogAvailability();
        }

        // ---- 操作按钮的模板部件名（模板里 x:Name 必须逐字一致）----

        public const string PartCommands = "Commands";
        public const string PartBtnEnable = "BtnEnable";
        public const string PartBtnStop = "BtnStop";
        public const string PartBtnHome = "BtnHome";
        public const string PartBtnJogForward = "BtnJogForward";
        public const string PartBtnJogReverse = "BtnJogReverse";
        public const string PartBtnClearAlarm = "BtnClearAlarm";

        /// <summary>点动模式胶囊：连续</summary>
        public const string PartBtnJogContinuous = "BtnJogContinuous";

        /// <summary>点动模式胶囊：单步</summary>
        public const string PartBtnJogStep = "BtnJogStep";

        /// <summary>单步步长输入框</summary>
        public const string PartJogStepBox = "JogStepBox";

        /// <summary>回零必须长按多久才触发（与参考模板一致：防误触）</summary>
        private static readonly TimeSpan HomeHoldDelay = TimeSpan.FromMilliseconds(500);

        /// <summary>回零长按计时器（模板套上时建、控件卸载时停）</summary>
        private DispatcherTimer? _homeHoldTimer;

        /// <summary>点动是否正按着（松开/移出/失焦都要发停止，不能只认 MouseUp）</summary>
        private bool _jogHeld;

        // 点动的四个处理委托必须存成字段：模板重新套用（换主题/换模板）时要能逐字摘掉，
        // 用匿名 lambda 现写一个 `-=` 是永远摘不掉的（每次都是新对象）——那正是订阅泄漏最隐蔽的写法。
        private Button? _jogForwardButton;
        private Button? _jogReverseButton;
        private MouseButtonEventHandler? _jogForwardDown;
        private MouseButtonEventHandler? _jogReverseDown;
        private MouseButtonEventHandler? _jogUp;
        private MouseEventHandler? _jogLeave;
        private MouseEventHandler? _jogLostCapture;

        // 点动模式胶囊与步长框：同样存字段，模板重套用时要能逐字摘掉。
        private ToggleButton? _jogContinuousButton;
        private ToggleButton? _jogStepButton;
        private TextBox? _jogStepBox;
        private KeyEventHandler? _jogStepKeyDown;
        private RoutedEventHandler? _jogStepLostFocus;

        /// <summary>
        /// 步长框里的文本是不是控件自己写进去的。
        ///
        /// TextBox.Text 的 TwoWay 回写与"用户敲字"在这一层分不出来，而 R35 明确要求用户意图只能由真实输入事件判定。
        /// 所以控件每次把算好的 JogStepText 填进去时置位，用户按键时清除；清除之后才算"这是人改的"。
        /// </summary>
        private bool _jogStepTextIsOurs = true;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            DetachCommandHandlers();

            if (GetTemplateChild(PartBtnEnable) is Button enable)
                enable.Click += OnEnableClicked;

            if (GetTemplateChild(PartBtnStop) is Button stop)
                stop.Click += OnStopClicked;

            if (GetTemplateChild(PartBtnClearAlarm) is Button clear)
                clear.Click += OnClearAlarmClicked;

            if (GetTemplateChild(PartBtnHome) is Button home)
            {
                home.PreviewMouseLeftButtonDown += OnHomePressed;
                home.PreviewMouseLeftButtonUp += OnHomeReleased;
                home.MouseLeave += OnHomeReleased;
            }

            if (GetTemplateChild(PartBtnJogForward) is Button jogForward)
                AttachJog(jogForward, +1);

            if (GetTemplateChild(PartBtnJogReverse) is Button jogReverse)
                AttachJog(jogReverse, -1);

            AttachJogMode();

            // 数据可能先于模板到达（RuntimeContext 在造控件之后才注入），这里补摆一次。
            RebuildAxisItems();
            RefreshFromSource();
            RefreshCanOperate();
            SetValue(CommandsVisibilityPropertyKey, ShowCommands ? Visibility.Visible : Visibility.Collapsed);
            UpdatePointsLayout();
            RefreshRows();
            RefreshJogAvailability();
        }

        /// <summary>
        /// 单步点动能不能走：步长必须落在 0~50 mm 内且不为 0。
        ///
        /// 判的是**原始值**，不是夹取后的值。属性面板/画布上手动填的 62.5 必须明确拒绝并说清范围，
        /// 不能先偷偷夹到 50 再走——那样用户以为走的是 62.5，实际只走了 50，
        /// 位置差出来的 12.5 mm 在精密测量站上是事故而不是不便。
        /// NormalizeJogStep 只用于"把合法输入量化到 0.1 粒度"，不负责任何越界兜底。
        /// </summary>
        private bool CanStepJog
            => !double.IsNaN(JogStepMm) && !double.IsInfinity(JogStepMm)
               && JogStepMm > 0 && JogStepMm <= JogStepMaxMm;

        private void AttachJogMode()
        {
            // 两个 ToggleButton 的 IsChecked 是 OneWay 绑到只读 DP 的（真正的档位由 JogMode 决定），
            // 所以这里只挂 Click：点一下就把 JogMode 切过去，选中态再从 RefreshJogAvailability 回流。
            // 不挂 Checked/Unchecked —— 那会在数据源回流时二次触发，变成来回翻。
            if (GetTemplateChild(PartBtnJogContinuous) is ToggleButton continuous)
            {
                _jogContinuousButton = continuous;
                continuous.Click += OnJogContinuousClicked;
            }

            if (GetTemplateChild(PartBtnJogStep) is ToggleButton step)
            {
                _jogStepButton = step;
                step.Click += OnJogStepClicked;
            }

            if (GetTemplateChild(PartJogStepBox) is TextBox box)
            {
                _jogStepBox = box;
                _jogStepKeyDown = OnJogStepKeyDown;
                _jogStepLostFocus = OnJogStepLostFocus;
                box.PreviewKeyDown += _jogStepKeyDown;
                box.LostFocus += _jogStepLostFocus;
            }
        }

        private void DetachJogMode()
        {
            if (_jogContinuousButton != null)
                _jogContinuousButton.Click -= OnJogContinuousClicked;

            if (_jogStepButton != null)
                _jogStepButton.Click -= OnJogStepClicked;

            if (_jogStepBox != null)
            {
                if (_jogStepKeyDown != null) _jogStepBox.PreviewKeyDown -= _jogStepKeyDown;
                if (_jogStepLostFocus != null) _jogStepBox.LostFocus -= _jogStepLostFocus;
            }

            _jogContinuousButton = null;
            _jogStepButton = null;
            _jogStepBox = null;
            _jogStepKeyDown = null;
            _jogStepLostFocus = null;
        }

        private void OnJogContinuousClicked(object sender, RoutedEventArgs e)
            => SetJogMode(ScadaJogMode.Continuous);

        private void OnJogStepClicked(object sender, RoutedEventArgs e)
            => SetJogMode(ScadaJogMode.Step);

        /// <summary>
        /// 切换点动模式（胶囊点击、脚本、断言都走这里）。
        /// 同档重复点击视为无效、不当成"取消选择"——点动只有连续/单步两种，没有第三态；
        /// 但 ToggleButton 被点一下会自己翻过去，所以同档时要把 IsChecked 按回真实值。
        /// </summary>
        public void SetJogMode(ScadaJogMode mode)
        {
            if (JogMode == mode)
            {
                RefreshJogAvailability();
                return;
            }

            JogMode = mode;
        }

        /// <summary>步长框里一按键就说明后面这段是人在改（R35：用户意图只认真实输入事件）</summary>
        private void OnJogStepKeyDown(object sender, KeyEventArgs e) => _jogStepTextIsOurs = false;

        private void OnJogStepLostFocus(object sender, RoutedEventArgs e)
            => CommitJogStep((sender as TextBox)?.Text);

        /// <summary>
        /// 步长框失焦提交：文本 → 0.1 mm 一档、0~50 mm 的合法步长。
        ///
        /// 宽进严出（R34）：能解析就收；解析不了或越界就说清被拒的原值，再把框里的文本刷回真实值，
        /// 不留下"框里写着 62.5、实际按 1.0 在走"这种看不出问题却会撞车的状态。
        /// </summary>
        private void CommitJogStep(string? text)
        {
            var raw = (text ?? string.Empty).Trim();

            // 从这一刻起文本重新归控件所有：无论提交成功与否，下面都会把框里的内容刷成真实值。
            _jogStepTextIsOurs = true;

            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value))
            {
                SetValue(CommandMessagePropertyKey,
                    $"步长「{raw}」不是有效数字（0 ~ {JogStepMaxMm:0.#} mm，精度 {JogStepQuantumMm:0.#}）");
                RefreshJogAvailability();
                return;
            }

            if (value < 0 || value > JogStepMaxMm)
            {
                SetValue(CommandMessagePropertyKey, $"步长「{raw}」超出 0 ~ {JogStepMaxMm:0.#} mm，已拒绝");
                RefreshJogAvailability();
                return;
            }

            var normalized = NormalizeJogStep(value);

            // 写回 DP 会走 OnJogStepMmPropertyChanged → RefreshJogAvailability，框里的文本随之刷新。
            if (Math.Abs(normalized - JogStepMm) > double.Epsilon)
                JogStepMm = normalized;
            else
                RefreshJogAvailability();
        }

        private void AttachJog(Button button, int direction)
        {
            // 共用同一组"松开"处理：方向只在按下那一刻需要。
            _jogUp ??= (_, _) => EndJog();
            _jogLeave ??= (_, _) => EndJog();
            _jogLostCapture ??= (_, _) => EndJog();

            MouseButtonEventHandler down = (_, e) =>
            {
                // 捕获鼠标：按住后移出按钮也要收到 MouseUp，否则"松手不停"。
                button.CaptureMouse();
                StartJog(direction);
                e.Handled = true;
            };

            if (direction > 0)
            {
                _jogForwardButton = button;
                _jogForwardDown = down;
            }
            else
            {
                _jogReverseButton = button;
                _jogReverseDown = down;
            }

            button.PreviewMouseLeftButtonDown += down;
            button.PreviewMouseLeftButtonUp += _jogUp;
            button.MouseLeave += _jogLeave;
            button.LostMouseCapture += _jogLostCapture;
        }

        private void DetachJog(Button? button, MouseButtonEventHandler? down)
        {
            if (button == null || down == null) return;

            button.PreviewMouseLeftButtonDown -= down;
            if (_jogUp != null) button.PreviewMouseLeftButtonUp -= _jogUp;
            if (_jogLeave != null) button.MouseLeave -= _jogLeave;
            if (_jogLostCapture != null) button.LostMouseCapture -= _jogLostCapture;
        }

        /// <summary>
        /// 开始点动（按钮按下时调用；也开放给脚本/断言）。
        /// 方向：正数正向、负数反向。
        ///
        /// 两种模式的区别就落在这里：
        /// <list type="bullet">
        /// <item>连续：<c>JogStart</c> + 带符号速度 <see cref="JogVelocityMmPerS"/>，松开由 <see cref="EndJog"/> 发停；</item>
        /// <item>单步：<c>TryMove(当前位置 ± 步长)</c>，与动作表「轴绝对定位」同一条路径——门禁/软限位/到位判定只有一份。</item>
        /// </list>
        /// 单步**不发停下**：它本身就是一条完整定位指令，再补一发 JogStop 只会在到位过程中打断它。
        /// </summary>
        public bool StartJog(int direction)
        {
            if (direction == 0) return false;

            // 运动锁：轴正在走的时候不接受点动（连续/单步都拒）。这条与 CanJog 同源，
            // 但真正按住时按钮已经灰了，这里再判一次是给脚本/断言与"按下那一刻刚好开始动"的竞态兜底。
            if (IsMoving)
            {
                SetValue(CommandMessagePropertyKey, "轴正在运动，点动已锁定（到位或停止后自动放开）");
                return false;
            }

            if (IsStepJog)
                return StepJog(direction);

            _jogHeld = true;
            bool ok = TryExecuteCommand(
                ScadaAxisCommandKind.JogStart, Math.Sign(direction) * JogVelocityMmPerS);

            // 没发出去就别留下"按着"的假状态，否则松开时会多发一条停止。
            if (!ok) _jogHeld = false;
            return ok;
        }

        /// <summary>
        /// 单步点动：目标 = 当前显示位置 ± 步长，一次 <see cref="IScadaAxisCommandSink.TryMove"/>。
        ///
        /// 位置取的是**面板上已经显示的那个三位小数**（<see cref="PositionText"/>）而不是原始快照：
        /// 用户看到 128.480、按 +1.0，期望的落点就是 129.480；拿内部更多位去算会得到他没见过的目标。
        /// </summary>
        private bool StepJog(int direction)
        {
            if (!CanStepJog)

            {
                SetValue(CommandMessagePropertyKey, JogStepMm <= 0
                    ? "单步点动的步长是 0，请先设一个 0.1~50 mm 的步长"
                    : $"单步步长「{JogStepMm:F2}」超出 0~{JogStepMaxMm:F0} mm，请改到范围内");
                return false;
            }

            if (!CheckCanRun(out var error))
            {
                SetValue(CommandMessagePropertyKey, error ?? "命令不可用");
                return false;
            }

            if (!TryParsePositionMm(out double current))
            {
                SetValue(CommandMessagePropertyKey, "当前没有有效位置，无法单步点动（请先使能/回零）");
                return false;
            }

            double step = NormalizeJogStep(JogStepMm);
            double target = current + (Math.Sign(direction) * step);

            bool ok;
            try
            {
                ok = RuntimeContext!.AxisCommands!.TryMove(SelectedAxisId, target, JogVelocityMmPerS, out error);
            }
            catch (Exception ex)
            {
                ok = false;
                error = $"命令执行异常：{ex.Message}";
            }

            SetValue(CommandMessagePropertyKey, ok
                ? $"点动 {step:F2} mm 已下发 · 目标 {target:F3}"
                : error ?? "命令被拒绝（没有给出原因）");

            return ok;
        }

        /// <summary>把面板上的位置文本（三位小数）还原成数值；"—"等占位一律算"没有位置"</summary>
        internal bool TryParsePositionMm(out double positionMm)
            => double.TryParse(PositionText, NumberStyles.Float, CultureInfo.InvariantCulture, out positionMm)
               && !double.IsNaN(positionMm)
               && !double.IsInfinity(positionMm);


        /// <summary>结束点动（按钮松开/移出/失焦时调用；也开放给脚本/断言）。</summary>
        public void EndJog()
        {
            if (!_jogHeld) return;
            _jogHeld = false;
            TryExecuteCommand(ScadaAxisCommandKind.JogStop);
        }

        /// <summary>
        /// 切轴/摘上下文时停掉**旧轴**的点动。
        ///
        /// 为什么不能用 <see cref="EndJog"/>：它按"当前选中轴"发停止，而切轴回调里
        /// <see cref="SelectedAxisId"/> 已经是新值了——那样会把停止发给新轴，旧轴继续走。
        /// 这里显式拿旧 Id 直呼出口，且**不过权限/门禁**：停是安全动作，
        /// 门禁里那条"未连接/无权限"恰恰是最该把停止发下去的时候。
        /// </summary>
        private void EndJogForAxis(Guid axisId)
        {
            if (!_jogHeld) return;
            _jogHeld = false;

            if (axisId == Guid.Empty || RuntimeContext?.AxisCommands is not { } sink)
                return;

            try
            {
                sink.TryExecute(axisId, ScadaAxisCommandKind.JogStop, null, out _);
            }
            catch
            {
                // 停止失败不能让切轴这个动作崩掉（执行器的看门狗仍会兜住）。
            }
        }

        private void OnEnableClicked(object? sender, RoutedEventArgs e)
            => TryExecuteCommand(AxisEnabled ? ScadaAxisCommandKind.Disable : ScadaAxisCommandKind.Enable);

        private void OnStopClicked(object? sender, RoutedEventArgs e)
            => TryExecuteCommand(ScadaAxisCommandKind.Stop);

        private void OnClearAlarmClicked(object? sender, RoutedEventArgs e)
            => TryExecuteCommand(ScadaAxisCommandKind.ClearAlarm);

        private void OnHomePressed(object? sender, MouseButtonEventArgs e)
        {
            if (!CanOperate) return;

            _homeHoldTimer ??= new DispatcherTimer { Interval = HomeHoldDelay };
            _homeHoldTimer.Tick -= OnHomeHoldElapsed;
            _homeHoldTimer.Tick += OnHomeHoldElapsed;
            _homeHoldTimer.Start();
        }

        private void OnHomeHoldElapsed(object? sender, EventArgs e)
        {
            _homeHoldTimer?.Stop();
            TryExecuteCommand(ScadaAxisCommandKind.Home);
        }

        private void OnHomeReleased(object? sender, EventArgs e) => _homeHoldTimer?.Stop();

        private void DetachCommandHandlers()
        {
            _homeHoldTimer?.Stop();

            if (GetTemplateChild(PartBtnEnable) is Button enable)
                enable.Click -= OnEnableClicked;

            if (GetTemplateChild(PartBtnStop) is Button stop)
                stop.Click -= OnStopClicked;

            if (GetTemplateChild(PartBtnClearAlarm) is Button clear)
                clear.Click -= OnClearAlarmClicked;

            if (GetTemplateChild(PartBtnHome) is Button home)
            {
                home.PreviewMouseLeftButtonDown -= OnHomePressed;
                home.PreviewMouseLeftButtonUp -= OnHomeReleased;
                home.MouseLeave -= OnHomeReleased;
            }

            // 点动的处理是匿名委托，只能靠存下来的字段逐字摘（见字段区注释）。
            DetachJog(_jogForwardButton, _jogForwardDown);
            DetachJog(_jogReverseButton, _jogReverseDown);
            DetachJogMode();
            _jogForwardButton = null;
            _jogReverseButton = null;
            _jogForwardDown = null;
            _jogReverseDown = null;
            _jogHeld = false;
        }

        /// <summary>
        /// 属性袋 → 候选轴清单。
        ///
        /// 这是本图元唯一的"自定义属性"入口（描述符里 <c>AxisOptions</c> 没有 TargetProperty，
        /// 由基类在 RefreshCore 里分派到这里）。解析放在 <see cref="ScadaAxisOptionsCodec"/>，
        /// 与属性面板、将来的离线校验共用同一份规则。
        /// </summary>
        protected override void ApplyCustomProperty(string key, string value)
        {
            if (string.Equals(key, ScadaAxisOptionsCodec.Key, StringComparison.Ordinal))
            {
                _options = ScadaAxisOptionsCodec.Parse(value);
                RebuildAxisItems();
                return;
            }

            base.ApplyCustomProperty(key, value);
        }

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            if (oldContext != null)
                oldContext.Beat.Beat -= OnBeat;

            if (newContext != null)
                newContext.Beat.Beat += OnBeat;

            _lastSourceVersion = -1;
            RebuildAxisItems();
            RefreshFromSource();
            RefreshCanOperate();
            RefreshRows();
        }

        /// <summary>
        /// 一拍：轴表拓扑变了就重建下拉项（改名/增删/启停），然后读当前轴的最新快照与点位。
        /// 只在 UI 线程上跑（节拍源由宿主在运行窗口的 Dispatcher 上养）。
        /// </summary>
        private void OnBeat(TimeSpan elapsed)
        {
            if (RuntimeContext?.AxisSource is not { } source)
                return;

            if (source.Version != _lastSourceVersion)
            {
                _lastSourceVersion = source.Version;
                RebuildAxisItems();
            }

            RefreshFromSource();

            // 点位是配置、没有变更通知：每拍重读一次，靠指纹短路（没变不重建行）。
            RefreshRows();

            // 登录/登出会改当前角色：每拍重算一次按钮可用性（一次策略调用，代价可忽略）。
            // 少了这一步，用户登录成工程师之后按钮仍是灰的，直到他切一次轴。
            RefreshCanOperate();
        }

        private static void OnSelectedAxisItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var panel = (AxisPanelElement)d;
            if (e.NewValue is not AxisPanelItem item || item.AxisId == Guid.Empty)
                return;

            if (panel.SelectedAxisId != item.AxisId)
                panel.SelectedAxisId = item.AxisId;
        }

        private static void OnSelectedAxisIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var panel = (AxisPanelElement)d;

            // ★ 切轴 = 先对**旧轴**收手，再换目标。
            //   合并控件里"显示轴"与"命令靶"是同一个变量：若用户按着点动 X 时切到 Y，
            //   不在这里停 X 的话，X 会一直走到执行器看门狗（1s）才停——期间界面显示的是 Y。
            //   旧 Id 从 e.OldValue 拿：此刻 SelectedAxisId 已经是新值。
            panel.EndJogForAxis(e.OldValue is Guid oldAxisId ? oldAxisId : Guid.Empty);
            panel._homeHoldTimer?.Stop();
            panel.SetValue(CommandMessagePropertyKey, string.Empty);

            panel.SelectedAxisItem = panel.AxisItems.FirstOrDefault(i => i.AxisId == panel.SelectedAxisId);
            panel.UpdateSelectedAxisName();
            panel.RefreshFromSource();
            panel.RefreshCanOperate();
            panel.RefreshRows();
        }

        /// <summary>
        /// 重建下拉项：设计期用落盘的名字；运行期按 Id 问数据源拿最新名字与可用性。
        ///
        /// 轴被删掉时<b>不自动跳到别的轴</b>：保留用户选中的 Id，项上显示"（已移除）"，
        /// 量值显示"—"。静默换轴比显示不可用危险得多——操作员会对着错误的对象按按钮
        /// （本轮只读，但下一批加上命令后这条尤其重要）。
        /// </summary>
        private void RebuildAxisItems()
        {
            var items = new List<AxisPanelItem>(_options.Count);
            var source = RuntimeContext?.AxisSource;

            foreach (var option in _options)
            {
                string name = option.Name;
                string card = string.Empty;
                bool available = true;

                if (source != null)
                {
                    if (source.TryGetInfo(option.AxisId, out var info))
                    {
                        if (!string.IsNullOrWhiteSpace(info.Name))
                            name = info.Name;

                        card = info.CardCaption;
                        available = info.Enabled && info.IsAvailable;

                        if (!info.Enabled)
                            name += "（已停用）";
                        else if (!info.IsAvailable)
                            name += "（不可用）";
                    }
                    else
                    {
                        available = false;
                        name += "（已移除）";
                    }
                }

                items.Add(new AxisPanelItem(option.AxisId, name, card, available));
            }

            SetValue(AxisItemsPropertyKey, (IReadOnlyList<AxisPanelItem>)items);

            // 下拉项换了新对象（轴改名/启停/增删）：把选中项也换成新对象，
            // 否则 ComboBox 的 SelectedItem 还指着旧实例，选中框会重新变空。
            SelectedAxisItem = items.FirstOrDefault(i => i.AxisId == SelectedAxisId);
            SetValue(HasAxesPropertyKey, items.Count > 0);
            SetValue(HasMultipleAxesPropertyKey, items.Count > 1);
            SetValue(SelectorVisibilityPropertyKey, items.Count > 1 ? Visibility.Visible : Visibility.Collapsed);
            SetValue(SingleAxisVisibilityPropertyKey, items.Count == 1 ? Visibility.Visible : Visibility.Collapsed);
            SetValue(EmptyVisibilityPropertyKey, items.Count == 0 ? Visibility.Visible : Visibility.Collapsed);

            if (items.Count == 0)
            {
                SelectedAxisId = Guid.Empty;
                return;
            }

            if (!items.Any(i => i.AxisId == SelectedAxisId))
                SelectedAxisId = (items.FirstOrDefault(i => i.IsAvailable) ?? items[0]).AxisId;

            // 选中的 Id 没变时 DP 回调不会触发，这里必须自己补一次。
            UpdateSelectedAxisName();
            RefreshFromSource();
            RefreshCanOperate();
        }

        private void UpdateSelectedAxisName()
        {
            var item = AxisItems.FirstOrDefault(i => i.AxisId == SelectedAxisId);
            SetValue(SelectedAxisNamePropertyKey, item?.DisplayName ?? string.Empty);
        }

        /// <summary>
        /// 读当前轴的快照并落到各只读属性。拿不到/离线时显示"—"，不把未知伪装成 0。
        /// </summary>
        private void RefreshFromSource()
        {
            var source = RuntimeContext?.AxisSource;

            if (source == null)
            {
                // 设计期（没有运行上下文）与"运行了但宿主没接运动模块"是两件事：
                // 前者是在编辑器里看版面，后者是现场真没有数据——不能都说成"设计预览"。
                ApplyUnknown(RuntimeContext == null
                    ? "设计预览（运行后显示实时值）"
                    : "未接入运动数据");
                return;
            }

            if (SelectedAxisId == Guid.Empty)
            {
                ApplyUnknown(HasAxes ? "未选择轴" : "未配置轴");
                return;
            }

            if (!source.TryGetSample(SelectedAxisId, out var sample))
            {
                ApplyUnknown("该轴已不在方案里");
                return;
            }

            SetValue(IsValidPropertyKey, sample.IsValid);
            SetValue(IsMovingPropertyKey, sample.Moving);
            SetValue(AxisEnabledPropertyKey, sample.Enabled);
            SetValue(EnableButtonTextPropertyKey, sample.Enabled ? "失能" : "使能");
            SetValue(IsInPositionPropertyKey, sample.InPosition);
            SetValue(IsAlarmPropertyKey, sample.Alarm);

            // 运动锁跟 IsMoving 一起走：轴开始/结束运动的这一拍就把点动键放开或锁上。
            RefreshJogAvailability();

            SetValue(PositionTextPropertyKey, sample.IsValid
                ? sample.PositionMm.ToString("F3", CultureInfo.InvariantCulture)
                : "—");

            SetValue(VelocityTextPropertyKey, sample.IsValid
                ? sample.VelocityMmPerS.ToString("F1", CultureInfo.InvariantCulture)
                : "—");

            SetValue(StatusTextPropertyKey, sample.IsValid
                ? sample.StatusText
                : sample.Error ?? "未知");

            ApplyLamps(sample);
        }

        private void ApplyUnknown(string statusText)
        {
            SetValue(IsValidPropertyKey, false);
            SetValue(IsMovingPropertyKey, false);
            SetValue(AxisEnabledPropertyKey, false);
            SetValue(EnableButtonTextPropertyKey, "使能");
            SetValue(IsInPositionPropertyKey, false);
            SetValue(IsAlarmPropertyKey, false);
            SetValue(PositionTextPropertyKey, "—");
            SetValue(VelocityTextPropertyKey, "—");
            SetValue(StatusTextPropertyKey, statusText);
            ApplyLamps(ScadaAxisSample.Invalid(statusText));
        }

        private void ApplyLamps(ScadaAxisSample sample)
        {
            Brush on = AccentColor ?? FallbackAccent;
            Brush alarm = AlarmColor ?? FallbackAlarm;
            Brush off = OffColor ?? FallbackOff;

            SetValue(MovingLampBrushPropertyKey, sample.IsValid && sample.Moving ? on : off);
            SetValue(EnableLampBrushPropertyKey, sample.IsValid && sample.Enabled ? on : off);
            SetValue(InPositionLampBrushPropertyKey, sample.IsValid && sample.InPosition ? on : off);
            SetValue(AlarmLampBrushPropertyKey, sample.IsValid && sample.Alarm ? alarm : off);
            SetValue(PosLimitLampBrushPropertyKey, sample.IsValid && sample.PositiveLimit ? alarm : off);
            SetValue(NegLimitLampBrushPropertyKey, sample.IsValid && sample.NegativeLimit ? alarm : off);
            SetValue(OriginLampBrushPropertyKey, sample.IsValid && sample.Origin ? on : off);
            SetValue(EmergencyStopLampBrushPropertyKey, sample.IsValid && sample.EmergencyStop ? alarm : off);
        }

        partial void OnAccentColorPropertyChanged() => RefreshFromSource();
        partial void OnAlarmColorPropertyChanged() => RefreshFromSource();
        partial void OnOffColorPropertyChanged() => RefreshFromSource();

        /// <summary>
        /// 点动模式换了：撤掉"按着"的状态（从单步切回连续、或反过来时，按住中的那一颗已经不对应任何东西），
        /// 再把步进行的算好属性刷一遍。
        /// </summary>
        partial void OnJogModePropertyChanged()
        {
            _jogHeld = false;
            RefreshJogAvailability();
        }

        partial void OnJogStepMmPropertyChanged() => RefreshJogAvailability();

        /// <summary>
        /// 点动相关的只读状态一起重算：运动锁 + 步进行文案/可编辑/可用 + 运动中提示条。
        ///
        /// 之所以集中一处，是因为这些值有四个驱动源（运动快照节拍、角色变化、换轴、改属性），
        /// 散着写漏一个，界面就会出现"按钮灰着却能点"或者"运动中还能点动"——
        /// 后者是真的会让机构抢轴，不是观感问题。
        /// </summary>
        private void RefreshJogAvailability()
        {
            bool can = CanOperate && !IsMoving;
            bool step = JogMode == ScadaJogMode.Step;
            double stepMm = NormalizeJogStep(JogStepMm);

            SetValue(CanJogPropertyKey, can);
            SetValue(IsStepJogPropertyKey, step);
            SetValue(IsContinuousJogPropertyKey, !step);
            SetValue(IsStepFieldEditablePropertyKey, step && CanOperate);
            SetValue(JogStepValidPropertyKey, step && stepMm > 0);
            SetValue(JogStepTextPropertyKey, step
                ? stepMm.ToString("F2", CultureInfo.InvariantCulture)
                : "—");
            SetValue(MovingBannerVisibilityPropertyKey, IsMoving ? Visibility.Visible : Visibility.Collapsed);

            SyncJogStepBox();
        }

        /// <summary>
        /// 把算好的步长文本灌回输入框。
        ///
        /// 只在"这段文本是控件自己写的、或者用户还没开始改"时才灌：用户正在框里敲 12.3 的时候，
        /// 任何一次节拍（200ms）刷进来的 F2 文本都会把他打到一半的字吃掉。
        /// 用户敲完之后由 <see cref="CommitJogStep"/> 统一按 0.1 mm 归一，再把 _jogStepTextIsOurs 置回 true。
        /// </summary>
        private void SyncJogStepBox()
        {
            if (_jogStepBox == null) return;

            // 用户已经改过、但还没失焦提交：这段输入归用户，不覆盖。
            if (!_jogStepTextIsOurs) return;

            string target = JogStepText;
            if (string.Equals(_jogStepBox.Text, target, StringComparison.Ordinal)) return;

            _jogStepBox.Text = target;
        }

        partial void OnShowCommandsPropertyChanged()
        {
            SetValue(CommandsVisibilityPropertyKey, ShowCommands ? Visibility.Visible : Visibility.Collapsed);

            // 关掉操作入口后点位速度框要变只读；重新打开后恢复（权限本身没变）。
            SetValue(CanEditPropertyKey, CanOperate && ShowCommands);
        }
    }

    /// <summary>
    /// 下拉框里的一项。名字在重建时已经拼好（含"已停用/不可用/已移除"后缀），
    /// 模板只负责显示，不再做判断。
    /// </summary>
    public sealed class AxisPanelItem
    {
        public AxisPanelItem(Guid axisId, string displayName, string cardCaption, bool isAvailable)
        {
            AxisId = axisId;
            DisplayName = displayName ?? string.Empty;
            CardCaption = cardCaption ?? string.Empty;
            IsAvailable = isAvailable;
        }

        public Guid AxisId { get; }

        public string DisplayName { get; }

        /// <summary>所属卡名（下拉项的悬停提示用；轴名本身已全局唯一）</summary>
        public string CardCaption { get; }

        public bool IsAvailable { get; }

        public override string ToString() => DisplayName;
    }
}
