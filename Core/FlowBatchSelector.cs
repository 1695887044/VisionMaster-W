using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Models
{
    /// <summary>
    /// 工具栏「运行一次 / 启动整线」的批量选择结果。
    /// Selected = 真正要启动的流程；EnabledButExcluded = 启用但不参与批量的流程（用于提示）。
    /// </summary>
    public sealed record FlowBatchSelection(
        IReadOnlyList<FlowModel> Selected,
        IReadOnlyList<FlowModel> EnabledButExcluded,
        string? Warning);

    /// <summary>
    /// 批量运行的选择策略：**勾了「随整线启动」的流程**。
    ///
    /// 【为什么不再有"唯一主流程"这条口径】2026-10-28 用户裁决：现场工位是并行的，
    /// 一条工位流程失败 / 卡死只该收尾它自己，其余照常循环。把工位做成主流程的子调用
    /// （串行、并行组、或"派发+汇合"都一样）都会让父流程必须"管"子流程——一有故障整线就停。
    /// 所以工位必须与主任务<b>平级</b>：各自一条流程、各自一个会话、由同一个按钮一次性启动。
    /// 同一批里「主流程」角色被退役（老 .vms 的 <c>Role: 2</c> 在加载时迁移成"勾整线"，
    /// 见 <see cref="MandatoryFlows.MigrateRetiredRoles"/>），因此这里只剩一条判据。
    ///
    /// 【为什么不是"所有启用流程"】工位/子程序流程为了能被「调用流程」拉起必须是启用的，
    /// 如果批量运行把它们全当顶层启动，就会和整线成员并发跑——7 GB 内存事故就是这么来的。
    /// 所以"哪些算整线"必须是用户逐个勾出来的，不能从"启用"推导。
    /// </summary>
    public static class FlowBatchSelector
    {
        public static FlowBatchSelection Select(SolutionModel? solution)
        {
            var enabled = solution?.Flows?
                .Where(f => f != null && f.IsEnabled)
                .ToList() ?? new List<FlowModel>();

            var lineMembers = enabled.Where(f => f.IsLineStartMember).ToList();
            if (lineMembers.Count > 0)
            {
                return new FlowBatchSelection(
                    lineMembers,
                    enabled.Where(f => !f.IsLineStartMember).ToList(),
                    null);
            }

            return new FlowBatchSelection(
                Array.Empty<FlowModel>(),
                enabled,
                "当前没有勾选「随整线启动」的流程。"
                + "请在「流程管理」里给要随整线启动的流程勾上「整线」。");
        }
    }
}
