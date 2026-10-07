using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Core.Halcon.Extensions;
using Core.Interfaces;
using HalconDotNet;
using Plugin.BeadInspect;

namespace BeadViewProbe
{
    /// <summary>
    /// 胶路检测配置视图的离屏渲染 + 行为探针（临时脚手架，跑完可整目录删掉）。
    ///
    /// · 截图：真实例化 BeadInspectView（DataContext = BeadInspectPlugin），
    ///   在 620 x 640（PluginViewMinWidth）与 1100 x 900 两档渲染 PNG，
    ///   并逐 TextBlock 量"文字宽 vs 实际宽"报裁切/截断（比肉眼更硬）。
    /// · 行为：D1 四级底图优先 / D2 非 UI 线程订阅 / D3 来源提示 / D4 无底图禁拾取 / D5 放宽学习。
    ///
    /// 用法：_BeadViewProbe.exe [前缀]   → 输出 <前缀>_<宽>x<高>_<场景>.png
    /// </summary>
    internal static class Program
    {
        private const string OutDir = @"D:\C#\VM\_BeadViewProbe";
        private const string BeadDir = @"D:\C#\VM\Image\bead";
        private static string _prefix = "shot";
        private static int _fail;
        private static int _limit;

        /// <summary>
        /// 视图支持尺寸下限：≥ 此尺寸下不允许出现任何裁切（[CLIP] 计入失败）；
        /// 低于此只报 [LIMIT]（已知不支持，不计失败，但会打印在日志里，绝不静默）。
        /// 口径 = 验收尺寸 1100x800 / 1490x880 留余量后的下界。实渲染度量：右列内容的固定体量
        /// = 工具条 92~250（按钮 3 行 + 横幅）+ 画布 150 + 参数卡 130 + 状态/预览 81~214，
        /// 最紧时约 640 高 —— 所以发布键 PluginViewMinHeight=360 对本视图不成立（见交接未决问题）。
        /// </summary>
        private const double SupportedMinWidth = 900;
        private const double SupportedMinHeight = 700;

        // 探针用的参考路径点列（与 FlowCanvasChecks 同源：真样图上的 14 控制点）
        private static readonly double[] RefRows =
        {
            701.767, 626.953, 538.867, 443.54, 390.447, 360.28,
            354.247, 363.9, 400.1, 458.02, 509.907, 588.34, 659.533, 696.94
        };
        private static readonly double[] RefCols =
        {
            319.24, 336.133, 367.507, 431.46, 489.38, 546.093,
            646.247, 722.267, 776.567, 826.04, 869.48, 912.92, 934.64, 929.813
        };

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0)
                _prefix = args[0];
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            if (Application.Current == null)
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Core.Halcon;component/Generic.xaml")
            });
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml")
            });

            try
            {
                RunBehaviorChecks();
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 行为自检崩溃: " + ex);
                _fail++;
            }

            // 「点学习 → 截图」：用户窗口尺寸（1490×880 内容区），成功与失败两个方向都要看见结论
            // 布局专项（2026-10-07 用户实测「右列被裁 / 参数卡与第 4 行不可达」）：
            // 验收尺寸 + 更紧的高度 + 真实外壳仿真。裸视图离屏渲染会高估 160px 可用高，
            // 所以外壳仿真才是"用户现场到底有多少余量"的权威口径。
            var shots = new List<(string Tag, Action Run)>
            {
                ("620x640_input", () => Capture(Scene.InputImage, 620, 640, "620x640_input")),
                ("620x900_input", () => Capture(Scene.InputImage, 620, 900, "620x900_input")),
                ("1100x900_input", () => Capture(Scene.InputImage, 1100, 900, "1100x900_input")),
                ("620x640_empty", () => Capture(Scene.Empty, 620, 640, "620x640_empty")),
                ("1100x900_ref", () => Capture(Scene.RefImage, 1100, 900, "1100x900_ref")),
                ("1490x880_learn_fail", () => Capture(Scene.LearnFail, 1490, 880, "1490x880_learn_fail")),
                ("1490x880_learn_ok", () => Capture(Scene.LearnOk, 1490, 880, "1490x880_learn_ok")),
                ("1100x800_learn_ok", () => Capture(Scene.LearnOk, 1100, 800, "1100x800_learn_ok")),
                ("1200x664_learn_ok", () => Capture(Scene.LearnOk, 1200, 664, "1200x664_learn_ok")),
                ("1200x664_learn_fail", () => Capture(Scene.LearnFail, 1200, 664, "1200x664_learn_fail")),
                ("620x640_learn_ok", () => Capture(Scene.LearnOk, 620, 640, "620x640_learn_ok")),
                // 用户现场的最可能形态：125% 缩放下 1250x1030 的窗口 → WPF 逻辑 ~1000x824（外壳占 160）
                // → 视图 900x664、右列 550 → 工具条 7 个按钮正好换到第 2 行 + 右列高度紧张
                ("1000x664_learn_ok", () => Capture(Scene.LearnOk, 1000, 664, "1000x664_learn_ok")),
                ("1000x664_learn_fail", () => Capture(Scene.LearnFail, 1000, 664, "1000x664_learn_fail")),
                ("shell_1040x824_learn_ok", () => CaptureShell(Scene.LearnOk, 1040, 824, "shell_1040x824_learn_ok")),
                ("shell_960x680_learn_ok", () => CaptureShell(Scene.LearnOk, 960, 680, "shell_960x680_learn_ok")),
                ("shell_1490x1030_learn_ok", () => CaptureShell(Scene.LearnOk, 1490, 1030, "shell_1490x1030_learn_ok")),
                ("shell_1192x824_learn_ok", () => CaptureShell(Scene.LearnOk, 1192, 824, "shell_1192x824_learn_ok")),
            };
            // 第二个参数 = 只跑标签含该串的场景（改一轮布局不用等全量）
            string? only = args.Length > 1 ? args[1] : null;
            foreach (var (tag, run) in shots)
            {
                if (only != null && !tag.Contains(only))
                    continue;
                run();
            }

            Console.WriteLine(_fail == 0 ? "=== PROBE: 全部通过 ===" : $"### PROBE: {_fail} 项失败");
            if (_limit > 0)
                Console.WriteLine($"### PROBE 附注：{_limit} 条 [LIMIT] —— 视图小于支持下限 "
                    + $"{SupportedMinWidth:F0}x{SupportedMinHeight:F0} 的场景（内容固定体量装不下，非本轮布局回归）");
            Environment.Exit(_fail == 0 ? 0 : 1);
        }

        // ==================================================================
        //  行为自检（D1~D5）
        // ==================================================================

        private static void RunBehaviorChecks()
        {
            Console.WriteLine("=== 行为自检 ===");

            // ── D1/D2/D3：四级底图优先 + 订阅 ──
            var p1 = new BeadInspectPlugin { InstanceName = "胶路_探针1" };
            try
            {
                p1.Initialize(new FakeStepData());
                p1.EditingEntry!.RefPointsJson = PointsJson();
                using var input = LoadImage(1);
                var inputSize = input.GetImageSize();

                p1.SrcImage.Value = input;   // 无参考图 + 绑定输入图
                Check("D1① 无参考图 + 输入图 → 底图 = 输入图",
                    p1.DisplayImage != null && p1.DisplayImage.IsInitialized()
                    && Math.Abs(p1.DisplayImage.GetImageSize()[0] - inputSize[0]) < 0.5,
                    Describe(p1.DisplayImage));
                Check("D3① 底图=输入图 → Warning 级提示 + 文案点名输入图",
                    BaseLevel(p1) == StatusLevel.Warning && BaseHint(p1).Contains("输入图"),
                    $"[{BaseLevel(p1)}] {BaseHint(p1)}");
                Check("D1 有图时画布不是空态", !IsEmpty(p1), $"IsCanvasEmpty={IsEmpty(p1)}");

                p1.EditingEntry.RefImagePath = Path.Combine(BeadDir, "adhesive_bead_ref.png");
                p1.LoadRefImage();
                Check("D1② 有参考图 → 底图 = 参考图（优先于输入图）",
                    BaseHint(p1).Contains("参考图") && BaseLevel(p1) == StatusLevel.Info,
                    $"[{BaseLevel(p1)}] {BaseHint(p1)}");
            }
            finally
            {
                p1.Dispose();
            }

            // ── D1④ / D4：什么都没有 → 空态 + 禁拾取 ──
            var p2 = new BeadInspectPlugin { InstanceName = "胶路_探针2" };
            try
            {
                p2.Initialize(new FakeStepData());
                Check("D1④ 无参考图且无输入图 → 底图空 + 空态",
                    p2.DisplayImage == null && IsEmpty(p2), $"IsCanvasEmpty={IsEmpty(p2)}");
                Check("D1④ 空态文案（画布中央提示用同一句）",
                    BaseHint(p2).Contains("尚未载入图像") && BaseHint(p2).Contains("绑定上游输入图"),
                    BaseHint(p2));

                int before = p2.PointRows.Count;
                p2.AddPoint(300, 300);
                Check("D4 无底图时 AddPoint 被拒（点列不增 + 红字）",
                    p2.PointRows.Count == before && p2.StatusLevel == StatusLevel.Error
                    && p2.StatusMessage.Contains("请先载入图像"),
                    $"点列={p2.PointRows.Count} [{p2.StatusLevel}] {p2.StatusMessage}");

                p2.DeleteNearestPoint(300, 300);
                Check("D4 无底图时 DeleteNearestPoint 被拒", p2.PointRows.Count == before, $"点列={p2.PointRows.Count}");

                bool drag = p2.BeginPointDrag(300, 300);
                Check("D4 无底图时 BeginPointDrag 被拒", !drag && p2.StatusLevel == StatusLevel.Error,
                    $"drag={drag} [{p2.StatusLevel}] {p2.StatusMessage}");
            }
            finally
            {
                p2.Dispose();
            }

            // ── D2：非 UI 线程赋值不抛 + 投递回 UI 后底图更新 ──
            var p3 = new BeadInspectPlugin { InstanceName = "胶路_探针3" };
            try
            {
                p3.Initialize(new FakeStepData());
                using var bg = LoadImage(2);
                Exception? thrown = null;
                var t = new Thread(() =>
                {
                    try { p3.SrcImage.Value = bg; }
                    catch (Exception ex) { thrown = ex; }
                });
                t.IsBackground = true;
                t.Start();
                t.Join();

                bool settled = PumpUntil(() => p3.DisplayImage != null, 2000);
                Check("D2 非 UI 线程给 SrcImage 赋值不抛", thrown == null, thrown?.ToString() ?? "");
                Check("D2 投入 UI 线程后底图更新为输入图", settled && !IsEmpty(p3),
                    Describe(p3.DisplayImage));
            }
            finally
            {
                p3.Dispose();
            }

            // ── D5：固定相机 + 无参考图 + 点列 ≥2 → 学习不再被参考图检查拦下 ──
            var p4 = new BeadInspectPlugin { InstanceName = "胶路_探针4" };
            try
            {
                p4.Initialize(new FakeStepData());
                var e = p4.EditingEntry!;
                e.RefPointsJson = PointsJson();
                e.TargetWidth = 15.26;
                e.WidthTolerance = 8;
                e.PositionTolerance = 30;
                e.Polarity = "dark";
                p4.AlignMode = BeadAlignMode.None;
                p4.LearnRecipe();
                Check("D5 AlignMode=None + 无参考图 → 学习不被参考图检查拦下",
                    !p4.StatusMessage.Contains("参考图不存在"), p4.StatusMessage);
                Check("D5 学习走到建模型并写入指纹", !string.IsNullOrEmpty(e.LearnedSignature),
                    string.IsNullOrEmpty(e.LearnedSignature) ? "(空)" : "已写指纹");

                // 反证：平面可变形模式仍然要求参考图（不能把守卫整条删掉）
                var p5 = new BeadInspectPlugin { InstanceName = "胶路_探针5" };
                try
                {
                    p5.Initialize(new FakeStepData());
                    var e5 = p5.EditingEntry!;
                    e5.RefPointsJson = PointsJson();
                    p5.AlignMode = BeadAlignMode.PlanarDeformable;
                    p5.LearnRecipe();
                    Check("D5 反证：平面可变形 + 无参考图 → 仍拒绝学习（提示参考图）",
                        p5.StatusMessage.Contains("参考图"), p5.StatusMessage);
                }
                finally { p5.Dispose(); }
            }
            finally
            {
                p4.Dispose();
            }
        }

        /// <summary>反射读新属性：探针要在"改前/改后"两版插件上都能编译（属性不存在时返回默认值）</summary>
        private static T? Reflect<T>(BeadInspectPlugin plugin, string name)
        {
            var prop = typeof(BeadInspectPlugin).GetProperty(name);
            if (prop == null) return default;
            try { return (T?)prop.GetValue(plugin); }
            catch { return default; }
        }

        private static string BaseHint(BeadInspectPlugin plugin) => Reflect<string>(plugin, "BaseImageHint") ?? "";
        private static StatusLevel BaseLevel(BeadInspectPlugin plugin) => Reflect<StatusLevel>(plugin, "BaseImageLevel");
        private static bool IsEmpty(BeadInspectPlugin plugin) => Reflect<bool>(plugin, "IsCanvasEmpty");

        private static bool PumpUntil(Func<bool> cond, int timeoutMs)
        {
            var end = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < end)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                if (cond()) return true;
                Thread.Sleep(20);
            }
            return cond();
        }

        private static string Describe(HImage? img)
        {
            try
            {
                if (img == null || !img.IsInitialized()) return "(空)";
                var s = img.GetImageSize();
                return $"{s[0]:0}x{s[1]:0}";
            }
            catch (Exception ex) { return "取尺寸失败:" + ex.Message; }
        }

        private static HImage LoadImage(int index)
        {
            HOperatorSet.ReadImage(out HObject raw, Path.Combine(BeadDir, $"adhesive_bead_{index:00}.png"));
            var img = new HImage(raw);
            raw.Dispose();
            return img;
        }

        private static string PointsJson()
        {
            var pts = new double[RefRows.Length][];
            for (int i = 0; i < RefRows.Length; i++)
                pts[i] = new[] { RefRows[i], RefCols[i] };
            return Newtonsoft.Json.JsonConvert.SerializeObject(pts);
        }

        // ==================================================================
        //  截图 + 文字裁切度量
        // ==================================================================

        private enum Scene { Empty, InputImage, RefImage, LearnFail, LearnOk }

        /// <summary>裸视图离屏渲染：width/height = 插件视图自己拿到的尺寸（不含外壳）</summary>
        private static void Capture(Scene scene, double width, double height, string tag) =>
            CaptureCore(scene, width, height, tag, false);

        /// <summary>
        /// 带插件配置外壳的仿真（PluginConfigShellView：72 头部 + 56 底部 + 内容区 Margin 20,16）。
        /// width/height = **外壳窗口**尺寸 —— 插件视图实际只拿到 (width-40) x (height-160)。
        /// 这是"用户现场到底有多少可用高"的权威口径：裸视图口径会凭空多出 160px。
        /// </summary>
        private static void CaptureShell(Scene scene, double width, double height, string tag) =>
            CaptureCore(scene, width, height, tag, true);

        private static void CaptureCore(Scene scene, double width, double height, string tag, bool shell)
        {
            var plugin = new BeadInspectPlugin { InstanceName = "胶路_截图" };
            FrameworkElement host;
            Window? win = null;
            try
            {
                plugin.Initialize(new FakeStepData());

                // 配方库：两条（列表高度/按钮组是否悬空）
                var e1 = plugin.EditingEntry!;
                e1.Name = "配方1-泵体";
                e1.RefPointsJson = PointsJson();
                e1.TargetWidth = 15.26;
                e1.WidthTolerance = 8;
                e1.PositionTolerance = 30;
                e1.Polarity = "dark";
                plugin.Library.Add(new BeadRecipeEntry { Name = "配方2-电机壳" });
                plugin.RefreshRecipeRows();

                if (scene == Scene.RefImage)
                    e1.RefImagePath = Path.Combine(BeadDir, "adhesive_bead_ref.png");

                var view = new BeadInspectView { DataContext = plugin };
                host = shell ? BuildShellHost(view, width, height) : BuildPlainHost(view, width, height);

                win = new Window
                {
                    Content = host,
                    Width = width + 40,
                    Height = height + 60,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 30,
                    Top = 30,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Title = "bead view probe " + tag,
                };
                win.Show();

                // 视图 Loaded → OnViewLoaded→LoadRefImage（参考图场景在这里上屏）
                Pump(700);

                if (scene == Scene.InputImage)
                {
                    using var input = LoadImage(1);
                    plugin.SrcImage.Value = input;   // 走配置态订阅：底图应变输入图
                    Pump(400);
                }
                if (scene == Scene.RefImage)
                    plugin.LoadRefImage();

                // 「点学习 → 截图」两个场景：全程走真实用户流（拾取 → 点学习），不直接调私有方法。
                // LearnFail = 用户实测现场形态：平面可变形 + 没设参考图（底图退到输入图兜底）+ 拾取 3 点。
                if (scene == Scene.LearnFail)
                {
                    using var input = LoadImage(1);
                    plugin.SrcImage.Value = input;                    // 底图 = 输入图兜底
                    plugin.AddPoint(RefRows[2], RefCols[2]);          // 3 点 → 工具栏显示「点数 3」
                    plugin.AddPoint(RefRows[5], RefCols[5]);
                    plugin.AddPoint(RefRows[8], RefCols[8]);
                    Pump(300);
                }
                if (scene == Scene.LearnOk)
                {
                    e1.RefImagePath = Path.Combine(BeadDir, "adhesive_bead_ref.png");
                    plugin.LoadRefImage();                            // = 点「载入」
                    for (int i = 0; i < 5; i++)
                        plugin.AddPoint(RefRows[i], RefCols[i]);      // = 画布逐点拾取
                    Pump(300);
                }
                if (scene == Scene.LearnFail || scene == Scene.LearnOk)
                {
                    plugin.LearnRecipe();                             // = 点「学习」（视图 Click 只转发这一句）
                    Pump(600);
                }
                Pump(400);

                host.UpdateLayout();
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                // 逼一次真实重排：离屏宿主首轮布局有时还留着"窗口初始尺寸"的残影，
                // 不改一次窗口尺寸就量不准（会让行高看起来离谱）
                win.Width = width + 41;
                Pump(250);
                host.UpdateLayout();
                win.Width = width + 40;
                Pump(250);
                host.UpdateLayout();
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                const double scale = 1.0;
                var rtb = new RenderTargetBitmap((int)(width * scale), (int)(height * scale),
                    96 * scale, 96 * scale, PixelFormats.Pbgra32);
                rtb.Render(host);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                var file = Path.Combine(OutDir, $"{_prefix}_{tag}.png");
                using (var fs = File.Create(file))
                    enc.Save(fs);
                Console.WriteLine($">> 已存 {file}");

                Report(host, tag, plugin);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"### 截图 {tag} 失败: {ex}");
                _fail++;
            }
            finally
            {
                try { win?.Close(); } catch { }
                try { plugin.Dispose(); } catch { }
            }
        }

        private static FrameworkElement BuildPlainHost(FrameworkElement view, double width, double height)
        {
            var host = new Grid { Width = width, Height = height, Background = Brushes.White };
            host.Children.Add(view);
            return host;
        }

        /// <summary>外壳仿真宿主：72 头部 + * 内容（Margin 20,16）+ 56 底部，与 PluginConfigShellView 同构</summary>
        private static FrameworkElement BuildShellHost(FrameworkElement view, double width, double height)
        {
            var host = new Grid { Width = width, Height = height, Background = Brushes.White };
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });

            var chrome = new SolidColorBrush(Color.FromRgb(0xF6, 0xF8, 0xFA));
            var header = new Border { Background = chrome };
            Grid.SetRow(header, 0);
            host.Children.Add(header);

            var content = new Grid { Margin = new Thickness(20, 16, 20, 16) };
            content.Children.Add(view);
            Grid.SetRow(content, 1);
            host.Children.Add(content);

            var footer = new Border { Background = chrome };
            Grid.SetRow(footer, 2);
            host.Children.Add(footer);
            return host;
        }

        private static void Pump(int ms)
        {
            var end = Environment.TickCount64 + ms;
            while (Environment.TickCount64 < end)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Thread.Sleep(10);
            }
        }

        /// <summary>逐 TextBlock 量字数：实际宽 &lt; 文字所需宽 = 被裁/被截断（换行文本不计）</summary>
        private static void Report(FrameworkElement root, string tag, BeadInspectPlugin plugin)
        {
            var clipped = new List<string>();
            var trimmed = new List<string>();

            foreach (var tb in FindAll<TextBlock>(root))
            {
                if (tb.Visibility != Visibility.Visible || tb.ActualWidth < 1 || string.IsNullOrEmpty(tb.Text))
                    continue;
                double need = MeasureText(tb);
                if (tb.TextWrapping != TextWrapping.NoWrap)
                    continue;
                if (tb.ActualWidth + 0.5 < need)
                {
                    var item = $"\"{Short(tb.Text)}\" actual={tb.ActualWidth:F1} need={need:F1}";
                    if (tb.TextTrimming != TextTrimming.None) trimmed.Add(item);
                    else clipped.Add(item);
                }
            }

            var buttons = FindAll<Button>(root)
                .Where(b => b.Visibility == Visibility.Visible && b.ActualWidth > 0 && HasAncestor<WrapPanel>(b))
                .ToList();
            var rows = buttons.Select(b => Math.Round(Bounds(b, root).Top)).Distinct().Count();

            Console.WriteLine($"   [{tag}] 工具栏按钮 {buttons.Count} 个 / 占 {rows} 行"
                + $"；文字裁切 {clipped.Count} 处；省略号截断 {trimmed.Count} 处");
            foreach (var c in clipped) Console.WriteLine("     [CLIP] " + c);
            foreach (var t in trimmed) Console.WriteLine("     [TRIM] " + t);

            // 操作结果横幅（点「学习」的结论）：必须真的渲染出来、且落在视口内——
            // 这才是"点学习有反应"的硬指标（不是"加了提示就算完"）。
            string banner = Reflect<string>(plugin, "ActionBanner") ?? "";
            if (banner.Length > 0)
            {
                var bt = FindAll<TextBlock>(root).FirstOrDefault(t => (t.Text ?? "") == banner);
                if (bt == null)
                {
                    Console.WriteLine($"   [{tag}] [FAIL] 横幅文字未出现在视图里：\"{Short(banner)}\"");
                    _fail++;
                }
                else
                {
                    var rb = Bounds(bt, root);
                    bool inView = rb.Top >= -0.5 && rb.Bottom <= root.ActualHeight + 0.5
                                  && rb.Left >= -0.5 && rb.Right <= root.ActualWidth + 0.5;
                    Console.WriteLine($"   [{tag}] 结果横幅（{Reflect<StatusLevel>(plugin, "ActionBannerLevel")}）"
                        + $" bounds={Fmt(rb)} 视口={root.ActualWidth:F0}x{root.ActualHeight:F0} 在视口内={inView}");
                    Console.WriteLine($"   [{tag}] 横幅文字 = \"{banner}\"");
                    if (!inView)
                    {
                        Console.WriteLine("     [CLIP] 结果横幅超出视口");
                        _fail++;
                    }
                }
            }
            else
            {
                Console.WriteLine($"   [{tag}] 结果横幅 = (空：尚未点过「学习」)");
            }

            // 画布标题 vs 操作提示：两行不重叠才算过
            var title = FindFirstText(root, "胶路中心线拾取");
            var hint = FindFirstText(root, "左键加点");
            if (title != null && hint != null)
            {
                var rt = Bounds(title, root);
                var rh = Bounds(hint, root);
                bool overlap = rt.IntersectsWith(rh);
                Console.WriteLine($"   [{tag}] 画布标题 {Fmt(rt)} / 提示 {Fmt(rh)} → 重叠={overlap}");
                if (overlap) _fail++;
            }

            // 空态提示（画布中央那条：用它的末行文字定位，避免与状态栏那句同名前缀混淆）
            var empty = FindFirstText(root, "填参考图路径后点");
            if (empty == null)
                Console.WriteLine($"   [{tag}] 画布空态提示 TextBlock=无");
            else
            {
                var r = Bounds(empty, root);
                var card = FindFirstText(root, "胶路中心线拾取") is { } t ? Bounds(t, root) : Rect.Empty;
                Console.WriteLine($"   [{tag}] 画布空态提示 {empty.Visibility} actual={empty.ActualWidth:F0}x{empty.ActualHeight:F0}"
                    + $" bounds={Fmt(r)}");
                if (empty.Visibility == Visibility.Visible && (r.Right > root.ActualWidth + 0.5 || r.Left < -0.5 || r.Bottom > root.ActualHeight + 0.5))
                    ClipOrLimit(root, $"画布空态提示超出视口 bounds={Fmt(r)}");
            }

            // 配方库高度
            foreach (var lb in FindAll<ListBox>(root).Where(l => l.Visibility == Visibility.Visible))
                Console.WriteLine($"   [{tag}] ListBox 高={lb.ActualHeight:F0} 条目={lb.Items.Count}");

            // 底部提示是否被父容器裁掉（自身在可视区内的比例）
            var bottom = FindFirstText(root, "位置容差待现场标定");
            if (bottom != null)
            {
                var rb = Bounds(bottom, root);
                var inView = rb.Top >= 0 && rb.Bottom <= root.ActualHeight + 0.5;
                Console.WriteLine($"   [{tag}] 底部提示 {Fmt(rb)} 高={bottom.ActualHeight:F0} 在视口内={inView}");
                if (!inView) Console.WriteLine("     [CLIP] 底部提示超出视口");
                DumpChain(bottom, root, tag, "底部提示");
            }
            var canvasTitle = FindFirstText(root, "胶路中心线拾取");
            if (canvasTitle != null)
                DumpChain(canvasTitle, root, tag, "画布标题");

            AuditRightColumn(root, tag, plugin);
        }

        // ==================================================================
        //  右列布局审计（2026-10-07 用户实测：右列第 3 行被下沿裁断、第 4 行整个不可达）
        //  判据不是"看着像被裁"，而是硬指标：
        //    ① 右列 4 行实际高之和 ≤ 可用高（Grid 不滚动，超出即被父级裁掉，用户"看不见"）；
        //    ② 每张卡的 bounds 都在右列可视区内（bottom ≤ 右列高）；
        //    ③ 换行文本不是"少画了几行"（FormattedText 按实际宽度重算应有高度）。
        // ==================================================================
        private static void AuditRightColumn(FrameworkElement root, string tag, BeadInspectPlugin plugin)
        {
            var canvas = FindAll<FrameworkElement>(root).FirstOrDefault(e => e.GetType().Name == "ImageEdit");
            if (canvas == null)
            {
                Console.WriteLine($"   [{tag}] [FAIL] 找不到画布控件 ImageEdit（布局审计锚点丢失）");
                _fail++;
                return;
            }
            var right = Ancestors(canvas).OfType<Grid>().FirstOrDefault(g => g.RowDefinitions.Count == 4);
            if (right == null)
            {
                Console.WriteLine($"   [{tag}] [FAIL] 找不到右列四行 Grid（布局审计锚点丢失）");
                _fail++;
                return;
            }

            var rows = right.RowDefinitions;
            double sum = rows.Sum(r => r.ActualHeight);
            var gridBounds = Bounds(right, root);
            Console.WriteLine($"   [{tag}] 右列 可用={right.ActualWidth:F0}x{right.ActualHeight:F0}"
                + $" rows=[{string.Join(",", rows.Select(r => r.ActualHeight.ToString("F0")))}]"
                + $" heights=[{string.Join(",", rows.Select(r => r.Height.ToString()))}]"
                + $" min=[{string.Join(",", rows.Select(r => r.MinHeight.ToString("F0")))}]"
                + $" max=[{string.Join(",", rows.Select(r => r.MaxHeight.ToString("F0")))}]"
                + $" 合计={sum:F0}");
            // 关键判据：右列自己的 bounds 超出视图 = 下沿内容用户永远看不到
            if (gridBounds.Bottom > root.ActualHeight + 0.5)
            {
                ClipOrLimit(root, $"右列本身被撑到 y={gridBounds.Bottom:F0}，超出视图 {root.ActualHeight:F0}："
                    + $" 下沿 {gridBounds.Bottom - root.ActualHeight:F0}px 的内容（含第 4 行）不可能被看到");
            }
            else if (sum > right.ActualHeight + 0.5)
            {
                ClipOrLimit(root, $"右列行高合计 {sum:F0} > 可用 {right.ActualHeight:F0}：超出 {sum - right.ActualHeight:F0}px");
            }

            var pointCount = FindAll<TextBlock>(root).FirstOrDefault(t => (t.Text ?? "") == plugin.PointCountText);
            var status = FindAll<TextBlock>(root).FirstOrDefault(t => (t.Text ?? "") == plugin.StatusMessage);
            var items = new (string Name, DependencyObject Inner, bool Row4)[]
            {
                ("工具条", pointCount ?? root, false),
                ("画布卡", FindFirstText(root, "胶路中心线拾取") ?? root, false),
                ("参数/点列卡", FindFirstText(root, "配方参数") ?? root, false),
                ("状态面板", status ?? root, true),
                ("预览卡", FindFirstText(root, "预览（学习叠加") ?? root, true),
            };
            foreach (var (name, inner, row4) in items)
            {
                var card = OutermostCardBorder(inner, right);
                if (card == null)
                {
                    Console.WriteLine($"   [{tag}] [FAIL] 定位不到「{name}」卡（布局锚点丢失）");
                    _fail++;
                    continue;
                }
                var b = Bounds(card, right);
                var vb = Bounds(card, root);
                bool inGrid = b.Top >= -0.5 && b.Bottom <= right.ActualHeight + 0.5
                              && b.Left >= -0.5 && b.Right <= right.ActualWidth + 0.5;
                bool inView = vb.Top >= -0.5 && vb.Bottom <= root.ActualHeight + 0.5
                              && vb.Left >= -0.5 && vb.Right <= root.ActualWidth + 0.5;
                var sv = FindAll<ScrollViewer>(card)
                    .FirstOrDefault(s => s.ScrollableHeight > 0 && !Ancestors(s).OfType<DataGrid>().Any());
                Console.WriteLine($"   [{tag}] 卡·{name}{(row4 ? "（第4行）" : "")} bounds={Fmt(b)} 视图内y={vb.Top:F0}..{vb.Bottom:F0}"
                    + $" 高={card.ActualHeight:F0} 在右列内={inGrid} 在视图内={inView}"
                    + (sv != null ? $" [自身可滚动 viewport={sv.ViewportHeight:F0}/extent={sv.ExtentHeight:F0}]" : ""));
                if (!inView && sv == null)
                {
                    ClipOrLimit(root, $"卡片下沿 {vb.Bottom:F0} 越过视图 {root.ActualHeight:F0}，且自身不可滚动 = 用户点不到（{name}）");
                }
            }

            // 参数卡分解：到底是谁把它撑这么高（决定"收紧哪里"）
            var paramsCard = FindFirstText(root, "配方参数") is { } pt ? OutermostCardBorder(pt, right) : null;
            if (paramsCard != null)
            {
                var dg = FindAll<DataGrid>(paramsCard).FirstOrDefault();
                var exp = FindAll<Expander>(paramsCard).FirstOrDefault();
                var hint = FindFirstText(paramsCard, "位置容差：3~5");
                Console.WriteLine($"   [{tag}] 参数卡分解 卡高={paramsCard.ActualHeight:F0}"
                    + $" 左列={(FindFirstText(paramsCard, "配方参数")?.Parent as FrameworkElement)?.ActualHeight ?? 0:F0}"
                    + $" 右列={(FindFirstText(paramsCard, "路径点列")?.Parent as FrameworkElement)?.ActualHeight ?? 0:F0}"
                    + $" DataGrid={dg?.ActualHeight:F0} Expander={exp?.ActualHeight:F0} 末行提示={hint?.ActualHeight:F0}");
            }

            // 工具条：WrapPanel 一行所需宽 vs 可用宽（「适应图片」掉到第二行的判据）
            var wrap = FindAll<WrapPanel>(root).FirstOrDefault(w => FindAll<Button>(w).Any());
            if (wrap != null)
            {
                var kids = wrap.Children.OfType<FrameworkElement>().ToList();
                double need = kids.Sum(k => k.DesiredSize.Width + k.Margin.Left + k.Margin.Right);
                double actual = kids.Sum(k => k.ActualWidth + k.Margin.Left + k.Margin.Right);
                int lines = kids.Select(k => Math.Round(Bounds(k, wrap).Top)).Distinct().Count();
                Console.WriteLine($"   [{tag}] 工具条 可用宽={wrap.ActualWidth:F0}"
                    + $" 一行实际占宽={actual:F0}（预算={need:F0}） 实占={lines} 行 按钮={kids.Count}"
                    + $" 最宽按钮={kids.Max(k => k.ActualWidth):F0}");
            }

            // ③ 卡提示：被裁 vs 只是换行 —— 按"上级可用宽"重算应有高度
            var hint3s = FindAll<TextBlock>(root)
                .Where(t => (t.Text ?? "").Contains("仅在「平面可变形」对齐模式下生效")).ToList();
            Console.WriteLine($"   [{tag}] ③卡提示命中 {hint3s.Count} 个 TextBlock");
            foreach (var hint3 in hint3s)
            {
                var parent = hint3.Parent as FrameworkElement;
                double availW = parent?.ActualWidth ?? hint3.ActualWidth;
                double needH = WrappedHeightAt(hint3, availW);
                bool cutV = hint3.ActualHeight + 1.5 < needH;
                bool overW = hint3.ActualWidth > availW + 0.5;
                var hb = Bounds(hint3, root);
                Console.WriteLine($"   [{tag}] ③卡提示 实际={hint3.ActualWidth:F0}x{hint3.ActualHeight:F0} 上级={parent?.GetType().Name}"
                    + $" 上级宽={availW:F0} MaxWidth={hint3.MaxWidth:F0} 换行={hint3.TextWrapping}"
                    + $" 按上级宽应需高={needH:F0} 少行={cutV} 超宽={overW} 视图内={Fmt(hb)}");
                DumpChain(hint3, root, tag, "③提示");
                if (cutV || overW)
                {
                    Console.WriteLine("     [CLIP] ③卡提示未被容器宽度约束：整行文字跑出卡片右缘（用户截图「…工件在视」处断掉）");
                    _fail++;
                }
            }

            // 「怎么用」卡体量（左列自带 ScrollViewer，不算裁切）
            var how = FindFirstText(root, "怎么用");
            var howSv = how != null ? Ancestors(how).OfType<ScrollViewer>().FirstOrDefault() : null;
            var howCard = how != null ? OutermostCardBorder(how, howSv ?? root) : null;
            Console.WriteLine($"   [{tag}] 「怎么用」卡 高={(howCard?.ActualHeight ?? 0):F0}"
                + $" 左列内容高={howSv?.ExtentHeight:F0} 左列视口高={howSv?.ViewportHeight:F0}"
                + $" 左列可滚={howSv?.ScrollableHeight:F0}");
        }

        /// <summary>视图中真正的 BeadInspectView 元素（外壳仿真时它比 root 小，是"用户实际拿到多大"的口径）</summary>
        private static FrameworkElement FindView(FrameworkElement root) =>
            FindAll<FrameworkElement>(root).FirstOrDefault(e => e.GetType().Name == "BeadInspectView") ?? root;

        /// <summary>裁切上报：达到支持下限 = [CLIP] 计入失败；低于下限 = [LIMIT] 只记录（绝不静默放过）</summary>
        private static void ClipOrLimit(FrameworkElement root, string detail)
        {
            var v = FindView(root);
            if (v.ActualWidth >= SupportedMinWidth && v.ActualHeight >= SupportedMinHeight)
            {
                Console.WriteLine("     [CLIP] " + detail);
                _fail++;
            }
            else
            {
                Console.WriteLine($"     [LIMIT] {detail} —— 视图 {v.ActualWidth:F0}x{v.ActualHeight:F0} < 支持下限 "
                    + $"{SupportedMinWidth:F0}x{SupportedMinHeight:F0}（属已知不支持尺寸）");
                _limit++;
            }
        }

        /// <summary>向上走到 stop（不含）为止遇到的最外层 Border = 卡片容器</summary>
        private static Border? OutermostCardBorder(DependencyObject inner, DependencyObject stop)
        {
            Border? best = null;
            DependencyObject? cur = inner;
            while (cur != null && !ReferenceEquals(cur, stop))
            {
                if (cur is Border b) best = b;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return best;
        }

        private static IEnumerable<DependencyObject> Ancestors(DependencyObject node)
        {
            for (var cur = VisualTreeHelper.GetParent(node); cur != null; cur = VisualTreeHelper.GetParent(cur))
                yield return cur;
        }

        /// <summary>换行文本按 "实际可用宽" 重算应有高度：ActualHeight 明显小于它 = 整行被吞</summary>
        private static double WrappedHeight(TextBlock tb) => WrappedHeightAt(tb, tb.ActualWidth);

        private static double WrappedHeightAt(TextBlock tb, double width)
        {
            var ft = new FormattedText(tb.Text ?? "", System.Globalization.CultureInfo.CurrentCulture,
                tb.FlowDirection, new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                tb.FontSize, tb.Foreground, VisualTreeHelper.GetDpi(tb).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, width),
            };
            return ft.Height;
        }

        /// <summary>祖先链宽高打印：定位"某个容器被按内容撑开/被按无穷宽测量"的层级</summary>
        private static void DumpChain(FrameworkElement el, FrameworkElement root, string tag, string label)
        {
            var sb = new StringBuilder();
            DependencyObject? cur = el;
            int guard = 0;
            while (cur != null && !ReferenceEquals(cur, root) && guard++ < 20)
            {
                if (cur is FrameworkElement fe)
                {
                    var b = Bounds(fe, root);
                    sb.AppendLine($"     [CHAIN·{label}] {fe.GetType().Name}{(string.IsNullOrEmpty(fe.Name) ? "" : "#" + fe.Name)}"
                        + $" actual={fe.ActualWidth:F0}x{fe.ActualHeight:F0} desired={fe.DesiredSize.Width:F0}x{fe.DesiredSize.Height:F0}"
                        + $" bounds={Fmt(b)}");
                    if (fe is Grid g)
                    {
                        if (g.ColumnDefinitions.Count > 0)
                            sb.AppendLine($"        cols=[{string.Join(",", g.ColumnDefinitions.Select(c => c.ActualWidth.ToString("F0")))}]"
                                + $" widths=[{string.Join(",", g.ColumnDefinitions.Select(c => c.Width.ToString()))}]");
                        if (g.RowDefinitions.Count > 0)
                            sb.AppendLine($"        rows=[{string.Join(",", g.RowDefinitions.Select(r => r.ActualHeight.ToString("F0")))}]"
                                + $" heights=[{string.Join(",", g.RowDefinitions.Select(r => r.Height.ToString()))}]");
                    }
                }
                cur = VisualTreeHelper.GetParent(cur);
            }
            Console.Write(sb.ToString());
        }

        private static string Fmt(Rect r) => $"({r.Left:F0},{r.Top:F0},{r.Width:F0}x{r.Height:F0})";

        private static string Short(string s) => s.Length <= 18 ? s : s.Substring(0, 18) + "…";

        private static double MeasureText(TextBlock tb)
        {
            var ft = new FormattedText(tb.Text, System.Globalization.CultureInfo.CurrentCulture,
                tb.FlowDirection, new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                tb.FontSize, tb.Foreground, VisualTreeHelper.GetDpi(tb).PixelsPerDip);
            return ft.Width;
        }

        private static Rect Bounds(Visual v, Visual ancestor)
        {
            var t = v.TransformToAncestor(ancestor);
            var size = (v as FrameworkElement) is { } fe
                ? new Size(fe.ActualWidth, fe.ActualHeight)
                : new Size(0, 0);
            return t.TransformBounds(new Rect(new Point(0, 0), size));
        }

        private static TextBlock? FindFirstText(DependencyObject root, string contains) =>
            FindAll<TextBlock>(root).FirstOrDefault(t => (t.Text ?? "").Contains(contains));

        private static bool HasAncestor<T>(DependencyObject node) where T : DependencyObject
        {
            for (var cur = VisualTreeHelper.GetParent(node); cur != null; cur = VisualTreeHelper.GetParent(cur))
            {
                if (cur is T) return true;
            }
            return false;
        }

        private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) yield return hit;
                foreach (var sub in FindAll<T>(child)) yield return sub;
            }
        }

        // ==================================================================
        //  桩
        // ==================================================================

        private sealed class FakeStepData : IStepConfigData
        {
            public Guid StepId { get; } = Guid.NewGuid();
            public string Icon { get; } = "";
            public string StepName { get; } = "BeadProbe";
            public string Description { get; } = "";
            public Dictionary<string, object> InputValues { get; } = new();
            public void SetInputValue(string key, object value) => InputValues[key] = value;
            public void RemoveInputValue(string key) => InputValues.Remove(key);
            public bool IsLinked(string inputPortName) => false;
            public string? GetLinkedAddress(string inputPortName) => null;
            public LinkReference? GetLink(string inputPortName) => null;
            public void SetLink(string inputPortName, LinkReference link) { }
            public void RemoveLink(string inputPortName) { }
            public List<DynamicPortInfo> OutputPortDefinitions { get; set; } = new();
        }

        private static void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? $"  ({detail})" : ""));
            if (!ok) _fail++;
        }
    }
}
