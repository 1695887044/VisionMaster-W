using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace ScadaChecks
{
    /// <summary>
    /// 模板参数化只替换**最外层**下标（P3/P4 回归）。
    ///
    /// 背景：旧实现用 <c>name.Replace("[0]", "[{i}]")</c> 全量替换，
    /// <c>Recipes[0].Ints[0]</c> 会被改成 <c>Recipes[{i}].Ints[{i}]</c>；
    /// 实例 1 展开成 <c>Recipes[1].Ints[1]</c> —— 变量存在、解析不报错、画面上有值，
    /// **读的却是错成员**。这里从四个角度钉住修复：
    ///   ① 变换规则本身（单层 / 嵌套 / 三层 / 首组非数字 / 畸形名）；
    ///   ② 不该动的引用必须原样保留（外层下标不等于实例号、已是 {i} 的模板）；
    ///   ③ 参数化 → 展开的往返：N 个实例全部落在同一个成员下标上；
    ///   ④ 旧模板（已含两处 {i}）展开时必须给出可见告警，而不是继续静默错绑。
    /// </summary>
    internal partial class Program
    {
        // ==================================================================
        //  常规断言（进 gate）
        // ==================================================================

        private static void StructIndexParameterizationChecks()
        {
            Section("[SP] 模板参数化：只替换最外层下标（P3/P4 回归）");

            SingleLevelMemberPath();
            NestedMemberIndexStaysFixed();
            DeepNestingKeepsInnerIndices();
            FourLevelPathWithTrailingMember();
            NonLeadingOrAlreadyParameterizedStaysUntouched();
            MalformedNamesAreLeftAlone();
            RootPlusArrayIndexFormStillWorks();
            ExpansionRoundTripKeepsMemberIndex();
            LegacyMultiTokenTemplateWarns();
        }

        /// <summary>单层成员路径：<c>Recipes[0].Title</c> → <c>Recipes[{i}].Title</c></summary>
        private static void SingleLevelMemberPath()
        {
            var payload = PayloadOf("Recipes[0].Title", -1);
            bool ok = ScadaTemplateParameterizer.TryParameterize(
                payload, 0, out int changed, out var roots, out string? error);

            Check("[SP1] 单层成员路径参数化：Recipes[0].Title → Recipes[{i}].Title",
                ok && changed == 1
                && BindingName(payload) == "Recipes[{i}].Title"
                && payload.Items[0].Bindings[0].ArrayIndex == -1,
                ok ? $"{BindingName(payload)} / idx={payload.Items[0].Bindings[0].ArrayIndex}" : error ?? "失败");

            Check("[SP1] 数组根清单 = 外层名 Recipes",
                ok && roots.SequenceEqual(new[] { "Recipes" }),
                ok ? string.Join("/", roots) : "-");
        }

        /// <summary>★ P3 核心：嵌套成员下标必须原样保留</summary>
        private static void NestedMemberIndexStaysFixed()
        {
            var payload = PayloadOf("Recipes[0].Ints[0]", -1);
            bool ok = ScadaTemplateParameterizer.TryParameterize(
                payload, 0, out int changed, out _, out string? error);

            Check("[SP2] ★嵌套成员下标不被替换：Recipes[0].Ints[0] → Recipes[{i}].Ints[0]",
                ok && changed == 1 && BindingName(payload) == "Recipes[{i}].Ints[0]",
                ok ? BindingName(payload) : error ?? "失败");

            // 成员下标不是 0 时同样不动
            var payload2 = PayloadOf("Recipes[0].Ints[7]", -1);
            bool ok2 = ScadaTemplateParameterizer.TryParameterize(
                payload2, 0, out _, out _, out string? error2);

            Check("[SP2] 成员下标为 7 时也只换外层：Recipes[0].Ints[7] → Recipes[{i}].Ints[7]",
                ok2 && BindingName(payload2) == "Recipes[{i}].Ints[7]",
                ok2 ? BindingName(payload2) : error2 ?? "失败");
        }

        /// <summary>三层及以上：只换最外层，中间层与最内层都不动</summary>
        private static void DeepNestingKeepsInnerIndices()
        {
            var payload = PayloadOf("Line[0].Cell[0].Slot[0]", -1);
            bool ok = ScadaTemplateParameterizer.TryParameterize(
                payload, 0, out _, out _, out string? error);

            Check("[SP3] 三层嵌套只换最外层：Line[0].Cell[0].Slot[0] → Line[{i}].Cell[0].Slot[0]",
                ok && BindingName(payload) == "Line[{i}].Cell[0].Slot[0]",
                ok ? BindingName(payload) : error ?? "失败");
        }

        /// <summary>
        /// 用户问的形态：<c>A[0].B[0].C[0].D</c>（三段下标 + 末尾成员）。
        /// 判据：只有最外层 <c>[0]</c> 被换成 <c>{i}</c>，中间两层与末尾成员一字不动。
        /// </summary>
        private static void FourLevelPathWithTrailingMember()
        {
            const string shape = "A[0].B[0].C[0].D";

            var payload = PayloadOf(shape, -1);
            bool ok = ScadaTemplateParameterizer.TryParameterize(
                payload, 0, out int changed, out var roots, out string? error);

            Check("[SP3b] A[0].B[0].C[0].D @0 → A[{i}].B[0].C[0].D（只换最外层）",
                ok && changed == 1 && BindingName(payload) == "A[{i}].B[0].C[0].D",
                ok ? BindingName(payload) : error ?? "失败");

            Check("[SP3b] 数组根清单 = A（取第一组 [ 之前）",
                ok && roots.SequenceEqual(new[] { "A" }),
                ok ? string.Join("/", roots) : "-");

            var mismatch = PayloadOf(shape, -1);
            bool mismatchOk = ScadaTemplateParameterizer.TryParameterize(
                mismatch, 2, out int mismatchChanged, out _, out _);

            Check("[SP3b] 同一形态 @2 不动（最外层下标是 0，不是 2）",
                !mismatchOk && mismatchChanged == 0 && BindingName(mismatch) == shape,
                $"{BindingName(mismatch)} / changed={mismatchChanged}");

            // 展开往返：3 个实例都应落在 A[k].B[0].C[0].D
            var variables = new List<IVariable>();
            for (int a = 0; a < 3; a++)
                variables.Add(VariableFactory.CreateLocal($"A[{a}].B[0].C[0].D", typeof(short), string.Empty, (short)0));

            var resolver = new ScadaTemplateVariableResolver(BuildRegistry(variables.ToArray()));
            bool expanded = ScadaTemplateExpansion.TryPlan(
                payload,
                new ScadaTemplateExpansionOptions { Count = 3, StartIndex = 0 },
                resolver, out var plan, out var errors, out _);

            var names = expanded
                ? plan!.Instances.Select(i => i.Items[0].Bindings[0].VariableName).ToArray()
                : Array.Empty<string>();

            Check("[SP3b] 展开 3 个实例：A[0]/A[1]/A[2].B[0].C[0].D（中间层不跟着变）",
                expanded && names.SequenceEqual(new[]
                {
                    "A[0].B[0].C[0].D",
                    "A[1].B[0].C[0].D",
                    "A[2].B[0].C[0].D"
                }),
                expanded ? string.Join(" / ", names) : DescribeErrors(errors));

            // 想换内层只能手写 {i}：这里是"手工令牌"路径，用来界定自动参数化的边界
            var handWritten = PayloadOf("A[0].B[0].C[{i}].D", -1);
            bool handExpanded = ScadaTemplateExpansion.TryPlan(
                handWritten,
                new ScadaTemplateExpansionOptions { Count = 2, StartIndex = 0 },
                new ScadaTemplateVariableResolver(BuildRegistry(
                    VariableFactory.CreateLocal("A[0].B[0].C[0].D", typeof(short), string.Empty, (short)0),
                    VariableFactory.CreateLocal("A[0].B[0].C[1].D", typeof(short), string.Empty, (short)0))),
                out var handPlan, out var handErrors, out _);

            var handNames = handExpanded
                ? handPlan!.Instances.Select(i => i.Items[0].Bindings[0].VariableName).ToArray()
                : Array.Empty<string>();

            Check("[SP3b] 内层要变只能手写 {i}：A[0].B[0].C[{i}].D 展开成 C[0]/C[1]（用的是同一个实例号）",
                handExpanded && handNames.SequenceEqual(new[] { "A[0].B[0].C[0].D", "A[0].B[0].C[1].D" }),
                handExpanded ? string.Join(" / ", handNames) : DescribeErrors(handErrors));
        }

        /// <summary>不该动的引用：外层下标 ≠ 实例号；已是 {i} 的模板再存一次</summary>
        private static void NonLeadingOrAlreadyParameterizedStaysUntouched()
        {
            var mismatch = PayloadOf("Recipes[2].Ints[0]", -1);
            bool mismatchOk = ScadaTemplateParameterizer.TryParameterize(
                mismatch, 0, out int mismatchChanged, out _, out string? mismatchError);

            Check("[SP4] ★外层下标 ≠ 实例号时不动它（Recipes[2].Ints[0] 保持原样）",
                !mismatchOk && mismatchChanged == 0
                && BindingName(mismatch) == "Recipes[2].Ints[0]"
                && (mismatchError?.Contains("没有找到", StringComparison.Ordinal) ?? false),
                $"{BindingName(mismatch)} / changed={mismatchChanged}");

            var already = PayloadOf("Recipes[{i}].Ints[0]", -1);
            bool alreadyOk = ScadaTemplateParameterizer.TryParameterize(
                already, 0, out int alreadyChanged, out _, out _);

            Check("[SP4] 已参数化过的模板再存一次不重复替换（幂等）",
                !alreadyOk && alreadyChanged == 0 && BindingName(already) == "Recipes[{i}].Ints[0]",
                $"{BindingName(already)} / changed={alreadyChanged}");
        }

        /// <summary>畸形名：不认就原样保留，绝不抛（尾部多余括号不算畸形，见 SP-S2 随机用例）</summary>
        private static void MalformedNamesAreLeftAlone()
        {
            foreach (string name in new[] { "A[]", "A[abc]", "A[0", "A[ -1 ]" })
            {
                var payload = PayloadOf(name, -1);
                bool threw = false;
                bool ok = false;
                try
                {
                    ok = ScadaTemplateParameterizer.TryParameterize(payload, 0, out _, out _, out _);
                }
                catch (Exception ex)
                {
                    threw = true;
                    Check($"[SP5] 畸形名「{name}」不抛异常", false, ex.GetType().Name);
                }

                if (threw) continue;

                Check($"[SP5] 畸形名「{name}」原样保留（不猜下标）",
                    !ok && BindingName(payload) == name,
                    $"{BindingName(payload)} / ok={ok}");
            }
        }

        /// <summary>B 形态（纯根名 + ArrayIndex）不受影响；带下标的根名不再叠一层</summary>
        private static void RootPlusArrayIndexFormStillWorks()
        {
            var plain = PayloadOf("Ctl", 0);
            bool plainOk = ScadaTemplateParameterizer.TryParameterize(
                plain, 0, out _, out var roots, out string? plainError);

            Check("[SP6] B 形态仍可用：Ctl + ArrayIndex 0 → Ctl[{i}]，下标清 -1",
                plainOk && BindingName(plain) == "Ctl[{i}]"
                && plain.Items[0].Bindings[0].ArrayIndex == -1
                && roots.SequenceEqual(new[] { "Ctl" }),
                plainOk ? $"{BindingName(plain)} / idx={plain.Items[0].Bindings[0].ArrayIndex}" : plainError ?? "失败");

            var bracketed = PayloadOf("Ctl[1]", 0);
            bool bracketedOk = ScadaTemplateParameterizer.TryParameterize(
                bracketed, 0, out _, out _, out _);

            Check("[SP6] 带下标的根名不再叠一层（Ctl[1] + ArrayIndex 0 保持原样）",
                !bracketedOk && BindingName(bracketed) == "Ctl[1]",
                $"{BindingName(bracketed)} / ok={bracketedOk}");
        }

        /// <summary>★ P4 回归钉：参数化 → 展开，N 个实例全部落在同一个成员下标</summary>
        private static void ExpansionRoundTripKeepsMemberIndex()
        {
            var variables = new List<IVariable>();
            for (int recipe = 0; recipe < 3; recipe++)
            {
                for (int member = 0; member < 2; member++)
                {
                    variables.Add(VariableFactory.CreateLocal(
                        $"Recipes[{recipe}].Ints[{member}]", typeof(short), string.Empty, (short)0));
                }
            }

            var resolver = new ScadaTemplateVariableResolver(BuildRegistry(variables.ToArray()));

            // 作者那一条绑在实例 1 的 Ints[0] 上
            var payload = PayloadOf("Recipes[1].Ints[0]", -1);
            bool parameterized = ScadaTemplateParameterizer.TryParameterize(
                payload, 1, out _, out _, out string? parameterError);

            Check("[SP7] 作者模板：Recipes[1].Ints[0] → Recipes[{i}].Ints[0]",
                parameterized && BindingName(payload) == "Recipes[{i}].Ints[0]",
                parameterized ? BindingName(payload) : parameterError ?? "失败");

            bool expanded = ScadaTemplateExpansion.TryPlan(
                payload,
                new ScadaTemplateExpansionOptions { Count = 3, StartIndex = 0 },
                resolver,
                out var plan,
                out var errors,
                out var warnings);

            var names = expanded
                ? plan!.Instances.Select(i => i.Items[0].Bindings[0].VariableName).ToArray()
                : Array.Empty<string>();

            Check("[SP7] ★展开：3 个实例全部绑 Ints[0]（旧实现会变成 Ints[0]/Ints[1]/Ints[2]）",
                expanded && names.SequenceEqual(new[]
                {
                    "Recipes[0].Ints[0]",
                    "Recipes[1].Ints[0]",
                    "Recipes[2].Ints[0]"
                }),
                expanded ? string.Join(" / ", names) : DescribeErrors(errors));

            Check("[SP7] 展开结果里没有任何 Ints[i] 错位（逐条复查）",
                expanded && plan!.Instances.Select((inst, k) =>
                {
                    var binding = inst.Items[0].Bindings[0];
                    return binding.VariableName == $"Recipes[{k}].Ints[0]" && binding.ArrayIndex == -1;
                }).All(ok => ok),
                expanded ? "全部命中" : DescribeErrors(errors));

            Check("[SP7] 单处 {i} 的模板不产生「不止一处」告警",
                expanded && !warnings.Any(w => w.Contains("不止一处", StringComparison.Ordinal)),
                expanded ? string.Join(" | ", warnings) : "-");
        }

        /// <summary>旧模板（旧版全量替换留下的 <c>Recipes[{i}].Ints[{i}]</c>）必须告警</summary>
        private static void LegacyMultiTokenTemplateWarns()
        {
            var variables = new List<IVariable>();
            for (int recipe = 0; recipe < 3; recipe++)
            {
                for (int member = 0; member < 2; member++)
                {
                    variables.Add(VariableFactory.CreateLocal(
                        $"Recipes[{recipe}].Ints[{member}]", typeof(short), string.Empty, (short)0));
                }
            }

            var resolver = new ScadaTemplateVariableResolver(BuildRegistry(variables.ToArray()));
            var legacy = PayloadOf("Recipes[{i}].Ints[{i}]", -1);

            bool expanded = ScadaTemplateExpansion.TryPlan(
                legacy,
                new ScadaTemplateExpansionOptions { Count = 2, StartIndex = 0 },
                resolver,
                out _,
                out var errors,
                out var warnings);

            Check("[SP8] ★旧模板（两处 {i}）展开仍成功，但给出「不止一处」告警",
                expanded && warnings.Any(w => w.Contains("不止一处", StringComparison.Ordinal)),
                expanded ? string.Join(" | ", warnings) : DescribeErrors(errors));
        }

        // ==================================================================
        //  压力 / 暴力测试（--stress）
        // ==================================================================

        private static void StressStructIndexParameterization()
        {
            Section("[SP-S] 模板参数化暴力/压力（--stress）");

            ExhaustiveShapeMatrix();
            RandomizedOracleComparison();
            ExpansionThroughputStress();
        }

        /// <summary>
        /// 穷举矩阵：深度 1..4 × 实例号 0..3 × 各层成员下标 0..3，共 4×4×4×4 = 256 种形态。
        /// 判据：只有最外层那组被换成 <c>{i}</c>，其余逐字符不变。
        /// </summary>
        private static void ExhaustiveShapeMatrix()
        {
            var stopwatch = Stopwatch.StartNew();
            int cases = 0;
            var mismatches = new List<string>();

            for (int depth = 1; depth <= 4; depth++)
            {
                for (int index = 0; index <= 3; index++)
                {
                    for (int inner = 0; inner <= 3; inner++)
                    {
                        for (int outer = 0; outer <= 3; outer++)
                        {
                            cases++;

                            // 外层用 outer，其余层用 inner，这样能区分"换错层"
                            string name = BuildNestedName(depth, outer, inner);
                            var payload = PayloadOf(name, -1);

                            try
                            {
                                ScadaTemplateParameterizer.TryParameterize(
                                    payload, index, out _, out _, out _);
                            }
                            catch (Exception ex)
                            {
                                mismatches.Add($"{name}@{index} 抛 {ex.GetType().Name}");
                                continue;
                            }

                            string expected = outer == index
                                ? ReplaceLeadingIndex(name, outer)
                                : name;

                            string actual = BindingName(payload);
                            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                                mismatches.Add($"{name}@{index} → {actual}（期望 {expected}）");
                        }
                    }
                }
            }

            stopwatch.Stop();

            Check($"[SP-S1] 穷举 {cases} 种嵌套形态：只有最外层下标被替换",
                mismatches.Count == 0,
                mismatches.Count == 0
                    ? $"全部命中（{stopwatch.ElapsedMilliseconds} ms）"
                    : $"{mismatches.Count} 处不符，首例：{mismatches[0]}");
        }

        /// <summary>
        /// 随机暴力：固定种子 5000 条随机名（1~4 组下标、名字段随机、下标 0~9），
        /// 与一份**独立写的朴素参照实现**逐条比对。
        /// </summary>
        private static void RandomizedOracleComparison()
        {
            var random = new Random(20261029);
            var stopwatch = Stopwatch.StartNew();

            const int total = 5000;
            int changed = 0;
            var mismatches = new List<string>();

            for (int i = 0; i < total; i++)
            {
                int depth = random.Next(1, 5);
                int index = random.Next(0, 6);
                string name = BuildRandomName(random, depth);

                var payload = PayloadOf(name, -1);

                try
                {
                    ScadaTemplateParameterizer.TryParameterize(payload, index, out int changedCount, out _, out _);
                    changed += changedCount;
                }
                catch (Exception ex)
                {
                    mismatches.Add($"{name}@{index} 抛 {ex.GetType().Name}");
                    continue;
                }

                string expected = ReferenceParameterize(name, index);
                string actual = BindingName(payload);

                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    mismatches.Add($"{name}@{index} → {actual}（期望 {expected}）");
            }

            stopwatch.Stop();

            Check($"[SP-S2] 随机暴力 {total} 条：与独立参照实现逐条一致",
                mismatches.Count == 0,
                mismatches.Count == 0
                    ? $"全部一致（改写 {changed} 条，{stopwatch.ElapsedMilliseconds} ms）"
                    : $"{mismatches.Count} 处不符，首例：{mismatches[0]}");
        }

        /// <summary>
        /// 展开吞吐：100 条引用 × 40 个实例 = 4000 次解析，逐条核对落点。
        /// 参数化一次，然后 Count=40 展开；每个实例的每条引用都必须落在
        /// <c>Recipes[k].Member{m}</c> 上（k = 实例号，m 固定）。
        /// </summary>
        private static void ExpansionThroughputStress()
        {
            const int members = 100;
            const int instances = 40;

            var variables = new List<IVariable>(members * instances);
            for (int recipe = 0; recipe < instances; recipe++)
            {
                for (int member = 0; member < members; member++)
                {
                    variables.Add(VariableFactory.CreateLocal(
                        $"Recipes[{recipe}].Member{member}", typeof(short), string.Empty, (short)0));
                }
            }

            var resolver = new ScadaTemplateVariableResolver(BuildRegistry(variables.ToArray()));

            // 模板：100 条引用，全部指向实例 0 的 Member0..99
            var payload = new ScadaClipboardPayload();
            var element = new ScadaElementSnapshot
            {
                TypeKey = "Hmi.Text",
                Name = "压测图元",
                Width = 100,
                Height = 30,
            };
            for (int member = 0; member < members; member++)
            {
                element.Bindings.Add(new ScadaBindingSnapshot
                {
                    TargetProperty = $"Value{member}",
                    VariableName = $"Recipes[0].Member{member}",
                    ArrayIndex = -1,
                });
            }
            payload.Items.Add(element);

            var stopwatch = Stopwatch.StartNew();

            bool parameterized = ScadaTemplateParameterizer.TryParameterize(
                payload, 0, out int changed, out _, out string? parameterError);

            bool expanded = ScadaTemplateExpansion.TryPlan(
                payload,
                new ScadaTemplateExpansionOptions { Count = instances, StartIndex = 0 },
                resolver,
                out var plan,
                out var errors,
                out _);

            stopwatch.Stop();

            int wrong = 0;
            string firstWrong = string.Empty;

            if (expanded)
            {
                for (int k = 0; k < instances; k++)
                {
                    var bindings = plan!.Instances[k].Items[0].Bindings;
                    for (int m = 0; m < members; m++)
                    {
                        string expected = $"Recipes[{k}].Member{m}";
                        if (!string.Equals(bindings[m].VariableName, expected, StringComparison.Ordinal))
                        {
                            if (wrong == 0) firstWrong = $"实例{k}/第{m}条 → {bindings[m].VariableName}（期望 {expected}）";
                            wrong++;
                        }
                    }
                }
            }

            Check($"[SP-S3] 压力展开：{members} 引用 × {instances} 实例 = {members * instances} 次解析",
                parameterized && changed == members && expanded && wrong == 0,
                !parameterized
                    ? parameterError ?? "参数化失败"
                    : !expanded
                        ? DescribeErrors(errors)
                        : wrong == 0
                            ? $"全部命中（{stopwatch.ElapsedMilliseconds} ms）"
                            : $"{wrong} 处错位，首例：{firstWrong}");
        }

        // ==================================================================
        //  夹具
        // ==================================================================

        private static ScadaClipboardPayload PayloadOf(string variableName, int arrayIndex)
        {
            var payload = new ScadaClipboardPayload();
            var element = new ScadaElementSnapshot
            {
                TypeKey = "Hmi.Text",
                Name = "SP 图元",
                Width = 100,
                Height = 30,
            };
            element.Bindings.Add(new ScadaBindingSnapshot
            {
                TargetProperty = "Value",
                VariableName = variableName,
                ArrayIndex = arrayIndex,
            });
            payload.Items.Add(element);
            return payload;
        }

        private static string BindingName(ScadaClipboardPayload payload)
            => payload.Items[0].Bindings[0].VariableName ?? string.Empty;

        /// <summary>构造 depth 层嵌套名：<c>N0[outer].N1[inner].N2[inner]…</c></summary>
        private static string BuildNestedName(int depth, int outer, int inner)
        {
            var parts = new List<string>(depth);
            for (int level = 0; level < depth; level++)
            {
                int value = level == 0 ? outer : inner;
                parts.Add($"N{level}[{value.ToString(CultureInfo.InvariantCulture)}]");
            }
            return string.Join(".", parts);
        }

        /// <summary>把最外层下标换成 <c>{i}</c>（穷举矩阵的期望值）</summary>
        private static string ReplaceLeadingIndex(string name, int index)
        {
            int open = name.IndexOf('[');
            int close = name.IndexOf(']', open + 1);
            return name[..open] + "[{i}]" + name[(close + 1)..];
        }

        /// <summary>
        /// 独立参照实现（朴素版，故意不共享产品代码）：
        /// 找到第一组 <c>[digits]</c>，等于实例号就只换这一组，否则整串不动。
        /// </summary>
        private static string ReferenceParameterize(string name, int index)
        {
            int open = name.IndexOf('[');
            if (open < 0) return name;

            int close = name.IndexOf(']', open + 1);
            if (close < 0) return name;

            string digits = name.Substring(open + 1, close - open - 1);
            if (digits.Length == 0) return name;
            if (!digits.All(ch => ch >= '0' && ch <= '9')) return name;
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int value)) return name;
            if (value != index) return name;

            return name.Substring(0, open) + "[{i}]" + name.Substring(close + 1);
        }

        /// <summary>随机名：depth 段，每段 <c>段名[k]</c>，下标 0~9</summary>
        private static string BuildRandomName(Random random, int depth)
        {
            var parts = new List<string>(depth);
            for (int level = 0; level < depth; level++)
            {
                int length = random.Next(1, 6);
                var chars = new char[length];
                for (int i = 0; i < length; i++)
                    chars[i] = (char)('A' + random.Next(0, 26));
                parts.Add($"{new string(chars)}[{random.Next(0, 10).ToString(CultureInfo.InvariantCulture)}]");
            }
            return string.Join(".", parts);
        }
    }
}
