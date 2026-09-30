using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using Core.Interfaces;
using Newtonsoft.Json;
using VisionMaster;
using VisionMaster.Communications;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 流程画布（海康式一图全展开）的非 GUI 断言宿主。
    ///
    /// 为什么单独开一个工程：FlowCanvasViewModel 挂在 VisionMaster（WinExe）里，
    /// 而它的全部对外契约（Nodes / Links / 命令 / 计数）都是 public，
    /// 不需要为测试在产品程序里塞一个「自检开关」污染启动路径；
    /// 唯一的 internal 目标（FlowCompiler.CheckLinkOrder / PushUndo）用反射直调，
    /// 这与 CommTest 反射 HslHelper 的既有做法一致。
    ///
    /// 能这么跑的前提：FlowCanvasViewModel 及其依赖链不碰 Application.Current / Dispatcher，
    /// GlobalEventBus.Publish 也是同步 Invoke。所以控制台进程里 new 出来就是安全的。
    ///
    /// 画布 v2（扁平化）与旧断言的差异：
    ///   · 不再断言下钻/面包屑/层根——整棵树摊在同一张画布上，这些概念已删除；
    ///   · 连线断言从「端口级一根线」改为「模块级聚合线」（同一对模块的多条绑定合并）；
    ///   · 全局变量/常量绑定从「降级线」改为「节点角标」；
    ///   · 新增折叠/展开、容器框几何、解绑撤销、隐藏绑定计数等断言面。
    /// </summary>
    internal class Program
    {
        private static int _pass, _fail;

        private static int Main(string[] args)
        {
            // 离线工具模式：VMTOOL_VMSIF=<方案路径> → 向 MainTask 注入一个 If 容器（带条件表达式与空分支）后回写。
            // 用途：给 UI 实测准备"带逻辑分支"的图纸（画布容器框/泳道/折叠的实机验证素材），并兼验落盘往返链路。
            if (args.Length >= 2 && args[0] == "--inject-if")
            {
                InjectIfIntoMainTask(args[1]);
                return 0;
            }

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("========== 流程画布 v2（海康式扁平画布）断言（容器框/泳道/模块连线/折叠/联动/撤销栈） ==========");

            EmptyWorkspaceSafety();
            FlatRenderAndOrder();
            LaneAndFrameGeometry();
            CollapseAndExpand();
            ModuleLinkAggregation();
            LinkLegalityAndCounts();
            LinkGestureAndRejection();
            TopologyVersusCompiler();
            BranchDisplayNameNotification();
            NodePoolKeepsSelection();
            LayoutWriteBackDiscipline();
            OutsideSelectionDrivesCanvas();
            DragReorderAndVersion();
            CrossBranchMoveAndUndo();
            UnbindUndoRedo();
            UndoStackBoundary();
            RuntimeVariableModuleLinks();

            // 执行层断言（流程引擎优化 A1/A2/A3/B1/B2/B3）
            ExecutionChecks.IfInsideLoopExecutes();
            ExecutionChecks.WhileLoopRunsAndExits();
            ExecutionChecks.ContainerStatesReportedAndBlueprintLinked();
            ExecutionChecks.DuplicateStartIsIgnored();
            ExecutionChecks.RuntimeStateDoesNotInvalidateVersion();
            ExecutionChecks.RegistryLockDoesNotBlockCollection();
            ExecutionChecks.ForLoopCountReadsRuntimeVariable();
            ExecutionChecks.ConditionBranchFailureSemantics();
            ExecutionChecks.SessionLockAlwaysReturned();
            ExecutionChecks.CompileTimeSilentFailuresAreReported();
            ExecutionChecks.SessionStateTransitionSemantics();
            ExecutionChecks.ContractHygieneAndStepCollection();

            // OCR：字符分割 + 识别（真图 + 真模型）
            OcrChecks.Run();
            CodeReaderChecks.Run();
            YoloChecks.Run();
            BlobDetectChecks.Run();
            PortBindChecks.Run();
            GlobalVariableChecks.Run();
            MotionChecks.Run();
            MotionZMotionChecks.Run();
            MatchingChecks.Run();
            CreateRoiChecks.Run();

            // HTTP 收图端到端冒烟（真起服务端 + 真发 HTTP 图片）
            HttpImageSmoke.Run();

            // 线序颜色检测方案端到端（真加载 .vms + 真编译 + 真执行 + 断言线序）
            WireSequenceCheck.Run();

            // 变量绑定窗口：回显当前值 / 换端口不串值
            VariableBindingCheck.Run();

            // 变量赋值：运行时 / 全局两种作用域
            VariableAssignmentCheck.Run();

            // 颜色序列检查插件（第一刀）：ROI 采样区的画框 / 存盘 / 回填
            ColorCheckRoiCheck.Run();

            // 颜色序列检查插件（第二刀 / 第三刀）：自适应找线 → 配方判定 → 结果投射
            ColorCheckSequenceCheck.Run();

            // 区域颜色检查算子（第二个颜色算子，兼验颜色内核真的通用）
            ColorRegionCheck.Run();

            // 线序检测·插件版方案端到端（真加载 .vms + 真编译 + 真执行 + 断言结果与投射）
            WireSequencePluginCheck.Run();

            // 合成复杂颜色图生成器 + 流程式验证（采集 → 序列 → 区域×2，含 16 位高位深归一链路）
            ColorSyntheticChecks.Run();

            // 海康相机驱动：Modules 发现契约 + 缺 SDK 优雅降级 + 像素格式分类
            HikvisionChecks.Run();

            Finish();
            return Environment.ExitCode;
        }

        // ==================================================================
        //  [A] 空工作区构造安全（无方案、无流程时不得抛，也不得留下脏状态）
        // ==================================================================
        private static void EmptyWorkspaceSafety()
        {
            Section("[A] 空工作区与切换重建");

            var bare = new FlowCanvasViewModel(new WorkspaceContext(), new StubPluginProvider());
            Check("空 workspace 构造不抛", true, "");
            Check("空态：Nodes / Links 皆空", bare.Nodes.Count == 0 && bare.Links.Count == 0,
                $"Nodes={bare.Nodes.Count} Links={bare.Links.Count}");
            Check("空态：HasFlow=false 且提示可见", !bare.HasFlow && bare.EmptyHintVisibility == Visibility.Visible,
                $"EmptyHint={bare.EmptyHintVisibility}");
            Check("空态：两个计数与告警条归零",
                bare.DeferredLinkCount == 0 && bare.IllegalLinkCount == 0 && bare.HiddenLinkCount == 0
                && bare.WarningVisibility == Visibility.Collapsed,
                $"deferred={bare.DeferredLinkCount} illegal={bare.IllegalLinkCount} hidden={bare.HiddenLinkCount}");

            // 先建图纸后开画布：构造时应当直接反映当前流程
            var h = new Harness();
            var a = h.Leaf("A");
            h.Add(a);
            Check("未开画布前不应触碰 Layout（不生成坐标）", h.Flow.Layout.Count == 0, $"Layout.Count={h.Flow.Layout.Count}");

            var c = h.Canvas;
            Check("打开画布即呈现当前流程", c.HasFlow, $"Steps={h.Steps().Count}");
            Check("标题取流程名", c.FlowTitle == h.Flow.FlowName, $"'{c.FlowTitle}'");

            // 切流程 → 走 CurrentFlow 通知自动重建
            var other = new FlowModel { FlowName = "辅助流程" };
            h.Solution.Flows.Add(other);
            h.Workspace.SwitchFlow(other);
            Check("切流程后画布清空（订阅挂在 WorkspaceContext 上）",
                c.Nodes.Count == 0 && !c.HasFlow, $"Nodes={c.Nodes.Count}");
            h.Workspace.SwitchFlow(h.Flow);
            Check("切回原流程后节点回来", c.Nodes.Any(n => n.Kind == CanvasNodeKind.Step && n.StepId == a.StepID), "");
        }

        // ==================================================================
        //  [B] 扁平渲染：全深度一图 + 执行序号 + 容器身份
        // ==================================================================
        private static void FlatRenderAndOrder()
        {
            Section("[B] 扁平渲染与执行序号");

            var h = new Harness();
            var a = h.Leaf("A");
            var cond = h.If("判断");
            var ifBranch = cond.Children[0];
            var b1 = h.Leaf("B1");
            ifBranch.Steps.Add(b1);
            var outer = h.For("外层循环");
            var e1 = h.Leaf("E1");
            outer.Children[0].Steps.Add(e1);
            h.Add(a); h.Add(cond); h.Add(outer);

            var c = h.Canvas;
            Check("全深度步骤都上画布（顶层 + 分支内 = 3）", h.Steps().Count == 3, $"实际 {h.Steps().Count}");
            Check("容器渲染成框节点（If + For = 2）", h.Containers().Count == 2, $"实际 {h.Containers().Count}");
            Check("顶层步骤 = 只有 a（容器另算）",
                h.MainSteps().Count == 1 && h.MainSteps()[0].StepId == a.StepID, $"顶层 {h.MainSteps().Count}");

            Check("容器框带类型徽标：If / For",
                h.Node(cond)!.TypeBadge == "If" && h.Node(outer)!.TypeBadge == "For"
                && h.Node(a)!.TypeBadge == "",
                $"'{h.Node(cond)!.TypeBadge}'/'{h.Node(outer)!.TypeBadge}'");
            Check("泳道按引用挂住分支",
                h.Lane(ifBranch) != null && h.Lane(outer.Children[0]) != null, "");
            Check("有内容的分支不算空泳道", h.Lane(ifBranch)!.IsLaneEmpty == false, "");
            Check("If 分支条件为空时标题带『未绑定条件』",
                h.Lane(ifBranch)!.Header.Contains("未绑定条件"), $"'{h.Lane(ifBranch)!.Header}'");

            // 执行序号按所属集合下标 1 基：顶层 a=1, cond=2, outer=3；分支内独立从 1 起
            Check("顶层序号：a=1 / cond=2 / outer=3（容器参与执行序）",
                h.Node(a)!.OrderIndex == 1 && h.Node(cond)!.OrderIndex == 2 && h.Node(outer)!.OrderIndex == 3,
                $"{h.Node(a)!.OrderIndex},{h.Node(cond)!.OrderIndex},{h.Node(outer)!.OrderIndex}");
            Check("分支内步骤序号独立从 1 起",
                h.Node(b1)!.OrderIndex == 1 && h.Node(e1)!.OrderIndex == 1,
                $"{h.Node(b1)!.OrderIndex},{h.Node(e1)!.OrderIndex}");

            // ---- 执行顺序链：同集合相邻两步一条纵向顺序线（容器框参与，分支内部各自成链） ----
            var b2 = h.Leaf("B2");
            ifBranch.Steps.Add(b2);
            Check("顺序链：顶层 a→cond→outer 共 2 条 + 分支内 b1→b2 共 1 条",
                c.Links.Count(l => l.IsOrderLink) == 3
                && c.Links.Count(l => l.IsOrderLink && ReferenceEquals(l.Source, h.Node(a)) && ReferenceEquals(l.Target, h.Node(cond))) == 1
                && c.Links.Count(l => l.IsOrderLink && ReferenceEquals(l.Source, h.Node(cond)) && ReferenceEquals(l.Target, h.Node(outer))) == 1
                && c.Links.Count(l => l.IsOrderLink && ReferenceEquals(l.Source, h.Node(b1)) && ReferenceEquals(l.Target, h.Node(b2))) == 1,
                "顺序链 " + c.Links.Count(l => l.IsOrderLink) + " 条: "
                + string.Join(" | ", c.Links.Where(l => l.IsOrderLink).Select(l => l.Source.Header + "→" + l.Target.Header)));
            var firstChain = c.Links.First(l => l.IsOrderLink && ReferenceEquals(l.Source, h.Node(a)));
            Check("顺序链端点 = 上一步底缘中点 → 下一步顶缘中点（纵向流动）",
                Math.Abs(firstChain.SourceAnchor.X - (h.Node(a)!.Location.X + FlowCanvasViewModel.NodeWidth / 2)) < 0.01
                && Math.Abs(firstChain.SourceAnchor.Y - (h.Node(a)!.Location.Y + FlowCanvasViewModel.NodeHeight)) < 0.01
                && Math.Abs(firstChain.TargetAnchor.Y - h.Node(cond)!.Location.Y) < 0.01,
                $"src={firstChain.SourceAnchor} tgt={firstChain.TargetAnchor}");
            Check("顺序链悬停提示带执行次序", firstChain.Tooltip.Contains("执行顺序"), $"'{firstChain.Tooltip}'");
            Check("顺序链不计入非法/降级（它不是数据线）",
                c.IllegalLinkCount == 0 && c.DeferredLinkCount == 0, "");

            // 折叠后：容器内部顺序链消失，容器本身仍在顶层链上
            c.ToggleCollapseCommand.Execute(h.Node(cond)!);
            Check("折叠容器后其内部顺序链消失、容器仍在顶层链",
                c.Links.Count(l => l.IsOrderLink && ReferenceEquals(l.Source, h.Node(b1))) == 0
                && c.Links.Count(l => l.IsOrderLink && ReferenceEquals(l.Source, h.Node(a)) && ReferenceEquals(l.Target, h.Node(cond))) == 1,
                $"顺序链 {c.Links.Count(l => l.IsOrderLink)} 条");
            c.ToggleCollapseCommand.Execute(h.Node(cond)!);

            // Z 序：容器框垫在它的泳道与子孙底下
            var frameIndex = c.Nodes.IndexOf(h.Node(cond)!);
            var laneIndex = c.Nodes.IndexOf(h.Lane(ifBranch)!);
            var childIndex = c.Nodes.IndexOf(h.Node(b1)!);
            Check("Z 序：容器框 → 泳道 → 子孙（框垫底）",
                frameIndex >= 0 && frameIndex < laneIndex && laneIndex < childIndex,
                $"frame={frameIndex} lane={laneIndex} child={childIndex}");

            Check("禁用步骤标题带后缀（重建后刷新）",
                RenderDisabledSuffix(h), "");
        }

        private static bool TrackAnchorWhileMoving(Harness h, StepModel a, FlowCanvasViewModel c)
        {
            var link = c.Links.FirstOrDefault();
            if (link == null) return false;

            var orig = h.Node(a)!.Location;
            var before = link.SourceAnchor;

            h.Node(a)!.Location = new Point(orig.X + 55, orig.Y + 40);
            var after = link.SourceAnchor;
            bool followed = Math.Abs(after.X - (orig.X + 55 + FlowCanvasViewModel.NodeWidth)) < 0.01
                && Math.Abs(after.Y - (orig.Y + 40 + FlowCanvasViewModel.NodeHeight / 2)) < 0.01
                && after != before;

            h.Node(a)!.Location = orig;
            bool restored = link.SourceAnchor == new Point(orig.X + FlowCanvasViewModel.NodeWidth, orig.Y + FlowCanvasViewModel.NodeHeight / 2);

            return followed && restored;
        }

        private static bool RenderDisabledSuffix(Harness h)
        {
            var a = h.Flow.Steps[0];
            a.IsDisEnable = true;
            h.Canvas.RebuildInPlace();
            var ok = (h.Node(a)!.Header ?? "").Contains("已禁用");
            a.IsDisEnable = false;
            h.Canvas.RebuildInPlace();
            return ok;
        }

        // ==================================================================
        //  [C] 泳道与容器框几何：包住内容、不入坐标库
        // ==================================================================
        private static void LaneAndFrameGeometry()
        {
            Section("[C] 泳道与容器框几何");

            var h = new Harness();
            var a = h.Leaf("A");
            var cond = h.If("判断");
            var ifBranch = cond.Children[0];
            var elseBranch = cond.Children[1];
            var b1 = h.Leaf("B1");
            ifBranch.Steps.Add(b1);
            h.Add(a); h.Add(cond);

            var c = h.Canvas;
            var frame = h.Node(cond)!;
            var lane = h.Lane(ifBranch)!;

            Check("框体尺寸为正", frame.LaneWidth > 0 && frame.LaneHeight > 0,
                $"{frame.LaneWidth:0}x{frame.LaneHeight:0}");
            Check("框体包住分支内步骤",
                frame.Location.X <= h.Node(b1)!.Location.X && frame.Location.Y <= h.Node(b1)!.Location.Y
                && frame.Location.X + frame.LaneWidth >= h.Node(b1)!.Location.X,
                $"frame=({frame.Location.X:0},{frame.Location.Y:0}) child=({h.Node(b1)!.Location.X:0},{h.Node(b1)!.Location.Y:0})");
            Check("空分支泳道给占位提示", h.Lane(elseBranch)!.IsLaneEmpty, "");
            Check("泳道外接框为正尺寸",
                lane.LaneWidth > 0 && lane.LaneHeight > 0, $"{lane.LaneWidth:0}x{lane.LaneHeight:0}");

            Check("泳道不可拖 / 不可选 / 属装饰",
                h.Lanes().All(l => !l.IsDraggable && !l.IsSelectable && l.IsDecorator), "");
            Check("泳道 StepId 为空 Guid（绝不写进坐标库）",
                h.Lanes().All(l => l.StepId == Guid.Empty), "");
            Check("容器框不可拖（位置由内容算出）但可选中（联动属性栏）",
                !frame.IsDraggable && frame.IsSelectable && !frame.IsDecorator, "");

            // 装饰节点与容器框的位置都不入库
            var before = h.Flow.Layout.Count;
            lane.Location = new Point(-7000, -7000);
            frame.Location = new Point(7000, 7000);
            Check("挪动泳道/容器框不新增、不污染坐标项",
                h.Flow.Layout.Count == before && h.Flow.Layout.Find(cond.StepID) != null
                && h.Flow.Layout.Find(cond.StepID)!.X != 7000,
                $"Layout.Count={h.Flow.Layout.Count}");

            // 真实步骤（含分支内）的坐标入库
            var b1Node = h.Node(b1)!;
            // 布局预留：容器分支内容之下才排后续兄弟（旧实现会把兄弟叠进容器框——
            // "展开逻辑分支后层级顺序乱了"的元凶）
            var tail = h.Leaf("尾部");
            h.Add(tail);
            Check("AutoLayout 预留容器分支高度：后续兄弟排在分支内容之下",
                Math.Abs(h.Node(tail)!.Location.Y - (h.Node(b1)!.Location.Y + 90)) < 0.01
                && h.Node(tail)!.Location.X == h.Node(a)!.Location.X,
                $"tail=({h.Node(tail)!.Location.X:0},{h.Node(tail)!.Location.Y:0}) b1=({h.Node(b1)!.Location.X:0},{h.Node(b1)!.Location.Y:0})");

            b1Node.Location = new Point(999, 888);
            Check("分支内步骤拖动写回坐标库",
                h.Flow.Layout.Find(b1.StepID) != null
                && Math.Abs(h.Flow.Layout.Find(b1.StepID)!.X - 999) < 0.01,
                $"X={h.Flow.Layout.Find(b1.StepID)?.X}");
        }

        // ==================================================================
        //  [D] 折叠与展开：子孙隐藏、角标计数、持久化、连线隐藏计数
        // ==================================================================
        private static void CollapseAndExpand()
        {
            Section("[D] 折叠与展开");

            var h = new Harness();
            var a = h.Leaf("A");
            var cond = h.If("判断");
            var b1 = h.Leaf("B1");
            var inner = h.For("内层");
            var deep = h.Leaf("深层");
            cond.Children[0].Steps.Add(b1);
            cond.Children[0].Steps.Add(inner);
            inner.Children[0].Steps.Add(deep);
            h.Add(a); h.Add(cond);

            var tail = h.Leaf("尾部");
            Harness.WithInput(tail, "In");
            h.Add(tail);
            Harness.RawLink(tail, "In", deep, "Out");

            var c = h.Canvas;
            var frame = h.Node(cond)!;
            Check("展开态：深层步骤可见且连线画到深层",
                h.Node(deep) != null && h.Link(deep, tail) != null, "");

            c.ToggleCollapseCommand.Execute(frame);
            Check("折叠后框标记翻转并落库",
                frame.IsCollapsed && h.Flow.Layout.Find(cond.StepID)!.Collapsed, "");
            Check("折叠后子孙节点全部摘下（含嵌套）",
                h.Node(b1) == null && h.Node(inner) == null && h.Node(deep) == null
                && h.Steps().Count == 2, $"步骤 {h.Steps().Count}");
            Check("折叠角标统计含嵌套子孙（b1+inner+deep = 3）",
                frame.CollapsedChildCount == 3 && frame.CollapsedBadgeText == "3 步",
                $"'{frame.CollapsedBadgeText}'");
            Check("折叠后框体尺寸回落到标称盒",
                frame.LaneWidth <= FlowCanvasViewModel.CollapsedFrameWidth + 1
                && frame.LaneHeight <= FlowCanvasViewModel.CollapsedFrameHeight + 1,
                $"{frame.LaneWidth:0}x{frame.LaneHeight:0}");
            Check("通往折叠子树的连线隐藏并计数（不告警、不删图纸）",
                c.HiddenLinkCount == 1 && c.DataLinkCount == 0
                && c.WarningVisibility == Visibility.Collapsed
                && deep.LinkedSources.ContainsKey("Out") == false
                && tail.LinkedSources.ContainsKey("In"),
                $"hidden={c.HiddenLinkCount}");

            c.ToggleCollapseCommand.Execute(frame);
            Check("再点展开：子孙回来、连线重画",
                !frame.IsCollapsed && h.Node(deep) != null && h.Link(deep, tail) != null, "");

            // 折叠态跨重建持久化（Layout.Collapsed 随图纸存盘）
            c.ToggleCollapseCommand.Execute(frame);
            var other = new FlowModel { FlowName = "其他流程" };
            h.Solution.Flows.Add(other);
            h.Workspace.SwitchFlow(other);
            h.Workspace.SwitchFlow(h.Flow);
            Check("切流程往返后折叠态保持（子孙仍隐藏）",
                h.Node(b1) == null && h.Flow.Layout.Find(cond.StepID)!.Collapsed, "");
            Check("折叠后外部选中隐藏步骤给出提示（不炸、给指引）",
                SelectHiddenAndCheckHint(h, deep), "");
            c.ToggleCollapseCommand.Execute(h.Node(cond)!);
            Check("再点展开后深层步骤回来", h.Node(deep) != null, "");
        }

        private static bool SelectHiddenAndCheckHint(Harness h, StepModel hidden)
        {
            h.Workspace.SwitchStep(hidden);
            var hint = h.Canvas.StatusHint ?? "";
            h.Workspace.SwitchStep(null);
            return hint.Contains("折叠");
        }

        // ==================================================================
        //  [E] 模块级连线聚合：一对多绑定合一根线；全局/常量折角标；野 Id 计降级
        // ==================================================================
        private static void ModuleLinkAggregation()
        {
            Section("[E] 模块连线聚合");

            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            Harness.WithInput(b, "In");
            h.Add(a); h.Add(b);
            Harness.RawLink(b, "In", a, "Out");
            var c = h.Canvas;

            Check("单绑定：一根线、无 ×N 角标",
                c.DataLinkCount == 1 && c.Links.First(l => !l.IsOrderLink).Bindings.Count == 1 && c.Links.First(l => !l.IsOrderLink).BindingCountLabel == "", "");
            Check("线的两端 = 生产方/消费方节点",
                ReferenceEquals(c.Links[0].Source, h.Node(a)) && ReferenceEquals(c.Links[0].Target, h.Node(b)), "");
            Check("锚点 = 盒子边缘中点（源右缘 / 目标左缘，纯数学不依赖控件回报）",
                c.Links[0].SourceAnchor == new Point(h.Node(a)!.Location.X + FlowCanvasViewModel.NodeWidth, h.Node(a)!.Location.Y + FlowCanvasViewModel.NodeHeight / 2)
                && c.Links[0].TargetAnchor == new Point(h.Node(b)!.Location.X, h.Node(b)!.Location.Y + FlowCanvasViewModel.NodeHeight / 2),
                $"src={c.Links[0].SourceAnchor} tgt={c.Links[0].TargetAnchor}");
            Check("节点挪动后锚点实时跟随（连线跟随拖拽的机制）",
                TrackAnchorWhileMoving(h, a, c), "");

            // 同一对模块的第二条绑定 → 聚合进同一根线
            b.SetInputValue("In2", 0d);
            Harness.RawLink(b, "In2", a, "Out");
            c.RebuildInPlace();   // LinkedSources 是属性写回，画布只订阅集合增删 → 手动重算
            Check("同对模块第二条绑定聚合进同一根线（×2）",
                c.DataLinkCount == 1 && c.Links.First(l => !l.IsOrderLink).Bindings.Count == 2 && c.Links.First(l => !l.IsOrderLink).BindingCountLabel == "×2",
                $"{c.DataLinkCount}/{c.Links.First(l => !l.IsOrderLink).Bindings.Count}");
            Check("悬停提示逐条列出绑定",
                c.Links[0].Tooltip.Contains("In ←") && c.Links[0].Tooltip.Contains("In2 ←"),
                $"'{c.Links[0].Tooltip.Replace("\n", "|")}'");

            // 第二个消费方 → 独立一根线
            var d = h.Leaf("D");
            Harness.WithInput(d, "In");
            h.Add(d);
            Harness.RawLink(d, "In", a, "Out");
            c.RebuildInPlace();
            Check("不同消费方各一根线", c.DataLinkCount == 2 && h.Link(a, d) != null, $"实际 {c.DataLinkCount}");

            // 全局变量 / 常量：不画线，折成消费方角标
            var q = h.Leaf("Q");
            Harness.WithInput(q, "In");
            h.Add(q);
            q.LinkedSources["In"] = new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "GV", "全局.gv");
            var r = h.Leaf("R");
            Harness.WithInput(r, "In");
            h.Add(r);
            r.LinkedSources["In"] = new LinkReference(LinkKind.Constant, Guid.Empty, "5", "常量 5");
            c.RebuildInPlace();
            Check("全局变量/常量绑定不画线、不计非法与降级",
                c.DataLinkCount == 2 && c.IllegalLinkCount == 0 && c.DeferredLinkCount == 0,
                $"links={c.Links.Count}");
            Check("全局变量折成角标（悬停带变量名）",
                h.Node(q)!.ExternalLinkCount == 1 && h.Node(q)!.ExternalLinkTooltip.Contains("全局变量 GV"),
                $"'{h.Node(q)!.ExternalLinkTooltip.Replace("\n", "|")}'");
            Check("常量折成角标", h.Node(r)!.ExternalLinkCount == 1 && h.Node(r)!.ExternalLinkTooltip.Contains("常量 5"), "");

            // 野 Id（上游已删除）→ 降级计数，不画线不标红
            var h2 = new Harness();
            var y = h2.Leaf("Y");
            Harness.WithInput(y, "In");
            h2.Add(y);
            y.SetLink("In", new LinkReference(LinkKind.StepPort, Guid.NewGuid(), "Out", "幽灵.Out"));
            var c2 = h2.Canvas;
            Check("上游已删 → 降级计数（编译会报致命断连）",
                c2.DeferredLinkCount == 1 && c2.Links.Count == 0 && c2.IllegalLinkCount == 0
                && c2.WarningVisibility == Visibility.Collapsed,
                $"deferred={c2.DeferredLinkCount}");
        }

        // ==================================================================
        //  [F] 连线合法性：倒序 / 跨分支 / 容器后 → 红虚线；祖先取数放行
        // ==================================================================
        private static void LinkLegalityAndCounts()
        {
            Section("[F] 连线合法性");

            // F1 改序把合法线变非法：自动重算 + 红虚线 + 告警条
            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            Harness.WithInput(b, "In");
            h.Add(a); h.Add(b);
            Harness.RawLink(b, "In", a, "Out");
            var c = h.Canvas;
            Check("合法线：0 告警", c.IllegalLinkCount == 0 && !c.Links[0].IsIllegal, "");

            h.Flow.Steps.Move(0, 1);
            Check("改序后自动重算并标记非法",
                c.IllegalLinkCount == 1 && c.WarningVisibility == Visibility.Visible, $"illegal={c.IllegalLinkCount}");
            Check("非法线带可读原因",
                c.Links.Single(l => !l.IsOrderLink).IsIllegal && c.Links.Single(l => !l.IsOrderLink).WarningText!.Contains("执行顺序倒序"),
                $"'{c.Links.Single(l => !l.IsOrderLink).WarningText}'");
            Check("告警条文案同时报非法与降级数量",
                c.WarningText.Contains("1 条连线结构非法"), $"'{c.WarningText}'");
            Check("画布不擅自删图纸引用", b.LinkedSources["In"].TargetStepId == a.StepID, "");

            // F2 跨分支（两端都可见 → 直接标红）
            var h4 = new Harness();
            var cond = h4.If("判断");
            var inIf = h4.Leaf("If内");
            cond.Children[0].Steps.Add(inIf);
            var inElse = h4.Leaf("Else内");
            Harness.WithInput(inElse, "In");
            cond.Children[1].Steps.Add(inElse);
            Harness.RawLink(inElse, "In", inIf, "Out");
            h4.Add(cond);
            var c4 = h4.Canvas;
            Check("跨分支连线标红且 Classify 判成跨分支",
                c4.IllegalLinkCount == 1 && c4.Links.Single(l => !l.IsOrderLink).IsIllegal
                && h4.Topology().Classify(inIf.StepID, inElse.StepID) == LinkLegality.CrossBranch, "");

            // F3 循环体取 For.Index：祖先取数放行（历史误判重灾区）
            var h5 = new Harness();
            var outer = h5.For("循环");
            var bodyStep = h5.Leaf("体内");
            Harness.WithInput(bodyStep, "In");
            outer.Children[0].Steps.Add(bodyStep);
            h5.Add(outer);
            Harness.RawLink(bodyStep, "In", outer, "Index");
            var c5 = h5.Canvas;
            c5.RebuildInPlace();
            Check("循环体取 For.Index 合法（ProducerIsAncestor）",
                c5.IllegalLinkCount == 0 && h5.Link(outer, bodyStep) != null && !h5.Link(outer, bodyStep)!.IsIllegal,
                $"illegal={c5.IllegalLinkCount} links={c5.Links.Count}");

            // F4 生产方排在容器之后 → 标红
            var h2 = new Harness();
            var roots2 = Harness.OuterBeforeInner(producerBefore: false,
                out var container2, out var consumer2, out var producer2);
            h2.Flow.Steps.Clear();
            foreach (var s in roots2) h2.Flow.Steps.Add(s);
            var c2 = h2.Canvas;
            Check("生产方排在包住消费方的容器之后 → 标红计 1",
                c2.IllegalLinkCount == 1 && c2.Links.Single(l => !l.IsOrderLink).IsIllegal, $"illegal={c2.IllegalLinkCount}");
            Check("非法文案含『生产方排在包住消费方的容器之后』",
                c2.Links.Single(l => !l.IsOrderLink).WarningText!.Contains("包住消费方的容器之后"),
                $"'{c2.Links.Single(l => !l.IsOrderLink).WarningText}'");
        }

        // ==================================================================
        //  [G] 建线手势：结构合法才发请求；端口对交绑定弹窗（画布不写连线）
        // ==================================================================
        private static void LinkGestureAndRejection()
        {
            Section("[G] 建线手势与拒绝");

            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            h.Add(a); h.Add(b);
            var c = h.Canvas;

            // 顺向拖线：结构合法 → 发建线请求（消费方回传），图纸零写入
            var requested = c.RequestLink(h.Node(a)!, h.Node(b)!);
            Check("顺向拖线发出建线请求且带回消费方",
                ReferenceEquals(requested, b), $"'{requested?.StepName}'");
            Check("画布自身不写连线（端口对归绑定弹窗，顺序链除外）",
                b.LinkedSources.Count == 0 && c.DataLinkCount == 0, "");

            // 反向起拖（从输入脚拖向生产方）也要归一成同一条请求
            var requested2 = c.RequestLinkReverse(h.Node(a)!, h.Node(b)!);
            Check("反向起拖归一后同样请求消费方", ReferenceEquals(requested2, b), "");

            // 倒序 / 自连 / 泳道：拒绝并给出原因
            var tail = h.Leaf("尾部");
            h.Add(tail);
            var rejected = c.RequestLink(h.Node(tail)!, h.Node(a)!);
            Check("倒序拖线被拒并给出原因",
                rejected == null && (c.StatusHint ?? "").Contains("执行顺序倒序"), $"'{c.StatusHint}'");
            Check("自连被拒", c.RequestLink(h.Node(a)!, h.Node(a)!) == null, "");
            Check("拒绝后提示可见（状态栏）", c.StatusHint != null, "");
        }

        // ==================================================================
        //  [H] FlowTopology.Classify 与编译期 CheckLinkOrder 必须同进退
        // ==================================================================
        private static void TopologyVersusCompiler()
        {
            Section("[H] 拓扑判定 ⇄ 编译校验一致性");

            var method = typeof(FlowCompiler).GetMethod("CheckLinkOrder", BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null)
            {
                Check("反射找到 FlowCompiler.CheckLinkOrder", false, "internal 方法改名或被删除");
                return;
            }
            Check("反射找到 FlowCompiler.CheckLinkOrder", true, "");

            void Assert(string label, List<StepModel> roots,
                        Guid producerId, Guid consumerId,
                        LinkLegality expect, bool expectError, string expectTag)
            {
                var topo = FlowTopology.Build(roots);
                var actual = topo.Classify(producerId, consumerId);
                var errors = new List<CompilationError>();
                method!.Invoke(null, new object[] { roots, errors });

                var ok = actual == expect
                         && (expectError ? errors.Count == 1 && errors[0].Message.Contains(expectTag) : errors.Count == 0);
                Check(label, ok, $"{actual} / 错误 {errors.Count} 条{(!ok && errors.Count > 0 ? " → " + errors[0].Message : "")}");
                if (expectError && errors.Count > 0)
                {
                    Check($"  └ 错误定位到消费方",
                        errors[0].StepId != null && errors[0].StepName != null,
                        $"{errors[0].StepName}");
                }
            }

            // 1. 同层前→后：合法
            {
                var p = new ActionStep("", "生产", StubPluginProvider.LeafPlugin, "P");
                p.SetInputValue("In", 0d);
                var c1 = new ActionStep("", "消费", StubPluginProvider.LeafPlugin, "C1");
                c1.SetInputValue("In", 0d);
                Harness.RawLink(c1, "In", p, "Out");
                var roots = new List<StepModel> { p, c1 };
                Assert("同层前→后合法", roots, p.StepID, c1.StepID,
                    LinkLegality.SameListBefore, false, "");
            }

            // 2. 外层排前 → 内层消费（SameListBefore 复用）
            {
                var roots = Harness.OuterBeforeInner(producerBefore: true,
                    out var container, out var consumer, out var producer);
                Assert("外层排前 → 内层消费（SameListBefore）",
                    roots, producer.StepID, consumer.StepID,
                    LinkLegality.SameListBefore, false, "");
            }

            // 3. 同层后→前（倒序）：致命错
            {
                var p = new ActionStep("", "生产", StubPluginProvider.LeafPlugin, "P");
                p.SetInputValue("In", 0d);
                var c1 = new ActionStep("", "消费", StubPluginProvider.LeafPlugin, "C1");
                c1.SetInputValue("In", 0d);
                Harness.RawLink(c1, "In", p, "Out");
                var roots = new List<StepModel> { c1, p };
                Assert("同层倒序 → 致命错",
                    roots, p.StepID, c1.StepID,
                    LinkLegality.SameListReversed, true, "依赖倒序");
            }

            // 4. 外层排后 → 内层消费（ProducerAfterEnclosingContainer）：致命错
            {
                var roots = Harness.OuterBeforeInner(producerBefore: false,
                    out var container, out var consumer, out var producer);
                Assert("外层排后 → ProducerAfterEnclosingContainer → 致命错",
                    roots, producer.StepID, consumer.StepID,
                    LinkLegality.ProducerAfterEnclosingContainer, true, "依赖倒序");
            }

            // 5. 跨分支：致命错
            {
                var cond = new ConditionStep("", "If", StubPluginProvider.IfPlugin, "判断");
                var inIf = new ActionStep("", "If内", StubPluginProvider.LeafPlugin, "If内");
                inIf.SetInputValue("In", 0d);
                var inElse = new ActionStep("", "Else内", StubPluginProvider.LeafPlugin, "Else内");
                inElse.SetInputValue("In", 0d);
                cond.Children[0].Steps.Add(inIf);
                cond.Children[1].Steps.Add(inElse);
                Harness.RawLink(inElse, "In", inIf, "Out");
                var roots = new List<StepModel> { cond };
                Assert("跨分支 → 致命错",
                    roots, inIf.StepID, inElse.StepID,
                    LinkLegality.CrossBranch, true, "跨分支取数");
            }

            // 6. 自连（SameListReversed，文案含"引用了自身的输出"）
            {
                var s = new ActionStep("", "自连", StubPluginProvider.LeafPlugin, "S");
                s.SetInputValue("In", 0d);
                Harness.RawLink(s, "In", s, "Out");
                var roots = new List<StepModel> { s };
                Assert("自连 → 致命错且文案含『引用了自身的输出』",
                    roots, s.StepID, s.StepID,
                    LinkLegality.SameListReversed, true, "引用了自身的输出");
            }

            // 7. For.Index → 循环体内消费（ProducerIsAncestor，合法）
            {
                var outer = new ForStep("", "For", StubPluginProvider.ForPlugin, "循环");
                var body = outer.Children[0];
                var inner = new ActionStep("", "体内", StubPluginProvider.LeafPlugin, "体内");
                inner.SetInputValue("In", 0d);
                body.Steps.Add(inner);
                var roots = new List<StepModel> { outer };
                Assert("For.Index → 循环体（ProducerIsAncestor）合法",
                    roots, outer.StepID, inner.StepID,
                    LinkLegality.ProducerIsAncestor, false, "");
            }

            // 8. 野 TargetStepId（Unknown）：不报错（LinkPorts 已报致命断连）
            {
                var ghost = Guid.NewGuid();
                var c1 = new ActionStep("", "消费", StubPluginProvider.LeafPlugin, "C1");
                c1.SetInputValue("In", 0d);
                c1.SetLink("In", new LinkReference(LinkKind.StepPort, ghost, "Out", "幽灵.Out"));
                var roots = new List<StepModel> { c1 };
                var topo = FlowTopology.Build(roots);
                var actual = topo.Classify(ghost, c1.StepID);
                var errors = new List<CompilationError>();
                method!.Invoke(null, new object[] { roots, errors });
                Check("野 Id → Classify=Unknown 且不报倒序错",
                    actual == LinkLegality.Unknown && errors.Count == 0,
                    $"{actual} / 错误 {errors.Count} 条");
            }

            // 9. 跳过：禁用消费步骤
            {
                var p = new ActionStep("", "生产", StubPluginProvider.LeafPlugin, "P");
                p.SetInputValue("In", 0d);
                var c1 = new ActionStep("", "消费", StubPluginProvider.LeafPlugin, "C1");
                c1.SetInputValue("In", 0d);
                c1.IsDisEnable = true;
                Harness.RawLink(c1, "In", p, "Out");
                var roots = new List<StepModel> { c1, p };
                var errors = new List<CompilationError>();
                method!.Invoke(null, new object[] { roots, errors });
                Check("禁用消费步骤 → 跳过倒序检查",
                    errors.Count == 0, $"错误 {errors.Count} 条");
            }

            // 10. 跳过：非 StepPort 来源
            {
                var c1 = new ActionStep("", "消费", StubPluginProvider.LeafPlugin, "C1");
                c1.SetInputValue("In", 0d);
                c1.SetLink("In", new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "GV", "全局.gv"));
                var roots = new List<StepModel> { c1 };
                var errors = new List<CompilationError>();
                method!.Invoke(null, new object[] { roots, errors });
                Check("非 StepPort 来源 → 跳过",
                    errors.Count == 0, $"错误 {errors.Count} 条");
            }

            // 11. 跳过：禁用生产方（且倒序时仍不报）
            {
                var p = new ActionStep("", "生产", StubPluginProvider.LeafPlugin, "P");
                p.SetInputValue("In", 0d);
                p.IsDisEnable = true;
                var c1 = new ActionStep("", "消费", StubPluginProvider.LeafPlugin, "C1");
                c1.SetInputValue("In", 0d);
                Harness.RawLink(c1, "In", p, "Out");
                var roots = new List<StepModel> { c1, p };
                var errors = new List<CompilationError>();
                method!.Invoke(null, new object[] { roots, errors });
                Check("禁用生产方 → 跳过倒序检查",
                    errors.Count == 0, $"错误 {errors.Count} 条");
            }
        }

        // ==================================================================
        //  [I] 分支改名后泳道标题跟着走
        // ==================================================================
        private static void BranchDisplayNameNotification()
        {
            Section("[I] 分支改名与显示名通知");

            var branch = new StepCollection { BranchType = BranchType.If, StepName = "If", Expression = "" };
            var raised = new List<string?>();
            ((INotifyPropertyChanged)branch).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            branch.StepName = "命中";
            Check("改 StepName 补发 DisplayName 通知",
                raised.Contains(nameof(StepCollection.StepName)) && raised.Contains(nameof(StepCollection.DisplayName)),
                string.Join(",", raised.Where(x => x != null)));
            Check("未绑定条件时显示名带占位提示", branch.DisplayName.Contains("未绑定条件"), $"'{branch.DisplayName}'");

            raised.Clear();
            branch.Expression = "Score > 80";
            Check("补上条件后显示名带表达式",
                raised.Contains(nameof(StepCollection.DisplayName)) && branch.DisplayName == "命中 [ Score > 80 ]",
                $"'{branch.DisplayName}'");

            raised.Clear();
            branch.BranchType = BranchType.Else;
            Check("改成 Else 后不再要求条件",
                !branch.RequiresExpression && branch.DisplayName == "命中" && raised.Contains(nameof(StepCollection.DisplayName)),
                $"'{branch.DisplayName}'");

            // 画布泳道标题回读 Branch.DisplayName → 改名后即时生效
            var h = new Harness();
            var cond = h.If("判断");
            h.Add(cond);
            var lane = h.Lane(cond.Children[0])!;
            var live = lane.Header;
            cond.Children[0].StepName = "成功分支";
            Check("画布泳道标题随分支改名刷新", lane.Header != live && lane.Header.Contains("成功分支"), $"'{lane.Header}'");
        }

        // ==================================================================
        //  [J] 节点池：重画不丢选中；删步骤摘节点
        // ==================================================================
        private static void NodePoolKeepsSelection()
        {
            Section("[J] 增量重建与选中保持");

            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            h.Add(a); h.Add(b);
            var c = h.Canvas;

            var nodeA = h.Node(a)!;
            nodeA.IsSelected = true;
            Check("置选中生效", h.Node(a)!.IsSelected, "");

            h.Add(h.Leaf("C"));
            Check("集合变更后仍复用同一 VM 实例", ReferenceEquals(h.Node(a), nodeA), "");
            Check("复用后选中态未蒸发", nodeA.IsSelected, "");
            Check("重画后步骤节点数递增",
                c.Nodes.Count(n => n.Kind == CanvasNodeKind.Step) == 3,
                $"{c.Nodes.Count(n => n.Kind == CanvasNodeKind.Step)} 个步骤节点");

            h.Flow.Steps.Remove(b);
            Check("删除步骤后画布摘掉节点",
                h.Node(b) == null && h.MainSteps().Count == 2, $"{h.MainSteps().Count}");
            Check("被删步骤的坐标同步回收", h.Flow.Layout.Find(b.StepID) == null, "");
            Check("存活节点仍保持选中", h.Node(a)!.IsSelected, "");

            // 上游被删：聚合连线随之消失并计入降级
            var h2 = new Harness();
            var p = h2.Leaf("P");
            var q = h2.Leaf("Q");
            Harness.WithInput(q, "In");
            h2.Add(p); h2.Add(q);
            Harness.RawLink(q, "In", p, "Out");
            var c2 = h2.Canvas;
            Check("连线画出到上游", c2.DataLinkCount == 1, $"{c2.Links.Count}");
            h2.Flow.Steps.Remove(p);
            Check("上游删除后连线随重画消失并计入降级",
                c2.DataLinkCount == 0 && c2.DeferredLinkCount == 1, $"deferred={c2.DeferredLinkCount}");
            Check("连接器连接态在重画时统一复位",
                c2.Nodes.SelectMany(n => n.Inputs.Concat(n.Outputs))
                    .All(port => !port.IsConnected), "");
        }

        // ==================================================================
        //  [K] 坐标写回纪律：只有步骤节点入库
        // ==================================================================
        private static void LayoutWriteBackDiscipline()
        {
            Section("[K] 坐标写回");

            var h = new Harness();
            var cond = h.If("判断");
            var innerFor = h.For("内层循环");
            innerFor.Children[0].Steps.Add(h.Leaf("B1"));
            cond.Children[0].Steps.Add(innerFor);
            var a = h.Leaf("A");
            h.Add(cond); h.Add(a);
            var c = h.Canvas;

            Check("首帧自动布局补齐缺失坐标（含嵌套）", h.Flow.Layout.Count >= 4, $"Layout.Count={h.Flow.Layout.Count}");
            var nodeA = h.Node(a)!;
            Check("自动布局结果已回填节点", h.Flow.Layout.TryGet(a.StepID, out var stored) && nodeA.Location.X == stored!.X, "");

            nodeA.Location = new Point(1234, 567);
            Check("拖动步骤节点写回坐标库",
                h.Flow.Layout.Find(a.StepID)!.X == 1234 && h.Flow.Layout.Find(a.StepID)!.Y == 567,
                $"({h.Flow.Layout.Find(a.StepID)!.X:0}, {h.Flow.Layout.Find(a.StepID)!.Y:0})");

            var versionBefore = h.Flow.Version;
            nodeA.Location = new Point(2000, 200);
            Check("挪坐标不递增图纸版本（不触发重编译）", h.Flow.Version == versionBefore, $"{versionBefore}");

            // 容器框的位置是算出来的：程序性改动不得入库
            var frame = h.Node(cond)!;
            var frameStoredX = h.Flow.Layout.Find(cond.StepID)!.X;
            frame.Location = new Point(424242, 424242);
            Check("挪动容器框不污染坐标库",
                h.Flow.Layout.Find(cond.StepID)!.X == frameStoredX, $"{frameStoredX:0}");
            c.RebuildInPlace();
            Check("重画后框体位置回到内容外接框",
                frame.Location.X != 424242, $"frame.X={frame.Location.X:0}");
        }

        // ==================================================================
        //  [L] 外部选中 → 画布选中并滚入视野（扁平画布无层级可切）
        // ==================================================================
        private static void OutsideSelectionDrivesCanvas()
        {
            Section("[L] 外部选中联动");

            var h = new Harness();
            var a = h.Leaf("A");
            var cond = h.If("判断");
            var deep = h.Leaf("深层");
            cond.Children[0].Steps.Add(deep);
            h.Add(a); h.Add(cond);
            var c = h.Canvas;

            h.Workspace.SwitchStep(deep);
            Check("外部选中深层步骤 → 画布对应节点被选中", h.Node(deep)!.IsSelected, "");

            h.Workspace.SwitchStep(a);
            Check("外部选中顶层步骤 → 选中跟着走", h.Node(a)!.IsSelected, "");

            // 画布点选 → 工作区当前步骤（流程栏/属性栏跟随）
            c.Nodes.First(n => n.Model == deep).IsSelected = true;
            Check("画布点选回写工作区当前步骤", ReferenceEquals(h.Workspace.CurrentStep, deep),
                $"'{h.Workspace.CurrentStep?.StepName}'");

            var ghost = new ActionStep("", "幽灵", StubPluginProvider.LeafPlugin, "幽灵") as StepModel;
            var thrown = false;
            try { h.Workspace.SwitchStep(ghost!); }
            catch (InvalidOperationException) { thrown = true; }
            Check("选中不在图纸里的步骤由 WorkspaceContext 抛错（画布不参与兜底）", thrown, "");
            Check("抛错后画布选中未被破坏", h.Node(a)!.IsSelected || h.Node(deep)!.IsSelected, "");

            // 步骤改名不重画（只订阅集合增删）
            var nodeCount = c.Nodes.Count;
            deep.StepName = "改名后的深层";
            Check("改名不触发重建", c.Nodes.Count == nodeCount && h.Node(deep)!.Header.Contains("改名后"),
                $"'{h.Node(deep)!.Header}'");
        }

        // ==================================================================
        //  [M] 拖拽改序提交 + Version 递增次数 + 撤销逆操作
        // ==================================================================
        private static void DragReorderAndVersion()
        {
            Section("[M] 拖拽改序与 Version");

            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            var c = h.Leaf("C");
            h.Add(a); h.Add(b); h.Add(c);
            var cv = h.Canvas;

            var version0 = h.Flow.Version;
            Check("初始画布已建：节点 3 步骤", h.MainSteps().Count == 3, "");

            // 模拟 OnItemsDragCompleted 提交改序：a 从 index 0 移到 index 2
            var reorder = FlowCanvasExtensions.CreateReorderCommand(h.Flow.Steps, 0, 2);
            FlowCanvasExtensions.InvokeRedo(reorder);

            Check("Redo 执行后 Steps[0] = B", h.Flow.Steps[0] == b, "");
            Check("Redo 执行后 Steps[2] = A", h.Flow.Steps[2] == a, "");
            Check("Redo 触发 Version 递增（1 次）", h.Flow.Version == version0 + 1, $"{version0} → {h.Flow.Version}");
            Check("Redo 后画布主列按新顺序展示",
                h.MainSteps().Select(n => n.StepId).SequenceEqual(new[] { b.StepID, c.StepID, a.StepID }),
                string.Join(",", h.MainSteps().Select(n => n.Header)));

            cv.PushUndoReflection(reorder);
            Check("压栈后撤销栈深度 = 1", cv.UndoStackSize == 1, $"size={cv.UndoStackSize}");
            Check("压栈后重做栈为空", cv.RedoStackSize == 0, "");

            var another = FlowCanvasExtensions.CreateReorderCommand(h.Flow.Steps, 0, 1);
            FlowCanvasExtensions.InvokeRedo(another);
            cv.PushUndoReflection(another);
            Check("第二次压栈后撤销栈 = 2", cv.UndoStackSize == 2, "");

            var versionBeforeUndo = h.Flow.Version;
            cv.UndoCommand.Execute();
            Check("Undo 把第二次改序回滚：Steps[0] = B", h.Flow.Steps[0] == b, $"'{h.Flow.Steps[0].StepName}'");
            Check("Undo 触发 Version 递增", h.Flow.Version == versionBeforeUndo + 1, "");
            Check("Undo 后重做栈 = 1", cv.RedoStackSize == 1, "");

            var versionBeforeRedo = h.Flow.Version;
            cv.RedoCommand.Execute();
            Check("Redo 重放第二次改序：Steps[0] = C", h.Flow.Steps[0] == c, $"'{h.Flow.Steps[0].StepName}'");
            Check("Redo 触发 Version 递增", h.Flow.Version == versionBeforeRedo + 1, "");
            Check("Redo 后撤销栈 = 2", cv.UndoStackSize == 2, "");

            cv.UndoCommand.Execute();
            cv.UndoCommand.Execute();
            Check("两次 Undo 后回到原始顺序 [A,B,C]",
                h.Flow.Steps[0] == a && h.Flow.Steps[1] == b && h.Flow.Steps[2] == c,
                string.Join(",", h.Flow.Steps.Select(s => s.StepName)));
            Check("全部 Undo 后撤销栈空", cv.UndoStackSize == 0, "");
            Check("全部 Undo 后重做栈 = 2", cv.RedoStackSize == 2, "");

            Check("栈空时 UndoCommand.CanExecute = false", !cv.UndoCommand.CanExecute(), "");
            Check("栈非空时 RedoCommand.CanExecute = true", cv.RedoCommand.CanExecute(), "");

            var another2 = FlowCanvasExtensions.CreateReorderCommand(h.Flow.Steps, 0, 1);
            FlowCanvasExtensions.InvokeRedo(another2);
            cv.PushUndoReflection(another2);
            Check("新操作后 redo 栈被清空", cv.RedoStackSize == 0, "");
        }

        // ==================================================================
        //  [O] 跨分支移分支（仅同层）
        // ==================================================================
        private static void CrossBranchMoveAndUndo()
        {
            Section("[O] 跨分支移分支");

            var h = new Harness();
            var cond = h.If("判断");
            var ifBranch = cond.Children[0];
            var elseBranch = cond.Children[1];
            var b1 = h.Leaf("B1");
            var b2 = h.Leaf("B2");
            ifBranch.Steps.Add(b1);
            ifBranch.Steps.Add(b2);
            h.Add(cond);
            var cv = h.Canvas;

            Check("建图：If 分支 2 个、Else 空",
                ifBranch.Steps.Count == 2 && elseBranch.Steps.Count == 0, "");

            var move = FlowCanvasExtensions.CreateMoveBranchCommand(
                b1, ifBranch.Steps, 0, elseBranch.Steps, 0);
            FlowCanvasExtensions.InvokeRedo(move);
            cv.PushUndoReflection(move);

            Check("Redo 后 If 分支剩 [B2]", ifBranch.Steps.Count == 1 && ifBranch.Steps[0] == b2, "");
            Check("Redo 后 Else 分支为 [B1]", elseBranch.Steps.Count == 1 && elseBranch.Steps[0] == b1, "");
            Check("画布重新渲染：两条泳道都在、两个节点都可见",
                h.Lane(ifBranch) != null && h.Lane(elseBranch) != null
                && h.Node(b1) != null && h.Node(b2) != null,
                "");

            cv.UndoCommand.Execute();
            Check("Undo 后 If 分支恢复 [B1,B2]",
                ifBranch.Steps.Count == 2 && ifBranch.Steps[0] == b1 && ifBranch.Steps[1] == b2, "");
            Check("Undo 后 Else 分支空", elseBranch.Steps.Count == 0, "");

            cv.RedoCommand.Execute();
            Check("Redo 后再次 If=[B2], Else=[B1]",
                ifBranch.Steps[0] == b2 && elseBranch.Steps[0] == b1, "");

            var other = new FlowModel { FlowName = "其他流程" };
            h.Solution.Flows.Add(other);
            h.Workspace.SwitchFlow(other);
            Check("切流程后撤销栈被清空", cv.UndoStackSize == 0 && cv.RedoStackSize == 0, "");
            h.Workspace.SwitchFlow(h.Flow);
            Check("切回原流程后栈仍空（不复活）", cv.UndoStackSize == 0 && cv.RedoStackSize == 0, "");
        }

        // ==================================================================
        //  [P] 解绑与撤销：右键连线逐条解绑（画布唯一的连线写回路径）
        // ==================================================================
        private static void UnbindUndoRedo()
        {
            Section("[P] 解绑 / 撤销");

            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            Harness.WithInput(b, "In");
            h.Add(a); h.Add(b);
            Harness.RawLink(b, "In", a, "Out");
            var c = h.Canvas;

            var link = h.Link(a, b)!;
            var binding = link.Bindings.Single();
            Check("绑定快照带解绑前的图纸引用（撤销的依据）", binding.OriginalLink != null, "");

            link.UnbindCommand.Execute(binding);
            Check("解绑后图纸引用删除", !b.LinkedSources.ContainsKey("In"), "");
            Check("解绑后连线消失", c.DataLinkCount == 0, $"{c.Links.Count}");
            Check("解绑留痕状态栏（可撤销）", (c.StatusHint ?? "").Contains("解绑"), $"'{c.StatusHint}'");

            c.UndoCommand.Execute();
            Check("撤销恢复图纸引用", b.LinkedSources.ContainsKey("In")
                && b.LinkedSources["In"].TargetStepId == a.StepID, "");
            Check("撤销后连线重画回来", c.DataLinkCount == 1 && h.Link(a, b) != null, "");

            c.RedoCommand.Execute();
            Check("重做再次解绑", !b.LinkedSources.ContainsKey("In") && c.DataLinkCount == 0, "");
        }

        // ==================================================================
        //  [Q] 栈深 50 上限与切流程清栈
        // ==================================================================
        private static void UndoStackBoundary()
        {
            Section("[Q] 栈深上限与切流程清栈");

            var h = new Harness();
            var p = h.Leaf("P");
            var q = h.Leaf("Q");
            Harness.WithInput(q, "In");
            h.Add(p); h.Add(q);
            Harness.RawLink(q, "In", p, "Out");
            var cv = h.Canvas;

            // 断线命令是 internal：与既有 CheckLinkOrder 同款反射构造
            var disconnectType = typeof(FlowCanvasViewModel).Assembly.GetType(
                "VisionMaster.ViewModels.DisconnectCommand")
                ?? throw new InvalidOperationException("DisconnectCommand 类型未找到");
            var cmd = Activator.CreateInstance(disconnectType,
                q, "In", q.LinkedSources["In"])!;

            for (int i = 0; i < 51; i++)
                cv.PushUndoReflection(cmd);
            Check("压栈 51 条后栈深封顶 50", cv.UndoStackSize == 50, $"size={cv.UndoStackSize}");

            var other = new FlowModel { FlowName = "其他流程" };
            h.Solution.Flows.Add(other);
            h.Workspace.SwitchFlow(other);
            Check("切流程清空撤销栈（旧图纸引用不阻塞 GC）",
                cv.UndoStackSize == 0 && !cv.UndoCommand.CanExecute(), "");
        }

        // ==================================================================
        //  [R] 运行时变量：定义节点 → 按变量名回找的模块连线 + 落盘往返
        // ==================================================================
        private static void RuntimeVariableModuleLinks()
        {
            Section("[R] 运行时变量模块连线");

            // ---- R1 定义节点 → For.LoopCount：按变量名聚合出一条线 ----
            var h = new Harness();
            var def = h.Variable("loopN", "int", "定义loopN");
            var forStep = h.For("循环");
            h.Add(def);
            h.Add(forStep);
            forStep.SetLink("LoopCount", new LinkReference(
                LinkKind.RuntimeVariable,
                LinkProtocol.RuntimeVariableMarkerGuid,
                "loopN",
                "Runtime.loopN"));
            var cv = h.Canvas;

            Check("定义节点被识别并渲染（身份来自 FlowQueryHelper 的同一口径）",
                h.Node(def) != null, "");
            Check("按变量名回找出模块连线（不依赖端口）",
                h.Link(def, forStep) != null, $"links={cv.Links.Count}");
            Check("变量线合法且不降级",
                cv.DeferredLinkCount == 0 && cv.IllegalLinkCount == 0 && !h.Link(def, forStep)!.IsIllegal, "");
            Check("绑定显示串带变量名与输入别名",
                h.Link(def, forStep)!.Bindings.Single().Display.Contains("loopN")
                && h.Link(def, forStep)!.Bindings.Single().Display.Contains("循环次数"),
                $"'{h.Link(def, forStep)!.Bindings.Single().Display}'");
            Check("图纸侧三元组不变（画布只读投影）",
                forStep.LinkedSources["LoopCount"].TargetStepId == LinkProtocol.RuntimeVariableMarkerGuid
                && forStep.LinkedSources["LoopCount"].TargetPortName == "loopN", "");

            // ---- R2 定义节点被删 → 降级隐形，不标红不删图纸 ----
            h.Flow.Steps.Remove(def);
            Check("定义节点被删 → 降级隐形（不画线、不标红、不告警）",
                cv.DeferredLinkCount == 1 && cv.DataLinkCount == 0 && cv.WarningVisibility == Visibility.Collapsed,
                $"deferred={cv.DeferredLinkCount}");
            Check("降级不动图纸（画布不替用户删线）",
                forStep.LinkedSources.ContainsKey("LoopCount"), "");

            // ---- R3 落盘往返：RuntimeVariable 显式落盘，定义身份不丢 ----
            var hb = new Harness();
            var var2 = hb.Variable("thresh", "double", "定义thresh");
            var leaf = hb.Leaf("消费");
            Harness.WithInput(leaf, "In");
            hb.Add(var2);
            hb.Add(leaf);
            leaf.SetLink("In", new LinkReference(
                LinkKind.RuntimeVariable,
                LinkProtocol.RuntimeVariableMarkerGuid,
                "thresh",
                "Runtime.thresh"));
            hb.Canvas.RebuildInPlace();
            Check("变量喂普通算子输入口同样回找成线",
                hb.Link(var2, leaf) != null && !hb.Link(var2, leaf)!.IsIllegal, "");

            var json = SolutionService.Serialize(hb.Solution);
            Check("落盘文本里 Kind 是数字 3（不再靠 DisplayAddress 猜）",
                json.Contains("\"Kind\": 3") && json.Contains("\"Runtime.thresh\""), "");

            var reloaded = JsonConvert.DeserializeObject<SolutionModel>(json, RoundTripSettings)!;
            var reloadedFlow = reloaded.Flows.Single(f => f.FlowName == "画布断言流程");
            var def2 = reloadedFlow.Steps.Single(s => s.PluginTypeName!.Contains("VariableDefinitionPlugin"));
            var leaf2 = reloadedFlow.Steps.Single(s => !ReferenceEquals(s, def2));
            Check("往返后连线身份不变",
                leaf2.LinkedSources["In"].NormalizeKind() == LinkKind.RuntimeVariable
                && leaf2.LinkedSources["In"].TargetPortName == "thresh", "");
            Check("往返后变量定义步骤仍能被识别（Name/Type 存成裸字符串）",
                FlowQueryHelper.TryGetDefinedVariable(def2, out var rn2, out var rt2)
                && rn2 == "thresh" && rt2 == typeof(double),
                $"{rn2}/{rt2.Name}");

            // ---- R4 地基回归：绑定弹窗候选树递归 For 子层 ----
            var hc = new Harness();
            var pre = hc.Leaf("前置");
            var outer = hc.For("外层For");
            var innerDef = hc.Variable("inLoop", "int", "循环内定义");
            var innerConsumer = hc.Leaf("循环内消费");
            Harness.WithInput(innerConsumer, "In");
            hc.Add(pre);
            hc.Add(outer);
            outer.Children[0].Steps.Add(innerDef);
            outer.Children[0].Steps.Add(innerConsumer);

            var upstream = FlowQueryHelper.GetUpstreamNodes(hc.Flow.Steps, innerConsumer);
            Check("候选树能递归进 For 子层拿到循环内定义的变量（旧实现只认 If）",
                upstream.Contains(pre) && upstream.Contains(outer) && upstream.Contains(innerDef),
                $"找到 {upstream.Count} 个：[{string.Join(",", upstream.Select(s => s.StepName))}]");
        }

        // ==================================================================
        //  离线工具：向方案文件的 MainTask 注入 If 容器（UI 实测素材）
        // ==================================================================
        private static void InjectIfIntoMainTask(string path)
        {
            var json = File.ReadAllText(path);
            var solution = JsonConvert.DeserializeObject<SolutionModel>(json, RoundTripSettings)!;
            var flow = solution.Flows.FirstOrDefault(f => f.FlowName == "MainTask")
                       ?? solution.Flows.First();

            var cond = new ConditionStep("", "If", "BuiltIn_If", "If_自动化测试");
            cond.Children[0].Expression = "1 > 0";
            flow.Steps.Add(cond);

            File.WriteAllText(path, SolutionService.Serialize(solution));
            Console.WriteLine($"OK: If 容器已注入 [{flow.FlowName}]，顶层步骤 {flow.Steps.Count} 个 → {path}");
        }

        // ==================================================================
        //  断言骨架
        // ==================================================================
        // 断言骨架对同程序的 ExecutionChecks（执行层断言）开放，共用一套计数与汇总口径
        internal static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine($"---- {title} ----");
        }

        internal static void Check(string name, bool ok, string detail)
        {
            _pass += ok ? 1 : 0;
            _fail += ok ? 0 : 1;
            Console.WriteLine($"  [{(ok ? "√通过" : "×失败")}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  →  " + detail)}");
        }

        private static void Finish()
        {
            Console.WriteLine();
            Console.WriteLine("========== 结果汇总 ==========");
            Console.WriteLine($"通过: {_pass}  失败: {_fail}");
            Console.WriteLine(_fail == 0 ? ">>> 全部断言通过 <<<" : $">>> 存在 {_fail} 项失败 <<<");
            Environment.ExitCode = _fail == 0 ? 0 : 1;
        }

        /// <summary>
        /// 方案落盘用的反序列化设置：与 <c>SolutionService</c> 内部那份保持一致。
        /// 产品里那份是私有字段，这里只能用同一个 public binder 复刻，否则往返断言测的不是同一条链路。
        /// </summary>
        private static readonly JsonSerializerSettings RoundTripSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = new ConnectionConfigSerializationBinder()
        };
    }
}
