using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;
using VisionMaster;
using VisionMaster.Models;
// 直接复用断言宿主的 Section/Check 计数口径，与 ExecutionChecks / DebugChecks 同款
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// S3：FlowModel 版本链（结构变更递增 + 订阅按可达性重扫）断言。
    ///
    /// 【为什么要有这一组】嵌套步骤（分支/循环体里的步骤）的"禁用 / 改参数"必须递增
    /// FlowModel.Version，否则编译会话的脏检查看不到变化，会拿旧图纸继续跑——用户侧的症状
    /// 就是"改了参数不生效"。旧实现按 CollectionChanged 的 New/OldItems 明细摘挂订阅，有三处漏：
    ///  · 容器新增分支只动 Children 集合，不盯它 → 新分支里的步骤永远挂不上订阅；
    ///  · Move（拖动改序）同时带 OldItems 与 NewItems → 按明细摘挂会把还在树上的步骤订阅摘掉；
    ///  · Reset（Clear）不带任何明细 → 漏摘已删除的步骤 → 假版本递增（白重编译）。
    /// 现在改为"从顶层 Steps 出发按可达性整树重扫"（FlowModel.RebuildSubscriptions），
    /// 本文件把那条设计钉成常设断言（原先是临时探针，26 条全绿后固化进检查工程）。
    ///
    /// 断言手段：全部用真模型对象做结构操作、读 Version 增量。"运行时属性不惊动"
    /// （[RuntimeState] 反射名单）与"恰好 +1"（重复订阅会 +2）也在这里钉。
    /// </summary>
    internal static class FlowModelVersionChecks
    {
        internal static void Run()
        {
            TopLevelAndNestedBumps();
            LateAddedBranchStepsAreWatched();
            DeeplyNestedStepChangeBumps();
            MoveKeepsSubscriptions();
            ClearDropsSubscriptions();
            ContainerRemovalDropsSubscriptions();
            ReplacingStepsCollectionRebinds();
            RuntimeStateDoesNotBump();
            DeserializeRoundTripRebinds();
        }

        /// <summary>建一个纯模型步骤：本组断言不编译、不执行，PluginTypeName 只占位</summary>
        private static ActionStep NewAction(string stepName)
            => new ActionStep("T", "测试算子", "BuiltIn_TestAction", stepName);

        // ==================================================================
        //  [E14] ① 顶层 / 嵌套步骤的属性变更（S3 的主缺口）
        // ==================================================================
        private static void TopLevelAndNestedBumps()
        {
            Section("[E14] 版本链：顶层与嵌套步骤的属性变更");

            var flow = new FlowModel { FlowName = "顶层嵌套版本链" };
            var top = NewAction("顶层步骤");
            flow.Steps.Add(top);
            int v = flow.Version;

            top.IsDisEnable = true;
            Check("顶层步骤禁用：Version +1", flow.Version == v + 1, $"Version {v} → {flow.Version}");
            v = flow.Version;

            // 容器先建、步骤后加（真实交互顺序：拖入 For → 再往循环体里拖步骤）
            var forStep = new ForStep("F", "计次循环", "BuiltIn_For", "计次循环_1");
            flow.Steps.Add(forStep);
            v = flow.Version;

            var body = NewAction("循环体步骤");
            forStep.Children[0].Steps.Add(body);
            Check("循环体里新增步骤：Version +1", flow.Version == v + 1, $"Version {v} → {flow.Version}");
            v = flow.Version;

            body.IsDisEnable = true;
            Check("★循环体内步骤禁用：Version +1（S3 修复前此处静默不递增）",
                flow.Version == v + 1, $"Version {v} → {flow.Version}（不递增 = 编译会话复用旧图纸）");
            v = flow.Version;

            body.Description = "改了描述";
            Check("嵌套步骤改属性：恰好 +1（重复订阅会 +2）", flow.Version == v + 1, $"Version {v} → {flow.Version}");
        }

        // ==================================================================
        //  [E14] ② 后加的分支（只动 Children）及其中的步骤
        // ==================================================================
        private static void LateAddedBranchStepsAreWatched()
        {
            Section("[E14] 版本链：后加的分支及其中的步骤");

            var flow = new FlowModel { FlowName = "分支版本链" };
            var ifStep = new ConditionStep("C", "条件判断", "BuiltIn_If", "如果_1");
            flow.Steps.Add(ifStep);
            int v = flow.Version;

            var inIf = NewAction("If分支步骤");
            ifStep.Children[0].Steps.Add(inIf);
            Check("已有 If 分支里新增步骤：Version +1", flow.Version == v + 1, $"Version {v} → {flow.Version}");
            v = flow.Version;

            inIf.IsDisEnable = true;
            Check("If 分支内步骤禁用：Version +1", flow.Version == v + 1, $"Version {v} → {flow.Version}");
            v = flow.Version;

            // 新增 ElseIf 分支：只动 Children 集合（不盯 Children 就收不到任何事件）
            var elseIf = new StepCollection { BranchType = BranchType.ElseIf, StepName = "ElseIf", Expression = "Score > 80" };
            ifStep.Children.Add(elseIf);
            Check("★新增 ElseIf 分支：Version +1（只动 Children 集合）", flow.Version == v + 1, $"Version {v} → {flow.Version}");
            v = flow.Version;

            var inElseIf = NewAction("ElseIf分支步骤");
            elseIf.Steps.Add(inElseIf);
            Check("新分支里新增步骤：Version +1", flow.Version == v + 1, $"Version {v} → {flow.Version}");
            v = flow.Version;

            inElseIf.IsDisEnable = true;
            Check("★后加分支内的步骤禁用：Version +1（修复前新分支的步骤永远挂不上订阅）",
                flow.Version == v + 1, $"Version {v} → {flow.Version}");
        }

        // ==================================================================
        //  [E14] ③ 三层嵌套（For → If → 步骤 / Else 分支）
        // ==================================================================
        private static void DeeplyNestedStepChangeBumps()
        {
            Section("[E14] 版本链：三层嵌套（For → If → 步骤）");

            var flow = new FlowModel { FlowName = "三层嵌套版本链" };
            var forStep = new ForStep("F", "计次循环", "BuiltIn_For", "外层For");
            flow.Steps.Add(forStep);

            var ifStep = new ConditionStep("C", "条件判断", "BuiltIn_If", "内层If");
            forStep.Children[0].Steps.Add(ifStep);

            var deep = NewAction("最内层步骤");
            ifStep.Children[0].Steps.Add(deep);
            int v = flow.Version;

            deep.IsDisEnable = true;
            Check("★第三层步骤禁用：Version +1", flow.Version == v + 1, $"Version {v} → {flow.Version}");
            v = flow.Version;

            ifStep.Children[1].Steps.Add(NewAction("Else分支步骤"));
            Check("第三层（Else 分支）新增步骤：Version +1", flow.Version == v + 1, $"Version {v} → {flow.Version}");
        }

        // ==================================================================
        //  [E14] ④ 拖动改序（Move 同时带 OldItems / NewItems 的坑）
        // ==================================================================
        private static void MoveKeepsSubscriptions()
        {
            Section("[E14] 版本链：拖动改序后订阅仍在");

            var flow = new FlowModel { FlowName = "改序版本链" };
            var a = NewAction("步骤A");
            var b = NewAction("步骤B");
            flow.Steps.Add(a);
            flow.Steps.Add(b);
            int v = flow.Version;

            flow.Steps.Move(0, 1); // A 与 B 对调
            Check("改序本身：Version +1", flow.Version == v + 1, $"Version {v} → {flow.Version}");
            v = flow.Version;

            a.IsDisEnable = true;
            Check("★被 Move 的步骤仍被订阅：禁用 Version +1（按 OldItems 摘挂会把订阅摘掉）",
                flow.Version == v + 1, $"Version {v} → {flow.Version}");
        }

        // ==================================================================
        //  [E14] ⑤ 清空（Reset 不带明细的坑）
        // ==================================================================
        private static void ClearDropsSubscriptions()
        {
            Section("[E14] 版本链：清空后订阅收口");

            var flow = new FlowModel { FlowName = "清空版本链" };
            var old = NewAction("将被清空的步骤");
            flow.Steps.Add(old);
            int v = flow.Version;

            flow.Steps.Clear();
            Check("清空：Version +1", flow.Version == v + 1, $"Version {v} → {flow.Version}");
            v = flow.Version;

            old.IsDisEnable = true;
            Check("★已清空的步骤不再惊动版本（漏摘 = 假递增 → 白重编译）",
                flow.Version == v, $"Version {v} → {flow.Version}（变了说明旧步骤订阅没摘）");
            v = flow.Version;

            var fresh = NewAction("清空后新加的步骤");
            flow.Steps.Add(fresh);
            Check("清空后新加步骤：Version +1", flow.Version == v + 1, $"Version {v} → {flow.Version}");
            v = flow.Version;

            fresh.IsDisEnable = true;
            Check("清空后新加步骤被订阅（禁用 +1）", flow.Version == v + 1, $"Version {v} → {flow.Version}");
        }

        // ==================================================================
        //  [E14] ⑥ 整个容器移出流程树
        // ==================================================================
        private static void ContainerRemovalDropsSubscriptions()
        {
            Section("[E14] 版本链：整个容器移出流程树");

            var flow = new FlowModel { FlowName = "容器移除版本链" };
            var ifStep = new ConditionStep("C", "条件判断", "BuiltIn_If", "待移除If");
            var inner = NewAction("分支内步骤");
            ifStep.Children[0].Steps.Add(inner);
            flow.Steps.Add(ifStep);
            int v = flow.Version;

            flow.Steps.Remove(ifStep);
            Check("移除容器：Version +1", flow.Version == v + 1, $"Version {v} → {flow.Version}");
            v = flow.Version;

            inner.IsDisEnable = true;
            Check("★容器移出后，其内部步骤不再惊动版本", flow.Version == v, $"Version {v} → {flow.Version}");
            v = flow.Version;

            ifStep.Description = "移出的容器改属性";
            Check("移出的容器自身也不再惊动版本", flow.Version == v, $"Version {v} → {flow.Version}");
        }

        // ==================================================================
        //  [E14] ⑦ 整体替换 Steps 集合（反序列化的赋值路径）
        // ==================================================================
        private static void ReplacingStepsCollectionRebinds()
        {
            Section("[E14] 版本链：整体替换 Steps（反序列化同款路径）");

            var flow = new FlowModel { FlowName = "替换集合版本链" };
            var oldStep = NewAction("旧集合步骤");
            flow.Steps.Add(oldStep);
            int v = flow.Version;

            var replacement = new ObservableCollection<StepModel>();
            var newStep = NewAction("新集合步骤");
            replacement.Add(newStep);
            flow.Steps = replacement; // 反序列化的赋值路径：整体替换

            Check("替换集合本身不改写 Version（版本号由存盘内容负责）", flow.Version == v, $"Version {v} → {flow.Version}");
            v = flow.Version;

            oldStep.IsDisEnable = true;
            Check("旧集合的步骤不再被订阅", flow.Version == v, $"Version {v} → {flow.Version}");
            v = flow.Version;

            newStep.IsDisEnable = true;
            Check("新集合的步骤已挂上订阅（禁用 +1）", flow.Version == v + 1, $"Version {v} → {flow.Version}");
        }

        // ==================================================================
        //  [E14] ⑧ 运行时属性不惊动（[RuntimeState] 反射名单）
        // ==================================================================
        private static void RuntimeStateDoesNotBump()
        {
            Section("[E14] 版本链：运行时属性不惊动版本");

            var flow = new FlowModel { FlowName = "运行时属性版本链" };
            var nested = NewAction("嵌套步骤");
            var forStep = new ForStep("F", "计次循环", "BuiltIn_For", "循环");
            forStep.Children[0].Steps.Add(nested);
            flow.Steps.Add(forStep);

            var flowNotices = new List<string>();
            flow.PropertyChanged += (_, e) => flowNotices.Add(e.PropertyName ?? "(null)");
            int v = flow.Version;

            nested.IsRunningFocus = true;
            nested.State = StepState.Running;
            nested.LastRunTimeMs = 12.5;

            Check("★[RuntimeState] 属性（焦点 / 状态 / 耗时）不递增 Version",
                flow.Version == v, $"Version {v} → {flow.Version}");
            Check("嵌套步骤的运行时属性也不惊动流程（一路零通知）",
                flowNotices.Count == 0, string.Join(",", flowNotices));
        }

        // ==================================================================
        //  [E14] ⑨ 反序列化往返后订阅重挂（存盘 → 重新打开这条真实链路）
        // ==================================================================
        private static void DeserializeRoundTripRebinds()
        {
            Section("[E14] 版本链：反序列化往返后订阅重挂");

            var src = new FlowModel { FlowName = "往返来源" };
            var forStep = new ForStep("F", "计次循环", "BuiltIn_For", "往返For");
            forStep.Children[0].Steps.Add(NewAction("往返循环体步骤"));
            src.Steps.Add(forStep);
            var ifStep = new ConditionStep("C", "条件判断", "BuiltIn_If", "往返If");
            ifStep.Children[0].Steps.Add(NewAction("往返If分支步骤"));
            src.Steps.Add(ifStep);

            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var json = JsonConvert.SerializeObject(src, settings);
            var back = JsonConvert.DeserializeObject<FlowModel>(json, settings);

            Check("往返反序列化成生（图纸类型信息齐全）",
                back != null && back.Steps.Count == 2, $"Steps={back?.Steps.Count}");
            if (back == null || back.Steps.Count != 2) return;

            var backFor = back.Steps.OfType<ForStep>().FirstOrDefault();
            var backIf = back.Steps.OfType<ConditionStep>().FirstOrDefault();
            Check("往返后容器与分支结构还原（For 循环体 1 个 / If 两分支）",
                backFor?.Children.Count == 1 && backFor.Children[0].Steps.Count == 1
                && backIf?.Children.Count == 2 && backIf.Children[0].Steps.Count == 1,
                $"for={backFor?.Children.Count} if={backIf?.Children.Count}");
            if (backFor == null || backIf == null) return;

            var backBody = backFor.Children[0].Steps[0];
            var backInIf = backIf.Children[0].Steps[0];
            int v = back.Version;

            backBody.IsDisEnable = true;
            Check("★往返后循环体步骤禁用：Version +1（订阅在赋值时已重挂）",
                back.Version == v + 1, $"Version {v} → {back.Version}");
            v = back.Version;

            backInIf.IsDisEnable = true;
            Check("往返后 If 分支步骤禁用：恰好 +1（无重复订阅）", back.Version == v + 1, $"Version {v} → {back.Version}");
            v = back.Version;

            var late = NewAction("往返后新增步骤");
            backFor.Children[0].Steps.Add(late);
            Check("往返后往循环体加步骤：Version +1", back.Version == v + 1, $"Version {v} → {back.Version}");
            v = back.Version;

            late.IsDisEnable = true;
            Check("往返后新加步骤被订阅（禁用 +1）", back.Version == v + 1, $"Version {v} → {back.Version}");
            v = back.Version;

            backBody.IsRunningFocus = true;
            Check("往返后运行时属性仍不惊动版本", back.Version == v, $"Version {v} → {back.Version}");
            v = back.Version;

            var lateBranch = new StepCollection { BranchType = BranchType.ElseIf, StepName = "ElseIf", Expression = "x > 1" };
            backIf.Children.Add(lateBranch);
            Check("往返后新增 ElseIf 分支：Version +1（Children 仍被盯）", back.Version == v + 1, $"Version {v} → {back.Version}");
            v = back.Version;

            var deep = NewAction("往返后新分支步骤");
            lateBranch.Steps.Add(deep);
            Check("往返后新分支加步骤：Version +1", back.Version == v + 1, $"Version {v} → {back.Version}");
            v = back.Version;

            deep.IsDisEnable = true;
            Check("★往返后新分支内的步骤禁用：Version +1", back.Version == v + 1, $"Version {v} → {back.Version}");
        }
    }
}
