using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WatsonWebserver;
using WatsonWebserver.Core;
using WatsonWebserver.Core.WebSockets;

internal static class Program
{
    // ---------- config ----------
    private const string Token = "test";
    private static readonly int[] CandidatePorts = { 18080, 18081, 18082 };

    // ---------- state ----------
    private static Webserver _server;
    private static string _baseUrl;
    private static string _wsBaseUrl;
    private static int _authCalls;
    private static volatile bool _authAllow = true;
    private static readonly TaskCompletionSource<BurstResult> BurstTcs =
        new TaskCompletionSource<BurstResult>(TaskCreationOptions.RunContinuationsAsynchronously);

    private static readonly byte[] AssetBytes = BuildAsset();

    // ---------- entry ----------
    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--dump")
        {
            Dump();
            return 0;
        }

        if (args.Length > 0 && args[0] == "--default-ws")
            return await ProbeWebSocketDisabledDefault();

        Console.WriteLine("=== WatsonWebserver WebSocket/Auth probe (v7.2.0) ===");
        Console.WriteLine("dotnet: " + Environment.Version);
        Console.WriteLine();

        var defaults = new WebserverSettings();
        Console.WriteLine("[defaults] WebSockets.Enable           = " + defaults.WebSockets.Enable);
        Console.WriteLine("[defaults] WebSockets.EnableHttp1      = " + defaults.WebSockets.EnableHttp1);
        Console.WriteLine("[defaults] WebSockets.AllowClientSuppliedGuid = " + defaults.WebSockets.AllowClientSuppliedGuid);
        Console.WriteLine("[defaults] WebSockets.MaxMessageSize   = " + defaults.WebSockets.MaxMessageSize);
        Console.WriteLine();

        if (!StartServer(out var startError))
        {
            Console.WriteLine("FATAL: could not start server: " + startError);
            return 2;
        }

        Console.WriteLine("[server] listening " + _baseUrl);
        Console.WriteLine("[server] routes: WS /ws-auth (auth:true), WS /ws-open (auth:false), WS /ws-burst (auth:false), GET /asset (raw), GET /asset-api (ApiRequest)");
        Console.WriteLine();

        var results = new List<string>();

        try
        {
            // ---------------- Phase 1: permissive authenticator (always Success) ----------------
            _authAllow = true;
            Console.WriteLine("---- Phase 1: AuthenticateApiRequest returns Success ----");

            int before = Volatile.Read(ref _authCalls);
            var t1 = await ConnectWs("/ws-auth", withToken: true, "A1 auth:true + Bearer");
            Report(results, "A1", "auth:true WS WITH token -> connects, receives hello",
                t1.Connected && t1.Received == "hello", t1.Describe(), "authCalls +" + Delta(before));

            before = Volatile.Read(ref _authCalls);
            var t2 = await ConnectWs("/ws-auth", withToken: false, "A2 auth:true no token");
            Report(results, "A2", "auth:true WS WITHOUT token -> connects (authenticator says Success)",
                t2.Connected && t2.Received == "hello", t2.Describe(), "authCalls +" + Delta(before));

            before = Volatile.Read(ref _authCalls);
            var t3 = await ConnectWs("/ws-open", withToken: false, "B1 auth:false no token");
            Report(results, "B1", "auth:false WS WITHOUT token -> connects, authCalls delta MUST be 0",
                t3.Connected && t3.Received == "hello" && Delta(before) == 0, t3.Describe(), "authCalls +" + Delta(before));

            before = Volatile.Read(ref _authCalls);
            var t4 = await ConnectWs("/ws-open", withToken: true, "B2 auth:false + Bearer");
            Report(results, "B2", "auth:false WS WITH token -> connects, authCalls delta MUST be 0",
                t4.Connected && t4.Received == "hello" && Delta(before) == 0, t4.Describe(), "authCalls +" + Delta(before));

            // ---------------- Phase 2: denying authenticator ----------------
            _authAllow = false;
            Console.WriteLine();
            Console.WriteLine("---- Phase 2: AuthenticateApiRequest returns NotFound/DeniedImplicit ----");

            before = Volatile.Read(ref _authCalls);
            var t5 = await ConnectWs("/ws-auth", withToken: true, "D1 auth:true + Bearer, auth DENIED");
            Report(results, "D1", "auth:true WS with DENIED authenticator -> handshake rejected",
                !t5.Connected && Delta(before) >= 1, t5.Describe(), "authCalls +" + Delta(before));

            before = Volatile.Read(ref _authCalls);
            var t6 = await ConnectWs("/ws-open", withToken: false, "D2 auth:false, auth DENIED");
            Report(results, "D2", "auth:false WS still connects while authenticator DENIES (default is unsafe)",
                t6.Connected && t6.Received == "hello" && Delta(before) == 0, t6.Describe(), "authCalls +" + Delta(before));

            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                await NoteDeniedHandshakeBody(http);
            }

            // NOTE: what happens on auth:true when the host forgot to set AuthenticateApiRequest?
            var savedAuth = _server.Routes.AuthenticateApiRequest;
            _server.Routes.AuthenticateApiRequest = null;
            var t7 = await ConnectWs("/ws-auth", withToken: false, "auth:true, AuthenticateApiRequest = null");
            _server.Routes.AuthenticateApiRequest = savedAuth;
            Console.WriteLine("   [NOTE] auth:true with AuthenticateApiRequest=null -> " + t7.Describe());

            // ---------------- Phase 3: static bytes ----------------
            Console.WriteLine();
            Console.WriteLine("---- Phase 3: embedded byte array over HTTP ----");
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                await CheckAsset(http, "/asset", "E1", results);
                await CheckAssetApi(http, "/asset-api", "E2", results);
                await NoteAssetWithAuth(http);
            }

            // ---------------- Phase 4: concurrent SendTextAsync ----------------
            Console.WriteLine();
            Console.WriteLine("---- Phase 4: 4 threads x 250 SendTextAsync on one session ----");
            await CheckBurst(results);
        }
        catch (Exception ex)
        {
            Console.WriteLine("FATAL during probe: " + ex);
            return 3;
        }
        finally
        {
            try { _server.Stop(); } catch { }
            try { _server.Dispose(); } catch { }
        }

        Console.WriteLine();
        Console.WriteLine("================ SUMMARY ================");
        foreach (var r in results) Console.WriteLine(r);

        int failed = results.Count(r => r.StartsWith("[FAIL]"));
        Console.WriteLine();
        Console.WriteLine("RESULT: " + (results.Count - failed) + " PASS / " + failed + " FAIL   (authCalls total = " + Volatile.Read(ref _authCalls) + ")");
        return failed == 0 ? 0 : 1;
    }

    // ---------- server ----------
    private static bool StartServer(out string error)
    {
        error = null;
        foreach (var port in CandidatePorts)
        {
            Webserver server = null;
            try
            {
                var settings = new WebserverSettings("127.0.0.1", port, false);
                // IMPORTANT: WebSockets.Enable defaults to false in 7.2.0 - without this no WS handshake happens.
                settings.WebSockets.Enable = true;
                server = new Webserver(settings, DefaultRoute);

                server.Routes.AuthenticateApiRequest = Authenticate;

                // WebSocket routes: auth:true => post-authentication group, auth:false => pre-authentication.
                server.WebSocket("/ws-auth", WsHelloHandler, auth: true);
                server.WebSocket("/ws-open", WsHelloHandler, auth: false);
                server.WebSocket("/ws-burst", WsBurstHandler, auth: false);

                // Raw byte route (no serializer): this is what WebserverBase.Get(...) does internally.
                server.Routes.PreAuthentication.Static.Add(
                    WatsonWebserver.Core.HttpMethod.GET, "/asset", AssetRawHandler);

                // Same raw handler, but registered in the post-authentication group => must pass the authenticator.
                server.Routes.PostAuthentication.Static.Add(
                    WatsonWebserver.Core.HttpMethod.GET, "/asset-auth", AssetRawHandler);

                // Documented convenience API (Func<ApiRequest, Task<object>>) returning a byte[].
                server.Get("/asset-api", AssetApiHandler);

                server.Start();
                if (!server.IsListening)
                {
                    error = "Start() returned but IsListening == false on port " + port;
                    try { server.Dispose(); } catch { }
                    continue;
                }

                _server = server;
                _baseUrl = "http://127.0.0.1:" + port;
                _wsBaseUrl = "ws://127.0.0.1:" + port;
                return true;
            }
            catch (Exception ex)
            {
                error = "port " + port + ": " + ex.GetType().Name + ": " + ex.Message;
                try { server?.Dispose(); } catch { }
            }
        }
        return false;
    }

    private static Task DefaultRoute(HttpContextBase ctx)
    {
        ctx.Response.StatusCode = 404;
        return ctx.Response.Send("not found", CancellationToken.None);
    }

    private static Task<AuthResult> Authenticate(HttpContextBase ctx)
    {
        int n = Interlocked.Increment(ref _authCalls);
        var header = ctx.Request.RetrieveHeaderValue("Authorization");
        bool has = !string.IsNullOrEmpty(header);
        Console.WriteLine(string.Format(
            "   [AUTH #{0}] {1} {2}  Authorization={3}  decision={4}",
            n, ctx.Request.MethodRaw, ctx.Request.Url.RawWithoutQuery,
            has ? header : "<none>",
            _authAllow ? "Success/Permitted" : "NotFound/DeniedImplicit"));

        if (_authAllow)
        {
            return Task.FromResult(new AuthResult
            {
                AuthenticationResult = AuthenticationResultEnum.Success,
                AuthorizationResult = AuthorizationResultEnum.Permitted,
            });
        }

        return Task.FromResult(new AuthResult
        {
            AuthenticationResult = AuthenticationResultEnum.NotFound,
            AuthorizationResult = AuthorizationResultEnum.DeniedImplicit,
        });
    }

    private static async Task WsHelloHandler(HttpContextBase ctx, WebSocketSession session)
    {
        var header = ctx.Request.RetrieveHeaderValue("Authorization");
        Console.WriteLine(string.Format(
            "   [WS] handler START path={0} session={1} Authorization={2}",
            ctx.Request.Url.RawWithoutQuery, session.Id,
            string.IsNullOrEmpty(header) ? "<none>" : header));
        try
        {
            await session.SendTextAsync("hello", CancellationToken.None);
            while (true)
            {
                var msg = await session.ReceiveAsync(CancellationToken.None);
                if (msg == null) break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("   [WS] handler EX path=" + ctx.Request.Url.RawWithoutQuery + " " + ex.GetType().Name + ": " + ex.Message);
        }
        Console.WriteLine("   [WS] handler END session=" + session.Id);
    }

    private static async Task WsBurstHandler(HttpContextBase ctx, WebSocketSession session)
    {
        Console.WriteLine("   [WS] burst handler START session=" + session.Id);
        Console.WriteLine("   [WS] WebSocketSession private fields: " + string.Join(", ",
            typeof(WebSocketSession).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.FieldType.Name + " " + f.Name)));

        var errors = new ConcurrentBag<string>();
        var sw = Stopwatch.StartNew();
        var workers = new Task[4];
        for (int t = 0; t < 4; t++)
        {
            int tid = t;
            // LongRunning => dedicated OS thread, so the 4 senders really run in parallel.
            workers[t] = Task.Factory.StartNew(async () =>
            {
                for (int i = 0; i < 250; i++)
                {
                    try
                    {
                        await session.SendTextAsync("burst-" + tid + "-" + i, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        errors.Add("thread" + tid + " msg" + i + " -> " + ex.GetType().FullName + ": " + ex.Message);
                    }
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        }

        await Task.WhenAll(workers);
        sw.Stop();
        BurstTcs.TrySetResult(new BurstResult(errors.Count, errors.ToArray(), sw.ElapsedMilliseconds));
        Console.WriteLine("   [WS] burst handler sends done errors=" + errors.Count + " in " + sw.ElapsedMilliseconds + " ms");

        try
        {
            while (await session.ReceiveAsync(CancellationToken.None) != null) { }
        }
        catch { }
        Console.WriteLine("   [WS] burst handler END session=" + session.Id);
    }

    private static async Task AssetRawHandler(HttpContextBase ctx)
    {
        Console.WriteLine("   [HTTP] raw /asset handler");
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "application/octet-stream";
        await ctx.Response.Send(AssetBytes, CancellationToken.None);
    }

    private static Task<object> AssetApiHandler(ApiRequest req)
    {
        Console.WriteLine("   [HTTP] api /asset-api handler");
        req.Http.Response.StatusCode = 200;
        req.Http.Response.ContentType = "application/octet-stream";
        return Task.FromResult<object>(AssetBytes);
    }

    private static byte[] BuildAsset()
    {
        var b = new byte[1024];
        var head = Encoding.ASCII.GetBytes("VM-ASSET-BEGIN");
        Array.Copy(head, b, head.Length);
        for (int i = 0; i < b.Length; i++) b[i] = (byte)((i * 7 + 13) % 256);
        Array.Copy(head, b, head.Length);
        return b;
    }

    // ---------- client ----------
    private sealed class WsAttempt
    {
        public bool Connected;
        public string Received = "";
        public string Error = "";
        public string Label;

        public string Describe()
        {
            if (Connected) return "connected, recv=\"" + Received + "\"";
            return "connect FAILED: " + Error;
        }
    }

    private static async Task<WsAttempt> ConnectWs(string path, bool withToken, string label)
    {
        var a = new WsAttempt { Label = label };
        using (var ws = new ClientWebSocket())
        {
            if (withToken) ws.Options.SetRequestHeader("Authorization", "Bearer " + Token);
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                try
                {
                    await ws.ConnectAsync(new Uri(_wsBaseUrl + path), cts.Token);
                    a.Connected = true;

                    var buf = new byte[8192];
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token);
                    a.Received = r.MessageType == WebSocketMessageType.Text
                        ? Encoding.UTF8.GetString(buf, 0, r.Count)
                        : "<binary " + r.Count + ">";

                    try
                    {
                        using (var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token);
                    }
                    catch { /* close is best-effort */ }
                }
                catch (Exception ex)
                {
                    a.Error = ex.GetType().FullName + ": " + ex.Message;
                    if (ex.InnerException != null)
                        a.Error += " | inner " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message;
                    if (ex is WebSocketException wse)
                        a.Error += " | WebSocketErrorCode=" + wse.WebSocketErrorCode;
                }
            }
        }

        Console.WriteLine("   [CLIENT] " + label + " -> " + a.Describe());
        await Task.Delay(150);
        return a;
    }

    private static async Task CheckAsset(HttpClient http, string path, string id, List<string> results)
    {
        int before = Volatile.Read(ref _authCalls);
        try
        {
            using (var resp = await http.GetAsync(_baseUrl + path))
            {
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                var ct = resp.Content.Headers.ContentType == null ? "<none>" : resp.Content.Headers.ContentType.ToString();
                string head = Encoding.ASCII.GetString(bytes.Take(Math.Min(20, bytes.Length)).ToArray());
                bool exact = bytes.Length == AssetBytes.Length && bytes.SequenceEqual(AssetBytes);
                Console.WriteLine(string.Format(
                    "   [CLIENT] GET {0} -> status={1} len={2} contentType={3} head=\"{4}\" exactMatch={5}",
                    path, (int)resp.StatusCode, bytes.Length, ct, head, exact));
                Report(results, id, "GET " + path + " returns embedded bytes verbatim",
                    exact && (int)resp.StatusCode == 200,
                    "status=" + (int)resp.StatusCode + " len=" + bytes.Length + " ct=" + ct + " head=\"" + head + "\"",
                    "authCalls +" + Delta(before));
            }
        }
        catch (Exception ex)
        {
            Report(results, id, "GET " + path + " returns embedded bytes verbatim", false,
                "EX " + ex.GetType().Name + ": " + ex.Message, "authCalls +" + Delta(before));
        }
    }

    private static async Task NoteAssetWithAuth(HttpClient http)
    {
        _authAllow = false;
        int before = Volatile.Read(ref _authCalls);
        try
        {
            using (var resp = await http.GetAsync(_baseUrl + "/asset-auth"))
            {
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                Console.WriteLine("   [NOTE] GET /asset-auth (PostAuthentication.Static) with DENY -> status=" +
                                  (int)resp.StatusCode + " len=" + bytes.Length + " authCalls +" + Delta(before));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("   [NOTE] GET /asset-auth DENY -> EX " + ex.GetType().Name + ": " + ex.Message);
        }

        _authAllow = true;
        before = Volatile.Read(ref _authCalls);
        try
        {
            using (var resp = await http.GetAsync(_baseUrl + "/asset-auth"))
            {
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                bool exact = bytes.SequenceEqual(AssetBytes);
                Console.WriteLine("   [NOTE] GET /asset-auth with ALLOW -> status=" + (int)resp.StatusCode +
                                  " len=" + bytes.Length + " exactMatch=" + exact + " authCalls +" + Delta(before));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("   [NOTE] GET /asset-auth ALLOW -> EX " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task NoteDeniedHandshakeBody(HttpClient http)
    {
        int before = Volatile.Read(ref _authCalls);
        try
        {
            using (var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, _baseUrl + "/ws-auth"))
            {
                req.Headers.Connection.Add("Upgrade");
                req.Headers.TryAddWithoutValidation("Upgrade", "websocket");
                req.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");
                req.Headers.TryAddWithoutValidation("Sec-WebSocket-Key",
                    Convert.ToBase64String(Guid.NewGuid().ToByteArray()));
                using (var resp = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead))
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    Console.WriteLine("   [NOTE] denied handshake raw response: status=" + (int)resp.StatusCode +
                                      " authCalls +" + Delta(before) + " body=\"" + body + "\"");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("   [NOTE] denied handshake raw request failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task CheckAssetApi(HttpClient http, string path, string id, List<string> results)
    {
        int before = Volatile.Read(ref _authCalls);
        try
        {
            using (var resp = await http.GetAsync(_baseUrl + path))
            {
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                var ct = resp.Content.Headers.ContentType == null ? "<none>" : resp.Content.Headers.ContentType.ToString();
                string text = Encoding.UTF8.GetString(bytes.Take(Math.Min(60, bytes.Length)).ToArray());
                bool raw = bytes.Length == AssetBytes.Length && bytes.SequenceEqual(AssetBytes);
                bool base64Json = text.StartsWith("\"Vk0tQVNTRVQtQkVHSU5", StringComparison.Ordinal);
                Console.WriteLine(string.Format(
                    "   [CLIENT] GET {0} -> status={1} len={2} contentType={3} rawMatch={4} head=\"{5}\"",
                    path, (int)resp.StatusCode, bytes.Length, ct, raw, text));
                // Informational: this route is the documented Func<ApiRequest,Task<object>> API and it
                // always runs the serializer, so byte[] comes back as a JSON string (base64).
                Report(results, id,
                    "ApiRequest route serializes byte[] to JSON/base64 (NOT raw) -> raw serving must use Static route",
                    !raw && base64Json,
                    "status=" + (int)resp.StatusCode + " len=" + bytes.Length + " rawMatch=" + raw + " head=\"" + text + "\"",
                    "authCalls +" + Delta(before));
            }
        }
        catch (Exception ex)
        {
            Report(results, id, "ApiRequest route serializes byte[] to JSON/base64 (NOT raw)", false,
                "EX " + ex.GetType().Name + ": " + ex.Message, "authCalls +" + Delta(before));
        }
    }

    private static async Task<int> ProbeWebSocketDisabledDefault()
    {
        Console.WriteLine("=== probe: WebSocket with DEFAULT settings (WebSockets.Enable untouched) ===");
        var settings = new WebserverSettings("127.0.0.1", 18090, false);
        Console.WriteLine("[default-ws] WebSockets.Enable = " + settings.WebSockets.Enable);
        using (var server = new Webserver(settings, DefaultRoute))
        {
            server.WebSocket("/ws-open", WsHelloHandler, auth: false);
            server.Start();
            Console.WriteLine("[default-ws] listening http://127.0.0.1:18090  (IsListening=" + server.IsListening + ")");
            _wsBaseUrl = "ws://127.0.0.1:18090";
            _baseUrl = "http://127.0.0.1:18090";
            var a = await ConnectWs("/ws-open", withToken: false, "default settings, no Enable");
            Console.WriteLine("[default-ws] RESULT: " + a.Describe());
            try { server.Stop(); } catch { }
        }
        return 0;
    }

    private static async Task CheckBurst(List<string> results)
    {
        int received = 0;
        string error = "";
        int serverErrors = -1;
        long serverMs = -1;

        using (var ws = new ClientWebSocket())
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
        {
            try
            {
                await ws.ConnectAsync(new Uri(_wsBaseUrl + "/ws-burst"), cts.Token);
                var buf = new byte[8192];
                while (received < 1000)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    received++;
                }
                try
                {
                    using (var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token);
                }
                catch { }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
            }
        }

        var done = await Task.WhenAny(BurstTcs.Task, Task.Delay(5000));
        if (done == BurstTcs.Task)
        {
            var br = BurstTcs.Task.Result;
            serverErrors = br.ErrorCount;
            serverMs = br.ElapsedMs;
            Console.WriteLine("   [SERVER] burst errors=" + br.ErrorCount + " elapsed=" + br.ElapsedMs + " ms");
            foreach (var e in br.Errors.Take(4)) Console.WriteLine("      " + e);
        }

        Console.WriteLine(string.Format(
            "   [CLIENT] burst -> clientReceived={0}/1000 serverSendErrors={1} elapsed={2}ms clientError=\"{3}\"",
            received, serverErrors, serverMs, error));

        bool pass = serverErrors == 0 && received == 1000;
        Report(results, "F",
            "concurrent SendTextAsync on one session is safe (4 threads x 250 msgs, all 1000 delivered, no server exception)",
            pass,
            "serverSendErrors=" + serverErrors + " clientReceived=" + received + "/1000 elapsed=" + serverMs + "ms" +
            (error.Length > 0 ? " clientEx=" + error : ""),
            "see [SERVER] lines above for exception text");
    }

    private sealed class BurstResult
    {
        public BurstResult(int errorCount, string[] errors, long elapsedMs)
        {
            ErrorCount = errorCount;
            Errors = errors;
            ElapsedMs = elapsedMs;
        }
        public int ErrorCount { get; }
        public string[] Errors { get; }
        public long ElapsedMs { get; }
    }

    // ---------- helpers ----------
    private static int Delta(int before) => Volatile.Read(ref _authCalls) - before;

    private static void Report(List<string> results, string id, string what, bool pass, string evidence, string extra)
    {
        var line = string.Format("[{0}] {1} {2} :: {3} | {4}",
            pass ? "PASS" : "FAIL", id, what, evidence, extra);
        results.Add(line);
        Console.WriteLine("   >>> " + line);
    }

    // ---------- API dump (evidence for README) ----------
    private static void Dump()
    {
        var asm = typeof(WatsonWebserver.Webserver).Assembly;
        Console.WriteLine("=== ASSEMBLY: " + asm.FullName);

        string[] typeNames =
        {
            "WatsonWebserver.Core.WebserverBase",
            "WatsonWebserver.Core.AuthResult",
            "WatsonWebserver.Core.AuthenticationResultEnum",
            "WatsonWebserver.Core.AuthorizationResultEnum",
            "WatsonWebserver.Core.Routing.WebserverRoutes",
            "WatsonWebserver.Core.Routing.RoutingGroup",
            "WatsonWebserver.Core.Routing.StaticRouteManager",
            "WatsonWebserver.Core.WebSockets.WebSocketRoute",
            "WatsonWebserver.Core.WebSockets.WebSocketRouteManager",
            "WatsonWebserver.Core.WebSockets.WebSocketSession",
            "WatsonWebserver.Core.HttpResponseBase",
            "WatsonWebserver.Core.ApiRequest",
            "WatsonWebserver.Core.WebserverSettings",
            "WatsonWebserver.Core.Settings.WebSocketSettings",
        };

        foreach (var tn in typeNames)
        {
            var t = asm.GetType(tn);
            if (t == null) { Console.WriteLine("\n### " + tn + " -> NOT FOUND"); continue; }
            Console.WriteLine("\n### " + tn + (t.IsEnum ? "  [enum]" : ""));
            if (t.IsEnum)
            {
                Console.WriteLine("   values: " + string.Join(", ", Enum.GetNames(t)));
                continue;
            }
            foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                Console.WriteLine("   .ctor(" + Sig(c.GetParameters()) + ")");
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).OrderBy(x => x.Name))
                Console.WriteLine("   prop " + TypeName(p.PropertyType) + " " + p.Name + " { " + (p.CanRead ? "get; " : "") + (p.CanWrite ? "set; " : "") + "}");
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                              .Where(m => !m.IsSpecialName).OrderBy(x => x.Name))
                Console.WriteLine("   " + (m.IsStatic ? "static " : "") + TypeName(m.ReturnType) + " " + m.Name + "(" + Sig(m.GetParameters()) + ")");
        }
    }

    private static string Sig(ParameterInfo[] ps) =>
        string.Join(", ", ps.Select(p => TypeName(p.ParameterType) + " " + p.Name + (p.HasDefaultValue ? " = " + (p.DefaultValue ?? "null") : "")));

    private static string TypeName(Type t)
    {
        if (t == null) return "?";
        if (!t.IsGenericType) return t.Name;
        var n = t.Name.Split('`')[0];
        return n + "<" + string.Join(", ", t.GetGenericArguments().Select(TypeName)) + ">";
    }
}
