using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
using Newtonsoft.Json;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
// ParallelExecutionMode 与 System.Linq.ParallelExecutionMode 撞名（本文件 using System.Linq），别名固定
using ParallelExecutionMode = VisionMaster.Models.ParallelExecutionMode;
// 本文件 using 了 System.Threading，裸写的 ExecutionContext 会和 System.Threading.ExecutionContext 撞名
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    // ==================================================================
    //  桩插件（纪律照 ExecutionHarness 顶部注释：真类型 + AssemblyQualifiedName、
    //  端口 IsRequired=false——本文件必须可用；端口值走 StepModel.SetInputValue →
    //  VisionPluginBase.ApplyConfigValues 按端口名灌入，所以 tag/变量名都是**每实例**的，
    //  不用 static 字段——同一张图里多实例共用 static 会互相覆盖（P14 的共享静态坑））
    // ==================================================================

    /// <summary>
    /// 暴力测试的记录用叶子桩（B0-B4 的统一观测点）：
    /// · Tag 端口：每实例一个 trace 标记（StepModel.SetInputValue("Tag", …) 灌入）。
    /// · VarMode=Set/Dec + VarName/Value：写 context.LocalVariables[VarName]。
    ///   B0 随机图的 While 终止性就靠它：条件只读 StressVar、循环体每圈至少一笔 Dec，
    ///   单调递减 + 条件 "StressVar &gt; 0" ⇒ 必有界（生成器的不变量，不是运气）。
    /// · In/Out 端口：B4 的取数连线用例需要一个"能接线"的两端口形（IsRequired=false，
    ///   不接线也不触发 [参数缺失]）。
    /// · Trace 是**共享** List：并行模式（Parallel）下多分支线程并发追写 → lock 串行化，
    ///   读一律走 Snapshot（不在锁外枚举活集合）。
    /// </summary>
    [ParallelSafe]
    internal sealed class TracePlugin : VisionPluginBase
    {
        /// <summary>全图共享的 trace（顺序在并行模式下不确定——比较口径见文件头）</summary>
        public static readonly List<string> Trace = new();

        /// <summary>并发窗口（可选）：&gt;0 时到达即自旋等待，放大交错窗口；默认 0（不拖慢常规 gate）</summary>
        public static int DelayMs = 0;

        public InputPort<string> Tag { get; } = new("Tag") { IsRequired = false };
        public InputPort<string> VarName { get; } = new("VarName") { IsRequired = false };
        public InputPort<string> VarMode { get; } = new("VarMode") { IsRequired = false };
        public InputPort<double> Value { get; } = new("Value") { IsRequired = false };
        /// <summary>数据消费口（B4 取数用例）</summary>
        public InputPort<double> In { get; } = new("In") { IsRequired = false };
        /// <summary>数据生产口（B4 取数用例的产出方）</summary>
        public OutputPort<double> Out { get; } = new("Out");

        public static void Reset() { lock (Trace) Trace.Clear(); }

        public static void Add(string tag) { lock (Trace) Trace.Add(tag); }

        public static List<string> Snapshot() { lock (Trace) return Trace.ToList(); }

        public override void RunAlgorithm(IExecutionContext context)
        {
            if (DelayMs > 0)
            {
                // 协作取消：令牌取消时提前退出，不拖 join
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < DelayMs)
                {
                    if (context.CancellationToken.IsCancellationRequested) break;
                    Thread.Yield();
                }
            }

            lock (Trace) Trace.Add(Tag.ActualValue ?? "");
            Out.Value = 1d;

            string varName = VarName.ActualValue;
            if (string.IsNullOrEmpty(varName)) return;

            switch (VarMode.ActualValue)
            {
                case "Set":
                    context.LocalVariables[varName] = Value.ActualValue;
                    break;
                case "Dec":
                    double cur = context.LocalVariables.TryGetValue(varName, out var v) && v != null
                        ? Convert.ToDouble(v)
                        : 0d;
                    context.LocalVariables[varName] = cur - 1;
                    break;
            }
        }
    }

    /// <summary>
    /// For.Index 回读桩（B2）：把"For 节点输出的 Index + 自己的 Tag"记进共享 trace，
    /// 用来断言"Index 输出在循环体内可读、0 基递减序列"。
    /// In 端口与 For.IndexPort 同型（int），链接走 StepPort("Index")。
    /// </summary>
    [ParallelSafe]
    internal sealed class IndexEchoPlugin : VisionPluginBase
    {
        public InputPort<string> Tag { get; } = new("Tag") { IsRequired = false };
        public InputPort<int> In { get; } = new("In") { IsRequired = false };

        public override void RunAlgorithm(IExecutionContext context)
            => TracePlugin.Add($"{Tag.ActualValue}#{In.ActualValue}");
    }

    /// <summary>
    /// 流程引擎逻辑功能暴力测试（判断 / 循环 / 并行 / 分支四类逻辑）。
    ///
    /// 手段：真图纸 → 真编译（FlowCompiler）→ 真引擎（CompiledFlow.Run 直调，装配走 ExecHarness）。
    ///
    /// ================= ★ 比较口径（后人别误读）★ =================
    /// · 确定性图（全顺序 / Parallel 节点用 Sequential 模式）：trace **逐元素全等**。
    /// · 并行图（Parallel 模式）：Trace 是共享 List，写入顺序不确定 → 比**排序后的多重集**；
    ///   "分支内部保序"用"同分支 Tag 前缀"分组后比**子序列**（每个分支的 RunSequence
    ///   是顺序的，分支内部永远保序；跨分支才无序）。
    /// · 失败 / 取消的图**不做**全 trace 对拍（时序本质不确定），只断"集合关系 + 终态 + 日志关键文案"。
    /// · B0 随机图里 Parallel 节点一律 **Sequential 模式**：参考模型把分支按声明序"平铺"
    ///   进外层序列 —— 这正是引擎 RunSequentialFlattened 的实现（含 Break/Continue 归属
    ///   外层求解器的一期语义），所以逐元素全等成立。Parallel 模式的等价关系由 B3 单独钉。
    ///
    /// ================= ★ 参考模型的语义来源（不许凭猜）★ =================
    /// · If：CompiledIfNode.RunAndGetNext —— 按分支序求值，首个为真的分支命中；
    ///   兜底分支（Else/Default）编译成恒真；全不命中 ⇒ 判断成功但无事可做。
    /// · For：CompiledForNode.RunLoop —— 计次；Break/Continue/Return 在本层消化
    ///   （Continue 进下一圈、Break 跳出整层、Return 交上层终结）。
    /// · While：CompiledWhileNode.RunLoop —— **先判后跑**；条件求值异常上抛（不静默）。
    /// · 序列：CompiledNode.RunSequence —— Break/Continue 在循环体（yield:true）交回上层，
    ///   顶层（yield:false）清状态 + Warn「无循环归属」+ 继续；Return 与层级无关、无条件终止。
    /// · Parallel：CompiledParallelNode（Sequential=平铺 / Parallel=扇出+同步汇合+裁决）
    ///   + docs\方案设计\parallel-execution-phase2-design.md（v2 定稿）。
    /// · 编译期门禁（哪些图**应当编译失败**也是对拍的一部分）：FlowCompiler
    ///   —— 空条件（If 非兜底分支 / While 循环体）→ 硬错误；无分支的并行组 → 硬错误。
    ///
    /// ================= ★ 看门狗 ★ =================
    /// 每个 case 跑在 Task.Run + Wait(6s)；超时用**协作取消**打断（Cancel 掉上下文令牌，
    /// While/For/RunSequence 每个迭代/节点都会检查），记 finding 且不阻塞后续 case。
    /// While 无上限保护写法（写错条件就是死循环）由这条通道兜住，而不是让套件挂死。
    /// 发现问题的处理纪律：参考模型与引擎不符时**先判性质**——参考模型写错改参考模型
    /// （detail 里写依据）；引擎与设计文档不符 → 报 finding + 图 JSON，**不改引擎**；
    /// 两者都对但口径未定义 → 记"口径未定义"并如实报告。
    /// ==================================================================
    /// </summary>
    internal static class LogicBruteforceChecks
    {
        // ==================================================================
        //  入口
        // ==================================================================
        private const int DefaultSeed = 20261010;
        private const int DefaultGraphCount = 120;

        /// <summary>StressVar 初始值：大于随机图里 While 的累计圈数上限（条件恒为 &gt;0，每圈至少一笔 Dec）</summary>
        private const double StressSeedValue = 40d;

        private const int WatchdogSeconds = 6;

        /// <summary>
        /// 常规 gate：B0 随机图对拍（默认 120 张）+ B1-B4 固定用例。
        /// 加力档（randomOnly=true）：只跑 B0 随机图，N 张（默认 2000），供"暴力跑一把"用。
        /// </summary>
        public static void Run(int bruteCount = 0, bool randomOnly = false)
        {
            if (randomOnly)
            {
                B0RandomGraphs(bruteCount <= 0 ? 2000 : bruteCount, DefaultSeed, quiet: true);
                return;
            }

            var sw = Stopwatch.StartNew();
            B0RandomGraphs(DefaultGraphCount, DefaultSeed, quiet: false);
            B1TruthTables();
            B2LoopSemantics();
            B3ParallelSemantics();
            B4BranchTopologyAndVersions();
            sw.Stop();
            Console.WriteLine($"  [计时] B0-B4 逻辑暴力测试总耗时 {sw.ElapsedMilliseconds}ms（默认档；加力档 = --brute N）");
        }

        // ==================================================================
        //  [B0] 参考解释器 × 随机图对拍
        // ==================================================================
        private static void B0RandomGraphs(int count, int seed, bool quiet)
        {
            Section($"[B0] 参考解释器 × 随机图对拍（{count} 张随机图，seed={seed}{(quiet ? "，加力档" : "")}）");

            int legalCompiled = 0, illegalRejected = 0, matched = 0, mismatched = 0, timedOut = 0;
            int traceVolume = 0;
            var census = new Dictionary<string, int>(StringComparer.Ordinal);
            var findings = new List<string>();
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < count; i++)
            {
                int graphSeed = seed + i * 7919;          // 每图定种子：图与结果都可复现
                var plan = GenGraph(graphSeed);
                var model = BuildGraph(plan);
                MergeCensus(census, NodeCensus(model));

                // ---- 编译判定对拍（"这张图该不该编译失败"也是语义的一部分）----
                var run = ExecHarness.Prepare(model);
                if (!plan.ExpectCompile)
                {
                    if (!run.Compiled && run.Errors.Contains(plan.ExpectError))
                    {
                        illegalRejected++;
                        matched++;
                        continue;
                    }

                    mismatched++;
                    string d0 = $"期望编译失败（含『{plan.ExpectError}』），实际 {(run.Compiled ? "编译成功" : "失败但文案不含期望片段")}；" +
                                $"errors={run.Errors}；plan={PlanJson(plan)}；blueprint={BlueprintJson(model)}";
                    findings.Add(d0);
                    if (!quiet) Check($"[B0#{i} seed={graphSeed}] 非法图按生成器口径被拒", false, d0);
                    continue;
                }

                if (!run.Compiled)
                {
                    mismatched++;
                    string d1 = $"期望编译成功，实际失败：{run.Errors}；plan={PlanJson(plan)}；blueprint={BlueprintJson(model)}";
                    findings.Add(d1);
                    if (!quiet) Check($"[B0#{i} seed={graphSeed}] 合法图编译成功", false, d1);
                    continue;
                }

                legalCompiled++;

                // ---- 期望 trace（参考解释器，读的是与引擎同一批 StepModel）----
                var reference = new RefState();
                reference.Vars["StressVar"] = StressSeedValue;
                RunRefList(model, reference, topLevel: true);

                // ---- 真引擎 ----
                var obs = new ExecObservation();
                RunPrepared(run, obs, StressSeedValue, WatchdogSeconds);

                string mismatch = null;
                if (obs.TimedOut) { mismatch = "超时：可能死循环/死锁"; timedOut++; }
                else if (obs.Exception != null) mismatch = $"异常上抛：{obs.Exception.GetType().Name}: {obs.Exception.Message}";
                else if (!obs.Trace.SequenceEqual(reference.Trace)) mismatch = "trace 不一致：" + TraceDiff(obs.Trace, reference.Trace);
                else if (Math.Abs(obs.FinalStress - reference.Vars["StressVar"]) > 1e-9)
                    mismatch = $"StressVar 终值不一致：引擎 {obs.FinalStress} / 参考 {reference.Vars["StressVar"]}";
                else
                {
                    // 终态交叉检查：参考模型判定"跑过"的叶子必须落 Success（引擎状态上报的自洽性）
                    var executed = new HashSet<string>(reference.Trace, StringComparer.Ordinal);
                    var bad = WalkLeaves(model)
                        .Where(l => executed.Contains(TagOf(l)) && l.State != StepState.Success)
                        .Select(l => $"{TagOf(l)}={l.State}")
                        .ToList();
                    if (bad.Count > 0) mismatch = "跑过的叶子未落 Success：" + string.Join(",", bad);
                }

                if (mismatch == null) { matched++; traceVolume += reference.Trace.Count; continue; }
                mismatched++;
                string d2 = $"{mismatch}；plan={PlanJson(plan)}；blueprint={BlueprintJson(model)}";
                findings.Add(d2);
                if (!quiet) Check($"[B0#{i} seed={graphSeed}] 图与参考模型一致", false, d2);
            }

            sw.Stop();
            string censusText = string.Join("/", census.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
            string summary =
                $"合法编译 {legalCompiled} 图 / 预期非法被拒 {illegalRejected} 图；一致 {matched} / 不一致 {mismatched}（含超时 {timedOut}）；" +
                $"对拍 trace 总量 {traceVolume} 项；节点普查 {censusText}；耗时 {sw.ElapsedMilliseconds}ms";
            if (quiet)
                Console.WriteLine($"[brute] 随机图 {count} 张：通过 {matched} / 失败 {mismatched} / 超时 {timedOut}");
            Console.WriteLine($"  [census] {summary}");
            Check($"B0：{count} 张随机图与参考模型一致（编译判定 + trace 逐元素 + 终值 + 终态）", mismatched == 0, summary);

            // 加力档只留下前 5 例 finding 图 JSON（复现入口）
            for (int i = 0; i < Math.Min(5, findings.Count); i++)
                Console.WriteLine($"  [finding {i + 1}] {findings[i]}");
        }

        // ==================================================================
        //  [B1] 判断（真值表穷举）
        // ==================================================================
        private static void B1TruthTables()
        {
            Section("[B1] 判断（If/ElseIf/Else 真值表穷举 + 条件形态 + 编译门禁）");

            // ---- 两分支：条件真/假 ----
            ExpectTrace("B1：If 两分支条件=true 只走 If 分支",
                new StepModel[] { MakeIf("两分支", ("true", new StepModel[] { Trace("T") }), (null, new StepModel[] { Trace("F") })) },
                new[] { "T" });
            ExpectTrace("B1：If 两分支条件=false 只走 Else 分支",
                new StepModel[] { MakeIf("两分支", ("false", new StepModel[] { Trace("T") }), (null, new StepModel[] { Trace("F") })) },
                new[] { "F" });

            // ---- 三链穷举：TT / TF / FT / FF ----
            ExpectTrace("B1：三链 (T,T) → 只走第一条真分支",
                new StepModel[] { MakeIf("三链", ("true", new StepModel[] { Trace("A") }), ("true", new StepModel[] { Trace("B") }), (null, new StepModel[] { Trace("C") })) },
                new[] { "A" });
            ExpectTrace("B1：三链 (T,F) → 只走第一条真分支",
                new StepModel[] { MakeIf("三链", ("true", new StepModel[] { Trace("A") }), ("false", new StepModel[] { Trace("B") }), (null, new StepModel[] { Trace("C") })) },
                new[] { "A" });
            ExpectTrace("B1：三链 (F,T) → 走 ElseIf",
                new StepModel[] { MakeIf("三链", ("false", new StepModel[] { Trace("A") }), ("true", new StepModel[] { Trace("B") }), (null, new StepModel[] { Trace("C") })) },
                new[] { "B" });
            ExpectTrace("B1：三链 (F,F) → 全假走 Else 兜底",
                new StepModel[] { MakeIf("三链", ("false", new StepModel[] { Trace("A") }), ("false", new StepModel[] { Trace("B") }), (null, new StepModel[] { Trace("C") })) },
                new[] { "C" });

            // ---- 无兜底（If/ElseIf 全假）→ 判断成功但无事可做 ----
            ExpectTrace("B1：无兜底 If/ElseIf 全假 → 不执行任何分支（判断本身仍是成功）",
                new StepModel[] { MakeIf("无兜底", ("false", new StepModel[] { Trace("A") }), ("false", new StepModel[] { Trace("B") })), Trace("尾") },
                new[] { "尾" });

            // ---- 条件表达式形态：常量 / 变量比较 / 算术 ----
            ExpectTrace("B1：常量 true → 走 If", IfWithCond("常量真", "true", "T", "F"), new[] { "T" });
            ExpectTrace("B1：常量 false → 走 Else", IfWithCond("常量假", "false", "T", "F"), new[] { "F" });
            ExpectTrace("B1：变量比较 StressVar > 2（seed=5）→ 真",
                IfWithCond("变量比较", "StressVar > 2", "T", "F"), new[] { "T" }, stressSeed: 5);
            ExpectTrace("B1：变量比较 StressVar > 2（seed=1）→ 假",
                IfWithCond("变量比较", "StressVar > 2", "T", "F"), new[] { "F" }, stressSeed: 1);
            ExpectTrace("B1：算术 (StressVar + 1) % 2 == 0（seed=1 → 2%2==0）→ 真",
                IfWithCond("算术式", "(StressVar + 1) % 2 == 0", "T", "F"), new[] { "T" }, stressSeed: 1);
            ExpectTrace("B1：算术 (StressVar + 1) % 2 == 0（seed=2 → 3%2==1）→ 假",
                IfWithCond("算术式", "(StressVar + 1) % 2 == 0", "T", "F"), new[] { "F" }, stressSeed: 2);

            // ---- 嵌套两层：内层 false 不影响外层后续步骤（内层两种形态各钉一条）----
            var innerNoFallback = MakeIf("内层无兜底", ("false", new StepModel[] { Trace("X") }), ("false", new StepModel[] { Trace("Y") }));
            var outerNoFallback = MakeIf("外层", ("true", new StepModel[] { Trace("A"), innerNoFallback, Trace("B") }), (null, new StepModel[0]));
            ExpectTrace("B1：嵌套两层 If（内层无兜底且 false → 内层不跑，外层后续步骤照跑）",
                new StepModel[] { outerNoFallback }, new[] { "A", "B" });

            var innerWithElse = MakeIf("内层有兜底", ("false", new StepModel[] { Trace("X") }), (null, new StepModel[] { Trace("Y") }));
            var outerWithElse = MakeIf("外层", ("true", new StepModel[] { Trace("A"), innerWithElse, Trace("B") }), (null, new StepModel[0]));
            ExpectTrace("B1：嵌套两层 If（内层 false → 走内层 Else 兜底，外层后续步骤照跑）",
                new StepModel[] { outerWithElse }, new[] { "A", "Y", "B" });

            // ---- 编译门禁：空条件 ----
            {
                var badIf = MakeIf("缺条件If", ("", new StepModel[] { Trace("A") }), (null, new StepModel[0]));
                var runIf = ExecHarness.Prepare(new StepModel[] { badIf });
                Check("B1：If 分支条件为空 → 编译失败且文案指向具体节点",
                    !runIf.Compiled && runIf.Errors.Contains("表达式为空") && runIf.Errors.Contains("缺条件If"),
                    runIf.Compiled ? "编译反而成功了" : runIf.Errors);

                var badElseIf = MakeIf("缺条件ElseIf", ("true", new StepModel[] { Trace("A") }), ("", new StepModel[] { Trace("B") }), (null, new StepModel[0]));
                var runElseIf = ExecHarness.Prepare(new StepModel[] { badElseIf });
                Check("B1：ElseIf 分支条件为空 → 同样是硬错误",
                    !runElseIf.Compiled && runElseIf.Errors.Contains("表达式为空"),
                    runElseIf.Compiled ? "编译反而成功了" : runElseIf.Errors);

                var badWhile = MakeWhile("缺条件While", "", DecStress("D"));
                var runWhile = ExecHarness.Prepare(new StepModel[] { badWhile });
                Check("B1：While 条件为空 → 编译失败（循环条件表达式为空）且指向节点",
                    !runWhile.Compiled && runWhile.Errors.Contains("循环条件表达式为空") && runWhile.Errors.Contains("缺条件While"),
                    runWhile.Compiled ? "编译反而成功了" : runWhile.Errors);

                var okElse = MakeIf("Else无条件", ("true", new StepModel[] { Trace("T") }), (null, new StepModel[] { Trace("F") }));
                var runElse = ExecHarness.Prepare(new StepModel[] { okElse });
                Check("B1：Else 不写条件 → 编译通过（RequiresExpression=false）", runElse.Compiled, runElse.Errors);
            }
        }

        /// <summary>单条件 If/Else 形状（B1 条件形态穷举用）</summary>
        private static StepModel[] IfWithCond(string name, string cond, string thenTag, string elseTag)
            => new StepModel[] { MakeIf(name, (cond, new StepModel[] { Trace(thenTag) }), (null, new StepModel[] { Trace(elseTag) })) };

        // ==================================================================
        //  [B2] 循环
        // ==================================================================
        private static void B2LoopSemantics()
        {
            Section("[B2] 循环（For/While/Break/Continue/Return/嵌套/并行×循环）");

            // ---- For 次数：1 / 3 / 0（负与超上限另有既有断言覆盖，这里不复述）----
            ExpectTrace("B2：For(1) 恰好 1 圈", new StepModel[] { MakeFor("F1", 1, Trace("A")) }, new[] { "A" });
            ExpectTrace("B2：For(3) 恰好 3 圈", new StepModel[] { MakeFor("F3", 3, Trace("A")) }, new[] { "A", "A", "A" });
            ExpectTrace("B2：For(0) 一圈不跑，循环后步骤照跑（且只执行一次）",
                new StepModel[] { MakeFor("F0", 0, Trace("A")), Trace("B") }, new[] { "B" });
            ExpectTrace("B2：For 跑完后循环后节点只执行一次",
                new StepModel[] { MakeFor("F3", 3, Trace("A")), Trace("B") }, new[] { "A", "A", "A", "B" });

            // ---- LoopCount 连线覆盖 DefaultLoopCount（运行时变量来源）----
            {
                var f = MakeFor("FL", 3, Trace("I"));
                f.SetLink("LoopCount", new LinkReference(
                    LinkKind.RuntimeVariable, LinkProtocol.RuntimeVariableMarkerGuid, "LoopN", "Runtime.LoopN"));
                ExpectTrace("B2：LoopCount 连线（LoopN=2）覆盖 DefaultLoopCount=3 → 只跑 2 圈",
                    new StepModel[] { VarWrite("W", "LoopN", 2), f }, new[] { "W", "I", "I" });
            }

            // ---- For.Index 输出在循环体内可读（0 基）----
            {
                var f = MakeFor("FI", 3);
                var echo = IndexEcho("IDX");
                echo.SetLink("In", new LinkReference(LinkKind.StepPort, f.StepID, "Index", "For.Index"));
                f.Children[0].Steps.Add(echo);
                ExpectTrace("B2：For.Index 在循环体内可读且 0 基递进", new StepModel[] { f }, new[] { "IDX#0", "IDX#1", "IDX#2" });
            }

            // ---- While：先判后跑 / 正常退出 ----
            ExpectTrace("B2：While 初值就假 → 循环体 0 圈（先判后跑）",
                new StepModel[] { MakeWhile("W0", "StressVar > 0", DecStress("D"), Trace("A")), Trace("B") },
                new[] { "B" }, stressSeed: 0);
            ExpectTrace("B2：While 正常退出（初值 3 → 3 圈，每圈 Dec 一并验证终值 0）",
                new StepModel[] { MakeWhile("W3", "StressVar > 0", DecStress("D"), Trace("A")) },
                new[] { "D", "A", "D", "A", "D", "A" },
                stressSeed: 3, expectedFinalStress: 0);

            // ---- Break / Continue ----
            ExpectTrace("B2：内层 Break 只跳出内层（外层继续，组合①）",
                new StepModel[]
                {
                    MakeFor("OB", 2,
                        Trace("O1"),
                        MakeFor("IB", 3, Trace("I1"), Brk("内层Break"), Trace("I2")),
                        Trace("O2")),
                },
                new[] { "O1", "I1", "O2", "O1", "I1", "O2" });

            ExpectTrace("B2：Continue 只跳过本圈剩余步骤（下一圈照常）",
                new StepModel[] { MakeFor("FC", 2, Trace("C1"), Cnt("Continue"), Trace("C2")) },
                new[] { "C1", "C1" });

            ExpectTrace("B2：顶层 Break 被忽略（Warn『无循环归属』+ 后续步骤照跑）",
                new StepModel[] { Trace("A"), Brk("顶层Break"), Trace("B") },
                new[] { "A", "B" }, requireWarn: "无循环归属");

            ExpectTrace("B2：顶层 Continue 同样被忽略 + Warn",
                new StepModel[] { Trace("A"), Cnt("顶层Continue"), Trace("B") },
                new[] { "A", "B" }, requireWarn: "无循环归属");

            // ---- Return：在循环体内 → 整个流程终止 ----
            {
                var obs = Observe(new StepModel[] { MakeFor("FR", 3, Trace("A"), Ret("返回"), Trace("B")), Trace("C") }, StressSeedValue);
                bool ok = obs.Compiled && !obs.TimedOut && obs.Exception == null
                          && obs.Trace.SequenceEqual(new[] { "A" })
                          && obs.Context!.CurrentFlowState == FlowControlState.Return;
                Check("B2：Return 在循环体内 → 整个流程终止（trace 到此为止 + 流程态 Return）", ok,
                    ok ? "" : DetailOf(obs, new[] { "A" }, null) + $" flowState={obs.Context?.CurrentFlowState}");
            }

            // ---- 嵌套 2 层：2×3 = 6 圈体 + Break 组合穷举 ----
            ExpectTrace("B2：嵌套 2 层 2×3 = 6 圈体",
                new StepModel[] { MakeFor("O6", 2, MakeFor("I6", 3, Trace("I"))) },
                new[] { "I", "I", "I", "I", "I", "I" });

            ExpectTrace("B2：外层 Break → 内层跑满 3 圈后整层结束（组合②）",
                new StepModel[] { MakeFor("OB2", 2, MakeFor("IB2", 3, Trace("I")), Brk("外层Break"), Trace("X")) },
                new[] { "I", "I", "I" });

            ExpectTrace("B2：内外都 Break → 内层 1 圈 + 外层立即结束（组合③）",
                new StepModel[] { MakeFor("OB3", 2, MakeFor("IB3", 3, Trace("I"), Brk("内层Break")), Brk("外层Break"), Trace("X")) },
                new[] { "I" });

            // ---- 并行 × 循环（真并发模式：比多重集 + 分支内部保序）----
            {
                var loopWithParallel = MakeFor("循环套并行", 3,
                    MakeParallel("体内并行", ParallelExecutionMode.Parallel,
                        new StepModel[] { Trace("PB1") },
                        new StepModel[] { Trace("PB2") }));
                var obs = Observe(new StepModel[] { loopWithParallel }, StressSeedValue);
                var expected = new[] { "PB1", "PB2", "PB1", "PB2", "PB1", "PB2" };
                bool ok = obs.Compiled && !obs.TimedOut && obs.Exception == null
                          && MultisetEquals(obs.Trace, expected)
                          && loopWithParallel.State == StepState.Success;
                Check("B2：For 体内并行组（Parallel）→ 多重集 = 每圈两分支各一次；容器 Success", ok,
                    ok ? "" : DetailOf(obs, expected, null) + $" For.State={loopWithParallel.State}");
            }

            {
                var parWithLoops = MakeParallel("并行套循环", ParallelExecutionMode.Parallel,
                    new StepModel[] { MakeFor("FA", 2, Trace("a1"), Trace("a2")) },
                    new StepModel[] { MakeFor("FB", 2, Trace("b1"), Trace("b2")) });
                var obs = Observe(new StepModel[] { parWithLoops }, StressSeedValue);
                var aOrder = BranchOrder(obs.Trace, "a");
                var bOrder = BranchOrder(obs.Trace, "b");
                bool ok = obs.Compiled && !obs.TimedOut && obs.Exception == null
                          && aOrder.SequenceEqual(new[] { "a1", "a2", "a1", "a2" })
                          && bOrder.SequenceEqual(new[] { "b1", "b2", "b1", "b2" })
                          && MultisetEquals(obs.Trace, new[] { "a1", "a2", "a1", "a2", "b1", "b2", "b1", "b2" });
                Check("B2：并行分支体内放 For → 每分支内部顺序保序（a1→a2→a1→a2 / b1→b2→b1→b2）", ok,
                    ok ? "" : DetailOf(obs, new[] { "a1", "a2", "a1", "a2", "b1", "b2", "b1", "b2" }, null)
                              + $" aOrder=[{string.Join(",", aOrder)}] bOrder=[{string.Join(",", bOrder)}]");
            }
            // ---- 看门狗自检：三层 9999 计次循环（≈10^12 圈）必然跑不完 → 必须在 2s 被打断记为超时 ----
            // 安全网自己也要有守卫：看门狗坏掉时，随机图一旦生成长循环就会挂死整个套件（而不是报 finding）。
            {
                var heavy = MakeFor("慢外", 9999, MakeFor("慢中", 9999, MakeFor("慢内", 9999)));
                var obs = Observe(new StepModel[] { heavy }, StressSeedValue, timeoutSeconds: 2);
                Check("B2：看门狗自检（三层 9999 计次循环 → 2s 内被协作取消打断并如实记超时）",
                    obs.TimedOut, $"timedOut={obs.TimedOut} ex={obs.Exception?.Message}（期望 timedOut=True）");
            }
        }

        // ==================================================================
        //  [B3] 并行（同一图式跨模式等价 + 分支数 + 并发压力 + 失败聚合边界）
        // ==================================================================
        private static void B3ParallelSemantics()
        {
            Section("[B3] 并行（模式等价 / 分支数 / 并发压力 / 失败聚合边界）");

            // ---- 模式等价：同一张图（两分支各 2 叶子 + 分支内嵌 If）分别以两种模式跑 ----
            StepModel[] BuildShape(ParallelExecutionMode mode) => new StepModel[]
            {
                MakeParallel("模式等价", mode,
                    new StepModel[]
                    {
                        Trace("A1"),
                        MakeIf("分支内If", ("true", new StepModel[] { Trace("A2") }), (null, new StepModel[] { Trace("A3") })),
                    },
                    new StepModel[] { Trace("B1"), Trace("B2") }),
            };

            var seqModel = BuildShape(ParallelExecutionMode.Sequential);
            var parModel = BuildShape(ParallelExecutionMode.Parallel);
            var obsSeq = Observe(seqModel, StressSeedValue);
            var obsPar = Observe(parModel, StressSeedValue);

            Check("B3：Sequential 模式 trace 逐元素 = 声明序（A1→A2→B1→B2）",
                obsSeq.Compiled && obsSeq.Trace.SequenceEqual(new[] { "A1", "A2", "B1", "B2" }),
                DetailOf(obsSeq, new[] { "A1", "A2", "B1", "B2" }, null));
            Check("B3：Parallel 模式 trace 多重集 = Sequential（并行不改变语义的核心不等式）",
                obsPar.Compiled && MultisetEquals(obsPar.Trace, obsSeq.Trace),
                DetailOf(obsPar, obsSeq.Trace.ToArray(), null));
            Check("B3：Parallel 模式下分支 1 内部保序（A1→A2）",
                BranchOrder(obsPar.Trace, "A").SequenceEqual(new[] { "A1", "A2" }),
                $"[{string.Join(",", obsPar.Trace)}]");
            Check("B3：Parallel 模式下分支 2 内部保序（B1→B2）",
                BranchOrder(obsPar.Trace, "B").SequenceEqual(new[] { "B1", "B2" }),
                $"[{string.Join(",", obsPar.Trace)}]");
            Check("B3：两模式最终步骤状态集相同",
                StateMap(seqModel).SequenceEqual(StateMap(parModel)),
                $"seq=[{string.Join(",", StateMap(seqModel).Select(kv => kv.Key + ":" + kv.Value))}] " +
                $"par=[{string.Join(",", StateMap(parModel).Select(kv => kv.Key + ":" + kv.Value))}]");
            Check("B3：两模式容器终态均为 Success",
                ((ParallelStep)seqModel[0]).State == StepState.Success && ((ParallelStep)parModel[0]).State == StepState.Success,
                $"seq={((ParallelStep)seqModel[0]).State} par={((ParallelStep)parModel[0]).State}");

            // ---- 分支数 2 / 4 / 8（8 = 建议上限）----
            foreach (int n in new[] { 2, 4, 8 })
            {
                var branches = Enumerable.Range(0, n)
                    .Select(b => new StepModel[] { Trace($"b{b + 1}.1"), Trace($"b{b + 1}.2") })
                    .ToArray();
                var graph = new StepModel[] { MakeParallel($"宽{n}", ParallelExecutionMode.Parallel, branches) };
                var obs = Observe(graph, StressSeedValue);
                var expected = branches.SelectMany(b => b.Select(s => TagOf((ActionStep)s))).ToArray();
                bool ok = obs.Compiled && !obs.TimedOut && obs.Exception == null
                          && MultisetEquals(obs.Trace, expected)
                          && ((ParallelStep)graph[0]).State == StepState.Success;
                Check($"B3：{n} 分支全部执行（多重集一致）且容器 Success", ok,
                    ok ? "" : DetailOf(obs, expected, null) + $" State={((ParallelStep)graph[0]).State}");
            }

            // ---- 空分支：不报错、不影响其它分支 ----
            {
                var graph = new StepModel[]
                {
                    MakeParallel("空分支组", ParallelExecutionMode.Parallel,
                        new StepModel[0],
                        new StepModel[] { Trace("B") }),
                };
                var obs = Observe(graph, StressSeedValue);
                bool ok = obs.Compiled && !obs.TimedOut && obs.Exception == null
                          && obs.Trace.SequenceEqual(new[] { "B" })
                          && ((ParallelStep)graph[0]).State == StepState.Success;
                Check("B3：空分支（无步骤）不报错、不影响其它分支，容器 Success", ok,
                    ok ? "" : DetailOf(obs, new[] { "B" }, null) + $" State={((ParallelStep)graph[0]).State}");
            }

            // ---- 单分支 Parallel 模式：与 Sequential 一致（P30 已钉，这里不打脸）----
            {
                var graph = new StepModel[]
                {
                    MakeParallel("单分支", ParallelExecutionMode.Parallel, new StepModel[] { Trace("S1"), Trace("S2") }),
                };
                var obs = Observe(graph, StressSeedValue);
                Check("B3：单分支 Parallel 模式 → 平铺退化（S1→S2）且容器 Success",
                    obs.Compiled && obs.Trace.SequenceEqual(new[] { "S1", "S2" }) && ((ParallelStep)graph[0]).State == StepState.Success,
                    DetailOf(obs, new[] { "S1", "S2" }, null));
            }

            // ---- 并发压力：4 分支 × 每分支 3 叶子（含嵌套 If + 每分支写不同名变量），连跑 50 轮 ----
            {
                var graph = new StepModel[]
                {
                    MakeParallel("压力组", ParallelExecutionMode.Parallel,
                        new StepModel[] { Trace("p1a"), VarWrite("p1w", "PV1", 11), Trace("p1c") },
                        new StepModel[]
                        {
                            MakeIf("压力If", ("true", new StepModel[] { VarWrite("p2w", "PV2", 22) }), (null, new StepModel[0])),
                            Trace("p2b"), Trace("p2c"),
                        },
                        new StepModel[] { Trace("p3a"), VarWrite("p3w", "PV3", 33), Trace("p3c") },
                        new StepModel[] { Trace("p4a"), Trace("p4b"), VarWrite("p4w", "PV4", 44) }),
                };
                var run = ExecHarness.Prepare(graph);
                Check("B3：压力图纸编译成功", run.Compiled, run.Errors);

                if (run.Compiled)
                {
                    var expected = new[]
                    {
                        "p1a", "p1w", "p1c", "p2w", "p2b", "p2c", "p3a", "p3w", "p3c", "p4a", "p4b", "p4w",
                    };
                    var expectedVars = new Dictionary<string, double> { ["PV1"] = 11, ["PV2"] = 22, ["PV3"] = 33, ["PV4"] = 44 };

                    int badRounds = 0, exRounds = 0, timeoutRounds = 0;
                    string firstBad = "";
                    TracePlugin.DelayMs = 1;   // 每个叶子 1ms 自旋：把四条分支的线程真叠在一起（默认 0 会太快、交错窗口窄）
                    try
                    {
                        for (int round = 0; round < 50; round++)
                        {
                            var obs = new ExecObservation();
                            RunPrepared(run, obs, StressSeedValue, WatchdogSeconds);

                            string why = null;
                            if (obs.TimedOut) { timeoutRounds++; why = "超时"; }
                            else if (obs.Exception != null) { exRounds++; why = "异常：" + obs.Exception.Message; }
                            else if (!MultisetEquals(obs.Trace, expected)) why = "多重集漂了：" + DetailOf(obs, expected, null);
                            else if (((ParallelStep)graph[0]).State != StepState.Success) why = "容器终态=" + ((ParallelStep)graph[0]).State;
                            else
                            {
                                foreach (var kv in expectedVars)
                                {
                                    if (!obs.Context!.LocalVariables.TryGetValue(kv.Key, out var v) || !Equals(v, kv.Value))
                                    {
                                        why = $"父域变量 {kv.Key}={v ?? "<无>"}（期望 {kv.Value}）";
                                        break;
                                    }
                                }
                                if (why == null && obs.Log.Warns.Any(w => w.Contains("同时写入了同名运行时变量")))
                                    why = "出现同名写冲突 Warn（各分支写的是不同名变量）";
                            }

                            if (why == null) continue;
                            badRounds++;
                            if (firstBad.Length == 0) firstBad = $"第 {round + 1} 轮：{why}";
                        }
                    }
                    finally
                    {
                        TracePlugin.DelayMs = 0;
                    }

                    Check("B3：并发压力 50 轮（4 分支 × 3 叶子）：多重集不变 / 无异常 / 容器 Success / 父域变量终值稳定",
                        badRounds == 0,
                        badRounds == 0 ? "" : $"{badRounds}/50 轮异常（含超时 {timeoutRounds} / 异常 {exRounds}）；首例 {firstBad}");
                }
            }

            // ---- 失败聚合边界：FailFast=On 时"分支内嵌套 If 的某条子分支业务失败"是否算分支业务失败 ----
            {
                SafeGatePlugin.Reset();
                var bizInIf = MakeIf("失败If", ("true", new StepModel[] { BizFailLeaf("If内业务失败") }), (null, new StepModel[0]));
                var par = MakeParallel("嵌套失败组", ParallelExecutionMode.Parallel,
                    new StepModel[] { bizInIf },
                    new StepModel[] { GateLeaf("兄弟闸门") });
                par.FailFastMode = FailFastMode.On;

                var run = ExecHarness.Prepare(new StepModel[] { par });
                Check("B3：失败聚合边界图纸编译成功", run.Compiled, run.Errors);

                if (run.Compiled)
                {
                    var log = new StubLog();
                    var cts = new CancellationTokenSource();
                    var ctx = new ExecutionContext(log, run.Session!, run.Workspace!, cts.Token);
                    Exception caught = null;
                    var bg = Task.Run(() =>
                    {
                        try { run.Engine!.Run(ctx); } catch (Exception ex) { caught = ex; }
                    });

                    // FailFast 生效 ⇒ 兄弟闸门被令牌唤醒提前返回（快速返回）；未生效 ⇒ 闸门等满 5s 兜底
                    bool fast = bg.Wait(TimeSpan.FromSeconds(2.5));
                    SafeGatePlugin.Proceed.Set();
                    bg.Wait(TimeSpan.FromSeconds(8));

                    Check("B3：嵌套 If 子分支的业务失败被识别为分支业务失败（FailFast 取消兄弟分支 → Run 快速返回）",
                        fast, fast ? "" : "Run 未在 2.5s 内返回：兄弟分支没有被取消（CompiledParallelNode.BranchHasFailedStep 的递归判定可能漏了嵌套分支）");
                    Check("B3：该路径无异常上抛（FailFast 不做异常升级）", caught == null, caught?.Message ?? "");
                    Check("B3：业务失败步骤如实落 Failed（未升级成会话 Faulted）",
                        bizInIf.Children[0].Steps.OfType<ActionStep>().First().State == StepState.Failed,
                        $"{bizInIf.Children[0].Steps.OfType<ActionStep>().First().State}");
                }
            }
        }

        // ==================================================================
        //  [B4] 分支/拓扑与版本链
        // ==================================================================
        private static void B4BranchTopologyAndVersions()
        {
            Section("[B4] 分支/拓扑（嵌套跨分支取数 / 版本链 / 越界 Break）");

            // ---- 合法：分支内引用外层步骤的输出（进入并行组前已有值）----
            {
                var producer = Trace("外层产出");
                var consumer = Trace("分支内消费");
                consumer.SetLink("In", new LinkReference(LinkKind.StepPort, producer.StepID, "Out", "外层产出.Out"));
                var par = MakeParallel("外层取数组", ParallelExecutionMode.Parallel,
                    new StepModel[] { consumer },
                    new StepModel[] { Trace("兄弟") });

                var obs = Observe(new StepModel[] { producer, par }, StressSeedValue);
                Check("B4：分支内引用外层步骤输出 → 合法（编译通过 + 两分支都跑）",
                    obs.Compiled && !obs.TimedOut && MultisetEquals(obs.Trace, new[] { "外层产出", "分支内消费", "兄弟" }),
                    obs.Compiled ? DetailOf(obs, new[] { "外层产出", "分支内消费", "兄弟" }, null) : obs.Errors);
            }

            // ---- 非法（嵌套组合）：并行分支 A 里的 If 子分支引用分支 B 内步骤 ----
            {
                var producerInB = Trace("B内产出");
                var consumerInIf = Trace("A内If子分支消费");
                consumerInIf.SetLink("In", new LinkReference(LinkKind.StepPort, producerInB.StepID, "Out", "B内产出.Out"));
                var ifInA = MakeIf("A内If", ("true", new StepModel[] { consumerInIf }), (null, new StepModel[0]));
                var par = MakeParallel("嵌套跨分支组", ParallelExecutionMode.Parallel,
                    new StepModel[] { ifInA },
                    new StepModel[] { producerInB });

                var run = ExecHarness.Prepare(new StepModel[] { par });
                Check("B4：并行分支 A 的 If 子分支引用分支 B 内步骤 → [跨分支取数] 编译失败",
                    !run.Compiled && run.Errors.Contains("跨分支取数"),
                    run.Compiled ? "编译反而通过了" : run.Errors);
            }

            // ---- 版本链：图改一处 → Version 变大（FlowModel 监听口径）----
            {
                var flow = new FlowModel { FlowName = "B4版本链" };
                var ifStep = MakeIf("版本If", ("true", new StepModel[] { Trace("V1") }), (null, new StepModel[] { Trace("V2") }));
                flow.Steps.Add(ifStep);

                int v0 = flow.Version;
                ifStep.Children[0].Steps.Add(Trace("V3"));
                Check("B4：分支内增步骤 → Version++（Steps 集合在监听面内）", flow.Version > v0, $"{v0} → {flow.Version}");

                int v1 = flow.Version;
                ifStep.Children.Add(new StepCollection { BranchType = BranchType.ElseIf, StepName = "新分支", Expression = "false" });
                Check("B4：容器新增分支 → Version++（Children 集合在监听面内）", flow.Version > v1, $"{v1} → {flow.Version}");

                int v2 = flow.Version;
                ifStep.Children[0].Steps.RemoveAt(0);
                Check("B4：分支内删步骤 → Version++", flow.Version > v2, $"{v2} → {flow.Version}");

                int v3 = flow.Version;
                ifStep.StepName = "改名If";
                Check("B4：容器改名（StepModel 语义属性）→ Version++", flow.Version > v3, $"{v3} → {flow.Version}");

                int v4 = flow.Version;
                ifStep.Children[0].StepName = "改名分支";
                Check("B4：分支名（StepCollection.StepName）不自动递增 —— R26 口径：它不在 FlowModel 监听面，由命令族手动 Version++",
                    flow.Version == v4, $"{v4} → {flow.Version}");
            }

            // ---- 随机图"改一处 → 版本变大"统一断言（同一批图形的结构化变更，各 5 张）----
            {
                int checkedCount = 0, bad = 0;
                for (int i = 0; i < 5; i++)
                {
                    var plan = GenGraph(DefaultSeed + i * 104729);
                    if (!plan.ExpectCompile) continue;
                    var model = BuildGraph(plan);
                    var container = model.FirstOrDefault(s => s is IContainerStep) as IContainerStep;
                    if (container == null || container.Children.Count == 0) continue;

                    var flow = new FlowModel { FlowName = $"B4随机图版本链{i}" };
                    foreach (var s in model) flow.Steps.Add(s);
                    int before = flow.Version;
                    container.Children[0].Steps.Add(Trace($"补{i}"));
                    checkedCount++;
                    if (flow.Version <= before) bad++;
                }
                Check("B4：随机图形（各 5 张）往分支里加一步 → Version 一律变大",
                    checkedCount > 0 && bad == 0, $"checked={checkedCount} bad={bad}");
            }

            // ---- 分支顶层 Break/Continue（无循环归属）：两种模式各自的既有 Warn 文案 ----
            {
                // Parallel 模式：分支自己跑 RunSequence(yield:true) → 记 FlowSignal + 分支级 Warn
                var parModel = MakeParallel("越界并行", ParallelExecutionMode.Parallel,
                    new StepModel[] { Trace("A"), Brk("分支顶层Break") },
                    new StepModel[] { Trace("B") });
                var obsPar = Observe(new StepModel[] { parModel }, StressSeedValue);
                bool okPar = obsPar.Compiled && !obsPar.TimedOut && obsPar.Exception == null
                             && MultisetEquals(obsPar.Trace, new[] { "A", "B" })
                             && obsPar.Log.Warns.Any(w => w.Contains("无循环归属") && w.Contains("并行分组"));
                Check("B4：Parallel 模式分支顶层 Break → 只终结本分支 + 「并行分组…无循环归属」Warn（文案不变）", okPar,
                    okPar ? "" : DetailOf(obsPar, new[] { "A", "B" }, "无循环归属") + " warns=[" + string.Join("|", obsPar.Log.Warns) + "]");

                // Sequential 模式：平铺进外层序列 → 顶层孤儿口径 Warn（一期平铺语义，设计文档 §3.7 已声明）
                var seqModel = MakeParallel("越界顺序", ParallelExecutionMode.Sequential,
                    new StepModel[] { Trace("A"), Cnt("分支顶层Continue"), Trace("C") },
                    new StepModel[] { Trace("B") });
                var obsSeq = Observe(new StepModel[] { seqModel }, StressSeedValue);
                bool okSeq = obsSeq.Compiled && !obsSeq.TimedOut && obsSeq.Exception == null
                             && obsSeq.Trace.SequenceEqual(new[] { "A", "C", "B" })
                             && obsSeq.Log.Warns.Any(w => w.Contains("流程顶层出现无循环归属"));
                Check("B4：Sequential 模式分支顶层 Continue → 平铺语义下按顶层孤儿处理（Warn + 后续照跑）", okSeq,
                    okSeq ? "" : DetailOf(obsSeq, new[] { "A", "C", "B" }, "流程顶层出现无循环归属") + " warns=[" + string.Join("|", obsSeq.Log.Warns) + "]");
            }
        }

        // ==================================================================
        //  参考解释器（B0 的"期望值"来源；语义逐条对应编译产物注释，见文件头）
        // ==================================================================
        private enum RefSignal { Normal, Break, Continue, Return }

        /// <summary>参考模型的运行态：trace + 变量域（B0 只用 StressVar 一根）</summary>
        private sealed class RefState
        {
            public readonly List<string> Trace = new();
            public readonly Dictionary<string, double> Vars = new(StringComparer.Ordinal);
        }

        /// <summary>
        /// 参考解释器：按 CompiledNode.RunSequence 的语义跑一棵 StepModel 树。
        /// topLevel=true 对应顶层序列（yield:false：Break/Continue 清状态 + Warn + **继续**）；
        /// topLevel=false 对应循环体（yield:true：Break/Continue 交回上层循环裁决）。
        /// </summary>
        private static RefSignal RunRefList(IReadOnlyList<StepModel> steps, RefState st, bool topLevel)
        {
            foreach (var step in steps)
            {
                if (step == null || step.IsDisEnable) continue;

                var sig = RunRefStep(step, st, topLevel);
                if (sig == RefSignal.Normal) continue;

                if (sig == RefSignal.Return) return RefSignal.Return;  // Return 与层级无关（RunSequence 首位判定）
                if (topLevel) continue;                                // 顶层孤儿：清零继续（引擎 Warn 后同样继续）
                return sig;
            }
            return RefSignal.Normal;
        }

        private static RefSignal RunRefStep(StepModel step, RefState st, bool topLevel)
        {
            // WhileStep : ConditionStep，必须先判 While
            if (step is WhileStep whileStep)
                return RunRefWhile(whileStep, st);
            if (step is ConditionStep condStep)
                return RunRefIf(condStep, st, topLevel);
            if (step is ForStep forStep)
                return RunRefFor(forStep, st);
            if (step is ParallelStep parStep)
            {
                // B0 的并行节点一律 Sequential：平铺 == 引擎 RunSequentialFlattened 的返回值被外层
                // RunSequence 就地消费（含 Break/Continue 归属外层求解器的一期语义）→ 逐元素全等成立。
                var flat = parStep.Children.SelectMany(ch => ch.Steps).ToList();
                return RunRefList(flat, st, topLevel);
            }
            return RunRefLeaf(step, st);
        }

        private static RefSignal RunRefLeaf(StepModel step, RefState st)
        {
            if (step.PluginTypeName == "BuiltIn_Break") return RefSignal.Break;
            if (step.PluginTypeName == "BuiltIn_Continue") return RefSignal.Continue;
            if (step.PluginTypeName == "BuiltIn_Return") return RefSignal.Return;

            // TracePlugin：tag + 可选变量写入（Dec 是 While 终止性的唯一来源）
            if (step.InputValues.TryGetValue("Tag", out var tagObj) && tagObj is string tag)
                st.Trace.Add(tag);

            string varName = step.InputValues.TryGetValue("VarName", out var vn) ? vn as string : null;
            string mode = step.InputValues.TryGetValue("VarMode", out var vm) ? vm as string : null;
            if (string.IsNullOrEmpty(varName) || string.IsNullOrEmpty(mode)) return RefSignal.Normal;

            double cur = st.Vars.TryGetValue(varName, out var v) ? v : 0d;
            if (mode == "Dec") st.Vars[varName] = cur - 1;
            else if (mode == "Set")
                st.Vars[varName] = step.InputValues.TryGetValue("Value", out var val) ? Convert.ToDouble(val) : 0d;
            return RefSignal.Normal;
        }

        private static RefSignal RunRefIf(ConditionStep cond, RefState st, bool topLevel)
        {
            foreach (var branch in cond.Children)
            {
                bool elseLike = branch.BranchType == BranchType.Else || branch.BranchType == BranchType.Default;
                bool hit = elseLike || EvalCond(branch.Expression, StressOf(st));
                if (!hit) continue;
                return RunRefList(branch.Steps.ToList(), st, topLevel);
            }
            return RefSignal.Normal;   // 无兜底且全不命中：判断成功、无事可做
        }

        private static RefSignal RunRefFor(ForStep forStep, RefState st)
        {
            var body = forStep.Children.FirstOrDefault()?.Steps.ToList() ?? new List<StepModel>();
            for (int i = 0; i < forStep.DefaultLoopCount; i++)
            {
                var sig = RunRefList(body, st, topLevel: false);
                if (sig == RefSignal.Break) return RefSignal.Normal;      // 跳出本层
                if (sig == RefSignal.Continue) continue;                  // 下一圈
                if (sig == RefSignal.Return) return RefSignal.Return;     // 交上层终结
            }
            return RefSignal.Normal;
        }

        private static RefSignal RunRefWhile(WhileStep whileStep, RefState st)
        {
            var body = whileStep.Children.FirstOrDefault();
            if (body == null) return RefSignal.Normal;

            for (int iter = 0; ; iter++)
            {
                if (iter > 9999)
                    throw new InvalidOperationException("参考模型：While 迭代超过 MaxIterations（生成器的终止性不变量被破坏）");
                if (!EvalCond(body.Expression, StressOf(st))) return RefSignal.Normal;   // 先判后跑

                var sig = RunRefList(body.Steps.ToList(), st, topLevel: false);
                if (sig == RefSignal.Break) return RefSignal.Normal;
                if (sig == RefSignal.Return) return RefSignal.Return;
                // Continue 与正常跑完都进下一圈（与 CompiledWhileNode 的 iter++ 口径一致）
            }
        }

        private static double StressOf(RefState st) => st.Vars.TryGetValue("StressVar", out var v) ? v : 0d;

        /// <summary>
        /// 条件求值（B0 生成器只会产出这几种形态；出现不认识的表达式 = 生成器与解释器脱节，
        /// 让它响亮地抛，别静默猜一个值把对拍变成自欺）。
        /// </summary>
        private static bool EvalCond(string expr, double stress) => expr switch
        {
            "true" => true,
            "false" => false,
            "StressVar > 0" => stress > 0,
            "StressVar > 2" => stress > 2,
            "(StressVar + 1) % 2 == 0" => (stress + 1) % 2 == 0,
            _ => throw new InvalidOperationException($"参考模型不认识的条件表达式：'{expr}'（生成器与解释器已脱节）"),
        };

        // ==================================================================
        //  随机图生成器（保证两条不变量：While 必然终止、变量名不冲突）
        // ==================================================================
        private static readonly string[] CondPool =
        {
            "true", "false", "StressVar > 2", "StressVar > 0", "(StressVar + 1) % 2 == 0",
        };

        /// <summary>随机图描述（生成器与 Build 之间的中间形态；序列化进 detail 供复现）</summary>
        private sealed class PlanGraph
        {
            public List<PlanNode> Roots { get; set; } = new();
            public bool ExpectCompile { get; set; } = true;
            public string ExpectError { get; set; } = "";
        }

        private sealed class PlanNode
        {
            /// <summary>Action / Break / Continue / If / For / While / Parallel</summary>
            public string Kind { get; set; } = "Action";
            /// <summary>节点/叶子的标记（Action 的 trace tag、容器名）</summary>
            public string Tag { get; set; }
            /// <summary>Action 的变量动作：""（只 trace）/ "Dec"（StressVar-1）</summary>
            public string Op { get; set; } = "";
            /// <summary>If/Parallel 每分支的条件（null = Else 兜底；"" = 非法空条件变体）</summary>
            public string[] Conditions { get; set; }
            /// <summary>While 条件（"" = 非法空条件变体）</summary>
            public string Condition { get; set; }
            /// <summary>If/Parallel 的分支；For/While 只有一项（循环体）</summary>
            public List<List<PlanNode>> Branches { get; set; } = new();
            public int LoopCount { get; set; }
        }

        private sealed class GenCtx
        {
            public Random Rng;
            public int LeafBudget = 12;
            public int TagSeq;
            public bool ExpectCompile = true;
            public string ExpectError = "";

            public string NextTag(string prefix) => prefix + (++TagSeq).ToString("00");

            public void MarkIllegal(string expectError)
            {
                if (!ExpectCompile) return;   // 只记第一处（断言只看"含期望片段"）
                ExpectCompile = false;
                ExpectError = expectError;
            }
        }

        private static PlanGraph GenGraph(int seed)
        {
            var g = new GenCtx { Rng = new Random(seed) };
            var graph = new PlanGraph { Roots = GenList(g, 0) };
            if (graph.Roots.Count == 0) graph.Roots.Add(GenLeaf(g));
            graph.ExpectCompile = g.ExpectCompile;
            graph.ExpectError = g.ExpectError;
            return graph;
        }

        private static List<PlanNode> GenList(GenCtx g, int depth)
        {
            // 顶层至少 2 个节点：单节点图对拍价值太低（要的是"组合"被跑到）
            int n = depth == 0 ? g.Rng.Next(2, 4) : g.Rng.Next(1, 4);
            var list = new List<PlanNode>();
            for (int i = 0; i < n && g.LeafBudget > 0; i++)
                list.Add(GenNode(g, depth));
            return list;
        }

        private static PlanNode GenNode(GenCtx g, int depth)
        {
            if (depth >= 3 || g.LeafBudget < 2) return GenLeaf(g);

            int roll = g.Rng.Next(10);
            if (roll < 4) return GenLeaf(g);
            if (roll < 6) return GenIf(g, depth);
            if (roll < 8) return GenFor(g, depth);
            if (roll < 9) return GenWhile(g, depth);
            return GenParallel(g, depth);
        }

        private static PlanNode GenLeaf(GenCtx g)
        {
            g.LeafBudget--;
            int roll = g.Rng.Next(12);
            if (roll == 0) return new PlanNode { Kind = "Break", Tag = g.NextTag("BRK") };
            if (roll == 1) return new PlanNode { Kind = "Continue", Tag = g.NextTag("CON") };
            return new PlanNode { Kind = "Action", Tag = g.NextTag("L") };
        }

        private static PlanNode GenIf(GenCtx g, int depth)
        {
            int shape = g.Rng.Next(3);                  // 0: If/Else；1: If/ElseIf（无兜底）；2: If/ElseIf/Else
            int branchCount = shape == 0 ? 2 : 3;
            var node = new PlanNode
            {
                Kind = "If",
                Tag = g.NextTag("IF"),
                Conditions = new string[branchCount],
                Branches = new List<List<PlanNode>>(),
            };

            for (int i = 0; i < branchCount; i++)
            {
                bool lastIsElse = i == branchCount - 1 && shape != 1;
                node.Conditions[i] = lastIsElse ? null : CondPool[g.Rng.Next(CondPool.Length)];
                g.LeafBudget--;
                node.Branches.Add(GenList(g, depth + 1));
            }

            // 非法变体（同批约 1/6）：清空一个非兜底分支的条件 → FlowCompiler 硬错误
            if (g.Rng.Next(6) == 0)
            {
                int idx = g.Rng.Next(branchCount - 1);  // 0..branchCount-2：恒为非 Else 分支
                node.Conditions[idx] = "";
                g.MarkIllegal("表达式为空");
            }
            return node;
        }

        private static PlanNode GenFor(GenCtx g, int depth)
        {
            var node = new PlanNode
            {
                Kind = "For",
                Tag = g.NextTag("FOR"),
                LoopCount = g.Rng.Next(1, 4),           // 1~3 圈
                Branches = new List<List<PlanNode>>(),
            };
            g.LeafBudget--;
            var body = GenList(g, depth + 1);
            if (body.Count == 0)
            {
                g.LeafBudget = Math.Max(g.LeafBudget, 1);
                body.Add(GenLeaf(g));
            }
            node.Branches.Add(body);
            return node;
        }

        private static PlanNode GenWhile(GenCtx g, int depth)
        {
            var node = new PlanNode
            {
                Kind = "While",
                Tag = g.NextTag("WH"),
                Condition = "StressVar > 0",
                Branches = new List<List<PlanNode>>(),
            };
            g.LeafBudget--;

            if (g.Rng.Next(8) == 0)
            {
                node.Condition = "";                    // 非法变体：空条件 → 硬错误
                g.MarkIllegal("循环条件表达式为空");
            }

            // 终止性不变量：循环体第一步永远是 Dec（StressVar 单调递减 + 条件 "> 0" ⇒ 必然转假）。
            // 条件只读 StressVar、全图只有 Dec 会写它 —— 变量名不冲突的另一半也由这一条保证。
            var body = new List<PlanNode> { new() { Kind = "Action", Tag = g.NextTag("DEC"), Op = "Dec" } };
            g.LeafBudget--;
            body.AddRange(GenList(g, depth + 1));
            node.Branches.Add(body);
            return node;
        }

        private static PlanNode GenParallel(GenCtx g, int depth)
        {
            var node = new PlanNode { Kind = "Parallel", Tag = g.NextTag("PAR"), Branches = new List<List<PlanNode>>() };
            g.LeafBudget--;

            if (g.Rng.Next(10) == 0)
            {
                g.MarkIllegal("没有任何分支");           // 非法变体：0 分支的并行组 → 硬错误
                return node;
            }

            int branches = g.Rng.Next(2, 4);
            for (int i = 0; i < branches; i++)
            {
                g.LeafBudget--;
                node.Branches.Add(GenList(g, depth + 1));   // 空分支是合法形态（B3 已单独钉）
            }
            return node;
        }

        // ==================================================================
        //  随机图 → StepModel 树（参考解释器读的就是这棵树，杜绝"计划与图纸两张皮"）
        // ==================================================================
        private static StepModel[] BuildGraph(PlanGraph graph)
            => graph.Roots.Select(BuildNode).ToArray();

        private static StepModel BuildNode(PlanNode n)
        {
            switch (n.Kind)
            {
                case "Action":
                {
                    var step = new ActionStep("\uE700", "跟踪", AqnTrace, n.Tag);
                    step.SetInputValue("Tag", n.Tag);
                    if (!string.IsNullOrEmpty(n.Op))
                    {
                        step.SetInputValue("VarName", "StressVar");
                        step.SetInputValue("VarMode", n.Op);
                    }
                    return step;
                }
                case "Break":
                    return new ActionStep("\uE700", "Break", "BuiltIn_Break", n.Tag);
                case "Continue":
                    return new ActionStep("\uE700", "Continue", "BuiltIn_Continue", n.Tag);

                case "If":
                {
                    var cond = new ConditionStep("\uE700", "条件", "BuiltIn_If", n.Tag);
                    cond.Children.Clear();
                    for (int i = 0; i < n.Branches.Count; i++)
                    {
                        bool elseLike = n.Conditions[i] == null;
                        var col = new StepCollection
                        {
                            BranchType = elseLike ? BranchType.Else : (i == 0 ? BranchType.If : BranchType.ElseIf),
                            StepName = elseLike ? "Else" : (i == 0 ? "If" : "ElseIf"),
                            Expression = elseLike ? "" : n.Conditions[i],
                        };
                        foreach (var child in n.Branches[i]) col.Steps.Add(BuildNode(child));
                        cond.Children.Add(col);
                    }
                    AddStressVarRef(cond);   // 条件可能引用 StressVar（运行时变量名表）
                    return cond;
                }
                case "For":
                {
                    var forStep = new ForStep("\uE700", "计次循环", "BuiltIn_For", n.Tag) { DefaultLoopCount = n.LoopCount };
                    foreach (var child in n.Branches[0]) forStep.Children[0].Steps.Add(BuildNode(child));
                    return forStep;
                }
                case "While":
                {
                    var whileStep = new WhileStep("\uE700", "条件循环", "BuiltIn_While", n.Tag);
                    whileStep.Children[0].Expression = n.Condition;
                    AddStressVarRef(whileStep);
                    foreach (var child in n.Branches[0]) whileStep.Children[0].Steps.Add(BuildNode(child));
                    return whileStep;
                }
                case "Parallel":
                {
                    // 统一 Sequential：参考模型按声明序平铺 == 引擎一期平铺路径（文件头"比较口径"）
                    var par = new ParallelStep("\uE700", "并行分组", "BuiltIn_Parallel", n.Tag)
                    {
                        ExecutionMode = ParallelExecutionMode.Sequential,
                    };
                    par.Children.Clear();
                    for (int i = 0; i < n.Branches.Count; i++)
                    {
                        var col = new StepCollection { BranchType = BranchType.Default, StepName = $"分支 {i + 1}" };
                        foreach (var child in n.Branches[i]) col.Steps.Add(BuildNode(child));
                        par.Children.Add(col);
                    }
                    return par;
                }
                default:
                    throw new InvalidOperationException($"生成器产出了未知节点类型 '{n.Kind}'");
            }
        }

        // ==================================================================
        //  手工建图辅助（B1-B4：显式期望值的用例都用它们）
        // ==================================================================
        private static string AqnTrace => typeof(TracePlugin).AssemblyQualifiedName!;
        private static string AqnIndex => typeof(IndexEchoPlugin).AssemblyQualifiedName!;

        private static ActionStep Trace(string tag)
        {
            var s = new ActionStep("\uE700", "跟踪", AqnTrace, tag);
            s.SetInputValue("Tag", tag);
            return s;
        }

        private static ActionStep DecStress(string tag)
        {
            var s = Trace(tag);
            s.SetInputValue("VarName", "StressVar");
            s.SetInputValue("VarMode", "Dec");
            return s;
        }

        private static ActionStep VarWrite(string tag, string varName, double value)
        {
            var s = Trace(tag);
            s.SetInputValue("VarName", varName);
            s.SetInputValue("VarMode", "Set");
            s.SetInputValue("Value", value);
            return s;
        }

        private static ActionStep IndexEcho(string tag)
        {
            var s = new ActionStep("\uE700", "索引回读", AqnIndex, tag);
            s.SetInputValue("Tag", tag);
            return s;
        }

        private static ActionStep Brk(string name) => new("\uE700", "Break", "BuiltIn_Break", name);

        private static ActionStep Cnt(string name) => new("\uE700", "Continue", "BuiltIn_Continue", name);

        private static ActionStep Ret(string name) => new("\uE700", "Return", "BuiltIn_Return", name);

        private static ActionStep BizFailLeaf(string name)
        {
            var s = new ActionStep("\uE700", "业务失败", typeof(BizFailPlugin).AssemblyQualifiedName!, name);
            return s;
        }

        private static ActionStep GateLeaf(string name)
            => new("\uE700", "闸门", typeof(SafeGatePlugin).AssemblyQualifiedName!, name);

        /// <summary>If 容器：cond == null 的分支是 Else 兜底；cond == "" 是"空条件"非法形态</summary>
        private static ConditionStep MakeIf(string name, params (string cond, StepModel[] steps)[] branches)
        {
            var cond = new ConditionStep("\uE700", "条件", "BuiltIn_If", name);
            cond.Children.Clear();
            for (int i = 0; i < branches.Length; i++)
            {
                var (expr, steps) = branches[i];
                bool elseLike = expr == null;
                var col = new StepCollection
                {
                    BranchType = elseLike ? BranchType.Else : (i == 0 ? BranchType.If : BranchType.ElseIf),
                    StepName = elseLike ? "Else" : (i == 0 ? "If" : "ElseIf"),
                    Expression = elseLike ? "" : expr,
                };
                foreach (var s in steps ?? Array.Empty<StepModel>()) col.Steps.Add(s);
                cond.Children.Add(col);
            }
            AddStressVarRef(cond);
            return cond;
        }

        private static ForStep MakeFor(string name, int count, params StepModel[] body)
        {
            var f = new ForStep("\uE700", "计次循环", "BuiltIn_For", name) { DefaultLoopCount = count };
            foreach (var s in body) f.Children[0].Steps.Add(s);
            return f;
        }

        private static WhileStep MakeWhile(string name, string cond, params StepModel[] body)
        {
            var w = new WhileStep("\uE700", "条件循环", "BuiltIn_While", name);
            w.Children[0].Expression = cond;
            AddStressVarRef(w);
            foreach (var s in body) w.Children[0].Steps.Add(s);
            return w;
        }

        private static ParallelStep MakeParallel(string name, ParallelExecutionMode mode, params StepModel[][] branches)
        {
            var p = new ParallelStep("\uE700", "并行分组", "BuiltIn_Parallel", name) { ExecutionMode = mode };
            p.Children.Clear();
            for (int i = 0; i < branches.Length; i++)
            {
                var col = new StepCollection { BranchType = BranchType.Default, StepName = $"分支 {i + 1}" };
                foreach (var s in branches[i]) col.Steps.Add(s);
                p.Children.Add(col);
            }
            return p;
        }

        /// <summary>条件变量声明里的运行时变量名表（StressVar 走 context.LocalVariables，按名现取）</summary>
        private static void AddStressVarRef(ConditionStep cond)
            => cond.RuntimeVariableRefs.Add(new LocalVariableItem { Name = "StressVar", DataTypeName = "System.Double" });

        // ==================================================================
        //  执行装配（看门狗 + trace 快照）
        // ==================================================================
        private sealed class ExecObservation
        {
            public bool Compiled;
            public string Errors = "";
            public bool TimedOut;
            public Exception Exception;
            public List<string> Trace = new();
            public double FinalStress = double.NaN;
            public StubLog Log = new();
            public ExecutionContext Context;
        }

        /// <summary>编译 + 执行（B1-B4 单用例入口）</summary>
        private static ExecObservation Observe(StepModel[] graph, double stressSeed, int timeoutSeconds = WatchdogSeconds)
        {
            var run = ExecHarness.Prepare(graph);
            var obs = new ExecObservation { Compiled = run.Compiled, Errors = run.Errors };
            if (run.Compiled) RunPrepared(run, obs, stressSeed, timeoutSeconds);
            return obs;
        }

        /// <summary>
        /// 跑一次已编译的图：全新上下文 + 预置 StressVar + trace 清零；
        /// 看门狗到点先用协作取消打断（While/For/RunSequence 每迭代/节点都查令牌），再收尾。
        /// </summary>
        private static void RunPrepared(ExecRun run, ExecObservation obs, double stressSeed, int timeoutSeconds)
        {
            var log = new StubLog();
            obs.Log = log;
            TracePlugin.Reset();

            var cts = new CancellationTokenSource();
            var ctx = new ExecutionContext(log, run.Session!, run.Workspace!, cts.Token);
            if (!double.IsNaN(stressSeed)) ctx.LocalVariables["StressVar"] = stressSeed;
            obs.Context = ctx;

            var bg = Task.Run(() =>
            {
                try { run.Engine!.Run(ctx); }
                catch (Exception ex) { obs.Exception = ex; }
            });

            if (!bg.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                cts.Cancel();
                bg.Wait(TimeSpan.FromSeconds(2));
                obs.TimedOut = true;
            }

            obs.Trace = TracePlugin.Snapshot();
            obs.FinalStress = ctx.LocalVariables.TryGetValue("StressVar", out var v) && v != null
                ? Convert.ToDouble(v)
                : double.NaN;
        }

        /// <summary>跑一次图 + 与显式期望 trace 逐元素比对（B1/B2 表格用例）</summary>
        private static void ExpectTrace(string label, StepModel[] graph, string[] expected,
            double stressSeed = StressSeedValue, double? expectedFinalStress = null,
            string requireWarn = null, FlowControlState? expectedFlowState = null)
        {
            var obs = Observe(graph, stressSeed);
            bool ok = obs.Compiled
                      && !obs.TimedOut
                      && obs.Exception == null
                      && obs.Trace.SequenceEqual(expected)
                      && (expectedFinalStress == null || Math.Abs(obs.FinalStress - expectedFinalStress.Value) < 1e-9)
                      && (requireWarn == null || obs.Log.Warns.Any(w => w.Contains(requireWarn)))
                      && (expectedFlowState == null || obs.Context!.CurrentFlowState == expectedFlowState.Value);

            Check(label, ok, ok ? "" : DetailOf(obs, expected, requireWarn)
                + (expectedFinalStress != null ? $" 终值={obs.FinalStress}(期望 {expectedFinalStress})" : "")
                + (expectedFlowState != null ? $" flowState={obs.Context?.CurrentFlowState}(期望 {expectedFlowState})" : ""));
        }

        private static string DetailOf(ExecObservation obs, string[] expected, string requireWarn)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("compiled=").Append(obs.Compiled);
            if (!obs.Compiled) sb.Append(" errors=").Append(obs.Errors);
            sb.Append(" timeout=").Append(obs.TimedOut);
            if (obs.Exception != null) sb.Append(" ex=").Append(obs.Exception.GetType().Name).Append(':').Append(obs.Exception.Message);
            sb.Append(" trace=[").Append(string.Join(",", obs.Trace)).Append(']');
            sb.Append(" 期望=[").Append(string.Join(",", expected)).Append(']');
            if (requireWarn != null) sb.Append(" warns=[").Append(string.Join("|", obs.Log.Warns)).Append(']');
            return sb.ToString();
        }

        // ==================================================================
        //  比较/记录辅助
        // ==================================================================
        /// <summary>排序后多重集相等（并行模式 trace 无序的唯一合法比法）</summary>
        private static bool MultisetEquals(IEnumerable<string> actual, IEnumerable<string> expected)
            => actual.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(expected.OrderBy(x => x, StringComparer.Ordinal));

        /// <summary>取同一分支（tag 前缀）的子序列：分支自己跑 RunSequence ⇒ 内部必然保序</summary>
        private static List<string> BranchOrder(IEnumerable<string> trace, string prefix)
            => trace.Where(t => t.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        /// <summary>trace 首个分歧点（finding 定位用）</summary>
        private static string TraceDiff(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
        {
            int n = Math.Min(actual.Count, expected.Count);
            for (int i = 0; i < n; i++)
                if (!string.Equals(actual[i], expected[i], StringComparison.Ordinal))
                    return $"首个分歧 @{i}：引擎 '{actual[i]}' vs 参考 '{expected[i]}'（引擎 {actual.Count} 项 / 参考 {expected.Count} 项）";
            return $"长度不一致：引擎 {actual.Count} 项 vs 参考 {expected.Count} 项（前缀全等）";
        }

        /// <summary>遍历全树，产出 TracePlugin 叶子（状态交叉检查与终态集比对的取样面）</summary>
        private static IEnumerable<ActionStep> WalkLeaves(IEnumerable<StepModel> roots)
        {
            foreach (var step in roots)
            {
                if (step == null) continue;
                if (step is IContainerStep container)
                {
                    foreach (var branch in container.Children)
                        foreach (var leaf in WalkLeaves(branch.Steps))
                            yield return leaf;
                }
                else if (step is ActionStep action && action.PluginTypeName == AqnTrace)
                {
                    yield return action;
                }
            }
        }

        private static string TagOf(ActionStep step)
            => step.InputValues.TryGetValue("Tag", out var v) ? v as string ?? "" : "";

        private static SortedDictionary<string, StepState> StateMap(StepModel[] roots)
        {
            var map = new SortedDictionary<string, StepState>(StringComparer.Ordinal);
            foreach (var leaf in WalkLeaves(roots)) map[TagOf(leaf)] = leaf.State;
            return map;
        }

        private static string PlanJson(PlanGraph plan)
            => JsonConvert.SerializeObject(plan, Formatting.None);

        /// <summary>节点种类普查（给"这批随机图到底测到了什么"留证据：不能只有叶子）</summary>
        private static Dictionary<string, int> NodeCensus(IEnumerable<StepModel> roots)
        {
            var census = new Dictionary<string, int>(StringComparer.Ordinal);
            Walk(roots);
            return census;

            void Walk(IEnumerable<StepModel> steps)
            {
                foreach (var s in steps)
                {
                    if (s == null) continue;
                    string kind = s switch
                    {
                        WhileStep => "While",
                        ConditionStep => "If",
                        ForStep => "For",
                        ParallelStep => "Parallel",
                        _ => s.PluginTypeName switch
                        {
                            "BuiltIn_Break" => "Break",
                            "BuiltIn_Continue" => "Continue",
                            "BuiltIn_Return" => "Return",
                            _ => "Action",
                        },
                    };
                    census[kind] = census.TryGetValue(kind, out var c) ? c + 1 : 1;
                    if (s is IContainerStep container)
                        foreach (var branch in container.Children)
                            Walk(branch.Steps);
                }
            }
        }

        private static void MergeCensus(Dictionary<string, int> into, Dictionary<string, int> part)
        {
            foreach (var kv in part)
                into[kv.Key] = into.TryGetValue(kv.Key, out var c) ? c + kv.Value : kv.Value;
        }

        /// <summary>图纸 JSON（复现用）：与 Harness 同款 RoundTripSettings；过长截断，图可由 seed+i 确定重建</summary>
        private static string BlueprintJson(StepModel[] model)
        {
            string json = JsonConvert.SerializeObject(model, RoundTripSettings);
            return json.Length <= 2000 ? json : json.Substring(0, 2000) + "…(截断；用同 seed+i 重建可得完整图)";
        }
    }
}
