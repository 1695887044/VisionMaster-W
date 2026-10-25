using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using Plugin.Motion.Virtual;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 运动控制内核断言（用虚拟卡，无硬件）。
    ///
    /// 为什么这批断言值得单独写
    /// ---------
    /// 运动控制里最容易错、也最难在现场发现的几条，全部落在这层（与具体板卡无关）：
    ///   ① <b>下发 ≠ 到位</b>：把"命令被接受"当成"轴已到位"，会写出看起来并行、实际串行的流程；
    ///   ② <b>急停必须能打断正在执行的命令</b>：只清队列不够 —— 回零卡住时急停等它跑完就不是急停；
    ///   ③ <b>报警中不许运动</b>：对着报警轴下发 Move 是真机上撞机的直接原因；
    ///   ④ <b>失联要安全停机并让位置失效</b>：增量编码器失联后位置不可信，必须要求重新回零。
    /// 这四条都只有真跑一遍（虚拟卡 + 时间推进 + 故障注入）才看得出来。
    ///
    /// 2026-10-22（P0-5）追加的三条安全语义，同样只有真跑才看得出来：
    ///   ⑤ <b>相对运动也受软限位约束</b>：目标 = 当前位置 + 增量，越界拒绝；
    ///   ⑥ <b>流程停止即停轴</b>：引擎在取消令牌之前发 <c>SessionStopRequested</c>（宿主据此 CancelAll）；
    ///   ⑦ <b>清报警解锁安全停机</b>：底层没报警且位置可信才回在线，报警还在就保持安全停机。
    ///
    /// 2026-10-22（P1-A）追加：
    ///   ⑧ <b>运动前置门禁</b>：Move 必须已使能、已回零（或该轴绝对编码器）、未在运动中；
    ///      Home 只挡"正在运动"；Stop/Disable/ClearAlarm 不受影响；
    ///   ⑨ <b>等待到位支持显式目标</b>：Target 有值时按"!Moving &amp;&amp; |pos-target| ≤ 容差"判，
    ///      Target=NaN 时沿用旧的 InPosition 语义（旧流程不受影响）。
    /// </summary>
    internal static class MotionChecks
    {
        public static void Run()
        {
            Section("[M] 运动控制内核：门禁 / 异步语义 / 软限位 / 急停打断 / 看门狗 / 报警 / 回零 / 前置门禁 / 等待到位");

            RunGateAndAsyncSemantics();
            RunSoftLimitGate();
            RunRelativeSoftLimitGate();
            RunEmergencyInterrupt();
            RunWatchdogAndSafeStop();
            RunAlarmGate();
            RunClearAlarmUnlocksSafeStop();
            RunFlowStopCancelsMotion();
            RunHoming();
            RunMotionPreconditions();
            RunUiGateMatchesDeviceLayer();
            RunWaitTargetModes();
        }

        // ==================================================================
        //  ① 门禁 + 异步语义（"下发 ≠ 到位"）
        // ==================================================================

        private static void RunGateAndAsyncSemantics()
        {
            Section("[M] 命令门禁与异步语义（下发 ≠ 到位）");

            using var card = NewCard();

            using var beforeConnect = Move(0, 10);
            Check("未连接时命令被拒绝（不会静默排队等着）",
                card.Enqueue(beforeConnect) == MotionCommandResult.Rejected_NotConnected
                && card.RejectedCommandCount > 0,
                $"返回={card.Enqueue(beforeConnect)} 拒绝数={card.RejectedCommandCount}");

            Check("连接成功且状态为在线",
                card.Connect() && card.State == MotionCardState.Online,
                $"状态={card.State} 说明={card.StateDetail}");

            Check("连接后能力已回填（3 轴 / 8 输入 / 8 输出 / 有硬限位）",
                card.Capabilities.AxisCount == 3
                && card.Capabilities.DigitalInputCount == 8
                && card.Capabilities.DigitalOutputCount == 8
                && card.Capabilities.SupportsHardLimit,
                $"轴={card.Capabilities.AxisCount} DI={card.Capabilities.DigitalInputCount} DO={card.Capabilities.DigitalOutputCount}");

            Check("绝对式编码器：连接后位置即可信（IsHomed=true）",
                card.IsHomed, "IsHomed=" + card.IsHomed);

            // 使能（等完成）
            using (var enable = new MotionCommand
            {
                Kind = MotionCommandKind.Enable,
                PhysicalAxis = 0,
                LogicalAxis = "X",
                WaitsForCompletion = true,
                Timeout = TimeSpan.FromSeconds(2),
            })
            {
                card.Enqueue(enable);
                var commandDone = enable.Completion.Wait(2000) && enable.State == MotionCommandState.Done;
                // 使能状态由状态轮询回填：命令完成 ≠ 快照已刷新（与"下发 ≠ 到位"同一类时序问题）
                var reportedEnabled = WaitFor(() => card.GetAxisStatus(0)?.Enabled == true, 1000);
                Check("轴使能命令完成，且设备回报已使能",
                    commandDone && reportedEnabled,
                    $"命令={enable.State} 设备回报使能={card.GetAxisStatus(0)?.Enabled}");
            }

            // ★ 核心：Move 下发成功 ≠ 已到位
            using var move = new MotionCommand
            {
                Kind = MotionCommandKind.MoveAbsolute,
                PhysicalAxis = 0,
                LogicalAxis = "X",
                TargetMm = 20,
                VelocityMmPerS = 200,
                Timeout = TimeSpan.FromSeconds(2),
            };

            var accepted = card.Enqueue(move);
            move.Completion.Wait(2000);
            Thread.Sleep(30); // 给状态轮询一两拍，确保读到的是"运动途中"而非下发前的旧快照

            var mid = card.GetAxisStatus(0);
            Check("★Move 下发成功 ≠ 已到位（命令已 Done，但轴仍在途中）",
                accepted == MotionCommandResult.Accepted
                && move.State == MotionCommandState.Done
                && mid is { InPosition: false }
                && mid.PositionMm < 20,
                $"命令={move.State} 到位={mid?.InPosition} 位置={mid?.PositionMm:F2}mm");

            Check("轮询推进后轴真的走到目标（位置 ≈ 20mm 且到位）",
                WaitFor(() => card.GetAxisStatus(0) is { InPosition: true } s && Math.Abs(s.PositionMm - 20) < 0.5, 3000),
                $"位置={(card.GetAxisStatus(0)?.PositionMm ?? double.NaN):F2}mm");

            // 越界轴号：能力已知时必须拦住（防止"配了 3 号轴、卡只有 2 个"这类配置错误）
            using var badAxis = Move(9, 5);
            Check("轴号越界被拒绝（能力已知时按能力校验）",
                card.Enqueue(badAxis) == MotionCommandResult.Rejected_InvalidArgument,
                "返回=" + card.Enqueue(badAxis));
        }

        // ==================================================================
        //  ② 软限位（软件层最后一道防线）
        // ==================================================================

        private static void RunSoftLimitGate()
        {
            Section("[M] 软限位门禁");

            using var card = NewCard();
            card.Connect();
            EnableAxis(card, 0);

            using var over = Move(0, 500);   // 软限位上限 100mm
            var result = card.Enqueue(over);

            Check("目标超出软限位时被拒绝（不让轴往限位方向走）",
                result == MotionCommandResult.Rejected_InvalidArgument,
                $"返回={result} 目标=500mm 软限位上限=100mm");

            Check("软限位拒绝记为 Notice 级（业务上正常，不该当成设备故障）",
                card.LastFault?.Severity == MotionFaultSeverity.Notice,
                $"级别={card.LastFault?.Severity} 说明={card.LastFault?.Suggestion}");

            Check("被软限位拒绝后设备仍是可用的（不进报警/安全停机）",
                card.State == MotionCardState.Online,
                "状态=" + card.State);
        }

        // ==================================================================
        //  ②b 相对运动同样受软限位约束（S5）
        // ==================================================================

        /// <summary>
        /// 相对运动的软限位：目标 = **当前位置 + 增量**。
        ///
        /// 为什么单列一条：旧实现只判 <see cref="MotionCommandKind.MoveAbsolute"/>，
        /// 相对运动可以一路走出软限位（轴在 190、限位 200，"+50" 直接冲到 240）。
        /// 这类漏洞在现场表现为"配了软限位还是撞了"，而软件层一句话都不说。
        /// </summary>
        private static void RunRelativeSoftLimitGate()
        {
            Section("[M] 软限位：相对运动也受约束（目标 = 当前位置 + 增量）");

            using var card = NewCard(softLimitMax: 200);
            card.Connect();
            EnableAxis(card, 0);

            // 把轴摆到 190mm（软限位上限 200），等轮询把快照刷成 190
            card.SetSimulatedPosition(0, 190);
            Check("前置：轴在 190mm（软限位上限 200）",
                WaitFor(() => Math.Abs((card.GetAxisStatus(0)?.PositionMm ?? double.NaN) - 190) < 0.5, 1000),
                $"位置={(card.GetAxisStatus(0)?.PositionMm ?? double.NaN):F2}mm");

            using var over = MoveRelative(0, 50);
            var overResult = card.Enqueue(over);

            Check("★相对 +50 越界被拒绝（190 + 50 = 240 > 200）",
                overResult == MotionCommandResult.Rejected_InvalidArgument,
                $"返回={overResult} 建议={card.LastFault?.Suggestion}");

            Check("相对越界也记 Notice 级（业务上正常，不该当成设备故障）",
                card.LastFault?.Severity == MotionFaultSeverity.Notice,
                $"级别={card.LastFault?.Severity} 说明={card.LastFault?.Suggestion}");

            Check("相对越界被拒后设备仍可用（不进报警/安全停机）",
                card.State == MotionCardState.Online, "状态=" + card.State);

            using var inside = MoveRelative(0, -20);
            var insideResult = card.Enqueue(inside);

            Check("★相对 -20 在限位内被接受（190 - 20 = 170）",
                insideResult == MotionCommandResult.Accepted,
                $"返回={insideResult} 目标≈170mm");

            // 绝对运动分支保持原样（回归：不能因为加了相对分支把绝对判据弄坏）
            using var absolute = Move(0, 500);
            Check("绝对运动越界仍然被拒绝（回归）",
                card.Enqueue(absolute) == MotionCommandResult.Rejected_InvalidArgument,
                "返回=" + card.Enqueue(absolute));
        }

        // ==================================================================
        //  ③ 急停：既要清队列，也要能打断正在执行的命令
        // ==================================================================

        private static void RunEmergencyInterrupt()
        {
            Section("[M] 急停：清空队列 + 打断正在执行的命令");

            using var card = NewCard();
            card.SimulatedHomeDurationMs = 500;   // 让回零占住命令线程足够久
            card.Connect();
            EnableAxis(card, 0);
            EnableAxis(card, 1);

            // 用回零占住命令线程（回零是 WaitsForCompletion 命令）
            using var home = new MotionCommand
            {
                Kind = MotionCommandKind.Home,
                PhysicalAxis = 1,
                LogicalAxis = "Y",
                HomeMode = HomeMode.NegativeLimitIndex,
                WaitsForCompletion = true,
                Timeout = TimeSpan.FromSeconds(10),
            };
            card.Enqueue(home);
            Thread.Sleep(80);   // 确认它已在执行

            // 排两条会在回零之后执行的 Move
            using var queued1 = Move(0, 30, velocity: 50);
            using var queued2 = Move(0, 40, velocity: 50);
            card.Enqueue(queued1);
            card.Enqueue(queued2);

            Check("回零执行期间，后续命令在排队等待",
                card.PendingCommandCount >= 2,
                "待执行=" + card.PendingCommandCount);

            var stopResult = card.EmergencyStop();
            Check("急停被接受", stopResult == MotionCommandResult.Accepted, "返回=" + stopResult);

            var homeInterrupted = home.Completion.Wait(1500) && home.State == MotionCommandState.Canceled;
            Check("★急停能打断正在执行的命令（回零立刻结束，而不是等它跑完）",
                homeInterrupted,
                $"回零状态={home.State} 原因={home.Error}");

            Check("★急停清空待执行队列（排队的 Move 全部被取消）",
                queued1.State == MotionCommandState.Canceled && queued2.State == MotionCommandState.Canceled,
                $"q1={queued1.State} q2={queued2.State}");

            Check("急停是预期操作：不记成设备故障、也不进安全停机",
                card.State != MotionCardState.SafeStopped,
                $"状态={card.State} 最近故障={card.LastFault?.Message}");
        }

        // ==================================================================
        //  ④ 看门狗与安全停机（含增量编码器的位置失效）
        // ==================================================================

        private static void RunWatchdogAndSafeStop()
        {
            Section("[M] 看门狗：轮询失联 → 安全停机 + 位置失效");

            using var card = NewCard(absoluteEncoder: false);   // 增量式
            card.Connect();
            EnableAxis(card, 0);

            Check("增量式编码器：连接后位置不可信（IsHomed=false）",
                !card.IsHomed, "IsHomed=" + card.IsHomed);

            RunHome(card, 0);
            Check("回零后 IsHomed=true", card.IsHomed, "IsHomed=" + card.IsHomed);

            // 故障注入：通信中断
            card.SimulatePollException = true;

            Check("★轮询连续失败后进入安全停机（看门狗）",
                WaitFor(() => card.State == MotionCardState.SafeStopped, 3000),
                $"状态={card.State} 说明={card.StateDetail}");

            // 这里注入的是"通信中断"，属于**可恢复**级（提示检查网线后重连）；
            // "需人工复位"留给"能通信但卡不响应命令/伺服报警"那一类。
            // 这个分界正是 MotionFaultSeverity 存在的意义，断言要把它钉准。
            Check("通信类失联记为可恢复级（而不是需人工复位）",
                card.LastFault?.Severity == MotionFaultSeverity.Recoverable,
                $"级别={card.LastFault?.Severity} 说明={card.LastFault?.Message} 建议={card.LastFault?.Suggestion}");

            Check("★增量编码器失联后 IsHomed 归 false（位置不可信，必须重新回零）",
                !card.IsHomed,
                "IsHomed=" + card.IsHomed);

            using var afterStop = Move(0, 5);
            Check("安全停机后不再接受运动命令（不会在不知道轴在哪的情况下继续走）",
                card.Enqueue(afterStop) == MotionCommandResult.Rejected_NotConnected,
                "返回=" + card.Enqueue(afterStop));
        }

        // ==================================================================
        //  ⑤ 报警门禁与清报警
        // ==================================================================

        private static void RunAlarmGate()
        {
            Section("[M] 报警：状态门禁 → 清报警恢复");

            using var card = NewCard();
            card.Connect();
            EnableAxis(card, 0);

            card.SimulateAlarm = true;

            Check("★轮询到伺服报警 → 状态转 Alarm",
                WaitFor(() => card.State == MotionCardState.Alarm, 2000),
                $"状态={card.State} 说明={card.StateDetail}");

            // 先断言报警故障本身：被拒绝的命令也会写 LastFault，
            // 顺序反了会读到"命令被拒绝"的那条，把报警故障盖掉
            Check("报警故障为需人工复位级（必须人工确认，不能自动恢复）",
                card.LastFault?.Severity == MotionFaultSeverity.RequiresReset,
                $"级别={card.LastFault?.Severity} 说明={card.LastFault?.Message}");

            using var blocked = Move(0, 5);
            var blockedResult = card.Enqueue(blocked);
            Check("★报警态拒绝运动命令（不会对着报警中的轴下发 Move）",
                blockedResult == MotionCommandResult.Rejected_NotConnected,
                "返回=" + blockedResult);

            // ★ 安全动作在报警态必须放行：停止 / 失能是"让设备停下"，
            //   把它们挡在门禁后面等于"越危险越不发"（旧实现用 CheckDevice，只认 Online）。
            using (var stop = Stop(0))
            {
                var accepted = card.Enqueue(stop);
                var done = accepted == MotionCommandResult.Accepted && stop.Completion.Wait(2000);
                Check("★报警态仍接受停止命令（停是安全动作，不能被门禁挡）",
                    done && stop.State == MotionCommandState.Done,
                    $"返回={accepted} 状态={stop.State} 原因={stop.Error}");
            }

            using (var disable = Disable(0))
            {
                var accepted = card.Enqueue(disable);
                var done = accepted == MotionCommandResult.Accepted && disable.Completion.Wait(2000);
                Check("★报警态仍接受失能命令（清报警前往往要先失能）",
                    done && disable.State == MotionCommandState.Done,
                    $"返回={accepted} 状态={disable.State} 原因={disable.Error}");
            }

            using (var enable = Enable(0))
            {
                var rejected = card.Enqueue(enable);
                Check("★报警态仍拒绝使能（必须先清报警，避免带着报警上电）",
                    rejected == MotionCommandResult.Rejected_NotConnected,
                    "返回=" + rejected);
            }

            var cleared = card.ClearAlarm(out var clearError);
            Check("★清除报警后回到可运动状态（不必重启软件）",
                cleared && card.State == MotionCardState.Online,
                $"清报警={cleared} 原因={clearError} 状态={card.State}");

            // 上面的失能断言把轴关了；清报警后要先重新使能，否则下面的 Move 会以
            // "轴未使能"失败，基类会把卡打进 SafeStopped，后面的单轴清报警就全被门禁拒了。
            EnableAxis(card, 0);

            using var afterClear = Move(0, 5);
            var afterClearResult = card.Enqueue(afterClear);
            Check("清报警后运动命令恢复正常",
                afterClearResult == MotionCommandResult.Accepted,
                "返回=" + afterClearResult);

            // ★ 单轴清报警：两根轴都报警，只清 0 号，1 号必须还在。
            //   旧实现无视 PhysicalAxis、永远清整卡，这条会红。
            card.SimulateAlarm = false;
            card.SetSimulatedAlarm(0, true);
            card.SetSimulatedAlarm(1, true);

            Check("两根轴都进入报警（准备单轴清除的初始条件）",
                WaitFor(() => card.GetAxisStatus(0)?.Alarm == true
                              && card.GetAxisStatus(1)?.Alarm == true, 2000),
                $"轴0={card.GetAxisStatus(0)?.Alarm} 轴1={card.GetAxisStatus(1)?.Alarm}");

            // 状态机转 Alarm 与轴状态快照是两条线：先等整卡真的进 Alarm，
            // 否则清报警命令可能赶在门禁拉起来之前执行，后面的"仍在 Alarm"断言会读到瞬态。
            Check("整卡进入 Alarm（等待轮询把报警门禁拉起来）",
                WaitFor(() => card.State == MotionCardState.Alarm, 2000),
                $"状态={card.State} 说明={card.StateDetail}");

            using (var clearAxis0 = ClearAlarm(0))
            {
                var accepted = card.Enqueue(clearAxis0);
                var done = accepted == MotionCommandResult.Accepted && clearAxis0.Completion.Wait(2000);
                Check("单轴清报警命令被接受并完成",
                    done && clearAxis0.State == MotionCommandState.Done,
                    $"返回={accepted} 状态={clearAxis0.State} 原因={clearAxis0.Error}");
            }

            Check("★只清 0 号轴：0 号报警消失、1 号报警保留（旧实现会清整卡）",
                WaitFor(() => card.GetAxisStatus(0) is { Alarm: false }
                              && card.GetAxisStatus(1) is { Alarm: true }, 2000),
                $"轴0={card.GetAxisStatus(0)?.Alarm} 轴1={card.GetAxisStatus(1)?.Alarm}");

            Check("清掉 0 号轴后整卡仍在 Alarm（1 号还在报警，不能误放行）",
                WaitFor(() => card.State == MotionCardState.Alarm, 2000),
                $"状态={card.State} 说明={card.StateDetail}");

            using (var clearAxis1 = ClearAlarm(1))
            {
                card.Enqueue(clearAxis1);
                clearAxis1.Completion.Wait(2000);
            }

            Check("★最后一根轴的报警清掉后整卡回到 Online（由轮询确认所有轴无报警）",
                WaitFor(() => card.State == MotionCardState.Online, 2000),
                $"状态={card.State} 说明={card.StateDetail}");
        }

        // ==================================================================
        //  ⑤b 清报警解锁安全停机（S7）
        // ==================================================================

        /// <summary>
        /// 清报警 → 从「已安全停机」回到「在线」。
        ///
        /// 两条判据缺一不可：
        ///  ① 正例：底层确实没报警了 → 放行（旧实现要求"断开再连接"，流程无法自愈）；
        ///  ② 反例：底层还报着警 → **保持安全停机**并返回失败原因（不许绕过真实报警）。
        /// 反例必须用可控的假设备：虚拟卡的清报警会真的把注入开关复位，造不出
        /// "驱动接受了清除请求、报警位却没落"这个组合。
        /// </summary>
        private static void RunClearAlarmUnlocksSafeStop()
        {
            Section("[M] 清报警解锁：安全停机 → 清报警 → 回在线（底层还报警就不放行）");

            // ---------- ① 正例（虚拟卡） ----------
            using (var card = NewCard())
            {
                card.Connect();
                EnableAxis(card, 0);

                // 制造安全停机：让一条运动命令在卡侧失败（基类据此退 SafeStopped）
                card.SimulateCommandFailure = true;
                using (var failing = Move(0, 10))
                {
                    card.Enqueue(failing);
                    failing.Completion.Wait(2000);
                }

                Check("前置：运动命令失败后进入安全停机",
                    WaitFor(() => card.State == MotionCardState.SafeStopped, 2000),
                    $"状态={card.State} 说明={card.StateDetail}");

                using (var blocked = Move(0, 5))
                {
                    var blockedResult = card.Enqueue(blocked);
                    Check("前置：安全停机后运动命令被拒绝",
                        blockedResult == MotionCommandResult.Rejected_NotConnected, "返回=" + blockedResult);
                }

                card.SimulateCommandFailure = false;

                var cleared = card.ClearAlarm(out var clearError);
                Check("★安全停机下清报警被接受（旧实现连命令都进不了队列）",
                    cleared, $"清报警={cleared} 原因={clearError}");

                Check("★清报警成功且底层无报警 → 回到在线",
                    card.State == MotionCardState.Online,
                    $"状态={card.State} 说明={card.StateDetail}");

                Check("恢复在线时清掉那条「安全停机」故障记录（界面不再显示过期建议）",
                    card.LastFault == null, $"LastFault={card.LastFault?.Message}");

                using var again = Move(0, 5);
                var againResult = card.Enqueue(again);
                Check("★恢复后可以再次下发运动命令",
                    againResult == MotionCommandResult.Accepted, "返回=" + againResult);
            }

            // ---------- ② 反例（可控假设备：报警位不随清报警落下） ----------
            var fake = new FakeAlarmCard(NewFakeDescriptor());
            try
            {
                fake.Log = new StubLog();
                fake.Connect();
                EnableAxis(fake, 0);

                // 白盒：这条断言拷的是"底层报警位还在 → 不放行"。
                // 不给底层的报警说明，轮询会把状态说明改写成"轴报警"，前置那条断言
                // 于是拿不到"命令失败后进入安全停机"的证据（2026-10-25 实测两红）。
                fake.AlarmMessage = "模拟：报警未清除";
                fake.FailMove = true;
                using (var failing = Move(0, 10))
                {
                    fake.Enqueue(failing);
                    // 5s，不是 2s：2026-10-22 全量 gate（六个宿主连着构建+跑）在这两处
                    // 超时判红，空载重跑同一条 exe 是 3298 通过 / 0 失败 —— 2s 量的是
                    // 机器负载，不是状态机。WaitFor 条件一满足就返回，绿灯不付这个余量。
                    failing.Completion.Wait(5000);
                }

                Check("前置：假设备命令失败后进入安全停机",
                    WaitFor(() => fake.State == MotionCardState.SafeStopped, 5000),
                    $"状态={fake.State} 说明={fake.StateDetail}");

                fake.FailMove = false;
                fake.ReportAlarm = true;   // 底层报警还挂着（安全停机期间不轮询，只能靠复核现读发现）

                var stubborn = fake.ClearAlarm(out var stubbornError);
                Check("★底层仍报警时清报警不放行：保持安全停机 + 给出原因",
                    !stubborn && fake.State == MotionCardState.SafeStopped
                    && stubbornError.Contains("仍在报警"),
                    $"清报警={stubborn} 状态={fake.State} 原因={stubbornError}");

                // 报警真清掉之后，同一个入口必须能恢复 —— 拒绝不是"永久拉黑"
                fake.ReportAlarm = false;
                var recovered = fake.ClearAlarm(out var recoveredError);
                Check("★底层报警清掉后可以恢复在线（拒绝不是永久的）",
                    recovered && fake.State == MotionCardState.Online,
                    $"清报警={recovered} 状态={fake.State} 原因={recoveredError}");
            }
            finally
            {
                fake.Dispose();
            }
        }

        // ==================================================================
        //  ⑥b 流程停止 → 会话停止事件（S6）
        // ==================================================================

        /// <summary>
        /// 流程停止必须能在"取消令牌之前"通知到订阅方（宿主据此 CancelAll 停轴）。
        ///
        /// 为什么这条不能靠真卡验：运动步骤只下发不等待，流程停止时轴不会自己停 ——
        /// 而"停轴"这件事发生在宿主（MotionModule）里，本断言只钉**引擎这一侧的挂点**：
        /// 事件发不发、发几次、在 Cancel 之前还是之后、正常跑完会不会误发。
        /// </summary>
        private static void RunFlowStopCancelsMotion()
        {
            Section("[M] 流程停止 → SessionStopRequested（停轴钩子的挂点）");

            var runtime = new RuntimeManager();
            var log = new StubLog();
            var workspace = new WorkspaceContext();
            var engine = new FlowEngineService(
                runtime, log, workspace, null, new ResourceLockService(), NullCameraProvider.Instance);

            var fired = 0;
            FlowSession? seen = null;
            var cancelledWhenFired = false;

            engine.SessionStopRequested += (_, session) =>
            {
                fired++;
                seen = session;
                // 判"事件是否在 Cancel 之前"：此刻令牌若已取消，说明顺序反了
                cancelledWhenFired = session?.CancellationTokenSource?.IsCancellationRequested ?? false;
            };

            var cts = new CancellationTokenSource();
            var running = new FlowSession
            {
                FlowName = "断言用流程",
                State = SessionState.Running,
                IsRunning = true,
                CancellationTokenSource = cts,
            };

            engine.StopSession(running);

            Check("★StopSession 触发 SessionStopRequested（流程停止即停轴的唯一挂点）",
                fired == 1 && ReferenceEquals(seen, running),
                $"触发次数={fired} 会话={(seen == null ? "null" : seen.FlowName)}");

            Check("★事件在取消令牌**之前**触发（先停轴，再让流程退栈）",
                !cancelledWhenFired && cts.IsCancellationRequested,
                $"触发时已取消={cancelledWhenFired} 调用后已取消={cts.IsCancellationRequested}");

            engine.StopSession(running);
            Check("重复 StopSession 不再触发（同一次运行只通知一次，不重复下发停轴）",
                fired == 1, "触发次数=" + fired);

            var paused = new FlowSession
            {
                FlowName = "暂停中的流程",
                State = SessionState.Paused,
                IsRunning = true,
                CancellationTokenSource = new CancellationTokenSource(),
            };
            engine.StopSession(paused);
            Check("暂停中的会话同样触发（暂停不是结束，轴还在按最后一条命令走）",
                fired == 2, "触发次数=" + fired);

            var finished = new FlowSession { FlowName = "已跑完的流程", State = SessionState.Stopped, IsRunning = false };
            engine.StopSession(finished);
            Check("正常跑完的会话不触发（跑完的一轮不该去停别人的运动）",
                fired == 2, "触发次数=" + fired);

            var starting = new FlowSession { FlowName = "启动窗口中的会话", State = SessionState.Idle, IsRunning = true };
            engine.StopSession(starting);
            Check("IsRunning 但状态还没到 Running/Paused（启动窗口）不触发",
                fired == 2, "触发次数=" + fired);
        }

        // ==================================================================
        //  ⑥ 回零
        // ==================================================================

        private static void RunHoming()
        {
            Section("[M] 回零：完成即归零 / 不支持的方式当场拦住");

            using var card = NewCard();
            card.Connect();
            EnableAxis(card, 0);

            // 先把轴挪开，再回零，才能看出"回零把它带回了零点"
            using (var move = Move(0, 60, velocity: 500))
            {
                card.Enqueue(move);
                WaitFor(() => card.GetAxisStatus(0) is { InPosition: true }, 2000);
            }

            var homed = RunHome(card, 0);
            Check("回零命令完成",
                homed,
                "状态=" + card.GetAxisStatus(0)?.Describe());

            Check("回零后位置归零（坐标系重建）",
                Math.Abs(card.GetAxisStatus(0)?.PositionMm ?? 99) < 0.5,
                $"位置={(card.GetAxisStatus(0)?.PositionMm ?? double.NaN):F2}mm");

            using var badHome = new MotionCommand
            {
                Kind = MotionCommandKind.Home,
                PhysicalAxis = 0,
                LogicalAxis = "X",
                HomeMode = HomeMode.PositiveLimit,   // 虚拟卡不支持（它支持 3 种）
                WaitsForCompletion = true,
                Timeout = TimeSpan.FromSeconds(2),
            };

            Check("★不支持的回零方式在入队时就被拒绝（而不是运行到一半才失败）",
                card.Enqueue(badHome) == MotionCommandResult.Rejected_NotSupported,
                "返回=" + card.Enqueue(badHome));
        }

        // ==================================================================
        //  ⑦ 运动前置门禁（P1-A）：未使能 / 未回零 / 运动中
        // ==================================================================

        /// <summary>
        /// Move 的三条前置条件与 Home 的一条，全部落在 <c>MotionDeviceBase</c> 的门禁里。
        ///
        /// 为什么必须真跑：这些判据若只写在插件里，UI / SCADA / 流程三处各有一份，
        /// 漂移一次就是"对着未使能的轴下发运动"；而且"使能命令完成 → 快照刷新"之间
        /// 有一个真实窗口（<c>_lastEnableChange</c> 就是为它存在的），只有运行期断言能钉住。
        /// </summary>
        private static void RunMotionPreconditions()
        {
            Section("[M] 运动前置门禁：未使能 / 未回零 / 运动中（P1-A）");

            // ---------- ① 未使能 Move 被拒 ----------
            using (var card = NewCard())   // 绝对编码器：把"未回零"排除，只考"未使能"
            {
                card.Connect();
                Check("前置：快照已刷新且轴未使能",
                    WaitFor(() => card.GetAxisStatus(0) is { Enabled: false }, 1000),
                    $"Enabled={card.GetAxisStatus(0)?.Enabled}");

                using var move = Move(0, 10);
                var result = card.Enqueue(move);

                Check("★未使能时 Move 被拒（设备层门禁，不靠驱动执行时报错）",
                    result == MotionCommandResult.Rejected_NotConnected
                    && card.LastFault?.Suggestion?.Contains("未使能") == true,
                    $"返回={result} 建议={card.LastFault?.Suggestion}");

                Check("未使能拒绝记为 Notice（流程少了一步，不是设备故障）",
                    card.LastFault?.Severity == MotionFaultSeverity.Notice,
                    $"级别={card.LastFault?.Severity}");
            }

            // ---------- ①b 使能命令完成后立刻 Move（跨过"命令完成 → 快照刷新"窗口） ----------
            // 用 200ms 的轮询周期：拿到第一拍"未使能"快照后，使能与 Move 之间不会再插进新快照，
            // 这样才真的考到 _lastEnableChange 的窗口逻辑（5ms 轮询可能恰好刷新，测不出东西）。
            using (var card = NewCard(pollIntervalMs: 200))
            {
                card.Connect();
                WaitFor(() => card.GetAxisStatus(0) is { Enabled: false }, 1500);

                EnableAxis(card, 0);   // 只等命令完成，不再等快照刷新

                using var move = Move(0, 10);
                var result = card.Enqueue(move);

                Check("★使能命令完成后立刻 Move 不被误拒（跨过命令→快照刷新窗口）",
                    result == MotionCommandResult.Accepted,
                    $"返回={result} 建议={card.LastFault?.Suggestion}");
            }

            // ---------- ② 未回零 Move 被拒（增量编码器） ----------
            using (var card = NewCard(absoluteEncoder: false))
            {
                card.Connect();
                EnableAxis(card, 0);

                Check("前置：增量卡已使能但未回零",
                    WaitFor(() => card.GetAxisStatus(0) is { Enabled: true }, 1000) && !card.IsHomed,
                    $"Enabled={card.GetAxisStatus(0)?.Enabled} IsHomed={card.IsHomed}");

                using var move = Move(0, 10);
                var result = card.Enqueue(move);

                Check("★未回零时 Move 被拒（增量编码器位置不可信）",
                    result == MotionCommandResult.Rejected_NotConnected
                    && card.LastFault?.Suggestion?.Contains("未回零") == true,
                    $"返回={result} 建议={card.LastFault?.Suggestion}");

                Check("未回零拒绝记为 Notice（先补一步回零即可，不是设备故障）",
                    card.LastFault?.Severity == MotionFaultSeverity.Notice,
                    $"级别={card.LastFault?.Severity}");

                // 逃生口：每轴 HasAbsoluteEncoder=true 时放行（默认 false，混合编码器机台用）
                card.Descriptor.Axes[0].HasAbsoluteEncoder = true;
                using var move2 = Move(0, 10);
                var result2 = card.Enqueue(move2);
                Check("每轴 HasAbsoluteEncoder=true 时未回零也放行（默认 false 的逃生口）",
                    result2 == MotionCommandResult.Accepted,
                    $"返回={result2} 建议={card.LastFault?.Suggestion}");
            }

            // ---------- ③ 运动中第二条 Move 被拒 ----------
            using (var card = NewCard())
            {
                card.Connect();
                EnableAxis(card, 0);

                using (var first = Move(0, 80, velocity: 20))
                {
                    card.Enqueue(first);
                    Check("前置：第一条 Move 已让轴动起来",
                        WaitFor(() => card.GetAxisStatus(0) is { Moving: true }, 2000),
                        $"Moving={card.GetAxisStatus(0)?.Moving} 位置={card.GetAxisStatus(0)?.PositionMm:F2}");
                }

                using var second = Move(0, 90);
                var result = card.Enqueue(second);

                Check("★运动中第二条 Move 被拒（同一根轴不许叠加目标）",
                    result == MotionCommandResult.Rejected_NotConnected
                    && card.LastFault?.Suggestion?.Contains("正在运动") == true,
                    $"返回={result} 建议={card.LastFault?.Suggestion}");
            }

            // ---------- ④ Home：只挡 Moving，未回零不挡 ----------
            using (var card = NewCard(absoluteEncoder: false))
            {
                card.Connect();
                EnableAxis(card, 0);

                using var home = Home(0);
                var result = card.Enqueue(home);

                Check("★未回零时 Home 不被拒（回零本身就是用来解决未回零的）",
                    result == MotionCommandResult.Accepted,
                    $"返回={result} 建议={card.LastFault?.Suggestion}");

                home.Completion.Wait(3000);
            }

            using (var card = NewCard())
            {
                card.Connect();
                EnableAxis(card, 0);

                using (var first = Move(0, 80, velocity: 20))
                {
                    card.Enqueue(first);
                    WaitFor(() => card.GetAxisStatus(0) is { Moving: true }, 2000);
                }

                using var home = Home(0);
                var result = card.Enqueue(home);

                Check("★运动中 Home 被拒（Home 只拦 Moving，这条正是那一条）",
                    result == MotionCommandResult.Rejected_NotConnected
                    && card.LastFault?.Suggestion?.Contains("正在运动") == true,
                    $"返回={result} 建议={card.LastFault?.Suggestion}");
            }

            // ---------- ⑤ Stop / Disable / ClearAlarm 在未使能/未回零时仍可用 ----------
            using (var card = NewCard(absoluteEncoder: false))
            {
                card.Connect();   // 刻意不使能、不回零

                using (var stop = Stop(0))
                {
                    var accepted = card.Enqueue(stop);
                    var done = accepted == MotionCommandResult.Accepted && stop.Completion.Wait(2000);
                    Check("★未使能/未回零时 Stop 仍可用（安全动作不被前置门禁挡）",
                        done && stop.State == MotionCommandState.Done,
                        $"返回={accepted} 状态={stop.State} 原因={stop.Error}");
                }

                using (var disable = Disable(0))
                {
                    var accepted = card.Enqueue(disable);
                    var done = accepted == MotionCommandResult.Accepted && disable.Completion.Wait(2000);
                    Check("★未使能/未回零时 Disable 仍可用",
                        done && disable.State == MotionCommandState.Done,
                        $"返回={accepted} 状态={disable.State} 原因={disable.Error}");
                }

                var cleared = card.ClearAlarm(out var clearError);
                Check("★未使能/未回零时 ClearAlarm 仍可用",
                    cleared, $"清报警={cleared} 原因={clearError} 状态={card.State}");
            }

            // ---------- ⑥ 普通打断（Stop）之后 Home 仍能执行 ----------
            using (var card = NewCard(absoluteEncoder: false))
            {
                card.Connect();
                EnableAxis(card, 0);

                using (var stop = Stop(0))
                {
                    card.Enqueue(stop);
                    stop.Completion.Wait(2000);
                }

                using var home = Home(0);
                var accepted = card.Enqueue(home);
                var done = accepted == MotionCommandResult.Accepted && home.Completion.Wait(3000);

                Check("★Stop 之后 Home 仍能执行（打断令牌不会污染后续命令）",
                    done && home.State == MotionCommandState.Done,
                    $"返回={accepted} 状态={home.State} 原因={home.Error}");
            }

            // ---------- ⑦ Home 不要求已使能（假设备：状态位报未使能，Home 本身成功） ----------
            var fake = new FakeAlarmCard(NewFakeDescriptor()) { Log = new StubLog() };
            try
            {
                fake.ReportEnabled = false;
                fake.Connect();

                Check("前置：假设备在线但轴未使能",
                    WaitFor(() => fake.GetAxisStatus(0) is { Enabled: false }, 1000),
                    $"Enabled={fake.GetAxisStatus(0)?.Enabled}");

                using var home = Home(0, HomeMode.Origin);
                var accepted = fake.Enqueue(home);
                var done = accepted == MotionCommandResult.Accepted && home.Completion.Wait(2000);

                Check("★Home 不受「未使能」门禁影响（是否使能由驱动在回零里判定）",
                    done && home.State == MotionCommandState.Done,
                    $"返回={accepted} 状态={home.State} 原因={home.Error}");
            }
            finally
            {
                fake.Dispose();
            }
        }

        // ==================================================================
        //  ⑦b 界面/SCADA 门禁（MotionCommandGate）与设备层同口径
        // ==================================================================

        /// <summary>
        /// <c>MotionCommandGate</c> 是界面与 SCADA 动作表的**预检**，
        /// <c>MotionDeviceBase.CheckGateLocked</c> 是设备层的**硬门禁**。两处判据必须一致 ——
        /// 不一致就会出现「界面/SCADA 拒绝、设备层放行」这种自相矛盾的现场表现
        /// （2026-10-22 用户裁决 B2：绝对值编码器算已经回零）。
        ///
        /// 口径（**以设备层为准**，预检向它对齐）：
        /// <c>IsHomed || 卡级 SupportsAbsoluteEncoder || 该轴 HasAbsoluteEncoder</c>。
        ///
        /// 一正一反都要有，缺一不可：
        ///   · 正 —— 该轴 <c>HasAbsoluteEncoder=true</c> 时未回零也放行（**真跑**，这是混合编码器机台
        ///     真正会用到的那条逃生口）；
        ///   · 反 —— 普通增量轴未回零**仍拒绝**且是原文案（防"为了过正面把门禁整条拆掉"）。
        ///
        /// 卡级 <c>SupportsAbsoluteEncoder</c> 那一支**行为上造不出来**：基类 <c>Connect()</c> 会把
        /// <c>IsHomed</c> 直接置成该能力位，能力位为真时 IsHomed 必然为真。所以它只能靠源码判据钉住
        /// （见 ③），防后人把判据"简化"成只剩 IsHomed。
        /// </summary>
        private static void RunUiGateMatchesDeviceLayer()
        {
            Section("[M] 界面/SCADA 门禁（MotionCommandGate）与设备层同口径：绝对编码器 = 已回零");

            // ---------- ① 反例：增量编码器 + 未回零 → 仍拒绝（原文案） ----------
            using (var card = NewCard(absoluteEncoder: false))
            {
                card.Connect();
                EnableAxis(card, 0);

                var enabled = WaitFor(() => card.GetAxisStatus(0) is { Enabled: true }, 1000);
                var axis = card.Descriptor.FindAxis(0)!;
                var status = card.GetAxisStatus(0);

                Check("前置：增量卡已使能、未回零（IsHomed=false）",
                    enabled && status is { Enabled: true } && !card.IsHomed,
                    $"Enabled={status?.Enabled} IsHomed={card.IsHomed}");

                var rejected = MotionCommandGate.CheckMove(card, axis, status, 10);
                Check("★普通轴未回零 → CheckMove 仍拒绝，且是原文案（反向判据：门禁没被拆掉）",
                    !rejected.Passed
                    && rejected.Reason.Contains("需要回零后才能运动", StringComparison.Ordinal),
                    $"放行={rejected.Passed} 原因={rejected.Reason}");

                // ---------- ② 每轴逃生口：HasAbsoluteEncoder=true → 放行 ----------
                // 与设备层 CheckMotionPreconditionsLocked 的 `mapping?.HasAbsoluteEncoder == true` 同一口径。
                axis.HasAbsoluteEncoder = true;
                var perAxis = MotionCommandGate.CheckMove(card, axis, status, 10);
                Check("★该轴 HasAbsoluteEncoder=true 时未回零也放行（每轴逃生口与设备层同口径）",
                    perAxis.Passed,
                    $"放行={perAxis.Passed} 原因={perAxis.Reason}");
            }

            // ---------- ③ 卡级能力位：只能用源码判据钉住（行为上造不出来） ----------
            // 为什么不能像 ①② 那样真跑：基类 Connect() 里有一行
            //     _isHomed = _capabilities.SupportsAbsoluteEncoder;
            // 只要卡级能力位为 true，连上之后 IsHomed 一定是 true ——
            // 「能力位 true 且 IsHomed false」在 MotionDeviceBase 的现有实现上**根本造不出来**
            // （这正是 ZMotionCard 把该能力位硬编码 false 的原因，见驱动注释 S4/2026-10-22）。
            // 所以卡级那一支是**防御性判据**：意义在于与设备层逐字同口径，而不是某天真会被触发。
            // 这里用源码判据钉住"三支都在、顺序与设备层一致"，防的是后人把它"简化"成只剩 IsHomed
            // ——那样"界面拒绝、设备层放行"的漂移就回来了。
            var gateFile = ResolveRepoFile(@"Shard\Core.Interfaces\Motion\MotionCommandGate.cs");
            string gateCompact = gateFile != null
                ? new string(File.ReadAllText(gateFile).Where(c => !char.IsWhiteSpace(c)).ToArray())
                : string.Empty;
            const string expectedGate =
                "device!.IsHomed||device.Capabilities.SupportsAbsoluteEncoder||axis.HasAbsoluteEncoder";
            Check("★CheckMove 的「位置可信」判据三支齐全（IsHomed / 卡级能力位 / 每轴能力位），与设备层同口径",
                gateFile != null && gateCompact.Contains(expectedGate, StringComparison.Ordinal),
                gateFile == null
                    ? @"定位不到 Shard\Core.Interfaces\Motion\MotionCommandGate.cs —— 文件被改名/搬走时这条必须红，不能记成跳过"
                    : gateCompact.Contains(expectedGate, StringComparison.Ordinal)
                        ? "三支齐全"
                        : "判据被改窄了：期望 " + expectedGate);
        }

        // ==================================================================
        //  ⑧ 等待到位：Target / Tolerance 显式模式（P1-A）
        // ==================================================================

        /// <summary>
        /// Target 的两种模式必须都真跑一遍：
        ///   · NaN（旧流程）→ 只认 InPosition &amp;&amp; !Moving；
        ///   · 有值 → 只认 !Moving &amp;&amp; |pos-target| ≤ 容差（不要求 InPosition）。
        /// 另外钉住"等待期间卡进安全停机要立刻失败"，而不是等满超时。
        ///
        /// 这里临时注入 <see cref="MotionAxisResolution"/>（插件解析轴名用），
        /// 用完还原成空注册表 —— 别把一张已释放的卡留给后续断言。
        /// </summary>
        private static void RunWaitTargetModes()
        {
            Section("[M] 等待到位：Target/Tolerance 显式模式（P1-A）");

            using var card = NewCard();   // 绝对编码器：IsHomed=true，把回零门禁排除
            card.Connect();
            EnableAxis(card, 0);

            var registry = new MotionAxisRegistry(() => new[] { card.Descriptor });
            MotionAxisResolution.Attach(registry, new SingleCardMotionProvider(card.Descriptor, card));

            try
            {
                Check("前置：轴静止在 0mm 且 InPosition=true",
                    WaitFor(() => card.GetAxisStatus(0) is { InPosition: true, Moving: false }, 1000),
                    $"InPosition={card.GetAxisStatus(0)?.InPosition} Moving={card.GetAxisStatus(0)?.Moving}");

                // ① Target=NaN → 旧 InPosition 语义
                var legacy = RunWait(card, "X", double.NaN, 0.05, 500);
                Check("★Target=NaN 时沿用旧 InPosition 语义（静止且到位 → 成功）",
                    legacy.Success && Math.Abs(legacy.Position) < 0.5,
                    $"成功={legacy.Success} 位置={legacy.Position:F3} 错误={legacy.Error}");

                // ② Target 有值且到位 → 成功
                using (var move = Move(0, 20, velocity: 200))
                {
                    card.Enqueue(move);
                    Check("前置：轴走到 20mm 并停稳",
                        WaitFor(() => card.GetAxisStatus(0) is { InPosition: true, Moving: false } s
                                       && Math.Abs(s.PositionMm - 20) < 0.5, 3000),
                        $"位置={card.GetAxisStatus(0)?.PositionMm:F2} Moving={card.GetAxisStatus(0)?.Moving}");
                }

                var hit = RunWait(card, "X", 20, 0.05, 2000);
                Check("★Target 有值且到位 → 成功（位置判据，不依赖 InPosition）",
                    hit.Success && Math.Abs(hit.Position - 20) <= 0.1,
                    $"成功={hit.Success} 位置={hit.Position:F3} 错误={hit.Error}");

                // ③ Target 有值但超差 → 超时
                var miss = RunWait(card, "X", 30, 0.05, 150);
                Check("★Target 有值但超差 → 超时（报错里带目标/容差/偏差）",
                    !miss.Success && miss.Error.Contains("超时") && miss.Error.Contains("偏差"),
                    $"成功={miss.Success} 错误={miss.Error}");

                // ④ 等待期间卡进安全停机 → 立刻失败，不等满超时
                using (var faultCard = NewCard())
                {
                    faultCard.Connect();
                    EnableAxis(faultCard, 0);

                    var faultRegistry = new MotionAxisRegistry(() => new[] { faultCard.Descriptor });
                    MotionAxisResolution.Attach(
                        faultRegistry, new SingleCardMotionProvider(faultCard.Descriptor, faultCard));

                    var watch = Stopwatch.StartNew();
                    var task = Task.Run(() => RunWait(faultCard, "X", 80, 0.05, 5000));
                    Thread.Sleep(50);
                    faultCard.SimulatePollException = true;   // 看门狗 → SafeStopped

                    bool completed;
                    WaitProbe stateFail;
                    try
                    {
                        completed = task.Wait(3000);
                        stateFail = completed ? task.Result : default(WaitProbe);
                    }
                    catch (AggregateException ex)
                    {
                        // 插件 Execute 已兜住业务异常；这里再兜一层，别让一条断言把后续全带崩
                        completed = true;
                        stateFail = new WaitProbe(false, ex.GetBaseException().Message, 0, 0);
                    }

                    watch.Stop();
                    Check("★等待期间卡进入安全停机 → 立即失败（不必等满超时）",
                        completed && !stateFail.Success && watch.ElapsedMilliseconds < 2000
                        && (stateFail.Error.Contains("不可用") || stateFail.Error.Contains("安全停机")),
                        $"完成={completed} 耗时={watch.ElapsedMilliseconds}ms "
                        + $"成功={stateFail.Success} 错误={stateFail.Error}");
                }
            }
            finally
            {
                // 还原静态注入：空注册表 + NullMotionProvider，后续断言不会再解析到已释放的卡
                MotionAxisResolution.Attach(
                    new MotionAxisRegistry(() => Array.Empty<MotionDescriptor>()),
                    NullMotionProvider.Instance);
            }
        }

        /// <summary>跑一次「等待到位」并收回四个结果（插件 Execute 会兜住异常并落到 Success/ErrorMessage）</summary>
        private static WaitProbe RunWait(
            VirtualMotionCard card, string axis, double target, double tolerance, int timeoutMs)
        {
            var plugin = new Plugin.Motion.Steps.MotionWaitPlugin
            {
                TimeoutMs = timeoutMs,
                PollIntervalMs = 5,
                InstanceName = "断言-等待到位",
            };
            plugin.Axis.Value = axis;
            plugin.Target.Value = target;
            plugin.Tolerance.Value = tolerance;

            var log = new StubLog();
            var ok = plugin.Execute(DataPipelineCheckSupport.NewContext(log));

            return new WaitProbe(
                ok,
                plugin.ErrorMessage.TypedValue ?? string.Empty,
                plugin.Position.TypedValue,
                plugin.ElapsedMs.TypedValue);
        }

        private readonly record struct WaitProbe(bool Success, string Error, double Position, int Elapsed);

        /// <summary>只服务「等待到位」断言的最小 IMotionProvider（一张卡 + 一个设备）</summary>
        private sealed class SingleCardMotionProvider : IMotionProvider
        {
            private readonly MotionDescriptor _descriptor;
            private readonly IMotionDevice _device;

            public SingleCardMotionProvider(MotionDescriptor descriptor, IMotionDevice device)
            {
                _descriptor = descriptor;
                _device = device;
            }

            public IReadOnlyList<MotionDescriptor> Cards => new[] { _descriptor };

            public bool TryGetDevice(Guid cardId, out IMotionDevice device)
            {
                if (cardId == _descriptor.Id)
                {
                    device = _device;
                    return true;
                }

                device = null!;
                return false;
            }

            public bool TryGetByKey(string address, out IMotionDevice device)
            {
                if (string.Equals(address, _descriptor.Address, StringComparison.Ordinal))
                {
                    device = _device;
                    return true;
                }

                device = null!;
                return false;
            }

            public bool TryGetByName(string displayName, out IMotionDevice device)
            {
                if (string.Equals(displayName, _descriptor.DisplayName, StringComparison.Ordinal))
                {
                    device = _device;
                    return true;
                }

                device = null!;
                return false;
            }

            public void ReleaseAll() { }
        }

        // ==================================================================
        //  构造与辅助
        // ==================================================================

        private static MotionDescriptor NewDescriptor(bool absoluteEncoder = true, double softLimitMax = 100) => new()
        {
            Id = Guid.NewGuid(),
            DriverTypeKey = typeof(VirtualMotionCard).AssemblyQualifiedName ?? nameof(VirtualMotionCard),
            DisplayName = "断言用虚拟卡",
            Address = "127.0.0.1:" + Guid.NewGuid().ToString("N").Substring(0, 6),
            Params = new MotionParams
            {
                PollIntervalMs = 5,
                WatchdogTimeoutMs = 200,
                CommandTimeoutMs = 3000,
                DefaultVelocityMmPerS = 200,
                MaxQueueDepth = 16,
            },
            Axes = new List<AxisMapping>
            {
                new() { LogicalName = "X", PhysicalIndex = 0, UnitsPerMm = 1000, SoftLimitMinMm = -100, SoftLimitMaxMm = softLimitMax },
                new() { LogicalName = "Y", PhysicalIndex = 1, UnitsPerMm = 1000, SoftLimitMinMm = -100, SoftLimitMaxMm = softLimitMax },
            },
        };

        private static VirtualMotionCard NewCard(
            bool absoluteEncoder = true, double softLimitMax = 100, int pollIntervalMs = 5)
        {
            var descriptor = NewDescriptor(absoluteEncoder, softLimitMax);
            descriptor.Params.PollIntervalMs = pollIntervalMs;

            var card = new VirtualMotionCard(descriptor)
            {
                Log = new StubLog(),
            };
            card.SimulatedAbsoluteEncoder = absoluteEncoder;
            return card;
        }

        /// <summary>假设备用的卡配置（1 轴、软限位放宽，避免干扰"报警/状态机"这两条断言）</summary>
        private static MotionDescriptor NewFakeDescriptor() => new()
        {
            Id = Guid.NewGuid(),
            DriverTypeKey = "FlowCanvasChecks.MotionChecks+FakeAlarmCard",
            DisplayName = "断言用假运动卡",
            Address = "fake://" + Guid.NewGuid().ToString("N").Substring(0, 6),
            Params = new MotionParams
            {
                PollIntervalMs = 5,
                WatchdogTimeoutMs = 500,
                CommandTimeoutMs = 2000,
                DefaultVelocityMmPerS = 100,
                MaxQueueDepth = 16,
            },
            Axes = new List<AxisMapping>
            {
                new() { LogicalName = "F", PhysicalIndex = 0, UnitsPerMm = 1000, SoftLimitMinMm = -1000, SoftLimitMaxMm = 1000 },
            },
        };

        private static MotionCommand Move(int axis, double target, double velocity = 200) => new()
        {
            Kind = MotionCommandKind.MoveAbsolute,
            PhysicalAxis = axis,
            LogicalAxis = axis == 0 ? "X" : "Y",
            TargetMm = target,
            VelocityMmPerS = velocity,
            Timeout = TimeSpan.FromSeconds(2),
        };

        /// <summary>相对运动命令（目标端口里放的是**增量**，可为负）</summary>
        private static MotionCommand MoveRelative(int axis, double delta, double velocity = 200) => new()
        {
            Kind = MotionCommandKind.MoveRelative,
            PhysicalAxis = axis,
            LogicalAxis = axis == 0 ? "X" : "Y",
            TargetMm = delta,
            VelocityMmPerS = velocity,
            Timeout = TimeSpan.FromSeconds(2),
        };

        /// <summary>回零命令（默认负限位+Index；假设备只支持 Origin 时显式传）</summary>
        private static MotionCommand Home(int axis, HomeMode mode = HomeMode.NegativeLimitIndex) => new()
        {
            Kind = MotionCommandKind.Home,
            PhysicalAxis = axis,
            LogicalAxis = axis == 0 ? "X" : "Y",
            HomeMode = mode,
            WaitsForCompletion = true,
            Retryable = false,
            Timeout = TimeSpan.FromSeconds(5),
        };

        private static MotionCommand Stop(int axis) => new()
        {
            Kind = MotionCommandKind.Stop,
            PhysicalAxis = axis,
            LogicalAxis = axis == 0 ? "X" : "Y",
            StopMode = 2,
            WaitsForCompletion = false,
            Retryable = false,
            Timeout = TimeSpan.FromSeconds(2),
        };

        private static MotionCommand Enable(int axis) => new()
        {
            Kind = MotionCommandKind.Enable,
            PhysicalAxis = axis,
            LogicalAxis = axis == 0 ? "X" : "Y",
            WaitsForCompletion = true,
            Timeout = TimeSpan.FromSeconds(2),
        };

        private static MotionCommand Disable(int axis) => new()
        {
            Kind = MotionCommandKind.Disable,
            PhysicalAxis = axis,
            LogicalAxis = axis == 0 ? "X" : "Y",
            WaitsForCompletion = true,
            Timeout = TimeSpan.FromSeconds(2),
        };

        private static MotionCommand ClearAlarm(int axis) => new()
        {
            Kind = MotionCommandKind.ClearAlarm,
            PhysicalAxis = axis,
            LogicalAxis = axis == 0 ? "X" : "Y",
            WaitsForCompletion = true,
            Retryable = false,
            Timeout = TimeSpan.FromSeconds(2),
        };

        private static void EnableAxis(MotionDeviceBase card, int axis)
        {
            using var enable = new MotionCommand
            {
                Kind = MotionCommandKind.Enable,
                PhysicalAxis = axis,
                WaitsForCompletion = true,
                Timeout = TimeSpan.FromSeconds(2),
            };
            card.Enqueue(enable);
            enable.Completion.Wait(2000);
        }

        private static bool RunHome(VirtualMotionCard card, int axis)
        {
            using var home = new MotionCommand
            {
                Kind = MotionCommandKind.Home,
                PhysicalAxis = axis,
                LogicalAxis = axis == 0 ? "X" : "Y",
                HomeMode = HomeMode.NegativeLimitIndex,
                WaitsForCompletion = true,
                Timeout = TimeSpan.FromSeconds(5),
            };
            card.Enqueue(home);
            return home.Completion.Wait(3000) && home.State == MotionCommandState.Done;
        }

        /// <summary>轮询等待条件成立（时间敏感的断言都用它，避免写死 Sleep 导致偶发红）</summary>
        private static bool WaitFor(Func<bool> condition, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(5);
            }
            return condition();
        }

        /// <summary>
        /// 从输出目录往上找仓库相对路径（断言宿主跑在 bin\… 下，源码不在旁边）。
        /// 取不到返回 null —— 调用方必须**红着说清**是哪个文件没定位到，不许记成"跳过"
        /// （R38：跳过型断言必须能被反证）。
        /// </summary>
        private static string? ResolveRepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        // ==================================================================
        //  断言用假设备
        // ==================================================================

        /// <summary>
        /// 只服务于 S7 反例的假设备：**清报警永远返回成功**，但报警位由测试开关决定是否落下。
        ///
        /// 为什么不用虚拟卡造这个组合：<c>VirtualMotionCard</c> 的卡级清报警会真的把
        /// <c>SimulateAlarm</c> 复位，于是"驱动接受了清除请求、底层报警却还在"这个
        /// 最需要防的场景根本造不出来（那正是"不要绕过真实报警"要守的东西）。
        ///
        /// 另外它刻意声明为绝对编码器：把"位置不可信"这条守卫排除掉，
        /// 让这条断言只考"报警有没有真的清掉"。
        /// </summary>
        private sealed class FakeAlarmCard : MotionDeviceBase
        {
            public FakeAlarmCard(MotionDescriptor descriptor) : base(descriptor) { }

            /// <summary>轮询时是否上报报警（= 底层真实报警位）</summary>
            public volatile bool ReportAlarm;

            /// <summary>上报报警时的报警说明（留空则基类按"轴报警"兜底）</summary>
            public volatile string AlarmMessage = string.Empty;

            /// <summary>
            /// 轮询时上报的使能位。默认 true（既有 S7 断言的前提）；
            /// P1-A 的「Home 不要求已使能」用例把它置 false，用来证明门禁只看 Moving。
            /// </summary>
            public volatile bool ReportEnabled = true;

            /// <summary>下一条运动命令是否失败（把设备打进安全停机用）</summary>
            public volatile bool FailMove;

            protected override bool ConnectCore()
            {
                SetCapabilities(new MotionCapabilities
                {
                    AxisCount = 1,
                    DigitalInputCount = 0,
                    DigitalOutputCount = 0,
                    SupportsHardLimit = true,
                    SupportsAbsoluteEncoder = true,
                    SupportsJog = false,
                    SupportedHomeModes = new[] { HomeMode.Origin },
                });
                MarkHomed(true);
                SetDetail("断言用假运动卡");
                return true;
            }

            protected override void DisconnectCore() { }

            protected override void PollStatusCore(CancellationToken ct)
            {
                UpdateAxisStatus(new AxisStatus
                {
                    PhysicalIndex = 0,
                    PositionMm = 0,
                    Enabled = ReportEnabled,
                    InPosition = true,
                    Alarm = ReportAlarm,
                    AlarmMessage = ReportAlarm ? AlarmMessage : string.Empty,
                });
            }

            protected override MotionCommandOutcome ExecuteCommandCore(
                MotionCommand command, CancellationToken ct, out string error)
            {
                error = string.Empty;

                if (command.Kind == MotionCommandKind.MoveAbsolute && FailMove)
                {
                    error = "模拟：运动命令执行失败";
                    return MotionCommandOutcome.Failed;
                }

                // 清报警"成功"但 ReportAlarm 仍为 true = 驱动接受了请求、报警位没落
                return MotionCommandOutcome.Done;
            }

            protected override void SafeStopCore() { }
        }
    }
}
