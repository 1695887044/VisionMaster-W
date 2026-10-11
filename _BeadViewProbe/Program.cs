using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        /// 视图支持尺寸下限：≥ 此尺寸下不允许出现任何裁切/塌陷（[CLIP] 计入失败）；
        /// 低于此只报 [LIMIT]（已知不支持，不计失败，但会打印在日志里，绝不静默）。
        /// 第三轮（2026-10-09）结构改版后口径下调到 620x540：右列只剩"工作区卡 + 状态条"，
        /// 左列设置卡全部自然高 + 滚动，实渲染无裁切。用户真机那档（914x547）必须落在承诺区间内 ——
        /// 否则它的问题只会被记成 [LIMIT] 而不是失败（前两轮就是这么漏掉真机症状的）。
        /// </summary>
        private const double SupportedMinWidth = 620;
        private const double SupportedMinHeight = 540;

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

        /// <summary>
        /// 用户真机第二问（2026-10-09）「行列数据显示不全」现场用的 11 个点：
        /// 与前 3 点的字面量取自用户截图（6 位小数 = 11 字符，是最宽的一档），其余接样图 14 控制点。
        /// 用它们量三件事：行内文字被不被裁 / 数值字面量完不完整 / 网格里能完整看见几行。
        /// </summary>
        private static readonly double[][] PointRows11 =
        {
            new[] { 609.007488, 440.566847 },
            new[] { 521.016408, 478.362075 },
            new[] { 448.610602, 444.022741 },
            new[] { 390.447000, 489.380000 },
            new[] { 354.247000, 546.093000 },
            new[] { 363.900000, 646.247000 },
            new[] { 400.130000, 722.267000 },
            new[] { 458.020000, 776.567000 },
            new[] { 509.907000, 826.040000 },
            new[] { 588.340000, 869.480000 },
            new[] { 659.533000, 912.920000 },
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
                // 用户实测「画布空间太小」的复现尺寸（2026-10-09 用户屏 1920x1080 @125% 下的配置窗内容区）
                ("1152x664_learn_ok", () => Capture(Scene.LearnOk, 1152, 664, "1152x664_learn_ok")),
                ("1152x664_learn_fail", () => Capture(Scene.LearnFail, 1152, 664, "1152x664_learn_fail")),
                ("620x640_learn_ok", () => Capture(Scene.LearnOk, 620, 640, "620x640_learn_ok")),
                // 用户现场的最可能形态：125% 缩放下 1250x1030 的窗口 → WPF 逻辑 ~1000x824（外壳占 160）
                // → 视图 900x664、右列 550 → 工具条 7 个按钮正好换到第 2 行 + 右列高度紧张
                ("1000x664_learn_ok", () => Capture(Scene.LearnOk, 1000, 664, "1000x664_learn_ok")),
                ("1000x664_learn_fail", () => Capture(Scene.LearnFail, 1000, 664, "1000x664_learn_fail")),
                ("shell_1040x824_learn_ok", () => CaptureShell(Scene.LearnOk, 1040, 824, "shell_1040x824_learn_ok")),
                ("shell_960x680_learn_ok", () => CaptureShell(Scene.LearnOk, 960, 680, "shell_960x680_learn_ok")),
                ("shell_1490x1030_learn_ok", () => CaptureShell(Scene.LearnOk, 1490, 1030, "shell_1490x1030_learn_ok")),
                ("shell_1192x824_learn_ok", () => CaptureShell(Scene.LearnOk, 1192, 824, "shell_1192x824_learn_ok")),
                // 2026-10-09 第三轮：用户真机截图换算出的实际视口（视口 914x547 = 外壳窗口 ~954x707 减去
                // 72 头 + 56 脚 + 内容边距 32）。用户反馈「布局不合适 / 不现代化 / 可视化也不行」的现场就是这一档，
                // 故它从"已知不支持尺寸"升格为断言尺寸：画布高、图片按高适配宽、工具条行数、状态可见性都在这里量。
                ("914x547_ref", () => Capture(Scene.RefImage, 914, 547, "914x547_ref")),
                ("914x547_learn_ok", () => Capture(Scene.LearnOk, 914, 547, "914x547_learn_ok")),
                ("914x547_learn_fail", () => Capture(Scene.LearnFail, 914, 547, "914x547_learn_fail")),
                ("914x547_empty", () => Capture(Scene.Empty, 914, 547, "914x547_empty")),
                // 同尺寸的外壳仿真（与用户截图的窗口形态一致，用于肉眼比对）
                ("shell_954x707_learn_ok", () => CaptureShell(Scene.LearnOk, 954, 707, "shell_954x707_learn_ok")),
                // 左列滚到「配方参数」的取证图：证明参数/点列在左列滚动范围内真的能到达、且不是"只剩表头"
                ("914x547_leftparams", () => CaptureScrolled(Scene.RefImage, 914, 547, "914x547_leftparams", "配方参数")),
                ("914x547_leftpoints", () => CaptureScrolled(Scene.RefImage, 914, 547, "914x547_leftpoints", "路径点列")),
                // 支持下限（620x540）本身也要有场景：承诺"≥ 此尺寸无裁切"就必须量过 —— 否则下限只是个没证据的数字
                ("620x540_ref", () => Capture(Scene.RefImage, 620, 540, "620x540_ref")),
                ("620x540_learn_ok", () => Capture(Scene.LearnOk, 620, 540, "620x540_learn_ok")),
                // 2026-10-09 第四轮：用户真机第二问「行列数据显示不全」的复现/验收。
                // 现场形态 = 平面可变形 + 没设参考图（底图退输入图）+ 11 个点（含 6 位小数坐标）。
                // _list 那档把左列滚到「路径点列」卡：验收线（≥6 行完整可见 / 0 裁字 / 数值完整）就在这档量。
                ("914x547_points11", () => Capture(Scene.Points11, 914, 547, "914x547_points11")),
                ("914x547_points11_list", () => CaptureScrolled(Scene.Points11, 914, 547, "914x547_points11_list", "路径点列")),
                ("1100x900_points11_list", () => CaptureScrolled(Scene.Points11, 1100, 900, "1100x900_points11_list", "路径点列")),
            };
            // 第二个参数 = 只跑标签含该串的场景（改一轮布局不用等全量）；逗号可给多个片段
            string[] only = args.Length > 1 ? args[1].Split(',') : null;
            foreach (var (tag, run) in shots)
            {
                if (only != null && !only.Any(p => tag.Contains(p)))
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

            RunLoadRefPickerChecks();
        }

        /// <summary>
        /// P1（2026-10-09 用户真机第一问「这个载入是不是没有效果」）：
        /// 「载入」在**路径为空 / 路径失效**时必须弹文件选择框（此前全插件没有文件选择器，
        /// 用户得在 200% 缩放的窗口里手打完整路径）；路径有效时保持原行为（直接载入）。
        ///
        /// 弹框本身在离屏宿主里弹不出来（没有交互桌面），所以把视图的
        /// <see cref="BeadInspectView.RefImagePicker"/> 换成返回固定路径的替身，
        /// 按钮用 RaiseEvent 走**真实 Click 链路**（Click → OnLoadRefClick → 选文件 → entry.RefImagePath
        /// → Plugin.LoadRefImage）——这就是"空路径走弹框分支"的代码级证据。
        /// （不用派生视图：XAML 根的 BAML 只能由它自己的类型 LoadComponent，派生类会抛
        /// "组件不具有由 URI 识别的资源"，2026-10-09 实测。）
        /// </summary>
        private static void RunLoadRefPickerChecks()
        {
            Console.WriteLine("=== P1「载入」文件选择自检 ===");
            var p6 = new BeadInspectPlugin { InstanceName = "胶路_载入空路径" };
            Window? win = null;
            try
            {
                p6.Initialize(new FakeStepData());
                p6.AlignMode = BeadAlignMode.PlanarDeformable;
                string refPath = Path.Combine(BeadDir, "adhesive_bead_ref.png");

                string? pickResult = refPath;      // 替身"用户在弹框里选了哪张图"（null = 点了取消）
                int pickCalls = 0;
                var view = new BeadInspectView
                {
                    DataContext = p6,
                    RefImagePicker = currentPath =>
                    {
                        pickCalls++;
                        Console.WriteLine($"     [PICK] 替身被调用（第 {pickCalls} 次，传入路径 = "
                            + (string.IsNullOrEmpty(currentPath) ? "(空)" : currentPath) + "）→ 返回 "
                            + (pickResult ?? "(取消)"));
                        return pickResult;
                    },
                };
                win = new Window
                {
                    Content = view,
                    Width = 760,
                    Height = 480,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 40,
                    Top = 40,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Title = "bead load-ref probe",
                };
                win.Show();
                Pump(400);

                var loadBtn = FindAll<Button>(view).FirstOrDefault(b => (b.Content as string) == "载入");
                Check("P1-a 视图里能定位「载入」按钮（Click 接线的真实入口）", loadBtn != null,
                    loadBtn == null ? "没找到" : $"实际宽={loadBtn.ActualWidth:F0}");
                if (loadBtn == null)
                    return;

                // ① 空路径 → 选文件分支被调用 → 路径回填 → 参考图真的上画布
                loadBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(300);
                Check("P1① 空路径点「载入」→ 走文件选择分支（1 次）", pickCalls == 1, $"pickCalls={pickCalls}");
                Check("P1① 选中路径写回 EditingEntry.RefImagePath（路径框随之刷新）",
                    p6.EditingEntry!.RefImagePath == refPath, p6.EditingEntry.RefImagePath ?? "(空)");
                Check("P1① 参考图真的上了画布（底图 = 参考图）", BaseHint(p6).Contains("参考图"), BaseHint(p6));

                // ② 用户点取消 → 路径不动、画布不动
                p6.EditingEntry.RefImagePath = "";
                p6.LoadRefImage(); // 让底图回到与"空路径"一致的状态
                pickResult = null;
                loadBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(250);
                Check("P1② 取消选文件 → 选择分支确实被走到（2 次）", pickCalls == 2, $"pickCalls={pickCalls}");
                Check("P1② 取消 → 路径仍为空（不写回）", string.IsNullOrEmpty(p6.EditingEntry.RefImagePath),
                    p6.EditingEntry.RefImagePath ?? "(空)");
                Check("P1② 取消 → 状态栏仍是「还没有参考图路径」那条（没被当成载入成功）",
                    p6.StatusMessage.Contains("还没有参考图路径"), p6.StatusMessage);

                // ③ 路径有效 → 不弹框（保持原行为：直接载入）
                p6.EditingEntry.RefImagePath = refPath;
                loadBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(250);
                Check("P1③ 路径有效点「载入」→ 不弹框（pickCalls 仍为 2）", pickCalls == 2, $"pickCalls={pickCalls}");
                Check("P1③ 路径有效 → 直接载入画布（底图 = 参考图）", BaseHint(p6).Contains("参考图"), BaseHint(p6));

                // ④ 路径填了但文件不存在（用户手打错路径）→ 也要弹框兜底
                p6.EditingEntry.RefImagePath = Path.Combine(BeadDir, "not_exist_2026.png");
                loadBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(250);
                Check("P1④ 路径失效点「载入」→ 走文件选择分支（3 次）", pickCalls == 3, $"pickCalls={pickCalls}");

                // ⑤ 悬停提示：必须自己说清"空路径会弹框"，否则用户还是会以为要点两次
                Check("P1⑤ 「载入」的 ToolTip 说明了弹框行为（悬停可自证）",
                    (loadBtn.ToolTip as string)?.Contains("弹文件选择框") == true,
                    loadBtn.ToolTip as string ?? "(无 ToolTip)");
            }
            catch (Exception ex)
            {
                Console.WriteLine("### P1 自检崩溃: " + ex);
                _fail++;
            }
            finally
            {
                try { win?.Close(); } catch { }
                try { p6.Dispose(); } catch { }
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

        private enum Scene { Empty, InputImage, RefImage, LearnFail, LearnOk, Points11 }

        /// <summary>裸视图离屏渲染：width/height = 插件视图自己拿到的尺寸（不含外壳）</summary>
        private static void Capture(Scene scene, double width, double height, string tag) =>
            CaptureCore(scene, width, height, tag, false);

        /// <summary>
        /// 裸视图 + 先把左列 ScrollViewer 滚到某个锚点文字处再拍（取证"参数/点列在左列滚动范围内可达"）。
        /// 锚点用中文匹配 TextBlock.Text.Contains，脚本侧不传中文（都在 Program.cs 里）。
        /// </summary>
        private static void CaptureScrolled(Scene scene, double width, double height, string tag, string leftAnchor) =>
            CaptureCore(scene, width, height, tag, false, leftAnchor);

        /// <summary>
        /// 带插件配置外壳的仿真（PluginConfigShellView：72 头部 + 56 底部 + 内容区 Margin 20,16）。
        /// width/height = **外壳窗口**尺寸 —— 插件视图实际只拿到 (width-40) x (height-160)。
        /// 这是"用户现场到底有多少可用高"的权威口径：裸视图口径会凭空多出 160px。
        /// </summary>
        private static void CaptureShell(Scene scene, double width, double height, string tag) =>
            CaptureCore(scene, width, height, tag, true);

        private static void CaptureCore(Scene scene, double width, double height, string tag, bool shell,
            string leftScrollAnchor = null)
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
                if (scene == Scene.Points11)
                {
                    // 用户现场：平面可变形 + 未设参考图（底图=输入图兜底，画布能拾取），逐点拾取 11 个点
                    plugin.AlignMode = BeadAlignMode.PlanarDeformable;
                    using var input = LoadImage(1);
                    plugin.SrcImage.Value = input;
                    for (int i = 0; i < PointRows11.Length; i++)
                        plugin.AddPoint(PointRows11[i][0], PointRows11[i][1]);
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

                // 左列取证：滚到锚点卡片（"参数在左列可达"的证据图），再逼一次重排
                if (leftScrollAnchor != null)
                {
                    ScrollLeftColumnTo(host, leftScrollAnchor);
                    Pump(250);
                    host.UpdateLayout();
                    host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                }

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

        /// <summary>把左列 ScrollViewer 滚到锚点卡片（取"参数/点列在左列滚动范围内可达"的证据图）</summary>
        private static void ScrollLeftColumnTo(FrameworkElement root, string anchor)
        {
            var tb = FindFirstText(root, anchor);
            if (tb == null)
            {
                Console.WriteLine($"   [SCROLL] 找不到锚点 \"{anchor}\"，跳过左列滚动");
                return;
            }
            var sv = Ancestors(tb).OfType<ScrollViewer>().FirstOrDefault(s => s.ScrollableHeight > 0);
            if (sv == null)
            {
                Console.WriteLine($"   [SCROLL] 锚点 \"{anchor}\" 不在可滚容器里");
                return;
            }
            double target = Math.Max(0, sv.VerticalOffset + Bounds(tb, sv).Top - 4);
            sv.ScrollToVerticalOffset(target);
            Console.WriteLine($"   [SCROLL] 左列滚到 \"{anchor}\"：目标 offset={target:F0}"
                + $"（范围 0..{sv.ScrollableHeight:F0}，视口 {sv.ViewportHeight:F0}，内容 {sv.ExtentHeight:F0}）");
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

            // 工具条按钮：只数真正的工具条（≥4 个按钮的 WrapPanel；画布标题行里的「适应图片」不算）
            var toolbarPanel = FindAll<WrapPanel>(root).FirstOrDefault(w => w.Children.OfType<Button>().Count() >= 4);
            var buttons = toolbarPanel == null
                ? new List<Button>()
                : toolbarPanel.Children.OfType<Button>()
                    .Where(b => b.Visibility == Visibility.Visible && b.ActualWidth > 0).ToList();
            var rows = buttons.Select(b => Math.Round(Bounds(b, root).Top)).Distinct().Count();
            bool wideEnough = FindView(root).ActualWidth >= 900;
            Console.WriteLine($"   [{tag}] 工具栏按钮 {buttons.Count} 个 / 占 {rows} 行"
                + $"；文字裁切 {clipped.Count} 处；省略号截断 {trimmed.Count} 处");
            if (wideEnough && rows > 1)
            {
                Console.WriteLine("     [CLIP] 工具条在 ≥900 宽下换行了（验收线：工具条一行、按钮文字 0 裁切）");
                _fail++;
            }
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

            // 空态提示（画布中央那条：用它的第二行文字定位——"绑定输入图，或"只出现在这条里，
            // 状态栏那句是"绑定上游输入图"，避免两个 TextBlock 撞名）
            var empty = FindFirstText(root, "绑定输入图，或");
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

            // 左列设置卡的可达性 + 卡内控件是否自然高（"参数/点列无需特殊操作即可在滚动范围内到达"）
            var canvasTitle = FindFirstText(root, "胶路中心线拾取");
            if (canvasTitle != null)
                DumpChain(canvasTitle, root, tag, "画布标题");

            AuditLayout(root, tag, plugin);
        }

        // ==================================================================
        //  布局审计（第三轮结构：右列 = 工作区卡（工具条 + 横幅 + 画布 hero）+ 状态条；设置卡全在左列滚动区）
        //  判据全是硬指标：
        //    ① 画布图片可视区（ImageEdit 的实际矩形）高 ≥ 330、按高适配后宽 ≥ 400 —— 用户尺寸验收线；
        //    ② 右列两行高之和 ≤ 可用高（Grid 不滚动，超出即被父级裁掉，用户"看不见"）；
        //    ③ 每块的 bounds 都在右列 / 视口内；
        //    ④ 左列每张设置卡都在 ScrollViewer 的滚动范围内，且卡内控件是自然高（不塌成"只剩表头"）。
        // ==================================================================
        private static void AuditLayout(FrameworkElement root, string tag, BeadInspectPlugin plugin)
        {
            var canvas = FindAll<FrameworkElement>(root).FirstOrDefault(e => e.GetType().Name == "ImageEdit");
            if (canvas == null)
            {
                Console.WriteLine($"   [{tag}] [FAIL] 找不到画布控件 ImageEdit（布局审计锚点丢失）");
                _fail++;
                return;
            }
            var host = FindView(root);
            bool userSize = host.ActualWidth >= 900 && host.ActualHeight >= 540;
            string banner = Reflect<string>(plugin, "ActionBanner") ?? "";

            // ── ① 画布图片可视区（本轮硬验收线；横幅出现时画布必然让位，见下面分支的说明）──
            var cb = Bounds(canvas, root);
            const double imgAspect = 1280.0 / 1024.0;   // 样图与输入图都是 1280x1024
            double fitW = cb.Height * imgAspect;
            Console.WriteLine($"   [{tag}] 画布可视区 {Fmt(cb)} 高={cb.Height:F0} 宽={cb.Width:F0}"
                + $" → 按高适配后图宽={fitW:F0}（比例 {imgAspect:0.###}） 横幅={(banner.Length > 0 ? "有" : "无")}");
            if (userSize && banner.Length == 0)
            {
                if (cb.Height < 330)
                {
                    Console.WriteLine($"     [CLIP] 画布可视区高 {cb.Height:F0} < 330（用户尺寸验收线，无横幅态）");
                    _fail++;
                }
                if (fitW < 400)
                {
                    Console.WriteLine($"     [CLIP] 图片按高适配后宽 {fitW:F0} < 400（用户尺寸验收线，无横幅态）");
                    _fail++;
                }
            }
            else if (userSize)
            {
                // 有横幅时画布必然让位（"点完学习立刻看见结论"是 17a/17e 的硬要求，优先级高于画布）：
                // 底线降到 300 —— 仍 ≥ 旧结构同尺寸实测值的 3 倍；横幅只占一行时基本回到 330 附近。
                if (cb.Height < 300)
                {
                    Console.WriteLine($"     [CLIP] 有横幅时画布可视区高 {cb.Height:F0} < 300（横幅态底线）");
                    _fail++;
                }
                else
                {
                    Console.WriteLine($"     [INFO] 横幅态：画布让位到 {cb.Height:F0}（≥300 底线；无横幅态见同尺寸 *_ref 场景）");
                }
            }
            else
            {
                Console.WriteLine($"     [INFO] 视图 {host.ActualWidth:F0}x{host.ActualHeight:F0} 小于用户尺寸，"
                    + $"画布 {cb.Height:F0} 高仅记录（验收线只在 ≥900x540 生效）");
            }

            // ── ② 右列两行：工作区卡 + 状态条 ──
            var right = Ancestors(canvas).OfType<Grid>().FirstOrDefault(g =>
                g.RowDefinitions.Count >= 2
                && FindAll<TextBlock>(g).Any(t => (t.Text ?? "") == plugin.StatusMessage));
            if (right == null)
            {
                Console.WriteLine($"   [{tag}] [FAIL] 找不到右列 Grid（布局审计锚点丢失）");
                _fail++;
            }
            else
            {
                var rows = right.RowDefinitions;
                double sum = rows.Sum(r => r.ActualHeight);
                var gridBounds = Bounds(right, root);
                Console.WriteLine($"   [{tag}] 右列 可用={right.ActualWidth:F0}x{right.ActualHeight:F0}"
                    + $" rows=[{string.Join(",", rows.Select(r => r.ActualHeight.ToString("F0")))}]"
                    + $" heights=[{string.Join(",", rows.Select(r => r.Height.ToString()))}]"
                    + $" min=[{string.Join(",", rows.Select(r => r.MinHeight.ToString("F0")))}]"
                    + $" 合计={sum:F0}");
                if (gridBounds.Bottom > root.ActualHeight + 0.5 || sum > right.ActualHeight + 0.5)
                {
                    ClipOrLimit(root, $"右列行高合计 {sum:F0} > 可用 {right.ActualHeight:F0}"
                        + $"（右列下沿 y={gridBounds.Bottom:F0} / 视图 {root.ActualHeight:F0}）");
                }

                var statusText = FindAll<TextBlock>(root).FirstOrDefault(t => (t.Text ?? "") == plugin.StatusMessage);
                foreach (var (name, inner) in new (string, DependencyObject)[]
                {
                    ("工作区卡", FindFirstText(root, "胶路中心线拾取") ?? root),
                    ("状态条", statusText ?? root),
                })
                {
                    var card = OutermostCardBorder(inner, right);
                    if (card == null)
                    {
                        Console.WriteLine($"   [{tag}] [FAIL] 定位不到「{name}」容器（布局锚点丢失）");
                        _fail++;
                        continue;
                    }
                    var b = Bounds(card, right);
                    var vb = Bounds(card, root);
                    bool inGrid = b.Top >= -0.5 && b.Bottom <= right.ActualHeight + 0.5
                                  && b.Left >= -0.5 && b.Right <= right.ActualWidth + 0.5;
                    bool inView = vb.Top >= -0.5 && vb.Bottom <= root.ActualHeight + 0.5
                                  && vb.Left >= -0.5 && vb.Right <= root.ActualWidth + 0.5;
                    Console.WriteLine($"   [{tag}] 块·{name} bounds={Fmt(b)} 视图内y={vb.Top:F0}..{vb.Bottom:F0}"
                        + $" 高={card.ActualHeight:F0} 在右列内={inGrid} 在视图内={inView}");
                    if (!inView)
                        ClipOrLimit(root, $"「{name}」下沿 {vb.Bottom:F0} 越过视图 {root.ActualHeight:F0} = 用户看不到");
                }

                // 状态条内容完整可见（StatusMessage + 底图行）——"状态行完整可见"的判据
                if (statusText != null)
                {
                    var rb = Bounds(statusText, root);
                    Console.WriteLine($"   [{tag}] 状态文案 {Fmt(rb)} 高={statusText.ActualHeight:F0}"
                        + $" 在视口内={rb.Top >= -0.5 && rb.Bottom <= root.ActualHeight + 0.5}");
                    if (rb.Bottom > root.ActualHeight + 0.5)
                        ClipOrLimit(root, $"状态文案下沿 {rb.Bottom:F0} 越过视图 {root.ActualHeight:F0}");
                }
            }

            // ── ③ 工具条：行数与逐按钮宽（"一行不换行、按钮文字 0 裁切"的判据）──
            var wrap = FindAll<WrapPanel>(root).FirstOrDefault(w => w.Children.OfType<Button>().Count() >= 4);
            if (wrap != null)
            {
                var kids = wrap.Children.OfType<FrameworkElement>().ToList();
                double need = kids.Sum(k => k.DesiredSize.Width + k.Margin.Left + k.Margin.Right);
                double actual = kids.Sum(k => k.ActualWidth + k.Margin.Left + k.Margin.Right);
                int lines = kids.Select(k => Math.Round(Bounds(k, wrap).Top)).Distinct().Count();
                Console.WriteLine($"   [{tag}] 工具条 可用宽={wrap.ActualWidth:F0}"
                    + $" 一行实际占宽={actual:F0}（预算={need:F0}） 实占={lines} 行 按钮={kids.Count}"
                    + $" 最宽按钮={kids.Max(k => k.ActualWidth):F0}");
                // 逐按钮宽度：重排时"哪几个按钮必须让位"就靠这张表（MinWidth / 文字宽 / 实际宽）
                foreach (var k in kids)
                {
                    string label = k switch
                    {
                        Button b when b.Content is string s => s,
                        Button b when b.Content is TextBlock t => t.Text ?? "",
                        _ => k.GetType().Name,
                    };
                    double minW = k is Button bb ? bb.MinWidth : 0;
                    Console.WriteLine($"   [{tag}]   按钮·{label} 实际={k.ActualWidth:F0} 需求={k.DesiredSize.Width:F0}"
                        + $" MinWidth={minW:F0} 行y={Bounds(k, wrap).Top:F0}");
                }
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

            AuditLeftColumn(root, tag, plugin);
        }

        /// <summary>
        /// 左列设置栏审计（第三轮：参数/点列/预览迁入左列后的护栏）：
        ///   ① 每张设置卡都在 ScrollViewer 的滚动范围内（滚到底能看到 = 「无需特殊操作即可到达」）；
        ///   ② 「配方参数」「路径点列」两张卡不塌（旧结构下它们被压到只剩表头）；
        ///   ③ 卡内控件是自然高：4 个配方参数控件、点列 DataGrid（含可见行数）、6 个搜索参数。
        /// </summary>
        private static void AuditLeftColumn(FrameworkElement root, string tag, BeadInspectPlugin plugin)
        {
            var anchor = FindFirstText(root, "输入绑定");
            var sv = anchor == null ? null : Ancestors(anchor).OfType<ScrollViewer>().FirstOrDefault(s => s.ScrollableHeight > 0);
            if (sv == null)
            {
                Console.WriteLine($"   [{tag}] [FAIL] 左列滚动区定位失败（布局审计锚点丢失）");
                _fail++;
                return;
            }
            Console.WriteLine($"   [{tag}] 左列 视口={sv.ViewportWidth:F0}x{sv.ViewportHeight:F0}"
                + $" 内容高={sv.ExtentHeight:F0} 可滚={sv.ScrollableHeight:F0}");

            foreach (var title in new[] { "输入绑定", "配方库", "路径点列", "配方参数", "运行参数", "平面匹配搜索范围", "预览（学习叠加", "怎么用" })
            {
                var t = FindFirstText(root, title);
                if (t == null)
                {
                    Console.WriteLine($"   [{tag}] [FAIL] 左列卡「{title}」不在视图里（绑定/控制项丢了）");
                    _fail++;
                    continue;
                }
                var card = OutermostCardBorder(t, sv);
                if (card == null)
                {
                    Console.WriteLine($"   [{tag}] [FAIL] 左列卡「{title}」容器定位失败");
                    _fail++;
                    continue;
                }
                var b = Bounds(card, sv);
                bool reachable = b.Bottom <= sv.ExtentHeight + 0.5;
                Console.WriteLine($"   [{tag}] 左列卡·{title} 高={card.ActualHeight:F0} 内容y={b.Top:F0}..{b.Bottom:F0}"
                    + $" 在滚动范围内={reachable} 可见={card.Visibility}");
                if (!reachable)
                {
                    Console.WriteLine($"     [CLIP] 左列「{title}」卡超出滚动内容（滚到底也看不到）");
                    _fail++;
                }
                if ((title == "配方参数" || title == "路径点列") && card.Visibility == Visibility.Visible && card.ActualHeight < 150)
                {
                    Console.WriteLine($"     [CLIP] 左列「{title}」卡高 {card.ActualHeight:F0} < 150：只剩表头的塌陷态");
                    _fail++;
                }
            }

            // 点列 DataGrid：行高/裁字/列宽/可见行数（用户真机第二问的度量面）
            AuditPointGrid(root, tag, plugin);

            // 配方参数 4 个控件（胶宽/容差/位置/极性）：按标签 TextBlock 找到同一 Grid 里的控件量高
            foreach (var label in new[] { "胶宽(px)", "容差(px)", "位置(px)", "极性" })
            {
                var lb = FindFirstText(root, label);
                var cell = lb?.Parent as Grid;
                var ctrl = cell == null ? null : FindAll<FrameworkElement>(cell)
                    .FirstOrDefault(e => e is TextBox || e is ComboBox);
                Console.WriteLine($"   [{tag}] 配方参数·{label} 控件高={(ctrl?.ActualHeight ?? 0):F0}"
                    + (ctrl == null ? " [FAIL] 控件不在同一 Grid" : ""));
                if (ctrl == null || ctrl.ActualHeight < 20)
                {
                    Console.WriteLine($"     [CLIP] 配方参数「{label}」输入控件高 {(ctrl?.ActualHeight ?? 0):F0} < 20（被压扁）");
                    _fail++;
                }
            }

            // 6 个平面匹配搜索参数：都在左列且自然高（断言 16d 只管绑定在不在，这里管"看不看得见"）
            int searchBoxes = 0;
            foreach (var label in new[] { "起始角(°)", "角度范围(°)", "缩放R最小", "缩放R最大", "缩放C最小", "缩放C最大" })
            {
                var lb = FindFirstText(root, label);
                var cell = lb?.Parent as Grid;
                var ctrl = cell == null ? null : FindAll<FrameworkElement>(cell).FirstOrDefault(e => e is TextBox);
                if (ctrl != null && ctrl.ActualHeight >= 20)
                    searchBoxes++;
            }
            Console.WriteLine($"   [{tag}] 平面匹配搜索参数 可见且自然高 {searchBoxes}/6");
            if (searchBoxes < 6)
            {
                Console.WriteLine($"     [CLIP] 搜索范围参数只有 {searchBoxes}/6 个是可见且自然高的");
                _fail++;
            }

            // 预览卡（迁入左列后）：无图时折叠；有图时给固定高（不再是一条黑缝）
            var pv = FindFirstText(root, "预览（学习叠加");
            if (pv != null)
            {
                var pvCard = OutermostCardBorder(pv, sv);
                var img = pvCard == null ? null : FindAll<FrameworkElement>(pvCard).FirstOrDefault(e => e.GetType().Name == "ImageReadOnly");
                Console.WriteLine($"   [{tag}] 预览卡 可见={pvCard?.Visibility} 高={pvCard?.ActualHeight ?? 0:F0}"
                    + $" 预览控件高={(img?.ActualHeight ?? 0):F0}");
            }
        }

        /// <summary>
        /// 路径点列表（DataGrid）专项审计 —— 用户真机第二问「行列数据显示不全」的度量面。
        ///
        /// 2026-10-09 复现到的账（共享主题的 DataGridCell Padding=12,8 + 本视图 RowHeight=24）：
        /// 主题里 12px 字一行高 16px，行高 24 减去上下内边距 16 只剩 8px —— 文字上下各溢出 4px，
        /// 被邻行的交替底色 / 横格线吃掉 = 屏幕上"半截数字"；列头 Padding=12,10（36px 高）再白吃 10px。
        ///
        /// 本方法量四件事，并给出硬断言（≥6 行完整可见 / 0 纵向溢出 / 0 横向裁字 / 数值字面量完整），
        /// 断言只在"11 个点"的验收场景生效（其余场景只记录，避免老场景因历史体量误报）：
        ///   ① 网格里能完整看见几行（列头下沿 → 网格下沿，完整落在里面才算）；
        ///   ② 每个文本单元格：文字矩形是否溢出所在行 / 文字所需宽是否大于文本块实宽；
        ///   ③ Row/Col 数值的字面量是否完整（绑定没被格式化）+ 是否超过单元格内容宽；
        ///   ④ 删列按钮是否落在网格宽度内（被挤出右缘 = 用户看不见删除入口）。
        /// </summary>
        private static void AuditPointGrid(FrameworkElement root, string tag, BeadInspectPlugin plugin)
        {
            var dg = FindAll<DataGrid>(root).FirstOrDefault();
            if (dg == null)
            {
                Console.WriteLine($"   [{tag}] [FAIL] 找不到点列 DataGrid");
                _fail++;
                return;
            }

            var dgRect = Bounds(dg, root);
            var headers = FindAll<DataGridColumnHeader>(dg).ToList();
            double headerBottom = headers.Count == 0 ? 0 : headers.Max(h => Bounds(h, dg).Bottom);
            double headerH = headers.Count == 0 ? 0 : headers.Max(h => h.ActualHeight);
            var cellStyle = FindAll<DataGridCell>(dg).FirstOrDefault();
            var colW = string.Join(" ", dg.Columns.Select((c, i) =>
                $"{(c.Header as string) ?? (i == dg.Columns.Count - 1 ? "删" : "?")}={c.ActualWidth:F0}"));
            Console.WriteLine($"   [{tag}] 点列 DataGrid 高={dg.ActualHeight:F0} 行={dg.Items.Count}"
                + $" RowHeight={dg.RowHeight:F0} 列头高={headerH:F0} FontSize={dg.FontSize:F0}"
                + $" 网格宽={dg.ActualWidth:F0} 列宽=[{colW}]"
                + (cellStyle == null ? "" : $" 单元格内边距={cellStyle.Padding}"));

            var rows = FindAll<DataGridRow>(dg).ToList();
            int fit = 0, overflowV = 0, clipH = 0, cells = 0;
            var details = new List<string>();
            var literalMissing = new List<string>();
            string deleteNote = "删按钮=未找到";
            double maxNeedW = 0;
            foreach (var row in rows)
            {
                var rb = Bounds(row, dg);
                if (rb.Top >= headerBottom - 0.5 && rb.Bottom <= dg.ActualHeight - 1 + 0.5)
                    fit++;
                int rowNo = dg.Items.IndexOf(row.Item) + 1;
                if (row.Item is BeadPointRow pt)
                {
                    // 字面量完整（绑定没被格式化/四舍五入）：拿行对象自己的 ToString 逐字比对
                    var texts = FindAll<TextBlock>(row).Select(t => t.Text ?? "").ToList();
                    if (!texts.Contains(pt.Row.ToString())) literalMissing.Add(pt.Row.ToString());
                    if (!texts.Contains(pt.Col.ToString())) literalMissing.Add(pt.Col.ToString());
                }
                foreach (var cell in FindAll<DataGridCell>(row))
                {
                    // 单元格"内容槽" = 单元格矩形减去内边距（主题 12,8 那档只有 24-16=8px 高）
                    var cb = Bounds(cell, row);
                    double slotH = cb.Height - cell.Padding.Top - cell.Padding.Bottom;
                    double slotW = cb.Width - cell.Padding.Left - cell.Padding.Right;
                    foreach (var tb in FindAll<TextBlock>(cell))
                    {
                        cells++;
                        double needW = MeasureText(tb);
                        double needH = TextHeight(tb);
                        maxNeedW = Math.Max(maxNeedW, needW);
                        // 纵向裁字：一行字比内容槽还高 —— 文字被裁在槽里（用户截图"半截数字"的来历）
                        bool vOver = needH > slotH + 0.5;
                        bool hClip = needW > slotW + 0.5 || needW > tb.ActualWidth + 0.5;
                        if (vOver) overflowV++;
                        if (hClip) clipH++;
                        if ((vOver || hClip) && details.Count < 8)
                            details.Add($"行{rowNo} \"{tb.Text}\" 内容槽={slotW:F1}x{slotH:F1}"
                                + $" 需={needW:F1}x{needH:F1} 行盒={rb.Height:F0} 内边距={cell.Padding}"
                                + (vOver ? " [纵向裁字]" : "") + (hClip ? " [横向裁字]" : ""));
                    }
                    var btn = FindAll<Button>(cell).FirstOrDefault();
                    if (btn != null)
                    {
                        var bb = Bounds(btn, dg);
                        bool inside = bb.Left >= -0.5 && bb.Right <= dg.ActualWidth + 0.5;
                        deleteNote = $"删按钮 {Fmt(bb)} 高={btn.ActualHeight:F0} 在网格宽内={inside}";
                        if (!inside)
                        {
                            Console.WriteLine($"     [CLIP] 点列删按钮被挤出网格右缘：{Fmt(bb)} 网格宽={dg.ActualWidth:F0}");
                            _fail++;
                        }
                        if (btn.ActualHeight > slotH + 0.5)
                        {
                            Console.WriteLine($"     [CLIP] 点列删按钮高 {btn.ActualHeight:F0} > 单元格内容槽 {slotH:F0}"
                                + "（溢出到邻行、盖住上下的行）");
                            _fail++;
                        }
                    }
                }
            }

            bool strict = dg.Items.Count >= 11;   // 11 点 = 用户现场，验收线在这档

            Console.WriteLine($"   [{tag}] 点列 可见行（网格内）= {fit}/{dg.Items.Count} 文本单元格={cells}"
                + $" 纵向溢出={overflowV} 横向裁字={clipH} 最宽文本={maxNeedW:F1} {deleteNote}"
                + (strict ? $" 字面量缺失={literalMissing.Count}" : ""));
            foreach (var d in details)
                Console.WriteLine($"     [CELL] {d}");

            if (dg.ActualHeight < 140)
            {
                Console.WriteLine($"     [CLIP] 点列 DataGrid 高 {dg.ActualHeight:F0} < 140（只剩两三行就失去复核价值）");
                _fail++;
            }
            if (overflowV > 0)
            {
                Console.WriteLine($"     [CLIP] 点列 {overflowV} 个单元格的文字纵向溢出所在行（行高与字号不匹配 = 半截数字）");
                _fail++;
            }
            if (clipH > 0)
            {
                Console.WriteLine($"     [CLIP] 点列 {clipH} 个单元格的文字横向被裁（列宽装不下 Row/Col 数值）");
                _fail++;
            }
            if (strict && fit < 6)
            {
                Console.WriteLine($"     [CLIP] 点列只能完整看见 {fit} 行 < 6 行（11 个点的复核面）");
                _fail++;
            }
            if (literalMissing.Count > 0)
            {
                Console.WriteLine($"     [CLIP] 点列数值字面量不完整（被格式化/四舍五入）："
                    + string.Join(",", literalMissing.Take(6)));
                _fail++;
            }

            // 卡内层叠：「位置容差」提示必须在网格之下、且在卡片矩形内（11 点撑高后不许被网格压住/越出卡缘）
            var hint = FindFirstText(root, "位置容差");
            var leftSv = hint == null ? null : Ancestors(hint).OfType<ScrollViewer>().FirstOrDefault();
            var card = hint == null ? null : OutermostCardBorder(hint, (DependencyObject?)leftSv ?? root);
            if (hint == null || card == null)
            {
                Console.WriteLine($"   [{tag}] [FAIL] 点列下方的「位置容差」提示找不到（或卡片定位失败）");
                _fail++;
            }
            else
            {
                var hb = Bounds(hint, root);
                var kb = Bounds(card, root);
                bool below = hb.Top >= Bounds(dg, root).Bottom - 0.5;
                bool insideCard = hb.Left >= kb.Left - 0.5 && hb.Right <= kb.Right + 0.5 && hb.Bottom <= kb.Bottom + 0.5;
                Console.WriteLine($"   [{tag}] 点列下提示 {Fmt(hb)} 在网格下={below} 在卡内={insideCard} 卡片={Fmt(kb)}");
                if (!below || !insideCard)
                {
                    Console.WriteLine("     [CLIP] 「位置容差」提示与点列/卡片层叠异常");
                    _fail++;
                }
            }

            // 编辑态：「任何行不裁字」的另一半 —— 用户双击单元格改坐标时，编辑框也得在行内装得下
            if (strict)
                MeasurePointEditState(dg, rows, tag);
        }

        /// <summary>
        /// 点列表编辑态度量：让第一行的 Row 单元格进入编辑，量编辑框（TextBox）在行内装不装得下。
        /// 主题的 TextBox（Padding=10,8 / FontSize=13，自然高 35）塞进 26px 的行 = 编辑框压住上下两行；
        /// 本视图用 BeadPointEditBox 就地收窄（20 ≤ 内容槽 21）。
        /// 进编辑失败只记录（不同 WPF 版本对虚拟化单元格的 BeginEdit 容忍度不同），不误伤主断言。
        /// </summary>
        private static void MeasurePointEditState(DataGrid dg, List<DataGridRow> rows, string tag)
        {
            try
            {
                var row0 = rows.FirstOrDefault(r => r.Item is BeadPointRow);
                var cell0 = row0 == null ? null : FindAll<DataGridCell>(row0).Skip(1).FirstOrDefault();
                if (cell0 == null)
                {
                    Console.WriteLine($"   [{tag}] 点列编辑态：(找不到可编辑单元格，跳过)");
                    return;
                }
                dg.CurrentCell = new DataGridCellInfo(cell0);
                if (!dg.BeginEdit())
                {
                    Console.WriteLine($"   [{tag}] 点列编辑态：(BeginEdit 被拒，跳过)");
                    return;
                }
                dg.UpdateLayout();
                var box = FindAll<TextBox>(cell0).FirstOrDefault();
                var cellRect = Bounds(cell0, row0);
                double slotH = cellRect.Height - cell0.Padding.Top - cell0.Padding.Bottom;
                if (box == null)
                {
                    Console.WriteLine($"   [{tag}] 点列编辑态：单元格里没有 TextBox（编辑元素不是 TextBox？）");
                }
                else
                {
                    Console.WriteLine($"   [{tag}] 点列编辑态 编辑框高={box.ActualHeight:F0} 文本=\"{box.Text}\""
                        + $" 单元格={cellRect.Width:F0}x{cellRect.Height:F0} 内容槽高={slotH:F0}");
                    if (box.ActualHeight > slotH + 0.5)
                    {
                        Console.WriteLine($"     [CLIP] 点列编辑态：编辑框高 {box.ActualHeight:F0} > 内容槽 {slotH:F0}"
                            + "（改坐标时编辑框压住上下行）");
                        _fail++;
                    }
                }
                dg.CancelEdit();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   [{tag}] [INFO] 点列编辑态未能测量：{ex.Message}");
            }
        }

        /// <summary>单项文本的排版高度（单行，用于判断"行高装不装得下这行字"）</summary>
        private static double TextHeight(TextBlock tb)
        {
            var ft = new FormattedText(tb.Text ?? "", System.Globalization.CultureInfo.CurrentCulture,
                tb.FlowDirection, new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                tb.FontSize, tb.Foreground, VisualTreeHelper.GetDpi(tb).PixelsPerDip);
            return ft.Height;
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
