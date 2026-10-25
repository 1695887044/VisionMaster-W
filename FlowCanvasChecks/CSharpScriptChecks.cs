using System;
using Core.Events;
using Core.Interfaces;
using HalconDotNet;
using Plugin.CSharpScript;
using VisionMaster.Models;
using VisionMaster.Services;
// 裸写 ExecutionContext 会与 System.Threading.ExecutionContext 撞名（隐式 using 已引入后者）
using ScriptExecContext = VisionMaster.Services.ExecutionContext;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// C#脚本插件的静默故障守门断言（2026-10-20）：
    ///
    /// ① 变量名 = 端口名 = 连线键。旧实现只在 Type 变化时重建端口，改名/删行都不动
    ///    StepData.LinkedSources —— 于是留下"没有同名端口"的孤儿键，FlowCompiler 直接
    ///    判整条流程编译失败（'没有名为 X 的输入端口'），而界面上够不到它。
    ///    更阴的两条：改名瞬间 Name 先变成空串时若当场删键，"全选→删除→重打"永久丢绑定；
    ///    逐字符改名时"中间名恰好与另一行重名"也会把原连线删掉。
    /// ② 智能提示旧实现插入**裸变量名**，而接口变量只是端口名/字典键、不是 C# 局部变量，
    ///    补全出来的代码必然 CS0103（"怎么写都不对"的元凶）。这里钉住"插入的是可编译模板"。
    /// ③ 手填值必须能往返（开窗→改名→确认→重开），且"取消"要把连线/变量表还原成打开时的样子。
    ///
    /// 全部是模型层直调：不需要窗口/Application/Dispatcher。
    /// </summary>
    internal static class CSharpScriptChecks
    {
        public static void Run()
        {
            Section("[CS] C#脚本：改名/删行的连线收尾 + 手填值往返 + 取消还原 + 补全模板 + 预览口径与端口释放");

            RenameMigratesLinkAndPort();
            ClearThenRetypeKeepsLink();
            MidRenameCollisionKeepsLink();
            DeleteRemovesLink();
            ConfirmCleansOrphanLinks();
            EmptyNameKeepsLink();
            ManualValueRoundTrips();
            PendingEditSurvivesRebuild();
            CancelRestoresModel();
            CompletionInsertsContextCalls();
            MissingPortWarnsOncePerRun();
            CancellationSurfacesAsOce();
            DuplicateNamesBlockConfirm();
            OutputDuplicatesBlockConfirm();
            BlankNameBlocksConfirm();
            PreviewPromisesOnlyHImage();
            RebuildReleasesOldPorts();
            RebuildDropsOutputValueWithoutDisposing();
            SharedVarAliasIsPinned();
            VarsViewIsReadOnly();
            CompletionUsesClrTypeNames();
        }

        private static (CSharpScriptPlugin Plugin, ActionStep Step) NewStep()
        {
            var step = new ActionStep("", "C#脚本",
                "Plugin.CSharpScript.CSharpScriptPlugin, Plugin.CSharpScript", "C#脚本_0");
            var plugin = new CSharpScriptPlugin();
            plugin.Initialize(step);
            return (plugin, step);
        }

        private static LinkReference GlobalLink(string name) =>
            new(LinkKind.GlobalVariable, Guid.Empty, name, $"全局变量 (Global).{name}");

        private static object PortValue(CSharpScriptPlugin plugin, string name) =>
            plugin.Inputs.TryGetValue(name, out var port) ? port.Value : null;

        /// <summary>
        /// 「显示窗口」列的判据逐类型走一遍：只有 HImage 该出现这一列。
        /// 返回违例清单（空 = 判据与承诺一致）。界面那一列与 PublishIconicOutputs 共用它，
        /// 所以这里一旦漂移，就是"配得出窗口但推不出去"的静默分支回来了。
        /// </summary>
        private static string ColumnRuleViolations()
        {
            var violations = new List<string>();
            foreach (ScriptVarType t in new[]
                     {
                         ScriptVarType.HImage, ScriptVarType.HRegion, ScriptVarType.HXld,
                         ScriptVarType.HObject, ScriptVarType.Object, ScriptVarType.Int,
                         ScriptVarType.String, ScriptVarType.HTuple,
                     })
            {
                var def = new ScriptVarDef { Type = t };
                if (def.ShowsDisplayWindow != (t == ScriptVarType.HImage)) violations.Add(t.ToString());
            }
            return string.Join("、", violations);
        }

        // ---- ① 改名：连线键 / 手填值 / 端口三样一起跟到新名字 ----
        private static void RenameMigratesLinkAndPort()
        {
            var (plugin, step) = NewStep();
            plugin.InputVars.Add(new ScriptVarDef { Name = "index", Type = ScriptVarType.Int });
            var link = GlobalLink("ImageIndex");
            step.SetLink("index", link);
            step.SetInputValue("index", 7);

            plugin.InputVars[0].Name = "counter";

            Check("CS1 改名：连线键迁到新名字（旧键不残留）",
                !step.IsLinked("index") && ReferenceEquals(step.GetLink("counter"), link),
                $"index={(step.IsLinked("index") ? "还在" : "已清")}；counter={step.GetLink("counter")?.DisplayAddress ?? "无"}");

            Check("CS2 改名：端口按新名字重建",
                plugin.Inputs.ContainsKey("counter") && !plugin.Inputs.ContainsKey("index"),
                string.Join(",", plugin.Inputs.Keys));

            Check("CS3 改名：手填值一并迁移",
                step.InputValues.TryGetValue("counter", out var manual)
                && Convert.ToInt32(manual) == 7
                && !step.InputValues.ContainsKey("index"),
                step.InputValues.TryGetValue("counter", out var m) ? $"counter={m}" : "counter 无值");
        }

        // ---- ② 清空名字（全选→删除）不许当场丢绑定，重新输入后要跟过去 ----
        private static void ClearThenRetypeKeepsLink()
        {
            var (plugin, step) = NewStep();
            plugin.InputVars.Add(new ScriptVarDef { Name = "index", Type = ScriptVarType.Int });
            step.SetLink("index", GlobalLink("ImageIndex"));

            plugin.InputVars[0].Name = "";

            Check("CS4 清空名字：连线不丢（锚点保留旧名）",
                step.IsLinked("index"),
                "清空输入框时若删键，重新输入就永久丢绑定");

            plugin.InputVars[0].Name = "idx";

            Check("CS5 重新输入：连线跟到新名字",
                step.IsLinked("idx") && !step.IsLinked("index"),
                $"idx={(step.IsLinked("idx") ? "已绑" : "无")}；index={(step.IsLinked("index") ? "残留" : "已清")}");
        }

        // ---- ②b 逐字符改名的中间名与另一行重名：不迁移、不丢原连线 ----
        private static void MidRenameCollisionKeepsLink()
        {
            var (plugin, step) = NewStep();
            plugin.InputVars.Add(new ScriptVarDef { Name = "a", Type = ScriptVarType.Int });
            plugin.InputVars.Add(new ScriptVarDef { Name = "ab", Type = ScriptVarType.Int });
            var link = GlobalLink("ImageIndex");
            step.SetLink("a", link);
            step.SetLink("ab", GlobalLink("Other"));

            plugin.InputVars[0].Name = "ab";   // 中间名与另一行重名

            Check("CS6 中间重名：不迁移、不丢原连线",
                ReferenceEquals(step.GetLink("a"), link),
                $"a={(step.IsLinked("a") ? "已绑" : "已丢")}；ab={(step.IsLinked("ab") ? "占用中" : "无")}");

            plugin.InputVars[0].Name = "abc";  // 继续打到最终名

            Check("CS7 继续打到最终名：原连线跟到 abc",
                !step.IsLinked("a") && ReferenceEquals(step.GetLink("abc"), link),
                $"abc={(step.IsLinked("abc") ? "已绑" : "无")}；a={(step.IsLinked("a") ? "残留" : "已清")}");
        }

        // ---- ③ 删行：连线键一起清，不留孤儿 ----
        private static void DeleteRemovesLink()
        {
            var (plugin, step) = NewStep();
            plugin.InputVars.Add(new ScriptVarDef { Name = "index", Type = ScriptVarType.Int });
            step.SetLink("index", GlobalLink("ImageIndex"));
            step.SetInputValue("index", 3);

            plugin.InputVars.RemoveAt(0);

            Check("CS8 删行：连线键一并清理（不留孤儿）",
                !step.IsLinked("index"),
                "孤儿键会让 FlowCompiler 报'没有名为 index 的输入端口'");
            Check("CS9 删行：端口移除",
                !plugin.Inputs.ContainsKey("index"),
                string.Join(",", plugin.Inputs.Keys));
        }

        // ---- ④ 确认配置：清掉历史遗留的孤儿键，但不动有效连线 ----
        private static void ConfirmCleansOrphanLinks()
        {
            var (plugin, step) = NewStep();
            plugin.InputVars.Add(new ScriptVarDef { Name = "index", Type = ScriptVarType.Int });
            step.SetLink("index", GlobalLink("ImageIndex"));
            step.SetLink("iN", GlobalLink("ImageIndex"));   // 改名前的旧键（真机事故形状）

            plugin.OnConfirm(step);

            Check("CS10 确认：清掉无对应变量的孤儿连线",
                !step.IsLinked("iN"),
                $"iN={(step.IsLinked("iN") ? "还在" : "已清")}");
            Check("CS11 确认：有效连线保留",
                step.IsLinked("index"),
                "清理不许误伤同名变量的有效绑定");
        }

        // ---- ④b 名字被清空时确认：不许把旧连线当孤儿删掉 ----
        private static void EmptyNameKeepsLink()
        {
            var (plugin, step) = NewStep();
            plugin.InputVars.Add(new ScriptVarDef { Name = "index", Type = ScriptVarType.Int });
            step.SetLink("index", GlobalLink("ImageIndex"));
            plugin.OnConfirm(step);

            var reopened = new CSharpScriptPlugin();
            reopened.Initialize(step);
            reopened.InputVars[0].Name = "";
            reopened.OnConfirm(step);

            Check("CS12 名字清空时确认：按锚点补回名字、连线保留（流程仍可编译）",
                step.IsLinked("index") && reopened.InputVars[0].Name == "index",
                $"name={reopened.InputVars[0].Name}；link={(step.IsLinked("index") ? "保留" : "丢了")}");
        }

        // ---- ⑤b 编辑期未确认的端口值：重建不许被 InputValues 旧值覆盖 ----
        private static void PendingEditSurvivesRebuild()
        {
            var (plugin, step) = NewStep();
            plugin.InputVars.Add(new ScriptVarDef { Name = "index", Type = ScriptVarType.Int });
            plugin.Inputs["index"].Value = 7;
            plugin.OnConfirm(step);                 // 上次确认：InputValues["index"]=7

            var reopened = new CSharpScriptPlugin();
            reopened.Initialize(step);

            reopened.Inputs["index"].Value = 8;     // 用户刚填、还没确认
            reopened.InputVars[0].Type = ScriptVarType.Double;

            Check("CS21 改类型：用户刚填的值不被 InputValues 旧值覆盖",
                Convert.ToDouble(PortValue(reopened, "index")) == 8,
                $"端口值={PortValue(reopened, "index") ?? "无端口"}（旧值 7 说明被覆盖）");

            reopened.Inputs["index"].Value = 9;
            reopened.InputVars.Add(new ScriptVarDef { Name = "extra", Type = ScriptVarType.Int });

            Check("CS22 新增行：既有端口的当前值不被打回",
                Convert.ToDouble(PortValue(reopened, "index")) == 9,
                $"端口值={PortValue(reopened, "index") ?? "无端口"}");

            reopened.InputVars[0].Name = "counter";

            Check("CS23 改名：未确认的当前值跟着端口走",
                Convert.ToDouble(PortValue(reopened, "counter")) == 9,
                $"端口值={PortValue(reopened, "counter") ?? "无端口"}");
        }

        // ---- ⑤ 手填值往返：开窗 → 改名 → 确认 → 重开 ----
        private static void ManualValueRoundTrips()
        {
            var (plugin, step) = NewStep();
            plugin.InputVars.Add(new ScriptVarDef { Name = "index", Type = ScriptVarType.Int });
            plugin.Inputs["index"].Value = 7;      // 用户在行内手填
            plugin.OnConfirm(step);                 // 落盘：InputVars 快照 + 手填值

            var reopened = new CSharpScriptPlugin();
            reopened.Initialize(step);

            Check("CS13 开窗：未连线端口的手填值被灌进端口（不再回默认 0）",
                Convert.ToInt32(PortValue(reopened, "index")) == 7,
                $"端口值={PortValue(reopened, "index") ?? "无端口"}");

            reopened.InputVars[0].Name = "counter";
            reopened.OnConfirm(step);

            var again = new CSharpScriptPlugin();
            again.Initialize(step);

            Check("CS14 改名→确认→重开：手填值仍是 7（0 不许覆盖 7）",
                Convert.ToInt32(PortValue(again, "counter")) == 7,
                $"端口值={PortValue(again, "counter") ?? "无端口"}");
        }

        // ---- ⑥ 取消：连线键与变量表还原成打开时的样子 ----
        private static void CancelRestoresModel()
        {
            var (plugin, step) = NewStep();
            plugin.InputVars.Add(new ScriptVarDef { Name = "index", Type = ScriptVarType.Int });
            step.SetLink("index", GlobalLink("ImageIndex"));
            plugin.OnConfirm(step);                 // 让 InputValues 里有 InputVars 快照

            var reopened = new CSharpScriptPlugin();
            reopened.Initialize(step);              // 拍快照

            reopened.InputVars[0].Name = "counter";
            reopened.InputVars.Add(new ScriptVarDef { Name = "extra", Type = ScriptVarType.Int });
            reopened.OnCancel();

            Check("CS15 取消：连线键回到打开时的名字",
                step.IsLinked("index") && !step.IsLinked("counter"),
                $"index={(step.IsLinked("index") ? "已恢复" : "丢了")}；counter={(step.IsLinked("counter") ? "残留" : "已清")}");

            Check("CS16 取消：变量表回到打开时（改名与新增行都被丢弃）",
                reopened.InputVars.Count == 1 && reopened.InputVars[0].Name == "index",
                $"count={reopened.InputVars.Count} first={reopened.InputVars[0].Name}");
        }

        // ---- ⑦ 补全：插入的是可编译的 Context 调用模板，光标位也对 ----
        private static void CompletionInsertsContextCalls()
        {
            var input = new CSharpIntelliSense.VarInfo
            { Name = "index", Kind = "输入", Type = "整数", ClrType = "int", IsOutput = false };
            var image = new CSharpIntelliSense.VarInfo
            { Name = "Image", Kind = "输入", Type = "Halcon 图像", ClrType = "HImage", IsOutput = false };
            var output = new CSharpIntelliSense.VarInfo
            { Name = "out", Kind = "输出", Type = "整数", ClrType = "int", IsOutput = true };

            var (inputText, inputCaret) = CSharpIntelliSense.BuildVarInsertWithCaret(input);
            Check("CS17 补全：输入变量插入 GetInput 模板、光标在末尾",
                inputText == "Context.GetInput<int>(\"index\")" && inputCaret == 0,
                $"{inputText}｜caretFromEnd={inputCaret}");

            Check("CS18 补全：图像类型映射到 HImage 泛型",
                CSharpIntelliSense.BuildVarInsert(image) == "Context.GetInput<HImage>(\"Image\")",
                CSharpIntelliSense.BuildVarInsert(image));

            var (outputText, outputCaret) = CSharpIntelliSense.BuildVarInsertWithCaret(output);
            Check("CS19 补全：输出变量插入 SetOutput 模板、光标落在逗号后",
                outputText == "Context.SetOutput(\"out\", )" && outputCaret == 1,
                $"{outputText}｜caretFromEnd={outputCaret}");

            Check("CS20 类型映射：HXld→HXLDCont、Object→object",
                ScriptVarType.HXld.ToClrTypeName() == "HXLDCont"
                && ScriptVarType.Object.ToClrTypeName() == "object",
                $"{ScriptVarType.HXld.ToClrTypeName()} / {ScriptVarType.Object.ToClrTypeName()}");
        }

        // ---- ⑧ 缺端口：不抛、不失败；每轮每方向每名只 Warn 一条 ----
        private static void MissingPortWarnsOncePerRun()
        {
            var (plugin, _) = NewStep();
            plugin.ScriptText =
                "Context.GetInput(\"NoSuchIn\");\n" +
                "Context.GetInput(\"NoSuchIn\");\n" +
                "Context.SetOutput(\"NoSuchOut\", 1);\n" +
                "Context.SetOutput(\"NoSuchOut\", 2);\n";
            var log = new StubLog();
            var ctx = new ScriptExecContext(
                log, new FlowSession { FlowName = "缺端口断言" }, new WorkspaceContext(), CancellationToken.None);

            plugin.RunAlgorithm(ctx);

            int inWarns = log.Warns.Count(w => w.Contains("NoSuchIn"));
            int outWarns = log.Warns.Count(w => w.Contains("NoSuchOut"));

            Check("CS24 缺端口：拼错端口名不抛、节点仍成功（旧行为是彻底静默）",
                plugin.Success.Value is true,
                $"Success={plugin.Success.Value}；Error={plugin.ErrorMessage.Value}");
            Check("CS25 缺端口：每轮每方向每个名字恰好一条 Warn（长循环不刷屏）",
                inWarns == 1 && outWarns == 1,
                $"输入 Warn={inWarns}；输出 Warn={outWarns}");
        }

        // ---- ⑨ 取消：ScriptContext 抛的 OCE 必须原样透出（节点层据此记 Skipped），不许落成失败 ----
        private static void CancellationSurfacesAsOce()
        {
            var (plugin, _) = NewStep();
            plugin.OutputVars.Add(new ScriptVarDef { Name = "Ran", Type = ScriptVarType.Bool });
            plugin.ScriptText =
                "Context.SetOutput(\"Ran\", true);\n" +
                "System.Threading.Thread.Sleep(150);\n" +
                "Context.ThrowIfCancellationRequested();\n";

            // 预热一次编译缓存：本条要在脚本执行途中取消，冷编译的耗时会让
            // "取消落在编译期"与"取消落在脚本里"分不开（那样就测不到门面方法本身）
            var warmLog = new StubLog();
            plugin.RunAlgorithm(new ScriptExecContext(
                warmLog, new FlowSession { FlowName = "取消断言-预热" }, new WorkspaceContext(), CancellationToken.None));
            plugin.Outputs["Ran"].Value = false;

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(50);
            var log = new StubLog();
            var ctx = new ScriptExecContext(
                log, new FlowSession { FlowName = "取消断言" }, new WorkspaceContext(), cts.Token);

            bool oce = false;
            string other = null;
            try { plugin.RunAlgorithm(ctx); }
            catch (OperationCanceledException) { oce = true; }
            catch (Exception ex) { other = ex.GetType().Name + ": " + ex.Message; }

            bool reachedCheckpoint = plugin.Outputs["Ran"].Value is true;
            Check("CS26 取消：Context.ThrowIfCancellationRequested 的 OCE 直接透出 RunAlgorithm（不被当成运行异常）",
                oce && reachedCheckpoint && string.IsNullOrEmpty(Convert.ToString(plugin.ErrorMessage.Value)),
                oce
                    ? $"OCE 已上抛；脚本已执行到检查点={reachedCheckpoint}；Error={plugin.ErrorMessage.Value}"
                    : $"OCE 被吞：{other ?? "原样返回"}；Success={plugin.Success.Value}；Error={plugin.ErrorMessage.Value}");
        }

        // ---- ⑩ 输入重名：确认被拒绝，撞名期间的原连线不被当孤儿删掉 ----
        private static void DuplicateNamesBlockConfirm()
        {
            var (plugin, step) = NewStep();
            plugin.InputVars.Add(new ScriptVarDef { Name = "a", Type = ScriptVarType.Int });
            plugin.InputVars.Add(new ScriptVarDef { Name = "b", Type = ScriptVarType.Int });
            var linkA = GlobalLink("ImageIndex");
            var linkB = GlobalLink("OtherIndex");
            step.SetLink("a", linkA);
            step.SetLink("b", linkB);

            plugin.InputVars[0].Name = "b";   // 撞名：目标名已连线 → 锚点留在 a、不迁移

            string error = null;
            try { plugin.OnConfirm(step); }
            catch (Exception ex) { error = ex.Message; }

            Check("CS27 输入重名：确认被拒绝，并指出冲突的两行行号",
                error != null && error.Contains("第 2 行") && error.Contains("第 1 行") && error.Contains("重名"),
                error ?? "OnConfirm 未拒绝（重名被静默接受）");
            Check("CS28 重名被拒时：两行原有的连线都还在（不再静默删活连线）",
                ReferenceEquals(step.GetLink("a"), linkA) && ReferenceEquals(step.GetLink("b"), linkB),
                $"a={(step.IsLinked("a") ? "在" : "丢了")}；b={(step.IsLinked("b") ? "在" : "丢了")}");

            plugin.InputVars[0].Name = "c";   // 改成不冲突的名字
            plugin.OnConfirm(step);
            Check("CS29 改成不冲突的名字后确认通过：连线从锚点 a 迁到 c",
                step.IsLinked("c") && !step.IsLinked("a"),
                $"c={(step.IsLinked("c") ? "已绑" : "无")}；a={(step.IsLinked("a") ? "残留" : "已清")}");
        }

        // ---- ⑪ 输出重名：端口与快照必须 1:1，确认同样拒绝 ----
        private static void OutputDuplicatesBlockConfirm()
        {
            var (plugin, step) = NewStep();
            plugin.OutputVars.Add(new ScriptVarDef { Name = "res", Type = ScriptVarType.Int });
            plugin.OutputVars.Add(new ScriptVarDef { Name = "res", Type = ScriptVarType.Double });

            int portCount = plugin.Outputs.ContainsKey("res") ? 1 : 0;
            int snapshotCount = step.OutputPortDefinitions.Count(d => d.Name == "res");
            Check("CS30 输出重名：编辑期端口与快照都只留一份（不出现同名两项）",
                portCount == 1 && snapshotCount == 1,
                $"端口={portCount}；快照={snapshotCount}");

            string error = null;
            try { plugin.OnConfirm(step); }
            catch (Exception ex) { error = ex.Message; }

            Check("CS31 输出重名：确认被拒绝（不再静默折叠后照常保存）",
                error != null && error.Contains("输出变量") && error.Contains("重名"),
                error ?? "OnConfirm 未拒绝");
        }

        // ---- ⑫ 空名字：没有锚点可还原时拒绝确认；「校验脚本」先于编译报出 ----
        private static void BlankNameBlocksConfirm()
        {
            var (plugin, step) = NewStep();
            plugin.InputVars.Add(new ScriptVarDef { Name = "", Type = ScriptVarType.Int });

            string error = null;
            try { plugin.OnConfirm(step); }
            catch (Exception ex) { error = ex.Message; }

            Check("CS32 空名字：确认被拒绝并指出行号（不留下没有端口的空行）",
                error != null && error.Contains("第 1 行") && error.Contains("名字为空"),
                error ?? "OnConfirm 未拒绝");

            string validated = plugin.ValidateScript();
            Check("CS33 校验脚本：变量表空名/重名先于编译报出（与确认同一文案来源）",
                validated != null && validated.Contains("名字为空"),
                validated ?? "校验通过");
        }

        // ---- ⑬ 预览：界面"显示窗口"那一列与推送判据必须同一个口径，只有 HImage 推得出去 ----
        //
        // 为什么钉这条：宿主预览通道（PublishPreview → ImageDisplayEvent<HImage>）只收图像。
        // 旧判据用 IsIconic，于是区域/轮廓行选得出窗口、执行完什么都不发生（静默）。
        // 现在承诺收窄到 HImage，ScriptVarDef.ShowsDisplayWindow 是界面与推送共用的唯一来源。
        private static void PreviewPromisesOnlyHImage()
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", 8, 8);
            var image = new HImage(proto);
            proto.Dispose();
            HOperatorSet.GenRectangle1(out HObject rect, 0.0, 0.0, 7.0, 7.0);
            var region = new HRegion(rect);
            rect.Dispose();

            try
            {
                var (plugin, _) = NewStep();
                plugin.InputVars.Add(new ScriptVarDef { Name = "Src", Type = ScriptVarType.HImage });
                plugin.InputVars.Add(new ScriptVarDef { Name = "SrcReg", Type = ScriptVarType.HRegion });
                plugin.OutputVars.Add(new ScriptVarDef { Name = "Img", Type = ScriptVarType.HImage, DisplayWindow = 2 });
                plugin.OutputVars.Add(new ScriptVarDef { Name = "Reg", Type = ScriptVarType.HRegion, DisplayWindow = 1 });
                plugin.Inputs["Src"].Value = image;
                plugin.Inputs["SrcReg"].Value = region;
                plugin.ScriptText =
                    "Context.SetOutput(\"Img\", Context.GetInput(\"Src\"));\n" +
                    "Context.SetOutput(\"Reg\", Context.GetInput(\"SrcReg\"));\n";

                var frames = new List<ImageDisplayEvent<HImage>>();
                Action<ImageDisplayEvent<HImage>> onFrame = frames.Add;
                GlobalEventBus.Subscribe(onFrame);
                var log = new StubLog();
                try
                {
                    plugin.RunAlgorithm(new ScriptExecContext(
                        log, new FlowSession { FlowName = "预览口径断言" }, new WorkspaceContext(), CancellationToken.None));
                }
                finally { GlobalEventBus.Unsubscribe(onFrame); }

                int toWindow2 = frames.Count(f => f.ViewIndex == 2);
                int toWindow1 = frames.Count(f => f.ViewIndex == 1);
                Check("CS34 预览口径：HImage 行推恰好一帧进窗口2；区域行选了窗口1 却一帧都不推（承诺=实现）",
                    toWindow2 == 1 && toWindow1 == 0 && plugin.Success.Value is true,
                    $"窗口2={toWindow2}；窗口1={toWindow1}（该行 DisplayWindow=1）；总帧数={frames.Count}；" +
                    $"Success={plugin.Success.Value}；Error={plugin.ErrorMessage.Value}");

                string violations = ColumnRuleViolations();
                Check("CS35 唯一口径：ShowsDisplayWindow 只在 HImage 为真（区域/轮廓/图形对象行根本选不出窗口）",
                    plugin.OutputVars[0].ShowsDisplayWindow && !plugin.OutputVars[1].ShowsDisplayWindow
                        && plugin.OutputVars[1].IsIconic && violations.Length == 0,
                    $"Img(={plugin.OutputVars[0].Type})={plugin.OutputVars[0].ShowsDisplayWindow}；" +
                    $"Reg(={plugin.OutputVars[1].Type})={plugin.OutputVars[1].ShowsDisplayWindow}；" +
                    $"Reg.IsIconic={plugin.OutputVars[1].IsIconic}；逐类型违例={(violations.Length == 0 ? "无" : violations)}");
            }
            finally
            {
                region.Dispose();
                image.Dispose();
            }
        }

        // ---- ⑭ 端口重建必须摘掉旧端口承载的东西（P2：逐次改名累积的订阅与图像） ----
        private static void RebuildReleasesOldPorts()
        {
            var (plugin, _) = NewStep();
            plugin.InputVars.Add(new ScriptVarDef { Name = "a", Type = ScriptVarType.Int });
            plugin.OutputVars.Add(new ScriptVarDef { Name = "out", Type = ScriptVarType.Int });

            var probe = new CountingOutputPort();
            IInputPort oldIn = plugin.Inputs["a"];
            oldIn.LinkedSource = probe;

            Check("CS36a 前置：链接确实建立了（上游挂着 1 个订阅者，否则下面的判据是空转）",
                probe.SubscribeCalls == 1 && probe.LiveSubscribers == 1 && oldIn.LinkedSource != null,
                $"订阅次数={probe.SubscribeCalls}；活订阅={probe.LiveSubscribers}；LinkedSource={(oldIn.LinkedSource == null ? "空" : "已接")}");

            plugin.InputVars[0].Name = "b";       // 改名触发重建（编辑期逐字符都会走到这里）
            plugin.OutputVars[0].Name = "out2";

            bool newInExists = plugin.Inputs.TryGetValue("b", out var newIn);
            Check("CS36 改名重建：旧输入端口已 Dispose——上游 ValueChanged 订阅断开（LinkedSource 归空），新端口是另一个对象",
                newInExists && !ReferenceEquals(newIn, oldIn) && oldIn.LinkedSource == null,
                $"新端口={(newInExists ? "有" : "无")}；同对象={ReferenceEquals(newIn, oldIn)}；" +
                $"旧端口 LinkedSource={(oldIn.LinkedSource == null ? "已断" : "仍指着上游")}");

            // 这一条才是"泄漏"本体：上游事件委托链里还挂着旧端口，就等于旧端口（和它缓存的那张图）
            // 被长寿命上游永久钉住。只查 port.Value 挡不住——链接时读的是缓存值，不是 Value。
            Check("CS36b 反证订阅真的注销了：上游的活订阅数归零（+= 与 -= 恰好配平）",
                probe.UnsubscribeCalls == 1 && probe.LiveSubscribers == 0 && probe.SubscribeCalls == 1,
                $"订阅 +={probe.SubscribeCalls}；退订 -={probe.UnsubscribeCalls}；仍挂在上游的订阅者={probe.LiveSubscribers}");
        }

        /// <summary>
        /// 只用来数"订阅往返"的假上游端口：泄漏判据必须能反证，
        /// 而 <c>OutputPort</c> 的委托链从外部读不到，所以自带一个计数器。
        /// </summary>
        private sealed class CountingOutputPort : IOutputPort
        {
            private readonly List<EventHandler> _handlers = new();

            public string Name => "UpCount";
            public Type DataType => typeof(int);
            public object Value { get; set; } = 3;
            public string Description { get; set; } = "断言用：只数订阅往返";

            public int SubscribeCalls { get; private set; }
            public int UnsubscribeCalls { get; private set; }
            public int LiveSubscribers => _handlers.Count;

            public event EventHandler ValueChanged
            {
                add { SubscribeCalls++; _handlers.Add(value); }
                remove { UnsubscribeCalls++; _handlers.Remove(value); }
            }
        }

        // ---- ⑮ 旧输出端口只断引用，绝不越权 Dispose 上游的图 ----
        private static void RebuildDropsOutputValueWithoutDisposing()
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", 4, 4);
            var image = new HImage(proto);
            proto.Dispose();

            try
            {
                var (plugin, _) = NewStep();
                plugin.OutputVars.Add(new ScriptVarDef { Name = "out", Type = ScriptVarType.HImage });
                IOutputPort oldOut = plugin.Outputs["out"];
                oldOut.Value = image;

                plugin.OutputVars[0].Name = "out2";   // 改名 → 旧输出端口被摘掉
                IOutputPort newOut = plugin.Outputs.TryGetValue("out2", out var p) ? p : null;

                Check("CS37 摘掉的旧输出端口：值引用已断（null），但图像本身没被本插件释放（透传图的所有权归生产者）",
                    oldOut.Value == null && newOut != null && !ReferenceEquals(newOut, oldOut) && image.IsInitialized(),
                    $"旧端口值={(oldOut.Value == null ? "已断" : "仍持有")}；新端口={(newOut == null ? "无" : "有")}；" +
                    $"图像 IsInitialized={image.IsInitialized()}");
            }
            finally
            {
                image.Dispose();
            }
        }

        // ---- ⑯ 共享变量别名留空：在"脚本键还权威"的时刻固化，宿主改名不漂 ----
        private static void SharedVarAliasIsPinned()
        {
            var id = Guid.NewGuid();
            var option = new VariableOption
            {
                VariableId = id,
                Name = "结果",
                Scope = "Global",
                TypeName = "String",
                CanWrite = true,
            };

            var (plugin, _) = NewStep();
            plugin.SharedVars.Add(new ScriptSharedVarDef { VariableId = id });   // 别名留空
            plugin.SetConfigContext(new PluginConfigContext
            {
                FlowName = "别名断言",
                Variables = new List<VariableOption> { option },
            });

            string pinnedAtBind = plugin.SharedVars[0].EffectiveAlias;

            option.Name = "检测结果";                       // 变量管理里改名
            plugin.RefreshSharedVarDefs();                  // 宿主重新推快照

            Check("CS38 别名留空：首次刷新就固化成当时的名，之后宿主改名脚本键不动、展示名跟着走",
                pinnedAtBind == "结果" && plugin.SharedVars[0].EffectiveAlias == "结果"
                    && plugin.SharedVars[0].Name == "检测结果",
                $"绑定时键={pinnedAtBind}；改名后 EffectiveAlias={plugin.SharedVars[0].EffectiveAlias}；" +
                $"Name={plugin.SharedVars[0].Name}");

            // 手填一行、没走候选选择：确认那一刻也必须固化（否则下次开配置就被改名漂走）
            var (plugin2, step2) = NewStep();
            plugin2.SharedVars.Add(new ScriptSharedVarDef { VariableId = Guid.NewGuid(), Name = "计数" });
            plugin2.SharedVars.Add(new ScriptSharedVarDef { VariableId = Guid.NewGuid(), Name = "" });   // 还没选变量
            plugin2.OnConfirm(step2);

            Check("CS38b 确认时兜底固化：手填行的空别名落定；名字为空的行不固化（不把空串锁成键）",
                plugin2.SharedVars[0].Alias == "计数" && string.IsNullOrEmpty(plugin2.SharedVars[1].Alias),
                $"第1行 Alias={plugin2.SharedVars[0].Alias}；第2行 Alias=「{plugin2.SharedVars[1].Alias}」");
        }

        // ---- ⑰ Context.Vars 是只读视图：读照常，写挡住并指回 SetVar ----
        private static void VarsViewIsReadOnly()
        {
            var (plugin, _) = NewStep();
            plugin.OutputVars.Add(new ScriptVarDef { Name = "Blocked", Type = ScriptVarType.Bool });
            plugin.OutputVars.Add(new ScriptVarDef { Name = "Usable", Type = ScriptVarType.Bool });
            plugin.OutputVars.Add(new ScriptVarDef { Name = "Absent", Type = ScriptVarType.Bool });
            plugin.ScriptText =
                "Context.SetVar(\"a\", 1);\n" +
                "bool blocked = false;\n" +
                "try { Context.Vars[\"b\"] = 2; }\n" +
                "catch (System.NotSupportedException) { blocked = true; }\n" +
                "Context.SetOutput(\"Blocked\", blocked);\n" +
                "Context.SetOutput(\"Usable\", Context.Vars.ContainsKey(\"a\") && Context.GetVar<int>(\"a\") == 1);\n" +
                "Context.SetOutput(\"Absent\", !Context.Vars.ContainsKey(\"b\"));\n";

            var log = new StubLog();
            plugin.RunAlgorithm(new ScriptExecContext(
                log, new FlowSession { FlowName = "Vars 只读断言" }, new WorkspaceContext(), CancellationToken.None));

            Check("CS39 Context.Vars 写不进（NotSupportedException），读与枚举照常；写走 SetVar 不受影响",
                plugin.Outputs["Blocked"].Value is true && plugin.Outputs["Usable"].Value is true
                    && plugin.Success.Value is true,
                $"写被挡={plugin.Outputs["Blocked"].Value}；仍可读={plugin.Outputs["Usable"].Value}；" +
                $"Success={plugin.Success.Value}；Error={plugin.ErrorMessage.Value}");

            Check("CS39b 半写没发生：写失败后流程变量池里确实没多出 b（不是「异常拦住了但值已进」）",
                plugin.Outputs["Absent"].Value is true && log.Errors.Count == 0,
                $"池中无 b={plugin.Outputs["Absent"].Value}；Error 条数={log.Errors.Count}；Error={plugin.ErrorMessage.Value}");
        }

        // ---- ⑱ 补全给的类型名必须是真 CLR 类型 ----
        private static void CompletionUsesClrTypeNames()
        {
            bool hasCont = CSharpApiDoc.CommonKeywords.Contains("HXLDCont");
            bool hasEnumName = CSharpApiDoc.CommonKeywords.Contains("HXld");

            Check("CS40 补全关键字给 HXLDCont（真 CLR 类型），不给枚举名 HXld（写了必 CS0246）",
                hasCont && !hasEnumName,
                $"含 HXLDCont={hasCont}；含 HXld={hasEnumName}；全表={string.Join(",", CSharpApiDoc.CommonKeywords)}");
        }
    }
}
