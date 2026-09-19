using System;
using System.ComponentModel;
using System.Diagnostics;
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
    ///   运行中：变量变化 → 数据泵合并刷帧 → 写图元控件（不碰模型）
    ///   关窗口 / 切方案 / Stop()  →  数据泵摘表（退订阅 + 刷回设计值）→ 退订会话与窗口的全部事件 → session.Stop() → 字段清空
    /// </code>
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
        /// 软件级配置（AppConfig.json）。这里只用它一件事：读"运行窗口该长什么样"。
        /// 每次 <see cref="Start"/> 现读 <c>Current</c>，所以「系统 → 运行窗口设置」改完立刻生效，
        /// 不用重启软件——设置弹窗自己负责写盘（见 ScadaRunWindowSettingsViewModel）。
        /// </summary>
        private readonly AppSettingsService _appSettings;

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
        /// 本轮的运行态数据泵（没运行 / 还没到首帧时是 null）。
        /// 生命周期严格跟着窗口：首帧后建表、窗口关闭时摘表，绝不跨轮次复用——
        /// 它内部攥着控件引用与变量订阅，跨轮复用就是"上一页的图元还在收这一页的值"。
        /// </summary>
        private ScadaRuntimeBinder? _binder;

        public ScadaRuntimeHost(
            ILogService log,
            IUserNotifier notifier,
            IWorkspaceManager workspace,
            IScadaActionDispatcher dispatcher,
            IScadaValueSource valueSource,
            AppSettingsService appSettings)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _valueSource = valueSource ?? throw new ArgumentNullException(nameof(valueSource));
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

            var session = new ScadaRuntime(document);
            var window = new ScadaRuntimeWindow(session);

            // 形态必须在 Show() 之前定死：WindowStyle/WindowState 这类属性在窗口显示之后再改，
            // WPF 会重建非客户区，表现为闪一下、尺寸跳一格（依附形态下更明显）。
            var mode = _appSettings.Current.RunWindowMode;
            window.ApplyRunWindowMode(mode);

            if (mode == ScadaRunWindowMode.AttachedToMainWindow)
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
            window.ContentRendered += OnFirstFrameRendered;
            window.Closed += OnWindowClosed;
            // 在窗口根上接住所有图元冒泡上来的组态事件：一处接线，中间的可视树层级不用管
            window.AddHandler(ScadaElementBase.ScadaEventEvent, _onScadaEvent);

            _session = session;
            _window = window;

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
            _session?.Stop();
            _session = null;
        }

        // ---- 会话侧 ----

        private void OnPageLoaded(ScadaPage page) => _pendingLoadedPage = page;

        /// <summary>
        /// 会话判完"哪条钩子命中"，这里把那一串动作交给分发器按序执行。
        /// 图元名一并带过去，日志里才能看出是哪个按钮被点了（分发器不认识模型，也不该认识）。
        /// </summary>
        private void OnElementEventRaised(ScadaElement element, ScadaEventHook hook)
            => _dispatcher.Dispatch(hook, element.Name);

        /// <summary>画面级钩子（Loaded）：与图元走同一个分发器，只是报名字的是画面</summary>
        private void OnPageEventRaised(ScadaPage page, ScadaEventHook hook)
            => _dispatcher.Dispatch(hook, page.Name);

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

        // ---- 窗口侧 ----

        /// <summary>
        /// 首帧真的画完了：记一行运行观测（页面 + 耗时），再把这一页的 Loaded 钩子交给会话判定、
        /// 由分发器执行。只会在窗口生命周期内来一次（本窗口不会隐藏后再显示，退出就是关掉）。
        /// </summary>
        private void OnFirstFrameRendered(object? sender, EventArgs e)
        {
            double ms = _watch?.Elapsed.TotalMilliseconds ?? 0;
            _watch = null;

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

            var session = _session;
            if (session != null)
            {
                // 先摘订阅再 Stop()：Stop() 之后会话就"不运行"了，理论上不会再广播事件，
                // 但把顺序写成"先断线后关灯"，将来谁改了 Stop() 的内部实现也不会漏出一次幽灵触发。
                session.PageLoaded -= OnPageLoaded;
                session.ElementEventRaised -= OnElementEventRaised;
                session.PageEventRaised -= OnPageEventRaised;
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
