using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Models
{
    /// <summary>
    /// 工具栏「运行一次 / 循环运行」的批量选择结果。
    /// Selected = 真正要启动的流程；EnabledButExcluded = 启用但不参与批量的流程（用于提示）。
    /// </summary>
    public sealed record FlowBatchSelection(
        IReadOnlyList<FlowModel> Selected,
        IReadOnlyList<FlowModel> EnabledButExcluded,
        string? Warning);

    /// <summary>
    /// 批量运行的选择策略：只选启用的主流程（Role=Main）。
    ///
    /// 为什么不是“所有启用流程”：工位/子程序流程为了能被「调用流程」拉起必须是启用的，
    /// 如果批量运行也把它们当顶层启动，就会和 Main 并发跑，7 GB 内存事故就是这么来的。
    /// 这里把策略收成一个纯函数，ShellViewModel 两个入口共用，断言不需要 UI。
    /// </summary>
    public static class FlowBatchSelector
    {
        public static FlowBatchSelection Select(SolutionModel? solution)
        {
            var enabled = solution?.Flows?
                .Where(f => f != null && f.IsEnabled)
                .ToList() ?? new List<FlowModel>();

            var mains = enabled.Where(f => f.Role == FlowRole.Main).ToList();

            if (mains.Count == 1)
            {
                var main = mains[0];
                return new FlowBatchSelection(
                    new[] { main },
                    enabled.Where(f => !ReferenceEquals(f, main)).ToList(),
                    null);
            }

            if (mains.Count == 0)
            {
                return new FlowBatchSelection(
                    Array.Empty<FlowModel>(),
                    enabled,
                    "当前没有启用的主流程（Role=Main）。请启用主流程，或把要运行的流程设为主流程。");
            }

            return new FlowBatchSelection(
                Array.Empty<FlowModel>(),
                enabled,
                $"检测到 {mains.Count} 条主流程：{string.Join("、", mains.Select(f => f.FlowName))}。"
                + "请只保留一条主流程。");
        }
    }
}
