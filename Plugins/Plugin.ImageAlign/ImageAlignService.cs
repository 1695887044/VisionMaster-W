using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Plugin.ImageAlign
{
    /// <summary>
    /// 图像对齐核心算法（纯 HALCON 调用，无状态）。
    ///
    /// 坐标约定（全文件统一）：
    ///  · 点对一律用 (Row, Column) 交错数组；
    ///  · <c>HomMat2D</c> = **当前图 → 基准帧**，直接喂 <c>projective_trans_image</c> 得到 AlignedImage；
    ///  · <c>InverseHomMat2D</c> = **基准帧 → 当前图**，下游把对齐图上的结果搬回原图；
    ///  · 6 元仿射统一补成 9 元（第三行 0,0,1），避免下游再分两种长度。
    ///
    /// 4 点透视没有冗余：4 对点刚好定 8 个自由度，**残差恒为 0**，单点错/遮挡无法从残差发现。
    /// 要检测错点必须给“验证点”（第 5 对点）或更多点；本类用 <c>validation</c> 参数做这件事。
    /// </summary>
    internal static class ImageAlignService
    {
        internal sealed class AlignFit
        {
            public HTuple HomMat2D = new();
            public HTuple InverseHomMat2D = new();
            public double Residual = double.NaN;   // 验证点残差或 RANSAC 内点 RMS；无则为 NaN
            public int UsedPointCount;
            public int InlierCount;
            public int OutlierCount;
            public string ModeText = string.Empty;
        }

        internal static AlignFit FitPointPairs(
            double[] refRows,
            double[] refCols,
            double[] curRows,
            double[] curCols,
            bool requireFour,
            double[]? validation,     // [refRow, refCol, curRow, curCol]
            double residualLimit,
            double inlierThreshold)
        {
            int n = refRows.Length;
            if (n != refCols.Length || n != curRows.Length || n != curCols.Length)
            {
                throw new InvalidOperationException("参考点与当前点的数量不一致。");
            }
            if (requireFour && n != 4)
            {
                throw new InvalidOperationException(
                    $"手动四点模式必须给 4 对点（当前 {n} 对）。少于 4 点无法确定透视，本插件不会退化成三点仿射。");
            }
            if (n < 2 || n > 12)
            {
                throw new InvalidOperationException($"点对数量 {n} 不支持：v1 接受 2~12 对（>4 对走 RANSAC）。");
            }

            if (n > 4)
            {
                return FitRansac(refRows, refCols, curRows, curCols, validation, residualLimit, inlierThreshold);
            }

            if (n == 2)
            {
                double dCur = Distance(curRows[0], curCols[0], curRows[1], curCols[1]);
                double dRef = Distance(refRows[0], refCols[0], refRows[1], refCols[1]);
                if (dCur < 1e-6 || dRef < 1e-6)
                {
                    throw new InvalidOperationException("两点刚性对齐失败：两个点重合（距离为 0），无法确定角度。");
                }
            }

            if (n == 3)
            {
                double area = Math.Abs(
                    (curRows[1] - curRows[0]) * (curCols[2] - curCols[0]) -
                    (curRows[2] - curRows[0]) * (curCols[1] - curCols[0]));
                if (area < 1e-6)
                {
                    throw new InvalidOperationException("三点仿射对齐失败：三个当前点共线（三角形面积为 0）。");
                }
            }

            HTuple hom;
            if (n == 2)
            {
                HOperatorSet.VectorToRigid(
                    new HTuple(curRows), new HTuple(curCols),
                    new HTuple(refRows), new HTuple(refCols),
                    out HTuple hom6);
                hom = hom6;
            }
            else if (n == 3)
            {
                HOperatorSet.VectorToHomMat2d(
                    new HTuple(curRows), new HTuple(curCols),
                    new HTuple(refRows), new HTuple(refCols),
                    out HTuple hom6);
                hom = hom6;
            }
            else
            {
                HOperatorSet.VectorToProjHomMat2d(
                    new HTuple(curRows), new HTuple(curCols),
                    new HTuple(refRows), new HTuple(refCols),
                    "normalized_dlt",
                    new HTuple(), new HTuple(), new HTuple(), new HTuple(), new HTuple(), new HTuple(),
                    out HTuple hom9, out HTuple _);
                hom = hom9;
            }

            HTuple homMat9 = ToHom9(hom);
            double det = Determinant(homMat9);
            if (Math.Abs(det) < 1e-12)
            {
                throw new InvalidOperationException(
                    "点对退化：拟合出的变换矩阵行列式接近 0（点共线/重合/分布过窄），无法稳定搬图。");
            }

            // 2/3 点是精确拟合，残差应≈0；若不为 0，说明坐标顺序/矩阵布局有问题，宁可红。
            if (n <= 3)
            {
                double max = 0.0;
                for (int i = 0; i < n; i++)
                {
                    var (qr, qc) = MapPoint(homMat9, curRows[i], curCols[i]);
                    max = Math.Max(max, Distance(qr, qc, refRows[i], refCols[i]));
                }
                if (max > 0.5)
                {
                    throw new InvalidOperationException(
                        $"点对自检失败：{n} 点精确拟合的残差 {max:F3}px 过大，点对或坐标顺序异常。");
                }
            }

            double residual = double.NaN;
            if (validation is { Length: 4 })
            {
                residual = MapResidual(homMat9, validation[0], validation[1], validation[2], validation[3]);
                if (residualLimit > 0 && residual > residualLimit)
                {
                    throw new InvalidOperationException(
                        $"验证点残差 {residual:F2}px 超过上限 {residualLimit:F2}px：点对可能错配/遮挡，或工件发生了非刚性变化。");
                }
            }

            return new AlignFit
            {
                HomMat2D = homMat9,
                InverseHomMat2D = Invert(homMat9),
                Residual = residual,
                UsedPointCount = n,
                InlierCount = n,
                OutlierCount = 0,
                ModeText = n == 2 ? "2 点刚性" : n == 3 ? "3 点仿射" : "4 点透视",
            };
        }

        /// <summary>
        /// >4 对点的确定性 RANSAC：枚举 4 点子集（最多 12 点，C(12,4)=495），
        /// 以“内点数最多、其次内点 RMS 最小”选最优；再用全部内点重拟合。
        /// 阈值单位是像素；内点不足 4 个直接失败。
        /// </summary>
        private static AlignFit FitRansac(
            double[] refRows, double[] refCols, double[] curRows, double[] curCols,
            double[]? validation, double residualLimit, double inlierThreshold)
        {
            int n = refRows.Length;
            double threshold = Math.Max(inlierThreshold, 0.01);
            HTuple? bestHom = null;
            bool[]? bestMask = null;
            int bestInliers = 0;
            double bestRms = double.MaxValue;

            for (int a = 0; a < n - 3; a++)
            for (int b = a + 1; b < n - 2; b++)
            for (int c = b + 1; c < n - 1; c++)
            for (int d = c + 1; d < n; d++)
            {
                var idx = new[] { a, b, c, d };
                HTuple hom;
                try
                {
                    hom = FitHomography(curRows, curCols, refRows, refCols, idx);
                }
                catch
                {
                    continue;
                }

                var mask = new bool[n];
                int inliers = 0;
                double sumSq = 0.0;
                for (int i = 0; i < n; i++)
                {
                    if (!TryResidualAt(hom, refRows[i], refCols[i], curRows[i], curCols[i], out double r))
                    {
                        mask[i] = false;
                        continue;
                    }
                    if (r <= threshold)
                    {
                        mask[i] = true;
                        inliers++;
                        sumSq += r * r;
                    }
                }
                if (inliers < 4) continue;
                double rms = Math.Sqrt(sumSq / inliers);
                if (inliers > bestInliers || (inliers == bestInliers && rms < bestRms))
                {
                    bestInliers = inliers;
                    bestRms = rms;
                    bestMask = mask;
                    bestHom = hom;
                }
            }

            if (bestHom == null || bestMask == null || bestInliers < 5)
            {
                throw new InvalidOperationException(
                    $"RANSAC 一致点不足：只找到 {bestInliers} 个（要求 ≥5，总 {n} 对，阈值 {threshold:F2}px）；"
                    + "只有 4 个一致点时请改用 4 点 + 验证点。");
            }

            // 用全部内点重拟合（>4 点走最小二乘/DLT）
            var inlierIdx = new List<int>();
            for (int i = 0; i < n; i++) if (bestMask[i]) inlierIdx.Add(i);
            HTuple finalHom = FitHomography(curRows, curCols, refRows, refCols, inlierIdx.ToArray());
            double det = Determinant(finalHom);
            if (Math.Abs(det) < 1e-12)
            {
                throw new InvalidOperationException("RANSAC 重拟合失败：内点分布退化（共线/重合/过窄）。");
            }

            double finalSumSq = 0.0;
            for (int i = 0; i < inlierIdx.Count; i++)
            {
                int k = inlierIdx[i];
                if (!TryResidualAt(finalHom, refRows[k], refCols[k], curRows[k], curCols[k], out double r))
                {
                    throw new InvalidOperationException("RANSAC 重拟合后点映射退化（齐次坐标 w≈0），请检查内点分布。");
                }
                finalSumSq += r * r;
            }
            double finalRms = Math.Sqrt(finalSumSq / inlierIdx.Count);

            double residual = finalRms;
            if (validation is { Length: 4 })
            {
                residual = MapResidual(finalHom, validation[0], validation[1], validation[2], validation[3]);
                if (residualLimit > 0 && residual > residualLimit)
                {
                    throw new InvalidOperationException(
                        $"验证点残差 {residual:F2}px 超过上限 {residualLimit:F2}px：点对可能错配/遮挡，或工件发生了非刚性变化。");
                }
            }

            return new AlignFit
            {
                HomMat2D = finalHom,
                InverseHomMat2D = Invert(finalHom),
                Residual = residual,
                UsedPointCount = n,
                InlierCount = inlierIdx.Count,
                OutlierCount = n - inlierIdx.Count,
                ModeText = $"RANSAC {inlierIdx.Count}/{n} 点透视",
            };
        }

        private static HTuple FitHomography(
            double[] curRows, double[] curCols, double[] refRows, double[] refCols, int[] idx)
        {
            var cr = new double[idx.Length];
            var cc = new double[idx.Length];
            var rr = new double[idx.Length];
            var rc = new double[idx.Length];
            for (int i = 0; i < idx.Length; i++)
            {
                cr[i] = curRows[idx[i]];
                cc[i] = curCols[idx[i]];
                rr[i] = refRows[idx[i]];
                rc[i] = refCols[idx[i]];
            }
            HOperatorSet.VectorToProjHomMat2d(
                new HTuple(cr), new HTuple(cc), new HTuple(rr), new HTuple(rc),
                "normalized_dlt",
                new HTuple(), new HTuple(), new HTuple(), new HTuple(), new HTuple(), new HTuple(),
                out HTuple hom, out HTuple _);
            return ToHom9(hom);
        }

        private static double ResidualAt(
            HTuple hom, double refRow, double refCol, double curRow, double curCol)
        {
            var (qr, qc) = MapPoint(hom, curRow, curCol);
            return Distance(qr, qc, refRow, refCol);
        }

        /// <summary>映射失败（w≈0）时返回 false，由 RANSAC 把该点当作外点，而不是让整轮失败。</summary>
        private static bool TryResidualAt(
            HTuple hom, double refRow, double refCol, double curRow, double curCol, out double residual)
        {
            try
            {
                residual = ResidualAt(hom, refRow, refCol, curRow, curCol);
                return true;
            }
            catch
            {
                residual = double.PositiveInfinity;
                return false;
            }
        }

        internal static AlignFit FitTemplatePose(
            double templateRefRow,
            double templateRefCol,
            double poseRow,
            double poseCol,
            double poseAngle,
            HTuple? upstreamHom)
        {
            HTuple homRefToCur;
            if (upstreamHom is { Length: > 0 })
            {
                homRefToCur = ToHom9(upstreamHom);
            }
            else
            {
                HOperatorSet.VectorAngleToRigid(
                    templateRefRow, templateRefCol, 0.0,
                    poseRow, poseCol, poseAngle,
                    out HTuple hom6);
                homRefToCur = ToHom9(hom6);
            }

            double det = Determinant(homRefToCur);
            if (Math.Abs(det) < 1e-12)
            {
                throw new InvalidOperationException("上游位姿退化：变换矩阵行列式接近 0，无法搬图。");
            }

            return new AlignFit
            {
                HomMat2D = Invert(homRefToCur),
                InverseHomMat2D = homRefToCur,
                Residual = double.NaN,
                UsedPointCount = 0,
                ModeText = "上游位姿",
            };
        }

        internal static (HImage Aligned, HRegion ValidRegion, double ValidAreaRatio) Warp(
            HImage image,
            HImage? reference,
            HTuple homCurrentToReference,
            string interpolation)
        {
            HOperatorSet.GetImageSize(image, out HTuple widthTuple, out HTuple heightTuple);
            int width = widthTuple[0].I;
            int height = heightTuple[0].I;

            HObject? destDomain = null;
            HObject? polygon = null;
            HObject? valid = null;
            try
            {
                if (reference != null)
                {
                    HOperatorSet.GetImageSize(reference, out HTuple rw, out HTuple rh);
                    if (rw[0].I != width || rh[0].I != height)
                    {
                        throw new InvalidOperationException(
                            $"ReferenceImage 尺寸 {rw[0].I}×{rh[0].I} 与 Image {width}×{height} 不一致；v1 不做输出尺寸缩放，请先统一尺寸。");
                    }
                    HOperatorSet.GetDomain(reference, out destDomain);
                }
                else
                {
                    HOperatorSet.GenRectangle1(out destDomain, 0, 0, height - 1, width - 1);
                }

                HOperatorSet.ProjectiveTransImage(
                    image, out HObject aligned, homCurrentToReference, interpolation, "false", "false");

                // 有效区：把输入图 4 个角点经同一个矩阵映射成四边形，再与目标域求交。
                // 不用 projective_trans_region：它的 AdaptRegion 参数在不同版本口径不一（实测 #1302）；
                // 四边形填充对仿射/透视都成立，且能直接得到几何有效区。
                double[] cornerRows = { 0, 0, height - 1, height - 1 };
                double[] cornerCols = { 0, width - 1, width - 1, 0 };
                var qRows = new double[4];
                var qCols = new double[4];
                for (int i = 0; i < 4; i++)
                {
                    var (qr, qc) = MapPoint(homCurrentToReference, cornerRows[i], cornerCols[i]);
                    qRows[i] = qr;
                    qCols[i] = qc;
                }
                HOperatorSet.GenRegionPolygonFilled(out HObject polygonObj, new HTuple(qRows), new HTuple(qCols));
                polygon = polygonObj;
                HOperatorSet.Intersection(polygon, destDomain, out valid);

                HOperatorSet.AreaCenter(valid, out HTuple areaTuple, out _, out _);
                double area = areaTuple.Length > 0 ? areaTuple[0].D : 0.0;
                double ratio = area / (width * (double)height);
                if (ratio <= 0.0)
                {
                    throw new InvalidOperationException(
                        "有效区为空：变换把整幅图移出画幅。请检查点对/位姿方向（是否把当前→基准写反了）。");
                }

                return (new HImage(aligned), new HRegion(valid), ratio);
            }
            finally
            {
                destDomain?.Dispose();
                polygon?.Dispose();
            }
        }

        internal static HTuple ToHom9(HTuple hom)
        {
            if (hom.Length == 9) return hom;
            if (hom.Length == 6)
            {
                return new HTuple(
                    hom[0].D, hom[1].D, hom[2].D,
                    hom[3].D, hom[4].D, hom[5].D,
                    0.0, 0.0, 1.0);
            }
            throw new InvalidOperationException($"变换矩阵长度 {hom.Length} 不支持（只接受 6 或 9）。");
        }

        internal static HTuple Invert(HTuple hom9)
        {
            HOperatorSet.HomMat2dInvert(hom9, out HTuple inv);
            return ToHom9(inv);
        }

        /// <summary>把当前图上的点映射到基准帧，返回 (row, col)。</summary>
        internal static (double Row, double Col) MapPoint(HTuple hom9, double row, double col)
        {
            double w = hom9[6].D * row + hom9[7].D * col + hom9[8].D;
            if (Math.Abs(w) < 1e-12)
            {
                throw new InvalidOperationException("点映射失败：齐次坐标 w 接近 0（点在无穷远/变换退化）。");
            }
            double qr = (hom9[0].D * row + hom9[1].D * col + hom9[2].D) / w;
            double qc = (hom9[3].D * row + hom9[4].D * col + hom9[5].D) / w;
            return (qr, qc);
        }

        internal static double MapResidual(
            HTuple hom9, double refRow, double refCol, double curRow, double curCol)
        {
            var (qr, qc) = MapPoint(hom9, curRow, curCol);
            return Distance(qr, qc, refRow, refCol);
        }

        private static double Determinant(HTuple h)
        {
            return h[0].D * (h[4].D * h[8].D - h[5].D * h[7].D)
                 - h[1].D * (h[3].D * h[8].D - h[5].D * h[6].D)
                 + h[2].D * (h[3].D * h[7].D - h[4].D * h[6].D);
        }

        private static double Distance(double r1, double c1, double r2, double c2)
        {
            double dr = r1 - r2;
            double dc = c1 - c2;
            return Math.Sqrt(dr * dr + dc * dc);
        }
    }
}
