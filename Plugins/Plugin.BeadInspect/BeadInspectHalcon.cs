using HalconDotNet;

namespace Plugin.BeadInspect
{
    /// <summary>
    /// 骨架段（树直径合并的输入单元）：一段骨架轮廓的点列与弧长。
    /// </summary>
    public sealed class BeadSeg
    {
        public double[] R;
        public double[] C;
        public double Len;

        public BeadSeg(double[] r, double[] c, double len)
        {
            R = r;
            C = c;
            Len = len;
        }

        public BeadSeg Reversed()
        {
            var r = (double[])R.Clone();
            var c = (double[])C.Clone();
            Array.Reverse(r);
            Array.Reverse(c);
            return new BeadSeg(r, c, Len);
        }
    }

    /// <summary>
    /// 胶路检测 · 纯算子层（方案说明书 §8.1：算法与界面分离，未来抽 Plugin.PlanarAlign 时直接搬）。
    ///
    /// 全部 static、只依赖 HALCON，不持有任何跨轮状态——断言可直呼，插件负责生命周期。
    /// 算法与参数全部来自 tools\BeadProbe 的实测结论（方案说明书 §7.1 P1~P12）：
    ///   · 参考路径用折线 gen_contour_polygon_xld（P1）；
    ///   · 分割优先「参考图差分」，回退 black-hat（P6）；
    ///   · 骨架转轮廓固定 mode='filter'（P8）、length=5；
    ///   · 分支合并必须用「树直径」（P6：只取最长覆盖仅 68.9%，树直径 98.2%）；
    ///   · 点列简化用托管侧等距抽稀（P7：gen_polygons_xld 本版本全 type 报 #1301，不可用）；
    ///   · 贴合度用托管侧有向 Hausdorff（P11 踩坑：distance_cc 的 max 是全局最大点对距离，不是偏离量）。
    /// </summary>
    public static class BeadInspectHalcon
    {
        // ==================================================================
        //  参考路径与 bead 模型
        // ==================================================================

        /// <summary>
        /// 参考路径点列 → 折线 XLD（gen_contour_polygon_xld，P1 实测口径）。
        /// 点数 &lt; 2 / 各点几乎重合时返回 false 并给中文原因（§5.2 话术）。
        /// </summary>
        public static bool TryBuildContour(double[] rows, double[] cols, out HObject? contour, out string error)
        {
            contour = null;
            error = string.Empty;
            if (rows == null || cols == null || rows.Length != cols.Length)
            {
                error = "参考路径点列无效（行列数不一致）";
                return false;
            }
            if (rows.Length < 2)
            {
                error = "参考路径至少需要 2 个点：请在配置界面拾取或导入胶路中心线";
                return false;
            }
            double total = 0;
            for (int i = 1; i < rows.Length; i++)
                total += Dist(rows[i - 1], cols[i - 1], rows[i], cols[i]);
            if (total < 1e-3)
            {
                error = "参考路径退化（各点几乎重合）：无法生成有效曲线";
                return false;
            }
            try
            {
                HOperatorSet.GenContourPolygonXld(out HObject c, new HTuple(rows), new HTuple(cols));
                contour = c;
                return true;
            }
            catch (Exception ex)
            {
                error = $"生成参考路径曲线失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>建 bead 检测模型（create_bead_inspection_model）。polarity 只收 'dark'/'light'（硬约束）</summary>
        public static HTuple CreateBeadModel(
            HObject contour,
            double targetWidth,
            double widthTolerance,
            double positionTolerance,
            string polarity)
        {
            // 硬约束（P10 实测）：target_thickness < 6 报 HALCON #3716——
            // 入参在插件层已被 [StepConfig] 钩子夹下限，这里再守一道，把算子错误翻译成中文。
            if (targetWidth < 6)
                throw new ArgumentException($"目标胶宽 {targetWidth:0.#} 低于 HALCON 合法下界 6（#3716）：请把「目标胶宽」调到 ≥ 6");
            HOperatorSet.CreateBeadInspectionModel(
                contour, targetWidth, widthTolerance, positionTolerance, polarity,
                new HTuple(), new HTuple(), out HTuple model);
            return model;
        }

        /// <summary>检测：apply_bead_inspection_model → 左右轮廓 + 错误段 + 错误类型</summary>
        public static void ApplyBead(
            HObject aligned,
            HTuple beadModel,
            out HObject left,
            out HObject right,
            out HObject errorSegment,
            out HTuple errorType)
        {
            HOperatorSet.ApplyBeadInspectionModel(
                aligned, out HObject l, out HObject r, out HObject es, beadModel, out HTuple et);
            left = l;
            right = r;
            errorSegment = es;
            errorType = et;
        }

        // ==================================================================
        //  平面可变形对齐（prepare_alignment + align 两段，范例口径）
        // ==================================================================

        /// <summary>
        /// 学习段：参考图 →（可选矫正）→ 平面区提取 → create_planar_uncalib_deformable_model。
        /// 返回的句柄/对象归调用方所有与释放（ClearPlanarUncalibDeformableModel + Dispose）。
        ///
        /// 平面区口径（范例 prepare_alignment，探针 B2 实测）：smooth_histo 取亮 → opening_circle(5.5)
        /// → connection → 最大连通域 → fill_up → dilation_circle(5.5)；锚点 (RowT, ColT) = 平面区质心。
        /// </summary>
        /// <param name="refImage">参考图（从 RefImagePath 载入）</param>
        /// <param name="quadRows">矫正四点 Row（null 或长度≠4 = 不矫正，P2：非必需但更稳）</param>
        /// <param name="quadCols">矫正四点 Col</param>
        /// <param name="dstRows">目标四点 Row（null = 由源四点外接矩形 + 20px 边距自动推导）</param>
        /// <param name="dstCols">目标四点 Col</param>
        /// <param name="planarModel">平面可变形模型句柄</param>
        /// <param name="rowT">对齐锚点 Row（运行期 hom_mat2d_translate 用）</param>
        /// <param name="colT">对齐锚点 Col</param>
        /// <param name="rectifiedRef">矫正后的参考图（未矫正时为 null；自动提取差分用）</param>
        /// <param name="plane">平面区（提取中心线的分割域）</param>
        /// <param name="error">失败原因（中文）</param>
        public static bool PrepareAlignment(
            HObject refImage,
            double[]? quadRows,
            double[]? quadCols,
            double[]? dstRows,
            double[]? dstCols,
            out HTuple planarModel,
            out double rowT,
            out double colT,
            out HObject? rectifiedRef,
            out HObject? plane,
            out string error)
        {
            planarModel = new HTuple();
            rowT = 0;
            colT = 0;
            rectifiedRef = null;
            plane = null;
            error = string.Empty;

            var temp = new List<HObject>();
            HObject? keptPlane = null;    // 交接给调用方的平面区（finally 里从 temp 摘掉）
            HObject? keptRect = null;     // 交接给调用方的矫正参考图（同上）
            try
            {
                HObject baseImage = refImage;
                if (quadRows is { Length: 4 } && quadCols is { Length: 4 })
                {
                    var dR = dstRows;
                    var dC = dstCols;
                    if (dR is not { Length: 4 } || dC is not { Length: 4 })
                        (dR, dC) = DeriveRectifyTarget(quadRows, quadCols);

                    HOperatorSet.VectorToProjHomMat2d(
                        new HTuple(quadRows), new HTuple(quadCols), new HTuple(dR), new HTuple(dC),
                        "normalized_dlt",
                        new HTuple(), new HTuple(), new HTuple(), new HTuple(), new HTuple(), new HTuple(),
                        out HTuple homRect, out _);
                    HOperatorSet.ProjectiveTransImage(refImage, out HObject rect, homRect, "bilinear", "false", "false");
                    temp.Add(rect);
                    keptRect = rect;
                    rectifiedRef = rect;
                    baseImage = rect;
                }

                // ── 平面区（范例 prepare_alignment 口径）──
                HOperatorSet.BinaryThreshold(baseImage, out HObject region, "smooth_histo", "light", out _);
                temp.Add(region);
                HOperatorSet.OpeningCircle(region, out HObject opened, 5.5);
                temp.Add(opened);
                HOperatorSet.Connection(opened, out HObject conn);
                temp.Add(conn);
                HOperatorSet.AreaCenter(conn, out HTuple areas, out _, out _);
                HOperatorSet.TupleNeg(areas, out HTuple negAreas);
                HOperatorSet.TupleSortIndex(negAreas, out HTuple sortIdx);
                // 注意 SelectObj 的对象序号是第 3 个参数（探针编译期踩过：写反会静默取错对象）
                HOperatorSet.SelectObj(conn, out HObject biggest, sortIdx[0] + 1);
                HOperatorSet.FillUp(biggest, out HObject filled);
                HOperatorSet.DilationCircle(filled, out HObject planeRegion, 5.5);
                temp.Add(biggest);
                temp.Add(filled);
                temp.Add(planeRegion);
                keptPlane = planeRegion;
                plane = planeRegion;

                HOperatorSet.AreaCenter(planeRegion, out _, out HTuple rT, out HTuple cT);
                rowT = rT.D;
                colT = cT.D;

                // ── 平面模型（P5 实测 100~360ms：必须由调用方缓存）──
                // 参数照抄范例 create 口径（numLevels="auto"、正反缩放 1、use_polarity、contrast auto）
                HOperatorSet.ReduceDomain(baseImage, planeRegion, out HObject reduced);
                temp.Add(reduced);
                HOperatorSet.CreatePlanarUncalibDeformableModel(
                    reduced, "auto", new HTuple(), new HTuple(),
                    "auto", 1, new HTuple(), "auto", 1, new HTuple(), "auto",
                    "none", "use_polarity", "auto", "auto",
                    new HTuple(), new HTuple(), out HTuple model);
                planarModel = model;
                return true;
            }
            catch (Exception ex)
            {
                error = $"平面模型准备失败：{ex.Message}";
                plane = null;
                rectifiedRef = null;
                keptPlane = null;
                keptRect = null;
                planarModel = new HTuple();
                return false;
            }
            finally
            {
                // 交接出去的对象（plane / rectifiedRef）先从 temp 摘掉，其余统一释放
                if (keptPlane != null) temp.Remove(keptPlane);
                if (keptRect != null) temp.Remove(keptRect);
                foreach (var o in temp)
                {
                    try { o?.Dispose(); } catch { /* 释放失败不阻断 */ }
                }
            }
        }

        /// <summary>
        /// 运行段：find_planar_uncalib_deformable_model → hom_mat2d_invert + translate(RowT, ColT)
        /// → projective_trans_image，把图搬回参考位姿。
        /// 角度/缩放范围由调用方传入（[StepConfig] 可配置，换工位不改代码）；
        /// minScore/numLevels 也由插件参数给。
        /// 未命中（score &lt; minScore）返回 false，error 按 §5.2 话术。
        /// </summary>
        public static bool AlignImage(
            HObject image,
            HTuple planarModel,
            double rowT,
            double colT,
            double minScore,
            int numLevels,
            double angleStart,
            double angleExtent,
            double scaleRMin, double scaleRMax,
            double scaleCMin, double scaleCMax,
            out HObject? aligned,
            out double score,
            out string error)
        {
            aligned = null;
            score = 0;
            error = string.Empty;
            try
            {
                HOperatorSet.FindPlanarUncalibDeformableModel(
                    image, planarModel,
                    angleStart, angleExtent, scaleRMin, scaleRMax, scaleCMin, scaleCMax,
                    minScore, 1, 1, numLevels, 0.9,
                    new HTuple(), new HTuple(),
                    out HTuple hom, out HTuple foundScore);
                if (foundScore == null || foundScore.Length == 0)
                {
                    // P2-1：0 命中时 score 还是初始值不是实测分，不能拿去编「低于阈值」——
                    // 两种失败分开给话术，各自指向对应的下一步排查方向。
                    error = "图像对齐失败：平面匹配未找到匹配实例：请检查参考图与当前图是否为同一平面、"
                            + "对齐模式与矫正设置是否正确";
                    return false;
                }
                score = foundScore[0].D;
                if (score < minScore)
                {
                    error = $"图像对齐失败：平面匹配分数 {score:0.00} 低于阈值 {minScore:0.00}，"
                            + "请检查参考图与当前图是否为同一平面";
                    return false;
                }
                HOperatorSet.HomMat2dInvert(hom, out HTuple homInv);
                HOperatorSet.HomMat2dTranslate(homInv, rowT, colT, out HTuple homT);
                HOperatorSet.ProjectiveTransImage(image, out HObject trans, homT, "bilinear", "false", "false");
                aligned = trans;
                return true;
            }
            catch (Exception ex)
            {
                error = $"图像对齐失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 自动推导矫正目标矩形：源四点外接矩形 + 20px 边距，按「上/下、左/右」把每个源点
        /// 映射到目标矩形的对应角（与源点顺序无关）。
        /// </summary>
        public static (double[] rows, double[] cols) DeriveRectifyTarget(double[] srcRows, double[] srcCols)
        {
            const double margin = 20;
            double rMin = srcRows.Min(), rMax = srcRows.Max();
            double cMin = srcCols.Min(), cMax = srcCols.Max();
            double rMid = (rMin + rMax) / 2, cMid = (cMin + cMax) / 2;
            var dstR = new double[4];
            var dstC = new double[4];
            for (int i = 0; i < 4; i++)
            {
                dstR[i] = srcRows[i] < rMid ? margin : margin + (rMax - rMin);
                dstC[i] = srcCols[i] < cMid ? margin : margin + (cMax - cMin);
            }
            return (dstR, dstC);
        }

        // ==================================================================
        //  自动提取中心线（§6.4 算法链，探针 StageF/I/K 已验证口径）
        // ==================================================================

        /// <summary>
        /// 「参考图差分」分割胶条（P6 口径①，σ=0.96px 最紧）：
        /// ReduceDomain 到平面区 → AbsDiffImage(当前, 无胶参考) → BinaryThreshold('smooth_histo','light')
        /// → closing_circle(3.5) → 最大连通域。
        /// ⚠ 分割前必须 ReduceDomain：对齐后的图带投影黑边，任何整图 smooth_histo 都会被黑边带偏
        /// （实测阈值被推到 231）。
        /// </summary>
        public static bool SegmentBeadByRefDiff(
            HObject aligned,
            HObject refAligned,
            HObject plane,
            out HObject? bead,
            out double threshold,
            out string error)
        {
            bead = null;
            threshold = 0;
            error = string.Empty;
            try
            {
                HOperatorSet.ReduceDomain(aligned, plane, out HObject a);
                HOperatorSet.ReduceDomain(refAligned, plane, out HObject b);
                HOperatorSet.AbsDiffImage(a, b, out HObject diff, 1.0);
                HOperatorSet.BinaryThreshold(diff, out HObject raw, "smooth_histo", "light", out HTuple th);
                bead = LargestComponent(raw, 3.5);
                threshold = th.D;
                a.Dispose();
                b.Dispose();
                diff.Dispose();
                raw.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                error = $"参考图差分分割失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// black-hat 回退分割（无无胶参考图时用，P6 口径②，只覆盖部分胶条）：
        /// GrayClosingRect(15,15) − 原图 → BinaryThreshold('light') → 最大连通域。
        /// 为什么不用直接取暗：平面区里含大块暗区（拱形开口实测 65323px），直接 'dark' 会整块误判成胶。
        /// </summary>
        public static bool SegmentBeadBlackHat(
            HObject aligned,
            HObject plane,
            out HObject? bead,
            out double threshold,
            out string error)
        {
            bead = null;
            threshold = 0;
            error = string.Empty;
            try
            {
                HOperatorSet.ReduceDomain(aligned, plane, out HObject planeImg);
                HOperatorSet.GrayClosingRect(planeImg, out HObject closed, 15, 15);
                HOperatorSet.SubImage(closed, planeImg, out HObject blackHat, 1.0, 0);
                HOperatorSet.BinaryThreshold(blackHat, out HObject raw, "smooth_histo", "light", out HTuple th);
                bead = LargestComponent(raw, 3.5);
                threshold = th.D;
                planeImg.Dispose();
                closed.Dispose();
                blackHat.Dispose();
                raw.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                error = $"black-hat 分割失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>闭运算去噪后取最大连通域（胶条分割两口径共用）</summary>
        private static HObject LargestComponent(HObject raw, double closeRadius)
        {
            HOperatorSet.ClosingCircle(raw, out HObject closed, closeRadius);
            HOperatorSet.Connection(closed, out HObject conn);
            HOperatorSet.AreaCenter(conn, out HTuple areas, out _, out _);
            HOperatorSet.TupleNeg(areas, out HTuple neg);
            HOperatorSet.TupleSortIndex(neg, out HTuple idx);
            HOperatorSet.SelectObj(conn, out HObject biggest, idx[0] + 1);
            closed.Dispose();
            conn.Dispose();
            return biggest;
        }

        /// <summary>
        /// 自动提取中心线（探针 StageI/K 的最终验证链路）：
        /// 差分分割 → Skeleton → GenContoursSkeletonXld(5,'filter')（P8：mode 只有 'filter' 合法）
        /// → <b>树直径合并</b>（P6 硬要求：只取最长覆盖仅 68.9%，未覆盖处会误判「缺胶」）
        /// → 相邻重复点去重 → 托管侧等距抽稀到 targetPoints 点（P7：gen_polygons_xld 本版本不可用）。
        ///
        /// 说明：探针 StageI 的最终验证链路未做 SmoothContoursXld（§6.4 ⑦），P11/P12 的实测数字
        /// （覆盖率 98.2%、平均偏离 1.98/2.18px）均来自未平滑的合并路径——为忠实复刻已验证口径，
        /// 这里同样不做平滑；后续若加，必须回归断言 13/15。
        /// </summary>
        /// <param name="aligned">已对齐的良品图</param>
        /// <param name="refAligned">已对齐的无胶参考图（差分用；null 时内部回退 black-hat）</param>
        /// <param name="plane">平面区（分割域，防投影黑边带偏阈值）</param>
        /// <param name="targetPoints">抽稀目标点数（40~80，P12：点数对检出几乎无影响）</param>
        /// <param name="rows">提取出的中心线点列 Row</param>
        /// <param name="cols">中心线点列 Col</param>
        /// <param name="skeletonSegmentCount">骨架段数（&gt;1 即存在分叉，树直径合并才有意义）</param>
        /// <param name="mergedCoverage">树直径合并后覆盖的骨架长度占比（实测 98.2%）</param>
        /// <param name="longestOnlyCoverage">「只取最长一条」的覆盖率（实测 68.9%，对照用）</param>
        /// <param name="error">失败原因（中文）</param>
        public static bool ExtractCenterline(
            HObject aligned,
            HObject? refAligned,
            HObject plane,
            int targetPoints,
            out double[] rows,
            out double[] cols,
            out int skeletonSegmentCount,
            out double mergedCoverage,
            out double longestOnlyCoverage,
            out string error)
        {
            rows = Array.Empty<double>();
            cols = Array.Empty<double>();
            skeletonSegmentCount = 0;
            mergedCoverage = 0;
            longestOnlyCoverage = 0;
            error = string.Empty;

            HObject? bead = null;
            HObject? skel = null;
            HObject? contours = null;
            try
            {
                bool segOk = refAligned != null
                    ? SegmentBeadByRefDiff(aligned, refAligned!, plane, out bead, out _, out error)
                    : SegmentBeadBlackHat(aligned, plane, out bead, out _, out error);
                if (!segOk || bead == null)
                {
                    error = string.IsNullOrEmpty(error) ? "未能分割出胶条" : error;
                    return false;
                }

                HOperatorSet.Skeleton(bead, out HObject sk);
                skel = sk;
                // P8：gen_contours_skeleton_xld 的 mode 只有 'filter' 合法（其余报 #1302），length=5 丢短枝
                HOperatorSet.GenContoursSkeletonXld(sk, out HObject cs, 5, "filter");
                contours = cs;
                HOperatorSet.CountObj(cs, out HTuple nC);
                int n = nC.I;
                var segs = new List<BeadSeg>(n);
                for (int i = 1; i <= n; i++)
                {
                    HOperatorSet.SelectObj(cs, out HObject s, i); // 对象序号是第 3 参
                    HOperatorSet.GetContourXld(s, out HTuple sr, out HTuple sc);
                    HOperatorSet.LengthXld(s, out HTuple sl);
                    segs.Add(new BeadSeg(sr.ToDArr(), sc.ToDArr(), sl.D));
                    s.Dispose();
                }

                skeletonSegmentCount = segs.Count;
                double total = segs.Sum(s => s.Len);
                if (segs.Count == 0 || total < 1e-6)
                {
                    error = "骨架为空：平面区内没有分割出有效胶条，无法提取中心线";
                    return false;
                }

                var (ordered, pathLen) = LongestPathThroughTree(segs, 20.0);
                mergedCoverage = pathLen / total;
                longestOnlyCoverage = segs.Max(s => s.Len) / total;

                // 拼成有序点列（相邻重复点 &lt;0.5px 去重，探针 StageI ② 口径）
                var pr = new List<double>(1024);
                var pc = new List<double>(1024);
                foreach (var s in ordered)
                {
                    for (int i = 0; i < s.R.Length; i++)
                    {
                        if (pr.Count > 0 && Dist(pr[^1], pc[^1], s.R[i], s.C[i]) < 0.5)
                            continue;
                        pr.Add(s.R[i]);
                        pc.Add(s.C[i]);
                    }
                }
                if (pr.Count < 2)
                {
                    error = "提取点列不足（合并后少于 2 点）：请检查图像中的胶条是否完整";
                    return false;
                }

                (rows, cols) = Downsample(pr, pc, targetPoints);
                return true;
            }
            catch (Exception ex)
            {
                error = $"自动提取中心线失败：{ex.Message}";
                return false;
            }
            finally
            {
                try { bead?.Dispose(); } catch { }
                try { skel?.Dispose(); } catch { }
                try { contours?.Dispose(); } catch { }
            }
        }

        // ==================================================================
        //  托管侧几何（树直径合并 / 等距抽稀 / 有向 Hausdorff）
        // ==================================================================

        /// <summary>
        /// 取骨架「树」的最长路径（直径）。返回沿路径有序的段序列与总长。
        ///
        /// 做法（探针 LongestPathThroughTree 原样移植）：端点按容差聚成节点 → 段成为带权边
        /// → 两次 DFS 求直径（任取一点找最远点 A，再从 A 找最远点 B，A→B 即直径）
        /// → 按父指针还原路径并逐段定向。短枝（不在直径上）自动排除，无需另写去毛刺规则。
        /// 为什么必须用它（P6 实测）：骨架常在分叉处被切成多条，「只取最长一条」覆盖仅 68.9%，
        /// 未覆盖处会被 apply_bead_inspection_model 误判「缺胶」；树直径覆盖 98.2%。
        /// </summary>
        public static (List<BeadSeg> ordered, double length) LongestPathThroughTree(List<BeadSeg> segs, double tol)
        {
            if (segs.Count == 0)
                return (new List<BeadSeg>(), 0);
            if (segs.Count == 1)
                return (new List<BeadSeg> { segs[0] }, segs[0].Len);

            // 端点聚类成节点
            var nodeR = new List<double>();
            var nodeC = new List<double>();
            int NodeOf(double r, double c)
            {
                for (int i = 0; i < nodeR.Count; i++)
                    if (Dist(r, c, nodeR[i], nodeC[i]) < tol)
                        return i;
                nodeR.Add(r);
                nodeC.Add(c);
                return nodeR.Count - 1;
            }

            var headNode = new int[segs.Count];
            var tailNode = new int[segs.Count];
            for (int i = 0; i < segs.Count; i++)
            {
                headNode[i] = NodeOf(segs[i].R[0], segs[i].C[0]);
                tailNode[i] = NodeOf(segs[i].R[^1], segs[i].C[^1]);
            }

            // 邻接：节点 → (段索引, 经该段到达的另一端节点)
            var adj = new List<(int seg, int other)>[nodeR.Count];
            for (int i = 0; i < adj.Length; i++)
                adj[i] = new List<(int, int)>();
            for (int i = 0; i < segs.Count; i++)
            {
                adj[headNode[i]].Add((i, tailNode[i]));
                adj[tailNode[i]].Add((i, headNode[i]));
            }

            // 两次 DFS 求直径（树无环，visited 即可）
            (int far, Dictionary<int, (int prevNode, int viaSeg)> prev) Walk(int start)
            {
                var prev = new Dictionary<int, (int, int)>();
                var seen = new HashSet<int> { start };
                var stack = new Stack<int>();
                stack.Push(start);
                var dist = new Dictionary<int, double> { [start] = 0 };
                int farNode = start;
                double farDist = 0;
                while (stack.Count > 0)
                {
                    int u = stack.Pop();
                    foreach (var (si, v) in adj[u])
                    {
                        if (seen.Contains(v))
                            continue;
                        seen.Add(v);
                        prev[v] = (u, si);
                        dist[v] = dist[u] + segs[si].Len;
                        if (dist[v] > farDist)
                        {
                            farDist = dist[v];
                            farNode = v;
                        }
                        stack.Push(v);
                    }
                }
                return (farNode, prev);
            }

            var (a, _) = Walk(0);
            var (b, prevMap) = Walk(a);

            // 还原 b → a 的节点序列
            var pathNodes = new List<int>();
            for (int cur = b; ; cur = prevMap[cur].prevNode)
            {
                pathNodes.Add(cur);
                if (cur == a || !prevMap.ContainsKey(cur))
                    break;
            }
            pathNodes.Reverse();

            // 按路径顺序取段并定向（每段的头必须接上前一段的尾）
            var ordered = new List<BeadSeg>();
            for (int k = 0; k + 1 < pathNodes.Count; k++)
            {
                int u = pathNodes[k], v = pathNodes[k + 1];
                foreach (var (si, other) in adj[u])
                {
                    if (other != v)
                        continue;
                    var s = segs[si];
                    // 让该段朝向「从 u 出发」：若其尾节点是 u，则翻转
                    ordered.Add(tailNode[si] == u ? s.Reversed() : s);
                    break;
                }
            }
            return (ordered, ordered.Sum(s => s.Len));
        }

        /// <summary>等距抽稀到约 n 个点（按累计弧长等距取样，保端点、保走向；探针 Downsample 原样移植）</summary>
        public static (double[] r, double[] c) Downsample(List<double> r, List<double> c, int n)
        {
            if (r.Count <= n)
                return (r.ToArray(), c.ToArray());
            var cum = new double[r.Count];
            for (int i = 1; i < r.Count; i++)
                cum[i] = cum[i - 1] + Dist(r[i - 1], c[i - 1], r[i], c[i]);
            double step = cum[^1] / (n - 1);
            var rr = new List<double>(n) { r[0] };
            var cc = new List<double>(n) { c[0] };
            int k = 1;
            for (int j = 1; j < n - 1; j++)
            {
                double target = step * j;
                while (k < r.Count - 1 && cum[k] < target)
                    k++;
                rr.Add(r[k]);
                cc.Add(c[k]);
            }
            rr.Add(r[^1]);
            cc.Add(c[^1]);
            return (rr.ToArray(), cc.ToArray());
        }

        /// <summary>欧氏距离</summary>
        public static double Dist(double r1, double c1, double r2, double c2) =>
            Math.Sqrt((r1 - r2) * (r1 - r2) + (c1 - c2) * (c1 - c2));

        /// <summary>
        /// 有向 Hausdorff：A 的每个点到 B 最近点的距离的最大值（最大偏离）。
        /// 为什么不用 distance_cc（P11 踩坑）：它的 max 是「全体点对的全局最大距离」（实测 ≈620px），
        /// 不是「A 偏离 B 多远」——用它判贴合度会得出完全相反的结论。
        /// </summary>
        public static double DirectedHausdorff(List<(double r, double c)> a, List<(double r, double c)> b)
        {
            double worst = 0;
            foreach (var p in a)
            {
                double best = double.MaxValue;
                foreach (var q in b)
                {
                    double d = Dist(p.r, p.c, q.r, q.c);
                    if (d < best)
                        best = d;
                }
                if (best > worst)
                    worst = best;
            }
            return worst;
        }

        /// <summary>A 各点到 B 最近距离的平均值（整体贴合度；P11 实测提取线对范例线 1.98px）</summary>
        public static double AverageNearest(List<(double r, double c)> a, List<(double r, double c)> b)
        {
            if (a.Count == 0)
                return 0;
            double sum = 0;
            foreach (var p in a)
            {
                double best = double.MaxValue;
                foreach (var q in b)
                {
                    double d = Dist(p.r, p.c, q.r, q.c);
                    if (d < best)
                        best = d;
                }
                sum += best == double.MaxValue ? 0 : best;
            }
            return sum / a.Count;
        }

        /// <summary>点列 → [(row,col)]（断言做贴合度对照用）</summary>
        public static List<(double r, double c)> ToPoints(double[] rows, double[] cols)
        {
            var list = new List<(double, double)>(rows.Length);
            for (int i = 0; i < rows.Length; i++)
                list.Add((rows[i], cols[i]));
            return list;
        }
    }
}
