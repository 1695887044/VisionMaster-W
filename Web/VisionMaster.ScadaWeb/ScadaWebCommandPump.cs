using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Threading;
using Core.Interfaces;

namespace VisionMaster.ScadaWeb
{
    /// <summary>
    /// Web 命令泵：<b>一个全局有界队列 + 单消费者 + 逐条投到 UI Dispatcher</b>（方案 §4.3）。
    ///
    /// <b>为什么必须"单读、单执行"</b>
    /// ---------
    /// 命令的副作用是领域状态（写变量、启停流程、让轴动、确认报警）。两个命令并发执行时，
    /// "先写 10 再写 20"与"先写 20 再写 10"的最终值不同，而审计文件按写入顺序落盘——
    /// 并发之后审计里的顺序与真实生效顺序<b>可能不一致</b>，事后复盘就会得出相反结论。
    /// 一个队列一个消费者，命令之间天然全序：审计顺序 = 执行顺序。
    ///
    /// <b>为什么消费者必须跑在"没有 SynchronizationContext"的线程上</b>
    /// ---------
    /// 若把 <c>await foreach</c> 起在 UI 线程，它的续体会被 <c>SynchronizationContext</c>
    /// 投回 UI 线程——泵就退化成"UI 线程上的一个循环"，网络线程写队列时 UI 线程在空转等待，
    /// 而真正的执行又要在 UI 线程排队，两层排队叠加。这里用 <see cref="Task.Run(Func{Task})"/>
    /// 启动：线程池线程没有同步上下文，续体留在池上，只有"执行这一条"被显式投到 Dispatcher。
    ///
    /// <b>背压：满时显式回 <c>E_BUSY</c>，绝不静默丢弃</b>
    /// ---------
    /// 全局容量 <see cref="GlobalCapacity"/>，每会话容量 <see cref="DefaultSessionCapacity"/>（可配 16~64）。
    /// 满了就 <see cref="Enqueue(ScadaWebSession, ScadaWebCommand, out ScadaWebCommandResult?)"/> 返回 false，
    /// 调用方回 <c>E_BUSY</c>。禁用 <c>DropWrite</c>/<c>DropOldest</c>：那两种模式下
    /// <c>TryWrite</c> 照样返回 true，客户端以为命令进了队列，实际被丢掉——
    /// 方案 §16.4 实测"容量 4 写 100 条，TryWrite 成功 100 次、实际入队 4 条"。
    ///
    /// <b>异常隔离</b>
    /// ---------
    /// 单条命令的异常（含 Dispatcher 已关闭）一律转成 <c>E_INTERNAL</c> 结果并记日志，
    /// <b>循环不退出</b>。一条坏命令把泵打死，表现是"之后所有浏览器都点不动"，而根因只是一次
    /// 空引用——这是可选模块最不该带下去的故障。
    /// </summary>
    public sealed class ScadaWebCommandPump : IDisposable
    {
        /// <summary>全局队列容量（所有会话合计）。</summary>
        public const int GlobalCapacity = 1024;

        /// <summary>每会话入站队列默认容量（与 <see cref="VisionMaster.Models.ScadaWebSettings.DefaultSessionInboundQueueCapacity"/> 同源）</summary>
        public const int DefaultSessionCapacity = VisionMaster.Models.ScadaWebSettings.DefaultSessionInboundQueueCapacity;

        /// <summary>单条命令在 UI 线程上超过这个时长记 Warn（方案 §4.3 规则 4）</summary>
        private const int SlowCommandMs = 8;

        /// <summary>命令在队列里等待超过这个时长记 Warn（"排队太久"通常是 UI 线程被别的活占住了）</summary>
        private const int SlowQueueMs = 2000;

        private readonly Channel<ScadaWebCommand> _channel;
        private readonly Func<ScadaWebCommand, ScadaWebCommandResult> _handler;
        private readonly Dispatcher? _uiDispatcher;
        private readonly ILogService _log;
        private readonly int _sessionCapacity;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _worker;

        /// <summary>每个会话"已入队但还没被消费者取走"的条数（键 = <see cref="ScadaWebSession.SessionId"/>）</summary>
        private readonly ConcurrentDictionary<string, int> _pendingBySession = new(StringComparer.Ordinal);

        /// <summary>pending 计数的读改写锁（见 <see cref="Enqueue"/> 与 <see cref="ReleasePending"/>）</summary>
        private readonly object _pendingGate = new();

        /// <summary>
        /// 每会话"正在 UI 线程执行"的条数。45 秒判死用它豁免长命令：
        /// 命令已经出队（pending=0）但还在执行时，会话不能被当成死连接回收。
        /// </summary>
        private readonly ConcurrentDictionary<string, int> _executingBySession = new(StringComparer.Ordinal);

        private long _processed;
        private int _stopped;

        /// <param name="handler">执行一条命令（通常是 <see cref="ScadaWebCommandRouter.Handle"/>）</param>
        /// <param name="log">日志</param>
        /// <param name="uiDispatcher">
        /// 宿主 UI 线程的 Dispatcher；<c>null</c> = 直接在当前（线程池）线程执行（断言/探针宿主）。
        /// 产品路径必须传——领域模型是单线程语义。
        /// </param>
        /// <param name="sessionCapacity">每会话入站队列容量（夹到 1~<see cref="GlobalCapacity"/>）</param>
        public ScadaWebCommandPump(
            Func<ScadaWebCommand, ScadaWebCommandResult> handler,
            ILogService log,
            Dispatcher? uiDispatcher = null,
            int sessionCapacity = DefaultSessionCapacity)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _uiDispatcher = uiDispatcher;
            _sessionCapacity = Math.Clamp(sessionCapacity, 1, GlobalCapacity);

            _channel = Channel.CreateBounded<ScadaWebCommand>(new BoundedChannelOptions(GlobalCapacity)
            {
                SingleReader = true,     // 一个消费者 = 命令全序（类注释）
                SingleWriter = false,    // 每条 WS 连接一个写者
                // 满时让 TryWrite 返回 false：调用方据此回 E_BUSY。
                // 绝不能改成 DropWrite / DropOldest——那会让 TryWrite 恒为 true（方案 §16.4）。
                FullMode = BoundedChannelFullMode.Wait
            });

            _worker = Task.Run(RunAsync);
        }

        /// <summary>
        /// 一条命令执行完（成功或失败）后触发。宿主在这里把结果序列化成 <c>ack</c> / <c>error</c>
        /// 发给发起会话。
        ///
        /// 处理器异常只记日志、不向上抛：回执通道坏掉不该把泵带下去。
        /// </summary>
        public event Action<ScadaWebCommand, ScadaWebCommandResult>? Completed;

        /// <summary>已执行的命令条数（诊断）</summary>
        public long ProcessedCount => Interlocked.Read(ref _processed);

        /// <summary>当前队列里等待的条数（诊断；<c>ChannelReader.Count</c> 对有界通道是精确值）</summary>
        public int PendingCount => _channel.Reader.Count;

        /// <summary>本泵的每会话容量（诊断 / 断言核对）</summary>
        public int SessionCapacity => _sessionCapacity;

        /// <summary>
        /// 该会话是否还有命令在排队或正在执行。45 秒判死的豁免判据：
        /// 有活干就不算死连接，哪怕这 45 秒里一帧都没有。
        /// </summary>
        public bool IsSessionBusy(string? sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return false;

            if (_pendingBySession.TryGetValue(sessionId, out var pending) && pending > 0)
                return true;

            return _executingBySession.TryGetValue(sessionId, out var executing) && executing > 0;
        }

        /// <summary>入队一条命令（不需要失败原因时用这个重载）</summary>
        public bool Enqueue(ScadaWebSession session, ScadaWebCommand command)
            => Enqueue(session, command, out _);

        /// <summary>
        /// 入队一条命令。返回 false 时 <paramref name="rejection"/> 是一条现成的
        /// <c>E_BUSY</c> 结果（调用方直接回给客户端即可，不必自己拼文案）。
        /// </summary>
        public bool Enqueue(
            ScadaWebSession session,
            ScadaWebCommand command,
            out ScadaWebCommandResult? rejection)
        {
            rejection = null;

            if (session == null || command == null)
            {
                rejection = ScadaWebCommandResult.Fail(
                    ScadaWebErrorCodes.BadRequest,
                    "命令或会话为空");
                return false;
            }

            if (Volatile.Read(ref _stopped) != 0)
            {
                rejection = ScadaWebCommandResult.Fail(
                    ScadaWebErrorCodes.Busy,
                    "命令泵已停止（服务正在关闭）");
                return false;
            }

            // 命令必须自带会话：名额按入队会话加一，出队时却按 command.Session 释放；
            // 允许 null 会让名额永久泄漏（该会话累计到容量后每条都 E_BUSY）。
            if (command.Session == null)
            {
                rejection = ScadaWebCommandResult.Fail(
                    ScadaWebErrorCodes.BadRequest,
                    "命令缺少会话（服务端接线错误）");
                return false;
            }

            // 队列归属以参数为准；命令里那份会话必须是同一条（否则名额会记在 A 上、
            // 释放时却去摘 B，泄漏的名额会让那个会话之后每一条都 E_BUSY）。
            if (!string.Equals(command.Session.SessionId, session.SessionId, StringComparison.Ordinal))
            {
                rejection = ScadaWebCommandResult.Fail(
                    ScadaWebErrorCodes.BadRequest,
                    "命令携带的会话与入队会话不一致（服务端接线错误）");
                return false;
            }

            var sessionId = session.SessionId;

            // 先占名额再写队列：两步之间若队列已满要回滚，否则名额会永久泄漏
            // （表现是这个会话之后每一条都 E_BUSY，直到重启）。
            // 计数与"减到 0 删键"必须同一把锁：AddOrUpdate 的减一与并发入队的加一
            // 之间若被 TryRemove 插进来，新命令的计数会被删掉，每会话上限随之失效
            // （2026-10-21 终审 P1-5）。
            lock (_pendingGate)
            {
                _pendingBySession.TryGetValue(sessionId, out var current);

                if (current >= _sessionCapacity)
                {
                    rejection = ScadaWebCommandResult.Fail(
                        ScadaWebErrorCodes.Busy,
                        $"该会话的命令队列已满（{_sessionCapacity} 条），请稍后重试");
                    return false;
                }

                _pendingBySession[sessionId] = current + 1;
            }

            if (!_channel.Writer.TryWrite(command))
            {
                ReleasePending(sessionId);

                rejection = ScadaWebCommandResult.Fail(
                    ScadaWebErrorCodes.Busy,
                    $"服务端命令队列已满（{GlobalCapacity} 条），请稍后重试");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 停止泵（同步）：<b>先完成队列、再等消费者把剩下的跑完</b>。
        /// 幂等；最多等 5 秒，超时后取消（防止某条命令卡死把退出链拖住）。
        /// </summary>
        public void Stop() => StopAsync().GetAwaiter().GetResult();

        /// <inheritdoc cref="Stop"/>
        public async Task StopAsync()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
                return;

            // 完成写端：ReadAllAsync 会把队列里已有的读完再结束。
            _channel.Writer.TryComplete();

            try
            {
                await _worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _log.Warn("[ScadaWeb] 命令泵 5 秒内未排空，强制取消（未执行的命令已丢弃）");
                try { _cts.Cancel(); } catch { /* 已取消 */ }
            }
            catch (Exception ex)
            {
                _log.Warn($"[ScadaWeb] 等待命令泵结束失败：{ex.GetType().Name}：{ex.Message}");
            }

            _pendingBySession.Clear();
        }

        /// <summary>释放（先尽力停止；不阻塞超过 5 秒）</summary>
        public void Dispose()
        {
            try { Stop(); } catch { /* 收尾失败没有补救动作 */ }

            try { _cts.Dispose(); } catch { /* 同上 */ }
        }

        // ── 消费者 ────────────────────────────────────────────────────────────────

        private async Task RunAsync()
        {
            try
            {
                await foreach (var command in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
                {
                    var commandSessionId = command.Session?.SessionId;
                    ReleasePending(commandSessionId);
                    MarkExecuting(commandSessionId);

                    var queueDelayMs = (int)Math.Max(0, (DateTime.UtcNow - command.EnqueuedAtUtc).TotalMilliseconds);
                    if (queueDelayMs > SlowQueueMs)
                    {
                        _log.Warn($"[ScadaWeb] 命令在队列里等待了 {queueDelayMs}ms（kind={command.Kind}，"
                                  + $"request={ShortId(command.RequestId)}）：UI 线程可能被别的活占住了");
                    }

                    var startedAt = Environment.TickCount64;
                    ScadaWebCommandResult result;
                    try
                    {
                        result = await ExecuteAsync(command).ConfigureAwait(false);
                    }
                    finally
                    {
                        UnmarkExecuting(commandSessionId);
                    }

                    var elapsedMs = Environment.TickCount64 - startedAt;

                    Interlocked.Increment(ref _processed);

                    if (elapsedMs > SlowCommandMs)
                    {
                        _log.Warn($"[ScadaWeb] 命令执行耗时 {elapsedMs}ms（kind={command.Kind}，"
                                  + $"request={ShortId(command.RequestId)}，ok={result.Ok}）："
                                  + "命令泵要求单条 ≤ 8ms，慢操作应外派到后台队列");
                    }

                    RaiseCompleted(command, result);
                }
            }
            catch (OperationCanceledException)
            {
                // 停服强制取消：正常收尾
            }
            catch (Exception ex)
            {
                // 通道本身出错（极少见）。泵退出之后 Enqueue 会一直失败，
                // 所以这里必须留下一条 Error——否则现场只看到"点了没反应"。
                _log.Error($"[ScadaWeb] 命令泵异常退出：{ex.GetType().Name}：{ex.Message}");
            }
        }

        private async Task<ScadaWebCommandResult> ExecuteAsync(ScadaWebCommand command)
        {
            try
            {
                var dispatcher = _uiDispatcher;

                if (dispatcher is null || dispatcher.CheckAccess())
                    return _handler(command);

                return await dispatcher.InvokeAsync(() => _handler(command)).Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error($"[ScadaWeb] 命令执行异常（kind={command.Kind}，request={ShortId(command.RequestId)}）："
                           + $"{ex.GetType().Name}：{ex.Message}");

                return ScadaWebCommandResult.Fail(
                    ScadaWebErrorCodes.Internal,
                    "服务端执行命令时发生异常，本次操作未生效");
            }
        }

        private void RaiseCompleted(ScadaWebCommand command, ScadaWebCommandResult result)
        {
            try
            {
                Completed?.Invoke(command, result);
            }
            catch (Exception ex)
            {
                _log.Warn($"[ScadaWeb] 回执处理器异常（kind={command.Kind}）：{ex.GetType().Name}：{ex.Message}");
            }
        }

        private void ReleasePending(string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return;

            lock (_pendingGate)
            {
                if (!_pendingBySession.TryGetValue(sessionId!, out var current))
                    return;

                if (current <= 1)
                    _pendingBySession.TryRemove(sessionId!, out _);
                else
                    _pendingBySession[sessionId!] = current - 1;
            }
        }

        private void MarkExecuting(string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return;
            _executingBySession.AddOrUpdate(sessionId!, 1, (_, current) => current + 1);
        }

        private void UnmarkExecuting(string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return;

            while (true)
            {
                if (!_executingBySession.TryGetValue(sessionId!, out var current))
                    return;

                if (current <= 1)
                {
                    if (_executingBySession.TryRemove(sessionId!, out _))
                        return;
                }
                else if (_executingBySession.TryUpdate(sessionId!, current - 1, current))
                {
                    return;
                }
            }
        }

        /// <summary>请求号的日志安全前缀（客户端生成的串可能很长，也可能是任意文本）</summary>
        private static string ShortId(string? requestId)
            => string.IsNullOrEmpty(requestId) ? "<none>" : (requestId.Length <= 8 ? requestId : requestId.Substring(0, 8));
    }
}
