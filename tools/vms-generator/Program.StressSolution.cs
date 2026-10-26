using System.Text;
using Newtonsoft.Json.Linq;

/// <summary>
/// 压测方案生成（<c>--stress</c>）：造一份"几千个节点"的 .vms，供人工验证流程切换 / 画布
/// 缩放拖拽的体感，也供 <c>FlowCanvasChecks --switch-perf</c> 量化。
///
/// 两条设计决策（都是为了让这份文件"一定能打开"）：
///   ① **所有步骤 IsDisEnable = true**。<c>FlowCompiler.CompileSteps</c> 对禁用步骤直接
///      <c>continue</c>（Engine\FlowCompiler.cs:303），所以条件表达式空着、变量没定义、
///      图片/设备/通讯一个都没有，也照样能编译通过——压测只关心画布，不关心能不能跑。
///   ② **预填 Layout.Nodes**。没坐标就会走 <c>FlowLayoutStore.AutoLayout</c>
///      （FlowCanvasViewModel.cs:862），那是一次性成本，会污染"切流程"的对比；
///      预填后首次打开与热切换同口径。<c>--layout none</c> 可以关掉，专门量 AutoLayout。
///
/// 坐标由本文件自带的递归排位算（常量与 FlowLayoutStore 对齐：NodeWidth/NodeHeight/
/// FramePadding/FrameHeaderHeight/LanePadding/LaneHeaderHeight/RowGap/ColumnGap/MinLaneWidth），
/// 不用引用 VM.Core —— 生成器是 net9.0 控制台工程，引 Core 就得跟着上 WPF 目标框架。
/// </summary>
internal static partial class Program
{
    // 与 Core\Models\FlowLayoutStore.cs:277-318 逐项对齐（改那边要同步这里，否则生成的坐标
    // 与画布实算的框体几何会有系统性偏差，表现为容器框压住下面的兄弟节点）
    const double SNodeW = 156;
    const double SNodeH = 58;
    const double SFramePad = 16;
    const double SFrameHeader = 46;
    const double SLanePad = 14;
    const double SLaneHeader = 28;
    const double SRowGap = 28;
    const double SColGap = 24;
    const double SMinLaneW = 240;
    const double SEmptyLaneW = 240;
    const double SEmptyLaneH = 88;

    static void RunStress(string[] args)
    {
        int nodes = GetOpt(args, "--nodes", 3000);
        int flows = GetOpt(args, "--flows", 10);
        int seed = GetOpt(args, "--seed", 1);
        bool withLayout = !string.Equals(GetStr(args, "--layout", "auto"), "none", StringComparison.OrdinalIgnoreCase);
        // --giant：把全部节点塞进**一条**流程。默认是摊到 N 条流程上（压"切换"），
        // 而"一条流程几千点"压的是单帧渲染与一次重建，两种负载不一样，都要量。
        bool giant = args.Any(a => string.Equals(a, "--giant", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"STRESS nodes={nodes} flows={(giant ? 1 : flows)} seed={seed} layout={(withLayout ? "auto" : "none")} giant={giant}");

        var solution = BuildStressSolution(nodes, giant ? 1 : flows, seed, withLayout, out var stats);

        // 方案名就是文件夹名（SolutionPaths.IsFolderLayout 的判据）：--giant 与普通版
        // 必须不同名，否则两个模式会写进同一个文件夹互相覆盖。
        solution["SolutionName"] = $"压测方案-{nodes}节点" + (giant ? "-单流程" : string.Empty);
        WriteSolutionInFolder(solution);

        Console.WriteLine(
            $"STRESS total_steps={stats.Steps} containers={stats.Containers} lanes={stats.Lanes} " +
            $"layout_entries={stats.LayoutEntries} flows={stats.Flows}");
    }

    static int GetOpt(string[] args, string name, int fallback)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out var v) && v > 0)
                return v;
        return fallback;
    }

    static string GetStr(string[] args, string name, string fallback)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return fallback;
    }

    sealed class StressStats
    {
        public int Steps, Containers, Lanes, LayoutEntries, Flows;
    }

    /// <summary>
    /// 造方案：Home/Main/End 骨架 + N 条普通流程，节点总数逼近 target。
    /// 每条普通流程按"块"循环铺：叶子链 / If / For / While / Parallel 轮流上，
    /// 保证叶子、双分支容器、单分支容器、多泳道四种几何都被覆盖。
    /// </summary>
    static JObject BuildStressSolution(int target, int flowCount, int seed, bool withLayout, out StressStats stats)
    {
        stats = new StressStats();

        var flows = new JArray();
        var rnd = new Random(seed);

        int skeleton = Math.Min(8, Math.Max(3, target / 200));   // 骨架流程每条的步数
        int normalTarget = Math.Max(1, (target - skeleton * 3) / Math.Max(1, flowCount));
        int remaining = target - skeleton * 3;

        var home = StressFlow("Home", 1, skeleton, seed, flows: ref flows, stats, prefix: "h");
        var main = StressFlow("Main", 2, skeleton, seed, flows: ref flows, stats, prefix: "m");
        var end = StressFlow("End", 3, skeleton, seed, flows: ref flows, stats, prefix: "e");

        flows.Insert(0, end);
        flows.Insert(0, main);
        flows.Insert(0, home);

        for (int i = 0; i < flowCount && remaining > 0; i++)
        {
            int want = Math.Min(remaining, normalTarget);
            if (i == flowCount - 1) want = remaining;   // 最后一条吃掉余量，总数才对得上
            remaining -= want;
            flows.Add(StressFlow($"压测_{i + 1:00}", 0, want, seed + i + 1, flows: ref flows, stats, prefix: "n" + i));
        }

        stats.Flows = flows.Count;

        var solution = new JObject
        {
            ["SolutionName"] = $"压测方案-{target}节点",
            ["Version"] = 1.0,
            ["Config"] = new JObject { ["ImageViewMode"] = 0 },
            ["Flows"] = flows,
            ["SolutionId"] = G("stress:solution").ToString(),
            ["CommunicationConfigs"] = new JArray(),
            ["IoBuses"] = new JArray(),
            ["IoDevices"] = new JArray(),
            ["CameraConfigs"] = new JArray(),
            ["MotionCards"] = new JArray(),
            ["CamTables"] = new JArray(),
            ["VariableSnapshots"] = new JArray(),
            ["VariableGroups"] = new JArray(),
            ["WatchItems"] = new JArray(),
            ["Scada"] = new JObject
            {
                ["SchemaVersion"] = 1,
                ["Pages"] = new JArray(),
                ["Alarms"] = new JArray(),
                ["VariableEvents"] = new JArray(),
                ["StartupPageId"] = Guid.Empty.ToString(),
                ["BasePages"] = new JArray(),
            },
        };

        if (withLayout)
        {
            int entries = 0;
            foreach (var flow in flows.OfType<JObject>())
            {
                var nodes = new JObject();
                var placed = new Dictionary<Guid, (double X, double Y)>();
                PlaceLevel((JArray)flow["Steps"]!, 40, 40, placed);
                foreach (var kv in placed)
                {
                    nodes[kv.Key.ToString()] = new JObject
                    {
                        ["X"] = Math.Round(kv.Value.X, 2),
                        ["Y"] = Math.Round(kv.Value.Y, 2),
                        ["Collapsed"] = false,
                    };
                    entries++;
                }
                flow["Layout"] = new JObject { ["Nodes"] = nodes, ["Count"] = entries };
            }
            stats.LayoutEntries = entries;
        }

        return solution;
    }

    /// <summary>一条流程：按块循环铺到 want 步为止（骨架流程用纯叶子链，便于一眼看出是哪条）</summary>
    static JObject StressFlow(string name, int role, int want, int seed, ref JArray flows, StressStats stats, string prefix)
    {
        var steps = new JArray();
        int built = 0;
        int block = 0;

        while (built < want)
        {
            int left = want - built;
            string key = prefix + ":b" + block;

            // 骨架流程（role != 0）只铺叶子，避免压测数据显示成"主流程 300 步嵌套"
            if (role != 0 || left < 6 || block % 5 == 0)
            {
                int n = role != 0 ? Math.Min(left, 4) : Math.Min(left, Math.Min(12, Math.Max(3, left / 3)));
                var leaves = new JArray();
                for (int i = 0; i < n; i++)
                {
                    leaves.Add(LeafStep(key + ":leaf" + i, $"算子_{built + i}", stats));
                    built++;
                }
                ChainLinks(leaves);   // 同一层的相邻叶子接成数据链（真连线，压 BuildLinks）
                foreach (var leaf in leaves) steps.Add(leaf);
            }
            else if (block % 5 == 1)
            {
                var then = new JArray
                {
                    LeafStep(key + ":t1", $"通过_{built}", stats),
                    LeafStep(key + ":t2", $"记录_{built}", stats),
                };
                ChainLinks(then);
                var els = new JArray { LeafStep(key + ":e1", $"异常_{built}", stats) };
                steps.Add(IfContainer(key, $"判断_{built}", then, els, stats));
                built += 3;
            }
            else if (block % 5 == 2)
            {
                var body = new JArray
                {
                    LeafStep(key + ":f1", $"循环体_{built}", stats),
                    LeafStep(key + ":f2", $"累计_{built}", stats),
                };
                ChainLinks(body);
                steps.Add(ForContainer(key, $"循环_{built}", body, stats));
                built += 2;
            }
            else if (block % 5 == 3)
            {
                var body = new JArray { LeafStep(key + ":w1", $"循环体_{built}", stats) };
                steps.Add(WhileContainer(key, $"条件循环_{built}", body, stats));
                built += 1;
            }
            else
            {
                var b1 = new JArray { LeafStep(key + ":p1", $"工位A_{built}", stats) };
                var b2 = new JArray { LeafStep(key + ":p2", $"工位B_{built}", stats) };
                var b3 = new JArray { LeafStep(key + ":p3", $"工位C_{built}", stats) };
                steps.Add(ParallelContainer(key, $"并行_{built}", b1, b2, b3, stats));
                built += 3;
            }

            block++;
        }

        return Flow(name, role switch { 1 => "回原 / 回零（压测）", 2 => "主任务（压测）", 3 => "收尾（压测）", _ => "压测流程" },
            role, 0, 0, steps, enabled: true);
    }

    static Guid StressId(string seed) => G("stress:" + seed);

    /// <summary>叶子步骤：全禁用（见文件头 ①），不再依赖任何插件真实可运行</summary>
    static JObject LeafStep(string seed, string name, StressStats stats)
    {
        var step = Action(seed, name, "常用工具", P_DELAY, "延时", "\uf017", new JObject { ["Delay"] = 10 });
        step["IsDisEnable"] = true;
        stats.Steps++;
        return step;
    }

    /// <summary>
    /// 给一串叶子接成"数据链"：第 i 步的 Delay 输入 ← 第 i-1 步的 ET 输出（都是 int，类型匹配）。
    ///
    /// 为什么压测方案必须带真实连线：`BuildLinks` 走的是完全独立的一条路径
    /// （聚合 → 合法性判定 → 决定画不画线 → 造 VM），没有 LinkedSources 的方案根本测不到它，
    /// 于是"减少连线分配"的改动在这份方案上会呈现为 0 收益（2026-10-23 实测踩过）。
    /// </summary>
    static void ChainLinks(JArray leaves)
    {
        for (int i = 1; i < leaves.Count; i++)
        {
            var prev = (JObject)leaves[i - 1]!;
            var cur = (JObject)leaves[i]!;
            var links = (JObject)cur["LinkedSources"]!;

            // 顺序链的语义是"同一集合里相邻两步"，数据链与之同构：都指向上一步
            links["Delay"] = new JObject
            {
                ["Kind"] = 1,
                ["TargetStepId"] = (string)prev["StepID"]!,
                ["TargetPortName"] = "ET",
                ["TargetVariableId"] = Guid.Empty.ToString(),
                ["DisplayAddress"] = (string)prev["StepName"]! + ".ET",
            };
        }
    }

    static JObject IfContainer(string seed, string name, JArray thenSteps, JArray elseSteps, StressStats stats)
    {
        var step = new JObject
        {
            ["$type"] = "VisionMaster.Models.ConditionStep, VM.Core",
            ["StepID"] = StressId(seed).ToString(),
            ["Icon"] = "\uf0e8",
            ["PluginName"] = "If",
            ["Category"] = "逻辑控制",
            ["PluginTypeName"] = "BuiltIn_If",
            ["StepName"] = name,
            ["Description"] = "条件判断",
            ["SortId"] = 0,
            ["IsDisEnable"] = true,
            ["State"] = 0,
            ["InputValues"] = new JObject(),
            ["LinkedSources"] = new JObject(),
            ["OutputPortDefinitions"] = new JArray(),
            ["LocalVariables"] = new JArray(),
            ["RuntimeVariableRefs"] = new JArray(),
            // Expression 给一个恒真文本：空串会让容器显示"缺条件"红边（StepCollection.IsConditionMissing），
            // 压测图一片红框会掩盖真正的视觉问题。编译不会跑（禁用），所以不考虑可执行性。
            ["Children"] = new JArray
            {
                new JObject { ["Steps"] = thenSteps, ["BranchType"] = 1, ["StepName"] = "If", ["Expression"] = "1 > 0" },
                new JObject { ["Steps"] = elseSteps, ["BranchType"] = 2, ["StepName"] = "Else", ["Expression"] = "" },
            },
        };
        stats.Steps++;
        stats.Containers++;
        stats.Lanes += 2;
        return step;
    }

    static JObject ForContainer(string seed, string name, JArray body, StressStats stats)
    {
        var step = new JObject
        {
            ["$type"] = "VisionMaster.Models.ForStep, VM.Core",
            ["StepID"] = StressId(seed).ToString(),
            ["Icon"] = "\uf0ca",
            ["PluginName"] = "For",
            ["Category"] = "逻辑控制",
            ["PluginTypeName"] = "BuiltIn_For",
            ["StepName"] = name,
            ["Description"] = "按次数循环",
            ["SortId"] = 0,
            ["IsDisEnable"] = true,
            ["State"] = 0,
            ["InputValues"] = new JObject(),
            ["LinkedSources"] = new JObject(),
            ["OutputPortDefinitions"] = new JArray(),
            ["DefaultLoopCount"] = 3,
            ["Children"] = new JArray
            {
                new JObject { ["Steps"] = body, ["BranchType"] = 0, ["StepName"] = "循环体", ["Expression"] = "" },
            },
        };
        stats.Steps++;
        stats.Containers++;
        stats.Lanes += 1;
        return step;
    }

    static JObject WhileContainer(string seed, string name, JArray body, StressStats stats)
    {
        var step = new JObject
        {
            ["$type"] = "VisionMaster.Models.WhileStep, VM.Core",
            ["StepID"] = StressId(seed).ToString(),
            ["Icon"] = "\uf01e",
            ["PluginName"] = "While",
            ["Category"] = "逻辑控制",
            ["PluginTypeName"] = "BuiltIn_While",
            ["StepName"] = name,
            ["Description"] = "条件循环",
            ["SortId"] = 0,
            ["IsDisEnable"] = true,
            ["State"] = 0,
            ["InputValues"] = new JObject(),
            ["LinkedSources"] = new JObject(),
            ["OutputPortDefinitions"] = new JArray(),
            ["LocalVariables"] = new JArray(),
            ["RuntimeVariableRefs"] = new JArray(),
            ["Children"] = new JArray
            {
                // While 的分支类型必须是非 Default（WhileLoop=5），否则画布把循环体当普通分支、
                // 组头显示"缺条件"（Core\Enums.cs:97-111）
                new JObject { ["Steps"] = body, ["BranchType"] = 5, ["StepName"] = "循环体", ["Expression"] = "1 > 0" },
            },
        };
        stats.Steps++;
        stats.Containers++;
        stats.Lanes += 1;
        return step;
    }

    static JObject ParallelContainer(string seed, string name, JArray b1, JArray b2, JArray b3, StressStats stats)
    {
        var branches = new JArray();
        var bodies = new[] { b1, b2, b3 };
        for (int i = 0; i < bodies.Length; i++)
        {
            branches.Add(new JObject
            {
                ["Steps"] = bodies[i],
                ["BranchType"] = 0,
                ["StepName"] = $"分支 {i + 1}",
                ["Expression"] = "",
            });
        }

        var step = new JObject
        {
            ["$type"] = "VisionMaster.Models.ParallelStep, VM.Core",
            ["StepID"] = StressId(seed).ToString(),
            ["Icon"] = "\uf0f7",
            ["PluginName"] = "并行分组",
            ["Category"] = "逻辑控制",
            ["PluginTypeName"] = "BuiltIn_Parallel",
            ["StepName"] = name,
            ["Description"] = "多分支并行执行",
            ["SortId"] = 0,
            ["IsDisEnable"] = true,
            ["State"] = 0,
            ["InputValues"] = new JObject(),
            ["LinkedSources"] = new JObject(),
            ["OutputPortDefinitions"] = new JArray(),
            // ExecutionMode=0（顺序）：=1 会要求分支内每个算子带 [ParallelSafe] 并做变量写冲突检查
            // （Engine\FlowCompiler.cs:908-915）。本文件虽全禁用不编译，但留 0 让"临时启用后自检"也不会炸。
            ["ExecutionMode"] = 0,
            ["FailFastMode"] = 0,
            ["JoinTimeoutMs"] = 0,
            ["Children"] = branches,
        };
        stats.Steps++;
        stats.Containers++;
        stats.Lanes += 3;
        return step;
    }

    // ==================================================================
    //  递归排位（对齐 FlowLayoutStore.AutoLayout 的尺寸感知规则）
    // ==================================================================

    /// <summary>
    /// 铺一层：纵向堆叠，容器递归铺其分支列并按实算框体推进。
    /// 返回本层外接矩形的高度（调用方据此推进 cursor）。
    /// </summary>
    static double PlaceLevel(JArray steps, double x, double y, Dictionary<Guid, (double X, double Y)> into)
    {
        double cursor = y;
        foreach (var step in steps.OfType<JObject>())
        {
            var id = Guid.Parse((string)step["StepID"]!);
            var children = step["Children"] as JArray;

            if (children == null || children.Count == 0)
            {
                into[id] = (x, cursor);
                cursor += SNodeH + SRowGap;
                continue;
            }

            // 容器：先把每个分支的内容铺在自己的列里，再用实算尺寸定框体左上角
            double laneTop = cursor + SFrameHeader + SFramePad;
            double cursorX = x + SFramePad;
            double maxLaneH = 0;
            double frameRight = cursorX;

            for (int b = 0; b < children.Count; b++)
            {
                var branchSteps = (JArray)children[b]!["Steps"]!;
                double laneContentW;
                double laneContentH;

                if (branchSteps.Count == 0)
                {
                    laneContentW = SEmptyLaneW - SLanePad * 2;
                    laneContentH = SEmptyLaneH - SLanePad * 2 - SLaneHeader;
                    frameRight = cursorX + Math.Max(laneContentW + SLanePad * 2, SMinLaneW);
                    maxLaneH = Math.Max(maxLaneH, laneContentH + SLanePad * 2 + SLaneHeader);
                }
                else
                {
                    double before = cursorX + SLanePad;
                    double top = laneTop + SLaneHeader + SLanePad;
                    PlaceLevel(branchSteps, before, top, into);

                    // 内容外接框（按已放好的坐标算，与画布 ComputeGeometry 同一口径）
                    double minX = double.MaxValue, minY = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue;
                    foreach (var so in branchSteps.OfType<JObject>())
                    {
                        if (!into.TryGetValue(Guid.Parse((string)so["StepID"]!), out var p)) continue;
                        minX = Math.Min(minX, p.X);
                        minY = Math.Min(minY, p.Y);
                        maxX = Math.Max(maxX, p.X + SNodeW);
                        maxY = Math.Max(maxY, p.Y + SNodeH);
                    }
                    if (minX == double.MaxValue) { minX = before; minY = top; maxX = before + SNodeW; maxY = top + SNodeH; }

                    laneContentW = maxX - minX;
                    laneContentH = maxY - minY + (SLanePad * 2) + SLaneHeader;
                    frameRight = cursorX + Math.Max(laneContentW + SLanePad * 2, SMinLaneW);
                    maxLaneH = Math.Max(maxLaneH, laneContentH);
                }

                cursorX = frameRight + SColGap;
            }

            double frameW = cursorX - SColGap - x;
            double frameH = maxLaneH + SFramePad * 2 + SFrameHeader;
            into[id] = (x, cursor);
            cursor += frameH + SRowGap;
        }

        return cursor - y;
    }
}
