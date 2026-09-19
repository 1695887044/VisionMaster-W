using System;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 时钟图元（TypeKey = <c>Hmi.Clock</c>）：走时的日期/时间显示。
    ///
    /// 它和别的图元最大的不同是<b>值的来源在软件自己身上</b>：其它图元的输入是变量或用户配置，
    /// 时钟的输入是系统时间，所以它自己养一个节拍源（见下）。
    ///
    /// <b>为什么节拍要"对齐整点"而不是简单 Interval = 1 秒</b>
    /// ---------
    /// DispatcherTimer 的间隔是"距上一次触发"计的，不是"距整秒"。若在 10:00:00.480 起表、
    /// 之后每秒一跳，秒数会在每秒钟的 .480 处翻——看上去永远比系统时间慢半拍，
    /// 现场对表时会被当成"画面时间不准"。所以每次跳完都重算"距下一个整拍还差多久"再设回去，
    /// 让跳动落在整秒（或整 0.1 秒，取决于间隔）上。
    ///
    /// <b>为什么节拍绑在 Loaded/Unloaded 上，而不是构造函数里起表</b>
    /// ---------
    /// 与基类"只在控件挂载时订阅模型"是同一条纪律：没挂上可视树的实例（被关掉的画面、
    /// 设计期缓存下来的控件）不该还在后台空转。Unloaded 停表，Loaded 重新起表。
    ///
    /// <b>为什么不做"每灯一个定时器"式的共享节拍源</b>
    /// ---------
    /// 时钟一般一屏一两个，各自 1 Hz 的代价可以忽略；真正需要共享节拍的是"一屏几十个报警灯闪烁"，
    /// 那是 S11 报警系统要解决的事（那里会有一个全画面统一节拍源）。此处不为将来的场景提前抽象。
    ///
    /// 显示串用<b>当前区域设置</b>而不是不变文化：这条串不落盘、不参与 .vms 交换，
    /// 而格式串里可能带 dddd（星期）/MMMM（月份）这类与文化强相关的记号——
    /// 中文界面上应该出现"星期五"，而不是"Friday"。数值类图元的显示串则相反（见 ProgressBarElement）。
    /// </summary>
    public class ClockElement : ScadaElementBase
    {
        /// <summary>节拍下限（秒）：再密就是拿 UI 线程烧 CPU 了，而毫秒位也不会因此更准</summary>
        private const double MinIntervalSeconds = 0.1;

        /// <summary>节拍上限（秒）</summary>
        private const double MaxIntervalSeconds = 60;

        /// <summary>时间格式串（标准 .NET 日期时间格式）</summary>
        public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
            nameof(Format), typeof(string), typeof(ClockElement),
            new FrameworkPropertyMetadata("yyyy-MM-dd HH:mm:ss", OnFormatChanged));

        /// <summary>刷新节拍（秒）</summary>
        public static readonly DependencyProperty IntervalProperty = DependencyProperty.Register(
            nameof(Interval), typeof(double), typeof(ClockElement),
            new FrameworkPropertyMetadata(1d, OnIntervalChanged));

        private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(DisplayText), typeof(string), typeof(ClockElement),
            new PropertyMetadata(string.Empty));

        /// <summary>当前该显示的时间串（= 系统时间按 Format 格式化，只读）</summary>
        public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;

        private DispatcherTimer? _timer;

        static ClockElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ClockElement),
                new FrameworkPropertyMetadata(typeof(ClockElement)));
        }

        public ClockElement()
        {
            // 先把第一帧填上：断言环境里控件不挂可视树、定时器根本不跳，
            // 若等到 Loaded 才第一次赋值，DisplayText 会一直空着（界面上表现为"时钟是个空框"）。
            UpdateText();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>时间格式串（见 <see cref="FormatProperty"/>）</summary>
        public string? Format
        {
            get => (string?)GetValue(FormatProperty);
            set => SetValue(FormatProperty, value);
        }

        /// <summary>刷新节拍，秒（见 <see cref="IntervalProperty"/>）</summary>
        public double Interval
        {
            get => (double)GetValue(IntervalProperty);
            set => SetValue(IntervalProperty, value);
        }

        /// <summary>当前显示的时间串（只读，见 <see cref="DisplayTextProperty"/>）</summary>
        public string? DisplayText => (string?)GetValue(DisplayTextProperty);

        private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ClockElement)d).UpdateText();

        private static void OnIntervalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var clock = (ClockElement)d;

            // 改节拍要当场生效：不重算的话，用户把 1 秒改成 0.1 秒后得等最多 1 秒才看到变快
            if (clock._timer is { IsEnabled: true } timer)
                timer.Interval = clock.DelayToNextTick();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateText(); // 挂上来的瞬间先对齐一次，别让第一帧是构造时那会儿的时间

            var timer = _timer ??= CreateTimer();
            timer.Interval = DelayToNextTick();
            timer.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => _timer?.Stop();

        private DispatcherTimer CreateTimer()
        {
            // Background 优先级：时钟晚几毫秒跳没人看得出来，但操作员拖画面时卡一下就有感觉了。
            var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            timer.Tick += OnTick;
            return timer;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            UpdateText();

            // 每次跳完重算下一次的间隔，把跳动重新对回整拍上（见类注释）
            if (_timer is { } timer)
                timer.Interval = DelayToNextTick();
        }

        private void UpdateText() => SetValue(DisplayTextPropertyKey, FormatNow());

        private string FormatNow()
        {
            var now = DateTime.Now;

            // Format 为 null / 空串时 DateTime.ToString 会走通用格式（"G"），不会抛——不必特判。
            // 真正会抛的是写错的格式串（如 "yyyy-MM-dd HH:mm:ss" 里混进未转义的字母），
            // 那不该让整页渲染中断，退回一个一定看得懂的样子。
            try
            {
                return now.ToString(Format, CultureInfo.CurrentCulture);
            }
            catch (FormatException)
            {
                return now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
            }
        }

        /// <summary>距下一个整拍还有多久（恒为正，且不超过一个节拍）</summary>
        private TimeSpan DelayToNextTick()
        {
            var seconds = IntervalSeconds();
            var sinceMidnight = DateTime.Now.TimeOfDay.TotalSeconds;

            // 用 Floor 而不是 %：% 对负数与浮点边界的语义不如显式取整来得清楚
            var remainder = sinceMidnight - Math.Floor(sinceMidnight / seconds) * seconds;
            var delay = seconds - remainder;

            // remainder 恰好为 0 时 delay = seconds（正合"再等一整拍"）；浮点误差导致的极小值也一并兜住
            if (!double.IsFinite(delay) || delay < MinIntervalSeconds)
                delay = seconds;

            return TimeSpan.FromSeconds(delay);
        }

        /// <summary>把配置的节拍收敛到可用区间：NaN/负数/过大都不会变成一个失控的定时器</summary>
        private double IntervalSeconds()
        {
            var seconds = Interval;
            if (!double.IsFinite(seconds) || seconds < MinIntervalSeconds)
                return MinIntervalSeconds;

            return seconds > MaxIntervalSeconds ? MaxIntervalSeconds : seconds;
        }
    }
}
