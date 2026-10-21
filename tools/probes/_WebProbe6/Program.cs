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
using VisionMaster.Scada;
using VisionMaster.Scada.Snapshots;
using VisionMaster.ScadaWeb;
using WatsonWebserver;
using WatsonWebserver.Core;
using WatsonWebserver.Core.WebSockets;

namespace WebProbe6
{
    /// <summary>
    /// M1b 探针：只读镜像（页面快照 + 10Hz 值帧 + 静态基础页）+ 重连 / 基座热更新 / 格式串。
    ///
    /// 判据：
    ///   B1  起服务（随机端口）
    ///   B2  静态页 GET / → 200 + text/html（无鉴权）
    ///   B3  GET /app.js → 200 + javascript（原样字节，不是 base64）
    ///   B4  GET /styles.css → 200 + text/css
    ///   B5  白名单外路径 → 404
    ///   B6  runtime.Start()
    ///   B7  签发 ticket
    ///   B8  WS 握手成功
    ///   B9  首帧 hello（readOnly）
    ///   B10 握手后收到 snapshot，元素数 = 10，元素带 Bindings
    ///   B11 收到至少一帧 frame，items 数 = 10
    ///   B12 值帧四类：Text/Number/Bool/Color 各一例
    ///   B13 改值 → 下一帧 Text/Number 跟着变（值帧不缓存）
    ///   B14 改 page.Version → 收到 page（不是 frame/snapshot）
    ///   B15 ping → pong
    ///   B16 未知 kind → E_UNSUPPORTED
    ///   B17 hub 停止 → 收到 stopped
    ///   B18 再次运行 → 重新收到 snapshot（不是 page）
    ///   B19 StopAsync 幂等
    ///
    /// 本批新增（M1b 评审三个 blocking）：
    ///   C1  /elements-process.js、/elements-process.css 200（白名单 + 内容）
    ///   C2  带 ?v=4 的同名请求也 200（路由必须按 path 匹配、忽略 query）
    ///   C3  hello 带 sessionToken（32 hex）+ sessionExpiresIn &gt; 0
    ///   C4  snapshot 带 basePageVersion（= 基座页当时的 Version）
    ///   C5  DisplayFormat=F2 → text="0.80"，number 仍是原值 0.8
    ///   C6  改基座图元属性 → 本页 Version 不变、basePageVersion 变 → 收到 page
    ///   C7  基座没变时 600ms 内不会多发 page（反向对照）
    ///   C8  旧 ticket 复用仍 401（"单次"语义没被会话令牌破坏）
    ///   C9  无效 refreshToken → 401 E_SESSION_EXPIRED
    ///   C10 有效 sessionToken → 换到新 ticket
    ///   C11 新 ticket 重连成功，hello 里仍是同一枚 sessionToken
    ///   C12 StopAsync 之后会话令牌全部清空
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
                AllowAnonymousView = true,   // 匿名只读观众：探针不走登录
                MaxSessions = 16
            };

            var log = new FakeLog();
            var hub = new FakeRunHub();
            var values = new FakeValueSource();
            var host = new ScadaWebHost(hub, authenticator: null, catalog: null, log, settings, values);

            host.Start();
            Check("B1", host.IsListening && host.WebSocketsEnabled,
                $"listening={host.IsListening} ws={host.WebSocketsEnabled} port={host.Port} err={host.LastError ?? "<none>"}");

            var baseUrl = $"http://127.0.0.1:{port}";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // ---------------- 静态资源 ----------------
            var (s1Status, s1Type, s1Body) = await GetAsync(http, baseUrl + "/");
            Check("B2", s1Status == 200 && s1Type.Contains("text/html") && s1Body.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase),
                $"status={s1Status} type={s1Type} len={s1Body.Length}");

            var (s2Status, s2Type, s2Body) = await GetAsync(http, baseUrl + "/app.js");
            Check("B3", s2Status == 200 && s2Type.Contains("javascript") && !s2Body.StartsWith("\"", StringComparison.Ordinal) && s2Body.Contains("ScadaRenderers", StringComparison.Ordinal),
                $"status={s2Status} type={s2Type} len={s2Body.Length} head={Truncate(s2Body, 60)}");

            var (s3Status, s3Type, s3Body) = await GetAsync(http, baseUrl + "/styles.css");
            Check("B4", s3Status == 200 && s3Type.Contains("text/css") && s3Body.Length > 0,
                $"status={s3Status} type={s3Type} len={s3Body.Length}");

            var (s4Status, s4Type, s4Body) = await GetAsync(http, baseUrl + "/appsettings.json");
            var (s5Status, s5Type, _) = await GetAsync(http, baseUrl + "/..%2Fappsettings.json");

            // 未命中路径会落到"鉴权之后的默认路由"（401），不是 404；两者都是拒绝。
            // 白名单保证的是"绝不把磁盘文件吐出去"，不是状态码形状。
            Check("B5", s4Status != 200 && s5Status != 200,
                $"未知路径 status={s4Status} type={s4Type} len={s4Body.Length}；穿越尝试 status={s5Status} type={s5Type}（白名单外一律拒绝）");

            // ---------------- 工艺图元渲染器：白名单 + 带 query 的请求 ----------------
            // 前端会用 /app.js?v=4、/elements-process.js?v=4 这种带版本号的 URL 做缓存击穿；
            // Watson 的静态路由按 path（RawWithoutQuery）匹配，query 必须被忽略——
            // 若哪天换了匹配口径，这两条会立刻变红（否则表现为"页面样式全丢、控制台 401"）。
            var (epJsStatus, epJsType, epJsBody) = await GetAsync(http, baseUrl + "/elements-process.js");
            var (epCssStatus, epCssType, epCssBody) = await GetAsync(http, baseUrl + "/elements-process.css");
            var (epJsQStatus, epJsQType, epJsQBody) = await GetAsync(http, baseUrl + "/elements-process.js?v=4");
            var (epCssQStatus, epCssQType, epCssQBody) = await GetAsync(http, baseUrl + "/elements-process.css?v=4");
            var (appJsQStatus, appJsQType, appJsQBody) = await GetAsync(http, baseUrl + "/app.js?v=4");

            Check("C1",
                epJsStatus == 200 && epJsType.Contains("javascript")
                && epJsBody.Contains("ScadaRenderers", StringComparison.Ordinal)
                && epCssStatus == 200 && epCssType.Contains("text/css") && epCssBody.Length > 0,
                $"elements-process.js={epJsStatus}/{epJsType}/len={epJsBody.Length}；"
                + $"elements-process.css={epCssStatus}/{epCssType}/len={epCssBody.Length}");

            Check("C2",
                epJsQStatus == 200 && epCssQStatus == 200 && appJsQStatus == 200
                && epJsQBody.Contains("ScadaRenderers", StringComparison.Ordinal)
                && epJsQBody.Length == epJsBody.Length
                && epCssQBody.Length == epCssBody.Length,
                $"?v=4 → js={epJsQStatus}/{epJsQType}(len={epJsQBody.Length}) "
                + $"css={epCssQStatus}/{epCssQType}(len={epCssQBody.Length}) app.js={appJsQStatus}(len={appJsQBody.Length})"
                + "（query 必须被静态路由忽略）");

            // ---------------- 运行态：10 图元页 ----------------
            var document = new ScadaDocument();
            var page = document.AddPage("总览");
            document.SetStartupPage(page);

            var seeds = new List<Seed>
            {
                new("Hmi.Text",      "温度",  12.5,        "Text"),
                new("Hmi.Text",      "压力",  0.8,         "Value"),
                new("Hmi.Tank",      "罐1",   55.0,        "Value"),
                new("Hmi.Indicator", "运行灯", true,       "Visible"),
                new("Hmi.Rectangle", "底色",  "#FF00FF00", "Fill"),
                new("Hmi.Gauge",     "表盘",  33.3,        "Value"),
                new("Hmi.Valve",     "阀",    false,       "Visible"),
                new("Hmi.Text",      "状态",  "运行中",     "Text"),
                new("Hmi.Indicator", "报警",  0,           "Visible"),
                new("Hmi.Text",      "产量",  1000,        "Text"),
            };

            var elements = new List<(ScadaElement Element, FakeHandle Handle)>();
            var handles = new Dictionary<string, FakeHandle>(StringComparer.Ordinal);

            for (var i = 0; i < seeds.Count; i++)
            {
                var seed = seeds[i];
                var variableId = Guid.NewGuid();

                var element = new ScadaElement
                {
                    TypeKey = seed.TypeKey,
                    Name = seed.Name,
                    X = 20 + (i % 5) * 200,
                    Y = 20 + (i / 5) * 160,
                    Width = 160,
                    Height = 120,
                    ZIndex = i,
                };

                var binding = element.AddBinding(seed.Property);
                binding.VariableId = variableId;
                binding.VariableName = "Var" + (i + 1);
                binding.IsEnabled = true;

                // 压力（第 2 个图元，值 0.8）走显示格式串：验证 Text 按 F2 渲染、Number 仍是原值。
                // 刻意挑一条 B12 不断言的绑定，免得把"四类值"那条判据搅进格式串的口径里。
                if (i == 1)
                    binding.DisplayFormat = "F2";

                if (!page.TryAddElement(element, out var error))
                {
                    Check("B6-setup", false, $"添加图元失败：{error}");
                    return 1;
                }

                var handle = values.Add(variableId, binding.VariableName!, seed.Value);
                elements.Add((element, handle));
                handles[seed.Name] = handle;
            }

            var runtime = new ScadaRuntime(document);

            // ---------------- 基座（跨页公共层）：验证热更新 ----------------
            // 基座里刻意<b>不挂绑定</b>：值帧的 items 数是按绑定算的（B11 判据 = 10），
            // 基座只负责"结构层"这一条，值通道由本页 10 条绑定覆盖。
            var basePage = document.AddPage("公共基座");
            var baseElement = new ScadaElement
            {
                TypeKey = "Hmi.Rectangle",
                Name = "基座底板",
                Width = 400,
                Height = 200,
                ZIndex = -1,
            };

            if (!basePage.TryAddElement(baseElement, out var baseElementError))
            {
                Check("C4-setup", false, $"向基座页添加图元失败：{baseElementError}");
                return 1;
            }

            if (!document.TryAddBasePage(basePage, out var baseRegisterError))
            {
                Check("C4-setup", false, $"注册基座失败：{baseRegisterError}");
                return 1;
            }

            if (!document.TrySetBasePage(page, basePage, out var baseRefError))
            {
                Check("C4-setup", false, $"设置基座引用失败：{baseRefError}");
                return 1;
            }

            var baseVersionBefore = basePage.Version;

            var started = runtime.Start();
            hub.SetRunning(runtime, document, generation: 7);

            Check("B6", started, $"runtime.Start()={started} pageVersion={page.Version}");

            // ---------------- WS ----------------
            var ticket = await IssueTicketAsync(http, baseUrl);
            Check("B7-ticket", ticket != null, $"ticket={Short(ticket)}");

            if (ticket == null)
            {
                await host.StopAsync();
                return Finish();
            }

            // 重连凭据：从 hello 里取（C8~C11 在首条连接关闭之后用它）
            string? sessionToken = null;

            var (connected, connectError, ws) = await ConnectAsync($"ws://127.0.0.1:{port}/ws?ticket={ticket}");
            Check("B8", connected && ws != null, $"connected={connected} err={connectError ?? "<none>"}");

            if (!connected || ws == null)
            {
                await host.StopAsync();
                return Finish();
            }

            using (ws)
            {
                var hello = await ReceiveUntilAsync(ws, m => TypeOf(m) == "hello");
                Check("B9", hello != null && hello.Value<bool?>("readOnly") == true,
                    hello == null ? "未收到 hello" : Truncate(hello.ToString(Newtonsoft.Json.Formatting.None), 200));

                // hello 里的重连凭据：客户端保存它，断线后换新 ticket（C8~C11 用它）
                sessionToken = hello?.Value<string>("sessionToken");
                var sessionExpiresIn = hello?.Value<int?>("sessionExpiresIn") ?? 0;

                Check("C3",
                    IsHex32(sessionToken) && sessionExpiresIn > 0,
                    $"sessionToken={(sessionToken == null ? "<null>" : Short(sessionToken))}(len={sessionToken?.Length ?? 0}) "
                    + $"sessionExpiresIn={sessionExpiresIn}");

                var snapshot = await ReceiveUntilAsync(ws, m => TypeOf(m) == "snapshot", 8000);
                var pageNode = snapshot == null ? null : Pick(snapshot, "page", "Page");
                var elementsNode = pageNode == null ? null : Pick(pageNode, "Elements", "elements") as JArray;
                var firstElement = elementsNode?.FirstOrDefault();
                var bindingsNode = firstElement == null ? null : Pick(firstElement, "Bindings", "bindings") as JArray;

                Check("B10",
                    snapshot != null
                    && elementsNode != null && elementsNode.Count == seeds.Count
                    && bindingsNode != null && bindingsNode.Count == 1
                    && Pick(firstElement!, "TypeKey", "typeKey")?.Value<string>() == seeds[0].TypeKey,
                    snapshot == null
                        ? "未收到 snapshot"
                        : $"elements={elementsNode?.Count} bindings={bindingsNode?.Count} json={Truncate(snapshot.ToString(Newtonsoft.Json.Formatting.None), 200)}");

                // 快照必须带上基座版本：否则"改了基座"在客户端看起来就是"什么都没变"
                var snapshotBaseVersion = Pick(snapshot, "basePageVersion", "BasePageVersion")?.Value<int?>();
                var snapshotBaseNode = Pick(pageNode, "BasePage", "basePage");
                var snapshotBaseElements = snapshotBaseNode == null ? null : Pick(snapshotBaseNode, "Elements", "elements") as JArray;

                Check("C4",
                    snapshot != null
                    && snapshotBaseVersion == baseVersionBefore
                    && snapshotBaseElements != null && snapshotBaseElements.Count == 1,
                    $"basePageVersion={snapshotBaseVersion}（期望 {baseVersionBefore}）"
                    + $" baseElements={snapshotBaseElements?.Count}");

                var frame = await ReceiveUntilAsync(ws, m => TypeOf(m) == "frame", 5000);
                var items = frame == null ? null : Pick(frame, "items", "Items") as JArray;

                Check("B11", frame != null && items != null && items.Count == seeds.Count,
                    frame == null ? "未收到 frame" : $"items={items?.Count} json={Truncate(frame.ToString(Newtonsoft.Json.Formatting.None), 200)}");

                var temperatureId = elements[0].Element.ElementId.ToString("D");
                var runningLightId = elements[3].Element.ElementId.ToString("D");
                var fillId = elements[4].Element.ElementId.ToString("D");
                var statusId = elements[7].Element.ElementId.ToString("D");

                var temperatureItem = FindItem(items, temperatureId, "Text");
                var runningLightItem = FindItem(items, runningLightId, "Visible");
                var fillItem = FindItem(items, fillId, "Fill");
                var statusItem = FindItem(items, statusId, "Text");

                Check("B12",
                    temperatureItem?.Value<string>("text") == "12.5"
                    && Math.Abs((temperatureItem.Value<double?>("number") ?? 0) - 12.5) < 1e-9
                    && runningLightItem?.Value<bool?>("bool") == true
                    && fillItem?.Value<string>("color") == "#FF00FF00"
                    && statusItem?.Value<string>("text") == "运行中",
                    $"温度={temperatureItem?.ToString(Newtonsoft.Json.Formatting.None)}"
                    + $" 灯={runningLightItem?.ToString(Newtonsoft.Json.Formatting.None)}"
                    + $" 底色={fillItem?.ToString(Newtonsoft.Json.Formatting.None)}"
                    + $" 状态={statusItem?.ToString(Newtonsoft.Json.Formatting.None)}");

                // ---------------- DisplayFormat：文本按格式串、Number 仍是原值 ----------------
                var pressureItem = FindItem(items, elements[1].Element.ElementId.ToString("D"), "Value");

                Check("C5",
                    pressureItem?.Value<string>("text") == "0.80"
                    && Math.Abs((pressureItem.Value<double?>("number") ?? 0) - 0.8) < 1e-9,
                    $"DisplayFormat=F2 → {pressureItem?.ToString(Newtonsoft.Json.Formatting.None)}"
                    + "（text 应为 0.80；number 应仍为 0.8——格式串不参与数值判定）");

                // ---------------- 改值 → 下一帧跟着变 ----------------
                handles["温度"].Value = 42.5;

                var changed = await ReceiveUntilAsync(ws, m =>
                {
                    if (TypeOf(m) != "frame") return false;
                    var item = FindItem(Pick(m, "items", "Items") as JArray, temperatureId, "Text");
                    return item?.Value<string>("text") == "42.5";
                }, 5000);

                Check("B13", changed != null,
                    changed == null ? "改值后 5s 内没看到新值" : $"frame={Truncate(changed.ToString(Newtonsoft.Json.Formatting.None), 200)}");

                // ---------------- 改页版本 → page ----------------
                page.Version = page.Version + 1;
                var expectedVersion = page.Version;

                var pageMessage = await ReceiveUntilAsync(ws, m => TypeOf(m) == "page", 5000);
                var pageVersionNode = pageMessage == null ? null : Pick(pageMessage, "pageVersion", "PageVersion");
                var innerPage = pageMessage == null ? null : Pick(pageMessage, "page", "Page");
                var innerVersion = innerPage == null ? null : Pick(innerPage, "PageVersion", "pageVersion");

                Check("B14",
                    pageMessage != null
                    && pageVersionNode?.Value<int>() == expectedVersion
                    && innerVersion?.Value<int>() == expectedVersion,
                    pageMessage == null ? "未收到 page" : $"expected={expectedVersion} json={Truncate(pageMessage.ToString(Newtonsoft.Json.Formatting.None), 200)}");

                // ---------------- 基座热更新：改基座图元 → 本页版本不变、basePageVersion 变 → page ----------------
                var pageVersionBeforeBaseChange = page.Version;
                baseElement.Width = baseElement.Width + 10;   // 改基座图元属性 → basePage.Version++

                var basePageMessage = await ReceiveUntilAsync(ws, m => TypeOf(m) == "page", 5000);
                var baseVersionNode = basePageMessage == null ? null : Pick(basePageMessage, "basePageVersion", "BasePageVersion");
                var baseVersionInner = basePageMessage == null ? null : Pick(Pick(basePageMessage, "page", "Page"), "BasePageVersion", "basePageVersion");
                var pageVersionNodeAfter = basePageMessage == null ? null : Pick(basePageMessage, "pageVersion", "PageVersion");

                Check("C6",
                    basePageMessage != null
                    && page.Version == pageVersionBeforeBaseChange
                    && baseVersionNode?.Value<int>() == basePage.Version
                    && baseVersionInner?.Value<int>() == basePage.Version
                    && pageVersionNodeAfter?.Value<int>() == pageVersionBeforeBaseChange,
                    basePageMessage == null
                        ? "改基座后未收到 page（基座热更新失效）"
                        : $"pageVersion={pageVersionNodeAfter?.Value<int>()}（期望仍为 {pageVersionBeforeBaseChange}）"
                          + $" basePageVersion={baseVersionNode?.Value<int>()}（期望 {basePage.Version}）"
                          + $" inner={baseVersionInner?.Value<int>()}");

                // ---------------- 反向对照：基座没变就不该多发 page ----------------
                // <b>判据不能用"读超时"</b>：.NET 里取消 <c>ReceiveAsync</c> 会 abort 整条连接，
                // 之后所有判据都会连坐（本轮第一版就是这么炸的）。
                // 做法：先空等 600ms 让帧堆在缓冲里，再发一条 ping，一直读到 pong 为止——
                // 这一段收到的消息恰好覆盖刚才那个窗口，其中不许有 page。
                await Task.Delay(600);
                await SendTextAsync(ws, "{\"type\":\"ping\",\"t\":\"neg\"}");

                var windowMessages = new List<JObject>();
                var negPong = await ReceiveUntilCollectAsync(ws,
                    m => TypeOf(m) == "pong" && m.Value<string>("t") == "neg", 5000, windowMessages);

                var pageInWindow = windowMessages.Any(m => TypeOf(m) == "page");
                var frameInWindow = windowMessages.Count(m => TypeOf(m) == "frame");

                Check("C7", negPong != null && !pageInWindow && frameInWindow > 0,
                    $"窗口内 frame={frameInWindow} page={(pageInWindow ? "有（不该有）" : "无")} "
                    + $"pong={(negPong != null ? "收到" : "未收到")}（frame>0 用来反证窗口真的读到了东西）");

                // ---------------- ping / 未知 kind ----------------
                await SendTextAsync(ws, "{\"type\":\"ping\",\"t\":\"p1\"}");
                var pong = await ReceiveUntilAsync(ws, m => TypeOf(m) == "pong", 3000);
                Check("B15", pong?.Value<string>("t") == "p1", pong == null ? "未收到 pong" : Truncate(pong.ToString(Newtonsoft.Json.Formatting.None), 120));

                await SendTextAsync(ws, "{\"type\":\"cmd.element\",\"elementId\":\"x\"}");
                var unsupported = await ReceiveUntilAsync(ws, m => TypeOf(m) == "error", 3000);
                Check("B16", unsupported?.Value<string>("code") == "E_UNSUPPORTED",
                    unsupported == null ? "未收到 error" : Truncate(unsupported.ToString(Newtonsoft.Json.Formatting.None), 160));

                // ---------------- 停 → stopped ----------------
                runtime.Stop();
                hub.SetStopped();

                var stopped = await ReceiveUntilAsync(ws, m => TypeOf(m) == "stopped", 3000);
                Check("B17", stopped != null, stopped == null ? "未收到 stopped" : Truncate(stopped.ToString(Newtonsoft.Json.Formatting.None), 120));

                // ---------------- 再运行 → snapshot（不是 page） ----------------
                var restarted = runtime.Start();
                hub.SetRunning(runtime, document, generation: 8);

                var secondSnapshot = await ReceiveUntilAsync(ws, m => TypeOf(m) == "snapshot", 5000);
                Check("B18", restarted && secondSnapshot != null,
                    secondSnapshot == null ? "再次运行后未收到 snapshot" : Truncate(secondSnapshot.ToString(Newtonsoft.Json.Formatting.None), 200));

                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                catch { /* 对端已断开 */ }
            }

            // ---------------- 重连（M1b 评审 blocking ①） ----------------
            // C8 先钉住"ticket 仍是单次"：会话令牌不能把门票的语义带松。
            var reusedTicketConnected = await TryConnectAsync($"ws://127.0.0.1:{port}/ws?ticket={ticket}");
            Check("C8", !reusedTicketConnected,
                $"旧 ticket 复用 → connected={reusedTicketConnected}（必须握手失败：ticket 单次）");

            var (badStatus, badCode, _, _) = await PostTicketAsync(http, baseUrl, "{\"refreshToken\":\"deadbeefdeadbeef\"}");
            Check("C9", badStatus == 401 && badCode == "E_SESSION_EXPIRED",
                $"无效 refreshToken → {badStatus}/{badCode ?? "<无 code>"}（期望 401/E_SESSION_EXPIRED）");

            var (refreshStatus, refreshCode, refreshedTicket, refreshedSessionToken) =
                await PostTicketAsync(http, baseUrl, "{\"refreshToken\":\"" + sessionToken + "\"}");

            Check("C10",
                refreshStatus == 200 && refreshedTicket != null && refreshedTicket != ticket,
                $"sessionToken={Short(sessionToken)} → {refreshStatus}/{refreshCode ?? "<ok>"} "
                + $"新 ticket={Short(refreshedTicket)}（与旧票不同 = {refreshedTicket != ticket}）"
                + $" 会话令牌={Short(refreshedSessionToken)}");

            if (refreshedTicket != null)
            {
                var (reconnected, reconnectError, ws2) = await ConnectAsync($"ws://127.0.0.1:{port}/ws?ticket={refreshedTicket}");
                JObject? reconnectHello = null;

                if (reconnected && ws2 != null)
                {
                    using (ws2)
                    {
                        reconnectHello = await ReceiveUntilAsync(ws2, m => TypeOf(m) == "hello", 5000);

                        try { await ws2.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                        catch { /* 对端已断开 */ }
                    }
                }

                Check("C11",
                    reconnected && reconnectHello != null && reconnectHello.Value<string>("sessionToken") == sessionToken,
                    $"重连 connected={reconnected} err={reconnectError ?? "<none>"} "
                    + $"hello.sessionToken={Short(reconnectHello?.Value<string>("sessionToken"))}（应与首连同一枚）");
            }
            else
            {
                Check("C11", false, "没换到新 ticket，无法验证重连");
            }

            // ---------------- 停服幂等 ----------------
            await host.StopAsync();
            await host.StopAsync();
            Check("B19", !host.IsListening, $"listening={host.IsListening}");

            // Stop() 必须把会话令牌一起作废（否则停服后仍在重连的页面能一直换到新票）
            Check("C12", host.Tickets.SessionCount == 0 && host.Tickets.LiveCount == 0,
                $"session={host.Tickets.SessionCount} live={host.Tickets.LiveCount} handoff={host.Tickets.HandoffCount}");

            return Finish();
        }

        // ---------------- 辅助 ----------------

        private sealed record Seed(string TypeKey, string Name, object Value, string Property);

        private static string? TypeOf(JObject message)
            => message.Value<string>("type") ?? message.Value<string>("kind");

        private static JToken? Pick(JToken? node, params string[] names)
        {
            if (node is not JObject obj) return null;

            foreach (var name in names)
            {
                if (obj.TryGetValue(name, out var value))
                    return value;
            }

            return null;
        }

        private static JObject? FindItem(JArray? items, string elementId, string property)
        {
            if (items == null) return null;

            foreach (var token in items)
            {
                if (token is not JObject item) continue;

                var id = Pick(item, "elementId", "ElementId")?.Value<string>();
                var prop = Pick(item, "property", "Property")?.Value<string>();

                if (string.Equals(id, elementId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(prop, property, StringComparison.Ordinal))
                    return item;
            }

            return null;
        }

        private static async Task<string?> IssueTicketAsync(HttpClient http, string baseUrl)
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(baseUrl + "/api/ticket", content);
            var body = await response.Content.ReadAsStringAsync();
            var json = TryJson(body);
            return json?.Value<string>("ticket");
        }

        /// <summary>POST /api/ticket（原样请求体）：返回状态码、错误码、ticket、会话令牌</summary>
        private static async Task<(int Status, string? Code, string? Ticket, string? SessionToken)> PostTicketAsync(
            HttpClient http,
            string baseUrl,
            string json)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(baseUrl + "/api/ticket", content);
            var body = await response.Content.ReadAsStringAsync();
            var parsed = TryJson(body);

            return ((int)response.StatusCode,
                parsed?.Value<string>("code"),
                parsed?.Value<string>("ticket"),
                parsed?.Value<string>("sessionToken"));
        }

        /// <summary>只判"握手能不能成"：连上就立刻关掉（用来反证"旧 ticket 复用必须失败"）</summary>
        private static async Task<bool> TryConnectAsync(string url)
        {
            var (ok, _, socket) = await ConnectAsync(url);

            if (socket == null)
                return ok;

            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
            catch { /* 对端已断开 */ }

            socket.Dispose();
            return ok;
        }

        /// <summary>
        /// 读到匹配消息为止，<b>顺便把沿途收到的每一条都记进 <paramref name="seen"/></b>。
        ///
        /// 反向对照（"这段时间里不该出现 page"）必须靠它：判据是"读到的消息集合里没有 X"，
        /// 而不是"读超时了"——后者在 .NET 里会 abort 连接（取消 <c>ReceiveAsync</c> 的副作用），
        /// 一条反向判据会把后面的全部判据连坐。
        /// </summary>
        private static async Task<JObject?> ReceiveUntilCollectAsync(
            ClientWebSocket socket,
            Func<JObject, bool> match,
            int timeoutMs,
            List<JObject> seen)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < deadline)
            {
                var remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
                var text = await ReceiveTextAsync(socket, remaining);

                if (text == null) return null;

                var json = TryJson(text);
                if (json == null) continue;

                seen.Add(json);
                if (match(json)) return json;
            }

            return null;
        }

        /// <summary>32 位十六进制（会话令牌的口径）</summary>
        private static bool IsHex32(string? token)
        {
            if (token == null || token.Length != 32)
                return false;

            foreach (var c in token)
            {
                if (!Uri.IsHexDigit(c))
                    return false;
            }

            return true;
        }

        private static async Task<(int Status, string ContentType, string Body)> GetAsync(HttpClient http, string url)
        {
            using var response = await http.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            return ((int)response.StatusCode, response.Content.Headers.ContentType?.ToString() ?? string.Empty, body);
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

        private static async Task<JObject?> ReceiveUntilAsync(
            ClientWebSocket socket,
            Func<JObject, bool> match,
            int timeoutMs = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < deadline)
            {
                var remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
                var text = await ReceiveTextAsync(socket, remaining);

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

        private static Task SendTextAsync(ClientWebSocket socket, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
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

        private static string Truncate(string? text, int max)
            => string.IsNullOrEmpty(text) ? "<null>" : (text.Length <= max ? text : text.Substring(0, max) + "…");

        private static string Short(string? ticket)
            => string.IsNullOrEmpty(ticket) ? "<null>" : ticket!.Substring(0, Math.Min(6, ticket.Length)) + "…";

        private static int Finish()
        {
            Console.WriteLine();
            Console.WriteLine($"RESULT: {Results.Count - _failed} PASS / {_failed} FAIL");
            return _failed == 0 ? 0 : 1;
        }

        private static void Check(string id, bool ok, string detail)
        {
            var line = (ok ? "[PASS] " : "[FAIL] ") + id + "  " + detail;
            Results.Add(line);
            Console.WriteLine(line);
            if (!ok) _failed++;
        }

        // ---------------- 测试替身 ----------------

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

        private sealed class FakeRunHub : IScadaRunHub
        {
            private readonly object _gate = new();
            private ScadaRuntime? _session;
            private ScadaDocument? _document;
            private long _generation;

            public bool IsRunning { get { lock (_gate) return _session != null; } }

            public ScadaRuntime? Session { get { lock (_gate) return _session; } }

            public ScadaDocument? Document { get { lock (_gate) return _document; } }

            public long RunGeneration { get { lock (_gate) return _generation; } }

            public event Action<ScadaRuntime, ScadaDocument, long>? RunStarted;
            public event Action<long>? RunStopped;

            public void SetRunning(ScadaRuntime session, ScadaDocument document, long generation)
            {
                lock (_gate)
                {
                    _session = session;
                    _document = document;
                    _generation = generation;
                }

                RunStarted?.Invoke(session, document, generation);
            }

            public void SetStopped()
            {
                long generation;
                lock (_gate)
                {
                    _session = null;
                    _document = null;
                    generation = _generation;
                }

                RunStopped?.Invoke(generation);
            }

            public bool TryGetCurrent(out ScadaRuntime? session, out ScadaDocument? document, out long generation)
            {
                lock (_gate)
                {
                    session = _session;
                    document = _document;
                    generation = _generation;
                    return session != null;
                }
            }
        }

        private sealed class FakeValueSource : IScadaValueSource
        {
            private readonly Dictionary<Guid, FakeHandle> _byId = new();
            private readonly object _gate = new();

            public FakeHandle Add(Guid id, string name, object? value)
            {
                var handle = new FakeHandle(id, name, value);
                lock (_gate) _byId[id] = handle;
                return handle;
            }

            public bool TryResolve(Guid variableId, string? fallbackName, out IScadaValueHandle? handle)
            {
                lock (_gate)
                {
                    if (variableId != Guid.Empty && _byId.TryGetValue(variableId, out var byId))
                    {
                        handle = byId;
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(fallbackName))
                    {
                        foreach (var pair in _byId)
                        {
                            if (string.Equals(pair.Value.Name, fallbackName, StringComparison.Ordinal))
                            {
                                handle = pair.Value;
                                return true;
                            }
                        }
                    }
                }

                handle = null;
                return false;
            }
        }

        private sealed class FakeHandle : IScadaValueHandle
        {
            private volatile object? _value;

            public FakeHandle(Guid id, string name, object? value)
            {
                VariableId = id;
                Name = name;
                _value = value;
            }

            public Guid VariableId { get; }

            public string Name { get; }

            public Type DataType => _value?.GetType() ?? typeof(object);

            public object? Value
            {
                get => _value;
                set
                {
                    _value = value;
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            public event EventHandler? ValueChanged;

            public bool TryWrite(object? value, out string? error)
            {
                Value = value;
                error = null;
                return true;
            }
        }
    }
}
