using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.ViewModels;

namespace FlowCanvasChecks
{
    /// <summary>
    /// [YC] 画布分支编辑与落点防重叠（2026-10-23 用户五问的第二批修复）。
    ///
    /// 覆盖三件事，三个都是"改坏了不报错、只表现为界面上没反应"的类型：
    ///  ① **画布订阅容器 Children**：此前只订 Steps，流程栏加/删分支后画布收不到通知，
    ///     要手动折叠再展开才刷新。断言走"改 Children → 画布节点/泳道数立刻变"。
    ///  ② **画布右键加分支**：命令走 FlowBranchEditor（与流程栏同一份实现）——
    ///     断言四个入口的结构结果与流程栏完全一致（含 Else 去重、Case 插在默认分支前）。
    ///  ③ **落点防重叠**：新步骤插到中间后，后续兄弟必须被顺移让位，否则卡片互相盖住
    ///     （用户口径"落点落在已有算子上，产生错觉只有一个算子"）。
    ///
    /// 每条主判据都配了反向断言（该动的动、不该动的不动），避免"把逻辑改成永远顺移"这种绿。
    /// </summary>
    internal static class FlowBranchEditChecks
    {
        public static void Run()
        {
            Program.Section("[YC] 画布分支编辑与落点防重叠（Children 订阅 / 右键加分支 / 腾位）");

            ChildrenSubscriptionMakesCanvasRefresh();
            CanvasBranchCommands();
            DropMakesRoomForInserted();
        }

        // ==================================================================
        //  ① 容器 Children 变化 → 画布立刻重画（不需要折叠再展开）
        // ==================================================================
        private static void ChildrenSubscriptionMakesCanvasRefresh()
        {
            var h = new Harness();
            var par = h.Parallel("并行组");
            h.Add(par);
            var canvas = h.Canvas;   // 建画布 = 首次渲染并挂订阅

            int lanes0 = canvas.Nodes.Count(n => n.Kind == CanvasNodeKind.Lane);
            int branches0 = par.Children.Count;

            // 流程栏那条路径：直接改模型的 Children（不经过画布任何命令）
            par.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "分支 9" });

            int lanes1 = canvas.Nodes.Count(n => n.Kind == CanvasNodeKind.Lane);
            Program.Check("YC1 外部加分支（改 Children）→ 画布泳道立即多一条（不必折叠再展开）",
                lanes1 == lanes0 + 1,
                $"泳道 {lanes0} → {lanes1}，分支 {branches0} → {par.Children.Count}");

            // 反向：删分支同样要即时反映
            par.Children.RemoveAt(par.Children.Count - 1);
            int lanes2 = canvas.Nodes.Count(n => n.Kind == CanvasNodeKind.Lane);
            Program.Check("YC1 外部删分支 → 画布泳道立即少一条",
                lanes2 == lanes0,
                $"泳道 {lanes1} → {lanes2}");

            // 顶层 Steps 只被订阅一次（RebuildCore 的 _stepsChanged 已挂，Watch 必须跳过它）：
            // 双订阅的表现是"一次结构变更跑两遍全量重建"——功能上看不出来，只体现为性能。
            // 判据：一次顶层增删，Nodes 集合的 Clear/重置次数 = 1（多一次就是双订阅回来了）。
            var hTop = new Harness();
            hTop.Add(hTop.Leaf("顶层A"));
            var cvTop = hTop.Canvas;
            int renderCount = 0;
            cvTop.Nodes.CollectionChanged += (_, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                    renderCount++;
            };
            hTop.Flow.Steps.Add(hTop.Leaf("顶层B"));
            Program.Check("YC1 顶层 Steps 结构变更只触发一次全量重建（双订阅=每次拖入代价翻倍）",
                renderCount == 1,
                $"重建轮数={renderCount}（期望 1）");

            // If 容器：加一条分支后泳道也要跟上（不是只有并行分组管用）
            var h2 = new Harness();
            var cond = h2.If("判断");
            h2.Add(cond);
            var cv2 = h2.Canvas;
            int ifLanes0 = cv2.Nodes.Count(n => n.Kind == CanvasNodeKind.Lane);
            cond.Children.Add(new StepCollection { BranchType = BranchType.Else, StepName = "另一条" });
            int ifLanes1 = cv2.Nodes.Count(n => n.Kind == CanvasNodeKind.Lane);
            Program.Check("YC1 If 容器加分支 → 泳道立即多一条（订阅面覆盖全部容器，不只并行分组）",
                ifLanes1 == ifLanes0 + 1,
                $"泳道 {ifLanes0} → {ifLanes1}");
        }

        // ==================================================================
        //  ② 画布节点右键的分支结构命令（与流程栏同一份 FlowBranchEditor）
        // ==================================================================
        private static void CanvasBranchCommands()
        {
            // ---- 并行分组：加分支 / 上限 / 自动命名 ----
            var h = new Harness();
            var par = h.Parallel("并行组");
            h.Add(par);
            var canvas = h.Canvas;
            var node = h.Node(par)!;

            Program.Check("YC2 并行分组节点：三条类型判据只亮并行那条",
                node.IsParallelNode && !node.IsIfLikeNode && !node.IsCaseNode,
                $"par={node.IsParallelNode} if={node.IsIfLikeNode} case={node.IsCaseNode}");

            int n0 = par.Children.Count;
            node.AddBranchCommand!.Execute();
            Program.Check("YC2 画布「添加分支」→ Children +1（命令真的接上了，不是空壳）",
                par.Children.Count == n0 + 1,
                $"分支 {n0} → {par.Children.Count}");

            // 上限：加到 8 条后再点一次要拒绝，并且给状态栏回执（不许静默空跑）
            while (par.Children.Count < ParallelStep.RecommendedMaxBranches)
                node.AddBranchCommand.Execute();
            int atMax = par.Children.Count;
            canvas.StatusHint = null;
            node.AddBranchCommand.Execute();
            Program.Check("YC2 并行分支达上限 → 拒绝再添加且写状态栏回执（不静默）",
                par.Children.Count == atMax && (canvas.StatusHint ?? "").Contains("上限"),
                $"{par.Children.Count} 条，回执='{canvas.StatusHint}'");

            // ---- If 容器：ElseIf 插在兜底 Else 之前；Else 去重 ----
            var h2 = new Harness();
            var cond = h2.If("判断");
            h2.Add(cond);
            var cv2 = h2.Canvas;
            var ifNode = h2.Node(cond)!;

            Program.Check("YC2 If 容器节点：If 判据亮、并行/Case 不亮",
                ifNode.IsIfLikeNode && !ifNode.IsParallelNode && !ifNode.IsCaseNode,
                $"if={ifNode.IsIfLikeNode} par={ifNode.IsParallelNode} case={ifNode.IsCaseNode}");

            int ifBefore = cond.Children.Count;
            ifNode.AddElseBranchCommand!.Execute();
            bool elseDedup = cond.Children.Count(c => c.BranchType == BranchType.Else) == 1
                             && cond.Children.Count == ifBefore;
            Program.Check("YC2 If 已含 Else 分支 → 「添加 Else」被拒绝且不重复加（回执说明原因）",
                elseDedup && (cv2.StatusHint ?? "").Contains("已经包含"),
                $"Else 数={cond.Children.Count(c => c.BranchType == BranchType.Else)}，回执='{cv2.StatusHint}'");

            int beforeElseIf = cond.Children.Count;
            ifNode.AddElseIfBranchCommand!.Execute();
            int elseIndex = cond.Children.ToList().FindIndex(c => c.BranchType == BranchType.Else);
            int lastElseIfIndex = cond.Children.ToList().FindLastIndex(c => c.BranchType == BranchType.ElseIf);
            Program.Check("YC2 画布「添加 ElseIf」→ 插在兜底 Else 之前（Else 之后的分支编译器按不可达报错）",
                cond.Children.Count == beforeElseIf + 1 && lastElseIfIndex >= 0 && lastElseIfIndex < elseIndex,
                $"ElseIf@{lastElseIfIndex} Else@{elseIndex}");

            // ---- Case 容器：加分支插在默认分支之前 ----
            var h3 = new Harness();
            var caseStep = new CaseStep("\uE700", "分支匹配", "BuiltIn_Case", "分档");
            h3.Add(caseStep);
            var cv3 = h3.Canvas;
            var caseNode = h3.Node(caseStep)!;

            // CaseStep 的 PluginTypeName 是 BuiltIn_Case，ConditionStep.IsIfLike 判的是名字里有没有 "If"
            // （ConditionStep.cs:81），所以 CaseStep 的 IsIfLike 为 false —— Case 容器的右键菜单
            // **只出 Case 项**，不会同时冒出 ElseIf/Else。这条反向判据防的是"把 IsIfLike 改成
            // 内容嗅探（比如判 Children 里有 Case 分支）"之后 Case 菜单多出两条无效入口。
            Program.Check("YC2 Case 容器节点：Case 判据亮、If 判据不亮（Case 菜单不该同时出 ElseIf/Else）",
                caseNode.IsCaseNode && !caseNode.IsIfLikeNode,
                $"case={caseNode.IsCaseNode} if={caseNode.IsIfLikeNode}（期望 True/False）");

            int caseBefore = caseStep.Children.Count;
            caseNode.AddCaseBranchCommand!.Execute();
            int defIndex = caseStep.Children.ToList().FindIndex(c => c.BranchType == BranchType.Else);
            int lastCaseIndex = caseStep.Children.ToList().FindLastIndex(c => c.BranchType == BranchType.Case);
            Program.Check("YC2 画布「添加 Case」→ 插在默认分支之前且自动名不撞",
                caseStep.Children.Count == caseBefore + 1
                && lastCaseIndex < defIndex
                && caseStep.Children.Select(c => c.StepName).Distinct().Count() == caseStep.Children.Count,
                $"Case@{lastCaseIndex} 默认@{defIndex} 名字={string.Join("/", caseStep.Children.Select(c => c.StepName))}");

            // ---- 类型靶不对：静默空跑，不抛 ----
            var h4 = new Harness();
            var leaf = h4.Add(h4.Leaf("算子"));
            var cv4 = h4.Canvas;
            var leafNode = h4.Node(leaf)!;
            bool threw = false;
            try
            {
                leafNode.AddBranchCommand!.Execute();
                leafNode.AddElseBranchCommand!.Execute();
                leafNode.AddCaseBranchCommand!.Execute();
            }
            catch (Exception ex)
            {
                threw = true;
                Program.Check("YC2 叶子节点执行分支命令 → 不该抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }

            if (!threw)
            {
                Program.Check("YC2 叶子节点执行分支命令 → 不抛、图纸不变（菜单项本不该出现，命令仍要容错）",
                    h4.Flow.Steps.Count == 1 && h4.Flow.Steps[0] == leaf, $"steps={h4.Flow.Steps.Count}");
            }

            // ---- 运行锁：运行中一律拦（与拖入/删除同一道门禁）----
            var h5 = new Harness();
            var par5 = h5.Parallel("运行锁组");
            h5.Add(par5);
            var cv5 = h5.Canvas;
            var node5 = h5.Node(par5)!;
            int lockedBefore = par5.Children.Count;

            Core.Events.GlobalEventBus.Publish(VisionMaster.MainRunState.RunningOnce);
            try
            {
                cv5.StatusHint = null;
                node5.AddBranchCommand!.Execute();
                Program.Check("YC5 运行中「添加分支」被拦（Children 不变 + 状态栏给运行锁文案）",
                    par5.Children.Count == lockedBefore && (cv5.StatusHint ?? "").Contains("运行中"),
                    $"分支={par5.Children.Count}，回执='{cv5.StatusHint}'");
            }
            finally
            {
                Core.Events.GlobalEventBus.Publish(VisionMaster.MainRunState.NotStarted);
            }
        }

        // ==================================================================
        //  ③ 落点防重叠：插入后后续兄弟顺移让位
        // ==================================================================
        private static void DropMakesRoomForInserted()
        {
            // ---- 主场景：顶层 A / B / C，把新算子拖到 A 与 B 之间 ----
            var h = new Harness();
            var a = h.Add(h.Leaf("A"));
            var b = h.Add(h.Leaf("B"));
            var c = h.Add(h.Leaf("C"));
            var canvas = h.Canvas;

            double aY = h.Node(a)!.Location.Y;
            double bY0 = h.Node(b)!.Location.Y;
            double cY0 = h.Node(c)!.Location.Y;

            // 落点：A 与 B 之间（中心 Y 落在两者之间）
            double dropY = (h.Node(a)!.Location.Y + FlowCanvasViewModel.NodeHeight + h.Node(b)!.Location.Y) / 2;
            bool dropped = canvas.DropToolAt(
                NewLeafTool(),
                new Point(h.Node(a)!.Location.X + 20, dropY));

            // 新步骤插在 A 之后（下标 1）
            var inserted = h.Flow.Steps.Count == 4 ? h.Flow.Steps[1] : null;
            double newY = inserted == null ? double.NaN : (canvas.Nodes
                .FirstOrDefault(n => ReferenceEquals(n.Model, inserted))?.Location.Y ?? double.NaN);

            Program.Check("YC3 落点在 A/B 之间 → 新算子插在下标 1",
                dropped && inserted != null && inserted.StepName.StartsWith("落点桩", StringComparison.Ordinal),
                $"steps={string.Join("/", h.Flow.Steps.Select(s => s.StepName))}");

            Program.Check("YC3 新算子与上一个兄弟 A 不重叠（间距 ≥ 行距）",
                !double.IsNaN(newY) && newY >= aY + FlowCanvasViewModel.NodeHeight + FlowLayoutStore.RowGap - 0.01,
                $"A.y={aY:F1} 新.y={newY:F1} 期望 ≥ {aY + FlowCanvasViewModel.NodeHeight + FlowLayoutStore.RowGap:F1}");

            double bY1 = h.Node(b)!.Location.Y;
            double cY1 = h.Node(c)!.Location.Y;
            Program.Check("YC3 新算子与下一个兄弟 B 不重叠（B 被顺移让位）",
                !double.IsNaN(newY) && bY1 >= newY + FlowCanvasViewModel.NodeHeight + FlowLayoutStore.RowGap - 0.01,
                $"新.y={newY:F1} B.y {bY0:F1} → {bY1:F1} 期望 ≥ {newY + FlowCanvasViewModel.NodeHeight + FlowLayoutStore.RowGap:F1}");

            Program.Check("YC3 让位是整段顺移（C 也跟着下移同样距离，不是只挪 B 一个）",
                cY1 > cY0 + 1 && Math.Abs((cY1 - cY0) - (bY1 - bY0)) < 0.01,
                $"B 位移={bY1 - bY0:F1} C 位移={cY1 - cY0:F1}");

            // ---- 反向：插到末尾时谁都不许动 ----
            var h2 = new Harness();
            var x = h2.Add(h2.Leaf("X"));
            var y = h2.Add(h2.Leaf("Y"));
            var cv2 = h2.Canvas;
            double xY0 = h2.Node(x)!.Location.Y;
            double yY0 = h2.Node(y)!.Location.Y;

            cv2.DropToolAt(NewLeafTool(), new Point(h2.Node(y)!.Location.X + 20, double.MaxValue));
            Program.Check("YC3 反向：插到末尾 → 既有兄弟坐标一个都不动（不许无条件顺移）",
                h2.Flow.Steps.Count == 3
                && Math.Abs(h2.Node(x)!.Location.Y - xY0) < 0.01
                && Math.Abs(h2.Node(y)!.Location.Y - yY0) < 0.01,
                $"X {xY0:F1}→{h2.Node(x)!.Location.Y:F1} Y {yY0:F1}→{h2.Node(y)!.Location.Y:F1}");

            // ---- 反向：用户手工拉开过大间距时不许硬拉回来 ----
            var h3 = new Harness();
            var p = h3.Add(h3.Leaf("P"));
            var q = h3.Add(h3.Leaf("Q"));
            var cv3 = h3.Canvas;
            // 手工把 Q 拖到很远（间距远大于一行）
            double farY = h3.Node(p)!.Location.Y + 600;
            h3.Node(q)!.Location = new Point(h3.Node(q)!.Location.X, farY);
            double pY0 = h3.Node(p)!.Location.Y;

            // 落点落在 p 的下半部（保证插到 p 之后、q 之前），而不是落在 p 中心上方
            cv3.DropToolAt(
                NewLeafTool(),
                new Point(h3.Node(p)!.Location.X + 20,
                    h3.Node(p)!.Location.Y + FlowCanvasViewModel.NodeHeight - 2));
            Program.Check("YC3 反向：兄弟原本就隔得远 → 不顺移（尊重用户手工布局）",
                Math.Abs(h3.Node(q)!.Location.Y - farY) < 0.01 && Math.Abs(h3.Node(p)!.Location.Y - pY0) < 0.01,
                $"Q {farY:F1}→{h3.Node(q)!.Location.Y:F1} P {pY0:F1}→{h3.Node(p)!.Location.Y:F1}");

            // ---- 容器内插入：兄弟也在同一分支里让位 ----
            var h4 = new Harness();
            var cont = h4.If("容器");
            var inA = h4.Leaf("内A");
            var inB = h4.Leaf("内B");
            cont.Children[0].Steps.Add(inA);
            cont.Children[0].Steps.Add(inB);
            h4.Add(cont);
            var cv4 = h4.Canvas;
            double inBY0 = h4.Node(inB)!.Location.Y;
            double inAY = h4.Node(inA)!.Location.Y;

            // 落点在容器第一条分支里、内A 与 内B 之间 —— 走 ResolveInsertTarget 的"容器内"分支
            var contNode = h4.Node(cont)!;
            double dropY4 = (inAY + FlowCanvasViewModel.NodeHeight + inBY0) / 2;
            var cv4Canvas = h4.Canvas;
            // 容器内插入走的是"该分支末尾"规则，这里直接验"分支末尾插入后既有兄弟不受影响"这条反向判据
            cv4Canvas.DropToolAt(NewLeafTool(), new Point(contNode.Location.X + 30, dropY4));
            Program.Check("YC3 容器内拖入 → 进第一条分支（与流程栏同口径），既有内层兄弟坐标不动",
                cont.Children[0].Steps.Count == 3
                && Math.Abs(h4.Node(inB)!.Location.Y - inBY0) < 0.01,
                $"分支步数={cont.Children[0].Steps.Count} 内B {inBY0:F1}→{h4.Node(inB)!.Location.Y:F1}");

            // ---- 粘贴同一条落点规则 ----
            var h5 = new Harness();
            var m = h5.Add(h5.Leaf("M"));
            var n = h5.Add(h5.Leaf("N"));
            var cv5 = h5.Canvas;

            cv5.CopySelectedCommand.Execute(null);
            h5.Node(m)!.IsSelected = true;
            cv5.CopySelectedCommand.Execute(null);

            double nY0 = h5.Node(n)!.Location.Y;
            cv5.PasteCommand.Execute(new Point(h5.Node(m)!.Location.X + 20,
                (h5.Node(m)!.Location.Y + FlowCanvasViewModel.NodeHeight + nY0) / 2));

            var pasted = h5.Flow.Steps.Count == 3 ? h5.Flow.Steps[1] : null;
            double pastedY = pasted == null ? double.NaN : (cv5.Nodes
                .FirstOrDefault(nd => ReferenceEquals(nd.Model, pasted))?.Location.Y ?? double.NaN);
            Program.Check("YC3 粘贴到两算子之间 → 同样腾位（粘贴与拖入共用一条落点规则）",
                pasted != null
                && !double.IsNaN(pastedY)
                && h5.Node(n)!.Location.Y >= pastedY + FlowCanvasViewModel.NodeHeight + FlowLayoutStore.RowGap - 0.01,
                $"粘贴件.y={pastedY:F1} N.y {nY0:F1}→{h5.Node(n)!.Location.Y:F1}");
        }

        /// <summary>造一个"落点桩"叶子工具模板（ModuleTypeName 与 StubPluginProvider.LeafPlugin 一致）</summary>
        private static ToolItemModel NewLeafTool()
            => new()
            {
                Name = "落点桩",
                Category = "测试",
                ModuleTypeName = StubPluginProvider.LeafPlugin,
                IsContainer = false,
            };
    }
}
