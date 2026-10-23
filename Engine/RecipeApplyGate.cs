using System;
using System.Diagnostics;
using System.Threading;

namespace VisionMaster.Services
{
    /// <summary>
    /// 配方应用与流程启动之间的互斥闸。
    ///
    /// 语义（不是普通锁）：
    ///  · 流程启动先 <see cref="TryBeginStart"/> 登记“在途启动”，拿到租约后做 ensure/start；
    ///  · 配方应用 <see cref="TryBeginApply"/> 先封门，再等所有在途启动归零（有界等待）；
    ///  · 封门期间新的启动一律快速失败，不排队、不阻塞 UI。
    ///
    /// 为什么不用 <see cref="ReaderWriterLockSlim"/>：run 的 finally 在 await 之后由线程池执行，
    /// 读写锁要求同线程释放；而且写锁阻塞会把 UI 冻住整轮连续运行。
    ///
    /// 覆盖范围：<see cref="FlowSessionFactory.TryEnsureSession"/>（编译/注册的唯一收口点）
    /// 与 <see cref="FlowEngineService.TryOccupySession"/>（引擎原子启动点）。
    /// 宿主侧的 6 个启动入口最终都经过这两处。
    /// </summary>
    public static class RecipeApplyGate
    {
        private static readonly object _gate = new();
        private static int _inFlightStarts;
        private static bool _sealed;

        /// <summary>当前是否处于配方应用窗口。</summary>
        public static bool IsApplying
        {
            get { lock (_gate) return _sealed; }
        }

        /// <summary>当前在途启动数（诊断用）。</summary>
        public static int InFlightStarts
        {
            get { lock (_gate) return _inFlightStarts; }
        }

        /// <summary>
        /// 流程启动登记。返回 false = 正在切换配方，调用方应直接放弃本次启动。
        /// </summary>
        public static bool TryBeginStart(out IDisposable? lease)
        {
            lock (_gate)
            {
                if (_sealed)
                {
                    lease = null;
                    return false;
                }

                _inFlightStarts++;
                lease = new Lease(ReleaseStart);
                return true;
            }
        }

        /// <summary>
        /// 进入配方应用窗口：先封门，再等在途启动归零。
        /// 超时则撤封并返回 false（调用方应拒绝本次应用，零写入）。
        /// </summary>
        public static bool TryBeginApply(int timeoutMs, out IDisposable? lease)
        {
            lease = null;
            lock (_gate)
            {
                if (_sealed) return false;
                _sealed = true;
            }

            var sw = Stopwatch.StartNew();
            while (true)
            {
                lock (_gate)
                {
                    if (_inFlightStarts == 0)
                    {
                        lease = new Lease(ReleaseApply);
                        return true;
                    }
                }

                if (sw.ElapsedMilliseconds >= Math.Max(0, timeoutMs))
                {
                    lock (_gate) _sealed = false;
                    return false;
                }

                Thread.Sleep(10);
            }
        }

        /// <summary>测试用：强制复位（只在断言宿主里调用；生产代码不得调用）。</summary>
        public static void ResetForTests()
        {
            lock (_gate)
            {
                _inFlightStarts = 0;
                _sealed = false;
            }
        }

        private static void ReleaseStart()
        {
            lock (_gate)
            {
                if (_inFlightStarts > 0) _inFlightStarts--;
            }
        }

        private static void ReleaseApply()
        {
            lock (_gate) _sealed = false;
        }

        private sealed class Lease : IDisposable
        {
            private Action? _release;

            public Lease(Action release) => _release = release;

            public void Dispose()
            {
                var release = Interlocked.Exchange(ref _release, null);
                release?.Invoke();
            }
        }
    }
}
