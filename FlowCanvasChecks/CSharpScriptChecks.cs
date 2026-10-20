using System;
using Core.Interfaces;
using Plugin.CSharpScript;
using VisionMaster.Models;
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
            Section("[CS] C#脚本：改名/删行的连线收尾 + 手填值往返 + 取消还原 + 补全模板");

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
    }
}
