using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Core.Interfaces;
using Plugin.CSharpScript;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// C#脚本「共享变量」声明（2026-10-20）的守门断言：
    ///
    /// 设计意图：输入/输出是**数据流边**（端口、连线、顺序/跨分支约束），
    /// 全局/系统变量是**命名状态**（按 VariableId 寻址、改名不断链、不占端口）。
    /// 覆盖：别名解析 / 未声明 / 读失败 / 类型转换 / 候选快照 / 持久化 /
    /// 写闸门（只读、系统变量）/ 并行写冲突收集（含嵌套）/ 输入只接上游的候选过滤。
    ///
    /// 全部是模型层直调：不需要窗口/Application/Dispatcher。
    /// </summary>
    internal static class CSharpSharedVarChecks
    {
        public static void Run()
        {
            Section("[SV] C#脚本·共享变量：别名解析 / 未声明 / 类型转换 / 候选快照 / 持久化 / 写闸门 / 并行写集合 / 输入过滤");

            AliasResolutionAndRead();
            ReadFailureAndUndeclared();
            ValidatorChecks();
            SystemVariableIsReadOnly();
            ConfigContextCandidates();
            PersistenceRoundTrip();
            CancelRestoresSharedVars();
            WriteGuards();
            ParallelWriteCollection();
            InputBindingFilter();
        }

        private static (CSharpScriptPlugin Plugin, ActionStep Step) NewStep()
        {
            var step = new ActionStep("", "C#脚本",
                "Plugin.CSharpScript.CSharpScriptPlugin, Plugin.CSharpScript", "C#脚本_0");
            var plugin = new CSharpScriptPlugin();
            plugin.Initialize(step);
            return (plugin, step);
        }

        /// <summary>最小共享变量存储桩：按 Id 给值，可注入读失败。</summary>
        private sealed class FakeStore : ISharedVariableAccess
        {
            public readonly Dictionary<Guid, object> Values = new();
            public readonly List<(Guid Id, object Value)> Writes = new();
            public string? ReadError;
            public string? WriteError;

            public bool TryRead(Guid variableId, string fallbackName, out object? value, out string? error)
            {
                error = ReadError;
                if (ReadError != null) { value = null; return false; }
                return Values.TryGetValue(variableId, out value);
            }

            public bool TryWrite(Guid variableId, string fallbackName, object? value, out string? error)
            {
                error = WriteError;
                if (WriteError != null) return false;
                Writes.Add((variableId, value));
                return true;
            }
        }

        // ---- ① 别名解析 + 读 + 类型转换 ----
        private static void AliasResolutionAndRead()
        {
            var id = Guid.NewGuid();
            var store = new FakeStore();
            store.Values[id] = 42;

            var defs = new[]
            {
                new ScriptSharedVarDef { VariableId = id, Name = "ImageIndex", TypeName = "Int32" },
                new ScriptSharedVarDef { VariableId = id, Name = "ImageIndex", Alias = "idx", TypeName = "Int32" },
            };
            var accessor = new ScriptSharedAccessor(defs, store);

            Check("SV1 按别名读共享变量（别名留空 = 变量名）",
                Convert.ToInt32(accessor.Get("ImageIndex")) == 42, $"值={accessor.Get("ImageIndex")}");

            Check("SV2 自定义别名可读",
                Convert.ToInt32(accessor.Get("idx")) == 42, $"值={accessor.Get("idx")}");

            Check("SV3 Get<T> 按声明类型转换（int → double）",
                Math.Abs(accessor.Get<double>("idx") - 42.0) < 1e-9, $"值={accessor.Get<double>("idx")}");
        }

        // ---- ② 未声明 / 读失败 ----
        private static void ReadFailureAndUndeclared()
        {
            var id = Guid.NewGuid();
            var store = new FakeStore();
            store.Values[id] = 1;
            var defs = new[] { new ScriptSharedVarDef { VariableId = id, Name = "ImageIndex" } };
            var accessor = new ScriptSharedAccessor(defs, store);

            string undeclared = Catch(() => accessor.Get("nope"));
            Check("SV4 未声明别名：抛异常并提示去哪声明",
                undeclared != null && undeclared.Contains("未声明") && undeclared.Contains("共享变量"),
                undeclared ?? "没有抛异常");

            var failing = new FakeStore { ReadError = "变量已被删除" };
            var failingAccessor = new ScriptSharedAccessor(defs, failing);
            string readError = Catch(() => failingAccessor.Get("ImageIndex"));
            Check("SV5 读失败：原样带出存储层的失败原因",
                readError == "变量已被删除", readError ?? "没有抛异常");

            var noStore = new ScriptSharedAccessor(defs, null);
            string noStoreError = Catch(() => noStore.Get("ImageIndex"));
            Check("SV6 无执行环境：明确报「不支持读取」而不是 NullReference",
                noStoreError != null && noStoreError.Contains("不支持"),
                noStoreError ?? "没有抛异常");
        }

        // ---- ③ 校验脚本扫描 ----
        private static void ValidatorChecks()
        {
            var defs = new[]
            {
                new ScriptSharedVarDef { Name = "ImageIndex", Access = ScriptSharedAccess.Read },
                new ScriptSharedVarDef { Name = "AxisX_Position", IsSystem = true, Access = ScriptSharedAccess.Read },
                new ScriptSharedVarDef { Name = "Counter", Access = ScriptSharedAccess.ReadWrite },
            };

            Check("SV7 校验：已声明 GetGlobal 通过",
                ScriptSharedUsageValidator.Validate("var x = Context.GetGlobal<int>(\"ImageIndex\");", defs) == null,
                ScriptSharedUsageValidator.Validate("var x = Context.GetGlobal<int>(\"ImageIndex\");", defs) ?? "通过");

            string undeclared = ScriptSharedUsageValidator.Validate(
                "// 注释\nvar x = Context.GetGlobal<int>(\"Nope\");", defs);
            Check("SV8 校验：未声明报行号",
                undeclared != null && undeclared.StartsWith("第 2 行", StringComparison.Ordinal)
                && undeclared.Contains("未声明"),
                undeclared ?? "通过");

            string readOnlyWrite = ScriptSharedUsageValidator.Validate(
                "Context.SetGlobal(\"ImageIndex\", 1);", defs);
            Check("SV9 校验：只读声明被写 → 报错并提示改方向",
                readOnlyWrite != null && readOnlyWrite.Contains("只读"),
                readOnlyWrite ?? "通过");

            string systemWrite = ScriptSharedUsageValidator.Validate(
                "Context.SetGlobal(\"AxisX_Position\", 1);", defs);
            Check("SV10 校验：系统变量被写 → 报「系统变量（只读）」",
                systemWrite != null && systemWrite.Contains("系统变量"),
                systemWrite ?? "通过");

            Check("SV11 校验：读写声明可写",
                ScriptSharedUsageValidator.Validate("Context.SetGlobal(\"Counter\", 1);", defs) == null,
                ScriptSharedUsageValidator.Validate("Context.SetGlobal(\"Counter\", 1);", defs) ?? "通过");

            var duplicate = new[]
            {
                new ScriptSharedVarDef { Name = "A", Alias = "same" },
                new ScriptSharedVarDef { Name = "B", Alias = "same" },
            };
            string dup = ScriptSharedUsageValidator.Validate("", duplicate);
            Check("SV12 校验：重复别名报错",
                dup != null && dup.Contains("重复"), dup ?? "通过");
        }

        // ---- ④ 系统变量恒只读 ----
        private static void SystemVariableIsReadOnly()
        {
            var def = new ScriptSharedVarDef
            {
                Name = "AxisX_Position",
                IsSystem = true,
                Access = ScriptSharedAccess.ReadWrite,
            };

            Check("SV13 系统变量：方向被强制回只读",
                def.Access == ScriptSharedAccess.Read && !def.CanWrite && !def.AccessSelectable,
                $"Access={def.Access} CanWrite={def.CanWrite}");
        }

        // ---- ⑤ 宿主快照：候选只含全局+系统；按 Id 刷新（改名不断链） ----
        private static void ConfigContextCandidates()
        {
            var (plugin, _) = NewStep();
            var globalId = Guid.NewGuid();
            var systemId = Guid.NewGuid();

            var context = new PluginConfigContext
            {
                Variables = new List<VariableOption>
                {
                    new() { VariableId = globalId, Name = "ImageIndex", Scope = "Global", TypeName = "Int32", CanWrite = true },
                    new() { VariableId = systemId, Name = "AxisX_Position", Scope = "System", TypeName = "Double", IsSystem = true, CanWrite = false },
                    new() { Name = "temp", Scope = "Runtime", TypeName = "Double" },
                },
            };

            plugin.SetConfigContext(context);

            Check("SV14 候选只含全局 + 系统变量（运行时变量不进共享表）",
                plugin.SharedVarCandidates.Count == 2
                && plugin.SharedVarCandidates[0].Name == "ImageIndex"
                && plugin.SharedVarCandidates[1].Name == "AxisX_Position",
                $"count={plugin.SharedVarCandidates.Count}");

            plugin.SharedVars.Add(new ScriptSharedVarDef { Name = "ImageIndex", Alias = "imageIndex" });
            plugin.RefreshSharedVarDefs();

            Check("SV15 刷新：旧声明按名字兜底补上 Id/类型",
                plugin.SharedVars[0].VariableId == globalId
                && plugin.SharedVars[0].TypeName == "Int32"
                && plugin.SharedVars[0].Alias == "imageIndex",
                $"Id={plugin.SharedVars[0].VariableId} 类型={plugin.SharedVars[0].TypeName}");

            // 变量管理里改名：Id 不变 → 声明命中，别名与脚本都不用动
            context.Variables[0].Name = "ImageIndex2";
            plugin.RefreshSharedVarDefs();

            Check("SV16 刷新：变量改名后按 Id 命中（别名不动）",
                plugin.SharedVars[0].Name == "ImageIndex2"
                && plugin.SharedVars[0].Alias == "imageIndex"
                && plugin.SharedVars[0].VariableId == globalId,
                $"Name={plugin.SharedVars[0].Name} Alias={plugin.SharedVars[0].Alias}");
        }

        // ---- ⑥ 持久化：SharedVars 随 .vms 往返 ----
        private static void PersistenceRoundTrip()
        {
            var (plugin, step) = NewStep();
            var id = Guid.NewGuid();
            plugin.SharedVars.Add(new ScriptSharedVarDef
            {
                VariableId = id,
                Name = "ImageIndex",
                Alias = "imageIndex",
                Access = ScriptSharedAccess.ReadWrite,
                TypeName = "Int32",
                Remark = "当前序号",
            });

            plugin.OnConfirm(step);

            var reopened = new CSharpScriptPlugin();
            reopened.Initialize(step);

            Check("SV17 持久化：SharedVars 随 InputValues 往返",
                reopened.SharedVars.Count == 1
                && reopened.SharedVars[0].VariableId == id
                && reopened.SharedVars[0].Alias == "imageIndex"
                && reopened.SharedVars[0].Access == ScriptSharedAccess.ReadWrite
                && reopened.SharedVars[0].Remark == "当前序号",
                reopened.SharedVars.Count == 1
                    ? $"Id={reopened.SharedVars[0].VariableId} Alias={reopened.SharedVars[0].Alias}"
                    : $"count={reopened.SharedVars.Count}");
        }

        // ---- ⑥b 取消：共享变量表也要还原（编辑期改的是活模型） ----
        private static void CancelRestoresSharedVars()
        {
            var (plugin, step) = NewStep();
            plugin.SharedVars.Add(new ScriptSharedVarDef
            {
                VariableId = Guid.NewGuid(),
                Name = "ImageIndex",
                Alias = "imageIndex",
                Access = ScriptSharedAccess.ReadWrite,
            });
            plugin.OnConfirm(step);

            var reopened = new CSharpScriptPlugin();
            reopened.Initialize(step);

            reopened.SharedVars[0].Alias = "changed";
            reopened.SharedVars.Add(new ScriptSharedVarDef { Name = "extra" });
            reopened.OnCancel();

            Check("SV31 取消：共享变量表回到打开时（别名与新增行都被丢弃）",
                reopened.SharedVars.Count == 1 && reopened.SharedVars[0].Alias == "imageIndex",
                $"count={reopened.SharedVars.Count} alias={(reopened.SharedVars.Count > 0 ? reopened.SharedVars[0].Alias : "无")}");
        }

        // ---- ⑦ 写闸门：声明只读 / 系统变量 / 存储层失败 ----
        private static void WriteGuards()
        {
            var id = Guid.NewGuid();
            var store = new FakeStore();
            var defs = new[]
            {
                new ScriptSharedVarDef { VariableId = id, Name = "ImageIndex", Access = ScriptSharedAccess.ReadWrite },
                new ScriptSharedVarDef { VariableId = id, Name = "ReadOnlyVar", Access = ScriptSharedAccess.Read },
                new ScriptSharedVarDef { VariableId = id, Name = "AxisX", IsSystem = true, Access = ScriptSharedAccess.ReadWrite },
            };
            var accessor = new ScriptSharedAccessor(defs, store);

            accessor.Set("ImageIndex", 5);
            Check("SV18 写：读写声明 → 落到存储层",
                store.Writes.Count == 1 && Convert.ToInt32(store.Writes[0].Value) == 5,
                $"写次数={store.Writes.Count}");

            string readOnly = Catch(() => accessor.Set("ReadOnlyVar", 1));
            Check("SV19 写：只读声明 → 拒绝并提示改方向",
                readOnly != null && readOnly.Contains("只读") && readOnly.Contains("读写"),
                readOnly ?? "没有抛异常");

            string system = Catch(() => accessor.Set("AxisX", 1));
            Check("SV20 写：系统变量 → 拒绝",
                system != null && system.Contains("系统变量"),
                system ?? "没有抛异常");

            bool ok = accessor.TrySet("ReadOnlyVar", 1, out var error);
            Check("SV21 TrySet：不抛异常、返回 false + 原因",
                !ok && !string.IsNullOrEmpty(error), error ?? "无错误");

            var failing = new FakeStore { WriteError = "设备离线" };
            var failingAccessor = new ScriptSharedAccessor(defs, failing);
            string writeError = Catch(() => failingAccessor.Set("ImageIndex", 1));
            Check("SV22 写失败：原样带出存储层原因",
                writeError == "设备离线", writeError ?? "没有抛异常");
        }

        // ---- ⑧ 并行写集合：脚本声明参与冲突检测（含嵌套容器） ----
        private static void ParallelWriteCollection()
        {
            var id = Guid.NewGuid();

            var par = new ParallelStep("", "并行分组", "BuiltIn_Parallel", "并行分组_0")
            {
                ExecutionMode = VisionMaster.Models.ParallelExecutionMode.Parallel,
            };
            par.Children[0].Steps.Add(ScriptStep("脚本A", id, "ImageIndex", ScriptSharedAccess.ReadWrite));
            par.Children[1].Steps.Add(ScriptStep("脚本B", id, "ImageIndex", ScriptSharedAccess.ReadWrite));

            var writes = FlowCompiler.CollectParallelWrites(par, (_, name) => name);
            Check("SV23 并行收集：两条分支的脚本写同一变量 → 同键 2 条",
                writes.Global.TryGetValue("ImageIndex", out var list) && list.Count == 2,
                $"keys={string.Join(",", writes.Global.Keys)}");

            // 只读声明不算写：把第二条换成只读
            par.Children[1].Steps.Clear();
            par.Children[1].Steps.Add(ScriptStep("脚本C", id, "ImageIndex", ScriptSharedAccess.Read));
            var writes2 = FlowCompiler.CollectParallelWrites(par, (_, name) => name);
            Check("SV24 并行收集：只读声明不计入写集合",
                !writes2.Global.TryGetValue("ImageIndex", out var list2) || list2.Count == 1,
                $"count={(writes2.Global.TryGetValue("ImageIndex", out var l2) ? l2.Count : 0)}");

            // 嵌套：For 循环体里的脚本也要被收集（旧实现只看直接分支步骤）
            var par2 = new ParallelStep("", "并行分组", "BuiltIn_Parallel", "并行分组_1")
            {
                ExecutionMode = VisionMaster.Models.ParallelExecutionMode.Parallel,
            };
            var forStep = new ForStep("", "循环", "BuiltIn_For", "循环_0");
            forStep.Children[0].Steps.Add(ScriptStep("嵌套脚本", id, "ImageIndex", ScriptSharedAccess.ReadWrite));
            par2.Children[0].Steps.Add(forStep);
            par2.Children[1].Steps.Add(ScriptStep("对侧脚本", id, "ImageIndex", ScriptSharedAccess.ReadWrite));

            var writes3 = FlowCompiler.CollectParallelWrites(par2, (_, name) => name);
            Check("SV25 并行收集：嵌套容器里的脚本参与冲突检测（递归）",
                writes3.Global.TryGetValue("ImageIndex", out var list3) && list3.Count == 2,
                $"count={(writes3.Global.TryGetValue("ImageIndex", out var l3) ? l3.Count : 0)}");
        }

        // ---- ⑨ 输入只接上游：绑定弹窗按 AllowGlobals 过滤候选 ----
        private static void InputBindingFilter()
        {
            Check("SV26 绑定事件：AllowGlobals 默认 true（既有插件行为不变）",
                new LinkPathEvent().AllowGlobals,
                "默认 false 会让所有插件的输入突然绑不了全局变量");

            var global = new ToolItemModel { ModuleGroup = "Global", Name = "全局变量 (Global)" };
            var upstream = new ToolItemModel { ModuleGroup = "Steps", Name = "上游步骤" };
            var nodes = new List<ToolItemModel> { global, upstream };

            var filtered = FlowQueryHelper.FilterGlobalNodes(nodes, allowGlobals: false);
            Check("SV27 AllowGlobals=false：全局节点被摘掉、上游节点保留",
                filtered.Count == 1 && ReferenceEquals(filtered[0], upstream),
                $"count={filtered.Count}");

            Check("SV28 AllowGlobals=true：节点原样保留",
                FlowQueryHelper.FilterGlobalNodes(nodes, allowGlobals: true).Count == 2,
                "默认路径不许误伤");

            string xaml = ReadRepoText(@"Plugins\Plugin.CSharpScript\CSharpScriptView.xaml");
            Check("SV29 C#脚本输入行显式 AllowGlobals=False（输入只接上游）",
                xaml != null && xaml.Contains("AllowGlobals=\"False\""),
                xaml == null ? "读不到视图 XAML" : "已声明");

            string editor = ReadRepoText(@"Shard\Core.Controls\LinkableValueEditor.xaml.cs");
            Check("SV30 链接按钮把 AllowGlobals 带进绑定请求",
                editor != null && editor.Contains("AllowGlobals = AllowGlobals"),
                editor == null ? "读不到控件源码" : "已透传");
        }

        private static string ReadRepoText(string relativePath)
        {
            // 从输出目录向上找仓库根（含 VisionMaster.sln 的那一层）
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = System.IO.Path.Combine(dir.FullName, relativePath);
                if (System.IO.File.Exists(candidate))
                    return System.IO.File.ReadAllText(candidate);
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>造一个"C#脚本步骤 + 一条共享变量声明"（编译器只认 InputValues，不加载插件）。</summary>
        private static ActionStep ScriptStep(string name, Guid variableId, string variableName, ScriptSharedAccess access)
        {
            var step = new ActionStep("", "C#脚本",
                "Plugin.CSharpScript.CSharpScriptPlugin, Plugin.CSharpScript", name);
            step.InputValues["SharedVars"] = new ObservableCollection<ScriptSharedVarDef>
            {
                new ScriptSharedVarDef { VariableId = variableId, Name = variableName, Access = access },
            };
            return step;
        }

        private static string Catch(Action action)
        {
            try { action(); return null; }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
