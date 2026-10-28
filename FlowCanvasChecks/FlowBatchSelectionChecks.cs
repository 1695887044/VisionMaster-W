using System;
using System.IO;
using System.Linq;
using VisionMaster.Models;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// [E20] 整线启动选择器与停止传播（2026-10-28 第二版口径）：
    ///  · 工具栏「启动整线 / 整线循环」选**勾了「随整线启动」的流程**，各起各的会话；
    ///  · 2026-10-28 起「主流程」角色退役：老 .vms 的 <c>Role=2</c> 在加载时迁移成"勾整线"，
    ///    因此选择器只剩一条判据，没有"退回唯一主流程"这条兜底；
    ///  · 未勾选的启用流程进 excluded（提示，不启动）；禁用的既不启动也不提示；
    ///  · 父流程取消时 FlowInvoker 必须 StopSession 子流程。
    ///
    /// 【为什么要多条一起启动】工位与主任务平级：一条失败/卡死只收尾它自己，其余照常循环。
    /// 若把工位做成主流程的子调用（串行或"派发+汇合"），父流程就必须等子流程——
    /// 一条卡住整线跟着停，正是这套选择策略要避免的形态（用户裁决 2026-10-28）。
    /// </summary>
    internal static class FlowBatchSelectionChecks
    {
        internal static void Run()
        {
            Section("[E20] 整线启动选「勾了整线的流程」+ 退役角色迁移 + 停止传播");

            LineMembersAreSelectedTogether();
            NothingMarkedIsRejected();
            HomeAndEndAreNotPickedByAccident();
            MultipleMarkedAreAllowed();
            DisabledFlowsAreNotExcluded();
            MarkedStationIsNotSubroutineOnly();
            RetiredMainRoleMigratesToLineStart();
            ShellUsesSelector();
            InvokerStopsChildOnCancel();
        }

        private static SolutionModel NewSolution(params FlowModel[] flows)
        {
            var solution = new SolutionModel();
            solution.Flows.Clear();
            foreach (var flow in flows) solution.Flows.Add(flow);
            return solution;
        }

        private static FlowModel Flow(string name, FlowRole role, bool enabled = true, FlowInvokeType invoke = FlowInvokeType.Manual)
            => new() { FlowName = name, Role = role, IsEnabled = enabled, InvokeType = invoke };

        private static void LineMembersAreSelectedTogether()
        {
            var sol = NewSolution(
                Flow("Main", FlowRole.None),
                Flow("工位1_上料", FlowRole.None, invoke: FlowInvokeType.LineStart),
                Flow("工位2_检测", FlowRole.None, invoke: FlowInvokeType.LineStart | FlowInvokeType.Subroutine),
                Flow("取配方", FlowRole.None, invoke: FlowInvokeType.Subroutine));

            var sel = FlowBatchSelector.Select(sol);
            Check("E20-1 勾了「随整线启动」的工位全部被选中（各起各的会话）",
                sel.Selected.Count == 2
                && sel.Selected.Any(f => f.FlowName == "工位1_上料")
                && sel.Selected.Any(f => f.FlowName == "工位2_检测"),
                $"selected={string.Join(",", sel.Selected.Select(f => f.FlowName))}");

            Check("E20-2 未勾「整线」的启用流程进 excluded（只提示，不启动）",
                sel.EnabledButExcluded.Count == 2
                && sel.EnabledButExcluded.Any(f => f.FlowName == "Main")
                && sel.EnabledButExcluded.Any(f => f.FlowName == "取配方"),
                $"excluded={string.Join(",", sel.EnabledButExcluded.Select(f => f.FlowName))}");
        }

        private static void NothingMarkedIsRejected()
        {
            // 没有任何整线成员：一条都不启动，提示必须指向唯一补救办法（去勾「整线」）。
            // 退役前这里会"退回唯一 Role=Main"，那条兜底已随角色一起删除。
            var sol = NewSolution(
                Flow("Main", FlowRole.None),
                Flow("Station_Load", FlowRole.None, invoke: FlowInvokeType.Subroutine),
                Flow("Line2", FlowRole.None, invoke: FlowInvokeType.Http));

            var sel = FlowBatchSelector.Select(sol);
            Check("E20-3 一条都没勾「整线」→ 一条也不启动，提示指向「流程管理」勾「整线」",
                sel.Selected.Count == 0
                && sel.Warning != null
                && sel.Warning.Contains("整线")
                && !sel.Warning.Contains("主流程"),
                $"selected={sel.Selected.Count} warning={sel.Warning}");
        }

        private static void HomeAndEndAreNotPickedByAccident()
        {
            var sol = NewSolution(
                Flow("Home", FlowRole.Home),
                Flow("Main", FlowRole.None),
                Flow("End", FlowRole.End));

            var sel = FlowBatchSelector.Select(sol);
            Check("E20-4 Home/End 不会被顺手拉起（只认显式勾选，骨架角色不参与批量）",
                sel.Selected.Count == 0,
                $"selected={string.Join(",", sel.Selected.Select(f => f.FlowName))}");
        }

        private static void MultipleMarkedAreAllowed()
        {
            // "谁算整线"完全由勾选决定，多条平级流程同时启动是常态
            var sol = NewSolution(
                Flow("Line_A", FlowRole.None, invoke: FlowInvokeType.LineStart),
                Flow("Line_B", FlowRole.None, invoke: FlowInvokeType.LineStart));

            var sel = FlowBatchSelector.Select(sol);
            Check("E20-5 多条勾了整线的流程 → 全部启动",
                sel.Selected.Count == 2 && sel.Warning == null,
                $"selected={string.Join(",", sel.Selected.Select(f => f.FlowName))} warning={sel.Warning}");
        }

        private static void DisabledFlowsAreNotExcluded()
        {
            var sol = NewSolution(
                Flow("Line_A", FlowRole.None, invoke: FlowInvokeType.LineStart),
                Flow("工位_停用", FlowRole.None, enabled: false, invoke: FlowInvokeType.LineStart));

            var sel = FlowBatchSelector.Select(sol);
            Check("E20-6 禁用的流程既不启动也不出现在 excluded 提示里（整线成员同理）",
                sel.Selected.Count == 1
                && sel.Selected[0].FlowName == "Line_A"
                && sel.EnabledButExcluded.Count == 0,
                $"selected={string.Join(",", sel.Selected.Select(f => f.FlowName))} excluded={sel.EnabledButExcluded.Count}");
        }

        private static void MarkedStationIsNotSubroutineOnly()
        {
            // 工位通常同时勾「子程序」（供「调用流程」复用）与「整线」。
            // 漏判会让列表把它标成"只能被调用"，用户就不敢按整线启动了。
            var station = Flow("工位1", FlowRole.None,
                invoke: FlowInvokeType.LineStart | FlowInvokeType.Subroutine);
            var subroutineOnly = Flow("取配方", FlowRole.None, invoke: FlowInvokeType.Subroutine);

            Check("E20-7 勾了整线的工位不算「仅子程序」，且被认成可直接启动",
                !station.IsSubroutineOnly
                && station.IsDirectlyLoopable
                && subroutineOnly.IsSubroutineOnly
                && !subroutineOnly.IsDirectlyLoopable,
                $"工位: only={station.IsSubroutineOnly} direct={station.IsDirectlyLoopable} / "
                + $"取配方: only={subroutineOnly.IsSubroutineOnly} direct={subroutineOnly.IsDirectlyLoopable}");
        }

        /// <summary>
        /// 退役角色迁移：老 .vms 里 <c>Role=2</c>（原主流程）必须变成"无角色 + 勾整线"。
        ///
        /// 为什么非断言不可：迁移漏掉时老方案既不在骨架里、也不在整线成员里，
        /// 按「启动整线」直接报"没有勾选整线的流程"，而用户什么都没改。
        /// 枚举成员已退役（<c>RetiredMain</c>，[Obsolete]），这里刻意用数值 2 构造老文件形态。
        /// </summary>
        private static void RetiredMainRoleMigratesToLineStart()
        {
            var legacy = Flow("MainTask", (FlowRole)2);
            var home = Flow("Home", FlowRole.Home);
            var sol = NewSolution(home, legacy);

            int migrated = MandatoryFlows.MigrateRetiredRoles(sol);
            var sel = FlowBatchSelector.Select(sol);

            Check("E20-8 Role=2（退役主流程）→ 无角色 + 勾整线，且立刻被「启动整线」选中",
                migrated == 1
                && legacy.Role == FlowRole.None
                && legacy.IsLineStartMember
                && sel.Selected.Count == 1
                && sel.Selected[0].FlowName == "MainTask",
                $"迁移={migrated} role={legacy.Role} invoke={legacy.InvokeType} "
                + $"selected={string.Join(",", sel.Selected.Select(f => f.FlowName))}");

            // 幂等：再跑一次不重复改、不重复计数
            int again = MandatoryFlows.MigrateRetiredRoles(sol);
            Check("E20-9 迁移幂等：再跑一次不重复计数",
                again == 0 && legacy.IsLineStartMember,
                $"再次迁移={again}");
        }

        private static void ShellUsesSelector()
        {
            var path = ResolveRepoFile(Path.Combine("VisionMaster", "ShellViewModel.cs"));
            if (path == null)
            {
                Check("E20-10 ShellViewModel 使用 FlowBatchSelector", false, "找不到 ShellViewModel.cs");
                return;
            }

            var text = File.ReadAllText(path);
            int count = CountOccurrences(text, "FlowBatchSelector.Select");
            Check("E20-10 RunAllEnabledOnce / RunAllEnabledContinuous 都走 FlowBatchSelector",
                count >= 2, $"FlowBatchSelector.Select 出现 {count} 次（期望 >=2）");
        }

        private static void InvokerStopsChildOnCancel()
        {
            var path = ResolveRepoFile(Path.Combine("Engine", "FlowInvoker.cs"));
            if (path == null)
            {
                Check("E20-11 父流程取消时停止子流程", false, "找不到 FlowInvoker.cs");
                return;
            }

            var text = File.ReadAllText(path);
            bool hasStop = text.Contains("_engine.StopSession(session)");
            bool hasMessage = text.Contains("已请求停止目标");
            Check("E20-11 父流程取消时 StopSession 子流程（不是“仍在后台收尾”）",
                hasStop && hasMessage,
                $"StopSession={hasStop} 已请求停止目标={hasMessage}");
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0, index = 0;
            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }

        private static string ResolveRepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
