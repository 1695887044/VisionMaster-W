using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Events;
using Core.Interfaces;
using HalconDotNet;
using Newtonsoft.Json;
using Plugin.BeadInspect;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 胶路检测（Plugin.BeadInspect）的断言（方案说明书 §7.2 全量：1~15）。
    /// 第一批已落 1~9、13、14、15；第二批补 10（自动提取）/ 11（mm 失配三态）/ 12（插点保序）。
    ///
    /// 真值策略：用仓库真样图 Image\bead\adhesive_bead_01..07.png + adhesive_bead_ref.png
    /// （全部 1280×1024 单通道），真值表 = 探针 P4 实测（范例控制点 + 容差 8）：
    ///   01/02/04 → 0 段（OK）；03 → 2 段（太细 1 + 偏移 1）；05 → 3 段（太粗 1 + 缺胶 1 + 太细 1）；
    ///   06 → 1 段（缺胶 1）；07 → 4 段（偏移 3 + 缺胶 1）。
    ///
    /// 人工拾取路径 = 探针 RefRows/RefCols 那 14 个控制点（矫正坐标系下的折线顶点，P1 折线口径）；
    /// 矫正四点 = 探针 StageB 的源四点 + 范例目标矩形（300..948 列 / 300..708 行），
    /// 以显式 src/dst 形式存进 RectifyQuadJson，保证与探针坐标系逐像素一致。
    /// </summary>
    internal static class BeadInspectChecks
    {
        private const string BeadDir = @"D:\C#\VM\Image\bead";

        // 范例 14 控制点（探针 RefRows / RefCols，矫正坐标系下的折线顶点）
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

        // 探针矫正四点（源 = 参考图上的平面四角；目标 = 范例 300..948 列 / 300..708 行）
        private static readonly double[] QuadRows = { 658.232, 291.923, 314.817, 691.533 };
        private static readonly double[] QuadCols = { 330.071, 316.617, 971.032, 947.008 };
        private static readonly double[] DstRows = { 708, 300, 300, 708 };
        private static readonly double[] DstCols = { 300, 300, 948, 948 };

        // P4 真值表：(错误段总数, 缺胶, 太细, 太粗, 位置偏移)
        private static readonly (int total, int noBead, int thin, int thick, int mispos)[] Truth =
        {
            (0, 0, 0, 0, 0), // 01 OK
            (0, 0, 0, 0, 0), // 02 OK
            (2, 0, 1, 0, 1), // 03 太细1 + 偏移1
            (0, 0, 0, 0, 0), // 04 OK
            (3, 1, 1, 1, 0), // 05 太粗1 + 缺胶1 + 太细1
            (1, 1, 0, 0, 0), // 06 缺胶1
            (4, 1, 0, 0, 3), // 07 偏移3 + 缺胶1
        };

        public static void Run()
        {
            Section("[BeadInspect] 胶路检测插件（真样图 Image\\bead，P4 真值表）");

            bool images = Directory.Exists(BeadDir)
                          && File.Exists(Path.Combine(BeadDir, "adhesive_bead_01.png"))
                          && File.Exists(Path.Combine(BeadDir, "adhesive_bead_ref.png"));
            Check("样图就位（Image\\bead 01..07 + ref）", images, BeadDir);
            if (!images)
                return;

            Check1_PathGeneration();
            Check2_BeadModel();
            Check3456_TruthTableAndSemantics();
            Check7_UnitConversion();
            Check8_PortSurface();
            Check9_ModelCache();

            var fixture = BuildFixture(out string fixtureError);
            Check("夹具 对齐夹具就绪（矫正 + planar 模型 + 双向对齐）", fixture != null, fixtureError);
            if (fixture != null)
            {
                try
                {
                    Check10_AutoExtract(fixture);
                    Check13_TreeDiameter(fixture);
                    Check15_PathDeviation(fixture);
                }
                finally
                {
                    fixture.Dispose();
                }
            }

            Check11_MmMismatch();
            Check12_InsertDeleteOrder();
            Check14_TargetWidthClamp();
            CheckUI_ViewContract();
            CheckUI_BindingCard();
            Check16_FindRangeConfigurable();
        }

        // ==================================================================
        //  断言 [UI]：配置视图的样式契约（本批新增 BeadInspectView 的护栏；
        //  BlobDetectChecks 同款静态扫描——运行期"找不到资源"就是这条没守住）
        // ==================================================================
        private static void CheckUI_ViewContract()
        {
            string? xamlPath = ResolveRepoFile(@"Plugins\Plugin.BeadInspect\BeadInspectView.xaml");
            string? colorsPath = ResolveRepoFile(@"UI\Controls\Themes\PluginConfigColors.xaml");
            string? stylesPath = ResolveRepoFile(@"UI\Controls\Themes\PluginConfigStyles.xaml");
            string? genericPath = ResolveRepoFile(@"UI\Controls\Themes\Generic.xaml");
            if (xamlPath == null || colorsPath == null || stylesPath == null || genericPath == null)
            {
                Check("[UI] BeadInspectView 样式契约（静态扫描）", true, "跳过：定位不到视图或主题文件");
                return;
            }

            string xaml = File.ReadAllText(xamlPath);
            string defined = File.ReadAllText(colorsPath) + File.ReadAllText(stylesPath) + File.ReadAllText(genericPath);

            // ① 视图引用的每个 Plugin* 键都必须有定义
            var used = System.Text.RegularExpressions.Regex.Matches(xaml, @"(?:Static|Dynamic)Resource ([A-Za-z0-9]+)")
                .Select(m => m.Groups[1].Value)
                .Where(k => k.StartsWith("Plugin"))
                .Distinct()
                .ToArray();
            var missing = used.Where(k => !defined.Contains($"x:Key=\"{k}\"")).ToList();
            Check("[UI] BeadInspectView 引用的每个 Plugin* 键都有定义",
                missing.Count == 0, missing.Count == 0 ? $"共 {used.Length} 个键" : "缺失：" + string.Join(", ", missing));

            // ② 不给 h: 控件挂内联 Style（会顶掉 Core.Halcon 隐式样式 → 控件没模板、预览永远空白）
            var inlineStyles = System.Text.RegularExpressions.Regex.Matches(xaml, @"<h:[A-Za-z0-9_]+\.Style>");
            Check("[UI] BeadInspectView 不给 h: 控件挂内联 Style", inlineStyles.Count == 0,
                inlineStyles.Count == 0 ? "" : "发现：" + string.Join(", ", inlineStyles.Select(m => m.Value).Distinct()));

            // ③ 颜色一律走令牌
            var colors = System.Text.RegularExpressions.Regex.Matches(xaml, @"#[0-9A-Fa-f]{3}\b|#[0-9A-Fa-f]{6}\b|#[0-9A-Fa-f]{8}\b");
            Check("[UI] BeadInspectView 无硬编码颜色", colors.Count == 0,
                colors.Count == 0 ? "" : string.Join(", ", colors.Select(m => m.Value).Take(8)));
        }

        /// <summary>从输出目录往上找仓库根，再拼相对路径（定位不到返回 null，由调用方跳过）</summary>
        private static string? ResolveRepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        // ==================================================================
        //  UI 断言②：⓪输入绑定卡（LinkableValueEditor × 5）
        //  起因：第二批配置界面漏了绑定卡，上游产出的图没法从配置界面绑到 SrcImage
        //  （2026-10-05 修复）。本断言锁两件事：
        //    A. 静态结构：XAML 里 5 个编辑器分别绑在 5 个输入端口上（改名/删卡即红）；
        //    B. 运行时行为（STA 真实例化视图）：
        //       预置链接回显 → 解绑按钮 → 绑定事件（LinkPathEvent→OnBound→SetLink）
        //       → OnConfirm 持久化 → 真实演示方案（.vms）的已存绑定回显。
        //  LinkableValueEditor 机制（源码实证）：LinkBtn_Click 发 LinkPathEvent，
        //  OnBound 回调里 stepData.SetLink + 编辑器自刷 LinkAddress——测试里自己扮演宿主
        //  订阅该事件即可模拟"用户在绑定弹窗里选中上游输出"，全程无需人工。
        // ==================================================================
        private static void CheckUI_BindingCard()
        {
            string? xamlPath = ResolveRepoFile(@"Plugins\Plugin.BeadInspect\BeadInspectView.xaml");
            if (xamlPath == null)
            {
                Check("[UI·绑定] 静态结构（跳过：定位不到视图）", true, "");
                return;
            }
            string xaml = File.ReadAllText(xamlPath);

            // A1. cv 命名空间在位
            Check("[UI·绑定] XAML 声明 Core.Controls 命名空间",
                xaml.Contains("xmlns:cv=\"clr-namespace:Core.Controls"), "");

            // A2. 恰好 5 个 LinkableValueEditor（少了=端口绑不了；多了=重复卡）
            int editorCount = System.Text.RegularExpressions.Regex.Matches(xaml, "<cv:LinkableValueEditor").Count;
            Check("[UI·绑定] 输入绑定卡有 5 个 LinkableValueEditor", editorCount == 5, "实际 " + editorCount);

            // A3. 5 个 Port 绑定逐一对上输入端口
            var expectedPorts = new[] { "SrcImage", "RecipeName", "AlignedImageIn", "RoiRegion", "Transform" };
            var missing = expectedPorts.Where(p => !xaml.Contains($"Port=\"{{Binding {p}}}\"")).ToArray();
            Check("[UI·绑定] 5 个编辑器分别绑在 5 个输入端口属性上", missing.Length == 0,
                missing.Length == 0 ? string.Join(",", expectedPorts) : "缺失：" + string.Join(",", missing));

            // A4. 每个编辑器都喂了 StepData（否则 RestoreLinkState 无从回显）
            int stepDataCount = System.Text.RegularExpressions.Regex.Matches(xaml, "StepData=\"{Binding StepData}\"").Count;
            Check("[UI·绑定] 5 个编辑器都绑定 StepData", stepDataCount == 5, "实际 " + stepDataCount);

            // A5. 卡标题在位（删卡/改名即红）
            Check("[UI·绑定] 「⓪ 输入绑定」卡标题在位", xaml.Contains("⓪ 输入绑定"), "");

            // B+C. 运行时行为（STA 线程实例化真实视图）
            var results = new List<(string Name, bool Ok, string Detail)>();
            var sta = new Thread(() =>
            {
                try { RunBindingRuntimeProbe(results); }
                catch (Exception ex) { lock (results) results.Add(("STA 运行", false, ex.GetType().Name + ": " + ex.Message)); }
            });
            sta.SetApartmentState(ApartmentState.STA);
            sta.Start();
            sta.Join();

            foreach (var r in results)
                Check("[UI·绑定] " + r.Name, r.Ok, r.Detail);
        }

        /// <summary>STA 线程侧：真实例化 BeadInspectView，跑绑定/解绑/回显全链路</summary>
        private static void RunBindingRuntimeProbe(List<(string Name, bool Ok, string Detail)> results)
        {
            void R(string name, bool ok, string detail)
            {
                lock (results) results.Add((name, ok, detail));
            }

            // ── B1. 实例化：预置链接的 StepData（模拟"方案文件里已保存的绑定"）──
            var plugin = BuildRuntimePlugin("胶路_绑定UI");
            var stepData = new LinkableFakeStepData();
            stepData.SetLink("SrcImage", new LinkReference(LinkKind.StepPort, Guid.NewGuid(), "Image", "图像采集_0.Image"));

            var view = new BeadInspectView { DataContext = plugin };
            plugin.Initialize(stepData);

            // 无宿主 Window 时绑定表达式停在 Unattached——包一层隐形 Window 激活绑定管线
            var host = new Window
            {
                Content = view,
                Width = 1100,
                Height = 640,
                WindowStyle = System.Windows.WindowStyle.None,
                ShowActivated = false,
                Opacity = 0,
            };
            host.Show();
            host.Hide();

            view.Measure(new System.Windows.Size(1100, 640));

            var editors = CollectVisual<Core.Controls.LinkableValueEditor>(view).ToList();
            R("实例化：视图里有 5 个 LinkableValueEditor", editors.Count == 5, "实际 " + editors.Count);
            if (editors.Count != 5) return;

            var expected = new[] { "SrcImage", "RecipeName", "AlignedImage", "RoiRegion", "Transform" };
            var names = editors.Select(e => e.Port?.Name ?? "(空)").OrderBy(n => n).ToArray();
            R("编辑器端口面 = 5 个输入端口", names.SequenceEqual(expected.OrderBy(n => n).ToArray()),
                string.Join(",", names));

            var src = editors.First(e => e.Port?.Name == "SrcImage");
            R("预置链接回显：SrcImage 显示已链接", src.IsLinked, src.LinkAddress ?? "(空)");
            R("预置链接回显：地址 = 图像采集_0.Image", src.LinkAddress == "图像采集_0.Image", src.LinkAddress ?? "(空)");
            R("其余 4 个端口初始未链接", editors.Count(e => e.IsLinked) == 1, "已链接 " + editors.Count(e => e.IsLinked));

            // ── B2. 解绑（点模板里的 PART_UnlinkBtn）──
            ClickEditorPart(src, "PART_UnlinkBtn");
            R("解绑：StepData 链接已移除", !stepData.IsLinked("SrcImage"), stepData.GetLinkedAddress("SrcImage") ?? "(空)");
            R("解绑：编辑器恢复未链接显示", !src.IsLinked, src.LinkAddress ?? "(空)");

            // ── B3. 绑定（LinkBtn 发 LinkPathEvent → 本测试扮演宿主回调 OnBound → SetLink）──
            void OnLinkPath(LinkPathEvent e)
            {
                if (e.InputPortName == "SrcImage")
                    e.OnBound(new LinkReference(LinkKind.StepPort, Guid.NewGuid(), "Image", "图像采集_0.Image"));
            }
            GlobalEventBus.Subscribe<LinkPathEvent>(OnLinkPath);
            try
            {
                ClickEditorPart(src, "PART_LinkBtn");
                R("绑定：StepData 收到链接", stepData.IsLinked("SrcImage"), stepData.GetLinkedAddress("SrcImage") ?? "(空)");
                R("绑定：编辑器显示已链接", src.IsLinked, src.LinkAddress ?? "(空)");
            }
            finally { GlobalEventBus.Unsubscribe<LinkPathEvent>(OnLinkPath); }

            // ── B4. OnConfirm 持久化（链接端口不落手动值）──
            plugin.OnConfirm(stepData);
            R("OnConfirm：链接保留、不写手动值",
                stepData.GetLink("SrcImage") != null && !stepData.InputValues.ContainsKey("SrcImage"),
                "GetLink=" + (stepData.GetLink("SrcImage") != null) + " 手动值=" + stepData.InputValues.ContainsKey("SrcImage"));

            // ── C. 真实演示方案回显（.vms 里的 LinkedSources 是真实存储）──
            var vmsPath = ResolveRepoFile(@"解决方案\胶路检测演示.vms");
            if (vmsPath == null) { R("演示方案回显：跳过（定位不到 .vms）", true, ""); return; }
            var loaded = new SolutionService().LoadAsync(vmsPath).GetAwaiter().GetResult();
            var step = loaded?.Data?.Flows.SelectMany(f => f.Steps)
                .FirstOrDefault(s => s.PluginTypeName.Contains("BeadInspect"));
            if (step == null) { R("演示方案回显：跳过（方案里无胶路步骤）", true, ""); return; }

            plugin.Initialize(step); // StepModel 本身就是 IStepConfigData（真实 LinkedSources 存储）
            R("演示方案回显：SrcImage 编辑器显示已链接", src.IsLinked, src.LinkAddress ?? "(空)");
            R("演示方案回显：地址 = 图像采集_0.Image", src.LinkAddress == "图像采集_0.Image", src.LinkAddress ?? "(空)");
        }

        /// <summary>收集可视树里全部指定类型元素（深度优先）</summary>
        private static IEnumerable<T> CollectVisual<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed) yield return typed;
                foreach (var sub in CollectVisual<T>(child)) yield return sub;
            }
        }

        /// <summary>在可视树里按 x:Name 找模板部件</summary>
        private static DependencyObject? FindVisualByName(DependencyObject root, string name)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is System.Windows.FrameworkElement fe && fe.Name == name) return child;
                var sub = FindVisualByName(child, name);
                if (sub != null) return sub;
            }
            return null;
        }

        /// <summary>触发编辑器模板里链接/解绑按钮的 Click（private 处理器的无头入口）</summary>
        private static void ClickEditorPart(Core.Controls.LinkableValueEditor editor, string partName)
        {
            editor.ApplyTemplate();
            if (FindVisualByName(editor, partName) is System.Windows.Controls.Primitives.ButtonBase btn)
                btn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        }

        /// <summary>会真实存链接的 StepData 桩（Matching 的 FakeStepData 链接方法是空操作，不能用于绑定断言）</summary>
        private sealed class LinkableFakeStepData : IStepConfigData
        {
            private readonly Dictionary<string, LinkReference> _links = new();
            public Guid StepId { get; } = Guid.NewGuid();
            public string Icon { get; } = "";
            public string StepName { get; } = "BeadLinkFake";
            public string Description { get; } = "";
            public Dictionary<string, object> InputValues { get; } = new();
            public void SetInputValue(string key, object value) => InputValues[key] = value;
            public void RemoveInputValue(string key) => InputValues.Remove(key);
            public bool IsLinked(string inputPortName) => _links.ContainsKey(inputPortName);
            public string GetLinkedAddress(string inputPortName) =>
                _links.TryGetValue(inputPortName, out var l) ? l.DisplayAddress : null;
            public LinkReference GetLink(string inputPortName) =>
                _links.TryGetValue(inputPortName, out var l) ? l : null;
            public void SetLink(string inputPortName, LinkReference link) => _links[inputPortName] = link;
            public void RemoveLink(string inputPortName) => _links.Remove(inputPortName);
            public List<DynamicPortInfo> OutputPortDefinitions { get; set; } = new();
        }

        // ==================================================================
        //  断言 10：自动提取中心线（§6.4 链路：差分 → 树直径 → 抽稀；P6/P12 实测 40 点）
        // ==================================================================
        private static void Check10_AutoExtract(BeadFixture fixture)
        {
            bool ok = BeadInspectHalcon.ExtractCenterline(
                fixture.Aligned01!, fixture.RefAligned!, fixture.Plane!, 40,
                out double[] rows, out double[] cols, out _, out _, out _, out string err);
            Check("10a 自动提取（参考图差分口径）出非空点列", ok && rows.Length > 0,
                ok ? $"点数={rows.Length} 首点=({rows[0]:0.#},{cols[0]:0.#})" : err);

            Check("10b 提取点数在 40~80 区间（规格 §7.2 #10，与 13d 同口径；P12：40 点抽稀对检出几乎无影响）",
                ok && rows.Length >= 40 && rows.Length <= 80, $"点数={rows.Length}");

            // black-hat 回退（界面在未填无胶参考图时走这条）：成功给非空点列、失败给中文原因，
            // 两条路都不允许抛未捕获异常——界面据此红字提示，绝不写空点列
            bool fb = BeadInspectHalcon.ExtractCenterline(
                fixture.Aligned01!, null, fixture.Plane!, 40,
                out double[] fr, out double[] fc, out _, out _, out _, out string ferr);
            Check("10c black-hat 回退不崩（成功给点列 / 失败给中文原因）",
                (fb && fr.Length >= 2) || (!fb && ferr.Length > 0),
                fb ? $"点数={fr.Length}" : ferr);
        }

        // ==================================================================
        //  断言 11：mm 换算失配三态（§5.4：失配必须明确失败，绝不静默按像素输出）
        // ==================================================================
        private static void Check11_MmMismatch()
        {
            // ① UnitOutput=Mm 且既无 Transform 也无 PixelSizeMm → Fail（中文话术）
            var p1 = BuildRuntimePlugin("胶路_mm无来源");
            try
            {
                using var img = LoadImage(6); // 06：缺胶 1 段，能走到换算层
                p1.MinErrorLength = 0;
                p1.UnitOutput = BeadUnit.Mm; // PixelSizeMm 保持 0，Transform 未接
                p1.SrcImage.Value = img;
                p1.Execute(MakeContext(new StubLog()));
                Check("11a UnitOutput=Mm 无任何来源 → Fail（中文话术）",
                    p1.Success.Value is false && p1.LastError.Contains("像素当量"),
                    $"Success={p1.Success.Value} Err='{p1.LastError}'");
            }
            finally
            {
                p1.Dispose();
            }

            // ② 接了 Transform 但标定时的图像尺寸与当前图不符 → Fail（换分辨率/换相机必须重标）
            var p2 = BuildRuntimePlugin("胶路_标定失配");
            try
            {
                using var img = LoadImage(6); // 1280×1024
                p2.MinErrorLength = 0;
                p2.UnitOutput = BeadUnit.Mm;
                p2.Transform.Value = new CalibrationTransform
                {
                    Kind = CalibrationKind.PixelScale,
                    MmPerPixel = 0.02,
                    SourceImageWidth = 640,   // ≠ 1280
                    SourceImageHeight = 512,  // ≠ 1024
                };
                p2.SrcImage.Value = img;
                p2.Execute(MakeContext(new StubLog()));
                Check("11b 标定图像尺寸与当前图不符 → Fail（中文话术）",
                    p2.Success.Value is false && p2.LastError.Contains("标定失配"),
                    $"Success={p2.Success.Value} Err='{p2.LastError}'");
            }
            finally
            {
                p2.Dispose();
            }

            // ③ PixelScale 标定只取 MmPerPixel 做长度换算：矩阵置全零（PixelScale 的矩阵无机械
            //    坐标含义，消费方不得使用）——换算结果仍必须 == 像素值 × 当量
            var p3 = BuildRuntimePlugin("胶路_PixelScale");
            try
            {
                using var img = LoadImage(6);
                p3.MinErrorLength = 0;
                p3.UnitOutput = BeadUnit.Pixel;
                p3.SrcImage.Value = img;
                p3.Execute(MakeContext(new StubLog()));
                double px = Convert.ToDouble(p3.TotalErrorLength.Value);

                p3.UnitOutput = BeadUnit.Mm;
                p3.Transform.Value = new CalibrationTransform
                {
                    Kind = CalibrationKind.PixelScale,
                    MmPerPixel = 0.02,
                    SourceImageWidth = 1280, // 与当前图一致：②的失配校验放行
                    SourceImageHeight = 1024,
                    Matrix = new double[6],  // 全零矩阵：若实现误用矩阵，结果会是 0
                };
                p3.Execute(MakeContext(new StubLog()));
                double mm = Convert.ToDouble(p3.TotalErrorLength.Value);
                Check("11c PixelScale 标定只取 MmPerPixel（矩阵全零不影响长度换算）",
                    p3.Success.Value is true && Math.Abs(mm - px * 0.02) < 1e-9,
                    $"px={px:0.####} mm={mm:0.########} Success={p3.Success.Value} Err='{p3.LastError}'");
            }
            finally
            {
                p3.Dispose();
            }
        }

        // ==================================================================
        //  断言 12：点列增删保序（§6.2 决策 D2：插到最近线段之间；删点后仍可建模型）。
        //  直接测 BeadPathEditor——配置界面画布拾取用的就是这一份实现（断言=界面行为）。
        // ==================================================================
        private static void Check12_InsertDeleteOrder()
        {
            // 12b 直线中段插点：点数 +1、插入段正确、走向不变（全部线段同向，不折返）
            double[] lineRows = { 100, 100, 100, 100, 100 };
            double[] lineCols = { 100, 200, 300, 400, 500 };
            bool insLine = BeadPathEditor.InsertNearest(
                lineRows, lineCols, 100, 252, out var lr, out var lc, out int lineIdx, out _);
            bool lineStraight = true;
            for (int i = 2; i < lc.Length; i++)
            {
                if (lc[i] - lc[i - 1] <= 0)
                    lineStraight = false;
            }
            Check("12b 直线中段插点：点数+1、插到最近段、走向不变（不折返）",
                insLine && lr.Length == 6 && lineIdx == 2 && lineStraight,
                $"插入位={lineIdx} 点数={lr.Length} 走向不变={lineStraight}");

            // 12c 范例 14 点路径中段插点（第 0~1 点线段中点）：保序 + 无折返 + 总长几乎不变
            double clickR = (RefRows[0] + RefRows[1]) / 2;
            double clickC = (RefCols[0] + RefCols[1]) / 2;
            bool insRef = BeadPathEditor.InsertNearest(
                RefRows, RefCols, clickR, clickC, out var mr, out var mc, out int refIdx, out _);
            bool orderKept = insRef && mr.Length == RefRows.Length + 1 && refIdx == 1;
            if (orderKept)
            {
                for (int i = 0; i < RefRows.Length && orderKept; i++)
                {
                    int src = i < refIdx ? i : i - 1;
                    if (i == refIdx)
                        continue;
                    if (Math.Abs(mr[i] - RefRows[src]) > 1e-9 || Math.Abs(mc[i] - RefCols[src]) > 1e-9)
                        orderKept = false;
                }
            }
            double turnBefore = MaxTurnDeg(RefRows, RefCols);
            double turnAfter = MaxTurnDeg(mr, mc);
            // 折返 = 出现 ~180° 反向：插入共线点不允许增大任何转角，插入点处局部转角必须 < 90°
            double localTurn = LocalTurnDeg(mr, mc, refIdx);
            double lenBefore = PathLength(RefRows, RefCols);
            double lenAfter = PathLength(mr, mc);
            Check("12c 范例路径中段插点：原点列保序、最大转角不变（走向不变）、局部转角<90°、总长偏差<0.1%",
                orderKept && turnAfter <= turnBefore + 1e-9 && localTurn < 90
                && Math.Abs(lenAfter - lenBefore) < lenBefore * 0.001,
                $"插入位={refIdx} 保序={orderKept} 转角 {turnBefore:0.#}°→{turnAfter:0.#}° 局部={localTurn:0.#}° "
                + $"长度 {lenBefore:0.##}→{lenAfter:0.##}");

            // 12d 删点后仍可建模型：删 1 点 → 13 点路径仍能建折线 XLD + bead 模型
            bool del = BeadPathEditor.DeleteNearest(
                RefRows, RefCols, RefRows[7], RefCols[7], BeadPathEditor.DefaultDeleteTolerance,
                out var dr, out var dc, out int delIdx, out _);
            bool rebuilt = false;
            string rebuildErr = string.Empty;
            if (del && dr.Length == RefRows.Length - 1 && delIdx == 7)
            {
                try
                {
                    if (BeadInspectHalcon.TryBuildContour(dr, dc, out HObject? contour, out rebuildErr))
                    {
                        var model = BeadInspectHalcon.CreateBeadModel(contour!, 15, 8, 30, "dark");
                        HOperatorSet.ClearBeadInspectionModel(model);
                        contour!.Dispose();
                        rebuilt = true;
                    }
                }
                catch (Exception ex)
                {
                    rebuildErr = ex.Message;
                }
            }
            Check("12d 删点后仍可建模型（13 点 → 折线 XLD + bead 模型构建成功）",
                del && rebuilt, $"删除位={delIdx} 剩={dr.Length} {rebuildErr}");
        }

        /// <summary>折线总长</summary>
        private static double PathLength(double[] r, double[] c)
        {
            double total = 0;
            for (int i = 1; i < r.Length; i++)
                total += BeadPathEditor.Dist(r[i - 1], c[i - 1], r[i], c[i]);
            return total;
        }

        /// <summary>相邻线段间最大转角（度）——折返检测的标尺</summary>
        private static double MaxTurnDeg(double[] r, double[] c)
        {
            double max = 0;
            for (int i = 1; i + 1 < r.Length; i++)
            {
                double t = LocalTurnDeg(r, c, i);
                if (t > max)
                    max = t;
            }
            return max;
        }

        /// <summary>第 i 个点处前后两段的夹角（度）；零长段按 0 处理</summary>
        private static double LocalTurnDeg(double[] r, double[] c, int i)
        {
            if (i <= 0 || i + 1 >= r.Length)
                return 0;
            double a1r = r[i] - r[i - 1], a1c = c[i] - c[i - 1];
            double a2r = r[i + 1] - r[i], a2c = c[i + 1] - c[i];
            double l1 = Math.Sqrt(a1r * a1r + a1c * a1c);
            double l2 = Math.Sqrt(a2r * a2r + a2c * a2c);
            if (l1 < 1e-9 || l2 < 1e-9)
                return 0;
            double cos = Math.Clamp((a1r * a2r + a1c * a2c) / (l1 * l2), -1.0, 1.0);
            return Math.Acos(cos) * 180.0 / Math.PI;
        }

        // ==================================================================
        //  断言 1：参考路径生成（点列 → 非空 XLD；点数不足 / 退化给中文错误）
        // ==================================================================
        private static void Check1_PathGeneration()
        {
            bool ok = BeadInspectHalcon.TryBuildContour(RefRows, RefCols, out HObject? contour, out string err14);
            HOperatorSet.CountObj(contour, out HTuple n);
            HOperatorSet.LengthXld(contour, out HTuple len);
            Check("1a 14 控制点生成非空折线 XLD（P1 折线口径）",
                ok && contour != null && n.I == 1 && len.D > 1000,
                $"对象数={n.I} 长度={len.D:0.##}px（探针 P1-b 实测 1015.68px）");
            contour?.Dispose();

            bool tooFew = !BeadInspectHalcon.TryBuildContour(new[] { 100.0 }, new[] { 100.0 }, out _, out string errFew);
            Check("1b 点数 < 2 → 中文错误（含「至少需要 2 个点」）", tooFew && errFew.Contains("2 个点"), errFew);

            bool degenerate = !BeadInspectHalcon.TryBuildContour(
                new[] { 100.0, 100.0, 100.0 }, new[] { 100.0, 100.0, 100.0 }, out _, out string errDeg);
            Check("1c 点列近似重合 → 中文错误（含「退化」）", degenerate && errDeg.Contains("退化"), errDeg);
        }

        // ==================================================================
        //  断言 2：bead 模型构建（create_bead_inspection_model 成功、参数回读一致）
        // ==================================================================
        private static void Check2_BeadModel()
        {
            BeadInspectHalcon.TryBuildContour(RefRows, RefCols, out HObject? contour, out _);
            try
            {
                var model = BeadInspectHalcon.CreateBeadModel(contour!, 15, 8, 30, "dark");
                HOperatorSet.GetBeadInspectionParam(model, "target_thickness", out HTuple tt);
                HOperatorSet.GetBeadInspectionParam(model, "thickness_tolerance", out HTuple tol);
                HOperatorSet.GetBeadInspectionParam(model, "position_tolerance", out HTuple pt);
                Check("2a bead 模型构建成功且句柄非空", model != null && model.Length > 0, $"句柄={model}");
                Check("2b 模型参数回读一致（target=15 / tol=8 / pos=30）",
                    Math.Abs(tt.D - 15) < 1e-6 && Math.Abs(tol.D - 8) < 1e-6 && Math.Abs(pt.D - 30) < 1e-6,
                    $"target={tt.D} tol={tol.D} pos={pt.D}");
                HOperatorSet.ClearBeadInspectionModel(model);
            }
            catch (Exception ex)
            {
                Check("2a bead 模型构建成功且句柄非空", false, ex.Message);
                Check("2b 模型参数回读一致（target=15 / tol=8 / pos=30）", false, ex.Message);
            }
            finally
            {
                contour?.Dispose();
            }
        }

        // ==================================================================
        //  断言 3/4/5/6：7 图真值表 + 良品零误报 + NG 语义 + 四类计数一致
        //  （端到端：配方库 JSON 往返 → ApplyConfigValues → 真 RunAlgorithm）
        // ==================================================================
        private static void Check3456_TruthTableAndSemantics()
        {
            var plugin = BuildRuntimePlugin("胶路_真值表");
            try
            {
                // P4 真值表是【未过滤】的算子原始计数（探针 StageD 没有 MinErrorLength 这一层），
                // 且实测各段长度只有 4~47px（03: too thin=4px；05: too thick=4px；06: no bead=4px）——
                // 插件出厂默认 MinErrorLength=5 会把 4px 的真值段滤掉。故真值表断言显式关掉过滤，
                // 生产行为（默认 5，滤碎小误报）不受影响，归 §4.1。
                plugin.MinErrorLength = 0;
                for (int i = 1; i <= 7; i++)
                {
                    HImage img = LoadImage(i);
                    plugin.SrcImage.Value = img;
                    plugin.Execute(MakeContext(new StubLog()));

                    var expect = Truth[i - 1];
                    int got = Convert.ToInt32(plugin.ErrorCount.Value);
                    int nb = Convert.ToInt32(plugin.NoBeadCount.Value);
                    int tn = Convert.ToInt32(plugin.TooThinCount.Value);
                    int tk = Convert.ToInt32(plugin.TooThickCount.Value);
                    int mp = Convert.ToInt32(plugin.MispositionCount.Value);

                    Check($"3-{i:00} 错误段数与 P4 真值一致（期望 {expect.total}）",
                        plugin.Success.Value is true && got == expect.total,
                        $"got={got} Success={plugin.Success.Value} Err='{plugin.LastError}'");

                    Check($"3-{i:00} 四类计数与真值一致（缺胶{expect.noBead}/太细{expect.thin}/太粗{expect.thick}/偏移{expect.mispos}）",
                        nb == expect.noBead && tn == expect.thin && tk == expect.thick && mp == expect.mispos,
                        $"got=缺胶{nb}/太细{tn}/太粗{tk}/偏移{mp}");

                    Check($"6-{i:00} 四类计数之和 == ErrorCount",
                        nb + tn + tk + mp == got,
                        $"{nb}+{tn}+{tk}+{mp} vs {got}");

                    bool isOk = plugin.IsOk.Value is true;
                    string ng = plugin.NgReason.Value as string ?? "";
                    if (expect.total == 0)
                    {
                        Check($"4-{i:00} 良品零误报（IsOk=true、NgReason 空、AnnotatedImage 就绪）",
                            isOk && ng.Length == 0
                            && plugin.AnnotatedImage.Value is HImage anno && anno.IsInitialized(),
                            $"IsOk={isOk} Ng='{ng}'");
                    }
                    else
                    {
                        Check($"5-{i:00} NG 语义（Success 仍为 true、NgReason 非空）",
                            plugin.Success.Value is true && !isOk && ng.Length > 0,
                            $"Success={plugin.Success.Value} IsOk={isOk} Ng='{ng}'");
                    }

                    if (i == 7)
                    {
                        // 7 号图跑完顺手看类型明细（P4：偏移3 + 缺胶1），再验数组端口逐项对齐
                        var types = plugin.ErrorTypes.Value is HTuple t ? t.ToSArr() : Array.Empty<string>();
                        var lens = plugin.ErrorLengths.Value is HTuple l ? l.ToDArr() : Array.Empty<double>();
                        var rows = plugin.ErrorRows.Value is HTuple r ? r.ToDArr() : Array.Empty<double>();
                        var cols = plugin.ErrorCols.Value is HTuple c ? c.ToDArr() : Array.Empty<double>();
                        Check("3-07 类型明细 = 偏移3 + 缺胶1（顺序与数组端口逐项对齐）",
                            types.Length == 4
                            && types.Count(v => v == "incorrect position") == 3
                            && types.Count(v => v == "no bead") == 1
                            && lens.Length == 4 && rows.Length == 4 && cols.Length == 4,
                            string.Join(",", types));
                        Check("3-07 ErrorSegments / BeadContours 输出就绪",
                            plugin.ErrorSegments.Value is HXLD es && es.IsInitialized()
                            && plugin.BeadContours.Value is HXLD bc && bc.IsInitialized(), "");
                    }

                    img.Dispose();
                }
            }
            finally
            {
                plugin.Dispose();
            }
        }

        // ==================================================================
        //  断言 7：单位换算（UnitOutput=Mm 且给定当量 → TotalErrorLength == 像素值 × 当量）
        // ==================================================================
        private static void Check7_UnitConversion()
        {
            var plugin = BuildRuntimePlugin("胶路_单位换算");
            try
            {
                plugin.MinErrorLength = 0; // 06 的缺胶段实测只有 4px，关掉碎段过滤才能拿到非零总长
                using var img = LoadImage(6); // 06：缺胶 1 段，总长 > 0
                plugin.SrcImage.Value = img;
                plugin.Execute(MakeContext(new StubLog()));
                double px = Convert.ToDouble(plugin.TotalErrorLength.Value);
                int count1 = Convert.ToInt32(plugin.ErrorCount.Value);

                plugin.UnitOutput = BeadUnit.Mm;
                plugin.PixelSizeMm = 0.02;
                plugin.Execute(MakeContext(new StubLog()));
                double mm = Convert.ToDouble(plugin.TotalErrorLength.Value);
                int count2 = Convert.ToInt32(plugin.ErrorCount.Value);

                Check("7 mm 输出 == 像素值 × 当量（容差 1e-9）",
                    count1 == 1 && count2 == 1 && Math.Abs(mm - px * 0.02) < 1e-9,
                    $"px={px:0.####} mm={mm:0.########} 预期={px * 0.02:0.########}");
            }
            finally
            {
                plugin.Dispose();
            }
        }

        // ==================================================================
        //  断言 8：端口类型面（§3 契约：端口名/类型锁定，改名即断言失败）
        // ==================================================================
        private static void Check8_PortSurface()
        {
            var plugin = new BeadInspectPlugin { InstanceName = "胶路_端口面" };
            try
            {
                Type t = typeof(BeadInspectPlugin);
                bool inputs =
                    t.GetProperty("SrcImage")!.PropertyType == typeof(InputPort<HImage>)
                    && t.GetProperty("RecipeName")!.PropertyType == typeof(InputPort<string>)
                    && t.GetProperty("AlignedImageIn")!.PropertyType == typeof(InputPort<HImage>)
                    && t.GetProperty("RoiRegion")!.PropertyType == typeof(InputPort<HRegion>)
                    && t.GetProperty("Transform")!.PropertyType == typeof(InputPort<CalibrationTransform?>)
                    && plugin.Inputs.ContainsKey("SrcImage") && plugin.Inputs.ContainsKey("RecipeName")
                    && plugin.Inputs.ContainsKey("AlignedImage") && plugin.Inputs.ContainsKey("RoiRegion")
                    && plugin.Inputs.ContainsKey("Transform");
                Check("8a 输入端口面：SrcImage/RecipeName/AlignedImage/RoiRegion/Transform（名字+类型锁定）",
                    inputs, $"Inputs=[{string.Join(",", plugin.Inputs.Keys)}]");

                bool outputs =
                    t.GetProperty("IsOk")!.PropertyType == typeof(OutputPort<bool>)
                    && t.GetProperty("NgReason")!.PropertyType == typeof(OutputPort<string>)
                    && t.GetProperty("AnnotatedImage")!.PropertyType == typeof(OutputPort<HImage>)
                    && t.GetProperty("ErrorCount")!.PropertyType == typeof(OutputPort<int>)
                    && t.GetProperty("ErrorTypes")!.PropertyType == typeof(OutputPort<HTuple>)
                    && t.GetProperty("ErrorLengths")!.PropertyType == typeof(OutputPort<HTuple>)
                    && t.GetProperty("ErrorRows")!.PropertyType == typeof(OutputPort<HTuple>)
                    && t.GetProperty("ErrorCols")!.PropertyType == typeof(OutputPort<HTuple>)
                    && t.GetProperty("TotalErrorLength")!.PropertyType == typeof(OutputPort<double>)
                    && t.GetProperty("NoBeadCount")!.PropertyType == typeof(OutputPort<int>)
                    && t.GetProperty("TooThinCount")!.PropertyType == typeof(OutputPort<int>)
                    && t.GetProperty("TooThickCount")!.PropertyType == typeof(OutputPort<int>)
                    && t.GetProperty("MispositionCount")!.PropertyType == typeof(OutputPort<int>)
                    && t.GetProperty("ErrorSegments")!.PropertyType == typeof(OutputPort<HXLD>)
                    && t.GetProperty("BeadContours")!.PropertyType == typeof(OutputPort<HXLD>)
                    && t.GetProperty("AlignedImage")!.PropertyType == typeof(OutputPort<HImage>)
                    && plugin.Outputs.ContainsKey("AlignedImage");
                Check("8b 输出端口面：16 个端口全部就位（名字+类型锁定）",
                    outputs, $"Outputs=[{string.Join(",", plugin.Outputs.Keys)}]");
            }
            finally
            {
                plugin.Dispose();
            }
        }

        // ==================================================================
        //  断言 9：模型缓存（同参数连续两轮不重建；参数变化才重建）
        // ==================================================================
        private static void Check9_ModelCache()
        {
            var plugin = BuildRuntimePlugin("胶路_缓存");
            try
            {
                using var img = LoadImage(1);
                plugin.SrcImage.Value = img;
                plugin.Execute(MakeContext(new StubLog()));
                int bead1 = plugin.BeadModelRebuildCount;
                int planar1 = plugin.PlanarModelRebuildCount;
                Check("9a 首轮：bead 与 planar 各重建 1 次",
                    bead1 == 1 && planar1 == 1, $"bead={bead1} planar={planar1}");

                plugin.Execute(MakeContext(new StubLog()));
                Check("9b 第二轮（同参数）：不重建（指纹缓存生效）",
                    plugin.BeadModelRebuildCount == bead1 && plugin.PlanarModelRebuildCount == planar1,
                    $"bead={plugin.BeadModelRebuildCount} planar={plugin.PlanarModelRebuildCount}");

                plugin.Library[0].WidthTolerance = 9; // 改容差 → bead 指纹失效
                plugin.Execute(MakeContext(new StubLog()));
                Check("9c 改容差后：bead 重建 1 次、planar 仍复用",
                    plugin.BeadModelRebuildCount == bead1 + 1 && plugin.PlanarModelRebuildCount == planar1,
                    $"bead={plugin.BeadModelRebuildCount} planar={plugin.PlanarModelRebuildCount}");
            }
            finally
            {
                plugin.Dispose();
            }
        }

        // ==================================================================
        //  断言 13：树直径合并（P6 回归：骨架分叉场景，合并覆盖 ≥95%，只取最长 <70%）
        // ==================================================================
        private static void Check13_TreeDiameter(BeadFixture fixture)
        {
            bool ok = BeadInspectHalcon.ExtractCenterline(
                fixture.Aligned01!, fixture.RefAligned!, fixture.Plane!, 40,
                out double[] rows, out double[] cols,
                out int segCount, out double merged, out double longest, out string err);

            Check("13a 提取成功且骨架段数 > 1（分叉场景成立）", ok && segCount > 1,
                ok ? $"段数={segCount}" : err);

            Check("13b 树直径合并覆盖率 ≥ 95%（P6 实测 98.2%）",
                ok && merged >= 0.95, $"{merged:P2}");

            Check("13c 只取最长一条的覆盖率 < 70%（P6 实测 68.9%，证明必须合并）",
                ok && longest < 0.70, $"{longest:P2}");

            Check("13d 抽稀点数在 40~80 区间（§6.4 ⑧）",
                ok && rows.Length >= 40 && rows.Length <= 80, $"点数={rows.Length}");
        }

        // ==================================================================
        //  断言 15：提取路径贴合度（P11 回归：平均偏离 < 3px，实测 1.98/2.18px）
        // ==================================================================
        private static void Check15_PathDeviation(BeadFixture fixture)
        {
            bool ok = BeadInspectHalcon.ExtractCenterline(
                fixture.Aligned01!, fixture.RefAligned!, fixture.Plane!, 40,
                out double[] rows, out double[] cols, out _, out _, out _, out string err);
            if (!ok)
            {
                Check("15 提取中心线到范例 ContourRef 平均偏离 < 3px", false, err);
                return;
            }

            // 范例 ContourRef：NURBS（探针 StageK 对照口径：knots=auto、权重 15、阶 3）
            var weights = new HTuple();
            for (int i = 0; i < RefRows.Length; i++)
                weights = weights.TupleConcat(15.0);
            HOperatorSet.GenContourNurbsXld(
                out HObject contourRef, new HTuple(RefRows), new HTuple(RefCols), "auto", weights, 3, 1, 5);
            try
            {
                HOperatorSet.GetContourXld(contourRef, out HTuple nr, out HTuple nc);
                var refPts = BeadInspectHalcon.ToPoints(nr.ToDArr(), nc.ToDArr());
                var extPts = BeadInspectHalcon.ToPoints(rows, cols);
                double avg = BeadInspectHalcon.AverageNearest(extPts, refPts);
                double hausdorff = BeadInspectHalcon.DirectedHausdorff(extPts, refPts);
                Check("15 提取中心线到范例 ContourRef 平均偏离 < 3px（P11 实测 1.98 全点 / 2.18 @40 点）",
                    avg < 3.0, $"平均={avg:0.##}px 最大偏离={hausdorff:0.##}px");
            }
            finally
            {
                contourRef.Dispose();
            }
        }

        // ==================================================================
        //  断言 14：TargetWidth 下限保护（P10 回归：传 1/2/4 被夹到 6，不抛 #3716）
        // ==================================================================
        private static void Check14_TargetWidthClamp()
        {
            var plugin = new BeadInspectPlugin { InstanceName = "胶路_下限" };
            try
            {
                plugin.TargetWidth = 4;
                double after4 = plugin.TargetWidth;
                plugin.TargetWidth = 2;
                double after2 = plugin.TargetWidth;
                plugin.TargetWidth = 1;
                double after1 = plugin.TargetWidth;
                plugin.TargetWidth = 15;
                double after15 = plugin.TargetWidth;
                Check("14a TargetWidth 传 4/2/1 都被钩子夹到 6，传 15 原样使用",
                    after4 == 6 && after2 == 6 && after1 == 6 && after15 == 15,
                    $"4→{after4} 2→{after2} 1→{after1} 15→{after15}");

                // 用夹到下限的 6 实际建一次模型：绝不抛 HALCON #3716
                bool built6 = false, threwBelow = false, threw3716 = false;
                string belowMsg = "";
                try
                {
                    BeadInspectHalcon.TryBuildContour(
                        new[] { 100.0, 200.0, 300.0 }, new[] { 100.0, 200.0, 300.0 }, out HObject? c6, out _);
                    var m6 = BeadInspectHalcon.CreateBeadModel(c6!, plugin.TargetWidth, 3, 30, "dark");
                    HOperatorSet.ClearBeadInspectionModel(m6);
                    c6!.Dispose();
                    built6 = true;
                }
                catch (Exception ex)
                {
                    belowMsg = ex.Message;
                }

                // 对照：下限保护确实必要——直接传 4 会让 HALCON 报 #3716（P10 实测）
                try
                {
                    BeadInspectHalcon.TryBuildContour(
                        new[] { 100.0, 200.0, 300.0 }, new[] { 100.0, 200.0, 300.0 }, out HObject? c4, out _);
                    var m4 = BeadInspectHalcon.CreateBeadModel(c4!, 4, 3, 30, "dark");
                    HOperatorSet.ClearBeadInspectionModel(m4);
                    c4!.Dispose();
                }
                catch (Exception ex)
                {
                    threwBelow = true;
                    threw3716 = ex.Message.Contains("3716");
                }

                Check("14b 用夹到 6 的胶宽实际建模型成功（不抛 #3716）", built6, belowMsg);
                Check("14c 对照：绕过保护直接传 4 确实报 #3716（证明夹取的必要性）",
                    threwBelow && threw3716, threw3716 ? "HALCON error #3716" : "未按预期失败");
            }
            finally
            {
                plugin.Dispose();
            }
        }

        // ==================================================================
        //  夹具
        // ==================================================================

        /// <summary>对齐夹具：与探针 StageB 同口径的 planar 模型 + 双向对齐产物（断言 13/15 共用）</summary>
        private sealed class BeadFixture : IDisposable
        {
            public HTuple PlanarModel = new();
            public double RowT, ColT;
            public HObject? RectifiedRef, Plane, RefAligned, Aligned01;

            public void Dispose()
            {
                try { if (PlanarModel != null && PlanarModel.Length > 0) HOperatorSet.ClearDeformableModel(PlanarModel); } catch { }
                foreach (var o in new[] { RectifiedRef, Plane, RefAligned, Aligned01 })
                {
                    try { o?.Dispose(); } catch { }
                }
            }
        }

        private static BeadFixture? BuildFixture(out string error)
        {
            error = string.Empty;
            try
            {
                HOperatorSet.ReadImage(out HObject refImg, Path.Combine(BeadDir, "adhesive_bead_ref.png"));
                if (!BeadInspectHalcon.PrepareAlignment(
                        refImg, QuadRows, QuadCols, DstRows, DstCols,
                        out var model, out var rowT, out var colT, out var rectRef, out var plane, out error))
                {
                    refImg.Dispose();
                    return null;
                }
                refImg.Dispose(); // PrepareAlignment 内部已完成所需投影，源参考图不再需要

                var fx = new BeadFixture
                {
                    PlanarModel = model,
                    RowT = rowT,
                    ColT = colT,
                    RectifiedRef = rectRef,
                    Plane = plane,
                };

                // 探针 B6 口径：把矫正后的参考图喂给 find，对齐进同一坐标系（自匹配 score≈1），
                // 得到"无胶参考图的对齐版"——自动提取差分的基准
                if (!BeadInspectHalcon.AlignImage(
                        rectRef!, model, rowT, colT, 0.4, 5,
                        -0.39, 0.78, 1, 1, 1, 1,
                        out var refAligned, out _, out error))
                {
                    fx.Dispose();
                    return null;
                }
                fx.RefAligned = refAligned;

                HOperatorSet.ReadImage(out HObject img01, Path.Combine(BeadDir, "adhesive_bead_01.png"));
                if (!BeadInspectHalcon.AlignImage(
                        img01, model, rowT, colT, 0.4, 5,
                        -0.39, 0.78, 1, 1, 1, 1,
                        out var aligned01, out _, out error))
                {
                    img01.Dispose();
                    fx.Dispose();
                    return null;
                }
                img01.Dispose();
                fx.Aligned01 = aligned01;
                return fx;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>真值表配方：范例控制点 + 探针矫正四点（显式 src/dst）+ 15/8/30/dark（P1/P3/P12/P4）</summary>
        private static BeadRecipeEntry BuildTruthRecipe() => new()
        {
            Name = "范例",
            RefImagePath = Path.Combine(BeadDir, "adhesive_bead_ref.png"),
            RefNoBeadImagePath = Path.Combine(BeadDir, "adhesive_bead_ref.png"), // 无胶参考图 = ref 本身
            RefPointsJson = PointsJson(RefRows, RefCols),
            RectifyQuadJson = QuadJson(QuadRows, QuadCols, DstRows, DstCols),
            TargetWidth = 15,   // P3 实测 15.26
            WidthTolerance = 8, // P12：范例的 7 照搬会过报
            PositionTolerance = 30,
            Polarity = "dark",
        };

        /// <summary>建库 → OnConfirm 落盘 → 新实例 ApplyConfigValues 加载（编译链路口径）</summary>
        private static BeadInspectPlugin BuildRuntimePlugin(string instanceName)
        {
            var creator = new BeadInspectPlugin { InstanceName = "胶路_建库" };
            creator.Library.Add(BuildTruthRecipe());
            var stepData = new FakeStepData();
            creator.OnConfirm(stepData);
            creator.Dispose();

            var runtime = new BeadInspectPlugin { InstanceName = instanceName };
            runtime.ApplyConfigValues(stepData); // FlowCompiler 对运行实例只调这个，不调 Initialize
            return runtime;
        }

        private static HImage LoadImage(int index)
        {
            HOperatorSet.ReadImage(out HObject raw, Path.Combine(BeadDir, $"adhesive_bead_{index:00}.png"));
            var img = new HImage(raw);
            raw.Dispose();
            return img;
        }

        private static string PointsJson(double[] rows, double[] cols)
        {
            var pts = new double[rows.Length][];
            for (int i = 0; i < rows.Length; i++)
                pts[i] = new[] { rows[i], cols[i] };
            return JsonConvert.SerializeObject(pts);
        }

        private static string QuadJson(double[] srcRows, double[] srcCols, double[] dstRows, double[] dstCols) =>
            JsonConvert.SerializeObject(new
            {
                src = Pairs(srcRows, srcCols),
                dst = Pairs(dstRows, dstCols),
            });

        private static double[][] Pairs(double[] rows, double[] cols)
        {
            var pts = new double[rows.Length][];
            for (int i = 0; i < rows.Length; i++)
                pts[i] = new[] { rows[i], cols[i] };
            return pts;
        }

        private static ExecutionContext MakeContext(ILogService log) =>
            new(log, new FlowSession { FlowName = "胶路插件断言" }, new WorkspaceContext(),
                new System.Threading.CancellationTokenSource().Token);

        // ==================================================================
        //  断言 16：平面匹配搜索范围可配置（2026-10-05 通用性改进）
        //  换工位/换相机时不再需要改代码：find 的角度/缩放范围提成了 [StepConfig]。
        //  本条证明参数真的传进了 find 算子（不是死配置）：
        //    ① 默认值 → 对齐成功、检出 OK；
        //    ② 搜索窗口挪到 90°~100°（工件不可能在那儿）→ 对齐必须失败；
        //    ③ 恢复默认 → 检出恢复。
        //  planar 模型按「参考图 + 矫正四点」缓存，与 find 参数无关 —— 三次运行共用同一模型，
        //  行为差异只可能来自 find 参数本身。
        // ==================================================================
        private static void Check16_FindRangeConfigurable()
        {
            // 静态面：XAML 必须把这 6 个参数接出来（漏接 = 用户改不了，等于没做可配置）
            string? xamlPath = ResolveRepoFile(@"Plugins\Plugin.BeadInspect\BeadInspectView.xaml");
            if (xamlPath != null)
            {
                string xaml = File.ReadAllText(xamlPath);
                var binds = new[]
                {
                    "FindAngleStartDeg", "FindAngleExtentDeg",
                    "FindScaleRMin", "FindScaleRMax", "FindScaleCMin", "FindScaleCMax",
                };
                var missing = binds.Where(b => !xaml.Contains($"Binding {b}")).ToArray();
                Check("16d 搜索范围 6 个参数在配置界面可编辑（XAML 绑定齐全）",
                    missing.Length == 0,
                    missing.Length == 0 ? string.Join(",", binds) : "缺失：" + string.Join(",", missing));
            }

            var plugin = BuildRuntimePlugin("胶路_搜索范围");
            var img = LoadImage(1);
            plugin.SrcImage.Value = img;

            plugin.Execute(MakeContext(new StubLog()));
            Check("16a 默认搜索范围：对齐成功、检出 OK",
                plugin.Success.Value is true && plugin.IsOk.Value is true,
                $"Success={plugin.Success.Value} IsOk={plugin.IsOk.Value} Err='{plugin.LastError}'");

            plugin.FindAngleStartDeg = 90;
            plugin.FindAngleExtentDeg = 10;
            plugin.Execute(MakeContext(new StubLog()));
            Check("16b 搜索窗口挪到 90°~100°：对齐失败（证明参数真的传进 find 算子）",
                plugin.Success.Value is false && plugin.LastError.Contains("平面匹配"),
                $"Success={plugin.Success.Value} Err='{plugin.LastError}'");

            plugin.FindAngleStartDeg = -22.35;
            plugin.FindAngleExtentDeg = 44.69;
            plugin.Execute(MakeContext(new StubLog()));
            Check("16c 恢复默认：检出恢复 OK",
                plugin.Success.Value is true && plugin.IsOk.Value is true,
                $"Success={plugin.Success.Value} IsOk={plugin.IsOk.Value} Err='{plugin.LastError}'");

            img.Dispose();
        }
    }
}
