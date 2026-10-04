using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Core.Interfaces;
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
            Section("[CAL] 标定插件：仿射求解 / 正反变换 / 质量闸门 / 端口面");

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
                rows, cols, xs, ys, out var matrix, out var rms, out var max, out _, out var error);
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
                out var m3, out var rms3, out _, out _, out var err3);
            Check("[CAL] 3 个非共线点恰好可解（残差≈0）",
                threeOk && m3 != null && rms3 < 1e-9 && MaxAbsDiff(m3, known) < 1e-6,
                threeOk ? $"rms={rms3:0.###e+0}" : err3 ?? "求解失败");

            // ---- 4) 共线点必须失败（防静默定不出方向） ----
            bool collinearOk = CalibrationMath.TrySolveAffine(
                new[] { 100d, 200, 300, 400, 500, 600, 700, 800, 900 },
                new[] { 100d, 200, 300, 400, 500, 600, 700, 800, 900 },
                new[] { 0d, 1, 2, 3, 4, 5, 6, 7, 8 },
                new[] { 0d, 1, 2, 3, 4, 5, 6, 7, 8 },
                out _, out _, out _, out _, out var collinearError);
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
                    && Math.Abs(back.ResidualByRadiusBands[2] - transform.ResidualByRadiusBands[2]) < 1e-12,
                    json.Length > 0 ? "" : "序列化为空");
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
            plugin.PointRows[4].MachineX -= 0.5;   // 先还原
            plugin.PointRows[2].ImageCol = 0;      // P3 变成半填（其余三值仍在）
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
