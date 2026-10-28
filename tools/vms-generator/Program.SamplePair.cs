using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

internal static partial class Program
{
    // ==================================================================
    //  视觉样例对拍（--pair）：在既有 .vms 上做**增量改造**（不是整案重生成）
    //
    //  改造清单（对齐 docs\方案设计\AI生成VMS文件指南.md）：
    //    1. 七条工位流程补「结果判定 → 变量输出」两环：
    //       判定表（Plugin.JudgeTable）+ 变量赋值（旧单值模型，test-suite.vms 同款形状）
    //    2. 工位流程 InvokeType 置 32（子程序位），否则 Main 的「调用流程」会被宿主拒绝
    //    3. 五条文件夹流程的 For.LoopCount 接全局参数变量（参数配置页可调）
    //    4. Blob 缺陷流程的 MmPerPixel 端口接全局参数变量
    //    5. Home / Main / End 骨架编排：
    //       Main = 七工位串行（调用流程）→ C# 统计脚本 → CSV 账本（喂报表与历史表格）
    //    6. VariableSnapshots：工位结果 / 分数 / 计数 / 良率 / 可调参数
    //    7. Scada：五页组态（主页 / 图像显示 / 参数配置 / 结果统计 / 运行日志）+ 报警 + 变量事件
    //
    //  已知边界（不静默，写进运行说明）：
    //    - 五条文件夹流程的 FolderPath/FilePath 必须是绝对路径
    //      （ImageAcquisition 的文件夹模式不做方案根锚定，指南 §1.1 已承认该边界）；
    //      其余一切资产（图像显示页的样图、模板）都是方案内相对路径。
    //    - 主界面图像视图窗口 1~7 与七条工位流程的结果显示一一对应；
    //      组态「图像显示」页放的是静态样图 + 说明，实时图在宿主图像窗口看。
    // ==================================================================

    const string PairSolutionPath = @"D:\C#\VM\解决方案\视觉样例对拍\视觉样例对拍.vms";
    const string PairBackupDir = @"D:\C#\VM\artifacts\logs\20261028\backup-vms";

    // 插件 AQN（与 Modules\ 下 DLL 逐字一致）
    const string P_JUDGE = "Plugin.JudgeTable.JudgeTablePlugin, Plugin.JudgeTable" + S;
    const string P_IMGASSIGN = "Plugin.ImageAssign.ImageAssignPlugin, Plugin.ImageAssign" + S;
    // P_UPLOAD（结果上报）已在 Program.cs 里定义，本文件直接用

    /// <summary>
    /// HalconDotNet.HImage 的 AssemblyQualifiedName。
    /// 用全名而不是短名：TypeCache.LoadType 对短名是"遍历已加载程序集"，
    /// 而方案加载时（VariablePersistenceService.Restore）halcondotnet 可能还没被加载
    /// → 变量被静默跳过 → 运行期「写工位图像」报"变量不存在"。
    /// 全名走 Type.GetType 会按需加载程序集，不受加载顺序影响。
    /// </summary>
    const string T_HIMG = "HalconDotNet.HImage, halcondotnet, Version=23.5.0.0, Culture=neutral, PublicKeyToken=4973bed59ddbf2b8";

    /// <summary>一条工位流程的改造参数</summary>
    sealed class PairStation
    {
        public string FlowName;      // .vms 里的流程名（寻址键）
        public string Short;         // 变量前缀（ASCII）
        public string Title;         // 界面展示名
        public string ImagePath;     // 图像显示页样图（方案内相对路径）
        public int ViewIndex;        // 宿主图像视图窗口号
        public string HdevNote;      // 对应 HALCON 例程（说明用）
        public bool HasFor;          // 是否文件夹循环流程（挂 LoopCount 参数）
        public string LoopVar;       // 循环张数参数变量名
        public int LoopDefault;      // 循环张数默认值 = 样例图张数
        public string Kind;          // Matching / Blob / CodeReader / Deformable
        public string ScoreVar;      // 主数值变量名（IOField 显示）
        public string ScoreLabel;    // 主数值标签
    }

    static readonly PairStation[] PairStations =
    {
        new PairStation { FlowName="模板匹配_NCC散焦图", Short="Ncc", Title="NCC 匹配 · SMD 散焦",
            ImagePath=@"Image\smd_on_chip_01.png", ViewIndex=1, HdevNote="Matching/Correlation-Based/find_ncc_model_defocused.hdev",
            HasFor=true, LoopVar="Loop_Ncc", LoopDefault=11, Kind="Matching", ScoreVar="Ncc_Score", ScoreLabel="匹配分数" },
        new PairStation { FlowName="形状匹配_散焦PCB", Short="Pcb", Title="形状匹配 · PCB 散焦",
            ImagePath=@"Image\pcb_focus_telecentric_001.png", ViewIndex=2, HdevNote="Applications/Position-Recognition-2D/matching_defocused_pcb.hdev",
            HasFor=true, LoopVar="Loop_Pcb", LoopDefault=121, Kind="Matching", ScoreVar="Pcb_Score", ScoreLabel="匹配分数" },
        new PairStation { FlowName="Blob缺陷检测_标签字符", Short="Label", Title="Blob 缺陷 · 标签字符",
            ImagePath=@"Image\label_01.png", ViewIndex=3, HdevNote="Applications/OCR/find_text_bottle_label.hdev",
            HasFor=true, LoopVar="Loop_Label", LoopDefault=10, Kind="Blob", ScoreVar="Label_DefectCount", ScoreLabel="缺陷数" },
        new PairStation { FlowName="码读取_Code128", Short="Code128", Title="条码 · Code128",
            ImagePath=@"Image\code12801.png", ViewIndex=4, HdevNote="Identification/Bar-Code/code128.hdev",
            HasFor=true, LoopVar="Loop_Code128", LoopDefault=3, Kind="CodeReader", ScoreVar="Code128_Count", ScoreLabel="码个数" },
        new PairStation { FlowName="二维码读取_QR", Short="Qr", Title="二维码 · QR",
            ImagePath=@"Image\qr_workpiece_01.png", ViewIndex=5, HdevNote="Applications/Data-Codes/qrcode_simple.hdev",
            HasFor=true, LoopVar="Loop_Qr", LoopDefault=9, Kind="CodeReader", ScoreVar="Qr_Count", ScoreLabel="码个数" },
        new PairStation { FlowName="形变匹配_局部非刚性", Short="DefLocal", Title="局部形变 · 棋盘格",
            ImagePath=@"Image\checkerboard_400.png", ViewIndex=6, HdevNote="Matching/Deformable（local_deformable_match 系）",
            HasFor=false, LoopVar="", LoopDefault=0, Kind="Deformable", ScoreVar="DefLocal_Score", ScoreLabel="形变分数" },
        new PairStation { FlowName="形变匹配_平面透视", Short="DefPlanar", Title="平面形变 · 刹车盘",
            ImagePath=@"Image\brake_disk_bike_01.png", ViewIndex=7, HdevNote="Matching/Deformable/create_planar_uncalib_deformable_model_xld.hdev",
            HasFor=false, LoopVar="", LoopDefault=0, Kind="Deformable", ScoreVar="DefPlanar_Score", ScoreLabel="形变分数" },
    };

    static void RunPair()
    {
        // ---------- 0) 备份（先备份后改写，绝不裸写） ----------
        Directory.CreateDirectory(PairBackupDir);
        var backup = Path.Combine(PairBackupDir,
            "视觉样例对拍-pre-pair-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".vms");
        File.Copy(PairSolutionPath, backup, overwrite: true);
        Console.WriteLine("backup: " + backup);

        // ---------- 1) 读 ----------
        var root = JObject.Parse(File.ReadAllText(PairSolutionPath, Encoding.UTF8));
        var flows = (JArray)root["Flows"];

        // ---------- 2) 七条工位流程：判定 + 输出 + 子程序位 + 参数联动 ----------
        foreach (var st in PairStations)
        {
            var flow = flows.FirstOrDefault(f => f["FlowName"]?.Value<string>() == st.FlowName) as JObject;
            if (flow == null) { Console.WriteLine("MISSING FLOW: " + st.FlowName); continue; }

            // 子程序位(32) + 随整线启动(128) = 160：
            //   · 子程序位让 Main 的「调用流程」仍能拉起它（整件串行模式）；
            //   · 整线位让工具栏「启动整线 / 整线循环」把它和另外六个工位**各起各的会话**一起启动，
            //     一条工位失败/卡死只收尾它自己（FlowBatchSelector 的新口径，2026-10-28）。
            // 工位 Role 保持 0（None）：整线成员是"平级"，不是主流程的子流程。
            flow["InvokeType"] = 160;

            // 算法步骤（按 PluginTypeName 前缀定位，不依赖步骤名）
            var algo = PairFindStep(flow["Steps"], st);
            if (algo == null) { Console.WriteLine("MISSING ALGO STEP: " + st.FlowName); continue; }

            // For 流程：循环张数写进 For 的手填值（DefaultLoopCount 已是初值；这里保持不接线）
            // 为什么不用全局变量联动 LoopCount：FlowCanvasChecks 的样例流程断言只做
            // SwitchSolution、不做 VariablePersistenceService.Restore，全局变量链接会被判
            // 「找不到全局变量」→ 整条流程编译失败。参数页仍可通过流程属性改 DefaultLoopCount。
            if (st.HasFor)
            {
                var forStep = flow["Steps"].OfType<JObject>()
                    .FirstOrDefault(s => s["$type"]?.Value<string>()?.Contains("ForStep") == true);
                if (forStep != null)
                {
                    forStep["DefaultLoopCount"] = st.LoopDefault;
                    Console.WriteLine($"  {st.FlowName}: For.DefaultLoopCount = {st.LoopDefault}（手填值，不接全局变量）");
                }
            }

            // Blob 流程：像素当量写手填值（MmPerPixel 端口不接线，理由同上）
            if (st.Kind == "Blob")
            {
                var inputs2 = (JObject)algo["InputValues"];
                if (inputs2["PixelSizeMm"] == null) inputs2["PixelSizeMm"] = 0.05;
                if (inputs2["MmPerPixel"] == null) inputs2["MmPerPixel"] = 0.05;
            }

            // ---- 组装追加步骤：判定表 + 变量赋值（追加在算法定级分支的同一容器里） ----
            var append = new JArray();

            string judgeStepName = "结果判定_" + st.Short;
            string judgeSeed = "pair:judge:" + st.Short;
            var judgeLinks = new JObject();
            var rules = new JArray();
            PairBuildRules(st, algo, rules, judgeLinks);
            append.Add(PairJudgeStep(judgeSeed, judgeStepName, rules, judgeLinks,
                "按规则逐项判定（HALCON 例程口径）：" + st.HdevNote));

            append.Add(PairVarAssign("pair:res:" + st.Short, "写工位判定_" + st.Short,
                st.Short + "_Result", L(judgeSeed, "IsOk", judgeStepName)));
            append.Add(PairVarAssign("pair:score:" + st.Short, "写工位数值_" + st.Short,
                st.ScoreVar, LById(PairStepId(algo), PairScorePort(st), PairStepName(algo))));
            append.Add(PairVarAssign("pair:reason:" + st.Short, "写工位说明_" + st.Short,
                st.Short + "_Reason", L(judgeSeed, "Reason", judgeStepName)));
            if (st.Kind == "CodeReader")
            {
                append.Add(PairVarAssign("pair:text:" + st.Short, "写码文本_" + st.Short,
                    st.Short + "_Text", LById(PairStepId(algo), "Text", PairStepName(algo))));
            }

            // 标注图写入全局图像变量（组态工位卡的 Hmi.ImageView 绑它）：
            // 数据源取「结果显示」的 AnnotatedImage —— 标注框与判定文字已经烧进图里，
            // 是操作员在组态画面里最该看到的那一张。七个工位都有结果显示，覆盖完整。
            var display = PairFindStepByPlugin(flow["Steps"], "Plugin.ResultDisplay.");
            if (display != null)
            {
                append.Add(PairImageAssign("pair:img:" + st.Short, "写工位图像_" + st.Short,
                    st.Short + "_Image", LById(PairStepId(display), "AnnotatedImage", PairStepName(display))));
            }

            // 循环末尾延时 200ms：每张图的标注结果都要在人眼里停留一下，
            // 否则连续跑图时宿主视觉图像窗口只看到最后一张（用户实测诉求）。
            // 延时**不进 append**：它必须永远是循环体的最后一步，而"结果上报"要排在它前面。
            // 三段各自有幂等闸（判定 / 上报 / 延时），重跑 --pair 只补缺的那段。
            string uploadName = "结果上报_" + st.Short;
            string delayName = "节拍延时_" + st.Short;

            // 追加位置：有 For 的进循环体末尾，无 For 的进顶层末尾（与算法同容器，避免跨分支取数）
            JArray target;
            if (st.HasFor)
            {
                var forStep = flow["Steps"].OfType<JObject>()
                    .FirstOrDefault(s => s["$type"]?.Value<string>()?.Contains("ForStep") == true);
                target = (JArray)forStep["Children"][0]["Steps"];
            }
            else
            {
                target = (JArray)flow["Steps"];
            }

            // ---- 第 1 段：判定表 + 变量赋值 + 图像赋值 ----
            if (PairContainsStepName(flow["Steps"], judgeStepName))
            {
                Console.WriteLine($"  {st.FlowName}: 已含「{judgeStepName}」，跳过判定/输出段");
            }
            else
            {
                foreach (var s in append) target.Add(s);
                Console.WriteLine($"  {st.FlowName}: 追加 {append.Count} 步（判定表 + 输出）");
            }

            // ---- 第 2 段：结果上报（整线模式下的工位级 MES/IM 上报）----
            // 默认「仅 NG」+ 未填 URL：正常件静默通过，NG 件留一条 WARN 提示"未配置目标 URL"。
            // 为什么不用「触发端口」时机：那要把 TriggerPort 接一个全局变量，而 FlowCanvasChecks
            // 的样例流程复跑不做变量恢复，全局变量连线会被判「找不到全局变量」→ 整条流程编译失败。
            if (PairContainsStepName(flow["Steps"], uploadName))
            {
                Console.WriteLine($"  {st.FlowName}: 已含「{uploadName}」，跳过上报段");
            }
            else
            {
                target.Add(PairResultUpload(uploadName, st, pieceLevel: false));
                Console.WriteLine($"  {st.FlowName}: 追加「{uploadName}」（仅 NG 上报，URL 留空）");
            }

            // ---- 第 3 段：节拍延时（循环体最后一步）----
            if (!st.HasFor)
            {
                // 单图流程（形变匹配）不加延时
            }
            else if (PairContainsStepName(flow["Steps"], delayName))
            {
                Console.WriteLine($"  {st.FlowName}: 已含「{delayName}」，跳过延时段");
            }
            else
            {
                target.Add(Action("pair:delay:" + st.Short, delayName, "常用工具",
                    P_DELAY, "延时", "\uf017", new JObject { ["Delay"] = 200 }));
                Console.WriteLine($"  {st.FlowName}: 追加「{delayName}」200ms");
            }
        }

        // ---------- 2.5) 四条扩展工位流程（几何计算 / 前置质量门 / 差分 / 颜色序列） ----------
        PairBuildExtraFlows(flows);

        // ---------- 3) Home / Main / End 骨架 ----------
        PairRebuildHome(flows);
        PairRebuildMain(flows);
        PairRebuildEnd(flows);

        // ---------- 4) 变量快照 ----------
        root["VariableSnapshots"] = PairBuildVariables();
        Console.WriteLine("variables: " + ((JArray)root["VariableSnapshots"]).Count);

        // ---------- 5) 组态 ----------
        root["Scada"] = PairBuildScada(flows);

        // ---------- 6) 写（UTF-8 无 BOM）+ 自检 ----------
        // 流程 Version 统一 +1：脚本正文已变，递增版本强制宿主重新编译
        //（CSharpScriptEngine 按内容指纹缓存；只改正文不改版本时，宿主的会话准备可能跳过重编译）
        foreach (var f in flows.OfType<JObject>())
        {
            var v = f["Version"]?.Value<int>() ?? 0;
            f["Version"] = v + 1;
        }

        var json = JsonConvert.SerializeObject(root, Formatting.Indented);
        File.WriteAllText(PairSolutionPath, json, new UTF8Encoding(false));
        Console.WriteLine("written: " + PairSolutionPath + "  bytes=" + new FileInfo(PairSolutionPath).Length);

        PairSelfCheck(json);
    }

    // ------------------------------------------------------------------
    //  工位流程内的步骤查找
    // ------------------------------------------------------------------
    static string PairStepId(JObject step) => step["StepID"]?.Value<string>() ?? "";
    static string PairStepName(JObject step) => step["StepName"]?.Value<string>() ?? "";

    static string PairScorePort(PairStation st) => st.Kind switch
    {
        "Matching" or "Deformable" => "Score",
        "Blob" => "DefectCount",
        "CodeReader" => "Count",
        _ => "Score",
    };

    static JObject PairFindStep(JToken steps, PairStation st)
        => PairFindStepDeep(steps, st);

    static JObject PairFindStepDeep(JToken steps, PairStation st)
    {
        foreach (var s in steps.OfType<JObject>())
        {
            var t = s["PluginTypeName"]?.Value<string>() ?? "";
            if (st.Kind == "Matching" && t.StartsWith("Plugin.Matching.")) return s;
            if (st.Kind == "Blob" && t.StartsWith("Plugin.BlobDetect.")) return s;
            if (st.Kind == "CodeReader" && t.StartsWith("Plugin.CodeReader.")) return s;
            if (st.Kind == "Deformable" && t.StartsWith("Plugin.DeformableMatching.")) return s;

            // 容器节点：深入分支找（文件夹流程的算法步骤在 For 循环体里）
            if (s["Children"] is JArray children)
                foreach (var c in children.OfType<JObject>())
                    if (c["Steps"] is JArray inner)
                    {
                        var hit = PairFindStepDeep(inner, st);
                        if (hit != null) return hit;
                    }
        }
        return null;
    }

    /// <summary>深搜某个步骤名是否已存在（容器节点进 Children[].Steps）。</summary>
    static bool PairContainsStepName(JToken steps, string name)
    {
        if (steps == null) return false;

        foreach (var s in steps.OfType<JObject>())
        {
            if (s["StepName"]?.Value<string>() == name) return true;

            if (s["Children"] is JArray children)
                foreach (var c in children.OfType<JObject>())
                    if (c["Steps"] is JArray inner && PairContainsStepName(inner, name))
                        return true;
        }

        return false;
    }

    // ------------------------------------------------------------------
    //  判定规则：数值直接取算法步骤的既有阈值（判定口径与算法配置同源）
    // ------------------------------------------------------------------
    static void PairBuildRules(PairStation st, JObject algo, JArray rules, JObject judgeLinks)
    {
        var inputs = algo["InputValues"] as JObject;

        void AddRule(string seed, string name, string expected, string upStepId,
            string upName, string upPort)
        {
            rules.Add(new JObject
            {
                ["RuleId"] = G(seed).ToString("N"),
                ["Name"] = name,
                ["Enabled"] = true,
                ["GenerateResult"] = true,
                ["Mode"] = 0,                       // JudgeMode.Numeric
                ["Expected"] = expected,
                ["Tolerance"] = 0.0,
                ["IgnoreCase"] = false,
                ["IgnoreWhitespace"] = false,
                ["MaxEditDistance"] = 0,
                ["Confusables"] = "",
                ["SetSemantics"] = 0,               // SetSemantics.AllExpected（数值模式不用）
            });
            judgeLinks["实际_" + name] = LById(upStepId, upPort, upName);
        }

        switch (st.Kind)
        {
            case "Matching":
            case "Deformable":
            {
                double minScore = inputs?["MinScore"]?.Value<double>() ?? 0.5;
                AddRule("pair:rule:" + st.Short + ":score", "分数下限", "≥ " + minScore.ToString("0.##"),
                    PairStepId(algo), PairStepName(algo), "Score");
                AddRule("pair:rule:" + st.Short + ":count", "匹配数", "≥ 1",
                    PairStepId(algo), PairStepName(algo), "MatchCount");
                break;
            }
            case "Blob":
            {
                double maxDefects = inputs?["MaxDefectCount"]?.Value<double>() ?? 3.0;
                double maxArea = inputs?["MaxSingleArea"]?.Value<double>() ?? 1000.0;
                AddRule("pair:rule:" + st.Short + ":count", "缺陷数上限", "≤ " + maxDefects.ToString("0.##"),
                    PairStepId(algo), PairStepName(algo), "DefectCount");
                AddRule("pair:rule:" + st.Short + ":area", "最大面积上限", "≤ " + maxArea.ToString("0.##"),
                    PairStepId(algo), PairStepName(algo), "MaxArea");
                break;
            }
            case "CodeReader":
            {
                AddRule("pair:rule:" + st.Short + ":count", "码个数", "≥ 1",
                    PairStepId(algo), PairStepName(algo), "Count");
                break;
            }
        }
    }

    // ------------------------------------------------------------------
    //  步骤构造器（旧单值变量赋值 = test-suite.vms 已验证形状；判定表按 JudgeRule 模型字段）
    // ------------------------------------------------------------------
    static JObject PairJudgeStep(string seed, string name, JArray rules, JObject links, string desc)
        => new JObject
        {
            ["$type"] = "VisionMaster.Models.ActionStep, VM.Core",
            ["StepID"] = G(seed).ToString(),
            ["Icon"] = "\uf0ce",
            ["PluginName"] = "判定表",
            ["Category"] = "逻辑判断",
            ["PluginTypeName"] = P_JUDGE,
            ["StepName"] = name,
            ["Description"] = desc,
            ["SortId"] = 0,
            ["IsDisEnable"] = false,
            ["State"] = 0,
            ["InputValues"] = new JObject { ["Rules"] = rules },
            ["LinkedSources"] = links,
            ["OutputPortDefinitions"] = new JArray(),
        };

    static JObject PairVarAssign(string seed, string name, string varName, JObject valueLink)
    {
        var inputs = new JObject
        {
            ["Name"] = varName,
            ["CreateIfNotExists"] = false,
            ["Scope"] = "Global",
        };
        var links = new JObject();
        if (valueLink != null) links["Value"] = valueLink;
        return Action(seed, name, "变量操作", P_VAR, "变量赋值", "\uf044", inputs, links);
    }

    /// <summary>
    /// 图像赋值：把上游图像写进全局图像变量（组态 Hmi.ImageView 的取图入口）。
    /// 端口是 InputImage（必填）+ VariableName（必填）。
    /// </summary>
    static JObject PairImageAssign(string seed, string name, string varName, JObject imageLink)
        => Action(seed, name, "图像处理", P_IMGASSIGN, "图像赋值", "\uf03e",
            new JObject { ["VariableName"] = varName },
            new JObject { ["InputImage"] = imageLink });

    /// <summary>
    /// 结果上报（Plugin.ResultUpload）：把判定结果 POST 成 JSON 给 MES / 钉钉 / 企微。
    ///
    /// 两个层级各一份：
    ///   · 工位级（<paramref name="st"/> 非空）：仅 NG 上报，字段 = 工位名 / 判定 / 数值 / 说明 / 时间。
    ///     这是**整线模式**的输出——七个工位各起各的会话，谁 NG 谁自己报。
    ///   · 整件级（<paramref name="st"/> 为空）：每次上报，字段 = 总数 / 良品 / 不良 / 良率 / 时间。
    ///     整件串行（Main）跑完统计后报一条汇总。
    ///
    /// 【URL 一律留空】方案里不写死任何地址：空 URL + 不阻断（默认）时，
    /// 命中上报时机的那些件只留一条 WARN「未配置目标 URL，跳过本次上报」，
    /// 既不会把流程打红，又能在日志里指着用户去填地址。
    /// 也**不接 TriggerPort**：接它要挂全局变量，而 FlowCanvasChecks 的样例流程复跑不做
    /// 变量恢复，全局变量连线会被判「找不到全局变量」→ 整条流程编译失败。
    /// </summary>
    static JObject PairResultUpload(string name, PairStation st, bool pieceLevel)
    {
        JObject F(string fieldName, string source, string variable = "", string builtIn = "")
            => new JObject
            {
                ["Name"] = fieldName,
                ["Source"] = source,
                ["VariableName"] = variable,
                ["BuiltIn"] = string.IsNullOrEmpty(builtIn) ? "Timestamp" : builtIn,
                ["Enabled"] = true,
            };

        var fields = new JArray();
        string judgeField;
        string ngValue;

        if (pieceLevel)
        {
            fields.Add(F("total", "Variable", "TotalCount"));
            fields.Add(F("ok", "Variable", "OkCount"));
            fields.Add(F("ng", "Variable", "NgCount"));
            fields.Add(F("yield", "Variable", "YieldRate"));
            fields.Add(F("time", "BuiltIn", builtIn: "Timestamp"));
            judgeField = "ng";
            ngValue = "";
        }
        else
        {
            // 工位名没有"常量"来源，借内置字段 StepName（步骤名就叫「结果上报_<工位>」）
            fields.Add(F("station", "BuiltIn", builtIn: "StepName"));
            fields.Add(F("result", "Variable", st.Short + "_Result"));
            fields.Add(F("score", "Variable", st.ScoreVar));
            fields.Add(F("reason", "Variable", st.Short + "_Reason"));
            fields.Add(F("time", "BuiltIn", builtIn: "Timestamp"));
            judgeField = "result";
            // result 是 bool：OnlyNG 的判定走 TryAsBool 分支，"False" 即 NG（不区分大小写）
            ngValue = "False";
        }

        var config = new JObject
        {
            ["PayloadKind"] = "FieldTable",
            ["Url"] = "",
            ["TimeoutMs"] = 3000,
            ["Headers"] = new JArray(),
            ["Fields"] = fields,
            ["SendTiming"] = pieceLevel ? "EachRun" : "OnlyNG",
            ["JudgeFieldName"] = judgeField,
            ["NgValue"] = ngValue,
            ["AsyncMode"] = true,          // 异步队列，不占节拍
            ["RetryCount"] = 3,
            ["BlockOnFailure"] = false,    // 宁漏报不停线
            ["SuccessKeyword"] = "",
            ["SignSecret"] = "",
            ["SkipCertValidation"] = false,
            ["SpoolEnabled"] = true,       // 断网续传
            ["TimestampFormat"] = "yyyy-MM-dd HH:mm:ss.fff",
            ["TimestampUtc"] = false,
        };

        return Action("pair:upload:" + (pieceLevel ? "piece" : st.Short), name,
            "数据处理", P_UPLOAD, "结果上报", "\uf093", config);
    }

    /// <summary>按 PluginTypeName 前缀深度查找步骤（容器分支里也找）。</summary>
    static JObject PairFindStepByPlugin(JToken steps, string pluginPrefix)
    {
        foreach (var s in steps.OfType<JObject>())
        {
            var t = s["PluginTypeName"]?.Value<string>() ?? "";
            if (t.StartsWith(pluginPrefix, StringComparison.Ordinal)) return s;

            if (s["Children"] is JArray children)
                foreach (var c in children.OfType<JObject>())
                    if (c["Steps"] is JArray inner)
                    {
                        var hit = PairFindStepByPlugin(inner, pluginPrefix);
                        if (hit != null) return hit;
                    }
        }
        return null;
    }

    /// <summary>连线到**已存在**步骤的真实 StepID（与 L() 的区别：L 是"种子→G()"，这里原样使用）</summary>
    static JObject LById(string stepId, string port, string srcStepName)
        => new()
        {
            ["Kind"] = 1,
            ["TargetStepId"] = stepId,
            ["TargetPortName"] = port,
            ["TargetVariableId"] = Guid.Empty.ToString(),
            ["DisplayAddress"] = srcStepName + "." + port,
        };

    static JObject PairRunFlow(string seed, string name, string flowName, int timeoutMs)
        => Action(seed, name, "流程控制", P_RUNFLOW, "调用流程", "\uf04b", new JObject
        {
            ["FlowName"] = flowName,
            ["TimeoutMs"] = timeoutMs,
        });

    static JObject PairVarSet(string seed, string name, string varName, JToken constant)
        => PairVarAssign(seed, name, varName, null).AlsoSet("InputValues", "Value", constant);

    // JObject 扩展小工具：在 InputValues 里补一个常量键
    static JObject AlsoSet(this JObject step, string container, string key, JToken value)
    {
        ((JObject)step[container])[key] = value;
        return step;
    }

    // ------------------------------------------------------------------
    //  Home / Main / End
    // ------------------------------------------------------------------
    static void PairRebuildHome(JArray flows)
    {
        var flow = PairFlow(flows, "Home");
        var steps = new JArray
        {
            PairVarSet("pair:home:ready", "置系统就绪", "SystemReady", new JValue(true)),
            PairRunFlow("pair:home:ncc", "预热NCC工位", "模板匹配_NCC散焦图", 300000),
            PairVarSet("pair:home:done", "置初始化完成标记", "Home_Done", new JValue(true)),
        };
        flow["Steps"] = steps;
        flow["Description"] = "回原 / 回零：置系统就绪并预热首个工位（无运动卡的样例方案）";
        Console.WriteLine("  Home: 3 步（就绪 + 预热）");
    }

    static void PairRebuildEnd(JArray flows)
    {
        var flow = PairFlow(flows, "End");
        flow["Steps"] = new JArray
        {
            PairVarSet("pair:end:ready", "清系统就绪", "SystemReady", new JValue(false)),
        };
        flow["Description"] = "收尾：清系统就绪位";
        Console.WriteLine("  End: 1 步");
    }

    static void PairRebuildMain(JArray flows)
    {
        var flow = PairFlow(flows, "Main");

        // 七工位串行调用（PCB 121 张给 5 分钟；其余 3 分钟）
        var steps = new JArray();
        foreach (var st in PairStations)
        {
            int timeout = st.Short == "Pcb" ? 300000 : 180000;
            steps.Add(PairRunFlow("pair:main:call:" + st.Short, "调用工位_" + st.Short, st.FlowName, timeout));
        }

        steps.Add(PairStatScript());
        // 整件级结果上报：统计脚本写完全局变量之后报一条汇总（总数/良品/不良/良率/时间）。
        // URL 留空 → 只留一条 WARN，不影响流程（详见 PairResultUpload 的注释）。
        steps.Add(PairResultUpload("结果上报_整件", null, pieceLevel: true));
        // CSV 插件步骤：真机实测未被调度（零日志零落盘），账本已改由统计脚本直接写盘。
        // 保留配置但停用，用户若要改回插件口径，右键「启用」即可。
        var csvStep = PairCsvRecord();
        csvStep["IsDisEnable"] = true;
        steps.Add(csvStep);

        flow["Steps"] = steps;
        flow["Description"] = "整件串行：七工位串行检测 → 统计（总数/OK/NG/良率）→ 结果上报 → CSV 账本";
        Console.WriteLine("  Main: " + steps.Count + " 步（七工位 + 统计 + 账本）");
    }

    static JObject PairFlow(JArray flows, string name)
        => (JObject)flows.FirstOrDefault(f => f["FlowName"]?.Value<string>() == name);

    /// <summary>统计脚本：读七工位全局结果 → 计数与良率 → 写回全局变量</summary>
    static JObject PairStatScript()
    {
        var shared = new JArray();
        void S(string name, string type, bool write)
        {
            shared.Add(new JObject
            {
                ["VariableId"] = G("var:" + name).ToString(),
                ["Name"] = name,
                ["Alias"] = name,
                ["Access"] = write ? 1 : 0,     // ScriptSharedAccess: 0=Read 1=ReadWrite
                ["Remark"] = write ? "统计输出" : "工位判定结果",
                ["TypeName"] = type,
                ["IsSystem"] = false,
            });
        }
        foreach (var st in PairStations) S(st.Short + "_Result", "System.Boolean", false);
        S("TotalCount", "System.Int32", true);
        S("OkCount", "System.Int32", true);
        S("NgCount", "System.Int32", true);
        S("YieldRate", "System.Double", true);
        S("CurrentResult", "System.Boolean", true);
        // 账本根目录：脚本读它拼 History\ 路径（只读声明）
        S("SolutionRoot", "System.String", false);

        var script = new StringBuilder();
        script.AppendLine("// ============================================================");
        script.AppendLine("// 主流程统计脚本（每轮七工位全部跑完后执行一次）");
        script.AppendLine("// 数据来源：各工位流程末尾的「变量赋值」把判定写进全局变量；");
        script.AppendLine("// 本脚本汇总累计检测数 / 良品数 / 不良数，并计算实时良率（%）。");
        script.AppendLine("// ============================================================");
        script.AppendLine("int total = Context.GetGlobal<int>(\"TotalCount\");");
        script.AppendLine("int ok    = Context.GetGlobal<int>(\"OkCount\");");
        script.AppendLine("int ng    = Context.GetGlobal<int>(\"NgCount\");");
        script.AppendLine("");
        script.AppendLine("// 七工位判定结果（true = 该工位通过）");
        foreach (var st in PairStations)
            script.AppendLine($"bool r_{st.Short} = Context.GetGlobal<bool>(\"{st.Short}_Result\");");
        script.AppendLine("");
        script.AppendLine("// 全部通过才算本件 OK");
        script.AppendLine("bool allOk = " + string.Join(" && ", PairStations.Select(s => "r_" + s.Short)) + ";");
        script.AppendLine("");
        script.AppendLine("total++;");
        script.AppendLine("if (allOk) ok++; else ng++;");
        script.AppendLine("double yield = total > 0 ? ok * 100.0 / total : 100.0;");
        script.AppendLine("");
        script.AppendLine("Context.SetGlobal(\"TotalCount\", total);");
        script.AppendLine("Context.SetGlobal(\"OkCount\", ok);");
        script.AppendLine("Context.SetGlobal(\"NgCount\", ng);");
        script.AppendLine("Context.SetGlobal(\"YieldRate\", yield);");
        script.AppendLine("Context.SetGlobal(\"CurrentResult\", allOk);");
        script.AppendLine("");
        script.AppendLine("// ---- 生产账本（CSV）：直接落方案 History\\，供「运行日志」页历史表格与报表读取 ----");
        script.AppendLine("// 为什么不靠「CSV记录」插件：该步骤在真机运行链中未被调度（已实测零日志零落盘），");
        script.AppendLine("// 而账本是「输出」环节的硬要求，故在此直接写盘，保证输出闭环。");
        script.AppendLine("try");
        script.AppendLine("{");
        script.AppendLine("    string root = Context.GetGlobal<string>(\"SolutionRoot\");");
        script.AppendLine("    Context.Info(\"账本根目录=\" + root);");
        script.AppendLine("    string dir  = System.IO.Path.Combine(root, \"History\", \"Main.CSV记录\", DateTime.Now.ToString(\"yyyy-MM\"));");
        script.AppendLine("    System.IO.Directory.CreateDirectory(dir);");
        script.AppendLine("    string file = System.IO.Path.Combine(dir, DateTime.Now.ToString(\"yyyy-MM-dd\") + \".csv\");");
        script.AppendLine("    Context.Info(\"账本文件=\" + file);");
        script.AppendLine("    bool needHeader = !System.IO.File.Exists(file) || new System.IO.FileInfo(file).Length == 0;");
        script.AppendLine("    var sb = new System.Text.StringBuilder();");
        script.AppendLine("    if (needHeader)");
        script.AppendLine("        sb.AppendLine(\"时间,序号,综合结果,NCC,PCB,标签,条码,二维码,局部形变,平面形变,良率\");");
        script.AppendLine("    sb.AppendLine(string.Join(\",\", new object[] {");
        script.AppendLine("        DateTime.Now.ToString(\"yyyy-MM-dd HH:mm:ss.fff\"), total, allOk,");
        script.AppendLine("        r_Ncc, r_Pcb, r_Label, r_Code128, r_Qr, r_DefLocal, r_DefPlanar,");
        script.AppendLine("        yield.ToString(\"F1\", System.Globalization.CultureInfo.InvariantCulture) }));");
        script.AppendLine("    System.IO.File.AppendAllText(file, sb.ToString(), System.Text.Encoding.UTF8);");
        script.AppendLine("}");
        script.AppendLine("catch (System.Exception ex)");
        script.AppendLine("{");
        script.AppendLine("    Context.Info(\"账本写入失败：\" + ex.Message);");
        script.AppendLine("}");
        script.AppendLine("Context.Info($\"统计完成：总数={total}，OK={ok}，NG={ng}，良率={yield:F1}%\");");

        return Action("pair:main:stat", "统计与良率", "逻辑控制", P_CSCRIPT, "C#脚本", "\uf121", new JObject
        {
            ["ScriptText"] = script.ToString(),
            ["InputVars"] = new JArray(),
            ["OutputVars"] = new JArray(),
            ["SharedVars"] = shared,
        });
    }

    /// <summary>CSV 账本：喂「运行日志」页的历史表格与「结果统计」页的报表</summary>
    static JObject PairCsvRecord()
    {
        // 目录模板用**方案根绝对路径**（指南 §1.1 的既定补救）：
        // RecordingHub 展开模板后按宿主进程 CWD 解析相对目录，而运行态的数据表格/报表
        // 历史源是按 <方案根>\History 锚定的（ScadaRuntimeHost.cs:895）——两边不一致会让
        // 账本落进 bin\History 而日志页查不到。本方案的采集路径本就是绝对路径（文件夹模式
        // 插件边界），这里保持同一口径；整包搬迁时本条需随方案根同步修改。
        var solutionRoot = Path.GetDirectoryName(PairSolutionPath)!;   // <方案根>\（.vms 所在目录）
        var csvDir = Path.Combine(solutionRoot, "History", "{StepName}", "{yyyy-MM}") + Path.DirectorySeparatorChar;
        return new JObject
        {
        ["$type"] = "VisionMaster.Models.ActionStep, VM.Core",
        ["StepID"] = G("pair:main:csv").ToString(),
        ["Icon"] = "\uf15c",
        ["PluginName"] = "CSV记录",
        ["Category"] = "数据处理",
        ["PluginTypeName"] = P_CSV,
        ["StepName"] = "CSV记录",
        ["Description"] = "生产账本：落方案 History\\，供历史表格与报表读取",
        ["SortId"] = 0,
        ["IsDisEnable"] = false,
        ["State"] = 0,
        ["InputValues"] = new JObject
        {
            ["DirectoryTemplate"] = csvDir,
            ["FileNamePattern"] = "{yyyy-MM-dd}.csv",
            ["WriteHeader"] = true,
            ["KeepDays"] = 30,
            ["BlockOnFailure"] = false,
            ["FlushEveryRow"] = false,
            ["Columns"] = new JArray
            {
                Col("时间", "BuiltIn", "", "Timestamp"),
                Col("序号", "BuiltIn", "", "Sequence"),
                Col("综合结果", "Variable", "CurrentResult", "Timestamp"),
                Col("NCC", "Variable", "Ncc_Result", "Timestamp"),
                Col("PCB", "Variable", "Pcb_Result", "Timestamp"),
                Col("标签", "Variable", "Label_Result", "Timestamp"),
                Col("条码", "Variable", "Code128_Result", "Timestamp"),
                Col("二维码", "Variable", "Qr_Result", "Timestamp"),
                Col("局部形变", "Variable", "DefLocal_Result", "Timestamp"),
                Col("平面形变", "Variable", "DefPlanar_Result", "Timestamp"),
                Col("良率", "Variable", "YieldRate", "Timestamp"),
            },
            ["ImageSaveMode"] = "Off",
            ["ResultColumn"] = "综合结果",
            ["NgValue"] = "False",
            ["ImageDirName"] = "img",
            ["ImageNameTemplate"] = "{序号}_{结果}",
            ["ImageFormat"] = "png",
        },
        ["LinkedSources"] = new JObject(),
        ["OutputPortDefinitions"] = new JArray(),
        };
    }

    // ==================================================================
    //  四条扩展工位流程（2026-10-28）
    //
    //  与七条工位同一口径：Role=0（普通工位）、InvokeType=32（子程序位）、启用；
    //  末尾都是「判定表 → 结果输出 → 变量赋值」，判定由「结果输出」显式声明给宿主
    //  （FlowVerdictResolver 的 ResultStep 分支），底栏 / System.Flow.* 才拿得到 OK/NG。
    //
    //  与七条工位的差异（刻意）：
    //   · 不进 Main、不置整线位（128）：Main 仍是七工位串行、账本列不变；
    //     这四条单独运行（流程栏右键运行 / 流程管理）。
    //   · 图像不写组态工位卡：自动运行页是 4 列 × 2 行的七卡布局，加卡要重排版面
    //     （属 R132 验收范围，另开一批）。
    //
    //  素材（均已复制进本方案 Image\，步骤里是绝对路径——文件夹模式的既定边界）：
    //   · geometry\angle90.png   合成图（横条 + 竖条正交，真值 90°）
    //   · quality\01_ok.png / 02_ng.png   pcb_focus 序列的对焦最好帧 / 最差帧
    //   · diff\01_same.png / 02_diff.png  与基准同帧 / 与基准相差 28% 像元的帧
    //   · color\cable2.png       HALCON 线束图（10 芯，配方见下）
    // ==================================================================

    const string P_ROI = "Plugin.CreateRoi.CreateRoiPlugin, Plugin.CreateRoi" + S;
    const string P_GEOMFIND = "Plugin.GeometryFinder.GeometryFinderPlugin, Plugin.GeometryFinder" + S;
    const string P_GEOMREL = "Plugin.GeometryRelation.GeometryRelationPlugin, Plugin.GeometryRelation" + S;
    const string P_IMAGEDIFF = "Plugin.ImageDiff.ImageDiffPlugin, Plugin.ImageDiff" + S;
    const string P_IMGQUALITY = "Plugin.ImageQuality.ImageQualityPlugin, Plugin.ImageQuality" + S;
    const string P_COLORCHECK = "Plugin.ColorCheck.ColorCheckPlugin, Plugin.ColorCheck" + S;
    const string P_RESULTOUT = "VisionMaster.Plugins.Util.ResultOutputPlugin, Plugin.Util" + S;

    /// <summary>判定表规则（Mode：0=数值 1=文本 2=集合 3=正则 4=布尔 5=计数 6=非空）</summary>
    static JObject PairRule(string seed, string name, int mode, string expected)
        => new JObject
        {
            ["RuleId"] = G(seed).ToString("N"),
            ["Name"] = name,
            ["Enabled"] = true,
            ["GenerateResult"] = true,
            ["Mode"] = mode,
            ["Expected"] = expected,
            ["Tolerance"] = 0.0,
            ["IgnoreCase"] = false,
            ["IgnoreWhitespace"] = false,
            ["MaxEditDistance"] = 0,
            ["Confusables"] = "",
            ["SetSemantics"] = 0,
        };

    /// <summary>判定表实际值连线：键名必须是插件算出的端口名（"实际_" + Sanitize(规则名)）</summary>
    static void PairRuleLink(JObject links, string ruleName, JObject sourceLink)
        => links["实际_" + ruleName] = sourceLink;

    /// <summary>结果输出（Plugin.Util.ResultOutputPlugin）：声明本轮 OK/NG，NG 时步骤仍 Success</summary>
    static JObject PairResultOutput(string seed, string name, JObject isOkLink, JObject reasonLink)
    {
        var links = new JObject { ["IsOk"] = isOkLink };
        if (reasonLink != null) links["Reason"] = reasonLink;
        return Action(seed, name, "流程控制", P_RESULTOUT, "结果输出", "\uf00c", new JObject(), links);
    }

    /// <summary>单图采集（指定图像；不投宿主图像窗口，免得和七工位的 1~7 号窗抢位置）</summary>
    static JObject PairAcqFile(string seed, string name, string absPath, string desc)
    {
        var step = Action(seed, name, "常用工具", P_IMAGE, "图像采集", "\uf1c5", new JObject
        {
            ["Mode"] = 0,
            ["FilePath"] = absPath,
            ["ImageAssetId"] = "",
            ["FolderPath"] = "",
            ["FileIndex"] = 0,
            ["DisplayViewIndex"] = 0,
        });
        step["Description"] = desc;
        return step;
    }

    /// <summary>文件夹采集（按索引取图；FileIndex 接 For.Index）</summary>
    static JObject PairAcqFolder(string seed, string name, string folder, string forSeed, string forName)
    {
        var step = Action(seed, name, "常用工具", P_IMAGE, "图像采集", "\uf1c5",
            new JObject
            {
                ["Mode"] = 1,
                ["FilePath"] = "",
                ["ImageAssetId"] = "",
                ["FolderPath"] = folder,
                ["FileIndex"] = 0,
                ["DisplayViewIndex"] = 0,
            },
            new JObject { ["FileIndex"] = L(forSeed, "Index", forName) });
        step["Description"] = "按文件索引遍历样例目录：" + folder;
        return step;
    }

    /// <summary>For 循环容器（单个"循环体"分支，与既有七条工位同形）</summary>
    static JObject PairFor(string seed, string name, int count, JArray body)
        => new JObject
        {
            ["$type"] = "VisionMaster.Models.ForStep, VM.Core",
            ["DefaultLoopCount"] = count,
            ["Children"] = new JArray
            {
                new JObject
                {
                    ["Steps"] = body,
                    ["BranchType"] = 0,
                    ["StepName"] = "循环体",
                    ["Expression"] = "",
                    ["DisplayName"] = "循环体",
                },
            },
            ["StepID"] = G(seed).ToString(),
            ["Icon"] = "\uf01e",
            ["PluginName"] = "For",
            ["Category"] = "逻辑控制",
            ["PluginTypeName"] = "BuiltIn_For",
            ["StepName"] = name,
            ["Description"] = "循环 " + count + " 次",
            ["SortId"] = 0,
            ["IsDisEnable"] = false,
            ["State"] = 0,
            ["InputValues"] = new JObject(),
            ["LinkedSources"] = new JObject(),
            ["OutputPortDefinitions"] = new JArray(),
        };

    /// <summary>
    /// If / Else 条件容器。
    ///
    /// 条件变量的连线键是**局部变量 Id 的字符串**（FlowCompiler 的 HasLinkedSource 与
    /// CompiledIfNode 都按 Guid 解析；按名字写只作旧数据兼容）。表达式里用的是变量名。
    /// </summary>
    static JObject PairIf(string seed, string name, string varName, string dataType, JObject sourceLink,
        string ifBranchName, JArray ifSteps, string elseBranchName, JArray elseSteps)
    {
        var varId = G("pair:condvar:" + seed);
        return new JObject
        {
            ["$type"] = "VisionMaster.Models.ConditionStep, VM.Core",
            ["LocalVariables"] = new JArray
            {
                new JObject
                {
                    ["Id"] = varId.ToString(),
                    ["Name"] = varName,
                    ["DataTypeName"] = dataType,
                },
            },
            ["RuntimeVariableRefs"] = new JArray(),
            ["Children"] = new JArray
            {
                new JObject
                {
                    ["Steps"] = ifSteps,
                    ["BranchType"] = 1,
                    ["StepName"] = ifBranchName,
                    ["Expression"] = varName,
                    ["DisplayName"] = ifBranchName,
                },
                new JObject
                {
                    ["Steps"] = elseSteps,
                    ["BranchType"] = 2,
                    ["StepName"] = elseBranchName,
                    ["Expression"] = "",
                    ["DisplayName"] = elseBranchName,
                },
            },
            ["StepID"] = G(seed).ToString(),
            ["Icon"] = "\uf0e8",
            ["PluginName"] = "If",
            ["Category"] = "逻辑判断",
            ["PluginTypeName"] = "BuiltIn_If",
            ["StepName"] = name,
            ["Description"] = "条件分支：" + varName,
            ["SortId"] = 0,
            ["IsDisEnable"] = false,
            ["State"] = 0,
            ["InputValues"] = new JObject(),
            ["LinkedSources"] = new JObject { [varId.ToString()] = sourceLink },
            ["OutputPortDefinitions"] = new JArray(),
        };

    /// <summary>
    /// ROI 的动态输出端口快照（R18/§6）：插件运行期按 RoiList 重建，
    /// 这里把同一份定义写进 .vms，编译器和绑定界面才看得到 Domain_xxx。
    /// 只用 Domain_xxx（带 domain 的原图）：坐标系不变，下游拟合出的仍是全局坐标。
    /// </summary>
    static JArray PairRoiPorts(string[] names)
    {
        var arr = new JArray();
        foreach (var n in names)
        {
            arr.Add(new JObject { ["Name"] = "Crop_" + n, ["DataTypeName"] = T_HIMG, ["Description"] = "ROI '" + n + "' 的裁剪图" });
            arr.Add(new JObject { ["Name"] = "Domain_" + n, ["DataTypeName"] = T_HIMG, ["Description"] = "ROI '" + n + "' 带 domain 的原图（全局坐标）" });
            arr.Add(new JObject { ["Name"] = "OffsetRow_" + n, ["DataTypeName"] = T_DBL, ["Description"] = "裁剪原点行" });
            arr.Add(new JObject { ["Name"] = "OffsetCol_" + n, ["DataTypeName"] = T_DBL, ["Description"] = "裁剪原点列" });
        }
        return arr;
    }

    static JObject PairNewFlow(string name, string desc, JArray steps)
        => new JObject
        {
            ["FlowID"] = G("flow:" + name).ToString("N"),
            ["FlowName"] = name,
            ["Description"] = desc,
            ["Version"] = 0,
            ["IsEnabled"] = true,
            ["Role"] = 0,
            ["InvokeType"] = 32,
            ["TimerIntervalMs"] = 0,
            ["StepsEncrypted"] = false,
            ["TriggerMode"] = 0,
            ["Layout"] = new JObject { ["Nodes"] = new JObject(), ["Count"] = 0 },
            ["Steps"] = steps,
        };

    /// <summary>追加四条扩展流程（幂等：流程已存在就跳过，不覆盖手工改动）</summary>
    static void PairBuildExtraFlows(JArray flows)
    {
        var root = Path.GetDirectoryName(PairSolutionPath)!;
        string Abs(string rel) => Path.Combine(root, rel);

        bool Exists(string name) => flows.Any(f => f["FlowName"]?.Value<string>() == name);

        // ---------- ① 几何计算_关系：ROI 圈出横条/竖条 → 两次几何查找 → 夹角 → 判定 ----------
        {
            const string flowName = "几何计算_关系";
            if (Exists(flowName)) Console.WriteLine("  " + flowName + ": 已存在，跳过");
            else
            {
                const string sAcq = "pair:geom:acq", sRoi = "pair:geom:roi";
                const string sFa = "pair:geom:findA", sFb = "pair:geom:findB";
                const string sRel = "pair:geom:rel", sJudge = "pair:geom:judge", sOut = "pair:geom:out";

                var roiList = new JArray
                {
                    new JObject
                    {
                        ["Name"] = "横条",
                        ["ShapeType"] = "Rectangle",
                        ["Params"] = new JArray { 120.0, 235.0, 0.0, 200.0, 15.0 },
                    },
                    new JObject
                    {
                        ["Name"] = "竖条",
                        ["ShapeType"] = "Rectangle",
                        ["Params"] = new JArray { 237.0, 440.0, 1.5708, 200.0, 15.0 },
                    },
                };

                var roiStep = Action(sRoi, "划分横条与竖条", "常用工具", P_ROI, "ROI", "\uf1c5",
                    new JObject
                    {
                        ["RoiList"] = roiList,
                        ["MaskInvert"] = false,
                        ["DisplayViewIndex"] = 0,
                    },
                    new JObject { ["SrcImage"] = L(sAcq, "OutputImage", "读取角度测试图") });
                roiStep["Description"] = "两个矩形 ROI：横条 / 竖条（Domain_xxx 输出保留全局坐标）";
                roiStep["OutputPortDefinitions"] = PairRoiPorts(new[] { "横条", "竖条" });

                var judgeLinks = new JObject();
                var rules = new JArray();
                rules.Add(PairRule("pair:rule:geom:angle", "夹角90度", 0, "89 ~ 91"));
                PairRuleLink(judgeLinks, "夹角90度", L(sRel, "Value", "夹角计算"));
                rules.Add(PairRule("pair:rule:geom:foundA", "横线找到", 4, "true"));
                PairRuleLink(judgeLinks, "横线找到", L(sFa, "IsFound", "拟合横线"));
                rules.Add(PairRule("pair:rule:geom:foundB", "竖线找到", 4, "true"));
                PairRuleLink(judgeLinks, "竖线找到", L(sFb, "IsFound", "拟合竖线"));

                var steps = new JArray
                {
                    PairAcqFile(sAcq, "读取角度测试图", Abs(@"Image\geometry\angle90.png"),
                        "合成测试图：横条 + 竖条正交，几何真值 90°"),
                    roiStep,
                    PairGeomFind(sFa, "拟合横线", "Line", L(sRoi, "Domain_横条", "划分横条与竖条")),
                    PairGeomFind(sFb, "拟合竖线", "Line", L(sRoi, "Domain_竖条", "划分横条与竖条")),
                    PairGeomRelation(sRel, "夹角计算",
                        L(sFa, "Geometry", "拟合横线"), L(sFb, "Geometry", "拟合竖线")),
                    PairJudgeStep(sJudge, "结果判定_Geom", rules, judgeLinks,
                        "横线/竖线都找到且夹角落在 90±1°"),
                    PairResultOutput(sOut, "结果输出_Geom",
                        L(sJudge, "IsOk", "结果判定_Geom"), L(sJudge, "Reason", "结果判定_Geom")),
                    PairVarAssign("pair:geom:res", "写工位判定_Geom", "Geom_Result", L(sJudge, "IsOk", "结果判定_Geom")),
                    PairVarAssign("pair:geom:val", "写工位数值_Geom", "Geom_Angle", L(sRel, "Value", "夹角计算")),
                    PairVarAssign("pair:geom:rsn", "写工位说明_Geom", "Geom_Reason", L(sJudge, "Reason", "结果判定_Geom")),
                };

                flows.Add(PairNewFlow(flowName,
                    "几何计算（夹角）：ROI 圈横条/竖条 → 几何查找各拟合一条直线 → 几何计算求夹角（真值 90°）",
                    steps));
                Console.WriteLine("  " + flowName + ": 新建 " + steps.Count + " 步");
            }
        }

        // ---------- ② 前置质量门：质量检查 → If(合格) 走下游算法 / Else 直接判 NG ----------
        {
            const string flowName = "前置质量门";
            if (Exists(flowName)) Console.WriteLine("  " + flowName + ": 已存在，跳过");
            else
            {
                const string sFor = "pair:q:for", sAcq = "pair:q:acq", sIq = "pair:q:iq", sIf = "pair:q:if";
                const string sFind = "pair:q:find", sJudge = "pair:q:judge", sOut = "pair:q:out";
                const string sJudgeNg = "pair:q:judgeNg", sOutNg = "pair:q:outNg";

                // 合格分支：下游算法（几何查找）+ 判定 + 结果输出 + 变量
                var okLinks = new JObject();
                var okRules = new JArray();
                okRules.Add(PairRule("pair:rule:q:found", "轮廓找到", 4, "true"));
                PairRuleLink(okLinks, "轮廓找到", L(sFind, "IsFound", "下游算法_找轮廓"));

                var okSteps = new JArray
                {
                    PairGeomFind(sFind, "下游算法_找轮廓", "Line", L(sAcq, "OutputImage", "读取待检图")),
                    PairJudgeStep(sJudge, "结果判定_Quality", okRules, okLinks, "成像合格后才跑下游算法；下游须找到轮廓"),
                    PairResultOutput(sOut, "结果输出_Quality",
                        L(sJudge, "IsOk", "结果判定_Quality"), L(sJudge, "Reason", "结果判定_Quality")),
                    PairVarAssign("pair:q:res", "写工位判定_Quality", "Quality_Result", L(sJudge, "IsOk", "结果判定_Quality")),
                    PairVarAssign("pair:q:val", "写工位数值_Quality", "Quality_Focus", L(sIq, "FocusScore", "成像质量检查")),
                    PairVarAssign("pair:q:rsn", "写工位说明_Quality", "Quality_Reason", L(sJudge, "Reason", "结果判定_Quality")),
                };

                // 不合格分支：把"成像不合格"如实判成 NG（判定表对 IsUsable 期望 true，实际 false → NG）
                var ngLinks = new JObject();
                var ngRules = new JArray();
                ngRules.Add(PairRule("pair:rule:q:usable", "成像合格", 4, "true"));
                PairRuleLink(ngLinks, "成像合格", L(sIq, "IsUsable", "成像质量检查"));

                var ngSteps = new JArray
                {
                    PairJudgeStep(sJudgeNg, "质量门判定_Quality", ngRules, ngLinks,
                        "质量门不合格：直接判 NG，不再跑下游算法"),
                    PairResultOutput(sOutNg, "结果输出_QualityNg",
                        L(sJudgeNg, "IsOk", "质量门判定_Quality"), L(sJudgeNg, "Reason", "质量门判定_Quality")),
                    PairVarAssign("pair:q:res2", "写工位判定_QualityNg", "Quality_Result", L(sJudgeNg, "IsOk", "质量门判定_Quality")),
                    PairVarAssign("pair:q:val2", "写工位数值_QualityNg", "Quality_Focus", L(sIq, "FocusScore", "成像质量检查")),
                    PairVarAssign("pair:q:rsn2", "写工位说明_QualityNg", "Quality_Reason", L(sJudgeNg, "Reason", "质量门判定_Quality")),
                };

                var body = new JArray
                {
                    PairAcqFolder(sAcq, "读取待检图", Abs(@"Image\quality"), sFor, "遍历待检图"),
                    PairImageQuality(sIq, "成像质量检查", L(sAcq, "OutputImage", "读取待检图")),
                    PairIf(sIf, "质量门", "Usable", T_BOOL, L(sIq, "IsUsable", "成像质量检查"),
                        "成像合格", okSteps, "成像不合格", ngSteps),
                    Action("pair:q:delay", "节拍延时_Quality", "常用工具", P_DELAY, "延时", "\uf017",
                        new JObject { ["Delay"] = 200 }),
                };

                var steps = new JArray { PairFor(sFor, "遍历待检图", 2, body) };

                flows.Add(PairNewFlow(flowName,
                    "前置质量门：对焦分低于下限直接判 NG 并跳过下游算法；合格才继续（2 张样图：合格 / 不合格）",
                    steps));
                Console.WriteLine("  " + flowName + ": 新建 1 个 For（循环体 " + body.Count + " 步）");
            }
        }

        // ---------- ③ 差分_基准比对：基准图固定 + 被测图遍历 → 逐像元差分 → 判定 ----------
        {
            const string flowName = "差分_基准比对";
            if (Exists(flowName)) Console.WriteLine("  " + flowName + ": 已存在，跳过");
            else
            {
                const string sRef = "pair:d:ref", sFor = "pair:d:for", sTest = "pair:d:test";
                const string sDiff = "pair:d:diff", sJudge = "pair:d:judge", sOut = "pair:d:out";

                var judgeLinks = new JObject();
                var rules = new JArray();
                rules.Add(PairRule("pair:rule:d:area", "差异面积上限", 0, "≤ 2000"));
                PairRuleLink(judgeLinks, "差异面积上限", L(sDiff, "DiffArea", "逐像元差分"));
                rules.Add(PairRule("pair:rule:d:hasdiff", "无差异", 4, "false"));
                PairRuleLink(judgeLinks, "无差异", L(sDiff, "HasDiff", "逐像元差分"));

                var body = new JArray
                {
                    PairAcqFolder(sTest, "读取被测图", Abs(@"Image\diff"), sFor, "遍历被测图"),
                    PairImageDiff(sDiff, "逐像元差分",
                        L(sRef, "OutputImage", "读取基准图"), L(sTest, "OutputImage", "读取被测图")),
                    PairJudgeStep(sJudge, "结果判定_Diff", rules, judgeLinks,
                        "差异总面积 ≤ 2000 px 且未判有差异（阈值按基准帧实测：同帧 2px / 换帧 101047px）"),
                    PairResultOutput(sOut, "结果输出_Diff",
                        L(sJudge, "IsOk", "结果判定_Diff"), L(sJudge, "Reason", "结果判定_Diff")),
                    PairVarAssign("pair:d:res", "写工位判定_Diff", "Diff_Result", L(sJudge, "IsOk", "结果判定_Diff")),
                    PairVarAssign("pair:d:val", "写工位数值_Diff", "Diff_Area", L(sDiff, "DiffArea", "逐像元差分")),
                    PairVarAssign("pair:d:rsn", "写工位说明_Diff", "Diff_Reason", L(sJudge, "Reason", "结果判定_Diff")),
                    Action("pair:d:delay", "节拍延时_Diff", "常用工具", P_DELAY, "延时", "\uf017",
                        new JObject { ["Delay"] = 200 }),
                };

                var steps = new JArray
                {
                    PairAcqFile(sRef, "读取基准图", Abs(@"Image\diff_ref\ref_060.png"),
                        "基准图（金样）：pcb_focus 对焦最好的那一帧"),
                    PairFor(sFor, "遍历被测图", 2, body),
                };

                flows.Add(PairNewFlow(flowName,
                    "基准比对：基准图固定，被测图逐张差分；差异面积超限即 NG（样图：同帧 OK / 换帧 NG）",
                    steps));
                Console.WriteLine("  " + flowName + ": 新建 2 步（基准采集 + For）");
            }
        }

        // ---------- ④ 颜色检查_线序：一维颜色序列 → 配方比对 ----------
        {
            const string flowName = "颜色检查_线序";
            if (Exists(flowName)) Console.WriteLine("  " + flowName + ": 已存在，跳过");
            else
            {
                const string sAcq = "pair:c:acq", sChk = "pair:c:chk";
                const string sJudge = "pair:c:judge", sOut = "pair:c:out";

                var judgeLinks = new JObject();
                var rules = new JArray();
                rules.Add(PairRule("pair:rule:c:result", "线序正确", 4, "true"));
                PairRuleLink(judgeLinks, "线序正确", L(sChk, "Result", "线序检查"));
                rules.Add(PairRule("pair:rule:c:count", "根数", 0, "10 ~ 10"));
                PairRuleLink(judgeLinks, "根数", L(sChk, "Count", "线序检查"));

                var steps = new JArray
                {
                    PairAcqFile(sAcq, "读取线束图", Abs(@"Image\color\cable2.png"),
                        "HALCON 线束图（10 芯，自上而下：黑 白 灰 紫 蓝 绿 黄 红 玫红 棕）"),
                    PairColorCheck(sChk, "线序检查", L(sAcq, "OutputImage", "读取线束图")),
                    PairJudgeStep(sJudge, "结果判定_Color", rules, judgeLinks,
                        "颜色序列与配方逐位一致且根数 = 10"),
                    PairResultOutput(sOut, "结果输出_Color",
                        L(sJudge, "IsOk", "结果判定_Color"), L(sJudge, "Reason", "结果判定_Color")),
                    PairVarAssign("pair:c:res", "写工位判定_Color", "Color_Result", L(sJudge, "IsOk", "结果判定_Color")),
                    PairVarAssign("pair:c:val", "写工位数值_Color", "Color_Count", L(sChk, "Count", "线序检查")),
                    PairVarAssign("pair:c:rsn", "写工位说明_Color", "Color_Reason", L(sJudge, "Reason", "结果判定_Color")),
                };

                flows.Add(PairNewFlow(flowName,
                    "颜色序列检查：垂直于线束取一维颜色序列，与「10 芯」配方逐位比对",
                    steps));
                Console.WriteLine("  " + flowName + ": 新建 " + steps.Count + " 步");
            }
        }
    }

    /// <summary>几何查找（Kind：Line / Circle / Ellipse）</summary>
    static JObject PairGeomFind(string seed, string name, string kind, JObject srcLink)
    {
        var step = Action(seed, name, "几何工具", P_GEOMFIND, "几何查找", "\uf542", new JObject
        {
            ["Kind"] = kind,
            ["Sigma"] = 1.0,
            ["LowThreshold"] = 20.0,
            ["HighThreshold"] = 40.0,
            ["MinContourLength"] = 30.0,
            ["FitAlgorithm"] = "Tukey",
            ["FailOnNotFound"] = true,
            ["DisplayViewIndex"] = 0,
        }, new JObject { ["SrcImage"] = srcLink });
        step["Description"] = "亚像素找边并拟合" + (kind == "Line" ? "直线" : kind);
        return step;
    }

    /// <summary>
    /// 几何计算（夹角模式）。公差是**幅值**：区间 = [标准值 - 下公差, 标准值 + 上公差]
    /// （Judge() 里是 value >= std - lo，所以下公差写 -1 会得到 [91,91] 这个退化区间）。
    /// </summary>
    static JObject PairGeomRelation(string seed, string name, JObject aLink, JObject bLink)
        => Action(seed, name, "几何工具", P_GEOMREL, "几何计算", "\uf542", new JObject
        {
            ["Op"] = "Angle",
            ["MmPerPixel"] = 0.0,
            ["StandardValue"] = 90.0,
            ["UpperTolerance"] = 1.0,
            ["LowerTolerance"] = 1.0,
            ["AngleTolerance"] = 0.1,
            ["DisplayViewIndex"] = 0,
        }, new JObject { ["A"] = aLink, ["B"] = bLink });

    /// <summary>图像质量检查：启用阈值判定，只卡对焦下限（其余保持默认 0~1）</summary>
    static JObject PairImageQuality(string seed, string name, JObject imageLink)
        => Action(seed, name, "图像处理", P_IMGQUALITY, "图像质量检查", "\uf24e", new JObject
        {
            ["EnableThreshold"] = true,
            ["FocusRef"] = 60.0,
            ["FocusMin"] = 0.5,
            ["FocusMax"] = 1.0,
            ["DisplayViewIndex"] = 0,
        }, new JObject { ["Image"] = imageLink });

    /// <summary>图像差分：绝对差 + 双侧极性；总面积上限与判定表阈值同源（2000 px）</summary>
    static JObject PairImageDiff(string seed, string name, JObject refLink, JObject testLink)
        => Action(seed, name, "图像处理", P_IMAGEDIFF, "图像差分", "\uf24e", new JObject
        {
            ["Algorithm"] = "Absolute",
            ["Polarity"] = "Both",
            ["Aggregation"] = "Components",
            ["SizeMismatch"] = "Fail",
            ["Threshold"] = 30.0,
            ["MinArea"] = 20.0,
            ["MaxTotalArea"] = 2000.0,
            ["MaxComponentArea"] = 0.0,
            ["MaxValueLimit"] = 0.0,
            ["DisplayViewIndex"] = 0,
        }, new JObject { ["Reference"] = refLink, ["Test"] = testLink });

    /// <summary>
    /// 颜色序列检查：采样带垂直于线束走向。
    /// RoiParams = [中心行, 中心列, 角度(弧度), 半长, 半宽]；角度 0 时长轴沿列方向（水平），
    /// 短边（半宽）才是跨线束的采样方向——所以横放的线束要用「长边水平、短边竖直」的框。
    /// </summary>
    static JObject PairColorCheck(string seed, string name, JObject imageLink)
        => Action(seed, name, "图像处理", P_COLORCHECK, "颜色序列检查", "\uf53f", new JObject
        {
            ["RoiParams"] = new JArray { 238.0, 300.0, 0.0, 80.0, 78.0 },
            ["ReverseSequence"] = false,
            ["WireCount"] = 0,
            ["Regularize"] = true,
            ["RecipeText"] = "10 芯 = 黑,白,灰,紫,蓝,绿,黄,红,玫红,棕",
            ["RecipeAssetId"] = "",
            ["DisplayViewIndex"] = 0,
        }, new JObject { ["Image"] = imageLink });

    // ------------------------------------------------------------------
    //  变量快照
    // ------------------------------------------------------------------
    static JArray PairBuildVariables()
    {
        var list = new JArray();
        void V(string name, string type, string desc, object value)
            => list.Add(Var(name, type, desc, value));   // 复用 Program.Var（含 G("var:") 身份）

        foreach (var st in PairStations)
        {
            V(st.Short + "_Result", T_BOOL, st.Title + " 判定（true=OK）", false);
            V(st.Short + "_Reason", T_STR, st.Title + " 判定说明", "");
            // 标注图变量（HImage）：组态工位卡的 Hmi.ImageView 绑它。
            // 初值 null —— 还没跑过时图元显示"无图像"占位，而不是一张假图。
            V(st.Short + "_Image", T_HIMG, st.Title + " 标注图（运行期由「图像赋值」写入）", null);
            if (st.Kind == "Matching" || st.Kind == "Deformable")
                V(st.ScoreVar, T_DBL, st.Title + " 匹配分数", 0.0);
            if (st.Kind == "Blob")
            {
                V("Label_DefectCount", T_INT, "标签缺陷数", 0);
                V("Label_MaxArea", T_DBL, "标签最大缺陷面积(px)", 0.0);
            }
            if (st.Kind == "CodeReader")
            {
                V(st.ScoreVar, T_INT, st.Title + " 码个数", 0);
                V(st.Short + "_Text", T_STR, st.Title + " 码文本", "");
            }
            if (st.HasFor)
                V(st.LoopVar, T_INT, st.Title + " 循环张数（参数可调）", st.LoopDefault);
        }

        V("Label_PixelSize", T_DBL, "标签检测像素当量 mm/px（参数可调）", 0.05);
        V("TotalCount", T_INT, "累计检测数", 0);
        V("OkCount", T_INT, "累计良品数", 0);
        V("NgCount", T_INT, "累计不良数", 0);
        // 初值 0 而不是 100：一次没跑时"累计检测 0 / 良率 100.0%"是自相矛盾的读数
        // （0 次检测谈不上良率）。首轮跑完脚本会写真实值。
        V("YieldRate", T_DBL, "实时良率(%)", 0.0);
        V("CurrentResult", T_BOOL, "本件综合判定", false);
        V("SystemReady", T_BOOL, "系统就绪（Home 置位 / End 复位）", false);
        V("Home_Done", T_BOOL, "Home 已执行标记", false);
        // 账本根目录（统计脚本据此写 History\；绝对路径属本方案的已记录边界）
        V("SolutionRoot", T_STR, "方案根目录（账本落点）", Path.GetDirectoryName(PairSolutionPath)!);

        // ---- 四条扩展工位的变量（与七工位同名式：Result / Reason / 主数值）----
        V("Geom_Result", T_BOOL, "几何计算_关系 判定（true=OK）", false);
        V("Geom_Reason", T_STR, "几何计算_关系 判定说明", "");
        V("Geom_Angle", T_DBL, "横线与竖线夹角(度)", 0.0);

        V("Quality_Result", T_BOOL, "前置质量门 判定（true=OK）", false);
        V("Quality_Reason", T_STR, "前置质量门 判定说明", "");
        V("Quality_Focus", T_DBL, "前置质量门 对焦分（0~1）", 0.0);

        V("Diff_Result", T_BOOL, "差分_基准比对 判定（true=OK）", false);
        V("Diff_Reason", T_STR, "差分_基准比对 判定说明", "");
        V("Diff_Area", T_DBL, "差分_基准比对 差异总面积(px)", 0.0);

        V("Color_Result", T_BOOL, "颜色检查_线序 判定（true=OK）", false);
        V("Color_Reason", T_STR, "颜色检查_线序 判定说明", "");
        V("Color_Count", T_INT, "颜色检查_线序 根数", 0);
        return list;
    }



    // ------------------------------------------------------------------
    //  组态：公共层（顶栏 / 底栏）+ 8 页内容层
    //
    //  为什么公共层用"每页各生成一份"而不是 BasePages 或 Hmi.SubPage
    //  ---------
    //  · BasePages：加载期 ScadaDocument.EnsureIdentity 按**引用**清理
    //    （`_pages.Contains(_basePages[i])`），而 .vms 序列化没有 PreserveReferencesHandling
    //    → 同一张基座页反序列化成两个实例 → 加载后基座被当"幽灵条目"删掉（实测 BasePages=0）。
    //  · Hmi.SubPage：内容能渲染，但占位层（"未找到画面"）与内容层同时可见（产品边角缺陷），
    //    且首次渲染早于 PageResolver 注入时会闪一次占位。
    //  · 所以公共层由 PairChrome 在每页各生成一份：图元、事件、绑定全走"本页"通道，
    //    零依赖边角机制。8 页视觉逐像素一致（同一段生成代码），改公共层改生成器即可。
    //
    //  版面（1920×1080）
    //  ---------
    //    0   ~ 40    顶栏（状态 / 方案名 / 操作员 / 时钟）
    //    40  ~ 968   本页内容层（8 页各自布局）
    //    968 ~ 1080  底栏（8 项导航，图标 + 文字）
    // ------------------------------------------------------------------

    /// <summary>流程名 → 真实 FlowID（PairBuildScada 从 flows 填充，供按钮动作引用）。</summary>
    static readonly System.Collections.Generic.Dictionary<string, string> PairFlowIds = new();

    const double CHROME_TOP_H = 48;
    const double CHROME_BOT_Y = 968;
    const double CHROME_BOT_H = 112;

    /// <summary>底栏导航项（顺序 = 显示顺序 = 从左到右）。</summary>
    static readonly (string Page, string Icon, string Label)[] PairNav =
    {
        ("主页", "home", "主页"),
        ("自动运行", "run", "自动运行"),
        ("手动控制", "manual", "手动控制"),
        ("参数管理", "param", "参数管理"),
        ("状态监控", "monitor", "状态监控"),
        ("结果统计", "chart", "结果统计"),
        ("报警记录", "alarm", "报警记录"),
        ("运行日志", "log", "运行日志"),
    };

    // 颜色令牌（触摸屏风格：深底 + 高对比卡片 + 语义色块）
    const string C_TEXT = "#FF111827";
    const string C_SUB = "#FF6B7280";
    const string C_CARD = "#FFFFFFFF";
    const string C_CARD_HI = "#FFFFFFFF";
    const string C_BORDER = "#FFE5E7EB";
    const string C_CYAN = "#FF2563EB";
    const string C_BLUE = "#FF2563EB";
    const string C_GREEN = "#FF10B981";
    const string C_AMBER = "#FFD97706";
    const string C_RED = "#FFDC2626";
    const string C_PURPLE = "#FF7C3AED";
    const string C_INDIGO = "#FF4F46E5";
    const string C_TEAL = "#FF0D9488";
    const string C_SLATE = "#FF6B7280";

    static JObject PairBuildScada(JArray flows)
    {
        // 工位流程的真实 FlowID 由 flows 现取（本方案的 FlowID 是外部生成的随机值，
        // 不是 G("flow:名")，写死种子会得到不存在的 Id → 按钮点了找不到流程）
        PairFlowIds.Clear();
        foreach (var f in flows.OfType<JObject>())
        {
            var n = f["FlowName"]?.Value<string>();
            var id = f["FlowID"]?.Value<string>();
            if (!string.IsNullOrEmpty(n) && !string.IsNullOrEmpty(id))
                PairFlowIds[n] = id;
        }

        string mainFlowId = PairFlowIds.TryGetValue("Main", out var mid) ? mid : null;
        string homeFlowId = PairFlowIds.TryGetValue("Home", out var hid) ? hid : null;

        var scada = new JObject
        {
            ["SchemaVersion"] = 1,
            ["Pages"] = new JArray
            {
                PairPageHome(),
                PairPageAuto(mainFlowId),
                PairPageManual(mainFlowId),
                PairPageParams(),
                PairPageMonitor(),
                PairPageStats(),
                PairPageAlarms(),
                PairPageLogs(),
            },
            ["Alarms"] = new JArray
            {
                ScadaAlarm("系统-未就绪", "SystemReady", 5, 0, 2,
                    "系统未就绪（Home 未执行或被 End 复位）", "系统", "未执行 Home 流程", "在「手动控制」页点「置就绪」或运行 Home"),
                ScadaAlarm("质量-良率过低", "YieldRate", 2, 90, 2,
                    "实时良率低于 90%", "质量", "批量不良或阈值过严", "查看「结果统计」与各工位判定说明"),
                ScadaAlarm("质量-累计不良超限", "NgCount", 0, 20, 1,
                    "累计不良数超过 20", "质量", "连续 NG 或工艺漂移", "查看「运行日志」的账本与各工位分数"),
            },
            ["VariableEvents"] = new JArray
            {
                new JObject
                {
                    ["VariableId"] = G("var:CurrentResult").ToString(),
                    ["VariableName"] = "CurrentResult",
                    ["UpperLimit"] = null,
                    ["LowerLimit"] = null,
                    ["IsEnabled"] = true,
                    ["EventHooks"] = new JArray
                    {
                        Hook(5, new JArray { ScadaAct(1, "本件判定 OK") }),
                        Hook(6, new JArray { ScadaAct(1, "本件判定 NG") }),
                    },
                },
            },
            ["StartupPageId"] = G("page:主页").ToString(),
            ["BasePages"] = new JArray(),
        };
        Console.WriteLine("scada: 8 content pages (chrome baked per page), startup=主页");
        return scada;
    }


    // ==================================================================
    //  公共层（顶栏 + 底栏）：在每一页各生成一份
    //
    //  调用点固定在 PairHmiPage 里，所以 8 页的顶栏/底栏是同一段代码产出的——
    //  视觉逐像素一致，改公共层只改这里。图元名带 "公共_" 前缀，便于在属性面板/图层里辨认。
    // ==================================================================

    static void PairChrome(JArray els, string page)
    {
        // ---------------- 顶栏（0~48，Web UI 式：8px 栅格 + 垂直居中 + 细分隔）----------------
        els.Add(El(page, "公共_顶栏底", "Hmi.Rectangle", 0, 0, 1920, CHROME_TOP_H, 30, new JObject
        {
            ["Fill"] = "#FFFFFFFF", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "0",
        }));
        els.Add(El(page, "公共_顶栏线", "Hmi.Rectangle", 0, CHROME_TOP_H - 1, 1920, 1, 31, new JObject
        {
            ["Fill"] = "#FFE5E7EB", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "0",
        }));

        // 左：方案名 + 竖分隔 + 副题
        // 这里原来还有一颗"就绪灯"，但它没有标签、和右侧「本件」灯长得一样，两个无字圆点只会让人猜。
        // 就绪状态在主页「运行状态」卡里已经写清楚（带图例），顶栏不再重复放一颗无名灯。
        els.Add(PairText(page, "公共_方案名", 20, 10, 220, 28, "视觉样例对拍", 18, C_TEXT, "SemiBold", "Left", 34));
        els.Add(El(page, "公共_分隔", "Hmi.Rectangle", 176, 16, 1, 16, 34, new JObject
        {
            ["Fill"] = "#FFE5E7EB", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "0",
        }));
        els.Add(PairText(page, "公共_副题", 192, 14, 460, 20,
            "七工位视觉检测 · HALCON 例程移植", 14, C_SUB, "Normal", "Left", 34));

        // 右：本件圆点 + 标签 + 操作员 + 时钟（右对齐，8px 栅格）
        els.Add(PairDot(page, "公共_本件灯", 1352, 12, "CurrentResult"));
        els.Add(PairText(page, "公共_本件标", 1376, 13, 48, 22, "本件", 14, C_SUB, "Normal", "Left", 34));
        els.Add(PairText(page, "公共_操作员", 1450, 13, 120, 22, "admin", 14, C_TEXT, "SemiBold", "Right", 34));
        els.Add(El(page, "公共_时钟", "Hmi.Clock", 1600, 6, 300, 36, 32, new JObject
        {
            ["Text"] = "", ["Foreground"] = C_TEXT, ["FontSize"] = "18",
            ["FontWeight"] = "Bold", ["TextAlignment"] = "Right",
        }));

        // ---------------- 底栏（968~1080）----------------
        els.Add(El(page, "公共_底栏底", "Hmi.Rectangle", 0, CHROME_BOT_Y, 1920, CHROME_BOT_H, 30, new JObject
        {
            ["Fill"] = "#FFFFFFFF", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "0",
        }));
        els.Add(El(page, "公共_底栏线", "Hmi.Rectangle", 0, CHROME_BOT_Y, 1920, 1, 31, new JObject
        {
            ["Fill"] = "#FFE5E7EB", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "0",
        }));

        // 8 项等分：左留 24，项宽 220，间距 16 → 24 + 8*220 + 7*16 = 1896
        for (int i = 0; i < PairNav.Length; i++)
        {
            var item = PairNav[i];
            double x = 24 + i * 236;
            double y = CHROME_BOT_Y + 6;

            // 选中态（本页）：浅主色底 + 底部 3px 指示条 + 主色标签。
            // 公共层是"每页烘一份"，所以"当前页"在这里是编译期已知的常量——
            // 不需要运行时绑定，也不会出现"点了没反应"的中间态。
            bool active = item.Page == page;
            if (active)
            {
                els.Add(El(page, "公共_导航选中底_" + item.Page, "Hmi.Rectangle", x, y, 220, 96, 33, new JObject
                {
                    ["Fill"] = "#FFEFF6FF", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "10",
                }));
                els.Add(El(page, "公共_导航选中条_" + item.Page, "Hmi.Rectangle", x + 70, y + 90, 80, 3, 35, new JObject
                {
                    ["Fill"] = C_CYAN, ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "2",
                }));
            }

            els.Add(El(page, "公共_图标_" + item.Page, "Hmi.Picture", x + 94, y + 8, 32, 32, 34, new JObject
            {
                ["SourcePath"] = @"Image\icon_" + item.Icon + ".png",
                ["Stretch"] = "Uniform",
                ["PlaceholderText"] = "",
                ["Fill"] = "#00FFFFFF", ["Stroke"] = "#00FFFFFF", ["StrokeThickness"] = "0",
            }));
            els.Add(PairText(page, "公共_文字_" + item.Page, x, y + 46, 220, 26,
                item.Label, 15, active ? C_CYAN : "#FF6B7280", "SemiBold", "Center", 34));
            els.Add(El(page, "公共_点击_" + item.Page, "Hmi.Button", x, y, 220, 96, 40, new JObject
            {
                ["Text"] = "", ["Fill"] = "#00000000", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0",
                ["CornerRadius"] = "10", ["Foreground"] = "#00000000", ["FontSize"] = "1",
            }, null, new JArray { Hook(2, new JArray { PairAct(3, pageName: item.Page) }) }));
        }
    }

    /// <summary>
    /// 一张 1920×1080 的内容页：深色科技背景图 + 公共层（顶栏/底栏）。
    /// </summary>
    static JObject PairHmiPage(string name, JArray els)
    {
        PairChrome(els, name);

        var page = ScadaPageObj(name, 1920, 1080, els);
        page["Background"] = "#FFF7F8FA";
        page["BackgroundImage"] = @"Image\bg_main.png";
        page["BackgroundStretch"] = "UniformToFill";
        page["BackgroundOpacity"] = 100.0;
        page["ShowGrid"] = false;
        return page;
    }


    /// <summary>带对齐的文本图元（TextProps 的 TextAlignment 需要单独写）。</summary>
    static JObject PairText(string page, string name, double x, double y, double w, double h,
        string text, double size, string color, string weight = "Normal", string align = "Left", int z = 10, string vAlign = "Center")
    {
        var props = TextProps(text, size, color, weight);
        props["TextAlignment"] = align;
        props["VerticalContentAlignment"] = vAlign;
        return El(page, name, "Hmi.Text", x, y, w, h, z, props);
    }

    /// <summary>
    /// 卡片：白底 + 1px 边框 + 圆角 12；有标题时标题栏透明、字色用 accent。
    ///
    /// 【空标题也必须显式 TitleHeight=0】Panel 的 TitleHeight 默认不是 0，标题栏底色走
    /// ScadaTokens 的深色令牌；title 为空时不写 TitleHeight → 卡片顶部会留一条 38px 的深色带
    /// （浅色主题下非常显眼，深色主题时恰好看不出来）。
    /// </summary>
    static JObject PairCard(string page, string name, double x, double y, double w, double h,
        string title, string accent = C_CYAN, int z = 5)
    {
        var props = new JObject
        {
            ["Fill"] = C_CARD,
            ["Stroke"] = C_BORDER,
            ["StrokeThickness"] = "1",
            ["CornerRadius"] = "12",
        };
        if (!string.IsNullOrEmpty(title))
        {
            props["TitleText"] = title;
            props["TitleHeight"] = "38";
            props["TitleFill"] = "#00000000";
            props["TitleForeground"] = accent;
        }
        else
        {
            props["TitleHeight"] = "0";
            props["TitleFill"] = "#00000000";
        }
        return El(page, name, "Hmi.Panel", x, y, w, h, z, props);
    }

    /// <summary>
    /// 紧凑状态圆点（顶栏用，24×24）。
    ///
    /// 【为什么必须给足尺寸 + FontSize=1】Hmi.Indicator 的模板是"灯体(* 行) + 文字(Auto 行)"两行 Grid，
    /// 文字行即使 Text 为空也按 FontSize 占高。给 12×12 时文字行吃满高度、灯体行被压成 0 —— 圆点直接消失。
    /// 所以尺寸给 24、FontSize 压到 1，灯体才拿得到完整空间。
    /// 也不能用公共的 Indicator 辅助：那个固定 64×72（带下方文字标签的大灯），放进 48px 顶栏会溢出。
    /// </summary>
    static JObject PairDot(string page, string name, double x, double y, string varName)
        => El(page, name, "Hmi.Indicator", x, y, 24, 24, 34, new JObject
        {
            ["IsOn"] = "False",
            ["OnColor"] = C_GREEN,
            ["OffColor"] = "#FFD1D5DB",
            ["Shape"] = "Circle",
            ["Text"] = "",
            ["FontSize"] = "1",
        }, new JArray { Bind("IsOn", varName) });

    /// <summary>
    /// 状态项 = 24×24 圆点 + 右侧文字标签（Web UI 式，一行高 28）。
    /// 圆点尺寸与 FontSize=1 的理由见 <see cref="PairDot"/>。
    ///
    /// 三态语义靠"实心绿 / 实心琥珀 / 空心灰"区分（见调用点）：变量只有 bool，
    /// 所以灯只能表达"满足 / 不满足"，图例写在卡片右上角，别让操作员猜灰点是什么意思。
    /// </summary>
    static void PairStatus(JArray els, string page, string name, double x, double y,
        string varName, string label, double labelWidth = 120)
    {
        els.Add(El(page, name + "_点", "Hmi.Indicator", x, y, 24, 24, 34, new JObject
        {
            ["IsOn"] = "False", ["OnColor"] = C_GREEN, ["OffColor"] = "#FFD1D5DB",
            ["Shape"] = "Circle", ["Text"] = "", ["FontSize"] = "1",
        }, new JArray { Bind("IsOn", varName) }));
        els.Add(PairText(page, name + "_字", x + 32, y - 3, labelWidth, 26, label, 14, C_TEXT, "Normal", "Left", 34));
    }

    /// <summary>
    /// 指标卡（Web 仪表盘式）：白卡 + 左侧强调条 + 小标签 + 大数字 + 右下单单位。
    ///
    /// 为什么不用 Hmi.KpiBoard：它的格子底色是模板固定的深色（无属性可改），
    /// 在浅色主题里会变成一排"黑洞"。自建四层（卡/条/标签/数值）反而可控，
    /// 且数值仍用 IOField 绑变量（PairText 的 Text 不可绑）。
    /// </summary>
    static void PairMetric(JArray els, string page, string name, double x, double y, double w, double h,
        string label, string varName, string unit, string format, string accent, double valueSize = 40)
    {
        els.Add(El(page, name + "_卡", "Hmi.Rectangle", x, y, w, h, 11, new JObject
        {
            ["Fill"] = "#FFFFFFFF", ["Stroke"] = C_BORDER, ["StrokeThickness"] = "1", ["CornerRadius"] = "10",
        }));
        els.Add(El(page, name + "_条", "Hmi.Rectangle", x, y + 14, 3, 24, 12, new JObject
        {
            ["Fill"] = accent, ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "2",
        }));
        els.Add(PairText(page, name + "_标", x + 18, y + 12, w - 88, 22, label, 14, C_SUB, "Normal", "Left", 13));
        els.Add(El(page, name + "_值", "Hmi.IOField", x + 18, y + 34, w - 36, valueSize + 14, 13, new JObject
        {
            ["Value"] = "0", ["ValueFormat"] = format,
            ["Unit"] = "", ["Mode"] = "Output", ["Text"] = "",
            ["Foreground"] = C_TEXT, ["FontSize"] = valueSize.ToString("0.#"), ["TextAlignment"] = "Left",
            ["Fill"] = "#00FFFFFF", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "0",
        }, new JArray { Bind("Value", varName) }));
        // 单位跟标签同一行右对齐：单位甩到卡片右下角会和数字隔着一整个卡片宽，读起来是两个东西
        if (!string.IsNullOrEmpty(unit))
            els.Add(PairText(page, name + "_单", x + w - 66, y + 12, 48, 22, unit, 14, C_SUB, "Normal", "Right", 13));
    }

    /// <summary>页标题：左侧竖色条 + 大标题 + 副标题（顶栏 48px 之下留 8px 呼吸）。</summary>
    static void PairTitle(JArray els, string page, string title, string subtitle, string accent = C_CYAN)
    {
        els.Add(El(page, "标题条", "Hmi.Rectangle", 40, 62, 4, 30, 8, new JObject
        {
            ["Fill"] = accent, ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "2",
        }));
        els.Add(PairText(page, "页标题", 56, 56, 700, 36, title, 25, C_TEXT, "Bold", "Left"));
        if (!string.IsNullOrEmpty(subtitle))
            els.Add(PairText(page, "页副题", 56, 90, 1100, 22, subtitle, 15, C_SUB, "Normal", "Left"));
    }

    /// <summary>
    /// 功能入口卡（Web 仪表盘式）：白底 + 1px 边框 + 圆角 10 + 左上图标 + 标题 + 一行描述。
    ///
    /// 为什么不再是"彩色大色块"：那是工控触摸屏的语言（大色块靠颜色区分功能）；
    /// Web 仪表盘靠**留白 + 层级**区分，颜色只留给"状态"和"主操作"。
    /// 图标是深灰线稿 PNG（浅底可见），卡片 hover/点击由透明 Button 承接。
    /// <paramref name="accent"/> 只用于图标下的细色条与标题左侧强调，不铺满整卡。
    /// </summary>
    static void PairTile(JArray els, string page, string name, double x, double y, double w, double h,
        string icon, string label, string accent, string targetPage,
        double iconSize = 40, double fontSize = 18, string desc = null)
    {
        // 卡面（白底 + 细边框）
        els.Add(El(page, name + "_底", "Hmi.Rectangle", x, y, w, h, 12, new JObject
        {
            ["Fill"] = "#FFFFFFFF", ["Stroke"] = "#FFE5E7EB", ["StrokeThickness"] = "1", ["CornerRadius"] = "10",
        }));
        // 左侧强调条（4px，accent 色，只在左边缘）
        els.Add(El(page, name + "_条", "Hmi.Rectangle", x, y + 16, 3, 24, 13, new JObject
        {
            ["Fill"] = accent, ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "2",
        }));
        // 图标（深灰线稿）
        els.Add(El(page, name + "_图标", "Hmi.Picture", x + 20, y + 20, iconSize, iconSize, 14, new JObject
        {
            ["SourcePath"] = @"Image\icon_" + icon + ".png",
            ["Stretch"] = "Uniform",
            ["PlaceholderText"] = "",
            ["Fill"] = "#00FFFFFF", ["Stroke"] = "#00FFFFFF", ["StrokeThickness"] = "0",
        }));
        // 标题 + 描述（左对齐，Web 卡片习惯）
        els.Add(PairText(page, name + "_标题", x + 20, y + 20 + iconSize + 8, w - 40, 30,
            label, fontSize, "#FF111827", "SemiBold", "Left", 16));
        if (!string.IsNullOrEmpty(desc))
            els.Add(PairText(page, name + "_述", x + 20, y + 20 + iconSize + 42, w - 40, 46,
                desc, 14, "#FF6B7280", "Normal", "Left", 16, "Top"));
        // 点击区（透明，覆盖整卡）
        els.Add(El(page, name + "_点击", "Hmi.Button", x, y, w, h, 20, new JObject
        {
            ["Text"] = "", ["Fill"] = "#00000000", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0",
            ["CornerRadius"] = "10", ["Foreground"] = "#00000000", ["FontSize"] = "1",
        }, null, new JArray { Hook(2, new JArray { PairAct(3, pageName: targetPage) }) }));
    }


    /// <summary>
    /// 主操作卡（主页唯一的主色卡）：白底 + 2px 主色边框 + 大图标 + 大标题 + 说明 + 底部实心 CTA。
    ///
    /// 为什么不用"整卡填主色"：图标是深灰线稿 PNG（浅底可见），铺主色底就看不见了；
    /// 而且整屏只有这一张卡该抢注意力，靠 2px 主色边框 + 实心 CTA 已经够，不必再染一大块蓝。
    ///
    /// 内容垂直分布（图标→标题→说明→CTA 到底边），消灭"上半截有字、下半截全空"的观感。
    /// </summary>
    static void PairHeroTile(JArray els, string page, string name, double x, double y, double w, double h,
        string icon, string label, string desc, string cta, string targetPage)
    {
        els.Add(El(page, name + "_底", "Hmi.Rectangle", x, y, w, h, 12, new JObject
        {
            ["Fill"] = "#FFFFFFFF", ["Stroke"] = C_CYAN, ["StrokeThickness"] = "2", ["CornerRadius"] = "12",
        }));
        els.Add(El(page, name + "_图标", "Hmi.Picture", x + 28, y + 28, 88, 88, 14, new JObject
        {
            ["SourcePath"] = @"Image\icon_" + icon + ".png",
            ["Stretch"] = "Uniform",
            ["PlaceholderText"] = "",
            ["Fill"] = "#00FFFFFF", ["Stroke"] = "#00FFFFFF", ["StrokeThickness"] = "0",
        }));
        els.Add(PairText(page, name + "_标题", x + 28, y + 138, w - 56, 44,
            label, 30, C_TEXT, "Bold", "Left", 16));
        els.Add(PairText(page, name + "_述", x + 28, y + 186, w - 56, 72,
            desc, 15, C_SUB, "Normal", "Left", 16, "Top"));

        // 实心主按钮（整卡唯一的主色块；点击由覆盖整卡的透明按钮承接）
        double ctaY = y + h - 92;
        els.Add(El(page, name + "_CTA底", "Hmi.Rectangle", x + 28, ctaY, w - 56, 56, 17, new JObject
        {
            ["Fill"] = C_CYAN, ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "10",
        }));
        els.Add(PairText(page, name + "_CTA字", x + 28, ctaY, w - 56, 56,
            cta, 18, "#FFFFFFFF", "SemiBold", "Center", 18));

        els.Add(El(page, name + "_点击", "Hmi.Button", x, y, w, h, 20, new JObject
        {
            ["Text"] = "", ["Fill"] = "#00000000", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0",
            ["CornerRadius"] = "12", ["Foreground"] = "#00000000", ["FontSize"] = "1",
        }, null, new JArray { Hook(2, new JArray { PairAct(3, pageName: targetPage) }) }));
    }


    // ==================================================================
    //  内容页 1：主页（宫格入口）
    // ==================================================================
    static JObject PairPageHome()
    {
        const string page = "主页";
        var els = new JArray();

        PairTitle(els, page, "生产总览", "点卡片进入各功能页 · 采集 → 处理 → 判定 → 输出", C_CYAN);

        // 主卡：自动运行（540×390，跨两行）—— 白底 + 2px 主色边框 + 大图标 + 实心 CTA
        PairHeroTile(els, page, "入口_自动运行", 40, 130, 540, 390, "run", "自动运行",
            "整线：七工位各起各的会话并行检测（采集 → 处理 → 判定 → 上报），\n一条故障只停它自己；整件串行可追溯统计与账本。",
            "开始运行  →", "自动运行");

        // 6 张副卡：3 列 × 2 行（宽 400，高 185，间距 20；右缘 1840 与其余 7 页同一条线）
        var subs = new (string Icon, string Label, string Accent, string Target, string Desc)[]
        {
            ("manual", "手动控制", C_GREEN,  "手动控制", "单工位触发与系统就绪"),
            ("param",  "参数管理", C_BLUE,   "参数管理", "循环张数与判定阈值"),
            ("monitor","状态监控", C_SLATE,  "状态监控", "良率趋势与实时报警"),
            ("chart",  "结果统计", C_TEAL,   "结果统计", "分布图表与生产报表"),
            ("alarm",  "报警记录", C_RED,    "报警记录", "报警清单与确认"),
            ("log",    "运行日志", C_PURPLE, "运行日志", "生产账本与关键计数"),
        };
        for (int i = 0; i < subs.Length; i++)
        {
            double x = 600 + (i % 3) * 420;
            double y = 130 + (i / 3) * 205;
            PairTile(els, page, "入口_" + subs[i].Target, x, y, 400, 185,
                subs[i].Icon, subs[i].Label, subs[i].Accent, subs[i].Target, 40, 18, subs[i].Desc);
        }

        // 底部 KPI：四张指标卡（白卡 + 强调条 + 大数字），不用 KpiBoard（它格子底是固定深色）
        els.Add(PairText(page, "KPI标题", 40, 562, 400, 26, "实时生产 KPI", 16, C_TEXT, "SemiBold", "Left", 13));
        PairMetric(els, page, "KPI1", 40, 598, 438, 112, "累计检测", "TotalCount", "件", "0", C_BLUE);
        PairMetric(els, page, "KPI2", 494, 598, 438, 112, "良品", "OkCount", "件", "0", C_GREEN);
        PairMetric(els, page, "KPI3", 948, 598, 438, 112, "不良", "NgCount", "件", "0", C_RED);
        PairMetric(els, page, "KPI4", 1402, 598, 438, 112, "良率", "YieldRate", "%", "F1", C_TEAL);

        // 状态与良率
        els.Add(PairCard(page, "状态卡", 40, 750, 890, 190, "运行状态", C_GREEN));
        // 图例：灯只有"满足/不满足"两态，不写图例就得让操作员猜灰点是什么意思
        els.Add(PairText(page, "状态图例", 690, 757, 220, 22,
            "● 满足    ○ 未满足", 13, C_SUB, "Normal", "Right", 34));
        PairStatus(els, page, "就绪", 66, 800, "SystemReady", "系统就绪", 100);
        PairStatus(els, page, "本件", 190, 800, "CurrentResult", "本件判定", 100);
        PairStatus(els, page, "初始化", 314, 800, "Home_Done", "已初始化", 100);
        // 进度条必须自带标签：一条恒定的绿条不写"在量什么"等于零信息
        els.Add(PairText(page, "节拍标签", 66, 856, 240, 26,
            "当前节拍进度", 14, C_TEXT, "Normal", "Left", 34));
        els.Add(PairText(page, "节拍目标", 700, 856, 190, 26,
            "目标 ≥ 90%", 13, C_SUB, "Normal", "Right", 34));
        // 值文字用深色：条空着时白字压在浅轨道上等于看不见（ShowLimits 的两根黄刻度也一并关掉，
        // 目标值改由右侧「目标 ≥ 90%」说明，避免两根孤立竖线被当成渲染残渣）。
        els.Add(El(page, "良率条", "Hmi.ProgressBar", 370, 856, 320, 26, 10, new JObject
        {
            ["Value"] = "0", ["Minimum"] = "0", ["Maximum"] = "100", ["ValueFormat"] = "F1",
            ["Orientation"] = "Horizontal", ["TrackColor"] = "#FFF3F4F6", ["ShowValue"] = "True",
            ["ShowTicks"] = "False", ["ShowLimits"] = "False",
            ["Fill"] = C_GREEN, ["Stroke"] = C_BORDER, ["StrokeThickness"] = "1", ["CornerRadius"] = "6",
            ["Foreground"] = C_TEXT, ["FontSize"] = "13",
        }, new JArray { Bind("Value", "YieldRate") }));

        els.Add(PairCard(page, "提示卡", 950, 750, 890, 190, "使用说明", C_SLATE));
        els.Add(PairText(page, "说明", 975, 800, 830, 124,
            "① 主页点「自动运行」→ 启动整线（七工位并行，各起各的会话）或执行一次整件（串行 + 统计 + 账本）\n" +
            "② 实时算法图像在宿主「视觉图像」面板 1~7 号窗口（与七工位一一对应）\n" +
            "③ 参数在「参数管理」页；历史记录与报表在「结果统计」「运行日志」页", 14, C_SUB, "Normal", "Left", 10, "Top"));

        return PairHmiPage(page, els);
    }

    // ==================================================================
    //  内容页 2：自动运行（生产主界面）
    // ==================================================================
    static JObject PairPageAuto(string mainFlowId)
    {
        const string page = "自动运行";
        var els = new JArray();

        PairTitle(els, page, "自动运行", "整线并行检测 · 整件串行 · 实时判定", C_CYAN);

        // 运行控制条。
        //
        // 【整线 vs 整件】2026-10-28 起流程管理有了「随整线启动」位（InvokeType 128）：
        //   整线 = 七个工位流程**各起各的会话**一起跑，一条失败/卡死只收尾它自己（生产模式）；
        //   整件 = Main 串行调七工位 → 统计 → 结果上报 → 写账本（演示/追溯模式）。
        // 组态里没有"启动整线"这个动作类型，所以整线按钮 = 一条钩子里挂七个「启动流程」动作，
        // 语义与工具栏「启动整线」一致（都只是逐条 StartFlow，互不等待）。
        var lineStartActions = new JArray();
        var lineStopActions = new JArray();
        foreach (var st in PairStations)
        {
            string flowId = PairStationFlowId(st);
            lineStartActions.Add(PairAct(4, flowId: flowId, flowName: st.FlowName));
            lineStopActions.Add(PairAct(5, flowId: flowId, flowName: st.FlowName));
        }

        els.Add(PairCard(page, "控制卡", 40, 130, 1800, 96, "", C_CYAN));
        els.Add(El(page, "启动整线", "Hmi.Button", 60, 152, 196, 56, 20, ButtonProps("启动整线", C_GREEN, "#FF047857"), null,
            new JArray { Hook(2, lineStartActions) }));
        els.Add(El(page, "停止整线", "Hmi.Button", 272, 152, 196, 56, 20, ButtonProps("停止整线", C_RED, "#FF991B1B"), null,
            new JArray { Hook(2, lineStopActions) }));
        els.Add(El(page, "执行一次整件", "Hmi.Button", 484, 152, 216, 56, 20, ButtonProps("执行一次整件", C_BLUE, "#FF1D4ED8"), null,
            new JArray { Hook(2, new JArray { PairAct(6, flowId: mainFlowId, flowName: "Main") }) }));
        els.Add(PairText(page, "运行提示", 716, 156, 560, 48,
            "整线 = 七工位各起各的会话并行（一条故障只停它自己）\n执行一次整件 = 串行跑 Main（含统计、结果上报与账本）",
            13, C_SUB, "Normal", "Left", 10, "Top"));
        PairStatus(els, page, "就绪", 1300, 160, "SystemReady", "就绪", 70);
        PairStatus(els, page, "本件", 1390, 160, "CurrentResult", "本件", 70);
        els.Add(El(page, "良率条2", "Hmi.ProgressBar", 1480, 176, 340, 26, 10, new JObject
        {
            ["Value"] = "0", ["Minimum"] = "0", ["Maximum"] = "100", ["ValueFormat"] = "F1",
            ["Orientation"] = "Horizontal", ["TrackColor"] = "#FFF3F4F6", ["ShowValue"] = "True",
            ["ShowTicks"] = "False", ["ShowLimits"] = "False",
            ["Fill"] = C_GREEN, ["Stroke"] = C_BORDER, ["StrokeThickness"] = "1", ["CornerRadius"] = "6",
            // 深色值文字：条空着时白字压在浅轨道上看不见（见主页同名注释）
            ["Foreground"] = C_TEXT, ["FontSize"] = "13",
        }, new JArray { Bind("Value", "YieldRate") }));

        // 七工位卡：4 列 × 2 行（宽 435，高 210）
        // 版式（Web 卡片式）：左 = 标注图缩略图（4:3），右 = 状态 / 窗口号 / 数值 / 操作
        for (int i = 0; i < PairStations.Length; i++)
        {
            var st = PairStations[i];
            double x = 40 + (i % 4) * 455;
            double y = 240 + (i / 4) * 226;

            els.Add(PairCard(page, "站卡_" + st.Short, x, y, 435, 210, st.Title, C_PURPLE));

            // 左：标注图（Hmi.ImageView 绑全局图像变量；未跑过时显示"无图像"占位）
            els.Add(El(page, "站图_" + st.Short, "Hmi.ImageView", x + 16, y + 46, 176, 132, 10, new JObject
            {
                ["Source"] = "",
                ["Stretch"] = "Uniform",
                ["PlaceholderText"] = "无图像",
                ["Fill"] = "#FFF3F4F6",
                ["Stroke"] = "#FFE5E7EB", ["StrokeThickness"] = "1", ["CornerRadius"] = "6",
            }, new JArray { Bind("Source", st.Short + "_Image") }));

            // 右列（x+204，宽 215）
            double rx = x + 204;
            PairStatus(els, page, "站态_" + st.Short, rx, y + 50, st.Short + "_Result", "判定", 60);
            els.Add(PairText(page, "站窗_" + st.Short, rx, y + 74, 215, 24,
                "视图窗口 " + st.ViewIndex + " · " + st.ScoreLabel, 13, C_SUB, "Normal", "Left", 34));
            els.Add(El(page, "站值_" + st.Short, "Hmi.IOField", rx, y + 100, 215, 32, 10, new JObject
            {
                ["Value"] = "0",
                ["ValueFormat"] = st.Kind == "CodeReader" || st.Kind == "Blob" ? "0" : "F3",
                ["Unit"] = "", ["Mode"] = "Output", ["Text"] = "",
                ["Foreground"] = C_TEXT, ["FontSize"] = "20", ["TextAlignment"] = "Right",
                ["Fill"] = "#00FFFFFF", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "4",
            }, new JArray { Bind("Value", st.ScoreVar) }));
            els.Add(El(page, "站跑_" + st.Short, "Hmi.Button", rx, y + 144, 215, 34, 20,
                ButtonProps("单跑本工位", C_SLATE, "#FFE5E7EB"), null,
                new JArray { Hook(2, new JArray { PairAct(6, flowId: PairStationFlowId(st), flowName: st.FlowName) }) }));
        }

        // 底部：良率趋势 + 报警条
        els.Add(PairCard(page, "趋势卡", 40, 692, 1180, 248, "良率趋势", C_CYAN));
        els.Add(El(page, "良率趋势", "Hmi.MultiTrend", 60, 738, 1140, 186, 10, new JObject
        {
            ["Value1"] = "100", ["Value2"] = "0", ["Value3"] = "0", ["Value4"] = "0",
            ["Series1Name"] = "良率 %", ["Series2Name"] = "", ["Series3Name"] = "", ["Series4Name"] = "",
            ["Series1Color"] = C_GREEN,
            ["TimeWindow"] = "300", ["RangeMode"] = "Shared", ["ShowLegend"] = "True",
            ["Fill"] = "#FFFFFFFF", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "0",
        }, new JArray { Bind("Value1", "YieldRate", "F1") }));

        els.Add(PairCard(page, "报警卡", 1240, 692, 600, 248, "实时报警", C_RED));
        els.Add(El(page, "报警条", "Hmi.AlarmBanner", 1258, 736, 564, 186, 10, new JObject
        {
            ["MaxRows"] = "6", ["EmptyText"] = "无报警",
            ["NormalColor"] = C_GREEN, ["WarningColor"] = C_AMBER, ["CriticalColor"] = C_RED,
            ["Fill"] = "#FFFFFFFF", ["Stroke"] = "#00000000", ["CornerRadius"] = "0",
        }));

        return PairHmiPage(page, els);
    }

    /// <summary>工位流程的真实 FlowID（从 PairFlowIds 取；本方案 FlowID 不是 G("flow:名")）。</summary>
    static string PairStationFlowId(PairStation st)
        => PairFlowIds.TryGetValue(st.FlowName, out var id) ? id : null;

    // ==================================================================
    //  内容页 3：手动控制（单工位调试）
    // ==================================================================
    static JObject PairPageManual(string mainFlowId)
    {
        const string page = "手动控制";
        var els = new JArray();

        PairTitle(els, page, "手动控制", "单工位触发 · 系统就绪 · 调试用", C_GREEN);

        els.Add(PairCard(page, "系统卡", 40, 130, 1800, 110, "系统状态", C_CYAN));
        els.Add(El(page, "置就绪", "Hmi.Button", 60, 178, 160, 44, 20, ButtonProps("置就绪", C_GREEN, "#FF047857"), null,
            new JArray { Hook(2, new JArray { ScadaAct(2, varName: "SystemReady", value: "True") }) }));
        els.Add(El(page, "清就绪", "Hmi.Button", 236, 178, 160, 44, 20, ButtonProps("清就绪", C_RED, "#FF991B1B"), null,
            new JArray { Hook(2, new JArray { ScadaAct(2, varName: "SystemReady", value: "False") }) }));
        // 状态项之间留 110：灯 24 + 间距 8 + 标签宽度，再挤就会和右侧说明撞在一起
        PairStatus(els, page, "就绪", 420, 186, "SystemReady", "就绪", 70);
        PairStatus(els, page, "初始化", 530, 186, "Home_Done", "已初始化", 100);
        els.Add(PairText(page, "手动说明", 680, 182, 1120, 32,
            "「单跑本工位」= 只跑这一条工位流程（含判定与变量输出，不写账本）；整轮请回「自动运行」页。",
            14, C_SUB, "Normal", "Left", 10, "Top"));

        // 七工位单跑按钮：2 列
        for (int i = 0; i < PairStations.Length; i++)
        {
            var st = PairStations[i];
            double x = 40 + (i % 2) * 910;
            double y = 262 + (i / 2) * 96;

            els.Add(PairCard(page, "手卡_" + st.Short, x, y, 890, 82, "", C_PURPLE));
            els.Add(PairText(page, "手名_" + st.Short, x + 24, y + 26, 420, 30, st.Title, 16, C_TEXT, "Bold", "Left"));
            els.Add(El(page, "手值_" + st.Short, "Hmi.IOField", x + 450, y + 24, 200, 32, 10, new JObject
            {
                ["Value"] = "0",
                ["ValueFormat"] = st.Kind == "CodeReader" || st.Kind == "Blob" ? "0" : "F3",
                ["Unit"] = "", ["Mode"] = "Output", ["Text"] = st.ScoreLabel,
                ["Foreground"] = "#FF111827", ["FontSize"] = "13", ["TextAlignment"] = "Right",
                ["Fill"] = "#00FFFFFF", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "2",
            }, new JArray { Bind("Value", st.ScoreVar) }));
            els.Add(El(page, "手跑_" + st.Short, "Hmi.Button", x + 690, y + 22, 176, 40, 20,
                ButtonProps("单跑本工位", C_BLUE, "#FF1D4ED8"), null,
                new JArray { Hook(2, new JArray { PairAct(6, flowId: PairStationFlowId(st), flowName: st.FlowName) }) }));
        }

        // 卡高按内容给：4 行 14px（行距 ~26）≈ 110，标题 38 + 上下留白 → 200 足够。
        // 给 286 会在卡底留 100 多像素空洞，比内容还高。
        els.Add(PairCard(page, "手动提示", 40, 654, 1800, 200, "调试提示", C_AMBER));
        els.Add(PairText(page, "手动提示文", 65, 700, 1750, 140,
            "· 单跑只执行该工位流程，判定结果写入全局变量 XXX_Result / XXX_Score / XXX_Reason。\n" +
            "· 每轮循环末尾有 200ms 延时（让每张图的标注结果都看得见），单跑时同样生效。\n" +
            "· PCB 工位样例图 121 张，单跑约需 25~30 秒（含延时），属正常。\n" +
            "· 要改阈值：在流程栏右键对应算法步骤改 MinScore / MaxDefectCount / MaxSingleArea。",
            14, C_SUB, "Normal", "Left", 10, "Top"));

        return PairHmiPage(page, els);
    }

    // ==================================================================
    //  内容页 4：参数管理
    // ==================================================================
    static JObject PairPageParams()
    {
        const string page = "参数管理";
        var els = new JArray();

        PairTitle(els, page, "参数管理", "循环张数与像素当量（参考值）· 判定阈值只读参考", C_BLUE);

        els.Add(PairCard(page, "参说明卡", 40, 130, 1800, 88, "", C_AMBER));
        els.Add(PairText(page, "参说明", 62, 150, 1760, 50,
            "本页的步进/复位只改全局变量（看板值）。要真正改变遍历张数：流程栏右键「遍历样例图」For 步骤改「循环次数」。",
            14, C_AMBER, "Normal", "Left", 10, "Top"));

        double y = 230;
        foreach (var st in PairStations.Where(s => s.HasFor))
        {
            PairParamRow(els, page, st.LoopVar, st.Title + " · 循环张数（参考值）", 1, 0, 200, y, "0", st.LoopDefault);
            y += 74;
        }
        PairParamRow(els, page, "Label_PixelSize", "标签检测 · 像素当量 mm/px（参考值）", 0.005, 0, 1, y, "0.000", 0.05);

        // 阈值参考表：四列独立图元按 x 定位（不用 PadRight 拼串——中文宽度不一致会错位）
        // 行高 25 / 卡高 268 是算过的：卡片底边必须落在 940 之内（底栏 968 起），
        // 字号抬到 14 之后 7 行 + 表头在 290 高度里就会顶穿底栏。
        double ty = y + 70;
        els.Add(PairCard(page, "阈值卡", 40, ty, 1800, 268, "判定阈值（只读参考，改阈值请改算法步骤）", C_PURPLE));

        double c1 = 66, c2 = 470, c3 = 900, c4 = 1340;
        els.Add(PairText(page, "阈值_表头1", c1, ty + 44, 380, 26, "工位", 14, C_CYAN, "Bold", "Left"));
        els.Add(PairText(page, "阈值_表头2", c2, ty + 44, 400, 26, "规则", 14, C_CYAN, "Bold", "Left"));
        els.Add(PairText(page, "阈值_表头3", c3, ty + 44, 400, 26, "期望", 14, C_CYAN, "Bold", "Left"));
        els.Add(PairText(page, "阈值_表头4", c4, ty + 44, 400, 26, "数据来源", 14, C_CYAN, "Bold", "Left"));
        els.Add(El(page, "阈值_表头线", "Hmi.Rectangle", 66, ty + 76, 1740, 1, 12, new JObject
        {
            ["Fill"] = "#3322D3EE", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "0",
        }));

        for (int i = 0; i < PairStations.Length; i++)
        {
            var st = PairStations[i];
            double ry = ty + 84 + i * 25;
            string rule, expect;
            switch (st.Kind)
            {
                case "Matching":
                case "Deformable":
                    rule = "分数下限 / 匹配数"; expect = "≥ MinScore / ≥ 1"; break;
                case "Blob":
                    rule = "缺陷数上限 / 最大面积"; expect = "≤ MaxDefectCount / ≤ MaxSingleArea"; break;
                default:
                    rule = "码个数"; expect = "≥ 1"; break;
            }
            els.Add(PairText(page, "阈值_" + st.Short + "_1", c1, ry, 380, 24, st.Title, 14, C_TEXT, "Normal", "Left"));
            els.Add(PairText(page, "阈值_" + st.Short + "_2", c2, ry, 400, 24, rule, 14, C_SUB, "Normal", "Left"));
            els.Add(PairText(page, "阈值_" + st.Short + "_3", c3, ry, 400, 24, expect, 14, C_SUB, "Normal", "Left"));
            els.Add(PairText(page, "阈值_" + st.Short + "_4", c4, ry, 400, 24, st.ScoreVar, 14, C_AMBER, "Normal", "Left"));
        }

        return PairHmiPage(page, els);
    }

    /// <summary>一行参数：名称 + IOField + 增/减/复位按钮（增减走 Increment/Decrement 动作，带步长与夹紧）</summary>
    static void PairParamRow(JArray els, string page, string varName, string label,
        double step, double min, double max, double y, string format, double resetValue)
    {
        els.Add(El(page, "行_" + varName, "Hmi.Rectangle", 40, y, 1800, 64, 5, new JObject
        {
            ["Fill"] = C_CARD, ["Stroke"] = C_BORDER, ["StrokeThickness"] = "1", ["CornerRadius"] = "10",
        }));
        // 三段式：标签（左）· 数值框（紧贴标签）· 按钮组（右对齐）
        els.Add(PairText(page, "名_" + varName, 66, y + 16, 520, 32, label, 15, C_TEXT, "Bold", "Left"));
        els.Add(El(page, "值_" + varName, "Hmi.IOField", 1060, y + 16, 210, 32, 10, new JObject
        {
            ["Value"] = "0", ["ValueFormat"] = format,
            ["Unit"] = "", ["Mode"] = "Output", ["Text"] = "",
            ["Foreground"] = C_TEXT, ["FontSize"] = "20", ["TextAlignment"] = "Right",
            ["Fill"] = "#00FFFFFF", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "4",
        }, new JArray { Bind("Value", varName) }));

        els.Add(PairIncDec(page, "增_" + varName, 1290, y + 14, "+ 步进", varName, 21, step, min, max, C_GREEN));
        els.Add(PairIncDec(page, "减_" + varName, 1432, y + 14, "- 步进", varName, 22, step, min, max, C_AMBER));
        els.Add(El(page, "复_" + varName, "Hmi.Button", 1574, y + 14, 100, 36, 20,
            ButtonProps("复位", C_SLATE, "#FFE5E7EB"), null,
            new JArray { Hook(2, new JArray { ScadaAct(2, varName: varName,
                value: resetValue.ToString(System.Globalization.CultureInfo.InvariantCulture)) }) }));
    }

    static JObject PairIncDec(string page, string name, double x, double y, string text,
        string varName, int actionType, double step, double min, double max, string fill)
        => El(page, name, "Hmi.Button", x, y, 130, 36, 20, ButtonProps(text, fill, "#FF1F2937"), null,
            new JArray
            {
                Hook(2, new JArray
                {
                    new JObject
                    {
                        ["Type"] = actionType,
                        ["Text"] = null,
                        ["VariableId"] = G("var:" + varName).ToString(),
                        ["VariableName"] = varName,
                        ["ArrayIndex"] = -1,
                        ["Value"] = null,
                        ["Step"] = step,
                        ["Min"] = min,
                        ["Max"] = max,
                        ["TargetPageId"] = Guid.Empty.ToString(),
                        ["TargetPageName"] = null,
                        ["FlowId"] = null,
                        ["FlowName"] = null,
                        ["AxisId"] = Guid.Empty.ToString(),
                        ["AxisName"] = null,
                        ["TargetMm"] = null,
                        ["VelocityMmPerSec"] = null,
                        ["OnlyIfPreviousSucceeded"] = false,
                    },
                }),
            });

    // ==================================================================
    //  内容页 5：状态监控
    // ==================================================================
    static JObject PairPageMonitor()
    {
        const string page = "状态监控";
        var els = new JArray();

        PairTitle(els, page, "状态监控", "良率趋势 · 仪表 · 生产计数 · 实时报警", C_TEAL);

        els.Add(PairCard(page, "趋势大卡", 40, 130, 1180, 380, "良率趋势（最近 300 秒）", C_CYAN));
        els.Add(El(page, "趋势", "Hmi.MultiTrend", 60, 178, 1140, 314, 10, new JObject
        {
            ["Value1"] = "100", ["Value2"] = "0", ["Value3"] = "0", ["Value4"] = "0",
            ["Series1Name"] = "良率 %", ["Series2Name"] = "累计检测", ["Series3Name"] = "", ["Series4Name"] = "",
            ["Series1Color"] = C_GREEN, ["Series2Color"] = C_CYAN,
            ["TimeWindow"] = "300", ["RangeMode"] = "Independent", ["ShowLegend"] = "True",
            ["Fill"] = "#FFFFFFFF", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "0",
        }, new JArray
        {
            Bind("Value1", "YieldRate", "F1"),
            Bind("Value2", "TotalCount", "F0"),
        }));

        els.Add(PairCard(page, "仪表卡", 1240, 130, 600, 380, "实时良率", C_GREEN));
        els.Add(El(page, "良率表盘", "Hmi.Gauge", 1290, 180, 300, 300, 10, new JObject
        {
            ["Value"] = "0", ["Minimum"] = "0", ["Maximum"] = "100", ["ValueFormat"] = "0.0", ["Unit"] = "%",
            ["ShowValue"] = "True", ["TrackColor"] = "#FFF3F4F6", ["ScaleColor"] = "#FF6B7280",
            ["Fill"] = C_GREEN, ["Stroke"] = "#FFE5E7EB", ["StrokeThickness"] = "1",
            ["Foreground"] = C_TEXT, ["FontSize"] = "15", ["FontWeight"] = "SemiBold",
        }, new JArray { Bind("Value", "YieldRate") }));
        // 表盘右边原来还有一条 210×26 的良率进度条：盘面上已经写着 0.0%，旁边再挂一条空轨道
        // 只会被当成"渲染残渣"。删掉，读数只留一处。
        // 计数卡：四张指标卡（2×2，卡内 400×84）
        els.Add(PairText(page, "计数标题", 40, 534, 400, 26, "生产计数", 16, C_TEXT, "SemiBold", "Left", 13));
        PairMetric(els, page, "计1", 40, 568, 428, 84, "总数", "TotalCount", "件", "0", C_BLUE, 24);
        PairMetric(els, page, "计2", 484, 568, 428, 84, "良品", "OkCount", "件", "0", C_GREEN, 24);
        PairMetric(els, page, "计3", 40, 664, 428, 84, "不良", "NgCount", "件", "0", C_RED, 24);
        PairMetric(els, page, "计4", 484, 664, 428, 84, "良率", "YieldRate", "%", "F1", C_TEAL, 24);

        els.Add(PairCard(page, "监控报警卡", 940, 530, 900, 200, "实时报警", C_RED));
        els.Add(El(page, "监控报警条", "Hmi.AlarmBanner", 958, 578, 864, 140, 10, new JObject
        {
            ["MaxRows"] = "4", ["EmptyText"] = "无报警",
            ["NormalColor"] = C_GREEN, ["WarningColor"] = C_AMBER, ["CriticalColor"] = C_RED,
            ["Fill"] = "#FFFFFFFF", ["Stroke"] = "#00000000", ["CornerRadius"] = "0",
        }));

        // 卡高按 3 行 14px 正文给（标题 38 + 正文 ~84 + 上下留白）：
        // 给 190 会在卡底留近百像素空洞，比内容还高。
        els.Add(PairCard(page, "监控说明", 40, 750, 1800, 160, "指标说明", C_SLATE));
        els.Add(PairText(page, "监控说明文", 65, 794, 1750, 106,
            "· 良率 = 良品数 / 累计检测数 × 100%，由主流程「统计与良率」C# 脚本每轮计算一次。\n" +
            "· 累计检测数含本轮；「本件」判定 = 七个工位全部 OK。\n" +
            "· 良率低于 90% 触发「质量-良率过低」报警；累计不良超过 20 触发「累计不良超限」报警。",
            14, C_SUB, "Normal", "Left", 10, "Top"));

        return PairHmiPage(page, els);
    }

    // ==================================================================
    //  内容页 6：结果统计
    // ==================================================================
    static JObject PairPageStats()
    {
        const string page = "结果统计";
        var els = new JArray();

        PairTitle(els, page, "结果统计", "良品/不良分布 · 判定占比 · 生产报表（数据来自 CSV 账本）", C_TEAL);

        els.Add(PairCard(page, "柱卡", 40, 130, 590, 320, "良品 / 不良", C_GREEN));
        els.Add(El(page, "良品不良", "Hmi.BarChart", 60, 178, 550, 252, 10, new JObject
        {
            ["BarSeries"] = JsonConvert.SerializeObject(new object[]
            {
                new { Label = "良品", Variable = "OkCount", Color = C_GREEN },
                new { Label = "不良", Variable = "NgCount", Color = C_RED },
            }),
            ["Horizontal"] = "False", ["MaxValue"] = "0", ["TargetValue"] = "0",
            ["ShowValues"] = "True", ["SortMode"] = "None", ["ValueFormat"] = "0",
            ["Fill"] = "#FFFFFFFF", ["Stroke"] = "#00000000", ["CornerRadius"] = "0",
        }));

        els.Add(PairCard(page, "饼卡", 650, 130, 590, 320, "判定占比", C_PURPLE));
        els.Add(El(page, "判定占比", "Hmi.PieChart", 670, 178, 550, 252, 10, new JObject
        {
            ["PieSlices"] = JsonConvert.SerializeObject(new object[]
            {
                new { Label = "良品", Variable = "OkCount", Color = C_GREEN },
                new { Label = "不良", Variable = "NgCount", Color = C_RED },
            }),
            ["Donut"] = "True", ["ShowPercent"] = "True", ["MergeBelow"] = "0.02", ["ValueFormat"] = "0",
            ["Fill"] = "#FFFFFFFF", ["Stroke"] = "#00000000", ["CornerRadius"] = "0",
        }));

        els.Add(PairCard(page, "统计仪表卡", 1260, 130, 580, 320, "实时良率", C_GREEN));
        els.Add(El(page, "统计表盘", "Hmi.Gauge", 1320, 176, 260, 260, 10, new JObject
        {
            ["Value"] = "0", ["Minimum"] = "0", ["Maximum"] = "100", ["ValueFormat"] = "0.0", ["Unit"] = "%",
            ["ShowValue"] = "True", ["TrackColor"] = "#FFF3F4F6", ["ScaleColor"] = "#FF6B7280",
            ["Fill"] = C_GREEN, ["Stroke"] = "#FFE5E7EB", ["StrokeThickness"] = "1",
            ["Foreground"] = C_TEXT, ["FontSize"] = "15", ["FontWeight"] = "SemiBold",
        }, new JArray { Bind("Value", "YieldRate") }));
        PairMetric(els, page, "统计总数", 1600, 210, 220, 110, "累计检测", "TotalCount", "件", "0", C_BLUE, 26);

        els.Add(PairCard(page, "报表卡", 40, 470, 1800, 470, "生产报表（读方案 History\\ 账本）", C_CYAN));
        els.Add(El(page, "生产报表", "Hmi.Report", 60, 516, 1760, 404, 10, new JObject
        {
            ["Mode"] = "Detail",
            ["From"] = "", ["To"] = "",
            ["Grouping"] = "None", ["ShiftStarts"] = "", ["Keyword"] = "",
            ["ReportColumns"] = JsonConvert.SerializeObject(new object[]
            {
                new { Source = "时间", Title = "时间", Format = "HH:mm:ss", Aggregate = 0, Width = 150 },
                new { Source = "综合结果", Title = "综合", Format = "", Aggregate = 0, Width = 80 },
                new { Source = "NCC", Title = "NCC", Format = "", Aggregate = 0, Width = 70 },
                new { Source = "PCB", Title = "PCB", Format = "", Aggregate = 0, Width = 70 },
                new { Source = "标签", Title = "标签", Format = "", Aggregate = 0, Width = 70 },
                new { Source = "条码", Title = "条码", Format = "", Aggregate = 0, Width = 70 },
                new { Source = "二维码", Title = "二维码", Format = "", Aggregate = 0, Width = 80 },
                new { Source = "局部形变", Title = "局部形变", Format = "", Aggregate = 0, Width = 100 },
                new { Source = "平面形变", Title = "平面形变", Format = "", Aggregate = 0, Width = 100 },
                new { Source = "良率", Title = "良率%", Format = "F1", Aggregate = 2, Width = 0 },
            }),
            ["AutoQuery"] = "True", ["MaxRows"] = "200", ["PageSize"] = "50",
            ["AllowExport"] = "True", ["AllowPrint"] = "True", ["ShowToolbar"] = "True",
            ["PageOrientation"] = "Landscape", ["Zebra"] = "True",
            // 浅色主题：报表模板的表头/列头/工具条默认是深色令牌，必须显式覆盖，
            // 否则白卡里横着三条黑带（2026-10-28 用户反馈"列表标题的黑框"）。
            ["Fill"] = "#FFFFFFFF", ["Stroke"] = "#FFE5E7EB", ["CornerRadius"] = "0",
            ["Foreground"] = C_TEXT,
            ["HeaderBackground"] = "#FFF9FAFB", ["HeaderForeground"] = C_TEXT,
            ["ZebraBackground"] = "#FFF3F4F6",
        }));

        return PairHmiPage(page, els);
    }

    // ==================================================================
    //  内容页 7：报警记录
    // ==================================================================
    static JObject PairPageAlarms()
    {
        const string page = "报警记录";
        var els = new JArray();

        PairTitle(els, page, "报警记录", "实时报警 · 报警确认 · 关键计数", C_RED);

        els.Add(PairCard(page, "报警大卡", 40, 130, 1180, 560, "实时报警", C_RED));
        els.Add(El(page, "报警大条", "Hmi.AlarmBanner", 60, 178, 1140, 494, 10, new JObject
        {
            ["MaxRows"] = "12", ["EmptyText"] = "无报警",
            ["NormalColor"] = C_GREEN, ["WarningColor"] = C_AMBER, ["CriticalColor"] = C_RED,
            ["Fill"] = "#FFFFFFFF", ["Stroke"] = "#00000000", ["CornerRadius"] = "0",
        }));

        els.Add(PairCard(page, "报警操作卡", 1240, 130, 600, 560, "操作", C_AMBER));
        // 确认报警保留库里的 danger 红：AckButton 的模板把内层 Button 钉在 AckButtonDangerStyle 上，
        // Fill/Stroke 只是基类外观属性、模板不消费（改颜色只能改库模板）。
        // 而且"确认"确实会清掉当前报警态，红与报警卡同族是库作者的刻意选择，不在这里硬拧。
        els.Add(El(page, "报警确认", "Hmi.AckButton", 1260, 180, 560, 56, 20, new JObject
        {
            ["Scope"] = "All", ["Text"] = "全部确认",
            ["Fill"] = C_RED, ["Stroke"] = "#FF991B1B", ["CornerRadius"] = "8",
            ["Foreground"] = "#FFFFFFFF", ["FontSize"] = "16", ["FontWeight"] = "Bold",
        }));

        string[] counters = { "TotalCount", "OkCount", "NgCount", "YieldRate" };
        string[] labels = { "累计检测", "良品", "不良", "良率 %" };
        for (int i = 0; i < counters.Length; i++)
        {
            els.Add(El(page, "报计_" + counters[i], "Hmi.IOField", 1260, 260 + i * 60, 560, 42, 10, new JObject
            {
                ["Value"] = "0", ["ValueFormat"] = i == 3 ? "F1" : "0",
                ["Unit"] = "", ["Mode"] = "Output", ["Text"] = labels[i],
                ["Foreground"] = "#FF111827", ["FontSize"] = "20", ["TextAlignment"] = "Right",
                ["Fill"] = "#00FFFFFF", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "2",
            }, new JArray { Bind("Value", counters[i]) }));
        }

        // 卡高按 4 行 14px 正文给（标题 38 + 正文 ~112 + 上下留白）；230 会在卡底留一大片空
        els.Add(PairCard(page, "报警说明卡", 40, 710, 1800, 200, "报警规则", C_SLATE));
        els.Add(PairText(page, "报警说明", 65, 758, 1750, 144,
            "· 系统-未就绪（严重）：SystemReady 为假 —— 未执行 Home 或被 End 复位。处置：手动控制页点「置就绪」或运行 Home。\n" +
            "· 质量-良率过低（严重）：YieldRate < 90% —— 批量不良或阈值过严。处置：查看结果统计与各工位判定说明。\n" +
            "· 质量-累计不良超限（警告）：NgCount > 20 —— 连续 NG 或工艺漂移。处置：查看运行日志账本与各工位分数。\n" +
            "报警历史按天落 CSV（方案 Alarms\\ 目录）；确认只清当前态，不清历史。",
            14, C_SUB, "Normal", "Left", 10, "Top"));

        return PairHmiPage(page, els);
    }

    // ==================================================================
    //  内容页 8：运行日志
    // ==================================================================
    static JObject PairPageLogs()
    {
        const string page = "运行日志";
        var els = new JArray();

        PairTitle(els, page, "运行日志", "生产账本（History\\CSV）· 范围筛选与分页 · 关键计数", C_PURPLE);

        els.Add(PairCard(page, "账本卡", 40, 130, 1180, 810, "生产账本", C_CYAN));
        els.Add(El(page, "账本表格", "Hmi.DataGrid", 60, 178, 1140, 744, 10, new JObject
        {
            ["Source"] = "History",
            ["MaxRows"] = "200", ["RowHeight"] = "30",
            ["NewestFirst"] = "True", ["Zebra"] = "True",
            ["GridColumns"] = JsonConvert.SerializeObject(new object[]
            {
                new { Header = "时间", Variable = "时间", Width = 150, Align = "left", Format = "", Monospace = false },
                new { Header = "综合", Variable = "综合结果", Width = 70, Align = "center", Format = "", Monospace = false },
                new { Header = "NCC", Variable = "NCC", Width = 60, Align = "center", Format = "", Monospace = false },
                new { Header = "PCB", Variable = "PCB", Width = 60, Align = "center", Format = "", Monospace = false },
                new { Header = "标签", Variable = "标签", Width = 60, Align = "center", Format = "", Monospace = false },
                new { Header = "条码", Variable = "条码", Width = 60, Align = "center", Format = "", Monospace = false },
                new { Header = "二维码", Variable = "二维码", Width = 70, Align = "center", Format = "", Monospace = false },
                new { Header = "局部形变", Variable = "局部形变", Width = 90, Align = "center", Format = "", Monospace = false },
                new { Header = "平面形变", Variable = "平面形变", Width = 90, Align = "center", Format = "", Monospace = false },
                new { Header = "良率%", Variable = "良率", Width = 0, Align = "right", Format = "F1", Monospace = false },
            }),
            // 浅色主题：模板默认深色令牌，不覆盖就是白卡里的一块黑（2026-10-28 修）。
            // DataGrid 不声明 CornerRadius（模板固定 8），故这里也不写。
            ["Fill"] = "#FFFFFFFF", ["Stroke"] = "#FFE5E7EB",
            ["Foreground"] = C_TEXT,
            ["HeaderBackground"] = "#FFF9FAFB", ["HeaderForeground"] = C_TEXT,
            ["ZebraBackground"] = "#FFF3F4F6",
        }));

        els.Add(PairCard(page, "日志计数卡", 1240, 130, 600, 400, "关键计数", C_BLUE));
        string[] counters = { "TotalCount", "OkCount", "NgCount", "YieldRate" };
        string[] labels = { "累计检测", "良品", "不良", "良率 %" };
        for (int i = 0; i < counters.Length; i++)
        {
            els.Add(El(page, "志计_" + counters[i], "Hmi.IOField", 1260, 182 + i * 62, 560, 44, 10, new JObject
            {
                ["Value"] = "0", ["ValueFormat"] = i == 3 ? "F1" : "0",
                ["Unit"] = "", ["Mode"] = "Output", ["Text"] = labels[i],
                ["Foreground"] = "#FF111827", ["FontSize"] = "15", ["TextAlignment"] = "Right",
                ["Fill"] = "#00FFFFFF", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "2",
            }, new JArray { Bind("Value", counters[i]) }));
        }

        els.Add(PairCard(page, "日志说明卡", 1240, 550, 600, 390, "账本说明", C_SLATE));
        els.Add(PairText(page, "日志说明", 1262, 600, 560, 320,
            "账本路径：方案目录 History\\Main.CSV记录\\yyyy-MM\\yyyy-MM-dd.csv\n\n" +
            "写入时机：主流程「统计与良率」脚本每轮追加一行（11 列）。\n\n" +
            "本表格是只读历史视图：先选范围（最近 7 天 / 昨天 / 今天…）再点「查询」；" +
            "带分页，行高 30px 适配触摸。\n\n" +
            "要导出整表：用「结果统计」页的生产报表（支持导出与打印）。",
            13, C_SUB, "Normal", "Left", 10, "Top"));

        return PairHmiPage(page, els);
    }

    /// <summary>带真实 FlowId 的动作（本方案 FlowID 不是 G("flow:名字")，必须用加载到的真实值）</summary>
    static JObject PairAct(int type, string varName = null, string value = null,
        string pageName = null, string flowId = null, string flowName = null)
    {
        var act = ScadaAct(type, varName: varName, value: value, pageName: pageName);
        if (flowId != null) { act["FlowId"] = flowId; act["FlowName"] = flowName; }
        return act;
    }

    // ------------------------------------------------------------------
    //  自检：JSON 可解析 / 连线目标存在 / 绝对路径清单（已知边界）
    // ------------------------------------------------------------------
    static void PairSelfCheck(string json)
    {
        var root = JObject.Parse(json);
        var flowIds = new HashSet<string>();
        foreach (var f in (JArray)root["Flows"]) flowIds.Add(f["FlowID"]?.Value<string>());

        int linkTotal = 0, badLinks = 0;
        foreach (var f in (JArray)root["Flows"])
        {
            var stepsArr = (JArray)f["Steps"];
            PairCheckLinks(stepsArr, stepsArr, f["FlowName"]?.Value<string>(), flowIds, ref linkTotal, ref badLinks);
        }
        Console.WriteLine($"self-check: links={linkTotal}, broken={badLinks}");

        // 绝对路径盘点（预期：五条文件夹流程 + 四条扩展流程的 FilePath/FolderPath，共 17 处）
        var abs = new System.Text.RegularExpressions.Regex(@"""[^""]*[A-Za-z]:\\\\");
        var hits = abs.Matches(json);
        Console.WriteLine("self-check: absolute-path-like hits = " + hits.Count + "（预期 17：文件夹模式的路径边界）");

        var scada = root["Scada"];
        Console.WriteLine("self-check: pages=" + ((JArray)scada["Pages"]).Count
            + ", startup=" + scada["StartupPageId"]?.Value<string>());
    }

    static void PairCheckLinks(JArray rootSteps, JToken steps, string flowName, HashSet<string> flowIds, ref int total, ref int bad)
    {
        foreach (var s in steps.OfType<JObject>())
        {
            if (s["LinkedSources"] is JObject links)
            {
                foreach (var kv in links.Properties())
                {
                    total++;
                    var link = kv.Value as JObject;
                    var kind = link?["Kind"]?.Value<int>() ?? 0;
                    if (kind == 1)
                    {
                        var target = link["TargetStepId"]?.Value<string>();
                        // 连线可能跨层级（如循环体内的采集步骤连到顶层 For），必须全流程搜索
                        if (!PairContainsStep(rootSteps, target))
                        {
                            bad++;
                            Console.WriteLine("    BROKEN [" + flowName + "] " + kv.Name
                                + " -> " + link["DisplayAddress"]?.Value<string>());
                        }
                    }
                }
            }
            if (s["Children"] is JArray children)
            {
                foreach (var c in children.OfType<JObject>())
                    if (c["Steps"] is JArray inner)
                        PairCheckLinks(rootSteps, inner, flowName, flowIds, ref total, ref bad);
            }
        }
    }

    static bool PairContainsStep(JToken steps, string stepId)
    {
        foreach (var s in steps.OfType<JObject>())
        {
            if (s["StepID"]?.Value<string>() == stepId) return true;
            if (s["Children"] is JArray children)
                foreach (var c in children.OfType<JObject>())
                    if (c["Steps"] is JArray inner && PairContainsStep(inner, stepId)) return true;
        }
        return false;
    }
}
