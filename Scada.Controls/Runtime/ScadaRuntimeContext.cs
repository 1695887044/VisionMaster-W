using System;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 运行态上下文：把"只有运行时才存在、且整幅画面只有一份"的那几样东西装进一只手提箱，
    /// 由宿主装配（<c>ScadaRuntimeHost.StartAlarms</c>）、由画布转发（<c>ScadaCanvas.CreateContainer</c>）、
    /// 由图元消费（<see cref="AlarmBannerElement"/> 是第一家）。
    ///
    /// 为什么要有这个类，而不是给图元直接加两个属性
    /// ---------
    /// ① <b>引擎与节拍同生共死</b>：它们在宿主的 <c>StartAlarms</c> 里一起建、在 <c>TeardownAlarms</c>
    ///    里一起收。拆成两个属性注入，消费方就得处理"一个来了另一个还没来""一个走了另一个还挂着"
    ///    这些组合状态；打包成一个对象之后，"有"就是都有、"没"就是都没，消费侧只剩一个分支。
    /// ② <b>注入点只开一次</b>：今天只有报警条要它，明天趋势图、走马灯、状态栏都要
    ///    （<see cref="ScadaBeatSource"/> 的类注释已经点了这几家的名）。每来一个消费者就改一次
    ///    <see cref="ScadaElementBase"/> / <see cref="ScadaCanvas"/> 的签名，是迟早要漏一处的改法。
    ///
    /// 为什么住在控件库，而不是领域层或宿主
    /// ---------
    /// 它攥着 <see cref="ScadaBeatSource"/>（里头是个 <c>DispatcherTimer</c>），领域层不许碰 WPF
    /// （见 <see cref="ScadaAlarmEngine"/> 类注释"引擎不起定时器"）；它也不属于宿主——宿主只负责
    /// 装配，图元才是消费者。控件库正好是"两边的类型都认识"的那一层，依赖方向也不翻。
    ///
    /// 线程
    /// ---------
    /// <see cref="Beat"/> 跑在宿主给的那个 UI 线程上，订阅方可以直接改依赖属性；
    /// <see cref="Alarms"/> 的三个事件则可能在变量轮询线程上抛（见 <see cref="ScadaAlarmEngine"/>
    /// 的线程约定）——<b>切线程是订阅方的责任</b>。
    ///
    /// 为什么 <see cref="Writer"/> 是可选的，而前两样不是
    /// ---------
    /// 引擎与节拍是"运行就有"——没有它们，报警条这类图元根本没法活，所以构造时缺一个就抛。
    /// 写通道不同：它只在<b>操作员能改值</b>这一种场景里被用到（今天只有 I/O 域的输入框），
    /// 而"这次运行没人会改值"是完全正常的状态（纯监视画面、断言工程里手搭的画布）。
    /// 强行要求人人给一个，调用方就得为"我这次不写值"编一个空实现——那是把可选性藏起来，
    /// 而不是消灭它。所以这里明写"可为 null"，消费侧照 null 判断，语义摆在签名上。
    ///
    /// 为什么 <see cref="AccessPolicy"/> 参数可选、属性却永不为 null
    /// ---------
    /// 权限与写通道是两种可选性。写通道的"没有"是一种真实状态（本次运行就是不写值），
    /// 消费侧必须能看见它；而权限的"没接线"不是一个状态——<b>"此刻是谁"这个问题永远有答案</b>，
    /// 没接线时的答案就是"未登录（操作员）"（见 <see cref="IScadaAccessPolicy.CurrentRole"/>）。
    /// 让消费侧去处理一个永远不存在的 null 分支，只会逼出"忘了判 null 就直接用"的隐患；
    /// 所以这里兜底成 <see cref="DefaultScadaAccessPolicy.Instance"/>，把"没接线"折叠进语义里。
    /// </summary>
    public sealed class ScadaRuntimeContext
    {
        /// <param name="alarms">本轮的报警引擎</param>
        /// <param name="beat">本轮的全画面统一节拍源</param>
        /// <param name="writer">本轮的回写通道；纯监视画面可以不给（见类注释）</param>
        /// <param name="accessPolicy">本轮的权限出口；不给则按"未登录（操作员）"算（见类注释）</param>
        public ScadaRuntimeContext(
            ScadaAlarmEngine alarms,
            ScadaBeatSource beat,
            IScadaValueWriter? writer = null,
            IScadaAccessPolicy? accessPolicy = null)
        {
            Alarms = alarms ?? throw new ArgumentNullException(nameof(alarms));
            Beat = beat ?? throw new ArgumentNullException(nameof(beat));
            Writer = writer;
            AccessPolicy = accessPolicy ?? DefaultScadaAccessPolicy.Instance;
        }

        /// <summary>
        /// 本轮的报警引擎。
        ///
        /// 消费方只该用它的<b>读面</b>：<see cref="ScadaAlarmEngine.ActiveAlarms"/>（已按严重度排好，
        /// 不要再自排）、<see cref="ScadaAlarmEngine.UnacknowledgedCount"/>、
        /// <see cref="ScadaAlarmEngine.HighestActiveSeverity"/>，以及三个变更事件。
        /// <c>Attach</c>/<c>Tick</c>/<c>Acknowledge</c> 归宿主与操作员面板，图元不该碰。
        /// </summary>
        public ScadaAlarmEngine Alarms { get; }

        /// <summary>
        /// 本轮的全画面统一节拍（闪烁相位的唯一来源）。
        /// 所有会闪的图元都读同一份 <see cref="ScadaBeatSource.Elapsed"/>，相位天然对齐。
        /// </summary>
        public ScadaBeatSource Beat { get; }

        /// <summary>
        /// 本轮的回写通道：图元输入框把操作员敲的文本送回工程变量。
        ///
        /// <b>可为 null</b>（见类注释）——为 null 时"改值"这条路整体不可用，
        /// 图元该做的是把输入框置为只读/不进入编辑态，而不是自己编一个"写入失败"给操作员看。
        /// 这与数据泵的方向刚好相反：读由 <c>ScadaRuntimeBinder</c> 走值源，写只能走这里。
        ///
        /// 消费方只管调用，不要缓存——本轮运行结束（<c>TeardownAlarms</c> 把
        /// <c>RuntimeContext</c> 置空）之后，手里那个引用就该一起丢掉。
        /// </summary>
        public IScadaValueWriter? Writer { get; }

        /// <summary>
        /// 本轮的权限出口：图元在"改现场量"之前先问一句"此刻的角色够不够格"。
        ///
        /// 为什么图元要自己问，而不是等写通道拒绝
        /// ---------
        /// 写通道（<see cref="IScadaValueWriter"/>）只认"变量收不收下这个值"，它不认识角色；
        /// 权限判定在领域层的会话出口上（<c>ScadaRuntime.RaiseElementEvent</c>），
        /// 那里卡的是<b>事件钩子</b>，不是写入本身。所以"写前先查"这件事只能由发起写入的图元做：
        /// 让操作员点一下就先知道"我没这个权限"，而不是敲完数、写完变量、才发现顺带的动作被拦了。
        ///
        /// <b>永不为 null</b>（见类注释）——没接线时是 <see cref="DefaultScadaAccessPolicy.Instance"/>，
        /// 口径与 <c>ScadaRuntime</c> 完全一致：未登录 = 操作员，<c>RequiredRole</c> 为 null = 不限制。
        /// </summary>
        public IScadaAccessPolicy AccessPolicy { get; }
    }
}
