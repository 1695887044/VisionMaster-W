using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// </summary>
    internal static class MotionChecks
    {
        public static void Run()
        {
            Section("[M] 运动控制内核：门禁 / 异步语义 / 软限位 / 急停打断 / 看门狗 / 报警 / 回零");

            RunGateAndAsyncSemantics();
            RunSoftLimitGate();
            RunRelativeSoftLimitGate();
            RunEmergencyInterrupt();
            RunWatchdogAndSafeStop();
            RunAlarmGate();
            RunClearAlarmUnlocksSafeStop();
            RunFlowStopCancelsMotion();
            RunHoming();
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

                fake.FailMove = true;
                using (var failing = Move(0, 10))
                {
                    fake.Enqueue(failing);
                    failing.Completion.Wait(2000);
                }

                Check("前置：假设备命令失败后进入安全停机",
                    WaitFor(() => fake.State == MotionCardState.SafeStopped, 2000),
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

        private static VirtualMotionCard NewCard(bool absoluteEncoder = true, double softLimitMax = 100)
        {
            var card = new VirtualMotionCard(NewDescriptor(absoluteEncoder, softLimitMax))
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
                    Enabled = true,
                    InPosition = true,
                    Alarm = ReportAlarm,
                    AlarmMessage = ReportAlarm ? "模拟：报警未清除" : string.Empty,
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
