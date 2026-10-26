using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Core.Interfaces;
using Newtonsoft.Json.Linq;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.ScadaWeb;

namespace ScadaWebChemicalDemo
{
    /// <summary>
    /// 暴力测试模式（<c>--stress</c>）：用**这一套真实的化工画面**压 Web 端四条路径。
    ///
    /// <b>与 <c>Web\ScadaWebStress</c> 的分工</b>
    /// ---------
    /// 那个夹具是"合成负载"：按参数批量造 N 页 × M 图元 × K 绑定，专门压快照体积与序列化。
    /// 本模式是"真实负载"：画面是手摆的（每页图元组合不同、有跨页基座、有 45 个真实量纲的
    /// 模拟量、有 10 条限值报警），压的是**现场真会走的那条路**——
    /// ① 每页图元类型分布不同（渲染器分支全走到）
    /// ② 跨页基座（改一次全页重发）
    /// ③ 高频模拟量（值帧体积与合并）
    /// ④ 宿主切页（结构重建 + 旧页渲染器/定时器回收）
    /// ⑤ 命令（权限 → 限速 → 幂等 → 审计 → 回执）
    /// 两者不能互相替代：合成负载能压到 1000 图元，但压不出"一页里 20 种图元"；
    /// 真实负载压不出数量级，但能证明"现场那种画面"跑得动。
    ///
    /// <b>客户端做什么</b>
    /// ---------
    /// 每个客户端：取票 → 连 WS → 收 hello/snapshot → 背靠背发 N 条 <c>cmd.element</c>
    /// （打一个真按钮）→ 在 hold 窗口里继续收 <c>page</c>/<c>snapshot</c>/<c>frame</c>。
    /// 主线程在命令发完后按 <c>--page-ms</c> 轮播切页，客户端据此验证"切页时结构重建"。
    /// </summary>
    internal static class StressRunner
    {
        public static async Task<int> RunAsync(string[] args)
        {
            var clients = ReadArg(args, "--clients", 20);
            var commands = ReadArg(args, "--commands", 10);
            var pageMs = ReadArg(args, "--page-ms", 1500);
            var holdMs = ReadArg(args, "--holdMs", 4000);
            var tickMs = ReadArg(args, "--tick", 500);

            var plant = new ChemicalPlant();
            var document = plant.Build();
            var values = new PlantValueSource(plant.Variables);

            // ── UI 线程（运行态、快照、命令泵都在这条线程上）────────────────
            Dispatcher? ui = null;
            var uiReady = new ManualResetEventSlim();
            var uiThread = new Thread(() =>
            {
                ui = Dispatcher.CurrentDispatcher;
                uiReady.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "ChemicalStress.UI"
            };
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            uiReady.Wait();

            var runtime = new ScadaRuntime(document);
            ui!.Invoke(() => runtime.Start());

            var alarmEngine = new ScadaAlarmEngine(document, values);
            alarmEngine.Attach();

            var hub = new DemoHub(runtime, document, generation: 1);
            var port = FreePort();
            var settings = new ScadaWebSettings
            {
                Enabled = true,
                Port = port,
                BindAddress = "127.0.0.1",
                AllowAnonymousView = false,
                AllowCommands = true,
                // B3：判权按权限 Key（StressAuthenticator 给全 Key），不再有 ClickPolicy。
                RequireConfirmForDangerous = true,
                OperationLeaseEnabled = false,
                MaxSessions = Math.Max(64, clients + 8),
                TicketRatePerMinute = 10_000,
                CommandRatePerSecond = 10_000,
                ElementRatePerSecond = 10_000
            };

            var log = new NullLog();
            var dispatcher = new PlantActionDispatcher(runtime, document, values, log);
            var writer = new PlantValueWriter(plant);
            var alarms = new PlantAlarmCommands(alarmEngine);
            var feed = new PlantAlarmFeed(alarmEngine);
            var audit = new CountingAudit();

            var host = new ScadaWebHost(
                hub,
                new StressAuthenticator(),
                catalog: null,
                log,
                settings,
                values: values,
                uiDispatcher: ui,
                actionDispatcher: dispatcher,
                valueWriter: writer,
                alarmCommands: alarms,
                audit: audit,
                alarmFeed: feed);

            host.Start();
            if (!host.IsListening)
            {
                Console.WriteLine($"START_FAILED port={port} err={host.LastError}");
                return 2;
            }

            // 模拟量刷新
            var plantTimer = new DispatcherTimer(DispatcherPriority.Background, ui)
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(50, tickMs))
            };
            plantTimer.Tick += (_, _) => plant.Advance();
            plantTimer.Start();

            var alarmTimer = new DispatcherTimer(DispatcherPriority.Background, ui)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            alarmTimer.Tick += (_, _) => alarmEngine.Tick();
            alarmTimer.Start();

            // ── 压测目标：一个真按钮（启动搅拌，在反应釜页）────────────────
            //
            // 必须按"跨页找"而不是"只看启动页"：启动页（装置总览）是纯监视画面，
            // 上面一个操作按钮都没有——这是刻意的（总览页只读，操作集中在设备页）。
            // 第一版按"启动页第一个按钮"找，直接在 .First() 上抛了
            // Sequence contains no matching element。
            var pages = document.Pages.Where(p => !document.IsBasePage(p)).ToList();

            var buttonPage = pages.FirstOrDefault(p => p.Elements.Any(e =>
                    string.Equals(e.TypeKey, "Hmi.Button", StringComparison.Ordinal)
                    && e.Name.Contains("启动搅拌", StringComparison.Ordinal)))
                ?? pages.FirstOrDefault(p => p.Elements.Any(e => e.TypeKey == "Hmi.Button"))
                ?? throw new InvalidOperationException("夹具里没有任何 Hmi.Button，压测无目标");

            var button = buttonPage.Elements.First(e =>
                string.Equals(e.TypeKey, "Hmi.Button", StringComparison.Ordinal)
                && (e.Name.Contains("启动搅拌", StringComparison.Ordinal)
                    || ReferenceEquals(e, buttonPage.Elements.First(x => x.TypeKey == "Hmi.Button"))));

            // 先把运行态切到按钮所在的那一页：命令带 pageId，服务端会拿它与当前页比对，
            // 不一致就是 E_STALE_PAGE（"点的是上一页的按钮"）。镜像模式下所有客户端看同一页，
            // 所以只要宿主切过去，全部客户端的命令都对得上。
            ui.Invoke(() => runtime.Navigate(buttonPage));

            Console.WriteLine($"stress start port={port} clients={clients} commands={commands} "
                + $"pages={pages.Count} pageMs={pageMs} holdMs={holdMs}");
            Console.WriteLine($"pagesDetail={string.Join(",", pages.Select(p => $"{p.Name}:{p.Elements.Count}"))}");
            Console.WriteLine($"button={button.Name} page={buttonPage.Name} elementId={button.ElementId:D}");

            var latencies = new ConcurrentBag<long>();
            var errors = new ConcurrentBag<string>();
            int snapshots = 0, acks = 0, connected = 0, finished = 0, commandsDone = 0;
            int pageMessages = 0, frameMessages = 0;
            long snapshotBytes = 0;

            var total = Stopwatch.StartNew();
            var tasks = new List<Task>(clients);

            for (var i = 0; i < clients; i++)
            {
                var index = i;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                        var body = new StringContent(
                            "{\"user\":\"stress\",\"password\":\"pw\"}",
                            Encoding.UTF8, "application/json");
                        var response = await http.PostAsync($"http://127.0.0.1:{port}/api/ticket", body);
                        var text = await response.Content.ReadAsStringAsync();
                        var ticket = JObject.Parse(text).Value<string>("ticket");
                        if (string.IsNullOrWhiteSpace(ticket))
                            throw new InvalidOperationException($"ticket missing: {response.StatusCode} {text}");

                        using var socket = new ClientWebSocket();
                        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?ticket={ticket}"), CancellationToken.None);
                        Interlocked.Increment(ref connected);

                        var hello = await ReceiveUntilAsync(socket, m => m.Value<string>("type") == "hello", 10_000);
                        var snapshot = await ReceiveUntilAsync(socket, m => m.Value<string>("type") == "snapshot", 20_000);
                        if (hello == null || snapshot == null)
                            throw new InvalidOperationException("hello/snapshot missing");

                        Interlocked.Increment(ref snapshots);
                        Interlocked.Add(ref snapshotBytes, snapshot.ToString(Newtonsoft.Json.Formatting.None).Length);

                        // 命令：背靠背发（最坏突发），每条等自己的 ack
                        //
                        // pageId/pageVersion 取**发命令这一刻**的当前页，而不是开跑前抓的那一份：
                        // 切页会让版本闸失败（E_STALE_PAGE）。ScadaRuntime 不是线程安全的，
                        // 所以必须投到 UI 线程读，不能在客户端线程上直接读字段。
                        for (var c = 0; c < commands; c++)
                        {
                            var id = $"s{index}-{c}";
                            var page = ui!.Invoke(() => runtime.CurrentPage) ?? buttonPage;
                            var json = "{\"type\":\"cmd.element\",\"id\":\"" + id
                                + "\",\"elementId\":\"" + button.ElementId.ToString("D")
                                + "\",\"event\":\"Pressed\",\"pageId\":\"" + page.PageId.ToString("D")
                                + "\",\"pageVersion\":" + page.Version
                                + ",\"runGeneration\":" + hub.RunGeneration + "}";

                            var started = Stopwatch.GetTimestamp();
                            await SendAsync(socket, json);
                            var reply = await ReceiveUntilAsync(
                                socket,
                                m => m.Value<string>("id") == id || m.Value<string>("requestId") == id,
                                15_000);

                            if (reply == null)
                                throw new TimeoutException($"ack timeout {id}");

                            if (reply.Value<bool?>("ok") == true)
                                Interlocked.Increment(ref acks);
                            else
                                errors.Add($"{id}:{reply.Value<string>("code")}");

                            latencies.Add((long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                        }

                        Interlocked.Increment(ref commandsDone);

                        // hold 窗口：继续收，验证切页的结构重建与值帧
                        var readDeadline = DateTime.UtcNow.AddMilliseconds(holdMs);
                        while (DateTime.UtcNow < readDeadline)
                        {
                            var remaining = (int)Math.Max(1, (readDeadline - DateTime.UtcNow).TotalMilliseconds);
                            var messageText = await ReceiveTextAsync(socket, remaining);
                            if (messageText == null) break;

                            var message = JObject.Parse(messageText);
                            switch (message.Value<string>("type"))
                            {
                                case "page":
                                    Interlocked.Increment(ref pageMessages);
                                    break;
                                case "snapshot":
                                    Interlocked.Increment(ref snapshots);
                                    Interlocked.Add(ref snapshotBytes, messageText.Length);
                                    break;
                                case "frame":
                                    Interlocked.Increment(ref frameMessages);
                                    break;
                            }
                        }

                        try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
                        catch { }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{index}:{ex.GetType().Name}:{ex.Message}");
                    }
                    finally
                    {
                        Interlocked.Increment(ref finished);
                    }
                }));
            }

            // 切页：等所有客户端发完命令，再按 pageMs 轮播（镜像模式下的唯一切页路径）
            var switcher = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (Volatile.Read(ref commandsDone) < clients && DateTime.UtcNow < deadline)
                    await Task.Delay(50);

                // 从按钮页之后开始轮播：第 0 轮就是按钮页会让"命令发到一半被切走"，
                // 那测的就不是命令延迟而是 E_STALE_PAGE。这里从 1 起跳，绕一圈回到按钮页时
                // 客户端早已发完（switcher 本来就等 commandsDone）。
                for (var round = 1; round <= pages.Count; round++)
                {
                    await Task.Delay(Math.Max(200, pageMs));
                    var target = pages[round % pages.Count];
                    ui!.Invoke(() => runtime.Navigate(target));
                }
            });

            await Task.WhenAll(tasks.Append(switcher));
            total.Stop();

            var ordered = latencies.OrderBy(x => x).ToArray();
            long P(double p) => ordered.Length == 0 ? 0 : ordered[Math.Min(ordered.Length - 1, (int)(ordered.Length * p))];

            Console.WriteLine($"RESULT clients={clients} connected={connected} snapshots={snapshots} "
                + $"pageMessages={pageMessages} frameMessages={frameMessages} "
                + $"acks={acks} errors={errors.Count} audit={audit.Count} dispatcher={dispatcher.Count}");
            Console.WriteLine($"SNAPSHOT totalKB={snapshotBytes / 1024} avgKB={(clients == 0 ? 0 : snapshotBytes / 1024 / clients)}");
            Console.WriteLine($"LATENCY p50={P(0.50)}ms p95={P(0.95)}ms max={(ordered.Length == 0 ? 0 : ordered[^1])}ms "
                + $"wall={total.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine($"MEMORY workingSet={Environment.WorkingSet / 1024 / 1024}MB gc={GC.GetTotalMemory(false) / 1024 / 1024}MB");

            foreach (var error in errors.Take(10))
                Console.WriteLine("ERR " + error);

            plantTimer.Stop();
            alarmTimer.Stop();
            alarmEngine.Detach();

            await host.StopAsync();
            ui.InvokeShutdown();

            return errors.Count == 0 && acks == clients * commands
                   && (pages.Count <= 1 || pageMessages > 0)
                ? 0 : 1;
        }

        private static int ReadArg(string[] args, string name, int fallback)
        {
            for (var i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(args[i + 1], out var value) && value > 0)
                    return value;
            return fallback;
        }

        private static int FreePort()
        {
            using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static async Task SendAsync(ClientWebSocket socket, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private static async Task<JObject?> ReceiveUntilAsync(ClientWebSocket socket, Func<JObject, bool> match, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var buffer = new byte[65536];

            try
            {
                while (true)
                {
                    using var stream = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                            return null;
                        stream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    var json = JObject.Parse(Encoding.UTF8.GetString(stream.ToArray()));
                    if (match(json))
                        return json;
                }
            }
            catch (OperationCanceledException) { return null; }
            catch (WebSocketException) { return null; }
        }

        private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var buffer = new byte[65536];
            using var stream = new MemoryStream();

            try
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return null;

                    stream.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage) break;
                }
            }
            catch (OperationCanceledException) { return null; }
            catch (WebSocketException) { return null; }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private sealed class StressAuthenticator : IScadaUserAuthenticator
        {
            // 压测账号拿全部内置 Key：要让每个图元都可点，靠的是 Key 集合（B3），不是角色。
            private static readonly string[] AllPerms = ScadaPermissionKeys.BuiltInKeys.ToArray();

            public bool TryValidate(string? userName, string? password, out ScadaUserInfo? user, out string? error)
            {
                user = null;
                error = null;

                if (!string.Equals(userName?.Trim(), "stress", StringComparison.OrdinalIgnoreCase)
                    || password != "pw")
                {
                    return false;
                }

                user = new ScadaUserInfo("stress", "G-stress", AllPerms, "压测组");
                return true;
            }

            public bool IsUsingDefaultPassword(string? userName) => false;
        }

        private sealed class CountingAudit : IScadaAuditSink
        {
            private int _count;

            public int Count => Volatile.Read(ref _count);

            public bool Append(string actor, string sessionId, string clientAddress, string subject,
                string eventText, string action, string result, string detail)
            {
                Interlocked.Increment(ref _count);
                return true;
            }
        }

        private sealed class NullLog : ILogService
        {
            public void Success(params string[] messages) { }
            public void Error(params Exception[] messages) { }
            public void Error(params string[] messages) { }
            public void Info(params string[] messages) { }
            public void Warn(params string[] messages) { }
        }
    }
}
