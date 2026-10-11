using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Core.Interfaces;
using HalconDotNet;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
// 本文件 using 了 System.Threading，裸写的 ExecutionContext 会和 System.Threading.ExecutionContext 撞名
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 「变量赋值」节点的断言：两种作用域都要真跑一遍。
    ///
    /// 为什么值得单独钉住
    /// ---------
    /// ① <b>运行时那条路是既有行为</b> —— 加作用域不能把它碰坏（老流程里到处都是这个节点）；
    /// ② <b>全局那条路是新增能力</b> —— 插件物理上够不到"变量管理"所在的程序集，
    ///    只能经 <see cref="IGlobalVariableWriter"/> 走主程序侧；这条通路不跑一遍就不知道通没通；
    /// ③ <b>失败必须说话</b> —— 变量不存在 / 类型不匹配都要给出中文原因，不许静默成功。
    ///    这一条最要紧：若写失败却报成功，操作员会以为画面上的图已经更新了。
    ///
    /// 插件是运行期装载的，本工程没有编译期引用，故用反射构造与设端口。
    /// </summary>
    internal static class VariableAssignmentCheck
    {
        public static void Run()
        {
            Section("[A2] 变量赋值：运行时 / 全局两种作用域");

            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                          .FirstOrDefault(a => a.GetName().Name == "Plugin.Util")
                      ?? Assembly.LoadFrom(Path.Combine(repoRoot, @"Modules\Plugin.Util.dll"));

            var pluginType = asm.GetType("VisionMaster.Plugins.Util.VariableAssignmentPlugin", true)!;

            // 作用域是 string 不是枚举：枚举经 InputValues 往返会抛 InvalidCastException（详见插件里的注释）
            const string scopeRuntime = "Runtime";
            const string scopeGlobal = "Global";

            var workspace = new WorkspaceContext();
            var solution = new SolutionModel { SolutionName = "变量赋值断言" };
            var flow = new FlowModel { FlowName = "赋值断言流程" };
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);
            workspace.SwitchFlow(flow);

            // 备两个全局变量：一个图像（要写进去）、一个整数（用来验类型守门）
            workspace.GlobalVariables.Add(VariableFactory.CreateLocal("Img", typeof(HImage), "断言用图像变量"));
            workspace.GlobalVariables.Add(VariableFactory.CreateLocal("Qty", typeof(int), "断言用计数变量"));
            var varCountBefore = workspace.GlobalVariables.Count;

            Check("前提：全局变量注册后按名可查（注册表订阅了集合变更）",
                workspace.VariableRegistry?.FindByName("Img") != null,
                $"FindByName(Img) = {(workspace.VariableRegistry?.FindByName("Img") == null ? "null" : "命中")}");

            // ---------- 路一：运行时变量（既有行为，不许被碰坏）----------
            var r1 = Assign(pluginType, scopeRuntime, "Out1", "hello", true, workspace);
            Check("【运行时】变量不存在 + 勾了自动创建 → 建成并赋值成功",
                r1.Success && r1.Ctx.LocalVariables.ContainsKey("Out1")
                && (r1.Ctx.LocalVariables["Out1"] as string) == "hello",
                $"Success={r1.Success} Err=[{r1.Error}] "
                + $"值=[{(r1.Ctx.LocalVariables.ContainsKey("Out1") ? r1.Ctx.LocalVariables["Out1"] : "<无>")}]");

            var r2 = Assign(pluginType, scopeRuntime, "Out1", "world", true, workspace);
            Check("【运行时】变量已存在 → 覆盖为新值（不重复创建）",
                r2.Success && (r2.Ctx.LocalVariables["Out1"] as string) == "world",
                $"Success={r2.Success} 值=[{r2.Ctx.LocalVariables["Out1"]}]");

            var r3 = Assign(pluginType, scopeRuntime, "Out2", "x", false, workspace);
            Check("【运行时】变量不存在 + 没勾自动创建 → 明确报「不存在」",
                !r3.Success && r3.Error.Contains("不存在"),
                $"Success={r3.Success} Err=[{r3.Error}]");

            // ---------- 路二：全局变量（新增能力）----------
            HOperatorSet.GenImageConst(out HObject imgObj, "byte", 8, 8);
            var image = new HImage(imgObj);
            try
            {
                var r4 = Assign(pluginType, scopeGlobal, "Img", image, false, workspace);
                var stored = workspace.VariableRegistry?.FindByName("Img")?.Value as HImage;
                Check("【全局】目标存在 + 值是 HImage → 写进全局变量",
                    r4.Success && stored != null && stored.IsInitialized(),
                    $"Success={r4.Success} Err=[{r4.Error}] 变量现值={(stored == null ? "null/不是 HImage" : "HImage ✓")}");

                var r5 = Assign(pluginType, scopeGlobal, "NoSuchVar", image, true, workspace);
                Check("【全局】目标不存在 → 明确报错、且不新建变量（勾了自动创建也不新建）",
                    !r5.Success && workspace.GlobalVariables.Count == varCountBefore,
                    $"Success={r5.Success} Err=[{r5.Error}] 变量数 {varCountBefore} → {workspace.GlobalVariables.Count}");

                var r6 = Assign(pluginType, scopeGlobal, "Qty", "这是文本不是整数", false, workspace);
                Check("【全局】类型不匹配（int 变量写文本）→ 明确报错",
                    !r6.Success && r6.Error.Length > 0,
                    $"Success={r6.Success} Err=[{r6.Error}]");

                var r7 = Assign(pluginType, "globl", "Img", image, false, workspace);
                Check("作用域取值拼错 → 明确报错，不静默当成运行时",
                    !r7.Success && r7.Error.Contains("无法识别"),
                    $"Success={r7.Success} Err=[{r7.Error}]");
            }
            finally
            {
                image.Dispose();
                imgObj?.Dispose();
            }

            // ---------- 路三：把图写进全局图像变量之后，方案还能不能存下来 ----------
            // 旧实现里 VariablePersistenceService.Capture 直接把变量的值（HImage）塞进 VariableDto，
            // 而该字段的约定是"JSON 原生编码：数字/字符串/布尔/数组"。Newtonsoft 序列化 HImage
            // 会走 Halcon 的 serialize_image，句柄为空就抛 HALCON #4056 —— 而这一抛发生在
            // "保存方案"里，于是整个方案都存不下来。
            Section("[A3] 图像变量：只存定义、不存值（存值会让方案存不下来）");

            var w2 = new WorkspaceContext();
            w2.GlobalVariables.Clear();

            HOperatorSet.GenImageConst(out HObject liveObj, "byte", 8, 8);
            var live = new HImage(liveObj);
            var empty = new HImage(); // 句柄为空 —— 就是它把"保存方案"炸掉的
            try
            {
                w2.GlobalVariables.Add(new LocalVariableModel
                { Name = "LiveImg", DataType = typeof(HImage), DefaultValue = live, Value = live });
                w2.GlobalVariables.Add(new LocalVariableModel
                { Name = "EmptyImg", DataType = typeof(HImage), DefaultValue = empty, Value = empty });
                w2.GlobalVariables.Add(new LocalVariableModel
                { Name = "Num", DataType = typeof(int), DefaultValue = 7, Value = 42 });

                var sol = new SolutionModel { SolutionName = "图像变量落盘断言" };
                var serializeError = "";
                try
                {
                    VariablePersistenceService.Capture(sol, w2);
                    SolutionService.Serialize(sol); // ← 旧实现在这一行抛 HALCON #4056
                }
                catch (Exception ex) { serializeError = ex.Message; }

                Check("含图像变量的方案能序列化（旧实现抛 HALCON #4056 object-ID is NULL）",
                    serializeError.Length == 0, serializeError);

                var dto = sol.VariableSnapshots.FirstOrDefault(d => d.Name == "LiveImg");
                Check("图像变量的定义照样落盘（名字 / 类型 / 稳定身份都在）",
                    dto != null && dto.DataTypeString.Contains("HImage") && dto.VariableId != Guid.Empty,
                    $"DataTypeString={dto?.DataTypeString} Id={dto?.VariableId}");

                Check("图像变量的当前值与初始值都不落盘（落 null，不把像素写进 .vms）",
                    dto != null && dto.Value == null && dto.DefaultValue == null,
                    $"Value={dto?.Value ?? "null"} / DefaultValue={dto?.DefaultValue ?? "null"}");

                var numDto = sol.VariableSnapshots.FirstOrDefault(d => d.Name == "Num");
                Check("普通变量的值照旧往返（这次收紧没有误伤）",
                    numDto != null && Convert.ToInt32(numDto.Value) == 42, $"Num.Value={numDto?.Value}");
            }
            finally
            {
                live.Dispose();
                liveObj?.Dispose();
                empty.Dispose();
            }

            // ---------- 路四：作用域=全局变量 —— 「名字」与「值」这两行的来路 ----------
            RunGlobalScopeContract(pluginType);
        }

        // ==================================================================
        //  [A4] 作用域=全局变量：「名字」与「值」这两行的来路
        // ==================================================================

        /// <summary>
        /// 真机反馈（2026-10-10）：
        ///   ① 作用域选「全局变量」后，在变量绑定弹窗里**选中全局变量**当绑定对象 → 编译不通过；
        ///      只有在弹窗的**常量框**里手打变量名才编得过；
        ///   ② 手打了名字之后，值填 21 仍然写不进去 —— 报
        ///      「全局变量「OKCount」的类型是 Int32，无法写入 String」。
        ///
        /// 两条同一个根：**「变量名」这一行要的是"名字"，而通用绑定弹窗只会给"值"**。
        ///   · 弹窗选中全局变量 = 落一条 GlobalVariable 连线（运行期把那个变量的**值**喂给本端口），
        ///     于是"名字"变成了该变量的值 —— 用户想让流程写 OKCount，实际写去的是一个不存在的名字；
        ///   · 值那一行更隐蔽：端口是 <c>InputPort&lt;object&gt;</c>，界面上手填只能产出**文本** "21"，
        ///     而宿主写入器（GlobalVariableWriter）按变量声明类型硬守门、不做任何解析 ——
        ///     于是"给 Int32 变量赋 21"在界面上根本不可能成功。
        ///
        /// 本段把"改好之后应有的样子"逐条钉住：名字行给**名字**（候选来自变量管理/上游变量定义），
        /// 值按目标变量的声明类型解析。走的是真编译 + 真执行（配置界面点「执行」同一条链）。
        /// </summary>
        private static void RunGlobalScopeContract(Type pluginType)
        {
            Section("[A4] 写全局变量：名字的来路 + 文本值按声明类型解析");

            // ---- ① 名字=手填常量，值=界面文本框产出的 "21" ----
            var (compile1, plugin1, ws1) = CompileAndRunGlobalAssign(pluginType, null, "OKCount", "21");
            var written1 = ws1.VariableRegistry?.FindByName("OKCount")?.Value;
            Check("【全局】名字手填 + 值填文本 21 → 写进 Int32 变量（界面只能产出文本，必须按声明类型解析）",
                compile1.Success && ReadSuccess(plugin1) && written1 is int i1 && i1 == 21,
                $"编译={(compile1.Success ? "通过" : "失败")} 运行={ReadSuccess(plugin1)} "
                + $"Err=[{ReadError(plugin1)}] 变量现值=[{Describe(written1)}] 编译错=[{ErrorsOf(compile1)}]");

            // ---- ② 值填了不能解析的文本 → 仍要明确报错（解析不等于来者不拒） ----
            var (compile2, plugin2, ws2) = CompileAndRunGlobalAssign(pluginType, null, "OKCount", "二十一");
            var written2 = ws2.VariableRegistry?.FindByName("OKCount")?.Value;
            Check("【全局】值填了非数字文本 → 明确报错、且不把变量改坏（宽进严出：解析不了就说清楚）",
                compile2.Success && !ReadSuccess(plugin2) && ReadError(plugin2).Length > 0 && written2 == null,
                $"运行={ReadSuccess(plugin2)} Err=[{ReadError(plugin2)}] 变量现值=[{Describe(written2)}]");

            // ---- ② b 千分位分隔符：默认解析会把 "2,5" 读成 25（用户想写 2.5）→ 必须报错，不许静默改值 ----
            var (compile2b, plugin2b, ws2b) = CompileAndRunGlobalAssign(pluginType, null, "OKCount", "2,5");
            var written2b = ws2b.VariableRegistry?.FindByName("OKCount")?.Value;
            Check("【全局】值填 2,5 → 报错而不是静默写成 25（数值文本不许带千分位）",
                compile2b.Success && !ReadSuccess(plugin2b) && written2b == null,
                $"运行={ReadSuccess(plugin2b)} Err=[{ReadError(plugin2b)}] 变量现值=[{Describe(written2b)}]");

            // ---- ③ 名字行挂「全局变量连线」（通用绑定弹窗选全局变量的真实产物）----
            // 这一条是**观察项**：断言的是"它写不到 OKCount"——
            // 也就是说这条连线替代不了"填名字"，视图不能再把它当成名字行的答案（见 ⑦ 的静态扫描）。
            var (compile3, plugin3, ws3) = CompileAndRunGlobalAssign(
                pluginType,
                new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "OKCount", "全局变量 (Global).OKCount"),
                null, "21");
            var written3 = ws3.VariableRegistry?.FindByName("OKCount")?.Value;
            Check("【全局】名字行挂「选中的全局变量」连线 → 写不到 OKCount，且运行期明确失败（连线给的是值，当不了名字）",
                written3 == null && compile3.Success && !ReadSuccess(plugin3) && ReadError(plugin3).Length > 0,
                $"编译={(compile3.Success ? "通过" : "失败")} 运行={ReadSuccess(plugin3)} "
                + $"Err=[{ReadError(plugin3)}] 变量现值=[{Describe(written3)}] 编译错=[{ErrorsOf(compile3)}]");

            // ③ b：名字行挂"塞不进 string 的变量"（HImage / 数组……）→**编译期**就被类型守门拦下。
            // 这一条就是真机"选了全局变量之后编译不通过"的那一类（2026-09-25 线序检测文档 §10 也记过同一条）：
            // 编译不过 → 用户只能把名字当常量手打。修好之后名字行不再产生连线，这条路走不到。
            var (compile4, _, _) = CompileAndRunGlobalAssign(
                pluginType,
                new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "Img", "全局变量 (Global).Img"),
                null, "21", addImageVariable: true);
            Check("【编译被拦】名字行挂 HImage 变量连线 → 编译报「连线类型不匹配」（真机说的编译不通过就是这一类）",
                !compile4.Success && compile4.Errors.Any(e => e.Message.Contains("连线类型不匹配")),
                $"编译={(compile4.Success ? "通过" : "失败")} 编译错=[{ErrorsOf(compile4)}]");

            RunNamePickerContract(pluginType);
        }

        /// <summary>
        /// 「变量名」这一行的新契约：候选来自宿主快照（变量管理里那些名字 + 上游「变量定义」声明的名字），
        /// 不再走通用绑定弹窗（它给的是"值"，见 <see cref="RunGlobalScopeContract"/>）。
        /// </summary>
        private static void RunNamePickerContract(Type pluginType)
        {
            Section("[A4] 变量名行：候选快照 + 提示 + 旧连线自愈");

            // ---- ④ 宿主快照：全局变量 + 上游变量定义的名字都在 ----
            var (workspace, assignStep, _) = NewWorkspaceWithVariables();
            var options = PluginVariableOptions.Build(workspace, assignStep.StepID);
            var globals = options.Where(o => o.Scope == PluginVariableOptions.ScopeGlobal).Select(o => o.Name).ToList();
            var runtimes = options.Where(o => o.Scope == PluginVariableOptions.ScopeRuntime).Select(o => o.Name).ToList();
            Check("【快照】候选含变量管理里的全局变量（带声明类型）+ 上游「变量定义」声明的运行时变量",
                globals.Contains("OKCount") && globals.Contains("SN") && runtimes.Contains("Counter")
                && options.First(o => o.Name == "OKCount").TypeName.Contains("Int32"),
                $"全局=[{string.Join(",", globals)}] 运行时=[{string.Join(",", runtimes)}] "
                + $"OKCount.TypeName=[{options.FirstOrDefault(o => o.Name == "OKCount")?.TypeName}]");

            // ---- ⑤ 插件侧：候选按作用域过滤；改名字要给出"它是什么类型" ----
            var context = new PluginConfigContext { Variables = options };
            var plugin = (VisionPluginBase)Activator.CreateInstance(pluginType)!;
            if (plugin is not IPluginConfigContextProvider provider)
            {
                Check("【插件】实现 IPluginConfigContextProvider（拿宿主快照）", false,
                    "插件没实现该接口 —— 名字行拿不到候选（只能手打）");
                return;
            }
            provider.SetConfigContext(context);
            plugin.Initialize((IStepConfigData)assignStep);

            Check("【插件】作用域=全局 → 候选只剩全局变量",
                NameCandidates(plugin).Count == 2 && NameCandidates(plugin).Contains("OKCount"),
                $"候选=[{string.Join(",", NameCandidates(plugin))}]");

            plugin.Initialize((IStepConfigData)assignStep);
            var hint = NameHint(plugin);
            Check("【插件】提示里点明目标变量的声明类型（用户据此才知道值该填什么样）",
                hint.Contains("Int32"),
                $"提示=[{hint}]");

            SetScope(plugin, "Runtime");
            Check("【插件】作用域切到运行时 → 候选换成上游变量定义声明的名字",
                NameCandidates(plugin).Count == 1 && NameCandidates(plugin)[0] == "Counter",
                $"候选=[{string.Join(",", NameCandidates(plugin))}]");
            Check("【插件】切作用域后提示跟着换（名字不在运行时候选里时会说清后果）",
                NameHint(plugin).Contains("运行时") || NameHint(plugin).Contains("自动创建"),
                $"提示=[{NameHint(plugin)}]");

            // ---- ⑥ 旧连线自愈：只在"用户真的动了这一行"时解除 ----
            SetScope(plugin, "Global");
            assignStep.SetLink("Name", new LinkReference(
                LinkKind.GlobalVariable, Guid.Empty, "OKCount", "全局变量 (Global).OKCount"));
            plugin.Initialize((IStepConfigData)assignStep);
            Check("【插件】打开窗口时能认出这一行的旧连线（并写进提示，不静默）",
                NameHint(plugin).Contains("上游"),
                $"提示=[{NameHint(plugin)}]");

            // 程序灌值（试运行把上游实际值桥接进配置实例端口、候选重算后的回填）**不是**用户编辑：
            // 若把"端口值变了"当成"用户改名"，用户什么都没动、连线就被解除（改的是活模型，取消也退不回来）
            SetPortValue(plugin, "VariableName", "SN");
            Check("【插件】程序灌值不解除旧连线（只有用户输入才算编辑）",
                assignStep.IsLinked("Name"),
                $"IsLinked(Name)={assignStep.IsLinked("Name")}（期望 True：这一步是程序写值）");

            NotifyNameEdited(plugin);
            Check("【插件】用户在这一行选了/输了名字 → 旧连线自动解除（否则键盘敲下去不生效，是静默故障）",
                !assignStep.IsLinked("Name"),
                $"IsLinked(Name)={assignStep.IsLinked("Name")} 地址=[{assignStep.GetLinkedAddress("Name")}]");

            // ---- ⑥ b 换作用域不许把名字清空 ----
            // 可编辑 ComboBox 在当前有选中项时，ItemsSource 被 Clear() 会把 Text 一并清空并沿
            // TwoWay 绑定把空串**推回端口**（复核实测的 WPF 行为）——
            // "选个名字 → 切作用域"这个连招会静默把名字清掉，再点确定就落盘成空名。
            //
            // 断言宿主里没有真的 ComboBox，所以这里**手动模拟绑定中的 ComboBox 干的事**：
            // 候选集合被 Reset（=Clear()）时，把端口值置空（=TwoWay 回写）。
            // 不模拟的话，端口值从头到尾都是本用例写进去的 "OKCount"，
            // 插件里"记住—顶回"那段补偿删掉了这条断言照样绿（复核抓到的假绿）。
            SetPortValue(plugin, "VariableName", "OKCount");
            var candidates = CandidateCollection(plugin);
            int writeBackFired = 0;
            System.Collections.Specialized.NotifyCollectionChangedEventHandler comboBoxWriteBack = (_, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                {
                    writeBackFired++;
                    SetPortValue(plugin, "VariableName", string.Empty);
                }
            };
            candidates.CollectionChanged += comboBoxWriteBack;
            try
            {
                SetScope(plugin, "Runtime"); // 触发候选重算（内部 Clear + 重填）
            }
            finally
            {
                candidates.CollectionChanged -= comboBoxWriteBack;
            }

            Check("【插件】换作用域重算候选后名字还在（候选重填会让绑定回写空串——必须顶回去）",
                (string?)Port(plugin.GetType(), plugin, "VariableName").Value == "OKCount",
                $"名字=[{Port(plugin.GetType(), plugin, "VariableName").Value}] 候选=[{string.Join(",", NameCandidates(plugin))}] "
                + $"模拟回写次数={writeBackFired}（必须 ≥1：模拟没跑起来这条断言就是假绿）");

            // ---- ⑦ 静态扫描：名字行不能再接回通用绑定弹窗 ----
            var xamlPath = ResolveRepoFile(@"Plugins\Plugin.Utility\VariableAssignmentView.xaml");
            if (xamlPath == null)
            {
                Check("【视图】名字行不是通用绑定控件（静态扫描）", false,
                    "断言过期：定位不到 VariableAssignmentView.xaml（文件被移动/改名？）");
                return;
            }
            var xaml = File.ReadAllText(xamlPath);
            int editors = System.Text.RegularExpressions.Regex
                .Matches(xaml, @"<cv:LinkableValueEditor").Count;
            // 精确到绑定串：光看"文件里有没有 ComboBox"会把「作用域」那一行的下拉也算进来
            bool nameBoxBound = xaml.Contains(
                "Text=\"{Binding VariableName.Value, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"");
            bool candidatesBound = xaml.Contains("ItemsSource=\"{Binding NameCandidates}\"");
            bool userInputReported = File.ReadAllText(
                    ResolveRepoFile(@"Plugins\Plugin.Utility\VariableAssignmentView.xaml.cs") ?? xamlPath)
                .Contains("NotifyNameEditedByUser");
            // 光有上报方法不够：三处挂载删掉了照样绿（复核提出的弱断言）。逐个钉住挂载串。
            var viewCs = File.ReadAllText(
                ResolveRepoFile(@"Plugins\Plugin.Utility\VariableAssignmentView.xaml.cs") ?? xamlPath);
            bool hooksPresent = viewCs.Contains("NameBox.PreviewTextInput")
                && viewCs.Contains("NameBox.SelectionChanged")
                && viewCs.Contains("DataObject.PastingEvent");
            Check("【视图】名字行是「名字编辑器」（候选下拉 + 手填文本 + 用户输入上报），通用绑定控件只剩「值」那一行",
                editors == 1 && nameBoxBound && candidatesBound && userInputReported && hooksPresent,
                $"LinkableValueEditor 出现 {editors} 次（应为 1：值行）；名字行两向绑定={nameBoxBound}；"
                + $"候选绑定={candidatesBound}；上报用户输入={userInputReported}；输入事件挂载={hooksPresent}");
        }

        /// <summary>
        /// 造一个「变量赋值（作用域=全局，名字=OKCount）+ 上游一个变量定义节点」的图纸，
        /// 给候选快照与插件侧断言共用。
        /// </summary>
        private static (WorkspaceContext Workspace, ActionStep Assign, ActionStep Define) NewWorkspaceWithVariables()
        {
            var workspace = NewWorkspaceWithGlobals();
            var flow = workspace.CurrentFlow!;

            var defineStep = new ActionStep("\uE700", "变量定义", "VM.Checks.VariableDefinitionPlugin", "变量定义_0");
            defineStep.SetInputValue("Name", "Counter");
            defineStep.SetInputValue("Type", "int");
            flow.Steps.Add(defineStep);

            var assignStep = new ActionStep("\uE700", "变量赋值", "VM.Checks.VariableAssignment", "变量赋值_0");
            assignStep.SetInputValue("Name", "OKCount");
            assignStep.SetInputValue("Scope", "Global");
            flow.Steps.Add(assignStep);

            return (workspace, assignStep, defineStep);
        }

        /// <summary>建一个只装了全局变量的工作区（OKCount=Int32 / SN=String），并切好方案与流程</summary>
        /// <param name="addImageVariable">再补一个 HImage 变量 Img（断言"塞不进 string 的类型会被编译期拦下"）</param>
        private static WorkspaceContext NewWorkspaceWithGlobals(bool addImageVariable = false)
        {
            var workspace = new WorkspaceContext();
            // 先清掉框架预置的演示变量（InitializeCommonVariables）：本次要数候选条数，
            // 混着 12 个演示变量既算不清，也说不清"这个名字是从哪来的"
            workspace.GlobalVariables.Clear();
            workspace.GlobalVariables.Add(VariableFactory.CreateLocal("OKCount", typeof(int), "断言用计数变量"));
            workspace.GlobalVariables.Add(VariableFactory.CreateLocal("SN", typeof(string), "断言用文本变量"));
            if (addImageVariable)
                workspace.GlobalVariables.Add(VariableFactory.CreateLocal("Img", typeof(HImage), "断言用图像变量"));

            var solution = new SolutionModel { SolutionName = "全局赋值断言" };
            var flow = new FlowModel { FlowName = "全局赋值断言流程" };
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);
            workspace.SwitchFlow(flow);
            return workspace;
        }

        /// <summary>
        /// 造「变量赋值（作用域=全局）」图纸 → 真编译 → 真跑一遍，把编译结果与插件实例交回来。
        ///
        /// 走真编译（而不是像 [A2] 那样直接调 RunAlgorithm）是必须的：真机反馈的两条都发生在
        /// "在配置界面配好 → 执行"这条链上——名字行在编译期被当连线解析，值在 ApplyConfigValues 里灌回端口，
        /// 两处都不是直调 RunAlgorithm 能覆盖的。
        /// </summary>
        private static (CompilationResult Compile, IVisionPlugin Plugin, WorkspaceContext Workspace)
            CompileAndRunGlobalAssign(Type pluginType, LinkReference? nameLink, string? constantName, object value,
                bool addImageVariable = false)
        {
            var workspace = NewWorkspaceWithGlobals(addImageVariable);
            var step = new ActionStep("\uE700", "变量赋值", pluginType.AssemblyQualifiedName!, "变量赋值_0");
            step.SetInputValue("Scope", "Global");
            step.SetInputValue("Value", value);
            step.SetInputValue("CreateIfNotExists", false);
            if (nameLink != null)
            {
                // 模拟"在变量绑定弹窗里选中了全局变量"的产物：连线指向该变量（Id 优先、名字兜底）
                var link = new LinkReference(nameLink.Kind, nameLink.TargetStepId,
                    nameLink.TargetPortName, nameLink.DisplayAddress)
                {
                    TargetVariableId = workspace.VariableRegistry!.FindByName(nameLink.TargetPortName)?.VariableId ?? Guid.Empty,
                };
                step.SetLink("Name", link);
            }
            else
            {
                step.SetInputValue("Name", constantName!);
            }

            var flow = workspace.CurrentFlow!;
            flow.Steps.Add(step);

            var compile = new FlowCompiler(workspace).Compile(flow.Steps, flow.FlowName);
            if (!compile.Success)
                return (compile, null!, workspace);

            var session = new FlowSession { FlowName = flow.FlowName, ExecutionEngine = compile.Data };
            session.AddBlueprintsDeep(flow.Steps);
            var ctx = new ExecutionContext(new StubLog(), session, workspace, new CancellationTokenSource().Token);
            compile.Data!.Run(ctx);

            var node = (CompiledPluginNode)compile.Data.NodeLookup[step.StepID];
            return (compile, node.ExternalPlugin, workspace);
        }

        private static bool ReadSuccess(IVisionPlugin plugin)
            => plugin != null && plugin.Outputs.TryGetValue("Success", out var p) && p.Value is bool b && b;

        private static string ReadError(IVisionPlugin plugin)
            => plugin != null && plugin.Outputs.TryGetValue("ErrorMessage", out var p)
                ? p.Value?.ToString() ?? string.Empty
                : string.Empty;

        private static string Describe(object? value) => value == null ? "<空>" : $"{value} ({value.GetType().Name})";

        private static string ErrorsOf(CompilationResult compile)
            => compile.Success ? "" : string.Join(" | ", compile.Errors.Select(e => e.Message));

        /// <summary>读插件侧"按作用域过滤后的候选名字"（实现细节不暴露到公共契约，断言走反射）</summary>
        private static List<string> NameCandidates(object plugin)
        {
            var value = plugin.GetType().GetProperty("NameCandidates")?.GetValue(plugin) as System.Collections.IEnumerable;
            return value == null ? new List<string>() : value.Cast<object>().Select(o => o?.ToString() ?? "").ToList();
        }

        private static string NameHint(object plugin)
            => plugin.GetType().GetProperty("NameHint")?.GetValue(plugin)?.ToString() ?? "";

        /// <summary>模拟视图上报"用户动了名字这一行"（视图侧接的是 PreviewTextInput / SelectionChanged）</summary>
        private static void NotifyNameEdited(object plugin)
            => plugin.GetType().GetMethod("NotifyNameEditedByUser")?.Invoke(plugin, null);

        /// <summary>候选集合的变更通道（断言里用它模拟"绑定中的 ComboBox 收听 ItemsSource 变化"）</summary>
        private static System.Collections.Specialized.INotifyCollectionChanged CandidateCollection(object plugin)
            => (System.Collections.Specialized.INotifyCollectionChanged)
                plugin.GetType().GetProperty("NameCandidates")!.GetValue(plugin)!;

        private static void SetScope(object plugin, string scope) => SetPortValue(plugin, "Scope", scope);

        private static void SetPortValue(object plugin, string propertyName, object value)
            => Port(plugin.GetType(), plugin, propertyName).Value = value;

        private static string? ResolveRepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private sealed class Outcome
        {
            public bool Success;
            public string Error = "";
            public ExecutionContext Ctx = null!;
        }

        /// <summary>设好端口、跑一次，把结果端口读回来</summary>
        private static Outcome Assign(
            Type pluginType, object scope, string name, object value, bool create,
            WorkspaceContext workspace)
        {
            var plugin = (VisionPluginBase)Activator.CreateInstance(pluginType)!;
            plugin.InstanceName = "断言.变量赋值";

            Port(pluginType, plugin, "VariableName").Value = name;
            Port(pluginType, plugin, "Value").Value = value;
            Port(pluginType, plugin, "CreateIfNotExists").Value = create;
            Port(pluginType, plugin, "Scope").Value = scope;

            var session = new FlowSession { FlowName = "赋值断言" };
            var ctx = new ExecutionContext(new StubLog(), session, workspace, new CancellationTokenSource().Token);
            plugin.RunAlgorithm(ctx);

            return new Outcome
            {
                Success = (bool)plugin.Success.Value,
                Error = plugin.ErrorMessage.Value as string ?? "",
                Ctx = ctx,
            };
        }

        private static IInputPort Port(Type pluginType, object plugin, string propertyName)
            => (IInputPort)pluginType.GetProperty(propertyName)!.GetValue(plugin)!;
    }
}
