using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
using Prism.Ioc;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.ScadaWeb;

namespace ScadaWebChecks
{
    /// <summary>
    /// B4：方案级 Web 会话（SW1–SW9，48 条）。
    ///
    /// <b>为什么单独一个文件、自带夹具</b>
    /// ---------
    /// <c>M2HttpChecks</c> 里的 <c>GetFreePort</c> / <c>FakeLog</c> / <c>HostFixture</c> / <c>FakeAccounts</c>
    /// 全是 <c>private</c>。把它们的可见性改成 <c>internal</c> 会动到八个既有断言段的可见性面，
    /// 而收益只是省一百来行夹具代码——不划算，所以这里**刻意复制一份**
    /// （这条偏差写进 D5）。复制的是形状，不是判据：本段判据与 M2 那几段完全不重叠。
    ///
    /// <b>这一段钉住的事</b>
    /// ---------
    /// 账号本从「机器级」改成「方案级」之后，Web 的票与 8 小时会话令牌必须<b>认方案</b>：
    ///   · 没有方案 → 取票 503，且**不跑 PBKDF2、不跑默认密码闸门**（SW1）；
    ///   · 票/会话快照是 <c>GroupId + Permissions</c>，**没有 Role**（SW2 / SW8）；
    ///   · 切方案 → 401 <c>E_SOLUTION_CHANGED</c>，且与「被撤销」「已过期」三者可分（SW3）；
    ///   · 切方案同时清 <c>_live</c> / <c>_handoff</c>（SW4）；
    ///   · 在线会话按组展示、不泄露 <c>SessionId</c>（SW5 / SW8）；
    ///   · 组撤销只影响该组（SW6 / SW7）；
    ///   · 模块装配缺方案上下文时 fail-closed，且权限闸的入参没有角色类型（SW9）。
    /// </summary>
    internal static class SolutionSessionChecks
    {
        public static int Pass { get; private set; }

        public static int Total { get; private set; }

        public static async Task<int> RunAsync(Action<string, bool, string> check)
        {
            await RunAsync(check, _ => { });
            return Pass;
        }

        /// <summary>
        /// <paramref name="settle"/> 在每段之间被调用，用来让调用方把异步端点的日志/连接排空。
        /// </summary>
        public static async Task RunAsync(Action<string, bool, string> check, Action<string> settle)
        {
            Pass = 0;
            Total = 0;

            void Check(string id, bool ok, string detail)
            {
                Total++;
                if (ok) Pass++;
                check(id, ok, detail);
            }

            await Sw1NoSolutionAsync(Check);
            settle("SW1");

            await Sw2SnapshotAsync(Check);
            settle("SW2");

            await Sw3SolutionChangedAsync(Check);
            settle("SW3");

            await Sw4RevokeAllAsync(Check);
            settle("SW4");

            await Sw5OnlineShowsGroupAsync(Check);
            settle("SW5");

            await Sw6RevokeGroupAsync(Check);
            settle("SW6");

            await Sw7GroupPermissionChangeAsync(Check);
            settle("SW7");

            await Sw8ReflectionAsync(Check);
            settle("SW8");

            await Sw9ModuleWiringAsync(Check);
            settle("SW9");
        }

        // ============================================================
        //  SW1：无方案 → 503 E_NO_SOLUTION，且零 PBKDF2、零默认密码闸门
        // ============================================================

        private static async Task Sw1NoSolutionAsync(Action<string, bool, string> check)
        {
            // 无方案：上下文解析得到，但 SolutionId == null（用户还没打开方案，属正常状态）。
            var solution = new FakeSolutionContext(null);
            var accounts = new CountingAccounts();
            await using var fx = await WebFixture.StartAsync(solution, accounts);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // SW1-a：匿名取票（空体 { }）
            var resp = await PostTicketRawAsync(fx, "{}");
            var body = await resp.Content.ReadAsStringAsync();
            check("SW1-a 无方案：匿名取票 503 E_NO_SOLUTION",
                (int)resp.StatusCode == 503 && body.Contains("E_NO_SOLUTION"),
                $"status={(int)resp.StatusCode} body={Trim(body, 120)}");

            // SW1-b：带账号密码取票同样是 503（无方案时连"验密码"都不该发生）
            resp = await PostTicketRawAsync(fx, "{\"user\":\"admin\",\"password\":\"admin123\"}");
            body = await resp.Content.ReadAsStringAsync();
            check("SW1-b 无方案：带账号取票仍 503（不是 401）",
                (int)resp.StatusCode == 503 && body.Contains("E_NO_SOLUTION"),
                $"status={(int)resp.StatusCode} body={Trim(body, 120)}");

            // SW1-c：续期令牌同样 503
            resp = await PostTicketRawAsync(fx, "{\"refreshToken\":\"00000000000000000000000000000000\"}");
            body = await resp.Content.ReadAsStringAsync();
            check("SW1-c 无方案：续期取票也 503",
                (int)resp.StatusCode == 503 && body.Contains("E_NO_SOLUTION"),
                $"status={(int)resp.StatusCode} body={Trim(body, 120)}");

            // SW1-d：零 PBKDF2 —— 503 必须早于登录分支
            check("SW1-d 无方案：零次账号校验（PBKDF2 没跑）",
                accounts.ValidateCount == 0,
                "validate=" + accounts.ValidateCount);

            // SW1-e：零默认密码闸门
            check("SW1-e 无方案：零次出厂密码判定",
                accounts.DefaultPasswordChecks == 0,
                "defaultPwd=" + accounts.DefaultPasswordChecks);

            // SW1-f：宿主仍在监听（503 是取票层判定，不是启动失败）
            using var probe = new TcpClient();
            var listening = false;
            try
            {
                await probe.ConnectAsync(IPAddress.Loopback, fx.Port);
                listening = probe.Connected;
            }
            catch { listening = false; }

            check("SW1-f 无方案：宿主仍监听（503 是取票层判定，不是启动失败）",
                listening, "port=" + fx.Port);
        }

        // ============================================================
        //  SW2：票/会话快照是 GroupId + Permissions，没有 Role
        // ============================================================

        private static async Task Sw2SnapshotAsync(Action<string, bool, string> check)
        {
            var solution = new FakeSolutionContext(Guid.NewGuid());
            var accounts = new CountingAccounts();
            await using var fx = await WebFixture.StartAsync(solution, accounts);

            // SW2-a：票 grant 上有组与权限，没有 Role
            var grantType = typeof(ScadaWebTicketGrant);
            var grantProps = grantType.GetProperties().Select(p => p.Name).ToArray();
            check("SW2-a 票 grant 有 GroupId/GroupName/Permissions，没有 Role",
                grantProps.Contains("GroupId")
                    && grantProps.Contains("GroupName")
                    && grantProps.Contains("Permissions")
                    && !grantProps.Contains("Role"),
                string.Join(",", grantProps));

            // SW2-b：会话 DTO 同样
            var infoProps = typeof(ScadaWebSessionInfo).GetProperties().Select(p => p.Name).ToArray();
            check("SW2-b 会话 DTO 有 GroupId/GroupName/Permissions，没有 Role",
                infoProps.Contains("GroupId")
                    && infoProps.Contains("GroupName")
                    && infoProps.Contains("Permissions")
                    && !infoProps.Contains("Role"),
                string.Join(",", infoProps));

            // SW2-c：签票时真的把组与权限抄了进去（结构对了但内容没填是另一种坏）
            var issued = IssueFor(fx.Host.Tickets, "op1", "G-lineop", "产线操作员",
                ScadaPermissionKeys.ViewRuntime, ScadaPermissionKeys.Stop);
            check("SW2-c 签出的票带着组 Id / 组名 / 权限快照",
                issued.GroupId == "G-lineop"
                    && issued.GroupName == "产线操作员"
                    && issued.Permissions.Count == 2
                    && issued.Permissions.Contains(ScadaPermissionKeys.Stop),
                $"group={issued.GroupId}/{issued.GroupName} perms={string.Join("+", issued.Permissions)}");

            // SW2-d：票与会话都盖了方案身份戳
            check("SW2-d 票带 SolutionId + Epoch（方案身份戳）",
                issued.SolutionId == solution.SolutionId && issued.Epoch == solution.Epoch,
                $"ticketSol={issued.SolutionId} epoch={issued.Epoch} ctxSol={solution.SolutionId}/{solution.Epoch}");

            var redeemed = fx.Host.Tickets.TryRedeem(issued.Ticket, "127.0.0.1", true,
                out var grant, out _, out _);
            var sessionGrant = grant?.Session;
            check("SW2-e 会话令牌沿用票上的方案身份戳（8 小时凭据不重新读当前方案）",
                redeemed && sessionGrant != null
                    && sessionGrant.SolutionId == solution.SolutionId
                    && sessionGrant.Epoch == solution.Epoch,
                $"sessionSol={sessionGrant?.SolutionId} epoch={sessionGrant?.Epoch}");
        }

        // ============================================================
        //  SW3：切方案 → E_SOLUTION_CHANGED，且与「被撤销」「已过期」三者可分
        // ============================================================

        private static async Task Sw3SolutionChangedAsync(Action<string, bool, string> check)
        {
            var solutionId = Guid.NewGuid();
            var solution = new FakeSolutionContext(solutionId);
            await using var fx = await WebFixture.StartAsync(solution, new CountingAccounts());
            var tickets = fx.Host.Tickets;

            // SW3-a：同一方案代内取票 → 核销正常（先证明闸门不是一律拒绝）
            var live = IssueFor(tickets, "op1", "G-lineop", "产线操作员",
                ScadaPermissionKeys.ViewRuntime);
            var okRedeem = tickets.TryRedeem(live.Ticket, "127.0.0.1", true,
                out var liveGrant, out _, out var okCode);
            check("SW3-a 同一方案代：取票 → 核销成功",
                okRedeem && okCode == null && liveGrant != null,
                "code=" + (okCode ?? "<null>"));

            // SW3-b：切方案后用旧票核销 → E_SOLUTION_CHANGED
            var staleTicket = IssueFor(tickets, "op2", "G-general", "普工",
                ScadaPermissionKeys.ViewRuntime);
            solution.SwitchTo(Guid.NewGuid());

            var staleRedeem = tickets.TryRedeem(staleTicket.Ticket, "127.0.0.1", true,
                out _, out _, out var staleCode);
            check("SW3-b 切方案后用旧票核销 → E_SOLUTION_CHANGED（不是 E_SESSION_REVOKED）",
                !staleRedeem && staleCode == ScadaWebTicketService.ErrorSolutionChanged,
                "code=" + (staleCode ?? "<null>"));

            // SW3-c：旧会话令牌续期 → E_SOLUTION_CHANGED。
            // 这条走的是 grant 上的 epoch 闸（没有任何人调 RevokeAll），
            // 与 SW3-d 的 RevokeAll 主路径互补。
            // ★ 必须"先签票、再切方案、再续期"：SW3-b 已经切过一次，若这里直接签，
            //   票上盖的就是新代，续期当然成功——那样这条断言会退化成"测了个寂寞"。
            var tokenOwner = IssueFor(tickets, "op3", "G-general", "普工",
                ScadaPermissionKeys.ViewRuntime);
            tickets.TryRedeem(tokenOwner.Ticket, "127.0.0.1", true, out var ownerGrant, out _, out _);
            var token = ownerGrant?.Session?.Token;

            solution.SwitchTo(Guid.NewGuid());

            var resolved = tickets.TryResolveSession(token, "127.0.0.1", out _, out var sessionCode);
            check("SW3-c 切方案后旧会话令牌续期 → E_SOLUTION_CHANGED（epoch 闸）",
                !resolved && sessionCode == ScadaWebTicketService.ErrorSolutionChanged,
                "code=" + (sessionCode ?? "<null>"));

            // SW3-d：走 RevokeAll 的主路径也是 E_SOLUTION_CHANGED（第四张表兜住）
            var viaRevokeAll = IssueFor(tickets, "op4", "G-general", "普工",
                ScadaPermissionKeys.ViewRuntime);
            tickets.TryRedeem(viaRevokeAll.Ticket, "127.0.0.1", true, out var grant4, out _, out _);
            var token4 = grant4?.Session?.Token;

            tickets.RevokeAll();
            var resolved4 = tickets.TryResolveSession(token4, "127.0.0.1", out _, out var code4);
            check("SW3-d 切方案（RevokeAll 路径）后令牌 → E_SOLUTION_CHANGED，不是「被管理员下线」",
                !resolved4 && code4 == ScadaWebTicketService.ErrorSolutionChanged,
                "code=" + (code4 ?? "<null>"));

            // SW3-e：同代内被普通撤销，仍是 E_SESSION_REVOKED（反向：三种不能合成一种）
            var plain = IssueFor(tickets, "op5", "G-general", "普工",
                ScadaPermissionKeys.ViewRuntime);
            tickets.TryRedeem(plain.Ticket, "127.0.0.1", true, out var grant5, out _, out _);
            var token5 = grant5?.Session?.Token;
            tickets.RevokeSession(token5);

            var resolved5 = tickets.TryResolveSession(token5, "127.0.0.1", out _, out var code5);
            check("SW3-e 同代内普通撤销仍报 E_SESSION_REVOKED（三分不能合并成一种）",
                !resolved5 && code5 == ScadaWebTicketService.ErrorSessionRevoked,
                "code=" + (code5 ?? "<null>"));

            // SW3-f：只换代（SolutionId 不变、Epoch++）也必须失效。
            // 判据是"只比 SolutionId 不够"——同一个方案重开/重绑时 id 不变，但那是一次真实重绑。
            var sameId = solution.SolutionId;
            var reopened = IssueFor(tickets, "op6", "G-general", "普工",
                ScadaPermissionKeys.ViewRuntime);
            solution.SwitchTo(sameId);

            var reopenedRedeem = tickets.TryRedeem(reopened.Ticket, "127.0.0.1", true,
                out _, out _, out var reopenedCode);
            check("SW3-f 同一方案重开（id 不变、Epoch++）旧票也失效",
                !reopenedRedeem && reopenedCode == ScadaWebTicketService.ErrorSolutionChanged,
                $"sol={sameId} code={(reopenedCode ?? "<null>")}");
        }

        // ============================================================
        //  SW4：RevokeAll 同时清 _live / _handoff / _sessions
        // ============================================================

        private static async Task Sw4RevokeAllAsync(Action<string, bool, string> check)
        {
            var solution = new FakeSolutionContext(Guid.NewGuid());
            await using var fx = await WebFixture.StartAsync(solution, new CountingAccounts());
            var tickets = fx.Host.Tickets;

            var a = IssueFor(tickets, "admin", "tpl-admin", "模板·管理员",
                ScadaPermissionKeys.UserAdministration);
            var b = IssueFor(tickets, "bob", "G-general", "普工",
                ScadaPermissionKeys.ViewRuntime);
            var pending = IssueFor(tickets, "carol", "G-lineop", "产线操作员",
                ScadaPermissionKeys.ViewRuntime);

            tickets.TryRedeem(a.Ticket, "127.0.0.1", true, out _, out _, out _);
            tickets.TryRedeem(b.Ticket, "127.0.0.1", true, out _, out _, out _);

            check("SW4-a 撤销前：2 枚会话令牌 + 1 张未核销票",
                tickets.SessionCount == 2 && tickets.LiveCount == 1,
                $"sessions={tickets.SessionCount} live={tickets.LiveCount}");

            var revoked = tickets.RevokeAll();

            check("SW4-b RevokeAll 之后三张表全空",
                tickets.SessionCount == 0 && tickets.LiveCount == 0 && tickets.HandoffCount == 0,
                $"sessions={tickets.SessionCount} live={tickets.LiveCount} handoff={tickets.HandoffCount}");

            check("SW4-c RevokeAll 返回被撤销的会话令牌数 = 2",
                revoked == 2, "revoked=" + revoked);

            // SW4-d：未核销的票也失效（只清 _sessions 会让它完成一次握手）
            var reused = tickets.TryRedeem(pending.Ticket, "127.0.0.1", true, out _, out _, out _);
            check("SW4-d 未核销的票在 RevokeAll 后也失效（_live 已清）",
                !reused, "reused=" + reused);
        }

        // ============================================================
        //  SW5：在线会话按组展示；CanOperate 与命令闸同源
        // ============================================================

        private static async Task Sw5OnlineShowsGroupAsync(Action<string, bool, string> check)
        {
            var solution = new FakeSolutionContext(Guid.NewGuid());
            await using var fx = await WebFixture.StartAsync(
                solution, new CountingAccounts(), allowCommands: true);

            var login = await LoginAndConnectAsync(fx, "op1", "pw");
            var online = fx.Host.ListOnline();

            check("SW5-a 在线列表非空且每条都有 GroupId/GroupName",
                online.Count > 0
                    && online.All(s => !string.IsNullOrWhiteSpace(s.GroupId)
                                       && !string.IsNullOrWhiteSpace(s.GroupName)),
                online.Count == 0
                    ? "在线列表为空（ticket=" + (login.Ticket ?? "<null>") + "）"
                    : string.Join(",", online.Select(s => s.GroupId + "/" + s.GroupName)));

            // 总闸开着时，刚登录的会话必须可操作——否则"能点"与"列表说能点"就已经分叉。
            // 这一条与 SW5-d（关掉总闸后立刻变 false）成对：两条一起才钉住
            // ListOnline 用的是"现算"而不是签票时冻结的 ReadOnly。
            check("SW5-b 总闸开着时登录会话 CanOperate=true、ReadOnly=false",
                online.Count > 0 && online.All(s => s.CanOperate && !s.ReadOnly),
                string.Join(",", online.Select(s => s.SessionRef + ":" + s.CanOperate + "/" + s.ReadOnly)));

            check("SW5-c 在线列表的权限快照非空（组权限来自登录那一刻）",
                online.Count > 0 && online.All(s => s.Permissions is { Count: > 0 }),
                string.Join(",", online.Select(s => s.Permissions?.Count.ToString() ?? "null")));

            // SW5-d（反向）：运行期把总闸关掉 → 同一条会话立刻变只读，不需要重连。
            // 这条钉住 ListOnline 用"现算"而不是签票时冻结的 ReadOnly。
            fx.Settings.AllowCommands = false;
            var afterOff = fx.Host.ListOnline();

            check("SW5-d 反向：运行期关掉总闸后同一条会话立刻 CanOperate=false（不靠重连）",
                afterOff.Count > 0 && afterOff.All(s => !s.CanOperate && s.ReadOnly),
                string.Join(",", afterOff.Select(s => s.SessionRef + ":" + s.CanOperate + "/" + s.ReadOnly)));
        }

        // ============================================================
        //  SW6：组撤销只影响该组
        // ============================================================

        private static async Task Sw6RevokeGroupAsync(Action<string, bool, string> check)
        {
            var solution = new FakeSolutionContext(Guid.NewGuid());
            await using var fx = await WebFixture.StartAsync(
                solution, new CountingAccounts(), allowCommands: true);
            var tickets = fx.Host.Tickets;

            var lineop = await LoginAndConnectAsync(fx, "op1", "pw");
            var general = await LoginAndConnectAsync(fx, "op2", "pw");

            check("SW6-a 两条连接都已登记（前置）",
                fx.Host.ListOnline().Count == 2,
                "online=" + fx.Host.ListOnline().Count);

            var revoked = fx.Host.RevokeGroup("G-lineop");

            check("SW6-b RevokeGroup 只撤销该组会话",
                revoked == 1 && tickets.SessionCount == 1,
                $"revoked={revoked} sessions={tickets.SessionCount}");

            check("SW6-c 其他组会话不受影响（按组过滤不是「全撤」的同义词）",
                fx.Host.ListOnline().All(s => s.GroupId == "G-general"),
                string.Join(",", fx.Host.ListOnline().Select(s => s.GroupId)));

            // SW6-d：被撤销组的那枚令牌续期 → E_SESSION_REVOKED（组撤销是「下线」，不是「换方案」）
            var lineopResolved = tickets.TryResolveSession(lineop.SessionToken, "127.0.0.1",
                out _, out var code);
            check("SW6-d 被撤销组的令牌续期 → E_SESSION_REVOKED（不是 E_SOLUTION_CHANGED）",
                !lineopResolved && code == ScadaWebTicketService.ErrorSessionRevoked,
                "code=" + (code ?? "<null>"));

            // SW6-e：另一组的令牌仍然可用
            var generalResolved = tickets.TryResolveSession(general.SessionToken, "127.0.0.1",
                out _, out var code2);
            check("SW6-e 另一组的令牌仍然可用",
                generalResolved && code2 == null, "code=" + (code2 ?? "<null>"));
        }

        // ============================================================
        //  SW7：改组权限 → 该组会话被撤销，且客户端拿到「被管理员下线」
        // ============================================================

        private static async Task Sw7GroupPermissionChangeAsync(Action<string, bool, string> check)
        {
            var solution = new FakeSolutionContext(Guid.NewGuid());
            await using var fx = await WebFixture.StartAsync(
                solution, new CountingAccounts(), allowCommands: true);
            var tickets = fx.Host.Tickets;

            var session = await LoginAndConnectAsync(fx, "op1", "pw");
            var token = session.SessionToken;

            // 协调器（SolutionSessionCoordinator）在组变更时调 RevokeGroup；
            // 这里直调宿主，钉住 Web 侧的行为本身。
            var revoked = fx.Host.RevokeGroup("G-lineop");

            check("SW7-a 改组后该组会话被撤销",
                revoked == 1 && tickets.SessionCount == 0,
                $"revoked={revoked} sessions={tickets.SessionCount}");

            // 与 SW3-d 的反向对照：组撤销不是方案切换，错误码必须是「被管理员下线」
            var resolved = tickets.TryResolveSession(token, "127.0.0.1", out _, out var code);
            check("SW7-b 客户端拿到「被管理员下线」（E_SESSION_REVOKED）而不是「方案已切换」",
                !resolved && code == ScadaWebTicketService.ErrorSessionRevoked,
                "code=" + (code ?? "<null>"));

            // SW7-c：宿主的连接也真的关了（不只是票表里删了）
            check("SW7-c 撤销同时把在线会话清掉（票表与连接表同源）",
                fx.Host.ListOnline().All(s => s.GroupId != "G-lineop"),
                string.Join(",", fx.Host.ListOnline().Select(s => s.GroupId)));
        }

        // ============================================================
        //  SW8：反射 —— 不泄露服务端凭据，也没有角色
        // ============================================================

        private static async Task Sw8ReflectionAsync(Action<string, bool, string> check)
        {
            await Task.CompletedTask;

            var infoType = typeof(ScadaWebSessionInfo);
            var infoProps = infoType.GetProperties().Select(p => p.Name).ToArray();

            check("SW8-a ScadaWebSessionInfo 没有 SessionId（32 hex 是服务端凭据）",
                infoProps.All(p => !p.Contains("SessionId", StringComparison.OrdinalIgnoreCase))
                    && infoProps.Contains("SessionRef"),
                string.Join(",", infoProps));

            check("SW8-b ScadaWebSessionInfo 没有 Role",
                !infoProps.Contains("Role"), string.Join(",", infoProps));

            // SW8-c：反向断言 —— ScadaWebSession.SessionId 必须**继续存在**。
            // 审计（AuditSecurity）与日志都靠它，删掉就再也对不上「哪条连接干的」。
            var sessionType = typeof(ScadaWebSession);
            check("SW8-c 反向：服务端 ScadaWebSession.SessionId 仍然存在（审计要靠它定位连接）",
                sessionType.GetProperty("SessionId") != null,
                string.Join(",", sessionType.GetProperties().Select(p => p.Name)));

            // SW8-d：判权入口按权限 Key，入参里没有角色类型。
            // 两道闸分居两处：Evaluate 管"总闸/认证/只读"，Can 管"这个 Key 在不在快照里"。
            var evaluate = typeof(ScadaWebAccessPolicy).GetMethod(
                "Evaluate", BindingFlags.Public | BindingFlags.Static);
            var evaluateParams = evaluate?.GetParameters().Select(p => p.ParameterType.Name).ToArray()
                                 ?? Array.Empty<string>();

            var can = typeof(ScadaWebAccessPolicy).GetMethod(
                "Can", BindingFlags.Public | BindingFlags.Instance);
            var canParams = can?.GetParameters() ?? Array.Empty<ParameterInfo>();

            check("SW8-d 判权入口没有 ScadaRole，且 Can 的入参是 requiredKey(string)",
                evaluate != null
                    && !evaluateParams.Contains("ScadaRole")
                    && can != null
                    && canParams.Any(p => p.Name == "requiredKey"
                                          && p.ParameterType == typeof(string)),
                "Evaluate(" + string.Join(",", evaluateParams) + ") Can("
                    + string.Join(",", canParams.Select(p => p.Name + ":" + p.ParameterType.Name)) + ")");
        }

        // ============================================================
        //  SW9：模块 fail-closed 接线
        // ============================================================

        private static async Task Sw9ModuleWiringAsync(Action<string, bool, string> check)
        {
            // SW9-a：容器解析不到方案上下文 → TryResolveSolutionContext 返回 null
            //（模块据此记 Error 且不启动 HTTP）
            var throwing = new ThrowingContainer();
            check("SW9-a 缺 IScadaSolutionContext → TryResolveSolutionContext 返回 null（fail-closed）",
                ScadaWebModule.TryResolveSolutionContext(throwing) == null,
                "抛异常时没被接住 = 启动链会被带走");

            // SW9-b：解析得到时原样返回（不是一律 null）
            var expected = new FakeSolutionContext(Guid.NewGuid());
            check("SW9-b 有 IScadaSolutionContext 时原样返回（fail-closed 不等于一律拒绝）",
                ReferenceEquals(ScadaWebModule.TryResolveSolutionContext(new FixedContainer(expected)), expected),
                "返回的不是同一个实例");

            // SW9-c：SubscribeSolutionChanges 在上下文为 null 时返回 null（不抛）
            check("SW9-c SubscribeSolutionChanges(null, host) 返回 null 且不抛",
                ScadaWebModule.SubscribeSolutionChanges(null, null) == null,
                "缺上下文时抛异常 = 启动链断裂");

            // SW9-d：订阅后切方案真的会撤销全部会话
            var solution = new FakeSolutionContext(Guid.NewGuid());
            await using var fx = await WebFixture.StartAsync(solution, new CountingAccounts());
            var handler = ScadaWebModule.SubscribeSolutionChanges(solution, fx.Host);

            var ticket = IssueFor(fx.Host.Tickets, "op1", "G-lineop", "产线操作员",
                ScadaPermissionKeys.ViewRuntime);
            fx.Host.Tickets.TryRedeem(ticket.Ticket, "127.0.0.1", true, out _, out _, out _);
            var before = fx.Host.Tickets.SessionCount;

            solution.SwitchTo(Guid.NewGuid());
            var after = fx.Host.Tickets.SessionCount;

            check("SW9-d 订阅生效：切方案后会话表清空（协调器 → RevokeAllSessions）",
                before == 1 && after == 0, $"before={before} after={after}");

            // SW9-e：退订后不再响应（退出任务必须摘订阅，否则宿主被钉住不放）
            if (handler != null) solution.SolutionChanged -= handler;

            var ticket2 = IssueFor(fx.Host.Tickets, "op2", "G-general", "普工",
                ScadaPermissionKeys.ViewRuntime);
            fx.Host.Tickets.TryRedeem(ticket2.Ticket, "127.0.0.1", true, out _, out _, out _);
            var beforeUnsub = fx.Host.Tickets.SessionCount;

            solution.SwitchTo(Guid.NewGuid());
            var afterUnsub = fx.Host.Tickets.SessionCount;

            check("SW9-e 退订后切方案不再撤销（订阅句柄真的解开了）",
                beforeUnsub == 1 && afterUnsub == 1,
                $"before={beforeUnsub} after={afterUnsub}");
        }

        // ============================================================
        //  小工具
        // ============================================================

        /// <summary>
        /// 签一张「登录用户」的票（不是匿名票）：B3 的身份只有组 + 权限快照。
        /// 收成一个私有助手，免得每处都写七个参数。
        /// </summary>
        private static ScadaWebTicketGrant IssueFor(
            ScadaWebTicketService tickets,
            string user,
            string groupId,
            string groupName,
            params string[] permissions)
            => tickets.Issue(user, groupId, groupName, permissions,
                readOnly: false, allowCommands: true);

        /// <summary>
        /// 走真 HTTP 登录 + WS 握手，返回票与 hello 里的会话令牌。
        ///
        /// <b>为什么必须走真连接</b>：<c>ListOnline</c> / <c>RevokeGroup</c> 读的是宿主的
        /// WS 连接表（<c>_sessions</c>），只签票不握手时那张表是空的——用它断言会得到
        /// 「撤销 0 条」的假绿。
        /// </summary>
        private static async Task<(string? Ticket, string? SessionToken)> LoginAndConnectAsync(
            WebFixture fx, string user, string password)
        {
            var body = await PostTicketJsonAsync(
                fx, "{\"user\":\"" + user + "\",\"password\":\"" + password + "\"}");

            var ticket = ReadJsonString(body, "ticket");
            if (string.IsNullOrEmpty(ticket))
                return (null, null);

            var hello = await fx.ConnectAndWaitHelloAsync(ticket);
            return (ticket, ReadJsonString(hello ?? string.Empty, "sessionToken"));
        }


        // ============================================================
        //  夹具
        // ============================================================

        /// <summary>
        /// 假方案上下文：可空 id（= 没打开方案）、可切换（推 <see cref="SolutionChanged"/>）。
        /// </summary>
        private sealed class FakeSolutionContext : IScadaSolutionContext
        {
            public FakeSolutionContext(Guid? id, string name = "验收方案", string? root = null)
            {
                SolutionId = id;
                SolutionName = name;
                SolutionRoot = root;
            }

            public Guid? SolutionId { get; private set; }

            public string SolutionName { get; }

            public string? SolutionRoot { get; }

            public bool IdentityPersisted => SolutionId != null;

            public long Epoch { get; private set; }

            public string AuditTag => SolutionId is { } id
                ? "[sol=" + id.ToString("N").Substring(0, 12) + " " + SolutionName + "]"
                : "[sol=none]";

            public event Action<ScadaSolutionChange>? SolutionChanged;

            public void SwitchTo(Guid? id)
            {
                var oldId = SolutionId;
                var oldEpoch = Epoch;

                SolutionId = id;
                Epoch++;

                SolutionChanged?.Invoke(new ScadaSolutionChange(oldId, oldEpoch, id, Epoch));
            }
        }

        /// <summary>
        /// 计数账号口：不碰 PBKDF2，只数「被问过几次」。
        /// SW1 用它钉住「无方案时零哈希、零默认密码判定」。
        /// </summary>
        private sealed class CountingAccounts : IScadaUserAuthenticator
        {
            private int _validate;
            private int _defaultPwd;

            public int ValidateCount => Volatile.Read(ref _validate);

            public int DefaultPasswordChecks => Volatile.Read(ref _defaultPwd);

            /// <summary>
            /// 出厂密码判定。SW1-e 要证明「无方案时这一路也没走」，
            /// 所以它必须<b>计数并且永不返回 true</b>（返回 true 会额外触发一条强制改密提示，
            /// 那是另一个断言的事）。
            /// </summary>
            public bool IsUsingDefaultPassword(string? userName)
            {
                Interlocked.Increment(ref _defaultPwd);
                return false;
            }

            public bool TryValidate(string? userName, string? password, out ScadaUserInfo? user, out string? error)
            {
                Interlocked.Increment(ref _validate);

                user = null;
                error = null;

                var name = userName?.Trim() ?? string.Empty;

                if (string.Equals(name, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    user = new ScadaUserInfo("admin", "tpl-admin",
                        ScadaPermissionKeys.BuiltInKeys.ToArray(), "模板·管理员");
                    return true;
                }

                if (string.Equals(name, "op1", StringComparison.OrdinalIgnoreCase))
                {
                    user = new ScadaUserInfo("op1", "G-lineop", new[]
                    {
                        ScadaPermissionKeys.ViewRuntime,
                        ScadaPermissionKeys.Stop,
                    }, "产线操作员");
                    return true;
                }

                if (string.Equals(name, "op2", StringComparison.OrdinalIgnoreCase))
                {
                    user = new ScadaUserInfo("op2", "G-general", new[]
                    {
                        ScadaPermissionKeys.ViewRuntime,
                    }, "普工");
                    return true;
                }

                error = "用户名或密码不正确";
                return false;
            }
        }

        /// <summary>假日志：只吞不写（断言宿主不需要日志落盘）</summary>
        private sealed class FakeLog : ILogService
        {
            public void Success(params string[] messages) { }

            public void Error(params Exception[] messages) { }

            public void Error(params string[] messages) { }

            public void Info(params string[] messages) { }

            public void Warn(params string[] messages) { }
        }

        /// <summary>
        /// 真宿主 + 随机端口。与 <c>M2HttpChecks.HostFixture</c> 同形但独立（理由见类注释）。
        /// </summary>
        private sealed class WebFixture : IAsyncDisposable
        {
            private WebFixture() { }

            public ScadaWebHost Host { get; private set; } = null!;

            public ScadaWebSettings Settings { get; } = new();

            public int Port { get; private set; }

            public string BaseUrl => "http://127.0.0.1:" + Port;

            public static async Task<WebFixture> StartAsync(
                IScadaSolutionContext? solution,
                IScadaUserAuthenticator? authenticator,
                bool allowCommands = false)
            {
                var fixture = new WebFixture();
                var port = GetFreePort();

                fixture.Port = port;
                fixture.Settings.Enabled = true;
                fixture.Settings.Port = port;
                fixture.Settings.BindAddress = "127.0.0.1";
                fixture.Settings.MaxSessions = 16;
                fixture.Settings.AllowAnonymousView = false;
                // 命令面默认关：SW1–SW4/SW9 只关心票与令牌，开着会多一层与判据无关的闸。
                // SW5–SW7 要断言 canOperate，必须显式传 true。
                fixture.Settings.AllowCommands = allowCommands;
                // 本段会连续取票（SW1 三次、SW5 等），默认 10 次/分钟太紧
                fixture.Settings.TicketRatePerMinute = 600;

                fixture.Host = new ScadaWebHost(
                    hub: null,
                    authenticator: authenticator,
                    catalog: null,
                    log: new FakeLog(),
                    settings: fixture.Settings,
                    solutionContext: solution);

                fixture.Host.Start();
                await Task.Delay(60).ConfigureAwait(false);
                return fixture;
            }

            /// <summary>
            /// 取票后立刻握手，等 hello（把会话真正登记进 presence），返回 hello 原文。
            ///
            /// 返回原文是必需的：8 小时会话令牌只在 <c>hello</c> 里下发一次
            /// （首帧 <c>sessionToken</c>），断言「被撤销的令牌续期报什么码」必须拿到它。
            /// </summary>
            public async Task<string?> ConnectAndWaitHelloAsync(string ticket)
            {
                var client = new ClientWebSocket();
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    await client.ConnectAsync(
                        new Uri("ws://127.0.0.1:" + Port + "/ws?ticket=" + ticket), timeout.Token)
                        .ConfigureAwait(false);

                    var buffer = new byte[64 * 1024];
                    using var receiveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    var received = await client.ReceiveAsync(
                        new ArraySegment<byte>(buffer), receiveTimeout.Token).ConfigureAwait(false);

                    return received.Count <= 0
                        ? null
                        : Encoding.UTF8.GetString(buffer, 0, received.Count);
                }
                catch
                {
                    // 握手失败不影响本段判据（SW5 会在「在线列表为空」那条上如实报红）
                    return null;
                }
            }

            public ValueTask DisposeAsync()
            {
                try { Host.StopAsync().GetAwaiter().GetResult(); } catch { }
                return ValueTask.CompletedTask;
            }
        }

        /// <summary>一个永远抛异常的容器：模拟 Prism/DryIoc「没注册」时的真实形态</summary>
        private sealed class ThrowingContainer : IContainerProvider
        {
            public IScopedProvider CurrentScope => null!;

            public IScopedProvider CreateScope()
                => throw new NotSupportedException("断言桩不创建作用域");

            public object Resolve(Type type) => throw new InvalidOperationException("未注册：" + type.Name);

            public object Resolve(Type type, params (Type Type, object Instance)[] parameters)
                => throw new InvalidOperationException("未注册：" + type.Name);

            public object Resolve(Type type, string name) => throw new InvalidOperationException("未注册：" + name);

            public object Resolve(Type type, string name, params (Type Type, object Instance)[] parameters)
                => throw new InvalidOperationException("未注册：" + name);
        }

        /// <summary>一个只认方案上下文的容器</summary>
        private sealed class FixedContainer : IContainerProvider
        {
            private readonly object _instance;

            public FixedContainer(object instance) { _instance = instance; }

            public IScopedProvider CurrentScope => null!;

            public IScopedProvider CreateScope()
                => throw new NotSupportedException("断言桩不创建作用域");

            public object Resolve(Type type)
                => type.IsInstanceOfType(_instance)
                    ? _instance
                    : throw new InvalidOperationException("未注册：" + type.Name);

            public object Resolve(Type type, params (Type Type, object Instance)[] parameters)
                => Resolve(type);

            public object Resolve(Type type, string name) => Resolve(type);

            public object Resolve(Type type, string name, params (Type Type, object Instance)[] parameters)
                => Resolve(type);
        }

        // ============================================================
        //  HTTP 小工具
        // ============================================================

        private static Task<HttpResponseMessage> PostTicketRawAsync(WebFixture fx, string json)
            => PostJsonAsync(fx.BaseUrl + "/api/ticket", json);

        private static async Task<string> PostTicketJsonAsync(WebFixture fx, string json)
        {
            var resp = await PostJsonAsync(fx.BaseUrl + "/api/ticket", json).ConfigureAwait(false);
            return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        // 复用一个 HttpClient：每请求 new 一个会在连续取票时把本机端口耗光
        // （TIME_WAIT 累积，SW3/SW4 各连发十几条）。Timeout 仍按请求级语义保留。
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        private static async Task<HttpResponseMessage> PostJsonAsync(string url, string json)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            return await Http.PostAsync(url, content).ConfigureAwait(false);
        }

        /// <summary>从响应 JSON 里抠一个字符串字段（不引 Newtonsoft，只做最小解析）</summary>
        private static string? ReadJsonString(string json, string field)
        {
            var key = "\"" + field + "\"";
            var at = json.IndexOf(key, StringComparison.Ordinal);
            if (at < 0) return null;

            var colon = json.IndexOf(':', at + key.Length);
            if (colon < 0) return null;

            var open = json.IndexOf('"', colon + 1);
            if (open < 0) return null;

            var close = json.IndexOf('"', open + 1);
            if (close < 0) return null;

            return json.Substring(open + 1, close - open - 1);
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static string Trim(string? text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "<空>";
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }
    }
}
