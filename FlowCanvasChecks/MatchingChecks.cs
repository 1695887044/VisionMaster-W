using System;
using System.Collections.Generic;
using System.Linq;
using Core.Halcon.Color;
using Core.Interfaces;
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
            RunNotFoundAndStaleness();
            RunCircleTemplate();
            RunSmearMask();
            RunRecipeContract();
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
        //  ⑦ 多模板/配方驱动：A/B 两条模板，RecipeName 端口选条目（二期核心）
        // ==================================================================
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
