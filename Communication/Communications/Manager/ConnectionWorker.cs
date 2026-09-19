using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>连接专属工作线程：把一个通信连接的全部设备 I/O（建连/读/写/轮询/断开）串行化到同一个线程执行。</para>
    /// <para>解决的痛点：</para>
    /// <para>1) B2——HSL 设备对象不是线程安全的，旧实现 Timer 回调/UI 线程/重连线程并发读写同一 socket，随机错包；</para>
    /// <para>2) C1——旧心跳只看 IsConnected 属性（半开连接永远"正常"），现在以"真实读写成功时刻"为生命依据；</para>
    /// <para>3) C5——旧重连固定间隔且多个 Timer 可叠加发起，现在单一状态机 + 指数退避，重连节奏可收敛。</para>
    /// <para>用法：Manager 为每个连接持有一个 Worker；读写通过 <see cref="Invoke"/> / <see cref="InvokeAsync"/> 投递命令并等待结果；</para>
    /// <para>轮询逻辑经 <see cref="PollAction"/> 注入，由 Worker 线程按 <see cref="PollIntervalMs"/> 周期驱动。</para>
    /// </summary>
    public sealed class ConnectionWorker : IDisposable
    {
        #region 命令模型

        private sealed class WorkItem
        {
            /// <summary>在 Worker 线程上执行的动作；参数为连接对象</summary>
            public Action<ICommunicationConnection> Action = null!;
            /// <summary>条目未被执行就因循环退出被丢弃时的清理钩子（释放调用方 TCS，防悬挂）</summary>
            public Action? OnAbandon;
        }

        #endregion

        private readonly ICommunicationConnection _connection;
        private readonly BlockingCollection<WorkItem> _queue = new(boundedCapacity: 256);
        private readonly CancellationTokenSource _cts = new();
        private readonly Thread _thread;

        // 状态与调度参数（跨线程读：State 用 Volatile；其余配置属性仅在建连前/加锁语义下赋值）
        private int _state = (int)ConnectionState.Disconnected;
        private volatile bool _autoConnect;          // 是否维持"应保持连接"意图（Connect 置 true，Disconnect/达到重连上限置 false）
        private volatile bool _autoReconnect = true; // 外部策略：断裂/建连失败后是否允许退避重连（Manager 按配置注入）
        private volatile int _pollIntervalMs = 500;  // 轮询周期（也是循环节奏）
        private long _nextConnectTick;               // 下次允许发起连接的时刻（TickCount64，Volatile 访问）

        private int _reconnectAttempt;
        private long _lastPollSuccessTicks;          // 仅 Worker 线程访问
        private long _lastSuccessTicks;              // 最近一次真实通信成功的 DateTime.Ticks（Interlocked 访问）
        private bool _disposed;

        #region 公共配置与事件

        /// <summary>连接当前状态（由本 Worker 独家维护，是唯一的真相源）</summary>
        public ConnectionState State => (ConnectionState)Volatile.Read(ref _state);

        public string ConnectionName => _connection.ConnectionName;

        /// <summary>是否处于已连接状态</summary>
        public bool IsConnected => State == ConnectionState.Connected && _connection.IsConnected;

        /// <summary>基础重连间隔（失败后按 2 的幂退避到 <see cref="ReconnectMaxIntervalMs"/>）</summary>
        public int ReconnectBaseIntervalMs { get; set; } = 5000;

        /// <summary>重连退避上限</summary>
        public int ReconnectMaxIntervalMs { get; set; } = 30000;

        /// <summary>
        /// 是否维持自动重连（由 Manager 按"管理器总开关 && 连接级开关"注入），默认 true。
        /// 置 false 后：首次建连失败或连接断裂会直接停在 <see cref="ConnectionState.Error"/> 终态，
        /// 不再等待退避重试，需外部重新调用 <see cref="Connect"/> 才会再次尝试。
        /// </summary>
        public bool AutoReconnect
        {
            get => _autoReconnect;
            set => _autoReconnect = value;
        }

        /// <summary>最大重连次数，0 = 无限</summary>
        public int MaxReconnectAttempts { get; set; } = 0;

        /// <summary>允许外部调整轮询周期（连接后按配置的 ReadCycleMs 设定）</summary>
        public int PollIntervalMs
        {
            get => _pollIntervalMs;
            set { if (value > 0) _pollIntervalMs = value; }
        }

        /// <summary>最近一次真实通信成功时刻（轮询/读写任一成功都会刷新——"真心跳"依据）</summary>
        public DateTime LastCommunicationSuccessTime => new DateTime(Interlocked.Read(ref _lastSuccessTicks));

        /// <summary>
        /// <para>轮询回调：由 Worker 线程按周期执行（t5 批量规划器挂这里）。</para>
        /// <para>契约：返回 true = 本轮通信成功；返回 false 或抛异常 = 通信级故障，Worker 判连接死亡并调度重连。
        /// 变量级错误（个别地址读失败）必须由回调内部消化，不得返回 false。</para>
        /// </summary>
        public Func<ICommunicationConnection, bool>? PollAction { get; set; }

        // H1：轮询计划脏标记——Manager 增删变量时只置位（O(1)），
        // Worker 在下一轮轮询拍前触发重编译（O(N) 编译每批注册只做一次，替代旧的"每次注册全量重建"）
        private volatile bool _pollPlanDirty;

        /// <summary>标脏轮询计划（Manager 注册/注销变量时调用，O(1)；下一拍由 <see cref="PollPlanDirtyHandler"/> 消费）</summary>
        public void MarkPollPlanDirty() => _pollPlanDirty = true;

        /// <summary>清脏标记（Manager 重编译完成后调用，保证任何重建落点之后无残留脏标记）</summary>
        public void ClearPollPlanDirty() => _pollPlanDirty = false;

        /// <summary>
        /// 拍前重编译回调（Manager 在 ConfigureWorker 时挂接）：Worker 在轮询拍前发现脏标记时调用，
        /// 回调内部重编译并重挂 <see cref="PollAction"/>；回调抛异常时脏标记未清，下一拍自动重试
        /// </summary>
        public Action? PollPlanDirtyHandler { get; set; }

        /// <summary>状态变化（旧状态, 新状态）——在 Worker 线程上触发，订阅方自行处理 UI 调度</summary>
        public event Action<ConnectionState, ConnectionState>? StateChanged;

        /// <summary>通信异常（重连失败、轮询故障等）——用于上层日志节流</summary>
        public event Action<Exception?>? CommunicationError;

        #endregion

        #region 生命周期

        public ConnectionWorker(ICommunicationConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = $"CommWorker-{connection.ConnectionName}"
            };
        }

        /// <summary>启动 Worker 线程（不隐含建连，建连由 <see cref="Connect"/> 触发）</summary>
        public void Start()
        {
            if (_thread.IsAlive) return;
            _thread.Start();
        }

        /// <summary>
        /// 请求建立连接并进入自动重连维持模式（幂等）。
        /// 等待语义：true = 已达成 Connected；false = 超时或 Worker 终止（后台状态机仍会继续重试重连）。
        /// </summary>
        public Task<bool> Connect(int timeoutMs = -1)
        {
            ThrowIfDisposed();
            _reconnectAttempt = 0;  // 外部重新发起 = 重试预算重置（否则 Error 终态后再 Connect 会立刻再次放弃）
            _autoConnect = true;
            Volatile.Write(ref _nextConnectTick, 0L); // 立即允许发起（循环最长 200ms 内醒来处理）
            return WaitStateAsync(ConnectionState.Connected, timeoutMs);
        }

        /// <summary>断开连接并停止自动重连</summary>
        public Task Disconnect()
        {
            _autoConnect = false;
            return InvokeAsync(conn =>
            {
                try { conn.Disconnect(); }
                finally { SetState(ConnectionState.Disconnected); }
            });
        }

        #endregion

        #region 命令投递（读写都串行到 Worker 线程）

        /// <summary>在 Worker 线程执行任意委托并取回结果（通信失败时异常回抛给调用方）</summary>
        public Task<TResult> Invoke<TResult>(Func<ICommunicationConnection, TResult> func)
        {
            ThrowIfDisposed();
            var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                if (!_queue.TryAdd(new WorkItem
                {
                    Action = conn =>
                    {
                        try
                        {
                            var r = func(conn);
                            TouchCommunicationSuccess();
                            tcs.TrySetResult(r);
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    },
                    OnAbandon = () => tcs.TrySetCanceled()
                }))
                {
                    tcs.TrySetException(new InvalidOperationException($"连接 {ConnectionName} 命令队列已满"));
                }
            }
            catch (InvalidOperationException)
            {
                // Dispose 竞态：CompleteAdding 之后 TryAdd 会抛
                tcs.TrySetException(new ObjectDisposedException(nameof(ConnectionWorker)));
            }
            return tcs.Task;
        }

        /// <summary>在 Worker 线程执行任意委托（需要回执）</summary>
        public Task InvokeAsync(Action<ICommunicationConnection> action)
        {
            return Invoke(conn =>
            {
                action(conn);
                return true;
            });
        }

        /// <summary>投递写命令（真异步 B7：调用方拿 Task，不阻塞流程线程）</summary>
        public Task<bool> EnqueueWrite(string address, object value)
        {
            return Invoke(conn =>
            {
                conn.Write(address, value);
                return true;
            });
        }

        #endregion

        #region Worker 主循环

        private void Loop()
        {
            var token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 1) 状态机：应保持连接但未连接 → 按退避时刻发起连接
                    if (_autoConnect && State != ConnectionState.Connected && Now() >= Volatile.Read(ref _nextConnectTick))
                    {
                        TryConnect();
                    }

                    // 2) 等待命令：有负载时最多等"距下一轮轮询的剩余时间"，空闲时 200ms 醒一次检查状态机
                    int waitMs = ComputeWaitMs();
                    if (_queue.TryTake(out var item, waitMs, token))
                    {
                        ExecuteItem(item);
                        DrainQueue();
                    }

                    // 3) 拍前消费"轮询计划脏"标记（H1）：必须独立于轮询分支——
                    //    "在线且从零注册第一个变量"时 PollAction 为 null，进不了下面的轮询分支，脏标记会被饿死
                    if (State == ConnectionState.Connected && _pollPlanDirty)
                        PollPlanDirtyHandler?.Invoke();

                    // 4) 已连接 → 按节拍驱动一轮轮询
                    if (State == ConnectionState.Connected && PollAction != null && ShouldPollNow())
                    {
                        RunPollCycle();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // 循环自身不允许崩（防御性兜底），等下一拍
                    Thread.Sleep(200);
                }
            }

            // 退出前清空剩余命令，避免调用方 Task 永远悬挂
            ReleasePendingItems();
        }

        private int ComputeWaitMs()
        {
            if (State == ConnectionState.Connected && PollAction != null)
                return 10; // 轮询节拍由 ShouldPollNow 控制，等待只用于快速响应命令
            if (_autoConnect && State != ConnectionState.Connected)
                return 100; // 重连中：小步快跑检查退避时刻
            return 200;
        }

        private bool ShouldPollNow()
        {
            long due = Volatile.Read(ref _lastPollSuccessTicks) + PollIntervalMs;
            return Now() >= due;
        }

        private void RunPollCycle()
        {
            try
            {
                bool ok = PollAction!(_connection);
                if (ok)
                {
                    TouchCommunicationSuccess();
                    Volatile.Write(ref _lastPollSuccessTicks, Now());
                    if (_reconnectAttempt > 0) _reconnectAttempt = 0;
                    return;
                }
                OnConnectionBroken(new InvalidOperationException("轮询返回失败：通信级故障"));
            }
            catch (Exception ex)
            {
                OnConnectionBroken(ex);
            }
        }

        private void TryConnect()
        {
            SetState(_reconnectAttempt == 0 ? ConnectionState.Connecting : ConnectionState.Reconnecting);
            try
            {
                if (_connection.Connect())
                {
                    _reconnectAttempt = 0;
                    TouchCommunicationSuccess();
                    Volatile.Write(ref _lastPollSuccessTicks, Now() - PollIntervalMs); // 立即允许首轮轮询
                    SetState(ConnectionState.Connected);
                    return;
                }
                OnConnectionFailed(new InvalidOperationException("建立连接失败"), connectPhase: true);
            }
            catch (Exception ex)
            {
                OnConnectionFailed(ex, connectPhase: true);
            }
        }

        /// <summary>连接动作本身失败：进入退避重连</summary>
        private void OnConnectionFailed(Exception ex, bool connectPhase)
        {
            CommunicationError?.Invoke(ex);
            ScheduleReconnect();
        }

        /// <summary>已连接后通信断裂（轮询失败/异常）：断开 socket 并进入退避重连</summary>
        private void OnConnectionBroken(Exception ex)
        {
            try { _connection.Disconnect(); } catch { /* 断开失败不改变调度 */ }
            CommunicationError?.Invoke(ex);
            if (_autoConnect)
            {
                ScheduleReconnect();
            }
            else
            {
                SetState(ConnectionState.Disconnected);
            }
        }

        /// <summary>指数退避调度下一次连接尝试；关闭自动重连或超过上限则放弃（Error 终态，需外部重新 Connect）</summary>
        private void ScheduleReconnect()
        {
            if (!_autoReconnect)
            {
                _autoConnect = false;
                SetState(ConnectionState.Error);
                return;
            }

            _reconnectAttempt++;
            if (MaxReconnectAttempts > 0 && _reconnectAttempt >= MaxReconnectAttempts)
            {
                _autoConnect = false;
                SetState(ConnectionState.Error);
                return;
            }

            // base * 2^(attempt-1)，封顶 max
            int shift = Math.Min(_reconnectAttempt - 1, 16);
            long backoff = (long)ReconnectBaseIntervalMs << shift;
            if (backoff > ReconnectMaxIntervalMs)
                backoff = ReconnectMaxIntervalMs;

            SetState(ConnectionState.Reconnecting);
            Volatile.Write(ref _nextConnectTick, Now() + backoff);
        }

        private void ExecuteItem(WorkItem item)
        {
            try
            {
                item.Action(_connection);
            }
            catch
            {
                // 委托内部自负异常处理（Invoke 已把结果写入 TCS）；循环不允许被单条命令打崩
            }
        }

        private void DrainQueue()
        {
            while (_queue.TryTake(out var item, 0))
                ExecuteItem(item);
        }

        private void ReleasePendingItems()
        {
            while (_queue.TryTake(out var item, 0))
                item.OnAbandon?.Invoke();
        }

        private async Task<bool> WaitStateAsync(ConnectionState target, int millisecondsTimeout)
        {
            long deadline = millisecondsTimeout < 0 ? long.MaxValue : Environment.TickCount64 + millisecondsTimeout;
            while (true)
            {
                if (State == target) return true;
                if (_disposed || _cts.IsCancellationRequested) return false;
                if (Environment.TickCount64 >= deadline) return State == target;
                await Task.Delay(25).ConfigureAwait(false);
            }
        }

        private void SetState(ConnectionState next)
        {
            var old = (ConnectionState)Interlocked.Exchange(ref _state, (int)next);
            if (old != next)
                StateChanged?.Invoke(old, next);
        }

        private void TouchCommunicationSuccess() => Interlocked.Exchange(ref _lastSuccessTicks, DateTime.Now.Ticks);

        /// <summary>统一时间基：TickCount64 单调递增且不会回绕，所有时刻比较都安全</summary>
        private static long Now() => Environment.TickCount64;

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ConnectionWorker));
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _autoConnect = false;
            _cts.Cancel();
            _queue.CompleteAdding();
            try
            {
                if (!_thread.Join(3000))
                    System.Diagnostics.Debug.WriteLine($"ConnectionWorker[{ConnectionName}] 线程未在 3s 内退出");
            }
            catch (InvalidOperationException)
            {
                // 线程尚未 Start
            }

            try { _connection.Disconnect(); } catch { }
            _connection.Dispose();
            _queue.Dispose();
            _cts.Dispose();
        }
    }
}
