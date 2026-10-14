using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Core.Events;
using Core.Interfaces;
using HalconDotNet;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 演示方案「解决方案\几何工具演示.vms」的端到端检查（真加载 → 真编译 → 真执行）。
    ///
    /// 为什么必须走真链路：手写/生成的 .vms 里最容易翻车的是——① `$type` 白名单、② 端口名对不上
    /// （如把图像采集输出写成旧版的 "Image" 而非 "OutputImage"）、③ 连线类型不匹配（HRegion→HRegion）、
    /// ④ 动态端口重建时机。这些都只有"真编译"才现形。
    /// 本检查同时钉住"几何查找/几何计算在真图上跑得出结果"：所有步骤 State==Success
    /// 即证明 finder 找到并拟合、relation 算出并判定，全部没走失败路径。
    /// </summary>
    internal static class GeoDemoCheck
    {
        private const string FlowName = "几何工具演示";

        public static void Run()
        {
            Section("[GEODEMO] 演示方案：加载 → 编译 → 执行（几何查找 + 几何计算）");

            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            var vmsPath = Path.Combine(repoRoot, "解决方案", "几何工具演示.vms");

            Check("演示方案文件存在", File.Exists(vmsPath), vmsPath);
            if (!File.Exists(vmsPath)) return;

            // 插件程序集装进默认 ALC（FlowCanvasChecks 的 bin 里没有 Plugin.*.dll）
            var modules = Path.Combine(repoRoot, "Modules");
            var loadError = "";
            foreach (var dll in new[]
            {
                "Plugin.ImageAcquisition.dll", "Plugin.CreateRoi.dll",
                "Plugin.GeometryFinder.dll", "Plugin.GeometryRelation.dll",
            })
            {
                try { Assembly.LoadFrom(Path.Combine(modules, dll)); }
                catch (Exception ex) { loadError += $"{dll}: {ex.Message}; "; }
            }
            Check("采集 / ROI / 几何查找 / 几何计算 四个插件程序集可装载", loadError.Length == 0, loadError);

            var loaded = new SolutionService().LoadAsync(vmsPath).GetAwaiter().GetResult();
            Check("生产加载器能打开这份方案", loaded.Success && loaded.Data != null, loaded.Message);
            if (!loaded.Success || loaded.Data == null) return;

            var solution = loaded.Data;
            var flow = solution.Flows.FirstOrDefault(f => f.FlowName == FlowName);
            Check("方案里能找到目标流程「几何工具演示」", flow != null,
                $"流程={string.Join(",", solution.Flows.Select(f => f.FlowName))}");
            if (flow == null) return;

            var expected = new[]
            {
                "图像采集_角度图", "ROI_横线", "ROI_竖线", "几何查找_横线", "几何查找_竖线",
                "几何计算_夹角", "几何计算_交点", "图像采集_圆图", "几何查找_圆",
            };
            Check("流程步骤依次与预期一致（9 步）",
                flow.Steps.Count == expected.Length && flow.Steps.Select(s => s.StepName).SequenceEqual(expected),
                $"步骤数={flow.Steps.Count}: {string.Join(" → ", flow.Steps.Select(s => s.StepName))}");

            // 连线检查（真编译会做类型校验，这里先给出可读证据）
            var f1 = flow.Steps[3];
            Check("几何查找_横线 的 MaskRegion 连到 ROI_横线.MaskRegion（HRegion→HRegion）",
                f1.LinkedSources.TryGetValue("MaskRegion", out var mk) && mk.TargetStepId == flow.Steps[1].StepID && mk.TargetPortName == "MaskRegion",
                f1.LinkedSources.TryGetValue("MaskRegion", out var m) ? m.DisplayAddress : "未连");
            var c1 = flow.Steps[5];
            Check("几何计算_夹角 的 A/B 连到两个几何查找的 Geometry 输出",
                c1.LinkedSources.TryGetValue("A", out var la) && la.TargetStepId == flow.Steps[3].StepID
                && c1.LinkedSources.TryGetValue("B", out var lb) && lb.TargetStepId == flow.Steps[4].StepID,
                $"A={(c1.LinkedSources.TryGetValue("A", out var a) ? a.DisplayAddress : "未连")}；B={(c1.LinkedSources.TryGetValue("B", out var b) ? b.DisplayAddress : "未连")}");

            // 采集步骤指向仓库内真图（.vms 里写的是绝对路径；这里按仓库根重定位，保证换机也能跑）
            flow.Steps[0].SetInputValue("FilePath", Path.Combine(repoRoot, "Image", "卡尺测试", "5-角度90.png"));
            flow.Steps[7].SetInputValue("FilePath", Path.Combine(repoRoot, "Image", "卡尺测试", "4-圆直径200px.png"));

            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(solution);
            VariablePersistenceService.Restore(solution, workspace);

            var steps = flow.Steps.ToArray();
            var compiled = new FlowCompiler(workspace).Compile(steps, FlowName);
            Check("流程编译通过（端口存在、连线类型匹配）", compiled.Success,
                compiled.Success ? "" : string.Join(" | ", compiled.Errors.Select(e => e.Message)));
            if (!compiled.Success || compiled.Data == null) return;

            var session = new FlowSession { FlowName = FlowName, ExecutionEngine = compiled.Data };
            foreach (var s in steps) session.Blueprints.Add(s);
            var log = new StubLog();
            var ctx = new ExecutionContext(log, session, workspace, new CancellationTokenSource().Token);

            var threw = "";
            try { compiled.Data.Run(ctx); }
            catch (Exception ex) { threw = ex.Message; }

            Check("流程执行无异常", threw.Length == 0, threw);
            var states = steps.Select(s => $"{s.StepName}={s.State}").ToList();
            Check("全部 9 步执行成功（无失败/未执行）",
                steps.All(s => s.State == StepState.Success),
                string.Join(" ", states) + " ｜ Warn=" + string.Join(" | ", log.Warns) + " ｜ Err=" + string.Join(" | ", log.Errors));

            // ---- 直接调插件验数值（与流程独立，确认算出来的数对） ----
            var angleImgPath = Path.Combine(repoRoot, "Image", "卡尺测试", "5-角度90.png");
            HOperatorSet.ReadImage(out HObject angleRaw, angleImgPath);
            using var angleImg = new HImage(angleRaw);
            HOperatorSet.GenRectangle1(out HObject hr, 108, 60, 132, 420);   // 横条
            using var hRegion = new HRegion(hr);
            HOperatorSet.GenRectangle1(out HObject vr, 40, 428, 400, 452);   // 竖条
            using var vRegion = new HRegion(vr);

            var fH = new Plugin.GeometryFinder.GeometryFinderPlugin();
            fH.SrcImage.Value = angleImg;
            fH.MaskRegion.Value = hRegion;
            fH.Kind.Value = Plugin.GeometryFinder.GeometryFindKind.Line;
            fH.RunAlgorithm(null);
            var lineH = fH.Geometry.Value as GeometryParam;

            var fV = new Plugin.GeometryFinder.GeometryFinderPlugin();
            fV.SrcImage.Value = angleImg;
            fV.MaskRegion.Value = vRegion;
            fV.Kind.Value = Plugin.GeometryFinder.GeometryFindKind.Line;
            fV.RunAlgorithm(null);
            var lineV = fV.Geometry.Value as GeometryParam;

            Check("直接调用：横线/竖线各拟合出一条线（Phi≈0° / 90°）",
                lineH != null && lineV != null
                && Math.Abs(lineH.Phi) < 1.0 && Math.Abs(lineV.Phi - 90) < 1.0,
                $"横线={lineH} 竖线={lineV}");

            var rel = new Plugin.GeometryRelation.GeometryRelationPlugin();
            rel.Op.Value = Plugin.GeometryRelation.GeometryOp.Angle;
            rel.A.Value = lineH;
            rel.B.Value = lineV;
            rel.RunAlgorithm(null);
            var angle = Convert.ToDouble(rel.Value.Value);
            Check($"直接调用：两线夹角 = 90°（实测 {angle:0.##}°）", Math.Abs(angle - 90) < 1.0, $"{angle:0.####}");

            HOperatorSet.ReadImage(out HObject circleRaw, Path.Combine(repoRoot, "Image", "卡尺测试", "4-圆直径200px.png"));
            using var circleImg = new HImage(circleRaw);
            var fC = new Plugin.GeometryFinder.GeometryFinderPlugin();
            fC.SrcImage.Value = circleImg;
            fC.Kind.Value = Plugin.GeometryFinder.GeometryFindKind.Circle;
            fC.RunAlgorithm(null);
            var radius = Convert.ToDouble(fC.Radius.Value);
            Check($"直接调用：圆半径 ≈ 100px（实测 {radius:0.##}）", Math.Abs(radius - 100) < 1.0, $"{radius:0.####}");
        }
    }
}
