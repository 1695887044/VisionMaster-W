using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using Plugin.Motion.Virtual;
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
    /// </summary>
    internal static class MotionChecks
    {
        public static void Run()
        {
            Section("[M] 运动控制内核：门禁 / 异步语义 / 软限位 / 急停打断 / 看门狗 / 报警 / 回零");

            RunGateAndAsyncSemantics();
            RunSoftLimitGate();
            RunEmergencyInterrupt();
            RunWatchdogAndSafeStop();
            RunAlarmGate();
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

            var cleared = card.ClearAlarm(out var clearError);
            Check("★清除报警后回到可运动状态（不必重启软件）",
                cleared && card.State == MotionCardState.Online,
                $"清报警={cleared} 原因={clearError} 状态={card.State}");

            using var afterClear = Move(0, 5);
            Check("清报警后运动命令恢复正常",
                card.Enqueue(afterClear) == MotionCommandResult.Accepted,
                "返回=" + card.Enqueue(afterClear));
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

        private static MotionDescriptor NewDescriptor(bool absoluteEncoder = true) => new()
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
                new() { LogicalName = "X", PhysicalIndex = 0, UnitsPerMm = 1000, SoftLimitMinMm = -100, SoftLimitMaxMm = 100 },
                new() { LogicalName = "Y", PhysicalIndex = 1, UnitsPerMm = 1000, SoftLimitMinMm = -100, SoftLimitMaxMm = 100 },
            },
        };

        private static VirtualMotionCard NewCard(bool absoluteEncoder = true)
        {
            var card = new VirtualMotionCard(NewDescriptor(absoluteEncoder))
            {
                Log = new StubLog(),
            };
            card.SimulatedAbsoluteEncoder = absoluteEncoder;
            return card;
        }

        private static MotionCommand Move(int axis, double target, double velocity = 200) => new()
        {
            Kind = MotionCommandKind.MoveAbsolute,
            PhysicalAxis = axis,
            LogicalAxis = axis == 0 ? "X" : "Y",
            TargetMm = target,
            VelocityMmPerS = velocity,
            Timeout = TimeSpan.FromSeconds(2),
        };

        private static void EnableAxis(VirtualMotionCard card, int axis)
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
    }
}
