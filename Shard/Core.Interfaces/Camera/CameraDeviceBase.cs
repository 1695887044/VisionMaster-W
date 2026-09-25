using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Core.Interfaces
{
    /// <summary>
    /// 相机驱动基类：把"推模式"的骨架一次性固化，厂商插件只写设备差异。
    ///
    /// 固化在这里的四件事（每一家相机都绕不开，各写一遍必然有的对有的错）：
    ///   1. 环形帧队列 + 溢出丢旧 + 溢出计数（丢帧必须可见，绝不能静默）
    ///   2. 连接状态机（Closed / Connecting / Online / Streaming）+ 状态变化通知
    ///   3. 心跳看门狗：多久没有"设备还活着"的证据就判掉线，并把队列里的陈旧帧清掉
    ///   4. 消费语义的取图口（WaitNextFrame 消费 / TryGetLatest 只读）
    ///
    /// 厂商插件要写的只有三件事：OpenCore / CloseCore / ApplySettingsCore，
    /// 外加（真机才有）"SDK 回调里解出像素后调 PushFrame"这一步。
    ///
    /// 与 VisionPluginBase 同住 Core.Interfaces 是刻意的：那个基类同样带大量逻辑（端口发现、
    /// 配置生命周期、JSON 快照），插件作者拿到的"骨架"一直就放在契约程序集里。
    /// 这样驱动插件只需引用 Core.Interfaces，不必也不可能引用宿主 exe。
    /// </summary>
    public abstract class CameraDeviceBase : ICameraDevice
    {
        private readonly object _gate = new();
        private readonly ConcurrentQueue<CameraFrame> _queue = new();

        /// <summary>
        /// "可能来新帧了"的通知信号（二元信号量）。与 ImageHub 同一范式：
        /// 用事件驱动而不是 Sleep 轮询——节拍快时不白等、节拍慢时不空转 CPU，
        /// 且停止流程后能立刻退出等待。
        /// 容量取 1 就够：它只表达"队列里可能有东西"，到底有没有由队列自己说了算
        /// （醒来后一律回队列重试）。
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0, 1);

        private CameraConnectionState _state = CameraConnectionState.Closed;
        private string _stateDetail = "未连接";
        private CameraSettings _settings;

        /// <summary>心跳看门狗。只在 Open 后存活，Close / Dispose 时释放</summary>
        private Timer? _watchdog;

        /// <summary>最近一次"设备还活着"的时刻（UTC Ticks）。跨线程读写，用 Volatile</summary>
        private long _lastAliveUtcTicks;

        private long _overflow;
        private long _dropped;
        private long _received;
        private long _frameSeed;

        /// <summary>用户"想不想在采流"的意图。掉线恢复后据此决定回到 Streaming 还是 Online</summary>
        private bool _streaming;

        /// <summary>是否接受入帧。StopStream 后置 false，把"我已停止"这个意图真正落到实处</summary>
        private bool _acceptFrames;

        /// <summary>溢出告警是否已发过（避免 33fps 下刷屏）。ResetCounters 会重新武装它</summary>
        private bool _overflowWarned;

        private bool _disposed;

        protected CameraDeviceBase(CameraDescriptor descriptor)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            _settings = (descriptor.Settings ?? new CameraSettings()).Normalized();
        }

        #region 对外只读状态

        /// <inheritdoc />
        public CameraDescriptor Descriptor { get; }

        /// <inheritdoc />
        public CameraConnectionState State
        {
            get { lock (_gate) return _state; }
        }

        /// <inheritdoc />
        public string StateDetail
        {
            get { lock (_gate) return _stateDetail; }
        }

        /// <inheritdoc />
        public long OverflowCount => Interlocked.Read(ref _overflow);

        /// <inheritdoc />
        public long DroppedFrameCount => Interlocked.Read(ref _dropped);

        /// <inheritdoc />
        public long ReceivedFrameCount => Interlocked.Read(ref _received);

        /// <inheritdoc />
        public int PendingFrameCount => _queue.Count;

        /// <summary>可选日志（由宿主 CameraProvider 在创建后注入；为空则全部静默）</summary>
        public ILogService? Log { get; set; }

        /// <inheritdoc />
        public event EventHandler StateChanged;

        #endregion

        #region 子类要实现的差异点

        /// <summary>打开设备/资源。返回 false 表示失败（失败原因请写进 <see cref="SetDetail"/>）</summary>
        protected abstract bool OpenCore();

        /// <summary>关闭设备/资源。实现里应吞掉异常（关闭失败不该让整个状态机卡住）</summary>
        protected abstract void CloseCore();

        /// <summary>
        /// 应用采集参数。默认"什么都不做但成功"——网络相机的曝光由客户端那边决定，
        /// 软件侧确实无处可设。真机必须重写（否则设曝光是静默失效的假象）。
        /// </summary>
        protected virtual bool ApplySettingsCore(CameraSettings settings) => true;

        /// <summary>真机去启动采集；网络相机无需动作（帧仍由客户端送来）</summary>
        protected virtual bool StartStreamCore() => true;

        /// <summary>真机去停止采集；网络相机无需动作</summary>
        protected virtual void StopStreamCore() { }

        /// <summary>
        /// 是否需要"活着"的证据（心跳）。默认 false（真机有 SDK 掉线回调，不需要轮询推断）；
        /// 网络相机必须重写为 true——HTTP 无连接，只能靠"多久没消息"来判掉线。
        /// </summary>
        protected virtual bool RequiresHeartbeat => false;

        /// <summary>Open 成功后的落点状态。真机 = Online；网络相机 = Connecting（还在等客户端接入）</summary>
        protected virtual CameraConnectionState StateAfterOpen => CameraConnectionState.Online;

        /// <summary>Open 成功后的状态说明</summary>
        protected virtual string OpenSuccessDetail => "已连接，等待开始采流";

        #endregion

        #region 打开 / 关闭

        /// <inheritdoc />
        public bool Open()
        {
            lock (_gate)
            {
                if (_disposed) return false;
                if (_state != CameraConnectionState.Closed) return true; // 已打开：幂等
                SetStateLocked(CameraConnectionState.Connecting, "正在打开设备…");
            }
            RaiseStateChanged();

            // OpenCore 放在锁外：真机打开设备可能要几百毫秒，持锁调用会把状态查询全堵死
            var ok = false;
            string error = null;
            try
            {
                ok = OpenCore();
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
            }

            bool changed;
            lock (_gate)
            {
                if (ok)
                {
                    Volatile.Write(ref _lastAliveUtcTicks, DateTime.UtcNow.Ticks);
                    _acceptFrames = true;
                    _streaming = false;
                    changed = SetStateLocked(StateAfterOpen, OpenSuccessDetail);
                    StartWatchdogLocked();
                }
                else
                {
                    changed = SetStateLocked(
                        CameraConnectionState.Closed,
                        $"打开失败：{error ?? "设备未给出原因"}");
                }
            }
            if (changed) RaiseStateChanged();

            if (!ok) LogWarn($"[{Descriptor.Caption}] 打开失败：{error ?? "设备未给出原因"}");
            return ok;
        }

        /// <inheritdoc />
        public void Close()
        {
            bool changed;
            lock (_gate)
            {
                if (_state == CameraConnectionState.Closed) return;
                StopWatchdogLocked();
                _acceptFrames = false;
                _streaming = false;
                changed = SetStateLocked(CameraConnectionState.Closed, "已断开");
            }

            // 先关设备再清队列：反过来的话，CloseCore 期间 SDK 还可能再送一帧进来
            try { CloseCore(); }
            catch (Exception ex) { LogWarn($"[{Descriptor.Caption}] 关闭设备时异常（已忽略）：{ex.Message}"); }

            DropAllQueued("断开连接");

            if (changed) RaiseStateChanged();
        }

        #endregion

        #region 采流开关

        /// <inheritdoc />
        public bool StartStream()
        {
            bool changed;
            lock (_gate)
            {
                if (_disposed) return false;
                if (_state == CameraConnectionState.Closed)
                {
                    SetStateLocked(CameraConnectionState.Closed, "尚未连接：请先连接相机再开始采流");
                    return false;
                }

                _acceptFrames = true;
                _streaming = true;
                Volatile.Write(ref _lastAliveUtcTicks, DateTime.UtcNow.Ticks);
                changed = SetStateLocked(CameraConnectionState.Streaming, "已开始采流");
            }

            var ok = false;
            string error = null;
            try
            {
                ok = StartStreamCore();
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
            }

            if (!ok)
            {
                lock (_gate)
                {
                    _streaming = false;
                    SetStateLocked(CameraConnectionState.Online, $"启动采流失败：{error ?? "设备未给出原因"}");
                }
            }

            if (changed) RaiseStateChanged();
            if (!ok) LogWarn($"[{Descriptor.Caption}] 启动采流失败：{error ?? "设备未给出原因"}");
            return ok;
        }

        /// <inheritdoc />
        public void StopStream()
        {
            bool changed;
            lock (_gate)
            {
                if (_disposed) return;
                _acceptFrames = false;
                _streaming = false;
                changed = SetStateLocked(
                    _state == CameraConnectionState.Closed
                        ? CameraConnectionState.Closed
                        : CameraConnectionState.Online,
                    "已停止采流");
            }

            try { StopStreamCore(); }
            catch (Exception ex) { LogWarn($"[{Descriptor.Caption}] 停止采流时异常（已忽略）：{ex.Message}"); }

            // 停止后残留的帧已无意义：留着会让"下次开始采流"立刻取到一张停止前的旧图
            DropAllQueued("停止采流");

            if (changed) RaiseStateChanged();
        }

        #endregion

        #region 参数

        /// <inheritdoc />
        public bool ApplySettings(CameraSettings settings)
        {
            if (settings == null) return false;

            var normalized = settings.Normalized();
            bool ok;
            try
            {
                ok = ApplySettingsCore(normalized);
            }
            catch (Exception ex)
            {
                lock (_gate) { SetStateLocked(_state, $"应用参数失败：{ex.Message}"); }
                RaiseStateChanged();
                LogWarn($"[{Descriptor.Caption}] 应用参数失败：{ex.Message}");
                return false;
            }

            if (!ok) return false;

            lock (_gate) { _settings = normalized; }
            // 同步回配置对象：配置界面点"确定"后由宿主把 Descriptor 落盘，
            // 两边不同步会出现"界面显示改了、下次打开又变回去"
            Descriptor.Settings = normalized.Clone();
            return true;
        }

        /// <inheritdoc />
        public CameraSettings ReadSettings()
        {
            lock (_gate) return _settings.Clone();
        }

        /// <summary>当前生效参数（内部用，不再拷贝）</summary>
        private CameraSettings SettingsSnapshot
        {
            get { lock (_gate) return _settings; }
        }

        #endregion

        #region 心跳与掉线

        /// <inheritdoc />
        public void NotifyAlive()
        {
            Volatile.Write(ref _lastAliveUtcTicks, DateTime.UtcNow.Ticks);

            bool changed;
            lock (_gate)
            {
                if (_disposed || _state == CameraConnectionState.Closed) return;

                // 只有"正在等"的状态才需要被唤醒；Online/Streaming 本来就已经在线
                if (_state != CameraConnectionState.Connecting) return;

                changed = _streaming
                    ? SetStateLocked(CameraConnectionState.Streaming, "设备已恢复，采流中")
                    : SetStateLocked(CameraConnectionState.Online, "设备已连接");
            }
            if (changed) RaiseStateChanged();
        }

        /// <summary>
        /// 由驱动在 SDK 的掉线回调里调用（真机专用）。
        /// 网络相机不需要：它的掉线由心跳超时推断出来。
        /// </summary>
        protected void NotifyDeviceLost(string reason) => HandleLost(reason);

        /// <summary>
        /// 判掉线：状态回 Connecting 并清空队列。
        ///
        /// 为什么要清空队列：掉线期间积压的帧在恢复后立刻被取走，会让流程把"几秒前的旧图"
        /// 当成"当前工件"来检测——这是比"明确失败"危险得多的错误结果错位。
        /// 丢弃量单独计数（DroppedFrameCount），与队列满导致的溢出分开，便于现场分辨根因。
        /// </summary>
        private void HandleLost(string reason)
        {
            bool changed;
            lock (_gate)
            {
                if (_disposed || _state == CameraConnectionState.Closed) return;
                if (_state == CameraConnectionState.Connecting) return; // 已经在等，不重复报

                changed = SetStateLocked(CameraConnectionState.Connecting, $"{reason}，等待设备恢复");
            }

            var dropped = DropAllQueued(reason);

            if (changed) RaiseStateChanged();
            LogWarn($"[{Descriptor.Caption}] {reason}（已清空 {dropped} 帧陈旧图像）");
        }

        private void OnWatchdog(object state)
        {
            if (_disposed || !RequiresHeartbeat) return;

            var last = Volatile.Read(ref _lastAliveUtcTicks);
            if (last == 0) return;

            CameraConnectionState current;
            int timeoutMs;
            lock (_gate)
            {
                current = _state;
                timeoutMs = _settings.HeartbeatTimeoutMs;
            }

            // 已经处于等待态就不再重复报；Closed 更不该报
            if (current == CameraConnectionState.Closed || current == CameraConnectionState.Connecting) return;

            var elapsedMs = (DateTime.UtcNow.Ticks - last) / TimeSpan.TicksPerMillisecond;
            if (elapsedMs <= timeoutMs) return;

            HandleLost($"已 {elapsedMs / 1000.0:F1}s 未收到心跳");
        }

        private void StartWatchdogLocked()
        {
            StopWatchdogLocked();
            if (!RequiresHeartbeat) return;

            // 检查频率取超时的 1/3，夹在 [200ms, 1000ms]：太密是无谓开销，太疏则掉线判定迟钝
            var intervalMs = Math.Clamp(_settings.HeartbeatTimeoutMs / 3, 200, 1000);
            _watchdog = new Timer(OnWatchdog, null, intervalMs, intervalMs);
        }

        private void StopWatchdogLocked()
        {
            _watchdog?.Dispose();
            _watchdog = null;
        }

        #endregion

        #region 送帧与取帧

        /// <inheritdoc />
        public bool PushFrame(CameraFrame frame, out string error)
        {
            error = null;

            if (frame == null)
            {
                error = "帧为空";
                return false;
            }

            if (!frame.IsValid)
            {
                // 尺寸不自洽的帧放进队列，会在消费者侧以 GenImage 抛异常的形式炸出来，
                // 而那时已经看不出是哪一帧的问题了 —— 必须在入口就拦下
                Interlocked.Increment(ref _dropped);
                error = $"帧数据无效（{frame.Width}x{frame.Height}x{frame.Channels}，像素 {frame.PixelData?.Length ?? 0} 字节）";
                return false;
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    error = "相机设备已释放";
                    return false;
                }

                if (_state == CameraConnectionState.Closed)
                {
                    error = "相机未打开，请先在「系统 → 相机设置」连接该相机";
                    Interlocked.Increment(ref _dropped);
                    return false;
                }

                if (!_acceptFrames)
                {
                    error = "相机已停止采流，帧被拒绝";
                    Interlocked.Increment(ref _dropped);
                    return false;
                }
            }

            // 回填身份：客户端只负责送像素与序列号，Id / 帧号由设备权威生成
            frame.CameraId = Descriptor.Id;
            frame.SerialNo = Descriptor.SerialNo;
            frame.FrameId = Interlocked.Increment(ref _frameSeed);
            if (frame.Timestamp == default) frame.Timestamp = DateTime.Now;

            _queue.Enqueue(frame);
            Interlocked.Increment(ref _received);
            Volatile.Write(ref _lastAliveUtcTicks, DateTime.UtcNow.Ticks);

            TrimOverflow();

            // 唤醒等待者。没人等时信号量已满（计数 1），Release 会抛 SemaphoreFullException——
            // 那正是"无需唤醒"的情形，吞掉即可
            try { _signal.Release(); }
            catch (SemaphoreFullException) { }

            // 首帧到达即视为已进入采流：设备在持续出图，就是"正在采流"最直接的证据。
            // 这也让"等待客户端接入（Connecting）"能在第一帧到来时自动跃迁，无需人工干预。
            bool changed;
            lock (_gate)
            {
                changed = SetStateLocked(
                    CameraConnectionState.Streaming,
                    $"采流中（已收 {Interlocked.Read(ref _received)} 帧）");
            }
            if (changed) RaiseStateChanged();

            return true;
        }

        /// <summary>
        /// 队列超限就丢最旧。丢帧必须可见，所以每次丢弃都计数；
        /// 且**首次**溢出立即告警一次（之后只计数）——33fps 下逐帧告警会把日志刷爆，
        /// 反而让人看不见真正的第一条。
        /// </summary>
        private void TrimOverflow()
        {
            var capacity = SettingsSnapshot.BufferCapacity;
            if (capacity < 1) capacity = 1;

            var dropped = 0;
            while (_queue.Count > capacity && _queue.TryDequeue(out _))
                dropped++;

            if (dropped <= 0) return;

            Interlocked.Add(ref _overflow, dropped);

            if (_overflowWarned) return;
            _overflowWarned = true;
            LogWarn(
                $"[{Descriptor.Caption}] 帧队列溢出：处理速度跟不上相机出图速度，已丢弃 {dropped} 帧最旧图像"
                + $"（队列上限 {capacity}）。丢弃的每一帧都意味着一个未被检测的工件，请提高流程处理速度或降低相机节拍。");
        }

        /// <inheritdoc />
        public bool TryGetLatest(out CameraFrame frame)
        {
            // Peek 而非 Dequeue：预览/监视画面绝不能吃掉生产帧，
            // 否则一开监视窗口就会偶发漏检，且极难复现
            return _queue.TryPeek(out frame);
        }

        /// <inheritdoc />
        public bool WaitNextFrame(out CameraFrame frame, int timeoutMs, CancellationToken ct)
        {
            frame = null;

            if (timeoutMs <= 0) timeoutMs = SettingsSnapshot.FrameTimeoutMs;

            var deadline = Environment.TickCount64 + timeoutMs;
            while (true)
            {
                // 快路径：队列里有货立刻拿走，零等待
                if (_queue.TryDequeue(out frame)) return true;

                // 进入等待前先看一眼取消：避免"已停止还要先睡一下"的多余延迟
                if (ct.IsCancellationRequested) return false;

                var remain = deadline - Environment.TickCount64;
                if (remain <= 0) return false;

                try
                {
                    if (!_signal.Wait((int)Math.Min(remain, int.MaxValue), ct))
                        return false; // 等满超时
                }
                catch (OperationCanceledException)
                {
                    return false; // 停止流程：交回调用方按取消语义处理
                }

                // 醒来后一律回队列重试：信号只代表"可能来了新帧"，
                // 也可能已被并发的另一个取图者拿走 —— 那就继续等
            }
        }

        /// <summary>清空队列并返回丢弃帧数</summary>
        private int DropAllQueued(string reason)
        {
            var dropped = 0;
            while (_queue.TryDequeue(out _)) dropped++;

            if (dropped > 0)
            {
                Interlocked.Add(ref _dropped, dropped);
                LogInfo($"[{Descriptor.Caption}] {reason}：已丢弃队列中 {dropped} 帧");
            }
            return dropped;
        }

        /// <inheritdoc />
        public void ResetCounters()
        {
            Interlocked.Exchange(ref _overflow, 0);
            Interlocked.Exchange(ref _dropped, 0);
            _overflowWarned = false;
        }

        #endregion

        #region 状态机与日志

        /// <summary>改状态（调用方必须持 _gate）。返回是否真的变了，便于在锁外决定要不要发通知</summary>
        private bool SetStateLocked(CameraConnectionState state, string detail)
        {
            var nextDetail = detail ?? string.Empty;
            if (_state == state && string.Equals(_stateDetail, nextDetail, StringComparison.Ordinal))
                return false;

            _state = state;
            _stateDetail = nextDetail;
            return true;
        }

        /// <summary>供子类在 OpenCore / ApplySettingsCore 里写失败原因</summary>
        protected void SetDetail(string detail)
        {
            lock (_gate) { SetStateLocked(_state, detail); }
            RaiseStateChanged();
        }

        /// <summary>在锁外触发，订阅方可以安全地回调设备上的只读查询</summary>
        private void RaiseStateChanged()
        {
            try
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                // 订阅方（界面）的异常绝不能影响设备内部状态机
            }
        }

        private void LogInfo(string message) => Log?.Info(message);

        private void LogWarn(string message) => Log?.Warn(message);

        #endregion

        #region 释放

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_gate)
            {
                StopWatchdogLocked();
                _acceptFrames = false;
                _streaming = false;
                SetStateLocked(CameraConnectionState.Closed, "已释放");
            }

            try { CloseCore(); }
            catch { /* 释放路径上的异常不再上抛 */ }

            while (_queue.TryDequeue(out _)) { }

            try { _signal.Dispose(); } catch { }
        }

        #endregion
    }
}
