using System.IO;
using System.Text;
using Core.Interfaces;
using Newtonsoft.Json;
using Plugin.BeadInspect;
using VisionMaster;            // FlowTriggerMode
using VisionMaster.Models;     // SolutionModel / FlowModel / ActionStep / FlowSession
using VisionMaster.Services;   // SolutionService / FlowCompiler / WorkspaceContext / ExecutionContext
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace SolutionProbe;

/// <summary>
/// 胶路检测 · 方案生成与验证台（临时脚手架）。
///
/// 产出：解决方案\胶路检测演示.vms —— 用户在宿主里"打开方案"即可运行。
/// 验证：宿主序列化器生成 → LoadAsync 回读 → FlowCompiler 编译 → 按 FileIndex 0..7 逐张运行，
///       从编译后实例的输出端口读 IsOk / ErrorCount / NgReason，与探针 P4 真值表对照。
/// </summary>
internal static class Program
{
    private const string BeadDir = @"D:\C#\VM\Image\bead";
    private const string VmsPath = @"D:\C#\VM\解决方案\胶路检测演示.vms";
    private const string OutFile = @"D:\C#\VM\tools\SolutionProbe\probe_result.txt";

    private static readonly StringBuilder L = new();

    private static void Main()
    {
        MainAsync().GetAwaiter().GetResult();
        try { File.WriteAllText(OutFile, L.ToString(), new UTF8Encoding(false)); } catch { }
    }

    private static async Task MainAsync()
    {
        try
        {
            RunProbe();
            await VerifyAsync();
        }
        catch (Exception ex)
        {
            L.AppendLine("[FAIL] 异常: " + ex.GetType().Name + ": " + ex.Message);
            if (ex.StackTrace != null) L.AppendLine(ex.StackTrace);
        }
    }

    private static SolutionModel? _loaded;

    private static void RunProbe()
    {
        L.AppendLine("===== 胶路检测演示方案 · 生成与全链路验证 =====");
        L.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        // ── 1. 构建方案模型（配方 = 断言同款"范例"：14 控制点折线 + 探针矫正四点 + 15/8/30/dark）──
        var solution = BuildSolution();

        // ── 2. 用宿主自己的序列化器落盘（不手写 JSON）──
        var json = SolutionService.Serialize(solution);
        Directory.CreateDirectory(Path.GetDirectoryName(VmsPath)!);
        File.WriteAllText(VmsPath, json, new UTF8Encoding(false));
        L.AppendLine($"[1] 方案已生成: {VmsPath}  ({json.Length} 字符)");
    }

    private static async Task VerifyAsync()
    {
        // ── 3. 回读（宿主 LoadAsync 路径）──
        var loader = new SolutionService();
        var loaded = await loader.LoadAsync(VmsPath);
        if (loaded is { Success: true, Data: not null })
        {
            _loaded = loaded.Data;
            var f = _loaded.Flows[0];
            L.AppendLine($"[2] 回读成功: 方案「{_loaded.SolutionName}」 流程「{f.FlowName}」 步骤数={f.Steps.Count}");
            foreach (var s in f.Steps)
                L.AppendLine($"    步骤: PluginName=「{s.PluginName}」 StepName=「{s.StepName}」 类型={s.PluginTypeName.Split(',')[0]}");
        }
        else
        {
            L.AppendLine($"[2] [FAIL] 回读失败: {loaded?.Message}");
            return;
        }

        // ── 4. 逐 FileIndex 编译 + 运行，读输出端口 ──
        // 期望真值表（探针 P4，MinErrorLength=0 口径）：
        // 01→0段OK 02→0段OK 03→2段(太细1+偏移1) 04→0段OK 05→3段(缺胶1+太细1+太粗1) 06→1段(缺胶1) 07→4段(偏移3+缺胶1)
        var expected = new Dictionary<string, (int seg, string kind)>
        {
            ["01"] = (0, "OK"),
            ["02"] = (0, "OK"),
            ["03"] = (2, "太细1+偏移1"),
            ["04"] = (0, "OK"),
            ["05"] = (3, "缺胶1+太细1+太粗1"),
            ["06"] = (1, "缺胶1"),
            ["07"] = (4, "偏移3+缺胶1"),
        };

        var workspace = new WorkspaceContext();
        workspace.SwitchSolution(_loaded);
        var flow = _loaded.Flows.FirstOrDefault(f => f.Steps.Any(s => s.PluginTypeName.Contains("BeadInspect")));
        if (flow == null) { L.AppendLine("[FAIL] 回读方案里找不到胶路检测流程"); return; }
        // 按类型名找步骤（不按 PluginName——显示名可能与加载后的值有出入）
        var acqStep = flow.Steps.First(s => s.PluginTypeName.Contains("ImageAcquisition"));
        var beadStep = flow.Steps.First(s => s.PluginTypeName.Contains("BeadInspect"));

        L.AppendLine();
        L.AppendLine("[3] 逐 FileIndex 编译+运行（图像采集.文件夹模式）：");
        L.AppendLine("     索引 | 采集到的文件                                | 判定 | 段数 | NgReason");

        int pass = 0, fail = 0;
        for (int idx = 0; idx <= 7; idx++)
        {
            acqStep.SetInputValue("FileIndex", idx);
            var compiled = new FlowCompiler(workspace).Compile(flow.Steps, flow.FlowName);
            if (!compiled.Success)
            {
                L.AppendLine($"     {idx,4} | [FAIL] 编译失败: " + string.Join(" | ", compiled.Errors.Select(e => e.Message)));
                fail++;
                continue;
            }

            var log = new StubLog();
            var session = new FlowSession { FlowName = flow.FlowName, ExecutionEngine = compiled.Data };
            foreach (var s in flow.Steps) session.Blueprints.Add(s);
            var ctx = new ExecutionContext(log, session, workspace, new CancellationTokenSource().Token);

            try { compiled.Data.Run(ctx); }
            catch (Exception ex) { L.AppendLine($"     {idx,4} | [FAIL] 运行抛出: {ex.Message}"); fail++; continue; }

            var acqPlugin = (Plugin.ImageAcquisition.ImageAcquisitionPlugin)compiled.Data.PluginLookup[acqStep.StepID];
            var beadPlugin = (BeadInspectPlugin)compiled.Data.PluginLookup[beadStep.StepID];

            var file = acqPlugin.CurrentFilePath.TypedValue ?? "(无)";
            var name = Path.GetFileName(file);
            bool ok = beadPlugin.IsOk.TypedValue;
            int seg = beadPlugin.ErrorCount.TypedValue;
            string ng = beadPlugin.NgReason.TypedValue ?? "";

            // 真值对照（按文件名后缀匹配：adhesive_bead_01 → "01"）
            var stem = Path.GetFileNameWithoutExtension(name);
            var key = stem.Replace("adhesive_bead_", "");
            string verdict;
            if (name.Contains("ref"))
                verdict = "参考图（无胶，预期 NG 缺胶）";
            else if (expected.TryGetValue(key, out var exp))
            {
                bool segOk = seg == exp.seg;
                bool okOk = ok == (exp.seg == 0);
                verdict = segOk && okOk ? "✔ 与真值表一致" : $"✗ 真值表期望 段数={exp.seg}({exp.kind})";
                if (segOk && okOk) pass++; else fail++;
            }
            else
                verdict = "(无真值对照)";

            L.AppendLine($"     {idx,4} | {name,-43} | {(ok ? "OK " : "NG ")} | {seg,4} | {ng}  {verdict}");
        }

        L.AppendLine();
        L.AppendLine($"===== 真值对照: 通过 {pass} / 失败 {fail} =====");
        if (fail == 0 && pass >= 7)
            L.AppendLine("[OK] 方案文件可被宿主加载、编译、运行，检出与探针真值表一致。");
        else
            L.AppendLine("[FAIL] 存在与真值表不一致的检出，方案不可交付。");
    }

    private static SolutionModel BuildSolution()
    {
        var acqId = Guid.NewGuid();
        var beadId = Guid.NewGuid();

        var acq = new ActionStep(
            icon: "",
            pluginName: "图像采集",
            pluginTypeName: "Plugin.ImageAcquisition.ImageAcquisitionPlugin, Plugin.ImageAcquisition, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            stepName: "图像采集_0")
        {
            StepID = acqId,
            Description = "图像采集",
            SortId = 0,
        };
        acq.SetInputValue("Mode", "Folder");
        acq.SetInputValue("FolderPath", BeadDir);
        acq.SetInputValue("FileIndex", 0);
        acq.SetInputValue("DisplayViewIndex", 1);

        // 配方（与 FlowCanvasChecks\BeadInspectChecks.cs 的 BuildTruthRecipe 同源同值）
        double[][] pts =
        {
            new[]{ 701.767, 319.24 },  new[]{ 626.953, 336.133 }, new[]{ 538.867, 367.507 },
            new[]{ 443.54, 431.46 },   new[]{ 390.447, 489.38 },  new[]{ 360.28, 546.093 },
            new[]{ 354.247, 646.247 }, new[]{ 363.9, 722.267 },   new[]{ 400.1, 776.567 },
            new[]{ 458.02, 826.04 },   new[]{ 509.907, 869.48 },  new[]{ 588.34, 912.92 },
            new[]{ 659.533, 934.64 },  new[]{ 696.94, 929.813 },
        };
        var entry = new BeadRecipeEntry
        {
            Name = "范例",
            RefImagePath = Path.Combine(BeadDir, "adhesive_bead_ref.png"),
            RefNoBeadImagePath = Path.Combine(BeadDir, "adhesive_bead_ref.png"), // 无胶参考图 = ref 本身（差分提取用）
            RefPointsJson = JsonConvert.SerializeObject(pts),
            RectifyQuadJson = JsonConvert.SerializeObject(new
            {
                src = new double[][]
                {
                    new[]{ 658.232, 330.071 }, new[]{ 291.923, 316.617 },
                    new[]{ 314.817, 971.032 }, new[]{ 691.533, 947.008 },
                },
                dst = new double[][]
                {
                    new[]{ 708.0, 300.0 }, new[]{ 300.0, 300.0 },
                    new[]{ 300.0, 948.0 }, new[]{ 708.0, 948.0 },
                },
            }),
            TargetWidth = 15,   // P3 实测 15.26
            WidthTolerance = 8, // P12：范例的 7 照搬会过报
            PositionTolerance = 30,
            Polarity = "dark",
        };

        var bead = new ActionStep(
            icon: "",
            pluginName: "胶路检测",
            pluginTypeName: "Plugin.BeadInspect.BeadInspectPlugin, Plugin.BeadInspect, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            stepName: "胶路检测_0")
        {
            StepID = beadId,
            Description = "胶路检测",
            SortId = 1,
        };
        bead.SetInputValue("RecipeLibraryJson", JsonConvert.SerializeObject(new[] { entry }));
        bead.SetInputValue("DefaultRecipeName", "范例");
        bead.SetInputValue("AlignMode", "PlanarDeformable");
        bead.SetInputValue("Polarity", "Dark");
        bead.SetInputValue("TargetWidth", 15.0);
        bead.SetInputValue("WidthTolerance", 8.0);
        bead.SetInputValue("PositionTolerance", 30.0);
        bead.SetInputValue("MinErrorLength", 0.0); // 演示口径：与探针真值表逐段一致；生产建议调回 5 滤碎段
        bead.SetInputValue("MinScore", 0.4);
        bead.SetInputValue("PlanarNumLevels", 5);
        bead.SetInputValue("DisplayViewIndex", 1);
        bead.SetInputValue("OutputAlignedImage", true);
        bead.SetInputValue("UnitOutput", "Pixel");
        bead.SetInputValue("PixelSizeMm", 0.0);
        bead.LinkedSources["SrcImage"] =
            new LinkReference(LinkKind.StepPort, acqId, "Image", "图像采集_0.Image");

        var flow = new FlowModel
        {
            FlowID = Guid.NewGuid().ToString("N"),
            FlowName = "胶路检测",
            Version = 4,
            IsEnabled = true,
            InvokeType = 0,
            TimerIntervalMs = 0,
            StepsEncrypted = false,
            TriggerMode = FlowTriggerMode.Single, // 手动单次：逐张换 FileIndex 检查；产线可切连续
        };
        flow.Steps.Add(acq);
        flow.Steps.Add(bead);

        var solution = new SolutionModel { SolutionName = "胶路检测演示", Version = 1.0 };
        // SolutionModel 构造时预置「GoHome 回原 / MainTask 主任务」两个空流程——
        // 演示方案只留胶路检测一条，避免用户打开后看到两个空流程
        solution.Flows.Clear();
        solution.Flows.Add(flow);
        return solution;
    }

    /// <summary>断言用最小日志桩（ILogService 全成员收集）</summary>
    private sealed class StubLog : ILogService
    {
        public List<string> All { get; } = new();
        public void Success(params string[] messages) { foreach (var m in messages) All.Add("S: " + m); }
        public void Error(params Exception[] messages) { foreach (var m in messages) All.Add("E: " + m.Message); }
        public void Error(params string[] messages) { foreach (var m in messages) All.Add("E: " + m); }
        public void Info(params string[] messages) { foreach (var m in messages) All.Add("I: " + m); }
        public void Warn(params string[] messages) { foreach (var m in messages) All.Add("W: " + m); }
    }
}
