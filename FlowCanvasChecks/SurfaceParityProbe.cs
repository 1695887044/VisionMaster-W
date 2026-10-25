using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using VisionMaster.Views;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 只读渲染层（<see cref="FlowCanvasSurface"/>）的几何一致性探针（C2 第 1 步验收）。
    ///
    /// 为什么需要它：自绘层的几何是**自己算**的（节点矩形、连线端点、外接框、适配视口），
    /// 一旦与画布 VM 的口径不一致，表现是"图看着差不多但框选错位、FitToScreen 差半屏"，
    /// 这类偏差肉眼很难发现。这里把两份几何逐项对照：
    ///   A 侧 = VM 的权威值（`Nodes` 的 Location + 固定尺寸 / `Links` 的 SourceAnchor/TargetAnchor）
    ///   B 侧 = 自绘层 `SetModel` 后快照出来的 `Nodes` / `Links`
    /// 并额外验证命中与适配视口的自洽性。
    ///
    /// 与 `--render` 的区别：那个量的是"真实 XAML 模板渲染出的像素"，本探针量的是"几何对不对"。
    /// 两者都要跑，但前者只有 14 张人看图，后者才能进 gate。
    /// </summary>
    internal static class SurfaceParityProbe
    {
        private static int _pass;
        private static int _fail;

        public static int Run(string? solutionPath, string? outDir)
        {
            int exit = 1;
            var thread = new Thread(() => exit = RunCore(solutionPath, outDir));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return exit;
        }

        private static int RunCore(string? solutionPath, string? outDir)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Console.WriteLine($"[surface] 图标字形面={FlowCanvasSurface.IconFontTypefaceCount}（0 = 字体没加载，FA 图标会是豆腐块）");

            var repoRoot = FindRepoRoot();
            if (repoRoot == null)
            {
                Console.WriteLine("[surface] FAIL: 找不到仓库根（AGENTS.md）");
                return 1;
            }

            var vmsPath = ResolveSolutionPath(repoRoot, solutionPath);
            if (vmsPath == null)
            {
                Console.WriteLine("[surface] FAIL: 未找到可用的 .vms");
                return 1;
            }
            Console.WriteLine("[surface] vms=" + vmsPath);

            var loaded = new SolutionService().LoadAsync(vmsPath).GetAwaiter().GetResult();
            if (!loaded.Success || loaded.Data == null)
            {
                Console.WriteLine("[surface] FAIL: 方案加载失败 " + loaded.Message);
                return 1;
            }

            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(loaded.Data);
            var canvasVm = new FlowCanvasViewModel(workspace, new StubPluginProvider());
            // 数据线默认关（与产品首次启动一致）；出图要看连线走线，所以这里显式打开。
            canvasVm.ShowDataLinks = true;

            var flows = loaded.Data.Flows.Where(f => f?.Steps != null && f.Steps.Count > 0)
                .OrderByDescending(f => CountSteps(f.Steps))
                .ToList();

            Console.WriteLine($"[surface] flows={flows.Count}");

            string renderDir = string.IsNullOrWhiteSpace(outDir)
                ? Path.Combine(repoRoot, "artifacts", "render", DateTime.Now.ToString("yyyyMMdd"), "surface")
                : outDir;
            Directory.CreateDirectory(renderDir);

            // 逐流程对照几何。全部流程都跑（这步很便宜：自绘层只建快照，不建元素）。
            foreach (var flow in flows)
            {
                workspace.SwitchFlow(flow);
                var surface = new FlowCanvasSurface();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                surface.SetModel(canvasVm);
                long build = sw.ElapsedMilliseconds;
                Console.WriteLine($"[surface] 快照「{flow.FlowName}」节点={surface.Nodes.Count} 连线={surface.Links.Count} 建快照={build}ms");

                CompareFlow(flow.FlowName, canvasVm, surface);
            }

            // 出图：挑 3 条"规模适中"的流程（100~800 个可见节点）。
            // 为什么不用最大的那条：极端流程的布局是单列（高 29 万像素），缩到 1600×900 后
            // 只剩一条 1 像素宽的竖线，人眼什么也看不出来，等于没出图。
            int rendered = 0;
            foreach (var flow in flows)
            {
                if (rendered >= 3) break;
                workspace.SwitchFlow(flow);
                var probe = new FlowCanvasSurface();
                probe.SetModel(canvasVm);
                if (probe.Nodes.Count < 8 || probe.Nodes.Count > 800) continue;

                RenderToPng(probe, 1600, 900,
                    Path.Combine(renderDir, "surface-" + Sanitize(flow.FlowName) + ".png"));

                // 诊断：连线端点与出入边（走线绕行判定依赖它们）
                if (flow.FlowName == "Test_Comm")
                {
                    for (int i = 0; i < canvasVm.Links.Count; i++)
                    {
                        var l = canvasVm.Links[i];
                        Console.WriteLine($"[link] {i} src={Fmt(l.SourceAnchor)} {l.SourcePosition} → tgt={Fmt(l.TargetAnchor)} {l.TargetPosition} order={l.IsOrderLink} label='{l.LinkLabel}'");
                    }
                }

                rendered++;
            }

            // 额外量一次"最大流程 + 适配全图"：这是 LOD（低缩放不画文字）的唯一战场——
            // 缩到能看全图时全部节点都在视口内，裁剪帮不上忙，只剩"不画文字"这一招。
            if (flows.Count > 0)
            {
                workspace.SwitchFlow(flows[0]);
                var big = new FlowCanvasSurface();
                big.SetModel(canvasVm);
                RenderToPng(big, 1600, 900,
                    Path.Combine(renderDir, "surface-" + Sanitize(flows[0].FlowName) + "-fit.png"),
                    fitToScreen: true);
            }

            // 数据线特写：对准线最密的一段，看绕行是否避开卡片、标签是否被遮挡。
            var commFlow = flows.FirstOrDefault(f => f.FlowName == "Test_Comm");
            if (commFlow != null)
            {
                workspace.SwitchFlow(commFlow);
                var linkView = new FlowCanvasSurface();
                linkView.SetModel(canvasVm);
                RenderToPng(linkView, 1200, 760,
                    Path.Combine(renderDir, "surface-Test_Comm-links.png"),
                    focusRect: new Rect(60, 1350, 460, 700));
            }

            // 容器特写：1:1 对准第一个容器框——这是"样式还原"最需要人眼过一遍的地方
            //（组头渐变/条件胶囊/折叠钮/分支泳道/数据线走线）。
            foreach (var flow in flows)
            {
                workspace.SwitchFlow(flow);
                var focus = new FlowCanvasSurface();
                focus.SetModel(canvasVm);

                var container = focus.Nodes.FirstOrDefault(n => n.Kind == FlowSurfaceKind.Container);
                if (container.Bounds.IsEmpty) continue;

                RenderToPng(focus, 1200, 760,
                    Path.Combine(renderDir, "surface-" + Sanitize(flow.FlowName) + "-container.png"),
                    focusRect: container.Bounds);
                break;
            }

            Console.WriteLine();
            Console.WriteLine($"========== 结果汇总 ==========");
            Console.WriteLine($"通过: {_pass}  失败: {_fail}");
            if (_fail > 0)
                Console.WriteLine($">>> 存在 {_fail} 项失败 <<<");

            return _fail == 0 ? 0 : 1;
        }

        private static void CompareFlow(string flowName, FlowCanvasViewModel vm, FlowCanvasSurface surface)
        {
            // 1. 节点数：自绘层只画"可见节点"（与 VM.Nodes 同口径）
            Check($"[{flowName}] 节点数 = VM.Nodes", surface.Nodes.Count == vm.Nodes.Count,
                $"surface={surface.Nodes.Count} vm={vm.Nodes.Count}");

            // 2. 逐节点矩形：A 侧由 VM 现算，B 侧取自绘层快照。
            //    配对键是**节点 VM 引用**（`FlowNodeRect.Model` 装的是 CanvasNodeViewModel，
            //    不是 StepModel —— 契约里写的是 object，这里按实现对齐）。
            var expected = new Dictionary<CanvasNodeViewModel, Rect>();
            foreach (var node in vm.Nodes)
            {
                if (node == null) continue;
                var w = node.Kind == CanvasNodeKind.Step ? FlowCanvasViewModel.NodeWidth : node.LaneWidth;
                var h = node.Kind == CanvasNodeKind.Step ? FlowCanvasViewModel.NodeHeight : node.LaneHeight;
                expected[node] = new Rect(node.Location.X, node.Location.Y, w, h);
            }

            int missing = 0, wrong = 0;
            string? firstWrong = null;
            foreach (var rect in surface.Nodes)
            {
                if (rect.Model is not CanvasNodeViewModel nodeVm) { missing++; continue; }
                if (!expected.TryGetValue(nodeVm, out var want)) { missing++; continue; }
                if (!NearlyEqual(rect.Bounds, want))
                {
                    wrong++;
                    firstWrong ??= $"kind={rect.Kind} got={Fmt(rect.Bounds)} want={Fmt(want)}";
                }
            }

            Check($"[{flowName}] 节点矩形逐项一致", wrong == 0 && missing == 0,
                $"不匹配={wrong} 未配对={missing} {(firstWrong == null ? "" : "首个：" + firstWrong)}");

            // 3. 连线：端点/进出边/三类标记逐项透传
            int linkWrong = 0;
            string? firstLinkWrong = null;
            int n = Math.Min(surface.Links.Count, vm.Links.Count);
            for (int i = 0; i < n; i++)
            {
                var got = surface.Links[i];
                var want = vm.Links[i];
                if (!NearlyEqual(got.Source, want.SourceAnchor)
                    || !NearlyEqual(got.Target, want.TargetAnchor)
                    || got.SourceSide != want.SourcePosition
                    || got.TargetSide != want.TargetPosition
                    || got.IsOrder != want.IsOrderLink
                    || got.IsIllegal != want.IsIllegal
                    || got.IsExecuted != want.IsExecuted)
                {
                    linkWrong++;
                    firstLinkWrong ??= $"#{i} order={got.IsOrder}/{want.IsOrderLink} "
                        + $"src={Fmt(got.Source)}/{Fmt(want.SourceAnchor)}";
                }
            }
            Check($"[{flowName}] 连线端点与标记一致", linkWrong == 0 && surface.Links.Count == vm.Links.Count,
                $"连线数 {surface.Links.Count}/{vm.Links.Count} 不匹配={linkWrong} "
                + $"{(firstLinkWrong == null ? "" : "首个：" + firstLinkWrong)}");

            // 4. 命中：每个 Step 节点中心必须命中"该节点或 Z 更高的覆盖物"（泳道/容器框会压在上面）
            int hitMiss = 0;
            string? firstMiss = null;
            foreach (var node in vm.Nodes)
            {
                if (node == null || node.Kind != CanvasNodeKind.Step) continue;
                var center = new Point(
                    node.Location.X + FlowCanvasViewModel.NodeWidth / 2,
                    node.Location.Y + FlowCanvasViewModel.NodeHeight / 2);

                var hit = surface.HitTest(center);
                if (hit == null || hit.Value.ZOrder < node.ZOrder)
                {
                    hitMiss++;
                    firstMiss ??= $"{node.Header} center={Fmt(center)} hit={(hit == null ? "null" : Fmt(hit.Value.Bounds))}";
                }
            }
            Check($"[{flowName}] 节点中心命中自身或更高层", hitMiss == 0,
                $"未命中={hitMiss} {(firstMiss == null ? "" : "首个：" + firstMiss)}");

            // 5. 适配视口：必须包住全部内容。
            //    这里把 minZoom 放到很小——本断言验的是**适配公式**；"夹取后允许装不下"
            //    是另一条语义（CanvasSurfaceGuards 的 SURF-3e 专门钉它），两者不能混。
            var viewport = new Size(1600, 900);
            var fit = surface.ComputeFitViewport(viewport, minZoom: 0.0001, maxZoom: 4.0);
            bool contains = !fit.IsEmpty && !surface.ContentBounds.IsEmpty
                && fit.Contains(surface.ContentBounds);
            Check($"[{flowName}] FitToScreen 包住内容", contains,
                $"fit={Fmt(fit)} content={Fmt(surface.ContentBounds)}");
        }

        private static void RenderToPng(
            FlowCanvasSurface surface,
            int width,
            int height,
            string path,
            bool fitToScreen = false,
            Rect? focusRect = null)
        {
            // 固定 1:1：出图是给人眼对照 XAML 模板用的，0.5 倍看不清字号与徽标对齐。
            // fitToScreen=true 时才走适配（专门量 LOD 那条路径）。
            const double zoom = 1.0;
            var content = surface.ContentBounds;

            if (fitToScreen)
            {
                var fit = surface.ComputeFitViewport(new Size(width, height), 0.001, 4.0);
                if (!fit.IsEmpty)
                {
                    double fitZoom = width / fit.Width;
                    surface.Zoom = fitZoom;
                    surface.Pan = new Vector(-fit.X * fitZoom, -fit.Y * fitZoom);
                }
            }
            else if (focusRect is { } focus && !focus.IsEmpty)
            {
                // 特写：把焦点矩形放在画面正中（留 40 边距）
                surface.Zoom = zoom;
                surface.Pan = new Vector(
                    width / 2.0 - (focus.X + focus.Width / 2) * zoom,
                    height / 2.0 - (focus.Y + focus.Height / 2) * zoom);
            }
            else
            {
                surface.Zoom = zoom;
                surface.Pan = content.IsEmpty
                    ? default
                    : new Vector(-(content.X - 20) * zoom, -(content.Y - 20) * zoom);
            }

            surface.Measure(new Size(width, height));
            surface.Arrange(new Rect(0, 0, width, height));
            surface.UpdateLayout();

            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var renderWatch = System.Diagnostics.Stopwatch.StartNew();
            bmp.Render(surface);
            long renderMs = renderWatch.ElapsedMilliseconds;

            // 第二遍（预热后）：首帧含 JIT、字形表与画笔初始化，量"稳态"必须看第二遍
            var bmp2 = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var warmWatch = System.Diagnostics.Stopwatch.StartNew();
            bmp2.Render(surface);
            long warmMs = warmWatch.ElapsedMilliseconds;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(path))
                encoder.Save(fs);

            Console.WriteLine($"[surface] 出图 {path} zoom={surface.Zoom:0.####} "
                + $"节点={surface.Nodes.Count} 实画={surface.LastDrawnNodeCount} "
                + $"首帧={renderMs}ms 稳态={warmMs}ms content={Fmt(content)}");
        }

        // ---------------------------------------------------------------- 工具

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) { _pass++; Console.WriteLine($"  [√通过] {name}  →  {detail}"); }
            else { _fail++; Console.WriteLine($"  [×失败] {name}  →  {detail}"); }
        }

        private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) <= 1e-6;

        private static bool NearlyEqual(Point a, Point b) => NearlyEqual(a.X, b.X) && NearlyEqual(a.Y, b.Y);

        private static bool NearlyEqual(Rect a, Rect b)
            => NearlyEqual(a.X, b.X) && NearlyEqual(a.Y, b.Y)
            && NearlyEqual(a.Width, b.Width) && NearlyEqual(a.Height, b.Height);

        private static string Fmt(Rect r)
            => r.IsEmpty ? "Empty" : $"({r.X:0.#},{r.Y:0.#},{r.Width:0.#}×{r.Height:0.#})";

        private static string Fmt(Point p) => $"({p.X:0.#},{p.Y:0.#})";

        private static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private static int CountSteps(IEnumerable<StepModel> steps)
        {
            int n = 0;
            foreach (var s in steps)
            {
                if (s == null) continue;
                n++;
                if (s is IContainerStep c && c.Children != null)
                    foreach (var b in c.Children)
                        if (b?.Steps != null) n += CountSteps(b.Steps);
            }
            return n;
        }

        private static string? FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
                    return dir.FullName;
            return null;
        }

        private static string? ResolveSolutionPath(string repoRoot, string? explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
                return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : null;

            var dir = Path.Combine(repoRoot, "解决方案");
            if (!Directory.Exists(dir)) return null;
            return Directory.GetFiles(dir, "*.vms", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.Length)
                .FirstOrDefault()?.FullName;
        }
    }
}
