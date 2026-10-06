using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Core.Events;
using Core.Halcon.Color;
using Core.Interfaces;
using HalconDotNet;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 合成复杂颜色图 + **流程式**验证：脚本现场生成一张难图（不是拿现成样图），
    /// 然后按现场的方式走完整条链 —— 图像采集(读盘) → 颜色序列检查 → 区域颜色检查×2，
    /// 真编译（FlowCompiler）真执行，从日志断言三个算子的输出。
    ///
    /// 为什么图要"复杂"
    /// ---------
    /// 这张图把颜色判据最容易翻车的情形全部摆进去：
    ///   · 12 根线束里 玫红(335°)与红(2°) 紧挨着（分界 350°）、紫(271°)与蓝(221°) 跨着分界 230°；
    ///   · 棕是低饱和暖灰（s≈0.41，刚好压在无彩/有彩分界 0.45 之下）；
    ///   · 白线(250)贴着浅灰背景(195~225) —— 明度上几乎不可分，靠规整化补回；
    ///   · 灰线(152)任何判据都抓不到，**必须**靠规整化按等间距补出，颜色取自规则窗采样；
    ///   · 亮度渐变背景 + 采样带上的 +22 高光带（盖住前 4 根的采样行，等比不改色相）
    ///     + 全图 ±4 噪声 —— 中位剖面与 Trimmed 采样必须把它们全部吸收掉。
    ///
    /// 为什么还要跑一遍 16 位
    /// ---------
    /// 高位深归一化（NormalizeTo8BitIfHighBitDepth）是颜色算子新增的自愈路径：
    /// 同一张图 ×40 转 uint2 落成 TIFF 再跑，判据阈值（黑 80 / 白 195 等）是按 8 位标定的，
    /// 不归一就是"黑判不出、白满天飞"。16 位图上必须跑出**同一条黄金序列**，
    /// 且三个颜色节点的 warning 里都留下"已等比压到 8 位"的痕迹。
    /// 生成器特意让 8 位图的最高灰阶 = 255（指示灯高光点）、×40 后 = 10200，
    /// 归一化系数 255/10200 恰好 = 1/40 —— 16 位跑出的数值与 8 位一字不差，断言才公平。
    ///
    /// 为什么走流程而不是直调插件
    /// ---------
    /// 直调（BlobDetectChecks 那样）验证的是算法；走流程额外验证：
    /// 端口按名连线、采集→算子的图像传递、配置灌值（RoiParams/ExpectedColors 落到实例）、
    /// 多算子同图各取所需 —— 这才是现场真正跑的那条链路。
    /// </summary>
    internal static class ColorSyntheticChecks
    {
        internal const string FlowName = "合成颜色验证";

        private const string CollectTypeName =
            "Plugin.ImageAcquisition.ImageAcquisitionPlugin, Plugin.ImageAcquisition, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
        private const string ColorCheckTypeName =
            "Plugin.ColorCheck.ColorCheckPlugin, Plugin.ColorCheck, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
        private const string RegionColorTypeName =
            "Plugin.ColorRegion.RegionColorPlugin, Plugin.ColorRegion, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        /// <summary>12 根线束的颜色序列（黄金参照，与调色板逐根对应）</summary>
        private const string Golden12 = "黑,棕,玫红,红,橙,黄,白,灰,紫,蓝,绿,红";

        public static void Run()
        {
            Section("[C9] 合成复杂颜色图（生成器）+ 流程式验证（采集 → 颜色序列 → 区域×2）");

            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            var modules = Path.Combine(repoRoot, "Modules");

            // 三个插件程序集先装进默认 ALC：编译器按 PluginTypeName 实例化，不装就找不到类型
            string loadError = "";
            foreach (var dll in new[] { "Plugin.ImageAcquisition.dll", "Plugin.ColorCheck.dll", "Plugin.ColorRegion.dll" })
            {
                try { Assembly.LoadFrom(Path.Combine(modules, dll)); }
                catch (Exception ex) { loadError += $"{dll}: {ex.Message}; "; }
            }
            Check("采集 / 颜色序列 / 区域颜色 三个插件程序集可装载", loadError.Length == 0, loadError);

            // ---- 1. 生成合成图（8 位 PNG + 16 位 TIFF）----
            // 落在仓库的 Image\颜色\ 里：既给自动化断言用，也让人能在软件里手动打开验证
            var imgDir = Path.Combine(repoRoot, "Image", "颜色");
            Directory.CreateDirectory(imgDir);
            string png8 = Path.Combine(imgDir, "合成颜色_8bit.png");
            string tiff16 = Path.Combine(imgDir, "合成颜色_16bit.tif");
            GenerateImages(png8, tiff16);
            Check("合成图生成成功（8 位 PNG + 16 位 TIFF，同一张图的两个位深）",
                File.Exists(png8) && File.Exists(tiff16), $"{png8} | {tiff16}");

            // ---- 1.5 像素抽检：生成器画没画对，读图直接验（抓生成器回归，不依赖插件）----
            // 走 ColorSampler.TryReadPixels 取色（与插件同一条取色路径），断言值取三通道最大
            HOperatorSet.ReadImage(out HObject diagImg, png8);
            try
            {
                int[] centers = Enumerable.Range(0, 12).Select(k => 78 + k * 36).ToArray();
                var rows = centers.Select(_ => 400.0).ToArray();
                ColorSampler.TryReadPixels(new HImage(diagImg), rows, centers.Select(c => (double)c).ToArray(),
                    out int[][] ch, out string sampleErr);

                var wrong = new List<string>();
                if (ch == null || ch.Length < 3)
                {
                    wrong.Add("取色失败：" + sampleErr);
                }
                else
                {
                    for (int k = 0; k < 12; k++)
                    {
                        var (er, eg, eb) = WirePalette[k];
                        // 高光带（rows 390..410）盖住前 4 根的采样行：期望值 = 调色板 +22
                        bool highlighted = k < 4;
                        int expMx = Math.Max(Math.Max(
                            highlighted ? Math.Min(255, er + 22) : er,
                            highlighted ? Math.Min(255, eg + 22) : eg),
                            highlighted ? Math.Min(255, eb + 22) : eb);
                        int mxAt = Math.Max(ch[0][k], Math.Max(ch[1][k], ch[2][k]));
                        if (Math.Abs(mxAt - expMx) > 10)
                            wrong.Add($"线{k + 1}@col{centers[k]}: got({ch[0][k]},{ch[1][k]},{ch[2][k]}) 期望({er},{eg},{eb}) mx={mxAt}≈{expMx}");
                    }
                }
                Check("【像素抽检】12 根线束中心的亮度与调色板一致（含高光带 +22）",
                    wrong.Count == 0, string.Join("; ", wrong));
            }
            finally { diagImg.Dispose(); }

            // ---- 2. 编程式构造流程：采集 → 序列 → 区域红 → 区域紫，并落盘成 .vms ----
            // （方案里 DisplayViewIndex=1：在软件里手动"执行"时，结果直接投到 1 号视图窗口；
            //   自动化跑批时 RunOnce 会临时改成 0，避免投射帧混进断言）
            var steps = BuildSteps(png8);

            var flowModel = new FlowModel { FlowName = FlowName };
            foreach (var s in steps) flowModel.Steps.Add(s);
            var solution = new SolutionModel();
            solution.Flows.Clear();   // SolutionModel 自带三条默认骨架流程（Home/Main/End）：不清掉会一起落盘/切换
            solution.Flows.Add(flowModel);
            string vmsPath = Path.Combine(repoRoot, "解决方案", "合成颜色验证.vms");
            File.WriteAllText(vmsPath, SolutionService.Serialize(solution));
            Check("流程方案已落盘（可在软件里直接打开）", File.Exists(vmsPath), vmsPath);

            var loaded = new SolutionService().LoadAsync(vmsPath).GetAwaiter().GetResult();
            var ld = loaded.Data;
            var loadedFlow = ld?.Flows.FirstOrDefault(f => f.FlowName == FlowName);
            Check("生产加载器能打开这份方案（主干 4 步：采集 → 序列 → 区域×2，连线完好）",
                loaded.Success && loadedFlow != null && loadedFlow.Steps.Count == 4
                && loadedFlow.Steps[1].GetLinkedAddress("Image") == "图像采集_0.Image",
                $"Success={loaded.Success} Msg={loaded.Message} Flows={ld?.Flows.Count} "
                + $"Steps={loadedFlow?.Steps.Count} "
                + $"Link={loadedFlow?.Steps[1].GetLinkedAddress("Image") ?? "(null)"} "
                + $"步骤={string.Join(" → ", loadedFlow?.Steps.Select(s => s.StepName) ?? Array.Empty<string>())}");

            // ---- 3. 8 位图跑一遍 ----
            var r8 = RunOnce(steps, png8);
            Check("【8 位】流程编译执行、四步全部 Success",
                r8.Compiled && r8.Threw.Length == 0 && r8.StepStates.All(s => s.EndsWith("=Success")), r8.Diary);
            Check("【8 位】序列检查报出 12 根，与黄金序列逐位一致",
                r8.Infos.Any(l => l.Contains($"找到 12 根：{Golden12}")), r8.Diary);
            Check("【8 位】线序判定合格（全通配配方，结论带配方名）",
                r8.Infos.Any(l => l.Contains("判定：线序正确，12 芯") && l.Contains("12 芯")), r8.Diary);
            Check("【8 位】红色指示灯：主色 红、判定颜色正确",
                r8.Infos.Any(l => l.Contains("主色 红（占")) && r8.Infos.Any(l => l.Contains("颜色正确：红")), r8.Diary);
            Check("【8 位】紫色色块：主色 紫、判定颜色正确",
                r8.Infos.Any(l => l.Contains("主色 紫（占")) && r8.Infos.Any(l => l.Contains("颜色正确：紫")), r8.Diary);
            Check("【8 位】不触发高位深归一（8 位图没有归一的道理）",
                !r8.Warns.Any(w => w.Contains("高位深")), string.Join(" | ", r8.Warns));

            // ---- 4. 16 位图跑同一套流程：黄金序列必须一字不差 ----
            var r16 = RunOnce(steps, tiff16);
            Check("【16 位】流程编译执行、四步全部 Success",
                r16.Compiled && r16.Threw.Length == 0 && r16.StepStates.All(s => s.EndsWith("=Success")), r16.Diary);
            Check("【16 位】高位深归一后仍扫出同一条黄金序列（黑能判出、白不泛滥）",
                r16.Infos.Any(l => l.Contains($"找到 12 根：{Golden12}")), r16.Diary);
            Check("【16 位】线序判定合格",
                r16.Infos.Any(l => l.Contains("判定：线序正确，12 芯")), r16.Diary);
            Check("【16 位】两个区域算子的主色与判定与 8 位一致",
                r16.Infos.Any(l => l.Contains("主色 红（占")) && r16.Infos.Any(l => l.Contains("颜色正确：红"))
                && r16.Infos.Any(l => l.Contains("主色 紫（占")) && r16.Infos.Any(l => l.Contains("颜色正确：紫")), r16.Diary);
            Check("【16 位】三个颜色节点都留下高位深归一的 warning（数值变小的原因可查）",
                r16.Warns.Count(w => w.Contains("高位深")) >= 3, string.Join(" | ", r16.Warns));
        }

        // ==================================================================
        //  驱动：改采集路径 → 编译 → 跑一趟 → 收三级日志
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

            internal string Diary => (Compiled ? Threw : CompileErrors)
                + $" ｜ 步骤状态={string.Join(" ", StepStates)}"
                + $" ｜ Warn={string.Join(" | ", Warns)}"
                + $" ｜ Err={string.Join(" | ", Errors)}";
        }

        /// <summary>
        /// 构造流程四步：图像采集 → 颜色序列检查 → 区域颜色检查×2（含配置与连线）。
        /// 采集的 FilePath 由调用方按要跑的位深现填。
        /// </summary>
        private static StepModel[] BuildSteps(string imagePath)
        {
            var collect = new ActionStep("", "图像采集", CollectTypeName, "图像采集_0");
            var seq = new ActionStep("", "颜色序列检查", ColorCheckTypeName, "颜色序列_0");
            var redLight = new ActionStep("", "区域颜色检查", RegionColorTypeName, "区域红灯_0");
            var purplePatch = new ActionStep("", "区域颜色检查", RegionColorTypeName, "区域紫块_0");

            collect.SetInputValue("FilePath", imagePath);

            // 线束：12 根、间距 36px、垂直走向；phi=90° 让长轴沿行方向（线束走向），
            // 半长 320 只判走向，短轴半宽 210 → 剖面 421 点正好罩住 66..486 列（线束 66..486）
            seq.SetInputValue("RoiParams", new double[] { 400, 276, Math.PI / 2, 320.0, 210.0 });
            seq.SetInputValue("RecipeText", "12 芯 = " + string.Join(",", Enumerable.Repeat("*", 12)) + "\r\n");
            seq.SetInputValue("DisplayViewIndex", 1);

            // 红色指示灯：圆 ROI(r=55) 完全落在灯里(r=70)，高光点(255)随它一起被采到
            // （圆形参数约定 [行, 列, 半径, 0, 0] —— 半径在第 3 个槽）
            redLight.SetInputValue("RoiShape", "Circle");
            redLight.SetInputValue("RoiParams", new double[] { 250, 830, 55.0, 0.0, 0.0 });
            redLight.SetInputValue("ExpectedColors", "红");
            redLight.SetInputValue("MinShare", 0.6);
            redLight.SetInputValue("DisplayViewIndex", 1);

            // 紫色色块：矩形 ROI 完全落在色块内（rows 460..550 ⊂ 450..560）
            purplePatch.SetInputValue("RoiShape", "Rectangle");
            purplePatch.SetInputValue("RoiParams", new double[] { 505, 790, 0.0, 45.0, 45.0 });
            purplePatch.SetInputValue("ExpectedColors", "紫");
            purplePatch.SetInputValue("DisplayViewIndex", 1);

            foreach (var s in new[] { seq, redLight, purplePatch })
                s.SetLink("Image", new LinkReference(LinkKind.StepPort, collect.StepID, "Image", "图像采集_0.Image"));

            return new StepModel[] { collect, seq, redLight, purplePatch };
        }

        private static Outcome RunOnce(StepModel[] steps, string imagePath)
        {
            var outcome = new Outcome();

            steps[0].SetInputValue("FilePath", imagePath);

            // 自动化跑批时关掉投射（方案里是 1，供人在软件里手动执行用），
            // 避免投射帧与断言互相干扰
            for (int i = 1; i < steps.Length; i++)
                steps[i].SetInputValue("DisplayViewIndex", 0);

            var flowModel = new FlowModel { FlowName = FlowName };
            foreach (var s in steps) flowModel.Steps.Add(s);
            var solution = new SolutionModel();
            solution.Flows.Clear();   // SolutionModel 自带三条默认骨架流程（Home/Main/End）：不清掉会一起落盘/切换
            solution.Flows.Add(flowModel);

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

            try { compiled.Data.Run(ctx); }
            catch (Exception ex) { outcome.Threw = ex.Message; }
            finally
            {
                outcome.Infos.AddRange(log.Infos);
                outcome.Warns.AddRange(log.Warns);
                outcome.Errors.AddRange(log.Errors);
                foreach (var s in steps) outcome.StepStates.Add($"{s.StepName}={s.State}");
            }

            return outcome;
        }

        // ==================================================================
        //  合成图生成器：三个 byte 平面分别画，再 Compose3 成彩色
        // ==================================================================

        /// <summary>线束调色板（12 根，顺序 = 黄金序列）。颜色特意贴着判据分界挑：
        /// 玫红 335°（红/玫红分界 350）、棕压在无彩分界 s=0.45 之下、紫 271° 与蓝 221° 跨分界 230°</summary>
        private static readonly (int R, int G, int B)[] WirePalette =
        {
            (25, 25, 28),     // 黑
            (145, 115, 85),   // 棕（低饱和暖灰，压线）
            (215, 45, 115),   // 玫红（hue 335°，离红分界 350 还有 15°）
            (205, 45, 40),    // 红（hue 2°）
            (240, 145, 30),   // 橙
            (238, 212, 60),   // 黄
            (250, 250, 246),  // 白（明度贴着背景，判据抓不到，靠规整化补）
            (152, 152, 155),  // 灰（任何判据都抓不到，必须靠规整化）
            (128, 58, 195),   // 紫
            (42, 95, 205),    // 蓝
            (55, 175, 80),    // 绿
            (198, 52, 48),    // 红（复位：验证回归锚定不会把远端序列错位）
        };

        private static void GenerateImages(string png8, string tiff16)
        {
            HOperatorSet.GenImageConst(out HObject rPlane, "byte", 1200, 800);
            HOperatorSet.GenImageConst(out HObject gPlane, "byte", 1200, 800);
            HOperatorSet.GenImageConst(out HObject bPlane, "byte", 1200, 800);

            try
            {
                // 背景：左半亮灰横向渐变（10 档 195→222），右半暗灰 —— 同一张图两种极性的背景
                for (int i = 0; i < 10; i++)
                {
                    int gray = 195 + i * 3;
                    HOperatorSet.GenRectangle1(out HObject band, 0, i * 60, 799, i * 60 + 59);
                    Paint(rPlane, gPlane, bPlane, band, gray, gray, gray);
                    band.Dispose();
                }
                HOperatorSet.GenRectangle1(out HObject rightBg, 0, 600, 799, 1199);
                Paint(rPlane, gPlane, bPlane, rightBg, 70, 70, 72);
                rightBg.Dispose();

                // 12 根垂直线束（跨全高）+ 11px 暗缝（160：比暗线判据亮，不会被当成线）。
                // 缝必须足够宽：找线判据用「剖面亮度的众数」当缝/背景水平 —— 缝太窄（点数太少）
                // 众数会被高光带推亮的某根线抢走，暗线判据线跟着抬高，整条剖面连成一根被当背景丢弃
                for (int k = 0; k < 12; k++)
                {
                    int center = 78 + k * 36;
                    HOperatorSet.GenRectangle1(out HObject wire, 0, center - 12, 799, center + 12);
                    var (wr, wg, wb) = WirePalette[k];
                    Paint(rPlane, gPlane, bPlane, wire, wr, wg, wb);
                    wire.Dispose();

                    if (k < 11)
                    {
                        HOperatorSet.GenRectangle1(out HObject seam, 0, center + 13, 799, center + 23);
                        Paint(rPlane, gPlane, bPlane, seam, 160, 160, 162);
                        seam.Dispose();
                    }
                }

                // 高光带：盖住前 4 根线在采样行（390~410，5 条采样线是 396/398/400/402/404）上的全部宽度，
                // 颜色整体 +22 —— 加法不换色相，但把"棕"推到离明度上限 175 只差 8 的位置，压测无彩/有彩分界
                for (int k = 0; k < 4; k++)
                {
                    int center = 78 + k * 36;
                    var (hr, hg, hb) = WirePalette[k];
                    HOperatorSet.GenRectangle1(out HObject hl, 390, center - 12, 410, center + 12);
                    Paint(rPlane, gPlane, bPlane, hl,
                        Math.Min(255, hr + 22), Math.Min(255, hg + 22), Math.Min(255, hb + 22));
                    hl.Dispose();
                }

                // 红色指示灯：外圈→内圈径向渐变 + 纯白高光点（255：它就是全图最高灰阶，
                // ×40 后 10200，归一化系数 255/10200 = 1/40，16 位能一字不差地回到 8 位数值）
                HOperatorSet.GenCircle(out HObject lampOuter, 250, 830, 70);
                Paint(rPlane, gPlane, bPlane, lampOuter, 150, 34, 30);
                lampOuter.Dispose();
                HOperatorSet.GenCircle(out HObject lampMid, 250, 830, 55);
                Paint(rPlane, gPlane, bPlane, lampMid, 180, 40, 36);
                lampMid.Dispose();
                HOperatorSet.GenCircle(out HObject lampCore, 250, 830, 40);
                Paint(rPlane, gPlane, bPlane, lampCore, 200, 45, 40);
                lampCore.Dispose();
                HOperatorSet.GenCircle(out HObject lampSpec, 245, 815, 12);
                Paint(rPlane, gPlane, bPlane, lampSpec, 255, 255, 255);
                lampSpec.Dispose();

                // 紫色色块（ROI 的 90×90 完全落在这里面）
                HOperatorSet.GenRectangle1(out HObject purple, 450, 700, 560, 880);
                Paint(rPlane, gPlane, bPlane, purple, 125, 58, 195);
                purple.Dispose();

                // 装饰：黄圆 + 蓝块（不在任何 ROI 里，只增加图的复杂度）
                HOperatorSet.GenCircle(out HObject decoDot, 150, 1000, 35);
                Paint(rPlane, gPlane, bPlane, decoDot, 235, 210, 60);
                decoDot.Dispose();
                HOperatorSet.GenRectangle1(out HObject decoRect, 660, 1020, 740, 1100);
                Paint(rPlane, gPlane, bPlane, decoRect, 42, 95, 205);
                decoRect.Dispose();

                // 全图 ±4 噪声：中位剖面与 Trimmed 采样必须吸收掉，判据分界的余量都按它留的
                HOperatorSet.AddNoiseWhite(rPlane, out HObject rNoisy, 4.0);
                rPlane.Dispose();
                HOperatorSet.AddNoiseWhite(gPlane, out HObject gNoisy, 4.0);
                gPlane.Dispose();
                HOperatorSet.AddNoiseWhite(bPlane, out HObject bNoisy, 4.0);
                bPlane.Dispose();

                HOperatorSet.Compose3(rNoisy, gNoisy, bNoisy, out HObject color8);
                rNoisy.Dispose(); gNoisy.Dispose(); bNoisy.Dispose();

                // 8 位 PNG
                HOperatorSet.WriteImage(color8, "png", 0, png8);

                // 16 位 TIFF：转 uint2 后整体 ×40（最高灰阶 255×40 = 10200）
                HOperatorSet.ConvertImageType(color8, out HObject u16, "uint2");
                color8.Dispose();
                HOperatorSet.ScaleImage(u16, out HObject scaled, 40.0, 0.0);
                u16.Dispose();
                HOperatorSet.WriteImage(scaled, "tiff", 0, tiff16);
                scaled.Dispose();
            }
            finally
            {
                try { rPlane.Dispose(); } catch { }
                try { gPlane.Dispose(); } catch { }
                try { bPlane.Dispose(); } catch { }
            }
        }

        private static void Paint(HObject rPlane, HObject gPlane, HObject bPlane,
            HObject region, int r, int g, int b)
        {
            HOperatorSet.OverpaintRegion(rPlane, region, r, "fill");
            HOperatorSet.OverpaintRegion(gPlane, region, g, "fill");
            HOperatorSet.OverpaintRegion(bPlane, region, b, "fill");
        }
    }
}
