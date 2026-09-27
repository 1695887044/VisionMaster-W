using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Core.Interfaces
{
    /// <summary>
    /// 运动卡驱动基类：把"命令模式"的骨架一次性固化，厂商插件只写设备差异。
    ///
    /// 固化在这里的六件事（每一家卡都绕不开，各写一遍必然有的对有的错）：
    ///   1. <b>命令队列 + 紧急插队</b>。普通命令 FIFO，急停走专用槽位 —— 排队执行的急停不是急停。
    ///   2. <b>命令线程</b>（一条）：从队列取命令、执行、回填生命周期状态并发出完成信号。
    ///   3. <b>状态轮询线程</b>（一条）：周期刷新轴状态快照，并据此做报警/限位侦测与看门狗判定。
    ///   4. <b>状态机</b>（Closed/Connecting/Online/Alarm/SafeStopped）+ 变化通知。
    ///   5. <b>命令门禁</b>：状态、能力、轴号、软限位四道检查集中在 <see cref="CheckGateLocked"/>，
    ///      绝不由各驱动各写一遍（漏一处就是一次对着报警轴下发 Move）。
    ///   6. <b>心跳看门狗</b>：多久没问通就判失联并安全停机（网口卡没有推送通道，只能这样判）。
    ///
    /// 厂商插件要写的只有四件事：
    ///   <see cref="ConnectCore"/> / <see cref="DisconnectCore"/> /
    ///   <see cref="PollStatusCore"/> / <see cref="ExecuteCommandCore"/>，
    ///   外加在轮询里用 <see cref="UpdateAxisStatus"/> / <see cref="UpdateInput"/> 回填快照。
    ///
    /// 与 <see cref="CameraDeviceBase"/> 同住 Core.Interfaces 是刻意的：
    /// 驱动插件只需引用 Core.Interfaces，不必也不可能引用宿主 exe。
    /// </summary>
    public abstract class MotionDeviceBase : IMotionDevice
    {
        /// <summary>连接进行中的占位说明（用于判断驱动有没有在 ConnectCore 里改写它）</summary>
        private const string ConnectingDetail = "正在连接运动卡…";

        private readonly object _gate = new();

        /// <summary>普通命令队列（FIFO）</summary>
        private readonly LinkedList<MotionCommand> _queue = new();

        /// <summary>紧急命令槽位（急停专用，只保留最后一条 —— 连按两次急停没有意义）</summary>
        private MotionCommand? _emergency;

        /// <summary>命令线程的唤醒信号（有命令入队 / 需要退出时置位）</summary>
        private readonly ManualResetEventSlim _wake = new(false);

        private Thread? _commandThread;
        private Thread? _pollThread;
        private CancellationTokenSource? _cts;

        /// <summary>
        /// 急停专用的取消源：<see cref="EmergencyStop"/> 时 Cancel，用来**打断正在执行的那条命令**。
        ///
        /// 为什么必须有它（只清队列是不够的）：
        /// 命令线程是逐条执行命令的。若某条命令耗时很长（回零要几十秒、某条 Move 卡在等待），
        /// 只清空队列 + 插一个急停槽位，急停就要**等这条命令跑完**才轮得到 ——
        /// 那已经不是急停了。真实场景里恰恰是"回零卡住/轴在跑"的时候最需要急停。
        /// </summary>
        private CancellationTokenSource? _emergencyCts;

        /// <summary>是否有急停待处理（区分"被急停打断"与"设备断开/流程停止"两种取消）</summary>
        private volatile bool _emergencyPending;

        private MotionCardState _state = MotionCardState.Closed;
        private string _stateDetail = "未连接";
        private MotionCapabilities _capabilities = MotionCapabilities.Unknown;
        private MotionFault? _lastFault;
        private bool _isHomed;

        /// <summary>最近一次"问通了"的时刻（UTC Ticks）。跨线程读写，用 Volatile</summary>
        private long _lastAliveUtcTicks;

        /// <summary>状态快照缓存（轮询线程写，其它线程读）。读它不发起通信，见 IMotionDevice 注释</summary>
        private readonly ConcurrentDictionary<int, AxisStatus> _axisCache = new();
        private readonly ConcurrentDictionary<int, bool> _inputCache = new();

        /// <summary>每个轴"上一次已上报的致命信号"（去重：同一原因不重复刷故障）</summary>
        private readonly Dictionary<int, string> _lastCriticalByAxis = new();

        private int _pollFailStreak;
        private long _executed;
        private long _rejected;
        private long _faultCount;
        private bool _disposed;

        protected MotionDeviceBase(MotionDescriptor descriptor)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Descriptor.Params ??= new MotionParams();
        }

        #region 对外只读状态

        /// <inheritdoc />
        public MotionDescriptor Descriptor { get; }

        /// <inheritdoc />
        public MotionCardState State { get { lock (_gate) return _state; } }

        /// <inheritdoc />
        public string StateDetail { get { lock (_gate) return _stateDetail; } }

        /// <inheritdoc />
        public MotionCapabilities Capabilities { get { lock (_gate) return _capabilities; } }

        /// <summary>设备型号名。默认空串（虚拟卡等无需识别机型）；真实驱动应覆盖它</summary>
        public virtual string ModelName => string.Empty;

        /// <inheritdoc />
        public MotionFault? LastFault { get { lock (_gate) return _lastFault; } }

        /// <inheritdoc />
        public bool IsHomed { get { lock (_gate) return _isHomed; } }

        /// <inheritdoc />
        public long ExecutedCommandCount => Interlocked.Read(ref _executed);

        /// <inheritdoc />
        public long RejectedCommandCount => Interlocked.Read(ref _rejected);

        /// <inheritdoc />
        public long FaultCount => Interlocked.Read(ref _faultCount);

        /// <inheritdoc />
        public int PendingCommandCount
        {
            get { lock (_gate) return _queue.Count + (_emergency != null ? 1 : 0); }
        }

        /// <summary>可选日志（由宿主 MotionProvider 在创建后注入；为空则全部静默）</summary>
        public ILogService? Log { get; set; }

        /// <inheritdoc />
        public event EventHandler? StateChanged;

        /// <inheritdoc />
        public event EventHandler<MotionFault>? FaultOccurred;

        /// <summary>当前生效参数（内部用）</summary>
        protected MotionParams Params => Descriptor.Params;

        #endregion

        #region 子类要实现的差异点

        /// <summary>打开设备。返回 false 表示失败（失败原因请写进 <see cref="SetDetail"/>）</summary>
        protected abstract bool ConnectCore();

        /// <summary>关闭设备。实现里应吞掉异常（关闭失败不该让状态机卡住）</summary>
        protected abstract void DisconnectCore();

        /// <summary>
        /// 刷新状态快照：读轴状态、输入输出，并用 <see cref="UpdateAxisStatus"/> / <see cref="UpdateInput"/> 回填。
        /// 由轮询线程按 <see cref="MotionParams.PollIntervalMs"/> 周期调用；抛异常会被当作"一次轮询失败"。
        /// </summary>
        protected abstract void PollStatusCore(CancellationToken ct);

        /// <summary>
        /// 执行一条命令（在命令线程上调用，不在 UI 线程）。
        /// 返回执行结果；失败原因写进 <paramref name="error"/>（中文，会进日志与界面）。
        ///
        /// 注意：<b>只有 <see cref="MotionCommand.WaitsForCompletion"/> 的命令才应在这里等待完成</b>
        /// （回零、清报警是；Move 不是 —— Move 下发即返回，等到位由独立的等待步骤负责）。
        ///
        /// <b>取消约定</b>：等待过程中若发现 <paramref name="ct"/> 已取消，请**抛 OperationCanceledException**，
        /// 不要返回 <see cref="MotionCommandOutcome.Failed"/>。基类据此区分两件完全不同的事：
        ///   · 被急停 / 断开 / 流程停止打断 → 标记为"已取消"（预期行为，不报故障、不进安全停机）；
        ///   · 真的执行失败 → 报故障、分级、必要时安全停机。
        /// 返回 Failed 会让"人工按了急停"被误记成"设备故障"，现场排查时会被带偏。
        /// </summary>
        protected abstract MotionCommandOutcome ExecuteCommandCore(
            MotionCommand command, CancellationToken ct, out string error);

        /// <summary>
        /// 安全停止全部轴（断开连接、失联、急停收尾时调用）。
        /// 默认什么都不做 —— 但**真机驱动必须重写**：不写的话，失联时轴还在按最后的命令继续走。
        /// </summary>
        protected virtual void SafeStopCore() { }

        /// <summary>
        /// 把执行结果分级（决定"能自动恢复 / 需复位 / 需维修"）。
        /// 默认：超时 → 需人工复位（卡可能卡死了），失败 → 可恢复（多为通信问题）。
        /// 驱动遇到"伺服报警""限位"这类明确信号时应重写，给出更准的级别与建议。
        /// </summary>
        protected virtual MotionFault ClassifyOutcome(
            MotionCommandOutcome outcome, MotionCommand command, string error)
            => new()
            {
                Severity = outcome == MotionCommandOutcome.TimedOut
                    ? MotionFaultSeverity.RequiresReset
                    : MotionFaultSeverity.Recoverable,
                Message = string.IsNullOrWhiteSpace(error) ? command.Describe() : error,
                Suggestion = outcome == MotionCommandOutcome.TimedOut
                    ? "检查运动卡是否卡死/网线是否松动，必要时断电重启后重新回零"
                    : "检查卡与上位机的通信（网线/交换机/IP），恢复后可重试",
                Axis = command.LogicalAxis,
                CommandId = command.CommandId,
            };

        #endregion

        #region 给驱动回填数据的 protected 成员

        /// <summary>回填一个轴的状态快照（轮询线程调用）</summary>
        protected void UpdateAxisStatus(AxisStatus status)
        {
            if (status == null) return;
            status.Timestamp = DateTime.Now;
            _axisCache[status.PhysicalIndex] = status;
        }

        /// <summary>回填一个数字输入的状态（轮询线程调用）</summary>
        protected void UpdateInput(int port, bool value) => _inputCache[port] = value;

        /// <summary>回填能力描述（连接成功后调用一次；决定门禁与界面提示）</summary>
        protected void SetCapabilities(MotionCapabilities capabilities)
        {
            if (capabilities == null) return;
            lock (_gate) { _capabilities = capabilities; }

            // 能力到位后，把配置里没铺满的轴补上逻辑名（默认 A0/A1…，用户再按机构改名）
            if (capabilities.AxisCount > 0)
            {
                lock (_gate) { Descriptor.EnsureAxes(capabilities.AxisCount); }
            }
        }

        /// <summary>标记"已回零"（回零成功、或绝对编码器连接后发现位置可信时调用）</summary>
        protected void MarkHomed(bool homed)
        {
            lock (_gate) { _isHomed = homed; }
        }

        /// <summary>清空轴快照（断线时调用：旧的轴位置已经没有意义，留着会被误当成"当前位置"）</summary>
        protected void ClearSnapshots()
        {
            _axisCache.Clear();
            _inputCache.Clear();
            lock (_gate) { _lastCriticalByAxis.Clear(); }
        }

        /// <summary>供驱动在 ConnectCore / PollStatusCore 里写状态说明</summary>
        protected void SetDetail(string detail)
        {
            bool changed;
            lock (_gate) { changed = SetStateLocked(_state, detail); }
            if (changed) RaiseStateChanged();
        }

        /// <summary>
        /// 由驱动在判定"设备已掉线"时调用（如 SDK 返回连接错误）。
        /// 会让设备进入安全停机并清空队列 —— 轴还在动的判断交给驱动自己先停。
        /// </summary>
        protected void NotifyDeviceLost(string reason, MotionFaultSeverity severity = MotionFaultSeverity.Recoverable)
            => HandleLost(reason, severity);

        /// <summary>上报一次故障（驱动在轮询/执行里发现伺服报警、限位等时调用）</summary>
        protected void ReportFault(MotionFault fault)
        {
            if (fault == null) return;
            lock (_gate) { _lastFault = fault; }
            Interlocked.Increment(ref _faultCount);
            RaiseFault(fault);
            LogWarn($"[{Descriptor.Caption}] {fault}");
        }

        #endregion

        #region 连接 / 断开

        /// <inheritdoc />
        public bool Connect()
        {
            lock (_gate)
            {
                if (_disposed) return false;
                if (_state != MotionCardState.Closed) return true; // 幂等
                SetStateLocked(MotionCardState.Connecting, ConnectingDetail);
            }
            RaiseStateChanged();

            // ConnectCore 放锁外：网口卡建链可能要几百毫秒，持锁会把状态查询全堵死
            var ok = false;
            string? error = null;
            try
            {
                ok = ConnectCore();
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
            }

            lock (_gate)
            {
                if (ok)
                {
                    Volatile.Write(ref _lastAliveUtcTicks, DateTime.UtcNow.Ticks);
                    // 绝对编码器掉电不丢位置，重连后位置仍可信；增量式必须重新回零
                    _isHomed = _capabilities.SupportsAbsoluteEncoder ;

                    // 保留驱动在 ConnectCore 里写的具体说明（机型 / 地址 / 轴数与 IO 数）：
                    // 覆盖成一句"已连接"会把现场最需要的信息丢掉 —— 一张带 8 根轴的卡，
                    // 用户最想知道的就是"连上的是哪台、几轴"，这恰恰是驱动才知道的事。
                    var detail = string.IsNullOrWhiteSpace(_stateDetail) || _stateDetail == ConnectingDetail
                        ? "已连接"
                        : _stateDetail;
                    SetStateLocked(MotionCardState.Online, detail);
                }
                else
                {
                    _isHomed = false;
                    SetStateLocked(MotionCardState.Closed, $"连接失败：{error ?? "设备未给出原因"}");
                }
            }

            if (ok) StartThreads();
            RaiseStateChanged();
            if (!ok) LogWarn($"[{Descriptor.Caption}] 连接失败：{error ?? "设备未给出原因"}");
            return ok;
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            bool changed;
            lock (_gate)
            {
                if (_state == MotionCardState.Closed) return;
                SetStateLocked(MotionCardState.Connecting, "正在断开…");
                changed = true;
            }

            // 顺序很重要：先停线程（否则命令线程可能正在执行命令），再安全停机，最后关句柄
            StopThreads();

            try { SafeStopCore(); }
            catch (Exception ex) { LogWarn($"[{Descriptor.Caption}] 断开时安全停止异常（已忽略）：{ex.Message}"); }

            CancelQueueLocked("设备已断开");

            try { DisconnectCore(); }
            catch (Exception ex) { LogWarn($"[{Descriptor.Caption}] 关闭设备时异常（已忽略）：{ex.Message}"); }

            ClearSnapshots();

            lock (_gate)
            {
                _isHomed = false;
                changed = SetStateLocked(MotionCardState.Closed, "已断开");
            }
            if (changed) RaiseStateChanged();
        }

        /// <inheritdoc />
        public bool ClearAlarm(out string error)
        {
            error = string.Empty;

            if (State == MotionCardState.Closed)
            {
                error = "运动卡未连接";
                return false;
            }

            using var cmd = new MotionCommand
            {
                Kind = MotionCommandKind.ClearAlarm,
                WaitsForCompletion = true,
                Retryable = false,   // 清报警失败多半是"报警原因还在"，盲目重试没意义
                Timeout = TimeSpan.FromMilliseconds(Params.CommandTimeoutMs),
            };

            var result = Enqueue(cmd);
            if (result != MotionCommandResult.Accepted)
            {
                error = $"清报警命令被拒绝（{result}）";
                return false;
            }

            var timeout = cmd.Timeout > TimeSpan.Zero
                ? cmd.Timeout
                : TimeSpan.FromMilliseconds(Params.CommandTimeoutMs);

            if (!cmd.Completion.Wait(timeout))
            {
                error = "清报警超时";
                return false;
            }

            if (cmd.State != MotionCommandState.Done)
            {
                error = string.IsNullOrWhiteSpace(cmd.Error) ? $"清报警失败（{cmd.State}）" : cmd.Error;
                return false;
            }

            // 清报警成功：从报警态回到在线（若之前是安全停机则不自动回 —— 那需要重新连接）
            lock (_gate)
            {
                if (_state == MotionCardState.Alarm)
                    SetStateLocked(MotionCardState.Online, "报警已清除");
            }
            RaiseStateChanged();
            return true;
        }

        #endregion

        #region 命令入口

        /// <summary>
        /// 是否属于"打断性命令"：入队时立即打断**正在执行的那条**（但不清队列）。
        ///
        /// 为什么 Stop / Disable 必须算：
        ///   命令线程逐条执行，而回零这类命令要跑几十秒。若"停止"老老实实排在它后面，
        ///   等轮到它时轴早就自己走完了 —— 那样的"停止"毫无意义。
        ///   实测反馈的"点失能界面卡死"正是这个组合：回零在前没结束、失能排不进去，
        ///   而界面又在同步等它（同步等待那部分已改为异步，见 MotionDebugViewModel.SetEnable）。
        ///
        /// 与 <see cref="EmergencyStop"/> 的区别：急停是**更重**的路径 ——
        ///   清空整个队列 + 打断 + 停全部轴；这里只打断当前那一条，其它命令照旧排队。
        /// </summary>
        private static bool IsInterrupting(MotionCommandKind kind)
            => kind is MotionCommandKind.Stop or MotionCommandKind.Disable;

        /// <inheritdoc />
        public MotionCommandResult Enqueue(MotionCommand? command)
        {
            if (command == null)
            {
                Interlocked.Increment(ref _rejected);
                return MotionCommandResult.Rejected_InvalidArgument;
            }

            MotionCommandResult gate;
            string reason = string.Empty;
            bool interruptRequired = false;
            lock (_gate)
            {
                if (_disposed)
                {
                    Interlocked.Increment(ref _rejected);
                    return MotionCommandResult.Rejected_NotConnected;
                }

                gate = CheckGateLocked(command, out reason);
                if (gate != MotionCommandResult.Accepted)
                {
                    Interlocked.Increment(ref _rejected);
                }
                else if (_queue.Count >= Math.Max(1, Params.MaxQueueDepth))
                {
                    gate = MotionCommandResult.Rejected_QueueFull;
                    reason = $"命令队列已满（上限 {Params.MaxQueueDepth} 条）";
                    Interlocked.Increment(ref _rejected);
                }
                else
                {
                    _queue.AddLast(command);
                    interruptRequired = IsInterrupting(command.Kind);
                }
            }

            // 打断放在锁外：Cancel 会同步进入命令线程的取消回调，
            // 在锁内做会与状态回填抢同一把锁（等于自己等自己）
            if (interruptRequired)
            {
                try { _emergencyCts?.Cancel(); }
                catch (ObjectDisposedException) { /* 正在重连/释放，忽略 */ }
            }

            if (gate != MotionCommandResult.Accepted)
            {
                // 被拒绝也要留痕：软限位拒绝属于 Notice（业务上正常），其余属于配置/状态问题
                var fault = new MotionFault
                {
                    Severity = gate == MotionCommandResult.Rejected_InvalidArgument && reason.Contains("软限位")
                        ? MotionFaultSeverity.Notice
                        : MotionFaultSeverity.Recoverable,
                    Message = $"命令被拒绝：{command.Describe()}",
                    Suggestion = reason,
                    Axis = command.LogicalAxis,
                    CommandId = command.CommandId,
                };
                lock (_gate) { _lastFault = fault; }
                RaiseFault(fault);
                LogWarn($"[{Descriptor.Caption}] {fault.Message} —— {reason}");
                return gate;
            }

            _wake.Set();
            return MotionCommandResult.Accepted;
        }

        /// <inheritdoc />
        public MotionCommandResult EmergencyStop()
        {
            var stop = new MotionCommand
            {
                Kind = MotionCommandKind.Stop,
                PhysicalAxis = -1,          // -1 = 全部轴（约定：驱动见到 -1 就停所有轴）
                StopMode = 3,               // 3 = 立即停（急停场合不用减速停）
                Retryable = false,
                WaitsForCompletion = true,
            };

            lock (_gate)
            {
                if (_disposed) return MotionCommandResult.Rejected_NotConnected;
                if (_state != MotionCardState.Online && _state != MotionCardState.Alarm)
                    return MotionCommandResult.Rejected_NotConnected;

                // 清空普通队列：急停之后还去执行"之前排队的 Move"是危险的
                foreach (var c in _queue)
                {
                    c.State = MotionCommandState.Canceled;
                    c.Error = "被急停清除";
                    c.CompletedAt = DateTime.Now;
                    try { c.Completion.Set(); } catch { }
                }
                _queue.Clear();

                _emergency = stop;          // 插队

                // 打断正在执行的那条命令（否则它跑完之前，急停根本轮不到）
                _emergencyPending = true;
                try { _emergencyCts?.Cancel(); } catch (ObjectDisposedException) { }
                // 换一个新的给"急停之后"的命令用：旧的那个已经被 Cancel，不能复用
                _emergencyCts = new CancellationTokenSource();
            }

            _wake.Set();
            LogWarn($"[{Descriptor.Caption}] 收到急停：已清空待执行命令并插队停止");
            return MotionCommandResult.Accepted;
        }

        /// <inheritdoc />
        public void CancelAll(string reason)
        {
            lock (_gate) { CancelQueueLocked(reason); }

            try { SafeStopCore(); }
            catch (Exception ex) { LogWarn($"[{Descriptor.Caption}] 取消时安全停止异常（已忽略）：{ex.Message}"); }

            LogInfo($"[{Descriptor.Caption}] 已取消全部命令（{reason}）");
        }

        /// <inheritdoc />
        public void NotifyAlive()
        {
            Volatile.Write(ref _lastAliveUtcTicks, DateTime.UtcNow.Ticks);
            _pollFailStreak = 0;
        }

        /// <inheritdoc />
        public AxisStatus? GetAxisStatus(int physicalIndex)
            => _axisCache.TryGetValue(physicalIndex, out var s) ? s : null;

        /// <inheritdoc />
        public bool ReadInput(int port)
            => _inputCache.TryGetValue(port, out var v) && v;

        /// <inheritdoc />
        public void ResetCounters()
        {
            Interlocked.Exchange(ref _executed, 0);
            Interlocked.Exchange(ref _rejected, 0);
            Interlocked.Exchange(ref _faultCount, 0);
        }

        #endregion

        #region 门禁（安全与配置校验的唯一落点）

        /// <summary>
        /// 命令门禁：状态、能力、轴号、软限位。
        ///
        /// 集中在这里而不是交给驱动，理由很直接：这四条检查**每一家卡都要做**，
        /// 交给驱动等于让每个作者各写一遍 —— 必然有人漏掉软限位或状态判断，
        /// 而漏掉的后果是"对着报警中的轴下发运动命令"，真机上就是撞机。
        /// </summary>
        private MotionCommandResult CheckGateLocked(MotionCommand command, out string reason)
        {
            reason = string.Empty;

            // 清报警：报警态与在线态都允许（报警态下它正是唯一该被允许的命令）
            if (command.Kind == MotionCommandKind.ClearAlarm)
            {
                if (_state == MotionCardState.Online || _state == MotionCardState.Alarm) return MotionCommandResult.Accepted;
                reason = _state == MotionCardState.Closed ? "运动卡未连接" : "当前状态不允许清除报警";
                return MotionCommandResult.Rejected_NotConnected;
            }

            // 其余命令一律要求"在线且能动"
            if (_state != MotionCardState.Online)
            {
                reason = _state switch
                {
                    MotionCardState.Closed => "运动卡未连接",
                    MotionCardState.Connecting => "运动卡正在连接/断开，请稍候",
                    MotionCardState.Alarm => "运动卡处于报警状态，请先清除报警",
                    MotionCardState.SafeStopped => "运动卡已安全停机，请重新连接后再操作",
                    _ => "运动卡当前状态不接受命令",
                };
                return MotionCommandResult.Rejected_NotConnected;
            }

            // 轴号有效性（含"配置了 3 号轴但卡只有 2 个轴"这类配置错误）
            if (command.Kind is MotionCommandKind.MoveAbsolute or MotionCommandKind.MoveRelative
                or MotionCommandKind.Home or MotionCommandKind.Enable or MotionCommandKind.Disable)
            {
                if (!_capabilities.IsAxisValid(command.PhysicalAxis))
                {
                    reason = _capabilities.AxisCount <= 0
                        ? $"轴号 {command.PhysicalAxis} 无法校验（能力未知）"
                        : $"轴号 {command.PhysicalAxis} 超出范围（本卡 {_capabilities.AxisCount} 轴）";
                    return MotionCommandResult.Rejected_InvalidArgument;
                }
            }

            // Stop 允许 PhysicalAxis = -1（全部轴）
            if (command.Kind == MotionCommandKind.Stop && command.PhysicalAxis >= 0
                && !_capabilities.IsAxisValid(command.PhysicalAxis))
            {
                reason = $"轴号 {command.PhysicalAxis} 超出范围";
                return MotionCommandResult.Rejected_InvalidArgument;
            }

            if (command.Kind == MotionCommandKind.Home && !_capabilities.SupportsHomeMode(command.HomeMode))
            {
                reason = $"该卡不支持回零方式「{command.HomeMode}」";
                return MotionCommandResult.Rejected_NotSupported;
            }

            if (command.Kind == MotionCommandKind.SetOutput && !_capabilities.IsOutputValid(command.IoPort))
            {
                reason = _capabilities.DigitalOutputCount <= 0
                    ? $"输出点 {command.IoPort} 无法校验（能力未知）"
                    : $"输出点 {command.IoPort} 超出范围（本卡 {_capabilities.DigitalOutputCount} 点）";
                return MotionCommandResult.Rejected_InvalidArgument;
            }

            // 软限位：软件层的最后一道防线（硬限位在卡上，由能力位表达）
            if (command.Kind == MotionCommandKind.MoveAbsolute)
            {
                var mapping = Descriptor.FindAxis(command.PhysicalAxis);
                if (mapping != null && mapping.Enabled && !mapping.IsWithinSoftLimit(command.TargetMm))
                {
                    reason = $"目标 {command.TargetMm:F3} mm 超出软限位 [{mapping.SoftLimitMinMm:F1}, {mapping.SoftLimitMaxMm:F1}] mm";
                    return MotionCommandResult.Rejected_InvalidArgument;
                }
            }

            return MotionCommandResult.Accepted;
        }

        #endregion

        #region 命令线程

        private void StartThreads()
        {
            lock (_gate)
            {
                if (_disposed) return;

                _cts?.Dispose();
                var cts = new CancellationTokenSource();
                _cts = cts;

                _emergencyCts?.Dispose();
                _emergencyCts = new CancellationTokenSource();
                _emergencyPending = false;

                _commandThread = new Thread(() => CommandLoop(cts.Token))
                {
                    IsBackground = true,
                    Name = $"MotionCmd-{Descriptor.Caption}",
                };
                _pollThread = new Thread(() => PollLoop(cts.Token))
                {
                    IsBackground = true,
                    Name = $"MotionPoll-{Descriptor.Caption}",
                };

                _commandThread.Start();
                _pollThread.Start();
            }
        }

        private void StopThreads()
        {
            Thread? command, poll;
            CancellationTokenSource? cts;

            lock (_gate)
            {
                cts = _cts;
                _cts = null;
                command = _commandThread;
                poll = _pollThread;
                _commandThread = null;
                _pollThread = null;
            }

            try { cts?.Cancel(); } catch { /* 已释放 */ }
            _wake.Set(); // 唤醒可能在等待的命令线程

            // Join 带超时：线程卡在 SDK 调用里时不能把调用方（可能是 UI 线程）拖死
            try { command?.Join(2000); } catch { }
            try { poll?.Join(2000); } catch { }

            try { cts?.Dispose(); } catch { }
            _wake.Reset();
        }

        private void CommandLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                MotionCommand? command;
                lock (_gate) { command = TakeNextLocked(); }

                if (command == null)
                {
                    try { _wake.Wait(50, ct); }
                    catch (OperationCanceledException) { break; }
                    catch (ObjectDisposedException) { break; }
                    _wake.Reset();
                    continue;
                }

                ExecuteOne(command, ct);
            }
        }

        /// <summary>取下一条要执行的命令（调用方须持 _gate）：紧急命令优先，其次 FIFO</summary>
        private MotionCommand? TakeNextLocked()
        {
            if (_emergency != null)
            {
                var emergency = _emergency;
                _emergency = null;
                // 这一刻起"急停打断"阶段结束（被打断的那条命令已经在上一轮收尾），
                // 后面新来的命令失败就该按正常故障处理了
                _emergencyPending = false;
                return emergency;
            }

            var first = _queue.First;
            if (first == null) return null;
            _queue.RemoveFirst();
            return first.Value;
        }

        private void ExecuteOne(MotionCommand command, CancellationToken ct)
        {
            if (command.State == MotionCommandState.Canceled) return;

            // 命令可能在队列里等了一会儿，期间状态可能变差（报警、断开）——执行前再校验一次
            string gateReason;
            bool allowed;
            lock (_gate) { allowed = CheckGateLocked(command, out gateReason) == MotionCommandResult.Accepted; }

            if (!allowed)
            {
                CompleteCommand(command, MotionCommandState.Canceled, $"执行前校验未通过：{gateReason}");
                return;
            }

            command.State = MotionCommandState.Executing;

            // 把"设备令牌"与"急停令牌"链接起来交给驱动：
            // 驱动只要尊重传入的 ct（等待循环用 ct.WaitHandle 之类），就同时获得了
            // "设备断开/应用退出"与"急停打断"两种中断能力 —— 不必自己区分它们。
            CancellationTokenSource? linked = null;
            try
            {
                CancellationToken emergencyToken;
                lock (_gate) { emergencyToken = _emergencyCts?.Token ?? ct; }
                linked = CancellationTokenSource.CreateLinkedTokenSource(ct, emergencyToken);
            }
            catch (ObjectDisposedException)
            {
                // 令牌刚被释放（正在断开）：直接用设备令牌，后续 catch 会按取消处理
            }

            string error = string.Empty;
            MotionCommandOutcome outcome;
            try
            {
                outcome = ExecuteCommandCore(command, linked?.Token ?? ct, out error);
            }
            catch (OperationCanceledException)
            {
                // 两种取消必须分开表述：被急停打断是**预期**行为（紧接着就会执行插入的停止命令），
                // 不能记成失败，更不能因此进安全停机 —— 那会把"人工急停"误报成设备故障。
                CompleteCommand(command, MotionCommandState.Canceled,
                    _emergencyPending ? "被急停中断" : "已取消（设备断开或流程中断）");
                return;
            }
            catch (Exception ex)
            {
                outcome = MotionCommandOutcome.Failed;
                error = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                linked?.Dispose();
            }

            var finalState = outcome switch
            {
                MotionCommandOutcome.Done => MotionCommandState.Done,
                MotionCommandOutcome.TimedOut => MotionCommandState.TimedOut,
                MotionCommandOutcome.NotSupported => MotionCommandState.Failed,
                _ => MotionCommandState.Failed,
            };

            // 先落副作用，**再**发完成信号。
            // 完成信号的语义是"这条命令的一切都结束了"；顺序反过来的话，
            // 等待方（流程步骤 / 断言 / 界面）被唤醒时会读到
            // "命令已完成、但 IsHomed 仍为 false"这种中间态 ——
            // 而"回零完成后位置才可信"恰恰是紧接着就要用到的结论。
            if (outcome == MotionCommandOutcome.Done && command.Kind == MotionCommandKind.Home)
                MarkHomed(true);

            CompleteCommand(command, finalState, error);

            if (outcome == MotionCommandOutcome.Done) return;

            // 急停正在打断：这条命令的失败是预期内的（紧接着执行插入的停止命令），
            // 不报故障、也不进安全停机 —— 否则"人工急停"会被记成一次设备故障。
            if (_emergencyPending)
            {
                LogInfo($"[{Descriptor.Caption}] {command.Describe()} → 因急停提前结束");
                return;
            }

            // 失败/超时：分级上报（驱动可用 ClassifyOutcome 给出更准的级别与建议）
            ReportFault(ClassifyOutcome(outcome, command, error));

            // 运动类命令失败后卡在什么状态不知道，退到安全停机并要求人工确认，
            // 好过继续接受下一条命令（那等于在不知道轴在哪的情况下继续走）
            if (outcome is MotionCommandOutcome.Failed or MotionCommandOutcome.TimedOut)
            {
                bool changed;
                lock (_gate)
                {
                    CancelQueueLocked("上一条命令失败");
                    changed = SetStateLocked(MotionCardState.SafeStopped, $"命令失败：{error}");
                }
                if (changed) RaiseStateChanged();

                try { SafeStopCore(); }
                catch (Exception ex) { LogWarn($"[{Descriptor.Caption}] 失败后安全停止异常（已忽略）：{ex.Message}"); }
            }
        }

        private void CompleteCommand(MotionCommand command, MotionCommandState state, string error)
        {
            command.State = state;
            command.Error = error ?? string.Empty;
            command.CompletedAt = DateTime.Now;

            if (state == MotionCommandState.Done) Interlocked.Increment(ref _executed);

            try { command.Completion.Set(); } catch (ObjectDisposedException) { /* 调用方已释放 */ }

            var tail = string.IsNullOrWhiteSpace(command.Error) ? string.Empty : $"：{command.Error}";
            if (state == MotionCommandState.Done) LogInfo($"[{Descriptor.Caption}] {command.Describe()} → 完成");
            else LogWarn($"[{Descriptor.Caption}] {command.Describe()} → {state}{tail}");
        }

        #endregion

        #region 轮询线程与安全侦测

        private void PollLoop(CancellationToken ct)
        {
            var interval = Math.Clamp(Params.PollIntervalMs, 1, 1000);

            while (!ct.IsCancellationRequested)
            {
                var state = State;
                if (state == MotionCardState.Online || state == MotionCardState.Alarm)
                {
                    try
                    {
                        PollStatusCore(ct);
                        NotifyAlive();          // 问通了 = 活着
                        DetectCriticalSignals();
                        DetectWatchdog();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        // 单次抖动不值得报警；连续失败才算"可能掉线"
                        _pollFailStreak++;
                        if (_pollFailStreak >= 3)
                        {
                            HandleLost(
                                $"状态轮询连续失败 {_pollFailStreak} 次：{ex.Message}",
                                MotionFaultSeverity.Recoverable);
                        }
                    }
                }

                try { ct.WaitHandle.WaitOne(interval); }
                catch { break; }
            }
        }

        /// <summary>
        /// 侦测轴上的致命信号（急停 / 限位 / 伺服报警）。
        ///
        /// 为什么由基类做而不是驱动：这是"安全"而不是"设备差异"。
        /// 各家 SDK 的报警位分布不同（由驱动在 PollStatusCore 里解析成 AxisStatus.Alarm），
        /// 但**"报警之后该做什么"必须一致** —— 停机、退到报警态、上报故障，一处也不能少。
        /// </summary>
        private void DetectCriticalSignals()
        {
            foreach (var kv in _axisCache)
            {
                var status = kv.Value;
                if (!status.HasCriticalSignal) continue;

                var mapping = Descriptor.FindAxis(status.PhysicalIndex);
                var axisName = mapping?.LogicalName ?? $"轴{status.PhysicalIndex}";

                var reason = status.EmergencyStop ? "急停输入被触发"
                    : status.PositiveLimit ? "正限位被触发"
                    : status.NegativeLimit ? "负限位被触发"
                    : status.Alarm ? (string.IsNullOrWhiteSpace(status.AlarmMessage) ? "轴报警" : status.AlarmMessage)
                    : string.Empty;

                if (string.IsNullOrEmpty(reason)) continue;

                // 去重：同一个轴、同一个原因只报一次（否则 10ms 轮询会把日志刷爆）
                lock (_gate)
                {
                    if (_lastCriticalByAxis.TryGetValue(status.PhysicalIndex, out var last) && last == reason) continue;
                    _lastCriticalByAxis[status.PhysicalIndex] = reason;
                }

                if (status.EmergencyStop || status.PositiveLimit || status.NegativeLimit)
                {
                    // 硬信号：立刻停（不排队）
                    EmergencyStop();

                    bool changed;
                    lock (_gate) { changed = SetStateLocked(MotionCardState.Alarm, $"{axisName}：{reason}"); }
                    if (changed) RaiseStateChanged();

                    ReportFault(new MotionFault
                    {
                        Severity = MotionFaultSeverity.RequiresReset,
                        Message = $"{axisName}：{reason}",
                        Suggestion = "排除触发原因（撞到限位/急停被按下）后，先清除报警再重新回零",
                        Axis = axisName,
                    });
                }
                else if (status.Alarm)
                {
                    bool changed;
                    lock (_gate) { changed = SetStateLocked(MotionCardState.Alarm, $"{axisName}：{reason}"); }
                    if (changed) RaiseStateChanged();

                    ReportFault(new MotionFault
                    {
                        Severity = MotionFaultSeverity.RequiresReset,
                        Message = $"{axisName}：{reason}",
                        Suggestion = "检查驱动器报警原因（过流/过载/断线），复位伺服后点「清除报警」",
                        Axis = axisName,
                    });
                }
            }

            // 信号恢复后清掉去重记录，让下次再触发还能报出来
            lock (_gate)
            {
                if (_lastCriticalByAxis.Count == 0) return;
                var healthy = new List<int>();
                foreach (var kv in _lastCriticalByAxis)
                {
                    if (!_axisCache.TryGetValue(kv.Key, out var s) || !s.HasCriticalSignal)
                        healthy.Add(kv.Key);
                }
                foreach (var index in healthy) _lastCriticalByAxis.Remove(index);
            }
        }

        private void DetectWatchdog()
        {
            var last = Volatile.Read(ref _lastAliveUtcTicks);
            if (last == 0) return;

            var elapsedMs = (DateTime.UtcNow.Ticks - last) / TimeSpan.TicksPerMillisecond;
            var timeout = Math.Max(200, Params.WatchdogTimeoutMs);
            if (elapsedMs <= timeout) return;

            HandleLost(
                $"已 {elapsedMs / 1000.0:F1}s 未收到状态响应（看门狗超时 {timeout}ms）",
                MotionFaultSeverity.RequiresReset);
        }

        /// <summary>
        /// 判失联：清空队列 → 安全停机 → 退到 SafeStopped。
        ///
        /// 为什么不是简单回 Online 或只记日志：失联时轴可能还在按最后一条命令继续走，
        /// 而软件已经不知道它的位置了。此时**唯一安全的动作是停下来并要求人工确认**，
        /// 绝不能"等它自己好"—— 增量式编码器还要额外要求重新回零（位置已不可信）。
        /// </summary>
        private void HandleLost(string reason, MotionFaultSeverity severity)
        {
            bool changed;
            lock (_gate)
            {
                if (_disposed || _state == MotionCardState.Closed) return;
                if (_state == MotionCardState.SafeStopped && (_stateDetail?.Contains(reason) ?? false)) return;

                CancelQueueLocked(reason);

                // 增量式编码器：位置已不可信，必须重新回零；绝对式可保留
                if (!_capabilities.SupportsAbsoluteEncoder) _isHomed = false;

                changed = SetStateLocked(MotionCardState.SafeStopped, $"{reason}；已安全停机");
            }

            try { SafeStopCore(); }
            catch (Exception ex) { LogWarn($"[{Descriptor.Caption}] 安全停止时异常（已忽略）：{ex.Message}"); }

            if (changed) RaiseStateChanged();

            ReportFault(new MotionFault
            {
                Severity = severity,
                Message = reason,
                Suggestion = _capabilities.SupportsAbsoluteEncoder
                    ? "检查网线/电源与卡是否死机，重新连接后即可继续"
                    : "检查网线/电源与卡是否死机；增量编码器重新连接后**必须重新回零**再运动",
            });
        }

        /// <summary>清空队列并把未完成的命令标为已取消（调用方须持 _gate）</summary>
        private void CancelQueueLocked(string reason)
        {
            foreach (var command in _queue)
            {
                command.State = MotionCommandState.Canceled;
                command.Error = reason;
                command.CompletedAt = DateTime.Now;
                try { command.Completion.Set(); } catch { }
            }
            _queue.Clear();

            if (_emergency != null)
            {
                _emergency.State = MotionCommandState.Canceled;
                _emergency.Error = reason;
                _emergency.CompletedAt = DateTime.Now;
                try { _emergency.Completion.Set(); } catch { }
                _emergency = null;
            }
        }

        #endregion

        #region 状态机与通知

        /// <summary>改状态（调用方须持 _gate）。返回是否真的变了，便于在锁外决定要不要发通知</summary>
        private bool SetStateLocked(MotionCardState state, string detail)
        {
            var next = detail ?? string.Empty;
            if (_state == state && string.Equals(_stateDetail, next, StringComparison.Ordinal)) return false;

            _state = state;
            _stateDetail = next;
            return true;
        }

        /// <summary>在锁外触发，订阅方可以安全地回调设备上的只读查询</summary>
        private void RaiseStateChanged()
        {
            try { StateChanged?.Invoke(this, EventArgs.Empty); }
            catch { /* 订阅方（界面）的异常绝不能影响设备内部状态机 */ }
        }

        private void RaiseFault(MotionFault fault)
        {
            try { FaultOccurred?.Invoke(this, fault); }
            catch { /* 同上 */ }
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

            StopThreads();

            try { SafeStopCore(); }
            catch { /* 释放路径不再上抛 */ }

            lock (_gate)
            {
                CancelQueueLocked("设备已释放");
                SetStateLocked(MotionCardState.Closed, "已释放");
            }

            try { DisconnectCore(); }
            catch { /* 同上 */ }

            _axisCache.Clear();
            _inputCache.Clear();

            try { _wake.Dispose(); } catch { }
        }

        #endregion
    }
}
