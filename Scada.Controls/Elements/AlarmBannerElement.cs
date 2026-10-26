using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 实时报警条图元（TypeKey = <c>Hmi.AlarmBanner</c>）：把运行态的报警引擎直接画成一列报警。
    ///
    /// 它跟别的图元有什么不同
    /// ---------
    /// 别的图元都是"一根变量 → 一个外观"：<see cref="LampElement"/> 吃一个状态、指示灯吃一个布尔。
    /// 报警条吃的是<b>一整张表</b>——当前挂着哪几条报警、各自什么严重度、确认了没有。
    /// 这些事实由 <see cref="ScadaAlarmEngine"/> 在运行态产生，值变化来自变量轮询线程，
    /// 还带时间维度（延时、回差、断线）。所以它不接 <see cref="ScadaBinding"/>，
    /// 接的是宿主注入的 <see cref="ScadaRuntimeContext"/>。
    ///
    /// 数据从哪来、什么时候重画
    /// ---------
    /// 装上运行态上下文时挂 <see cref="ScadaAlarmEngine.AlarmRaised"/> /
    /// <see cref="ScadaAlarmEngine.AlarmChanged"/> / <see cref="ScadaAlarmEngine.AlarmCleared"/>，
    /// 任一事件来了就<b>整表重读</b> <see cref="ScadaAlarmEngine.ActiveAlarms"/>。
    /// 不做增量：报警列表最多几十行，重读一次是几十次比较；而增量更新要维护"哪一行对应哪条记录"，
    /// 配置热重载（引擎会自动重挂，旧记录成批消失）时最容易留下幽灵行。
    /// 三个事件共用一个处理函数，也是同一个理由——既然整表重读，就不必知道"是哪一条怎么变了"。
    ///
    /// 排序不在这里做：<see cref="ScadaAlarmEngine.ActiveAlarms"/> 已经按
    /// 严重度降序、同级按激活时间降序排好（那是产品规则，不是本面板的显示偏好），
    /// 这里只做"取前 <see cref="MaxRows"/> 行"。
    ///
    /// 闪烁为什么要接节拍源
    /// ---------
    /// "有严重未确认的报警时左侧色条闪"是现场最直白的"有人在等你"。若本图元自己起一个
    /// <c>DispatcherTimer</c>，一屏放两个报警条就会各闪各的（相位对不齐，看上去像画面卡了）——
    /// 这正是 <see cref="LampElement"/> 类注释里点名要避免的坑。所以相位一律读
    /// <see cref="ScadaBeatSource.Elapsed"/>：同一屏上所有会闪的东西天然对齐。
    ///
    /// 线程
    /// ---------
    /// 引擎的三个事件可能在变量轮询线程上抛（见 <see cref="ScadaAlarmEngine"/> 的线程约定），
    /// 而依赖属性有线程亲和性，所以每个入口都先切回本控件的 <see cref="DispatcherObject.Dispatcher"/>。
    /// 节拍本来就在宿主的 UI 线程上，这里的 <c>CheckAccess</c> 只是不假设这件事。
    ///
    /// 设计态（<see cref="ScadaElementBase.RuntimeContext"/> 为 null）
    /// ---------
    /// 显示 <see cref="EmptyText"/>（默认"系统正常"），正好是组态时想看到的预览。
    /// 编辑器里不运行、也没有引擎，这条分支是常态而不是异常。
    /// </summary>
    [TemplatePart(Name = PartListScroll, Type = typeof(ScrollViewer))]
    [TemplatePart(Name = PartEmptyHint, Type = typeof(TextBlock))]
    [TemplatePart(Name = PartAccentBar, Type = typeof(Border))]
    public partial class AlarmBannerElement : ScadaElementBase
    {
        // 闪烁半周期用基类的 ScadaElementBase.BlinkHalfPeriodMilliseconds：
        // 报警条的闪烁与图元动画的闪烁必须是同一套节拍，值只能有一个来源。

        /// <summary>模板部件名：报警行列表（一条报警都没有时收起）</summary>
        public const string PartListScroll = "ListScroll";

        /// <summary>模板部件名：空态提示（有报警时收起，与列表互斥）</summary>
        public const string PartEmptyHint = "EmptyHint";

        /// <summary>模板部件名：左侧严重度色条（闪烁就是改它的透明度）</summary>
        public const string PartAccentBar = "AccentBar";

        /// <summary>跑马灯容器（单行、裁剪）</summary>
        public const string PartTickerHost = "TickerHost";

        /// <summary>跑马灯文字（Inlines 由代码按严重度上色）</summary>
        public const string PartTickerText = "PART_TickerText";

        /// <summary>跑马灯的横向位移</summary>
        public const string PartTickerShift = "TickerShift";

        /// <summary>闪烁"暗"那一拍的色条透明度</summary>
        private const double BlinkDimOpacity = 0.25;

        /// <summary>行数上限的钳制范围：0 行等于隐形、几百行等于没有上限，两者都不该被配出来</summary>
        private const int MinRows = 1;
        private const int MaxRowsLimit = 50;

        // 默认色必须冻结：依赖属性默认值被所有实例共享，未冻结的 Freezable 被某实例改到会串到别的实例。
        // 其中 NormalColor 还要给 AccentBrush 的只读默认值用，所以这一个字段留着；
        // 其余三个的默认值由 ScadaPropertyDefaults.Resolve 在类型静态构造期解析并冻结。
        private static readonly Brush DefaultNormalColor = ScadaBrushes.Frozen("#FF34C759");

        // 声明次序 = 属性面板里的行序（生成器按源码次序收集）：数据 → 文字 → 状态，
        // 与原先手写描述符的分组次序一致。

        /// <summary>最多显示几行（超出的截断；表头的计数仍报全量）</summary>
        [ScadaProperty(Key = "MaxRows", DisplayName = "最多显示", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "5", Min = 1, Max = 50,
                       Description = "最多画几行，超出的不画（表头计数仍报全量）；" +
                                     "行数多到超出控件高度时会出现滚动条，所以这里不必留余量")]
        public partial int MaxRows { get; set; }

        /// <summary>
        /// 形态：`Ticker` = 单行跑马灯（默认，从右往左滚，只报"哪条报警在触发"）；
        /// `List` = 卡片列表（带严重度胶囊、时间、状态，占地方但信息全）。
        ///
        /// 为什么默认改成跑马灯（2026-10-25 用户反馈）：报警条钉在画面顶部，卡片列表 140px 高
        /// 会吃掉整块画面；跑马灯单行 28~32px 就够，且"报警在响"这件事本身就是扫一眼。
        /// </summary>
        [ScadaProperty(Key = "ScrollMode", DisplayName = "形态", Kind = ElementPropertyKind.Choice,
                       Choices = new[] { "Ticker", "List" },
                       Group = ScadaPropertyGroups.State, DefaultValue = "Ticker",
                       Description = "Ticker = 单行跑马灯（从右往左，默认）；List = 卡片列表（带时间与状态，占地方）")]
        public partial string ScrollMode { get; set; }

        /// <summary>跑马灯速度（像素/秒）</summary>
        [ScadaProperty(Key = "ScrollSpeed", DisplayName = "跑马灯速度（像素/秒）", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.State, DefaultValue = "60", Min = 10, Max = 300,
                       Description = "单行跑马灯的移动速度；太快看不清，现场一般 40~80")]
        public partial double ScrollSpeed { get; set; }

        /// <summary>自动滚动（默认开）。报警条是"扫一眼"的东西：条数超过一屏时，
        /// 让它按固定节奏逐行下移、到底回顶，操作员不必手动去拖滚动条
        /// （触摸屏上拖一条 140px 高的条子本来就很难受）。
        /// </summary>
        [ScadaProperty(Key = "AutoScroll", DisplayName = "自动滚动", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.State, DefaultValue = "True",
                       Description = "条数超过一屏时按固定节奏逐行下移、到底回顶；关掉则停在原处由用户手动滚动")]
        public partial bool AutoScroll { get; set; }

        /// <summary>每行停留秒数（自动滚动时）</summary>
        [ScadaProperty(Key = "SecondsPerRow", DisplayName = "每行停留（秒）", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.State, DefaultValue = "2.5", Min = 0.5, Max = 30,
                       Description = "自动滚动时每行停留多久。太小看不清、太大像没在动；现场一般 2~3 秒")]
        public partial double SecondsPerRow { get; set; }

        /// <summary>一条报警都没有时显示的那句话</summary>
        [ScadaProperty(Key = "EmptyText", DisplayName = "无报警提示", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "系统正常",
                       Description = "一条报警都没有时显示的字；设计期预览看到的也是它")]
        public partial string? EmptyText { get; set; }

        /// <summary>没有报警时左侧色条与徽标的颜色（"一切正常"的绿）</summary>
        [ScadaProperty(Key = "NormalColor", DisplayName = "正常色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.State, DefaultValue = "#FF34C759",
                       Description = "没有报警时左侧色条的颜色；与多态灯、泵、管道的「运行绿」是同一个色")]
        public partial Brush? NormalColor { get; set; }

        /// <summary>提示级报警的颜色</summary>
        [ScadaProperty(Key = "InfoColor", DisplayName = "提示色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.State, DefaultValue = "#FF3B82F6")]
        public partial Brush? InfoColor { get; set; }

        /// <summary>警告级报警的颜色</summary>
        [ScadaProperty(Key = "WarningColor", DisplayName = "警告色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.State, DefaultValue = "#FFFFB020",
                       Description = "警告级报警的颜色；与多态灯的「警告黄」同色，整幅画面一套语汇")]
        public partial Brush? WarningColor { get; set; }

        /// <summary>严重级报警的颜色（也是"要闪"的那一档）</summary>
        [ScadaProperty(Key = "CriticalColor", DisplayName = "严重色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.State, DefaultValue = "#FFE03A2B",
                       Description = "严重级报警的颜色（也是触发闪烁的那一档）；与多态灯的「报警红」同色")]
        public partial Brush? CriticalColor { get; set; }

        private static readonly DependencyPropertyKey RowsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(Rows), typeof(IReadOnlyList<AlarmBannerRow>), typeof(AlarmBannerElement),
            new PropertyMetadata(Array.Empty<AlarmBannerRow>()));

        /// <summary>要显示的行（已截断到 <see cref="MaxRows"/> 行，顺序即引擎给的次序）</summary>
        public static readonly DependencyProperty RowsProperty = RowsPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey HasAlarmsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HasAlarms), typeof(bool), typeof(AlarmBannerElement),
            new PropertyMetadata(false));

        /// <summary>此刻有没有报警——模板据此在"列表"与"空态提示"之间切换</summary>
        public static readonly DependencyProperty HasAlarmsProperty = HasAlarmsPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey SummaryTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(SummaryText), typeof(string), typeof(AlarmBannerElement),
            new PropertyMetadata(string.Empty));

        /// <summary>表头右侧的计数（如 <c>"未确认 2 / 共 3"</c>）；无报警时为空串</summary>
        public static readonly DependencyProperty SummaryTextProperty = SummaryTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey AccentBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(AccentBrush), typeof(Brush), typeof(AlarmBannerElement),
            new PropertyMetadata(DefaultNormalColor));

        /// <summary>左侧色条的颜色：当前最高严重度；无报警时是 <see cref="NormalColor"/></summary>
        public static readonly DependencyProperty AccentBrushProperty = AccentBrushPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey IsFlashingPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsFlashing), typeof(bool), typeof(AlarmBannerElement),
            new PropertyMetadata(false));

        /// <summary>此刻是否该闪（存在"严重且未确认"的报警）</summary>
        public static readonly DependencyProperty IsFlashingProperty = IsFlashingPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey BlinkOnPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(BlinkOn), typeof(bool), typeof(AlarmBannerElement),
            new PropertyMetadata(true));

        /// <summary>
        /// 闪烁的当前相位：该亮时为 true。不闪的时候恒为 true（色条常亮）。
        /// 控件拿不到模板内部的元素（那正是"模板可以被替换"的代价），所以相位做成属性；
        /// 色条的透明度由 <see cref="UpdateBannerLayout"/> 读这个属性去改，
        /// 而不是在模板触发器里绑它——本环境下 ControlTemplate.Triggers 里的 DataTrigger 不触发。
        /// </summary>
        public static readonly DependencyProperty BlinkOnProperty = BlinkOnPropertyKey.DependencyProperty;

        /// <summary>引擎此刻挂着的全部报警（未截断，节拍与颜色换算都要用全量）</summary>
        private IReadOnlyList<ScadaAlarmRecord> _active = Array.Empty<ScadaAlarmRecord>();

        /// <summary>与 <see cref="IsFlashingProperty"/> 同步的普通字段，供节拍线程无锁读取</summary>
        private bool _isFlashing;

        /// <summary>跑马灯开始悬浮的时刻（累计秒）；null = 没在暂停</summary>
        private double? _tickerPausedAt;

        /// <summary>累计被暂停的秒数（恢复时从位移里扣掉，避免文字跳回起点）</summary>
        private double _tickerPausedSeconds;

        /// <summary>与 <see cref="BlinkOnProperty"/> 同步的普通字段，同上</summary>
        private bool _blinkOn = true;

        static AlarmBannerElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(AlarmBannerElement),
                new FrameworkPropertyMetadata(typeof(AlarmBannerElement)));
        }

        /// <summary>要显示的行（只读，见 <see cref="RowsProperty"/>）</summary>
        public IReadOnlyList<AlarmBannerRow> Rows => (IReadOnlyList<AlarmBannerRow>)GetValue(RowsProperty);

        /// <summary>此刻有没有报警（只读，见 <see cref="HasAlarmsProperty"/>）</summary>
        public bool HasAlarms => (bool)GetValue(HasAlarmsProperty);

        /// <summary>表头计数文本（只读，见 <see cref="SummaryTextProperty"/>）</summary>
        public string SummaryText => (string)GetValue(SummaryTextProperty);

        /// <summary>左侧色条颜色（只读，见 <see cref="AccentBrushProperty"/>）</summary>
        public Brush? AccentBrush => (Brush?)GetValue(AccentBrushProperty);

        /// <summary>此刻是否该闪（只读，见 <see cref="IsFlashingProperty"/>）</summary>
        public bool IsFlashing => (bool)GetValue(IsFlashingProperty);

        /// <summary>闪烁相位（只读，见 <see cref="BlinkOnProperty"/>）</summary>
        public bool BlinkOn => (bool)GetValue(BlinkOnProperty);

        protected override void OnElementRefreshed() => ApplyStrokeInset(1);

        // 模板刚套上时补摆一次：HasAlarms / IsFlashing / BlinkOn 若在套模板之前就落地，
        // 那几次调用都是空操作（那时还取不到模板部件），这里必须再摆一次。
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            // 模板里的位移变换是"封存期被冻结、所有实例共享"的那一个，必须在这里换成本实例可写的一份。
            // 见 EnsureTickerShift 的说明；提前换（而不是等第一拍）还有一个好处：
            // "模板套上之后跑马灯的变换一定可写"成为一条可以静态断言的不变式。
            if (GetTemplateChild(PartTickerText) is TextBlock tickerText)
                EnsureTickerShift(tickerText);

            UpdateBannerLayout();
        }

        /// <summary>
        /// 按当前状态摆好模板里三块互相顶替的东西：列表 / 空态提示 / 色条明暗。
        ///
        /// 本环境下 <c>ControlTemplate.Triggers</c> 里的 DataTrigger 一律不触发
        /// （见 <see cref="ScadaElementBase.SetPartVisible"/>），所以这两件事只能由代码翻，
        /// 模板里<b>既不写 Visibility 初值、也不写 ControlTemplate.Triggers</b>。
        /// 模板尚未套上时是空操作，套上后由 <see cref="OnApplyTemplate"/> 补一次。
        /// </summary>
        private void UpdateBannerLayout()
        {
            var ticker = IsTickerMode;

            // 跑马灯与卡片列表互斥：有报警时按形态二选一，没报警时两块都收起、只留空态提示。
            SetPartVisible(PartTickerHost, HasAlarms && ticker);
            SetPartVisible(PartListScroll, HasAlarms && !ticker);
            SetPartVisible(PartEmptyHint, !HasAlarms);

            // 空态文案分两种（2026-10-26 会审 P1）：没接报警源 ≠ 系统正常。
            // 前者是"静默假正常"，比不显示更危险——工程忘了启用报警时，界面不该替它报平安。
            if (GetTemplateChild(PartEmptyHint) is TextBlock hint)
            {
                var hasSource = RuntimeContext?.Alarms != null;
                hint.Text = AlarmBannerScroll.EmptyHintFor(hasSource, EmptyText);
                hint.Opacity = hasSource ? 1d : 0.75d;
            }

            if (ticker && HasAlarms)
                RebuildTicker();

            if (GetTemplateChild(PartAccentBar) is UIElement accent)
                accent.Opacity = IsFlashing && !BlinkOn ? BlinkDimOpacity : 1d;
        }

        /// <summary>形态是否为单行跑马灯（任何非 List 的写法都按 Ticker 处理，老工程缺键时也走默认）</summary>
        private bool IsTickerMode
            => !string.Equals(ScrollMode, "List", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 重建跑马灯文字：**所有"触发条件为 ON"的报警**都进队列（不看是否确认——
        /// 用户口径：报警在响就该滚，确认与否是报警汇总表的事），按严重度排序，
        /// 每条按自己的严重度色，条与条之间用分隔符隔开。
        /// </summary>
        private void RebuildTicker()
        {
            if (GetTemplateChild(PartTickerText) is not TextBlock text)
                return;

            text.Inlines.Clear();

            // 内容换了就从头滚（新的那句话从右缘进来）；暂停累计归零，否则新内容会"凭空偏一段"
            _tickerPausedSeconds = 0;
            _tickerPausedAt = null;

            for (int i = 0; i < _active.Count; i++)
            {
                if (i > 0)
                    text.Inlines.Add(new Run("　·　") { Foreground = HmiMutedBrush() });

                var record = _active[i];
                var label = string.IsNullOrWhiteSpace(record.Message)
                    ? record.Name
                    : $"{record.Name}：{record.Message}";

                text.Inlines.Add(new Run(label) { Foreground = PickColor(record.Severity), FontWeight = FontWeights.SemiBold });
            }
        }

        private static Brush HmiMutedBrush()
            => System.Windows.Application.Current?.TryFindResource("HmiTextMutedBrush") as Brush ?? Brushes.Gray;

        /// <summary>跑马灯每拍推进（按累计时长取模，不累加）</summary>
        private void ApplyTicker(TimeSpan elapsed)
        {
            if (!IsTickerMode || !HasAlarms)
                return;

            if (GetTemplateChild(PartTickerText) is not TextBlock text)
                return;

            var shift = EnsureTickerShift(text);

            // 悬浮暂停（2026-10-26 用户要求）：鼠标停在条上就不走，让人把这句话读完。
            // 恢复时**扣掉暂停时长**而不是清零重来——清零会让文字瞬间跳回起点，读的人跟丢。
            var now = elapsed.TotalSeconds;

            if (IsMouseOver)
            {
                _tickerPausedAt ??= now;
                return;
            }

            if (_tickerPausedAt is { } pausedAt)
            {
                _tickerPausedSeconds += Math.Max(0, now - pausedAt);
                _tickerPausedAt = null;
            }

            var effective = AlarmBannerScroll.EffectiveSeconds(now, _tickerPausedSeconds);

            var textWidth = text.ActualWidth;
            var viewport = Math.Max(0, ActualWidth);

            if (textWidth <= 0)
            {
                // 还没量出来（首帧/文本刚换）：下一拍再算，别把位移钉死在 0
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                textWidth = text.DesiredSize.Width;
                if (textWidth <= 0)
                    return;
            }

            shift.X = AlarmBannerScroll.TickerX(effective, ScrollSpeed, textWidth, viewport);
        }

        /// <summary>
        /// 取跑马灯文字的位移变换，保证它是<b>本实例私有、可写</b>的那一个。
        ///
        /// <b>为什么不能直接用模板里声明的那一个</b>：WPF 在模板封存时会把 ControlTemplate 里的
        /// Freezable <b>冻结</b>，而且<b>所有实例共享同一个冻结对象</b>（.NET 9 实测：同一个模板
        /// 两次实例化拿到的 RenderTransform 引用相等、<c>IsFrozen</c> 为 true）。
        /// 往它身上写 <c>X</c> 会抛"无法在对象 System.Windows.Media.TranslateTransform 上设置属性，
        /// 因为它处于只读状态"，而调用点是节拍订阅者——异常被 <see cref="ScadaBeatSource"/> 吞成
        /// 一行日志，现场表现是<b>跑马灯纹丝不动 + 日志每拍刷两行</b>，且两条报警条就算不抛也会
        /// 共用同一个位移。所以模板只负责声明"有个位移"，真正写入的对象在这里换成本实例新造的。
        ///
        /// 只在真的不可写时才替换：模板若哪天改成代码赋值（未冻结），这条路径不会再白造对象。
        /// </summary>
        private static TranslateTransform EnsureTickerShift(TextBlock text)
        {
            if (text.RenderTransform is TranslateTransform shift && !shift.IsFrozen)
                return shift;

            var fresh = new TranslateTransform();
            text.RenderTransform = fresh;

            return fresh;
        }

        /// <summary>
        /// 运行态上下文换人：退掉旧引擎与旧节拍的订阅、挂上新的，然后立刻整表重读一次。
        ///
        /// <b>为什么订阅只跟着上下文走，不跟着 Loaded 走</b>
        /// 模型订阅（<see cref="ScadaElementBase"/> 里的那份）必须在 Unloaded 时退掉，因为模型是
        /// 长命的编辑期对象，不退就是一路泄漏。这里相反：引擎的生命周期就是"这一轮运行"，
        /// 收场时宿主把上下文置回 null，订阅在那时成对退掉——覆盖了完整的生命周期。
        /// 而"跟着 Loaded 走"会让无窗口的断言（不会触发 Loaded）完全测不到实时路径，
        /// 那是拿可测性换一个本来就不存在的泄漏。
        /// </summary>
        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            if (oldContext != null)
            {
                oldContext.Alarms.AlarmRaised -= OnEngineAlarmChanged;
                oldContext.Alarms.AlarmChanged -= OnEngineAlarmChanged;
                oldContext.Alarms.AlarmCleared -= OnEngineAlarmChanged;
                oldContext.Beat.Beat -= OnBeat;
            }

            if (newContext != null)
            {
                newContext.Alarms.AlarmRaised += OnEngineAlarmChanged;
                newContext.Alarms.AlarmChanged += OnEngineAlarmChanged;
                newContext.Alarms.AlarmCleared += OnEngineAlarmChanged;
                newContext.Beat.Beat += OnBeat;
            }

            RefreshFromEngine();
        }

        // 五个输入共用同一条副作用（行、色条、计数都要重画）。
        // EmptyText 不在此列：它只是模板里一段文字，改了由绑定自己刷新（原手写依赖属性也只挂了 AffectsRender）。
        partial void OnMaxRowsPropertyChanged() => RebuildRows();
        partial void OnNormalColorPropertyChanged() => RebuildRows();
        partial void OnInfoColorPropertyChanged() => RebuildRows();
        partial void OnWarningColorPropertyChanged() => RebuildRows();
        partial void OnCriticalColorPropertyChanged() => RebuildRows();

        /// <summary>引擎上有任何动静 → 整表重读（三个事件共用，理由见类注释）</summary>
        private void OnEngineAlarmChanged(ScadaAlarmRecord record) => RunOnUi(RefreshFromEngine);

        /// <summary>整表重读一次：快照 + 重画行</summary>
        private void RefreshFromEngine()
        {
            _active = RuntimeContext?.Alarms.ActiveAlarms ?? Array.Empty<ScadaAlarmRecord>();

            RebuildRows();
        }

        /// <summary>
        /// 按快照重画行（同时刷新色条、计数、闪烁开关）。
        ///
        /// 颜色与行数是输入，行是输出，所以改颜色、改行数都从这条路上走一次——
        /// 比"在模板里按严重度挑颜色"少一套转换器，也少一处"颜色改了行没跟着变"的隐患。
        /// </summary>
        private void RebuildRows()
        {
            int max = Math.Clamp(MaxRows, MinRows, MaxRowsLimit);
            int shown = Math.Min(max, _active.Count);

            var rows = new List<AlarmBannerRow>(shown);

            for (int i = 0; i < shown; i++)
                rows.Add(new AlarmBannerRow(_active[i], PickColor(_active[i].Severity)));

            SetValue(RowsPropertyKey, (IReadOnlyList<AlarmBannerRow>)rows);
            SetValue(HasAlarmsPropertyKey, _active.Count > 0);
            SetValue(AccentBrushPropertyKey, _active.Count == 0
                ? NormalColor ?? Brushes.Transparent
                : PickColor(_active[0].Severity)); // 已按严重度降序，第一条就是最高的那条
            SetValue(SummaryTextPropertyKey, BuildSummary(_active.Count));

            bool flashing = _active.Any(r => r.Severity == ScadaAlarmSeverity.Critical && r.State.NeedsAcknowledge());
            _isFlashing = flashing;
            SetValue(IsFlashingPropertyKey, flashing);

            if (!flashing && !_blinkOn)
            {
                // 不闪了就把相位归位到"亮"，否则色条会停在暗的那一拍上，
                // 看着像"报警还在但变灰了"——比不闪更让人误解。
                _blinkOn = true;
                SetValue(BlinkOnPropertyKey, true);
            }

            // 行数、有无报警、是否该闪都可能刚变过，模板里那三块互相顶替的东西在这里一次摆平。
            UpdateBannerLayout();
        }

        /// <summary>
        /// 表头计数。
        ///
        /// "共几条"与"几条没确认"是两个不同的问题：前者是"设备现在有多糟"，
        /// 后者是"我还欠多少活"。只给一个数字的话，操作员没法判断自己能不能走开，
        /// 所以两个都给——这正是商业报警条表头的样子。
        /// </summary>
        private string BuildSummary(int count)
        {
            if (count == 0)
                return string.Empty;

            int pending = RuntimeContext?.Alarms.UnacknowledgedCount ?? 0;

            return pending > 0 ? $"未确认 {pending} / 共 {count}" : $"共 {count} 条";
        }

        /// <summary>按严重度挑颜色；没配到就退回透明（宁可看不见，也别拿别人的颜色顶上）</summary>
        private Brush PickColor(ScadaAlarmSeverity severity) => severity switch
        {
            ScadaAlarmSeverity.Critical => CriticalColor,
            ScadaAlarmSeverity.Warning => WarningColor,
            _ => InfoColor,
        } ?? Brushes.Transparent;

        /// <summary>
        /// 自动滚动：按累计时长算出该停在哪一行，把列表滚过去。
        ///
        /// 行数不足一屏时不滚（否则"滚了个寂寞"，还让人以为漏了报警）；
        /// 位置按累计时长取模，漏拍/卡顿都不会越走越偏（见 <see cref="AlarmBannerScroll"/>）。
        /// </summary>
        private void ApplyAutoScroll(TimeSpan elapsed)
        {
            if (!AutoScroll)
                return;

            if (GetTemplateChild(PartListScroll) is not ScrollViewer scroll)
                return;

            int count = Rows?.Count ?? 0;
            if (count <= 1 || scroll.ViewportHeight <= 0)
                return;

            int visible = Math.Max(1, (int)(scroll.ViewportHeight / AlarmBannerScroll.RowStride));
            if (AlarmBannerScroll.ScrollPositions(count, visible) <= 0)
                return;

            double offset = AlarmBannerScroll.Offset(elapsed.TotalSeconds, SecondsPerRow, count, visible);

            if (Math.Abs(scroll.VerticalOffset - offset) > 0.5)
                scroll.ScrollToVerticalOffset(offset);
        }

        /// <summary>
        /// 节拍到了：只在"该闪"时改相位，其余时候一拍都不碰 UI。
        ///
        /// 相位从 <paramref name="elapsed"/>（累计时长）算，而不是"每拍取反"：
        /// 漏拍、卡顿、窗口被拖动时，取反会越走越偏，而按累计时长算永远落在正确的半周期上——
        /// 这正是 <see cref="ScadaBeatSource"/> 发累计时长而不是发"又过了一拍"的理由。
        /// </summary>
        private void OnBeat(TimeSpan elapsed)
        {
            // 自动滚动先走：它不依赖"该不该闪"，而且必须每拍都推进，
            // 否则一条不闪的报警条永远停在第一屏（2026-10-25 用户反馈"效果不理想"）。
            if (IsTickerMode)
                ApplyTicker(elapsed);
            else
                ApplyAutoScroll(elapsed);

            if (!_isFlashing)
                return;

            bool on = (long)(elapsed.TotalMilliseconds / BlinkHalfPeriodMilliseconds) % 2 == 0;

            if (on == _blinkOn)
                return;

            RunOnUi(() =>
            {
                _blinkOn = on;
                SetValue(BlinkOnPropertyKey, on);

                // 相位一变，色条的明暗就得跟着走一趟。
                UpdateBannerLayout();
            });
        }

        /// <summary>
        /// 把一段动作送回本控件的 UI 线程执行（已经在 UI 线程上就直接跑）。
        ///
        /// 为什么要判 <c>HasShutdownStarted</c>：运行窗口关掉的一瞬间，变量轮询线程可能正好
        /// 抛出一条报警，此时往已停摆的 Dispatcher 队列里塞东西会直接抛异常。
        /// 那种异常发生在收场路径上，最难归因，也不值得为它留一条日志。
        /// </summary>
        private void RunOnUi(Action action)
        {
            var dispatcher = Dispatcher;

            if (dispatcher.CheckAccess())
            {
                action();
                return;
            }

            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            dispatcher.BeginInvoke(DispatcherPriority.Background, action);
        }
    }
}
