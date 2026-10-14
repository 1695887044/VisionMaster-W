using HalconDotNet;
using System;

namespace Plugin.GeometryFinder.Services
{
    /// <summary>
    /// 几何查找的 HALCON 封装：亚像素轮廓提取 → 按长度筛选 → 取最长轮廓 → 拟合。
    /// 自包含（不依赖卡尺插件），只搬"标准 XLD 找边 + 拟合"链。
    /// 所有产出的非托管对象由调用方 Dispose；这里只负责调用与结果解包。
    /// </summary>
    internal static class GeometryHService
    {
        private const double RadToDeg = 180.0 / Math.PI;

        /// <summary>亚像素边缘提取（canny），返回 XLD 轮廓集合（调用方 Dispose）。</summary>
        public static HObject EdgeContours(HObject image, double sigma, double low, double high)
        {
            HOperatorSet.EdgesSubPix(image, out HObject edges, "canny", sigma, low, high);
            return edges;
        }

        /// <summary>取最长且长度 ≥ minLen 的轮廓（返回单个 XLD 对象；无满足者返回 null）。</summary>
        public static HObject? LongestContour(HObject contours, double minLen, out double length)
        {
            length = 0;
            HOperatorSet.CountObj(contours, out HTuple n);
            if (n.I <= 0) return null;

            HObject? best = null;
            double bestLen = -1;
            for (int i = 1; i <= n.I; i++)
            {
                HOperatorSet.SelectObj(contours, out HObject one, i);
                HOperatorSet.LengthXld(one, out HTuple l);
                if (l.D >= minLen && l.D > bestLen)
                {
                    best?.Dispose();
                    best = one;
                    bestLen = l.D;
                }
                else
                {
                    one.Dispose();
                }
            }
            length = bestLen > 0 ? bestLen : 0;
            return best;
        }

        /// <summary>拟合直线：返回锚点（中点）与方向角 Phi（度，[0,180)）以及线段两端点（标注用）；失败返回 false。</summary>
        public static bool TryFitLine(HObject contour, FitAlgorithmKind alg,
            out double row, out double col, out double phi,
            out double r1, out double c1, out double r2, out double c2)
        {
            row = col = phi = r1 = c1 = r2 = c2 = 0;
            try
            {
                HOperatorSet.FitLineContourXld(contour, LineAlgorithm(alg), -1, 0, 5, 2.0,
                    out HTuple rb, out HTuple cb, out HTuple re, out HTuple ce,
                    out HTuple _, out HTuple _, out HTuple _);
                r1 = rb.D; c1 = cb.D; r2 = re.D; c2 = ce.D;
                var dr = r2 - r1;
                var dc = c2 - c1;
                if (Math.Sqrt(dr * dr + dc * dc) < 1e-9) return false;   // 零长度
                row = (r1 + r2) / 2.0;
                col = (c1 + c2) / 2.0;
                phi = Core.Interfaces.GeometryParam.NormalizePhi(Math.Atan2(dr, dc) * RadToDeg);
                return true;
            }
            catch (HOperatorException)
            {
                return false;
            }
        }

        /// <summary>圆的轮廓采样点（[r0,c0,r1,c1,...]），用于折线标注。</summary>
        public static double[] CirclePoints(double row, double col, double radius, int n = 72)
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

        /// <summary>椭圆轮廓采样点（[r0,c0,...]）；长轴方向角 phiDeg（度），与线的方向角同口径。</summary>
        public static double[] EllipsePoints(double row, double col, double phiDeg, double major, double minor, int n = 72)
        {
            var phi = phiDeg * Math.PI / 180.0;
            var dr = Math.Sin(phi); var dc = Math.Cos(phi);      // 长轴方向
            var pr = Math.Cos(phi); var pc = -Math.Sin(phi);     // 短轴方向（垂直）
            var pts = new double[n * 2];
            for (int i = 0; i < n; i++)
            {
                var t = 2 * Math.PI * i / n;
                var a = major * Math.Cos(t);
                var b = minor * Math.Sin(t);
                pts[2 * i] = row + a * dr + b * pr;
                pts[2 * i + 1] = col + a * dc + b * pc;
            }
            return pts;
        }

        /// <summary>拟合圆：返回圆心与半径；失败返回 false。</summary>
        public static bool TryFitCircle(HObject contour, FitAlgorithmKind alg,
            out double row, out double col, out double radius)
        {
            row = col = radius = 0;
            HObject? closed = null;
            try
            {
                // 闭合轮廓（首点补到末尾）：fit_circle 的 maxClosureDist=0 要求闭合
                HOperatorSet.GetContourXld(contour, out HTuple rows, out HTuple cols);
                if (rows.Length < 3) return false;
                var r = new double[rows.Length + 1];
                var c = new double[cols.Length + 1];
                for (int i = 0; i < rows.Length; i++) { r[i] = rows[i].D; c[i] = cols[i].D; }
                r[rows.Length] = rows[0].D;
                c[cols.Length] = cols[0].D;
                HOperatorSet.GenContourPolygonXld(out closed, new HTuple(r), new HTuple(c));

                HOperatorSet.FitCircleContourXld(closed, CircleAlgorithm(alg), -1, 0, 0, 5, 2.0,
                    out HTuple rowC, out HTuple colC, out HTuple rad,
                    out HTuple _, out HTuple _, out HTuple _);
                row = rowC.D; col = colC.D; radius = rad.D;
                return double.IsFinite(radius) && radius > 0;
            }
            catch (HOperatorException)
            {
                return false;
            }
            finally
            {
                closed?.Dispose();
            }
        }

        /// <summary>拟合椭圆：返回中心、长轴方向 Phi（度）、长/短半轴；失败返回 false。</summary>
        public static bool TryFitEllipse(HObject contour, FitAlgorithmKind alg,
            out double row, out double col, out double phi, out double r1, out double r2)
        {
            row = col = phi = r1 = r2 = 0;
            HObject? closed = null;
            try
            {
                HOperatorSet.GetContourXld(contour, out HTuple rows, out HTuple cols);
                if (rows.Length < 5) return false;
                var r = new double[rows.Length + 1];
                var c = new double[cols.Length + 1];
                for (int i = 0; i < rows.Length; i++) { r[i] = rows[i].D; c[i] = cols[i].D; }
                r[rows.Length] = rows[0].D;
                c[cols.Length] = cols[0].D;
                HOperatorSet.GenContourPolygonXld(out closed, new HTuple(r), new HTuple(c));

                // fit_ellipse_contour_xld(Contours, Algorithm, MaxNumPoints, MaxClosureDist,
                //   ClippingEndPoints, VossTabSize, Iterations, ClippingFactor, out Row, Col, Phi, Radius1, Radius2, ...)
                HOperatorSet.FitEllipseContourXld(closed, "fitzgibbon", -1, 0, 0, 200, 3, 2.0,
                    out HTuple rowC, out HTuple colC, out HTuple phiT, out HTuple rad1, out HTuple rad2,
                    out HTuple _, out HTuple _, out HTuple _);
                row = rowC.D; col = colC.D;
                phi = Core.Interfaces.GeometryParam.NormalizePhi(phiT.D * RadToDeg);
                r1 = rad1.D; r2 = rad2.D;
                return double.IsFinite(r1) && r1 > 0 && double.IsFinite(r2) && r2 > 0;
            }
            catch (HOperatorException)
            {
                return false;
            }
            finally
            {
                closed?.Dispose();
            }
        }

        private static string LineAlgorithm(FitAlgorithmKind a) => a switch
        {
            FitAlgorithmKind.Regression => "regression",
            FitAlgorithmKind.Huber => "huber",
            _ => "tukey",
        };

        private static string CircleAlgorithm(FitAlgorithmKind a) => a switch
        {
            FitAlgorithmKind.Regression => "geometric",
            FitAlgorithmKind.Huber => "geohuber",
            _ => "geotukey",
        };
    }
}
