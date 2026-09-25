using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Core.Events;
using Core.Halcon;
using Core.Halcon.Color;
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
    /// 「区域颜色检查」算子（第二个颜色算子）的断言。
    ///
    /// 这一份断言有**双重目的**：
    ///   ① 验新算子本身：一块区域的主色、占比、判定、投射对不对；
    ///   ② **验颜色内核真的通用** —— 内核抽出来时只有「颜色序列检查」一个消费者，
    ///      而"只有一个消费者的内核是猜的"。本算子是第二个消费者：
    ///      它用的是内核的同一套颜色词、同一套采样（给点取色）、同一套投射语言（绿/红/橙），
    ///      但没有复用序列算子的任何东西（不找线、不比顺序）。
    ///
    /// 样本图上的坐标是量出来的：cable2 的线中心行号 147/168/189/209/230/251/272/292/313/334
    /// （黑/白/灰/紫/蓝/绿/黄/红/玫红/棕），采样列 440。
    /// </summary>
    internal static class ColorRegionCheck
    {
        private const string PluginTypeName =
            "Plugin.ColorRegion.RegionColorPlugin, Plugin.ColorRegion, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        public static void Run()
        {
            Section("[R6] 区域颜色检查：主色 / 占比 / 判定 / 投射（第二个颜色算子，兼验内核通用）");

            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            var loadError = "";
            Assembly? asm = null;
            try
            {
                asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Plugin.ColorRegion")
                      ?? Assembly.LoadFrom(Path.Combine(repoRoot, @"Modules\Plugin.ColorRegion.dll"));
            }
            catch (Exception ex) { loadError = ex.Message; }

            Check("插件程序集可装载（含 WPF 视图与共用阈值编辑器）", asm != null, loadError.Length > 0 ? loadError : "Plugin.ColorRegion");
            if (asm == null) return;

            Type pluginType = asm.GetType("Plugin.ColorRegion.RegionColorPlugin", true)!;
            string cable2 = Path.Combine(repoRoot, @"Image\颜色\cable2.png");
            string cable1 = Path.Combine(repoRoot, @"Image\颜色\cable1.png");

            RunShapeChecks(pluginType, cable2);
            RunJudgeChecks(pluginType, cable2);
            RunBoundaryChecks(pluginType, cable2, cable1);
            RunCanvasSyncCheck(pluginType);
            RunUpstreamImageCheck(pluginType, cable2, cable1);
            RunProjectionCheck(pluginType, cable2);
            RunTeachAndPersistenceCheck(pluginType, cable2);
        }

        // ==================================================================
        //  三种 ROI 形状：都能正确取到线
        // ==================================================================

        private static void RunShapeChecks(Type pluginType, string cable2)
        {
            // 矩形框住 cable2 的第 3 根线（灰，中心行 189，采样列 440）
            var rect = RunOn(pluginType, cable2, RoiShapeNames.Rectangle, new[] { 189.0, 440.0, 0.0, 6.0, 5.0 }, "灰");
            Check("【矩形】框住灰线 → 主色 灰、占比高、判合格",
                rect.Success && rect.Passed && rect.Color == "灰" && rect.Share >= 0.6,
                $"Success={rect.Success} 主色={rect.Color} 占比={rect.Share:0.00} 明细=[{rect.Table}] 结论=[{rect.Verdict}]");

            // 同一位置换一根线（红，中心行 292），证明不是碰巧
            var rect2 = RunOn(pluginType, cable2, RoiShapeNames.Rectangle, new[] { 292.0, 440.0, 0.0, 6.0, 5.0 }, "红");
            Check("【矩形】同一流程换一根线（红）→ 主色跟着变 红",
                rect2.Success && rect2.Passed && rect2.Color == "红",
                $"主色={rect2.Color} 占比={rect2.Share:0.00} 明细=[{rect2.Table}]");

            // 圆形：半径 7 的圆落在灰线上（区域掩膜只含圆内像素）
            var circle = RunOn(pluginType, cable2, RoiShapeNames.Circle, new[] { 189.0, 440.0, 7.0, 0.0, 0.0 }, "灰");
            Check("【圆形】圆框住灰线 → 主色 灰（圆掩膜生效，不是方框四角）",
                circle.Success && circle.Passed && circle.Color == "灰",
                $"主色={circle.Color} 占比={circle.Share:0.00} 明细=[{circle.Table}]");

            // 椭圆：半径 9 × 6
            var ellipse = RunOn(pluginType, cable2, RoiShapeNames.Ellipse, new[] { 189.0, 440.0, 0.0, 9.0, 6.0 }, "灰");
            Check("【椭圆】椭圆框住灰线 → 主色 灰",
                ellipse.Success && ellipse.Passed && ellipse.Color == "灰",
                $"主色={ellipse.Color} 占比={ellipse.Share:0.00} 明细=[{ellipse.Table}]");
        }

        // ==================================================================
        //  判定：合格 / 颜色错误 / 不纯 / 通配
        // ==================================================================

        private static void RunJudgeChecks(Type pluginType, string cable2)
        {
            // 颜色错误：期望红，实测灰
            var wrong = RunOn(pluginType, cable2, RoiShapeNames.Rectangle, new[] { 189.0, 440.0, 0.0, 6.0, 5.0 }, "红");
            Check("【判定·颜色错误】期望 红 但实测 灰 → 不合格，结论说清实测与期望",
                wrong.Success && !wrong.Passed && wrong.Verdict.Contains("颜色错误")
                && wrong.Verdict.Contains("灰") && wrong.Verdict.Contains("红"),
                $"合格={wrong.Passed} 结论=[{wrong.Verdict}]");

            // 多候选：期望 红,玫红,灰 → 命中其一即合格
            var anyOf = RunOn(pluginType, cable2, RoiShapeNames.Rectangle, new[] { 189.0, 440.0, 0.0, 6.0, 5.0 }, "红,玫红,灰");
            Check("【判定·多候选】期望写 红,玫红,灰 → 命中其一即合格",
                anyOf.Success && anyOf.Passed, $"合格={anyOf.Passed} 结论=[{anyOf.Verdict}]");

            // 通配：只报颜色不判定
            var wildcard = RunOn(pluginType, cable2, RoiShapeNames.Rectangle, new[] { 189.0, 440.0, 0.0, 6.0, 5.0 }, "*");
            Check("【判定·通配】期望写 * → 只报颜色不判定，仍算通过",
                wildcard.Success && wildcard.Passed && wildcard.Verdict.Contains("只报颜色")
                && wildcard.Color == "灰",
                $"合格={wildcard.Passed} 主色={wildcard.Color} 结论=[{wildcard.Verdict}]");

            // 混合：把框拉长到同时罩住两根线（蓝 230 / 绿 251）与它们之间的缝
            // 注意缝（线间的暗影）本身会被归到"灰"，所以这种框的主色往往是灰 —— 这正是
            // "一块区域里混了多种颜色"的真实样子：明细里会出现好几种颜色，主色占比明显低于单根线。
            var mixed = RunOn(pluginType, cable2, RoiShapeNames.Rectangle, new[] { 240.0, 440.0, 0.0, 6.0, 26.0 }, "蓝");
            int distinctColors = mixed.Table.Split('、', StringSplitOptions.RemoveEmptyEntries).Length;
            Check("【判定·混合】框住两根线+缝 → 明细里出现多种颜色，主色占比明显低于单根线",
                mixed.Success && distinctColors >= 3 && mixed.Share < 0.8
                && mixed.Table.Contains("%"),
                $"主色={mixed.Color} 占比={mixed.Share:0.00} 颜色数={distinctColors} 明细=[{mixed.Table}]");

            // 占比不足：期望对（红红框住的就是红），但要求 ≥90% → 判「颜色不纯」
            var strict = RunOn(pluginType, cable2, RoiShapeNames.Rectangle, new[] { 292.0, 440.0, 0.0, 6.0, 5.0 }, "红",
                minShare: 0.90);
            Check("【判定·占比不足】期望对但要求 ≥90%（实测只有 67%）→ 判「颜色不纯」",
                strict.Success && !strict.Passed && strict.Verdict.Contains("颜色不纯")
                && strict.Verdict.Contains("明细"),
                $"合格={strict.Passed} 占比={strict.Share:0.00} 结论=[{strict.Verdict}]");
        }

        // ==================================================================
        //  边界：报错要说清"怎么补"
        // ==================================================================

        private static void RunBoundaryChecks(Type pluginType, string cable2, string cable1)
        {
            // 没框采样区
            var noRoi = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["PreviewImagePath"] = "",
                ["ExpectedColors"] = "灰",
            });
            var noRoiOutcome = RunWithImage(noRoi, cable2);
            Check("没框采样区 → 明确报错并说清怎么补",
                !noRoiOutcome.Success && noRoiOutcome.Error.Contains("采样区"),
                $"Success={noRoiOutcome.Success} Err=[{noRoiOutcome.Error}]");

            // 没填期望颜色
            var noExpected = RunOn(pluginType, cable2, RoiShapeNames.Rectangle, new[] { 189.0, 440.0, 0.0, 6.0, 5.0 }, "");
            Check("没填期望颜色 → 明确报错（配置问题不静默通过、也不判成不良）",
                !noExpected.Success && noExpected.Error.Contains("尚未配置期望颜色"),
                $"Success={noExpected.Success} Err=[{noExpected.Error}]");

            // 期望词写错（不在词表里）
            var badWord = RunOn(pluginType, cable2, RoiShapeNames.Rectangle, new[] { 189.0, 440.0, 0.0, 6.0, 5.0 }, "深红");
            Check("期望颜色写了词表外的词（深红）→ 明确报错并列出可用词",
                !badWord.Success && badWord.Error.Contains("认不出的词") && badWord.Error.Contains("玫红"),
                $"Success={badWord.Success} Err=[{badWord.Error}]");

            // 灰度图
            var gray = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiShape"] = RoiShapeNames.Rectangle,
                ["RoiParams"] = new[] { 189.0, 440.0, 0.0, 6.0, 5.0 },
                ["ExpectedColors"] = "灰",
                ["DisplayViewIndex"] = 0,
            });
            var grayOutcome = RunWithImage(gray, cable2, toGray: true);
            Check("灰度图 → 明确报「要彩色图」",
                !grayOutcome.Success && grayOutcome.Error.Contains("彩色图"),
                $"Success={grayOutcome.Success} Err=[{grayOutcome.Error}]");

            // 采样区与图像没有交集（框到图像外）
            var outside = RunOn(pluginType, cable2, RoiShapeNames.Rectangle, new[] { -50.0, 440.0, 0.0, 6.0, 5.0 }, "灰");
            Check("采样区整个落在图像外 → 报「没有交集」",
                !outside.Success && outside.Error.Contains("没有交集"),
                $"Success={outside.Success} Err=[{outside.Error}]");

            // 5 芯图同样能认（证明不是只为 cable2 调的）
            var cable1Red = RunOn(pluginType, cable1, RoiShapeNames.Rectangle, new[] { 300.0, 440.0, 0.0, 6.0, 5.0 }, "红");
            Check("【5 芯图】框住红线 → 主色 红（同一算子换图不用改配置）",
                cable1Red.Success && cable1Red.Passed && cable1Red.Color == "红",
                $"主色={cable1Red.Color} 占比={cable1Red.Share:0.00} 明细=[{cable1Red.Table}]");
        }

        // ==================================================================
        //  画布同步：三种形状的框都能回填与回写
        // ==================================================================

        private static void RunCanvasSyncCheck(Type pluginType)
        {
            var shapes = new (string Shape, DrawShapeType Draw, double[] Pars, HTuple[] Tuples)[]
            {
                ("矩形", DrawShapeType.Rectangle, new[] { 189.0, 440.0, 0.0, 6.0, 5.0 },
                    new[] { new HTuple(189.0), new HTuple(440.0), new HTuple(0.0), new HTuple(6.0), new HTuple(5.0) }),
                ("圆形", DrawShapeType.Circle, new[] { 189.0, 440.0, 7.0, 0.0, 0.0 },
                    new[] { new HTuple(189.0), new HTuple(440.0), new HTuple(7.0) }),
                ("椭圆", DrawShapeType.Ellipse, new[] { 189.0, 440.0, 0.0, 9.0, 6.0 },
                    new[] { new HTuple(189.0), new HTuple(440.0), new HTuple(0.0), new HTuple(9.0), new HTuple(6.0) }),
            };

            foreach (var (shape, draw, pars, tuples) in shapes)
            {
                var step = new ActionStep("", "区域颜色检查", PluginTypeName, "区域颜色检查_0");
                step.SetInputValue("RoiShape", shape == "矩形" ? RoiShapeNames.Rectangle
                    : shape == "圆形" ? RoiShapeNames.Circle : RoiShapeNames.Ellipse);
                step.SetInputValue("RoiParams", pars);

                // 打开配置：按已存形状与参数回填出 1 个对应形状的框
                var plugin = CreatePlugin(pluginType, new Dictionary<string, object>());
                plugin.Initialize(step);
                var canvas = GetCanvasRois(plugin);

                // 注意别在断言详情里直接写 canvas[0] —— 详情字符串是先求值的，
                // 个数为 0 时会先抛越界，把"回填失败"变成"崩溃"（这一条踩过）
                object? backfilled = canvas.Count == 1 ? canvas[0] : null;
                Check($"【画布·{shape}】打开配置时回填出 1 个 {shape} 框，参数一致",
                    backfilled != null && GetShapeType(backfilled) == draw
                    && SameNumbers(GetParams(plugin), pars, 3),
                    $"个数={canvas.Count} 形状={(backfilled == null ? "(无)" : GetShapeType(backfilled).ToString())} "
                    + $"参数=[{string.Join(",", GetParams(plugin).Select(d => d.ToString("0.#")))}]");

                // 用户在图上新画一个同形状的框 → 回写参数
                canvas.Clear();
                canvas.Add(new DrawingObjectInfo(draw, tuples, "采样区"));
                Check($"【画布·{shape}】在图上画框后，形状与参数被同步回写",
                    Read<string>(plugin, "RoiShape") == (shape == "矩形" ? RoiShapeNames.Rectangle
                        : shape == "圆形" ? RoiShapeNames.Circle : RoiShapeNames.Ellipse)
                    && SameNumbers(GetParams(plugin), pars, 3),
                    $"形状={Read<string>(plugin, "RoiShape")} 参数=[{string.Join(",", GetParams(plugin).Select(d => d.ToString("0.#")))}]");
            }
        }

        // ==================================================================
        //  投射：轮廓 + 主色标签 + 结论行
        // ==================================================================

        private static void RunProjectionCheck(Type pluginType, string cable2)
        {
            // 合格：结论绿字
            var frames = new List<ImageDisplayEvent<HImage>>();
            var ok = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiShape"] = RoiShapeNames.Rectangle,
                ["RoiParams"] = new[] { 189.0, 440.0, 0.0, 6.0, 5.0 },
                ["ExpectedColors"] = "灰",
                ["DisplayViewIndex"] = 1,
            });
            var okOutcome = RunWithImage(ok, cable2, frames: frames);

            Check("【投射】只投一帧、投到 1 号视图窗口",
                okOutcome.Success && frames.Count == 1 && frames[0].ViewIndex == 1,
                $"帧数={frames.Count} 窗口={(frames.Count > 0 ? frames[0].ViewIndex : 0)}");
            if (frames.Count != 1) return;

            var marks = frames[0].Annotations ?? new List<MeasureAnnotation>();
            int lines = marks.Count(m => m.Type == MeasureType.Line);
            Check("【投射】矩形轮廓 = 4 条线 + 主色标签 + 结论行（共 6 条标注）",
                lines == 4 && marks.Count == 6,
                $"线={lines} 总={marks.Count}");

            var verdictMark = marks.FirstOrDefault(m => m.Text == okOutcome.Verdict);
            var labelMark = marks.FirstOrDefault(m => m.Text != null && m.Text.StartsWith("灰 "));
            Check("【投射·合格】结论与主色标签都是绿字",
                verdictMark != null && verdictMark.Color == "green"
                && labelMark != null && labelMark.Color == "green",
                $"结论色={verdictMark?.Color} 标签={labelMark?.Text}（{labelMark?.Color}）");

            // 圆形：轮廓是 32 段折线（不是方框）
            var circleFrames = new List<ImageDisplayEvent<HImage>>();
            var circle = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiShape"] = RoiShapeNames.Circle,
                ["RoiParams"] = new[] { 189.0, 440.0, 7.0, 0.0, 0.0 },
                ["ExpectedColors"] = "灰",
                ["DisplayViewIndex"] = 1,
            });
            RunWithImage(circle, cable2, frames: circleFrames);
            var circleMarks = circleFrames.Count == 1
                ? circleFrames[0].Annotations ?? new List<MeasureAnnotation>()
                : new List<MeasureAnnotation>();
            Check("【投射·圆形】轮廓按 32 段折线画（圆要像圆，不能画成方框）",
                circleMarks.Count(m => m.Type == MeasureType.Line) == 32,
                $"线={circleMarks.Count(m => m.Type == MeasureType.Line)}");

            // 不合格：结论红字
            var badFrames = new List<ImageDisplayEvent<HImage>>();
            var bad = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiShape"] = RoiShapeNames.Rectangle,
                ["RoiParams"] = new[] { 189.0, 440.0, 0.0, 6.0, 5.0 },
                ["ExpectedColors"] = "红",
                ["DisplayViewIndex"] = 1,
            });
            var badOutcome = RunWithImage(bad, cable2, frames: badFrames);
            var badMarks = badFrames.Count == 1
                ? badFrames[0].Annotations ?? new List<MeasureAnnotation>()
                : new List<MeasureAnnotation>();
            Check("【投射·不合格】结论是红字",
                badMarks.Any(m => m.Text == badOutcome.Verdict && m.Color == "red"),
                $"结论=[{badOutcome.Verdict}] 色={badMarks.FirstOrDefault(m => m.Text == badOutcome.Verdict)?.Color}");
        }

        // ==================================================================
        //  示教（一键填期望）与参数落盘
        // ==================================================================

        private static void RunTeachAndPersistenceCheck(Type pluginType, string cable2)
        {
            // 一键把试算出的主色填成期望颜色
            var teacher = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiShape"] = RoiShapeNames.Rectangle,
                ["RoiParams"] = new[] { 189.0, 440.0, 0.0, 6.0, 5.0 },
                ["DisplayViewIndex"] = 0,
            });

            HOperatorSet.ReadImage(out HObject raw, cable2);
            var image = new HImage(raw);
            raw?.Dispose();
            try
            {
                teacher.GetType().GetProperty("DisplayImage")!.SetValue(teacher, image);
                teacher.GetType().GetMethod("TryPreviewAnalyze")!.Invoke(teacher, null);
                string summary = (string)teacher.GetType().GetProperty("ResultSummary")!.GetValue(teacher)!;
                Check("【试算】给出主色、占比、中位色与明细（现场据此判断框得对不对）",
                    summary.Contains("主色") && summary.Contains("明细") && summary.Contains("中位色"),
                    summary.Replace("\r\n", " | "));

                teacher.GetType().GetMethod("ApplyPreviewAsExpected")!.Invoke(teacher, null);
                Check("【示教】「把主色填成期望颜色」一键成配方（省得手打颜色词打错）",
                    Read<string>(teacher, "ExpectedColors") == "灰",
                    $"期望颜色=[{Read<string>(teacher, "ExpectedColors")}]");
            }
            finally
            {
                image.Dispose();
            }

            // 参数落盘往返
            var step = new ActionStep("", "区域颜色检查", PluginTypeName, "区域颜色检查_0");
            step.SetInputValue("RoiShape", RoiShapeNames.Circle);
            step.SetInputValue("RoiParams", new[] { 189.0, 440.0, 7.0, 0.0, 0.0 });
            step.SetInputValue("PreviewImagePath", "");
            step.SetInputValue("ExpectedColors", "红,玫红");
            step.SetInputValue("MinShare", 0.75);
            step.SetInputValue("SamplePoints", 250);
            step.SetInputValue("DisplayViewIndex", 2);
            step.SetInputValue("BlueHueTo", 225.0);

            var plugin = CreatePlugin(pluginType, new Dictionary<string, object>());
            plugin.Initialize(step);
            plugin.OnConfirm(step);

            var reopened = CreatePlugin(pluginType, new Dictionary<string, object>());
            reopened.Initialize(step);

            bool ok = Read<string>(reopened, "RoiShape") == RoiShapeNames.Circle
                   && SameNumbers(GetParams(reopened), new[] { 189.0, 440.0, 7.0, 0.0, 0.0 }, 3)
                   && Read<string>(reopened, "ExpectedColors") == "红,玫红"
                   && Math.Abs(Read<double>(reopened, "MinShare") - 0.75) < 1e-6
                   && Read<int>(reopened, "SamplePoints") == 250
                   && Read<int>(reopened, "DisplayViewIndex") == 2
                   && Math.Abs(Read<double>(reopened, "BlueHueTo") - 225.0) < 1e-6;

            Check("参数能随方案落盘并回填（形状 / 采样区 / 期望颜色 / 最小占比 / 采样点数 / 投射窗口 / 颜色阈值）",
                ok,
                $"形状={Read<string>(reopened, "RoiShape")} 期望=[{Read<string>(reopened, "ExpectedColors")}] "
                + $"MinShare={Read<double>(reopened, "MinShare")} SamplePoints={Read<int>(reopened, "SamplePoints")} "
                + $"DisplayViewIndex={Read<int>(reopened, "DisplayViewIndex")} BlueHueTo={Read<double>(reopened, "BlueHueTo")}");
        }

        // ==================================================================
        //  上游图自动上屏（用户在真机上反馈的：配置界面里"图像绑不上上游"）
        // ==================================================================

        private static void RunUpstreamImageCheck(Type pluginType, string cable2, string cable1)
        {
            // 前提：配置窗口的「执行」会执行上游链，并把上游图**灌进配置实例的 Image 端口**
            // （PluginTestRunner.BridgeInputs 就是这么做的）。插件侧只要盯着端口就能把图搬到画布上。
            var plugin = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiShape"] = RoiShapeNames.Rectangle,
                ["RoiParams"] = new[] { 189.0, 440.0, 0.0, 6.0, 5.0 },
                ["ExpectedColors"] = "灰",
                ["DisplayViewIndex"] = 0,
            });

            HOperatorSet.ReadImage(out HObject raw, cable2);
            var upstream = new HImage(raw);
            raw?.Dispose();

            HOperatorSet.ReadImage(out HObject raw1, cable1);
            var other = new HImage(raw1);
            raw1?.Dispose();

            try
            {
                // 灌端口 = 模拟试运行的桥接动作
                ((IInputPort)plugin.GetType().GetProperty("Image")!.GetValue(plugin)!).Value = upstream;

                var display = plugin.GetType().GetProperty("DisplayImage")!.GetValue(plugin) as HImage;
                Check("【上游图】上游图灌进 Image 端口后，自动显示到配置界面的画布上",
                    display != null && display.IsInitialized(),
                    display == null ? "(画布上还是空的)" : "已上屏");

                int w = -1, h = -1, w2 = -1, h2 = -1;
                display?.GetImageSize(out w, out h);
                upstream.GetImageSize(out int uw, out int uh);
                Check("【上游图】画布上显示的就是上游那张图（尺寸一致）",
                    w == uw && h == uh, $"画布 {w}x{h} / 上游 {uw}x{uh}");

                // 拷一份的意义：上游句柄归框架管，我们只显示自己的副本 —— 上游释放后画布必须还在
                upstream.Dispose();
                bool alive = display != null && display.IsInitialized();
                display?.GetImageSize(out w2, out h2);
                Check("【上游图】显示的是自己拷的一份：上游句柄释放后画布仍然可用（不会变成空句柄）",
                    alive && w2 == uw && h2 == uh, $"释放上游后画布 {w2}x{h2}");

                // 换一张上游图（模拟上游换产品）→ 画布跟着换
                ((IInputPort)plugin.GetType().GetProperty("Image")!.GetValue(plugin)!).Value = other;
                var display2 = plugin.GetType().GetProperty("DisplayImage")!.GetValue(plugin) as HImage;
                int w3 = -1, h3 = -1;
                display2?.GetImageSize(out w3, out h3);
                other.GetImageSize(out int ow, out int oh);
                Check("【上游图】上游换成另一张图（换产品）→ 画布跟着换",
                    w3 == ow && h3 == oh, $"画布 {w3}x{h3} / 新上游 {ow}x{oh}");

                // 打开配置时：有上游图就不该去读（可能失效的）示意图路径
                var reopen = CreatePlugin(pluginType, new Dictionary<string, object>
                {
                    ["RoiShape"] = RoiShapeNames.Rectangle,
                    ["RoiParams"] = new[] { 189.0, 440.0, 0.0, 6.0, 5.0 },
                    ["ExpectedColors"] = "灰",
                    ["DisplayViewIndex"] = 0,
                    ["PreviewImagePath"] = @"Z:\根本没有这个文件.png",
                });
                ((IInputPort)reopen.GetType().GetProperty("Image")!.GetValue(reopen)!).Value = other;
                reopen.GetType().GetMethod("OnViewLoaded")!.Invoke(reopen, null);
                string hint = (string)reopen.GetType().GetProperty("Hint")!.GetValue(reopen)!;
                Check("【上游图】打开配置时优先用上游图，不去读（可能失效的）示意图路径",
                    hint.Contains("上游图像") && !hint.Contains("不存在"),
                    hint);
            }
            finally
            {
                other.Dispose();
            }
        }

        // ==================================================================
        //  小工具
        // ==================================================================

        private readonly struct Outcome
        {
            internal Outcome(bool success, string error, string color, double share, string table, string verdict, bool passed)
            {
                Success = success;
                Error = error;
                Color = color;
                Share = share;
                Table = table;
                Verdict = verdict;
                Passed = passed;
            }

            internal bool Success { get; }
            internal string Error { get; }
            internal string Color { get; }
            internal double Share { get; }
            internal string Table { get; }
            internal string Verdict { get; }
            internal bool Passed { get; }
        }

        /// <summary>按形状 + 采样区 + 期望颜色跑一趟</summary>
        private static Outcome RunOn(Type pluginType, string imagePath, string shape, double[] pars,
            string expected, double minShare = 0.60)
        {
            var plugin = CreatePlugin(pluginType, new Dictionary<string, object>
            {
                ["RoiShape"] = shape,
                ["RoiParams"] = pars,
                ["ExpectedColors"] = expected,
                ["MinShare"] = minShare,
                ["DisplayViewIndex"] = 0,
            });
            return RunWithImage(plugin, imagePath);
        }

        private static VisionPluginBase CreatePlugin(Type pluginType, IDictionary<string, object> config)
        {
            var plugin = (VisionPluginBase)Activator.CreateInstance(pluginType)!;
            plugin.InstanceName = "区域颜色检查_0";

            if (config.Count == 0) return plugin;

            var step = new ActionStep("", "区域颜色检查", PluginTypeName, "区域颜色检查_0");
            foreach (var kv in config) step.SetInputValue(kv.Key, kv.Value);

            plugin.Initialize(step);
            return plugin;
        }

        private static Outcome RunWithImage(VisionPluginBase plugin, string imagePath, bool toGray = false,
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
                    // 必须转成独立的 HImage 句柄：直接把 HObject 塞给 InputPort<HImage> 会抛 InvalidCastException
                    HOperatorSet.AccessChannel(image, out HObject channel, 1);
                    try { grayFeed = new HImage(channel); }
                    finally { channel.Dispose(); }
                    feed = grayFeed;
                }

                ((IInputPort)plugin.GetType().GetProperty("Image")!.GetValue(plugin)!).Value = feed;

                var context = new ExecutionContext(
                    new StubLog(), new FlowSession { FlowName = "区域颜色断言" },
                    new WorkspaceContext(), new CancellationTokenSource().Token);

                // 无 WPF 宿主时 PublishOnUIThread 降级为同步直调，所以订阅后能同步收到帧
                Action<ImageDisplayEvent<HImage>>? onPreview = frames == null
                    ? null
                    : e => { lock (frames) frames.Add(e); };
                if (onPreview != null) GlobalEventBus.Subscribe(onPreview);

                try
                {
                    // Success 由引擎的 Execute 预置为 true，直接驱动 RunAlgorithm 时要照引擎补上
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
                    PortValue(plugin, "Color") as string ?? "",
                    PortValue(plugin, "Share") is double s ? s : 0,
                    PortValue(plugin, "Table") as string ?? "",
                    PortValue(plugin, "Verdict") as string ?? "",
                    PortValue(plugin, "Result") is bool b && b);
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

        private static System.Collections.IList GetCanvasRois(VisionPluginBase plugin)
            => (System.Collections.IList)plugin.GetType().GetProperty("CanvasRois")!.GetValue(plugin)!;

        private static double[] GetParams(VisionPluginBase plugin)
            => (double[])plugin.GetType().GetProperty("RoiParams")!.GetValue(plugin)!;

        private static DrawShapeType GetShapeType(object roi)
            => (DrawShapeType)roi.GetType().GetProperty("ShapeType")!.GetValue(roi)!;

        /// <summary>逐个数比（容差 1e-9）；只比前 count 个 —— 圆只用到前 3 个参数</summary>
        private static bool SameNumbers(double[] a, double[] b, int count = 5)
        {
            if (a == null || a.Length < count || b.Length < count) return false;
            for (int i = 0; i < count; i++)
                if (Math.Abs(a[i] - b[i]) > 1e-9) return false;
            return true;
        }
    }
}
