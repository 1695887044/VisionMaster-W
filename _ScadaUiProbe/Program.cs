using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using VisionMaster.Views;

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

            Shot(dir, "scada-editor-05-empty.png", 1500, 830, null, createPage: false);
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

            window.Close();
            vm.Deactivate();
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

        private sealed class FakeRuntimeHost : IScadaRuntimeHost
        {
            public bool IsRunning { get; private set; }

            public void Start(ScadaDocument document) => IsRunning = true;

            public void Stop() => IsRunning = false;
        }
    }
}
