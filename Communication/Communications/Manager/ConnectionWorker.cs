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
    /// <para>用法：Manager 为每个连接持有一个 Worker；读命令通过 <see cref="Invoke"/> / <see cref="InvokeAsync"/> 投递，
    /// 写命令通过 <see cref="InvokeWrite"/> / <see cref="EnqueueWrite"/> 投递；</para>
    /// <para>轮询逻辑经 <see cref="PollScheduler"/> 注入，由 Worker 线程按各扫描组各自的目标周期驱动（组间周期升序 = 优先级）。</para>
    /// <para>N1（写命令优先级）：读/写分成两条队列，Worker 每轮先清空写队列再清空读队列——
    /// 写命令只需等"当前这一条命令/一段轮询"跑完即可插队，不再与读命令挤同一条 FIFO 排在队尾；</para>
    /// <para>N2（队列背压）：队列容量 <see cref="CommandQueueCapacity"/>，投递时最多等待 <see cref="CommandEnqueueTimeoutMs"/>
    /// 让 Worker 消化，超时才判失败——把"瞬时排队"与"真故障"分开。</para>
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

        /// <summary>N2：命令队列容量（UI 连点写 / 脚本批量写不再轻易打满）</summary>
        private const int CommandQueueCapacity = 4096;

        /// <summary>N2：投递命令时等待 Worker 消化的上限（ms），超时才判"队列已满"失败</summary>
        private const int CommandEnqueueTimeoutMs = 2000;

        /// <summary>
        /// C1：单次等待时长上限（ms）。轮询到期时刻再远（扫描组周期最长 1 小时）也不睡超过它，
        /// 保证"轮询计划脏标记重编译 / 状态机变化"的最坏响应延迟与旧实现同量级（旧实现在"已连接但无轮询工作"时正是睡 200ms）。
        /// </summary>
        private const int MaxIdleWaitMs = 200;

        private readonly ICommunicationConnection _connection;

        // N1：读/写分队列——Worker 每轮先清空写队列再清空读队列，写命令得以插队，不再与读命令挤同一条 FIFO
        private readonly BlockingCollection<WorkItem> _readQueue = new(boundedCapacity: CommandQueueCapacity);
        private readonly BlockingCollection<WorkItem> _writeQueue = new(boundedCapacity: CommandQueueCapacity);

        // N1：唤醒信号——Worker 阻塞等待时，任一条队列有新命令入队都要能立刻叫醒它；
        // 容量固定为 1：只表达"有活干了"，多余信号被吞掉即可（醒来后无条件排空两队列，不会漏命令）
        private readonly SemaphoreSlim _commandSignal = new(0, 1);

        private readonly CancellationTokenSource _cts = new();
        private readonly Thread _thread;

        // 状态与调度参数（跨线程读：State 用 Volatile；其余配置属性仅在建连前/加锁语义下赋值）
        private int _state = (int)ConnectionState.Disconnected;
        private volatile bool _autoConnect;          // 是否维持"应保持连接"意图（Connect 置 true，Disconnect/达到重连上限置 false）
        private volatile bool _autoReconnect = true; // 外部策略：断裂/建连失败后是否允许退避重连（Manager 按配置注入）
        private long _nextConnectTick;               // 下次允许发起连接的时刻（TickCount64，Volatile 访问）

        private int _reconnectAttempt;
        private long _lastSuccessTicks;              // 最近一次真实通信成功的 DateTime.Ticks（Interlocked 访问）
        private bool _disposed;

        // M3：主线程 Dispose 等待线程退出超时后置位——改由 Worker 线程在退出前自行释放连接对象，
        // 避免"后台线程仍在使用连接对象时主线程抢先 Dispose"（socket 层竞态 / 诡异报错）
        private volatile bool _disposeConnectionOnExit;

        // 最近一次通信故障消息：Worker 线程写、任意线程读。
        // 不经 Dispatcher，因此在 Application.Current == null 的宿主（控制台 / Windows 服务 / 单元测试）下依然可用——
        // 而 CommunicationConfig.LastError 的回写被 SafeDispatch 丢弃，那种形态下不可信。
        private volatile string? _lastError;

        #region 公共配置与事件

        /// <summary>连接当前状态（由本 Worker 独家维护，是唯一的真相源）</summary>
        public ConnectionState State => (ConnectionState)Volatile.Read(ref _state);

        public string ConnectionName => _connection.ConnectionName;

        /// <summary>是否处于已连接状态</summary>
        public bool IsConnected => State == ConnectionState.Connected && _connection.IsConnected;

        /// <summary>
        /// 最近一次通信故障消息（Worker 线程写入、可跨线程读，不经 Dispatcher）；连接成功后自动清空。
        /// <para>与 <c>CommunicationConfig.LastError</c> 的区别：后者回写被包在 SafeDispatch.BeginInvoke 里，
        /// 无 WPF 宿主时会被直接丢弃，故本属性才是任何宿主下都可靠的错误来源。</para>
        /// </summary>
        public string? LastError => _lastError;

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

        /// <summary>Connect 默认等待时长（ms）：未显式指定时不再无限等待（M2）</summary>
        public const int DefaultConnectTimeoutMs = 30000;

        /// <summary>
        /// 连接等待上限（ms）。即便调用方传 -1（旧语义"无限等待"）也封顶 5 分钟：
        /// Worker 在后台仍按退避持续重连，等待超时只影响本次调用结果，不改变重连意图（M2）
        /// </summary>
        private const int MaxConnectWaitMs = 300_000;

        /// <summary>最近一次真实通信成功时刻（轮询/读写任一成功都会刷新——"真心跳"依据）</summary>
        public DateTime LastCommunicationSuccessTime => new DateTime(Interlocked.Read(ref _lastSuccessTicks));

        // 调度器由 Manager 在重编译轮询计划时整体替换（引用赋值原子），Worker 线程每拍读取
        private volatile PollScheduler? _scheduler;

        /// <summary>
        /// <para>扫描组调度器（t5 批量规划器 + 多周期调度挂这里）。为 null 或 <see cref="PollScheduler.HasWork"/> 为 false 时不轮询。</para>
        /// <para>契约：<see cref="PollScheduler.Run"/> 返回 false = 通信级故障，Worker 判连接死亡并调度重连；
        /// 变量级错误（个别地址读失败）由调度器内部消化，不会返回 false。</para>
        /// </summary>
        public PollScheduler? PollScheduler
        {
            get => _scheduler;
            set => _scheduler = value;
        }

        // H1：轮询计划脏标记——Manager 增删变量时只置位（O(1)），
        // Worker 在下一轮轮询拍前触发重编译（O(N) 编译每批注册只做一次，替代旧的"每次注册全量重建"）
        private volatile bool _pollPlanDirty;

        /// <summary>标脏轮询计划（Manager 注册/注销变量时调用，O(1)；下一拍由 <see cref="PollPlanDirtyHandler"/> 消费）</summary>
        public void MarkPollPlanDirty() => _pollPlanDirty = true;

        /// <summary>清脏标记（Manager 重编译完成后调用，保证任何重建落点之后无残留脏标记）</summary>
        public void ClearPollPlanDirty() => _pollPlanDirty = false;

        /// <summary>
        /// 拍前重编译回调（Manager 在 ConfigureWorker 时挂接）：Worker 在轮询拍前发现脏标记时调用，
        /// 回调内部重编译并重挂 <see cref="PollScheduler"/>；回调抛异常时脏标记未清，下一拍自动重试
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
        public Task<bool> Connect(int timeoutMs = DefaultConnectTimeoutMs)
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

        /// <summary>在 Worker 线程执行"读/普通"委托并取回结果（通信失败时异常回抛给调用方）</summary>
        public Task<TResult> Invoke<TResult>(Func<ICommunicationConnection, TResult> func)
            => EnqueueCommand(_readQueue, func);

        /// <summary>
        /// 在 Worker 线程执行"写"委托并取回结果（N1：走写队列，优先于读命令与轮询执行）。
        /// 语义与 <see cref="Invoke"/> 完全一致，仅排队优先级不同。
        /// </summary>
        public Task<TResult> InvokeWrite<TResult>(Func<ICommunicationConnection, TResult> func)
            => EnqueueCommand(_writeQueue, func);

        /// <summary>
        /// 命令投递核心（N2）：带超时等待入队——队列满时先给 Worker 一点时间消化（默认 2s），
        /// 仍无法入队才判失败，避免"瞬时排队"被误报成通信故障。
        /// </summary>
        private Task<TResult> EnqueueCommand<TResult>(BlockingCollection<WorkItem> queue, Func<ICommunicationConnection, TResult> func)
        {
            ThrowIfDisposed();
            var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                if (!queue.TryAdd(new WorkItem
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
                }, CommandEnqueueTimeoutMs))
                {
                    tcs.TrySetException(new InvalidOperationException(
                        $"连接 {ConnectionName} 命令队列已满（等待 {CommandEnqueueTimeoutMs}ms 仍未消化）"));
                    return tcs.Task;
                }
            }
            catch (InvalidOperationException)
            {
                // Dispose 竞态：CompleteAdding 之后 TryAdd 会抛
                tcs.TrySetException(new ObjectDisposedException(nameof(ConnectionWorker)));
                return tcs.Task;
            }

            // 入队成功 → 唤醒 Worker（若它正阻塞等待）。已有待处理信号时 Release 会抛，吞掉即可：
            // 醒来后无条件排空两队列，多一次空转无害，少一次信号才会漏命令
            try { _commandSignal.Release(); }
            catch (SemaphoreFullException) { /* 已有信号待消费，无需重复通知 */ }

            return tcs.Task;
        }

        /// <summary>在 Worker 线程执行任意委托（需要回执，走读队列）</summary>
        public Task InvokeAsync(Action<ICommunicationConnection> action)
        {
            return Invoke(conn =>
            {
                action(conn);
                return true;
            });
        }

        /// <summary>投递写命令（真异步 B7：调用方拿 Task，不阻塞流程线程；N1：走写队列优先执行）</summary>
        public Task<bool> EnqueueWrite(string address, object value)
        {
            return InvokeWrite(conn =>
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

                    // 2) 等待命令：阻塞在唤醒信号上。睡多久由 ComputeWaitMs 决定——C1 之后是"距最近一个扫描组到期还有多久"
                    //    （封顶 200ms），空闲时 200ms 醒一次检查状态机。命令到达由 _commandSignal 立刻叫醒，不受这里影响。
                    //    返回值不参与判定——无论因超时还是因新命令 Release 醒来，都无条件排空两队列，避免漏命令
                    _commandSignal.Wait(ComputeWaitMs(), token);
                    DrainQueues();

                    // 3) 拍前消费"轮询计划脏"标记（H1）：必须独立于轮询分支——
                    //    "在线且从零注册第一个变量"时调度器为 null/无工作，进不了下面的轮询分支，脏标记会被饿死
                    if (State == ConnectionState.Connected && _pollPlanDirty)
                        PollPlanDirtyHandler?.Invoke();

                    // 4) 已连接 → 按各组到期情况驱动一拍轮询（调度器内部按周期升序跑完所有到期组）
                    if (State == ConnectionState.Connected && _scheduler?.ShouldRun() == true)
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

            // M3：主线程 Dispose 未能在超时内等到本线程退出 → 由本线程退出前释放连接对象
            // （"谁使用谁释放"：此刻本线程已不再触碰连接对象，释放是安全的；主线程那边不会重复释放）
            if (_disposeConnectionOnExit)
            {
                try { _connection.Disconnect(); } catch { /* 释放路径不抛 */ }
                try { _connection.Dispose(); } catch { /* 释放路径不抛 */ }
            }
        }

        private int ComputeWaitMs()
        {
            // C1：已连接且有轮询工作时，按"距最近一个扫描组到期还有多久"睡，而不是固定 10ms 空转。
            // 为什么现在敢这么改：旧实现的 10ms 里有一半理由是"好快点响应命令"，而命令唤醒已由
            // _commandSignal 接管（N1）——等待时长可以纯粹为轮询节拍服务了。
            // 下限 1ms：返回 0 会让 Wait 立即返回，万一出现"到期却没能跑"的边角场景就会变成死循环空转，
            //        留 1ms 兜底（对轮询精度无可感影响）；上限 MaxIdleWaitMs 见常量注释。
            if (State == ConnectionState.Connected && _scheduler is { HasWork: true } scheduler)
                return Math.Clamp(scheduler.MsUntilNextDue(), 1, MaxIdleWaitMs);

            if (_autoConnect && State != ConnectionState.Connected)
                return 100; // 重连中：小步快跑检查退避时刻
            return 200;
        }

        private void RunPollCycle()
        {
            try
            {
                bool ok = _scheduler!.Run(_connection);
                if (ok)
                {
                    TouchCommunicationSuccess();
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
                    _lastError = null;
                    TouchCommunicationSuccess();
                    _scheduler?.ResetAllDue(); // 立即允许首轮轮询
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
            _lastError = ex.Message;
            CommunicationError?.Invoke(ex);
            ScheduleReconnect();
        }

        /// <summary>已连接后通信断裂（轮询失败/异常）：断开 socket 并进入退避重连</summary>
        private void OnConnectionBroken(Exception ex)
        {
            _lastError = ex.Message;
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

        /// <summary>
        /// N1：每取一条命令前都**先看一眼写队列**——写优先；写队列空才取读命令。
        /// <para>为什么不是"先 while 抽干写队列、再 while 抽干读队列"：那样内层读循环会一次性把读队列抽干，
        /// 期间新到的写命令仍只能排在这些读命令后面，写优先形同虚设（C13-2 断言就是这么抓出来的）。
        /// 写成"写一条、读一条"的交替形式，既保证写优先，又保证写命令持续涌入时读命令不被饿死。</para>
        /// </summary>
        private void DrainQueues()
        {
            while (true)
            {
                if (_writeQueue.TryTake(out var w, 0)) { ExecuteItem(w); continue; }
                if (_readQueue.TryTake(out var r, 0)) { ExecuteItem(r); continue; }
                break;
            }
        }

        private void ReleasePendingItems()
        {
            while (_writeQueue.TryTake(out var w, 0))
                w.OnAbandon?.Invoke();
            while (_readQueue.TryTake(out var r, 0))
                r.OnAbandon?.Invoke();
        }

        private async Task<bool> WaitStateAsync(ConnectionState target, int millisecondsTimeout)
        {
            // M2：负数（含 -1"无限等待"）一律按上限封顶——旧实现 deadline=long.MaxValue 时，
            // 设备永久离线 + 无限重连会让 Task 永不完成，同步 .GetAwaiter().GetResult() 的线程永久挂起。
            // 封顶只影响"本次调用能否拿到成功结果"，不影响后台重连意图（超时返回 false，重连照常继续）
            long wait = millisecondsTimeout < 0 ? MaxConnectWaitMs : millisecondsTimeout;
            long deadline = Environment.TickCount64 + wait;
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
            _readQueue.CompleteAdding();
            _writeQueue.CompleteAdding();
            // 唤醒可能正阻塞在信号上的 Worker 线程，让它尽快看到取消并退出
            try { _commandSignal.Release(); }
            catch (SemaphoreFullException) { }
            catch (ObjectDisposedException) { }

            bool threadExited = true;
            try
            {
                if (!_thread.Join(3000))
                {
                    threadExited = false;
                    System.Diagnostics.Debug.WriteLine($"ConnectionWorker[{ConnectionName}] 线程未在 3s 内退出");
                }
            }
            catch (ThreadStateException)
            {
                // 线程尚未 Start：Join 对未启动线程抛的是 ThreadStateException（不是 InvalidOperationException）。
                // 这种 Worker 从没跑过，直接按"已退出"继续释放即可。
            }

            if (!threadExited)
            {
                // M3：线程仍活着（多因卡在建连超时等长 IO）。此刻释放 _connection/_queue/_cts 会让
                // 后台线程操作已释放对象（socket 层竞态、TryTake 抛 ObjectDisposedException）。
                // 处理：置标志，由线程退出前自行释放连接对象；队列/CTS 不释放（无句柄泄漏——
                // _queue 已 CompleteAdding 且 _disposed 已置位，Invoke 侧不会再投递新命令，线程读空即退出）
                _disposeConnectionOnExit = true;
                return;
            }

            try { _connection.Disconnect(); } catch { }
            _connection.Dispose();
            _readQueue.Dispose();
            _writeQueue.Dispose();
            _commandSignal.Dispose();
            _cts.Dispose();
        }
    }
}
