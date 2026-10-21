using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
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

namespace ScadaEditorProbe
{
    /// <summary>
    /// 组态编辑器探针：
    ///   ① 离屏渲染出图（与 docs/UI设计基线/app-shell/design-v1.html 的 .scada-view 对照）；
    ///   ② 真窗口 + Win32 鼠标注入，复现"空白处拖拽框选"。
    /// </summary>
    internal static class Program
    {
        private const int ViewWidth = 1010;
        private const int ViewHeight = 760;

        [STAThread]
        private static int Main(string[] args)
        {
            bool interact = args.Length > 0 && args[0] == "--interact";
            bool shots = args.Length > 0 && args[0] == "--shots";

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MergeAppResources(app);
            ViewModelLocationProvider.SetDefaultViewModelFactory(_ => null);

            try
            {
                if (interact)
                {
                    RunMarqueeCheck();
                }
                else if (shots)
                {
                    RenderShotMatrix();
                }
                else
                {
                    int w = args.Length > 1 && int.TryParse(args[1], out int parsedW) ? parsedW : ViewWidth;
                    int h = args.Length > 2 && int.TryParse(args[2], out int parsedH) ? parsedH : ViewHeight;
                    RenderEditor(w, h);
                }

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

        // ================================================================ ① 渲染

        private static void RenderEditor(int width = ViewWidth, int height = ViewHeight)
        {
            var (view, vm) = BuildEditor();

            // 必须放进真窗口：视图的"首次入树 → 适应窗口"挂在 Loaded 上，
            // 只 Measure/Arrange 的宿主不会触发它（画布会停在 100%，看不到工作台）。
            var window = new Window
            {
                Width = width,
                Height = height,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 40,
                Top = 40,
                ShowInTaskbar = false,
                Topmost = true,
                Content = view,
            };

            window.Show();
            Pump(700);

            string dir = Path.Combine(AppContext.BaseDirectory, "out");
            Directory.CreateDirectory(dir);
            Save(Render(view, (int)view.ActualWidth, (int)view.ActualHeight),
                Path.Combine(dir, "scada-editor-current.png"));

            var canvas = FindCanvas(view);
            if (canvas != null)
            {
                Console.WriteLine($"画布尺寸: {canvas.ActualWidth:0.#} x {canvas.ActualHeight:0.#}");
                Console.WriteLine($"画布 Background: {Describe(canvas.Background)}");
                Console.WriteLine($"画布 PageBackground: {Describe(canvas.PageBackground)}");
                Console.WriteLine($"缩放: {canvas.Zoom:0.###}");
            }

            Console.WriteLine($"证据: {Path.Combine(dir, "scada-editor-current.png")}");
            window.Close();
            vm.Deactivate();
        }

        // ================================================================ ② 框选

        /// <summary>
        /// 真窗口 + Win32 鼠标注入：在画布空白处按住左键拖一段，看 SelectedElements 有没有变化。
        /// 光看代码看不出问题——这里要的是"用户那一下到底发生了什么"。
        /// </summary>
        private static void RunMarqueeCheck()
        {
            var (view, vm) = BuildEditor();

            var window = new Window
            {
                Width = ViewWidth,
                Height = ViewHeight,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 40,
                Top = 40,
                ShowInTaskbar = false,
                Topmost = true,
                Content = view,
            };

            window.Show();
            Pump(600);

            var canvas = FindCanvas(view)
                ?? throw new InvalidOperationException("找不到 ScadaCanvas");

            Console.WriteLine($"画布实际尺寸: {canvas.ActualWidth:0.#} x {canvas.ActualHeight:0.#}");
            Console.WriteLine($"IsReadOnly={canvas.IsReadOnly} IsEnabled={canvas.IsEnabled} HitTestVisible={canvas.IsHitTestVisible}");
            Console.WriteLine($"图元数量: {vm.SelectedPage?.Elements.Count}");
            Console.WriteLine($"初始选中: {canvas.SelectedElements.Count}");

            int down = 0, move = 0, up = 0;
            canvas.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
                new System.Windows.Input.MouseButtonEventHandler((_, _) => down++), true);
            canvas.PreviewMouseMove += (_, _) => move++;
            canvas.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,
                new System.Windows.Input.MouseButtonEventHandler((_, _) => up++), true);

            // 拖一个能罩住左上角三四个图元的框（用画布本地 DIP 坐标，避开 DPI 换算误差）
            var from = new Point(10, 10);
            var to = new Point(600, 400);
            Point fromScreen = canvas.PointToScreen(from);
            Point toScreen = canvas.PointToScreen(to);

            Console.WriteLine($"起手(屏幕): {fromScreen.X:0},{fromScreen.Y:0} → 终点 {toScreen.X:0},{toScreen.Y:0}");
            Console.WriteLine($"该点下的元素: {DescribeHit(canvas, from)}");

            SetForegroundWindow(new WindowInteropHelper(window).Handle);
            Pump(150);

            // 预热：窗口未激活时第一次按下会被"激活窗口"吃掉（探针假象，不是产品行为）。
            SetCursorPos((int)fromScreen.X, (int)fromScreen.Y);
            Pump(100);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Pump(60);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Pump(150);

            down = move = up = 0;
            Console.WriteLine($"预热后选中: {canvas.SelectedElements.Count}（预热那一下点了空白，预期 0）");

            var watchElements = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                ScadaCanvas.SelectedElementsProperty, typeof(ScadaCanvas));
            watchElements.AddValueChanged(canvas, (_, _) =>
                Console.WriteLine($"    [集合变更] 画布={canvas.SelectedElements.Count} 视图模型={vm.SelectedElements.Count}"));
            var watchMain = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                ScadaCanvas.SelectedElementProperty, typeof(ScadaCanvas));
            watchMain.AddValueChanged(canvas, (_, _) =>
                Console.WriteLine($"    [主选中变更] {canvas.SelectedElement?.Name ?? "<null>"}"));

            canvas.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,
                new System.Windows.Input.MouseButtonEventHandler((_, e) =>
                {
                    Console.WriteLine($"    [Up 时] 模式={ReadField(canvas, "_dragMode")} " +
                        $"起点={ReadField(canvas, "_dragStartModel")} 当前={canvas.ToModelPoint(e.GetPosition(canvas))}");
                }), true);

            SetCursorPos((int)fromScreen.X, (int)fromScreen.Y);
            Pump(120);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Pump(80);

            for (int i = 1; i <= 10; i++)
            {
                SetCursorPos(
                    (int)(fromScreen.X + (toScreen.X - fromScreen.X) * i / 10.0),
                    (int)(fromScreen.Y + (toScreen.Y - fromScreen.Y) * i / 10.0));
                Pump(40);
            }

            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Pump(200);

            int count = canvas.SelectedElements.Count;
            Console.WriteLine($"画布收到事件: Down={down} Move={move} Up={up}");
            Console.WriteLine($"拖完之后选中: {count}（{string.Join("、", canvas.SelectedElements.Select(e => e.Name))}）");
            Console.WriteLine($"主选中: {canvas.SelectedElement?.Name ?? "<null>"}");

            var m0 = canvas.ToModelPoint(canvas.PointFromScreen(fromScreen));
            var m1 = canvas.ToModelPoint(canvas.PointFromScreen(toScreen));
            Console.WriteLine($"Zoom={canvas.Zoom:0.###} Offset={canvas.Offset.X:0.#},{canvas.Offset.Y:0.#}");
            Console.WriteLine($"框(设计坐标): {Math.Min(m0.X, m1.X):0.#},{Math.Min(m0.Y, m1.Y):0.#} {Math.Abs(m1.X - m0.X):0.#}x{Math.Abs(m1.Y - m0.Y):0.#}");

            foreach (var element in vm.SelectedPage!.Elements)
            {
                var box = new Rect(element.X, element.Y, Math.Max(1, element.Width), Math.Max(1, element.Height));
                var band = new Rect(Math.Min(m0.X, m1.X), Math.Min(m0.Y, m1.Y),
                    Math.Abs(m1.X - m0.X), Math.Abs(m1.Y - m0.Y));
                Console.WriteLine(
                    $"  图元 {element.Name} @ {element.X:0.#},{element.Y:0.#} {element.Width:0.#}x{element.Height:0.#}" +
                    $" 可见={vm.SelectedPage.IsElementVisible(element)} 父={element.ParentId}" +
                    $" 相交={band.IntersectsWith(box)}");
            }

            // 分叉定位：直接调画布自己的 SetSelection(4)，看是"命中计算"错了还是"落账链路"把它压成 1
            Console.WriteLine($"ItemsSource: {canvas.ItemsSource?.GetType().Name} " +
                $"同集合={ReferenceEquals(canvas.ItemsSource, vm.SelectedPage.Elements)}");
            var four = vm.SelectedPage.Elements.Take(4).ToList();
            var setSelection = typeof(ScadaCanvas).GetMethod("SetSelection",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            setSelection!.Invoke(canvas, new object[] { four });
            Console.WriteLine($"手动 SetSelection(4) 之后: {canvas.SelectedElements.Count}");

            var dir = Path.Combine(AppContext.BaseDirectory, "out");
            Directory.CreateDirectory(dir);
            Save(Render(view, ViewWidth, ViewHeight), Path.Combine(dir, "scada-marquee-after.png"));

            window.Close();
            vm.Deactivate();

            Console.WriteLine(count > 0 ? "MARQUEE: OK" : "MARQUEE: FAIL（拖完没有选中任何图元）");
        }

        /// <summary>看某个视口点下命中的是什么控件——用来判断"空白"到底空不空。</summary>
        private static string DescribeHit(ScadaCanvas canvas, Point local)
        {
            var hit = canvas.InputHitTest(local) as DependencyObject;
            if (hit == null) return "<无命中>";

            var parts = new System.Collections.Generic.List<string>();
            var node = hit;
            while (node != null && !ReferenceEquals(node, canvas))
            {
                parts.Add(node.GetType().Name + (node is FrameworkElement fe && fe.Name.Length > 0 ? "#" + fe.Name : ""));
                node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
            }
            return string.Join(" ← ", parts);
        }

        // ================================================================ ③ 出图矩阵

        /// <summary>
        /// 把这次改动关心的几个状态各截一张，落到 out\ 下。
        /// 用户打不开工程时靠这几张图验收版式与交互。
        /// </summary>
        private static void RenderShotMatrix()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "out");
            Directory.CreateDirectory(dir);

            Capture(dir, "01-默认未选中.png", 1500, 830, (view, vm, canvas) =>
                Select(canvas, Array.Empty<ScadaElement>()));

            Capture(dir, "02-选中单个图元.png", 1500, 830, (view, vm, canvas) =>
                Select(canvas, new[] { vm.SelectedPage!.Elements[0] }));

            Capture(dir, "03-框选多个图元.png", 1500, 830, (view, vm, canvas) =>
                Select(canvas, vm.SelectedPage!.Elements.Take(4).ToList()));

            Capture(dir, "04-窄面板（顶栏出滚动条）.png", 1010, 700, (view, vm, canvas) =>
                Select(canvas, vm.SelectedPage!.Elements.Take(2).ToList()));

            Capture(dir, "05-空态（没有画面）.png", 1500, 830, null, createPage: false);

            CaptureMarqueeDrag(dir, "06-真实拖拽框选之后.png", 1500, 830);

            Console.WriteLine($"出图目录: {dir}");
        }

        private static void Capture(
            string dir,
            string fileName,
            int width,
            int height,
            Action<ScadaEditorView, ScadaEditorViewModel, ScadaCanvas> arrange,
            bool createPage = true)
        {
            var (view, vm) = BuildEditor(createPage);

            var window = new Window
            {
                Width = width,
                Height = height,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 40,
                Top = 40,
                ShowInTaskbar = false,
                Topmost = true,
                Content = view,
            };

            window.Show();
            Pump(600);

            var canvas = FindCanvas(view);
            if (canvas != null && arrange != null)
            {
                arrange(view, vm, canvas);
                Pump(250);
            }

            Save(Render(view, (int)view.ActualWidth, (int)view.ActualHeight), Path.Combine(dir, fileName));
            Console.WriteLine($"  {fileName}  {view.ActualWidth:0}×{view.ActualHeight:0}  " +
                $"缩放={canvas?.Zoom.ToString("0.##") ?? "-"}  选中={canvas?.SelectedElements.Count ?? -1}");

            window.Close();
            vm.Deactivate();
        }

        /// <summary>真窗口 + 鼠标注入拖一次框选，然后把结果截下来（这张是框选修复的视觉证据）。</summary>
        private static void CaptureMarqueeDrag(string dir, string fileName, int width, int height)
        {
            var (view, vm) = BuildEditor();

            var window = new Window
            {
                Width = width,
                Height = height,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 40,
                Top = 40,
                ShowInTaskbar = false,
                Topmost = true,
                Content = view,
            };

            window.Show();
            Pump(700);

            var canvas = FindCanvas(view) ?? throw new InvalidOperationException("找不到 ScadaCanvas");

            // 先点一下空白，把选中清干净（同时点亮窗口，免得第一次按下被"激活窗口"吃掉）
            var blank = canvas.PointToScreen(new Point(canvas.ActualWidth - 40, canvas.ActualHeight - 40));
            SetForegroundWindow(new WindowInteropHelper(window).Handle);
            Pump(150);
            SetCursorPos((int)blank.X, (int)blank.Y);
            Pump(100);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Pump(60);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Pump(200);

            // 从左上角拖到右下，把整页图元都罩进去
            var from = canvas.PointToScreen(new Point(6, 6));
            var to = canvas.PointToScreen(new Point(canvas.ActualWidth - 60, canvas.ActualHeight - 60));

            SetCursorPos((int)from.X, (int)from.Y);
            Pump(120);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Pump(80);

            for (int i = 1; i <= 12; i++)
            {
                SetCursorPos(
                    (int)(from.X + (to.X - from.X) * i / 12.0),
                    (int)(from.Y + (to.Y - from.Y) * i / 12.0));
                Pump(35);
            }

            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Pump(300);

            Save(Render(view, (int)view.ActualWidth, (int)view.ActualHeight), Path.Combine(dir, fileName));
            Console.WriteLine($"  {fileName}  框选后选中={canvas.SelectedElements.Count}" +
                $"（{string.Join("、", canvas.SelectedElements.Select(e => e.Name))}）");

            window.Close();
            vm.Deactivate();
        }

        /// <summary>走画布自己的落账入口换选中（反射调 SetSelection：公开 setter 走 SetValue 会把绑定摘掉）。</summary>
        private static void Select(ScadaCanvas canvas, IReadOnlyList<ScadaElement> items)
        {
            var method = typeof(ScadaCanvas).GetMethod("SetSelection",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method!.Invoke(canvas, new object[] { items });
        }


        private static (ScadaEditorView View, ScadaEditorViewModel Vm) BuildEditor(bool createPage = true)
        {
            var workspace = new WorkspaceContext();

            var doc = new ScadaDocument();
            var page = createPage ? doc.AddPage("主画面") : null;
            if (page != null)
            {
                page.Width = 1280;
                page.Height = 800;
            }

            var solution = new SolutionModel { Scada = doc };
            workspace.SwitchSolution(solution);

            var vm = new ScadaEditorViewModel(workspace, new FakeRuntimeHost());
            vm.Activate();
            vm.SelectedPage = page;

            if (page != null)
            {
                Add(vm, "Hmi.Tank", 60, 96);
                Add(vm, "Hmi.Pump", 380, 150);
                Add(vm, "Hmi.Valve", 540, 180);
                Add(vm, "Hmi.Gauge", 640, 90);
                Add(vm, "Hmi.Button", 620, 330);
                Add(vm, "Hmi.Text", 60, 40);
            }

            ViewModelLocationProvider.Register<ScadaEditorView>(() => vm);

            return (new ScadaEditorView(), vm);
        }

        private static void Add(ScadaEditorViewModel vm, string typeKey, double x, double y)
        {
            if (vm.AddElement(typeKey, new Point(x, y)) == null)
                Console.WriteLine($"警告: 图元 {typeKey} 未能加入");
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

        private static string Describe(Brush brush)
            => brush is SolidColorBrush solid ? solid.Color.ToString() : brush?.ToString() ?? "<null>";

        private static string ReadField(object target, string name)
        {
            var field = target.GetType().GetField(name,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field?.GetValue(target)?.ToString() ?? "<?>";
        }

        private static RenderTargetBitmap Render(Visual visual, int width, int height)
        {
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
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

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extraInfo);

        private sealed class FakeRuntimeHost : IScadaRuntimeHost
        {
            public bool IsRunning { get; private set; }

            public void Start(ScadaDocument document) => IsRunning = true;

            public void Stop() => IsRunning = false;
        }
    }
}
