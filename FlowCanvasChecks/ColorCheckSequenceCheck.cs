using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Core.Interfaces;
using Core.Events;
using Core.Halcon.Models;
using HalconDotNet;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 「颜色序列检查」插件第二刀（自适应找线 + 颜色序列）的断言。
    ///
    /// 本轮要证明的事
    /// ---------
    /// **把采样区交给算法，它能自己算出"几根、什么颜色"，且结果与脚本版的黄金参照一致。**
    /// 黄金参照来自既有脚本方案（线序检测.vms 已经跑通的那条流程）：
    ///   cable1（5 芯）= 黑,棕,玫红,红,黄
    ///   cable2（10 芯）= 黑,白,灰,紫,蓝,绿,黄,红,玫红,棕  （行号 147,168,189,209,230,251,272,292,313,334）
    /// 脚本那套判据写死了 150 / 40 / 240 三个数，换一盏灯就要重调；本轮的算法把它们换成
    /// 从本幅图现算的量（众数 + 百分位 + 大津法），此断言就是"换法之后结果没变差"的证据。
    ///
    /// 为什么还要钉住"关掉规整化"的对照组
    /// ---------
    /// 白线与浅背景只差几个灰阶，颜色上不可分 —— 能报出它靠的就是「等间距规整化」。
    /// 所以必须有一条断言证明：关掉它，根数会变少。否则"规整化"这四个字就是空话。
    /// </summary>
    internal static class ColorCheckSequenceCheck
    {
        private const string PluginTypeName =
            "Plugin.ColorCheck.ColorCheckPlugin, Plugin.ColorCheck, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        /// <summary>采样区：中心 (240,440)、不旋转、半长 120（沿列）、半宽 100（沿行）——
        /// 短轴方向（行）盖住 140..340，正好把线束整条罩住；长轴方向只用来判"哪边是线束走向"</summary>
        private static readonly double[] Roi = { 240.0, 440.0, 0.0, 120.0, 100.0 };

        private static readonly string[] Cable1Golden = { "黑", "棕", "玫红", "红", "黄" };
        private static readonly string[] Cable2Golden = { "黑", "白", "灰", "紫", "蓝", "绿", "黄", "红", "玫红", "棕" };
        private static readonly int[] Cable2GoldenRows = { 147, 168, 189, 209, 230, 251, 272, 292, 313, 334 };

        /// <summary>
        /// 第二刀那些用例只验「找线」，判定部分给一份全通配的配方：每一位都是 `*`（不检），
        /// 这样"几根"对上了就判合格，判定结果不会干扰找线的断言。
        /// 第三刀的用例才用真配方。
        /// </summary>
        private const string WildcardRecipe =
            "5 芯 = *,*,*,*,*\r\n" +
            "8 芯 = *,*,*,*,*,*,*,*\r\n" +
            "9 芯 = *,*,*,*,*,*,*,*,*\r\n" +
            "10 芯 = *,*,*,*,*,*,*,*,*,*\r\n";

        public static void Run()
        {
            Section("[R3] 颜色序列检查（第二刀）：自适应找线与颜色序列");

            string repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            Assembly? asm = null;
            string loadError = "";
            try
            {
                asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Plugin.ColorCheck")
                      ?? Assembly.LoadFrom(Path.Combine(repoRoot, @"Modules\Plugin.ColorCheck.dll"));
            }
            catch (Exception ex) { loadError = ex.Message; }

            Check("插件程序集可装载", asm != null, loadError.Length > 0 ? loadError : "Plugin.ColorCheck");
            if (asm == null) return;

            Type pluginType = asm.GetType("Plugin.ColorCheck.ColorCheckPlugin", true)!;

            RunCable(repoRoot, pluginType, "cable2.png", Cable2Golden, "10 芯", Cable2GoldenRows);
            RunCable(repoRoot, pluginType, "cable1.png", Cable1Golden, "5 芯", null);

            RunCrossChecks(repoRoot, pluginType);
            RunBoundaryChecks(repoRoot, pluginType);
            RunPreviewCheck(repoRoot, pluginType);
            RunJudgeChecks(repoRoot, pluginType);
            RunPersistenceCheck(pluginType);
        }

        // ==================================================================
        //  主链路：样本图 → 根数 / 序列 / 行号
        // ==================================================================

        private static void RunCable(string repoRoot, Type pluginType, string imageName,
            string[] golden, string product, int[]? goldenRows)
        {
            var plugin = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiParams"] = Roi,
                ["PreviewImagePath"] = "",
                ["RecipeText"] = WildcardRecipe,
                ["DisplayViewIndex"] = 0,
            });

            var outcome = RunOn(plugin, Path.Combine(repoRoot, @"Image\颜色\", imageName));

            Check($"【{product}】{imageName} 跑通并给出结果", outcome.Success, $"Success={outcome.Success} Err=[{outcome.Error}]");
            if (!outcome.Success) return;

            Check($"【{product}】根数 = {golden.Length}（脚本版的黄金参照）",
                outcome.Count == golden.Length, $"实测 {outcome.Count}");

            Check($"【{product}】颜色序列与黄金参照逐位一致：{string.Join(",", golden)}",
                outcome.Sequence == string.Join(",", golden), $"实测 {outcome.Sequence}");

            // 逐根来源：序号 + 颜色 + 检/补
            var parts = outcome.Detail.Split(',', StringSplitOptions.RemoveEmptyEntries);
            Check($"【{product}】逐根明细条数与根数一致",
                parts.Length == outcome.Count, $"明细 {parts.Length} 条 / 根数 {outcome.Count}");
            Check($"【{product}】每根都带合法的来源标记（检 = 检出 / 补 = 规整补出）",
                parts.All(p => Regex.IsMatch(p, @"^\d+[^\d]+[检补]$")),
                outcome.Detail);
            Check($"【{product}】补出来的根数不超过一半（补太多说明判据选不出线）",
                outcome.Detail.Count(c => c == '补') <= outcome.Count / 2,
                $"补 {outcome.Detail.Count(c => c == '补')} / 共 {outcome.Count}");

            if (goldenRows != null)
            {
                var rows = ParseNumbers(outcome.Rows).ToArray();
                bool rowsOk = rows.Length == goldenRows.Length
                              && rows.Zip(goldenRows, (a, b) => Math.Abs(a - b) <= 8).All(v => v);
                Check($"【{product}】每根线的行号与脚本版对得上（容差 8 像素）",
                    rowsOk, $"实测 [{string.Join(",", rows)}] / 参照 [{string.Join(",", goldenRows)}]");
            }
        }

        // ==================================================================
        //  对照组与旋钮
        // ==================================================================

        private static void RunCrossChecks(string repoRoot, Type pluginType)
        {
            string image = Path.Combine(repoRoot, @"Image\颜色\cable2.png");

            // ---- 关掉规整化：只报看得见的线，根数必须变少 ----
            var raw = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiParams"] = Roi,
                ["Regularize"] = false,
                ["RecipeText"] = WildcardRecipe,
                ["DisplayViewIndex"] = 0,
            });
            var rawOutcome = RunOn(raw, image);
            Check("【对照组】关掉规整化后只报看得见的线，根数确实变少",
                rawOutcome.Success && rawOutcome.Count < Cable2Golden.Length && rawOutcome.Count > 0,
                $"规整化关 → {rawOutcome.Count} 根；规整化开 → {Cable2Golden.Length} 根");

            // ---- 手动指定线数：以检出范围的中点为基准展开 ----
            var forced = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiParams"] = Roi,
                ["WireCount"] = 8,
                ["RecipeText"] = WildcardRecipe,
                ["DisplayViewIndex"] = 0,
            });
            var forcedOutcome = RunOn(forced, image);
            Check("【旋钮】手动指定 8 根 → 就报 8 根",
                forcedOutcome.Success && forcedOutcome.Count == 8,
                $"Success={forcedOutcome.Success} 实测 {forcedOutcome.Count} 根");

            // ---- 序列反向 ----
            var reversed = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiParams"] = Roi,
                ["ReverseSequence"] = true,
                ["RecipeText"] = WildcardRecipe,
                ["DisplayViewIndex"] = 0,
            });
            var reversedOutcome = RunOn(reversed, image);
            // 显式走 Enumerable.Reverse：对数组直接写 .Reverse() 会被 .NET 9 的
            // MemoryExtensions.Reverse(this Span<T>) 抢走（它返回 void），编译器会莫名其妙地报错
            var reversedExpected = string.Join(",", Enumerable.Reverse(Cable2Golden));
            Check("【旋钮】勾上「序列反向」→ 序列首尾对调",
                reversedOutcome.Success && reversedOutcome.Sequence == reversedExpected,
                $"实测 {reversedOutcome.Sequence}");
        }

        // ==================================================================
        //  边界：报错要说清"怎么补"
        // ==================================================================

        private static void RunBoundaryChecks(string repoRoot, Type pluginType)
        {
            string color = Path.Combine(repoRoot, @"Image\颜色\cable2.png");

            // ---- 没框采样区 ----
            var noRoi = CreatePlugin(pluginType, new Dictionary<string, object> { ["PreviewImagePath"] = "" });
            var noRoiOutcome = RunOn(noRoi, color);
            Check("没框采样区 → 明确报错并说清怎么补",
                !noRoiOutcome.Success && noRoiOutcome.Error.Contains("采样区"),
                $"Success={noRoiOutcome.Success} Err=[{noRoiOutcome.Error}]");

            // ---- 灰度图 ----
            var gray = CreatePlugin(pluginType, new Dictionary<string, object> { ["RoiParams"] = Roi });
            var grayOutcome = RunOn(gray, color, toGray: true);
            Check("灰度图 → 明确报「要彩色图」（不静默给错结果）",
                !grayOutcome.Success && grayOutcome.Error.Contains("彩色图"),
                $"Success={grayOutcome.Success} Err=[{grayOutcome.Error}]");

            // ---- 采样区超出图像 ----
            var oversized = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiParams"] = new[] { 240.0, 440.0, 0.0, 300.0, 300.0 },
            });
            var oversizedOutcome = RunOn(oversized, color);
            Check("采样区超出图像 → 报「超出图像范围」",
                !oversizedOutcome.Success && oversizedOutcome.Error.Contains("超出图像范围"),
                $"Success={oversizedOutcome.Success} Err=[{oversizedOutcome.Error}]");

            // ---- 采样区太窄 ----
            var tooNarrow = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiParams"] = new[] { 240.0, 440.0, 0.0, 120.0, 2.0 },
            });
            var tooNarrowOutcome = RunOn(tooNarrow, color);
            Check("采样区短边只有几个像素 → 报「太窄」",
                !tooNarrowOutcome.Success && tooNarrowOutcome.Error.Contains("太窄"),
                $"Success={tooNarrowOutcome.Success} Err=[{tooNarrowOutcome.Error}]");

            // ---- 采样区落在空白背景上（没有线）----
            // 注意不要拿"连接器"当反例：连接器上有一排金属触点，它们本身就是等间距的暗块，
            // 算法会理所当然地把它们当"线"报出来 —— 那不是 bug，是采样区画错了地方。
            // 真正的反例是纯背景。
            var empty = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiParams"] = new[] { 420.0, 120.0, 0.0, 100.0, 40.0 },
            });
            var emptyOutcome = RunOn(empty, color);
            Check("采样区落在空白背景上（没有线）→ 明确报错，不硬凑根数",
                !emptyOutcome.Success
                && (emptyOutcome.Error.Contains("空白") || emptyOutcome.Error.Contains("可靠线")),
                $"Success={emptyOutcome.Success} Err=[{emptyOutcome.Error}]");

            // ---- 界面试算：没载图时给提示 ----
            var fresh = CreatePlugin(pluginType, new Dictionary<string, object> { ["RoiParams"] = Roi });
            fresh.GetType().GetMethod("TryPreviewAnalyze")!.Invoke(fresh, null);
            string summary = (string)fresh.GetType().GetProperty("ResultSummary")!.GetValue(fresh)!;
            Check("界面「试算」在没载示意图时给出明确提示（不是静默无反应）",
                summary.Contains("载入"), summary);
        }

        // ==================================================================
        //  配置界面的「试算」按钮
        // ==================================================================

        private static void RunPreviewCheck(string repoRoot, Type pluginType)
        {
            var plugin = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiParams"] = Roi,
                ["RecipeText"] = WildcardRecipe,
                ["DisplayViewIndex"] = 0,
            });

            HOperatorSet.ReadImage(out HObject raw, Path.Combine(repoRoot, @"Image\颜色\cable1.png"));
            var image = new HImage(raw);
            raw?.Dispose();
            try
            {
                plugin.GetType().GetProperty("DisplayImage")!.SetValue(plugin, image);
                plugin.GetType().GetMethod("TryPreviewAnalyze")!.Invoke(plugin, null);
                string summary = (string)plugin.GetType().GetProperty("ResultSummary")!.GetValue(plugin)!;

                Check("界面「试算」给出根数、线距与判据阈值（调参的依据就在这几行里）",
                    summary.Contains("找到") && summary.Contains("线距") && summary.Contains("阈值"),
                    summary.Replace("\r\n", " | "));
            }
            finally { image.Dispose(); }
        }

        // ==================================================================
        //  配置往返：新参数要能随方案落盘
        // ==================================================================

        private static void RunPersistenceCheck(Type pluginType)
        {
            var step = new ActionStep("", "颜色序列检查", PluginTypeName, "颜色序列检查_0");
            step.SetInputValue("RoiParams", Roi);
            step.SetInputValue("PreviewImagePath", "");
            step.SetInputValue("WireCount", 7);
            step.SetInputValue("Regularize", false);
            step.SetInputValue("DarkSpanRatio", 42.0);
            step.SetInputValue("BrightPercentile", 88.0);
            step.SetInputValue("MinRun", 6);
            step.SetInputValue("ExtendEnds", false);
            step.SetInputValue("RecipeText", "8 芯 = 黑,白,灰,紫,蓝,绿,黄,红\r\n");
            step.SetInputValue("DisplayViewIndex", 3);

            var plugin = CreatePlugin(pluginType, new Dictionary<string, object>());
            plugin.Initialize(step);
            plugin.OnConfirm(step);

            var reopened = CreatePlugin(pluginType, new Dictionary<string, object>());
            reopened.Initialize(step);

            bool ok = Read<int>(reopened, "WireCount") == 7
                   && !Read<bool>(reopened, "Regularize")
                   && Math.Abs(Read<double>(reopened, "DarkSpanRatio") - 42.0) < 1e-6
                   && Math.Abs(Read<double>(reopened, "BrightPercentile") - 88.0) < 1e-6
                   && Read<int>(reopened, "MinRun") == 6
                   && !Read<bool>(reopened, "ExtendEnds")
                   && Read<string>(reopened, "RecipeText").Contains("8 芯 = 黑,白,灰,紫,蓝,绿,黄,红")
                   && Read<int>(reopened, "DisplayViewIndex") == 3;

            Check("新增的检查参数能随方案落盘并回填（线数 / 规整化 / 判据幅度 / 白线百分位 / 最小段长 / 端点外推 / 配方表 / 投射窗口）",
                ok,
                $"WireCount={Read<int>(reopened, "WireCount")} Regularize={Read<bool>(reopened, "Regularize")} "
                + $"DarkSpanRatio={Read<double>(reopened, "DarkSpanRatio")} "
                + $"BrightPercentile={Read<double>(reopened, "BrightPercentile")} "
                + $"MinRun={Read<int>(reopened, "MinRun")} ExtendEnds={Read<bool>(reopened, "ExtendEnds")} "
                + $"DisplayViewIndex={Read<int>(reopened, "DisplayViewIndex")}");
        }

        // ==================================================================
        //  第三刀：配方 / 判定 / 投射
        // ==================================================================

        /// <summary>配方表：5 芯一条、10 芯一条，外加注释与空行（顺带证明注释和空行会被忽略）</summary>
        private const string RecipeTwo =
            "# 线序配方（井号是注释，空行忽略）\r\n" +
            "5 芯排线  = 黑,棕,玫红,红,黄\r\n" +
            "\r\n" +
            "10 芯排线 = 黑,白,灰,紫,蓝,绿,黄,红,玫红,棕\r\n";

        private static void RunJudgeChecks(string repoRoot, Type pluginType)
        {
            string cable2 = Path.Combine(repoRoot, @"Image\颜色\cable2.png");
            string cable1 = Path.Combine(repoRoot, @"Image\颜色\cable1.png");

            // ---------- 合格：按根数匹配到对应的那条配方 ----------
            var ten = RunWithRecipe(pluginType, cable2, RecipeTwo);
            Check("【配方·合格】10 芯按根数匹配到「10 芯排线」并判合格",
                ten.Success && ten.Passed && ten.ExpectedSequence == string.Join(",", Cable2Golden)
                && ten.Verdict.Contains("线序正确"),
                $"合格={ten.Passed} 期望=[{ten.ExpectedSequence}] 结论=[{ten.Verdict}]");

            var five = RunWithRecipe(pluginType, cable1, RecipeTwo);
            Check("【配方·合格】同一个节点换成 5 芯图 → 匹配到「5 芯排线」并判合格（不用改任何配置）",
                five.Success && five.Passed && five.BadIndex == 0
                && five.ExpectedSequence == string.Join(",", Cable1Golden),
                $"合格={five.Passed} 期望=[{five.ExpectedSequence}] 结论=[{five.Verdict}]");

            // ---------- 不合格：指到具体第几位 ----------
            var wrong = RunWithRecipe(pluginType, cable2, "10 芯 = 黑,白,紫,紫,蓝,绿,黄,红,玫红,棕\r\n");
            Check("【配方·不合格】第 3 位被写成紫 → 判不合格且指到第 3 位",
                wrong.Success && !wrong.Passed && wrong.BadIndex == 3
                && wrong.Verdict.Contains("第 3 根应为 紫、实测 灰"),
                $"合格={wrong.Passed} BadIndex={wrong.BadIndex} 结论=[{wrong.Verdict}]");

            // ---------- 通配位 ----------
            var wildcard = RunWithRecipe(pluginType, cable2, "10 芯 = 黑,白,*,紫,蓝,绿,黄,红,玫红,棕\r\n");
            Check("【配方·通配】第 3 位写 * → 这一位不检，整体仍判合格",
                wildcard.Success && wildcard.Passed && wildcard.BadIndex == 0,
                $"合格={wildcard.Passed} 结论=[{wildcard.Verdict}]");

            // ---------- 端口优先 ----------
            var portFirst = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiParams"] = Roi,
                // 配方表故意给一条全错的；端口给对的 → 应以端口为准
                ["RecipeText"] = "10 芯 = 紫,紫,紫,紫,紫,紫,紫,紫,紫,紫\r\n",
                ["ExpectedSequence"] = string.Join(",", Cable2Golden),
                ["DisplayViewIndex"] = 0,
            });
            var portFirstOutcome = RunOn(portFirst, cable2);
            Check("【端口优先】「期望序列」端口有值时以端口为准（配方表里的错配方不生效）",
                portFirstOutcome.Success && portFirstOutcome.Passed
                && portFirstOutcome.ExpectedSequence == string.Join(",", Cable2Golden),
                $"合格={portFirstOutcome.Passed} 期望=[{portFirstOutcome.ExpectedSequence}]");

            // ---------- 根数不符（只能从端口进来：配方表本来就是按芯数匹配的） ----------
            var countMismatch = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiParams"] = Roi,
                ["ExpectedSequence"] = "黑,白,灰,紫,蓝,绿,黄,红",
                ["DisplayViewIndex"] = 0,
            });
            var countMismatchOutcome = RunOn(countMismatch, cable2);
            Check("【配方·根数不符】期望 8 位、实测 10 根 → 判不合格且结论说清是根数问题",
                countMismatchOutcome.Success && !countMismatchOutcome.Passed
                && countMismatchOutcome.BadIndex == 0
                && countMismatchOutcome.Verdict.Contains("根数不符"),
                $"合格={countMismatchOutcome.Passed} BadIndex={countMismatchOutcome.BadIndex} 结论=[{countMismatchOutcome.Verdict}]");

            // ---------- 配方缺失：报错让流程停住（与脚本版有意不同） ----------
            var missing = RunWithRecipe(pluginType, cable2, "5 芯排线 = 黑,棕,玫红,红,黄\r\n");
            Check("【配方缺失】10 芯在配方表里找不到 → 步骤失败让流程停住（不判成 NG）",
                !missing.Success && missing.Error.Contains("未知产品") && missing.Error.Contains("5 芯排线"),
                $"Success={missing.Success} Err=[{missing.Error}]");

            // ---------- 配方表格式错 ----------
            var badFormat = RunWithRecipe(pluginType, cable2, "10 芯排线 =\r\n");
            Check("【配方表格式错】某行只有产品名没有颜色 → 明确报出是第几行",
                !badFormat.Success && badFormat.Error.Contains("没有颜色") && badFormat.Error.Contains("第 1 行"),
                $"Success={badFormat.Success} Err=[{badFormat.Error}]");

            RunProjectionChecks(pluginType, cable2);
            RunSaveRecipeCheck(pluginType, cable2);
        }

        /// <summary>带配方跑一次（默认不投射，免得投射帧混进这些用例）</summary>
        private static Outcome RunWithRecipe(Type pluginType, string imagePath, string recipe)
        {
            var plugin = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiParams"] = Roi,
                ["RecipeText"] = recipe,
                ["DisplayViewIndex"] = 0,
            });
            return RunOn(plugin, imagePath);
        }

        // ---------- 投射 ----------

        private static void RunProjectionChecks(Type pluginType, string cable2)
        {
            // ---- 合格：采样线 + 每根 1 个标签 + 1 行结论；结论绿字；补出来的那根橙字 ----
            var frames = new List<ImageDisplayEvent<HImage>>();
            var okOutcome = RunOn(CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiParams"] = Roi,
                ["RecipeText"] = RecipeTwo,
                ["DisplayViewIndex"] = 1,
            }), cable2, frames: frames);

            Check("【投射】只投一帧、投到 1 号视图窗口（不重复投射）",
                okOutcome.Success && frames.Count == 1 && frames[0].ViewIndex == 1,
                $"帧数={frames.Count} 窗口={(frames.Count > 0 ? frames[0].ViewIndex : 0)}");
            if (frames.Count != 1) return;

            var marks = frames[0].Annotations ?? new List<MeasureAnnotation>();
            int lines = marks.Count(m => m.Type == MeasureType.Line);
            int texts = marks.Count(m => m.Type == MeasureType.Text);
            Check("【投射·合格】1 条采样线 + 每根 1 个颜色标签 + 1 行结论（10 芯 → 1 线 + 11 文本）",
                lines == 1 && texts == Cable2Golden.Length + 1,
                $"线={lines} 文本={texts}");

            var verdictMark = marks.FirstOrDefault(m => m.Text == okOutcome.Verdict);
            Check("【投射·合格】结论那一行是绿字，内容就是 Verdict 端口那一句",
                verdictMark != null && verdictMark.Color == "green" && okOutcome.Verdict.Contains("线序正确"),
                $"色={verdictMark?.Color} 文={verdictMark?.Text}");

            var filler = marks.FirstOrDefault(m => m.Text == "3.灰");
            Check("【投射】规整补出来的那根用橙字标出（Detail 里 §补 的画面版）",
                filler != null && filler.Color == "orange",
                $"色={filler?.Color} 文={filler?.Text}");

            var firstLabel = marks.FirstOrDefault(m => m.Text == "1.黑");
            Check("【投射】检出到的线是绿字（与橙字区分开）",
                firstLabel != null && firstLabel.Color == "green",
                $"色={firstLabel?.Color} 文={firstLabel?.Text}");

            // ---- 不合格：结论红字，不符的那根也红字 ----
            var badFrames = new List<ImageDisplayEvent<HImage>>();
            var badOutcome = RunOn(CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiParams"] = Roi,
                ["RecipeText"] = "10 芯 = 黑,白,紫,紫,蓝,绿,黄,红,玫红,棕\r\n",
                ["DisplayViewIndex"] = 1,
            }), cable2, frames: badFrames);

            var badMarks = badFrames.Count == 1
                ? badFrames[0].Annotations ?? new List<MeasureAnnotation>()
                : new List<MeasureAnnotation>();
            var badVerdict = badMarks.FirstOrDefault(m => m.Text == badOutcome.Verdict);
            var badLabel = badMarks.FirstOrDefault(m => m.Text == "3.灰");
            Check("【投射·不合格】结论与不符的那一根都是红字",
                badVerdict != null && badVerdict.Color == "red" && badLabel != null && badLabel.Color == "red",
                $"结论色={badVerdict?.Color} 第 3 根色={badLabel?.Color}");

            // ---- 窗口号 0 = 不投射 ----
            var offFrames = new List<ImageDisplayEvent<HImage>>();
            RunOn(CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiParams"] = Roi,
                ["RecipeText"] = RecipeTwo,
                ["DisplayViewIndex"] = 0,
            }), cable2, frames: offFrames);
            Check("【投射】视图窗口号填 0 → 完全不投射", offFrames.Count == 0, $"帧数={offFrames.Count}");
        }

        // ---------- 一键存配方 ----------

        private static void RunSaveRecipeCheck(Type pluginType, string cable2)
        {
            var saver = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiParams"] = Roi,
                ["RecipeText"] = WildcardRecipe,
                ["DisplayViewIndex"] = 0,
            });

            HOperatorSet.ReadImage(out HObject raw, cable2);
            var image = new HImage(raw);
            raw?.Dispose();
            try
            {
                saver.GetType().GetProperty("DisplayImage")!.SetValue(saver, image);
                saver.GetType().GetMethod("TryPreviewAnalyze")!.Invoke(saver, null);
                saver.GetType().GetMethod("SavePreviewAsRecipe")!.Invoke(saver, null);

                string recipe = (string)saver.GetType().GetProperty("RecipeText")!.GetValue(saver)!;
                Check("【一键存配方】把试算结果存成一条配方（省得手打颜色词打错）",
                    recipe.Contains($"10 芯 = {string.Join(",", Cable2Golden)}"),
                    recipe.Replace("\r\n", " / "));

                saver.GetType().GetMethod("SavePreviewAsRecipe")!.Invoke(saver, null);
                string again = (string)saver.GetType().GetProperty("RecipeText")!.GetValue(saver)!;
                Check("【一键存配方】再点一次不会重复追加同一条", again == recipe, again.Replace("\r\n", " / "));
            }
            finally
            {
                image.Dispose();
            }
        }

        // ==================================================================
        //  小工具
        // ==================================================================

        private readonly struct Outcome
        {
            internal Outcome(bool success, string error, int count, string sequence, string detail, string rows,
                bool passed, int badIndex, string verdict, string expected)
            {
                Success = success;
                Error = error;
                Count = count;
                Sequence = sequence;
                Detail = detail;
                Rows = rows;
                Passed = passed;
                BadIndex = badIndex;
                Verdict = verdict;
                ExpectedSequence = expected;
            }

            internal bool Success { get; }
            internal string Error { get; }
            internal int Count { get; }
            internal string Sequence { get; }
            internal string Detail { get; }
            internal string Rows { get; }

            /// <summary>判定结论：合格 / 不合格</summary>
            internal bool Passed { get; }

            /// <summary>第几位不符（1 起）；0 = 全对或根数不符</summary>
            internal int BadIndex { get; }

            /// <summary>结论全文</summary>
            internal string Verdict { get; }

            /// <summary>本次比对用的期望序列</summary>
            internal string ExpectedSequence { get; }
        }

        private static VisionPluginBase CreatePlugin(Type pluginType, IDictionary<string, object> config)
        {
            var plugin = (VisionPluginBase)Activator.CreateInstance(pluginType)!;
            plugin.InstanceName = "颜色序列检查_0";

            if (config.Count == 0) return plugin;

            var step = new ActionStep("", "颜色序列检查", PluginTypeName, "颜色序列检查_0");
            foreach (var kv in config) step.SetInputValue(kv.Key, kv.Value);

            plugin.Initialize(step);
            return plugin;
        }

        /// <summary>拿一张真图跑一次，并取回端口值与投射帧</summary>
        private static Outcome RunOn(VisionPluginBase plugin, string imagePath, bool toGray = false,
            List<ImageDisplayEvent<HImage>>? frames = null)
        {
            HOperatorSet.ReadImage(out HObject raw, imagePath);
            var image = new HImage(raw);
            raw?.Dispose();

            HObject? grayFeed = null;
            try
            {
                HObject feed = image;
                if (toGray)
                {
                    // 必须转成独立的 HImage 句柄：直接把 HObject 塞给 InputPort<HImage> 会抛
                    // InvalidCastException（端口 setter 走的是硬转换，不认 HObject 基类）
                    HOperatorSet.AccessChannel(image, out HObject channel, 1);
                    try { grayFeed = new HImage(channel); }
                    finally { channel.Dispose(); }
                    feed = grayFeed;
                }

                ((IInputPort)plugin.GetType().GetProperty("Image")!.GetValue(plugin)!).Value = feed;

                var context = new ExecutionContext(
                    new StubLog(), new FlowSession { FlowName = "颜色序列断言" },
                    new WorkspaceContext(), new CancellationTokenSource().Token);

                // 无 WPF 宿主时 PublishOnUIThread 降级为同步直调，所以订阅后能同步收到帧
                Action<ImageDisplayEvent<HImage>>? onPreview = frames == null
                    ? null
                    : e => { lock (frames) frames.Add(e); };
                if (onPreview != null) GlobalEventBus.Subscribe(onPreview);

                try
                {
                    // Success 由引擎的 Execute 预置为 true（插件成功时自己不该去设它），
                    // 这里直接驱动 RunAlgorithm，就得照引擎的做法先补上这一句
                    plugin.Success.Value = true;
                    plugin.RunAlgorithm(context);
                }
                finally
                {
                    if (onPreview != null) GlobalEventBus.Unsubscribe(onPreview);
                }

                return new Outcome(
                    (bool)plugin.Success.Value,
                    plugin.ErrorMessage.Value as string ?? "",
                    PortValue(plugin, "Count") is int c ? c : 0,
                    PortValue(plugin, "Sequence") as string ?? "",
                    PortValue(plugin, "Detail") as string ?? "",
                    PortValue(plugin, "Rows") as string ?? "",
                    PortValue(plugin, "Result") is bool b && b,
                    PortValue(plugin, "BadIndex") is int bi ? bi : 0,
                    PortValue(plugin, "Verdict") as string ?? "",
                    PortValue(plugin, "Expected") as string ?? "");
            }
            finally
            {
                grayFeed?.Dispose();
                image.Dispose();
            }
        }

        private static object? PortValue(VisionPluginBase plugin, string portName)
        {
            object? port = plugin.GetType().GetProperty(portName)?.GetValue(plugin);
            return port?.GetType().GetProperty("Value")?.GetValue(port);
        }

        private static T Read<T>(VisionPluginBase plugin, string propertyName)
            => (T)plugin.GetType().GetProperty(propertyName)!.GetValue(plugin)!;

        private static IEnumerable<int> ParseNumbers(string text)
            => text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => int.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out int v) ? v : int.MinValue);
    }
}
