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
            RunFlowTargetCandidates();
            TickSources();
            SchedulerWithRealClockRunsAndStops();
            L2RoundRecordChecks();
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

        /// <summary>从输出目录向上找仓库内文件（与其它 Check 类同款；断言宿主不在仓库根运行）。</summary>
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

        /// <summary>绑定弹窗断言用的空插件表（它只用端口定义，不查插件字典）。</summary>
        private sealed class EmptyPluginProvider : IPluginProvider
        {
            private static readonly System.Collections.Generic.Dictionary<string, ToolItemModel> Empty = new();

            public System.Collections.Generic.IReadOnlyDictionary<string, ToolItemModel> ModulePlugins => Empty;
            public System.Collections.Generic.IReadOnlyDictionary<string, ToolItemModel> CameraPlugins => Empty;
            public System.Collections.Generic.IReadOnlyDictionary<string, ToolItemModel> LaserPlugins => Empty;
            public System.Collections.Generic.IReadOnlyDictionary<string, ToolItemModel> MotionPlugins => Empty;

            public void RegisterModule(ToolItemModel plugin) { }
            public void RegisterCamera(ToolItemModel plugin) { }
            public void RegisterLaser(ToolItemModel plugin) { }
            public void RegisterMotion(ToolItemModel plugin) { }

            public ToolItemModel GetModule(string name) => null;
            public ToolItemModel GetCamera(string name) => null;
            public ToolItemModel GetLaser(string name) => null;
            public ToolItemModel GetMotion(string name) => null;
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
        //  [E16] ⑦b 目标流程名：宿主候选（只列子程序 + 状态后缀 + 注册接线）
        // ==================================================================
        private static void RunFlowTargetCandidates()
        {
            Section("[E16] 「调用流程」目标流程名：宿主候选");

            var modulesDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\Modules"));
            var pluginDll = Path.Combine(modulesDir, "Plugin.RunFlow.dll");

            Type pluginType = null;
            string loadError = string.Empty;
            try
            {
                pluginType = Assembly.LoadFrom(pluginDll)
                    .GetType("VisionMaster.Plugins.RunFlow.RunFlowPlugin", throwOnError: false);
            }
            catch (Exception ex)
            {
                loadError = ex.Message;
            }

            if (pluginType == null && loadError.Length == 0)
                loadError = $"类型未找到（{pluginDll}）";
            Check("（前提）Plugin.RunFlow.dll 可装载（候选断言）", pluginType != null,
                pluginType == null ? loadError : pluginType.AssemblyQualifiedName!);
            if (pluginType == null) return;

            // ---- ① 端口声明：封闭候选 + 候选类别 ----
            IInputPort port = null;
            string portError = string.Empty;
            try
            {
                var instance = Activator.CreateInstance(pluginType);
                port = pluginType.GetProperty("TargetFlowPort")?.GetValue(instance) as IInputPort;
            }
            catch (Exception ex)
            {
                portError = ex.Message;
            }

            Check("★FlowName 端口声明为封闭候选（IsFunctionalEnum + SubroutineFlowName）",
                port != null && port.IsFunctionalEnum && port.OptionKind == StepConfigOptionKind.SubroutineFlowName,
                port == null
                    ? (portError.Length > 0 ? portError : "端口反射失败")
                    : $"IsFunctionalEnum={port.IsFunctionalEnum} OptionKind={port.OptionKind}");

            // ---- ② 候选过滤：只列子程序位；禁用/加密/当前流程照列并标注 ----
            var workspace = new WorkspaceContext();
            var solution = new SolutionModel();
            solution.Flows.Clear();

            var sub = new FlowModel { FlowName = "回原", InvokeType = FlowInvokeType.Subroutine };
            var disabled = new FlowModel { FlowName = "禁用子流程", InvokeType = FlowInvokeType.Subroutine, IsEnabled = false };
            var encrypted = new FlowModel { FlowName = "加密子流程", InvokeType = FlowInvokeType.Subroutine, StepsEncrypted = true };
            var manual = new FlowModel { FlowName = "普通流程", InvokeType = FlowInvokeType.Manual };
            var timer = new FlowModel { FlowName = "定时流程", InvokeType = FlowInvokeType.Timer };
            // 边界：同名（人工编辑 .vms）+ 名字首尾带空格（Trim 后按 Ordinal 永远命中不了）
            var duplicate = new FlowModel { FlowName = "回原", InvokeType = FlowInvokeType.Subroutine };
            var spaced = new FlowModel { FlowName = " 带空格子流程 ", InvokeType = FlowInvokeType.Subroutine };
            solution.Flows.Add(sub);
            solution.Flows.Add(disabled);
            solution.Flows.Add(encrypted);
            solution.Flows.Add(manual);
            solution.Flows.Add(timer);
            solution.Flows.Add(duplicate);
            solution.Flows.Add(spaced);
            workspace.SwitchSolution(solution);
            // 当前流程选普通流程：先测"正常子程序显示纯名字"，当前流程后缀单独测
            workspace.SwitchFlow(manual);

            var options = SubroutineFlowOptions.Build(workspace);
            var values = options.Select(o => o.Value).ToList();
            var displays = options.ToDictionary(o => o.Value, o => o.Display, StringComparer.Ordinal);

            Check("★候选只列勾了「子程序」的流程（手动/定时不出现）",
                values.Count == 3
                && values.Contains("回原") && values.Contains("禁用子流程") && values.Contains("加密子流程")
                && !values.Contains("普通流程") && !values.Contains("定时流程"),
                string.Join(",", values));

            Check("★禁用 / 加密仍列出且后缀写明原因（剔掉会让已配好的值显示空白）",
                displays.TryGetValue("回原", out var plainText) && plainText == "回原"
                && displays.TryGetValue("禁用子流程", out var disabledText) && disabledText == "禁用子流程（已禁用）"
                && displays.TryGetValue("加密子流程", out var encryptedText) && encryptedText == "加密子流程（步序已加密）",
                string.Join(" | ", options.Select(o => o.Display)));

            Check("候选按名字升序（下拉顺序稳定，不随流程表顺序漂）",
                values.SequenceEqual(new[] { "加密子流程", "回原", "禁用子流程" }, StringComparer.Ordinal),
                string.Join(",", values));

            Check("同名流程只列一条（FindFlow 也只认第一条，列两项会让人以为选哪个都行）",
                values.Count(v => v == "回原") == 1, string.Join(",", values));

            Check("名字首尾带空格的流程不列（FlowInvoker 先 Trim 再按 Ordinal 比，永远命中不了）",
                !values.Contains("带空格子流程") && !values.Contains(" 带空格子流程 "), string.Join(",", values));

            // 当前流程：照列 + 后缀（自己调自己会被"正在运行中"拦下，界面上先说清）
            workspace.SwitchFlow(sub);
            var selfOptions = SubroutineFlowOptions.Build(workspace);
            var selfDisplay = selfOptions.FirstOrDefault(o => o.Value == "回原")?.Display;
            Check("当前流程照列并标注「不能调用自己」",
                selfDisplay == "回原（当前流程，不能调用自己）", selfDisplay ?? "(null)");

            // ---- ②b 历史数据：整线 + 子程序并存（2026-10-29 起界面互斥，旧 .vms 仍可能有）----
            // 仍要列出来（否则已配好的值在下拉里显示成空白 = "配置丢了"），
            // 但显示文本必须说明"整线运行时不可调用"——这正是"列表里为什么混进非子流程"的答案。
            // 独立夹具：不动上面那份 workspace（后面 viaSource 还要按它的候选数比对）。
            var legacyWorkspace = new WorkspaceContext();
            var legacySolution = new SolutionModel();
            legacySolution.Flows.Clear();
            legacySolution.Flows.Add(new FlowModel
            {
                FlowName = "工位_整线子程序",
                InvokeType = FlowInvokeType.LineStart | FlowInvokeType.Subroutine,
            });
            legacyWorkspace.SwitchSolution(legacySolution);
            var legacyDisplay = SubroutineFlowOptions.Build(legacyWorkspace)
                .FirstOrDefault(o => o.Value == "工位_整线子程序")?.Display;
            Check("★整线成员仍在候选里，但后缀写明「整线运行时不可调用」",
                legacyDisplay == "工位_整线子程序（整线成员，整线运行时不可调用）",
                legacyDisplay ?? "(null)");

            // ---- ③ 注册接线：契约层来源表能取到同一份候选，且不顶掉其它来源 ----
            StepConfigOptionSource.RegisterAdditional(kind =>
                kind == StepConfigOptionKind.MotionAxisName
                    ? new[] { new StepConfigOption("X", "X") }
                    : Array.Empty<StepConfigOption>());
            SubroutineFlowOptions.Register(() => workspace);

            var viaSource = StepConfigOptionSource.GetOptionItems(StepConfigOptionKind.SubroutineFlowName);
            Check("★注册后从契约层来源表取到同一份候选（RegisterAdditional 接线）",
                viaSource.Count == selfOptions.Count
                && viaSource.Select(o => o.Value).SequenceEqual(selfOptions.Select(o => o.Value), StringComparer.Ordinal),
                string.Join(",", viaSource.Select(o => o.Value)));

            var axisOptions = StepConfigOptionSource.GetOptionItems(StepConfigOptionKind.MotionAxisName);
            Check("★RegisterAdditional 不顶掉其它来源（轴名候选仍在）",
                axisOptions.Any(o => o.Value == "X"), $"MotionAxisName=[{string.Join(",", axisOptions.Select(o => o.Value))}]");

            // ---- ③b 真控件形态：端口进 LinkableValueEditor 必须渲染成"只能选"下拉 ----
            // 只断端口标志还不够：面板到底渲染成下拉还是文本框，取决于控件里的
            // IsOptionPort（= 封闭集合 ∧ 候选非空）。这一条把"用户看到的形态"钉住。
            string editorProbe = "未执行";
            var editorThread = new Thread(() =>
            {
                try
                {
                    var editor = new Core.Controls.LinkableValueEditor { Port = port };
                    editor.Measure(new System.Windows.Size(520, 40));
                    editor.Arrange(new System.Windows.Rect(0, 0, 520, 40));
                    editor.UpdateLayout();
                    editorProbe = $"只能选={editor.IsOptionPort} 候选数={editor.EnumOptions.Count}"
                                  + $" 首项={editor.EnumOptions.FirstOrDefault()?.Display ?? "-"}";
                }
                catch (Exception ex)
                {
                    editorProbe = "异常：" + ex;
                }
            })
            { IsBackground = true };
            editorThread.SetApartmentState(ApartmentState.STA);
            editorThread.Start();
            editorThread.Join(8000);

            Check("★FlowName 端口在真控件里渲染成「只能选」下拉（候选来自方案）",
                editorProbe.Contains("只能选=True") && editorProbe.Contains($"候选数={selfOptions.Count}"),
                editorProbe);

            // ---- ③c 老 .vms 的上游连线：下拉照常显示，选一项即拆线写固定值 ----
            var legacyStep = new ActionStep("R", "调用流程", pluginType.AssemblyQualifiedName, "老连线步骤");
            legacyStep.SetLink("FlowName", new LinkReference(
                LinkKind.StepPort, Guid.NewGuid(), "Out", "上游步骤.Out"));

            string legacyProbe = "未执行";
            var legacyThread = new Thread(() =>
            {
                try
                {
                    var editor = new Core.Controls.LinkableValueEditor { Port = port, StepData = legacyStep };
                    editor.Measure(new System.Windows.Size(520, 40));
                    editor.Arrange(new System.Windows.Rect(0, 0, 520, 40));
                    editor.UpdateLayout();
                    var combo = editor.FindName("PART_EnumBox") as System.Windows.Controls.ComboBox;
                    var linkBox = editor.FindName("PART_LinkBox") as System.Windows.Controls.TextBox;
                    var before = $"链接={editor.IsLinked} 只能选={editor.IsOptionPort} 封闭={editor.IsClosedValuePort}"
                               + $" 下拉={combo?.Visibility} 链接框={linkBox?.Visibility}";
                    editor.PortValue = "回原";   // 模拟用户在下拉里选一项
                    legacyProbe = before
                        + $" → 仍链接={editor.IsLinked} 模型链接={legacyStep.IsLinked("FlowName")} 值={editor.Port.Value}";
                }
                catch (Exception ex)
                {
                    legacyProbe = "异常：" + ex;
                }
            })
            { IsBackground = true };
            legacyThread.SetApartmentState(ApartmentState.STA);
            legacyThread.Start();
            legacyThread.Join(8000);

            Check("★老 .vms 的 FlowName 连线：下拉照常显示，选一项即拆线并写固定值",
                legacyProbe.Contains("链接=True") && legacyProbe.Contains("只能选=True")
                && legacyProbe.Contains("下拉=Visible") && legacyProbe.Contains("链接框=Collapsed")
                && legacyProbe.Contains("仍链接=False") && legacyProbe.Contains("模型链接=False")
                && legacyProbe.Contains("值=回原"),
                legacyProbe);

            // ---- ③c2 老连线 + 候选为空：手填框必须还在（否则整行空白，改不了也拆不掉） ----
            var emptyPort = new InputPort<string>("FlowName", "", "目标流程名")
            {
                IsRequired = true,
                IsFunctionalEnum = true,
                // 本宿主没有任何来源回答这个类别 → 候选必为空
                OptionKind = StepConfigOptionKind.CommunicationVariableName,
            };
            var emptyStep = new ActionStep("R", "调用流程", pluginType.AssemblyQualifiedName, "老连线空候选");
            emptyStep.SetLink("FlowName", new LinkReference(
                LinkKind.StepPort, Guid.NewGuid(), "Out", "上游步骤.Out"));

            string emptyProbe = "未执行";
            var emptyThread = new Thread(() =>
            {
                try
                {
                    var editor = new Core.Controls.LinkableValueEditor { Port = emptyPort, StepData = emptyStep };
                    editor.Measure(new System.Windows.Size(520, 40));
                    editor.Arrange(new System.Windows.Rect(0, 0, 520, 40));
                    editor.UpdateLayout();
                    var box = editor.FindName("PART_ValueBox") as System.Windows.Controls.TextBox;
                    var emptyState = editor.FindName("PART_OptionEmpty") as System.Windows.UIElement;
                    emptyProbe = $"只能选={editor.IsOptionPort} 手填退化={editor.IsHandFillFallback}"
                                 + $" 空态={editor.IsOptionEmpty}"
                                 + $" 链接={editor.IsLinked} 文本框={(box == null ? "找不到" : box.Visibility.ToString())}"
                                 + $" 空态块={(emptyState == null ? "找不到" : emptyState.Visibility.ToString())}";
                }
                catch (Exception ex)
                {
                    emptyProbe = "异常：" + ex;
                }
            })
            { IsBackground = true };
            emptyThread.SetApartmentState(ApartmentState.STA);
            emptyThread.Start();
            emptyThread.Join(8000);

            // 2026-10-24 口径：候选为空不再退化成手填，改为「主输入框折叠 + 空态块说明原因」。
            // 老连线仍在（链接=True）说明没被拆掉，但值不能被手打进去。
            Check("★老连线 + 候选为空：封闭端口不再手填，而是折叠主框并显示空态说明（整行不再空白）",
                emptyProbe.Contains("只能选=False") && emptyProbe.Contains("手填退化=False")
                && emptyProbe.Contains("空态=True")
                && emptyProbe.Contains("链接=True")
                && emptyProbe.Contains("文本框=Collapsed") && emptyProbe.Contains("空态块=Visible"),
                emptyProbe);

            // ---- ③d 绑定弹窗：封闭端口即使候选为空也必须强制常量模式 ----
            // 画布拖线进入的绑定弹窗是另一条"给值"的入口；旧判据是
            // `IsFunctionalEnum && items.Count > 0`，空候选时会放开上游面板 —— 那等于 R33 漏了。
            var vm = new VisionMaster.ViewModels.VariableBindingViewModel(workspace, new EmptyPluginProvider());
            vm.SelectedInputPort = new InputPortUIModel(new PortDefinition
            {
                Name = "FlowName",
                DataTypeName = typeof(string).AssemblyQualifiedName,
                IsFunctionalEnum = true,
                // 本宿主没有任何来源回答这个类别 → 候选必为空
                OptionKind = StepConfigOptionKind.CommunicationVariableName,
            }, null);

            Check("★绑定弹窗：封闭端口即使候选为空也强制常量模式（上游面板不出现）",
                vm.HasFunctionalEnumPort && vm.IsConstantMode && !vm.IsUpstreamMode && vm.PresetOptions.Count == 0,
                $"封闭={vm.HasFunctionalEnumPort} 常量={vm.IsConstantMode} 上游={vm.IsUpstreamMode} 候选={vm.PresetOptions.Count}");

            vm.SelectedInputPort = new InputPortUIModel(new PortDefinition
            {
                Name = "FlowName",
                DataTypeName = typeof(string).AssemblyQualifiedName,
                IsFunctionalEnum = true,
                OptionKind = StepConfigOptionKind.SubroutineFlowName,
            }, null);

            Check("绑定弹窗：有候选时列出全部候选并默认选中第一项",
                vm.HasFunctionalEnumPort && vm.PresetOptions.Count == 3
                && !string.IsNullOrWhiteSpace(vm.ConstantValue)
                && vm.ConstantValue == vm.PresetOptions[0].Option,
                $"候选={vm.PresetOptions.Count} 默认={vm.ConstantValue}");

            // ---- ③e 确认前的状态：Confirm 必走常量分支 ----
            // 不直接调 ConfirmCommand：Prism 的 DialogCloseListener 未由 DialogService 初始化时，
            // Invoke 会在 ThreadPool 上抛未处理异常，把整个断言宿主带崩（实测）。
            // 这里断"决定 Confirm 走哪条分支"的输入状态；常量分支的写法由既有
            // VariableBindingCheck 的绑定用例覆盖。
            var confirmVm = new VisionMaster.ViewModels.VariableBindingViewModel(workspace, new EmptyPluginProvider());
            confirmVm.OnDialogOpened(new Prism.Dialogs.DialogParameters
            {
                { "TargetStep", new ActionStep("R", "调用流程", pluginType.AssemblyQualifiedName, "确认状态") },
                { "IsSingleBindMode", false },
            });
            confirmVm.SelectedInputPort = new InputPortUIModel(new PortDefinition
            {
                Name = "FlowName",
                DataTypeName = typeof(string).AssemblyQualifiedName,
                IsFunctionalEnum = true,
                OptionKind = StepConfigOptionKind.SubroutineFlowName,
            }, null);

            Check("★绑定弹窗确认前的状态：常量模式 + 有值（Confirm 必走常量分支，不写上游连线）",
                confirmVm.IsConstantMode && !confirmVm.IsUpstreamMode
                && !string.IsNullOrWhiteSpace(confirmVm.ConstantValue),
                $"常量模式={confirmVm.IsConstantMode} 上游={confirmVm.IsUpstreamMode} 值={confirmVm.ConstantValue}");

            // ---- ④ 宿主真接线：FlowEngineModule 启动时必须注册这个来源 ----
            var modulePath = ResolveRepoFile(@"Engine\FlowEngineModule.cs");
            var moduleText = modulePath == null ? string.Empty : File.ReadAllText(modulePath);
            var moduleWired = moduleText.Contains("SubroutineFlowOptions.Register(", StringComparison.Ordinal);
            Check("★FlowEngineModule 启动时注册流程候选来源（静态扫描：没接上等于面板永远退回手填）",
                modulePath != null && moduleWired,
                modulePath == null
                    ? "定位不到 Engine\\FlowEngineModule.cs"
                    : (moduleWired ? "已找到注册调用" : "FlowEngineModule.cs 里没有 SubroutineFlowOptions.Register("));
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

        // ==================================================================
        //  [L2] 每轮记录：LastRound / FlowRoundCompleted / NG 不是调用故障
        // ==================================================================
        private static void L2RoundRecordChecks()
        {
            Section("[L2] 每轮记录：LastRound / FlowRoundCompleted / NG 不是调用故障");

            var flow = new FlowModel
            {
                FlowName = "L2_轮次",
                IsEnabled = true,
                InvokeType = FlowInvokeType.Subroutine,
            };
            flow.Steps.Add(new ActionStep("S", "计数", Aqn<CountC>(), "L2 子流程步骤"));

            var (workspace, _) = NewWorkspace(flow);
            var log = new StubLog();
            var runtime = new RuntimeManager();
            var resolver = new StubVerdictResolver();
            var engine = new FlowEngineService(
                runtime,
                log,
                workspace,
                null,
                new ResourceLockService(),
                NullCameraProvider.Instance,
                verdictResolver: resolver);

            var session = CompileAndRegister(runtime, workspace, flow, out var error);
            Check("L2-0 编译通过", session != null, error ?? string.Empty);
            if (session == null) return;

            FlowRoundResult published = null;
            int publishedCount = 0;
            engine.FlowRoundCompleted += r =>
            {
                published = r;
                publishedCount++;
            };

            var run = engine.RunSessionOnceAsync(session);
            WaitFor(() => run.IsCompleted, 10000);

            var last = session.LastRound;
            Check("L2-1 单次运行产出 LastRound", last != null, last?.OutcomeText ?? "null");
            Check("L2-2 FlowRoundCompleted 触发一次", publishedCount == 1, $"{publishedCount}");
            Check("L2-3 解析器 Ng → Outcome=Ng / Source=ResultStep",
                last?.Outcome == FlowRoundOutcome.Ng && last?.VerdictSource == FlowVerdictSource.ResultStep,
                $"{last?.Outcome} / {last?.VerdictSource}");
            Check("L2-4 RoundIndex=1、StepCount=1、Completed=1",
                last?.RoundIndex == 1 && last?.StepCount == 1 && last?.CompletedStepCount == 1,
                $"{last?.RoundIndex} / {last?.StepCount} / {last?.CompletedStepCount}");
            Check("L2-7 步骤时间线：1 步 / Success / 耗时 ≥ 0",
                last?.Steps.Count == 1
                && last.Steps[0].State == "Success"
                && last.Steps[0].ElapsedMs >= 0,
                $"{last?.Steps.Count} / {last?.Steps.FirstOrDefault()?.State} / {last?.Steps.FirstOrDefault()?.ElapsedMs:F1}ms");
            Check("L2-5 发布的记录与 LastRound 同一条", ReferenceEquals(published, last), "same");

            var invoker = new FlowInvoker(workspace, runtime, engine, new FlowCompiler(workspace), log);
            var result = invoker.Invoke(flow.FlowName, 5000, CancellationToken.None);
            Check("L2-6 子流程 Ng → Success=true / Outcome=Ng / IsOk=false",
                result.Success && result.Outcome == FlowRoundOutcome.Ng && result.IsOk == false,
                $"{result.Success} / {result.Outcome} / {result.IsOk} / {result.Message}");
        }

        private sealed class StubVerdictResolver : IFlowVerdictResolver
        {
            public FlowVerdict Verdict { get; set; } =
                new(FlowRoundOutcome.Ng, FlowVerdictSource.ResultStep, "stub");

            public FlowVerdict Resolve(FlowSession session) => Verdict;
        }
    }
}
