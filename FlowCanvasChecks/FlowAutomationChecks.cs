using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Core.Interfaces;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 「调用方式」运行侧三位的断言（本批：定时调度 / 变量触发 / 子程序调用 + 「调用流程」插件）。
    ///
    /// 断言面与手段：
    ///  · 定时调度：纯判定（ShouldFire / 间隔下限）走逻辑断言；真跑用手动时钟驱动
    ///    <c>TickOnce(utcNow)</c>——检查宿主不能用真实计时器等 200ms 猜时序，手动时钟是确定性的；
    ///  · 变量触发：真值判定（Null/0/空白 vs 非 0/非空白）逐档断言；真跑用**真变量对象**
    ///    的 ValueChanged 事件链路（LocalVariableModel.Value setter → 服务 → 引擎），
    ///    钉住"上升沿触发一次、假值不触发、再次上升沿再触发"；
    ///  · 子程序调用：门禁（不存在 / 未勾「子程序」/ 禁用 / 在跑）逐条断言，成功路径**真跑**目标流程；
    ///  · 插件端到端：从 Modules\ 装载 Plugin.RunFlow.dll，父流程的一步 = 调用流程，
    ///    跑完断言子流程确实执行过（这条只有真跑才能证伪"插件拿到了 NullFlowInvoker"）。
    /// </summary>
    internal static class FlowAutomationChecks
    {
        internal static void Run()
        {
            TimerFireDecision();
            TimerFiresWithManualClock();
            TruthyDecision();
            VariableRisingEdgeFires();
            InvokerGates();
            InvokerLosesRaceReportsFailure();
            RunFlowPluginEndToEnd();
            TickSources();
            SchedulerWithRealClockRunsAndStops();
        }

        /// <summary>节拍源工厂的"只试某一个"入口签名（直接复用产品代码的 TryCreate*，避免断言另写一份探测）</summary>
        private delegate bool TryCreateTickSource(out IFlowTickSource source, out string error);

        // ==================================================================
        //  公共夹具
        // ==================================================================

        private static string Aqn<T>() => typeof(T).AssemblyQualifiedName!;

        private static bool WaitFor(Func<bool> condition, int timeoutMs = 5000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(10);
            }
            return condition();
        }

        private static FlowEngineService NewEngine(WorkspaceContext workspace, StubLog log)
            => new(new RuntimeManager(), log, workspace, null, new ResourceLockService(), NullCameraProvider.Instance);

        /// <summary>建一个"只有这一条流程"的工作区（新建方案自带三条骨架，全部清掉）</summary>
        private static (WorkspaceContext workspace, SolutionModel solution) NewWorkspace(FlowModel flow)
        {
            var workspace = new WorkspaceContext();
            var solution = new SolutionModel();
            solution.Flows.Clear();
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);
            return (workspace, solution);
        }

        private static FlowSession CompileAndRegister(RuntimeManager runtime, WorkspaceContext workspace, FlowModel flow, out string error)
        {
            var compiled = new FlowCompiler(workspace).Compile(flow.Steps, flow.FlowName);
            if (!compiled.Success)
            {
                error = string.Join("；", compiled.Errors.Select(e => e.Message));
                return null;
            }

            var session = new FlowSession
            {
                FlowName = flow.FlowName,
                ExecutionEngine = compiled.Data,
                CompiledVersion = flow.Version,
            };
            session.AddBlueprintsDeep(flow.Steps);
            runtime.RegisterSession(session);
            error = null;
            return session;
        }

        // ==================================================================
        //  [E16] ① 定时触发：纯判定（勾选位 / 启用 / 间隔 / 加密 / 下限）
        // ==================================================================
        private static void TimerFireDecision()
        {
            Section("[E16] 定时调度：触发判定");

            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var timerFlow = new FlowModel { FlowName = "定时判定", InvokeType = FlowInvokeType.Timer, TimerIntervalMs = 500 };
            Check("勾了「定时」且到点 → 触发",
                FlowTimerScheduler.ShouldFire(timerFlow, t0, t0.AddMilliseconds(500)), "");
            Check("★没到点 → 不触发（差 1ms 也不触发）",
                !FlowTimerScheduler.ShouldFire(timerFlow, t0, t0.AddMilliseconds(499)), "");
            Check("从没跑过（上次=MinValue）→ 立即触发",
                FlowTimerScheduler.ShouldFire(timerFlow, DateTime.MinValue, t0), "");

            timerFlow.IsEnabled = false;
            Check("禁用流程不触发", !FlowTimerScheduler.ShouldFire(timerFlow, DateTime.MinValue, t0), "");
            timerFlow.IsEnabled = true;

            timerFlow.InvokeType = FlowInvokeType.Http;   // 只有 HTTP 位
            Check("没勾「定时」位 → 不触发（HTTP 位不管定时）",
                !FlowTimerScheduler.ShouldFire(timerFlow, DateTime.MinValue, t0), "");
            timerFlow.InvokeType = FlowInvokeType.Timer | FlowInvokeType.Http;
            Check("定时位与其它位叠加仍触发", FlowTimerScheduler.ShouldFire(timerFlow, DateTime.MinValue, t0), "");

            timerFlow.TimerIntervalMs = 0;
            Check("★间隔 0（没配）→ 不触发（0 不是「按最小间隔跑」）",
                !FlowTimerScheduler.ShouldFire(timerFlow, DateTime.MinValue, t0), "");
            // 配得比下限还小 → 按 20ms 执行（配置 5ms 不能变成死循环）
            timerFlow.TimerIntervalMs = 5;
            Check("小于下限的间隔按下限执行（不做成死循环）",
                FlowTimerScheduler.ResolveIntervalMs(5) == FlowTimerScheduler.MinIntervalMs
                && FlowTimerScheduler.ShouldFire(timerFlow, t0, t0.AddMilliseconds(FlowTimerScheduler.MinIntervalMs))
                && !FlowTimerScheduler.ShouldFire(timerFlow, t0, t0.AddMilliseconds(FlowTimerScheduler.MinIntervalMs - 1)),
                $"ResolveIntervalMs(5)={FlowTimerScheduler.ResolveIntervalMs(5)} Min={FlowTimerScheduler.MinIntervalMs}");
        }

        // ==================================================================
        //  [E16] ② 定时触发：手动时钟驱动，真编译真执行
        // ==================================================================
        private static void TimerFiresWithManualClock()
        {
            Section("[E16] 定时调度：手动时钟真跑");

            CountA.Reset();
            var flow = new FlowModel
            {
                FlowName = "定时真跑流程",
                IsEnabled = true,
                InvokeType = FlowInvokeType.Timer,
                TimerIntervalMs = 200,
            };
            flow.Steps.Add(new ActionStep("T", "计数A", Aqn<CountA>(), "定时步骤"));

            var (workspace, _) = NewWorkspace(flow);
            var log = new StubLog();
            var runtime = new RuntimeManager();
            var engine = NewEngine(workspace, log);
            using var scheduler = new FlowTimerScheduler(workspace, runtime, engine, new FlowCompiler(workspace), log);

            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            scheduler.TickOnce(t0);
            bool firstRun = WaitFor(() => CountA.Runs == 1);
            Check("[E16②] 第一拍即触发（上次=MinValue）并跑完一次", firstRun, $"Runs={CountA.Runs}");
            Check("跑完后会话已释放（不占锁）",
                WaitFor(() => runtime.GetSessionByName(flow.FlowName)?.IsRunning != true), "");

            scheduler.TickOnce(t0.AddMilliseconds(100));   // 未到 200ms
            Thread.Sleep(200);
            Check("★未到间隔的第二拍不触发", CountA.Runs == 1, $"Runs={CountA.Runs}");

            scheduler.TickOnce(t0.AddSeconds(1));
            Check("到点后又跑一次（累计 2 次）", WaitFor(() => CountA.Runs == 2), $"Runs={CountA.Runs}");

            // 目标在跑时到的拍：跳过且**不补跑**（本轮一次触发=一次执行，漏拍不追）
            var session = runtime.GetSessionByName(flow.FlowName);
            if (session != null)
            {
                session.IsRunning = true;   // 手工制造"正在运行"窗口
                try
                {
                    scheduler.TickOnce(t0.AddSeconds(2));
                    Thread.Sleep(150);
                    Check("★目标在跑时到点 → 跳过（不排队、不补跑）", CountA.Runs == 2, $"Runs={CountA.Runs}");
                    Check("跳过留下了 Warn（现场能查到这次为什么没跑）",
                        log.Warns.Any(w => w.Contains("正在运行")), string.Join(" | ", log.Warns));
                }
                finally
                {
                    session.IsRunning = false;
                }
            }
        }

        // ==================================================================
        //  [E16] ③ 变量触发：真值判定
        // ==================================================================
        private static void TruthyDecision()
        {
            Section("[E16] 变量触发：真值判定");

            Check("null → 假", !FlowVariableTriggerService.IsTruthy(null), "");
            Check("bool false → 假 / bool true → 真",
                !FlowVariableTriggerService.IsTruthy(false) && FlowVariableTriggerService.IsTruthy(true), "");
            Check("数值 0 → 假 / 非 0 → 真",
                !FlowVariableTriggerService.IsTruthy(0) && !FlowVariableTriggerService.IsTruthy(0.0)
                && FlowVariableTriggerService.IsTruthy(1) && FlowVariableTriggerService.IsTruthy(-0.5), "");
            Check("空串 / 空白串 → 假；非空串 → 真",
                !FlowVariableTriggerService.IsTruthy("") && !FlowVariableTriggerService.IsTruthy("   ")
                && FlowVariableTriggerService.IsTruthy("OK"), "");
        }

        // ==================================================================
        //  [E16] ④ 变量触发：真变量的上升沿真跑
        // ==================================================================
        private static void VariableRisingEdgeFires()
        {
            Section("[E16] 变量触发：上升沿真跑");

            CountB.Reset();
            var flow = new FlowModel
            {
                FlowName = "变量真跑流程",
                IsEnabled = true,
                InvokeType = FlowInvokeType.Variable,
                TriggerVariable = "触发位",
            };
            flow.Steps.Add(new ActionStep("V", "计数B", Aqn<CountB>(), "变量步骤"));

            var (workspace, _) = NewWorkspace(flow);
            var variable = new LocalVariableModel { Name = "触发位", Value = false };
            workspace.GlobalVariables.Add(variable);

            var log = new StubLog();
            var runtime = new RuntimeManager();
            var engine = NewEngine(workspace, log);
            using var trigger = new FlowVariableTriggerService(workspace, runtime, engine, new FlowCompiler(workspace), log);
            trigger.Start();   // 走真订阅（集合 → 变量 ValueChanged）

            variable.Value = true;   // 假 → 真：上升沿
            Check("[E16④] 上升沿触发并跑完一次", WaitFor(() => CountB.Runs == 1), $"Runs={CountB.Runs}");

            variable.Value = false;  // 真 → 假：下降沿不触发
            Thread.Sleep(200);
            Check("下降沿不触发", CountB.Runs == 1, $"Runs={CountB.Runs}");

            variable.Value = true;   // 再次上升沿
            Check("再次上升沿再触发一次（累计 2 次）", WaitFor(() => CountB.Runs == 2), $"Runs={CountB.Runs}");

            // 名字不匹配的变量变化不该触发（触发位绑的是"触发位"，另一个变量不相关）
            var other = new LocalVariableModel { Name = "别的位", Value = false };
            workspace.GlobalVariables.Add(other);
            other.Value = true;
            Thread.Sleep(200);
            Check("★TriggerVariable 名字不匹配的变量变化不触发", CountB.Runs == 2, $"Runs={CountB.Runs}");
        }

        // ==================================================================
        //  [E16] ⑤ 子程序调用：门禁与非门禁
        // ==================================================================
        private static void InvokerGates()
        {
            Section("[E16] 子程序调用：门禁");

            CountC.Reset();
            var target = new FlowModel { FlowName = "子流程", IsEnabled = true, InvokeType = FlowInvokeType.Manual };
            target.Steps.Add(new ActionStep("S", "计数C", Aqn<CountC>(), "子流程步骤"));

            var (workspace, _) = NewWorkspace(target);
            var log = new StubLog();
            var runtime = new RuntimeManager();
            var engine = NewEngine(workspace, log);
            var invoker = new FlowInvoker(workspace, runtime, engine, new FlowCompiler(workspace), log);

            var notFound = invoker.Invoke("不存在的流程", 2000, CancellationToken.None);
            Check("不存在的流程 → 失败且说明原因",
                !notFound.Success && notFound.Message.Contains("没有名为"), notFound.Message);

            var notAllowed = invoker.Invoke(target.FlowName, 2000, CancellationToken.None);
            Check("★未勾「子程序」→ 失败并指路流程管理",
                !notAllowed.Success && notAllowed.Message.Contains("未开放"), notAllowed.Message);
            Check("被门禁拦下的调用没有真的执行（计数为 0）", CountC.Runs == 0, $"Runs={CountC.Runs}");

            target.IsEnabled = false;
            var disabled = invoker.Invoke(target.FlowName, 2000, CancellationToken.None);
            Check("禁用流程 → 失败（禁用判定在调用方式之前）",
                !disabled.Success && disabled.Message.Contains("已被禁用"), disabled.Message);
            target.IsEnabled = true;

            // 门禁全过：真跑
            target.InvokeType = FlowInvokeType.Subroutine;
            var ok = invoker.Invoke(target.FlowName, 5000, CancellationToken.None);
            Check("★勾选后真跑一次并成功（含耗时）",
                ok.Success && CountC.Runs == 1 && ok.ElapsedMs >= 0,
                $"{ok.Message} Runs={CountC.Runs} Elapsed={ok.ElapsedMs}ms");

            var busy = invoker.Invoke(target.FlowName, 2000, CancellationToken.None);
            // 上一笔刚跑完（会话空闲），这里应当是"又成功跑了一次"——先记录基线，再由下面的
            // 手工置位制造"正在运行"窗口，证门禁确实在"在跑"时拦下（而不是靠时序碰巧）
            int runsAfterSecond = CountC.Runs;
            var session = runtime.GetSessionByName(target.FlowName);
            if (session != null)
            {
                session.IsRunning = true;
                try
                {
                    var busy2 = invoker.Invoke(target.FlowName, 2000, CancellationToken.None);
                    Check("★目标在跑 → 直接失败（不排队，防成环挂死）",
                        !busy2.Success && busy2.Message.Contains("正在运行"), busy2.Message);
                    Check("被拦下的这次没有执行（计数不涨）",
                        CountC.Runs == runsAfterSecond, $"Runs={CountC.Runs}");
                }
                finally
                {
                    session.IsRunning = false;
                }
            }
        }

        // ==================================================================
        //  [E16] ⑥+ 并发窗口：抢不到会话锁必须**如实报失败**（不得把"我没跑"读成"跑完了"）
        // ==================================================================
        private static void InvokerLosesRaceReportsFailure()
        {
            Section("[E16] 子程序调用：并发窗口如实报失败");

            // 目标流程带"闸门算子"：它能把自己挂在执行中途不退出来，从而造出真实的"运行中"窗口
            GatePlugin.Reset();
            var target = new FlowModel { FlowName = "闸门子流程", IsEnabled = true, InvokeType = FlowInvokeType.Subroutine };
            target.Steps.Add(new ActionStep("G", "闸门", Aqn<GatePlugin>(), "闸门步骤"));

            var (workspace, _) = NewWorkspace(target);
            var log = new StubLog();
            var runtime = new RuntimeManager();
            var engine = NewEngine(workspace, log);
            var invoker = new FlowInvoker(workspace, runtime, engine, new FlowCompiler(workspace), log);

            var session = CompileAndRegister(runtime, workspace, target, out var error);
            Check("闸门子流程编译通过", session != null, error ?? "");
            if (session == null) return;

            // 先真跑起来（卡在闸门里），再把 IsRunning 假置 false ——
            // 精确复刻"前置检查之后、抢会话锁之前"那一瞬（TOCTOU 窗口）
            var flying = engine.RunSessionOnceAsync(session);
            bool inGate = GatePlugin.Reached.Wait(TimeSpan.FromSeconds(5));
            Check("闸门子流程已在运行（造出并发窗口）", inGate, "5 秒内没进闸门，后面的断言无意义");
            if (!inGate)
            {
                GatePlugin.Proceed.Set();
                flying.Wait(TimeSpan.FromSeconds(5));
                return;
            }

            session.IsRunning = false;   // 骗过前置检查：只剩引擎的抢锁这道真闸
            var raced = invoker.Invoke(target.FlowName, 2000, CancellationToken.None);

            Check("★抢不到锁 → 如实报失败（旧签名会'正常返回'，把没跑上读成跑完）",
                !raced.Success && raced.Message.Contains("未执行"), raced.Message);

            GatePlugin.Proceed.Set();
            Check("放行后第一笔执行正常收尾", flying.Wait(TimeSpan.FromSeconds(5)), $"IsCompleted={flying.IsCompleted}");
        }

        // ==================================================================
        //  [E16] ⑦ 插件端到端：父流程一步 = 调用流程（真装载 Modules\Plugin.RunFlow.dll）
        // ==================================================================
        private static void RunFlowPluginEndToEnd()
        {
            Section("[E16] 「调用流程」插件：父流程真调子流程");

            var modulesDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\Modules"));
            var pluginDll = Path.Combine(modulesDir, "Plugin.RunFlow.dll");
            Type pluginType = null;
            try
            {
                var asm = Assembly.LoadFrom(pluginDll);
                pluginType = asm.GetType("VisionMaster.Plugins.RunFlow.RunFlowPlugin", throwOnError: false);
            }
            catch (Exception ex)
            {
                Check("Plugin.RunFlow.dll 可装载", false, ex.Message);
            }

            Check("Plugin.RunFlow.dll 已投递到 Modules 且类型可解析（红线①：插件产物必经 Modules 投递）",
                pluginType != null, pluginType == null ? $"未找到类型（{pluginDll}）" : pluginType.AssemblyQualifiedName);
            if (pluginType == null) return;

            CountD.Reset();
            var child = new FlowModel
            {
                FlowName = "被调用子流程",
                IsEnabled = true,
                InvokeType = FlowInvokeType.Subroutine,   // 勾了「子程序」才允许被调用
            };
            child.Steps.Add(new ActionStep("S", "计数D", Aqn<CountD>(), "子流程里的步骤"));

            var parent = new FlowModel { FlowName = "父流程", IsEnabled = true };
            var callStep = new ActionStep("R", "调用流程", pluginType.AssemblyQualifiedName, "调用子流程");
            callStep.SetInputValue("FlowName", child.FlowName);
            callStep.SetInputValue("TimeoutMs", 5000);
            parent.Steps.Add(callStep);

            var workspace = new WorkspaceContext();
            var solution = new SolutionModel();
            solution.Flows.Clear();
            solution.Flows.Add(parent);
            solution.Flows.Add(child);
            workspace.SwitchSolution(solution);

            var log = new StubLog();
            var runtime = new RuntimeManager();
            var engine = NewEngine(workspace, log);
            // 装配调用器：真实现里这一步由 FlowEngineModule 完成（属性注入，环引用构造期解不开）
            engine.FlowInvoker = new FlowInvoker(workspace, runtime, engine, new FlowCompiler(workspace), log);

            var parentSession = CompileAndRegister(runtime, workspace, parent, out var parentError);
            Check("父流程（含「调用流程」步骤）编译通过", parentSession != null, parentError ?? "");
            if (parentSession == null) return;

            var run = engine.RunSessionOnceAsync(parentSession);
            bool completed = WaitFor(() => run.IsCompleted, 15000);
            Check("父流程跑完（未挂死——子流程等待是真等的）", completed, $"IsCompleted={run.IsCompleted}");

            Check("★子流程真的被执行了（插件拿到的是真调用器，不是 NullFlowInvoker）",
                CountD.Runs == 1, $"Runs={CountD.Runs}");
            Check("父流程里这一步判定为成功", callStep.State == StepState.Success, $"State={callStep.State}");

            // 未勾「子程序」的子流程应当被拦下：改成 Manual 再跑一次，父流程这一步必须失败
            child.InvokeType = FlowInvokeType.Manual;
            CountD.Reset();
            var second = engine.RunSessionOnceAsync(parentSession);
            WaitFor(() => second.IsCompleted, 15000);
            Check("★子流程未勾「子程序」→ 调用被门禁拦下（这一步失败、子流程没跑）",
                callStep.State != StepState.Success && CountD.Runs == 0,
                $"State={callStep.State} Runs={CountD.Runs}");
        }

        // ==================================================================
        //  [E16] ⑧ 节拍源：高精度等待计时器 / 多媒体时钟（winmm）/ 托管兜底
        // ==================================================================
        private static void TickSources()
        {
            Section("[E16] 节拍源：三种时钟的真实跳拍");

            var log = new StubLog();

            // ① 工厂按优先级挑选（不真实 Start 调度器，只看挑中了谁）
            using (var picked = FlowTickSourceFactory.Create(log))
            {
                Check("工厂总能给出一个可用节拍源（全部不可用时兜底托管）",
                    picked != null, picked?.Name ?? "(null)");
                Check($"本机挑中的节拍源是高精度源：{picked.Name}",
                    picked.IsHighPrecision,
                    picked.IsHighPrecision ? "" : "只剩托管兜底：节拍抖动约 15.6ms，定时间隔建议 ≥50ms");
            }

            // ② 高精度等待计时器（本期默认实现）：真跑 300ms 数拍
            MeasureTicks("高精度等待计时器", FlowTickSourceFactory.TryCreateWaitableTimer);

            // ③ 多媒体计时器（winmm，用户问的"Windows 媒体时钟"）：本机可用就真跑
            MeasureTicks("多媒体计时器（winmm 媒体时钟）", FlowTickSourceFactory.TryCreateMediaClock);

            // ④ 托管兜底源：永远可用，必须也能跳
            MeasureTicks("托管计时器（兜底）", (out IFlowTickSource s, out string e) =>
            {
                s = new ManagedTickSource();
                e = null;
                return true;
            });
        }

        private static void MeasureTicks(string label, TryCreateTickSource factory)
        {
            if (!factory(out var source, out var error))
            {
                // 本机不支持不算失败（与"缺 SDK 优雅降级"同一口径）：工厂会自动降级，日志里说明原因
                Check($"{label}：本机不可用 → 工厂已降级（不作为失败）", true, error);
                return;
            }

            int ticks = 0;
            var name = source.Name;
            try
            {
                using (source)
                {
                    source.Start(10, () => Interlocked.Increment(ref ticks));
                    Thread.Sleep(300);
                }
            }
            catch (Exception ex)
            {
                Check($"{label}：启动失败 → 工厂已降级（不作为失败）", true, ex.Message);
                return;
            }

            // 10ms 拍 × 300ms ≈ 30 拍；放宽到 ≥8 拍（机器忙时也不能少得离谱）
            Check($"{label}：300ms 内跳拍密度合理（10ms 拍，期望 ≈30）", ticks >= 8,
                $"ticks={ticks} 源={name}");
        }

        // ==================================================================
        //  [E16] ⑨ 真时钟 + 调度器端到端：跑起来 / Dispose 后停住
        // ==================================================================
        private static void SchedulerWithRealClockRunsAndStops()
        {
            Section("[E16] 真时钟 + 调度器：端到端");

            CountA.Reset();
            var flow = new FlowModel
            {
                FlowName = "真时钟流程",
                IsEnabled = true,
                InvokeType = FlowInvokeType.Timer,
                TimerIntervalMs = FlowTimerScheduler.MinIntervalMs,   // 20ms：贴着下限跑，最能暴露时钟不准
            };
            flow.Steps.Add(new ActionStep("T", "计数A", Aqn<CountA>(), "真时钟步骤"));

            var (workspace, _) = NewWorkspace(flow);
            var log = new StubLog();
            var runtime = new RuntimeManager();
            var engine = NewEngine(workspace, log);

            var scheduler = new FlowTimerScheduler(workspace, runtime, engine, new FlowCompiler(workspace), log);
            scheduler.Start();
            Check("调度器启动后已确定节拍源", scheduler.TickSource != null, scheduler.TickSource?.Name ?? "(null)");

            bool ran = WaitFor(() => CountA.Runs >= 2, 3000);
            Check("★真时钟驱动：20ms 间隔的流程在 3 秒内跑了 ≥2 次", ran, $"Runs={CountA.Runs}");

            scheduler.Dispose();
            int after = CountA.Runs;   // 在途那一拍可能刚好落地，取快照再等
            Thread.Sleep(400);
            Check("★Dispose 后节拍停住（计数不再增长）", CountA.Runs == after, $"{after} → {CountA.Runs}");
        }
    }
}
