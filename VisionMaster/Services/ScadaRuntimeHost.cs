using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Views;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IScadaRuntimeHost"/> 的默认实现：一次只允许一个运行会话 + 一个运行窗口。
    ///
    /// 同时实现 <see cref="IScadaRunHub"/>：把"当前这一轮"的会话/文档/轮次号与启停信号
    /// 交给只引用 <c>VM.Scada</c> 的可选模块（Web 运行态等）。启停信号的门禁全部落在
    /// <see cref="ScadaRunHubState"/> 上——本类只决定"什么时候调它"，不另写一套"发没发过"的判据。
    /// 两条硬口径：① 启动失败（无画面 / 起报警或 Show 抛异常）<b>不发</b> RunStarted，
    /// 收场时也不会补发 RunStopped；② RunStopped 一律在清理<b>之后</b>发，
    /// 订阅方在处理器里读 <see cref="Session"/>/<see cref="Document"/> 必须已经是 null。
    ///
    /// 一次"运行"的完整生命周期
    /// ---------
    /// <code>
    /// Start(document)
    ///   ├─ 已有窗口？  → Activate() 带前台，返回（单实例，绝不开第二份会话）
    ///   ├─ new ScadaRuntime(document)        领域会话：该显示哪一页、Loaded 该不该发
    ///   ├─ new ScadaRuntimeWindow(session)   壳子：摆画布 + 停止运行 + 状态条
    ///   ├─ Stopwatch.StartNew()              计时起点 = 决定运行的那一刻
    ///   ├─ session.Start()  false → 提示"没有可运行的画面"并收场（不弹空窗口）
    ///   └─ window.Show()
    ///           ├─ ContentRendered（首帧真的画完了）
    ///           │     ├─ 状态条回填耗时
    ///           │     ├─ ScadaRuntimeBinder.Start()  建"变量 → 图元属性"的表并先刷一遍当前值
    ///           │     └─ session.RaisePageEvent(Loaded) → 命中画面钩子 → 分发器执行动作
    ///           └─ 操作员点图元 → 冒泡到窗口根的组态事件
    ///                 → session.RaiseElementEvent（三道闸门）→ 命中钩子 → 分发器按序执行动作
    ///   运行中切页：动作「切换画面」→ IScadaNavigator（ScadaNavigator）→ session.Navigate
    ///           ├─ 旧页 Unloaded 钩子 → 分发器执行
    ///           ├─ CurrentPage 换掉 → 画布 XAML 绑定自动重造图元控件（可视内容这一半白送）
    ///           └─ 新页 PageLoaded → 本宿主接住 → 旧页数据泵摘表 → 等这一页布局跑完
    ///                 → 新页数据泵建表 → 新页 Loaded 钩子 → 分发器执行
    ///   运行中：变量变化 → 数据泵合并刷帧 → 写图元控件（不碰模型）
    ///   运行中：报警引擎挂到同一批变量上 → 值一变就判条件 → 报警态迁移
    ///           ├─ 立即报警 → AlarmRaised → 追加一行 CSV 历史（按天滚动）+ 记一条运行日志
    ///           └─ 延时到期 / 通信断线 只能靠时间流逝发现 → 由"全画面统一节拍源"每拍推一次 Tick
    ///   起报警系统时顺带把「引擎 + 节拍」打成 ScadaRuntimeContext 交给画布 → 画布在造每个图元控件时
    ///           转发下去 → 报警条图元据此订阅三事件与节拍（宿主装一次，整页图元都拿得到）
    ///   运行中：变量事件引擎挂到同一批变量上 → 值一变就判边沿（更改数值 / 值为真 / 值为假 / 上越限 / 下越限）
    ///           → 命中钩子 → <b>切回 UI 线程</b> → 分发器按序执行动作
    ///   关窗口 / 切方案 / Stop()  →  摘掉图元的运行态上下文（退订引擎与节拍）→ 停节拍 → 数据泵摘表
    ///                              （退订阅 + 刷回设计值）→ 报警引擎摘槽 → 变量事件引擎摘槽
    ///                              → 摘掉导航器的会话 → 退订会话与窗口的全部事件 → session.Stop() → 字段清空
    /// </code>
    ///
    /// 为什么切页要单设一条通道，而不是复用 <c>ContentRendered</c>
    /// ---------
    /// <c>ContentRendered</c> 只在窗口生命周期内来一次（本窗口不会隐藏后再显示，退出就是关掉），
    /// 拿它当"新页画好了"的信号，第二次切页就永远等不到。而"画好了"这件事又必须等：
    /// 图元控件是画布布局阶段才造出来的，早于它建表只会建出一张空表。
    /// 所以运行中切页改用"<c>CurrentPage</c> 变了 + 这一页的布局跑完"作为等价信号
    /// （见 <see cref="SchedulePageAfterLayout"/>），首帧那一页仍走 <c>ContentRendered</c>（那里还要测耗时）。
    ///
    /// 为什么"鼠标 → 事件"这一跳由宿主接，而不是让画布直接找到会话
    /// ---------
    /// 画布（<c>ScadaCanvas</c>）是设计器与运行窗口<b>共用的同一个控件</b>，它不知道自己挂在谁身上；
    /// 让它去调 <see cref="ScadaRuntime"/>，等于让下层反过来认识上层，依赖方向就翻了。
    /// 所以图元事件沿可视树冒泡，由宿主在<b>窗口根上</b>接一手交给会话判定，自己一条规则都不写——
    /// 判"是不是当前画面的图元、配没配钩子"在会话里，"执行哪条动作"在分发器里，这里只是接线。
    ///
    /// 为什么耗时不在 <see cref="ScadaRuntime"/> 里测
    /// ---------
    /// 领域层的 <c>PageLoaded</c> 是在 <c>Start()</c> 里同步发出的，那一刻一个像素都还没画。
    /// 现场排查"画面点开是黑的/转圈久"要看的是**首帧**，那只有 WPF 的
    /// <c>ContentRendered</c> 知道。所以领域层只交出一个事实（该显示这一页），
    /// 由宿主把"什么时候真画出来了"补上——两边都不越界。
    /// </summary>
    public sealed class ScadaRuntimeHost : IScadaRuntimeHost, IScadaRunHub, IScadaAlarmCommands, IScadaAlarmFeed,
        IScadaRuntimeDataProvider
    {
        private readonly ILogService _log;
        private readonly IUserNotifier _notifier;

        /// <summary>工作区（弱依赖：拿不到通知就不做"切方案自动停止"，但运行本身照样能用）</summary>
        private readonly INotifyPropertyChanged? _workspace;

        /// <summary>工作区强引用：报警历史要按当前方案的 .vms 路径定位 <c>Alarms</c> 目录</summary>
        private readonly IWorkspaceManager _workspaceManager;

        /// <summary>报警历史快照（方案目录 <c>Alarms\AlarmHistory.json</c>）；没运行/收场后为 null</summary>
        private ScadaAlarmHistoryStore? _alarmStore;

        /// <summary>本轮开始前从方案目录读回的历史记录；每次写快照都要与本轮记录合并，不能覆盖掉</summary>
        private IReadOnlyList<ScadaAlarmRecord> _priorAlarms = Array.Empty<ScadaAlarmRecord>();

        /// <summary>CSV 追加与 JSON 快照写入的串行闸：报警事件可能来自变量轮询线程</summary>
        private readonly object _alarmPersistGate = new();

        private ScadaRuntime? _session;
        private ScadaRuntimeWindow? _window;

        /// <summary>
        /// 取 DataGrid 图元的行/列快照（Web 运行态的只读数据出口）。
        ///
        /// 只在 UI 线程调用：运行窗口的画布与控件都是 UI 单线程语义。
        /// 只找运行窗口（含弹窗画布）里的控件——编辑器画布也有一份同名 ElementId 的控件，
        /// 遍历 Application.Windows 会读错对象。
        /// </summary>
        public bool TryGetGrid(Guid elementId, out ScadaGridSnapshot snapshot)
        {
            snapshot = null!;
            var window = _window;
            if (window == null)
                return false;

            return TryFindGrid(window.CanvasHost, elementId, out snapshot)
                   || TryFindGrid(window.PopupCanvasHost, elementId, out snapshot);
        }

        private static bool TryFindGrid(ScadaCanvas? canvas, Guid elementId, out ScadaGridSnapshot snapshot)
        {
            snapshot = null!;
            if (canvas == null)
                return false;

            foreach (var control in canvas.EnumerateControls())
            {
                if (control is DataGridElement grid
                    && grid.Element != null
                    && grid.Element.ElementId == elementId)
                {
                    snapshot = new ScadaGridSnapshot
                    {
                        Columns = grid.Columns,
                        Rows = grid.Buffer,
                        RecordedCount = grid.RecordedCount
                    };
                    return true;
                }
            }

            return false;
        }

        /// <summary>方案级监控（报警/变量事件/节拍）。窗口只是它的消费者，关窗不停它。</summary>
        private readonly ScadaMonitoringHost _monitoring;

        /// <summary>命中钩子之后干活的人（单例，见 App 里的注册）</summary>
        private readonly IScadaActionDispatcher _dispatcher;

        /// <summary>
        /// 变量值通道（单例）。宿主只负责把它交到数据泵手上，自己不解析变量、不订阅值——
        /// "哪个变量对应哪个图元属性"是画面绑定说了算的，宿主一条规则都不判。
        /// </summary>
        private readonly IScadaValueSource _valueSource;

        /// <summary>
        /// 变量回写通道（单例）。宿主只把它塞进 <see cref="ScadaRuntimeContext"/>，
        /// 自己不碰——"哪个输入框改了值该写回哪个变量"是图元的绑定说了算的，
        /// 与读方向的 <see cref="_valueSource"/> 同一条分工。
        /// </summary>
        private readonly IScadaValueWriter _valueWriter;

        /// <summary>
        /// 轴控台「示教/定位」的二次确认出口。无窗口宿主（断言/离线）返回 false，
        /// 图元侧会把"需要确认但确认不了"如实拒绝，而不是静默跳过确认。
        /// </summary>
        private readonly IScadaConfirmationService _confirmations;

        /// <summary>
        /// 软件级配置（AppConfig.json）。这里只用它两件事：读"运行窗口该长什么样"
        /// （<c>RunWindowMode</c>）和"该落在哪块屏"（<c>RunWindowMonitor</c>）。
        /// 每次 <see cref="Start"/> 现读 <c>Current</c>，所以「系统 → 运行窗口设置」改完立刻生效，
        /// 不用重启软件——设置弹窗自己负责写盘（见 ScadaSystemSettingsViewModel）。
        /// </summary>
        private readonly AppSettingsService _appSettings;

        /// <summary>
        /// 运行态切页出口（单例）。宿主只做一件事：建好会话时把它挂上、收场时摘掉，
        /// 让长命的分发器在动作执行的那一刻找得到"此刻活着的是哪一个会话"。
        /// </summary>
        private readonly ScadaNavigator _navigator;

        /// <summary>
        /// 运行态权限出口（S12，单例）。宿主只用它两件事：
        /// 建会话时交给 <see cref="ScadaRuntime"/> 去判"这个图元该不该放行"，
        /// 执行动作时把"此刻是谁在操作"写进日志署名。
        ///
        /// 宿主自己<b>一条权限规则都不判</b>——与事件判定同一条分工：
        /// 判"够不够格"在领域层（那里有唯一的规则实现），宿主只是把"谁在操作"递过去。
        /// </summary>
        private readonly IScadaAccessPolicy _accessPolicy;

        /// <summary>
        /// 操作审计落盘端（S12）。宿主只用它一件事：把<b>被权限拦下的那一次操作</b>记进凭证
        /// （见 <see cref="OnElementAccessDenied"/>）。成功执行的那些动作由分发器自己落
        /// （它才是执行侧），宿主不重复记一遍——同一件事记两处，早晚变成两条对不上的记录。
        /// </summary>
        private readonly ScadaAuditWriter _audit;

        /// <summary>
        /// 运行轮次状态机（"这一轮起没起 / 第几轮"）。抽在领域层是为了让断言能对<b>同一份实现</b>
        /// 跑满启停序列（见 ScadaRunHubState 的类注释）；本类只负责"什么时候调它、调到了发什么信号"。
        /// 线程：与 <see cref="_session"/> 同一条口径——只在 UI 线程上读写。
        /// </summary>
        private readonly ScadaRunHubState _hubState = new();

        /// <summary>
        /// 窗口根上接组态事件的那个委托。存成字段只为 <c>AddHandler</c> 与 <c>RemoveHandler</c>
        /// 拿到的是同一个实例——路由事件按委托相等性摘挂，两处各写一遍方法组虽然等价但读起来要猜。
        /// </summary>
        private readonly EventHandler<ScadaElementEventArgs> _onScadaEvent;

        /// <summary>从决定运行到首帧渲染完成的计时表（窗口没开就是 null）</summary>
        private Stopwatch? _watch;

        /// <summary>
        /// Loaded 钩子已经等着跑、但还在等首帧的那一页。
        /// 会话发"该显示了"与动作真跑之间有渲染这一跳，所以要把这一页存下来（见 <see cref="OnFirstFrameRendered"/>）。
        /// </summary>
        private ScadaPage? _pendingLoadedPage;

        /// <summary>
        /// 首帧是不是已经画完了。它是两条通路的分界线：
        /// - <c>false</c>（窗口刚开、起步页还没显示）：<see cref="OnPageLoaded"/> 只把页存进
        ///   <see cref="_pendingLoadedPage"/>，等 <c>ContentRendered</c> 来收（那里还要测耗时）。
        /// - <c>true</c>（运行中）：<c>ContentRendered</c> 不会再来，切页改走
        ///   <see cref="SchedulePageAfterLayout"/>。
        /// </summary>
        private bool _firstFrameDone;

        /// <summary>
        /// 本轮的运行态数据泵（没运行 / 还没到首帧时是 null）。
        /// 生命周期严格跟着窗口：首帧后建表、窗口关闭时摘表，绝不跨轮次复用——
        /// 它内部攥着控件引用与变量订阅，跨轮复用就是"上一页的图元还在收这一页的值"。
        /// </summary>
        private ScadaRuntimeBinder? _binder;

        /// <summary>
        /// 当前弹窗的数据泵（没有弹窗时为 null）。与主页数据泵同一套生命周期：
        /// 弹窗打开并布局完成后建表、关闭时摘表；它只服务弹窗页，不与主页表混用。
        /// </summary>
        private ScadaRuntimeBinder? _popupBinder;

        /// <summary>
        /// 弹窗建表代次：关闭/重开同一弹窗时，上一轮排队的 Dispatcher 回调必须作废，
        /// 否则两个回调都会通过"CurrentPopup 还是它"的判据，后一个覆盖前一个却不摘表。
        /// </summary>
        private int _popupGeneration;

        /// <summary>
        /// 本轮运行的<b>全画面统一节拍源</b>（没运行 / 还没起表时是 null）。
        /// 一次运行只养一个：报警的延时与断线判定以它为分辨率，报警灯闪烁与它共用相位。
        /// 领域层不自己起定时器（见 <see cref="ScadaAlarmEngine.Tick"/>），所以这一个必须由宿主来养。
        /// </summary>
        private ScadaBeatSource? _beat;

        /// <summary>
        /// 本轮的报警引擎（没运行 / 还没挂载时是 null）。生命周期严格跟着窗口：
        /// 起窗口时挂载（挂上变量订阅）、关窗口时摘槽——它和数据泵攥着同一批变量订阅，
        /// 跨轮复用就是"上一轮的报警还在盯着这一轮的变量"。
        /// </summary>
        private ScadaAlarmEngine? _alarms;

        /// <summary>
        /// 报警历史落盘端（没运行 / 还没起时是 null）。与引擎同生命周期：
        /// 引擎发事实、它记文件——把"记到哪儿、记不成怎么办"留在宿主侧（见 ScadaAlarmHistoryWriter 类注释）。
        /// </summary>
        private ScadaAlarmHistoryWriter? _alarmWriter;

        /// <summary>
        /// 本轮的变量事件引擎（没运行 / 还没挂载时是 null）。生命周期严格跟着窗口：
        /// 起窗口时挂载（挂上变量订阅）、关窗口时摘槽——它和数据泵、报警引擎攥着同一批变量订阅，
        /// 跨轮复用就是"上一轮的变量事件还在盯着这一轮的变量"。
        /// </summary>
        private ScadaVariableEventEngine? _variableEvents;

        /// <summary>
        /// 变量事件的投递用 Dispatcher（挂载时取好，收场时置空）。
        ///
        /// 为什么不在回调里现取 <see cref="_window"/>：回调可能在变量轮询线程上，
        /// 而 <see cref="_window"/> 是 UI 线程随时会改的字段——取早一次，读的就不是一个会变的局面。
        /// 置空即表示"这一轮运行已经收场"，迟到的值事件直接丢弃。
        /// </summary>
        private Dispatcher? _variableEventDispatcher;

        /// <summary>
        /// 报警节拍周期。它就是报警系统的时间分辨率——激活延时"到没到"、通信断线"断了多久"，
        /// 都只在这个精度上被判定（见 ScadaAlarmEngine.Tick 建议的 100~500ms）。
        /// </summary>
        private static readonly TimeSpan AlarmBeatInterval = TimeSpan.FromMilliseconds(ScadaBeatSource.DefaultIntervalMilliseconds);

        /// <summary>
        /// 本轮的运动轴快照源（单例）。宿主只把它塞进 <see cref="ScadaRuntimeContext"/>，
        /// 自己不读轴——"当前显示哪根轴"是轴面板自己的选择状态。
        /// 没接运动模块时可以是 null（轴面板会显示"未接入运动数据"）。
        /// </summary>
        private readonly IScadaAxisSource? _axisSource;

        /// <summary>
        /// 本轮的轴命令出口（单例）。与 <see cref="_axisSource"/> 成对注入：
        /// 读方向与写方向走同一个执行器，门禁/看门狗只有一份。
        /// </summary>
        private readonly IScadaAxisCommandSink? _axisCommands;

        /// <summary>
        /// 运行态未授权点击的登录回调（B3）。null = 无界面场景（断言宿主）不弹登录，直接拒绝。
        /// </summary>
        private readonly Func<string, bool>? _accessLoginPrompt;

        /// <summary>语言服务（v6）：文本 @键 解析；没接线时所有文本按字面量显示</summary>
        private readonly IScadaLanguageService? _languages;

        /// <summary>报警静音出口（v7）：画面上「静音」动作用</summary>
        private readonly IScadaAlarmMute? _alarmMute;

        /// <summary>配方面板命令出口（v7）：选/下载/上传/保存</summary>
        private readonly IScadaRecipeCommands? _recipeCommands;

        public ScadaRuntimeHost(
            ILogService log,
            IUserNotifier notifier,
            IWorkspaceManager workspace,
            IScadaActionDispatcher dispatcher,
            IScadaValueSource valueSource,
            IScadaValueWriter valueWriter,
            ScadaNavigator navigator,
            IScadaAccessPolicy accessPolicy,
            ScadaAuditWriter audit,
            AppSettingsService appSettings,
            IScadaAxisSource? axisSource = null,
            IScadaAxisCommandSink? axisCommands = null,
            IScadaConfirmationService? confirmations = null,
            ScadaMonitoringHost? monitoring = null,
            Func<string, bool>? accessLoginPrompt = null,
            IScadaLanguageService? languages = null,
            IScadaAlarmMute? alarmMute = null,
            IScadaRecipeCommands? recipeCommands = null)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _valueSource = valueSource ?? throw new ArgumentNullException(nameof(valueSource));
            _valueWriter = valueWriter ?? throw new ArgumentNullException(nameof(valueWriter));
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
            _axisSource = axisSource;
            _axisCommands = axisCommands;
            _accessLoginPrompt = accessLoginPrompt;
            _languages = languages;
            _alarmMute = alarmMute;
            _recipeCommands = recipeCommands;
            _confirmations = confirmations ?? new ScadaConfirmationService();
            _workspaceManager = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _monitoring = monitoring ?? (System.Windows.Application.Current == null
                // 断言/设计器：没有容器，自己搭一份（不会与 DI 单例并存）。
                ? new ScadaMonitoringHost(
                    log, workspace, valueSource, valueWriter, accessPolicy,
                    axisSource, axisCommands, _confirmations, dispatcher, appSettings)
                // 生产：必须由容器注入同一个单例；缺了就是装配错误，宁可启动当场炸，
                // 也不要偷偷再 new 一份（两份引擎会把报警/变量事件各执行两遍）。
                : throw new InvalidOperationException(
                    "ScadaRuntimeHost 缺少 ScadaMonitoringHost 注入：方案级监控必须与宿主共用同一实例。"));
            _onScadaEvent = OnScadaEvent;

            // 三个都是单例，这条订阅不会把谁钉住（不存在"宿主早于工作区被回收"的情况），
            // 所以不留 Dispose：为了对称而写一套只会被调一次的解绑代码，读的人反而要猜为什么。
            _workspace = workspace as INotifyPropertyChanged;
            if (_workspace != null)
                _workspace.PropertyChanged += OnWorkspacePropertyChanged;
        }

        /// <inheritdoc/>
        public bool IsRunning => _window != null;

        // ---- IScadaRunHub：把"当前这一轮运行"的事实与启停信号交给可选模块 ----

        /// <inheritdoc/>
        public ScadaRuntime? Session => _session;

        /// <inheritdoc/>
        public ScadaDocument? Document => _session?.Document;

        /// <inheritdoc/>
        public long RunGeneration => _hubState.Generation;

        /// <inheritdoc/>
        public bool TryGetCurrent(out ScadaRuntime? session, out ScadaDocument? document, out long generation)
        {
            // 判据与 IsRunning 同源（窗口在 = 这一轮活着）。字段只在 Show() 成功之后才落地，
            // 所以"启动半路失败"不会让晚订阅者拿到一个从没显示过的会话。
            var current = _session;
            if (current == null || _window == null)
            {
                session = null;
                document = null;
                generation = 0;
                return false;
            }

            session = current;
            document = current.Document;
            generation = _hubState.Generation;
            return true;
        }

        /// <inheritdoc/>
        public event Action<ScadaRuntime, ScadaDocument, long>? RunStarted;

        /// <inheritdoc/>
        public event Action<long>? RunStopped;

        /// <inheritdoc/>
        public void Start(ScadaDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            // 单实例。重复点"运行"在现场太常见：真开出两份会话，就是两份运行态各自往
            // 同一批变量上写，出问题时分不清是谁写的。所以第二次只是把窗口带到前台。
            // 这里刻意不动状态机：没有新的一轮，就不该有新的 generation、也不该再发 RunStarted。
            if (_window != null)
            {
                _window.Activate();
                return;
            }

            // 上一轮若在收场半路抛了（OnWindowClosed 还没走到发 RunStopped 那一步），状态机会停在"已启动"。
            // 这里先把那一轮补发结束：否则下面的 TryStarted 会被状态机挡掉，订阅方永远等不到下一次 Started。
            if (_hubState.IsStarted && _hubState.TryStopped(out var orphanedGeneration))
                PublishStopped(orphanedGeneration);

            // 会话与窗口先落在局部变量上，_session/_window 只在 Show() 成功之后才赋值。
            // 这条纪律是"点了运行没反应"那类故障的根：任何一步失败（建窗口、定形态、Start 会话、
            // 起报警/变量事件、Show）若已经把字段填上，就会留下"窗口非空但从未显示"的状态——
            // Activate() 对未显示的窗口是静默 no-op，之后每次「运行」都早退在那里，
            // IsRunning 与 IScadaRunHub 也会一直谎报在跑。
            ScadaRuntime? session = null;
            ScadaRuntimeWindow? window = null;
            var stage = "创建运行会话";

            try
            {
                session = new ScadaRuntime(document, _accessPolicy);
                session.AccessLoginPrompt = _accessLoginPrompt;

                stage = "创建运行窗口";
                window = new ScadaRuntimeWindow(session);

                // 背景图按“方案目录 + 相对路径”解析；方案还没保存过就为空（背景回退背景色）。
                window.SolutionRoot = SolutionPaths.GetSolutionRoot(_workspaceManager.CurrentSolution?.SolutionFilePath);

                // 形态必须在 Show() 之前定死：WindowStyle/WindowState 这类属性在窗口显示之后再改，
                // WPF 会重建非客户区，表现为闪一下、尺寸跳一格（依附形态下更明显）。
                //
                // 这里一次把"长什么样"和"落在哪块屏"都现读出来：两者都来自软件级配置，
                // 都只在这一次 Start 上生效（改完不必重启，下一次点「运行」就是新样子）。
                stage = "应用运行窗口形态";
                var config = _appSettings.Current;
                var monitor = ScadaMonitors.Resolve(config.RunWindowMonitor, out var monitorFellBack);
                if (monitorFellBack)
                {
                    // 配置指着一块现在不在的屏（拔线 / 换了视频口 / 在别的机器上打开同一份配置）。
                    // 落屏已经回落主屏（见 ScadaMonitors.Resolve），这里只负责留个证据——
                    // 没有这条日志，现场看到"运行画面跑到主屏去了"就只能靠猜。
                    _log.Warn($"[Scada] 运行窗口指定的显示器「{config.RunWindowMonitor}」当前不在线，本次已回落主屏。");
                }
                window.ApplyRunWindowMode(config.RunWindowMode, monitor);

                if (config.RunWindowMode == ScadaRunWindowMode.AttachedToMainWindow)
                {
                    // 挂在主界面之下：关主界面时运行窗口跟着关（"关编辑器自动停止"这条不用自己写代码，
                    // 也就不会出现漏摘的订阅）。注意副作用——最小化主界面会连带最小化运行窗口，
                    // 对"设计完看一眼效果"这个用途正是想要的行为。
                    //
                    // 独立形态刻意<b>不</b>挂 Owner：挂上就永远压在主界面之上、还会跟着一起最小化，
                    // "和视觉图像窗口一起显示"就落空了（形态的取舍见 ScadaRuntimeWindow.ApplyRunWindowMode）。
                    var owner = System.Windows.Application.Current?.MainWindow;
                    if (owner != null && owner.IsLoaded)
                        window.Owner = owner;
                }

                stage = "订阅运行会话事件";
                session.PageLoaded += OnPageLoaded;
                session.PopupOpened += OnPopupOpened;
                session.PopupClosed += OnPopupClosed;
                session.ElementEventRaised += OnElementEventRaised;
                session.PageEventRaised += OnPageEventRaised;
                session.ElementAccessDenied += OnElementAccessDenied;
                window.ContentRendered += OnFirstFrameRendered;
                window.Closed += OnWindowClosed;
                // 在窗口根上接住所有图元冒泡上来的组态事件：一处接线，中间的可视树层级不用管
                window.AddHandler(ScadaElementBase.ScadaEventEvent, _onScadaEvent);

                // 把"此刻活着的是哪一个会话"告诉切页出口。放在 Start() 之前：Start() 里就会发出
                // 第一页的 PageLoaded，那一刻若有钩子要切页，动作也得找得到会话（虽然正常配不出来）。
                // 摘掉在 OnWindowClosed 的收尾里，与这里成对。
                _navigator.Attach(session);

                // 计时从这一刻起：包含了 Start() 里的选页、建窗口、布局、首帧渲染。
                // 用 Stopwatch 而不是 DateTime.Now：墙上时钟会被 NTP/手工调整跳一下，
                // 差值就变成负数或几百秒，现场看到只会怀疑代码。
                _watch = Stopwatch.StartNew();

                stage = "启动运行会话";
                if (!session.Start())
                {
                    // 方案里一页都没有：不弹空白全屏（那比什么都不发生更让人慌），给一句人话。
                    // 收场同样走 AbortStart：只 Close() 的话，一旦 Close 自己失败或没触发 Closed，
                    // 半截启动的订阅与字段就会留在原地（下一次点运行就是"没反应"）。
                    _notifier.ShowWarn("当前方案里没有可运行的画面，请先新建画面并指定启动画面。");
                    AbortStart(window, session, "启动运行会话（当前方案里没有可运行的画面）", null);
                    return;
                }

                // 报警/变量事件由方案级监控宿主负责，窗口只是消费者：
                // 先确保监控已启动，再把同一份上下文交给画布（历史面板也从这里拿）。
                stage = "启动方案监控";
                _monitoring.EnsureStarted(document);
                window.CanvasHost.RuntimeContext = _monitoring.Context
                    ?? throw new InvalidOperationException("方案监控未启动，无法提供报警/变量事件上下文");
                window.PopupCanvasHost.RuntimeContext = window.CanvasHost.RuntimeContext;

                // 声音服务归方案级监控宿主（关窗后报警仍评估、声音状态要留着），
                // 运行窗口只是借用：在 Show 之前交过去，顶条按钮首帧就能显示真实开关。
                window.SoundAlarm = _monitoring.SoundAlarm;

                stage = "显示运行窗口";
                window.Show();
                // 窗口在 = 有人能点确认；关窗后监控仍跑，但确认框必须退回“按取消处理”。
                ScadaConfirmationService.InteractiveAllowed = true;

                // Show() 成功之后才认下这一轮：字段一落地，IsRunning / TryGetCurrent 才算"在跑"。
                _session = session;
                _window = window;

                // 到这里才是一轮真的起来了：先落 generation，再发事件。
                // 顺序不能反——订阅方在 RunStarted 里读 hub.RunGeneration，必须已经拿到本轮号。
                if (_hubState.TryStarted(out var generation))
                    PublishStarted(session, document, generation);
            }
            catch (Exception ex)
            {
                // 任何一步抛出来都走同一条收场：留一个"非空但没显示"的窗口，
                // 之后每一次「运行」都会早退到一个 Activate() 静默失败的窗口上。
                AbortStart(window, session, stage, ex);
            }
        }

        /// <inheritdoc/>
        public void Stop()
        {
            var window = _window;
            if (window != null)
            {
                window.Close();   // 收尾只在 OnWindowClosed 一处做，避免两条路各改一半状态
                return;
            }

            // 窗口不在就顺手把会话状态也归零：正常路径走不到这里，
            // 但"会话活着而窗口没了"是最难复现的一类脏状态，宁可在这里兜住。
            // 报警/变量事件引擎归 ScadaMonitoringHost（方案级），不随窗口收场。
            _navigator.Attach(null);   // 与 OnWindowClosed 成对：会话没了，导航器不能还攥着一个停掉的会话
            _session?.Stop();
            _session = null;

            // 收尾之后才发（订阅方在处理器里读 hub.Session/Document 必须已经是 null）。
            // 只有确实起过一轮才发：未启动 / 启动失败的那条路 TryStopped 返回 false，重复 Stop 也不会重复发。
            if (_hubState.TryStopped(out var generation))
                PublishStopped(generation);
        }

        // ---- 运行态启停广播（IScadaRunHub） ----

        /// <summary>
        /// 发 <see cref="RunStarted"/>。
        ///
        /// <b>逐个订阅方 try/catch</b>：多播委托里一个处理器抛异常会打断后面所有处理器，
        /// 而"某个可选模块没接好"绝不该让另一个模块收不到信号，更不该冒回 <see cref="Start"/>
        /// 把已经显示出来的运行窗口带下去。记 Warn 而不是 Error：这不是软件坏了，
        /// 是外部模块的处理逻辑出了问题（与 <see cref="OnElementAccessDenied"/> 同一条口径）。
        /// </summary>
        private void PublishStarted(ScadaRuntime session, ScadaDocument document, long generation)
        {
            var handlers = RunStarted;
            if (handlers == null)
                return;

            foreach (Action<ScadaRuntime, ScadaDocument, long> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(session, document, generation);
                }
                catch (Exception ex)
                {
                    _log.Warn($"[运行] RunStarted 订阅方处理失败（不影响本次运行）：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 发 <see cref="RunStopped"/>。逐个订阅方 try/catch 的理由同 <see cref="PublishStarted"/>。
        /// 调用点必须已经清空 <see cref="_session"/> / <see cref="_window"/>：
        /// 订阅方在处理器里读 <see cref="Session"/> / <see cref="Document"/> 要看到 null。
        /// </summary>
        private void PublishStopped(long generation)
        {
            var handlers = RunStopped;
            if (handlers == null)
                return;

            foreach (Action<long> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(generation);
                }
                catch (Exception ex)
                {
                    _log.Warn($"[运行] RunStopped 订阅方处理失败：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 启动半路失败的统一收场：建会话/建窗口/定形态/会话 <c>Start()</c>（含返回 false）/
        /// 起报警/起变量事件/<c>Show()</c> 任何一步出问题，都只走这一条路。
        ///
        /// 为什么不能只 <c>Close()</c> 了事：<c>Close()</c> 自己也可能抛（窗口状态已经坏了），
        /// 而那条路上 <see cref="_window"/> 会一直非空——之后每一次「运行」都在
        /// <see cref="Start"/> 开头早退到一个<b>根本没显示出来</b>的窗口上，界面上就是"点了运行没反应"。
        /// 所以这里无论 Close 成没成都确认一次"Closed 收尾到底跑没跑"，没跑就手动跑一遍，
        /// 最后把两个字段与本轮标志<b>强制</b>归零：下一轮 Start 必须能从同一条失败路径重来。
        ///
        /// 为什么字段要在这里临时指一下：收尾只有 <see cref="OnWindowClosed"/> 一条实现，
        /// 它按字段取会话与窗口。临时指过去、结束后强制清空——全程在 UI 线程同一次调用里，
        /// 外面看不到这个中间态（<see cref="IsRunning"/> 只在 Show() 成功后才该为 true）。
        /// </summary>
        private void AbortStart(ScadaRuntimeWindow? window, ScadaRuntime? session, string stage, Exception? error)
        {
            if (error == null)
                _log.Warn($"[运行] {stage}失败，本次运行已取消。");
            else
                _log.Warn($"[运行] {stage}失败，本次运行已取消：{error.Message}");

            if (window != null)
            {
                _session = session;
                _window = window;

                try
                {
                    // 正常会同步触发 Closed → OnWindowClosed，收尾只在那一处做。
                    window.Close();
                }
                catch (Exception closeError)
                {
                    _log.Warn($"[运行] 启动失败后关闭运行窗口也失败：{closeError.Message}");
                }

                if (ReferenceEquals(_window, window))
                {
                    try
                    {
                        OnWindowClosed(window, EventArgs.Empty);
                    }
                    catch (Exception teardownError)
                    {
                        _log.Warn($"[运行] 启动失败后收场出错：{teardownError.Message}");
                    }
                }
            }
            else if (session != null)
            {
                // 窗口都没造出来（<c>new ScadaRuntimeWindow</c> 抛了）：会话可能已经挂上导航器，
                // 这里按 OnWindowClosed 的后半段收一遍，免得留下一条指向半成品的引用。
                try
                {
                    _navigator.Attach(null);
                    session.Stop();
                }
                catch (Exception teardownError)
                {
                    _log.Warn($"[运行] 启动失败后收场出错：{teardownError.Message}");
                }
            }

            // 兜底：任何一步没收干净都不许把半成品留在字段上——留下的非空引用会让
            // 下一次 Start 早退（IsRunning 谎报在跑），现场表现就是"之后每次点运行都没反应"。
            _session = null;
            _window = null;
            _watch = null;
            _pendingLoadedPage = null;
            _firstFrameDone = false;
        }

        // ---- 会话侧 ----

        /// <summary>
        /// 会话决定"该显示这一页了"。两条通路，分界线是首帧：
        /// - <b>首帧之前</b>（<see cref="Start"/> 挑出的起步页）：先存着，等 <c>ContentRendered</c>
        ///   说明"真画出来了"再建表、再发 Loaded（见 <see cref="OnFirstFrameRendered"/>）。
        /// - <b>首帧之后</b>（运行中切页）：<c>ContentRendered</c> 一辈子只来一次，改用
        ///   "这一页的布局跑完"这个等价信号（见 <see cref="SchedulePageAfterLayout"/>）。
        ///
        /// 两条路的<b>后半段完全相同</b>（建表 → 发 Loaded），差别只在"什么时候算画好了"。
        /// 这也让"Loaded 只触发一次"这条承诺有两处守卫：会话侧靠"目标就是当前页就不换"
        /// （见 <see cref="ScadaRuntime.Navigate(ScadaPage?)"/>），宿主侧靠下面这一次分流。
        /// </summary>
        private void OnPageLoaded(ScadaPage page)
        {
            if (_firstFrameDone)
                SchedulePageAfterLayout(page);
            else
                _pendingLoadedPage = page;
        }

        /// <summary>
        /// 弹窗打开：等弹窗画布布局跑完再建表、发 Loaded。与切页后半段同一套顺序，
        /// 只是画布换成 <see cref="ScadaRuntimeWindow.PopupCanvasHost"/>。
        ///
        /// 弹窗关闭时 <see cref="OnPopupClosed"/> 摘表；窗口关闭时兜底再摘一次。
        /// </summary>
        private void OnPopupOpened(ScadaPage page)
        {
            _popupBinder?.Stop();
            _popupBinder = null;

            var generation = ++_popupGeneration;

            var dispatcher = _window?.Dispatcher;
            if (dispatcher == null)
                return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                // 回调是异步的：执行时可能已经关了弹窗、或者关了又开了同一页。
                // 引用比对防前者，代次防后者（同一页重开时引用会相等）。
                if (generation != _popupGeneration
                    || !ReferenceEquals(_session?.CurrentPopup, page))
                    return;

                var canvas = _window?.PopupCanvasHost;
                if (canvas == null)
                    return;

                _popupBinder?.Stop();
                _popupBinder = null;

                var binder = new ScadaRuntimeBinder(canvas, _valueSource, page, OnBindingDiagnostic);
                binder.Start();
                _popupBinder = binder;

                _session?.RaisePageEvent(page, ScadaEventType.Loaded);
            }), DispatcherPriority.Loaded);
        }

        /// <summary>弹窗关闭：作废排队回调并摘掉弹窗数据泵（控件刷回设计值），不留订阅。</summary>
        private void OnPopupClosed(ScadaPage page)
        {
            _popupGeneration++;
            _popupBinder?.Stop();
            _popupBinder = null;
        }

        /// <summary>
        /// 会话判完"哪条钩子命中"，这里把那一串动作交给分发器按序执行。
        /// 图元名一并带过去，日志里才能看出是哪个按钮被点了（分发器不认识模型，也不该认识）；
        /// "谁在操作"同理——分发器不认识用户系统，署名由宿主现取一次递过去。
        /// </summary>
        private void OnElementEventRaised(ScadaElement element, ScadaEventHook hook)
            => _dispatcher.Dispatch(hook, element.Name, _accessPolicy.CurrentUserName);

        /// <summary>画面级钩子（Loaded）：与图元走同一个分发器，只是报名字的是画面</summary>
        private void OnPageEventRaised(ScadaPage page, ScadaEventHook hook)
            => _dispatcher.Dispatch(hook, page.Name, _accessPolicy.CurrentUserName);

        /// <summary>
        /// 会话拦下了一次越权操作（S12）。两件事一起做：<b>说给人听</b>（气泡）+
        /// <b>留给查的人</b>（运行日志）。
        ///
        /// 为什么两条都要，而不是只弹一句
        /// ---------
        /// 气泡三秒就没了。现场"张三说他按过、李四说没按过"这种争执，只能靠日志里的
        /// 那一行（谁、什么时候、想按哪个图元、为什么不行）来判。反过来只写日志不弹气泡，
        /// 操作员看到的是"点了没反应"，会当成软件故障反复重试。
        ///
        /// 为什么记 Warn 而不是 Error：这不是软件坏了，是"这个人的权限不够"，
        /// 属于预期内的业务结果（<see cref="ScadaActionDispatcher"/> 里"配置没对"也记 Warn，
        /// 口径一致——Error 是留给"这次操作真没成、要人去查"的）。
        ///
        /// 原因原样带上来、不加工：那句话出自
        /// <see cref="ScadaRoleExtensions.Allows"/>（权限规则的唯一出处），
        /// 这里重写一遍措辞，就等于多了一份会跟它分叉的副本。
        ///
        /// 为什么被拦下的这一次也要落审计（S12-d）
        /// ---------
        /// 审计只记成功的那一半，事后查"他到底按过没有"时答案会是"没有"——
        /// 而事实上他按了、只是没成。权限系统里最该留痕的恰恰是这种<b>越权尝试</b>：
        /// 是操作不熟练，还是在试着绕权限，靠的只有这一行。
        /// 它记 Denied 档而不并进"失败"：权限不足不是故障，是设计如此。
        /// </summary>
        private void OnElementAccessDenied(ScadaElement element, string reason)
        {
            var who = _accessPolicy.CurrentUserName;
            var what = string.IsNullOrWhiteSpace(element.Name) ? "未命名图元" : element.Name;

            _log.Warn($"[权限] {who} 操作「{what}」被拒绝：{reason}");
            _notifier.ShowWarn($"没有权限操作「{what}」：{reason}");

            // 事件列写"权限校验"而不是"点击/按下"：闸门是在事件已经冒上来之后才判的，
            // 这里拿不到（也不该去猜）究竟是哪个事件触发的。写一个确切知道的事实，
            // 比写一个可能是错的猜测有用——查的人要问的是"谁想动这个图元"，不是"他用哪种方式点的"。
            // 动作列写"（未执行）"而不是留空：空着分不清"没记"和"没有动作"。
            _audit.Append(new ScadaAuditEntry(
                who, what, "权限校验", "（未执行）", ScadaAuditOutcome.Denied, reason));
        }

        // ---- 事件入口（窗口根上接冒泡） ----

        /// <summary>
        /// 从控件反查模型，再交给会话判那三道闸门。
        /// 这里<b>一条规则都不判</b>——连"这是不是当前画面上的图元"都不问：判归会话，跑动作归分发器，
        /// 宿主只是把两个互不相识的部分接起来。
        /// </summary>
        private void OnScadaEvent(object? sender, ScadaElementEventArgs args)
        {
            // _session 为 null 说明这一轮运行已经收场（窗口关掉之后仍可能有事件冒上来），直接收手
            if (_session == null) return;
            // 必须用 OriginalSource 而不是 Source：图元控件挂在代码创建的图层下（无逻辑父级），
            // 冒泡途中 WPF 会把 Source 重定目标到最近有逻辑连接的祖先（画布），
            // 而 OriginalSource 永远锁定在 RaiseEvent 的那个控件本尊上，不受重定目标影响。
            if (args.OriginalSource is not ScadaElementBase control) return;

            _session.RaiseElementEvent(control.Element, args.ScadaEvent);
        }

        // ---- 报警侧 ----

        /// <summary>
        /// 起报警系统：建落盘端 → 建引擎并挂上变量 → 起节拍。
        ///
        /// 为什么在 <c>Show()</c> <b>之前</b>起：报警引擎挂载时会对每条定义<b>立即初判一次</b>，
        /// 而"启动那一刻变量早就越限了"是现场常态（设备停在那里、温度本来就高）。
        /// 挂在 Show() 之后，这段窗口期里发生的报警就没人判了；更要紧的是——初判属于
        /// "软件一启动就该报"的事实，不该取决于操作员看不看那个窗口。
        ///
        /// 为什么节拍用窗口的 Dispatcher：图元闪烁要改依赖属性、报警面板要改集合，
        /// 都必须在 UI 线程上。节拍源统一跑在运行窗口的 UI 线程，消费者就不必各自切线程。
        /// </summary>
        private void StartAlarms(ScadaDocument document, ScadaRuntimeWindow window)
        {
            // 先赋值再挂载：Attach() 里就会做初判并可能立刻抛 AlarmRaised，
            // 那一刻 _alarmWriter / _alarms 必须已经是活的，否则第一条报警会被吞掉。
            // 报警历史跟着方案走：<方案目录>\Alarms。CSV 是给人看的流水，JSON 是给历史面板恢复的快照。
            // 方案还没保存过（SolutionFilePath 为空）时回落到软件目录，至少不丢数据。
            var alarmDirectory = ScadaAlarmHistoryStore.ResolveDirectory(_workspaceManager.CurrentSolution);
            _alarmStore = new ScadaAlarmHistoryStore(alarmDirectory, OnBindingDiagnostic);
            _priorAlarms = _alarmStore.Load();
            var priorAlarms = _priorAlarms;
            if (priorAlarms.Count > 0)
                _log.Info($"[报警] 已从方案目录恢复历史 {priorAlarms.Count} 条：{alarmDirectory}");

            _alarmWriter = new ScadaAlarmHistoryWriter(
                alarmDirectory,
                // 诊断口复用数据泵那一个：日志里"落盘失败"和"绑定没接上"应该长得一样，
                // 现场排查时才不用先想"这条是哪个子系统报的"。
                OnBindingDiagnostic,
                // 保留期每次清理时现读软件级配置（S13-d）：报警历史攒得比审计快得多，
                // 现场"这台机半年没人翻历史"和"故障复盘要翻一年"两种诉求都得容得下。
                // 现读而非构造时定死：改完「系统 → 系统参数设置」不必重启，下一次跨天清理就用新值。
                retentionProvider: () => _appSettings.Current.AlarmHistoryRetentionDays);

            var engine = new ScadaAlarmEngine(document, _valueSource);
            engine.AlarmRaised += OnAlarmRaised;
            // 状态迁移（确认/恢复/清除）也要进 CSV 与 JSON：只订阅 AlarmRaised 的话，
            // 历史文件永远停在“激活”那一行，操作员确认过没有、什么时候恢复的都查不到。
            engine.AlarmChanged += OnAlarmChanged;
            _alarms = engine;

            var beat = new ScadaBeatSource(window.Dispatcher, AlarmBeatInterval, OnBindingDiagnostic);
            beat.Beat += OnBeat;
            _beat = beat;

            // 把"引擎 + 节拍 + 回写通道"打成一个包交给画布，由画布在造每个图元控件时转发下去。
            // 顺序上必须在 Attach()/Start() 之前：这两个调用一落地就可能抛事件，而订阅方
            // （报警条图元）是"装上上下文才订阅"的——先装包，订阅关系才赶得上第一条报警。
            //
            // 为什么由画布转发而不是宿主自己遍历控件：宿主压根不认识那些控件，也不该认识。
            // 它知道的是"这一轮运行有个引擎、有个节拍"，画布知道的是"这一页上有哪些图元"，
            // 各交各的那一半（见 ScadaCanvas.RuntimeContext 的注释）。
            //
            // 回写通道（第三个）与读通道（数据泵里的 _valueSource）同生共死：都在这里装、
            // 都在 TeardownAlarms 里随上下文一起撤（上下文一置空，图元手里的写口就跟着失效）。
            //
            // 权限出口（第四个）也一起装进去：图元要"写前先查"，可它只攥着这个上下文，
            // 够不着会话（_accessPolicy 在本类里，图元不认识本类）。它与写通道同一时机进出，
            // 且会话对象是活的——"此刻是谁"由它每次现问，登录/登出立刻生效。
            // 值通道（第八个）：数据表格这类"列定义里写的是变量名"的图元要按名字解析句柄。
            // 数据泵虽然拿着同一个值源，但它只按 ScadaBinding 推属性，帮不了"按名字取数"。
            // 历史与报表共用同一个目录（<方案根>\History 下的 CSV 账本）：
            // · history 给「数据表格」的 History 模式（分页看最近 N 条）；
            // · reports 给「报表」图元（整段范围聚合）。
            // 桌面运行窗口原先漏了 history（只有后台监控宿主接了），表现是运行态表格切到历史
            // 一直显示"未接入历史数据源"——这里一并补上。
            var historyDirectory = SolutionPaths.GetHistoryDirectory(_workspaceManager.CurrentSolution?.SolutionFilePath);
            var historySource = new CsvFolderHistorySource(historyDirectory);
            var reportSource = new CsvReportSource(historyDirectory);

            window.CanvasHost.RuntimeContext = new ScadaRuntimeContext(
                engine, beat, _valueWriter, _accessPolicy, _axisSource, _axisCommands, _confirmations, _valueSource,
                priorAlarms, historySource, document.StateLists, subPages: _session,
                reports: reportSource, reportExporter: new ScadaReportExporter(), audit: _audit,
                accessLoginPrompt: _accessLoginPrompt,
                languages: _languages,
                alarmMute: _alarmMute,
                recipeCommands: _recipeCommands);

            engine.Attach();
            beat.Start();

            // 配了 0 条报警就别记这一行——那是绝大多数方案的常态，记了只会把日志淹掉。
            // 未订阅上的条数必须报：它和 SlotCount 一起才能回答"报警怎么不响"。
            if (engine.SlotCount > 0)
            {
                _log.Info($"[报警] 引擎已挂载：定义 {engine.SlotCount} 条 / 订阅变量 {engine.SubscribedCount} 个 / 节拍 {AlarmBeatInterval.TotalMilliseconds:0}ms");
            }
        }

        /// <summary>
        /// 一拍：把时间推给报警引擎与变量事件引擎。
        ///
        /// 引擎<b>自己不起定时器</b>（见 <see cref="ScadaAlarmEngine.Tick"/> 的注释），
        /// 所以少了这一推，"激活延时到期"和"通信断线"这两类报警就永远不会响——
        /// 而这两类恰恰是现场最要紧的：延时是消抖、断线是"你连当前值都不知道了"。
        ///
        /// 变量事件引擎也搭这一拍，但它<b>没有时间维度</b>（边沿判定只看新旧两个值）：
        /// 这一推只用来把"运行中改过变量事件配置"转成一次重挂
        /// （见 <see cref="ScadaVariableEventEngine.Tick"/>）。共用一拍就不必为它再养一个定时器。
        /// </summary>
        private void OnBeat(TimeSpan elapsed)
        {
            _alarms?.Tick();
            _variableEvents?.Tick();
        }

        // ---- 报警命令出口（M2：Web 命令层的「确认报警」） ----

        /// <summary>
        /// 当前活着的报警引擎。报警引擎是<b>方案级</b>的（归 <see cref="ScadaMonitoringHost"/>），
        /// 不是运行窗口的附属品——所以先取监控上下文，本类的 <c>_alarms</c> 只作窗口级旧路径的兜底。
        ///
        /// <b>顺序不能反</b>：只认 <c>_alarms</c> 的话，Web 在运行中确认报警会永远得到"未运行"，
        /// 因为那条路径现在已不建引擎（<c>StartAlarms</c> 只保留着未接线的旧实现）。
        /// </summary>
        private ScadaAlarmEngine? CurrentAlarmEngine => _monitoring.Context?.Alarms ?? _alarms;

        /// <inheritdoc/>
        public ScadaAlarmFeedSnapshot Snapshot()
        {
            var engine = CurrentAlarmEngine;
            if (engine == null)
                return ScadaAlarmFeedSnapshot.Empty;

            var active = engine.ActiveAlarms;
            var alarms = new List<ScadaAlarmInfo>(active.Count);

            foreach (var record in active)
            {
                alarms.Add(new ScadaAlarmInfo(
                    record.AlarmId,
                    record.Name ?? string.Empty,
                    record.Message ?? string.Empty,
                    record.Severity,
                    record.ActivatedAtUtc,
                    record.AcknowledgedAtUtc != null));
            }

            return new ScadaAlarmFeedSnapshot(alarms, engine.UnacknowledgedCount);
        }

        /// <inheritdoc/>
        public bool TryAcknowledge(Guid alarmId, out string? error)
        {
            error = null;

            if (!IsRunning)
            {
                error = "未运行";
                return false;
            }

            var engine = CurrentAlarmEngine;
            if (engine == null)
            {
                error = "未运行";
                return false;
            }

            if (alarmId == Guid.Empty)
            {
                error = "报警 Id 为空";
                return false;
            }

            try
            {
                // 先记下"确认之前"它在不在实时列表上、处于哪个态：引擎返回 0 时，
                // 这两种情况要给出不同的原因（见 IScadaAlarmCommands.TryAcknowledge 的注释）。
                ScadaAlarmRecord? record = null;
                foreach (var item in engine.ActiveAlarms)
                {
                    if (item.AlarmId == alarmId)
                    {
                        record = item;
                        break;
                    }
                }

                if (engine.Acknowledge(alarmId) > 0)
                    return true;

                error = record == null
                    ? "报警不在当前实时列表上（可能已恢复清除，或不属于本轮运行）"
                    : record.State == ScadaAlarmState.Acknowledged
                        ? "该报警已经确认过"
                        : $"该报警当前状态为「{record.State.DisplayName()}」，确认未生效";
                return false;
            }
            catch (Exception ex)
            {
                // 引擎本身不抛业务异常，但确认会同步触发 AlarmChanged（宿主落盘订阅方）。
                // Web 命令不许把异常冒到网络线程/进程顶层，按"这次没成"如实回一句话。
                error = $"确认报警失败：{ex.Message}";
                return false;
            }
        }

        /// <inheritdoc/>
        public int AcknowledgeAll(out string? error)
        {
            error = null;

            if (!IsRunning)
            {
                error = "未运行";
                return 0;
            }

            var engine = CurrentAlarmEngine;
            if (engine == null)
            {
                error = "未运行";
                return 0;
            }

            try
            {
                // 0 = 没有可确认的（已确认过的不计入），不是失败，所以 error 保持 null。
                return engine.AcknowledgeAll();
            }
            catch (Exception ex)
            {
                error = $"全部确认失败：{ex.Message}";
                return 0;
            }
        }

        /// <summary>
        /// 一条报警刚被激活：落一行历史，再记一条运行日志。
        ///
        /// <b>线程</b>：引擎承诺事件在锁外抛，但<b>不</b>承诺在 UI 线程抛——条件类报警的值变化来自
        /// 变量轮询线程，断线报警则由节拍（UI 线程）发现。落盘与写日志都不碰 UI 控件，
        /// 所以在哪个线程上都安全；将来要在这里弹窗或刷面板，必须自己切回 Dispatcher。
        /// </summary>
        private void OnAlarmRaised(ScadaAlarmRecord record)
        {
            // 报警是运行观测里最该留痕的一类：配没配报警面板，日志里都得能查到"当时报了什么"。
            // 严重度决定记 Error 还是 Warn——把"严重"降级成 Warn，复盘时会看不出当时有多急。
            var text = $"[报警] {record.Severity.DisplayName()}｜{record.Name}：{record.Message}"
                     + $"（{record.ConditionText}，触发值 {record.TriggerValue ?? "—"}）";
            if (record.Severity >= ScadaAlarmSeverity.Critical)
                _log.Error(text);
            else
                _log.Warn(text);

            // 报警帧留存（默认关，见 ImageGallerySettings.SaveFramesOnAlarm）：
            // 把当前图集整批落盘到 AlarmFrames，便于事后复盘"报警当时画面是什么样"。
            // 走单例而不是构造函数注入：本类由容器构造、可选参数不会被注入（见 App.xaml.cs 的注册注释），
            // 而这是个默认关闭的可选能力，不该为它改动 ScadaRuntimeHost 的构造契约。
            try { ImageCollectionService.Instance?.SaveSnapshot("报警_" + record.Name); }
            catch (Exception ex) { _log.Warn($"[报警留存] 图像集留存失败：{ex.Message}"); }
        }

        /// <summary>
        /// 报警态迁移（激活 / 确认 / 恢复 / 清除）的统一落盘点。
        ///
        /// 为什么挂 <see cref="ScadaAlarmEngine.AlarmChanged"/> 而不是 <c>AlarmRaised</c>：
        /// 后者只在"从正常进入激活"时抛一次；只挂它，CSV 与 JSON 会永远停在激活态，
        /// 操作员确认/恢复/清除的时间线全丢。AlarmChanged 在激活时也会抛（引擎保证），
        /// 所以这里一条事件覆盖完整生命周期，不会漏也不会重复。
        /// </summary>
        private void OnAlarmChanged(ScadaAlarmRecord record)
        {
            lock (_alarmPersistGate)
            {
                // CSV：一行一次状态迁移（BuildRow 写的是迁移后的态，顺着文件读就是时间线）
                _alarmWriter?.Append(record);

                // JSON：整体快照，供下次打开方案时历史面板恢复“上次看到的报警”。
                // 必须把本轮开始前读回的 prior 一起写回：只写本轮 engine.History 会把
                // 上一次运行的记录在第一次报警时整批覆盖掉（“这次加载又看不到上次的”）。
                var recent = _alarms?.RecentRecords(ScadaAlarmHistoryStore.MaxRecords);
                if (recent != null)
                    _alarmStore?.Save(ScadaAlarmHistoryStore.Merge(_priorAlarms, recent));
            }
        }

        /// <summary>
        /// 收报警系统：停节拍 → 摘事件 → 引擎摘槽。
        ///
        /// 顺序的理由：<b>节拍最先停</b>，否则一次迟到的 Tick 会落到一个正在收场的引擎上
        /// （引擎内部会判 <c>_attached</c> 兜住，但"先断线后关灯"读起来不必靠对方兜）。
        /// <b>摘槽在 <c>session.Stop()</c> 之前</b>：引擎攥着变量订阅，而变量注册表比窗口活得久得多。
        /// </summary>
        private void TeardownAlarms()
        {
            // 注入先摘：这一步会让每个图元控件退掉引擎三事件与节拍的订阅。
            // 排在"停节拍 / 摘引擎"之前，是因为订阅方比引擎先退场更符合因果——
            // 反过来的话，摘订阅就发生在引擎已经收场之后，虽然结果一样，读起来像对着尸体动手。
            // 画布还在（OnWindowClosed 里 _window 尚未清空），所以这条在正常收场路径上一定生效。
            if (_window != null)
                _window.CanvasHost.RuntimeContext = null;

            if (_beat != null)
            {
                _beat.Stop();
                _beat.Beat -= OnBeat;
                _beat = null;
            }

            if (_alarms != null)
            {
                _alarms.AlarmRaised -= OnAlarmRaised;
                _alarms.AlarmChanged -= OnAlarmChanged;

                // 收场前再落一次快照：最后一次状态迁移可能刚发生、事件还没跑完，
                // 或者用户直接关窗口（没有下一次 AlarmChanged 可等）。
                lock (_alarmPersistGate)
                {
                    var recent = _alarms.RecentRecords(ScadaAlarmHistoryStore.MaxRecords);
                    if (recent.Count > 0)
                        _alarmStore?.Save(ScadaAlarmHistoryStore.Merge(_priorAlarms, recent));
                }

                _alarms.Detach();   // 历史保留在引擎里；这里只是退订阅、丢槽
                _alarms = null;
            }

            _alarmWriter = null;
            _alarmStore = null;
            _priorAlarms = Array.Empty<ScadaAlarmRecord>();
        }

        // ---- 变量事件侧 ----

        /// <summary>
        /// 起变量事件系统：建引擎并挂上变量。
        ///
        /// 为什么与报警引擎一样排在 <c>Show()</c> 之前：挂载只记基线、不补发历史
        /// （见 <see cref="ScadaVariableEventEngine.Attach"/>），所以早挂只会让监听窗口更早生效，
        /// 晚挂则会漏掉这段窗口期里的那次跳变——那是操作员真做过的动作，漏了就查不到。
        ///
        /// 为什么引擎不塞进 <see cref="ScadaRuntime"/>：会话刻意不加锁（只在 UI 线程上用），
        /// 而这里的值回调来自变量轮询线程——与报警引擎同一条论证。
        /// </summary>
        private void StartVariableEvents(ScadaDocument document, ScadaRuntimeWindow window)
        {
            var engine = new ScadaVariableEventEngine(document, _valueSource);
            engine.VariableEventRaised += OnVariableEventRaised;
            _variableEvents = engine;
            _variableEventDispatcher = window.Dispatcher;

            engine.Attach();

            // 配了 0 条就别记这一行——那是绝大多数方案的常态，记了只会把日志淹掉。
            // 未订阅上的条数必须报：它和 SlotCount 一起才能回答"变量事件怎么不响"。
            if (engine.SlotCount > 0)
            {
                _log.Info($"[变量事件] 引擎已挂载：记录 {engine.SlotCount} 条 / 订阅变量 {engine.SubscribedCount} 个");
            }
        }

        /// <summary>
        /// 一条变量级事件（更改数值 / 值为真 / 值为假 / 上越限 / 下越限）命中，把动作串交给分发器。
        ///
        /// <b>线程</b>：引擎承诺事件在锁外抛，但<b>不</b>承诺在 UI 线程抛——值回调来自变量轮询线程。
        /// 而分发器明说"只在 UI 线程被调用、故不加锁"（见 <see cref="IScadaActionDispatcher"/>），
        /// 所以这里必须自己切回去。照抄 <see cref="OnElementEventRaised"/> 的直接调用，
        /// 就是一条只在多线程下偶发、事后极难归因的脏写路径。
        ///
        /// 署名用变量名而不是图元名：这次动作不是谁点的，是"这个量变了"。
        /// 与报警一样，判"哪条钩子命中"在领域层，宿主只负责切线程 + 把"谁在操作"递过去。
        /// </summary>
        private void OnVariableEventRaised(ScadaVariableEvent record, ScadaEventHook hook)
            => RunOnUi(() => DispatchVariableEvent(record, hook));

        /// <summary>
        /// 真正跑动作的那一半（保证在 UI 线程上）。
        ///
        /// 排到队里之后局面可能已经变了（窗口关了、切方案了），所以这里再确认一次引擎还活着：
        /// 引擎一摘就说明"这一轮运行"已经收场，迟到的动作不该再落到那个会话上。
        /// </summary>
        private void DispatchVariableEvent(ScadaVariableEvent record, ScadaEventHook hook)
        {
            if (_variableEvents == null)
                return;

            _dispatcher.Dispatch(hook, record.VariableName, _accessPolicy.CurrentUserName);
        }

        /// <summary>
        /// 把一段动作送回运行窗口的 UI 线程执行（已经在 UI 线程上就直接跑）。
        ///
        /// 为什么判 <c>HasShutdownStarted</c>：窗口关掉的一瞬间，变量轮询线程可能正好
        /// 抛出一条变量事件，此时往已停摆的 Dispatcher 队列里塞东西会直接抛异常。
        /// 那种异常发生在收场路径上，最难归因，也不值得为它留一条日志。
        /// （与 AlarmBannerElement.RunOnUi 同一口径：Dispatcher 已经收摊，动作就没有受众了。）
        /// </summary>
        private void RunOnUi(Action action)
        {
            var dispatcher = _variableEventDispatcher;
            if (dispatcher == null)
                return;   // 这一轮运行已经收场

            if (dispatcher.CheckAccess())
            {
                action();
                return;
            }

            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
        }

        /// <summary>
        /// 收变量事件系统：摘事件 → 引擎摘槽。
        ///
        /// 顺序与报警同一条理由：引擎攥着变量订阅，而变量注册表比窗口活得久得多——
        /// 摘槽必须在窗口收场里做掉，不能指望"窗口没了订阅自然就没了"（不会有这种事）。
        /// </summary>
        private void TeardownVariableEvents()
        {
            // 先置空 Dispatcher：它一空，任何迟到的值事件都直接丢弃，
            // 不会再往一个正在收摊的 Dispatcher 上排队。
            _variableEventDispatcher = null;

            if (_variableEvents != null)
            {
                _variableEvents.VariableEventRaised -= OnVariableEventRaised;
                _variableEvents.Detach();   // 事件配置留在文档里；这里只是退订阅、丢槽
                _variableEvents = null;
            }
        }

        // ---- 窗口侧 ----

        /// <summary>
        /// 首帧真的画完了：记一行运行观测（页面 + 耗时），再把这一页的 Loaded 钩子交给会话判定、
        /// 由分发器执行。只会在窗口生命周期内来一次（本窗口不会隐藏后再显示，退出就是关掉）。
        /// </summary>
        private void OnFirstFrameRendered(object? sender, EventArgs e)
        {
            double ms = _watch?.Elapsed.TotalMilliseconds ?? 0;
            _watch = null;

            // 从这一刻起，"会话说要显示某一页"改走切页通道——ContentRendered 不会再来第二次。
            // 放在取页之前：这一帧本身仍按首帧的老路走（下面那段），标志只影响之后的切页。
            _firstFrameDone = true;

            // 状态条回填的耗时随窗口一起消失，日志里这一行不会：现场排查"画面点开是黑的/转圈久"
            // 要看的就是它，所以这一行属于**运行观测**，配没配事件都得记。
            // 放在取页之前：耗时是"窗口开出来了"这件事的度量，跟哪一页关系不大，
            // 万一页没记上（正常路径不会，Start() 后 PageLoaded 是无条件的）也不该把耗时吞掉。
            _window?.ReportLoadElapsed(ms);

            var page = _pendingLoadedPage;
            _pendingLoadedPage = null;
            if (page == null) return;

            _log.Info($"[运行] 画面「{page.Name}」({page.Width:0}×{page.Height:0}) 首帧耗时 {ms:0}ms");

            // Loaded 的**动作**（日志/写变量/…）必须等这一帧之后才跑：
            // 画面还没画出来就记"加载完成"，现场看到只会以为程序卡在了别处。
            // 这里只上报"发生了什么"，配没配钩子、要不要执行，仍归会话判（宿主不越权）。
            StartBinder(page);

            _session?.RaisePageEvent(page, ScadaEventType.Loaded);
        }

        /// <summary>
        /// 建运行态数据泵：把这一页上每条"变量 → 图元属性"的绑定接到变量上，并先刷一遍当前值。
        ///
        /// 为什么卡在"首帧之后、Loaded 动作之前"这个位置
        /// ---------
        /// 之前：画布上的图元控件是布局阶段（<c>OnApplyTemplate → RebuildElements</c>）才造出来的，
        /// 会话 <c>Start()</c> 那一刻画布还是空的，那时建表只会建出一张空表。
        /// 之后：Loaded 钩子里可能有"写变量"的动作——表建好了，这一写才有图元收得到，
        /// 否则操作员会看到"画面刚打开时那个值没显示，动一下才出来"。
        ///
        /// 建表失败（拿不到画布）不当作错误：这只在窗口提前关闭时可能发生，
        /// 那种情况下整轮运行本来就在收场，没必要再报一句吓人。
        /// </summary>
        private void StartBinder(ScadaPage page)
        {
            var canvas = _window?.CanvasHost;
            if (canvas == null)
                return;

            var binder = new ScadaRuntimeBinder(canvas, _valueSource, page, OnBindingDiagnostic);
            binder.Start();
            _binder = binder;

            // 没配任何绑定的画面不记这一行：那是绝大多数页面的常态，记了只会把日志淹掉。
            // 未命中的条数也要报——它和 BoundCount 一起才能回答"画面怎么不动"。
            if (binder.BoundCount + binder.MissCount > 0)
            {
                _log.Info($"[运行] 数据泵建表：命中 {binder.BoundCount} 条绑定 / 未命中 {binder.MissCount} 条 / 订阅变量 {binder.SubscriptionCount} 个");
            }
        }

        /// <summary>
        /// 运行中切页的后半段：旧页数据泵摘表 → 等新页布局跑完 → 新页建表 → 发新页 Loaded 动作。
        ///
        /// 为什么摘表要<b>同步</b>做、建表要<b>等</b>：
        /// <c>CurrentPage</c> 一变，画布上的 XAML 绑定（<c>ItemsSource="{Binding CurrentPage.Elements}"</c>）
        /// 就会把旧图元控件全丢掉、按新页重造一遍。旧表攥着旧控件的引用与变量订阅，
        /// 必须赶在控件被丢掉之前摘干净（摘表时会顺手把控件刷回设计值）；而新表只能等新控件造出来
        /// 才建得成——这正是"布局跑完"这个信号的用处。
        ///
        /// 为什么用 <see cref="DispatcherPriority.Loaded"/>：它排在 <c>Render</c> 之后（画面已经画完）、
        /// <c>Input</c> 之前（还赶得上在操作员下一次点击前把值刷上）。用 <c>Background</c> 也能跑，
        /// 但持续有鼠标动作时会被一直往后排，"画面刚打开时那个值没显示，动一下才出来"就会回来。
        /// </summary>
        private void SchedulePageAfterLayout(ScadaPage page)
        {
            // 旧页的表先摘，理由见上面"为什么摘表要同步做"
            _binder?.Stop();
            _binder = null;

            var dispatcher = _window?.Dispatcher;
            if (dispatcher == null)
                return;   // 窗口已经关了：这一轮运行正在收场，没有画布可建表

            _log.Info($"[运行] 切换到画面「{page.Name}」({page.Width:0}×{page.Height:0})");

            dispatcher.BeginInvoke(new Action(() =>
            {
                // 回调是异步的，执行时局面可能已经变了：窗口关了，或者又连点着切了一页。
                // 判"当前页还是不是它"按引用比对，与会话里那三道闸门同一口径——
                // 这里再判一次不是不信任会话，而是**这一刻**和**排队那一刻**本来就可能不是同一时刻。
                if (!ReferenceEquals(_session?.CurrentPage, page))
                    return;

                StartBinder(page);
                _session?.RaisePageEvent(page, ScadaEventType.Loaded);
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 数据泵的诊断上报（角标之外的第二条记录）。
        /// 角标随窗口消失，日志不消失——"当时到底哪条绑定没接上"只能靠它复盘。
        /// </summary>
        private void OnBindingDiagnostic(ScadaDiagnosticLevel level, string message)
        {
            if (level >= ScadaDiagnosticLevel.Error)
                _log.Error(message);
            else
                _log.Warn(message);
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            // 数据泵第一个摘：它攥着变量订阅，而变量注册表比窗口活得久得多
            // （窗口关一百次，注册表还拿着一百份控件树）。而且它摘表时会把控件刷回设计值，
            // 这一步得趁控件还活着做，所以必须排在任何"清空引用"之前。
            _binder?.Stop();
            _binder = null;

            _popupBinder?.Stop();
            _popupBinder = null;

            // 报警/变量事件引擎不在这里摘：它们是方案级监控（ScadaMonitoringHost），
            // 关窗口只是退视图，监控继续跑。这正是“只跑流程、不开画面也能报警/触发变量事件”的关键。

            // 摘掉切页出口上的会话：分发器是长命单例，留着这根线头会让一条迟到的"切换画面"动作
            // 去操作一个已经停掉的会话（见 ScadaNavigator 的类注释）。排在 session.Stop() 之前，
            // 摘掉之后就再没有动作能落到这个会话上了。
            _navigator.Attach(null);

            var session = _session;
            if (session != null)
            {
                // 先摘订阅再 Stop()：Stop() 之后会话就"不运行"了，理论上不会再广播事件，
                // 但把顺序写成"先断线后关灯"，将来谁改了 Stop() 的内部实现也不会漏出一次幽灵触发。
                session.PageLoaded -= OnPageLoaded;
                session.PopupOpened -= OnPopupOpened;
                session.PopupClosed -= OnPopupClosed;
                session.ElementEventRaised -= OnElementEventRaised;
                session.PageEventRaised -= OnPageEventRaised;
                session.ElementAccessDenied -= OnElementAccessDenied;
                session.Stop();
            }

            if (sender is ScadaRuntimeWindow window)
            {
                window.ContentRendered -= OnFirstFrameRendered;
                window.Closed -= OnWindowClosed;
                window.RemoveHandler(ScadaElementBase.ScadaEventEvent, _onScadaEvent);
            }

            // 宿主是单例，会话与窗口是一次性的：留下的引用会把上一轮的页面模型一直撑住
            // （切方案时的画面明明该重建了，却还被这儿拽着），所以这里必须清空。
            _session = null;
            _window = null;
            _watch = null;
            _pendingLoadedPage = null;
            _firstFrameDone = false;
            ScadaConfirmationService.InteractiveAllowed = false;

            // 发 RunStopped 必须排在上面那些清理之后：订阅方在处理器里读 hub.Session/Document
            // 必须已经是 null（"最后一帧"要自己缓存，见 IScadaRunHub.RunStopped 的注释）。
            // 只有确实发过 RunStarted 的一轮才会发——启动失败那条路（session.Start() 返回 false、
            // 或 Show() 抛异常）从未调过 TryStarted，所以这里 TryStopped 返回 false，不会漏发一次 Stopped。
            if (_hubState.TryStopped(out var generation))
                PublishStopped(generation);
        }

        private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 只认 CurrentSolution：换方案 = 要跑的已经不是刚才那份文档了。
            // 留着旧会话，就是在运行一份界面上已经看不见的方案。
            if (e.PropertyName != nameof(IReadOnlyWorkspaceContext.CurrentSolution)) return;

            Stop();
        }
    }
}
