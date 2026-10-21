using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Core.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Scada.Snapshots;
using WatsonWebserver;
using WatsonWebserver.Core;
using WatsonWebserver.Core.WebSockets;

// 领域层有两个同名快照：剪贴板用的 VisionMaster.Scada.ScadaElementSnapshot（槽位号语义）
// 与渲染器用的 VisionMaster.Scada.Snapshots.ScadaElementSnapshot（caps 填在后者上）。
// 这里用别名钉死：编译器在两个同名类型之间"选一个"是编译错误，改错名字则会把 caps 填到错误的类型上。
using ScadaElementSnapshot = VisionMaster.Scada.Snapshots.ScadaElementSnapshot;

namespace VisionMaster.ScadaWeb
{
    /// <summary>
    /// SCADA 运行态 Web 服务端（<b>M2a：只读镜像 + 命令面</b>——静态基础页 + 页面快照 + 10Hz 值帧 +
    /// 四条 <c>cmd.*</c> 命令 + 在线列表 + 逐图元 <c>caps</c>）。
    ///
    /// <b>M2a 做什么 / 不做什么</b>
    /// ---------
    ///   · 做：起 Watson、<c>/api/*</c> 三个查询接口、<c>WS /ws</c>（<c>auth:true</c> + 一次性 ticket）、
    ///     握手后发 <c>hello</c> → <c>snapshot</c>（或 <c>idle</c>）、每 100ms 广播值帧 <c>frame</c>、
    ///     结构变化（<c>pageId</c>/<c>pageVersion</c>/<c>basePageVersion</c>）发 <c>page</c>、运行结束发 <c>stopped</c>、
    ///     每 2s 广播 <c>presence</c>、四条命令经命令泵串行执行并回 <c>ack</c>/<c>error</c>、
    ///     以及 7 个静态文件（<c>/</c>、<c>/app.js</c>、<c>/styles.css</c>、<c>/elements-process.js</c>、
    ///     <c>/elements-process.css</c>、<c>/elements-interactive.js</c>、<c>/elements-interactive.css</c>，无鉴权）；
    ///   · 不做：租约 <c>lease.*</c>、<c>auth.login</c>/<c>view.attach</c>（后续批次）、<c>/img</c> 取图（固定 404）。
    ///
    /// <b>命令面的三闸都在服务端、且缺一不可</b>
    /// ---------
    ///   ① 总闸 <see cref="ScadaWebSettings.AllowCommands"/>（false = 只读镜像，任何 <c>cmd.*</c> 回 <c>E_POLICY</c>）；
    ///   ② 会话闸（只读票 / 未登录 / 角色 / <c>ClickPolicy</c>，见 <see cref="ScadaWebAccessPolicy"/>）；
    ///   ③ 版本闸（<c>runGeneration</c>/<c>pageId</c>/<c>pageVersion</c> 必填，见 <see cref="ScadaWebCommandRouter"/>）。
    /// 客户端身份字段一律不读——身份只从 <see cref="ScadaWebSession"/> 取（改一个 JSON 字段不该变管理员）。
    ///
    /// <b>Watson 7.2.0 的三个静默失效点（编译期全绿、运行期不报错）</b>
    /// ---------
    ///   1. <c>WebserverSettings.WebSockets.Enable</c> 默认 <c>false</c> → WS 握手 400；
    ///   2. <c>WebSocket(path, handler)</c> 的 <c>auth</c> 默认 <c>false</c> → 完全不鉴权；
    ///   3. <c>auth:true</c> 但 <c>Routes.AuthenticateApiRequest == null</c> → <b>fail-open</b>。
    ///
    /// 三条都按"启动断言"处理，而不是靠代码评审记住（见 <see cref="Start"/> 里的三连检查）。
    /// 依据：<c>_WebProbe2</c>（9 PASS / 0 FAIL）与 <c>docs\SCADA运行态Web客户端方案.md</c> §6.1。
    ///
    /// <b>线程（M1b 起是硬纪律）</b>
    /// ---------
    /// Watson 在 IO 线程上调 handler；<see cref="ScadaRuntime"/> / <c>ScadaPage</c> 的集合与
    /// <see cref="IScadaValueSource"/> 都是<b>单线程（UI）语义</b>。所以：
    ///   · 拍快照 + 解析值**只在 UI Dispatcher 上做**（<see cref="RunOnUiAsync"/>）；
    ///   · 序列化与网络发送在拍完之后做（快照是不可变的，后台线程读它安全）；
    ///   · 会话本身不持有任何领域对象（见 <see cref="ScadaWebSession"/> 的类注释）。
    /// 没有 Dispatcher 的宿主（断言/探针）走"直接调用"分支——那里没有 UI 线程，也就没有跨线程问题。
    ///
    /// <b>为什么是宿主级一条值帧循环</b>
    /// ---------
    /// M1 只有 10 个图元、会话上限 16：每会话一条循环会让同一份快照被拍 N 次、占 N 倍 UI 线程时间。
    /// 宿主级一条循环拍一次、序列化一次、扇出给所有会话（镜像模式的前提就是"大家看的是同一页"）。
    /// 慢消费者只会丢自己队列里的帧（<see cref="ScadaWebSession"/> 的有界队列），不拖累别人。
    /// </summary>
    public sealed class ScadaWebHost
    {
        /// <summary>协议版本（hello/status 里回给客户端；与帧结构同版本号）</summary>
        public const string ProtocolVersion = "1";

        /// <summary>值帧周期：100ms = 10Hz（方案 §4.2 默认 <c>FrameRateHz=10</c>）</summary>
        private const int FrameIntervalMs = 100;

        /// <summary>在线列表广播周期（方案 §9.3：2 秒；连接进出时另外立即广播一次）</summary>
        private const int PresenceIntervalMs = 2000;

        /// <summary>会话被撤销时的 WS 关闭码（4403 = 业务自定义"已撤销"，与 403 语义对齐）</summary>
        private const int RevokedCloseCode = 4403;

        /// <summary>会话自然过期时的 WS 关闭码（4401 = 需要重新登录，与 401 语义对齐）</summary>
        private const int ExpiredCloseCode = 4401;

        private const string AuthHeaderName = "Authorization";
        private const string BearerPrefix = "Bearer ";
        private const string TicketQueryName = "ticket";

        /// <summary>
        /// <c>requestId</c> 的长度上限（字符）。幂等表按 <c>(SessionId, RequestId)</c> 记，
        /// 超长 id 等于让客户端用一个字段决定服务端键的大小；空 id 更糟——所有命令共用一个键。
        /// </summary>
        private const int MaxRequestIdLength = 64;

        /// <summary>
        /// 入站命令帧的 UTF-8 上限。WS 整条消息上限已放宽到 4MB（出站快照需要），
        /// 所以入站必须在这里单独收窄：命令帧最大也就几 KB，64KB 足够。
        /// </summary>
        private const int MaxInboundCommandBytes = 64 * 1024;

        /// <summary>静态资源白名单（路径 → 文件名同一套名字；白名单本身就是"拒绝 .."的判据）</summary>
        private static readonly string[] StaticFiles =
        {
            "index.html",
            "app.js",
            "styles.css",
            "elements-process.js",
            "elements-process.css",
            // M2a 交互图元分册：index.html 会引用它们；不在白名单里就是"页面能打开、
            // 交互图元全丢"（404 只在浏览器控制台可见，服务端看起来一切正常）。
            "elements-interactive.js",
            "elements-interactive.css"
        };

        /// <summary>
        /// 数据帧的 JSON 口径：<b>DTO 原名</b> + 省略 null。
        ///
        /// 为什么不是全局 camelCase：页面快照的消费者是 <c>wwwroot\elements-process.js</c>，
        /// 它按 <c>el.Properties</c> / <c>el.TypeKey</c> / <c>el.Width</c> 读——改成 camelCase
        /// 会让每个图元的属性袋与几何全部读成 undefined（编译期全绿、只有真机看得见）。
        /// 值帧 item 的 camelCase 名字由 <see cref="ScadaFrameItemDto"/> 上的 JsonProperty 显式声明
        /// （渲染器把 <c>values[属性键]</c> 当 <c>{ text, number, bool, color }</c> 消费）。
        /// </summary>
        private static readonly JsonSerializerSettings WireJson = new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        private static readonly string ServerVersion =
            typeof(ScadaWebHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        private readonly IScadaRunHub? _hub;
        private readonly IScadaUserAuthenticator? _authenticator;
        private readonly IScadaRenderCatalog? _catalog;
        private readonly IScadaValueSource? _values;
        private readonly Dispatcher? _uiDispatcher;
        private readonly ILogService _log;
        private readonly ScadaWebSettings _settings;
        private readonly ScadaWebTicketService _tickets = new();
        private readonly ConcurrentDictionary<Guid, ScadaWebSession> _sessions = new();
        private readonly ScadaPageSnapshotBuilder _builder = new();
        private readonly ScadaWebValueResolver _resolver;

        // 结构快照缓存：只在"结构键"变化时重建整页（3000 图元 × 10Hz 的逐图元复制/分配
        // 是 UI 线程最大的固定开销）。缓存的是**未加 caps 的原始快照**——caps 逐会话，
        // 绝不能进这里。只在 UI 线程（CaptureLiveState）读写。
        private string? _structureKey;
        private ScadaPageSnapshot? _structurePage;
        private long _structureBuildCount;

        /// <summary>
        /// 每帧每会话的 UTF-8 字节预算（含 envelope）。由 70Mbps / MaxSessions / 10Hz 推导，
        /// 并与配置项取较小值——120KiB 的默认值原样生效等于 157Mbps，不能直接信。
        /// </summary>
        private readonly int _frameBudgetBytes;

        /// <summary>周期 keyframe：10 秒一次（丢帧/慢消费者的自愈上限）</summary>
        private static readonly TimeSpan KeyframeInterval = TimeSpan.FromSeconds(10);

        // ---- M2a：命令面的五个协作者（宿主只做接线，判据都在各自类里）----
        private readonly IScadaActionDispatcher? _actionDispatcher;
        private readonly IScadaValueWriter? _valueWriter;
        private readonly IScadaAlarmCommands? _alarmCommands;
        private readonly IScadaAlarmFeed? _alarmFeed;
        private readonly IScadaAuditSink? _audit;
        private readonly IScadaFlowTriggerQuery? _flowTriggerQuery;
        private readonly ScadaWebSecurityGate _securityGate;
        private readonly ScadaWebPresence _presence;
        private readonly ScadaWebAccessPolicy _policy;
        private readonly ScadaWebLease _lease;
        private readonly ScadaWebRateLimiter _limiter;

        private readonly object _stateGate = new();
        private Webserver? _server;
        private CancellationTokenSource? _cts;
        private Task? _frameLoop;
        private Task? _presenceLoop;
        private ScadaWebCommandRouter? _router;
        private ScadaWebCommandPump? _pump;

        /// <summary>值帧/快照消息序号（宿主级单调递增；客户端按缺号判断"漏了一帧"）</summary>
        private long _seq;

        /// <summary>值帧循环上一拍看到的状态（只由循环线程写；<c>RunStopped</c> 处理器也会清它）</summary>
        private volatile bool _loopSawRunning;

        /// <param name="hub">运行态事实出口；<c>null</c> = 未注册（status 一律按"未运行"回）</param>
        /// <param name="authenticator">账号校验出口；<c>null</c> = 无法登录（匿名只读仍可用）</param>
        /// <param name="catalog">图元目录出口；<c>null</c> = /api/catalog 回空目录</param>
        /// <param name="log">日志</param>
        /// <param name="settings">配置快照（改完需重启，见 <see cref="ScadaWebSettings"/> 类注释）</param>
        /// <param name="values">运行态值通道；<c>null</c> = 只发页面结构、值帧为空</param>
        /// <param name="uiDispatcher">
        /// 宿主 UI 线程的 Dispatcher；<c>null</c> = 直接在当前线程读模型（断言/探针宿主）。
        /// 产品路径必须传——模型集合是单线程语义，见类注释"线程"。
        /// </param>
        /// <param name="actionDispatcher">
        /// 图元动作分发器（<c>cmd.element</c>）；<c>null</c> = 服务端未接入，该命令回 <c>E_INTERNAL</c>。
        /// 宿主在 <c>App.RegisterTypes</c> 注册（<c>IScadaActionDispatcher</c>）。
        /// </param>
        /// <param name="valueWriter">
        /// 输入写值通道（<c>cmd.input</c>）；<c>null</c> = 未接入，该命令回 <c>E_INTERNAL</c>。
        /// </param>
        /// <param name="alarmCommands">
        /// 报警确认出口（<c>cmd.alarm.ack</c>）；<c>null</c> = 未接入，该命令回 <c>E_INTERNAL</c>。
        /// </param>
        /// <param name="audit">
        /// 命令级审计出口；<c>null</c> = 不落审计（产品路径必须传——"谁按的"只有审计能回答）。
        /// </param>
        public ScadaWebHost(
            IScadaRunHub? hub,
            IScadaUserAuthenticator? authenticator,
            IScadaRenderCatalog? catalog,
            ILogService log,
            ScadaWebSettings settings,
            IScadaValueSource? values = null,
            Dispatcher? uiDispatcher = null,
            IScadaActionDispatcher? actionDispatcher = null,
            IScadaValueWriter? valueWriter = null,
            IScadaAlarmCommands? alarmCommands = null,
            IScadaAuditSink? audit = null,
            IScadaAlarmFeed? alarmFeed = null,
            IScadaFlowTriggerQuery? flowTriggerQuery = null)
        {
            _hub = hub;
            _authenticator = authenticator;
            _catalog = catalog;
            _values = values;
            _uiDispatcher = uiDispatcher;
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _settings = settings ?? new ScadaWebSettings();
            _resolver = new ScadaWebValueResolver(message => _log.Warn(message));

            // 预算推导：70Mbps 目标、15% 余量、16 会话、10Hz → 约 46KB/帧/会话。
            // 配置值更大时不放行（那会把目标直接打穿），更小时尊重现场带宽更窄的选择。
            var budgetSessions = Math.Max(1,
                _settings.MaxSessions > 0 ? _settings.MaxSessions : ScadaWebSettings.DefaultMaxSessions);
            var budgetCeiling = (int)(70_000_000 * 0.85 / 8 / budgetSessions / (1000 / FrameIntervalMs));
            _frameBudgetBytes = Math.Clamp(
                Math.Min(_settings.FrameBudgetBytes, budgetCeiling),
                8 * 1024,
                256 * 1024);

            if (_frameBudgetBytes != _settings.FrameBudgetBytes)
            {
                _log.Info($"[ScadaWeb] FrameBudgetBytes={_settings.FrameBudgetBytes} 已按 "
                    + $"{budgetSessions} 会话 × 10Hz × 70Mbps 目标夹取为 {_frameBudgetBytes} 字节/帧/会话");
            }

            _actionDispatcher = actionDispatcher;
            _valueWriter = valueWriter;
            _alarmCommands = alarmCommands;
            _alarmFeed = alarmFeed;
            _audit = audit;
            _flowTriggerQuery = flowTriggerQuery;

            // 票表是身份的唯一来源：安全闸必须委托**同一份**票表（两份 = 撤销一边、放行另一边）。
            _tickets.AllowAnonymousView = _settings.AllowAnonymousView;

            _securityGate = new ScadaWebSecurityGate(_tickets)
            {
                // 配置项接进闸门：常量 10 只是默认值，调参必须真的生效（M2a-2 未决项）。
                TicketRatePerMinute = _settings.TicketRatePerMinute
            };

            // 在线列表 / 权限策略 / 限速三者共用同一份配置快照与会话表：
            // presence 里显示"可操作"、点下去却被拒，是这类系统最招人烦的不一致。
            _presence = new ScadaWebPresence(_settings);
            _lease = new ScadaWebLease();
            _policy = new ScadaWebAccessPolicy(_settings, _presence, _lease);
            _limiter = new ScadaWebRateLimiter(_settings);
        }

        /// <summary>是否正在监听（<b>不是</b>运行态是否在跑；后者见 /api/status 的 running）</summary>
        public bool IsListening => _server?.IsListening ?? false;

        /// <summary>当前在线会话数</summary>
        public int OnlineCount => _sessions.Count;

        /// <summary>实际生效的每帧每会话字节预算（诊断/断言用；配置值可能被 70Mbps 目标夹小）</summary>
        public int EffectiveFrameBudgetBytes => _frameBudgetBytes;

        /// <summary>结构快照真正重建的次数（诊断/断言用：同页连续两拍不该增加）</summary>
        public long StructureBuildCount => Interlocked.Read(ref _structureBuildCount);

        /// <summary>实际生效的监听端口（<c>0</c> = 未启动）</summary>
        public int Port { get; private set; }

        /// <summary>实际生效的监听地址</summary>
        public string BindAddress { get; private set; } = string.Empty;

        /// <summary>最近一次启动失败原因（<c>null</c> = 没失败过）。模块用它决定日志级别</summary>
        public string? LastError { get; private set; }

        /// <summary>WS 是否已启用（启动断言三连的第 1 条；断言宿主用它反证）</summary>
        public bool WebSocketsEnabled { get; private set; }

        /// <summary>一次性 ticket 服务（断言宿主观测"用完即删"）</summary>
        public ScadaWebTicketService Tickets => _tickets;

        /// <summary>安全前置闸门（Origin / 取票限速 / 默认密码 / 撤销；断言与桌面端踢人入口共用）</summary>
        public ScadaWebSecurityGate SecurityGate => _securityGate;

        /// <summary>在线会话列表（presence 的数据源；只读快照由它产出）</summary>
        public ScadaWebPresence Presence => _presence;

        /// <summary>权限策略（caps 与命令执行共用同一判据）</summary>
        public ScadaWebAccessPolicy Policy => _policy;

        /// <summary>操作权租约（M2b；C77）</summary>
        public ScadaWebLease Lease => _lease;

        /// <summary>当前命令路由（<c>null</c> = 未启动 / 没有运行态出口）</summary>
        public ScadaWebCommandRouter? Router => _router;

        /// <summary>当前命令泵（<c>null</c> = 未启动 / 没有运行态出口）</summary>
        public ScadaWebCommandPump? Pump => _pump;

        #region 启动 / 停止

        /// <summary>
        /// 启动监听。<b>任何失败只记 Error、不抛</b>——Web 是可选模块，
        /// 端口被占/权限不足都不该把宿主带下去（与 <c>HttpImageServer</c> 同一条口径）。
        /// </summary>
        public void Start()
        {
            lock (_stateGate)
            {
                if (_server != null)
                {
                    _log.Info("[ScadaWeb] 服务已在运行，忽略重复 Start");
                    return;
                }
            }

            var bindAddress = string.IsNullOrWhiteSpace(_settings.BindAddress)
                ? ScadaWebSettings.DefaultBindAddress
                : _settings.BindAddress.Trim();

            var port = _settings.Port is > 0 and <= 65535
                ? _settings.Port
                : ScadaWebSettings.DefaultPort;

            BindAddress = bindAddress;
            Port = port;
            LastError = null;

            Webserver? server = null;
            CancellationTokenSource? cts = null;

            try
            {
                var serverSettings = new WebserverSettings(bindAddress, port);

                // ★ 静默失效点 1：默认 false → WS 握手 400。必须显式打开。
                serverSettings.WebSockets.Enable = true;

                // 未鉴权的 /api/ticket 与 WS 命令帧都必须有硬上限：
                // Watson 默认 MaxRequestBodySize=0（不限）、MaxMessageSize=16MB，
                // 一个匿名请求就能把整段 JSON 读进内存再解析（2026-10-21 终审 P1-5）。
                // 64KB 对 ticket/命令帧绰绰有余；超限由 Watson 直接拒绝，不进入我们的解析器。
                // WS 的整条消息上限放宽到 4MB：它是**双向**的，出站快照在 3000 图元时约 1.5–1.8MB，
                // 64KB 会把整份快照卡死。入站命令帧仍由 HandleTextFrame 的 64KB 闸单独限制
                // （见 MaxInboundCommandBytes），所以放宽不会让匿名连接拿 4MB 命令打内存。
                serverSettings.IO.MaxRequestBodySize = 64 * 1024;
                serverSettings.WebSockets.MaxMessageSize = 4 * 1024 * 1024;

                server = new Webserver(serverSettings, DefaultRoute);

                // ★ 静默失效点 3：必须先赋鉴权器、再注册 auth:true 路由（否则 fail-open）。
                // 顺序不是风格问题：Watson 在注册时不校验，运行时才看这个字段。
                server.Routes.AuthenticateApiRequest = Authenticate;

                // ---- HTTP：三个无鉴权查询接口（方案 §6.1 路由表）----
                server.Get("/api/status", HandleStatus);
                server.Get("/api/catalog", HandleCatalog);
                server.Post("/api/ticket", HandleTicket);

                // ---- /img：M1b 仍未接 imageToken；现在一律 404，占住路由形状 ----
                server.Get("/img", HandleImagePlaceholder);
                server.Get("/img/{elementId}", HandleImagePlaceholder);
                server.Get("/img/{elementId}/{version}", HandleImagePlaceholder);

                // ---- 静态资源：基础页（无鉴权、不含任何组态数据；数据只走 /ws）----
                RegisterStaticRoutes(server);

                // ★ 静默失效点 2：auth 默认 false。业务 WS 路由必须显式 auth: true。
                server.WebSocket("/ws", HandleWebSocket, auth: true);

                // ---- 启动断言三连（fail-open 比不启动更危险，方案 §6.1 第 4 条）----
                if (!serverSettings.WebSockets.Enable)
                    throw new InvalidOperationException("WebSockets.Enable 未打开（握手会 400）");
                if (server.Routes.AuthenticateApiRequest == null)
                    throw new InvalidOperationException("AuthenticateApiRequest 为空（auth:true 会 fail-open）");

                cts = new CancellationTokenSource();
                server.Start();

                if (!server.IsListening)
                    throw new InvalidOperationException("Start() 返回但 IsListening == false（端口未真正绑定）");

                lock (_stateGate)
                {
                    _server = server;
                    _cts = cts;
                }

                WebSocketsEnabled = serverSettings.WebSockets.Enable;

                // 值帧循环 + 运行态事件 + 在线列表：都只在这一份服务实例上活着（Stop 时一并收掉）
                SubscribeHubEvents();

                // 命令面必须在"服务已开始监听"之后建：建失败不该让一个没起来的服务留下一个泵线程。
                StartCommandPlane();

                _frameLoop = Task.Run(() => RunFrameLoopAsync(cts.Token));
                _presenceLoop = Task.Run(() => RunPresenceLoopAsync(cts.Token));

                _log.Info($"[ScadaWeb] 已监听 http://{bindAddress}:{port}/"
                    + $"（/api/status、/api/catalog、POST /api/ticket、WS /ws〔auth:true〕、静态页；"
                    + $"快照 + {1000 / FrameIntervalMs}Hz 值帧 + 2s 在线列表；"
                    + (_settings.AllowCommands ? "命令面已开启" : "命令面只读（AllowCommands=false）")
                    + (_values == null ? "；未注册值源，值帧为空" : string.Empty)
                    + "）");
            }
            catch (Exception ex)
            {
                LastError = $"{ex.GetType().Name}：{ex.Message}";
                _log.Error($"[ScadaWeb] 启动失败（{bindAddress}:{port}）：{LastError}。"
                    + "Web 功能不可用，宿主继续运行（改端口/权限后重启软件生效）");

                try { cts?.Dispose(); } catch { /* 释放失败没有补救动作 */ }
                try { server?.Dispose(); } catch { /* 同上 */ }

                // 失败点若在 _server 赋值之后（StartCommandPlane/循环启动），必须把字段清掉：
                // 否则下一次 Start 会走"已在运行"分支，而服务实际没在监听（P2-8）。
                lock (_stateGate)
                {
                    if (ReferenceEquals(_server, server))
                    {
                        _server = null;
                        _cts = null;
                    }
                }

                UnsubscribeHubEvents();

                // 命令泵可能已经建起来（失败点若在其后），不收拾会留下一条常驻线程
                StopCommandPlane();

                Port = 0;
                WebSocketsEnabled = false;
            }
        }

        /// <summary>
        /// 停止服务（同步）。停服 = 停止接受新连接 → 取消令牌（断开接收循环）→ 停命令泵 → 关闭会话 →
        /// 释放服务端 → 清空在线列表/限速/撤销账。<b>幂等</b>：未启动或已停止时仍然会把命令面收干净。
        /// </summary>
        public void Stop()
        {
            var detached = Detach();
            if (detached != null) ShutdownServer(detached);

            StopCommandPlane();

            if (detached != null)
            {
                foreach (var session in detached.Sessions)
                {
                    session.Dispose();
                    _ = session.CloseAsync(WebSocketCloseStatus.NormalClosure, "server stopping");
                }
            }

            ResetCommandState();

            if (detached != null) _log.Info("[ScadaWeb] 服务已停止");
        }

        /// <summary>
        /// 停止服务（异步，退出链用）：与 <see cref="Stop"/> 同序，但会等关闭帧发完（最多 2 秒），
        /// 并等值帧循环 / 在线列表循环 / 命令泵收尾（它们都已拿到取消令牌，通常立刻返回）。
        /// </summary>
        public async Task StopAsync()
        {
            var detached = Detach();
            if (detached != null) ShutdownServer(detached);

            // 先停泵（它会把手里的命令跑完），再关连接——反过来的话回执没有出口。
            await StopCommandPlaneAsync().ConfigureAwait(false);

            if (detached != null)
            {
                var closes = detached.Sessions
                    .Select(session =>
                    {
                        session.Dispose();
                        return session.CloseAsync(WebSocketCloseStatus.NormalClosure, "server stopping");
                    })
                    .ToArray();

                if (closes.Length > 0)
                {
                    try
                    {
                        await Task.WhenAll(closes).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    }
                    catch
                    {
                        // 对端不回 close 帧 / 已断开：退出链不该为它多等
                    }
                }

                await WaitForFrameLoopAsync(detached.FrameLoop).ConfigureAwait(false);
                await WaitForFrameLoopAsync(detached.PresenceLoop).ConfigureAwait(false);
            }

            ResetCommandState();

            if (detached != null) _log.Info("[ScadaWeb] 服务已停止（退出链）");
        }

        /// <summary>等值帧循环退出（最多 2 秒）；超时不算失败——它已经拿到取消令牌</summary>
        private static async Task WaitForFrameLoopAsync(Task? loop)
        {
            if (loop == null) return;

            try
            {
                await loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch
            {
                // 循环内部已吞异常；这里只处理"2 秒内没退完"
            }
        }

        /// <summary>把"当前这一份运行态"摘下来（锁内一次性取走，之后的状态都归调用方处理）</summary>
        private DetachedState? Detach()
        {
            Webserver? server;
            CancellationTokenSource? cts;
            Task? frameLoop;
            Task? presenceLoop;
            ScadaWebSession[] sessions;

            lock (_stateGate)
            {
                server = _server;
                cts = _cts;
                frameLoop = _frameLoop;
                presenceLoop = _presenceLoop;
                _server = null;
                _cts = null;
                _frameLoop = null;
                _presenceLoop = null;
                sessions = _sessions.Values.ToArray();
                _sessions.Clear();
            }

            if (server == null && cts == null) return null;   // 幂等：没启动过 / 已停止

            // 停服后下一轮可能换方案：结构缓存不能跨轮存活。
            _structureKey = null;
            _structurePage = null;

            // 先退订：停服过程中 hub 再发 RunStarted/RunStopped 时不该还有人往会话里塞帧
            UnsubscribeHubEvents();

            // 先取消：接收循环 break、发送循环退出、值帧/在线列表循环退出，再关连接才不会互相等
            try { cts?.Cancel(); } catch { /* 已取消 */ }

            _tickets.Clear();

            return new DetachedState(server, cts, sessions, frameLoop, presenceLoop);
        }

        private void ShutdownServer(DetachedState state)
        {
            if (state.Server != null)
            {
                try
                {
                    if (state.Server.IsListening) state.Server.Stop();
                }
                catch (Exception ex)
                {
                    _log.Warn($"[ScadaWeb] 停止监听失败：{ex.Message}");
                }

                // 已通过鉴权、但 handler 还没把会话登记进字典的连接（极小窗口）也要关掉，
                // 否则 Stop() 之后它们会挂着一条没有接收循环的死连接
                try
                {
                    foreach (var socket in state.Server.ListWebSocketSessions())
                    {
                        try { _ = socket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "server stopping"); }
                        catch { /* 已断开 */ }
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn($"[ScadaWeb] 枚举残余 WS 会话失败：{ex.Message}");
                }

                try { state.Server.Dispose(); } catch (Exception ex) { _log.Warn($"[ScadaWeb] 释放服务端失败：{ex.Message}"); }
            }

            try { state.Cts?.Dispose(); } catch { /* 释放失败没有补救动作 */ }
        }

        private sealed record DetachedState(
            Webserver? Server,
            CancellationTokenSource? Cts,
            ScadaWebSession[] Sessions,
            Task? FrameLoop,
            Task? PresenceLoop);

        // ── 命令面（路由 + 泵）生命周期 ────────────────────────────────────────

        /// <summary>
        /// 建命令面。<b>只在 <see cref="Start"/> 里调用</b>（服务已经在监听之后）：
        /// 泵是常驻线程，建在构造函数里会让"从未启动的宿主"也留一条后台线程。
        /// 没有运行态出口（<see cref="_hub"/> 为 <c>null</c>）时不建——命令无从判定版本闸。
        /// </summary>
        private void StartCommandPlane()
        {
            if (_hub == null)
            {
                _log.Warn("[ScadaWeb] 未注册 IScadaRunHub：命令面不可用（cmd.* 一律回 E_INTERNAL）");
                return;
            }

            var capacity = Math.Clamp(
                _settings.SessionInboundQueueCapacity,
                ScadaWebSettings.MinSessionInboundQueueCapacity,
                ScadaWebSettings.MaxSessionInboundQueueCapacity);

            if (capacity != _settings.SessionInboundQueueCapacity)
            {
                _log.Warn($"[ScadaWeb] SessionInboundQueueCapacity={_settings.SessionInboundQueueCapacity} 越界，"
                    + $"已按 {capacity} 夹取（允许 {ScadaWebSettings.MinSessionInboundQueueCapacity}"
                    + $"~{ScadaWebSettings.MaxSessionInboundQueueCapacity}）");
            }

            _router = new ScadaWebCommandRouter(
                _hub,
                _actionDispatcher,
                _valueWriter,
                _alarmCommands,
                _policy,
                _limiter,
                _audit,
                _log,
                _settings,
                flowTriggerQuery: _flowTriggerQuery);

            _pump = new ScadaWebCommandPump(_router.Handle, _log, _uiDispatcher, capacity);
            _pump.Completed += OnCommandCompleted;

            _log.Info($"[ScadaWeb] 命令面已就绪（每会话 {capacity} 条 → 全局 {ScadaWebCommandPump.GlobalCapacity} 条；"
                + $"允许动作 {_router.AllowedActions.Count} 种；"
                + (_actionDispatcher == null ? "未接入动作分发器；" : string.Empty)
                + (_valueWriter == null ? "未接入写值通道；" : string.Empty)
                + (_alarmCommands == null ? "未接入报警出口；" : string.Empty)
                + (_audit == null ? "未接入审计出口" : "审计已接入")
                + "）");
        }

        /// <summary>停命令面（同步，最多等 5 秒）。幂等。</summary>
        private void StopCommandPlane()
            => StopCommandPlaneAsync().GetAwaiter().GetResult();

        /// <summary>停命令面（异步）：先排空队列，再清确认令牌与幂等表。</summary>
        private async Task StopCommandPlaneAsync()
        {
            var pump = _pump;
            var router = _router;
            _pump = null;
            _router = null;

            if (pump != null)
            {
                pump.Completed -= OnCommandCompleted;

                pump.Dispose();
            }

            router?.ConfirmTokens.Clear();
            router?.Dedup.Clear();
        }

        /// <summary>停服清空：在线列表 / 限速窗口 / 撤销账（票表由 <see cref="Detach"/> 清）</summary>
        private void ResetCommandState()
        {
            try { _presence.Clear(); } catch { /* 清空失败没有补救动作 */ }
            try { _lease.Clear(); } catch { /* 同上 */ }
            try { _limiter.Reset(); } catch { /* 同上 */ }
            try { _securityGate.Reset(); } catch { /* 同上 */ }
        }

        /// <summary>一条命令执行完（成功或失败）→ 回执发给发起会话（<c>ack</c> / <c>error</c>）</summary>
        private void OnCommandCompleted(ScadaWebCommand command, ScadaWebCommandResult result)
        {
            var session = command.Session;

            if (session == null)
                return;

            var reply = Json(new
            {
                type = result.Ok ? "ack" : "error",
                id = command.RequestId,
                requestId = command.RequestId,
                kind = command.Kind,
                ok = result.Ok,
                code = result.Code,
                message = result.Message,
                result = result.Result
            });

            if (!session.TrySend(reply))
            {
                _log.Warn($"[ScadaWeb] 命令回执未能入队（ref={session.SessionRef}，"
                    + $"request={ShortToken(command.RequestId)}，code={result.Code}）：出站队列已满或会话已断开");

                // 回执丢了，客户端只会一直等到超时、却不知道动作是否已执行。
                // 主动以 1011 关闭连接，让客户端立刻走"连接异常"分支（可重连并重新取快照），
                // 而不是把"已执行但没回执"误当成"没执行"再点一次（2026-10-21 终审 P1-15）。
                _ = session.CloseAsync(WebSocketCloseStatus.InternalServerError, "ack delivery failed");
            }
        }

        #endregion

        #region 安全审计

        /// <summary>
        /// 安全事件审计（Origin 拒绝 / 取票限速 / 默认密码 / 握手 401 / 只读策略 / 队列满 / 撤销）。
        ///
        /// <b>为什么这些行要单独写</b>：命令级审计只覆盖"命令已经进了路由"之后的事，
        /// 而上面这些事件<b>根本走不到路由</b>——它们回答的是"谁在敲门、被哪一道闸挡下的"，
        /// 是事后唯一能证明"有人试过"的那部分记录。
        ///
        /// <b>线程</b>：Watson 在 HTTP/WS 线程上调这里；<see cref="IScadaAuditSink"/> 的实现
        /// （宿主 <c>ScadaAuditWriter</c>）用写锁串行化，允许任意线程调用。
        /// <c>[web s=… ip=…]</c> 前缀由写入器统一拼，这里<b>不</b>再拼一次。
        ///
        /// <b>没有审计出口时只记 Warn、不抛</b>：安全闸的返回值不能因为"审计写不了"而改变
        /// （既不许因此放行，也不许因此把请求打成 500）。
        /// </summary>
        private void AuditSecurity(
            string? actor,
            string? sessionId,
            string? ip,
            string subject,
            string eventText,
            string action,
            string result,
            string detail)
        {
            var audit = _audit;

            if (audit == null)
            {
                _log.Warn($"[ScadaWeb] 安全事件未落审计（未接入 IScadaAuditSink）："
                    + $"actor={actor ?? "<空>"}，subject={subject}，event={eventText}，result={result}，detail={detail}");
                return;
            }

            try
            {
                if (!audit.Append(
                        string.IsNullOrWhiteSpace(actor) ? "未登录" : actor!,
                        sessionId ?? string.Empty,
                        ip ?? string.Empty,
                        subject,
                        eventText,
                        action,
                        result,
                        detail))
                {
                    _log.Warn($"[ScadaWeb] 安全事件审计未写入：actor={actor ?? "<空>"}，subject={subject}，result={result}");
                }
            }
            catch (Exception ex)
            {
                // 审计坏掉不许把安全闸的返回值带下去（与命令级审计同一条口径）
                _log.Warn($"[ScadaWeb] 写安全事件审计异常：{ex.GetType().Name}：{ex.Message}");
            }
        }

        #endregion

        #region 鉴权

        /// <summary>
        /// 握手鉴权：先过 Origin 同源闸，再核销一次性 ticket（query <c>?ticket=</c> 优先，
        /// 其次 <c>Authorization: Bearer</c>）。
        ///
        /// <b>为什么两种取法都要</b>
        /// ---------
        /// 浏览器 <c>WebSocket</c> 只能放 query；<c>ClientWebSocket</c>（探针/网关）习惯放头。
        /// 两条路读同一张票，语义完全一致——不存在"头比 query 更可信"这回事。
        ///
        /// <b>失败一律 401</b>：Watson 会自动写结构化错误体，业务 handler 一次都不会执行
        /// （<c>_WebProbe2</c> §3 实测）。原因只写服务端日志，不回给客户端（避免账号/票面枚举）。
        ///
        /// <b>为什么核销时要传 <c>allowCommands</c></b>：票的有效期 30 秒，配置可能在这 30 秒里
        /// 被改成只读；按<b>核销那一刻</b>的策略再压一次只读标记，窗口才收得回来
        /// （见 <see cref="ScadaWebTicketService.TryRedeem(string?,string?,bool,out ScadaWebTicketGrant?,out string,out string?)"/>）。
        /// </summary>
        private Task<AuthResult> Authenticate(HttpContextBase ctx)
        {
            // 匿名访问开关每轮现读：票表构造时只拿到一次值，不刷新的话"关掉匿名访问"
            // 对已经签发/正在续期的匿名票要等到重启才生效（现场表现：配置改了没反应）。
            _tickets.AllowAnonymousView = _settings.AllowAnonymousView;

            // ① Origin 同源：跨站页面借用受害者浏览器连 WS 是 WS 不受同源策略约束的直接后果。
            var origin = ReadHeader(ctx.Request, "Origin");
            var host = ReadHostHeader(ctx.Request);

            if (!_securityGate.IsOriginAllowed(origin, host, out var originError))
            {
                _log.Warn($"[ScadaWeb] WS 握手被拒（跨站）：{originError}（origin={origin ?? "<无>"}，"
                    + $"path={SafeRawPath(ctx)}）");

                AuditSecurity("未登录", sessionId: null, ReadClientIp(ctx.Request), "WS 握手", "跨站请求",
                    "握手鉴权", "拒绝", $"{originError}（origin={origin ?? "<无>"}，path={SafeRawPath(ctx)}）");

                return Task.FromResult(DenyAuth());
            }

            var ticket = ReadTicket(ctx.Request);
            var remoteIp = ReadClientIp(ctx.Request);

            if (_tickets.TryRedeem(ticket, remoteIp, _settings.AllowCommands, out var grant, out var reason, out var errorCode))
            {
                _log.Info($"[ScadaWeb] WS 握手放行：ticket={grant!.ShortTicket}…，"
                    + $"user={grant.UserName ?? "<匿名只读>"}，role={grant.Role.DisplayName()}，"
                    + $"readOnly={grant.ReadOnly}");

                return Task.FromResult(new AuthResult
                {
                    AuthenticationResult = AuthenticationResultEnum.Success,
                    AuthorizationResult = AuthorizationResultEnum.Permitted
                });
            }

            // E_SESSION_EXPIRED / E_SESSION_REVOKED / E_CAPACITY 在握手处<b>都是 401</b>：
            // Watson 7.2.0 的 AuthResult 只有 AuthenticationResult / AuthorizationResult / Metadata
            // 三个成员（反射核实，无状态码字段），握手阶段表达不了 429。三者的区别落在服务端日志里
            // （现场据此判断是"过期"还是"被踢"还是"表满"），客户端统一回登录/重试。
            _log.Warn($"[ScadaWeb] WS 握手被拒：{reason}"
                + (string.IsNullOrEmpty(errorCode) ? string.Empty : $"（code={errorCode}）")
                + $"（path={SafeRawPath(ctx)}）");

            AuditSecurity("未登录", sessionId: null, remoteIp, "WS 握手", "ticket 核销",
                "握手鉴权", "拒绝", $"{reason}（code={errorCode ?? "<无>"}，path={SafeRawPath(ctx)}）");

            return Task.FromResult(DenyAuth());
        }

        private static AuthResult DenyAuth() => new()
        {
            AuthenticationResult = AuthenticationResultEnum.NotFound,
            AuthorizationResult = AuthorizationResultEnum.DeniedImplicit
        };

        /// <summary>读请求头（缺失/空白统一回 <c>null</c>，让调用方的判据只有一种"没有"）</summary>
        private static string? ReadHeader(HttpRequestBase request, string name)
        {
            try
            {
                var value = request.RetrieveHeaderValue(name);
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 本次请求的 <c>Host</c>（Origin 同源判据的一半）。
        /// 优先取请求头；个别 Watson 版本不把 Host 放进 <c>Headers</c> 时按 URL 的主机+端口重建
        /// （等价值：浏览器发 Origin 时的端口就是它实际连上的端口）。
        /// </summary>
        private static string? ReadHostHeader(HttpRequestBase request)
        {
            var header = ReadHeader(request, "Host");
            if (header != null) return header;

            try
            {
                var url = request.Url;
                if (url == null || string.IsNullOrWhiteSpace(url.Host)) return null;

                return url.Port.HasValue ? $"{url.Host}:{url.Port.Value}" : url.Host;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>客户端 IP（取票限速 / 审计用；取不到回 <c>null</c>，限速按共享额度处理）</summary>
        private static string? ReadClientIp(HttpRequestBase request)
        {
            try
            {
                var ip = request.Source?.IpAddress;
                return string.IsNullOrWhiteSpace(ip) ? null : ip;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>从握手请求里取 ticket：Authorization 头 → query → 手工解析 RawWithQuery</summary>
        private static string? ReadTicket(HttpRequestBase request)
        {
            var header = request.RetrieveHeaderValue(AuthHeaderName);
            if (!string.IsNullOrEmpty(header)
                && header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var token = header.Substring(BearerPrefix.Length).Trim();
                if (token.Length > 0) return token;
            }

            var queryValue = request.RetrieveQueryValue(TicketQueryName);
            if (!string.IsNullOrWhiteSpace(queryValue)) return queryValue;

            // 兜底：WS 升级请求的 Query 容器在个别 Watson 版本里是空的，但原始 URL 一定有。
            // 少了这条，浏览器（只能放 query）会在某些版本上 100% 连不上，而探针（放头）全绿。
            return ReadQueryValueManually(request.Url?.RawWithQuery, TicketQueryName);
        }

        private static string? ReadQueryValueManually(string? rawWithQuery, string key)
        {
            if (string.IsNullOrEmpty(rawWithQuery)) return null;

            var mark = rawWithQuery.IndexOf('?');
            if (mark < 0 || mark == rawWithQuery.Length - 1) return null;

            foreach (var part in rawWithQuery.Substring(mark + 1).Split('&'))
            {
                if (part.Length == 0) continue;

                var eq = part.IndexOf('=');
                var name = eq < 0 ? part : part.Substring(0, eq);

                if (!string.Equals(Uri.UnescapeDataString(name), key, StringComparison.Ordinal))
                    continue;

                return eq < 0 ? string.Empty : Uri.UnescapeDataString(part.Substring(eq + 1));
            }

            return null;
        }

        #endregion

        #region HTTP 接口

        /// <summary>
        /// <c>GET /api/status</c>：服务与运行态状态。<b>未运行也给 200</b>——
        /// 基础页要靠它区分"桌面端没在跑"与"服务挂了"，后者根本拿不到响应。
        /// </summary>
        private Task<object> HandleStatus(ApiRequest request)
        {
            try
            {
                var snapshot = ReadRunSnapshot();

                return Task.FromResult<object>(new
                {
                    ok = true,
                    running = snapshot.Running,
                    pageId = snapshot.PageId,
                    pageName = snapshot.PageName,
                    pageVersion = snapshot.PageVersion,
                    online = _sessions.Count,
                    version = ServerVersion,
                    protocol = ProtocolVersion,
                    readOnly = !_settings.AllowCommands
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult<object>(Fail(request, 500, "E_STATUS", $"读取运行态失败：{ex.Message}"));
            }
        }

        /// <summary>
        /// <c>GET /api/catalog</c>：图元类型目录（无鉴权）。
        /// 目录里只有 TypeKey/显示名/默认尺寸/属性键，没有 WPF 类型，也没有画面内容——
        /// 所以它可以是公开的（基础页要在登录前就把"这是什么软件"画出来）。
        /// </summary>
        private Task<object> HandleCatalog(ApiRequest request)
        {
            try
            {
                var items = _catalog?.All;

                if (items == null)
                {
                    return Task.FromResult<object>(new
                    {
                        ok = true,
                        count = 0,
                        items = Array.Empty<ScadaElementDescriptorInfo>(),
                        warning = "服务端未注册 IScadaRenderCatalog，目录为空"
                    });
                }

                return Task.FromResult<object>(new
                {
                    ok = true,
                    count = items.Count,
                    items
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult<object>(Fail(request, 500, "E_CATALOG", $"读取图元目录失败：{ex.Message}"));
            }
        }

        /// <summary>
        /// <c>POST /api/ticket</c>：签发一次性 WS 握手 ticket。
        ///
        /// <b>三闸顺序（M2a，缺一不可）</b>
        /// ---------
        ///   ① <b>Origin 同源</b>（<see cref="ScadaWebSecurityGate.IsOriginAllowed"/>）→ 403 <c>E_ORIGIN</c>；
        ///   ② <b>每来源 IP 限速</b>（<c>ScadaWeb.TicketRatePerMinute</c>，默认 10/分钟）→ 429 <c>E_RATE_LIMITED</c> + <c>retryAfterMs</c>；
        ///   ③ <b>出厂默认密码闸门</b>（登录分支，账号校验通过之后）→ 403 <c>E_DEFAULT_PASSWORD</c>。
        /// 三条都在<b>签票之前</b>：拒绝路径不产生 ticket、不做无意义的后续计算。
        /// 另有一道<b>容量预检</b>：会话令牌表满时 503 <c>E_CAPACITY</c>（只挡登录 / 匿名这两条会新签令牌的路径，
        /// 续期沿用原令牌不受限）。握手阶段的容量拒绝只能回 401（Watson 的 AuthResult 没有 503 语义），
        /// 所以在取票处提前挡一次，客户端拿到的是一句能看懂的"稍后重试"。
        ///
        /// <b>三种请求体（按优先级）</b>
        /// ---------
        ///   1. <c>{"refreshToken":"…"}</c> —— <b>断线重连</b>：校验 8 小时会话令牌，通过即签新票，
        ///      <b>不要求密码</b>；无效/过期回 401 <c>E_SESSION_EXPIRED</c>（客户端据此回到登录页）；
        ///   2. <c>{"user":"…","password":"…"}</c> —— 正常登录；
        ///   3. 空体 / <c>{}</c> —— 匿名只读观众，仅在 <c>AllowAnonymousView=true</c> 时签发。
        ///
        /// <b>只读口径</b>：<c>ScadaWeb.AllowCommands=false</c> 时任何票都是只读票；
        /// 匿名观众<b>永远</b>只读（见 <see cref="ScadaWebTicketService.Issue"/>）。响应带 <c>readOnly</c>。
        ///
        /// <b>账号校验必须离开 Watson 的 IO 线程</b>：PBKDF2 十万次是几十毫秒级 CPU 活，
        /// 直接在这里算会占住一条 IO 线程（并发登录时整体吞吐塌陷）。所以 <c>Task.Run</c> 一跳。
        ///
        /// <b>ticket 与会话令牌都不进日志</b>：日志只记前 6 位
        /// （<see cref="ScadaWebTicketGrant.ShortTicket"/> / <see cref="ScadaWebSessionGrant.ShortToken"/>）。
        /// </summary>
        private async Task<object> HandleTicket(ApiRequest request)
        {
            // 匿名访问开关每轮现读（理由见 Authenticate）：匿名续期/取票必须按当前配置立即生效。
            _tickets.AllowAnonymousView = _settings.AllowAnonymousView;

            // ---- ① Origin 同源（跨站页面拿不到票 = 拿不到 WS 入口）----
            var origin = ReadHeader(request.Http.Request, "Origin");
            var host = ReadHostHeader(request.Http.Request);

            if (!_securityGate.IsOriginAllowed(origin, host, out var originError))
            {
                _log.Warn($"[ScadaWeb] 取票被拒（跨站）：{originError}（origin={origin ?? "<无>"}）");

                AuditSecurity("未登录", sessionId: null, ReadClientIp(request.Http.Request), "取票", "跨站请求",
                    "取票", "拒绝", $"{originError}（origin={origin ?? "<无>"}）");

                return Fail(request, 403, ScadaWebSecurityGate.ErrorOrigin, originError!);
            }

            // ---- ② 每来源 IP 限速（按 IP 合并计数：换用户名绕不过去）----
            var clientIp = ReadClientIp(request.Http.Request);

            if (!_securityGate.TryCheckTicketRateLimit(clientIp, out var rateError))
            {
                _log.Warn($"[ScadaWeb] 取票被拒（限速）：ip={clientIp ?? "<unknown>"}，{rateError}");

                AuditSecurity("未登录", sessionId: null, clientIp, "取票", "取票限速",
                    "取票", "拒绝", rateError ?? "超过取票速率上限");

                return Fail(request, 429, ScadaWebSecurityGate.ErrorRateLimited, rateError!,
                    ("retryAfterMs", (int)ScadaWebSecurityGate.TicketRateWindow.TotalMilliseconds));
            }

            string user = string.Empty;
            string password = string.Empty;
            string refreshToken = string.Empty;

            try
            {
                var body = request.Http.Request.DataAsString;

                // 空体不再是 400：第三种请求形态（匿名观众）本来就"没有凭据可填"。
                if (!string.IsNullOrWhiteSpace(body))
                {
                    var json = JObject.Parse(body);
                    user = json.Value<string>("user") ?? string.Empty;
                    password = json.Value<string>("password") ?? string.Empty;
                    refreshToken = json.Value<string>("refreshToken") ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                return Fail(request, 400, "E_BAD_REQUEST", $"请求体不是合法 JSON：{ex.Message}");
            }

            // 长度上限在进 PBKDF2 之前挡：十万次哈希 × 16MB 密码是现成的 CPU 放大面。
            // 与方案 §8.10 的「用户名 64 / 密码 256」对齐（2026-10-21 终审 P1-6）。
            if (user.Length > 64 || password.Length > 256 || refreshToken.Length > 64)
            {
                return Fail(request, 400, "E_BAD_REQUEST",
                    "用户名/密码/续期令牌超长（用户名 ≤64、密码 ≤256、refreshToken ≤64）");
            }

            // 用户名必须在这里归一化：ScadaUserStore.TryValidate 内部会 Trim，
            // 但签票/建会话用的是本方法的原串。不 Trim 时 "alice " 能登录成功，
            // 而 RevokeUser("alice") 在票表/在线会话/handoff 三处都匹配不到它，
            // 改密/降权/删号都踢不掉这条 8 小时会话（2026-10-21 终审新 P0-1）。
            user = user.Trim();
            refreshToken = refreshToken.Trim();

            var allowCommands = _settings.AllowCommands;

            // ---- ① 会话令牌续期（断线重连）：不要求密码 ----
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                if (!_tickets.TryResolveSession(refreshToken, clientIp, out var session, out var sessionError) || session == null)
                {
                    // 只回一条统一文案：不区分"不存在/已过期/被撤销/绑定的 IP 不符"（避免令牌枚举）。
                    // 稳定码区分"被踢"与"过期"——客户端据此决定"回登录页"还是"提示被管理员下线"。
                    var code = sessionError == ScadaWebTicketService.ErrorSessionRevoked
                        ? ScadaWebTicketService.ErrorSessionRevoked
                        : ScadaWebTicketService.ErrorSessionExpired;

                    _log.Warn($"[ScadaWeb] 会话续期被拒：session={ShortToken(refreshToken)}…（{code}）");
                    return Fail(request, 401, code, "会话已失效，请重新登录");
                }

                var renewed = _tickets.Issue(session.UserName, session.Role,
                    session.ExplicitReadOnly ?? session.ReadOnly, allowCommands: allowCommands);

                // 续期不轮换令牌：客户端手里那枚继续有效（见 BindSession 的注释）
                _tickets.BindSession(renewed, session);

                _log.Info($"[ScadaWeb] 会话续期：session={session.ShortToken}…，user={MaskUser(session.UserName)}，"
                    + $"role={session.Role.DisplayName()} → 新 ticket {renewed.ShortTicket}…（30s、单次）");

                return TicketResponse(renewed);
            }

            var hasCredentials = !string.IsNullOrWhiteSpace(user) || !string.IsNullOrEmpty(password);

            // ---- 容量预检：会话令牌表满时提前回 503 ----
            // 握手阶段表达不了 503（Watson 的 AuthResult 只有 401 语义，见 Authenticate 的注释），
            // 所以在这里先挡一次，客户端拿到的是一句能看懂的"稍后重试"而不是握手 401。
            // 只挡"会新签一枚会话令牌"的两条路径（登录 / 匿名）；续期票沿用原令牌，不占新名额。
            var sessionCapacity = _tickets.MaxSessionTokens > 0
                ? _tickets.MaxSessionTokens
                : ScadaWebTicketService.DefaultMaxSessionTokens;

            if (_tickets.SessionCount >= sessionCapacity)
            {
                _log.Warn($"[ScadaWeb] 取票被拒（会话表已满 {sessionCapacity}）：ip={clientIp ?? "<unknown>"}");

                AuditSecurity("未登录", sessionId: null, clientIp, "取票", "会话表容量",
                    "取票", "拒绝", $"会话令牌表已满（{sessionCapacity}）");

                return Fail(request, 503, ScadaWebTicketService.ErrorCapacity,
                    $"服务端会话表已满（{sessionCapacity}），请稍后重试");
            }

            // ---- ③ 匿名只读观众：只在显式配置 AllowAnonymousView=true 时签发 ----
            if (!hasCredentials)
            {
                if (!_settings.AllowAnonymousView)
                    return Fail(request, 401, "E_AUTH_REQUIRED", "需要登录后才能查看运行画面（AllowAnonymousView=false）");

                var viewer = _tickets.Issue(userName: null, ScadaRole.Operator, readOnly: true, allowCommands: allowCommands);
                _log.Info($"[ScadaWeb] 已签发匿名只读 ticket：{viewer.ShortTicket}…（30s、单次）");

                return TicketResponse(viewer);
            }

            // ---- ④ 账号校验：后台线程 ----
            var result = await Task.Run(() => ValidateCredentials(user, password)).ConfigureAwait(false);

            if (!result.Ok)
            {
                // 不区分"用户不存在"与"密码错误"（避免账号枚举）；原因只进日志
                _log.Warn($"[ScadaWeb] 登录被拒：user={MaskUser(user)}，原因={result.Error}");

                // 登录失败也是安全事件：事后要能回答"谁在什么时候试过、被拒了几次"。
                AuditSecurity(user, sessionId: null, clientIp, "取票", "Web 登录",
                    "登录", "拒绝", result.Error ?? "用户名或密码错误");

                return Fail(request, 401, "E_AUTH_FAILED", result.Error ?? "用户名或密码错误");
            }

            if (!result.Role.IsDefined())
            {
                _log.Warn($"[ScadaWeb] 登录被拒：user={MaskUser(user)}，账号角色非法（{(int)result.Role}）");
                return Fail(request, 403, "E_ROLE", "账号角色非法，已按安全策略拒绝");
            }

            // ---- ⑤ 出厂默认密码闸门（账号校验通过之后、签票之前）----
            if (!_securityGate.TryCheckDefaultPassword(_authenticator, user, out var passwordError))
            {
                _log.Warn($"[ScadaWeb] 登录被拒（默认密码）：user={MaskUser(user)}");

                AuditSecurity(user, sessionId: null, clientIp, "取票", "Web 登录",
                    "登录", "拒绝", passwordError ?? "账号仍在使用出厂默认密码");

                return Fail(request, 403, ScadaWebSecurityGate.ErrorDefaultPassword, passwordError!,
                    ("gate", "DefaultPasswordNotChanged"));
            }

            // 只读口径 = 配置总闸；匿名永远只读由 Issue 内部再压一次。
            var grant = _tickets.Issue(user, result.Role, readOnly: false, allowCommands: allowCommands);

            _log.Info($"[ScadaWeb] 已签发 ticket：{grant.ShortTicket}…，user={MaskUser(user)}，"
                + $"role={grant.Role.DisplayName()}，readOnly={grant.ReadOnly}（30s、单次）");

            // 登录成功同样留痕：与失败成对，才能看出"某账号在某段时间的登录轨迹"。
            AuditSecurity(user, sessionId: null, clientIp, "取票", "Web 登录",
                "登录", "成功", $"role={grant.Role.DisplayName()}，readOnly={grant.ReadOnly}");

            return TicketResponse(grant);
        }

        /// <summary>
        /// <c>GET /img/…</c>：M1b 接 <c>imageToken</c> 后才会真正出图；现在固定 404，
        /// 但把路由形状占住（客户端拼 URL 的行为从 M1a 就能跑通，不会 404 成"服务不存在"）。
        /// </summary>
        private Task<object> HandleImagePlaceholder(ApiRequest request)
        {
            request.Http.Response.StatusCode = 404;
            return Task.FromResult<object>(new
            {
                ok = false,
                code = "E_IMG_NOT_READY",
                message = "M1a 尚未实现图像通道（M1b 接入 imageToken）"
            });
        }

        /// <summary>
        /// ticket 响应。会话令牌只在"续期"路径上已经存在
        /// （首次登录时它要等 ticket 核销之后才签发，经 <c>hello</c> 下发），
        /// 所以这里按"有就带上"处理。
        /// </summary>
        private static object TicketResponse(ScadaWebTicketGrant grant) => new
        {
            ok = true,
            ticket = grant.Ticket,
            expiresIn = (int)ScadaWebTicketService.Lifetime.TotalSeconds,
            user = grant.UserName,
            role = grant.Role.ToString(),
            readOnly = grant.ReadOnly,
            sessionToken = grant.Session?.Token,
            sessionExpiresIn = grant.Session?.RemainingSeconds(DateTime.UtcNow) ?? 0
        };

        /// <summary>凭据校验（在后台线程执行）。<c>IScadaUserAuthenticator</c> 缺席时一律失败</summary>
        private (bool Ok, ScadaRole Role, string? Error) ValidateCredentials(string user, string password)
        {
            if (_authenticator == null)
                return (false, ScadaRole.Undefined, "服务端未注册账号校验服务（IScadaUserAuthenticator），Web 登录不可用");

            try
            {
                if (_authenticator.TryValidate(user, password, out var role, out var error))
                    return (true, role, null);

                return (false, ScadaRole.Undefined,
                    string.IsNullOrWhiteSpace(error) ? "用户名或密码错误" : error);
            }
            catch (Exception ex)
            {
                // 校验实现自己抛异常 = 服务端故障，但对外仍是"登录失败"（不泄露内部细节）
                return (false, ScadaRole.Undefined, $"账号校验异常：{ex.GetType().Name}");
            }
        }

        /// <summary>
        /// 统一的失败响应体（<c>ok/code/message</c> + 任意附加字段）。
        /// 用字典而不是匿名类型：附加字段是"按码不同"的（<c>retryAfterMs</c> / <c>gate</c>），
        /// 匿名类型没法按条件拼；字典序列化后就是平铺的 JSON 对象。
        /// </summary>
        private static object Fail(
            ApiRequest request,
            int statusCode,
            string code,
            string message,
            params (string Key, object? Value)[] extra)
        {
            request.Http.Response.StatusCode = statusCode;

            var payload = new Dictionary<string, object?>(3 + extra.Length)
            {
                ["ok"] = false,
                ["code"] = code,
                ["message"] = message
            };

            foreach (var (key, value) in extra)
                payload[key] = value;

            return payload;
        }

        private static Task DefaultRoute(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 404;
            return ctx.Response.Send("not found", CancellationToken.None);
        }

        #endregion

        #region WebSocket

        /// <summary>
        /// <c>WS /ws</c>：握手已由 <see cref="Authenticate"/> 放行（否则这里根本不会执行）。
        /// 本方法负责：取回已核销的身份 → 容量判定 → 建会话 → 发 <c>hello</c> → 单接收循环。
        /// </summary>
        private async Task HandleWebSocket(HttpContextBase ctx, WebSocketSession socket)
        {
            var ticket = ReadTicket(ctx.Request);

            // 鉴权已放行，但交接表里没有这张票 = 内部状态不一致（或 handler 被重入）。
            // 宁可断开，也不许建一条"没有身份"的会话。
            if (!_tickets.TryTakeHandoff(ticket, out var grant) || grant == null)
            {
                _log.Warn("[ScadaWeb] WS 已通过握手鉴权但取不到 ticket 授权，主动断开");
                await SafeCloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "ticket handoff missing").ConfigureAwait(false);
                return;
            }

            var maxSessions = _settings.MaxSessions > 0 ? _settings.MaxSessions : ScadaWebSettings.DefaultMaxSessions;

            // 容量按"已登记会话数"算：握手鉴权已经过了，这里拒绝不涉及身份泄露
            if (_sessions.Count >= maxSessions)
            {
                _log.Warn($"[ScadaWeb] 会话已达上限 {maxSessions}，拒绝新连接");

                // 这条连接的身份在 TryRedeem 里已经核销。只有"本次新签"的令牌才回收；
                // 续期票沿用客户端正在用的 8 小时令牌，回收它会把其它标签页一起登出
                // （2026-10-21 终审 P1-2）。
                var reclaimedToken = grant.SessionReused ? null : grant.Session?.Token;

                if (!string.IsNullOrEmpty(reclaimedToken))
                    _tickets.RevokeSession(reclaimedToken);

                AuditSecurity(grant.UserName, sessionId: null, socket.RemoteIp, "WS 握手", "会话容量",
                    "握手鉴权", "拒绝",
                    $"已达最大连接数 {maxSessions}，"
                    + (grant.SessionReused ? "续期令牌保留（不回收）" : "已回收本次新签的会话令牌")
                    + $"（ticket={grant.ShortTicket}）");

                await SafeSendAsync(socket, Json(new
                {
                    type = "bye",
                    reason = "at-capacity",
                    closeCode = 1013,
                    message = $"已达最大连接数 {maxSessions}"
                })).ConfigureAwait(false);
                await SafeCloseAsync(socket, WebSocketCloseStatus.EndpointUnavailable, "at capacity").ConfigureAwait(false);
                return;
            }

            var session = new ScadaWebSession(
                socket,
                grant,
                socket.RemoteIp ?? string.Empty,
                ctx.Request.Useragent ?? string.Empty);

            if (!_sessions.TryAdd(socket.Id, session))
            {
                _log.Warn($"[ScadaWeb] 同一连接重复进入 WS handler（{socket.Id}），忽略后到的一次");
                session.Dispose();
                return;
            }

            // 与 Stop() 的竞争窗口：Detach 之后 _cts 已置空，此时进来的连接不该再开接收循环
            // （否则它拿 CancellationToken.None 一直挂着，等不到任何取消信号）
            var cts = _cts;
            if (cts == null)
            {
                _sessions.TryRemove(socket.Id, out _);
                session.Dispose();
                await SafeCloseAsync(socket, WebSocketCloseStatus.EndpointUnavailable, "server stopping").ConfigureAwait(false);
                return;
            }

            var token = cts.Token;

            // 在线列表在 hello 之前登记：首屏快照算 caps 时要用"此刻在线的会话集合"
            // （HighestRoleOnly 的判据），晚登记会让第一个连上来的最高角色看到自己"不可操作"。
            _presence.Register(session);
            BroadcastPresence();

            _log.Info($"[ScadaWeb] WS 会话建立：ref={session.SessionRef}，ip={session.RemoteIp}，"
                + $"user={session.UserName ?? "<匿名只读>"}，readOnly={session.ReadOnly}，online={_sessions.Count}");

            var sendLoop = Task.CompletedTask;

            try
            {
                // hello 走直发：此刻还没有第二个发送者，直发能保证"首帧一定是 hello"
                await session.SendDirectAsync(BuildHello(session), token).ConfigureAwait(false);

                sendLoop = session.RunSendLoopAsync(token);

                // hello 之后立刻给首屏：运行中 → snapshot，未运行 → idle。
                // 走发送队列（不直发）：之后所有帧都排在同一队列里，顺序天然确定。
                await SendInitialStateAsync(session, token).ConfigureAwait(false);

                await ReceiveLoopAsync(session, socket, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 停服 / 客户端断开：正常收尾
            }
            catch (Exception ex)
            {
                _log.Warn($"[ScadaWeb] WS 会话异常（ref={session.SessionRef}）：{ex.GetType().Name}：{ex.Message}");
            }
            finally
            {
                session.Dispose();
                _sessions.TryRemove(socket.Id, out _);

                // 在线列表与限速表都要摘掉这条会话：留下就是"幽灵在线"与"每连一次多一个键"。
                _lease.Release(session);
                _presence.Unregister(session);
                _limiter.RemoveSession(session.SessionId);
                BroadcastPresence();

                try
                {
                    await sendLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch
                {
                    // 发送循环没能及时收尾（对端不读）——它已经拿到取消令牌，后台会自己结束
                }

                _log.Info($"[ScadaWeb] WS 会话结束：ref={session.SessionRef}，"
                    + $"sent={session.SentCount}，dropped={session.DroppedCount}，online={_sessions.Count}");
            }
        }

        /// <summary>单接收循环（<c>ReceiveAsync</c> 本身有互斥，绝不能多线程读同一 session）</summary>
        private async Task ReceiveLoopAsync(ScadaWebSession session, WebSocketSession socket, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                WebSocketMessage? message;

                try
                {
                    message = await socket.ReceiveAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _log.Warn($"[ScadaWeb] 接收失败（ref={session.SessionRef}）：{ex.GetType().Name}：{ex.Message}");
                    return;
                }

                if (message == null) return;   // 对端关闭

                session.Touch();

                if (message.MessageType == WebSocketMessageType.Close) return;

                if (message.MessageType != WebSocketMessageType.Text) continue;

                HandleTextFrame(session, message.Text);
            }
        }

        /// <summary>
        /// WS 文本帧入口（M2a）：<c>ping</c> → <c>pong</c>；<c>hello</c> → <c>hello.ok</c>；
        /// 四条 <c>cmd.*</c> → 命令泵；其余 kind 回 <c>error/E_UNSUPPORTED</c>。
        ///
        /// <b>为什么"未知 kind"仍然回 <c>E_UNSUPPORTED</c> 而不是静默丢弃</b>：前端可能已经发了
        /// 后续批次的消息（<c>lease.*</c> / <c>view.*</c>），静默丢弃会让"点了没反应"无从排查。
        /// <b>但 <c>cmd.*</c> 的四条绝不能落到这个分支</b>——M2a 之前它就是这么回 <c>E_UNSUPPORTED</c> 的，
        /// 现场会以为"命令面还没做"（实际是没接线）。
        /// </summary>
        private void HandleTextFrame(ScadaWebSession session, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            if (Encoding.UTF8.GetByteCount(text) > MaxInboundCommandBytes)
            {
                session.TrySend(Json(new
                {
                    type = "error",
                    code = ScadaWebErrorCodes.BadRequest,
                    message = $"命令帧过大（上限 {MaxInboundCommandBytes / 1024}KB）"
                }));
                return;
            }

            string? type = null;
            JObject json;

            try
            {
                json = JObject.Parse(text);
                type = json.Value<string>("type") ?? json.Value<string>("kind");
            }
            catch (JsonException)
            {
                session.TrySend(Json(new { type = "error", code = "E_BAD_JSON", message = "帧不是合法 JSON" }));
                return;
            }

            switch (type)
            {
                case "ping":
                    session.TrySend(Json(new { type = "pong", t = json.Value<string>("t") }));
                    return;

                case "hello":
                    // 客户端也会发一条 hello（方案 §9.2）。服务端已经发过首帧 hello，这里只回确认
                    session.TrySend(Json(new
                    {
                        type = "hello.ok",
                        sessionRef = session.SessionRef,
                        readOnly = IsSessionReadOnly(session)
                    }));
                    return;

                case ScadaWebCommandKinds.LeaseAcquire:
                case ScadaWebCommandKinds.LeaseRenew:
                case ScadaWebCommandKinds.LeaseRelease:
                    HandleLeaseFrame(session, type!, json);
                    return;

                case ScadaWebCommandKinds.Element:
                case ScadaWebCommandKinds.Input:
                case ScadaWebCommandKinds.AlarmAck:
                case ScadaWebCommandKinds.ConfirmRequest:
                    HandleCommandFrame(session, type!, json);
                    return;

                default:
                    session.TrySend(Json(new
                    {
                        type = "error",
                        code = ScadaWebErrorCodes.Unsupported,
                        message = $"本版本不处理该消息类型：{type ?? "<无 type>"}（命令面支持 cmd.* 与 lease.acquire/renew/release）"
                    }));
                    return;
            }
        }

        /// <summary>
        /// <c>lease.acquire / renew / release</c>（M2b，C77）。
        ///
        /// 租约操作<b>不进命令泵</b>：它不改画面/变量，只改一个加锁的内存状态；
        /// 在 WS 线程上直接处理，回执立即返回。命令执行侧仍会在 <c>CanOperate</c> 里
        /// 按同一份租约状态再判一次（TOCTOU 防线）。
        /// </summary>
        private void HandleLeaseFrame(ScadaWebSession session, string kind, JObject json)
        {
            string requestId;

            try
            {
                requestId = json.Value<string>("id") ?? json.Value<string>("requestId") ?? string.Empty;
            }
            catch (Exception)
            {
                ReplyCommandError(session, string.Empty, kind, ScadaWebErrorCodes.BadRequest,
                    "requestId 必须是字符串，本次未执行");
                return;
            }

            if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > MaxRequestIdLength)
            {
                ReplyCommandError(session, requestId, kind, ScadaWebErrorCodes.BadRequest,
                    $"requestId 缺失或超长（上限 {MaxRequestIdLength} 个字符），本次未执行");
                return;
            }

            if (!_settings.OperationLeaseEnabled)
            {
                ReplyCommandError(session, requestId, kind, ScadaWebErrorCodes.Policy,
                    "操作权租约未启用（ScadaWeb.OperationLeaseEnabled=false）");
                return;
            }

            // 租约作为"可选加强"时（AllOperatorsRequiresLease=true）必须先过与命令相同的
            // 角色/策略闸门（不含租约本身，否则死锁）：否则 HighestRoleOnly 下的普通操作员
            // 可以先占住租约，把管理员也挡在门外。
            // 仅 OperationLeaseEnabled=true、AllOperatorsRequiresLease=false 时租约只是
            // 展示/协作信息，不参与判权，任何已登录会话都可以取得/释放。
            if (kind != ScadaWebCommandKinds.LeaseRelease && _settings.AllOperatorsRequiresLease)
            {
                if (!ScadaWebAccessPolicy.Evaluate(
                        session, requiredRole: null, _settings, _presence.Sessions,
                        out var gateCode, out var gateReason))
                {
                    ReplyCommandError(session, requestId, kind, gateCode, gateReason);
                    return;
                }
            }

            bool ok;
            string? error;

            switch (kind)
            {
                case ScadaWebCommandKinds.LeaseAcquire:
                    ok = _lease.TryAcquire(session, out error);
                    break;
                case ScadaWebCommandKinds.LeaseRenew:
                    ok = _lease.TryRenew(session, out error);
                    break;
                default:
                    _lease.Release(session);
                    ok = true;
                    error = null;
                    break;
            }

            var snapshot = _lease.Snapshot();

            AuditSecurity(session.UserName, session.SessionId, session.RemoteIp,
                "租约", "Web 租约", kind,
                ok ? "成功" : "拒绝",
                $"holder={snapshot.Holder ?? "<none>"} remaining={snapshot.RemainingSeconds}s"
                + (ok ? string.Empty : $" reason={error}"));

            if (ok)
            {
                session.TrySend(Json(new
                {
                    type = "ack",
                    id = requestId,
                    requestId,
                    kind,
                    ok = true,
                    lease = new
                    {
                        enabled = _settings.OperationLeaseEnabled,
                        required = _settings.OperationLeaseEnabled && _settings.AllOperatorsRequiresLease,
                        held = snapshot.Held,
                        holder = snapshot.Holder,
                        holderSessionRef = snapshot.HolderSessionRef,
                        remainingSeconds = snapshot.RemainingSeconds
                    }
                }));
            }
            else
            {
                ReplyCommandError(session, requestId, kind,
                    ScadaWebErrorCodes.LeaseHeld,
                    error ?? "租约由他人持有",
                    result: snapshot);
            }
        }

        /// <summary>
        /// 一条 <c>cmd.*</c>：会话撤销闸 → 总闸 → 组命令 → 入队。
        /// <b>身份字段一个都不从消息里读</b>（用户名/角色/只读全在 <see cref="ScadaWebSession"/> 上）。
        ///
        /// <b>为什么总闸在这里再判一次</b>（路由里也有）：<c>AllowCommands=false</c> 时
        /// 四条命令必须<b>一律</b>回 <c>E_POLICY</c>（矩阵 C79 逐字文案）。路由里那条判据排在版本闸之后，
        /// 而"客户端没带 pageId"时它会先回 <c>E_BAD_REQUEST</c>——只读部署下这是误导。
        /// </summary>
        private void HandleCommandFrame(ScadaWebSession session, string kind, JObject json)
        {
            string requestId;

            try
            {
                requestId = json.Value<string>("id") ?? json.Value<string>("requestId") ?? string.Empty;
            }
            catch (Exception)
            {
                // id 传了对象/数组：按坏请求回执，不能让它抛到接收循环把整条连接关掉。
                ReplyCommandError(session, string.Empty, kind, ScadaWebErrorCodes.BadRequest,
                    "requestId 必须是字符串，本次未执行");
                return;
            }

            // ⓪ 请求号形状：空/超长一律 E_BAD_REQUEST，<b>不进泵、不进幂等表</b>。
            //    空 id 会让所有命令共用一个去重键（第二条起被当成重放），超长 id 则是
            //    让客户端用一个字段决定服务端键的大小——两种都必须在入口挡掉。
            if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > MaxRequestIdLength)
            {
                var echoId = requestId.Length <= MaxRequestIdLength
                    ? requestId
                    : requestId.Substring(0, MaxRequestIdLength);

                ReplyCommandError(session, echoId, kind, ScadaWebErrorCodes.BadRequest,
                    $"requestId 缺失或超长（上限 {MaxRequestIdLength} 个字符），本次未执行");
                return;
            }

            // ⓪b 图元/输入字段的形状：缺 elementId、超长、输入文本超长都按 E_BAD_REQUEST，
            //    不能等路由把它报成 E_NOT_FOUND（那会让"字段没填"看起来像"图元被删了"）。
            if (kind is ScadaWebCommandKinds.Element or ScadaWebCommandKinds.Input)
            {
                string? elementId;

                try
                {
                    elementId = json.Value<string>("elementId");
                }
                catch (Exception)
                {
                    ReplyCommandError(session, requestId, kind, ScadaWebErrorCodes.BadRequest,
                        "elementId 必须是字符串，本次未执行");
                    return;
                }

                if (string.IsNullOrWhiteSpace(elementId) || elementId.Length > 36)
                {
                    ReplyCommandError(session, requestId, kind, ScadaWebErrorCodes.BadRequest,
                        "elementId 缺失或超长（上限 36 个字符），本次未执行");
                    return;
                }

                if (kind == ScadaWebCommandKinds.Input)
                {
                    var valueText = json["value"]?.ToString(Formatting.None) ?? string.Empty;
                    if (valueText.Length > 4096)
                    {
                        ReplyCommandError(session, requestId, kind, ScadaWebErrorCodes.BadRequest,
                            "输入值超长（上限 4096 个字符），本次未执行");
                        return;
                    }
                }
            }

            // ① 会话撤销闸：撤销之后即便连接还没断干净，下一条命令也必须被拒并断开
            if (!TryValidateSessionAlive(session, out var sessionCode, out var sessionMessage))
            {
                ReplyCommandError(session, requestId, kind, sessionCode!, sessionMessage!);

                // 过期与撤销是两件事：客户端 4401 回登录卡片、4403 显示"被踢下线"。
                // 以前一律走 4403，会把"到点了"显示成"被管理员踢了"（2026-10-21 终审 P1-2）。
                if (sessionCode == ScadaWebTicketService.ErrorSessionRevoked)
                    CloseRevokedSession(session, sessionMessage!);
                else
                    CloseExpiredSession(session, sessionMessage!);

                return;
            }

            // ② 总闸：只读镜像（M2a 默认）
            if (!_settings.AllowCommands)
            {
                const string policyMessage = "当前为只读模式（ScadaWeb.AllowCommands=false）";

                // 这条拒绝发生在路由之前（路由那条总闸排在版本闸之后），所以命令级审计
                // 覆盖不到它——只读部署下"谁在试"只能靠这一行。
                AuditSecurity(session.UserName, session.SessionId, session.RemoteIp, kind, "命令",
                    "Web 命令", "拒绝", $"{policyMessage}（request={requestId}）");

                ReplyCommandError(session, requestId, kind, ScadaWebErrorCodes.Policy, policyMessage);
                return;
            }

            var pump = _pump;

            if (pump == null)
            {
                ReplyCommandError(session, requestId, kind,
                    ScadaWebErrorCodes.Internal,
                    "服务端未接入运行态（IScadaRunHub），命令面不可用");
                return;
            }

            // ③ 组命令（字段形状错误在这里挡掉，不占队列）
            //    Value<T> 遇到对象/数组会抛；必须在这里转成 E_BAD_REQUEST，
            //    否则异常会沿接收循环关闭整条连接（2026-10-21 终审 P1-3）。
            ScadaWebCommand? command;
            string? buildError;

            try
            {
                if (!TryBuildCommand(session, kind, requestId, json, out command, out buildError))
                {
                    ReplyCommandError(session, requestId, kind, ScadaWebErrorCodes.BadRequest, buildError!);
                    return;
                }
            }
            catch (Exception ex)
            {
                ReplyCommandError(session, requestId, kind, ScadaWebErrorCodes.BadRequest,
                    $"命令字段类型不合法：{ex.Message}");
                return;
            }

            // ④ 入队；满时回 E_BUSY + retryAfterMs（客户端用同一 requestId 重试，幂等表会兜住重复执行）
            if (!pump.Enqueue(session, command!, out var rejection))
            {
                var failed = rejection ?? ScadaWebCommandResult.Fail(ScadaWebErrorCodes.Busy, "命令队列已满，请稍后重试");

                // 队列满也是"被拒的命令"：审计里要能看出"有人按了、服务端没接住"，
                // 否则现场只会看到"点了没反应"，而服务端日志里一行都没有。
                AuditSecurity(session.UserName, session.SessionId, session.RemoteIp, kind, "命令",
                    "Web 命令", "拒绝",
                    $"{failed.Code}：{failed.Message ?? "命令队列已满，请稍后重试"}（request={requestId}）");

                ReplyCommandError(session, requestId, kind, failed.Code,
                    failed.Message ?? "命令队列已满，请稍后重试",
                    failed.Result,
                    failed.Code == ScadaWebErrorCodes.Busy ? 200 : null);
            }
        }

        /// <summary>
        /// 会话令牌是否仍然有效（撤销/过期）。匿名观众与"没拿到会话令牌"的连接不在这里判——
        /// 前者由权限层按只读处理，后者是内部状态不一致（握手时一定会签发）。
        /// </summary>
        private bool TryValidateSessionAlive(ScadaWebSession session, out string? code, out string? message)
        {
            code = null;
            message = null;

            // 每帧现读（与 Authenticate / HandleTicket 同一条口径）：关掉匿名访问之后，
            // 已连着的匿名会话在下一条命令上就该被判失效，而不是等它下次取票。
            _tickets.AllowAnonymousView = _settings.AllowAnonymousView;

            var grant = session.Session;
            if (grant == null) return true;

            if (_tickets.TryResolveSession(grant.Token, session.RemoteIp, out _, out var errorCode))
                return true;

            if (errorCode == ScadaWebTicketService.ErrorSessionRevoked)
            {
                code = ScadaWebTicketService.ErrorSessionRevoked;
                message = "会话已被撤销，请重新登录";
            }
            else
            {
                code = ScadaWebTicketService.ErrorSessionExpired;
                message = "会话已过期，请重新登录";
            }

            return false;
        }

        /// <summary>
        /// 把一帧 WS 文本解析成 <see cref="ScadaWebCommand"/>。
        /// 字段名与 <c>wwwroot\app.js</c> 的 <c>sendCommand</c> 逐字对齐（<c>id</c> / <c>pageId</c> /
        /// <c>pageVersion</c> / <c>runGeneration</c>），同时接受 <c>requestId</c> 作为别名（探针/网关）。
        /// </summary>
        private static bool TryBuildCommand(
            ScadaWebSession session,
            string kind,
            string requestId,
            JObject json,
            out ScadaWebCommand? command,
            out string? error)
        {
            command = null;
            error = null;

            ScadaEventType? eventType = null;
            var eventText = json.Value<string>("event");

            if (!string.IsNullOrWhiteSpace(eventText))
            {
                if (!Enum.TryParse<ScadaEventType>(eventText, ignoreCase: true, out var parsed))
                {
                    error = $"event 不是合法的事件类型：{eventText}";
                    return false;
                }

                eventType = parsed;
            }

            if (kind == ScadaWebCommandKinds.Element && eventType == null)
            {
                error = "cmd.element 缺少 event 字段（如 Pressed / Released）";
                return false;
            }

            Guid? alarmId = null;
            var alarmText = json.Value<string>("alarmId");

            if (!string.IsNullOrWhiteSpace(alarmText))
            {
                if (!Guid.TryParse(alarmText, out var parsedAlarm))
                {
                    error = $"alarmId 不是合法的 Guid：{alarmText}";
                    return false;
                }

                alarmId = parsedAlarm;
            }

            command = new ScadaWebCommand
            {
                Kind = kind,
                RequestId = requestId,
                Session = session,
                ElementId = json.Value<string>("elementId"),
                Event = eventType,
                Property = json.Value<string>("property"),
                Value = ReadScalarText(json, "value"),
                PageId = json.Value<string>("pageId"),
                PageVersion = json.Value<int?>("pageVersion") ?? 0,
                RunGeneration = json.Value<long?>("runGeneration") ?? 0,
                ConfirmToken = json.Value<string>("confirmToken") ?? json.Value<string>("token"),
                AlarmId = alarmId,
                All = json.Value<bool?>("all") ?? false
            };

            return true;
        }

        /// <summary>
        /// 把 <c>value</c> 读成"操作员原文"：数字/布尔按字面量、对象/数组按紧凑 JSON、缺省为 <c>null</c>。
        /// 不做类型转换——目标类型由宿主写入器按变量声明类型统一解析（R34 口径）。
        /// </summary>
        private static string? ReadScalarText(JObject json, string name)
        {
            var token = json[name];

            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                return null;

            return token.Type is JTokenType.Object or JTokenType.Array
                ? token.ToString(Formatting.None)
                : token.ToString();
        }

        /// <summary>一条命令的失败回执（<c>id</c> 必须回给客户端——它靠它配对 pending 命令）</summary>
        private static void ReplyCommandError(
            ScadaWebSession session,
            string requestId,
            string kind,
            string code,
            string message,
            object? result = null,
            int? retryAfterMs = null)
        {
            var payload = new Dictionary<string, object?>
            {
                ["type"] = "error",
                ["id"] = requestId,
                ["requestId"] = requestId,
                ["kind"] = kind,
                ["ok"] = false,
                ["code"] = code,
                ["message"] = message,
                ["result"] = result
            };

            // 只有"队列满"带重试建议：其它失败重试也不会变（矩阵 C74 ④ 用同一 requestId 重试）。
            if (retryAfterMs.HasValue)
                payload["retryAfterMs"] = retryAfterMs.Value;

            session.TrySend(Json(payload));
        }

        /// <summary>
        /// 关闭一条被撤销的会话：先塞一条 <c>bye</c>（带稳定码，客户端据此回登录页），再按 4403 关连接。
        /// </summary>
        private void CloseRevokedSession(ScadaWebSession session, string message)
        {
            session.TrySend(Json(new
            {
                type = "bye",
                reason = "revoked",
                closeCode = RevokedCloseCode,
                message
            }));

            _ = session.CloseAsync((WebSocketCloseStatus)RevokedCloseCode, "session revoked");
        }

        /// <summary>
        /// 关闭一条自然过期的会话：与撤销分开用 4401，客户端据此回登录卡片而不是显示"被踢下线"。
        /// </summary>
        private void CloseExpiredSession(ScadaWebSession session, string message)
        {
            session.TrySend(Json(new
            {
                type = "bye",
                reason = "expired",
                closeCode = ExpiredCloseCode,
                message
            }));

            _ = session.CloseAsync((WebSocketCloseStatus)ExpiredCloseCode, "session expired");
        }

        /// <summary>
        /// 首帧。任务口径：<c>{"type":"hello","sessionRef":…,"readOnly":true}</c>；
        /// 额外字段（running/pageId/…）是给前端做"首屏就知道要不要等运行"的，不属于协议必需项。
        ///
        /// <b><c>sessionToken</c> / <c>sessionExpiresIn</c></b>（M1b 重连）：
        /// 这是<b>可复用</b>的重连凭据（8 小时），客户端必须保存；断线后拿它
        /// <c>POST /api/ticket {"refreshToken":…}</c> 换新票，不必重新登录。
        /// 它与 <c>ticket</c> 是两种东西：ticket 单次、30 秒，只用于本次握手。
        /// </summary>
        /// <summary>
        /// 会话级"当前是否只读"：必须与 <see cref="ScadaWebAccessPolicy.CanOperate"/> 的
        /// ①总闸 / ②未登录 / ③显式只读三道闸同源，而不是签票时冻结的 <see cref="ScadaWebSession.ReadOnly"/>。
        ///
        /// <b>为什么现算</b>：AllowCommands 是运行期可切换的（M2a 紧急关断）。若 hello/caps 继续下发
        /// 签票时的旧值，客户端会在"配置已放开"后仍被 <c>state.readOnly</c> 短路成不可点，
        /// 或者"配置已关闭"后仍显示可操作（2026-10-21 终审 P0-C）。
        /// </summary>
        private bool IsSessionReadOnly(ScadaWebSession session)
            => !_settings.AllowCommands
               || !session.IsAuthenticated
               || (session.Grant?.ExplicitReadOnly ?? session.ReadOnly);

        private string BuildHello(ScadaWebSession session)
        {
            var snapshot = ReadRunSnapshot();
            var sessionGrant = session.Session;

            return Json(new
            {
                type = "hello",
                protocol = ProtocolVersion,
                server = ServerVersion,
                sessionRef = session.SessionRef,
                readOnly = IsSessionReadOnly(session),
                user = session.UserName,
                role = session.Role.ToString(),
                running = snapshot.Running,
                pageId = snapshot.PageId,
                pageName = snapshot.PageName,
                pageVersion = snapshot.PageVersion,
                online = _sessions.Count,
                sessionToken = sessionGrant?.Token,
                sessionExpiresIn = sessionGrant?.RemainingSeconds(DateTime.UtcNow) ?? 0
            });
        }

        #endregion

        #region 运行态镜像（快照 / 值帧）

        /// <summary>
        /// 订阅运行态启停。<b>处理器必须自己 try/catch</b>（接口契约要求），
        /// 且只做"往会话队列里塞一条消息"这种不会抛的事。
        /// </summary>
        private void SubscribeHubEvents()
        {
            if (_hub == null) return;

            _hub.RunStarted += OnRunStarted;
            _hub.RunStopped += OnRunStopped;
        }

        private void UnsubscribeHubEvents()
        {
            if (_hub == null) return;

            try
            {
                _hub.RunStarted -= OnRunStarted;
                _hub.RunStopped -= OnRunStopped;
            }
            catch (Exception ex)
            {
                _log.Warn($"[ScadaWeb] 退订运行态事件失败：{ex.GetType().Name}：{ex.Message}");
            }
        }

        /// <summary>
        /// 新一轮运行开始：把每个会话的"当前页键"作废 → 下一拍发 <b>完整 snapshot</b>
        /// （不是 <c>page</c>：新的一轮可能换了方案/换了页，客户端本地结构必须整份重来）。
        /// </summary>
        private void OnRunStarted(ScadaRuntime session, ScadaDocument document, long generation)
        {
            try
            {
                _structureKey = null;
                _structurePage = null;

                foreach (var item in _sessions.Values)
                    item.ResetPageKey();

                _loopSawRunning = true;

                _log.Info($"[ScadaWeb] 运行开始（generation={generation}）：已通知 {_sessions.Count} 个会话重取快照");
            }
            catch (Exception ex)
            {
                _log.Warn($"[ScadaWeb] 处理运行开始事件失败：{ex.GetType().Name}：{ex.Message}");
            }
        }

        /// <summary>
        /// 运行结束：<b>立即</b>广播 <c>stopped</c>（不等 100ms 那一拍），并作废页键。
        /// 之后循环还会自己发现"没在跑"，但那时 <see cref="_loopSawRunning"/> 已被清掉，不会重复发。
        /// </summary>
        private void OnRunStopped(long generation)
        {
            try
            {
                _loopSawRunning = false;
                _structureKey = null;
                _structurePage = null;

                var message = Json(new { type = "stopped", runGeneration = generation });
                foreach (var item in _sessions.Values)
                {
                    item.ResetPageKey();
                    item.TrySend(message);
                }

                _log.Info($"[ScadaWeb] 运行结束（generation={generation}）：已通知 {_sessions.Count} 个会话");
            }
            catch (Exception ex)
            {
                _log.Warn($"[ScadaWeb] 处理运行结束事件失败：{ex.GetType().Name}：{ex.Message}");
            }
        }

        /// <summary>
        /// 宿主级值帧循环（10Hz）：拍一次 → 序列化一次 → 扇出给所有会话。
        /// 循环常驻到服务停止为止；"运行结束"表现为<b>不再发帧</b>并补一条 <c>stopped</c>，
        /// 而不是把循环销毁再重建（重建要处理"停止期间有新会话连入"的竞态，得不偿失）。
        /// </summary>
        private async Task RunFrameLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(FrameIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    await PumpFrameAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // 一拍失败不许把循环打死（否则"画面停在某一帧"且没有任何后续帧）
                    _log.Warn($"[ScadaWeb] 值帧循环异常：{ex.GetType().Name}：{ex.Message}");
                }
            }
        }

        /// <summary>一拍：在 UI 线程拍快照/解析值，然后在当前线程序列化并广播</summary>
        private async Task PumpFrameAsync(CancellationToken token)
        {
            // 没人看就不拍：构造快照要占 UI 线程，空转 10Hz 是白花的
            if (_sessions.IsEmpty)
                return;

            var state = await RunOnUiAsync(CaptureLiveState, token).ConfigureAwait(false);

            if (!state.Running || state.Page is null)
            {
                // 停运行后缓存必须作废：下一轮可能换方案/换页，旧快照的 PageId/Version 不能复用。
                _structureKey = null;
                _structurePage = null;

                if (_loopSawRunning)
                {
                    _loopSawRunning = false;

                    var stopped = Json(new { type = "stopped" });
                    foreach (var session in _sessions.Values)
                    {
                        session.ResetPageKey();
                        if (!session.TrySend(stopped))
                        {
                            // 队列满的慢消费者收不到停运行：直接断开，避免它停在旧画面继续显示。
                            _ = session.CloseAsync(
                                System.Net.WebSockets.WebSocketCloseStatus.EndpointUnavailable,
                                "outbound-full");
                        }
                    }
                }

                return;
            }

            _loopSawRunning = true;

            var pageKey = state.PageKey;

            // 快照/结构消息<b>按会话序列化</b>：caps（CanOperate/OperateReason）是逐会话算的
            // （角色 + 最高角色策略 + 只读票），共用一份会把"谁能点"广播成同一个答案。
            // 值帧也改成按会话：增量基线是"每个连接已经成功收到哪些值"，共享一份会让
            // 慢消费者丢掉的变化在别人的成功里被误记成"已送达"。
            foreach (var session in _sessions.Values)
            {
                var previousKey = session.CurrentPageKey;

                if (!string.Equals(previousKey, pageKey, StringComparison.Ordinal))
                {
                    // 第一次拿到页面 → snapshot（客户端要整份结构）；同一轮里结构变了 → page（增量结构）
                    bool sent;
                    if (previousKey is null)
                    {
                        sent = session.TrySend(BuildSnapshotMessage(state, session));
                    }
                    else
                    {
                        sent = session.TrySend(BuildPageMessage(state, session));
                    }

                    // 只有真的入队成功才推进页键；失败（出站队列满）就保持旧键，下一拍重试。
                    // 先推进再发送会让客户端永远停在旧结构上——这是 M1c 复评抓到的静默冻结。
                    if (sent)
                    {
                        session.CurrentPageKey = pageKey;
                        // 结构变了：增量基线作废，下一帧必须是整帧（客户端也要按新结构重建值）。
                        session.ResetDeltaForNewStructure();
                    }

                    continue;
                }

                if (state.Items.Count == 0)
                    continue;

                SendFrame(state, session);
            }
        }

        /// <summary>
        /// 在 UI 线程上取"此刻该画什么 + 值是什么"。
        /// <b>只读</b>：不写模型、不订阅、不碰撤销栈（<see cref="ScadaPageSnapshotBuilder"/> 的契约）。
        /// </summary>
        private LiveState CaptureLiveState()
        {
            if (_hub == null)
                return LiveState.NotRunning;

            try
            {
                // TryGetCurrent 是给晚订阅者的入口：未运行时三个 out 一律 null/0（不返回上一轮残留）
                if (!_hub.TryGetCurrent(out var session, out _, out var generation) || session is null)
                    return LiveState.NotRunning;

                // 结构键覆盖：运行轮次、本页标量（名称/尺寸/底色/弹窗标记，这些 setter 不升 Version）、
                // 有序基座身份+版本（BasePageVersion 是求和，对换序不敏感）、弹窗及其基座。
                var key = BuildStructureKey(session, generation);
                ScadaPageSnapshot? page;

                if (string.Equals(_structureKey, key, StringComparison.Ordinal) && _structurePage != null)
                {
                    page = _structurePage;
                }
                else
                {
                    page = _builder.Build(session);
                    if (page is null)
                        return LiveState.NotRunning;

                    Interlocked.Increment(ref _structureBuildCount);
                    _structureKey = key;
                    _structurePage = page;
                }

                var items = _values is null
                    ? (IReadOnlyList<ScadaFrameItemDto>)Array.Empty<ScadaFrameItemDto>()
                    : _resolver.Resolve(page, _values);

                return new LiveState(true, generation, page, items, key);
            }
            catch (Exception ex)
            {
                // hub 是 UI 线程写的，这里也在 UI 线程；真出异常也不能让循环/会话挂掉
                _log.Warn($"[ScadaWeb] 拍摄页面快照失败：{ex.GetType().Name}：{ex.Message}");
                return LiveState.NotRunning;
            }
        }

        /// <summary>握手后的首屏：运行中给 <c>snapshot</c>，否则给 <c>idle</c></summary>
        private async Task SendInitialStateAsync(ScadaWebSession session, CancellationToken token)
        {
            var state = await RunOnUiAsync(CaptureLiveState, token).ConfigureAwait(false);

            if (!state.Running || state.Page is null)
            {
                session.ResetPageKey();
                session.TrySend(Json(new { type = "idle" }));
                return;
            }

            // 先发、成功才推进页键：失败（出站队列满）时下一拍必须重试整份快照，
            // 先推进会让客户端永远停在"没有结构"的状态上。
            if (session.TrySend(BuildSnapshotMessage(state, session)))
            {
                session.CurrentPageKey = state.PageKey;
                session.ResetDeltaForNewStructure();
            }
        }

        /// <summary>
        /// 快照 / 结构消息（<b>逐会话</b>：caps 按会话算）。<c>page</c> 里的 <c>CanOperate</c>
        /// 与命令执行走同一个判据（<see cref="ScadaWebAccessPolicy.CanOperate(ScadaWebSession, VisionMaster.Scada.Snapshots.ScadaElementSnapshot, out string)"/>），
        /// 所以"界面上能点、点下去被拒"这类不一致在源头就不存在。
        /// </summary>
        private string BuildSnapshotMessage(LiveState state, ScadaWebSession session) => JsonWire(new
        {
            type = "snapshot",
            seq = Interlocked.Increment(ref _seq),
            runGeneration = state.Generation,
            pageId = state.Page!.PageId,
            pageVersion = state.Page.PageVersion,
            basePageVersion = state.Page.BasePageVersion,
            page = ApplyCaps(state.Page, session)
        });

        private string BuildPageMessage(LiveState state, ScadaWebSession session) => JsonWire(new
        {
            type = "page",
            seq = Interlocked.Increment(ref _seq),
            runGeneration = state.Generation,
            pageId = state.Page!.PageId,
            pageVersion = state.Page.PageVersion,
            basePageVersion = state.Page.BasePageVersion,
            page = ApplyCaps(state.Page, session)
        });

        /// <summary>
        /// 把一张页面快照的每个图元补上 <c>CanOperate</c>/<c>OperateReason</c>（本页 + 基座页）。
        /// <b>只读</b>：不改原快照（它是共享的、且被声明为不可变），返回一份带 caps 的副本；
        /// 属性袋/绑定/动画是只读集合，直接共用（不再复制一遍，省一次整页深拷贝）。
        /// </summary>
        private ScadaPageSnapshot ApplyCaps(ScadaPageSnapshot page, ScadaWebSession session)
        {
            // 报警计数与在线列表各读一次，本页 + 基座共用；避免每个图元都复制一次在线数组。
            var unacknowledged = _alarmFeed?.Snapshot().UnacknowledgedCount ?? 0;
            var online = _policy.OnlineSnapshot(session);
            var elements = new List<ScadaElementSnapshot>(page.Elements.Count);

            foreach (var element in page.Elements)
                elements.Add(ApplyCaps(element, session, unacknowledged, online));

            return new ScadaPageSnapshot
            {
                PageId = page.PageId,
                PageName = page.PageName,
                PageVersion = page.PageVersion,
                BasePageVersion = page.BasePageVersion,
                Width = page.Width,
                Height = page.Height,
                Background = page.Background,
                BasePage = page.BasePage == null
                    ? null
                    : ApplyCaps(page.BasePage, session, unacknowledged, online),
                BasePages = page.BasePages
                    .Select(b => ApplyCaps(b, session, unacknowledged, online))
                    .ToList()
                    .AsReadOnly(),
                IsPopup = page.IsPopup,
                PopupAnchor = page.PopupAnchor,
                PopupModal = page.PopupModal,
                Popup = page.Popup == null
                    ? null
                    : ApplyCaps(page.Popup, session, unacknowledged, online),
                Elements = elements.AsReadOnly()
            };
        }

        private ScadaPageSnapshot ApplyCaps(
            ScadaPageSnapshot page,
            ScadaWebSession session,
            int unacknowledged,
            IReadOnlyList<ScadaWebSession> online)
        {
            var elements = new List<ScadaElementSnapshot>(page.Elements.Count);

            foreach (var element in page.Elements)
                elements.Add(ApplyCaps(element, session, unacknowledged, online));

            return new ScadaPageSnapshot
            {
                PageId = page.PageId,
                PageName = page.PageName,
                PageVersion = page.PageVersion,
                BasePageVersion = page.BasePageVersion,
                Width = page.Width,
                Height = page.Height,
                Background = page.Background,
                BasePage = page.BasePage == null
                    ? null
                    : ApplyCaps(page.BasePage, session, unacknowledged, online),
                BasePages = page.BasePages
                    .Select(b => ApplyCaps(b, session, unacknowledged, online))
                    .ToList()
                    .AsReadOnly(),
                IsPopup = page.IsPopup,
                PopupAnchor = page.PopupAnchor,
                PopupModal = page.PopupModal,
                Popup = page.Popup == null
                    ? null
                    : ApplyCaps(page.Popup, session, unacknowledged, online),
                Elements = elements.AsReadOnly()
            };
        }

        private ScadaElementSnapshot ApplyCaps(
            ScadaElementSnapshot element,
            ScadaWebSession session,
            int unacknowledged,
            IReadOnlyList<ScadaWebSession> online)
        {
            var canOperate = _policy.CanOperate(session, element, online, out _, out var reason);

            // AckButton 的 UnacknowledgedCount 是控件 DP，不在模型属性袋里；
            // Web 渲染器从属性袋读这个键（elements-interactive.js 的 countNow）。
            // 在这里补一份运行期投影，不写模型、不影响桌面。
            IReadOnlyDictionary<string, string> properties = element.Properties;

            if (string.Equals(element.TypeKey, "Hmi.AckButton", StringComparison.Ordinal))
            {
                var copy = new Dictionary<string, string>(element.Properties)
                {
                    ["UnacknowledgedCount"] = unacknowledged.ToString(CultureInfo.InvariantCulture)
                };

                properties = new ReadOnlyDictionary<string, string>(copy);
            }

            return new ScadaElementSnapshot
            {
                ElementId = element.ElementId,
                TypeKey = element.TypeKey,
                Name = element.Name,
                X = element.X,
                Y = element.Y,
                Width = element.Width,
                Height = element.Height,
                Rotation = element.Rotation,
                ZIndex = element.ZIndex,
                ParentId = element.ParentId,
                LayerId = element.LayerId,
                LayerVisible = element.LayerVisible,
                RequiredRole = element.RequiredRole,
                Properties = properties,
                Bindings = element.Bindings,
                Animations = element.Animations,
                CanOperate = canOperate,
                OperateReason = canOperate ? null : reason
            };
        }

        /// <summary>
        /// 给一个会话发一帧：增量（只含变化项）或整帧（首帧/结构变更后/每 10 秒）。
        ///
        /// <b>基线只在 <c>TrySend</c> 成功之后推进</b>：队列满时这一帧没出去，下一拍必须
        /// 重新带上同一批变化；否则客户端会永久停在旧值上，直到下一次 keyframe。
        ///
        /// <b>预算按 UTF-8 字节、整条 item 裁</b>：超预算的项留在"未送达"状态，下一拍继续；
        /// 游标轮转让 1000 项全变时尾部不会被永久饿死。整帧（keyframe）不裁预算——
        /// 它每 10 秒才一次，10Hz 的预算不该把"自愈帧"也切成半截。
        /// </summary>
        private void SendFrame(LiveState state, ScadaWebSession session)
        {
            var now = DateTime.UtcNow;
            var keyframe = session.NeedsKeyframe
                || now - session.LastKeyframeUtc >= KeyframeInterval;

            var items = state.Items;
            if (items.Count == 0)
                return;

            var included = new List<KeyValuePair<string, string>>(
                keyframe ? items.Count : Math.Min(items.Count, 64));

            var sb = new StringBuilder(Math.Max(512, items.Count * 64));
            var frameSeq = session.FrameSeq + 1;

            sb.Append("{\"type\":\"frame\",\"seq\":").Append(Interlocked.Increment(ref _seq))
              .Append(",\"frameSeq\":").Append(frameSeq)
              .Append(",\"keyframe\":").Append(keyframe ? "true" : "false")
              .Append(",\"runGeneration\":").Append(state.Generation)
              .Append(",\"pageId\":\"").Append(state.Page!.PageId).Append('"')
              .Append(",\"pageVersion\":").Append(state.Page.PageVersion)
              .Append(",\"items\":[");

            var usedBytes = Encoding.UTF8.GetByteCount(sb.ToString());
            var stoppedForBudget = false;
            var count = items.Count;
            var start = count > 0 ? session.DeltaCursor % count : 0;
            var nextCursor = 0;

            for (var n = 0; n < count; n++)
            {
                var index = (start + n) % count;
                var item = items[index];
                if (item is null)
                    continue;

                var key = item.ElementId + "\u001F" + item.Property;
                var json = JsonConvert.SerializeObject(item, WireJson);

                if (!keyframe
                    && session.LastValues.TryGetValue(key, out var previous)
                    && string.Equals(previous, json, StringComparison.Ordinal))
                {
                    continue;
                }

                var bytes = Encoding.UTF8.GetByteCount(json) + (included.Count == 0 ? 0 : 1);

                // 增量帧按预算裁；至少放一条（单条超预算也要发，否则这条永远出不去）。
                if (!keyframe && included.Count > 0 && usedBytes + bytes > _frameBudgetBytes)
                {
                    stoppedForBudget = true;
                    nextCursor = index;
                    break;
                }

                if (included.Count > 0)
                    sb.Append(',');

                sb.Append(json);
                usedBytes += bytes;
                included.Add(new KeyValuePair<string, string>(key, json));
            }

            // 没有变化也发一条空帧：保持 10Hz 心跳与 frameSeq 连续。
            // 空帧约 150B，16 端合计 ~24kbps，换来的是"客户端能区分'没变化'与'连接死了'"。
            sb.Append("]}");

            if (!session.TrySend(sb.ToString()))
                return;   // 入队失败：基线不推进，下一拍重发同一批变化

            foreach (var pair in included)
                session.LastValues[pair.Key] = pair.Value;

            session.FrameSeq = frameSeq;
            session.NeedsKeyframe = false;
            session.DeltaCursor = stoppedForBudget ? nextCursor : 0;

            if (keyframe)
                session.LastKeyframeUtc = now;
        }

        /// <summary>
        /// 结构键：决定"要不要重建整页快照 / 要不要给客户端发 <c>page</c>"。
        ///
        /// <b>为什么不能只看 <c>pageId@pageVersion@basePageVersion</c></b>
        /// ---------
        /// ① 基座版本是求和，对"两只基座换序/同版本替换"不敏感，也不含基座身份；
        /// ② 页面标量（名称/宽高/底色/弹窗标记/弹窗锚点）走 <c>SetProperty</c>，<b>不升 Version</b>；
        /// ③ 弹窗打开/关闭不动 <c>CurrentPage.Version</c>（<c>ScadaRuntime.OpenPopup</c> 只换
        ///    <c>CurrentPopup</c>），旧键会让弹窗永远推不出去；
        /// ④ 跨轮 <c>Page.Version</c> 可能归零（<c>[JsonIgnore]</c> 不落盘），必须带 runGeneration。
        ///
        /// 所以键 = 运行轮次 + 本页身份/版本/标量哈希 + 有序基座身份/版本 + 弹窗及弹窗基座。
        /// 键只用于"是否重建/是否发结构消息"，不发给客户端。
        /// </summary>
        private static string BuildStructureKey(ScadaRuntime session, long generation)
        {
            var page = session.CurrentPage;
            if (page is null)
                return string.Empty;

            var sb = new StringBuilder(192);
            sb.Append(generation.ToString(CultureInfo.InvariantCulture)).Append('|');
            AppendPageIdentity(sb, page);

            foreach (var basePage in session.CurrentBasePages)
            {
                sb.Append(';');
                AppendPageIdentity(sb, basePage);
            }

            sb.Append("|p|");
            var popup = session.CurrentPopup;
            if (popup is not null)
            {
                AppendPageIdentity(sb, popup);
                foreach (var basePage in session.CurrentPopupBasePages)
                {
                    sb.Append(';');
                    AppendPageIdentity(sb, basePage);
                }
            }

            return sb.ToString();
        }

        /// <summary>页面身份段：Id/Version 显式拼、标量走 FNV-1a 64（避免把整段用户文本塞进每拍比较的键）。</summary>
        private static void AppendPageIdentity(StringBuilder sb, ScadaPage page)
        {
            sb.Append(page.PageId.ToString("N")).Append(':')
              .Append(page.Version.ToString(CultureInfo.InvariantCulture)).Append(':');

            ulong hash = 14695981039346656037UL;
            hash = Fnv(hash, page.Name);
            hash = Fnv(hash, page.Background);
            hash = Fnv(hash, page.Width.ToString("R", CultureInfo.InvariantCulture));
            hash = Fnv(hash, page.Height.ToString("R", CultureInfo.InvariantCulture));
            hash = Fnv(hash, page.IsPopup ? "1" : "0");
            hash = Fnv(hash, page.PopupAnchor.ToString());
            hash = Fnv(hash, page.PopupModal ? "1" : "0");

            sb.Append(hash.ToString("X16"));
        }

        private static ulong Fnv(ulong hash, string? text)
        {
            if (string.IsNullOrEmpty(text))
                return hash;

            foreach (var ch in text)
            {
                hash ^= ch;
                hash *= 1099511628211UL;
            }

            return hash;
        }

        /// <summary>
        /// 把一段读模型的动作放到 UI 线程执行。
        /// 没有 Dispatcher（断言/探针宿主）时直接调用——那里没有 UI 线程，也就没有跨线程问题。
        /// </summary>
        private async Task<T> RunOnUiAsync<T>(Func<T> func, CancellationToken token)
        {
            var dispatcher = _uiDispatcher;

            if (dispatcher is null || dispatcher.CheckAccess())
                return func();

            return await dispatcher.InvokeAsync(func, DispatcherPriority.Normal, token).Task.ConfigureAwait(false);
        }

        /// <summary>一拍的结果（快照是不可变的，拍完就可以安全地交给后台线程序列化）</summary>
        private sealed record LiveState(
            bool Running,
            long Generation,
            ScadaPageSnapshot? Page,
            IReadOnlyList<ScadaFrameItemDto> Items,
            string PageKey)
        {
            public static readonly LiveState NotRunning =
                new(false, 0, null, Array.Empty<ScadaFrameItemDto>(), string.Empty);
        }

        #endregion

        #region 在线列表 / 撤销

        /// <summary>
        /// 在线列表广播循环（2 秒一拍）。没人看就不发（空会话表时直接跳过）。
        /// 与值帧循环一样：一拍失败只记 Warn，循环不退出（否则列表永远停在最后一份）。
        /// </summary>
        private async Task RunPresenceLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(PresenceIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    SweepDeadSessions();
                    BroadcastPresence();
                    await BroadcastCapsAsync(token).ConfigureAwait(false);
                    BroadcastAlarms();
                }
                catch (Exception ex)
                {
                    _log.Warn($"[ScadaWeb] 在线列表/报警广播异常：{ex.GetType().Name}：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 回收"既没有入站、也没有成功出站"的死连接（默认 45 秒，配置为 0 时关闭）。
        ///
        /// <b>双条件</b>：只看入站会误杀被浏览器节流的后台标签页（ping 定时器被降到 1/min）；
        /// 只看出站会杀掉所有空闲页（未运行时本来就没有值帧）。两个方向都停滞才是死连接。
        /// <b>豁免</b>：撤销中的会话交给撤销路径（否则 B43 拿不到 4403）；命令排队/执行中的
        /// 会话不算死（长命令可能占住 UI 线程几十秒）。
        /// </summary>
        private void SweepDeadSessions()
        {
            var seconds = _settings.SessionIdleTimeoutSeconds;
            if (seconds <= 0 || _sessions.IsEmpty)
                return;

            var timeout = TimeSpan.FromSeconds(seconds);
            var now = DateTime.UtcNow;

            foreach (var pair in _sessions)
            {
                var session = pair.Value;

                if (session.IsRevoked)
                    continue;

                if (_pump?.IsSessionBusy(session.SessionId) == true)
                    continue;

                var lastIn = session.LastSeenUtc;
                var lastOutTicks = session.LastSendSucceededUtcTicks;
                var lastOut = lastOutTicks > 0
                    ? new DateTime(lastOutTicks, DateTimeKind.Utc)
                    : session.ConnectedAtUtc;

                if (now - lastIn < timeout || now - lastOut < timeout)
                    continue;

                if (!_sessions.TryRemove(pair.Key, out var removed))
                    continue;

                _log.Warn($"[ScadaWeb] 会话 {removed.SessionRef} 超过 {seconds}s 无收发进展，按死连接回收");
                _ = CloseIdleSessionAsync(removed, seconds);
            }
        }

        private async Task CloseIdleSessionAsync(ScadaWebSession session, int seconds)
        {
            try
            {
                await SafeSendAsync(session.Socket, Json(new
                {
                    type = "bye",
                    reason = "idle-timeout",
                    closeCode = 4408,
                    message = $"连接 {seconds} 秒无收发进展"
                })).ConfigureAwait(false);
            }
            catch
            {
                // 对端已经不读了：下面照样关连接
            }

            try
            {
                await session.CloseAsync((WebSocketCloseStatus)4408, "idle-timeout").ConfigureAwait(false);
            }
            catch
            {
                // 已断开
            }
        }

        /// <summary>
        /// 广播一帧 <c>presence</c>。字段<b>只有</b> <c>sessionRef/user/role/ip/connectedAtUtc/lastSeenUtc/canOperate</c>：
        /// <c>SessionId</c> / 会话令牌 / ticket / UserAgent 一律不出现在这一帧里
        /// （在线列表发给所有浏览器，带上稳定标识就能把审计里的"某条操作"对应到"当时那个浏览器"）。
        /// </summary>
        private void BroadcastPresence()
        {
            if (_sessions.IsEmpty)
                return;

            var entries = _presence.Snapshot();
            var users = new object[entries.Count];

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];

                users[i] = new
                {
                    sessionRef = entry.SessionRef,
                    user = entry.User,
                    role = entry.Role,
                    ip = entry.Ip,
                    connectedAtUtc = entry.ConnectedAtUtc,
                    lastSeenUtc = entry.LastSeenUtc,
                    canOperate = entry.CanOperate
                };
            }

            var json = JsonWire(new { type = "presence", users });

            foreach (var session in _sessions.Values)
                session.TrySend(json);
        }

        /// <summary>
        /// 广播报警列表（2 秒一拍，与 presence 同频）。AckButton 的「最早一条」与未确认计数
        /// 都从这里来；数据源是 <see cref="IScadaAlarmFeed"/> 的不可变快照，不把报警引擎交给 Web。
        /// </summary>
        private void BroadcastAlarms()
        {
            if (_alarmFeed == null || _sessions.IsEmpty)
                return;

            var snapshot = _alarmFeed.Snapshot();
            var alarms = new object[snapshot.Alarms.Count];

            for (var i = 0; i < alarms.Length; i++)
            {
                var alarm = snapshot.Alarms[i];
                alarms[i] = new
                {
                    alarmId = alarm.AlarmId,
                    name = alarm.Name,
                    message = alarm.Message,
                    severity = alarm.Severity.ToString(),
                    activatedAtUtc = alarm.ActivatedAtUtc,
                    acknowledged = alarm.Acknowledged
                };
            }

            var json = JsonWire(new
            {
                type = "alarms",
                alarms,
                unacknowledgedCount = snapshot.UnacknowledgedCount
            });

            foreach (var session in _sessions.Values)
                session.TrySend(json);
        }

        /// <summary>
        /// 广播一帧 <c>caps</c>：角色/策略变化后让已连客户端<b>立即</b>刷新可操作性。
        ///
        /// 快照里的 <c>CanOperate</c> 只在结构变化时下发；如果只靠它，
        /// “管理员上线/下线、工程师降权、ClickPolicy 切换”要等到下一次切页或重连才生效，
        /// 现场表现就是“列表里说只读、其实还能点”或反过来。2 秒一拍与 presence 同频，
        /// 对 10 图元/10 客户端足够；客户端按 <c>deny/allow/default</c> 覆盖快照里的旧值。
        /// </summary>
        private async Task BroadcastCapsAsync(CancellationToken token)
        {
            if (_sessions.IsEmpty)
                return;

            LiveState state;
            try
            {
                state = await RunOnUiAsync(CaptureLiveState, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.Warn($"[ScadaWeb] caps 广播取运行态异常：{ex.GetType().Name}：{ex.Message}");
                return;
            }

            if (!state.Running || state.Page == null)
                return;

            var policyVersion = Environment.TickCount64;

            foreach (var session in _sessions.Values)
            {
                try
                {
                    var page = ApplyCaps(state.Page, session);
                    var deny = new List<object[]>();
                    var allow = new List<object[]>();
                    CollectCaps(page.Elements, deny, allow);
                    if (page.BasePage != null)
                        CollectCaps(page.BasePage.Elements, deny, allow);

                    var json = JsonWire(new
                    {
                        type = "caps",
                        caps = new
                        {
                            policyVersion,
                            runGeneration = state.Generation,
                            pageId = page.PageId,
                            pageVersion = page.PageVersion,
                            readOnly = IsSessionReadOnly(session),
                            lease = new
                            {
                                enabled = _settings.OperationLeaseEnabled,
                                required = _settings.OperationLeaseEnabled && _settings.AllOperatorsRequiresLease,
                                held = _lease.Snapshot().Held,
                                holder = _lease.Snapshot().Holder,
                                holderSessionRef = _lease.Snapshot().HolderSessionRef,
                                remainingSeconds = _lease.Snapshot().RemainingSeconds
                            },
                            @default = false,
                            defaultReason = "当前角色没有操作权限",
                            deny,
                            allow
                        }
                    });
                    session.TrySend(json);
                }
                catch (Exception ex)
                {
                    _log.Warn($"[ScadaWeb] caps 广播失败（会话 {session.SessionRef}）：{ex.GetType().Name}：{ex.Message}");
                }
            }
        }

        private static void CollectCaps(
            IReadOnlyList<ScadaElementSnapshot> elements,
            List<object[]> deny,
            List<object[]> allow)
        {
            foreach (var element in elements)
            {
                if (element.CanOperate)
                    allow.Add(new object[] { element.ElementId });
                else
                    deny.Add(new object[] { element.ElementId, element.OperateReason ?? "当前角色没有操作权限" });
            }
        }

        /// <summary>
        /// 撤销一枚会话令牌（登出 / 踢人）：先让票表失效，再<b>立刻</b>关掉匹配的 WS。
        ///
        /// <b>为什么必须关连接</b>：令牌失效只挡住"下一次换票/续期"，已建立的连接还能继续收帧、
        /// 继续发命令——那正是"把操作员踢下线"要立刻阻止的事。关闭码用 4403（业务自定义），
        /// 客户端据此回登录页而不是当成网络抖动重连。
        /// </summary>
        /// <returns>被关闭的连接数</returns>
        public int RevokeSession(string? sessionToken)
        {
            _securityGate.RevokeSession(sessionToken);

            if (string.IsNullOrWhiteSpace(sessionToken))
                return 0;

            var closed = 0;

            foreach (var session in _sessions.Values)
            {
                var grant = session.Session;

                if (grant == null || !string.Equals(grant.Token, sessionToken, StringComparison.Ordinal))
                    continue;

                session.MarkRevoked();
                CloseRevokedSession(session, "会话已被撤销，请重新登录");
                _lease.Release(session);
                _presence.Unregister(session);
                closed++;
            }

            if (closed > 0)
            {
                BroadcastPresence();
                _log.Info($"[ScadaWeb] 已撤销 1 枚会话令牌，关闭 {closed} 条连接");
            }

            // 撤销是安全事件：即使没有在线连接匹配（令牌只在内存里、页面已关），
            // 也要留下"谁在什么时候踢了哪枚令牌"这一行——否则事后无法解释某人的令牌为何失效。
            AuditSecurity("桌面端", sessionId: null, ip: null, "会话", "撤销会话",
                "撤销", "成功", $"session={ShortToken(sessionToken)}，关闭 {closed} 条连接");

            return closed;
        }

        /// <summary>
        /// 撤销一个账号的全部会话（改密 / 降权 / 删号之后）：票表按用户撤销，并关闭该用户所有连接。
        /// </summary>
        /// <returns>被关闭的连接数</returns>
        public int RevokeUser(string? userName)
        {
            _securityGate.RevokeUser(userName);

            if (string.IsNullOrWhiteSpace(userName))
                return 0;

            var closed = 0;

            foreach (var session in _sessions.Values)
            {
                if (!string.Equals(session.UserName, userName, StringComparison.OrdinalIgnoreCase))
                    continue;

                session.MarkRevoked();
                CloseRevokedSession(session, "账号的会话已被撤销，请重新登录");
                _lease.Release(session);
                _presence.Unregister(session);
                closed++;
            }

            if (closed > 0)
            {
                BroadcastPresence();
                _log.Info($"[ScadaWeb] 已撤销账号「{MaskUser(userName)}」的全部会话，关闭 {closed} 条连接");
            }

            // 与 RevokeSession 同一条口径：账号级撤销是"改密 / 降权 / 删号"的后果，
            // 审计里必须能对上"什么时候把谁踢下线了"。
            AuditSecurity("桌面端", sessionId: null, ip: null, "账号", "撤销账号",
                "撤销", "成功", $"user={MaskUser(userName)}，关闭 {closed} 条连接");

            return closed;
        }

        #endregion

        #region 静态资源（基础页）

        /// <summary>
        /// 注册 7 个静态文件。<b>无鉴权</b>：基础页要在登录前就能加载（它只含渲染器代码，
        /// 不含任何组态数据）；数据一律走 <c>/ws</c>。
        ///
        /// <b>为什么下沉到 <c>PreAuthentication.Static</c></b>
        /// ---------
        /// Watson 的 <c>WebserverBase.Get(path, Func&lt;ApiRequest, Task&lt;object&gt;&gt;)</c> 会把返回值
        /// 走 JSON 序列化——返回 <c>byte[]</c> 会变成 base64 字符串（<c>_WebProbe2</c> §4 实测）。
        /// 只有 <c>Static</c> 路由拿得到 <c>HttpContextBase</c>，能原样吐字节。
        /// </summary>
        private void RegisterStaticRoutes(Webserver server)
        {
            var routes = server.Routes.PreAuthentication.Static;

            // "/" 与 "/index.html" 都给首页
            AddStaticRoute(routes, "/", "index.html");

            foreach (var file in StaticFiles)
                AddStaticRoute(routes, "/" + file, file);

            // 刻意不注册"单段路径兜底 404"：Watson 的参数路由先于 WebSocket 路由匹配，
            // 一条 PreAuthentication 的 /{file} 会把 WS 升级请求 /ws 一起吃掉（握手变 404）。
            // 未命中的路径会落到"鉴权之后的默认路由"，回 401——同样是拒绝，
            // 而"只服务白名单里这几个文件"由上面的列表保证（不是靠路径解析）。
        }

        private static void AddStaticRoute(
            WatsonWebserver.Core.Routing.StaticRouteManager routes,
            string path,
            string fileName)
        {
            // 全限定 HttpMethod：ImplicitUsings 会带进 System.Net.Http.HttpMethod，裸写会歧义
            routes.Add(
                WatsonWebserver.Core.HttpMethod.GET,
                path,
                ctx => SendStaticFileAsync(ctx, fileName));
        }

        private static async Task SendStaticFileAsync(HttpContextBase ctx, string fileName)
        {
            var fullPath = ResolveStaticFile(fileName);

            if (fullPath == null)
            {
                // 白名单外的路径 / 文件不存在：同一个 404，不泄露磁盘结构
                ctx.Response.StatusCode = 404;
                await ctx.Response.Send("not found", CancellationToken.None).ConfigureAwait(false);
                return;
            }

            byte[] bytes;

            try
            {
                bytes = File.ReadAllBytes(fullPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.Send("not found", CancellationToken.None).ConfigureAwait(false);
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = ContentTypeFor(fileName);

            try
            {
                ctx.Response.Headers.Add("X-Content-Type-Options", "nosniff");
                ctx.Response.Headers.Add("Cache-Control", "no-cache");
            }
            catch
            {
                // 某些 Watson 版本对重复头会抛：静态资源不加这两条也能用
            }

            await ctx.Response.Send(bytes, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// 解析静态文件：<b>只认白名单里的文件名</b>（白名单本身就是"拒绝 <c>..</c>"的判据，
        /// 任何带路径分隔符的输入都进不来），再依次在候选根目录下找。
        ///
        /// 两个候选根：
        ///   · <c>Web\wwwroot</c> —— 产品布局（<c>Web\</c> 只放模块 DLL + wwwroot）；
        ///   · <c>wwwroot</c>     —— 探针/自包含布局（工程引用把 Content 拷到输出根）。
        /// </summary>
        private static string? ResolveStaticFile(string fileName)
        {
            if (!StaticFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                return null;

            var baseDir = AppContext.BaseDirectory;

            foreach (var root in new[]
                     {
                         Path.Combine(baseDir, "Web", "wwwroot"),
                         Path.Combine(baseDir, "wwwroot")
                     })
            {
                string candidate;

                try
                {
                    var fullRoot = Path.GetFullPath(root);
                    candidate = Path.GetFullPath(Path.Combine(fullRoot, fileName));

                    // 双保险：即便白名单将来放宽，也不许跳出根目录
                    if (!candidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                catch
                {
                    continue;
                }

                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            _ => "application/octet-stream"
        };

        #endregion

        #region 运行态快照

        /// <summary>
        /// 现取一次运行态事实（<b>不缓存</b>）。
        /// 缓存会让"运行结束"与"读到旧页面"之间的窗口变大，而查询频率极低
        /// （HTTP 状态查询 / 每次握手一次），现取更简单也更准。
        /// 注意：这是 M1a 留下的<b>无 Dispatcher 的标量读</b>（只读 IsRunning/Session/CurrentPage 的引用与标量），
        /// 值帧与快照那条路必须走 <see cref="RunOnUiAsync"/>。
        /// </summary>
        private RunSnapshot ReadRunSnapshot()
        {
            if (_hub == null)
                return RunSnapshot.NotRunning;

            try
            {
                if (!_hub.IsRunning)
                    return RunSnapshot.NotRunning;

                var page = _hub.Session?.CurrentPage;
                if (page == null)
                    return new RunSnapshot(true, null, null, 0);

                return new RunSnapshot(true, page.PageId.ToString(), page.Name, page.Version);
            }
            catch (Exception ex)
            {
                // hub 是 UI 线程写的；这里只读引用型快照。真出异常也不能让状态查询 500
                _log.Warn($"[ScadaWeb] 读取运行态失败：{ex.GetType().Name}：{ex.Message}");
                return RunSnapshot.NotRunning;
            }
        }

        private sealed record RunSnapshot(bool Running, string? PageId, string? PageName, int PageVersion)
        {
            public static readonly RunSnapshot NotRunning = new(false, null, null, 0);
        }

        #endregion

        #region 小工具

        private static string Json(object value) => JsonConvert.SerializeObject(value);

        /// <summary>
        /// 数据帧（snapshot/page/frame）的序列化：camelCase + 省略 null。
        /// 与 <see cref="Json"/> 分开是刻意的——控制帧（hello/pong/error）沿用 M1a 的默认口径，
        /// 不能因为数据帧要紧凑就把它们的 <c>null</c> 字段悄悄去掉。
        /// </summary>
        private static string JsonWire(object value) => JsonConvert.SerializeObject(value, WireJson);

        private static string MaskUser(string? user)
        {
            if (string.IsNullOrEmpty(user)) return "<空>";

            // 用户名不是秘密，但日志里没必要留全串（现场常有工号/手机号当账号）
            return user.Length <= 2 ? user : user.Substring(0, 2) + "***";
        }

        /// <summary>
        /// 令牌的日志安全前缀（前 6 位）。<b>ticket / 会话令牌一律不许整串写日志</b>：
        /// 日志文件常被现场打包带走，整串等于把凭据一起发出去。
        /// </summary>
        private static string ShortToken(string? token)
            => string.IsNullOrEmpty(token) ? "<空>" : (token.Length <= 6 ? token : token.Substring(0, 6));

        private static string SafeRawPath(HttpContextBase ctx)
        {
            try { return ctx.Request.Url?.RawWithoutQuery ?? "<unknown>"; }
            catch { return "<unknown>"; }
        }

        private static async Task SafeSendAsync(WebSocketSession socket, string json)
        {
            try { await socket.SendTextAsync(json, CancellationToken.None).ConfigureAwait(false); }
            catch { /* 对端已断开 */ }
        }

        private static async Task SafeCloseAsync(WebSocketSession socket, WebSocketCloseStatus status, string reason)
        {
            try { await socket.CloseAsync(status, reason, CancellationToken.None).ConfigureAwait(false); }
            catch { /* 已断开 */ }
        }

        #endregion
    }
}
