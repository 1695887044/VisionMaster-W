using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Core.Events;
using HalconDotNet;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
// 裸写 ExecutionContext 会和 System.Threading.ExecutionContext 撞名（同 ExecutionHarness）
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 视觉样例对拍方案的生产运行时复跑（`--sample-flows`）。
    ///
    /// 为什么要有这一份：BlobDetectChecks / CodeReaderChecks 只把**算法步骤**拿出来喂一张图，
    /// 证明不了「For 循环 + 文件夹采集 + 结果显示」这条现场链路。本文件用生产加载器打开
    /// `解决方案\视觉样例对拍\视觉样例对拍.vms`，把 5 条批量流程各整跑一遍，断言：
    ///   · 每圈都加载了**不同**的图（不是同一张跑 N 次）；
    ///   · 每圈算法都出了结果；
    ///   · 每圈「结果显示」都发布了显示帧（窗口 1）；
    ///   · 全程无 Error。
    ///
    /// 真机 UI 复跑与这份断言的分工：UI 证明"窗口真的更新"，本文件证明"每圈数据都对"；
    /// 两者都绿才算交付（R175）。
    /// </summary>
    internal static class SampleFlowsReplayChecks
    {
        private sealed record FlowCase(string Name, int ExpectedCount, string ResultPattern);

        public static void Run()
        {
            Section("[Replay] 视觉样例对拍：生产加载器 → 编译 → 整条流程真跑");

            string repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            string vmsPath = Path.Combine(repoRoot, "解决方案", "视觉样例对拍", "视觉样例对拍.vms");
            Check("样例方案存在（解决方案\\视觉样例对拍\\视觉样例对拍.vms）", File.Exists(vmsPath), vmsPath);
            if (!File.Exists(vmsPath)) return;

            var loaded = new SolutionService().LoadAsync(vmsPath).GetAwaiter().GetResult();
            Check("样例方案可被生产加载器打开", loaded.Success && loaded.Data != null, loaded.Message);
            if (!loaded.Success || loaded.Data == null) return;

            var cases = new[]
            {
                new FlowCase("模板匹配_NCC散焦图", 11, "匹配 1 个实例"),
                new FlowCase("形状匹配_散焦PCB", 121, "匹配 1 个实例"),
                new FlowCase("Blob缺陷检测_标签字符", 10, "缺陷数="),
                new FlowCase("码读取_Code128", 3, "码读取：读到"),
                new FlowCase("二维码读取_QR", 9, "码读取：读到"),
            };

            foreach (var c in cases) RunOne(loaded.Data, c);
        }

        private static void RunOne(SolutionModel solution, FlowCase c)
        {
            Section("[Replay] " + c.Name);

            var flow = solution.Flows.FirstOrDefault(f => f.FlowName == c.Name);
            Check("流程存在且启用", flow != null && flow.IsEnabled, flow == null ? "缺失" : "已启用");
            if (flow == null) return;

            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(solution);
            var compiled = new FlowCompiler(workspace).Compile(flow.Steps.ToArray(), flow.FlowName);
            Check("编译通过", compiled.Success,
                compiled.Success ? "" : string.Join(" | ", compiled.Errors.Select(e => e.Message)));
            if (!compiled.Success) return;

            var session = new FlowSession { FlowName = flow.FlowName, ExecutionEngine = compiled.Data };
            session.AddBlueprintsDeep(flow.Steps);

            var log = new StubLog();
            var frames = new List<ImageDisplayEvent<HImage>>();
            Action<ImageDisplayEvent<HImage>> onFrame = e => frames.Add(e);
            GlobalEventBus.Subscribe(onFrame);
            try
            {
                var ctx = new ExecutionContext(log, session, workspace, CancellationToken.None);
                compiled.Data.Run(ctx);
            }
            finally
            {
                GlobalEventBus.Unsubscribe(onFrame);
            }

            var loads = log.Infos.Where(l => l.Contains("已加载图像")).ToList();
            var results = log.Infos.Where(l => l.Contains(c.ResultPattern)).ToList();
            var files = loads.Select(ExtractPath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Check($"每圈都换图：{c.ExpectedCount} 次加载", loads.Count == c.ExpectedCount, $"实际 {loads.Count}");
            Check($"加载的是不同文件：{c.ExpectedCount} 个", files.Count == c.ExpectedCount, $"不同 {files.Count} 个");
            Check($"每圈都出结果：{c.ExpectedCount} 条（匹配「{c.ResultPattern}」）",
                results.Count == c.ExpectedCount, $"实际 {results.Count}");
            var displayFrames = frames.Where(f => f.Title == "结果显示").ToList();
            Check($"每圈都发布显示帧：{c.ExpectedCount} 帧（窗口 ≥1）",
                displayFrames.Count == c.ExpectedCount && displayFrames.All(f => f.ViewIndex >= 1),
                $"结果显示帧 {displayFrames.Count}（全部帧 {frames.Count}），窗口={string.Join(",", displayFrames.Select(f => f.ViewIndex).Distinct())}");
            Check("全程无 Error 日志", log.Errors.Count == 0, log.Errors.FirstOrDefault() ?? "");
        }

        /// <summary>从「已加载图像 [i/N]: D:\...png」里取路径部分</summary>
        private static string? ExtractPath(string line)
        {
            int i = line.IndexOf("]: ", StringComparison.Ordinal);
            if (i >= 0) return line.Substring(i + 3).Trim();
            i = line.IndexOf("已加载图像:", StringComparison.Ordinal);
            if (i >= 0)
            {
                var rest = line.Substring(i + "已加载图像:".Length).Trim();
                int space = rest.IndexOf(" (", StringComparison.Ordinal);
                return space > 0 ? rest.Substring(0, space) : rest;
            }
            return null;
        }
    }
}
