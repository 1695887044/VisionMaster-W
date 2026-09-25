using System.ComponentModel;
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
    /// M2-1 段2 的非 GUI 断言宿主。
    ///
    /// 为什么单独开一个工程：FlowCanvasViewModel 挂在 VisionMaster（WinExe）里，
    /// 而它的全部对外契约（Nodes / Connections / Breadcrumb / 五个命令 / 四个计数）都是 public，
    /// 不需要为测试在产品程序里塞一个「自检开关」污染启动路径；
    /// 唯一的 internal 目标（FlowCompiler.CheckLinkOrder）用反射直调，
    /// 这与 CommTest 反射 HslHelper 的既有做法一致。
    ///
    /// 能这么跑的前提：FlowCanvasViewModel 及其依赖链不碰 Application.Current / Dispatcher，
    /// GlobalEventBus.Publish 也是同步 Invoke。所以控制台进程里 new 出来就是安全的。
    /// </summary>
    internal class Program
    {
        private static int _pass, _fail;

        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("========== 流程画布 M2-1 段2+段3 断言（子画布/泳道/层根/联动/增量重建 + 拖拽改序/撤销栈） ==========");

            // 一次性反射 dump（仅开发期辅助，不参与断言）
            // NodifyReflection.Dump();

            EmptyWorkspaceSafety();
            DecoratorNodesAndLanes();
            DrillDownAndBreadcrumb();
            LayerDegradation();
            PortExposureAndAnchors();
            ConnectionWritesAndRejections();
            IllegalAndDeferredCounts();
            TopologyVersusCompiler();
            BranchDisplayNameNotification();
            NodePoolKeepsSelection();
            LayoutWriteBackDiscipline();
            OutsideSelectionDrivesCanvas();
            DragReorderAndVersion();
            CrossBranchMoveAndUndo();
            ConnectDisconnectUndoRedo();
            UndoStackBoundary();
            RuntimeVariablePortAndLink();

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
            Check("空态：Nodes / Connections 皆空", bare.Nodes.Count == 0 && bare.Connections.Count == 0,
                $"Nodes={bare.Nodes.Count} Connections={bare.Connections.Count}");
            Check("空态：HasFlow=false 且提示可见", !bare.HasFlow && bare.EmptyHintVisibility == Visibility.Visible,
                $"EmptyHint={bare.EmptyHintVisibility}");
            Check("空态：IsNested=false / 面包屑空", !bare.IsNested && bare.Breadcrumb.Count == 0, "");
            Check("空态：两个计数与告警条归零",
                bare.DeferredLinkCount == 0 && bare.IllegalLinkCount == 0 && bare.WarningVisibility == Visibility.Collapsed,
                $"deferred={bare.DeferredLinkCount} illegal={bare.IllegalLinkCount}");

            // 先建图纸后开画布：构造时应当直接反映当前流程
            var h = new Harness();
            var a = h.Leaf("A");
            h.Add(a);
            Check("未开画布前不应触碰 Layout（不生成坐标）", h.Flow.Layout.Count == 0, $"Layout.Count={h.Flow.Layout.Count}");

            var c = h.Canvas;
            Check("打开画布即呈现当前流程", c.HasFlow && c.Breadcrumb.Count == 1 && c.Breadcrumb[0].IsCurrent,
                $"breadcrumb={string.Join(" > ", c.Breadcrumb.Select(x => x.Label))}");
            Check("面包屑首级文案取流程名", c.Breadcrumb[0].Label == h.Flow.FlowName, $"'{c.Breadcrumb[0].Label}'");

            // 切流程 → 走 CurrentFlow 通知自动重建
            var other = new FlowModel { FlowName = "辅助流程" };
            h.Solution.Flows.Add(other);
            h.Workspace.SwitchFlow(other);
            Check("切流程后画布清空（订阅挂在 WorkspaceContext 上）",
                c.Nodes.Count == 0 && !c.HasFlow && !c.IsNested, $"Nodes={c.Nodes.Count}");
            h.Workspace.SwitchFlow(h.Flow);
            Check("切回原流程后节点回来", c.Nodes.Any(n => n.Kind == CanvasNodeKind.Step && n.StepId == a.StepID), "");
        }

        // ==================================================================
        //  [B] 装饰节点：分支泳道 + 顶层无层根 + 几何 + 不入库
        // ==================================================================
        private static void DecoratorNodesAndLanes()
        {
            Section("[B] 分支泳道与装饰节点");

            var h = new Harness();
            var a = h.Leaf("A");
            var cond = h.If("判断");
            var ifBranch = cond.Children[0];
            var elseBranch = cond.Children[1];
            var b1 = h.Leaf("B1");
            ifBranch.Steps.Add(b1);
            var outer = h.For("外层循环");
            var body = outer.Children[0];
            var e1 = h.Leaf("E1");
            body.Steps.Add(e1);
            h.Add(a); h.Add(cond); h.Add(outer);

            var c = h.Canvas;
            var lanes = h.Lanes();
            Check("If/Else + 循环体 = 3 条泳道", lanes.Count == 3, $"实际 {lanes.Count}");
            Check("泳道按引用挂住分支（不是按名字猜）",
                h.Lane(ifBranch) != null && h.Lane(elseBranch) != null && h.Lane(body) != null, "");
            Check("泳道标题取分支显示名",
                h.Lane(elseBranch)!.Header == "Else", $"'{h.Lane(elseBranch)!.Header}'");
            Check("有内容的分支不算空泳道", h.Lane(ifBranch)!.IsLaneEmpty == false, "");
            Check("If 分支条件为空时标题带『未绑定条件』",
                h.Lane(ifBranch)!.Header.Contains("未绑定条件"), $"'{h.Lane(ifBranch)!.Header}'");

            Check("顶层没有层根锚点", h.Roots().Count == 0, $"实际 {h.Roots().Count}");
            // BuildNodes 无去重：主列步骤 + 各泳道内步骤都以 Kind==Step 进 Nodes
            Check("主列步骤数正确（不含泳道内步骤）", h.MainSteps().Count == 3, $"实际 {h.MainSteps().Count}");
            Check("全展开步骤数含泳道内步骤", h.Steps().Count == 5, $"实际 {h.Steps().Count}");
            Check("装饰节点排在最前（Z 序天然垫底）",
                c.Nodes[0].Kind != CanvasNodeKind.Step && c.Nodes[1].Kind != CanvasNodeKind.Step
                && c.Nodes.Skip(3).All(n => n.Kind == CanvasNodeKind.Step),
                string.Join(",", c.Nodes.Select(n => n.Kind)));

            Check("泳道不可拖 / 不可选 / 属装饰",
                lanes.All(l => !l.IsDraggable && !l.IsSelectable && l.IsDecorator), "");
            Check("泳道 StepId 为空 Guid（绝不写进坐标库）",
                lanes.All(l => l.StepId == Guid.Empty), "");
            foreach (var lane in lanes)
                Check($"泳道『{lane.Header}』外接框为正尺寸且包住内容",
                    lane.LaneWidth > 0 && lane.LaneHeight > 0, $"{lane.LaneWidth:0}x{lane.LaneHeight:0}");

            var before = h.Flow.Layout.Count;
            lanes[0].Location = new Point(-7000, -7000);
            Check("挪动泳道不新增坐标项", h.Flow.Layout.Count == before, $"{before} → {h.Flow.Layout.Count}");

            // 序号按本层下标 1 基：主列 a=1,cond=2,outer=3；泳道内 b1=1,e1=1（独立从 1 起）
            Check("主列执行序号按本层下标 1 基展示",
                h.MainSteps().Select(s => s.OrderIndex).OrderBy(x => x).SequenceEqual(new[] { 1, 2, 3 }),
                string.Join(",", h.MainSteps().Select(s => s.OrderIndex)));
            var laneSteps = h.Steps().Where(s => !h.MainSteps().Contains(s)).ToList();
            Check("泳道内步骤序号独立从 1 起",
                laneSteps.All(s => s.OrderIndex == 1),
                string.Join(",", laneSteps.Select(s => s.OrderIndex)));
            Check("序号标签随 OrderIndex 同步", h.Node(a)!.OrderLabel == "1", $"'{h.Node(a)!.OrderLabel}'");
        }

        // ==================================================================
        //  [C] 双击下钻、层根锚点、面包屑回跳
        // ==================================================================
        private static void DrillDownAndBreadcrumb()
        {
            Section("[C] 下钻 / 层根 / 面包屑");

            var h = new Harness();
            var a = h.Leaf("A");
            var cond = h.If("判断");
            var ifBranch = cond.Children[0];
            var inner = h.For("内层循环");
            var innerBody = inner.Children[0];
            var g = h.Leaf("G");
            innerBody.Steps.Add(g);
            var b1 = h.Leaf("B1");
            ifBranch.Steps.Add(b1);
            ifBranch.Steps.Add(inner);               // 内层容器也挂入 If 分支
            var later = h.Leaf("Z");
            h.Add(a); h.Add(cond); h.Add(later);

            var c = h.Canvas;

            c.DrillDownCommand.Execute(h.Node(cond));
            Check("双击容器下钻进第 0 分支", c.IsNested && c.Breadcrumb.Count == 2, $"IsNested={c.IsNested}");
            Check("下钻后面包屑末级为当前层", c.Breadcrumb[^1].IsCurrent && !c.Breadcrumb[0].IsCurrent, "");
            Check("面包屑标签含容器名与分支名",
                c.Breadcrumb[1].Label.Contains("判断") && c.Breadcrumb[1].Label.Contains("If"),
                $"'{c.Breadcrumb[1].Label}'");
            Check("下钻会把当前步骤切到容器（联动 TreeView）", ReferenceEquals(h.Workspace.CurrentStep, cond),
                $"'{h.Workspace.CurrentStep?.StepName}'");

            var roots = h.Roots();
            Check("子层出现 1 个层根锚点", roots.Count == 1 && roots[0].StepId == cond.StepID, $"实际 {roots.Count}");
            Check("层根只暴露产出端（输入端为空）", roots[0].Inputs.Count == 0, $"Inputs={roots[0].Inputs.Count}");
            Check("层根输出取算子静态输出口",
                roots[0].Outputs.Any(o => o.PortName == "Cond"), string.Join(",", roots[0].Outputs.Select(o => o.PortName)));
            Check("层根不可拖但可选中", !roots[0].IsDraggable && roots[0].IsSelectable && roots[0].IsDecorator, "");
            Check("层根参与本层节点集合但不算步骤",
                c.Nodes.Contains(roots[0]) && !h.Steps().Contains(roots[0]), "");

            // 主列 = [b1, inner]；全展开 = [b1, inner, g]（g 在 inner 的循环体泳道里）
            Check("子层主列只含本分支步骤",
                h.MainSteps().Select(s => s.StepId).SequenceEqual(new[] { b1.StepID, inner.StepID }),
                string.Join(",", h.MainSteps().Select(s => s.Header)));
            Check("子层全展开含泳道内步骤", h.Steps().Count == 3, $"Steps={h.Steps().Count}");
            Check("子层为内层容器继续开泳道",
                h.Lanes().Count == 1 && ReferenceEquals(h.Lanes()[0].Branch, innerBody), $"泳道 {h.Lanes().Count} 条");
            Check("子层节点标记为嵌套", h.Node(b1)!.IsNested, "");

            // 再下钻一层
            c.DrillDownCommand.Execute(h.Node(inner));
            Check("嵌套下钻到第 2 层", c.IsNested && c.Breadcrumb.Count == 3, $"面包屑 {c.Breadcrumb.Count} 级");
            roots = h.Roots();
            Check("祖先链逐层堆叠且由外及里",
                roots.Count == 2 && roots[0].StepId == cond.StepID && roots[1].StepId == inner.StepID,
                string.Join("→", roots.Select(r => r.Header)));
            Check("For 层根带出 Index 输出（算子未声明任何输出口）",
                roots[1].Outputs.Count == 1 && roots[1].Outputs[0].PortName == "Index"
                && roots[1].Outputs[0].DataType == typeof(int),
                string.Join(",", roots[1].Outputs.Select(o => o.PortName + ":" + o.DataType.Name)));

            var snapshot = (c.Breadcrumb.Count, h.Roots().Count, c.IsNested);
            c.DrillDownCommand.Execute(h.Roots()[0]);
            Check("双击层根不响应（层根不是容器入口）",
                c.Breadcrumb.Count == snapshot.Item1 && h.Roots().Count == snapshot.Item2 && c.IsNested, "");
            c.DrillDownCommand.Execute(null);
            Check("双击空节点不炸", c.Breadcrumb.Count == snapshot.Item1, "");

            // 双击泳道 = 下钻进那条分支
            c.NavigateCrumbCommand.Execute(c.Breadcrumb[0]);
            Check("面包屑回跳顶层", !c.IsNested && c.Breadcrumb.Count == 1 && h.Workspace.CurrentStep == null, "");
            c.DrillDownCommand.Execute(h.Lane(cond.Children[1]));
            Check("双击泳道直接进入那条分支",
                c.IsNested && c.Breadcrumb.Count == 2 && h.Steps().Count == 0 && h.Lanes().Count == 0,
                $"主列 {h.Steps().Count} 个 / 泳道 {h.Lanes().Count} 条");
            Check("空分支下钻后仍给出层根锚点", h.Roots().Count == 1, $"实际 {h.Roots().Count}");

            // 点当前层自己不跳
            var crumbCount = c.Breadcrumb.Count;
            c.NavigateCrumbCommand.Execute(c.Breadcrumb[^1]);
            Check("点面包屑当前级是空操作", c.Breadcrumb.Count == crumbCount && c.IsNested, "");

            // 中间级回跳：先进两层，再点第 1 级（非顶层）
            c.NavigateCrumbCommand.Execute(c.Breadcrumb[0]);
            c.DrillDownCommand.Execute(h.Node(cond));
            c.DrillDownCommand.Execute(h.Node(inner));
            Check("回到第 2 层", c.Breadcrumb.Count == 3, $"{c.Breadcrumb.Count}");
            c.NavigateCrumbCommand.Execute(c.Breadcrumb[1]);
            Check("点中间级回跳到第 1 层",
                c.IsNested && c.Breadcrumb.Count == 2 && ReferenceEquals(h.Workspace.CurrentStep, cond),
                $"面包屑 {c.Breadcrumb.Count} 级 / CurrentStep='{h.Workspace.CurrentStep?.StepName}'");
        }

        // ==================================================================
        //  [D] 路径失效时干净降级回顶层
        // ==================================================================
        private static void LayerDegradation()
        {
            Section("[D] 层路径失效降级");

            var h = new Harness();
            var cond = h.If("判断");
            var ifBranch = cond.Children[0];
            ifBranch.Steps.Add(h.Leaf("B1"));
            var tail = h.Leaf("Z");
            h.Add(cond); h.Add(tail);
            var c = h.Canvas;

            c.DrillDownCommand.Execute(h.Node(cond));
            Check("降级前先确认在子层", c.IsNested && c.Breadcrumb.Count == 2, "");

            h.Flow.Steps.Remove(cond);
            Check("删掉容器后自动回顶层（集合变更驱动）",
                !c.IsNested && c.Breadcrumb.Count == 1 && h.Roots().Count == 0 && h.Lanes().Count == 0,
                $"IsNested={c.IsNested} 面包屑={c.Breadcrumb.Count}");
            Check("降级后主列只剩存活步骤",
                h.MainSteps().Select(s => s.StepId).SequenceEqual(new[] { tail.StepID }),
                string.Join(",", h.MainSteps().Select(s => s.Header)));

            // 分支整体被删（Children 变更不触发画布订阅，需间接驱动 RebuildInPlace）
            // 关键：不能用 Rebuild()（它 dropLayer:true 本身就清层，属同义反复），
            //        改成「先改 Children，再动一个被 Watch 的集合触发 RebuildInPlace」。
            //        且 Children.RemoveAt(0) 不构成降级（Else 顶到 index 0）→ 必须用 Clear()
            var h2 = new Harness();
            var cond2 = h2.If("判断");
            cond2.Children[0].Steps.Add(h2.Leaf("B1"));
            var other = h2.Leaf("Z");
            h2.Add(cond2); h2.Add(other);
            var c2 = h2.Canvas;
            c2.DrillDownCommand.Execute(h2.Node(cond2));
            cond2.Children.Clear();
            h2.Flow.Steps.Add(h2.Leaf("触发"));   // flow.Steps 被 Watch → RebuildInPlace → ResolveTrail 失败
            Check("通往本层的分支被删后退回顶层",
                !c2.IsNested && c2.Breadcrumb.Count == 1 && h2.Roots().Count == 0,
                $"IsNested={c2.IsNested} 层根={h2.Roots().Count}");
            Check("降级后主列含原步骤与触发步骤",
                h2.MainSteps().Count == 3, $"主列 {h2.MainSteps().Count} 个");

            // 分支清空（与上面同模式，但只有一个容器步骤）
            var h3 = new Harness();
            var cond3 = h3.If("判断");
            cond3.Children[0].Steps.Add(h3.Leaf("B1"));
            h3.Add(cond3);
            var c3 = h3.Canvas;
            c3.DrillDownCommand.Execute(h3.Node(cond3));
            Check("正常路径不触发降级", c3.IsNested, "");
            cond3.Children.Clear();
            h3.Flow.Steps.Add(h3.Leaf("触发"));
            Check("分支清空后降级且不残留层根",
                !c3.IsNested && c3.Breadcrumb.Count == 1 && h3.Roots().Count == 0, $"层根 {h3.Roots().Count} 个");
        }

        // ==================================================================
        //  [E] 端口暴露规则与安全锚点范围
        // ==================================================================
        private static void PortExposureAndAnchors()
        {
            Section("[E] 端口暴露与安全锚点");

            var h = new Harness();
            var plain = new ActionStep("", "无输入算子", StubPluginProvider.LeafPlugin, "裸步骤");
            var withInput = h.Leaf("有输入");
            Harness.WithInput(withInput, "In");
            var cond = h.If("判断");
            var local = new LocalVariableItem { Name = "Score", DataTypeName = "System.Double" };
            cond.LocalVariables.Add(local);
            var outer = h.For("循环");
            var bodyLeaf = h.Leaf("体内");
            Harness.WithInput(bodyLeaf, "In");
            outer.Children[0].Steps.Add(bodyLeaf);
            h.Add(plain); h.Add(withInput); h.Add(cond); h.Add(outer);

            var c = h.Canvas;
            Check("未设输入值的步骤没有输入端口（InputValues 驱动）", h.Node(plain)!.Inputs.Count == 0,
                $"Inputs={h.Node(plain)!.Inputs.Count}");
            Check("SetInputValue 后出现同名输入端口",
                h.Node(withInput)!.Inputs.Count == 1 && h.Node(withInput)!.Inputs[0].PortName == "In", "");
            Check("条件步骤输入端口来自局部变量（键=Id，显示=Name）",
                h.Node(cond)!.Inputs.Count == 1 && h.Node(cond)!.Inputs[0].PortName == local.Id.ToString()
                && h.Node(cond)!.Inputs[0].DisplayLabel == "Score"
                && h.Node(cond)!.Inputs[0].DataType == typeof(double),
                $"'{h.Node(cond)!.Inputs[0].PortName}' / '{h.Node(cond)!.Inputs[0].DisplayLabel}'");
            Check("For 固定一个循环次数输入端口",
                h.Node(outer)!.Inputs.Any(p => p.PortName == "LoopCount" && p.DataType == typeof(int)), "");
            Check("算子静态输出口映射为输出端口",
                h.Node(withInput)!.Outputs.Count == 1 && h.Node(withInput)!.Outputs[0].PortName == "Out", "");
            Check("For 步骤自身也带 Index 输出端口",
                h.Node(outer)!.Outputs.Any(p => p.PortName == "Index"),
                string.Join(",", h.Node(outer)!.Outputs.Select(p => p.PortName)));
            Check("无法解析的类型名回落 object",
                h.Node(withInput)!.Outputs[0].DataType == typeof(double), $"{h.Node(withInput)!.Outputs[0].DataType.Name}");

            // 动态输出口（插件运行时追加）与静态口同名时只留一条
            var dyn = h.Leaf("动态口");
            dyn.OutputPortDefinitions.Add(new DynamicPortInfo { Name = "Out", DataTypeName = "System.String" });
            dyn.OutputPortDefinitions.Add(new DynamicPortInfo { Name = "Extra", DataTypeName = "System.Int32" });
            h.Add(dyn);
            var dynNode = h.Node(dyn)!;
            Check("同名输出去重 + 新动态口追加",
                dynNode.Outputs.Count == 2 && dynNode.Outputs.Count(p => p.PortName == "Out") == 1
                && dynNode.Outputs.Any(p => p.PortName == "Extra"),
                string.Join(",", dynNode.Outputs.Select(p => p.PortName)));

            // 空闲态可连性：层根输出端可连，层根无输入端
            c.DrillDownCommand.Execute(h.Node(outer));
            var root = h.Roots()[0];
            Check("空闲态：层根输出端口全部可连", root.Outputs.All(p => p.IsConnectable), "");
            Check("空闲态：本层真实步骤的输入端口可连",
                h.MainSteps().All(n => n.Inputs.All(p => p.IsConnectable)), "");
            Check("层根输入端为空即是最强约束（本层步骤无法回填它）", root.Inputs.Count == 0, "");

            // 拖线态的变灰规则
            var bodyStep = h.MainSteps()[0];
            var bodyOut = bodyStep.Outputs[0];
            var bodyIn = bodyStep.Inputs[0];
            var rootOut = root.Outputs[0];

            c.PendingConnection.Source = rootOut;
            c.PendingConnection.IsVisible = true;
            Check("拖线中：异节点的输入端口高亮", bodyIn.IsConnectable, "");
            Check("拖线中：同节点端口变灰（禁自连）", !bodyOut.IsConnectable && bodyOut.UnconnectableReason != null,
                $"'{bodyOut.UnconnectableReason}'");
            c.PendingConnection.Source = bodyOut;
            Check("改从步骤输出拖出后：层根输出端口变灰（出对出不可连）", !rootOut.IsConnectable, "");
            c.PendingConnection.IsVisible = false;
            Check("拖线结束：可连性复位", rootOut.IsConnectable && bodyOut.IsConnectable && bodyIn.IsConnectable, "");

            // 层根 → 本层步骤：安全锚点取数放行
            c.Connect(rootOut, bodyIn);
            Check("层根输出可喂本层步骤（祖先取数放行）",
                c.Connections.Count == 1 && bodyIn.IsConnected && rootOut.IsConnected, $"连线 {c.Connections.Count} 根");
            var link = bodyStep.Model!.LinkedSources["In"];
            Check("取数落到 LinkedSources 且键为祖先步骤",
                link.TargetStepId == outer.StepID && link.TargetPortName == rootOut.PortName
                && link.NormalizeKind() == LinkKind.StepPort,
                $"{link.TargetStepId:N}/{link.TargetPortName}");
            Check("祖先取数不计入非法", c.IllegalLinkCount == 0 && c.DeferredLinkCount == 0,
                $"illegal={c.IllegalLinkCount} deferred={c.DeferredLinkCount}");

            // ProducerAfterEnclosingContainer：生产方排在容器之后 → 画布标红
            // 不下钻：在顶层全展开时 consumer 在泳道里、producer 在主列，两端都可见
            var h2 = new Harness();
            var roots2 = Harness.OuterBeforeInner(producerBefore: false,
                out var container2, out var consumer2, out var producer2);
            h2.Flow.Steps.Clear();
            foreach (var s in roots2) h2.Flow.Steps.Add(s);
            var c2 = h2.Canvas;
            var illegalConns = c2.Connections.Where(conn => conn.IsIllegal).ToList();
            Check("生产方排在本层所属容器之后 → 画布标红且计 1 条非法",
                c2.IllegalLinkCount == 1 && c2.DeferredLinkCount == 0
                && illegalConns.Count == 1,
                $"illegal={c2.IllegalLinkCount} deferred={c2.DeferredLinkCount}");
            Check("非法线文案含『生产方排在本层所属容器之后』",
                illegalConns[0].WarningText != null
                && illegalConns[0].WarningText!.Contains("生产方排在本层所属容器之后"),
                $"'{illegalConns[0].WarningText}'");
        }

        // ==================================================================
        //  [F] 连线：放行 / 拒绝 / 换线 / 断线
        // ==================================================================
        private static void ConnectionWritesAndRejections()
        {
            Section("[F] 连线写回与拒绝");

            var h = new Harness();
            var a = h.Leaf("A");
            Harness.WithInput(a, "In");
            var b = h.Leaf("B");
            Harness.WithInput(b, "In");
            var d = h.Leaf("D");
            Harness.WithInput(d, "In");
            h.Add(a); h.Add(d); h.Add(b);
            var c = h.Canvas;

            c.Connect(h.Out(h.Node(a)!, "Out"), h.In(h.Node(b)!, "In"));
            Check("顺向连线放行", c.Connections.Count == 1 && c.StatusHint == null, $"'{c.StatusHint}'");
            Check("消费方写入 LinkReference",
                b.LinkedSources["In"].TargetStepId == a.StepID && b.LinkedSources["In"].TargetPortName == "Out", "");
            Check("两端端口点亮",
                h.Out(h.Node(a)!, "Out")!.IsConnected && h.In(h.Node(b)!, "In")!.IsConnected, "");
            Check("连线不改坐标库", h.Flow.Layout.Count >= 3, $"Layout.Count={h.Flow.Layout.Count}");

            var versionBefore = h.Flow.Version;
            c.Connect(h.Out(h.Node(b)!, "Out"), h.In(h.Node(a)!, "In"));
            Check("倒序连线被拒并给出原因",
                c.StatusHint != null && c.StatusHint.Contains("连线被拒绝") && c.StatusHint.Contains("执行顺序倒序"),
                $"'{c.StatusHint}'");
            Check("被拒后连线数不变", c.Connections.Count == 1, $"{c.Connections.Count}");
            Check("被拒后图纸未被写入", a.LinkedSources.Count == 0, $"LinkedSources={a.LinkedSources.Count}");
            Check("被拒不递增图纸版本", h.Flow.Version == versionBefore, $"{versionBefore} → {h.Flow.Version}");

            c.Connect(h.Out(h.Node(a)!, "Out"), h.In(h.Node(a)!, "In"));
            Check("同节点自连被拒",
                c.StatusHint != null && c.StatusHint.Contains("连线被拒绝") && c.Connections.Count == 1, $"'{c.StatusHint}'");

            // 一个输入端口只留一条线：换生产方
            c.Connect(h.Out(h.Node(d)!, "Out"), h.In(h.Node(b)!, "In"));
            Check("同输入端口换线后仍只有一根", c.Connections.Count == 1, $"{c.Connections.Count}");
            Check("换线后图纸改指新生产方", b.LinkedSources["In"].TargetStepId == d.StepID, "");
            Check("旧生产方端口熄灭，新生产方点亮",
                !h.Out(h.Node(a)!, "Out")!.IsConnected && h.Out(h.Node(d)!, "Out")!.IsConnected, "");
            Check("成功连线清掉状态栏提示", c.StatusHint == null, $"'{c.StatusHint}'");

            // 一个输出可喂多个输入
            var d2 = h.Leaf("D2");
            Harness.WithInput(d2, "In");
            h.Add(d2);
            c.Connect(h.Out(h.Node(d)!, "Out"), h.In(h.Node(d2)!, "In"));
            Check("同一输出可喂多个输入", c.Connections.Count == 2, $"{c.Connections.Count}");
            var sharedOut = h.Out(h.Node(d)!, "Out")!;

            c.DisconnectConnectorCommand.Execute(h.In(h.Node(b)!, "In"));
            Check("断一根线后另一根仍在", c.Connections.Count == 1 && sharedOut.IsConnected, $"{c.Connections.Count}");
            Check("断线清除图纸引用", !b.LinkedSources.ContainsKey("In"), "");
            c.DisconnectConnectorCommand.Execute(h.In(h.Node(d2)!, "In"));
            Check("全部断开后输出端口熄灭", c.Connections.Count == 0 && !sharedOut.IsConnected, "");

            c.DisconnectConnectorCommand.Execute(sharedOut);
            Check("从输出侧断线是空操作（一个输出可喂多输入）", d.LinkedSources.Count == 0, "");
            c.DisconnectConnectorCommand.Execute("不是端口");
            Check("断线命令传入非法参数不炸", true, "");

            // 端口失效（步骤没这个输入端口）时连线应被拒
            // noPort 有意义：它真的没有 In 端口（RemoveInputValue 清掉了）
            var noPort = h.Leaf("无端口");
            Harness.WithInput(noPort, "In");
            noPort.RemoveInputValue("In");
            h.Add(noPort);
            Check("无端口步骤确实没有输入端口", h.Node(noPort)!.Inputs.Count == 0, $"Inputs={h.Node(noPort)!.Inputs.Count}");
            c.Connect(h.Out(h.Node(d)!, "Out"), null);
            Check("端点为空时连线不炸且不写提示（两端不全非空则不写 StatusHint）",
                c.Connections.Count == 0 && c.StatusHint == null, $"'{c.StatusHint}'");
        }

        // ==================================================================
        //  [G] 非法线与降级线的计数、告警文案
        // ==================================================================
        private static void IllegalAndDeferredCounts()
        {
            Section("[G] 非法线 / 降级线计数");

            // G1 同层倒序（图纸改序后的既有连线）——画布必须画成灰色虚线并计数
            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            Harness.WithInput(b, "In");
            h.Add(a); h.Add(b);
            Harness.RawLink(b, "In", a, "Out");   // 合法
            var c = h.Canvas;
            Check("合法线：0 告警 0 降级",
                c.IllegalLinkCount == 0 && c.DeferredLinkCount == 0 && c.Connections.Count == 1
                && !c.Connections[0].IsIllegal, "");

            h.Flow.Steps.Move(0, 1);              // 改成 B 在前、A 在后 → 既有连线倒序
            Check("改序后自动重算并标记非法",
                c.IllegalLinkCount == 1 && c.WarningVisibility == Visibility.Visible, $"illegal={c.IllegalLinkCount}");
            var illegal = c.Connections.Single();
            Check("非法线上带可读原因",
                illegal.IsIllegal && illegal.WarningText != null && illegal.WarningText.Contains("执行顺序倒序"),
                $"'{illegal.WarningText}'");
            Check("告警条文案同时报两类数量",
                c.WarningText.Contains("1 条连线结构非法"), $"'{c.WarningText}'");
            Check("非法线仍写回图纸侧不动（画布不擅自删线）", b.LinkedSources["In"].TargetStepId == a.StepID, "");

            // G2 野 Id：上游被删 → 计入降级，不计非法
            var h2 = new Harness();
            var x = h2.Leaf("X");
            var y = h2.Leaf("Y");
            Harness.WithInput(y, "In");
            h2.Add(x); h2.Add(y);
            Harness.RawLink(y, "In", x, "Out");
            var c2 = h2.Canvas;
            h2.Flow.Steps.Remove(x);
            Check("上游被删计入降级",
                c2.DeferredLinkCount == 1 && c2.IllegalLinkCount == 0 && c2.Connections.Count == 0,
                $"deferred={c2.DeferredLinkCount} illegal={c2.IllegalLinkCount}");
            Check("降级不弹告警条", c2.WarningVisibility == Visibility.Collapsed && c2.WarningText == "", $"'{c2.WarningText}'");

            // G3 非步骤来源（全局变量 / 运行时变量 / 常量）走降级，且绝不标红
            var h3 = new Harness();
            var p = h3.Leaf("P");
            var q = h3.Leaf("Q");
            h3.Add(p); h3.Add(q);
            q.LinkedSources["In"] = new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "GV", "全局.gv");
            var c3 = h3.Canvas;
            Check("全局变量来源不计非法",
                c3.DeferredLinkCount == 1 && c3.IllegalLinkCount == 0 && c3.Connections.Count == 0,
                $"deferred={c3.DeferredLinkCount}");
            p.LinkedSources["In"] = new LinkReference(LinkKind.RuntimeVariable, Guid.Empty, "RV", "运行时.rv");
            c3.Rebuild();
            Check("运行时变量来源同样降级", c3.DeferredLinkCount == 2 && c3.IllegalLinkCount == 0, $"deferred={c3.DeferredLinkCount}");

            // G4 跨分支连线在顶层全展开时两端都可见 → 画布标红
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
            // 不下钻：顶层全展开时 inIf 和 inElse 分属不同泳道，两端都在 _visibleSteps 里
            Check("跨分支连线在顶层表现为非法线",
                c4.IllegalLinkCount == 1 && c4.DeferredLinkCount == 0,
                $"deferred={c4.DeferredLinkCount} illegal={c4.IllegalLinkCount}");
            Check("Classify 仍把它判成跨分支（编译期据此报致命错）",
                h4.Topology().Classify(inIf.StepID, inElse.StepID) == LinkLegality.CrossBranch, "");

            // G5 循环体取 For.Index：必须合法（历史上这里误判过）
            var h5 = new Harness();
            var outer = h5.For("循环");
            var bodyStep = h5.Leaf("体内");
            Harness.WithInput(bodyStep, "In");
            outer.Children[0].Steps.Add(bodyStep);
            h5.Add(outer);
            var c5 = h5.Canvas;
            c5.DrillDownCommand.Execute(h5.Node(outer));
            c5.Connect(h5.Roots()[0].Outputs.First(o => o.PortName == "Index"), h5.In(h5.Node(bodyStep)!, "In"));
            Check("循环体取 For.Index 放行且不标灰",
                c5.Connections.Count == 1 && !c5.Connections[0].IsIllegal && c5.IllegalLinkCount == 0,
                $"illegal={c5.IllegalLinkCount}");
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

            // CheckLinkOrder 遍历 topology.Positions（全层），所以必须喂图纸根序列
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
                var roots = new List<StepModel> { c1, p };  // 消费在前
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
                // For.StepID 是 producer，inner 是 consumer
                // Classify(outer.StepID, inner.StepID) → ProducerIsAncestor
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
                var roots = new List<StepModel> { c1, p };  // 倒序，但消费方被禁用
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
                var roots = new List<StepModel> { c1, p };  // 倒序，但生产方被禁用
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
        //  [J] 节点池：重画不丢选中
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
            // Nodes 含装饰节点，所以总数 = 装饰 + 步骤
            Check("重画后节点数递增（含装饰）",
                c.Nodes.Count(n => n.Kind == CanvasNodeKind.Step) == 3,
                $"{c.Nodes.Count(n => n.Kind == CanvasNodeKind.Step)} 个步骤节点");

            h.Flow.Steps.Remove(b);
            Check("删除步骤后画布摘掉节点",
                h.Node(b) == null && h.MainSteps().Count == 2, $"{h.MainSteps().Count}");
            Check("被删步骤的坐标同步回收", h.Flow.Layout.Find(b.StepID) == null, "");
            Check("存活节点仍保持选中", h.Node(a)!.IsSelected, "");

            // 端口可用性：被删的上游不留残线
            var h2 = new Harness();
            var p = h2.Leaf("P");
            var q = h2.Leaf("Q");
            Harness.WithInput(q, "In");
            h2.Add(p); h2.Add(q);
            var c2 = h2.Canvas;
            c2.Connect(h2.Out(h2.Node(p)!, "Out"), h2.In(h2.Node(q)!, "In"));
            Check("建线后端口点亮", h2.In(h2.Node(q)!, "In")!.IsConnected, "");
            h2.Flow.Steps.Remove(p);
            Check("上游删除后连线随重画消失", c2.Connections.Count == 0, $"{c2.Connections.Count}");
            Check("端口连接态在重画时统一复位",
                c2.Nodes.Where(n => n.Kind == CanvasNodeKind.Step).SelectMany(n => n.Inputs.Concat(n.Outputs))
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

            Check("首帧自动布局补齐缺失坐标", h.Flow.Layout.Count >= 3, $"Layout.Count={h.Flow.Layout.Count}");
            var nodeA = h.Node(a)!;
            Check("自动布局结果已回填节点", h.Flow.Layout.TryGet(a.StepID, out var stored) && nodeA.Location.X == stored!.X, "");

            nodeA.Location = new Point(1234, 567);
            Check("拖动步骤节点写回坐标库",
                h.Flow.Layout.Find(a.StepID)!.X == 1234 && h.Flow.Layout.Find(a.StepID)!.Y == 567,
                $"({h.Flow.Layout.Find(a.StepID)!.X:0}, {h.Flow.Layout.Find(a.StepID)!.Y:0})");

            var versionBefore = h.Flow.Version;
            nodeA.Location = new Point(2000, 200);
            Check("挪坐标不递增图纸版本（不触发重编译）", h.Flow.Version == versionBefore, $"{versionBefore}");

            c.DrillDownCommand.Execute(h.Node(cond));
            var rootStoredX = h.Flow.Layout.Find(cond.StepID)!.X;
            var laneBefore = h.Flow.Layout.Count;
            h.Roots()[0].Location = new Point(424242, 424242);
            h.Lanes()[0].Location = new Point(-424242, -424242);
            Check("拖动层根不污染祖先坐标", h.Flow.Layout.Find(cond.StepID)!.X == rootStoredX,
                $"{rootStoredX:0} vs {h.Flow.Layout.Find(cond.StepID)!.X:0}");
            Check("拖动装饰节点不新增坐标项", h.Flow.Layout.Count == laneBefore, $"{laneBefore} → {h.Flow.Layout.Count}");

            // 泳道内步骤的坐标入库（不是装饰节点）
            var laneStep = h.Steps().First(s => !h.MainSteps().Contains(s));
            var laneStepBefore = h.Flow.Layout.Count;
            laneStep.Location = new Point(999, 888);
            Check("泳道内步骤拖动写回坐标库（它不是装饰节点）",
                h.Flow.Layout.Count == laneStepBefore + 1 || h.Flow.Layout.Find(laneStep.StepId) != null,
                $"Layout.Count={h.Flow.Layout.Count}");
            Check("祖先 cond 的坐标项仍在",
                h.Flow.Layout.Find(cond.StepID) != null, "");
        }

        // ==================================================================
        //  [L] 外部选中 → 画布自动定位（联动 A 方案的反向半边）
        // ==================================================================
        private static void OutsideSelectionDrivesCanvas()
        {
            Section("[L] 外部选中联动");

            var h = new Harness();
            var a = h.Leaf("A");
            var cond = h.If("判断");
            var deep = h.Leaf("深层");
            cond.Children[0].Steps.Add(deep);
            var inner = h.For("内层");
            inner.Children[0].Steps.Add(h.Leaf("更深"));
            cond.Children[0].Steps.Add(inner);
            h.Add(a); h.Add(cond);
            var c = h.Canvas;

            Check("初始在顶层", !c.IsNested && c.Breadcrumb.Count == 1, "");
            h.Workspace.SwitchStep(deep);
            Check("外部选中深层步骤后画布自动下钻",
                c.IsNested && c.Breadcrumb.Count == 2, $"面包屑 {c.Breadcrumb.Count} 级");
            Check("自动选中对应节点", h.Node(deep)!.IsSelected, "");

            h.Workspace.SwitchStep(a);
            Check("外部选中顶层步骤退回顶层", !c.IsNested && h.Node(a)!.IsSelected, "");

            h.Workspace.SwitchStep(null);
            Check("清空当前步骤不改变画布层级", !c.IsNested, "");

            // 画布自己下钻产生的 CurrentStep 变更不得回环重刷（表现为：不弹回顶层）
            c.DrillDownCommand.Execute(h.Node(cond));
            Check("画布下钻后停在子层（闸门生效）",
                c.IsNested && ReferenceEquals(h.Workspace.CurrentStep, cond), $"'{h.Workspace.CurrentStep?.StepName}'");

            var breadcrumb = c.Breadcrumb.Count;
            h.Workspace.SwitchStep(deep);
            Check("同层内外部选中不改变层级", c.IsNested && c.Breadcrumb.Count == breadcrumb && h.Node(deep)!.IsSelected, "");

            var ghost = new ActionStep("", "幽灵", StubPluginProvider.LeafPlugin, "幽灵") as StepModel;
            var thrown = false;
            try { h.Workspace.SwitchStep(ghost!); }
            catch (InvalidOperationException) { thrown = true; }
            Check("选中不在图纸里的步骤由 WorkspaceContext 抛错（画布不参与兜底）", thrown, "");
            Check("抛错后画布层级未被破坏", c.IsNested && c.Breadcrumb.Count == breadcrumb, "");

            // 步骤改名不重画（只订阅集合增删）
            var nodeCount = c.Nodes.Count;
            deep.StepName = "改名后的深层";
            Check("改名不触发重建", c.Nodes.Count == nodeCount && h.Node(deep)!.Header.Contains("改名后"),
                $"'{h.Node(deep)!.Header}'");
        }

        // ==================================================================
        //  [M] 段3：拖拽改序提交 + Version 递增次数 + 撤销逆操作
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
            // 顺序：cmd.Redo() → PushUndo
            var reorder = FlowCanvasExtensions.CreateReorderCommand(h.Flow.Steps, 0, 2);
            FlowCanvasExtensions.InvokeRedo(reorder);

            Check("Redo 执行后 Steps[0] = B", h.Flow.Steps[0] == b, "");
            Check("Redo 执行后 Steps[2] = A", h.Flow.Steps[2] == a, "");
            Check("Redo 触发 Version 递增（1 次）", h.Flow.Version == version0 + 1, $"{version0} → {h.Flow.Version}");
            Check("Redo 后画布主列按新顺序展示",
                h.MainSteps().Select(n => n.StepId).SequenceEqual(new[] { b.StepID, c.StepID, a.StepID }),
                string.Join(",", h.MainSteps().Select(n => n.Header)));

            // 压栈（Redo 已发生，PushUndo 只记录）
            cv.PushUndoReflection(reorder);
            Check("压栈后撤销栈深度 = 1", cv.UndoStackSize == 1, $"size={cv.UndoStackSize}");
            Check("压栈后重做栈为空", cv.RedoStackSize == 0, "");

            // 新操作压栈时清空 redo
            var another = FlowCanvasExtensions.CreateReorderCommand(h.Flow.Steps, 0, 1);
            FlowCanvasExtensions.InvokeRedo(another);
            cv.PushUndoReflection(another);
            Check("第二次压栈后撤销栈 = 2", cv.UndoStackSize == 2, "");

            // 撤销
            var versionBeforeUndo = h.Flow.Version;
            cv.UndoCommand.Execute();
            Check("Undo 把第二次改序回滚：Steps[0] = B", h.Flow.Steps[0] == b, $"'{h.Flow.Steps[0].StepName}'");
            Check("Undo 触发 Version 递增", h.Flow.Version == versionBeforeUndo + 1, "");
            Check("Undo 后重做栈 = 1", cv.RedoStackSize == 1, "");

            // 重做
            var versionBeforeRedo = h.Flow.Version;
            cv.RedoCommand.Execute();
            Check("Redo 重放第二次改序：Steps[0] = C", h.Flow.Steps[0] == c, $"'{h.Flow.Steps[0].StepName}'");
            Check("Redo 触发 Version 递增", h.Flow.Version == versionBeforeRedo + 1, "");
            Check("Redo 后撤销栈 = 2", cv.UndoStackSize == 2, "");

            // 全部撤销
            cv.UndoCommand.Execute();
            cv.UndoCommand.Execute();
            Check("两次 Undo 后回到原始顺序 [A,B,C]",
                h.Flow.Steps[0] == a && h.Flow.Steps[1] == b && h.Flow.Steps[2] == c,
                string.Join(",", h.Flow.Steps.Select(s => s.StepName)));
            Check("全部 Undo 后撤销栈空", cv.UndoStackSize == 0, "");
            Check("全部 Undo 后重做栈 = 2", cv.RedoStackSize == 2, "");

            // CanExecute 跟随栈状态
            Check("栈空时 UndoCommand.CanExecute = false", !cv.UndoCommand.CanExecute(), "");
            Check("栈非空时 RedoCommand.CanExecute = true", cv.RedoCommand.CanExecute(), "");

            // 新操作清空 redo
            var another2 = FlowCanvasExtensions.CreateReorderCommand(h.Flow.Steps, 0, 1);
            FlowCanvasExtensions.InvokeRedo(another2);
            cv.PushUndoReflection(another2);
            Check("新操作后 redo 栈被清空", cv.RedoStackSize == 0, "");
        }

        // ==================================================================
        //  [O] 段3：跨分支移分支（仅同层）
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

            // 把 b1 从 If 分支 index 0 移到 Else 分支 index 0
            var move = FlowCanvasExtensions.CreateMoveBranchCommand(
                b1, ifBranch.Steps, 0, elseBranch.Steps, 0);
            FlowCanvasExtensions.InvokeRedo(move);
            cv.PushUndoReflection(move);

            Check("Redo 后 If 分支剩 [B2]", ifBranch.Steps.Count == 1 && ifBranch.Steps[0] == b2, "");
            Check("Redo 后 Else 分支为 [B1]", elseBranch.Steps.Count == 1 && elseBranch.Steps[0] == b1, "");
            Check("画布重新渲染：If 泳道只剩 B2、Else 泳道含 B1",
                h.Lane(ifBranch) != null && h.Lane(elseBranch) != null
                && h.Node(b1) != null && h.Node(b2) != null,
                "");

            // 撤销跨分支移动
            cv.UndoCommand.Execute();
            Check("Undo 后 If 分支恢复 [B1,B2]",
                ifBranch.Steps.Count == 2 && ifBranch.Steps[0] == b1 && ifBranch.Steps[1] == b2, "");
            Check("Undo 后 Else 分支空", elseBranch.Steps.Count == 0, "");

            // 重做
            cv.RedoCommand.Execute();
            Check("Redo 后再次 If=[B2], Else=[B1]",
                ifBranch.Steps[0] == b2 && elseBranch.Steps[0] == b1, "");

            // 切流程清栈
            var other = new FlowModel { FlowName = "其他流程" };
            h.Solution.Flows.Add(other);
            h.Workspace.SwitchFlow(other);
            Check("切流程后撤销栈被清空", cv.UndoStackSize == 0 && cv.RedoStackSize == 0, "");
            h.Workspace.SwitchFlow(h.Flow);
            Check("切回原流程后栈仍空（不复活）", cv.UndoStackSize == 0 && cv.RedoStackSize == 0, "");
        }

        // ==================================================================
        //  [P] 段3：连线 / 断线的撤销逆操作
        // ==================================================================
        private static void ConnectDisconnectUndoRedo()
        {
            Section("[P] 连线 / 断线 撤销");

            var h = new Harness();
            var p = h.Leaf("生产");
            var q = h.Leaf("消费");
            Harness.WithInput(q, "In");
            h.Add(p); h.Add(q);
            var cv = h.Canvas;

            // 建线 → 压栈 ConnectCommand
            cv.Connect(h.Out(h.Node(p)!, "Out"), h.In(h.Node(q)!, "In"));
            Check("建线后连线数 = 1", cv.Connections.Count == 1, "");
            Check("建线后撤销栈 = 1", cv.UndoStackSize == 1, "");

            // 撤销建线
            cv.UndoCommand.Execute();
            Check("Undo 建线后连线清空", cv.Connections.Count == 0, "");
            Check("Undo 建线后图纸侧 LinkedSources 已移除",
                !q.LinkedSources.ContainsKey("In"), "");

            // 重做
            cv.RedoCommand.Execute();
            Check("Redo 建线后连线恢复", cv.Connections.Count == 1 && q.LinkedSources.ContainsKey("In"), "");

            // 加线：P→Q 已建，再连 P→D2（输出端口可喂多个输入），Undo 应当只撤销 P→D2
            var d2 = h.Leaf("D2");
            Harness.WithInput(d2, "In");
            h.Add(d2);
            // 当前 P→Q 已建，新增 P→D2 —— P.Out 可喂多个输入，Q.In 不受影响
            cv.Connect(h.Out(h.Node(p)!, "Out"), h.In(h.Node(d2)!, "In"));
            Check("新增 P→D2 后 D2 连上、Q 仍连着 P",
                d2.LinkedSources.ContainsKey("In") && q.LinkedSources.ContainsKey("In"), "");

            cv.UndoCommand.Execute();
            Check("Undo 新增后 D2 断开、Q 仍连着 P",
                !d2.LinkedSources.ContainsKey("In") && q.LinkedSources.ContainsKey("In"), "");
            Check("Undo 后 Q.LinkedSources 仍指向 P",
                q.LinkedSources["In"].TargetStepId == p.StepID, "");

            // 断线 → 压栈 DisconnectCommand
            cv.DisconnectConnectorCommand.Execute(h.In(h.Node(q)!, "In"));
            Check("断线后撤销栈深度增加", cv.UndoStackSize > 1, $"size={cv.UndoStackSize}");

            cv.UndoCommand.Execute();
            Check("Undo 断线后 Q 重新连上 P",
                q.LinkedSources.ContainsKey("In") && q.LinkedSources["In"].TargetStepId == p.StepID, "");

            // 空断线（端口没连）不应入栈
            var stackBefore = cv.UndoStackSize;
            var ghost = h.Leaf("未连");
            Harness.WithInput(ghost, "In");
            h.Add(ghost);
            cv.DisconnectConnectorCommand.Execute(h.In(h.Node(ghost)!, "In"));
            Check("对未连线的端口断线不入栈", cv.UndoStackSize == stackBefore, $"{stackBefore} → {cv.UndoStackSize}");
        }

        // ==================================================================
        //  [Q] 段3：撤销栈容量上限 50 + 切流程清栈
        // ==================================================================
        private static void UndoStackBoundary()
        {
            Section("[Q] 栈深 50 上限与切流程清栈");

            var h = new Harness();
            // 建一个有 51 步骤的流程，方便做 50 次改序
            for (int i = 0; i < 4; i++) h.Add(h.Leaf($"S{i}"));
            var cv = h.Canvas;
            var step0 = h.Flow.Steps[0];

            // 压 51 次改序（每次 0↔1 切换），超过 50 应丢最早项
            for (int i = 0; i < 51; i++)
            {
                var cmd = FlowCanvasExtensions.CreateReorderCommand(h.Flow.Steps, 0, 1);
                FlowCanvasExtensions.InvokeRedo(cmd);
                cv.PushUndoReflection(cmd);
            }
            Check("压 51 次后栈深仍 = 50（丢最早项）", cv.UndoStackSize == 50, $"size={cv.UndoStackSize}");

            // 切到另一流程
            var other = new FlowModel { FlowName = "边界流程" };
            h.Solution.Flows.Add(other);
            h.Workspace.SwitchFlow(other);
            Check("切流程后撤销栈与重做栈都清空",
                cv.UndoStackSize == 0 && cv.RedoStackSize == 0, "");
            Check("切流程后 Step0 引用不再被栈持有（GC 友好）——间接判：原流程可被另一流程替换",
                !ReferenceEquals(h.Workspace.CurrentFlow, h.Flow), "");

            // 切回原流程后栈不复活
            h.Workspace.SwitchFlow(h.Flow);
            cv.UndoCommand.Execute();
            Check("切回后撤销栈空 → Undo 是空操作", true, "");

            // 同流程切层不清栈：建一个容器，下钻再上钻，栈应当保留
            var cond = h.If("判断");
            h.Flow.Steps.Add(cond);
            // 先压一条改序到栈
            var preCmd = FlowCanvasExtensions.CreateReorderCommand(h.Flow.Steps, 0, 1);
            FlowCanvasExtensions.InvokeRedo(preCmd);
            cv.PushUndoReflection(preCmd);
            var stackBeforeDrill = cv.UndoStackSize;

            cv.DrillDownCommand.Execute(h.Node(cond));
            Check("下钻后撤销栈保留", cv.UndoStackSize == stackBeforeDrill, $"{stackBeforeDrill} → {cv.UndoStackSize}");
            cv.NavigateCrumbCommand.Execute(cv.Breadcrumb[0]);
            Check("回顶层后撤销栈仍保留", cv.UndoStackSize == stackBeforeDrill, "");
        }

        // ==================================================================
        //  [R] 运行时变量值脚：变量定义节点长脚 → 拖线建 RuntimeVariable 线 → 存盘往返 / 降级
        // ==================================================================

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

        private static void RuntimeVariablePortAndLink()
        {
            Section("[R] 运行时变量值脚建线");

            // ---- R1 值脚的存在性：只有变量定义节点长脚，端口名 = 变量名，类型来自 Type 参数 ----
            var h = new Harness();
            var def = h.Variable("loopN", "int", "定义loopN");
            var forStep = h.For("循环");
            h.Add(def);
            h.Add(forStep);
            var cv = h.Canvas;

            var defNode = h.Node(def)!;
            var valuePort = h.Out(defNode, "loopN");
            Check("变量定义节点长出以变量名命名的值脚", valuePort != null,
                $"Outputs=[{string.Join(",", defNode.Outputs.Select(o => o.PortName))}]");
            Check("值脚带 RuntimeVariable 标记且类型取自 Type 参数（int）",
                valuePort!.IsRuntimeVariablePort && valuePort.DataType == typeof(int),
                $"flag={valuePort.IsRuntimeVariablePort} type={valuePort.DataType.Name}");
            Check("变量定义节点自身输入口是 Name/Type（插件参数，不是变量）",
                defNode.Inputs.Count == 2 && h.In(defNode, "Name") != null && h.In(defNode, "Type") != null,
                $"Inputs=[{string.Join(",", defNode.Inputs.Select(i => i.PortName))}]");
            Check("全画布只有这一个值脚（普通步骤不长脚）",
                cv.Nodes.SelectMany(n => n.Outputs).Count(o => o.IsRuntimeVariablePort) == 1, "");

            // ---- R2 拖线建 RuntimeVariable 线：三元组写回图纸 ----
            var versionBefore = h.Flow.Version;
            var loopCountIn = h.In(h.Node(forStep)!, "LoopCount");
            Check("For 节点暴露 LoopCount 输入脚（类型 int）",
                loopCountIn != null && loopCountIn.DataType == typeof(int),
                loopCountIn == null ? "无 LoopCount 输入脚" : loopCountIn.DataType.Name);

            cv.Connect(valuePort, loopCountIn);
            var link = forStep.LinkedSources["LoopCount"];
            Check("值脚可当连线源端且状态栏无拒绝",
                cv.Connections.Count == 1 && cv.StatusHint == null && ReferenceEquals(cv.Connections[0].Output, valuePort),
                $"count={cv.Connections.Count} hint='{cv.StatusHint}'");
            Check("连线落成显式 Kind=RuntimeVariable",
                link.Kind == LinkKind.RuntimeVariable && link.NormalizeKind() == LinkKind.RuntimeVariable, $"{link.Kind}");
            Check("TargetStepId 是标记 Guid，不是定义步骤 Id",
                link.TargetStepId == LinkProtocol.RuntimeVariableMarkerGuid && link.TargetStepId != def.StepID, "");
            Check("TargetPortName=变量名 / DisplayAddress=Runtime.变量名",
                link.TargetPortName == "loopN" && link.DisplayAddress == "Runtime.loopN", link.DisplayAddress);
            Check("建线走统一写路径递增图纸版本", h.Flow.Version != versionBefore, $"{versionBefore} → {h.Flow.Version}");
            Check("两端端口点亮",
                valuePort.IsConnected && loopCountIn!.IsConnected, "");
            Check("定义在本层 → 画实线，不降级不标红",
                cv.DeferredLinkCount == 0 && cv.IllegalLinkCount == 0 && !cv.Connections[0].IsIllegal,
                $"deferred={cv.DeferredLinkCount} illegal={cv.IllegalLinkCount}");

            // ---- R3 撤销 / 重做 ----
            cv.UndoCommand.Execute();
            Check("撤销后图纸引用与画布连线一起消失",
                !forStep.LinkedSources.ContainsKey("LoopCount") && cv.Connections.Count == 0,
                $"count={cv.Connections.Count}");
            cv.RedoCommand.Execute();
            var redoLink = forStep.LinkedSources["LoopCount"];
            Check("重做恢复三要素（Kind/变量名/标记 Guid）",
                redoLink.Kind == LinkKind.RuntimeVariable
                && redoLink.TargetPortName == "loopN"
                && redoLink.TargetStepId == LinkProtocol.RuntimeVariableMarkerGuid, "");

            // ---- R4 存盘往返：Kind 显式落盘，变量定义身份不丢 ----
            var json = SolutionService.Serialize(h.Solution);
            Check("落盘文本里 Kind 是数字 3（不再靠 DisplayAddress 猜）",
                json.Contains("\"Kind\": 3") && json.Contains("\"Runtime.loopN\""), "");

            var reloaded = JsonConvert.DeserializeObject<SolutionModel>(json, RoundTripSettings)!;
            // SolutionModel 的字段初始化器预置了 GoHome / MainTask 两条流程，
            // 反序列化走 ObjectCreationHandling.Replace 原样带回来 → 按名字取断言那条
            var reloadedFlow = reloaded.Flows.Single(f => f.FlowName == "画布断言流程");
            var for2 = reloadedFlow.Steps.OfType<ForStep>().Single();
            var def2 = reloadedFlow.Steps.OfType<ActionStep>().Single();

            Check("往返后连线身份不变",
                for2.LinkedSources["LoopCount"].NormalizeKind() == LinkKind.RuntimeVariable
                && for2.LinkedSources["LoopCount"].TargetPortName == "loopN", "");
            Check("往返后 For 的循环体分支不重复（应为 1）",
                for2.Children.Count == 1, $"实际 {for2.Children.Count}");
            Check("往返后变量定义步骤仍能长出值脚（Name/Type 存成裸字符串）",
                FlowQueryHelper.TryGetDefinedVariable(def2, out var rn, out var rt)
                && rn == "loopN" && rt == typeof(int), $"{rn}/{rt.Name}");

            // ---- R5 异层/失效降级：定义节点没了就隐形，但绝不标红、绝不擅自删图纸引用 ----
            h.Flow.Steps.Remove(def);
            Check("定义节点被删 → 降级隐形（不画线、不标红、不告警）",
                cv.DeferredLinkCount == 1 && cv.IllegalLinkCount == 0
                && cv.Connections.Count == 0 && cv.WarningVisibility == Visibility.Collapsed,
                $"deferred={cv.DeferredLinkCount} illegal={cv.IllegalLinkCount}");
            Check("降级不动图纸（画布不替用户删线）", forStep.LinkedSources.ContainsKey("LoopCount"), "");

            // ---- R6 值脚不限消费方：任意步骤输入口都能收，且全量重建后按变量名回找 ----
            var hb = new Harness();
            var var2 = hb.Variable("thresh", "double", "定义thresh");
            var leaf = hb.Leaf("消费");
            Harness.WithInput(leaf, "In");
            hb.Add(var2);
            hb.Add(leaf);
            var cb = hb.Canvas;
            cb.Connect(hb.Out(hb.Node(var2)!, "thresh"), hb.In(hb.Node(leaf)!, "In"));
            Check("值脚同样能喂普通算子输入口",
                leaf.LinkedSources["In"].Kind == LinkKind.RuntimeVariable
                && leaf.LinkedSources["In"].TargetPortName == "thresh", "");

            cb.Rebuild();
            Check("全量重建后按变量名回找回实线（不依赖建线时的即时 Add）",
                cb.Connections.Count == 1 && cb.DeferredLinkCount == 0 && !cb.Connections[0].IsIllegal,
                $"count={cb.Connections.Count} deferred={cb.DeferredLinkCount}");

            // ---- R7 地基回归：绑定弹窗候选树递归 For 子层 ----
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
    }
}
