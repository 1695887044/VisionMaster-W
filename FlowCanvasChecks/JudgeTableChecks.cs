using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using Core.Interfaces;
using Newtonsoft.Json;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
// 本文件 using 了 System.Threading（STA 视图冒烟），裸写的 ExecutionContext 会与 System.Threading.ExecutionContext 撞名
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 判定表（Plugin.JudgeTable）的断言。
    ///
    /// 【为什么走反射】本工程不引用 Plugin.JudgeTable（断言工程只引用 VisionMaster），
    /// 而 <c>FlowCanvasChecks.csproj</c> 不在本批改动范围内 —— 与 VariableAssignmentCheck
    /// 对 Plugin.Util 的做法同源：从 <c>Modules\Plugin.JudgeTable.dll</c> 加载，按名字取类型。
    /// 能强类型的地方仍然强类型：插件对象转成 <see cref="VisionPluginBase"/> 后，
    /// 端口表 / Execute / Initialize 都是编译期检查过的。
    ///
    /// 覆盖面（与交付口径一一对应）：
    ///   ① 动态端口：3 条规则 → 3 入 3 出；快照名字/类型；删/改名/关生成/停用；
    ///   ② 运行时变量：每条规则写 <c>LocalVariables[全名]</c>，值等于端口值；
    ///   ③ 表级端口：IsOk / FailedCount / Reason / Details / Results；
    ///   ④ **不污染全局**：执行前后 workspace.GlobalVariables 逐项不变；
    ///   ⑤ 七种判定模式各一条（含反向证伪）；
    ///   ⑥ 持久化：StepModel JSON 往返保留规则与端口快照，重新 Initialize 后端口重建；
    ///   ⑦ 动态输入未接 → 该规则失败、Details 含端口名、不抛异常；
    ///   ⑧ 视图：真 XAML 实例化 + 布局（资源键/模板/绑定），静态扫描三件套与零十六进制。
    /// </summary>
    internal static class JudgeTableChecks
    {
        private const string PluginTypeName = "Plugin.JudgeTable.JudgeTablePlugin";
        private const string RuleTypeName = "Plugin.JudgeTable.Models.JudgeRule";
        private const string EngineTypeName = "Plugin.JudgeTable.Models.JudgeEngine";
        private const string ModeEnumName = "Plugin.JudgeTable.Models.JudgeMode";
        private const string SetSemanticsEnumName = "Plugin.JudgeTable.Models.SetSemantics";
        private const string ViewTypeName = "Plugin.JudgeTable.JudgeTableView";

        public static void Run()
        {
            Section("[JT] 判定表：动态端口 / 运行时变量 / 七种判定 / 不污染全局 / 持久化");

            try
            {
                RunDynamicPorts();
                RunRuntimeVariablesAndGlobals();
                RunModes();
                RunPersistence();
                RunUnlinkedInput();
                RunSourceWiring();
                RunCancelRollback();
                RunUnsupportedActual();
                RunViewSmoke();
            }
            catch (Exception ex)
            {
                // 反射断链（类型改名/搬命名空间）时给一条可定位的红，而不是让整个 gate 崩在这一节。
                // 打完整 ToString（含堆栈）：只印 Message 时"空引用"这类异常定位不到行。
                Check("[JT] 断言自身未抛异常（反射入口可解析）", false, ex.ToString());
            }
        }

        // ==================================================================
        //  ① 动态端口：生成 / 快照 / 删除 / 改名 / 开关
        // ==================================================================
        private static void RunDynamicPorts()
        {
            var plugin = NewPlugin("工位A判定", out var step);
            var p = (VisionPluginBase)plugin;
            var rules = RulesOf(plugin);

            rules.Add(NewRule("外径", "Numeric", "12.00 ± 0.05"));
            rules.Add(NewRule("高度", "Numeric", "8.00 ± 0.10"));
            rules.Add(NewRule("主码", "Set", "CODE-A, CODE-B"));

            var expectedIn = new[] { "实际_外径", "实际_高度", "实际_主码" };
            var expectedOut = new[] { "工位A判定.外径.结果", "工位A判定.高度.结果", "工位A判定.主码.结果" };

            Check("[JT1] 3 条规则 → 3 个动态输入端口 + 3 个动态输出端口",
                DynamicInputs(p).SetEquals(expectedIn) && DynamicOutputs(p).SetEquals(expectedOut),
                $"输入=[{Join(DynamicInputs(p))}] 输出=[{Join(DynamicOutputs(p))}]");

            var snapshot = step.OutputPortDefinitions;
            bool snapshotOk = snapshot.Count == 3
                              && snapshot.All(d => d.DataTypeName == typeof(bool).AssemblyQualifiedName)
                              && snapshot.Select(d => d.Name).OrderBy(x => x, StringComparer.Ordinal)
                                  .SequenceEqual(expectedOut.OrderBy(x => x, StringComparer.Ordinal));
            Check("[JT2] StepData.OutputPortDefinitions 快照名字/类型与端口一致（bool，编译器据此供下游接线）",
                snapshotOk,
                snapshot.Count == 0
                    ? "快照为空"
                    : string.Join(" | ", snapshot.Select(d => $"{d.Name}:{ShortType(d.DataTypeName)}")));

            // ---- 删除一条 → 端口消失 ----
            rules.RemoveAt(2);
            Check("[JT3] 删除 1 条规则 → 对应的输入与输出端口都消失（快照同步缩表）",
                DynamicInputs(p).SetEquals(new[] { "实际_外径", "实际_高度" })
                && DynamicOutputs(p).SetEquals(new[] { "工位A判定.外径.结果", "工位A判定.高度.结果" })
                && step.OutputPortDefinitions.Count == 2,
                $"输入=[{Join(DynamicInputs(p))}] 输出=[{Join(DynamicOutputs(p))}] 快照={step.OutputPortDefinitions.Count}");

            // ---- 改名 → 端口更新 ----
            var first = (object)rules[0]!;
            Set(first, "Name", "外径复检");
            Check("[JT4] 规则改名 → 输入端口与输出端口同步改名（旧名字不再存在）",
                DynamicInputs(p).SetEquals(new[] { "实际_外径复检", "实际_高度" })
                && DynamicOutputs(p).SetEquals(new[] { "工位A判定.外径复检.结果", "工位A判定.高度.结果" })
                && !p.Inputs.ContainsKey("实际_外径"),
                $"输入=[{Join(DynamicInputs(p))}] 输出=[{Join(DynamicOutputs(p))}]");

            // ---- 重名 → 自动后缀（否则端口折叠、连线互相覆盖） ----
            rules.Add(NewRule("高度", "Numeric", "1"));
            Check("[JT5] 两条规则同名 → 后一条的端口自动加 _2 后缀（不折叠、不覆盖）",
                DynamicInputs(p).Contains("实际_高度_2") && DynamicOutputs(p).Contains("工位A判定.高度_2.结果"),
                $"输入=[{Join(DynamicInputs(p))}] 输出=[{Join(DynamicOutputs(p))}]");
            rules.RemoveAt(rules.Count - 1);

            // ---- GenerateResult=false → 不生成输出，输入仍在 ----
            var second = (object)rules[1]!;
            Set(second, "GenerateResult", false);
            Check("[JT6] 关闭结果输出 → 不生成输出端口，但输入端口仍在（连线键不会变孤儿）",
                p.Inputs.ContainsKey("实际_高度")
                && !p.Outputs.ContainsKey("工位A判定.高度.结果")
                && DynamicOutputs(p).Count == 1,
                $"输出=[{Join(DynamicOutputs(p))}] 输入在={p.Inputs.ContainsKey("实际_高度")}");

            // ---- Enabled=false → 不生成输出，输入仍在 ----
            Set(second, "GenerateResult", true);
            Set(second, "Enabled", false);
            Check("[JT7] 停用规则 → 不生成输出端口，但输入端口仍在（重新启用即恢复）",
                p.Inputs.ContainsKey("实际_高度") && !p.Outputs.ContainsKey("工位A判定.高度.结果"),
                $"输出=[{Join(DynamicOutputs(p))}]");
            Set(second, "Enabled", true);
            Check("[JT8] 重新启用 → 输出端口恢复（配置保留，不用重配）",
                p.Outputs.ContainsKey("工位A判定.高度.结果"), $"输出=[{Join(DynamicOutputs(p))}]");

            p.Dispose();
        }

        // ==================================================================
        //  ②③④ 运行时变量 / 表级端口 / 不污染全局
        // ==================================================================
        private static void RunRuntimeVariablesAndGlobals()
        {
            var workspace = NewWorkspace();
            var before = SnapshotGlobals(workspace);

            var plugin = NewPlugin("工位A判定", out _);
            var p = (VisionPluginBase)plugin;
            var rules = RulesOf(plugin);
            rules.Add(NewRule("外径", "Numeric", "12.00 ± 0.05"));
            rules.Add(NewRule("高度", "Numeric", "8.00 ± 0.10"));
            rules.Add(NewRule("主码", "Set", "CODE-A, CODE-B"));

            SetActual(p, "实际_外径", 12.03);          // OK
            SetActual(p, "实际_高度", 8.30);           // NG（超上限 0.2）
            SetActual(p, "实际_主码", "CODE-A, CODE-B, CODE-D"); // AllExpected → OK

            var log = new StubLog();
            var ctx = NewContext(workspace, log);
            bool executed = p.Execute(ctx);

            Check("[JT9] 每条规则的结果端口写入了 IsOk（12.03 通过 / 8.30 失败 / 集合通过）",
                ReadBool(p, "工位A判定.外径.结果") == true
                && ReadBool(p, "工位A判定.高度.结果") == false
                && ReadBool(p, "工位A判定.主码.结果") == true,
                $"外径={ReadBool(p, "工位A判定.外径.结果")} 高度={ReadBool(p, "工位A判定.高度.结果")} 主码={ReadBool(p, "工位A判定.主码.结果")}");

            Check("[JT10] 运行时变量与端口同名同值（context.LocalVariables[全名] == 端口值）",
                ctx.LocalVariables.TryGetValue("工位A判定.外径.结果", out var v1) && v1 is bool b1 && b1
                && ctx.LocalVariables.TryGetValue("工位A判定.高度.结果", out var v2) && v2 is bool b2 && !b2
                && ctx.LocalVariables.TryGetValue("工位A判定.主码.结果", out var v3) && v3 is bool b3 && b3,
                $"LocalVariables 键=[{Join(new HashSet<string>(ctx.LocalVariables.Keys, StringComparer.Ordinal))}]");

            Check("[JT11] 表级端口：IsOk=false / FailedCount=1 / Results=[true,false,true] / Details 三条",
                ReadBool(p, "IsOk") == false
                && Convert.ToInt32(p.Outputs["FailedCount"].Value) == 1
                && p.Outputs["Results"].Value is bool[] results && results.SequenceEqual(new[] { true, false, true })
                && p.Outputs["Details"].Value is string[] details && details.Length == 3
                && details[1].Contains("高度"),
                $"IsOk={ReadBool(p, "IsOk")} Failed={p.Outputs["FailedCount"].Value} "
                + $"Results=[{Join((p.Outputs["Results"].Value as bool[]) ?? Array.Empty<bool>())}]");

            Check("[JT12] 整体 NG → 步骤按插件契约失败（Success=false 且 ErrorMessage=Reason）",
                !executed
                && p.Success.Value is false
                && (p.ErrorMessage.Value as string ?? "").Contains("NG")
                && (p.Outputs["Reason"].Value as string ?? "").Contains("1/3"),
                $"Success={p.Success.Value} Reason=[{p.Outputs["Reason"].Value}]");

            Check("[JT13] **不污染全局**：执行前后 workspace.GlobalVariables 数量与逐项值都不变",
                GlobalsUnchanged(workspace, before),
                $"执行前 {before.Count} 项 / 执行后 {workspace.GlobalVariables.Count} 项：{DescribeGlobals(workspace)}");

            // ---- 反向：全部通过 → IsOk=true，运行时变量全 true ----
            SetActual(p, "实际_高度", 8.05);
            var ctx2 = NewContext(workspace, new StubLog());
            bool executed2 = p.Execute(ctx2);
            Check("[JT14] 反向：三条都通过 → IsOk=true / FailedCount=0 / 步骤成功 / 运行时变量全 true",
                executed2
                && ReadBool(p, "IsOk") == true
                && Convert.ToInt32(p.Outputs["FailedCount"].Value) == 0
                && ctx2.LocalVariables.Values.OfType<bool>().Count() == 3
                && ctx2.LocalVariables.Values.OfType<bool>().All(x => x),
                $"Success={p.Success.Value} IsOk={ReadBool(p, "IsOk")} Failed={p.Outputs["FailedCount"].Value}");

            // ---- 反向：判定 NG 时运行时变量必须是 false（不是"没写"也不是 true） ----
            SetActual(p, "实际_外径", 12.20);
            var ctx3 = NewContext(workspace, new StubLog());
            p.Execute(ctx3);
            Check("[JT15] 反向：判定 NG 时运行时变量为 false（不是缺键、也不是 true）",
                ctx3.LocalVariables.TryGetValue("工位A判定.外径.结果", out var ng) && ng is bool ngb && ngb == false,
                $"LocalVariables[外径.结果]={(ctx3.LocalVariables.TryGetValue("工位A判定.外径.结果", out var raw) ? raw : "<缺键>")}");

            // ---- GenerateResult=false：端口不存在，但该行仍参与整体判定 ----
            var firstRule = (object)rules[0]!;
            Set(firstRule, "GenerateResult", false);
            SetActual(p, "实际_外径", 12.00);   // 这一行判定通过
            var ctx4 = NewContext(workspace, new StubLog());
            bool executed4 = p.Execute(ctx4);
            Check("[JT16] 关闭结果输出：端口与运行时变量都不生成，但该行仍参与总判定（Results[0] 有效）",
                !p.Outputs.ContainsKey("工位A判定.外径.结果")
                && !ctx4.LocalVariables.ContainsKey("工位A判定.外径.结果")
                && p.Outputs["Results"].Value is bool[] r4 && r4.Length == 3 && r4[0]
                && executed4,
                $"端口在={p.Outputs.ContainsKey("工位A判定.外径.结果")} "
                + $"运行时变量在={ctx4.LocalVariables.ContainsKey("工位A判定.外径.结果")} "
                + $"Results=[{Join((p.Outputs["Results"].Value as bool[]) ?? Array.Empty<bool>())}]");
            Set(firstRule, "GenerateResult", true);

            // ---- 一条规则都没启用 → 整表 NG（空表不许静默通过） ----
            foreach (var rule in rules.Cast<object>()) Set(rule, "Enabled", false);
            var ctx5 = NewContext(workspace, new StubLog());
            bool executed5 = p.Execute(ctx5);
            Check("[JT17] 没有启用任何规则 → IsOk=false 且 Reason 写明原因（空表不静默通过）",
                !executed5
                && ReadBool(p, "IsOk") == false
                && (p.Outputs["Reason"].Value as string ?? "").Contains("没有启用任何规则"),
                $"Success={p.Success.Value} Reason=[{p.Outputs["Reason"].Value}]");

            Check("[JT18] 一轮都没跑过的行：Actual / 原因显示占位文案（配置态不装成已跑过）",
                NeverRunPlaceholderOk(),
                NeverRunPlaceholderDetail());

            p.Dispose();
        }

        /// <summary>新建一条规则（一次都没跑过）→ Actual / 原因应是占位文案</summary>
        private static bool NeverRunPlaceholderOk()
        {
            var fresh = NewPlugin("工位C判定", out _);
            try
            {
                var freshRules = RulesOf(fresh);
                freshRules.Add(NewRule("新行", "Numeric", "1"));
                var rule = (object)freshRules[0]!;
                return GetString(rule, "ActualDisplay") == "—（运行时）"
                       && GetString(rule, "ReasonDisplay") == "—（运行时）";
            }
            finally { ((VisionPluginBase)fresh).Dispose(); }
        }

        private static string NeverRunPlaceholderDetail()
        {
            var fresh = NewPlugin("工位C判定", out _);
            try
            {
                var freshRules = RulesOf(fresh);
                freshRules.Add(NewRule("新行", "Numeric", "1"));
                var rule = (object)freshRules[0]!;
                return $"Actual=[{GetString(rule, "ActualDisplay")}] 原因=[{GetString(rule, "ReasonDisplay")}]";
            }
            finally { ((VisionPluginBase)fresh).Dispose(); }
        }

        // ==================================================================
        //  ⑤ 七种判定模式（纯函数层，各带反向证伪）
        // ==================================================================
        private static void RunModes()
        {
            // ---- 数值 ----
            Check("[JT19] 数值 ±：12.04 在 12.00±0.05 内 → OK；12.06 越界 → NG",
                JudgeOk("Numeric", "12.00 ± 0.05", 12.04) == true
                && JudgeOk("Numeric", "12.00 ± 0.05", 12.06) == false,
                JudgeDetail("Numeric", "12.00 ± 0.05", 12.06));

            Check("[JT20] 数值区间：18~24 含端点 → OK；17 / 25 → NG",
                JudgeOk("Numeric", "18 ~ 24", 18) == true
                && JudgeOk("Numeric", "18 ~ 24", 24) == true
                && JudgeOk("Numeric", "18 ~ 24", 17) == false
                && JudgeOk("Numeric", "18 ~ 24", 25) == false,
                JudgeDetail("Numeric", "18 ~ 24", 17));

            Check("[JT21] 数值下限：≥2.5 → 2.5 通过、2.4 失败；≤2.5 → 2.5 通过、2.6 失败",
                JudgeOk("Numeric", "≥ 2.5", 2.5) == true
                && JudgeOk("Numeric", "≥ 2.5", 2.4) == false
                && JudgeOk("Numeric", "≤ 2.5", 2.5) == true
                && JudgeOk("Numeric", "≤ 2.5", 2.6) == false,
                JudgeDetail("Numeric", "≥ 2.5", 2.4));

            Check("[JT22] 数值容差字段：期望只写中心值时用「容差 ±」；NaN / Inf / 非数字文本一律 NG",
                JudgeOk("Numeric", "12", 12.04, tolerance: 0.05) == true
                && JudgeOk("Numeric", "12", 12.20, tolerance: 0.05) == false
                && JudgeOk("Numeric", "12", double.NaN) == false
                && JudgeOk("Numeric", "12", double.PositiveInfinity) == false
                && JudgeOk("Numeric", "12", "abc") == false,
                JudgeDetail("Numeric", "12", double.NaN));

            Check("[JT23] 数值：字符串 \"12.5\" 可解析；带千分位的 \"1,000\" 不认（防静默读成 1000）",
                JudgeOk("Numeric", "12.5", "12.5") == true
                && JudgeOk("Numeric", "1000", "1,000") == false,
                JudgeDetail("Numeric", "1000", "1,000"));

            // ---- 文本 ----
            Check("[JT24] 文本·忽略大小写：开=true / 关=false（同一对字符串，证明开关真的生效）",
                JudgeOk("Text", "lot-a", "LOT-A", ignoreCase: true) == true
                && JudgeOk("Text", "lot-a", "LOT-A", ignoreCase: false) == false,
                JudgeDetail("Text", "lot-a", "LOT-A"));

            Check("[JT25] 文本·忽略空白：开=true / 关=false",
                JudgeOk("Text", "A B C", "ABC", ignoreWhitespace: true) == true
                && JudgeOk("Text", "A B C", "ABC", ignoreWhitespace: false) == false,
                JudgeDetail("Text", "A B C", "ABC"));

            Check("[JT26] 文本·编辑距离：允许 1 时 ABCD/ABCE 通过；允许 0 时失败",
                JudgeOk("Text", "ABCD", "ABCE", maxEditDistance: 1) == true
                && JudgeOk("Text", "ABCD", "ABCE", maxEditDistance: 0) == false,
                JudgeDetail("Text", "ABCD", "ABCE"));

            Check("[JT27] 文本·易混字符：配 0O 时 1027≡1O27；不配时失败",
                JudgeOk("Text", "1027", "1O27", confusables: "0O,1I,5S") == true
                && JudgeOk("Text", "1027", "1O27") == false,
                JudgeDetail("Text", "1027", "1O27"));

            Check("[JT28] 文本·中文按码点算距离：\"批号\" vs \"批\" 距离 1（不被拆成两字节）",
                JudgeOk("Text", "批号", "批", maxEditDistance: 1) == true
                && JudgeOk("Text", "批号", "批", maxEditDistance: 0) == false,
                JudgeDetail("Text", "批号", "批"));

            // ---- 集合 ----
            Check("[JT29] 集合·全部期望都在：实际多一项仍 OK；少一项 NG",
                JudgeOk("Set", "CODE-A, CODE-B", "CODE-A, CODE-B, CODE-D") == true
                && JudgeOk("Set", "CODE-A, CODE-B", "CODE-A") == false,
                JudgeDetail("Set", "CODE-A, CODE-B", "CODE-A"));

            Check("[JT30] 集合·完全一致：相等 OK；多一项 / 少一项都 NG",
                JudgeOk("Set", "A, B", "B, A", setSemantics: "Exact") == true
                && JudgeOk("Set", "A, B", "A, B, C", setSemantics: "Exact") == false
                && JudgeOk("Set", "A, B", "A", setSemantics: "Exact") == false,
                JudgeDetail("Set", "A, B", "A, B, C"));

            Check("[JT31] 集合·不许多余：实际是期望子集 OK；出现期望外的项 NG",
                JudgeOk("Set", "A, B", "A", setSemantics: "NoExtra") == true
                && JudgeOk("Set", "A, B", "A, C", setSemantics: "NoExtra") == false,
                JudgeDetail("Set", "A, B", "A, C"));

            Check("[JT32] 集合·忽略大小写 + 数组实际值：{a,b} 对 {A,B} 通过（集合来源支持数组）",
                JudgeOk("Set", "a, b", new[] { "A", "B" }, ignoreCase: true) == true
                && JudgeOk("Set", "a, b", new[] { "A", "C" }, ignoreCase: true) == false,
                JudgeDetail("Set", "a, b", new[] { "A", "C" }));

            // ---- 正则 ----
            Check("[JT33] 正则：^LOT-\\d{4}$ 命中 LOT-2026；LOT-26 不命中；非法模式明确失败",
                JudgeOk("Regex", @"^LOT-\d{4}$", "LOT-2026") == true
                && JudgeOk("Regex", @"^LOT-\d{4}$", "LOT-26") == false
                && JudgeOk("Regex", "[", "LOT-2026") == false,
                JudgeDetail("Regex", "[", "LOT-2026"));

            // ---- 布尔 ----
            Check("[JT34] 布尔：true 对 true 通过；true 对 false 失败；期望写错明确失败",
                JudgeOk("Boolean", "true", true) == true
                && JudgeOk("Boolean", "true", false) == false
                && JudgeOk("Boolean", "是", true) == false,
                JudgeDetail("Boolean", "是", true));

            // ---- 计数 ----
            Check("[JT35] 计数：21 ∈ [18,24] 通过；30 失败；字符串按项数、数组按元素数",
                JudgeOk("Count", "18 ~ 24", 21) == true
                && JudgeOk("Count", "18 ~ 24", 30) == false
                && JudgeOk("Count", "2 ~ 3", "A, B, C") == true
                && JudgeOk("Count", "2 ~ 3", new[] { "A" }) == false,
                JudgeDetail("Count", "18 ~ 24", 30));

            // ---- 非空 ----
            Check("[JT36] 非空：null / 空白文本 / 空数组 都失败；有内容的文本与数组通过",
                JudgeOk("NotEmpty", "", null) == false
                && JudgeOk("NotEmpty", "", "   ") == false
                && JudgeOk("NotEmpty", "", Array.Empty<string>()) == false
                && JudgeOk("NotEmpty", "", "x") == true
                && JudgeOk("NotEmpty", "", new[] { 1 }) == true,
                JudgeDetail("NotEmpty", "", "   "));

            // ---- 期望为空 / 未接线：都不许静默通过 ----
            Check("[JT37] 期望为空 → 不通过并写明原因（不静默通过）",
                JudgeOk("Numeric", "", 12) == false && JudgeReason("Numeric", "", 12).Contains("没有填写期望值"),
                JudgeReason("Numeric", "", 12));
        }

        // ==================================================================
        //  ⑥ 持久化：StepModel JSON 往返 + 重新 Initialize 重建端口
        // ==================================================================
        private static void RunPersistence()
        {
            var step = new ActionStep("", "判定表", PluginTypeName, "工位A判定");
            var plugin = (VisionPluginBase)Activator.CreateInstance(PluginAssembly.GetType(PluginTypeName, true)!)!;
            plugin.Initialize(step);

            var rules = RulesOf(plugin);
            rules.Add(NewRule("外径", "Numeric", "12.00 ± 0.05"));
            rules.Add(NewRule("主码", "Set", "CODE-A, CODE-B"));
            plugin.OnConfirm(step);

            string json = JsonConvert.SerializeObject(step, typeof(StepModel), SolutionService.SerializationSettings);
            var back = JsonConvert.DeserializeObject<StepModel>(json, SolutionService.SerializationSettings) as ActionStep;

            bool rulesKept = back != null
                             && back.InputValues.TryGetValue("Rules", out var raw)
                             && raw is IEnumerable enumerable
                             && enumerable.Cast<object>().Count() == 2
                             && enumerable.Cast<object>().Select(r => GetString(r, "Name"))
                                 .SequenceEqual(new[] { "外径", "主码" });
            Check("[JT38] StepModel JSON 往返保留规则表（名字/顺序不变）",
                rulesKept,
                back == null ? "反序列化得到 null（$type 白名单没放行？）" : DescribeRules(back));

            bool snapshotKept = back != null
                                && back.OutputPortDefinitions.Count == 2
                                && back.OutputPortDefinitions.All(d => d.DataTypeName == typeof(bool).AssemblyQualifiedName)
                                && back.OutputPortDefinitions.Any(d => d.Name == "工位A判定.外径.结果");
            Check("[JT39] 往返保留 OutputPortDefinitions（名字 + bool 类型）",
                snapshotKept,
                back == null ? "null" : string.Join(",", back.OutputPortDefinitions.Select(d => d.Name)));

            Check("[JT40] 运行期回显不落盘（JSON 里没有 LastActual / LastOk / LastReason）",
                !json.Contains("LastActual", StringComparison.Ordinal)
                && !json.Contains("LastOk", StringComparison.Ordinal)
                && !json.Contains("LastReason", StringComparison.Ordinal),
                "含运行期字段：" + string.Join(",",
                    new[] { "LastActual", "LastOk", "LastReason" }.Where(k => json.Contains(k, StringComparison.Ordinal))));

            // 重新 Initialize 一个新实例 → 端口按反序列化后的规则重建
            if (back != null)
            {
                var reopened = (VisionPluginBase)Activator.CreateInstance(PluginAssembly.GetType(PluginTypeName, true)!)!;
                reopened.Initialize(back);
                Check("[JT41] 重新 Initialize 后端口按规则重建（输入 2 / 输出 2）",
                    DynamicInputs(reopened).SetEquals(new[] { "实际_外径", "实际_主码" })
                    && DynamicOutputs(reopened).SetEquals(new[] { "工位A判定.外径.结果", "工位A判定.主码.结果" }),
                    $"输入=[{Join(DynamicInputs(reopened))}] 输出=[{Join(DynamicOutputs(reopened))}]");
                reopened.Dispose();
            }

            plugin.Dispose();
        }

        // ==================================================================
        //  ⑦ 动态输入未接 → 该规则失败、Details 含端口名、不抛异常
        // ==================================================================
        private static void RunUnlinkedInput()
        {
            var workspace = NewWorkspace();
            var plugin = NewPlugin("工位B判定", out _);
            var p = (VisionPluginBase)plugin;
            var rules = RulesOf(plugin);
            rules.Add(NewRule("已接", "Numeric", "1 ± 0.1"));
            rules.Add(NewRule("未接", "Numeric", "1 ± 0.1"));
            rules.Add(NewRule("空上游", "Numeric", "1 ± 0.1"));

            SetActual(p, "实际_已接", 1.0);
            // 「实际_未接」故意不设值也不连线
            // 「实际_空上游」连一个值为 null 的上游（"连线了但上游没出值"）
            p.Inputs["实际_空上游"].LinkedSource = new OutputPort<object>("上游空值", "断言用空值上游");

            bool threw = false;
            var ctx = NewContext(workspace, new StubLog());
            try { p.Execute(ctx); }
            catch (Exception ex) { threw = true; Console.WriteLine("      未接线用例抛异常：" + ex.Message); }

            var details = p.Outputs["Details"].Value as string[] ?? Array.Empty<string>();
            Check("[JT42] 动态输入未接 → 该规则失败、Details 含端口名、整体不抛异常",
                !threw
                && p.Success.Value is false
                && p.Outputs["Results"].Value is bool[] r && r.Length == 3 && r[0] && !r[1] && !r[2]
                && details.Length == 3
                && details[1].Contains("实际_未接")
                && details[1].Contains("未连线"),
                string.Join(" | ", details));

            Check("[JT43] 三种「没有值」分开说：未连线 / 上游值为空（不合并成一句）",
                details.Length == 3
                && details[1].Contains("未连线")
                && details[2].Contains("上游值为空")
                && !details[1].Contains("上游值为空"),
                string.Join(" | ", details));

            p.Dispose();
        }

        // ==================================================================
        //  ⑨ 数据源接线：端口对象随重建换新 + 回显与连线同源
        // ==================================================================
        /// <summary>
        /// 断言「数据源」那一行（<c>LinkableValueEditor</c>）依赖的两件事：
        ///
        /// ① 视图经 <c>InputPortConverter</c>（端口名 + 插件 + <c>PortsVersion</c> 三路 MultiBinding）
        ///    调 <c>GetInputPort</c> 取端口，取到的必须是**当前**对象。改名 / 启停 / 增删会重建端口
        ///    （旧对象被 RemoveDynamicInput + Dispose），若还取得到旧名字的对象，编辑器就会连着
        ///    已丢弃的实例 —— 手填常量不落盘、🔗 把连线写到旧端口名上。
        /// ② <c>LinkAddress</c> 用 **null** 表示未连线（控件按"非空即已链接"决定渲染形态），
        ///    且它与显示串 <c>SourceText</c> 同源 —— 写两个属性就会出现"框里显示已连、
        ///    折叠列还写着未连线"。
        /// </summary>
        private static void RunSourceWiring()
        {
            var plugin = NewPlugin("接线判定", out var step);
            var p = (VisionPluginBase)plugin;
            var rules = RulesOf(plugin);
            var rule = NewRule("外径", "Numeric", "12.00 ± 0.05");
            rules.Add(rule);
            void RebuildAll() => plugin.GetType().GetMethod("RebuildAll")!.Invoke(plugin, null);
            object? GetInputPort(string name)
                => plugin.GetType().GetMethod("GetInputPort")!.Invoke(plugin, new object?[] { name });

            // 视图那一行经 InputPortConverter 按名取端口（R189），取到的必须是**当前**对象
            var port = GetInputPort("实际_外径") as IInputPort;
            bool portBound = port != null
                             && p.Inputs.TryGetValue("实际_外径", out var live)
                             && ReferenceEquals(live, port);

            // 改名 → 端口重建：旧名字必须取不到（否则编辑器会连着已 Dispose 的旧对象），
            // 新名字取到的是新实例
            Set(rule, "Name", "外径复检");
            var renamed = GetInputPort("实际_外径复检") as IInputPort;
            bool portFollows = GetInputPort("实际_外径") == null
                               && renamed != null
                               && !ReferenceEquals(renamed, port)
                               && p.Inputs.TryGetValue("实际_外径复检", out var liveRenamed)
                               && ReferenceEquals(liveRenamed, renamed);

            Check("[JT46] GetInputPort（InputPortConverter 的落点）按名取当前端口：改名后旧名取不到、新名是新实例",
                portBound && portFollows,
                $"初值={(port != null ? port.Name : "<null>")} 改名后={(renamed != null ? renamed.Name : "<null>")} "
                + $"旧名仍可取={GetInputPort("实际_外径") != null} 仍是同一对象={ReferenceEquals(renamed, port)}");

            // 回显与连线同源：null = 未连线；SetLink / RemoveLink 后两处同步
            step.SetLink("实际_外径复检", new LinkReference(
                LinkKind.StepPort, Guid.NewGuid(), "Score", "局部形变匹配.Score"));
            RebuildAll();
            string linkedAddress = (Get(rule, "LinkAddress") as string) ?? "null";
            string linkedText = GetString(rule, "SourceText");
            bool linked = linkedAddress == "局部形变匹配.Score" && linkedText == "局部形变匹配.Score";

            step.RemoveLink("实际_外径复检");
            RebuildAll();
            string unlinkedAddress = (Get(rule, "LinkAddress") as string) ?? "null";
            string unlinkedText = GetString(rule, "SourceText");
            bool unlinked = unlinkedAddress == "null" && unlinkedText == "未连线";

            Check("[JT47] 连线状态回显：未连线时 LinkAddress=null（不是「未连线」文案），SetLink/RemoveLink 后与显示串同步",
                linked && unlinked,
                $"已连[{linkedAddress} / {linkedText}] 解绑[{unlinkedAddress} / {unlinkedText}]");

            p.Dispose();
        }

        // ==================================================================
        //  ⑩ 取消回滚：连线是立即写活模型的，规则却是草稿
        // ==================================================================
        /// <summary>
        /// 断言「取消配置要把连线回滚到打开时的快照」。
        ///
        /// 规则只有 <c>OnConfirm</c> 才回写 stepData，连线却是立即写活模型的
        /// （单绑定弹窗只回传 <c>LinkReference</c>，写回在插件侧）。不回滚就会出现两种不一致：
        /// ① 改完规则点取消 → 规则没保存、连线却留下了；
        /// ② 新增规则 → 绑定 → 取消 → 留下没有同名端口的孤儿键（规则没保存），
        ///    下一次打开该步骤或编译时才会被 <c>SyncInputLinks</c> 清掉。
        /// 反向同时验：打开时就存在的连线不许被取消误删；确认之后快照作废，再取消也不许倒回去。
        /// </summary>
        private static void RunCancelRollback()
        {
            // ① 先造一条"打开配置时就存在"的连线：规则已提交 + 连线已写
            var step = new FakeStepData { StepName = "回滚判定" };
            var first = NewPluginOn(step);
            RulesOf(first).Add(NewRule("外径", "Numeric", "12.00 ± 0.05"));
            step.SetLink("实际_外径", ScoreLink("局部形变匹配.Score"));
            first.OnConfirm(step);
            first.Dispose();

            // ② 重新打开（快照拍到这里已有的连线）→ 会话内改绑 + 新增一条 → 取消
            var reopened = NewPluginOn(step);
            var rules = RulesOf(reopened);
            bool snapshotTaken = step.IsLinked("实际_外径");

            step.SetLink("实际_外径", ScoreLink("模板匹配.Score"));   // 会话内改绑已有规则
            rules.Add(NewRule("高度", "Numeric", "8.00 ± 0.10"));      // 会话内新增规则
            step.SetLink("实际_高度", ScoreLink("模板匹配.Score"));
            reopened.OnCancel();

            bool restored = string.Equals(
                step.GetLinkedAddress("实际_外径"), "局部形变匹配.Score", StringComparison.Ordinal);
            bool addedGone = !step.IsLinked("实际_高度");
            bool textSynced = SourceOf(rules, "高度") == "未连线"
                              && SourceOf(rules, "外径") == "局部形变匹配.Score";

            Check("[JT49] 取消配置回滚连线：会话内改绑的恢复原值、新绑的线撤掉、回显同步",
                snapshotTaken && restored && addedGone && textSynced,
                $"快照={snapshotTaken} 改绑已还原={restored} 新绑已撤={addedGone} 回显同步={textSynced} "
                + $"[外径={step.GetLinkedAddress("实际_外径") ?? "<无>"} 高度={SourceOf(rules, "高度")}]");

            // ③ 确认（含试运行前的参数同步）之后快照作废：再点取消不许把已提交的连线倒回去
            var confirmed = NewPluginOn(step);
            step.SetLink("实际_外径", ScoreLink("模板匹配.Score"));
            confirmed.OnConfirm(step);
            confirmed.OnCancel();
            bool committedKept = string.Equals(
                step.GetLinkedAddress("实际_外径"), "模板匹配.Score", StringComparison.Ordinal);

            Check("[JT50] 确认之后快照作废：再点取消不会把已提交的连线倒回去",
                committedKept,
                $"确认后取消仍在线={committedKept} 地址={step.GetLinkedAddress("实际_外径") ?? "<无>"}");

            reopened.Dispose();
            confirmed.Dispose();
        }

        /// <summary>
        /// 数据源类型收窄：HALCON 图像/区域绑进判定表时，运行期要给"类型不支持 + 该绑什么"，
        /// 而不是把它 ToString 成类型名去参与比较（用户只会看到看不懂的 NG）。
        /// 替身类型定义在文件末尾的 <c>HalconDotNet</c> 命名空间里 —— 判据就是命名空间前缀。
        /// </summary>
        private static void RunUnsupportedActual()
        {
            var method = PluginAssembly.GetType(PluginTypeName, true)!
                .GetMethod("DescribeUnsupportedActual", BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                Check("[JT51] 数据源类型收窄：图像/区域判为不支持，数值/文本/普通集合照旧放行", false,
                    "DescribeUnsupportedActual 不存在（判据被改名或删掉？）");
                return;
            }

            string? Kind(object value) => (string?)method.Invoke(null, new[] { value });

            var image = Kind(new HalconDotNet.FakeJudgeActual());
            var array = Kind(new object[] { new HalconDotNet.FakeJudgeActual() });
            var number = Kind(12.5);
            var text = Kind("CODE-A");
            var set = Kind(new List<double> { 1, 2 });

            Check("[JT51] 数据源类型收窄：图像/区域判为不支持，数值/文本/普通集合照旧放行",
                image == "FakeJudgeActual" && array == "FakeJudgeActual"
                && number == null && text == null && set == null,
                $"对象={image ?? "<放行>"} 数组={array ?? "<放行>"} 数值={number ?? "<放行>"} "
                + $"文本={text ?? "<放行>"} 集合={set ?? "<放行>"}");
        }

        private static LinkReference ScoreLink(string address)
            => new(LinkKind.StepPort, Guid.NewGuid(), "Score", address);

        /// <summary>按规则名取「数据源」回显串（规则集合是运行期草稿，按名字找最稳）</summary>
        private static string SourceOf(IList rules, string name)
        {
            foreach (var rule in rules)
            {
                if (rule != null && GetString(rule, "Name") == name)
                    return GetString(rule, "SourceText");
            }
            return "<缺规则>";
        }

        /// <summary>在既有 stepData 上开一个插件实例（与 <see cref="NewPlugin"/> 的差别：不换步骤）</summary>
        private static VisionPluginBase NewPluginOn(FakeStepData step)
        {
            var plugin = (VisionPluginBase)Activator.CreateInstance(
                PluginAssembly.GetType(PluginTypeName, true)!)!;
            plugin.InstanceName = step.StepName;
            plugin.Initialize(step);
            return plugin;
        }

        private static void RunViewSmoke()
        {
            Exception? viewError = null;
            var sta = new Thread(() =>
            {
                try
                {
                    var plugin = NewPlugin("工位A判定", out _);
                    var rules = RulesOf(plugin);
                    rules.Add(NewRule("外径", "Numeric", "12.00 ± 0.05"));
                    rules.Add(NewRule("主码", "Set", "CODE-A, CODE-B"));
                    Set((object)rules[0]!, "IsExpanded", true);   // 让展开行（判定规则 + 结果输出卡片）真的被实例化

                    var viewType = PluginAssembly.GetType(ViewTypeName, true)!;
                    var view = (FrameworkElement)Activator.CreateInstance(viewType)!;
                    view.DataContext = plugin;
                    view.Measure(new Size(1000, 620));
                    view.Arrange(new Rect(0, 0, 1000, 620));
                    view.UpdateLayout();
                    ((VisionPluginBase)plugin).Dispose();
                }
                catch (Exception ex) { viewError = ex; }
            });
            sta.SetApartmentState(ApartmentState.STA);
            sta.Start();
            sta.Join();

            Check("[JT44] JudgeTableView 实例化 + 布局无错（XAML 解析 / 资源键 / 模板 / 绑定）",
                viewError == null, viewError?.Message ?? "");

            var xamlPath = ResolveRepoFile(@"Plugins\Plugin.JudgeTable\JudgeTableView.xaml");
            if (xamlPath == null)
            {
                Check("[JT45] 视图静态扫描（GridListViewStyle / GridViewRowPresenter / 零十六进制）", false,
                    "定位不到 JudgeTableView.xaml（文件被移动/改名？）");
                return;
            }

            var xaml = File.ReadAllText(xamlPath);
            int hex = Regex.Matches(Regex.Replace(xaml, "<!--.*?-->", "", RegexOptions.Singleline),
                "#[0-9A-Fa-f]{6}").Count;
            Check("[JT45] 视图静态扫描：GridView 三件套齐全 + 零十六进制颜色（R19-R22 / R140）",
                xaml.Contains("GridListViewStyle", StringComparison.Ordinal)
                && xaml.Contains("GridViewRowPresenter", StringComparison.Ordinal)
                && xaml.Contains("Controls/GridView.xaml", StringComparison.Ordinal)
                && hex == 0,
                $"GridListViewStyle={xaml.Contains("GridListViewStyle", StringComparison.Ordinal)} "
                + $"GridViewRowPresenter={xaml.Contains("GridViewRowPresenter", StringComparison.Ordinal)} "
                + $"就地合并={xaml.Contains("Controls/GridView.xaml", StringComparison.Ordinal)} 硬编码颜色={hex}");

            // 「数据源」那一行必须复用全仓通用的绑定控件，且按 R189 的强制范式接线：
            // MultiBinding（端口名 + 插件 + PortsVersion）经插件自己的 InputPortConverter 取端口，
            // 按需实例化（ContentControl + DataTemplate）。
            // 绑定路径写错不会抛异常，只会静默渲染成一行空白，光靠 JT44「实例化不抛」查不出来。
            bool hasNs = xaml.Contains("xmlns:cc=\"clr-namespace:Core.Controls;assembly=Core.Controls\"", StringComparison.Ordinal);
            bool hasConverter = xaml.Contains("<local:InputPortConverter x:Key=\"InputPortConv\" />", StringComparison.Ordinal);
            bool hasControl = xaml.Contains("<cc:LinkableValueEditor", StringComparison.Ordinal);
            bool hasMulti = xaml.Contains("<MultiBinding Converter=\"{StaticResource InputPortConv}\">", StringComparison.Ordinal);
            bool hasNamePath = xaml.Contains("<Binding Path=\"InputPortName\" />", StringComparison.Ordinal);
            bool hasPortsVersion = xaml.Contains("Path=\"DataContext.PortsVersion\"", StringComparison.Ordinal);
            bool hasOnDemand = xaml.Contains("ContentTemplate=\"{StaticResource RulePortEditorTemplate}\"", StringComparison.Ordinal);
            bool hasLink = xaml.Contains("LinkAddress=\"{Binding LinkAddress, Mode=TwoWay}\"", StringComparison.Ordinal);
            bool hasStepData = xaml.Contains("StepData=\"{Binding DataContext.StepData,", StringComparison.Ordinal);
            bool noCachedPort = !xaml.Contains("Port=\"{Binding InputPort}\"", StringComparison.Ordinal);

            // R189 另一条：不许在插件里自己发 LinkPathEvent 手搓绑定按钮
            var pluginSrc = ResolveRepoFile(@"Plugins\Plugin.JudgeTable\JudgeTablePlugin.cs");
            bool noHandWrittenEvent = pluginSrc != null
                                      && !File.ReadAllText(pluginSrc).Contains("LinkPathEvent", StringComparison.Ordinal);

            Check("[JT48] 数据源行按 R189 接线：通用控件 + MultiBinding(端口名/插件/PortsVersion) + 按需实例化，且无手搓 LinkPathEvent",
                hasNs && hasConverter && hasControl && hasMulti && hasNamePath && hasPortsVersion
                && hasOnDemand && hasLink && hasStepData && noCachedPort && noHandWrittenEvent,
                $"xmlns:cc={hasNs} 转换器={hasConverter} 控件={hasControl} MultiBinding={hasMulti} "
                + $"端口名={hasNamePath} PortsVersion={hasPortsVersion} 按需实例化={hasOnDemand} "
                + $"LinkAddress={hasLink} StepData={hasStepData} 未缓存Port={noCachedPort} 无手搓事件={noHandWrittenEvent}");
        }

        // ==================================================================
        //  反射门面（类型 / 属性 / 方法）
        // ==================================================================
        private static Assembly? _assembly;

        private static Assembly PluginAssembly => _assembly ??= LoadPluginAssembly();

        private static Assembly LoadPluginAssembly()
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Plugin.JudgeTable");
            if (loaded != null) return loaded;

            var repoRoot = RepoRoot();
            var candidates = new[]
            {
                Path.Combine(repoRoot, @"Modules\Plugin.JudgeTable.dll"),
                Path.Combine(repoRoot, @"Plugins\Plugin.JudgeTable\bin\Debug\net9.0-windows\Plugin.JudgeTable.dll"),
            };
            foreach (var path in candidates)
                if (File.Exists(path)) return Assembly.LoadFrom(path);

            throw new FileNotFoundException(
                "找不到 Plugin.JudgeTable.dll（先构建插件工程）：" + string.Join(" / ", candidates));
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) return dir.FullName;
            }
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\"));
        }

        private static string? ResolveRepoFile(string relative)
        {
            var root = RepoRoot();
            var candidate = Path.Combine(root, relative);
            return File.Exists(candidate) ? candidate : null;
        }

        private static Type RuleType => PluginAssembly.GetType(RuleTypeName, true)!;
        private static Type ModeType => PluginAssembly.GetType(ModeEnumName, true)!;
        private static Type SetSemanticsType => PluginAssembly.GetType(SetSemanticsEnumName, true)!;
        private static Type EngineType => PluginAssembly.GetType(EngineTypeName, true)!;

        private static object NewPlugin(string stepName, out FakeStepData stepData)
        {
            var step = new FakeStepData { StepName = stepName };
            stepData = step;
            var plugin = (VisionPluginBase)Activator.CreateInstance(PluginAssembly.GetType(PluginTypeName, true)!)!;
            plugin.InstanceName = stepName;
            plugin.Initialize(step);
            return plugin;
        }

        private static IList RulesOf(object plugin)
            => (IList)(plugin.GetType().GetProperty("Rules")!.GetValue(plugin)
                       ?? throw new InvalidOperationException("Rules 为 null"));

        private static object NewRule(
            string name, string mode, string expected,
            double tolerance = 0, bool generateResult = true, bool enabled = true,
            bool ignoreCase = false, bool ignoreWhitespace = false, int maxEditDistance = 0,
            string confusables = "", string setSemantics = "AllExpected")
        {
            var rule = Activator.CreateInstance(RuleType)!;
            Set(rule, "Name", name);
            Set(rule, "Mode", Enum.Parse(ModeType, mode));
            Set(rule, "Expected", expected);
            Set(rule, "Tolerance", tolerance);
            Set(rule, "GenerateResult", generateResult);
            Set(rule, "Enabled", enabled);
            Set(rule, "IgnoreCase", ignoreCase);
            Set(rule, "IgnoreWhitespace", ignoreWhitespace);
            Set(rule, "MaxEditDistance", maxEditDistance);
            Set(rule, "Confusables", confusables);
            Set(rule, "SetSemantics", Enum.Parse(SetSemanticsType, setSemantics));
            return rule;
        }

        private static void Set(object target, string property, object? value)
            => target.GetType().GetProperty(property)!.SetValue(target, value);

        private static object? Get(object target, string property)
            => target.GetType().GetProperty(property)!.GetValue(target);

        private static string GetString(object target, string property)
            => target.GetType().GetProperty(property)!.GetValue(target)?.ToString() ?? string.Empty;

        // ---- 判定内核（纯函数层） ----
        private static object Judge(string mode, string expected, object? actual,
            double tolerance = 0, bool ignoreCase = false, bool ignoreWhitespace = false,
            int maxEditDistance = 0, string confusables = "", string setSemantics = "AllExpected")
        {
            var rule = NewRule("断言", mode, expected, tolerance, true, true,
                ignoreCase, ignoreWhitespace, maxEditDistance, confusables, setSemantics);
            var evaluate = EngineType.GetMethod("Evaluate", new[] { RuleType, typeof(object) })
                           ?? throw new InvalidOperationException("JudgeEngine.Evaluate 签名变了");
            return evaluate.Invoke(null, new[] { rule, actual })!;
        }

        private static bool JudgeOk(string mode, string expected, object? actual,
            double tolerance = 0, bool ignoreCase = false, bool ignoreWhitespace = false,
            int maxEditDistance = 0, string confusables = "", string setSemantics = "AllExpected")
        {
            var outcome = Judge(mode, expected, actual, tolerance, ignoreCase, ignoreWhitespace,
                maxEditDistance, confusables, setSemantics);
            return (bool)outcome.GetType().GetProperty("IsOk")!.GetValue(outcome)!;
        }

        private static string JudgeReason(string mode, string expected, object? actual)
        {
            var outcome = Judge(mode, expected, actual);
            return outcome.GetType().GetProperty("Reason")!.GetValue(outcome)?.ToString() ?? string.Empty;
        }

        private static string JudgeDetail(string mode, string expected, object? actual)
            => $"{mode} 期望[{expected}] 实际[{Describe(actual)}] → {JudgeReason(mode, expected, actual)}";

        // ---- 端口 / 结果读取 ----
        private static HashSet<string> DynamicInputs(VisionPluginBase p)
            => new(p.Inputs.Keys.Where(k => k.StartsWith("实际_", StringComparison.Ordinal)), StringComparer.Ordinal);

        private static HashSet<string> DynamicOutputs(VisionPluginBase p)
            => new(p.Outputs.Keys.Where(k => k.EndsWith(".结果", StringComparison.Ordinal)), StringComparer.Ordinal);

        private static bool? ReadBool(VisionPluginBase p, string portName)
            => p.Outputs.TryGetValue(portName, out var port) && port.Value is bool b ? b : null;

        /// <summary>写动态输入端口的手动值（未连线时即为实际值）</summary>
        private static void SetActual(VisionPluginBase p, string portName, object value)
        {
            if (!p.Inputs.TryGetValue(portName, out var port))
                throw new InvalidOperationException($"输入端口不存在：{portName}（现有 [{Join(new HashSet<string>(p.Inputs.Keys, StringComparer.Ordinal))}]）");
            port.Value = value;
        }

        private static string ShortType(string? assemblyQualified)
        {
            if (string.IsNullOrEmpty(assemblyQualified)) return "<空>";
            int comma = assemblyQualified!.IndexOf(',');
            return comma > 0 ? assemblyQualified.Substring(0, comma) : assemblyQualified;
        }

        private static string Describe(object? value) => value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            IEnumerable e and not string => "[" + Join(e.Cast<object?>().Select(x => x?.ToString() ?? "null")) + "]",
            _ => value.ToString() ?? "null",
        };

        private static string Join(IEnumerable<string> items) => string.Join(",", items);

        private static string Join(IEnumerable<bool> items) => string.Join(",", items.Select(b => b ? "true" : "false"));

        private static string DescribeRules(StepModel? step)
        {
            if (step == null || !step.InputValues.TryGetValue("Rules", out var raw) || raw is not IEnumerable e)
                return "没有 Rules 键";
            return Join(e.Cast<object>().Select(r => GetString(r, "Name")));
        }

        // ---- 全局变量快照（"不污染全局"的判据） ----
        private static WorkspaceContext NewWorkspace()
        {
            var workspace = new WorkspaceContext();
            workspace.GlobalVariables.Clear();   // 去掉框架预置的演示变量，免得数不清
            workspace.GlobalVariables.Add(VariableFactory.CreateLocal("OKCount", typeof(int), "断言用计数变量"));
            workspace.GlobalVariables.Add(VariableFactory.CreateLocal("SN", typeof(string), "断言用文本变量"));
            var solution = new SolutionModel { SolutionName = "判定表断言" };
            var flow = new FlowModel { FlowName = "判定表断言流程" };
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);
            workspace.SwitchFlow(flow);
            return workspace;
        }

        private static List<(string Name, object? Value)> SnapshotGlobals(WorkspaceContext workspace)
            => workspace.GlobalVariables.Select(v => (v.Name, v.Value)).ToList();

        private static bool GlobalsUnchanged(WorkspaceContext workspace, List<(string Name, object? Value)> before)
        {
            if (workspace.GlobalVariables.Count != before.Count) return false;
            for (int i = 0; i < before.Count; i++)
            {
                var current = workspace.GlobalVariables[i];
                if (!string.Equals(current.Name, before[i].Name, StringComparison.Ordinal)) return false;
                // 值相等即可（引用类型走引用相等，值类型走内容相等）——
                // 判定表"不写全局"的判据是"一个都没被动过"，不是"没换成等值的新对象"
                if (!Equals(current.Value, before[i].Value)) return false;
            }
            return true;
        }

        private static string DescribeGlobals(WorkspaceContext workspace)
            => string.Join(",", workspace.GlobalVariables.Select(v => $"{v.Name}={v.Value ?? "null"}"));

        private static ExecutionContext NewContext(WorkspaceContext workspace, StubLog log)
            => new(log, new FlowSession { FlowName = "判定表断言流程" }, workspace,
                new CancellationTokenSource().Token);

        /// <summary>测试用步骤数据：字典 + 连线表兜底的 IStepConfigData 最小实现</summary>
        private sealed class FakeStepData : IStepConfigData
        {
            public Guid StepId { get; } = Guid.NewGuid();
            public string Icon { get; } = "";
            public string StepName { get; set; } = "Fake";
            public string Description { get; set; } = "";
            public Dictionary<string, object> InputValues { get; } = new();
            public Dictionary<string, LinkReference> Links { get; } = new(StringComparer.Ordinal);
            public List<DynamicPortInfo> OutputPortDefinitions { get; set; } = new();

            public void SetInputValue(string key, object value) => InputValues[key] = value;
            public void RemoveInputValue(string key) => InputValues.Remove(key);
            public bool IsLinked(string inputPortName) => Links.ContainsKey(inputPortName ?? string.Empty);
            public string GetLinkedAddress(string inputPortName)
                => Links.TryGetValue(inputPortName ?? string.Empty, out var link) ? link.DisplayAddress : null!;
            public LinkReference GetLink(string inputPortName)
                => Links.TryGetValue(inputPortName ?? string.Empty, out var link) ? link : null!;
            public void SetLink(string inputPortName, LinkReference link) => Links[inputPortName] = link;
            public void RemoveLink(string inputPortName) => Links.Remove(inputPortName ?? string.Empty);
        }
    }
}

namespace HalconDotNet
{
    /// <summary>
    /// 测试替身：验证「按命名空间前缀判断不可判定数据源」这条判据
    /// （<c>Plugin.JudgeTable.DescribeUnsupportedActual</c>）。
    /// 真实 HALCON 类型不能在这里造（要运行时与许可证），而判据只看 <c>FullName</c> 前缀，
    /// 替身足以证明"图像/区域会被拦、数值/文本/普通集合照旧放行"。
    /// </summary>
    internal sealed class FakeJudgeActual { }
}
