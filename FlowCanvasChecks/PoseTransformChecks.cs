using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using HalconDotNet;
using Plugin.PoseTransform;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 坐标变换插件（Plugin.PoseTransform）断言。
    ///
    /// 两层：
    ///   · **数学层**（<see cref="PoseTransformMath"/>，纯托管）：正/反变换、角度映射、三类失配——
    ///     全部用**手算常量**钉住方向与符号（转置/轴交换这类静默错误只有手算常量能抓）；
    ///   · **HALCON 层**（<see cref="PoseTransformHalcon"/>，真算子）：姿态敏感（90° 换宽高）、
    ///     模板态自洽（面积相对差 &lt; 1e-6）、对齐图方向（T⁻¹，不是被推远两倍）；
    ///   · **插件层**：真跑 RunAlgorithm（真 ExecutionContext）——三类失配必须失败且带"下一步"，
    ///     成功要产出什么、失败不留残留、角度"接了才输出"。
    ///
    /// 为什么要单测"对齐图方向"：方案 §六 的公式块把区域与图像都写成同一方向的正变换——
    /// 区域在模板坐标系（正变换对），图在当前坐标系（应为逆变换）；写反了会"看着像对"地推远两倍。
    /// </summary>
    internal static class PoseTransformChecks
    {
        public static void Run()
        {
            Section("[PT] 坐标变换插件：正反变换 / 角度 / 三类失配 / 位姿跟随 / 端口面");

            // ================= 数学层 =================

            // ---- 1) 正变换：按契约展开（X=a11·Row+a12·Col+a31），平移符号也在内 ----
            var tIdentity = NinePoint(new[] { 1d, 0, 0, 1, 10, 20 });
            bool m1 = PoseTransformMath.TryMapPixelToMechanical(tIdentity, 100, 200, out double x1, out double y1, out string? e1);
            Check("[PT] 正变换按契约展开（(100,200) → (110,220)，手算常量）",
                m1 && Math.Abs(x1 - 110) < 1e-12 && Math.Abs(y1 - 220) < 1e-12,
                m1 ? $"({x1}, {y1})" : e1 ?? "失败");

            // 轴交换 + 符号：X = 0·Row + 2·Col + 5 = 405；Y = 3·Row + 0·Col + 7 = 307
            var tSwap = NinePoint(new[] { 0d, 2, 3, 0, 5, 7 });
            bool m2 = PoseTransformMath.TryMapPixelToMechanical(tSwap, 100, 200, out double x2, out double y2, out string? e2);
            Check("[PT] 轴交换/符号不错位（(100,200) → (405,307)，手算常量）",
                m2 && Math.Abs(x2 - 405) < 1e-12 && Math.Abs(y2 - 307) < 1e-12,
                m2 ? $"({x2}, {y2})" : e2 ?? "失败");

            // ---- 2) 反向互逆 ----
            bool inv = PoseTransformMath.TryMapMechanicalToPixel(tSwap, x2, y2, out double rr, out double cc, out string? e3);
            Check("[PT] 反向互逆（T⁻¹(T(p)) 误差 < 1e-9）",
                inv && Math.Abs(rr - 100) < 1e-9 && Math.Abs(cc - 200) < 1e-9,
                inv ? $"({rr:0.######}, {cc:0.######})" : e3 ?? "失败");

            // 旋转+缩放的矩阵也要互逆（非整数系数，查运算顺序）
            var tRot = NinePoint(new[] { 0.0173205080756888, -0.01, 0.01, 0.0173205080756888, 250.0, -80.0 });
            bool mRot = PoseTransformMath.TryMapPixelToMechanical(tRot, 321.7, 654.3, out double xr, out double yr, out _);
            double rr2 = 0, cc2 = 0;
            bool invRot = mRot && PoseTransformMath.TryMapMechanicalToPixel(tRot, xr, yr, out rr2, out cc2, out _);
            Check("[PT] 旋转+缩放矩阵的正反互逆（< 1e-9）",
                invRot && Math.Abs(rr2 - 321.7) < 1e-9 && Math.Abs(cc2 - 654.3) < 1e-9,
                invRot ? $"({rr2:0.######}, {cc2:0.######})" : "失败");

            // ---- 3) 角度：保向标定原样映射；轴交换（反射）取反 ----
            // M = s·R(α)（x=Col, y=Row）→ 机械角 = 像素角（任意 θ）
            double s = 0.02;
            double alpha = 30 * Math.PI / 180;
            var tSimilar = NinePoint(new[]
            {
                -s * Math.Sin(alpha), s * Math.Cos(alpha),
                s * Math.Cos(alpha),  s * Math.Sin(alpha),
                0d, 0d
            });
            bool ang1 = PoseTransformMath.TryPixelAngleToMechanical(tSimilar, 30, out double ma1, out string? ea1);
            Check("[PT] 角度：保向标定下像素 30° → 机械 30°（±1e-3）",
                ang1 && Math.Abs(ma1 - 30) < 1e-3, ang1 ? $"{ma1:0.####}°" : ea1 ?? "失败");
            bool ang2 = PoseTransformMath.TryPixelAngleToMechanical(tSimilar, -90, out double ma2, out _);
            Check("[PT] 角度：保向标定下像素 −90° → 机械 −90°（±1e-3）",
                ang2 && Math.Abs(ma2 + 90) < 1e-3, $"{ma2:0.####}°");
            bool ang0 = PoseTransformMath.TryPixelAngleToMechanical(tSimilar, 0, out double ma0, out _);
            Check("[PT] 角度：像素 0° → 机械 0°（基准定义，任何标定都成立）",
                ang0 && Math.Abs(ma0) < 1e-9, $"{ma0:0.######}°");

            // 轴交换 = 反射（X=Row, Y=Col）：60°−90° = −30°，转角取反由矩阵吸收
            var tMirror = NinePoint(new[] { 1d, 0, 0, 1, 0, 0 });
            bool angM = PoseTransformMath.TryPixelAngleToMechanical(tMirror, 30, out double maM, out _);
            Check("[PT] 角度：轴交换（反射）→ 转角取反（−30°，由矩阵吸收、不再校正）",
                angM && Math.Abs(maM + 30) < 1e-9, $"{maM:0.####}°");

            bool norm1 = Math.Abs(PoseTransformMath.NormalizeDegrees(190) + 170) < 1e-12
                      && Math.Abs(PoseTransformMath.NormalizeDegrees(-190) - 170) < 1e-12
                      && Math.Abs(PoseTransformMath.NormalizeDegrees(180) - 180) < 1e-12;
            Check("[PT] 角度归一化到 (−180, 180]", norm1, "");

            // ---- 4) 三类失配（数学层）：文案必须带"下一步" ----
            bool miss = !PoseTransformMath.CheckCalibrationUsable(null, out string? missError)
                        && (missError ?? "").Contains("未接标定") && (missError ?? "").Contains("「标定」");
            Check("[PT] 失配①未接标定 → 失败并指到「标定」插件", miss, missError ?? "（竟然通过）");

            var tScaleOnly = new CalibrationTransform
            {
                Kind = CalibrationKind.PixelScale,
                MmPerPixel = 0.02,
                Matrix = new[] { 0.02, 0, 0, 0.02, 0, 0 }
            };
            bool kindBad = !PoseTransformMath.CheckCalibrationUsable(tScaleOnly, out string? kindError)
                           && (kindError ?? "").Contains("九点");
            Check("[PT] 失配②当量标定当九点用 → 失败并指到「九点标定」", kindBad, kindError ?? "（竟然通过）");

            var tBroken = new CalibrationTransform { Kind = CalibrationKind.NinePoint, Matrix = new double[6] };
            bool broken = !PoseTransformMath.CheckCalibrationUsable(tBroken, out string? brokenError)
                          && (brokenError ?? "").Contains("损坏");
            Check("[PT] 矩阵缺失/退化 → 失败并指到「重新运行标定」", broken, brokenError ?? "（竟然通过）");

            var tSized = NinePoint(new[] { 0.02, 0, 0, 0.02, 0, 0 }, 2448, 2048);
            bool sizeBad = !PoseTransformMath.CheckImageSizeMatch(tSized, 1280, 1024, out string? sizeError)
                           && (sizeError ?? "").Contains("2448×2048") && (sizeError ?? "").Contains("1280×1024")
                           && (sizeError ?? "").Contains("重新标定");
            Check("[PT] 失配③图像尺寸不符 → 失败、两个尺寸都在文案里", sizeBad, sizeError ?? "（竟然通过）");
            bool sizeOk = PoseTransformMath.CheckImageSizeMatch(tSized, 2448, 2048, out _);
            bool sizeSkip = PoseTransformMath.CheckImageSizeMatch(
                NinePoint(new[] { 0.02, 0, 0, 0.02, 0, 0 }), 123, 456, out _);
            Check("[PT] 尺寸相符→通过；标定未记录尺寸→放行（无从比对，不误报）", sizeOk && sizeSkip, "");

            // ---- 5) 位姿 / 像素点守卫（数学层） ----
            bool poseNan = !PoseTransformMath.IsValidPose(double.NaN, 0, 0, out string? poseNanError)
                           && (poseNanError ?? "").Contains("位姿无效");
            bool poseZero = !PoseTransformMath.IsValidPose(0, 0, 0, out string? poseZeroError)
                            && (poseZeroError ?? "").Contains("位姿");
            bool poseOk = PoseTransformMath.IsValidPose(100, 100, 0, out _);
            Check("[PT] 位姿守卫：NaN/全零必须失败、正常位姿通过（防匹配失败被静默变换）",
                poseNan && poseZero && poseOk, poseNanError ?? poseZeroError ?? "（竟然通过）");

            bool ptZero = !PoseTransformMath.IsUsablePixelPoint(0, 0, out string? ptError)
                          && (ptError ?? "").Contains("(0,0)");
            bool ptNan = !PoseTransformMath.IsUsablePixelPoint(double.NaN, 5, out _);
            Check("[PT] 像素点守卫：(0,0)/NaN 必须失败（与位姿守卫同口径）", ptZero && ptNan, ptError ?? "（竟然通过）");

            // ================= HALCON 层 =================

            // ---- 6) 位姿跟随：姿态敏感 + 模板态自洽 ----
            // 基准：41 行 × 21 列矩形（Rows 80..120, Cols 90..110），中心正好 (100,100)
            HOperatorSet.GenRectangle1(out HObject rectObj, 80, 90, 120, 110);
            using var baseRegion = new HRegion(rectObj);
            TryArea(baseRegion, out double baseArea, out _, out _);

            using (var followed90 = PoseTransformHalcon.FollowRegion(baseRegion, 100, 100, 100, 100, 90))
            {
                HOperatorSet.SmallestRectangle1(followed90, out HTuple f1, out HTuple g1, out HTuple f2, out HTuple g2);
                double hF = f2[0].D - f1[0].D + 1;
                double wF = g2[0].D - g1[0].D + 1;
                TryArea(followed90, out double a90, out _, out _);
                double areaRel = Math.Abs(a90 - baseArea) / baseArea;

                Check("[PT] 跟随姿态敏感：转 90° → 外接框宽高互换（41×21 → 21×41，±1px）",
                    Math.Abs(hF - 21) <= 1 && Math.Abs(wF - 41) <= 1,
                    $"高 {hF} 宽 {wF}（基准 高 41 宽 21）");
                Check("[PT] 旋转不丢面积（相对差 < 1e-6）", areaRel < 1e-6, $"相对差 {areaRel:0.###e+0}");
            }

            using (var followedSame = PoseTransformHalcon.FollowRegion(baseRegion, 100, 100, 100, 100, 0))
            {
                TryArea(followedSame, out double aSame, out double cRow, out double cCol);
                double rel = Math.Abs(aSame - baseArea) / baseArea;
                Check("[PT] 模板态自洽：位姿=参考点、角度 0 → 与基准 ROI 完全重合（面积相对差 < 1e-6）",
                    rel < 1e-6 && Math.Abs(cRow - 100) < 1e-9 && Math.Abs(cCol - 100) < 1e-9,
                    $"面积相对差 {rel:0.###e+0}，中心 ({cRow:0.####},{cCol:0.####})");
            }

            // ---- 7) 对齐图方向：内容从"位姿"搬回"参考点"（T⁻¹） ----
            HOperatorSet.GenImageConst(out HObject canvasObj, "byte", 200, 200);
            using var canvas = new HImage(canvasObj);
            HOperatorSet.GenCircle(out HObject dotObj, 100, 50, 10);
            using var dot = new HRegion(dotObj);
            HOperatorSet.PaintRegion(dot, canvas, out HObject paintedObj, 255, "fill");
            using var painted = new HImage(paintedObj);

            using (var aligned = PoseTransformHalcon.FollowImageBack(painted, 60, 60, 100, 50, 0))
            {
                HOperatorSet.Threshold(aligned, out HObject blobObj, 128, 255);
                using var blob = new HRegion(blobObj);
                TryArea(blob, out double blobArea, out double blobRow, out double blobCol);
                // 位姿在 (100,50)、参考点 (60,60)：对齐后亮点必须落在参考点。
                // 若方向写反（用正变换推图），亮点会到 (140,40) —— 一倍偏差的两倍远。
                Check("[PT] 对齐图方向：内容从位姿搬回参考点（T⁻¹，不是推远两倍）",
                    blobArea > 100 && Math.Abs(blobRow - 60) <= 1 && Math.Abs(blobCol - 60) <= 1,
                    $"亮点中心 ({blobRow:0.##},{blobCol:0.##})，面积 {blobArea:0.#}（期望中心 (60,60)）");
            }

            // ================= 插件层 =================

            var plugin = new PoseTransformPlugin();
            Check("[PT] 构造后 DisplayImage 为 null（构造期不碰 HALCON 原生库）",
                plugin.DisplayImage == null, "");

            var ctx = new ExecutionContext(
                new StubLog(), new FlowSession { FlowName = "坐标变换断言" },
                new WorkspaceContext(), new CancellationTokenSource().Token);

            // ---- 8) 插件级：三类失配 ----
            plugin.Mode = TransformMode.ToMechanical;

            plugin.RunAlgorithm(ctx);
            Check("[PT] 插件级失配①未接标定 → 失败且文案含「标定」下一步",
                plugin.Success.Value is false && Err(plugin).Contains("未接标定") && Err(plugin).Contains("「标定」"),
                Err(plugin));

            plugin.Transform.Value = tScaleOnly;
            plugin.RunAlgorithm(ctx);
            Check("[PT] 插件级失配②当量标定当九点用 → 失败且指到「九点标定」",
                plugin.Success.Value is false && Err(plugin).Contains("九点"), Err(plugin));

            plugin.Transform.Value = tSized;
            HOperatorSet.GenImageConst(out HObject bigObj, "byte", 200, 200);
            var bigImage = new HImage(bigObj);
            plugin.SrcImage.Value = bigImage;
            plugin.RunAlgorithm(ctx);
            Check("[PT] 插件级失配③图像尺寸不符 → 失败且报「重新标定」与两个尺寸",
                plugin.Success.Value is false && Err(plugin).Contains("重新标定") && Err(plugin).Contains("2448×2048"),
                Err(plugin));
            plugin.SrcImage.Value = null!;
            bigImage.Dispose();

            // ---- 9) 插件级：成功路径（手算常量 + 回显自校验 + 角度"接了才输出"） ----
            plugin.Transform.Value = tSwap;
            plugin.PixelPointRow.Value = 100;
            plugin.PixelPointCol.Value = 200;
            plugin.RunAlgorithm(ctx);
            Check("[PT] 插件级成功：机械坐标 = 手算 (405,307)，Success=true",
                plugin.Success.Value is true
                && Math.Abs(plugin.MechanicalX.TypedValue - 405) < 1e-9
                && Math.Abs(plugin.MechanicalY.TypedValue - 307) < 1e-9,
                $"({plugin.MechanicalX.TypedValue}, {plugin.MechanicalY.TypedValue}) {Err(plugin)}");
            Check("[PT] 反向回显 = 原始像素点（自校验端口）",
                Math.Abs(plugin.PixelEchoRow.TypedValue - 100) < 1e-9 && Math.Abs(plugin.PixelEchoCol.TypedValue - 200) < 1e-9,
                $"({plugin.PixelEchoRow.TypedValue}, {plugin.PixelEchoCol.TypedValue})");
            Check("[PT] 像素角度未接 → MechanicalAngle 不输出（保持 0，不瞎给）",
                plugin.MechanicalAngle.TypedValue == 0.0, $"{plugin.MechanicalAngle.TypedValue}");

            var angleSource = new OutputPort<double>("Angle") { Value = 30.0 };
            plugin.PixelAngle.LinkedSource = angleSource;
            plugin.Transform.Value = tSimilar;
            plugin.PixelPointRow.Value = 10;
            plugin.PixelPointCol.Value = 10;
            plugin.RunAlgorithm(ctx);
            Check("[PT] 像素角度接了 → MechanicalAngle ≈ 30°（保向标定原样映射）",
                plugin.Success.Value is true && Math.Abs(plugin.MechanicalAngle.TypedValue - 30) < 1e-3,
                $"{plugin.MechanicalAngle.TypedValue:0.####}° {Err(plugin)}");
            plugin.PixelAngle.LinkedSource = null!;

            // ---- 10) 插件级：跟随的失败与成功 ----
            plugin.Mode = TransformMode.FollowRoi;

            plugin.RunAlgorithm(ctx);
            Check("[PT] 空 ROI → 失败（不静默输出空区域）",
                plugin.Success.Value is false && Err(plugin).Contains("基准 ROI"), Err(plugin));

            plugin.BaseRegion.Value = baseRegion;
            plugin.PoseRow.Value = double.NaN;
            plugin.PoseCol.Value = 100;
            plugin.PoseAngle.Value = 0;
            plugin.RunAlgorithm(ctx);
            Check("[PT] NaN 位姿 → 失败（防匹配失败被静默变换）",
                plugin.Success.Value is false && Err(plugin).Contains("位姿无效"), Err(plugin));

            plugin.PoseRow.Value = 0;
            plugin.PoseCol.Value = 0;
            plugin.PoseAngle.Value = 0;
            plugin.RunAlgorithm(ctx);
            Check("[PT] 位姿全零（未接/匹配失败）→ 失败并说明原因",
                plugin.Success.Value is false && Err(plugin).Contains("位姿"), Err(plugin));

            // 模板态：位姿 = 参考点 (100,100)、角度 0 → 跟随区域与基准重合
            plugin.PoseRow.Value = 100;
            plugin.PoseCol.Value = 100;
            plugin.RunAlgorithm(ctx);
            var followedOut = plugin.FollowedRegion.Value as HRegion;
            double relOut = 0;
            if (followedOut != null)
            {
                TryArea(followedOut, out double fo, out _, out _);
                relOut = Math.Abs(fo - baseArea) / baseArea;
            }
            Check("[PT] 插件级跟随成功（模板态）：FollowedRegion 有值且与基准重合（相对差 < 1e-6）",
                plugin.Success.Value is true && followedOut != null && relOut < 1e-6,
                followedOut == null ? Err(plugin) : $"面积相对差 {relOut:0.###e+0}");

            // 对齐图开关：关（默认）→ 不输出；开 + 接了 SrcImage → 有值
            Check("[PT] 对齐图默认关：FollowedImage 未输出", plugin.FollowedImage.Value == null, "");
            plugin.EmitFollowedImage = true;
            HOperatorSet.GenImageConst(out HObject smallObj, "byte", 200, 200);
            var smallImage = new HImage(smallObj);
            plugin.SrcImage.Value = smallImage;
            plugin.RunAlgorithm(ctx);
            Check("[PT] 勾选对齐图 + 接了 SrcImage → FollowedImage 有值",
                plugin.Success.Value is true && plugin.FollowedImage.Value is HImage, Err(plugin));
            plugin.SrcImage.Value = null!;
            smallImage.Dispose();

            plugin.Dispose();

            // ---- 11) 端口面：改名即断下游接线，锁住 ----
            var probe = new PoseTransformPlugin();
            var inputs = probe.Inputs.Keys.ToList();
            Check("[PT] 输入端口面齐全（绑定用）",
                new[]
                {
                    "SrcImage", "BaseRegion", "TemplateRefRow", "TemplateRefCol",
                    "PoseRow", "PoseCol", "PoseAngle", "Transform",
                    "PixelPointRow", "PixelPointCol", "PixelAngle"
                }.All(inputs.Contains),
                string.Join(",", inputs));

            var outputs = probe.Outputs.Keys.ToList();
            Check("[PT] 输出端口面齐全",
                new[]
                {
                    "FollowedRegion", "FollowedImage",
                    "MechanicalX", "MechanicalY", "MechanicalAngle",
                    "PixelEchoRow", "PixelEchoCol"
                }.All(outputs.Contains),
                string.Join(",", outputs));

            Check("[PT] Transform 端口类型 = CalibrationTransform（接标定插件输出）",
                probe.Inputs["Transform"].DataType == typeof(CalibrationTransform),
                probe.Inputs["Transform"].DataType.Name);

            Check("[PT] 跟随端口类型 = HRegion / HImage（接 BlobDetect.MaskRegion 与图像下游）",
                probe.Outputs["FollowedRegion"].DataType == typeof(HRegion)
                && probe.Outputs["FollowedImage"].DataType == typeof(HImage), "");

            Check("[PT] 插件带 [Display]（GroupName=定位，否则工具箱里拖不出来）",
                probe.GetType().GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false).Length == 1,
                "");
        }

        /// <summary>构造"九点"标定（本套断言只关心矩阵语义，MmPerPixel 不参与计算）。</summary>
        private static CalibrationTransform NinePoint(double[] matrix, int width = 0, int height = 0) => new()
        {
            Kind = CalibrationKind.NinePoint,
            MmPerPixel = 0.02,
            Matrix = matrix,
            SourceImageWidth = width,
            SourceImageHeight = height,
            SourceTag = "合成",
            CameraSerial = "SYNTH-PT"
        };

        private static string Err(PoseTransformPlugin plugin) =>
            plugin.ErrorMessage.Value?.ToString() ?? string.Empty;

        /// <summary>区域面积/中心（空区域/未初始化返回 false，不抛）。</summary>
        private static bool TryArea(HRegion region, out double area, out double row, out double col)
        {
            area = 0;
            row = 0;
            col = 0;
            try
            {
                HOperatorSet.AreaCenter(region, out HTuple a, out HTuple r, out HTuple c);
                if (a.Length == 0 || r.Length == 0 || c.Length == 0)
                    return false;

                area = a[0].D;
                row = r[0].D;
                col = c[0].D;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
