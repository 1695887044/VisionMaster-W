using System;
using System.Collections.Generic;
using System.Linq;
using Core.Halcon;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using Plugin.CreateRoi;
using Plugin.CreateRoi.Models;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 创建ROI（Plugin.CreateRoi）的断言。
    ///
    /// 真值策略与 MatchingChecks 同款：不依赖外部样图，HALCON 现场合成确定性图像/区域，
    /// 期望值笔算可验（面积/灰度/包围盒）。覆盖：三形状区域构建、掩膜构建与反转、
    /// 合并区域与涂擦修正、动态端口重建与重名防御、选中联动契约（参数微调不断链）、
    /// 运行端到端（裁剪/擦除修正/成功计数/失败语义）、涂擦游程序列化往返（含 gz 压缩）。
    /// </summary>
    internal static class CreateRoiChecks
    {
        private const int ImgH = 400, ImgW = 520;

        public static void Run()
        {
            Section("[CreateRoi] 创建ROI插件（形状/掩膜/涂擦/动态端口/运行端到端）");

            RunShapeAndMask();
            RunMergedAndSmear();
            RunDynamicPortsAndNames();
            RunSelectionContract();
            RunAlgorithmContract();
            RunSerializeRoundTrip();
        }

        // ==================================================================
        //  ① 三形状区域构建 + 掩膜构建（正常/反转/三通道）
        // ==================================================================
        private static void RunShapeAndMask()
        {
            // 注意：HALCON 区域按像素栅格化，面积断言按"理想值+像素化偏差"给容差；
            // 矩形中心不能贴原点（半宽 88 会伸出负坐标被 HALCON 裁剪，面积/质心都会偏）
            var rect = CreateRoiPlugin.BuildRegion(new RoiItem
                { ShapeType = DrawShapeType.Rectangle, Params = new double[] { 150, 150, 0.3, 80, 40 } });
            Check("【形状】矩形区域面积 = 4·L1·L2（旋转不变，容差按栅格化边界）",
                rect != null && Math.Abs(AreaOf(rect!) - 4 * 80 * 40) <= 320,
                rect == null ? "null" : $"area={AreaOf(rect):0.00}");
            var (r1, c1) = CenterOf(rect!);
            Check("【形状】矩形中心回准 (150,150)", Math.Abs(r1 - 150) < 0.5 && Math.Abs(c1 - 150) < 0.5, $"({r1:0.00},{c1:0.00})");

            var circ = CreateRoiPlugin.BuildRegion(new RoiItem
                { ShapeType = DrawShapeType.Circle, Params = new double[] { 30, 40, 15 } });
            Check("【形状】圆形区域面积 = π·r²（r=15 像素化后 716 px）",
                circ != null && Math.Abs(AreaOf(circ!) - 716) <= 3,
                circ == null ? "null" : $"area={AreaOf(circ):0.00}");

            var ell = CreateRoiPlugin.BuildRegion(new RoiItem
                { ShapeType = DrawShapeType.Ellipse, Params = new double[] { 100, 120, 0, 60, 30 } });
            Check("【形状】椭圆区域面积 = π·Ra·Rb（60×30 像素化后 5732 px）",
                ell != null && Math.Abs(AreaOf(ell!) - 5732) <= 6,
                ell == null ? "null" : $"area={AreaOf(ell):0.00}");

            Check("【形状】空参数/参数不足 → null（不炸）",
                CreateRoiPlugin.BuildRegion(new RoiItem { ShapeType = DrawShapeType.Circle }) == null
                && CreateRoiPlugin.BuildRegion(new RoiItem { ShapeType = DrawShapeType.Rectangle, Params = new[] { 1d, 2d } }) == null,
                "");

            var src = MakeGray(180);
            var roi = CircleRegion(200, 260, 50);
            try
            {
                var p = new CreateRoiPlugin();
                var masked = p.BuildMaskAndResult(src, roi, invert: false, out var mask, out _);
                Check("【掩膜】正常模式：ROI 内保留原图灰度", masked != null && Math.Abs(GrayAt(masked, 200, 260) - 180) < 0.01,
                    masked == null ? "null" : $"gray={GrayAt(masked, 200, 260):0.0}");
                Check("【掩膜】正常模式：ROI 外置黑", Math.Abs(GrayAt(masked!, 0, 0)) < 0.01, "");
                Check("【掩膜】二值掩膜内 255 外 0",
                    Math.Abs(GrayAt(mask!, 200, 260) - 255) < 0.01 && Math.Abs(GrayAt(mask, 0, 0)) < 0.01, "");

                var inv = p.BuildMaskAndResult(src, roi, invert: true, out var mask2, out _);
                Check("【掩膜】排除模式：ROI 内置黑、外部保留",
                    Math.Abs(GrayAt(inv!, 200, 260)) < 0.01 && Math.Abs(GrayAt(inv, 0, 0) - 180) < 0.01, "");
                Check("【掩膜】排除模式掩膜反转（内 0 外 255）",
                    Math.Abs(GrayAt(mask2!, 200, 260)) < 0.01 && Math.Abs(GrayAt(mask2, 0, 0) - 255) < 0.01, "");

                var src3 = MakeGray3(180);
                var masked3 = p.BuildMaskAndResult(src3, roi, invert: false, out _, out _);
                HOperatorSet.AccessChannel(masked3!, out HObject ch1, 1);
                var ch = new HImage(ch1);
                ch1.Dispose();
                Check("【掩膜】三通道图合成正常（逐通道相乘不截断）", Math.Abs(GrayAt(ch, 200, 260) - 180) < 0.01, "");
                ch.Dispose();
                masked.Dispose(); mask.Dispose(); inv.Dispose(); mask2.Dispose(); masked3.Dispose();
                src3.Dispose();
                p.Dispose();
            }
            finally
            {
                src.Dispose();
                roi.Dispose();
            }
        }

        // ==================================================================
        //  ② 合并区域 + 涂擦修正（∪ 涂抹 − 擦除）
        // ==================================================================
        private static void RunMergedAndSmear()
        {
            var p = new CreateRoiPlugin();
            try
            {
                p.RoiList.Add(new RoiItem { Name = "A", ShapeType = DrawShapeType.Circle, Params = new double[] { 100, 100, 30 } });
                p.RoiList.Add(new RoiItem { Name = "B", ShapeType = DrawShapeType.Circle, Params = new double[] { 200, 100, 30 } });

                using (var merged = p.BuildMergedRegion())
                    Check("【合并】两个不相交 ROI 面积 = 各自之和（r=30 圆盘 2828 px/个）",
                        Math.Abs(AreaOf(merged) - 2 * 2828) <= 3, $"area={AreaOf(merged):0.00}");

                p.SmearDraw = CircleRegion(350, 100, 20); // 不与任何 ROI 相交的涂抹区
                using (var merged = p.BuildMergedRegion())
                    Check("【合并】涂抹区并入有效区域（∪ 涂抹，r=20 圆盘 1264 px）",
                        Math.Abs(AreaOf(merged) - (2 * 2828 + 1264)) <= 3,
                        $"area={AreaOf(merged):0.00}");

                p.SmearErase = CircleRegion(100, 100, 15); // 完全落在 ROI A 内的擦除区
                using (var merged = p.BuildMergedRegion())
                    Check("【合并】擦除区从有效区域挖除（− 擦除，r=15 圆盘 716 px）",
                        Math.Abs(AreaOf(merged) - (2 * 2828 + 1264 - 716)) <= 3,
                        $"area={AreaOf(merged):0.00}");
            }
            finally
            {
                p.Dispose();
            }
        }

        // ==================================================================
        //  ③ 动态端口重建 + 重名防御（画布撞名 / 历史数据播种去重）
        // ==================================================================
        private static void RunDynamicPortsAndNames()
        {
            var stepData = new FakeStepData();
            var p = new CreateRoiPlugin { InstanceName = "ROI_端口" };
            try
            {
                p.Initialize(stepData);
                AddCanvasRoi(p, "ROI_0", DrawShapeType.Rectangle, new double[] { 50, 60, 0, 80, 40 });
                AddCanvasRoi(p, "ROI_1", DrawShapeType.Circle, new double[] { 100, 100, 20 });

                var cropPorts = p.Outputs.Keys.Where(k => k.StartsWith("Crop_")).OrderBy(x => x).ToList();
                Check("【端口】画布新增 ROI 即长出 Crop_ 动态端口",
                    cropPorts.SequenceEqual(new[] { "Crop_ROI_0", "Crop_ROI_1" }),
                    string.Join(",", cropPorts));
                Check("【端口】固定端口 MaskRegion/MaskImage 随插件就位",
                    p.Outputs.ContainsKey("MaskRegion") && p.Outputs.ContainsKey("MaskImage"), "");
                Check("【端口】快照同步到 StepData（编译器据此接线）",
                    stepData.OutputPortDefinitions.Select(x => x.Name).OrderBy(x => x)
                        .SequenceEqual(new[] { "Crop_ROI_0", "Crop_ROI_1" }),
                    string.Join(",", stepData.OutputPortDefinitions.Select(x => x.Name)));

                // 模拟"重开配置后控件命名序号归零再画"的撞名场景
                AddCanvasRoi(p, "ROI_1", DrawShapeType.Circle, new double[] { 300, 300, 25 });
                var names = p.RoiList.Select(x => x.Name).ToList();
                Check("【重名防御】画布撞名后 RoiList 全局唯一",
                    names.Count == 3 && names.Distinct().Count() == 3,
                    string.Join(",", names));
                Check("【重名防御】唯一名回写控件标签（画布显示跟进）",
                    p.CanvasRois.Last().RoiName == "ROI_2", $"'{p.CanvasRois.Last().RoiName}'");
                Check("【重名防御】端口无重名（不再静默去重/互相覆盖）",
                    p.Outputs.Keys.Count(k => k.StartsWith("Crop_")) == 3
                    && p.Outputs.ContainsKey("Crop_ROI_2"),
                    string.Join(",", p.Outputs.Keys.Where(k => k.StartsWith("Crop_")).OrderBy(x => x)));

                // 历史重名数据播种去重（旧版 bug 存下的方案）
                var step2 = new FakeStepData();
                var p2 = new CreateRoiPlugin();
                try
                {
                    p2.RoiList.Add(new RoiItem { Name = "ROI_0", ShapeType = DrawShapeType.Circle, Params = new double[] { 10, 10, 5 } });
                    p2.RoiList.Add(new RoiItem { Name = "ROI_0", ShapeType = DrawShapeType.Circle, Params = new double[] { 20, 10, 5 } });
                    p2.Initialize(step2);
                    Check("【重名防御】历史重名方案播种时去重改名",
                        p2.RoiList[0].Name == "ROI_0" && p2.RoiList[1].Name == "ROI_1"
                        && p2.CanvasRois[1].RoiName == "ROI_1",
                        $"{p2.RoiList[0].Name}/{p2.RoiList[1].Name}");
                    Check("【重名防御】去重后端口与快照同步重建",
                        step2.OutputPortDefinitions.Select(x => x.Name).OrderBy(x => x)
                            .SequenceEqual(new[] { "Crop_ROI_0", "Crop_ROI_1" }),
                        string.Join(",", step2.OutputPortDefinitions.Select(x => x.Name)));
                }
                finally { p2.Dispose(); }
            }
            finally
            {
                p.Dispose();
            }
        }

        // ==================================================================
        //  ④ 选中联动契约：列表选中 → 参数微调同步画布（旧 bug 在此断链）
        // ==================================================================
        private static void RunSelectionContract()
        {
            var p = new CreateRoiPlugin();
            try
            {
                p.Initialize(new FakeStepData());
                AddCanvasRoi(p, "ROI_0", DrawShapeType.Rectangle, new double[] { 50, 60, 0, 80, 40 });
                AddCanvasRoi(p, "ROI_1", DrawShapeType.Circle, new double[] { 300, 300, 25 });
                var roi0 = p.RoiList[0];
                var roi1 = p.RoiList[1];
                var info0 = p.CanvasRois.First(x => x.RoiName == "ROI_0");
                var info1 = p.CanvasRois.First(x => x.RoiName == "ROI_1");

                p.SelectedRoi = roi0; // 列表选中路径（会经 CanvasActiveRoi 联动回写同值）
                Check("【联动】列表选中 → 画布挂接同一对象", ReferenceEquals(p.CanvasActiveRoi, info0), "");
                roi0.ParamEntries[0].Value = 77;
                Check("【联动】参数微调写回画布句柄（回归：旧 bug 在列表选中路径断链）",
                    info0.HTuples[0].D == 77, $"HTuples[0]={info0.HTuples[0].D}");

                p.CanvasActiveRoi = info1; // 画布点选路径
                Check("【联动】画布点选 → 列表定位", ReferenceEquals(p.SelectedRoi, roi1), "");
                roi1.ParamEntries[1].Value = 88;
                Check("【联动】画布选中路径参数微调同样生效", info1.HTuples[1].D == 88, $"HTuples[1]={info1.HTuples[1].D}");
                Check("【联动】换选后旧 ROI 参数不被误改", info0.HTuples[0].D == 77, "");

                p.CanvasActiveRoi = null; // 画布点空白
                Check("【联动】画布点空白 → 列表同步取消选中", p.SelectedRoi == null, "");

                p.SelectedRoi = roi0; // 重选后订阅恢复，微调仍生效
                roi0.ParamEntries[3].Value = 99;
                Check("【联动】重选后参数微调恢复生效", info0.HTuples[3].D == 99, $"HTuples[3]={info0.HTuples[3].D}");
            }
            finally
            {
                p.Dispose();
            }
        }

        // ==================================================================
        //  ⑤ 运行端到端：裁剪/擦除修正/掩膜端口/成功计数/失败语义
        // ==================================================================
        private static void RunAlgorithmContract()
        {
            var stepData = new FakeStepData();
            var p = new CreateRoiPlugin { InstanceName = "ROI_运行" };
            try
            {
                p.Initialize(stepData);
                AddCanvasRoi(p, "ROI_0", DrawShapeType.Rectangle, new double[] { 50, 60, 0, 40, 30 });
                AddCanvasRoi(p, "ROI_1", DrawShapeType.Circle, new double[] { 300, 400, 30 });
                p.DisplayViewIndex = 0; // "不显示"：不得发布也不得炸（旧 bug 会误发到窗口1）

                var src = MakeGray(180);
                try
                {
                    p.SrcImage.Value = src;
                    var log = new StubLog();
                    p.Execute(NewContext(log));

                    Check("【运行】成功且 RoiCount = 实际输出数", p.Success.Value is true && Convert.ToInt32(p.RoiCount.Value) == 2,
                        $"Success={p.Success.Value} RoiCount={p.RoiCount.Value}");
                    // 像素化口径：矩形含边界 61×81=4941 px，r=30 圆盘 2828 px
                    var mr = p.MaskRegion.Value as HRegion;
                    Check("【运行】MaskRegion = 两 ROI 面积之和",
                        mr != null && Math.Abs(AreaOf(mr) - (4941 + 2828)) <= 3,
                        mr != null ? $"area={AreaOf(mr):0.00}" : "null");
                    Check("【运行】MaskImage 二值化正确（ROI 内 255 外 0）",
                        p.MaskImage.Value is HImage mi && Math.Abs(GrayAt(mi, 50, 60) - 255) < 0.01
                        && Math.Abs(GrayAt(mi, 0, 0)) < 0.01, "");

                    var crop0 = p.Outputs["Crop_ROI_0"].Value as HImage;
                    crop0!.GetImageSize(out int w0, out int h0);
                    Check("【运行】矩形裁剪图包围盒正确（81×61）且域完整",
                        crop0.IsInitialized() && w0 == 81 && h0 == 61 && Math.Abs(DomainAreaOf(crop0) - 4941) <= 2,
                        $"{w0}×{h0} domain={DomainAreaOf(crop0):0.0}");
                    var crop1 = p.Outputs["Crop_ROI_1"].Value as HImage;
                    Check("【运行】圆形裁剪图域面积 = π·r²（像素化 2828 px）",
                        crop1!.IsInitialized() && Math.Abs(DomainAreaOf(crop1) - 2828) <= 2,
                        $"domain={DomainAreaOf(crop1):0.0}");

                    // 成功计数语义：混入参数无效的 ROI → 只跳过它并留痕，其余照常输出
                    p.CanvasRois.Add(new DrawingObjectInfo(DrawShapeType.Circle, Array.Empty<HTuple>(), "坏ROI"));
                    log = new StubLog();
                    p.Execute(NewContext(log));
                    Check("【运行】坏 ROI 只跳过不阻断，RoiCount 仍=成功数",
                        p.Success.Value is true && Convert.ToInt32(p.RoiCount.Value) == 2
                        && p.Outputs["Crop_ROI_0"].Value is HImage, $"RoiCount={p.RoiCount.Value}");
                    Check("【运行】坏 ROI 留下警告日志（现场可追查）",
                        log.HasWarn("坏ROI") && log.HasWarn("参数无效"), string.Join(";", log.Warns));
                }
                finally { src.Dispose(); }
            }
            finally { p.Dispose(); }

            // 擦除修正吃进裁剪域
            var step6 = new FakeStepData();
            var p6 = new CreateRoiPlugin { InstanceName = "ROI_擦除" };
            try
            {
                p6.Initialize(step6);
                AddCanvasRoi(p6, "ROI_0", DrawShapeType.Rectangle, new double[] { 50, 60, 0, 40, 30 });
                p6.SmearErase = CircleRegion(50, 60, 10); // 圆心在矩形中心，整体被吞
                var src6 = MakeGray(180);
                try
                {
                    p6.SrcImage.Value = src6;
                    p6.Execute(NewContext(new StubLog()));
                    var crop = p6.Outputs["Crop_ROI_0"].Value as HImage;
                    // r=10 圆盘栅格化 316 px：4941−316=4625
                    Check("【擦除】擦除区从裁剪域挖除（4941−316）",
                        crop!.IsInitialized() && Math.Abs(DomainAreaOf(crop) - 4625) <= 2,
                        $"domain={DomainAreaOf(crop):0.0}");
                    crop.GetImageSize(out int w, out int h);
                    Check("【擦除】裁剪包围盒不变（只挖洞不缩框）", w == 81 && h == 61, $"{w}×{h}");
                }
                finally { src6.Dispose(); }
            }
            finally { p6.Dispose(); }

            // 空输入失败语义
            var p4 = new CreateRoiPlugin { InstanceName = "ROI_空入" };
            try
            {
                p4.Initialize(new FakeStepData());
                var ok = p4.Execute(NewContext(new StubLog()));
                Check("【失败】无输入图 → 显式失败并写明原因",
                    !ok && (p4.ErrorMessage.Value as string ?? "").Contains("输入图像为空"),
                    $"'{p4.ErrorMessage.Value}'");
            }
            finally { p4.Dispose(); }

            // 三通道底图 + 默认显示窗口（发布路径 headless 退化为同步空操作，不得炸）
            var p5 = new CreateRoiPlugin { InstanceName = "ROI_三通道" };
            try
            {
                p5.Initialize(new FakeStepData());
                AddCanvasRoi(p5, "ROI_0", DrawShapeType.Circle, new double[] { 200, 260, 50 });
                var src5 = MakeGray3(180);
                try
                {
                    p5.SrcImage.Value = src5;
                    p5.Execute(NewContext(new StubLog()));
                    Check("【运行】三通道底图全链路正常（裁剪+掩膜+发布）",
                        p5.Success.Value is true && Convert.ToInt32(p5.RoiCount.Value) == 1
                        && p5.Outputs["Crop_ROI_0"].Value is HImage c && c.IsInitialized(), "");
                }
                finally { src5.Dispose(); }
            }
            finally { p5.Dispose(); }
        }

        // ==================================================================
        //  ⑥ 序列化往返：RoiList 快照 + 涂擦游程（含 gz 压缩格式）
        // ==================================================================
        private static void RunSerializeRoundTrip()
        {
            var stepData = new FakeStepData();
            var p = new CreateRoiPlugin { InstanceName = "ROI_往返" };
            try
            {
                p.Initialize(stepData);
                AddCanvasRoi(p, "ROI_0", DrawShapeType.Rectangle, new double[] { 50, 60, 0, 40, 30 });
                p.SmearDraw = CircleRegion(100, 100, 20);   // 小区域 → 纯文本格式
                p.SmearErase = BigRunsRegion();             // 600 行游程 → 超 4KB 触发 gz 压缩
                p.OnConfirm(stepData);

                var drawData = stepData.InputValues["SmearDrawData"] as string ?? "";
                var eraseData = stepData.InputValues["SmearEraseData"] as string ?? "";
                Check("【往返】小涂擦走纯文本格式（可读可手改）",
                    drawData.Length > 0 && !drawData.StartsWith("gz:", StringComparison.Ordinal)
                    && drawData.Contains(","), drawData.Length > 40 ? drawData.Substring(0, 40) + "…" : drawData);
                Check("【往返】大涂擦自动转 gz 压缩（方案文件不膨胀）",
                    eraseData.StartsWith("gz:", StringComparison.Ordinal)
                    && eraseData.Length < drawData.Length * 50,
                    $"erase {eraseData.Length} 字符 / 解压前 RLE 约 6600");

                Check("【往返】RoiList 快照落盘且与活对象脱钩",
                    stepData.InputValues.ContainsKey("RoiList")
                    && !ReferenceEquals(stepData.InputValues["RoiList"], p.RoiList), "");

                var p2 = new CreateRoiPlugin { InstanceName = "ROI_重开" };
                try
                {
                    p2.Initialize(stepData);
                    var roi = p2.RoiList.Single();
                    Check("【往返】ROI 配置无损恢复（名字/形状/参数）",
                        roi.Name == "ROI_0" && roi.ShapeType == DrawShapeType.Rectangle
                        && roi.Params.SequenceEqual(new double[] { 50, 60, 0, 40, 30 }),
                        $"{roi.Name}[{roi.ShapeType}]");
                    Check("【往返】涂抹区从纯文本恢复（r=20 圆盘像素化 1264 px）",
                        p2.SmearDraw != null && Math.Abs(AreaOf(p2.SmearDraw) - 1264) <= 6,
                        p2.SmearDraw == null ? "null" : $"area={AreaOf(p2.SmearDraw):0.00}");
                    Check("【往返】擦除区从 gz 压缩恢复（600 行游程全量还原）",
                        p2.SmearErase != null && Math.Abs(AreaOf(p2.SmearErase) - 1200) <= 0.5,
                        p2.SmearErase == null ? "null" : $"area={AreaOf(p2.SmearErase):0.00}");
                }
                finally { p2.Dispose(); }

                p.Dispose(); p.Dispose(); // Dispose 幂等
                Check("【释放】Dispose 幂等不抛", true, "");
            }
            finally { p.Dispose(); }

            // 视图冒烟：XAML/BAML 运行时解析（布局重构后 UniformGrid/绑定路径等不再炸配置窗口）。
            // WPF 控件要求 STA 线程，单开线程构造并做一次布局测量（资源/样式/模板错误在此暴露）
            Exception? viewError = null;
            var sta = new Thread(() =>
            {
                try
                {
                    var vm = new CreateRoiPlugin();
                    var view = new CreateRoiView { DataContext = vm };
                    view.Measure(new System.Windows.Size(1000, 600));
                    view.Arrange(new System.Windows.Rect(0, 0, 1000, 600));
                    view.UpdateLayout();
                    vm.Dispose();
                }
                catch (Exception ex) { viewError = ex; }
            });
            sta.SetApartmentState(ApartmentState.STA);
            sta.Start();
            sta.Join();
            Check("【视图】CreateRoiView 实例化+布局无错（XAML 解析/资源/模板/绑定）", viewError == null,
                viewError?.Message ?? "");
        }

        // ==================================================================
        //  夹具
        // ==================================================================

        private static HImage MakeGray(byte val = 180)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", ImgW, ImgH);
            HOperatorSet.GenImageProto(proto, out HObject img, val);
            proto.Dispose();
            var image = new HImage(img);
            img.Dispose();
            return image;
        }

        private static HImage MakeGray3(byte val = 180)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", ImgW, ImgH);
            HOperatorSet.GenImageProto(proto, out HObject img, val);
            HOperatorSet.Compose3(img, img, img, out HObject img3);
            proto.Dispose();
            img.Dispose();
            var image = new HImage(img3);
            img3.Dispose();
            return image;
        }

        private static HRegion CircleRegion(double row, double col, double radius)
        {
            var r = new HRegion();
            r.GenCircle(row, col, radius);
            return r;
        }

        /// <summary>600 行不相邻的窄条区域：游程文本约 6600 字符，超过 4KB 阈值触发 gz 路径</summary>
        private static HRegion BigRunsRegion()
        {
            var rows = new List<int>();
            var c1 = new List<int>();
            var c2 = new List<int>();
            for (int r = 0; r < 600; r++)
            {
                rows.Add(r);
                c1.Add(10);
                c2.Add(11);
            }
            HOperatorSet.GenRegionRuns(out HObject obj,
                new HTuple(rows.ToArray()), new HTuple(c1.ToArray()), new HTuple(c2.ToArray()));
            var region = new HRegion(obj);
            obj.Dispose();
            return region;
        }

        private static void AddCanvasRoi(CreateRoiPlugin p, string name, DrawShapeType shape, double[] pars)
            => p.CanvasRois.Add(new DrawingObjectInfo(shape, pars.Select(v => new HTuple(v)).ToArray(), name));

        private static double GrayAt(HImage img, int row, int col)
        {
            HOperatorSet.GetGrayval(img, row, col, out HTuple g);
            return g.D;
        }

        private static double AreaOf(HRegion region)
        {
            HOperatorSet.AreaCenter(region, out HTuple area, out HTuple _, out HTuple _);
            return area.D;
        }

        private static (double row, double col) CenterOf(HRegion region)
        {
            HOperatorSet.AreaCenter(region, out HTuple _, out HTuple row, out HTuple col);
            return (row.D, col.D);
        }

        private static double DomainAreaOf(HImage img)
        {
            HOperatorSet.GetDomain(img, out HObject dom);
            using var d = new HRegion(dom);
            dom.Dispose();
            return AreaOf(d);
        }

        private static ExecutionContext NewContext(ILogService log)
            => new(log, new FlowSession { FlowName = "ROI插件断言" }, new WorkspaceContext(),
                new System.Threading.CancellationTokenSource().Token);

        /// <summary>测试用步骤数据：字典兜底的 IStepConfigData 最小实现</summary>
        private sealed class FakeStepData : IStepConfigData
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
}
