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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Core.Interfaces;
using Newtonsoft.Json.Linq;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.ScadaWeb;

namespace ScadaWebStress
{
    /// <summary>
    /// M3 暴力测试夹具：真实 ScadaWebHost + 1000 图元 + N 个真实 WS 客户端。
    /// 只测协议层吞吐/延迟/错误，不渲染浏览器。
    /// </summary>
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            var clients = ReadArg(args, "--clients", 20);
            var commandsPerClient = ReadArg(args, "--commands", 20);
            var pageCount = ReadArg(args, "--pages", 4);
            var elementsPerPage = ReadArg(args, "--elements", 300);
            var bindingsPerElement = ReadArg(args, "--bindings", 2);
            var holdMs = ReadArg(args, "--holdMs", 3000);

            var port = FreePort();
            var document = new ScadaDocument();

            // 100 个变量，文本/数值混合；每个图元挂 1~N 条绑定。
            var variables = new List<FakeHandle>(100);
            for (var i = 0; i < 100; i++)
            {
                var isText = i % 3 == 0;
                variables.Add(new FakeHandle(
                    Guid.NewGuid(),
                    "V" + i,
                    isText ? typeof(string) : typeof(double),
                    isText ? "值" + i : (object)(double)i));
            }

            var values = new FakeValueSource(variables);
            var elementTypes = new[]
            {
                "Hmi.Button", "Hmi.TextBlock", "Hmi.TextField", "Hmi.Tank",
                "Hmi.ProgressBar", "Hmi.Lamp", "Hmi.ComboBox", "Hmi.SelectorSwitch",
                "Hmi.AckButton", "Hmi.AlarmBanner", "Hmi.AxisPanel", "Hmi.DataGrid"
            };

            var pages = new List<ScadaPage>(pageCount);
            ScadaElement? button = null;

            for (var p = 0; p < pageCount; p++)
            {
                var page = document.AddPage($"复杂页 {p + 1}");
                pages.Add(page);

                for (var i = 0; i < elementsPerPage; i++)
                {
                    var type = elementTypes[i % elementTypes.Length];
                    var element = new ScadaElement
                    {
                        TypeKey = type,
                        Name = $"{type.Split('.')[1]}-{p + 1}-{i + 1}",
                        X = 10 + (i % 20) * 70,
                        Y = 10 + (i / 20) * 44,
                        Width = 64,
                        Height = 32,
                        ZIndex = i + 1
                    };

                    for (var b = 0; b < bindingsPerElement; b++)
                    {
                        var handle = variables[(p * elementsPerPage + i + b) % variables.Count];
                        element.Bindings.Add(new ScadaBinding
                        {
                            TargetProperty = TargetPropertyOf(type, b),
                            VariableId = handle.VariableId,
                            VariableName = handle.Name,
                            IsEnabled = true
                        });
                    }

                    page.Elements.Add(element);

                    if (p == 0 && i == 0)
                    {
                        button = element;
                        var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                        hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "complex-stress" });
                        element.EventHooks.Add(hook);
                    }
                }
            }

            document.SetStartupPage(pages[0]);
            button ??= pages[0].Elements[0];

            // 运行态只在专用 STA/Dispatcher 线程上启动/换页；帧循环与命令泵都投到这个线程。
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
                ClickPolicy = ScadaWebClickPolicy.AllOperators,
                RequireConfirmForDangerous = true,
                OperationLeaseEnabled = false,
                MaxSessions = Math.Max(64, clients + 8),
                TicketRatePerMinute = 10_000,
                CommandRatePerSecond = 10_000,
                ElementRatePerSecond = 10_000
            };

            var log = new NullLog();
            var audit = new CountingAudit();
            var dispatcher = new CountingDispatcher();
            var host = new ScadaWebHost(
                hub,
                new FakeAuth(),
                catalog: null,
                log,
                settings,
                values: values,
                uiDispatcher: ui,
                actionDispatcher: dispatcher,
                valueWriter: new FakeWriter(),
                alarmCommands: null,
                audit: audit,
                alarmFeed: null);

            host.Start();
            if (!host.IsListening)
            {
                Console.WriteLine($"START_FAILED port={port} err={host.LastError}");
                return 2;
            }

            Console.WriteLine($"stress start port={port} clients={clients} commandsPerClient={commandsPerClient} "
                + $"pages={pageCount} elementsPerPage={elementsPerPage} bindingsPerElement={bindingsPerElement} "
                + $"totalElements={pageCount * elementsPerPage}");
            var latencies = new ConcurrentBag<long>();
            var errors = new ConcurrentBag<string>();
            var snapshots = 0;
            var acks = 0;
            var connected = 0;
            var finished = 0;
            var commandsDone = 0;
            var pageMessages = 0;
            var frameMessages = 0;
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
                        var snapshot = await ReceiveUntilAsync(socket, m => m.Value<string>("type") == "snapshot", 15_000);
                        if (hello == null || snapshot == null)
                            throw new InvalidOperationException("hello/snapshot missing");
                        Interlocked.Increment(ref snapshots);
                        Interlocked.Add(ref snapshotBytes, snapshot.ToString(Newtonsoft.Json.Formatting.None).Length);

                        for (var c = 0; c < commandsPerClient; c++)
                        {
                            var id = $"s{index}-{c}";
                            var json = "{\"type\":\"cmd.element\",\"id\":\"" + id
                                + "\",\"elementId\":\"" + button.ElementId.ToString("D")
                                + "\",\"event\":\"Pressed\",\"pageId\":\"" + pages[0].PageId.ToString("D")
                                + "\",\"pageVersion\":" + pages[0].Version
                                + ",\"runGeneration\":" + hub.RunGeneration + "}";

                            var started = Stopwatch.GetTimestamp();
                            await SendAsync(socket, json);
                            var reply = await ReceiveUntilAsync(
                                socket,
                                m => m.Value<string>("id") == id || m.Value<string>("requestId") == id,
                                15_000);

                            if (reply == null)
                                throw new TimeoutException($"ack timeout {id}");

                            var ok = reply.Value<bool?>("ok") == true;
                            if (!ok)
                                errors.Add($"{id}:{reply.Value<string>("code")}");
                            else
                                Interlocked.Increment(ref acks);

                            latencies.Add((long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                        }

                        Interlocked.Increment(ref commandsDone);

                        // 命令发完后继续读 holdMs：主线程会在这段时间切页，客户端应收到 page/snapshot。
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

            // 等所有客户端发完命令，再切页；客户端此时在 hold 读循环里收 page/snapshot。
            var switcher = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (Volatile.Read(ref commandsDone) < clients && DateTime.UtcNow < deadline)
                    await Task.Delay(50);

                for (var p = 1; p < pages.Count; p++)
                {
                    await Task.Delay(400);
                    ui!.Invoke(() => runtime.Navigate(pages[p]));
                }
            });

            await Task.WhenAll(tasks.Append(switcher));
            total.Stop();

            var ordered = latencies.OrderBy(x => x).ToArray();
            long P(double p) => ordered.Length == 0 ? 0 : ordered[Math.Min(ordered.Length - 1, (int)(ordered.Length * p))];

            Console.WriteLine($"RESULT clients={clients} connected={connected} snapshots={snapshots} "
                + $"pageMessages={pageMessages} frameMessages={frameMessages} "
                + $"acks={acks} errors={errors.Count} audit={audit.Count} dispatcher={dispatcher.Count}");
            Console.WriteLine($"SNAPSHOT totalKB={snapshotBytes / 1024} avgKB={(clients == 0 ? 0 : snapshotBytes / 1024 / clients)} "
                + $"elementsPerPage={elementsPerPage} bindingsPerElement={bindingsPerElement} pages={pageCount}");
            Console.WriteLine($"LATENCY p50={P(0.50)}ms p95={P(0.95)}ms max={(ordered.Length == 0 ? 0 : ordered[^1])}ms "
                + $"wall={total.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine($"MEMORY workingSet={Environment.WorkingSet / 1024 / 1024}MB gc={GC.GetTotalMemory(false) / 1024 / 1024}MB");

            foreach (var error in errors.Take(10))
                Console.WriteLine("ERR " + error);

            await host.StopAsync();
            ui.InvokeShutdown();
            return errors.Count == 0 && acks == clients * commandsPerClient
                   && (pageCount <= 1 || pageMessages > 0)
                ? 0 : 1;
        }

        private static string TargetPropertyOf(string type, int bindingIndex)
        {
            if (bindingIndex > 0)
                return bindingIndex == 1 ? "Fill" : "Visible";

            return type switch
            {
                "Hmi.Tank" => "Level",
                "Hmi.ProgressBar" => "Value",
                "Hmi.Lamp" => "Fill",
                "Hmi.ComboBox" => "SelectedIndex",
                "Hmi.SelectorSwitch" => "SelectedIndex",
                "Hmi.AckButton" => "UnacknowledgedCount",
                _ => "Text"
            };
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
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static async Task SendAsync(ClientWebSocket socket, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private static async Task<JObject?> ReceiveUntilAsync(
            ClientWebSocket socket,
            Func<JObject, bool> match,
            int timeoutMs)
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
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (WebSocketException)
            {
                return null;
            }
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
            public object? Value => _value;

            public event EventHandler? ValueChanged;

            public bool TryWrite(object? value, out string? error)
            {
                _value = value;
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
            public bool TryValidate(string? userName, string? password, out ScadaRole role, out string? error)
            {
                role = ScadaRole.Operator;
                error = null;
                return string.Equals(userName?.Trim(), "stress", StringComparison.OrdinalIgnoreCase)
                       && password == "pw";
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
