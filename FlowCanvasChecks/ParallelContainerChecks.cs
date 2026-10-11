using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Core.Interfaces;
using Newtonsoft.Json;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 顺序记录桩算子：把编译器拼好的 InstanceName（含步骤名）追加进静态 Order。
    /// 独立于 CountingPlugin（那是共享计数器），这里要的是"先谁后谁"。
    /// 用前 Reset 清零。
    /// [ParallelSafe]：Order 是 List 但只在 Reset（用例前）与 join 后读，
    /// 并行断言（P15/P26/P30）把它放进分支内记录执行序；写侧单分支线程独占。
    /// </summary>
    [ParallelSafe]
    internal sealed class OrderPlugin : VisionPluginBase
    {
        public static readonly List<string> Order = new();

        public static void Reset() => Order.Clear();

        public override void RunAlgorithm(IExecutionContext context)
            => Order.Add(InstanceName ?? "?");
    }

    /// <summary>
    /// 并行分组容器（第一期：顺序逐分支 + 并列泳道）断言：
    ///   · 模型：默认双分支、存盘往返不翻倍（Children 两件套）；
    ///   · 拓扑：ParallelStep 被 IContainerStep 口径覆盖（分支内步骤有位置、深度正确）；
    ///     分支间连线 = CrossBranch 非法（含画布文案）、分支接外层输入合法；
    ///   · 编译：无分支硬错误；超过建议上限给结构提醒；
    ///   · 执行：顺序逐分支跑完汇合（分支 1 → 分支 2），容器状态 Success；
    ///   · 画布：类型徽标 Parallel、并列泳道几何（分支等高起点、横向平铺）；
    ///   · 几何不变量（V5）：泳道内嵌套容器不互压、空泳道按列依次排布。
    /// </summary>
    internal static class ParallelContainerChecks
    {
        internal static void Run()
        {
            ModelDefaultsAndRoundTrip();
            TopologyAndLegality();
            CompileAndExecute();
            CanvasBadgesAndLanes();
            NestedContainersInsideLanes();
        }

        private static void ModelDefaultsAndRoundTrip()
        {
            Section("[V1] 并行分组模型与存盘往返");

            var h = new Harness();
            var par = h.Parallel("并行组");
            Check("默认创建两条分支", par.Children.Count == 2, $"{par.Children.Count}");
            Check("分支均为 Default 型（无条件语义）",
                par.Children.All(c => c.BranchType == BranchType.Default), "");
            Check("分支名从 1 起编号", par.Children[0].StepName == "分支 1" && par.Children[1].StepName == "分支 2", "");

            h.Add(par);
            var json = SolutionService.Serialize(h.Solution);
            var reloaded = JsonConvert.DeserializeObject<SolutionModel>(json, RoundTripSettings)!;
            // SolutionModel 序列化把预置的 Home/Main/End 骨架一起写出，
            // Flows[0] 是 Home——必须按 FlowName 找回我们那个流程（既有断言同款口径）
            var reloadedFlow = reloaded.Flows.Single(f => f.FlowName == "画布断言流程");
            var parReloaded = reloadedFlow.Steps.OfType<ParallelStep>().Single();
            Check("存盘往返后 ParallelStep 身份保真", parReloaded.Children.Count == 2,
                $"branches={parReloaded.Children.Count}");
            Check("存盘往返后分支不翻倍（Replace 两件套）",
                reloadedFlow.Steps.Count == 1 && parReloaded.Children.Count == 2,
                $"steps={reloadedFlow.Steps.Count}");
        }

        private static void TopologyAndLegality()
        {
            Section("[V2] 并行分组拓扑与连线合法性");

            var h = new Harness();
            var producer = h.Leaf("外部输入");
            var par = h.Parallel("并行组");
            var a1 = h.Leaf("分支1算子A");
            var a2 = h.Leaf("分支1算子B");
            var b1 = h.Leaf("分支2算子");
            par.Children[0].Steps.Add(a1);
            par.Children[0].Steps.Add(a2);
            par.Children[1].Steps.Add(b1);
            h.Add(producer);
            h.Add(par);

            var topo = FlowTopology.Build(h.Flow);
            var okA1 = topo.TryGet(a1.StepID, out var posA1);
            var okB1 = topo.TryGet(b1.StepID, out var posB1);
            Check("分支内步骤有拓扑位置（IContainerStep 口径自动覆盖）",
                okA1 && okB1, "");
            Check("分支内步骤嵌套深度 = 1",
                posA1!.Depth == 1 && posB1!.Depth == 1, $"{posA1!.Depth}/{posB1!.Depth}");
            Check("分支内步骤的 Branch 指向各自集合（不是容器本体）",
                ReferenceEquals(posA1.Branch, par.Children[0]) && ReferenceEquals(posB1.Branch, par.Children[1]), "");

            Check("分支间接外层输入 = 合法（进入前已有值）",
                topo.Classify(producer.StepID, a1.StepID) == LinkLegality.SameListBefore,
                $"{topo.Classify(producer.StepID, a1.StepID)}");
            Check("分支之间互取 = CrossBranch 非法（时序不确定）",
                topo.Classify(a1.StepID, b1.StepID) == LinkLegality.CrossBranch,
                $"{topo.Classify(a1.StepID, b1.StepID)}");
            Check("分支 1 内部前后互取 = 合法",
                topo.Classify(a1.StepID, a2.StepID) == LinkLegality.SameListBefore,
                $"{topo.Classify(a1.StepID, a2.StepID)}");

            // 画布口径与拓扑同一真相源：跨并行分支的线在画布上也是红虚线
            Harness.RawLink(b1, "In", a1, "Out");
            var cv = h.Canvas;
            var crossLink = cv.Links.FirstOrDefault(l => !l.IsOrderLink);
            Check("画布把跨并行分支连线标非法（红虚线）",
                crossLink != null && crossLink.IsIllegal
                && (crossLink.WarningText ?? string.Empty).Contains("并行分支"),
                $"'{crossLink?.WarningText}'");
        }

        private static void CompileAndExecute()
        {
            Section("[V3] 并行分组编译与执行（第一期顺序语义）");

            // 无分支 = 硬错误（不给假绿）
            var hEmpty = new Harness();
            hEmpty.Add(hEmpty.Parallel("空并行组"));
            hEmpty.Flow.Steps.Clear();
            // 构造空 Children：直接清掉默认分支
            var emptyPar = hEmpty.Parallel("空并行组");
            emptyPar.Children.Clear();
            var runEmpty = ExecHarness.Prepare(new StepModel[] { emptyPar });
            Check("无分支的并行组 = 编译错误（空操作不给假绿）",
                !runEmpty.Compiled && runEmpty.Errors.Contains("没有任何分支"),
                $"'{runEmpty.Errors}'");

            // 正常执行：分支 1（两算子）→ 分支 2（一算子），顺序逐条
            var orderPluginType = typeof(OrderPlugin);
            ActionStep OrderStep(string name) => new("\uE700", "顺序记录", orderPluginType.AssemblyQualifiedName!, name);

            var par = new ParallelStep("\uE700", "并行分组", "VM.CanvasStub.Parallel", "并行组");
            par.Children[0].Steps.Add(OrderStep("分支1A"));
            par.Children[0].Steps.Add(OrderStep("分支1B"));
            par.Children[1].Steps.Add(OrderStep("分支2A"));

            OrderPlugin.Reset();
            var run = ExecHarness.Prepare(new StepModel[] { par });
            Check("并行组编译成功", run.Compiled, run.Errors);

            var log = new StubLog();
            var ctx = run.NewContext(log);
            run.Engine!.Run(ctx);
            Check("三个算子全部执行", OrderPlugin.Order.Count == 3, $"{OrderPlugin.Order.Count}");
            Check("顺序语义：分支1A → 分支1B → 分支2A（第一期）",
                string.Join("→", OrderPlugin.Order.Select(n => n.Split('.').Last())) == "分支1A→分支1B→分支2A",
                string.Join("→", OrderPlugin.Order));
            Check("容器状态 = Success", par.State == StepState.Success, $"{par.State}");

            // 超过建议上限 = 编译旁注 + 运行日志（不拦截）。默认 2 分支 + 补 9 = 11 条
            var wide = new ParallelStep("\uE700", "并行分组", "VM.CanvasStub.Parallel", "宽并行组");
            for (int i = 0; i < 9; i++)
                wide.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = $"补充分支{i}" });
            var runWide = ExecHarness.Prepare(new StepModel[] { wide });
            Check("11 条分支编译仍成功（观感提醒不拦截）", runWide.Compiled, runWide.Errors);

            var logWide = new StubLog();
            runWide.Engine!.Run(runWide.NewContext(logWide));
            Check("超上限提醒经运行日志带出（编译旁注不进 Errors）",
                logWide.HasWarn("建议上限"), string.Join("|", logWide.Warns));
        }

        private static void CanvasBadgesAndLanes()
        {
            Section("[V4] 并行分组画布渲染（徽标与并列泳道）");

            var h = new Harness();
            var par = h.Parallel("并行组");
            var a1 = h.Leaf("分支1A");
            var b1 = h.Leaf("分支2A");
            par.Children[0].Steps.Add(a1);
            par.Children[1].Steps.Add(b1);
            h.Add(par);
            var cv = h.Canvas;

            var parNode = h.Node(par)!;
            Check("容器框类型徽标 = Parallel", parNode.TypeBadge == "Parallel", $"'{parNode.TypeBadge}'");
            Check("容器框渲染（IContainerStep 口径自动覆盖）", parNode.Kind == CanvasNodeKind.Container, "");

            var lane1 = h.Lane(par.Children[0])!;
            var lane2 = h.Lane(par.Children[1])!;
            Check("两条泳道都渲染", lane1 != null && lane2 != null, "");

            // 并列泳道几何：两分支等高起点（顶部对齐）、横向平铺（左泳道整体在右泳道左侧）
            Check("并列泳道：分支 1 顶边 = 分支 2 顶边（等高起点）",
                Math.Abs(lane1.Location.Y - lane2.Location.Y) < 1,
                $"y1={lane1.Location.Y:0} y2={lane2.Location.Y:0}");
            Check("并列泳道：分支 1 在分支 2 左侧（横向平铺）",
                lane1.Location.X < lane2.Location.X && lane1.Location.X + lane1.LaneWidth <= lane2.Location.X + 1,
                $"x1={lane1.Location.X:0} x2={lane2.Location.X:0}");
            Check("泳道标题绑分支名", lane1.Header.Contains("分支 1"), $"'{lane1.Header}'");
        }

        // ==================================================================
        //  [V5] 泳道内嵌套容器与空泳道的几何不变量
        //
        //  2026-10-09 真机"流程画布太乱"的根因：布局用固定行高/列距
        //  （RowHeight=90 / ColumnGap=260 / BranchIndent=220），而容器框的渲染几何是
        //  "子孙外接框 + 内边距"——框右缘天然越过下一列起点 200px 以上。顶层靠兄弟纵向堆叠
        //  侥幸躲过，并行分组"各分支同 Y 横向平铺"就原样暴露成互压；混合空泳道又统一贴到
        //  内容左上角，多条空泳道同址重叠。
        //
        //  修法三条，本节逐条钉住：
        //    I1/I2  布局改尺寸感知（度量 + 派位）：列宽按泳道实宽推进，框体完整落在所属泳道内；
        //    I3     空泳道按列序定位（有内容列定盘、空列向左回推/向右推进）；
        //    I4     行距按上一行框体实际高推进——兄弟行不叠。
        // ==================================================================
        private static void NestedContainersInsideLanes()
        {
            Section("[V5] 泳道内嵌套容器与空泳道的几何不变量（I1~I4、I6）");

            NestedIfInsideParallelLane();
            EmptyLanesSitInColumnOrder();
            NestedForInsideParallelLane();
            TidyLayoutRestoresInvariants();
            DeepChainTidyLayoutGeometry();
            ZOrderOcclusionDirection();
            DegenerateContainersTidy();
        }

        // ==================================================================
        //  [V5] ZOrder 遮挡方向守闸：面板上 ZIndex 高者必须真的盖住低者
        //
        //  背景（2026-10-09 审查"未覆盖"#3）：Panel.ZIndex 与 VM.ZOrder 一致已由探针
        //  自检钉住，但"谁真的盖住谁"（渲染结果）没有守闸——若 items 宿主换成一个
        //  不吃 Panel.ZIndex 的容器，绑定照样生效、探针照样绿，遮挡却退回集合序。
        //  这里在断言宿主里直接做一次位图采样：两个全重叠的 Border 先加蓝后加红，
        //  给蓝更高 ZIndex → 采样像素必须是蓝（不给 ZIndex 时是后加的红在上）。
        // ==================================================================
        private static void ZOrderOcclusionDirection()
        {
            // 不 new Application（断言宿主 Main 已是 [STAThread]，第二个 Application 会卡初始化）；
            // RenderTargetBitmap 渲染纯视觉树也不需要 Application。
            // 先在后台 STA 线程里跑（Main 线程此刻可能被前面的 Check 输出占用无关，但
            // Dispatcher 消息泵不归我们管，索性独立线程 + Join，行为确定）。
            var thread = new System.Threading.Thread(() =>
            {
                var host = new System.Windows.Controls.Canvas { Width = 60, Height = 60 };

                System.Windows.Media.SolidColorBrush Blue() =>
                    new(System.Windows.Media.Color.FromRgb(0, 0, 255));
                System.Windows.Media.SolidColorBrush Red() =>
                    new(System.Windows.Media.Color.FromRgb(255, 0, 0));

                var blue = new System.Windows.Controls.Border { Width = 60, Height = 60, Background = Blue() };
                var red = new System.Windows.Controls.Border { Width = 60, Height = 60, Background = Red() };
                host.Children.Add(blue);   // 先加蓝
                host.Children.Add(red);    // 后加红（无 ZIndex 时红在上）

                host.Measure(new System.Windows.Size(60, 60));
                host.Arrange(new System.Windows.Rect(0, 0, 60, 60));
                host.UpdateLayout();

                System.Windows.Media.Color Sample()
                {
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(60, 60, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(host);
                    // stride/buffer 按整幅算（Pbgra32 = 4 字节/像素）：60×60 = 14400 字节
                    var pixels = new byte[rtb.PixelWidth * 4 * rtb.PixelHeight];
                    rtb.CopyPixels(System.Windows.Int32Rect.Empty, pixels, rtb.PixelWidth * 4, 0);
                    return System.Windows.Media.Color.FromRgb(pixels[2], pixels[1], pixels[0]);
                }

                var noIndex = Sample();
                System.Windows.Controls.Panel.SetZIndex(blue, 10);
                host.UpdateLayout();
                var withIndex = Sample();

                Check("V5：items 宿主是吃 Panel.ZIndex 的 Panel（不给 ZIndex=后加者在上）",
                    noIndex == System.Windows.Media.Color.FromRgb(255, 0, 0),
                    $"采样={noIndex}");
                Check("V5：ZIndex 高者真的盖住低者（蓝置顶后采样为蓝）——画布叠放层次的运行时前提",
                    withIndex == System.Windows.Media.Color.FromRgb(0, 0, 255),
                    $"采样={withIndex}");
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        // ==================================================================
        //  [V5] 畸形容器参与「整理」（2026-10-09 审查"未覆盖"#5）
        //
        //  两种"结构损坏/特殊"的容器：Children 为 null（反序列化损坏）、Children 为空列表
        //  （刚建好没分支）。它们走的是布局/渲染的兜底分支，此前无断言——
        //  整理不许抛异常，且空分支容器要渲染出一个框（折叠框尺寸）而不是崩掉。
        // ==================================================================
        private static void DegenerateContainersTidy()
        {
            var h = new Harness();
            var emptyChildren = new ParallelStep("\uE700", "并行分组", "BuiltIn_Parallel", "空分支组");
            emptyChildren.Children.Clear();
            var nullChildren = new ParallelStep("\uE700", "并行分组", "BuiltIn_Parallel", "无分支组")
            {
                Children = null,
            };
            var leaf = h.Leaf("普通算子");
            h.Add(emptyChildren);
            h.Add(nullChildren);
            h.Add(leaf);

            Exception boom = null;
            try { h.Canvas.TidyLayoutCommand.Execute(); }
            catch (Exception ex) { boom = ex; }

            Check("V5：畸形容器（空分支列表 / null 分支）参与整理不抛异常",
                boom == null, boom == null ? "" : boom.GetType().Name + ": " + boom.Message);

            var emptyNode = h.Node(emptyChildren);
            Check("V5：空分支容器渲染出折叠框尺寸的框体（兜底路径有产物，不是崩或零尺寸）",
                emptyNode != null && emptyNode.LaneWidth > 0 && emptyNode.LaneHeight > 0,
                emptyNode == null ? "没有节点" : $"{emptyNode.LaneWidth:0}×{emptyNode.LaneHeight:0}");
        }

        // ==================================================================
        //  [V5] 深度 ≥4 的嵌套链参与「整理」重排时的几何一致性（2026-10-09 审查 #1 锁死）
        //
        //  病根：TidyLayoutCore 的口径是"记录折叠集 → 清空坐标库 → AutoLayout → 恢复现场"。
        //  清库后每个容器在排位眼里都是"新项"，于是深于 AutoCollapseDepth(=3) 的容器按
        //  "超深自动折叠"的 230×64 折叠足迹参与父级行/列推进；随后的恢复循环又把它们改回展开。
        //  结果：渲染出来的框远大于父级预留 → 同级泳道互压、后续兄弟被吞进框内
        //  （深层框/泳道 ZOrder 更高，把兄弟压在下面）。十层图式实测：外层框 1196×2092，
        //  而排位只预留了 700，顶层兄弟「结果汇总_0」落进外层框内约 1300px。
        //
        //  修法：排位期把"最终折叠态"经 collapseOverride 交给 FlowLayoutStore，足迹与标记同源
        //  （见 FlowLayoutStore.PlaceContainer）。本组用例把该口径钉住。
        //
        //  图式（两用例共用）：并行分组（2 分支）→ 分支 1 里 4 层 If 嵌套链 L1⊃L2⊃L3⊃L4
        //  （每层第 2 分支各一叶子，L4 两分支各一叶子，L3 第 1 分支里 L4 之后还有一个同行后继）
        //  → 分支 2 一个叶子；顶层兄弟「结果汇总」排在并行组之后。
        //  AutoCollapseDepth=3 下 L1/L2（深度 2/3）在阈值内、L3/L4（深度 4/5）默认折叠——
        //  正是本缺陷的触发面：整理前手动展开它们，排位若仍按折叠足迹算就必然错位。
        // ==================================================================
        private static void DeepChainTidyLayoutGeometry()
        {
            // 图式构造：ifs[0..3] = L1..L4（外→内）、sides[i] = Li 第 2 分支的叶子、
            // deepA = L4 第 1 分支的叶子、afterDeep = L3 第 1 分支里跟在 L4 之后的同行后继。
            static (ParallelStep Par, ActionStep ParSibling, ConditionStep[] Ifs, ActionStep[] Sides,
                    ActionStep DeepA, ActionStep AfterDeep) Build(Harness h)
            {
                var ifs = new[] { h.If("嵌套L1"), h.If("嵌套L2"), h.If("嵌套L3"), h.If("嵌套L4") };
                var sides = new ActionStep[ifs.Length];
                for (int i = 0; i < ifs.Length; i++)
                {
                    if (i + 1 < ifs.Length) ifs[i].Children[0].Steps.Add(ifs[i + 1]);
                    sides[i] = h.Leaf($"L{i + 1}分支");
                    ifs[i].Children[1].Steps.Add(sides[i]);
                }

                var deepA = h.Leaf("L4分支A");
                ifs[3].Children[0].Steps.Add(deepA);

                var afterDeep = h.Leaf("L4后继");
                ifs[2].Children[0].Steps.Add(afterDeep);

                var par = h.Parallel("并行组");
                var parSibling = h.Leaf("分支2算子");
                par.Children[0].Steps.Add(ifs[0]);
                par.Children[1].Steps.Add(parSibling);
                h.Add(par);
                return (par, parSibling, ifs, sides, deepA, afterDeep);
            }

            // ---- 用例 A：深链全展开 → 「整理」→ 几何必须与最终态一致 ----
            var ha = new Harness();
            var a = Build(ha);
            var summary = ha.Add(ha.Leaf("结果汇总"));
            var cvA = ha.Canvas;   // 首次构建：L3/L4 按超深自动折叠（override 缺省口径）

            foreach (var c in a.Ifs)
                ha.Flow.Layout.SetCollapsed(c.StepID, false);   // 深链各层显式展开

            cvA.TidyLayoutCommand.Execute();

            // 逐层取"左列泳道 / 右列泳道"：并行组 2 列 + 4 层 If 各 2 列
            var levels = new List<(string Name, CanvasNodeViewModel Left, CanvasNodeViewModel Right)>
            {
                ("并行组", ha.Lane(a.Par.Children[0])!, ha.Lane(a.Par.Children[1])!),
            };
            for (int i = 0; i < a.Ifs.Length; i++)
                levels.Add(($"L{i + 1}", ha.Lane(a.Ifs[i].Children[0])!, ha.Lane(a.Ifs[i].Children[1])!));

            var parNode = ha.Node(a.Par)!;
            var parFrame = NodeBox(parNode);
            var summaryNode = ha.Node(summary)!;

            var badColumns = levels
                .Where(l => l.Left.Location.X + l.Left.LaneWidth > l.Right.Location.X + 1)
                .Select(l => $"{l.Name}: 左列.right={l.Left.Location.X + l.Left.LaneWidth:0} 右列.left={l.Right.Location.X:0}")
                .ToList();
            Check("V5：4 层嵌套链全展开整理后——逐层同级分支列不相交（并行组 + 4 层 If 各 2 列）",
                badColumns.Count == 0,
                badColumns.Count == 0
                    ? string.Join(" | ", levels.Select(l => $"{l.Name}:{l.Right.Location.X - (l.Left.Location.X + l.Left.LaneWidth):0}px"))
                    : string.Join("；", badColumns));

            var containFails = new List<string>();
            bool Contains(string label, CanvasNodeViewModel outer, CanvasNodeViewModel inner)
            {
                bool ok = Inside(NodeBox(outer), NodeBox(inner));
                if (!ok) containFails.Add($"{label}: outer={NodeBox(outer)} inner={NodeBox(inner)}");
                return ok;
            }

            bool containOk = true;
            containOk &= Contains("并行组框 ⊇ 分支1泳道", parNode, levels[0].Left);
            containOk &= Contains("并行组框 ⊇ 分支2泳道", parNode, levels[0].Right);
            for (int i = 0; i < a.Ifs.Length; i++)
            {
                var ifNode = ha.Node(a.Ifs[i])!;
                containOk &= Contains($"L{i + 1} 框 ⊇ 分支1泳道", ifNode, levels[i + 1].Left);
                containOk &= Contains($"L{i + 1} 框 ⊇ 分支2泳道", ifNode, levels[i + 1].Right);
                if (i + 1 < a.Ifs.Length)
                    containOk &= Contains($"L{i + 1} 分支1泳道 ⊇ L{i + 2} 框", levels[i + 1].Left, ha.Node(a.Ifs[i + 1])!);
            }

            Check("V5：4 层嵌套链全展开整理后——逐层嵌套框 ⊆ 其父泳道、容器框 ⊇ 自身两条泳道",
                containOk && containFails.Count == 0,
                containFails.Count == 0 ? "全层包含链成立" : string.Join("；", containFails));

            Check("V5：4 层嵌套链全展开整理后——顶层后续兄弟「结果汇总」在容器框底缘之下（旧实现：排位只按折叠足迹预留，兄弟被吞进框内）",
                summaryNode.Location.Y >= parFrame.Bottom - 1,
                $"并行组框={parFrame.Left:0},{parFrame.Top:0} {parFrame.Width:0}×{parFrame.Height:0} bottom={parFrame.Bottom:0}；"
                + $"结果汇总.top={summaryNode.Location.Y:0}（差 {summaryNode.Location.Y - parFrame.Bottom:0}px，负值 = 落进框内）");

            var boxes = new List<(StepModel Model, Rect Box)>
            {
                (a.Par, parFrame),
                (a.ParSibling, NodeBox(ha.Node(a.ParSibling)!)),
                (summary, NodeBox(summaryNode)),
                (a.DeepA, NodeBox(ha.Node(a.DeepA)!)),
                (a.AfterDeep, NodeBox(ha.Node(a.AfterDeep)!)),
            };
            for (int i = 0; i < a.Ifs.Length; i++)
            {
                boxes.Add((a.Ifs[i], NodeBox(ha.Node(a.Ifs[i])!)));
                boxes.Add((a.Sides[i], NodeBox(ha.Node(a.Sides[i])!)));
            }

            int pairs = 0;
            var clashes = new List<string>();
            for (int i = 0; i < boxes.Count; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    // 同一条父子链上的矩形本就该互相包含，不算"互压"
                    if (IsAncestorOf(boxes[i].Model, boxes[j].Model) || IsAncestorOf(boxes[j].Model, boxes[i].Model))
                        continue;
                    pairs++;
                    if (Overlaps(boxes[i].Box, boxes[j].Box))
                        clashes.Add($"{boxes[i].Model.StepName}×{boxes[j].Model.StepName}");
                }
            }

            Check("V5：4 层嵌套链全展开整理后——非同父子链的框/叶子两两不交（旧实现：深框按折叠足迹排位、越界压住相邻列/兄弟行）",
                clashes.Count == 0,
                $"检查 {pairs} 对，冲突 {clashes.Count}: {string.Join("；", clashes.Take(6))}");

            // ---- 用例 B（对照）：最内层折叠 → 「整理」→ 折叠足迹与标记同样要自洽 ----
            var hb = new Harness();
            var b = Build(hb);
            var summaryB = hb.Add(hb.Leaf("结果汇总"));
            var cvB = hb.Canvas;

            foreach (var c in b.Ifs)
                hb.Flow.Layout.SetCollapsed(c.StepID, false);
            hb.Flow.Layout.SetCollapsed(b.Ifs[3].StepID, true);   // 只留最内层 L4 折叠
            cvB.TidyLayoutCommand.Execute();

            var deepNode = hb.Node(b.Ifs[3])!;
            var parentLane = hb.Lane(b.Ifs[2].Children[0])!;      // L4 所在的那一列（L3 分支 1 泳道）
            var deepBox = NodeBox(deepNode);
            var afterNode = hb.Node(b.AfterDeep)!;

            bool collapsedSize = Math.Abs(deepBox.Width - FlowCanvasViewModel.CollapsedFrameWidth) < 0.01
                                 && Math.Abs(deepBox.Height - FlowCanvasViewModel.CollapsedFrameHeight) < 0.01;
            bool inColumn = Inside(NodeBox(parentLane), deepBox);
            bool rowClear = deepBox.Bottom <= afterNode.Location.Y + 1
                            && !Overlaps(deepBox, NodeBox(hb.Node(b.Sides[2])!))
                            && !Overlaps(deepBox, NodeBox(hb.Node(b.ParSibling)!))
                            && !Overlaps(NodeBox(hb.Node(b.Par)!), NodeBox(hb.Node(summaryB)!));

            Check("V5：4 层嵌套链最内层折叠整理后——折叠框仍 230×64、落在它该在的列里、兄弟行不压它",
                collapsedSize && inColumn && rowClear,
                $"折叠框={deepBox.Width:0}×{deepBox.Height:0}@{deepBox.Left:0},{deepBox.Top:0} 在列内={inColumn}；"
                + $"同行后继.top={afterNode.Location.Y:0}（框底={deepBox.Bottom:0}）；"
                + $"与L3分支2叶子相交={Overlaps(deepBox, NodeBox(hb.Node(b.Sides[2])!))}，与并行分支2叶子相交={Overlaps(deepBox, NodeBox(hb.Node(b.ParSibling)!))}");
        }

        /// <summary>a 是否是 b 的祖先（沿容器分支递归；用于挑出"不属于同一条父子链"的矩形对）</summary>
        private static bool IsAncestorOf(StepModel a, StepModel b)
        {
            if (a is not IContainerStep container || container.Children == null) return false;

            foreach (var branch in container.Children)
            {
                if (branch?.Steps == null) continue;
                foreach (var step in branch.Steps)
                {
                    if (ReferenceEquals(step, b) || IsAncestorOf(step, b)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// I6 生效路径：老图纸里已经是旧布局写下的散乱坐标，坐标库有记录 → 首次渲染根本不进 AutoLayout。
        /// 按一次「整理」（清空坐标重排）后必须回到不变量——这正是真机验收的路径。
        /// </summary>
        private static void TidyLayoutRestoresInvariants()
        {
            var h = new Harness();
            var par = h.Parallel("并行组");
            var nestedIf = h.If("泳道内嵌套判断");
            var okLeaf = h.Leaf("嵌套合格");
            var ngLeaf = h.Leaf("嵌套不合格");
            nestedIf.Children[0].Steps.Add(okLeaf);
            nestedIf.Children[1].Steps.Add(ngLeaf);
            par.Children[0].Steps.Add(nestedIf);
            par.Children[1].Steps.Add(h.Leaf("分支2算子"));
            h.Add(par);
            var cv = h.Canvas;

            // 模拟旧固定列距（缩进 220 / 列距 260）写下的老坐标：嵌套 If 的两支整块压到并行分支 2 的列上
            h.Flow.Layout.Set(nestedIf.StepID, 190, 0);
            h.Flow.Layout.Set(okLeaf.StepID, 220, 92);
            h.Flow.Layout.Set(ngLeaf.StepID, 480, 92);
            cv.RebuildInPlace();

            bool dirtyOverlap = Overlaps(NodeBox(h.Node(nestedIf)!), NodeBox(h.Lane(par.Children[1])!));
            cv.TidyLayoutCommand.Execute();

            var lane1 = h.Lane(par.Children[0])!;
            var lane2 = h.Lane(par.Children[1])!;
            Check("I6 老图纸嵌套框压住兄弟泳道（脏态）→ 按「整理」重排 → 列序/包含不变量全部恢复",
                dirtyOverlap
                && lane1.Location.X + lane1.LaneWidth <= lane2.Location.X + 1
                && !Overlaps(NodeBox(h.Node(nestedIf)!), NodeBox(lane2))
                && Inside(NodeBox(lane1), NodeBox(h.Node(nestedIf)!))
                && Inside(NodeBox(h.Node(nestedIf)!), NodeBox(h.Lane(nestedIf.Children[0])!)),
                $"整理前框压住分支2泳道={dirtyOverlap}；整理后 lane1.right={lane1.Location.X + lane1.LaneWidth:0} lane2.left={lane2.Location.X:0}");
        }

        /// <summary>并行泳道里放嵌套 If（两分支各一叶子）→ I1（同级列不相交 / 框 ⊆ 泳道）+ I2（跨层不互压）</summary>
        private static void NestedIfInsideParallelLane()
        {
            var h = new Harness();
            var par = h.Parallel("并行组");
            var nestedIf = h.If("泳道内嵌套判断");
            nestedIf.Children[0].Steps.Add(h.Leaf("嵌套合格"));
            nestedIf.Children[1].Steps.Add(h.Leaf("嵌套不合格"));
            var after = h.Leaf("分支1后继");
            par.Children[0].Steps.Add(nestedIf);
            par.Children[0].Steps.Add(after);
            var sibling = h.Leaf("分支2算子");
            par.Children[1].Steps.Add(sibling);
            h.Add(par);

            var cv = h.Canvas;
            var parNode = h.Node(par)!;
            var lane1 = h.Lane(par.Children[0])!;
            var lane2 = h.Lane(par.Children[1])!;
            var ifNode = h.Node(nestedIf)!;
            var ifLane1 = h.Lane(nestedIf.Children[0])!;
            var ifLane2 = h.Lane(nestedIf.Children[1])!;

            Check("图式齐备：并行组 2 条泳道 + 嵌套 If 2 条泳道 + 嵌套框节点",
                lane1 != null && lane2 != null && ifLane1 != null && ifLane2 != null && ifNode.Kind == CanvasNodeKind.Container,
                "");

            Check("I1 同级列不相交：并行分支 1 泳道右缘不越过分支 2 泳道左缘",
                lane1.Location.X + lane1.LaneWidth <= lane2.Location.X + 1,
                $"lane1.right={lane1.Location.X + lane1.LaneWidth:0} lane2.left={lane2.Location.X:0}");

            Check("I1 嵌套 If 框体完整落在所属泳道（分支 1）内",
                Inside(NodeBox(lane1), NodeBox(ifNode)),
                $"frame={NodeBox(ifNode)} lane1={NodeBox(lane1)}");

            Check("I1 嵌套 If 的两条分支列不互压",
                ifLane1.Location.X + ifLane1.LaneWidth <= ifLane2.Location.X + 1,
                $"ifLane1.right={ifLane1.Location.X + ifLane1.LaneWidth:0} ifLane2.left={ifLane2.Location.X:0}");

            Check("I1 嵌套 If 的两条分支泳道都落在其框体内",
                Inside(NodeBox(ifNode), NodeBox(ifLane1)) && Inside(NodeBox(ifNode), NodeBox(ifLane2)),
                $"frame={NodeBox(ifNode)} ifLane1={NodeBox(ifLane1)} ifLane2={NodeBox(ifLane2)}");

            Check("I1 并行组框体完整包含两条分支泳道",
                Inside(NodeBox(parNode), NodeBox(lane1)) && Inside(NodeBox(parNode), NodeBox(lane2)),
                $"frame={NodeBox(parNode)}");

            Check("I2 嵌套 If 框体不与并行分支 2 泳道相交（旧布局：框右缘整块压过去）",
                !Overlaps(NodeBox(ifNode), NodeBox(lane2)),
                $"frame={NodeBox(ifNode)} lane2={NodeBox(lane2)}");

            Check("I2 嵌套 If 的分支泳道也不与并行分支 2 泳道相交",
                !Overlaps(NodeBox(ifLane1), NodeBox(lane2)) && !Overlaps(NodeBox(ifLane2), NodeBox(lane2)),
                $"ifLane2.right={NodeBox(ifLane2).Right:0} lane2.left={NodeBox(lane2).Left:0}");

            Check("I2 嵌套 If 框体不与兄弟分支内容、同泳道后继步骤相交",
                !Overlaps(NodeBox(ifNode), NodeBox(h.Node(sibling)!))
                && !Overlaps(NodeBox(ifNode), NodeBox(h.Node(after)!)),
                $"frame={NodeBox(ifNode)} sibling={NodeBox(h.Node(sibling)!)} after={NodeBox(h.Node(after)!)}");

            Check("I4 兄弟行不叠：嵌套 If 底缘不越过同泳道后继步骤顶缘",
                NodeBox(ifNode).Bottom <= h.Node(after)!.Location.Y + 1,
                $"frame.bottom={NodeBox(ifNode).Bottom:0} after.top={h.Node(after)!.Location.Y:0}");
        }

        /// <summary>4 条分支只有第 1 条有内容 → I3（空泳道不同址、不与内容相交、按列依次排布）</summary>
        private static void EmptyLanesSitInColumnOrder()
        {
            var h = new Harness();
            var par = h.Parallel("四分支并行组");
            par.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "分支 3" });
            par.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "分支 4" });
            var only = h.Leaf("分支1唯一算子");
            par.Children[0].Steps.Add(only);
            h.Add(par);

            var cv = h.Canvas;
            var parNode = h.Node(par)!;
            var lanes = par.Children.Select(c => h.Lane(c)!).ToList();

            Check("4 条分支的泳道全部渲染（含 3 条空泳道）",
                lanes.Count == 4 && lanes.All(l => l != null), $"lanes={lanes.Count}");

            bool ordered = true;
            for (int i = 1; i < lanes.Count; i++)
                ordered &= lanes[i - 1].Location.X + lanes[i - 1].LaneWidth <= lanes[i].Location.X + 1;
            Check("I3 空泳道依次排布：分支序 = 左→右、相邻列不互压", ordered,
                string.Join(" | ", lanes.Select(l => $"{l.Header}@x={l.Location.X:0}+{l.LaneWidth:0}")));

            bool sameAddress = false;
            for (int i = 0; i < lanes.Count; i++)
                for (int j = i + 1; j < lanes.Count; j++)
                    sameAddress |= Math.Abs(lanes[i].Location.X - lanes[j].Location.X) < 0.01
                        && Math.Abs(lanes[i].Location.Y - lanes[j].Location.Y) < 0.01;
            Check("I3 空泳道不同址（旧实现：3 条空泳道拿到同一坐标、整片重叠）",
                !sameAddress,
                string.Join(" | ", lanes.Select(l => $"({l.Location.X:0},{l.Location.Y:0})")));

            var contentBox = NodeBox(h.Node(only)!);
            Check("I3 空泳道不与有内容泳道的内容/泳道相交",
                lanes.Skip(1).All(l => !Overlaps(NodeBox(l), contentBox) && !Overlaps(NodeBox(l), NodeBox(lanes[0]))),
                $"content={contentBox} lane1={NodeBox(lanes[0])}");

            Check("I3 空分支提示：3 条空泳道带占位提示、有内容泳道不带",
                !lanes[0].IsLaneEmpty && lanes.Skip(1).All(l => l.IsLaneEmpty), "");

            Check("并行组框体包住全部 4 条泳道（空泳道也在框内）",
                lanes.All(l => Inside(NodeBox(parNode), NodeBox(l))), $"frame={NodeBox(parNode)}");

            // 全空场景：4 条空泳道同样按列铺开（框体锚在容器存储坐标上）
            var h2 = new Harness();
            var emptyPar = h2.Parallel("全空并行组");
            emptyPar.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "分支 3" });
            emptyPar.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "分支 4" });
            h2.Add(emptyPar);
            var cv2 = h2.Canvas;
            var emptyLanes = emptyPar.Children.Select(c => h2.Lane(c)!).ToList();

            bool emptyOrdered = true, emptyOverlap = false;
            for (int i = 1; i < emptyLanes.Count; i++)
                emptyOrdered &= emptyLanes[i - 1].Location.X + emptyLanes[i - 1].LaneWidth <= emptyLanes[i].Location.X + 1;
            for (int i = 0; i < emptyLanes.Count; i++)
                for (int j = i + 1; j < emptyLanes.Count; j++)
                    emptyOverlap |= Overlaps(NodeBox(emptyLanes[i]), NodeBox(emptyLanes[j]));
            Check("I3 全空场景：空泳道同样按列依次排布且互不相交",
                emptyOrdered && !emptyOverlap && emptyLanes.All(l => l.IsLaneEmpty),
                string.Join(" | ", emptyLanes.Select(l => $"{l.Header}@x={l.Location.X:0}+{l.LaneWidth:0}")));
            Check("I3 全空场景：框体包住 4 条空泳道、宽度覆盖到最后一列",
                emptyLanes.All(l => Inside(NodeBox(h2.Node(emptyPar)!), NodeBox(l))),
                $"frame={NodeBox(h2.Node(emptyPar)!)} lastLane.right={NodeBox(emptyLanes[3]).Right:0}");
        }

        /// <summary>并行泳道里的嵌套 For（循环体内再嵌 If）→ 三层包含链 + I1/I2/I4</summary>
        private static void NestedForInsideParallelLane()
        {
            var h = new Harness();
            var par = h.Parallel("并行组");
            var forStep = h.For("泳道内嵌套循环");
            var innerIf = h.If("循环体内判断");
            var okLeaf = h.Leaf("判断分支A");
            var ngLeaf = h.Leaf("判断分支B");
            innerIf.Children[0].Steps.Add(okLeaf);
            innerIf.Children[1].Steps.Add(ngLeaf);
            var bodyLeaf = h.Leaf("循环体后继");
            forStep.Children[0].Steps.Add(innerIf);
            forStep.Children[0].Steps.Add(bodyLeaf);
            var after = h.Leaf("分支1后继");
            par.Children[0].Steps.Add(forStep);
            par.Children[0].Steps.Add(after);
            var sibling = h.Leaf("分支2算子");
            par.Children[1].Steps.Add(sibling);
            h.Add(par);

            var cv = h.Canvas;
            var lane1 = h.Lane(par.Children[0])!;
            var lane2 = h.Lane(par.Children[1])!;
            var forNode = h.Node(forStep)!;
            var forBody = h.Lane(forStep.Children[0])!;
            var ifNode = h.Node(innerIf)!;
            var ifLane1 = h.Lane(innerIf.Children[0])!;
            var ifLane2 = h.Lane(innerIf.Children[1])!;

            Check("图式齐备：并行泳道 → For → 循环体泳道 → 内层 If",
                forNode.Kind == CanvasNodeKind.Container && forBody != null && ifNode.Kind == CanvasNodeKind.Container
                && forNode.TypeBadge == "For" && ifNode.TypeBadge == "If", "");

            Check("I1 嵌套 For 框体完整落在所属泳道内",
                Inside(NodeBox(lane1), NodeBox(forNode)), $"frame={NodeBox(forNode)} lane1={NodeBox(lane1)}");

            Check("I1 三层包含链：For 框 ⊇ 循环体泳道 ⊇ 内层 If 框",
                Inside(NodeBox(forNode), NodeBox(forBody)) && Inside(NodeBox(forBody), NodeBox(ifNode)),
                $"for={NodeBox(forNode)} body={NodeBox(forBody)} if={NodeBox(ifNode)}");

            Check("I1 内层 If 的两条分支泳道都落在其框体内、且彼此不互压",
                Inside(NodeBox(ifNode), NodeBox(ifLane1)) && Inside(NodeBox(ifNode), NodeBox(ifLane2))
                && ifLane1.Location.X + ifLane1.LaneWidth <= ifLane2.Location.X + 1,
                $"if={NodeBox(ifNode)} l1={NodeBox(ifLane1)} l2={NodeBox(ifLane2)}");

            Check("I2 内层 If 框体不与循环体后继步骤相交",
                !Overlaps(NodeBox(ifNode), NodeBox(h.Node(bodyLeaf)!)),
                $"if.bottom={NodeBox(ifNode).Bottom:0} bodyLeaf.top={h.Node(bodyLeaf)!.Location.Y:0}");

            Check("I2 嵌套 For 框体不与并行分支 2 的泳道/内容相交",
                !Overlaps(NodeBox(forNode), NodeBox(lane2)) && !Overlaps(NodeBox(forNode), NodeBox(h.Node(sibling)!)),
                $"for.right={NodeBox(forNode).Right:0} lane2.left={NodeBox(lane2).Left:0}");

            Check("I2 内层 If 的两条分支泳道不与并行分支 2 泳道相交",
                !Overlaps(NodeBox(ifLane1), NodeBox(lane2)) && !Overlaps(NodeBox(ifLane2), NodeBox(lane2)), "");

            Check("I4 兄弟行不叠：嵌套 For 底缘不越过同泳道后继步骤顶缘",
                NodeBox(forNode).Bottom <= h.Node(after)!.Location.Y + 1,
                $"for.bottom={NodeBox(forNode).Bottom:0} after.top={h.Node(after)!.Location.Y:0}");
        }
    }
}
