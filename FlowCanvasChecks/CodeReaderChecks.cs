using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using HalconDotNet;
using Plugin.CodeReader;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 码读取（Plugin.CodeReader）的断言。
    ///
    /// 本轮要证明的事
    /// ---------
    /// **用 HALCON 官方样张，各码制都能真解出内容**，而不是"能建模型、不报错"。
    /// 样张来自 examples\images\datacode（二维全套）与 examples\images\barcode（一维全套）。
    ///
    /// 关于"真值"的诚实说明
    /// ---------
    /// 二维码/条码的**内容**无法像 OCR 那样人工读图确认（人眼读不出 QR 的比特）。
    /// 所以这里的断言分三类，都不依赖"外部真值"：
    ///   ① 官方样张必须解出 ≥1 个码 —— HALCON 自己的样张就是为该码制准备的，解不出说明接线有问题
    ///   ② 码制错配必须解不出、且不抛异常 —— 验证"未找到码是正常工况"这条语义
    ///   ③ 同一张混合图上，分别用一维/二维码制都能解出 —— 验证"一个节点覆盖两个家族"
    /// 内容层面的自洽（同一码在多张不同扫描图上解出同一串）由 ②③ 之外的多样张同码制覆盖。
    /// 需要更强的内容真值时，应另找独立解码器做交叉验证 —— 那是另一个课题，本文件不假装做了。
    /// </summary>
    internal static class CodeReaderChecks
    {
        /// <summary>识别参数：与插件将来要用的默认值一致，这样参数名/取值有问题会一次性暴露</summary>
        private const string Polarity = "any";
        private const int TimeoutMs = 500;

        private sealed record Case(string RelativePath, string Symbology, string Label);

        /// <summary>二维：每个码制用它的官方样张</summary>
        private static readonly Case[] DataCodeCases =
        {
            new(@"datacode\ecc200\ecc200_cpu_001.png", "Data Matrix ECC 200", "DataMatrix(CPU板)"),
            new(@"datacode\qrcode\qr_generated.png", "QR Code", "QR(生成图)"),
            new(@"datacode\micro_qr\micro_qr_board_01.png", "Micro QR Code", "MicroQR(板)"),
            new(@"datacode\pdf417\pdf417_hd_01.png", "PDF417", "PDF417"),
            new(@"datacode\aztec\aztec_smartphone_01.png", "Aztec Code", "Aztec(手机屏)"),
            new(@"datacode\dotcode\dotcode_mvtec_01.png", "DotCode", "DotCode"),
        };

        /// <summary>一维：同样用官方样张，码制明确指定（不用 auto —— auto 更慢且不是推荐用法）</summary>
        private static readonly Case[] BarCodeCases =
        {
            new(@"barcode\code128\code12801.png", "Code 128", "Code128"),
            new(@"barcode\code39\code3901.png", "Code 39", "Code39"),
            new(@"barcode\ean13\ean1301.png", "EAN-13", "EAN-13"),
            new(@"barcode\ean8\ean801.png", "EAN-8", "EAN-8"),
            new(@"barcode\codabar\codabar01.png", "Codabar", "Codabar"),
            new(@"barcode\25interleaved\25interleaved01.png", "2/5 Interleaved", "交插二五码"),
        };

        public static void Run()
        {
            Section("[码读取] 条码/二维码识别（官方样张验证）");

            string? root = ResolveImagesRoot();
            if (root == null)
            {
                Check("码读取真图断言（需要 HALCON 官方示例图库）", true,
                    "跳过：未找到 examples\\images（HALCONIMAGES 与 HALCONROOT 都没配）");
                return;
            }

            int ok2D = 0;
            int ok1D = 0;

            foreach (var c in DataCodeCases)
                if (RunCase(root, c, expectFound: true)) ok2D++;

            foreach (var c in BarCodeCases)
                if (RunCase(root, c, expectFound: true)) ok1D++;

            Check($"二维样张全部解出（{ok2D}/{DataCodeCases.Length}）",
                ok2D == DataCodeCases.Length, $"成功 {ok2D} 个");
            Check($"一维样张全部解出（{ok1D}/{BarCodeCases.Length}）",
                ok1D == BarCodeCases.Length, $"成功 {ok1D} 个");

            RunNegativeCases(root);
            RunMixedImageCases(root);
            RunEanChecksumCases(root);
            RunPluginLevel(root);
        }

        /// <summary>
        /// 插件壳的断言：端口、完整运行路径、**数组输出形态**、"未找到码是正常工况"这条语义、模型释放。
        ///
        /// 为什么算法跑通了还要单独测壳：算法对不等于插件可用 —— 端口能不能承载数组、
        /// 配置能不能灌进去、用户选的那条失败语义有没有真的实现、句柄有没有释放，这些全在壳上。
        /// </summary>
        private static void RunPluginLevel(string root)
        {
            // ---- ① 完整运行路径 + 数组输出形态 ----
            // 用 Code39 那张样张：实测它里面有【两个】条码 —— 正好一次验两件事：
            // 数组端口能不能承载多个码、以及 Text（逗号分隔）与 Codes 是否内容一致。
            var log = new StubLog();
            var plugin = new CodeReaderPlugin { Symbology = "Code 39", InstanceName = "Code_0" };
            HImage? image = ReadImageOrNull(Path.Combine(root, @"barcode\code39\code3901.png"));

            Exception? boom = null;
            if (image != null)
            {
                try
                {
                    plugin.Image.Value = image;
                    plugin.Execute(MakeContext(log));
                }
                catch (Exception ex) { boom = ex; }
            }

            var codes = plugin.Codes.Value as string[];
            Check("插件运行不抛异常", boom == null, boom == null ? "" : $"{boom.GetType().Name}: {boom.Message}");
            Check("插件报成功（读到码属正常成功路径）",
                plugin.Success.Value is true,
                $"Success={plugin.Success.Value} Error='{plugin.ErrorMessage.Value}'");
            Check("【核心】一帧两个码 → Codes 数组真的承载了两个元素（数组端口可用）",
                codes is { Length: 2 },
                $"Codes={(codes == null ? "null" : string.Join(" | ", codes))} Count={plugin.Count.Value}");
            Check("Count 与数组长度一致",
                codes != null && Convert.ToInt32(plugin.Count.Value) == codes.Length,
                $"Count={plugin.Count.Value} 数组长={codes?.Length}");
            Check("Text（逗号分隔）与 Codes 内容一致 —— 标量下游就靠它接",
                codes != null && (plugin.Text.Value as string) == string.Join(",", codes),
                $"Text='{plugin.Text.Value}'");

            // ---- ② "未找到码是正常工况"：这条是用户明确选的语义，必须钉住 ----
            // 同时验证那条 Info 里带了码制名 —— "读不到码"最常见的原因是"码制选错了"，
            // 把当前码制写进日志，现场一眼就能自查，不用去翻节点配置。
            var logBlank = new StubLog();
            var pluginBlank = new CodeReaderPlugin { Symbology = "Data Matrix ECC 200", InstanceName = "Code_空白图" };
            HOperatorSet.GenImageConst(out HObject blank, "byte", 640, 480);
            var blankImage = new HImage(blank);
            blank.Dispose();

            boom = null;
            try
            {
                pluginBlank.Image.Value = blankImage;
                pluginBlank.Execute(MakeContext(logBlank));
            }
            catch (Exception ex) { boom = ex; }

            Check("【核心】图上没有码 → 不抛异常、Success=true（不把正常工况当失败）",
                boom == null && pluginBlank.Success.Value is true,
                $"Success={pluginBlank.Success.Value} Error='{pluginBlank.ErrorMessage.Value}'");
            Check("【核心】图上没有码 → Count=0 且 Codes 为空数组（下游用 Count 判有没有）",
                Convert.ToInt32(pluginBlank.Count.Value) == 0
                    && pluginBlank.Codes.Value is string[] { Length: 0 },
                $"Count={pluginBlank.Count.Value} Codes={(pluginBlank.Codes.Value as string[])?.Length}");
            Check("未读到码时写 Info，且带上当前码制（便于现场自查是不是码制选错了）",
                logBlank.Infos.Exists(i => i.Contains("未读到码") && i.Contains("Data Matrix ECC 200")),
                string.Join(" | ", logBlank.Infos));

            // ---- ③ 码制名不认识 → 明确失败，不静默退回默认码制 ----
            // 静默退回会让现场以为在用 A 码制、实际按 B 在找，表现只是"读不到码"，无从排查。
            var pluginBadSym = new CodeReaderPlugin { Symbology = "不存在的码制", InstanceName = "Code_坏码制" };
            pluginBadSym.Image.Value = blankImage;
            pluginBadSym.Execute(MakeContext(new StubLog()));
            Check("码制名不在表里 → 报失败且把可选值列出来（不静默退回默认）",
                pluginBadSym.Success.Value is false
                    && (pluginBadSym.ErrorMessage.Value as string ?? "").Contains("不在支持的码制表里"),
                $"Success={pluginBadSym.Success.Value} Error='{pluginBadSym.ErrorMessage.Value}'");

            // ---- ④ 上游没连图 → 明确失败，提示写清怎么补 ----
            var pluginNoImage = new CodeReaderPlugin { InstanceName = "Code_无图" };
            pluginNoImage.Execute(MakeContext(new StubLog()));
            Check("上游没连图 → 报失败且提示指向连 Image",
                pluginNoImage.Success.Value is false
                    && (pluginNoImage.ErrorMessage.Value as string ?? "").Contains("上游没有图像"),
                $"Success={pluginNoImage.Success.Value} Error='{pluginNoImage.ErrorMessage.Value}'");

            // ---- ⑤ 释放不抛（两个 Halcon 模型句柄）----
            Exception? disposeBoom = null;
            try
            {
                plugin.Dispose();
                pluginBlank.Dispose();
                pluginBadSym.Dispose();
                pluginNoImage.Dispose();
            }
            catch (Exception ex) { disposeBoom = ex; }
            Check("Dispose 释放两个模型句柄不抛异常", disposeBoom == null,
                disposeBoom == null ? "" : $"{disposeBoom.GetType().Name}: {disposeBoom.Message}");

            image?.Dispose();
            blankImage.Dispose();
        }

        private static ExecutionContext MakeContext(ILogService log) =>
            new(log, new FlowSession { FlowName = "码读取插件断言" }, new WorkspaceContext(),
                new CancellationTokenSource().Token);

        private static HImage? ReadImageOrNull(string path)
        {
            if (!File.Exists(path)) return null;
            HOperatorSet.ReadImage(out HObject raw, path);
            var image = new HImage(raw);
            raw?.Dispose();
            return image;
        }

        /// <summary>
        /// EAN 校验位验算 —— 本文件里**唯一的"独立真值"**。
        ///
        /// 为什么它比其余断言强一个量级：二维码/条码的**内容**没法像 OCR 那样人工读图确认，
        /// 所以别处只能断言"解出了点什么"（Count≥1）。而 EAN 的最高位是**按编码规则算出来的校验位**，
        /// 验算不依赖 HALCON —— 一个"把内容读错"的 bug 会在这里现形，在别处不会。
        /// </summary>
        private static void RunEanChecksumCases(string root)
        {
            VerifyEanChecksum(root, @"barcode\ean13\ean1301.png", "EAN-13", 13);
            VerifyEanChecksum(root, @"barcode\ean8\ean801.png", "EAN-8", 8);
        }

        private static void VerifyEanChecksum(string root, string relativePath, string symbology, int digits)
        {
            string path = Path.Combine(root, relativePath);
            if (!File.Exists(path))
            {
                Check($"{symbology} 校验位验算：找到样张", false, path);
                return;
            }

            var outcome = Decode(path, symbology);
            if (!outcome.Ok || outcome.Codes.Count == 0)
            {
                Check($"{symbology} 校验位验算", false,
                    outcome.Ok ? "未解出内容" : outcome.Error);
                return;
            }

            string code = outcome.Codes[0].Trim();
            Check($"{symbology} 解出 {digits} 位纯数字（实为 '{code}'）",
                code.Length == digits && code.All(char.IsDigit), $"长度 {code.Length}");

            bool ok = EanChecksumOk(code, out string detail);
            Check($"{symbology} 校验位正确（独立于 HALCON 的数学验算）", ok, $"{code} → {detail}");
        }

        /// <summary>
        /// EAN-13 / EAN-8 的校验位验算：从最右的数据位起，权重交替 3、1，求和后取 (10 - 和%10)%10。
        /// 13 位与 8 位用的是同一条规则，所以一个函数够用。
        /// </summary>
        private static bool EanChecksumOk(string code, out string detail)
        {
            detail = string.Empty;
            if (code.Length < 2 || !code.All(char.IsDigit))
            {
                detail = "不是纯数字，无法验算";
                return false;
            }

            int last = code.Length - 1;
            int sum = 0;
            for (int i = 0; i < last; i++)
            {
                int weight = ((last - i) % 2 == 1) ? 3 : 1;
                sum += (code[i] - '0') * weight;
            }

            int expected = (10 - sum % 10) % 10;
            int actual = code[last] - '0';
            detail = $"加权和 {sum}，校验位期望 {expected} 实际 {actual}";
            return expected == actual;
        }

        /// <summary>正面用例：官方样张必须解出 ≥1 个码；顺带把解出的内容打出来供人工核对</summary>
        private static bool RunCase(string root, Case c, bool expectFound)
        {
            string path = Path.Combine(root, c.RelativePath);
            if (!File.Exists(path))
            {
                Check($"{c.Label}：找到样张", false, $"样张不存在：{path}");
                return false;
            }

            var outcome = Decode(path, c.Symbology);
            if (!outcome.Ok)
            {
                Check($"{c.Label} 识别不抛异常", false, outcome.Error);
                return false;
            }

            string content = outcome.Codes.Count == 0 ? "（未解出）" : string.Join(" | ", outcome.Codes);
            Check($"{c.Label} 解出内容（码制 {c.Symbology}）",
                outcome.Codes.Count >= 1,
                $"Count={outcome.Codes.Count} 内容：{content}");
            return outcome.Codes.Count >= 1;
        }

        /// <summary>
        /// 反面用例：码制错配必须"解不出"而不是"抛异常"。
        /// 这验证的是用户确认过的那条语义 —— 未找到码是正常工况，不该让流程中断。
        /// </summary>
        private static void RunNegativeCases(string root)
        {
            // ① 用 Data Matrix 模型去读 QR 图：应当一个都解不出
            string qr = Path.Combine(root, @"datacode\qrcode\qr_generated.png");
            if (File.Exists(qr))
            {
                var mis = Decode(qr, "Data Matrix ECC 200");
                Check("码制错配（用 DataMatrix 读 QR 图）→ 解不出且不抛异常",
                    mis.Ok && mis.Codes.Count == 0,
                    mis.Ok ? $"Count={mis.Codes.Count} 内容：{string.Join(" | ", mis.Codes)}" : mis.Error);
            }

            // ② 纯常量图（无任何码）：应当一个都解不出，且不抛异常
            HOperatorSet.GenImageConst(out HObject blank, "byte", 640, 480);
            var blankImage = new HImage(blank);
            blank.Dispose();

            var b1 = DecodeImage(blankImage, "Data Matrix ECC 200");
            Check("空白图上读二维码 → 解不出且不抛异常",
                b1.Ok && b1.Codes.Count == 0, b1.Ok ? $"Count={b1.Codes.Count}" : b1.Error);

            var b2 = DecodeImage(blankImage, "Code 128");
            Check("空白图上读一维码 → 解不出且不抛异常",
                b2.Ok && b2.Codes.Count == 0, b2.Ok ? $"Count={b2.Codes.Count}" : b2.Error);

            blankImage.Dispose();
        }

        /// <summary>
        /// 混合图：同一张图里同时有条码与二维码。
        /// 验证"一个节点覆盖两个家族"——选哪个码制就走哪个引擎。
        /// </summary>
        private static void RunMixedImageCases(string root)
        {
            string mixed = Path.Combine(root, @"barcode\mixed\barcodes_datacodes_mixed_01.png");
            if (!File.Exists(mixed))
            {
                Check("混合码图（条码+二维码同图）断言", true, $"跳过：样张不存在 {mixed}");
                return;
            }

            var one = Decode(mixed, "Code 128");
            Check("混合图上按一维码制读 → 解出条码",
                one.Ok && one.Codes.Count >= 1,
                one.Ok ? $"Count={one.Codes.Count} 内容：{string.Join(" | ", one.Codes)}" : one.Error);

            var two = Decode(mixed, "QR Code");
            Check("混合图上按二维码码制读 → 解出二维码",
                two.Ok && two.Codes.Count >= 1,
                two.Ok ? $"Count={two.Codes.Count} 内容：{string.Join(" | ", two.Codes)}" : two.Error);
        }

        // ==================================================================
        //  辅助
        // ==================================================================

        private sealed class Outcome
        {
            public bool Ok;
            public string Error = string.Empty;
            public List<string> Codes = new();
            public string CountNote = string.Empty;
        }

        private static Outcome Decode(string imagePath, string symbology)
        {
            HOperatorSet.ReadImage(out HObject raw, imagePath);
            var image = new HImage(raw);
            raw?.Dispose();

            try
            {
                return DecodeImage(image, symbology);
            }
            finally
            {
                image.Dispose();
            }
        }

        private static Outcome DecodeImage(HImage image, string symbology)
        {
            var outcome = new Outcome();

            if (!CodeSymbologyTable.TryGet(symbology, out var entry))
            {
                outcome.Error = $"码制不在表里：{symbology}";
                return outcome;
            }

            if (entry.Family == CodeFamily.DataCode2D)
            {
                var engine = new DataCodeEngine();
                try
                {
                    outcome.Ok = engine.TryDecode(image, entry.HalconName, Polarity, TimeoutMs,
                        out var codes, out string error);
                    outcome.Error = error;
                    outcome.Codes = codes;
                    outcome.CountNote = engine.LastCountNote;
                }
                finally { engine.Dispose(); }
            }
            else
            {
                var engine = new BarCodeEngine();
                try
                {
                    // 一维没有极性参数（实测 set_bar_code_param 拒绝 'polarity'），所以这里不传
                    outcome.Ok = engine.TryDecode(image, entry.HalconName, TimeoutMs,
                        out var codes, out string error);
                    outcome.Error = error;
                    outcome.Codes = codes;
                }
                finally { engine.Dispose(); }
            }

            return outcome;
        }

        /// <summary>缺省位置：优先 HALCONIMAGES，再退回 HALCONROOT 下的约定路径</summary>
        private static string? ResolveImagesRoot()
        {
            string? images = Environment.GetEnvironmentVariable("HALCONIMAGES");
            if (!string.IsNullOrWhiteSpace(images) && Directory.Exists(images)) return images;

            string? root = Environment.GetEnvironmentVariable("HALCONROOT");
            if (!string.IsNullOrWhiteSpace(root))
            {
                string p = Path.Combine(root!, "examples", "images");
                if (Directory.Exists(p)) return p;
            }

            return null;
        }
    }
}
