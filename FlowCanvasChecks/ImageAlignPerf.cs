using System;
using System.Diagnostics;
using System.Threading;
using Core.Interfaces;
using HalconDotNet;
using Plugin.ImageAlign;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 5MP 对齐耗时实测（独立模式，不进常规 gate；正式数字用 Release）。
    /// 测的是整条 RunAlgorithm：点对解析 + 拟合 + projective_trans_image + 有效区 + 端口赋值。
    /// </summary>
    internal static class ImageAlignPerf
    {
        private const int W = 2448;
        private const int H = 2048;

        public static void Run()
        {
            Section("[AlignPerf] 5MP 图像对齐耗时（Release 为正式数字）");

            using HImage reference = MakeReference();
            using HImage current = Warp(reference, HomTranslate(10, 15));

            var plugin = new ImageAlignPlugin { InstanceName = "对齐性能" };
            try
            {
                plugin.Mode = AlignMode.PointPairs;
                plugin.RefPointsText = "40,40; 40,2400; 2000,40; 2000,2400";
                plugin.CurPointsText = "50,55; 50,2415; 2010,55; 2010,2415";
                plugin.Image.Value = current;
                var ctx = NewContext();

                plugin.Execute(ctx);   // warmup
                if (plugin.Success.Value is not true)
                {
                    Check("[AlignPerf] 5MP 对齐 warmup 成功", false,
                        $"Success={plugin.Success.Value} 文案={plugin.ErrorMessage.Value}");
                    return;
                }

                const int reps = 5;
                var times = new double[reps];
                for (int i = 0; i < reps; i++)
                {
                    var sw = Stopwatch.StartNew();
                    plugin.Execute(ctx);
                    sw.Stop();
                    times[i] = sw.Elapsed.TotalMilliseconds;
                    if (plugin.Success.Value is not true)
                    {
                        Check($"[AlignPerf] 第 {i + 1} 轮执行成功", false,
                            $"Success={plugin.Success.Value} 文案={plugin.ErrorMessage.Value}");
                        return;
                    }
                }

                Array.Sort(times);
                double median = times[reps / 2];
                Check($"[AlignPerf] 5MP（{W}×{H}）对齐中位数 {median:F1}ms（min {times[0]:F1} / max {times[reps - 1]:F1}）",
                    median < 10000,
                    $"有效区比例={plugin.ValidAreaRatio.Value}，内点={plugin.InlierCount.Value}，外点={plugin.OutlierCount.Value}");
            }
            finally
            {
                plugin.Dispose();
            }
        }

        private static HImage MakeReference()
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", W, H);
            HObject? baseObj = null;
            HImage? step1 = null;
            HImage? step2 = null;
            HImage? step3 = null;
            try
            {
                HOperatorSet.GenImageProto(proto, out baseObj, 100);
                using HImage baseImage = new HImage(baseObj);
                step1 = StampRect(baseImage, 100, 100, 800, 800, 40);
                step2 = StampRect(step1, 1200, 300, 1900, 1200, 200);
                step3 = StampRect(step2, 500, 1500, 1400, 2300, 220);
                HOperatorSet.SmoothImage(step3, out HObject smooth, "gauss", 5.0);
                return new HImage(smooth);
            }
            finally
            {
                proto.Dispose();
                step1?.Dispose();
                step2?.Dispose();
                step3?.Dispose();
            }
        }

        private static HImage StampRect(HImage canvas, int row1, int col1, int row2, int col2, int gray)
        {
            HOperatorSet.GenRectangle1(out HObject rect, row1, col1, row2, col2);
            try
            {
                HOperatorSet.PaintRegion(rect, canvas, out HObject painted, gray, "fill");
                return new HImage(painted);
            }
            finally { rect.Dispose(); }
        }

        private static HImage Warp(HImage image, HTuple hom)
        {
            HOperatorSet.ProjectiveTransImage(image, out HObject result, hom, "bilinear", "false", "false");
            return new HImage(result);
        }

        private static HTuple HomTranslate(double row, double col)
        {
            HOperatorSet.HomMat2dIdentity(out HTuple hom);
            HOperatorSet.HomMat2dTranslate(hom, row, col, out HTuple result);
            return result;
        }

        private static ExecutionContext NewContext() => new(
            new StubLog(), new FlowSession { FlowName = "对齐性能" },
            new WorkspaceContext(), new CancellationTokenSource().Token);
    }
}
