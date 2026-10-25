using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using VisionMaster.Views;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 把现有 <see cref="FlowCanvasViewModel"/> 的节点/连线投影成自绘画布几何契约
    /// （<see cref="IFlowCanvasGeometry"/>），供"整块自绘"落地前先把几何语义钉住。
    ///
    /// 为什么需要这一层（而不是等 <c>FlowCanvasSurface</c> 落地再断言）：
    ///   · 自绘层的第一块地基是"图坐标里每个节点占哪块矩形、命中谁、内容外接框多大"，
    ///     这几条语义与 WPF 控件无关，现在就能用纯 VM 数据验证；
    ///   · 契约（FlowSurfaceKind / FlowNodeRect / FlowLinkLine / IFlowCanvasGeometry）已冻结，
    ///     本类只依赖接口，**不引用具体实现类**——自绘层换实现时本文件不用改。
    ///
    /// 映射口径（与 <see cref="FlowLayoutStore"/> 的度量常量同源，不许在这里写第二份数值）：
    ///   · Step       → <c>Location + (FlowCanvasViewModel.NodeWidth, NodeHeight)</c>
    ///   · Container  → <c>Location + (LaneWidth, LaneHeight)</c>（框体由子孙外接框算出，见 ComputeContainerGeometry）
    ///   · Lane       → <c>Location + (LaneWidth, LaneHeight)</c>
    ///   · ZOrder     → <see cref="CanvasNodeViewModel.ZOrder"/>（深度 × 3 + 身份偏移，框 3 / 泳道 4 / 内容 5）
    ///   · 连线端点   → <see cref="CanvasLinkViewModel.SourceAnchor"/> / <c>TargetAnchor</c>（已是图坐标）
    /// </summary>
    internal sealed class VmGeometryAdapter : IFlowCanvasGeometry
    {
        /// <summary>FitToScreen 四周留白（屏幕像素）：内容不贴视口边缘，与设计稿"留呼吸"口径一致</summary>
        public const double FitMargin = 24.0;

        private readonly FlowCanvasViewModel _vm;

        public VmGeometryAdapter(FlowCanvasViewModel vm)
            => _vm = vm ?? throw new ArgumentNullException(nameof(vm));

        public IReadOnlyList<FlowNodeRect> Nodes
        {
            get
            {
                var list = new List<FlowNodeRect>(_vm.Nodes.Count);
                foreach (var node in _vm.Nodes) list.Add(Project(node));
                return list;
            }
        }

        public IReadOnlyList<FlowLinkLine> Links
        {
            get
            {
                var list = new List<FlowLinkLine>(_vm.Links.Count);
                foreach (var link in _vm.Links)
                {
                    list.Add(new FlowLinkLine(
                        link.SourceAnchor,
                        link.TargetAnchor,
                        link.SourcePosition,
                        link.TargetPosition,
                        link.IsOrderLink,
                        link.IsIllegal,
                        link.IsExecuted));
                }
                return list;
            }
        }

        /// <summary>命中：包含该点的节点里取 ZOrder 最大者（泳道盖住容器框、内容盖住泳道）</summary>
        public FlowNodeRect? HitTest(Point graphPoint)
        {
            FlowNodeRect? best = null;
            foreach (var node in _vm.Nodes)
            {
                var projected = Project(node);
                if (!projected.Bounds.Contains(graphPoint)) continue;
                if (best == null || projected.ZOrder > best.Value.ZOrder) best = projected;
            }
            return best;
        }

        /// <summary>所有节点矩形的并集；空画布返回 <see cref="Rect.Empty"/></summary>
        public Rect ContentBounds
        {
            get
            {
                var bounds = Rect.Empty;
                foreach (var node in _vm.Nodes) bounds = Rect.Union(bounds, Project(node).Bounds);
                return bounds;
            }
        }

        /// <summary>
        /// FitToScreen 的图坐标可见区：按"视口四周各留 <see cref="FitMargin"/> 像素"求缩放，
        /// 夹到 [minZoom, maxZoom]，再以内容中心为中心反算可见矩形。
        ///
        /// 夹取的代价必须写清：zoom 被 minZoom 夹住时可见区装不下内容（超大图纸的必然结果，
        /// 不是缺陷）——[SURF-3e] 把这条钉成断言，避免以后有人"修"成静默改 zoom。
        /// </summary>
        public Rect ComputeFitViewport(Size viewport, double minZoom = 0.05, double maxZoom = 2.0)
        {
            var content = ContentBounds;
            if (content.IsEmpty || content.Width <= 0 || content.Height <= 0) return Rect.Empty;
            if (viewport.Width <= 0 || viewport.Height <= 0) return Rect.Empty;

            if (minZoom <= 0) minZoom = 0.05;
            if (maxZoom <= 0) maxZoom = 2.0;
            if (minZoom > maxZoom) (minZoom, maxZoom) = (maxZoom, minZoom);

            double availW = Math.Max(1.0, viewport.Width - FitMargin * 2);
            double availH = Math.Max(1.0, viewport.Height - FitMargin * 2);
            double zoom = Math.Clamp(Math.Min(availW / content.Width, availH / content.Height), minZoom, maxZoom);

            double visibleW = viewport.Width / zoom;
            double visibleH = viewport.Height / zoom;
            double centerX = content.X + content.Width / 2;
            double centerY = content.Y + content.Height / 2;
            return new Rect(centerX - visibleW / 2, centerY - visibleH / 2, visibleW, visibleH);
        }

        private static FlowNodeRect Project(CanvasNodeViewModel node)
            // 泳道的 Model 为 null（装饰节点没有图纸身份），契约的 object 位置照实传 null：
            // "这条矩形背后是哪个模型"对泳道本来就无解，编一个哨兵值反而会让调用方误判。
            => new(MapKind(node.Kind), BoundsOf(node), node.ZOrder, node.Model!);

        private static FlowSurfaceKind MapKind(CanvasNodeKind kind) => kind switch
        {
            CanvasNodeKind.Container => FlowSurfaceKind.Container,
            CanvasNodeKind.Lane => FlowSurfaceKind.Lane,
            _ => FlowSurfaceKind.Step,
        };

        private static Rect BoundsOf(CanvasNodeViewModel node) => node.Kind == CanvasNodeKind.Step
            ? new Rect(node.Location.X, node.Location.Y,
                FlowCanvasViewModel.NodeWidth, FlowCanvasViewModel.NodeHeight)
            : new Rect(node.Location.X, node.Location.Y, node.LaneWidth, node.LaneHeight);
    }

    /// <summary>
    /// 自绘画布几何契约的第 0 步断言（<c>--surface-guards</c> 路由）。
    ///
    /// 三条主断言，各带反向/边界（R38：不许留"永远为真"的绿）：
    ///   [SURF-1] 框选几何 —— 覆盖 2 个节点的矩形命中集合恰好是那 2 个；空矩形不命中；
    ///            HitTest 在"容器框 ∩ 泳道"处返回泳道（证明取 ZOrder 最大者，不是先到先得）。
    ///   [SURF-2] 多选批量位移 —— 两个 Step 精确偏移 (120,40) 并写回 FlowLayoutStore；
    ///            容器/泳道位移**不**写库（反向）；ContentBounds 向右外扩而底缘不动
    ///            （证明它是真并集，不是"整体外扩一个位移量"）。
    ///   [SURF-3] FitToScreen —— 视口包含内容四角、zoom 均匀且在 [minZoom,maxZoom]；
    ///            同一视口缩 10% 后不再包含（鉴别力）；minZoom 夹取装不下内容（夹取的代价）。
    ///
    /// 为什么自己起 STA 线程 + new Application：与 <see cref="FlowSwitchPerf"/> / RenderProbe 同一套
    /// 线程纪律（Application 必须在 STA 上建），且本模式是独立进程的一次性路由，
    /// 建完就退出，不会污染常规断言进程（常规 gate 里 **不** 走这条路由）。
    /// </summary>
    internal static class CanvasSurfaceGuards
    {
        private static int _pass, _fail;

        /// <summary>路由入口：0 = 全绿，1 = 有红（与 Program.Finish 的退出码口径一致）</summary>
        public static int Run()
        {
            int exitCode = 1;
            var thread = new Thread(() => { exitCode = RunCore(); });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();
            return exitCode;
        }

        private static int RunCore()
        {
            Application? app = null;
            try
            {
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                PrepareResources();

                Section("[SURF] 自绘画布几何契约（框选 / 批量位移 / FitToScreen）");
                SurfaceSelectionByRect();
                SurfaceBatchMove();
                SurfaceFitViewport();

                Console.WriteLine();
                Console.WriteLine("========== 结果汇总（SURF） ==========");
                Console.WriteLine($"通过: {_pass}  失败: {_fail}");
                Console.WriteLine(_fail == 0 ? ">>> 全部断言通过 <<<" : $">>> 存在 {_fail} 项失败 <<<");
                return _fail == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SURF] FAIL: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
            finally
            {
                // 退出期异常不许盖住断言结果（与 FlowSwitchPerf 同款收尾）
                try { app?.Shutdown(); } catch { }
            }
        }

        // ==================================================================
        //  [SURF-1] 框选几何：矩形命中集合 / 空矩形 / 最高 ZOrder
        // ==================================================================
        private static void SurfaceSelectionByRect()
        {
            Section("[SURF-1] 框选几何（矩形命中集合 / 空矩形 / HitTest 取最高 ZOrder）");

            try
            {
                var h = BuildFlow(out var a, out var b, out _, out var container);
                var geo = new VmGeometryAdapter(h.Canvas);

                // ---- 前置：投影面完整。少一种身份，"恰好命中"就退化成"部分覆盖"而看不出来 ----
                var counts = geo.Nodes.GroupBy(n => n.Kind).ToDictionary(g => g.Key, g => g.Count());
                Check("SURF-1a 节点投影：3 Step + 1 Container + 1 Lane = 5",
                    geo.Nodes.Count == 5
                    && KindCount(counts, FlowSurfaceKind.Step) == 3
                    && KindCount(counts, FlowSurfaceKind.Container) == 1
                    && KindCount(counts, FlowSurfaceKind.Lane) == 1,
                    string.Join(" ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));

                Check("SURF-1b 连线投影：根数一致，端点/进出边/三类标记原样透传",
                    geo.Links.Count == h.Canvas.Links.Count && geo.Links.Count > 0
                    && geo.Links.Zip(h.Canvas.Links, (p, v) =>
                            p.Source == v.SourceAnchor && p.Target == v.TargetAnchor
                            && p.SourceSide == v.SourcePosition && p.TargetSide == v.TargetPosition
                            && p.IsOrder == v.IsOrderLink && p.IsIllegal == v.IsIllegal
                            && p.IsExecuted == v.IsExecuted)
                        .All(same => same),
                    $"{geo.Links.Count} 条（画布 {h.Canvas.Links.Count} 条）");

                var nodeA = NodeOf(geo, a);
                var nodeB = NodeOf(geo, b);
                var nodeFrame = NodeOf(geo, container);
                var lane = geo.Nodes.Single(n => n.Kind == FlowSurfaceKind.Lane);

                // 前置：容器框整块在 B 底缘之下——否则"覆盖 A+B"的矩形会顺带压住容器，
                // 后面的"恰好两个"就变成假绿（框住三个也会被判红，但红的原因说不清）
                Check("SURF-1c 前置：容器框顶缘严格在 B 底缘之下（框选矩形不会误伤容器）",
                    nodeFrame.Bounds.Top > nodeB.Bounds.Bottom,
                    $"container.Top={nodeFrame.Bounds.Top:0.##} B.Bottom={nodeB.Bounds.Bottom:0.##}");

                // ---- ① 覆盖 A+B 的矩形：命中集合恰好 {A, B} ----
                var marquee = Rect.Union(nodeA.Bounds, nodeB.Bounds);
                var hits = geo.Nodes.Where(n => n.Bounds.IntersectsWith(marquee)).ToList();
                Check("SURF-1d 框选命中恰好 A/B 两个 Step（容器、泳道、分支内叶子都不进）",
                    hits.Count == 2
                    && hits.Any(x => ReferenceEquals(x.Model, a))
                    && hits.Any(x => ReferenceEquals(x.Model, b))
                    && hits.All(x => x.Kind == FlowSurfaceKind.Step),
                    $"marquee={Fmt(marquee)} 命中={hits.Count}: {string.Join(",", hits.Select(x => Label(x.Model)))}");

                // ---- ② 边界：空矩形不命中 ----
                var emptyHits = geo.Nodes.Where(n => n.Bounds.IntersectsWith(Rect.Empty)).ToList();
                Check("SURF-1e 空矩形（Rect.Empty）不命中任何节点",
                    emptyHits.Count == 0, $"命中={emptyHits.Count}");

                // ---- ③ 反向：同一矩形下移半格只剩 B（证明 1d 不是"恒等于全集"）----
                var lower = new Rect(marquee.X, marquee.Y + marquee.Height / 2, marquee.Width, marquee.Height / 2);
                var lowerHits = geo.Nodes.Where(n => n.Bounds.IntersectsWith(lower)).ToList();
                Check("SURF-1f 反向：矩形下移半格后命中集合 = {B}",
                    lowerHits.Count == 1 && ReferenceEquals(lowerHits[0].Model, b),
                    $"命中={lowerHits.Count}: {string.Join(",", lowerHits.Select(x => Label(x.Model)))}");

                // ---- ④ HitTest：取 ZOrder 最大者 ----
                // 泳道头带这一点同时落在容器框（ZOrder 3）与泳道（ZOrder 4）里 → 必须返回泳道
                var lanePoint = new Point(lane.Bounds.Left + lane.Bounds.Width / 2, lane.Bounds.Top + 6);
                var laneHit = geo.HitTest(lanePoint);
                Check("SURF-1g HitTest 在「容器框 ∩ 泳道」处返回泳道（ZOrder 4 > 3，不是先到先得）",
                    laneHit.HasValue && laneHit.Value.Kind == FlowSurfaceKind.Lane,
                    $"point={Fmt(lanePoint)} hit={KindText(laneHit)} z={laneHit?.ZOrder}");

                // 容器头带只在框里（在泳道之上），返回容器框
                var framePoint = new Point(nodeFrame.Bounds.Left + nodeFrame.Bounds.Width / 2, nodeFrame.Bounds.Top + 6);
                var frameHit = geo.HitTest(framePoint);
                Check("SURF-1h HitTest 在容器头带（泳道之上）返回容器框",
                    frameHit.HasValue && frameHit.Value.Kind == FlowSurfaceKind.Container,
                    $"point={Fmt(framePoint)} hit={KindText(frameHit)}");

                // 反向：画布外空白点 → null
                var outside = new Point(nodeFrame.Bounds.Right + 5000, nodeFrame.Bounds.Bottom + 5000);
                Check("SURF-1i 反向：画布外空白点 HitTest = null", geo.HitTest(outside) == null, Fmt(outside));
            }
            catch (Exception ex)
            {
                Check("SURF-1 全程未抛异常", false, ex.GetType().Name + ": " + ex.Message);
            }
        }

        // ==================================================================
        //  [SURF-2] 多选批量位移：精确偏移 / 只写 Step 坐标库 / ContentBounds 跟随
        // ==================================================================
        private static void SurfaceBatchMove()
        {
            Section("[SURF-2] 多选批量位移（坐标精确 / 只有 Step 写回坐标库 / ContentBounds 跟随）");

            try
            {
                var h = BuildFlow(out var a, out var b, out _, out var container);
                var geo = new VmGeometryAdapter(h.Canvas);
                var before = geo.ContentBounds;

                // 真机口径：框选只写 IsSelected，位移由批量拖动逐节点写 Location
                h.Node(a)!.IsSelected = true;
                h.Node(b)!.IsSelected = true;
                var selected = h.Canvas.Nodes
                    .Where(n => n.IsSelected && n.Kind == CanvasNodeKind.Step)
                    .ToList();
                Check("SURF-2a 选中集 = 2 个 Step", selected.Count == 2, $"{selected.Count} 个");

                var fromA = h.Node(a)!.Location;
                var fromB = h.Node(b)!.Location;
                const double dx = 120, dy = 40;
                foreach (var node in selected)
                    node.Location = new Point(node.Location.X + dx, node.Location.Y + dy);

                var toA = h.Node(a)!.Location;
                var toB = h.Node(b)!.Location;

                // ---- ① 位移精确 ----
                Check("SURF-2b 两个节点 Location 精确偏移 (120,40)",
                    Near(toA.X, fromA.X + dx) && Near(toA.Y, fromA.Y + dy)
                    && Near(toB.X, fromB.X + dx) && Near(toB.Y, fromB.Y + dy),
                    $"A {Fmt(fromA)}→{Fmt(toA)}  B {Fmt(fromB)}→{Fmt(toB)}");

                // ---- ② 坐标库：Step 写回（与 Location 同值，不是"写了个旧值"）----
                var storedA = h.Flow.Layout.Find(a.StepID);
                var storedB = h.Flow.Layout.Find(b.StepID);
                Check("SURF-2c Step 坐标写回 FlowLayoutStore（与 Location 同值）",
                    storedA != null && storedB != null
                    && Near(storedA.X, toA.X) && Near(storedA.Y, toA.Y)
                    && Near(storedB.X, toB.X) && Near(storedB.Y, toB.Y),
                    $"store A=({storedA?.X:0.##},{storedA?.Y:0.##}) B=({storedB?.X:0.##},{storedB?.Y:0.##})");

                // ---- ② 反向：容器框/泳道位移不写库 ----
                int storeCount = h.Flow.Layout.Count;
                var storedFrame = h.Flow.Layout.Find(container.StepID);
                double frameX = storedFrame?.X ?? double.NaN;
                double frameY = storedFrame?.Y ?? double.NaN;

                var frameNode = h.Node(container)!;
                var laneNode = h.Lanes().Single();
                var frameFrom = frameNode.Location;
                var laneFrom = laneNode.Location;

                // 模拟"自绘层把框也当可拖对象"的错误实现：直接挪框与泳道，坐标库必须纹丝不动
                frameNode.Location = new Point(frameFrom.X + 500, frameFrom.Y + 500);
                laneNode.Location = new Point(laneFrom.X + 500, laneFrom.Y + 500);
                var storedFrameAfter = h.Flow.Layout.Find(container.StepID);

                Check("SURF-2d 反向：容器框/泳道位移不写坐标库（只有 Kind==Step 写回）",
                    storedFrame != null && storedFrameAfter != null
                    && h.Flow.Layout.Count == storeCount
                    && Near(storedFrameAfter.X, frameX) && Near(storedFrameAfter.Y, frameY)
                    && h.Flow.Layout.Find(Guid.Empty) == null,
                    $"count {storeCount}→{h.Flow.Layout.Count} "
                    + $"框=({storedFrameAfter?.X:0.##},{storedFrameAfter?.Y:0.##}) 泳道 StepId=空");

                // 还原，避免污染后面的几何断言（Location 是纯 VM 属性，赋值即精确还原）
                frameNode.Location = frameFrom;
                laneNode.Location = laneFrom;

                // ---- ③ ContentBounds：包含全部节点 + 沿位移方向扩张 + 其余边不动 ----
                var after = geo.ContentBounds;
                var movedA = NodeOf(geo, a).Bounds;

                // 前半句是并集的自洽性（防"缓存不失效 / 只更新部分节点"），
                // 后半句才是鉴别点：A 的新盒子必须**逃出**旧外接框——否则"包含"恒真，说明 bounds 是陈的
                Check("SURF-2e ContentBounds 包含全部节点，且位移后的 A 已逃出旧外接框（不是陈的缓存）",
                    geo.Nodes.All(n => Inside(after, n.Bounds, 1e-6))
                    && !Inside(before, movedA, 1e-6),
                    $"before={Fmt(before)} after={Fmt(after)} movedA={Fmt(movedA)}");

                Check("SURF-2f ContentBounds 向右扩张（A/B 成为最右，右缘严格外扩）",
                    after.Right > before.Right + 1e-9,
                    $"right {before.Right:0.##}→{after.Right:0.##}");

                // 三边各自按"谁是极值"判定，证明它是逐节点真并集，不是"整体外扩一个位移量"：
                //   · 顶缘 = A 的顶（A 是最顶节点）→ 跟着 +40；
                //   · 左缘 = 容器框的左（框比盒子宽）→ 不动；
                //   · 底缘 = 容器框的底（框比盒子高，且 A/B 下移 40 仍在框顶之上）→ 不动。
                // 若实现按"整体外扩"算，底缘会跟着 +40、顶缘反而不动，两条都会红。
                Check("SURF-2g 反向：顶缘 +40（跟着最顶节点）、左缘/底缘不动（框仍是极值）",
                    Near(after.Top, before.Top + dy)
                    && Near(after.Left, before.Left)
                    && Near(after.Bottom, before.Bottom),
                    $"top {before.Top:0.##}→{after.Top:0.##} left {before.Left:0.##}→{after.Left:0.##} "
                    + $"bottom {before.Bottom:0.##}→{after.Bottom:0.##}");
            }
            catch (Exception ex)
            {
                Check("SURF-2 全程未抛异常", false, ex.GetType().Name + ": " + ex.Message);
            }
        }

        // ==================================================================
        //  [SURF-3] FitToScreen：覆盖全量 / zoom 夹取 / 缩 10% 失去包含
        // ==================================================================
        private static void SurfaceFitViewport()
        {
            Section("[SURF-3] FitToScreen 覆盖全量（四角 / zoom 夹取 / 缩 10% 失去包含）");

            try
            {
                var h = BuildTallFlow();
                var geo = new VmGeometryAdapter(h.Canvas);
                var content = geo.ContentBounds;
                var viewport = new Size(1600, 900);
                var fit = geo.ComputeFitViewport(viewport);

                double zoomX = fit.IsEmpty ? 0 : viewport.Width / fit.Width;
                double zoomY = fit.IsEmpty ? 0 : viewport.Height / fit.Height;

                // 前置：zoom 不能顶到 maxZoom。顶到上限时视口远大于内容，"缩 10% 失去包含"会恒假，
                // 反向断言就失去鉴别力（这条前置自己也会红，不会静默跳过）
                Check("SURF-3a 前置：内容非空、fit 有解、zoom 未顶到 maxZoom",
                    !content.IsEmpty && content.Width > 0 && content.Height > 0 && !fit.IsEmpty
                    && zoomX > 0.05 + 1e-9 && zoomX < 2.0 - 1e-9,
                    $"content={Fmt(content)} fit={Fmt(fit)} zoom={zoomX:0.######}");

                // ---- ① 视口包含内容四角 ----
                var escaped = Corners(content).Count(p => !ContainsPoint(fit, p));
                Check("SURF-3b 视口包含 ContentBounds 的每一个角（四个角全部在内）",
                    escaped == 0, $"逃出={escaped}/4 fit={Fmt(fit)} content={Fmt(content)}");

                // ---- ② zoom 均匀且在 [minZoom, maxZoom] 内 ----
                double availW = viewport.Width - VmGeometryAdapter.FitMargin * 2;
                double availH = viewport.Height - VmGeometryAdapter.FitMargin * 2;
                double expected = Math.Clamp(Math.Min(availW / content.Width, availH / content.Height), 0.05, 2.0);
                Check("SURF-3c zoom 均匀（X/Y 同比）、落在 [0.05,2.0] 内，且等于 (视口−48px)/内容 的较小比",
                    Near(zoomX, zoomY, 1e-9)
                    && zoomX >= 0.05 - 1e-9 && zoomX <= 2.0 + 1e-9
                    && Near(zoomX, expected, 1e-9),
                    $"zoomX={zoomX:0.######} zoomY={zoomY:0.######} expected={expected:0.######}");

                // ---- ③ 反向：同一视口缩 10%（绕中心）后不再包含内容 ----
                var shrunk = ShrinkAboutCenter(fit, 0.9);
                int escapedAfterShrink = Corners(content).Count(p => !ContainsPoint(shrunk, p));
                Check("SURF-3d 反向：同一视口缩 10% 后不再包含 ContentBounds（本组断言有鉴别力）",
                    escapedAfterShrink > 0,
                    $"缩后逃出={escapedAfterShrink}/4 shrunk={Fmt(shrunk)} content={Fmt(content)}");

                // ---- 边界：zoom 被 minZoom 夹住时视口装不下内容（夹取的必然代价，不是缺陷）----
                var clamped = geo.ComputeFitViewport(viewport, minZoom: 1.5, maxZoom: 3.0);
                double clampedZoom = clamped.IsEmpty ? 0 : viewport.Width / clamped.Width;
                int escapedClamped = Corners(content).Count(p => !ContainsPoint(clamped, p));
                Check("SURF-3e 边界：minZoom=1.5 时 zoom 夹到 1.5 → 视口装不下内容（夹取的代价，不是缺陷）",
                    Near(clampedZoom, 1.5) && escapedClamped > 0,
                    $"zoom={clampedZoom:0.######} 装下的角={4 - escapedClamped}/4");

                // ---- 边界：空画布 / 零尺寸视口 ----
                var bare = new VmGeometryAdapter(
                    new FlowCanvasViewModel(new WorkspaceContext(), new StubPluginProvider()));
                Check("SURF-3f 边界：空画布 ContentBounds=Empty、ComputeFitViewport=Empty、HitTest=null（不抛）",
                    bare.Nodes.Count == 0
                    && bare.ContentBounds == Rect.Empty
                    && bare.ComputeFitViewport(viewport) == Rect.Empty
                    && geo.ComputeFitViewport(new Size(0, 0)) == Rect.Empty
                    && bare.HitTest(new Point(0, 0)) == null,
                    $"nodes={bare.Nodes.Count} bounds={(bare.ContentBounds == Rect.Empty ? "Empty" : Fmt(bare.ContentBounds))}");
            }
            catch (Exception ex)
            {
                Check("SURF-3 全程未抛异常", false, ex.GetType().Name + ": " + ex.Message);
            }
        }

        // ==================================================================
        //  夹具：真实构造的流程（Harness 走 public 面，不碰任何 internal）
        // ==================================================================

        /// <summary>
        /// SURF-1/2 的图式：A → B → For（循环体里 1 个叶子）。
        /// 投影面 = 3 个 Step（A/B/分支内）+ 1 个 Container + 1 条 Lane。
        /// 顶层纵向排布由 AutoLayout 决定（A 行 0、B 行 1、容器行 2），本文件不写死坐标。
        /// </summary>
        private static Harness BuildFlow(
            out ActionStep a, out ActionStep b, out ActionStep inner, out ForStep container)
        {
            var h = new Harness();
            a = h.Leaf("A");
            b = h.Leaf("B");
            container = h.For("循环");
            inner = h.Leaf("分支内");
            container.Children[0].Steps.Add(inner);
            h.Add(a);
            h.Add(b);
            h.Add(container);
            _ = h.Canvas;   // 触发 AutoLayout + 渲染（坐标落到 FlowLayoutStore）
            return h;
        }

        /// <summary>
        /// SURF-3 的图式：8 个顶层叶子 + 1 个带循环体的容器。
        /// 为什么要这么多行：内容必须**高于**视口可用高，zoom 才不会顶到 maxZoom——
        /// 否则"缩 10% 失去包含"恒假，反向断言等于没写（R38）。
        /// </summary>
        private static Harness BuildTallFlow()
        {
            var h = new Harness();
            for (int i = 0; i < 8; i++) h.Add(h.Leaf("S" + i));
            var container = h.For("循环");
            container.Children[0].Steps.Add(h.Leaf("分支内"));
            h.Add(container);
            _ = h.Canvas;
            return h;
        }

        // ==================================================================
        //  断言骨架 / 辅助
        // ==================================================================

        /// <summary>
        /// 与 <see cref="Program.Check"/> 同格式输出，另外自己记一份计数——
        /// 本路由是独立进程一次性入口，Program._pass/_fail 是 private 且 Finish() 不会跑到，
        /// 退出码只能由本类自己算（0 = 全绿 / 1 = 有红）。
        /// </summary>
        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            Program.Check(name, ok, detail);
        }

        private static FlowNodeRect NodeOf(IFlowCanvasGeometry geo, object model)
            => geo.Nodes.First(n => ReferenceEquals(n.Model, model));

        private static int KindCount(Dictionary<FlowSurfaceKind, int> counts, FlowSurfaceKind kind)
            => counts.TryGetValue(kind, out var n) ? n : 0;

        private static string KindText(FlowNodeRect? rect) => rect.HasValue ? rect.Value.Kind.ToString() : "null";

        private static string Label(object? model) => model switch
        {
            StepModel step => step.StepName,
            null => "泳道",
            _ => model.GetType().Name,
        };

        private static IEnumerable<Point> Corners(Rect rect)
            => new[] { rect.TopLeft, rect.TopRight, rect.BottomLeft, rect.BottomRight };

        private static bool ContainsPoint(Rect outer, Point p, double tol = 1e-6)
            => p.X >= outer.Left - tol && p.X <= outer.Right + tol
               && p.Y >= outer.Top - tol && p.Y <= outer.Bottom + tol;

        private static Rect ShrinkAboutCenter(Rect rect, double factor)
        {
            double w = rect.Width * factor;
            double h = rect.Height * factor;
            double cx = rect.Left + rect.Width / 2;
            double cy = rect.Top + rect.Height / 2;
            return new Rect(cx - w / 2, cy - h / 2, w, h);
        }

        private static bool Near(double x, double y, double tol = 1e-9) => Math.Abs(x - y) <= tol;

        private static string Fmt(Rect r) => r.IsEmpty
            ? "Empty"
            : $"({r.Left:0.##},{r.Top:0.##},{r.Width:0.##},{r.Height:0.##})";

        private static string Fmt(Point p) => $"({p.X:0.##},{p.Y:0.##})";

        // ==================================================================
        //  环境准备：与真机同源的画布资源（最小版，不复制 FlowSwitchPerf 那一坨）
        // ==================================================================

        /// <summary>
        /// 只合并 <c>UI\Controls\Themes\Colors.xaml</c>（画布第一层令牌）。
        ///
        /// 失败不判红：本组三条断言只碰纯 VM 几何，不解析任何 XAML 资源；
        /// 但必须把失败原因打在 stdout（静默吞掉会让"以后自绘层要资源时才发现探针环境是假的"）。
        /// </summary>
        private static void PrepareResources()
        {
            try
            {
                var repoRoot = FindRepoRoot();
                if (repoRoot == null)
                {
                    Console.WriteLine("[SURF-ENV] 未找到仓库根（AGENTS.md）：跳过 Colors.xaml 合并（几何断言不依赖主题资源）");
                    return;
                }

                var colorsPath = Path.Combine(repoRoot, @"UI\Controls\Themes\Colors.xaml");
                if (!File.Exists(colorsPath))
                {
                    Console.WriteLine("[SURF-ENV] Colors.xaml 不存在：" + colorsPath + "（几何断言不依赖主题资源）");
                    return;
                }

                var colors = (ResourceDictionary)XamlReader.Parse(File.ReadAllText(colorsPath));
                Application.Current.Resources.MergedDictionaries.Add(colors);
                // FA 字体的 pack URI 探针够不着：用系统自带 MDL2 兜底（与 RenderProbe 同一口径）
                Application.Current.Resources["Icon"] = new FontFamily("Segoe MDL2 Assets");
                Console.WriteLine("[SURF-ENV] Colors.xaml 已合并（与真机同源的画布令牌）");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SURF-ENV] Colors.xaml 合并失败：" + ex.GetType().Name + ": " + ex.Message
                    + "（几何断言不依赖主题资源，继续）");
            }
        }

        /// <summary>从 exe 目录向上找 AGENTS.md（与 FlowSwitchPerf/RenderProbe 同一套定位口径）</summary>
        private static string? FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) return dir.FullName;
            }
            return null;
        }
    }
}
