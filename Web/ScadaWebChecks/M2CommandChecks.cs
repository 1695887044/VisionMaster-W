using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Core.Interfaces;
using Newtonsoft.Json;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.ScadaWeb;
using WatsonWebserver.Core.WebSockets;

// 领域层有两个同名快照：剪贴板用的 VisionMaster.Scada.ScadaElementSnapshot（槽位号语义）
// 与渲染器用的 VisionMaster.Scada.Snapshots.ScadaElementSnapshot（caps 填在后者上）。
using ElementSnapshot = VisionMaster.Scada.Snapshots.ScadaElementSnapshot;

namespace ScadaWebChecks
{
    /// <summary>
    /// M2 命令层断言：命令泵 / 权限策略 / 双层限速 / 路由 / 在线列表 / caps / 确认令牌。
    ///
    /// <b>为什么这些断言是"纯内存"的</b>
    /// ---------
    /// 它们要钉住的是"闸门顺序、拒绝原因、过滤结果、串行语义"，而不是 HTTP/WS 的字节形状
    /// （那部分由 <c>Program</c> 的 B 系列覆盖）。起一个真服务再打进去，失败原因会埋在日志里；
    /// 这里直接构造路由与泵，失败时能打印"期望 vs 实际"。
    ///
    /// 覆盖（M2-01 ~ M2-13）：
    ///   M2-01 命令泵串行（20 条按入队顺序执行、无重入），消费者线程无 SynchronizationContext
    ///   M2-02 有 Dispatcher 时命令在 UI 线程执行（线程 Id 与 SynchronizationContext 双判）
    ///   M2-03 每会话入站队列有界：满时 false + E_BUSY，别的会话不受影响
    ///   M2-04 cmd.element 动作副本过滤：Navigate 被剔除、源钩子未被修改、审计记 mirror-mode
    ///   M2-05 cmd.input 写值：走 IScadaValueWriter、审计带旧值→新值
    ///   M2-06 cmd.alarm.ack：单条 / 全部确认各走各的出口
    ///   M2-07 权限拒绝：只读总闸 / 只读会话 / 未登录 / 角色不足 / HighestRoleOnly（含平局）
    ///   M2-08 双层限速：会话 5/s、图元 2/s 跨会话共享、窗口恢复、Reset
    ///   M2-09 caps：快照默认 false/null、按 RequiredRole 与总闸算 canOperate
    ///   M2-10 presence：只给 SessionRef、绝不出现 SessionId；canOperate 与策略同源
    ///   M2-11 确认令牌：绑定三元组、单次、过期、他人/他图元不匹配
    ///   M2-12 版本闸：轮次 / 页 / 页版本不符各回对应码
    ///   M2-13 动作白名单：不在表里跳过并审计；全部被跳过回 E_ACTION_FORBIDDEN；运动默认禁用
    ///
    /// 接线（在 Program.Main 的 Finish() 之前一行）：
    ///   <c>_failed += await M2CommandChecks.RunAsync();</c>
    ///
    /// <b>会话对象的构造</b>：<see cref="ScadaWebSession"/> 的构造器是 internal（只给宿主用），
    /// 断言工程按<b>同一个签名</b>反射调用它。找不到构造器时本条断言会明确变红（不是静默跳过）——
    /// 那条红的意思是"会话形状变了，断言该跟着改"。
    /// </summary>
    internal static class M2CommandChecks
    {
        private static int _failed;
        private static int _total;

        /// <summary>本段通过的条数（Program 用它并入总 PASS 数；只影响计数，不改变任何断言）</summary>
        public static int Pass => _total - _failed;

        /// <summary>本段执行的断言总条数</summary>
        public static int Total => _total;

        /// <summary>跑完全部 M2 命令层断言，返回失败条数（0 = 全绿）</summary>
        public static async Task<int> RunAsync()
        {
            _failed = 0;
            _total = 0;

            Console.WriteLine();
            Console.WriteLine("--- M2CommandChecks：命令泵 / 权限 / 限速 / 路由 / 在线列表 / caps ---");

            try
            {
                await CheckPumpSerialAsync();
                await CheckPumpDispatcherAsync();
                await CheckPumpSessionCapacityAsync();
                CheckElementFilter();
                CheckInputWrite();
                CheckAlarmAck();
                CheckPermissionDenials();
                CheckRateLimiter();
                CheckCaps();
                CheckPresence();
                CheckConfirmTokens();
                CheckVersionGates();
                CheckActionWhitelist();
            }
            catch (Exception ex)
            {
                Check("M2-00", false, $"断言执行异常：{ex.GetType().Name}：{ex.Message}");
            }

            Console.WriteLine($"RESULT: {_total - _failed} PASS / {_failed} FAIL（M2CommandChecks）");
            return _failed;
        }

        // ==================================================================
        //  命令泵
        // ==================================================================

        private static async Task CheckPumpSerialAsync()
        {
            var log = new FakeLog();
            var session = NewSession("张工", ScadaRole.Engineer);

            var order = new List<int>();
            var inHandler = 0;
            var overlap = false;
            SynchronizationContext? contextInHandler = null;
            var handlerThread = 0;

            using var pump = new ScadaWebCommandPump(cmd =>
            {
                contextInHandler ??= SynchronizationContext.Current;
                handlerThread = Environment.CurrentManagedThreadId;

                if (Interlocked.Increment(ref inHandler) != 1)
                    overlap = true;

                order.Add(int.Parse(cmd.RequestId, System.Globalization.CultureInfo.InvariantCulture));
                Thread.Sleep(1);

                Interlocked.Decrement(ref inHandler);
                return ScadaWebCommandResult.Success();
            }, log, uiDispatcher: null, sessionCapacity: 64);

            for (var i = 0; i < 20; i++)
            {
                var accepted = pump.Enqueue(session, NewCommand(session, requestId: i.ToString(System.Globalization.CultureInfo.InvariantCulture)));

                if (!accepted)
                {
                    Check("M2-01", false, $"第 {i} 条命令入队被拒（容量 64 不该满）");
                    return;
                }
            }

            var drained = await WaitUntilAsync(() => pump.ProcessedCount == 20, 5000);
            var sequential = order.SequenceEqual(Enumerable.Range(0, 20));

            Check("M2-01",
                drained && sequential && !overlap && contextInHandler == null && handlerThread != 0,
                $"processed={pump.ProcessedCount} 顺序一致={sequential} 重入={overlap} "
                + $"消费者SyncContext={(contextInHandler == null ? "<null>" : contextInHandler.GetType().Name)} "
                + $"消费者线程={handlerThread}");

            await pump.StopAsync();
        }

        private static async Task CheckPumpDispatcherAsync()
        {
            var log = new FakeLog();
            var session = NewSession("李工", ScadaRole.Engineer);

            var (dispatcher, thread) = StartDispatcherThread();

            try
            {
                var handlerThread = 0;
                SynchronizationContext? contextInHandler = null;

                using var pump = new ScadaWebCommandPump(cmd =>
                {
                    handlerThread = Environment.CurrentManagedThreadId;
                    contextInHandler = SynchronizationContext.Current;
                    return ScadaWebCommandResult.Success();
                }, log, dispatcher, sessionCapacity: 8);

                var accepted = pump.Enqueue(session, NewCommand(session, requestId: "dispatch"));
                var drained = await WaitUntilAsync(() => pump.ProcessedCount == 1, 5000);

                Check("M2-02",
                    accepted && drained && handlerThread == thread.ManagedThreadId && contextInHandler != null,
                    $"accepted={accepted} processed={pump.ProcessedCount} "
                    + $"handlerThread={handlerThread} dispatcherThread={thread.ManagedThreadId} "
                    + $"SyncContext={(contextInHandler == null ? "<null>" : contextInHandler.GetType().Name)}");

                await pump.StopAsync();
            }
            finally
            {
                try { dispatcher.InvokeShutdown(); } catch { /* 已关闭 */ }
                thread.Join(2000);
            }
        }

        private static async Task CheckPumpSessionCapacityAsync()
        {
            var log = new FakeLog();
            var sessionA = NewSession("A", ScadaRole.Engineer);
            var sessionB = NewSession("B", ScadaRole.Engineer);

            var entered = new ManualResetEventSlim(false);
            var gate = new ManualResetEventSlim(false);

            using var pump = new ScadaWebCommandPump(cmd =>
            {
                entered.Set();
                gate.Wait(3000);
                return ScadaWebCommandResult.Success();
            }, log, uiDispatcher: null, sessionCapacity: 4);

            try
            {
                var first = pump.Enqueue(sessionA, NewCommand(sessionA, requestId: "inflight"));
                entered.Wait(3000);   // 保证第一条已被消费者取走（在途），后续 4 条才是"排队"

                var accepted = 0;
                for (var i = 0; i < 4; i++)
                {
                    if (pump.Enqueue(sessionA, NewCommand(sessionA, requestId: "queued-" + i)))
                        accepted++;
                }

                var sixth = pump.Enqueue(sessionA, NewCommand(sessionA, requestId: "overflow"), out var rejection);
                var otherSession = pump.Enqueue(sessionB, NewCommand(sessionB, requestId: "other"));

                gate.Set();
                await WaitUntilAsync(() => pump.ProcessedCount >= 6, 5000);

                Check("M2-03",
                    first && accepted == 4 && !sixth && otherSession
                    && rejection?.Code == ScadaWebErrorCodes.Busy,
                    $"首条={first} 排队接收={accepted}/4 第 6 条={sixth}（code={rejection?.Code}）"
                    + $" 其它会话={otherSession} processed={pump.ProcessedCount}");
            }
            finally
            {
                gate.Set();
                await pump.StopAsync();
            }
        }

        // ==================================================================
        //  cmd.element / cmd.input / cmd.alarm.ack
        // ==================================================================

        private static void CheckElementFilter()
        {
            using var world = new World();

            var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
            hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "按下" });
            hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Navigate, TargetPageName = "另一页" });
            hook.Actions.Add(new ScadaAction { Type = ScadaActionType.WriteVariable, VariableName = "启动", Value = "1" });
            world.Element.EventHooks.Add(hook);

            var session = world.RegisterSession("操作员甲", ScadaRole.Operator);
            var result = world.Router.Handle(world.ElementCommand(session, ScadaEventType.Pressed));

            ScadaEventHook? dispatchedHook = world.Dispatcher.Calls.Count == 1
                ? world.Dispatcher.Calls[0].Hook
                : null;

            var dispatchedTypes = dispatchedHook?.Actions.Select(a => a.Type).ToArray()
                                  ?? Array.Empty<ScadaActionType>();

            var sourceUnchanged = hook.Actions.Count == 3
                                  && hook.Actions.Any(a => a.Type == ScadaActionType.Navigate);

            var mirrorAudit = world.Audit.Rows.Any(r =>
                r.Detail.Contains("mirror-mode", StringComparison.Ordinal));

            Check("M2-04",
                result.Ok && dispatchedHook != null
                && dispatchedTypes.SequenceEqual(new[] { ScadaActionType.Log, ScadaActionType.WriteVariable })
                && dispatchedHook.RequireConfirmation == false
                && sourceUnchanged
                && mirrorAudit
                && world.Audit.Rows.Any(r => r.Result == "成功"),
                $"ok={result.Ok} 分发次数={world.Dispatcher.Calls.Count} "
                + $"副本动作=[{string.Join(",", dispatchedTypes)}] 源钩子未改={sourceUnchanged} "
                + $"mirror审计={mirrorAudit} code={result.Code}");
        }

        private static void CheckInputWrite()
        {
            using var world = new World();

            world.Element.Bindings.Add(new ScadaBinding { TargetProperty = "Value", IsEnabled = true });
            world.Element.Properties["Value"] = "10";

            var session = world.RegisterSession("操作员乙", ScadaRole.Operator);
            var command = new ScadaWebCommand
            {
                Kind = ScadaWebCommandKinds.Input,
                RequestId = "input-1",
                Session = session,
                ElementId = world.Element.ElementId.ToString("D"),
                Property = "Value",
                Value = "42",
                PageId = world.Page.PageId.ToString("D"),
                PageVersion = world.Page.Version,
                RunGeneration = world.Hub.RunGeneration
            };

            var result = world.Router.Handle(command);

            var writerCall = world.Writer.Calls.Count == 1
                ? world.Writer.Calls[0]
                : default;

            var audit = world.Audit.Rows.FirstOrDefault(r => r.Result == "成功");

            Check("M2-05",
                result.Ok && world.Writer.Calls.Count == 1
                && writerCall.Text == "42" && writerCall.Property == "Value"
                && audit != null && audit.Detail.Contains("10 → 42", StringComparison.Ordinal),
                $"ok={result.Ok} 写入器调用={world.Writer.Calls.Count} "
                + $"property={writerCall.Property} text={writerCall.Text} 审计明细={audit?.Detail ?? "<无>"}");

            // 没绑变量（或绑定停用）→ E_NOT_OPERABLE，且绝不调用写入器。
            // 注意：改模型会把 Page.Version 推进，所以命令要重新构造（否则先撞版本闸，测不到目标分支）。
            world.Element.Bindings[0].IsEnabled = false;

            var rejected = world.Router.Handle(new ScadaWebCommand
            {
                Kind = ScadaWebCommandKinds.Input,
                RequestId = "input-2",
                Session = session,
                ElementId = world.Element.ElementId.ToString("D"),
                Property = "Value",
                Value = "43",
                PageId = world.Page.PageId.ToString("D"),
                PageVersion = world.Page.Version,
                RunGeneration = world.Hub.RunGeneration
            });

            Check("M2-05b",
                !rejected.Ok && rejected.Code == ScadaWebErrorCodes.NotOperable && world.Writer.Calls.Count == 1,
                $"code={rejected.Code} message={rejected.Message} 写入器调用={world.Writer.Calls.Count}");
        }

        private static void CheckAlarmAck()
        {
            using var world = new World();
            var session = world.RegisterSession("操作员丙", ScadaRole.Operator);

            var alarmId = Guid.NewGuid();

            var single = world.Router.Handle(new ScadaWebCommand
            {
                Kind = ScadaWebCommandKinds.AlarmAck,
                RequestId = "ack-1",
                Session = session,
                ElementId = world.Element.ElementId.ToString("D"),
                AlarmId = alarmId,
                PageId = world.Page.PageId.ToString("D"),
                PageVersion = world.Page.Version,
                RunGeneration = world.Hub.RunGeneration
            });

            var all = world.Router.Handle(new ScadaWebCommand
            {
                Kind = ScadaWebCommandKinds.AlarmAck,
                RequestId = "ack-all",
                Session = session,
                ElementId = world.Element.ElementId.ToString("D"),
                All = true,
                PageId = world.Page.PageId.ToString("D"),
                PageVersion = world.Page.Version,
                RunGeneration = world.Hub.RunGeneration
            });

            Check("M2-06",
                single.Ok && all.Ok
                && world.Alarms.Acked.Count == 1 && world.Alarms.Acked[0] == alarmId
                && world.Alarms.AcknowledgeAllCalls == 1,
                $"singleOk={single.Ok} allOk={all.Ok} 单条确认={world.Alarms.Acked.Count} "
                + $"全部确认调用={world.Alarms.AcknowledgeAllCalls}");

            // 单条确认失败 → 原因逐字上屏（不吞成一句"确认失败"）
            world.Alarms.SingleResult = false;
            world.Alarms.SingleError = "该报警已经确认过了";

            var failed = world.Router.Handle(new ScadaWebCommand
            {
                Kind = ScadaWebCommandKinds.AlarmAck,
                RequestId = "ack-2",
                Session = session,
                ElementId = world.Element.ElementId.ToString("D"),
                AlarmId = alarmId,
                PageId = world.Page.PageId.ToString("D"),
                PageVersion = world.Page.Version,
                RunGeneration = world.Hub.RunGeneration
            });

            Check("M2-06b",
                !failed.Ok && failed.Message == "该报警已经确认过了"
                && world.Audit.Rows.Any(r => r.Result == "失败" && r.Detail.Contains("已经确认", StringComparison.Ordinal)),
                $"ok={failed.Ok} code={failed.Code} message={failed.Message}");
        }

        // ==================================================================
        //  权限
        // ==================================================================

        private static void CheckPermissionDenials()
        {
            // ① 总闸 AllowCommands=false：即使角色/策略都允许也拒绝
            {
                using var world = new World(allowCommands: false);
                var session = world.RegisterSession("管理员甲", ScadaRole.Administrator);
                var result = world.Router.Handle(world.ElementCommand(session, ScadaEventType.Pressed));

                Check("M2-07a",
                    !result.Ok && result.Code == ScadaWebErrorCodes.Policy
                    && (result.Message ?? string.Empty).Contains("只读模式", StringComparison.Ordinal)
                    && world.Dispatcher.Calls.Count == 0,
                    $"code={result.Code} message={result.Message} 分发={world.Dispatcher.Calls.Count}");
            }

            // ② 只读会话 / ③ 未登录
            {
                using var world = new World();
                var readOnly = world.RegisterSession("只读票", ScadaRole.Engineer, readOnly: true);
                var anonymous = world.RegisterSession(userName: null, role: ScadaRole.Operator, readOnly: true);

                var readOnlyResult = world.Router.Handle(world.ElementCommand(readOnly, ScadaEventType.Pressed));
                var anonymousResult = world.Router.Handle(world.ElementCommand(anonymous, ScadaEventType.Pressed));

                Check("M2-07b",
                    !readOnlyResult.Ok && readOnlyResult.Code == ScadaWebErrorCodes.Policy
                    && !anonymousResult.Ok && anonymousResult.Code == ScadaWebErrorCodes.Unauthenticated,
                    $"只读会话 code={readOnlyResult.Code}；未登录 code={anonymousResult.Code} "
                    + $"message={anonymousResult.Message}");
            }

            // ④ 角色不足（原因逐字来自 ScadaRoleExtensions.Allows）
            {
                using var world = new World();
                world.Element.RequiredRole = ScadaRole.Engineer;
                var session = world.RegisterSession("操作员丁", ScadaRole.Operator);

                var result = world.Router.Handle(world.ElementCommand(session, ScadaEventType.Pressed));

                Check("M2-07c",
                    !result.Ok && result.Code == ScadaWebErrorCodes.Role
                    && (result.Message ?? string.Empty).Contains("工程师", StringComparison.Ordinal)
                    && (result.Message ?? string.Empty).Contains("操作员", StringComparison.Ordinal),
                    $"code={result.Code} message={result.Message}");
            }

            // ⑤ HighestRoleOnly：只有最高档可点；平局都可点；没有已认证会话 → 全员只读
            {
                var settings = new ScadaWebSettings
                {
                    AllowCommands = true,
                    ClickPolicy = ScadaWebClickPolicy.HighestRoleOnly
                };

                var presence = new ScadaWebPresence(settings);
                var policy = new ScadaWebAccessPolicy(settings, presence);

                var engineer = NewSession("工程师", ScadaRole.Engineer);
                var operatorUser = NewSession("操作员", ScadaRole.Operator);
                var admin = NewSession("管理员", ScadaRole.Administrator);

                presence.Register(engineer);
                presence.Register(operatorUser);
                presence.Register(admin);

                var engineerAllowed = policy.CanOperate(engineer, requiredRole: null, out _, out _);
                var operatorAllowed = policy.CanOperate(operatorUser, requiredRole: null, out var oCode, out var oReason);
                var adminAllowed = policy.CanOperate(admin, requiredRole: null, out _, out _);

                // 平局：第二个工程师也能点（最高角色相同）
                var engineer2 = NewSession("工程师2", ScadaRole.Engineer);
                presence.Register(engineer2);
                presence.Unregister(admin);
                var tieAllowed = policy.CanOperate(engineer2, requiredRole: null, out _, out _);

                // 没有已认证会话：只剩匿名观众 → 全员只读
                var emptySettings = new ScadaWebSettings { AllowCommands = true, ClickPolicy = ScadaWebClickPolicy.HighestRoleOnly };
                var emptyPresence = new ScadaWebPresence(emptySettings);
                var emptyPolicy = new ScadaWebAccessPolicy(emptySettings, emptyPresence);
                var anonymous = NewSession(userName: null, role: ScadaRole.Operator, readOnly: false);
                var anonymousAllowed = emptyPolicy.CanOperate(anonymous, requiredRole: null, out _, out _);

                Check("M2-07d",
                    !engineerAllowed && !operatorAllowed && adminAllowed && tieAllowed && !anonymousAllowed
                    && oCode == ScadaWebErrorCodes.Policy
                    && (oReason ?? string.Empty).Contains("管理员", StringComparison.Ordinal),
                    $"在线有管理员时：工程师={engineerAllowed} 管理员={adminAllowed} 操作员={operatorAllowed}"
                    + $"（{oCode}：{oReason}）；管理员离开后两个工程师平局={tieAllowed} 匿名={anonymousAllowed}");
            }

            // M2-07e：AllOperators 的监管项在"用的时候"复核。
            // 模块启动时的夹取会被随后的 ConfigCheck.Load() 原地回写覆盖，
            // 所以判权必须自己再验一次；缺项时回退 HighestRoleOnly（工程师不可点、管理员可点）。
            using (var fallbackWorld = new World(clickPolicy: ScadaWebClickPolicy.AllOperators))
            {
                fallbackWorld.Settings.RequireAudit = false;

                var engineer = fallbackWorld.RegisterSession("工程师回退", ScadaRole.Engineer);
                var admin = fallbackWorld.RegisterSession("管理员回退", ScadaRole.Administrator);

                var engineerOk = fallbackWorld.Policy.CanOperate(engineer, requiredRole: null, out _, out _);
                var adminOk = fallbackWorld.Policy.CanOperate(admin, requiredRole: null, out _, out _);

                Check("M2-07e", !engineerOk && adminOk,
                    $"监管缺项时 AllOperators 回退 HighestRoleOnly：工程师={engineerOk} 管理员={adminOk}");
            }

            // M2-14：租约进 CanOperate（C77 ③④）。
            // AllOperatorsRequiresLease + OperationLeaseEnabled 同时为 true 时，
            // 未持租约的会话必须 E_LEASE_HELD；acquire 后本人可点、他人仍不可点；release 后恢复拒绝。
            using (var leaseWorld = new World(clickPolicy: ScadaWebClickPolicy.AllOperators))
            {
                leaseWorld.Settings.OperationLeaseEnabled = true;
                leaseWorld.Settings.AllOperatorsRequiresLease = true;

                var holder = leaseWorld.RegisterSession("租约A", ScadaRole.Operator);
                var other = leaseWorld.RegisterSession("租约B", ScadaRole.Operator);

                var before = leaseWorld.Policy.CanOperate(holder, requiredRole: null, out var beforeCode, out _);
                var acquired = leaseWorld.Lease.TryAcquire(holder, out var acquireError);
                var holderAfter = leaseWorld.Policy.CanOperate(holder, requiredRole: null, out _, out _);
                var otherAfter = leaseWorld.Policy.CanOperate(other, requiredRole: null, out var otherCode, out _);
                leaseWorld.Lease.Release(holder);
                var afterRelease = leaseWorld.Policy.CanOperate(holder, requiredRole: null, out _, out _);

                Check("M2-14",
                    !before && beforeCode == ScadaWebErrorCodes.LeaseHeld
                    && acquired
                    && holderAfter
                    && !otherAfter && otherCode == ScadaWebErrorCodes.LeaseHeld
                    && !afterRelease,
                    $"未持租约={before}（{beforeCode}） acquire={acquired}（{acquireError ?? "<null>"}） "
                    + $"持有者={holderAfter} 他人={otherAfter}（{otherCode}） 释放后={afterRelease}");
            }
        }

        // ==================================================================
        //  限速
        // ==================================================================

        private static void CheckRateLimiter()
        {
            var now = new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);
            var limiter = new ScadaWebRateLimiter(commandRatePerSecond: 5, elementRatePerSecond: 2, utcNow: () => now);

            var sessionOk = 0;
            for (var i = 0; i < 5; i++)
            {
                if (limiter.TryAcquire("s1", "e-" + i, out _, out _))
                    sessionOk++;
            }

            var sixth = limiter.TryAcquire("s1", "e-5", out var sessionError, out var sessionRetry);
            var otherSession = limiter.TryAcquire("s2", "e-5", out _, out _);

            // 图元级：同一 elementId 跨会话共享（2/s）
            var elementLimiter = new ScadaWebRateLimiter(commandRatePerSecond: 100, elementRatePerSecond: 2, utcNow: () => now);
            var a1 = elementLimiter.TryAcquire("A", "shared", out _, out _);
            var b1 = elementLimiter.TryAcquire("B", "shared", out _, out _);
            var a2 = elementLimiter.TryAcquire("A", "shared", out var elementError, out _);

            // 窗口滑过 1 秒 → 恢复
            now = now.AddMilliseconds(1100);
            var afterWindow = limiter.TryAcquire("s1", "e-5", out _, out _);

            // Reset 清空
            limiter.Reset();

            Check("M2-08",
                sessionOk == 5 && !sixth && otherSession
                && a1 && b1 && !a2
                && afterWindow && limiter.TrackedSessions == 0
                && sessionRetry > 0
                && (sessionError ?? string.Empty).Contains("5", StringComparison.Ordinal)
                && (elementError ?? string.Empty).Contains("2", StringComparison.Ordinal),
                $"会话前 5 条={sessionOk} 第 6 条={sixth}（{sessionError}，retry={sessionRetry}ms）"
                + $" 其它会话={otherSession}；图元 A1={a1} B1={b1} A2={a2}（{elementError}）"
                + $" 1.1s 后恢复={afterWindow} Reset 后会话表={limiter.TrackedSessions}");

            // 路由集成：限速命中时回 E_RATE_LIMITED，且不调分发器
            using var world = new World(commandRatePerSecond: 1, elementRatePerSecond: 1);

            var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
            hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "记录" });
            world.Element.EventHooks.Add(hook);

            var session = world.RegisterSession("限速用户", ScadaRole.Operator);

            var first = world.Router.Handle(world.ElementCommand(session, ScadaEventType.Pressed));
            var second = world.Router.Handle(world.ElementCommand(session, ScadaEventType.Pressed));

            Check("M2-08b",
                first.Ok && !second.Ok && second.Code == ScadaWebErrorCodes.RateLimited
                && world.Dispatcher.Calls.Count == 1,
                $"first={first.Ok} second={second.Code} 分发次数={world.Dispatcher.Calls.Count}");
        }

        // ==================================================================
        //  caps / presence
        // ==================================================================

        private static void CheckCaps()
        {
            var defaults = new ElementSnapshot();

            using var world = new World();
            world.Element.RequiredRole = ScadaRole.Engineer;

            var engineer = world.RegisterSession("工程师甲", ScadaRole.Engineer);
            var operatorUser = world.RegisterSession("操作员戊", ScadaRole.Operator);

            var engineerSnapshot = new ElementSnapshot { RequiredRole = "Engineer" };
            var operatorSnapshot = new ElementSnapshot { RequiredRole = "Engineer" };

            var engineerOk = world.Policy.CanOperate(engineer, engineerSnapshot, out var engineerReason);
            var operatorOk = world.Policy.CanOperate(operatorUser, operatorSnapshot, out var operatorReason);

            // Builder 不填 caps：默认 false/null（"未评估"绝不等于"放行"）
            var pageSnapshot = new VisionMaster.Scada.Snapshots.ScadaPageSnapshotBuilder().Build(world.Runtime);
            var built = pageSnapshot?.Elements.FirstOrDefault(e => e.ElementId == world.Element.ElementId.ToString("D"));

            // 总闸关掉 → 谁都不行
            var closedSettings = new ScadaWebSettings { AllowCommands = false, ClickPolicy = ScadaWebClickPolicy.AllOperators };
            var closedPresence = new ScadaWebPresence(closedSettings);
            var closedPolicy = new ScadaWebAccessPolicy(closedSettings, closedPresence);
            var closedSession = NewSession("工程师乙", ScadaRole.Engineer);
            closedPresence.Register(closedSession);
            var closedOk = closedPolicy.CanOperate(closedSession, engineerSnapshot, out var closedReason);

            Check("M2-09",
                defaults.CanOperate == false && defaults.OperateReason == null
                && engineerOk && !operatorOk
                && (operatorReason ?? string.Empty).Contains("工程师", StringComparison.Ordinal)
                && built != null && !built.CanOperate && built.OperateReason == null
                && !closedOk && (closedReason ?? string.Empty).Contains("只读模式", StringComparison.Ordinal),
                $"默认=({defaults.CanOperate},{defaults.OperateReason ?? "<null>"}) "
                + $"工程师={engineerOk} 操作员={operatorOk}（{operatorReason}） "
                + $"Builder 产出=({built?.CanOperate},{built?.OperateReason ?? "<null>"}) "
                + $"总闸关={closedOk}（{closedReason}） 工程师原因={engineerReason}");
        }

        private static void CheckPresence()
        {
            var settings = new ScadaWebSettings
            {
                AllowCommands = true,
                ClickPolicy = ScadaWebClickPolicy.AllOperators
            };

            var presence = new ScadaWebPresence(settings);

            var engineer = NewSession("张三", ScadaRole.Engineer, ip: "10.1.1.10");
            var operatorUser = NewSession("李四", ScadaRole.Operator, ip: "10.1.1.11");

            presence.Register(engineer);
            presence.Register(operatorUser);

            var snapshot = presence.Snapshot();
            var json = JsonConvert.SerializeObject(snapshot);

            var leakedSessionId = json.Contains(engineer.SessionId, StringComparison.Ordinal)
                                  || json.Contains(operatorUser.SessionId, StringComparison.Ordinal);

            var hasRefs = json.Contains(engineer.SessionRef, StringComparison.Ordinal)
                          && json.Contains(operatorUser.SessionRef, StringComparison.Ordinal);

            var engineerRow = snapshot.FirstOrDefault(e => e.SessionRef == engineer.SessionRef);
            var operatorRow = snapshot.FirstOrDefault(e => e.SessionRef == operatorUser.SessionRef);

            presence.Unregister(operatorUser);
            var afterUnregister = presence.Count;

            Check("M2-10",
                snapshot.Count == 2 && !leakedSessionId && hasRefs
                && engineerRow?.CanOperate == true && operatorRow?.CanOperate == true
                && engineerRow?.Ip == "10.1.1.10" && engineerRow?.Role == "Engineer"
                && engineerRow?.RoleDisplay == "工程师"
                && afterUnregister == 1,
                $"entries={snapshot.Count} 泄漏 SessionId={leakedSessionId} 含 SessionRef={hasRefs} "
                + $"张三 canOperate={engineerRow?.CanOperate} role={engineerRow?.Role}/{engineerRow?.RoleDisplay} "
                + $"ip={engineerRow?.Ip} 摘除后={afterUnregister}");

            // HighestRoleOnly 下普通操作员在列表里就是"不可操作"（与命令路由同源）
            var strictSettings = new ScadaWebSettings
            {
                AllowCommands = true,
                ClickPolicy = ScadaWebClickPolicy.HighestRoleOnly
            };

            var strictPresence = new ScadaWebPresence(strictSettings);
            var strictEngineer = NewSession("王五", ScadaRole.Engineer);
            var strictOperator = NewSession("赵六", ScadaRole.Operator);
            strictPresence.Register(strictEngineer);
            strictPresence.Register(strictOperator);

            var strictRows = strictPresence.Snapshot();
            var strictEngineerRow = strictRows.First(e => e.SessionRef == strictEngineer.SessionRef);
            var strictOperatorRow = strictRows.First(e => e.SessionRef == strictOperator.SessionRef);

            Check("M2-10b",
                strictEngineerRow.CanOperate && !strictOperatorRow.CanOperate
                && (strictOperatorRow.OperateReason ?? string.Empty).Contains("工程师", StringComparison.Ordinal),
                $"工程师={strictEngineerRow.CanOperate} 操作员={strictOperatorRow.CanOperate}"
                + $"（{strictOperatorRow.OperateReason}）");
        }

        // ==================================================================
        //  确认令牌 / 版本闸 / 白名单
        // ==================================================================

        private static void CheckConfirmTokens()
        {
            var now = new DateTime(2026, 10, 21, 11, 0, 0, DateTimeKind.Utc);
            var store = new ScadaWebConfirmTokenStore(lifetimeSeconds: 30, utcNow: () => now);

            var token = store.Issue("s1", "e1", "event:Pressed");

            var wrongSession = store.TryConsume(token, "s2", "e1", "event:Pressed", out var wrongSessionReason);
            var wrongElement = store.TryConsume(token, "s1", "e2", "event:Pressed", out var wrongElementReason);
            var wrongAction = store.TryConsume(token, "s1", "e1", "event:Released", out var wrongActionReason);

            // 不匹配不消耗：正确持有者仍能用
            var firstUse = store.TryConsume(token, "s1", "e1", "event:Pressed", out _);
            var replay = store.TryConsume(token, "s1", "e1", "event:Pressed", out var replayReason);

            var expiredToken = store.Issue("s1", "e1", "event:Pressed");
            now = now.AddSeconds(31);
            var expired = store.TryConsume(expiredToken, "s1", "e1", "event:Pressed", out var expiredReason);

            Check("M2-11",
                !wrongSession && wrongSessionReason == "mismatch:session"
                && !wrongElement && wrongElementReason == "mismatch:element"
                && !wrongAction && wrongActionReason == "mismatch:action"
                && firstUse && !replay && replayReason == "unknown"
                && !expired && expiredReason == "expired",
                $"他人={wrongSessionReason} 他图元={wrongElementReason} 他动作={wrongActionReason} "
                + $"首次={firstUse} 重放={replayReason} 过期={expiredReason}");

            // 路由：危险动作无令牌 → E_CONFIRM；申请令牌后带令牌 → 成功
            using var world = new World(requireConfirmForDangerous: true);
            var session = world.RegisterSession("操作员己", ScadaRole.Operator);

            var dangerous = new ScadaEventHook { Event = ScadaEventType.Pressed };
            dangerous.Actions.Add(new ScadaAction { Type = ScadaActionType.StopFlow, FlowName = "主线" });
            world.Element.EventHooks.Add(dangerous);

            var withoutToken = world.Router.Handle(world.ElementCommand(session, ScadaEventType.Pressed));

            var request = world.Router.Handle(new ScadaWebCommand
            {
                Kind = ScadaWebCommandKinds.ConfirmRequest,
                RequestId = "confirm-1",
                Session = session,
                ElementId = world.Element.ElementId.ToString("D"),
                Event = ScadaEventType.Pressed,
                PageId = world.Page.PageId.ToString("D"),
                PageVersion = world.Page.Version,
                RunGeneration = world.Hub.RunGeneration
            });

            var tokenValue = ReadTokenFromResult(request.Result);

            var withToken = world.Router.Handle(new ScadaWebCommand
            {
                Kind = ScadaWebCommandKinds.Element,
                RequestId = "element-2",
                Session = session,
                ElementId = world.Element.ElementId.ToString("D"),
                Event = ScadaEventType.Pressed,
                ConfirmToken = tokenValue,
                PageId = world.Page.PageId.ToString("D"),
                PageVersion = world.Page.Version,
                RunGeneration = world.Hub.RunGeneration
            });

            Check("M2-11b",
                !withoutToken.Ok && withoutToken.Code == ScadaWebErrorCodes.Confirm
                && request.Ok && !string.IsNullOrWhiteSpace(tokenValue)
                && withToken.Ok && world.Dispatcher.Calls.Count == 1,
                $"无令牌={withoutToken.Code}（{withoutToken.Message}） 申请={request.Ok} "
                + $"tokenLen={tokenValue?.Length ?? 0} 带令牌={withToken.Ok} 分发={world.Dispatcher.Calls.Count}");

            // M2-11c：钩子只勾了"执行前确认"、动作类型本身不危险（只有日志）。
            // 过滤器把副本的 RequireConfirmation 置 false，若危险判据只看副本，这里会漏掉令牌。
            using (var confirmOnly = new World())
            {
                var hook = new ScadaEventHook { Event = ScadaEventType.Pressed, RequireConfirmation = true };
                hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "确认后记录" });
                confirmOnly.Element.EventHooks.Add(hook);

                var operatorUser = confirmOnly.RegisterSession("操作员确认", ScadaRole.Operator);
                var confirmOnlyResult = confirmOnly.Router.Handle(
                    confirmOnly.ElementCommand(operatorUser, ScadaEventType.Pressed));

                Check("M2-11c",
                    !confirmOnlyResult.Ok && confirmOnlyResult.Code == ScadaWebErrorCodes.Confirm
                    && confirmOnly.Dispatcher.Calls.Count == 0,
                    $"confirm-only 无令牌={confirmOnlyResult.Code} 分发={confirmOnly.Dispatcher.Calls.Count}（期望 E_CONFIRM / 0）");
            }
        }

        private static void CheckVersionGates()
        {
            using var world = new World();
            var session = world.RegisterSession("操作员庚", ScadaRole.Operator);

            var staleRun = world.Router.Handle(world.ElementCommand(session, ScadaEventType.Pressed, runGeneration: 999));
            var stalePage = world.Router.Handle(world.ElementCommand(session, ScadaEventType.Pressed, pageId: Guid.NewGuid().ToString("D")));
            var staleVersion = world.Router.Handle(world.ElementCommand(session, ScadaEventType.Pressed, pageVersion: world.Page.Version + 5));

            ScadaWebCommand Bare(string requestId, string? pageId, int pageVersion, long runGeneration) => new()
            {
                Kind = ScadaWebCommandKinds.Element,
                RequestId = requestId,
                Session = session,
                ElementId = world.Element.ElementId.ToString("D"),
                Event = ScadaEventType.Pressed,
                PageId = pageId,
                PageVersion = pageVersion,
                RunGeneration = runGeneration
            };

            var noRun = world.Router.Handle(Bare("gate-no-run",
                world.Page.PageId.ToString("D"), world.Page.Version, 0));
            var noPage = world.Router.Handle(Bare("gate-no-page",
                null, world.Page.Version, world.Hub.RunGeneration));
            var noVersion = world.Router.Handle(Bare("gate-no-version",
                world.Page.PageId.ToString("D"), 0, world.Hub.RunGeneration));

            Check("M2-15",
                noRun.Code == ScadaWebErrorCodes.BadRequest
                && noPage.Code == ScadaWebErrorCodes.BadRequest
                && noVersion.Code == ScadaWebErrorCodes.BadRequest
                && world.Dispatcher.Calls.Count == 0,
                $"缺 runGeneration={noRun.Code} 缺 pageId={noPage.Code} 缺 pageVersion={noVersion.Code} "
                + $"分发={world.Dispatcher.Calls.Count}（三闸必填，缺一 E_BAD_REQUEST）");

            // M2-16 放在 M2-12 之后：它会真正分发一次，先跑会让"版本闸不执行"的计数变脏。
            world.Hub.SetStopped();
            var notRunning = world.Router.Handle(world.ElementCommand(session, ScadaEventType.Pressed));

            Check("M2-12",
                staleRun.Code == ScadaWebErrorCodes.StaleRun
                && stalePage.Code == ScadaWebErrorCodes.StalePage
                && staleVersion.Code == ScadaWebErrorCodes.StalePage
                && notRunning.Code == ScadaWebErrorCodes.StaleRun
                && world.Dispatcher.Calls.Count == 0,
                $"轮次={staleRun.Code} 页={stalePage.Code} 页版本={staleVersion.Code} 未运行={notRunning.Code} "
                + $"分发={world.Dispatcher.Calls.Count}");

            world.Hub.SetRunning(world.Runtime, world.Document, generation: world.Hub.RunGeneration);

            // M2-16：同一 requestId 重放只执行一次（幂等键 = 稳定会话令牌/SessionId）。
            var idempotentHook = new ScadaEventHook { Event = ScadaEventType.Pressed };
            idempotentHook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "只执行一次" });
            world.Element.EventHooks.Add(idempotentHook);

            var first = world.Router.Handle(Bare("dup-1",
                world.Page.PageId.ToString("D"), world.Page.Version, world.Hub.RunGeneration));
            var second = world.Router.Handle(Bare("dup-1",
                world.Page.PageId.ToString("D"), world.Page.Version, world.Hub.RunGeneration));

            Check("M2-16",
                first.Ok && second.Ok && world.Dispatcher.Calls.Count == 1,
                $"first={first.Ok} second={second.Ok} 分发={world.Dispatcher.Calls.Count}（重放不得再执行）");
        }

        private static void CheckActionWhitelist()
        {
            // 白名单只放 Log：StartFlow 被跳过并审计 not-allowed，Log 照常执行
            {
                using var world = new World(
                    allowedActions: new[] { ScadaActionType.Log },
                    requireConfirmForDangerous: false);

                var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "记录" });
                hook.Actions.Add(new ScadaAction { Type = ScadaActionType.StartFlow, FlowName = "主线" });
                world.Element.EventHooks.Add(hook);

                var session = world.RegisterSession("操作员辛", ScadaRole.Operator);
                var result = world.Router.Handle(world.ElementCommand(session, ScadaEventType.Pressed));

                var notAllowedAudit = world.Audit.Rows.Any(r =>
                    r.Detail.Contains("not-allowed", StringComparison.Ordinal));

                var finalRows = world.Audit.Rows.Where(r =>
                    r.Detail.Contains("hooks=", StringComparison.Ordinal)).ToArray();

                var firstFinal = finalRows.Length > 0 ? finalRows[0] : null;

                Check("M2-13d",
                    finalRows.Length == 1
                    && firstFinal != null
                    && firstFinal.Result == "成功"
                    && firstFinal.Detail.Contains("ok=1", StringComparison.Ordinal)
                    && firstFinal.Detail.Contains("skipped=1", StringComparison.Ordinal),
                    $"命令级审计行数={finalRows.Length} 结果={firstFinal?.Result ?? "<无>"} "
                    + $"明细={firstFinal?.Detail ?? "<无>"}（部分成功必须记成功且只有一行）");

                Check("M2-13",
                    result.Ok && world.Dispatcher.Calls.Count == 1
                    && world.Dispatcher.Calls[0].Hook.Actions.Count == 1
                    && notAllowedAudit,
                    $"ok={result.Ok} 副本动作={(world.Dispatcher.Calls.Count > 0 ? world.Dispatcher.Calls[0].Hook.Actions.Count : -1)} "
                    + $"not-allowed审计={notAllowedAudit}");
            }

            // 全部被跳过 → E_ACTION_FORBIDDEN（且不调分发器）
            {
                using var world = new World(
                    allowedActions: new[] { ScadaActionType.Log },
                    requireConfirmForDangerous: false);

                var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                hook.Actions.Add(new ScadaAction { Type = ScadaActionType.StartFlow, FlowName = "主线" });
                world.Element.EventHooks.Add(hook);

                var session = world.RegisterSession("操作员壬", ScadaRole.Operator);
                var result = world.Router.Handle(world.ElementCommand(session, ScadaEventType.Pressed));

                Check("M2-13b",
                    !result.Ok && result.Code == ScadaWebErrorCodes.ActionForbidden
                    && world.Dispatcher.Calls.Count == 0,
                    $"code={result.Code} message={result.Message} 分发={world.Dispatcher.Calls.Count}");
            }

            // 运动动作：白名单里有，但 AllowMotionCommands=false → 仍然跳过（两闸都要开）
            {
                using var world = new World(
                    allowedActions: new[] { ScadaActionType.MotionEnable },
                    allowMotionCommands: false,
                    requireConfirmForDangerous: false);

                var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                hook.Actions.Add(new ScadaAction { Type = ScadaActionType.MotionEnable, AxisName = "X" });
                world.Element.EventHooks.Add(hook);

                var session = world.RegisterSession("操作员癸", ScadaRole.Operator);
                var result = world.Router.Handle(world.ElementCommand(session, ScadaEventType.Pressed));

                var allowedSet = world.Router.AllowedActions.Contains(ScadaActionType.MotionEnable);

                Check("M2-13c",
                    !result.Ok && result.Code == ScadaWebErrorCodes.ActionForbidden
                    && !allowedSet && world.Dispatcher.Calls.Count == 0,
                    $"code={result.Code} 生效白名单含运动={allowedSet} 分发={world.Dispatcher.Calls.Count}");
            }

            // A 范围（2026-10-22）：流程启动三类即使显式塞进 allowedActions 也必须被硬排除；
            // 停/暂停是安全方向，保留。这条闸是"Web 轴只读"能成立的前提——流程里可以含运动步骤。
            {
                using var world = new World(
                    allowedActions: new[]
                    {
                        ScadaActionType.StartFlow,
                        ScadaActionType.RunFlowOnce,
                        ScadaActionType.ResumeFlow,
                        ScadaActionType.StopFlow,
                        ScadaActionType.PauseFlow,
                    },
                    allowMotionCommands: true,
                    requireConfirmForDangerous: false);

                var allowed = world.Router.AllowedActions;

                Check("M2-13e",
                    !allowed.Contains(ScadaActionType.StartFlow)
                    && !allowed.Contains(ScadaActionType.RunFlowOnce)
                    && !allowed.Contains(ScadaActionType.ResumeFlow)
                    && allowed.Contains(ScadaActionType.StopFlow)
                    && allowed.Contains(ScadaActionType.PauseFlow),
                    "生效白名单=" + string.Join("、", allowed.OrderBy(a => a)));
            }

            // A 范围：事件动作写"流程触发变量"必须整条拒绝（变量上升沿会启动流程）。
            {
                using var world = new World(
                    allowedActions: new[] { ScadaActionType.Log, ScadaActionType.WriteVariable },
                    requireConfirmForDangerous: false,
                    flowTriggerQuery: new FakeFlowTriggerQuery("启动"));

                var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                hook.Actions.Add(new ScadaAction
                {
                    Type = ScadaActionType.WriteVariable,
                    VariableName = "启动",
                    Value = "1"
                });
                world.Element.EventHooks.Add(hook);

                var session = world.RegisterSession("操作员子", ScadaRole.Operator);
                var result = world.Router.Handle(world.ElementCommand(session, ScadaEventType.Pressed));

                Check("M2-13f",
                    !result.Ok && result.Code == ScadaWebErrorCodes.FlowTrigger
                    && world.Dispatcher.Calls.Count == 0,
                    $"code={result.Code} message={result.Message} 分发={world.Dispatcher.Calls.Count}");
            }

            // A 范围：cmd.input 写"流程触发变量"必须被拒（不能只封 cmd.element）。
            {
                using var world = new World(
                    allowedActions: new[] { ScadaActionType.Log },
                    requireConfirmForDangerous: false,
                    flowTriggerQuery: new FakeFlowTriggerQuery("启动"));

                var input = new ScadaElement { TypeKey = "Hmi.TextField", Name = "输入框" };
                input.Bindings.Add(new ScadaBinding
                {
                    TargetProperty = "Value",
                    VariableName = "启动",
                    IsEnabled = true
                });
                world.Page.Elements.Add(input);

                var session = world.RegisterSession("操作员丑", ScadaRole.Operator);
                var command = new ScadaWebCommand
                {
                    Kind = ScadaWebCommandKinds.Input,
                    RequestId = Guid.NewGuid().ToString("N"),
                    Session = session,
                    ElementId = input.ElementId.ToString("D"),
                    Property = "Value",
                    Value = "1",
                    PageId = world.Page.PageId.ToString("D"),
                    PageVersion = world.Page.Version,
                    RunGeneration = world.Hub.RunGeneration
                };

                var result = world.Router.Handle(command);

                Check("M2-13g",
                    !result.Ok && result.Code == ScadaWebErrorCodes.FlowTrigger
                    && world.Writer.Calls.Count == 0,
                    $"code={result.Code} message={result.Message} 写入={world.Writer.Calls.Count}");
            }

            // cmd.result 的只读查询：执行过的命令可按 requestId 查到结果；
            // 查询本身**不删除**幂等表条目——否则一次查询会让随后的同 id 重试
            // 变成"新命令"而再执行一遍（这正是 cmd.result 存在的意义：查完还要能重放）。
            {
                using var world = new World(
                    allowedActions: new[] { ScadaActionType.Log },
                    requireConfirmForDangerous: false);

                var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log });
                world.Element.EventHooks.Add(hook);

                var session = world.RegisterSession("操作员寅", ScadaRole.Operator);
                var command = world.ElementCommand(session, ScadaEventType.Pressed);
                var executed = world.Router.Handle(command);

                var key = session.Session?.Token ?? session.SessionId;
                var firstFound = world.Router.Dedup.TryPeek(
                    key, command.RequestId, out var cached, out var firstSeen);
                var secondFound = world.Router.Dedup.TryPeek(
                    key, command.RequestId, out var cachedAgain, out _);
                var missingFound = world.Router.Dedup.TryPeek(
                    key, "no-such-request", out _, out _);

                Check("M2-13h",
                    executed.Ok
                    && firstFound && secondFound
                    && cached?.Ok == true && cachedAgain?.Ok == true
                    && !missingFound
                    && firstSeen <= DateTime.UtcNow,
                    $"执行={executed.Ok} 首查={firstFound} 复查={secondFound} "
                    + $"未知={missingFound} 缓存={cached?.Code ?? "<null>"}");
            }
        }

        // ==================================================================
        //  测试夹具
        // ==================================================================

        /// <summary>
        /// 一个"正在运行 + 一个按钮图元"的最小世界。每个用例自己 new 一份，互不共享限速表/审计。
        /// </summary>
        private sealed class World : IDisposable
        {
            public World(
                bool allowCommands = true,
                ScadaWebClickPolicy clickPolicy = ScadaWebClickPolicy.AllOperators,
                int commandRatePerSecond = 100,
                int elementRatePerSecond = 100,
                bool allowMotionCommands = false,
                bool requireConfirmForDangerous = true,
                IEnumerable<ScadaActionType>? allowedActions = null,
                IScadaFlowTriggerQuery? flowTriggerQuery = null)
            {
                Settings = new ScadaWebSettings
                {
                    AllowCommands = allowCommands,
                    ClickPolicy = clickPolicy,
                    CommandRatePerSecond = commandRatePerSecond,
                    ElementRatePerSecond = elementRatePerSecond,
                    AllowMotionCommands = allowMotionCommands,
                    RequireConfirmForDangerous = requireConfirmForDangerous,
                    RequireAudit = true,
                    RequireOnlineUsers = true,
                    RequireRateLimit = true,
                    ConfirmTokenSeconds = 30
                };

                Document = new ScadaDocument();
                Page = Document.AddPage("总览");
                Document.SetStartupPage(Page);

                Element = new ScadaElement { TypeKey = "Hmi.Button", Name = "启动按钮" };
                Page.Elements.Add(Element);

                Runtime = new ScadaRuntime(Document);
                Runtime.Start();

                Hub = new FakeRunHub();
                Hub.SetRunning(Runtime, Document, generation: 7);

                Presence = new ScadaWebPresence(Settings);
                Lease = new ScadaWebLease();
                Policy = new ScadaWebAccessPolicy(Settings, Presence, Lease);
                Limiter = new ScadaWebRateLimiter(commandRatePerSecond, elementRatePerSecond);
                Audit = new FakeAudit();

                Dispatcher = new FakeActionDispatcher();
                Writer = new FakeValueWriter();
                Alarms = new FakeAlarmCommands();
                Log = new FakeLog();

                Router = new ScadaWebCommandRouter(
                    Hub,
                    Dispatcher,
                    Writer,
                    Alarms,
                    Policy,
                    Limiter,
                    Audit,
                    Log,
                    Settings,
                    allowedActions,
                    flowTriggerQuery ?? new FakeFlowTriggerQuery());
            }

            public ScadaDocument Document { get; }

            public ScadaPage Page { get; }

            public ScadaElement Element { get; }

            public ScadaRuntime Runtime { get; }

            public FakeRunHub Hub { get; }

            public ScadaWebSettings Settings { get; }

            public ScadaWebPresence Presence { get; }

            public ScadaWebLease Lease { get; }

            public ScadaWebAccessPolicy Policy { get; }

            public ScadaWebRateLimiter Limiter { get; }

            public FakeAudit Audit { get; }

            public FakeActionDispatcher Dispatcher { get; }

            public FakeValueWriter Writer { get; }

            public FakeAlarmCommands Alarms { get; }

            public FakeLog Log { get; }

            public ScadaWebCommandRouter Router { get; }

            public ScadaWebSession RegisterSession(
                string? userName,
                ScadaRole role,
                bool readOnly = false,
                string ip = "10.0.0.5")
            {
                var session = NewSession(userName, role, readOnly, ip);
                Presence.Register(session);
                return session;
            }

            public ScadaWebCommand ElementCommand(
                ScadaWebSession session,
                ScadaEventType eventType,
                string? pageId = null,
                int? pageVersion = null,
                long? runGeneration = null)
                => new()
                {
                    Kind = ScadaWebCommandKinds.Element,
                    RequestId = Guid.NewGuid().ToString("N"),
                    Session = session,
                    ElementId = Element.ElementId.ToString("D"),
                    Event = eventType,
                    PageId = pageId ?? Page.PageId.ToString("D"),
                    PageVersion = pageVersion ?? Page.Version,
                    RunGeneration = runGeneration ?? Hub.RunGeneration
                };

            public void Dispose()
            {
                try { Runtime.Stop(); } catch { /* 收尾失败没有补救动作 */ }
            }
        }

        /// <summary>
        /// 造一条会话。构造器是 internal（只给宿主用），这里按<b>同一签名</b>反射调用；
        /// 找不到就抛，让断言明确变红而不是静默少测一条。
        ///
        /// socket 传 <c>null</c>：本组断言只读身份/只读口径/地址，不碰发送通道；
        /// 触碰 <c>ConnectionId</c> 的代码路径在这里不会被走到。
        /// </summary>
        private static ScadaWebSession NewSession(
            string? userName,
            ScadaRole role,
            bool readOnly = false,
            string ip = "10.0.0.5")
        {
            var grant = new ScadaWebTicketGrant
            {
                Ticket = Guid.NewGuid().ToString("N"),
                UserName = userName,
                Role = role,
                ReadOnly = readOnly || userName == null,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
            };

            var ctor = typeof(ScadaWebSession).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(WebSocketSession), typeof(ScadaWebTicketGrant), typeof(string), typeof(string) },
                modifiers: null);

            if (ctor == null)
            {
                throw new InvalidOperationException(
                    "找不到 ScadaWebSession 的内部构造 (WebSocketSession, ScadaWebTicketGrant, string, string)；"
                    + "会话形状变了，本组断言需要同步修改");
            }

            return (ScadaWebSession)ctor.Invoke(new object?[] { null, grant, ip, "ScadaWebChecks" });
        }

        private static ScadaWebCommand NewCommand(ScadaWebSession session, string requestId)
            => new()
            {
                Kind = ScadaWebCommandKinds.Element,
                RequestId = requestId,
                Session = session
            };

        /// <summary>从确认令牌结果里取 token（结果对象由路由以匿名类型返回，按属性名读一次）</summary>
        private static string? ReadTokenFromResult(object? result)
        {
            if (result == null) return null;

            return result.GetType().GetProperty("confirmToken")?.GetValue(result) as string;
        }

        private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs)
        {
            var deadline = Environment.TickCount64 + timeoutMs;

            while (Environment.TickCount64 < deadline)
            {
                if (condition()) return true;
                await Task.Delay(10).ConfigureAwait(false);
            }

            return condition();
        }

        private static (Dispatcher Dispatcher, Thread Thread) StartDispatcherThread()
        {
            Dispatcher? dispatcher = null;
            var ready = new ManualResetEventSlim(false);

            var thread = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "M2CommandChecks.UI"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (!ready.Wait(3000) || dispatcher == null)
                throw new InvalidOperationException("UI Dispatcher 线程未在 3 秒内就绪");

            return (dispatcher, thread);
        }

        private static void Check(string id, bool ok, string detail)
        {
            _total++;
            Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + id + "  " + detail);
            if (!ok) _failed++;
        }

        // ---------------- 测试替身 ----------------

        private sealed class FakeLog : ILogService
        {
            public List<string> Lines { get; } = new();

            public void Success(params string[] messages) { }

            public void Error(params Exception[] messages) { }

            public void Error(params string[] messages)
            {
                lock (Lines) Lines.AddRange(messages);
            }

            public void Info(params string[] messages) { }

            public void Warn(params string[] messages)
            {
                lock (Lines) Lines.AddRange(messages);
            }
        }

        private sealed class FakeRunHub : IScadaRunHub
        {
            private readonly object _gate = new();
            private ScadaRuntime? _session;
            private ScadaDocument? _document;
            private long _generation;

            public bool IsRunning { get { lock (_gate) return _session != null; } }

            public ScadaRuntime? Session { get { lock (_gate) return _session; } }

            public ScadaDocument? Document { get { lock (_gate) return _document; } }

            public long RunGeneration { get { lock (_gate) return _generation; } }

            public event Action<ScadaRuntime, ScadaDocument, long>? RunStarted;

            public event Action<long>? RunStopped;

            public void SetRunning(ScadaRuntime session, ScadaDocument document, long generation)
            {
                lock (_gate)
                {
                    _session = session;
                    _document = document;
                    _generation = generation;
                }

                RunStarted?.Invoke(session, document, generation);
            }

            public void SetStopped()
            {
                long generation;

                lock (_gate)
                {
                    _session = null;
                    _document = null;
                    generation = _generation;
                }

                RunStopped?.Invoke(generation);
            }

            public bool TryGetCurrent(out ScadaRuntime? session, out ScadaDocument? document, out long generation)
            {
                lock (_gate)
                {
                    session = _session;
                    document = _document;
                    generation = _generation;
                    return session != null;
                }
            }
        }

        private sealed class FakeActionDispatcher : IScadaActionDispatcher
        {
            public List<(ScadaEventHook Hook, string? SourceName, string? Actor)> Calls { get; } = new();

            public void Dispatch(ScadaEventHook? hook, string? sourceName, string? operatorName = null)
            {
                if (hook == null) return;

                Calls.Add((hook, sourceName, operatorName));
            }

            /// <summary>
            /// 测试夹具：所有动作都算成功。M2a 终审 P0-3 要求 Router 区分
            /// "分发器没给明细"与"动作成功"，所以这里必须显式给计数。
            /// </summary>
            public ScadaActionDispatchResult DispatchWithResult(
                ScadaEventHook? hook,
                string? sourceName,
                string? operatorName = null)
            {
                Dispatch(hook, sourceName, operatorName);

                var total = hook?.Actions.Count ?? 0;
                return new ScadaActionDispatchResult(total, total, 0, 0);
            }
        }

        /// <summary>测试用流程触发查询：默认不命中；传变量名则命中并给出"测试流程"。</summary>
        private sealed class FakeFlowTriggerQuery : IScadaFlowTriggerQuery
        {
            private readonly HashSet<string> _triggers;

            public FakeFlowTriggerQuery(params string[] triggers)
                => _triggers = new HashSet<string>(
                    triggers ?? Array.Empty<string>(), StringComparer.Ordinal);

            public bool IsFlowTrigger(Guid variableId, string? variableName, out string? flowName)
            {
                if (!string.IsNullOrWhiteSpace(variableName) && _triggers.Contains(variableName!))
                {
                    flowName = "测试流程";
                    return true;
                }

                flowName = null;
                return false;
            }
        }

        private sealed class FakeValueWriter : IScadaValueWriter
        {
            public List<(ScadaElement Element, string Property, string? Text)> Calls { get; } = new();

            public bool Result { get; set; } = true;

            public string? Error { get; set; }

            public bool TryWriteText(ScadaElement element, string targetProperty, string? text, out string? error)
            {
                Calls.Add((element, targetProperty, text));
                error = Result ? null : Error;
                return Result;
            }

            /// <summary>
            /// 测试夹具用图元属性袋充当"运行期变量当前值"（M2-05 把 Value 预置成 "10"）。
            /// 真实实现 <c>ScadaValueWriter</c> 是从值源句柄读，不是属性袋。
            /// </summary>
            public bool TryReadText(ScadaElement element, string targetProperty, out string? text)
            {
                text = null;

                if (element == null || !element.Properties.TryGetValue(targetProperty, out var value))
                    return false;

                text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
        }

        private sealed class FakeAlarmCommands : IScadaAlarmCommands
        {
            public List<Guid> Acked { get; } = new();

            public int AcknowledgeAllCalls { get; private set; }

            public bool SingleResult { get; set; } = true;

            public string? SingleError { get; set; }

            public int AllResult { get; set; } = 1;

            public string? AllError { get; set; }

            public bool TryAcknowledge(Guid alarmId, out string? error)
            {
                Acked.Add(alarmId);
                error = SingleResult ? null : SingleError;
                return SingleResult;
            }

            public int AcknowledgeAll(out string? error)
            {
                AcknowledgeAllCalls++;
                error = AllError;
                return AllResult;
            }
        }

        private sealed record AuditRow(
            string Actor,
            string SessionId,
            string Ip,
            string Subject,
            string EventText,
            string Action,
            string Result,
            string Detail);

        private sealed class FakeAudit : IScadaAuditSink
        {
            public List<AuditRow> Rows { get; } = new();

            public bool Append(
                string actor,
                string sessionId,
                string clientAddress,
                string subject,
                string eventText,
                string action,
                string result,
                string detail)
            {
                Rows.Add(new AuditRow(actor, sessionId, clientAddress, subject, eventText, action, result, detail));
                return true;
            }
        }
    }
}
