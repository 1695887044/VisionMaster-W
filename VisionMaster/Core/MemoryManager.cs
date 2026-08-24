using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualBasic.Logging;

namespace VisionMaster.Core
{
    /// <summary>
    /// 跨平台内存管理器 — 定时巡检 + 触发式分级清理
    /// <list type="bullet">
    ///   <item><b>SoftTrim</b>：GC + LOH 压缩，只回收无引用对象，不影响活跃页面</item>
    ///   <item><b>HardTrim</b>：GC + LOH + 平台级内存释放，释放所有非活跃页面</item>
    /// </list>
    /// 智能切档逻辑：空闲→HardTrim，忙碌→SoftTrim，低于阈值→跳过
    /// </summary>
    public sealed class MemoryManager : IDisposable
    {
        #region 单例实现（推荐使用依赖注入替代）
        private static readonly Lazy<MemoryManager> _instance = new(() => new MemoryManager());
        public static MemoryManager Instance => _instance.Value;
        #endregion

        #region 私有字段
        private Timer _timer;
        private int _thresholdMB = 100;
        private int _intervalSec = 30;
        private long _lastTrimTicks;
        private long _isExecuting; // 0=空闲, 1=执行中
        private bool _disposed;
        private Process _currentProcess;

        // 可配置常量
        private long _debounceIntervalTicks = 5 * TimeSpan.TicksPerSecond; // 5秒防抖
        #endregion

        #region 公共配置属性（运行时可动态修改）
        /// <summary>
        /// 内存阈值（MB），超过此值才会触发清理
        /// </summary>
        public int ThresholdMB
        {
            get => _thresholdMB;
            set => _thresholdMB = Math.Max(50, value); // 最小50MB
        }

        /// <summary>
        /// 巡检间隔（秒）
        /// </summary>
        public int IntervalSec
        {
            get => _intervalSec;
            set
            {
                _intervalSec = Math.Max(10, value); // 最小10秒
                if (_timer != null)
                {
                    _timer.Change(
                        TimeSpan.FromSeconds(_intervalSec),
                        TimeSpan.FromSeconds(_intervalSec)
                    );
                }
            }
        }

        /// <summary>
        /// 防抖间隔（秒），防止高频触发清理
        /// </summary>
        public int DebounceIntervalSec
        {
            get => (int)(_debounceIntervalTicks / TimeSpan.TicksPerSecond);
            set => _debounceIntervalTicks = Math.Max(1, value) * TimeSpan.TicksPerSecond;
        }

        /// <summary>
        /// 业务层设置：true 表示正在执行高优先级操作（拍照/算法）
        /// </summary>
        public volatile bool IsBusy;

        /// <summary>
        /// 是否启用定时巡检
        /// </summary>
        public bool IsRunning => _timer != null;
        #endregion

        #region 事件
        /// <summary>
        /// 内存清理完成事件
        /// </summary>
        public event EventHandler<MemoryTrimEventArgs> TrimCompleted;
        #endregion

        #region 平台 P/Invoke
        /// <summary>Windows：缩减进程工作集</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(
            nint process,
            nint minSize,
            nint maxSize
        );

        /// <summary>Linux：释放 glibc 空闲内存回操作系统</summary>
        [DllImport("libc", EntryPoint = "malloc_trim", SetLastError = true)]
        private static extern int MallocTrim(int pad);
        #endregion

        #region 构造函数与析构函数
        private MemoryManager()
        {
            _currentProcess = Process.GetCurrentProcess();
        }

        ~MemoryManager()
        {
            Dispose(false);
        }
        #endregion

        #region 公共 API
        /// <summary>
        /// 启动定时巡检
        /// </summary>
        /// <param name="thresholdMB">内存阈值（MB），默认100</param>
        /// <param name="intervalSec">巡检间隔（秒），默认30</param>
        public void Start(int thresholdMB = 100, int intervalSec = 30)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MemoryManager));

            lock (this)
            {
                ThresholdMB = thresholdMB;
                IntervalSec = intervalSec;

                _timer?.Dispose();
                _timer = new Timer(
                    OnTimerTick,
                    null,
                    TimeSpan.FromSeconds(_intervalSec),
                    TimeSpan.FromSeconds(_intervalSec)
                );

                // Log.Info($"内存管理器启动: 阈值={_thresholdMB}MB, 间隔={_intervalSec}s");
            }
        }

        /// <summary>
        /// 停止定时巡检
        /// </summary>
        public void Stop()
        {
            if (_disposed)
                return;

            lock (this)
            {
                _timer?.Dispose();
                _timer = null;
                // Log.Info("内存管理器已停止");
            }
        }

        /// <summary>
        /// 轻量清理：只回收无引用的 .NET 对象，不影响活跃页面
        /// </summary>
        /// <returns>是否成功执行清理</returns>
        public bool SoftTrim()
        {
            if (
                _disposed
                || !Debounce()
                || Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0
            )
                return false;

            try
            {
                var before = GetPrivateMemoryMB();
                PerformGarbageCollection();
                var after = GetPrivateMemoryMB();
                var freed = before - after;

                // Log.Debug($"SoftTrim: {before}MB → {after}MB (释放 {freed}MB)");
                OnTrimCompleted(TrimType.Soft, before, after, freed);

                return true;
            }
            catch (Exception ex)
            {
                // Log.Error($"SoftTrim 执行失败: {ex.Message}", ex);
                return false;
            }
            finally
            {
                Interlocked.Exchange(ref _isExecuting, 0);
            }
        }

        /// <summary>
        /// 激进清理：GC + 平台级内存释放，释放所有非活跃页面
        /// </summary>
        /// <returns>是否成功执行清理</returns>
        public bool HardTrim()
        {
            if (
                _disposed
                || !Debounce()
                || Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0
            )
                return false;

            try
            {
                var before = GetPrivateMemoryMB();
                PerformGarbageCollection();
                PlatformTrim();
                var after = GetPrivateMemoryMB();
                var freed = before - after;

                // Log.Debug($"HardTrim: {before}MB → {after}MB (释放 {freed}MB)");
                OnTrimCompleted(TrimType.Hard, before, after, freed);

                return true;
            }
            catch (Exception ex)
            {
                // Log.Error($"HardTrim 执行失败: {ex.Message}", ex);
                return false;
            }
            finally
            {
                Interlocked.Exchange(ref _isExecuting, 0);
            }
        }

        /// <summary>
        /// 获取当前进程私有内存使用量（MB）
        /// </summary>
        public long GetPrivateMemoryMB()
        {
            if (_disposed)
                return 0;

            try
            {
                _currentProcess.Refresh();
                return _currentProcess.PrivateMemorySize64 / 1024 / 1024;
            }
            catch
            {
                // 如果Process对象失效，重新创建
                _currentProcess.Dispose();
                _currentProcess = Process.GetCurrentProcess();
                return _currentProcess.PrivateMemorySize64 / 1024 / 1024;
            }
        }
        #endregion

        #region 内部实现
        private void OnTimerTick(object state)
        {
            if (_disposed || Interlocked.Read(ref _isExecuting) == 1)
                return;

            try
            {
                var currentMB = GetPrivateMemoryMB();
                if (currentMB < _thresholdMB)
                {
                    //Log.Debug($"巡检: 当前内存 {currentMB}MB < 阈值 {_thresholdMB}MB，跳过清理");
                    return;
                }

                if (IsBusy)
                {
                    //Log.Debug($"巡检: 当前内存 {currentMB}MB > 阈值 {_thresholdMB}MB，系统忙碌 → SoftTrim");
                    SoftTrim();
                }
                else
                {
                    //Log.Debug($"巡检: 当前内存 {currentMB}MB > 阈值 {_thresholdMB}MB，系统空闲 → HardTrim");
                    HardTrim();
                }
            }
            catch (Exception ex)
            {
                // 绝对不能让Timer回调抛出异常，否则会导致进程崩溃
                // Log.Error($"内存巡检异常: {ex.Message}", ex);
            }
        }

        private void PerformGarbageCollection()
        {
            // 压缩大对象堆
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

            // 强制全代回收，阻塞直到完成
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);

            // 等待所有终结器执行完毕
            GC.WaitForPendingFinalizers();

            // 再次回收终结器队列中释放的对象
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
        }

        private void PlatformTrim()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows：将工作集缩减到最小，释放不活跃页面
                    if (!SetProcessWorkingSetSize(_currentProcess.Handle, -1, -1))
                    {
                        var errorCode = Marshal.GetLastWin32Error();
                        //Log.Debug($"Windows 工作集缩减失败，错误码: {errorCode}");
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // Linux：调用glibc malloc_trim释放空闲堆内存
                    try
                    {
                        var result = MallocTrim(0);
                        // Log.Debug($"Linux malloc_trim 执行结果: {result}");
                    }
                    catch (DllNotFoundException)
                    {
                        //Log.Debug("当前系统不支持 malloc_trim（非glibc环境），跳过平台级释放");
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // macOS：无直接等价API，通过强制GC和释放未使用的库来间接释放内存
                    // Log.Debug("macOS 平台，执行额外GC以释放内存");
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Log.Debug($"平台级内存释放失败（可忽略）: {ex.Message}");
            }
        }

        private bool Debounce()
        {
            var now = DateTime.UtcNow.Ticks;
            var last = Interlocked.Read(ref _lastTrimTicks);
            if (now - last < _debounceIntervalTicks)
            {
                // Log.Debug($"距离上次清理不足 {DebounceIntervalSec} 秒，跳过");
                return false;
            }
            Interlocked.Exchange(ref _lastTrimTicks, now);
            return true;
        }

        private void OnTrimCompleted(TrimType type, long beforeMB, long afterMB, long freedMB)
        {
            TrimCompleted?.Invoke(this, new MemoryTrimEventArgs(type, beforeMB, afterMB, freedMB));
        }
        #endregion

        #region IDisposable 实现
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // 释放托管资源
                Stop();
                _currentProcess?.Dispose();
            }

            _disposed = true;
        }
        #endregion
    }

    #region 辅助类
    /// <summary>
    /// 清理类型
    /// </summary>
    public enum TrimType
    {
        Soft,
        Hard,
    }

    /// <summary>
    /// 内存清理事件参数
    /// </summary>
    public class MemoryTrimEventArgs : EventArgs
    {
        public TrimType Type { get; }
        public long BeforeMB { get; }
        public long AfterMB { get; }
        public long FreedMB { get; }
        public DateTime Timestamp { get; } = DateTime.UtcNow;

        public MemoryTrimEventArgs(TrimType type, long beforeMB, long afterMB, long freedMB)
        {
            Type = type;
            BeforeMB = beforeMB;
            AfterMB = afterMB;
            FreedMB = freedMB;
        }
    }
    #endregion
}
