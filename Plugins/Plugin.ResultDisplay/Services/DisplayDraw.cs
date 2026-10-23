using Core.Halcon.Helpers;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Plugin.ResultDisplay.Services
{
    /// <summary>一个结果项本轮的"定义 + 取到的值"。</summary>
    internal readonly record struct DisplayItem(DisplayItemDef Def, object? Value);

    /// <summary>
    /// <see cref="DisplayDraw.Build"/> 的产物。
    /// <see cref="TempObjects"/> 是本次为画轮廓**新生成**的 HALCON 对象（上游给的区域/轮廓不在里面，
    /// 那些所有权归上游），调用方在 Bake 之后必须 <see cref="DisposeTemps"/>。
    /// </summary>
    internal sealed class DisplayDrawResult
    {
        public BakePlan Plan { get; } = new();
        public List<ImageInfoRow> InfoRows { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<HObject> TempObjects { get; } = new();

        /// <summary>本轮真的画进图里的结果项数（一个项可能画多个元素）</summary>
        public int DisplayedCount { get; internal set; }

        public void DisposeTemps()
        {
            foreach (var o in TempObjects)
            {
                try { o.Dispose(); } catch { /* 中间对象释放失败不阻断 */ }
            }
            TempObjects.Clear();
        }
    }

    /// <summary>
    /// "结果项 → 图上要画什么"的翻译层：把任意类型的上游输出翻成 <see cref="BakePlan"/> + 悬停信息行。
    ///
    /// ★ **这份文件是全仓「结果 → 图上标注」的收敛点**：后续插件要在图上显示结果，请把输出接到
    /// 「结果显示」算子上，**不要再自己拼标注**。它取代的两批旧实现（迁移单开一轮）：
    ///   · 覆盖层五份（画进 ImageFrame.Annotations，由宿主 RenderAnnotations 叠图）
    ///     Plugin.GeometryFinder\Services\GeometryHService.cs:77,90 / Plugin.ColorRegion\RegionColorPlugin.cs:447-483 /
    ///     Plugin.BeadInspect\BeadInspectPluginConfigView.cs:797-838 / Plugin.Calibration\CalibrationPlugin.cs:462-504 /
    ///     Plugin.CaliperMeasure\CaliperMeasurePlugin.cs:1434-1436
    ///   · 离屏烘焙五份（自己开 buffer 窗口）Plugin.BlobDetect\AnnotationRenderer.cs /
    ///     Plugin.CaliperMeasure\AnnotationRenderer.cs / Plugin.BeadInspect\BeadInspectPlugin.cs:818-939 /
    ///     Plugin.Matching\MatchingPlugin.cs:1866-1982 / Plugin.ImageScript\HDevDisplayBackend.cs
    ///
    /// ⚠ 分工界线（正是五份旧实现里"基础设施逐字相同、图层清单各不相同"那条真实分界）：
    ///   **窗口 / 回读 / 释放 / 色名归一化 / 线宽还原 → <see cref="AnnotationBaker"/>（Core.Halcon 共享件）；
    ///   "画什么"（类型分派、行主口径几何、排版、钳制、元素上限）→ 本文件（插件私有）。**
    /// 本文件**不许**出现 OpenWindow / DumpWindowImage / SetPart —— 那属于共享件的职责。
    /// </summary>
    internal static class DisplayDraw
    {
        /// <summary>几何固定 cyan：与"判定色"（green/red）区分开，避免满屏红绿看不出哪个是判定</summary>
        internal const string GeometryColor = "cyan";
        /// <summary>判定通过（照 Services\Core.Halcon\Color\ColorMarks.cs:9-22 的全仓画面语言）</summary>
        internal const string OkColor = "green";
        /// <summary>判定失败 / 不支持的值</summary>
        internal const string NgColor = "red";
        /// <summary>提醒（元素被截断）</summary>
        internal const string NoteColor = "orange";

        /// <summary>轮廓/折线的点数抽稀上限：超过就等间隔抽样（首末点必留）</summary>
        internal const int MaxContourPoints = 2000;

        /// <summary>HTuple 逐元素分派的上限（超出截断并追加 …(+N)）</summary>
        internal const int MaxTupleElements = 32;

        public static DisplayDrawResult Build(
            IReadOnlyList<DisplayItem>? items,
            int width,
            int height,
            bool showTextOnImage,
            string? judgeItemLabel,
            string? ngValue,
            int maxDrawElements,
            TextAnchorKind textAnchor = TextAnchorKind.TopLeft,
            int textMargin = 0,
            double textScale = 1.0)
        {
            var res = new DisplayDrawResult();
            var ctx = new Ctx(res, Math.Max(1, width), Math.Max(1, height), showTextOnImage, maxDrawElements,
                textAnchor, textMargin, textScale);
            ctx.TextColor = IsNg(items, judgeItemLabel, ngValue) ? NgColor : OkColor;

            if (items != null)
            {
                foreach (var it in items)
                {
                    if (it.Def == null) continue;
                    int before = ctx.Used;
                    try
                    {
                        Dispatch(it, ctx);
                    }
                    catch (Exception ex)
                    {
                        // 单项翻译失败只丢这一项：显示算子绝不能把一次成功的检测判成失败
                        ctx.Warn($"结果项「{LabelOf(it.Def)}」处理失败（已跳过该项）：{ex.GetType().Name}: {ex.Message}");
                    }
                    if (ctx.Used > before) res.DisplayedCount++;
                }
            }

            if (ctx.Truncated)
                ctx.AddTextNote($"（元素数超上限，仅绘制前 {ctx.Budget} 个元素）");

            // ★ 堆叠文字到这才落位：锚点的垂直分量要的是"整块多高"，而行数要到派发完才知道。
            //   安全前提：烘焙件按**类别**画（Shapes → Objects → Texts，AnnotationBaker.cs:148-150），
            //   文字入列早晚都不影响图层顺序；受影响的只有文字之间的叠压次序（见 Ctx.EmitStack 注释）。
            ctx.EmitStack();

            if (res.InfoRows.Count == 0)
                res.InfoRows.Add(new ImageInfoRow("结果", "未接入结果项"));

            return res;
        }

        // ────────────────────────── 类型分派 ──────────────────────────

        private static void Dispatch(DisplayItem it, Ctx ctx)
        {
            var def = it.Def;
            string label = LabelOf(def);
            object? v = it.Value;

            if (v == null)
            {
                // 未连线 / 上游失败：不画东西，但悬停里要能看出来"这项是空的"
                ctx.Info(label, "(空)");
                return;
            }

            // 判型顺序必须 HImage → GeometryParam → HRegion → HXLD → HObject：
            // HImage / HRegion / HXLD 都派生自 HObject，顺序错了会走错分支（范式 ImageScriptPlugin.cs:1101-1103）
            switch (v)
            {
                case HImage:
                    RejectImage(label, ctx);
                    return;
                case GeometryParam g:
                    DrawGeometry(label, g, ctx);
                    return;
                case HRegion region:
                    DrawHalconObject(label, region, "区域", ctx);
                    return;
                case HXLD xld:
                    DrawHalconObject(label, xld, "轮廓", ctx);
                    return;
                case HTuple tuple:
                    DrawTuple(label, def, tuple, ctx);
                    return;
                // ★ A3：数组类输出（CodeReader.Codes / Yolo.ClassNames/Confidences/Boxes …）**必须排在 default 之前**。
                //   `string[]` / `int[]` / `double[]` 都不是 IConvertible，落到 default 会被当成「不支持的类型」
                //   在图上写一行红字——输出本身是好的，红字纯属误伤。
                case string[] sarr:
                    DrawArray(label, def, sarr, ctx);
                    return;
                case int[] iarr:
                    DrawArray(label, def, iarr, ctx);
                    return;
                case double[] darr:
                    DrawArray(label, def, darr, ctx);
                    return;
                case bool[] barr:
                    DrawArray(label, def, barr, ctx);
                    return;
                case Array array:
                    DrawArray(label, def, array, ctx);
                    return;
                case HObject obj:
                    DrawHalconObject(label, obj, "对象", ctx);
                    return;
                default:
                    DrawScalar(label, def, v, ctx);
                    return;
            }
        }

        /// <summary>
        /// 图像值一律拒收：底图只有 <c>SrcImage</c> 一个入口。
        /// 不拒收的后果是"往离屏窗口 disp_obj 一张图" = 把底图盖掉，用户看到一张莫名其妙的图。
        /// </summary>
        private static void RejectImage(string label, Ctx ctx)
        {
            ctx.Warn($"结果项「{label}」不支持图像值（底图请用 SrcImage），已在图上标红字");
            ctx.AddTextLine($"{label}: 不支持图像值（底图请用 SrcImage）", NgColor);
            ctx.Info(label, "(不支持图像值)");
        }

        private static void DrawHalconObject(string label, HObject obj, string kindName, Ctx ctx)
        {
            if (!HasContent(obj))
            {
                ctx.Info(label, $"(空{kindName})");
                return;
            }
            // 上游给的对象：所有权在上游，本插件只读不释放（所以不进 TempObjects）
            ctx.AddObj(obj, GeometryColor, 2, "margin");
            ctx.Info(label, kindName);
        }

        private static void DrawScalar(string label, DisplayItemDef def, object v, Ctx ctx)
        {
            // 认得出来的标量 = IConvertible（string/bool/enum/DateTime/全部数值型）。
            // 认不出来的（自定义模型、HALCON 句柄等）**不抛**：红字写清类型全名与被拒的原值（R34 宽进严出）
            if (v is not IConvertible)
            {
                string tn = v.GetType().FullName ?? v.GetType().Name;
                ctx.Warn($"结果项「{label}」不支持的类型 {tn}，已在图上标红字");
                ctx.AddTextLine($"{label}: 不支持的类型 {tn} = {Trunc(Safe(v), 60)}", NgColor);
                ctx.Info(label, $"不支持的类型 {tn}");
                return;
            }

            string text = FormatValue(v, def.Format) + (def.Unit ?? "");
            ctx.AddTextLine($"{label}: {text}", ctx.TextColor);
            ctx.Info(label, text);
        }

        private static void DrawTuple(string label, DisplayItemDef def, HTuple tuple, Ctx ctx)
        {
            int n = tuple.Length;
            if (n == 0)
            {
                ctx.Info(label, "(空元组)");
                return;
            }

            int take = Math.Min(n, MaxTupleElements);
            var parts = new List<string>(take);
            for (int i = 0; i < take; i++)
                parts.Add(FormatScalar(TupleElement(tuple[i]), def.Format));
            if (n > take) parts.Add($"…(+{n - take})");

            string joined = string.Join(", ", parts);
            ctx.AddTextLine($"{label}: {Trunc(joined, 240)}", ctx.TextColor);
            ctx.Info(label, joined);
        }

        /// <summary>
        /// A3：数组类输出 → 一行文字（逐元素上限 <see cref="MaxTupleElements"/>）。
        ///
        /// ★ 特例：**扁平框数组画框**。YOLO 的 <c>Boxes</c> 是 <c>double[]</c>，每 4 个一组 <c>x1,y1,x2,y2</c>
        /// （见 <c>Plugin.Yolo\YoloPlugin.cs</c> 端口注释）。长度是 4 的倍数且每组的 x2&gt;x1、y2&gt;y1 时，
        /// 除了文字还画 N 个绿框 + 序号，用户不用再自己去对"第 3 个框在哪"。
        /// 判据不满足（长度不是 4 的倍数 / 有反向框）就**只出文字**并 Warn —— 绝不猜测另一种布局。
        /// </summary>
        private static void DrawArray(string label, DisplayItemDef def, Array array, Ctx ctx)
        {
            int n = array.Length;
            if (n == 0)
            {
                ctx.Info(label, "(空数组)");
                return;
            }

            // 先做文字行（与 HTuple 同口径：截断 + 省略号，不静默丢数据）
            int take = Math.Min(n, MaxTupleElements);
            var parts = new List<string>(take);
            for (int i = 0; i < take; i++)
                parts.Add(FormatScalar(array.GetValue(i), def.Format));
            if (n > take) parts.Add($"…(+{n - take})");

            string joined = string.Join(", ", parts);
            ctx.AddTextLine($"{label}: {Trunc(joined, 240)}", ctx.TextColor);
            ctx.Info(label, joined);

            if (array is double[] boxes)
                TryDrawFlatBoxes(label, boxes, ctx);
        }

        /// <summary>
        /// 扁平框数组 → N 个框。用 4 条 <see cref="BakeShapeKind.Line"/> 而不是 Rectangle：
        /// <c>BakeShapeKind</c> 的矩形参数口径（角点 vs 中心 + 角度）与这里的 x1,y1,x2,y2 不是一回事，
        /// 硬套会把框画歪；四条边没有任何歧义。
        /// </summary>
        private static void TryDrawFlatBoxes(string label, double[] boxes, Ctx ctx)
        {
            if (boxes.Length < 4 || boxes.Length % 4 != 0) return;

            int count = boxes.Length / 4;
            for (int i = 0; i < count; i++)
            {
                double x1 = boxes[i * 4], y1 = boxes[i * 4 + 1], x2 = boxes[i * 4 + 2], y2 = boxes[i * 4 + 3];
                if (!double.IsFinite(x1) || !double.IsFinite(y1) || !double.IsFinite(x2) || !double.IsFinite(y2)) return;
                if (x2 <= x1 || y2 <= y1)
                {
                    // 反向框 = 不是"每 4 个一组 x1,y1,x2,y2"这种布局，退化为纯文字，不猜
                    ctx.Warn($"结果项「{label}」的数组长度是 4 的倍数但第 {i + 1} 组不是正向框（{x1},{y1},{x2},{y2}），已只按文字显示");
                    return;
                }
            }

            for (int i = 0; i < count; i++)
            {
                double x1 = boxes[i * 4], y1 = boxes[i * 4 + 1], x2 = boxes[i * 4 + 2], y2 = boxes[i * 4 + 3];
                ctx.AddShape(BakeShapeKind.Line, new[] { y1, x1, y1, x2 }, OkColor, 2);   // 上边
                ctx.AddShape(BakeShapeKind.Line, new[] { y2, x1, y2, x2 }, OkColor, 2);   // 下边
                ctx.AddShape(BakeShapeKind.Line, new[] { y1, x1, y2, x1 }, OkColor, 2);   // 左边
                ctx.AddShape(BakeShapeKind.Line, new[] { y1, x2, y2, x2 }, OkColor, 2);   // 右边
                ctx.AddTextAt($"{label}{i + 1}", Math.Max(0, y1 - ctx.FontSize), x1, OkColor);
            }
        }

        // ────────────────────────── GeometryParam → 图形 ──────────────────────────

        /// <summary>
        /// ★ 行主口径，全插件最容易静默错的一处。
        /// <see cref="GeometryParam.Phi"/> 的定义是 <c>atan2(dr, dc)</c>（GeometryHService.cs:63-67），
        /// 所以**方向向量 = (dr, dc) = (sin φ, cos φ)**，不是数学惯例的 (cos, sin)。
        /// 套错就把水平线画成竖线（45° 镜像）——图形看着"差不多直"，量全错。
        /// </summary>
        private static void DrawGeometry(string label, GeometryParam g, Ctx ctx)
        {
            double rad = g.Phi * Math.PI / 180.0;
            double dr = Math.Sin(rad), dc = Math.Cos(rad);
            double crossSize = Math.Max(6, ctx.Height * 0.01);

            switch (g.Kind)
            {
                case GeometryKind.Point:
                    ctx.AddShape(BakeShapeKind.Cross, new[] { g.Row, g.Col, crossSize }, GeometryColor, 2);
                    ctx.AddTextAt(label, g.Row - crossSize - ctx.FontSize, g.Col + crossSize, GeometryColor);
                    ctx.Info(label, g.ToString());
                    break;

                case GeometryKind.Line:
                {
                    // 线是"过 (Row,Col)、方向 φ"的无限长线，画多长由图幅定：取短边的 1/3 当半长
                    double half = Math.Clamp(Math.Min(ctx.Width, ctx.Height) / 3.0, 40, 4000);
                    ctx.AddShape(BakeShapeKind.Line, new[]
                    {
                        g.Row - half * dr, g.Col - half * dc,
                        g.Row + half * dr, g.Col + half * dc
                    }, GeometryColor, 2);
                    ctx.AddTextAt($"{label} φ={g.Phi.ToString("0.0", CultureInfo.InvariantCulture)}°",
                        g.Row + 4, g.Col + 4, GeometryColor);
                    ctx.Info(label, g.ToString());
                    break;
                }

                case GeometryKind.Circle:
                    if (!(g.Radius > 0))
                    {
                        ctx.Warn($"结果项「{label}」圆半径非法（{g.Radius}），已跳过");
                        ctx.Info(label, g.ToString());
                        break;
                    }
                    ctx.AddShape(BakeShapeKind.Circle, new[] { g.Row, g.Col, g.Radius }, GeometryColor, 2);
                    ctx.AddShape(BakeShapeKind.Cross, new[] { g.Row, g.Col, crossSize }, GeometryColor, 1);
                    ctx.AddTextAt($"{label} r={g.Radius.ToString("0.0", CultureInfo.InvariantCulture)}",
                        g.Row + 4, g.Col + 4, GeometryColor);
                    ctx.Info(label, g.ToString());
                    break;

                case GeometryKind.Ellipse:
                    if (!(g.Radius > 0) || !(g.Radius2 > 0))
                    {
                        ctx.Warn($"结果项「{label}」椭圆半轴非法（{g.Radius}×{g.Radius2}），已跳过");
                        ctx.Info(label, g.ToString());
                        break;
                    }
                    // ★ 角度符号必须取反：本仓 GeometryParam.Phi 的方向向量是 (sin φ, cos φ)（行主），
                    //   而 HALCON 的 gen_ellipse / disp_ellipse 的 Phi 是"自列轴逆时针"，
                    //   在"行向下"的图像系里等价于方向向量 (−sin Phi, cos Phi)（同一口径也用于
                    //   Plugin.CreateRoi\CreateRoiPlugin.cs:881 直接把 phi 交给 GenEllipse）。
                    //   两边差一个负号，不取反就把长轴镜像到另一条对角线上——φ=0/90 时看不出来，φ=45 才现形。
                    ctx.AddShape(BakeShapeKind.Ellipse,
                        new[] { g.Row, g.Col, -g.Phi, g.Radius, g.Radius2 }, GeometryColor, 2);
                    ctx.AddTextAt($"{label} {g.Radius.ToString("0.0", CultureInfo.InvariantCulture)}×{g.Radius2.ToString("0.0", CultureInfo.InvariantCulture)}",
                        g.Row + 4, g.Col + 4, GeometryColor);
                    ctx.Info(label, g.ToString());
                    break;

                case GeometryKind.Contour:
                    DrawContourPoints(label, g, ctx);
                    break;

                default:
                    ctx.Warn($"结果项「{label}」未知几何种类 {g.Kind}，已跳过");
                    ctx.Info(label, g.ToString());
                    break;
            }
        }

        /// <summary>
        /// 轮廓 → 一条 XLD 折线（生成一次、DispObj、由调用方在 Bake 之后释放）。
        /// 不退化成逐段 DispLine：点数一多就把元素预算吃光（300 的上限几十个点就用完了）。
        /// </summary>
        private static void DrawContourPoints(string label, GeometryParam g, Ctx ctx)
        {
            var pts = g.Points;
            int coordCount = pts?.Length ?? 0;
            if (coordCount < 4)
            {
                ctx.Warn($"结果项「{label}」轮廓点数不足（{coordCount} 个坐标，至少要 2 个点），已跳过");
                ctx.Info(label, g.ToString());
                return;
            }

            for (int i = 0; i < coordCount; i++)
            {
                if (double.IsFinite(pts![i])) continue;
                ctx.Warn($"结果项「{label}」轮廓第 {i} 个坐标非有限值（{pts[i]}），已跳过整条轮廓");
                ctx.Info(label, g.ToString());
                return;
            }

            var thinned = ThinPoints(pts!, MaxContourPoints);
            int n = thinned.Length / 2;
            var rows = new double[n];
            var cols = new double[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = thinned[2 * i];
                cols[i] = thinned[2 * i + 1];
            }

            HOperatorSet.GenContourPolygonXld(out HObject xld, new HTuple(rows), new HTuple(cols));
            ctx.Temp.Add(xld);                 // 本插件自己造的 → 用完必须释放
            ctx.AddObj(xld, GeometryColor, 2, "margin");
            ctx.Info(label, g.ToString() + (n < coordCount / 2 ? $"（已抽稀到 {n} 点）" : ""));
        }

        /// <summary>等间隔抽样到 ≤ maxPoints 个点；首末点必留（否则轮廓开口）。</summary>
        private static double[] ThinPoints(double[] pts, int maxPoints)
        {
            int n = pts.Length / 2;
            if (n <= maxPoints) return pts;

            var outPts = new double[maxPoints * 2];
            for (int i = 0; i < maxPoints; i++)
            {
                int src = (int)Math.Round((double)i * (n - 1) / (maxPoints - 1));
                outPts[2 * i] = pts[2 * src];
                outPts[2 * i + 1] = pts[2 * src + 1];
            }
            return outPts;
        }

        // ────────────────────────── 格式化 ──────────────────────────

        /// <summary>
        /// 值 → 文本。一律 <see cref="CultureInfo.InvariantCulture"/>：别让机器的区域设置改显示结果
        /// （德语区把 12.5 显示成 12,5，现场核对数值时最容易被这类"看着对"的文本骗过去，R34 同族）。
        /// </summary>
        private static string FormatValue(object? v, string? format)
        {
            string fmt = format?.Trim() ?? "";
            switch (v)
            {
                case null: return "(空)";
                case bool b: return b ? "OK" : "NG";
                case string s: return s;
                case DateTime dt:
                    return SafeFormat(() => dt.ToString(fmt.Length == 0 ? "HH:mm:ss.fff" : fmt, CultureInfo.InvariantCulture),
                        dt.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
                case Enum e: return e.ToString();
                case double d: return Num(d, fmt);
                case float f: return Num(f, fmt);
                case decimal m: return Num((double)m, fmt);
                // IFormattable 才有 (格式串, 区域性) 这个重载；IConvertible 只有 (区域性)。
                // 整型/字符型走这里（int 也认 "F2"）；两者都不是的（如 HTuple 装着的怪东西）落到默认档。
                case IFormattable formattable:
                    return SafeFormat(
                        () => formattable.ToString(fmt.Length == 0 ? null : fmt, CultureInfo.InvariantCulture),
                        formattable.ToString(null, CultureInfo.InvariantCulture));
                default: return Trunc(Safe(v), 60);
            }
        }

        private static string FormatScalar(object? v, string? format) => FormatValue(v, format);

        private static string Num(double d, string fmt)
        {
            if (!double.IsFinite(d)) return d.ToString(CultureInfo.InvariantCulture);
            return fmt.Length == 0
                ? d.ToString("0.####", CultureInfo.InvariantCulture)
                : SafeFormat(() => d.ToString(fmt, CultureInfo.InvariantCulture),
                             d.ToString("0.####", CultureInfo.InvariantCulture));
        }

        /// <summary>用户手填的格式串可能是垃圾（如 "{"）：格式化失败退回默认，绝不让显示算子抛出去。</summary>
        private static string SafeFormat(Func<string> act, string fallback)
        {
            try { return act(); } catch { return fallback; }
        }

        /// <summary>
        /// HTuple 元素 → 可格式化的值。刻意不用 <c>HTuple.Type</c> 判型：数值优先试 <c>.D</c>、
        /// 再试 <c>.S</c>，两条都抛就退回 ToString —— 类型判错最多显示得难看，不会崩。
        /// </summary>
        private static object? TupleElement(HTuple e)
        {
            try { return e.D; } catch { /* 不是数值 */ }
            try { return e.S; } catch { /* 不是字符串 */ }
            try { return e.ToString(); } catch { return null; }
        }

        // ────────────────────────── 判定（决定文字颜色） ──────────────────────────

        /// <summary>
        /// 本件是否 NG：判定项的值与 NG 判定值**智能比对** ——
        /// 两边都能解析成数值按数值比（12.0 能对上 "12"），都能解析成布尔按布尔比（True 能对上 true），
        /// 都不行才退回字符串比（不区分大小写）。口径与 Plugin.ResultUpload\ResultUploadPlugin.cs:592-628 一致。
        /// 判定项按 <see cref="DisplayItemDef.Label"/> 找（找不到再按 Id 找一次，宽容）。
        /// </summary>
        private static bool IsNg(IReadOnlyList<DisplayItem>? items, string? judgeItemLabel, string? ngValue)
        {
            string key = (judgeItemLabel ?? "").Trim();
            if (key.Length == 0 || items == null) return false;
            string cfg = (ngValue ?? "").Trim();
            if (cfg.Length == 0) return false;

            foreach (var it in items)
            {
                if (it.Def == null) continue;
                if (!string.Equals((it.Def.Label ?? "").Trim(), key, StringComparison.Ordinal) &&
                    !string.Equals((it.Def.Id ?? "").Trim(), key, StringComparison.Ordinal)) continue;

                object? v = it.Value;
                if (v == null) return false;
                if (TryAsDouble(v, out double a) &&
                    double.TryParse(cfg, NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
                    return a.Equals(b);
                if (TryAsBool(v, out bool ba) && TryParseJudgeBool(cfg, out bool bb))
                    return ba == bb;
                return string.Equals(ToPlainString(v), cfg, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        /// <summary>
        /// 判定值的真假解析。**比 <c>bool.TryParse</c> 多认 OK / NG / 1 / 0**，理由是本文件自己
        /// 把 bool 显示成 "OK"/"NG"（见 <see cref="FormatValue"/>）：若判定只认 "true"/"false"，
        /// 就会出现"图上那行字写着 NG、整幅标注却是绿色"的自相矛盾 —— 现场最容易被这种
        /// "看着对"的输出骗过去（R34 同族）。
        /// </summary>
        private static bool TryParseJudgeBool(string text, out bool result)
        {
            if (bool.TryParse(text, out result)) return true;
            if (string.Equals(text, "NG", StringComparison.OrdinalIgnoreCase) ||
                text == "0") { result = false; return true; }
            if (string.Equals(text, "OK", StringComparison.OrdinalIgnoreCase) ||
                text == "1") { result = true; return true; }
            return false;
        }

        private static bool TryAsDouble(object v, out double d)
        {
            switch (v)
            {
                case double x: d = x; return true;
                case float x: d = x; return true;
                case decimal x: d = (double)x; return true;
                case int x: d = x; return true;
                case long x: d = x; return true;
                case short x: d = x; return true;
                case byte x: d = x; return true;
                case bool: d = 0; return false;
                // NumberStyles.Float（不是 Any）：Any 含 AllowThousands，"1,5" 会被读成 15 —— R34 明令拒绝
                default:
                    return double.TryParse(ToPlainString(v), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            }
        }

        private static bool TryAsBool(object v, out bool b)
        {
            if (v is bool x) { b = x; return true; }
            return bool.TryParse(ToPlainString(v), out b);
        }

        private static string ToPlainString(object? v) => v is string s ? s : (v?.ToString() ?? "");

        // ────────────────────────── 工具 ──────────────────────────

        private static string LabelOf(DisplayItemDef def)
        {
            string label = (def.Label ?? "").Trim();
            return label.Length == 0 ? (def.Id ?? "").Trim() : label;
        }

        /// <summary>
        /// 空对象集没有"类型"可言：HALCON 画空对象是合法的（什么都不画），但取类型/取元素会抛，
        /// 所以先数个数（与 AnnotationBaker.HasContent 同一判据）。
        /// </summary>
        private static bool HasContent(HObject o)
        {
            if (o == null || !o.IsInitialized()) return false;
            HOperatorSet.CountObj(o, out HTuple number);
            return number.Length > 0 && number[0].I > 0;
        }

        private static string Safe(object? v)
        {
            try { return v?.ToString() ?? ""; } catch { return "(无法取值)"; }
        }

        private static string Trunc(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s!.Length <= max ? s : s.Substring(0, max) + "…";
        }

        // ────────────────────────── 排版 / 预算 / 钳制 ──────────────────────────

        /// <summary>
        /// 一次 Build 的上下文：排版参数、元素预算、颜色、告警与信息行出口。
        ///
        /// 【排版口径】堆叠文字块的位置由 <paramref name="Anchor"/> 决定（九宫格），几何项的文字标签
        /// 不受它影响（永远跟着图形走）。字号按图高自适应再乘 <see cref="Scale"/>，行距跟着字号走。
        /// 颜色仍按判定自动 —— 只把"压住工件"这一件现场真抱怨交给用户，其余继续自动（D7）。
        /// </summary>
        private sealed class Ctx
        {
            private readonly DisplayDrawResult _res;

            /// <summary>堆叠文字先收集、Build 末尾才按锚点落位（垂直锚点要的是整块高度 = 行数 × 行距）</summary>
            private readonly List<(string Text, string Color)> _stack = new();

            public Ctx(
                DisplayDrawResult res, int width, int height, bool showText, int maxDrawElements,
                TextAnchorKind anchor, int margin, double scale)
            {
                _res = res;
                Width = width;
                Height = height;
                ShowText = showText;
                Budget = Math.Max(1, maxDrawElements);
                Anchor = anchor;

                // 0 = 沿用自动值（老方案里用户根本没有这一项，行为必须一模一样）
                Margin = margin > 0 ? margin : Math.Max(12, height * 0.02);

                Scale = double.IsFinite(scale) ? Math.Clamp(scale, 0.5, 3.0) : 1.0;
                // 既有五份烘焙实现硬编码 20/16 号字，因为它们的图尺寸已知；通用算子必须按图高自适应
                int autoFontSize = Math.Clamp((int)(height * 0.028), 14, 64);
                FontSize = Math.Clamp((int)Math.Round(autoFontSize * Scale), 6, 200);
                LineHeight = Math.Max(FontSize + 4, Math.Max(20, height * 0.035) * Scale);
            }

            public int Width { get; }
            public int Height { get; }
            public bool ShowText { get; }
            public int Budget { get; }
            public int Used { get; private set; }
            public bool Truncated { get; private set; }
            public double Margin { get; }
            public double LineHeight { get; }
            public int FontSize { get; }
            public TextAnchorKind Anchor { get; }
            public double Scale { get; }

            /// <summary>本轮收集到的堆叠行数（供断言核对"锚点算的是不是真实行数"）</summary>
            public int StackCount => _stack.Count;

            /// <summary>文本项颜色：未配判定项 = green；判定项命中 NG 值 = red（§5.6）</summary>
            public string TextColor { get; set; } = OkColor;

            /// <summary>
            /// 本插件自己造的 HALCON 中间对象登记处（如抽稀后重生成的 XLD）。
            /// 上游给的对象**绝不进这里**（所有权在产出它的算子），只有"本插件 new 出来的"才登记，
            /// 由 <see cref="DisplayDrawResult.DisposeTemps"/> 在 Bake 之后统一释放。
            /// </summary>
            public List<HObject> Temp => _res.TempObjects;

            /// <summary>领一个元素预算名额。领不到就把"截断"标记立起来，由 Build 在末尾追加一行说明</summary>
            private bool Take()
            {
                if (Used >= Budget) { Truncated = true; return false; }
                Used++;
                return true;
            }

            public bool AddShape(BakeShapeKind kind, double[] p, string color, int lineWidth)
            {
                for (int i = 0; i < p.Length; i++)
                {
                    if (double.IsFinite(p[i])) continue;
                    Warn($"{kind} 的第 {i} 个参数非有限值（{p[i]}），已跳过该元素");
                    return false;
                }
                ClampShapeCoords(kind, p);
                if (!Take()) return false;
                _res.Plan.Shapes.Add(new BakeShape(kind, p, color, lineWidth));
                return true;
            }

            public bool AddObj(HObject obj, string color, int lineWidth, string draw)
            {
                if (!Take()) return false;
                _res.Plan.Objects.Add(new BakeObj(obj, color, lineWidth, draw));
                return true;
            }

            /// <summary>堆叠文本行（进文字块，位置由锚点决定，<see cref="EmitStack"/> 才落位）。带白底框：复杂背景上要读得清</summary>
            public bool AddTextLine(string text, string color)
            {
                if (!ShowText || string.IsNullOrEmpty(text)) return false;
                if (!Take()) return false;
                _stack.Add((text, color));
                return true;
            }

            /// <summary>
            /// 跟随图形的文字标签（几何项专用），**不带白底框、也不受九宫格锚点影响**：
            /// 标签就贴在圆心/线中点旁边，一块白框会把它正在标注的那个特征整个盖住，
            /// 而把它挪到角落就等于取消"这个数属于这条线"的对应关系。
            /// 字号/行距的缩放**照用**（用户要的是"整体看清楚了"，不是"只有角上的字变大"）。
            /// </summary>
            public bool AddTextAt(string text, double row, double col, string color)
            {
                if (!ShowText || string.IsNullOrEmpty(text)) return false;
                if (!double.IsFinite(row) || !double.IsFinite(col)) return false;
                if (!Take()) return false;
                _res.Plan.Texts.Add(new BakeText(text, row, col, color, FontSize, false));
                return true;
            }

            /// <summary>截断说明行：不占预算（否则"超上限"这件事本身就说不出来了）</summary>
            public void AddTextNote(string text)
            {
                if (!ShowText || string.IsNullOrEmpty(text)) return;
                _stack.Add((text, NoteColor));
            }

            /// <summary>
            /// 把收集到的堆叠行按九宫格锚点写进 plan。
            ///
            /// 垂直：整块高 = 行数 × 行距；上/中带分别是 Margin 与 (画布高−块高)/2，下带留一个 Margin。
            /// 夹到 ≥0 —— 行数多到块比画布还高时，宁可让它从顶部溢出（下面的行看不见），
            /// 也不能把首行推到负行号被裁掉（首行通常是判定项，最该看见）。
            /// 水平：<c>Col</c> 给的是**锚点边**（左边缘 / 中心 / 右边缘），具体含义由 <c>HAlign</c>
            /// 交给烘焙件用 get_string_extents 实测宽度换算——本文件拿不到窗口，量不了宽度。
            ///
            /// 副作用（可接受，写明）：堆叠文字现在统一排在几何标签**之后**入列，所以两处文字重叠时
            /// 堆叠块盖在标签上面。层序由类别决定（Shapes → Objects → Texts），堆叠块之间仍是原顺序。
            /// </summary>
            public void EmitStack()
            {
                if (_stack.Count == 0) return;

                int band = (int)Anchor / 3;     // 0 上 / 1 中 / 2 下（成员顺序承重，见 TextAnchorKind）
                int side = (int)Anchor % 3;     // 0 左 / 1 中 / 2 右
                double blockHeight = _stack.Count * LineHeight;

                double top = band switch
                {
                    1 => (Height - blockHeight) / 2.0,
                    2 => Height - Margin - blockHeight,
                    _ => Margin
                };
                top = Math.Max(0, top);

                double col = side switch
                {
                    1 => Width / 2.0,
                    2 => Width - Margin,
                    _ => Margin
                };
                var align = side switch { 1 => BakeHAlign.Center, 2 => BakeHAlign.Right, _ => BakeHAlign.Left };

                for (int i = 0; i < _stack.Count; i++)
                {
                    var (text, color) = _stack[i];
                    _res.Plan.Texts.Add(new BakeText(text, top + i * LineHeight, col, color, FontSize, true)
                    {
                        HAlign = align
                    });
                }
            }

            public void Info(string label, string value) =>
                _res.InfoRows.Add(new ImageInfoRow(label, value ?? ""));

            public void Warn(string message) => _res.Warnings.Add(message);

            private double ClampRow(double v) => Math.Clamp(v, 0, Height - 1);
            private double ClampCol(double v) => Math.Clamp(v, 0, Width - 1);

            /// <summary>
            /// 坐标钳进图幅（§5.7-2）。只钳**坐标分量**：半径/半轴/角度/十字尺寸不是坐标，
            /// 把它们钳进 [0, w-1] 会把"半径 3000 的大圆"改成"半径 w-1"，那是篡改数据不是保护。
            /// </summary>
            private void ClampShapeCoords(BakeShapeKind kind, double[] p)
            {
                switch (kind)
                {
                    case BakeShapeKind.Line:                       // [r1,c1,r2,c2]
                        p[0] = ClampRow(p[0]); p[2] = ClampRow(p[2]);
                        p[1] = ClampCol(p[1]); p[3] = ClampCol(p[3]);
                        break;
                    case BakeShapeKind.Circle:                     // [row,col,radius]
                    case BakeShapeKind.Ellipse:                    // [row,col,phiDeg,ra,rb]
                    case BakeShapeKind.Cross:                      // [row,col] / [row,col,size]
                        p[0] = ClampRow(p[0]);
                        if (p.Length > 1) p[1] = ClampCol(p[1]);
                        break;
                }
            }
        }
    }
}
