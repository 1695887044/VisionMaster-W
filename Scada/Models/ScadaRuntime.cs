using System;
using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 组态画面的<b>运行态会话</b>：一次"从当前方案跑起来"的过程。
    ///
    /// 它解决的是什么问题
    /// ---------
    /// 编辑器（<c>ScadaEditorViewModel</c>）管的是"怎么改这份文档"，运行态管的是
    /// "这份文档此刻该显示哪一页"。两件事的写入面完全不同：前者要撤销、要脏标记、
    /// 要图层与选中；后者只读模型、只关心画面切换与事件触发。混在一个类里，
    /// 迟早出现"运行中用户改了画布"这类两头都不认的状态。
    ///
    /// 为什么住在领域层（本层不依赖 WPF）
    /// ---------
    /// 1) 选哪一页起步、Loaded 该不该发、运行中能不能再 Start——这些是**产品规则**，
    ///    不是渲染细节。规则要能被无界面的检查（ScadaChecks）钉住，否则每次改动都得开界面点一遍。
    /// 2) 将来接报表、无人值守模式、多屏运行时，同一套规则要能复用，而窗口壳子不能跟着复制三份。
    ///
    /// 边界（本阶段刻意不做的事）
    /// ---------
    /// - <b>只读文档</b>：不改 <see cref="ScadaDocument"/> / <see cref="ScadaPage"/> 的任何东西。
    ///   运行态改组态是另一条产品决策（要不要允许、改了落不落盘），没定之前不留口子。
    /// - <b>导航只在会话内走</b>：<see cref="Navigate(Guid, string?, out string?)"/> 只认<b>本方案里</b>的画面
    ///   （Id 优先、名字兜底，与变量寻址同一口径），页栈只记"来过哪几页"，不回写文档。
    ///   运行态改组态（比如"跳转时顺手删掉某页"）仍然不做。
    /// - <b>不测耗时</b>：真正的"加载完成"发生在 WPF 渲染之后，领域层拿不到那一帧。
    ///   所以本类只发"该显示哪一页"这个事实，耗时由宿主自己掐表（见 <c>ScadaRuntimeLauncher</c>）。
    /// - <b>线程</b>：只在 UI 线程上用，不做加锁。加锁在这里换不来任何东西，只会把
    ///   "从后台线程 Start"这个真 bug 掩盖成偶发行为。
    /// </summary>
    public class ScadaRuntime : BindableBase, IScadaNavigator
    {
        /// <summary>
        /// 页栈深度上限。超过就从<b>栈底</b>丢最老的一页。
        ///
        /// 为什么要有上限：运行态可以来回跳（A→B→A→B…），每跳一次压一页，没人回退就是无限增长。
        /// 这是唯一一处会随操作时长增长的集合，而组态软件是要连开几个月的。
        /// 32 层足够覆盖任何真实的"菜单→列表→详情→参数"层级，再深用户自己也找不到北。
        /// </summary>
        private const int MaxPageStackDepth = 32;

        private readonly ScadaDocument _document;

        /// <summary>
        /// 运行态权限出口（S12）。<b>永不为 null</b>：没接线时落到
        /// <see cref="DefaultScadaAccessPolicy.Instance"/>（未登录 = 操作员），
        /// 这样判定路径只有一条，不存在"某个入口忘了判权限"的分支。
        /// </summary>
        private readonly IScadaAccessPolicy _accessPolicy;

        /// <summary>来过的画面（栈底最老、栈顶最近一次离开的那一页）；只被 <see cref="GoBack"/> 读</summary>
        private readonly List<ScadaPage> _pageStack = new();

        private ScadaPage? _currentPage;
        private bool _running;

        /// <param name="document">要运行的那份文档（<b>内存里的当前方案</b>，不是磁盘上的旧副本——
        /// 用户刚改完就点运行，看到的必须是他刚改的东西）</param>
        /// <param name="accessPolicy">
        /// 权限出口（S12）。<c>null</c> = 本宿主没有用户系统，按"未登录 = 操作员"处理。
        ///
        /// 为什么做成可选参数而不是必填：会话的绝大多数用途（断言、设计态预览、将来不接用户系统的
        /// 宿主）与"谁在操作"无关，逼它们各造一个策略只会逼出"随便传个允许一切的假策略"，
        /// 那才是真的把权限架空了。<b>不传的默认值是"操作员"，不是"管理员"</b>——
        /// 漏接线的表现应该是"权限没放开"，而不是"权限失效了"。
        /// </param>
        public ScadaRuntime(ScadaDocument document, IScadaAccessPolicy? accessPolicy = null)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _accessPolicy = accessPolicy ?? DefaultScadaAccessPolicy.Instance;
        }

        /// <summary>正在运行的文档（运行期间不换；换方案由宿主 <see cref="Stop"/> 后重建会话）</summary>
        public ScadaDocument Document => _document;

        /// <summary>
        /// 本会话用的权限出口，<b>永不为 null</b>（没接线时是 <see cref="DefaultScadaAccessPolicy.Instance"/>）。
        ///
        /// 为什么把它读出来（本类其余部分只读文档）
        /// ---------
        /// 运行窗口底条要显示"此刻是谁、哪一档"——那正是本会话判定权限时用的那一份答案，
        /// 由本会话转手给出，读数和实际生效的判定就<b>不可能</b>来自两份不同的策略。
        /// 反过来若让窗口自己去容器里再取一次，将来真出现两份实例时，
        /// 底条会理直气壮地显示"管理员"，而按钮照样按不动。
        ///
        /// 只读：登录/登出刻意不在本类上（见 <see cref="IScadaAccessPolicy"/> 的契约③），
        /// 组态运行窗口只显示、不提供登录入口。
        /// </summary>
        public IScadaAccessPolicy AccessPolicy => _accessPolicy;

        /// <summary>会话是否活着（决定"停止运行"该不该置灰，也挡住连点运行）</summary>
        public bool IsRunning => _running;

        /// <summary>此刻显示的那一页；未运行或方案里一页都没有时为 null</summary>
        public ScadaPage? CurrentPage
        {
            get => _currentPage;
            private set => SetProperty(ref _currentPage, value);
        }

        /// <summary>
        /// 会话决定把某一页显示出来（<b>无条件发一次</b>，与配没配事件无关）。
        ///
        /// 注意这里的语义是"该显示这一页了"，不是"这一页已经画完了"——领域层拿不到渲染那一帧。
        /// 宿主接这条只为了存下"等着跑 Loaded 钩子的是哪一页"，等首帧画完再调
        /// <see cref="RaisePageEvent"/>，所以 S4 那条"日志出现在画面真画出来之后"的观感不变。
        ///
        /// 为什么不按"有没有配 Loaded 钩子"来筛：那是动作层面的判断，混进来会让这条事实事件
        /// 变成"只有配了事件的画面才被通知"，将来任何想看"当前显示哪一页"的订阅方（状态栏、
        /// 多屏、报表）都得重新定义一遍。发事实，判动作，两件事分开。
        /// </summary>
        public event Action<ScadaPage>? PageLoaded;

        /// <summary>
        /// 图元上的某个事件钩子被命中（操作员按下/松开了一个配了事件的图元）。
        ///
        /// 为什么由运行态会话来判定"命没命中"，而不是让控件自己执行动作：
        /// 执行要回答三个问题——这是不是<b>当前画面</b>上的图元、这个事件在不在它的钩子表里、
        /// 该按什么顺序跑哪几条动作。这三个都是产品规则，留在领域层才能被 ScadaChecks 钉住；
        /// 而且将来 S12 的权限校验必须卡在这一个出口上（界面隐藏按钮挡不住直接调接口）。
        ///
        /// 一次触发可能有多个钩子（同一事件被手工配了两条），所以是"每个钩子回调一次"，
        /// 宿主在回调里按顺序执行 <see cref="ScadaEventHook.Actions"/>。
        /// </summary>
        public event Action<ScadaElement, ScadaEventHook>? ElementEventRaised;

        /// <summary>
        /// 一次图元操作<b>被权限闸门拦下</b>（S12）：谁想操作哪个图元、为什么不行。
        /// 参数是"被点的那一个图元"和一句可以直接念给操作员听的原因
        /// （"需要「工程师」权限，当前是「操作员」"）。
        ///
        /// 为什么是一条<b>事实事件</b>而不是在这里弹窗、写文件
        /// ---------
        /// 与会话里其他事件同一条分工：领域层只回答"发生了什么"，"怎么让人知道"
        /// （气泡、状态栏、蜂鸣、写审计文件）全是宿主的事。领域层不认识通知服务，
        /// 也不该认识——本层不依赖 WPF。
        ///
        /// 为什么需要它、而不是安静返回 0 了事
        /// ---------
        /// "点了没反应"是现场最招人烦、也最难自查的一类问题：操作员分不清是按钮没配动作、
        /// 软件卡住了、还是自己权限不够。这一条事件让宿主有机会把最后那种情况说清楚。
        ///
        /// 只在<b>这次事件真的配过钩子</b>时才会发（见 <see cref="RaiseElementEvent"/>）：
        /// 没配过钩子的图元点了本来就没反应，再弹一句"没权限"只会让人以为事件配错了。
        /// </summary>
        public event Action<ScadaElement, string>? ElementAccessDenied;

        /// <summary>
        /// 画面上的某个事件钩子被命中（本阶段只有 <see cref="ScadaEventType.Loaded"/>，
        /// 由宿主在首帧画完后调 <see cref="RaisePageEvent"/> 上报）。
        /// 存在的理由与 <see cref="ElementEventRaised"/> 完全相同：判定留在领域层，执行交给分发器。
        /// </summary>
        public event Action<ScadaPage, ScadaEventHook>? PageEventRaised;

        /// <summary>
        /// 上报一个图元事件（WPF 侧的画布在运行态把鼠标动作转成这句话）。
        /// 返回<b>命中并广播出去的钩子条数</b>，0 表示什么都没发生。
        ///
        /// 三道闸门，缺一不可：
        /// ① 会话没在跑就不算：设计器里点图元只是选中，绝不能触发动作（这是 S4 定的
        ///    "事件钩子是运行态语义"，界面上再小心也不如在这里挡一次）。
        /// ② 图元必须还是<b>当前画面</b>上那一个（按引用比对，不是按 Id）。
        ///    将来接上画面导航之后，被切走的画面上的控件仍可能在可视树里活着并收到鼠标事件，
        ///    那时"按 Id 查当前画面"会落空、而"拿手里这个对象直接执行"会执行到已离开的画面上——
        ///    现在就把口径定死，接导航时不必再改这里的逻辑。
        /// ③ 事件必须在它的钩子表里配过：没配过就是 0 次回调，不产生任何日志。
        ///    这条决定了热路径的成本——画面上一百个图元、九十九个没配事件，
        ///    每次按下只需扫一遍它自己那条很短的集合，不必问注册表也不必遍历画面。
        ///
        /// S12 追加的第四道：<b>权限</b>。它是本方法唯一一处会<b>拒绝</b>而不是"没命中"的闸门，
        /// 所以单独说明：前三条判的是"这条事件该不该响"，它判的是"这个人配不配让它响"。
        /// 卡在这里而不是界面上的理由见 <see cref="IScadaAccessPolicy"/>；
        /// 判定口径（未登录算操作员、高角色含低角色、非法值一律拒绝）只有一份实现，
        /// 在 <see cref="ScadaRoleExtensions.Allows"/>。
        /// </summary>
        public int RaiseElementEvent(ScadaElement? element, ScadaEventType eventType)
        {
            if (!_running || element == null)
                return 0;

            if (CurrentPage == null || !ReferenceEquals(CurrentPage.FindElement(element.ElementId), element))
                return 0;

            // ④ 权限闸门（S12）。顺序上排在结构判断之后、扫钩子之前：
            // 前三条都是"这个对象到底算不算数"的廉价判断，先做掉才不会为一次无效点击去问策略；
            // 而排在扫钩子之前，是因为被拦下的这一次操作<b>一条动作都不许跑</b>——
            // 让 RaiseHooks 先跑一遍再判权限，就成了"先干活再查证件"。
            //
            // 只有 RequiredRole 配过（非 null）才去问策略：绝大多数图元没配过，
            // 热路径上不该为它们多做一次接口调用。
            if (element.RequiredRole is { } required && !_accessPolicy.CanOperate(required, out var reason))
            {
                // 出声的条件是"这次事件本来真有条动作要跑"：没配过钩子的图元点了本来就没反应，
                // 再弹一句"没权限"只会让人以为事件配错了（理由详见 ElementAccessDenied 的注释）。
                if (HasHooks(element.EventHooks, eventType))
                    ElementAccessDenied?.Invoke(element, reason ?? "当前角色没有操作权限");

                return 0;
            }

            return RaiseHooks(element.EventHooks, eventType, hook => ElementEventRaised?.Invoke(element, hook));
        }

        /// <summary>
        /// 上报一个<b>画面级</b>事件（宿主在首帧画完后把 Loaded 报进来）。
        /// 三道闸门与 <see cref="RaiseElementEvent"/> 同一套，只是第②道问的是"这还是不是当前那一页"。
        ///
        /// 为什么要单开一个方法，而不是让宿主自己去遍历 <c>page.EventHooks</c>：
        /// 那样"没配钩子就不该有动作""配了空动作表等同于没配"这两条规则就漏到了 WPF 侧，
        /// 而它们和图元那三条是同一条规则——规则的副本数就是将来 bug 的分布数。
        /// </summary>
        public int RaisePageEvent(ScadaPage? page, ScadaEventType eventType)
        {
            if (!_running || page == null)
                return 0;

            if (!ReferenceEquals(CurrentPage, page))
                return 0;

            return BroadcastPageHooks(page, eventType);
        }

        /// <summary>
        /// 画面钩子的广播本体（<b>不含闸门</b>）。闸门在 <see cref="RaisePageEvent"/> 里；
        /// 唯一绕过它的是 <see cref="Stop"/> 与 <see cref="SwitchTo"/>——那两处的调用条件
        /// 自己已经判完了（"这一页确实要走了"），再走一遍闸门只会把"离开的是旧页"这条事实挡掉。
        /// </summary>
        private int BroadcastPageHooks(ScadaPage page, ScadaEventType eventType)
            => RaiseHooks(page.EventHooks, eventType, hook => PageEventRaised?.Invoke(page, hook));

        /// <summary>
        /// 图元与画面共用的那一段：扫钩子、判命中、逐条广播，返回命中条数。
        /// 同一次触发命中多条时按集合顺序广播（口径见 <see cref="ScadaEventHook"/>）。
        ///
        /// 命中口径（"非空 + 事件对上 + 动作表非空"）不在这里实现，见
        /// <see cref="ScadaHookMatcher"/>——变量事件引擎要在后台线程上问同一个问题，
        /// 两处各写一份早晚分叉。
        /// </summary>
        private int RaiseHooks(IEnumerable<ScadaEventHook> hooks, ScadaEventType eventType, Action<ScadaEventHook> broadcast)
            => ScadaHookMatcher.Raise(hooks, eventType, broadcast);

        /// <summary>
        /// 这个事件在这张钩子表里<b>配过没有</b>——判定见 <see cref="ScadaHookMatcher.Has"/>。
        ///
        /// 为什么单开一个方法、而不是"权限被拦就一律出声"：被拦下的图元要回答的是
        /// "本来该不该有反应"，这跟"点下去有没有反应"是同一个问题，条件必须一模一样。
        /// 两处各写一份判断，早晚出现"没配钩子的图元也弹没权限"这种噪音，
        /// 而噪音的代价是操作员再也不看那些提示了。
        /// </summary>
        private static bool HasHooks(IEnumerable<ScadaEventHook> hooks, ScadaEventType eventType)
            => ScadaHookMatcher.Has(hooks, eventType);

        /// <summary>
        /// 启动会话：挑出起步画面、置为运行中、发一次 <see cref="PageLoaded"/>。
        ///
        /// 返回 false 的两种情况（都不算错，宿主据此决定要不要弹窗口）：
        /// - 已经在跑：连点"运行"不该把同一个画面重新加载一遍，那会让"Loaded 只触发一次"
        ///   这条承诺变成假的。
        /// - 方案里一页都没有：<see cref="ScadaDocument.ResolveStartupPage"/> 返回 null，
        ///   弹一个空白全屏窗口比不弹更让人慌。
        /// </summary>
        public bool Start()
        {
            if (_running) return false;

            var page = _document.ResolveStartupPage();
            if (page == null) return false;

            _running = true;
            CurrentPage = page;

            // 事件在 CurrentPage 赋值之后发：订阅方（宿主）往往要按"已经显示出来了"来记日志，
            // 先赋值后通知，订阅方读 CurrentPage 才拿得到这一页而不是上一页。
            PageLoaded?.Invoke(page);

            return true;
        }

        /// <summary>
        /// 结束会话。可重复调用（关窗口、切方案、关编辑器三条路都会走到这里，不该互相甩异常）。
        ///
        /// 三步的顺序是有讲究的：
        /// 1) 先关运行开关：卸载事件上若配了"切换画面"，那条动作会被这里挡掉（会话都要停了，
        ///    再切一页出去只会留下一个没人管的窗口），同时挡掉一切重入。
        /// 2) 再补发当前页的 <see cref="ScadaEventType.Unloaded"/>：用户在卸载事件上配的收尾动作
        ///    （写变量、记日志）得在会话还活着时跑完，否则"停止运行"会静默丢掉最后一件事。
        /// 3) 最后才清 <see cref="CurrentPage"/>：订阅方读它时看到的是"已经没有了"，
        ///    不会把刚要离开的那一页当成还在显示。
        /// </summary>
        public void Stop()
        {
            if (!_running && CurrentPage == null) return;

            var leaving = _currentPage;

            _running = false;

            if (leaving != null)
                BroadcastPageHooks(leaving, ScadaEventType.Unloaded);

            CurrentPage = null;
            _pageStack.Clear();
        }

        // ── 画面导航（S8）──────────────────────────────────────────────────────────
        //
        // 运行态切页要回答三个问题，答案都在这一段里：
        // ① 目标怎么找：Id 优先、名字兜底（与变量寻址同一口径），且只认本方案里的画面。
        // ② 事件怎么发：旧页 Unloaded → 换 CurrentPage → 新页 PageLoaded，顺序固定，各发一次。
        // ③ 重复怎么挡：目标就是当前页时什么都不做——"连点两下不能触发两次 Loaded"。

        /// <summary>还能不能回退（页栈非空）</summary>
        public bool CanGoBack => _pageStack.Count > 0;

        /// <summary>页栈里压着几页（状态栏显示、断言核对用）</summary>
        public int PageStackDepth => _pageStack.Count;

        /// <summary>
        /// 按"Id 优先、名字兜底"让运行态显示某一页（<see cref="IScadaNavigator"/> 的实现）。
        ///
        /// 寻址交给 <see cref="ScadaDocument"/>，本类不自己遍历画面——按名找画面的忽略大小写规则
        /// 只该有一份实现（见 <see cref="ScadaDocument.FindPageByName"/>）。
        ///
        /// 返回 true = 目标状态已达成（含"本来就在显示这一页"，那时不重发 Loaded、不压栈）；
        /// 返回 false 时 <paramref name="reason"/> 是一句能直接写进日志的原因。
        /// 刻意不返回"这次换页发生了没有"：分发器要的是"操作员的意图达成了吗"，
        /// 而"点了两下"和"画面被删了"在日志里必须是两句话，不能都叫"失败"。
        /// </summary>
        public bool Navigate(Guid pageId, string? fallbackName, out string? reason)
        {
            var page = _document.FindPage(pageId) ?? _document.FindPageByName(fallbackName);

            if (page == null)
            {
                reason = string.IsNullOrWhiteSpace(fallbackName)
                    ? "还没选目标画面"
                    : $"方案里没有叫「{fallbackName.Trim()}」的画面";
                return false;
            }

            if (!_running)
            {
                reason = "运行已经结束";
                return false;
            }

            reason = null;
            return Navigate(page);
        }

        /// <summary>
        /// 切到指定的那一页（调用方已经拿到画面对象时走这条）。
        ///
        /// 四道判断，任一不过就返回 false 且<b>不压栈</b>——把"没切成"和"切成了"分清楚，
        /// 是"连点两下只触发一次 Loaded"这条承诺的实现基础：
        /// - 会话没在跑：设计态点图元、窗口已经关掉之后的迟到调用，都不该换页。
        /// - 目标为 null：调用方没找到画面，不必替它猜。
        /// - 目标不在本方案里：按引用比对（不是按 Id）——Id 相同但不是文档里那一份对象的，
        ///   是个野生副本，切过去它不会被画布渲染，只会留下一个"看着像成功了"的假象。
        /// - 目标就是当前页：<b>返回 true 但什么都不做</b>。这是"目标状态"语义的落点：
        ///   要的状态已经成立，只是不能再发一次 Loaded、也不能再压一层栈。
        /// </summary>
        public bool Navigate(ScadaPage? page)
        {
            if (!_running || page == null) return false;
            if (!ReferenceEquals(_document.FindPage(page.PageId), page)) return false;
            if (ReferenceEquals(page, _currentPage)) return true;

            PushPage(_currentPage);
            return SwitchTo(page);
        }

        /// <summary>
        /// 回退到上一页（页栈弹一层）。栈空返回 false。
        ///
        /// 弹出的那一页可能已经不在文档里了（独立窗口模式下运行中也能改方案），
        /// 这种就丢掉继续往下找；找到底都没有就返回 false——"回退按钮点了没反应"
        /// 比"回退到一个已经不存在的画面"要好排查得多。
        /// </summary>
        public bool GoBack()
        {
            if (!_running) return false;

            while (_pageStack.Count > 0)
            {
                var page = PopPage();
                if (page == null || ReferenceEquals(page, _currentPage)) continue;
                if (!ReferenceEquals(_document.FindPage(page.PageId), page)) continue;

                return SwitchTo(page);
            }

            return false;
        }

        /// <summary>
        /// 换页的唯一落点：旧页 <see cref="ScadaEventType.Unloaded"/> → 换 <see cref="CurrentPage"/>
        /// → 新页 <see cref="PageLoaded"/>。
        ///
        /// 为什么顺序必须是"先卸载再加载"，不能反过来：两条钩子都可能写同一个变量
        /// （旧页"离开时复位"、新页"进入时置位"），顺序反了最终值就是错的，而且只在配了两条
        /// 钩子的工程上复现——那种 bug 靠现场是查不出来的。
        ///
        /// 为什么卸载发在 CurrentPage 换掉<b>之前</b>：卸载钩子里的动作常常要读"我现在在哪一页"
        /// （记日志、判断要不要提示），那时它应该还能读到旧页。
        /// 而 <see cref="PageLoaded"/> 发在换掉<b>之后</b>，理由与 <see cref="Start"/> 相同。
        /// </summary>
        private bool SwitchTo(ScadaPage page)
        {
            var leaving = _currentPage;

            if (leaving != null)
                BroadcastPageHooks(leaving, ScadaEventType.Unloaded);

            CurrentPage = page;
            PageLoaded?.Invoke(page);

            return true;
        }

        /// <summary>压栈；超过上限从栈底丢最老的一页（口径见 <see cref="MaxPageStackDepth"/>）</summary>
        private void PushPage(ScadaPage? page)
        {
            if (page == null) return;

            _pageStack.Add(page);

            if (_pageStack.Count > MaxPageStackDepth)
                _pageStack.RemoveAt(0);
        }

        private ScadaPage? PopPage()
        {
            if (_pageStack.Count == 0) return null;

            int last = _pageStack.Count - 1;
            var page = _pageStack[last];
            _pageStack.RemoveAt(last);
            return page;
        }
    }
}
