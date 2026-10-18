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
                       Group = "状态", DefaultValue = "#FFFF6161",
                       Description = "激活态的颜色：使能/到位/运动中/原点灯与位置大字；默认取参考模板的珊瑚红")]
        public partial Brush? AccentColor { get; set; }

        /// <summary>告警态颜色（报警/正负限位/急停）</summary>
        [ScadaProperty(Key = "AlarmColor", DisplayName = "告警色", Kind = ElementPropertyKind.Color,
                       Group = "状态", DefaultValue = "#FFE03A2B",
                       Description = "报警/限位/急停灯的颜色；与多态灯、报警条的「严重红」同一个色")]
        public partial Brush? AlarmColor { get; set; }

        /// <summary>熄灭态颜色（灯不亮时的底色）</summary>
        [ScadaProperty(Key = "OffColor", DisplayName = "熄灭色", Kind = ElementPropertyKind.Color,
                       Group = "状态", DefaultValue = "#FF2A2D31",
                       Description = "灯不亮时的底色；比卡片底稍亮一档，才看得出「这里有一只灯」")]
        public partial Brush? OffColor { get; set; }

        /// <summary>次要文字色（单位、说明、轴卡片副标题）</summary>
        [ScadaProperty(Key = "MutedColor", DisplayName = "次要文字色", Kind = ElementPropertyKind.Color,
                       Group = "文字", DefaultValue = "#FF9A9A9D",
                       Description = "单位、状态说明与轴卡片副标题的颜色")]
        public partial Brush? MutedColor { get; set; }

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

        private static readonly DependencyPropertyKey IsEnabledPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsEnabled), typeof(bool), typeof(AxisPanelElement), new PropertyMetadata(false));

        /// <summary>当前轴是否已使能</summary>
        public static readonly DependencyProperty IsEnabledProperty = IsEnabledPropertyKey.DependencyProperty;

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
        public bool IsEnabled => (bool)GetValue(IsEnabledProperty);
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

        protected override void OnElementRefreshed() => ApplyStrokeInset(1);

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            // 数据可能先于模板到达（RuntimeContext 在造控件之后才注入），这里补摆一次。
            RebuildAxisItems();
            RefreshFromSource();
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
        }

        /// <summary>
        /// 一拍：轴表拓扑变了就重建下拉项（改名/增删/启停），然后读当前轴的最新快照。
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
        }

        private static void OnSelectedAxisIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var panel = (AxisPanelElement)d;
            panel.UpdateSelectedAxisName();
            panel.RefreshFromSource();
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
            SetValue(IsEnabledPropertyKey, sample.Enabled);
            SetValue(IsInPositionPropertyKey, sample.InPosition);
            SetValue(IsAlarmPropertyKey, sample.Alarm);

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
            SetValue(IsEnabledPropertyKey, false);
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
