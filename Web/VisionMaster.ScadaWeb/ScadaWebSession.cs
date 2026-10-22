using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Core.Interfaces;
using VisionMaster.Scada;
using WatsonWebserver.Core.WebSockets;

namespace VisionMaster.ScadaWeb
{
    /// <summary>
    /// 一条浏览器连接（M1b：只读镜像）。
    ///
    /// <b>只持有"连接 + 身份 + 发送队列"，不持有任何领域对象</b>
    /// ---------
    /// 设计口径（方案 §4.2）明确列出会话<b>不持有</b> <c>ScadaRuntime</c> / <c>ScadaDocument</c> /
    /// 任何 WPF 控件。M1b 接快照之后这条纪律更容易破：把 hub 塞进会话，之后每个 handler 都能
    /// 直接读运行态，而"运行结束时 hub.Session 已经清空"这条约束就没人守了。
    /// 所以运行态事实一律由 <see cref="ScadaWebHost"/> 在 UI 线程现取，会话只负责收发。
    ///
    /// <b>为什么发送要排队而不是直接 await</b>
    /// ---------
    /// Watson 的 <c>SendTextAsync</c> 内部有 <c>SemaphoreSlim _SendLock</c>（并发安全，见 <c>_WebProbe2</c> §2-F），
    /// 但"并发安全"不等于"顺序确定"：多个后台任务同时发帧时，帧的先后由抢锁顺序决定，
    /// 客户端可能先收到增量再收到快照。每会话一条发送循环 = 入队顺序就是上屏顺序，
    /// 而且慢消费者只会堆积在自己的有界队列里，不拖住别的会话。
    /// </summary>
    public sealed class ScadaWebSession : IDisposable
    {
        /// <summary>出站队列容量。满了 <see cref="TrySend"/> 返回 false；结构消息由调用方重试，值帧丢一拍</summary>
        public const int OutboundCapacity = 256;

        /// <summary>控制队列容量（ack/error/bye/stopped 等；只等当前一条数据消息，不排整批）</summary>
        public const int ControlCapacity = 64;

        /// <summary>单条消息发送超时：慢消费者不能把发送循环永久挂住</summary>
        public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(15);

        private readonly WebSocketSession _socket;
        private readonly Channel<string> _outbound;
        private readonly Channel<string> _control;
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private readonly CancellationTokenSource _lifetimeCts = new();
        private readonly ILogService? _log;
        private int _sendFaulted;
        private int _deltaResetRequested;
        private long _sent;
        private long _dropped;
        private int _disposed;
        private int _revoked;
        private long _lastSendSucceededUtcTicks;

        /// <summary>
        /// 增量值帧的"已成功送达"基线：<c>(elementId + 属性键) → 该 item 的规范 JSON</c>。
        ///
        /// <b>只在 <see cref="TrySend"/> 返回 true 之后推进</b>：入队失败/队列满时下一拍必须
        /// 重新带上这些变化，否则客户端会永久停在旧值上，直到下一次 keyframe。
        /// 连接级（不是宿主级）：每个会话的发送进度不同。
        /// </summary>
        internal Dictionary<string, string> LastValues { get; } = new(StringComparer.Ordinal);

        /// <summary>每连接单调递增的帧序号（delta/缺口诊断用；与宿主级 <c>seq</c> 分开）</summary>
        internal long FrameSeq { get; set; }

        /// <summary>下一帧是否必须整帧（首帧 / 结构变更后 / 重连后）</summary>
        internal bool NeedsKeyframe { get; set; } = true;

        /// <summary>上一次 keyframe 的 UTC 时刻（周期 keyframe 用）</summary>
        internal DateTime LastKeyframeUtc { get; set; } = DateTime.MinValue;

        /// <summary>预算裁剪的轮转游标：保证 1000 项全变时尾部不会被永久饿死</summary>
        internal int DeltaCursor { get; set; }

        /// <summary>
        /// 上一次下发的 caps JSON 哈希：内容没变就不再发（3000 图元全量 deny/allow 约 200KB，
        /// 每 2 秒发一次本身就是队头问题）。只在帧循环/广播循环线程读写。
        /// </summary>
        internal ulong? LastCapsHash { get; set; }

        /// <summary>上一次回 pong 的时刻（ticks）：防止 ping 洪泛塞满控制队列</summary>
        internal long LastPongReplyTicks;

        /// <summary>上一次回 hello.ok 的时刻（ticks）：同上</summary>
        internal long LastHelloReplyTicks;

        /// <summary>view.resync(scope=structure) 待处理标志（WS 线程置，帧循环消费）</summary>
        internal int ResyncRequested;

        /// <summary>view.resync(scope=frames) 待处理标志（WS 线程置，帧循环消费）</summary>
        internal int ForceKeyframe;

        /// <summary>上一次受理 resync 的时刻（ticks；每会话 1 秒限速）</summary>
        internal long LastResyncUtcTicks;

        /// <summary>
        /// 首屏状态还没发（1 = 待发）。帧循环必须跳过这种会话：
        /// 否则它会在 hello 的 SendInitialStateAsync 之前先发一份 snapshot，首屏变两份。
        /// </summary>
        internal int InitialStatePending = 1;

        /// <summary>
        /// 结构已超过硬上限（1 = 已拒绝）。帧循环看到后不再每 100ms 重试/重发 error，
        /// 连接由 SendStructure 的 1009 关闭收尾。
        /// </summary>
        internal int StructureTooLarge;

        /// <summary>最近一次"真正发出去"的 UTC ticks（45 秒死连接判据的一半；TrySend 只是入队，不算）</summary>
        internal long LastSendSucceededUtcTicks => Volatile.Read(ref _lastSendSucceededUtcTicks);

        internal ScadaWebSession(
            WebSocketSession socket,
            ScadaWebTicketGrant grant,
            string remoteIp,
            string userAgent,
            ILogService? log = null)
        {
            _socket = socket;
            _log = log;
            Grant = grant;
            RemoteIp = remoteIp ?? string.Empty;
            UserAgent = userAgent ?? string.Empty;

            // SessionId 只对服务端可见（日志/审计）；SessionRef 才是下发给客户端的不透明标识。
            // 两者都随机生成、互不可推导——客户端拿 sessionRef 反查不出 SessionId（方案 §9.3）。
            SessionId = Guid.NewGuid().ToString("N");
            SessionRef = Guid.NewGuid().ToString("N").Substring(0, 16);

            _outbound = Channel.CreateBounded<string>(new BoundedChannelOptions(OutboundCapacity)
            {
                // 单读（发送循环）+ 多写（各 handler）：WPF 侧不需要 SingleWriter 的优化
                SingleReader = true,
                SingleWriter = false,
                // 满时让 TryWrite 返回 false（不是 DropWrite）：结构消息必须由调用方看到"没发出去"，
                // 否则页键已推进、客户端却永远收不到结构，画面会冻结到下一次结构变更或重连。
                // 值帧仍然可以丢一拍；M2 的命令帧要显式回 E_BUSY。
                FullMode = BoundedChannelFullMode.Wait
            });

            _control = Channel.CreateBounded<string>(new BoundedChannelOptions(ControlCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        }

        /// <summary>Watson 连接标识（同一连接的 socket.Id）</summary>
        public Guid ConnectionId => _socket.Id;

        /// <summary>服务端会话 ID（32 hex）。<b>绝不下发给其它会话</b>，只进日志/审计</summary>
        public string SessionId { get; }

        /// <summary>对外的会话引用（16 hex，断开即废）。presence/界面只认它</summary>
        public string SessionRef { get; }

        /// <summary>签发这条连接的 ticket 授权（身份来源）</summary>
        public ScadaWebTicketGrant Grant { get; }

        /// <summary>
        /// 重连凭据（8 小时、<b>可重复使用</b>）；<c>null</c> = 未签发。
        ///
        /// 在 ticket 核销时签发、随首帧 <c>hello</c> 下发；客户端断线后拿它换新 ticket。
        /// 它<b>不能</b>当握手 ticket 用（见 <see cref="ScadaWebSessionGrant"/> 的类注释）。
        /// </summary>
        public ScadaWebSessionGrant? Session => Grant.Session;

        /// <summary>登录用户名；<c>null</c> = 匿名只读观众</summary>
        public string? UserName => Grant.UserName;

        /// <summary>角色（匿名观众为操作员）</summary>
        public ScadaRole Role => Grant.Role;

        /// <summary>是否已登录（匿名观众为 false）</summary>
        public bool IsAuthenticated => !Grant.IsAnonymous;

        /// <summary>是否只读（M1a 恒为 true）</summary>
        public bool ReadOnly => Grant.ReadOnly;

        /// <summary>
        /// 服务端已把这条会话标记为撤销（登出 / 踢人 / 改密 / 降权 / 删号）。
        ///
        /// 撤销时 WS 会被关掉，但命令泵里可能还有最多 32 条已入队命令；
        /// 路由在<b>执行前</b>再查一次这个标记，保证"踢下线"对队列里的命令也立即生效。
        /// </summary>
        public bool IsRevoked => Volatile.Read(ref _revoked) != 0;

        /// <summary>重连凭据是否已过期（没有凭据的匿名连接按未过期处理，由权限层判只读）</summary>
        public bool IsSessionExpired => Session?.IsExpired(DateTime.UtcNow) ?? false;

        /// <summary>标记为撤销（幂等；只由 <see cref="ScadaWebHost"/> 的撤销路径调用）</summary>
        internal void MarkRevoked() => Interlocked.Exchange(ref _revoked, 1);

        /// <summary>远端 IP（审计/在线列表用）</summary>
        public string RemoteIp { get; }

        /// <summary>User-Agent（resume 绑定键的一半，M1b 用）</summary>
        public string UserAgent { get; }

        /// <summary>连接建立时刻（UTC）</summary>
        public DateTime ConnectedAtUtc { get; } = DateTime.UtcNow;

        /// <summary>最近一次收到帧的时刻（UTC）</summary>
        public DateTime LastSeenUtc { get; private set; } = DateTime.UtcNow;

        /// <summary>已成功入队/发送的帧数（诊断用）</summary>
        public long SentCount => Interlocked.Read(ref _sent);

        /// <summary>因队列满被丢弃的帧数（诊断用）</summary>
        public long DroppedCount => Interlocked.Read(ref _dropped);

        /// <summary>
        /// 本会话已经拿到的页面键（<c>pageId@pageVersion</c>）；<c>null</c> = 还没拿到过。
        ///
        /// 值帧循环靠它决定"这一拍该发 <c>snapshot</c>（第一次 / 新一轮运行）还是
        /// <c>page</c>（同一轮里结构变了）"。由宿主写、循环读；字符串引用赋值是原子的，
        /// 读到旧值最多多发/少发一条快照，下一拍就自愈。
        /// </summary>
        internal string? CurrentPageKey { get; set; }

        /// <summary>作废页键（运行开始/结束、新会话首屏）：下一拍必定重发完整快照</summary>
        internal void ResetPageKey()
        {
            CurrentPageKey = null;

            // 不在这里清 LastValues/FrameSeq：本方法可能由 UI 线程（OnRunStopped）或 WS 线程
            // （首屏）调用，而 SendFrame 在帧循环线程写 LastValues。跨线程 Clear + 写会让
            // Dictionary 损坏/死循环。真正的重置由帧循环在下一拍消费这个标志后执行。
            RequestDeltaReset();
        }

        /// <summary>请求"下一帧前重置增量基线"（线程安全；只有帧循环会真正执行重置）</summary>
        internal void RequestDeltaReset() => Interlocked.Exchange(ref _deltaResetRequested, 1);

        /// <summary>帧循环消费重置请求（返回 true = 本拍发送前必须重置）</summary>
        internal bool ConsumeDeltaResetRequested()
            => Interlocked.Exchange(ref _deltaResetRequested, 0) != 0;

        /// <summary>
        /// 真正重置增量基线。<b>只允许帧循环线程调用</b>（它独占 <see cref="LastValues"/>）；
        /// 其它线程请用 <see cref="RequestDeltaReset"/>。
        /// </summary>
        internal void ResetDeltaForNewStructure()
        {
            LastValues.Clear();
            FrameSeq = 0;
            NeedsKeyframe = true;
            LastKeyframeUtc = DateTime.MinValue;
            DeltaCursor = 0;
        }

        /// <summary>发送循环里"这一帧真的出去了"——45 秒判死只认这个时间戳</summary>
        internal void MarkSendSucceeded()
            => Interlocked.Exchange(ref _lastSendSucceededUtcTicks, DateTime.UtcNow.Ticks);

        /// <summary>底层 Watson 会话（<b>只给 <see cref="ScadaWebHost"/> 收尾用</b>，别在业务里直接用）</summary>
        internal WebSocketSession Socket => _socket;

        /// <summary>标记收到一帧（空闲回收的依据）</summary>
        internal void Touch() => LastSeenUtc = DateTime.UtcNow;

        /// <summary>
        /// 入队一帧数据（值帧/结构/axis/grid/presence/alarms/caps）。返回 false = 队列满、这一帧被丢弃
        /// （<see cref="DroppedCount"/> 自增）。
        /// </summary>
        public bool TrySend(string json)
        {
            if (Volatile.Read(ref _disposed) != 0) return false;

            if (_outbound.Writer.TryWrite(json))
            {
                Interlocked.Increment(ref _sent);
                return true;
            }

            Interlocked.Increment(ref _dropped);
            return false;
        }

        /// <summary>
        /// 入队一条控制消息（ack/error/result/bye/stopped/idle/img.token/pong/hello.ok）。
        ///
        /// <b>它不排整批数据</b>：控制循环只等"当前正在发的那一条"（共享发送闸），
        /// 不会排在 256 条值帧/一整份快照后面。返回 false = 控制队列满（64 条）；
        /// ack/error/bye/stopped 的调用方必须关连接，不能静默丢。
        /// </summary>
        public bool TrySendControl(string json)
        {
            if (Volatile.Read(ref _disposed) != 0) return false;

            if (_control.Writer.TryWrite(json))
            {
                Interlocked.Increment(ref _sent);
                return true;
            }

            Interlocked.Increment(ref _dropped);
            return false;
        }

        /// <summary>
        /// 两条发送循环（控制/数据）共用一个发送闸：入队顺序仍然是各自通道内的顺序，
        /// 控制消息最多等"当前这一条"发完，不排整批。任一条异常都会记日志并关连接。
        /// </summary>
        internal async Task RunSendLoopAsync(CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeCts.Token);
            var linkedToken = linked.Token;

            await Task.WhenAll(
                RunControlSendLoopAsync(linkedToken),
                RunDataSendLoopAsync(linkedToken)).ConfigureAwait(false);
        }

        private async Task RunControlSendLoopAsync(CancellationToken token)
        {
            try
            {
                await foreach (var message in _control.Reader.ReadAllAsync(token).ConfigureAwait(false))
                    await SendOneAsync(message, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // 每消息 SendTimeout 到期（不是停服/Dispose）：不能当正常收尾，
                // 否则这条发送循环静默死掉、另一条还在刷 MarkSendSucceeded，会话永远不被回收。
                HandleSendFault(new TimeoutException(
                    $"发送超时（{SendTimeout.TotalSeconds:0} 秒未完成）"));
            }
            catch (OperationCanceledException)
            {
                // 正常收尾（停服 / 客户端断开 / Dispose 取消）
            }
            catch (Exception ex)
            {
                HandleSendFault(ex);
            }
        }

        private async Task RunDataSendLoopAsync(CancellationToken token)
        {
            try
            {
                await foreach (var message in _outbound.Reader.ReadAllAsync(token).ConfigureAwait(false))
                    await SendOneAsync(message, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                HandleSendFault(new TimeoutException(
                    $"发送超时（{SendTimeout.TotalSeconds:0} 秒未完成）"));
            }
            catch (OperationCanceledException)
            {
                // 正常收尾
            }
            catch (Exception ex)
            {
                HandleSendFault(ex);
            }
        }

        private async Task SendOneAsync(string json, CancellationToken token)
        {
            await _sendGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(SendTimeout);
                await _socket.SendTextAsync(json, timeout.Token).ConfigureAwait(false);
                MarkSendSucceeded();
            }
            finally
            {
                _sendGate.Release();
            }
        }

        /// <summary>
        /// 发送循环异常：记日志 + 取消本会话 + 关连接。
        /// 旧写法两条循环各自 `catch { }`，死一条完全静默（连接还在、结构永远发不出去）。
        /// </summary>
        private void HandleSendFault(Exception ex)
        {
            if (Interlocked.Exchange(ref _sendFaulted, 1) != 0)
                return;

            _log?.Warn($"[ScadaWeb] 会话发送失败（ref={SessionRef}）：{ex.GetType().Name}：{ex.Message}");

            try { _lifetimeCts.Cancel(); } catch { /* 已取消 */ }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await _socket.CloseAsync(
                        System.Net.WebSockets.WebSocketCloseStatus.InternalServerError,
                        "send failed",
                        timeout.Token).ConfigureAwait(false);
                }
                catch
                {
                    // 连接已经坏了 / 关闭超时
                }
            });
        }

        /// <summary>直接发一帧（不排队）。<b>只给握手首帧 hello 用</b>——那一刻还没有第二个发送者</summary>
        internal Task SendDirectAsync(string json, CancellationToken token) => _socket.SendTextAsync(json, token);

        /// <summary>主动关闭底层连接（容量拒绝 / 停服）；最多等 2 秒，避免卡在阻塞发送后面</summary>
        internal async Task CloseAsync(System.Net.WebSockets.WebSocketCloseStatus status, string reason)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(status, reason, timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                // 已经断开 / 对端没回 close 帧 / 发送锁被占：关闭失败没有补救动作
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            // 让两条发送循环自然结束（ReadAllAsync 在通道完成后返回）
            _outbound.Writer.TryComplete();
            _control.Writer.TryComplete();

            // 取消正在 await 的 SendTextAsync：否则关闭会话后发送任务会挂到宿主停止
            try { _lifetimeCts.Cancel(); } catch { /* 已取消 */ }
        }
    }
}
