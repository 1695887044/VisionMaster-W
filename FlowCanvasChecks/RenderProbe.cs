using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using Nodify;
using Prism.Dialogs;
using VisionMaster;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using VisionMaster.ViewModels.DialogViewModels;
using VisionMaster.Views;
using VisionMaster.Views.DialogViews;
// 隐式全局 using 里有 System.Linq，它也有个 ParallelExecutionMode（本文件要写并行组的模式，取模型侧）
using ParallelExecutionMode = VisionMaster.Models.ParallelExecutionMode;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 流程画布渲染探针：--render 模式下起 STA 线程，真实实例化 FlowCanvasView
    ///（App 级资源用 Colors.xaml 文本 + Icon 字体文件兜底），把两个典型图式渲染成 PNG。
    ///
    /// 为什么需要它：断言工程是 headless 的，逻辑断言绿 ≠ 视觉过关——2026-10-07 用户看真机截图
    /// 直接指出"连线太丑/意义不明"。视觉整改的前后对比必须出图，PNG 路径打印到 stdout 供主会话引用。
    ///
    /// 用法：dotnet run --no-build -- --render [输出目录]
    /// 输出：flowcanvas-render-A.png（纵向两步 + 数据线/顺序链，复刻真机反馈场景；**默认态**只画顺序链，
    ///                                     数据线默认隐藏——用户口径"端口之间的连线默认不显示"）
    ///       flowcanvas-render-A2-datalinks.png（同 A 图式、显式打开数据线——开关的视觉对照）
    ///       flowcanvas-render-B.png（If 容器 + 双分支 + 跨容器连线 + 折叠态）
    ///       flowcanvas-render-C-expand/-collapse.png（多条件多嵌套）
    ///       flowcanvas-render-D-10levels.png（十层嵌套压力图式）
    ///       flowcanvas-render-E-parallel.png（画布上的并行分组图式）
    ///       process-tree-render-F.png（左侧流程栏步骤树：并行分组/If 容器/分支胶囊，2026-10-09 类型全名事故对照；
    ///                                     兼自检分支卡片左键拦截与分支胶囊右键菜单——结构动作从面板下沉到右键）
    ///       parallel-group-params-render-G.png（并行分组参数面板：FlatPropertyGrid 标准属性面板本体，
    ///                                     2026-10-09 专属视图收编后改出属性网格；弹窗壳是 Popup 不出图）
    ///       flowcanvas-render-H-parallel-nested.png（并行分组 4 分支、分支 1 里嵌套 If/For/While 的真机图式，
    ///                                               2026-10-09 "流程画布太乱"互压缺陷的修复对照）
    /// </summary>
    internal static class RenderProbe
    {
        public static void Run(string outDir)
        {
            var thread = new Thread(() => RenderBoth(outDir));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        private static void RenderBoth(string outDir)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            // FlowCanvasView 的 StaticResource 在 InitializeComponent 时解析，资源必须先行：
            // Colors.xaml 文本直载 + Icon 字体按文件路径补（App.xaml 的 pack URI 探针够不着）
            var colors = (ResourceDictionary)XamlReader.Parse(
                File.ReadAllText(@"D:\C#\VM\UI\Controls\Themes\Colors.xaml"));
            app.Resources.MergedDictionaries.Add(colors);
            // FA 字体的 pack URI 探针够不着：用系统自带的 MDL2 兜底 + MDL2 码位字形
            app.Resources["Icon"] = new FontFamily("Segoe MDL2 Assets");

            Directory.CreateDirectory(outDir);

            RenderScenarioA(Path.Combine(outDir, "flowcanvas-render-A.png"));
            RenderScenarioA2(Path.Combine(outDir, "flowcanvas-render-A2-datalinks.png"));
            RenderScenarioB(Path.Combine(outDir, "flowcanvas-render-B.png"));
            RenderScenarioC(Path.Combine(outDir, "flowcanvas-render-C-expand.png"), collapseDeepIf: false);
            RenderScenarioC(Path.Combine(outDir, "flowcanvas-render-C-collapse.png"), collapseDeepIf: true);
            RenderScenarioD(Path.Combine(outDir, "flowcanvas-render-D-10levels.png"));
            RenderScenarioE(Path.Combine(outDir, "flowcanvas-render-E-parallel.png"));
            RenderScenarioF(Path.Combine(outDir, "process-tree-render-F.png"));
            RenderScenarioG(Path.Combine(outDir, "parallel-group-params-render-G.png"));
            RenderScenarioH(Path.Combine(outDir, "flowcanvas-render-H-parallel-nested.png"));
            RenderScenarioI(Path.Combine(outDir, "variable-manager-render-I.png"));
            RenderScenarioJ(Path.Combine(outDir, "variable-add-popup-render-J.png"));
            RenderScenarioK(Path.Combine(outDir, "array-editor-render-K.png"), withError: false);
            RenderScenarioK(Path.Combine(outDir, "array-editor-error-render-K2.png"), withError: true);
            RenderScenarioKNetwork(Path.Combine(outDir, "array-editor-network-render-K3.png"));

            // 性能量化（2026-10-12 评审清单第三批）：压力图式 300/600 节点，只量化不优化——
            // RebuildInPlace / 整帧毫秒打 [perf] 行（3 次取中位数），结论供评审拍板"第一刀接不接局部刷新"
            RenderScenarioPerf(Path.Combine(outDir, "flowcanvas-render-perf-300.png"), 300);
            RenderScenarioPerf(Path.Combine(outDir, "flowcanvas-render-perf-600.png"), 600);

            app.Shutdown();
        }

        // ==================================================================
        //  尺寸实量子模式（--measure-canvas）
        //
        //  为什么单独开一个进程模式：断言宿主主进程**不能** `new Application`（实测两个后果：
        //  预览帧投射改走 UI 线程投递分支永不落地 / 退出时挂住把 exe 锁住，见 BlobDetectChecks
        //  类注释与 docs/code-changes/2026-09-25-冒烟命令超时排障与规避.md）。所以
        //  CanvasDesignChecks 用子进程跑这里，父进程只解析 stdout 的 [measure] 行。
        //
        //  量的是"设计稿定稿的可见口径"：卡片 156×58、标题行真有宽高、小地图 180×118 且
        //  关掉开关后真变 Collapsed（触发器生效，不只是绑定值变了）。
        // ==================================================================

        /// <summary>入口：起 STA 线程跑测量（与 Run 同一套线程纪律）。</summary>
        public static void RunMeasure()
        {
            var thread = new Thread(MeasureCanvas);
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        private static void MeasureCanvas()
        {
            Application? app = null;
            try
            {
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

                string? repoRoot = FindRepoRoot();
                if (repoRoot == null)
                {
                    Console.WriteLine("[measure] FAIL: 找不到仓库根（AGENTS.md）");
                    Environment.ExitCode = 1;
                    return;
                }

                // 与 RenderBoth 同一份资源准备：Colors.xaml 文本直载 + Icon 字体按系统字体兜底
                var colors = (ResourceDictionary)XamlReader.Parse(
                    File.ReadAllText(Path.Combine(repoRoot, @"UI\Controls\Themes\Colors.xaml")));
                app.Resources.MergedDictionaries.Add(colors);
                app.Resources["Icon"] = new FontFamily("Segoe MDL2 Assets");

                var h = new Harness();
                h.Flow.FlowName = "尺寸探针";
                var leaf = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "采集_0");
                h.Add(leaf);

                // FLC-21 素材：单泳道长条件 While——条件文本远宽于组头，用来量"条件列会不会
                // 把右侧统计/状态点/折叠钮挤出组头"。循环体给一个算子，泳道才拿到 MinLaneWidth 的实宽。
                var longWhile = new WhileStep("\uE700", "桩While", "VM.CanvasStub.While", "长条件循环");
                longWhile.Children[0].Expression =
                    "Score > 80 AND Recipe == \"A\" AND Station == 3 AND Camera.Ready == true AND LightLevel > 120";
                longWhile.Children[0].Steps.Add(
                    new ActionStep("\uE700", "循环体算子", StubPluginProvider.LeafPlugin, "循环体_0"));
                h.Add(longWhile);

                var canvas = new FlowCanvasViewModel(h.Workspace, new StubPluginProvider());
                WireViewModelFactory(canvas);
                canvas.ShowMinimap = true;   // 默认关闭 → 先打开才量得到 180×118

                var view = new FlowCanvasView();
                var host = new Border
                {
                    Width = 900,
                    Height = 600,
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                    Child = view,
                };
                host.Measure(new Size(900, 600));
                host.Arrange(new Rect(0, 0, 900, 600));
                host.UpdateLayout();
                PumpFrames(10);

                // ---- 卡片与标题行 ----
                // 容器生成跨多帧（Nodify 的 ItemContainer + 模板展开）；慢机上 10 帧可能还没生成，
                // 这里补一段有界等待——等到就量，等不到照旧判 FAIL（不是跳过）。
                var header = WaitForHeader(view, TimeSpan.FromSeconds(3));
                var card = FindCardAncestor(header);

                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "[measure] card={0:0.#}x{1:0.#}",
                    card?.ActualWidth ?? double.NaN,
                    card?.ActualHeight ?? double.NaN));

                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "[measure] header={0}|w={1:0.#}|h={2:0.#}",
                    header?.Text ?? "",
                    header?.ActualWidth ?? double.NaN,
                    header?.ActualHeight ?? double.NaN));

                // ---- 工具箱拖入的接线（2026-10-21 拖影残留事故）----
                // gong 的拖影 Popup 只在它自己的 DropTargetOnDrop 里关闭：画布若只挂裸 Drop/DragOver
                // 而没声明 IsDropTarget，成功落子后胶囊行会永久停在落点。这里钉住"编辑器是拖放目标
                // 且挂了 IDropTarget"，接线一被改回去就红。
                var editor = FindDescendant<NodifyEditor>(view);
                bool isDropTarget = editor != null
                    && GongSolutions.Wpf.DragDrop.DragDrop.GetIsDropTarget(editor);
                bool hasDropHandler = editor != null
                    && GongSolutions.Wpf.DragDrop.DragDrop.GetDropHandler(editor)
                        is GongSolutions.Wpf.DragDrop.IDropTarget;
                Console.WriteLine(
                    $"[measure] dragdrop=IsDropTarget={isDropTarget}|IDropTarget={hasDropHandler}");

                // ---- 小地图：打开态量尺寸，关掉后看 Visibility ----
                var minimap = FindDescendant<Minimap>(view);
                var minimapBorder = minimap == null ? null : VisualTreeHelper.GetParent(minimap) as Border;

                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "[measure] minimap={0:0.#}x{1:0.#}",
                    minimapBorder?.ActualWidth ?? double.NaN,
                    minimapBorder?.ActualHeight ?? double.NaN));

                canvas.ShowMinimap = false;
                host.UpdateLayout();
                PumpFrames(4);

                Console.WriteLine($"[measure] minimap-hidden={minimapBorder?.Visibility}");

                if (card == null || header == null || minimapBorder == null)
                {
                    Console.WriteLine("[measure] FAIL: 模板部件没找到（NodeHeaderText / 卡片 Grid / 小地图 Border）");
                    Environment.ExitCode = 1;
                    return;
                }

                // ---- 容器组头（单泳道长条件 While）：四件套边界（FLC-21）----
                MeasureContainerHeader(host, view, longWhile);

                // ---- 禁用态整节点降透明（FLC-22）：模板根 Grid 的 Opacity 真变成 0.45 ----
                MeasureDisabledOpacity(host, view, leaf, longWhile);

                Console.WriteLine("[measure] ok");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[measure] FAIL: {ex.GetType().Name}: {ex.Message}");

                // XamlParseException 的根因全在 InnerException 里（外层只说"设置属性时引发异常"），
                // 不把这条链打出来就只能靠猜
                for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                    Console.WriteLine($"[measure]   inner: {inner.GetType().Name}: {inner.Message}");

                Environment.ExitCode = 1;
            }
            finally
            {
                try { app?.Shutdown(); } catch { }
            }
        }

        /// <summary>按 x:Name 找模板里的命名元素（DataTemplate 的 x:Name 会落在 FrameworkElement.Name 上）</summary>
        private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed && string.Equals(typed.Name, name, StringComparison.Ordinal))
                    return typed;
                var found = FindByName<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>有界等待标题行出现：容器生成跨多帧，慢机上固定帧数可能不够</summary>
        private static TextBlock? WaitForHeader(DependencyObject root, TimeSpan timeout)
        {
            var header = FindByName<TextBlock>(root, "NodeHeaderText");
            var deadline = DateTime.UtcNow + timeout;
            while (header == null && DateTime.UtcNow < deadline)
            {
                PumpFrames(2);
                header = FindByName<TextBlock>(root, "NodeHeaderText");
            }
            return header;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) return hit;
                var found = FindDescendant<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>元素相对某个祖先的布局矩形（DIP；TransformBounds 一并带上编辑器的缩放/平移）</summary>
        private static Rect BoundsRelativeTo(FrameworkElement element, Visual ancestor)
            => element.TransformToAncestor(ancestor)
                .TransformBounds(new Rect(new Point(0, 0), element.RenderSize));

        /// <summary>机器可读矩形行：x/y/w/h/right/bottom 一次给全（父进程只做算术，不猜）</summary>
        private static string RectLine(string prefix, Rect r)
            => string.Format(
                CultureInfo.InvariantCulture,
                "{0}x={1:0.#}|y={2:0.#}|w={3:0.#}|h={4:0.#}|right={5:0.#}|bottom={6:0.#}",
                prefix, r.X, r.Y, r.Width, r.Height, r.Right, r.Bottom);

        /// <summary>容器盒 Grid：组头的祖先里，第一个直接子级含 ContainerCollapseToggle 的 Grid
        /// （折叠钮是 ContainerFrame 的兄弟，不在组头里——组头不是它的祖先）</summary>
        private static Grid? FindContainerBoxGrid(DependencyObject header)
        {
            for (var cur = VisualTreeHelper.GetParent(header); cur != null; cur = VisualTreeHelper.GetParent(cur))
            {
                if (cur is Grid grid && FindDirectChildByName(grid, "ContainerCollapseToggle") != null)
                    return grid;
            }
            return null;
        }

        private static FrameworkElement? FindDirectChildByName(DependencyObject parent, string name)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(parent, i) is FrameworkElement fe
                    && string.Equals(fe.Name, name, StringComparison.Ordinal))
                {
                    return fe;
                }
            }
            return null;
        }

        /// <summary>
        /// FLC-21：单泳道长条件 While 的组头边界。
        /// 量的四件套都是模板里的 x:Name 部件：条件胶囊（ContainerCondBorder/ContainerCondText）、
        /// 右侧统计（ContainerMetaLabel）、状态点（ContainerStatusDot）、折叠钮（ContainerCollapseToggle）。
        /// 折叠钮不在组头 Border 里（它是 ContainerFrame 的兄弟），所以它相对"容器盒 Grid"量，
        /// 组头也按同一个祖先量，父进程才能直接比 right/bottom。
        /// 认人按 DataContext 的模型：同场景若再加容器，也不会量到别人的组头。
        /// </summary>
        private static void MeasureContainerHeader(FrameworkElement host, DependencyObject view, StepModel container)
        {
            var header = WaitForNamedRootForModel(view, "ContainerHeader", container, TimeSpan.FromSeconds(3));
            if (header == null)
            {
                Console.WriteLine("[measure] while-FAIL: 找不到长条件 While 的 ContainerHeader（模板没生成？）");
                return;
            }

            var box = FindContainerBoxGrid(header);
            var condBorder = FindByName<Border>(header, "ContainerCondBorder");
            var condText = FindByName<TextBlock>(header, "ContainerCondText");
            var meta = FindByName<TextBlock>(header, "ContainerMetaLabel");
            var dot = FindByName<System.Windows.Shapes.Ellipse>(header, "ContainerStatusDot");
            var toggle = box == null ? null : FindDirectChildByName(box, "ContainerCollapseToggle");

            if (box == null || condBorder == null || condText == null
                || meta == null || dot == null || toggle == null)
            {
                Console.WriteLine(
                    "[measure] while-FAIL: 容器组头部件没找齐"
                    + $"（box={box != null} condBorder={condBorder != null} condText={condText != null}"
                    + $" meta={meta != null} dot={dot != null} toggle={toggle != null}）");
                return;
            }

            host.UpdateLayout();
            PumpFrames(4);

            Console.WriteLine(RectLine("[measure] while-header=", BoundsRelativeTo(header, box)));
            Console.WriteLine(RectLine("[measure] while-cond=", BoundsRelativeTo(condBorder, header)));
            Console.WriteLine(RectLine("[measure] while-condtext=", BoundsRelativeTo(condText, header)));
            Console.WriteLine(RectLine("[measure] while-meta=", BoundsRelativeTo(meta, header)));
            Console.WriteLine(RectLine("[measure] while-dot=", BoundsRelativeTo(dot, header)));
            Console.WriteLine(RectLine("[measure] while-toggle=", BoundsRelativeTo(toggle, box)));
        }

        /// <summary>
        /// FLC-22：禁用态必须整节点降透明（模板根 Grid 的 Opacity），不是只压卡片本体。
        /// 量两处根 Grid：StepNodeRoot（步骤模板根）与 ContainerNodeRoot（容器模板根）。
        /// </summary>
        private static void MeasureDisabledOpacity(
            FrameworkElement host, DependencyObject view, StepModel leaf, StepModel container)
        {
            // 按"元素名 + DataContext 的模型"认人：树里有两个步骤模板实例（顶层算子 + 循环体算子），
            // 只按名字取第一个会把"禁用 A、量 B"混成假绿。
            var stepRoot = FindNamedRootForModel(view, "StepNodeRoot", leaf);
            leaf.IsDisEnable = true;
            host.UpdateLayout();
            PumpFrames(6);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "[measure] step-disabled-opacity={0:0.###}",
                stepRoot?.Opacity ?? double.NaN));
            leaf.IsDisEnable = false;

            var containerRoot = FindNamedRootForModel(view, "ContainerNodeRoot", container);
            container.IsDisEnable = true;
            host.UpdateLayout();
            PumpFrames(6);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "[measure] container-disabled-opacity={0:0.###}",
                containerRoot?.Opacity ?? double.NaN));
            container.IsDisEnable = false;
        }

        /// <summary>按 x:Name + DataContext 的 StepModel 找模板根元素（同模板多实例时不会认错人）</summary>
        private static FrameworkElement? FindNamedRootForModel(DependencyObject view, string name, StepModel model)
        {
            var all = new List<FrameworkElement>();
            CollectDescendants(view, all);
            return all.FirstOrDefault(fe => string.Equals(fe.Name, name, StringComparison.Ordinal)
                && fe.DataContext is CanvasNodeViewModel vm
                && ReferenceEquals(vm.Model, model));
        }

        /// <summary>有界等待"某个模型"的模板根出现（容器模板生成跨帧，慢机上固定帧数不够）</summary>
        private static FrameworkElement? WaitForNamedRootForModel(
            DependencyObject view, string name, StepModel model, TimeSpan timeout)
        {
            var hit = FindNamedRootForModel(view, name, model);
            var deadline = DateTime.UtcNow + timeout;
            while (hit == null && DateTime.UtcNow < deadline)
            {
                PumpFrames(2);
                hit = FindNamedRootForModel(view, name, model);
            }
            return hit;
        }

        /// <summary>从标题行往上找卡片本体：第一个宽高等于 NodeWidth/NodeHeight 的元素就是模板里那层固定盒子</summary>
        private static FrameworkElement? FindCardAncestor(DependencyObject? node)
        {
            while (node != null)
            {
                if (node is FrameworkElement fe
                    && Math.Abs(fe.Width - FlowCanvasViewModel.NodeWidth) < 0.01
                    && Math.Abs(fe.Height - FlowCanvasViewModel.NodeHeight) < 0.01)
                {
                    return fe;
                }

                node = node is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : null;
            }
            return null;
        }

        /// <summary>从输出目录往上找 AGENTS.md（与各断言文件的 RepoRoot 同一口径）</summary>
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

        /// <summary>场景 A：复刻真机反馈图式——纵向两步（采集→检测）。默认态：只见顺序链（数据线默认隐藏）</summary>
        private static void RenderScenarioA(string path)
            => Render(BuildScenarioA(), path, 640, 780);

        /// <summary>
        /// 场景 A2：与 A 同图式、显式打开数据线——工具条「数据线」勾选后的视觉对照。
        /// A（默认态）与 A2（打开态）两张图就是那条开关的亲眼前后证据。
        /// </summary>
        private static void RenderScenarioA2(string path)
            => Render(BuildScenarioA(), path, 640, 780, showDataLinks: true);

        private static Harness BuildScenarioA()
        {
            var h = new Harness();
            h.Flow.FlowName = "胶路检测";

            var capture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "图像采集_0")
            {
                State = StepState.Success,
            };
            var bead = new ActionStep("\uE7C1", "胶路检测", StubPluginProvider.LeafPlugin, "胶路检测_0")
            {
                State = StepState.Failed,
            };
            h.Add(capture);
            h.Add(bead);
            Harness.RawLink(bead, "In", capture, "Out");

            // 耗时徽章出图素材（2026-10-11 卡片徽章复核）：冻结值由引擎 EndTiming 写入，
            // 探针这里直接补数——采集 12.4ms / 检测 87.6ms
            capture.LastRunTimeMs = 12.4;
            bead.LastRunTimeMs = 87.6;

            return h;
        }

        /// <summary>场景 B：If 容器双分支（一实一空）+ 容器内外连线，验证框体/泳道/连线观感</summary>
        private static void RenderScenarioB(string path)
        {
            var h = new Harness();
            h.Flow.FlowName = "带分支流程";

            var capture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "图像采集_0")
            {
                State = StepState.Success,
            };
            var judge = new ConditionStep("\uE7C1", "胶路检测", StubPluginProvider.IfPlugin, "结果判断");
            var okStep = new ActionStep("\uE73E", "合格出货", StubPluginProvider.LeafPlugin, "合格出货_0");
            var ngStep = new ActionStep("\uE711", "NG 处理", StubPluginProvider.LeafPlugin, "NG处理_0");
            judge.Children[0].Steps.Add(okStep);
            judge.Children[1].Steps.Add(ngStep);   // 第二分支给内容（两个分支都有泳道）

            h.Add(capture);
            h.Add(judge);
            Harness.RawLink(judge, "In", capture, "Out");
            Harness.RawLink(okStep, "In", capture, "Out");

            Render(h, path, 900, 900);
        }

        /// <summary>
        /// 场景 C：多条件多嵌套图式（视觉评审用）——
        /// 顶层：图像采集 → 图像预处理 → If「质量分档」→ 结果汇总；
        /// 「质量分档」的 If 分支：合格品复检 → 嵌套 If「深度检测」（精细测量 / 返修标记）；
        /// Else 分支：For「重试采集」（内嵌重拍图像）。
        /// 连线含跨层级长线（顶层采集直喂深层测量）与内层喂外层（测量→结果汇总）。
        /// collapseDeepIf=true 时把内层「深度检测」折叠，展示折叠框与角标观感。
        /// </summary>
        private static void RenderScenarioC(string path, bool collapseDeepIf)
        {
            var h = new Harness();
            h.Flow.FlowName = collapseDeepIf ? "胶路检测·复杂图式（深度检测已折叠）" : "胶路检测·复杂图式";

            // ---- 顶层 ----
            var capture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "图像采集_0")
            {
                State = StepState.Success,
                Description = "车间工位 3 面阵相机，500 万像素",
            };
            var preprocess = new ActionStep("\uE7BA", "图像预处理", StubPluginProvider.LeafPlugin, "图像预处理_0")
            {
                State = StepState.Running,
                Description = "畸变校正后裁出胶路检测 ROI",
            };
            var qualityIf = new ConditionStep("\uE7C1", "结果判断", StubPluginProvider.IfPlugin, "质量分档")
            {
                Description = "按复检分数分档：≥90 进深度检测，否则重试",
            };
            var summary = new ActionStep("\uE73E", "数据汇总", StubPluginProvider.LeafPlugin, "结果汇总_0");

            // ---- 质量分档 · If 分支：复检 → 嵌套 If「深度检测」 ----
            var recheck = new ActionStep("\uE73E", "合格品复检", StubPluginProvider.LeafPlugin, "合格品复检_0")
            {
                State = StepState.Success,
                Description = "二次模板匹配 + 分数闸门",
            };
            var deepIf = new ConditionStep("\uE7C1", "结果判断", StubPluginProvider.IfPlugin, "深度检测");
            var measure = new ActionStep("\uE722", "尺寸测量", StubPluginProvider.LeafPlugin, "精细测量_0")
            {
                State = StepState.Success,
                Description = "卡尺拟合轮廓，输出胶路宽度/高度",
            };
            var repair = new ActionStep("\uE711", "返修标记", StubPluginProvider.LeafPlugin, "返修标记_0")
            {
                State = StepState.Skipped,
            };
            deepIf.Children[0].Steps.Add(measure);
            deepIf.Children[1].Steps.Add(repair);
            qualityIf.Children[0].Steps.Add(recheck);
            qualityIf.Children[0].Steps.Add(deepIf);

            // ---- 质量分档 · Else 分支：For「重试采集」 ----
            var retryFor = new ForStep("\uE722", "循环", StubPluginProvider.ForPlugin, "重试采集");
            var recapture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "重拍图像_0")
            {
                State = StepState.Running,
            };
            retryFor.Children[0].Steps.Add(recapture);
            qualityIf.Children[1].Steps.Add(retryFor);

            h.Add(capture);
            h.Add(preprocess);
            h.Add(qualityIf);
            h.Add(summary);

            // ---- 连线（FlowTopology 口径内）----
            Harness.RawLink(preprocess, "In", capture, "Out");
            Harness.RawLink(qualityIf, "In", preprocess, "Out");
            Harness.RawLink(recheck, "In", preprocess, "Out");       // 预处理直喂分支内复检
            Harness.RawLink(deepIf, "In", recheck, "Out");
            Harness.RawLink(measure, "In", capture, "Out");          // 跨层级长线：顶层采集直喂深层测量
            Harness.RawLink(summary, "In", measure, "Out");          // 内层喂外层尾部
            Harness.RawLink(recapture, "In", capture, "Out");        // 跨层级：顶层采集喂 Else 分支内重拍

            var canvas = new FlowCanvasViewModel(h.Workspace, new StubPluginProvider());
            WireViewModelFactory(canvas);

            if (collapseDeepIf)
            {
                // 画布构造时已 AutoLayout，布局落定后折叠内层容器再重画
                h.Flow.Layout.ToggleCollapsed(deepIf.StepID);
                canvas.RebuildInPlace();
            }

            RenderCore(canvas, path, 1000, 1000);
        }

        /// <summary>
        /// 场景 D（压力图式）：10 层 If 嵌套、33 个算子、21 条数据线 + 全量顺序链（110+ 画布项）。
        /// 嵌套链：分档_1 的 If 分支里放 测量_1A + 分档_2，递归到分档_10（最内层）；
        /// 每个容器的两个分支各一个叶子步骤（测量_xA / 测量_xB 共 20 个）；
        /// 数据线 = 顶层采集喂全部 20 个叶子 + 预处理喂最外层容器（跨层级长线压力）。
        /// 超深自动折叠（AutoCollapseDepth=3）生效后：1~3 层展开、4~10 层收成折叠框
        ///（图不再铺成一屏对角线；点折叠框头可逐层展开）。
        /// </summary>
        private static void RenderScenarioD(string path)
        {
            var h = new Harness();
            h.Flow.FlowName = "十层嵌套压力图式（自动折叠 3 层）";

            var capture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "图像采集_0")
            {
                State = StepState.Success,
            };
            var preprocess = new ActionStep("\uE7BA", "图像预处理", StubPluginProvider.LeafPlugin, "图像预处理_0")
            {
                State = StepState.Success,
            };
            var summary = new ActionStep("\uE73E", "数据汇总", StubPluginProvider.LeafPlugin, "结果汇总_0");

            // 自内向外组装嵌套链：循环终点 inner = 分档_1（最外层）
            ConditionStep? inner = null;
            var leaves = new System.Collections.Generic.List<ActionStep>();
            var ifSteps = new System.Collections.Generic.List<ConditionStep>();
            for (int level = 10; level >= 1; level--)
            {
                var ifStep = new ConditionStep("\uE7C1", "结果判断", StubPluginProvider.IfPlugin, $"分档_{level}");
                var leafA = new ActionStep("\uE73E", "尺寸测量", StubPluginProvider.LeafPlugin, $"测量_{level}A")
                {
                    State = level % 3 == 0 ? StepState.Skipped : StepState.Success,
                };
                var leafB = new ActionStep("\uE711", "缺陷标记", StubPluginProvider.LeafPlugin, $"测量_{level}B")
                {
                    State = level % 4 == 0 ? StepState.Failed : StepState.Running,
                };
                ifStep.Children[0].Steps.Add(leafA);
                if (inner != null)
                    ifStep.Children[0].Steps.Add(inner);
                ifStep.Children[1].Steps.Add(leafB);

                leaves.Add(leafA);
                leaves.Add(leafB);
                ifSteps.Add(ifStep);
                inner = ifStep;
            }

            h.Add(capture);
            h.Add(preprocess);
            h.Add(inner!);
            h.Add(summary);
            ifSteps.Reverse();   // 分档_1 在前，便于引用

            // 数据线：采集喂全部叶子 + 预处理喂最外层容器（全部合法：生产方排在容器之前）
            foreach (var leaf in leaves)
                Harness.RawLink(leaf, "In", capture, "Out");
            Harness.RawLink(inner!, "In", preprocess, "Out");

            Render(h, path, 1280, 1560);
        }

        /// <summary>
        /// 场景 E：并行分组（第一期）——采集 → 预处理 → Parallel{双分支} → 汇总，
        /// 分支间接外层输入合法、分支间互取画成红虚线 + 告警条。
        /// </summary>
        private static void RenderScenarioE(string path)
        {
            var h = new Harness();
            h.Flow.FlowName = "并行分组图式";

            var capture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "图像采集_0")
            {
                State = StepState.Success,
                Description = "相机 1 与相机 2 交替采集",
            };
            var preprocess = new ActionStep("\uE7BA", "图像预处理", StubPluginProvider.LeafPlugin, "图像预处理_0")
            {
                State = StepState.Success,
            };
            var par = new ParallelStep("\uE7C1", "并行分组", "VM.CanvasStub.Parallel", "多工位并行")
            {
                Description = "左工位与右工位各自检测，跑完汇合",
            };
            var leftA = new ActionStep("\uE73E", "尺寸测量", StubPluginProvider.LeafPlugin, "左工位测量_0")
            {
                State = StepState.Success,
                Description = "左工位胶路宽度/高度",
            };
            var leftB = new ActionStep("\uE73E", "缺陷标记", StubPluginProvider.LeafPlugin, "左工位标记_0")
            {
                State = StepState.Success,
            };
            var rightA = new ActionStep("\uE711", "缺陷检测", StubPluginProvider.LeafPlugin, "右工位检测_0")
            {
                State = StepState.Running,
                Description = "右工位气泡/断胶",
                // 运行中徽章素材：IsRunningFocus → 耗时徽章显示 CurrentRunTimeMs 实时值（Z8 双态绑定）
                IsRunningFocus = true,
                CurrentRunTimeMs = 15230,
            };
            par.Children[0].Steps.Add(leftA);
            par.Children[0].Steps.Add(leftB);
            par.Children[1].Steps.Add(rightA);
            var summary = new ActionStep("\uE73E", "数据汇总", StubPluginProvider.LeafPlugin, "结果汇总_0");

            h.Add(capture);
            h.Add(preprocess);
            h.Add(par);
            h.Add(summary);

            Harness.RawLink(preprocess, "In", capture, "Out");
            Harness.RawLink(par, "In", preprocess, "Out");
            Harness.RawLink(leftA, "In", capture, "Out");     // 分支接外层：合法
            Harness.RawLink(rightA, "In", capture, "Out");    // 分支接外层：合法
            Harness.RawLink(leftB, "In", leftA, "Out");       // 分支 1 内部：合法
            Harness.RawLink(summary, "In", leftB, "Out");
            Harness.RawLink(summary, "In", rightA, "Out");
            // 刻意一条跨分支非法线：左工位结果喂右工位（时序不确定）
            Harness.RawLink(rightA, "In", leftA, "Out");

            Render(h, path, 960, 900);
        }

        /// <summary>
        /// 场景 F：左侧流程栏（ProcessView 步骤树）——真机图式的对照图。
        /// 事故：树按 DataType 隐式选模型模板，ConditionStep/WhileStep/ForStep/StepCollection/ActionStep
        /// 都有、唯独 ParallelStep 没有 → WPF 回退默认模板把 ToString() 上屏，整行显示
        /// "VisionMaster.Models.ParallelStep"（分支也全丢）。
        ///
        /// 图式（2026-10-09 打磨后按用户真机截图重建，供"层级引导线 / 空分支提示 / 分支算子数 /
        /// 四类行左对齐 / 空分支瘦身"五处观感复核；2026-10-11 增补分支匹配容器）：
        ///顶层 图像采集_0(成功) → 胶路检测_0(运行) → 并行分组_0(4 分支) → 分支匹配-[分档_0]
        ///（判据 Type：Case 1/Case 2 有料、默认分支空，紫身份）→ 逻辑路由-[If_1]
        ///（If 带表达式 Score &gt; 80、Else）→ For 循环-[For_0]（循环体有料）→ While 循环-[While_0]
        ///（循环体空且条件未绑）→ Break_0；
        /// 并行分组_0 分支 1 = 逻辑路由-[If_0]（两分支空）/ For 循环-[For_0]（循环体空）/
        /// While 循环-[While_0]（循环体空、条件未绑，红卡）/ Break_0，分支 2~4 全空。
        /// </summary>
        private static void RenderScenarioF(string path)
        {
            var h = new Harness();
            h.Flow.FlowName = "并行分组图式";

            var capture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "图像采集_0")
            {
                State = StepState.Success,
                Description = "图像采集",
            };
            var bead = new ActionStep("\uE7C1", "胶路检测", StubPluginProvider.LeafPlugin, "胶路检测_0")
            {
                State = StepState.Running,
                Description = "胶路检测",
            };

            // ---- 并行分组_0：4 条分支，只有分支 1 有内容 ----
            var par = new ParallelStep("\uF0F7", "并行分组", "BuiltIn_Parallel", "并行分组_0")
            {
                Description = "4 工位并行，跑完汇合",
            };
            par.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "分支 3" });
            par.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "分支 4" });

            // 分支 1 = 真机图式：If_0（未绑定条件、两分支空）→ For_0（循环体空）→ While_0（循环体空、条件未绑）→ Break_0
            var branchIf = new ConditionStep("\uE7C1", "结果判断", StubPluginProvider.IfPlugin, "If_0");
            var branchFor = new ForStep("\uE722", "循环", StubPluginProvider.ForPlugin, "For_0");
            var branchWhile = new WhileStep("\uE722", "条件循环", StubPluginProvider.IfPlugin, "While_0");
            // Break 落 ActionStep 承建：真机里工具箱 BuiltIn_Break 就是按非容器分派成 ActionStep 的
            //（模型类 BreakStep 无树模板，是 U2 例外清单成员）——这里若 new 模型类，出图会复现
            //"类型全名上屏"的假象，与真机不符。
            var branchBreak = new ActionStep("\uE711", "跳出循环", "VM.CanvasStub.Break", "Break_0");
            par.Children[0].Steps.Add(branchIf);
            par.Children[0].Steps.Add(branchFor);
            par.Children[0].Steps.Add(branchWhile);
            par.Children[0].Steps.Add(branchBreak);

            // ---- 失败态对照（2026-10-18）：红边留白 vs 旧整卡红底——
            // 与用户真机截图同图式：并行分组的两个相邻分支各挂一张失败延时卡，
            // 验证"两张失败卡相邻时不再压过容器层级"（旧观感=两片红底抢第一眼）。
            var failDelay = new ActionStep("", "延时", StubPluginProvider.LeafPlugin, "延时_0")
            {
                State = StepState.Failed,
                Description = "延时",
            };
            par.Children[1].Steps.Add(failDelay);
            var failDelay2 = new ActionStep("", "延时", StubPluginProvider.LeafPlugin, "延时_1")
            {
                State = StepState.Failed,
                Description = "延时",
            };
            par.Children[2].Steps.Add(failDelay2);

            // ---- 顶层容器行：If（一实两空）对照分支胶囊的"N 个算子"与灰/蓝身份色 ----
            var topIf = new ConditionStep("\uE7C1", "结果判断", StubPluginProvider.IfPlugin, "If_1");
            topIf.Children[0].Expression = "Score > 80";
            topIf.Children[0]
                .Steps.Add(new ActionStep("\uE73E", "尺寸测量", StubPluginProvider.LeafPlugin, "精细测量_0"));
            topIf.Children[1]
                .Steps.Add(new ActionStep("\uE711", "返修标记", StubPluginProvider.LeafPlugin, "返修标记_0"));

            // ---- 顶层容器行：分支匹配（Case，紫身份）——判据/匹配值语义的对照图式（2026-10-11）----
            // Case 1 有料（绿勾位）、Case 2 有料、默认分支留空（"拖入算子"空态）；
            // Case 分支胶囊 = 紫身份 + DisplayName 拼匹配值，缺失匹配值亮红灯（IsConditionMissing 同源）。
            var topCase = new CaseStep("\uE7C1", "分支匹配", "BuiltIn_Case", "分档_0")
            {
                JudgeExpression = "Type",
                Description = "按判据的匹配值选择分支",
            };
            topCase.Children[0].Expression = "1";
            topCase.Children[0]
                .Steps.Add(new ActionStep("\uE73E", "尺寸测量", StubPluginProvider.LeafPlugin, "A档测量_0"));
            topCase.Children.Insert(
                1,
                new StepCollection { BranchType = BranchType.Case, StepName = "Case 2", Expression = "2" });
            topCase.Children[1]
                .Steps.Add(new ActionStep("\uE73E", "深度测量", StubPluginProvider.LeafPlugin, "B档测量_0"));

            // ---- 顶层容器行：For（循环体有料）/ While（循环体空且条件未绑）/ Break ----
            var topFor = new ForStep("\uE722", "循环", StubPluginProvider.ForPlugin, "For_0");
            topFor.Children[0]
                .Steps.Add(new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "重拍图像_0"));
            var topWhile = new WhileStep("\uE722", "条件循环", StubPluginProvider.IfPlugin, "While_0");
            var topBreak = new ActionStep("\uE711", "跳出循环", "VM.CanvasStub.Break", "Break_0");

            h.Add(capture);
            h.Add(bead);
            h.Add(par);
            h.Add(topCase);
            h.Add(topIf);
            h.Add(topFor);
            h.Add(topWhile);
            h.Add(topBreak);

            // 高度按真机图式给足：八个顶层节点 + 并行分组 4 分支 + 分支 1 内四层嵌套全展开约 1100px，
            // 460×900 会把顶层 For/While/Break 三行截在框外（看不到就没法对照容器行观感）。
            RenderTree(h.Workspace, path, 460, 1320);
        }

        /// <summary>
        /// 流程栏渲染：ProcessView 用到的 UI 分册资源只在本次渲染期间临时并入（出图毕即摘，A-E 的画布不受影响）。
        /// 并入的两册都要：Button.xaml（ToggleButton 基础样式）与 List.xaml——
        /// ExpandCollapseToggleStyle 已从 Button.xaml 搬到 List.xaml（2026-10-09 分册整理），
        /// 只并前者会让 ProcessView 在解析期抛"找不到 ExpandCollapseToggleStyle"（场景 F 整体出不了图）。
        /// DataContext 用最小桩：树只读 Workspace.CurrentFlow.Steps，drag-drop/命令绑定缺失时静默空跑。
        /// </summary>
        private static void RenderTree(WorkspaceContext workspace, string path, double width, double height)
        {
            var themeControls = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Controls/Button.xaml"),
            };
            var themeList = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Controls/List.xaml"),
            };
            Application.Current.Resources.MergedDictionaries.Add(themeControls);
            Application.Current.Resources.MergedDictionaries.Add(themeList);
            try
            {
                var stub = new TreeProbeViewModel(workspace);
                Prism.Mvvm.ViewModelLocationProvider.SetDefaultViewModelFactory(t =>
                {
                    if (t == typeof(ProcessViewModel)) return stub;
                    // ProcessView 顶部嵌入的 FlowListView 在真实宿主由 Prism 容器解析；
                    // 渲染探针没有容器，返回 null 让它的绑定静默空跑，不影响流程树出图。
                    if (t == typeof(VisionMaster.ViewModels.FlowListViewModel)) return null!;
                    return Activator.CreateInstance(t)!;
                });

                var view = new ProcessView();
                var host = new Border
                {
                    Width = width,
                    Height = height,
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                    Child = view,
                };
                host.Measure(new Size(width, height));
                host.Arrange(new Rect(0, 0, width, height));
                host.UpdateLayout();
                PumpFrames(6);

                // 根项默认收起：像真机那样层层展开，分支胶囊才可见（展开会生成子容器，多跑几轮收敛）
                for (int pass = 0; pass < 4; pass++)
                {
                    ExpandAll(view);
                    host.UpdateLayout();
                    PumpFrames(2);
                }

                // 分支卡片点击拦截自检（三条差异判据，事件回放到"树入口"）：PreviewMouseLeftButtonDown
                // 是 Direct 事件（按钮专用事件按元素逐个 crack，合成事件只能投给挂处理器的那个元素），
                // 所以按"处理器在树上看到的那份 args"回放：Source 指向被点元素。
                //  ① 分支卡片文字 → 必须被拦（不选中、不派发双击）；
                //  ② 分支卡片上的展开箭头（ToggleButton）→ **必须放行**（2026-10-09 审查抓到的回归：
                //     箭头就在分支卡片自己的容器模板里，判据若不豁免它，鼠标就点不开分支）；
                //  ③ 普通算子卡片 → 必须不拦（防误拦）。
                var tree = FindDescendant<TreeView>(view, _ => true);
                var branchText = FindDescendant<TextBlock>(
                    view,
                    d => d is TextBlock tb && tb.DataContext is StepCollection);
                var stepText = FindDescendant<TextBlock>(
                    view,
                    d => d is TextBlock tb && tb.DataContext is ActionStep);
                if (tree != null && branchText != null)
                {
                    var branchItem = FindVisualParent<TreeViewItem>(branchText);
                    bool branchBlocked = ReplayLeftDown(tree, branchText);
                    var expander = branchItem == null ? null : FindDescendant<ToggleButton>(branchItem, _ => true);
                    bool arrowBlocked = expander != null && ReplayLeftDown(tree, expander);
                    bool stepBlocked = stepText != null && ReplayLeftDown(tree, stepText);
                    Console.WriteLine(
                        $"[render][check] 分支卡片被拦截={branchBlocked}（期望 True）；分支箭头被拦截={arrowBlocked}（期望 False）；算子卡片被拦截={stepBlocked}（期望 False）");

                    // 分支级右键菜单自检（2026-10-09：分支结构编辑从参数面板下沉到流程栏右键菜单）：
                    //  ① 并行分组的分支胶囊自带菜单：两项、文案对、命令绑对、靶绑的是
                    //     PlacementTarget.DataContext（PlacementTarget = 胶囊 Border，其 DataContext 就是那条分支）；
                    //  ② 右键语义：胶囊上的右键不再被 Handled 拦（让 ContextMenuService 开胶囊那份菜单），
                    //     非并行分组的分支卡片仍被拦（放行只会误开父级菜单）。
                    var capsule = FindVisualParent<Border>(branchText);
                    var menu = capsule?.ContextMenu;
                    var menuItems = menu?.Items.OfType<MenuItem>().ToList() ?? new List<MenuItem>();
                    var renameItem = menuItems.FirstOrDefault();
                    var removeItem = menuItems.Count > 1 ? menuItems[1] : null;
                    bool menuOk =
                        menu != null && menuItems.Count == 2
                        && (renameItem?.Header as string) == "重命名本条分支"
                        && (removeItem?.Header as string) == "删除本条分支"
                        && CommandPath(renameItem) == "RenameParallelBranchCommand"
                        && CommandPath(removeItem) == "RemoveParallelBranchCommand"
                        && ParameterPath(renameItem) == "PlacementTarget.DataContext"
                        && ParameterPath(removeItem) == "PlacementTarget.DataContext"
                        && capsule?.DataContext is StepCollection; // 靶机制落点：胶囊的 DataContext 就是这条分支
                    bool capsuleRightBlocked = ReplayRightUp(tree, branchText);

                    // 反向：非并行分组的分支胶囊（If/For/While 的分支）没挂菜单，右键仍被拦
                    var otherCapsuleText = FindDescendant<TextBlock>(
                        view,
                        d => d is TextBlock tb
                             && tb.DataContext is StepCollection
                             && FindVisualParent<Border>(tb)?.ContextMenu == null);
                    bool otherRightBlocked = otherCapsuleText != null && ReplayRightUp(tree, otherCapsuleText);
                    Console.WriteLine(
                        $"[render][check] 分支胶囊菜单={menuOk}（期望 True：项={menuItems.Count} 靶绑定={ParameterPath(renameItem)} DataContext={(menu?.DataContext == null ? "null" : "非空")}）；"
                        + $"右键胶囊被拦截={capsuleRightBlocked}（期望 False，放行开胶囊菜单）；右键非并行分支被拦截={otherRightBlocked}（期望 True{((otherCapsuleText == null) ? "，但未取到非并行分支胶囊" : "")}）");

                    // 组头菜单「添加分支」（同一功能的另一半）：靶是"当前选中的组头"（SelectStep），
                    // 只在选中项是并行分组时可见——与 If 的「添加 ElseIf/Else」同一口径。
                    var addItem = tree.ContextMenu?.Items.OfType<MenuItem>()
                        .FirstOrDefault(m => (m.Header as string) == "添加分支");
                    string addVisibility = BindingPath(addItem, MenuItem.VisibilityProperty);
                    bool groupMenuOk = addItem != null
                        && CommandPath(addItem) == "AddParallelBranchCommand"
                        && ParameterPath(addItem) == "SelectStep"
                        && addVisibility == "IsParallelNodeSelected";
                    Console.WriteLine(
                        $"[render][check] 组头菜单「添加分支」={groupMenuOk}（期望 True：命令={CommandPath(addItem)} 靶={ParameterPath(addItem)} 可见性={addVisibility}）");

                    // 拖拽浮层：模板选择器按数据类型分派，卡片取到模型字段。
                    // gong 一个拖拽源只有一份 DragAdornerTemplate，树里混着 StepModel / StepCollection，
                    // 写死一种另一种就是空卡片（2026-10-21 设计基线 v2 落地时加的闸）。
                    var ghostSelector = GongSolutions.Wpf.DragDrop.DragDrop.GetDragAdornerTemplateSelector(tree);
                    var stepModel = stepText?.DataContext as StepModel;
                    var branchModel = branchText.DataContext as StepCollection;
                    var stepGhostTemplate = ghostSelector?.SelectTemplate(stepModel, tree);
                    var branchGhostTemplate = ghostSelector?.SelectTemplate(branchModel, tree);
                    bool stepGhostOk = ProbeGhostCard(
                        stepGhostTemplate, stepModel,
                        stepModel?.StepName ?? "", stepModel?.Category ?? "");
                    bool branchGhostOk = ProbeGhostCard(
                        branchGhostTemplate, branchModel,
                        branchModel?.DisplayName ?? "", "分支");
                    Console.WriteLine(
                        $"[render][check] 流程栏拖拽浮层：步骤卡={stepGhostOk}（期望 True）；分支卡={branchGhostOk}（期望 True）");

                    RenderGhostCards(
                        Path.Combine(Path.GetDirectoryName(path) ?? ".", "process-tree-drag-ghost.png"),
                        stepGhostTemplate, stepModel, branchGhostTemplate, branchModel);
                }
                else
                {
                    Console.WriteLine("[render][check] 未找到流程树/分支卡片文字元素（场景 F 图式变了？）");
                }

                SavePng(host, path);
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(themeList);
                Application.Current.Resources.MergedDictionaries.Remove(themeControls);
            }
        }

        private static void ExpandAll(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TreeViewItem item)
                    item.IsExpanded = true;
                ExpandAll(child);
            }
        }

        /// <summary>把左键按下事件投到树上（Source 指向被点元素），返回处理器有没有把它吃掉。</summary>
        private static bool ReplayLeftDown(TreeView tree, DependencyObject source)
        {
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                Source = source,
            };
            tree.RaiseEvent(args);
            return args.Handled;
        }

        /// <summary>
        /// 把右键抬起事件投到树上（Source 指向被点元素），返回处理器有没有把它吃掉。
        /// 视图的右键处理器挂在 PreviewMouseRightButtonUp 上（方法名叫 ...RightButtonDown，历史命名，别被误导）；
        /// 这类"按键专用"事件是 Direct 路由，合成事件只能投给挂处理器的那个元素（= 树），Source 声明被点处。
        /// </summary>
        private static bool ReplayRightUp(TreeView tree, DependencyObject source)
        {
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
            {
                RoutedEvent = UIElement.PreviewMouseRightButtonUpEvent,
                Source = source,
            };
            tree.RaiseEvent(args);
            return args.Handled;
        }

        /// <summary>取元素上某属性的 Binding 路径（没绑或绑的不是 Binding 就返回空串）。</summary>
        private static string BindingPath(DependencyObject target, DependencyProperty property)
            => target == null ? string.Empty : BindingOperations.GetBinding(target, property)?.Path?.Path ?? string.Empty;

        /// <summary>菜单项 Command 的绑定路径（期望是 VM 上的命令名）</summary>
        private static string CommandPath(MenuItem item) => item == null ? string.Empty : BindingPath(item, MenuItem.CommandProperty);

        /// <summary>菜单项 CommandParameter 的绑定路径（期望是 PlacementTarget.DataContext = 命中分支）</summary>
        private static string ParameterPath(MenuItem item)
            => item == null ? string.Empty : BindingPath(item, MenuItem.CommandParameterProperty);

        /// <summary>深度优先找第一个命中的后代元素（流程栏自检用）。</summary>
        private static T? FindDescendant<T>(DependencyObject root, Func<DependencyObject, bool> match)
            where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit && match(child))
                    return hit;
                var found = FindDescendant<T>(child, match);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>沿视觉树向上找指定类型的祖先（源可能是文本、图标等深层元素）。</summary>
        private static T? FindVisualParent<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null && source is not T)
                source = VisualTreeHelper.GetParent(source);
            return source as T;
        }

        /// <summary>流程栏（ProcessView）的最小 DataContext：树只绑 Workspace.CurrentFlow.Steps。</summary>
        private sealed class TreeProbeViewModel
        {
            public TreeProbeViewModel(WorkspaceContext workspace) => Workspace = workspace;

            public WorkspaceContext Workspace { get; }

            /// <summary>
            /// 底部工具条的「失败自动跳转」勾选框也绑这个属性（搜索行下线后它是唯一入口）。
            /// 桩上给真机默认值 true：不给的话绑定静默落空、出图里框是空的，
            /// 看图的会以为"默认关"——而真机默认是开。
            /// </summary>
            public bool AutoJumpToFailure { get; set; } = true;
        }

        /// <summary>
        /// 场景 G：并行分组参数面板——标准属性面板（FlatPropertyGrid + ParallelGroupEditModel）。
        /// 2026-10-09 用户裁决"没必要新建一个视图"后，专属视图 ParallelGroupConfigView 已删除，
        /// 出图对象改为 EasyDialog.ShowPropertyGridSync 弹窗里的那个 FlatPropertyGrid 本体
        ///（弹窗自身是 Popup/Window，RenderTargetBitmap 出不了它，出属性网格控件即所见内容）。
        /// 主题字典并入方式照旧：临时并入 /UI;component/Themes/Generic.xaml，出图毕即摘。
        /// </summary>
        private static void RenderScenarioG(string path)
        {
            var h = new Harness();
            var par = new ParallelStep("\uF0F7", "并行分组", "BuiltIn_Parallel", "左右工位并行")
            {
                ExecutionMode = ParallelExecutionMode.Parallel,
                FailFastMode = FailFastMode.On,
                JoinTimeoutMs = 8000,
            };
            par.Children[0].StepName = "左工位";
            par.Children[1].StepName = "右工位";
            par.Children[0].Steps.Add(new ActionStep("\uE73E", "尺寸测量", StubPluginProvider.LeafPlugin, "左工位测量_0"));
            par.Children[1].Steps.Add(new ActionStep("\uE711", "缺陷检测", StubPluginProvider.LeafPlugin, "右工位检测_0"));
            h.Add(par);

            var theme = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml"),
            };
            Application.Current.Resources.MergedDictionaries.Add(theme);
            try
            {
                // 与 StepParameterDialog 的 ParallelStep 分支同款构造（EasyDialog 弹窗里就是这么建的）
                var edit = new ParallelGroupEditModel(par);
                var grid = new UI.CustomControl.FlatPropertyGrid
                {
                    BindingObject = edit,
                    MinWidth = 450,
                    MaxHeight = 600,
                };
                var host = new Border
                {
                    Width = 760,
                    Height = 640,
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                    Child = grid,
                };
                host.Measure(new Size(760, 640));
                host.Arrange(new Rect(0, 0, 760, 640));
                host.UpdateLayout();
                PumpFrames(6);

                SavePng(host, path);
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        }

        /// <summary>
        /// 场景 I：变量管理弹窗（两栏版）——真 GlobalVariableView + 最小桩 VM 出图。
        ///
        /// 为什么要出这张图：2026-10-15 把"三栏（来源 / 新建表单 / 明细）"收成"两栏
        /// （来源 / 明细）并把录入动作搬进「＋ 添加变量」弹窗，这是**布局级**改动——
        /// 断言只能守住"控件还在、列宽契约还在"，守不住"左栏 216 下右栏会不会挤、卡片头
        /// 那颗按钮会不会被搜索框顶出去"。出图供人眼复核。
        ///
        /// 桩 VM 提供：SourceNodes（本地 + 一条连接）/ SelectedSource / FilteredDisplayNodes
        /// （≥4 行：本地标量、本地数组根 + 2 个元素子行、网络变量）/ SearchText / AddCommand。
        /// 视图用的 Dialog* 资源只在本次渲染期间临时并入（照场景 F 的思路，出图毕即摘）。
        /// </summary>
        private static void RenderScenarioI(string path)
        {
            // 与场景 F 同款：本视图引用的 Dialog.* 各分册、Button/List 基础控件皮肤
            // 都挂在 Generic.xaml 的合并链上，所以并 Generic.xaml 一册即可覆盖绝大部分。
            var theme = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml"),
            };
            // 但 **ExpandToggleButtonTemplate**（变量名列的展开箭头）与 **LinkButtonStyle**
            // （应用级链接按钮样式；数组入口 v2 已不再用它，但 App 内联资源里仍可能被其它视图引用）只定义在 VisionMaster\App.xaml 的**内联资源**里，
            // UI 库的合并链上没有——首次出图实测：缺 ExpandToggleButtonTemplate 会让视图
            // 在解析期抛 XamlParseException（整个场景出不了图）。
            // 这里按 _ResProbe.AddAppInlineResources 的同一套做法抠出那段内联资源临时并入
            //（剥掉 MergedDictionaries 段：那部分由探针自己按顺序并）。
            // 注意 App.xaml 的 Icon 键**不会**顶掉探针设的 MDL2 兜底：ResourceDictionary 的
            // 直接条目优先于合并字典，所以 A-H 各图的字体不受影响。
            var appInline = LoadAppInlineResources();
            Application.Current.Resources.MergedDictionaries.Add(theme);
            if (appInline != null) Application.Current.Resources.MergedDictionaries.Add(appInline);
            VariableManagerProbeViewModel? stub = null;
            try
            {
                stub = new VariableManagerProbeViewModel();
                // 按**视图类型**注册（不是 SetDefaultViewModelFactory）：Prism 的命名约定
                // 会把 GlobalVariableView 推成 GlobalVariableViewModel，而真 VM 叫
                // GlobalVariableManagerViewModel（App.xaml.cs 的 RegisterDialog 显式绑的那一对），
                // 约定解析不出类型 → 工厂根本不会被调用 → DataContext 为 null（首次出图实测：
                // 整张表空白、来源列表空白）。ScadaChecks 的 ScadaPropertyView 也是这么注册的。
                Prism.Mvvm.ViewModelLocationProvider.Register<GlobalVariableView>(() => stub);

                var view = new GlobalVariableView();
                var host = new Border
                {
                    Width = 1300,
                    Height = 700,
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                    Child = view,
                };
                host.Measure(new Size(1300, 700));
                host.Arrange(new Rect(0, 0, 1300, 700));
                host.UpdateLayout();
                PumpFrames(6);

                Console.WriteLine(
                    $"[render] 场景 I：变量管理两栏（左栏来源 {stub.SourceNodes.Count} 项 / 右栏明细 {stub.FilteredDisplayNodes.Count} 行，"
                    + "添加变量入口 = 右栏卡片头 AddCommand）");
                SavePng(host, path);
            }
            finally
            {
                stub?.DisposeProbe();
                if (appInline != null) Application.Current.Resources.MergedDictionaries.Remove(appInline);
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        }

        /// <summary>
        /// 场景 K：数组编辑弹窗（设计基线 global-variables v2 的第二个面）。
        ///
        /// 弹窗自身是 Prism Dialog（无系统边框的 Window），RenderTargetBitmap 出不了 Window，
        /// 这里直接渲染视图内容（与场景 J 渲染 FlatPropertyGrid 同款做法）。
        /// 正常态与校验失败态各出一张：失败态要能看出"出错项红框 + 底部错误条、弹窗没关"。
        /// </summary>
        private static void RenderScenarioK(string path, bool withError)
        {
            var theme = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml"),
            };
            var appInline = LoadAppInlineResources();
            Application.Current.Resources.MergedDictionaries.Add(theme);
            if (appInline != null) Application.Current.Resources.MergedDictionaries.Add(appInline);

            try
            {
                var model = new LocalVariableModel
                {
                    Name = "HoleCoordinates",
                    DataType = typeof(double[]),
                    // 基准与当前值故意差一项：出图要能看到「基准：33」与行内 ↺ / 1 项已修改
                    DefaultValue = new[] { 12.5, 18.0, 33.0, 40.25 },
                    Value = new[] { 12.5, 18.0, 35.0, 40.25 },
                };

                var vm = new ArrayEditorViewModel();
                vm.OnDialogOpened(new DialogParameters
                {
                    { ArrayEditorViewModel.VariableKey, model },
                });

                if (withError)
                {
                    vm.Elements[1].Value = "abc";
                    vm.ConfirmCommand.Execute();   // 失败只标红 + 出错误条，不关窗
                }

                var view = new ArrayEditorView { DataContext = vm };
                var host = new Border
                {
                    Width = 620,
                    Height = 640,
                    Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7)),
                    Child = view,
                };
                host.Measure(new Size(620, 640));
                host.Arrange(new Rect(0, 0, 620, 640));
                host.UpdateLayout();
                PumpFrames(6);

                Console.WriteLine(
                    $"[render] 场景 K：数组编辑弹窗（{(withError ? "校验失败" : "正常")}，"
                    + $"{model.Name} / {vm.Elements.Count} 项 / 基准提示 = {vm.BaselineHint}）");
                SavePng(host, path);
            }
            finally
            {
                if (appInline != null) Application.Current.Resources.MergedDictionaries.Remove(appInline);
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        }

        /// <summary>
        /// 场景 K3：网络数组「写入数组」弹窗（默认只写修改项 / 逐元素换算 / 质量预警）。
        /// <para>与 K/K2 的本地编辑是两套语义：这里要看到「写入范围」分段、每行的「原值」、
        /// 「换算」列（×0.1 +5 ℃ / — / ×2 mm）与质量预警条——这些在本地模式都不出现。</para>
        /// </summary>
        private static void RenderScenarioKNetwork(string path)
        {
            var theme = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml"),
            };
            var appInline = LoadAppInlineResources();
            Application.Current.Resources.MergedDictionaries.Add(theme);
            if (appInline != null) Application.Current.Resources.MergedDictionaries.Add(appInline);

            try
            {
                var model = new NetworkVariableModel
                {
                    Name = "State",
                    DataType = typeof(int[]),
                    ConnectionName = "PLC1",
                    AddressConfig = new ModbusAddress
                    {
                        Area = ModbusArea.HoldingRegisters,
                        DataType = DataValueType.Int32,
                        Offset = "0",
                        Length = 3
                    },
                    Value = new double[] { 10, 20, 30 },
                    ElementConversions = new ElementConversion?[]
                    {
                        new() { Scale = 0.1, Offset = 5, DecimalPlaces = 1, Unit = "℃" },
                        null,
                        new() { Scale = 2, Offset = 0, DecimalPlaces = 0, Unit = "mm" },
                    }
                };
                // 质量不是 Good：出图要能看到「当前值是旧值，建议先刷新」的预警条
                model.UpdateQuality(VariableQuality.Uncertain);

                var vm = new ArrayEditorViewModel();
                vm.OnDialogOpened(new DialogParameters
                {
                    { ArrayEditorViewModel.VariableKey, model },
                });
                // 改一项：出图要能看到「原值：20」与「已改 1 项」
                vm.Elements[1].Value = "25";

                var view = new ArrayEditorView { DataContext = vm };
                var host = new Border
                {
                    Width = 640,
                    Height = 680,
                    Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7)),
                    Child = view,
                };
                host.Measure(new Size(640, 680));
                host.Arrange(new Rect(0, 0, 640, 680));
                host.UpdateLayout();
                PumpFrames(6);

                Console.WriteLine(
                    $"[render] 场景 K3：网络数组写入弹窗（{model.Name} / {vm.Elements.Count} 项 / "
                    + $"模式={(vm.IsChangedOnly ? "只写修改项" : "整组写入")} / 已改={vm.ChangedCount} / "
                    + $"换算={vm.Elements[0].ConversionText}）");
                SavePng(host, path);
            }
            finally
            {
                if (appInline != null) Application.Current.Resources.MergedDictionaries.Remove(appInline);
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        }

        /// <summary>
        /// 把 VisionMaster\App.xaml 的**内联资源**（Icon / ExpandToggleButtonTemplate /
        /// LinkButtonStyle）抠出来临时并入——这三支键不在 UI 库的合并链上，
        /// 只在应用级 App.xaml 里。做法与 <c>_ResProbe.AddAppInlineResources</c> 逐字同源
        /// （剥掉 MergedDictionaries 段，只取 PrismApplication.Resources 的内联条目）。
        /// 定位不到 App.xaml 时返回 null（场景仍可出图，只是缺那几支键的视图会抛）。
        /// </summary>
        private static ResourceDictionary? LoadAppInlineResources()
        {
            try
            {
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
                {
                    var candidate = Path.Combine(dir.FullName, @"VisionMaster\App.xaml");
                    if (!File.Exists(candidate)) continue;

                    var text = File.ReadAllText(candidate);
                    const string openTag = "<prism:PrismApplication.Resources>";
                    const string closeTag = "</prism:PrismApplication.Resources>";
                    int s = text.IndexOf(openTag, StringComparison.Ordinal);
                    int e = text.IndexOf(closeTag, StringComparison.Ordinal);
                    if (s < 0 || e < 0) return null;

                    var inner = text.Substring(s + openTag.Length, e - s - openTag.Length);
                    inner = System.Text.RegularExpressions.Regex.Replace(
                        inner,
                        "<ResourceDictionary.MergedDictionaries>.*?</ResourceDictionary.MergedDictionaries>",
                        string.Empty,
                        System.Text.RegularExpressions.RegexOptions.Singleline);

                    int bodyStart = inner.IndexOf('>', inner.IndexOf("<ResourceDictionary", StringComparison.Ordinal)) + 1;
                    int bodyEnd = inner.LastIndexOf("</ResourceDictionary>", StringComparison.Ordinal);
                    if (bodyStart <= 0 || bodyEnd <= bodyStart) return null;

                    var wrapper =
                        "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"" +
                        " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"" +
                        " xmlns:prism=\"http://prismlibrary.com/\">" +
                        inner.Substring(bodyStart, bodyEnd - bodyStart) + "</ResourceDictionary>";

                    return (ResourceDictionary)XamlReader.Parse(wrapper);
                }
            }
            catch
            {
                // 探针不该因为"抠不出内联资源"而整体失败：缺键的视图会在解析期自己抛出来
            }
            return null;
        }

        /// <summary>
        /// 场景 J：添加变量弹窗里的属性网格本体（网络变量草稿）。
        ///
        /// 弹窗自身是 EasyDialog 动态建的 Window（RenderTargetBitmap 出不了它），
        /// 出 FlatPropertyGrid 控件即"弹窗里那一屏内容"——与场景 G（并行分组参数面板）同款做法。
        /// 选网络草稿而不是本地草稿：它多出「网络参数」与「说明」两组，正是本轮新加、
        /// 也最需要肉眼复核的部分（三个运行期候选下拉 + 地址预览只读行）。
        /// </summary>
        private static void RenderScenarioJ(string path)
        {
            var theme = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml"),
            };
            Application.Current.Resources.MergedDictionaries.Add(theme);
            try
            {
                var draft = new NetworkVariableDraft(
                    new[] { "整数 (int)", "整数数组 (int[])", "小数 (double)", "布尔 (bool)" },
                    new[] { "PLC_01", "PLC_02" },
                    new[] { "默认组", "快组 (50ms)" },
                    new[] { "保持寄存器 (4x, 读写字)", "线圈 (0x, 读写布尔)" },
                    d => $"x=3;{d.Offset}",
                    // 协议/类型回调：场景 J 要出「地址参数（Modbus 站号/40001）+ 元素参数（数组个数）」
                    // 两块互斥字段，没有这两个回调就只会看到"先选所属连接"的提示行。
                    new NetworkVariableDraftContext
                    {
                        ProtocolFor = _ => CommunicationType.ModbusTcp,
                        TypeResolver = GlobalVariableManagerViewModel.ResolveType,
                    })
                {
                    VariableName = "Motor1_Speed",
                    TypeName = "整数数组 (int[])",
                    InitialValue = "0",
                    Description = "1 号工位主轴转速",
                    ConnectionName = "PLC_01",
                    ScanGroupName = "快组 (50ms)",
                    AreaName = "保持寄存器 (4x, 读写字)",
                    Offset = "100",
                };

                var grid = new UI.CustomControl.FlatPropertyGrid
                {
                    BindingObject = draft,
                    MinWidth = 450,
                    MaxHeight = 600,
                };
                var host = new Border
                {
                    Width = 760,
                    Height = 640,
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                    Child = grid,
                };
                host.Measure(new Size(760, 640));
                host.Arrange(new Rect(0, 0, 760, 640));
                host.UpdateLayout();
                PumpFrames(6);

                // 属性网格是**懒加载**的：内容只在对应 Tab 被选中时才构建，
                // 默认停在第一个 Tab（基本）——不切页的话「网络参数」「说明」两组
                // 在图上根本看不见，而那正是本轮新增、最需要复核的部分。
                // 先逐页点一遍（每个 Tab 都真建一次内容：只读行/候选下拉的生成路径全跑到，
                // 有异常当场抛），最后停在「网络参数」出图。
                foreach (var tab in EnumeratePropertyGridTabs(host))
                {
                    tab.IsSelected = true;
                    host.UpdateLayout();
                    PumpFrames(2);
                }
                SelectPropertyGridTab(host, "网络参数");
                host.UpdateLayout();
                PumpFrames(6);

                // 自检行：三个分组都真建出了内容（懒加载的每个 Tab 都被选中过一次）。
                // 「说明」组的地址预览与线圈提示是只读行，若生成路径有异常这里就已经抛了；
                // 这里再把"建了几个 Tab"印出来，作为"三组都跑到"的证据。
                Console.WriteLine(
                    $"[render][check] 场景 J 属性网格 Tab 数={EnumeratePropertyGridTabs(host).Count()}"
                    + $"（预期 3：基本 / 网络参数 / 说明）");

                Console.WriteLine(
                    $"[render] 场景 J：添加变量弹窗（网络变量草稿 FlatPropertyGrid，"
                    + $"连接={draft.ConnectionName} 扫描组={draft.ScanGroupName} 存储区={draft.AreaName} 地址预览={draft.AddressPreview}）");
                SavePng(host, path);
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        }

        /// <summary>
        /// 变量管理弹窗的真实 VM 探针：视图的 code-behind 只认
        /// <see cref="GlobalVariableManagerViewModel"/>（HookViewModel / ApplyDetailColumnMode /
        /// 分组视图都在那里执行），所以这里用真实服务构造，不再用"只有 AddCommand"的桩。
        /// </summary>
        private sealed class VariableManagerProbeViewModel : GlobalVariableManagerViewModel
        {
            private readonly WorkspaceContext _workspace;
            private readonly AdvancedCommunicationManager _manager;
            private readonly NetworkVariableBridge _bridge;

            public VariableManagerProbeViewModel() : this(CreateDependencies()) { }

            private VariableManagerProbeViewModel(
                (WorkspaceContext Workspace, AdvancedCommunicationManager Manager, NetworkVariableBridge Bridge, IDialogService Dialog) deps)
                : base(deps.Workspace, deps.Manager, deps.Bridge, deps.Dialog)
            {
                _workspace = deps.Workspace;
                _manager = deps.Manager;
                _bridge = deps.Bridge;
                Populate();
            }

            private static (WorkspaceContext, AdvancedCommunicationManager, NetworkVariableBridge, IDialogService) CreateDependencies()
            {
                var workspace = new WorkspaceContext();
                workspace.GlobalVariables.Clear();
                var manager = new AdvancedCommunicationManager();
                var bridge = new NetworkVariableBridge(workspace, manager, new StubLog());
                return (workspace, manager, bridge, new SilentDialogService());
            }

            public void DisposeProbe()
            {
                try { Dispose(); } catch { }
                try { _bridge.Dispose(); } catch { }
                try { _manager.Dispose(); } catch { }
            }

            private void Populate()
            {
                SourceNodes.Clear();
                SourceNodes.Add(new VariableSourceNode
                {
                    Key = "Local",
                    DisplayName = "本地变量",
                    Icon = "\uF15B",
                    Count = 2,
                    IsOnline = true,
                });
                SourceNodes.Add(new VariableSourceNode
                {
                    Key = "PLC_01",
                    DisplayName = "PLC_01",
                    Icon = "\uF1EB",
                    IsNetwork = true,
                    ProtocolText = "Modbus TCP",
                    Count = 1,
                    IsOnline = true,
                });
                SelectedSource = SourceNodes[0];   // 本地来源：三列收起，与设计稿默认态一致

                var localScalar = new LocalVariableModel
                {
                    Name = "CycleCount",
                    DataType = typeof(int),
                    Description = "节拍计数",
                    DefaultValue = 12,
                    Value = 15,
                };
                var localArray = new LocalVariableModel
                {
                    Name = "Barcode",
                    DataType = typeof(string[]),
                    Description = "条码结果",
                    DefaultValue = new[] { "SN-A", "SN-B" },
                    Value = new[] { "SN-A", "SN-B" },
                };
                FilteredDisplayNodes.Add(Root(localScalar));
                var arrayRoot = Root(localArray);
                arrayRoot.IsExpanded = true;
                FilteredDisplayNodes.Add(arrayRoot);
                FilteredDisplayNodes.Add(Child(arrayRoot, "[0]", "SN-A", 0));
                FilteredDisplayNodes.Add(Child(arrayRoot, "[1]", "SN-B", 1));
            }

            private static VariableNode Root(IVariable model)
            {
                var isNetwork = model.VariableType == VariableType.Communication;
                var node = new VariableNode
                {
                    OriginalModel = model,
                    Name = model.Name,
                    DataType = model.DataType,
                    TypeName = model.DataType?.Name,
                    Description = model.Description,
                    ChildDefaultValue = model.DefaultValue,
                    ChildValue = model.Value,
                    Level = 0,
                    IsNetwork = isNetwork,
                    SourceLabel = isNetwork ? model.ConnectionName : "本地",
                    Address = model.AddressConfig?.Address ?? (isNetwork ? "x=3;100" : null),
                    IsConnected = true,
                };
                if (isNetwork)
                {
                    node.ScanGroupOptions.Add("默认组");
                    node.ScanGroupOptions.Add("快组 (50ms)");
                    node.ScanGroupText = "快组 (50ms)";
                }
                return node;
            }

            private static VariableNode Child(VariableNode parent, string name, string value, int index)
            {
                var child = new VariableNode
                {
                    OriginalModel = parent.OriginalModel,
                    Name = name,
                    DataType = parent.DataType?.GetElementType(),
                    TypeName = parent.DataType?.GetElementType()?.Name,
                    ChildValue = value,
                    // ElementIndex 必须给：视图的「当前值」单元格读的是父数组 CurrentValueText[index]，
                    // 不给就是 -1 → 落到根路径 → 显示成数组整体（出图会误导成"元素行显示类型名"）
                    ElementIndex = index,
                    Level = 1,
                    SourceLabel = parent.SourceLabel,
                    IsNetwork = parent.IsNetwork,
                    IsConnected = true,
                };
                parent.Children.Add(child);
                return child;
            }
        }

        /// <summary>属性网格的全部 Tab（懒加载：逐页选中即逐页建内容）</summary>
        private static IEnumerable<TabItem> EnumeratePropertyGridTabs(DependencyObject root)
        {
            var tab = FindDescendant<TabControl>(root, _ => true);
            return tab == null ? Enumerable.Empty<TabItem>() : tab.Items.OfType<TabItem>().ToList();
        }

        /// <summary>
        /// 把属性网格切到指定表头的 Tab（属性网格懒加载内容，不切页就看不到那一组）。
        /// 找不到同名 Tab 时静默不动（图还是能出，只是停在默认页）。
        /// </summary>
        private static void SelectPropertyGridTab(DependencyObject root, string header)
        {
            var tab = FindDescendant<TabControl>(root, _ => true);
            if (tab == null) return;

            foreach (var item in tab.Items.OfType<TabItem>())
            {
                if (string.Equals(item.Header as string, header, StringComparison.Ordinal))
                {
                    tab.SelectedItem = item;
                    return;
                }
            }
        }

        private static void Render(Harness h, string path, double width, double height, bool showDataLinks = false)
        {
            var canvas = new FlowCanvasViewModel(h.Workspace, new StubPluginProvider());
            WireViewModelFactory(canvas);

            // 数据线默认隐藏：需要"打开态"对照图时显式打开（setter 自带重建）
            if (showDataLinks)
                canvas.ShowDataLinks = true;

            RenderCore(canvas, path, width, height);
        }

        /// <summary>
        /// 场景 perf（性能量化，2026-10-12 评审清单第三批）：300/600 节点压力图式，
        /// 与常规 gate 的 [G8] 断言共用 <see cref="BuildPerfGraph"/> 一份生成器。
        /// 只量化不优化（局部刷新/虚拟化都不做）——量 RebuildInPlace 毫秒与整帧毫秒
        ///（各 3 次取中位数防抖），stdout 打 [perf] 行，结论供评审拍板
        /// "当前规模是否已疼、第一刀接不接 FlowLayoutStore 局部刷新"。
        /// </summary>
        private static void RenderScenarioPerf(string path, int targetNodes)
        {
            var (h, expected) = BuildPerfGraph(targetNodes);
            var canvas = new FlowCanvasViewModel(h.Workspace, new StubPluginProvider());
            WireViewModelFactory(canvas);

            // 全量展开：压力图式要测的是"全节点在场"的重建与整帧（首建的超深自动折叠先撤掉）
            canvas.ExpandAllCommand.Execute();

            var view = new FlowCanvasView();
            var host = new Border
            {
                Width = 1280,
                Height = 1560,
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                Child = view,
            };
            host.Measure(new Size(1280, 1560));
            host.Arrange(new Rect(0, 0, 1280, 1560));
            host.UpdateLayout();
            PumpFrames(16);

            // 两段式适配：300/600 个 ItemContainer 的生成跨多帧，先泵足再适配，
            // 否则 ItemsExtent 还没长全、FitToScreen 按局部内容算 → 停在 1:1 只拍到一截（实测踩过）
            var editor = FindEditor(view);
            if (editor != null)
            {
                editor.FitToScreen(null);
                host.UpdateLayout();
                PumpFrames(8);

                // FitToScreen 在大图上不缩（ItemsExtent 未长全时按局部内容算，停在 1:1 只拍一截，实测踩过）
                // ——以 extent 实测为准手工兜底：按可视面积算缩放，再把 extent 中心对到视口中心
                //（不走 BringIntoView：600 节点图实测它没把视口送到内容处，出图整版空）
                var extent = editor.ItemsExtent;
                if (!extent.IsEmpty && extent.Width > 0 && extent.Height > 0
                    && editor.ActualWidth > 0 && editor.ActualHeight > 0)
                {
                    double fit = Math.Min((host.Width - 40) / extent.Width, (host.Height - 40) / extent.Height);
                    double zoom = Math.Max(0.05, Math.Min(fit, 1.0));
                    if (editor.ViewportZoom > zoom)
                    {
                        editor.ViewportZoom = zoom;
                        editor.ViewportLocation = new Point(
                            extent.X + extent.Width / 2 - editor.ActualWidth / 2 / zoom,
                            extent.Y + extent.Height / 2 - editor.ActualHeight / 2 / zoom);
                    }
                }

                host.UpdateLayout();
                PumpFrames(6);
                Console.WriteLine(
                    $"[render][diag] perf{targetNodes} extent={editor.ItemsExtent.Width:0}x{editor.ItemsExtent.Height:0}"
                    + $" zoom={editor.ViewportZoom:0.00}");
            }

            // 暖场一轮（JIT / 模板缓存），不计时
            canvas.RebuildInPlace();
            host.UpdateLayout();
            PumpFrames(4);

            var rebuildMs = new System.Collections.Generic.List<double>(3);
            var frameMs = new System.Collections.Generic.List<double>(3);
            for (int round = 0; round < 3; round++)
            {
                // RebuildInPlace：VM 侧节点/连线/几何全量重建
                var sw = System.Diagnostics.Stopwatch.StartNew();
                canvas.RebuildInPlace();
                sw.Stop();
                rebuildMs.Add(sw.Elapsed.TotalMilliseconds);

                // 整帧：重建 + 布局 + 容器生成泵两拍（可视树跟上这次重建）
                sw.Restart();
                host.UpdateLayout();
                PumpFrames(2);
                sw.Stop();
                frameMs.Add(sw.Elapsed.TotalMilliseconds);
            }

            int rendered = canvas.Nodes.Count(n => n.Kind != CanvasNodeKind.Lane);
            Console.WriteLine(
                $"[perf] N={targetNodes} 节点={rendered}（预期 {expected}）"
                + $" RebuildInPlace={Median(rebuildMs):0.#} ms 全帧={Median(frameMs):0.#} ms");

            SavePng(host, path);
        }

        /// <summary>三次取中位数（防抖；偶发的 GC 尖峰不进数字）</summary>
        private static double Median(System.Collections.Generic.List<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }

        /// <summary>
        /// 压力图式生成器（[G8] 断言与 perf 场景共用一份，图式只有一份不劈叉）：
        /// 宽×深混合——顶层 topCount 个算子 + 1 个并行分组（branches 条并行分支），
        /// 每条分支 depth 层嵌套 If、每层 If 共 leavesPerIf 个叶子（If 分支 2 片 + Else 分支 1 片）；
        /// 数据线 = 顶层「采集」喂全部叶子（宽幅长线压力，与场景 D 同款合法拓扑：生产方在最前）。
        /// 目标 &gt;400 用 10 层嵌套、否则 5 层，顶层步骤数都压在百级。
        /// </summary>
        /// <returns>图纸夹具与精确预期算子数（顶层 + 分组 1 + 全部 If + 全部叶子）</returns>
        internal static (Harness Harness, int ExpectedNodes) BuildPerfGraph(int targetNodes)
        {
            const int branches = 12;
            int depth = targetNodes > 400 ? 10 : 5;
            const int leavesPerIf = 3;

            int inner = branches * depth * (1 + leavesPerIf);
            int topCount = Math.Max(20, targetNodes - inner - 1);
            int expected = 1 + inner + topCount;

            var h = new Harness();
            h.Flow.FlowName = $"压力图式（{expected} 算子）";

            var capture = h.Add(new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "采集_0"));
            for (int i = 1; i < topCount; i++)
                h.Add(new ActionStep("\uE700", "顶层算子", StubPluginProvider.LeafPlugin, $"顶层_{i}"));

            var par = new ParallelStep("\uE7C1", "并行分组", "VM.CanvasStub.Parallel", "并行_0");
            h.Add(par);
            while (par.Children.Count < branches)
                par.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = $"分支 {par.Children.Count + 1}" });

            ActionStep NewLeaf(string name)
            {
                var leaf = Harness.WithInput(new ActionStep("\uE73E", "尺寸测量", StubPluginProvider.LeafPlugin, name), "In");
                Harness.RawLink(leaf, "In", capture, "Out");
                return leaf;
            }

            int ifNo = 0, leafNo = 0;
            for (int branch = 0; branch < branches; branch++)
            {
                // 自内向外组装嵌套链：每层 If 的 If 分支挂 [叶, 叶, 下一层 If]，Else 分支挂 1 叶
                ConditionStep? innerIf = null;
                for (int level = depth; level >= 1; level--)
                {
                    var ifStep = new ConditionStep("\uE7C1", "结果判断", StubPluginProvider.IfPlugin, $"分档_{ifNo++}");
                    ifStep.Children[0].Steps.Add(NewLeaf($"测量_{leafNo++}"));
                    ifStep.Children[0].Steps.Add(NewLeaf($"测量_{leafNo++}"));
                    if (innerIf != null)
                        ifStep.Children[0].Steps.Add(innerIf);
                    ifStep.Children[1].Steps.Add(NewLeaf($"测量_{leafNo++}"));
                    innerIf = ifStep;
                }

                par.Children[branch].Steps.Add(innerIf!);
            }

            return (h, expected);
        }

        /// <summary>
        /// 场景 H：并行分组 4 条分支、只有分支 1 有内容——真机截图"流程画布太乱"的复原图式。
        /// 分支 1 里串起 If_0（未绑定条件、两分支空）→ For_0（循环体空）→ While_0（循环体未绑定条件）→ Break_0；
        /// 分支 2~4 全空。顶层：图像采集_0 → 胶路检测_0 → 并行分组_0 → 结果汇总_0。
        ///
        /// 旧布局（固定行高 90 / 列距 260）下这里必然互压：If 框宽 356 + 两侧内边距，右缘越过下一列起点 200px 以上，
        /// 直接压进分支 2 的泳道；3 条空泳道又统一贴到内容左上角、同址重叠在嵌套容器上。
        /// 修复后：4 条泳道按列依次排开，嵌套框完整落在分支 1 泳道内，框体/泳道/内容三层按深度叠放。
        /// </summary>
        private static void RenderScenarioH(string path)
        {
            var h = new Harness();
            h.Flow.FlowName = "并行分组·泳道内嵌套（真机图式）";

            var capture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "图像采集_0")
            {
                State = StepState.Success,
                Description = "车间工位 3 面阵相机",
            };
            var bead = new ActionStep("\uE7C1", "胶路检测", StubPluginProvider.LeafPlugin, "胶路检测_0")
            {
                State = StepState.Running,
                Description = "左工位胶路宽度/高度",
            };

            var par = new ParallelStep("\uF0F7", "并行分组", "BuiltIn_Parallel", "并行分组_0")
            {
                Description = "4 工位并行，跑完汇合",
            };
            par.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "分支 3" });
            par.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "分支 4" });

            // 分支 1 = 真机图式：If（未绑定条件、两分支空）→ For（循环体空）→ While（循环体未绑定条件）→ Break
            var ifStep = new ConditionStep("\uE7C1", "结果判断", StubPluginProvider.IfPlugin, "If_0");
            var forStep = new ForStep("\uE722", "循环", StubPluginProvider.ForPlugin, "For_0");
            var whileStep = new WhileStep("\uE722", "条件循环", StubPluginProvider.IfPlugin, "While_0");
            var breakStep = new BreakStep("\uE711", "跳出循环", "VM.CanvasStub.Break", "Break_0");
            par.Children[0].Steps.Add(ifStep);
            par.Children[0].Steps.Add(forStep);
            par.Children[0].Steps.Add(whileStep);
            par.Children[0].Steps.Add(breakStep);

            var summary = new ActionStep("\uE73E", "数据汇总", StubPluginProvider.LeafPlugin, "结果汇总_0");

            h.Add(capture);
            h.Add(bead);
            h.Add(par);
            h.Add(summary);

            Harness.RawLink(bead, "In", capture, "Out");
            Harness.RawLink(par, "In", bead, "Out");
            Harness.RawLink(ifStep, "In", capture, "Out");      // 顶层采集 → 分支内层：合法（进入前已有值）

            var canvas = new FlowCanvasViewModel(h.Workspace, new StubPluginProvider());
            WireViewModelFactory(canvas);

            var host = RenderCore(canvas, path, 1400, 1200);

            // 层次自检（不依赖视觉树，直接用 VM 的层次值）：
            // 嵌套容器框必须压在兄弟泳道之上——旧实现靠集合序，后入分支的泳道会盖住先入分支里的嵌套框
            var nestedFrame = canvas.Nodes.First(n => n.Model == ifStep);
            var siblingLane = canvas.Nodes.First(n => n.Kind == CanvasNodeKind.Lane && ReferenceEquals(n.Branch, par.Children[1]));
            Console.WriteLine(
                $"[render][check] 嵌套框 ZOrder={nestedFrame.ZOrder} > 兄弟泳道 ZOrder={siblingLane.ZOrder}（期望 True）= {nestedFrame.ZOrder > siblingLane.ZOrder}");

            ProbeItemZIndex(host);
            ProbeCanvasEditingEntries(host);
        }

        /// <summary>
        /// 画布"主编辑面"入口自检（视图侧证据）：
        ///  ① 节点右键菜单里有「模块参数…」且命令绑到 OpenModuleParametersCommand（与双击同一入口）；
        ///  ② 容器头带的折叠钮带约定的 x:Name=ContainerCollapseToggle —— 双击豁免的判据就是它，
        ///     改名（或删名）会让"双击折叠钮弹参数窗"的回归悄悄回来（R25 家族的教训）；
        ///  ③ 双击命中路由（反射直调 FlowCanvasView.FindNodeFromSource，与真机同一条代码）：
        ///     算子卡片 → 节点 VM；折叠钮内文字 / 连接点 → null（豁免，别把箭头与拖线抓手吃掉）。
        /// </summary>
        private static void ProbeCanvasEditingEntries(DependencyObject host)
        {
            var menus = new List<ContextMenu>();
            CollectNodeMenus(host, menus);
            var parameterItem = menus
                .SelectMany(m => m.Items.OfType<MenuItem>())
                .FirstOrDefault(m => (m.Header as string) == "步骤参数…");
            var foldBorder = FindDescendant<FrameworkElement>(
                host, d => d is FrameworkElement { Name: "ContainerCollapseToggle" });

            // 双击命中路由：反射调视图的私有静态判定（断言宿主拿不到 internal，与 CheckLinkOrder 同款手法）
            var findNode = typeof(FlowCanvasView).GetMethod(
                "FindNodeFromSource", BindingFlags.Static | BindingFlags.NonPublic);
            var stepText = FindDescendant<TextBlock>(
                host, d => d is TextBlock tb && tb.DataContext is CanvasNodeViewModel { Kind: CanvasNodeKind.Step });
            var foldChild = foldBorder == null ? null : FindDescendant<TextBlock>(foldBorder, _ => true);
            var connector = FindDescendant<Connector>(host, _ => true);

            var hitStep = findNode?.Invoke(null, new object?[] { stepText }) as CanvasNodeViewModel;
            var hitFold = findNode?.Invoke(null, new object?[] { foldChild }) as CanvasNodeViewModel;
            var hitConnector = findNode?.Invoke(null, new object?[] { connector }) as CanvasNodeViewModel;

            Console.WriteLine(
                $"[render][check] 画布节点菜单「步骤参数…」存在={parameterItem != null}（期望 True）；"
                + $"命令绑定={CommandPath(parameterItem)}（期望 OpenModuleParametersCommand）；"
                + $"折叠钮 x:Name=ContainerCollapseToggle 存在={foldBorder != null}（期望 True，双击豁免判据）");
            Console.WriteLine(
                $"[render][check] 双击命中路由：算子卡片→{(hitStep != null ? "节点 VM（开参数）" : "null")}（期望节点 VM）；"
                + $"折叠钮内文字→{(hitFold == null ? "null（豁免）" : "节点 VM（回归！）")}（期望 null）；"
                + $"连接点→{(hitConnector == null ? "null（豁免）" : "节点 VM（回归！）")}（期望 null）");
        }

        /// <summary>收集视觉树里出现过的 ContextMenu（节点模板内联菜单 = 每实例一份）</summary>
        private static void CollectNodeMenus(DependencyObject root, List<ContextMenu> into)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement { ContextMenu: { } menu } && !into.Contains(menu))
                    into.Add(menu);
                CollectNodeMenus(child, into);
            }
        }

        /// <summary>
        /// Panel.ZIndex 绑定自检：ItemContainer 上的 ZIndex 必须等于节点 VM 的 ZOrder（XAML 绑定真的生效），
        /// 且 items 宿主必须是 Panel（Panel.ZIndex 只对 Panel 的子元素排序）。
        /// </summary>
        private static void ProbeItemZIndex(DependencyObject host)
        {
            var items = new List<ItemContainer>();
            CollectDescendants(host, items);

            int mismatched = items.Count(c =>
                c.DataContext is CanvasNodeViewModel vm && Panel.GetZIndex(c) != vm.ZOrder);
            var panel = items.Count > 0 ? VisualTreeHelper.GetParent(items[0]) as Panel : null;

            Console.WriteLine(
                $"[render][check] 节点容器={items.Count}；Panel.ZIndex 与 VM.ZOrder 一致={items.Count > 0 && mismatched == 0}"
                + $"（不一致 {mismatched} 个）；items 宿主={panel?.GetType().Name ?? "?"}（Panel 才吃 Panel.ZIndex）");
        }

        /// <summary>深度优先收集全部命中的后代元素（ZIndex 自检用）</summary>
        private static void CollectDescendants<T>(DependencyObject root, List<T> into) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) into.Add(hit);
                CollectDescendants(child, into);
            }
        }

        /// <summary>FlowCanvasView 的 XAML 带 AutoWireViewModel=True 且在构造期触发——VM 无无参构造，
        /// 用 Prism 的兜底工厂钩子返回预建实例（Register 工厂在 Prism 9 的解析路径里被默认实现抢先）。</summary>
        private static void WireViewModelFactory(FlowCanvasViewModel canvas)
        {
            Prism.Mvvm.ViewModelLocationProvider.SetDefaultViewModelFactory(t =>
                t == typeof(FlowCanvasViewModel)
                    ? canvas
                    : Activator.CreateInstance(t)!);
        }

        private static Border RenderCore(FlowCanvasViewModel canvas, string path, double width, double height)
        {
            var view = new FlowCanvasView();

            var host = new Border
            {
                Width = width,
                Height = height,
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                Child = view,
            };

            host.Measure(new Size(width, height));
            host.Arrange(new Rect(0, 0, width, height));
            host.UpdateLayout();
            PumpFrames(8);

            var editor = FindEditor(view);
            if (editor != null)
            {
                editor.FitToScreen(null);
                if (editor.ViewportZoom > 1.0)
                    editor.ViewportZoom = 1.0;
                host.UpdateLayout();
                PumpFrames(6);
            }

            // 连线自检行：数据线/顺序链/非法/降级/折叠隐藏 五个计数，
            // 出图时一并落 stdout——"图上有告警条但没人知道是哪来的"属可诊断性缺口
            Console.WriteLine(
                $"[render][diag] {Path.GetFileName(path)} 数据线={canvas.DataLinkCount} 顺序链={canvas.OrderLinkCount}"
                + $" 非法={canvas.IllegalLinkCount} 降级={canvas.DeferredLinkCount} 折叠隐藏={canvas.HiddenLinkCount}");

            SavePng(host, path);
            return host;
        }

        /// <summary>把已布局的宿主逐像素渲染成 PNG（画布与流程栏出图共用）。</summary>
        private static void SavePng(FrameworkElement host, string path)
        {
            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(host.ActualWidth),
                (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(host);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path))
                encoder.Save(fs);

            Console.WriteLine($"[render] {path}");
        }

        private static void PumpFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }
        }

        /// <summary>
        /// 出图：流程栏两种拖拽浮层（步骤卡 / 分支卡）竖排，留白 24px 让阴影完整入图。
        /// </summary>
        private static void RenderGhostCards(
            string path,
            DataTemplate? stepTemplate,
            object? stepModel,
            DataTemplate? branchTemplate,
            object? branchModel)
        {
            if (stepTemplate == null || branchTemplate == null || stepModel == null || branchModel == null)
                return;

            var panel = new StackPanel { Margin = new Thickness(24), Orientation = Orientation.Vertical };
            foreach (var (template, model) in new[] { (stepTemplate, stepModel), (branchTemplate, branchModel) })
            {
                var card = (FrameworkElement)template.LoadContent();
                card.DataContext = model;
                card.Margin = new Thickness(0, 0, 0, 16);
                panel.Children.Add(card);
            }

            var stage = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                Child = panel,
            };
            stage.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            stage.Arrange(new Rect(0, 0, stage.DesiredSize.Width, stage.DesiredSize.Height));
            stage.UpdateLayout();
            PumpFrames(1);
            SavePng(stage, path);
        }

        /// <summary>
        /// 把拖拽浮层的模板实例化出来，核对卡片取到的标题/副标题。
        ///
        /// 用 LoadContent 复刻 gong 在独立 Popup 里的物化路径：模板里的 StaticResource
        /// 找不到会在这里当场抛（与真机"一拖就崩"同源，R173）。
        /// </summary>
        private static bool ProbeGhostCard(
            DataTemplate? template,
            object? dataContext,
            string expectedTitle,
            string expectedCategory)
        {
            if (template == null || dataContext == null)
                return false;

            try
            {
                var element = (FrameworkElement)template.LoadContent();
                element.DataContext = dataContext;
                element.Measure(new Size(400, 80));
                element.Arrange(new Rect(0, 0, 400, 80));
                element.UpdateLayout();
                PumpFrames(1);

                var card = element as VisionMaster.Controls.DragGhostCard
                           ?? FindDescendant<VisionMaster.Controls.DragGhostCard>(element, _ => true);

                return card != null
                       && (card.Title ?? "") == (expectedTitle ?? "")
                       && (card.Category ?? "") == (expectedCategory ?? "");
            }
            catch
            {
                return false;
            }
        }

        private static NodifyEditor? FindEditor(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is NodifyEditor editor) return editor;
                var found = FindEditor(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
