using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using VisionMaster.Views;
using VisionMaster.Views.DialogViews;

namespace ScadaUiProbe
{
    /// <summary>
    /// 组态 UI 出图探针：把 ScadaEditorView / ScadaToolboxView 在真窗口里装起来截图。
    /// 输出落到 docs/UI设计基线/app-shell/evidence（随文档走，不会被临时目录清理带走）。
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MergeAppResources(app);
            ViewModelLocationProvider.SetDefaultViewModelFactory(_ => null);

            string dir = OutputDirectory();
            Directory.CreateDirectory(dir);

            try
            {
                string mode = args.Length > 0 ? args[0] : "--all";

                if (mode is "--all" or "--toolbox")
                    RenderToolbox(dir);

                if (mode is "--all" or "--editor")
                    RenderEditorShots(dir);

                if (mode is "--glyphs")
                    ProbeGlyphs();

                if (mode is "--solution")
                    RenderSolutionPages(dir, args.Length > 1 ? args[1] : null);

                if (mode is "--flow-manager")
                    RenderFlowManager(dir);

                if (mode is "--flow-list")
                    RenderFlowList(dir);

                if (mode is "--right-pane-hide")
                    ProbeRightPaneHide();

                if (mode is "--theme-keys")
                    DumpThemeKeys();

                if (mode is "--tab-props")
                    DumpTabProps();

                // 第三轮会审：图元状态出图（只覆盖不依赖运行态上下文的状态，见 ElementStates 的说明）
                if (mode is "--elements")
                    ElementStates.RenderAll(dir);

                Console.WriteLine($"出图目录: {dir}");
                Console.WriteLine("RESULT: OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("RESULT: FAIL");
                Console.WriteLine(ex);
                return 1;
            }
        }

        /// <summary>仓库根下的 docs/UI设计基线/app-shell/evidence。</summary>
        private static string OutputDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VisionMaster.sln")))
                dir = dir.Parent;

            string root = dir?.FullName ?? AppContext.BaseDirectory;
            return Path.Combine(root, "docs", "UI设计基线", "app-shell", "evidence");
        }

        private static void MergeAppResources(Application app)
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml"),
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml"),
            });
            app.Resources["Icon"] = new FontFamily(
                "pack://application:,,,/UI;component/Asserts/FontFamilys/Font Awesome 6 Pro-Solid-900.otf#Font Awesome 6 Pro Solid");
        }

        // ================================================================ 字形探测

        /// <summary>
        /// 逐个码点问字体"有没有这个字形"。
        /// 为什么必须问：图标字体里没有的码点会渲染成豆腐块或空白，而编译期、静态扫描全绿——
        /// 只有真机上那一眼才看得见。这里用 GlyphTypeface.CharacterToGlyphMap 直接查表，不靠猜。
        /// </summary>
        private static void ProbeGlyphs()
        {
            var fontFile = Path.Combine(
                Path.GetDirectoryName(OutputDirectory())!, "..", "..", "..",
                "UI", "Controls", "Asserts", "FontFamilys", "Font Awesome 6 Pro-Solid-900.otf");

            fontFile = Path.GetFullPath(fontFile);

            if (!File.Exists(fontFile))
            {
                Console.WriteLine($"找不到字体文件: {fontFile}");
                return;
            }

            var glyphTypeface = new GlyphTypeface(new Uri(fontFile));

            // 候选：按语义分组，码点都是 FA6 Solid 里我打算用的那些
            var candidates = new (string Name, int Code)[]
            {
                ("square", 0xF0C8), ("circle", 0xF111), ("font", 0xF031), ("text-height", 0xF034),
                ("image", 0xF1C5), ("bars", 0xF0C9), ("table-cells", 0xF0C9), ("th-large", 0xF00A),
                ("toggle-on", 0xF205), ("check-square", 0xF14A), ("sliders", 0xF1DE), ("sliders-h", 0xF1DE),
                ("lightbulb", 0xF0EB), ("bolt", 0xF0E7), ("clock", 0xF017), ("chart-bar", 0xF080),
                ("pen", 0xF304), ("tachometer", 0xF0E4), ("chart-line", 0xF201), ("terminal", 0xF120),
                ("caret-down", 0xF0D7), ("sitemap", 0xF0E8), ("play", 0xF04B), ("gear", 0xF013),
                ("exchange", 0xF0EC), ("arrows-alt-h", 0xF337), ("database", 0xF1C0), ("bell", 0xF0F3),
                ("check", 0xF00C), ("gamepad", 0xF11B), ("table", 0xF0CE), ("cubes", 0xF1B3),
                ("gauge-simple", 0xF629), ("gauge", 0xF624), ("wave-square", 0xF83E), ("chart-simple", 0xF080),
                ("grip-vertical", 0xF58D), ("ellipsis-vertical", 0xF142), ("circle-dot", 0xF192),
                ("diagram-project", 0xF542), ("bezier-curve", 0xF55B), ("draw-polygon", 0xF5EE),
                ("arrow-right-arrow-left", 0xF0EC), ("rectangle-list", 0xF022), ("border-all", 0xF84C),
                ("bars-staggered", 0xF550), ("list", 0xF03A), ("list-check", 0xF0AE), ("grip", 0xF58D),
                ("toggle-off", 0xF204), ("stopwatch", 0xF2F2), ("clock-rotate-left", 0xF1DA),
                ("square-check", 0xF14A), ("window-maximize", 0xF2D0), ("layer-group", 0xF5FD),
                ("chart-column", 0xF080), ("table-list", 0xF00B), ("diagram-successor", 0xF5FE),
            };

            Console.WriteLine("---- 字形存在性 ----");
            foreach (var (name, code) in candidates)
            {
                bool ok = glyphTypeface.CharacterToGlyphMap.ContainsKey(code);
                Console.WriteLine($"{(ok ? "有" : "无")}  U+{code:X4}  {name}");
            }
        }

        // ================================================================ 图元工具箱

        private static void RenderToolbox(string dir)
        {
            var vm = new ScadaToolboxViewModel();
            ViewModelLocationProvider.Register<ScadaToolboxView>(() => vm);

            Capture(dir, "toolbox-300.png", 300, 760, () => new ScadaToolboxView());
            Capture(dir, "toolbox-260.png", 260, 700, () => new ScadaToolboxView());

            DumpWidths(dir);

            Console.WriteLine($"  图元 {vm.TotalCount} 种 / {vm.Groups.Count} 个分类：" +
                string.Join("、", vm.Groups.Select(g => $"{g.Name}({g.Items.Count})")));
        }

        /// <summary>量一遍宽度链：视图 → ScrollViewer → ItemsControl → 第一行。宽度溢出时一眼看出断在哪层。</summary>
        private static void DumpWidths(string dir)
        {
            var view = new ScadaToolboxView();
            var window = Show(view, 300, 760);
            Pump(400);

            var scroll = Find<ScrollViewer>(view);
            var firstRow = FindAll<Border>(view).FirstOrDefault(b => Math.Abs(b.ActualHeight - 30) < 0.5);

            Console.WriteLine($"  宽度链: 视图={view.ActualWidth:0.#} " +
                $"滚动区={scroll?.ActualWidth:0.#} 视口={scroll?.ViewportWidth:0.#} " +
                $"行={firstRow?.ActualWidth:0.#}");

            window.Close();
        }

        private static T Find<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T hit) return hit;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = Find<T>(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        private static System.Collections.Generic.List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            var list = new System.Collections.Generic.List<T>();
            if (root is T hit) list.Add(hit);
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
                list.AddRange(FindAll<T>(VisualTreeHelper.GetChild(root, i)));
            return list;
        }

        // ================================================================ 流程条：调用关系徽标

        /// <summary>
        /// 用 340 宽的真实尺寸渲染流程条两态：13 条（224，max）与 6 条（112，min）。
        /// 检查 24px 胶囊、单枚角色/调用徽标、选中左条与底栏「全部」是否挤掉流程名。
        /// </summary>
        private static void RenderFlowList(string dir)
        {
            FlowModel F(string name, FlowRole role, FlowInvokeType invoke)
                => new() { FlowName = name, Role = role, InvokeType = invoke };

            var all = new[]
            {
                F("Home", FlowRole.Home, FlowInvokeType.Manual),
                F("Main", FlowRole.Main, FlowInvokeType.Manual),
                F("ST1_LoadC", FlowRole.None, FlowInvokeType.Http),
                F("ST2_Position", FlowRole.None, FlowInvokeType.Timer),
                F("ST3_Measure", FlowRole.None, FlowInvokeType.Http),
                F("ST4_Surface", FlowRole.None, FlowInvokeType.Variable),
                F("ST5_Clamp", FlowRole.None, FlowInvokeType.Http),
                F("ST6_Weld", FlowRole.None, FlowInvokeType.Http),
                F("ST7_Inspect", FlowRole.None, FlowInvokeType.Http),
                F("ST8_Pack", FlowRole.None, FlowInvokeType.Http),
                F("ST9_Unload", FlowRole.None, FlowInvokeType.Http),
                F("AlarmScan", FlowRole.None, FlowInvokeType.Http),
                F("RecipePush", FlowRole.None, FlowInvokeType.Subroutine),
            };

            RenderFlowListShot(dir, all, 340, 224, "flow-list-call-badges.png");
            RenderFlowListShot(dir, all.Take(6).ToArray(), 340, 112, "flow-list-v2-112.png");
        }

        private static void RenderFlowListShot(string dir, FlowModel[] flows, int width, int height, string file)
        {
            var selected = flows.FirstOrDefault(f => f.Role == FlowRole.Main);
            var view = new FlowListView
            {
                Width = width,
                Height = height,
                DataContext = new FlowListProbeContext
                {
                    Workspace = new
                    {
                        CurrentSolution = new { Flows = flows },
                    },
                    SelectFlow = selected,
                },
            };

            // 无边框 + SizeToContent：窗口客户区 = 视图 340×112/224，不扣标题栏与边框。
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = -4000,
                ShowInTaskbar = false,
                Content = view,
            };
            window.Show();
            Pump(600);

            Save(Render(view, (int)view.ActualWidth, (int)view.ActualHeight),
                Path.Combine(dir, file));
            Console.WriteLine($"  {file}  {view.ActualWidth:0}×{view.ActualHeight:0}");

            window.Close();
        }

        /// <summary>探针最小上下文：流程集合 + 选中流程（ListBox 的 SelectedItem 需要可写）。</summary>
        private sealed class FlowListProbeContext
        {
            public object Workspace { get; init; } = null!;
            public FlowModel? SelectFlow { get; set; }
        }

        // ================================================================ 流程管理：调用关系徽标

        /// <summary>
        /// 用四条假流程渲染流程管理表格，专门看“循环 / 外部 / 子 / 手动”四种调用关系徽标。
        /// 只做视觉探针：DataContext 是一个最小匿名对象，不依赖 FlowManagerViewModel 的 DI。
        /// </summary>
        private static void RenderFlowManager(string dir)
        {
            var flows = new object[]
            {
                new { Flow = new FlowModel { FlowName = "Main", Role = FlowRole.Main, InvokeType = FlowInvokeType.Manual } },
                new { Flow = new FlowModel { FlowName = "Station_Load", Role = FlowRole.None, InvokeType = FlowInvokeType.Subroutine } },
                new { Flow = new FlowModel { FlowName = "Line2_HTTP", Role = FlowRole.None, InvokeType = FlowInvokeType.Http } },
                new { Flow = new FlowModel { FlowName = "ManualFlow", Role = FlowRole.None, InvokeType = FlowInvokeType.Manual } },
            };

            var view = new FlowManagerView { DataContext = new { Flows = flows } };
            var window = Show(view, 1500, 760);
            Pump(600);

            Save(Render(view, (int)view.ActualWidth, (int)view.ActualHeight),
                Path.Combine(dir, "flow-manager-call-badges.png"));
            Console.WriteLine($"  flow-manager-call-badges.png  {view.ActualWidth:0}×{view.ActualHeight:0}");

            window.Close();
        }

        // ================================================================ 方案组态出图

        /// <summary>
        /// 从 .vms 里只取 Scada 段，逐页渲染成 PNG。
        /// 不整份反序列化方案：流程步骤里带插件类型，需要先把 Modules 下的插件程序集装进来；
        /// 组态预览与算子无关，只解析画面/图元就够了。
        /// </summary>
        private static void RenderSolutionPages(string dir, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Console.WriteLine($"找不到方案文件: {path}");
                return;
            }

            var root = JObject.Parse(File.ReadAllText(path));
            var token = root["Scada"];
            if (token == null)
            {
                Console.WriteLine("方案里没有 Scada 段");
                return;
            }

            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var doc = token.ToObject<ScadaDocument>(JsonSerializer.Create(settings));
            if (doc == null)
            {
                Console.WriteLine("Scada 段反序列化失败");
                return;
            }

            doc.EnsureIdentity();
            VisionMaster.Scada.Controls.Charts.ScadaChartElements.Register();

            var theme = new ResourceDictionary
            {
                Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
            };

            string solution = Path.GetFileNameWithoutExtension(path);
            int index = 1;
            foreach (var page in doc.Pages)
            {
                var canvas = new ScadaCanvas
                {
                    PageWidth = page.Width,
                    PageHeight = page.Height,
                    GridSize = page.GridSize,
                    SnapToGrid = page.SnapToGrid,
                    ItemsSource = page.Elements,
                    Page = page,
                    ShowGrid = false,
                };
                canvas.Style = (Style)theme[typeof(ScadaCanvas)];

                // 方案图片（Hmi.Picture 的 SourcePath）在真机上由宿主注入解析器；探针没有宿主，
                // 这里按"方案目录 + Image\ 相对路径"补一个，好让带图图元在证据图里真的出像素。
                // 解析不到返回 null → 控件回退占位，与真机"图丢了"的表现一致。
                string solutionDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
                canvas.ImageAssetResolver = rel =>
                {
                    if (string.IsNullOrWhiteSpace(rel)) return null;
                    var full = Path.IsPathRooted(rel) ? rel : Path.Combine(solutionDir, rel);
                    if (!File.Exists(full)) return null;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(full, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                };

                // 放屏幕外：HwndHost/渲染目标照常建立，但不会在桌面上闪。
                var window = new Window
                {
                    Width = Math.Max(200, page.Width + 2),
                    Height = Math.Max(200, page.Height + 2),
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000,
                    Top = 0,
                    ShowInTaskbar = false,
                    Content = canvas,
                };
                window.Show();
                Pump(600);

                string safeName = string.Join("_", page.Name.Split(Path.GetInvalidFileNameChars()));
                string file = $"{solution}-{index:00}-{safeName}.png";
                Save(Render(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight), Path.Combine(dir, file));
                Console.WriteLine($"  {file}  {canvas.ActualWidth:0}×{canvas.ActualHeight:0}  图元 {page.Elements.Count}");

                window.Close();
                index++;
            }
        }

        // ================================================================ 组态编辑器

        private static void RenderEditorShots(string dir)
        {
            Shot(dir, "scada-editor-01-default.png", 1500, 830, (view, vm, canvas) =>
                Select(canvas, Array.Empty<ScadaElement>()));

            Shot(dir, "scada-editor-02-single.png", 1500, 830, (view, vm, canvas) =>
                Select(canvas, new[] { vm.SelectedPage!.Elements[0] }));

            Shot(dir, "scada-editor-03-multi.png", 1500, 830, (view, vm, canvas) =>
                Select(canvas, vm.SelectedPage!.Elements.Take(4).ToList()));

            Shot(dir, "scada-editor-04-narrow.png", 1010, 700, (view, vm, canvas) =>
                Select(canvas, vm.SelectedPage!.Elements.Take(2).ToList()));

            // 顶栏折行：宽度不够时右组整组掉到第二行（见 ScadaEditorView.xaml 顶栏注释）。
            // 700 是"窗口 1120 最小宽 + 右侧属性/图层面板都停靠"时文档区的大致宽度；
            // 520 再压一档，验"连一组都放不下时组内按钮各自折行、任何宽度都不裁切"。
            Shot(dir, "scada-editor-06-toolbar-700.png", 700, 620, null);
            Shot(dir, "scada-editor-07-toolbar-520.png", 520, 620, null);

            ProbeToolbarResize(dir);

            Shot(dir, "scada-editor-05-empty.png", 1500, 830, null, createPage: false);
        }

        /// <summary>
        /// 顶栏折行的"活尺寸变化"探针：同一个视图先宽后窄再变宽。
        ///
        /// 单独验这一条，是因为 <see cref="Shot"/> 每张图都新建视图，走不到
        /// "ToolbarSpacer 已经撑开之后再把窗口拖窄"那条路径——而它正是用户拖分隔条时天天走的，
        /// 也是"改尺寸 → 布局 → 再改尺寸"死循环最容易冒出来的地方（真循环就是界面卡死）。
        /// </summary>
        private static void ProbeToolbarResize(string dir)
        {
            var workspace = new WorkspaceContext();
            var doc = new ScadaDocument();
            var page = doc.AddPage("主画面");
            page.Width = 1280;
            page.Height = 800;
            workspace.SwitchSolution(new SolutionModel { Scada = doc });

            var vm = new ScadaEditorViewModel(workspace, new FakeRuntimeHost());
            vm.Activate();
            vm.SelectedPage = page;
            ViewModelLocationProvider.Register<ScadaEditorView>(() => vm);

            var view = new ScadaEditorView();
            var window = Show(view, 1500, 700);
            Pump(500);

            foreach (int width in new[] { 1500, 700, 1500, 520, 1500 })
            {
                window.Width = width;
                Pump(300);

                string state = view.FindName("ToolbarPanel") is WrapPanel panel
                    ? $"{panel.ActualWidth:0.#}×{panel.ActualHeight:0.#}"
                    : "找不到 ToolbarPanel";
                Console.WriteLine($"    窗口宽 {width,4} → 顶栏 {state}");
            }

            Save(Render(view, (int)view.ActualWidth, (int)view.ActualHeight),
                Path.Combine(dir, "scada-editor-08-resize-back.png"));

            window.Close();
            vm.Deactivate();
        }

        private static void Shot(
            string dir,
            string fileName,
            int width,
            int height,
            Action<ScadaEditorView, ScadaEditorViewModel, ScadaCanvas> arrange,
            bool createPage = true)
        {
            var workspace = new WorkspaceContext();
            var doc = new ScadaDocument();
            var page = createPage ? doc.AddPage("主画面") : null;

            if (page != null)
            {
                page.Width = 1280;
                page.Height = 800;
            }

            workspace.SwitchSolution(new SolutionModel { Scada = doc });

            var vm = new ScadaEditorViewModel(workspace, new FakeRuntimeHost());
            vm.Activate();
            vm.SelectedPage = page;

            if (page != null)
            {
                vm.AddElement("Hmi.Tank", new Point(60, 96));
                vm.AddElement("Hmi.Pump", new Point(380, 150));
                vm.AddElement("Hmi.Valve", new Point(540, 180));
                vm.AddElement("Hmi.Gauge", new Point(640, 90));
                vm.AddElement("Hmi.Button", new Point(620, 330));
                vm.AddElement("Hmi.Text", new Point(60, 40));
            }

            ViewModelLocationProvider.Register<ScadaEditorView>(() => vm);

            var view = new ScadaEditorView();
            var window = Show(view, width, height);
            Pump(600);

            var canvas = FindCanvas(view);
            if (canvas != null && arrange != null)
            {
                arrange(view, vm, canvas);
                Pump(250);
            }

            Save(Render(view, (int)view.ActualWidth, (int)view.ActualHeight), Path.Combine(dir, fileName));
            Console.WriteLine($"  {fileName}  选中={canvas?.SelectedElements.Count ?? -1}  缩放={canvas?.Zoom.ToString("0.##") ?? "-"}");

            // 顶栏高度就是"折没折行"的判据：单行 = 32、折两行 ≈ 68（44 是整行含居中留白的最小高度）
            if (view.FindName("ToolbarPanel") is WrapPanel toolbar)
            {
                string rowHeight = toolbar.Parent is FrameworkElement host
                    ? host.ActualHeight.ToString("0.#")
                    : "-";
                Console.WriteLine($"    顶栏 {toolbar.ActualWidth:0.#}×{toolbar.ActualHeight:0.#}（行高 {rowHeight}）" +
                    $"（{(toolbar.ActualHeight > 40 ? "已折行" : "单行")}）");
            }

            window.Close();
            vm.Deactivate();
        }

        // ================================================================ 右栏隐藏

        /// <summary>
        /// 复现"把工具箱隐藏后右列不收"：搭一个与 Shell 同构的 DockingManager
        /// （左 340/320、中文档、右 320/300；右列 1 可见 + 3 隐藏），
        /// 把唯一可见的「工具箱」IsVisible=false，量右列 ActualWidth / DockWidth / DockMinWidth。
        /// </summary>
        private static void ProbeRightPaneHide()
        {
            var dock = new AvalonDock.DockingManager { AllowMixedOrientation = true };
            dock.Theme = new ProbeDockTheme();
            var root = new AvalonDock.Layout.LayoutRoot();
            var panel = new AvalonDock.Layout.LayoutPanel { Orientation = Orientation.Horizontal };
            root.RootPanel = panel;

            AvalonDock.Layout.LayoutAnchorable Make(string id, string title, Brush bg)
                => new()
                {
                    Title = title,
                    ContentId = id,
                    AutoHideWidth = 300,
                    Content = new Border
                    {
                        Background = bg,
                        Child = new TextBlock { Text = title, Margin = new Thickness(8) },
                    },
                };

            var leftPane = new AvalonDock.Layout.LayoutAnchorablePane
            {
                DockWidth = new GridLength(340),
                DockMinWidth = 320,
            };
            leftPane.Children.Add(Make("Panel_ProcessView", "流程导航", Brushes.Gainsboro));

            var docPane = new AvalonDock.Layout.LayoutDocumentPane();
            docPane.Children.Add(new AvalonDock.Layout.LayoutDocument
            {
                Title = "视觉图像",
                ContentId = "Panel_ImageView",
                Content = new Border { Background = Brushes.Black },
            });

            var rightPane = new AvalonDock.Layout.LayoutAnchorablePane
            {
                DockWidth = new GridLength(320),
                DockMinWidth = 300,
            };
            var tool = Make("Panel_ToolView", "工具箱", Brushes.WhiteSmoke);
            var prop = Make("Panel_ScadaPropertyView", "属性", Brushes.WhiteSmoke);
            var layer = Make("Panel_ScadaLayerView", "图层", Brushes.WhiteSmoke);
            var elem = Make("Panel_ScadaToolboxView", "图元工具箱", Brushes.WhiteSmoke);
            rightPane.Children.Add(tool);
            rightPane.Children.Add(prop);
            rightPane.Children.Add(layer);
            rightPane.Children.Add(elem);

            panel.Children.Add(leftPane);
            panel.Children.Add(docPane);
            panel.Children.Add(rightPane);

            dock.Layout = root;

            var window = new Window
            {
                Width = 1310,
                Height = 829,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = -4000,
                ShowInTaskbar = false,
                Content = dock,
            };
            window.Show();
            Pump(300);

            // 非组态态：只留工具箱
            prop.IsVisible = false;
            layer.IsVisible = false;
            elem.IsVisible = false;
            Pump(300);

            Console.WriteLine(
                $"[before] right.DockWidth={rightPane.DockWidth} DockMinWidth={rightPane.DockMinWidth} " +
                $"right.Visible={rightPane.IsVisible} tool.Visible={tool.IsVisible} tool.Hidden={tool.IsHidden} " +
                $"pane.Children={rightPane.ChildrenCount}");
            Console.WriteLine($"[before] center.DockWidth={docPane.DockWidth} window={window.ActualWidth:0.#}");
            DumpRootGrid(dock, "[before]");
            DumpPaneControls(dock, "[before]");

            tool.IsVisible = false;
            Pump(500);

            Console.WriteLine(
                $"[after]  right.DockWidth={rightPane.DockWidth} DockMinWidth={rightPane.DockMinWidth} " +
                $"right.Visible={rightPane.IsVisible} tool.Visible={tool.IsVisible} tool.Hidden={tool.IsHidden} " +
                $"tool.AutoHidden={tool.IsAutoHidden} pane.Children={rightPane.ChildrenCount}");
            Console.WriteLine($"[after]  center.DockWidth={docPane.DockWidth} window={window.ActualWidth:0.#}");
            DumpRootGrid(dock, "[after] ");
            DumpPaneControls(dock, "[after] ");
            Console.WriteLine($"[after]  tool.TabItem={tool.TabItem?.GetType().FullName ?? "null"}");
            if (tool.TabItem is Control tabControl)
                Console.WriteLine($"          tabItem template={tabControl.Template?.GetType().Name} actual={tabControl.ActualWidth:0.#}x{tabControl.ActualHeight:0.#}");
            foreach (Window w in Application.Current.Windows)
                Console.WriteLine($"          window {w.GetType().FullName} {w.ActualWidth:0.#}x{w.ActualHeight:0.#} visible={w.IsVisible}");
            foreach (var element in FindAll<FrameworkElement>(dock).Where(e => e.GetType().Name.Contains("TabItem")))
            {
                Console.WriteLine($"          dock tab {element.GetType().Name} actual={element.ActualWidth:0.#}x{element.ActualHeight:0.#} " +
                    $"model={element.GetType().GetProperty("Model")?.GetValue(element)?.GetType().Name}");
            }

            var shotDir = Path.Combine(@"D:\C#\VM\artifacts\render", DateTime.Now.ToString("yyyyMMdd"));
            Directory.CreateDirectory(shotDir);
            Save(Render(dock, (int)dock.ActualWidth, (int)dock.ActualHeight),
                Path.Combine(shotDir, "right-pane-hide-after.png"));
            Console.WriteLine($"  right-pane-hide-after.png  {dock.ActualWidth:0}×{dock.ActualHeight:0}");

            // 复现"点胶囊钉回"：ShowPanel 后工具箱应回到右侧 LayoutAnchorablePane，不能变成中央文档。
            try
            {
                tool.Show();
                tool.IsSelected = true;
                tool.IsActive = true;
                Pump(500);
                Console.WriteLine(
                    $"[show] tool.Parent={tool.Parent?.GetType().Name ?? "null"} visible={tool.IsVisible} " +
                    $"right.Children={rightPane.ChildrenCount} right.Visible={rightPane.IsVisible}");
                foreach (var doc in FindAll<FrameworkElement>(dock).Where(e => e.GetType().Name.Contains("TabItem")))
                {
                    Console.WriteLine(
                        $"          tab {doc.GetType().Name} model={doc.GetType().GetProperty("Model")?.GetValue(doc)?.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[show] 抛异常：{ex.GetType().Name} {ex.Message}");
            }

            tool.IsVisible = true;
            Pump(500);
            Console.WriteLine(
                $"[restore] right.DockWidth={rightPane.DockWidth} DockMinWidth={rightPane.DockMinWidth} " +
                $"tool.Visible={tool.IsVisible} tool.AutoHidden={tool.IsAutoHidden}");

            window.Close();
        }

        /// <summary>打印根布局面板的 3 列实际宽度（左 / 中 / 右），确认空列是否真的收成 0。</summary>
        private static void DumpRootGrid(DependencyObject root, string tag)
        {
            foreach (var grid in FindAll<Grid>(root))
            {
                if (grid.ColumnDefinitions.Count != 3 || grid.ActualWidth < 500) continue;
                var cols = string.Join(" | ", grid.ColumnDefinitions.Select(c => $"{c.Width}:{c.ActualWidth:0.#}"));
                Console.WriteLine($"{tag} grid {grid.ActualWidth:0.#}x{grid.ActualHeight:0.#} cols[{cols}]");
            }
        }

        /// <summary>列出 VS2013 主题里与 AutoHide / Tab 相关的资源键，找可覆盖的 auto-hide tab 样式。</summary>
        private static void DumpThemeKeys()
        {
            var dict = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/AvalonDock.Themes.VS2013;component/LightTheme.xaml"),
            };
            DumpDict(dict, "");
        }

        private static void DumpDict(ResourceDictionary dict, string indent)
        {
            foreach (var key in dict.Keys)
            {
                var text = key is System.Windows.ComponentResourceKey component
                    ? component.ResourceId?.ToString()
                    : key?.ToString();
                if (text == null) continue;
                if (text.Contains("AutoHide") || text.Contains("Tab"))
                    Console.WriteLine($"{indent}{text} -> {dict[key]?.GetType().Name}");
            }

            foreach (var merged in dict.MergedDictionaries)
                DumpDict(merged, indent + "  ");
        }

        /// <summary>打印 tab item / anchorable 的属性与主题模板触发器，找 auto-hide 的判据。</summary>
        private static void DumpTabProps()
        {
            var tabType = typeof(AvalonDock.Controls.LayoutAnchorableTabItem);
            Console.WriteLine("== LayoutAnchorableTabItem props ==");
            foreach (var p in tabType.GetProperties())
                Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");

            Console.WriteLine("== LayoutAnchorable auto/hide props ==");
            foreach (var p in typeof(AvalonDock.Layout.LayoutAnchorable).GetProperties()
                .Where(p => p.Name.Contains("Auto") || p.Name.Contains("Hide")
                    || p.Name.Contains("Visible") || p.Name.Contains("Tab") || p.Name.Contains("Parent")))
            {
                Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
            }

            var dict = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/AvalonDock.Themes.VS2013;component/LightTheme.xaml"),
            };
            var style = dict[tabType] as Style;
            Console.WriteLine($"== style for {tabType.Name}: found={style != null} setters={style?.Setters.Count} triggers={style?.Triggers.Count}");
            if (style == null) return;

            foreach (var setter in style.Setters.OfType<Setter>())
                Console.WriteLine($"  setter {setter.Property?.Name}");

            var templateSetter = style.Setters.OfType<Setter>()
                .FirstOrDefault(s => s.Property == System.Windows.Controls.Control.TemplateProperty);
            if (templateSetter?.Value is not ControlTemplate template) return;

            Console.WriteLine($"  template visual={template.VisualTree?.Type?.Name}");
            foreach (var trigger in template.Triggers)
            {
                var text = trigger.ToString();
                if (trigger is DataTrigger dataTrigger)
                    text += $" binding={(dataTrigger.Binding as System.Windows.Data.Binding)?.Path?.Path} value={dataTrigger.Value}";
                if (trigger is Trigger propertyTrigger)
                    text += $" property={propertyTrigger.Property?.Name} value={propertyTrigger.Value}";
                Console.WriteLine($"    trigger {trigger.GetType().Name} {text}");
            }

            DumpFactory(template.VisualTree, "    ");
        }

        private static void DumpFactory(FrameworkElementFactory factory, string indent)
        {
            if (factory == null) return;
            Console.WriteLine($"{indent}{factory.Type?.Name} name={factory.Name}");
            var child = factory.FirstChild;
            while (child != null)
            {
                DumpFactory(child, indent + "  ");
                child = child.NextSibling;
            }
        }

        /// <summary>打印 AvalonDock 各 pane 控件的实际宽度与 Model 子项，确认空 pane 是否收成 0。</summary>
        private static void DumpPaneControls(DependencyObject root, string tag)
        {
            foreach (var element in FindAll<FrameworkElement>(root))
            {
                if (!element.GetType().Name.Contains("LayoutAnchorablePaneControl")) continue;

                var model = element.GetType().GetProperty("Model")?.GetValue(element);
                var ids = model is AvalonDock.Layout.LayoutAnchorablePane pane
                    ? string.Join(",", pane.Children.Select(c => c.ContentId))
                    : "(?)";
                Console.WriteLine(
                    $"{tag} {element.GetType().Name} actual={element.ActualWidth:0.#} " +
                    $"visible={element.IsVisible} model={model?.GetType().Name} ids=[{ids}]");
            }
        }

        // ================================================================ 公共

        private static void Capture(string dir, string fileName, int width, int height, Func<FrameworkElement> factory)
        {
            var view = factory();
            var window = Show(view, width, height);
            Pump(500);

            Save(Render(view, (int)view.ActualWidth, (int)view.ActualHeight), Path.Combine(dir, fileName));
            Console.WriteLine($"  {fileName}  {view.ActualWidth:0}×{view.ActualHeight:0}");

            window.Close();
        }

        private static Window Show(FrameworkElement content, int width, int height)
        {
            var window = new Window
            {
                Width = width,
                Height = height,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 40,
                Top = 40,
                ShowInTaskbar = false,
                Topmost = true,
                Content = content,
            };

            window.Show();
            return window;
        }

        private static void Select(ScadaCanvas canvas, System.Collections.Generic.IReadOnlyList<ScadaElement> items)
        {
            var method = typeof(ScadaCanvas).GetMethod("SetSelection",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method!.Invoke(canvas, new object[] { items });
        }

        private static ScadaCanvas FindCanvas(DependencyObject root)
        {
            if (root is ScadaCanvas canvas) return canvas;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FindCanvas(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        private static RenderTargetBitmap Render(Visual visual, int width, int height)
        {
            var bitmap = new RenderTargetBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static void Save(BitmapSource bitmap, string path)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var stopper = new DispatcherTimer(DispatcherPriority.Send)
            {
                Interval = TimeSpan.FromMilliseconds(milliseconds),
            };
            stopper.Tick += (_, _) =>
            {
                stopper.Stop();
                frame.Continue = false;
            };
            stopper.Start();
            Dispatcher.PushFrame(frame);
        }

        private sealed class ProbeDockTheme : AvalonDock.Themes.DictionaryTheme
        {
            public ProbeDockTheme()
                : base(new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/VisionMaster;component/Themes/DockThemes/FluentLight.xaml",
                        UriKind.Absolute),
                })
            {
            }
        }

        private sealed class FakeRuntimeHost : IScadaRuntimeHost
        {
            public bool IsRunning { get; private set; }

            public void Start(ScadaDocument document) => IsRunning = true;

            public void Stop() => IsRunning = false;
        }
    }
}
