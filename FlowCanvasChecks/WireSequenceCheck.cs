using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Core.Events;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
// 本文件 using 了 System.Threading，裸写的 ExecutionContext 会和 System.Threading.ExecutionContext 撞名
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 「多产品线序检测」方案（解决方案\线序检测.vms）的端到端检查。
    ///
    /// 这份方案要证明的不再是"能扫出线序"，而是**分支真的按芯数走对了路**：
    ///   5 芯  → If      → 配方 A → 逐位比对 → 合格
    ///   10 芯 → ElseIf  → 配方 B → 逐位比对 → 合格
    ///   其它  → Else    → 无配方 → 明确报"未知产品"（不许含糊成"线数不符"）
    /// 三条路都要真跑一遍。只验第一条的话，ElseIf 的条件写错、Else 分支漏建，测试照样全绿。
    ///
    /// 为什么必须走「真加载 → 真编译 → 真执行」
    /// ---------
    /// ① <c>$type</c> 白名单 —— 插件自带的配置类型（ScriptVarDef）必须被放行，否则表现是"方案打不开"；
    /// ② 动态端口重建 —— 脚本的输入/输出端口由 InputVars/OutputVars 现建，建晚了按名连线就找不到端口；
    /// ③ 脚本正文由 Roslyn 在运行期才编译，编译错误只有跑起来才现形；
    /// ④ 分支条件由 DynamicExpresso 在运行期对运行时变量求值 —— 变量名拼错、类型不匹配，
    ///    表现为"所有分支集体不走、流程还照常往下跑"，是最难查的静默故障。
    ///
    /// 关于「脚本引用集」的间接覆盖
    /// ---------
    /// halcondotnet 在 deps.json 里登记为 <c>type: reference</c>，这类库不进运行期 TPA，
    /// 于是脚本引擎的 Roslyn 引用集一度取决于"编译脚本那一刻 HalconDotNet 有没有被加载过"——
    /// 主程序里报过「The type or namespace name 'HImage' could not be found」，而无界面宿主里同一份脚本却能编译。
    /// 本条检查不直接断言引用集内部（GetOptions 是私有的），而是用「脚本步骤自报执行成功」覆盖它：
    /// 引用集一旦残缺，脚本就编译不过，那一条会立刻变红。
    /// </summary>
    internal static class WireSequenceCheck
    {
        private const string FlowName = "线序检测";

        /// <summary>5 芯样本 cable1.png 的黄金线序</summary>
        private const string Golden5 = "黑,棕,玫红,红,黄";

        /// <summary>10 芯样本 cable2.png 的黄金线序（白色那根靠等间距规整化补回来的）</summary>
        private const string Golden10 = "黑,白,灰,紫,蓝,绿,黄,红,玫红,棕";

        public static void Run()
        {
            Section("[W] 多产品线序检测方案：加载 → 编译 → 三分支各跑一遍 → 断言结果与投射");

            // 仓库根：本工程输出在 FlowCanvasChecks\bin\<配置>\net9.0-windows\，上溯四级即仓库根
            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            var vmsPath = Path.Combine(repoRoot, "解决方案", "线序检测.vms");
            var imgDir = Path.Combine(repoRoot, "Image", "颜色");

            Check("方案文件存在", File.Exists(vmsPath), vmsPath);
            if (!File.Exists(vmsPath)) return;

            // ---- W1 三个插件必须先装进默认 ALC ----
            // 与 HttpImageSmoke 同因：FlowCanvasChecks 的 bin 里没有 Plugin.*.dll。
            // Plugin.Util 是「变量赋值」所在的程序集 —— 分支体里全是它，漏装的表现是编译期 Type.GetType 找不到类型。
            var modules = Path.Combine(repoRoot, "Modules");
            var loadError = "";
            foreach (var dll in new[] { "Plugin.ImageAcquisition.dll", "Plugin.CSharpScript.dll", "Plugin.Util.dll" })
            {
                try { Assembly.LoadFrom(Path.Combine(modules, dll)); }
                catch (Exception ex) { loadError += $"{dll}: {ex.Message}; "; }
            }
            Check("采集 / 脚本 / 变量赋值 三个插件程序集可装载", loadError.Length == 0, loadError);

            // ---- W2 用生产加载器打开（含 $type 白名单校验）----
            var loaded = new SolutionService().LoadAsync(vmsPath).GetAwaiter().GetResult();
            Check("生产加载器能打开这份方案", loaded.Success && loaded.Data != null, loaded.Message);
            if (!loaded.Success || loaded.Data == null) return;

            var solution = loaded.Data;
            var flow = solution.Flows.FirstOrDefault();
            Check("方案里只有一条流程，且名为「线序检测」",
                solution.Flows.Count == 1 && flow?.FlowName == FlowName,
                $"流程数={solution.Flows.Count} 名称={flow?.FlowName}");
            if (flow == null) return;

            // ---- W3 流程骨架 ----
            // 只断言"主干在"，不断言"总数正好 5" —— 用户往流程后面再挂节点（例如加一个变量赋值把图写进全局变量）
            // 是完全正常的编辑，钉死总数会让断言在用户正常操作后无谓变红。
            var trunk = new[] { "图像采集_0", "找线", "选配方", "比对", "结果分流" };
            Check("流程主干前 5 步依次是 采集 → 找线 → 选配方 → 比对 → 结果分流",
                flow.Steps.Count >= 5 && flow.Steps.Take(5).Select(s => s.StepName).SequenceEqual(trunk),
                $"步骤数={flow.Steps.Count}: {string.Join(" → ", flow.Steps.Select(s => s.StepName))}");
            if (flow.Steps.Count < 5) return;

            var collect = flow.Steps[0];
            var scan = flow.Steps[1];
            var pick = flow.Steps[2] as ConditionStep;
            var compare = flow.Steps[3];
            var route = flow.Steps[4] as ConditionStep;

            Check("步骤类型依次是 采集 / 脚本 / 条件分支 / 脚本 / 条件分支",
                collect.PluginName == "图像采集" && scan.PluginName == "C#脚本"
                && pick != null && compare.PluginName == "C#脚本" && route != null,
                $"{collect.PluginName} / {scan.PluginName} / {pick?.PluginName} / {compare.PluginName} / {route?.PluginName}");
            if (pick == null || route == null) return;

            // ---- W4 选配方：If / ElseIf / Else 三路 ----
            Check("选配方有 3 个分支（If / ElseIf / Else）",
                pick.Children.Count == 3
                && pick.Children[0].BranchType == BranchType.If
                && pick.Children[1].BranchType == BranchType.ElseIf
                && pick.Children[2].BranchType == BranchType.Else,
                $"分支={string.Join(",", pick.Children.Select(c => c.BranchType.ToString()))}");

            Check("If 的条件是「5 芯」、ElseIf 的条件是「10 芯」",
                pick.Children.Count == 3
                && (pick.Children[0].Expression ?? "").Replace(" ", "") == "WireCount==5"
                && (pick.Children[1].Expression ?? "").Replace(" ", "") == "WireCount==10",
                $"If=[{pick.Children.ElementAtOrDefault(0)?.Expression}] ElseIf=[{pick.Children.ElementAtOrDefault(1)?.Expression}]");

            Check("条件节点声明了运行时变量 WireCount（不声明条件求值时会取不到值）",
                pick.RuntimeVariableRefs.Any(v => v.Name == "WireCount"),
                string.Join(",", pick.RuntimeVariableRefs.Select(v => $"{v.Name}:{v.DataTypeName}")));

            Check("三个分支体里各有 1 个「变量赋值」节点（配方就写在里面）",
                pick.Children.All(c => c.Steps.Count == 1 && c.Steps[0].PluginName == "变量赋值"),
                string.Join(" | ", pick.Children.Select(c => $"{c.BranchType}:{c.Steps.Count}步")));

            Check("结果分流有 If / Else 两路，条件是 Result == true",
                route.Children.Count == 2
                && route.Children[0].BranchType == BranchType.If
                && (route.Children[0].Expression ?? "").Replace(" ", "") == "Result==true"
                && route.Children[1].BranchType == BranchType.Else,
                $"分支={string.Join(",", route.Children.Select(c => $"{c.BranchType}:[{c.Expression}]"))}");

            Check("两个脚本步骤的 Image 输入都连到采集步骤的 Image 输出",
                scan.LinkedSources.TryGetValue("Image", out var l1) && l1.TargetStepId == collect.StepID
                && compare.LinkedSources.TryGetValue("Image", out var l2) && l2.TargetStepId == collect.StepID,
                $"找线→{(scan.LinkedSources.TryGetValue("Image", out var a) ? a.DisplayAddress : "未连")}；"
                + $"比对→{(compare.LinkedSources.TryGetValue("Image", out var b) ? b.DisplayAddress : "未连")}");

            // ---- W5 三条路各跑一遍 ----
            var cable1 = Path.Combine(imgDir, "cable1.png");
            var cable2 = Path.Combine(imgDir, "cable2.png");

            // 路一：5 芯 → If → 配方 A
            var r5 = RunOnce(solution, flow, cable1);
            Check("【5 芯】编译并执行完成", r5.Compiled && r5.Threw.Length == 0, r5.FailureDiary);
            Check($"【5 芯】扫出 5 根（实测 {r5.Var("WireCount")}）",
                Convert.ToInt32(r5.Var("WireCount") ?? -1) == 5, $"WireCount={r5.Var("WireCount")}");
            Check($"【5 芯】走 If 分支取到配方 A = {Golden5}",
                (r5.Var("Expected") as string) == Golden5, $"Expected=[{r5.Var("Expected")}]");
            Check("【5 芯】线序与黄金参照一致",
                (r5.Var("Sequence") as string) == Golden5, $"实测=[{r5.Var("Sequence")}]");
            Check("【5 芯】判定合格、分流走 If 记 OK",
                r5.Var("Result") is bool b5 && b5 && (r5.Var("Verdict") as string) == "OK",
                $"Result={r5.Var("Result")} Verdict={r5.Var("Verdict")}");
            Check("【5 芯】日志留下结论全文",
                r5.Infos.Any(i => i.Contains("线序正确")), string.Join(" | ", r5.Infos.Take(4)));

            // 路二：10 芯 → ElseIf → 配方 B（这条才是"新加第二张图"要验的）
            var r10 = RunOnce(solution, flow, cable2);
            Check("【10 芯】编译并执行完成", r10.Compiled && r10.Threw.Length == 0, r10.FailureDiary);
            Check($"【10 芯】扫出 10 根（实测 {r10.Var("WireCount")}）",
                Convert.ToInt32(r10.Var("WireCount") ?? -1) == 10, $"WireCount={r10.Var("WireCount")}");
            Check($"【10 芯】线序与黄金参照一致：{Golden10}",
                (r10.Var("Sequence") as string) == Golden10, $"实测=[{r10.Var("Sequence")}]");
            Check("【10 芯】走 ElseIf 分支取到配方 B",
                (r10.Var("Expected") as string) == Golden10, $"Expected=[{r10.Var("Expected")}]");
            Check("【10 芯】判定合格、分流走 If 记 OK",
                r10.Var("Result") is bool b10 && b10 && (r10.Var("Verdict") as string) == "OK",
                $"Result={r10.Var("Result")} Verdict={r10.Var("Verdict")}");

            // 路三：未知芯数 → Else → 无配方。把 ElseIf 的条件改成永不成立，逼 10 芯落到 Else
            var rUnknown = RunOnce(solution, flow, cable2, elseIfExpr: "WireCount == 99");
            Check("【未知芯数】编译并执行完成", rUnknown.Compiled && rUnknown.Threw.Length == 0, rUnknown.FailureDiary);
            Check("【未知芯数】走 Else 分支（Expected 被清成空）",
                (rUnknown.Var("Expected") as string) == "", $"Expected=[{rUnknown.Var("Expected")}]");
            Check("【未知芯数】判定不合格",
                rUnknown.Var("Result") is bool bu && !bu, $"Result={rUnknown.Var("Result")}");
            Check("【未知芯数】结论明确写「未知产品」，不含糊成「线数不符」",
                rUnknown.Warns.Any(w => w.Contains("未知产品")),
                $"Warn=[{string.Join(" | ", rUnknown.Warns)}]");
            Check("【未知芯数】分流走 Else 记 NG",
                (rUnknown.Var("Verdict") as string) == "NG", $"Verdict={rUnknown.Var("Verdict")}");

            // ---- W6 投射：拿 10 芯那次的结果帧验（芯数最多，标签最全）----
            Check($"脚本往视图窗口发过预览帧（共 {r10.Frames.Count} 帧）",
                r10.Frames.Count > 0,
                r10.Frames.Count > 0
                    ? $"帧数={r10.Frames.Count}，目标窗口={string.Join(",", r10.Frames.Select(f => f.ViewIndex))}"
                    : "一帧都没发：ShowImage 没被调到，或预览通道断了");

            // 两个脚本节点各投射一帧：找线那帧画"在哪量的 + 逐根颜色"，比对那帧再叠上结论。
            // 后发者胜 —— 现场最终看到的是带结论的那帧。
            var annotated = r10.Frames.Where(f => f.Annotations != null && f.Annotations.Count > 0).ToList();
            var projected = annotated.LastOrDefault();

            Check("带标注的结果帧恰好 2 帧（找线一帧 + 比对一帧，不会重复投射）",
                annotated.Count == 2,
                $"各帧目标窗口：{string.Join(",", r10.Frames.Select(f => f.ViewIndex))}；带标注的帧数={annotated.Count}");

            Check("结果帧落在合法的视图窗口号上（1~9）",
                projected != null && projected.ViewIndex >= 1 && projected.ViewIndex <= 9,
                projected == null ? "没有带标注的帧" : $"投射窗口={projected.ViewIndex}");

            if (projected == null) return;

            var texts = projected.Annotations.Where(a => a.Type == MeasureType.Text).ToList();
            var lines = projected.Annotations.Where(a => a.Type == MeasureType.Line).ToList();

            Check("标注层 = 1 条采样线 + 每根 1 个颜色标签 + 1 行结论（10 芯 → 1 线 + 11 文本）",
                lines.Count == 1 && texts.Count == 11,
                $"线={lines.Count} 文本={texts.Count}：{string.Join(" / ", texts.Select(t => t.Text))}");

            Check("采样线竖着画在采样列上（两端的列号相同）",
                lines.Count == 1 && Math.Abs(lines[0].Points[1] - lines[0].Points[3]) < 0.001,
                lines.Count == 1 ? $"两端列号={lines[0].Points[1]} / {lines[0].Points[3]}" : "没有线标注");

            var labels = texts.Take(10).Select(t => t.Text).ToList();
            Check("逐根标签就是实测线序（贴的位置与线一一对应）",
                labels.SequenceEqual(Golden10.Split(',').Select((c, i) => $"{i + 1}.{c}")),
                string.Join(",", labels));

            var labelRows = texts.Take(10).Select(t => t.Points[0]).ToList();
            Check("逐根标签自上而下排列（行号递增，不会贴错位置）",
                labelRows.SequenceEqual(labelRows.OrderBy(x => x)), string.Join(",", labelRows));

            Check("结论那一行是绿字（10 芯那趟是合格的）",
                texts.Last().Color == "green" && texts.Last().Text.Contains("线序正确"),
                $"色={texts.Last().Color} 文={texts.Last().Text}");
        }

        /// <summary>一趟执行的产物</summary>
        private sealed class RunOutcome
        {
            public bool Compiled;
            public string CompileErrors = "";
            public string Threw = "";
            public List<string> Infos { get; } = new();
            public List<string> Warns { get; } = new();
            public List<string> Errors { get; } = new();
            public List<string> StepStates { get; } = new();
            public List<ImageDisplayEvent<HImage>> Frames { get; } = new();
            private Dictionary<string, object?> Vars { get; } = new();

            public object? Var(string key) => Vars.TryGetValue(key, out var v) ? v : null;
            public void SetVar(string key, object? v) => Vars[key] = v;

            /// <summary>
            /// 执行失败时的排障简报：把编译错误 / 异常、每步状态、三级日志一次说清。
            /// 成功时返回空串 —— 不给通过项添噪。
            /// </summary>
            public string FailureDiary =>
                (Compiled && Threw.Length == 0)
                    ? ""
                    : (Compiled ? Threw : CompileErrors)
                      + $" ｜ 步骤状态={string.Join(" ", StepStates)}"
                      + $" ｜ Warn={string.Join(" | ", Warns)}"
                      + $" ｜ Err={string.Join(" | ", Errors)}";
        }

        /// <summary>
        /// 把采集指向指定图，编译、执行一趟，回收运行时变量与预览帧。
        /// elseIfExpr 非空时临时改掉 ElseIf 的条件（只在内存里改，不动磁盘上的 .vms），
        /// 用来逼一条数据落到 Else 分支上 —— 否则"未知产品"这条路永远测不到。
        /// </summary>
        private static RunOutcome RunOnce(SolutionModel solution, FlowModel flow, string imagePath,
                                          string? elseIfExpr = null)
        {
            var outcome = new RunOutcome();

            var collect = flow.Steps[0];
            collect.SetInputValue("FilePath", imagePath);

            if (elseIfExpr != null && flow.Steps[2] is ConditionStep pickStep && pickStep.Children.Count >= 2)
            {
                pickStep.Children[1].Expression = elseIfExpr;
            }

            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(solution);

            var steps = flow.Steps.ToArray();
            var compiled = new FlowCompiler(workspace).Compile(steps, FlowName);
            outcome.Compiled = compiled.Success;
            outcome.CompileErrors = compiled.Success
                ? ""
                : string.Join(" | ", compiled.Errors.Select(e => e.Message));
            if (!compiled.Success || compiled.Data == null) return outcome;

            var session = new FlowSession { FlowName = FlowName, ExecutionEngine = compiled.Data };
            foreach (var s in steps) session.Blueprints.Add(s);

            var log = new StubLog();
            var ctx = new ExecutionContext(log, session, workspace, new CancellationTokenSource().Token);

            // 无 WPF 宿主时 PublishOnUIThread 降级为同步直调，所以这里能同步收到帧
            Action<ImageDisplayEvent<HImage>> onPreview = e => { lock (outcome.Frames) outcome.Frames.Add(e); };
            GlobalEventBus.Subscribe(onPreview);

            try { compiled.Data.Run(ctx); }
            catch (Exception ex) { outcome.Threw = ex.Message; }
            finally
            {
                GlobalEventBus.Unsubscribe(onPreview);
                outcome.Infos.AddRange(log.Infos);
                outcome.Warns.AddRange(log.Warns);
                outcome.Errors.AddRange(log.Errors);
                foreach (var s in steps) outcome.StepStates.Add($"{s.StepName}={s.State}");
            }

            foreach (var key in new[] { "WireCount", "Sequence", "Expected", "Result", "Detail", "Verdict" })
            {
                outcome.SetVar(key, ctx.LocalVariables.ContainsKey(key) ? ctx.LocalVariables[key] : null);
            }

            return outcome;
        }
    }
}
