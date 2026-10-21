using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
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

        private readonly WebSocketSession _socket;
        private readonly Channel<string> _outbound;
        private long _sent;
        private long _dropped;
        private int _disposed;
        private int _revoked;

        internal ScadaWebSession(WebSocketSession socket, ScadaWebTicketGrant grant, string remoteIp, string userAgent)
        {
            _socket = socket;
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
        internal void ResetPageKey() => CurrentPageKey = null;

        /// <summary>底层 Watson 会话（<b>只给 <see cref="ScadaWebHost"/> 收尾用</b>，别在业务里直接用）</summary>
        internal WebSocketSession Socket => _socket;

        /// <summary>标记收到一帧（空闲回收的依据）</summary>
        internal void Touch() => LastSeenUtc = DateTime.UtcNow;

        /// <summary>
        /// 入队一帧文本。返回 false = 队列满、这一帧被丢弃（<see cref="DroppedCount"/> 自增）。
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
        /// 单条发送循环：入队顺序 = 上屏顺序。连接关闭/服务停止时由取消令牌结束。
        /// </summary>
        internal async Task RunSendLoopAsync(CancellationToken token)
        {
            try
            {
                await foreach (var message in _outbound.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    await _socket.SendTextAsync(message, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常收尾（停服 / 客户端断开）
            }
            catch (Exception)
            {
                // 发送失败 = 连接已坏，交给接收循环去收尾；这里吞掉是为了不让发送循环的异常
                // 变成"未观察任务"（那个坑的表现是进程退出时静默丢栈）
            }
        }

        /// <summary>直接发一帧（不排队）。<b>只给握手首帧 hello 用</b>——那一刻还没有第二个发送者</summary>
        internal Task SendDirectAsync(string json, CancellationToken token) => _socket.SendTextAsync(json, token);

        /// <summary>主动关闭底层连接（容量拒绝 / 停服）</summary>
        internal async Task CloseAsync(System.Net.WebSockets.WebSocketCloseStatus status, string reason)
        {
            try
            {
                await _socket.CloseAsync(status, reason, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // 已经断开 / 对端没回 close 帧：关闭失败没有补救动作
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            // 让发送循环自然结束（ReadAllAsync 在通道完成后返回）
            _outbound.Writer.TryComplete();
        }
    }
}
