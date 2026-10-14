using Core.Events;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using Plugin.GeometryFinder.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Plugin.GeometryFinder
{
    /// <summary>
    /// 几何查找：在 ROI 内做亚像素找边并拟合，输出**类型化几何基元**（点/线/圆/椭圆），
    /// 供「几何计算」等下游消费——补上"平台只出 HXLD、无人能消费几何"的缺口。
    ///
    /// 算法（自包含，不依赖卡尺插件）：亚像素边缘提取（edges_sub_pix/canny）→ 按长度筛选 →
    /// 取最长轮廓 → 拟合直线/圆/椭圆（Tukey 抗离群）。
    ///
    /// 参数全走**输入端口**（无自定义视图；宿主 AutoPortConfigView 兜底渲染输入端口）。
    /// 基元为**图像像素系**、角度**度**、Phi 归一化 [0,180)；不乘像素当量（见方案说明书决策 5）。
    /// </summary>
    [Display(
        Name = "几何查找",
        GroupName = "几何工具",
        Description = "在 ROI 内亚像素找边并拟合，输出可复用的几何基元（直线/圆/椭圆）；供几何计算等下游消费",
        ShortName = "\uf140"
    )]
    [ParallelSafe] // 纯视觉计算，不写全局变量
    public class GeometryFinderPlugin : VisionPluginBase
    {
        #region 输入端口

        /// <summary>待查找图像（必接）</summary>
        public InputPort<HImage> SrcImage { get; } = new("SrcImage", description: "待查找图像");

        /// <summary>查找区域（可选）：只在区域内找；接 ROI 的 Domain_*；未接=整图</summary>
        public InputPort<HRegion> MaskRegion { get; } = new("MaskRegion", description: "查找区域（可选）：只在区域内找；未接=整图")
        { IsRequired = false };

        /// <summary>查找类型</summary>
        public InputPort<GeometryFindKind> Kind { get; } = new("Kind", GeometryFindKind.Line, "查找类型（直线/圆/椭圆）");

        /// <summary>边缘提取平滑 Sigma（越大越平滑）</summary>
        public InputPort<double> Sigma { get; } = new("Sigma", 1.0, "边缘提取平滑 Sigma（越大越平滑）")
        { IsRequired = false };

        /// <summary>边缘低阈值</summary>
        public InputPort<double> LowThreshold { get; } = new("LowThreshold", 20.0, "边缘低阈值")
        { IsRequired = false };

        /// <summary>边缘高阈值</summary>
        public InputPort<double> HighThreshold { get; } = new("HighThreshold", 40.0, "边缘高阈值")
        { IsRequired = false };

        /// <summary>参与拟合的轮廓长度下限（px）</summary>
        public InputPort<double> MinContourLength { get; } = new("MinContourLength", 30.0, "轮廓长度下限(px)")
        { IsRequired = false };

        /// <summary>拟合鲁棒算法</summary>
        public InputPort<FitAlgorithmKind> FitAlgorithm { get; } = new("FitAlgorithm", FitAlgorithmKind.Tukey, "拟合鲁棒算法")
        { IsRequired = false };

        /// <summary>找不到是否判失败（默认是；探测场景可关）</summary>
        public InputPort<bool> FailOnNotFound { get; } = new("FailOnNotFound", true, "找不到是否判失败（默认是）")
        { IsRequired = false };

        /// <summary>显示窗口（0=不显示，1~9=主界面视图窗口号；把结果图推送到该窗口）</summary>
        public InputPort<int> DisplayViewIndex { get; } = new("DisplayViewIndex", 1, "显示窗口(0=不显示,1~9=窗口号)")
        { IsRequired = false };

        #endregion

        #region 输出端口

        /// <summary>主输出：类型化几何基元（Kind 由查找类型定）</summary>
        public OutputPort<GeometryParam> Geometry { get; } = new("Geometry", "几何基元（线/圆/椭圆）");

        /// <summary>参与拟合的最长轮廓（XLD）</summary>
        public OutputPort<HXLD> Contours { get; } = new("Contours", "参与拟合的最长轮廓（XLD）");

        /// <summary>是否找到</summary>
        public OutputPort<bool> IsFound { get; } = new("IsFound", "是否找到");

        /// <summary>基准点行（线中点 / 圆心）</summary>
        public OutputPort<double> Row { get; } = new("Row", "基准点行（线中点/圆心）");

        /// <summary>基准点列</summary>
        public OutputPort<double> Col { get; } = new("Col", "基准点列");

        /// <summary>方向角（线/椭圆长轴，度，[0,180)）</summary>
        public OutputPort<double> Phi { get; } = new("Phi", "方向角（线/椭圆长轴，度，[0,180)）");

        /// <summary>半径（圆/椭圆长半轴）</summary>
        public OutputPort<double> Radius { get; } = new("Radius", "半径（圆/椭圆长半轴）");

        #endregion

        public override void RunAlgorithm(IExecutionContext context)
        {
            // 每轮开头显式重置非 IDisposable 输出（防失败轮下游读到上一轮脏值）
            Geometry.Value = null;
            IsFound.Value = false;
            Row.Value = 0;
            Col.Value = 0;
            Phi.Value = 0;
            Radius.Value = 0;

            var img = SrcImage.ActualValue;
            if (img == null || !img.IsInitialized())
            {
                Fail("几何查找：未接入图像（SrcImage 为空）");
                return;
            }

            var kind = Kind.ActualValue;
            var alg = FitAlgorithm.ActualValue;
            var sigma = Math.Max(0.4, Sigma.ActualValue);
            var low = Math.Max(1, LowThreshold.ActualValue);
            var high = Math.Max(low + 1, HighThreshold.ActualValue);
            var minLen = Math.Max(1, MinContourLength.ActualValue);

            HObject? edges = null, best = null, reduced = null;
            try
            {
                var region = MaskRegion.ActualValue;
                HObject source = img;
                if (region != null && region.IsInitialized())
                {
                    HOperatorSet.ReduceDomain(img, region, out reduced);
                    source = reduced;
                }

                edges = GeometryHService.EdgeContours(source, sigma, low, high);
                best = GeometryHService.LongestContour(edges, minLen, out _);

                if (best == null)
                {
                    Publish(context, img, new List<MeasureAnnotation>
                    {
                        new() { Type = MeasureType.Text, Points = new[] { 20.0, 20.0 }, Text = "未找到轮廓", Color = "red" }
                    }, new List<ImageInfoRow> { new("类型", KindLabel(kind)) });
                    if (FailOnNotFound.ActualValue)
                        Fail($"几何查找：未找到足够长的轮廓（长度下限 {minLen:0.#} px）——请检查 ROI/阈值/Sigma，或放宽长度下限");
                    return;
                }

                // 交给输出端口（独立包装；base 轮首自动回收）。包装后源对象仍由本方法释放。
                Contours.Value = new HXLDCont(best);

                var annotations = new List<MeasureAnnotation>();
                var info = new List<ImageInfoRow> { new("类型", KindLabel(kind)) };

                bool ok;
                switch (kind)
                {
                    case GeometryFindKind.Line:
                    {
                        ok = GeometryHService.TryFitLine(best, alg, out var lr, out var lc, out var lphi,
                            out var lr1, out var lc1, out var lr2, out var lc2);
                        if (ok)
                        {
                            Geometry.Value = GeometryParam.Line(lr, lc, lphi);
                            Row.Value = lr; Col.Value = lc; Phi.Value = lphi;
                            annotations.Add(new MeasureAnnotation { Type = MeasureType.Line, Points = new[] { lr1, lc1, lr2, lc2 }, Text = $"φ={lphi:0.##}°", Color = "green" });
                            info.Add(new("Phi(°)", lphi.ToString("0.##")));
                            info.Add(new("中点", $"({lr:0.#},{lc:0.#})"));
                        }
                        break;
                    }

                    case GeometryFindKind.Circle:
                    {
                        ok = GeometryHService.TryFitCircle(best, alg, out var cr, out var cc, out var crad);
                        if (ok)
                        {
                            Geometry.Value = GeometryParam.Circle(cr, cc, crad);
                            Row.Value = cr; Col.Value = cc; Radius.Value = crad;
                            annotations.Add(new MeasureAnnotation { Type = MeasureType.Polyline, Points = GeometryHService.CirclePoints(cr, cc, crad), Color = "green" });
                            annotations.Add(new MeasureAnnotation { Type = MeasureType.Point, Points = new[] { cr, cc }, Text = $"r={crad:0.##}", Color = "green" });
                            info.Add(new("半径", crad.ToString("0.##")));
                            info.Add(new("圆心", $"({cr:0.#},{cc:0.#})"));
                        }
                        break;
                    }

                    case GeometryFindKind.Ellipse:
                    {
                        ok = GeometryHService.TryFitEllipse(best, alg, out var er, out var ec, out var ephi, out var era, out var erb);
                        if (ok)
                        {
                            Geometry.Value = GeometryParam.Ellipse(er, ec, ephi, era, erb);
                            Row.Value = er; Col.Value = ec; Phi.Value = ephi; Radius.Value = era;
                            annotations.Add(new MeasureAnnotation { Type = MeasureType.Polyline, Points = GeometryHService.EllipsePoints(er, ec, ephi, era, erb), Color = "green" });
                            annotations.Add(new MeasureAnnotation { Type = MeasureType.Point, Points = new[] { er, ec }, Text = $"{era:0.#}×{erb:0.#}", Color = "green" });
                            info.Add(new("长半轴", era.ToString("0.##")));
                            info.Add(new("短半轴", erb.ToString("0.##")));
                        }
                        break;
                    }

                    default:
                        Fail($"几何查找：未知查找类型 {kind}");
                        return;
                }

                if (!ok)
                {
                    annotations.Add(new MeasureAnnotation { Type = MeasureType.Text, Points = new[] { 20.0, 20.0 }, Text = "未拟合到目标", Color = "red" });
                    Publish(context, img, annotations, info);
                    if (FailOnNotFound.ActualValue)
                        Fail($"几何查找：轮廓存在但拟合 {kind} 失败（点集退化/特征不是该形状）——请检查 ROI 是否套住目标、增大 Sigma 或轮廓长度下限");
                    return;
                }

                IsFound.Value = true;
                Publish(context, img, annotations, info);
            }
            finally
            {
                edges?.Dispose();
                best?.Dispose();
                reduced?.Dispose();
            }
        }

        public override void Initialize() { }
        public override void Dispose() { }

        private static string KindLabel(GeometryFindKind kind) => kind switch
        {
            GeometryFindKind.Line => "直线",
            GeometryFindKind.Circle => "圆",
            GeometryFindKind.Ellipse => "椭圆",
            _ => kind.ToString(),
        };

        /// <summary>
        /// 把源图 + 标注（拟合出的线/圆/椭圆 + 结果文字）+ 结果信息行发布到主界面视图窗口。
        /// 显示失败（无 WPF 宿主等）不影响算法结果，但**必须留一句 Warn**：
        /// 静默吞掉的话，"图上了屏却没标注"与"根本没发布"在日志里长得一模一样，现场无从下手。
        /// </summary>
        private void Publish(IExecutionContext context, HImage source, List<MeasureAnnotation> annotations, List<ImageInfoRow> info)
        {
            var viewIndex = DisplayViewIndex.ActualValue;
            if (source == null || viewIndex <= 0) return;
            if (viewIndex > 9) viewIndex = 9;
            try { this.PublishPreview(source, viewIndex, "几何查找", info, annotations); }
            catch (Exception ex)
            {
                context.Logger?.Warn($"{InstanceName} 预览发布失败（视图 {viewIndex}），算法结果不受影响：{ex.Message}");
            }
        }
    }
}
