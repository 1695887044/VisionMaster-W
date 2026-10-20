using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Xml.Linq;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Plugins.Util;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 画布「flow-logic 设计稿」收尾守门（2026-10-20）。
    ///
    /// 设计稿定稿的三个可见口径，本文件各守一段：
    ///   ① 操作徽标 —— 比较/逻辑插件把枚举成员折成 GTE / XOR 这类大写短标（其它节点 Collapsed 不占位）；
    ///   ② 节点尺寸 —— 卡片固定 156×58，数值源只有 FlowLayoutStore 一处，XAML 必须走
    ///      {x:Static vm:FlowCanvasViewModel.NodeWidth/NodeHeight}（不许写死数字，否则两处漂移）；
    ///   ③ 小地图 —— 默认关闭（常驻右下角会盖住"结果汇总"这类输出节点）、180×118、工具条开关驱动。
    ///
    /// 为什么"真实测量"要开子进程：断言宿主是控制台进程，`new Application` 有两个实测后果
    /// （预览帧投射改走 UI 线程投递分支永不落地 / 退出时挂住锁 exe，见 BlobDetectChecks 类注释与
    /// docs/code-changes/2026-09-25-冒烟命令超时排障与规避.md）。所以本文件把"实例化 FlowCanvasView
    /// 并量 ActualWidth/ActualHeight"交给子进程（同一个 exe 的 --measure-canvas 模式，即 RenderProbe
    /// 的渲染环境），父进程只解析它打回来的机器可读行——既不污染主进程，又拿到真实测量。
    /// </summary>
    internal static class CanvasDesignChecks
    {
        public static void Run()
        {
            Section("[FLC] 画布 flow-logic 设计稿收尾（操作徽标 / 节点尺寸 156×58 / 小地图 180×118）");

            OpBadgeMatchesDesign();
            NodeSizeMatchesDesign();
            MinimapMatchesDesign();
            ContainerChromeMatchesDesign();
        }

        // ==================================================================
        //  ① 操作徽标：真 ActionStep + 真枚举值（不造假模型、不扫字符串）
        // ==================================================================
        private static void OpBadgeMatchesDesign()
        {
            // 比较插件：Type=GreaterThanOrEqual → GTE。类型名走插件真类型名，
            // 画布的识别口径是 PluginTypeName.Contains("ComparisonPlugin")（与运行时同一份注册表口径）。
            var compare = new ActionStep(
                "\uE700", "数值比较", "VisionMaster.Plugins.Util.ComparisonPlugin", "比较_0");
            compare.SetInputValue("Type", ComparisonType.GreaterThanOrEqual);
            var compareNode = new CanvasNodeViewModel(compare);

            Check("FLC-1 比较插件 Type=GreaterThanOrEqual → OpBadge=\"GTE\" 且 Visible",
                compareNode.OpBadge == "GTE" && compareNode.OpBadgeVisibility == Visibility.Visible,
                $"badge='{compareNode.OpBadge}' vis={compareNode.OpBadgeVisibility}");

            // 逻辑插件：Operation=Xor → XOR
            var logic = new ActionStep(
                "\uE700", "逻辑运算", "VisionMaster.Plugins.Util.LogicOperationPlugin", "逻辑_0");
            logic.SetInputValue("Operation", LogicOperationType.Xor);
            var logicNode = new CanvasNodeViewModel(logic);

            Check("FLC-2 逻辑插件 Operation=Xor → OpBadge=\"XOR\" 且 Visible",
                logicNode.OpBadge == "XOR" && logicNode.OpBadgeVisibility == Visibility.Visible,
                $"badge='{logicNode.OpBadge}' vis={logicNode.OpBadgeVisibility}");

            // 普通节点：空串 + Collapsed（不占位——设计稿里标题行只有标题与类型徽标）
            var plain = new ActionStep("\uE700", "图像采集", StubPluginProvider.LeafPlugin, "采集_0");
            var plainNode = new CanvasNodeViewModel(plain);

            Check("FLC-3 普通节点 → OpBadge 为空串且 Collapsed（不占位）",
                plainNode.OpBadge.Length == 0 && plainNode.OpBadgeVisibility == Visibility.Collapsed,
                $"badge='{plainNode.OpBadge}' vis={plainNode.OpBadgeVisibility}");

            // 改参数 → 徽标即时跟随（节点 VM 吃 InputValues 通知；不重建画布）。
            // 这条同时是 FLC-1/2 的反证：若节点没订阅 InputValues，下面这句就停在 GTE。
            bool notified = false;
            compareNode.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CanvasNodeViewModel.OpBadge)) notified = true;
            };
            compare.SetInputValue("Type", ComparisonType.LessThanOrEqual);

            Check("FLC-4 改 Type（GTE→LTE）→ OpBadge 即时变 \"LTE\" 且发通知（不重建画布）",
                notified && compareNode.OpBadge == "LTE",
                $"badge='{compareNode.OpBadge}' notified={notified}");
        }

        // ==================================================================
        //  ② 节点尺寸：数值源唯一（FlowLayoutStore） + XAML 走 x:Static + 子进程真量
        // ==================================================================
        private static void NodeSizeMatchesDesign()
        {
            // 数值源：设计稿 156×58（flow-logic `.node`）
            Check("FLC-5 FlowLayoutStore.NodeWidth/NodeHeight = 156/58（设计稿口径的唯一数值源）",
                FlowLayoutStore.NodeWidth == 156 && FlowLayoutStore.NodeHeight == 58,
                $"{FlowLayoutStore.NodeWidth}×{FlowLayoutStore.NodeHeight}");

            // 画布侧常量必须是别名（Core 不引 VM，只能 VM → Core 单向引用；两处各写一份必漂移）
            Check("FLC-6 FlowCanvasViewModel.NodeWidth/NodeHeight 是 FlowLayoutStore 的别名（无第二份字面量）",
                FlowCanvasViewModel.NodeWidth == FlowLayoutStore.NodeWidth
                && FlowCanvasViewModel.NodeHeight == FlowLayoutStore.NodeHeight,
                $"vm={FlowCanvasViewModel.NodeWidth}×{FlowCanvasViewModel.NodeHeight} "
                + $"store={FlowLayoutStore.NodeWidth}×{FlowLayoutStore.NodeHeight}");

            // XAML：StepNodeTemplate 的卡片 Grid 必须绑 x:Static 常量（写死 156/58 = 以后改一处漏一处）
            var xamlPath = ResolveRepoFile(@"VisionMaster\Views\FlowCanvasView.xaml");
            if (xamlPath == null)
            {
                Check("FLC-7 前置：FlowCanvasView.xaml 可定位", false, "未找到 AGENTS.md / 视图文件");
            }
            else
            {
                XDocument doc = XDocument.Load(xamlPath);
                var template = doc.Descendants(Presentation + "DataTemplate")
                    .FirstOrDefault(e => (string?)e.Attribute(Xaml + "Key") == "StepNodeTemplate");
                // 模板里第一个同时带 Width/Height 的 Grid = 卡片本体（图标块 28×28 在它内部，文档序更靠后）
                var card = template?.Descendants(Presentation + "Grid")
                    .FirstOrDefault(g => g.Attribute("Width") != null && g.Attribute("Height") != null);
                string width = (string?)card?.Attribute("Width") ?? "";
                string height = (string?)card?.Attribute("Height") ?? "";

                Check("FLC-7 StepNodeTemplate 卡片宽高走 {x:Static vm:FlowCanvasViewModel.NodeWidth/NodeHeight}",
                    width == "{x:Static vm:FlowCanvasViewModel.NodeWidth}"
                    && height == "{x:Static vm:FlowCanvasViewModel.NodeHeight}",
                    card == null ? "模板里找不到带 Width/Height 的卡片 Grid" : $"Width='{width}' Height='{height}'");
            }

            MeasureRealNodeCard();
        }

        /// <summary>
        /// 子进程真量：起 `FlowCanvasChecks --measure-canvas`（RenderProbe 的渲染环境里实例化
        /// FlowCanvasView、布局后量卡片与 NodeHeaderText 的 ActualWidth/ActualHeight），
        /// 解析它打回的 [measure] 行。子进程起不来/没打 ok 一律判红并带上原因——不许静默跳过。
        /// </summary>
        private static void MeasureRealNodeCard()
        {
            string output;
            string? error;
            int exitCode;
            try
            {
                (output, error, exitCode) = RunMeasurementProcess();
            }
            catch (Exception ex)
            {
                Check("FLC-8 子进程真实测量（实例化 FlowCanvasView 量卡片）", false,
                    $"启动子进程失败：{ex.GetType().Name}: {ex.Message}");
                return;
            }

            var lines = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .ToList();
            _measurement = lines;   // 小地图段复用同一次测量（不重复起进程）

            string? Line(string prefix) => lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));

            string? cardLine = Line("[measure] card=");
            string? headerLine = Line("[measure] header=");
            bool ok = Line("[measure] ok") != null;

            Check("FLC-8 子进程真实测量跑完（[measure] ok；失败时带 stderr）",
                ok && exitCode == 0,
                ok ? $"exit={exitCode}" : $"exit={exitCode} out='{string.Join(" | ", lines.Take(4))}' err='{Trim(error, 300)}'");
            if (!ok) return;

            // 卡片实际尺寸 = 156×58（±0.5 容差：DIP 取整/边框像素舍入）
            var (cw, ch) = ParseSize(cardLine?.Substring("[measure] card=".Length));
            Check("FLC-9 卡片实量 = 156×58（±0.5）",
                Math.Abs(cw - FlowLayoutStore.NodeWidth) <= 0.5
                && Math.Abs(ch - FlowLayoutStore.NodeHeight) <= 0.5,
                cardLine ?? "没有 card 行");

            // 标题行在卡片里且量得到宽度（"NodeHeaderText 被裁成 0 宽"这类模板事故的探针）
            const string headerPrefix = "[measure] header=";
            var headerParts = (headerLine ?? "").Split('|');
            string headerText = headerParts.Length > 0 && headerParts[0].Length >= headerPrefix.Length
                ? headerParts[0].Substring(headerPrefix.Length)
                : "";
            double hw = ParseNamedNumber(headerParts, "w=");
            double hh = ParseNamedNumber(headerParts, "h=");

            Check("FLC-10 NodeHeaderText 实量有宽高且落在卡片内（模板真生成、标题没被裁掉）",
                headerText.Length > 0 && hw > 0 && hh > 0
                && hw < cw && hh <= ch,
                $"text='{headerText}' w={hw:0.#} h={hh:0.#} card={cw:0.#}×{ch:0.#}");
        }

        private static (string Output, string Error, int ExitCode) RunMeasurementProcess()
        {
            var psi = new ProcessStartInfo
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = AppContext.BaseDirectory,
            };

            // 直跑 exe 与 `dotnet run`（ProcessPath = dotnet.exe）两种形态都要能起
            string? processPath = Environment.ProcessPath;
            string? entryDll = Assembly.GetEntryAssembly()?.Location;
            if (processPath != null
                && string.Equals(Path.GetFileNameWithoutExtension(processPath), "FlowCanvasChecks",
                    StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = processPath;
            }
            else
            {
                psi.FileName = "dotnet";
                if (string.IsNullOrEmpty(entryDll))
                    throw new InvalidOperationException("取不到入口程序集路径，无法启动测量子进程");
                psi.ArgumentList.Add(entryDll);
            }

            psi.ArgumentList.Add("--measure-canvas");

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start 返回 null");
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(120_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("测量子进程 120 秒未退出");
            }

            return (stdout, stderr, process.ExitCode);
        }

        private static (double W, double H) ParseSize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return (double.NaN, double.NaN);
            var parts = text.Split('x');
            return parts.Length == 2
                ? (ParseDouble(parts[0]), ParseDouble(parts[1]))
                : (double.NaN, double.NaN);
        }

        private static double ParseNamedNumber(string[] parts, string prefix)
        {
            var part = parts.FirstOrDefault(p => p.StartsWith(prefix, StringComparison.Ordinal));
            return part == null ? double.NaN : ParseDouble(part.Substring(prefix.Length));
        }

        private static double ParseDouble(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : double.NaN;

        private static string Trim(string? text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var flat = text.Replace("\r", " ").Replace("\n", " ");
            return flat.Length <= max ? flat : flat.Substring(0, max) + "…";
        }

        // ==================================================================
        //  ③ 小地图：默认关 / 180×118 / 工具条开关 + ShowMinimap 触发器
        // ==================================================================
        private static void MinimapMatchesDesign()
        {
            // 默认关闭（设计稿明确修掉的遮挡问题：常驻右下角会盖住"结果汇总"这类输出节点）
            var bare = new FlowCanvasViewModel(new WorkspaceContext(), new StubPluginProvider());
            Check("FLC-11 ShowMinimap 默认 false（会话内视图态，不落盘）",
                !bare.ShowMinimap, $"ShowMinimap={bare.ShowMinimap}");

            bool notified = false;
            bare.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FlowCanvasViewModel.ShowMinimap)) notified = true;
            };
            bare.ShowMinimap = true;
            Check("FLC-12 打开小地图 → 值翻转并发 ShowMinimap 通知（工具条 CheckBox 的数据源）",
                bare.ShowMinimap && notified, $"ShowMinimap={bare.ShowMinimap} notified={notified}");

            var xamlPath = ResolveRepoFile(@"VisionMaster\Views\FlowCanvasView.xaml");
            if (xamlPath == null)
            {
                Check("FLC-13 前置：FlowCanvasView.xaml 可定位（小地图段）", false, "未找到 AGENTS.md / 视图文件");
                return;
            }

            XDocument doc = XDocument.Load(xamlPath);

            // 小地图宿主 Border：以"包含 nodify:Minimap"认人（不靠行号/注释）
            var minimapBorder = doc.Descendants(Presentation + "Border")
                .FirstOrDefault(b => b.Descendants(Nodify + "Minimap").Any());
            string width = (string?)minimapBorder?.Attribute("Width") ?? "";
            string height = (string?)minimapBorder?.Attribute("Height") ?? "";

            Check("FLC-13 小地图 Border = 180×118（设计稿口径）",
                width == "180" && height == "118",
                minimapBorder == null ? "找不到包含 nodify:Minimap 的 Border" : $"Width='{width}' Height='{height}'");

            // 关闭时整体 Collapsed（不是隐藏占位，也不许只是 Opacity=0）
            var borderStyle = minimapBorder?.Element(Presentation + "Border.Style");
            var hideTrigger = borderStyle?
                .Descendants(Presentation + "DataTrigger")
                .FirstOrDefault(t => ((string?)t.Attribute("Binding") ?? "").Contains("ShowMinimap", StringComparison.Ordinal)
                                     && (string?)t.Attribute("Value") == "False");
            bool collapses = hideTrigger?
                .Elements(Presentation + "Setter")
                .Any(s => (string?)s.Attribute("Property") == "Visibility"
                          && (string?)s.Attribute("Value") == "Collapsed") == true;

            Check("FLC-14 ShowMinimap=False → 小地图 Border Visibility=Collapsed（不占位、不遮挡）",
                collapses,
                hideTrigger == null ? "找不到 Binding=ShowMinimap / Value=False 的 DataTrigger" : "触发器齐备");

            // 工具条开关：CheckBox 绑 ShowMinimap（唯一入口，且与属性同名）
            var toggles = doc.Descendants(Presentation + "CheckBox")
                .Where(c => ((string?)c.Attribute("IsChecked") ?? "").Contains("ShowMinimap", StringComparison.Ordinal))
                .ToList();

            Check("FLC-15 工具条「小地图」开关绑 ShowMinimap（CheckBox 恰一个）",
                toggles.Count == 1, $"命中 {toggles.Count} 个");

            // 真量：尺寸与显隐都来自子进程（Collapsed 态量不到宽高，必须打开后量）
            MeasureRealMinimap();
        }

        private static void MeasureRealMinimap()
        {
            // 与 FLC-8 共用同一个子进程入口：它同时量卡片与小地图（一次启动，两段断言共用证据）。
            // 这里只读缓存结果，避免重复起进程；没有结果时按"未测量"判红（不许假绿）。
            if (_measurement == null)
            {
                Check("FLC-16 小地图实量（子进程）", false, "没有测量结果（FLC-8 子进程未跑通）");
                return;
            }

            var lines = _measurement;
            string? Line(string prefix) => lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));

            string? sizeLine = Line("[measure] minimap=");
            string? hiddenLine = Line("[measure] minimap-hidden=");
            var (w, h) = ParseSize(sizeLine?.Substring("[measure] minimap=".Length));

            Check("FLC-16 小地图实量 = 180×118（±0.5）",
                Math.Abs(w - 180) <= 0.5 && Math.Abs(h - 118) <= 0.5,
                sizeLine ?? "没有 minimap 行");

            Check("FLC-17 关掉开关后小地图实量为 Collapsed（触发器真生效，不是只改了绑定值）",
                hiddenLine == "[measure] minimap-hidden=Collapsed",
                hiddenLine ?? "没有 minimap-hidden 行");
        }

        /// <summary>FLC-8 子进程输出的行缓存（小地图段复用同一次测量，不重复起进程）</summary>
        private static List<string>? _measurement;

        // ==================================================================
        //  ④ 容器/泳道头与状态表达（FLC-18..22，2026-10-20）
        //
        //  产品侧本批补的四个可见口径：
        //    · 组头 46 / 泳道头 28 是 FlowLayoutStore 的唯一定义，XAML 必须走 x:Static；
        //    · If 的 Else/ElseIf 与 Case 的默认分支是"中性泳道"（不套类型色）；
        //    · 父容器失败或任一分支缺条件时整列泳道红染（IsLaneParentFaulted）；
        //    · 单泳道长条件容器的组头不许被条件胶囊撑爆（条件列 * + MaxWidth 190 + 裁剪）；
        //    · 禁用态整节点降透明（模板根 Grid 的 Opacity），不是只压卡片本体。
        // ==================================================================
        private static void ContainerChromeMatchesDesign()
        {
            var xamlPath = ResolveRepoFile(@"VisionMaster\Views\FlowCanvasView.xaml");
            XDocument? doc = xamlPath == null ? null : XDocument.Load(xamlPath);

            HeaderHeightSource(doc);
            LaneNeutralMatrix();
            LaneParentFaultMatrix();
            ContainerHeaderChrome(doc);
            DisabledOpacityChrome(doc);
            ContainerHeaderMeasurement();
        }

        // ---- FLC-18：组头/泳道头高度只有一处数值源，XAML 走 x:Static ----
        private static void HeaderHeightSource(XDocument? doc)
        {
            Check("FLC-18 FlowLayoutStore.FrameHeaderHeight=46 / LaneHeaderHeight=28（组头/泳道头唯一数值源）",
                FlowLayoutStore.FrameHeaderHeight == 46 && FlowLayoutStore.LaneHeaderHeight == 28,
                $"frame={FlowLayoutStore.FrameHeaderHeight} lane={FlowLayoutStore.LaneHeaderHeight}");

            var containerHeader = FindNamedElement(doc, "ContainerHeader");
            var laneHeader = FindNamedElement(doc, "LaneHeader");
            string containerHeight = (string?)containerHeader?.Attribute("Height") ?? "";
            string laneHeight = (string?)laneHeader?.Attribute("Height") ?? "";

            Check("FLC-18 FlowCanvasView.xaml 容器头/泳道头 Height 走 x:Static（不写死 46/28）",
                containerHeight == "{x:Static vm:FlowCanvasViewModel.FrameHeaderHeight}"
                && laneHeight == "{x:Static vm:FlowCanvasViewModel.LaneHeaderHeight}",
                containerHeader == null || laneHeader == null
                    ? $"找不到 ContainerHeader/LaneHeader（{(containerHeader != null)}/{(laneHeader != null)}）"
                    : $"ContainerHeader='{containerHeight}' LaneHeader='{laneHeight}'");
        }

        // ---- FLC-19：中性泳道（If 的 Else / Case 的默认分支不套类型色）----
        private static void LaneNeutralMatrix()
        {
            var h = new Harness();
            var ifStep = h.If("判断");
            ifStep.Children[0].Expression = "Score > 80";   // 条件齐备，避免"缺条件"干扰中性判定
            var caseStep = new CaseStep("\uE700", "分支匹配", "BuiltIn_Case", "case_0");
            caseStep.Children[0].Expression = "1";          // Case 分支的匹配值（空值会判缺条件）
            h.Add(ifStep);
            h.Add(caseStep);
            _ = h.Canvas;

            var ifLane = h.Lane(ifStep.Children[0]);
            var elseLane = h.Lane(ifStep.Children[1]);
            var caseLane = h.Lane(caseStep.Children[0]);
            var defaultLane = h.Lane(caseStep.Children[1]);
            if (ifLane == null || elseLane == null || caseLane == null || defaultLane == null)
            {
                Check("FLC-19 前置：四条泳道节点都生成", false,
                    $"if={ifLane != null} else={elseLane != null} case={caseLane != null} default={defaultLane != null}");
                return;
            }

            Check("FLC-19 If 容器：If 分支 IsNeutralLane=false / Else 分支 =true",
                !ifLane.IsNeutralLane && elseLane.IsNeutralLane,
                $"if={ifLane.IsNeutralLane} else={elseLane.IsNeutralLane}");

            Check("FLC-19 Case 容器：Case 分支 IsNeutralLane=false / 默认分支 =true",
                !caseLane.IsNeutralLane && defaultLane.IsNeutralLane,
                $"case={caseLane.IsNeutralLane} default={defaultLane.IsNeutralLane}");

            // 模板触发器吃 IsNeutralLane：分支类型一变就必须补通知，否则界面停在旧配色
            bool neutralNotified = false;
            ifLane.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CanvasNodeViewModel.IsNeutralLane)) neutralNotified = true;
            };

            ifStep.Children[0].BranchType = BranchType.Else;
            Check("FLC-19 改分支 BranchType → IsNeutralLane 即时翻转并发通知",
                neutralNotified && ifLane.IsNeutralLane,
                $"neutral={ifLane.IsNeutralLane} notified={neutralNotified}");
            ifStep.Children[0].BranchType = BranchType.If;
        }

        // ---- FLC-20：父容器失败 / 分支缺条件 → 整列泳道红染（恢复后撤销）----
        private static void LaneParentFaultMatrix()
        {
            var h = new Harness();
            var cond = h.If("判断");
            cond.Children[0].Expression = "Score > 80";   // 基线：条件齐备
            h.Add(cond);
            _ = h.Canvas;

            var lane = h.Lane(cond.Children[0]);
            if (lane == null)
            {
                Check("FLC-20 前置：泳道节点生成", false, "If 分支泳道没上画布");
                return;
            }

            bool notified = false;
            lane.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CanvasNodeViewModel.IsLaneParentFaulted)) notified = true;
            };

            Check("FLC-20 基线：父容器 Idle + 条件齐备 → 泳道不红染（两个分项都 false）",
                !lane.IsLaneParentFaulted && !lane.IsLaneParentFailed && !lane.IsLaneParentMissing,
                $"faulted={lane.IsLaneParentFaulted} failed={lane.IsLaneParentFailed} missing={lane.IsLaneParentMissing}");

            cond.State = StepState.Failed;
            Check("FLC-20 父容器 State=Failed → IsLaneParentFaulted=true（IsLaneParentFailed=true）且发通知",
                lane.IsLaneParentFaulted && lane.IsLaneParentFailed && !lane.IsLaneParentMissing && notified,
                $"faulted={lane.IsLaneParentFaulted} failed={lane.IsLaneParentFailed} "
                + $"missing={lane.IsLaneParentMissing} notified={notified}");

            notified = false;
            cond.State = StepState.Idle;
            Check("FLC-20 恢复 State=Idle → 红染撤销且发通知",
                !lane.IsLaneParentFaulted && notified,
                $"faulted={lane.IsLaneParentFaulted} notified={notified}");

            notified = false;
            cond.Children[0].Expression = "";
            Check("FLC-20 某分支 IsConditionMissing=true → IsLaneParentFaulted=true（IsLaneParentMissing=true）且发通知",
                lane.IsLaneParentFaulted && lane.IsLaneParentMissing && !lane.IsLaneParentFailed && notified,
                $"faulted={lane.IsLaneParentFaulted} missing={lane.IsLaneParentMissing} "
                + $"failed={lane.IsLaneParentFailed} notified={notified}");

            cond.Children[0].Expression = "Score > 80";
            Check("FLC-20 补齐条件 → 红染撤销",
                !lane.IsLaneParentFaulted && !lane.IsLaneParentMissing,
                $"faulted={lane.IsLaneParentFaulted} missing={lane.IsLaneParentMissing}");
        }

        // ---- FLC-21 静态：条件列 = * / 胶囊 MaxWidth=190 / 组头裁剪 ----
        private static void ContainerHeaderChrome(XDocument? doc)
        {
            var containerHeader = FindNamedElement(doc, "ContainerHeader");
            var condBorder = FindNamedElement(doc, "ContainerCondBorder");
            var headerGrid = containerHeader?.Elements(Presentation + "Grid").FirstOrDefault();
            var columnDefs = headerGrid?.Element(Presentation + "Grid.ColumnDefinitions")?
                .Elements(Presentation + "ColumnDefinition").ToList();

            int condColumn = -1;
            var condColumnText = (string?)condBorder?.Attribute("Grid.Column");
            if (condColumnText != null && !int.TryParse(condColumnText, out condColumn)) condColumn = -1;

            string condColumnWidth = condColumn >= 0 && columnDefs != null && condColumn < columnDefs.Count
                ? (string?)columnDefs[condColumn].Attribute("Width") ?? ""
                : "";
            Check("FLC-21 组头条件列宽 = *（条件胶囊按剩余空间收敛，不按文本长度撑开）",
                condColumnWidth == "*",
                condBorder == null
                    ? "找不到 ContainerCondBorder"
                    : $"Grid.Column={condColumn} 列数={columnDefs?.Count ?? 0} width='{condColumnWidth}'");

            bool maxOk = (string?)condBorder?.Attribute("MaxWidth") == "190"
                         && (string?)condBorder?.Attribute("MinWidth") == "0";
            bool clipOk = (string?)containerHeader?.Attribute("ClipToBounds") == "True";
            Check("FLC-21 条件胶囊 MaxWidth=190 / MinWidth=0，且组头 ClipToBounds=True（超长文本裁在组头内）",
                maxOk && clipOk,
                $"MaxWidth='{(string?)condBorder?.Attribute("MaxWidth")}' "
                + $"MinWidth='{(string?)condBorder?.Attribute("MinWidth")}' "
                + $"ClipToBounds='{(string?)containerHeader?.Attribute("ClipToBounds")}'");
        }

        // ---- FLC-22 静态：禁用态降透明的靶子是模板根 Grid，不是卡片本体 ----
        private static void DisabledOpacityChrome(XDocument? doc)
        {
            var stepTemplate = FindTemplate(doc, "StepNodeTemplate");
            var stepRoot = stepTemplate?.Elements(Presentation + "Grid").FirstOrDefault();
            string stepRootName = (string?)stepRoot?.Attribute(Xaml + "Name") ?? "";
            var stepTrigger = stepTemplate?
                .Element(Presentation + "DataTemplate.Triggers")?
                .Elements(Presentation + "DataTrigger")
                .FirstOrDefault(t => ((string?)t.Attribute("Binding") ?? "")
                                         .Contains("Model.IsDisEnable", StringComparison.Ordinal)
                                     && (string?)t.Attribute("Value") == "True");
            bool stepRootOpacity = stepRootName.Length > 0 && stepTrigger?
                .Elements(Presentation + "Setter")
                .Any(s => (string?)s.Attribute("TargetName") == stepRootName
                          && (string?)s.Attribute("Property") == "Opacity"
                          && (string?)s.Attribute("Value") == "0.45") == true;

            Check("FLC-22 禁用节点：模板根 Grid（StepNodeRoot）自身吃 Opacity=0.45（不是只降卡片本体）",
                stepRootOpacity,
                stepTemplate == null
                    ? "找不到 StepNodeTemplate"
                    : $"root='{stepRootName}' trigger={(stepTrigger != null)}");

            var containerTemplate = FindTemplate(doc, "ContainerNodeTemplate");
            var containerRoot = containerTemplate?.Elements(Presentation + "Grid").FirstOrDefault();
            string containerRootName = (string?)containerRoot?.Attribute(Xaml + "Name") ?? "";
            var containerStyle = containerRoot?.Element(Presentation + "Grid.Style")?
                .Element(Presentation + "Style");
            var containerTrigger = containerStyle?.Element(Presentation + "Style.Triggers")?
                .Elements(Presentation + "DataTrigger")
                .FirstOrDefault(t => ((string?)t.Attribute("Binding") ?? "")
                                         .Contains("Model.IsDisEnable", StringComparison.Ordinal)
                                     && (string?)t.Attribute("Value") == "True");
            bool containerRootOpacity = containerTrigger?
                .Elements(Presentation + "Setter")
                .Any(s => (string?)s.Attribute("TargetName") == null
                          && (string?)s.Attribute("Property") == "Opacity"
                          && (string?)s.Attribute("Value") == "0.45") == true;

            Check("FLC-22 禁用容器：根 Grid（ContainerNodeRoot）样式触发器 Opacity=0.45（作用于自身）",
                containerRootName == "ContainerNodeRoot" && containerRootOpacity,
                containerTemplate == null
                    ? "找不到 ContainerNodeTemplate"
                    : $"root='{containerRootName}' trigger={(containerTrigger != null)}");
        }

        // ---- FLC-21 实量：长条件组头的四件套边界；FLC-22 实量：禁用态根 Grid 透明度 ----
        private static void ContainerHeaderMeasurement()
        {
            if (_measurement == null)
            {
                Check("FLC-21 长条件 While 组头实量（子进程）", false,
                    "没有测量结果（FLC-8 子进程未跑通）");
                Check("FLC-22 禁用态根 Grid 实量 Opacity=0.45", false,
                    "没有测量结果（FLC-8 子进程未跑通）");
                return;
            }

            var lines = _measurement;
            string? Line(string prefix) => lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));

            var headerRect = ParseRectLine(Line("[measure] while-header="), "[measure] while-header=");
            var condRect = ParseRectLine(Line("[measure] while-cond="), "[measure] while-cond=");
            var condTextRect = ParseRectLine(Line("[measure] while-condtext="), "[measure] while-condtext=");
            var metaRect = ParseRectLine(Line("[measure] while-meta="), "[measure] while-meta=");
            var dotRect = ParseRectLine(Line("[measure] while-dot="), "[measure] while-dot=");
            var toggleRect = ParseRectLine(Line("[measure] while-toggle="), "[measure] while-toggle=");

            string? whileFail = Line("[measure] while-FAIL:");
            if (headerRect == null || condRect == null || condTextRect == null
                || metaRect == null || dotRect == null || toggleRect == null)
            {
                Check("FLC-21 长条件 While 组头实量（子进程）", false,
                    whileFail ?? "缺少 while-header/cond/condtext/meta/dot/toggle 测量行");
            }
            else
            {
                var header = headerRect.Value;
                var cond = condRect.Value;
                var condText = condTextRect.Value;
                var meta = metaRect.Value;
                var dot = dotRect.Value;
                var toggle = toggleRect.Value;

                Check("FLC-21 长条件 While 组头实量：高 46 / 宽 > 0（组头真生成）",
                    Math.Abs(header.Height - FlowLayoutStore.FrameHeaderHeight) <= 0.5 && header.Width > 0,
                    Line("[measure] while-header=") ?? "");

                Check("FLC-21 条件胶囊受 MaxWidth=190 约束，且文字不越出胶囊",
                    cond.Width <= 190.5 && condText.Width <= cond.Width + 0.5 && condText.Right <= cond.Right + 0.5,
                    $"cond.w={cond.Width:0.#} text.w={condText.Width:0.#} "
                    + $"text.right={condText.Right:0.#} cond.right={cond.Right:0.#}");

                Check("FLC-21 条件列不压右侧统计（cond.right <= meta.left）",
                    cond.Right <= meta.Left + 0.5,
                    $"cond.right={cond.Right:0.#} meta.left={meta.Left:0.#}");

                Check("FLC-21 统计不压状态点，状态点/折叠钮都在组头内（不溢出）",
                    meta.Right <= dot.Left + 0.5
                    && dot.Left >= -0.5 && dot.Right <= header.Width + 0.5
                    && toggle.Left >= header.Left - 0.5 && toggle.Right <= header.Right + 0.5
                    && toggle.Top >= header.Top - 0.5 && toggle.Bottom <= header.Bottom + 0.5,
                    $"meta.right={meta.Right:0.#} dot=[{dot.Left:0.#},{dot.Right:0.#}] "
                    + $"toggle=[{toggle.Left:0.#},{toggle.Right:0.#}]×[{toggle.Top:0.#},{toggle.Bottom:0.#}] "
                    + $"header=[{header.Left:0.#},{header.Right:0.#}]×[{header.Top:0.#},{header.Bottom:0.#}]");
            }

            double stepOpacity = ParseTrailingNumber(
                Line("[measure] step-disabled-opacity="), "[measure] step-disabled-opacity=");
            double containerOpacity = ParseTrailingNumber(
                Line("[measure] container-disabled-opacity="), "[measure] container-disabled-opacity=");
            Check("FLC-22 禁用态根 Grid 实量 Opacity=0.45（StepNodeRoot / ContainerNodeRoot，不只是卡片本体）",
                Math.Abs(stepOpacity - 0.45) <= 0.001 && Math.Abs(containerOpacity - 0.45) <= 0.001,
                $"step={stepOpacity:0.###} container={containerOpacity:0.###}");
        }

        // ==================================================================
        //  helpers
        // ==================================================================

        private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        private static readonly XNamespace Nodify = "clr-namespace:Nodify;assembly=Nodify";

        private static XElement? FindNamedElement(XDocument? doc, string name)
            => doc?.Descendants().FirstOrDefault(e => (string?)e.Attribute(Xaml + "Name") == name);

        private static XElement? FindTemplate(XDocument? doc, string key)
            => doc?.Descendants(Presentation + "DataTemplate")
                .FirstOrDefault(e => (string?)e.Attribute(Xaml + "Key") == key);

        /// <summary>解析子进程的矩形行（x/y/w/h 四项缺一即视为"没量到"）</summary>
        private static Rect? ParseRectLine(string? line, string prefix)
        {
            if (string.IsNullOrEmpty(line) || !line.StartsWith(prefix, StringComparison.Ordinal)) return null;

            var parts = line.Substring(prefix.Length).Split('|');
            double x = ParseNamedNumber(parts, "x=");
            double y = ParseNamedNumber(parts, "y=");
            double w = ParseNamedNumber(parts, "w=");
            double h = ParseNamedNumber(parts, "h=");
            return double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(w) || double.IsNaN(h)
                ? null
                : new Rect(x, y, w, h);
        }

        private static double ParseTrailingNumber(string? line, string prefix)
            => line != null && line.StartsWith(prefix, StringComparison.Ordinal)
                ? ParseDouble(line.Substring(prefix.Length))
                : double.NaN;

        private static string? ResolveRepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
