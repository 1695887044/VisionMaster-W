using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
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
    public class AlarmBannerElement : ScadaElementBase
    {
        // 闪烁半周期用基类的 ScadaElementBase.BlinkHalfPeriodMilliseconds：
        // 报警条的闪烁与图元动画的闪烁必须是同一套节拍，值只能有一个来源。

        /// <summary>行数上限的钳制范围：0 行等于隐形、几百行等于没有上限，两者都不该被配出来</summary>
        private const int MinRows = 1;
        private const int MaxRowsLimit = 50;

        // 默认色必须冻结：依赖属性默认值被所有实例共享，未冻结的 Freezable 被某实例改到会串到别的实例。
        private static readonly Brush DefaultNormalColor = ScadaBrushes.Frozen("#FF34C759");
        private static readonly Brush DefaultInfoColor = ScadaBrushes.Frozen("#FF3B82F6");
        private static readonly Brush DefaultWarningColor = ScadaBrushes.Frozen("#FFFFB020");
        private static readonly Brush DefaultCriticalColor = ScadaBrushes.Frozen("#FFE03A2B");

        /// <summary>最多显示几行（超出的截断；表头的计数仍报全量）</summary>
        public static readonly DependencyProperty MaxRowsProperty = DependencyProperty.Register(
            nameof(MaxRows), typeof(int), typeof(AlarmBannerElement),
            new FrameworkPropertyMetadata(5, FrameworkPropertyMetadataOptions.AffectsRender, OnBannerInputChanged));

        /// <summary>没有报警时左侧色条与徽标的颜色（"一切正常"的绿）</summary>
        public static readonly DependencyProperty NormalColorProperty = DependencyProperty.Register(
            nameof(NormalColor), typeof(Brush), typeof(AlarmBannerElement),
            new FrameworkPropertyMetadata(DefaultNormalColor, FrameworkPropertyMetadataOptions.AffectsRender, OnBannerInputChanged));

        /// <summary>提示级报警的颜色</summary>
        public static readonly DependencyProperty InfoColorProperty = DependencyProperty.Register(
            nameof(InfoColor), typeof(Brush), typeof(AlarmBannerElement),
            new FrameworkPropertyMetadata(DefaultInfoColor, FrameworkPropertyMetadataOptions.AffectsRender, OnBannerInputChanged));

        /// <summary>警告级报警的颜色</summary>
        public static readonly DependencyProperty WarningColorProperty = DependencyProperty.Register(
            nameof(WarningColor), typeof(Brush), typeof(AlarmBannerElement),
            new FrameworkPropertyMetadata(DefaultWarningColor, FrameworkPropertyMetadataOptions.AffectsRender, OnBannerInputChanged));

        /// <summary>严重级报警的颜色（也是"要闪"的那一档）</summary>
        public static readonly DependencyProperty CriticalColorProperty = DependencyProperty.Register(
            nameof(CriticalColor), typeof(Brush), typeof(AlarmBannerElement),
            new FrameworkPropertyMetadata(DefaultCriticalColor, FrameworkPropertyMetadataOptions.AffectsRender, OnBannerInputChanged));

        /// <summary>一条报警都没有时显示的那句话</summary>
        public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
            nameof(EmptyText), typeof(string), typeof(AlarmBannerElement),
            new FrameworkPropertyMetadata("系统正常", FrameworkPropertyMetadataOptions.AffectsRender));

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
        /// 模板绑它去改色条的 Opacity——把相位做成一个属性、而不是在控件里直接改模板元素，
        /// 是因为控件拿不到模板内部的元素（那正是"模板可以被替换"的代价）。
        /// </summary>
        public static readonly DependencyProperty BlinkOnProperty = BlinkOnPropertyKey.DependencyProperty;

        /// <summary>引擎此刻挂着的全部报警（未截断，节拍与颜色换算都要用全量）</summary>
        private IReadOnlyList<ScadaAlarmRecord> _active = Array.Empty<ScadaAlarmRecord>();

        /// <summary>与 <see cref="IsFlashingProperty"/> 同步的普通字段，供节拍线程无锁读取</summary>
        private bool _isFlashing;

        /// <summary>与 <see cref="BlinkOnProperty"/> 同步的普通字段，同上</summary>
        private bool _blinkOn = true;

        static AlarmBannerElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(AlarmBannerElement),
                new FrameworkPropertyMetadata(typeof(AlarmBannerElement)));
        }

        /// <summary>最多显示几行（见 <see cref="MaxRowsProperty"/>）</summary>
        public int MaxRows
        {
            get => (int)GetValue(MaxRowsProperty);
            set => SetValue(MaxRowsProperty, value);
        }

        /// <summary>正常色（见 <see cref="NormalColorProperty"/>）</summary>
        public Brush? NormalColor
        {
            get => (Brush?)GetValue(NormalColorProperty);
            set => SetValue(NormalColorProperty, value);
        }

        /// <summary>提示色（见 <see cref="InfoColorProperty"/>）</summary>
        public Brush? InfoColor
        {
            get => (Brush?)GetValue(InfoColorProperty);
            set => SetValue(InfoColorProperty, value);
        }

        /// <summary>警告色（见 <see cref="WarningColorProperty"/>）</summary>
        public Brush? WarningColor
        {
            get => (Brush?)GetValue(WarningColorProperty);
            set => SetValue(WarningColorProperty, value);
        }

        /// <summary>严重色（见 <see cref="CriticalColorProperty"/>）</summary>
        public Brush? CriticalColor
        {
            get => (Brush?)GetValue(CriticalColorProperty);
            set => SetValue(CriticalColorProperty, value);
        }

        /// <summary>空态提示语（见 <see cref="EmptyTextProperty"/>）</summary>
        public string? EmptyText
        {
            get => (string?)GetValue(EmptyTextProperty);
            set => SetValue(EmptyTextProperty, value);
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

        private static void OnBannerInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((AlarmBannerElement)d).RebuildRows();

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
        /// 节拍到了：只在"该闪"时改相位，其余时候一拍都不碰 UI。
        ///
        /// 相位从 <paramref name="elapsed"/>（累计时长）算，而不是"每拍取反"：
        /// 漏拍、卡顿、窗口被拖动时，取反会越走越偏，而按累计时长算永远落在正确的半周期上——
        /// 这正是 <see cref="ScadaBeatSource"/> 发累计时长而不是发"又过了一拍"的理由。
        /// </summary>
        private void OnBeat(TimeSpan elapsed)
        {
            if (!_isFlashing)
                return;

            bool on = (long)(elapsed.TotalMilliseconds / BlinkHalfPeriodMilliseconds) % 2 == 0;

            if (on == _blinkOn)
                return;

            RunOnUi(() =>
            {
                _blinkOn = on;
                SetValue(BlinkOnPropertyKey, on);
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
