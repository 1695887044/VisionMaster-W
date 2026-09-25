using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
// 直接复用断言宿主的 Section/Check 计数口径，不再另造一套
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 流程引擎优化（A1 / A2 / A3 / B1 / B2 / B3）的执行层断言。
    ///
    /// 与画布断言的分工：画布那 16 段验的是"图纸拓扑与交互"，本段验的是"编译出来的引擎
    /// 到底怎么跑"。所以这里必须真编译、真执行、真并发，桩插件也要是能实例化的真类型。
    /// </summary>
    internal static class ExecutionChecks
    {
        private static string TypeOf(object plugin) => plugin.GetType().AssemblyQualifiedName!;

        /// <summary>图纸放进容器的某个分支里</summary>
        private static void Put(StepCollection branch, params StepModel[] steps)
        {
            foreach (var s in steps) branch.Steps.Add(s);
        }

        // ---- 对照探针：Newtonsoft 对"只读集合属性"的 ObjectCreationHandling 到底怎么处理 ----
        // 已用于定位 ConditionStep/ForStep.Children 存盘翻倍的修法，结论固定如下，探针本身已撤除：
        //   只读集合 + 无标注            → Populate 追加，分支翻倍
        //   只读集合 + Replace           → 新集合无法赋回属性，文件数据被整体丢弃（更危险）
        //   有 setter + Replace          → 整体替换且保留文件数据（正确）
        // 因此 Children 必须是"带 setter + Replace"两件套；下面的往返断言负责钉住这个行为。
        private static T RoundTrip<T>(T value) =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<T>(
                Newtonsoft.Json.JsonConvert.SerializeObject(value)
            )!;

        // ==================================================================
        //  [E1] A1：循环体内的 If 分支必须真的执行
        //       旧实现是 foreach 挨个 step.RunAndGetNext(context) 并丢弃返回值，
        //       而 CompiledIfNode 靠返回值交出选中分支 —— 于是循环体里的 If
        //       条件照算、不报错、不写日志，分支里的算子一个都不跑。旧代码此处理应为 0。
        // ==================================================================
        internal static void IfInsideLoopExecutes()
        {
            Section("[E1] A1 循环体内的 If 分支");

            CountingPlugin.Reset();
            var forStep = new ForStep("F", "计次循环", TypeOf(new CountingPlugin()), "外层For");
            forStep.DefaultLoopCount = 2;

            var ifStep = new ConditionStep("I", "条件", "SomeIfOperator", "循环内If");
            ifStep.Children[0].Expression = "1 == 1";                 // If 分支
            Put(ifStep.Children[0], new ActionStep("A", "计数", TypeOf(new CountingPlugin()), "分支内计数"));
            Put(forStep.Children[0], ifStep);

            var run = ExecHarness.Prepare(new StepModel[] { forStep });
            Check("图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            run.Engine!.Run(run.NewContext(new StubLog()));

            Check("For(2) 套 If(真) 内的算子恰好跑 2 次", CountingPlugin.Runs == 2,
                $"实际 Runs={CountingPlugin.Runs}（旧实现此处为 0，即循环内 If 静默失效）");

            // For 循环体里的 Break：必须由循环体序列立刻交回控制权，由 For 决定跳出
            CountingPlugin.Reset();
            var forBreak = new ForStep("F", "计次循环", TypeOf(new CountingPlugin()), "Break用For");
            forBreak.DefaultLoopCount = 3;
            Put(forBreak.Children[0],
                new ActionStep("A", "计数", TypeOf(new CountingPlugin()), "Break前计数"),
                new ActionStep("B", "跳出", "BuiltIn_Break", "Break"));

            var runB = ExecHarness.Prepare(new StepModel[] { forBreak });
            Check("含 Break 的图纸编译通过", runB.Compiled, runB.Errors);
            if (!runB.Compiled) return;

            runB.Engine!.Run(runB.NewContext(new StubLog()));
            Check("循环体内 Break 立即跳出（3 圈只跑 1 圈）", CountingPlugin.Runs == 1,
                $"实际 Runs={CountingPlugin.Runs}");
        }

        // ==================================================================
        //  [E2] A1：While 循环体同样要能承接分支，且条件能推进、能精确退出
        //       条件里的运行时变量每圈现从 context.LocalVariables 取，
        //       一个 context 一份 —— 所以循环体末端的桩把它 +1 就能控圈数。
        // ==================================================================
        internal static void WhileLoopRunsAndExits()
        {
            Section("[E2] A1 While 循环体与条件推进");

            CountingPlugin.Reset();
            var whileStep = new WhileStep("W", "条件循环", "SomeWhileOperator", "计数While");
            whileStep.Children[0].Expression = "Counter < 2";
            whileStep.RuntimeVariableRefs.Add(new LocalVariableItem { Name = "Counter", DataTypeName = "System.Double" });
            Put(whileStep.Children[0], new ActionStep("A", "累加计数", TypeOf(new CounterStepPlugin()), "每圈累加"));

            var run = ExecHarness.Prepare(new StepModel[] { whileStep });
            Check("While 图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var sw = Stopwatch.StartNew();
            run.Engine!.Run(run.NewContext(new StubLog()));
            sw.Stop();

            Check("While(Counter<2) 精确跑 2 圈", CountingPlugin.Runs == 2,
                $"实际 Runs={CountingPlugin.Runs}（撞 MaxIterations=9999 说明条件没推进）");
            Check("没有退化成 9999 圈死循环", sw.ElapsedMilliseconds < 3000, $"{sw.ElapsedMilliseconds}ms");
        }

        // ==================================================================
        //  [E3] A2 + B1：容器节点自己上报运行状态，且靠编译期反向指针零查找
        // ==================================================================
        internal static void ContainerStatesReportedAndBlueprintLinked()
        {
            Section("[E3] A2 容器状态上报 + B1 反向指针");

            CountingPlugin.Reset();
            var forStep = new ForStep("F", "计次循环", TypeOf(new CountingPlugin()), "状态For");
            forStep.DefaultLoopCount = 1;
            var body = new ActionStep("A", "循环体", TypeOf(new CountingPlugin()), "循环体计数");
            Put(forStep.Children[0], body);

            var ifStep = new ConditionStep("I", "条件", "SomeIfOperator", "顶层If");
            ifStep.Children[0].Expression = "1 == 1";

            var run = ExecHarness.Prepare(new StepModel[] { forStep, ifStep });
            Check("状态用例编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            // A2 的硬前提：上下文必须带会话，否则 UpdateStepRuntimeState 整段被跳过
            var context = run.NewContext(new StubLog());
            run.Engine!.Run(context);

            Check("For 容器跑完落 Success", forStep.State == StepState.Success, $"State={forStep.State}");
            Check("If 容器跑完落 Success", ifStep.State == StepState.Success, $"State={ifStep.State}");
            Check("循环体内的算子也落 Success", body.State == StepState.Success, $"State={body.State}");
            Check("For 容器冻结了非零耗时", forStep.LastRunTimeMs >= 0 && forStep.LastRunStartTimestamp != null,
                $"LastRunTimeMs={forStep.LastRunTimeMs}");
            Check("运行焦点移交到最后一步（执行指针恒定单行）",
                ReferenceEquals(run.Session!.FocusedStep, ifStep),
                $"FocusedStep={run.Session!.FocusedStep?.StepName ?? "(null)"}");

            // B1：每个节点都挂着来源图纸，运行时不再线性扫 Blueprints
            var nodes = run.Engine!.NodeLookup;
            bool allLinked = nodes.Count > 0 && nodes.Values.All(n => n.Blueprint != null);
            Check("NodeLookup 内每个节点都挂了 Blueprint", allLinked,
                $"{nodes.Values.Count(n => n.Blueprint != null)}/{nodes.Count}");
            Check("For 节点的反向指针指回原图纸",
                ReferenceEquals(((CompiledForNode)nodes[forStep.StepID]).Blueprint, forStep), "");
            Check("嵌套的循环体节点同样挂对图纸",
                ReferenceEquals(nodes[body.StepID].Blueprint, body), "");
        }

        // ==================================================================
        //  [E4] A3：同一会话的启动请求必须原子互斥，停止后锁必须干净归还
        // ==================================================================
        internal static void DuplicateStartIsIgnored()
        {
            Section("[E4] A3 会话级启动互斥");

            GatePlugin.Reset();
            var gate = new ActionStep("G", "闸门", TypeOf(new GatePlugin()), "阻塞闸门");
            var run = ExecHarness.Prepare(new StepModel[] { gate });
            Check("闸门图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var locks = new ResourceLockService();
            var log = new StubLog();
            var engine = new FlowEngineService(
                new RuntimeManager(), log, run.Workspace!, null, locks, Core.Interfaces.NullCameraProvider.Instance);
            var session = run.Session!;

            var first = engine.RunSessionAsync(session);
            bool arrived = GatePlugin.Reached.Wait(TimeSpan.FromSeconds(5));
            Check("执行线程已抵达闸门（构造出真实的运行中窗口）", arrived,
                arrived ? "" : "5 秒内没进到算子，后面的并发断言无意义");

            var lockKey = $"FlowSession:{session.SessionID}";
            Check("运行期间会话锁确实被占住", locks.IsLocked(lockKey), lockKey);

            // 第二次启动：抢不到锁就该一声不响地放弃，绝不能覆盖 CancellationTokenSource
            engine.RunSessionAsync(session).Wait(TimeSpan.FromSeconds(5));
            Check("重复启动被忽略并留下 Warn", log.HasWarn("忽略重复的启动请求"),
                string.Join(" | ", log.Warns));
            Check("重复启动没有踩坏会话的取消令牌源", session.CancellationTokenSource != null, "");

            GatePlugin.Proceed.Set();
            engine.StopSession(session);
            Check("停止后执行任务退出", first.Wait(TimeSpan.FromSeconds(5)), "");

            Check("停止后 IsRunning=false 且状态归为 Stopped",
                !session.IsRunning && session.State == SessionState.Stopped,
                $"IsRunning={session.IsRunning} State={session.State}");
            Check("停止后会话锁干净归还", !locks.IsLocked(lockKey), lockKey);

            // 锁泄漏的判据是"这个会话再也启不来"，所以必须真启一次
            GatePlugin.Reached.Reset();
            var second = engine.RunSessionAsync(session);
            bool arrivedAgain = GatePlugin.Reached.Wait(TimeSpan.FromSeconds(5));
            Check("停止后同一会话能再次启动（无锁泄漏）", arrivedAgain,
                arrivedAgain ? "" : "再次启动没进到算子，说明前一轮的锁或令牌没归还干净");
            GatePlugin.Proceed.Set();
            engine.StopSession(session);
            second.Wait(TimeSpan.FromSeconds(5));
        }

        // ==================================================================
        //  [E5] B2：运行状态不得惊动 Version（防"通知风暴 → 每轮全量重编译"回归）
        // ==================================================================
        internal static void RuntimeStateDoesNotInvalidateVersion()
        {
            Section("[E5] B2 运行状态通知降频");

            var step = new ActionStep("A", "计数", TypeOf(new CountingPlugin()), "复位用步骤");
            var flow = new FlowModel { FlowName = "通知风暴断言" };
            flow.Steps.Add(step);

            // 运行状态是发在步骤自己身上的通知（订 flow 会一条都收不到，等于没验）
            var seen = new List<string>();
            step.PropertyChanged += (_, e) => seen.Add(e.PropertyName ?? "(null)");
            // flow 这一路必须全程静默：一旦有步骤发空串，FlowModel 会当成语义变更 → Version++ → 全量重编译
            var flowNotices = new List<string>();
            flow.PropertyChanged += (_, e) => flowNotices.Add(e.PropertyName ?? "(null)");

            int versionBefore = flow.Version;
            step.State = StepState.Running;
            step.IsRunningFocus = true;
            step.BeginTiming();
            step.State = StepState.Success;
            step.EndTiming();
            step.ResetState();
            step.ResetState(); // 第二轮：本来就该零通知，验证 ResetState 的自检闸门

            Check("运行状态连改 7 次不递增 Version", flow.Version == versionBefore,
                $"Version {versionBefore} → {flow.Version}");
            Check("状态变更确实发了通知（界面不会不刷新）", seen.Count > 0,
                $"收到 {seen.Count} 条：{string.Join(",", seen.Distinct())}");
            Check("状态通知全部记名（无空串广播）", !seen.Any(string.IsNullOrEmpty),
                $"共 {seen.Count} 条，每条都带属性名");
            Check("流程对象一路静默（没被步骤的空串通知惊到）", flowNotices.Count == 0,
                $"flow 收到 {flowNotices.Count} 条：{string.Join(",", flowNotices.Distinct())}");
            Check("通知的属性名都在 [RuntimeState] 名单内",
                seen.All(n => n is nameof(StepModel.State)
                            or nameof(StepModel.IsRunningFocus)
                            or nameof(StepModel.LastRunStartTimestamp)
                            or nameof(StepModel.LastRunTimeMs)
                            or nameof(StepModel.CurrentRunTimeMs)),
                string.Join(",", seen.Except(new[] { nameof(StepModel.State) }).Distinct()));

            // 语义变更必须仍然惊动 Version —— 否则上面那条"不递增"是靠关掉了整套机制做到的
            step.Description = "语义变更";
            Check("语义变更仍能触发 Version++（闸门没被误焊死）", flow.Version == versionBefore + 1,
                $"Version={flow.Version}");
        }

        // ==================================================================
        //  [E6] B3：注册会话不得长时间占住集合锁（WPF 绑定锁与注册锁分家）
        // ==================================================================
        internal static void RegistryLockDoesNotBlockCollection()
        {
            Section("[E6] B3 注册与会话集合锁分家");

            RuntimeManager manager;
            try
            {
                manager = new RuntimeManager();
            }
            catch (Exception ex)
            {
                Check("控制台进程内可构造 RuntimeManager", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            Check("RuntimeManager 构造未抛（集合同步已启用）", true, "");

            Exception readerBoom = null;
            int lookups = 0;
            var reading = new Thread(() =>
            {
                try
                {
                    // 读的是公开查询面（ShellViewModel 就是这么找会话的），不是裸 foreach 源集合：
                    // EnableCollectionSynchronization 只承诺 WPF 的 CollectionView 能安全读，
                    // 应用代码想安全读就得自己走集合锁 —— 这条断言守的正是"查询有没有老实上锁"。
                    for (int i = 0; i < 20000; i++)
                    {
                        _ = manager.GetSessionByName($"并发注册会话{i % 20}");
                        _ = manager.GetSessionById("(这个 id 不存在)");
                        lookups++;
                    }
                }
                catch (Exception ex) { readerBoom = ex; }
            }) { IsBackground = true };
            reading.Start();

            for (int i = 0; i < 20; i++)
                manager.RegisterSession(new FlowSession { FlowName = $"并发注册会话{i}" });

            bool joined = reading.Join(TimeSpan.FromSeconds(5));
            Check("注册期间并发查询未卡死", joined, joined ? "" : "读线程 5 秒没跑完，集合锁被长时间占住");
            Check("边注册边查询未抛异常", readerBoom == null,
                readerBoom == null ? $"完成 {lookups} 轮查询" : readerBoom.Message);
            Check("20 个会话注册后同名替换未堆积", manager.SnapshotSessions().Count(s => s.FlowName!.StartsWith("并发注册会话")) == 20,
                $"实际 {manager.SnapshotSessions().Count}");

            manager.ClearAll();
            Check("ClearAll 后集合清空", manager.SnapshotSessions().Count == 0, $"残留 {manager.SnapshotSessions().Count}");
        }

        // ==================================================================
        //  [E7] For.LoopCount 连线取运行时变量：编译期挂上代理端口，运行期圈数由变量值决定
        //       这是「画布长值脚 → 拖线 → 存盘 → 编译 → 执行」这条链的最后一环：
        //       前面几段验的是数据形如什么样，这里验它真能改变机器行为。
        // ==================================================================
        internal static void ForLoopCountReadsRuntimeVariable()
        {
            Section("[E7] For.LoopCount 吃运行时变量");

            // 图纸：[变量定义(写 loopN) → For(弹窗默认 99 圈，体内一个计数桩)]
            (StepModel[] Blueprints, ForStep Loop) Build(bool withProducer)
            {
                var producer = new ActionStep("V", "变量定义", TypeOf(new LoopVarPlugin()), "写入loopN");
                var loop = new ForStep("F", "计次循环", TypeOf(new CountingPlugin()), "变量圈数For")
                {
                    DefaultLoopCount = 99
                };
                loop.SetLink("LoopCount", new LinkReference(
                    LinkKind.RuntimeVariable,
                    LinkProtocol.RuntimeVariableMarkerGuid,
                    "loopN",
                    "Runtime.loopN"));
                Put(loop.Children[0], new ActionStep("A", "计数", TypeOf(new CountingPlugin()), "循环体计数"));

                return (withProducer
                    ? new StepModel[] { producer, loop }
                    : new StepModel[] { loop }, loop);
            }

            // ---- 正常值：连线值覆盖弹窗默认次数 ----
            CountingPlugin.Reset();
            LoopVarPlugin.Value = 3;
            var (bp, forStep) = Build(withProducer: true);
            var run = ExecHarness.Prepare(bp);
            Check("带变量线的图纸编译通过（marker Guid 不会被 CheckLinkOrder 当野 Id）", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var forNode = (CompiledForNode)run.Engine!.NodeLookup[forStep.StepID];
            var proxy = forNode.LoopCountLink as RuntimeVariableProxyPort;
            Check("编译期把变量线接成 LoopCountLink 代理端口（变量名 + int 类型）",
                proxy != null && proxy.VariableName == "loopN" && proxy.DataType == typeof(int),
                forNode.LoopCountLink == null
                    ? "(null)"
                    : proxy == null ? forNode.LoopCountLink.GetType().Name : $"{proxy.VariableName}/{proxy.DataType.Name}");
            Check("弹窗默认次数仍留在节点上（只有连线取不到值时才用）",
                forNode.DefaultLoopCount == 99, $"{forNode.DefaultLoopCount}");

            var log = new StubLog();
            run.Engine.Run(run.NewContext(log));
            Check("圈数由变量值决定（3 圈），不是弹窗默认值 99", CountingPlugin.Runs == 3, $"实际 {CountingPlugin.Runs}");
            Check("正常值不产生 Warn", log.Warns.Count == 0, string.Join(" | ", log.Warns));

            // ---- 脏值：变量被写成非数值字符串 → 不炸流程，Warn 留痕后回落默认次数 ----
            CountingPlugin.Reset();
            LoopVarPlugin.Value = "abc";
            var run2 = ExecHarness.Prepare(Build(withProducer: true).Blueprints);
            Check("脏值图纸编译通过", run2.Compiled, run2.Errors);
            if (run2.Compiled)
            {
                var log2 = new StubLog();
                run2.Engine!.Run(run2.NewContext(log2));
                Check("脏值回落弹窗默认次数（99 圈）且不抛穿流程", CountingPlugin.Runs == 99, $"实际 {CountingPlugin.Runs}");
                Check("脏值有 Warn 留痕", log2.HasWarn("无法转为整数"), string.Join(" | ", log2.Warns));
            }

            // ---- 负数：按 0 圈处理 ----
            CountingPlugin.Reset();
            LoopVarPlugin.Value = -1;
            var run3 = ExecHarness.Prepare(Build(withProducer: true).Blueprints);
            if (run3.Compiled)
            {
                var log3 = new StubLog();
                run3.Engine!.Run(run3.NewContext(log3));
                Check("负数圈数按 0 次处理", CountingPlugin.Runs == 0, $"实际 {CountingPlugin.Runs}");
                Check("负数有 Warn 留痕", log3.HasWarn("按 0 次处理"), string.Join(" | ", log3.Warns));
            }
            else
            {
                Check("负数图纸编译通过", false, run3.Errors);
            }

            // ---- 变量没人写：代理端口对 int 缺键返回 0（非 null），因此**不**回落默认次数 ----
            CountingPlugin.Reset();
            var run4 = ExecHarness.Prepare(Build(withProducer: false).Blueprints);
            Check("缺上游定义的图纸照样编译通过（类型校验在建线期不做）", run4.Compiled, run4.Errors);
            if (run4.Compiled)
            {
                run4.Engine!.Run(run4.NewContext(new StubLog()));
                Check("变量缺键 → 取到 int 默认值 0 → 跑 0 圈（现行为：不回落默认次数）",
                    CountingPlugin.Runs == 0, $"实际 {CountingPlugin.Runs}");
            }

            LoopVarPlugin.Value = 3;
        }

        // ==================================================================
        //  [E8] 条件分支的失败语义 / 类型归一 / 分支可达性
        //
        //  这一段守的是"编译期口径 = 运行期口径"这条线，以及"结果未知时绝不许蒙"这条纪律。
        //  三组条件求值用例共用同一套图纸形状（[变量定义桩 → If]），只改变量声明类型与写入值，
        //  每组都能给出新旧实现行为相反的证据，而不是"新代码也能过"的同义反复。
        // ==================================================================
        internal static void ConditionBranchFailureSemantics()
        {
            Section("[E8] 条件分支失败语义与类型归一");

            // 图纸：[变量定义(写 loopN) → If(loopN 条件)，Else 分支里放计数桩]
            // 计数桩刻意只放在 Else 里：跑起来就说明"条件没判成"或"判断被吞了"，正是要抓的病灶。
            (StepModel[] Blueprints, ConditionStep If) BuildIf(string expression, bool withProducer)
            {
                var ifStep = new ConditionStep("I", "条件", "SomeIfOperator", "类型归一If");
                ifStep.Children[0].Expression = expression;
                // 声明成 int：DynamicExpresso 的形参类型就是 int，收窄转换会当场炸
                ifStep.RuntimeVariableRefs.Add(
                    new LocalVariableItem { Name = "loopN", DataTypeName = "System.Int32" });
                Put(ifStep.Children[1], new ActionStep("A", "计数", TypeOf(new CountingPlugin()), "Else内计数"));

                var producer = new ActionStep("V", "变量定义", TypeOf(new LoopVarPlugin()), "写入loopN");
                return (withProducer
                    ? new StepModel[] { producer, ifStep }
                    : new StepModel[] { ifStep }, ifStep);
            }

            // ---- ① P0-1 反证：条件求值抛异常，必须停机，绝不能"试下一个分支" ----
            // 变量声明 int、值被写成非数字字符串 → 归一失败。
            // 旧实现：catch 只记一条日志就 continue → 轮到 Else 的恒真兜底 → Else 里的算子跑起来，步骤还标 Success。
            CountingPlugin.Reset();
            LoopVarPlugin.Value = "abc";
            var (bpBad, ifBad) = BuildIf("loopN == 1", withProducer: true);
            var runBad = ExecHarness.Prepare(bpBad);
            Check("脏值图纸编译通过（类型校验不拦运行期脏数据）", runBad.Compiled, runBad.Errors);
            if (!runBad.Compiled) { LoopVarPlugin.Value = 3; return; }

            Exception boom = null;
            try { runBad.Engine!.Run(runBad.NewContext(new StubLog())); }
            catch (Exception ex) { boom = ex; }

            Check("条件求值失败会抛穿流程（不再被吞掉）", boom != null,
                boom == null ? "没抛：条件求值异常又被吃掉了" : boom.GetType().Name);
            Check("If 节点落 Failed（不是悄悄走 Else 还标绿）", ifBad.State == StepState.Failed,
                $"State={ifBad.State}");
            Check("Else 分支一个算子都没跑（旧实现此处为 1，即走错分支）", CountingPlugin.Runs == 0,
                $"实际 Runs={CountingPlugin.Runs}");

            // ---- ② P0-2 正向钉住：变量声明 int、值是 double → 归一后照常命中 If ----
            // 旧实现：Invoke([2.0]) 收窄炸 → 被吞 → 走 Else（Runs 记在 Else 上）。
            CountingPlugin.Reset();
            LoopVarPlugin.Value = 2.0;
            var (bpWide, ifWide) = BuildIf("loopN == 2", withProducer: true);
            var runWide = ExecHarness.Prepare(bpWide);
            Check("double 值图纸编译通过", runWide.Compiled, runWide.Errors);
            if (runWide.Compiled)
            {
                runWide.Engine!.Run(runWide.NewContext(new StubLog()));
                Check("声明 int 收到 double 2.0 → 归一为 2，If 命中（Else 计数保持 0）",
                    CountingPlugin.Runs == 0 && ifWide.State == StepState.Success,
                    $"Runs={CountingPlugin.Runs} State={ifWide.State}");
            }

            // ---- ③ P0-3 钉住：变量没人写 → 兜底值必须是 int 的 0，不能是 double 的 0.0 ----
            CountingPlugin.Reset();
            var (bpEmpty, ifEmpty) = BuildIf("loopN == 0", withProducer: false);
            var runEmpty = ExecHarness.Prepare(bpEmpty);
            Check("无写入者的图纸编译通过", runEmpty.Compiled, runEmpty.Errors);
            if (runEmpty.Compiled)
            {
                runEmpty.Engine!.Run(runEmpty.NewContext(new StubLog()));
                Check("缺值兜底给 int 的 0（注 0.0 会因收窄炸掉）",
                    CountingPlugin.Runs == 0 && ifEmpty.State == StepState.Success,
                    $"Runs={CountingPlugin.Runs} State={ifEmpty.State}");
            }

            // ---- ④ While 撞迭代上限：标 Failed 并上抛，不许"少跑几圈还标绿" ----
            CountingPlugin.Reset();
            var whileStep = new WhileStep("W", "条件循环", "SomeWhileOperator", "死循环While");
            whileStep.Children[0].Expression = "1 == 1"; // 恒真：只能靠上限掐断
            Put(whileStep.Children[0], new ActionStep("A", "计数", TypeOf(new CountingPlugin()), "每圈计数"));

            var runLoop = ExecHarness.Prepare(new StepModel[] { whileStep });
            Check("恒真 While 图纸编译通过", runLoop.Compiled, runLoop.Errors);
            if (runLoop.Compiled)
            {
                // 上限调小，避免真跑 9999 圈；引擎语义与上限大小无关
                ((CompiledWhileNode)runLoop.Engine!.NodeLookup[whileStep.StepID]).MaxIterations = 5;

                Exception loopBoom = null;
                try { runLoop.Engine.Run(runLoop.NewContext(new StubLog())); }
                catch (Exception ex) { loopBoom = ex; }

                Check("撞上限会抛穿流程", loopBoom != null,
                    loopBoom == null ? "没抛：循环被掐断却当成正常结束" : loopBoom.GetType().Name);
                Check("While 节点落 Failed", whileStep.State == StepState.Failed, $"State={whileStep.State}");
                Check("恰好跑满上限 5 圈（不多不少）", CountingPlugin.Runs == 5, $"实际 Runs={CountingPlugin.Runs}");
            }

            // ---- ④b 反证 ④：条件恰好在上限那一圈转假 = 正常跑完，绝不能误报死循环 ----
            // 与 ④ 的差别只有"退出原因"：④ 是条件恒真被掐断（该抛），本组是条件自然转假（不该抛）。
            // 旧实现用 while (iter < MaxIterations) 当守卫，退出后无法区分这两种原因 → 一律抛"疑似死循环"，
            // 于是 `i < N` 配 MaxIterations = N 这种完全合法的写法会让整条流程 Faulted。
            // 默认上限就是 9999，工业连续场景真会撞上，所以这不是纸面问题。
            CountingPlugin.Reset();
            var whileExact = new WhileStep("W", "条件循环", "SomeWhileOperator", "刚好跑满While");
            whileExact.Children[0].Expression = "Counter < 5"; // 第 5 圈结束后转假
            whileExact.RuntimeVariableRefs.Add(
                new LocalVariableItem { Name = "Counter", DataTypeName = "System.Double" });
            Put(whileExact.Children[0], new ActionStep("A", "累加计数", TypeOf(new CounterStepPlugin()), "每圈累加"));

            var runExact = ExecHarness.Prepare(new StepModel[] { whileExact });
            Check("刚好跑满上限的图纸编译通过", runExact.Compiled, runExact.Errors);
            if (runExact.Compiled)
            {
                // 上限恰好等于条件允许的圈数：Counter 从 0 走到 5，正好 5 圈后转假
                ((CompiledWhileNode)runExact.Engine!.NodeLookup[whileExact.StepID]).MaxIterations = 5;

                Exception exactBoom = null;
                try { runExact.Engine.Run(runExact.NewContext(new StubLog())); }
                catch (Exception ex) { exactBoom = ex; }

                Check("条件恰好在上限那一圈转假 → 不误报死循环（旧实现此处必抛）", exactBoom == null,
                    exactBoom == null ? "" : exactBoom.GetType().Name + ": " + exactBoom.Message);
                Check("While 节点落 Success（不是 Failed）",
                    whileExact.State == StepState.Success, $"State={whileExact.State}");
                Check("恰好跑 5 圈（不多不少）", CountingPlugin.Runs == 5, $"实际 Runs={CountingPlugin.Runs}");
            }

            // ---- ⑤ Else 之后的分支不可达：编译期就得拦下来 ----
            var unordered = new ConditionStep("I", "条件", "SomeIfOperator", "乱序If");
            unordered.Children[0].Expression = "1 == 1";
            // 构造时是 [If, Else]，追加 ElseIf 后成 [If, Else, ElseIf] —— Else 恒真，ElseIf 永远轮不到
            unordered.Children.Add(
                new StepCollection
                {
                    BranchType = BranchType.ElseIf,
                    StepName = "ElseIf 分支",
                    Expression = "1 == 1",
                });

            var runOrder = ExecHarness.Prepare(new StepModel[] { unordered });
            Check("Else 之后的分支被判为不可达并阻断编译",
                !runOrder.Compiled && runOrder.Errors.Contains("不可达"), runOrder.Errors);

            // ---- ⑥ 识别口径只有一份：认算子类型名，不认可能被改的显示名 ----
            var renamed = new ConditionStep("I", "条件判断", "BuiltIn_If", "改名If");
            Check("ConditionStep.IsIfLike 认类型名而非显示名",
                renamed.IsIfLike && renamed.Children.Count == 2
                    && renamed.Children[1].BranchType == BranchType.Else,
                $"IsIfLike={renamed.IsIfLike} Children={renamed.Children.Count}");
            Check("非 If 型条件算子不会误判成 If",
                !new ConditionStep("I", "条件", "BuiltIn_While", "非If").IsIfLike, "");

            // ---- ⑦ 只读集合 Children 的存盘往返：必须"整体替换"，不能"追加" ----
            // Newtonsoft 对无 setter 的集合属性默认走 ObjectCreationHandling.Auto（= Populate/Add），
            // 而 ConditionStep/ForStep/WhileStep 的构造函数已预建分支；若被 Populate，反序列化会 Append 而非替换 → 分支翻倍。
            // 项目里 SolutionModel/FlowModel 的同类集合都显式标了 Replace，这几个步骤模型却没标，故实测一次。
            var ifProbe = new ConditionStep("I", "条件", "BuiltIn_If", "往返探针If");
            ifProbe.Children[0].Expression = "1 == 1";
            var ifBack = RoundTrip(ifProbe);
            Check("If 图纸存盘往返后分支不重复且条件保留（应为 2 且含表达式）",
                ifBack.Children.Count == 2 && ifBack.Children[0].Expression == "1 == 1",
                $"实际 {ifBack.Children.Count}（{string.Join(",", ifBack.Children.Select(c => c.BranchType))}）Expr0='{ifBack.Children[0].Expression}'");

            var forBack = RoundTrip(new ForStep("F", "For", "BuiltIn_For", "往返探针For"));
            Check("For 图纸存盘往返后循环体不重复（应为 1）",
                forBack.Children.Count == 1, $"实际 {forBack.Children.Count}");

            var whileBack = RoundTrip(new WhileStep("W", "While", "BuiltIn_While", "往返探针While"));
            Check("While 图纸存盘往返后循环体不重复（应为 1）",
                whileBack.Children.Count == 1, $"实际 {whileBack.Children.Count}");

            LoopVarPlugin.Value = 3;
        }

        // ==================================================================
        //  [E9] 会话生命周期的锁归还纪律（A 组并发修复的防回归面）
        //
        //  这四条断言的共同命题只有一句：**收尾路径上无论谁抛异常，会话锁都必须还回去**。
        //  锁漏还的后果不是"少跑一次"，而是该会话永久停在"已在运行中"，
        //  除了重启软件再也启不来 —— 现场表现是"点启动没反应"，极难定位。
        //
        //  所以这里不是"新代码也能过"的同义反复，而是让订阅者主动去抛异常、
        //  主动去释放收尾还要用到的对象，把旧实现的漏还现场真做出来。
        // ==================================================================
        internal static void SessionLockAlwaysReturned()
        {
            Section("[E9] A 组 会话锁归还纪律");

            // Task.Wait 在任务 Faulted 时会抛 AggregateException，而"旧实现收尾炸掉"正是要抓的东西，
            // 所以把"跑完了"和"炸出来了"都收成一个 bool，别让断言自己先炸。
            static bool RanClean(Task t)
            {
                try { return t.Wait(TimeSpan.FromSeconds(5)); }
                catch { return false; }
            }

            GatePlugin.Reset();
            var gate = new ActionStep("G", "闸门", TypeOf(new GatePlugin()), "生命周期闸门");
            var run = ExecHarness.Prepare(new StepModel[] { gate });
            Check("生命周期图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var locks = new ResourceLockService();
            var log = new StubLog();
            var manager = new RuntimeManager();
            var engine = new FlowEngineService(manager, log, run.Workspace!, null, locks, Core.Interfaces.NullCameraProvider.Instance);
            var session = run.Session!;
            var lockKey = $"FlowSession:{session.SessionID}";

            // 订阅者固定挂在 engine 上，用 mode 切换"怎么捣乱"。
            // 只对 Stopped 动手：Running/Faulted 那两次通知在 try 体内，会被业务 catch 提前接走，
            // 验不到我们真正要守的收尾路径。
            int mode = 0; // 0=不捣乱 1=Stopped 时抛异常 2=Stopped 时释放 PauseLock
            engine.SessionStateChanged += (_, e) =>
            {
                if (e.NewState != SessionState.Stopped) return;
                if (mode == 1)
                    throw new InvalidOperationException("订阅者故意在 Stopped 上炸");
                if (mode == 2)
                    session.PauseLock.Dispose();
            };

            // ---- ① 订阅者抛异常：锁照还、任务照正常结束 ----
            // 旧实现：异常从 finally 里爬出来，把后面的 RecordSessionEnd / PauseLock.Set /
            // CTS.Dispose / ReleaseSession 全部跳过 → 锁永久泄漏 + 任务 Faulted。
            mode = 1;
            var first = engine.RunSessionAsync(session);
            bool arrived = GatePlugin.Reached.Wait(TimeSpan.FromSeconds(5));
            Check("执行线程已抵达闸门（构造出运行中的窗口）", arrived, arrived ? "" : "5 秒内没进到算子");
            GatePlugin.Proceed.Set();
            // 连续执行是死循环（只在取消时退出），必须显式停，否则任务永远不结束
            engine.StopSession(session);

            Check("订阅者抛异常不影响本轮正常收尾（任务不 Faulted）", RanClean(first),
                first.IsFaulted ? $"任务已 Faulted：{first.Exception?.GetBaseException().Message}" : "");
            Check("订阅者异常被就地隔离并留痕",
                log.Errors.Any(x => x.Contains("状态变更订阅者抛出异常")),
                string.Join(" | ", log.Errors));
            Check("【核心】订阅者抛异常后会话锁仍被归还", !locks.IsLocked(lockKey), lockKey);
            Check("停稳后 IsRunning=false 且状态归为 Stopped",
                !session.IsRunning && session.State == SessionState.Stopped,
                $"IsRunning={session.IsRunning} State={session.State}");

            // ---- ② 收尾窗口里 PauseLock 被释放：锁照还、任务照正常结束 ----
            // 真实场景：IsRunning 已置 false，RuntimeManager.RemoveAndDispose 据此判定"跑完了"
            // 并去 session.Dispose()（内含 PauseLock.Dispose()），而收尾还没走到 PauseLock.Set()。
            // 这里让订阅者替 RemoveAndDispose 动手，把这个窗口稳定做出来。
            // 旧实现：PauseLock.Set() 抛 ObjectDisposedException → 跳过 CTS.Dispose 与 ReleaseSession → 锁泄漏。
            mode = 2;
            GatePlugin.Reached.Reset();
            var second = engine.RunSessionAsync(session);
            bool arrivedAgain = GatePlugin.Reached.Wait(TimeSpan.FromSeconds(5));
            GatePlugin.Proceed.Set();
            engine.StopSession(session);

            // 这条必须先过，否则下面那句"任务正常结束"是空过的：
            // 一旦①把锁泄漏了，②会被 TryOccupySession 挡在门外、直接返回一个已完成的空任务，
            // 于是"正常结束"假通过 —— 灵敏度实测（把 finally 还原成旧结构）正是观察到了这个现象。
            Check("第二次执行确实进到了算子（不是被残留的锁拒之门外）", arrivedAgain,
                arrivedAgain ? "" : "没进到算子：上一轮的会话锁没归还");
            Check("收尾窗口里 PauseLock 被释放，任务仍正常结束", RanClean(second),
                second.IsFaulted ? $"任务已 Faulted：{second.Exception?.GetBaseException().Message}" : "");
            Check("【核心】PauseLock 被提前释放后会话锁仍被归还", !locks.IsLocked(lockKey), lockKey);

            mode = 0;

            // ---- ③ 对已释放的取消令牌调 StopSession：不抛 ----
            // 白盒构造（同 [E8] ④ 用反射改 MaxIterations 的理由）：真实时序是"循环线程在 finally 里
            // Dispose 了令牌"与"退出任务/停止按钮调 Cancel"撞车，窗口窄且不可控；
            // 这里直接把那个中间态摆出来，把"公共 StopSession 必须容错"这个契约钉死。
            // 旧实现：属性读两次，Cancel() 打在已释放的 CTS 上 → ObjectDisposedException 冲出公共 API。
            session.IsRunning = true;
            session.CancellationTokenSource = new CancellationTokenSource();
            session.CancellationTokenSource.Dispose();

            Exception stopBoom = null;
            try { engine.StopSession(session); }
            catch (Exception ex) { stopBoom = ex; }
            Check("对已释放的取消令牌调 StopSession 不抛", stopBoom == null,
                stopBoom == null ? "" : stopBoom.GetType().Name + ": " + stopBoom.Message);

            session.IsRunning = false;
            session.CancellationTokenSource = null;

            // ---- ④ 边注册边 StopAll / ActiveSessionCount：裸枚举会撞 "Collection was modified" ----
            // 生产触发路径：HTTP 请求线程 RegisterSession 的同时，退出任务在跑 StopAll。
            // 旧实现直接遍历 ActiveSessions，撞上增删即抛 —— 而 StopAll 是退出任务的一环，
            // 抛出去会拖住整条退出链。
            Exception enumBoom = null;
            var reading = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < 10000; i++)
                    {
                        _ = engine.ActiveSessionCount;
                        engine.StopAll();
                    }
                }
                catch (Exception ex) { enumBoom = ex; }
            }) { IsBackground = true };
            reading.Start();

            for (int i = 0; i < 300; i++)
                manager.RegisterSession(new FlowSession { FlowName = $"生命周期并发会话{i % 10}" });

            bool joined = reading.Join(TimeSpan.FromSeconds(10));
            Check("StopAll/ActiveSessionCount 与注册并发时未卡死", joined, joined ? "" : "10 秒没跑完");
            Check("StopAll/ActiveSessionCount 与注册并发时未抛异常", enumBoom == null,
                enumBoom == null ? "" : enumBoom.GetType().Name + ": " + enumBoom.Message);
        }

        // ==================================================================
        //  [E10] 编译期静默失效收口（C 组）
        //
        //  这一段的共同命题：**"取到了数据源却无处安放"绝不能无声无息**。
        //  旧实现在这些出口一律什么都不做 —— sourcePort 留 null，公共赋值段整段被跳过，
        //  用户拿到的是"编译成功"，但那条线永远不生效。现场表现为"变量没值 / 圈数不对"，
        //  却找不到任何错误提示，只能通读图纸猜。
        //
        //  另两条（重复 Id / While 无分支）原本分别会炸成"系统崩溃级错误"和静默空操作。
        //
        //  【为什么不用带输出口的桩】这几条路径的错误都在"查输出端口"之前发出，
        //  只需要"上游是一个真算子"；而 ExecutionHarness 的桩刻意不声明端口（见该文件注释）。
        // ==================================================================
        internal static void CompileTimeSilentFailuresAreReported()
        {
            Section("[E10] C 组 编译期静默失效收口");

            string counting = TypeOf(new CountingPlugin());

            // ---- ① #12-2 重复 StepId：不再冒到顶层变成无定位的"系统崩溃级错误" ----
            var dupA = new ActionStep("A", "计数", counting, "重复Id甲");
            var dupB = new ActionStep("B", "计数", counting, "重复Id乙");
            dupB.StepID = dupA.StepID; // 手工制造重复 Id（StepID 是可写属性）
            var runDup = ExecHarness.Prepare(new StepModel[] { dupA, dupB });
            Check("重复 StepId 不再炸成无定位的'系统崩溃级错误'",
                !runDup.Compiled && runDup.Errors.Contains("[结构错误]") && runDup.Errors.Contains("Id 重复"),
                runDup.Errors);
            Check("重复 StepId 的错误带上了节点名（可定位）",
                runDup.Errors.Contains("重复Id乙"), runDup.Errors);

            // ---- ② #12-3 While 没有循环分支：不再静默编成空操作 ----
            var emptyWhile = new WhileStep("W", "条件循环", "SomeWhileOperator", "空体While");
            emptyWhile.Children.Clear();
            var runEmptyWhile = ExecHarness.Prepare(new StepModel[] { emptyWhile });
            Check("While 没有循环分支 → 编译报错（不再静默成空操作）",
                !runEmptyWhile.Compiled && runEmptyWhile.Errors.Contains("没有循环分支"),
                runEmptyWhile.Errors);

            // ---- ③ #10-1 上游是条件节点：它不提供输出端口，连线必须报错 ----
            var ifUp = new ConditionStep("I", "条件", "SomeIfOperator", "上游If");
            ifUp.Children[0].Expression = "1 == 1";
            var sinkCondUp = new ActionStep("A", "计数", counting, "条件上游的下游");
            sinkCondUp.LinkedSources["InImage"] = new LinkReference
            {
                Kind = LinkKind.StepPort,
                TargetStepId = ifUp.StepID,
                TargetPortName = "Value",
            };
            var runCondUp = ExecHarness.Prepare(new StepModel[] { ifUp, sinkCondUp });
            Check("上游是 If 节点（无输出端口）→ 连线报错，不再无声失效",
                !runCondUp.Compiled && runCondUp.Errors.Contains("不提供输出端口"),
                runCondUp.Errors);

            // ---- ④ #10-2 RuntimeVariable 连到不存在的输入名 ----
            var sinkBadInput = new ActionStep("A", "计数", counting, "错输入名的下游");
            sinkBadInput.LinkedSources["不存在的输入"] = new LinkReference
            {
                Kind = LinkKind.RuntimeVariable,
                TargetStepId = LinkProtocol.RuntimeVariableMarkerGuid,
                TargetPortName = "someVar",
            };
            var runBadInput = ExecHarness.Prepare(new StepModel[] { sinkBadInput });
            Check("运行时变量连到不存在的输入名 → 连线报错",
                !runBadInput.Compiled && runBadInput.Errors.Contains("没有名为"),
                runBadInput.Errors);

            // ---- ⑤ #10-3 For 的连线键不是 LoopCount ----
            var badFor = new ForStep("F", "计次循环", "BuiltIn_For", "错键For");
            badFor.DefaultLoopCount = 1;
            badFor.LinkedSources["Bogus"] = new LinkReference
            {
                Kind = LinkKind.RuntimeVariable,
                TargetStepId = LinkProtocol.RuntimeVariableMarkerGuid,
                TargetPortName = "loopN",
            };
            var runBadFor = ExecHarness.Prepare(new StepModel[] { badFor });
            Check("For 的连线键不是 LoopCount → 连线报错",
                !runBadFor.Compiled && runBadFor.Errors.Contains("只接受循环次数端口"),
                runBadFor.Errors);

            // ---- ⑥ #12-1 端口下标溢出：OverflowException 变成定位错误 ----
            var idxUp = new ActionStep("U", "计数", counting, "下标上游");
            var idxDown = new ActionStep("D", "计数", counting, "下标下游");
            idxDown.LinkedSources["InImage"] = new LinkReference
            {
                Kind = LinkKind.StepPort,
                TargetStepId = idxUp.StepID,
                TargetPortName = "Value[99999999999999]",
            };
            var runOverflow = ExecHarness.Prepare(new StepModel[] { idxUp, idxDown });
            Check("端口下标超出整数范围 → 定位错误（不再炸成'系统崩溃级错误'）",
                !runOverflow.Compiled && runOverflow.Errors.Contains("超出整数范围"),
                runOverflow.Errors);
            Check("下标溢出的错误带上了出错节点名（可定位）",
                runOverflow.Errors.Contains("下标下游"), runOverflow.Errors);

            // ---- ⑦ 端口名格式非法：同一条正则的另一半出口 ----
            var badNameDown = new ActionStep("D", "计数", counting, "坏名下游");
            badNameDown.LinkedSources["InImage"] = new LinkReference
            {
                Kind = LinkKind.StepPort,
                TargetStepId = idxUp.StepID,
                TargetPortName = "Value[abc]",
            };
            var runBadName = ExecHarness.Prepare(new StepModel[] { idxUp, badNameDown });
            Check("端口名格式非法 → 定位错误，不再无声失效",
                !runBadName.Compiled && runBadName.Errors.Contains("格式非法"),
                runBadName.Errors);
        }

        // ==================================================================
        //  [E11] 会话状态转换语义（D 组）
        //
        //  三条命题：
        //   ① OldState 必须是真的"旧值"，不能恒等于 NewState
        //      （CommStress 压测程序正是靠 OldState 统计状态转换的，恒等 = 那个工具在统计空气）
        //   ② 一次状态转换只触发一次 PropertyChanged —— 用来反证清单里"会触发两次"的推测
        //   ③ 单次执行不接受暂停且拒绝时留痕；连续执行照常可暂停（防止语义澄清改过头）
        // ==================================================================
        internal static void SessionStateTransitionSemantics()
        {
            Section("[E11] D 组 状态转换语义");

            // 引擎里"停止"是个合法出口：循环可能正好停在 PauseLock.Wait(token) 上，
            // 此时令牌取消会让它抛 OperationCanceledException，任务状态变 Canceled 而不是 RanToCompletion。
            // 两种都算"退出了"，只有 Faulted 才是异常。
            static bool EndedCleanly(Task t)
            {
                try
                {
                    t.Wait(TimeSpan.FromSeconds(5));
                    return true;
                }
                catch (AggregateException ae)
                    when (t.IsCanceled && ae.InnerExceptions.All(x => x is TaskCanceledException))
                {
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            GatePlugin.Reset();
            var gate = new ActionStep("G", "闸门", TypeOf(new GatePlugin()), "状态语义闸门");
            var run = ExecHarness.Prepare(new StepModel[] { gate });
            Check("状态语义图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var log = new StubLog();
            var engine = new FlowEngineService(
                new RuntimeManager(), log, run.Workspace!, null, new ResourceLockService(), Core.Interfaces.NullCameraProvider.Instance);
            var session = run.Session!;

            // ---- ① + ② 同时采集：转换序列 + session 上 State 通知次数 ----
            var transitions = new List<(SessionState Old, SessionState New)>();
            engine.SessionStateChanged += (_, e) =>
            {
                lock (transitions) transitions.Add((e.OldState, e.NewState));
            };

            int stateNotices = 0;
            session.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FlowSession.State)) Interlocked.Increment(ref stateNotices);
            };

            var continuous = engine.RunSessionAsync(session);
            GatePlugin.Reached.Wait(TimeSpan.FromSeconds(5));
            GatePlugin.Proceed.Set();
            engine.StopSession(session);
            Check("连续执行正常收尾", EndedCleanly(continuous), continuous.IsFaulted ? "任务 Faulted" : "");

            List<(SessionState Old, SessionState New)> snapshot;
            lock (transitions) snapshot = new List<(SessionState, SessionState)>(transitions);
            string seq = string.Join(" → ", snapshot.Select(t => $"{t.Old}▶{t.New}"));

            var stoppedStep = snapshot.FirstOrDefault(t => t.New == SessionState.Stopped);
            Check("【核心】OldState 不再恒等于 NewState（Stopped 事件的旧状态是 Running）",
                stoppedStep.Old == SessionState.Running && stoppedStep.New == SessionState.Stopped,
                $"实际 {snapshot.Count} 次转换: {seq}");
            Check("本次运行恰好两次转换 Running / Stopped（无冗余通知）",
                snapshot.Count == 2, seq);
            Check("一次状态转换只触发一次 State 通知（反证清单'触发两次'的推测）",
                stateNotices == 2, $"实际 {stateNotices} 次");

            // ---- ③a 单次执行：暂停必须被拒绝并留痕 ----
            GatePlugin.Reset();
            var single = engine.RunSessionOnceAsync(session);
            bool inSingle = GatePlugin.Reached.Wait(TimeSpan.FromSeconds(5));
            Check("单次执行已进入算子（构造出真实运行窗口）", inSingle,
                inSingle ? "" : "5 秒内没进到算子，后面的暂停断言无意义");
            Check("单次执行期间 IsContinuousRun=false", !session.IsContinuousRun,
                $"IsContinuousRun={session.IsContinuousRun}");

            engine.PauseSession(session);
            Check("【核心】单次执行期间 PauseSession 不生效（State 仍是 Running，不是 Paused）",
                session.State == SessionState.Running, $"State={session.State}");
            Check("单次执行的暂停请求被显式拒绝并留痕（不静默吞掉）",
                log.Warns.Any(w => w.Contains("单次执行不支持暂停")),
                string.Join(" | ", log.Warns));

            GatePlugin.Proceed.Set();
            Check("单次执行正常收尾", EndedCleanly(single), single.IsFaulted ? "任务 Faulted" : "");

            // ---- ③b 连续执行：暂停照常生效（防止语义澄清改过头） ----
            GatePlugin.Reset();
            var again = engine.RunSessionAsync(session);
            bool inContinuous = GatePlugin.Reached.Wait(TimeSpan.FromSeconds(5));
            Check("连续执行已进入算子", inContinuous,
                inContinuous ? "" : "5 秒内没进到算子，后面的暂停断言无意义");
            Check("连续执行期间 IsContinuousRun=true", session.IsContinuousRun,
                $"IsContinuousRun={session.IsContinuousRun}");

            engine.PauseSession(session);
            Check("【核心】连续执行期间 PauseSession 照常生效（State=Paused）",
                session.State == SessionState.Paused, $"State={session.State}");

            engine.ResumeSession(session);
            Check("恢复后回到 Running", session.State == SessionState.Running, $"State={session.State}");

            GatePlugin.Proceed.Set();
            engine.StopSession(session);
            Check("连续执行正常收尾", EndedCleanly(again), again.IsFaulted ? "任务 Faulted" : "");
        }

        // ==================================================================
        //  [E12] 契约与步骤收集（E 组）
        //
        //  两条命题：
        //   ① #8 插件分类注册口绝不能抛 —— 它一旦抛，PluginService 的按组名分派会让
        //      整个外部 DLL 加载失败，而错误文案只有"加载插件失败 {dllPath}"，看不出是组名问题。
        //   ② #9 Blueprints 必须递归填充 —— 只填顶层，嵌套步骤就永远不上报运行状态。
        //
        //  #13（删掉恒为 null 的 PortBindingService）由"删除 + 编译通过"保证，无需运行期断言。
        // ==================================================================
        internal static void ContractHygieneAndStepCollection()
        {
            Section("[E12] E 组 契约与步骤收集");

            // ---- ① #8 分类注册口不再抛，且注册结果可见 ----
            var notifier = new StubNotifier();
            var provider = new PluginProvider(notifier);
            var camera = new ToolItemModel
            {
                ModuleTypeName = "Stub.Camera",
                Name = "桩相机",
                Category = "相机",
            };

            Exception regBoom = null;
            try { provider.RegisterCamera(camera); }
            catch (Exception ex) { regBoom = ex; }
            Check("RegisterCamera 不再抛 NotImplementedException（旧实现必抛）",
                regBoom == null, regBoom == null ? "" : regBoom.GetType().Name + ": " + regBoom.Message);
            Check("相机插件转交模块注册表后可见（否则等于'加载成功却拖不出来'）",
                provider.ModulePlugins.ContainsKey("Stub.Camera"),
                $"模块表 {provider.ModulePlugins.Count} 项，相机分类表 {provider.CameraPlugins.Count} 项");
            Check("GetCamera 与注册同表查找，不再'注册进去却查不到'",
                provider.GetCamera("Stub.Camera") != null, "返回 null 说明两个口查了不同的表");

            Exception getBoom = null;
            try { provider.GetLaser("查不到的名字"); }
            catch (Exception ex) { getBoom = ex; }
            Check("GetLaser 查不到时返回 null 而不是抛（旧实现必抛）",
                getBoom == null, getBoom == null ? "" : getBoom.GetType().Name);

            // ---- ② #9 Blueprints 必须递归填充 ----
            // 结构：For → 循环体 → If → If 分支 → 计数桩（三层）
            var leaf = new ActionStep("A", "计数", TypeOf(new CountingPlugin()), "最内层计数");
            var innerIf = new ConditionStep("I", "条件", "SomeIfOperator", "内层If");
            innerIf.Children[0].Expression = "1 == 1";
            Put(innerIf.Children[0], leaf);

            var outerFor = new ForStep("F", "计次循环", "BuiltIn_For", "外层For");
            outerFor.DefaultLoopCount = 1;
            Put(outerFor.Children[0], innerIf);

            var deep = new FlowSession { FlowName = "蓝图深填断言" };
            deep.AddBlueprintsDeep(new StepModel[] { outerFor });
            Check("顶层步骤进 Blueprints", deep.Blueprints.Contains(outerFor), "");
            Check("循环体内的一层步骤也进 Blueprints（浅填会漏掉）",
                deep.Blueprints.Contains(innerIf), $"共 {deep.Blueprints.Count} 项");
            Check("分支里的第二层步骤也进 Blueprints（递归到底）",
                deep.Blueprints.Contains(leaf), $"共 {deep.Blueprints.Count} 项");
            Check("递归收集恰好 3 个（For / 内层If / 最内层计数）",
                deep.Blueprints.Count == 3, $"实际 {deep.Blueprints.Count}");

            // 反证：这正是 #9 的病灶 —— PluginTestRunner 原来就是这么浅填的
            var shallow = new FlowSession { FlowName = "蓝图浅填对照" };
            foreach (var s in new StepModel[] { outerFor })
                shallow.Blueprints.Add(s);
            Check("对照：浅填只拿到顶层 1 个（#9 病灶本身）",
                shallow.Blueprints.Count == 1, $"实际 {shallow.Blueprints.Count}");
        }
    }
}
