using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 工艺符号（TypeKey = <c>Hmi.Symbol</c>）：一个图元画 30 种设备/阀门/管道/仪表符号。
    ///
    /// 为什么一个 TypeKey 而不是 30 个
    /// ---------
    /// 见 <see cref="ProcessSymbolCatalog"/>：符号之间的差别只是"几笔画在哪"，本质是数据；
    /// 30 个 TypeKey 会把工具箱撑成一面墙、复制 30 份近义属性、还要 30 个 Web 渲染器。
    ///
    /// 运行态能力（符号库的价值就在这里）
    /// ---------
    /// · <see cref="State"/> 可绑变量：0=停止 / 1=运行 / 2=故障 → 装饰色随之变；
    /// · <see cref="BlinkOnFault"/>：故障时装饰闪烁，相位读统一节拍源（与报警条对齐）；
    /// · <see cref="Text"/>（位号）可绑变量；<see cref="ShowValue"/> 可在符号内叠一个读数；
    /// · <see cref="Interactive"/> 打开后才发按下/释放事件——工艺符号默认是"图"，不是操作件。
    /// </summary>
    public partial class ProcessSymbolElement : ScadaElementBase
    {
        /// <summary>
        /// 默认样式键钉到本类型（R73 同族，2026-10-25 修）：不覆盖就继承基类样式键，
        /// 主题里按本类型键的那份模板永远匹配不上，画布上是一个空壳（拖上去看不见）。
        /// 断言里显式赋 Style 绕过了这条查找，所以绿过——见 PipeRunElement 同名注释。
        /// </summary>
        static ProcessSymbolElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ProcessSymbolElement),
                new FrameworkPropertyMetadata(typeof(ProcessSymbolElement)));
        }

        /// <summary>几何的设计空间边长（模板用 Viewbox 等比缩放到实际尺寸）</summary>
        public const double DesignSize = 100d;

        /// <summary>状态：不接变量时用 <c>None</c>（只有主色）</summary>
        public const string StateNone = "None";
        public const string StateRunning = "Running";
        public const string StateStopped = "Stopped";
        public const string StateFault = "Fault";

        // ---- 配置属性 ----

        /// <summary>符号种类</summary>
        [ScadaProperty(Key = "SymbolKind", DisplayName = "符号", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "Tower",
                       Choices = new[]
                       {
                           "Tower", "Reactor", "ShellTube", "PlateHx", "Compressor", "Fan", "Filter", "Separator",
                           "TankHorizontal", "Sphere", "ControlValve", "GateValve",
                           "Burner", "Stack", "Conveyor", "Agitator", "Cyclone", "CoolingTower",
                           "BallValve", "ButterflyValve", "SafetyValve", "CheckValve",
                           "Tee", "Cross", "Flange", "Blind", "Reducer", "FlowArrow", "Gauge", "PidLoop",
                       },
                       Description = "30 种工艺符号：设备 / 阀门 / 管道连接件 / 仪表")]
        public partial string? Symbol { get; set; }

        /// <summary>运行状态（可绑变量）</summary>
        [ScadaProperty(Key = "State", DisplayName = "状态", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.State, DefaultValue = "None",
                       Choices = new[] { "None", "Running", "Stopped", "Fault" }, IsBindable = true,
                       Description = "None = 只有主色；Running/Stopped/Fault 用对应色画装饰。绑变量后随值变")]
        public partial string? State { get; set; }

        /// <summary>运行色（装饰）</summary>
        [ScadaProperty(Key = "AccentColor", DisplayName = "运行色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.State, DefaultValue = "#FF34C759",
                       Description = "状态=Running 时装饰件的颜色；也是 None 时的默认装饰色")]
        public partial Brush? AccentColor { get; set; }

        /// <summary>停止色（装饰）</summary>
        [ScadaProperty(Key = "StoppedColor", DisplayName = "停止色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.State, DefaultValue = "#FF7A7A7A",
                       Description = "状态=Stopped 时的装饰色")]
        public partial Brush? StoppedColor { get; set; }

        /// <summary>故障色（装饰）</summary>
        [ScadaProperty(Key = "FaultColor", DisplayName = "故障色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.State, DefaultValue = "#FFE03A2B",
                       Description = "状态=Fault 时的装饰色（也是闪烁的那一档）")]
        public partial Brush? FaultColor { get; set; }

        /// <summary>故障闪烁</summary>
        [ScadaProperty(Key = "BlinkOnFault", DisplayName = "故障闪烁", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.State, DefaultValue = "False",
                       Description = "状态=Fault 时装饰件闪烁；相位与报警条同源，整屏对齐")]
        public partial bool BlinkOnFault { get; set; }

        /// <summary>重复细部数量</summary>
        [ScadaProperty(Key = "DetailCount", DisplayName = "细部数量", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", Min = 0, Max = 20,
                       Description = "塔板 / 叶片 / 滤芯 / 托辊的条数；0 = 用该符号的建议值")]
        public partial int DetailCount { get; set; }

        /// <summary>位号位置</summary>
        [ScadaProperty(Key = "TagPlacement", DisplayName = "位号位置", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "Below",
                       Choices = new[] { "Above", "Below", "Inside", "None" },
                       Description = "位号文字摆在哪；None = 不显示")]
        public partial string? TagPlacement { get; set; }

        /// <summary>显示数值</summary>
        [ScadaProperty(Key = "ShowValue", DisplayName = "显示数值", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "False",
                       Description = "在符号中央叠一个读数（等价于「符号 + 数值域」合体）")]
        public partial bool ShowValue { get; set; }

        /// <summary>数值（可绑变量）</summary>
        [ScadaProperty(Key = "Value", DisplayName = "数值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", IsBindable = true,
                       Description = "打开「显示数值」后显示在符号中央；可绑变量")]
        public partial double Value { get; set; }

        /// <summary>数值格式</summary>
        [ScadaProperty(Key = "ValueFormat", DisplayName = "数值格式", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "0.#", IsBindable = false,
                       Description = "如 0.0 / 0 / 0.###")]
        public partial string? ValueFormat { get; set; }

        /// <summary>单位</summary>
        [ScadaProperty(Key = "Unit", DisplayName = "单位", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "", IsBindable = false,
                       Description = "跟在读数后面，如 ℃ / MPa")]
        public partial string? Unit { get; set; }

        /// <summary>允许点击</summary>
        [ScadaProperty(Key = "Interactive", DisplayName = "允许点击", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Interaction, DefaultValue = "False",
                       Description = "打开后才发按下/释放事件（点设备弹详情）。默认关：工艺符号是图，不是操作件")]
        public partial bool Interactive { get; set; }

        // ---- 只读投影 ----

        private static readonly DependencyPropertyKey OutlineGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(OutlineGeometry), typeof(Geometry), typeof(ProcessSymbolElement), new PropertyMetadata(null));

        public static readonly DependencyProperty OutlineGeometryProperty = OutlineGeometryPropertyKey.DependencyProperty;

        /// <summary>轮廓几何（描边）</summary>
        public Geometry? OutlineGeometry => (Geometry?)GetValue(OutlineGeometryProperty);

        private static readonly DependencyPropertyKey AccentGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(AccentGeometry), typeof(Geometry), typeof(ProcessSymbolElement), new PropertyMetadata(null));

        public static readonly DependencyProperty AccentGeometryProperty = AccentGeometryPropertyKey.DependencyProperty;

        /// <summary>装饰几何（填充，颜色随状态）</summary>
        public Geometry? AccentGeometry => (Geometry?)GetValue(AccentGeometryProperty);

        private static readonly DependencyPropertyKey AccentBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(AccentBrush), typeof(Brush), typeof(ProcessSymbolElement),
            new PropertyMetadata(Brushes.Transparent));

        public static readonly DependencyProperty AccentBrushProperty = AccentBrushPropertyKey.DependencyProperty;

        /// <summary>装饰色（按 <see cref="State"/> 选）</summary>
        public Brush AccentBrush => (Brush)GetValue(AccentBrushProperty);

        private static readonly DependencyPropertyKey ValueTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ValueText), typeof(string), typeof(ProcessSymbolElement), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty ValueTextProperty = ValueTextPropertyKey.DependencyProperty;

        /// <summary>读数文本（数值 + 单位）</summary>
        public string ValueText => (string)GetValue(ValueTextProperty);

        private static readonly DependencyPropertyKey ValueVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ValueVisibility), typeof(Visibility), typeof(ProcessSymbolElement),
            new PropertyMetadata(Visibility.Collapsed));

        public static readonly DependencyProperty ValueVisibilityProperty = ValueVisibilityPropertyKey.DependencyProperty;

        /// <summary>读数显隐</summary>
        public Visibility ValueVisibility => (Visibility)GetValue(ValueVisibilityProperty);

        private static readonly DependencyPropertyKey TagAboveVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(TagAboveVisibility), typeof(Visibility), typeof(ProcessSymbolElement),
            new PropertyMetadata(Visibility.Collapsed));

        public static readonly DependencyProperty TagAboveVisibilityProperty = TagAboveVisibilityPropertyKey.DependencyProperty;

        /// <summary>位号摆在符号上方（模板里是第 0 行）</summary>
        public Visibility TagAboveVisibility => (Visibility)GetValue(TagAboveVisibilityProperty);

        private static readonly DependencyPropertyKey TagBelowVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(TagBelowVisibility), typeof(Visibility), typeof(ProcessSymbolElement),
            new PropertyMetadata(Visibility.Visible));

        public static readonly DependencyProperty TagBelowVisibilityProperty = TagBelowVisibilityPropertyKey.DependencyProperty;

        /// <summary>位号摆在符号下方（模板里是第 2 行）</summary>
        public Visibility TagBelowVisibility => (Visibility)GetValue(TagBelowVisibilityProperty);

        private static readonly DependencyPropertyKey TagInsideVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(TagInsideVisibility), typeof(Visibility), typeof(ProcessSymbolElement),
            new PropertyMetadata(Visibility.Collapsed));

        public static readonly DependencyProperty TagInsideVisibilityProperty = TagInsideVisibilityPropertyKey.DependencyProperty;

        /// <summary>位号压在符号中央（模板里跨三行）</summary>
        public Visibility TagInsideVisibility => (Visibility)GetValue(TagInsideVisibilityProperty);

        private ScadaBeatSource? _beat;

        public ProcessSymbolElement()
        {
        }

        partial void OnSymbolPropertyChanged() => RebuildGeometry();
        partial void OnDetailCountPropertyChanged() => RebuildGeometry();
        partial void OnStatePropertyChanged() => UpdateAccent();
        partial void OnAccentColorPropertyChanged() => UpdateAccent();
        partial void OnStoppedColorPropertyChanged() => UpdateAccent();
        partial void OnFaultColorPropertyChanged() => UpdateAccent();
        partial void OnBlinkOnFaultPropertyChanged() => UpdateBlinkSubscription();
        partial void OnShowValuePropertyChanged() => UpdateValueText();
        partial void OnValuePropertyChanged() => UpdateValueText();
        partial void OnValueFormatPropertyChanged() => UpdateValueText();
        partial void OnUnitPropertyChanged() => UpdateValueText();
        partial void OnTagPlacementPropertyChanged() => UpdateTagPlacement();

        protected override void OnElementRefreshed()
        {
            base.OnElementRefreshed();
            RebuildGeometry();
            UpdateAccent();
            UpdateValueText();
            UpdateTagPlacement();
        }

        /// <summary>
        /// 「允许点击」关着时不发按下/释放：工艺符号默认是"图"，不是操作件。
        ///
        /// 为什么在这里拦而不是设 <c>IsHitTestVisible=false</c>：那样设计期也点不中，
        /// 图元就选不上了。命中测试必须一直通，只有"发声"这一步按开关收口。
        /// </summary>
        protected internal override void RaiseScadaEvent(ScadaEventType eventType)
        {
            if (!Interactive
                && (eventType == ScadaEventType.Pressed || eventType == ScadaEventType.Released))
            {
                return;
            }

            base.RaiseScadaEvent(eventType);
        }

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            base.OnRuntimeContextChanged(oldContext, newContext);

            _beat?.Beat -= OnBeat;
            _beat = null;
            UpdateBlinkSubscription();
        }

        /// <summary>当前符号种类（解析失败回落塔）</summary>
        public SymbolKind CurrentKind
            => Enum.TryParse<SymbolKind>(Symbol, ignoreCase: true, out var kind) ? kind : SymbolKind.Tower;

        /// <summary>生效的细部数量（0 = 用符号建议值）</summary>
        public int EffectiveDetailCount
        {
            get
            {
                var definition = ProcessSymbolCatalog.Get(CurrentKind);
                return DetailCount > 0 ? Math.Clamp(DetailCount, 0, 20) : definition.DefaultDetailCount;
            }
        }

        private void RebuildGeometry()
        {
            var definition = ProcessSymbolCatalog.Get(CurrentKind);

            // 在固定的 100×100 设计空间里画，模板用 Viewbox 等比缩放：
            // 符号是"图形"，拉伸变形比留白更难看；固定空间也让几何不随尺寸抖动重算。
            SetValue(OutlineGeometryPropertyKey,
                ProcessSymbolCatalog.BuildGeometry(definition, SymbolRole.Outline, DesignSize, DesignSize, EffectiveDetailCount));
            SetValue(AccentGeometryPropertyKey,
                ProcessSymbolCatalog.BuildGeometry(definition, SymbolRole.Accent, DesignSize, DesignSize, EffectiveDetailCount));
        }

        private void UpdateAccent()
        {
            var state = (State ?? StateNone).Trim();

            var brush = state.Equals(StateRunning, StringComparison.OrdinalIgnoreCase) ? AccentColor
                : state.Equals(StateStopped, StringComparison.OrdinalIgnoreCase) ? StoppedColor
                : state.Equals(StateFault, StringComparison.OrdinalIgnoreCase) ? FaultColor
                : AccentColor;

            SetValue(AccentBrushPropertyKey, brush ?? Brushes.Transparent);
            UpdateBlinkSubscription();
        }

        private void UpdateBlinkSubscription()
        {
            var shouldBlink = BlinkOnFault
                && string.Equals((State ?? string.Empty).Trim(), StateFault, StringComparison.OrdinalIgnoreCase)
                && RuntimeContext?.Beat != null;

            if (shouldBlink)
            {
                if (_beat == null && RuntimeContext?.Beat is { } beat)
                {
                    _beat = beat;
                    _beat.Beat += OnBeat;
                }
            }
            else
            {
                if (_beat != null)
                {
                    _beat.Beat -= OnBeat;
                    _beat = null;
                }

                Opacity = 1d;
            }
        }

        private void OnBeat(TimeSpan elapsed)
        {
            if (_beat == null)
                return;

            // 相位读统一节拍源（与报警条同一套）：各图元自己起定时器会各闪各的
            var phase = (elapsed.TotalMilliseconds / BlinkHalfPeriodMilliseconds) % 2;
            Opacity = phase < 1 ? 1d : 0.3d;
        }

        private void UpdateValueText()
        {
            SetValue(ValueVisibilityPropertyKey, ShowValue ? Visibility.Visible : Visibility.Collapsed);

            if (!ShowValue)
            {
                SetValue(ValueTextPropertyKey, string.Empty);
                return;
            }

            var format = string.IsNullOrWhiteSpace(ValueFormat) ? "0.#" : ValueFormat!.Trim();
            string text;

            try
            {
                text = Value.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                text = Value.ToString("0.#", CultureInfo.InvariantCulture);
            }

            var unit = string.IsNullOrWhiteSpace(Unit) ? string.Empty : " " + Unit!.Trim();
            SetValue(ValueTextPropertyKey, text + unit);
        }

        private void UpdateTagPlacement()
        {
            var placement = (TagPlacement ?? "Below").Trim().ToLowerInvariant();

            SetValue(TagAboveVisibilityPropertyKey,
                placement == "above" ? Visibility.Visible : Visibility.Collapsed);
            SetValue(TagBelowVisibilityPropertyKey,
                placement == "below" ? Visibility.Visible : Visibility.Collapsed);
            SetValue(TagInsideVisibilityPropertyKey,
                placement == "inside" ? Visibility.Visible : Visibility.Collapsed);
        }
    }
}
