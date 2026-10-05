using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using HalconDotNet;
using Newtonsoft.Json;
using Plugin.Calibration;
using Plugin.Calibration.Models;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 标定插件（Plugin.Calibration）断言。
    ///
    /// 两层：
    ///   · **算法层**（<see cref="CalibrationMath"/>，纯托管）：合成已知仿射 → 求解 → 反算，
    ///     把"精度/互逆/共线守卫/径向带诊断/当量反解"这些数学契约钉死；
    ///   · **插件层**：真跑 <c>RunAlgorithm</c>（真 ExecutionContext），把"成功要产出什么、
    ///     失败必须说什么"这类面向现场的语义钉死——质量闸门、半填行指名、端口面不改名。
    ///
    /// 为什么全部用合成数据：标定的真值无法用真图构造（真图里没有"已知仿射"），
    /// 而合成数据能把误差精确控到 1e-9，这才守得住"公式写反/转置/轴交换"这类静默错误。
    /// </summary>
    internal static class CalibrationChecks
    {
        public static void Run()
        {
            Section("[CAL] 标定插件：仿射/透视求解 / 正反变换 / 质量闸门 / 端口面");

            // 已知矩阵：旋转 30° + 当量 0.02mm/px + 平移（给一个"非平凡"的仿射）
            double mmPerPx = 0.02;
            double phi = 30 * Math.PI / 180;
            var known = new[]
            {
                mmPerPx * Math.Cos(phi), -mmPerPx * Math.Sin(phi),   // a11 a12
                mmPerPx * Math.Sin(phi), mmPerPx * Math.Cos(phi),    // a21 a22
                250.0, -80.0                                          // a31 a32
            };

            // 3×3 图像点（覆盖 1000×800 的视野）
            var imgPts = new (double Row, double Col)[]
            {
                (150, 150), (150, 500), (150, 850),
                (400, 150), (400, 500), (400, 850),
                (650, 150), (650, 500), (650, 850)
            };
            var rows = imgPts.Select(p => p.Row).ToArray();
            var cols = imgPts.Select(p => p.Col).ToArray();
            var xs = imgPts.Select(p => known[0] * p.Row + known[1] * p.Col + known[4]).ToArray();
            var ys = imgPts.Select(p => known[2] * p.Row + known[3] * p.Col + known[5]).ToArray();

            // ---- 1) 已知仿射可精确还原 ----
            bool solved = CalibrationMath.TrySolveAffine(
                rows, cols, xs, ys, out var matrix, out var rms, out var max, out _, out _, out var error);
            Check("[CAL] 已知仿射可精确还原（噪声 0，误差 < 1e-6）",
                solved && matrix != null && MaxAbsDiff(matrix, known) < 1e-6,
                solved ? $"最大系数偏差 {MaxAbsDiff(matrix!, known):0.###e+0}" : error ?? "求解失败");
            Check("[CAL] 无噪声时残差 ≈ 0（< 1e-9）",
                solved && rms < 1e-9 && max < 1e-9,
                $"rms={rms:0.###e+0}, max={max:0.###e+0}");

            // ---- 2) 正变换与手算一致 + 正反互逆 ----
            bool mapped = CalibrationMath.TryMapPixelToXY(matrix!, 321.7, 654.3, out var mx, out var my);
            double expX = known[0] * 321.7 + known[1] * 654.3 + known[4];
            double expY = known[2] * 321.7 + known[3] * 654.3 + known[5];
            Check("[CAL] 正变换与手算一致（< 1e-9）",
                mapped && Math.Abs(mx - expX) < 1e-9 && Math.Abs(my - expY) < 1e-9,
                $"({mx:0.####}, {my:0.####}) vs ({expX:0.####}, {expY:0.####})");

            bool inverse = CalibrationMath.TryMapXYToPixel(matrix!, mx, my, out var ir, out var ic);
            Check("[CAL] 正反变换互逆（< 1e-9）",
                inverse && Math.Abs(ir - 321.7) < 1e-9 && Math.Abs(ic - 654.3) < 1e-9,
                $"({ir:0.######}, {ic:0.######})");

            // ---- 3) 3 点恰好可解 ----
            bool threeOk = CalibrationMath.TrySolveAffine(
                new[] { rows[0], rows[1], rows[3] }, new[] { cols[0], cols[1], cols[3] },
                new[] { xs[0], xs[1], xs[3] }, new[] { ys[0], ys[1], ys[3] },
                out var m3, out var rms3, out _, out _, out _, out var err3);
            Check("[CAL] 3 个非共线点恰好可解（残差≈0）",
                threeOk && m3 != null && rms3 < 1e-9 && MaxAbsDiff(m3, known) < 1e-6,
                threeOk ? $"rms={rms3:0.###e+0}" : err3 ?? "求解失败");

            // ---- 4) 共线点必须失败（防静默定不出方向） ----
            bool collinearOk = CalibrationMath.TrySolveAffine(
                new[] { 100d, 200, 300, 400, 500, 600, 700, 800, 900 },
                new[] { 100d, 200, 300, 400, 500, 600, 700, 800, 900 },
                new[] { 0d, 1, 2, 3, 4, 5, 6, 7, 8 },
                new[] { 0d, 1, 2, 3, 4, 5, 6, 7, 8 },
                out _, out _, out _, out _, out _, out var collinearError);
            Check("[CAL] 共线点必须失败且文案含「共线」",
                !collinearOk && (collinearError ?? "").Contains("共线"),
                collinearError ?? "（竟然通过了）");

            // ---- 5) 径向带诊断：残差随半径增大 → 必须提示畸变 ----
            string radialHint = CalibrationMath.DiagnoseRadialBands(new[] { 0.10, 0.25, 0.90 });
            string flatHint = CalibrationMath.DiagnoseRadialBands(new[] { 0.10, 0.11, 0.12 });
            Check("[CAL] 残差随半径增大 → 提示疑似畸变",
                radialHint.Contains("畸变"), radialHint);
            Check("[CAL] 残差形态正常 → 不给误导性提示",
                flatHint.Length == 0, flatHint.Length == 0 ? "（无提示，正确）" : flatHint);

            // ---- 6) 当量反解与各向异性 ----
            double derived = CalibrationMath.DeriveMmPerPixel(known, out var anisotropy);
            Check("[CAL] 当量反解 ≈ 0.02 mm/px 且各向异性 ≈ 0",
                Math.Abs(derived - mmPerPx) < 1e-9 && anisotropy < 1e-6,
                $"当量={derived:0.######}, 各向异性={anisotropy:0.####}%");

            var squashed = new[] { known[0], known[1], known[2], known[3] * 1.06, known[4], known[5] };
            CalibrationMath.DeriveMmPerPixel(squashed, out var anisotropy2);
            Check("[CAL] 两向当量不一致 → 各向异性可被识别（> 1%）",
                anisotropy2 > 1.0, $"各向异性={anisotropy2:0.###}%");

            // ---- 7) 像素当量 ----
            bool scaleOk = CalibrationMath.TryPixelScale(0, 0, 300, 400, 100, out var mm, out var dist, out var scaleError);
            Check("[CAL] 像素当量：3-4-5 直角三角形（500px / 100mm → 0.2 mm/px）",
                scaleOk && Math.Abs(dist - 500) < 1e-9 && Math.Abs(mm - 0.2) < 1e-9,
                scaleOk ? $"dist={dist:0.###}, mm/px={mm:0.######}" : scaleError ?? "失败");

            bool degenerateOk = CalibrationMath.TryPixelScale(100, 100, 100, 100, 100, out _, out _, out var degenerateError);
            Check("[CAL] 两点重合 → 失败并给出下一步（提示去拖动标记）",
                !degenerateOk && (degenerateError ?? "").Contains("标记"),
                degenerateError ?? "（竟然通过了）");

            // ---- 8) 插件级：九点成功路径（真 RunAlgorithm）----
            var plugin = BuildPlugin(imgPts, xs, ys);

            // 先确认构造期不碰 HALCON（无引擎机器上也要能加载）
            Check("[CAL] 构造后 DisplayImage 为 null（构造期不碰 HALCON 原生库）",
                plugin.DisplayImage == null, "");

            plugin.CameraSerial = "SYNTH-CAM-01";
            var ctx = new ExecutionContext(
                new StubLog(), new FlowSession { FlowName = "标定断言" },
                new WorkspaceContext(), new CancellationTokenSource().Token);

            plugin.RunAlgorithm(ctx);
            var transform = plugin.Transform.Value as CalibrationTransform;
            Check("[CAL] 插件级九点成功：Success=true 且 Transform 端口有值",
                plugin.Success.Value is true && transform != null,
                plugin.ErrorMessage.Value?.ToString() ?? "");
            Check("[CAL] 输出矩阵与已知一致（< 1e-6）",
                transform != null && MaxAbsDiff(transform.Matrix, known) < 1e-6,
                transform == null ? "无输出" : $"最大偏差 {MaxAbsDiff(transform.Matrix, known):0.###e+0}");
            Check("[CAL] 输出当量正确、相机序列号透传、Kind=NinePoint",
                transform != null
                && Math.Abs(transform.MmPerPixel - mmPerPx) < 1e-6
                && transform.CameraSerial == "SYNTH-CAM-01"
                && transform.Kind == CalibrationKind.NinePoint,
                transform == null ? "无输出" : $"mm/px={transform.MmPerPixel:0.######}, cam={transform.CameraSerial}, kind={transform.Kind}");
            Check("[CAL] 端口当量/残差与结果一致",
                plugin.MmPerPixel.Value is double pm && Math.Abs(pm - mmPerPx) < 1e-6
                && plugin.ResidualRmsPx.Value is double pr && pr < 1e-9,
                $"端口 mm/px={plugin.MmPerPixel.Value}, rms={plugin.ResidualRmsPx.Value}");

            // ---- 9) DTO JSON 往返（它要随方案落盘）----
            if (transform != null)
            {
                var json = JsonConvert.SerializeObject(transform);
                var back = JsonConvert.DeserializeObject<CalibrationTransform>(json);
                Check("[CAL] CalibrationTransform JSON 往返字段不丢",
                    back != null
                    && back.Kind == transform.Kind
                    && Math.Abs(back.MmPerPixel - transform.MmPerPixel) < 1e-12
                    && MaxAbsDiff(back.Matrix, transform.Matrix) < 1e-12
                    && Math.Abs(back.ResidualRmsPx - transform.ResidualRmsPx) < 1e-12
                    && back.CameraSerial == transform.CameraSerial
                    && Math.Abs(back.ResidualByRadiusBands[2] - transform.ResidualByRadiusBands[2]) < 1e-12
                    && back.ResidualBandCounts != null && back.ResidualBandCounts.Length == 3,
                    json.Length > 0 ? "" : "序列化为空");
                // NaN 会被 Newtonsoft 写成非标准 JSON——径向带必须全程无 NaN（无点的带=0+计数0）
                Check("[CAL] 径向带无 NaN（无点带=0 + 计数表达，JSON 才跨语言安全）",
                    transform.ResidualByRadiusBands.All(v => !double.IsNaN(v))
                    && transform.ResidualBandCounts.Sum() == 9,
                    $"bands=[{string.Join(",", transform.ResidualByRadiusBands)}] counts=[{string.Join(",", transform.ResidualBandCounts)}]");
            }

            // ---- 10) 质量闸门：残差超阈值 → 失败且文案带残差数字 ----
            // 0.5mm ≈ 25px 偏移（当量 0.02mm/px）→ 必然超 1px 阈值。
            // 这条同时守着 mm→px 单位换算：求解器残差算在机械坐标系（mm），
            // 若忘了按当量除一次，0.5 会被当成 0.5"像素"混过 1px 闸门（静默放行错标定）。
            plugin.PointRows[4].MachineX += 0.5;
            plugin.RunAlgorithm(ctx);
            string residualError = plugin.ErrorMessage.Value?.ToString() ?? "";
            Check("[CAL] 残差超阈值 → 失败且文案带残差与检查清单",
                plugin.Success.Value is false && residualError.Contains("残差") && residualError.Contains("畸变"),
                residualError);

            // ---- 11) 半填行 → 失败并指到行名（防"漏填一格"被静默忽略）----
            // 新语义（0 是合法坐标）："没填" = null —— 半填行造法是把 ImageCol 清空
            plugin.PointRows[4].MachineX -= 0.5;   // 先还原
            plugin.PointRows[2].ImageCol = null;   // P3 变成半填（其余三值仍在）
            plugin.RunAlgorithm(ctx);
            string partialError = plugin.ErrorMessage.Value?.ToString() ?? "";
            Check("[CAL] 半填行 → 失败且指到行名（P3）",
                plugin.Success.Value is false && partialError.Contains("P3") && partialError.Contains("只填了一部分"),
                partialError);

            // ---- 12) 端口面：改名即断下游接线，锁住 ----
            var probe = new CalibrationPlugin();
            var outputs = probe.Outputs.Keys.ToList();
            Check("[CAL] 输出端口齐全（Transform / MmPerPixel / ResidualRmsPx / ResidualMaxPx）",
                new[] { "Transform", "MmPerPixel", "ResidualRmsPx", "ResidualMaxPx" }.All(outputs.Contains),
                string.Join(",", outputs));
            Check("[CAL] Transform 端口类型 = CalibrationTransform（下游按类型接）",
                probe.Outputs["Transform"].DataType == typeof(CalibrationTransform),
                probe.Outputs["Transform"].DataType.Name);
            Check("[CAL] 输入端口 SourceImagePath 存在（配置态取点/预览入口）",
                probe.Inputs.ContainsKey("SourceImagePath"),
                string.Join(",", probe.Inputs.Keys));
            Check("[CAL] 插件带 [Display]（GroupName=标定，否则工具箱里拖不出来）",
                probe.GetType().GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false).Length == 1,
                "");

            // ---- 13) 表格编辑即时刷新质量区（手工填表与拖标记两条路径同体验） ----
            // 病根：PointRows 只挂了集合级事件，逐行的 PropertyChanged 没订阅——
            // 在表格里直接敲机械坐标时质量区与残差列不刷新（要切模式或点「重建网格」才更新）。
            // 断言口径：连改两格，质量读数必须每次都不一样（完全不重算就会两次相同）。
            // 步长取 0.02mm（≈0.3px RMS）：留在 1px 闸门内，保证质量区有读数而不是被判失败清空。
            {
                var live = BuildPlugin(imgPts, xs, ys);
                try
                {
                    live.PointRows[0].MachineX += 0.02;
                    string first = live.QualityText;
                    live.PointRows[0].MachineX += 0.02;
                    string second = live.QualityText;

                    Check("[CAL] 表格改一格 → 质量区立即重算（连续两次改动的读数不同）",
                        first.Length > 0 && !string.Equals(first, second, StringComparison.Ordinal),
                        $"第一次='{first}' 第二次='{second}'");
                }
                finally
                {
                    live.Dispose();
                }
            }

            // ---- 14) 网格缩小遇非空行 → 一行都不删（不留"半新半旧"的表） ----
            {
                var grid = BuildPlugin(imgPts, xs, ys);   // 9 行，P1…P9 全非空
                try
                {
                    int before = grid.PointRows.Count;
                    grid.GridSize = 2;            // 目标 4 行，P5…P9 非空 → 必须整体拒绝
                    grid.RebuildGridCommand.Execute();
                    Check("[CAL] 网格缩小遇非空行 → 不删任何行（表保持 9 行原样）",
                        grid.PointRows.Count == before,
                        $"行数 {before} → {grid.PointRows.Count}");
                    Check("[CAL] 拒绝缩小时给出可操作提示（含能容纳全部行的 3×3 建议）",
                        grid.StatusLevel == StatusLevel.Warning && grid.StatusMessage.Contains("3×3"),
                        grid.StatusMessage);

                    // 非平方行数（模拟"粘贴扩容"到 12 行）：建议值必须向上取整——
                    // 12 行 → 4×4（16 行可容纳）；若用 Round 会给 3×3=9 < 12，照做仍被拒（死循环）。
                    for (int i = 0; i < 3; i++)
                    {
                        grid.PointRows.Add(new CalibPointRow
                        {
                            Name = "P" + (10 + i),
                            MachineX = 50 + i,
                            MachineY = 60 + i,
                            ImageRow = 70 + i,
                            ImageCol = 80 + i
                        });
                    }
                    grid.RebuildGridCommand.Execute();
                    Check("[CAL] 非平方行数被拒缩时建议向上取整（12 行 → 4×4 而非 3×3）",
                        grid.PointRows.Count == 12
                        && grid.StatusLevel == StatusLevel.Warning
                        && grid.StatusMessage.Contains("4×4")
                        && !grid.StatusMessage.Contains("3×3"),
                        $"行数={grid.PointRows.Count} 文案='{grid.StatusMessage}'");
                }
                finally
                {
                    grid.Dispose();
                }
            }

            // ---- 15) 网格规模下拉覆盖 clamp 全范围（2~10），否则旧方案的 6×6 会显示空白 ----
            {
                var probe2 = new CalibrationPlugin();
                Check("[CAL] 网格规模可选值覆盖 2~10（与 OnGridSizeChanging 的 clamp 一致）",
                    probe2.GridSizeOptions.Contains(2) && probe2.GridSizeOptions.Contains(10)
                    && probe2.GridSizeOptions.Length == 9,
                    string.Join(",", probe2.GridSizeOptions));
            }

            // ---- 16) 清空预览路径 → 在途读图不得"复活"（轮次号守卫） ----
            // 病根：EnsurePreviewLoaded 的空路径分支清了图但没递增 _previewLoadId，
            // 在途的 Task 完成后守卫（loadId != _previewLoadId）判断仍成立 → 已清空的预览又出现。
            // 做法：起一张足够大的图拉开"在途"窗口，读到一半清空路径，再等它跑完。
            {
                var race = new CalibrationPlugin { InstanceName = "标定_预览竞态" };
                try
                {
                    string bigPath = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), $"vm_cal_big_{Guid.NewGuid():N}.tif");
                    HOperatorSet.GenImageConst(out HObject proto, "byte", 6000, 6000);
                    HOperatorSet.GenImageProto(proto, out HObject bigImg, 128);
                    HOperatorSet.WriteImage(bigImg, "tiff", 0, bigPath);
                    proto.Dispose();
                    bigImg.Dispose();

                    try
                    {
                        race.SourceImagePath.Value = bigPath;   // 触发后台读盘
                        race.SourceImagePath.Value = string.Empty;   // 立刻清空（读盘还在途中）
                        for (int i = 0; i < 40 && race.DisplayImage != null; i++)
                            Thread.Sleep(50);   // 给在途任务留出完成时间（最多 2s）

                        Check("[CAL] 清空路径后，在途读图不把预览'复活'（DisplayImage 保持空）",
                            race.DisplayImage == null && race.PreviewImagePath.Length == 0,
                            $"DisplayImage={(race.DisplayImage == null ? "空" : "非空")} 路径='{race.PreviewImagePath}'");
                    }
                    finally
                    {
                        try { System.IO.File.Delete(bigPath); } catch { }
                    }
                }
                finally
                {
                    race.Dispose();
                }
            }

            // ---- 17) 0 是合法坐标：机械 X=0 的真实标定点不得被判"半填"或静默剔除 ----
            // 病根：旧模型拿 0 当"没填"的哨兵——现场回零位/工件原点取点（机械 X 或 Y=0）必踩，
            // 要么报"只填了一部分"，要么与图像 0 一起被当空行**静默剔除**（标定悄悄算歪）。
            // 造法：把机械坐标系整体平移，使 P1 的 MachineX 恰好为 0（同一组一致的点，残差仍≈0）
            // ——这样"含 0 的一行没被剔除"就直接体现在 9 点全参与 + 求解成功上。
            {
                var zero = BuildPlugin(imgPts, xs, ys);
                try
                {
                    double shiftX = xs[0];
                    for (int i = 0; i < zero.PointRows.Count; i++)
                        zero.PointRows[i].MachineX = xs[i] - shiftX;   // P1 的 X 恰为 0

                    zero.RunAlgorithm(ctx);
                    var zt = zero.Transform.Value as CalibrationTransform;
                    Check("[CAL] 机械坐标 0 是合法值（不再被判半填/空行，正常求解）",
                        zero.Success.Value is true && zt != null
                        && Math.Abs((zero.PointRows[0].MachineX ?? double.NaN)) < 1e-12,
                        zero.ErrorMessage.Value?.ToString() ?? "");
                    Check("[CAL] 含 0 坐标的解不含该点被剔除的痕迹（9 点全参与）",
                        zt != null && zt.ResidualBandCounts.Sum() == 9 && zt.ResidualRmsPx < 1e-9,
                        zt == null ? "无输出" : $"counts=[{string.Join(",", zt.ResidualBandCounts)}] rms={zt.ResidualRmsPx:0.###e+0}");
                }
                finally
                {
                    zero.Dispose();
                }
            }

            // ---- 18) 旧方案迁移：四值全 0 = 旧版"空行"约定 → 还原成 null（不丢真实点）----
            {
                var legacy = new CalibPointRow { MachineX = 0, MachineY = 0, ImageRow = 0, ImageCol = 0 };
                bool migrated = legacy.NormalizeLegacyAllZero();
                var realZero = new CalibPointRow { MachineX = 0, MachineY = 5, ImageRow = 100, ImageCol = 200 };
                var blank = new CalibPointRow();   // 已是 null 的行：迁移必须是空操作
                Check("[CAL] 旧方案迁移：四值全 0 → 空行（旧版本就把这种行当空行）",
                    migrated && legacy.IsEmpty, $"IsEmpty={legacy.IsEmpty}");
                Check("[CAL] 旧方案迁移：含非 0 值的行不被碰（0 坐标照常保留为有效点）",
                    !realZero.NormalizeLegacyAllZero() && realZero.IsFilled, $"IsFilled={realZero.IsFilled}");
                Check("[CAL] 旧方案迁移：已经是 null 的行是空操作（幂等）",
                    !blank.NormalizeLegacyAllZero() && blank.IsEmpty, "");
            }

            // ---- 19) 表格残差列的 px 口径（0.5mm 偏差点 → 该行残差 ≈22px，不是 0.5"px"）----
            // 病根：ApplyRowResiduals 把机械系偏差（mm）直接写进"残差px"列——
            // 与 TrySolveAffine 已修的那条 P0 同族：单位错一个当量比（0.02），数字看着合理、全错。
            {
                var live = BuildPlugin(imgPts, xs, ys);
                try
                {
                    live.PointRows[4].MachineX += 0.5;   // 与断言 10 同一注入：中心点杠杆 1/9
                    live.Mode = CalibrationMode.NinePoint;   // 钩子 → RefreshStatus（配置态同一份计算）
                    double rowRes = live.PointRows[4].ResidualPx;
                    Check("[CAL] 表格残差列按像素口径（0.5mm ≈ 22px，不是 0.5）",
                        rowRes > 15 && rowRes < 30,
                        $"P5 残差 = {rowRes:0.##}px（期望 ≈ 0.5×8/9/0.02 ≈ 22.2px）");
                    Check("[CAL] 配置态残差回写表格（不用跑一次流程才填）",
                        live.PointRows.Where(r => r.IsFilled).All(r => r.ResidualPx >= 0)
                        && live.PointRows[4].ResidualPx > 0,
                        $"质量区='{live.QualityText}'");
                    Check("[CAL] 闸门未过时质量数字保留（红字显示差多少，不再清空）",
                        live.QualityLevel == StatusLevel.Error && live.QualityText.Contains("残差"),
                        $"Level={live.QualityLevel} 文本='{live.QualityText}'");
                }
                finally
                {
                    live.Dispose();
                }
            }

            // ---- 20) NaN 按行名指名（数学层只知道"第几个点"，行名由插件侧给）----
            {
                var nan = BuildPlugin(imgPts, xs, ys);
                try
                {
                    nan.PointRows[1].MachineX = double.NaN;
                    nan.RunAlgorithm(ctx);
                    string err = nan.ErrorMessage.Value?.ToString() ?? "";
                    Check("[CAL] NaN 指名到行（P2），不再报「第 N 组点」",
                        nan.Success.Value is false && err.Contains("P2") && err.Contains("非法数值"),
                        err);
                }
                finally
                {
                    nan.Dispose();
                }
            }

            // ---- 21) 畸变诊断：带计数判读 + 门限随阈值走 ----
            {
                // 只有中心带有 1 个点（3 点标定常态）：没有计数时会被误读成"中心残差 0 → 边缘大 → 畸变"
                string withCounts = CalibrationMath.DiagnoseRadialBands(
                    new[] { 0.0, 0.0, 0.9 }, new[] { 1, 0, 0 }, 0.5);
                string normalCounts = CalibrationMath.DiagnoseRadialBands(
                    new[] { 0.10, 0.25, 0.90 }, new[] { 1, 4, 4 }, 0.5);
                string relaxed = CalibrationMath.DiagnoseRadialBands(
                    new[] { 0.10, 0.25, 0.60 }, new[] { 1, 4, 4 }, 1.5);   // 阈值放宽到 3px → 门限 1.5px
                Check("[CAL] 空带不误报畸变（计数为 0 的带不参与形态判读）",
                    withCounts.Length == 0, withCounts.Length == 0 ? "（无提示，正确）" : withCounts);
                Check("[CAL] 有计数时照常提示畸变",
                    normalCounts.Contains("畸变"), normalCounts);
                Check("[CAL] 提示门限随残差阈值放宽（阈值 3px → 0.6px 边缘不再提示）",
                    relaxed.Length == 0, relaxed.Length == 0 ? "（无提示，正确）" : relaxed);
            }

            // ---- 22) 标定时间戳：运行期每轮重算不得把它刷成"刚刚" ----
            {
                var stamp = BuildPlugin(imgPts, xs, ys);
                try
                {
                    stamp.RunAlgorithm(ctx);
                    var t1 = ((CalibrationTransform)stamp.Transform.Value!).CreatedAtUtc;
                    Thread.Sleep(20);
                    stamp.RunAlgorithm(ctx);
                    var t2 = ((CalibrationTransform)stamp.Transform.Value!).CreatedAtUtc;
                    Check("[CAL] 连续两次运行 CreatedAtUtc 稳定（签名不变不推进）",
                        t1 == t2, $"{t1:O} → {t2:O}");

                    stamp.PointRows[0].MachineX += 0.01;   // 标定数据真的变了 → 时间戳才推进
                    stamp.RunAlgorithm(ctx);
                    var t3 = ((CalibrationTransform)stamp.Transform.Value!).CreatedAtUtc;
                    Check("[CAL] 修改标定数据后 CreatedAtUtc 前进",
                        t3 > t2, $"{t2:O} → {t3:O}");
                }
                finally
                {
                    stamp.Dispose();
                }
            }

            // ---- 22b) 载入方案后的运行实例首轮：已存标定时间不得被刷成"刚刚" ----
            // 病根（审查发现）：流程编译出的运行实例只走 ApplyConfigValues（不经配置界面 Initialize），
            // 若"首轮必推进"，每次运行都会把方案里载入的标定时间覆盖成运行时刻。
            // 模拟顺序（关键）：先填数据、再设"载入的时间戳"，最后触发首轮计算——
            // 中途不能先跑过一轮（否则数据从无到有本身就会被正确地当成"标定数据变了"）。
            {
                var loaded = new CalibrationPlugin { InstanceName = "标定_载入时间戳" };
                try
                {
                    loaded.PointRows.Clear();
                    for (int i = 0; i < imgPts.Length; i++)
                        loaded.PointRows.Add(new CalibPointRow
                        {
                            Name = "P" + (i + 1),
                            MachineX = xs[i],
                            MachineY = ys[i],
                            ImageRow = imgPts[i].Row,
                            ImageCol = imgPts[i].Col
                        });

                    var stored = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                    loaded.CalibrationStampUtc = stored;      // 模拟"从方案载入的标定时间"
                    loaded.Mode = CalibrationMode.NinePoint;  // 首轮计算（只记签名，不推进）

                    loaded.RunAlgorithm(ctx);                 // 运行实例正式运行
                    var first = ((CalibrationTransform)loaded.Transform.Value!).CreatedAtUtc;
                    Check("[CAL] 载入的标定时间在运行首轮保持不变（首轮只记签名）",
                        first == stored, $"stored={stored:O} → 输出={first:O}");
                }
                finally
                {
                    loaded.Dispose();
                }
            }

            // ---- 23) 像素当量：A/B 未取全必须明确失败（null 与 0 区分；不再拿 (0,0) 悄悄算）----
            {
                var scale = new CalibrationPlugin { InstanceName = "标定_像素当量" };
                try
                {
                    scale.Mode = CalibrationMode.PixelScale;
                    scale.RunAlgorithm(ctx);
                    string err = scale.ErrorMessage.Value?.ToString() ?? "";
                    Check("[CAL] A/B 未取全 → 明确失败并给下一步（不再误用 (0,0)）",
                        scale.Success.Value is false && err.Contains("取全"),
                        err);

                    // 跨度提示：短基线警告（方案 §五① 承诺的"跨越视野百分比"）
                    scale.SourceImageWidth = 1000;
                    scale.SourceImageHeight = 800;
                    scale.ScaleARow = 100; scale.ScaleACol = 100;
                    scale.ScaleBRow = 150; scale.ScaleBCol = 120;   // 距离 ≈54px → 占对角 ≈4%
                    Check("[CAL] 短标定线给出跨度警告（<1/3 视野）",
                        scale.ScaleSpanLevel == StatusLevel.Warning && scale.ScaleSpanText.Contains("偏短"),
                        scale.ScaleSpanText);
                    scale.ScaleBRow = 300; scale.ScaleBCol = 700;   // 距离 ≈632px → ≈49%
                    Check("[CAL] 跨度充足时不警告",
                        scale.ScaleSpanLevel == StatusLevel.Info && scale.ScaleSpanText.Contains("充足"),
                        scale.ScaleSpanText);

                    scale.RunAlgorithm(ctx);   // 取齐 A/B 后必须能成功（上一次失败是"未取全"）
                    var st = scale.Transform.Value as CalibrationTransform;
                    Check("[CAL] 像素当量取齐 A/B 后成功，且 Kind=PixelScale",
                        scale.Success.Value is true
                        && st != null
                        && st.Kind == CalibrationKind.PixelScale
                        && st.Matrix[0] > 0 && st.Matrix[1] == 0 && st.Matrix[2] == 0 && st.Matrix[3] > 0,
                        scale.ErrorMessage.Value?.ToString() ?? "");
                }
                finally
                {
                    scale.Dispose();
                }
            }

            // ---- 24) 锁定：锁定后编辑被拒、解锁恢复；取点待命受锁定与无图双重守卫 ----
            {
                var locked = BuildPlugin(imgPts, xs, ys);
                try
                {
                    locked.IsCalibrationLocked = true;
                    Check("[CAL] 锁定后 IsEditable=false",
                        locked.IsEditable == false, "");
                    locked.PickRowPointCommand.Execute(locked.PointRows[0]);
                    Check("[CAL] 锁定态取点被拒（文案含「锁定」）",
                        locked.StatusMessage.Contains("锁定"), locked.StatusMessage);

                    locked.ClearRowsCommand.Execute();
                    Check("[CAL] 锁定态清空表格被拒（数据原样保留）",
                        locked.PointRows[0].IsFilled && locked.StatusMessage.Contains("锁定"),
                        locked.StatusMessage);

                    locked.IsCalibrationLocked = false;
                    locked.PickRowPointCommand.Execute(locked.PointRows[0]);
                    Check("[CAL] 未锁定但无标定图 → 取点提示先载图",
                        locked.StatusMessage.Contains("标定图"), locked.StatusMessage);
                }
                finally
                {
                    locked.Dispose();
                }
            }

            // ---- 25) 共享画布取点通道：opt-in 且默认关（其他插件零行为变化） ----
            {
                var dp = Core.Halcon.Controls.HalconBase.IsPickModeProperty;
                bool defaultOff = (bool)dp.GetMetadata(typeof(Core.Halcon.Controls.HalconBase)).DefaultValue!;
                bool eventExists = typeof(Core.Halcon.Controls.HalconBase)
                    .GetEvent("ImagePicked") != null;
                Check("[CAL] HalconBase.IsPickMode 默认 false（加法不改变其他插件行为）",
                    !defaultOff, $"默认值={defaultOff}");
                Check("[CAL] HalconBase.ImagePicked 事件存在（一次性取点通道）",
                    eventExists, "");
            }

            // ---- 26) 配置面：新 [StepConfig] 键随步骤落盘（加法，不破旧方案） ----
            {
                var probe3 = new CalibrationPlugin();
                var scaleAProp = probe3.GetType().GetProperty("ScaleARow");
                Check("[CAL] 配置面新增键存在（CalibrationStampUtc / IsCalibrationLocked / 可空 A/B）",
                    probe3.GetType().GetProperty("CalibrationStampUtc") != null
                    && probe3.GetType().GetProperty("IsCalibrationLocked") != null
                    && scaleAProp != null
                    && scaleAProp.PropertyType == typeof(double?),
                    $"ScaleARow 类型={scaleAProp?.PropertyType.Name ?? "(缺失)"}");
            }

            // ================= 二期① 透视标定（断言 58~71） =================
            // 透视真值：倾斜平面（含梯形项 h31/h32），0.02mm/px 量级；沿用同一 3×3 网格
            var pH = new[] { 0.0196, -0.0032, 24.0, 0.0034, 0.0199, -80.0, 8.0e-5, 3.2e-5, 1.0 };
            var pRows = rows;
            var pCols = cols;
            MapAllProj(pH, pRows, pCols, out var pXs, out var pYs);

            // ---- 58) 倾斜真值精确还原（含梯形项，h33 归一后 9 元逐位 < 1e-6） ----
            bool pSolved = CalibrationMath.TrySolveProjective(
                pRows, pCols, pXs, pYs,
                out var pHom, out var pRms, out var pMax, out var pBands, out var pCounts, out var pErr);
            Check("[CAL] 透视：倾斜真值精确还原（h33 归一后 9 元逐位 < 1e-6）",
                pSolved && pHom != null && MaxAbsDiff(pHom!, pH) < 1e-6,
                pSolved ? $"最大系数偏差 {MaxAbsDiff(pHom!, pH):0.###e+0}" : pErr ?? "求解失败");

            // ---- 59) 无噪声残差 < 1e-9；正/反投影互逆 < 1e-9 ----
            double pMx = 0, pMy = 0, pIr = 0, pIc = 0;
            bool pFwd = pSolved && CalibrationMath.TryMapPixelToXYProj(pHom!, 321.7, 654.3, out pMx, out pMy, out _);
            var (pEx, pEy) = MapProj(pH, 321.7, 654.3);
            bool pInv = pSolved && CalibrationMath.TryMapXYToPixelProj(pHom!, pEx, pEy, out pIr, out pIc, out _);
            Check("[CAL] 透视：无噪声残差 < 1e-9 且正/反投影互逆 < 1e-9",
                pSolved && pRms < 1e-9 && pMax < 1e-9
                && pFwd && Math.Abs(pMx - pEx) < 1e-9 && Math.Abs(pMy - pEy) < 1e-9
                && pInv && Math.Abs(pIr - 321.7) < 1e-9 && Math.Abs(pIc - 654.3) < 1e-9,
                $"rms={pRms:0.###e+0}, max={pMax:0.###e+0}, 互逆偏差=({Math.Abs(pIr - 321.7):0.###e+0},{Math.Abs(pIc - 654.3):0.###e+0})");

            // ---- 60) 3 点 / 近共线 4 点 / 重复 4 点 → 失败且文案可分辨 ----
            bool pThree = CalibrationMath.TrySolveProjective(
                new[] { pRows[0], pRows[1], pRows[2] }, new[] { pCols[0], pCols[1], pCols[2] },
                new[] { pXs[0], pXs[1], pXs[2] }, new[] { pYs[0], pYs[1], pYs[2] },
                out _, out _, out _, out _, out _, out var pThreeErr);
            var nRows = new[] { 100d, 400, 700, 900 };
            var nCols = new[] { 100d, 400, 700, 900.01 };   // 四点近乎共线（第 4 点偏离 0.01px）
            MapAllProj(pH, nRows, nCols, out var nXs, out var nYs);
            bool pNear = CalibrationMath.TrySolveProjective(
                nRows, nCols, nXs, nYs, out _, out _, out _, out _, out _, out var pNearErr);
            var dRows = new[] { 100d, 100, 100, 100 };
            var dCols = new[] { 100d, 100, 100, 100 };      // 四点全重合
            bool pDup = CalibrationMath.TrySolveProjective(
                dRows, dCols, new[] { 1d, 2, 3, 4 }, new[] { 1d, 2, 3, 4 },
                out _, out _, out _, out _, out _, out var pDupErr);
            Check("[CAL] 透视：3 点失败（至少需要 4 组）/ 近共线 4 点、重复 4 点失败（退化）",
                !pThree && (pThreeErr ?? "").Contains("至少需要 4 组")
                && !pNear && (pNearErr ?? "").Contains("退化")
                && !pDup && (pDupErr ?? "").Contains("退化"),
                $"3点='{TrimErr(pThreeErr)}'；近共线='{TrimErr(pNearErr)}'；重复='{TrimErr(pDupErr)}'");

            // ---- 61) 价值反证：同一倾斜数据，仿射残差超阈值 且 投影残差达标 ----
            bool pAffOk = CalibrationMath.TrySolveAffine(
                pRows, pCols, pXs, pYs, out _, out var pAffRms, out _, out _, out _, out _);
            Check("[CAL] 透视价值反证：仿射残差超阈值（≈6px）且投影模型残差达标（< 1e-9）",
                pAffOk && pAffRms > 2.0 && pSolved && pRms < 1e-9,
                $"仿射 rms={pAffRms:0.###}px（阀门阈值 1px）、投影 rms={pRms:0.###e+0}px");

            // ---- 62) 单位反证：注入单点 mm 偏差 → 该行残差（px）= mm ÷ 局部当量（≠ mm ÷ 中心当量） ----
            {
                var pv = BuildPerspectivePlugin(pRows, pCols, pXs, pYs);
                try
                {
                    pv.SourceImageWidth = 1000;
                    pv.SourceImageHeight = 800;
                    pv.PointRows[0].MachineX = pXs[0] + 0.01;   // 0.01mm ≈ 0.5px @P1 局部当量——留在 1px 闸门内
                    pv.RunAlgorithm(ctx);
                    var t = pv.Transform.Value as CalibrationTransform;
                    double rowPx = pv.PointRows[0].ResidualPx;
                    double expByLocal = double.NaN, expByCenter = double.NaN;
                    if (t != null)
                        TryPerspectiveResiduals(t, pRows[0], pCols[0], pXs[0] + 0.01, pYs[0], out expByLocal, out expByCenter);
                    Check("[CAL] 透视单位反证：该行残差（px）= mm ÷ 局部当量（≠ 中心当量，差 >2.5%）",
                        pv.Success.Value is true && t != null
                        && Math.Abs(rowPx - expByLocal) < 1e-9
                        && Math.Abs(rowPx - expByCenter) > 0.025 * Math.Abs(rowPx),
                        $"行残差={rowPx:0.####}px；局部口径={expByLocal:0.####}，中心当量口径={expByCenter:0.####}");
                }
                finally
                {
                    pv.Dispose();
                }
            }

            // ---- 63) 中心局部当量 = 中心差分独立算值；各向异性可识别；退化护栏 >0 生效 ----
            {
                var (fdLr, fdLc) = JacobianByCentralDiff(pH, 400, 500, 1e-4);
                double fdLocal = (fdLr + fdLc) / 2;
                double fdAniso = Math.Abs(fdLr - fdLc) / ((fdLr + fdLc) / 2) * 100;
                double hLocal = CalibrationMath.DeriveMmPerPixelProjective(pH, 400, 500, out var hAniso);
                var pH2 = new[] { 0.02, 0.004, 30.0, -0.002, 0.016, -60.0, 3.0e-6, 1.5e-6, 1.0 };
                double h2Local = CalibrationMath.DeriveMmPerPixelProjective(pH2, 400, 500, out var h2Aniso);
                var hLine = new[] { 1.0, 0, 0, 0, 1, 0, 1, 0, -100.0 };
                double lineLocal = CalibrationMath.DeriveMmPerPixelProjective(hLine, 100, 500, out _);   // w=0 线上
                Check("[CAL] 透视局部当量：中心值=中心差分独立算值（<1e-8）、各向异性可识别（>5%）、退化护栏>0",
                    Math.Abs(hLocal - fdLocal) < 1e-8
                    && Math.Abs(hAniso - fdAniso) < 1e-4
                    && hAniso > 5.0 && h2Local > 0 && h2Aniso > 5.0
                    && lineLocal == 0,
                    $"中心局部={hLocal:0.######}（差分 {fdLocal:0.######}；aniso {hAniso:0.####}% vs 差分 {fdAniso:0.####}%）；H2 aniso={h2Aniso:0.###}%；退化线点当量={lineLocal}");
            }

            // ---- 64) 跨模型一致：仿射数据上 投影解 = 仿射解重排（锁两套公式不漂移） ----
            bool pOnAffine = CalibrationMath.TrySolveProjective(
                rows, cols, xs, ys, out var pHomAff, out _, out _, out _, out _, out var pAffErr2);
            var expected9 = new[] { matrix![0], matrix![1], matrix![4], matrix![2], matrix![3], matrix![5], 0d, 0d, 1d };
            Check("[CAL] 透视跨模型一致：仿射数据上 与 TrySolveAffine 输出重排后逐位 < 1e-9",
                pOnAffine && pHomAff != null && MaxAbsDiff(pHomAff!, expected9) < 1e-9,
                pOnAffine ? $"最大偏差 {MaxAbsDiff(pHomAff!, expected9):0.###e+0}" : pAffErr2 ?? "求解失败");

            // ---- 65) JSON：ProjectiveMatrix 往返逐位一致、h33=1、无 NaN/Inf；旧 JSON 读入行为不变 ----
            {
                var pt = new CalibrationTransform
                {
                    Kind = CalibrationKind.Perspective,
                    MmPerPixel = 0.0199,
                    ProjectiveMatrix = (double[])pHom!.Clone(),
                    SourceImageWidth = 1000,
                    SourceImageHeight = 800,
                    CameraSerial = "SYNTH-CAM-PJ"
                };
                var pJson = JsonConvert.SerializeObject(pt);
                var pBack = JsonConvert.DeserializeObject<CalibrationTransform>(pJson);
                double roundDiff = pBack?.ProjectiveMatrix != null
                    ? MaxAbsDiff(pBack.ProjectiveMatrix, pt.ProjectiveMatrix)
                    : double.PositiveInfinity;
                bool noBad = pt.ProjectiveMatrix!.All(v => !double.IsNaN(v) && !double.IsInfinity(v))
                             && pt.ProjectiveMatrix[8] == 1.0;

                const string legacyJson = "{\"Kind\":1,\"MmPerPixel\":0.02,\"Matrix\":[0.0196,-0.0032,0.0034,0.0199,24,-80],\"CameraSerial\":\"OLD\"}";
                var legacy = JsonConvert.DeserializeObject<CalibrationTransform>(legacyJson);
                bool legacyOk = legacy != null && legacy.ProjectiveMatrix == null
                    && legacy.Kind == CalibrationKind.NinePoint
                    && Math.Abs(legacy.MmPerPixel - 0.02) < 1e-12
                    && Math.Abs(legacy.Matrix[4] - 24) < 1e-12;
                Check("[CAL] 透视 JSON：ProjectiveMatrix 往返逐位一致、h33=1、无 NaN/Inf；旧 JSON 读入行为不变",
                    roundDiff == 0 && noBad && legacyOk,
                    $"往返偏差={roundDiff:0.###e+0}；h33={pt.ProjectiveMatrix[8]}；旧 JSON: Kind={legacy?.Kind}、新字段={(legacy?.ProjectiveMatrix == null ? "null（正确）" : "非空（错）")}");
            }

            // ---- 66) 透视拒绝面（第一阶段）：矩阵缺失/退化被拒 + 旧口径（零 Matrix）被拒 ----
            // 注："未知 Kind=(CalibrationKind)7 → 拒绝"属消费侧（坐标变换插件，第二阶段）联动语义——
            //     按施工要求标注留待第二阶段，不在此弱化处理。
            {
                bool missRejected = !CalibrationMath.IsUsableProjective(null)
                                    && !CalibrationMath.IsUsableProjective(new double[8])
                                    && !CalibrationMath.IsUsableProjective(new[] { 1.0, 2, 3, 4, 5, 6, 7, 8, double.NaN });
                bool degRejected = !CalibrationMath.IsUsableProjective(new double[9]);   // det=0
                // 透视产物的旧 6 元 Matrix 保持零 → 旧口径消费者（IsUsableMatrix）明确失败（不静默近似）
                bool oldPathRejected = !CalibrationMath.IsUsableMatrix(new double[6]);
                Check("[CAL] 透视拒绝面：缺失/退化投影矩阵被拒 + 旧口径（零 Matrix）被拒（未知 Kind=7 的消费侧联动留待第二阶段）",
                    missRejected && degRejected && oldPathRejected, "");
            }

            // ---- 67) 插件级透视全链路：Success/Kind/端口/相机尺寸透传/当量=中心局部/表格残差 ----
            {
                var pv2 = BuildPerspectivePlugin(pRows, pCols, pXs, pYs);
                try
                {
                    pv2.CameraSerial = "SYNTH-CAM-PJ";
                    pv2.SourceImageWidth = 1000;
                    pv2.SourceImageHeight = 800;

                    // 第一段：干净数据——产物与真值一致（< 1e-6）、当量=中心局部、端口/相机/尺寸透传
                    pv2.RunAlgorithm(ctx);
                    var t2 = pv2.Transform.Value as CalibrationTransform;
                    bool ports = pv2.MmPerPixel.Value is double pmv && t2 != null && Math.Abs(pmv - t2.MmPerPixel) < 1e-12
                                 && pv2.ResidualRmsPx.Value is double prr && t2 != null && Math.Abs(prr - t2.ResidualRmsPx) < 1e-12
                                 && pv2.ResidualMaxPx.Value is double prm && t2 != null && Math.Abs(prm - t2.MaxResidualPx) < 1e-12;
                    bool clean = pv2.Success.Value is true && t2 != null
                        && t2.Kind == CalibrationKind.Perspective
                        && t2.ProjectiveMatrix != null && MaxAbsDiff(t2.ProjectiveMatrix!, pH) < 1e-6
                        && Math.Abs(t2.MmPerPixel - CalibrationMath.DeriveMmPerPixelProjective(t2.ProjectiveMatrix!, 400, 500, out _)) < 1e-9
                        && Math.Abs(t2.MmPerPixel - 0.019900811457) < 1e-6
                        && t2.CameraSerial == "SYNTH-CAM-PJ"
                        && t2.SourceImageWidth == 1000 && t2.SourceImageHeight == 800
                        && ports;

                    // 第二段：注入单点 mm 偏差——表格残差必须等于"mm ÷ 该点局部当量"的复算值（局部口径）
                    pv2.PointRows[0].MachineX = pXs[0] + 0.01;
                    pv2.RunAlgorithm(ctx);
                    var t2b = pv2.Transform.Value as CalibrationTransform;
                    double rowPx2 = pv2.PointRows[0].ResidualPx;
                    double expPx = double.NaN;
                    bool rez = t2b != null && TryPerspectiveResiduals(t2b, pRows[0], pCols[0], pXs[0] + 0.01, pYs[0], out expPx, out _);

                    Check("[CAL] 透视插件全链路：Success/Kind/端口/相机与尺寸透传/当量=中心局部/表格残差=局部口径",
                        clean && pv2.Success.Value is true && rez && Math.Abs(rowPx2 - expPx) < 1e-9,
                        $"rms={t2b?.ResidualRmsPx:0.###e+0}，当量={t2?.MmPerPixel:0.######}，行残差={rowPx2:0.####}（局部复算 {expPx:0.####}），端口={ports}");
                }
                finally
                {
                    pv2.Dispose();
                }
            }

            // ---- 68) 签名/时间戳：切换 NinePoint↔Perspective 推进；同数据重复运行不推进 ----
            {
                var st2 = BuildPerspectivePlugin(pRows, pCols, pXs, pYs);
                try
                {
                    st2.RunAlgorithm(ctx);
                    var s1 = st2.CalibrationStampUtc;
                    Thread.Sleep(20);
                    st2.RunAlgorithm(ctx);
                    var s2 = st2.CalibrationStampUtc;
                    Thread.Sleep(20);
                    st2.Mode = CalibrationMode.NinePoint;
                    st2.RunAlgorithm(ctx);
                    var s3 = st2.CalibrationStampUtc;
                    Thread.Sleep(20);
                    st2.Mode = CalibrationMode.Perspective;
                    st2.RunAlgorithm(ctx);
                    var s4 = st2.CalibrationStampUtc;
                    Check("[CAL] 透视签名/时间戳：切换 NinePoint↔Perspective 推进；同数据重复运行不推进",
                        s1 == s2 && s3 > s2 && s4 > s3,
                        $"{s1:HH:mm:ss.fff} → {s2:HH:mm:ss.fff} → {s3:HH:mm:ss.fff} → {s4:HH:mm:ss.fff}");
                }
                finally
                {
                    st2.Dispose();
                }
            }

            // ---- 69) UI 面：ShowPointTable 真值表（含通知）、状态含「透视」、端口面 1 入 4 出不变 ----
            {
                var ui = new CalibrationPlugin();
                var names2 = new List<string?>();
                ((System.ComponentModel.INotifyPropertyChanged)ui).PropertyChanged += (_, e) => names2.Add(e.PropertyName);
                ui.Mode = CalibrationMode.NinePoint;
                bool show9 = ui.ShowPointTable;
                ui.Mode = CalibrationMode.Perspective;
                bool showPj = ui.ShowPointTable;
                ui.Mode = CalibrationMode.PixelScale;
                bool showPs = ui.ShowPointTable;
                bool notified = names2.Contains("ShowPointTable");

                var ui2 = BuildPerspectivePlugin(pRows, pCols, pXs, pYs);
                string uiStatus;
                try
                {
                    // 构建后第一次刷新发生在"空表"时刻（状态文案还是老的）——切一次模式补一次带数据的刷新
                    ui2.Mode = CalibrationMode.PixelScale;
                    ui2.Mode = CalibrationMode.Perspective;
                    ui2.RunAlgorithm(ctx);
                    uiStatus = ui2.StatusMessage;
                }
                finally
                {
                    ui2.Dispose();
                }

                // 端口面钉住：声明端口 5 入 / 4 出（含并行新增的取点输入），基类另有 Success/ErrorMessage 两输出（4+2=6）
                var probePj = new CalibrationPlugin();
                var expectedIns = new[] { "SourceImagePath", "SourceImage", "SourceSerial", "SourcePointRow", "SourcePointCol" };
                var expectedOuts = new[] { "Transform", "MmPerPixel", "ResidualRmsPx", "ResidualMaxPx" };
                var declaredIns = typeof(CalibrationPlugin).GetProperties()
                    .Where(p => typeof(IInputPort).IsAssignableFrom(p.PropertyType) && p.DeclaringType == typeof(CalibrationPlugin))
                    .Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                var declaredOuts = typeof(CalibrationPlugin).GetProperties()
                    .Where(p => typeof(IOutputPort).IsAssignableFrom(p.PropertyType) && p.DeclaringType == typeof(CalibrationPlugin))
                    .Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                bool portFace = declaredIns.SequenceEqual(expectedIns.OrderBy(x => x, StringComparer.Ordinal))
                    && declaredOuts.SequenceEqual(expectedOuts.OrderBy(x => x, StringComparer.Ordinal))
                    && probePj.Inputs.Count == expectedIns.Length
                    && probePj.Outputs.Count == expectedOuts.Length + 2;
                Check("[CAL] 透视 UI 面：ShowPointTable 真值表（含通知）、状态含「透视」、端口面 5 入 4 出（+基类 2 输出）不变",
                    show9 && showPj && !showPs && notified && uiStatus.Contains("透视") && portFace,
                    $"ShowPointTable=(像素:{showPs}/九点:{show9}/透视:{showPj})，通知={notified}，状态='{uiStatus}'，端口={string.Join("/", declaredIns)} | {string.Join("/", declaredOuts)}");
            }

            // ---- 70) HALCON 对拍：vector_to_proj_hom_mat2d（'normalized_dlt'）h33 归一后 < 1e-6 ----
            {
                HOperatorSet.VectorToProjHomMat2d(
                    new HTuple(pRows), new HTuple(pCols), new HTuple(pXs), new HTuple(pYs), "normalized_dlt",
                    new HTuple(), new HTuple(), new HTuple(), new HTuple(), new HTuple(), new HTuple(),
                    out HTuple hcHom, out _);
                var hcArr = new double[9];
                for (int i = 0; i < 9; i++) hcArr[i] = hcHom[i].D;
                double hcDiff = pSolved && pHom != null
                    ? MaxAbsDiff(NormalizeH33(pHom!), NormalizeH33(hcArr))
                    : double.NaN;
                Check("[CAL] 透视 HALCON 对拍（vector_to_proj_hom_mat2d / normalized_dlt）：h33 归一后逐元 < 1e-6",
                    pSolved && hcDiff < 1e-6, $"maxdiff={hcDiff:0.###e+0}");
            }

            // ---- 71) 守卫余项：h33≈0 拒绝 / 映射折返（雅可比符号不一致）拒绝 / 退化线点拒绝 ----
            {
                // (a) 真值 h33=0 的构造：解退化 → 拒绝且文案含 h33
                var h33zero = new[] { 1.0, 2, 3, 4, 5, 6, 7, 8, 0.0 };
                MapAllProj(h33zero, pRows, pCols, out var zXs, out var zYs);
                bool zRejected = !CalibrationMath.TrySolveProjective(
                    pRows, pCols, zXs, zYs, out _, out _, out _, out _, out _, out var zErr)
                    && (zErr ?? "").Contains("h33");

                // (b) 折返：w 跨越 0（detJ = det(H)/w³ 符号不一致）→ 拒绝且文案含「折返」
                var hFold = new[] { 1.0, 0, 0, 0, 1, 0, 1, 0, -500.0 };
                var fRows = new[] { 100d, 100, 300, 700, 900, 900 };
                var fCols = new[] { 100d, 900, 500, 500, 100, 900 };
                MapAllProj(hFold, fRows, fCols, out var fXs, out var fYs);
                bool fRejected = !CalibrationMath.TrySolveProjective(
                    fRows, fCols, fXs, fYs, out _, out _, out _, out _, out _, out var fErr)
                    && (fErr ?? "").Contains("折返");

                // (c) 映射退化线守卫：w=0 上的点必须拒绝
                var hLine2 = new[] { 1.0, 0, 0, 0, 1, 0, 1, 0, -100.0 };
                bool mapRejected = !CalibrationMath.TryMapPixelToXYProj(hLine2, 100, 500, out _, out _, out var mErr)
                    && (mErr ?? "").Contains("退化线");

                Check("[CAL] 透视守卫余项：h33≈0 拒绝 / 映射折返拒绝 / 退化线点拒绝",
                    zRejected && fRejected && mapRejected,
                    $"h33='{TrimErr(zErr)}'；折返='{TrimErr(fErr)}'；退化线='{TrimErr(mErr)}'");
            }

            // ---- 72) 标定文件导入/导出：往返保真（0 vs null）、快照、拒绝面、求解与时间戳 ----
            {
                // (a) 往返保真：0 必须还是 0（不是 null）、null 必须还是 null
                var quirkSrc = BuildPlugin(imgPts, xs, ys);
                var quirkDst = new CalibrationPlugin { InstanceName = "标定_导入保真" };
                try
                {
                    quirkSrc.CameraSerial = "SYNTH-EXPORT-01";
                    quirkSrc.PointRows[0].MachineX = 0;      // 合法 0：往返后不能被"迁移"成 null
                    quirkSrc.PointRows[1].ImageCol = null;   // 未填：往返后不能变成 0
                    string jsonQuirk = quirkSrc.BuildCalibrationExportJson();
                    var errQuirk = quirkDst.ImportCalibrationJson(jsonQuirk);
                    Check("[CAL] 导出/导入往返：0 与 null 的区分不丢、序列号逐项一致",
                        errQuirk.Length == 0
                        && quirkDst.Mode == CalibrationMode.NinePoint
                        && quirkDst.CameraSerial == "SYNTH-EXPORT-01"
                        && quirkDst.PointRows.Count == quirkSrc.PointRows.Count
                        && (quirkDst.PointRows[0].MachineX ?? -1) == 0
                        && quirkDst.PointRows[1].ImageCol is null
                        && (quirkDst.PointRows[2].MachineX ?? -1) == (quirkSrc.PointRows[2].MachineX ?? -1),
                        errQuirk.Length > 0 ? errQuirk : $"rows={quirkDst.PointRows.Count}");
                }
                finally
                {
                    quirkSrc.Dispose();
                    quirkDst.Dispose();
                }

                // (b) 可解配置：导出带质量快照；导入后求解成功、时间戳以文件为准（不被导入动作推进）
                var clean = BuildPlugin(imgPts, xs, ys);
                var dst = new CalibrationPlugin { InstanceName = "标定_导入求解" };
                try
                {
                    clean.CameraSerial = "SYNTH-EXPORT-01";
                    string jsonClean = clean.BuildCalibrationExportJson();
                    var stampClean = clean.CalibrationStampUtc;
                    Check("[CAL] 导出：可解配置带质量快照（Snapshot）与版本号",
                        jsonClean.Contains("\"Snapshot\"") && jsonClean.Contains("\"Version\": 1"),
                        "");

                    var errClean = dst.ImportCalibrationJson(jsonClean);
                    dst.RunAlgorithm(ctx);
                    var tDst = dst.Transform.Value as CalibrationTransform;
                    Check("[CAL] 导入后：按文件数据求解成功、时间戳以文件为准（首轮不推进）",
                        errClean.Length == 0 && dst.Success.Value is true && tDst != null
                        && tDst.CreatedAtUtc == stampClean
                        && tDst.CameraSerial == "SYNTH-EXPORT-01",
                        errClean.Length > 0 ? errClean
                            : $"stamp={stampClean:O} 输出={(tDst?.CreatedAtUtc.ToString("O") ?? "无")}");
                }
                finally
                {
                    clean.Dispose();
                    dst.Dispose();
                }

                // (c) 拒绝面：空内容 / 非法 JSON / 更高版本 / 锁定态
                var rej = new CalibrationPlugin { InstanceName = "标定_导入拒绝" };
                try
                {
                    bool emptyRejected = rej.ImportCalibrationJson("").Length > 0;
                    bool badJsonRejected = rej.ImportCalibrationJson("{ not json").Length > 0;
                    bool versionRejected = rej.ImportCalibrationJson("{\"Version\":99,\"Points\":[]}").Length > 0;
                    rej.IsCalibrationLocked = true;
                    bool lockedRejected = rej.ImportCalibrationJson("{\"Version\":1,\"Points\":[]}").Contains("锁定");
                    rej.IsCalibrationLocked = false;
                    Check("[CAL] 导入拒绝面：空内容 / 非法 JSON / 更高版本 / 锁定态",
                        emptyRejected && badJsonRejected && versionRejected && lockedRejected,
                        $"empty={emptyRejected} badJson={badJsonRejected} version={versionRejected} locked={lockedRejected}");
                }
                finally
                {
                    rej.Dispose();
                }
            }

            // ---- 73) 在线取点：实时图优先接管画布；断开后尺寸记录保留（快照不因"图片不在场"作废）----
            {
                var p = BuildPlugin(imgPts, xs, ys);
                HOperatorSet.GenImageConst(out HObject proto, "byte", 320, 240);
                var online = new HImage(proto);
                proto.Dispose();
                try
                {
                    p.SourceImage.Value = online;   // 触发 RefreshOnlineImage
                    Check("[CAL] 实时图接入：画布底图切换为实时图且尺寸记录更新",
                        ReferenceEquals(p.CanvasImage, online)
                        && p.SourceImageWidth == 320 && p.SourceImageHeight == 240,
                        $"canvas={(ReferenceEquals(p.CanvasImage, online) ? "实时图" : "其它")} {p.SourceImageWidth}×{p.SourceImageHeight}");

                    p.SourceImage.Value = null;     // 断开
                    Check("[CAL] 实时图断开：画布回落但尺寸记录保留（快照不被误清）",
                        p.CanvasImage == null && p.SourceImageWidth == 320 && p.SourceImageHeight == 240,
                        $"{p.SourceImageWidth}×{p.SourceImageHeight}");
                }
                finally
                {
                    online.Dispose();
                    p.Dispose();
                }

                // 无文件预览的标定"打开"（空路径分支）：尺寸记录不得被误清（旧病回归）
                var keep = new CalibrationPlugin { InstanceName = "标定_尺寸保留" };
                try
                {
                    keep.SourceImageWidth = 1280;
                    keep.SourceImageHeight = 1024;
                    keep.SourceImagePath.Value = "   ";   // 触发空路径分支
                    Check("[CAL] 空路径且从未载过图：尺寸记录保留（修复“打开即清尺寸”）",
                        keep.SourceImageWidth == 1280 && keep.SourceImageHeight == 1024,
                        $"{keep.SourceImageWidth}×{keep.SourceImageHeight}");
                }
                finally
                {
                    keep.Dispose();
                }
            }

            // ---- 74) 预填：上游定位点回填 & 缺值时明确拒绝且不动数据 ----
            {
                var p = BuildPlugin(imgPts, xs, ys);
                try
                {
                    var row = p.PointRows[0];
                    row.ImageRow = null;
                    row.ImageCol = null;
                    p.SourcePointRow.Value = 111.5;
                    p.SourcePointCol.Value = 222.5;
                    p.PrefillRowFromSourceCommand.Execute(row);
                    Check("[CAL] 预填：从上游定位点端口（Row/Col）回填该行",
                        (row.ImageRow ?? -1) == 111.5 && (row.ImageCol ?? -1) == 222.5,
                        $"Row={row.ImageRow} Col={row.ImageCol}");

                    p.SourcePointRow.Value = null;
                    var row2 = p.PointRows[1];
                    row2.ImageRow = null;
                    row2.ImageCol = null;
                    p.PrefillRowFromSourceCommand.Execute(row2);
                    Check("[CAL] 预填：上游为空 → 明确拒绝且不动行数据",
                        row2.ImageRow is null && row2.ImageCol is null && p.StatusMessage.Contains("上游定位点"),
                        p.StatusMessage);
                }
                finally
                {
                    p.Dispose();
                }
            }

            // ---- 75) 多相机身份：序列号不一致必须失败；一致（忽略大小写）/未提供放行 ----
            {
                var p = BuildPlugin(imgPts, xs, ys);
                try
                {
                    p.CameraSerial = "CAM-A";
                    p.SourceSerial.Value = "CAM-B";
                    p.RunAlgorithm(ctx);
                    Check("[CAL] 相机身份不符 → 明确失败（标定属于 A、图像来自 B）",
                        p.Success.Value is false
                        && (p.ErrorMessage.Value as string ?? "").Contains("相机身份不符"),
                        p.ErrorMessage.Value?.ToString() ?? "");

                    p.SourceSerial.Value = "cam-a";
                    p.RunAlgorithm(ctx);
                    Check("[CAL] 序列号一致（忽略大小写）→ 正常成功", p.Success.Value is true,
                        p.ErrorMessage.Value?.ToString() ?? "");

                    p.SourceSerial.Value = "";
                    p.RunAlgorithm(ctx);
                    Check("[CAL] 上游未提供序列号 → 不校验（单相机/未接线零影响）",
                        p.Success.Value is true, "");
                }
                finally
                {
                    p.Dispose();
                }
            }

            // ---- 76) 粘贴解析（public 纯函数）：Tab 空格子=未填、行首空列保留、全空行跳过、5 列带序号 ----
            {
                string text = "1\t2\t3\t4\n\t2\t3\t4\n5\t\t7\t8\n\n\t\t\t";
                bool ok = CalibrationPlugin.TryParseRows(text, out var parsedRows, out var err);
                Check("[CAL] 粘贴解析：空单元格=null、行首空格子保留、全空行跳过",
                    ok && parsedRows.Count == 3
                    && parsedRows[0][0] == 1 && parsedRows[0][1] == 2
                    && parsedRows[1][0] is null && parsedRows[1][1] == 2
                    && parsedRows[2][0] == 5 && parsedRows[2][1] is null,
                    ok ? $"rows={parsedRows.Count}" : err ?? "");

                bool ok5 = CalibrationPlugin.TryParseRows("9\t10\t20\t30\t40", out var parsed5, out _);
                Check("[CAL] 粘贴解析：5 列带序号 → 首列丢弃、四值就位",
                    ok5 && parsed5.Count == 1 && parsed5[0][0] == 10 && parsed5[0][3] == 40,
                    ok5 ? $"count={parsed5.Count}" : "");

                bool bad = CalibrationPlugin.TryParseRows("1\t2\t3", out _, out var errBad);
                Check("[CAL] 粘贴解析：列数不对 → 报错并说明应为 4/5 列",
                    !bad && (errBad ?? "").Contains("4 列"), errBad ?? "");
            }
        }

        /// <summary>造一个九点模式的插件实例（行名 P1…P9 与网格一一对应）</summary>
        private static CalibrationPlugin BuildPlugin(
            (double Row, double Col)[] imgPts, double[] xs, double[] ys)
        {
            var plugin = new CalibrationPlugin
            {
                InstanceName = "标定断言",
                Mode = CalibrationMode.NinePoint
            };

            plugin.PointRows.Clear();
            for (int i = 0; i < imgPts.Length; i++)
            {
                plugin.PointRows.Add(new CalibPointRow
                {
                    Name = "P" + (i + 1),
                    MachineX = xs[i],
                    MachineY = ys[i],
                    ImageRow = imgPts[i].Row,
                    ImageCol = imgPts[i].Col
                });
            }

            return plugin;
        }

        /// <summary>透视真值映射（测试用）：(Row,Col) → (X,Y)。</summary>
        private static (double x, double y) MapProj(double[] h, double row, double col)
        {
            double w = h[6] * row + h[7] * col + h[8];
            return ((h[0] * row + h[1] * col + h[2]) / w, (h[3] * row + h[4] * col + h[5]) / w);
        }

        /// <summary>按透视真值批量生成机械坐标（测试用）。</summary>
        private static void MapAllProj(double[] h, double[] rows, double[] cols, out double[] xs, out double[] ys)
        {
            xs = new double[rows.Length];
            ys = new double[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                var (x, y) = MapProj(h, rows[i], cols[i]);
                xs[i] = x;
                ys[i] = y;
            }
        }

        /// <summary>h33 归一（各矩阵各自归一后再逐元比较）。</summary>
        private static double[] NormalizeH33(double[] h)
        {
            var o = new double[9];
            for (int i = 0; i < 9; i++) o[i] = h[i] / h[8];
            return o;
        }

        /// <summary>中心差分独立算 (‖J·e_row‖, ‖J·e_col‖)——独立于被测公式（断言 63）。</summary>
        private static (double lenRow, double lenCol) JacobianByCentralDiff(double[] h, double row, double col, double eps)
        {
            var (x1, y1) = MapProj(h, row + eps, col);
            var (x2, y2) = MapProj(h, row - eps, col);
            double lr = Math.Sqrt(Math.Pow((x1 - x2) / (2 * eps), 2) + Math.Pow((y1 - y2) / (2 * eps), 2));
            var (x3, y3) = MapProj(h, row, col + eps);
            var (x4, y4) = MapProj(h, row, col - eps);
            double lc = Math.Sqrt(Math.Pow((x3 - x4) / (2 * eps), 2) + Math.Pow((y3 - y4) / (2 * eps), 2));
            return (lr, lc);
        }

        /// <summary>
        /// 行残差的"局部当量口径"复算：mm 偏差 ÷ 该点局部当量；并给出"若用中心当量（1000×800 图像中心）"的对照值。
        /// </summary>
        private static bool TryPerspectiveResiduals(CalibrationTransform t, double row, double col,
            double targetX, double targetY, out double byLocalPx, out double byCenterPx)
        {
            byLocalPx = double.NaN;
            byCenterPx = double.NaN;

            var hom = t.ProjectiveMatrix;
            if (hom == null || !CalibrationMath.TryMapPixelToXYProj(hom, row, col, out var x, out var y, out _))
                return false;

            double d = Math.Sqrt((x - targetX) * (x - targetX) + (y - targetY) * (y - targetY));
            double local = CalibrationMath.DeriveMmPerPixelProjective(hom, row, col, out _);
            double center = CalibrationMath.DeriveMmPerPixelProjective(hom, 400, 500, out _);
            if (!(local > 0) || !(center > 0))
                return false;

            byLocalPx = d / local;
            byCenterPx = d / center;
            return true;
        }

        /// <summary>错误文案截断（现场输出用，避免单条断言详情过长）。</summary>
        private static string TrimErr(string? e)
            => e == null ? "(null)" : (e.Length <= 42 ? e : e.Substring(0, 42) + "…");

        /// <summary>造一个透视模式的插件实例（行名 P1…PN；数据由调用方给）。</summary>
        private static CalibrationPlugin BuildPerspectivePlugin(
            double[] rows, double[] cols, double[] xs, double[] ys)
        {
            var plugin = new CalibrationPlugin
            {
                InstanceName = "标定断言",
                Mode = CalibrationMode.Perspective
            };

            plugin.PointRows.Clear();
            for (int i = 0; i < rows.Length; i++)
            {
                plugin.PointRows.Add(new CalibPointRow
                {
                    Name = "P" + (i + 1),
                    MachineX = xs[i],
                    MachineY = ys[i],
                    ImageRow = rows[i],
                    ImageCol = cols[i]
                });
            }

            return plugin;
        }

        /// <summary>两个矩阵逐元素最大绝对差（长度不一致时返回 ∞）</summary>
        private static double MaxAbsDiff(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return double.PositiveInfinity;

            double max = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double d = Math.Abs(a[i] - b[i]);
                if (d > max) max = d;
            }
            return max;
        }
    }
}
