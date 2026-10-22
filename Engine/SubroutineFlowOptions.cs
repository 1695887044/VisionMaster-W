using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 「调用流程」目标流程名的候选来源（宿主侧唯一一份）。
    ///
    /// 为什么必须有它：插件只引用 Core.Interfaces，物理上看不到方案里的流程表；
    /// 而"当前方案里哪些流程允许被当子程序调用"只有宿主知道（判据是
    /// <see cref="FlowModel.InvokeType"/> 的 Subroutine 位）。没有候选时用户只能手打名字，
    /// 打错的代价是运行期一句"当前方案中没有名为「X」的流程"——配错与拼错在日志里长得一模一样。
    ///
    /// 为什么值仍是流程名（不是 Id）：<see cref="FlowInvoker"/> 按名 Ordinal 查找
    /// （与 HTTP 路由、运行状态镜像同一口径）；改成 Id 会与既有 .vms、日志、HTTP 路径全部脱节。
    /// 显示文本只加状态后缀，不参与寻址（见 <see cref="StepConfigOption"/> 的值/显示分离）。
    /// </summary>
    internal static class SubroutineFlowOptions
    {
        /// <summary>
        /// 组装候选：只列勾了「子程序」的流程。
        ///
        /// 禁用 / 步序加密 / 当前流程**仍然列出**，只在显示文本上标出来：
        /// 把它们从候选里剔掉的话，已配好的值会在下拉里显示成空白（值还在，只是选不到），
        /// 用户看到的是"配置丢了"；标出来才看得出"它为什么不能调"。
        /// 与轴名候选"解析不到就不列"的区别：轴名是寻址失败（列出来必然动不了），
        /// 这里是运行期门禁（启用/解密后同名即可用），属于可恢复状态。
        /// </summary>
        internal static IReadOnlyList<StepConfigOption> Build(IWorkspaceManager workspace)
        {
            var flows = workspace?.CurrentSolution?.Flows;
            if (flows == null) return Array.Empty<StepConfigOption>();

            var current = workspace.CurrentFlow;
            var result = new List<StepConfigOption>(flows.Count);

            foreach (var flow in flows)
            {
                if (flow == null || string.IsNullOrWhiteSpace(flow.FlowName))
                    continue;
                if ((flow.InvokeType & FlowInvokeType.Subroutine) == 0)
                    continue;

                var name = flow.FlowName.Trim();
                var note = Describe(flow, ReferenceEquals(flow, current));
                result.Add(new StepConfigOption(name, note == null ? name : $"{name}（{note}）"));
            }

            // 同名流程（人工编辑 .vms）只留第一条：FlowInvoker.FindFlow 也是取第一条，
            // 列出两项会让人以为"选哪个都行"，实际只有第一条会被执行。
            return result
                .GroupBy(o => o.Value, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(o => o.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>把"选了会失败"的原因写进显示文本；正常流程返回 null（显示纯名字）。</summary>
        private static string? Describe(FlowModel flow, bool isCurrent)
        {
            if (isCurrent) return "当前流程，不能调用自己";
            if (!flow.IsEnabled) return "已禁用";
            if (flow.StepsEncrypted) return "步序已加密";
            return null;
        }

        /// <summary>
        /// 注册到契约层候选来源表（宿主启动时调一次）。
        ///
        /// 用 RegisterAdditional 而不是 Register：运动模块（轴名/卡名）与通讯模块
        /// （通讯变量名）也各注册一份，谁也不该顶掉谁。解析工作区放在委托里现做，
        /// 不在注册时取一次快照——方案可以中途切换，候选必须跟着当前方案变。
        /// </summary>
        internal static void Register(Func<IWorkspaceManager> resolveWorkspace)
        {
            if (resolveWorkspace == null) throw new ArgumentNullException(nameof(resolveWorkspace));

            StepConfigOptionSource.RegisterAdditional(kind =>
                kind == StepConfigOptionKind.SubroutineFlowName
                    ? Build(resolveWorkspace())
                    : Array.Empty<StepConfigOption>());
        }
    }
}
