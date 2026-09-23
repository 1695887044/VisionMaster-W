using System;
using System.Diagnostics;
using System.Windows.Threading;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 全画面统一节拍源：一次运行里<b>只有一个</b>定时器，所有"随时间流逝才会发生"的运行态行为
    /// 都挂到它上面（报警引擎的 <c>Tick</c>、报警灯闪烁、未来的走马灯/趋势刷新）。
    ///
    /// 为什么必须"全画面统一"而不是各图元各养一个定时器
    /// ---------
    /// 一屏上挂十几个报警灯是常态。若每个灯自己起一个 <see cref="DispatcherTimer"/>：
    /// ① 它们各自的起表时刻不同，闪烁相位就不同——看上去不是"报警在闪"，而是"画面卡了/花了"；
    /// ② 定时器数量随图元数线性增长，每个都往 UI 线程的消息队列里塞一条，图元一多就把渲染挤掉。
    /// 统一成一个之后，闪烁相位天然对齐（都读同一份 <see cref="Elapsed"/>），
    /// 而且"多久跳一拍"变成一个可以调的全局值。
    ///
    /// 为什么把"累计时长"一起发出去，而不是只发一个"拍到了"的信号
    /// ---------
    /// 闪烁要的是相位：若消费者各自累加"我收了几拍"，只要漏一拍（UI 忙、窗口最小化后恢复）
    /// 相位就永久错开，而错开的表现又是"画面花了"。发绝对时长则任何消费者都能自己算出
    /// 此刻该亮还是该灭，漏拍只会让动画跳一下，不会让相位漂移。
    ///
    /// 为什么不放在领域层
    /// ---------
    /// <see cref="ScadaAlarmEngine"/> 的注释把这条边界写死了：领域层不自己起定时器，
    /// 否则"节拍多快""跑在哪个线程"这两条宿主决策就写进模型里，断言里也没法一口气把时间推过去。
    /// 本类属于<b>宿主侧</b>（WPF），它替宿主养那唯一的定时器。
    ///
    /// 为什么优先级用 <see cref="DispatcherPriority.Normal"/>（而时钟图元用 Background）
    /// ---------
    /// 时钟晚几毫秒跳没人看得出来，但报警的延时判定、通信断线的判定都以节拍周期为分辨率——
    /// 用 <c>Background</c> 的话，操作员按住鼠标拖图元时队列里会一直有输入消息排在我们前面，
    /// 报警就会跟着"卡顿"，而这恰恰是最不能卡的一条链。<c>Normal</c> 仍然低于输入优先级，
    /// 不会抢操作员的手感，只是不会被人为地一直往后排。
    /// </summary>
    public sealed class ScadaBeatSource
    {
        /// <summary>默认节拍周期（毫秒）。与报警引擎建议的 100~500ms 取中间值</summary>
        public const int DefaultIntervalMilliseconds = 200;

        private readonly DispatcherTimer _timer;

        /// <summary>从本次起表到现在的累计时长。它就是"相位"的唯一来源（见类注释）</summary>
        private readonly Stopwatch _watch = new();

        private readonly Action<ScadaDiagnosticLevel, string>? _onError;

        private bool _running;

        /// <param name="dispatcher">节拍跑在哪个 UI 线程上（正常就是运行窗口的 Dispatcher）</param>
        /// <param name="interval">节拍周期；null = <see cref="DefaultIntervalMilliseconds"/></param>
        /// <param name="onError">某一拍的订阅者抛异常时的上报口。
        /// 传了它，坏掉一个订阅者不会连带打死其余订阅者，也不会静默（见 <see cref="OnTick"/>）</param>
        public ScadaBeatSource(
            Dispatcher dispatcher,
            TimeSpan? interval = null,
            Action<ScadaDiagnosticLevel, string>? onError = null)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));

            _onError = onError;
            _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
            {
                Interval = Normalize(interval),
            };
            _timer.Tick += OnTick;
        }

        /// <summary>每一拍。参数 = 从 <see cref="Start"/> 到这一拍的累计时长（相位用）</summary>
        public event Action<TimeSpan>? Beat;

        /// <summary>节拍周期。运行中改会立即生效，且<b>不</b>重置相位（累计时长照走）</summary>
        public TimeSpan Interval
        {
            get => _timer.Interval;
            set => _timer.Interval = Normalize(value);
        }

        /// <summary>是否在跳</summary>
        public bool IsRunning => _running;

        /// <summary>本次起表以来的累计时长。停表后保留最后一次的值，不归零</summary>
        public TimeSpan Elapsed => _watch.Elapsed;

        /// <summary>起表。已在跳则空操作（<b>不</b>重置相位——重复 Start 不该让闪烁回到起点）</summary>
        public void Start()
        {
            if (_running)
                return;

            _running = true;
            _watch.Restart();
            _timer.Start();
        }

        /// <summary>停表。可重复调用；停完再 <see cref="Start"/> 会重新从零计相位</summary>
        public void Stop()
        {
            _running = false;
            _timer.Stop();
            _watch.Stop();
        }

        /// <summary>
        /// 跳一拍：算出相位，逐个通知。
        ///
        /// 为什么要逐个 try/catch：<see cref="DispatcherTimer"/> 的 Tick 里漏出异常会直接冒到
        /// 调度循环上——那是进程级崩溃。报警链上"一个订阅者写坏了"的代价不该是整机停摆，
        /// 但也不能静默吞掉（报警系统悄悄不工作了，比崩掉更危险），所以收住之后<b>必须</b>上报。
        /// 上报口没给（断言等场景）时仍然收住：让断言看到"这一拍没生效"而不是整个进程挂掉。
        /// </summary>
        private void OnTick(object? sender, EventArgs e)
        {
            var handler = Beat;
            if (handler == null)
                return;

            var elapsed = _watch.Elapsed;

            foreach (Action<TimeSpan> subscriber in handler.GetInvocationList())
            {
                try
                {
                    subscriber(elapsed);
                }
                catch (Exception ex)
                {
                    _onError?.Invoke(
                        ScadaDiagnosticLevel.Error,
                        $"[节拍] 一个节拍订阅者抛出异常，已跳过（其余订阅者不受影响）：{ex.Message}");
                }
            }
        }

        /// <summary>周期下限 10ms：再密就是拿 UI 线程烧 CPU，而报警的延时分辨率也不会因此更准</summary>
        private static TimeSpan Normalize(TimeSpan? interval)
        {
            if (interval == null)
                return TimeSpan.FromMilliseconds(DefaultIntervalMilliseconds);

            var value = interval.Value;
            return value < TimeSpan.FromMilliseconds(10)
                ? TimeSpan.FromMilliseconds(10)
                : value;
        }
    }
}
