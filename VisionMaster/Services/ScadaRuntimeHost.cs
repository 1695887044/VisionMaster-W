using System;
using System.ComponentModel;
using System.Diagnostics;
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
    public sealed class ScadaRuntimeHost : IScadaRuntimeHost
    {
        private readonly ILogService _log;
        private readonly IUserNotifier _notifier;

        /// <summary>工作区（弱依赖：拿不到通知就不做"切方案自动停止"，但运行本身照样能用）</summary>
        private readonly INotifyPropertyChanged? _workspace;

        private ScadaRuntime? _session;
        private ScadaRuntimeWindow? _window;

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
        /// 软件级配置（AppConfig.json）。这里只用它两件事：读"运行窗口该长什么样"
        /// （<c>RunWindowMode</c>）和"该落在哪块屏"（<c>RunWindowMonitor</c>）。
        /// 每次 <see cref="Start"/> 现读 <c>Current</c>，所以「系统 → 运行窗口设置」改完立刻生效，
        /// 不用重启软件——设置弹窗自己负责写盘（见 ScadaRunWindowSettingsViewModel）。
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
            AppSettingsService appSettings)
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
            _onScadaEvent = OnScadaEvent;

            // 三个都是单例，这条订阅不会把谁钉住（不存在"宿主早于工作区被回收"的情况），
            // 所以不留 Dispose：为了对称而写一套只会被调一次的解绑代码，读的人反而要猜为什么。
            _workspace = workspace as INotifyPropertyChanged;
            if (_workspace != null)
                _workspace.PropertyChanged += OnWorkspacePropertyChanged;
        }

        /// <inheritdoc/>
        public bool IsRunning => _window != null;

        /// <inheritdoc/>
        public void Start(ScadaDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            // 单实例。重复点"运行"在现场太常见：真开出两份会话，就是两份运行态各自往
            // 同一批变量上写，出问题时分不清是谁写的。所以第二次只是把窗口带到前台。
            if (_window != null)
            {
                _window.Activate();
                return;
            }

            var session = new ScadaRuntime(document, _accessPolicy);
            var window = new ScadaRuntimeWindow(session);

            // 形态必须在 Show() 之前定死：WindowStyle/WindowState 这类属性在窗口显示之后再改，
            // WPF 会重建非客户区，表现为闪一下、尺寸跳一格（依附形态下更明显）。
            //
            // 这里一次把"长什么样"和"落在哪块屏"都现读出来：两者都来自软件级配置，
            // 都只在这一次 Start 上生效（改完不必重启，下一次点「运行」就是新样子）。
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

            session.PageLoaded += OnPageLoaded;
            session.ElementEventRaised += OnElementEventRaised;
            session.PageEventRaised += OnPageEventRaised;
            session.ElementAccessDenied += OnElementAccessDenied;
            window.ContentRendered += OnFirstFrameRendered;
            window.Closed += OnWindowClosed;
            // 在窗口根上接住所有图元冒泡上来的组态事件：一处接线，中间的可视树层级不用管
            window.AddHandler(ScadaElementBase.ScadaEventEvent, _onScadaEvent);

            _session = session;
            _window = window;

            // 把"此刻活着的是哪一个会话"告诉切页出口。放在 Start() 之前：Start() 里就会发出
            // 第一页的 PageLoaded，那一刻若有钩子要切页，动作也得找得到会话（虽然正常配不出来）。
            // 摘掉在 OnWindowClosed 的收尾里，与这里成对。
            _navigator.Attach(session);

            // 计时从这一刻起：包含了 Start() 里的选页、建窗口、布局、首帧渲染。
            // 用 Stopwatch 而不是 DateTime.Now：墙上时钟会被 NTP/手工调整跳一下，
            // 差值就变成负数或几百秒，现场看到只会怀疑代码。
            _watch = Stopwatch.StartNew();

            if (!session.Start())
            {
                // 方案里一页都没有：不弹空白全屏（那比什么都不发生更让人慌），给一句人话。
                // 收场走 Close()，让 OnWindowClosed 那一处负责全部清理，不留第二条清理路径。
                _notifier.ShowWarn("当前方案里没有可运行的画面，请先新建画面并指定启动画面。");
                window.Close();
                return;
            }

            StartAlarms(document, window);
            StartVariableEvents(document, window);

            window.Show();
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
            // 报警引擎、变量事件引擎与节拍同理——它们比窗口先起，也必须在同一处收干净。
            TeardownAlarms();
            TeardownVariableEvents();
            _session?.Stop();
            _session = null;
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
            _alarmWriter = new ScadaAlarmHistoryWriter(
                ScadaAlarmHistoryWriter.DefaultDirectory,
                // 诊断口复用数据泵那一个：日志里"落盘失败"和"绑定没接上"应该长得一样，
                // 现场排查时才不用先想"这条是哪个子系统报的"。
                OnBindingDiagnostic,
                // 保留期每次清理时现读软件级配置（S13-d）：报警历史攒得比审计快得多，
                // 现场"这台机半年没人翻历史"和"故障复盘要翻一年"两种诉求都得容得下。
                // 现读而非构造时定死：改完「系统 → 系统参数设置」不必重启，下一次跨天清理就用新值。
                retentionProvider: () => _appSettings.Current.AlarmHistoryRetentionDays);

            var engine = new ScadaAlarmEngine(document, _valueSource);
            engine.AlarmRaised += OnAlarmRaised;
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
            window.CanvasHost.RuntimeContext = new ScadaRuntimeContext(engine, beat, _valueWriter);

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

        /// <summary>
        /// 一条报警刚被激活：落一行历史，再记一条运行日志。
        ///
        /// <b>线程</b>：引擎承诺事件在锁外抛，但<b>不</b>承诺在 UI 线程抛——条件类报警的值变化来自
        /// 变量轮询线程，断线报警则由节拍（UI 线程）发现。落盘与写日志都不碰 UI 控件，
        /// 所以在哪个线程上都安全；将来要在这里弹窗或刷面板，必须自己切回 Dispatcher。
        /// </summary>
        private void OnAlarmRaised(ScadaAlarmRecord record)
        {
            // 落盘失败不抛、只报诊断（见 ScadaAlarmHistoryWriter）：丢几行历史远好过报警系统崩掉
            _alarmWriter?.Append(record);

            // 报警是运行观测里最该留痕的一类：配没配报警面板，日志里都得能查到"当时报了什么"。
            // 严重度决定记 Error 还是 Warn——把"严重"降级成 Warn，复盘时会看不出当时有多急。
            var text = $"[报警] {record.Severity.DisplayName()}｜{record.Name}：{record.Message}"
                     + $"（{record.ConditionText}，触发值 {record.TriggerValue ?? "—"}）";
            if (record.Severity >= ScadaAlarmSeverity.Critical)
                _log.Error(text);
            else
                _log.Warn(text);
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
                _alarms.Detach();   // 历史保留在引擎里；这里只是退订阅、丢槽
                _alarms = null;
            }

            _alarmWriter = null;
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

            // 报警引擎第二个摘：它和数据泵一样攥着变量订阅（只是不攥控件），
            // 而变量注册表比窗口活得久得多。摘槽时顺带把节拍停掉——见 TeardownAlarms。
            TeardownAlarms();

            // 变量事件引擎第三个摘：它同样攥着变量订阅。排在 TeardownAlarms 之后，
            // 是因为它搭报警那一拍做 Tick（见 OnBeat）——节拍先停，就绝不会有一拍落到正在收场的引擎上。
            TeardownVariableEvents();

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
