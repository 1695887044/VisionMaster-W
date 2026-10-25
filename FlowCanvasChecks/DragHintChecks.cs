using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;

namespace FlowCanvasChecks
{
    /// <summary>
    /// [DH] 拖拽落点提示（设计基线 docs/UI设计基线/drag-drop-hint v2）。
    ///
    /// 三层判据，每层都是"改坏了不报错、只表现为提示不对或没提示"的类型：
    ///  ① **文案公式**（DragHintText）：纯函数，逐条钉字符串与截断口径；
    ///  ② **状态通道**（DragHintPresenter）：单写者状态的生命周期（Show/Hide/Reset）；
    ///  ③ **落点判定**（FlowCanvasViewModel.DescribeDropTarget）：与 DropToolAt 共用判据，
    ///     四档状态各自可达，且"折叠容器 → Caution""Break 在循环外 → Illegal"这两个最容易被
    ///     写成永远 Valid 的分支必须真出现。
    ///
    /// 另有一条静态扫描钉住 XAML 接线（三处模板都用 DragGhostStack，提示条绑的是
    /// DragHintPresenter.Current）——这类"模板漏换一处就没提示"的问题只有扫描能拦。
    /// </summary>
    internal static class DragHintChecks
    {
        public static void Run()
        {
            Program.Section("[DH] 拖拽落点提示（文案公式 / 状态通道 / 画布落点判定）");

            TextFormula();
            PresenterLifecycle();
            CanvasDropTargets();
            XamlWiring();
        }

        // ==================================================================
        //  ① 文案公式
        // ==================================================================
        private static void TextFormula()
        {
            Program.Check("DH1 添加：动词 + 名称 + 路径 + 位置",
                DragHintText.Add("图像采集", DragHintText.TopLevelPath) == "添加「图像采集」→ 主流程 · 末尾",
                DragHintText.Add("图像采集", DragHintText.TopLevelPath));

            Program.Check("DH1 移动：动词换成「移动」（同一套公式，不按来源猜）",
                DragHintText.Move("模板匹配", "并行组 / 分支 2") == "移动「模板匹配」→ 并行组 / 分支 2 · 末尾",
                DragHintText.Move("模板匹配", "并行组 / 分支 2"));

            Program.Check("DH1 按效果选动词：isCopy=false → 移动（不出现「添加」）",
                DragHintText.Build(false, "A", "主流程") == "移动「A」→ 主流程 · 末尾",
                DragHintText.Build(false, "A", "主流程"));

            Program.Check("DH1 插到之前/之后：两个方向都是单源（不在调用点手拼）",
                DragHintText.Add("采集", "主流程", DragHintText.InsertBeforePosition("匹配"))
                    == "添加「采集」→ 主流程 · 插到「匹配」之前"
                && DragHintText.InsertAfterPosition("匹配") == "插到「匹配」之后",
                DragHintText.InsertAfterPosition("匹配"));

            Program.Check("DH1 路径分隔符是 /（用户 2026-10-24 拍板，不是 ›）",
                DragHintText.Path("并行组", "分支 2") == "并行组 / 分支 2"
                && !DragHintText.Path("并行组", "分支 2").Contains('\u203A'),
                DragHintText.Path("并行组", "分支 2"));

            Program.Check("DH1 路径 ≥3 级保留末两级、前缀省略为 …（叶子信息优先）",
                DragHintText.Path("L1", "L2", "L3") == "… / L2 / L3",
                DragHintText.Path("L1", "L2", "L3"));

            Program.Check("DH1 路径空集合回落「主流程」（不是空串）",
                DragHintText.Path(Array.Empty<string?>()) == DragHintText.TopLevelPath,
                DragHintText.Path(Array.Empty<string?>()));

            Program.Check("DH1 非法：原因原样跟在「不能放这里：」之后（必须可执行）",
                DragHintText.Illegal(StepFactory.LoopControlPlacementMessage)
                    == "不能放这里：" + StepFactory.LoopControlPlacementMessage
                && DragHintText.Illegal("x").StartsWith("不能放这里：", StringComparison.Ordinal),
                DragHintText.Illegal(StepFactory.LoopControlPlacementMessage));

            Program.Check("DH1 需注意：会展开提示带容器名",
                DragHintText.WillExpand("并行组") == "会展开「并行组」后再放入",
                DragHintText.WillExpand("并行组"));

            Program.Check("DH1 运行锁文案与 EditGate 同源（不另抄一份字面量）",
                DragHintText.Locked == EditGate.RunLockedMessage && DragHintText.Locked.Length > 0,
                DragHintText.Locked);
        }

        // ==================================================================
        //  ② 状态通道生命周期
        // ==================================================================
        private static void PresenterLifecycle()
        {
            DragHintPresenter.Reset();
            Program.Check("DH2 复位后：无文案、Hidden、不可见",
                DragHintPresenter.Current.Text.Length == 0
                && DragHintPresenter.Current.Kind == DragHintKind.Hidden
                && !DragHintPresenter.Current.IsVisible,
                $"text='{DragHintPresenter.Current.Text}' kind={DragHintPresenter.Current.Kind}");

            int notified = 0;
            void OnChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e) => notified++;
            DragHintPresenter.Current.PropertyChanged += OnChanged;
            try
            {
                DragHintPresenter.Show(DragHintPresenter.CanvasOwner, "添加「A」→ 主流程 · 末尾", DragHintKind.Valid);
                Program.Check("DH2 Show：写入文案与档位、可见，并发出通知",
                    DragHintPresenter.Current.Text == "添加「A」→ 主流程 · 末尾"
                    && DragHintPresenter.Current.Kind == DragHintKind.Valid
                    && DragHintPresenter.Current.IsVisible
                    && notified > 0,
                    $"kind={DragHintPresenter.Current.Kind} 通知={notified}");

                // 同值再写一次不该再发通知（否则拖拽每帧都刷一遍绑定）
                notified = 0;
                DragHintPresenter.Show(DragHintPresenter.CanvasOwner, "添加「A」→ 主流程 · 末尾", DragHintKind.Valid);
                Program.Check("DH2 同值重复 Show 不发通知（拖拽每帧都会调，不能每帧刷绑定）",
                    notified == 0, $"通知={notified}");

                // 跨放置端：别的放置端的 DragLeave 不许抹掉当前提示。
                // gong 的 DragLeave 是 Dispatcher 延迟派发的，从流程栏移到画布时它可能后到。
                DragHintPresenter.Hide(DragHintPresenter.ProcessTreeOwner);
                Program.Check("DH2 别的放置端 DragLeave 不抹当前提示（延迟派发的 DragLeave 会后到）",
                    DragHintPresenter.Current.IsVisible
                    && DragHintPresenter.Current.Text.Length > 0,
                    $"kind={DragHintPresenter.Current.Kind}");

                DragHintPresenter.Hide(DragHintPresenter.CanvasOwner);
                Program.Check("DH2 Hide：收起并把文案清空（否则下次拖拽会先闪上一次的落点）",
                    !DragHintPresenter.Current.IsVisible
                    && DragHintPresenter.Current.Text.Length == 0,
                    $"text='{DragHintPresenter.Current.Text}'");

                // 空文案视同"没有落点"：Show 必须自己收起，不能留下一条空底色
                DragHintPresenter.Show(DragHintPresenter.CanvasOwner, "添加「A」→ 主流程 · 末尾", DragHintKind.Valid);
                DragHintPresenter.Show(DragHintPresenter.CanvasOwner, "   ", DragHintKind.Valid);
                Program.Check("DH2 空白文案 Show → 自动收起（不留空底色）",
                    !DragHintPresenter.Current.IsVisible,
                    $"kind={DragHintPresenter.Current.Kind}");

                // Hidden 档位即使带文案也不显示
                DragHintPresenter.Show(DragHintPresenter.CanvasOwner, "某文案", DragHintKind.Hidden);
                Program.Check("DH2 Hidden 档位带文案也不显示（档位优先于文案）",
                    !DragHintPresenter.Current.IsVisible,
                    $"kind={DragHintPresenter.Current.Kind}");
            }
            finally
            {
                DragHintPresenter.Current.PropertyChanged -= OnChanged;
                DragHintPresenter.Reset();
            }
        }

        // ==================================================================
        //  ③ 画布落点判定（四档各自可达）
        // ==================================================================
        private static void CanvasDropTargets()
        {
            // ---- 顶层末尾 ----
            var h = new Harness();
            var a = h.Add(h.Leaf("A"));
            var canvas = h.Canvas;
            var tool = NewTool("落点桩");

            var empty = canvas.DescribeDropTarget(tool, new Point(0, 5000));
            Program.Check("DH3 顶层末尾 → Valid「添加「落点桩」→ 主流程 · 末尾」",
                empty.Kind == DragHintKind.Valid
                && empty.Text == "添加「落点桩」→ 主流程 · 末尾",
                $"{empty.Kind} / {empty.Text}");

            // ---- 插到两个兄弟之间 ----
            var b = h.Add(h.Leaf("B"));
            var nodeA = h.Node(a)!;
            var nodeB = h.Node(b)!;
            var between = canvas.DescribeDropTarget(tool,
                new Point(nodeA.Location.X + 20, (nodeA.Location.Y + FlowCanvasViewModel.NodeHeight + nodeB.Location.Y) / 2));
            Program.Check("DH3 两兄弟之间 → Valid 且位置是「插到「B」之前」（与 DropToolAt 的插入下标同源）",
                between.Kind == DragHintKind.Valid
                && between.Text == "添加「落点桩」→ 主流程 · 插到「B」之前",
                $"{between.Kind} / {between.Text}");

            // ---- 容器内：路径 = 容器 / 第一条分支 ----
            var h2 = new Harness();
            var cond = h2.If("判断");
            cond.Children[0].Steps.Add(h2.Leaf("内A"));
            h2.Add(cond);
            var cv2 = h2.Canvas;
            var frame = h2.Node(cond)!;
            var inside = cv2.DescribeDropTarget(tool,
                new Point(frame.Location.X + frame.LaneWidth / 2, frame.Location.Y + frame.LaneHeight / 2));

            Program.Check("DH3 容器内 → Valid 且路径带「容器 / 分支」（不是只写容器名）",
                inside.Kind == DragHintKind.Valid
                && inside.Text.Contains("判断")
                && inside.Text.Contains(" / ")
                && inside.Text.Contains("· 末尾"),
                $"{inside.Kind} / {inside.Text}");

            // ---- 状态③：折叠容器 → Caution，并且落下时真的展开 ----
            cv2.ToggleCollapseCommand.Execute(frame);
            var collapsedFrame = h2.Node(cond)!;
            var intoCollapsed = cv2.DescribeDropTarget(tool,
                new Point(collapsedFrame.Location.X + collapsedFrame.LaneWidth / 2,
                    collapsedFrame.Location.Y + collapsedFrame.LaneHeight / 2));

            Program.Check("DH3 落点在折叠容器内 → Caution「会展开「判断」后再放入」（状态③）",
                intoCollapsed.Kind == DragHintKind.Caution
                && intoCollapsed.Text == "会展开「判断」后再放入",
                $"{intoCollapsed.Kind} / {intoCollapsed.Text}");

            // 状态③的兑现：提示说"会展开"，落下去就必须真展开——否则新步骤插进去了却看不见
            int stepsBefore = cond.Children[0].Steps.Count;
            bool dropped = cv2.DropToolAt(tool,
                new Point(collapsedFrame.Location.X + collapsedFrame.LaneWidth / 2,
                    collapsedFrame.Location.Y + collapsedFrame.LaneHeight / 2));
            var afterDropNode = h2.Node(cond);
            var insertedInBranch = cond.Children[0].Steps.LastOrDefault();

            Program.Check("DH3 状态③兑现：落进折叠容器后容器自动展开、新步骤可见",
                dropped
                && cond.Children[0].Steps.Count == stepsBefore + 1
                && afterDropNode != null
                && !afterDropNode.IsCollapsed
                && insertedInBranch != null
                && h2.Node(insertedInBranch) != null,
                $"步骤={cond.Children[0].Steps.Count} 折叠={afterDropNode?.IsCollapsed} 新节点可见={insertedInBranch != null && h2.Node(insertedInBranch) != null}");

            // 反向：容器已展开时不出现 Caution（否则状态③永远亮着，等于没有）
            var expandedFrame = h2.Node(cond)!;
            var intoExpanded = cv2.DescribeDropTarget(tool,
                new Point(expandedFrame.Location.X + expandedFrame.LaneWidth / 2,
                    expandedFrame.Location.Y + expandedFrame.LaneHeight / 2));
            Program.Check("DH3 反向：容器已展开 → 不是 Caution（状态③只对折叠容器亮）",
                intoExpanded.Kind == DragHintKind.Valid,
                $"{intoExpanded.Kind} / {intoExpanded.Text}");

            // ---- null 步骤不许把提示打断（集合里可能有反序列化残留）----
            // 注意：这条断言必须自己把 null 摘掉。WorkspaceContext 订阅着全局 StepRenamed 消息，
            // 会把**所有**曾创建过的方案都遍历一遍；留着 null 会让后面任何一次改名在
            // RefreshStepPortDisplayAddresses 里 NRE（实测把 G7 复制粘贴整段打崩）。
            var hNull = new Harness();
            var n1 = hNull.Add(hNull.Leaf("N1"));
            var n2 = hNull.Add(hNull.Leaf("N2"));
            var cvNull = hNull.Canvas;
            bool threw = false;
            string nullText = string.Empty;
            try
            {
                hNull.Flow.Steps.Insert(1, null!);
                var r = cvNull.DescribeDropTarget(tool, new Point(hNull.Node(n1)!.Location.X + 20,
                    (hNull.Node(n1)!.Location.Y + FlowCanvasViewModel.NodeHeight + hNull.Node(n2)!.Location.Y) / 2));
                nullText = r.Text;
            }
            catch (Exception ex)
            {
                threw = true;
                nullText = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                // 无论成败都要摘干净（见上面的注释）
                while (hNull.Flow.Steps.Remove(null!)) { }
            }

            Program.Check("DH3 集合里有 null 步骤时提示不抛（跳过空项取名）",
                !threw && nullText.Length > 0, nullText);

            // ---- 非法：Break 落在顶层 ----
            var breakTool = NewTool("BuiltIn_Break", "跳出循环");
            var illegal = canvas.DescribeDropTarget(breakTool, new Point(0, 5000));
            Program.Check("DH3 Break 落在顶层 → Illegal，且原因是可执行的那句（不是「不支持」）",
                illegal.Kind == DragHintKind.Illegal
                && illegal.Text == "不能放这里：" + StepFactory.LoopControlPlacementMessage,
                $"{illegal.Kind} / {illegal.Text}");

            // 反向：Break 落进 For 循环体内必须放行
            var h3 = new Harness();
            var forStep = h3.For("循环");
            h3.Add(forStep);
            var cv3 = h3.Canvas;
            var forFrame = h3.Node(forStep)!;
            var breakInside = cv3.DescribeDropTarget(breakTool,
                new Point(forFrame.Location.X + forFrame.LaneWidth / 2, forFrame.Location.Y + forFrame.LaneHeight / 2));
            Program.Check("DH3 反向：Break 落进 For 循环体内 → Valid（不是一律拦）",
                breakInside.Kind == DragHintKind.Valid,
                $"{breakInside.Kind} / {breakInside.Text}");

            // ---- 运行锁 → Locked ----
            Core.Events.GlobalEventBus.Publish(MainRunState.RunningOnce);
            try
            {
                var locked = canvas.DescribeDropTarget(tool, new Point(0, 5000));
                Program.Check("DH3 运行中 → Locked 且文案与运行锁同源",
                    locked.Kind == DragHintKind.Locked
                    && locked.Text == EditGate.RunLockedMessage,
                    $"{locked.Kind} / {locked.Text}");
            }
            finally
            {
                Core.Events.GlobalEventBus.Publish(MainRunState.NotStarted);
            }

            // ---- 没有挂流程 → 收起（不是"添加「X」→ 主流程"）----
            var bare = new FlowCanvasViewModel(new WorkspaceContext(), new StubPluginProvider());
            var none = bare.DescribeDropTarget(tool, new Point(0, 0));
            Program.Check("DH3 未挂流程 → Hidden 且无文案（不编一个假落点）",
                none.Kind == DragHintKind.Hidden && none.Text.Length == 0,
                $"{none.Kind} / '{none.Text}'");
        }

        // ==================================================================
        //  ④ XAML 接线（模板漏换一处 = 那一路拖拽永远没有提示）
        // ==================================================================
        private static void XamlWiring()
        {
            string root = @"d:\C#\VM";
            string toolView = System.IO.Path.Combine(root, "VisionMaster", "Views", "ToolView.xaml");
            string processView = System.IO.Path.Combine(root, "VisionMaster", "Views", "ProcessView.xaml");
            string stack = System.IO.Path.Combine(root, "VisionMaster", "Controls", "DragGhostStack.xaml");
            string pill = System.IO.Path.Combine(root, "VisionMaster", "Controls", "DragHintPill.xaml");
            string colors = System.IO.Path.Combine(root, "UI", "Controls", "Themes", "Colors.xaml");

            foreach (var f in new[] { toolView, processView, stack, pill, colors })
            {
                if (!System.IO.File.Exists(f))
                {
                    Program.Check("DH4 前提：文件存在 " + System.IO.Path.GetFileName(f), false, f);
                    return;
                }
            }

            string toolText = System.IO.File.ReadAllText(toolView);
            string processText = System.IO.File.ReadAllText(processView);
            string stackText = System.IO.File.ReadAllText(stack);
            string pillText = System.IO.File.ReadAllText(pill);
            string colorsText = System.IO.File.ReadAllText(colors);

            Program.Check("DH4 工具箱拖拽模板用 DragGhostStack（卡片 + 提示条组合）",
                Regex.IsMatch(toolText, @"<DataTemplate x:Key=""MyDragAdornerTemplate"">[\s\S]*?<controls:DragGhostStack")
                && !toolText.Contains("<controls:DragGhostCard"),
                "ToolView 的 MyDragAdornerTemplate 里应只剩 DragGhostStack");

            // 流程栏两个模板（步骤 / 分支卡片）都要换——按模板键精确匹配，不数总数
            bool stepTemplateOk = Regex.IsMatch(processText,
                @"<DataTemplate x:Key=""StepDragGhostTemplate"">[\s\S]*?<controls:DragGhostStack");
            bool branchTemplateOk = Regex.IsMatch(processText,
                @"<DataTemplate x:Key=""BranchDragGhostTemplate"">[\s\S]*?<controls:DragGhostStack");
            Program.Check("DH4 流程栏两个拖拽模板都换成 DragGhostStack（步骤 + 分支卡片，漏一个那一路没提示）",
                stepTemplateOk && branchTemplateOk && !processText.Contains("<controls:DragGhostCard"),
                $"步骤={stepTemplateOk} 分支={branchTemplateOk}");

            Program.Check("DH4 组合控件把提示条的 Text 与 Kind 都绑到 DragHintPresenter.Current（漏一个就只剩静态样式）",
                Regex.IsMatch(stackText, @"Kind=""\{Binding\s+Kind,\s*Source=\{x:Static\s+svc:DragHintPresenter\.Current\}\}""")
                && Regex.IsMatch(stackText, @"Text=""\{Binding\s+Text,\s*Source=\{x:Static\s+svc:DragHintPresenter\.Current\}\}""")
                && stackText.Contains("<controls:DragHintPill"),
                "DragGhostStack 里应绑定 Current 的 Text 与 Kind");

            // 四档状态 × (底色 + 文字色 + 图标色) = 12 个键，每个都要真被引用
            var hintTokens = new[]
            {
                "DragHintValidBackgroundBrush", "DragHintValidTextBrush", "DragHintValidIconBrush",
                "DragHintCautionBackgroundBrush", "DragHintCautionTextBrush", "DragHintCautionIconBrush",
                "DragHintIllegalBackgroundBrush", "DragHintIllegalTextBrush", "DragHintIllegalIconBrush",
                "DragHintLockedBackgroundBrush", "DragHintLockedTextBrush", "DragHintLockedIconBrush",
            };
            var missingInPill = hintTokens.Where(k => !pillText.Contains(k)).ToList();
            var missingInColors = hintTokens.Where(k => !colorsText.Contains(k)).ToList();
            Program.Check("DH4 提示条四档状态各接底色/文字色/图标色（缺一档就退回默认天蓝，非法/锁定看不出来）",
                missingInPill.Count == 0 && missingInColors.Count == 0,
                $"pill 缺={string.Join(",", missingInPill)} colors 缺={string.Join(",", missingInColors)}");

            Program.Check("DH4 提示条正文是 11.5 Medium + 单行省略（设计稿 §05）",
                pillText.Contains("FontSize=\"11.5\"")
                && pillText.Contains("FontWeight=\"Medium\"")
                && pillText.Contains("TextTrimming=\"CharacterEllipsis\""),
                "");

            Program.Check("DH4 提示条带单层软阴影（设计稿 §05 双层阴影的 WPF 近似，与 v2 卡片同约定）",
                pillText.Contains("DropShadowEffect") && pillText.Contains("DialogShadowColor"),
                "");

            Program.Check("DH4 新令牌都定义在 Colors.xaml（R140：第一方 XAML 不许写字面色）",
                hintTokens.All(k => colorsText.Contains(k)),
                "Colors.xaml 里应有 12 个 DragHint* 键");

            Program.Check("DH4 提示条 XAML 内没有字面 #RRGGBB（颜色只走令牌）",
                !Regex.IsMatch(pillText, @"#[0-9A-Fa-f]{6,8}\b"),
                "DragHintPill.xaml 里出现了硬编码颜色");

            // 拖拽开始时复位：非法落点松手时 gong 不再派发 Drop/DragLeave，那一路没有收尾回调，
            // 只能靠"下一轮拖拽开始"清掉残留（控件由 gong 在每次拖拽开始时实例化 → Loaded）
            string stackCode = System.IO.Path.Combine(root, "VisionMaster", "Controls", "DragGhostStack.xaml.cs");
            Program.Check("DH4 拖拽开始时复位提示（否则上一轮的落点文案会闪进下一轮）",
                System.IO.File.Exists(stackCode)
                && System.IO.File.ReadAllText(stackCode).Contains("DragHintPresenter.Reset()"),
                stackCode);
        }

        private static ToolItemModel NewTool(string typeName, string name = "落点桩") => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Icon = "\uE700",
            Category = "测试",
            ModuleTypeName = typeName,
            IsContainer = false,
        };
    }
}
