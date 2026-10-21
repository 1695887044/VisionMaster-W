using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace VM.Charts
{
    /// <summary>
    /// 全局渲染调度器:所有 ChartView 共用一个 DispatcherTimer,
    /// 每帧逐个检查脏标记。避免"N 个图 N 个唤醒源"造成 CPU 空转;
    /// 持有弱引用,控件释放后自动清除,最后一个图卸载即停表。
    /// </summary>
    internal static class ChartRenderScheduler
    {
        private const int IntervalMs = 30;

        private static readonly object Lock = new object();
        private static readonly List<WeakReference<ChartView>> Views = new List<WeakReference<ChartView>>();
        private static DispatcherTimer _timer;

        public static void Register(ChartView view)
        {
            lock (Lock)
            {
                foreach (var wr in Views)
                {
                    ChartView existing;
                    if (wr.TryGetTarget(out existing) && ReferenceEquals(existing, view)) return;
                }
                Views.Add(new WeakReference<ChartView>(view));
                EnsureTimer();
            }
        }

        public static void Unregister(ChartView view)
        {
            lock (Lock)
            {
                for (var i = Views.Count - 1; i >= 0; i--)
                {
                    ChartView existing;
                    if (!Views[i].TryGetTarget(out existing) || ReferenceEquals(existing, view))
                        Views.RemoveAt(i);
                }
                if (Views.Count == 0 && _timer != null)
                {
                    _timer.Stop();
                    _timer = null;
                }
            }
        }

        private static void EnsureTimer()
        {
            if (_timer != null) return;
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(IntervalMs)
            };
            _timer.Tick += Tick;
            _timer.Start();
        }

        private static void Tick(object sender, EventArgs e)
        {
            ChartView[] snapshot;
            lock (Lock)
            {
                // 清掉已释放的弱引用
                for (var i = Views.Count - 1; i >= 0; i--)
                {
                    ChartView existing;
                    if (!Views[i].TryGetTarget(out existing)) Views.RemoveAt(i);
                }
                snapshot = new ChartView[Views.Count];
                for (var i = 0; i < Views.Count; i++)
                {
                    ChartView existing;
                    Views[i].TryGetTarget(out existing);
                    snapshot[i] = existing;
                }
            }

            foreach (var view in snapshot)
                if (view != null)
                    view.TryRender();
        }
    }
}
