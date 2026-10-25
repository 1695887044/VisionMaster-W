using System;
using System.Collections.Generic;
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
        /// <param name="axisSource">本轮的运动轴快照源；纯画面（不跑轴面板）可以不给</param>
        /// <param name="axisCommands">本轮的运动轴命令出口；纯监视画面可以不给</param>
        /// <param name="confirmations">本轮的操作二次确认出口；不给则轴控台的示教/定位在需要确认时拒绝执行</param>
        /// <param name="values">本轮的值通道（按变量名解析句柄）；不给则数据表格这类"按名字取数"的图元显示未接入</param>
        /// <param name="priorAlarms">
        /// 本轮开始前从方案目录读回的历史报警记录（最新在前）。历史面板把这份与
        /// <see cref="Alarms"/> 的本轮记录合并显示；历史记录只读，不许点确认。
        /// </param>
        /// <param name="history">
        /// 本轮的历史数据源（数据表格 <c>Source=History</c> 用）；纯监视画面可以不给。
        /// </param>
        /// <param name="stateLists">
        /// 方案级文本列表（ComboBox 用）；设计期由画布的 <c>StateListResolver</c> 提供，
        /// 运行态走这一份，断言宿主两者都可给。
        /// </param>
        public ScadaRuntimeContext(
            ScadaAlarmEngine alarms,
            ScadaBeatSource beat,
            IScadaValueWriter? writer = null,
            IScadaAccessPolicy? accessPolicy = null,
            IScadaAxisSource? axisSource = null,
            IScadaAxisCommandSink? axisCommands = null,
            IScadaConfirmationService? confirmations = null,
            IScadaValueSource? values = null,
            IReadOnlyList<ScadaAlarmRecord>? priorAlarms = null,
            IScadaHistorySource? history = null,
            IReadOnlyList<ScadaStateList>? stateLists = null,
            IScadaSubPageHost? subPages = null)
        {
            Alarms = alarms ?? throw new ArgumentNullException(nameof(alarms));
            Beat = beat ?? throw new ArgumentNullException(nameof(beat));
            Writer = writer;
            AccessPolicy = accessPolicy ?? DefaultScadaAccessPolicy.Instance;
            AxisSource = axisSource;
            AxisCommands = axisCommands;
            Confirmations = confirmations;
            Values = values;
            PriorAlarms = priorAlarms ?? Array.Empty<ScadaAlarmRecord>();
            History = history;
            StateLists = stateLists ?? Array.Empty<ScadaStateList>();
            SubPages = subPages;
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
        /// 本轮开始前从方案目录 <c>Alarms\AlarmHistory.json</c> 读回的历史记录（最新在前）。
        /// 历史面板把它与 <see cref="Alarms"/> 的本轮记录合并显示；<see cref="ScadaAlarmRecord.IsHistorical"/>
        /// 为 true 的行只读（确认按钮不出现）——上一轮运行的引擎已经没了，按 AlarmId 找不回去。
        /// </summary>
        public IReadOnlyList<ScadaAlarmRecord> PriorAlarms { get; }

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

        /// <summary>
        /// 本轮的运动轴快照源（<b>可为 null</b>：画面里没有轴面板、或宿主没接运动模块）。
        ///
        /// 与 <see cref="Writer"/> 同一种"可选性"：没有它时轴面板显示"未接入运动数据"，
        /// 而不是编一个 0 位置。轴面板按统一节拍<b>拉</b>当前轴的快照，
        /// 不订阅每轴事件（运动卡 10ms 轮询，推事件会把 UI 线程刷满）。
        /// </summary>
        public IScadaAxisSource? AxisSource { get; }

        /// <summary>
        /// 本轮的运动轴命令出口（<b>可为 null</b>：画面里没有轴面板、或宿主没接运动模块）。
        ///
        /// 与 <see cref="Writer"/> 同一种可选性：没有它时轴面板上的操作按钮整体置灰，
        /// 而不是"按下去没反应"。所有命令最终落到宿主的同一个执行器上——
        /// 门禁、点动看门狗、下发口径只有一份（见 <see cref="IScadaAxisCommandSink"/>）。
        /// </summary>
        public IScadaAxisCommandSink? AxisCommands { get; }

        /// <summary>
        /// 本轮的操作二次确认出口（<b>可为 null</b>：无窗口宿主/断言工程可以不给）。
        ///
        /// 轴控台的「示教 / 定位」默认先弹一次确认（<c>ConfirmCommands=true</c>）；
        /// 没有这个出口时，需要确认的操作会<b>拒绝执行并给出原因</b>，
        /// 不会静默跳过确认——那等于把"防误触"开关悄悄关掉。
        /// </summary>
        public IScadaConfirmationService? Confirmations { get; }

        /// <summary>
        /// 本轮的值通道（<b>可为 null</b>：断言工程手搭的画布、纯静态预览画面）。
        ///
        /// 数据泵（<see cref="ScadaRuntimeBinder"/>）已经拿着同一个值源，但它只负责
        /// "按绑定把值推进属性"；表格图元的列是<b>按名字</b>取的（列定义是手填的配置，
        /// 不是逐列一条 <see cref="ScadaBinding"/>），所以它需要自己按名字解析句柄。
        ///
        /// 为什么把值源放进上下文而不是让图元去问注册表：与 <see cref="AxisSource"/> 逐字同一理由——
        /// 图元库刻意不引用 VM.Core，宿主是唯一知道"变量注册表长什么样"的地方。
        /// </summary>
        public IScadaValueSource? Values { get; }

        /// <summary>
        /// 本轮的历史数据源（<b>可为 null</b>：方案没有历史目录、或宿主没接）。
        ///
        /// 只有数据表格的 <c>Source=History</c> 会用它。没有它时表格显示
        /// "未接入历史数据源"，而不是显示空表——空表会被当成"这段时间没生产"。
        /// </summary>
        public IScadaHistorySource? History { get; }

        /// <summary>
        /// 方案级文本列表（ComboBox 按 Id 解析）。<b>永不为 null</b>；没有配置时是空表。
        /// 设计期画布的 <c>StateListResolver</c> 优先，运行态没有解析器时回落到这里。
        /// </summary>
        public IReadOnlyList<ScadaStateList> StateLists { get; }

        /// <summary>
        /// 活跃子页宿主（SubPage 用）。<b>可为 null</b>：断言/无会话宿主只显示不登记事件。
        /// </summary>
        public IScadaSubPageHost? SubPages { get; }
    }
}
