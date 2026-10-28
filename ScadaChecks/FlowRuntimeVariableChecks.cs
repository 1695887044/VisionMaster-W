using System;
using System.Collections.Generic;
using VisionMaster.Binding;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [FRV] 流程运行统计系统变量目录：
        /// 身份确定性 / 实例复用 / 改名 / 删除 / 值更新不刷整表。
        /// </summary>
        private static void FlowRuntimeVariableChecks()
        {
            Section("[FRV] 流程运行统计系统变量：身份 / 改名 / 删除 / 值更新");

            var source = new FakeFlowRuntimeSource();
            Guid a = Guid.NewGuid();
            Guid b = Guid.NewGuid();
            source.Items.Add(Snap(a, "Station_A", "运行", hasRound: true, cycle: 410, ok: 10, ng: 1, unk: 0));
            source.Items.Add(Snap(b, "Station_B", "运行", hasRound: true, cycle: 842, ok: 9, ng: 2, unk: 3));

            var catalog = new FlowRuntimeVariableCatalog(source);

            Check("FRV-1 两条流程 → 父 + 16 字段 ×2 = 34 条",
                catalog.Snapshot().Count == 34,
                $"count={catalog.Snapshot().Count}");

            bool hasState = catalog.TryGet("System.Flow.Station_A.State", out var state)
                && Equals(state.Value, "运行");
            Check("FRV-2 按派生名取 State，值正确", hasState, $"{state?.Value}");

            bool hasParent = catalog.TryGet("System.Flow.Station_A", out var parent)
                && parent.IsComposite
                && parent.Fields.Count == 16;
            Check("FRV-3 父变量存在且含 16 个子字段", hasParent, $"fields={parent?.Fields.Count}");

            bool hasOk = catalog.TryGet("System.Flow.Station_A.OkCount", out var ok)
                && ok.Value is long okValue && okValue == 10L;
            Check("FRV-4 OkCount 类型 long、值 10", hasOk, $"{ok?.Value} ({ok?.Value?.GetType().Name})");

            Guid okId = ok!.VariableId;
            Guid parentId = parent!.VariableId;

            // ---- 身份确定：同一 FlowId 同一字段，换一个目录实例仍得到同一个 Id ----
            var catalog2 = new FlowRuntimeVariableCatalog(source);
            bool sameId = catalog2.TryGet("System.Flow.Station_A.OkCount", out var ok2)
                && ok2.VariableId == okId;
            bool sameParentId = catalog2.TryGet("System.Flow.Station_A", out var parent2)
                && parent2.VariableId == parentId;
            Check("FRV-5 确定性 Id：重建目录后 OkCount / 父变量 Id 不变",
                sameId && sameParentId,
                $"ok={okId:N} / parent={parentId:N}");
            catalog2.Dispose();

            // ---- 值更新（不改拓扑）：模型值变，目录不触发 Changed（宿主不重排集合） ----
            int changed = 0;
            catalog.Changed += (_, _) => changed++;
            source.Items[0] = Snap(a, "Station_A", "运行", hasRound: true, cycle: 500, ok: 11, ng: 1, unk: 0);
            source.Raise();
            bool valueUpdated = catalog.TryGet("System.Flow.Station_A.OkCount", out var okAfter)
                && okAfter.Value is long afterValue && afterValue == 11L;
            Check("FRV-6 值更新：OkCount 变 11，且不触发整表 Changed",
                valueUpdated && changed == 0,
                $"value={okAfter?.Value} changed={changed}");

            // ---- 改名：实例复用（Id 不变）、新名可查、旧名消失 ----
            source.Items[0] = Snap(a, "Station_A2", "运行", hasRound: true, cycle: 500, ok: 11, ng: 1, unk: 0);
            source.Raise();
            bool renamed = catalog.TryGet("System.Flow.Station_A2.OkCount", out var renamedOk)
                && renamedOk.VariableId == okId;
            bool oldGone = !catalog.TryGet("System.Flow.Station_A.OkCount", out _);
            Check("FRV-7 改名：新名可查、旧名消失、变量 Id 不变",
                renamed && oldGone,
                $"new={renamedOk?.VariableId:N} oldGone={oldGone}");

            // ---- 删除：变量从集合消失，拓扑变化触发 Changed ----
            changed = 0;
            source.Items.RemoveAt(1);
            source.Raise();
            bool removed = !catalog.TryGet("System.Flow.Station_B.State", out _)
                && catalog.Snapshot().Count == 17;
            Check("FRV-8 删除流程：B 的变量消失、剩 17 条、触发 Changed",
                removed && changed == 1,
                $"count={catalog.Snapshot().Count} changed={changed}");

            // ---- 角色/字段：StepCount 等字段按类型落值 ----
            bool stepCount = catalog.TryGet("System.Flow.Station_A2.StepCount", out var steps)
                && steps.Value is int stepValue && stepValue == 37;
            Check("FRV-9 StepCount 类型 int、值 37", stepCount, $"{steps?.Value}");

            catalog.Dispose();
        }

        private static FlowRuntimeSnapshot Snap(
            Guid id, string name, string state, bool hasRound, double cycle, long ok, long ng, long unk)
            => new(
                id,
                name,
                "普通",
                state,
                IsRunning: true,
                IsDebug: false,
                HasRound: hasRound,
                CycleTimeMs: cycle,
                AverageCycleTimeMs: cycle,
                RoundCount: hasRound ? 1 : 0,
                OkCount: ok,
                NgCount: ng,
                UnknownCount: unk,
                CurrentStep: "步骤 3",
                CurrentStepIndex: 3,
                StepCount: 37,
                LastRoundAt: "14:32:08",
                LastReason: string.Empty,
                VerdictSource: "结果输出步骤");

        private sealed class FakeFlowRuntimeSource : IFlowRuntimeSource
        {
            public List<FlowRuntimeSnapshot> Items { get; } = new();

            public event EventHandler? Changed;

            public IReadOnlyList<FlowRuntimeSnapshot> Snapshot() => Items;

            public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
