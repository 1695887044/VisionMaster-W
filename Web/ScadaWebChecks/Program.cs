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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Core.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Scada.Snapshots;
using VisionMaster.ScadaWeb;

namespace ScadaWebChecks
{
    /// <summary>
    /// SCADA 运行态 Web 的<b>协议层断言</b>（M1b 探针 _WebProbe6 的正式化版本）。
    ///
    /// 覆盖（19 条 M1b 基线）：
    ///   B1  起服务（随机端口）
    ///   B2  GET / → 200 + text/html（无鉴权）
    ///   B3  GET /app.js → 200 + javascript（原样字节，不是 base64）
    ///   B4  GET /styles.css → 200 + text/css
    ///   B4b GET /elements-interactive.js → 200 + javascript + 字节与当前源文件一致（M2 交互图元分册）
    ///   B4c GET /elements-interactive.css → 200 + text/css + 字节与当前源文件一致
    ///   B4d GET /elements-process.js|css → 200 + 字节与当前源文件一致（防旧 wwwroot 被缓存/漏拷）
    ///   B4e 暂存目录：宿主实际服务的 wwwroot 里 7 个静态文件与源文件逐字节一致
    ///   B5  白名单外路径 → 拒绝（401/404，不是 200）
    ///   B6  runtime.Start()
    ///   B7  POST /api/ticket 签发一次性 ticket
    ///   B8  WS 握手成功
    ///   B9  首帧 hello（readOnly=true）
    ///   B10 握手后收到 snapshot，元素数 = 10，元素带 Bindings
    ///   B11 收到至少一帧 frame，items 数 = 10
    ///   B12 值帧四类：Text/Number/Bool/Color 各一例
    ///   B13 改值 → 下一帧 Text 跟着变（值帧不缓存）
    ///   B14 改 page.Version → 收到 page（不是 frame/snapshot）
    ///   B15 ping → pong
    ///   B16 未知 kind → E_UNSUPPORTED
    ///   B17 hub 停止 → 收到 stopped
    ///   B18 再次运行 → 重新收到 snapshot（不是 page）
    ///   B19 StopAsync 幂等
    ///
    /// 新增（M1c 预期；产品代码未实现时如实 FAIL，不跳过）：
    ///   M1c-01 hello 带 sessionToken（可复用的重连凭据）
    ///   M1c-02 用 sessionToken 作 refreshToken 调 POST /api/ticket，换到新 ticket（同一会话）
    ///   M1c-03 用新 ticket 重连成功（收到 hello）
    ///   M1c-04 旧 ticket 复用仍被拒（401）
    ///   M1c-05 基座页 Version 变化 → 收到 page
    ///   M1c-06 该 page 的 basePageVersion 变、本页 pageVersion 不变
    ///   M1c-07 /elements-process.js 与 /elements-process.css → 200
    ///   M1c-08 带 ?v=4 的同一对静态资源 → 200
    ///   M1c-09 snapshot 里 10 个 Hmi.* 图元全部出现（Id 与种子一致）
    ///
    /// 新增（M1 门槛；2026-10-21）：
    ///   B20 10Hz 节奏：连收 20 帧，到达间隔 median 70–140ms、p95 ≤ 200ms、seq 单调递增
    ///   B21 两客户端共享同一页：首屏元素数一致；改值 → 两边都收到 frame；改页版本 → 两边都收到 page
    ///   B23 关标签不停内核：关闭客户端 A 后，B 仍能收到下一帧、hub/runtime 仍在跑
    ///   B24 停运行：B 收到 stopped；之后 300ms 不再有 frame；hub.IsRunning=false；再 Stop 一次不抛
    ///   B36 未实现图元：Hmi.Unsupported 仍进 snapshot（服务端不丢），元素数 +1
    ///
    /// 新增（M2a；2026-10-21，纯内存三段，接在 HTTP/WS 流程之后）：
    ///   M2ActionFilterChecks（11）动作白名单 / Navigate 剔除 / 原钩子不被改
    ///   M2SecurityChecks（18）Origin / 取票限速 / 默认密码 / 匿名不续期 / 撤销 / 会话表上限
    ///   M2CommandChecks（23）命令泵串行与背压 / 四条命令 / 权限 / 双层限速 / caps / presence / 确认令牌 / 三闸
    ///   M2HttpChecks（4）安全前置的 HTTP/WS 端到端：C68 默认密码 / C69 取票限速 /
    ///                     C70 匿名不续期 / C72 会话表上限（每用例独立宿主 + 随机端口）
    ///
    /// 这是协议层断言，不是浏览器 E2E：不渲染像素、不跑 JS、不验证视觉效果。
    /// </summary>
    internal static class Program
    {
        private static readonly List<string> Results = new();
        private static int _passed;
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
                AllowAnonymousView = true,   // 匿名只读观众：断言不走登录
                MaxSessions = 16,
                OperationLeaseEnabled = true // M2b：B41 走一遍 lease.acquire 回执
            };

            var log = new FakeLog();
            var hub = new FakeRunHub();
            var values = new FakeValueSource();
            var alarmFeed = new FakeAlarmFeed();
            var host = new ScadaWebHost(hub, authenticator: null, catalog: null, log, settings, values,
                alarmFeed: alarmFeed);

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

            // M2a：交互图元分册（elements-interactive.js/css）必须能被无鉴权取到，且内容就是<b>当前源文件</b>。
            // 只比"非空"不够：旧 wwwroot 被缓存 / 漏拷时页面照样 200，但跑的是上一版渲染器。
            var sourceWwwRoot = FindSourceWwwRoot();

            var interactiveJs = await CheckStaticAssetAsync(
                http, baseUrl, sourceWwwRoot, "elements-interactive.js",
                expectJavascript: true, expectCss: false);
            Check("B4b", interactiveJs.Ok, interactiveJs.Detail);

            var interactiveCss = await CheckStaticAssetAsync(
                http, baseUrl, sourceWwwRoot, "elements-interactive.css",
                expectJavascript: false, expectCss: true);
            Check("B4c", interactiveCss.Ok, interactiveCss.Detail);

            var processJs = await CheckStaticAssetAsync(
                http, baseUrl, sourceWwwRoot, "elements-process.js",
                expectJavascript: true, expectCss: false);
            var processCss = await CheckStaticAssetAsync(
                http, baseUrl, sourceWwwRoot, "elements-process.css",
                expectJavascript: false, expectCss: true);
            Check("B4d", processJs.Ok && processCss.Ok, processJs.Detail + "；" + processCss.Detail);

            var staged = CheckStagedStaticFiles(sourceWwwRoot);
            Check("B4e", staged.Ok, staged.Detail);

            // M1c：元素渲染器的两个分册（基础页 + 缓存穿透查询串）
            var (pjsStatus, pjsType, pjsBody) = await GetAsync(http, baseUrl + "/elements-process.js");
            var (pcssStatus, pcssType, pcssBody) = await GetAsync(http, baseUrl + "/elements-process.css");
            Check("M1c-07",
                pjsStatus == 200 && pjsBody.Length > 0 && pcssStatus == 200 && pcssBody.Length > 0,
                $"js status={pjsStatus} type={pjsType} len={pjsBody.Length}；css status={pcssStatus} type={pcssType} len={pcssBody.Length}");

            var (qjsStatus, qjsType, qjsBody) = await GetAsync(http, baseUrl + "/elements-process.js?v=4");
            var (qcssStatus, qcssType, qcssBody) = await GetAsync(http, baseUrl + "/elements-process.css?v=4");
            Check("M1c-08",
                qjsStatus == 200 && qjsBody.Length > 0 && qcssStatus == 200 && qcssBody.Length > 0,
                $"js?v=4 status={qjsStatus} type={qjsType} len={qjsBody.Length}；css?v=4 status={qcssStatus} type={qcssType} len={qcssBody.Length}");

            var (s4Status, s4Type, s4Body) = await GetAsync(http, baseUrl + "/appsettings.json");
            var (s5Status, s5Type, _) = await GetAsync(http, baseUrl + "/..%2Fappsettings.json");

            // 未命中路径会落到"鉴权之后的默认路由"（401），不是 404；两者都是拒绝。
            // 白名单保证的是"绝不把磁盘文件吐出去"，不是状态码形状。
            Check("B5", s4Status != 200 && s5Status != 200,
                $"未知路径 status={s4Status} type={s4Type} len={s4Body.Length}；穿越尝试 status={s5Status} type={s5Type}（白名单外一律拒绝）");

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
            var started = runtime.Start();
            hub.SetRunning(runtime, document, generation: 7);

            Check("B6", started, $"runtime.Start()={started} pageVersion={page.Version}");

            // ---------------- WS ----------------
            var ticketIssue = await IssueTicketAsync(http, baseUrl);
            var ticket = ticketIssue.Ticket;
            Check("B7-ticket", ticket != null,
                $"status={ticketIssue.Status} ticket={Short(ticket)} sessionToken={Short(ticketIssue.SessionToken)}");

            if (ticket == null)
            {
                await host.StopAsync();
                return Finish();
            }

            var (connected, connectError, ws) = await ConnectAsync($"ws://127.0.0.1:{port}/ws?ticket={ticket}");
            Check("B8", connected && ws != null, $"connected={connected} err={connectError ?? "<none>"}");

            if (!connected || ws == null)
            {
                await host.StopAsync();
                return Finish();
            }

            // hello 下发的会话令牌（重连凭据）；在 using 块外还要用它换新票，所以先声明
            string? sessionToken = null;

            using (ws)
            {
                var hello = await ReceiveUntilAsync(ws, m => TypeOf(m) == "hello");
                Check("B9", hello != null && hello.Value<bool?>("readOnly") == true,
                    hello == null ? "未收到 hello" : Truncate(hello.ToString(Formatting.None), 200));

                // M1c：hello 必须带会话令牌（浏览器后续 refresh / 重连要用）
                sessionToken = hello?.Value<string>("sessionToken");
                Check("M1c-01", !string.IsNullOrWhiteSpace(sessionToken),
                    hello == null
                        ? "未收到 hello"
                        : $"sessionToken={(string.IsNullOrWhiteSpace(sessionToken) ? "<缺失>" : Short(sessionToken))}"
                          + $" sessionExpiresIn={Show(hello.Value<int?>("sessionExpiresIn"))}"
                          + $" json={Truncate(hello.ToString(Formatting.None), 200)}");

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
                        : $"elements={elementsNode?.Count} bindings={bindingsNode?.Count} json={Truncate(snapshot.ToString(Formatting.None), 200)}");

                // B10b：ImageView.Source 不得进 10Hz 标量帧——每拍全尺寸 HImage→BitmapSource
                // 会把 UI 线程打满，而且四类标量帧只能把 BitmapSource 变成类型名。
                // 图像应由 /img + imageToken 按需取；这里用纯内存快照钉住 resolver 的跳过规则。
                var imagePage = new VisionMaster.Scada.Snapshots.ScadaPageSnapshot
                {
                    PageId = Guid.NewGuid().ToString("D"),
                    PageVersion = 1,
                    Elements = new List<VisionMaster.Scada.Snapshots.ScadaElementSnapshot>
                    {
                        new()
                        {
                            ElementId = Guid.NewGuid().ToString("D"),
                            TypeKey = "Hmi.ImageView",
                            Bindings = new List<VisionMaster.Scada.Snapshots.ScadaBindingSnapshot>
                            {
                                new()
                                {
                                    TargetProperty = "Source",
                                    VariableName = "img",
                                    Enabled = true
                                }
                            }.AsReadOnly()
                        }
                    }.AsReadOnly()
                };

                var imageItems = new ScadaWebValueResolver().Resolve(imagePage, new ImageOnlyValueSource());
                Check("B10b", imageItems.Count == 0,
                    $"items={imageItems.Count}（期望 0；图像应由 /img 通道提供）");

                // M1c：10 个 Hmi.* 图元全部出现，且 Id 与种子逐条一致
                var hmiElements = elementsNode?.OfType<JObject>().ToList() ?? new List<JObject>();
                var typeKeys = hmiElements
                    .Select(e => Pick(e, "TypeKey", "typeKey")?.Value<string>() ?? string.Empty)
                    .ToList();
                var elementIds = hmiElements
                    .Select(e => Pick(e, "ElementId", "elementId")?.Value<string>() ?? string.Empty)
                    .ToList();
                var expectedIds = elements.Select(e => e.Element.ElementId.ToString("D")).ToList();
                var allHmi = typeKeys.Count == seeds.Count
                    && typeKeys.All(t => t.StartsWith("Hmi.", StringComparison.Ordinal));
                var idsMatch = elementIds.Count == expectedIds.Count
                    && expectedIds.All(id => elementIds.Contains(id, StringComparer.OrdinalIgnoreCase));
                var distinctTypes = string.Join(",", typeKeys.Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal));

                Check("M1c-09", allHmi && idsMatch,
                    $"elements={typeKeys.Count}/{seeds.Count} allHmi={allHmi} idsMatch={idsMatch} distinctTypes={distinctTypes}");

                var frame = await ReceiveUntilAsync(ws, m => TypeOf(m) == "frame", 5000);
                var items = frame == null ? null : Pick(frame, "items", "Items") as JArray;

                Check("B11", frame != null && items != null && items.Count == seeds.Count,
                    frame == null ? "未收到 frame" : $"items={items?.Count} json={Truncate(frame.ToString(Formatting.None), 200)}");

                // B11b/B11c（2026-10-22 增量帧）：首帧必须是 keyframe（全量）；
                // 随后没有变化的帧是增量空帧（10Hz 心跳），且同页连续帧不应再重建整页快照。
                var firstKeyframe = frame?.Value<bool?>("keyframe") == true;
                var idleFrame = await ReceiveUntilAsync(ws, m => TypeOf(m) == "frame", 3000);
                var idleItems = idleFrame == null ? null : Pick(idleFrame, "items", "Items") as JArray;

                Check("B11b",
                    firstKeyframe
                    && idleFrame != null
                    && idleFrame.Value<bool?>("keyframe") == false
                    && idleItems != null && idleItems.Count == 0,
                    $"firstKeyframe={firstKeyframe} idleKeyframe={idleFrame?.Value<bool?>("keyframe")} "
                    + $"idleItems={idleItems?.Count}");

                var buildsBefore = host.StructureBuildCount;
                var idleFrame2 = await ReceiveUntilAsync(ws, m => TypeOf(m) == "frame", 3000);
                var buildsAfter = host.StructureBuildCount;

                Check("B11c",
                    idleFrame2 != null && buildsAfter == buildsBefore,
                    $"同页连续帧重建次数 {buildsBefore}→{buildsAfter}（期望不变）");

                // B40（M2b）：AckButton 的报警列表/未确认计数必须由服务端主动广播。
                var alarmsMessage = await ReceiveUntilAsync(ws, m => TypeOf(m) == "alarms", 5000);
                var alarmItems = alarmsMessage == null ? null : Pick(alarmsMessage, "alarms", "Alarms") as JArray;

                Check("B40",
                    alarmsMessage != null
                    && alarmItems != null && alarmItems.Count == 1
                    && alarmsMessage.Value<int?>("unacknowledgedCount") == 1
                    && alarmItems[0]?.Value<string>("alarmId") == alarmFeed.AlarmId.ToString("D"),
                    alarmsMessage == null
                        ? "未收到 alarms"
                        : $"alarms={alarmItems?.Count} unack={alarmsMessage.Value<int?>("unacknowledgedCount")} "
                          + $"json={Truncate(alarmsMessage.ToString(Formatting.None), 200)}");

                // B41（M2b）：lease.acquire 必须走真实 WS 回执，并带租约快照。
                await SendTextAsync(ws, "{\"type\":\"lease.acquire\",\"id\":\"lease-1\"}");
                var leaseAck = await ReceiveUntilAsync(
                    ws, m => TypeOf(m) == "ack" && m.Value<string>("id") == "lease-1", 3000);
                var leaseNode = leaseAck == null ? null : Pick(leaseAck, "lease", "Lease") as JObject;

                Check("B41",
                    leaseAck != null
                    && leaseAck.Value<bool?>("ok") == true
                    && leaseNode?.Value<bool?>("held") == true,
                    leaseAck == null ? "未收到 lease ack" : Truncate(leaseAck.ToString(Formatting.None), 200));

                // B42（M2b）：caps 必须逐图元下发 deny/allow，并带租约状态；匿名会话全 deny。
                var capsMessage = await ReceiveUntilAsync(ws, m => TypeOf(m) == "caps", 5000);
                var capsNode = capsMessage == null ? null : Pick(capsMessage, "caps", "Caps") as JObject;
                var capsDeny = capsNode?["deny"] as JArray;
                var capsAllow = capsNode?["allow"] as JArray;
                var capsLease = capsNode?["lease"] as JObject;

                Check("B42",
                    capsNode != null
                    && (capsNode.Value<long?>("policyVersion") ?? 0) > 0
                    && capsNode.Value<bool?>("default") == false
                    && capsDeny != null && capsDeny.Count >= seeds.Count
                    && capsAllow != null && capsAllow.Count == 0
                    && capsLease?.Value<bool?>("enabled") == true,
                    capsNode == null
                        ? "未收到 caps"
                        : $"deny={capsDeny?.Count} allow={capsAllow?.Count} "
                          + $"policyVersion={capsNode.Value<long?>("policyVersion")} "
                          + $"lease={capsLease?.ToString(Formatting.None)}");

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
                    $"温度={temperatureItem?.ToString(Formatting.None)}"
                    + $" 灯={runningLightItem?.ToString(Formatting.None)}"
                    + $" 底色={fillItem?.ToString(Formatting.None)}"
                    + $" 状态={statusItem?.ToString(Formatting.None)}");

                // ---------------- 改值 → 下一帧跟着变 ----------------
                handles["温度"].Value = 42.5;

                var changed = await ReceiveUntilAsync(ws, m =>
                {
                    if (TypeOf(m) != "frame") return false;
                    var item = FindItem(Pick(m, "items", "Items") as JArray, temperatureId, "Text");
                    return item?.Value<string>("text") == "42.5";
                }, 5000);

                Check("B13", changed != null,
                    changed == null ? "改值后 5s 内没看到新值" : $"frame={Truncate(changed.ToString(Formatting.None), 200)}");

                // B13b：改一个值 → 增量帧只带这一项（不是全量 10 项）。
                var changedItems = changed == null ? null : Pick(changed, "items", "Items") as JArray;
                Check("B13b",
                    changed != null
                    && changed.Value<bool?>("keyframe") == false
                    && changedItems != null && changedItems.Count == 1,
                    $"keyframe={changed?.Value<bool?>("keyframe")} items={changedItems?.Count}");

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
                    pageMessage == null ? "未收到 page" : $"expected={expectedVersion} json={Truncate(pageMessage.ToString(Formatting.None), 200)}");

                // B14b：结构变更后的第一帧必须是 keyframe（增量基线作废）。
                var afterPageFrame = await ReceiveUntilAsync(
                    ws, m => TypeOf(m) == "frame" && m.Value<bool?>("keyframe") == true, 3000);

                Check("B14b", afterPageFrame != null,
                    afterPageFrame == null
                        ? "结构变更后 3s 内没收到 keyframe"
                        : $"frameSeq={afterPageFrame.Value<long?>("frameSeq")} "
                          + $"items={(Pick(afterPageFrame, "items", "Items") as JArray)?.Count}");

                // ---------------- ping / 未知 kind ----------------
                await SendTextAsync(ws, "{\"type\":\"ping\",\"t\":\"p1\"}");
                var pong = await ReceiveUntilAsync(ws, m => TypeOf(m) == "pong", 3000);
                Check("B15", pong?.Value<string>("t") == "p1", pong == null ? "未收到 pong" : Truncate(pong.ToString(Formatting.None), 120));

                // 未知 kind → E_UNSUPPORTED。M2a 起 cmd.* 已知；M2b 起 lease.acquire/renew/release 也已知，
                // 所以这里必须用一个真的不认识的 kind（lease.steal 仍属后续批次）。
                await SendTextAsync(ws, "{\"type\":\"lease.steal\"}");
                var unsupported = await ReceiveUntilAsync(ws, m => TypeOf(m) == "error", 3000);
                Check("B16", unsupported?.Value<string>("code") == "E_UNSUPPORTED",
                    unsupported == null ? "未收到 error" : Truncate(unsupported.ToString(Formatting.None), 160));

                // ---------------- 停 → stopped ----------------
                runtime.Stop();
                hub.SetStopped();

                var stopped = await ReceiveUntilAsync(ws, m => TypeOf(m) == "stopped", 3000);
                Check("B17", stopped != null, stopped == null ? "未收到 stopped" : Truncate(stopped.ToString(Formatting.None), 120));

                // ---------------- 再运行 → snapshot（不是 page） ----------------
                var restarted = runtime.Start();
                hub.SetRunning(runtime, document, generation: 8);

                var secondSnapshot = await ReceiveUntilAsync(ws, m => TypeOf(m) == "snapshot", 5000);
                Check("B18", restarted && secondSnapshot != null,
                    secondSnapshot == null ? "再次运行后未收到 snapshot" : Truncate(secondSnapshot.ToString(Formatting.None), 200));

                // ---------------- 基座页版本（M1c 预期） ----------------
                // 放在 B18 之后：此时 snapshot/frame 的既有断言已经跑完，新增基座不会影响它们。
                var basePage = document.AddPage("公共基座");
                var baseElement = new ScadaElement
                {
                    TypeKey = "Hmi.Rectangle",
                    Name = "基座底",
                    X = 0,
                    Y = 0,
                    Width = 800,
                    Height = 600,
                    ZIndex = -1,
                };

                var baseSetupError = string.Empty;

                if (!basePage.TryAddElement(baseElement, out var addBaseError))
                    baseSetupError = $"添加基座图元失败：{addBaseError}";
                else if (!document.TryAddBasePage(basePage, out var registerBaseError))
                    baseSetupError = $"注册基座失败：{registerBaseError}";
                else if (!document.TrySetBasePage(page, basePage, out var linkBaseError))
                    baseSetupError = $"本页引用基座失败：{linkBaseError}";

                // 本页 BasePageId 变化会让本页 Version +1 → 先吃掉这条 page，拿到基线
                var linkPageMessage = await ReceiveUntilAsync(ws, m => TypeOf(m) == "page", 5000);
                var linkPageVersion = linkPageMessage?.Value<int?>("pageVersion");
                var linkBaseVersion = ReadBasePageVersion(linkPageMessage);
                var linkNestedBaseVersion = ReadNestedBasePageVersion(linkPageMessage);

                var baseVersionBefore = basePage.Version;
                basePage.Version = baseVersionBefore + 1;
                var expectedBaseVersion = basePage.Version;

                var baseChangedPage = await ReceiveUntilAsync(ws, m =>
                {
                    if (TypeOf(m) != "page") return false;
                    return ReadBasePageVersion(m) == expectedBaseVersion;
                }, 5000);

                Check("M1c-05",
                    baseSetupError.Length == 0 && baseChangedPage != null,
                    baseSetupError.Length > 0
                        ? baseSetupError
                        : baseChangedPage == null
                            ? $"改基座 Version {baseVersionBefore}→{expectedBaseVersion} 后 5s 内没收到 page"
                              + "（当前实现只按本页 pageVersion 判页键，基座版本不在判据里）"
                            : Truncate(baseChangedPage.ToString(Formatting.None), 200));

                Check("M1c-06",
                    baseSetupError.Length == 0
                    && linkPageMessage != null
                    && linkBaseVersion == baseVersionBefore
                    && baseChangedPage != null
                    && ReadBasePageVersion(baseChangedPage) == expectedBaseVersion
                    && baseChangedPage.Value<int?>("pageVersion") == linkPageVersion,
                    $"setup={baseSetupError.Length == 0}"
                    + $" linkPage={linkPageMessage != null}"
                    + $" linkBasePageVersion={Show(linkBaseVersion)}（期望 {baseVersionBefore}，嵌套 BasePage.PageVersion={Show(linkNestedBaseVersion)}）"
                    + $" linkPageVersion={Show(linkPageVersion)}"
                    + $" changedBasePageVersion={Show(ReadBasePageVersion(baseChangedPage))}（期望 {expectedBaseVersion}）"
                    + $" changedPageVersion={Show(baseChangedPage?.Value<int?>("pageVersion"))}");

                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                catch { /* 对端已断开 */ }
            }

            // ---------------- 一次性 ticket 复用 / refreshToken（M1c 预期） ----------------
            var (oldRejected, oldStatus, oldDetail) = await ConnectExpectingRejectionAsync(
                $"ws://127.0.0.1:{port}/ws?ticket={ticket}");

            Check("M1c-04", oldRejected && oldStatus == 401,
                $"rejected={oldRejected} status={Show(oldStatus)} detail={oldDetail}");

            // 刷新端点：POST /api/ticket {"refreshToken":"…"}（请求字段叫 refreshToken，
            // 值是 hello 下发的 sessionToken）。刻意不用 /api/ticket/refresh：
            // 产品协议就是同一个 /api/ticket 按请求体分流。
            var refresh = await RefreshTicketAsync(http, baseUrl, sessionToken);
            var refreshedTicket = refresh.Json?.Value<string>("ticket");
            var refreshedSessionToken = refresh.Json?.Value<string>("sessionToken")
                ?? refresh.Json?.Value<string>("refreshToken");

            Check("M1c-02",
                !string.IsNullOrWhiteSpace(sessionToken)
                && refresh.Status == 200
                && !string.IsNullOrWhiteSpace(refreshedTicket)
                && !string.Equals(refreshedTicket, ticket, StringComparison.Ordinal)
                && string.Equals(refreshedSessionToken, sessionToken, StringComparison.Ordinal),
                $"hello.sessionToken={(string.IsNullOrWhiteSpace(sessionToken) ? "<缺失>" : Short(sessionToken))}"
                + $" refreshStatus={refresh.Status}"
                + $" newTicket={(string.IsNullOrWhiteSpace(refreshedTicket) ? "<缺失>" : Short(refreshedTicket))}"
                + $" responseSessionToken={(string.IsNullOrWhiteSpace(refreshedSessionToken) ? "<缺失>" : Short(refreshedSessionToken))}"
                + $" body={Truncate(refresh.Body, 120)}");

            if (string.IsNullOrWhiteSpace(refreshedTicket))
            {
                Check("M1c-03", false,
                    "未拿到 refreshToken 换来的新 ticket，无法重连（检查 POST /api/ticket {\"refreshToken\":…}）");
            }
            else
            {
                var (reconnected, reconnectDetail) = await ConnectAndHelloAsync(
                    $"ws://127.0.0.1:{port}/ws?ticket={refreshedTicket}");

                Check("M1c-03", reconnected, reconnectDetail);
            }

            // ============================================================
            //  M1 门槛：10Hz 节奏 / 多客户端 / 关标签 / 停运行 / 未实现图元
            //  这一段全部要求"运行中"，所以放在 B18 重启之后、停服之前。
            //  建连失败也照样 Check(false)——不跳过，退出码非 0。
            // ============================================================
            var wsBase = $"ws://127.0.0.1:{port}";
            var temperatureElementId = elements[0].Element.ElementId.ToString("D");

            // B36 的"+1"基线：B21 之前本页元素数（B21 建连失败时退回种子数）
            var elementCountBeforeUnsupported = seeds.Count;

            // ---------------- B20：10Hz 节奏（20 帧） ----------------
            var cadence = await MeasureCadenceAsync(http, baseUrl, wsBase, frameCount: 20);
            Check("B20", cadence.Ok, cadence.Detail);

            // ---------------- B21/B23/B24：两个客户端看同一页 ----------------
            var (openA, detailA, clientA) = await OpenClientAsync(http, baseUrl, wsBase);
            var (openB, detailB, clientB) = await OpenClientAsync(http, baseUrl, wsBase);

            if (!openA || !openB || clientA == null || clientB == null)
            {
                clientA?.Dispose();
                clientB?.Dispose();

                Check("B21", false, $"两客户端建连失败：A={detailA}；B={detailB}");
                Check("B23", false, "无法验证：两客户端未建立");
                Check("B24", false, "无法验证：两客户端未建立");
            }
            else
            {
                using (clientA)
                using (clientB)
                {
                    elementCountBeforeUnsupported = clientA.ElementCount;

                    var countA = clientA.ElementCount;
                    var countB = clientB.ElementCount;
                    var sameSnapshot = countA == countB && countA == seeds.Count;

                    // ① 改假值源 → 两个客户端都收到对应值帧
                    handles["温度"].Value = 77.5;

                    var sharedFrameA = await ReceiveUntilAsync(clientA.Socket,
                        m => FrameHasText(m, temperatureElementId, "77.5"), 5000);
                    var sharedFrameB = await ReceiveUntilAsync(clientB.Socket,
                        m => FrameHasText(m, temperatureElementId, "77.5"), 5000);

                    // ② 改页 Version → 两个客户端都收到 page（不是 frame/snapshot）
                    var sharedPageVersion = page.Version + 1;
                    page.Version = sharedPageVersion;

                    var sharedPageA = await ReceiveUntilAsync(clientA.Socket,
                        m => IsPageMessage(m, sharedPageVersion), 5000);
                    var sharedPageB = await ReceiveUntilAsync(clientB.Socket,
                        m => IsPageMessage(m, sharedPageVersion), 5000);

                    Check("B21",
                        sameSnapshot && sharedFrameA != null && sharedFrameB != null
                        && sharedPageA != null && sharedPageB != null,
                        $"elementsA={countA} elementsB={countB} 一致={sameSnapshot}"
                        + $" frameA={sharedFrameA != null} frameB={sharedFrameB != null}（温度 12.5→77.5）"
                        + $" pageA={sharedPageA != null} pageB={sharedPageB != null}（pageVersion→{sharedPageVersion}）");

                    // ---------------- B23：关标签不停内核 ----------------
                    await clientA.CloseAsync();

                    var frameAfterClose = await ReceiveUntilAsync(clientB.Socket, m => TypeOf(m) == "frame", 3000);
                    var onlineSettled = await WaitUntilAsync(() => host.OnlineCount <= 1, 3000);

                    Check("B23",
                        frameAfterClose != null && hub.IsRunning && runtime.IsRunning && onlineSettled,
                        $"关闭 A 后 B 收到下一帧={frameAfterClose != null}"
                        + $" hubIsRunning={hub.IsRunning} runtimeIsRunning={runtime.IsRunning}"
                        + $" online={host.OnlineCount}（3s 内降到 ≤1={onlineSettled}）");

                    // ---------------- B24：停运行 → stopped + 不再出帧 ----------------
                    var stopError = string.Empty;

                    try { hub.Stop(); }
                    catch (Exception ex) { stopError = $"{ex.GetType().Name}: {FirstLine(ex.Message)}"; }

                    var stoppedB = await ReceiveUntilAsync(clientB.Socket, m => TypeOf(m) == "stopped", 3000);

                    // 停运行那一拍可能已经拍完快照、正把最后一帧塞进队列（队列是 FIFO，
                    // 这条在途帧会排在 stopped 之后）。先给它 250ms 落下来，再验 300ms 静默：
                    // 判的是"停了之后不再持续出帧"，不是"停止瞬间没有在途帧"。
                    var inFlight = await ReceiveUntilAsync(clientB.Socket, m => TypeOf(m) == "frame", 250);
                    var quietAfterStop = await ReceiveUntilAsync(clientB.Socket, m => TypeOf(m) == "frame", 300);

                    var reStopError = string.Empty;

                    try { hub.Stop(); }
                    catch (Exception ex) { reStopError = $"{ex.GetType().Name}: {FirstLine(ex.Message)}"; }

                    Check("B24",
                        stopError.Length == 0 && stoppedB != null && quietAfterStop == null
                        && !hub.IsRunning && reStopError.Length == 0,
                        $"stopError={(stopError.Length == 0 ? "<none>" : stopError)}"
                        + $" stopped={stoppedB != null}"
                        + $" 在途帧={inFlight != null} 300ms静默={quietAfterStop == null}"
                        + $" hubIsRunning={hub.IsRunning}"
                        + $" reStopError={(reStopError.Length == 0 ? "<none>" : reStopError)}");
                }
            }

            // ---------------- B36：未实现图元不丢（协议层） ----------------
            // 客户端占位盒由 wwwroot\app.js 负责；本项只验服务端不把未知 TypeKey 的图元过滤掉。
            var unsupportedElement = new ScadaElement
            {
                TypeKey = "Hmi.Unsupported",
                Name = "未实现图元",
                X = 640,
                Y = 480,
                Width = 120,
                Height = 80,
                ZIndex = 99,
            };

            var unsupportedAdded = page.TryAddElement(unsupportedElement, out var unsupportedError);
            var restartedForUnsupported = runtime.IsRunning || runtime.Start();
            hub.SetRunning(runtime, document, generation: 9);

            var (openU, detailU, clientU) = await OpenClientAsync(http, baseUrl, wsBase);

            if (!openU || clientU == null)
            {
                Check("B36", false, $"建客户端失败：{detailU}");
            }
            else
            {
                using (clientU)
                {
                    var unsupportedPage = Pick(clientU.Snapshot, "page", "Page");
                    var unsupportedElements = Pick(unsupportedPage, "Elements", "elements") as JArray;
                    var unsupportedNode = unsupportedElements?.OfType<JObject>().FirstOrDefault(e =>
                        string.Equals(Pick(e, "TypeKey", "typeKey")?.Value<string>(),
                            "Hmi.Unsupported", StringComparison.Ordinal));

                    var nameOk = Pick(unsupportedNode, "Name", "name")?.Value<string>() == unsupportedElement.Name;
                    var geometryOk = IsClose(ReadNumber(unsupportedNode, "X", "x"), unsupportedElement.X)
                        && IsClose(ReadNumber(unsupportedNode, "Y", "y"), unsupportedElement.Y)
                        && IsClose(ReadNumber(unsupportedNode, "Width", "width"), unsupportedElement.Width)
                        && IsClose(ReadNumber(unsupportedNode, "Height", "height"), unsupportedElement.Height);

                    Check("B36",
                        unsupportedAdded && restartedForUnsupported
                        && unsupportedElements != null
                        && unsupportedElements.Count == elementCountBeforeUnsupported + 1
                        && unsupportedNode != null && nameOk && geometryOk,
                        $"added={unsupportedAdded}（{(unsupportedError.Length == 0 ? "<none>" : unsupportedError)}）"
                        + $" restarted={restartedForUnsupported}"
                        + $" elements={unsupportedElements?.Count}（期望 {elementCountBeforeUnsupported + 1}）"
                        + $" found={unsupportedNode != null} name={nameOk} geometry={geometryOk}");

                    // B44（M2b / C79）：AllowCommands=false 时命令必须在入口回 E_POLICY，不执行。
                    await SendTextAsync(clientU.Socket,
                        "{\"type\":\"cmd.element\",\"id\":\"policy-1\",\"elementId\":\""
                        + unsupportedElement.ElementId.ToString("D")
                        + "\",\"event\":\"Pressed\",\"pageId\":\""
                        + page.PageId.ToString("D")
                        + "\",\"pageVersion\":" + page.Version
                        + ",\"runGeneration\":" + hub.RunGeneration + "}");

                    var policyError = await ReceiveUntilAsync(
                        clientU.Socket, m => TypeOf(m) == "error" && m.Value<string>("id") == "policy-1", 3000);

                    Check("B44",
                        policyError?.Value<string>("code") == ScadaWebErrorCodes.Policy,
                        policyError == null
                            ? "未收到 E_POLICY"
                            : Truncate(policyError.ToString(Formatting.None), 200));

                    // B43（M2b）：撤销会话必须用 4403 关闭真实 WS，而不是等它自然超时。
                    var tokenU = clientU.Hello?.Value<string>("sessionToken");
                    var revokedCount = string.IsNullOrWhiteSpace(tokenU) ? 0 : host.RevokeSession(tokenU);
                    var closeCode = await WaitForCloseCodeAsync(clientU.Socket, 3000);

                    Check("B43",
                        !string.IsNullOrWhiteSpace(tokenU) && revokedCount == 1 && closeCode == 4403,
                        $"sessionToken={(string.IsNullOrWhiteSpace(tokenU) ? "<缺失>" : Short(tokenU))} "
                        + $"closed={revokedCount} closeCode={closeCode?.ToString() ?? "<none>"}（期望 4403）");

                    await clientU.CloseAsync();
                }
            }

            // ---------------- B51：/img 图像通道（独立宿主 + 2x2 测试图） ----------------
            await RunImageChecksAsync(http);

            // ---------------- B52：轴只读数据（独立宿主 + 假 IScadaAxisSource） ----------------
            await RunAxisChecksAsync(http);

            // ---------------- B53：DataGrid 行数据（独立宿主 + 假 IScadaRuntimeDataProvider） ----------------
            await RunGridChecksAsync(http);

            // ---------------- B54：cmd.result 只读查询（ack 丢失后的兜底；主宿主 AllowCommands=false） ----------------
            {
                var resultIssue = await IssueTicketAsync(http, baseUrl);
                var (resultConnected, resultError, resultSocket) =
                    await ConnectAsync($"{wsBase}/ws?ticket={resultIssue.Ticket}");

                if (!resultConnected || resultSocket == null)
                {
                    Check("B54", false, $"连接失败 err={resultError ?? "<none>"}");
                    Check("B54b", false, $"连接失败 err={resultError ?? "<none>"}");
                }
                else
                {
                    using (resultSocket)
                    {
                        await ReceiveUntilAsync(resultSocket, m => TypeOf(m) == "hello", 3000);
                        await ReceiveUntilAsync(resultSocket, m => TypeOf(m) == "snapshot", 5000);

                        var queryId = Guid.NewGuid().ToString("N");
                        await SendTextAsync(resultSocket, JsonConvert.SerializeObject(new
                        {
                            type = "cmd.result",
                            id = queryId,
                            targetRequestId = "missing-" + queryId
                        }));

                        var unknown = await ReceiveUntilAsync(
                            resultSocket,
                            m => TypeOf(m) == "result" && m.Value<string>("id") == queryId,
                            3000);

                        Check("B54",
                            unknown != null
                            && unknown.Value<string>("state") == "unknown"
                            && unknown.Value<string>("targetRequestId") == "missing-" + queryId
                            && unknown["ok"] == null,
                            unknown == null
                                ? "未收到 result"
                                : $"state={unknown.Value<string>("state")} "
                                  + $"target={unknown.Value<string>("targetRequestId")} "
                                  + $"hasOk={unknown["ok"] != null}");

                        var badQueryId = Guid.NewGuid().ToString("N");
                        await SendTextAsync(resultSocket, JsonConvert.SerializeObject(new
                        {
                            type = "cmd.result",
                            id = badQueryId
                        }));

                        var bad = await ReceiveUntilAsync(
                            resultSocket,
                            m => TypeOf(m) == "error" && m.Value<string>("id") == badQueryId,
                            3000);

                        Check("B54b",
                            bad != null && bad.Value<string>("code") == "E_BAD_REQUEST",
                            bad == null
                                ? "未收到 error"
                                : $"code={bad.Value<string>("code")} message={bad.Value<string>("message")}");
                    }

                    // 正常连接走完"预留 → 晋升 → 释放"：关闭后两本账都必须归零，
                    // 否则下一次启动会把 16 个名额慢慢漏光（B55c 是 24 槽的集成反证）。
                    var admissionSettled = await WaitUntilAsync(
                        () => host.AdmittedCount == 0 && host.HandshakeCount == 0, 3000);

                    Check("B55c", admissionSettled,
                        $"admitted={host.AdmittedCount} handshake={host.HandshakeCount}");
                }
            }

            // ---------------- B55/B55b：24 槽准入闸（16 活跃 + 8 握手，5s TTL） ----------------
            {
                var now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
                var gate = new ScadaWebAdmissionGate(maxActive: 16);

                var reserved = 0;
                for (var i = 0; i < 8; i++)
                {
                    if (gate.TryReserveHandshake("h" + i, now.AddSeconds(30), now, out _))
                        reserved++;
                }

                var ninth = gate.TryReserveHandshake("h8", now.AddSeconds(30), now, out _);

                var promoted = 0;
                for (var i = 0; i < 8; i++)
                {
                    if (gate.TryPromote("h" + i, now, out _))
                        promoted++;
                }

                var secondBatch = 0;
                for (var i = 8; i < 16; i++)
                {
                    if (gate.TryReserveHandshake("h" + i, now.AddSeconds(30), now, out _))
                        secondBatch++;
                }

                var promoted2 = 0;
                for (var i = 8; i < 16; i++)
                {
                    if (gate.TryPromote("h" + i, now, out _))
                        promoted2++;
                }

                // 活跃已满时仍允许"占握手槽"（在途），但第 17 个活跃一定转不过去
                for (var i = 16; i < 24; i++)
                    gate.TryReserveHandshake("h" + i, now.AddSeconds(30), now, out _);

                var overflow = gate.TryPromote("h16", now, out _);
                gate.ReleaseActive();
                var afterRelease = gate.TryPromote("h17", now, out _);

                Check("B55",
                    reserved == 8 && !ninth && promoted == 8 && secondBatch == 8 && promoted2 == 8
                    && !overflow && afterRelease && gate.ActiveCount == 16,
                    $"预留={reserved} 第9={ninth} 晋升={promoted}/{promoted2} 溢出={overflow} "
                    + $"释放后={afterRelease} active={gate.ActiveCount} handshake={gate.HandshakeCount}");

                var ttlGate = new ScadaWebAdmissionGate(maxActive: 4, maxHandshake: 2);
                ttlGate.TryReserveHandshake("a", now.AddSeconds(30), now, out _);
                ttlGate.TryReserveHandshake("b", now.AddSeconds(30), now, out _);
                var third = ttlGate.TryReserveHandshake("c", now.AddSeconds(30), now, out _);
                var swept = ttlGate.SweepExpired(now.AddSeconds(6));
                var promoteAfterTtl = ttlGate.TryPromote("a", now.AddSeconds(6), out _);

                ttlGate.TryReserveHandshake("d", now.AddSeconds(30), now, out _);
                ttlGate.TryPromote("d", now, out _);
                ttlGate.ReleaseActive();
                ttlGate.ReleaseActive();
                ttlGate.ReleaseActive();

                Check("B55b",
                    !third && swept == 2 && !promoteAfterTtl
                    && ttlGate.ActiveCount == 0 && ttlGate.HandshakeCount == 0,
                    $"第3={third} 清扫={swept} TTL后晋升={promoteAfterTtl} "
                    + $"active={ttlGate.ActiveCount} handshake={ttlGate.HandshakeCount}");
            }

            // ---------------- B56：第 17 个活跃走 WS 1013（不是 HTTP 503） ----------------
            await RunAdmissionHostChecksAsync(http);

            // ---------------- B57：/api/catalog 暴露 DefaultValue + 客户端首帧补键 ----------------
            await RunCatalogChecksAsync(http);

            // ---------------- B58-B61：structureSeq + view.resync（帧循环单写者） ----------------
            await RunResyncChecksAsync(http, baseUrl, wsBase, page, hub);

            // ---------------- B45：死连接回收（判死阈值压到 1s；运行已停止，双向都无进展） ----------------
            {
                hub.Stop();
                settings.SessionIdleTimeoutSeconds = 1;

                var idleIssue = await IssueTicketAsync(http, baseUrl);
                var (idleConnected, idleError, idleSocket) = await ConnectAsync($"{wsBase}/ws?ticket={idleIssue.Ticket}");

                if (!idleConnected || idleSocket == null)
                {
                    Check("B45", false, $"连接失败 err={idleError ?? "<none>"}");
                }
                else
                {
                    using (idleSocket)
                    {
                        await ReceiveUntilAsync(idleSocket, m => TypeOf(m) == "hello", 3000);
                        var idleCloseCode = await WaitForCloseCodeAsync(idleSocket, 6000);

                        Check("B45", idleCloseCode == 4408,
                            $"closeCode={idleCloseCode?.ToString() ?? "<none>"}（期望 4408）");
                    }
                }

                settings.SessionIdleTimeoutSeconds = 45;
            }

            // ---------------- 停服幂等 ----------------
            await host.StopAsync();
            await host.StopAsync();
            Check("B19", !host.IsListening, $"listening={host.IsListening}");

            // 诊断：宿主只把 Warn 收进 FakeLog；失败时把最近几条打出来（否则"没收到 page"无从判断）
            if (_failed > 0 && log.Lines.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"--- host warnings ({log.Lines.Count}) ---");
                foreach (var line in log.Lines.TakeLast(20))
                    Console.WriteLine("[WARN] " + line);
            }

            // ============================================================
            //  M2a：安全前置 / 动作过滤 / 命令面（纯内存，起完服务再跑）
            //  顺序：动作过滤（11）→ 安全前置（18）→ 命令层（23）。
            //  这三段不依赖上面的 HTTP/WS 流程，也不占端口；失败条数直接计入退出码。
            // ============================================================
            M2ActionFilterChecks.Run();
            _passed += M2ActionFilterChecks.Pass;
            _failed += M2ActionFilterChecks.Failures;

            _failed += M2SecurityChecks.Run();
            _passed += M2SecurityChecks.Pass;

            _failed += await M2CommandChecks.RunAsync();
            _passed += M2CommandChecks.Pass;

            // M2a 安全前置的 HTTP/WS 端到端（C68/C69/C70/C72）：
            // 每个用例一个独立宿主 + 独立配置 + 随机端口，收尾各自 StopAsync。
            // 与 M2SecurityChecks 互补——那里证明判据对，这里证明判据真的接在 HTTP/WS 路径上。
            _failed += await M2HttpChecks.RunAsync();
            _passed += M2HttpChecks.Pass;

            // M2a 终审 P0 回归（pageVersion 基线 / ArrayIndex / 配置对象身份 / caps 只读态）
            M2RegressionChecks.Run();
            _passed += M2RegressionChecks.Pass;
            _failed += M2RegressionChecks.Failures;

            Console.WriteLine();
            Console.WriteLine(
                $"--- M2 汇总：动作过滤 {M2ActionFilterChecks.Pass}/{M2ActionFilterChecks.Total}"
                + $"、安全 {M2SecurityChecks.Pass}/{M2SecurityChecks.Total}"
                + $"、命令 {M2CommandChecks.Pass}/{M2CommandChecks.Total}"
                + $"、HTTP 端到端 {M2HttpChecks.Pass}/{M2HttpChecks.Total}"
                + $"、回归 {M2RegressionChecks.Pass}/{M2RegressionChecks.Total}"
                + $"；本进程累计 {_passed} PASS / {_failed} FAIL ---");

            return Finish();
        }

        // ---------------- 辅助 ----------------

        private sealed record Seed(string TypeKey, string Name, object Value, string Property);

        private sealed record TicketIssue(int Status, string Body, JObject? Json)
        {
            public string? Ticket => Json?.Value<string>("ticket");

            /// <summary>响应里的会话令牌（首次签发时通常没有，要等 ticket 核销后经 hello 下发）</summary>
            public string? SessionToken => Json?.Value<string>("sessionToken") ?? Json?.Value<string>("refreshToken");
        }

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

        /// <summary>
        /// 读消息里的基座页版本（<b>只认显式字段</b>）：消息顶层或 <c>page</c> 里的
        /// <c>basePageVersion</c>。刻意不回退到嵌套 <c>BasePage.PageVersion</c>——
        /// M1c 要的是协议字段，不是"快照里恰好能看到基座"。
        /// </summary>
        private static int? ReadBasePageVersion(JObject? message)
        {
            if (message == null) return null;

            var top = Pick(message, "basePageVersion", "BasePageVersion");
            if (top != null && top.Type != JTokenType.Null) return top.Value<int?>();

            var page = Pick(message, "page", "Page");
            var inner = Pick(page, "basePageVersion", "BasePageVersion");
            if (inner != null && inner.Type != JTokenType.Null) return inner.Value<int?>();

            return null;
        }

        /// <summary>诊断用：嵌套 <c>page.BasePage.PageVersion</c>（当前 M1b 快照有这一层）</summary>
        private static int? ReadNestedBasePageVersion(JObject? message)
        {
            var page = message == null ? null : Pick(message, "page", "Page");
            var baseNode = page == null ? null : Pick(page, "BasePage", "basePage");
            return baseNode == null ? null : Pick(baseNode, "PageVersion", "pageVersion")?.Value<int?>();
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

        private static async Task<TicketIssue> IssueTicketAsync(HttpClient http, string baseUrl)
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(baseUrl + "/api/ticket", content);
            var body = await response.Content.ReadAsStringAsync();
            return new TicketIssue((int)response.StatusCode, body, TryJson(body));
        }

        private static async Task<(int Status, string Body, JObject? Json)> RefreshTicketAsync(
            HttpClient http,
            string baseUrl,
            string? refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                return (0, string.Empty, null);

            // 协议：POST /api/ticket {"refreshToken":"…"} —— 同一个入口按请求体分流
            var payload = new JObject { ["refreshToken"] = refreshToken }.ToString(Formatting.None);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(baseUrl + "/api/ticket", content);
            var body = await response.Content.ReadAsStringAsync();
            return ((int)response.StatusCode, body, TryJson(body));
        }

        private static async Task<(int Status, string ContentType, string Body)> GetAsync(HttpClient http, string url)
        {
            using var response = await http.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            return ((int)response.StatusCode, response.Content.Headers.ContentType?.ToString() ?? string.Empty, body);
        }

        /// <summary>按<b>字节</b>取静态资源（长度/逐字节比对必须用字节，不能用解码后的字符串长度）</summary>
        private static async Task<(int Status, string ContentType, byte[] Body)> GetBytesAsync(HttpClient http, string url)
        {
            using var response = await http.GetAsync(url);
            var body = await response.Content.ReadAsByteArrayAsync();
            return ((int)response.StatusCode, response.Content.Headers.ContentType?.ToString() ?? string.Empty, body);
        }

        /// <summary>
        /// 找"当前源文件"所在的 wwwroot：从输出目录逐级向上，命中
        /// <c>&lt;根&gt;\Web\VisionMaster.ScadaWeb\wwwroot</c>（从仓库根跑）或
        /// <c>&lt;根&gt;\VisionMaster.ScadaWeb\wwwroot</c>（从 <c>Web\</c> 下跑）。
        /// 找不到返回 null —— 调用方把断言判红并打印查找起点，<b>不跳过</b>（R38）。
        /// </summary>
        private static string? FindSourceWwwRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);

            while (dir != null)
            {
                foreach (var candidate in new[]
                         {
                             Path.Combine(dir.FullName, "Web", "VisionMaster.ScadaWeb", "wwwroot"),
                             Path.Combine(dir.FullName, "VisionMaster.ScadaWeb", "wwwroot"),
                         })
                {
                    if (Directory.Exists(candidate))
                        return candidate;
                }

                dir = dir.Parent;
            }

            return null;
        }

        /// <summary>
        /// 取一条静态资源并核对：状态码 / Content-Type / 字节数与<b>源文件</b>一致（逐字节也比一次）。
        /// 失败明细里必须带"实得字节 / 期望字节 / 源文件路径"，否则"旧 wwwroot"这类问题无从定位。
        /// </summary>
        private static async Task<(bool Ok, string Detail)> CheckStaticAssetAsync(
            HttpClient http,
            string baseUrl,
            string? sourceRoot,
            string fileName,
            bool expectJavascript,
            bool expectCss)
        {
            var (status, type, body) = await GetBytesAsync(http, baseUrl + "/" + fileName);

            var sourcePath = sourceRoot == null ? null : Path.Combine(sourceRoot, fileName);
            byte[]? sourceBytes = null;
            var sourceError = string.Empty;

            if (sourcePath != null && File.Exists(sourcePath))
            {
                try { sourceBytes = File.ReadAllBytes(sourcePath); }
                catch (Exception ex) { sourceError = $"{ex.GetType().Name}: {FirstLine(ex.Message)}"; }
            }

            var expectedLength = sourceBytes?.LongLength;
            var lengthOk = expectedLength.HasValue && body.LongLength == expectedLength.Value;
            var bytesOk = sourceBytes != null && body.AsSpan().SequenceEqual(sourceBytes);
            var typeOk = (!expectJavascript || type.Contains("javascript", StringComparison.OrdinalIgnoreCase))
                         && (!expectCss || type.Contains("text/css", StringComparison.OrdinalIgnoreCase));

            var ok = status == 200 && typeOk && lengthOk && bytesOk;

            return (ok,
                $"{fileName} status={status} type={type}"
                + $" 实得字节={body.LongLength}"
                + $" 期望字节={expectedLength?.ToString(CultureInfo.InvariantCulture) ?? "<源文件缺失>"}"
                + $" 逐字节一致={bytesOk}"
                + $" 源文件={sourcePath ?? "<源目录未找到，起点 " + AppContext.BaseDirectory + ">"}"
                + (sourceError.Length == 0 ? string.Empty : $" 读源失败={sourceError}"));
        }

        /// <summary>
        /// 宿主实际服务的那份 wwwroot（与 <c>ScadaWebHost.ResolveStaticFile</c> 的候选根顺序一致：
        /// 先 <c>Web\wwwroot</c>，再输出根的 <c>wwwroot</c>）里，7 个静态文件是否与源文件逐字节一致。
        ///
        /// 这条与 B4b~B4d 互补：B4b~B4d 证明"HTTP 吐出来的字节对"，这里证明"暂存目录本身不是旧的"，
        /// 两者任一为旧都会在现场表现为"改了 JS/CSS 但页面没变"。
        /// </summary>
        private static (bool Ok, string Detail) CheckStagedStaticFiles(string? sourceRoot)
        {
            var baseDir = AppContext.BaseDirectory;
            var productRoot = Path.Combine(baseDir, "Web", "wwwroot");
            var selfRoot = Path.Combine(baseDir, "wwwroot");
            var servedRoot = Directory.Exists(productRoot) ? productRoot
                : Directory.Exists(selfRoot) ? selfRoot
                : null;

            if (servedRoot == null)
                return (false, $"输出目录下没有 wwwroot（起点 {baseDir}）");

            var files = new[]
            {
                "index.html", "app.js", "styles.css",
                "elements-process.js", "elements-process.css",
                "elements-interactive.js", "elements-interactive.css",
            };

            var mismatched = new List<string>();
            var sourceMissing = new List<string>();

            foreach (var file in files)
            {
                var sourcePath = sourceRoot == null ? null : Path.Combine(sourceRoot, file);
                var stagedPath = Path.Combine(servedRoot, file);

                if (sourcePath == null || !File.Exists(sourcePath))
                {
                    sourceMissing.Add(file);
                    continue;
                }

                if (!File.Exists(stagedPath))
                {
                    mismatched.Add($"{file}: 暂存缺失（期望 {new FileInfo(sourcePath).Length} 字节，源 {sourcePath}）");
                    continue;
                }

                var expected = File.ReadAllBytes(sourcePath);
                var actual = File.ReadAllBytes(stagedPath);

                if (!actual.AsSpan().SequenceEqual(expected))
                {
                    mismatched.Add($"{file}: 实得 {actual.LongLength} 字节 / 期望 {expected.LongLength} 字节"
                                   + $"（暂存 {stagedPath}，源 {sourcePath}）");
                }
            }

            var ok = mismatched.Count == 0 && sourceMissing.Count == 0;

            return (ok,
                $"servedRoot={servedRoot} 源目录={sourceRoot ?? "<未找到>"}"
                + $" 不一致=[{(mismatched.Count == 0 ? "<无>" : string.Join("；", mismatched))}]"
                + $" 源缺失=[{(sourceMissing.Count == 0 ? "<无>" : string.Join("；", sourceMissing))}]");
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
                return (false, $"{ex.GetType().Name}: {FirstLine(ex.Message)}", null);
            }
        }

        /// <summary>尝试连接并期望被拒；返回 HTTP 状态码（从 .NET 的异常文案里解析）</summary>
        private static async Task<(bool Rejected, int? Status, string Detail)> ConnectExpectingRejectionAsync(string url)
        {
            using var socket = new ClientWebSocket();
            try
            {
                await socket.ConnectAsync(new Uri(url), CancellationToken.None);

                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "unexpected", CancellationToken.None); }
                catch { /* 对端已断开 */ }

                return (false, null, "连接意外成功（旧 ticket 仍可复用）");
            }
            catch (Exception ex)
            {
                var text = ex.ToString();
                return (true, TryParseStatusCode(text), $"{ex.GetType().Name}: {FirstLine(ex.Message)}");
            }
        }

        private static async Task<(bool Ok, string Detail)> ConnectAndHelloAsync(string url)
        {
            using var socket = new ClientWebSocket();
            try
            {
                await socket.ConnectAsync(new Uri(url), CancellationToken.None);
                var hello = await ReceiveUntilAsync(socket, m => TypeOf(m) == "hello", 3000);

                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                catch { /* 对端已断开 */ }

                return (hello != null, hello == null
                    ? "连上了但 3s 内没收到 hello"
                    : Truncate(hello.ToString(Formatting.None), 160));
            }
            catch (Exception ex)
            {
                return (false, $"{ex.GetType().Name}: {FirstLine(ex.Message)}");
            }
        }

        private static int? TryParseStatusCode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            var match = Regex.Match(text, @"status code '(\d{3})'", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed))
                return parsed;

            match = Regex.Match(text, @"\b(401|403)\b");
            return match.Success && int.TryParse(match.Groups[1].Value, out parsed) ? parsed : null;
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

        /// <summary>等对端关闭帧，返回关闭码（超时/异常返回 null）。用于 B43 验 4403。</summary>
        /// <summary>
        /// B51：/img 图像通道。独立宿主 + 一个 ImageView（Source 绑定到 2x2 红色位图），
        /// 用 hello.imageToken 取图，断言 PNG 字节与坏 token 的 401。
        /// </summary>
        private static async Task RunImageChecksAsync(HttpClient http)
        {
            var port = GetFreePort();
            var document = new ScadaDocument();
            var page = document.AddPage("图像页");
            page.Version = 1;

            var values = new FakeValueSource();
            var handle = values.Add(Guid.NewGuid(), "img", CreateTestBitmap());
            var element = new ScadaElement
            {
                TypeKey = "Hmi.ImageView",
                Name = "图像",
                X = 10,
                Y = 10,
                Width = 120,
                Height = 80
            };
            element.Bindings.Add(new ScadaBinding
            {
                TargetProperty = "Source",
                VariableId = handle.VariableId,
                VariableName = handle.Name,
                IsEnabled = true
            });
            page.Elements.Add(element);
            document.SetStartupPage(page);

            var runtime = new ScadaRuntime(document);
            runtime.Start();

            var hub = new FakeRunHub();
            hub.SetRunning(runtime, document, 1);

            var settings = new ScadaWebSettings
            {
                Enabled = true,
                Port = port,
                BindAddress = "127.0.0.1",
                AllowAnonymousView = true,
                MaxSessions = 4
            };

            var host = new ScadaWebHost(
                hub,
                authenticator: null,
                catalog: null,
                new FakeLog(),
                settings,
                values);

            host.Start();

            if (!host.IsListening)
            {
                Check("B51", false, $"图像宿主未启动：{host.LastError ?? "<none>"}");
                return;
            }

            var baseUrl = $"http://127.0.0.1:{port}";

            try
            {
                var issue = await IssueTicketAsync(http, baseUrl);
                var (connected, connectError, socket) = await ConnectAsync($"{baseUrl.Replace("http", "ws")}/ws?ticket={issue.Ticket}");

                if (!connected || socket == null)
                {
                    Check("B51", false, $"图像宿主 WS 连接失败：{connectError ?? "<none>"}");
                    return;
                }

                using (socket)
                {
                    var hello = await ReceiveUntilAsync(socket, m => TypeOf(m) == "hello", 3000);
                    var token = hello?.Value<string>("imageToken");
                    var elementId = element.ElementId.ToString("D");
                    var url = $"{baseUrl}/img?elementId={elementId}&version=1&t={Uri.EscapeDataString(token ?? "")}";
                    var (status, contentType, bytes) = await GetBytesAsync(http, url);

                    var pngMagic = bytes.Length >= 8
                                   && bytes[0] == 0x89 && bytes[1] == 0x50
                                   && bytes[2] == 0x4E && bytes[3] == 0x47;

                    Check("B51",
                        !string.IsNullOrWhiteSpace(token)
                        && status == 200
                        && contentType.Contains("image/png", StringComparison.OrdinalIgnoreCase)
                        && pngMagic
                        && bytes.Length > 8,
                        $"token={(string.IsNullOrWhiteSpace(token) ? "<缺失>" : Short(token))} "
                        + $"status={status} type={contentType} bytes={bytes.Length} png={pngMagic}");

                    var (badStatus, _, _) = await GetBytesAsync(
                        http, $"{baseUrl}/img?elementId={elementId}&version=1&t=not-a-token");

                    Check("B51b", badStatus == 401, $"badTokenStatus={badStatus}（期望 401）");
                }
            }
            finally
            {
                await host.StopAsync();
            }
        }

        /// <summary>
        /// B52：轴面板只读数据。独立宿主 + 假 IScadaAxisSource，断言 axis 消息里的
        /// 轴名/位置/点位表，以及"没有命令字段"（A 范围）。
        /// </summary>
        private static async Task RunAxisChecksAsync(HttpClient http)
        {
            var port = GetFreePort();
            var axis = new FakeAxisSource();

            var document = new ScadaDocument();
            var page = document.AddPage("轴页");
            page.Version = 1;

            var element = new ScadaElement
            {
                TypeKey = "Hmi.AxisPanel",
                Name = "轴面板",
                X = 10,
                Y = 10,
                Width = 280,
                Height = 280
            };
            element.Properties[ScadaAxisOptionsCodec.Key] =
                ScadaAxisOptionsCodec.Format(new[] { new ScadaAxisRef(axis.AxisId, "X") });
            page.Elements.Add(element);
            document.SetStartupPage(page);

            var runtime = new ScadaRuntime(document);
            runtime.Start();

            var hub = new FakeRunHub();
            hub.SetRunning(runtime, document, 1);

            var settings = new ScadaWebSettings
            {
                Enabled = true,
                Port = port,
                BindAddress = "127.0.0.1",
                AllowAnonymousView = true,
                MaxSessions = 4
            };

            var host = new ScadaWebHost(
                hub,
                authenticator: null,
                catalog: null,
                new FakeLog(),
                settings,
                values: null,
                axisSource: axis);

            host.Start();

            if (!host.IsListening)
            {
                Check("B52", false, $"轴宿主未启动：{host.LastError ?? "<none>"}");
                return;
            }

            var baseUrl = $"http://127.0.0.1:{port}";

            try
            {
                var issue = await IssueTicketAsync(http, baseUrl);
                var (connected, connectError, socket) = await ConnectAsync($"{baseUrl.Replace("http", "ws")}/ws?ticket={issue.Ticket}");

                if (!connected || socket == null)
                {
                    Check("B52", false, $"轴宿主 WS 连接失败：{connectError ?? "<none>"}");
                    return;
                }

                using (socket)
                {
                    await ReceiveUntilAsync(socket, m => TypeOf(m) == "hello", 3000);
                    await ReceiveUntilAsync(socket, m => TypeOf(m) == "snapshot", 5000);

                    var axisMessage = await ReceiveUntilAsync(socket, m => TypeOf(m) == "axis", 5000);
                    var axes = axisMessage?["axes"] as JArray;
                    var firstAxis = axes?.FirstOrDefault() as JObject;
                    var points = axisMessage?["points"] as JObject;
                    var pointRows = firstAxis == null ? null : points?[firstAxis.Value<string>("axisId")] as JArray;
                    var firstPoint = pointRows?.FirstOrDefault() as JObject;

                    var noCommands = axisMessage?["commands"] == null
                                     && axisMessage?["canOperate"] == null;

                    Check("B52",
                        axisMessage != null
                        && firstAxis?.Value<string>("name") == "X"
                        && Math.Abs((firstAxis.Value<double?>("position") ?? 0) - 12.345) < 1e-6
                        && firstAxis.Value<bool?>("valid") == true
                        && firstPoint?.Value<int?>("index") == 0
                        && noCommands,
                        axisMessage == null
                            ? "未收到 axis"
                            : $"axes={axes?.Count} name={firstAxis?.Value<string>("name")} "
                              + $"pos={firstAxis?.Value<double?>("position")} "
                              + $"points={pointRows?.Count} noCommands={noCommands}");
                }
            }
            finally
            {
                await host.StopAsync();
            }
        }

        /// <summary>
        /// B53：DataGrid 行数据。独立宿主 + 假 IScadaRuntimeDataProvider，
        /// 断言 grid 消息里的列/行/errorCell/recorded。
        /// </summary>
        private static async Task RunGridChecksAsync(HttpClient http)
        {
            var port = GetFreePort();
            var document = new ScadaDocument();
            var page = document.AddPage("表格页");
            page.Version = 1;

            var element = new ScadaElement
            {
                TypeKey = "Hmi.DataGrid",
                Name = "数据表格",
                X = 10,
                Y = 10,
                Width = 360,
                Height = 240
            };
            page.Elements.Add(element);
            document.SetStartupPage(page);

            var runtime = new ScadaRuntime(document);
            runtime.Start();

            var hub = new FakeRunHub();
            hub.SetRunning(runtime, document, 1);

            var provider = new FakeRuntimeDataProvider { ElementId = element.ElementId };

            var settings = new ScadaWebSettings
            {
                Enabled = true,
                Port = port,
                BindAddress = "127.0.0.1",
                AllowAnonymousView = true,
                MaxSessions = 4
            };

            var host = new ScadaWebHost(
                hub,
                authenticator: null,
                catalog: null,
                new FakeLog(),
                settings,
                values: null,
                dataProvider: provider);

            host.Start();

            if (!host.IsListening)
            {
                Check("B53", false, $"表格宿主未启动：{host.LastError ?? "<none>"}");
                return;
            }

            var baseUrl = $"http://127.0.0.1:{port}";

            try
            {
                var issue = await IssueTicketAsync(http, baseUrl);
                var (connected, connectError, socket) = await ConnectAsync($"{baseUrl.Replace("http", "ws")}/ws?ticket={issue.Ticket}");

                if (!connected || socket == null)
                {
                    Check("B53", false, $"表格宿主 WS 连接失败：{connectError ?? "<none>"}");
                    return;
                }

                using (socket)
                {
                    await ReceiveUntilAsync(socket, m => TypeOf(m) == "hello", 3000);
                    await ReceiveUntilAsync(socket, m => TypeOf(m) == "snapshot", 5000);

                    var gridMessage = await ReceiveUntilAsync(socket, m => TypeOf(m) == "grid", 6000);
                    var elements = gridMessage?["elements"] as JArray;
                    var first = elements?.FirstOrDefault() as JObject;
                    var columns = first?["columns"] as JArray;
                    var rows = first?["rows"] as JArray;
                    var secondRow = rows?.Count > 1 ? rows[1] as JObject : null;

                    Check("B53",
                        gridMessage != null
                        && columns?.Count == 2
                        && rows?.Count == 2
                        && secondRow?.Value<int?>("errorCell") == 1
                        && first?.Value<int?>("recorded") == 2,
                        gridMessage == null
                            ? "未收到 grid"
                            : $"columns={columns?.Count} rows={rows?.Count} "
                              + $"errorCell={secondRow?.Value<int?>("errorCell")} "
                              + $"recorded={first?.Value<int?>("recorded")}");
                }
            }
            finally
            {
                await host.StopAsync();
            }
        }

        /// <summary>
        /// B56：24 槽的容量行为。MaxSessions=1 时：
        /// 第一个连接正常入册（admitted=1、handshake=0）；
        /// 第二个客户端仍能取票并通过握手（握手预留槽），但转活跃时被 1013 拒绝——
        /// 不是 HTTP 503（那会被前端渲染成"登录失败"）。
        /// </summary>
        private static async Task RunAdmissionHostChecksAsync(HttpClient http)
        {
            var port = GetFreePort();
            var document = new ScadaDocument();
            var page = document.AddPage("准入页");
            page.Version = 1;
            document.SetStartupPage(page);

            var runtime = new ScadaRuntime(document);
            runtime.Start();

            var hub = new FakeRunHub();
            hub.SetRunning(runtime, document, 1);

            var settings = new ScadaWebSettings
            {
                Enabled = true,
                Port = port,
                BindAddress = "127.0.0.1",
                AllowAnonymousView = true,
                MaxSessions = 1
            };

            var host = new ScadaWebHost(hub, authenticator: null, catalog: null, new FakeLog(), settings);
            host.Start();

            if (!host.IsListening)
            {
                Check("B56", false, $"准入宿主未启动：{host.LastError ?? "<none>"}");
                Check("B56b", false, $"准入宿主未启动：{host.LastError ?? "<none>"}");
                return;
            }

            var baseUrl = $"http://127.0.0.1:{port}";
            var wsBase = $"ws://127.0.0.1:{port}";

            try
            {
                var firstIssue = await IssueTicketAsync(http, baseUrl);
                var (firstConnected, firstError, firstSocket) =
                    await ConnectAsync($"{wsBase}/ws?ticket={firstIssue.Ticket}");

                if (!firstConnected || firstSocket == null)
                {
                    Check("B56", false, $"首连接失败 err={firstError ?? "<none>"}");
                    Check("B56b", false, $"首连接失败 err={firstError ?? "<none>"}");
                    return;
                }

                int? secondCloseCode = null;
                var secondHandshake = false;

                using (firstSocket)
                {
                    await ReceiveUntilAsync(firstSocket, m => TypeOf(m) == "hello", 3000);
                    await ReceiveUntilAsync(firstSocket, m => TypeOf(m) == "snapshot", 5000);

                    var admitted = host.AdmittedCount;
                    var handshake = host.HandshakeCount;

                    var secondIssue = await IssueTicketAsync(http, baseUrl);
                    var (secondConnected, secondError, secondSocket) =
                        await ConnectAsync($"{wsBase}/ws?ticket={secondIssue.Ticket}");

                    secondHandshake = secondConnected;

                    if (secondSocket != null)
                    {
                        using (secondSocket)
                            secondCloseCode = await WaitForCloseCodeAsync(secondSocket, 5000);
                    }
                    else
                    {
                        _ = secondError;
                    }

                    Check("B56",
                        admitted == 1 && handshake == 0
                        && secondHandshake && secondCloseCode == 1013,
                        $"admitted={admitted} handshake={handshake} "
                        + $"secondHandshake={secondHandshake} "
                        + $"closeCode={secondCloseCode?.ToString() ?? "<none>"}（期望 1013）");
                }

                var settled = await WaitUntilAsync(
                    () => host.AdmittedCount == 0 && host.HandshakeCount == 0, 3000);

                Check("B56b", settled,
                    $"admitted={host.AdmittedCount} handshake={host.HandshakeCount}");
            }
            finally
            {
                await host.StopAsync();
            }
        }

        /// <summary>
        /// B57：默认值只有一个事实源（/api/catalog 的 Properties[].DefaultValue）。
        /// ① 服务端把默认值原样发出来（空串 = 没默认，客户端不补）；
        /// ② app.js 的补键函数必须在 createRecord 之前执行（否则渲染器已经读了字面回退）。
        /// </summary>
        private static async Task RunCatalogChecksAsync(HttpClient http)
        {
            var port = GetFreePort();
            var settings = new ScadaWebSettings
            {
                Enabled = true,
                Port = port,
                BindAddress = "127.0.0.1",
                AllowAnonymousView = true,
                MaxSessions = 2
            };

            var host = new ScadaWebHost(
                hub: null,
                authenticator: null,
                catalog: new FakeRenderCatalog(),
                log: new FakeLog(),
                settings: settings);

            host.Start();

            if (!host.IsListening)
            {
                Check("B57", false, $"目录宿主未启动：{host.LastError ?? "<none>"}");
                return;
            }

            try
            {
                var (status, _, body) = await GetAsync(http, $"http://127.0.0.1:{port}/api/catalog");
                var json = TryJson(body);
                var items = json?["items"] as JArray;
                var first = items?.FirstOrDefault() as JObject;
                var props = first?["Properties"] as JArray;
                var format = props?.FirstOrDefault() as JObject;
                var empty = props?.Count > 1 ? props[1] as JObject : null;

                Check("B57",
                    status == 200
                    && first?.Value<string>("TypeKey") == "Hmi.Text"
                    && format?.Value<string>("Key") == "Format"
                    && format.Value<string>("DefaultValue") == "HH:mm:ss"
                    && empty?.Value<string>("DefaultValue") == string.Empty,
                    $"status={status} type={first?.Value<string>("TypeKey")} "
                    + $"format={format?.Value<string>("DefaultValue")} "
                    + $"empty={(empty == null ? "<缺失>" : "\"" + empty.Value<string>("DefaultValue") + "\"")}");
            }
            finally
            {
                await host.StopAsync();
            }

            // 静态闸：补键必须在 createRecord 之前（函数体位置只作"先后"判据，不钉行号）。
            var root = FindSourceWwwRoot();
            var appJs = root == null ? string.Empty : Path.Combine(root, "app.js");
            var text = File.Exists(appJs) ? File.ReadAllText(appJs) : string.Empty;
            var normalizeAt = text.IndexOf("applyCatalogDefaults(el.Properties, el.TypeKey)", StringComparison.Ordinal);
            var createAt = text.IndexOf("function createRecord(", StringComparison.Ordinal);
            var loadAt = text.IndexOf("loadCatalog();", StringComparison.Ordinal);

            Check("B57b",
                normalizeAt > 0 && createAt > normalizeAt && loadAt > 0,
                $"appJs={(appJs == null ? "<未找到>" : appJs)} normalizeAt={normalizeAt} "
                + $"createAt={createAt} loadAt={loadAt}");

            // 客户端陈旧快照闸 / 致命态 / 值帧基线：这些是纯 JS 状态机，没有 DOM 时只能静态钉住关键分支。
            var staleGuard = text.Contains("structureSeq <= state.structureSeq", StringComparison.Ordinal)
                             && text.Contains("generation <= state.stoppedGeneration", StringComparison.Ordinal);
            var fatalGuard = text.Contains("E_SNAPSHOT_TOO_LARGE", StringComparison.Ordinal)
                             && text.Contains("code === 1009", StringComparison.Ordinal)
                             && text.Contains("function enterFatal(", StringComparison.Ordinal)
                             && text.Contains("if (state.fatal)", StringComparison.Ordinal);
            var frameBaseline = text.Contains("state.lastReceivedFrameSeq", StringComparison.Ordinal);

            Check("B62",
                staleGuard && fatalGuard && frameBaseline,
                $"staleGuard={staleGuard} fatalGuard={fatalGuard} frameBaseline={frameBaseline}");
        }

        /// <summary>
        /// B58-B61：结构序号与 view.resync。
        /// 用主宿主（运行中）单独开一条连接：
        /// ① 改页版本 → page 的 structureSeq 必须更大；
        /// ② scope=frames → ack + 下一帧 keyframe；
        /// ③ scope=structure → ack + 新 snapshot，structureSeq 继续增大；
        /// ④ 紧接着第二次 structure → E_RATE_LIMITED + retryAfterMs（不产生第二份快照）。
        /// </summary>
        private static async Task RunResyncChecksAsync(
            HttpClient http,
            string baseUrl,
            string wsBase,
            ScadaPage page,
            FakeRunHub hub)
        {
            var issue = await IssueTicketAsync(http, baseUrl);
            var (connected, error, socket) = await ConnectAsync($"{wsBase}/ws?ticket={issue.Ticket}");

            if (!connected || socket == null)
            {
                Check("B58", false, $"连接失败 err={error ?? "<none>"}");
                Check("B59", false, $"连接失败 err={error ?? "<none>"}");
                Check("B60", false, $"连接失败 err={error ?? "<none>"}");
                Check("B61", false, $"连接失败 err={error ?? "<none>"}");
                Check("B63", false, $"连接失败 err={error ?? "<none>"}");
                Check("B64", false, $"连接失败 err={error ?? "<none>"}");
                Check("B65", false, $"连接失败 err={error ?? "<none>"}");
                return;
            }

            using (socket)
            {
                // 首帧必须是 hello：客户端在 hello 里归零 structureSeq/generation，
                // 任何 presence/snapshot 抢先都会让这条前提失效。
                var firstText = await ReceiveTextAsync(socket, 3000);
                var first = TryJson(firstText);
                Check("B65",
                    TypeOf(first) == "hello"
                    && first?["runGeneration"] != null
                    && first["structureSeq"] != null,
                    first == null
                        ? "未收到首帧"
                        : $"firstType={TypeOf(first)} "
                          + $"gen={first["runGeneration"]} seq={first["structureSeq"]}");

                var snapshot = await ReceiveUntilAsync(socket, m => TypeOf(m) == "snapshot", 8000);
                var snapshotSeq = snapshot?.Value<long?>("structureSeq") ?? 0;

                // ① 结构变更 → page
                page.Version = page.Version + 1;
                var pageMessage = await ReceiveUntilAsync(socket, m => TypeOf(m) == "page", 5000);
                var pageSeq = pageMessage?.Value<long?>("structureSeq") ?? 0;

                Check("B58",
                    snapshotSeq > 0 && pageSeq > snapshotSeq,
                    $"snapshotSeq={snapshotSeq} pageSeq={pageSeq}");

                // ② frames：只要求下一帧 keyframe
                var framesId = Guid.NewGuid().ToString("N");
                await SendTextAsync(socket, JsonConvert.SerializeObject(new
                {
                    type = "view.resync",
                    id = framesId,
                    scope = "frames",
                    reason = "test"
                }));

                var framesAck = await ReceiveUntilAsync(
                    socket,
                    m => TypeOf(m) == "ack" && m.Value<string>("id") == framesId,
                    3000);
                var keyframe = await ReceiveUntilAsync(
                    socket,
                    m => TypeOf(m) == "frame" && m.Value<bool?>("keyframe") == true,
                    5000);

                Check("B59",
                    framesAck?.Value<string>("kind") == "view.resync"
                    && framesAck?.Value<bool?>("ok") == true
                    && keyframe != null,
                    framesAck == null
                        ? "未收到 resync ack"
                        : $"ackOk={framesAck.Value<bool?>("ok")} keyframe={keyframe != null}");

                // ③ structure：等过 1s 限速窗口，再要整份结构
                await Task.Delay(1100);

                var structureId = Guid.NewGuid().ToString("N");
                await SendTextAsync(socket, JsonConvert.SerializeObject(new
                {
                    type = "view.resync",
                    id = structureId,
                    scope = "structure",
                    reason = "test"
                }));

                var structureAck = await ReceiveUntilAsync(
                    socket,
                    m => TypeOf(m) == "ack" && m.Value<string>("id") == structureId,
                    3000);
                var resynced = await ReceiveUntilAsync(socket, m => TypeOf(m) == "snapshot", 5000);
                var resyncedSeq = resynced?.Value<long?>("structureSeq") ?? 0;

                Check("B60",
                    structureAck?.Value<bool?>("ok") == true
                    && resynced != null
                    && resyncedSeq > pageSeq,
                    $"ackOk={structureAck?.Value<bool?>("ok")} "
                    + $"pageSeq={pageSeq} resyncedSeq={resyncedSeq}");

                // ④ 紧接着第二次 structure：限速
                var rateId = Guid.NewGuid().ToString("N");
                await SendTextAsync(socket, JsonConvert.SerializeObject(new
                {
                    type = "view.resync",
                    id = rateId,
                    scope = "structure",
                    reason = "test-rate"
                }));

                var rateError = await ReceiveUntilAsync(
                    socket,
                    m => TypeOf(m) == "error" && m.Value<string>("id") == rateId,
                    3000);

                Check("B61",
                    rateError?.Value<string>("kind") == "view.resync"
                    && rateError?.Value<string>("code") == "E_RATE_LIMITED"
                    && (rateError.Value<int?>("retryAfterMs") ?? 0) > 0,
                    rateError == null
                        ? "未收到 E_RATE_LIMITED"
                        : $"code={rateError.Value<string>("code")} "
                          + $"retryAfterMs={rateError.Value<int?>("retryAfterMs")}");

                // ⑤ 未运行：structure resync 回 idle；frames resync 明确 ack.idle=true + idle
                hub.Stop();
                await Task.Delay(1100);   // 等过 B60/B61 的 1s resync 限速窗口

                var idleId = Guid.NewGuid().ToString("N");
                await SendTextAsync(socket, JsonConvert.SerializeObject(new
                {
                    type = "view.resync",
                    id = idleId,
                    scope = "structure",
                    reason = "test-idle"
                }));

                var idleAck = await ReceiveUntilAsync(
                    socket,
                    m => TypeOf(m) == "ack" && m.Value<string>("id") == idleId,
                    3000);
                var idleMessage = await ReceiveUntilAsync(socket, m => TypeOf(m) == "idle", 3000);

                Check("B63",
                    idleAck?.Value<bool?>("ok") == true && idleMessage != null,
                    $"ackOk={idleAck?.Value<bool?>("ok")} idle={idleMessage != null}");

                await Task.Delay(1100);   // B64 也要一个干净的限速窗口

                var framesIdleId = Guid.NewGuid().ToString("N");
                await SendTextAsync(socket, JsonConvert.SerializeObject(new
                {
                    type = "view.resync",
                    id = framesIdleId,
                    scope = "frames",
                    reason = "test-frames-idle"
                }));

                var framesIdleAck = await ReceiveUntilAsync(
                    socket,
                    m => TypeOf(m) == "ack" && m.Value<string>("id") == framesIdleId,
                    3000);
                var framesIdleMessage = await ReceiveUntilAsync(socket, m => TypeOf(m) == "idle", 3000);

                Check("B64",
                    framesIdleAck?["result"]?["idle"]?.Value<bool?>() == true
                    && framesIdleMessage != null,
                    framesIdleAck == null
                        ? "未收到 frames ack"
                        : $"ackIdle={framesIdleAck["result"]?["idle"]} "
                          + $"idle={framesIdleMessage != null}");
            }
        }

        private sealed class FakeRenderCatalog : IScadaRenderCatalog
        {
            public IReadOnlyList<ScadaElementDescriptorInfo> All { get; } = new[]
            {
                new ScadaElementDescriptorInfo
                {
                    TypeKey = "Hmi.Text",
                    DisplayName = "文本",
                    Properties = new[]
                    {
                        new ScadaElementPropertyInfo
                        {
                            Key = "Format",
                            DisplayName = "格式",
                            DefaultValue = "HH:mm:ss"
                        },
                        new ScadaElementPropertyInfo
                        {
                            Key = "Empty",
                            DisplayName = "空默认",
                            DefaultValue = string.Empty
                        }
                    }
                }
            };
        }

        private sealed class FakeRuntimeDataProvider : IScadaRuntimeDataProvider
        {
            public Guid ElementId { get; set; }

            public bool TryGetGrid(Guid elementId, out ScadaGridSnapshot snapshot)
            {
                if (elementId != ElementId)
                {
                    snapshot = null!;
                    return false;
                }

                snapshot = new ScadaGridSnapshot
                {
                    Columns = new[]
                    {
                        new ScadaGridColumn("时间", "time", 80, "left", string.Empty, true),
                        new ScadaGridColumn("角度", "angle", 0, "right", "F2", true)
                    },
                    Rows = new[]
                    {
                        new ScadaGridRow(new[] { "08:00:00", "12.34" }, -1, DateTime.Now),
                        new ScadaGridRow(new[] { "08:00:01", "?" }, 1, DateTime.Now)
                    },
                    RecordedCount = 2
                };

                return true;
            }
        }

        private sealed class FakeAxisSource : IScadaAxisSource
        {
            public Guid AxisId { get; } = Guid.NewGuid();

            public int Version => 1;

            public event EventHandler? Changed
            {
                add { }
                remove { }
            }

            public bool TryGetInfo(Guid axisId, out ScadaAxisInfo info)
            {
                if (axisId == AxisId)
                {
                    info = new ScadaAxisInfo(axisId, "X", "虚拟卡", true, true);
                    return true;
                }

                info = null!;
                return false;
            }

            public bool TryGetSample(Guid axisId, out ScadaAxisSample sample)
            {
                if (axisId == AxisId)
                {
                    sample = new ScadaAxisSample(
                        true, null, 12.345, 6.7, true, false, true, false,
                        false, false, false, false, "运动中", DateTime.UtcNow);
                    return true;
                }

                sample = default;
                return false;
            }

            public bool TryGetPoints(Guid axisId, out IReadOnlyList<ScadaAxisPointInfo> points)
            {
                if (axisId == AxisId)
                {
                    points = new[] { new ScadaAxisPointInfo(0, "取料位", 10.5, 20) };
                    return true;
                }

                points = Array.Empty<ScadaAxisPointInfo>();
                return false;
            }
        }

        private static BitmapSource CreateTestBitmap()
        {
            // 2x2 BGRA 红色（B=0,G=0,R=255,A=255），冻结后跨线程安全
            var pixels = new byte[2 * 2 * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 0;
                pixels[i + 1] = 0;
                pixels[i + 2] = 255;
                pixels[i + 3] = 255;
            }

            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 2 * 4);
            bitmap.Freeze();
            return bitmap;
        }

        private static async Task<int?> WaitForCloseCodeAsync(ClientWebSocket socket, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var buffer = new byte[4096];

            try
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return result.CloseStatus.HasValue ? (int)result.CloseStatus.Value : null;
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

        // ---------------- M1 门槛用例的共用夹具 ----------------

        /// <summary>值帧里 (elementId, property=Text) 的 text 是否等于期望值</summary>
        private static bool FrameHasText(JObject message, string elementId, string expectedText)
            => TypeOf(message) == "frame"
               && string.Equals(
                      FindItem(Pick(message, "items", "Items") as JArray, elementId, "Text")?.Value<string>("text"),
                      expectedText,
                      StringComparison.Ordinal);

        /// <summary>是不是"指定页版本"的 page 消息</summary>
        private static bool IsPageMessage(JObject message, int expectedPageVersion)
            => TypeOf(message) == "page"
               && message.Value<int?>("pageVersion") == expectedPageVersion;

        private static double? ReadNumber(JToken? node, params string[] names)
            => Pick(node, names)?.Value<double?>();

        private static bool IsClose(double? actual, double expected)
            => actual.HasValue && Math.Abs(actual.Value - expected) < 1e-9;

        /// <summary>轮询等待一个条件成立（最多 <paramref name="timeoutMs"/>；用于"服务端会话数落下来"这类异步收尾）</summary>
        private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                await Task.Delay(25).ConfigureAwait(false);
            }

            return condition();
        }

        /// <summary>最近秩法（nearest-rank）：已升序的样本里取第 ceil(p·n) 个（1 起）</summary>
        private static double Percentile(List<double> sorted, double p)
        {
            if (sorted.Count == 0) return 0;

            var rank = (int)Math.Ceiling(p * sorted.Count) - 1;
            return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
        }

        /// <summary>
        /// 建一条"已握手、已拿到首屏 snapshot"的客户端（B20/B21/B23/B24/B36 共用）。
        /// 任一步失败都返回 <c>Ok=false</c> + 一句可直接上屏的原因，调用方据此 Check(false)。
        /// </summary>
        private static async Task<(bool Ok, string Detail, WsClient? Client)> OpenClientAsync(
            HttpClient http,
            string baseUrl,
            string wsBase,
            int snapshotTimeoutMs = 6000)
        {
            var issue = await IssueTicketAsync(http, baseUrl);
            var ticket = issue.Ticket;

            if (string.IsNullOrWhiteSpace(ticket))
                return (false, $"ticket 签发失败 status={issue.Status} body={Truncate(issue.Body, 120)}", null);

            var (connected, error, socket) = await ConnectAsync($"{wsBase}/ws?ticket={ticket}");

            if (!connected || socket == null)
                return (false, $"WS 握手失败 err={error ?? "<none>"}", null);

            var hello = await ReceiveUntilAsync(socket, m => TypeOf(m) == "hello", 3000);

            if (hello == null)
            {
                socket.Dispose();
                return (false, "连上了但 3s 内没收到 hello", null);
            }

            var snapshot = await ReceiveUntilAsync(socket, m => TypeOf(m) == "snapshot", snapshotTimeoutMs);

            if (snapshot == null)
            {
                socket.Dispose();
                return (false, $"hello 后 {snapshotTimeoutMs}ms 内没收到 snapshot（运行态未在跑？）", null);
            }

            return (true, "ok", new WsClient(socket, hello, snapshot));
        }

        /// <summary>
        /// B20：量 10Hz 节奏。连接一个新客户端 → 收 <paramref name="frameCount"/> 帧
        /// （跳过 page/pong 等非 frame 消息）→ 用到达时刻算相邻间隔。
        ///
        /// <b>不用固定 sleep 判"没收到"</b>：每一条都用带超时的 ReceiveAsync
        /// （<see cref="ReceiveTextAsync"/>），总期限按"帧数 × 250ms + 3s"给，超时即如实报收到几帧。
        /// </summary>
        private static async Task<(bool Ok, string Detail)> MeasureCadenceAsync(
            HttpClient http,
            string baseUrl,
            string wsBase,
            int frameCount)
        {
            var (opened, openDetail, client) = await OpenClientAsync(http, baseUrl, wsBase);

            if (!opened || client == null)
                return (false, $"建客户端失败：{openDetail}");

            using (client)
            {
                var arrivals = new List<DateTime>(frameCount);
                var sequences = new List<long>(frameCount);
                var skipped = 0;
                var budgetMs = frameCount * 250 + 3000;
                var deadline = DateTime.UtcNow.AddMilliseconds(budgetMs);

                while (arrivals.Count < frameCount && DateTime.UtcNow < deadline)
                {
                    var remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
                    var text = await ReceiveTextAsync(client.Socket, remaining);

                    if (text == null) break;

                    var json = TryJson(text);
                    if (json == null) continue;

                    if (TypeOf(json) != "frame")
                    {
                        skipped++;
                        continue;
                    }

                    arrivals.Add(DateTime.UtcNow);
                    sequences.Add(Pick(json, "seq", "Seq")?.Value<long?>() ?? -1);
                }

                await client.CloseAsync();

                if (arrivals.Count < frameCount)
                {
                    return (false,
                        $"只收到 {arrivals.Count}/{frameCount} 帧（跳过 {skipped} 条非 frame，期限 {budgetMs}ms）");
                }

                var intervals = new List<double>(arrivals.Count - 1);
                for (var i = 1; i < arrivals.Count; i++)
                    intervals.Add((arrivals[i] - arrivals[i - 1]).TotalMilliseconds);

                intervals.Sort();

                var median = Percentile(intervals, 0.50);
                var p95 = Percentile(intervals, 0.95);

                var monotonic = true;
                for (var i = 1; i < sequences.Count; i++)
                {
                    if (sequences[i] <= sequences[i - 1])
                    {
                        monotonic = false;
                        break;
                    }
                }

                var ok = median >= 70 && median <= 140 && p95 <= 200 && monotonic;

                return (ok,
                    $"frames={arrivals.Count} median={median:F1}ms p95={p95:F1}ms"
                    + $" min={intervals[0]:F1}ms max={intervals[^1]:F1}ms"
                    + $" seq={sequences[0]}..{sequences[^1]} 单调={monotonic}"
                    + $" 跳过非frame={skipped}");
            }
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

        private static string Short(string? value)
            => string.IsNullOrEmpty(value) ? "<null>" : value!.Substring(0, Math.Min(6, value.Length)) + "…";

        private static string Show(int? value)
            => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "<缺失>";

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var index = text.IndexOfAny(new[] { '\r', '\n' });
            return index < 0 ? text : text.Substring(0, index);
        }

        private static int Finish()
        {
            Console.WriteLine();
            Console.WriteLine($"RESULT: {_passed} PASS / {_failed} FAIL");
            return _failed == 0 ? 0 : 1;
        }

        private static void Check(string id, bool ok, string detail)
        {
            var line = (ok ? "[PASS] " : "[FAIL] ") + id + "  " + detail;
            Results.Add(line);
            Console.WriteLine(line);
            if (ok) _passed++;
            else _failed++;
        }

        // ---------------- 测试替身 ----------------

        /// <summary>
        /// 一条已握手、已拿到首屏 snapshot 的 WS 客户端（B20/B21/B23/B24/B36 共用）。
        /// 只包"连接 + 首屏消息"，不含任何断言逻辑——用例自己决定要等什么。
        /// </summary>
        private sealed class WsClient : IDisposable
        {
            public WsClient(ClientWebSocket socket, JObject? hello, JObject? snapshot)
            {
                Socket = socket;
                Hello = hello;
                Snapshot = snapshot;
            }

            public ClientWebSocket Socket { get; }

            public JObject? Hello { get; }

            public JObject? Snapshot { get; }

            /// <summary>首屏 snapshot 里的本页元素数（取不到 = -1）</summary>
            public int ElementCount
            {
                get
                {
                    var page = Snapshot == null ? null : Pick(Snapshot, "page", "Page");
                    var elements = page == null ? null : Pick(page, "Elements", "elements") as JArray;
                    return elements?.Count ?? -1;
                }
            }

            /// <summary>优雅关闭（2 秒上限）：对端不回 close 帧也不许把断言挂住</summary>
            public async Task CloseAsync()
            {
                try
                {
                    if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token);
                    }
                }
                catch
                {
                    // 对端已断开 / close 超时：收尾失败没有补救动作
                }
                finally
                {
                    Dispose();
                }
            }

            public void Dispose()
            {
                try { Socket.Dispose(); } catch { /* 已释放 */ }
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

            /// <summary>
            /// 真实宿主"停止运行"的等价动作：先停会话（<see cref="ScadaRuntime.Stop"/> 幂等），
            /// 再发 <see cref="RunStopped"/>。<b>不吞异常</b>——B24 要断言"再 Stop 一次不抛"。
            /// </summary>
            public void Stop()
            {
                ScadaRuntime? session;

                lock (_gate) session = _session;

                session?.Stop();
                SetStopped();
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

        private sealed class FakeAlarmFeed : IScadaAlarmFeed
        {
            public Guid AlarmId { get; } = Guid.NewGuid();

            public ScadaAlarmFeedSnapshot Snapshot()
                => new(
                    new[]
                    {
                        new ScadaAlarmInfo(
                            AlarmId,
                            "温度高",
                            "温度超过上限",
                            ScadaAlarmSeverity.Critical,
                            DateTime.UtcNow,
                            Acknowledged: false)
                    },
                    UnacknowledgedCount: 1);
        }

        /// <summary>B10b 专用：任何变量引用都返回一个非图像值，用来证明 resolver 根本没读它。</summary>
        private sealed class ImageOnlyValueSource : IScadaValueSource
        {
            public bool TryResolve(Guid variableId, string? fallbackName, out IScadaValueHandle? handle)
            {
                handle = new ImageOnlyHandle();
                return true;
            }

            private sealed class ImageOnlyHandle : IScadaValueHandle
            {
                public Guid VariableId => Guid.Empty;
                public string Name => "img";
                public Type DataType => typeof(string);
                public object? Value => "not-an-image";

                public event EventHandler? ValueChanged
                {
                    add { }
                    remove { }
                }

                public bool TryWrite(object? value, out string? error)
                {
                    error = null;
                    return false;
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
