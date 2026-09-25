using System;
using System.IO;
using System.Threading;
using Core.Interfaces;
using HalconDotNet;
using Plugin.Ocr;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// OCR 插件（Plugin.Ocr）的断言。
    ///
    /// 本轮要证明的事
    /// ---------
    /// **拿真实工业图，插件能把字切开、并认对。** 用的图是 HALCON 官方示例图库
    /// （examples\images\ocr），真值由人工读图确定：
    ///   lot_number_01/02/03.png = "11240B"  同一文本、三种取景与曝光
    ///   lot_number_04.png       = "09240A"
    ///   dot_print_02.png        = "041023/A5"（含 '/' —— 不在 0-9A-Z 字符集内）
    ///
    /// 为什么用官方图库而不是仓库自带的图
    /// ---------
    /// 仓库里的 Image\ 是产线自己的图，真值得靠人一帧帧认；官方 OCR 图库是"公认的难例集合"
    /// （点阵喷印、低对比、曲面印刷），拿它验证等于顺便证明了算法不是只对自家样张调出来的。
    ///
    /// 为什么图库找不到就跳过而不是失败
    /// ---------
    /// 这些图属于 HALCON 安装目录，不在仓库里。断言必须能在"没装 HALCON 示例库"的机器上
    /// 干净通过 —— 否则 CI 与同事的机器上会红一片，反而把真问题的信号淹掉。
    /// </summary>
    internal static class OcrChecks
    {
        // ── 各图的人工真值与识别区 ────────────────────────────────────────────
        // 识别区格式 [中心行, 中心列, 角度(弧度), 半长(沿列), 半宽(沿行)]，
        // 与 Plugin.ColorCheck 的采样区同一套约定（phi=0 时长轴沿列方向）。

        /// <summary>lot_number_01/02/03：同一场景三种取景，"11240B" 大致落在同一带内</summary>
        private static readonly double[] RoiLotNumber = { 275.0, 320.0, 0.0, 185.0, 55.0 };

        /// <summary>
        /// lot_number_04："09240A"，字符高约 65px。
        /// 首轮框在 row 300 / 半高 32，裁出来一看**字符顶部被切掉**（字形残缺 → 认成 II1V4WUA 乱码）；
        /// 半高放到 45 又会带进字符下方的面板暗边（多出 4 个区域）。按裁剪图定到 290/42。
        /// </summary>
        private static readonly double[] RoiLotNumber04 = { 290.0, 342.0, 0.0, 175.0, 42.0 };

        /// <summary>
        /// dot_print_02："041023/A5"，字符高约 28px。
        /// 首轮 col 318 / 半宽 160 把首个字符 '0' 切掉了一半 —— 往左挪到 310、放宽到 180；
        /// 半高按实际字符高收到 24，少留空白（空白里的纸纹会被阈值切成假区域）。
        /// </summary>
        private static readonly double[] RoiDotPrint02 = { 355.0, 310.0, 0.0, 180.0, 24.0 };

        /// <summary>
        /// dot_print_01："06:57 MHD 28 03 06"，字符高约 25px，横向几乎占满幅面。
        /// 首轮框在 row 310 完全框空（整块白纸）；裁出来一看真实文本在 row≈200 那一条。
        /// </summary>
        private static readonly double[] RoiDotPrint01 = { 213.0, 320.0, 0.0, 285.0, 30.0 };

        /// <summary>
        /// 点阵字的形态学半径——这批图**全是点阵喷印**（放大看每个字都由小圆点拼成），
        /// 所以它不是可选的优化项，而是必需项。不做形态学时点与点连不上：
        ///   轻则一个字被切成两三个区域（lot_number_04 首轮 6 个字切出 9 个）；
        ///   重则整幅纸面的纹理都被当成分块（dot_print_01 首轮切出 44 个）。
        ///
        /// 用**闭运算**而不是膨胀：两者都能连上散点，但膨胀只扩不缩会让笔画变粗、
        /// 字形失真，分类器随即给出系统性错认（实测 0→A、3→T、5→Q）；
        /// 闭运算先扩后缩，连上了但外轮廓尺寸还原。
        /// 取值参照脚本原型注释：从 1.5 起试，过头会把相邻字符粘成一个。
        ///
        /// 默认值仍是 0（实心印刷字既不需要连接、也不能被外扩）—— 这条正好说明
        /// "分层暴露 + 试算"为什么必要：默认值管得住标准场景，难例必须能看见中间结果再调。
        /// </summary>
        private const double DotMatrixClosing = 2.0;

        public static void Run()
        {
            Section("[OCR] 插件算法：分割 + 识别（真图验证）");

            // ---- ① 内置模型的路径解析：这是"开箱可用"的根据 ----
            bool modelOk = OcrEngine.TryResolveModel(null, out string modelPath, out string modelError);
            Check("内置 OCR 模型按默认路径可解析", modelOk && File.Exists(modelPath),
                modelOk ? modelPath : modelError);

            // ---- ② 显式配了不存在的模型 → 必须报错，不许静默退回内置 ----
            // 这条是刻意的：静默退回会让"填了中文模型却拿 0-9A-Z 分类器在跑"这种问题
            // 一路装成"这 OCR 认不出中文"，现场没有任何线索指向真因。
            bool bogusRejected = !OcrEngine.TryResolveModel(@"C:\不存在的模型.abc", out _, out string bogusError);
            Check("显式配了不存在的模型 → 报错而不是静默退回内置", bogusRejected,
                bogusRejected ? bogusError : "居然返回了成功");

            string? imageDir = ResolveImageDir();
            if (imageDir == null)
            {
                Check("OCR 真图断言（需要 HALCON 官方示例图库）", true,
                    "跳过：未找到 examples\\images\\ocr（HALCONIMAGES 与 HALCONROOT 都没配）");
                return;
            }

            // ---- ③ 同一识别区跨三张取景不同的图："11240B" ----
            // 这三张是同一批次号的三种取景/曝光，用同一个识别区跑它们，
            // 证的是"参数不是对着某一张调出来的"。
            foreach (var fileName in new[] { "lot_number_01.png", "lot_number_02.png", "lot_number_03.png" })
            {
                var r = RunOnce(imageDir, fileName, RoiLotNumber);
                Check($"{fileName} 识别出 11240B（真值读图确认）",
                    r.Text == "11240B",
                    Describe(r));
            }

            // ---- ④ 换一张换文本，证的不是"只会认 11240B" ----
            // 这张字符更大（高约 65px）、点更疏：必须膨胀焊合，且尺寸/面积档按实际字符尺寸收紧，
            // 否则面板暗边会被一起切成区域（首轮就多切出 3 块）。
            var lot04 = RunOnce(imageDir, "lot_number_04.png", RoiLotNumber04, o =>
            {
                o.ClosingRadius = DotMatrixClosing;
                o.MinCharHeight = 25;
                o.MaxCharHeight = 120;
                o.MinCharWidth = 10;
                o.MaxCharWidth = 90;
                o.MinArea = 100;
                o.MaxArea = 6000;
            });
            Check("lot_number_04.png 识别出 09240A", lot04.Text == "09240A", Describe(lot04));
            Check("lot_number_04.png 恰好切出 6 个字符且零拒识（多切=噪声，少切=字符被切断）",
                lot04.Count == 6 && lot04.RejectedCount == 0, Describe(lot04));

            // ---- ⑤ 点阵字的第一道坎：切得对 ----
            // dot_print_02 是 "041023/A5"，喷印在塑料瓶盖的**曲面**上，字符仅约 28px，
            // 而且对比度极低（实测字≈90、底≈150）。
            // 这条只断言**分割**：9 个字符一个不多一个不少。识别结果见下面 ⑥ 与本组「已知边界」——
            // 这两张图当前**认不准**，与其把断言调松掩盖掉，不如把失败面显式钉出来。
            var dot02 = RunOnce(imageDir, "dot_print_02.png", RoiDotPrint02, o =>
            {
                o.ClosingRadius = DotMatrixClosing;
                o.MinCharHeight = 12;
                o.MaxCharHeight = 45;
                o.MinCharWidth = 4;
                o.MaxCharWidth = 35;
                o.MinArea = 25;
                o.MaxArea = 900;
            });
            Check("dot_print_02.png 切出 9 个字符（041023/A5 共 9 个）",
                dot02.Count == 9, Describe(dot02));

            // ---- ⑥ 认不准时，必须"看得出来认不准" ----
            // 这是本组最重要的一条。极低对比 + 曲面 + 小字号下，分类器会大面积拒识、置信度崩到 0.7 上下，
            // 这不奇怪；真正危险的是"给出一串看似正常的错字符却没有任何提示"，
            // 那种结果会被下游当成一次成功识别直接放行。
            // 所以这里钉的是：**认不准必须表现为可观测的低置信度 / 拒识**，
            // 让下游与现场能据此判"本次结果不可信"，而不是无条件相信 Text。
            var dot01 = RunOnce(imageDir, "dot_print_01.png", RoiDotPrint01, o =>
            {
                o.ClosingRadius = DotMatrixClosing;
                o.MinCharHeight = 12;
                o.MaxCharHeight = 45;
                o.MinCharWidth = 4;
                o.MaxCharWidth = 35;
                o.MinArea = 25;
                o.MaxArea = 900;
            });
            Check("dot_print_01.png 不抛异常并给出结果或可读原因（分层参数能被现场用来继续调）",
                dot01.Segmented ? dot01.Count > 0 : !string.IsNullOrEmpty(dot01.Error),
                Describe(dot01));
            Check("【核心】低对比点阵图认不准时给出低置信度（现场据此判不可信）",
                dot01.Segmented && dot02.Confidence < 0.9 && dot01.Confidence < 0.9,
                $"dot02 置信={dot02.Confidence:0.00}，dot01 置信={dot01.Confidence:0.00}");
            Check("【核心】低对比点阵图必须出现拒识，而不是清一色'认出来了'",
                dot01.RejectedCount > 0 || dot02.RejectedCount > 0,
                $"dot02 拒识={dot02.RejectedCount}，dot01 拒识={dot01.RejectedCount}");

            RunPluginLevel(imageDir);
        }

        /// <summary>
        /// 插件壳的断言：端口、配置、完整运行路径、模型路径容错、释放。
        ///
        /// 为什么算法跑通了还要单独测一遍壳：算法正确不等于插件可用 —— 端口类型对不对、
        /// 配置能不能灌进去、模型按**默认路径**找不找得到、句柄有没有释放，这些都在壳上，
        /// 而且只有壳这一层能在真实运行路径（Execute → RunAlgorithm）上被验证。
        /// </summary>
        private static void RunPluginLevel(string imageDir)
        {
            // ---- ① 完整运行路径：默认配置（ModelPath 留空）→ 内置模型 → 识别正确 ----
            var log = new StubLog();
            var plugin = new OcrPlugin { RoiParams = RoiLotNumber, InstanceName = "OCR_0" };
            string? imagePath = Path.Combine(imageDir, "lot_number_01.png");
            HOperatorSet.ReadImage(out HObject raw, imagePath);
            var image = new HImage(raw);
            raw?.Dispose();

            Exception boom = null;
            try
            {
                plugin.Image.Value = image;
                plugin.Execute(MakeContext(log));
            }
            catch (Exception ex) { boom = ex; }

            Check("插件运行不抛异常（Execute 的失败契约兜住了）", boom == null,
                boom == null ? "" : $"{boom.GetType().Name}: {boom.Message}");
            Check("插件按默认配置走内置模型并识别出 11240B",
                plugin.Text.Value as string == "11240B",
                $"Text='{plugin.Text.Value}' Success={plugin.Success.Value} Error='{plugin.ErrorMessage.Value}'");
            Check("插件报成功且字符数为 6",
                plugin.Success.Value is true && Convert.ToInt32(plugin.Count.Value) == 6,
                $"Success={plugin.Success.Value} Count={plugin.Count.Value} Confidence={plugin.Confidence.Value}");

            // ---- ② 认不准时必须留痕：拒识 + 低置信度两条 Warn ----
            // 这是把"诚实机制"从注释变成可验证的行为。没有这两条 Warn，
            // 低质量图会产出一个"看着正常"的错字符串，被下游当成功结果直接放行。
            var logLow = new StubLog();
            var pluginLow = new OcrPlugin
            {
                RoiParams = RoiDotPrint02,
                ClosingRadius = DotMatrixClosing,
                MinCharHeight = 12,
                MaxCharHeight = 45,
                MinCharWidth = 4,
                MaxCharWidth = 35,
                MinArea = 25,
                MaxArea = 900,
                InstanceName = "OCR_低对比",
            };
            HOperatorSet.ReadImage(out HObject rawLow, Path.Combine(imageDir, "dot_print_02.png"));
            var imageLow = new HImage(rawLow);
            rawLow?.Dispose();

            try
            {
                pluginLow.Image.Value = imageLow;
                pluginLow.Execute(MakeContext(logLow));
            }
            catch (Exception ex) { boom = ex; }
            Check("低对比图走插件路径也不抛异常", boom == null,
                boom == null ? "" : $"{boom.GetType().Name}: {boom.Message}");
            Check("【核心】出现拒识字时写 Warn（否则错字符串会被当成功结果放行）",
                logLow.Warns.Exists(w => w.Contains("被拒识")), string.Join(" | ", logLow.Warns));
            Check("【核心】均值置信度偏低时写 Warn 提示结果不可信",
                logLow.Warns.Exists(w => w.Contains("置信度偏低")), string.Join(" | ", logLow.Warns));

            // ---- ③ 模型路径配错 → 明确失败，不许静默退回内置模型 ----
            // 静默退回会让"填了中文模型却拿 0-9A-Z 分类器在跑"一路装成"这 OCR 认不出中文"。
            var pluginBadModel = new OcrPlugin
            {
                RoiParams = RoiLotNumber,
                ModelPath = @"C:\不存在的模型.omc",
                InstanceName = "OCR_坏模型",
            };
            pluginBadModel.Image.Value = image;
            pluginBadModel.Execute(MakeContext(new StubLog()));
            Check("模型路径配错 → 插件报失败且原因可读（不静默退回内置）",
                pluginBadModel.Success.Value is false
                    && (pluginBadModel.ErrorMessage.Value as string ?? "").Contains("不存在"),
                $"Success={pluginBadModel.Success.Value} Error='{pluginBadModel.ErrorMessage.Value}'");

            // ---- ④ 没框识别区 → 明确失败，提示写清"怎么补" ----
            var pluginNoRoi = new OcrPlugin { InstanceName = "OCR_无识别区" };
            pluginNoRoi.Image.Value = image;
            pluginNoRoi.Execute(MakeContext(new StubLog()));
            Check("没框识别区 → 报失败且提示指向右键新建矩形",
                pluginNoRoi.Success.Value is false
                    && (pluginNoRoi.ErrorMessage.Value as string ?? "").Contains("框选识别区"),
                $"Success={pluginNoRoi.Success.Value} Error='{pluginNoRoi.ErrorMessage.Value}'");

            // ---- ⑤ 释放不抛（Halcon 分类器句柄）----
            Exception disposeBoom = null;
            try
            {
                plugin.Dispose();
                pluginLow.Dispose();
                pluginBadModel.Dispose();
                pluginNoRoi.Dispose();
            }
            catch (Exception ex) { disposeBoom = ex; }
            Check("Dispose 释放分类器句柄不抛异常", disposeBoom == null,
                disposeBoom == null ? "" : $"{disposeBoom.GetType().Name}: {disposeBoom.Message}");

            image.Dispose();
            imageLow.Dispose();
        }

        private static ExecutionContext MakeContext(ILogService log) =>
            new(log, new FlowSession { FlowName = "OCR 插件断言" }, new WorkspaceContext(),
                new CancellationTokenSource().Token);

        /// <summary>缺省位置：优先 HALCONIMAGES（HALCON 给示例图库的标准环境变量），再退回 HALCONROOT 下的约定路径</summary>
        private static string? ResolveImageDir()
        {
            string? images = Environment.GetEnvironmentVariable("HALCONIMAGES");
            if (!string.IsNullOrWhiteSpace(images))
            {
                string p = Path.Combine(images!, "ocr");
                if (Directory.Exists(p)) return p;
            }

            string? root = Environment.GetEnvironmentVariable("HALCONROOT");
            if (!string.IsNullOrWhiteSpace(root))
            {
                string p = Path.Combine(root!, "examples", "images", "ocr");
                if (Directory.Exists(p)) return p;
            }

            return null;
        }

        private sealed class Outcome
        {
            public bool Segmented;
            public string Error = string.Empty;
            public string Text = string.Empty;
            public string Detail = string.Empty;
            public int Count;
            public int RejectedCount;
            public double Confidence;
            public double UsedThreshold;
        }

        private static string Describe(Outcome r)
        {
            if (!r.Segmented || !string.IsNullOrEmpty(r.Error))
                return string.IsNullOrEmpty(r.Error) ? "未分割出字符" : r.Error;

            return $"文本='{r.Text}' 字符数={r.Count} 拒识={r.RejectedCount} 均值置信={r.Confidence:0.00} "
                 + $"阈值={r.UsedThreshold:0} 明细={r.Detail}";
        }

        /// <summary>读一张真图，跑一遍「分割 → 识别」</summary>
        private static Outcome RunOnce(
            string imageDir,
            string fileName,
            double[] roi,
            Action<CharSegmenter.Options>? tune = null
        )
        {
            var outcome = new Outcome();
            string path = Path.Combine(imageDir, fileName);
            if (!File.Exists(path))
            {
                outcome.Error = "测试图不存在：" + path;
                return outcome;
            }

            HOperatorSet.ReadImage(out HObject raw, path);
            var image = new HImage(raw);
            raw?.Dispose();

            var engine = new OcrEngine();
            try
            {
                var options = new CharSegmenter.Options();
                tune?.Invoke(options);

                if (!CharSegmenter.TrySegment(image, roi, options, out var seg, out string segError))
                {
                    outcome.Error = segError;
                    return outcome;
                }

                using (seg!)
                {
                    outcome.Segmented = true;
                    outcome.Count = seg!.Count;
                    outcome.UsedThreshold = seg.UsedThreshold;

                    if (!OcrEngine.TryResolveModel(null, out string modelPath, out string modelError))
                    {
                        outcome.Error = modelError;
                        return outcome;
                    }

                    if (!engine.TryRecognize(seg, modelPath, out var rec, out string recError))
                    {
                        outcome.Error = recError;
                        return outcome;
                    }

                    outcome.Text = rec!.Text;
                    outcome.Detail = rec.Detail;
                    outcome.Confidence = rec.AverageConfidence;
                    outcome.RejectedCount = rec.RejectedCount;
                }
            }
            catch (Exception ex)
            {
                outcome.Error = $"抛出异常（不应发生）：{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                engine.Dispose();
                image.Dispose();
            }

            return outcome;
        }
    }
}
