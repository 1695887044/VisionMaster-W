using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using VisionMaster.Scada;

namespace VisionMaster.ScadaWeb
{
    /// <summary>
    /// SCADA Web 的<b>安全前置闸门</b>（M2 前必须补的四项：Origin 校验、取票限速、
    /// 出厂默认密码闸门、会话/账号撤销）。
    ///
    /// <b>为什么单独一个类、而不是塞进 <see cref="ScadaWebHost"/></b>
    /// ---------
    /// 这四项的判据都是"纯函数 + 一张内存表"，与 Watson、UI 线程、快照都没有关系：
    /// 放进宿主会让它们只能"起一个真服务再打 HTTP"才验得到（慢、脆、失败原因埋在日志里）；
    /// 独立出来之后断言可以直接调（见 <c>ScadaWebChecks\M2SecurityChecks.cs</c>），
    /// 宿主只负责在正确的时刻问一句。
    ///
    /// <b>四道闸分别挡什么</b>
    /// ---------
    ///   ① <see cref="IsOriginAllowed"/>：<b>跨站页面借用受害者的浏览器连 WS</b>。
    ///      WS 不受同源策略约束（浏览器不校验响应头），服务端不看 <c>Origin</c> 就等于
    ///      "任何网页都能开一条到产线画面的连接"。判据是"<c>Origin</c> 的主机与 <c>Host</c> 一致"
    ///      （scheme 允许 http/https，端口按 <c>Host</c> 头算）；没有 <c>Origin</c> 的请求
    ///      （curl / 服务端网关 / 同源导航）放行——它们不是"被别的页面驱使的浏览器"。
    ///   ② <see cref="TryCheckTicketRateLimit"/>：<b><c>POST /api/ticket</c> 被当成密码爆破入口</b>。
    ///      取票是唯一"不需要任何凭据也能打"的写路径（匿名只读观众连密码都不用），
    ///      不设上限时一个脚本每分钟能打几千次，PBKDF2 十万次会把 CPU 打满（等于免费放大器）。
    ///   ③ <see cref="TryCheckDefaultPassword"/>：<b>出厂密码直接被拿到公网用</b>。
    ///      默认密码是公开知识，改密之前"登录成功"不代表任何安全性——登录成功也不许进。
    ///   ④ <see cref="RevokeSession"/> / <see cref="RevokeUser"/>：<b>改密/降权/登出之后老页面还能看</b>。
    ///      会话令牌 8 小时有效，没有撤销通道时"把操作员降成只读"要等 8 小时才生效。
    ///
    /// <b>线程</b>：全部成员线程安全（限速表用 <see cref="ConcurrentDictionary{TKey,TValue}"/> +
    /// 每键独立锁；撤销表委托给 <see cref="ScadaWebTicketService"/> 的 <c>_gate</c>）。
    /// Watson 的 HTTP/握手回调是并发的，本类可以被多线程同时调用。
    /// </summary>
    public sealed class ScadaWebSecurityGate
    {
        /// <summary>
        /// 同一来源在 <see cref="TicketRateWindow"/> 内允许取票的<b>默认</b>次数（= <c>ScadaWeb.TicketRatePerMinute</c> 的默认值）。
        /// 实际额度读 <see cref="TicketRatePerMinute"/>：产品路径由宿主按配置写进来，
        /// 断言与探针不设它就落到这个默认值（保持既有断言口径不变）。
        /// </summary>
        public const int TicketRateLimitPerWindow = 10;

        /// <summary>取票限速的滑动窗口（1 分钟）</summary>
        public static readonly TimeSpan TicketRateWindow = TimeSpan.FromMinutes(1);

        /// <summary>取票过快（HTTP 429）</summary>
        public const string ErrorRateLimited = "E_RATE_LIMITED";

        /// <summary>账号仍在使用出厂默认密码（登录拒绝）</summary>
        public const string ErrorDefaultPassword = "E_DEFAULT_PASSWORD";

        /// <summary>Origin 与 Host 不同源（握手 403）</summary>
        public const string ErrorOrigin = "E_ORIGIN";

        /// <summary>取不到来源地址时用的键（仍然限速：宁可误伤一条，也不给"无地址 = 无限"的口子）</summary>
        private const string UnknownClient = "<unknown>";

        /// <summary>限速表条目超过这个数才开始清扫（小规模部署根本走不到）</summary>
        private const int SweepThreshold = 256;

        private readonly ScadaWebTicketService _tickets;
        private readonly Func<DateTime> _utcNow;
        private readonly ConcurrentDictionary<string, TicketWindow> _windows = new(StringComparer.Ordinal);
        private int _sweepCounter;

        /// <param name="tickets">会话/票的权威表（撤销委托给它；<b>必须是宿主实际使用的那一份</b>）</param>
        /// <param name="utcNow">
        /// 时钟（断言用来把"1 分钟窗口"快进过去，产品路径传 <c>null</c> = <see cref="DateTime.UtcNow"/>）。
        /// 传进来而不是各处直接读 <c>UtcNow</c>：限速窗口的过期判据否则只能靠真等 60 秒来验。
        /// </param>
        public ScadaWebSecurityGate(ScadaWebTicketService tickets, Func<DateTime>? utcNow = null)
        {
            _tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>自建票表的独立闸门（探针/单测用；产品路径请传宿主的那一份）</summary>
        public ScadaWebSecurityGate()
            : this(new ScadaWebTicketService())
        {
        }

        /// <summary>本闸门委托的票/会话表（宿主接线时用它设 <c>AllowAnonymousView</c>）</summary>
        public ScadaWebTicketService Tickets => _tickets;

        /// <summary>
        /// 取票限速额度（<b>每个来源 IP 每分钟</b>）。产品路径由宿主接 <c>ScadaWebSettings.TicketRatePerMinute</c>；
        /// 默认 <see cref="TicketRateLimitPerWindow"/>。
        ///
        /// <b>为什么是可写属性而不是构造参数</b>：构造参数会让既有的 <c>new ScadaWebSecurityGate(tickets, utcNow)</c>
        /// 调用点全部要改，而"额度"是配置项、不是身份依赖——属性更贴合它的语义。
        /// 取值在判定时夹到 <c>1~600</c>（<c>0</c>/负数 = 每轮都超限，等于把取票口彻底关死）。
        /// </summary>
        public int TicketRatePerMinute { get; set; } = TicketRateLimitPerWindow;

        /// <summary>当前被限速跟踪的来源数（诊断用；<see cref="Reset"/> 之后归零）</summary>
        public int TrackedClients => _windows.Count;

        #region ① Origin 校验

        /// <summary>Origin 是否与本次请求的 <c>Host</c> 同源（不关心原因时用这个重载）</summary>
        public bool IsOriginAllowed(string? origin, string? host)
            => IsOriginAllowed(origin, host, out _);

        /// <summary>
        /// Origin 是否与本次请求的 <c>Host</c> 同源。用于 <c>POST /api/ticket</c> 与 WS 握手前。
        ///
        /// <b>判据（逐条，缺一条都会被绕过）</b>
        ///   · <b>没有 Origin</b>（<c>null</c>/空白）→ <b>放行</b>。浏览器发起跨站请求时<b>一定</b>带
        ///     <c>Origin</c>，所以"没有"意味着不是被别的页面驱使的浏览器（curl、服务端网关、同源导航）；
        ///     拿它当拒绝条件会把所有非浏览器客户端一起关在门外。
        ///   · <c>Origin: null</c>（沙箱 iframe / <c>file://</c> / 部分重定向）→ <b>拒绝</b>：
        ///     它是"来源被刻意抹掉"的信号，不是同源。
        ///   · scheme 只允许 <c>http</c>/<c>https</c>（现场前面挂反向代理时是 http，直连 https 是另一种）；
        ///     其它 scheme（<c>file:</c>、<c>chrome-extension:</c>、<c>app:</c>）一律拒绝。
        ///   · 主机名逐字比较（忽略大小写，<b>不做</b> <c>localhost</c> ↔ <c>127.0.0.1</c> 之类的别名等价：
        ///     两者在浏览器的"源"里就是两个不同的源，替它们互相认账等于自己开一条绕过路径）；
        ///     IPv6 的方括号只做"归一化后比较"（<c>Host: [::1]:8080</c> 与 <c>Origin: http://[::1]:8080</c>
        ///     是同一个源，<see cref="Uri.Host"/> 保留方括号、Host 头也带方括号，两边都剥掉再比）。
        ///   · 端口必须相等：<c>Host</c> 显式带端口时按它比；<b>没带端口时按 Origin 的 scheme 默认端口</b>
        ///     （<c>http</c>=80、<c>https</c>=443）比——浏览器对默认端口会省略，
        ///     所以 <c>Origin: http://h:8080</c> + <c>Host: h</c> 是<b>两个不同的源</b>，必须拒绝。
        /// </summary>
        /// <param name="origin">请求头 <c>Origin</c>（可为 <c>null</c>）</param>
        /// <param name="host">请求头 <c>Host</c>（可含端口；IPv6 用 <c>[::1]:8080</c> 形状）</param>
        /// <param name="error">不匹配时的可读原因（带稳定码 <see cref="ErrorOrigin"/>；匹配时为 <c>null</c>）</param>
        public bool IsOriginAllowed(string? origin, string? host, out string? error)
        {
            error = null;

            // 没有 Origin = 不是被别的页面驱使的浏览器。放行。
            if (string.IsNullOrWhiteSpace(origin))
                return true;

            var text = origin.Trim();

            // "Origin: null"：沙箱 iframe / file:// / 某些跨站重定向。这是"来源被抹掉"，不是同源。
            if (string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
            {
                error = $"{ErrorOrigin}：Origin 为 null（沙箱 iframe / file://），不允许";
                return false;
            }

            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                error = $"{ErrorOrigin}：Origin 不是合法的绝对 URI";
                return false;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                error = $"{ErrorOrigin}：Origin 的 scheme「{uri.Scheme}」不受支持（只允许 http/https）";
                return false;
            }

            if (string.IsNullOrWhiteSpace(host))
            {
                error = $"{ErrorOrigin}：缺少 Host 头，无法判断是否同源";
                return false;
            }

            if (!TrySplitHostAndPort(host, out var hostName, out var hostPort))
            {
                error = $"{ErrorOrigin}：Host 头无法解析";
                return false;
            }

            if (!string.Equals(NormalizeHost(hostName), NormalizeHost(uri.Host), StringComparison.OrdinalIgnoreCase))
            {
                error = $"{ErrorOrigin}：Origin 主机「{uri.Host}」与 Host「{hostName}」不一致";
                return false;
            }

            var originPort = uri.IsDefaultPort ? DefaultPortOf(uri.Scheme) : uri.Port;

            // Host 没写端口 = 这次请求走的是该 scheme 的默认端口（浏览器对默认端口会省略）。
            // 注意这里<b>不能</b>因为"Host 没写端口"就跳过端口比较：
            // Origin 是 http://h:8080、Host 是 h，那是两个不同的源（一个 8080、一个 80）。
            var hostEffectivePort = hostPort ?? DefaultPortOf(uri.Scheme);

            if (hostEffectivePort != originPort)
            {
                error = $"{ErrorOrigin}：Origin 端口 {originPort} 与 Host 端口 {hostEffectivePort} 不一致";
                return false;
            }

            return true;
        }

        /// <summary>IPv6 的方括号归一化（<c>[::1]</c> 与 <c>::1</c> 视为同一个主机）</summary>
        private static string NormalizeHost(string host)
        {
            var text = host.Trim();

            return text.Length > 1 && text[0] == '[' && text[text.Length - 1] == ']'
                ? text.Substring(1, text.Length - 2)
                : text;
        }

        private static int DefaultPortOf(string scheme)
            => string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80;

        /// <summary>
        /// 拆 <c>Host</c> 头：<c>host[:port]</c>，IPv6 是 <c>[::1][:port]</c>。
        /// 裸 IPv6（没有方括号、也没有端口）里冒号不止一个，按"没有端口"处理。
        /// </summary>
        private static bool TrySplitHostAndPort(string host, out string name, out int? port)
        {
            name = string.Empty;
            port = null;

            var text = host.Trim();
            if (text.Length == 0) return false;

            if (text[0] == '[')
            {
                var close = text.IndexOf(']');
                if (close < 0) return false;

                name = text.Substring(1, close - 1);
                if (name.Length == 0) return false;

                var rest = text.Substring(close + 1);
                if (rest.Length == 0) return true;
                if (rest[0] != ':') return false;

                return TryParsePort(rest.Substring(1), out port);
            }

            var colon = text.LastIndexOf(':');

            // 没有冒号，或多于一个冒号（裸 IPv6）：整串都是主机名
            if (colon < 0 || text.IndexOf(':') != colon)
            {
                name = text;
                return name.Length > 0;
            }

            name = text.Substring(0, colon);
            if (name.Length == 0) return false;

            return TryParsePort(text.Substring(colon + 1), out port);
        }

        private static bool TryParsePort(string text, out int? port)
        {
            port = null;

            // "host:" 视为没写端口（浏览器不会这么发，但别因此判死）
            if (text.Length == 0) return true;

            // NumberStyles.None：不许符号、空白、千分位——"80 80" 这种不该被当成 8080
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                return false;

            if (value <= 0 || value > 65535) return false;

            port = value;
            return true;
        }

        #endregion

        #region ② 取票限速（每 IP 10 次/分钟）

        /// <summary>
        /// 记一次取票请求并判定是否超限（<b>每 IP 滑动窗口</b>）。
        ///
        /// <b>为什么是滑动窗口而不是固定分钟桶</b>：固定桶在整分钟边界上允许"上一分钟末尾 10 次
        /// + 这一分钟开头 10 次"，瞬间 20 次；滑动窗口没有这个边界。
        ///
        /// <b>被拒绝的那一次不消耗额度</b>：窗口按"最近 60 秒内<b>放行</b>的次数"算，
        /// 所以超限之后持续重试不会把窗口越推越远（否则一个脚本能让自己被永久锁死，
        /// 而现场正常的客户端也永远等不到恢复）。
        /// </summary>
        /// <param name="clientAddress">来源地址（IP；<c>null</c>/空白按 <c>&lt;unknown&gt;</c> 共享一个额度）</param>
        /// <param name="error">超限时的可读原因（带 <see cref="ErrorRateLimited"/>）</param>
        public bool TryCheckTicketRateLimit(string? clientAddress, out string? error)
        {
            error = null;

            var key = string.IsNullOrWhiteSpace(clientAddress) ? UnknownClient : clientAddress.Trim();
            var now = _utcNow();
            var limit = EffectiveTicketRatePerMinute;

            var window = _windows.GetOrAdd(key, static _ => new TicketWindow());

            if (!window.TryHit(now, TicketRateWindow, limit))
            {
                error = $"{ErrorRateLimited}：同一来源每分钟最多取 {limit} 次 ticket，请稍后重试";
                return false;
            }

            SweepExpiredWindows(now);
            return true;
        }

        /// <summary>
        /// 生效额度：<see cref="TicketRatePerMinute"/> 夹到 <c>1~600</c>。
        /// 夹取而不是"照配置用"：<c>0</c> 会让每一次取票都被拒（连登录都进不去，且现场看不出是配置问题），
        /// 负数同理；上限 600 只是防止一个荒谬的数字把内存与 CPU 放大。
        /// </summary>
        private int EffectiveTicketRatePerMinute
            => Math.Clamp(TicketRatePerMinute, 1, 600);

        /// <summary>
        /// 清掉已经空了的来源（否则"每来一个新 IP 就多一个键"，长期运行会慢慢涨）。
        /// 每 64 次调用扫一遍、且只有表超过 <see cref="SweepThreshold"/> 才扫——
        /// 常驻会话通常只有几个 IP，不需要每次取票都遍历一遍。
        /// </summary>
        private void SweepExpiredWindows(DateTime now)
        {
            if (_windows.Count <= SweepThreshold) return;

            if ((System.Threading.Interlocked.Increment(ref _sweepCounter) & 0x3F) != 0) return;

            foreach (var pair in _windows)
            {
                // 先摘出来再看是否真过期；万一它刚被写入新的一次，放回去。
                // （"检查后删除"之间的窗口会让该来源多一次额度，属于可接受的极小误差。）
                if (!_windows.TryRemove(pair.Key, out var removed)) continue;

                if (!removed.IsExpired(now, TicketRateWindow))
                    _windows.TryAdd(pair.Key, removed);
            }
        }

        #endregion

        #region ③ 出厂默认密码闸门

        /// <summary>
        /// 登录用户是否仍在使用出厂默认密码。用于"账号校验通过之后、签票之前"。
        ///
        /// <b>三条口径</b>
        ///   · <b>只查登录用户</b>：<paramref name="userName"/> 为空（匿名只读观众）直接放行——
        ///     它没有密码可改，把它拦下来等于连"只读看画面"都不许。
        ///   · <b>查不出来 = 拒绝</b>（fail closed）：默认密码闸门的全部意义是挡住出厂密码，
        ///     "无法确认状态"与"确认是默认密码"在风险上等价；放行等于给实现方一个静默的旁路。
        ///   · 账号校验器缺席（宿主没注册）→ 放行：那种情况下登录根本不可能成功，
        ///     在这里再判死只会让"Web 起来了但一直说默认密码"更难排查。
        /// </summary>
        /// <param name="authenticator">账号校验出口（可为 <c>null</c>）</param>
        /// <param name="userName">登录用户名；<c>null</c>/空白 = 匿名观众（不查）</param>
        /// <param name="error">拒绝原因（带 <see cref="ErrorDefaultPassword"/>）</param>
        public bool TryCheckDefaultPassword(IScadaUserAuthenticator? authenticator, string? userName, out string? error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(userName)) return true;
            if (authenticator == null) return true;

            bool isDefault;

            try
            {
                isDefault = authenticator.IsUsingDefaultPassword(userName);
            }
            catch (Exception ex)
            {
                error = $"{ErrorDefaultPassword}：无法确认账号「{userName}」的密码状态（{ex.GetType().Name}），已拒绝登录";
                return false;
            }

            if (!isDefault) return true;

            error = $"{ErrorDefaultPassword}：账号「{userName}」仍在使用出厂默认密码，请先在桌面端修改密码后再远程登录";
            return false;
        }

        #endregion

        #region ④ 撤销（登出 / 改密 / 降权）

        /// <summary>撤销一枚会话令牌（登出、单点踢出）。之后续期/换票一律 <c>E_SESSION_REVOKED</c></summary>
        public void RevokeSession(string? sessionToken) => _tickets.RevokeSession(sessionToken);

        /// <summary>
        /// 撤销一个账号的全部会话与未核销票（改密 / 降权 / 删号之后调用）。
        /// 语义见 <see cref="ScadaWebTicketService.RevokeUser"/>。
        /// </summary>
        public void RevokeUser(string? userName) => _tickets.RevokeUser(userName);

        /// <summary>
        /// 停服清空：限速窗口 + 撤销记录。
        ///
        /// <b>只清这两张表</b>：会话/票本身由 <see cref="ScadaWebTicketService.Clear"/> 在停服时清
        /// （宿主 <c>Stop</c> 已经这么做）。两处分开是为了"停服只清撤销账"这种场景不会顺手
        /// 把还在线的会话表清掉——那会让正在收尾的连接拿到不一致的身份。
        /// </summary>
        public void Reset()
        {
            _windows.Clear();
            _tickets.ClearRevocations();
        }

        #endregion

        /// <summary>
        /// 单个来源的滑动窗口（<b>每键一把锁</b>：不同 IP 之间不互相阻塞）。
        /// 队列里只留窗口内的时刻，长度上限就是额度（10），所以内存是 O(1)。
        /// </summary>
        private sealed class TicketWindow
        {
            private readonly Queue<DateTime> _hits = new();
            private readonly object _gate = new();

            public bool TryHit(DateTime now, TimeSpan window, int limit)
            {
                lock (_gate)
                {
                    var cutoff = now - window;

                    while (_hits.Count > 0 && _hits.Peek() <= cutoff)
                        _hits.Dequeue();

                    if (_hits.Count >= limit)
                        return false;

                    _hits.Enqueue(now);
                    return true;
                }
            }

            public bool IsExpired(DateTime now, TimeSpan window)
            {
                lock (_gate)
                {
                    return _hits.Count == 0 || _hits.Peek() <= now - window;
                }
            }
        }
    }
}
