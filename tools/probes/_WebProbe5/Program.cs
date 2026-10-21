using System;
using System.Collections.Generic;
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
using Newtonsoft.Json.Linq;
using VisionMaster.Models;
using VisionMaster.Lifetime;
using VisionMaster.Scada;
using VisionMaster.ScadaWeb;
using WatsonWebserver;
using WatsonWebserver.Core;
using WatsonWebserver.Core.WebSockets;

namespace WebProbe5
{
    /// <summary>
    /// M1a 探针：SCADA 运行态 Web 的 HTTP + WebSocket 骨架。
    ///
    /// 判据（对应任务书的 A01–A09 / F23 / F24 服务端前置）：
    ///   S1  起服务（随机端口）+ 启动断言三连（WS 已启用）
    ///   S2  GET /api/status 200，未运行时也 200 且 running=false
    ///   S3  运行中 → running/pageId/pageName/pageVersion 正确
    ///   S4  GET /api/catalog 200 且条目数一致（无鉴权）
    ///   S5  GET /img/… → 404（M1b 才实现）
    ///   S6  无凭证 + AllowAnonymousView=false → 401 E_AUTH_REQUIRED
    ///   S7  错密码 → 401 E_AUTH_FAILED
    ///   S8  正确账号 → 200 + 32 hex ticket + expiresIn=30 + readOnly=true
    ///   S9  WS /ws?ticket=… 握手成功
    ///   S10 首帧 hello（type/sessionRef/readOnly）
    ///   S11 ping → pong
    ///   S12 无 ticket → 401
    ///   S13 错 ticket → 401
    ///   S14 复用同一 ticket → 401（单次）
    ///   S15 ticket 全文不进日志（只记前 6 位）
    ///   S16 AllowAnonymousView=true + 无凭证 → 200 viewer ticket（Operator / readOnly）
    ///   S17 匿名 viewer 也能连 WS 并收到 readOnly hello
    ///   S18 断开后在线数回落
    ///   S19 StopAsync 后不再监听 + 幂等
    ///   S20 反证：WebSockets.Enable=false 时 WS 握手 400
    /// </summary>
    internal static class Program
    {
        private static readonly List<string> Results = new();
        private static int _failed;

        private static async Task<int> Main()
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { /* 控制台不支持就算了 */ }

            var port = GetFreePort();
            var settings = new ScadaWebSettings
            {
                Enabled = true,
                Port = port,
                BindAddress = "127.0.0.1",
                AllowAnonymousView = false,
                MaxSessions = 16
            };

            var log = new FakeLog();
            var hub = new FakeRunHub();
            var authenticator = new FakeAuthenticator("admin", "pw", ScadaRole.Administrator);
            var catalog = new FakeCatalog();
            var host = new ScadaWebHost(hub, authenticator, catalog, log, settings);

            host.Start();

            Check("S1", host.IsListening && host.WebSocketsEnabled,
                $"listening={host.IsListening} wsEnabled={host.WebSocketsEnabled} port={host.Port} err={host.LastError ?? "<none>"}");

            var baseUrl = $"http://127.0.0.1:{port}";
            var wsUrl = $"ws://127.0.0.1:{port}/ws";

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // ---------- S2：未运行也 200 ----------
            var (s2Status, s2Body) = await GetAsync(http, baseUrl + "/api/status");
            var s2 = TryJson(s2Body);
            Check("S2", s2Status == 200 && s2 != null && s2.Value<bool>("ok") && !s2.Value<bool>("running")
                       && s2.Value<int>("online") == 0,
                $"status={s2Status} body={s2Body}");

            // ---------- S3：运行中页面信息 ----------
            var document = new ScadaDocument();
            var page = document.AddPage("总览");
            document.SetStartupPage(page);

            var runtime = new ScadaRuntime(document);
            var started = runtime.Start();
            hub.SetRunning(runtime, document, generation: 7);

            var (s3Status, s3Body) = await GetAsync(http, baseUrl + "/api/status");
            var s3 = TryJson(s3Body);
            var pageOk = s3 != null
                         && s3.Value<bool>("running")
                         && string.Equals(s3.Value<string>("pageId"), page.PageId.ToString(), StringComparison.OrdinalIgnoreCase)
                         && s3.Value<string>("pageName") == page.Name
                         && s3.Value<int>("pageVersion") == page.Version;
            Check("S3", started && s3Status == 200 && pageOk,
                $"runtimeStarted={started} status={s3Status} body={s3Body}");

            // ---------- S4：目录（无鉴权） ----------
            var (s4Status, s4Body) = await GetAsync(http, baseUrl + "/api/catalog");
            var s4 = TryJson(s4Body);
            Check("S4", s4Status == 200 && s4 != null && s4.Value<int>("count") == 2
                       && (s4["items"] as JArray)?.Count == 2,
                $"status={s4Status} body={s4Body}");

            // ---------- S5：/img 占位 404 ----------
            var (s5Status, s5Body) = await GetAsync(http, baseUrl + "/img/e-1/3.png");
            var s5 = TryJson(s5Body);
            Check("S5", s5Status == 404 && s5?.Value<string>("code") == "E_IMG_NOT_READY",
                $"status={s5Status} body={s5Body}");

            // ---------- S6：无凭证 + 禁止匿名 ----------
            var (s6Status, s6Body) = await PostJsonAsync(http, baseUrl + "/api/ticket", "{}");
            var s6 = TryJson(s6Body);
            Check("S6", s6Status == 401 && s6?.Value<string>("code") == "E_AUTH_REQUIRED",
                $"status={s6Status} body={s6Body}");

            // ---------- S7：错密码 ----------
            var (s7Status, s7Body) = await PostJsonAsync(http, baseUrl + "/api/ticket",
                "{\"user\":\"admin\",\"password\":\"wrong\"}");
            var s7 = TryJson(s7Body);
            Check("S7", s7Status == 401 && s7?.Value<string>("code") == "E_AUTH_FAILED",
                $"status={s7Status} body={s7Body}");

            // ---------- S8：正确账号 → ticket ----------
            var (s8Status, s8Body) = await PostJsonAsync(http, baseUrl + "/api/ticket",
                "{\"user\":\"admin\",\"password\":\"pw\"}");
            var s8 = TryJson(s8Body);
            var ticket = s8?.Value<string>("ticket");
            Check("S8",
                s8Status == 200
                && ticket != null && ticket.Length == 32
                && s8.Value<int>("expiresIn") == 30
                && s8.Value<bool>("readOnly")
                && s8.Value<string>("role") == "Administrator",
                $"status={s8Status} ticket={Short(ticket)} len={ticket?.Length} body={s8Body}");

            // ---------- S9/S10/S11：WS 握手 + hello + ping ----------
            ClientWebSocket? ws = null;
            if (ticket != null)
            {
                var (s9Ok, s9Err, s9Socket) = await ConnectAsync($"{wsUrl}?ticket={ticket}");
                ws = s9Socket;
                Check("S9", s9Ok, $"connected={s9Ok} err={s9Err ?? "<none>"}");

                if (s9Ok && ws != null)
                {
                    var helloText = await ReceiveTextAsync(ws);
                    var hello = TryJson(helloText);
                    var sessionRef = hello?.Value<string>("sessionRef");

                    Check("S10",
                        hello?.Value<string>("type") == "hello"
                        && !string.IsNullOrEmpty(sessionRef) && sessionRef!.Length == 16
                        && hello.Value<bool>("readOnly")
                        && hello.Value<bool>("running"),
                        $"hello={helloText}");

                    await SendTextAsync(ws, "{\"type\":\"ping\",\"t\":\"abc\"}");

                    // M1b 起 hello 之后紧跟 snapshot（运行中）/ idle，随后每 100ms 一帧 frame：
                    // 这里跳过非 pong 帧再断言，否则拿到的是快照而不是 pong（协议变更，不是回归）
                    JObject? pong = null;
                    string? pongText = null;
                    for (var i = 0; i < 8; i++)
                    {
                        pongText = await ReceiveTextAsync(ws);
                        pong = TryJson(pongText);
                        if (pong?.Value<string>("type") == "pong") break;
                    }

                    Check("S11", pong?.Value<string>("type") == "pong" && pong.Value<string>("t") == "abc",
                        $"pong={pongText}");

                    Check("S17-online", host.OnlineCount == 1, $"online={host.OnlineCount}");
                }
            }
            else
            {
                Check("S9", false, "S8 未拿到 ticket，跳过 WS 检查");
            }

            // ---------- S12/S13/S14：无票 / 错票 / 复用票 ----------
            var (s12Ok, s12Err, _) = await ConnectAsync(wsUrl);
            Check("S12", !s12Ok && Contains401(s12Err), $"connected={s12Ok} err={s12Err}");

            var (s13Ok, s13Err, _) = await ConnectAsync($"{wsUrl}?ticket=0123456789abcdef0123456789abcdef");
            Check("S13", !s13Ok && Contains401(s13Err), $"connected={s13Ok} err={s13Err}");

            if (ticket != null)
            {
                var (s14Ok, s14Err, _) = await ConnectAsync($"{wsUrl}?ticket={ticket}");
                Check("S14", !s14Ok && Contains401(s14Err), $"connected={s14Ok} err={s14Err}");
            }

            // ---------- S15：ticket 全文不进日志 ----------
            if (ticket != null)
            {
                var full = log.Lines.Any(line => line.Contains(ticket, StringComparison.Ordinal));
                var prefix = log.Lines.Any(line => line.Contains(ticket.Substring(0, 6), StringComparison.Ordinal));
                Check("S15", !full && prefix, $"fullInLog={full} prefixInLog={prefix}");
            }

            // ---------- S16/S17：匿名只读 viewer ----------
            settings.AllowAnonymousView = true;

            var (s16Status, s16Body) = await PostJsonAsync(http, baseUrl + "/api/ticket", "{}");
            var s16 = TryJson(s16Body);
            var viewerTicket = s16?.Value<string>("ticket");
            Check("S16",
                s16Status == 200
                && viewerTicket != null && viewerTicket.Length == 32
                && s16.Value<string>("role") == "Operator"
                && s16.Value<bool>("readOnly")
                // Watson 的序列化器会省略 null 成员，所以"没有 user 键"与"user: null"都算匿名
                && (s16["user"] == null || s16["user"]!.Type == JTokenType.Null),
                $"status={s16Status} body={s16Body}");

            if (viewerTicket != null)
            {
                var (vOk, vErr, vSocket) = await ConnectAsync($"{wsUrl}?ticket={viewerTicket}");
                if (vOk && vSocket != null)
                {
                    var vHelloText = await ReceiveTextAsync(vSocket);
                    var vHello = TryJson(vHelloText);
                    Check("S17", vHello?.Value<string>("type") == "hello" && vHello.Value<bool>("readOnly"),
                        $"hello={vHelloText}");

                    try
                    {
                        await vSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    }
                    catch { /* 对端已断开 */ }
                    vSocket.Dispose();
                }
                else
                {
                    Check("S17", false, $"connected={vOk} err={vErr}");
                }
            }

            // ---------- S18：断开后在线数回落 ----------
            if (ws != null)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                catch { /* 对端已断开 */ }
                ws.Dispose();
            }

            var drained = await WaitUntilAsync(() => host.OnlineCount == 0, TimeSpan.FromSeconds(5));
            Check("S18", drained, $"online={host.OnlineCount}（等待断开收尾 5s）");

            // ---------- S19：停服 + 幂等 ----------
            await host.StopAsync();
            var stopped = !host.IsListening;
            await host.StopAsync();   // 第二次不该抛

            var refused = false;
            try
            {
                await GetAsync(http, baseUrl + "/api/status");
            }
            catch
            {
                refused = true;
            }

            Check("S19", stopped && refused, $"stopped={stopped} refusedAfterStop={refused}（重复 StopAsync 未抛）");

            // ---------- S20：反证 WebSockets.Enable=false → 400 ----------
            await RunWebSocketsDisabledReverseProofAsync();

            // ---------- M1：模块 Initialize 幂等（复刻 ScadaChecks 的 ML6 判据） ----------
            RunModuleInitializeCheck();

            Console.WriteLine();
            Console.WriteLine($"RESULT: {Results.Count - _failed} PASS / {_failed} FAIL");
            return _failed == 0 ? 0 : 1;
        }

        // ---------------- 反证：Enable=false ----------------

        private static async Task RunWebSocketsDisabledReverseProofAsync()
        {
            var port = GetFreePort();
            Webserver? server = null;
            try
            {
                var settings = new WebserverSettings("127.0.0.1", port);
                // 刻意保持默认 false（Watson 7.2.0 的默认值）
                server = new Webserver(settings, ctx =>
                {
                    ctx.Response.StatusCode = 404;
                    return ctx.Response.Send("not found", CancellationToken.None);
                });

                server.Routes.AuthenticateApiRequest = _ => Task.FromResult(new AuthResult
                {
                    AuthenticationResult = AuthenticationResultEnum.Success,
                    AuthorizationResult = AuthorizationResultEnum.Permitted
                });

                server.WebSocket("/ws", async (ctx, session) =>
                {
                    await session.SendTextAsync("hello", CancellationToken.None);
                    while (await session.ReceiveAsync(CancellationToken.None) != null) { }
                }, auth: true);

                server.Start();

                var (ok, err, _) = await ConnectAsync($"ws://127.0.0.1:{port}/ws");
                Check("S20", !ok && (err?.Contains("400") ?? false),
                    $"enable={settings.WebSockets.Enable} connected={ok} err={err}");
            }
            catch (Exception ex)
            {
                Check("S20", false, $"反证服务端启动失败：{ex.GetType().Name}：{ex.Message}");
            }
            finally
            {
                try { if (server?.IsListening == true) server.Stop(); } catch { }
                try { server?.Dispose(); } catch { }
            }
        }

        // ---------------- 客户端小工具 ----------------

        private static async Task<(int Status, string Body)> GetAsync(HttpClient http, string url)
        {
            using var response = await http.GetAsync(url);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private static async Task<(int Status, string Body)> PostJsonAsync(HttpClient http, string url, string json)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(url, content);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private static async Task<(bool Ok, string? Error, ClientWebSocket? Socket)> ConnectAsync(string url)
        {
            var socket = new ClientWebSocket();
            try
            {
                await socket.ConnectAsync(new Uri(url), CancellationToken.None);
                return (true, null, socket);
            }
            catch (Exception ex)
            {
                socket.Dispose();
                return (false, $"{ex.GetType().Name}: {ex.Message}", null);
            }
        }

        private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, int timeoutMs = 5000)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var buffer = new byte[8192];
            using var stream = new MemoryStream();

            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) return null;

                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) break;
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static Task SendTextAsync(ClientWebSocket socket, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (predicate()) return true;
                await Task.Delay(50);
            }

            return predicate();
        }

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

        private static bool Contains401(string? error)
            => error != null && (error.Contains("401") || error.Contains("NotAuthorized"));

        private static string Short(string? ticket)
            => string.IsNullOrEmpty(ticket) ? "<null>" : ticket!.Substring(0, Math.Min(6, ticket.Length)) + "…";

        private static void Check(string id, bool ok, string detail)
        {
            var line = (ok ? "[PASS] " : "[FAIL] ") + id + "  " + detail;
            Results.Add(line);
            Console.WriteLine(line);
            if (!ok) _failed++;
        }

        // ---------------- 模块幂等（ScadaChecks ML6 的等价判据） ----------------

        private static void RunModuleInitializeCheck()
        {
            var log = new CountingLog();
            var module = new ScadaWebModule();
            var lifetime = new AppLifetimeService(log);
            var container = new StubContainerProvider(log);

            string? failure = null;
            try
            {
                module.Initialize(container, lifetime);
                module.Initialize(container, lifetime);   // 第二次必须静默返回
            }
            catch (Exception ex)
            {
                failure = ex.ToString();
            }

            Check("M1", failure == null && module.InitializeCount == 1 && log.InfoCount == 1,
                failure ?? $"initializeCount={module.InitializeCount} infoCount={log.InfoCount} host={module.Host?.GetType().Name ?? "<null>"}");
        }

        /// <summary>
        /// 只解析 <see cref="ILogService"/> 的容器桩：与 ScadaChecks 的 StubContainerProvider 同形。
        /// 其余类型返回 null——产品代码必须能容忍"配置取不到"并走"不启动"的降级路径。
        /// </summary>
        private sealed class StubContainerProvider : Prism.Ioc.IContainerProvider
        {
            private readonly ILogService _log;

            public StubContainerProvider(ILogService log) => _log = log;

            public Prism.Ioc.IScopedProvider CurrentScope => null!;

            public Prism.Ioc.IScopedProvider CreateScope()
                => throw new NotSupportedException("探针桩不创建作用域");

            public object Resolve(Type type) => type == typeof(ILogService) ? _log : null!;

            public object Resolve(Type type, params (Type Type, object Instance)[] parameters) => Resolve(type);

            public object Resolve(Type type, string name) => Resolve(type);

            public object Resolve(Type type, string name, params (Type Type, object Instance)[] parameters) => Resolve(type);
        }

        /// <summary>只数 Info 条数的日志桩（ML6 的判据就是 InfoCount == 1）</summary>
        private sealed class CountingLog : ILogService
        {
            public int InfoCount { get; private set; }

            public void Success(params string[] messages) { }
            public void Error(params Exception[] messages) { }
            public void Error(params string[] messages) { }
            public void Info(params string[] messages) => InfoCount++;
            public void Warn(params string[] messages) { }
        }

        // ---------------- 测试替身 ----------------

        private sealed class FakeLog : ILogService
        {
            private readonly object _gate = new();

            public List<string> Lines { get; } = new();

            public void Success(params string[] messages) => Add("SUCCESS", messages);
            public void Error(params Exception[] messages) => Add("ERROR", messages.Select(e => e.ToString()).ToArray());
            public void Error(params string[] messages) => Add("ERROR", messages);
            public void Info(params string[] messages) => Add("INFO", messages);
            public void Warn(params string[] messages) => Add("WARN", messages);

            private void Add(string level, string[] messages)
            {
                lock (_gate)
                {
                    foreach (var message in messages)
                        Lines.Add($"[{level}] {message}");
                }
            }
        }

        private sealed class FakeRunHub : IScadaRunHub
        {
            private ScadaRuntime? _session;
            private ScadaDocument? _document;

            public bool IsRunning => _session != null;

            public ScadaRuntime? Session => _session;

            public ScadaDocument? Document => _document;

            public long RunGeneration { get; private set; }

            public event Action<ScadaRuntime, ScadaDocument, long>? RunStarted;
            public event Action<long>? RunStopped;

            public void SetRunning(ScadaRuntime session, ScadaDocument document, long generation)
            {
                _session = session;
                _document = document;
                RunGeneration = generation;
                RunStarted?.Invoke(session, document, generation);
            }

            public void SetStopped()
            {
                _session = null;
                _document = null;
                RunStopped?.Invoke(RunGeneration);
            }

            public bool TryGetCurrent(out ScadaRuntime? session, out ScadaDocument? document, out long generation)
            {
                session = _session;
                document = _document;
                generation = RunGeneration;
                return session != null;
            }
        }

        private sealed class FakeAuthenticator : IScadaUserAuthenticator
        {
            private readonly string _user;
            private readonly string _password;
            private readonly ScadaRole _role;

            public FakeAuthenticator(string user, string password, ScadaRole role)
            {
                _user = user;
                _password = password;
                _role = role;
            }

            public bool TryValidate(string? userName, string? password, out ScadaRole role, out string? error)
            {
                if (string.Equals(userName, _user, StringComparison.Ordinal)
                    && string.Equals(password, _password, StringComparison.Ordinal))
                {
                    role = _role;
                    error = null;
                    return true;
                }

                role = ScadaRole.Undefined;
                error = "用户名或密码错误";
                return false;
            }

            public bool IsUsingDefaultPassword(string? userName) => false;
        }

        private sealed class FakeCatalog : IScadaRenderCatalog
        {
            public IReadOnlyList<ScadaElementDescriptorInfo> All { get; } = new List<ScadaElementDescriptorInfo>
            {
                new()
                {
                    TypeKey = "Hmi.Tank",
                    DisplayName = "罐体",
                    Category = "工艺",
                    DefaultWidth = 120,
                    DefaultHeight = 160,
                    ShowInToolbox = true,
                    PropertyKeys = new List<string> { "$X", "$Y", "Fill" },
                    EventNames = new List<string> { "Pressed" }
                },
                new()
                {
                    TypeKey = "Hmi.Pump",
                    DisplayName = "泵",
                    Category = "工艺",
                    DefaultWidth = 80,
                    DefaultHeight = 80,
                    ShowInToolbox = true,
                    PropertyKeys = new List<string> { "$X", "$Y" },
                    EventNames = new List<string>()
                }
            };
        }
    }
}
