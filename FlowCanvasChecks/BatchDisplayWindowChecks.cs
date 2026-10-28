using System;
using System.Linq;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// [BW] 单流程"批量设置显示窗口"（<see cref="BatchDisplayWindowService"/>）。
    ///
    /// 【为什么这组断言必须有】这个功能一次改十几个步骤的参数，改错了没有撤销栈可退
    ///（参数编辑不进画布撤销，见需求梳理文档），而且三类跳过条件（无该属性 / 端口已链接 /
    /// 插件类型解析不到）都是**静默**的：写错的表现是"点了没反应"或"改完没生效"。
    ///
    /// 【口径】判据一律看落盘的 <c>InputValues</c>（编译期真正读的那份），不看插件实例；
    /// 版本号用"改前改后比较"而不是绝对值（流程 Add 步骤本身就会涨版本）。
    /// </summary>
    internal static class BatchDisplayWindowChecks
    {
        public static void Run()
        {
            Section("[BW] 批量设置显示窗口（单流程 · 递归 · 两种声明形态）");

            VerifyRecursiveScan();
            VerifyDeclKindsAndDefaults();
            VerifyWithoutDisplayWindow();
            VerifyLinkedPortSkipped();
            VerifyApplyWritesAndIsIdempotent();
            VerifyDisabledStillWritten();
            VerifyOutOfRangeRejected();
            VerifyNonTargetsUntouched();
            VerifySuggestInitialValue();
        }

        // ------------------------------------------------------------------
        //  构造辅助
        // ------------------------------------------------------------------

        private static ActionStep NewStep(Type pluginType, string name)
            => new ActionStep(string.Empty, name, pluginType.AssemblyQualifiedName!, name);

        private static FlowModel NewFlow() => new FlowModel { FlowName = "批量显示窗口测试" };

        // ------------------------------------------------------------------
        //  [BW1] 递归覆盖：顶层 / If 分支 / For 循环体
        // ------------------------------------------------------------------
        private static void VerifyRecursiveScan()
        {
            var flow = NewFlow();
            flow.Steps.Add(NewStep(typeof(Plugin.Matching.MatchingPlugin), "匹配_0"));

            var ifStep = new ConditionStep(string.Empty, "If", "BuiltIn_If", "If_0");
            ifStep.Children[0].Steps.Add(NewStep(typeof(Plugin.BlobDetect.BlobDetectPlugin), "斑点_0"));
            flow.Steps.Add(ifStep);

            var forStep = new ForStep(string.Empty, "For", "BuiltIn_For", "For_0");
            forStep.Children[0].Steps.Add(NewStep(typeof(Plugin.ImageAcquisition.ImageAcquisitionPlugin), "采集_0"));
            flow.Steps.Add(forStep);

            var plan = BatchDisplayWindowService.BuildPlan(flow);

            Check("[BW1] 递归覆盖：顶层 + If 分支 + For 循环体的出图算子全部命中",
                plan.Targets.Count == 3 && plan.ScannedActionSteps == 3,
                $"targets={plan.Targets.Count} scanned={plan.ScannedActionSteps}");

            var names = string.Join("/", plan.Targets.Select(t => t.StepName).OrderBy(n => n, StringComparer.Ordinal));
            Check("[BW2] 命中的正是三个算子（没有把容器头当成算子）",
                names.Contains("匹配_0") && names.Contains("斑点_0") && names.Contains("采集_0"),
                names);
        }

        // ------------------------------------------------------------------
        //  [BW3] 两种声明形态 + 默认值读取
        // ------------------------------------------------------------------
        private static void VerifyDeclKindsAndDefaults()
        {
            var flow = NewFlow();
            flow.Steps.Add(NewStep(typeof(Plugin.Matching.MatchingPlugin), "匹配_0"));                 // [StepConfig] int
            flow.Steps.Add(NewStep(typeof(Plugin.GeometryFinder.GeometryFinderPlugin), "几何查找_0")); // InputPort<int>

            var plan = BatchDisplayWindowService.BuildPlan(flow);

            var config = plan.Targets.FirstOrDefault(t => t.StepName == "匹配_0");
            var port = plan.Targets.FirstOrDefault(t => t.StepName == "几何查找_0");

            Check("[BW3] [StepConfig] int 形态被识别（Matching）",
                config != null && config.Kind == DisplayWindowDeclKind.ConfigProperty,
                config == null ? "未命中" : config.Kind.ToString());

            Check("[BW4] InputPort<int> 形态被识别（GeometryFinder）",
                port != null && port.Kind == DisplayWindowDeclKind.InputPort,
                port == null ? "未命中" : port.Kind.ToString());

            // 两个插件的声明默认值都是 1（ImageAcquisition 的 DefaultValue(1) 同口径）
            Check("[BW5] 未配置时当前值读的是插件声明默认值（1），不是 0 / null",
                config?.CurrentValue == 1 && port?.CurrentValue == 1,
                $"匹配={config?.CurrentValue?.ToString() ?? "null"} 几何查找={port?.CurrentValue?.ToString() ?? "null"}");
        }

        // ------------------------------------------------------------------
        //  [BW6] 没有显示窗口的算子只计数、不进目标
        // ------------------------------------------------------------------
        private static void VerifyWithoutDisplayWindow()
        {
            var flow = NewFlow();
            var delay = NewStep(typeof(VisionMaster.Plugins.Util.DelayPlugin), "延时_0");
            flow.Steps.Add(delay);

            var plan = BatchDisplayWindowService.BuildPlan(flow);

            Check("[BW6] 无显示窗口的算子只计入 WithoutDisplayWindow（不进目标、不报错）",
                plan.Targets.Count == 0 && plan.WithoutDisplayWindow == 1 && plan.ScannedActionSteps == 1,
                $"targets={plan.Targets.Count} without={plan.WithoutDisplayWindow} scanned={plan.ScannedActionSteps}");
        }

        // ------------------------------------------------------------------
        //  [BW7] 端口已链接 → 跳过并计数
        // ------------------------------------------------------------------
        private static void VerifyLinkedPortSkipped()
        {
            var flow = NewFlow();
            var geo = NewStep(typeof(Plugin.GeometryFinder.GeometryFinderPlugin), "几何查找_0");
            geo.SetLink("DisplayViewIndex", new LinkReference
            {
                Kind = LinkKind.GlobalVariable,
                TargetPortName = "目标窗口",
                DisplayAddress = "目标窗口",
            });
            flow.Steps.Add(geo);

            var plan = BatchDisplayWindowService.BuildPlan(flow);

            Check("[BW7] 端口形态已链接 → 跳过（写固定值不会生效）",
                plan.Targets.Count == 0 && plan.LinkedSkipped.Count == 1,
                $"targets={plan.Targets.Count} linked={plan.LinkedSkipped.Count}");

            // 反向：配置属性形态即使 LinkedSources 里残留同名键也不该被跳过
            //（[StepConfig] 属性不进连线体系，按链接跳过会让它永远改不了）
            var flow2 = NewFlow();
            var matching = NewStep(typeof(Plugin.Matching.MatchingPlugin), "匹配_0");
            matching.SetLink("DisplayViewIndex", new LinkReference
            {
                Kind = LinkKind.GlobalVariable,
                TargetPortName = "目标窗口",
                DisplayAddress = "目标窗口",
            });
            flow2.Steps.Add(matching);

            var plan2 = BatchDisplayWindowService.BuildPlan(flow2);
            Check("[BW8] 反向：配置属性形态不受残留链接键影响（仍可写）",
                plan2.Targets.Count == 1 && plan2.LinkedSkipped.Count == 0,
                $"targets={plan2.Targets.Count} linked={plan2.LinkedSkipped.Count}");
        }

        // ------------------------------------------------------------------
        //  [BW9][BW10] 写回：落盘类型 / 版本号 / 幂等
        // ------------------------------------------------------------------
        private static void VerifyApplyWritesAndIsIdempotent()
        {
            var flow = NewFlow();
            var matching = NewStep(typeof(Plugin.Matching.MatchingPlugin), "匹配_0");
            var geo = NewStep(typeof(Plugin.GeometryFinder.GeometryFinderPlugin), "几何查找_0");
            flow.Steps.Add(matching);
            flow.Steps.Add(geo);

            var plan = BatchDisplayWindowService.BuildPlan(flow);
            int versionBefore = flow.Version;

            var result = BatchDisplayWindowService.Apply(plan, 0);

            bool typed = matching.InputValues.TryGetValue("DisplayViewIndex", out var a) && a is int va && va == 0
                && geo.InputValues.TryGetValue("DisplayViewIndex", out var b) && b is int vb && vb == 0;

            Check("[BW9] 写回落到 InputValues 且是 int 0（不是字符串 / 空值 / null）",
                result.Changed == 2 && typed,
                $"changed={result.Changed} 匹配={matching.InputValues.GetValueOrDefault("DisplayViewIndex") ?? "null"} " +
                $"几何查找={geo.InputValues.GetValueOrDefault("DisplayViewIndex") ?? "null"}");

            Check("[BW10] 写回推进流程版本（下一轮运行才会重编译）",
                flow.Version > versionBefore,
                $"Version {versionBefore} → {flow.Version}");

            // 幂等：同一值再点一次不产生写入（避免每次点确定都白刷版本链）
            int versionAfterFirst = flow.Version;
            var second = BatchDisplayWindowService.Apply(BatchDisplayWindowService.BuildPlan(flow), 0);
            Check("[BW11] 幂等：同值重复执行 Changed=0 / Unchanged=2，版本不再涨",
                second.Changed == 0 && second.Unchanged == 2 && flow.Version == versionAfterFirst,
                $"changed={second.Changed} unchanged={second.Unchanged} Version={flow.Version}（前 {versionAfterFirst}）");

            // 反向：0 必须真写进去（"0 = 不显示"是最常用的一次批量操作，不能被当成空值跳过）
            Check("[BW12] 0 值不被当成空值丢弃（IsValidValue(0) 为真）",
                BatchDisplayWindowService.IsValidValue(0),
                "IsValidValue(0)");
        }

        // ------------------------------------------------------------------
        //  [BW13] 已禁用步骤仍写入并计数
        // ------------------------------------------------------------------
        private static void VerifyDisabledStillWritten()
        {
            var flow = NewFlow();
            var disabled = NewStep(typeof(Plugin.BlobDetect.BlobDetectPlugin), "斑点_0");
            disabled.IsDisEnable = true;
            flow.Steps.Add(disabled);

            var plan = BatchDisplayWindowService.BuildPlan(flow);
            Check("[BW13] 已禁用步骤仍进目标并单独计数（禁用不等于不算）",
                plan.Targets.Count == 1 && plan.DisabledTargets == 1,
                $"targets={plan.Targets.Count} disabled={plan.DisabledTargets}");

            var result = BatchDisplayWindowService.Apply(plan, 3);
            Check("[BW14] 已禁用步骤照常写入（DisabledChanged 计数）",
                result.Changed == 1 && result.DisabledChanged == 1
                    && disabled.InputValues.TryGetValue("DisplayViewIndex", out var v) && v is int iv && iv == 3,
                $"changed={result.Changed} disabledChanged={result.DisabledChanged} value={disabled.InputValues.GetValueOrDefault("DisplayViewIndex") ?? "null"}");
        }

        // ------------------------------------------------------------------
        //  [BW15] 越界值拒绝
        // ------------------------------------------------------------------
        private static void VerifyOutOfRangeRejected()
        {
            var flow = NewFlow();
            flow.Steps.Add(NewStep(typeof(Plugin.Matching.MatchingPlugin), "匹配_0"));
            var plan = BatchDisplayWindowService.BuildPlan(flow);

            bool high = Throws(() => BatchDisplayWindowService.Apply(plan, 10));
            bool low = Throws(() => BatchDisplayWindowService.Apply(plan, -1));

            Check("[BW15] 越界值（10 / -1）拒绝写入并抛 ArgumentOutOfRangeException",
                high && low,
                $"10→{(high ? "拒绝" : "竟然写了")} -1→{(low ? "拒绝" : "竟然写了")}");

            Check("[BW16] 合法边界值 0 与 9 被接受",
                BatchDisplayWindowService.IsValidValue(0) && BatchDisplayWindowService.IsValidValue(9)
                    && !BatchDisplayWindowService.IsValidValue(10) && !BatchDisplayWindowService.IsValidValue(-1),
                "0/9 合法，10/-1 非法");

            static bool Throws(Action action)
            {
                try { action(); return false; }
                catch (ArgumentOutOfRangeException) { return true; }
                catch { return false; }
            }
        }

        // ------------------------------------------------------------------
        //  [BW17] 非目标步骤不受影响
        // ------------------------------------------------------------------
        private static void VerifyNonTargetsUntouched()
        {
            var flow = NewFlow();
            var delay = NewStep(typeof(VisionMaster.Plugins.Util.DelayPlugin), "延时_0");
            delay.SetInputValue("Delay", 500);
            flow.Steps.Add(delay);
            flow.Steps.Add(NewStep(typeof(Plugin.Matching.MatchingPlugin), "匹配_0"));

            var plan = BatchDisplayWindowService.BuildPlan(flow);
            BatchDisplayWindowService.Apply(plan, 0);

            bool delayUntouched = delay.InputValues.Count == 1
                && delay.InputValues.TryGetValue("Delay", out var d) && d is int dv && dv == 500;

            Check("[BW17] 无显示窗口的步骤一个键都不被写（延时仍只有 Delay=500）",
                delayUntouched,
                string.Join(",", delay.InputValues.Select(kv => kv.Key + "=" + kv.Value)));
        }

        // ------------------------------------------------------------------
        //  [BW18] 弹窗默认值 = 当前值众数
        // ------------------------------------------------------------------
        private static void VerifySuggestInitialValue()
        {
            var flow = NewFlow();
            var a = NewStep(typeof(Plugin.Matching.MatchingPlugin), "匹配_0");
            var b = NewStep(typeof(Plugin.BlobDetect.BlobDetectPlugin), "斑点_0");
            var c = NewStep(typeof(Plugin.ImageAcquisition.ImageAcquisitionPlugin), "采集_0");
            a.SetInputValue("DisplayViewIndex", 2);
            b.SetInputValue("DisplayViewIndex", 2);
            c.SetInputValue("DisplayViewIndex", 1);
            flow.Steps.Add(a);
            flow.Steps.Add(b);
            flow.Steps.Add(c);

            var plan = BatchDisplayWindowService.BuildPlan(flow);
            int suggested = plan.SuggestInitialValue();

            Check("[BW18] 弹窗默认值取当前值众数（2×2 vs 1×1 → 2）",
                suggested == 2,
                $"suggest={suggested} summary={plan.BuildSummary()}");
        }
    }
}
