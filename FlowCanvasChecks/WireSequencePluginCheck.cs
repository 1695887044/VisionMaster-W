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
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 「线序检测·插件版」方案的端到端断言。
    ///
    /// 这一份与 <see cref="WireSequenceCheck"/>（脚本版）是**对照关系**：同一个产品、同样的黄金参照，
    /// 一个跑脚本方案、一个跑插件方案。插件版的意义在于「一条节点顶掉脚本版的六个」：
    ///
    ///     脚本版：图像采集 → 找线 → 选配方(If/ElseIf/Else + 3 个变量赋值) → 比对 → 结果分流
    ///     插件版：图像采集 → 颜色序列检查        ← 找线 + 选配方 + 比对 + 投射 都在这个节点里
    ///
    /// 断言读的是**日志与投射帧**，不是输出端口：端口长在编译后的插件实例上，
    /// 而这条链路（FlowCompiler → ExecutionEngine）不向外暴露实例；日志与帧同样能证明
    /// 「几根、什么颜色、判成什么、画了什么」，且更接近现场看到的东西。
    /// </summary>
    internal static class WireSequencePluginCheck
    {
        internal const string FlowName = "线序检测";

        /// <summary>插件版方案里的配方表（生成器与断言共用同一份，免得两处写法漂移）</summary>
        internal const string RecipeTwo =
            "# 线序配方（井号是注释，空行忽略）\r\n" +
            "5 芯排线  = 黑,棕,玫红,红,黄\r\n" +
            "\r\n" +
            "10 芯排线 = 黑,白,灰,紫,蓝,绿,黄,红,玫红,棕\r\n";

        private const string CollectTypeName =
            "Plugin.ImageAcquisition.ImageAcquisitionPlugin, Plugin.ImageAcquisition, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        private const string ColorCheckTypeName =
            "Plugin.ColorCheck.ColorCheckPlugin, Plugin.ColorCheck, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        private const string Golden5 = "黑,棕,玫红,红,黄";
        private const string Golden10 = "黑,白,灰,紫,蓝,绿,黄,红,玫红,棕";

        public static void Run()
        {
            Section("[R5] 线序检测·插件版方案：加载 → 编译 → 各跑一遍 → 断言结果与投射");

            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            var vmsPath = Path.Combine(repoRoot, "解决方案", "线序检测-插件版.vms");
            var imgDir = Path.Combine(repoRoot, "Image", "颜色");

            Check("方案文件存在", File.Exists(vmsPath), vmsPath);
            if (!File.Exists(vmsPath)) return;

            // 两个插件程序集先装进默认 ALC：本工程的 bin 里没有 Plugin.*.dll，
            // 不装的话编译期 Type.GetType 会找不到类型
            var modules = Path.Combine(repoRoot, "Modules");
            string loadError = "";
            foreach (var dll in new[] { "Plugin.ImageAcquisition.dll", "Plugin.ColorCheck.dll" })
            {
                try { Assembly.LoadFrom(Path.Combine(modules, dll)); }
                catch (Exception ex) { loadError += $"{dll}: {ex.Message}; "; }
            }
            Check("采集 / 颜色序列检查 两个插件程序集可装载", loadError.Length == 0, loadError);

            var loaded = new SolutionService().LoadAsync(vmsPath).GetAwaiter().GetResult();
            Check("生产加载器能打开这份方案", loaded.Success && loaded.Data != null, loaded.Message);
            if (!loaded.Success || loaded.Data == null) return;

            var solution = loaded.Data;
            var flow = solution.Flows.FirstOrDefault();
            Check("方案里只有一条流程，且名为「线序检测」",
                solution.Flows.Count == 1 && flow?.FlowName == FlowName,
                $"流程数={solution.Flows.Count} 名称={flow?.FlowName}");
            if (flow == null) return;

            Check("主干只有两步：图像采集 → 颜色序列检查（一条节点顶掉脚本版那六个）",
                flow.Steps.Count == 2
                && flow.Steps[0].StepName == "图像采集_0" && flow.Steps[0].PluginName == "图像采集"
                && flow.Steps[1].PluginName == "颜色序列检查",
                $"步骤数={flow.Steps.Count}: {string.Join(" → ", flow.Steps.Select(s => s.StepName))}");
            if (flow.Steps.Count < 2) return;

            Check("插件节点的 Image 输入连到采集步骤的 Image 输出",
                flow.Steps[1].GetLinkedAddress("Image") == "图像采集_0.Image",
                flow.Steps[1].GetLinkedAddress("Image") ?? "(未连线)");

            // ---- 10 芯 ----
            var ten = RunOnce(solution, Path.Combine(imgDir, "cable2.png"));
            Check("【10 芯】流程跑完、两步都成功", ten.StepStates.All(s => s.EndsWith("=Success")), ten.Diary);
            Check("【10 芯】日志报出 10 根与黄金序列",
                ten.Infos.Any(l => l.Contains($"找到 10 根：{Golden10}")), ten.Diary);
            Check("【10 芯】判定合格，且结论里带上用了哪条配方",
                ten.Infos.Any(l => l.Contains("判定：线序正确，10 芯") && l.Contains("10 芯排线")), ten.Diary);
            Check("【10 芯】采集投一帧、检查再投一帧，都落在 1 号窗口",
                ten.Frames.Count == 2 && ten.Frames.All(f => f.ViewIndex == 1),
                $"帧数={ten.Frames.Count} 窗口={string.Join(",", ten.Frames.Select(f => f.ViewIndex))}");

            var resultFrame = ten.Frames.LastOrDefault();
            var marks = resultFrame?.Annotations ?? new List<MeasureAnnotation>();
            Check("【10 芯】结果帧带标注：1 条采样线 + 每根 1 个颜色标签 + 1 行结论（10 芯 → 1 线 + 11 文本）",
                marks.Count(m => m.Type == MeasureType.Line) == 1
                && marks.Count(m => m.Type == MeasureType.Text) == 11,
                $"线={marks.Count(m => m.Type == MeasureType.Line)} 文本={marks.Count(m => m.Type == MeasureType.Text)}");
            Check("【10 芯】结论那一行是绿字（合格），且补出来的那根是橙字",
                marks.Any(m => m.Text != null && m.Text.Contains("线序正确") && m.Color == "green")
                && marks.Any(m => m.Text == "3.灰" && m.Color == "orange"),
                string.Join(" | ", marks.Where(m => m.Text != null).Select(m => $"{m.Color}:{m.Text}")));

            // ---- 5 芯：同一条流程、不改任何配置 ----
            var five = RunOnce(solution, Path.Combine(imgDir, "cable1.png"));
            Check("【5 芯】同一条流程不改任何配置就自动换到 5 芯配方并判合格",
                five.StepStates.All(s => s.EndsWith("=Success"))
                && five.Infos.Any(l => l.Contains($"找到 5 根：{Golden5}"))
                && five.Infos.Any(l => l.Contains("判定：线序正确，5 芯") && l.Contains("5 芯排线")),
                five.Diary);

            // ---- 未知芯数：把配方表临时改成只有 5 芯（只在内存里改，不动磁盘） ----
            var unknown = RunOnce(solution, Path.Combine(imgDir, "cable2.png"),
                recipeOverride: "5 芯排线 = 黑,棕,玫红,红,黄\r\n");
            Check("【未知芯数】配方表里没有 10 芯 → 插件步骤失败、流程停住（不是判成 NG）",
                unknown.StepStates.Any(s => s.Contains("颜色序列检查_0") && !s.EndsWith("=Success")),
                unknown.Diary);
            Check("【未知芯数】报错说清「未知产品 + 配方表里有什么」",
                unknown.Messages.Any(l => l.Contains("未知产品") && l.Contains("5 芯排线")),
                unknown.Diary);
        }

        // ==================================================================
        //  驱动：加载后的模型 → 编译 → 跑一趟 → 收日志与投射帧
        // ==================================================================

        private sealed class Outcome
        {
            internal bool Compiled { get; set; }
            internal string CompileErrors { get; set; } = "";
            internal string Threw { get; set; } = "";
            internal List<string> Infos { get; } = new();
            internal List<string> Warns { get; } = new();
            internal List<string> Errors { get; } = new();
            internal List<string> StepStates { get; } = new();
            internal List<ImageDisplayEvent<HImage>> Frames { get; } = new();

            /// <summary>三级日志合起来（查故障时一条也别漏）</summary>
            internal IEnumerable<string> Messages => Infos.Concat(Warns).Concat(Errors);

            internal string Diary => (Compiled ? Threw : CompileErrors)
                + $" ｜ 步骤状态={string.Join(" ", StepStates)}"
                + $" ｜ Warn={string.Join(" | ", Warns)}"
                + $" ｜ Err={string.Join(" | ", Errors)}";
        }

        /// <summary>
        /// 把采集指向指定图，编译、执行一趟，回收日志与预览帧。
        /// recipeOverride 非空时临时改掉插件节点的配方表（只在内存里改，不动磁盘上的 .vms），
        /// 用来逼出"未知产品"那条路 —— 否则它永远测不到。
        /// </summary>
        private static Outcome RunOnce(SolutionModel solution, string imagePath, string? recipeOverride = null)
        {
            var outcome = new Outcome();

            var flow = solution.Flows[0];
            var steps = flow.Steps.ToArray();
            var collect = steps[0];
            var check = steps[1];

            collect.SetInputValue("FilePath", imagePath);
            if (recipeOverride != null) check.SetInputValue("RecipeText", recipeOverride);

            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(solution);

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

            return outcome;
        }
    }
}
