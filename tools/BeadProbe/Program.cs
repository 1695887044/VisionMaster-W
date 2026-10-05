using System.Diagnostics;
using System.Text;
using HalconDotNet;

namespace BeadProbe;

/// <summary>
/// 胶路检测插件 · 探针脚手架（临时工程，结论回写 docs\胶路检测\ 后即删）。
///
/// 目的：把方案说明书 §7.1 的 P1~P8 从"待实测"变成"有实测出处"。
/// 输出写 UTF-8 文件而非 stdout —— 控制台代码页会把中文转坏。
/// </summary>
internal static class Program
{
    private static readonly StringBuilder Log = new();
    private const string Dir = @"D:\C#\VM\Image\bead";
    private const string OutFile = @"D:\C#\VM\tools\BeadProbe\probe_result.txt";

    private static void L(string s = "") => Log.AppendLine(s);
    private static void H(string s) { L(); L("=== " + s + " ==="); }
    private static void Ok(string s) => L("  [OK]   " + s);
    private static void Bad(string s) => L("  [FAIL] " + s);

    private static int Main()
    {
        L("===== 胶路检测插件 · 探针 P1~P8 =====");
        L("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        L("HALCONROOT: " + (Environment.GetEnvironmentVariable("HALCONROOT") ?? "(null)"));

        try { StageA_ImageBasics(); } catch (Exception ex) { Bad("StageA 异常: " + ex.Message); }
        try { StageB_Alignment(); } catch (Exception ex) { Bad("StageB 异常: " + ex.Message); }
        try { StageC_PathAndModel(); } catch (Exception ex) { Bad("StageC 异常: " + ex.Message); }
        try { StageD_DetectAll(); } catch (Exception ex) { Bad("StageD 异常: " + ex.Message); }
        try { StageE_MeasureWidth(); } catch (Exception ex) { Bad("StageE 异常: " + ex.Message); }
        try { StageF_ExtractCenterline(); } catch (Exception ex) { Bad("StageF 异常: " + ex.Message); }
        try { StageG_PositionDeviation(); } catch (Exception ex) { Bad("StageG 异常: " + ex.Message); }
        try { StageH_MicroProbes(); } catch (Exception ex) { Bad("StageH 异常: " + ex.Message); }
        try { StageI_OrderBranches(); } catch (Exception ex) { Bad("StageI 异常: " + ex.Message); }
        try { StageJ_PointCount(); } catch (Exception ex) { Bad("StageJ 异常: " + ex.Message); }
        try { StageK_CompareToReference(); } catch (Exception ex) { Bad("StageK 异常: " + ex.Message); }

        L();
        L("===== 结束 =====");
        try { File.WriteAllText(OutFile, Log.ToString(), new UTF8Encoding(false)); } catch { }
        return 0;
    }

    private static HObject ReadImg(string name)
    {
        HOperatorSet.ReadImage(out HObject img, Path.Combine(Dir, name));
        return img;
    }

    // ─────────────────────────── A. 图像基础 ───────────────────────────

    private static void StageA_ImageBasics()
    {
        H("A. 图像基础信息");
        var names = new List<string> { "adhesive_bead_ref.png" };
        for (int i = 1; i <= 7; i++) names.Add($"adhesive_bead_{i:00}.png");

        foreach (var n in names)
        {
            try
            {
                HOperatorSet.ReadImage(out HObject img, Path.Combine(Dir, n));
                HOperatorSet.GetImageSize(img, out HTuple w, out HTuple h);
                HOperatorSet.CountChannels(img, out HTuple ch);
                HOperatorSet.GetImageType(img, out HTuple type);
                HOperatorSet.MinMaxGray(img, img, 0, out HTuple mn, out HTuple mx, out _);
                L($"  {n,-26} {w.I}x{h.I}  ch={ch.I}  type={type.S}  gray=[{mn.D:0.#},{mx.D:0.#}]");
                img.Dispose();
            }
            catch (Exception ex) { Bad($"{n}: {ex.Message}"); }
        }
    }

    // ───────────────── B. 平面可变形对齐（P2 矫正对比 / P5 计时）─────────────────

    private static void StageB_Alignment()
    {
        H("B. 平面可变形（投影）对齐");

        // ── B1. 按范例口径做矫正（vector_to_proj_hom_mat2d）──
        HOperatorSet.ReadImage(out HObject refImg, Path.Combine(Dir, "adhesive_bead_ref.png"));

        var rows = new HTuple(658.232, 291.923, 314.817, 691.533);
        var cols = new HTuple(330.071, 316.617, 971.032, 947.008);
        double W = 5.4, Hh = 3.4;
        var row1 = new HTuple(300 + Hh * 120.0, 300.0, 300.0, 300 + Hh * 120.0);
        var col1 = new HTuple(300.0, 300.0, 300 + W * 120.0, 300 + W * 120.0);

        HOperatorSet.VectorToProjHomMat2d(rows, cols, row1, col1, "normalized_dlt",
            new HTuple(), new HTuple(), new HTuple(), new HTuple(), new HTuple(), new HTuple(),
            out HTuple homRect, out _);
        HOperatorSet.ProjectiveTransImage(refImg, out HObject refTrans, homRect, "bilinear", "false", "false");
        HOperatorSet.GetImageSize(refTrans, out HTuple rw, out HTuple rh);
        Ok($"矫正后参考图尺寸 {rw.I}x{rh.I}（范例目标区 300..{300 + W * 120:0} 列 / 300..{300 + Hh * 120:0} 行）");

        // ── B2. 在矫正图上取平面区（范例 prepare_alignment 口径）──
        HOperatorSet.BinaryThreshold(refTrans, out HObject reg, "smooth_histo", "light", out HTuple usedTh);
        HOperatorSet.OpeningCircle(reg, out HObject regOp, 5.5);
        HOperatorSet.Connection(regOp, out HObject conn);
        HOperatorSet.AreaCenter(conn, out HTuple areas, out _, out _);
        HOperatorSet.TupleNeg(areas, out HTuple negAreas);
        HOperatorSet.TupleSortIndex(negAreas, out HTuple sortIdx);
        HOperatorSet.SelectObj(conn, out HObject biggest, sortIdx[0] + 1);
        HOperatorSet.FillUp(biggest, out HObject filled);
        HOperatorSet.DilationCircle(filled, out HObject part, 5.5);
        HOperatorSet.AreaCenter(part, out HTuple partArea, out HTuple rowT, out HTuple colT);
        Ok($"平面区: 面积={partArea.D:0}  锚点(RowT,ColT)=({rowT.D:0.##},{colT.D:0.##})  二值化阈值={usedTh.D:0.#}");

        // ── B3. 建平面模型（P5 计时）──
        HOperatorSet.ReduceDomain(refTrans, part, out HObject refReduced);
        var sw = Stopwatch.StartNew();
        HOperatorSet.CreatePlanarUncalibDeformableModel(refReduced, "auto", new HTuple(), new HTuple(),
            "auto", 1, new HTuple(), "auto", 1, new HTuple(), "auto", "none", "use_polarity",
            "auto", "auto", new HTuple(), new HTuple(), out HTuple planarModel);
        sw.Stop();
        Ok($"P5: create_planar_uncalib_deformable_model 耗时 = {sw.ElapsedMilliseconds} ms");

        // ── B4. 对 7 张图求位姿（P2 变体1：已矫正）──
        L("  对齐分数（变体1 = 按范例做矫正）:");
        var scoresRect = new List<double>();
        for (int i = 1; i <= 7; i++)
        {
            try
            {
                HOperatorSet.ReadImage(out HObject img, Path.Combine(Dir, $"adhesive_bead_{i:00}.png"));
                HOperatorSet.FindPlanarUncalibDeformableModel(img, planarModel,
                    -0.39, 0.78, 1, 1, 1, 1, 0.4, 1, 1, 5, 0.9, new HTuple(), new HTuple(),
                    out HTuple hom, out HTuple score);
                scoresRect.Add(score.D);
                L($"    图{i:00}: score={score.D:0.0000}");
                img.Dispose();
            }
            catch (Exception ex) { L($"    图{i:00}: 失败 - {ex.Message}"); scoresRect.Add(double.NaN); }
        }

        // ── B5. P2 变体2：不做矫正，直接在原参考图上取平面区 ──
        HOperatorSet.BinaryThreshold(refImg, out HObject reg2, "smooth_histo", "light", out _);
        HOperatorSet.OpeningCircle(reg2, out HObject reg2o, 5.5);
        HOperatorSet.Connection(reg2o, out HObject conn2);
        HOperatorSet.AreaCenter(conn2, out HTuple areas2, out _, out _);
        HOperatorSet.TupleNeg(areas2, out HTuple negAreas2);
        HOperatorSet.TupleSortIndex(negAreas2, out HTuple sortIdx2);
        HOperatorSet.SelectObj(conn2, out HObject biggest2, sortIdx2[0] + 1);
        HOperatorSet.FillUp(biggest2, out HObject filled2);
        HOperatorSet.DilationCircle(filled2, out HObject part2, 5.5);
        HOperatorSet.ReduceDomain(refImg, part2, out HObject refReduced2);
        HOperatorSet.CreatePlanarUncalibDeformableModel(refReduced2, "auto", new HTuple(), new HTuple(),
            "auto", 1, new HTuple(), "auto", 1, new HTuple(), "auto", "none", "use_polarity",
            "auto", "auto", new HTuple(), new HTuple(), out HTuple planarModel2);

        L("  对齐分数（变体2 = 不做矫正）:");
        var scoresRaw = new List<double>();
        for (int i = 1; i <= 7; i++)
        {
            try
            {
                HOperatorSet.ReadImage(out HObject img, Path.Combine(Dir, $"adhesive_bead_{i:00}.png"));
                HOperatorSet.FindPlanarUncalibDeformableModel(img, planarModel2,
                    -0.39, 0.78, 1, 1, 1, 1, 0.4, 1, 1, 5, 0.9, new HTuple(), new HTuple(),
                    out HTuple hom, out HTuple score);
                scoresRaw.Add(score.D);
                L($"    图{i:00}: score={score.D:0.0000}");
                img.Dispose();
            }
            catch (Exception ex) { L($"    图{i:00}: 失败 - {ex.Message}"); scoresRaw.Add(double.NaN); }
        }

        double avg1 = scoresRect.Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Average();
        double avg2 = scoresRaw.Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Average();
        int fail1 = scoresRect.Count(double.IsNaN), fail2 = scoresRaw.Count(double.IsNaN);
        L($"  P2 小结: 矫正后 平均score={avg1:0.0000} 失败{fail1}张 | 不矫正 平均score={avg2:0.0000} 失败{fail2}张");

        // ── B6. 落盘一张对齐图供后续阶段用 ──
        HOperatorSet.ReadImage(out HObject g1, Path.Combine(Dir, "adhesive_bead_01.png"));
        HOperatorSet.FindPlanarUncalibDeformableModel(g1, planarModel,
            -0.39, 0.78, 1, 1, 1, 1, 0.4, 1, 1, 5, 0.9, new HTuple(), new HTuple(),
            out HTuple h1, out _);
        HOperatorSet.HomMat2dInvert(h1, out HTuple h1inv);
        HOperatorSet.HomMat2dTranslate(h1inv, rowT, colT, out HTuple h1t);
        HOperatorSet.ProjectiveTransImage(g1, out HObject aligned1, h1t, "bilinear", "false", "false");
        try
        {
            HOperatorSet.WriteImage(aligned1, "png", 0, Path.Combine(AppContext.BaseDirectory, "aligned_01.png"));
            Ok("已导出对齐图 aligned_01.png（供人工目视核对）");
        }
        catch (Exception ex) { L("  导出对齐图失败: " + ex.Message); }

        // 保存供后续阶段
        _planarModel = planarModel;
        _rowT = rowT; _colT = colT;
        _aligned1 = aligned1;
        // 平面区（fill_up 已把胶条所在"洞"填实，dilation 再放宽）——
        // 对齐后的图与参考同姿态，故这块区域可直接当后续"找胶"的域
        _plane = part;

        // 参考图也对齐到同一坐标系（投影矫正 + 同一参考位姿）：
        // 它没有胶，可与有胶图做差分 —— 这是"从良品图教路径"最直接的实现。
        HOperatorSet.ProjectiveTransImage(refImg, out HObject refRect, homRect, "bilinear", "false", "false");
        HOperatorSet.FindPlanarUncalibDeformableModel(refRect, planarModel,
            -0.39, 0.78, 1, 1, 1, 1, 0.4, 1, 1, 5, 0.9, new HTuple(), new HTuple(),
            out HTuple hRef, out HTuple scRef);
        HOperatorSet.HomMat2dInvert(hRef, out HTuple hRefInv);
        HOperatorSet.HomMat2dTranslate(hRefInv, rowT, colT, out HTuple hRefT);
        HOperatorSet.ProjectiveTransImage(refRect, out HObject refAligned, hRefT, "bilinear", "false", "false");
        Ok($"参考图已对齐到同一坐标系（自匹配 score={scRef.D:0.0000}）");
        try
        {
            HOperatorSet.WriteImage(refAligned, "png", 0, Path.Combine(AppContext.BaseDirectory, "aligned_ref.png"));
        }
        catch { }
        _refAligned = refAligned;
    }

    private static HTuple? _planarModel;
    private static HTuple? _rowT;
    private static HTuple? _colT;
    private static HObject? _aligned1;
    private static HObject? _plane;
    private static HObject? _refAligned;
    private static double _measuredWidth;
    private static double _measuredTol;

    // ────────────── C. 参考路径与 bead 模型（P1 两种曲线口径）──────────────

    private static readonly double[] RefRows =
    {
        701.767, 626.953, 538.867, 443.54, 390.447, 360.28,
        354.247, 363.9, 400.1, 458.02, 509.907, 588.34, 659.533, 696.94
    };
    private static readonly double[] RefCols =
    {
        319.24, 336.133, 367.507, 431.46, 489.38, 546.093,
        646.247, 722.267, 776.567, 826.04, 869.48, 912.92, 934.64, 929.813
    };

    private static void StageC_PathAndModel()
    {
        H("C. 参考路径与 bead 模型（范例控制点）");

        var rows = new HTuple(RefRows);
        var cols = new HTuple(RefCols);

        // ── P1-a: gen_contour_nurbs_xld（范例口径）──
        HTuple modelNurbs = null!, modelPoly = null!;
        try
        {
            var weights = new HTuple();
            for (int i = 0; i < RefRows.Length; i++) weights = weights.TupleConcat(15.0);
            HOperatorSet.GenContourNurbsXld(out HObject cNurbs, rows, cols, "auto", weights, 3, 1, 5);
            HOperatorSet.LengthXld(cNurbs, out HTuple lenN);
            HOperatorSet.CountObj(cNurbs, out HTuple cntN);
            Ok($"P1-a gen_contour_nurbs_xld 成功: 对象数={cntN.I} 长度={lenN.D:0.##}px");

            HOperatorSet.CreateBeadInspectionModel(cNurbs, 14, 7, 30, "dark",
                new HTuple(), new HTuple(), out modelNurbs);
            Ok("     以 NURBS 曲线建 bead 模型成功");
            cNurbs.Dispose();
        }
        catch (Exception ex) { Bad("P1-a NURBS 口径失败: " + ex.Message); }

        // ── P1-b: gen_contour_polygon_xld（折线兜底口径）──
        try
        {
            HOperatorSet.GenContourPolygonXld(out HObject cPoly, rows, cols);
            HOperatorSet.LengthXld(cPoly, out HTuple lenP);
            Ok($"P1-b gen_contour_polygon_xld 成功: 长度={lenP.D:0.##}px（{RefRows.Length} 个顶点）");

            HOperatorSet.CreateBeadInspectionModel(cPoly, 14, 7, 30, "dark",
                new HTuple(), new HTuple(), out modelPoly);
            Ok("     以折线建 bead 模型成功");
            cPoly.Dispose();
        }
        catch (Exception ex) { Bad("P1-b 折线口径失败: " + ex.Message); }

        // ── 模型自省：读出容差参数确认口径 ──
        foreach (var (label, m) in new[] { ("NURBS", modelNurbs), ("折线", modelPoly) })
        {
            if (m == null) continue;
            try
            {
                HOperatorSet.GetBeadInspectionParam(m, "target_thickness", out HTuple tt);
                HOperatorSet.GetBeadInspectionParam(m, "thickness_tolerance", out HTuple tol);
                HOperatorSet.GetBeadInspectionParam(m, "position_tolerance", out HTuple pt);
                Ok($"     [{label}] target_thickness={tt.D} thickness_tolerance={tol.D} position_tolerance={pt.D}");
            }
            catch (Exception ex) { L($"     [{label}] get_bead_inspection_param 不可用: {ex.Message}"); }
        }

        // ── P10: create_bead_inspection_model 的参数合法域（实测 #3716 的边界）──
        // 起因：用提取的点列以 target_thickness=4 建模型时报 #3716，
        // 说明该参数有下界（与轮廓采样/离散化有关），必须实测出可用区间。
        L("  P10 create_bead_inspection_model 参数合法域探测（轮廓=范例 NURBS）:");
        try
        {
            var weights = new HTuple();
            for (int i = 0; i < RefRows.Length; i++) weights = weights.TupleConcat(15.0);
            HOperatorSet.GenContourNurbsXld(out HObject cProbe, new HTuple(RefRows), new HTuple(RefCols),
                "auto", weights, 3, 1, 5);

            foreach (double tw in new[] { 1.0, 2.0, 4.0, 6.0, 8.0, 10.0, 14.0, 20.0, 40.0 })
            {
                try
                {
                    HOperatorSet.CreateBeadInspectionModel(cProbe, tw, Math.Max(tw * 0.5, 1), 30, "dark",
                        new HTuple(), new HTuple(), out HTuple mm);
                    Ok($"     target_thickness={tw,5:0.#} 可用");
                    HOperatorSet.ClearBeadInspectionModel(mm);
                }
                catch (Exception ex)
                {
                    var m2 = ex.Message;
                    int p = m2.IndexOf("operator");
                    if (p > 0) m2 = m2.Substring(0, p);
                    L($"     target_thickness={tw,5:0.#} 失败: {m2.Trim()}");
                }
            }
            cProbe.Dispose();
        }
        catch (Exception ex) { Bad("P10 探测失败: " + ex.Message); }

        _modelNurbs = modelNurbs;
        _modelPoly = modelPoly;
    }

    private static HTuple? _modelNurbs;
    private static HTuple? _modelPoly;

    // ────────────── D. 7 张样图检测（P4 真值表）──────────────

    private static void StageD_DetectAll()
    {
        H("D. 7 张样图检测（P4 真值表）");

        if (_planarModel == null || _modelNurbs == null) { Bad("前置阶段未就绪，跳过"); return; }

        L("  图号 | 对齐score | 错误段数 | 错误类型");
        for (int i = 1; i <= 7; i++)
        {
            try
            {
                HOperatorSet.ReadImage(out HObject img, Path.Combine(Dir, $"adhesive_bead_{i:00}.png"));
                HOperatorSet.FindPlanarUncalibDeformableModel(img, _planarModel,
                    -0.39, 0.78, 1, 1, 1, 1, 0.4, 1, 1, 5, 0.9, new HTuple(), new HTuple(),
                    out HTuple hom, out HTuple score);
                HOperatorSet.HomMat2dInvert(hom, out HTuple hInv);
                HOperatorSet.HomMat2dTranslate(hInv, _rowT!, _colT!, out HTuple hT);
                HOperatorSet.ProjectiveTransImage(img, out HObject aligned, hT, "bilinear", "false", "false");

                HOperatorSet.ApplyBeadInspectionModel(aligned, out HObject left, out HObject right,
                    out HObject errSeg, _modelNurbs, out HTuple errType);

                HOperatorSet.CountObj(errSeg, out HTuple segCnt);
                int n = errType.Length;
                var types = n == 0 ? "(无)" : string.Join(", ", errType.ToSArr());

                // 按类计数
                int nb = CountOf(errType, "no bead");
                int thin = CountOf(errType, "too thin");
                int thick = CountOf(errType, "too thick");
                int pos = CountOf(errType, "incorrect position");
                L($"   {i:00}  |  {score.D:0.0000}  |    {n,2}    | {types}   [缺胶{nb}/太细{thin}/太粗{thick}/偏移{pos}]");

                left.Dispose(); right.Dispose(); errSeg.Dispose(); aligned.Dispose(); img.Dispose();
            }
            catch (Exception ex) { L($"   {i:00}  | 失败: {ex.Message}"); }
        }
    }

    private static int CountOf(HTuple t, string s)
    {
        int c = 0;
        foreach (var v in t.ToSArr()) if (v == s) c++;
        return c;
    }

    // ────────────── E. 实测胶宽（P3 像素口径）──────────────
    //
    // 关键：胶是"亮零件上的一条暗线"，直接 binary_threshold(...,'dark') 会把
    // 整张暗背景（约占 87% 像素）当成胶 —— 必须先在亮平面区内找暗线。
    // 这也是配置态「自动提取」的真实前置条件。

    /// <summary>
    /// 在**亮平面区**内分割出胶条（black-hat 口径）。
    ///
    /// 为什么用 black-hat（灰度闭运算 − 原图）而不是直接取暗：
    /// 胶是「亮加工面上的一条细暗线」，而平面区里还含**大块暗区**（拱形开口）。
    /// 直接 binary_threshold(...,'dark') 会优先切到大块暗区（实测 65323px / 663x419 外接矩形，
    /// 那根本不是胶），细线反而被淹没。black-hat 的判别力正在于此：闭运算的掩膜比细线宽时
    /// 细线被填平 → 差值高；大块暗区远超掩膜尺寸填不平 → 差值低。于是「细暗线」被单独拎出。
    /// </summary>
    private static HObject SegmentBeadBlackHat(HObject aligned, HObject plane, int mask, out double th)
    {
        HOperatorSet.ReduceDomain(aligned, plane, out HObject planeImg);
        HOperatorSet.GrayClosingRect(planeImg, out HObject closedImg, mask, mask);
        HOperatorSet.SubImage(closedImg, planeImg, out HObject blackHat, 1.0, 0);
        HOperatorSet.BinaryThreshold(blackHat, out HObject raw, "smooth_histo", "light", out HTuple thDark);
        HObject bead = LargestComponent(raw, 3.5);
        planeImg.Dispose(); closedImg.Dispose(); blackHat.Dispose(); raw.Dispose();
        th = thDark.D;
        return bead;
    }

    /// <summary>
    /// 与「无胶参考图」做差分提取胶条（最贴合"从良品图教路径"的语义）。
    /// 前提：能拿到同姿态的无胶参考图（本例 `adhesive_bead_ref.png`）。
    /// </summary>
    private static HObject SegmentBeadByRefDiff(HObject aligned, HObject refAligned, HObject plane, out double th)
    {
        HOperatorSet.ReduceDomain(aligned, plane, out HObject a);
        HOperatorSet.ReduceDomain(refAligned, plane, out HObject b);
        HOperatorSet.AbsDiffImage(a, b, out HObject diff, 1.0);
        HOperatorSet.BinaryThreshold(diff, out HObject raw, "smooth_histo", "light", out HTuple thDiff);
        HObject bead = LargestComponent(raw, 3.5);
        a.Dispose(); b.Dispose(); diff.Dispose(); raw.Dispose();
        th = thDiff.D;
        return bead;
    }

    /// <summary>闭运算去噪后取最大连通域</summary>
    private static HObject LargestComponent(HObject raw, double closeRadius)
    {
        HOperatorSet.ClosingCircle(raw, out HObject closed, closeRadius);
        HOperatorSet.Connection(closed, out HObject conn);
        HOperatorSet.AreaCenter(conn, out HTuple areas, out _, out _);
        HOperatorSet.TupleNeg(areas, out HTuple neg);
        HOperatorSet.TupleSortIndex(neg, out HTuple idx);
        HOperatorSet.SelectObj(conn, out HObject biggest, idx[0] + 1);
        closed.Dispose(); conn.Dispose();
        return biggest;
    }

    /// <summary>取轮廓集合中最长的一条</summary>
    private static HObject LongestOf(HObject contours, out double len)
    {
        HOperatorSet.LengthXld(contours, out HTuple lens);
        HOperatorSet.TupleSortIndex(lens, out HTuple idx);
        HOperatorSet.SelectObj(contours, out HObject longest, idx[idx.Length - 1] + 1);
        HOperatorSet.LengthXld(longest, out HTuple l);
        len = l.D;
        return longest;
    }

    private static void StageE_MeasureWidth()
    {
        H("E. 实测胶宽（P3 像素口径）");

        if (_aligned1 == null || _plane == null) { Bad("无对齐图/平面区，跳过"); return; }

        try
        {
            // 两种分割口径都测，取覆盖更完整者
            HObject bh = SegmentBeadBlackHat(_aligned1, _plane, 15, out double thBH);
            HObject rd = SegmentBeadByRefDiff(_aligned1, _refAligned!, _plane, out double thRD);
            HOperatorSet.AreaCenter(bh, out HTuple aBH, out _, out _);
            HOperatorSet.AreaCenter(rd, out HTuple aRD, out _, out _);

            HObject mainBead = aRD.D > aBH.D ? rd : bh;
            string which = aRD.D > aBH.D ? "参考图差分" : "black-hat(15)";
            double th = aRD.D > aBH.D ? thRD : thBH;
            Ok($"分割口径对比: black-hat={aBH.D:0}px(阈值{thBH:0.#}) | 参考图差分={aRD.D:0}px(阈值{thRD:0.#})");
            Ok($"选用「{which}」→ 胶条面积={Math.Max(aBH.D, aRD.D):0}px");

            HOperatorSet.AreaCenter(_plane, out HTuple planeArea, out _, out _);
            HOperatorSet.SmallestRectangle1(mainBead, out HTuple r1, out HTuple c1, out HTuple r2, out HTuple c2);
            Ok($"亮平面区面积={planeArea.D:0}px（占全图 {planeArea.D / (1280.0 * 1024) * 100:0.#}%）");
            Ok($"主胶条外接矩形 {c2.D - c1.D + 1:0}x{r2.D - r1.D + 1:0}  阈值={th:0.#}");

            // 骨架 + 距离变换 → 沿中心线的半宽
            HOperatorSet.GetImageSize(_aligned1, out HTuple iw, out HTuple ih);
            HOperatorSet.Skeleton(mainBead, out HObject skel);
            HOperatorSet.DistanceTransform(mainBead, out HObject distImg, "euclidean", "true", iw, ih);
            HOperatorSet.Intensity(skel, distImg, out HTuple meanHalf, out HTuple devHalf);
            HOperatorSet.MinMaxGray(mainBead, distImg, 0, out _, out HTuple dmax, out _);

            HOperatorSet.AreaCenter(skel, out HTuple skelPts, out _, out _);
            Ok($"沿中心线平均半宽={meanHalf.D:0.##}px (σ={devHalf.D:0.##})  →  平均胶宽 ≈ {2 * meanHalf.D:0.##}px");
            Ok($"骨架点数（中心线长度近似）={skelPts.D:0}");
            Ok($"最粗处半宽={dmax.D:0.##}px  →  最大胶宽 ≈ {2 * dmax.D:0.##}px");

            L($"  P3 对照（范例 TargetWidth=14 是「矫正坐标系」下的值）:");
            L($"     本图平均胶宽 {2 * meanHalf.D:0.##}px  →  与 14 的比值 {2 * meanHalf.D / 14:0.00}");
            L($"     半宽波动 σ={devHalf.D:0.##}px  →  与 WidthTolerance=7 的比值 {devHalf.D / 7:0.00}");
            L($"     ⇒ 范例的 14/7/30 不能直接照搬，必须按本坐标系实测重定");

            // 用实测胶宽反推"等效"参数（供方案默认值参考）
            double tw = 2 * meanHalf.D;
            double tol = Math.Max(devHalf.D * 3, 1);
            _measuredWidth = tw;
            _measuredTol = tol;
            L($"  P3 建议默认值（本坐标系，1280x1024）:");
            L($"     TargetWidth ≈ {tw:0}  （实测平均胶宽）");
            L($"     WidthTolerance ≈ {tol:0}  （3σ 覆盖正常波动）");
            L($"     ⚠ 注意 P10 实测：target_thickness 有下界（<6 报 #3716）");
            if (tw < 6) L($"       实测值 {tw:0} 低于下界 6 → 本插件必须给出中文提示或自动下限保护");

            bh.Dispose(); rd.Dispose(); mainBead.Dispose(); skel.Dispose(); distImg.Dispose();
        }
        catch (Exception ex) { Bad("实测胶宽失败: " + ex.Message); }
    }

    // ────────────── F. 自动提取中心线（P6/P7/P8）──────────────

    private static void StageF_ExtractCenterline()
    {
        H("F. 自动提取中心线（P6/P7/P8）");

        if (_aligned1 == null || _plane == null || _refAligned == null)
        {
            Bad("无对齐图/平面区/参考对齐图，跳过"); return;
        }

        try
        {
            // ── 两种分割口径对比 ──
            HObject bh = SegmentBeadBlackHat(_aligned1, _plane, 15, out double thBH);
            HObject rd = SegmentBeadByRefDiff(_aligned1, _refAligned, _plane, out double thRD);
            HOperatorSet.AreaCenter(bh, out HTuple aBH, out _, out _);
            HOperatorSet.AreaCenter(rd, out HTuple aRD, out _, out _);
            HOperatorSet.SmallestRectangle1(bh, out HTuple br1, out HTuple bc1, out HTuple br2, out HTuple bc2);
            HOperatorSet.SmallestRectangle1(rd, out HTuple rr1, out HTuple rc1, out HTuple rr2, out HTuple rc2);
            L($"  口径A black-hat(15): 面积={aBH.D:0}px 外接={bc2.D - bc1.D + 1:0}x{br2.D - br1.D + 1:0} 阈值={thBH:0.#}");
            L($"  口径B 参考图差分  : 面积={aRD.D:0}px 外接={rc2.D - rc1.D + 1:0}x{rr2.D - rr1.D + 1:0} 阈值={thRD:0.#}");

            // 胶条应在拱形轮廓上、外接矩形接近整条胶路尺度；
            // 这里以"面积/外接矩形长宽比"做粗判，最终由 P6 的检出一致性裁决
            HObject bead = aRD.D > aBH.D ? rd : bh;
            string which = aRD.D > aBH.D ? "口径B(参考图差分)" : "口径A(black-hat)";
            Ok($"选用 {which}（面积更大者更可能覆盖整条胶路）");

            HOperatorSet.Skeleton(bead, out HObject skel);
            HOperatorSet.AreaCenter(skel, out HTuple skelPts, out _, out _);
            Ok($"骨架点数={skelPts.D:0}");

            // ── P8: gen_contours_skeleton_xld 的 mode 合法值集 ──
            L("  P8 mode 合法值集探测:");
            var modes = new[] { "filter", "nurbs", "polygon", "original", "lines", "smooth", "filter_smooth" };
            string bestMode = "";
            foreach (var mode in modes)
            {
                try
                {
                    HOperatorSet.GenContoursSkeletonXld(skel, out HObject c, 5, mode);
                    HOperatorSet.CountObj(c, out HTuple cnt);
                    HOperatorSet.LengthXld(c, out HTuple len);
                    Ok($"     mode='{mode}' 可用: 轮廓数={cnt.I} 总长={len.D:0.##}px");
                    if (bestMode == "") bestMode = mode;
                    c.Dispose();
                }
                catch (Exception ex)
                {
                    var m = ex.Message;
                    int p = m.IndexOf(" in operator");
                    if (p > 0) m = m.Substring(0, p);
                    L($"     mode='{mode}' 不可用: {m.Trim()}");
                }
            }
            if (bestMode == "") { Bad("P8 所有候选 mode 都失败"); return; }
            L($"  P8 结论: 合法 mode = '{bestMode}'");

            // ── 骨架 → 轮廓 → 最长 → 平滑 ──
            HOperatorSet.GenContoursSkeletonXld(skel, out HObject contours, 5, bestMode);
            HOperatorSet.CountObj(contours, out HTuple nContours);
            HObject longest = LongestOf(contours, out double longestLen);
            HOperatorSet.LengthXld(contours, out HTuple allLens);
            double totalLen = allLens.ToDArr().Sum();
            Ok($"骨架线对象数={nContours.I} → 最长={longestLen:0.##}px  全部合计={totalLen:0.##}px");
            // 覆盖率：与范例参考线长度（1006.23px，同坐标系）对照
            L($"  P6 覆盖率: 最长骨架线 {longestLen:0.##}px / 范例参考线 1006.23px = {longestLen / 1006.23 * 100:0.#}%");
            if (longestLen / 1006.23 < 0.9)
                L($"     ⚠ 提取不完整（<90%）：'取最长一条'会丢掉其余分支 → 需合并多分支（见下 P6 结论）");

            HOperatorSet.SmoothContoursXld(longest, out HObject smoothed, 11);
            Ok("平滑 SmoothContoursXld(numRegrPoints=11) 成功");

            // ── 分支合并试验：骨架常带短枝，只取最长会丢覆盖 ──
            // 用 union_collinear_contours_xld 把首尾相近的段接起来，再看覆盖率
            try
            {
                HOperatorSet.UnionCollinearContoursXld(contours, out HObject unioned,
                    10.0, 1.0, 30.0, 0.1, "attr_keep");
                HOperatorSet.CountObj(unioned, out HTuple nU);
                HObject uLong = LongestOf(unioned, out double uLen);
                L($"  P6 合并试验 union_collinear_contours_xld: 段数 {nContours.I} → {nU.I}  最长 {longestLen:0.##} → {uLen:0.##}px");
                uLong.Dispose(); unioned.Dispose();
            }
            catch (Exception ex)
            {
                var m = ex.Message; int p = m.IndexOf(" in operator");
                if (p > 0) m = m.Substring(0, p);
                L($"  P6 合并试验失败: {m.Trim()}");
            }

            // ── P7: gen_polygons_xld 的 type 值集 ──
            L("  P7 gen_polygons_xld type 值集探测:");
            var types = new[] { "lines", "lines_color", "lines_circles", "lines_ellipses",
                                "polygon", "none", "true", "false" };
            string bestType = "";
            foreach (var ty in types)
            {
                try
                {
                    HOperatorSet.GenPolygonsXld(smoothed, out HObject p0, ty, 10.0);
                    HOperatorSet.GetContourXld(p0, out HTuple pr0, out _);
                    Ok($"     type='{ty}' 可用 → 点数={pr0.Length}");
                    if (bestType == "") bestType = ty;
                    p0.Dispose();
                }
                catch (Exception ex)
                {
                    var m = ex.Message;
                    int p = m.IndexOf(" in operator");
                    if (p > 0) m = m.Substring(0, p);
                    L($"     type='{ty}' 不可用: {m.Trim()}");
                }
            }

            HTuple exR, exC;
            if (bestType != "")
            {
                L($"  P7 结论: 合法 type = '{bestType}'；alpha 扫描:");
                foreach (double alpha in new[] { 2.0, 5.0, 10.0, 20.0, 40.0 })
                {
                    try
                    {
                        HOperatorSet.GenPolygonsXld(smoothed, out HObject poly, bestType, alpha);
                        HOperatorSet.GetContourXld(poly, out HTuple pr, out _);
                        HOperatorSet.DistanceCc(smoothed, poly, "point_to_point", out _, out HTuple dev);
                        Ok($"     alpha={alpha,5:0.#} → 点数={pr.Length,4}  最大偏差={dev.D:0.###}px");
                        poly.Dispose();
                    }
                    catch (Exception ex) { L($"     alpha={alpha} 失败: {ex.Message}"); }
                }
                HOperatorSet.GenPolygonsXld(smoothed, out HObject polyRef, bestType, 10.0);
                HOperatorSet.GetContourXld(polyRef, out exR, out exC);
                polyRef.Dispose();
            }
            else
            {
                L("  P7 结论: 无合法 type → 改用 GetContourXld 直接取点（不简化）");
                HOperatorSet.GetContourXld(smoothed, out exR, out exC);
            }
            Ok($"P6 提取点列: {exR.Length} 点");

            var er = exR.ToDArr(); var ec = exC.ToDArr();
            if (er.Length > 0)
                L($"     提取范围: Row [{er.Min():0.#}, {er.Max():0.#}]  Col [{ec.Min():0.#}, {ec.Max():0.#}]");

            // ── P6 关键判据：用提取的点列建模型 + 跑检测，与范例控制点的结果比 ──
            if (er.Length >= 2)
            {
                double twUse = Math.Max(_measuredWidth, 6);
                double tolUse = Math.Max(_measuredTol, 1);
                try
                {
                    HOperatorSet.GenContourPolygonXld(out HObject polyForModel, exR, exC);
                    HOperatorSet.CreateBeadInspectionModel(polyForModel, twUse, tolUse, 30, "dark",
                        new HTuple(), new HTuple(), out HTuple mEx);
                    Ok($"P6 用提取点列建 bead 模型成功（target_thickness={twUse:0.#} tolerance={tolUse:0.#}）");
                    L($"     ⚠ 但提取路径只覆盖 {longestLen:0}px / 范例 1006px —— 分支未合并，");
                    L($"       故下表的检出必然比范例多报（路径没覆盖到的地方被判「缺胶」）：");

                    // 用这个"自动提取"的模型跑 7 张图，看结论是否与范例控制点模型一致
                    L("  P6 检出对照（自动提取路径 vs 范例控制点路径）:");
                    for (int i = 1; i <= 7; i++)
                    {
                        try
                        {
                            HOperatorSet.ReadImage(out HObject img, Path.Combine(Dir, $"adhesive_bead_{i:00}.png"));
                            HOperatorSet.FindPlanarUncalibDeformableModel(img, _planarModel!,
                                -0.39, 0.78, 1, 1, 1, 1, 0.4, 1, 1, 5, 0.9, new HTuple(), new HTuple(),
                                out HTuple hom, out _);
                            HOperatorSet.HomMat2dInvert(hom, out HTuple hInv);
                            HOperatorSet.HomMat2dTranslate(hInv, _rowT!, _colT!, out HTuple hT);
                            HOperatorSet.ProjectiveTransImage(img, out HObject al, hT, "bilinear", "false", "false");

                            HOperatorSet.ApplyBeadInspectionModel(al, out _, out _, out HObject es, mEx, out HTuple et);
                            int nb = CountOf(et, "no bead"), th = CountOf(et, "too thin"),
                                tk = CountOf(et, "too thick"), ps = CountOf(et, "incorrect position");
                            L($"     图{i:00}: {et.Length,3} 段  [缺胶{nb}/太细{th}/太粗{tk}/偏移{ps}]");
                            es.Dispose(); al.Dispose(); img.Dispose();
                        }
                        catch (Exception ex) { L($"     图{i:00}: 失败 - {ex.Message}"); }
                    }
                    polyForModel.Dispose();
                }
                catch (Exception ex) { Bad("P6 用提取点列建模型失败: " + ex.Message); }
            }

            bh.Dispose(); rd.Dispose(); bead.Dispose(); skel.Dispose();
        }
        catch (Exception ex) { Bad("中心线提取链路失败: " + ex.Message); }
    }

    // ────────────── G. 位置偏移分布（P9：定 PositionTolerance）──────────────

    private static void StageG_PositionDeviation()
    {
        H("G. 位置偏移分布（P9：定 PositionTolerance 默认值）");

        if (_aligned1 == null || _plane == null) { Bad("无对齐图/平面区，跳过"); return; }

        try
        {
            // 基准 = 良品图(01)上提取的中心线；再测其余图中心线相对它的偏移
            HObject bead1 = SegmentBeadByRefDiff(_aligned1, _refAligned!, _plane, out _);
            HOperatorSet.Skeleton(bead1, out HObject skel1);
            HOperatorSet.GenContoursSkeletonXld(skel1, out HObject cs1, 5, "filter");
            HObject refLine = LongestOf(cs1, out double refLen);
            Ok($"基准中心线（图01，参考图差分口径）长度={refLen:0.##}px");

            L("  各图中心线相对基准的偏移（用于定 PositionTolerance）:");
            for (int i = 2; i <= 7; i++)
            {
                try
                {
                    HOperatorSet.ReadImage(out HObject img, Path.Combine(Dir, $"adhesive_bead_{i:00}.png"));
                    HOperatorSet.FindPlanarUncalibDeformableModel(img, _planarModel!,
                        -0.39, 0.78, 1, 1, 1, 1, 0.4, 1, 1, 5, 0.9, new HTuple(), new HTuple(),
                        out HTuple hom, out _);
                    HOperatorSet.HomMat2dInvert(hom, out HTuple hInv);
                    HOperatorSet.HomMat2dTranslate(hInv, _rowT!, _colT!, out HTuple hT);
                    HOperatorSet.ProjectiveTransImage(img, out HObject aligned, hT, "bilinear", "false", "false");

                    HObject bd = SegmentBeadByRefDiff(aligned, _refAligned!, _plane, out _);
                    HOperatorSet.Skeleton(bd, out HObject sk);
                    HOperatorSet.GenContoursSkeletonXld(sk, out HObject cs, 5, "filter");
                    HObject line = LongestOf(cs, out double ln);

                    HOperatorSet.DistanceCcMin(refLine, line, "point_to_point", out HTuple dMin);
                    HOperatorSet.DistanceCc(refLine, line, "point_to_point", out _, out HTuple dMax);
                    Ok($"     图{i:00}: 长度={ln,7:0.##}px  最小偏移={dMin.D,7:0.##}px  最大偏移={dMax.D,7:0.##}px");

                    bd.Dispose(); sk.Dispose(); cs.Dispose(); line.Dispose(); aligned.Dispose(); img.Dispose();
                }
                catch (Exception ex)
                {
                    var m = ex.Message; if (m.Length > 60) m = m.Substring(0, 60) + "...";
                    L($"     图{i:00}: 失败 - {m}");
                }
            }

            L("  P9 说明: 上述「最大偏移」含真实胶路偏移与提取误差两部分；");
            L("     定 PositionTolerance 应取「良品图偏移分布」上界再留余量。");
            L("     注意：图01 自身不是经范例验证的良品——范例真值表里 01/02/04 判 OK，");
            L("     故更稳妥的基准应取 01/02/04 的共识中心线，本期先用 01。");

            bead1.Dispose(); skel1.Dispose(); cs1.Dispose(); refLine.Dispose();
        }
        catch (Exception ex) { Bad("位置偏移测量失败: " + ex.Message); }
    }

    // ────────────── H. 微探针：解开 P7 / P6 分支两个遗留问题 ──────────────

    private static void StageH_MicroProbes()
    {
        H("H. 微探针（P7 参数类型 / P6 分支覆盖）");

        if (_aligned1 == null || _plane == null || _refAligned == null) { Bad("前置未就绪"); return; }

        // ── H1: gen_polygons_xld 的 type 到底收什么（用 HTuple 直接试整数/字符串）──
        try
        {
            HObject bead = SegmentBeadByRefDiff(_aligned1, _refAligned, _plane, out _);
            HOperatorSet.Skeleton(bead, out HObject sk);
            HOperatorSet.GenContoursSkeletonXld(sk, out HObject cs, 5, "filter");
            HObject ln = LongestOf(cs, out _);
            HOperatorSet.SmoothContoursXld(ln, out HObject sm, 11);

            L("  H1 gen_polygons_xld type 试探（含整数候选）:");
            var typeCands = new (string label, HTuple val)[]
            {
                ("lines", "lines"), ("lines_color", "lines_color"),
                ("lines_circles", "lines_circles"), ("lines_ellipses", "lines_ellipses"),
                ("max_deviation", "max_deviation"), ("max_distance", "max_distance"),
                ("int 0", 0), ("int 1", 1), ("int 2", 2), ("int 3", 3),
            };
            foreach (var (label, ty) in typeCands)
            {
                try
                {
                    HOperatorSet.GenPolygonsXld(sm, out HObject p, ty, 10.0);
                    HOperatorSet.GetContourXld(p, out HTuple pr, out _);
                    Ok($"     type={label} 可用 → 点数={pr.Length}");
                    p.Dispose();
                }
                catch (Exception ex)
                {
                    var m = ex.Message; int q = m.IndexOf(" in operator");
                    if (q > 0) m = m.Substring(0, q);
                    L($"     type={label} 不可用: {m.Trim()}");
                }
            }

            // ── H2: 分支覆盖 —— 只取最长 vs 全部 ──
            L("  H2 分支覆盖试验（全部分支 vs 只取最长）:");
            HOperatorSet.CountObj(cs, out HTuple nc);
            HOperatorSet.LengthXld(cs, out HTuple lens);
            var la = lens.ToDArr();
            double sum = la.Sum(), mx = la.Max();
            L($"     骨架线 {nc.I} 条: 最长={mx:0.##}px 合计={sum:0.##}px  → 只取最长丢掉 {sum - mx:0.##}px ({(sum - mx) / sum * 100:0.#}%)");
            L($"     ⇒ 只取最长一条不可行：未覆盖的 {sum - mx:0.##}px 会被误判「缺胶」");

            sm.Dispose(); ln.Dispose(); cs.Dispose(); sk.Dispose(); bead.Dispose();
        }
        catch (Exception ex) { Bad("H1/H2 失败: " + ex.Message); }

        // ── H3: 确认「范例 14/7/30 在本坐标系」的正确换算 ──
        // 范例的 14 是它自己矫正坐标系下的值；本仓库图与范例图的平面区尺度不同。
        // 用"同一参考线在两个坐标系下的长度比"作为尺度因子，把 14 换算过来做交叉验证。
        try
        {
            L("  H3 尺度交叉验证（范例参数 → 本坐标系）:");
            // 范例参考线在其坐标系长 1006.23px（已知）；
            // 本坐标系下"同一物理胶路"长 = 提取中心线全长（用合并口径）
            HObject bead = SegmentBeadByRefDiff(_aligned1, _refAligned!, _plane, out _);
            HOperatorSet.Skeleton(bead, out HObject sk);
            HOperatorSet.GenContoursSkeletonXld(sk, out HObject cs, 5, "filter");
            HOperatorSet.LengthXld(cs, out HTuple lens);
            double total = lens.ToDArr().Sum();
            double scale = total / 1006.23;
            L($"     本坐标系胶路全长={total:0.##}px；范例坐标系=1006.23px → 尺度因子={scale:0.000}");
            L($"     范例 TargetWidth=14 换算到本坐标系 ≈ {14 * scale:0.##}px");
            L($"     P3 实测平均胶宽={_measuredWidth:0.##}px  →  两者比值={_measuredWidth / (14 * scale):0.00}");
            L($"     ⇒ 比值接近 1 说明「实测」与「范例参数换算」自洽，可互相印证；偏离大则需查分割口径");
            sk.Dispose(); cs.Dispose(); bead.Dispose();
        }
        catch (Exception ex) { Bad("H3 失败: " + ex.Message); }
    }

    // ────────────── I. 分支顺序与覆盖率（P6 的核心遗留问题）──────────────

    /// <summary>
    /// 骨架在分叉处会被切成多条线（实测本图 3 条，全部交汇于同一点），
    /// 「只取最长一条」会丢掉 31% 路径 → 未覆盖处被误判「缺胶」。
    /// 本阶段验证正确的做法：**去短枝 + 按共享端点串接成一条有序路径**。
    /// </summary>
    private static void StageI_OrderBranches()
    {
        H("I. 分支合并（P6 核心遗留）");

        if (_aligned1 == null || _plane == null || _refAligned == null) { Bad("前置未就绪"); return; }

        try
        {
            HObject bead = SegmentBeadByRefDiff(_aligned1, _refAligned, _plane, out _);
            HOperatorSet.Skeleton(bead, out HObject sk);
            HOperatorSet.GenContoursSkeletonXld(sk, out HObject cs, 5, "filter");
            HOperatorSet.CountObj(cs, out HTuple nSeg);

            var segs = new List<Seg>();
            for (int i = 1; i <= nSeg.I; i++)
            {
                HOperatorSet.SelectObj(cs, out HObject s, i);
                HOperatorSet.GetContourXld(s, out HTuple sr, out HTuple sc);
                HOperatorSet.LengthXld(s, out HTuple sl);
                segs.Add(new Seg(sr.ToDArr(), sc.ToDArr(), sl.D));
                s.Dispose();
            }
            double total = segs.Sum(s => s.Len);
            L($"  骨架段数={segs.Count}  总长={total:0.##}px");
            for (int i = 0; i < segs.Count; i++)
                L($"     段{i + 1}: ({segs[i].R[0]:0.#},{segs[i].C[0]:0.#})→({segs[i].R[^1]:0.#},{segs[i].C[^1]:0.#}) 长={segs[i].Len:0.##}px");

            // ── ① 关键：骨架是"树"，正确路径 = 树的**最长路径（直径）** ──
            // 实测本图是 Y 形：两条长枝 + 一条 20px 短枝，三枝交汇于一点。
            // 「只取最长一条」会丢掉 31% 路径（未覆盖处被误判「缺胶」）——
            // 正确做法是取穿过树的最长路径，短枝自然被排除在外。
            var (pathSegs, pathLen) = LongestPathThroughTree(segs, 20.0);
            Ok($"① 树直径法：取到 {pathSegs.Count}/{segs.Count} 段，覆盖 {pathLen:0.##}px / 总长 {total:0.##}px = {pathLen / total * 100:0.#}%");

            // ── ② 拼成点列（相邻重复点去重）──
            var or2 = new List<double>(); var oc2 = new List<double>();
            foreach (var s in pathSegs)
                for (int i = 0; i < s.R.Length; i++)
                {
                    if (or2.Count > 0 && Dist(or2[^1], oc2[^1], s.R[i], s.C[i]) < 0.5) continue;
                    or2.Add(s.R[i]); oc2.Add(s.C[i]);
                }
            Ok($"② 合并点列: {or2.Count} 点");

            // ── ③ 托管侧等距抽稀（P7 实测 gen_polygons_xld 不可用，见 H1）──
            var (dr, dc) = Downsample(or2, oc2, 40);
            Ok($"③ 抽稀到 {dr.Length} 点（目标 ~40 点，便于人工微调）");

            // ── ④ 用合并点列建模型并跑 7 张图（P6 的最终判据）──
            double tw = Math.Max(_measuredWidth, 6), tol2 = Math.Max(_measuredTol, 1);
            HOperatorSet.GenContourPolygonXld(out HObject merged, new HTuple(dr), new HTuple(dc));
            HOperatorSet.CreateBeadInspectionModel(merged, tw, tol2, 30, "dark",
                new HTuple(), new HTuple(), out HTuple mMerged);
            L($"  ④ 合并点列建模型成功（target={tw:0.#} tol={tol2:0.#}）——检出对照:");
            L("     图号 | 自动提取路径检出        | 范例控制点检出（真值）");
            var truth = new[] { "OK", "OK", "太细1+偏移1", "OK", "缺胶1+太细1+太粗1", "缺胶1", "缺胶1+偏移3" };
            for (int i = 1; i <= 7; i++)
            {
                try
                {
                    HOperatorSet.ReadImage(out HObject img, Path.Combine(Dir, $"adhesive_bead_{i:00}.png"));
                    HOperatorSet.FindPlanarUncalibDeformableModel(img, _planarModel!,
                        -0.39, 0.78, 1, 1, 1, 1, 0.4, 1, 1, 5, 0.9, new HTuple(), new HTuple(),
                        out HTuple hom, out _);
                    HOperatorSet.HomMat2dInvert(hom, out HTuple hInv);
                    HOperatorSet.HomMat2dTranslate(hInv, _rowT!, _colT!, out HTuple hT);
                    HOperatorSet.ProjectiveTransImage(img, out HObject al, hT, "bilinear", "false", "false");
                    HOperatorSet.ApplyBeadInspectionModel(al, out _, out _, out HObject es, mMerged, out HTuple et);
                    int nb = CountOf(et, "no bead"), tn = CountOf(et, "too thin"),
                        tk = CountOf(et, "too thick"), ps = CountOf(et, "incorrect position");
                    L($"     图{i:00} | {et.Length,3}段 [缺胶{nb}/太细{tn}/太粗{tk}/偏移{ps}] | {truth[i - 1]}");
                    es.Dispose(); al.Dispose(); img.Dispose();
                }
                catch (Exception ex) { L($"     图{i:00} | 失败 - {ex.Message}"); }
            }
            HOperatorSet.ClearBeadInspectionModel(mMerged);
            merged.Dispose();
            cs.Dispose(); sk.Dispose(); bead.Dispose();
        }
        catch (Exception ex) { Bad("分支合并失败: " + ex.Message); }
    }

    /// <summary>
    /// 取骨架"树"的最长路径（直径）。返回沿路径有序的段序列与总长。
    ///
    /// 做法：端点按容差聚成节点 → 段成为带权边 → 树直径（两次遍历：任取一点找最远点 A，
    /// 再从 A 找最远点 B，A→B 即直径）→ 按父指针还原路径并逐段定向。
    /// 短枝（不在直径上）自动被排除，无需单独写"去毛刺"规则。
    /// </summary>
    private static (List<Seg> ordered, double length) LongestPathThroughTree(List<Seg> segs, double tol)
    {
        if (segs.Count == 1) return (new List<Seg> { segs[0] }, segs[0].Len);

        // 端点聚类成节点
        var nodeR = new List<double>(); var nodeC = new List<double>();
        int NodeOf(double r, double c)
        {
            for (int i = 0; i < nodeR.Count; i++)
                if (Dist(r, c, nodeR[i], nodeC[i]) < tol) return i;
            nodeR.Add(r); nodeC.Add(c);
            return nodeR.Count - 1;
        }
        var headNode = new int[segs.Count]; var tailNode = new int[segs.Count];
        for (int i = 0; i < segs.Count; i++)
        {
            headNode[i] = NodeOf(segs[i].R[0], segs[i].C[0]);
            tailNode[i] = NodeOf(segs[i].R[^1], segs[i].C[^1]);
        }

        // 邻接：节点 → (段索引, 另一端节点)
        var adj = new List<(int seg, int other)>[nodeR.Count];
        for (int i = 0; i < adj.Length; i++) adj[i] = new List<(int, int)>();
        for (int i = 0; i < segs.Count; i++)
        {
            adj[headNode[i]].Add((i, tailNode[i]));
            adj[tailNode[i]].Add((i, headNode[i]));
        }

        // 两次 DFS 求直径（树无环，visited 即可）
        (int far, Dictionary<int, (int prevNode, int viaSeg)> tree) Walk(int start)
        {
            var prev = new Dictionary<int, (int, int)>();
            var seen = new HashSet<int> { start };
            var stack = new Stack<int>();
            stack.Push(start);
            var dist = new Dictionary<int, double> { [start] = 0 };
            int farNode = start; double farDist = 0;
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                foreach (var (si, v) in adj[u])
                {
                    if (seen.Contains(v)) continue;
                    seen.Add(v);
                    prev[v] = (u, si);
                    dist[v] = dist[u] + segs[si].Len;
                    if (dist[v] > farDist) { farDist = dist[v]; farNode = v; }
                    stack.Push(v);
                }
            }
            return (farNode, prev);
        }

        var (a, _) = Walk(0);
        var (b, prevMap) = Walk(a);

        // 还原 b → a 的段序列
        var pathNodes = new List<int>();
        for (int cur = b; ; cur = prevMap[cur].prevNode)
        {
            pathNodes.Add(cur);
            if (cur == a || !prevMap.ContainsKey(cur)) break;
        }
        pathNodes.Reverse();

        // 按路径顺序取段并定向（每段的头必须接上前一段的尾）
        var ordered = new List<Seg>();
        for (int k = 0; k + 1 < pathNodes.Count; k++)
        {
            int u = pathNodes[k], v = pathNodes[k + 1];
            foreach (var (si, other) in adj[u])
            {
                if (other != v) continue;
                var s = segs[si];
                // 让该段朝向"从 u 出发"：若其尾节点是 u，则翻转
                bool tailIsU = tailNode[si] == u;
                ordered.Add(tailIsU ? s.Reversed() : s);
                break;
            }
        }
        return (ordered, ordered.Sum(s => s.Len));
    }

    private sealed class Seg
    {
        public double[] R; public double[] C; public double Len;
        public Seg(double[] r, double[] c, double len) { R = r; C = c; Len = len; }
        public Seg Reversed()
        {
            var r = (double[])R.Clone(); var c = (double[])C.Clone();
            Array.Reverse(r); Array.Reverse(c);
            return new Seg(r, c, Len);
        }
    }

    /// <summary>等距抽稀到约 n 个点（保端点、保走向）</summary>
    private static (double[] r, double[] c) Downsample(List<double> r, List<double> c, int n)
    {
        if (r.Count <= n) return (r.ToArray(), c.ToArray());
        // 按累计弧长等距取点，保证首末点必取
        var cum = new double[r.Count];
        for (int i = 1; i < r.Count; i++) cum[i] = cum[i - 1] + Dist(r[i - 1], c[i - 1], r[i], c[i]);
        double step = cum[^1] / (n - 1);
        var rr = new List<double> { r[0] }; var cc = new List<double> { c[0] };
        int k = 1;
        for (int j = 1; j < n - 1; j++)
        {
            double target = step * j;
            while (k < r.Count - 1 && cum[k] < target) k++;
            rr.Add(r[k]); cc.Add(c[k]);
        }
        rr.Add(r[^1]); cc.Add(c[^1]);
        return (rr.ToArray(), cc.ToArray());
    }

    private static double Dist(double r1, double c1, double r2, double c2)
        => Math.Sqrt((r1 - r2) * (r1 - r2) + (c1 - c2) * (c1 - c2));

    // ────────────── K. 决定性对照：提取路径 vs 范例参考线（同坐标系？）──────────────

    /// <summary>
    /// 全部结论都取决于一个前提：**对齐后的图与范例的 ContourRef 是否在同一坐标系**。
    ///
    /// 从范例流程推断：`align_bead` 做 hom_mat2d_invert + hom_mat2d_translate(RowT,ColT)，
    /// 把测试图搬到「参考图矫正坐标系 + 平面区锚点」的位置 —— 而 ContourRef 正是画在该坐标系里的。
    /// 若推断成立，则"提取的中心线"到"ContourRef"的距离应当很小（几像素内）；
    /// 若距离很大，说明两个坐标系有系统性偏差，此前所有参数换算都要推翻。
    /// 本阶段用 distance_cc 直接量出来，不再靠推断。
    /// </summary>
    private static void StageK_CompareToReference()
    {
        H("K. 决定性对照：提取中心线 vs 范例参考线");

        if (_aligned1 == null || _plane == null || _refAligned == null) { Bad("前置未就绪"); return; }

        try
        {
            // 范例参考线（其自身坐标系）
            var weights = new HTuple();
            for (int i = 0; i < RefRows.Length; i++) weights = weights.TupleConcat(15.0);
            HOperatorSet.GenContourNurbsXld(out HObject contourRef, new HTuple(RefRows), new HTuple(RefCols),
                "auto", weights, 3, 1, 5);
            HOperatorSet.LengthXld(contourRef, out HTuple refLen);
            HOperatorSet.SmallestRectangle1Xld(contourRef, out HTuple rr1, out HTuple rc1, out HTuple rr2, out HTuple rc2);
            L($"  范例 ContourRef: 长={refLen.D:0.##}px  外接 Row[{rr1.D:0.#},{rr2.D:0.#}] Col[{rc1.D:0.#},{rc2.D:0.#}]");

            // 提取的中心线（树直径合并 + 全点）
            HObject bead = SegmentBeadByRefDiff(_aligned1, _refAligned, _plane, out _);
            HOperatorSet.Skeleton(bead, out HObject sk);
            HOperatorSet.GenContoursSkeletonXld(sk, out HObject cs, 5, "filter");
            HOperatorSet.CountObj(cs, out HTuple nSeg);
            var segs = new List<Seg>();
            for (int i = 1; i <= nSeg.I; i++)
            {
                HOperatorSet.SelectObj(cs, out HObject s, i);
                HOperatorSet.GetContourXld(s, out HTuple sr, out HTuple sc);
                HOperatorSet.LengthXld(s, out HTuple sl);
                segs.Add(new Seg(sr.ToDArr(), sc.ToDArr(), sl.D));
                s.Dispose();
            }
            var (path, pathLen) = LongestPathThroughTree(segs, 20.0);
            var fr = new List<double>(); var fc = new List<double>();
            foreach (var s in path)
                for (int i = 0; i < s.R.Length; i++)
                {
                    if (fr.Count > 0 && Dist(fr[^1], fc[^1], s.R[i], s.C[i]) < 0.5) continue;
                    fr.Add(s.R[i]); fc.Add(s.C[i]);
                }
            HOperatorSet.GenContourPolygonXld(out HObject extLine, new HTuple(fr.ToArray()), new HTuple(fc.ToArray()));
            HOperatorSet.SmallestRectangle1Xld(extLine, out HTuple er1, out HTuple ec1, out HTuple er2, out HTuple ec2);
            L($"  提取中心线: 长={pathLen:0.##}px  外接 Row[{er1.D:0.#},{er2.D:0.#}] Col[{ec1.D:0.#},{ec2.D:0.#}]");

            // ── 正确的"偏离量"：有向 Hausdorff（逐点取到对方最近点的距离，再取最大）──
            // 注意 distance_cc 的 max 是"全体点对的全局最大距离"（≈ 拱形自身的尺度 620px），
            // 不是"提取线偏离范例线多远" —— 用它判读会得出完全错误的结论，故这里自己算。
            HOperatorSet.GetContourXld(contourRef, out HTuple crr, out HTuple crc);
            var refPts = ToPts(crr.ToDArr(), crc.ToDArr());
            var extPts = ToPts(fr.ToArray(), fc.ToArray());

            double hExt2Ref = DirectedHausdorff(extPts, refPts);
            double hRef2Ext = DirectedHausdorff(refPts, extPts);
            double hAvg = AverageNearest(extPts, refPts);
            L($"  提取线→范例线 有向Hausdorff(最大偏离)={hExt2Ref:0.##}px  平均偏离={hAvg:0.##}px");
            L($"  范例线→提取线 有向Hausdorff(最大偏离)={hRef2Ext:0.##}px");

            if (hExt2Ref < 5 && hRef2Ext < 5)
                Ok($"结论：两线最大偏离 <5px（平均 {hAvg:0.##}px）→ **同一坐标系且提取准确**");
            else
                L($"结论：存在最大 {Math.Max(hExt2Ref, hRef2Ext):0.#}px 的局部偏离 → 需定位偏离位置（多为分叉/端点）");

            // 长度比对（骨架链码长度会高估真实弧长，最多 ~8%）
            double ratio = pathLen / refLen.D;
            L($"  长度比 提取/范例 = {ratio:0.000}（骨架链码长度对数字化曲线的固有高估上限约 8%）");
            if (ratio > 1.08)
                L($"     ⚠ 超出链码高估上限 → 提取路径比范例长，可能含分叉残段或多走了一段");
            else
                Ok("     在链码高估范围内 → 提取路径与范例参考线长度一致");

            // 抽稀到 40 点后再测（验证抽稀是否引入偏差）
            var (dr40, dc40) = Downsample(fr, fc, 40);
            var d40 = ToPts(dr40, dc40);
            double h40 = DirectedHausdorff(d40, refPts);
            double h40avg = AverageNearest(d40, refPts);
            L($"  抽稀到 40 点后：最大偏离={h40:0.##}px 平均偏离={h40avg:0.##}px（对比全点 {hExt2Ref:0.##}/{hAvg:0.##}px）");
            if (h40 > hExt2Ref + 1)
                L($"     ⚠ 抽稀使最大偏离增大 {h40 - hExt2Ref:0.##}px → 40 点偏少，建议 80~150 点");
            else
                Ok("     抽稀未显著增大偏离 → 40 点可用");

            contourRef.Dispose();
            cs.Dispose(); sk.Dispose(); bead.Dispose();
        }
        catch (Exception ex) { Bad("对照失败: " + ex.Message); }
    }

    private static List<(double r, double c)> ToPts(double[] r, double[] c)
    {
        var l = new List<(double, double)>(r.Length);
        for (int i = 0; i < r.Length; i++) l.Add((r[i], c[i]));
        return l;
    }

    /// <summary>有向 Hausdorff：对 A 的每个点求到 B 最近点的距离，取最大值</summary>
    private static double DirectedHausdorff(List<(double r, double c)> a, List<(double r, double c)> b)
    {
        double worst = 0;
        foreach (var p in a)
        {
            double best = double.MaxValue;
            foreach (var q in b)
            {
                double d = Dist(p.r, p.c, q.r, q.c);
                if (d < best) best = d;
            }
            if (best > worst) worst = best;
        }
        return worst;
    }

    /// <summary>A 各点到 B 的最近距离的平均值（整体贴合度）</summary>
    private static double AverageNearest(List<(double r, double c)> a, List<(double r, double c)> b)
    {
        double sum = 0;
        foreach (var p in a)
        {
            double best = double.MaxValue;
            foreach (var q in b)
            {
                double d = Dist(p.r, p.c, q.r, q.c);
                if (d < best) best = d;
            }
            sum += best;
        }
        return sum / a.Count;
    }

    /// <summary>
    /// Stage I 用 40 点抽稀后，对范例判 OK 的图（01/02/04）仍报错误段。
    /// 本阶段分离两个嫌疑：①抽稀太狠（路径偏离）②target/tolerance 口径不符。
    /// 做法：同一份提取点列，扫「点数 × tolerance」，看哪组能回到范例真值。
    /// </summary>
    private static void StageJ_PointCount()
    {
        H("J. 点数与容差对检出的影响（定位过度报错）");

        if (_aligned1 == null || _plane == null || _refAligned == null) { Bad("前置未就绪"); return; }

        try
        {
            HObject bead = SegmentBeadByRefDiff(_aligned1, _refAligned, _plane, out _);
            HOperatorSet.Skeleton(bead, out HObject sk);
            HOperatorSet.GenContoursSkeletonXld(sk, out HObject cs, 5, "filter");
            HOperatorSet.CountObj(cs, out HTuple nSeg);

            var segs = new List<Seg>();
            for (int i = 1; i <= nSeg.I; i++)
            {
                HOperatorSet.SelectObj(cs, out HObject s, i);
                HOperatorSet.GetContourXld(s, out HTuple sr, out HTuple sc);
                HOperatorSet.LengthXld(s, out HTuple sl);
                segs.Add(new Seg(sr.ToDArr(), sc.ToDArr(), sl.D));
                s.Dispose();
            }
            var (path, pathLen) = LongestPathThroughTree(segs, 20.0);
            var fr = new List<double>(); var fc = new List<double>();
            foreach (var s in path)
                for (int i = 0; i < s.R.Length; i++)
                {
                    if (fr.Count > 0 && Dist(fr[^1], fc[^1], s.R[i], s.C[i]) < 0.5) continue;
                    fr.Add(s.R[i]); fc.Add(s.C[i]);
                }
            L($"  基准：树直径路径 {pathLen:0.##}px，原始点 {fr.Count} 个");

            // 预先对齐 7 张图（只做一次，后面反复用）
            var aligned = new List<(int idx, HObject img, HTuple score)>();
            for (int i = 1; i <= 7; i++)
            {
                HOperatorSet.ReadImage(out HObject img, Path.Combine(Dir, $"adhesive_bead_{i:00}.png"));
                HOperatorSet.FindPlanarUncalibDeformableModel(img, _planarModel!,
                    -0.39, 0.78, 1, 1, 1, 1, 0.4, 1, 1, 5, 0.9, new HTuple(), new HTuple(),
                    out HTuple hom, out HTuple sc);
                HOperatorSet.HomMat2dInvert(hom, out HTuple hInv);
                HOperatorSet.HomMat2dTranslate(hInv, _rowT!, _colT!, out HTuple hT);
                HOperatorSet.ProjectiveTransImage(img, out HObject al, hT, "bilinear", "false", "false");
                aligned.Add((i, al, sc));
            }

            var truth = new[] { 0, 0, 2, 0, 3, 1, 4 };   // 范例真值的错误段数
            L("  扫描（点数 × tolerance）→ 各图错误段数 [真值]：");
            foreach (int nPts in new[] { 0, 40, 80, 150, 300 })   // 0 = 不抽稀
            {
                double[] rs; double[] cs2;
                if (nPts == 0) { rs = fr.ToArray(); cs2 = fc.ToArray(); }
                else { (rs, cs2) = Downsample(fr, fc, nPts); }

                foreach (double tol in new[] { 1.0, 2.0, 4.0, 8.0 })
                {
                    try
                    {
                        HOperatorSet.GenContourPolygonXld(out HObject c, new HTuple(rs), new HTuple(cs2));
                        HOperatorSet.CreateBeadInspectionModel(c, Math.Max(_measuredWidth, 6), tol, 30, "dark",
                            new HTuple(), new HTuple(), out HTuple mm);
                        var counts = new List<int>();
                        foreach (var (idx, al, _) in aligned)
                        {
                            HOperatorSet.ApplyBeadInspectionModel(al, out _, out _, out HObject es, mm, out HTuple et);
                            counts.Add(et.Length);
                            es.Dispose();
                        }
                        string diff = string.Join(",", counts.Select((v, i) => (v - truth[i]).ToString("+0;-0;0")));
                        L($"     点={rs.Length,4} tol={tol,4:0.#} → 段数 [{string.Join(",", counts)}]  偏差[{diff}]");
                        HOperatorSet.ClearBeadInspectionModel(mm);
                        c.Dispose();
                    }
                    catch (Exception ex) { L($"     点={rs.Length} tol={tol} 失败: {ex.Message}"); }
                }
            }
            L("  判读：偏差全 0 的那组 = 正确口径；若只有高点数才归零 → 抽稀太狠；");
            L("       若任何组都归不了零 → 提取路径本身偏离参考胶路（需查分割/坐标系）。");

            foreach (var (_, al, _) in aligned) al.Dispose();
            cs.Dispose(); sk.Dispose(); bead.Dispose();
        }
        catch (Exception ex) { Bad("点数/容差扫描失败: " + ex.Message); }
    }
}
