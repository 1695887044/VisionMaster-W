using System;
using System.Collections.Generic;
using System.Threading;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 流程定时调度器：把勾选了「定时」调用方式的流程，按 <see cref="FlowModel.TimerIntervalMs"/>
    /// 周期自动跑一遍（单次执行，非调试路径——断点不生效）。
    ///
    /// 时序模型：**单节拍源 + 每拍扫一遍**，不是"每条流程一个计时器"。
    ///  · 一个节拍源好收尾（退出链里一次 Dispose 全停，不会漏掉某个匿名计时器把进程钉住）；
    ///  · 每条流程各自记"上次触发时刻"，因此 20ms 与 10 分钟的间隔用同一套机制表达，
    ///    配错间隔也不会退化成高频空转。
    ///
    /// 节拍从哪来：<see cref="IFlowTickSource"/>（本期新增）——
    /// 默认自动挑最高精度的一个（高精度等待计时器 → 多媒体计时器 → 托管计时器，见
    /// <see cref="FlowTickSourceFactory"/>）。高精度源下拍间隔取 5ms（抖动 ~1ms），
    /// 托管兜底时取 25ms（系统时钟粒度 ~15.6ms，拍再密也没意义）。
    ///
    /// 间隔下限 <see cref="MinIntervalMs"/>：低于它就不是"定时"而是"死循环"——
    /// 引擎一轮要建上下文、跑图、收结果，20ms 已经是"给最短的取流场景留出的极限"；
    /// 再快只会把 CPU 吃光（把 0 当"每 20ms 跑"更是荒唐：0 的语义是"没配间隔"，直接不触发）。
    /// ⚠ 若节拍源降级为托管计时器（启动日志会写明），实际稳定可用的下限约 50ms——
    /// 20ms 的配置在那种环境下会带十几毫秒抖动，属平台能力限制，不是配置错误。
    ///
    /// 错过的拍（流程正在跑）**不补跑**：本轮的语义是"到点了，且目标空闲就跑一次"，
    /// 补跑会让"一小时错过 200 拍"在一瞬间连跑 200 次——现场没有任何场景想要这个。
    /// </summary>
    public sealed class FlowTimerScheduler : IDisposable
    {
        /// <summary>定时间隔下限（毫秒）：低于它的配置按它执行，见类型注释</summary>
        public const int MinIntervalMs = 20;

        /// <summary>高精度源下的拍间隔（毫秒）：比下限更小，留出一拍余量</summary>
        private const int HighPrecisionTickMs = 5;

        /// <summary>托管兜底下的拍间隔（毫秒）：再密也快不过系统时钟粒度</summary>
        private const int ManagedTickMs = 25;

        private readonly IWorkspaceManager _workspace;
        private readonly IRuntimeManager _runtime;
        private readonly IFlowEngine _engine;
        private readonly FlowCompiler _compiler;
        private readonly ILogService _log;

        /// <summary>测试/断言可注入固定节拍源；null = 由工厂按优先级自动挑</summary>
        private readonly IFlowTickSource _tickSourceOverride;

        /// <summary>每条流程最近一次触发时刻（按流程名；方案切换后旧名字的残留项无害）</summary>
        private readonly Dictionary<string, DateTime> _lastFireUtc = new(StringComparer.Ordinal);

        private IFlowTickSource _tickSource;
        private int _ticking;

        /// <summary>已释放：释放后不再允许 Start（退出链停掉又被人调起 = 进程都退一半了还在触发流程）</summary>
        private bool _disposed;

        public FlowTimerScheduler(
            IWorkspaceManager workspace,
            IRuntimeManager runtime,
            IFlowEngine engine,
            FlowCompiler compiler,
            ILogService log,
            IFlowTickSource tickSource = null)
        {
            _workspace = workspace;
            _runtime = runtime;
            _engine = engine;
            _compiler = compiler;
            _log = log;
            _tickSourceOverride = tickSource;
        }

        /// <summary>当前生效的节拍源（启动后非空；供日志与断言查看"到底用的哪个时钟"）</summary>
        public IFlowTickSource TickSource => _tickSource;

        public void Start()
        {
            if (_tickSource != null || _disposed) return;

            // 方案切换时清空"上次触发时刻"账本：账本以流程名为键，跨方案残留会让
            // 同名流程继承上一份方案的上次时刻（改名后则被当成"从没跑过"下一拍立即补触发）
            if (_workspace is System.ComponentModel.INotifyPropertyChanged inpc)
                inpc.PropertyChanged += OnWorkspacePropertyChanged;

            _tickSource = _tickSourceOverride ?? FlowTickSourceFactory.Create(_log);
            try
            {
                _tickSource.Start(_tickSource.IsHighPrecision ? HighPrecisionTickMs : ManagedTickMs, SafeTick);
            }
            catch (Exception ex)
            {
                // 兜底源都起不来（几乎不可能）：记清楚原因，调度停摆但不拖垮宿主
                _log?.Error($"[定时] 节拍源启动失败，本次按无定时调度运行：{ex.Message}");
                _tickSource.Dispose();
                _tickSource = null;
            }
        }

        public void Dispose()
        {
            _disposed = true;

            if (_workspace is System.ComponentModel.INotifyPropertyChanged inpc)
                inpc.PropertyChanged -= OnWorkspacePropertyChanged;

            var source = Interlocked.Exchange(ref _tickSource, null);
            source?.Dispose();
        }

        private void OnWorkspacePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IWorkspaceManager.CurrentSolution))
                _lastFireUtc.Clear();
        }

        /// <summary>计时器回调的护栏版：重入直接丢拍（上一拍可能正卡在编译），异常不逃逸出计时器线程</summary>
        private void SafeTick()
        {
            if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
            try
            {
                TickOnce(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _log?.Warn($"[定时] 调度拍出错（已隔离，不影响流程执行）：{ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _ticking, 0);
            }
        }

        /// <summary>
        /// 一拍调度。带时间戳的这版是**公开**的：检查工程用手动时钟驱动它做确定性断言
        /// （真实现由计时器调 SafeTick 走 <c>DateTime.UtcNow</c>）。
        /// </summary>
        public void TickOnce(DateTime utcNow)
        {
            var flows = _workspace?.CurrentSolution?.Flows;
            if (flows == null) return;

            foreach (var flow in flows)
            {
                if (flow == null) continue;

                var lastFire = _lastFireUtc.TryGetValue(flow.FlowName, out var t) ? t : DateTime.MinValue;
                if (!ShouldFire(flow, lastFire, utcNow)) continue;

                // 先记时刻再跑：一轮要几百毫秒到几秒，不先记会在下一拍被当成"从没跑过"重复触发
                _lastFireUtc[flow.FlowName] = utcNow;

                FlowAutoRunner.RunOnce(_workspace, _runtime, _engine, _compiler, _log, flow, "定时");
            }
        }

        /// <summary>间隔取值：配置值夹到下限之上（见 <see cref="MinIntervalMs"/>）</summary>
        public static int ResolveIntervalMs(int configuredMs)
            => configuredMs < MinIntervalMs ? MinIntervalMs : configuredMs;

        /// <summary>
        /// 纯判定（可断言）：这条流程在这一拍该不该触发。
        /// 只判"该不该"，不判"能不能跑"（在跑/编译失败由执行段处理并记日志）。
        /// </summary>
        public static bool ShouldFire(FlowModel flow, DateTime lastFireUtc, DateTime nowUtc)
        {
            if (flow == null || !flow.IsEnabled) return false;
            if ((flow.InvokeType & FlowInvokeType.Timer) == 0) return false;
            if (flow.TimerIntervalMs <= 0) return false;   // 0 = 没配间隔，不是"每 100ms"
            if (flow.StepsEncrypted) return false;

            return nowUtc - lastFireUtc >= TimeSpan.FromMilliseconds(ResolveIntervalMs(flow.TimerIntervalMs));
        }
    }
}
