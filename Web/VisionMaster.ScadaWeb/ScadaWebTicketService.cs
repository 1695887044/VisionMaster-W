using System;
using System.Collections.Generic;
using VisionMaster.Scada;

namespace VisionMaster.ScadaWeb
{
    /// <summary>
    /// 一次性 WS 握手 ticket 的授权内容（ticket 本身只是一把钥匙，身份在这里）。
    ///
    /// <b>为什么 ticket 不是"登录态"</b>
    /// ---------
    /// 浏览器 <c>WebSocket</c> 构造器不能自定义请求头，而 Watson 的 <c>auth:true</c> 只认
    /// <c>AuthenticateApiRequest</c>（握手阶段）。所以凭证只能放 query；放 query 的东西
    /// 会进代理日志与浏览器历史，只能是<b>短命、单次</b>的"准入门票"。
    /// 真正的用户身份由 M1b 的 <c>auth.login</c> 在连接内建立；本类只回答
    /// "这条连接确实来自本服务发出的页面"。
    /// </summary>
    public sealed class ScadaWebTicketGrant
    {
        /// <summary>ticket 原文（32 hex）。<b>禁止整串写日志</b>，用 <see cref="ShortTicket"/></summary>
        public string Ticket { get; init; } = string.Empty;

        /// <summary>登录用户名；<c>null</c> = 匿名只读观众</summary>
        public string? UserName { get; init; }

        /// <summary>校验通过的角色（匿名观众为 <see cref="ScadaRole.Operator"/>）</summary>
        public ScadaRole Role { get; init; }

        /// <summary>
        /// 只读标记。M1a 全局只读（任何 ticket 都是 <c>true</c>）；
        /// M2 起按 <c>allowCommands</c> 与"是否匿名"分流（见 <see cref="ScadaWebTicketService.Issue"/>）。
        ///
        /// <b>为什么是 <c>set</c> 而不是 <c>init</c></b>：<see cref="ScadaWebTicketService.TryRedeem"/>
        /// 要在核销那一刻按<b>当时的</b>策略再压一次（配置在"签票 → 握手"这 30 秒内被改成只读时，
        /// 已经发出去的票不许带着旧权限进连接）。那一刻对象已构造完，<c>init</c> 写不进去。
        /// 写入只发生在"交给 WS handler 之前"，此后只读。
        /// </summary>
        public bool ReadOnly { get; set; }

        /// <summary>
        /// 调用方<b>显式</b>指定的只读票（<see cref="ScadaWebTicketService.Issue"/> 的 <c>readOnly</c> 参数）。
        ///
        /// <b>为什么要与 <see cref="ReadOnly"/> 分开</b>
        /// ---------
        /// <see cref="ReadOnly"/> 是"生效只读"：签票时的 <c>AllowCommands=false</c>、匿名观众、
        /// 核销时按当时策略再压一次，都会把它置 true。它是<b>那一刻</b>的策略快照。
        /// 而权限判定（<see cref="ScadaWebAccessPolicy"/>）必须按<b>当前</b> <c>AllowCommands</c> 回答——
        /// 否则"配置 false→true 之后同一会话仍不能操作、只能重连"，热切换等于没发生。
        /// 所以"显式只读"单独记一份：只有它才永久只读。
        ///
        /// <c>null</c> = 未标注（手工构造的票 / 旧路径）：判定方回退到 <see cref="ReadOnly"/>，
        /// 保持"拿不到来源就按只读"的 fail-safe 口径。
        /// </summary>
        public bool? ExplicitReadOnly { get; init; }

        /// <summary>绑定的来源 IP（<c>null</c> = 不绑定）。M1a 不签发绑定票，字段留给 M1b 的 Origin/IP 门禁</summary>
        public string? BoundIp { get; init; }

        /// <summary>到期时刻（UTC）</summary>
        public DateTime ExpiresAtUtc { get; init; }

        /// <summary>是否匿名观众票</summary>
        public bool IsAnonymous => string.IsNullOrEmpty(UserName);

        /// <summary>日志安全的短标识（前 6 位）。设计口径：ticket 只记前 6 位与 sessionId</summary>
        public string ShortTicket => Ticket.Length <= 6 ? Ticket : Ticket.Substring(0, 6);

        /// <summary>
        /// 核销时签发的会话令牌（<b>重连凭据</b>）；<c>null</c> = 还没核销。
        ///
        /// 由 <see cref="ScadaWebTicketService.TryRedeem"/> 在核销成功后挂上
        /// （<b>续期票沿用原令牌，不重新签发</b>），再经 <c>hello</c> 下发给客户端；
        /// 客户端断开后用它换新 ticket，不必重新登录。
        /// 只在下发给客户端之前写一次，此后只读。
        /// </summary>
        public ScadaWebSessionGrant? Session { get; set; }

        /// <summary>
        /// 本次核销是否<b>沿用</b>了客户端已有的会话令牌（refresh 路径）。
        ///
        /// 容量拒绝 / handoff 过期时只能回收"本次新签"的令牌；续期票挂的是客户端
        /// 正在用的那枚 8 小时令牌，回收它会把用户其它标签页一起登出
        /// （2026-10-21 终审 P1-2）。
        /// </summary>
        public bool SessionReused { get; set; }

        /// <summary>是否已过期</summary>
        public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;
    }

    /// <summary>
    /// 会话令牌（<b>可重复使用</b>，与一次性 ticket 严格区分）。
    ///
    /// <b>为什么需要它</b>
    /// ---------
    /// ticket 是"单次 + 30 秒"的准入门票（见 <see cref="ScadaWebTicketService"/> 的类注释），
    /// 而浏览器断线重连（网络抖动 / 平板休眠 / 服务端重启前的最后一次重试）会复用同一张票——
    /// 若只认 ticket，重连永远是 401，用户得重新登录。会话令牌把"身份"从"门票"里拆出来：
    ///   · ticket  → 一次性，只用来过 WS 握手；
    ///   · session → 8 小时可复用，只用来<b>换新 ticket</b>（<c>POST /api/ticket {refreshToken}</c>）。
    ///
    /// <b>它不能当 ticket 用</b>：<see cref="ScadaWebTicketService.TryRedeem"/> 只查一次性表，
    /// 会话令牌进不了握手——否则"单次"这条语义就没了。
    /// </summary>
    public sealed class ScadaWebSessionGrant
    {
        /// <summary>令牌原文（32 hex）。<b>禁止整串写日志</b>，用 <see cref="ShortToken"/></summary>
        public string Token { get; init; } = string.Empty;

        /// <summary>登录用户名；<c>null</c> = 匿名只读观众</summary>
        public string? UserName { get; init; }

        /// <summary>角色</summary>
        public ScadaRole Role { get; init; }

        /// <summary>只读标记（与签发时的 ticket 一致）</summary>
        public bool ReadOnly { get; init; }

        /// <summary>
        /// 显式只读（与 <see cref="ScadaWebTicketGrant.ExplicitReadOnly"/> 同义）。
        /// 续期时按它重签票：<b>不</b>把"签票时配置是只读"冻进下一张票，
        /// 否则配置改回可写之后，续期出来的票仍然是只读的。
        /// </summary>
        public bool? ExplicitReadOnly { get; init; }

        /// <summary>绑定的来源 IP（<c>null</c> = 不绑定；M1 不绑定，见 <see cref="ScadaWebTicketService.Issue"/> 的注释）</summary>
        public string? BoundIp { get; init; }

        /// <summary>到期时刻（UTC）</summary>
        public DateTime ExpiresAtUtc { get; init; }

        /// <summary>是否匿名观众</summary>
        public bool IsAnonymous => string.IsNullOrEmpty(UserName);

        /// <summary>日志安全的短标识（前 6 位）</summary>
        public string ShortToken => Token.Length <= 6 ? Token : Token.Substring(0, 6);

        /// <summary>是否已过期</summary>
        public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;

        /// <summary>剩余有效秒数（<c>hello.sessionExpiresIn</c> 用它；过期返回 0）</summary>
        public int RemainingSeconds(DateTime nowUtc)
        {
            var remaining = (ExpiresAtUtc - nowUtc).TotalSeconds;
            return remaining <= 0 ? 0 : (int)remaining;
        }
    }

    /// <summary>
    /// 一次性 ticket 的签发 / 核销（纯内存，进程退出即清空），以及<b>重连用的会话令牌</b>。
    ///
    /// <b>三张表的职责必须分清（否则"单次"会被自己破掉）</b>
    /// ---------
    ///   · <c>_live</c>     = 已签发、尚未核销的票。只有 <see cref="TryRedeem"/> 会动它；
    ///   · <c>_handoff</c>  = <b>已核销</b>、等着 WS handler 来取身份的票；
    ///   · <c>_sessions</c> = 核销时签发的会话令牌（8 小时、<b>可重复使用</b>，只用来换新票）。
    ///
    /// 为什么需要第二张表：核销发生在 <c>AuthenticateApiRequest</c>（握手前），
    /// 而"这条连接属于谁"要在 WS handler 里才知道。两张表分开之后，同一个 ticket
    /// 第二次握手会在 <see cref="_live"/> 里查不到（已移走）→ 401，
    /// 不会因为"handler 还没取走"而被重复使用。
    ///
    /// 为什么需要第三张表：ticket 单次 + 30 秒，浏览器断线重连只能重新登录；
    /// 会话令牌把"身份"从"门票"里拆出来（见 <see cref="ScadaWebSessionGrant"/>），
    /// 但<b>它进不了握手</b>——<see cref="TryRedeem"/> 只查 <c>_live</c>，所以"单次"语义不受影响。
    ///
    /// <b>线程</b>：Watson 的握手可能并发，全部操作在 <c>_gate</c> 内完成（纯内存字典，无 IO）。
    /// </summary>
    public sealed class ScadaWebTicketService
    {
        /// <summary>ticket 有效期。设计口径 30 秒：足够一次握手，又不至于在代理日志里长期有效</summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 会话令牌有效期（8 小时）。口径：一个班次内断线重连不必重新登录，
        /// 跨班次就该重新登录（令牌只在内存里，进程重启即清空）。
        /// </summary>
        public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

        /// <summary>
        /// 会话令牌表的默认上限（<b>4096</b>）。
        ///
        /// <b>为什么必须有上限</b>：会话令牌 8 小时有效、每次"登录 + 握手"都会签发一枚，
        /// 而签发发生在<b>已经通过账号校验</b>之后——没有上限时，一个能通过校验的账号
        /// （或被暴力猜中的账号）可以靠反复登录把这张表堆到内存吃紧；
        /// 更糟的是"匿名只读观众"那条路（<c>AllowAnonymousView=true</c> 时不需要任何凭据），
        /// 任何人反复换票就能把它堆起来。超限一律拒绝新会话（<see cref="ErrorCapacity"/>），
        /// 不排队、不挤掉已有会话——挤掉会让正在看画面的人莫名掉线。
        /// </summary>
        public const int DefaultMaxSessionTokens = 4096;

        /// <summary>会话不存在 / 已过期 / 匿名续期被策略关闭（对客户端是同一个出口：回登录页）</summary>
        public const string ErrorSessionExpired = "E_SESSION_EXPIRED";

        /// <summary>会话被主动撤销（登出 / 改密 / 降权）</summary>
        public const string ErrorSessionRevoked = "E_SESSION_REVOKED";

        /// <summary>会话令牌表已满</summary>
        public const string ErrorCapacity = "E_CAPACITY";

        private readonly Dictionary<string, ScadaWebTicketGrant> _live = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ScadaWebTicketGrant> _handoff = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ScadaWebSessionGrant> _sessions = new(StringComparer.Ordinal);

        /// <summary>被撤销的会话令牌 → 撤销时刻（只用来在日志/响应里区分"过期"与"被撤销"）</summary>
        private readonly Dictionary<string, DateTime> _revokedTokens = new(StringComparer.Ordinal);

        /// <summary>被整账号撤销的用户名 → 撤销时刻（撤销后该用户<b>所有</b>会话失效）</summary>
        private readonly Dictionary<string, DateTime> _revokedUsers = new(StringComparer.OrdinalIgnoreCase);

        private readonly object _gate = new();

        /// <summary>
        /// 会话令牌表上限（<c>&lt;= 0</c> 按 <see cref="DefaultMaxSessionTokens"/> 处理）。
        /// 做成可写属性是为了断言能把上限压到 2 条来验拒绝路径，产品路径不要改它。
        /// </summary>
        public int MaxSessionTokens { get; set; } = DefaultMaxSessionTokens;

        /// <summary>
        /// 是否允许<b>匿名只读观众</b>用会话令牌续期。<b>默认 true</b>（保持 M1c 行为）。
        ///
        /// 宿主应在启动时把它接到 <c>ScadaWebSettings.AllowAnonymousView</c>：
        /// 该配置关掉之后，<b>已经签发出去的匿名令牌也不许再续期</b>——
        /// 否则"关掉匿名访问"只对下一个新访客生效，之前拿到令牌的页面能一直换票看下去
        /// （配置改了、现场看到的却还是能看，是最容易被当成"没生效"的形态）。
        /// </summary>
        public bool AllowAnonymousView { get; set; } = true;

        /// <summary>当前未核销的票数（诊断用；断言宿主也用它验"用完即删"）</summary>
        public int LiveCount
        {
            get { lock (_gate) return _live.Count; }
        }

        /// <summary>已核销、尚未被 WS handler 取走的票数（诊断用）</summary>
        public int HandoffCount
        {
            get { lock (_gate) return _handoff.Count; }
        }

        /// <summary>当前有效的会话令牌数（诊断用；<see cref="Clear"/> 之后必须归零）</summary>
        public int SessionCount
        {
            get
            {
                lock (_gate)
                {
                    // 先清过期项再报数：调用方（/api/ticket 的容量预检）拿这个数判"满没满"，
                    // 若把 4096 条已过期令牌算作满，新的登录/匿名取票会永久 503，直到重启
                    // （2026-10-21 终审 P1-3）。
                    PruneLocked(DateTime.UtcNow);
                    return _sessions.Count;
                }
            }
        }

        /// <summary>
        /// 签发一张一次性票。返回的对象同时是"核销后交给 WS handler 的身份"。
        /// </summary>
        /// <param name="userName">登录用户名；<c>null</c> = 匿名只读观众</param>
        /// <param name="role">角色</param>
        /// <param name="readOnly">调用方直接指定的只读标记（<c>true</c> 恒只读）</param>
        /// <param name="boundIp">绑定 IP（<c>null</c> = 不绑定）</param>
        /// <param name="allowCommands">
        /// 是否允许这条连接下发写命令（M2）。<b>最终只读口径 = <paramref name="readOnly"/> ∨ ¬<paramref name="allowCommands"/> ∨ 匿名</b>：
        /// 匿名观众<b>永远</b>只读（不依赖调用方有没有记得传 true），登录用户才按配置放开。
        /// 默认 <c>true</c> 保持 M1c 的调用形状（老调用点不传这一项也不会被意外降权/提权）。
        /// </param>
        public ScadaWebTicketGrant Issue(
            string? userName,
            ScadaRole role,
            bool readOnly,
            string? boundIp = null,
            bool allowCommands = true)
        {
            var now = DateTime.UtcNow;
            var effectiveUserName = string.IsNullOrWhiteSpace(userName) ? null : userName.Trim();

            var grant = new ScadaWebTicketGrant
            {
                Ticket = Guid.NewGuid().ToString("N"),
                UserName = effectiveUserName,
                Role = role,
                ReadOnly = readOnly || !allowCommands || effectiveUserName == null,
                ExplicitReadOnly = readOnly,
                BoundIp = string.IsNullOrWhiteSpace(boundIp) ? null : boundIp,
                ExpiresAtUtc = now + Lifetime
            };

            lock (_gate)
            {
                PruneLocked(now);

                // 新票 = 刚刚通过账号校验（登录路径）或续期路径。既然身份已经重新证明过，
                // 账号级撤销（改密/降权之后调 RevokeUser）就不该再拦这个人的<b>新</b>会话：
                // 撤销要挡的是"改密前拿到的那批令牌"，不是"这个人从此不能登录"。
                // 不加这一句的后果很隐蔽：改密后用户重新登录成功、握手也成功，
                // 但下一次断线续期会被 E_SESSION_REVOKED 打回登录页，看起来像"改完密码就登不住"。
                if (effectiveUserName != null)
                    _revokedUsers.Remove(effectiveUserName);

                _live[grant.Ticket] = grant;
            }

            return grant;
        }

        /// <summary>
        /// 核销一张票（握手阶段调用一次）。成功即从"可用表"移除，并放进交接表等 handler 取。
        /// </summary>
        /// <param name="ticket">票面值（query 或 Authorization 头里取出的原文）</param>
        /// <param name="remoteIp">来源 IP（未绑定 IP 的票忽略它）</param>
        /// <param name="grant">核销成功时的授权内容</param>
        /// <param name="reason">失败原因（只进日志，不回给客户端——避免账号/票面枚举）</param>
        public bool TryRedeem(string? ticket, string? remoteIp, out ScadaWebTicketGrant? grant, out string reason)
            => TryRedeem(ticket, remoteIp, allowCommands: true, out grant, out reason, out _);

        /// <summary>
        /// 核销一张票（M2 形状）：额外按<b>核销那一刻</b>的策略压一次只读标记，并给出稳定错误码。
        ///
        /// <b>为什么核销时还要再判一次 <paramref name="allowCommands"/></b>
        /// ---------
        /// 票的有效期是 30 秒，而配置/策略可能在这 30 秒里被改（关掉写权限、关掉匿名访问）。
        /// 只按签发时的口径走，等于"改配置后最多还能写 30 秒"；这里按核销时重压一次，
        /// 把窗口收窄到"已经建立的连接"（那是 M2 的租约/权限闸要管的事，不在本类）。
        /// </summary>
        /// <param name="ticket">票面值</param>
        /// <param name="remoteIp">来源 IP</param>
        /// <param name="allowCommands">当前策略是否允许写命令；<c>false</c> 时这张票一律只读</param>
        /// <param name="grant">核销成功时的授权内容</param>
        /// <param name="reason">失败原因（只进日志）</param>
        /// <param name="errorCode">
        /// 稳定错误码（回给客户端用）：<c>null</c> = 成功；<see cref="ErrorCapacity"/> = 会话令牌表已满；
        /// 其余失败仍返回 <c>null</c>（握手统一 401，不给枚举口子）。
        /// </param>
        public bool TryRedeem(
            string? ticket,
            string? remoteIp,
            bool allowCommands,
            out ScadaWebTicketGrant? grant,
            out string reason,
            out string? errorCode)
        {
            grant = null;
            errorCode = null;

            if (string.IsNullOrWhiteSpace(ticket))
            {
                reason = "缺少 ticket（query ?ticket= 或 Authorization: Bearer）";
                return false;
            }

            var now = DateTime.UtcNow;

            lock (_gate)
            {
                PruneLocked(now);

                if (!_live.TryGetValue(ticket, out var found))
                {
                    // 三种情况共用一条原因：不存在 / 已过期（已被 Prune 移除）/ 已被使用。
                    // 对客户端一律 401，原因只进服务端日志（不区分 = 不给枚举口子）。
                    reason = "ticket 不存在、已过期或已被使用";
                    return false;
                }

                _live.Remove(ticket);

                if (found.IsExpired(now))
                {
                    reason = "ticket 已过期";
                    return false;
                }

                if (!string.IsNullOrEmpty(found.BoundIp)
                    && !string.Equals(found.BoundIp, remoteIp, StringComparison.Ordinal))
                {
                    reason = "ticket 绑定的来源 IP 不匹配";
                    return false;
                }

                // 匿名票在 AllowAnonymousView 关掉之后不许再进（与"匿名令牌不许续期"同一条口径）
                if (found.IsAnonymous && !AllowAnonymousView)
                {
                    reason = "匿名只读观众已被配置关闭（AllowAnonymousView=false）";
                    errorCode = ErrorSessionExpired;
                    return false;
                }

                // 核销时按当前策略压只读：配置在 30 秒有效期内被改成"禁止写"时，
                // 已签发的票也不许带着旧权限进连接。
                if (!allowCommands || found.IsAnonymous)
                    found.ReadOnly = true;

                // 核销成功 = 身份确认，此刻签发会话令牌（重连凭据）。
                // 顺序刻意放在所有检查之后：过期/绑定不符的票不该顺带留下一枚 8 小时有效的令牌。
                //
                // <b>续期路径必须沿用已有的那枚</b>（`found.Session != null`）：续期票在签发时
                // 就挂上了原令牌，若这里再签一枚，每次重连都会多出一枚"没人知道"的 8 小时令牌
                // （客户端 hello 之后才拿到新的，连接一旦在握手后失败就再也收不到），
                // 而旧令牌还留在表里——既泄漏又让"客户端存的那枚"与服务端脱节。
                if (found.Session == null)
                {
                    var capacity = MaxSessionTokens > 0 ? MaxSessionTokens : DefaultMaxSessionTokens;

                    if (_sessions.Count >= capacity)
                    {
                        // 表满：拒绝新会话（不挤掉在线的、不排队）。票已核销掉——宁可让客户端
                        // 重新取票，也不让同一张票在"别人退出后"被延迟放行（那时它可能已经过期）。
                        reason = $"会话令牌表已满（{capacity}），拒绝新会话";
                        errorCode = ErrorCapacity;
                        return false;
                    }

                    found.Session = IssueSessionLocked(found);
                    found.SessionReused = false;
                }
                else
                {
                    // 续期票沿用客户端已有的令牌：失败路径不许回收它。
                    found.SessionReused = true;
                }

                // 交接给 WS handler：只在这里写入，TryTakeHandoff 取走即删。
                _handoff[ticket] = found;
                grant = found;
                reason = string.Empty;
                return true;
            }
        }

        /// <summary>
        /// WS handler 取走"已核销"的身份（取走即删，保证一条 ticket 只喂一条连接）。
        /// </summary>
        public bool TryTakeHandoff(string? ticket, out ScadaWebTicketGrant? grant)
        {
            grant = null;

            if (string.IsNullOrWhiteSpace(ticket))
                return false;

            var now = DateTime.UtcNow;

            lock (_gate)
            {
                PruneLocked(now);

                if (!_handoff.TryGetValue(ticket, out var found))
                    return false;

                _handoff.Remove(ticket);

                if (found.IsExpired(now))
                    return false;

                // 撤销必须覆盖 handoff：票已经核销、WS 还没取走身份时，令牌可能已被
                // RevokeSession/RevokeUser 撤销。只查过期会让"被踢的会话"再完成一次握手
                // 并继续收帧到第一条命令（2026-10-21 终审 P1-1）。
                if (found.Session != null
                    && (_revokedTokens.ContainsKey(found.Session.Token)
                        || (!string.IsNullOrEmpty(found.UserName) && _revokedUsers.ContainsKey(found.UserName))))
                {
                    _sessions.Remove(found.Session.Token);
                    _revokedTokens[found.Session.Token] = now;
                    return false;
                }

                grant = found;
                return true;
            }
        }

        /// <summary>
        /// 校验一枚会话令牌（<b>可重复调用</b>，取用后不删）。
        /// </summary>
        /// <param name="token">令牌原文（<c>POST /api/ticket</c> 的 <c>refreshToken</c>）</param>
        /// <param name="remoteIp">来源 IP（未绑定 IP 的令牌忽略它）</param>
        /// <param name="session">校验通过时的身份</param>
        public bool TryResolveSession(string? token, string? remoteIp, out ScadaWebSessionGrant? session)
            => TryResolveSession(token, remoteIp, out session, out _);

        /// <summary>
        /// 校验一枚会话令牌，并给出稳定错误码（M2 形状）。
        ///
        /// <b>三条失效口径</b>（顺序即优先级）：
        ///   ① 被撤销（<see cref="RevokeSession"/> / <see cref="RevokeUser"/>）→ <see cref="ErrorSessionRevoked"/>；
        ///   ② 匿名令牌 + <see cref="AllowAnonymousView"/>=false → <see cref="ErrorSessionExpired"/>
        ///      （<b>已签发</b>的匿名令牌也一并失效，否则"关掉匿名访问"对老页面等于没发生）；
        ///   ③ 不存在 / 已过期 / 绑定 IP 不符 → <see cref="ErrorSessionExpired"/>
        ///      （不区分这三种：不给令牌枚举口子）。
        /// </summary>
        /// <param name="token">令牌原文</param>
        /// <param name="remoteIp">来源 IP</param>
        /// <param name="session">校验通过时的身份</param>
        /// <param name="errorCode">失败时的稳定错误码；成功为 <c>null</c></param>
        public bool TryResolveSession(
            string? token,
            string? remoteIp,
            out ScadaWebSessionGrant? session,
            out string? errorCode)
        {
            session = null;
            errorCode = null;

            if (string.IsNullOrWhiteSpace(token))
            {
                errorCode = ErrorSessionExpired;
                return false;
            }

            var now = DateTime.UtcNow;

            lock (_gate)
            {
                PruneLocked(now);

                if (_revokedTokens.ContainsKey(token))
                {
                    errorCode = ErrorSessionRevoked;
                    return false;
                }

                if (!_sessions.TryGetValue(token, out var found))
                {
                    errorCode = ErrorSessionExpired;   // 不存在 / 已过期（已被 Prune 移除）
                    return false;
                }

                // 整账号撤销之后，即便某枚令牌漏在表里（并发窗口）也不许再用
                if (!string.IsNullOrEmpty(found.UserName) && _revokedUsers.ContainsKey(found.UserName))
                {
                    _sessions.Remove(token);
                    _revokedTokens[token] = now;
                    errorCode = ErrorSessionRevoked;
                    return false;
                }

                // 匿名观众被配置关掉：已签发的匿名令牌同样不许续期
                if (found.IsAnonymous && !AllowAnonymousView)
                {
                    errorCode = ErrorSessionExpired;
                    return false;
                }

                if (!string.IsNullOrEmpty(found.BoundIp)
                    && !string.Equals(found.BoundIp, remoteIp, StringComparison.Ordinal))
                {
                    errorCode = ErrorSessionExpired;
                    return false;
                }

                session = found;
                return true;
            }
        }

        /// <summary>
        /// 把一枚已校验的会话令牌挂到新签发的 ticket 上（<b>续期路径</b>用）。
        ///
        /// 续期<b>不轮换令牌</b>：客户端手里那枚继续有效，避免"换票失败 = 令牌也丢了"的两头落空。
        /// </summary>
        public void BindSession(ScadaWebTicketGrant grant, ScadaWebSessionGrant session)
        {
            if (grant is null || session is null)
                return;

            grant.Session = session;
        }

        /// <summary>
        /// 撤销一枚会话令牌（<b>登出 / 单点踢出</b>）。
        ///
        /// 撤销后这枚令牌<b>立即</b>从表里消失，并且记进"已撤销"表——
        /// 下次拿它续期/换票一律 <see cref="ErrorSessionRevoked"/>（而不是"过期"），
        /// 客户端据此可以区分"到点了重新登录"与"被踢下线"。
        /// 已撤销的记录按 <see cref="SessionLifetime"/> 老化（超过原有效期就没有区分意义了）。
        /// </summary>
        public void RevokeSession(string? sessionToken)
        {
            if (string.IsNullOrWhiteSpace(sessionToken)) return;

            sessionToken = sessionToken.Trim();

            var now = DateTime.UtcNow;

            lock (_gate)
            {
                PruneLocked(now);

                // 只给"确实存在过"的令牌留撤销记录：登出接口的入参是客户端给的，
                // 拿乱码反复调它会把撤销表撑成无界增长（记录要留满 8 小时）。
                // 不存在的令牌本来就用不了，没必要记。
                if (_sessions.Remove(sessionToken))
                    _revokedTokens[sessionToken] = now;

                // 已核销但还没被 WS 取走的 handoff 也一并作废（否则它还能完成一次握手）。
                List<string>? deadHandoffs = null;
                foreach (var pair in _handoff)
                {
                    if (pair.Value.Session == null) continue;
                    if (!string.Equals(pair.Value.Session.Token, sessionToken, StringComparison.Ordinal)) continue;
                    (deadHandoffs ??= new List<string>()).Add(pair.Key);
                }

                if (deadHandoffs != null)
                {
                    foreach (var key in deadHandoffs) _handoff.Remove(key);
                }
            }
        }

        /// <summary>
        /// 撤销一个账号的<b>全部</b>会话令牌（<b>改密 / 降权 / 删号</b>之后必须调用）。
        ///
        /// 三件事一起做，少一件都会留下"改了密码但老页面还能看"的窗口：
        ///   ① 该用户已签发的会话令牌全部移出表，并逐枚记进"已撤销"表；
        ///   ② 该用户尚未核销的 ticket 全部作废（ticket 只有 30 秒，但那是"改密后还能不能进"的窗口）；
        ///   ③ 记下用户名本身——并发窗口里漏在表里的令牌，续期时按用户名再拦一次。
        /// </summary>
        public void RevokeUser(string? userName)
        {
            if (string.IsNullOrWhiteSpace(userName)) return;

            // 与 Issue 同一条归一化口径：调用方传 "alice " 也要能撤掉 "alice" 的会话。
            userName = userName.Trim();

            var now = DateTime.UtcNow;

            lock (_gate)
            {
                PruneLocked(now);

                _revokedUsers[userName] = now;

                List<string>? deadSessions = null;
                foreach (var pair in _sessions)
                {
                    if (!string.Equals(pair.Value.UserName, userName, StringComparison.OrdinalIgnoreCase)) continue;
                    (deadSessions ??= new List<string>()).Add(pair.Key);
                }

                if (deadSessions != null)
                {
                    foreach (var key in deadSessions)
                    {
                        _sessions.Remove(key);
                        _revokedTokens[key] = now;
                    }
                }

                // 未核销的票：直接作废（不必记进"已撤销"表——握手失败一律 401，没有区分语义）
                List<string>? deadTickets = null;
                foreach (var pair in _live)
                {
                    if (!string.Equals(pair.Value.UserName, userName, StringComparison.OrdinalIgnoreCase)) continue;
                    (deadTickets ??= new List<string>()).Add(pair.Key);
                }

                if (deadTickets != null)
                {
                    foreach (var key in deadTickets) _live.Remove(key);
                }

                deadTickets = null;
                foreach (var pair in _handoff)
                {
                    if (!string.Equals(pair.Value.UserName, userName, StringComparison.OrdinalIgnoreCase)) continue;
                    (deadTickets ??= new List<string>()).Add(pair.Key);
                }

                if (deadTickets != null)
                {
                    foreach (var key in deadTickets) _handoff.Remove(key);
                }
            }
        }

        /// <summary>
        /// 清空撤销记录（<b>停服时调用</b>）。停服会把整张会话表清掉，
        /// "被撤销"与"不存在"已经没有区别，留着只会让下一次启动带着上一轮的账。
        /// </summary>
        public void ClearRevocations()
        {
            lock (_gate)
            {
                _revokedTokens.Clear();
                _revokedUsers.Clear();
            }
        }

        /// <summary>
        /// 清空全部票与会话令牌（服务停止时调用）。<b>只清内存</b>，没有落盘也没有撤销广播——
        /// ticket 本来就活不过 30 秒，会话令牌则必须跟着停服一起作废
        /// （否则"停服后仍在重连的页面"能一直换到新票）。
        /// </summary>
        public void Clear()
        {
            lock (_gate)
            {
                _live.Clear();
                _handoff.Clear();
                _sessions.Clear();
                _revokedTokens.Clear();
                _revokedUsers.Clear();
            }
        }

        /// <summary>签发一枚会话令牌（<b>调用方必须已持有 <c>_gate</c></b>）</summary>
        private ScadaWebSessionGrant IssueSessionLocked(ScadaWebTicketGrant ticket)
        {
            var now = DateTime.UtcNow;

            var session = new ScadaWebSessionGrant
            {
                Token = Guid.NewGuid().ToString("N"),
                UserName = ticket.UserName,
                Role = ticket.Role,
                ReadOnly = ticket.ReadOnly,
                ExplicitReadOnly = ticket.ExplicitReadOnly,
                // 不绑定 IP：现场常有反向代理/多网卡，绑死 IP 会让"换了个网段就重连不上"。
                // 字段保留，将来要开 Origin/IP 门禁时只改这里。
                BoundIp = null,
                ExpiresAtUtc = now + SessionLifetime
            };

            _sessions[session.Token] = session;
            return session;
        }

        /// <summary>清掉三张表里过期的条目。每次读写前顺手调用，省掉一个定时器</summary>
        private void PruneLocked(DateTime nowUtc)
        {
            if (_live.Count > 0)
            {
                List<string>? dead = null;
                foreach (var pair in _live)
                {
                    if (!pair.Value.IsExpired(nowUtc)) continue;
                    (dead ??= new List<string>()).Add(pair.Key);
                }

                if (dead != null)
                {
                    foreach (var key in dead) _live.Remove(key);
                }
            }

            if (_handoff.Count > 0)
            {
                List<string>? dead = null;
                foreach (var pair in _handoff)
                {
                    if (!pair.Value.IsExpired(nowUtc)) continue;
                    (dead ??= new List<string>()).Add(pair.Key);
                }

                if (dead != null)
                {
                    foreach (var key in dead)
                    {
                        // handoff 过期 = 这张票核销后没人来取身份（握手失败/客户端立刻断开）。
                        // 它挂着的会话令牌若不回收，会在表里白占 8 小时；匿名视图开启时
                        // 可以被反复用来把 4096 张表填满（2026-10-21 终审 P1-2）。
                        if (_handoff.TryGetValue(key, out var grant) && grant.Session != null
                            && !grant.SessionReused)
                        {
                            _sessions.Remove(grant.Session.Token);
                            _revokedTokens[grant.Session.Token] = nowUtc;
                        }

                        _handoff.Remove(key);
                    }
                }
            }

            if (_sessions.Count > 0)
            {
                List<string>? dead = null;
                foreach (var pair in _sessions)
                {
                    if (!pair.Value.IsExpired(nowUtc)) continue;
                    (dead ??= new List<string>()).Add(pair.Key);
                }

                if (dead != null)
                {
                    foreach (var key in dead) _sessions.Remove(key);
                }
            }

            // 撤销记录只保留一个会话生命周期：超过 SessionLifetime 之后，
            // 那枚令牌本来也已经过期，"被撤销"与"已过期"对客户端没有区别。
            PruneRevocationsLocked(_revokedTokens, nowUtc);
            PruneRevocationsLocked(_revokedUsers, nowUtc);
        }

        private static void PruneRevocationsLocked(Dictionary<string, DateTime> table, DateTime nowUtc)
        {
            if (table.Count == 0) return;

            List<string>? dead = null;
            foreach (var pair in table)
            {
                if (nowUtc - pair.Value < SessionLifetime) continue;
                (dead ??= new List<string>()).Add(pair.Key);
            }

            if (dead != null)
            {
                foreach (var key in dead) table.Remove(key);
            }
        }
    }
}
