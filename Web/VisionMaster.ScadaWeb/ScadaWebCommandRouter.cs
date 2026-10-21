using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Scada;

namespace VisionMaster.ScadaWeb
{
    /// <summary>
    /// Web 命令路由：把一条 <see cref="ScadaWebCommand"/> 变成领域调用 + 审计 + 回执（方案 §8.11）。
    ///
    /// <b>它负责什么 / 不负责什么</b>
    /// ---------
    ///   · 负责：版本闸、认证/角色/策略闸、限速闸、确认闸、图元查找、动作副本过滤、命令级审计、
    ///     幂等去重、调用领域出口（分发器 / 值写入器 / 报警命令）、组装回执；
    ///   · 不负责：线程（命令泵已保证在 UI Dispatcher 上）、网络回执（泵的 <c>Completed</c> 事件）、
    ///     租约（后续批次）。
    ///
    /// <b>为什么 web 不调 <c>ScadaRuntime.RaiseElementEvent</c></b>
    /// ---------
    /// 那条路用的是<b>桌面全局</b>的 <see cref="IScadaAccessPolicy"/>：它会把"浏览器里的操作员"
    /// 判成"桌面前坐着的那一位"，宿主写审计时署名也取桌面用户名——web 用户会以别人的名字进审计。
    /// 所以这里自己判权、自己署名、自己调分发器；桌面路径一字不动。
    ///
    /// <b>动作必须"在副本上过滤"</b>
    /// ---------
    /// <see cref="ScadaActionDispatcher"/> 对 <see cref="ScadaActionType.Navigate"/> 会<b>真的切桌面页</b>，
    /// 对 <c>RequireConfirmation</c> 会弹<b>桌面模态框</b>（同步阻塞 UI 线程，所有会话一起卡住）。
    /// web 侧没有拦截点，所以先把钩子复制成"只含白名单动作、无 Navigate、无桌面确认"的副本
    /// （<see cref="ScadaWebActionFilter.BuildAllowedCopy"/>），再把副本交给分发器；
    /// 被剔除的条目逐条审计 <c>Skipped</c>，用户查得到"为什么点了没反应"。
    ///
    /// <b>闸门顺序（与方案 §8.11 的差异及理由）</b>
    /// ---------
    /// 方案写的是"③ 限速 → ④ 角色 → ⑤ 策略"，这里改成"<b>角色/策略 → 限速</b>"。
    /// 理由是图元级限速<b>跨会话共享</b>：先限速再判权，一个未授权（甚至未登录）的会话
    /// 可以靠连点把某个图元的 2/s 额度耗光，让真正有权限的操作员在下一秒点不动——
    /// 把限速放在判权之后，未授权请求根本走不到限速表，这条旁路就不存在。
    /// 其余顺序（版本 → 认证 → 角色 → 策略 → 确认）与方案逐条一致。
    /// </summary>
    public sealed class ScadaWebCommandRouter
    {
        /// <summary>
        /// Web 可执行动作的默认白名单（M2a：<b>全部 <see cref="ScadaActionType"/> 去掉 <see cref="ScadaActionType.Navigate"/></b>）。
        ///
        /// <b>为什么不是逐条枚举</b>：M2a 的默认口径是"桌面能配的动作，Web 默认都能执行"，只有镜像模式
        /// 做不到的 <see cref="ScadaActionType.Navigate"/> 剔除（它会在<b>桌面</b>上切页，改掉所有会话看到的画面）。
        /// 逐条枚举的写法每加一个动作类型就会静默漏掉一个（表现为"新动作在 Web 上永远跳过"），
        /// 而漏掉是<b>静默</b>的——只有审计里多一行 <c>not-allowed</c>。
        /// 运动九条仍由 <see cref="ScadaWebSettings.AllowMotionCommands"/> 二次收紧（见 <see cref="BuildAllowedSet"/>）。
        /// </summary>
        public static readonly IReadOnlyList<ScadaActionType> DefaultAllowedActions =
            Enum.GetValues<ScadaActionType>();

        private readonly IScadaRunHub _hub;
        private readonly IScadaActionDispatcher? _dispatcher;
        private readonly IScadaValueWriter? _valueWriter;
        private readonly IScadaAlarmCommands? _alarmCommands;
        private readonly ScadaWebAccessPolicy _policy;
        private readonly ScadaWebRateLimiter _rateLimiter;
        private readonly IScadaAuditSink? _audit;
        private readonly ILogService _log;
        private readonly ScadaWebSettings _settings;
        private readonly HashSet<ScadaActionType> _allowed;

        /// <summary>审计出口缺失只记一次 Error（每条命令都记会把日志刷满，真正的原因反而被埋掉）</summary>
        private int _auditMissingLogged;

        /// <param name="hub">运行态事实出口（版本闸与当前页）</param>
        /// <param name="dispatcher">动作分发器；<c>null</c> = 服务端未接入，<c>cmd.element</c> 回 <c>E_INTERNAL</c></param>
        /// <param name="valueWriter">值写入器；<c>null</c> = 未接入，<c>cmd.input</c> 回 <c>E_INTERNAL</c></param>
        /// <param name="alarmCommands">报警确认出口；<c>null</c> = 未接入，<c>cmd.alarm.ack</c> 回 <c>E_INTERNAL</c></param>
        /// <param name="policy">权限策略（与 caps / presence 共用同一份）</param>
        /// <param name="rateLimiter">双层限速</param>
        /// <param name="audit">
        /// 审计出口；<c>null</c> = <b>写路径关闭</b>：四条命令一律回 <c>E_INTERNAL</c> 且不执行
        /// （没有"谁按的"就不许改现场状态，见 <see cref="Handle"/> 的 ⓪）。
        /// </param>
        /// <param name="log">日志</param>
        /// <param name="settings">配置快照</param>
        /// <param name="allowedActions">
        /// 动作白名单；<c>null</c> = <see cref="DefaultAllowedActions"/>。
        /// 传进来的 <see cref="ScadaActionType.Navigate"/> 会被忽略（镜像模式）；
        /// <see cref="ScadaWebSettings.AllowMotionCommands"/>=false 时运动动作也会被剔除。
        /// </param>
        public ScadaWebCommandRouter(
            IScadaRunHub hub,
            IScadaActionDispatcher? dispatcher,
            IScadaValueWriter? valueWriter,
            IScadaAlarmCommands? alarmCommands,
            ScadaWebAccessPolicy policy,
            ScadaWebRateLimiter rateLimiter,
            IScadaAuditSink? audit,
            ILogService log,
            ScadaWebSettings settings,
            IEnumerable<ScadaActionType>? allowedActions = null)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            _dispatcher = dispatcher;
            _valueWriter = valueWriter;
            _alarmCommands = alarmCommands;
            _audit = audit;

            _allowed = BuildAllowedSet(allowedActions, settings);
            ConfirmTokens = new ScadaWebConfirmTokenStore(
                settings.ConfirmTokenSeconds,
                utcNow: null);
            Dedup = new ScadaWebCommandDedup();
        }

        /// <summary>危险动作的一次性确认令牌（宿主/断言可查、可清）</summary>
        public ScadaWebConfirmTokenStore ConfirmTokens { get; }

        /// <summary><c>(SessionId, RequestId)</c> 幂等表（宿主停服时 <see cref="ScadaWebCommandDedup.Clear"/>）</summary>
        public ScadaWebCommandDedup Dedup { get; }

        /// <summary>本次生效的动作白名单（只读；诊断与断言用）</summary>
        public IReadOnlyCollection<ScadaActionType> AllowedActions => _allowed;

        /// <summary>
        /// 执行一条命令。<b>不抛异常</b>：任何意外都转成 <c>E_INTERNAL</c> 结果（命令泵还有一层兜底）。
        ///
        /// <b>幂等在这里、不在泵里</b>：泵只回答"进没进队列"，重复判定要看"这条命令有没有真的执行过"，
        /// 而执行结果只有路由知道。去重表按 <c>(SessionId, RequestId)</c> 记（矩阵 C02/C17/C74/F18）：
        /// 窗口内重放回原回执、<b>不重复执行</b>；窗口外（仍在保留期内）回 <c>E_DUPLICATE</c> 且不执行。
        /// 空白 <see cref="ScadaWebCommand.RequestId"/> 不参与去重（断言/探针的裸命令）。
        /// </summary>
        public ScadaWebCommandResult Handle(ScadaWebCommand command)
        {
            if (command == null)
                return ScadaWebCommandResult.Fail(ScadaWebErrorCodes.BadRequest, "命令为空");

            if (command.Session == null)
                return ScadaWebCommandResult.Fail(ScadaWebErrorCodes.BadRequest, "命令缺少会话");

            // ⓪ 执行前再查一次会话状态：命令可能在队列里等了最多 5 秒，而撤销/过期
            //    发生在入队之后。只靠 HandleCommandFrame 的入口检查会让"踢下线"后
            //    队列里的命令继续生效（2026-10-21 终审 P1-7）。
            if (command.Session.IsRevoked)
            {
                return Deny(command, ScadaWebTicketService.ErrorSessionRevoked,
                    "会话已被撤销，本次命令未执行",
                    command.ElementId ?? "命令", "命令");
            }

            if (command.Session.IsSessionExpired)
            {
                return Deny(command, ScadaWebTicketService.ErrorSessionExpired,
                    "会话已过期，本次命令未执行",
                    command.ElementId ?? "命令", "命令");
            }

            // ⓪ 审计出口缺失 = 写路径关闭（fail closed）。
            // 审计是"谁按的"唯一凭证；没有它，四条命令一律<b>不执行</b>（连确认令牌都不签发）——
            // 宁可让现场看到"命令面不可用"，也不能出现"动作生效了、查不到是谁按的"。
            // 模块侧在启动时已按这条口径把 AllowCommands 关掉；这里是第二道（直接构造路由的宿主）。
            if (_audit == null && IsWriteKind(command.Kind))
            {
                if (Interlocked.Exchange(ref _auditMissingLogged, 1) == 0)
                {
                    _log.Error("[ScadaWeb] 审计出口缺失（IScadaAuditSink）：写路径已关闭，cmd.* 一律回 E_INTERNAL");
                }

                return ScadaWebCommandResult.Fail(
                    ScadaWebErrorCodes.Internal,
                    "审计出口缺失，写路径已关闭（服务端未接入 IScadaAuditSink）");
            }

            var trackDuplicate = !string.IsNullOrWhiteSpace(command.RequestId);

            // 幂等键用"稳定的 8 小时会话令牌"，不是每条 WS 连接新生成的 SessionId：
            // 断线重连 / 同账号多标签页会换一条连接、换一个 SessionId，但令牌不变；
            // 用 SessionId 会让 C02（断线重发）与 C37（多标签防重）失效。
            // 没有令牌的会话（理论上不存在）回退到 SessionId。
            var dedupKey = command.Session.Session?.Token ?? command.Session.SessionId;

            if (trackDuplicate)
            {
                var replay = Dedup.Check(dedupKey, command.RequestId, out var cached);

                if (replay == ScadaWebCommandReplay.Cached && cached != null)
                    return cached;   // 窗口内重放：原回执原样回，不执行、不再记一条成功审计

                if (replay == ScadaWebCommandReplay.Expired)
                {
                    return Deny(command, ScadaWebErrorCodes.Duplicate,
                        $"重复请求：同一 requestId 已执行过且超出 {ScadaWebCommandDedup.AckWindow.TotalMinutes:F0} 分钟幂等窗口，本次未执行",
                        command.ElementId ?? "命令", "命令");
                }
            }

            ScadaWebCommandResult result;

            try
            {
                result = command.Kind switch
                {
                    ScadaWebCommandKinds.Element => HandleElement(command),
                    ScadaWebCommandKinds.Input => HandleInput(command),
                    ScadaWebCommandKinds.AlarmAck => HandleAlarmAck(command),
                    ScadaWebCommandKinds.ConfirmRequest => HandleConfirmRequest(command),
                    _ => ScadaWebCommandResult.Fail(
                        ScadaWebErrorCodes.Unsupported,
                        $"本版本不处理该命令类型：{command.Kind ?? "<空>"}")
                };
            }
            catch (Exception ex)
            {
                _log.Error($"[ScadaWeb] 路由命令异常（kind={command.Kind}，request={command.RequestId}）："
                           + $"{ex.GetType().Name}：{ex.Message}");

                result = Deny(command, ScadaWebErrorCodes.Internal,
                    "服务端处理命令时发生异常，本次操作未生效",
                    command.ElementId ?? "命令", "命令",
                    detailExtra: ex.GetType().Name);
            }

            // 失败结果也记进去：同一条命令的重放不该再执行一次、也不该再写一条审计。
            if (trackDuplicate)
                Dedup.Complete(dedupKey, command.RequestId, result);

            return result;
        }

        #region cmd.element

        private ScadaWebCommandResult HandleElement(ScadaWebCommand command)
        {
            if (!TryResolveContext(command, out var runtime, out var page, out var failure))
                return failure;

            var element = FindElement(runtime, page, command.ElementId, out var lookupError);
            if (element == null)
                return Deny(command, ScadaWebErrorCodes.NotFound, lookupError!, command.ElementId ?? "页面", "查找图元");

            if (command.Event == null)
                return Deny(command, ScadaWebErrorCodes.BadRequest, "cmd.element 缺少 event 字段",
                    element.Name, "命令");

            var eventType = command.Event.Value;

            // ① 角色 + 策略（先于限速，理由见类注释）
            if (!_policy.CanOperate(command.Session, element.RequiredRole, out var denyCode, out var denyReason))
                return Deny(command, denyCode, denyReason, element.Name, eventType.ToString());

            // ② 限速（图元额度跨会话共享）
            if (!TryRateLimit(command, ElementKey(element), element.Name, eventType.ToString(), out var limited))
                return limited;

            // ③ 找钩子
            var hooks = CollectHooks(element, eventType);
            if (hooks.Count == 0)
            {
                return Deny(command, ScadaWebErrorCodes.NotOperable,
                    $"图元「{element.Name}」没有为事件「{eventType}」配置动作",
                    element.Name, eventType.ToString());
            }

            // ④ 动作副本过滤（白名单 + 剔除 Navigate + RequireConfirmation=false）
            //    先过滤再判"危不危险"：运动动作可能被 AllowMotionCommands=false 整条挡掉，
            //    此时不该先要求用户确认、确认完再回 E_ACTION_FORBIDDEN（2026-10-21 终审 P1-11）。
            var copies = new List<ScadaEventHook>();
            var skipDetails = new List<string>();
            var skipped = 0;

            foreach (var hook in hooks)
            {
                var copy = ScadaWebActionFilter.BuildAllowedCopy(hook, _allowed, out var skippedTypes);

                // 桌面模态确认在 Web 上由一次性令牌替代；副本里 RequireConfirmation 已被置 false。
                // 这条"配置被转换"的事实只进最终审计的 details，不再单独写一行命令级审计——
                // 命令级记录必须保持"一次命令一行"（2026-10-21 终审 P1-4）。
                if (hook.RequireConfirmation)
                    skipDetails.Add("confirmation:mirror-confirm");

                foreach (var type in skippedTypes)
                {
                    // 页面类动作（切页/回退）与"不在白名单"是两件事，审计里必须能分开：
                    // 前者是镜像模式的固有限制（谁都改不了），后者是配置/策略决定（能改）。
                    var why = ScadaWebActionFilter.IsMirrorOnlyPageAction(type) ? "mirror-mode" : "not-allowed";
                    skipDetails.Add($"{type}:{why}");
                    skipped++;
                }

                if (copy != null && copy.Actions.Count > 0)
                    copies.Add(copy);
            }

            if (copies.Count == 0)
            {
                Audit(command, element.Name, "Web 操作", eventType.ToString(), "跳过",
                    $"全部动作被跳过（{string.Join(",", skipDetails)}）");

                var message = skipped == 0
                    ? "该图元的事件没有可执行的动作（动作表为空或全是坏数据）"
                    : "该图元配置的动作全部不能在镜像模式下执行（切页/回退 / 白名单外动作）";

                return ScadaWebCommandResult.Fail(
                    ScadaWebErrorCodes.ActionForbidden,
                    message,
                    new { actions = 0, skipped, details = skipDetails });
            }

            var actionCount = copies.Sum(c => c.Actions.Count);

            if (_dispatcher == null)
            {
                return Deny(command, ScadaWebErrorCodes.Internal,
                    "服务端未接入动作分发器（IScadaActionDispatcher），图元事件不可用",
                    element.Name, eventType.ToString());
            }

            // ⑤ 危险动作确认闸（桌面模态框在 web 链路上没人能回答，用一次性令牌替代）
            //    判据只看"过滤后真正会执行的动作"：被白名单/运动总闸挡掉的动作不触发确认。
            // 危险判据必须同时看原钩子与过滤后的副本：
            // 副本的 RequireConfirmation 被过滤器强制置 false，只看副本会漏掉
            // "用户勾了执行前确认、但动作类型本身不危险（只有日志/写变量）"的钩子
            // （2026-10-21 终审 P1-1）。动作类型危险仍按副本判（被过滤掉的不算）。
            var dangerous = hooks.Any(h => h.RequireConfirmation) || copies.Any(HookIsDangerous);
            var actionKind = ActionKindFor(eventType);

            if (_settings.RequireConfirmForDangerous && dangerous)
            {
                if (!ConfirmTokens.TryConsume(
                        command.ConfirmToken,
                        command.Session.SessionId,
                        ElementKey(element),
                        actionKind,
                        out var tokenReason))
                {
                    return Deny(command, ScadaWebErrorCodes.Confirm,
                        $"危险操作需要一次性确认令牌（{tokenReason}），请先申请再重试",
                        element.Name, eventType.ToString(),
                        detailExtra: $"reason={tokenReason}");
                }
            }

            // ⑥ 分发（动作级审计由 dispatcher 自己写）
            //    收集逐条结果：分发器"没抛异常"不等于动作成功（没选变量/转换失败都是 Skipped/Failed）。
            var succeeded = 0;
            var dispatcherSkipped = 0;
            var dispatcherFailed = 0;

            try
            {
                foreach (var copy in copies)
                {
                    var dispatchResult = _dispatcher.DispatchWithResult(copy, element.Name, ActorOf(command.Session));

                    // 默认接口实现返回 Empty（"没给明细"）。它不能当成成功：
                    // 有动作要执行却拿不到任何计数，只能按"未确认"记失败，让现场去补实现。
                    if (dispatchResult.Total == 0 && copy.Actions.Count > 0)
                    {
                        dispatcherFailed += copy.Actions.Count;
                        continue;
                    }

                    succeeded += dispatchResult.Succeeded;
                    dispatcherSkipped += dispatchResult.Skipped;
                    dispatcherFailed += dispatchResult.Failed;
                }
            }
            catch (Exception ex)
            {
                // 接口契约要求分发器不抛；真抛了也不能让回执显示"成功"。
                _log.Error($"[ScadaWeb] 动作分发异常（图元={element.Name}，事件={eventType}）："
                           + $"{ex.GetType().Name}：{ex.Message}");

                Audit(command, element.Name, "Web 操作", eventType.ToString(), "失败",
                    $"dispatcher 异常：{ex.GetType().Name}");

                return ScadaWebCommandResult.Fail(
                    ScadaWebErrorCodes.Internal,
                    "动作执行时发生异常，部分动作可能已生效",
                    new { actions = actionCount, skipped, details = skipDetails });
            }

            // ⑦ 命令级审计写最终结果：一次命令只留一行，避免异常时“成功 + 失败”两行。
            // 动作级审计由 dispatcher 自己写；这里只回答“这条命令最后成没成”。
            // 只有"一条都没成功、全部未执行"才记「跳过」；部分成功仍记「成功」，
            // 否则审计会把已经发生的成功动作说成没执行（2026-10-21 终审 P0-3 第三条）。
            var outcome = dispatcherFailed > 0
                ? "失败"
                : succeeded == 0 && dispatcherSkipped > 0
                    ? "跳过"
                    : "成功";
            Audit(command, element.Name, "Web 操作", eventType.ToString(), outcome,
                $"hooks={copies.Count} actions={actionCount} ok={succeeded} "
                + $"skipped={skipped + dispatcherSkipped} failed={dispatcherFailed} "
                + $"details={string.Join(",", skipDetails)}");

            var resultPayload = new
            {
                actions = actionCount,
                succeeded,
                skipped = skipped + dispatcherSkipped,
                failed = dispatcherFailed,
                hooks = copies.Count,
                details = skipDetails
            };

            if (dispatcherFailed > 0)
            {
                return ScadaWebCommandResult.Fail(
                    ScadaWebErrorCodes.Internal,
                    $"动作执行失败 {dispatcherFailed} 条（成功 {succeeded}、未执行 {skipped + dispatcherSkipped}）",
                    resultPayload);
            }

            return ScadaWebCommandResult.Success(resultPayload);
        }

        #endregion

        #region cmd.input

        private ScadaWebCommandResult HandleInput(ScadaWebCommand command)
        {
            if (!TryResolveContext(command, out var runtime, out var page, out var failure))
                return failure;

            var element = FindElement(runtime, page, command.ElementId, out var lookupError);
            if (element == null)
                return Deny(command, ScadaWebErrorCodes.NotFound, lookupError!, command.ElementId ?? "页面", "写值");

            var property = string.IsNullOrWhiteSpace(command.Property)
                ? "Value"
                : command.Property!.Trim();

            if (!_policy.CanOperate(command.Session, element.RequiredRole, out var denyCode, out var denyReason))
                return Deny(command, denyCode, denyReason, element.Name, "输入");

            if (!TryRateLimit(command, ElementKey(element), element.Name, "输入", out var limited))
                return limited;

            // "不可输入" = 该属性没有启用的变量绑定。锁（IsLocked）是编辑期概念，运行期不参与。
            var bound = false;
            foreach (var binding in element.Bindings)
            {
                if (binding == null || !binding.IsEnabled)
                    continue;

                if (string.Equals(binding.TargetProperty, property, StringComparison.Ordinal))
                {
                    bound = true;
                    break;
                }
            }

            if (!bound)
            {
                return Deny(command, ScadaWebErrorCodes.NotOperable,
                    $"图元「{element.Name}」的「{property}」没有启用变量绑定，无法输入",
                    element.Name, "输入");
            }

            if (_valueWriter == null)
            {
                return Deny(command, ScadaWebErrorCodes.Internal,
                    "服务端未接入值写入通道（IScadaValueWriter），输入功能不可用",
                    element.Name, "输入");
            }

            // 旧值必须取"运行期变量值"，不能拿图元的属性袋冒充：
            // 属性袋是组态期的静态配置（如 TextField.Properties["Value"]），
            // 绑定变量之后它不随运行值变化，审计里会写成"10 → 21"而实际变量是 1485（2026-10-21 终审 P1-12）。
            _valueWriter.TryReadText(element, property, out var oldValue);
            var text = command.Value ?? string.Empty;

            if (!_valueWriter.TryWriteText(element, property, text, out var writeError))
            {
                Audit(command, element.Name, "Web 操作", "输入", "失败",
                    $"property={property} 值={text} 原因={writeError ?? "<未给出>"}");

                return ScadaWebCommandResult.Fail(
                    ScadaWebErrorCodes.Value,
                    string.IsNullOrWhiteSpace(writeError) ? "写入失败（原因由服务端写入器决定）" : writeError,
                    new { property, value = text });
            }

            Audit(command, element.Name, "Web 操作", "输入", "成功",
                $"property={property} {oldValue ?? "<未知>"} → {text}");

            return ScadaWebCommandResult.Success(new
            {
                property,
                oldValue,
                value = text
            });
        }

        #endregion

        #region cmd.alarm.ack

        private ScadaWebCommandResult HandleAlarmAck(ScadaWebCommand command)
        {
            if (!TryResolveContext(command, out var runtime, out var page, out var failure))
                return failure;

            // 报警确认可以来自某个图元（AckButton，按它的 RequiredRole 判权），
            // 也可以来自顶部工具条（没有图元 → 只要求"已登录 + 策略允许"）。
            ScadaElement? element = null;

            if (!string.IsNullOrWhiteSpace(command.ElementId))
            {
                element = FindElement(runtime, page, command.ElementId, out var lookupError);
                if (element == null)
                    return Deny(command, ScadaWebErrorCodes.NotFound, lookupError!,
                        command.ElementId!, "确认报警");
            }

            if (!_policy.CanOperate(command.Session, element?.RequiredRole, out var denyCode, out var denyReason))
                return Deny(command, denyCode, denyReason, element?.Name ?? "报警", "确认报警");

            if (!TryRateLimit(command, element == null ? null : ElementKey(element),
                    element?.Name ?? "报警", "确认报警", out var limited))
            {
                return limited;
            }

            if (_alarmCommands == null)
            {
                return Deny(command, ScadaWebErrorCodes.Internal,
                    "服务端未接入报警命令出口（IScadaAlarmCommands），报警确认不可用",
                    element?.Name ?? "报警", "确认报警");
            }

            if (command.All)
            {
                var acknowledged = _alarmCommands.AcknowledgeAll(out var allError);

                if (!string.IsNullOrWhiteSpace(allError))
                {
                    Audit(command, element?.Name ?? "报警", "Web 操作", "确认报警", "失败",
                        $"全部确认失败：{allError}");

                    return ScadaWebCommandResult.Fail(ScadaWebErrorCodes.NotOperable, allError!);
                }

                Audit(command, element?.Name ?? "报警", "Web 操作", "确认报警", "成功",
                    $"全部确认 {acknowledged} 条");

                return ScadaWebCommandResult.Success(new { acknowledged, all = true });
            }

            if (command.AlarmId == null || command.AlarmId.Value == Guid.Empty)
            {
                return Deny(command, ScadaWebErrorCodes.BadRequest,
                    "cmd.alarm.ack 需要 alarmId，或 all=true",
                    element?.Name ?? "报警", "确认报警");
            }

            if (!_alarmCommands.TryAcknowledge(command.AlarmId.Value, out var error))
            {
                var reason = string.IsNullOrWhiteSpace(error) ? "该报警此刻无法确认" : error!;

                Audit(command, element?.Name ?? "报警", "Web 操作", "确认报警", "失败",
                    $"alarmId={command.AlarmId.Value:D} 原因={reason}");

                return ScadaWebCommandResult.Fail(ScadaWebErrorCodes.NotOperable, reason);
            }

            Audit(command, element?.Name ?? "报警", "Web 操作", "确认报警", "成功",
                $"alarmId={command.AlarmId.Value:D}");

            return ScadaWebCommandResult.Success(new { acknowledged = 1, all = false });
        }

        #endregion

        #region cmd.confirm.request

        private ScadaWebCommandResult HandleConfirmRequest(ScadaWebCommand command)
        {
            if (!TryResolveContext(command, out var runtime, out var page, out var failure))
                return failure;

            var element = FindElement(runtime, page, command.ElementId, out var lookupError);
            if (element == null)
                return Deny(command, ScadaWebErrorCodes.NotFound, lookupError!, command.ElementId ?? "页面", "申请确认");

            // 确认令牌按事件绑定（Pressed/Released 不共享）。缺 event 时签发一个
            // actionKind="cmd.element" 的令牌，而 cmd.element 只接受 "event:<Event>"，
            // 用户拿到的令牌永远用不上——必须在签发前就拒绝。
            if (command.Event == null)
                return Deny(command, ScadaWebErrorCodes.BadRequest,
                    "cmd.confirm.request 缺少 event 字段（如 Pressed / Released）",
                    element.Name, "申请确认");

            if (!_policy.CanOperate(command.Session, element.RequiredRole, out var denyCode, out var denyReason))
                return Deny(command, denyCode, denyReason, element.Name, "申请确认");

            if (!TryRateLimit(command, ElementKey(element), element.Name, "申请确认", out var limited))
                return limited;

            var actionKind = ActionKindFor(command.Event.Value);

            var token = ConfirmTokens.Issue(
                command.Session.SessionId,
                ElementKey(element),
                actionKind);

            // 令牌本身绝不进审计（与 ticket/sessionToken 同一条口径：日志会被打包带走）。
            Audit(command, element.Name, "Web 操作", "申请确认", "成功",
                $"签发一次性确认令牌（action={actionKind}，有效期={ConfirmTokens.LifetimeSeconds}s）");

            return ScadaWebCommandResult.Success(new
            {
                confirmToken = token,
                expiresIn = ConfirmTokens.LifetimeSeconds,
                actionKind
            });
        }

        #endregion

        #region 闸门与工具

        /// <summary>
        /// 版本闸 + 认证闸之前的事实解析：当前这一轮、当前页、轮次号。
        /// 三闸顺序固定 <c>runGeneration → pageId → pageVersion</c>；都不过时只报第一道（方案 §4.3 规则 7）。
        ///
        /// <b>M2a：三闸必填</b>。缺 <c>runGeneration</c> / <c>pageId</c> / <c>pageVersion</c> 一律
        /// <c>E_BAD_REQUEST</c>，<b>不许"有值才判"</b>——那等于让客户端把字段删掉就跳过三道闸
        /// （矩阵 C75 ④；旧实现正是这个形状）。
        /// </summary>
        private bool TryResolveContext(
            ScadaWebCommand command,
            out ScadaRuntime runtime,
            out ScadaPage page,
            out ScadaWebCommandResult failure)
        {
            runtime = null!;
            page = null!;
            failure = null!;

            // ① 三闸字段先做"存在性"判定（与运行态无关，缺字段不该被报成"没在运行"）
            if (command.RunGeneration <= 0)
            {
                failure = Deny(command, ScadaWebErrorCodes.BadRequest,
                    "命令缺少 runGeneration（当前运行轮次），三闸必填不可省略",
                    command.ElementId ?? "命令", "命令");
                return false;
            }

            if (string.IsNullOrWhiteSpace(command.PageId))
            {
                failure = Deny(command, ScadaWebErrorCodes.BadRequest,
                    "命令缺少 pageId（当前画面），三闸必填不可省略",
                    command.ElementId ?? "命令", "命令");
                return false;
            }

            if (!Guid.TryParse(command.PageId, out var requestedPageId))
            {
                failure = Deny(command, ScadaWebErrorCodes.BadRequest,
                    "pageId 不是合法的 Guid", command.ElementId ?? "命令", "命令");
                return false;
            }

            if (command.PageVersion <= 0)
            {
                failure = Deny(command, ScadaWebErrorCodes.BadRequest,
                    "命令缺少 pageVersion（画面版本），三闸必填不可省略",
                    command.ElementId ?? "命令", "命令");
                return false;
            }

            ScadaRuntime? live;
            long generation;

            try
            {
                if (!_hub.TryGetCurrent(out live, out _, out generation) || live == null)
                {
                    failure = Deny(command, ScadaWebErrorCodes.StaleRun,
                        "当前没有运行中的画面（桌面端未运行或已停止）",
                        command.ElementId ?? "页面", "命令");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"[ScadaWeb] 读取运行态失败：{ex.GetType().Name}：{ex.Message}");
                failure = ScadaWebCommandResult.Fail(
                    ScadaWebErrorCodes.StaleRun, "无法读取运行态（服务端异常）");
                return false;
            }

            if (command.RunGeneration != generation)
            {
                failure = Deny(command, ScadaWebErrorCodes.StaleRun,
                    $"运行轮次已变化（命令 {command.RunGeneration}，当前 {generation}），请重新取快照",
                    command.ElementId ?? "页面", "命令",
                    detailExtra: $"expected={command.RunGeneration} actual={generation}");
                return false;
            }

            var current = live.CurrentPage;

            if (current == null)
            {
                failure = Deny(command, ScadaWebErrorCodes.StaleRun,
                    "当前没有显示中的画面", command.ElementId ?? "页面", "命令");
                return false;
            }

            if (requestedPageId != current.PageId)
            {
                failure = Deny(command, ScadaWebErrorCodes.StalePage,
                    "命令来自另一张画面（已切页），请重新取快照",
                    command.ElementId ?? "页面", "命令",
                    detailExtra: $"expected={requestedPageId:D} actual={current.PageId:D}");
                return false;
            }

            if (command.PageVersion != current.Version)
            {
                failure = Deny(command, ScadaWebErrorCodes.StalePage,
                    $"画面结构已变化（命令版本 {command.PageVersion}，当前 {current.Version}），请重新取快照",
                    command.ElementId ?? "页面", "命令",
                    detailExtra: $"expected={command.PageVersion} actual={current.Version}");
                return false;
            }

            runtime = live;
            page = current;
            return true;
        }

        /// <summary>
        /// 在当前页里找图元；找不到再找基座页（基座是跨页公共层，它的按钮在每一页上都能按）。
        /// 基座页里的图元同样按它自己的 <see cref="ScadaElement.RequiredRole"/> 判权。
        /// </summary>
        private ScadaElement? FindElement(
            ScadaRuntime runtime,
            ScadaPage page,
            string? elementId,
            out string? error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(elementId))
            {
                error = "命令缺少 elementId";
                return null;
            }

            if (!Guid.TryParse(elementId, out var id))
            {
                error = $"elementId 不是合法的 Guid：{elementId}";
                return null;
            }

            var found = page.FindElement(id);
            if (found != null)
                return found;

            foreach (var basePage in runtime.CurrentBasePages)
            {
                found = basePage.FindElement(id);
                if (found != null)
                    return found;
            }

            error = "该图元不在当前画面上（可能已被删除或已切页）";
            return null;
        }

        /// <summary>双层限速；失败时顺手落一条拒绝审计</summary>
        private bool TryRateLimit(
            ScadaWebCommand command,
            string? elementKey,
            string subject,
            string eventText,
            out ScadaWebCommandResult failure)
        {
            failure = null!;

            if (_rateLimiter.TryAcquire(command.Session.SessionId, elementKey, out var error, out var retryAfterMs))
                return true;

            failure = Deny(command, ScadaWebErrorCodes.RateLimited,
                error ?? "命令过快，请稍后重试",
                subject, eventText,
                result: new { retryAfterMs });

            return false;
        }

        /// <summary>本事件在钩子表里"配过且真有动作"的那些钩子（与 <c>ScadaHookMatcher</c> 同一口径）</summary>
        private static List<ScadaEventHook> CollectHooks(ScadaElement element, ScadaEventType eventType)
        {
            var hooks = new List<ScadaEventHook>();

            foreach (var hook in element.EventHooks)
            {
                if (hook == null || hook.Event != eventType || hook.Actions.Count == 0)
                    continue;

                hooks.Add(hook);
            }

            return hooks;
        }

        /// <summary>
        /// 这条钩子算不算"危险动作"：钩子本身要求确认，或串里至少有一条
        /// <see cref="ScadaActionTypeExtensions.IsDangerous"/> 的动作（停流程 / 运动类）。
        /// </summary>
        private static bool HookIsDangerous(ScadaEventHook hook)
        {
            if (hook.RequireConfirmation)
                return true;

            foreach (var action in hook.Actions)
            {
                if (action != null && action.Type.IsDangerous())
                    return true;
            }

            return false;
        }

        private static HashSet<ScadaActionType> BuildAllowedSet(
            IEnumerable<ScadaActionType>? allowedActions,
            ScadaWebSettings settings)
        {
            var source = allowedActions ?? DefaultAllowedActions;
            var set = new HashSet<ScadaActionType>();

            foreach (var type in source)
            {
                // 页面类动作在过滤器里还有一道硬剔除；这里先摘掉，免得诊断时看到"允许切页/回退"。
                if (ScadaWebActionFilter.IsMirrorOnlyPageAction(type))
                    continue;

                if (IsMotion(type) && !settings.AllowMotionCommands)
                    continue;

                set.Add(type);
            }

            return set;
        }

        private static bool IsMotion(ScadaActionType type) => type is
            ScadaActionType.MotionEnable or ScadaActionType.MotionDisable
            or ScadaActionType.MotionMoveAbsolute or ScadaActionType.MotionMoveRelative
            or ScadaActionType.MotionHome or ScadaActionType.MotionStop
            or ScadaActionType.MotionClearAlarm or ScadaActionType.MotionJogStart
            or ScadaActionType.MotionJogStop;

        /// <summary>确认令牌绑定的"动作种类"：按事件区分（同一个按钮的 Pressed 与 Released 不共享令牌）</summary>
        private static string ActionKindFor(ScadaEventType eventType) => "event:" + eventType;

        /// <summary>会改变现场状态的四条命令（缺审计出口时一律不执行，见 <see cref="Handle"/>）</summary>
        private static bool IsWriteKind(string? kind)
            => string.Equals(kind, ScadaWebCommandKinds.Element, StringComparison.Ordinal)
               || string.Equals(kind, ScadaWebCommandKinds.Input, StringComparison.Ordinal)
               || string.Equals(kind, ScadaWebCommandKinds.AlarmAck, StringComparison.Ordinal)
               || string.Equals(kind, ScadaWebCommandKinds.ConfirmRequest, StringComparison.Ordinal);

        private static string ElementKey(ScadaElement element) => element.ElementId.ToString("D");

        private static string ActorOf(ScadaWebSession session)
            => string.IsNullOrWhiteSpace(session.UserName) ? "未登录" : session.UserName!;

        /// <summary>拒绝 + 命令级审计（审计写失败只记日志，不改变回执）</summary>
        private ScadaWebCommandResult Deny(
            ScadaWebCommand command,
            string code,
            string reason,
            string subject,
            string eventText,
            object? result = null,
            string? detailExtra = null)
        {
            var detail = string.IsNullOrWhiteSpace(detailExtra)
                ? reason
                : $"{reason}（{detailExtra}）";

            Audit(command, subject, "Web 操作", eventText, "拒绝", detail);

            return ScadaWebCommandResult.Fail(code, reason, result);
        }

        /// <summary>
        /// 命令级审计。参数顺序与 <see cref="IScadaAuditSink.Append"/> 一致；
        /// <c>[web s=… ip=…]</c> 前缀由宿主写入器统一拼（这里不拼，否则会拼两遍）。
        /// </summary>
        private void Audit(
            ScadaWebCommand command,
            string subject,
            string eventText,
            string action,
            string result,
            string detail)
        {
            if (_audit == null)
                return;

            var session = command.Session;

            try
            {
                if (!_audit.Append(
                        ActorOf(session),
                        session.SessionId,
                        session.RemoteIp,
                        subject,
                        eventText,
                        action,
                        result,
                        detail))
                {
                    _log.Warn($"[ScadaWeb] 命令级审计未写入（actor={ActorOf(session)}，subject={subject}，result={result}）");
                }
            }
            catch (Exception ex)
            {
                // 审计坏掉不许把命令带下去（方案口径：记日志、不阻断动作本身）。
                _log.Warn($"[ScadaWeb] 写命令级审计异常：{ex.GetType().Name}：{ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// 危险动作的一次性确认令牌（方案 §8.4.1；<c>cmd.confirm.request</c> 签发、<c>cmd.element</c> 校验）。
    ///
    /// <b>三条硬语义</b>
    /// ---------
    ///   ① <b>绑定三元组</b> <c>(sessionId, elementId, actionKind)</c>：换一个会话、换一个图元、
    ///      换一个事件都不认。令牌不是"通行证"，是"这一次、这个按钮"的确认回执。
    ///   ② <b>单次</b>：校验通过<b>先删再返回 true</b>——先执行后删的话，执行期间重放会成功第二次。
    ///   ③ <b>有效期</b>（默认 30s，见 <see cref="ScadaWebSettings.ConfirmTokenSeconds"/>）：
    ///      过期即作废；过期的条目在校验时顺手删掉。
    ///
    /// <b>为什么不用桌面模态框</b>
    /// ---------
    /// <c>ScadaActionDispatcher</c> 的确认是宿主模态框（同步阻塞 UI 线程）。Web 用户不在那台机器前，
    /// 弹框会把<b>所有会话</b>的命令泵一起卡住，直到桌面前有人点掉；而且"浏览器里谁按的"变成
    /// "桌面前的人确认"，审计主体错位。
    ///
    /// <b>线程</b>：签发来自命令泵（UI 线程），校验也在命令泵；锁仍然加上——将来若有
    /// 后台预热/清理任务，不必再改这里。
    /// </summary>
    public sealed class ScadaWebConfirmTokenStore
    {
        /// <summary>默认有效期（秒）</summary>
        public const int DefaultLifetimeSeconds = ScadaWebSettings.DefaultConfirmTokenSeconds;

        private const int SweepThreshold = 64;

        private readonly object _gate = new();
        private readonly Dictionary<string, Entry> _tokens = new(StringComparer.Ordinal);
        private readonly Func<DateTime> _utcNow;

        /// <summary>本令牌表签发的令牌有效期（秒，已夹到配置允许的区间）</summary>
        public int LifetimeSeconds { get; }

        /// <param name="lifetimeSeconds">有效期（秒）；越界按 <see cref="ScadaWebSettings.MinConfirmTokenSeconds"/>~<see cref="ScadaWebSettings.MaxConfirmTokenSeconds"/> 夹取</param>
        /// <param name="utcNow">时钟（断言用来把 30 秒快进过去；产品路径传 <c>null</c>）</param>
        public ScadaWebConfirmTokenStore(int lifetimeSeconds = DefaultLifetimeSeconds, Func<DateTime>? utcNow = null)
        {
            LifetimeSeconds = Math.Clamp(
                lifetimeSeconds,
                ScadaWebSettings.MinConfirmTokenSeconds,
                ScadaWebSettings.MaxConfirmTokenSeconds);

            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>当前存活（未过期、未使用）的令牌数</summary>
        public int Count
        {
            get { lock (_gate) return _tokens.Count; }
        }

        /// <summary>签发一枚令牌（32 hex）。同一会话/图元/动作重复申请会得到多枚，每枚各自单次有效</summary>
        public string Issue(string? sessionId, string? elementId, string? actionKind)
        {
            var now = _utcNow();
            var token = Guid.NewGuid().ToString("N");

            lock (_gate)
            {
                SweepExpired(now);

                _tokens[token] = new Entry
                {
                    SessionId = sessionId ?? string.Empty,
                    ElementId = elementId ?? string.Empty,
                    ActionKind = actionKind ?? string.Empty,
                    ExpiresAtUtc = now.AddSeconds(LifetimeSeconds)
                };
            }

            return token;
        }

        /// <summary>
        /// 校验并<b>消费</b>一枚令牌。失败原因（进审计 detail 的 <c>reason=</c>）：
        /// <c>missing</c> / <c>unknown</c> / <c>expired</c> / <c>mismatch:session</c> /
        /// <c>mismatch:element</c> / <c>mismatch:action</c>。
        ///
        /// <b>不匹配时不删除</b>：那枚令牌仍是合法持有者的，别人拿错了不该把它废掉。
        /// </summary>
        public bool TryConsume(
            string? token,
            string? sessionId,
            string? elementId,
            string? actionKind,
            out string reason)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                reason = "missing";
                return false;
            }

            lock (_gate)
            {
                if (!_tokens.TryGetValue(token!, out var entry))
                {
                    reason = "unknown";
                    return false;
                }

                if (_utcNow() >= entry.ExpiresAtUtc)
                {
                    _tokens.Remove(token!);
                    reason = "expired";
                    return false;
                }

                if (!string.Equals(entry.SessionId, sessionId ?? string.Empty, StringComparison.Ordinal))
                {
                    reason = "mismatch:session";
                    return false;
                }

                if (!string.Equals(entry.ElementId, elementId ?? string.Empty, StringComparison.Ordinal))
                {
                    reason = "mismatch:element";
                    return false;
                }

                if (!string.Equals(entry.ActionKind, actionKind ?? string.Empty, StringComparison.Ordinal))
                {
                    reason = "mismatch:action";
                    return false;
                }

                // 先删后执行：执行期间重放必须失败（方案 §8.4.1）。
                _tokens.Remove(token!);
                reason = string.Empty;
                return true;
            }
        }

        /// <summary>清空（停服 / 断言段收尾）</summary>
        public void Clear()
        {
            lock (_gate)
                _tokens.Clear();
        }

        private void SweepExpired(DateTime now)
        {
            if (_tokens.Count <= SweepThreshold)
                return;

            List<string>? dead = null;

            foreach (var pair in _tokens)
            {
                if (pair.Value.ExpiresAtUtc > now)
                    continue;

                (dead ??= new List<string>()).Add(pair.Key);
            }

            if (dead == null)
                return;

            foreach (var key in dead)
                _tokens.Remove(key);
        }

        private sealed class Entry
        {
            public string SessionId { get; init; } = string.Empty;

            public string ElementId { get; init; } = string.Empty;

            public string ActionKind { get; init; } = string.Empty;

            public DateTime ExpiresAtUtc { get; init; }
        }
    }

    /// <summary>一次 <c>requestId</c> 查询的结果（见 <see cref="ScadaWebCommandDedup.Check"/>）</summary>
    public enum ScadaWebCommandReplay
    {
        /// <summary>没见过的请求号：正常执行</summary>
        None = 0,

        /// <summary>幂等窗口内的重放：回原回执、<b>不执行</b></summary>
        Cached = 1,

        /// <summary>见过但已超出幂等窗口（仍在保留期）：回 <c>E_DUPLICATE</c>、<b>不执行</b></summary>
        Expired = 2
    }

    /// <summary>
    /// 命令幂等表：<c>(SessionId, RequestId)</c> → 首次执行结果（矩阵 C02 / C17 / C74 / F18）。
    ///
    /// <b>为什么是两档而不是一档</b>
    /// ---------
    /// 一档（"5 分钟后忘掉"）会把 6 分钟后的重放当成一条全新命令——那正好是重放攻击与
    /// 断线重发最常见的时刻，结果是<b>重复执行</b>。所以：
    ///   · <see cref="AckWindow"/>（5 分钟）内：回原回执（客户端重试能拿到同一个结果，不执行）；
    ///   · 超出窗口但仍在 <see cref="Retention"/>（30 分钟）内：回 <c>E_DUPLICATE</c> 且不执行；
    ///   · 超出保留期 / 表满被挤掉：当新命令处理。
    ///
    /// <b>为什么键里有 SessionId</b>：requestId 由客户端生成，不同会话撞号是完全可能的
    /// （同一份前端代码、同一个计数器起点）。只按 requestId 去重会让"A 的请求把 B 的命令吃掉"。
    ///
    /// <b>线程</b>：路由只在命令泵的 UI 回调里执行（单线程），锁仍然加上——将来若把"清理"
    /// 挪到后台任务，不必再改这里。表有上限（<see cref="MaxEntries"/>），超限先扫过期、再挤最旧的。
    /// </summary>
    public sealed class ScadaWebCommandDedup
    {
        /// <summary>窗口内重放回原回执的时长（5 分钟，方案 §9.8 / 矩阵 C17）</summary>
        public static readonly TimeSpan AckWindow = TimeSpan.FromMinutes(5);

        /// <summary>窗口外仍判 <c>E_DUPLICATE</c> 的保留时长（30 分钟）</summary>
        public static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);

        /// <summary>
        /// 表的硬上限。默认 16 会话 × 5 条/秒 × 5 分钟窗口 ≈ 24000 条，取 65536 留足余量。
        /// <b>绝不为了腾位置淘汰仍在 AckWindow 内的条目</b>——那等于把幂等窗口从 5 分钟
        /// 偷偷缩短到"表满为止"，重放会再次执行（2026-10-21 终审 P1-4）。
        /// </summary>
        public const int MaxEntries = 65536;

        private readonly object _gate = new();
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly Func<DateTime> _utcNow;

        /// <param name="utcNow">时钟（断言用来把 5/30 分钟快进过去；产品路径传 <c>null</c>）</param>
        public ScadaWebCommandDedup(Func<DateTime>? utcNow = null)
        {
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>当前记录条数（诊断 / 停服后应为 0）</summary>
        public int Count
        {
            get { lock (_gate) return _entries.Count; }
        }

        /// <summary>
        /// 查一次请求号。返回 <see cref="ScadaWebCommandReplay.Cached"/> 时
        /// <paramref name="cached"/> 是首次执行的结果（原样回给客户端）。
        /// </summary>
        public ScadaWebCommandReplay Check(string? sessionId, string? requestId, out ScadaWebCommandResult? cached)
        {
            cached = null;

            if (string.IsNullOrWhiteSpace(requestId))
                return ScadaWebCommandReplay.None;

            var key = KeyOf(sessionId, requestId);
            var now = _utcNow();

            lock (_gate)
            {
                if (!_entries.TryGetValue(key, out var entry))
                    return ScadaWebCommandReplay.None;

                var age = now - entry.FirstSeenUtc;

                if (age > Retention)
                {
                    _entries.Remove(key);
                    return ScadaWebCommandReplay.None;
                }

                if (age <= AckWindow)
                {
                    cached = entry.Result;
                    return ScadaWebCommandReplay.Cached;
                }

                return ScadaWebCommandReplay.Expired;
            }
        }

        /// <summary>记下一条命令的首次执行结果（同一键已存在时保留最早那条）</summary>
        public void Complete(string? sessionId, string? requestId, ScadaWebCommandResult result)
        {
            if (string.IsNullOrWhiteSpace(requestId) || result == null)
                return;

            var key = KeyOf(sessionId, requestId);
            var now = _utcNow();

            lock (_gate)
            {
                if (_entries.ContainsKey(key))
                    return;

                if (_entries.Count >= MaxEntries)
                    TrimLocked(now);

                // 清完过期仍满：宁可不记这一条（该请求号失去去重保护），也不淘汰窗口内
                // 仍在生效的条目——后者会让"重放不执行"的承诺在表满时失效。
                if (_entries.Count >= MaxEntries)
                    return;

                _entries[key] = new Entry
                {
                    FirstSeenUtc = now,
                    Result = result
                };
            }
        }

        /// <summary>清空（停服 / 断言段收尾）</summary>
        public void Clear()
        {
            lock (_gate)
                _entries.Clear();
        }

        private static string KeyOf(string? sessionId, string? requestId)
            => (sessionId ?? string.Empty) + "\u001f" + requestId;

        /// <summary>只清超过 <see cref="Retention"/> 的过期项；窗口内的条目一律保留</summary>
        private void TrimLocked(DateTime now)
        {
            List<string>? dead = null;

            foreach (var pair in _entries)
            {
                if (now - pair.Value.FirstSeenUtc <= Retention)
                    continue;

                (dead ??= new List<string>()).Add(pair.Key);
            }

            if (dead != null)
            {
                foreach (var key in dead)
                    _entries.Remove(key);
            }
        }

        private sealed class Entry
        {
            public DateTime FirstSeenUtc { get; init; }

            public ScadaWebCommandResult Result { get; init; } = ScadaWebCommandResult.Success();
        }
    }
}
