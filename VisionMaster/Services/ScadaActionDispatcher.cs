using System;
using System.Globalization;
using Core.Interfaces;
using VisionMaster.Helpers;
using VisionMaster.Scada;
using VisionMaster.Services.Motion;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IScadaActionDispatcher"/> 的默认实现：把一条钩子里的动作按顺序落到运行日志上。
    ///
    /// 现在接通了哪些动作
    /// ---------
    /// 十五个动作<b>都已真执行</b>：「记录日志」写一行运行日志；「写变量」走
    /// <see cref="IScadaValueSource"/> 解析后落 <c>IWritableVariable.TryWrite</c>（S6 接入）；
    /// 「切换画面」走 <see cref="IScadaNavigator"/> 找画面并换 <c>CurrentPage</c>（S8 接入）；
    /// 「启动流程 / 停止流程 / 执行一次」走 <see cref="IScadaFlowRunner"/> 启停目标流程（Id 优先、
    /// 名字兜底；连续幂等、单次不幂等、停止最宽——语义由契约说了算，本类不做二次解释）；
    /// 运动九条（轴使能 / 失能 / 绝对定位 / 相对定位 / 回零 / 停止 / 清报警 / 点动开始 / 点动结束）
    /// 走 <see cref="MotionActionExecutor"/>（取轴、门禁、下发全在那一层，见其类注释）。
    ///
    /// 运动动作的唯一例外：<b>下发成功不代表到位</b>
    /// ---------
    /// <see cref="MotionActionExecutor.TryExecute"/> 返回 true 的含义是"命令被卡收进队列了"，
    /// 与流程侧「轴运动」步骤同一口径。日志里因此写的是"已下发（未等到位）"而不是"已完成"——
    /// 把入队当到位是运动控制里最经典的错误，而组态按钮恰恰是最容易被误解成"点完就好了"的地方。
    ///
    /// 执行不了的怎么办：<b>照实说一句</b>
    /// ---------
    /// "执行不了"分两种，措辞不同、级别也不同：
    /// ① <b>本版本不认识该动作</b>（高版本软件存下的动作类型读进低版本，
    /// Newtonsoft 按数值反序列化、不校验取值范围）；
    /// ② <b>宿主没接运动模块</b>（构造时没注入 <see cref="MotionActionExecutor"/>）。
    /// 两种都记 Warn 并如实写"本条未执行"：现场配了一条动作却一声不响，用户的结论一定是
    /// "软件有 bug"，然后整件事都得重查一遍。① 的措辞出处是
    /// <see cref="ScadaActionTypeExtensions.PendingReason"/>（不是本类的私有方法）：
    /// 属性面板上那一行提示用的是同一句，两处口径必须一致；② 是宿主装配状态，
    /// 不属于"动作类型没接通"，所以由本类自己说（断言宿主与网关场景就是这一档）。
    ///
    /// 写变量为什么按"没选变量 / 找不到 / 转不过 / 写不进"分四档报
    /// ---------
    /// 这四种失败对现场是四件事：没选变量是<b>配置漏了</b>（去面板上补）；找不到变量是<b>改名或删了</b>
    /// （去变量管理里对一下）；转不过是<b>值写错了</b>（"abc" 写不进 int）；写不进是<b>设备侧的事</b>
    /// （离线 / 没配地址 / 驱动报错，原因由 <c>IWritableVariable.TryWrite</c> 原样带上来）。
    /// 合成一句"写变量失败"，用户就得自己把这四条路各试一遍。
    /// 分档落在日志级别上：前两档是配置问题记 <c>Warn</c>（软件没坏，配置没对），
    /// 后两档是这次操作真没成记 <c>Error</c>（要人去查）。四档都带动作原文，一条都不哑掉。
    ///
    /// 切换画面为什么只有两档，而且<b>没有"点了两下"这一档</b>
    /// ---------
    /// 切页的失败只有一种：目标画面找不到（改名/删了），记 <c>Warn</c>，原因由
    /// <see cref="IScadaNavigator"/> 带上来。而"目标页正在显示"<b>不算失败</b>——
    /// 那是操作员点了两下，或者一个"回主页"按钮正好配在主页上，此时运行态什么都不做
    /// （不重发 Loaded、不压栈）并回报成功。为一次正常的重复点击在日志里留一行警告，
    /// 只会把真正该看的告警淹掉。
    ///
    /// 运动动作为什么只有"成功 / 跳过"两档，而且<b>没有"点了两下"这一档</b>
    /// ---------
    /// 运动执行器回的 false 全是"配错了"（没选轴/轴停用/卡没配）或"此刻不能动"
    /// （卡不在线/未使能/未回零/超软限位/命令被拒）——没有"设备侧故障"这一层
    /// （真正的故障由驱动自己报进运行日志与报警）。所以记 <c>Warn</c> 不记 <c>Error</c>，
    /// 与切画面、流程启停同一口径：软件没坏，下一次配对/使能/回零就好。
    /// 唯一真正的"失败"是被抛上来的异常（例如设备在 Enqueue 里崩了），那一档照旧记 Error。
    ///
    /// 为什么动作执行只往日志里写，不弹对话框
    /// ---------
    /// 操作员点一下按钮就弹窗打断，是组态现场最招人烦的设计（他可能连点二十次）。
    /// 配置类问题（动作没接上、变量名写错）是给集成人员看的，运行日志正好是他们的入口。
    ///
    /// 为什么一次触发里每条动作各写一行，而不是攒成一行
    /// ---------
    /// 三条动作攒成一行，中间那条失败了就说不清是哪条成的、哪条没成；
    /// 一行一条，日志本身就是执行流水，排查"点了没反应"时按时间从上往下读就行。
    ///
    /// 为什么每一行都带"谁在操作"（S12 审计署名）
    /// ---------
    /// 运行日志在现场就是操作审计流水：出了问题要回答的是"谁在什么时候按了哪个按钮、
    /// 值从多少改到多少"。后两问日志本来就答得出（动作原文里有变量名与目标值），
    /// 唯独"谁"必须由调用方带进来——分发器不认识用户系统（那是
    /// <see cref="IScadaAccessPolicy"/> 的事），也不该为了写一行日志去认识它。
    /// 署名取不到时落"未登录"，<b>不留空白</b>：一条没有署名的记录，等于这条记录白记了。
    ///
    /// 为什么还要再落一份 CSV（<see cref="ScadaAuditWriter"/>）
    /// ---------
    /// 运行日志是过程记录，会被滚动、会被现场清掉；审计是凭证，不能被冲掉。
    /// 两处记的是<b>同一件事</b>，所以这里把"记日志"和"落审计"绑在同一处发生
    /// （见 <see cref="Ok"/> / <see cref="Skipped"/> / <see cref="Failed"/>）——
    /// 分开写的话，将来新增一条失败分支时极容易只补了日志、忘了审计，
    /// 而"漏记的那一次"恰恰就是事后要查的那一次。
    /// 写文件不在这里做：分发器只交出 <see cref="ScadaAuditEntry"/> 这个纯数据，
    /// "记到哪个文件、写不成怎么办"归宿主（与报警"引擎发事实、宿主记文件"同一分工）。
    ///
    /// 线程：只在 UI 线程上被调用（事件的来源是鼠标与渲染，都在这条线上），故不加锁。
    /// <see cref="IScadaValueSource"/> 也是线程安全的只读查询（写值最终落到变量模型，
    /// 网络变量自己管通讯线程），<see cref="IScadaNavigator"/> 同样是 UI 线程上的会话转发；
    /// <see cref="IScadaFlowRunner"/> 的启动配方（查会话/编译/注册）也是 UI 线程链路上的一小段
    /// （真正费时的执行在引擎自己的任务里）；<see cref="MotionActionExecutor"/> 只做"取轴 + 门禁 +
    /// 入队"三步同步动作（设备侧真正的执行在卡自己的命令线程上），所以这里不需要额外的同步。
    /// </summary>
    public sealed class ScadaActionDispatcher : IScadaActionDispatcher
    {
        /// <summary>
        /// 没人登录时写在署名位置上的名字。
        ///
        /// 与 <see cref="DefaultScadaAccessPolicy.CurrentUserName"/> 的回落值是同一个词：
        /// "此刻没有会话"这件事，在运行日志、状态栏、审计文件里必须写成同一句话，
        /// 否则同一条记录在不同地方看起来像两件事。
        /// </summary>
        private const string AnonymousActor = "未登录";

        /// <summary>变量值读不出来时写在"说明"列里的占位（不是空字符串：空着看不出是"没值"还是"没记"）</summary>
        private const string UnknownValue = "—";

        private readonly ILogService _log;
        private readonly IScadaValueSource _values;
        private readonly IScadaNavigator _navigator;

        /// <summary>
        /// 流程启停出口。可为 null——没接线时流程类动作按"未接入"如实跳过（与审计同款的可选参数）：
        /// 断言工程那 16 处 <c>new ScadaActionDispatcher(...)</c> 与网关/离线场景
        /// 不该为了跑流程动作去解析一整套引擎依赖。
        /// </summary>
        private readonly IScadaFlowRunner? _flows;

        /// <summary>
        /// 操作审计落盘端（S12）。可为 null——不接线时只记运行日志，
        /// 动作执行本身一字不差地照跑（审计是旁路，不是执行的前置条件）。
        /// 留成可选参数还为了断言工程：那 16 处 <c>new ScadaActionDispatcher(...)</c>
        /// 不该为了记一份 CSV 全都去碰文件系统。
        /// </summary>
        private readonly ScadaAuditWriter? _audit;

        /// <summary>
        /// 运动命令出口（组态动作表的九条运动动作）。可为 null——没接线时运动类动作按"未接入"如实跳过。
        ///
        /// 为什么是可选参数：断言工程那 16 处 <c>new ScadaActionDispatcher(...)</c> 与网关/离线场景
        /// 不该为了跑一条"轴使能"去搭一整套运动卡注册表 + 设备仓库。
        /// 为什么由宿主显式注入而不是容器去猜：与本类 audit / flows 两个可选参数同一条纪律——
        /// 交给容器猜的结果是编译能过、运行起来按钮永远"未接入"，而且不报任何错。
        /// </summary>
        private readonly MotionActionExecutor? _motions;

        public ScadaActionDispatcher(
            ILogService log,
            IScadaValueSource values,
            IScadaNavigator navigator,
            ScadaAuditWriter? audit = null,
            IScadaFlowRunner? flows = null,
            MotionActionExecutor? motions = null)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _values = values ?? throw new ArgumentNullException(nameof(values));
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            _audit = audit;
            _flows = flows;
            _motions = motions;
        }

        /// <inheritdoc/>
        public void Dispatch(ScadaEventHook? hook, string? sourceName, string? operatorName = null)
        {
            // 空钩子/空动作表直接收手：没配动作不是错误，日志里也不该留一行"什么都没干"，
            // 审计同理——一次什么都没干的点击不该在凭证里留一行
            if (hook == null || hook.Actions.Count == 0)
                return;

            var subject = string.IsNullOrWhiteSpace(sourceName) ? "未命名对象" : sourceName;
            // 署名排在最前：现场翻日志是"先看谁干的、再看干了什么"，
            // 而且这一行同时就是 S12 要的操作审计流水（谁 · 什么时候 · 按了哪个对象 · 什么事件 → 干了什么）。
            // 时间戳由日志服务出，这里只补"谁"。
            var actor = string.IsNullOrWhiteSpace(operatorName) ? AnonymousActor : operatorName.Trim();
            // 事件名只取一次：日志那一行与审计那一列必须是同一个词
            var eventText = hook.Event.DisplayName();
            var head = $"[组态事件] {actor} · {subject} · {eventText}";
            // 审计要的四样"上下文"打包带着走：一次触发里每条动作共用同一份，
            // 逐条去拼容易在某一条上拼错（比如漏了署名），而错的那一条正是要查的那一条
            var stamp = new AuditStamp(actor, subject, eventText);

            foreach (var action in hook.Actions)
            {
                // 集合里的 null 只可能来自手工改坏的 .vms（反序列化会把 null 原样塞进 List），
                // 跳过它，不要因为一条坏数据把整串动作和这次点击一起废掉
                if (action == null)
                    continue;

                try
                {
                    Execute(action, head, stamp);
                }
                catch (Exception ex)
                {
                    // 接口契约②：一条失败不中断后面的动作。吞掉异常是这里的选择，
                    // 但必须留下带动作原文的一行，否则就成了查不到的哑失败。
                    Failed(stamp, action, $"{head} → {action.Describe()} 执行失败：{ex.Message}", ex.Message);
                }
            }
        }

        /// <summary>
        /// 一次触发的公共上下文（谁 · 对谁 · 什么事件）。打包传递而不是每个方法各收三个字符串：
        /// 漏传一个就是"审计里那一列是空的"，而空白署名/空白对象的记录等于白记。
        /// </summary>
        private readonly record struct AuditStamp(string Actor, string Subject, string EventText);

        /// <summary>
        /// 动作<b>成功</b>：记一行 Info，并落一条审计（成功档）。
        /// </summary>
        /// <param name="detail">
        /// 这次操作的<b>净效果</b>。写变量那条会给出"变量：旧值 → 新值"——
        /// 那是审计文件存在的意义之一（"值从多少改到多少"），而它只有执行侧拿得到。
        /// </param>
        private void Ok(AuditStamp stamp, ScadaAction action, string message, string? detail = null)
        {
            _log.Info(message);
            Audit(stamp, action, ScadaAuditOutcome.Success, detail);
        }

        /// <summary>
        /// 动作<b>未执行</b>：配置没对（没选变量/画面、本版本不认识这个动作）。
        /// 记 Warn 不记 Error——软件没坏，配置没对（与宿主里"权限不足记 Warn"同一口径）。
        /// </summary>
        private void Skipped(AuditStamp stamp, ScadaAction action, string message, string? detail = null)
        {
            _log.Warn(message);
            Audit(stamp, action, ScadaAuditOutcome.Skipped, detail);
        }

        /// <summary>
        /// 动作<b>失败</b>：这次操作真没成，要人去查。记 Error。
        /// </summary>
        private void Failed(AuditStamp stamp, ScadaAction action, string message, string? detail = null)
        {
            _log.Error(message);
            Audit(stamp, action, ScadaAuditOutcome.Failed, detail);
        }

        /// <summary>
        /// 落一条审计。<paramref name="action"/> 的摘要走
        /// <see cref="ScadaAction.Describe"/>——与日志、与属性面板同一个出处，
        /// 三处看到的必须是同一句话。落盘失败不抛、由落盘端自己报一次诊断
        /// （见 <see cref="ScadaAuditWriter"/>）：审计写不成不该让操作失败。
        /// </summary>
        private void Audit(AuditStamp stamp, ScadaAction action, ScadaAuditOutcome outcome, string? detail)
            => _audit?.Append(new ScadaAuditEntry(
                stamp.Actor, stamp.Subject, stamp.EventText, action.Describe(), outcome, detail));

        /// <summary>把变量值拍成一行文字。读不到时为 <see cref="UnknownValue"/>，不落一个空列</summary>
        private static string Text(object? value)
            => value == null
                ? UnknownValue
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? UnknownValue;

        /// <summary>
        /// 执行单条动作。<paramref name="head"/> 是这次触发的公共前缀（谁 + 发生了什么事件），
        /// 传进来而不是在这儿重新拼，是为了让"面板上那一行的措辞"和"日志里那一行的措辞"
        /// 由同一个出处生成（<see cref="ScadaEventTypeExtensions.DisplayName"/> 与
        /// <see cref="ScadaAction.Describe"/>）。
        /// </summary>
        private void Execute(ScadaAction action, string head, AuditStamp stamp)
        {
            switch (action.Type)
            {
                case ScadaActionType.Log:
                    // 内容留空时回落成"对象 + 事件"——新建一条动作就立刻能用，不必先逼用户填一句话
                    Ok(stamp, action, string.IsNullOrWhiteSpace(action.Text) ? head : $"{head} → {action.Text}");
                    return;

                case ScadaActionType.WriteVariable:
                    ExecuteWriteVariable(action, head, stamp);
                    return;

                case ScadaActionType.Navigate:
                    ExecuteNavigate(action, head, stamp);
                    return;

                case ScadaActionType.StartFlow:
                case ScadaActionType.StopFlow:
                case ScadaActionType.RunFlowOnce:
                    ExecuteFlowAction(action, head, stamp);
                    return;

                // 运动九条：取轴、门禁、下发全在 executor 里（那层认识 IMotionDevice 与门禁，
                // 本类只认识"动作语义"）。这里只做"接没接运动模块"与"成没成"两档交代。
                case ScadaActionType.MotionEnable:
                case ScadaActionType.MotionDisable:
                case ScadaActionType.MotionMoveAbsolute:
                case ScadaActionType.MotionMoveRelative:
                case ScadaActionType.MotionHome:
                case ScadaActionType.MotionStop:
                case ScadaActionType.MotionClearAlarm:
                case ScadaActionType.MotionJogStart:
                case ScadaActionType.MotionJogStop:
                    ExecuteMotionAction(action, head, stamp);
                    return;

                default:
                    // "高版本存下的、本版本不认识的类型"（枚举按数值反序列化，不校验取值范围）：
                    // 配置存得下、执行还没接上，所以照实说一句。那句话由领域层出
                    // （ScadaActionType.PendingReason），与属性面板上那一行提示<b>逐字相同</b>，
                    // 用户才能把"我配的这条"和"日志里这条"对上号。
                    Skipped(stamp, action,
                        $"{head} → {action.Describe()}：{action.Type.PendingReason()}，本条未执行",
                        action.Type.PendingReason());
                    return;
            }
        }

        /// <summary>
        /// 执行一条"切换画面"动作：解析目标画面 → 让运行态显示它。
        ///
        /// 为什么解析走 <see cref="IScadaNavigator"/> 而不是让本类去查文档：
        /// 与数据泵、写变量走同一条寻址口径（Id 优先、名字兜底），
        /// 而且本类<b>拿不到运行态会话</b>——"切页"只能通过那个握有会话的中介发生。
        /// 目标页被删掉/改名对不上的原因，也由那一层原样带上来，这里不加工。
        /// </summary>
        private void ExecuteNavigate(ScadaAction action, string head, AuditStamp stamp)
        {
            // ① 没选画面：配置漏了。Describe() 此刻是"切换画面（未选画面）"，再补一句就重复了，直接自己组织
            if (action.TargetPageId == Guid.Empty && string.IsNullOrWhiteSpace(action.TargetPageName))
            {
                Skipped(stamp, action, $"{head} → 切换画面：还没选画面，本条未执行", "还没选画面");
                return;
            }

            // ② 没切过去：只剩"目标画面找不到"这一种（"已经在显示"会被判成功，见类注释）
            if (!_navigator.Navigate(action.TargetPageId, action.TargetPageName, out var reason))
            {
                Skipped(stamp, action, $"{head} → {action.Describe()}：{reason}，本条未执行", reason);
                return;
            }

            // 成功照实记一行。这里用 Describe() 而不是自己拼："切换画面 → 主界面" 与属性面板上那条
            // 动作的摘要同一个出处，日志和配置界面对得上号。
            Ok(stamp, action, $"{head} → {action.Describe()}");
        }

        /// <summary>
        /// 执行一条流程动作（启动 / 停止 / 执行一次）：把目标交给 <see cref="IScadaFlowRunner"/>。
        ///
        /// 为什么解析与启停都走出口、本类不碰引擎：与写变量/切画面走同一条分工——
        /// 寻址口径（Id 优先、名字兜底）、会话准备、加密门禁全由那一处说了算，
        /// 本类只负责"没选流程"与"出口说了没办成"两档交代。
        ///
        /// 为什么失败只有 Skipped 一档（记 Warn 不记 Error）：出口回的 false 全是
        /// "配错了"（没选流程/找不到/停用/加密）或"这次没排上"（正在运行/被占用）——
        /// 没有设备侧故障那一层（引擎的事故由引擎自己报进运行日志）。
        /// 与切画面同一口径：软件没坏，下次配对/等它跑完就好。
        /// </summary>
        private void ExecuteFlowAction(ScadaAction action, string head, AuditStamp stamp)
        {
            // ⓪ 出口没接线（断言宿主/网关等没注入 runner）：照实说一句，跳过
            if (_flows == null)
            {
                Skipped(stamp, action,
                    $"{head} → {action.Describe()}：流程执行能力未接入，本条未执行",
                    "流程执行能力未接入");
                return;
            }

            // ① 没选流程：配置漏了。与"还没选画面"同款措辞（Describe() 此刻是"××流程（未选流程）"，
            //    再补一句就重复了，所以动词取类型的显示名、不取 Describe）
            if (string.IsNullOrWhiteSpace(action.FlowId) && string.IsNullOrWhiteSpace(action.FlowName))
            {
                Skipped(stamp, action,
                    $"{head} → {action.Type.DisplayName()}：还没选流程，本条未执行", "还没选流程");
                return;
            }

            // ② 交给出口。三个动作签名同构，这里只做类型 → 方法的翻译，成败语义归出口一处
            bool accepted;
            string? reason;
            switch (action.Type)
            {
                case ScadaActionType.StartFlow:
                    accepted = _flows.StartContinuous(action.FlowId ?? string.Empty, action.FlowName, out reason);
                    break;
                case ScadaActionType.StopFlow:
                    accepted = _flows.Stop(action.FlowId ?? string.Empty, action.FlowName, out reason);
                    break;
                case ScadaActionType.RunFlowOnce:
                    accepted = _flows.RunOnce(action.FlowId ?? string.Empty, action.FlowName, out reason);
                    break;
                default:
                    // 编译期不可达（上面 Execute 的 switch 已按类型分派）；留着防将来加类型漏改
                    Skipped(stamp, action,
                        $"{head} → {action.Describe()}：{action.Type.PendingReason()}，本条未执行",
                        action.Type.PendingReason());
                    return;
            }

            // ③ 出口说了"没办成"：原因原样带上来（找不到流程/已停用/加密/正在运行…），不加工
            if (!accepted)
            {
                Skipped(stamp, action, $"{head} → {action.Describe()}：{reason}，本条未执行", reason);
                return;
            }

            // 成功照实记一行；审计"说明"列补上净效果（"已启动" / "已在运行，未重复启动" /
            // "已下发单次执行" / "已发出停止" / "本就未在运行"）——那是只有出口拿得到的一句话。
            Ok(stamp, action, $"{head} → {action.Describe()}", reason);
        }

        /// <summary>
        /// 执行一条运动动作（使能 / 失能 / 定位 / 回零 / 停止 / 清报警 / 点动开始 / 点动结束）。
        ///
        /// 为什么本类只做"接线判断 + 结果翻译"，取轴与门禁全交给 <see cref="MotionActionExecutor"/>：
        /// 那两件事需要 <c>MotionAxisRegistry</c> / <c>IMotionProvider</c> / <c>MotionCommandGate</c>
        /// 一整套运动知识，而本类的职责边界是"动作语义按顺序落地"。混进来之后，
        /// 断言工程要跑一条"记录日志"动作也得先搭一整套运动依赖。
        ///
        /// 三档出口，与另外三族动作同款：
        ///   ⓪ 出口没接线 → Skipped（"未接入"是宿主装配状态，不是动作类型没接通，见类注释）
        ///   ① 执行器回了 false 且有话 → Skipped（配错了 / 此刻不能动，记 Warn 不记 Error）
        ///   ② 执行器回了 false 但没话 → <b>什么都不记</b>（静默档：点动时轴正忙，
        ///      连点期间它会反复发生，每次都记一行只会把真正该看的告警淹掉）
        /// </summary>
        private void ExecuteMotionAction(ScadaAction action, string head, AuditStamp stamp)
        {
            // ⓪ 出口没接线（断言宿主/网关等没注入 executor）：照实说一句，跳过
            if (_motions == null)
            {
                Skipped(stamp, action,
                    $"{head} → {action.Describe()}：运动模块未接入，本条未执行",
                    "运动模块未接入");
                return;
            }

            if (_motions.TryExecute(action, out var message))
            {
                // 成功照实记一行；审计"说明"列补的是同一句话——"已下发（未等到位）"。
                // 这两处都<b>不</b>说"已完成"：Enqueue 只代表命令被收下（见 MotionActionExecutor 类注释）。
                Ok(stamp, action, $"{head} → {message}", message);
                return;
            }

            // 静默档：执行器约定"false + 空 message"表示这次拒绝不该留痕（见 TryExecute 的说明）
            if (string.IsNullOrEmpty(message))
                return;

            Skipped(stamp, action, $"{head} → {message}", message);
        }

        /// <summary>
        /// 执行一条"写变量"动作：解析变量 → 把文本转成变量的类型 → 走显式写契约。
        ///
        /// 为什么值要从字符串现转、而不是配置时转好存下来：变量类型是可以改的
        /// （变量管理里把 int 改成 string），配置时转好的值会跟着僵死；而且 .vms 是给人看、
        /// 手工可改的，转换必须发生在"真要写的那一刻"，规则只认 <see cref="VariableValueConverter"/> 一份。
        ///
        /// 为什么解析走 <see cref="IScadaValueSource"/> 而不是直接查变量注册表：
        /// 与数据泵（<c>ScadaRuntimeBinder</c>）走同一条寻址通道，Id 优先、名字兜底的口径才不会分叉；
        /// 而且写能力被收在 <c>IWritableVariable</c> 后面，这里只认句柄，
        /// 将来换成"OPC 组写 / 历史回放"只需换值源实现，本方法一行不动。
        /// </summary>
        private void ExecuteWriteVariable(ScadaAction action, string head, AuditStamp stamp)
        {
            // ① 没选变量：配置漏了。Describe() 此刻是"写变量（未选变量）"，再补一句就重复了，直接自己组织
            if (action.VariableId == Guid.Empty && string.IsNullOrWhiteSpace(action.VariableName))
            {
                Skipped(stamp, action, $"{head} → 写变量：还没选变量，本条未执行", "还没选变量");
                return;
            }

            // ② 找不到变量：改名或删了（Id 对不上、名字也对不上）
            if (!_values.TryResolve(action.VariableId, action.VariableName, out var handle) || handle == null)
            {
                Skipped(stamp, action,
                    $"{head} → {action.Describe()}：找不到变量「{action.VariableName}」，本条未执行",
                    $"找不到变量「{action.VariableName}」");
                return;
            }

            // 旧值先取一次快照。写成功之后 handle.Value 已经是新值，
            // 而审计要回答的正是"值从多少改到多少"——不先取，这一问就永远答不出。
            // 位置放在"转换"之前：转换失败的那一次也想知道它原本是多少。
            var before = handle.Value;

            // ③ 值转不过去：写错了类型（"abc" 写不进 int）。原因由转换器出，带目标类型名，可直接照做
            if (!VariableValueConverter.TryConvert(action.Value, handle.DataType, out var value, out var convertError))
            {
                Failed(stamp, action, $"{head} → {action.Describe()}：{convertError}", convertError);
                return;
            }

            // ④ 写不进：设备侧的事（离线 / 没配地址 / 驱动异常）。原因由变量自己给，原样带上来不加工——
            //    这里是唯一拿得到真实结果的地方，包一层"写入失败"就把可行动的信息盖掉了。
            if (!handle.TryWrite(value, out var writeError))
            {
                Failed(stamp, action, $"{head} → {action.Describe()}：写入失败：{writeError}", writeError);
                return;
            }

            // 成功照实记一行。这里用 Describe() 而不是自己拼："写变量 启动 := 1" 与属性面板上那条
            // 动作的摘要同一个出处，日志和配置界面对得上号。
            // 审计的"说明"列补上净效果（旧值 → 新值）：日志那一行只写了目标值，
            // 而"从多少改到多少"是审计独有的那一问。
            Ok(stamp, action, $"{head} → {action.Describe()}",
                $"{handle.Name}：{Text(before)} → {Text(value)}");
        }
    }
}
