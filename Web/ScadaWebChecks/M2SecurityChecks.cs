using System;
using System.Collections.Generic;
using VisionMaster.Scada;
using VisionMaster.ScadaWeb;

namespace ScadaWebChecks
{
    /// <summary>
    /// M2 前四项安全前置的断言（<b>纯内存</b>，不起服务、不开端口、不依赖 Watson）。
    ///
    /// 覆盖：
    ///   S1  Origin 同源通过（http/https、带端口、默认端口）
    ///   S2  Origin 跨源拒绝（主机名不一致）
    ///   S3  无 Origin 通过（非浏览器 / 同源导航）
    ///   S4  Origin 与 Host 端口不一致拒绝
    ///   S5  Origin 为 "null" / 非 http(s) scheme 拒绝
    ///   S6  每 IP 第 11 次取票被拒（E_RATE_LIMITED），别的 IP 不受影响
    ///   S7  限速窗口（60s）过后恢复
    ///   S8  出厂默认密码账号被拒（E_DEFAULT_PASSWORD），已改密的账号放行
    ///   S9  匿名观众不查默认密码；校验器抛异常按"拒绝"处理（fail closed）
    ///   S10 匿名令牌在 AllowAnonymousView=false 后不能续期（E_SESSION_EXPIRED）
    ///   S11 撤销单枚会话 → 该令牌 E_SESSION_REVOKED，同用户其它令牌不受影响
    ///   S12 撤销整账号 → 该用户全部令牌失效（E_SESSION_REVOKED），别人不受影响
    ///   S13 会话令牌表上限拒绝（E_CAPACITY）
    ///   S14 只读口径：allowCommands=false / 匿名 → 只读；登录用户按配置可写
    ///   S15 核销时按当前策略再压一次只读（已签发的可写票也能被降权）
    ///   S16 ticket 仍是"30 秒 + 单次"（本轮改动没碰这条语义）
    ///   S17 Reset() 清空限速窗口与撤销记录
    ///   S18 账号级撤销只挡旧令牌：重新登录后的新令牌可以正常续期
    ///
    /// <b>为什么这些断言不放进 <c>Program.Main</c> 的 HTTP 流程里</b>：四项闸门的判据都是纯函数 +
    /// 一张内存表，起真服务只会让失败原因埋在日志里；这里直接调，失败时能打印"期望 vs 实际"。
    /// 接线只有一行（见文件末尾注释）。
    /// </summary>
    internal static class M2SecurityChecks
    {
        private static int _failed;
        private static int _total;

        /// <summary>本段通过的条数（Program 用它并入总 PASS 数；只影响计数，不改变任何断言）</summary>
        public static int Pass => _total - _failed;

        /// <summary>本段执行的断言总条数</summary>
        public static int Total => _total;

        /// <summary>跑完全部 M2 安全断言，返回<b>失败条数</b>（0 = 全绿）</summary>
        public static int Run()
        {
            _failed = 0;
            _total = 0;

            void Check(string id, bool ok, string detail)
            {
                _total++;
                Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + id + "  " + detail);
                if (!ok) _failed++;
            }

            Console.WriteLine();
            Console.WriteLine("--- M2 安全前置（Origin / 限速 / 默认密码 / 撤销） ---");

            // ---------------- S1~S5：Origin ----------------
            {
                var gate = new ScadaWebSecurityGate();

                var sameOriginHttp = gate.IsOriginAllowed("http://127.0.0.1:8080", "127.0.0.1:8080", out var sameOriginError);
                var sameOriginHttps = gate.IsOriginAllowed("https://scada.local:8443", "scada.local:8443");
                var defaultPort = gate.IsOriginAllowed("http://scada.local", "scada.local");
                var httpsDefaultPort = gate.IsOriginAllowed("https://scada.local", "scada.local");

                Check("S1", sameOriginHttp && sameOriginHttps && defaultPort && httpsDefaultPort && sameOriginError == null,
                    $"http={sameOriginHttp} https={sameOriginHttps} defaultPort={defaultPort} httpsDefaultPort={httpsDefaultPort}"
                    + $" err={sameOriginError ?? "<null>"}");

                var crossHost = gate.IsOriginAllowed("http://evil.example", "127.0.0.1:8080", out var crossHostError);
                var crossPort = gate.IsOriginAllowed("http://127.0.0.1:9090", "127.0.0.1:8080", out var crossPortError);
                var aliasHost = gate.IsOriginAllowed("http://localhost:8080", "127.0.0.1:8080");

                Check("S2", !crossHost && !crossPort && !aliasHost
                        && (crossHostError ?? string.Empty).Contains(ScadaWebSecurityGate.ErrorOrigin, StringComparison.Ordinal)
                        && (crossPortError ?? string.Empty).Contains(ScadaWebSecurityGate.ErrorOrigin, StringComparison.Ordinal),
                    $"crossHost={crossHost}（{crossHostError}）"
                    + $" crossPort={crossPort}（{crossPortError}）"
                    + $" localhost!=127.0.0.1: {!aliasHost}");

                var noOrigin = gate.IsOriginAllowed(null, "127.0.0.1:8080");
                var emptyOrigin = gate.IsOriginAllowed("   ", "127.0.0.1:8080");

                Check("S3", noOrigin && emptyOrigin, $"null={noOrigin} empty={emptyOrigin}（无 Origin = 非浏览器，放行）");

                var hostWithoutPort = gate.IsOriginAllowed("http://127.0.0.1:8080", "127.0.0.1");
                var hostWithPort = gate.IsOriginAllowed("http://127.0.0.1", "127.0.0.1:8080");
                var ipv6 = gate.IsOriginAllowed("http://[::1]:8080", "[::1]:8080");

                Check("S4", !hostWithoutPort && !hostWithPort && ipv6,
                    $"Host 无端口但 Origin 带 8080 → {hostWithoutPort}（应拒绝）"
                    + $"；Origin 默认端口但 Host 带 8080 → {hostWithPort}（应拒绝）"
                    + $"；IPv6 同源 → {ipv6}");

                var nullOrigin = gate.IsOriginAllowed("null", "127.0.0.1:8080", out var nullOriginError);
                var fileScheme = gate.IsOriginAllowed("file:///C:/x.html", "127.0.0.1:8080", out var fileError);
                var extensionScheme = gate.IsOriginAllowed("chrome-extension://abcdef", "127.0.0.1:8080", out var extError);
                var badUri = gate.IsOriginAllowed("not a uri", "127.0.0.1:8080", out var badUriError);

                Check("S5", !nullOrigin && !fileScheme && !extensionScheme && !badUri
                        && (nullOriginError ?? string.Empty).Contains(ScadaWebSecurityGate.ErrorOrigin, StringComparison.Ordinal)
                        && (fileError ?? string.Empty).Contains(ScadaWebSecurityGate.ErrorOrigin, StringComparison.Ordinal)
                        && (extError ?? string.Empty).Contains(ScadaWebSecurityGate.ErrorOrigin, StringComparison.Ordinal)
                        && (badUriError ?? string.Empty).Contains(ScadaWebSecurityGate.ErrorOrigin, StringComparison.Ordinal),
                    $"null={nullOrigin}（{nullOriginError}） file={fileScheme}（{fileError}）"
                    + $" extension={extensionScheme}（{extError}） badUri={badUri}（{badUriError}）");
            }

            // ---------------- S6~S7：取票限速（每 IP 10 次/分钟） ----------------
            {
                var now = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
                var gate = new ScadaWebSecurityGate(new ScadaWebTicketService(), () => now);

                var allowed = 0;
                string? lastError = null;

                for (var i = 0; i < ScadaWebSecurityGate.TicketRateLimitPerWindow; i++)
                {
                    if (gate.TryCheckTicketRateLimit("192.168.1.10", out lastError)) allowed++;
                }

                var eleventh = gate.TryCheckTicketRateLimit("192.168.1.10", out var eleventhError);
                var otherIp = gate.TryCheckTicketRateLimit("192.168.1.11", out var otherError);

                Check("S6",
                    allowed == ScadaWebSecurityGate.TicketRateLimitPerWindow
                    && !eleventh && otherIp
                    && (eleventhError ?? string.Empty).Contains(ScadaWebSecurityGate.ErrorRateLimited, StringComparison.Ordinal)
                    && otherError == null,
                    $"前 {ScadaWebSecurityGate.TicketRateLimitPerWindow} 次放行 {allowed}；"
                    + $"第 11 次={eleventh}（{eleventhError}）；另一 IP={otherIp}（{otherError ?? "<null>"}）");

                now = now.AddSeconds(61);
                var afterWindow = gate.TryCheckTicketRateLimit("192.168.1.10", out var afterError);

                Check("S7", afterWindow && afterError == null,
                    $"窗口过后同一 IP 再次取票={afterWindow}（{afterError ?? "<null>"}）；跟踪来源数={gate.TrackedClients}");
            }

            // ---------------- S8~S9：出厂默认密码 ----------------
            {
                var gate = new ScadaWebSecurityGate();
                var auth = new FakeAuthenticator("admin");

                var admin = gate.TryCheckDefaultPassword(auth, "admin", out var adminError);
                var operatorUser = gate.TryCheckDefaultPassword(auth, "op", out var operatorError);
                var anonymous = gate.TryCheckDefaultPassword(auth, userName: null, out var anonymousError);
                var noAuthenticator = gate.TryCheckDefaultPassword(authenticator: null, "admin", out _);

                Check("S8",
                    !admin && operatorUser && anonymous && anonymousError == null
                    && (adminError ?? string.Empty).Contains(ScadaWebSecurityGate.ErrorDefaultPassword, StringComparison.Ordinal)
                    && operatorError == null && noAuthenticator,
                    $"默认密码 admin 放行={admin}（{adminError}）；已改密 op 放行={operatorUser}"
                    + $"；未注册校验器放行={noAuthenticator}");

                var throwing = new ThrowingAuthenticator();
                var throwingAllowed = gate.TryCheckDefaultPassword(throwing, "admin", out var throwingError);

                Check("S9", anonymous && !throwingAllowed
                        && (throwingError ?? string.Empty).Contains(ScadaWebSecurityGate.ErrorDefaultPassword, StringComparison.Ordinal),
                    $"匿名不查默认密码={anonymous}（{anonymousError ?? "<null>"}）；"
                    + $"校验器抛异常 → 拒绝={!throwingAllowed}（{throwingError}）");
            }

            // ---------------- S10：匿名令牌在 AllowAnonymousView=false 后不能续期 ----------------
            {
                var tickets = new ScadaWebTicketService();
                var anonymousTicket = tickets.Issue(userName: null, readOnly: true);

                var redeemed = tickets.TryRedeem(anonymousTicket.Ticket, remoteIp: null, out var grant, out var reason);
                var token = grant?.Session?.Token;

                var beforeResolve = tickets.TryResolveSession(token, remoteIp: null, out _, out var beforeError);

                tickets.AllowAnonymousView = false;

                var afterResolve = tickets.TryResolveSession(token, remoteIp: null, out _, out var afterError);

                // 关掉匿名之后，连"已签发但未核销的匿名票"也不许再进
                var secondAnonymousTicket = tickets.Issue(userName: null, readOnly: true);
                var secondRedeem = tickets.TryRedeem(secondAnonymousTicket.Ticket, remoteIp: null, allowCommands: true, out _, out _, out var secondError);

                Check("S10",
                    redeemed && !string.IsNullOrEmpty(token)
                    && beforeResolve && beforeError == null
                    && !afterResolve && afterError == ScadaWebTicketService.ErrorSessionExpired
                    && !secondRedeem && secondError == ScadaWebTicketService.ErrorSessionExpired,
                    $"redeemed={redeemed}（{reason}） token={Short(token)}"
                    + $"；关闭前续期={beforeResolve}（{beforeError ?? "<null>"}）"
                    + $"；关闭后续期={afterResolve}（{afterError}）"
                    + $"；关闭后匿名握手={secondRedeem}（{secondError}）");
            }

            // ---------------- S11：撤销单枚会话 ----------------
            {
                var tickets = new ScadaWebTicketService();
                var gate = new ScadaWebSecurityGate(tickets);

                var first = RedeemFor(tickets, "alice");
                var second = RedeemFor(tickets, "alice");
                var other = RedeemFor(tickets, "bob");

                var countBefore = tickets.SessionCount;

                gate.RevokeSession(first.Token);

                var firstResolve = tickets.TryResolveSession(first.Token, remoteIp: null, out _, out var firstError);
                var secondResolve = tickets.TryResolveSession(second.Token, remoteIp: null, out _, out var secondError);
                var otherResolve = tickets.TryResolveSession(other.Token, remoteIp: null, out _, out var otherError);

                Check("S11",
                    !firstResolve && firstError == ScadaWebTicketService.ErrorSessionRevoked
                    && secondResolve && secondError == null
                    && otherResolve && otherError == null
                    && tickets.SessionCount == countBefore - 1,
                    $"撤销第 1 枚后：该令牌续期={firstResolve}（{firstError}）"
                    + $"；同用户第 2 枚={secondResolve}（{secondError ?? "<null>"}）"
                    + $"；别人={otherResolve}（{otherError ?? "<null>"}）"
                    + $"；SessionCount {countBefore}→{tickets.SessionCount}");
            }

            // ---------------- S12：撤销整账号 ----------------
            {
                var tickets = new ScadaWebTicketService();
                var gate = new ScadaWebSecurityGate(tickets);

                var aliceA = RedeemFor(tickets, "alice");
                var aliceB = RedeemFor(tickets, "alice");
                var bob = RedeemFor(tickets, "bob");

                // 撤销前先留一张未核销的票：撤销账号必须连它一起作废
                var pending = tickets.Issue("alice", readOnly: false);

                gate.RevokeUser("alice");

                var aResolve = tickets.TryResolveSession(aliceA.Token, remoteIp: null, out _, out var aError);
                var bResolve = tickets.TryResolveSession(aliceB.Token, remoteIp: null, out _, out var bError);
                var bobResolve = tickets.TryResolveSession(bob.Token, remoteIp: null, out _, out var bobError);
                var pendingRedeem = tickets.TryRedeem(pending.Ticket, remoteIp: null, allowCommands: true, out _, out _, out _);

                Check("S12",
                    !aResolve && aError == ScadaWebTicketService.ErrorSessionRevoked
                    && !bResolve && bError == ScadaWebTicketService.ErrorSessionRevoked
                    && bobResolve && bobError == null
                    && !pendingRedeem
                    && tickets.SessionCount == 1,
                    $"alice 两枚都失效={!aResolve && !bResolve}（{aError}/{bError}）"
                    + $"；bob 不受影响={bobResolve}（{bobError ?? "<null>"}）"
                    + $"；未核销票被作废={!pendingRedeem}；SessionCount={tickets.SessionCount}");
            }

            // ---------------- S13：会话表上限 ----------------
            {
                var tickets = new ScadaWebTicketService { MaxSessionTokens = 2 };

                var first = tickets.Issue("op1", readOnly: false);
                var second = tickets.Issue("op2", readOnly: false);
                var third = tickets.Issue("op3", readOnly: false);

                var firstOk = tickets.TryRedeem(first.Ticket, null, allowCommands: true, out _, out _, out var firstError);
                var secondOk = tickets.TryRedeem(second.Ticket, null, allowCommands: true, out _, out _, out var secondError);
                var thirdOk = tickets.TryRedeem(third.Ticket, null, allowCommands: true, out var thirdGrant, out var thirdReason, out var thirdError);

                Check("S13",
                    firstOk && secondOk && !thirdOk
                    && thirdGrant == null
                    && thirdError == ScadaWebTicketService.ErrorCapacity
                    && tickets.SessionCount == 2,
                    $"上限 2：第 1/2 张={firstOk}/{secondOk}（{firstError ?? "-"}/{secondError ?? "-"}）"
                    + $"；第 3 张={thirdOk}（{thirdError}：{thirdReason}）；SessionCount={tickets.SessionCount}");
            }

            // ---------------- S14~S15：只读口径 ----------------
            {
                var tickets = new ScadaWebTicketService();

                var writable = tickets.Issue("op", readOnly: false, allowCommands: true);
                var readOnlyByPolicy = tickets.Issue("op", readOnly: false, allowCommands: false);
                var anonymous = tickets.Issue(userName: null, readOnly: false, allowCommands: true);

                Check("S14",
                    !writable.ReadOnly && readOnlyByPolicy.ReadOnly && anonymous.ReadOnly,
                    $"登录+allowCommands=true → readOnly={writable.ReadOnly}（期望 false）"
                    + $"；登录+allowCommands=false → readOnly={readOnlyByPolicy.ReadOnly}（期望 true）"
                    + $"；匿名+allowCommands=true → readOnly={anonymous.ReadOnly}（期望 true）");

                // 核销时按当前策略再压一次：签票时可写，握手前策略改成"禁止写"
                var downgraded = tickets.Issue("op", readOnly: false, allowCommands: true);
                var redeemOk = tickets.TryRedeem(downgraded.Ticket, null, allowCommands: false, out var grant, out var reason, out _);

                Check("S15",
                    redeemOk && grant != null && grant.ReadOnly
                    && grant.Session != null && grant.Session.ReadOnly,
                    $"可写票在 allowCommands=false 下核销：ok={redeemOk}（{reason}）"
                    + $" grant.readOnly={grant?.ReadOnly} session.readOnly={grant?.Session?.ReadOnly}");
            }

            // ---------------- S16：ticket 仍是 30 秒 + 单次 ----------------
            {
                var tickets = new ScadaWebTicketService();
                var ticket = tickets.Issue("op", readOnly: false);

                var firstRedeem = tickets.TryRedeem(ticket.Ticket, null, out _, out _);
                var secondRedeem = tickets.TryRedeem(ticket.Ticket, null, out _, out var secondReason);
                var liveAfter = tickets.LiveCount;
                var handoffAfter = tickets.HandoffCount;

                Check("S16",
                    firstRedeem && !secondRedeem
                    && liveAfter == 0 && handoffAfter == 1
                    && ScadaWebTicketService.Lifetime == TimeSpan.FromSeconds(30),
                    $"首次核销={firstRedeem} 复用={secondRedeem}（{secondReason}）"
                    + $" LiveCount={liveAfter} HandoffCount={handoffAfter}"
                    + $" Lifetime={ScadaWebTicketService.Lifetime.TotalSeconds:F0}s");
            }

            // ---------------- S17：Reset 清空 ----------------
            {
                var tickets = new ScadaWebTicketService();
                var gate = new ScadaWebSecurityGate(tickets);

                var session = RedeemFor(tickets, "alice");
                gate.RevokeSession(session.Token);

                for (var i = 0; i < 3; i++) gate.TryCheckTicketRateLimit("10.0.0.1", out _);

                var trackedBefore = gate.TrackedClients;
                gate.Reset();

                // 撤销记录被清掉之后，那枚令牌"已经不在表里"，因此表现为"过期"而不是"被撤销"
                var resolveAfterReset = tickets.TryResolveSession(session.Token, null, out _, out var errorAfterReset);

                Check("S17",
                    trackedBefore == 1 && gate.TrackedClients == 0
                    && !resolveAfterReset && errorAfterReset == ScadaWebTicketService.ErrorSessionExpired,
                    $"Reset 前跟踪来源={trackedBefore} 后={gate.TrackedClients}；"
                    + $"撤销记录清空后同一令牌续期={resolveAfterReset}（{errorAfterReset}）");

                gate.Reset();   // 幂等：再清一次不抛
            }

            // ---------------- S18：改密后重新登录 → 账号级撤销解除 ----------------
            {
                var tickets = new ScadaWebTicketService();
                var gate = new ScadaWebSecurityGate(tickets);

                var before = RedeemFor(tickets, "alice");
                gate.RevokeUser("alice");

                var oldResolve = tickets.TryResolveSession(before.Token, null, out _, out var oldError);

                // 重新登录 = 宿主重新 Issue（那一刻身份刚被账号校验证明过）
                var afterLogin = RedeemFor(tickets, "alice");
                var newResolve = tickets.TryResolveSession(afterLogin.Token, null, out _, out var newError);

                Check("S18",
                    !oldResolve && oldError == ScadaWebTicketService.ErrorSessionRevoked
                    && newResolve && newError == null,
                    $"撤销后旧令牌={oldResolve}（{oldError}）；"
                    + $"重新登录的新令牌续期={newResolve}（{newError ?? "<null>"}）"
                    + "（账号级撤销只挡旧令牌，不该让这个人从此登不住）");
            }

            // S19：用户名带尾空格也必须能被账号级撤销命中。
            // Host 入口已 Trim；票层再 Trim 一次，保证任何调用方（探针/网关）都得到同一身份。
            {
                var tickets = new ScadaWebTicketService();
                var session = RedeemFor(tickets, "alice ");
                tickets.RevokeUser("alice");

                var stillValid = tickets.TryResolveSession(session.Token, null, out _, out var code);

                Check("S19", !stillValid && code == ScadaWebTicketService.ErrorSessionRevoked,
                    $"\"alice \" 的会话在 RevokeUser(\"alice\") 后仍有效={stillValid}（{code ?? "<null>"}）");
            }

            return _failed;
        }

        /// <summary>签一张票并核销，返回它的会话令牌（断言夹具）</summary>
        private static ScadaWebSessionGrant RedeemFor(ScadaWebTicketService tickets, string userName)
        {
            var ticket = tickets.Issue(userName, readOnly: false);

            if (!tickets.TryRedeem(ticket.Ticket, remoteIp: null, out var grant, out var reason) || grant?.Session == null)
                throw new InvalidOperationException($"断言夹具：ticket 核销失败（{reason}）");

            return grant.Session;
        }

        private static string Short(string? token)
            => string.IsNullOrEmpty(token) ? "<null>" : token!.Substring(0, Math.Min(6, token.Length)) + "…";

        /// <summary>假账号校验器：构造参数里的用户名"仍在用出厂默认密码"</summary>
        private sealed class FakeAuthenticator : IScadaUserAuthenticator
        {
            private readonly HashSet<string> _defaultPasswordUsers;

            public FakeAuthenticator(params string[] defaultPasswordUsers)
                => _defaultPasswordUsers = new HashSet<string>(defaultPasswordUsers, StringComparer.Ordinal);

            public bool TryValidate(string? userName, string? password, out ScadaUserInfo? user, out string? error)
            {
                user = new ScadaUserInfo(userName ?? string.Empty, "G-op",
                    new[] { ScadaPermissionKeys.ViewRuntime });
                error = null;
                return !string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(password);
            }

            public bool IsUsingDefaultPassword(string? userName)
                => userName != null && _defaultPasswordUsers.Contains(userName);
        }

        /// <summary>校验器自己抛异常（验证"查不出来 = 拒绝"的 fail closed 口径）</summary>
        private sealed class ThrowingAuthenticator : IScadaUserAuthenticator
        {
            public bool TryValidate(string? userName, string? password, out ScadaUserInfo? user, out string? error)
            {
                user = new ScadaUserInfo(userName ?? string.Empty, "G-op",
                    new[] { ScadaPermissionKeys.ViewRuntime });
                error = null;
                return true;
            }

            public bool IsUsingDefaultPassword(string? userName)
                => throw new InvalidOperationException("模拟账号库不可读");
        }
    }
}

// ---------------------------------------------------------------------------
// 接线（Program.cs 里加一行，放在 `return Finish();` 之前即可）：
//
//     _failed += M2SecurityChecks.Run();
//
// 断言纯内存、毫秒级、不占端口，因此不需要起服务，也不依赖上面那套 HTTP/WS 流程。
// ---------------------------------------------------------------------------
