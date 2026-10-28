using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Core.Halcon.Color;
using Core.Interfaces;
using Core.Interfaces.Assets;
using HalconDotNet;
using Plugin.Matching;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 模板匹配（Plugin.Matching）的断言。
    ///
    /// 真值策略与 BlobDetectChecks 同款：**不依赖任何外部样图**，全部用 HALCON 现场合成
    /// 确定性图像（背景恒定 + 已知位置/已知形状的特征图案），期望值可笔算。
    /// 断言的是"同图回准、平移/旋转/缩放找回、多模板/配方驱动、序列化往返、失败语义、
    /// 数组端口对齐、涂抹掩膜、老方案迁移"这些契约。
    ///
    /// 测试图案：暗圆盘（灰 60，半径 55）+ 亮十字（灰 255，臂宽 20）——梯度特征丰富，
    /// shape model 在合成图上能稳定命中。
    /// </summary>
    internal static class MatchingChecks
    {
        private const double PatternRow = 150, PatternCol = 200;
        private const int ImgH = 400, ImgW = 520;
        private const double DiskRadius = 55;

        public static void Run()
        {
            Section("[Matching] 模板匹配插件（合成确定性图像验证）");

            RunLocateContract();
            RunTransformContract();
            RunScaleAndSerialize();
            RunAnisoScaleContract();
            RunNotFoundAndStaleness();
            RunCircleTemplate();
            RunSmearMask();
            RunNccOrigin();
            RunHalconSampleSolution();
            RunRecipeContract();
            RunRefImageAssetContract();
            RunMigration();
            RunRegressionFixes();
        }

        // ==================================================================
        //  ⑨ 回归：本轮修掉的缺陷（每条都对应当时"为什么冒烟看不见它"）
        // ==================================================================
        private static void RunRegressionFixes()
        {
            Section("[Matching] 回归：本轮修复的缺陷");

            // ---- ① P0：学习成功后状态栏不得报失败 ----
            // 病根：成功文案里取了迁移字段 TemplateRect[3]/[4]，新方案下它恒为空数组 →
            // IndexOutOfRangeException → 被 CreateTemplate 的 catch 吞成"创建模板失败"，
            // 而模型其实已经学好了。断言只看 Model.Length 是看不见的，所以必须看状态栏。
            {
                var (creator, runtime, scene) = BuildTemplate();
                try
                {
                    Check("【回归·P0】学习成功后状态栏不是失败（早先每次学习都报创建模板失败）",
                        creator.StatusLevel != StatusLevel.Error,
                        $"StatusLevel={creator.StatusLevel} 文案='{creator.StatusMessage}'");
                    Check("【回归·P0】成功文案给出特征区域尺寸（不是 0×0、也不是空）",
                        creator.StatusMessage.Contains("特征区域")
                        && !creator.StatusMessage.Contains("0×0"),
                        creator.StatusMessage);
                }
                finally
                {
                    creator.Dispose(); runtime.Dispose(); scene.Dispose();
                }
            }

            // ---- ② P1：轮廓放置的"度 → 弧度"换算 ----
            // 直接用一根已知角度的细长矩形 XLD 验证：给 30° 就应当真的转 30°。
            // 早先双重转换时，30° 会被转成 0.5° 左右（肉眼只看到"轮廓不跟着转"）。
            {
                HOperatorSet.GenRectangle2ContourXld(out HObject bar, 0, 0, 0, 60, 4);
                try
                {
                    using var p0 = MatchingPlugin.PlaceContoursAtPoses(
                        bar, new HTuple(100.0), new HTuple(100.0), new HTuple(0.0));
                    using var p30 = MatchingPlugin.PlaceContoursAtPoses(
                        bar, new HTuple(100.0), new HTuple(100.0), new HTuple(30.0));

                    double phi0 = PhiOf(p0), phi30 = PhiOf(p30);
                    Check("【回归·P1】轮廓放置按【度】换算：给 0° 就是 0°", Math.Abs(phi0) <= 1.0,
                        $"Phi={phi0:0.###}°");
                    Check("【回归·P1】轮廓放置按【度】换算：给 30° 就转 30°（早先被双重转成 ≈0.5°）",
                        Math.Abs(phi30 - 30.0) <= 1.0, $"Phi={phi30:0.###}°");
                }
                finally
                {
                    bar.Dispose();
                }
            }

            // ---- ③ P2：删除条目必须释放模型句柄 ----
            {
                var (creator, runtime, scene) = BuildTemplate();
                try
                {
                    var doomed = creator.Library[0];
                    creator.EditingEntry = doomed;
                    creator.DeleteSelectedEntry();
                    Check("【回归·P2】删除条目时释放了模型句柄（条目已从库里摘掉，Dispose 够不着它）",
                        doomed.RuntimeModelId == null && doomed.RuntimeContours == null,
                        $"RuntimeModelId={(doomed.RuntimeModelId == null ? "已释放" : "仍持有")}");
                }
                finally
                {
                    creator.Dispose(); runtime.Dispose(); scene.Dispose();
                }
            }

            // ---- ④ P2：掩膜口径一致 → 换条目来回切不应凭空提示"需重新学习" ----
            {
                var (creator, runtime, scene) = BuildTemplate();
                try
                {
                    creator.AddTemplateEntry("模板2");
                    creator.EditingEntry = creator.Library[1];
                    creator.EditingEntry = creator.Library[0];   // 切回已学习的第一条

                    Check("【回归·P2】换条目再切回，不误报需重新学习",
                        !creator.IsModelStale,
                        $"徽标='{creator.TemplateStatusText}'");
                }
                finally
                {
                    creator.Dispose(); runtime.Dispose(); scene.Dispose();
                }
            }

            // ---- ⑤ P2：运行实例不做配置态工作（不把参考图搬进画布） ----
            // 这里刻意用 ApplyConfigValues 而不是 Initialize：流程编译器构造运行实例走的就是前者，
            // 只有"打开配置界面"才走 Initialize。用错入口这个断言就失去意义了。
            {
                var (creator, runtime, scene) = BuildTemplate();
                try
                {
                    var compiled = new MatchingPlugin { InstanceName = "匹配_编译实例" };
                    compiled.ApplyConfigValues(ConfirmToStepData(creator));

                    Check("【回归·P2】编译实例不持有显示用参考图（只读该读的，不渲染预览）",
                        compiled.DisplayImage == null,
                        $"DisplayImage={(compiled.DisplayImage == null ? "null" : "非 null")}");
                    Check("【回归·P2】编译实例仍然拿得到模板库（隔离不能把功能也隔掉）",
                        compiled.Library.Count >= 1, $"库 {compiled.Library.Count} 条");
                    compiled.Dispose();

                    // 对照：走 Initialize（= 打开配置界面）时必须恢复画布与预览
                    var reopened = new MatchingPlugin { InstanceName = "匹配_重开" };
                    reopened.Initialize(ConfirmToStepData(creator));
                    Check("【回归·P2】对照：配置态走 Initialize 时预览照常恢复（隔离没矫枉过正）",
                        reopened.Library.Count >= 1 && reopened.EditingEntry != null,
                        $"库 {reopened.Library.Count} 条，EditingEntry={(reopened.EditingEntry?.Name ?? "null")}");
                    reopened.Dispose();
                }
                finally
                {
                    creator.Dispose(); runtime.Dispose(); scene.Dispose();
                }
            }

            // ---- ⑥ P3：条目区域损坏 → 给能照着修的中文提示，而不是索引越界 ----
            {
                var (creator, runtime, scene) = BuildTemplate();
                try
                {
                    creator.EditingEntry!.Rect = Array.Empty<double>();   // 模拟方案被手工改坏
                    var stepData = ConfirmToStepData(creator);
                    var broken = new MatchingPlugin { InstanceName = "匹配_坏条目" };
                    broken.Initialize(stepData);
                    broken.Image.Value = scene;
                    broken.Execute(MakeContext(new StubLog()));

                    Check("【回归·P3】条目区域参数损坏 → 失败原因写明区域参数损坏",
                        broken.Success.Value is false
                        && (broken.ErrorMessage.Value as string ?? "").Contains("区域参数损坏"),
                        $"Success={broken.Success.Value} Err='{broken.ErrorMessage.Value}'");
                    broken.Dispose();
                }
                finally
                {
                    creator.Dispose(); runtime.Dispose(); scene.Dispose();
                }
            }

            // ---- ⑦ P3：缩放上下限相等（退化解）不再让学习失败 ----
            {
                var scene = BuildScene(PatternRow, PatternCol);
                var creator = new MatchingPlugin { InstanceName = "匹配_退化缩放" };
                try
                {
                    creator.DisplayImage = scene;
                    creator.AddTemplateEntry("退化");
                    creator.CanvasShape = RoiShapeNames.Rectangle;
                    creator.CanvasRect = new double[] { PatternRow, PatternCol, 0, 80, 80 };
                    creator.EditingEntry!.ScaleEnabled = true;
                    creator.EditingEntry!.ScaleMin = 1.0;
                    creator.EditingEntry!.ScaleMax = 1.0;   // 完全退化
                    creator.CreateTemplate();

                    Check("【回归·P3】缩放上下限相等时仍能学习（撑开最小窗口而不是报错）",
                        creator.StatusLevel != StatusLevel.Error
                        && creator.Library[0].Model.Length > 0,
                        $"StatusLevel={creator.StatusLevel} 文案='{creator.StatusMessage}'");
                }
                finally
                {
                    creator.Dispose();
                    scene.Dispose();
                }
            }
        }

        /// <summary>取一组 XLD 的最小外接矩形角度（度）。用于验证轮廓放置的换算</summary>
        private static double PhiOf(HObject? xld)
        {
            if (xld == null || !xld.IsInitialized())
                return double.NaN;
            HOperatorSet.SmallestRectangle2Xld(xld, out _, out _, out HTuple phi, out _, out _);
            return phi.D * 180.0 / Math.PI;
        }

        // ==================================================================
        //  ① 同图定位 + 输出端口契约
        // ==================================================================
        private static void RunLocateContract()
        {
            var (creator, runtime, scene) = BuildTemplate();
            try
            {
                Check("【模板】学习成功且载荷随方案序列化",
                    creator.Library[0].Model.Length > 1000,
                    $"载荷 {creator.Library[0].Model.Length} 字符");
                Check("【模板】学习后参数未变不提示重学", !creator.IsModelStale, "");

                runtime.Image.Value = scene;
                runtime.Execute(MakeContext(new StubLog()));

                int count = Convert.ToInt32(runtime.MatchCount.Value);
                double score = Convert.ToDouble(runtime.Score.Value);
                double row = Convert.ToDouble(runtime.Row.Value);
                double col = Convert.ToDouble(runtime.Column.Value);
                double angle = Convert.ToDouble(runtime.Angle.Value);

                Check("【内核】同图定位命中 1 个实例", count == 1, $"MatchCount={count} Score={score:0.000}");
                Check("【内核】同图定位分数 ≥ 0.9", score >= 0.9, $"Score={score:0.000}");
                Check("【内核】位置亚像素级回准（150,200 ±0.5）",
                    Math.Abs(row - PatternRow) <= 0.5 && Math.Abs(col - PatternCol) <= 0.5,
                    $"({row:0.00},{col:0.00})");
                Check("【内核】同图角度 ≈ 0（±0.5°）", Math.Abs(angle) <= 0.5, $"Angle={angle:0.000}°");
                Check("【输出】位姿归一化图像已生成",
                    runtime.AlignedImage.Value is HImage a && a.IsInitialized(), "");
                Check("【输出】标注图已生成",
                    runtime.MeasureImage.Value is HImage m && m.IsInitialized(), "");

                // ---- 标注底图逐点保真 ----
                // 场景背景恒定 180，图案只在 (150,200) 附近；取底部两角（避开左上角文字框与图案）：
                // 底图被"缩进左上角"（离屏窗口漏掉 set_part）时，这两点会一起塌成同一个常数。
                var matchShot = runtime.MeasureImage.TypedValue;
                if (matchShot != null && matchShot.IsInitialized())
                {
                    double bl = PixelProbe.At(matchShot, ImgH - 10, 10);
                    double br = PixelProbe.At(matchShot, ImgH - 10, ImgW - 10);
                    Check("【输出】标注底图逐点保真（两下角 == 场景背景 180，底图没被缩进左上角）",
                        Math.Abs(bl - 180) <= 3 && Math.Abs(br - 180) <= 3,
                        $"({ImgH - 10},10)={bl:0.#} ({ImgH - 10},{ImgW - 10})={br:0.#}");
                }
                else
                {
                    Check("【输出】标注底图逐点保真（两下角 == 场景背景 180，底图没被缩进左上角）",
                        false, $"MeasureImage 为空；Err='{runtime.ErrorMessage.Value}'");
                }
                Check("【输出】MatchedTemplate 回传配方名",
                    (runtime.MatchedTemplate.Value as string ?? "") == "模板1",
                    $"MatchedTemplate='{runtime.MatchedTemplate.Value}'");
                Check("【数组】数组端口与实例数逐项对齐",
                    AsDoubles(runtime.Rows.Value).Length == 1
                    && AsDoubles(runtime.Columns.Value).Length == 1
                    && AsDoubles(runtime.Angles.Value).Length == 1
                    && AsDoubles(runtime.Scores.Value).Length == 1
                    && AsDoubles(runtime.Scales.Value).Length == 1,
                    $"rows={AsDoubles(runtime.Rows.Value).Length} scores={AsDoubles(runtime.Scores.Value).Length}");
                Check("【数组】未启用缩放时 Scales 恒为 1",
                    AsDoubles(runtime.Scales.Value).All(v => Math.Abs(v - 1.0) < 1e-9), "");
            }
            finally
            {
                creator.Dispose();
                runtime.Dispose();
                scene.Dispose();
            }
        }

        // ==================================================================
        //  ② 平移 / 旋转找回（定位的核心价值：目标动了照样找到）
        // ==================================================================
        private static void RunTransformContract()
        {
            var (creator, runtime, scene) = BuildTemplate();
            try
            {
                // 平移：图案从 (150,200) 搬到 (280,360)，位姿端口必须跟着走
                using var translated = BuildScene(280, 360);
                runtime.Image.Value = translated;
                runtime.Execute(MakeContext(new StubLog()));
                double tr = Convert.ToDouble(runtime.Row.Value);
                double tc = Convert.ToDouble(runtime.Column.Value);
                Check("【平移】图案搬到 (280,360) 后位置回准（±1）",
                    Convert.ToInt32(runtime.MatchCount.Value) == 1
                    && Math.Abs(tr - 280) <= 1 && Math.Abs(tc - 360) <= 1,
                    $"({tr:0.00},{tc:0.00})");

                // 旋转：整图转 10°，角度端口回准（取绝对值——回准量级是契约，符号随图像坐标系约定）
                using var rotated = RotateScene(scene, 10);
                runtime.Image.Value = rotated;
                runtime.Execute(MakeContext(new StubLog()));
                double rAngle = Convert.ToDouble(runtime.Angle.Value);
                double rScore = Convert.ToDouble(runtime.Score.Value);
                Check("【旋转】整图转 10° 后仍命中", Convert.ToInt32(runtime.MatchCount.Value) >= 1
                    && rScore >= 0.6, $"Score={rScore:0.000}");
                Check("【旋转】角度回准 10°（±0.5°）", Math.Abs(Math.Abs(rAngle) - 10) <= 0.5,
                    $"Angle={rAngle:0.000}°");
            }
            finally
            {
                creator.Dispose();
                runtime.Dispose();
                scene.Dispose();
            }
        }

        // ==================================================================
        //  ③ 缩放匹配（create_scaled_shape_model 链路）+ base64 往返即本组前提
        // ==================================================================
        private static void RunScaleAndSerialize()
        {
            var (creator, runtime, scene) = BuildTemplate(scaled: true);
            try
            {
                using var zoomed = ZoomScene(scene, 1.15);
                runtime.Image.Value = zoomed;
                runtime.Execute(MakeContext(new StubLog()));
                int count = Convert.ToInt32(runtime.MatchCount.Value);
                double scale = AsDoubles(runtime.Scales.Value).FirstOrDefault();
                double score = Convert.ToDouble(runtime.Score.Value);
                Check("【缩放】启用缩放模型后 1.15× 目标仍命中", count >= 1 && score >= 0.6,
                    $"count={count} Score={score:0.000}");
                Check("【缩放】缩放倍率回准 1.15（±0.05）", Math.Abs(scale - 1.15) <= 0.05,
                    $"Scale={scale:0.000}");

                // base64 序列化往返：runtime 就是 OnConfirm → Initialize(stepData) 的产物，
                // 能定位即证明载荷经方案字段往返无损
                Check("【序列化】base64 载荷经确认/加载往返无损",
                    count >= 1 && runtime.Success.Value is true,
                    $"载荷 {creator.Library[0].Model.Length} 字符 → 新实例匹配成功");
            }
            finally
            {
                creator.Dispose();
                runtime.Dispose();
                scene.Dispose();
            }
        }

        // ==================================================================
        //  ③b 各向异性缩放（行/列分别缩放）：create_aniso_shape_model 链路
        // ==================================================================

        /// <summary>各向异性图案的中心（与 BuildScene 的 (150,200) 错开，避免两个图案的断言互相借用真值）</summary>
        private const double AnisoRow = 200,
            AnisoCol = 260;

        /// <summary>
        /// 各向异性专用图案：暗底板（120×100，灰 60）+ 5 根 7px 细亮竖条 + 右下角不对称方块（灰 255）。
        ///
        /// 为什么不用 <see cref="BuildScene"/> 的"圆盘 + 十字"：那套图案 90° 旋转对称，
        /// 列向拉伸 1.2 时**各向异性与等比都能凑到 0.84 分**（本机实测），反向判据立不住。
        /// 细竖条把"列向拉伸"累积成逐条错位，等比模型分数会明显掉下来（实测 0.77），
        /// 各向异性模型则能回到 0.9997 —— 这就是"功能真的被使用"的判据来源。
        /// </summary>
        private static HImage BuildAnisoScene(double row, double col)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", ImgW, ImgH);
            HOperatorSet.GenImageProto(proto, out HObject img, 180);
            HOperatorSet.GenRectangle1(out HObject body, row - 50, col - 60, row + 50, col + 60);
            HOperatorSet.OverpaintRegion(img, body, 60, "fill");
            for (int i = 0; i < 5; i++)
            {
                double c0 = col - 56 + i * 24;
                HOperatorSet.GenRectangle1(out HObject bar, row - 44, c0, row + 20, c0 + 7);
                HOperatorSet.OverpaintRegion(img, bar, 255, "fill");
                bar.Dispose();
            }
            HOperatorSet.GenRectangle1(out HObject marker, row + 30, col + 34, row + 46, col + 56);
            HOperatorSet.OverpaintRegion(img, marker, 255, "fill");
            body.Dispose();
            marker.Dispose();
            proto.Dispose();
            var image = new HImage(img);
            img.Dispose();
            return image;
        }

        /// <summary>
        /// 各向异性档与等比档的同一个"列向拉伸 1.2 / 行向不变"图。
        /// 参数顺序踩过的坑：`zoom_image_factor(Image, Zoomed, ScaleWidth, ScaleHeight, Interpolation)`
        /// —— **宽度（列）在前**，与"行/列"的直觉顺序相反；写反了得到的是行向拉伸，断言会假红。
        /// </summary>
        private static HImage StretchColumns(HImage src, double colFactor, double rowFactor)
        {
            HOperatorSet.ZoomImageFactor(src, out HObject zoomed, colFactor, rowFactor, "constant");
            var result = new HImage(zoomed);
            zoomed.Dispose();
            return result;
        }

        /// <summary>
        /// 建一条"梳状图案"模板（aniso=true 走各向异性档，false 走等比档），并返回配置态 + 运行态。
        /// 每个 creator 拿一份自己的图像拷贝：`MatchingPlugin.Dispose()` 会释放 `DisplayImage`，
        /// 两个模板共用同一张图会让"先释放一个 creator 再跑另一个"变成用已释放的图。
        /// </summary>
        private static (MatchingPlugin creator, MatchingPlugin runtime) BuildAnisoTemplate(
            HImage scene,
            bool aniso,
            double rowMin,
            double rowMax,
            double colMin,
            double colMax
        )
        {
            var creator = new MatchingPlugin { InstanceName = "匹配_各向异性_建模板" };
            creator.DisplayImage = new HImage(scene);
            creator.AddTemplateEntry("各向异性");
            creator.CanvasShape = RoiShapeNames.Rectangle;
            creator.CanvasRect = new double[] { AnisoRow, AnisoCol, 0, 70, 70 };

            var entry = creator.EditingEntry!;
            if (aniso)
            {
                entry.AnisoScaleEnabled = true;
                entry.ScaleRowMin = rowMin;
                entry.ScaleRowMax = rowMax;
                entry.ScaleColMin = colMin;
                entry.ScaleColMax = colMax;
            }
            else
            {
                entry.ScaleEnabled = true;
                entry.ScaleMin = rowMin;
                entry.ScaleMax = colMax; // 等比只有一路：取能覆盖拉伸倍率的上限
            }

            creator.CreateTemplate();

            var runtime = new MatchingPlugin { InstanceName = "匹配_各向异性_断言" };
            runtime.Initialize(ConfirmToStepData(creator));
            return (creator, runtime);
        }

        /// <summary>
        /// 各向异性缩放的四组判据：
        /// ① 列向拉伸 1.2 的图必须命中、列缩放回准 1.2、行缩放回准 1.0；
        /// ② 反向：同一张图关掉各向异性（只开等比）分数必须明显下降（证明功能真被用到）；
        /// ③ 与同进程 HALCON 参考实现对拍（同图同参数）；
        /// ④ 指纹与 JSON（改列范围 → 需重学；往返保留新字段；老 JSON 走默认值）。
        /// 外加两条"不许静默"：NCC 下勾各向异性要明确告警；开关改了没重学要落 Warn。
        ///
        /// 本机实测（HALCON 23.05，520×400 合成图，拉伸 = zoom_image_factor(宽 1.2, 高 1.0)）：
        /// 各向异性 ScaleR=0.9994 / ScaleC=1.2000 / Score=0.9997；
        /// 等比（0.9~1.35）Score=0.7665 —— 容差按这组实测值定（行/列 ±0.02，分数 ≥0.95 vs ≤0.85）。
        /// </summary>
        private static void RunAnisoScaleContract()
        {
            Section("[Matching] 各向异性缩放：行/列分别缩放（create_aniso_shape_model / find_aniso_shape_model）");

            using var scene = BuildAnisoScene(AnisoRow, AnisoCol);
            using var stretched = StretchColumns(scene, 1.2, 1.0);

            // 各向异性范围：行 0.9~1.1、列 1.0~1.35（**必须包含 1.0**，否则参考图自匹配不到、
            // 原点锚定会静默失效；目标列缩放 1.2 也落在范围内）
            var (creator, runtime) = BuildAnisoTemplate(scene, true, 0.9, 1.1, 1.0, 1.35);
            try
            {
                // ---- ① 命中 + 行/列缩放回准 ----
                Check("【各向异性】学习成功且载荷随方案序列化",
                    creator.StatusLevel != StatusLevel.Error && creator.Library[0].Model.Length > 1000,
                    $"StatusLevel={creator.StatusLevel} 文案='{creator.StatusMessage}' 载荷={creator.Library[0].Model.Length}");

                var log = new StubLog();
                runtime.Image.Value = stretched;
                runtime.Execute(MakeContext(log));

                int count = Convert.ToInt32(runtime.MatchCount.Value);
                double score = Convert.ToDouble(runtime.Score.Value);
                double[] rowsScale = AsDoubles(runtime.ScalesRow.Value);
                double[] colsScale = AsDoubles(runtime.ScalesCol.Value);
                double[] scales = AsDoubles(runtime.Scales.Value);
                double scaleRow = rowsScale.FirstOrDefault();
                double scaleCol = colsScale.FirstOrDefault();

                Check("【各向异性】列 1.2 拉伸图命中 1 个实例且分数 ≥ 0.95",
                    count == 1 && score >= 0.95,
                    $"MatchCount={count} Score={score:0.000}（实测 0.9997）");
                Check("【各向异性】列缩放回准 1.2（±0.02，实测 1.2000）",
                    Math.Abs(scaleCol - 1.2) <= 0.02,
                    $"ScaleCol={scaleCol:0.0000}（实测 1.2000，容差 ±0.02 覆盖金字塔/亚像素离散）");
                Check("【各向异性】行缩放回准 1.0（±0.02，实测 0.9994）",
                    Math.Abs(scaleRow - 1.0) <= 0.02,
                    $"ScaleRow={scaleRow:0.0000}（实测 0.9994，容差 ±0.02）");
                Check("【各向异性】Scales = 行缩放、ScalesCol = 列缩放（两路真的分开，差 ≈0.2）",
                    Math.Abs(scales.FirstOrDefault() - scaleRow) <= 1e-9
                    && Math.Abs(scaleCol - scaleRow) >= 0.15,
                    $"Scales={scales.FirstOrDefault():0.0000} ScaleRow={scaleRow:0.0000} ScaleCol={scaleCol:0.0000}");
                Check("【各向异性】ScalesRow/ScalesCol 与命中数逐项对齐",
                    rowsScale.Length == count && colsScale.Length == count && count > 0,
                    $"MatchCount={count} ScalesRow={rowsScale.Length} ScalesCol={colsScale.Length}");

                // ---- ③ 与同进程 HALCON 参考实现对拍（同图、同参数）----
                HOperatorSet.GenRectangle2(out HObject refRoi, AnisoRow, AnisoCol, 0, 70, 70);
                HOperatorSet.ReduceDomain(scene, refRoi, out HObject refDomain);
                refRoi.Dispose();
                HOperatorSet.CreateAnisoShapeModel(
                    refDomain, 5, -Math.PI, 2 * Math.PI, "auto",
                    0.9, 1.1, "auto", 1.0, 1.35, "auto",
                    "auto", "use_polarity", "auto", "auto",
                    out HTuple refModelId);
                refDomain.Dispose();
                try
                {
                    HOperatorSet.FindAnisoShapeModel(
                        stretched, refModelId, -Math.PI, 2 * Math.PI,
                        0.9, 1.1, 1.0, 1.35, 0.5, 1, 0.3,
                        "least_squares", new HTuple(new[] { 5, 1 }), 0.8,
                        out _, out _, out _, out HTuple refRowScale, out HTuple refColScale, out HTuple refScores);
                    double refRow = refRowScale.Length > 0 ? refRowScale[0].D : double.NaN;
                    double refCol = refColScale.Length > 0 ? refColScale[0].D : double.NaN;
                    double refScore = refScores.Length > 0 ? refScores[0].D : double.NaN;
                    Check("【各向异性·对拍】与 HALCON 参考实现行缩放差 < 0.01",
                        Math.Abs(refRow - scaleRow) < 0.01,
                        $"插件 {scaleRow:0.######} / 参考 {refRow:0.######} 差 {Math.Abs(refRow - scaleRow):0.######}");
                    Check("【各向异性·对拍】与 HALCON 参考实现列缩放差 < 0.01",
                        Math.Abs(refCol - scaleCol) < 0.01,
                        $"插件 {scaleCol:0.######} / 参考 {refCol:0.######} 差 {Math.Abs(refCol - scaleCol):0.######}");
                    Check("【各向异性·对拍】与 HALCON 参考实现分数差 < 0.02",
                        Math.Abs(refScore - score) < 0.02,
                        $"插件 {score:0.######} / 参考 {refScore:0.######} 差 {Math.Abs(refScore - score):0.######}");
                }
                finally
                {
                    HOperatorSet.ClearShapeModel(refModelId);
                }

                // ---- ④ 指纹：改列范围必须提示重学（改回即恢复）----
                var entry = creator.EditingEntry!;
                entry.ScaleColMax = 1.5;
                bool staleAfterEdit = creator.IsModelStale;
                entry.ScaleColMax = 1.35;
                bool staleAfterRevert = creator.IsModelStale;
                Check("【各向异性·指纹】改 ScaleColMax 后提示需重新学习（改回即恢复）",
                    staleAfterEdit && !staleAfterRevert,
                    $"改后 IsModelStale={staleAfterEdit}，改回 IsModelStale={staleAfterRevert}");

                // ---- ④ JSON：方案往返保留新字段 ----
                var roundTrip = runtime.Library[0];
                Check("【各向异性·JSON】方案往返保留各向异性字段（确认 → 新实例加载）",
                    roundTrip.AnisoScaleEnabled
                    && Math.Abs(roundTrip.ScaleRowMin - 0.9) < 1e-9
                    && Math.Abs(roundTrip.ScaleRowMax - 1.1) < 1e-9
                    && Math.Abs(roundTrip.ScaleColMin - 1.0) < 1e-9
                    && Math.Abs(roundTrip.ScaleColMax - 1.35) < 1e-9
                    && roundTrip.LearnedAnisoScaleEnabled,
                    $"往返后 Aniso={roundTrip.AnisoScaleEnabled} 行 {roundTrip.ScaleRowMin}/{roundTrip.ScaleRowMax} "
                        + $"列 {roundTrip.ScaleColMin}/{roundTrip.ScaleColMax} 学习快照={roundTrip.LearnedAnisoScaleEnabled}");

                // 老 JSON（升级前保存的条目）没有这五个字段 → 必须是"各向异性关 + 0.9/1.1"
                var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<MatchingTemplateEntry>(
                    "{\"Name\":\"老模板\",\"ScaleEnabled\":true,\"ScaleMin\":0.8,\"ScaleMax\":1.2}");
                Check("【各向异性·JSON】老 JSON 缺字段走默认值（关 / 行 0.9~1.1 / 列 0.9~1.1）",
                    legacy != null
                    && !legacy.AnisoScaleEnabled
                    && Math.Abs(legacy.ScaleRowMin - 0.9) < 1e-9
                    && Math.Abs(legacy.ScaleRowMax - 1.1) < 1e-9
                    && Math.Abs(legacy.ScaleColMin - 0.9) < 1e-9
                    && Math.Abs(legacy.ScaleColMax - 1.1) < 1e-9
                    && !legacy.LearnedAnisoScaleEnabled
                    && legacy.ScaleEnabled,
                    legacy == null
                        ? "反序列化返回 null"
                        : $"Aniso={legacy.AnisoScaleEnabled} 行 {legacy.ScaleRowMin}/{legacy.ScaleRowMax} "
                            + $"列 {legacy.ScaleColMin}/{legacy.ScaleColMax} 等比={legacy.ScaleEnabled}/{legacy.ScaleMin}/{legacy.ScaleMax}");
            }
            finally
            {
                creator.Dispose();
                runtime.Dispose();
            }

            // ---- ② 反向判据：同一张拉伸图，关掉各向异性（只开等比）分数必须明显下降 ----
            // 等比范围取 0.9~1.35（覆盖 1.2），排除"范围没覆盖到"这种伪原因。
            var (eqCreator, eqRuntime) = BuildAnisoTemplate(scene, false, 0.9, 1.1, 1.0, 1.35);
            try
            {
                eqRuntime.Image.Value = stretched;
                eqRuntime.Execute(MakeContext(new StubLog()));
                int eqCount = Convert.ToInt32(eqRuntime.MatchCount.Value);
                double eqScore = Convert.ToDouble(eqRuntime.Score.Value);
                double[] eqScales = AsDoubles(eqRuntime.Scales.Value);
                double[] eqRow = AsDoubles(eqRuntime.ScalesRow.Value);
                double[] eqCol = AsDoubles(eqRuntime.ScalesCol.Value);

                Check("【各向异性·反向】同一张 1.2 拉伸图：等比档分数 ≤ 0.85（各向异性 ≥ 0.95）",
                    eqCount == 0 || eqScore <= 0.85,
                    $"等比 MatchCount={eqCount} Score={eqScore:0.000}（实测 0.7665，容差上界 0.85）");
                Check("【各向异性·反向】等比档两路缩放恒相等（它表达不了行/列差异）",
                    eqRow.Length == eqCol.Length
                    && eqRow.Zip(eqCol, (a, b) => Math.Abs(a - b) <= 1e-9).All(x => x),
                    $"Scales={string.Join(",", eqScales.Select(v => v.ToString("0.0000")))} "
                        + $"ScalesRow={string.Join(",", eqRow.Select(v => v.ToString("0.0000")))} "
                        + $"ScalesCol={string.Join(",", eqCol.Select(v => v.ToString("0.0000")))}");
            }
            finally
            {
                eqCreator.Dispose();
                eqRuntime.Dispose();
            }

            // ---- NCC 下勾各向异性：学习成功但必须明确告警"已忽略" ----
            {
                var nccCreator = new MatchingPlugin { InstanceName = "匹配_各向异性_NCC" };
                try
                {
                    nccCreator.DisplayImage = new HImage(scene);
                    nccCreator.AddTemplateEntry("NCC各向异性");
                    nccCreator.CanvasShape = RoiShapeNames.Rectangle;
                    nccCreator.CanvasRect = new double[] { AnisoRow, AnisoCol, 0, 70, 70 };
                    var nccEntry = nccCreator.EditingEntry!;
                    nccEntry.Algorithm = MatchingAlgorithm.Ncc;
                    nccEntry.AnisoScaleEnabled = true;
                    nccCreator.CreateTemplate();

                    Check("【各向异性·NCC】NCC 下勾各向异性：学习成功但状态栏是 Warning 且写明已忽略",
                        nccCreator.StatusLevel == StatusLevel.Warning
                        && nccCreator.StatusMessage.Contains("各向异性")
                        && nccCreator.StatusMessage.Contains("NCC")
                        && !nccCreator.Library[0].LearnedAnisoScaleEnabled,
                        $"StatusLevel={nccCreator.StatusLevel} 学习快照={nccCreator.Library[0].LearnedAnisoScaleEnabled} "
                            + $"文案='{nccCreator.StatusMessage}'");
                }
                finally
                {
                    nccCreator.Dispose();
                }
            }

            // ---- 运行期守卫：开关改了没重学 → 必须落一行 Warn（HALCON 对此不报错，只会静默错结果）----
            {
                var (guardCreator, guardRuntime) = BuildAnisoTemplate(scene, false, 0.9, 1.1, 1.0, 1.35);
                try
                {
                    // 模拟"用户在配置界面勾上各向异性但没重新学习"：模型仍是等比/普通模型
                    guardRuntime.Library[0].AnisoScaleEnabled = true;
                    var guardLog = new StubLog();
                    guardRuntime.Image.Value = stretched;
                    guardRuntime.Execute(MakeContext(guardLog));
                    Check("【各向异性·守卫】开关改了没重学：运行期落一行 Warn（不静默错结果）",
                        guardLog.HasWarn("各向异性"),
                        guardLog.Warns.Count == 0 ? "没有任何 Warn（静默）" : string.Join(" | ", guardLog.Warns));
                }
                finally
                {
                    guardCreator.Dispose();
                    guardRuntime.Dispose();
                }
            }
        }


        // ==================================================================
        //  ④ 未找到的失败语义 + 多实例 + 参数变更重学提示
        // ==================================================================
        private static void RunNotFoundAndStaleness()
        {
            var (creator, runtime, scene) = BuildTemplate();
            try
            {
                // 未找到：空白背景（没有任何特征）→ Fail 且说清原因，标注图仍给（原图 + 红字）
                using var blank = MakeFlat();
                runtime.Image.Value = blank;
                runtime.Execute(MakeContext(new StubLog()));
                Check("【未找到】空白背景判失败（Success=false）", runtime.Success.Value is false,
                    $"Success={runtime.Success.Value}");
                Check("【未找到】失败原因写明「未找到目标」并带上分数阈值",
                    (runtime.ErrorMessage.Value as string ?? "").Contains("未找到目标")
                    && (runtime.ErrorMessage.Value as string ?? "").Contains("0.50"),
                    $"Err='{runtime.ErrorMessage.Value}'");
                Check("【未找到】MatchCount=0 且标注图仍生成（产线看现场）",
                    Convert.ToInt32(runtime.MatchCount.Value) == 0
                    && runtime.MeasureImage.Value is HImage m && m.IsInitialized(), "");

                // 多实例：场景里放两个相同图案，MaxMatches=5 → 全部找回且数组对齐
                using var two = TwoPatternScene();
                runtime.MaxMatches = 5;
                runtime.Image.Value = two;
                runtime.Execute(MakeContext(new StubLog()));
                int count = Convert.ToInt32(runtime.MatchCount.Value);
                Check("【多实例】两个相同图案全部找回（count=2）", count == 2, $"MatchCount={count}");
                Check("【多实例】数组端口逐项对齐且分数都 ≥ 0.9",
                    AsDoubles(runtime.Rows.Value).Length == 2
                    && AsDoubles(runtime.Scores.Value).Length == 2
                    && AsDoubles(runtime.Scores.Value).All(v => v >= 0.9),
                    $"scores={string.Join(",", AsDoubles(runtime.Scores.Value).Select(v => v.ToString("0.000")))}");

                // MaxMatches 上限：同一张双图案图，上限 1 → 只报最高分那一个
                runtime.MaxMatches = 1;
                runtime.Execute(MakeContext(new StubLog()));
                Check("【MaxMatches】上限 1 → 只报最高分实例（不越限）",
                    Convert.ToInt32(runtime.MatchCount.Value) == 1,
                    $"MatchCount={runtime.MatchCount.Value}");
                runtime.MaxMatches = 5;

                // ---- MaxOverlap 去重：两个相距 90px 的图案（模型框 160×160，框重叠约 44%）----
                // 图案本身互不接触（圆盘 r=55、十字臂 ±40 都够不着），只有"模型框"重叠——
                // 这样测的才是 MaxOverlap 的去重语义，而不是"特征糊在一起找不着"。
                using var closePair = BuildScene(PatternRow, PatternCol);
                HOperatorSet.GenCircle(out HObject disk2, PatternRow, PatternCol + 90, DiskRadius);
                HOperatorSet.OverpaintRegion(closePair, disk2, 60, "fill");
                HOperatorSet.GenRectangle1(out HObject arm2a, PatternRow - 40, PatternCol + 80, PatternRow + 40, PatternCol + 100);
                HOperatorSet.GenRectangle1(out HObject arm2b, PatternRow - 10, PatternCol + 50, PatternRow + 10, PatternCol + 130);
                HOperatorSet.OverpaintRegion(closePair, arm2a, 255, "fill");
                HOperatorSet.OverpaintRegion(closePair, arm2b, 255, "fill");
                disk2.Dispose();
                arm2a.Dispose();
                arm2b.Dispose();

                runtime.MaxOverlap = 0.9;
                runtime.Image.Value = closePair;
                runtime.Execute(MakeContext(new StubLog()));
                int looseCount = Convert.ToInt32(runtime.MatchCount.Value);
                Check("【MaxOverlap】阈值放宽（0.9）→ 两个重叠实例都报出",
                    looseCount == 2,
                    $"MatchCount={looseCount}（期望 2）");

                runtime.MaxOverlap = 0.05;
                runtime.Execute(MakeContext(new StubLog()));
                int strictCount = Convert.ToInt32(runtime.MatchCount.Value);
                Check("【MaxOverlap】阈值收紧（0.05）→ 重叠实例被去重成一个",
                    strictCount == 1,
                    $"MatchCount={strictCount}（期望 1，宽松时 {looseCount}）");

                // 成功匹配时 AlignedImage 必须非空（本轮刚跑完一次成功匹配）
                Check("【AlignedImage】成功匹配后位姿归一化图非空",
                    runtime.AlignedImage.Value is HImage aligned && aligned.IsInitialized(),
                    $"AlignedImage={runtime.AlignedImage.Value}");

                // ---- 涂抹时序（用配置态实例：只有它有 DisplayImage 可作序列化坐标系）----
                // 注意：必须先把 region 画好再赋给 SmearMask —— setter 一收到值就写回条目，
                // 赋一个空 region 只会写回空掩膜。
                var entry0 = creator.EditingEntry!;
                var stroke1 = new HRegion();
                stroke1.GenRectangle1(30d, 30d, 40d, 40d);
                creator.SmearMask = stroke1;   // 所有权转移：setter 负责写回与旧实例释放
                Check("【涂抹时序·即写即存】涂抹后条目掩膜已写回（点「确定」不会丢）",
                    entry0.Mask.Length > 0,
                    $"掩膜长度={entry0.Mask.Length}");

                Check("【涂抹时序·过期及时】涂抹后立即提示需重新学习（不必等换条目）",
                    creator.IsModelStale,
                    $"IsModelStale={creator.IsModelStale} 状态='{creator.TemplateStatusText}'");

                creator.ClearSmear();
                Check("【涂抹时序·清除生效】清空涂抹后条目掩膜真正清空（换条目再切回不会复活）",
                    entry0.Mask.Length == 0,
                    $"掩膜长度={entry0.Mask.Length}");
            }
            finally
            {
                creator.Dispose();
                runtime.Dispose();
                scene.Dispose();
            }

            // 提取参数 vs 匹配参数的过期判定口径（都作用在编辑条目上）
            var (c2, r2, s2) = BuildTemplate();
            try
            {
                c2.MinScore = 0.6;
                Check("【重学提示】改匹配参数（运行期）不触发重学", !c2.IsModelStale, "");
                c2.EditingEntry!.NumLevels = 6;
                Check("【重学提示】改提取参数（创建态）触发重学提示",
                    c2.IsModelStale && c2.TemplateStatusText.Contains("重新学习"),
                    $"StatusText='{c2.TemplateStatusText}'");
                Check("【重学提示】徽标文字对运行实例同样成立（载荷未变时不误报）",
                    !r2.IsModelStale, "");
            }
            finally
            {
                c2.Dispose();
                r2.Dispose();
                s2.Dispose();
            }
        }

        // ==================================================================
        //  ⑤ 圆形模板区域（create_shape_model 吃任意域，圆域模型原点=圆心）
        // ==================================================================
        private static void RunCircleTemplate()
        {
            var (creator, runtime, scene) = BuildTemplate(shape: RoiShapeNames.Circle);
            try
            {
                Check("【圆形模板】圆形区域可学习模板（域任意形状，无矩形硬限制）",
                    creator.Library[0].Model.Length > 1000
                    && creator.Library[0].Shape == RoiShapeNames.Circle,
                    $"载荷 {creator.Library[0].Model.Length} 字符");
                runtime.Image.Value = scene;
                runtime.Execute(MakeContext(new StubLog()));
                double row = Convert.ToDouble(runtime.Row.Value);
                double col = Convert.ToDouble(runtime.Column.Value);
                Check("【圆形模板】同图定位回准圆心 (150,200 ±0.5)",
                    Convert.ToInt32(runtime.MatchCount.Value) == 1
                    && Math.Abs(row - PatternRow) <= 0.5 && Math.Abs(col - PatternCol) <= 0.5,
                    $"({row:0.00},{col:0.00}) Score={Convert.ToDouble(runtime.Score.Value):0.000}");
            }
            finally
            {
                creator.Dispose();
                runtime.Dispose();
                scene.Dispose();
            }
        }

        // ==================================================================
        //  ⑥ 涂抹掩膜：挖掉的不参与学习 + 掩膜随方案落盘 + Initialize 往返恢复
        // ==================================================================
        private static void RunSmearMask()
        {
            var (creator, runtime, scene) = BuildTemplate();
            try
            {
                int bareLen = creator.Library[0].Model.Length;
                Check("【涂抹】无涂抹时掩膜为空",
                    string.IsNullOrEmpty(creator.Library[0].Mask) && creator.SmearMask == null,
                    $"掩膜长度={creator.Library[0].Mask.Length}");

                // 挖掉十字中心（半径 15 圆域）→ 重新学习：模型变小 + 掩膜落盘
                var mask = new HRegion();
                mask.GenCircle(PatternRow, PatternCol, 15);
                creator.SmearMask = mask;   // 所有权转移：插件 setter 负责旧实例释放
                creator.CreateTemplate();
                Check("【涂抹】涂抹后重新学习：掩膜随条目落盘", creator.Library[0].Mask.Length > 100,
                    $"掩膜 {creator.Library[0].Mask.Length} 字符");
                Check("【涂抹】挖掉中心后模型载荷变小（特征点减少）",
                    creator.Library[0].Model.Length < bareLen,
                    $"裸={bareLen} 涂后={creator.Library[0].Model.Length}");

                // 定位仍成立（盘边缘特征未受影响），新载荷走懒加载
                runtime.Initialize(ConfirmToStepData(creator));
                runtime.Image.Value = scene;
                runtime.Execute(MakeContext(new StubLog()));
                double row = Convert.ToDouble(runtime.Row.Value);
                double col = Convert.ToDouble(runtime.Column.Value);
                Check("【涂抹】挖掉中心后定位仍回准 (150,200 ±1)",
                    Convert.ToInt32(runtime.MatchCount.Value) == 1
                    && Math.Abs(row - PatternRow) <= 1 && Math.Abs(col - PatternCol) <= 1,
                    $"({row:0.00},{col:0.00})");
            }
            finally
            {
                creator.Dispose();
                runtime.Dispose();
                scene.Dispose();
            }

            // Initialize 往返：确认（OnConfirm 落盘）→ 新实例 Initialize 恢复掩膜（模拟重开配置界面）
            var (c1, r1, s1) = BuildTemplate();
            try
            {
                var mask2 = new HRegion();
                mask2.GenCircle(PatternRow, PatternCol, 12);
                c1.SmearMask = mask2;
                c1.CreateTemplate();
                // 条目补参考图路径（场景落盘），验证"选中条目自动恢复特征预览"
                string refPng = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vm_ref_{Guid.NewGuid():N}.png");
                HOperatorSet.WriteImage(s1, "png", 0, refPng);
                c1.EditingEntry!.RefImagePath = refPng;
                var stepData = ConfirmToStepData(c1);

                var reopened = new MatchingPlugin { InstanceName = "匹配_重开" };
                reopened.Initialize(stepData);
                Check("【重开】选中条目自动恢复特征预览（参考图 + 绿色轮廓）",
                    reopened.HasTemplatePreview, "");
                System.IO.File.Delete(refPng);
                Check("【重开】Initialize 从方案恢复涂抹掩膜（橙色显示、可继续编辑）",
                    reopened.SmearMask != null && reopened.SmearMask.IsInitialized(),
                    $"掩膜 base64 {(reopened.EditingEntry?.Mask ?? "").Length} 字符");
                Check("【重开】模板模型一并恢复（不用重画重学）",
                    (reopened.EditingEntry?.Model ?? "").Length > 1000 && !reopened.IsModelStale, "");
                reopened.Image.Value = s1;
                reopened.Execute(MakeContext(new StubLog()));
                Check("【重开】恢复后的模板直接可定位",
                    Convert.ToInt32(reopened.MatchCount.Value) == 1, "");
            }
            finally
            {
                c1.Dispose();
                r1.Dispose();
                s1.Dispose();
            }
        }

        // ==================================================================
        //  ⑥b NCC 原点：Row/Column 必须是 ROI 中心，不是模板域重心
        // ==================================================================
        private static void RunNccOrigin()
        {
            Section("[Matching] NCC 原点：Row/Column 必须是 ROI 中心（非模板域重心）");

            var scene = BuildScene(PatternRow, PatternCol);
            var creator = new MatchingPlugin { InstanceName = "匹配_NCC建模板" };
            try
            {
                creator.DisplayImage = scene;
                creator.AddTemplateEntry("NCC模板");
                creator.CanvasShape = RoiShapeNames.Rectangle;
                creator.CanvasRect = new double[] { PatternRow, PatternCol, 0, 80, 80 };
                creator.MinScore = 0.2;
                creator.EditingEntry!.Algorithm = MatchingAlgorithm.Ncc;

                // 非对称掩膜：挖掉 ROI 左上角一块 → 模板域重心偏离 ROI 中心。
                // 没有这块掩膜，NCC 重心≈ROI 中心，测不出原点补偿是否真的生效。
                var mask = new HRegion();
                mask.GenRectangle1(PatternRow - 80, PatternCol - 80, PatternRow - 10, PatternCol - 10);
                creator.SmearMask = mask;   // 所有权转移给插件

                creator.CreateTemplate();
                var entry = creator.Library[0];
                Check("【NCC 原点】非对称掩膜确实产生了重心偏移（否则本断言无效）",
                    Math.Abs(entry.NccOffsetRow) > 1 || Math.Abs(entry.NccOffsetCol) > 1,
                    $"offset=({entry.NccOffsetRow:0.###},{entry.NccOffsetCol:0.###})");

                var runtime = new MatchingPlugin { InstanceName = "匹配_NCC断言" };
                try
                {
                    runtime.Initialize(ConfirmToStepData(creator));
                    runtime.Image.Value = scene;
                    runtime.Execute(MakeContext(new StubLog()));

                    double row = Convert.ToDouble(runtime.Row.Value);
                    double col = Convert.ToDouble(runtime.Column.Value);
                    Check("【NCC 原点】同图定位回准 ROI 中心 (150,200 ±0.5)",
                        Convert.ToInt32(runtime.MatchCount.Value) == 1
                        && Math.Abs(row - PatternRow) <= 0.5 && Math.Abs(col - PatternCol) <= 0.5,
                        $"({row:0.00},{col:0.00}) Score={Convert.ToDouble(runtime.Score.Value):0.000}");

                    var runtimeEntry = runtime.Library[0];
                    if (runtimeEntry.RuntimeContours != null && runtimeEntry.RuntimeContours.IsInitialized())
                    {
                        using var placed = MatchingPlugin.PlaceContoursAtPoses(
                            runtimeEntry.RuntimeContours,
                            runtime.Rows.Value as HTuple ?? new HTuple(),
                            runtime.Columns.Value as HTuple ?? new HTuple(),
                            runtime.Angles.Value as HTuple ?? new HTuple());
                        HOperatorSet.SmallestRectangle1Xld(
                            placed, out HTuple rr1, out HTuple cc1, out HTuple rr2, out HTuple cc2);
                        double cr = (rr1.D + rr2.D) / 2, cc = (cc1.D + cc2.D) / 2;
                        Check("【NCC 轮廓】轮廓放置回 ROI 中心（不是参考图绝对坐标再平移一次）",
                            Math.Abs(cr - PatternRow) <= 1 && Math.Abs(cc - PatternCol) <= 1,
                            $"bbox center=({cr:0.00},{cc:0.00})");
                    }
                    else
                    {
                        Check("【NCC 轮廓】RuntimeContours 已生成", false, "null");
                    }
                }
                finally
                {
                    runtime.Dispose();
                }
            }
            finally
            {
                creator.Dispose();
                scene.Dispose();
            }
        }

        // ==================================================================
        //  ⑥c HALCON 样例流程：视觉样例对拍.vms（NCC / 形状）
        // ==================================================================
        private static void RunHalconSampleSolution()
        {
            RunHalconSampleFlow(
                "模板匹配_NCC散焦图", "读取散焦图像", "NCC模板匹配",
                "NCC", 11, 307.952, 308.395, 0.0, 0.7824);
            RunHalconSampleFlow(
                "形状匹配_散焦PCB", "读取PCB散焦图像", "形状模板匹配",
                "形状", 121, 225.206, 362.393, -0.302, 0.8795);
        }

        private static void RunHalconSampleFlow(
            string flowName, string acqStepName, string matchStepName,
            string label, int expectedCount, double expectedRow, double expectedCol,
            double expectedAngle, double expectedScore)
        {
            Section($"[Matching] HALCON 样例流程：视觉样例对拍.vms / {flowName}");

            string repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            string vmsPath = Path.Combine(repoRoot, "解决方案", "视觉样例对拍", "视觉样例对拍.vms");
            Check("样例方案存在（解决方案\\视觉样例对拍\\视觉样例对拍.vms）", File.Exists(vmsPath), vmsPath);
            if (!File.Exists(vmsPath)) return;

            var loaded = new SolutionService().LoadAsync(vmsPath).GetAwaiter().GetResult();
            Check("样例方案可被生产加载器打开", loaded.Success && loaded.Data != null, loaded.Message);
            if (!loaded.Success || loaded.Data == null) return;
            // 生产宿主在 Load 后会把落盘路径写回模型；资产解析（方案根 + RelativePath）依赖它
            loaded.Data.SolutionFilePath = vmsPath;

            var flow = loaded.Data.Flows.FirstOrDefault(f => f.FlowName == flowName);
            Check($"流程名 = {flowName}", flow != null,
                $"流程={string.Join(",", loaded.Data.Flows.Select(f => f.FlowName))}");
            if (flow == null) return;

            CheckBatchFlow(flow, expectedCount);

            var sampleSteps = FlattenActionSteps(flow.Steps).ToList();
            var acq = sampleSteps.FirstOrDefault(s => s.StepName == acqStepName);
            var match = sampleSteps.FirstOrDefault(s => s.StepName == matchStepName);
            Check($"采集/算法两个步骤存在（{acqStepName} / {matchStepName}）", acq != null && match != null,
                $"步骤={string.Join(" → ", sampleSteps.Select(s => s.StepName))}");
            if (acq == null || match == null) return;

            var display = sampleSteps.FirstOrDefault(s => s.PluginName == "结果显示");
            bool displayOk = display != null
                && display.InputValues.TryGetValue("DisplayViewIndex", out var dv)
                && Convert.ToInt32(dv) >= 1
                && display.LinkedSources.TryGetValue("SrcImage", out var displayLink)
                && displayLink.TargetPortName == "Image"
                && display.LinkedSources.TryGetValue("OverlayContours", out var overlayLink)
                && overlayLink.TargetPortName == "MatchContours";
            Check("结果显示接原始图 + 算法几何（MatchContours），标注图不再由算法烧", displayOk,
                display == null
                    ? "缺少「结果显示」步骤"
                    : $"View={display.InputValues.GetValueOrDefault("DisplayViewIndex")} "
                      + $"SrcImage→{display.LinkedSources.GetValueOrDefault("SrcImage")?.TargetPortName} "
                      + $"OverlayContours→{display.LinkedSources.GetValueOrDefault("OverlayContours")?.TargetPortName}");
            Check("算法步骤只输出结果（DisplayViewIndex=0 且 KeepAnnotatedImage=false）",
                Convert.ToInt32(match.InputValues.GetValueOrDefault("DisplayViewIndex") ?? 1) == 0
                && !Convert.ToBoolean(match.InputValues.GetValueOrDefault("KeepAnnotatedImage") ?? true),
                $"View={match.InputValues.GetValueOrDefault("DisplayViewIndex")} "
                + $"Keep={match.InputValues.GetValueOrDefault("KeepAnnotatedImage")}");

            string imagePath = acq.InputValues.TryGetValue("FilePath", out var p) ? p as string : null;
            // 文件夹化方案：ImageAcquisition 的 FilePath 是方案内相对路径，按 .vms 所在目录解析
            if (imagePath != null && !Path.IsPathRooted(imagePath))
                imagePath = Path.Combine(Path.GetDirectoryName(vmsPath)!, imagePath);
            Check("图像路径指向方案内样例图且存在", imagePath != null && File.Exists(imagePath), imagePath ?? "(null)");
            if (imagePath == null || !File.Exists(imagePath)) return;

            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(loaded.Data);

            string imageAssetId = acq.InputValues.TryGetValue("ImageAssetId", out var aid) ? aid as string : null;
            var assetContext = new ExecutionContext(
                new StubLog(), new FlowSession { FlowName = flow.FlowName }, workspace, CancellationToken.None);
            bool assetOk = ProjectAssetPathResolver.TryResolve(
                assetContext, imageAssetId, imagePath, "image", out var assetPath, out var assetError)
                && File.Exists(assetPath);
            Check("ImageAssetId 解析到方案内图片（方案根 + RelativePath）", assetOk,
                assetOk ? assetPath : $"AssetId='{imageAssetId}' Err='{assetError}' Path='{assetPath}'");

            var compiled = new FlowCompiler(workspace).Compile(flow.Steps.ToArray(), flow.FlowName);
            Check("样例流程编译通过（端口名/连线类型）", compiled.Success,
                compiled.Success ? "" : string.Join(" | ", compiled.Errors.Select(e => e.Message)));
            if (!compiled.Success) return;

            HOperatorSet.ReadImage(out HObject raw, imagePath);
            using var image = new HImage(raw);
            raw.Dispose();
            var plugin = new MatchingPlugin { InstanceName = $"样例_{label}" };
            try
            {
                plugin.Initialize(match);
                plugin.Image.LinkedSource = null;   // 直接喂图，不让它去解析上游步骤
                plugin.Image.Value = image;
                plugin.Execute(new ExecutionContext(
                    new StubLog(), new FlowSession { FlowName = flow.FlowName },
                    workspace, CancellationToken.None));

                double row = Convert.ToDouble(plugin.Row.Value);
                double col = Convert.ToDouble(plugin.Column.Value);
                double angle = Convert.ToDouble(plugin.Angle.Value);
                double score = Convert.ToDouble(plugin.Score.Value);
                int count = Convert.ToInt32(plugin.MatchCount.Value);
                Check($"{label} 结果与 HALCON 一致（{expectedRow:0.###},{expectedCol:0.###},"
                    + $"{expectedAngle:0.###}°,{expectedScore:0.####}）",
                    count == 1
                    && Math.Abs(row - expectedRow) <= 0.01
                    && Math.Abs(col - expectedCol) <= 0.01
                    && Math.Abs(angle - expectedAngle) <= 0.01
                    && Math.Abs(score - expectedScore) <= 0.001,
                    $"count={count} ({row:0.###},{col:0.###},{angle:0.###}°,{score:0.####}) "
                    + $"Err='{plugin.ErrorMessage.Value}'");
            }
            finally
            {
                plugin.Dispose();
            }
        }

        // ==================================================================
        //  ⑦ 多模板/配方驱动：A/B 两条模板，RecipeName 端口选条目（二期核心）
        // ==================================================================
        // ==================================================================
        //  ⑩ 条目参考图 × 图像资产（2026-10-28）
        // ==================================================================

        /// <summary>
        /// 条目参考图能不能挂"图像资产"，以及"资产优先、解析不到不静默退回"这条判据。
        ///
        /// 为什么必须测：条目里的 RefImagePath 是本机绝对路径，换台机器"重学"就断；
        /// 而最容易写错的不是解析本身，是**失败时静默用旧路径** —— 那样界面上看到的是另一张图，
        /// 学出来的模型与框住的区域对不上，且没有任何报错指向真因。
        /// </summary>
        private static void RunRefImageAssetContract()
        {
            var image = FindRepoImage(Path.Combine("Image", "board", "board_01.png"));
            var imageAssetId = Guid.NewGuid();
            var templateAssetId = Guid.NewGuid();
            var context = new PluginConfigContext
            {
                Assets =
                {
                    new AssetPathOption
                    {
                        AssetId = imageAssetId,
                        Kind = "image",
                        Name = "标定板",
                        AbsolutePath = image ?? string.Empty,
                        Version = 3,
                    },
                    new AssetPathOption
                    {
                        AssetId = templateAssetId,
                        Kind = "template",
                        Name = "模板库",
                        AbsolutePath = image ?? string.Empty,
                    },
                },
            };

            // ① 契约：正常解析
            bool okPath = PluginConfigAssetPaths.TryResolve(
                context, imageAssetId.ToString(), "image", out var resolved, out var err1);
            Check("【参考图资产】契约：image 资产 → 解析出绝对路径",
                okPath && string.Equals(resolved, image, StringComparison.OrdinalIgnoreCase),
                okPath ? resolved : err1);

            // ② 契约：类型不匹配必须拒，并点名两边类型
            bool okKind = PluginConfigAssetPaths.TryResolve(
                context, templateAssetId.ToString(), "image", out _, out var err2);
            Check("【参考图资产】契约：template 资产当 image 用 → 拒绝并点名两边类型",
                !okKind && err2.Contains("template") && err2.Contains("image"), err2);

            // ③ 契约：空 / 非法 Guid / 不存在 / 无上下文 → 四种都拒
            bool okEmpty = PluginConfigAssetPaths.TryResolve(context, "", "image", out _, out var err3);
            bool okBad = PluginConfigAssetPaths.TryResolve(context, "not-a-guid", "image", out _, out var err4);
            bool okMissing = PluginConfigAssetPaths.TryResolve(context, Guid.NewGuid().ToString(), "image", out _, out var err5);
            bool okNoCtx = PluginConfigAssetPaths.TryResolve(null, imageAssetId.ToString(), "image", out _, out var err6);
            Check("【参考图资产】契约：空/非法/不存在/无上下文 → 全部拒绝且各有中文原因",
                !okEmpty && !okBad && !okMissing && !okNoCtx
                && err3.Length > 0 && err4.Contains("Guid") && err5.Length > 0 && err6.Contains("上下文"),
                $"{err3} | {err4} | {err5} | {err6}");

            if (image == null)
            {
                // 判红而不是跳过：仓库自带这张样例图，缺了说明工作区不完整（R38）
                Check("【参考图资产】仓库样例图 Image\\board\\board_01.png 存在", false, "找不到该文件");
                return;
            }

            // ④ 插件层：资产优先于旧路径（旧路径故意写成不存在的文件）
            var plugin = new MatchingPlugin { InstanceName = "匹配_参考图资产" };
            plugin.SetConfigContext(context);
            plugin.AddTemplateEntry("A型");
            plugin.EditingEntry!.RefImagePath =
                Path.Combine(Path.GetTempPath(), "不存在的参考图_" + Guid.NewGuid().ToString("N") + ".png");
            plugin.EditingEntry.RefImageAssetId = imageAssetId.ToString();

            plugin.LoadPreviewImage();
            Check("【参考图资产】插件层：选了资产 → 用资产图（旧路径不存在也不影响）",
                plugin.DisplayImage != null && plugin.StatusMessage.Contains("已载入参考图"),
                plugin.DisplayImage == null ? "画布没载入图：" + plugin.StatusMessage : plugin.StatusMessage);

            // ⑤ 资产解析不到 → 明确失败，**不**静默退回旧路径（旧路径这次是好的）
            plugin.EditingEntry.RefImagePath = image;
            plugin.EditingEntry.RefImageAssetId = Guid.NewGuid().ToString();
            plugin.LoadPreviewImage();
            Check("【参考图资产】插件层：资产解析不到 → 明确报错，不静默退回旧路径",
                plugin.StatusMessage.Contains("参考图无法解析"), plugin.StatusMessage);

            // ⑥ 条目字段随模板库 JSON 往返（重开方案后还在）
            plugin.EditingEntry.RefImageAssetId = imageAssetId.ToString();
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(plugin.Library);
            var back = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MatchingTemplateEntry>>(json);
            Check("【参考图资产】条目字段随库 JSON 往返",
                back != null && back.Count == 1 && back[0].RefImageAssetId == imageAssetId.ToString(),
                back == null ? "反序列化为 null" : back[0].RefImageAssetId);
        }

        /// <summary>从 exe 目录向上找仓库里的样例图（断言宿主在 bin 深处，不能写死相对层级）</summary>
        private static string? FindRepoImage(string relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static void RunRecipeContract()
        {
            // 两个产品两条模板：A型（图案在 (150,200)）、B型（图案在 (280,360)）
            var creator = new MatchingPlugin { InstanceName = "匹配_建库" };
            MatchingPlugin? runtime = null;
            HImage? sceneA = null, sceneB = null;
            try
            {
                sceneA = BuildScene(PatternRow, PatternCol);
                creator.DisplayImage = sceneA;
                creator.AddTemplateEntry("A型");
                creator.CanvasShape = RoiShapeNames.Rectangle;
                creator.CanvasRect = new double[] { PatternRow, PatternCol, 0, 80, 80 };
                creator.CreateTemplate();
                Check("【配方】条目 A型 学习成功", creator.Library[0].Model.Length > 1000,
                    $"载荷 {creator.Library[0].Model.Length} 字符");

                sceneB = BuildScene(280, 360);
                creator.DisplayImage = sceneB;
                creator.AddTemplateEntry("B型");
                creator.CanvasShape = RoiShapeNames.Rectangle;
                creator.CanvasRect = new double[] { 280, 360, 0, 80, 80 };
                creator.CreateTemplate();
                Check("【配方】条目 B型 学习成功（库 2 条）", creator.Library.Count == 2, $"库 {creator.Library.Count} 条");

                // 确认落盘 → 运行实例加载
                creator.DefaultTemplateName = "A型";
                var rt = new MatchingPlugin { InstanceName = "匹配_断言" };
                runtime = rt;
                rt.Initialize(ConfirmToStepData(creator));
                Check("【配方】运行实例加载到 2 条模板", rt.Library.Count == 2, $"库 {rt.Library.Count} 条");

                // 默认模板：RecipeName 为空 → 用「设为默认」的 A型
                rt.Image.Value = sceneA;
                rt.Execute(MakeContext(new StubLog()));
                Check("【配方】RecipeName 为空 → 用默认模板 A型 且回准",
                    (rt.MatchedTemplate.Value as string ?? "") == "A型"
                    && Math.Abs(Convert.ToDouble(rt.Row.Value) - PatternRow) <= 1,
                    $"Matched='{rt.MatchedTemplate.Value}' Row={Convert.ToDouble(rt.Row.Value):0.0}");

                // 配方驱动：RecipeName=B型 → 用 B 型模板在 B 场景定位
                rt.RecipeName.Value = "B型";
                rt.Image.Value = sceneB;
                rt.Execute(MakeContext(new StubLog()));
                Check("【配方】RecipeName=B型 → 用 B型模板且回准 (280,360)",
                    (rt.MatchedTemplate.Value as string ?? "") == "B型"
                    && Math.Abs(Convert.ToDouble(rt.Row.Value) - 280) <= 1
                    && Math.Abs(Convert.ToDouble(rt.Column.Value) - 360) <= 1,
                    $"Matched='{rt.MatchedTemplate.Value}' Row={Convert.ToDouble(rt.Row.Value):0.0}");

                // 未知配方：明确失败并列出库里现有的名字（不静默、不猜）
                rt.RecipeName.Value = "C型";
                rt.Image.Value = sceneA;
                rt.Execute(MakeContext(new StubLog()));
                var err = rt.ErrorMessage.Value as string ?? "";
                Check("【配方】未知配方判失败且列出库里现有的名字",
                    rt.Success.Value is false && err.Contains("未知产品")
                    && err.Contains("A型") && err.Contains("B型"),
                    $"Err='{err}'");

                // 配方名清空 → 回落默认
                rt.RecipeName.Value = string.Empty;
                rt.Image.Value = sceneA;
                rt.Execute(MakeContext(new StubLog()));
                Check("【配方】RecipeName 清空 → 回落默认模板 A型",
                    (rt.MatchedTemplate.Value as string ?? "") == "A型",
                    $"Matched='{rt.MatchedTemplate.Value}'");

                // 配方选择与场景内容无关：选了 B 就用 B（A 场景里有同款图案 → 如实命中，
                // 这正是"多个相同工件"场景的正常行为；MatchedTemplate 始终如实报告用的哪条）
                rt.RecipeName.Value = "B型";
                rt.Image.Value = sceneA;
                rt.Execute(MakeContext(new StubLog()));
                Check("【配方】配方选择与场景内容无关（选 B 就用 B 并如实报告）",
                    (rt.MatchedTemplate.Value as string ?? "") == "B型"
                    && Convert.ToInt32(rt.MatchCount.Value) >= 1
                    && rt.Success.Value is true, "");
            }
            finally
            {
                creator.Dispose();
                runtime?.Dispose();
                sceneA?.Dispose();
                sceneB?.Dispose();
            }
        }

        // ==================================================================
        //  ⑧ 老方案迁移：单模板字段 → 模板库第一条，迁移后可定位、遗留字段清空
        // ==================================================================
        private static void RunMigration()
        {
            var (creator, runtime, scene) = BuildTemplate();
            try
            {
                // 伪造一个"旧版本保存的方案"：单模板字段 + 空库 JSON
                var legacyStepData = new FakeStepData();
                legacyStepData.InputValues["TemplateModel"] = creator.Library[0].Model;
                legacyStepData.InputValues["TemplateShape"] = creator.Library[0].Shape;
                legacyStepData.InputValues["TemplateRect"] = creator.Library[0].Rect;
                legacyStepData.InputValues["TemplateMask"] = creator.Library[0].Mask;

                var migrated = new MatchingPlugin { InstanceName = "匹配_迁移" };
                migrated.Initialize(legacyStepData);
                Check("【迁移】老方案单模板字段自动迁入模板库第一条",
                    migrated.Library.Count == 1 && migrated.Library[0].Model.Length > 1000,
                    $"库 {migrated.Library.Count} 条，载荷 {migrated.Library[0].Model.Length} 字符");
                Check("【迁移】迁移后遗留字段清空（防重复导入）",
                    string.IsNullOrEmpty(migrated.TemplateModel), "");
                migrated.Image.Value = scene;
                migrated.Execute(MakeContext(new StubLog()));
                Check("【迁移】迁移出的模板可直接定位",
                    Convert.ToInt32(migrated.MatchCount.Value) == 1, "");

                // 迁移后的方案再确认落盘，再加载：走库 JSON，不再重复迁移
                var stepData = ConfirmToStepData(migrated);
                var again = new MatchingPlugin { InstanceName = "匹配_迁移后再加载" };
                again.Initialize(stepData);
                Check("【迁移】迁移后的方案再加载不再重复迁移（仍 1 条）",
                    again.Library.Count == 1, $"库 {again.Library.Count} 条");
                again.Dispose();

                // 编译链路回归：FlowCompiler 对运行实例只调 ApplyConfigValues（不调 Initialize）——
                // 库解析必须在那条路上也完成，否则"配置里明明已学习，运行却报尚未创建模板"
                var compiledPath = new MatchingPlugin { InstanceName = "匹配_编译链路" };
                compiledPath.ApplyConfigValues(stepData);
                compiledPath.Image.Value = scene;
                compiledPath.Execute(MakeContext(new StubLog()));
                Check("【编译链路】只走 ApplyConfigValues 也能拿到模板库并定位",
                    Convert.ToInt32(compiledPath.MatchCount.Value) == 1,
                    $"MatchCount={compiledPath.MatchCount.Value}");
                compiledPath.Dispose();
            }
            finally
            {
                creator.Dispose();
                runtime.Dispose();
                scene.Dispose();
            }
        }

        // ==================================================================
        //  夹具
        // ==================================================================

        /// <summary>确认落盘 → stepData（模拟宿主的 OnConfirm 写 InputValues）</summary>
        private static FakeStepData ConfirmToStepData(MatchingPlugin plugin)
        {
            var stepData = new FakeStepData();
            plugin.OnConfirm(stepData);
            return stepData;
        }

        /// <summary>
        /// 建模板对：creator 在参考图 (150,200) 上学模板（走模板库：新增条目 → 画布区域 → 学习
        /// → OnConfirm 落盘）；runtime 是"编译执行"实例——Initialize(stepData) 走真实加载链路。
        /// </summary>
        private static (MatchingPlugin creator, MatchingPlugin runtime, HImage scene) BuildTemplate(
            bool scaled = false, string shape = RoiShapeNames.Rectangle, string name = "模板1")
        {
            var scene = BuildScene(PatternRow, PatternCol);

            var creator = new MatchingPlugin { InstanceName = "匹配_建模板" };
            creator.DisplayImage = scene;
            creator.AddTemplateEntry(name);
            creator.CanvasShape = shape;
            creator.CanvasRect = shape == RoiShapeNames.Circle
                ? new double[] { PatternRow, PatternCol, DiskRadius, 0, 0 }
                : new double[] { PatternRow, PatternCol, 0, 80, 80 };
            if (scaled) creator.EditingEntry!.ScaleEnabled = true;
            creator.CreateTemplate();

            var runtime = new MatchingPlugin { InstanceName = "匹配_断言" };
            runtime.Initialize(ConfirmToStepData(creator));
            return (creator, runtime, scene);
        }

        /// <summary>
        /// 测试图案：暗圆盘 + 亮十字。位置参数化（平移测试用），梯度特征丰富。
        /// internal：CreateRoiChecks 验证"裁剪图/domain 图的坐标系"时复用同一张图，
        /// 保证两个插件断言里的"目标真实位置"是同一个真值。
        /// </summary>
        internal static HImage BuildScene(double row, double col)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", ImgW, ImgH);
            HOperatorSet.GenImageProto(proto, out HObject img, 180);
            HOperatorSet.GenCircle(out HObject disk, row, col, DiskRadius);
            HOperatorSet.OverpaintRegion(img, disk, 60, "fill");
            HOperatorSet.GenRectangle1(out HObject arm1, row - 40, col - 10, row + 40, col + 10);
            HOperatorSet.GenRectangle1(out HObject arm2, row - 10, col - 40, row + 10, col + 40);
            HOperatorSet.OverpaintRegion(img, arm1, 255, "fill");
            HOperatorSet.OverpaintRegion(img, arm2, 255, "fill");
            disk.Dispose();
            arm1.Dispose();
            arm2.Dispose();
            proto.Dispose();
            var image = new HImage(img);
            img.Dispose();
            return image;
        }

        /// <summary>两个相同图案的场景（多实例断言用），位置相距足够远（MaxOverlap 不会合并）</summary>
        private static HImage TwoPatternScene()
        {
            var scene = BuildScene(PatternRow, PatternCol);
            HOperatorSet.GenCircle(out HObject disk, 280, 360, DiskRadius);
            HOperatorSet.OverpaintRegion(scene, disk, 60, "fill");
            HOperatorSet.GenRectangle1(out HObject arm1, 240, 350, 320, 370);
            HOperatorSet.GenRectangle1(out HObject arm2, 270, 320, 290, 400);
            HOperatorSet.OverpaintRegion(scene, arm1, 255, "fill");
            HOperatorSet.OverpaintRegion(scene, arm2, 255, "fill");
            disk.Dispose();
            arm1.Dispose();
            arm2.Dispose();
            return scene;
        }

        private static HImage MakeFlat()
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", ImgW, ImgH);
            HOperatorSet.GenImageProto(proto, out HObject img, 180);
            proto.Dispose();
            var image = new HImage(img);
            img.Dispose();
            return image;
        }

        private static HImage RotateScene(HImage src, double deg)
        {
            // 注意：HOperatorSet.RotateImage 的角度实测按"度"解释（传弧度值 0.1745 实测只转了
            // 0.164°，与冒烟断言的偏差吻合），所以这里直接传度数
            HOperatorSet.RotateImage(src, out HObject rotated, deg, "constant");
            var result = new HImage(rotated);
            rotated.Dispose();
            return result;
        }

        private static HImage ZoomScene(HImage src, double factor)
        {
            HOperatorSet.ZoomImageFactor(src, out HObject zoomed, factor, factor, "constant");
            var result = new HImage(zoomed);
            zoomed.Dispose();
            return result;
        }

        private static double[] AsDoubles(object? tuple)
            => tuple is HTuple t && t.Length > 0 ? t.ToDArr() : Array.Empty<double>();

        private static ExecutionContext MakeContext(ILogService log) =>
            new(log, new FlowSession { FlowName = "匹配插件断言" }, new WorkspaceContext(),
                new System.Threading.CancellationTokenSource().Token);
    }

    /// <summary>测试用步骤数据：字典兜底的 IStepConfigData 最小实现</summary>
    internal sealed class FakeStepData : IStepConfigData
    {
        public Guid StepId { get; } = Guid.NewGuid();
        public string Icon { get; } = "";
        public string StepName { get; } = "Fake";
        public string Description { get; } = "";
        public Dictionary<string, object> InputValues { get; } = new();
        public void SetInputValue(string key, object value) => InputValues[key] = value;
        public void RemoveInputValue(string key) => InputValues.Remove(key);
        public bool IsLinked(string inputPortName) => false;
        public string GetLinkedAddress(string inputPortName) => null;
        public LinkReference GetLink(string inputPortName) => null;
        public void SetLink(string inputPortName, LinkReference link) { }
        public void RemoveLink(string inputPortName) { }
        public List<DynamicPortInfo> OutputPortDefinitions { get; set; } = new();
    }
}
