using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Core.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.ScadaWeb;

namespace ScadaWebStress
{
    /// <summary>
    /// SCADA Web 协议层暴力压测夹具（2026-10-22 A′ 后重写）。
    ///
    /// <b>它测什么 / 不测什么</b>
    /// ---------
    ///   · 测：真实 ScadaWebHost + N 条真实 WS 连接下的出帧字节、帧率、delta/keyframe 分布、
    ///     ack 延迟、首屏耗时、快照字节、容量拒绝、慢消费者与 64KB 入站边界；
    ///   · 不测：浏览器 DOM/布局/绘制、真实网卡/交换机/WiFi、桌面 + Web 三端一致。
    ///     报告必须把这两类分开写，不能拿回环数字冒充现场带宽。
    ///
    /// <b>与旧版的关键差异</b>
    /// ---------
    ///   · 值真的会变（--changePct/--changeHz），否则 A′ 的增量帧路径根本不会执行；
    ///   · MaxSessions 用生产值（默认 16），不再 Math.Max(64, clients+8) 把预算夹成 11.6KB；
    ///   · 字节一律按收到的 UTF-8 原始负载计数（不用字符数）；
    ///   · 单收循环 + pending ack 表，帧与 ack 不再互相吞；
    ///   · 输出 raw.jsonl / stats / summary.json，报告可从 raw 复算。
    /// </summary>
    internal static class Program
    {
        private static readonly object RawGate = new();
        private static StreamWriter? _raw;
        private static long _startTicks;
        private static long NowMs => (long)Stopwatch.GetElapsedTime(_startTicks).TotalMilliseconds;

        private static readonly List<FrameStat> Frames = new();
        private static readonly List<long> AckLatencies = new();
        private static readonly List<long> FirstScreenMs = new();
        private static readonly ConcurrentDictionary<int, long> BytesPerSecond = new();
        private static readonly ConcurrentBag<string> Errors = new();
        private static long _totalBytes;
        private static long _snapshotBytes;
        private static long _ackCount;
        private static long _frameCount;
        private static long _keyframeCount;
        private static long _deltaCount;
        private static long _itemCount;
        private static long _connected;
        private static long _snapshotCount;

        private static async Task<int> Main(string[] args)
        {
            _startTicks = Stopwatch.GetTimestamp();

            var clients = ArgInt(args, "--clients", 16, 1);
            var elements = ArgInt(args, "--elements", 3000, 1);
            var variables = ArgInt(args, "--variables", 1000, 1);
            var boundElements = Math.Min(elements, ArgInt(args, "--boundElements", Math.Min(1000, elements), 0));
            var changePct = ArgInt(args, "--changePct", 10, 0);
            var changeHz = ArgInt(args, "--changeHz", 10, 1);
            var holdMs = ArgInt(args, "--holdMs", 60_000, 1000);
            var commands = ArgInt(args, "--commands", 5, 0);
            var maxSessions = ArgInt(args, "--maxSessions", 16, 1);
            var slowClientMs = ArgInt(args, "--slowClientMs", 0, 0);
            var capacityProbe = args.Any(a => string.Equals(a, "--capacityProbe", StringComparison.OrdinalIgnoreCase));
            var outDir = ArgString(args, "--outDir", null) ?? DefaultOutDir();

            Directory.CreateDirectory(outDir);
            _raw = new StreamWriter(Path.Combine(outDir, "raw.jsonl"), append: false, Encoding.UTF8)
            {
                AutoFlush = true
            };

            var port = FreePort();
            var document = new ScadaDocument();
            var page = document.AddPage("压测页");
            page.Version = 1;

            var handles = new List<FakeHandle>(variables);
            for (var i = 0; i < variables; i++)
                handles.Add(new FakeHandle(Guid.NewGuid(), "V" + i, typeof(double), (double)i));

            var values = new FakeValueSource(handles);

            for (var i = 0; i < elements; i++)
            {
                var bound = i < boundElements;
                var element = new ScadaElement
                {
                    TypeKey = bound ? "Hmi.ProgressBar" : "Hmi.Text",
                    Name = (bound ? "bar-" : "txt-") + i,
                    X = 8 + (i % 30) * 62,
                    Y = 8 + (i / 30) * 36,
                    Width = 60,
                    Height = 30,
                    ZIndex = i + 1
                };

                if (bound)
                {
                    var handle = handles[i % handles.Count];
                    element.Bindings.Add(new ScadaBinding
                    {
                        TargetProperty = "Value",
                        VariableId = handle.VariableId,
                        VariableName = handle.Name,
                        IsEnabled = true
                    });
                }

                page.Elements.Add(element);
            }

            // 命令靶：一个带 Pressed→Log 钩子的按钮。cmd.element 要求"该事件配了动作"，
            // 拿没有钩子的进度条当靶会回 E_NOT_OPERABLE（压测就成了测拒绝路径）。
            var commandTarget = new ScadaElement
            {
                TypeKey = "Hmi.Button",
                Name = "stress-button",
                X = 8,
                Y = 8,
                Width = 80,
                Height = 30,
                ZIndex = elements + 1
            };
            var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
            hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "stress" });
            commandTarget.EventHooks.Add(hook);
            page.Elements.Add(commandTarget);

            document.SetStartupPage(page);

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
                Name = "ScadaWebStress.UI"
            };
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            uiReady.Wait();

            var runtime = new ScadaRuntime(document);
            ui!.Invoke(() => runtime.Start());

            var hub = new FakeHub(runtime, document, 1);
            var settings = new ScadaWebSettings
            {
                Enabled = true,
                Port = port,
                BindAddress = "127.0.0.1",
                AllowAnonymousView = false,
                AllowCommands = true,
                // B3：判权按权限 Key，不再有 ClickPolicy；压测要"谁都能点"，让 FakeAuth 给全 Key 即可。
                RequireConfirmForDangerous = false,
                OperationLeaseEnabled = false,
                MaxSessions = maxSessions,
                TicketRatePerMinute = 10_000,
                CommandRatePerSecond = 10_000,
                ElementRatePerSecond = 10_000
            };

            var host = new ScadaWebHost(
                hub,
                new FakeAuth(),
                catalog: null,
                new NullLog(),
                settings,
                values: values,
                uiDispatcher: ui,
                actionDispatcher: new CountingDispatcher(),
                valueWriter: new FakeWriter(),
                alarmCommands: null,
                audit: new CountingAudit(),
                alarmFeed: null);

            host.Start();
            if (!host.IsListening)
            {
                Console.WriteLine($"START_FAILED port={port} err={host.LastError}");
                return 2;
            }

            var effectiveBudget = host.EffectiveFrameBudgetBytes;
            var effectiveSnapshot = host.EffectiveSnapshotMaxBytes;

            Console.WriteLine($"stress start port={port} clients={clients} elements={elements} variables={variables} "
                + $"boundElements={boundElements} changePct={changePct} changeHz={changeHz} holdMs={holdMs} "
                + $"maxSessions={maxSessions} budget={effectiveBudget}B snapshotLimit={effectiveSnapshot}B");

            // 值变化循环：changePct% 的变量每拍轮转写入一个新值。
            using var mutationCts = new CancellationTokenSource();
            var mutation = Task.Run(async () =>
            {
                if (changePct <= 0) return;

                var count = Math.Max(1, variables * changePct / 100);
                var interval = Math.Max(10, 1000 / changeHz);
                var cursor = 0;
                var tick = 0;

                try
                {
                    while (!mutationCts.IsCancellationRequested)
                    {
                        await Task.Delay(interval, mutationCts.Token).ConfigureAwait(false);

                        for (var n = 0; n < count; n++)
                        {
                            var handle = handles[(cursor + n) % handles.Count];
                            handle.TryWrite((double)((tick * 37 + n) % 1000), out _);
                        }

                        cursor = (cursor + count) % handles.Count;
                        tick++;
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });

            var serve = args.Any(a => string.Equals(a, "--serve", StringComparison.OrdinalIgnoreCase));
            var serveSeconds = ArgInt(args, "--serveSeconds", 300, 1);
            var reconnectRounds = ArgInt(args, "--reconnectStorm", 0, 0);

            if (serve)
            {
                // 浏览器级验证入口：起真实宿主、打印地址后保持运行（不做压测客户端）。
                // 目录为 null → /api/catalog 回空目录，客户端沿用渲染器字面默认值（不影响登录/渲染）。
                Console.WriteLine($"SERVE http://127.0.0.1:{port}/  登录 stress / pw  （{serveSeconds}s 后自动退出）");
                Console.Out.Flush();

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(serveSeconds)).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }

                mutationCts.Cancel();
                try { await mutation.ConfigureAwait(false); } catch { }

                await host.StopAsync().ConfigureAwait(false);
                ui.InvokeShutdown();
                _raw?.Dispose();
                _raw = null;
                Console.WriteLine("SERVE_STOPPED");
                return 0;
            }

            if (reconnectRounds > 0)
            {
                // 重连风暴：反复"取票 → 握手 → 收 hello/snapshot → 正常关闭"，
                // 专门验证 24 槽准入在持续 churn 下不漏名额（漏一个，后面就会 401/1013）。
                var storm = await RunReconnectStormAsync(port, reconnectRounds, clients).ConfigureAwait(false);
                WriteJson(Path.Combine(outDir, "reconnect-storm.json"), storm);
                Console.WriteLine(JsonConvert.SerializeObject(storm, Formatting.Indented));

                mutationCts.Cancel();
                try { await mutation.ConfigureAwait(false); } catch { }
                await host.StopAsync().ConfigureAwait(false);
                ui.InvokeShutdown();
                _raw?.Dispose();
                _raw = null;
                return storm.Value<bool>("pass") ? 0 : 1;
            }

            var tasks = new List<Task>(clients);
            for (var i = 0; i < clients; i++)
            {
                var index = i;
                tasks.Add(Task.Run(() => RunClientAsync(
                    index, port, page, commandTarget, hub, holdMs, commands,
                    slowClientMs > 0 && index == 0 ? slowClientMs : 0)));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            mutationCts.Cancel();
            try { await mutation.ConfigureAwait(false); } catch { }

            var capacity = capacityProbe
                ? await ProbeCapacityAsync(port, maxSessions).ConfigureAwait(false)
                : null;

            var abnormal = args.Any(a => string.Equals(a, "--abnormalProbe", StringComparison.OrdinalIgnoreCase))
                ? await ProbeAbnormalAsync(port).ConfigureAwait(false)
                : null;

            await host.StopAsync().ConfigureAwait(false);
            ui.InvokeShutdown();

            var summary = BuildSummary(
                args, outDir, clients, elements, variables, boundElements,
                changePct, changeHz, holdMs, commands, maxSessions, effectiveBudget, effectiveSnapshot,
                capacity, abnormal);

            WriteJson(Path.Combine(outDir, "summary.json"), summary);
            WriteMeta(Path.Combine(outDir, "meta.json"), args, effectiveBudget, effectiveSnapshot, maxSessions);

            _raw?.Dispose();
            _raw = null;

            Console.WriteLine(JsonConvert.SerializeObject(summary, Formatting.Indented));
            Console.WriteLine($"EVIDENCE {outDir}");

            var pass = summary.Value<bool>("pass");
            return pass ? 0 : 1;
        }

        private static async Task<JObject> RunReconnectStormAsync(int port, int rounds, int clients)
        {
            var attempts = 0;
            var ok = 0;
            var failures = new ConcurrentBag<string>();
            var sw = Stopwatch.StartNew();

            for (var round = 0; round < rounds; round++)
            {
                var tasks = new List<Task>(clients);

                for (var i = 0; i < clients; i++)
                {
                    var index = i;
                    tasks.Add(Task.Run(async () =>
                    {
                        Interlocked.Increment(ref attempts);

                        try
                        {
                            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                            var ticket = await IssueTicketAsync(http, port).ConfigureAwait(false);
                            if (string.IsNullOrWhiteSpace(ticket))
                                throw new InvalidOperationException("ticket missing");

                            using var socket = new ClientWebSocket();
                            await socket.ConnectAsync(
                                new Uri($"ws://127.0.0.1:{port}/ws?ticket={ticket}"),
                                CancellationToken.None).ConfigureAwait(false);

                            var (hello, _) = await ReceiveUntilWithBytesAsync(
                                socket, m => m.Value<string>("type") == "hello", 5000).ConfigureAwait(false);
                            var (initial, _) = await ReceiveUntilWithBytesAsync(
                                socket,
                                m => m.Value<string>("type") == "snapshot" || m.Value<string>("type") == "idle",
                                10000).ConfigureAwait(false);

                            if (hello == null || initial == null)
                                throw new InvalidOperationException("hello/snapshot missing");

                            await socket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure, "storm", CancellationToken.None)
                                .ConfigureAwait(false);

                            Interlocked.Increment(ref ok);
                        }
                        catch (Exception ex)
                        {
                            failures.Add($"r{round}c{index}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            sw.Stop();

            return new JObject
            {
                ["rounds"] = rounds,
                ["clients"] = clients,
                ["attempts"] = attempts,
                ["ok"] = ok,
                ["failed"] = attempts - ok,
                ["elapsedMs"] = sw.ElapsedMilliseconds,
                ["pass"] = attempts == ok,
                ["errorSamples"] = new JArray(failures.Take(10).ToArray())
            };
        }

        private static async Task RunClientAsync(
            int index,
            int port,
            ScadaPage page,
            ScadaElement commandTarget,
            FakeHub hub,
            int holdMs,
            int commands,
            int slowClientMs)
        {
            try
            {
                var connectStarted = NowMs;
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                var ticket = await IssueTicketAsync(http, port).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(ticket))
                    throw new InvalidOperationException("ticket missing");

                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?ticket={ticket}"), CancellationToken.None)
                    .ConfigureAwait(false);
                Interlocked.Increment(ref _connected);

                var (hello, _) = await ReceiveUntilWithBytesAsync(
                    socket, m => m.Value<string>("type") == "hello", 10_000).ConfigureAwait(false);
                var (snapshot, snapshotBytes) = await ReceiveUntilWithBytesAsync(
                    socket, m => m.Value<string>("type") == "snapshot", 20_000).ConfigureAwait(false);
                if (hello == null || snapshot == null)
                    throw new InvalidOperationException("hello/snapshot missing");

                Interlocked.Increment(ref _snapshotCount);
                Interlocked.Add(ref _snapshotBytes, snapshotBytes);
                lock (FirstScreenMs)
                    FirstScreenMs.Add(NowMs - connectStarted);

                // 慢消费者：读完首屏后故意不读，让服务端出站队列积压。
                if (slowClientMs > 0)
                    await Task.Delay(slowClientMs).ConfigureAwait(false);

                using var receiveCts = new CancellationTokenSource();
                var pending = new ConcurrentDictionary<string, TaskCompletionSource<JObject>>(StringComparer.Ordinal);
                var sendAt = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
                var receiver = Task.Run(() => ReceiveLoopAsync(index, socket, pending, sendAt, receiveCts.Token));

                for (var c = 0; c < commands; c++)
                {
                    var id = $"s{index}-{c}";
                    var json = "{\"type\":\"cmd.element\",\"id\":\"" + id
                        + "\",\"elementId\":\"" + commandTarget.ElementId.ToString("D")
                        + "\",\"event\":\"Pressed\",\"pageId\":\"" + page.PageId.ToString("D")
                        + "\",\"pageVersion\":" + page.Version
                        + ",\"runGeneration\":" + hub.RunGeneration + "}";

                    var tcs = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
                    pending[id] = tcs;
                    sendAt[id] = NowMs;

                    await SendAsync(socket, json).ConfigureAwait(false);

                    var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000)).ConfigureAwait(false);
                    if (completed != tcs.Task)
                    {
                        pending.TryRemove(id, out _);
                        Errors.Add($"{index}:{id}:ack-timeout");
                    }
                    else
                    {
                        var reply = await tcs.Task.ConfigureAwait(false);
                        var latency = NowMs - sendAt[id];
                        lock (AckLatencies) AckLatencies.Add(latency);
                        if (reply.Value<bool?>("ok") != true)
                            Errors.Add($"{index}:{id}:{reply.Value<string>("code")}");
                        else
                            Interlocked.Increment(ref _ackCount);
                    }
                }

                // 继续读 holdMs（值帧/结构帧都在这个窗口里）。
                var deadline = NowMs + holdMs;
                while (NowMs < deadline)
                    await Task.Delay(100).ConfigureAwait(false);

                receiveCts.Cancel();
                try { await receiver.ConfigureAwait(false); } catch { }

                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                Errors.Add($"{index}:{ex.GetType().Name}:{ex.Message}");
            }
        }

        private static async Task ReceiveLoopAsync(
            int client,
            ClientWebSocket socket,
            ConcurrentDictionary<string, TaskCompletionSource<JObject>> pending,
            ConcurrentDictionary<string, long> sendAt,
            CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var (text, bytes) = await ReceiveTextWithBytesAsync(socket, 5000, token).ConfigureAwait(false);
                if (text == null)
                {
                    if (token.IsCancellationRequested) return;
                    continue;
                }

                Interlocked.Add(ref _totalBytes, bytes);
                var second = (int)(NowMs / 1000);
                BytesPerSecond.AddOrUpdate(second, bytes, (_, current) => current + bytes);

                JObject message;
                try
                {
                    message = JObject.Parse(text);
                }
                catch
                {
                    Errors.Add($"{client}:bad-json");
                    continue;
                }

                var type = message.Value<string>("type") ?? string.Empty;

                switch (type)
                {
                    case "frame":
                    {
                        var keyframe = message.Value<bool?>("keyframe") == true;
                        var items = (message["items"] as JArray)?.Count ?? 0;
                        Interlocked.Increment(ref _frameCount);
                        Interlocked.Add(ref _itemCount, items);
                        if (keyframe) Interlocked.Increment(ref _keyframeCount);
                        else Interlocked.Increment(ref _deltaCount);

                        lock (Frames)
                            Frames.Add(new FrameStat(client, NowMs, bytes, keyframe, items));
                        break;
                    }

                    case "snapshot":
                    case "page":
                        Interlocked.Add(ref _snapshotBytes, bytes);
                        break;

                    case "ack":
                    case "error":
                    {
                        var id = message.Value<string>("id") ?? message.Value<string>("requestId");
                        if (id != null && pending.TryRemove(id, out var tcs))
                            tcs.TrySetResult(message);
                        break;
                    }

                    case "bye":
                        Errors.Add($"{client}:bye:{message.Value<string>("reason")}:{message.Value<string>("closeCode")}");
                        break;
                }

                WriteRaw(client, type, bytes, message);
            }
        }

        private static async Task<(string? Text, int Bytes)> ReceiveTextWithBytesAsync(
            ClientWebSocket socket,
            int timeoutMs,
            CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
            linked.CancelAfter(timeoutMs);

            var buffer = new byte[64 * 1024];
            using var stream = new MemoryStream();

            try
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), linked.Token)
                        .ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return (null, 0);

                    stream.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                return (null, 0);
            }
            catch (WebSocketException)
            {
                return (null, 0);
            }

            var bytes = (int)stream.Length;
            return (Encoding.UTF8.GetString(stream.ToArray()), bytes);
        }

        private static void WriteRaw(int client, string type, int bytes, JObject message)
        {
            if (_raw == null) return;

            var line = new JObject
            {
                ["client"] = client,
                ["tMs"] = NowMs,
                ["type"] = type,
                ["bytes"] = bytes
            };

            if (message["seq"] != null) line["seq"] = message["seq"];
            if (message["frameSeq"] != null) line["frameSeq"] = message["frameSeq"];
            if (message["keyframe"] != null) line["keyframe"] = message["keyframe"];
            if (message["items"] is JArray items) line["items"] = items.Count;
            if (message["code"] != null) line["code"] = message["code"];

            lock (RawGate)
                _raw.WriteLine(line.ToString(Formatting.None));
        }

        private static async Task<JObject?> ProbeCapacityAsync(int port, int maxSessions)
        {
            var probes = new List<ClientWebSocket>();
            var result = new JObject { ["requested"] = maxSessions + 1, ["rejected"] = false, ["detail"] = "" };

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

                for (var i = 0; i < maxSessions; i++)
                {
                    var ticket = await IssueTicketAsync(http, port).ConfigureAwait(false);
                    var socket = new ClientWebSocket();
                    await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?ticket={ticket}"), CancellationToken.None)
                        .ConfigureAwait(false);
                    await ReceiveUntilAsync(socket, m => m.Value<string>("type") == "hello", 5000).ConfigureAwait(false);
                    probes.Add(socket);
                }

                var extraTicket = await IssueTicketAsync(http, port).ConfigureAwait(false);
                using var extra = new ClientWebSocket();
                await extra.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?ticket={extraTicket}"), CancellationToken.None)
                    .ConfigureAwait(false);

                var bye = await ReceiveUntilAsync(extra, m => m.Value<string>("type") == "bye", 5000)
                    .ConfigureAwait(false);

                result["bye"] = bye?.ToString(Formatting.None) ?? "<none>";
                result["rejected"] = bye?.Value<int?>("closeCode") == 1013
                                     || extra.CloseStatus == (WebSocketCloseStatus)1013;
                result["detail"] = $"admitted={maxSessions} extraClose={(int?)extra.CloseStatus}";
            }
            catch (Exception ex)
            {
                result["detail"] = $"{ex.GetType().Name}:{ex.Message}";
            }
            finally
            {
                foreach (var socket in probes)
                {
                    try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "probe", CancellationToken.None); }
                    catch { }
                    socket.Dispose();
                }
            }

            return result;
        }

        /// <summary>
        /// 边界/异常探针：坏 JSON、未知 type、超过 64KB 的入站帧，最后 ping 确认连接仍活着。
        /// 三条都必须回明确错误码而不是断连；断连即判失败（对应 ScadaWebHost.HandleTextFrame）。
        /// </summary>
        private static async Task<JObject> ProbeAbnormalAsync(int port)
        {
            var result = new JObject();

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var ticket = await IssueTicketAsync(http, port).ConfigureAwait(false);
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?ticket={ticket}"), CancellationToken.None)
                    .ConfigureAwait(false);
                await ReceiveUntilAsync(socket, m => m.Value<string>("type") == "hello", 5000).ConfigureAwait(false);

                await SendAsync(socket, "{not json").ConfigureAwait(false);
                var badJson = await ReceiveUntilAsync(socket, m => m.Value<string>("type") == "error", 3000)
                    .ConfigureAwait(false);
                result["badJson"] = badJson?.Value<string>("code") == "E_BAD_JSON";

                await SendAsync(socket, "{\"type\":\"nope\",\"id\":\"u1\"}").ConfigureAwait(false);
                var unknown = await ReceiveUntilAsync(socket, m => m.Value<string>("type") == "error", 3000)
                    .ConfigureAwait(false);
                result["unknownType"] = unknown?.Value<string>("code") == "E_UNSUPPORTED";

                var big = "{\"type\":\"ping\",\"t\":\"" + new string('x', 66 * 1024) + "\"}";
                await SendAsync(socket, big).ConfigureAwait(false);
                var tooBig = await ReceiveUntilAsync(socket, m => m.Value<string>("type") == "error", 3000)
                    .ConfigureAwait(false);
                result["oversized"] = tooBig?.Value<string>("code") == "E_BAD_REQUEST";

                await SendAsync(socket, "{\"type\":\"ping\",\"t\":\"after\"}").ConfigureAwait(false);
                var pong = await ReceiveUntilAsync(socket, m => m.Value<string>("type") == "pong", 3000)
                    .ConfigureAwait(false);
                result["aliveAfterErrors"] = pong != null;

                result["pass"] = result.Value<bool?>("badJson") == true
                                 && result.Value<bool?>("unknownType") == true
                                 && result.Value<bool?>("oversized") == true
                                 && result.Value<bool?>("aliveAfterErrors") == true;

                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "probe", CancellationToken.None); }
                catch { }
            }
            catch (Exception ex)
            {
                result["pass"] = false;
                result["detail"] = $"{ex.GetType().Name}:{ex.Message}";
            }

            return result;
        }

        private static JObject BuildSummary(
            string[] args,
            string outDir,
            int clients,
            int elements,
            int variables,
            int boundElements,
            int changePct,
            int changeHz,
            int holdMs,
            int commands,
            int maxSessions,
            int effectiveBudget,
            int effectiveSnapshot,
            JObject? capacity,
            JObject? abnormal)
        {
            FrameStat[] frames;
            lock (Frames) frames = Frames.ToArray();

            long[] ack;
            lock (AckLatencies) ack = AckLatencies.OrderBy(x => x).ToArray();

            long[] first;
            lock (FirstScreenMs) first = FirstScreenMs.OrderBy(x => x).ToArray();

            var deltaBytes = frames.Where(f => !f.Keyframe).Select(f => (long)f.Bytes).OrderBy(x => x).ToArray();
            var keyBytes = frames.Where(f => f.Keyframe).Select(f => (long)f.Bytes).OrderBy(x => x).ToArray();

            var durationSec = Math.Max(1.0, holdMs / 1000.0);
            var meanMbps = _totalBytes * 8.0 / durationSec / 1_000_000.0;
            var peak1s = BytesPerSecond.Count == 0
                ? 0
                : BytesPerSecond.Values.Max() * 8.0 / 1_000_000.0;
            var fps = _frameCount / durationSec / Math.Max(1, clients);

            var summary = new JObject
            {
                ["outDir"] = outDir,
                ["clients"] = clients,
                ["elements"] = elements,
                ["variables"] = variables,
                ["boundElements"] = boundElements,
                ["changePct"] = changePct,
                ["changeHz"] = changeHz,
                ["holdMs"] = holdMs,
                ["maxSessions"] = maxSessions,
                ["effectiveFrameBudgetBytes"] = effectiveBudget,
                ["effectiveSnapshotMaxBytes"] = effectiveSnapshot,
                ["connected"] = _connected,
                ["snapshots"] = _snapshotCount,
                ["frames"] = _frameCount,
                ["deltaFrames"] = _deltaCount,
                ["keyframes"] = _keyframeCount,
                ["items"] = _itemCount,
                ["acks"] = _ackCount,
                ["errors"] = Errors.Count,
                ["totalBytes"] = _totalBytes,
                ["snapshotBytes"] = _snapshotBytes,
                ["meanMbps"] = Math.Round(meanMbps, 3),
                ["peak1sMbps"] = Math.Round(peak1s, 3),
                ["fps"] = Math.Round(fps, 3),
                ["deltaBytes"] = new JObject
                {
                    ["p50"] = P(deltaBytes, 0.50),
                    ["p95"] = P(deltaBytes, 0.95),
                    ["p99"] = P(deltaBytes, 0.99),
                    ["max"] = deltaBytes.Length == 0 ? 0 : deltaBytes[^1]
                },
                ["keyframeBytes"] = new JObject
                {
                    ["p50"] = P(keyBytes, 0.50),
                    ["p95"] = P(keyBytes, 0.95),
                    ["max"] = keyBytes.Length == 0 ? 0 : keyBytes[^1]
                },
                ["ackMs"] = new JObject
                {
                    ["p50"] = P(ack, 0.50),
                    ["p95"] = P(ack, 0.95),
                    ["max"] = ack.Length == 0 ? 0 : ack[^1]
                },
                ["firstScreenMs"] = new JObject
                {
                    ["p50"] = P(first, 0.50),
                    ["p95"] = P(first, 0.95),
                    ["max"] = first.Length == 0 ? 0 : first[^1]
                },
                ["capacityProbe"] = capacity ?? new JObject { ["requested"] = 0, ["rejected"] = false },
                ["abnormalProbe"] = abnormal ?? new JObject { ["pass"] = true, ["skipped"] = true }
            };

            var deltaP95 = P(deltaBytes, 0.95);
            var firstP95 = P(first, 0.95);
            var capacityOk = capacity == null || capacity.Value<bool?>("rejected") == true;
            var abnormalOk = abnormal == null || abnormal.Value<bool?>("pass") == true;
            var acksOk = _ackCount == clients * commands;

            var pass = _connected == clients
                       && Errors.Count == 0
                       && acksOk
                       && _frameCount > 0
                       && _deltaCount > 0
                       && deltaP95 <= effectiveBudget + 8
                       && meanMbps <= 70
                       && peak1s <= 100
                       && fps >= 9.5
                       && firstP95 <= 3000
                       && capacityOk
                       && abnormalOk;

            summary["pass"] = pass;
            summary["checks"] = new JObject
            {
                ["connectedAll"] = _connected == clients,
                ["acksAll"] = acksOk,
                ["noErrors"] = Errors.Count == 0,
                ["framesSeen"] = _frameCount > 0,
                ["deltaSeen"] = _deltaCount > 0,
                ["deltaP95WithinBudget"] = deltaP95 <= effectiveBudget + 8,
                ["meanMbpsWithin70"] = meanMbps <= 70,
                ["peak1sWithin100"] = peak1s <= 100,
                ["fpsWithin9_5"] = fps >= 9.5,
                ["firstScreenP95Within3s"] = firstP95 <= 3000,
                ["capacityRejected"] = capacityOk,
                ["abnormalHandled"] = abnormalOk
            };

            if (Errors.Count > 0)
                summary["errorSamples"] = new JArray(Errors.Take(10).ToArray());

            return summary;
        }

        private static void WriteMeta(string path, string[] args, int budget, int snapshotLimit, int maxSessions)
        {
            var meta = new JObject
            {
                ["args"] = new JArray(args),
                ["startedUtc"] = DateTime.UtcNow,
                ["machine"] = Environment.MachineName,
                ["os"] = RuntimeInformation.OSDescription,
                ["framework"] = RuntimeInformation.FrameworkDescription,
                ["processorCount"] = Environment.ProcessorCount,
                ["workingSetMB"] = Environment.WorkingSet / 1024 / 1024,
                ["effectiveFrameBudgetBytes"] = budget,
                ["effectiveSnapshotMaxBytes"] = snapshotLimit,
                ["maxSessions"] = maxSessions,
                ["mode"] = "in-process host + ws clients (protocol layer; no browser)"
            };

            WriteJson(path, meta);
        }

        private static void WriteJson(string path, JObject value)
            => File.WriteAllText(path, value.ToString(Formatting.Indented), new UTF8Encoding(false));

        private static long P(long[] sorted, double p)
        {
            if (sorted.Length == 0) return 0;
            var rank = (int)Math.Ceiling(p * sorted.Length) - 1;
            return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
        }

        private static string DefaultOutDir()
        {
            var root = FindRepoRoot() ?? Directory.GetCurrentDirectory();
            return Path.Combine(root, "docs", "performance", "evidence",
                $"{DateTime.Now:yyyyMMdd-HHmmss}-stress");
        }

        private static string? FindRepoRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "VisionMaster.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            return null;
        }

        private static async Task<string?> IssueTicketAsync(HttpClient http, int port)
        {
            var body = new StringContent("{\"user\":\"stress\",\"password\":\"pw\"}", Encoding.UTF8, "application/json");
            var response = await http.PostAsync($"http://127.0.0.1:{port}/api/ticket", body).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            try { return JObject.Parse(text).Value<string>("ticket"); }
            catch { return null; }
        }

        private static async Task SendAsync(ClientWebSocket socket, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None)
                .ConfigureAwait(false);
        }

        private static async Task<(JObject? Json, int Bytes)> ReceiveUntilWithBytesAsync(
            ClientWebSocket socket,
            Func<JObject, bool> match,
            int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var buffer = new byte[64 * 1024];

            try
            {
                while (true)
                {
                    using var stream = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                            return (null, 0);
                        stream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    var bytes = (int)stream.Length;
                    var json = JObject.Parse(Encoding.UTF8.GetString(stream.ToArray()));
                    if (match(json)) return (json, bytes);
                }
            }
            catch (OperationCanceledException)
            {
                return (null, 0);
            }
            catch (WebSocketException)
            {
                return (null, 0);
            }
        }

        private static async Task<JObject?> ReceiveUntilAsync(
            ClientWebSocket socket,
            Func<JObject, bool> match,
            int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var buffer = new byte[64 * 1024];

            try
            {
                while (true)
                {
                    using var stream = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                            return null;
                        stream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    var json = JObject.Parse(Encoding.UTF8.GetString(stream.ToArray()));
                    if (match(json)) return json;
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
        }

        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static int ArgInt(string[] args, string name, int fallback, int min)
        {
            for (var i = 0; i + 1 < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                if (int.TryParse(args[i + 1], out var value) && value >= min) return value;
            }

            return fallback;
        }

        private static string? ArgString(string[] args, string name, string? fallback)
        {
            for (var i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return fallback;
        }

        private sealed record FrameStat(int Client, long Ms, int Bytes, bool Keyframe, int Items);

        private sealed class FakeValueSource : IScadaValueSource
        {
            private readonly Dictionary<Guid, FakeHandle> _byId = new();
            private readonly Dictionary<string, FakeHandle> _byName = new(StringComparer.Ordinal);

            public FakeValueSource(IEnumerable<FakeHandle> handles)
            {
                foreach (var handle in handles)
                {
                    _byId[handle.VariableId] = handle;
                    _byName[handle.Name] = handle;
                }
            }

            public bool TryResolve(Guid variableId, string? fallbackName, out IScadaValueHandle? handle)
            {
                if (variableId != Guid.Empty && _byId.TryGetValue(variableId, out var byId))
                {
                    handle = byId;
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(fallbackName)
                    && _byName.TryGetValue(fallbackName!, out var byName))
                {
                    handle = byName;
                    return true;
                }

                handle = null;
                return false;
            }
        }

        private sealed class FakeHandle : IScadaValueHandle
        {
            private readonly object _gate = new();
            private object? _value;

            public FakeHandle(Guid variableId, string name, Type dataType, object? value)
            {
                VariableId = variableId;
                Name = name;
                DataType = dataType;
                _value = value;
            }

            public Guid VariableId { get; }
            public string Name { get; }
            public Type DataType { get; }

            public object? Value
            {
                get { lock (_gate) return _value; }
            }

            public event EventHandler? ValueChanged;

            public bool TryWrite(object? value, out string? error)
            {
                lock (_gate) _value = value;
                error = null;
                ValueChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
        }

        private sealed class FakeHub : IScadaRunHub
        {
            private readonly ScadaRuntime _runtime;
            private readonly ScadaDocument _document;

            public FakeHub(ScadaRuntime runtime, ScadaDocument document, long generation)
            {
                _runtime = runtime;
                _document = document;
                RunGeneration = generation;
            }

            public bool IsRunning => _runtime.IsRunning;
            public ScadaRuntime? Session => _runtime;
            public ScadaDocument? Document => _document;
            public long RunGeneration { get; }

            public bool TryGetCurrent(out ScadaRuntime? session, out ScadaDocument? document, out long generation)
            {
                session = _runtime;
                document = _document;
                generation = RunGeneration;
                return _runtime.IsRunning;
            }

            public event Action<ScadaRuntime, ScadaDocument, long>? RunStarted;
            public event Action<long>? RunStopped;
        }

        private sealed class FakeAuth : IScadaUserAuthenticator
        {
            // 压测账号拿全部内置 Key（"谁都能点"靠这个，不靠已删除的 ClickPolicy）。
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

        private sealed class CountingDispatcher : IScadaActionDispatcher
        {
            private int _count;

            public int Count => Volatile.Read(ref _count);

            public void Dispatch(ScadaEventHook? hook, string? sourceName, string? operatorName = null)
                => _ = DispatchWithResult(hook, sourceName, operatorName);

            public ScadaActionDispatchResult DispatchWithResult(
                ScadaEventHook? hook,
                string? sourceName,
                string? operatorName = null)
            {
                Interlocked.Increment(ref _count);
                var total = hook?.Actions.Count ?? 0;
                return new ScadaActionDispatchResult(total, total, 0, 0);
            }
        }

        private sealed class CountingAudit : IScadaAuditSink
        {
            private int _count;

            public int Count => Volatile.Read(ref _count);

            public bool Append(
                string actor,
                string sessionId,
                string clientAddress,
                string subject,
                string eventText,
                string action,
                string result,
                string detail)
            {
                Interlocked.Increment(ref _count);
                return true;
            }
        }

        private sealed class FakeWriter : IScadaValueWriter
        {
            public bool TryWriteText(ScadaElement element, string targetProperty, string? text, out string? error)
            {
                error = null;
                return true;
            }

            public bool TryReadText(ScadaElement element, string targetProperty, out string? text)
            {
                text = "0";
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
