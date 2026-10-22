using System;
using System.IO;
using System.Linq;
using VisionMaster.Models;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// [E20] 批量运行选择器与停止传播：
    ///  · 工具栏「运行一次 / 循环运行」只选启用的 Role=Main；
    ///  · Home/End/普通流程即使启用也不参与批量启动；
    ///  · 0 条/多条主流程必须明确拒绝（不静默跑全部）；
    ///  · 父流程取消时 FlowInvoker 必须 StopSession 子流程。
    /// </summary>
    internal static class FlowBatchSelectionChecks
    {
        internal static void Run()
        {
            Section("[E20] 批量运行只选主流程 + 停止传播");

            SelectsOnlyEnabledMain();
            NoMainIsRejected();
            HomeAndEndAreExcluded();
            MultipleMainsAreRejected();
            DisabledFlowsAreNotExcluded();
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

        private static void SelectsOnlyEnabledMain()
        {
            var sol = NewSolution(
                Flow("Main", FlowRole.Main),
                Flow("Station_Load", FlowRole.None, invoke: FlowInvokeType.Subroutine),
                Flow("Line2", FlowRole.None, invoke: FlowInvokeType.Http));

            var sel = FlowBatchSelector.Select(sol);
            Check("E20-1 只选启用的主流程", sel.Selected.Count == 1 && sel.Selected[0].FlowName == "Main",
                $"selected={string.Join(",", sel.Selected.Select(f => f.FlowName))}");
            Check("E20-2 启用的工位流程进入 excluded（用于提示，不被启动）",
                sel.EnabledButExcluded.Count == 2
                && sel.EnabledButExcluded.Any(f => f.FlowName == "Station_Load")
                && sel.EnabledButExcluded.Any(f => f.FlowName == "Line2"),
                $"excluded={string.Join(",", sel.EnabledButExcluded.Select(f => f.FlowName))}");
        }

        private static void NoMainIsRejected()
        {
            var sol = NewSolution(
                Flow("Station_Load", FlowRole.None, invoke: FlowInvokeType.Subroutine),
                Flow("Line2", FlowRole.None, invoke: FlowInvokeType.Http));

            var sel = FlowBatchSelector.Select(sol);
            Check("E20-3 没有启用的主流程 → 一条也不启动",
                sel.Selected.Count == 0 && !string.IsNullOrWhiteSpace(sel.Warning),
                $"selected={sel.Selected.Count} warning={sel.Warning}");
        }

        private static void HomeAndEndAreExcluded()
        {
            var sol = NewSolution(
                Flow("Home", FlowRole.Home),
                Flow("Main", FlowRole.Main),
                Flow("End", FlowRole.End));

            var sel = FlowBatchSelector.Select(sol);
            Check("E20-4 Home/End 即使启用也不参与批量运行",
                sel.Selected.Count == 1 && sel.Selected[0].FlowName == "Main",
                $"selected={string.Join(",", sel.Selected.Select(f => f.FlowName))}");
        }

        private static void MultipleMainsAreRejected()
        {
            var sol = NewSolution(
                Flow("Main_A", FlowRole.Main),
                Flow("Main_B", FlowRole.Main));

            var sel = FlowBatchSelector.Select(sol);
            Check("E20-5 多条主流程 → 拒绝并列出名字（不并发跑）",
                sel.Selected.Count == 0
                && sel.Warning != null
                && sel.Warning.Contains("Main_A")
                && sel.Warning.Contains("Main_B"),
                $"selected={sel.Selected.Count} warning={sel.Warning}");
        }

        private static void DisabledFlowsAreNotExcluded()
        {
            var sol = NewSolution(
                Flow("Main", FlowRole.Main),
                Flow("Station_Off", FlowRole.None, enabled: false, invoke: FlowInvokeType.Subroutine));

            var sel = FlowBatchSelector.Select(sol);
            Check("E20-6 禁用流程既不启动也不出现在 excluded 提示里",
                sel.Selected.Count == 1 && sel.EnabledButExcluded.Count == 0,
                $"selected={sel.Selected.Count} excluded={sel.EnabledButExcluded.Count}");
        }

        private static void ShellUsesSelector()
        {
            var path = ResolveRepoFile(Path.Combine("VisionMaster", "ShellViewModel.cs"));
            if (path == null)
            {
                Check("E20-7 ShellViewModel 使用 FlowBatchSelector", false, "找不到 ShellViewModel.cs");
                return;
            }

            var text = File.ReadAllText(path);
            int count = CountOccurrences(text, "FlowBatchSelector.Select");
            Check("E20-7 RunAllEnabledOnce / RunAllEnabledContinuous 都走 FlowBatchSelector",
                count >= 2, $"FlowBatchSelector.Select 出现 {count} 次（期望 >=2）");
        }

        private static void InvokerStopsChildOnCancel()
        {
            var path = ResolveRepoFile(Path.Combine("Engine", "FlowInvoker.cs"));
            if (path == null)
            {
                Check("E20-8 父流程取消时停止子流程", false, "找不到 FlowInvoker.cs");
                return;
            }

            var text = File.ReadAllText(path);
            bool hasStop = text.Contains("_engine.StopSession(session)");
            bool hasMessage = text.Contains("已请求停止目标");
            Check("E20-8 父流程取消时 StopSession 子流程（不是“仍在后台收尾”）",
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
