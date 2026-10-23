using System;
using System.Collections.Generic;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 方案加载后的步骤图标补全。
    ///
    /// 为什么需要它：步骤的 <see cref="StepModel.Icon"/> 是**建步骤时**从工具箱快照进 `.vms` 的。
    /// 老方案里的步骤、或者"插件当时还没写 ShortName"时建的步骤，Icon 会是空串——
    /// 流程栏上就是一个空图标框。插件补上 ShortName 之后，新步骤自然有图标，
    /// 但旧步骤不会自己变；这里在加载后按 `PluginTypeName` 从插件表补一次。
    ///
    /// 三条纪律：
    /// ① **只补空值**，绝不覆盖已有图标（用户/插件显式设过的一律不动）；
    /// ② 只改展示字段（Icon/Category），不碰端口、参数、链接，也不触发流程版本递增；
    /// ③ 找不到插件类型就跳过（旧方案引用了已卸载的插件是合法状态）。
    /// </summary>
    public static class StepIconBackfill
    {
        public static void Apply(SolutionModel solution, IPluginProvider provider)
        {
            if (solution?.Flows == null || provider == null) return;

            var byType = new Dictionary<string, ToolItemModel>(StringComparer.Ordinal);
            foreach (var tool in provider.ModulePlugins.Values)
            {
                if (tool == null || string.IsNullOrWhiteSpace(tool.ModuleTypeName)) continue;
                byType[tool.ModuleTypeName] = tool;
            }

            if (byType.Count == 0) return;

            foreach (var flow in solution.Flows)
            {
                if (flow?.Steps == null) continue;
                Walk(flow.Steps, byType);
            }
        }

        private static void Walk(IEnumerable<StepModel> steps, IReadOnlyDictionary<string, ToolItemModel> byType)
        {
            if (steps == null) return;

            foreach (var step in steps)
            {
                if (step == null) continue;

                Backfill(step, byType);

                foreach (var child in ChildrenOf(step))
                    Walk(child.Steps, byType);
            }
        }

        private static void Backfill(StepModel step, IReadOnlyDictionary<string, ToolItemModel> byType)
        {
            if (string.IsNullOrEmpty(step.PluginTypeName)) return;
            if (!byType.TryGetValue(step.PluginTypeName, out var tool) || tool == null) return;

            if (string.IsNullOrWhiteSpace(step.Icon) && !string.IsNullOrWhiteSpace(tool.Icon))
                step.Icon = tool.Icon;

            if (string.IsNullOrWhiteSpace(step.Category) && !string.IsNullOrWhiteSpace(tool.Category))
                step.Category = tool.Category;
        }

        private static IEnumerable<StepCollection> ChildrenOf(StepModel step) => step switch
        {
            ForStep f when f.Children != null => f.Children,
            ConditionStep c when c.Children != null => c.Children,
            ParallelStep p when p.Children != null => p.Children,
            _ => Array.Empty<StepCollection>()
        };
    }
}
