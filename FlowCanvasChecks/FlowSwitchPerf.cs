using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using VisionMaster.Views;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 流程切换卡顿的量化探针（<c>--switch-perf [方案路径] [reps]</c>，选择性运行，不进常规 gate）。
    ///
    /// 为什么必须真视图 + 真方案：切换卡顿的分母是 WPF 的容器生成。NodifyEditor 的条目面板是
    /// NodifyCanvas（**不虚拟化**），流程栏那棵 TreeView 也不虚拟化——这两段成本 headless 的
    /// VM 断言一条都量不到。2026-10-10 的 perf 场景证明了「RebuildInPlace 被固定项托底、
    /// 全帧随节点数线性」，但没回答「一次切换里，UI 线程被占住多久、其中 VM / 布局 / 延迟工作各占多少」。
    ///
    /// 三段分别计时（互不重叠）：
    ///   ① vm       = <c>Workspace.SwitchFlow</c> 同步返回（VM 重建、Nodes/Links 集合变化、绑定通知）
    ///   ② layout   = 紧随其后的 <c>UpdateLayout()</c>（ItemContainer 生成、模板展开、连线出图）
    ///   ③ deferred = 泵 Background 帧直到队列排空（≤Mmax 帧）。这一段装的是**切换自己排进去的
    ///                后续工作**：画布的 <c>FitToScreen</c>（FlowCanvasView.OnViewportActionRequested
    ///                排在 Background）、流程栏的选中同步、以及 200ms 运行时计时器。
    ///                **不含渲染线程**——渲染线程在计时开始前已由 WaitRender 对齐。
    ///
    /// 两个必须说清的边界：
    ///   · 这三段都是 **UI 线程忙时**，不是"到画面稳定"的总时长。渲染线程的实际提交无法在
    ///    托管侧观测，只能靠 WaitRender 这种"等一帧"的间接对齐，因此本探针不下"端到端多少毫秒"的结论。
    ///   · <c>deferred</c> 里包含 <c>FitToScreen</c>，而它对用户是不可见的准备动作，但确实占 UI 线程。
    ///
    /// 相位（变体对照，逐个单独测才能归因）：
    ///   canvas         只画布，默认开关（顺序链开、数据线关）
    ///   canvas-noorder 关顺序链
    ///   canvas-minimap 打开小地图
    ///   canvas-collapse 全部容器折叠（逐流程设标记，不靠命令）
    ///   tree           只流程栏
    ///
    /// 输出是机器可读行；文档里用「三次独立进程各跑一遍，取三次的中位数」抵消进程内抖动。
    /// </summary>
    internal static class FlowSwitchPerf
    {
        /// <summary>每条流程的热切换轮数（奇数，中位数落在样本上）</summary>
        private const int Rounds = 7;

        /// <summary>
        /// 窗口实验的节点上限（0 = 不裁）。由相位名 `windowNNN` 设置：
        /// 只让前 N 个节点参与布局，用来量"布局耗时 vs 节点数"的曲线，
        /// 判断"分页限量"到底能不能把打开时间压下来。
        /// </summary>
        private static int WindowLimit;

        /// <summary>窗口实验需要拿到画布 VM 才能裁 Nodes（由 RunCore 注入）</summary>
        private static FlowCanvasViewModel? _canvasVmForWindow;

        /// <summary>deferred 段最多泵几帧；超了就是"队列一直有事"，记下来而不是无限等</summary>
        private const int MaxDeferredFrames = 8;

        /// <summary>逐流程打行只打这几个大类，其余只进汇总（输出太长反而没人读）</summary>
        private static readonly string[] VerboseFlows =
        {
            "Test_Vision", "Test_Logic", "Test_Comm", "Test_FlowEdge",
        };

        public static void Run(string? solutionPath, int reps, string phase)
        {
            var thread = new Thread(() => RunCore(solutionPath, reps, phase));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        /// <summary>合法相位名（每个相位必须在独立进程里跑，否则前一相位的残留会算进后一相位）</summary>
        public static readonly string[] Phases = { "canvas", "surface", "datalinks", "open", "open-fluent", "noorder", "minimap", "collapse", "tree", "none", "floor", "diag" };

        private static void RunCore(string? solutionPath, int reps, string phase)
        {
            try
            {
                var repoRoot = FindRepoRoot();
                if (repoRoot == null)
                {
                    Console.WriteLine("[switchperf] FAIL: 找不到仓库根（AGENTS.md）");
                    Environment.ExitCode = 1;
                    return;
                }

                var vmsPath = ResolveSolutionPath(repoRoot, solutionPath);
                if (vmsPath == null)
                {
                    Console.WriteLine("[switchperf] FAIL: 未指定方案，且 解决方案\\ 下没有可用的 .vms");
                    Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine("[switchperf] vms=" + vmsPath);

                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                // fluent 相位：按真机 App.xaml:67-70 的口径合并 UI 主题分册 + .NET 9 Fluent。
                // 这是探针与真机最大的环境差异——Fluent 会给大量隐式样式（TreeViewItem / MenuItem /
                // ScrollViewer / Button…）套上自己的模板与触发器，逐元素成本会显著上升。
                PrepareResources(repoRoot, withFluentTheme: phase.EndsWith("-fluent", StringComparison.Ordinal));

                var loadWatch = System.Diagnostics.Stopwatch.StartNew();
                var loaded = new SolutionService().LoadAsync(vmsPath).GetAwaiter().GetResult();
                Console.WriteLine($"[switchperf] load_ms={loadWatch.ElapsedMilliseconds}");
                if (!loaded.Success || loaded.Data == null)
                {
                    Console.WriteLine("[switchperf] FAIL: 方案加载失败 " + loaded.Message);
                    Environment.ExitCode = 1;
                    return;
                }

                var solution = loaded.Data;
                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);
                VariablePersistenceService.Restore(solution, workspace);

                // 真 VM：ProcessViewModel 会起 200ms 运行时计时器（真机同款，顺带验证它不构成主项）
                var processVm = new ProcessViewModel(workspace, new SilentDialogService());
                var canvasVm = new FlowCanvasViewModel(workspace, new StubPluginProvider());
                // 与真机同一口径的渲染分段日志（RenderCore 内部计时），输出到 stdout 便于逐段对照
                FlowCanvasViewModel.PerfLog = message => Console.WriteLine("[switchperf] " + message);
                Prism.Mvvm.ViewModelLocationProvider.SetDefaultViewModelFactory(t =>
                {
                    if (t == typeof(ProcessViewModel)) return processVm;
                    if (t == typeof(FlowCanvasViewModel)) return canvasVm;
                    // ProcessView 顶部嵌入的 FlowListView 在探针里没有容器：给 null 让它静默空跑
                    if (t == typeof(FlowListViewModel)) return null!;
                    return Activator.CreateInstance(t)!;
                });

                var (host, window) = BuildHost();
                host.Measure(new Size(1600, 900));
                host.Arrange(new Rect(0, 0, 1600, 900));
                window.UpdateLayout();
                WaitRender();
                Drain();

                var flows = solution.Flows.Where(f => f != null).ToList();
                Console.WriteLine($"[switchperf] solution={Path.GetFileName(vmsPath)} flows={flows.Count}");
                Console.WriteLine(
                    $"[switchperf] rounds={Rounds} reps={reps} "
                    + "scope=UI 线程忙时（vm+layout+deferred），不含渲染线程");
                // 渲染档位必须打出来：Tier 0 是纯软件渲染，容器生成与渲染提交会慢一个数量级，
                // 拿这种机器上的绝对毫秒数当基线会得出完全错误的结论（同一台机器开/关远程桌面就变）。
                Console.WriteLine(
                    $"[switchperf] render_tier={RenderCapability.Tier >> 16} "
                    + $"ps2={RenderCapability.IsPixelShaderVersionSupported(2, 0)} "
                    + $"processors={Environment.ProcessorCount}");

                try
                {
                    RunSinglePhase(phase, host, flows, workspace, canvasVm, processVm, reps);
                }
                finally
                {
                    // 折叠变体会改布局库的折叠标记：本次进程内还原一次，便于前后对照
                    foreach (var flow in flows)
                        foreach (var container in CollectContainers(flow.Steps))
                            flow.Layout.SetCollapsed(container.StepID, false);

                    window.Close();
                }

                app.Shutdown();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[switchperf] FAIL: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Environment.ExitCode = 1;
            }
        }

        /// <summary>
        /// 只跑一个相位。
        ///
        /// 为什么不再一个进程跑全部相位：2026-10-23 实测同一进程里连跑五个相位，越靠后的相位数字
        /// 越大（canvas 238ms → tree 1601ms），而中间只有开关差异——这是进程内累积（WPF 缓存/GC 堆/
        /// 渲染线程队列）造成的污染，不是相位本身的差异。归因必须每相位一个干净进程。
        /// </summary>
        private static void RunSinglePhase(
            string phase,
            Grid host, List<FlowModel> flows, WorkspaceContext workspace,
            FlowCanvasViewModel canvasVm, ProcessViewModel processVm, int reps)
        {
            switch (phase)
            {
                case "canvas":
                    MeasurePhase("canvas", host, flows, workspace, canvasVm, processVm, reps,
                        measureCold: true, keepCanvas: true, keepTree: false);
                    break;

                case "noorder":
                    MeasurePhase("canvas-noorder", host, flows, workspace, canvasVm, processVm, reps,
                        measureCold: false, keepCanvas: true, keepTree: false,
                        configure: cv => cv.ShowOrderLinks = false);
                    break;

                case "datalinks":
                    // 用户机器上 AppConfig.json 的 ShowDataLinks 是 **true**（默认值是 false）。
                    // 探针一直按默认 false 在量，等于漏掉了"每根数据线一个 StepConnection 元素"这一整层。
                    // 这一相位就是"真机口径"：数据线 + 顺序链全开。
                    MeasurePhase("canvas-datalinks", host, flows, workspace, canvasVm, processVm, reps,
                        measureCold: false, keepCanvas: true, keepTree: false,
                        configure: cv => { cv.ShowDataLinks = true; cv.ShowOrderLinks = true; });
                    break;

                case "open-fluent":
                    // 真机环境口径：资源在 RunCore 开头已按 "-fluent" 后缀合并了 UI 分册 + Fluent，
                    // 这里只负责量"首次打开"。与 "open" 的唯一差别就是主题。
                    MeasurePhase("open-fluent", host, flows, workspace, canvasVm, processVm, reps,
                        measureCold: true, keepCanvas: true, keepTree: true,
                        configure: cv => { cv.ShowDataLinks = true; cv.ShowOrderLinks = true; });
                    break;

                case "open":
                    // "打开方案"口径：只量一次冷切换（=== 首次挂接该流程），不做热循环。
                    // 真机卡死的场景是"打开 .vms 后第一次显示"，不是来回切。
                    MeasurePhase("open", host, flows, workspace, canvasVm, processVm, reps,
                        measureCold: true, keepCanvas: true, keepTree: true,
                        configure: cv => { cv.ShowDataLinks = true; cv.ShowOrderLinks = true; });
                    break;

                case "minimap":
                    MeasurePhase("canvas-minimap", host, flows, workspace, canvasVm, processVm, reps,
                        measureCold: false, keepCanvas: true, keepTree: false,
                        configure: cv => cv.ShowMinimap = true);
                    break;

                case "collapse":
                    // 逐流程设折叠标记：CollapseAllCommand 只作用于"当时挂接的那条流程"，
                    // 用它做相位开关会让其余流程保持展开（2026-10-23 实测：nodes 数没变）
                    MeasurePhase("canvas-collapse", host, flows, workspace, canvasVm, processVm, reps,
                        measureCold: false, keepCanvas: true, keepTree: false,
                        configure: _ =>
                        {
                            foreach (var flow in flows)
                                foreach (var container in CollectContainers(flow.Steps))
                                    flow.Layout.SetCollapsed(container.StepID, true);
                        });
                    break;

                case "tree":
                    MeasurePhase("tree", host, flows, workspace, canvasVm, processVm, reps,
                        measureCold: false, keepCanvas: false, keepTree: true);
                    break;

                case "none":
                    // 对照：两栏都收起。剩下的就是"两个 VM 各自重建 + 绑定通知"的纯 VM 成本，
                    // 用来证明 layout 段确实是 WPF 容器生成而不是 VM 侧（否则 vm 段会吃掉它）
                    MeasurePhase("none", host, flows, workspace, canvasVm, processVm, reps,
                        measureCold: false, keepCanvas: false, keepTree: false);
                    break;

                case "floor":
                    // 归零对照：不切流程、只 UpdateLayout + 排空。用来回答"layout 那几百毫秒到底
                    // 是切换引起的，还是这台机器上 UpdateLayout 本身就有底噪（点阵背景/GC/渲染线程）"。
                    MeasureFloor(host, flows, workspace, reps);
                    break;

                case "diag":
                    MeasureDiag(host, flows, workspace, canvasVm);
                    break;

                case "surface":
                    // C2 决策测量：把画布换成自绘层（只读），同一份流程同一相位口径。
                    // 期望形态：layout 段塌到接近 0，成本转到 deferred（SetModel 重拍快照）。
                    {
                        var view = host.Children.OfType<VisionMaster.Views.FlowCanvasView>().FirstOrDefault();
                        if (view == null)
                        {
                            Console.WriteLine("[switchperf] FAIL: 找不到 FlowCanvasView");
                            Environment.ExitCode = 1;
                            return;
                        }
                        view.UseSelfDrawnCanvas = true;
                        Console.WriteLine($"[switchperf] self_drawn=on active={view.IsSelfDrawnActive}");
                        MeasurePhase("surface", host, flows, workspace, canvasVm, processVm, reps,
                            measureCold: true, keepCanvas: true, keepTree: false);
                        break;
                    }

                default:
                    // 窗口实验：phase = "window60" / "window120" / "window240" ...
                    if (phase.StartsWith("window", StringComparison.Ordinal)
                        && int.TryParse(phase.Substring(6), out int limit) && limit > 0)
                    {
                        WindowLimit = limit;
                        _canvasVmForWindow = canvasVm;
                        Console.WriteLine($"[switchperf] window_limit={limit}");
                        MeasurePhase($"window{limit}", host, flows, workspace, canvasVm, processVm, reps,
                            measureCold: true, keepCanvas: true, keepTree: false);
                        break;
                    }

                    Console.WriteLine($"[switchperf] FAIL: 未知相位 '{phase}'，可选：{string.Join(" / ", Phases)}");
                    Environment.ExitCode = 1;
                    return;
            }

            foreach (var flow in flows)
                foreach (var container in CollectContainers(flow.Steps))
                    flow.Layout.SetCollapsed(container.StepID, false);
            canvasVm.RebuildInPlace();
        }

        /// <summary>
        /// 解析被测方案：显式参数优先；否则取 <c>解决方案\</c> 下**最大的** .vms
        /// （含一级子目录里的文件夹式方案）。
        ///
        /// 为什么不写死文件名：原先默认读 <c>综合测试台（1023）.vms</c>，该文件被"保存为文件夹式方案"
        /// 取代后消失，探针从此不带参数就报"方案不存在"——这类"默认值悄悄失效"和 R65 的陈旧 exe
        /// 是同一类问题：数字看起来正常，其实量的是别的东西。
        /// </summary>
        private static string? ResolveSolutionPath(string repoRoot, string? explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
                return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : null;

            var dir = Path.Combine(repoRoot, "解决方案");
            if (!Directory.Exists(dir)) return null;

            var candidates = Directory.GetFiles(dir, "*.vms", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.Length)
                .ToList();

            // 太小的多半是演示件，压测没有意义；取最大的那份（压测方案通常是最大的）
            return candidates.Count == 0 ? null : candidates[0].FullName;
        }

        /// <summary>一个相位的完整测量：可选冷 + Rounds×reps 轮热切换</summary>
        private static void MeasurePhase(
            string phase,
            Grid host,
            List<FlowModel> flows,
            WorkspaceContext workspace,
            FlowCanvasViewModel canvasVm,
            ProcessViewModel processVm,
            int reps,
            bool measureCold,
            bool keepCanvas,
            bool keepTree,
            Action<FlowCanvasViewModel>? configure = null)
        {
            ApplyPhaseHost(host, keepCanvas, keepTree);
            host.UpdateLayout();
            WaitRender();

            // 相位自检：必须证明"收起"真的生效，否则相位对照全部作废（2026-10-23 第一版就踩了：
            // 五个相位的 layout 全在 330~530ms，等于相位开关没改变被测对象）
            ReportPhaseState(phase, host);

            // 只画布的相位里，流程栏 VM 的 200ms 运行时计时器仍在跑（它一 tick 就遍历全树 +
            // 发两条统计通知）。那不是"切换"的成本，却会随机落进某个测量窗口——实测这是
            // 同一相位两次跑能差 2 倍的主因之一。流程栏不在相位里时就把它停掉。
            if (keepTree)
                processVm.Activate();
            else
                processVm.Deactivate();

            configure?.Invoke(canvasVm);

            // 相位起点对齐：渲染对齐 + 延迟队列排空 + GC 归零，否则上一相位残余会算进本相位的 deferred
            host.UpdateLayout();
            WaitRender();
            Drain();
            CollectGarbage();
            int gc0 = GC.CollectionCount(2);

            var summaryVm = new List<double>();
            var summaryLayout = new List<double>();
            var summaryDeferred = new List<double>();
            var summaryCold = new List<double>();
            var heavyVm = new List<double>();
            var heavyLayout = new List<double>();
            var heavyDeferred = new List<double>();
            int deferredCapped = 0;

            // 地板：切到"已经是当前流程"（SetProperty 短路、零重建）后走同一条计时 —— 纯噪声底
            var nopSamples = new List<double>();
            for (int i = 0; i < 10; i++)
                nopSamples.Add(MeasureNop(workspace, host, flows[0]));

            foreach (var flow in flows)
            {
                var other = flows.FirstOrDefault(f => !ReferenceEquals(f, flow));
                if (other == null) continue;

                if (measureCold)
                {
                    bool layoutMissing = flow.Layout.HasMissing(EnumerateSteps(flow.Steps));
                    var s = MeasureOnce(workspace, host, other, flow, out bool capped);
                    if (capped) deferredCapped++;
                    double total = s.Vm + s.Layout + s.Deferred;
                    summaryCold.Add(total);
                    Console.WriteLine(
                        $"[switchperf] cold phase={phase} flow={flow.FlowName} "
                        + $"steps={CountSteps(flow.Steps)} layoutWasMissing={layoutMissing} "
                        + $"vm={s.Vm:0.0} layout={s.Layout:0.0} deferred={s.Deferred:0.0} total={total:0.0}");
                }
                else
                {
                    // 未量冷的相位先呈现一次，保证下面的"热"确实是热
                    GoTo(workspace, host, flow);
                }

                var vms = new List<double>(Rounds * reps);
                var layouts = new List<double>(Rounds * reps);
                var deferreds = new List<double>(Rounds * reps);
                var gcPauses = new List<double>(Rounds * reps);

                for (int round = 0; round < Rounds * reps; round++)
                {
                    var s = MeasureOnce(workspace, host, other, flow, out bool capped);
                    if (capped) deferredCapped++;
                    vms.Add(s.Vm);
                    layouts.Add(s.Layout);
                    deferreds.Add(s.Deferred);
                    gcPauses.Add(s.GcPause);
                }

                double mvm = Median(vms), mlay = Median(layouts), mdef = Median(deferreds);

                summaryVm.AddRange(vms);
                summaryLayout.AddRange(layouts);
                summaryDeferred.AddRange(deferreds);

                if (CountSteps(flow.Steps) >= 30)
                {
                    heavyVm.AddRange(vms);
                    heavyLayout.AddRange(layouts);
                    heavyDeferred.AddRange(deferreds);
                }

                if (VerboseFlows.Contains(flow.FlowName, StringComparer.Ordinal) || flows.Count <= 6)
                {
                    Console.WriteLine(
                        $"[switchperf] warm phase={phase} flow={flow.FlowName} "
                        + $"steps={CountSteps(flow.Steps)} nodes={canvasVm.Nodes.Count} links={canvasVm.Links.Count} "
                        + $"vm={mvm:0.0} layout={mlay:0.0} deferred={mdef:0.0} gc={Median(gcPauses):0.0} "
                        + $"total={mvm + mlay + mdef:0.0} p90={P90(vms) + P90(layouts) + P90(deferreds):0.0}");
                }
            }

            Console.WriteLine(
                $"[switchperf] summary phase={phase} "
                + $"nop_floor={Median(nopSamples):0.0} "
                + $"all_vm={Median(summaryVm):0.0} all_layout={Median(summaryLayout):0.0} "
                + $"all_total={Median(summaryVm) + Median(summaryLayout) + Median(summaryDeferred):0.0} "
                + $"heavy_vm={Median(heavyVm):0.0} heavy_layout={Median(heavyLayout):0.0} "
                + $"heavy_deferred={Median(heavyDeferred):0.0} "
                + $"heavy_total={Median(heavyVm) + Median(heavyLayout) + Median(heavyDeferred):0.0} "
                + $"capped={deferredCapped} gc2={GC.CollectionCount(2) - gc0}"
                + (measureCold ? $" cold_total_median={Median(summaryCold):0.0} cold_max={Max(summaryCold):0.0}" : ""));
        }

        /// <summary>
        /// 归零对照：**不切流程**，只重复 <c>UpdateLayout()</c> + 排空。
        ///
        /// 为什么必须有它：切换的 layout 段里混着"这台机器上 UpdateLayout 本身的底噪"
        /// （点阵 DrawingBrush 背景、GC、渲染线程排队）。没有这一列，就无法回答
        /// "四百毫秒里有多少是切换造成的、有多少是每帧都要付的"。
        /// </summary>
        private static void MeasureFloor(Grid host, List<FlowModel> flows, WorkspaceContext workspace, int reps)
        {
            ApplyPhaseHost(host, keepCanvas: true, keepTree: true);
            host.UpdateLayout();
            WaitRender();
            Drain();
            ReportPhaseState("floor", host);
            CollectGarbage();

            var layoutSamples = new List<double>();
            var deferredSamples = new List<double>();

            GoTo(workspace, host, flows[0]);

            for (int i = 0; i < Rounds * reps; i++)
            {
                var sw = Stopwatch.StartNew();
                host.UpdateLayout();
                layoutSamples.Add(sw.Elapsed.TotalMilliseconds);

                sw.Restart();
                DrainBounded(MaxDeferredFrames);
                deferredSamples.Add(sw.Elapsed.TotalMilliseconds);
            }

            Console.WriteLine(
                $"[switchperf] summary phase=floor n={layoutSamples.Count} "
                + $"layout={Median(layoutSamples):0.0} layout_max={Max(layoutSamples):0.0} "
                + $"deferred={Median(deferredSamples):0.0}");
        }

        /// <summary>
        /// 归因实验：把"切换"这件事拆成三个越来越小的子集，看 layout 段的钱花在哪。
        ///
        /// 为什么需要它（2026-10-23 的实测逼出来的）：`none` 相位（两栏都 Collapsed）的 layout
        /// 与 `canvas` 相位几乎一样（230~280ms），而 `floor` 相位（不切流程）是 0.0ms。
        /// 也就是说这笔钱既不是画布容器生成、也不是流程树容器生成——**必须再往下拆**，
        /// 否则任何"优化渲染"的改动都打不到点上。三个子集：
        ///   A. none      两栏可见但收起（已有相位，作为基线）
        ///   B. detached  两栏 DataContext 置 null（切断一切绑定），仍调 SwitchFlow
        ///   C. vm-only   连视图都不实例化，只跑 SwitchFlow（纯 VM 侧）
        /// 若 B 掉到接近 floor、C 更低 → 钱在绑定求值；若 B 仍高 → 钱在 VM 侧分配 + GC。
        /// </summary>
        private static void MeasureDiag(Grid host, List<FlowModel> flows, WorkspaceContext workspace, FlowCanvasViewModel canvasVm)
        {
            ApplyPhaseHost(host, keepCanvas: true, keepTree: true);
            host.UpdateLayout();
            WaitRender();
            Drain();
            ReportPhaseState("diag-base", host);
            CollectGarbage();

            // 必须用**大流程**做靶：第一版拿了 flows[0]（Home，11 步）做 A 样本，
            // 得到 layout=41ms，而同一相位在汇总里是 210ms——样本量级不同，结论会全错。
            var targets = flows.Where(f => CountSteps(f.Steps) >= 30).ToList();
            var other = targets.FirstOrDefault(f => !ReferenceEquals(f, targets[0])) ?? targets[0];

            // ---- A：两栏可见但收起（与 none 相位同构）----
            ApplyPhaseHost(host, keepCanvas: false, keepTree: false);
            host.UpdateLayout();
            WaitRender();
            Drain();
            foreach (var flow in targets)
                Console.WriteLine($"[switchperf] diag A_none flow={flow.FlowName} {RunDiagSample(workspace, host, other, flow)}");

            // ---- B：只摘画布 DataContext（画布 VM 随之 Deactivate），流程栏仍在 ----
            var canvasView = host.Children.OfType<FlowCanvasView>().FirstOrDefault();
            var treeView = host.Children.OfType<ProcessView>().FirstOrDefault();
            if (canvasView != null) canvasView.DataContext = null;
            host.UpdateLayout();
            WaitRender();
            Drain();
            ReportPhaseState("diag-noCanvasContext", host);
            foreach (var flow in targets)
                Console.WriteLine($"[switchperf] diag B_noCanvasCtx flow={flow.FlowName} {RunDiagSample(workspace, host, other, flow)}");

            // ---- C：再摘流程栏 DataContext（两个 VM 都停止监听），只剩工作区自身 ----
            if (treeView != null) treeView.DataContext = null;
            host.UpdateLayout();
            WaitRender();
            Drain();
            ReportPhaseState("diag-noContexts", host);
            foreach (var flow in targets)
                Console.WriteLine($"[switchperf] diag C_noContexts flow={flow.FlowName} {RunDiagSample(workspace, host, other, flow)}");

            // ---- D：分配量。GC 停顿只是结果，"每次切换分配了多少 MB"才是能下手的地方。
            //        用 GetAllocatedBytesForCurrentThread（UI 线程）——画布重建全程同步跑在它上面。
            if (canvasView != null) canvasView.DataContext = canvasVm;
            host.UpdateLayout();
            WaitRender();
            Drain();
            foreach (var flow in targets)
            {
                GoTo(workspace, host, other);
                long before = GC.GetAllocatedBytesForCurrentThread();
                workspace.SwitchFlow(flow);
                long after = GC.GetAllocatedBytesForCurrentThread();
                Console.WriteLine(
                    $"[switchperf] diag D_alloc flow={flow.FlowName} steps={CountSteps(flow.Steps)} "
                    + $"alloc_kb={(after - before) / 1024.0:0.0} nodes={canvasVm.Nodes.Count} links={canvasVm.Links.Count}");
            }
        }

        /// <summary>一次切换的 vm/layout/deferred/GC 快照（诊断用，单样本）</summary>
        private static string RunDiagSample(WorkspaceContext workspace, Grid host, FlowModel other, FlowModel target)
        {
            GoTo(workspace, host, other);

            long pauseBefore = GC.GetTotalPauseDuration().Ticks;
            var sw = Stopwatch.StartNew();
            workspace.SwitchFlow(target);
            double vm = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            host.UpdateLayout();
            double layout = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            DrainBounded(MaxDeferredFrames);
            double deferred = sw.Elapsed.TotalMilliseconds;

            double gc = TimeSpan.FromTicks(GC.GetTotalPauseDuration().Ticks - pauseBefore).TotalMilliseconds;
            return $"vm={vm:0.0} layout={layout:0.0} deferred={deferred:0.0} gc={gc:0.0}";
        }

        private readonly record struct Sample(double Vm, double Layout, double Deferred, double GcPause);

        /// <summary>
        /// 先切到 other 再切到 target，三段分别计时。
        ///
        /// 计时前必须 WaitRender + Drain：否则 GoTo 留下的渲染积压与延迟工作会被算进 target 的
        /// deferred（2026-10-23 实测：不做这一步，同一相位两次跑出的 deferred 能差 5 倍）。
        /// </summary>
        private static Sample MeasureOnce(
            WorkspaceContext workspace, Grid host, FlowModel other, FlowModel target, out bool capped)
        {
            GoTo(workspace, host, other);

            long pauseBefore = GC.GetTotalPauseDuration().Ticks;

            var sw = Stopwatch.StartNew();
            workspace.SwitchFlow(target);
            double vm = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            // 窗口实验（phase=windowNNN）：只让前 N 个节点留在 Nodes 里再布局。
            // 目的：量「布局耗时 vs 节点数」是线性还是超线性——决定"分页限量"能不能救。
            if (WindowLimit > 0 && _canvasVmForWindow != null
                && _canvasVmForWindow.Nodes.Count > WindowLimit)
            {
                while (_canvasVmForWindow.Nodes.Count > WindowLimit)
                    _canvasVmForWindow.Nodes.RemoveAt(_canvasVmForWindow.Nodes.Count - 1);
            }
            host.UpdateLayout();
            double layout = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            capped = DrainBounded(MaxDeferredFrames);
            double deferred = sw.Elapsed.TotalMilliseconds;

            // GC 暂停是这台机器上 layout 段抖动的主嫌疑：同一相位三次跑能差 10%。
            // 记下来是为了能在报告里把"GC 占多少"与"容器生成占多少"分开——否则
            // 一次 Gen2 停顿会被误读成"切换变慢了"。
            double gcPause = TimeSpan.FromTicks(GC.GetTotalPauseDuration().Ticks - pauseBefore).TotalMilliseconds;

            return new Sample(vm, layout, deferred, gcPause);
        }

        /// <summary>零重建的 pump 地板：切到"已经是当前"的流程（SetProperty 短路）后走同一条计时</summary>
        private static double MeasureNop(WorkspaceContext workspace, Grid host, FlowModel current)
        {
            GoTo(workspace, host, current);
            WaitRender();

            var sw = Stopwatch.StartNew();
            workspace.SwitchFlow(current);
            host.UpdateLayout();
            DrainBounded(MaxDeferredFrames);
            return sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>切到指定流程并让其彻底落定（不计时）：切 → 布局 → 渲染对齐 → 队列排空</summary>
        private static void GoTo(WorkspaceContext workspace, Grid host, FlowModel flow)
        {
            workspace.SwitchFlow(flow);
            host.UpdateLayout();
            WaitRender();
            Drain();
        }

        /// <summary>排空延迟队列（不计时用）：一直泵到 Background 无事可做，上限 32 帧</summary>
        private static void Drain()
        {
            for (int i = 0; i < 32; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                if (Dispatcher.CurrentDispatcher.HasShutdownStarted) return;
            }
        }

        /// <summary>排空延迟队列（计时用）：最多 maxFrames 帧；返回 true = 到上限还没排空</summary>
        private static bool DrainBounded(int maxFrames)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                if (Dispatcher.CurrentDispatcher.HasShutdownStarted) return false;
            }

            // 到上限还活着 = 队列一直有事。用一个"是否还有 Background 待办"的探针判定
            return !IsQueueQuiet();
        }

        /// <summary>Background 队列是否已经排空（投一个标记，泵到它 == 空）</summary>
        private static bool IsQueueQuiet()
        {
            bool ran = false;
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => ran = true));
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            return ran;
        }

        /// <summary>
        /// 等一帧渲染完成：CompositionTarget.Rendering 在**渲染线程提交之前**于 UI 线程触发，
        /// 所以拿它只能保证"UI 侧的渲染准备工作做完"。够用来避免把上一轮的渲染积压算进本轮。
        /// </summary>
        private static void WaitRender()
        {
            var frame = new DispatcherFrame();
            EventHandler? handler = null;
            handler = (s, e) =>
            {
                CompositionTarget.Rendering -= handler;
                frame.Continue = false;
            };
            CompositionTarget.Rendering += handler;
            Dispatcher.PushFrame(frame);
        }

        /// <summary>按相位保留/摘掉两栏；摘掉后 UpdateLayout 只跑剩下那一栏的容器生成</summary>
        private static void ApplyPhaseHost(Grid host, bool keepCanvas, bool keepTree)
        {
            foreach (var child in host.Children.OfType<FrameworkElement>().ToList())
            {
                bool isCanvas = child is FlowCanvasView;
                bool isTree = child is ProcessView;
                child.Visibility = (isCanvas && keepCanvas) || (isTree && keepTree)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 相位自检：把两栏的**实际**状态打出来（Visibility + 可视树节点数）。
        /// 没有这一行，"相位"只是我以为的开关——第一版就出现过 CollapseAllCommand 只作用于
        /// 当前挂接流程、其余流程照旧展开的情况，而 layout 数字看起来"合理"，差点被当成结论。
        /// </summary>
        private static void ReportPhaseState(string phase, Grid host)
        {
            var parts = new List<string>(2);
            foreach (var child in host.Children.OfType<FrameworkElement>())
            {
                string name = child is FlowCanvasView ? "canvas" : child is ProcessView ? "tree" : child.GetType().Name;
                parts.Add($"{name}={child.Visibility}/{CountVisuals(child)}");
            }
            Console.WriteLine($"[switchperf] phase_state phase={phase} {string.Join(" ", parts)}");
        }

        /// <summary>可视树节点数（相位自检用；上限 20000 防跑飞）</summary>
        private static int CountVisuals(DependencyObject root)
        {
            int n = 0;
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0 && n < 20000)
            {
                var d = stack.Pop();
                n++;
                int count = VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < count; i++)
                    stack.Push(VisualTreeHelper.GetChild(d, i));
            }
            return n;
        }

        /// <summary>
        /// 与真机同构：左流程栏 340 + 右画布自适应，装在一个**屏幕外真窗口**里。
        ///
        /// 为什么必须是真窗口（R53 同款理由）：没有 HWND 时 WPF 走离屏合成，渲染与
        /// CompositionTarget.Rendering 的时序跟真机不同——2026-10-23 实测同一份代码
        /// 「无窗口」与「屏幕外窗口」的 layout 段能差 2~3 倍，而差的正是本探针要量的那一段。
        /// 窗口置于 (-4000,-4000) 且不进任务栏，肉眼不可见，不影响交互坐标。
        /// </summary>
        private static (Grid Host, Window Window) BuildHost()
        {
            var grid = new Grid { Width = 1600, Height = 900 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var processView = new ProcessView();
            Grid.SetColumn(processView, 0);
            grid.Children.Add(processView);

            var canvasView = new FlowCanvasView();
            Grid.SetColumn(canvasView, 1);
            grid.Children.Add(canvasView);

            var window = new Window
            {
                Width = 1600,
                Height = 900,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -4000,
                Top = -4000,
                Background = Brushes.White,
                Content = grid,
            };

            window.Show();
            window.UpdateLayout();

            return (grid, window);
        }

        private static void PrepareResources(string repoRoot, bool withFluentTheme = false)
        {
            // 画布/流程栏的 StaticResource 全部来自 Colors.xaml（与 RenderProbe 同一口径）
            var colors = (ResourceDictionary)XamlReader.Parse(
                File.ReadAllText(Path.Combine(repoRoot, @"UI\Controls\Themes\Colors.xaml")));
            Application.Current.Resources.MergedDictionaries.Add(colors);
            // FA 字体的 pack URI 探针够不着：用系统自带 MDL2 兜底（码位只影响字形，不影响布局量级）
            Application.Current.Resources["Icon"] = new FontFamily("Segoe MDL2 Assets");

            // ProcessView 的展开箭头样式在 List.xaml、基础按钮在 Button.xaml（见 RenderProbe.RenderTree）
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Controls/Button.xaml"),
            });
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Controls/List.xaml"),
            });

            if (withFluentTheme)
            {
                // 真机 App.xaml:67/70 的顺序：先 UI/Themes/Generic，再 PresentationFramework.Fluent。
                // 只合这两个（Input.xaml 等不影响画布节点/树的模板）。
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml"),
                });
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml"),
                });
            }
        }

        private static IEnumerable<StepModel> CollectContainers(IEnumerable<StepModel> steps)
        {
            foreach (var step in steps)
            {
                if (step == null) continue;
                if (step is IContainerStep container && container.Children != null)
                {
                    yield return step;
                    foreach (var branch in container.Children)
                    {
                        if (branch?.Steps == null) continue;
                        foreach (var nested in CollectContainers(branch.Steps))
                            yield return nested;
                    }
                }
            }
        }

        private static int CountSteps(IEnumerable<StepModel> steps) => EnumerateSteps(steps).Count();

        private static IEnumerable<StepModel> EnumerateSteps(IEnumerable<StepModel>? steps)
        {
            if (steps == null) yield break;
            foreach (var step in steps)
            {
                if (step == null) continue;
                yield return step;
                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                    {
                        if (branch?.Steps == null) continue;
                        foreach (var nested in EnumerateSteps(branch.Steps))
                            yield return nested;
                    }
                }
            }
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0) return 0;
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }

        private static double P90(List<double> values)
        {
            if (values.Count == 0) return 0;
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[(int)Math.Min(sorted.Count - 1, Math.Ceiling(sorted.Count * 0.9) - 1)];
        }

        private static double Max(List<double> values) => values.Count == 0 ? 0 : values.Max();

        private static void CollectGarbage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static string? FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
                    return dir.FullName;
            }
            return null;
        }
    }
}
