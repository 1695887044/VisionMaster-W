using System;
using System.Runtime.InteropServices;
using System.Threading;
using Core.Interfaces;

namespace VisionMaster.Services
{
    /// <summary>
    /// 定时调度的**节拍源**：把"多久敲一拍、用什么时钟敲"从调度逻辑里独立出来。
    ///
    /// 为什么要把时钟源抽出来（这一期用户提问的直接动因："定时器能不能用 Windows 的媒体时钟"）：
    ///  · 精度是平台能力，不是调度逻辑：同一套"该不该触发"的判定，配 1ms 的时钟与配 15.6ms 的时钟，
    ///    现场表现天差地别；把时钟换掉不该动触发语义一行；
    ///  · 可断言：调度逻辑（ShouldFire / TickOnce）用手动时钟做确定性断言，
    ///    "时钟本身准不准"另用真实短跑断言，两件事分开证。
    ///
    /// 候选实现（按推荐顺序，见 <see cref="FlowTickSourceFactory"/>）：
    ///  ① <see cref="WaitableTimerTickSource"/>：kernel32 高精度等待计时器
    ///     （CreateWaitableTimerEx + CREATE_WAITABLE_TIMER_HIGH_RESOLUTION，Win10 1803+，抖动 ~1ms 内，
    ///     **不抬系统时钟分辨率**、无全局副作用）；
    ///  ② <see cref="WinMmMediaClockTickSource"/>：winmm 多媒体计时器（timeBeginPeriod(1) + timeSetEvent）。
    ///     即用户问的"媒体时钟"：能用，但微软已把 Multimedia Timers 标为 legacy
    ///     （"superseded by Multimedia Class Scheduler Service / strongly recommends new code use MMCS"），
    ///     且 timeBeginPeriod 会**全局抬高系统时钟分辨率**（多耗电，笔记本现场要留意）——故排在②；
    ///  ③ <see cref="ManagedTickSource"/>：System.Threading.Timer（系统的 ~15.6ms 粒度）。
    ///     永远可用，作为最终兜底；代价是节拍抖动可达十几毫秒。
    /// </summary>
    public interface IFlowTickSource : IDisposable
    {
        /// <summary>人话名称（启动日志与断言用；要能一眼看出"现在到底用的是哪个时钟"）</summary>
        string Name { get; }

        /// <summary>是否高精度源（等待计时器 / 多媒体计时器 = true；托管计时器 = false）</summary>
        bool IsHighPrecision { get; }

        /// <summary>
        /// 开始按 <paramref name="periodMs"/> 周期回调。回调跑在**源自己的线程**上，
        /// 回调内的异常由调用方（调度器）隔离——不许漏进时钟线程。
        /// </summary>
        void Start(int periodMs, Action onTick);
    }

    /// <summary>
    /// 高精度等待计时器（推荐）：kernel32 <c>CreateWaitableTimerEx</c> +
    /// <c>CREATE_WAITABLE_TIMER_HIGH_RESOLUTION</c>（Win10 1803+）。
    ///
    /// 相比多媒体计时器：不调 timeBeginPeriod、不改系统时钟分辨率（笔记本/工控机的耗电更友好），
    /// 精度相当（亚毫秒~1ms 量级），而且是微软当前推荐的做法（MMCS 之外最轻的选择）。
    /// </summary>
    public sealed class WaitableTimerTickSource : IFlowTickSource
    {
        private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
        private const uint TIMER_ALL_ACCESS = 0x001F0003;
        private const uint INFINITE = 0xFFFFFFFF;
        private const uint WAIT_OBJECT_0 = 0x00000000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWaitableTimerEx(
            IntPtr lpTimerAttributes, string lpTimerName, uint dwFlags, uint dwDesiredAccess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetWaitableTimer(
            IntPtr hTimer, ref long pDueTime, int lPeriod,
            IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, bool fResume);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForMultipleObjects(
            uint nCount, IntPtr[] lpHandles, bool bWaitAll, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelWaitableTimer(IntPtr hTimer);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private IntPtr _timerHandle = IntPtr.Zero;
        private ManualResetEvent _stopEvent;
        private Thread _thread;

        public string Name => "高精度等待计时器（kernel32 CreateWaitableTimerEx，不抬系统时钟分辨率）";

        public bool IsHighPrecision => true;

        public void Start(int periodMs, Action onTick)
        {
            if (_timerHandle != IntPtr.Zero) return;

            _timerHandle = CreateWaitableTimerEx(
                IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);

            // 失败即抛：由工厂决定降级到哪个源（旧系统不认识高精度标志时会走到这里）
            if (_timerHandle == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"CreateWaitableTimerEx 失败（错误码 {Marshal.GetLastWin32Error()}）：本机内核不支持高精度等待计时器");

            // 相对到期时间：负数 = 相对，单位 100ns；周期用毫秒参数（lPeriod）
            long dueTime = -(long)Math.Max(1, periodMs) * 10_000L;
            if (!SetWaitableTimer(_timerHandle, ref dueTime, Math.Max(1, periodMs), IntPtr.Zero, IntPtr.Zero, false))
            {
                var code = Marshal.GetLastWin32Error();
                CloseHandle(_timerHandle);
                _timerHandle = IntPtr.Zero;
                throw new InvalidOperationException($"SetWaitableTimer 失败（错误码 {code}）");
            }

            _stopEvent = new ManualResetEvent(false);
            _thread = new Thread(() => Loop(onTick))
            {
                IsBackground = true,
                Name = "FlowTimerTick",
            };
            _thread.Start();
        }

        private void Loop(Action onTick)
        {
            // 两个句柄一起等：计时器到点（index 0）或停止事件（index 1）。
            // 不用 WaitForSingleObject 单等计时器，是因为"停止"没法把被取消的计时器叫醒（会卡到进程退出）
            var handles = new IntPtr[]
            {
                _timerHandle,
                _stopEvent.SafeWaitHandle.DangerousGetHandle(),
            };

            while (true)
            {
                var result = WaitForMultipleObjects(2, handles, false, INFINITE);
                if (result != WAIT_OBJECT_0) return;   // 停止事件 / WAIT_FAILED：退出线程

                try
                {
                    onTick?.Invoke();
                }
                catch
                {
                    // 时钟线程不许被回调异常打死；调度器内部已有隔离与日志
                }
            }
        }

        public void Dispose()
        {
            if (_timerHandle != IntPtr.Zero)
            {
                CancelWaitableTimer(_timerHandle);
                CloseHandle(_timerHandle);
                _timerHandle = IntPtr.Zero;
            }

            _stopEvent?.Set();
            // 等时钟线程收干净再释放事件句柄：线程还在 WaitForMultipleObjects 上握着它
            _thread?.Join(TimeSpan.FromSeconds(2));
            _stopEvent?.Dispose();
            _stopEvent = null;
            _thread = null;
        }
    }

    /// <summary>
    /// 多媒体计时器（winmm，即用户问的"Windows 媒体时钟"）：
    /// <c>timeBeginPeriod(1)</c> 抬到 1ms 分辨率，<c>timeSetEvent</c> 周期回调。
    ///
    /// ⚠ 两条要如实知道的事：
    ///  ① 微软已把 Multimedia Timers 标为 **legacy**（"superseded by Multimedia Class Scheduler Service…
    ///     strongly recommends new code use MMCS"），故它不是首选实现；
    ///  ② <c>timeBeginPeriod</c> 改的是**系统级**时钟分辨率（不只是本进程）——工控机常年跑会多耗电，
    ///     所以本实现严格成对调用 timeEndPeriod，且只在真的用它时才会抬。
    ///
    /// 它仍然有价值：老系统（Win10 1803 之前）没有高精度等待计时器标志，这时它是唯一能拿到 1ms 节拍的路子。
    /// </summary>
    public sealed class WinMmMediaClockTickSource : IFlowTickSource
    {
        private const uint TIME_PERIODIC = 0x0001;
        private const uint TIME_CALLBACK_FUNCTION = 0x0000;

        private delegate void TimeProc(uint uTimerId, uint uMsg, IntPtr dwUser, IntPtr dw1, IntPtr dw2);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint timeSetEvent(
            uint uDelay, uint uResolution, TimeProc lpTimeProc, IntPtr dwUser, uint fuEvent);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint timeKillEvent(uint uTimerId);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint timeEndPeriod(uint uPeriod);

        /// <summary>必须存成字段：委托被 GC 掉之后再回调就是访问已释放的封送存根</summary>
        private TimeProc _callback;

        private uint _timerId;
        private bool _periodRaised;

        public string Name => "多媒体计时器（winmm timeSetEvent + timeBeginPeriod(1)，1ms 分辨率）";

        public bool IsHighPrecision => true;

        public void Start(int periodMs, Action onTick)
        {
            if (_timerId != 0) return;

            _callback = (_, _, _, _, _) =>
            {
                try
                {
                    onTick?.Invoke();
                }
                catch
                {
                    // 时钟线程不许被回调异常打死（调度器内部已有隔离与日志）
                }
            };

            // 先抬分辨率再起定时器：抬失败不算致命（节拍会退到 ~15.6ms 粒度），尽量把时钟开起来
            _periodRaised = timeBeginPeriod(1) == 0;

            _timerId = timeSetEvent(
                (uint)Math.Max(1, periodMs), 1, _callback, IntPtr.Zero, TIME_PERIODIC | TIME_CALLBACK_FUNCTION);

            if (_timerId == 0)
            {
                if (_periodRaised)
                {
                    timeEndPeriod(1);
                    _periodRaised = false;
                }
                _callback = null;
                throw new InvalidOperationException("winmm timeSetEvent 返回 0（本机不支持多媒体计时器或参数非法）");
            }
        }

        public void Dispose()
        {
            if (_timerId != 0)
            {
                timeKillEvent(_timerId);
                _timerId = 0;
            }

            if (_periodRaised)
            {
                timeEndPeriod(1);
                _periodRaised = false;
            }

            _callback = null;
        }
    }

    /// <summary>托管计时器（兜底）：System.Threading.Timer，节拍粒度受系统时钟 ~15.6ms 限制。</summary>
    public sealed class ManagedTickSource : IFlowTickSource
    {
        private Timer _timer;

        public string Name => "托管计时器（System.Threading.Timer，节拍粒度约 15.6ms）";

        public bool IsHighPrecision => false;

        public void Start(int periodMs, Action onTick)
        {
            if (_timer != null) return;
            _timer = new Timer(_ =>
            {
                try
                {
                    onTick?.Invoke();
                }
                catch
                {
                    // 同前：异常由调度器隔离，时钟线程不许被它打死
                }
            }, null, periodMs, periodMs);
        }

        public void Dispose()
        {
            var timer = Interlocked.Exchange(ref _timer, null);
            timer?.Dispose();
        }
    }

    /// <summary>
    /// 节拍源工厂：按"精度从高到低"依次尝试，全部失败也要给出一个能跑的源（托管兜底）。
    ///
    /// 为什么不做成"配置项让用户选"：现场没人会为了定时器去翻配置文件；能自动挑最好的就自动挑，
    /// 挑不到时**在启动日志里说清楚降级原因**（"为什么节拍不准"是现场常见疑问，日志必须能回答）。
    /// </summary>
    public static class FlowTickSourceFactory
    {
        /// <summary>按优先级创建；返回的源已确定可用（但尚未 Start）</summary>
        public static IFlowTickSource Create(ILogService log)
        {
            IFlowTickSource source;
            string error;

            if (TryCreateWaitableTimer(out source, out error))
            {
                log?.Info($"[定时] 节拍源：{source.Name}");
                return source;
            }
            log?.Warn($"[定时] 高精度等待计时器不可用，降级到多媒体计时器：{error}");

            if (TryCreateMediaClock(out source, out error))
            {
                log?.Info($"[定时] 节拍源：{source.Name}");
                return source;
            }
            log?.Warn($"[定时] 多媒体计时器不可用，降级到托管计时器（节拍抖动可达十几毫秒，定时间隔建议 ≥50ms）：{error}");

            log?.Info($"[定时] 节拍源：{new ManagedTickSource().Name}");
            return new ManagedTickSource();
        }

        /// <summary>只试高精度等待计时器（断言与排障用）</summary>
        public static bool TryCreateWaitableTimer(out IFlowTickSource source, out string error)
        {
            source = null;
            error = null;
            try
            {
                var candidate = new WaitableTimerTickSource();
                // 真起一下再停：只有"能起"的源才算可用（旧系统要到 Create/Set 才知道）
                candidate.Start(1000, () => { });
                candidate.Dispose();
                source = new WaitableTimerTickSource();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>只试多媒体计时器（断言与排障用）</summary>
        public static bool TryCreateMediaClock(out IFlowTickSource source, out string error)
        {
            source = null;
            error = null;
            try
            {
                var candidate = new WinMmMediaClockTickSource();
                candidate.Start(1000, () => { });
                candidate.Dispose();
                source = new WinMmMediaClockTickSource();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
