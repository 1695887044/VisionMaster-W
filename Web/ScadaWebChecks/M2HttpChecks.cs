using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.ScadaWeb;

namespace ScadaWebChecks
{
    /// <summary>
    /// M2 前四项安全前置的 <b>HTTP / WS 端到端</b>断言（矩阵 C68 / C69 / C70 / C72）。
    ///
    /// <b>为什么与 <see cref="M2SecurityChecks"/> 并存</b>
    /// ---------
    /// M2SecurityChecks 直接调闸门/票表的纯函数，证明"判据本身对不对"；
    /// 本文件起<b>真实</b> <see cref="ScadaWebHost"/>，用 HTTP 与 <see cref="ClientWebSocket"/> 打它，
    /// 证明"判据真的接在 HTTP/WS 路径上、状态码与响应字段真的是矩阵要求的那一个"。
    /// 两层的失败原因不同：前者是判据错，后者是接线错（编译期全绿、只有真打一次才看得见）。
    ///
    /// <b>四个用例各自独立</b>
    /// ---------
    /// 每个用例一个独立宿主 + 独立 <see cref="ScadaWebSettings"/> + 随机端口
    /// （<c>TcpListener(Loopback, 0)</c>），<c>finally</c> 里 <see cref="ScadaWebHost.StopAsync"/>。
    /// 原因：<see cref="ScadaWebSecurityGate"/> 的限速窗口与 <see cref="ScadaWebTicketService"/>
    /// 的会话表都是宿主级实例，共用一个宿主会让前一个用例的计数污染后一个用例的判据。
    ///
    /// <b>两条口径必须写清（避免日后被当成缺陷回炉）</b>
    /// ---------
    ///   1. <b>默认密码闸在 PBKDF2 之后</b>（产品注释与方案 §8.2 的刻意顺序）：先校验凭据、再判"是否仍是
    ///      出厂密码"。所以 C68 里"凭据校验次数 = 1"是<b>正确</b>的，本文件钉的是"拒绝路径不签票、不建会话、
    ///      闸门每轮现读"，而不是"不跑 PBKDF2"（那句与产品顺序冲突，见 M2a 测试矩阵 C68 行的措辞）。
    ///   2. <b>会话令牌 8 小时有效，关闭 WS 不会回收它</b>（只有 <see cref="ScadaWebHost.RevokeSession"/>
    ///      或过期才会空出名额）。C72 的"空出一个"因此先试"关连接"（产品若改成随连接回收也照样通过），
    ///      仍满时再显式撤销一枚令牌——两条路都记录在明细里，不隐藏实际发生的是哪一条。
    /// </summary>
    internal static class M2HttpChecks
    {
        private static int _failed;
        private static int _total;

        /// <summary>本段通过的条数（Program 用它并入总 PASS 数）</summary>
        public static int Pass => _total - _failed;

        /// <summary>本段执行的断言总条数（4）</summary>
        public static int Total => _total;

        /// <summary>跑完全部四条 HTTP 端到端断言，返回<b>失败条数</b>（0 = 全绿）</summary>
        public static async Task<int> RunAsync()
        {
            _failed = 0;
            _total = 0;

            Console.WriteLine();
            Console.WriteLine("--- M2 HTTP 端到端（C68 默认密码 / C69 取票限速 / C70 匿名不续期 / C72 会话表上限） ---");

            await CheckC68Async().ConfigureAwait(false);
            await CheckC69Async().ConfigureAwait(false);
            await CheckC70Async().ConfigureAwait(false);
            await CheckC72Async().ConfigureAwait(false);

            return _failed;
        }

        // ============================================================
        //  C68：出厂默认密码闸门阻断写路径
        // ============================================================

        /// <summary>
        /// C68：<c>admin</c> 仍是出厂密码时，<c>POST /api/ticket {user,password}</c> 必须
        /// <b>403 <c>E_DEFAULT_PASSWORD</c></b> + <c>gate:"DefaultPasswordNotChanged"</c>，
        /// <b>不签发 ticket、不建会话</b>；模拟桌面端改密后同一宿主立刻放行（闸门不许缓存结果），
        /// 且新票能真的完成 WS 握手。
        /// </summary>
        private static async Task CheckC68Async()
        {
            var accounts = new FakeAccounts()
                .Add("admin", "admin123", defaultPassword: true)
                .Add("op", "op123", defaultPassword: false);

            var fx = HostFixture.Start(
                settings =>
                {
                    settings.AllowAnonymousView = false;   // 强制走登录分支
                    settings.TicketRatePerMinute = 10;     // 本用例只打 2 次，不触碰限速
                },
                accounts);

            ClientWebSocket? socket = null;

            try
            {
                var listening = fx.Host.IsListening;
                var liveBefore = fx.Host.Tickets.LiveCount;

                // ① 出厂密码：凭据本身是对的，拒绝必须来自默认密码闸（403 + gate），不是 401
                var blocked = await PostTicketAsync(fx.Http, fx.BaseUrl, Login("admin", "admin123"));

                var liveAfterBlocked = fx.Host.Tickets.LiveCount;
                var sessionsAfterBlocked = fx.Host.Tickets.SessionCount;
                var validationsAfterBlocked = accounts.ValidateCount;
                var defaultChecksAfterBlocked = accounts.DefaultPasswordChecks;

                var blockedOk = blocked.Status == 403
                                && string.Equals(blocked.Code, ScadaWebSecurityGate.ErrorDefaultPassword, StringComparison.Ordinal)
                                && string.Equals(blocked.Gate, "DefaultPasswordNotChanged", StringComparison.Ordinal)
                                && string.IsNullOrEmpty(blocked.Ticket)
                                && blocked.Body.IndexOf("admin123", StringComparison.Ordinal) < 0   // 响应里不许回显密码
                                && liveAfterBlocked == liveBefore                                  // 不签发 ticket
                                && sessionsAfterBlocked == 0                                       // 不建会话
                                && validationsAfterBlocked == 1                                    // 只校验一次凭据（闸门不复算）
                                && defaultChecksAfterBlocked == 1;                                 // 闸门确实被问过（不是静默跳过）

                // ② 桌面端改密：同一个宿主、同一个校验器实例改标记 → 闸门必须每轮现读
                accounts.SetDefaultPassword("admin", false);

                var allowed = await PostTicketAsync(fx.Http, fx.BaseUrl, Login("admin", "admin123"));
                var validationsAfterAllowed = accounts.ValidateCount;

                var allowedOk = allowed.Status == 200
                                && !string.IsNullOrEmpty(allowed.Ticket)
                                && allowed.ReadOnly == true          // AllowCommands 默认 false = 只读镜像
                                && validationsAfterAllowed == 2;

                // ③ 改密后的票必须真的能用（端到端：取票 → WS 握手 → hello.sessionToken）
                string? sessionToken = null;
                var helloDetail = "未取到 ticket，未握手";

                if (!string.IsNullOrEmpty(allowed.Ticket))
                {
                    var (connected, connectError, ws) = await ConnectAsync(fx.WsUrl(allowed.Ticket!));
                    socket = ws;

                    if (connected && ws != null)
                    {
                        var hello = await ReceiveUntilAsync(ws, m => TypeOf(m) == "hello", 3000);
                        sessionToken = hello?.Value<string>("sessionToken");
                        helloDetail = hello == null
                            ? "连上了但 3s 内没收到 hello"
                            : $"hello.user={hello.Value<string>("user") ?? "<无>"} sessionToken={Short(sessionToken)}";
                    }
                    else
                    {
                        helloDetail = $"WS 握手失败：{connectError ?? "<无>"}";
                    }
                }

                var helloOk = !string.IsNullOrWhiteSpace(sessionToken);

                var ok = listening && blockedOk && allowedOk && helloOk;

                Check("C68", ok,
                    $"listening={listening}；"
                    + $"①出厂密码: status={blocked.Status} code={blocked.Code ?? "<无>"} gate={blocked.Gate ?? "<无>"}"
                    + $" ticket={(string.IsNullOrEmpty(blocked.Ticket) ? "<无>" : Short(blocked.Ticket))}"
                    + $" LiveCount {liveBefore}→{liveAfterBlocked} SessionCount={sessionsAfterBlocked}"
                    + $" 凭据校验={validationsAfterBlocked} 默认密码查询={defaultChecksAfterBlocked}（{Mark(blockedOk)}）"
                    + $"；②改密后: status={allowed.Status} ticket={Short(allowed.Ticket)}"
                    + $" readOnly={Show(allowed.ReadOnly)} 凭据校验={validationsAfterAllowed}（{Mark(allowedOk)}）"
                    + $"；③握手: {helloDetail}（{Mark(helloOk)}）"
                    + $"；响应体={Truncate(blocked.Body, 180)}"
                    + "；注：默认密码闸在产品里排在 PBKDF2 之后（刻意顺序），故凭据校验 1 次属正常，"
                    + "本条钉的是拒绝路径不签票/不建会话");

                if (!ok) DumpWarnings("C68", fx);
            }
            finally
            {
                if (socket != null) await CloseAsync(socket).ConfigureAwait(false);
                await fx.StopAsync().ConfigureAwait(false);
            }
        }

        // ============================================================
        //  C69：/api/ticket 每 IP 限速（TicketRatePerMinute=2）
        // ============================================================

        /// <summary>
        /// C69：<c>TicketRatePerMinute=2</c> 时同一来源 IP 前 2 次放行、第 3 次
        /// <b>429 <c>E_RATE_LIMITED</c> + <c>retryAfterMs</c></b>；第 3 次<b>换用户名</b>并伪造
        /// <c>X-Forwarded-For</c> 仍被拒（Host 读 socket IP，按 IP 合并计数），且拒绝路径
        /// <b>不跑凭据校验、不签票</b>。
        /// </summary>
        private static async Task CheckC69Async()
        {
            var accounts = new FakeAccounts()
                .Add("op", "op123", defaultPassword: false)
                .Add("admin", "admin123", defaultPassword: false);

            var fx = HostFixture.Start(
                settings =>
                {
                    settings.AllowAnonymousView = false;
                    settings.TicketRatePerMinute = 2;      // 本用例的判据全靠这个额度
                },
                accounts);

            try
            {
                var listening = fx.Host.IsListening;

                // ① 前 2 次：两个不同用户名，都放行（额度按 IP 算，不按用户算）
                var first = await PostTicketAsync(fx.Http, fx.BaseUrl, Login("op", "op123"));
                var second = await PostTicketAsync(fx.Http, fx.BaseUrl, Login("admin", "admin123"));

                var liveBeforeThird = fx.Host.Tickets.LiveCount;
                var validationsBeforeThird = accounts.ValidateCount;

                // ② 第 3 次：换用户名 + 伪造 X-Forwarded-For —— 两条都必须绕不过去
                var third = await PostTicketAsync(
                    fx.Http,
                    fx.BaseUrl,
                    Login("ghost", "whatever"),
                    ("X-Forwarded-For", "10.9.9.9"));

                var liveAfterThird = fx.Host.Tickets.LiveCount;
                var validationsAfterThird = accounts.ValidateCount;
                var trackedClients = fx.Host.SecurityGate.TrackedClients;

                var firstOk = first.Status == 200 && !string.IsNullOrEmpty(first.Ticket);
                var secondOk = second.Status == 200 && !string.IsNullOrEmpty(second.Ticket);

                var thirdOk = third.Status == 429
                              && string.Equals(third.Code, ScadaWebSecurityGate.ErrorRateLimited, StringComparison.Ordinal)
                              && third.RetryAfterMs == (int)ScadaWebSecurityGate.TicketRateWindow.TotalMilliseconds
                              && string.IsNullOrEmpty(third.Ticket);

                var noPkdf2 = validationsAfterThird == validationsBeforeThird;   // 限速在凭据校验之前
                var noTicket = liveAfterThird == liveBeforeThird;                // 拒绝路径不签票
                var oneKey = trackedClients == 1;                                // 只跟踪了一个来源（按 IP 合并）

                // ③ 第二来源地址：Windows 的 127.0.0.2 也是回环地址，能绑就能直接反证"另一个 IP 不受影响"。
                //    绑不上（或本机路由不允许）时如实记录——这一条不进判据，因为"按 IP 分开计数"已由
                //    换用户名 + XFF 仍 429、且只跟踪 1 个来源反证（矩阵允许这条退路）。
                var secondIp = await TrySecondSourceAsync(fx);
                var secondIpOk = !secondIp.Available || secondIp.Status == 200;

                var ok = listening && firstOk && secondOk && thirdOk && noPkdf2 && noTicket && oneKey && secondIpOk;

                Check("C69", ok,
                    $"listening={listening}；额度={fx.Settings.TicketRatePerMinute}/分钟；"
                    + $"①前 2 次: op→{first.Status}/{Short(first.Ticket)} admin→{second.Status}/{Short(second.Ticket)}"
                    + $"（{Mark(firstOk && secondOk)}）"
                    + $"；②第 3 次（用户名 ghost + X-Forwarded-For: 10.9.9.9）: status={third.Status}"
                    + $" code={third.Code ?? "<无>"} retryAfterMs={Show(third.RetryAfterMs)}"
                    + $" ticket={(string.IsNullOrEmpty(third.Ticket) ? "<无>" : Short(third.Ticket))}（{Mark(thirdOk)}）"
                    + $"；③拒绝路径: 凭据校验 {validationsBeforeThird}→{validationsAfterThird}（不跑 PBKDF2，{Mark(noPkdf2)}）"
                    + $" LiveCount {liveBeforeThird}→{liveAfterThird}（不签票，{Mark(noTicket)}）"
                    + $"；④限速键: TrackedClients={trackedClients}（按 IP 合并，{Mark(oneKey)}）"
                    + $"；⑤第二来源: {secondIp.Detail}（{Mark(secondIpOk)}）"
                    + $"；响应体={Truncate(third.Body, 180)}");

                if (!ok) DumpWarnings("C69", fx);
            }
            finally
            {
                await fx.StopAsync().ConfigureAwait(false);
            }
        }

        // ============================================================
        //  C70：匿名令牌在 AllowAnonymousView=false 后不续期
        // ============================================================

        /// <summary>
        /// C70：<c>AllowAnonymousView=true</c> 时取匿名 ticket → WS 握手拿 <c>sessionToken</c>；
        /// 把<b>同一份</b> <see cref="ScadaWebSettings"/> 的 <c>AllowAnonymousView</c> 改成 false 之后：
        /// ① 已建立的连接不主动踢（ping 仍回 pong）；② <c>POST /api/ticket {refreshToken}</c> 必须
        /// <b>401 <c>E_SESSION_EXPIRED</c></b> 且不签新票；③ 再取匿名票也被拒（重连必须登录）。
        /// </summary>
        private static async Task CheckC70Async()
        {
            var fx = HostFixture.Start(
                settings =>
                {
                    settings.AllowAnonymousView = true;
                    settings.TicketRatePerMinute = 10;
                },
                authenticator: null);   // 匿名只读观众：登录分支不可用，本用例也不走它

            ClientWebSocket? socket = null;

            try
            {
                var listening = fx.Host.IsListening;

                // ① 匿名取票（空体）→ 一次性 ticket
                var anonymous = await PostTicketAsync(fx.Http, fx.BaseUrl, body: null);
                var anonymousOk = anonymous.Status == 200
                                  && !string.IsNullOrEmpty(anonymous.Ticket)
                                  && anonymous.ReadOnly == true;

                // ② 握手拿 hello.sessionToken（匿名会话令牌）
                string? sessionToken = null;
                var helloDetail = "未取到 ticket，未握手";

                if (!string.IsNullOrEmpty(anonymous.Ticket))
                {
                    var (connected, connectError, ws) = await ConnectAsync(fx.WsUrl(anonymous.Ticket!));
                    socket = ws;

                    if (connected && ws != null)
                    {
                        var hello = await ReceiveUntilAsync(ws, m => TypeOf(m) == "hello", 3000);
                        sessionToken = hello?.Value<string>("sessionToken");
                        helloDetail = hello == null
                            ? "连上了但 3s 内没收到 hello"
                            : $"readOnly={Show(hello.Value<bool?>("readOnly"))} sessionToken={Short(sessionToken)}";
                    }
                    else
                    {
                        helloDetail = $"WS 握手失败：{connectError ?? "<无>"}";
                    }
                }

                var helloOk = !string.IsNullOrWhiteSpace(sessionToken);

                // ③ 运行期关掉匿名访问（同一份 settings 对象；Host 每轮现读）
                fx.Settings.AllowAnonymousView = false;

                // ③a 已有连接不主动踢：ping 仍应回 pong
                var pongOk = false;

                if (socket != null && socket.State == WebSocketState.Open)
                {
                    await SendTextAsync(socket, "{\"type\":\"ping\",\"t\":\"c70\"}");
                    var pong = await ReceiveUntilAsync(socket, m => TypeOf(m) == "pong", 3000);
                    pongOk = pong?.Value<string>("t") == "c70";
                }

                // ③b 匿名 refreshToken 换票：401 E_SESSION_EXPIRED，不签新票
                var liveBeforeRefresh = fx.Host.Tickets.LiveCount;
                TicketReply? refresh = null;

                if (!string.IsNullOrWhiteSpace(sessionToken))
                {
                    refresh = await PostTicketAsync(
                        fx.Http,
                        fx.BaseUrl,
                        new JObject { ["refreshToken"] = sessionToken });
                }

                var liveAfterRefresh = fx.Host.Tickets.LiveCount;

                var refreshOk = refresh != null
                                && refresh.Status == 401
                                && string.Equals(refresh.Code, ScadaWebTicketService.ErrorSessionExpired, StringComparison.Ordinal)
                                && string.IsNullOrEmpty(refresh.Ticket)
                                && liveAfterRefresh == liveBeforeRefresh;

                // ③c 断开后重连必须登录：匿名取票被同一开关拒绝
                var anonymousAgain = await PostTicketAsync(fx.Http, fx.BaseUrl, body: null);
                var anonymousAgainOk = anonymousAgain.Status == 401
                                       && string.Equals(anonymousAgain.Code, "E_AUTH_REQUIRED", StringComparison.Ordinal)
                                       && string.IsNullOrEmpty(anonymousAgain.Ticket);

                var ok = listening && anonymousOk && helloOk && pongOk && refreshOk && anonymousAgainOk;

                Check("C70", ok,
                    $"listening={listening}；"
                    + $"①匿名取票: status={anonymous.Status} ticket={Short(anonymous.Ticket)}"
                    + $" readOnly={Show(anonymous.ReadOnly)}（{Mark(anonymousOk)}）"
                    + $"；②握手: {helloDetail}（{Mark(helloOk)}）"
                    + $"；③关掉匿名后: 已连连接 ping→pong={pongOk}（{Mark(pongOk)}）"
                    + $" 续期: status={Show(refresh?.Status)} code={refresh?.Code ?? "<无>"}"
                    + $" ticket={(string.IsNullOrEmpty(refresh?.Ticket) ? "<无>" : Short(refresh!.Ticket))}"
                    + $" LiveCount {liveBeforeRefresh}→{liveAfterRefresh}（{Mark(refreshOk)}）"
                    + $" 再取匿名票: status={anonymousAgain.Status} code={anonymousAgain.Code ?? "<无>"}（{Mark(anonymousAgainOk)}）"
                    + $"；续期响应体={Truncate(refresh?.Body, 160)}");

                if (!ok) DumpWarnings("C70", fx);
            }
            finally
            {
                if (socket != null) await CloseAsync(socket).ConfigureAwait(false);
                await fx.StopAsync().ConfigureAwait(false);
            }
        }

        // ============================================================
        //  C72：会话表上限（MaxSessionTokens=2）
        // ============================================================

        /// <summary>
        /// C72：<c>MaxSessionTokens=2</c>，两个已核销会话（各自真实 WS 握手并收到 hello）之后，
        /// 第三次 <c>POST /api/ticket</c> 必须 <b>503 <c>E_CAPACITY</c></b> 且不签票、
        /// <b>不挤掉在线会话</b>（原连接仍能 ping/pong）、<c>SessionCount</c> 不超上限；
        /// 空出一个名额后恢复取票，且恢复出来的票能再次完成握手。
        ///
        /// <b>"空出一个"的两条路都试</b>：先关 WS（产品若改成随连接回收会话令牌，这一步就会成功），
        /// 仍满时再 <see cref="ScadaWebHost.RevokeSession"/> 显式撤销——实际走哪条写进明细。
        /// </summary>
        private static async Task CheckC72Async()
        {
            var accounts = new FakeAccounts().Add("op", "op123", defaultPassword: false);

            var fx = HostFixture.Start(
                settings =>
                {
                    settings.AllowAnonymousView = false;
                    settings.TicketRatePerMinute = 10;   // 本用例最多 5 次取票
                },
                accounts,
                host => host.Tickets.MaxSessionTokens = 2);

            var sockets = new List<ClientWebSocket>();

            try
            {
                var listening = fx.Host.IsListening;
                var max = fx.Host.Tickets.MaxSessionTokens;

                // ① 第一个已核销会话
                var t1 = await PostTicketAsync(fx.Http, fx.BaseUrl, Login("op", "op123"));
                var (ws1, token1, d1) = await OpenSessionAsync(fx, t1.Ticket);
                if (ws1 != null) sockets.Add(ws1);

                var count1 = fx.Host.Tickets.SessionCount;

                // ② 第二个已核销会话
                var t2 = await PostTicketAsync(fx.Http, fx.BaseUrl, Login("op", "op123"));
                var (ws2, token2, d2) = await OpenSessionAsync(fx, t2.Ticket);
                if (ws2 != null) sockets.Add(ws2);

                var count2 = fx.Host.Tickets.SessionCount;

                // ③ 第三张：容量预检必须 503 E_CAPACITY（握手阶段只能 401，所以在取票处先挡）
                var t3 = await PostTicketAsync(fx.Http, fx.BaseUrl, Login("op", "op123"));
                var countAfterThird = fx.Host.Tickets.SessionCount;

                var capacityOk = t3.Status == 503
                                 && string.Equals(t3.Code, ScadaWebTicketService.ErrorCapacity, StringComparison.Ordinal)
                                 && string.IsNullOrEmpty(t3.Ticket)
                                 && countAfterThird <= max;

                // ③a 不挤掉在线会话：第二个连接仍能 ping/pong
                var aliveOk = false;

                if (ws2 != null && ws2.State == WebSocketState.Open)
                {
                    await SendTextAsync(ws2, "{\"type\":\"ping\",\"t\":\"c72\"}");
                    var pong = await ReceiveUntilAsync(ws2, m => TypeOf(m) == "pong", 3000);
                    aliveOk = pong?.Value<string>("t") == "c72";
                }

                // ④ 空出一个名额：先试"关连接"（等 300ms 让服务端收尾），仍满再显式撤销令牌
                TicketReply? afterClose = null;

                if (ws1 != null)
                {
                    await CloseAsync(ws1).ConfigureAwait(false);
                    sockets.Remove(ws1);
                    await Task.Delay(300).ConfigureAwait(false);

                    afterClose = await PostTicketAsync(fx.Http, fx.BaseUrl, Login("op", "op123"));
                }

                var afterCloseStatus = afterClose?.Status ?? -1;
                var freedByClose = afterClose != null
                                   && afterClose.Status == 200
                                   && !string.IsNullOrEmpty(afterClose.Ticket);

                TicketReply? recovered;
                string recoveredBy;

                if (freedByClose)
                {
                    // 关连接就把名额还回来了（产品若改成"随连接回收会话令牌"就是这条）：
                    // 直接复用那次响应，不再多打一次（多打会额外签一张票，让明细难读）。
                    recovered = afterClose;
                    recoveredBy = "关闭连接后即恢复";
                }
                else
                {
                    recoveredBy = "撤销会话令牌后恢复";
                    if (!string.IsNullOrWhiteSpace(token1)) fx.Host.RevokeSession(token1);
                    await Task.Delay(200).ConfigureAwait(false);

                    recovered = await PostTicketAsync(fx.Http, fx.BaseUrl, Login("op", "op123"));
                }

                var recoveredOk = recovered != null
                                  && recovered.Status == 200
                                  && !string.IsNullOrEmpty(recovered.Ticket);

                // ⑤ 恢复出来的票必须真的能用（再建一个会话）
                var (ws3, token3, d3) = await OpenSessionAsync(fx, recovered?.Ticket);
                if (ws3 != null) sockets.Add(ws3);

                var countFinal = fx.Host.Tickets.SessionCount;

                var ok = listening
                         && max == 2
                         && !string.IsNullOrEmpty(t1.Ticket) && ws1 != null && !string.IsNullOrWhiteSpace(token1)
                         && !string.IsNullOrEmpty(t2.Ticket) && ws2 != null && !string.IsNullOrWhiteSpace(token2)
                         && count1 == 1 && count2 == 2
                         && capacityOk
                         && aliveOk
                         && recoveredOk
                         && ws3 != null && !string.IsNullOrWhiteSpace(token3)
                         && countFinal <= max;

                Check("C72", ok,
                    $"listening={listening} MaxSessionTokens={max}；"
                    + $"①会话1: ticket={Short(t1.Ticket)} {d1} SessionCount={count1}"
                    + $"；②会话2: ticket={Short(t2.Ticket)} {d2} SessionCount={count2}"
                    + $"；③第三张: status={t3.Status} code={t3.Code ?? "<无>"}"
                    + $" ticket={(string.IsNullOrEmpty(t3.Ticket) ? "<无>" : Short(t3.Ticket))}"
                    + $" SessionCount={countAfterThird}（≤{max}，{Mark(capacityOk)}）"
                    + $" 在线会话未被挤掉（ws2 ping→pong={aliveOk}，{Mark(aliveOk)}）"
                    + $"；④空出一个: 仅关连接→status={Show(afterCloseStatus)}，"
                    + $"实际方式={recoveredBy}→status={Show(recovered?.Status)}"
                    + $" ticket={Short(recovered?.Ticket)}（{Mark(recoveredOk)}）"
                    + $"；⑤恢复后握手: {d3} SessionCount={countFinal}（≤{max}）"
                    + $"；第三张响应体={Truncate(t3.Body, 160)}");

                if (!ok) DumpWarnings("C72", fx);
            }
            finally
            {
                foreach (var socket in sockets)
                    await CloseAsync(socket).ConfigureAwait(false);

                await fx.StopAsync().ConfigureAwait(false);
            }
        }

        // ============================================================
        //  夹具：一个真实宿主 + 随机端口 + 收尾
        // ============================================================

        /// <summary>
        /// 一个已启动的真实宿主。端口来自 <see cref="GetFreePort"/>（<c>TcpListener(Loopback, 0)</c>），
        /// 不占 8080/8899，可与正在运行的桌面端并存。
        /// </summary>
        private sealed class HostFixture
        {
            private HostFixture()
            {
            }

            public ScadaWebHost Host { get; private set; } = null!;

            public ScadaWebSettings Settings { get; } = new();

            public FakeLog Log { get; } = new();

            public HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(10) };

            public int Port { get; private set; }

            public string BaseUrl => $"http://127.0.0.1:{Port}";

            public string WsUrl(string ticket) => $"ws://127.0.0.1:{Port}/ws?ticket={ticket}";

            /// <param name="configureSettings">在构造宿主<b>之前</b>改配置（限速额度等只在构造时读一次）</param>
            /// <param name="authenticator">账号校验出口；<c>null</c> = 只做匿名观众</param>
            /// <param name="configureHost">构造之后、<c>Start()</c> 之前改宿主（如 <c>Tickets.MaxSessionTokens</c>）</param>
            public static HostFixture Start(
                Action<ScadaWebSettings> configureSettings,
                IScadaUserAuthenticator? authenticator,
                Action<ScadaWebHost>? configureHost = null)
            {
                var fixture = new HostFixture();
                var port = GetFreePort();

                fixture.Port = port;
                fixture.Settings.Enabled = true;
                fixture.Settings.Port = port;
                fixture.Settings.BindAddress = "127.0.0.1";
                fixture.Settings.MaxSessions = 16;
                fixture.Settings.AllowAnonymousView = false;
                fixture.Settings.AllowCommands = false;

                configureSettings(fixture.Settings);

                // hub/values 传 null：这四条用例只判"取票 / 握手 / 会话表"，不需要快照与值帧。
                // 未注册 hub 时 WS 握手后发 idle，hello 仍照常下发（会话令牌就在 hello 里）。
                fixture.Host = new ScadaWebHost(
                    hub: null,
                    authenticator: authenticator,
                    catalog: null,
                    log: fixture.Log,
                    settings: fixture.Settings);

                configureHost?.Invoke(fixture.Host);
                fixture.Host.Start();

                return fixture;
            }

            public async Task StopAsync()
            {
                try { await Host.StopAsync().ConfigureAwait(false); }
                catch { /* 收尾失败没有补救动作，不影响断言 */ }

                try { Http.Dispose(); } catch { /* 同上 */ }
            }
        }

        /// <summary>
        /// 假账号库：<c>admin/admin123</c>（可切换"仍是出厂密码"）、<c>op/op123</c>（已改密）。
        /// 两个计数用于反证"拒绝路径不做凭据校验 / 闸门不复算"。
        /// </summary>
        private sealed class FakeAccounts : IScadaUserAuthenticator
        {
            private readonly Dictionary<string, string> _passwords = new(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _defaultPasswordUsers = new(StringComparer.OrdinalIgnoreCase);

            private int _validateCount;
            private int _defaultChecks;

            public int ValidateCount => Volatile.Read(ref _validateCount);

            public int DefaultPasswordChecks => Volatile.Read(ref _defaultChecks);

            public FakeAccounts Add(string user, string password, bool defaultPassword)
            {
                _passwords[user] = password;
                SetDefaultPassword(user, defaultPassword);
                return this;
            }

            /// <summary>模拟"桌面端改密"：同一实例换标记（证明闸门每轮现读，没有缓存）</summary>
            public void SetDefaultPassword(string user, bool isDefault)
            {
                if (isDefault) _defaultPasswordUsers.Add(user);
                else _defaultPasswordUsers.Remove(user);
            }

            public bool TryValidate(string? userName, string? password, out ScadaUserInfo? user, out string? error)
            {
                Interlocked.Increment(ref _validateCount);

                user = null;
                error = null;

                var name = userName?.Trim() ?? string.Empty;

                if (!_passwords.TryGetValue(name, out var expected)
                    || !string.Equals(expected, password, StringComparison.Ordinal))
                {
                    error = "用户名或密码不正确";
                    return false;
                }

                var isAdmin = string.Equals(name, "admin", StringComparison.OrdinalIgnoreCase);
                user = new ScadaUserInfo(name,
                    isAdmin ? ScadaGroupDefaults.AdminTemplateId : "G-op",
                    isAdmin
                        ? ScadaPermissionKeys.BuiltInKeys.ToArray()
                        : new[] { ScadaPermissionKeys.ViewRuntime, ScadaPermissionKeys.ControlRun });

                return true;
            }

            public bool IsUsingDefaultPassword(string? userName)
            {
                Interlocked.Increment(ref _defaultChecks);

                var name = userName?.Trim() ?? string.Empty;
                return _defaultPasswordUsers.Contains(name);
            }
        }

        private sealed class FakeLog : ILogService
        {
            public List<string> Lines { get; } = new();

            public void Success(params string[] messages) { }
            public void Error(params Exception[] messages) { }
            public void Error(params string[] messages) { }
            public void Info(params string[] messages) { }

            public void Warn(params string[] messages)
            {
                lock (Lines) Lines.AddRange(messages);
            }
        }

        /// <summary>一条 <c>POST /api/ticket</c> 响应（字段按产品协议取，缺失即 null）</summary>
        private sealed record TicketReply(
            int Status,
            string Body,
            JObject? Json,
            string? Code,
            string? Ticket,
            string? Gate,
            int? RetryAfterMs,
            bool? ReadOnly,
            string? SessionToken);

        // ============================================================
        //  HTTP / WS 辅助
        // ============================================================

        private static JObject Login(string user, string password)
            => new() { ["user"] = user, ["password"] = password };

        /// <summary>
        /// <c>POST /api/ticket</c>。<paramref name="body"/> 为 <c>null</c> = 空体形态（匿名观众）；
        /// 额外请求头用于反证 <c>X-Forwarded-For</c> 不能改限速键。
        /// </summary>
        private static async Task<TicketReply> PostTicketAsync(
            HttpClient http,
            string baseUrl,
            JObject? body,
            params (string Name, string Value)[] headers)
        {
            using var content = new StringContent(
                body == null ? "{}" : body.ToString(Formatting.None),
                Encoding.UTF8,
                "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/ticket")
            {
                Content = content
            };

            foreach (var (name, value) in headers)
                request.Headers.TryAddWithoutValidation(name, value);

            using var response = await http.SendAsync(request).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var json = TryJson(text);

            return new TicketReply(
                (int)response.StatusCode,
                text,
                json,
                json?.Value<string>("code"),
                json?.Value<string>("ticket"),
                json?.Value<string>("gate"),
                json?.Value<int?>("retryAfterMs"),
                json?.Value<bool?>("readOnly"),
                json?.Value<string>("sessionToken"));
        }

        /// <summary>取票 + 握手 + 收到 hello（C72 的"一个已核销会话"）</summary>
        private static async Task<(ClientWebSocket? Socket, string? SessionToken, string Detail)> OpenSessionAsync(
            HostFixture fixture,
            string? ticket)
        {
            if (string.IsNullOrEmpty(ticket))
                return (null, null, "无 ticket，未握手");

            var (connected, connectError, socket) = await ConnectAsync(fixture.WsUrl(ticket!));

            if (!connected || socket == null)
                return (null, null, $"握手失败：{connectError ?? "<无>"}");

            var hello = await ReceiveUntilAsync(socket, m => TypeOf(m) == "hello", 3000);

            if (hello == null)
                return (socket, null, "连上了但 3s 内没收到 hello");

            var token = hello.Value<string>("sessionToken");
            return (socket, token, $"hello.sessionToken={Short(token)}");
        }

        /// <summary>
        /// 用绑定到 <c>127.0.0.2</c> 的源地址再取一次票（模拟"另一台机器/另一个 IP"）。
        /// 绑不上就返回 <c>Available=false</c> + 原因（调用方据此决定是否把这一条计入判据）。
        /// </summary>
        private static async Task<(bool Available, int Status, string Detail)> TrySecondSourceAsync(HostFixture fixture)
        {
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, token) =>
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                    try
                    {
                        socket.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
                        await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            try
            {
                var reply = await PostTicketAsync(client, fixture.BaseUrl, Login("op", "op123"));

                return (true, reply.Status,
                    $"源 127.0.0.2 → status={reply.Status} code={reply.Code ?? "<无>"}"
                    + $" ticket={Short(reply.Ticket)}");
            }
            catch (Exception ex)
            {
                return (false, 0,
                    $"源 127.0.0.2 不可用（{ex.GetType().Name}：{FirstLine(ex.Message)}），"
                    + "本机无法模拟第二来源地址");
            }
        }

        private static async Task<(bool Connected, string? Error, ClientWebSocket? Socket)> ConnectAsync(string url)
        {
            var socket = new ClientWebSocket();

            try
            {
                await socket.ConnectAsync(new Uri(url), CancellationToken.None).ConfigureAwait(false);
                return (true, null, socket);
            }
            catch (Exception ex)
            {
                socket.Dispose();
                return (false, $"{ex.GetType().Name}: {FirstLine(ex.Message)}", null);
            }
        }

        private static async Task CloseAsync(ClientWebSocket socket)
        {
            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                // 对端已断开 / close 超时：收尾失败没有补救动作
            }
            finally
            {
                try { socket.Dispose(); } catch { /* 已释放 */ }
            }
        }

        private static async Task<JObject?> ReceiveUntilAsync(
            ClientWebSocket socket,
            Func<JObject, bool> match,
            int timeoutMs = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < deadline)
            {
                var remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
                var text = await ReceiveTextAsync(socket, remaining).ConfigureAwait(false);

                if (text == null) return null;

                var json = TryJson(text);
                if (json == null) continue;
                if (match(json)) return json;
            }

            return null;
        }

        private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var buffer = new byte[16384];
            using var stream = new MemoryStream();

            try
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return null;

                    stream.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage) break;
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (WebSocketException)
            {
                return null;
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static Task SendTextAsync(ClientWebSocket socket, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private static string? TypeOf(JObject message)
            => message.Value<string>("type") ?? message.Value<string>("kind");

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static JObject? TryJson(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return JObject.Parse(text); } catch { return null; }
        }

        // ============================================================
        //  输出
        // ============================================================

        private static void Check(string id, bool ok, string detail)
        {
            _total++;
            if (!ok) _failed++;

            Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + id + "  " + detail);
        }

        /// <summary>失败时把宿主告警打出来（"没收到 hello / 403 原因"只有这里能看到）</summary>
        private static void DumpWarnings(string id, HostFixture fixture)
        {
            List<string> lines;

            lock (fixture.Log.Lines) lines = fixture.Log.Lines.ToList();

            if (lines.Count == 0) return;

            Console.WriteLine($"--- {id} host warnings ({lines.Count}) ---");

            foreach (var line in lines.TakeLast(12))
                Console.WriteLine("  [WARN] " + line);
        }

        private static string Mark(bool ok) => ok ? "OK" : "NG";

        private static string Truncate(string? text, int max)
            => string.IsNullOrEmpty(text) ? "<null>" : (text.Length <= max ? text : text.Substring(0, max) + "…");

        private static string Short(string? value)
            => string.IsNullOrEmpty(value) ? "<null>" : value!.Substring(0, Math.Min(6, value.Length)) + "…";

        private static string Show(int? value)
            => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "<缺失>";

        private static string Show(bool? value)
            => value.HasValue ? (value.Value ? "true" : "false") : "<缺失>";

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var index = text.IndexOfAny(new[] { '\r', '\n' });
            return index < 0 ? text : text.Substring(0, index);
        }
    }
}
