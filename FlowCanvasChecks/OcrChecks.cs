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
                // 端口契约不依赖真图，别跟着一起跳掉
                RunStructuredOutputChecks(null);
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
            RunStructuredOutputChecks(imageDir);
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

        // ══════════════════════════════════════════════════════════════════
        //  结构化输出端口：Classes / Confidences / RejectedCount / CharBoxes
        //
        //  这一组钉的不是"认不认得出字"，而是"交给下游的数据自不自洽"：
        //  类型定死没有、四个端口彼此对不对齐、拒识怎么表达、失败轮会不会把上一轮的值漏给下游。
        //  判定（OK/NG）不归本插件 —— 这些端口只提供数据。
        // ══════════════════════════════════════════════════════════════════

        /// <summary>一次插件级结构化运行的快照（端口值原样取出，断言只读它）</summary>
        private sealed class StructuredRun
        {
            public bool Ok;
            public string Error = string.Empty;
            public string Text = string.Empty;
            public string Detail = string.Empty;
            public string[] Classes = Array.Empty<string>();
            public double[] Confidences = Array.Empty<double>();
            public double[] Boxes = Array.Empty<double>();
            public int Count;
            public int RejectedCount;
            public int ImageWidth;
            public int ImageHeight;

            public string Describe()
            {
                if (!Ok) return Error;

                string boxes = string.Empty;
                for (int i = 0; i + 3 < Boxes.Length; i += 4)
                {
                    if (boxes.Length > 0) boxes += " ";
                    boxes += $"[{Boxes[i]:0},{Boxes[i + 1]:0},{Boxes[i + 2]:0}x{Boxes[i + 3]:0}]";
                }

                return $"文本='{Text}' 字符数={Count} 拒识={RejectedCount} 图={ImageWidth}x{ImageHeight} "
                     + $"Classes={Classes.Length} Confidences={Confidences.Length} CharBoxes={Boxes.Length} "
                     + $"框={boxes}";
            }
        }

        /// <summary>
        /// 结构化输出端口的断言。
        /// <paramref name="imageDir"/> 为 null 时只跑不依赖真图的端口契约 —— 端口类型跟图库无关，
        /// 不该因为"这台机器没装 HALCON 示例图"就一起不查了。
        /// </summary>
        private static void RunStructuredOutputChecks(string? imageDir)
        {
            Section("[OCR] 结构化输出端口：逐字符类别 / 置信度 / 拒识数 / 字符框");

            // ---- ⑥ 端口契约：类型定死，改类型等于改下游全部取值代码 ----
            var probe = new OcrPlugin { InstanceName = "OCR_端口契约" };
            bool contractOk =
                probe.Classes.DataType == typeof(string[])
                && probe.Confidences.DataType == typeof(HTuple)
                && probe.RejectedCount.DataType == typeof(int)
                && probe.CharBoxes.DataType == typeof(HTuple);
            Check("端口契约：Classes=string[] / Confidences=HTuple / RejectedCount=int / CharBoxes=HTuple",
                contractOk,
                $"Classes={probe.Classes.DataType.Name} Confidences={probe.Confidences.DataType.Name} "
                + $"RejectedCount={probe.RejectedCount.DataType.Name} CharBoxes={probe.CharBoxes.DataType.Name}");
            probe.Dispose();

            if (imageDir == null) return; // 下面的真图断言需要 HALCON 官方示例图库

            // ---- ① 11240B：四个端口与 Count 逐项对齐 ----
            var main = RunStructured(imageDir, "lot_number_01.png", RoiLotNumber, null);

            Check("结构化端口与 Count 对齐：Classes/Confidences 长度 = Count，CharBoxes 长度 = Count×4",
                main.Ok && main.Count == 6
                    && main.Classes.Length == main.Count
                    && main.Confidences.Length == main.Count
                    && main.Boxes.Length == main.Count * 4,
                main.Describe());

            // ---- ② Classes 拼起来 == Text：逐字符与整串同源 ----
            // 若哪天有人给 Text 单独拼一遍（或给 Classes 换一套来源），这条立刻红。
            string joined = string.Concat(main.Classes);
            Check("Classes 直接拼接 == Text（逐字符与整串同源，不是两份数据）",
                main.Ok && joined == main.Text,
                $"拼接='{joined}' Text='{main.Text}'");

            // ---- ③ 置信度逐项落在 0~1 ----
            bool confInRange = main.Ok && main.Confidences.Length > 0;
            for (int i = 0; i < main.Confidences.Length && confInRange; i++)
                if (!(main.Confidences[i] >= 0 && main.Confidences[i] <= 1)) confInRange = false;

            Check("Confidences 逐项落在 0~1（长度由对齐那条钉住）", confInRange,
                main.Ok ? "置信度=" + JoinDoubles(main.Confidences) : main.Error);

            // ---- ④ 零拒识：11240B 全是字符集内的字 ----
            int subInMain = CountSub(main.Classes);
            Check("11240B 零拒识：RejectedCount = 0 且 Classes 里没有 U+001A",
                main.Ok && main.RejectedCount == 0 && subInMain == 0,
                $"RejectedCount={main.RejectedCount} U+001A 个数={subInMain}");

            // ---- ⑤ 字符框几何：宽高 > 0、落在图内、按阅读顺序（横排列递增）----
            bool boxShape = main.Ok && main.Count > 0 && main.Boxes.Length == main.Count * 4;
            bool boxInside = boxShape;
            bool boxOrder = boxShape;
            double prevCenterCol = double.NegativeInfinity;

            for (int i = 0; i < main.Count && boxShape; i++)
            {
                double row1 = main.Boxes[i * 4 + 0];
                double col1 = main.Boxes[i * 4 + 1];
                double w = main.Boxes[i * 4 + 2];
                double h = main.Boxes[i * 4 + 3];

                if (!(w > 0) || !(h > 0)) boxShape = false;

                if (row1 < -0.5 || col1 < -0.5
                    || row1 + h > main.ImageHeight + 0.5 || col1 + w > main.ImageWidth + 0.5)
                    boxInside = false;

                double centerCol = col1 + (w - 1) / 2.0;
                if (!(centerCol > prevCenterCol)) boxOrder = false;
                prevCenterCol = centerCol;
            }

            Check("每个字符框宽高 > 0 且落在图像范围内（像素含端点，单像素宽的字也是 1）",
                boxShape && boxInside, main.Describe());
            Check("字符框按阅读顺序排列（横排：框中心列严格递增）",
                boxOrder, main.Describe());

            // ---- ④b 低对比点阵图：拒识数怎么算 ----
            // 这两张图**允许分割失败**（既有断言的口径是"给出结果或可读原因"），所以一致性只核"跑出结果的那张"；
            // "确有拒识"仍要求两张合计 > 0 —— 与既有第 12 条同口径，不额外收紧。
            var dot02 = RunStructured(imageDir, "dot_print_02.png", RoiDotPrint02, TuneDotMatrix);
            var dot01 = RunStructured(imageDir, "dot_print_01.png", RoiDotPrint01, TuneDotMatrix);

            int sub02 = CountSub(dot02.Classes);
            int sub01 = CountSub(dot01.Classes);
            int rejectedTotal = (dot02.Ok ? dot02.RejectedCount : 0) + (dot01.Ok ? dot01.RejectedCount : 0);

            bool rejectConsistent = true;
            if (dot02.Ok && dot02.RejectedCount != sub02) rejectConsistent = false;
            if (dot01.Ok && dot01.RejectedCount != sub01) rejectConsistent = false;

            Check("【核心】拒识数 == Classes 里 U+001A 的个数（低对比点阵图确有拒识）",
                rejectConsistent && rejectedTotal > 0,
                $"dot02 拒识={dot02.RejectedCount}/U+001A={sub02}（{(dot02.Ok ? "已出结果" : "未出结果")}），"
                + $"dot01 拒识={dot01.RejectedCount}/U+001A={sub01}（{(dot01.Ok ? "已出结果" : "未出结果")}）");

            // ---- ④c 拒识位在 Detail 里显示 '?'（且只有拒识位是 '?'）----
            bool detailOk = true;
            if (dot02.Ok && !DetailMarksRejectedAsQuestion(dot02.Detail, dot02.Classes)) detailOk = false;
            if (dot01.Ok && !DetailMarksRejectedAsQuestion(dot01.Detail, dot01.Classes)) detailOk = false;

            Check("拒识位在 Detail 里是 '?'，非拒识位不是 '?'（逐位对照 Classes）",
                detailOk && rejectedTotal > 0,
                $"dot02 明细='{dot02.Detail}'；dot01 明细='{dot01.Detail}'");

            // ---- ⑤b 反向：失败轮不许把上一轮的值留给下游 ----
            // 同一个实例先跑成功、再清掉识别区（必然失败），四个新端口必须回到空 ——
            // 否则下游在失败轮读到的是上一帧的字符，比"没有值"危险得多。
            var pluginResidue = new OcrPlugin { RoiParams = RoiLotNumber, InstanceName = "OCR_残留" };
            HOperatorSet.ReadImage(out HObject rawResidue, Path.Combine(imageDir, "lot_number_01.png"));
            var imageResidue = new HImage(rawResidue);
            rawResidue?.Dispose();

            int firstCount = 0;
            Exception residueBoom = null;
            try
            {
                pluginResidue.Image.Value = imageResidue;
                pluginResidue.Execute(MakeContext(new StubLog()));
                firstCount = Convert.ToInt32(pluginResidue.Count.Value);

                pluginResidue.RoiParams = Array.Empty<double>(); // 不框识别区 → 必然失败
                pluginResidue.Execute(MakeContext(new StubLog()));
            }
            catch (Exception ex) { residueBoom = ex; }

            bool allEmpty =
                (pluginResidue.Classes.TypedValue?.Length ?? 0) == 0
                && (pluginResidue.Confidences.TypedValue?.Length ?? 0) == 0
                && (pluginResidue.CharBoxes.TypedValue?.Length ?? 0) == 0
                && Convert.ToInt32(pluginResidue.RejectedCount.Value) == 0
                && Convert.ToInt32(pluginResidue.Count.Value) == 0;

            Check("【反向】失败轮四个新端口全空、Count = 0（下游读不到上一轮残留）",
                residueBoom == null && firstCount > 0 && pluginResidue.Success.Value is false && allEmpty,
                $"第一轮 Count={firstCount} 第二轮 Success={pluginResidue.Success.Value} "
                + $"Count={pluginResidue.Count.Value} Classes={pluginResidue.Classes.TypedValue?.Length} "
                + $"Confidences={pluginResidue.Confidences.TypedValue?.Length} "
                + $"CharBoxes={pluginResidue.CharBoxes.TypedValue?.Length} "
                + $"Rejected={pluginResidue.RejectedCount.Value}"
                + (residueBoom == null ? "" : $" 异常={residueBoom.GetType().Name}: {residueBoom.Message}"));

            pluginResidue.Dispose();
            imageResidue.Dispose();
        }

        /// <summary>与 Run() 里 dot_print 两张图的用例保持完全相同的参数（改一处就要改两处，故意的）</summary>
        private static void TuneDotMatrix(OcrPlugin plugin)
        {
            plugin.ClosingRadius = DotMatrixClosing;
            plugin.MinCharHeight = 12;
            plugin.MaxCharHeight = 45;
            plugin.MinCharWidth = 4;
            plugin.MaxCharWidth = 35;
            plugin.MinArea = 25;
            plugin.MaxArea = 900;
        }

        /// <summary>跑一次插件级"分割 → 识别"，把四个结构化端口的原值取出来</summary>
        private static StructuredRun RunStructured(
            string imageDir,
            string fileName,
            double[] roi,
            Action<OcrPlugin>? tune)
        {
            var run = new StructuredRun();
            string path = Path.Combine(imageDir, fileName);
            if (!File.Exists(path))
            {
                run.Error = "测试图不存在：" + path;
                return run;
            }

            var plugin = new OcrPlugin { RoiParams = roi, InstanceName = "OCR_结构化" };
            tune?.Invoke(plugin);

            HOperatorSet.ReadImage(out HObject raw, path);
            var image = new HImage(raw);
            raw?.Dispose();

            try
            {
                HOperatorSet.GetImageSize(image, out HTuple imgW, out HTuple imgH);
                run.ImageWidth = imgW.I;
                run.ImageHeight = imgH.I;

                plugin.Image.Value = image;
                plugin.Execute(MakeContext(new StubLog()));

                run.Ok = plugin.Success.Value is true;
                run.Text = plugin.Text.Value as string ?? string.Empty;
                run.Detail = plugin.Detail.Value as string ?? string.Empty;
                run.Classes = plugin.Classes.TypedValue ?? Array.Empty<string>();
                run.Confidences = ToDoubleArray(plugin.Confidences.TypedValue);
                run.Boxes = ToDoubleArray(plugin.CharBoxes.TypedValue);
                run.Count = Convert.ToInt32(plugin.Count.Value);
                run.RejectedCount = Convert.ToInt32(plugin.RejectedCount.Value);

                if (!run.Ok)
                    run.Error = "本步失败：" + (plugin.ErrorMessage.Value as string ?? "");
            }
            catch (Exception ex)
            {
                run.Ok = false;
                run.Error = $"抛出异常（不应发生）：{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                plugin.Dispose();
                image.Dispose();
            }

            return run;
        }

        private static double[] ToDoubleArray(HTuple? tuple)
        {
            if (tuple == null || tuple.Length == 0) return Array.Empty<double>();

            var values = new double[tuple.Length];
            for (int i = 0; i < tuple.Length; i++) values[i] = tuple[i].D;
            return values;
        }

        /// <summary>数出 Classes 里含 U+001A（SUB，分类器的拒识占位）的个数</summary>
        private static int CountSub(string[] classes)
        {
            int n = 0;
            foreach (var cls in classes)
                if (!string.IsNullOrEmpty(cls) && cls.IndexOf('\u001A') >= 0) n++;
            return n;
        }

        /// <summary>
        /// 逐位对照：Classes 里是 U+001A 的位，Detail 必须显示 '?'；其余位不许是 '?'。
        /// 反向那一半是关键 —— 只查"拒识位是 ?"的话，把 Detail 改成"全显示 ?"也能过。
        /// </summary>
        private static bool DetailMarksRejectedAsQuestion(string detail, string[] classes)
        {
            string[] tokens = detail.Split(' ');
            if (tokens.Length != classes.Length) return false;

            for (int i = 0; i < classes.Length; i++)
            {
                bool rejected = !string.IsNullOrEmpty(classes[i]) && classes[i].IndexOf('\u001A') >= 0;
                bool showsQuestion = tokens[i].StartsWith((i + 1) + ":?", StringComparison.Ordinal);
                if (rejected != showsQuestion) return false;
            }
            return true;
        }

        private static string JoinDoubles(double[] values)
        {
            string text = string.Empty;
            foreach (var v in values)
            {
                if (text.Length > 0) text += " ";
                text += v.ToString("0.000");
            }
            return text;
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
