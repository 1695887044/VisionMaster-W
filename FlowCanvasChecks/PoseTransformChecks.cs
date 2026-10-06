using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using HalconDotNet;
using Plugin.Calibration;
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
    ///   · **插件层**：真跑 RunAlgorithm（真 ExecutionContext）——四类失配必须失败且带"下一步"，
    ///     成功要产出什么、失败不留残留、角度"接了才输出"。
    ///
    /// 为什么要单测"对齐图方向"：方案 §六 的公式块把区域与图像都写成同一方向的正变换——
    /// 区域在模板坐标系（正变换对），图在当前坐标系（应为逆变换）；写反了会"看着像对"地推远两倍。
    /// </summary>
    internal static class PoseTransformChecks
    {
        public static void Run()
        {
            Section("[PT] 坐标变换插件：正反变换 / 角度 / 四类失配 / 位姿跟随 / 透视联动 / 网格联动 / 端口面");

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
            bool ang1 = PoseTransformMath.TryPixelAngleToMechanical(tSimilar, 30, 0, 0, out double ma1, out string? ea1);
            Check("[PT] 角度：保向标定下像素 30° → 机械 30°（±1e-3）",
                ang1 && Math.Abs(ma1 - 30) < 1e-3, ang1 ? $"{ma1:0.####}°" : ea1 ?? "失败");
            bool ang2 = PoseTransformMath.TryPixelAngleToMechanical(tSimilar, -90, 0, 0, out double ma2, out _);
            Check("[PT] 角度：保向标定下像素 −90° → 机械 −90°（±1e-3）",
                ang2 && Math.Abs(ma2 + 90) < 1e-3, $"{ma2:0.####}°");
            bool ang0 = PoseTransformMath.TryPixelAngleToMechanical(tSimilar, 0, 0, 0, out double ma0, out _);
            Check("[PT] 角度：像素 0° → 机械 0°（基准定义，任何标定都成立）",
                ang0 && Math.Abs(ma0) < 1e-9, $"{ma0:0.######}°");

            // 轴交换 = 反射（X=Row, Y=Col）：60°−90° = −30°，转角取反由矩阵吸收
            var tMirror = NinePoint(new[] { 1d, 0, 0, 1, 0, 0 });
            bool angM = PoseTransformMath.TryPixelAngleToMechanical(tMirror, 30, 0, 0, out double maM, out _);
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

            // ---- 8b) 插件级失配④：相机身份不符（多相机共线防"拿错相机的标定"） ----
            var tSerial = NinePoint(new[] { 0d, 2, 3, 0, 5, 7 });
            tSerial.CameraSerial = "CAM-A";
            plugin.Transform.Value = tSerial;
            plugin.PixelPointRow.Value = 100;
            plugin.PixelPointCol.Value = 200;

            plugin.SourceSerial.Value = "CAM-B";
            plugin.RunAlgorithm(ctx);
            Check("[PT] 失配④相机身份不符 → 失败、两台相机都在文案里",
                plugin.Success.Value is false && Err(plugin).Contains("相机身份不符")
                && Err(plugin).Contains("CAM-A") && Err(plugin).Contains("CAM-B"),
                Err(plugin));

            plugin.SourceSerial.Value = "cam-a";   // 大小写不敏感
            plugin.RunAlgorithm(ctx);
            Check("[PT] 身份相符（忽略大小写）→ 正常成功（手算 (405,307)）",
                plugin.Success.Value is true
                && Math.Abs(plugin.MechanicalX.TypedValue - 405) < 1e-9
                && Math.Abs(plugin.MechanicalY.TypedValue - 307) < 1e-9,
                $"({plugin.MechanicalX.TypedValue}, {plugin.MechanicalY.TypedValue}) {Err(plugin)}");

            plugin.SourceSerial.Value = "";
            plugin.RunAlgorithm(ctx);
            Check("[PT] 上游序列号为空 → 不查身份（离线/单相机不打扰）",
                plugin.Success.Value is true, Err(plugin));

            // 反向：标定没记录序列号（旧文件）时，上游给了也不拦
            var tNoSerial = NinePoint(new[] { 0d, 2, 3, 0, 5, 7 });
            tNoSerial.CameraSerial = string.Empty;
            plugin.Transform.Value = tNoSerial;
            plugin.SourceSerial.Value = "CAM-B";
            plugin.RunAlgorithm(ctx);
            Check("[PT] 标定未记录序列号 → 放行（旧标定文件不误拦）",
                plugin.Success.Value is true, Err(plugin));
            plugin.SourceSerial.Value = null!;

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

            // ---- 新增：角度换算失败 → 所有标量端口保持开轮清零值（不留半成品） ----
            // 用 NaN 像素角触发 TryPixelAngleToMechanical 失败：此时 MechanicalX/Y、PixelEchoRow/Col
            // 已经算出来了，若先写端口再算角度，下游会读到"看起来正常"的半成品值。
            // 注意必须显式设 Mode=ToMechanical（默认是 FollowRoi，会因"基准 ROI 为空"提前失败，
            // 让断言假通过）。
            var failPlugin = new PoseTransformPlugin { Mode = TransformMode.ToMechanical };
            failPlugin.Transform.Value = tSwap;
            failPlugin.PixelPointRow.Value = 100;
            failPlugin.PixelPointCol.Value = 200;
            failPlugin.PixelAngle.Value = double.NaN;
            failPlugin.RunAlgorithm(ctx);
            Check("[PT] 角度换算失败 → 五个标量端口全部为 0（不留半成品值）",
                failPlugin.Success.Value is false
                && Err(failPlugin).Contains("角度")
                && failPlugin.MechanicalX.TypedValue == 0.0
                && failPlugin.MechanicalY.TypedValue == 0.0
                && failPlugin.PixelEchoRow.TypedValue == 0.0
                && failPlugin.PixelEchoCol.TypedValue == 0.0
                && failPlugin.MechanicalAngle.TypedValue == 0.0,
                $"Success={failPlugin.Success.Value} ({failPlugin.MechanicalX.TypedValue}, {failPlugin.MechanicalY.TypedValue}) 回显=({failPlugin.PixelEchoRow.TypedValue}, {failPlugin.PixelEchoCol.TypedValue}) Err={Err(failPlugin)}");
            failPlugin.Dispose();

            // ---- 新增：角度端口断开后 MechanicalAngle 复位（不残留上一轮角度） ----
            var anglePlugin = new PoseTransformPlugin { Mode = TransformMode.ToMechanical };
            anglePlugin.Transform.Value = tSwap;
            anglePlugin.PixelPointRow.Value = 100;
            anglePlugin.PixelPointCol.Value = 200;
            anglePlugin.PixelAngle.Value = 30.0;
            anglePlugin.RunAlgorithm(ctx);
            Check("[PT] 角度有值 → MechanicalAngle 已输出（前置条件）",
                anglePlugin.Success.Value is true && anglePlugin.MechanicalAngle.TypedValue != 0.0,
                $"{anglePlugin.MechanicalAngle.TypedValue:0.####}° {Err(anglePlugin)}");
            anglePlugin.PixelAngle.Value = 0.0;
            anglePlugin.RunAlgorithm(ctx);
            Check("[PT] 角度断开/归零 → MechanicalAngle 复位为 0（不残留上一轮）",
                anglePlugin.Success.Value is true && anglePlugin.MechanicalAngle.TypedValue == 0.0,
                $"{anglePlugin.MechanicalAngle.TypedValue:0.####}° {Err(anglePlugin)}");
            anglePlugin.Dispose();

            // ---- 11) 端口面：改名即断下游接线，锁住 ----
            var probe = new PoseTransformPlugin();
            var inputs = probe.Inputs.Keys.ToList();
            Check("[PT] 输入端口面齐全（绑定用）",
                new[]
                {
                    "SrcImage", "BaseRegion", "TemplateRefRow", "TemplateRefCol",
                    "PoseRow", "PoseCol", "PoseAngle", "Transform",
                    "PixelPointRow", "PixelPointCol", "PixelAngle", "SourceSerial"
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

            // ================= 透视标定联动（二期①；承接标定侧 #66 的消费方联动项） =================
            // 真透视样例（手算友好）：w 在 (250,500) 处恰好 = 2、在 (0,0) 处 = 1，便于逐位手算核对。
            var tProjective = Perspective(new[] { 2.0, 0.2, 30.0, 0.5, 1.5, -75.0, 0.002, 0.001, 1.0 });

            // ---- 12) 透视正/反变换：手算常量 ----
            bool pm1 = PoseTransformMath.TryMapPixelToMechanical(tProjective, 250, 500, out double px1, out double py1, out string? pe1);
            bool pm2 = PoseTransformMath.TryMapPixelToMechanical(tProjective, 0, 0, out double px2, out double py2, out _);
            bool pinv = PoseTransformMath.TryMapMechanicalToPixel(tProjective, 315, 400, out double prr, out double pcc, out string? pe2);
            Check("[PT] 透视正/反变换：手算常量（(250,500)→(315,400)、(0,0)→(30,−75)；互逆 < 1e-9）",
                pm1 && Math.Abs(px1 - 315) < 1e-9 && Math.Abs(py1 - 400) < 1e-9
                && pm2 && Math.Abs(px2 - 30) < 1e-9 && Math.Abs(py2 + 75) < 1e-9
                && pinv && Math.Abs(prr - 250) < 1e-9 && Math.Abs(pcc - 500) < 1e-9,
                pm1 ? $"({px1:0.######},{py1:0.######})、({px2:0.######},{py2:0.######}) 反向 ({prr:0.######},{pcc:0.######})" : pe1 ?? pe2 ?? "失败");

            // ---- 13) 缺失/退化投影矩阵 → 拒绝（文案含「损坏/重新运行标定」） ----
            var tProjMissing = new CalibrationTransform { Kind = CalibrationKind.Perspective, ProjectiveMatrix = null };
            var tProjZero = new CalibrationTransform { Kind = CalibrationKind.Perspective, ProjectiveMatrix = new double[9] };
            var tProjShort = new CalibrationTransform { Kind = CalibrationKind.Perspective, ProjectiveMatrix = new double[8] };
            bool pjMiss = !PoseTransformMath.CheckCalibrationUsable(tProjMissing, out string? pje1)
                          && (pje1 ?? "").Contains("损坏") && (pje1 ?? "").Contains("重新运行");
            bool pjZero = !PoseTransformMath.CheckCalibrationUsable(tProjZero, out string? pje2)
                          && (pje2 ?? "").Contains("损坏") && (pje2 ?? "").Contains("重新运行");
            bool pjShort = !PoseTransformMath.CheckCalibrationUsable(tProjShort, out _);
            Check("[PT] 透视矩阵缺失/退化/长度不足 → 拒绝且文案含「损坏/重新运行标定」",
                pjMiss && pjZero && pjShort, pje1 ?? pje2 ?? "");

            // ---- 14) 未知 Kind → 拒绝（不静默按旧逻辑；矩阵故意给"可用仿射"作反证） ----
            var tUnknown = new CalibrationTransform { Kind = (CalibrationKind)7, Matrix = new[] { 1d, 0, 0, 1, 10, 20 } };
            bool unknownRejected = !PoseTransformMath.CheckCalibrationUsable(tUnknown, out string? ue1)
                                   && (ue1 ?? "").Contains("不支持的标定类型");
            Check("[PT] 未知 Kind=(CalibrationKind)7 → 拒绝（防「忘记分支」静默按旧逻辑放行）",
                unknownRejected, ue1 ?? "（竟然通过）");

            // ---- 15) 分母≈0 的像素点 → 明确失败（w=0 线上拒绝；线外点照常映射） ----
            var tDegLine = Perspective(new[] { 1.0, 0, 0, 0, 1, 0, 1, 0, -100.0 });
            bool onLine = !PoseTransformMath.TryMapPixelToMechanical(tDegLine, 100, 50, out _, out _, out string? dle)
                          && (dle ?? "").Contains("退化线");
            bool offLine = PoseTransformMath.TryMapPixelToMechanical(tDegLine, 0, 50, out double offX, out double offY, out _)
                           && Math.Abs(offX) < 1e-12 && Math.Abs(offY + 0.5) < 1e-12;
            Check("[PT] 透视分母≈0 的像素点 → 明确失败（w=0 线上拒绝；线外点照常映射）",
                onLine && offLine, dle ?? "（竟然通过）");

            // ---- 16) 角度位置相关：J(p) 手算对比（±1e-6）；同 θ 两位置结果不同 ----
            bool jacOk = PoseTransformMath.JacobianAt(tProjective.ProjectiveMatrix!, 250, 500, out double ja, out double jb, out double jc, out double jd)
                         && Math.Abs(ja - 0.685) < 1e-6 && Math.Abs(jb + 0.0575) < 1e-6
                         && Math.Abs(jc + 0.15) < 1e-6 && Math.Abs(jd - 0.55) < 1e-6;
            bool pa1 = PoseTransformMath.TryPixelAngleToMechanical(tProjective, 30, 250, 500, out double pma1, out string? pae1);
            bool pa2 = PoseTransformMath.TryPixelAngleToMechanical(tProjective, 30, 100, 100, out double pma2, out _);
            // 期望角：用同一手算 J 按契约公式独立算（v0=J·(0,1)、vθ=J·(sinθ,cosθ)）
            double pRad = 30 * Math.PI / 180.0;
            double expAng = PoseTransformMath.NormalizeDegrees(
                Math.Atan2(jc * Math.Sin(pRad) + jd * Math.Cos(pRad), ja * Math.Sin(pRad) + jb * Math.Cos(pRad)) * 180.0 / Math.PI
                - Math.Atan2(jd, jb) * 180.0 / Math.PI);
            Check("[PT] 透视角度位置相关：J(p) 手算对比（±1e-6）；同 θ 两位置结果不同",
                jacOk && pa1 && pa2 && Math.Abs(pma1 - expAng) < 1e-6 && Math.Abs(pma1 - pma2) > 0.5,
                $"J=({ja:0.####},{jb:0.####},{jc:0.####},{jd:0.####})；θ=30°@(250,500)={pma1:0.####}°（期望 {expAng:0.####}°）vs @(100,100)={pma2:0.####}° {pae1}");

            // ---- 17) 插件级透视 ToMechanical：坐标=手算、回显 < 1e-6、机械角=手算 ----
            var projPlugin = new PoseTransformPlugin { Mode = TransformMode.ToMechanical };
            projPlugin.Transform.Value = tProjective;
            projPlugin.PixelPointRow.Value = 250;
            projPlugin.PixelPointCol.Value = 500;
            projPlugin.PixelAngle.Value = 30.0;
            projPlugin.RunAlgorithm(ctx);
            Check("[PT] 插件级透视 ToMechanical：坐标=手算 (315,400)、回显 < 1e-6、机械角=手算",
                projPlugin.Success.Value is true
                && Math.Abs(projPlugin.MechanicalX.TypedValue - 315) < 1e-9
                && Math.Abs(projPlugin.MechanicalY.TypedValue - 400) < 1e-9
                && Math.Abs(projPlugin.PixelEchoRow.TypedValue - 250) < 1e-6
                && Math.Abs(projPlugin.PixelEchoCol.TypedValue - 500) < 1e-6
                && Math.Abs(projPlugin.MechanicalAngle.TypedValue - expAng) < 1e-6,
                $"({projPlugin.MechanicalX.TypedValue:0.####},{projPlugin.MechanicalY.TypedValue:0.####}) 回显=({projPlugin.PixelEchoRow.TypedValue:0.####},{projPlugin.PixelEchoCol.TypedValue:0.####}) 角={projPlugin.MechanicalAngle.TypedValue:0.####}° {Err(projPlugin)}");
            projPlugin.Dispose();

            // ---- 18) FollowRoi 与标定类型解耦：九点/透视输出完全一致；不接标定照常成功 ----
            var fA = new PoseTransformPlugin { Mode = TransformMode.FollowRoi };
            fA.Transform.Value = tSwap;            // 九点（跟随不消费标定，仅"挂着"）
            var fB = new PoseTransformPlugin { Mode = TransformMode.FollowRoi };
            fB.Transform.Value = tProjective;      // 透视
            var fC = new PoseTransformPlugin { Mode = TransformMode.FollowRoi };   // 不接标定
            foreach (var f in new[] { fA, fB, fC })
            {
                f.BaseRegion.Value = baseRegion;
                f.PoseRow.Value = 110;
                f.PoseCol.Value = 95;
                f.PoseAngle.Value = 15;
                f.RunAlgorithm(ctx);
            }
            double fAreaA = 0, fRowA = 0, fColA = 0, fAreaB = 0, fRowB = 0, fColB = 0, fAreaC = 0;
            if (fA.FollowedRegion.Value is HRegion rA) TryArea(rA, out fAreaA, out fRowA, out fColA);
            if (fB.FollowedRegion.Value is HRegion rB) TryArea(rB, out fAreaB, out fRowB, out fColB);
            if (fC.FollowedRegion.Value is HRegion rC) TryArea(rC, out fAreaC, out _, out _);
            Check("[PT] FollowRoi 与标定类型解耦：九点/透视输出完全一致、不接标定照常成功",
                fA.Success.Value is true && fB.Success.Value is true && fC.Success.Value is true
                && fAreaA > 0
                && Math.Abs(fAreaA - fAreaB) <= 1e-9 * fAreaA
                && Math.Abs(fRowA - fRowB) < 1e-9 && Math.Abs(fColA - fColB) < 1e-9
                && Math.Abs(fAreaA - fAreaC) <= 1e-9 * fAreaA,
                $"面积 {fAreaA:0.###}/{fAreaB:0.###}/{fAreaC:0.###}，中心差 ({Math.Abs(fRowA - fRowB):0.###e+0},{Math.Abs(fColA - fColB):0.###e+0}) {Err(fA)}{Err(fB)}{Err(fC)}");
            fA.Dispose();
            fB.Dispose();
            fC.Dispose();

            // ---- 19) 失配②文案：同时含「九点」与「透视」（旧断言依赖「九点」不丢） ----
            bool twoWords = PoseTransformMath.PixelScaleCannotMapMessage.Contains("九点")
                            && PoseTransformMath.PixelScaleCannotMapMessage.Contains("透视");
            bool twoWordsViaCheck = !PoseTransformMath.CheckCalibrationUsable(tScaleOnly, out string? twoErr)
                                    && (twoErr ?? "").Contains("九点") && (twoErr ?? "").Contains("透视");
            Check("[PT] 失配②文案同时含「九点」与「透视」（旧断言依赖「九点」不丢）",
                twoWords && twoWordsViaCheck, twoErr ?? "");

            // ---- 20) 尺寸失配（透视）：两个尺寸都进文案；尺寸相符放行 ----
            var tProjSized = Perspective(new[] { 2.0, 0.2, 30.0, 0.5, 1.5, -75.0, 0.002, 0.001, 1.0 }, 2448, 2048);
            bool pSizeBad = !PoseTransformMath.CheckImageSizeMatch(tProjSized, 1280, 1024, out string? psErr)
                            && (psErr ?? "").Contains("2448×2048") && (psErr ?? "").Contains("1280×1024")
                            && (psErr ?? "").Contains("重新标定");
            bool pSizeOk = PoseTransformMath.CheckImageSizeMatch(tProjSized, 2448, 2048, out _);
            Check("[PT] 尺寸失配（透视）：两个尺寸都进文案；相符放行",
                pSizeBad && pSizeOk, psErr ?? "（竟然通过）");

            // ================= 网格标定联动（二期②） =================

            // ---- 21) 仿射等价网格：分段仿射必须与原仿射**处处相等**（三角化/重心插值的精确正确性断言） ----
            var tAffineForMesh = NinePoint(new[] { 0.0, 0.02, 0.02, 0.0, 100.0, 50.0 });   // X=0.02·Col+100, Y=0.02·Row+50
            (double x, double y) AffineAt(double row, double col) => (
                tAffineForMesh.Matrix[0] * row + tAffineForMesh.Matrix[1] * col + tAffineForMesh.Matrix[4],
                tAffineForMesh.Matrix[2] * row + tAffineForMesh.Matrix[3] * col + tAffineForMesh.Matrix[5]);

            var meshNodes = new double[3 * 3 * 4];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    double pr = 100 + r * 100, pc = 50 + c * 100;
                    var (mx, my) = AffineAt(pr, pc);
                    int k = (r * 3 + c) * 4;
                    meshNodes[k] = pr;
                    meshNodes[k + 1] = pc;
                    meshNodes[k + 2] = mx;
                    meshNodes[k + 3] = my;
                }
            }
            var tMeshAffine = Mesh(meshNodes, 3);

            bool meshUsable = PoseTransformMath.CheckCalibrationUsable(tMeshAffine, out string? meshUseErr);
            double worstMeshDelta = 0;
            foreach (var (pr, pc) in new[] { (150.0, 100.0), (250.0, 200.0), (200.0, 150.0), (101.0, 51.0) })
            {
                PoseTransformMath.TryMapPixelToMechanical(tMeshAffine, pr, pc, out double mx, out double my, out _);
                var (ax0, ay0) = AffineAt(pr, pc);
                worstMeshDelta = Math.Max(worstMeshDelta, Math.Max(Math.Abs(mx - ax0), Math.Abs(my - ay0)));
            }
            Check("[PT] 网格（节点取自同一仿射）：映射与仿射处处一致（< 1e-9，含节点与格内点）",
                meshUsable && worstMeshDelta < 1e-9, meshUseErr ?? $"{worstMeshDelta:0.###e+0}");

            // 反向互逆：像素 (200,150) 的机械坐标 = (103, 54)（手算）
            bool meshInv = PoseTransformMath.TryMapMechanicalToPixel(tMeshAffine, 103.0, 54.0, out double invRow, out double invCol, out string? meshInvErr)
                           && Math.Abs(invRow - 200) < 1e-9 && Math.Abs(invCol - 150) < 1e-9;
            Check("[PT] 网格反向：机械 → 像素 回到原像素（< 1e-9）",
                meshInv, meshInvErr ?? $"({invRow:0.######},{invCol:0.######})");

            // 角度：仿射等价网格下，网格角度换算（局部雅可比）必须与九点角度换算一致
            bool meshAngleCallOk = PoseTransformMath.TryPixelAngleToMechanical(
                tMeshAffine, 30.0, 200.0, 150.0, out double meshAngle, out string? meshAngleErr);
            bool affineAngleCallOk = PoseTransformMath.TryPixelAngleToMechanical(
                tAffineForMesh, 30.0, 200.0, 150.0, out double affineAngle, out _);
            Check("[PT] 网格角度：与九点角度换算一致（局部雅可比口径正确）",
                meshAngleCallOk && affineAngleCallOk && Math.Abs(meshAngle - affineAngle) < 1e-9,
                meshAngleErr ?? $"网格 {meshAngle:0.####}° ≠ 九点 {affineAngle:0.####}°");

            // ---- 22) 畸变网格：消费侧映射必须比全局仿射准一倍以上（与 [CAL] 同一份共享数学） ----
            const double ptMmPerPixel = 0.2;
            const double ptSpacing = 40.0;
            const double ptK = 0.05;
            const double ptCx = 200.0, ptCy = 150.0;
            const double ptCenterRow = 240.0, ptCenterCol = 320.0;
            double ptRMax = Math.Sqrt(2) * ptSpacing / ptMmPerPixel;
            (double Row, double Col) PtObserved(double x, double y)
            {
                double u = (x - ptCx) / ptMmPerPixel;
                double v = (y - ptCy) / ptMmPerPixel;
                double rho = Math.Sqrt(u * u + v * v) / ptRMax;
                double s = 1 + ptK * rho * rho;
                return (ptCenterRow + u * s, ptCenterCol + v * s);
            }

            var distNodes = new double[9 * 4];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    double x = ptCx + (i - 1) * ptSpacing;
                    double y = ptCy + (j - 1) * ptSpacing;
                    var (pr, pc) = PtObserved(x, y);
                    int k = (i * 3 + j) * 4;
                    distNodes[k] = pr;
                    distNodes[k + 1] = pc;
                    distNodes[k + 2] = x;
                    distNodes[k + 3] = y;
                }
            }
            var tMeshDist = Mesh(distNodes, 3);

            var (por, poc) = PtObserved(ptCx + ptSpacing / 2, ptCy + ptSpacing / 2);
            PoseTransformMath.TryMapPixelToMechanical(tMeshDist, por, poc, out double dmx, out double dmy, out _);
            double dMeshErr = Math.Sqrt(
                (dmx - (ptCx + ptSpacing / 2)) * (dmx - (ptCx + ptSpacing / 2))
                + (dmy - (ptCy + ptSpacing / 2)) * (dmy - (ptCy + ptSpacing / 2)));

            var dRows = new double[9]; var dCols = new double[9];
            var dXs = new double[9]; var dYs = new double[9];
            for (int idx = 0; idx < 9; idx++)
            {
                dRows[idx] = distNodes[idx * 4];
                dCols[idx] = distNodes[idx * 4 + 1];
                dXs[idx] = distNodes[idx * 4 + 2];
                dYs[idx] = distNodes[idx * 4 + 3];
            }
            CalibrationMath.TrySolveAffine(dRows, dCols, dXs, dYs, out var dBaseline, out _, out _, out _, out _, out _);
            CalibrationMath.TryMapPixelToXY(dBaseline!, por, poc, out double dax, out double day);
            double dAffineErr = Math.Sqrt(
                (dax - (ptCx + ptSpacing / 2)) * (dax - (ptCx + ptSpacing / 2))
                + (day - (ptCy + ptSpacing / 2)) * (day - (ptCy + ptSpacing / 2)));

            Check("[PT] 畸变网格：格内点误差 ＜ 全局仿射的一半（消费侧确实在吸收畸变）",
                dMeshErr < dAffineErr / 2.0,
                $"网格 {dMeshErr:0.####}mm vs 仿射 {dAffineErr:0.####}mm");

            // ---- 23) 网格失败面：损坏网格 / 网格外 必须明确失败，绝不放行也不外推 ----
            bool meshBrokenRejected = !PoseTransformMath.CheckCalibrationUsable(
                Mesh(new double[20], 3), out string? meshBrokenErr)
                && (meshBrokenErr ?? "").Contains("网格")
                && (meshBrokenErr ?? "").Contains("重新运行「标定」步骤");   // 文案契约：损坏必须给下一步
            bool meshNullRejected = !PoseTransformMath.CheckCalibrationUsable(
                Mesh(null, 3), out string? meshNullErr)
                && (meshNullErr ?? "").Contains("网格")
                && (meshNullErr ?? "").Contains("重新运行「标定」步骤");
            Check("[PT] 网格损坏（长度不符 / 缺失）→ 明确失败、文案含「网格」与「重新运行「标定」步骤」",
                meshBrokenRejected && meshNullRejected,
                meshBrokenErr ?? meshNullErr ?? "（竟然通过）");

            bool meshOutside = !PoseTransformMath.TryMapPixelToMechanical(
                tMeshDist, 5, 5, out _, out _, out string? meshOutsideErr)
                && (meshOutsideErr ?? "").Contains("不在网格覆盖范围内");
            bool meshOutsideInv = !PoseTransformMath.TryMapMechanicalToPixel(
                tMeshDist, 9999, 9999, out _, out _, out string? meshOutsideInvErr)
                && (meshOutsideInvErr ?? "").Contains("不在网格覆盖范围内");
            Check("[PT] 网格外（正向像素 / 反向机械）→ 明确失败（不外推）",
                meshOutside && meshOutsideInv, meshOutsideInvErr ?? meshOutsideErr ?? "（竟然通过）");

            // ---- 24) 插件级：网格标定走真 RunAlgorithm（机械坐标 = 手算、反向回显、Kind 标注） ----
            var meshPlugin = new PoseTransformPlugin();
            try
            {
                meshPlugin.Mode = TransformMode.ToMechanical;
                meshPlugin.Transform.Value = tMeshAffine;
                meshPlugin.PixelPointRow.Value = 200;
                meshPlugin.PixelPointCol.Value = 150;
                meshPlugin.RunAlgorithm(ctx);
                Check("[PT] 插件级网格：机械坐标 = 手算 (103, 54)，Success=true、回显自校验通过",
                    meshPlugin.Success.Value is true
                    && Math.Abs(meshPlugin.MechanicalX.TypedValue - 103) < 1e-9
                    && Math.Abs(meshPlugin.MechanicalY.TypedValue - 54) < 1e-9
                    && Math.Abs(meshPlugin.PixelEchoRow.TypedValue - 200) < 1e-9,
                    $"({meshPlugin.MechanicalX.TypedValue}, {meshPlugin.MechanicalY.TypedValue}) {Err(meshPlugin)}");
            }
            finally
            {
                meshPlugin.Dispose();
            }
        }

        /// <summary>构造"透视"标定（本套断言只关心矩阵语义，MmPerPixel 不参与计算）。</summary>
        private static CalibrationTransform Perspective(double[] hom, int width = 0, int height = 0) => new()
        {
            Kind = CalibrationKind.Perspective,
            MmPerPixel = 0.02,
            ProjectiveMatrix = hom,
            SourceImageWidth = width,
            SourceImageHeight = height,
            SourceTag = "合成",
            CameraSerial = "SYNTH-PT"
        };

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

        /// <summary>构造"网格"标定（分段仿射；节点 [Row,Col,X,Y] 行主序，与契约一致）。</summary>
        private static CalibrationTransform Mesh(double[]? nodes, int n) => new()
        {
            Kind = CalibrationKind.Mesh,
            MmPerPixel = 0.02,
            MeshNodes = nodes,
            MeshSize = n,
            SourceImageWidth = 640,
            SourceImageHeight = 480,
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
