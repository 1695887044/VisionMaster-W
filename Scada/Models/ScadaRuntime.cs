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
    /// - <b>不做画面导航</b>：没有任何 UI 会去调它，先不预留 <c>Navigate()</c>。
    ///   等"运行态切页"真成需求时再加，那时才知道要按 Id 还是按名、要不要 Unloaded。
    /// - <b>不测耗时</b>：真正的"加载完成"发生在 WPF 渲染之后，领域层拿不到那一帧。
    ///   所以本类只发"该显示哪一页"这个事实，耗时由宿主自己掐表（见 <c>ScadaRuntimeLauncher</c>）。
    /// - <b>线程</b>：只在 UI 线程上用，不做加锁。加锁在这里换不来任何东西，只会把
    ///   "从后台线程 Start"这个真 bug 掩盖成偶发行为。
    /// </summary>
    public class ScadaRuntime : BindableBase
    {
        private readonly ScadaDocument _document;

        private ScadaPage? _currentPage;
        private bool _running;

        /// <param name="document">要运行的那份文档（<b>内存里的当前方案</b>，不是磁盘上的旧副本——
        /// 用户刚改完就点运行，看到的必须是他刚改的东西）</param>
        public ScadaRuntime(ScadaDocument document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
        }

        /// <summary>正在运行的文档（运行期间不换；换方案由宿主 <see cref="Stop"/> 后重建会话）</summary>
        public ScadaDocument Document => _document;

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
        /// </summary>
        public int RaiseElementEvent(ScadaElement? element, ScadaEventType eventType)
        {
            if (!_running || element == null)
                return 0;

            if (CurrentPage == null || !ReferenceEquals(CurrentPage.FindElement(element.ElementId), element))
                return 0;

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

            return RaiseHooks(page.EventHooks, eventType, hook => PageEventRaised?.Invoke(page, hook));
        }

        /// <summary>
        /// 图元与画面共用的那一段：扫钩子、判命中、逐条广播，返回命中条数。
        /// 同一次触发命中多条时按集合顺序广播（口径见 <see cref="ScadaEventHook"/>）。
        /// </summary>
        private int RaiseHooks(IEnumerable<ScadaEventHook> hooks, ScadaEventType eventType, Action<ScadaEventHook> broadcast)
        {
            int hits = 0;

            foreach (var hook in hooks)
            {
                if (hook == null || hook.Event != eventType || hook.Actions.Count == 0)
                    continue;

                broadcast(hook);
                hits++;
            }

            return hits;
        }

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
        /// 不发 Unloaded：接上画面导航（S8）之前根本没有"离开某一页"的时刻，发了也没人接。
        /// </summary>
        public void Stop()
        {
            if (!_running && CurrentPage == null) return;

            _running = false;
            CurrentPage = null;
        }
    }
}
