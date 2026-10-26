using System;
using System.Threading;
using Core.Halcon;
using Core.Interfaces;
using HalconDotNet;
using Plugin.CaliperMeasure;
using Plugin.CaliperMeasure.Models;
using Plugin.Calibration;
using Plugin.Calibration.Models;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 卡尺插件（Plugin.CaliperMeasure）×「标定」插件 **像素当量直连** 的断言（2026-10-04 晚）。
    ///
    /// 背景：当量此前只能手填（"项目暂无标定模块"的遗留），现场把标定当量抄进卡尺，
    /// 抄错 = 所有测量值整体缩放错且难以察觉。本轮给卡尺加了 MmPerPixel 输入端口：
    /// · 未接上游 → 手填值（旧行为，零变化）；
    /// · 已接上游 → 以连线为准（标定插件每轮重算的值经端口自动传导）；
    /// · 上游无效（≤0/NaN，例如标定步骤本轮失败）→ **明确失败**，绝不静默回退旧值。
    ///
    /// 断言分两层：纯逻辑（GetEffectivePixelSizeMm 三态）+ 真实链路
    /// （合成标定数据 → 真标定插件 → 真连到卡尺 → 真测出一张合成亮带图的宽度）。
    /// 合成图与真值都可笔算（亮带 200~440 列，宽 ≈240px），不依赖任何外部样图。
    /// </summary>
    internal static class CaliperMeasureChecks
    {
        public static void Run()
        {
            Section("[CALM] 卡尺插件：像素当量直连（手填/上游/失效三态 + 真实测量链路）");

            var ctx = new ExecutionContext(
                new StubLog(), new FlowSession { FlowName = "卡尺直连断言" },
                new WorkspaceContext(), new CancellationTokenSource().Token);

            // ---- 1) 端口面：MmPerPixel 存在且为可选端口（未接=旧行为） ----
            var probe = new CaliperMeasurePlugin { InstanceName = "卡尺_端口面" };
            try
            {
                Check("[CALM] 输入端口 MmPerPixel 存在、可选、类型 double（可直接接标定输出）",
                    probe.Inputs.ContainsKey("MmPerPixel") && !probe.MmPerPixel.IsRequired
                    && probe.MmPerPixel.DataType == typeof(double),
                    $"存在={probe.Inputs.ContainsKey("MmPerPixel")} 必填={probe.MmPerPixel.IsRequired}");
            }
            finally
            {
                probe.Dispose();
            }

            // ---- 1b) 源头端口类型：标定 MmPerPixel 输出 = double（直连线两端类型吻合） ----
            {
                var calibProbe = new CalibrationPlugin { InstanceName = "标定_端口面" };
                try
                {
                    Check("[CALM] 标定插件 MmPerPixel 输出类型 = double（与卡尺输入端口类型吻合）",
                        calibProbe.Outputs.ContainsKey("MmPerPixel")
                        && calibProbe.Outputs["MmPerPixel"].DataType == typeof(double),
                        calibProbe.Outputs.TryGetValue("MmPerPixel", out var mp)
                            ? mp.DataType.Name : "端口缺失");
                }
                finally
                {
                    calibProbe.Dispose();
                }
            }

            // ---- 2) 生效值三态（纯逻辑） ----
            {
                var p = new CaliperMeasurePlugin { InstanceName = "卡尺_三态" };
                try
                {
                    p.PixelSizeMm = 0.03;
                    double eff0 = p.GetEffectivePixelSizeMm(out var err0);
                    Check("[CALM] 未接上游 → 用手填值（0.03）",
                        Math.Abs(eff0 - 0.03) < 1e-12 && err0.Length == 0, $"值={eff0}");

                    var upstream = new OutputPort<double>("测试当量", "断言用的上游输出");
                    upstream.Value = 0.02;
                    p.MmPerPixel.LinkedSource = upstream;
                    double eff1 = p.GetEffectivePixelSizeMm(out var err1);
                    Check("[CALM] 已接上游 → 以连线为准（0.02，手填被打断点）",
                        Math.Abs(eff1 - 0.02) < 1e-12 && err1.Length == 0, $"值={eff1}");

                    // 模拟"配置界面试运行"：PluginTestRunner 把上游实际值灌进端口手动值（并清掉链接）——
                    // 此时必须仍以端口值为准，否则试运行拿手填值算，与运行期不一致（"所见非所得"）
                    p.MmPerPixel.LinkedSource = null;
                    p.MmPerPixel.Value = 0.02;
                    double effBridge = p.GetEffectivePixelSizeMm(out var errBridge);
                    Check("[CALM] 试运行桥接：无链接但端口值>0 → 以端口值为准（试运行所见=运行所得）",
                        Math.Abs(effBridge - 0.02) < 1e-12 && errBridge.Length == 0, $"值={effBridge}");

                    p.MmPerPixel.LinkedSource = upstream;   // 复原：继续测失效口径
                    upstream.Value = 0;
                    double eff2 = p.GetEffectivePixelSizeMm(out var err2);
                    Check("[CALM] 上游=0 → 报错并指「标定」（不静默回退）",
                        err2.Contains("标定") && Math.Abs(eff2 - 0.03) < 1e-12, err2);

                    upstream.Value = double.NaN;
                    p.GetEffectivePixelSizeMm(out var err3);
                    Check("[CALM] 上游=NaN → 同样报错（NaN 不得溜进任何计算）",
                        err3.Contains("标定"), err3);
                }
                finally
                {
                    p.Dispose();
                }
            }

            // ---- 3) 真实测量链路：标定插件 → 卡尺插件 ----
            using var image = BuildStripeImage();
            var cali = new CaliperMeasurePlugin { InstanceName = "卡尺_直连" };
            var calib = new CalibrationPlugin { InstanceName = "标定_供当量" };
            try
            {
                // 搜索区：L1=150 横跨亮带（列 200~440）、L2=30 沿竖直均布、Phi=0（扫描方向=水平，硬事实）
                cali.CaliperRegions.Add(new CaliperRegion
                {
                    Name = "搜索区1",
                    ShapeType = DrawShapeType.Rectangle,
                    Params = new[] { 240.0, 320.0, 0.0, 150.0, 30.0 }
                });
                cali.CaliperCount = 5;
                cali.SrcImage.Value = image;

                // ── 态①：未接上游，手填 0.5 ──
                cali.PixelSizeMm = 0.5;
                cali.Execute(ctx);
                bool ok1 = cali.Success.Value is true;
                double v1 = Convert.ToDouble(cali.MeasureValue.Value);
                Check("[CALM] 未接上游：手填当量跑通（亮带 ≈240px × 0.5 ≈ 120mm）",
                    ok1 && v1 > 100 && v1 < 140,
                    $"Success={cali.Success.Value} 值={v1:0.###} Err='{cali.ErrorMessage.Value}'");
                double px = v1 / 0.5;   // 反推像素宽度，供后续比例断言（同一条亮带、确定性测量）

                // ── 态②：接普通上游 2.0 → 以连线为准（手填 0.5 必须被忽略） ──
                var upstream = new OutputPort<double>("测试当量", "");
                upstream.Value = 2.0;
                cali.MmPerPixel.LinkedSource = upstream;
                cali.Execute(ctx);
                double v2 = Convert.ToDouble(cali.MeasureValue.Value);
                Check("[CALM] 已接上游：以连线为准（同一亮带，2.0/0.5 应放大 4 倍）",
                    cali.Success.Value is true && px > 0 && Math.Abs(v2 / (px * 2.0) - 1) < 1e-6,
                    $"值={v2:0.###}（期望 ≈ {px * 2.0:0.###}）");

                // ── 态③：上游失效（0）→ 明确失败 ──
                upstream.Value = 0;
                cali.Execute(ctx);
                string errBad = cali.ErrorMessage.Value as string ?? "";
                Check("[CALM] 上游当量=0 → 步骤失败并指「标定」（不给旧值硬算的机会）",
                    cali.Success.Value is false && errBad.Contains("标定"),
                    $"Success={cali.Success.Value} Err='{errBad}'");
                // 失败轮不得留上一轮脏数据（开轮重置契约）——下游读不到旧的测量值
                Check("[CALM] 当量失效的失败轮：输出端口清零（无上一轮脏值）",
                    Convert.ToDouble(cali.MeasureValue.Value) == 0
                    && Convert.ToDouble(cali.Deviation.Value) == 0
                    && cali.IsOk.Value is false,
                    $"值={cali.MeasureValue.Value} 偏差={cali.Deviation.Value} IsOk={cali.IsOk.Value}");

                // ── 态④：真实链路——真标定插件产出 0.02 → 直接连到卡尺 ──
                calib.Mode = CalibrationMode.NinePoint;
                calib.PointRows.Clear();
                calib.PointRows.Add(new CalibPointRow { Name = "P1", MachineX = 2, MachineY = 2, ImageRow = 100, ImageCol = 100 });
                calib.PointRows.Add(new CalibPointRow { Name = "P2", MachineX = 2, MachineY = 12, ImageRow = 100, ImageCol = 600 });
                calib.PointRows.Add(new CalibPointRow { Name = "P3", MachineX = 12, MachineY = 2, ImageRow = 600, ImageCol = 100 });
                calib.Execute(ctx);
                var t = calib.Transform.Value as CalibrationTransform;
                Check("[CALM] 标定插件先产出一份当量 0.02 的标定（链路源头）",
                    calib.Success.Value is true && t != null && Math.Abs(t.MmPerPixel - 0.02) < 1e-9,
                    t == null ? "无输出" : $"mm/px={t.MmPerPixel}");

                cali.MmPerPixel.LinkedSource = calib.MmPerPixel;
                cali.PixelSizeMm = 9.99;   // 手填值再换一个：必须仍被连线覆盖
                cali.Execute(ctx);
                double v4 = Convert.ToDouble(cali.MeasureValue.Value);
                Check("[CALM] 真实链路：卡尺以标定当量为准（≈240px × 0.02 ≈ 4.8mm）",
                    cali.Success.Value is true && px > 0 && Math.Abs(v4 / (px * 0.02) - 1) < 1e-6,
                    $"值={v4:0.####}（期望 ≈ {px * 0.02:0.####}）");

                // ---- 标注底图逐点保真 ----
                // 亮带是"全高、列 200~440"，所以取 ROI（行 225~255）**下方**的行 470：
                // 列 320 在亮带内应为 255、列 100 在亮带外应为 0，两点都避开左上角文字框与搜索区标注。
                // 底图被"缩进左上角"（离屏窗口漏掉 set_part）时，这两点会一起塌成同一个常数。
                var calmShot = cali.MeasureImage.TypedValue;
                if (calmShot != null && calmShot.IsInitialized())
                {
                    double inStripe = PixelProbe.At(calmShot, 470, 320);
                    double outStripe = PixelProbe.At(calmShot, 470, 100);
                    Check("[CALM] 标注底图逐点保真（亮带内 255 / 亮带外 0，底图没被缩进左上角）",
                        Math.Abs(inStripe - 255) <= 3 && outStripe <= 3,
                        $"(470,320)={inStripe:0.#} (470,100)={outStripe:0.#}");
                }
                else
                {
                    Check("[CALM] 标注底图逐点保真（亮带内 255 / 亮带外 0，底图没被缩进左上角）",
                        false, $"MeasureImage 为空；Err='{cali.ErrorMessage.Value}'");
                }

                // ── 态⑤：标定失败 → 当量口归 0 → 卡尺明确失败（端到端"绝不静默"） ──
                calib.PointRows[0].MachineX = null;   // P1 半填 → 标定失败
                calib.Execute(ctx);
                cali.Execute(ctx);
                string errChain = cali.ErrorMessage.Value as string ?? "";
                Check("[CALM] 标定失败传导：当量口归 0 → 卡尺明确失败（不拿旧标定硬算）",
                    calib.Success.Value is false && cali.Success.Value is false && errChain.Contains("标定"),
                    $"标定 Success={calib.Success.Value}；卡尺 Err='{errChain}'");
            }
            finally
            {
                cali.Dispose();
                calib.Dispose();
            }

            RunMeasurementTruth(ctx);
        }

        // ==================================================================
        //  测量真值：宽度 / 圆直径 / 角度（合成图，真插件 Execute）
        // ==================================================================
        private static void RunMeasurementTruth(ExecutionContext ctx)
        {
            Section("[CALM] 测量真值：宽度/圆直径/角度（合成图，真插件 Execute）");

            using (var img = BuildBandImage(300, 339))   // 40 px 宽
            {
                var p = NewMeasurePlugin(img, MeasureKind.Width);
                try
                {
                    p.CaliperCount = 5;
                    p.CaliperRegions.Add(Rect(240, 320, 0, 80, 100));
                    p.Execute(ctx);
                    double v = Convert.ToDouble(p.MeasureValue.Value);
                    Check("[CALM] 宽度：40px 亮带 → 40px",
                        p.Success.Value is true && Math.Abs(v - 40) <= 0.05,
                        $"实测={v:0.####} Success={p.Success.Value} Err='{p.ErrorMessage.Value}'");
                }
                finally { p.Dispose(); }
            }

            using (var img = BuildCircleImage(240, 320, 100))
            {
                var p = NewMeasurePlugin(img, MeasureKind.CircleDiameter);
                try
                {
                    p.CaliperCount = 36;
                    p.AnnulusWidth = 40;
                    p.FitMinPoints = 3;
                    p.CaliperRegions.Add(Circle(240, 320, 100));
                    p.Execute(ctx);
                    double v = Convert.ToDouble(p.MeasureValue.Value);
                    Check("[CALM] 圆直径：r=100 → 200px",
                        p.Success.Value is true && Math.Abs(v - 200) <= 0.5,
                        $"实测={v:0.####} Success={p.Success.Value} Err='{p.ErrorMessage.Value}'");
                }
                finally { p.Dispose(); }
            }

            using (var img = BuildTwoBarsImage())
            {
                double a1 = 30 * Math.PI / 180, a2 = 80 * Math.PI / 180;

                // 同侧：两条射线夹角 50°
                var p = NewMeasurePlugin(img, MeasureKind.Angle);
                try
                {
                    p.CaliperCount = 5;
                    p.FitMinPoints = 2;
                    p.FitMaxError = 5;
                    p.CaliperRegions.Add(Rect(
                        240 - 100 * Math.Sin(a1), 320 + 100 * Math.Cos(a1), a1 - Math.PI / 2, 30, 80));
                    p.CaliperRegions.Add(Rect(
                        240 - 100 * Math.Sin(a2), 320 + 100 * Math.Cos(a2), a2 - Math.PI / 2, 30, 80));
                    p.Execute(ctx);
                    double v = Convert.ToDouble(p.MeasureValue.Value);
                    Check("[CALM] 角度：30/80 度两条亮条、ROI 同侧 → 50°",
                        p.Success.Value is true && Math.Abs(v - 50) <= 2,
                        $"实测={v:0.####} Success={p.Success.Value} Err='{p.ErrorMessage.Value}'");
                }
                finally { p.Dispose(); }

                // 异侧：ROI1 在交点负方向 → 130°
                var q = NewMeasurePlugin(img, MeasureKind.Angle);
                try
                {
                    q.CaliperCount = 5;
                    q.FitMinPoints = 2;
                    q.FitMaxError = 5;
                    q.CaliperRegions.Add(Rect(
                        240 + 100 * Math.Sin(a1), 320 - 100 * Math.Cos(a1), a1 - Math.PI / 2, 30, 80));
                    q.CaliperRegions.Add(Rect(
                        240 - 100 * Math.Sin(a2), 320 + 100 * Math.Cos(a2), a2 - Math.PI / 2, 30, 80));
                    q.Execute(ctx);
                    double v = Convert.ToDouble(q.MeasureValue.Value);
                    Check("[CALM] 角度：30/80 度两条亮条、ROI 异侧 → 130°",
                        q.Success.Value is true && Math.Abs(v - 130) <= 2,
                        $"实测={v:0.####} Success={q.Success.Value} Err='{q.ErrorMessage.Value}'");
                }
                finally { q.Dispose(); }
            }
        }

        private static CaliperMeasurePlugin NewMeasurePlugin(HImage image, MeasureKind kind)
        {
            var p = new CaliperMeasurePlugin { InstanceName = "卡尺_真值" };
            p.SrcImage.Value = image;
            p.MeasureKind = kind;
            p.CaliperRegions.Clear();   // 清掉 MeasureKind setter 播种的默认搜索区
            p.CaliperCount = 1;
            p.Sigma = 1.0;
            p.EdgeThreshold = 20;
            p.Polarity = EdgePolarity.All;
            p.Select = EdgeSelect.First;
            p.Interpolation = EdgeInterpolation.Bilinear;
            p.FitAlgorithm = FitAlgorithmKind.Regression;
            p.FitMaxError = 2.0;
            p.FitMinPoints = 3;
            p.AnnulusWidth = 40;
            p.StandardValue = 0;
            p.UpperTolerance = 1e9;
            p.LowerTolerance = -1e9;
            p.PixelSizeMm = 1.0;
            return p;
        }

        private static CaliperRegion Rect(double row, double col, double phi, double l1, double l2)
            => new() { ShapeType = DrawShapeType.Rectangle, Params = new[] { row, col, phi, l1, l2 } };

        private static CaliperRegion Circle(double row, double col, double radius)
            => new() { ShapeType = DrawShapeType.Circle, Params = new[] { row, col, radius, 0, 0 } };

        private static HImage BuildBandImage(double c1, double c2)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", 640, 480);
            HOperatorSet.GenImageProto(proto, out HObject img, 0);
            HOperatorSet.GenRectangle1(out HObject band, 0, c1, 479, c2);
            HOperatorSet.PaintRegion(band, img, out HObject painted, 255, "fill");
            proto.Dispose(); img.Dispose(); band.Dispose();
            var result = new HImage(painted);
            painted.Dispose();
            return result;
        }

        private static HImage BuildCircleImage(double row, double col, double radius)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", 640, 480);
            HOperatorSet.GenImageProto(proto, out HObject img, 0);
            HOperatorSet.GenCircle(out HObject disc, row, col, radius);
            HOperatorSet.PaintRegion(disc, img, out HObject painted, 255, "fill");
            proto.Dispose(); img.Dispose(); disc.Dispose();
            var result = new HImage(painted);
            painted.Dispose();
            return result;
        }

        private static HImage BuildTwoBarsImage()
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", 640, 480);
            HOperatorSet.GenImageProto(proto, out HObject img, 0);
            HOperatorSet.GenRectangle2(out HObject bar1, 240, 320, 30 * Math.PI / 180, 200, 4);
            HOperatorSet.PaintRegion(bar1, img, out HObject painted1, 255, "fill");
            HOperatorSet.GenRectangle2(out HObject bar2, 240, 320, 80 * Math.PI / 180, 200, 4);
            HOperatorSet.PaintRegion(bar2, painted1, out HObject painted2, 255, "fill");
            proto.Dispose(); img.Dispose(); bar1.Dispose(); painted1.Dispose(); bar2.Dispose();
            var result = new HImage(painted2);
            painted2.Dispose();
            return result;
        }

        /// <summary>合成图：640×480 黑底 + 竖直亮带（列 200~440，宽 ≈240px）——"L1 横跨亮带"的教科书场景</summary>
        private static HImage BuildStripeImage()
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", 640, 480);
            HOperatorSet.GenImageProto(proto, out HObject img, 0);
            HOperatorSet.GenRectangle1(out HObject stripe, 0, 200, 479, 440);
            HOperatorSet.PaintRegion(stripe, img, out HObject painted, 255, "fill");
            proto.Dispose();
            img.Dispose();
            stripe.Dispose();
            var result = new HImage(painted);
            painted.Dispose();
            return result;
        }
    }
}
