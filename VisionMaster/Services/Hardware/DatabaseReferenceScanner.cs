using System;
using System.Collections.Generic;
using VisionMaster.Models;

namespace VisionMaster.Services.Hardware
{
    /// <summary>
    /// 删除前的引用检查：当前方案里有没有步骤引用这个数据库/语句。
    ///
    /// 为什么必须深遍历：步骤可能藏在 If / For / While / Parallel 的容器里，
    /// 只扫顶层会让"看起来没人用"的语句被删掉，下次编译流程直接失败。
    ///
    /// 为什么读 InputValues 而不是插件实例：插件实例在编辑期不存在（每次打开配置窗口才 new），
    /// `.vms` 里能证明引用的只有步骤配置里那两个 Guid。
    /// </summary>
    public static class DatabaseReferenceScanner
    {
        public static IReadOnlyList<string> FindReferences(IWorkspaceManager? workspace, Guid profileId, Guid statementId)
        {
            var hits = new List<string>();
            var solution = workspace?.CurrentSolution;
            if (solution == null) return hits;

            foreach (var flow in solution.Flows)
                Walk(flow.Steps, flow.FlowName, profileId, statementId, hits);

            return hits;
        }

        private static void Walk(
            IEnumerable<StepModel> steps,
            string flowName,
            Guid profileId,
            Guid statementId,
            List<string> hits)
        {
            if (steps == null) return;

            foreach (var step in steps)
            {
                if (step == null) continue;

                if (References(step, profileId, statementId))
                    hits.Add($"{flowName} / {step.StepName}");

                foreach (var child in ChildrenOf(step))
                    Walk(child.Steps, flowName, profileId, statementId, hits);
            }
        }

        private static bool References(StepModel step, Guid profileId, Guid statementId)
        {
            var values = step.InputValues;
            if (values == null) return false;

            if (profileId != Guid.Empty && TryGuid(values, "ProfileId", out var p) && p == profileId)
                return true;

            if (statementId != Guid.Empty && TryGuid(values, "StatementId", out var s) && s == statementId)
                return true;

            return false;
        }

        private static bool TryGuid(IDictionary<string, object> values, string key, out Guid id)
        {
            id = Guid.Empty;
            if (!values.TryGetValue(key, out var raw) || raw == null) return false;
            if (raw is Guid g)
            {
                id = g;
                return true;
            }

            // .vms 反序列化后是 JValue/字符串，统一按文本再解一次
            return Guid.TryParse(raw.ToString(), out id);
        }

        private static IEnumerable<StepCollection> ChildrenOf(StepModel step)
        {
            switch (step)
            {
                case ForStep f when f.Children != null:
                    return f.Children;
                case ConditionStep c when c.Children != null:
                    return c.Children;
                case ParallelStep p when p.Children != null:
                    return p.Children;
                default:
                    return Array.Empty<StepCollection>();
            }
        }
    }
}
