using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Interfaces.Io
{
    /// <summary>
    /// IO 设备基类：把“单 Worker 串行、轮询、写优先、重连、快照、质量、死区/防抖”固定下来。
    /// 厂商驱动只实现四个 Core 方法：ConnectCore / DisconnectCore / PollCore / WriteCore。
    ///
    /// 契约（必须遵守）：
    /// 1) 四个 Core 方法只在设备 Worker 线程上执行；
    /// 2) Core 方法内不得调用本类的公共 API（WriteAndWait/TryRead 等），否则会自锁；
    /// 3) 厂商 SDK 回调只允许“拷一份 + 入队”，不得重入 Core。
    /// </summary>
    public abstract class IoDeviceBase : IIoDevice
    {
        private sealed class WorkItem
        {
            public Func<object?> Action = null!;
            public TaskCompletionSource<object?> Completion = null!;
            public volatile bool Cancelled;
            public volatile bool Started;
            public string? PointId;
            public long Sequence;
        }

        private readonly ConcurrentDictionary<string, IoPointValue> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, long> _lastWriteTicks = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, (bool Value, long Since)> _debounce = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, long> _groupDueTicks = new(StringComparer.OrdinalIgnoreCase);

        private readonly BlockingCollection<WorkItem> _highPriority;
        private readonly BlockingCollection<WorkItem> _writes;
        private readonly SemaphoreSlim _signal = new(0, 1);
        private readonly CancellationTokenSource _cts = new();
        private readonly Thread _thread;

        private int _state = (int)IoDeviceState.Closed;
        private volatile bool _autoConnect;
        private volatile bool _autoReconnect = true;
        private volatile bool _disposed;
        private volatile bool _disposeConnectionOnExit;
        private long _nextConnectTick;
        private int _reconnectAttempt;
        private long _lastSuccessTicks;
        private long _sequence;
        private long _writeCount;
        private long _rejectedWriteCount;
        private long _faultCount;
        private double _averagePollMs;
        private string _stateDetail = "未连接";

        protected IoDeviceBase(IoDeviceDescriptor descriptor, IoBusDescriptor bus, IIoRuntimeContext context)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Bus = bus ?? throw new ArgumentNullException(nameof(bus));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Descriptor.Params ??= new IoParams();

            int capacity = Math.Max(16, Descriptor.Params.CommandQueueCapacity);
            _highPriority = new BlockingCollection<WorkItem>(capacity);
            _writes = new BlockingCollection<WorkItem>(capacity);

            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = $"IoWorker-{descriptor.DeviceId}"
            };
        }

        public IoDeviceDescriptor Descriptor { get; }
        public IoBusDescriptor Bus { get; }
        public IIoRuntimeContext Context { get; }
        public IoDeviceState State => (IoDeviceState)Volatile.Read(ref _state);
        public string StateDetail { get { lock (_cts) return _stateDetail; } }
        public IoCapabilities Capabilities { get; protected set; } = IoCapabilities.None;

        public DateTime LastSuccessTime
        {
            get
            {
                long ticks = Interlocked.Read(ref _lastSuccessTicks);
                return ticks == 0 ? default : new DateTime(ticks);
            }
        }

        public long WriteCount => Interlocked.Read(ref _writeCount);
        public long RejectedWriteCount => Interlocked.Read(ref _rejectedWriteCount);
        public long FaultCount => Interlocked.Read(ref _faultCount);

        /// <summary>
        /// 实测轮询耗时（毫秒，指数滑动平均；0 = 还没有样本）。
        /// <para>量的是 <see cref="PollCore"/> 一次调用本身的耗时，不含等待周期——
        /// 它回答的是"这条链路读一轮要多久"，健康面板用它判断现场是否接近超时。</para>
        /// </summary>
        public double AveragePollMs => Volatile.Read(ref _averagePollMs);

        public event EventHandler? StateChanged;
        public event EventHandler<IoPointsChangedEventArgs>? PointsChanged;
        public event EventHandler<IoFault>? FaultOccurred;

        #region 子类差异点

        protected abstract bool ConnectCore(out string? error);
        protected abstract void DisconnectCore();
        protected abstract bool PollCore(IReadOnlyList<IoPointDescriptor> points,
                                        IDictionary<string, IoPointValue> values,
                                        out string? error);
        protected abstract bool WriteCore(IReadOnlyList<IoWriteRequest> writes, out string? error);

        /// <summary>单点即时读。默认用 PollCore 读一个点；驱动可覆盖以走更高效的专用读。</summary>
        protected virtual bool ReadPointCore(IoPointDescriptor point, out IoPointValue value, out string? error)
        {
            var values = new Dictionary<string, IoPointValue>(StringComparer.OrdinalIgnoreCase);
            bool ok = PollCore(new[] { point }, values, out error);
            if (ok && values.TryGetValue(point.PointId, out value))
                return true;
            value = IoPointValue.Bad(point.DataType);
            error ??= "点位未返回数据";
            return false;
        }

        #endregion

        #region 公共 API

        public bool Connect()
        {
            ThrowIfDisposed();
            _reconnectAttempt = 0;
            _autoConnect = true;
            Volatile.Write(ref _nextConnectTick, 0);
            Wake();
            return WaitForState(IoDeviceState.Online, Descriptor.Params.ConnectTimeoutMs);
        }

        public void Disconnect()
        {
            if (_disposed) return;
            _autoConnect = false;
            var item = Enqueue(_highPriority, () =>
            {
                try { DisconnectCore(); }
                finally { SetState(IoDeviceState.Closed, "已断开"); }
                return null;
            }, null);
            try { item.Completion.Task.Wait(3000); } catch { /* 断开路径不抛 */ }
        }

        public bool TryRead(string pointId, out IoPointValue value)
        {
            if (_cache.TryGetValue(pointId, out value))
                return value.Quality is IoQuality.Good or IoQuality.Uncertain;
            value = IoPointValue.Bad(IoDataType.Bool);
            return false;
        }

        public IoPointValue ReadNow(string pointId, int timeoutMs, CancellationToken ct, out string? error)
        {
            ThrowIfDisposed();
            if (!TryGetPoint(pointId, out var point))
            {
                error = $"未配置点位：{pointId}";
                return IoPointValue.Bad(IoDataType.Bool);
            }

            var item = Enqueue(_highPriority, () =>
            {
                if (State != IoDeviceState.Online)
                    return (object?)("设备未连接");
                if (!ReadPointCore(point, out var raw, out var readError))
                    return (object?)(readError ?? "读取失败");
                PublishOne(point, raw, DateTime.Now);
                return (object?)null;
            }, pointId);

            try
            {
                if (!item.Completion.Task.Wait(Math.Max(1, timeoutMs), ct))
                {
                    item.Cancelled = true;
                    error = "读取超时";
                    return IoPointValue.Bad(point.DataType);
                }

                if (item.Completion.Task.Result is string message)
                {
                    error = message;
                    return IoPointValue.Bad(point.DataType);
                }

                error = null;
                return _cache.TryGetValue(pointId, out var value) ? value : IoPointValue.Bad(point.DataType);
            }
            catch (OperationCanceledException)
            {
                item.Cancelled = true;
                error = "已取消";
                return IoPointValue.Bad(point.DataType);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return IoPointValue.Bad(point.DataType);
            }
        }

        public IoWriteResult Write(string pointId, IoPointValue value, string? ownerId = null)
        {
            ThrowIfDisposed();
            if (!TryPrepareWrite(pointId, value, out var request, out var error))
                return IoWriteResult.Reject(error!);

            long seq = Interlocked.Increment(ref _sequence);
            var item = Enqueue(_writes, () =>
            {
                if (State != IoDeviceState.Online)
                    return IoWriteOutcome.Rejected;
                bool ok = WriteCore(new[] { request }, out var writeError);
                if (!ok) return IoWriteOutcome.Rejected;
                TouchWrite(pointId, value);
                return IoWriteOutcome.Executed;
            }, pointId);
            item.Sequence = seq;
            return IoWriteResult.Accept(seq, item.Completion.Task);
        }

        public bool WriteAndWait(string pointId, IoPointValue value, int timeoutMs,
                                 CancellationToken ct, out string? error, string? ownerId = null)
        {
            ThrowIfDisposed();
            if (!TryPrepareWrite(pointId, value, out var request, out error))
                return false;

            var item = Enqueue(_writes, () =>
            {
                if (State != IoDeviceState.Online)
                    return (object?)"设备未连接";
                if (!WriteCore(new[] { request }, out var writeError))
                    return (object?)(writeError ?? "写入失败");
                TouchWrite(pointId, value);
                return (object?)null;
            }, pointId);

            try
            {
                if (!item.Completion.Task.Wait(Math.Max(1, timeoutMs), ct))
                {
                    item.Cancelled = true;
                    error = item.Started
                        ? "写入超时，可能已执行，请检查设备状态"
                        : "写入超时，未执行";
                    Interlocked.Increment(ref _rejectedWriteCount);
                    return false;
                }

                if (item.Completion.Task.Result is string message)
                {
                    error = message;
                    Interlocked.Increment(ref _rejectedWriteCount);
                    return false;
                }

                error = null;
                Interlocked.Increment(ref _writeCount);
                return true;
            }
            catch (OperationCanceledException)
            {
                item.Cancelled = true;
                error = "已取消";
                Interlocked.Increment(ref _rejectedWriteCount);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Interlocked.Increment(ref _rejectedWriteCount);
                return false;
            }
        }

        public void ResetCounters()
        {
            Interlocked.Exchange(ref _writeCount, 0);
            Interlocked.Exchange(ref _rejectedWriteCount, 0);
            Interlocked.Exchange(ref _faultCount, 0);
            Volatile.Write(ref _averagePollMs, 0);
        }

        /// <summary>记录一次轮询耗时（指数滑动平均：新样本权重 0.2，抑制单次抖动）。</summary>
        private void RecordPollDuration(double elapsedMs)
        {
            if (elapsedMs < 0 || double.IsNaN(elapsedMs) || double.IsInfinity(elapsedMs)) return;
            double current = Volatile.Read(ref _averagePollMs);
            Volatile.Write(ref _averagePollMs,
                current <= 0 ? elapsedMs : current * 0.8 + elapsedMs * 0.2);
        }

        #endregion

        #region Worker

        public void StartWorker()
        {
            if (_thread.IsAlive) return;
            _thread.Start();
        }

        private void Loop()
        {
            var token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_autoConnect && State != IoDeviceState.Online && Environment.TickCount64 >= Volatile.Read(ref _nextConnectTick))
                        TryConnect();

                    DrainHighPriority();
                    if (State == IoDeviceState.Online)
                    {
                        RunDuePolls();
                        DrainWrites();
                    }

                    _signal.Wait(ComputeWaitMs(), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ReportFault("Loop", ex.GetType().Name, ex.Message);
                    Thread.Sleep(200);
                }
            }

            ReleasePending(_highPriority);
            ReleasePending(_writes);

            if (_disposeConnectionOnExit)
            {
                try { DisconnectCore(); } catch { }
            }
        }

        private void TryConnect()
        {
            SetState(_reconnectAttempt == 0 ? IoDeviceState.Connecting : IoDeviceState.Reconnecting,
                     _reconnectAttempt == 0 ? "正在连接…" : "正在重连…");
            try
            {
                if (ConnectCore(out var error))
                {
                    _reconnectAttempt = 0;
                    Interlocked.Exchange(ref _lastSuccessTicks, DateTime.Now.Ticks);
                    MarkAllGroupsDue();
                    SetState(IoDeviceState.Online, "已连接");
                    return;
                }

                ReportFault("Connect", "ConnectFailed", error ?? "建立连接失败");
                ScheduleReconnect();
            }
            catch (Exception ex)
            {
                ReportFault("Connect", ex.GetType().Name, ex.Message);
                ScheduleReconnect();
            }
        }

        private void ScheduleReconnect()
        {
            if (!_autoReconnect)
            {
                _autoConnect = false;
                SetState(IoDeviceState.Faulted, "自动重连已关闭");
                return;
            }

            _reconnectAttempt++;
            var policy = Bus.Reconnect ?? new IoReconnectPolicy();
            if (policy.MaxAttempts > 0 && _reconnectAttempt >= policy.MaxAttempts)
            {
                _autoConnect = false;
                SetState(IoDeviceState.Faulted, "达到最大重连次数");
                return;
            }

            long backoff = Math.Min(
                (long)Math.Max(100, policy.BaseIntervalMs) << Math.Min(_reconnectAttempt - 1, 16),
                Math.Max(100, policy.MaxIntervalMs));
            SetState(IoDeviceState.Reconnecting, $"将在 {backoff}ms 后重连");
            Volatile.Write(ref _nextConnectTick, Environment.TickCount64 + backoff);
        }

        private void RunDuePolls()
        {
            var groups = Descriptor.Params.PollGroups;
            if (groups == null || groups.Count == 0)
                groups = new List<IoPollGroup> { new() { Name = "default", PeriodMs = 50 } };

            long now = Environment.TickCount64;
            foreach (var group in groups)
            {
                long due = _groupDueTicks.GetOrAdd(group.Name, 0);
                if (now < due) continue;

                var points = ResolveGroupPoints(group);
                if (points.Count == 0)
                {
                    _groupDueTicks[group.Name] = now + Math.Max(10, group.PeriodMs);
                    continue;
                }

                var values = new Dictionary<string, IoPointValue>(StringComparer.OrdinalIgnoreCase);
                long pollStarted = Stopwatch.GetTimestamp();
                try
                {
                    if (PollCore(points, values, out var error))
                    {
                        PublishBatch(points, values);
                        Interlocked.Exchange(ref _lastSuccessTicks, DateTime.Now.Ticks);
                        _groupDueTicks[group.Name] = now + Math.Max(10, group.PeriodMs);
                    }
                    else
                    {
                        MarkUncertain(points);
                        ReportFault("Poll", "PollFailed", error ?? "轮询失败");
                        _groupDueTicks[group.Name] = now + Math.Max(10, group.PeriodMs);
                        // 轮询整体失败按链路故障处理；单点坏地址由驱动返回 true + 缺失点表示。
                        if (State == IoDeviceState.Online)
                        {
                            try { DisconnectCore(); } catch { }
                            ScheduleReconnect();
                        }
                        break;
                    }
                }
                catch (Exception ex)
                {
                    MarkUncertain(points);
                    ReportFault("Poll", ex.GetType().Name, ex.Message);
                    try { DisconnectCore(); } catch { }
                    ScheduleReconnect();
                    break;
                }
                finally
                {
                    // 成功/失败都记：失败那次的耗时正是"超时前等了多久"，对现场诊断更有价值。
                    RecordPollDuration((Stopwatch.GetTimestamp() - pollStarted) * 1000.0 / Stopwatch.Frequency);
                }

                // 段间让出：写命令优先，避免整轮轮询拖住输出。
                DrainHighPriority();
                DrainWrites();
                now = Environment.TickCount64;
            }
        }

        private List<IoPointDescriptor> ResolveGroupPoints(IoPollGroup group)
        {
            long now = Environment.TickCount64;

            // 只轮询“可读”的点：输出点默认不回读（除非能力位声明 Readback）。
            // 这样“本轮缺失的点”就等价于“读失败”，坏点隔离才有可靠判据。
            var all = Descriptor.Points
                .Where(p => p.Enabled && IsPollable(p) && !IsIsolated(p.PointId, now))
                .ToList();

            if (group.PointIds == null || group.PointIds.Count == 0)
                return all;

            var ids = new HashSet<string>(group.PointIds, StringComparer.OrdinalIgnoreCase);
            var result = all.Where(p => ids.Contains(p.PointId)).ToList();
            return result.Count > 0 ? result : all;
        }

        private bool IsPollable(IoPointDescriptor point)
        {
            if (point.Kind is IoPointKind.DigitalInput or IoPointKind.AnalogInput)
                return true;
            return (Capabilities & IoCapabilities.Readback) != 0;
        }

        /// <summary>坏点隔离：连续失败达到阈值后移出正常轮询，改为低频探测。</summary>
        private bool IsIsolated(string pointId, long now)
        {
            if (!_pointFailures.TryGetValue(pointId, out var failure)) return false;
            if (failure.Count < FaultedPointThreshold) return false;

            long nextProbe = failure.LastAttemptTicks + IsolatedProbeIntervalMs;
            return now < nextProbe;
        }

        /// <summary>坏点隔离阈值：连续 3 轮读不到才隔离（单轮抖动不隔离）。</summary>
        public const int FaultedPointThreshold = 3;

        /// <summary>隔离点的低频探测间隔（毫秒）。</summary>
        public const int IsolatedProbeIntervalMs = 5000;

        private readonly ConcurrentDictionary<string, (int Count, long LastAttemptTicks, string? Error)> _pointFailures
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>当前被隔离的点（诊断用）</summary>
        public IReadOnlyList<string> IsolatedPoints =>
            _pointFailures.Where(p => p.Value.Count >= FaultedPointThreshold)
                .Select(p => p.Key)
                .ToList();

        private void PublishBatch(IReadOnlyList<IoPointDescriptor> points, IDictionary<string, IoPointValue> values)
        {
            var changes = new List<IoPointReadResult>();
            long nowTicks = DateTime.Now.Ticks;
            long now = Environment.TickCount64;

            foreach (var point in points)
            {
                if (!values.TryGetValue(point.PointId, out var raw))
                {
                    // 驱动返回 true 但该点没有值 = 这一轮这个点读失败（坏地址/协议异常）。
                    var failure = _pointFailures.TryGetValue(point.PointId, out var previous)
                        ? previous
                        : (0, 0L, null);
                    int count = failure.Count + 1;
                    _pointFailures[point.PointId] = (count, now, "本轮未返回该点的值");

                    if (count == FaultedPointThreshold)
                    {
                        ReportFault("Poll", "PointFaulted",
                            $"点位 {point.PointId} 连续 {count} 轮未返回数据，已从正常轮询移出（低频探测）",
                            point.PointId);
                    }
                    continue;
                }

                // 读到值即视为该点恢复：清掉失败计数，隔离自然解除。
                if (_pointFailures.TryRemove(point.PointId, out _))
                {
                }

                var value = ApplyEngineering(point, raw, nowTicks);
                if (!ShouldPublish(point, value)) continue;
                _cache[point.PointId] = value;
                changes.Add(IoPointReadResult.Ok(point.PointId, value));
            }

            if (changes.Count > 0)
                PointsChanged?.Invoke(this, new IoPointsChangedEventArgs(changes));
        }

        private void PublishOne(IoPointDescriptor point, IoPointValue raw, DateTime time)
        {
            var value = ApplyEngineering(point, raw, time.Ticks);
            _cache[point.PointId] = value;
            PointsChanged?.Invoke(this, new IoPointsChangedEventArgs(new[] { IoPointReadResult.Ok(point.PointId, value) }));
        }

        private IoPointValue ApplyEngineering(IoPointDescriptor point, IoPointValue raw, long nowTicks)
        {
            if (point.Kind is IoPointKind.DigitalInput or IoPointKind.DigitalOutput)
                return new IoPointValue(IoDataType.Bool, raw.Boolean ? 1 : 0, raw.Boolean ? 1 : 0,
                                        raw.Boolean, raw.Quality, new DateTime(nowTicks));

            double engineering = raw.Numeric * point.Scale + point.Offset;
            return new IoPointValue(point.DataType, engineering, raw.Raw, raw.Boolean,
                                    raw.Quality, new DateTime(nowTicks));
        }

        private bool ShouldPublish(IoPointDescriptor point, IoPointValue value)
        {
            // 写回显宽限期：写成功后短暂忽略轮询旧值，避免界面“写成功又跳回去”。
            if (_lastWriteTicks.TryGetValue(point.PointId, out long writeTicks))
            {
                int grace = Descriptor.Params.WriteEchoGraceMs;
                if (grace <= 0)
                {
                    var group = Descriptor.Params.PollGroups?.FirstOrDefault();
                    grace = Math.Max(50, (group?.PeriodMs ?? 50) * 2);
                }
                if (Environment.TickCount64 - writeTicks < grace)
                    return false;
            }

            if (!_cache.TryGetValue(point.PointId, out var previous))
                return true;

            if (value.Quality != previous.Quality)
                return true;

            if (point.Kind is IoPointKind.DigitalInput or IoPointKind.DigitalOutput)
            {
                if (point.DebounceMs > 0 && value.Boolean != previous.Boolean)
                {
                    var state = _debounce.GetOrAdd(point.PointId, (value.Boolean, Environment.TickCount64));
                    if (state.Value != value.Boolean)
                    {
                        _debounce[point.PointId] = (value.Boolean, Environment.TickCount64);
                        return false;
                    }
                    if (Environment.TickCount64 - state.Since < point.DebounceMs)
                        return false;
                }
                return value.Boolean != previous.Boolean;
            }

            if (point.Deadband > 0)
                return Math.Abs(value.Numeric - previous.Numeric) >= point.Deadband;

            return Math.Abs(value.Numeric - previous.Numeric) > double.Epsilon;
        }

        private void MarkUncertain(IReadOnlyList<IoPointDescriptor> points)
        {
            foreach (var point in points)
            {
                if (_cache.TryGetValue(point.PointId, out var old))
                    _cache[point.PointId] = old with { Quality = IoQuality.Uncertain };
                else
                    _cache[point.PointId] = IoPointValue.Bad(point.DataType);
            }
        }

        private void TouchWrite(string pointId, IoPointValue value)
        {
            _cache[pointId] = value;
            _lastWriteTicks[pointId] = Environment.TickCount64;
            Interlocked.Exchange(ref _lastSuccessTicks, DateTime.Now.Ticks);
            PointsChanged?.Invoke(this, new IoPointsChangedEventArgs(new[] { IoPointReadResult.Ok(pointId, value) }));
        }

        private int ComputeWaitMs()
        {
            if (State != IoDeviceState.Online)
                return _autoConnect ? 100 : 200;

            long now = Environment.TickCount64;
            long min = 200;
            foreach (var group in Descriptor.Params.PollGroups ?? Enumerable.Empty<IoPollGroup>())
            {
                long due = _groupDueTicks.GetOrAdd(group.Name, 0);
                min = Math.Min(min, Math.Max(1, due - now));
            }
            return (int)Math.Clamp(min, 1, 200);
        }

        private void MarkAllGroupsDue()
        {
            foreach (var group in Descriptor.Params.PollGroups ?? Enumerable.Empty<IoPollGroup>())
                _groupDueTicks[group.Name] = 0;
        }

        #endregion

        #region 队列与工具

        private WorkItem Enqueue(BlockingCollection<WorkItem> queue, Func<object?> action, string? pointId)
        {
            var item = new WorkItem
            {
                Action = action,
                PointId = pointId,
                Completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously)
            };

            try
            {
                if (!queue.TryAdd(item, Math.Max(1, Descriptor.Params.CommandEnqueueTimeoutMs)))
                {
                    item.Completion.TrySetException(new InvalidOperationException("设备命令队列已满"));
                    return item;
                }
            }
            catch (InvalidOperationException)
            {
                item.Completion.TrySetException(new ObjectDisposedException(nameof(IoDeviceBase)));
                return item;
            }

            Wake();
            return item;
        }

        private void DrainHighPriority()
        {
            while (_highPriority.TryTake(out var item))
                Execute(item);
        }

        private void DrainWrites()
        {
            while (_writes.TryTake(out var item))
                Execute(item);
        }

        private void Execute(WorkItem item)
        {
            if (item.Cancelled)
            {
                item.Completion.TrySetCanceled();
                return;
            }

            item.Started = true;
            try
            {
                var result = item.Action();
                item.Completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                item.Completion.TrySetException(ex);
            }
        }

        private void ReleasePending(BlockingCollection<WorkItem> queue)
        {
            while (queue.TryTake(out var item))
                item.Completion.TrySetCanceled();
        }

        private void Wake()
        {
            try { _signal.Release(); }
            catch (SemaphoreFullException) { }
            catch (ObjectDisposedException) { }
        }

        private bool TryPrepareWrite(string pointId, IoPointValue value, out IoWriteRequest request, out string? error)
        {
            request = default;
            error = null;

            if (!TryGetPoint(pointId, out var point))
            {
                error = $"未配置点位：{pointId}";
                return false;
            }

            if (!point.Enabled)
            {
                error = $"点位已禁用：{pointId}";
                return false;
            }

            if (!point.IsWritable)
            {
                error = $"点位只读：{pointId}";
                return false;
            }

            if (point.Kind is IoPointKind.DigitalOutput)
            {
                var raw = new IoPointValue(IoDataType.Bool, value.Boolean ? 1 : 0, value.Boolean ? 1 : 0,
                                           value.Boolean, IoQuality.Good, DateTime.Now);
                request = new IoWriteRequest(pointId, raw);
                return true;
            }

            if (point.Scale == 0)
            {
                error = $"点位 {pointId} 的缩放系数为 0，无法写入";
                return false;
            }

            double rawValue = (value.Numeric - point.Offset) / point.Scale;
            long rawInt = (long)Math.Round(rawValue);
            var rawAnalog = new IoPointValue(point.DataType, rawValue, rawInt, rawInt != 0, IoQuality.Good, DateTime.Now);
            request = new IoWriteRequest(pointId, rawAnalog);
            return true;
        }

        protected bool TryGetPoint(string pointId, out IoPointDescriptor point)
        {
            point = Descriptor.Points.FirstOrDefault(p =>
                string.Equals(p.PointId, pointId, StringComparison.OrdinalIgnoreCase))!;
            return point != null;
        }

        private bool WaitForState(IoDeviceState target, int timeoutMs)
        {
            int wait = Math.Max(1, timeoutMs);
            long deadline = Environment.TickCount64 + wait;
            while (State != target)
            {
                if (_disposed || _cts.IsCancellationRequested) return false;
                if (Environment.TickCount64 >= deadline) return State == target;
                Thread.Sleep(25);
            }
            return true;
        }

        private void SetState(IoDeviceState next, string detail)
        {
            var old = (IoDeviceState)Interlocked.Exchange(ref _state, (int)next);
            lock (_cts) _stateDetail = detail ?? string.Empty;
            if (old != next)
                StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ReportFault(string kind, string code, string message, string? pointId = null)
        {
            Interlocked.Increment(ref _faultCount);
            var fault = new IoFault(DateTime.Now, kind, code, message, pointId);
            FaultOccurred?.Invoke(this, fault);
            try { Context.Log?.Warn($"[IO] [{Descriptor.DeviceId}] {kind}/{code}: {message}"); }
            catch { }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _autoConnect = false;

            _cts.Cancel();
            _highPriority.CompleteAdding();
            _writes.CompleteAdding();
            Wake();

            bool exited = true;
            try
            {
                if (!_thread.Join(3000)) exited = false;
            }
            catch (ThreadStateException) { }

            if (!exited)
            {
                _disposeConnectionOnExit = true;
                return;
            }

            try { DisconnectCore(); } catch { }
            _highPriority.Dispose();
            _writes.Dispose();
            _signal.Dispose();
            _cts.Dispose();
        }
    }
}
