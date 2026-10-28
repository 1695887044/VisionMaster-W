using Core.Events;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using Plugin.GeometryRelation.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Plugin.GeometryRelation
{
    /// <summary>
    /// 几何计算：对上游几何基元做关系运算（距离 / 夹角 / 求交 / 垂足 / 平行垂直 / 圆关系 / 构造），
    /// 输出数值（Value）或新基元（Geometry）——基元结果可继续喂给下一个几何计算，构成"几何表达式树"。
    ///
    /// 与「几何查找」配套：查找出基元 → 计算算关系。纯托管数学（Services/GeometryMath.cs），不调 HALCON。
    ///
    /// 参数全走**输入端口**（不是 [StepConfig]）：本插件无自定义视图，宿主用 AutoPortConfigView
    /// 兜底渲染——而它**只渲染输入端口**，[StepConfig] 常量在界面上没有入口
    ///（见 AutoPortConfigView 注释：历史上"轴运动的绝对/相对切换"就是这么改不了的）。
    ///
    /// 单位口径：距离类数值 = 像素 × 像素当量；**未接当量（MmPerPixel ≤ 0）= 直接输出像素**（不假装是 mm）；
    /// 夹角 = 度（量纲无关，不受当量影响）。
    /// </summary>
    [Display(
        Name = "几何计算",
        GroupName = "几何工具",
        Description = "对上游几何基元做关系运算：距离/夹角/求交/垂足/平行垂直/圆关系/构造；输出数值或新基元",
        ShortName = "\uf542"
    )]
    [ParallelSafe] // 纯托管几何计算，不写全局变量
    public class GeometryRelationPlugin : VisionPluginBase
    {
        #region 输入端口

        /// <summary>关系模式</summary>
        public InputPort<GeometryOp> Op { get; } = new("Op", GeometryOp.Distance, "关系模式");

        /// <summary>几何 A（点/线/圆）——必接</summary>
        public InputPort<GeometryParam> A { get; } = new("A", null, "几何 A（点/线/圆）");

        /// <summary>几何 B（可选；多数模式需要）</summary>
        public InputPort<GeometryParam> B { get; } = new("B", null, "几何 B（可选）")
        { IsRequired = false };

        /// <summary>几何 C（可选；三点构圆用）</summary>
        public InputPort<GeometryParam> C { get; } = new("C", null, "几何 C（可选，三点构圆用）")
        { IsRequired = false };

        /// <summary>像素当量（mm/px）：接「标定」的 MmPerPixel；未接或 ≤0 = 数值输出像素</summary>
        public InputPort<double> MmPerPixel { get; } = new("MmPerPixel", 0.0, "像素当量(mm/px)：接标定；未接=输出像素")
        { IsRequired = false };

        /// <summary>判定标准值（默认 0）</summary>
        public InputPort<double> StandardValue { get; } = new("StandardValue", 0.0, "标准值（判定用）")
        { IsRequired = false };

        /// <summary>上公差（默认 0 = 零容忍）</summary>
        public InputPort<double> UpperTolerance { get; } = new("UpperTolerance", 0.0, "上公差（默认 0=零容忍）")
        { IsRequired = false };

        /// <summary>下公差（默认 0 = 零容忍）</summary>
        public InputPort<double> LowerTolerance { get; } = new("LowerTolerance", 0.0, "下公差（默认 0=零容忍）")
        { IsRequired = false };

        /// <summary>平行/垂直判定的角度容差（度）</summary>
        public InputPort<double> AngleTolerance { get; } = new("AngleTolerance", 0.1, "平行/垂直角度容差(度)")
        { IsRequired = false };

        /// <summary>平行/垂直判定目标</summary>
        public InputPort<LineRelationKind> RelationKind { get; } = new("RelationKind", LineRelationKind.Parallel, "平行/垂直")
        { IsRequired = false };

        /// <summary>构造方式（属性名不能叫 ConstructKind——会与枚举类型同名，字段初值里解析不到类型）</summary>
        public InputPort<ConstructKind> ConstructMode { get; } = new("ConstructMode", ConstructKind.LineThroughTwoPoints, "构造方式")
        { IsRequired = false };

        /// <summary>叠加显示用图像（可选）：接上游图像源，把结果画在它上面；不接=不显示</summary>
        public InputPort<HImage> SrcImage { get; } = new("SrcImage", null, "叠加显示用图像（可选）：接上游图像源")
        { IsRequired = false };

        /// <summary>显示窗口（0=不显示，1~9=主界面视图窗口号）</summary>
        public InputPort<int> DisplayViewIndex { get; } = new("DisplayViewIndex", 1, "显示窗口(0=不显示,1~9=窗口号)")
        { IsRequired = false };

        /// <summary>
        /// 残差用的轮廓点集（P1-B A2）：接「几何查找」的 Contours（HXLD）或上游任意点集，
        /// 本步骤把它摊成 [r0,c0,r1,c1,...] 再算各点到 A 的距离统计。
        /// · 不接 = 不算残差（ResidualRms/Max 保持 NaN，ResidualCount = 0），**不报错**——绝大多数流程不需要残差；
        /// · 接了但点集为空 / A 是 Point 之外的不可算基元（如 Contour）= **明确失败**，不许静默给 0。
        /// </summary>
        public InputPort<HObject> ResidualPoints { get; } = new("ResidualPoints", null, "残差点集（可选）：接「几何查找」的 Contours（XLD），算各点到 A 的残差统计")
        { IsRequired = false };

        #endregion

        #region 输出端口

        /// <summary>数值结果（距离 mm/px、夹角 度、圆心距、垂距…）</summary>
        public OutputPort<double> Value { get; } = new("Value", "数值结果");

        /// <summary>基元结果（交点/构造基元）；无基元输出的模式为 null</summary>
        public OutputPort<GeometryParam> Geometry { get; } = new("Geometry", "基元结果（交点/构造基元）");

        /// <summary>判定结果（true = OK）</summary>
        public OutputPort<bool> IsOk { get; } = new("IsOk", "判定结果（true = OK）");

        /// <summary>残差 RMS（像素）：参与点集到 A 的距离均方根；未接 ResidualPoints 或点集不可算时为 NaN</summary>
        public OutputPort<double> ResidualRms { get; } = new("ResidualRms", "残差均方根（像素；未接点集或不可算=NaN）");

        /// <summary>残差最大值（像素）</summary>
        public OutputPort<double> ResidualMax { get; } = new("ResidualMax", "残差最大值（像素）");

        /// <summary>参与残差统计的点数（0 = 没算）</summary>
        public OutputPort<int> ResidualCount { get; } = new("ResidualCount", "参与残差统计的点数（0=没算）");

        /// <summary>
        /// 本轮投射用的标注列表（托管对象，**不烘焙、不开窗**）。
        /// 接「结果显示」的任意结果项，由它把标注烧进最终展示图；SCADA 只认像素。
        /// </summary>
        public OutputPort<List<MeasureAnnotation>> Annotations { get; } =
            new("Annotations", "标注列表（接「结果显示」烧成图，供图像赋值/SCADA 使用）");

        #endregion

        public override void RunAlgorithm(IExecutionContext context)
        {
            // 每轮开头显式重置非 IDisposable 输出：失败/未命中轮下游必须读到空值，
            // 而不是上一轮的脏值（Value/Geometry/IsOk 都不受基类轮首回收保护）。
            Value.Value = 0;
            Geometry.Value = null;
            IsOk.Value = false;
            // 残差默认 NaN = "没算 / 算不了"。给 0 会让下游把"没算"当成"完美拟合"（A2 的静默陷阱）。
            ResidualRms.Value = double.NaN;
            ResidualMax.Value = double.NaN;
            ResidualCount.Value = 0;
            Annotations.Value = null!;

            var op = Op.ActualValue;
            var a = A.ActualValue;
            var b = B.ActualValue;
            var c = C.ActualValue;

            if (a == null)
            {
                Fail("几何计算：输入 A 为空（请接上游几何基元：几何查找 / 卡尺 FittedGeometry）");
                return;
            }

            if (!TryResolveScale(out var scale, out var scaleError))
            {
                Fail(scaleError);
                return;
            }

            switch (op)
            {
                case GeometryOp.Distance: DoDistance(a, b, scale); break;
                case GeometryOp.Angle: DoAngle(a, b); break;
                case GeometryOp.Intersect: DoIntersect(a, b); break;
                case GeometryOp.PerpendicularFoot: DoPerpendicularFoot(a, b, scale); break;
                case GeometryOp.LineRelation: DoLineRelation(a, b); break;
                case GeometryOp.CircleRelation: DoCircleRelation(a, b, scale); break;
                case GeometryOp.Construct: DoConstruct(a, b, c); break;
                default: Fail($"几何计算：未知模式 {op}"); break;
            }

            if (Success.Value is true)
            {
                // A2：残差在模式算完之后、发布之前算。失败不牵连主结果——
                // 残差是"拟合质量的旁证"，不是主判定；但**不可算时必须红着嘴说清楚**，不许静默 0。
                if (!TryComputeResidual(a, out var residualError))
                {
                    // 残差是可选诊断输入：读不出来只 Warn，不能把已经算好的主结果判失败
                    context.Logger.Warn($"几何计算：残差不可算，主结果仍输出：{residualError}");
                }

                Publish(a, b, op);
            }
        }

        public override void Initialize() { }
        public override void Dispose() { }

        #region 各模式

        private void DoDistance(GeometryParam a, GeometryParam b, double scale)
        {
            if (b == null) { Fail("几何计算：距离模式需要输入 B"); return; }

            double d;
            if (a.Kind == GeometryKind.Point && b.Kind == GeometryKind.Point)
                d = GeometryMath.DistancePointPoint(a, b);
            else if (a.Kind == GeometryKind.Point && b.Kind == GeometryKind.Line)
                d = GeometryMath.DistancePointLine(a, b);
            else if (a.Kind == GeometryKind.Line && b.Kind == GeometryKind.Point)
                d = GeometryMath.DistancePointLine(b, a);
            else if (a.Kind == GeometryKind.Line && b.Kind == GeometryKind.Line)
            {
                var tol = AngleTolerance.ActualValue;
                if (!GeometryMath.AreParallel(a, b, tol <= 0 ? 0.1 : tol))
                {
                    Fail("几何计算：距离模式的两条线不平行（平行线距才有意义）；如需非平行线关系请用「求交」");
                    return;
                }
                d = GeometryMath.DistancePointLine(GeometryParam.Point(b.Row, b.Col), a);
            }
            else
            {
                Fail($"几何计算：距离模式不支持 {a.Kind} 与 {b.Kind} 的组合（支持 点-点 / 点-线 / 平行线）");
                return;
            }

            var value = d * scale;
            Value.Value = value;
            Judge(value);
        }

        private void DoAngle(GeometryParam a, GeometryParam b)
        {
            if (b == null) { Fail("几何计算：夹角模式需要输入 B"); return; }
            if (a.Kind != GeometryKind.Line || b.Kind != GeometryKind.Line)
            {
                Fail($"几何计算：夹角模式需要两条线，实际收到 {a.Kind} 与 {b.Kind}");
                return;
            }
            var value = GeometryMath.AngleBetweenLines(a, b);
            Value.Value = value;
            Judge(value);
        }

        private void DoIntersect(GeometryParam a, GeometryParam b)
        {
            if (b == null) { Fail("几何计算：求交模式需要输入 B"); return; }

            bool ok;
            double row, col;
            if (a.Kind == GeometryKind.Line && b.Kind == GeometryKind.Line)
            {
                ok = GeometryMath.TryIntersectLines(a, b, out row, out col);
                if (!ok) { Fail("几何计算：两线平行（方向共线），没有唯一交点"); return; }
            }
            else if (a.Kind == GeometryKind.Line && b.Kind == GeometryKind.Circle)
            {
                ok = GeometryMath.TryIntersectLineCircle(a, b, out row, out col, out _);
                if (!ok) { Fail("几何计算：直线与圆不相交"); return; }
            }
            else if (a.Kind == GeometryKind.Circle && b.Kind == GeometryKind.Line)
            {
                ok = GeometryMath.TryIntersectLineCircle(b, a, out row, out col, out _);
                if (!ok) { Fail("几何计算：圆与直线不相交"); return; }
            }
            else if (a.Kind == GeometryKind.Circle && b.Kind == GeometryKind.Circle)
            {
                ok = GeometryMath.TryIntersectCircles(a, b, out row, out col, out _);
                if (!ok) { Fail("几何计算：两圆不相交或同心/内含"); return; }
            }
            else
            {
                Fail($"几何计算：求交模式不支持 {a.Kind} 与 {b.Kind}（支持 线-线 / 线-圆 / 圆-圆）");
                return;
            }

            Geometry.Value = GeometryParam.Point(row, col);
        }

        private void DoPerpendicularFoot(GeometryParam a, GeometryParam b, double scale)
        {
            if (b == null) { Fail("几何计算：垂足模式需要输入 B"); return; }

            GeometryParam point, line;
            if (a.Kind == GeometryKind.Point && b.Kind == GeometryKind.Line) { point = a; line = b; }
            else if (a.Kind == GeometryKind.Line && b.Kind == GeometryKind.Point) { point = b; line = a; }
            else { Fail($"几何计算：垂足模式需要 点+线，实际收到 {a.Kind} 与 {b.Kind}"); return; }

            var (row, col) = GeometryMath.PerpendicularFoot(point, line);
            Geometry.Value = GeometryParam.Point(row, col);
            Value.Value = GeometryMath.DistancePointLine(point, line) * scale;
        }

        private void DoLineRelation(GeometryParam a, GeometryParam b)
        {
            if (b == null) { Fail("几何计算：平行/垂直判定需要输入 B"); return; }
            if (a.Kind != GeometryKind.Line || b.Kind != GeometryKind.Line)
            {
                Fail($"几何计算：平行/垂直判定需要两条线，实际收到 {a.Kind} 与 {b.Kind}");
                return;
            }

            var tol = AngleTolerance.ActualValue;
            if (tol <= 0) tol = 0.1;
            Value.Value = GeometryMath.AngleBetweenLines(a, b);
            IsOk.Value = RelationKind.ActualValue == LineRelationKind.Parallel
                ? GeometryMath.AreParallel(a, b, tol)
                : GeometryMath.ArePerpendicular(a, b, tol);
        }

        private void DoCircleRelation(GeometryParam a, GeometryParam b, double scale)
        {
            if (b == null) { Fail("几何计算：圆关系需要输入 B"); return; }
            if (a.Kind != GeometryKind.Circle || b.Kind != GeometryKind.Circle)
            {
                Fail($"几何计算：圆关系需要两个圆，实际收到 {a.Kind} 与 {b.Kind}");
                return;
            }
            var value = GeometryMath.DistancePointPoint(a, b) * scale;   // 圆心距
            Value.Value = value;
            Judge(value);                                                // 同心 = 标准值 0 + 小公差
        }

        private void DoConstruct(GeometryParam a, GeometryParam b, GeometryParam c)
        {
            var kind = ConstructMode.ActualValue;
            if (kind == ConstructKind.LineThroughTwoPoints)
            {
                if (b == null || a.Kind != GeometryKind.Point || b.Kind != GeometryKind.Point)
                {
                    Fail("几何计算：过两点构线需要 A、B 都是点");
                    return;
                }
                Geometry.Value = GeometryMath.LineThroughTwoPoints(a, b);
            }
            else // CircleThroughThreePoints
            {
                if (b == null || c == null
                    || a.Kind != GeometryKind.Point || b.Kind != GeometryKind.Point || c.Kind != GeometryKind.Point)
                {
                    Fail("几何计算：三点构圆需要 A、B、C 都是点");
                    return;
                }
                if (!GeometryMath.TryCircleThroughThreePoints(a, b, c, out var circle))
                {
                    Fail("几何计算：三点共线，无法确定外接圆");
                    return;
                }
                Geometry.Value = circle;
            }
        }

        #endregion

        #region 判定与当量

        /// <summary>判定：Value ∈ [标准值−下公差, 标准值+上公差]（默认全 0 = 零容忍，未填即 NG，提示去填）</summary>
        private void Judge(double value)
        {
            var std = StandardValue.ActualValue;
            var up = UpperTolerance.ActualValue;
            var lo = LowerTolerance.ActualValue;
            const double eps = 1e-9;
            IsOk.Value = value >= std - lo - eps && value <= std + up + eps;
        }

        /// <summary>
        /// 像素当量取值（与卡尺 GetEffectivePixelSizeMm 同口径）：
        /// · 端口已连线 → 用上游值；上游值无效（≤0/NaN）→ **明确失败**（标定本轮可能失败，绝不静默回退）；
        /// · 端口未连线 → 用手填值；≤0 → 视为"未标定"，scale=1（**输出像素**，不假装 mm）。
        /// </summary>
        private bool TryResolveScale(out double scale, out string error)
        {
            scale = 1.0;
            error = string.Empty;

            if (MmPerPixel.LinkedSource != null)
            {
                var v = MmPerPixel.ActualValue;
                if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0)
                {
                    error = "几何计算：上游像素当量无效（≤0 / NaN / Inf），标定步骤本轮可能失败——请检查「标定」接线";
                    return false;
                }
                // 与卡尺 GetEffectivePixelSizeMm 同口径夹取（防极端值）
                scale = Math.Clamp(v, 1e-9, 1e6);
                return true;
            }

            var manual = MmPerPixel.ActualValue;
            scale = (double.IsNaN(manual) || manual <= 0) ? 1.0 : manual;
            return true;
        }

        /// <summary>
        /// 残差统计（P1-B A2）：把 ResidualPoints（HXLD / HRegion）摊成 [r,c,r,c,…]，算各点到 A 的距离统计。
        ///
        /// 三条口径（都是为了"不许静默"）：
        /// · **没接点集 = 合法跳过**（ResidualCount 保持 0、RMS/Max 保持 NaN），返回 true——绝大多数流程不看残差；
        /// · **接了但点集为空** → 失败，文案说清"接了点集但一个点都没有，多半是上游几何查找没找到"；
        /// · **A 的基元类型不可算残差**（Contour 等）→ 失败，文案列出支持的类型。
        ///
        /// 像素口径：残差恒以**像素**输出（拟合质量与物理当量无关），乘当量会让人误以为"残差越小越好"随单位变。
        /// </summary>
        private bool TryComputeResidual(GeometryParam a, out string error)
        {
            error = string.Empty;

            var pointsSource = ResidualPoints.ActualValue;
            if (pointsSource == null || !pointsSource.IsInitialized())
                return true;   // 没接 = 不算，合法

            if (a.Kind is not (GeometryKind.Point or GeometryKind.Line
                               or GeometryKind.Circle or GeometryKind.Ellipse))
            {
                error = $"几何计算：A 是 {a.Kind}，残差只支持 点/线/圆/椭圆（拟合基元）；请把拟合结果作为 A 接入";
                return false;
            }

            if (!TryGetPointArray(pointsSource, out var points, out var count))
            {
                error = "几何计算：残差点集无法读取（只支持 XLD 轮廓或区域，且必须有轮廓点）";
                return false;
            }

            if (count == 0)
            {
                error = "几何计算：残差点集为空（接了点集但一个点都没有）——多半是上游「几何查找」本轮没找到轮廓，请检查它的判定与阈值";
                return false;
            }

            var stats = GeometryMath.ResidualStats(a, points);
            if (stats.Count == 0)
            {
                error = "几何计算：残差点集里全部是无效坐标（NaN/Inf），无法统计";
                return false;
            }

            ResidualRms.Value = stats.Rms;
            ResidualMax.Value = stats.Max;
            ResidualCount.Value = stats.Count;

            if (stats.Skipped > 0)
            {
                // 跳过是"信息"，不失败；但要能看见，否则会有人对着少算的点集下结论。
                LastResidualSkipped = stats.Skipped;
            }

            return true;
        }

        /// <summary>本轮被跳过的无效点数（0 = 全部参与）；仅供排查，不进端口（避免下游误接）。</summary>
        internal int LastResidualSkipped { get; private set; }

        /// <summary>把 XLD 轮廓 / 区域轮廓摊成 [r,c,r,c,…]。区域先取边界再摊点。</summary>
        private static bool TryGetPointArray(HObject source, out double[] points, out int count)
        {
            points = Array.Empty<double>();
            count = 0;

            HObject? contours = null;
            var owned = false;
            try
            {
                // 不能写 source is HXLD：上游很多端口是 HObject（C# 脚本/BeD 轮廓/区域），
                // 算子返回的 XLD 也是 HObject，类型判断会恒 false → 被误当区域 → #4052。
                bool isXld = false;
                try
                {
                    HTuple classes = source.GetObjClass();
                    isXld = classes.Length > 0
                            && (classes[0].S ?? string.Empty)
                                .StartsWith("xld", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    // 取不到类名按区域处理；GenContourRegionXld 会对非区域明确失败
                }

                if (isXld)
                {
                    contours = source;
                }
                else
                {
                    // 区域 → 边界（XLD）。GenContourRegionXld 的 'border' 模式给区域外边界。
                    HOperatorSet.GenContourRegionXld(source, out var generated, "border");
                    contours = generated;
                    owned = true;
                }

                HOperatorSet.CountObj(contours, out var n);
                if (n.Length == 0 || n[0].I <= 0)
                    return true;   // 空：count 保持 0，由调用方判失败

                // 多对象 XLD / 多连通域：逐条取点后拼接。GetContourXld 对对象元组会报 #1501，
                // 不能直接一次调用。
                var all = new List<double>();
                for (int i = 1; i <= n[0].I; i++)
                {
                    HObject? one = null;
                    try
                    {
                        HOperatorSet.SelectObj(contours, out one, i);
                        HOperatorSet.GetContourXld(one, out var rows, out var cols);
                        int total = Math.Min(rows.Length, cols.Length);
                        for (int j = 0; j < total; j++)
                        {
                            all.Add(rows[j].D);
                            all.Add(cols[j].D);
                        }
                    }
                    finally
                    {
                        one?.Dispose();
                    }
                }

                points = all.ToArray();
                count = all.Count / 2;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (owned) contours?.Dispose();
            }
        }

        #endregion

        #region 结果标注与发布

        /// <summary>把源图 + 输入/结果基元 + 结果信息发布到主界面视图窗口（SrcImage 未接或窗口=0 则跳过）。</summary>
        private void Publish(GeometryParam a, GeometryParam b, GeometryOp op)
        {
            var src = SrcImage.ActualValue;
            if (src == null || !src.IsInitialized()) return;

            var viewIndex = DisplayViewIndex.ActualValue;

            var ann = new List<MeasureAnnotation>();
            AddGeometry(ann, a, src, "green");
            AddGeometry(ann, b, src, "green");
            AddGeometry(ann, Geometry.Value as GeometryParam, src, "red");

            ann.Add(new MeasureAnnotation
            {
                Type = MeasureType.Text,
                Points = new[] { 20.0, 20.0 },
                Text = $"{OpLabel(op)} = {Value.Value}",
                Color = IsOk.TypedValue ? "green" : "yellow"
            });

            // 标注先交端口：显示窗口=0 也要给「结果显示」合成最终图用
            Annotations.Value = ann;

            if (viewIndex <= 0) return;
            if (viewIndex > 9) viewIndex = 9;

            var info = new List<ImageInfoRow>
            {
                new("模式", OpLabel(op)),
                new("结果", Value.Value?.ToString() ?? ""),
                new("判定", IsOk.TypedValue ? "OK" : "NG"),
            };
            try { this.PublishPreview(src, viewIndex, "几何计算", info, ann); }
            catch { /* 显示层异常不拖垮流程 */ }
        }

        private static void AddGeometry(List<MeasureAnnotation> ann, GeometryParam? g, HImage img, string color)
        {
            if (g == null) return;
            switch (g.Kind)
            {
                case GeometryKind.Point:
                    ann.Add(new MeasureAnnotation { Type = MeasureType.Point, Points = new[] { g.Row, g.Col }, Text = "P", Color = color });
                    break;

                case GeometryKind.Line:
                {
                    // 基元只带 (Row,Col,Phi)，没有端点：以锚点为中心、沿方向画一段（长度取图对角线的 1/4）
                    img.GetImageSize(out int w, out int h);
                    var len = Math.Sqrt((double)w * w + (double)h * h) / 4.0;
                    var (dr, dc) = GeometryMath.Direction(g.Phi);
                    ann.Add(new MeasureAnnotation
                    {
                        Type = MeasureType.Line,
                        Points = new[] { g.Row - len * dr, g.Col - len * dc, g.Row + len * dr, g.Col + len * dc },
                        Text = $"φ={g.Phi:0.##}°",
                        Color = color
                    });
                    break;
                }

                case GeometryKind.Circle:
                    ann.Add(new MeasureAnnotation { Type = MeasureType.Polyline, Points = CirclePoints(g.Row, g.Col, g.Radius), Color = color });
                    break;

                case GeometryKind.Ellipse:
                    ann.Add(new MeasureAnnotation { Type = MeasureType.Polyline, Points = EllipsePoints(g.Row, g.Col, g.Phi, g.Radius, g.Radius2), Color = color });
                    break;
            }
        }

        private static double[] CirclePoints(double row, double col, double radius, int n = 72)
        {
            var pts = new double[n * 2];
            for (int i = 0; i < n; i++)
            {
                var t = 2 * Math.PI * i / n;
                pts[2 * i] = row + radius * Math.Sin(t);
                pts[2 * i + 1] = col + radius * Math.Cos(t);
            }
            return pts;
        }

        private static double[] EllipsePoints(double row, double col, double phiDeg, double major, double minor, int n = 72)
        {
            var phi = phiDeg * Math.PI / 180.0;
            var dr = Math.Sin(phi); var dc = Math.Cos(phi);
            var pr = Math.Cos(phi); var pc = -Math.Sin(phi);
            var pts = new double[n * 2];
            for (int i = 0; i < n; i++)
            {
                var t = 2 * Math.PI * i / n;
                var a = major * Math.Cos(t);
                var bb = minor * Math.Sin(t);
                pts[2 * i] = row + a * dr + bb * pr;
                pts[2 * i + 1] = col + a * dc + bb * pc;
            }
            return pts;
        }

        private static string OpLabel(GeometryOp op) => op switch
        {
            GeometryOp.Distance => "距离",
            GeometryOp.Angle => "夹角",
            GeometryOp.Intersect => "求交",
            GeometryOp.PerpendicularFoot => "垂足",
            GeometryOp.LineRelation => "平行/垂直",
            GeometryOp.CircleRelation => "圆关系",
            GeometryOp.Construct => "构造",
            _ => op.ToString(),
        };

        #endregion
    }
}
